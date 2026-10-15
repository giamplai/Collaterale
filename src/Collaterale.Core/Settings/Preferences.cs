namespace Collaterale.Core.Settings;

/// <summary>Where a new session opens.</summary>
public enum OpenIn { Tab, Window }

/// <summary>The quick-compare dialog that can precede a file comparison started from the command line.</summary>
public enum QuickCompareKind { Binary, Rules }

/// <summary>How a copy of a file is kept before it is overwritten.</summary>
public enum BackupMode
{
    /// <summary>No backups.</summary>
    Off,

    /// <summary>Next to the file, with a suffix (<c>name.txt.bak</c>).</summary>
    SameFolderSuffix,

    /// <summary>Next to the file, numbered (<c>name.txt.bak1</c>, <c>.bak2</c>…).</summary>
    SameFolderNumbered,

    /// <summary>Next to the file, stamped with the date and time (<c>name.txt.20260131-142530.bak</c>).</summary>
    SameFolderTimestamp,

    /// <summary>In one folder of your choice, keeping the relative layout.</summary>
    BackupFolder,
}

/// <summary>What happens at a synchronize prompt that would otherwise ask every time.</summary>
public enum Confirmation { Prompt, YesToAll, NoToAll }

/// <summary>Work done in the background runs at this priority.</summary>
public enum ComparisonPriority { Low, Normal, High }

public sealed class StartupPreferences
{
    /// <summary>Workspace opened when the app starts with no command line; empty = the Home view.</summary>
    public string LoadWorkspace { get; set; } = "";

    /// <summary>Workspace saved when the app closes; empty = none. Use the same name as <see cref="LoadWorkspace"/> to come back to where you left off.</summary>
    public string SaveWorkspaceOnExit { get; set; } = "";

    /// <summary>Show a summary dialog before a file comparison started from the command line.</summary>
    public bool QuickCompareDialog { get; set; }

    public QuickCompareKind QuickCompare { get; set; } = QuickCompareKind.Rules;

    /// <summary>With the quick dialog on: open the full view straight away when the files differ (the dialog appears only when they match).</summary>
    public bool OpenViewIfDifferent { get; set; } = true;

    public bool RunClipboardCompareOnLogin { get; set; }
}

public sealed class TabPreferences
{
    public OpenIn NewSessions { get; set; } = OpenIn.Tab;

    /// <summary>Where a file session opened from a folder session goes.</summary>
    public OpenIn ChildSessions { get; set; } = OpenIn.Tab;

    public bool ClosingLastTabClosesWindow { get; set; }
    public bool HideTabBarIfSingleTab { get; set; }
}

public sealed class TextEditingPreferences
{
    public bool AutoIndent { get; set; } = true;
    public bool BackspaceUnindents { get; set; } = true;
    public bool InitFindFromWord { get; set; } = true;
    public bool ShowFilteredLineCounts { get; set; } = true;

    /// <summary>Unchanged lines kept around each difference when unchanged text is folded.</summary>
    public int ContextLines { get; set; } = 3;

    /// <summary>The width of a tab, in spaces, for indenting and unindenting.</summary>
    public int TabSize { get; set; } = 4;
}

public sealed class NextDifferencePreferences
{
    public bool GoToFirstOnLoad { get; set; } = true;
    public bool GoToNextAfterCopy { get; set; } = true;
    public bool LimitFilesToCurrentFolder { get; set; }
    public bool WrapAround { get; set; } = true;
    public bool ShowMessagePanel { get; set; } = true;
}

public sealed class BackupPreferences
{
    /// <summary>Off by default: a backup next to the file would show up as an extra file in the next comparison of the same folders.</summary>
    public BackupMode Mode { get; set; } = BackupMode.Off;

    /// <summary>The suffix for <see cref="BackupMode.SameFolderSuffix"/>.</summary>
    public string Suffix { get; set; } = ".bak";

    /// <summary>The folder for <see cref="BackupMode.BackupFolder"/>.</summary>
    public string Folder { get; set; } = "";

    /// <summary>How many numbered or time-stamped backups of one file are kept; older ones are deleted (0 = keep all).</summary>
    public int KeepVersions { get; set; } = 5;
}

public sealed class FileOperationPreferences
{
    public bool ConfirmCopy { get; set; } = true;
    public bool ConfirmMove { get; set; } = true;
    public bool ConfirmDelete { get; set; } = true;
    public bool ConfirmOverwrite { get; set; } = true;
    public Confirmation SyncConfirmation { get; set; } = Confirmation.Prompt;
}

public sealed class ArchiveType
{
    public string Format { get; set; } = "";

    /// <summary>File masks separated by semicolons, for example <c>*.zip;*.jar</c>; empty drops support for the format.</summary>
    public string Mask { get; set; } = "";
}

public sealed class OpenWithApp
{
    public string Name { get; set; } = "";

    /// <summary>The program to start.</summary>
    public string Command { get; set; } = "";

