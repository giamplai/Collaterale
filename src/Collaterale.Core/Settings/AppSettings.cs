using System.Text.Json;
using Collaterale.Core.Cli;
using Collaterale.Core.Diff;
using Collaterale.Core.Folders;

namespace Collaterale.Core.Settings;

public sealed record RecentSession(LaunchKind Kind, string? Left, string? Right, string? Base, string? Output, DateTime OpenedUtc)
{
    public string Title => Kind switch
    {
        LaunchKind.Merge => $"Merge {Name(Output ?? Left)}",
        _ => $"{Name(Left)}  ↔  {Name(Right)}",
    };

    private static string Name(string? p) => string.IsNullOrEmpty(p) ? "?" : Path.GetFileName(p.TrimEnd('/', '\\')) is { Length: > 0 } n ? n : p;

    public bool SameAs(RecentSession o) => Kind == o.Kind && Left == o.Left && Right == o.Right && Base == o.Base && Output == o.Output;
}

/// <summary>
/// How the text compare looks and compares when it opens; every change made in a text compare is written back here, so the
/// next session starts the way the last one was left. (Whitespace, case and algorithm live on <see cref="AppSettings"/> and are shared with the merge.)
/// </summary>
public sealed class TextViewDefaults
{
    public bool DetectMoves { get; set; } = true;
    public bool CollapseUnchanged { get; set; }
    public bool ShowWhitespace { get; set; }
    public bool WordWrap { get; set; }
    public bool OverUnder { get; set; }

    /// <summary>The panel under the panes: "None", "Text" or "Hex".</summary>
    public string Details { get; set; } = "None";

    public bool IgnoreComments { get; set; }
    public bool IgnoreStrings { get; set; }
    public bool IgnoreTimestamps { get; set; }
    public bool IgnoreBlankLines { get; set; }
}

/// <summary>What a three-way merge remembers from the last one (whitespace and case are shared with the text compare).</summary>
public sealed class MergeViewDefaults
{
    public bool ShowBase { get; set; } = true;

    /// <summary>Which sections are shown: the name of a <c>MergeFilter</c>.</summary>
    public string Filter { get; set; } = "All";

    /// <summary>The panel under the result: "None" or "Text".</summary>
    public string Details { get; set; } = "None";
}

/// <summary>Colors of the folder view as #RRGGBB; null keeps the theme's own default (which differs between light and dark).</summary>
public sealed class FolderColors
{
    /// <summary>The older copy of a file that differs, and anything that cannot be judged.</summary>
    public string? Older { get; set; }

    /// <summary>Identical files; null = the normal text color.</summary>
    public string? Same { get; set; }

    /// <summary>Files that exist on one side only.</summary>
    public string? Orphan { get; set; }

    /// <summary>The newer copy of a file that differs, and other differences.</summary>
    public string? Newer { get; set; }

    /// <summary>A file hidden by a filter that is shown only because its counterpart is not hidden.</summary>
    public string? Filtered { get; set; }
}

public sealed class AppSettings
{
    public const int MaxRecent = 20;

    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";

    public List<RecentSession> Recent { get; set; } = [];

    // defaults for new text comparisons
    public WhitespaceMode Whitespace { get; set; } = WhitespaceMode.Exact;
    public bool IgnoreCase { get; set; }

    public TextViewDefaults TextView { get; set; } = new();
    public MergeViewDefaults MergeView { get; set; } = new();

    /// <summary>Saved sets of folder name filters.</summary>
    public List<Collaterale.Core.Folders.FilterPreset> FilterPresets { get; set; } = [];
    public DiffAlgorithm Algorithm { get; set; } = DiffAlgorithm.Myers;

    public FolderColors Colors { get; set; } = new();

    /// <summary>What the options window edits.</summary>
    public Preferences Prefs { get; set; } = new();

    /// <summary>Settings of the folder compare dialog saved as the starting point of new sessions; null = built-in defaults.</summary>
    public FolderOptions? FolderDefaults { get; set; }

    // defaults for new folder comparisons
    public CompareCriteria Criteria { get; set; } = CompareCriteria.SizeAndTime;
    public string ExcludeFolders { get; set; } = ".git;.svn;.hg;node_modules";
    public string ExcludeFiles { get; set; } = "";

    public List<SavedSession> Sessions { get; set; } = [];
    public List<Workspace> Workspaces { get; set; } = [];

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public void SaveSession(SavedSession s)
    {
        Sessions.RemoveAll(x => Same(x.Name, s.Name));
        Sessions.Add(s);
    }

    /// <summary>Removes one of your own sessions; shared sessions cannot be removed here.</summary>
    public bool DeleteSession(string name) => Sessions.RemoveAll(x => Same(x.Name, name)) > 0;

    public void SaveWorkspace(Workspace w)
    {
        Workspaces.RemoveAll(x => Same(x.Name, w.Name));
        Workspaces.Add(w);
    }

    public bool DeleteWorkspace(string name) => Workspaces.RemoveAll(x => Same(x.Name, name)) > 0;

    /// <summary>Your sessions followed by those of the shared sessions file.</summary>
    public IEnumerable<(SavedSession Session, bool Shared)> AllSessions()
    {
        foreach (var s in Sessions) { yield return (s, false); }
        if (!string.IsNullOrWhiteSpace(Prefs.SharedSessionsFile) && File.Exists(Prefs.SharedSessionsFile))
        {
            foreach (var s in SessionFile.Read(Prefs.SharedSessionsFile).Sessions) { yield return (s, true); }
        }
    }

    public SavedSession? FindSession(string name) => AllSessions().Select(t => t.Session).FirstOrDefault(s => Same(s.Name, name));

    /// <summary>What a name on the command line stands for: a session (one tab) or a workspace; empty when it is neither.</summary>
    public Workspace Resolve(string name)
    {
        if (FindSession(name) is { } session) { return new Workspace { Name = session.Name, Sessions = [session] }; }
        var shared = string.IsNullOrWhiteSpace(Prefs.SharedSessionsFile) || !File.Exists(Prefs.SharedSessionsFile) ? [] : SessionFile.Read(Prefs.SharedSessionsFile).Workspaces;
        return Workspaces.Concat(shared).FirstOrDefault(w => Same(w.Name, name)) ?? new Workspace { Name = name };
    }

    /// <summary>Keeps a snapshot of what was open, dropping the oldest snapshots beyond the preferred number (0 keeps none).</summary>
    public void AutoSaveWorkspace(Workspace w, DateTime now)
    {
        var keep = Prefs.Metrics.MaxAutoSavedSessions;
        Workspaces.RemoveAll(x => x.AutoSaved && keep <= 0);
        if (keep <= 0 || w.Sessions.Count == 0) { return; }
        w.AutoSaved = true;
        w.Name = "Autosave " + now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        Workspaces.RemoveAll(x => x.AutoSaved && Same(x.Name, w.Name));
        Workspaces.Insert(0, w);
        var auto = Workspaces.Where(x => x.AutoSaved).ToList();
        foreach (var old in auto.Skip(keep)) { Workspaces.Remove(old); }
    }

    public void AddRecent(RecentSession s)
    {
        Recent.RemoveAll(x => x.SameAs(s));
        Recent.Insert(0, s);
        var max = Math.Max(1, Prefs.Metrics.MaxRecentItems);
        if (Recent.Count > max)
        {
            Recent.RemoveRange(max, Recent.Count - max);
        }
    }
}

/// <summary>JSON settings file. Missing or corrupt files yield defaults; saves are atomic.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Collaterale", "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings() : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
