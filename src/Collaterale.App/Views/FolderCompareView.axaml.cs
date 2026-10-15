using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Collaterale.App.ViewModels;

namespace Collaterale.App.Views;

public partial class FolderCompareView : UserControl
{
    public FolderCompareView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => Collaterale.App.Controls.ShortcutBinder.Changed -= BindKeys;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is FolderCompareViewModel vm && TopLevel.GetTopLevel(this) is Window owner)
            {
                vm.Confirm = (title, message, lines) => ConfirmWindow.AskAsync(owner, title, message, lines);
            }
        };
        AttachedToVisualTree += (_, _) =>
        {
            Collaterale.App.Controls.ShortcutBinder.Changed += BindKeys;
            BindKeys();
            if (DataContext is FolderCompareViewModel vm && TopLevel.GetTopLevel(this) is Window owner)
            {
                vm.Confirm = (title, message, lines) => ConfirmWindow.AskAsync(owner, title, message, lines);
                vm.SyncRequested = () => OnSync(this, new RoutedEventArgs());
            }
        };
    }

    private void BindKeys()
    {
        if (Vm is not { } vm) { return; }
        var map = new Dictionary<string, (System.Windows.Input.ICommand, object?)>
        {
            ["folder.refresh"] = (vm.CompareCommand, null), ["folder.copyright"] = (vm.CopyToRightCommand, null),
            ["folder.copyleft"] = (vm.CopyToLeftCommand, null), ["folder.expand"] = (vm.ExpandAllCommand, null),
        };
        Collaterale.App.Controls.ShortcutBinder.Apply(this, vm.Prefs, map);
    }

    private Collaterale.Core.Support.IncrementalSearch? _typeAhead;

    /// <summary>Typing letters in the tree jumps to the next name that starts with them (the pause limit comes from the metrics preferences).</summary>
    private void OnTreeTextInput(object? sender, TextInputEventArgs e)
    {
        if (Vm is not { } vm || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]) || char.IsWhiteSpace(e.Text[0])) { return; }
        _typeAhead = new Collaterale.Core.Support.IncrementalSearch(vm.Prefs.Metrics.IncrementalSearchResetMs, () => DateTime.Now) is var fresh && _typeAhead is null ? fresh : _typeAhead;
        var names = vm.Rows.Select(r => r.Node.Name).ToList();
        if (names.Count == 0) { return; }
        var index = _typeAhead.Type(e.Text[0], names, Math.Max(Tree.SelectedIndex, -1));
        if (index >= 0 && index < names.Count)
        {
            Tree.SelectedIndex = index;
            Tree.ScrollIntoView(index);
        }

        e.Handled = true;
    }

    /// <summary>Fills the "Open with" menu from the preferences, for the item the context menu was opened on.</summary>
    private void OnOpenWithOpening(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || Vm is not { } vm) { return; }
        menu.Items.Clear();
        var apps = vm.Prefs.OpenWith.Where(Collaterale.Core.Integration.OpenWith.IsUsable).ToList();
        if (apps.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "(add applications in Options ▸ “Open with” applications)", IsEnabled = false });
            return;
        }

        foreach (var app in apps)
        {
            var item = new MenuItem { Header = app.Name };
            item.Click += (_, _) => OpenWith(app);
            menu.Items.Add(item);
        }
    }

    private void OpenWith(Collaterale.Core.Settings.OpenWithApp app)
    {
        if (Vm is not { } vm || Tree.SelectedItem is not FolderRow row || row.Node.IsDirectory) { return; }
        var (left, right) = (row.Node.Left is null ? null : Path.Combine(vm.LeftPath, row.Node.LeftRelativePath ?? row.Node.RelativePath), row.Node.Right is null ? null : Path.Combine(vm.RightPath, row.Node.RightRelativePath ?? row.Node.RelativePath));
        var (file, other) = left is not null ? (left, right) : (right!, null);
        if (Collaterale.Core.Integration.OpenWith.Start(app, file, other) is { } problem) { vm.Status = problem; }
    }

    private FolderCompareViewModel? Vm => DataContext as FolderCompareViewModel;

    private async Task<string?> PickAsync(string title)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return null;
        }

        var f = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return f.Count > 0 ? f[0].TryGetLocalPath() : null;
    }

    private async void OnBrowseLeft(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && await PickAsync("Left folder") is { } p)
        {
            vm.LeftPath = p;
            await TryCompare(vm);
        }
    }

    private async void OnBrowseRight(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && await PickAsync("Right folder") is { } p)
        {
            vm.RightPath = p;
            await TryCompare(vm);
        }
    }

    private static async Task TryCompare(FolderCompareViewModel vm)
    {
        if (Directory.Exists(vm.LeftPath) && Directory.Exists(vm.RightPath))
        {
            await vm.CompareAsync();
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => Vm?.SetSelection(Tree.SelectedItems?.OfType<FolderRow>() ?? []);

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Tree.SelectedItem is FolderRow row)
        {
            Vm?.Activate(row);
        }
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || Tree.SelectedItem is not FolderRow row)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                _ = PeekAsync();
                e.Handled = true;
                break;
            case Key.Enter:
                vm.Activate(row);
                e.Handled = true;
                break;
            case Key.Right when row.HasChildren && !row.IsExpanded:
            case Key.Left when row.HasChildren && row.IsExpanded:
                vm.Toggle(row);
                e.Handled = true;
                break;
        }
    }

    private async void OnSettings(object? sender, RoutedEventArgs e) => await OpenSettingsAsync();

    private async void OnFilters(object? sender, RoutedEventArgs e) => await OpenSettingsAsync(tab: 3);

    /// <summary>Shows the session settings; <paramref name="tab"/> is the tab to start on (3 = name filters).</summary>
    public async Task OpenSettingsAsync(int tab = 0)
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var settings = FolderSettingsViewModel.From(vm);
        var dialog = new FolderSettingsWindow(settings);
        if (dialog.FindControl<TabControl>("Tabs") is { } tabs) { tabs.SelectedIndex = tab; }
        if (await dialog.ShowDialog<bool>(owner))
        {
            await settings.ApplyAsync(vm, vm.AppSettings);
            if (dialog.SaveAsDefaults)
            {
                vm.AppSettings.FolderDefaults = settings.ToOptions() with { Alignments = [] };
                vm.AppSettings.FilterPresets = [.. settings.Presets];
                vm.RequestSaveSettings?.Invoke();
            }
        }
    }

    private void OnPresetsOpening(object? sender, EventArgs e)
    {
        if (Vm is not { } vm || sender is not MenuFlyout menu)
        {
            return;
        }

        menu.Items.Clear();
        foreach (var preset in vm.Presets)
        {
            var item = new MenuItem { Header = preset.Name };
            var captured = preset;
            item.Click += async (_, _) => await vm.ApplyPresetAsync(captured);
            menu.Items.Add(item);
        }

        if (vm.Presets.Count == 0) { menu.Items.Add(new MenuItem { Header = "No presets yet", IsEnabled = false }); }
        menu.Items.Add(new Separator());
        var save = new MenuItem { Header = "Save the current filters as a preset" };
        save.Click += (_, _) => vm.SaveCurrentAsPreset();
        menu.Items.Add(save);
    }

    private void OnOpenSelected(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && Tree.SelectedItem is FolderRow row) { vm.Activate(row); }
    }

    private async void OnCopyPaths(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) { await clipboard.SetTextAsync(vm.SelectedPaths()); }
    }

    private async void OnPeek(object? sender, RoutedEventArgs e) => await PeekAsync();

    private async Task PeekAsync()
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this) is not Window owner || !vm.HasSelection)
        {
            return;
        }

        await new PeekWindow(vm.PeekTitle, vm.Peek()).ShowDialog(owner);
    }

    private async void OnSync(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (!vm.SyncAllowed(out var reason)) { vm.Status = reason; return; }
        var dialog = new SyncWindow(vm);
        if (await dialog.ShowDialog<Collaterale.Core.Folders.SyncPlan?>(owner) is { } plan)
        {
            await vm.ExecuteAsync(plan, "Synchronize", askConfirmation: false);
        }
    }
}