    /// <summary>Its arguments; <c>%1</c> is the file, <c>%2</c> the file on the other side when there is one.</summary>
    public string Arguments { get; set; } = "\"%1\"";
}

/// <summary>A keyboard shortcut chosen by the user for a command.</summary>
public sealed class CommandBinding
{
    public string Command { get; set; } = "";

    /// <summary>Null keeps the default; empty removes the shortcut.</summary>
    public string? Shortcut { get; set; }

}

public sealed class LogPreferences
{
    public bool Summaries { get; set; } = true;
    public bool Details { get; set; }
    public bool Warnings { get; set; } = true;
    public bool Debug { get; set; }

    /// <summary>Messages kept in the panel before the oldest are dropped.</summary>
    public int MaxMessages { get; set; } = 500;

    /// <summary>When set, messages are also appended to this file.</summary>
    public string AutoSaveFile { get; set; } = "";
}

public sealed class MetricsPreferences
{
    public int MaxAutoSavedSessions { get; set; } = 10;
    public int MaxRecentItems { get; set; } = 20;
    public int HintAppearMs { get; set; } = 500;
    public int IncrementalSearchResetMs { get; set; } = 1000;
}

public sealed class ScriptPreferences
{
    public bool BeepWhenFinished { get; set; }
    public bool CloseWhenFinished { get; set; }

    /// <summary>A script name that is not found in the current folder is looked for here.</summary>
    public string SharedFolder { get; set; } = "";
}

public sealed class EditorDisplayPreferences
{
    public bool SyntaxOnDifferenceLines { get; set; } = true;
    public bool CrosshatchBeyondEnd { get; set; } = true;
    public bool UseOrphanColor { get; set; }

    /// <summary>The color of lines that exist on one side only, when <see cref="UseOrphanColor"/> is on (#RRGGBB).</summary>
    public string OrphanColor { get; set; } = "#A06BE0";

    /// <summary>Column at which a thin vertical line is drawn; 0 hides it.</summary>
    public int ColumnLine { get; set; }

    public bool DimInactivePane { get; set; }
    /// <summary>A font family for the three input panes of a merge (empty = the same as the others) and its size.</summary>
    public string MergeInputFont { get; set; } = "";

    public int MergeInputFontSize { get; set; } = 12;
}

public sealed class FileHandlingPreferences
{
    public bool BeepAfterLongOperations { get; set; }
    public bool RemoveReadOnlyFromOptical { get; set; } = true;
    public bool CheckFilesChangedOnDisk { get; set; } = true;
    public bool AutoReloadUnlessEdited { get; set; }
    public ComparisonPriority Priority { get; set; } = ComparisonPriority.Normal;

    /// <summary>Bytes read at a time in a binary comparison.</summary>
    public int BinaryBufferSize { get; set; } = 81920;
}

public sealed class InterfacePreferences
{
    public bool ShowHints { get; set; } = true;
    public bool EscClosesFileViews { get; set; }
    public bool StickySplitter { get; set; }
    public bool StickySessionSelection { get; set; }

    /// <summary>The share of the merge window the three input panes take (kept when <see cref="StickySplitter"/> is on).</summary>
    public double MergeTopShare { get; set; } = 0.5;

    /// <summary>The tile of the Home view used last (kept when <see cref="StickySessionSelection"/> is on).</summary>
    public string LastHomeTile { get; set; } = "";
}

public sealed class UpdatePreferences
{
    public bool Check { get; set; }
    public int EveryDays { get; set; } = 7;

    /// <summary>Address of a small JSON file <c>{"version": "1.2.3", "url": "…"}</c>; empty disables the check.</summary>
    public string FeedUrl { get; set; } = "";

    /// <summary>When the last check ran (so the next one waits <see cref="EveryDays"/>).</summary>
    public DateTime LastCheckUtc { get; set; }
}

/// <summary>Everything the options window edits, grouped as its pages are.</summary>
public sealed class Preferences
{
    public StartupPreferences Startup { get; set; } = new();
    public TabPreferences Tabs { get; set; } = new();
    public TextEditingPreferences TextEditing { get; set; } = new();
    public NextDifferencePreferences NextDifference { get; set; } = new();
    public BackupPreferences Backup { get; set; } = new();
    public FileOperationPreferences FileOperations { get; set; } = new();

    /// <summary>The file masks that make a file an archive, per format; null = the built-in list.</summary>
    public List<ArchiveType>? Archives { get; set; }

    public List<CommandBinding> Commands { get; set; } = [];
    public List<OpenWithApp> OpenWith { get; set; } = [];
    public LogPreferences Log { get; set; } = new();
    public MetricsPreferences Metrics { get; set; } = new();
    public ScriptPreferences Scripts { get; set; } = new();
    public EditorDisplayPreferences Editor { get; set; } = new();
    public FileHandlingPreferences Files { get; set; } = new();
    public InterfacePreferences Interface { get; set; } = new();
    public UpdatePreferences Updates { get; set; } = new();

    /// <summary>A file of sessions kept by someone else (read-only here).</summary>
    public string SharedSessionsFile { get; set; } = "";
}
