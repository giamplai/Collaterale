using Collaterale.Core.Cli;
using Collaterale.Core.Integration;
using Collaterale.Core.Scripting;
using Collaterale.Core.Settings;
using Collaterale.Core.Support;
using System.Net.Http;

namespace Collaterale.App.ViewModels;

/// <summary>Sessions, workspaces, scripts, packages, patches and text editing: everything that is more than "open two things".</summary>
public sealed partial class MainWindowViewModel
{
    // ───────────── describing what is open ─────────────

    /// <summary>The command that would open the same thing again; null for tabs that cannot be saved (Home, an empty compare).</summary>
    public static LaunchRequest? RequestOf(ISessionContent content) => content switch
    {
        TextCompareViewModel t when t.Left.Path is { } l && t.Right.Path is { } r && t.TitleOverride is null => new LaunchRequest(LaunchKind.TextCompare, l, r),
        FolderCompareViewModel { LeftPath.Length: > 0, RightPath.Length: > 0 } f => new LaunchRequest(LaunchKind.FolderCompare, f.LeftPath, f.RightPath) { Options = f.Include.Length > 0 ? new LaunchOptions { Filters = f.Include } : LaunchOptions.None },
        MergeViewModel m when m.Left.Path is { } l && m.Right.Path is { } r => new LaunchRequest(LaunchKind.Merge, l, r, m.Base.Path, m.OutputPath),
        HexCompareViewModel h when h.Left.Path is { } l && h.Right.Path is { } r => new LaunchRequest(LaunchKind.HexCompare, l, r),
        TableCompareViewModel { LeftPath: { } l, RightPath: { } r } => new LaunchRequest(LaunchKind.TableCompare, l, r),
        ImageCompareViewModel { LeftPath: { } l, RightPath: { } r } => new LaunchRequest(LaunchKind.ImageCompare, l, r),
        TextEditViewModel { Path: { } p } => new LaunchRequest(LaunchKind.TextEdit, p),
        _ => null,
    };

    /// <summary>Everything that is open, for a workspace.</summary>
    public Workspace CurrentWorkspace(string name)
    {
        var sessions = new List<SavedSession>();
        var selected = 0;
        foreach (var tab in Tabs)
        {
            if (RequestOf(tab.Content) is not { } req) { continue; }
            if (tab == SelectedTab) { selected = sessions.Count; }
            sessions.Add(SavedSession.From(tab.Content.Title, req));
        }

        return new Workspace { Name = name, Sessions = sessions, Selected = selected };
    }

    public bool CanSaveSession => SelectedTab is { } t && RequestOf(t.Content) is not null;

    /// <summary>Saves the tab in front under a name (the Session menu).</summary>
    public bool SaveSession(string name)
    {
        if (SelectedTab is not { } tab || RequestOf(tab.Content) is not { } req)
        {
            Message = "Open two files or folders first: there is nothing in this tab to save as a session.";
            return false;
        }

        Settings.SaveSession(SavedSession.From(name, req));
        Persist();
        Message = null;
        return true;
    }

    public bool SaveWorkspace(string name)
    {
        var w = CurrentWorkspace(name);
        if (w.Sessions.Count == 0)
        {
            Message = "Nothing is open that could be saved in a workspace.";
            return false;
        }

        Settings.SaveWorkspace(w);
        Persist();
        Message = null;
        return true;
    }

    /// <summary>Opens a saved session or workspace by name.</summary>
    public void OpenNamed(string name)
    {
        var w = Settings.Resolve(name);
        if (w.Sessions.Count == 0)
        {
            Message = $"There is no session or workspace named “{name}”.";
            return;
        }

        OpenWorkspace(w);
    }

    public void OpenWorkspace(Workspace w)
    {
        var before = Tabs.Count;
        foreach (var s in w.Sessions)
        {
            if (!s.PathsExist()) { Message = $"“{s.Name}” was skipped: a file or folder it names is gone."; continue; }
            Launch(s.ToRequest());
        }

        var wanted = before + Math.Clamp(w.Selected, 0, Math.Max(w.Sessions.Count - 1, 0));
        if (wanted < Tabs.Count) { SelectedTab = Tabs[wanted]; }
    }

    /// <summary>What the preferences say to do when the program starts with no command line.</summary>
    public void StartupWorkspace()
    {
        if (Settings.Prefs.Startup.LoadWorkspace is { Length: > 0 } name) { OpenNamed(name); }
    }

    /// <summary>Called when the window closes: keeps a snapshot of what was open.</summary>
    public void SaveOnExit()
    {
        var w = CurrentWorkspace("");
        if (w.Sessions.Count == 0) { Persist(); return; }
        if (Settings.Prefs.Startup.SaveWorkspaceOnExit is { Length: > 0 } name)
        {
            w.Name = name;
            Settings.SaveWorkspace(w);
        }

        Settings.AutoSaveWorkspace(CurrentWorkspace(""), DateTime.Now);
        Persist();
    }

    public void Persist()
    {
        try
        {
            _store.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // settings are a convenience
        }

        RefreshRecent();
        RefreshSessions();
    }

    /// <summary>The Home view lists saved sessions and workspaces.</summary>
    public void RefreshSessions()
    {
        Home.Sessions.Clear();
        foreach (var (s, shared) in Settings.AllSessions()) { Home.Sessions.Add(new SessionEntry(s.Name, shared ? "shared session" : "session", $"{System.IO.Path.GetFileName(s.Left ?? "")}  ↔  {System.IO.Path.GetFileName(s.Right ?? "")}")); }
        foreach (var w in Settings.Workspaces.Where(w => !w.AutoSaved)) { Home.Sessions.Add(new SessionEntry(w.Name, "workspace", $"{w.Sessions.Count} session{(w.Sessions.Count == 1 ? "" : "s")}")); }
    }

