using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Styling;
using Collaterale.Core.Cli;
using Collaterale.Core.Folders;
using Collaterale.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collaterale.App.ViewModels;

/// <summary>Anything that can live in a tab: it names itself and tells the shell when its name changes.</summary>
public interface ISessionContent : INotifyPropertyChanged
{
    string Title { get; }
}

public sealed partial class SessionTab(ISessionContent content, string glyph, bool closable) : ObservableObject
{
    public ISessionContent Content { get; } = content;
    public string Glyph { get; } = glyph;
    public bool Closable { get; } = closable;
}

public sealed partial class HomeViewModel : ObservableObject, ISessionContent
{
    public string Title => "Home";

    public ObservableCollection<RecentSession> Recent { get; } = [];

    /// <summary>Saved sessions and workspaces, to open from the Home view.</summary>
    public ObservableCollection<SessionEntry> Sessions { get; } = [];
}

/// <summary>One line of the sessions list on the Home view.</summary>
public sealed record SessionEntry(string Name, string Kind, string Detail);

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly SettingsStore _store;

    public MainWindowViewModel(SettingsStore? store = null) : this(store ?? new SettingsStore(SettingsStore.DefaultPath), null, null)
    {
    }

    /// <summary>
    /// A further window shares the settings of the first. With a <paramref name="firstTab"/> it is a stand-alone window for that one
    /// session (no Home tab), which closes when the session is closed.
    /// </summary>
    public MainWindowViewModel(SettingsStore store, AppSettings? shared, SessionTab? firstTab)
    {
        _store = store;
        Settings = shared ?? _store.Load();
        Standalone = firstTab is not null;
        Home = new HomeViewModel();
        if (firstTab is null)
        {
            var homeTab = new SessionTab(Home, "⌂", closable: false);
            Tabs.Add(homeTab);
            SelectedTab = homeTab;
        }
        else
        {
            Tabs.Add(firstTab);
            SelectedTab = firstTab;
        }

        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowTabStrip));
        RefreshRecent();
        RefreshSessions();
        ApplyTheme();
        ApplyPreferences();
    }

    public SettingsStore Store => _store;

    /// <summary>True for a window that was opened for one session and has no Home tab.</summary>
    public bool Standalone { get; }

    /// <summary>Set by the window: opens a session in a window of its own (when the preferences ask for it).</summary>
    public Action<SessionTab>? NewWindowRequested { get; set; }

    /// <summary>Set by the window: close it (the last tab was closed and the preferences say the window goes too).</summary>
    public Action? CloseWindowRequested { get; set; }

    /// <summary>False when there is only one tab and the preferences hide the tab bar then.</summary>
    public bool ShowTabStrip => !(Settings.Prefs.Tabs.HideTabBarIfSingleTab && Tabs.Count <= 1);

    /// <summary>The preferences, applied wherever the program holds them in a static place.</summary>
    public void ApplyPreferences()
    {
        ArchiveExtractor.Configure(Settings.Prefs.Archives);
        FolderSync.RemoveReadOnlyFromOptical = Settings.Prefs.Files.RemoveReadOnlyFromOptical;
        FolderComparer.ConfigureBuffer(Settings.Prefs.Files.BinaryBufferSize);
        foreach (var tab in Tabs) { (tab.Content as TextCompareViewModel)?.PreferencesChanged(); }
        OnPropertyChanged(nameof(ShowTabStrip));
    }

    public AppSettings Settings { get; }
    public HomeViewModel Home { get; }
    public ObservableCollection<SessionTab> Tabs { get; } = [];

    [ObservableProperty] private SessionTab? _selectedTab;
    [ObservableProperty] private ThemeVariant _themeVariant = ThemeVariant.Default;
    [ObservableProperty] private string? _message;

    public string ThemeLabel => Settings.Theme switch { "Light" => "☀ Light", "Dark" => "🌙 Dark", _ => "◐ System" };

    private void ApplyTheme()
    {
        ThemeVariant = Settings.Theme switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
        OnPropertyChanged(nameof(ThemeLabel));
    }

    [RelayCommand]
    private void CycleTheme()
    {
        Settings.Theme = Settings.Theme switch { "System" => "Light", "Light" => "Dark", _ => "System" };
        ApplyTheme();
        Save();
    }

    private void Save()
    {
        try
        {
            _store.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // settings are a convenience; never fail the app because they cannot be written
        }
    }

    private void RefreshRecent()
    {
        Home.Recent.Clear();
        foreach (var r in Settings.Recent)
        {
            Home.Recent.Add(r);
        }
    }

    private void Remember(RecentSession s)
    {
        Settings.AddRecent(s);
        Save();
        RefreshRecent();
    }

    // ───────────── opening sessions ─────────────

    public TextCompareViewModel NewTextCompare()
    {
        var vm = new TextCompareViewModel();
        vm.ApplyDefaults(Settings, Save);
        vm.OpenFormattedRequested = (title, l, r) =>
        {
            var tab = NewTextCompare();
            tab.TitleOverride = title;
            tab.OpenText(null, l, null, r);
        };
        Add(new SessionTab(vm, "≋", closable: true));
        return vm;
    }

    /// <summary>Opens two files in whichever view suits them (picture, table, hex or text).</summary>
    public ISessionContent OpenFilePair(string left, string right) => CompareRouter.ForFiles(left, right) switch
    {
        LaunchKind.ImageCompare => OpenImageCompare(left, right),
        LaunchKind.TableCompare => OpenTableCompare(left, right),
        LaunchKind.HexCompare => OpenHexCompare(left, right),
        LaunchKind.ArchiveCompare => OpenArchiveCompare(left, right),
        _ => OpenTextCompare(left, right),
    };

    public TextCompareViewModel OpenTextCompare(string left, string right)
    {
        var vm = NewTextCompare();
        vm.Open(left, right);
        Remember(new RecentSession(LaunchKind.TextCompare, left, right, null, null, DateTime.UtcNow));
        return vm;
    }

    public HexCompareViewModel NewHexCompare()
    {
        var vm = new HexCompareViewModel();
        Add(new SessionTab(vm, "⌗", closable: true));
        return vm;
    }

    public HexCompareViewModel OpenHexCompare(string left, string right)
    {
        var vm = NewHexCompare();
        vm.Open(left, right);
        Remember(new RecentSession(LaunchKind.HexCompare, left, right, null, null, DateTime.UtcNow));
        return vm;
    }

    [RelayCommand]
    private void NewHexTab() => NewHexCompare();

    public TableCompareViewModel NewTableCompare()
    {
        var vm = new TableCompareViewModel();
        Add(new SessionTab(vm, "▦", closable: true));
        return vm;
    }

    public TableCompareViewModel OpenTableCompare(string left, string right)
    {
        var vm = NewTableCompare();
        vm.Open(left, right);
        Remember(new RecentSession(LaunchKind.TableCompare, left, right, null, null, DateTime.UtcNow));
        return vm;
    }

    [RelayCommand]
    private void NewTableTab() => NewTableCompare();

    public ImageCompareViewModel NewImageCompare()
    {
        var vm = new ImageCompareViewModel();
        Add(new SessionTab(vm, "◫", closable: true));
        return vm;
    }

    public ImageCompareViewModel OpenImageCompare(string left, string right)
    {
        var vm = NewImageCompare();
        vm.Open(left, right);
        Remember(new RecentSession(LaunchKind.ImageCompare, left, right, null, null, DateTime.UtcNow));
        return vm;
    }

    [RelayCommand]
    private void NewImageTab() => NewImageCompare();

    /// <summary>True while a session opened from inside a folder session is being added (it may go to a window of its own).</summary>
    private bool _fromFolder;

    private void Add(SessionTab tab)
    {
        var where = _fromFolder ? Settings.Prefs.Tabs.ChildSessions : Settings.Prefs.Tabs.NewSessions;
        if (where == OpenIn.Window && NewWindowRequested is not null)
        {
            NewWindowRequested(tab);
            return;
        }

        Tabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private void NewTextTab() => NewTextCompare();

    public FolderCompareViewModel NewFolderCompare()
    {
        var vm = new FolderCompareViewModel();
        vm.ApplyDefaults(Settings);
        vm.OpenTextRequested = (l, r) =>
        {
            // "Disable editing" protects what is opened from this session too
            _fromFolder = true;
            try
            {
                if (OpenFilePair(l, r) is TextCompareViewModel text)
                {
                    if (vm.DisableEditing) { text.EditingDisabled = true; }
                    // "next difference file" walks through the folder this file came from
                    text.FileNavigator = step => vm.NeighbourDifferentFile(text.Left.Path ?? l, step);
                }
            }
            finally
            {
                _fromFolder = false;
            }
        };
        vm.RequestSaveSettings = () => _store.Save(Settings);
        Add(new SessionTab(vm, "🗀", closable: true));
        return vm;
    }

    public async Task<FolderCompareViewModel> OpenFolderCompareAsync(string left, string right)
    {
        var vm = NewFolderCompare();
        Remember(new RecentSession(LaunchKind.FolderCompare, left, right, null, null, DateTime.UtcNow));
        await vm.OpenAsync(left, right);
        return vm;
    }

    /// <summary>Unpacks two archives into temporary folders (in the background) and compares those as folders.</summary>
    public FolderCompareViewModel OpenArchiveCompare(string left, string right)
    {
        var vm = NewFolderCompare();
        Remember(new RecentSession(LaunchKind.ArchiveCompare, left, right, null, null, DateTime.UtcNow));
        _ = OpenArchivesAsync(vm, left, right);
        return vm;
    }

    private static async Task OpenArchivesAsync(FolderCompareViewModel vm, string left, string right)
    {
        try
        {
            var (l, r) = await Task.Run(() => (ArchiveExtractor.Extract(left), ArchiveExtractor.Extract(right)));
            await vm.OpenAsync(l, r);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            vm.Status = $"Cannot open the archive: {ex.Message}";
        }
    }

    [RelayCommand]
    private void NewFolderTab() => NewFolderCompare();

    public MergeViewModel NewMerge()
    {
        var vm = new MergeViewModel { Prefs = Settings.Prefs };
        vm.ApplyDefaults(Settings, Save);
        Add(new SessionTab(vm, "⑃", closable: true));
        return vm;
    }

    public MergeViewModel OpenMerge(string? basePath, string left, string right, string? output)
    {
        var vm = NewMerge();
        vm.Open(basePath, left, right, output);
        Remember(new RecentSession(LaunchKind.Merge, left, right, basePath, output, DateTime.UtcNow));
        return vm;
    }

    /// <summary>The merge session started from the command line; decides the process exit code.</summary>
    public MergeViewModel? CommandLineMerge { get; private set; }

    /// <summary>0 when a command-line merge was saved without conflicts, otherwise 1 (what git's mergetool expects).</summary>
    public int ExitCode => CommandLineMerge is { Completed: false } ? 1 : 0;

    [RelayCommand]
    private void NewMergeTab() => NewMerge();

    [RelayCommand]
    private void CloseTab(SessionTab? tab)
    {
        if (tab is null || !tab.Closable)
        {
            return;
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (Tabs.Count == 0 || (Settings.Prefs.Tabs.ClosingLastTabClosesWindow && Tabs.All(t => !t.Closable)))
        {
            if (Standalone || Tabs.Count == 0) { CloseWindowRequested?.Invoke(); return; }
            CloseWindowRequested?.Invoke();
            return;
        }

        if (SelectedTab == tab || SelectedTab is null)
        {
            SelectedTab = Tabs[Math.Min(Math.Max(index - 1, 0), Tabs.Count - 1)];
        }
    }

    [RelayCommand]
    private void OpenRecent(RecentSession? r)
    {
        if (r is not null)
        {
            Launch(new LaunchRequest(r.Kind, r.Left, r.Right, r.Base, r.Output));
        }
    }

    /// <summary>Starts whatever a command line (or a drop, or a recent entry) asks for.</summary>
    public void Launch(LaunchRequest req)
    {
        Message = null;
        switch (req.Kind)
        {
            case LaunchKind.TextCompare when req.Left is not null && req.Right is not null:
                ApplyOptions(OpenTextCompare(req.Left, req.Right), req.Options);
                break;
            case LaunchKind.ImageCompare when req.Left is not null && req.Right is not null:
                OpenImageCompare(req.Left, req.Right);
                break;
            case LaunchKind.TableCompare when req.Left is not null && req.Right is not null:
                OpenTableCompare(req.Left, req.Right);
                break;
            case LaunchKind.HexCompare when req.Left is not null && req.Right is not null:
                OpenHexCompare(req.Left, req.Right);
                break;
            case LaunchKind.FolderCompare or LaunchKind.FolderSync when req.Left is not null && req.Right is not null:
                _ = OpenFolderFromCommandLineAsync(req);
                break;
            case LaunchKind.ArchiveCompare when req.Left is not null && req.Right is not null:
                OpenArchiveCompare(req.Left, req.Right);
                break;
            case LaunchKind.Merge when req.Left is not null && req.Right is not null:
                CommandLineMerge = OpenMerge(req.Base, req.Left, req.Right, req.Output);
                CommandLineMerge.NoBackups = req.Options.NoBackups;
                CommandLineMerge.TitleOverrides = [req.Options.Titles[0] ?? req.Options.Vcs[0], req.Options.Titles[1] ?? req.Options.Vcs[1], req.Options.Titles[2] ?? req.Options.Vcs[2], req.Options.Titles[3] ?? req.Options.Vcs[3]];
                CommandLineMerge.RefreshTitles();
                break;
            case LaunchKind.Named when req.Left is not null:
                OpenNamed(req.Left);
                break;
            case LaunchKind.Script when req.Left is not null:
                RunScript(req.Left, req.Options.CloseScript);
                break;
            case LaunchKind.Package when req.Left is not null:
                ImportPackage(req.Left);
                break;
            case LaunchKind.Patch when req.Left is not null:
                OpenPatch(req.Left);
                break;
            case LaunchKind.TextEdit:
                OpenTextEdit(req.Left);
                break;
            case LaunchKind.Stdin:
                OpenStandardInput();
                break;
            case LaunchKind.Clipboard:
                OpenClipboardCompare();
                break;
            case LaunchKind.Error:
                Message = req.Message;
                break;
        }
    }

    /// <summary>Applies the switches that protect sides, rename them or redirect saving.</summary>
    private static void ApplyOptions(TextCompareViewModel vm, LaunchOptions o)
    {
        vm.NoBackups = o.NoBackups;
        vm.LeftReadOnly = o.ReadOnlyLeft;
        vm.RightReadOnly = o.ReadOnlyRight;
        vm.SaveTarget = o.SaveTarget;
        vm.LeftTitleOverride = o.Titles[0] ?? o.Vcs[0];
        vm.RightTitleOverride = o.Titles[1] ?? o.Vcs[1];
    }

    private async Task OpenFolderFromCommandLineAsync(LaunchRequest req)
    {
        var vm = NewFolderCompare();
        if (req.Options.Filters is { Length: > 0 } filters) { vm.Include = filters; }
        if (req.Options.ReadOnlyLeft && req.Options.ReadOnlyRight) { vm.DisableEditing = true; }
        Remember(new RecentSession(LaunchKind.FolderCompare, req.Left, req.Right, null, null, DateTime.UtcNow));
        await vm.OpenAsync(req.Left!, req.Right!);
        if (req.Options.ExpandAll) { vm.ExpandAllCommand.Execute(null); }
        if (req.Kind == LaunchKind.FolderSync) { vm.SyncRequested?.Invoke(); }
    }

    /// <summary>Two dropped paths behave like <c>collaterale left right</c>.</summary>
    public void Drop(IReadOnlyList<string> paths)
    {
        if (paths.Count == 2)
        {
            Launch(LaunchParser.Parse(paths));
        }
        else
        {
            Message = "Drop exactly two files or two folders to compare them.";
        }
    }
}
