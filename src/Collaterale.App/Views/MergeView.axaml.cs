using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Collaterale.App.Controls;
using Collaterale.App.ViewModels;

namespace Collaterale.App.Views;

public partial class MergeView : UserControl
{
    private MergeViewModel? _vm;
    private bool _syncing;

    public MergeView()
    {
        InitializeComponent();
        ResultPane.IsReadOnly = false;

        foreach (var (from, others) in new[] { (LeftPane, new[] { BasePane, RightPane }), (BasePane, new[] { LeftPane, RightPane }), (RightPane, new[] { LeftPane, BasePane }) })
        {
            from.TextArea.TextView.ScrollOffsetChanged += (_, _) => Follow(from, others);
        }

        ResultPane.UserEdited += (_, _) =>
        {
            if (_vm is not null && !_vm.ResultEdited)
            {
                ResultPane.ClearRows();
                _vm.NotifyResultEdited(ResultPane.Document.Text);
            }
            else if (_vm is not null)
            {
                _vm.NotifyResultEdited(ResultPane.Document.Text);
            }
        };

        LeftPane.FoldClicked += row => _vm?.ExpandRegion(row);
        BasePane.FoldClicked += row => _vm?.ExpandRegion(row);
        RightPane.FoldClicked += row => _vm?.ExpandRegion(row);
        ResultPane.InstallMergeActions(r => _vm?.KindOf(r), r => _vm?.IsResolved(r) ?? false, (r, c) => _vm?.TakeRegion(r, c));

        DataContextChanged += (_, _) => Attach(DataContext as MergeViewModel);
        AttachedToVisualTree += (_, _) => { RestoreSplit(); ShortcutBinder.Changed += BindKeys; };
        DetachedFromVisualTree += (_, _) => { RememberSplit(); ShortcutBinder.Changed -= BindKeys; };
    }

    private void Attach(MergeViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.DisplayChanged -= OnDisplayChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = vm;
        if (vm is null)
        {
            return;
        }

        vm.DisplayChanged += OnDisplayChanged;
        vm.PropertyChanged += OnVmPropertyChanged;
        BindKeys();
        ApplyBaseVisibility();
        OnDisplayChanged(vm, EventArgs.Empty);
    }

    private void BindKeys()
    {
        if (_vm is null) { return; }
        var map = new Dictionary<string, (System.Windows.Input.ICommand, object?)>
        {
            ["merge.next"] = (_vm.NextChangeCommand, null), ["merge.prev"] = (_vm.PreviousChangeCommand, null), ["merge.nextconflict"] = (_vm.NextConflictCommand, null),
            ["merge.takeleft"] = (_vm.TakeLeftCommand, null), ["merge.takeright"] = (_vm.TakeRightCommand, null), ["merge.takecenter"] = (_vm.TakeBaseCommand, null),
            ["merge.takeboth"] = (_vm.TakeBothCommand, null), ["merge.save"] = (_vm.SaveCommand, null),
        };
        ShortcutBinder.Apply(this, _vm.Prefs, map);
    }

    /// <summary>"Sticky splitter": the share of the window the three input panes take is remembered between sessions.</summary>
    private void RestoreSplit()
    {
        if (_vm is null || !_vm.Prefs.Interface.StickySplitter) { return; }
        var share = Math.Clamp(_vm.Prefs.Interface.MergeTopShare, 0.15, 0.85);
        MergeGrid.RowDefinitions[0].Height = new GridLength(share, GridUnitType.Star);
        MergeGrid.RowDefinitions[2].Height = new GridLength(1 - share, GridUnitType.Star);
    }

