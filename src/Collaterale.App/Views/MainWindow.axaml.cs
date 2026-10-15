using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Collaterale.App.ViewModels;
using Collaterale.Core.Cli;

namespace Collaterale.App.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;

    public MainWindow() : this(new MainWindowViewModel(), Environment.GetCommandLineArgs().Skip(1).ToArray())
    {
    }

    public MainWindow(MainWindowViewModel vm, IReadOnlyList<string>? args = null)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        Closed += (_, _) => Program.ExitCode = vm.ExitCode;
        Closing += (_, _) => { if (!vm.Standalone) { vm.SaveOnExit(); } };
        vm.CloseWindowRequested = Close;
        vm.NewWindowRequested = tab =>
        {
            var window = new MainWindow(new MainWindowViewModel(vm.Store, vm.Settings, tab), []);
            window.Show();
        };
        ApplyHints(vm);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.ShowTabStrip)) { ApplyHints(vm); }
        };
        KeyDown += OnWindowKeyDown;
        Activated += async (_, _) =>
        {
            (vm.SelectedTab?.Content as TextCompareViewModel)?.CheckDisk();
            try { vm.CaptureClipboard(await ReadClipboardAsync()); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException) { /* some systems refuse clipboard access in the background */ }
        };
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);

        if (args is { Count: > 0 })
        {
            Opened += (_, _) => Start(LaunchParser.Parse(args));
        }
        else if (!vm.Standalone)
        {
            Opened += (_, _) => vm.StartupWorkspace();
        }

        if (!vm.Standalone) { Opened += async (_, _) => await vm.CheckForUpdatesAsync(); }
    }

    /// <summary>The hint delays of the metrics preferences, or no hints at all.</summary>
    private void ApplyHints(MainWindowViewModel vm)
    {
        var p = vm.Settings.Prefs;
        ToolTip.SetShowDelay(this, p.Interface.ShowHints ? Math.Max(0, p.Metrics.HintAppearMs) : int.MaxValue / 2);
    }

    /// <summary>Esc closes the file view in front when the preferences say so (folder views and the Home view stay).</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_vm.Settings.Prefs.Interface.EscClosesFileViews || e.Handled) { return; }
        if (_vm.SelectedTab is { Closable: true } tab && tab.Content is not FolderCompareViewModel)
        {
            _vm.CloseTabCommand.Execute(tab);
            e.Handled = true;
        }
    }

    // ───────────── menus ─────────────

    private async void OnOptions(object? sender, RoutedEventArgs e)
    {
        var options = new OptionsViewModel(_vm.Settings);
        if (await new OptionsWindow(options).ShowDialog<bool>(this))
        {
            options.ApplyTo(_vm.Settings);
            ApplyLoginStart(_vm.Settings.Prefs.Startup.RunClipboardCompareOnLogin);
            _vm.ApplyPreferences();
            _vm.Persist();
            ApplyHints(_vm);
            Collaterale.App.Controls.ShortcutBinder.Refresh();
        }
    }

    /// <summary>Starts (or stops starting) the clipboard compare when the user logs in.</summary>
    private void ApplyLoginStart(bool enabled)
    {
        try
        {
            Collaterale.Core.Integration.AutoStart.Set(Collaterale.Core.Integration.AutoStart.Current, enabled, Environment.ProcessPath ?? "collaterale", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm.Message = "The start-at-login entry could not be written: " + ex.Message;
        }
    }

    private async void OnSaveSession(object? sender, RoutedEventArgs e)
    {
        if (await PromptWindow.AskAsync(this, "Save session", "Name this comparison so it can be opened again from the Home view or with collaterale \"name\".", _vm.SelectedTab?.Content.Title ?? "", null, "Save") is { } name)
        {
            _vm.SaveSession(name);
        }
    }

    private async void OnSaveWorkspace(object? sender, RoutedEventArgs e)
    {
        if (await PromptWindow.AskAsync(this, "Save workspace", "Name the set of sessions that are open now.", "", null, "Save") is { } name)
        {
            _vm.SaveWorkspace(name);
        }
    }

    private async void OnOpenSession(object? sender, RoutedEventArgs e)
    {
        var names = _vm.Settings.AllSessions().Select(t => t.Session.Name).ToList();
        if (names.Count == 0) { _vm.Message = "There are no saved sessions yet."; return; }
        if (await PromptWindow.AskAsync(this, "Open session", "Choose a saved session.", "", names, "Open") is { } name) { _vm.OpenNamed(name); }
    }

    private async void OnOpenWorkspace(object? sender, RoutedEventArgs e)
    {
        var names = _vm.Settings.Workspaces.Select(w => w.Name).ToList();
        if (names.Count == 0) { _vm.Message = "There are no saved workspaces yet."; return; }
        if (await PromptWindow.AskAsync(this, "Load workspace", "Choose a saved workspace.", "", names, "Load") is { } name) { _vm.OpenNamed(name); }
    }

    private async void OnRunScript(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Run a script", AllowMultiple = false });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) { _vm.RunScript(path); }
    }

    private async void OnExportSettings(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export settings", SuggestedFileName = "collaterale-settings.cpkg", DefaultExtension = "cpkg" });
        if (file?.TryGetLocalPath() is { } path) { _vm.ExportPackage(path); }
    }

    private async void OnImportSettings(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import settings", AllowMultiple = false });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) { _vm.ImportPackage(path); }
    }

    private async void OnExportSessions(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export sessions", SuggestedFileName = "collaterale-sessions.json", DefaultExtension = "json" });
        if (file?.TryGetLocalPath() is { } path) { _vm.ExportSessions(path); }
    }

    private async Task<string?> ReadClipboardAsync()
    {
        if (Clipboard is not { } clipboard) { return null; }
        using var data = await clipboard.TryGetDataAsync();
        return data is null ? null : await data.TryGetTextAsync();
    }

    private async void OnClipboardCompare(object? sender, RoutedEventArgs e)
    {
        try { _vm.CaptureClipboard(await ReadClipboardAsync()); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException) { /* keep what is already known */ }
        _vm.OpenClipboardCompare();
    }

    private async void OnClipboardFile(object? sender, RoutedEventArgs e)
    {
        var text = await ReadClipboardAsync();
        if (string.IsNullOrEmpty(text)) { _vm.Message = "The clipboard holds no text."; return; }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Compare the clipboard with…", AllowMultiple = false });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) { _vm.OpenClipboardWithFile(text, path); }
    }

    private async void OnAbout(object? sender, RoutedEventArgs e)
        => await ConfirmWindow.AskAsync(this, "About Collaterale", $"{AppInfo.Name} {AppInfo.Version}\nCompare and merge files and folders.", [], "OK");

    private async void Start(LaunchRequest req)
    {
        // "quick compare dialog": a one-line answer before (or instead of) the full view, for a pair of files given on the command line
        var q = _vm.Settings.Prefs.Startup;
        if (q.QuickCompareDialog && req.Kind is LaunchKind.TextCompare or LaunchKind.HexCompare or LaunchKind.TableCompare or LaunchKind.ImageCompare
            && req.Left is not null && req.Right is not null && req.Options.QuickCompare is null)
        {
            var probe = req with { Options = new LaunchOptions { QuickCompare = q.QuickCompare == Collaterale.Core.Settings.QuickCompareKind.Binary ? "binary" : "" } };
            var code = Collaterale.Core.Cli.CliHeadless.Run(probe, TextWriter.Null, TextWriter.Null);
            if (Collaterale.Core.Cli.CliHeadless.ShowQuickDialog(code, q.OpenViewIfDifferent))
            {
                var open = await ConfirmWindow.AskAsync(this, "Quick compare", Collaterale.Core.Cli.CliHeadless.Describe(code).Text + $"\n\n{System.IO.Path.GetFileName(req.Left)}  ↔  {System.IO.Path.GetFileName(req.Right)}", [], "Open the view");
                if (!open) { return; }
            }
        }

        _vm.Launch(req);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths is not null)
        {
            _vm.Drop(paths);
        }
    }
}
