using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Collaterale.App.Controls;
using Collaterale.App.ViewModels;

namespace Collaterale.App.Views;

public partial class TextCompareView : UserControl
{
    private TextCompareViewModel? _vm;
    private bool _syncing;
    private readonly Avalonia.Threading.DispatcherTimer _editTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly HashSet<Side> _pending = [];
    private (Side Side, int Line, int Column)? _restoreCaret;

    public TextCompareView()
    {
        InitializeComponent();
        LeftPane.IsReadOnly = false;
        RightPane.IsReadOnly = false;
        Gutter.Attach(LeftPane.TextArea.TextView);

        // typing: commit after a short pause (or when leaving the pane) so the comparison catches up without disturbing the typist
        LeftPane.FoldClicked += row => _vm?.ExpandFold(row);
        RightPane.FoldClicked += row => _vm?.ExpandFold(row);
        LeftPane.UserEdited += (_, _) => Touched(Side.Left);
        RightPane.UserEdited += (_, _) => Touched(Side.Right);
        LeftPane.LostFocus += (_, _) => FlushEdits();
        RightPane.LostFocus += (_, _) => FlushEdits();
        _editTimer.Tick += (_, _) => FlushEdits();

        // The editors' own undo history is switched off (rows are rebuilt after every edit), so Ctrl+Z / Ctrl+Y must reach the
        // whole-file undo before the editor swallows them.
        foreach (var pane in new[] { LeftPane, RightPane })
        {
            pane.AddHandler(KeyDownEvent, OnPaneKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        LeftPane.TextArea.TextView.ScrollOffsetChanged += (_, _) => Follow(LeftPane, RightPane);
        RightPane.TextArea.TextView.ScrollOffsetChanged += (_, _) => Follow(RightPane, LeftPane);
        Gutter.CopyRequested += (group, from) =>
        {
            if (_pending.Count > 0)
            {
                FlushEdits();   // the layout may have changed under the click: apply the typing first and let the user click again
                return;
            }

            _vm?.CopyGroup(group, from);
        };
        Gutter.GroupSelected += g => { if (_vm is not null) { _vm.CurrentGroup = g; } };
        LeftPane.GotFocus += (_, _) => DimOther(RightPane, LeftPane);
        RightPane.GotFocus += (_, _) => DimOther(LeftPane, RightPane);
        foreach (var (pane, side) in new[] { (LeftPane, Side.Left), (RightPane, Side.Right) })
        {
            pane.TextArea.Caret.PositionChanged += (_, _) => OnCaretMoved(pane, side);
        }

        Map.ScrollRequested += fraction =>
        {
            var tv = LeftPane.TextArea.TextView;
            LeftPane.ScrollToVerticalOffset(Math.Max(0, fraction * tv.DocumentHeight - tv.Bounds.Height / 2));
        };

        DataContextChanged += (_, _) => Attach(DataContext as TextCompareViewModel);
        AttachedToVisualTree += (_, _) => ShortcutBinder.Changed += BindKeys;
        DetachedFromVisualTree += (_, _) => ShortcutBinder.Changed -= BindKeys;
    }

    private void Attach(TextCompareViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.DisplayChanged -= OnDisplayChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.FindHitSelected -= OnFindHit;
            _vm.ItemFocusRequested -= OnItemFocus;
            _vm.DiskChanged -= OnDiskChanged;
            _vm.FindMatchesChanged -= OnFindMatchesChanged;
        }

        _vm = vm;
        if (vm is null)
        {
            return;
        }

        vm.DisplayChanged += OnDisplayChanged;
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.FlushEdits = FlushEdits;
        vm.GoToLineRequested = GoToLineAsync;
        vm.CurrentWordProvider = () => (RightPane.IsKeyboardFocusWithin ? RightPane : LeftPane).WordAtCaret();
        vm.FindHitSelected += OnFindHit;
        vm.ItemFocusRequested += OnItemFocus;
        vm.DiskChanged += OnDiskChanged;
        BindKeys();
        vm.FindMatchesChanged += OnFindMatchesChanged;
        ApplyLayout();
        LeftPane.ShowWhitespace = RightPane.ShowWhitespace = vm.ShowWhitespace;
        LeftPane.WordWrap = RightPane.WordWrap = vm.WordWrap;
        OnDisplayChanged(vm, EventArgs.Empty);
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TextCompareViewModel.CurrentGroup):
                ShowCurrentGroup(scroll: true);
                break;
            case nameof(TextCompareViewModel.ShowWhitespace):
                LeftPane.ShowWhitespace = RightPane.ShowWhitespace = _vm!.ShowWhitespace;
                break;
            case nameof(TextCompareViewModel.WordWrap):
                LeftPane.WordWrap = RightPane.WordWrap = _vm!.WordWrap;
                break;
            case nameof(TextCompareViewModel.OverUnder):
                ApplyLayout();
                break;
            case nameof(TextCompareViewModel.FindVisible) when _vm!.FindVisible:
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { FindBox.Focus(); }, Avalonia.Threading.DispatcherPriority.Input);
                break;
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        LeftPane.ApplyPreferences(_vm.Prefs);
        RightPane.ApplyPreferences(_vm.Prefs);
        var offset = LeftPane.VerticalOffset; // keep the reading position across re-comparisons
        (LeftPane.IsReadOnly, RightPane.IsReadOnly) = (_vm.CollapseUnchanged || _vm.LeftLocked, _vm.CollapseUnchanged || _vm.RightLocked);
        LeftPane.SetSyntaxFor(_vm.Left.Path);
        RightPane.SetSyntaxFor(_vm.Right.Path);
        LeftPane.SetContent(_vm.LeftText, _vm.LeftRows);
        RightPane.SetContent(_vm.RightText, _vm.RightRows);
        Gutter.Groups = _vm.DisplayGroups;
        Gutter.Rows = _vm.DisplayRows;
        Map.Rows = _vm.DisplayRows;
        // a collapsed view hides lines the typing logic would need, so it is read-only; expand to edit
        (LeftPane.IsReadOnly, RightPane.IsReadOnly) = (_vm.CollapseUnchanged || _vm.LeftLocked, _vm.CollapseUnchanged || _vm.RightLocked);
        ShowCurrentGroup(scroll: false);
        LeftPane.ScrollToVerticalOffset(offset);
        RightPane.ScrollToVerticalOffset(offset);
        if (_restoreCaret is { } c)
        {
            (c.Side == Side.Left ? LeftPane : RightPane).RestoreCaret(c.Line, c.Column);
            _restoreCaret = null;
        }