    // ───────────── scripts, packages, patches, editing ─────────────

    public void RunScript(string path, bool closeWhenDone = false)
    {
        var vm = new ScriptViewModel(path);
        Add(new SessionTab(vm, "▷", closable: true));
        var prefs = Settings.Prefs.Scripts;
        _ = Task.Run(() =>
        {
            var writer = new StringWriter();
            var code = ScriptEngine.RunNamed(path, writer, prefs);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                vm.Output = writer.ToString();
                vm.ExitCode = code;
                vm.Running = false;
                if ((prefs.CloseWhenFinished || closeWhenDone) && code == 0) { CloseWindowRequested?.Invoke(); }
            });
        });
    }

    public void ImportPackage(string path)
    {
        try
        {
            Message = SettingsPackage.Import(path, Settings);
            ApplyTheme();
            ApplyPreferences();
            Persist();
        }
        catch (InvalidDataException ex)
        {
            Message = ex.Message;
        }
    }

    public void ExportPackage(string path)
    {
        try
        {
            SettingsPackage.Export(Settings, path);
            Message = $"Exported the settings to {path}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = ex.Message;
        }
    }

    public void ExportSessions(string path)
    {
        try
        {
            SessionFile.Write(path, Settings.Sessions, Settings.Workspaces.Where(w => !w.AutoSaved));
            Message = $"Exported {Settings.Sessions.Count} session(s) to {path}. Set it as the shared sessions file on another machine.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = ex.Message;
        }
    }

    /// <summary>A patch file: each file in it opens as before/after in a read-only text compare.</summary>
    public void OpenPatch(string path)
    {
        IReadOnlyList<FilePatch> files;
        try
        {
            files = PatchReader.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = ex.Message;
            return;
        }

        if (files.Count == 0) { Message = $"'{System.IO.Path.GetFileName(path)}' does not contain a patch in the unified diff format."; return; }
        foreach (var f in files)
        {
            var (before, after) = f.Sides();
            var vm = NewTextCompare();
            vm.TitleOverride = "Patch: " + (f.IsDeleted ? f.OldName : f.NewName);
            vm.LeftTitleOverride = f.OldName + " (before)";
            vm.RightTitleOverride = f.NewName + " (after)";
            vm.OpenText(null, before, null, after);
            vm.LeftReadOnly = vm.RightReadOnly = true;
        }
    }

    public TextEditViewModel OpenTextEdit(string? path)
    {
        var vm = new TextEditViewModel { Prefs = Settings.Prefs };
        if (path is not null) { vm.Open(path); Remember(new RecentSession(LaunchKind.TextEdit, path, null, null, null, DateTime.UtcNow)); }
        Add(new SessionTab(vm, "✎", closable: true));
        return vm;
    }

    public TextEditViewModel OpenStandardInput()
    {
        var vm = new TextEditViewModel();
        string text;
        try
        {
            text = Console.IsInputRedirected ? Console.In.ReadToEnd() : "";
        }
        catch (IOException)
        {
            text = "";
        }

        vm.OpenText(text, "Standard input");
        Add(new SessionTab(vm, "✎", closable: true));
        return vm;
    }

    // ───────────── updates ─────────────

    /// <summary>Asks the update feed when the preferences say a check is due; the answer appears as a message.</summary>
    public async Task CheckForUpdatesAsync(HttpClient? http = null)
    {
        var u = Settings.Prefs.Updates;
        if (!UpdateCheck.IsDue(u, DateTime.UtcNow)) { return; }
        u.LastCheckUtc = DateTime.UtcNow;
        Persist();
        using var own = http is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(10) } : null;
        var result = await UpdateCheck.RunAsync(http ?? own!, u.FeedUrl, AppInfo.Version);
        if (result.Available) { Message = result.Message; }
    }

    // ───────────── clipboard ─────────────

    private ClipboardHistory? _clipboard;

    public ClipboardHistory Clipboard => _clipboard ??= new ClipboardHistory(ClipboardHistory.DefaultPath, 10);

    /// <summary>Remembers what is on the clipboard now (called when the window gets the focus).</summary>
    public void CaptureClipboard(string? text) => Clipboard.Add(text);

    /// <summary>Compares the two most recent clipboard texts in a text compare.</summary>
    public bool OpenClipboardCompare()
    {
        if (Clipboard.LastTwo() is not { } pair)
        {
            Message = "Copy two different pieces of text first: the clipboard history has fewer than two entries.";
            return false;
        }

        var vm = NewTextCompare();
        vm.TitleOverride = "Clipboard compare";
        vm.LeftTitleOverride = "Previous clipboard text";
        vm.RightTitleOverride = "Latest clipboard text";
        vm.OpenText(null, TextFileLines(pair.Previous), null, TextFileLines(pair.Newest));
        return true;
    }

    /// <summary>Compares the clipboard text (left) with a file (right).</summary>
    public void OpenClipboardWithFile(string text, string path)
    {
        var vm = NewTextCompare();
        vm.TitleOverride = "Clipboard ↔ " + System.IO.Path.GetFileName(path);
        vm.LeftTitleOverride = "Clipboard";
        vm.OpenText(null, TextFileLines(text), path, Collaterale.Core.Text.TextFile.Load(path).Lines);
    }

    private static IEnumerable<string> TextFileLines(string text) => Collaterale.Core.Text.TextFile.Split(text).Lines;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NewEditTab() => OpenTextEdit(null);
}