    private void RememberSplit()
    {
        if (_vm is null || !_vm.Prefs.Interface.StickySplitter) { return; }
        var (top, bottom) = (MergeGrid.RowDefinitions[0].ActualHeight, MergeGrid.RowDefinitions[2].ActualHeight);
        if (top + bottom > 50) { _vm.Prefs.Interface.MergeTopShare = top / (top + bottom); }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MergeViewModel.CurrentChange):
                ShowCurrent(scroll: true);
                break;
            case nameof(MergeViewModel.ShowBase):
                ApplyBaseVisibility();
                break;
        }
    }

    private void ApplyBaseVisibility()
    {
        var show = _vm?.ShowBase ?? true;
        BaseBox.IsVisible = show;
        TopGrid.ColumnDefinitions[1].Width = show ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        TopGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        TopGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var offset = LeftPane.VerticalOffset;
        foreach (var pane in new[] { LeftPane, BasePane, RightPane, ResultPane }) { pane.ApplyPreferences(_vm.Prefs); }
        var editor = _vm.Prefs.Editor;
        foreach (var pane in new[] { LeftPane, BasePane, RightPane })
        {
            if (editor.MergeInputFont.Length > 0) { pane.FontFamily = new Avalonia.Media.FontFamily(editor.MergeInputFont); pane.FontSize = Math.Max(8, editor.MergeInputFontSize); }
        }

        LeftPane.SetSyntaxFor(_vm.Left.Path);
        BasePane.SetSyntaxFor(_vm.Left.Path);
        RightPane.SetSyntaxFor(_vm.Left.Path);
        ResultPane.SetSyntaxFor(_vm.OutputPath ?? _vm.Left.Path);
        LeftPane.SetContent(_vm.LeftText, _vm.LeftRows);
        BasePane.SetContent(_vm.BaseText, _vm.BaseRows);
        RightPane.SetContent(_vm.RightText, _vm.RightRows);
        ResultPane.SetContent(_vm.ResultText, _vm.ResultRows);
        ShowCurrent(scroll: false);
        foreach (var p in new[] { LeftPane, BasePane, RightPane })
        {
            p.ScrollToVerticalOffset(offset);
        }
    }

    private void ShowCurrent(bool scroll)
    {
        if (_vm is null)
        {
            return;
        }

        var region = _vm.CurrentRegion;
        foreach (var p in new[] { LeftPane, BasePane, RightPane, ResultPane })
        {
            p.CurrentGroup = region;
        }

        if (!scroll || region < 0)
        {
            return;
        }

        ScrollTo(LeftPane, _vm.RegionRowStart.ElementAtOrDefault(region) + 1);
        if (!_vm.ResultEdited)
        {
            ScrollTo(ResultPane, _vm.ResultStart.ElementAtOrDefault(region) + 1);
        }
    }

    private static void ScrollTo(DiffPane pane, int line)
    {
        if (pane.Document is not { } doc || doc.LineCount == 0)
        {
            return;
        }

        var tv = pane.TextArea.TextView;
        var y = tv.GetVisualTopByDocumentLine(Math.Min(Math.Max(line, 1), doc.LineCount));
        if (y < tv.VerticalOffset + 10 || y > tv.VerticalOffset + tv.Bounds.Height - 60)
        {
            pane.ScrollToVerticalOffset(Math.Max(0, y - tv.Bounds.Height / 3));
        }
    }

    /// <summary>The three aligned panes have identical line counts, so mirroring the offset keeps them level.</summary>
    private void Follow(DiffPane from, DiffPane[] others)
    {
        if (_syncing)
        {
            return;
        }

        if (from.ConsumeEcho())
        {
            return;
        }

        _syncing = true;
        try
        {
            foreach (var o in others)
            {
                if (Math.Abs(o.VerticalOffset - from.VerticalOffset) > 0.5)
                {
                    o.FollowOffset(from.VerticalOffset);
                }
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnTargetLeft(object? sender, RoutedEventArgs e) { if (_vm is not null) { _vm.Target = MergeTarget.Left; } }

    private void OnTargetRight(object? sender, RoutedEventArgs e) { if (_vm is not null) { _vm.Target = MergeTarget.Right; } }

    private void OnTargetOther(object? sender, RoutedEventArgs e) { if (_vm is not null) { _vm.Target = MergeTarget.Other; } }

    private async Task<string?> PickOpenAsync(string title)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) { return null; }
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async void OnOpenLeft(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null && await PickOpenAsync("Left version") is { } p) { _vm.SetSource(MergeRole.Left, p); }
    }

    private async void OnOpenRight(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null && await PickOpenAsync("Right version") is { } p) { _vm.SetSource(MergeRole.Right, p); }
    }

    private async void OnOpenBase(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null && await PickOpenAsync("Base (common ancestor)") is { } p) { _vm.SetSource(MergeRole.Base, p); }
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        if (_vm.NeedsOutputPath && TopLevel.GetTopLevel(this) is { } top)
        {
            var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions { Title = "Save merged result as" });
            if (file?.TryGetLocalPath() is not { } path) { return; }
            _vm.OutputPath = path;
        }

        if (_vm.TrySave(allowMarkers: false))
        {
            return;
        }

        if (_vm.UnresolvedCount > 0 && TopLevel.GetTopLevel(this) is Window owner
            && await ConfirmWindow.AskAsync(owner, "Unresolved conflicts",
                $"{_vm.UnresolvedCount} conflict(s) are unresolved. Save anyway with conflict markers in the file?", [], "Save with markers"))
        {
            _vm.TrySave(allowMarkers: true);
        }
    }
}