        UpdateMap();
        OnFindMatchesChanged(this, EventArgs.Empty);
    }

    /// <summary>The shortcuts of the commands, from the options (or the defaults).</summary>
    private void BindKeys()
    {
        if (_vm is null) { return; }
        var map = new Dictionary<string, (System.Windows.Input.ICommand, object?)>
        {
            ["text.next"] = (_vm.NextDifferenceCommand, null), ["text.prev"] = (_vm.PreviousDifferenceCommand, null),
            ["text.next2"] = (_vm.NextDifferenceCommand, null), ["text.prev2"] = (_vm.PreviousDifferenceCommand, null),
            ["text.nextfile"] = (_vm.NextFileCommand, null), ["text.prevfile"] = (_vm.PreviousFileCommand, null),
            ["text.nextitem"] = (_vm.NextItemCommand, null), ["text.previtem"] = (_vm.PreviousItemCommand, null),
            ["text.copyright"] = (_vm.CopyToRightCommand, null), ["text.copyleft"] = (_vm.CopyToLeftCommand, null),
            ["text.undo"] = (_vm.UndoCommand, null), ["text.redo"] = (_vm.RedoCommand, null), ["text.reload"] = (_vm.ReloadCommand, null),
            ["text.goto"] = (_vm.GoToLineCommand, null), ["text.find"] = (_vm.ShowFindCommand, false), ["text.replace"] = (_vm.ShowReplaceCommand, null),
            ["text.findnext"] = (_vm.FindNextCommand, null), ["text.findprev"] = (_vm.FindPreviousCommand, null), ["text.swap"] = (_vm.SwapCommand, null),
        };
        ShortcutBinder.Apply(this, _vm.Prefs, map, [("Escape", _vm.CloseFindCommand)]);
    }

    /// <summary>The preference "dim the inactive pane": the pane that does not have the cursor is drawn paler.</summary>
    private void DimOther(DiffPane other, DiffPane active)
    {
        var dim = _vm?.Prefs.Editor.DimInactivePane ?? false;
        other.Opacity = dim ? 0.72 : 1;
        active.Opacity = 1;
    }

    // ───────────── layout, find, details ─────────────

    /// <summary>Side by side (headers above, the gutter and map between and beside) or stacked (left file above the right one).</summary>
    private void ApplyLayout()
    {
        var stacked = _vm?.OverUnder ?? false;
        void Put(Control c, int row, int col, int rowSpan = 1)
        {
            Grid.SetRow(c, row); Grid.SetColumn(c, col); Grid.SetRowSpan(c, rowSpan);
        }

        if (stacked)
        {
            Layout.RowDefinitions[0].Height = GridLength.Auto;
            Layout.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            Layout.RowDefinitions[2].Height = GridLength.Auto;
            Layout.RowDefinitions[3].Height = new GridLength(1, GridUnitType.Star);
            Layout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            Layout.ColumnDefinitions[1].Width = new GridLength(0);
            Layout.ColumnDefinitions[2].Width = new GridLength(0);
            Put(HeaderLeft, 0, 0); Put(LeftBox, 1, 0); Put(HeaderRight, 2, 0); Put(RightBox, 3, 0);
            Put(Map, 1, 3, 3);
            Gutter.IsVisible = false;
        }
        else
        {
            Layout.RowDefinitions[0].Height = GridLength.Auto;
            Layout.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            Layout.RowDefinitions[2].Height = new GridLength(0);
            Layout.RowDefinitions[3].Height = new GridLength(0);
            Layout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            Layout.ColumnDefinitions[1].Width = new GridLength(56);
            Layout.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            Put(HeaderLeft, 0, 0); Put(HeaderRight, 0, 2); Put(LeftBox, 1, 0, 3); Put(RightBox, 1, 2, 3);
            Put(Map, 1, 3, 3);
            Put(Gutter, 1, 1, 3);
            Gutter.IsVisible = true;
        }
    }

    private void OnCaretMoved(DiffPane pane, Side side)
    {
        if (_vm is null) { return; }
        var (line, start, end) = pane.CaretSpan();
        var real = _vm.LineOfRow(side, line - 1);
        _vm.Caret = (side, real, start, end);
        _vm.CaretMoved(side, line - 1);
    }

    private async void OnDiskChanged(Side side)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not Window owner) { return; }
        var name = System.IO.Path.GetFileName((side == Side.Left ? _vm.Left.Path : _vm.Right.Path) ?? "");
        var edited = side == Side.Left ? _vm.Left.Dirty : _vm.Right.Dirty;
        var question = edited ? $"“{name}” changed on disk, and you have unsaved changes in it. Reload and lose them?" : $"“{name}” changed on disk. Reload?";
        if (await ConfirmWindow.AskAsync(owner, "File changed on disk", question, [], "Reload")) { _vm.ReloadCommand.Execute(null); }
    }

    /// <summary>Moves the cursor onto a changed word (or line) and brings it into view.</summary>
    private void OnItemFocus(TextCompareViewModel.DifferenceItem item)
    {
        if (_vm is null) { return; }
        var row = _vm.DisplayRowOfComparisonRow(item.Row);
        if (row < 0) { return; }
        var pane = item.Side == Side.Left ? LeftPane : RightPane;
        pane.SelectSpan(row + 1, item.Start, Math.Max(item.Length, 0));
        var other = item.Side == Side.Left ? RightPane : LeftPane;
        other.ScrollToVerticalOffset(pane.VerticalOffset);
    }

    private void OnFindHit(FindHit hit)
    {
        if (_vm is null) { return; }
        var row = _vm.RowOfLine(hit.Side, hit.Line);
        if (row < 0) { return; }
        var pane = hit.Side == Side.Left ? LeftPane : RightPane;
        var other = hit.Side == Side.Left ? RightPane : LeftPane;
        pane.SelectSpan(row + 1, hit.Start, hit.Length);
        if (other.Document is { } doc && row < doc.LineCount)
        {
            // the other pane is level with this one: bring it to the same height
            other.ScrollToVerticalOffset(pane.VerticalOffset);
        }
    }

    private void OnFindMatchesChanged(object? sender, EventArgs e)
    {
        if (_vm is null) { return; }
        List<(int, int, int)> Marks(Side side) => [.. _vm.FindMatches.Where(h => h.Side == side).Select(h => (Row: _vm.RowOfLine(side, h.Line), h)).Where(t => t.Row >= 0).Select(t => (t.Row + 1, t.h.Start, t.h.Length))];
        LeftPane.SetSearchMarks(Marks(Side.Left));
        RightPane.SetSearchMarks(Marks(Side.Right));
    }

    private void OnFindKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (_vm is null) { return; }
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) ? _vm.FindPreviousCommand : _vm.FindNextCommand).Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Escape)
        {
            _vm.CloseFindCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnDetailKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter && sender is TextBox box) { CommitDetail(box); e.Handled = true; }
    }

    private void OnDetailCommitted(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) { CommitDetail(box); }
    }

    /// <summary>Applies what was typed in the details panel to its line.</summary>
    private void CommitDetail(TextBox box)
    {
        if (_vm is null) { return; }
        var left = (box.Tag as string) == "left";
        _vm.SetLine(left ? Side.Left : Side.Right, left ? _vm.LeftCaretLine : _vm.RightCaretLine, box.Text ?? "");
    }

    private void OnPaneKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (_vm is null || !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            return;
        }

        var redo = e.Key == Avalonia.Input.Key.Y || (e.Key == Avalonia.Input.Key.Z && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift));
        var undo = e.Key == Avalonia.Input.Key.Z && !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift);
        if (undo || redo)
        {
            (undo ? _vm.UndoCommand : _vm.RedoCommand).Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Ctrl+G: asks for a line of the pane that has focus (the left one by default) and scrolls both panes to it.</summary>
    private async Task GoToLineAsync()
    {
        if (_vm is null || _vm.Comparison is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var side = RightPane.IsKeyboardFocusWithin ? Side.Right : Side.Left;
        var count = side == Side.Left ? _vm.Comparison.LeftLines.Count : _vm.Comparison.RightLines.Count;
        var line = await new GoToLineWindow(side == Side.Left ? "left" : "right", count).ShowDialog<int?>(owner);
        if (line is { } n)
        {
            ShowLine(side, n - 1);
        }
    }

    /// <summary>Scrolls both panes to a line of one side (opening a collapsed stretch if needed) and puts the caret there.</summary>
    public void ShowLine(Side side, int line)
    {
        if (_vm is null) { return; }
        var row = _vm.RevealLine(side, line);
        if (row < 0) { return; }
        var pane = side == Side.Left ? LeftPane : RightPane;
        if (pane.Document is { } doc && row < doc.LineCount)
        {
            var tv = pane.TextArea.TextView;
            LeftPane.ScrollToVerticalOffset(Math.Max(0, tv.GetVisualTopByDocumentLine(row + 1) - tv.Bounds.Height / 3));
            pane.TextArea.Caret.Line = row + 1;
            pane.TextArea.Caret.Column = 1;
            pane.TextArea.Focus();
        }
    }

    private void Touched(Side side)
    {
        _pending.Add(side);
        _editTimer.Stop();
        _editTimer.Start();
    }

    /// <summary>Turns whatever was typed into the model and re-compares. Safe to call at any time.</summary>
    public void FlushEdits()
    {
        _editTimer.Stop();
        if (_pending.Count == 0 || _vm is null)
        {
            return;
        }

        var sides = _pending.ToList();
        _pending.Clear();

        // read every edited pane before anything is rebuilt: the first rebuild would replace the other pane's document
        List<string>? left = null, right = null;
        foreach (var side in sides)
        {
            var pane = side == Side.Left ? LeftPane : RightPane;
            var lines = pane.ReconstructLines();
            var current = side == Side.Left ? _vm.Left.Lines : _vm.Right.Lines;
            if (lines.SequenceEqual(current))
            {
                continue;
            }

            if (side == Side.Left) { left = lines; } else { right = lines; }
            var (line, col) = pane.GetCaretModelPosition();
            _restoreCaret = (side, line, col);
        }

        _vm.ApplyEdits(left, right);
    }

    private void ShowCurrentGroup(bool scroll)
    {
        if (_vm is null)
        {
            return;
        }

        LeftPane.CurrentGroup = _vm.CurrentGroup;
        RightPane.CurrentGroup = _vm.CurrentGroup;
        Gutter.CurrentGroup = _vm.CurrentGroup;
        if (scroll && _vm.CurrentGroup >= 0 && LeftPane.Document is { } doc)
        {
            var line = Math.Min(_vm.RowOfGroup(_vm.CurrentGroup) + 1, doc.LineCount);
            var tv = LeftPane.TextArea.TextView;
            var y = tv.GetVisualTopByDocumentLine(line);
            if (y < tv.VerticalOffset + 20 || y > tv.VerticalOffset + tv.Bounds.Height - 80)
            {
                LeftPane.ScrollToVerticalOffset(Math.Max(0, y - tv.Bounds.Height / 3));
            }
        }
    }

    /// <summary>Both panes always have the same number of lines, so mirroring the vertical offset keeps them aligned.</summary>
    private void Follow(DiffPane from, DiffPane to)
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
            if (Math.Abs(to.VerticalOffset - from.VerticalOffset) > 0.5)
            {
                to.FollowOffset(from.VerticalOffset);
            }

            UpdateMap();
            Gutter.InvalidateVisual();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void UpdateMap()
    {
        var tv = LeftPane.TextArea.TextView;
        var height = Math.Max(tv.DocumentHeight, 1);
        Map.ViewportStart = tv.VerticalOffset / height;
        Map.ViewportSize = Math.Min(1, tv.Bounds.Height / height);
    }

    private async void OnOpenLeft(object? sender, RoutedEventArgs e) => await PickAsync(Side.Left);

    private async void OnOpenRight(object? sender, RoutedEventArgs e) => await PickAsync(Side.Right);

    private async Task PickAsync(Side side)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = side == Side.Left ? "Open left file" : "Open right file", AllowMultiple = false });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        var (l, r) = side == Side.Left ? (path, _vm.Right.Path) : (_vm.Left.Path, path);
        if (l is not null && r is not null)
        {
            _vm.Open(l, r);
        }
        else if (side == Side.Left)
        {
            _vm.OpenText(path, TextFileLines(path), _vm.Right.Path, _vm.Right.Lines);
        }
        else
        {
            _vm.OpenText(_vm.Left.Path, _vm.Left.Lines, path, TextFileLines(path));
        }
    }

    private static IEnumerable<string> TextFileLines(string path) => Collaterale.Core.Text.TextFile.Load(path).Lines;
}
