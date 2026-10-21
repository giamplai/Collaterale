using System.Text;
using Collaterale.App.Controls;
using Collaterale.Core.Diff;
using Collaterale.Core.Languages;
using Collaterale.Core.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collaterale.App.ViewModels;

public enum Side { Left, Right }

/// <summary>One side of a text comparison: the file it came from plus the working copy of its lines.</summary>
public sealed class TextSide
{
    public string? Path { get; set; }
    public List<string> Lines { get; set; } = [];
    public EolStyle Eol { get; set; } = EolStyle.Lf;
    public bool EndsWithNewline { get; set; } = true;
    public Encoding Encoding { get; set; } = new UTF8Encoding(false);
    public bool HasBom { get; set; }
    public bool IsBinary { get; set; }
    public bool Dirty { get; set; }

    public string EncodingLabel => Encoding.CodePage switch
    {
        65001 => HasBom ? "UTF-8 BOM" : "UTF-8",
        1200 => "UTF-16 LE",
        1201 => "UTF-16 BE",
        _ => Encoding.WebName.ToUpperInvariant(),
    };

    public string EolLabel => Eol switch { EolStyle.CrLf => "CRLF", EolStyle.Cr => "CR", EolStyle.Mixed => "Mixed", _ => "LF" };

    public void Load(string path)
    {
        var t = TextFile.Load(path);
        Path = path;
        Lines = [.. t.Lines];
        Eol = t.Eol == EolStyle.None ? EolStyle.Lf : t.Eol;
        EndsWithNewline = t.EndsWithNewline;
        Encoding = t.Encoding;
        HasBom = t.HasBom;
        IsBinary = t.IsBinary;
        Dirty = false;
        Stamp();
    }

    /// <summary>When the file on disk was last known to match what is shown (to notice later changes made by others).</summary>
    public DateTime LoadedUtc { get; private set; }

    /// <summary>Notes the file's current modification time as "seen".</summary>
    public void Stamp() => LoadedUtc = Path is not null && File.Exists(Path) ? File.GetLastWriteTimeUtc(Path) : DateTime.MinValue;

    /// <summary>True when the file has been changed on disk since it was loaded or saved here.</summary>
    public bool ChangedOnDisk => Path is not null && File.Exists(Path) && File.GetLastWriteTimeUtc(Path) > LoadedUtc;

    public void SetText(string? path, IEnumerable<string> lines)
    {
        Path = path;
        Lines = [.. lines];
        Dirty = false;
    }

    public void Save(string? path = null, Collaterale.Core.Settings.BackupPreferences? backup = null)
    {
        path ??= Path ?? throw new InvalidOperationException("No file path to save to.");
        if (backup is not null) { Collaterale.Core.Settings.Backups.Create(path, backup); }
        File.WriteAllBytes(path, TextFile.Encode(Lines, Eol, EndsWithNewline, Encoding, HasBom));
        Path = path;
        Dirty = false;
        Stamp();
    }

    /// <summary>Writes the text to another file, leaving this side attached to its own file (the /savetarget switch).</summary>
    public void SaveTo(string path, Collaterale.Core.Settings.BackupPreferences? backup = null)
    {
        if (backup is not null) { Collaterale.Core.Settings.Backups.Create(path, backup); }
        File.WriteAllBytes(path, TextFile.Encode(Lines, Eol, EndsWithNewline, Encoding, HasBom));
        Dirty = false;
    }
}

public partial class TextCompareViewModel : ObservableObject, ISessionContent
{
    /// <summary>Set for tabs that show derived text (for example the formatted structure of two JSON files) instead of the files themselves.</summary>
    public string? TitleOverride { get; set; }

    public string Title => TitleOverride ?? (Left.Path, Right.Path) switch
    {
        (null, null) => "Text compare",
        (var l, var r) => $"{(l is null ? "…" : System.IO.Path.GetFileName(l))}  ↔  {(r is null ? "…" : System.IO.Path.GetFileName(r))}",
    };

    /// <summary>Starts from the user's saved preferences.</summary>
    /// <summary>Raised when a file was changed by someone else and the user should be asked whether to reload.</summary>
    public event Action<Side>? DiskChanged;

    /// <summary>
    /// Called when the window gets the focus back: notices files changed on disk. A file without unsaved edits is reloaded at once when
    /// the preferences say so; otherwise the user is asked (and asked only once per change).
    /// </summary>
    public void CheckDisk()
    {
        if (!Prefs.Files.CheckFilesChangedOnDisk || TitleOverride is not null) { return; }
        var changed = new[] { (Side.Left, Left), (Side.Right, Right) }.Where(t => t.Item2.ChangedOnDisk).ToList();
        if (changed.Count == 0) { return; }
        if (Prefs.Files.AutoReloadUnlessEdited && changed.All(t => !t.Item2.Dirty))
        {
            ReloadCommand.Execute(null);
            Status = "Reloaded: the file changed on disk";
            return;
        }

        foreach (var (side, s) in changed)
        {
            s.Stamp();                      // ask once per change
            DiskChanged?.Invoke(side);
        }
    }

    /// <summary>The program's preferences; a fresh set until the shell supplies the real one.</summary>
    public Collaterale.Core.Settings.Preferences Prefs { get; set; } = new();

    /// <summary>The /nobackups switch: no backup of files this session saves.</summary>
    public bool NoBackups { get; set; }

    private Collaterale.Core.Settings.BackupPreferences Backup => NoBackups ? new() { Mode = Collaterale.Core.Settings.BackupMode.Off } : Prefs.Backup;

    /// <summary>
    /// Starts from the choices remembered in the settings; from then on every change of an option or of the view is written back
    /// (through <paramref name="save"/>), so the next session opens the way this one was left.
    /// </summary>
    public void ApplyDefaults(Collaterale.Core.Settings.AppSettings s, Action? save = null)
    {
        Prefs = s.Prefs;
        _remembering = true;
        var v = s.TextView;
        Whitespace = s.Whitespace;
        IgnoreCase = s.IgnoreCase;
        Algorithm = s.Algorithm;
        DetectMoves = v.DetectMoves;
        CollapseUnchanged = v.CollapseUnchanged;
        ShowWhitespace = v.ShowWhitespace;
        WordWrap = v.WordWrap;
        OverUnder = v.OverUnder;
        Details = Enum.TryParse<DetailsMode>(v.Details, out var d) ? d : DetailsMode.None;
        IgnoreComments = v.IgnoreComments;
        IgnoreTimestamps = v.IgnoreTimestamps;
        IgnoreBlankLines = v.IgnoreBlankLines;
        _remembering = false;
        (_settings, _saveSettings) = (s, save);
    }

    private Collaterale.Core.Settings.AppSettings? _settings;
    private Action? _saveSettings;
    private bool _remembering;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_remembering || _settings is not { } s) { return; }
        var v = s.TextView;
        switch (e.PropertyName)
        {
            case nameof(Whitespace): s.Whitespace = Whitespace; break;
            case nameof(IgnoreCase): s.IgnoreCase = IgnoreCase; break;
            case nameof(Algorithm): s.Algorithm = Algorithm; break;
            case nameof(DetectMoves): v.DetectMoves = DetectMoves; break;
            case nameof(CollapseUnchanged): v.CollapseUnchanged = CollapseUnchanged; break;
            case nameof(ShowWhitespace): v.ShowWhitespace = ShowWhitespace; break;
            case nameof(WordWrap): v.WordWrap = WordWrap; break;
            case nameof(OverUnder): v.OverUnder = OverUnder; break;
            case nameof(Details): v.Details = Details.ToString(); break;
            case nameof(IgnoreComments): v.IgnoreComments = IgnoreComments; break;
            case nameof(IgnoreTimestamps): v.IgnoreTimestamps = IgnoreTimestamps; break;
            case nameof(IgnoreBlankLines): v.IgnoreBlankLines = IgnoreBlankLines; break;
            default: return;
        }

        _saveSettings?.Invoke();
    }

    private const int UndoLimit = 100;

    private readonly Stack<(List<string> Left, List<string> Right, bool LDirty, bool RDirty)> _undo = new();
    private readonly Stack<(List<string> Left, List<string> Right, bool LDirty, bool RDirty)> _redo = new();

    public TextSide Left { get; } = new();
    public TextSide Right { get; } = new();

    [ObservableProperty] private TextComparison? _comparison;
    [ObservableProperty] private int _currentGroup = -1;
    [ObservableProperty] private string _status = "Open two files to compare them";
    [ObservableProperty] private string? _error;

    // options
    [ObservableProperty] private WhitespaceMode _whitespace = WhitespaceMode.Exact;
    [ObservableProperty] private bool _ignoreCase;
    [ObservableProperty] private DiffAlgorithm _algorithm = DiffAlgorithm.Myers;
    [ObservableProperty] private bool _detectMoves = true;
    [ObservableProperty] private bool _collapseUnchanged;
    [ObservableProperty] private string _ignorePatterns = "";
    /// <summary>Protects the files: no typing, no copying between the sides, no saving (set for sessions opened from a folder compare with "Disable editing").</summary>
    [ObservableProperty] private bool _editingDisabled;

    /// <summary>One side protected (the /ro1 and /ro2 switches): it cannot be typed in, copied into or saved.</summary>
    [ObservableProperty] private bool _leftReadOnly;
    [ObservableProperty] private bool _rightReadOnly;

    /// <summary>Save into this file instead of the opened ones (the /savetarget switch).</summary>
    public string? SaveTarget { get; set; }

    /// <summary>Descriptions shown instead of the paths (the /title1 and /title2 switches).</summary>
    public string? LeftTitleOverride { get; set; }
    public string? RightTitleOverride { get; set; }

    private bool _leftFileLocked;
    private bool _rightFileLocked;

    /// <summary>True when the left side cannot be changed: editing is disabled, the side is protected, or the file is read-only on disk.</summary>
    public bool LeftLocked => EditingDisabled || LeftReadOnly || _leftFileLocked;

    public bool RightLocked => EditingDisabled || RightReadOnly || _rightFileLocked;

    /// <summary>Why a side cannot be edited, in words (null when it can).</summary>
    public string? LockReason(Side side)
    {
        var (locked, protectedOnCommandLine, file, name) = side == Side.Left ? (LeftLocked, LeftReadOnly, _leftFileLocked, "left") : (RightLocked, RightReadOnly, _rightFileLocked, "right");
        if (!locked) { return null; }
        if (EditingDisabled) { return "editing is disabled for this session"; }
        if (protectedOnCommandLine) { return $"the {name} side is protected (read-only)"; }
        return $"the {name} file is read-only on disk (or on read-only media)";
    }

    /// <summary>One line for the status bar when anything is locked, empty otherwise.</summary>
    public string LockText
    {
        get
        {
            if (EditingDisabled) { return "🔒 Editing is disabled for this session"; }
            var parts = new[] { (Side.Left, "Left"), (Side.Right, "Right") }.Where(t => LockReason(t.Item1) is not null).Select(t => $"{t.Item2}: {LockReason(t.Item1)}").ToList();
            return parts.Count == 0 ? "" : "🔒 " + string.Join(" · ", parts);
        }
    }

    private static bool IsReadOnlyOnDisk(string? path)
    {
        try
        {
            return path is not null && File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void RefreshLocks()
    {
        (_leftFileLocked, _rightFileLocked) = (IsReadOnlyOnDisk(Left.Path), IsReadOnlyOnDisk(Right.Path));
        OnPropertyChanged(nameof(LeftLocked));
        OnPropertyChanged(nameof(RightLocked));
        OnPropertyChanged(nameof(LockText));
    }

    partial void OnLeftReadOnlyChanged(bool value) => SidesProtectionChanged();
    partial void OnRightReadOnlyChanged(bool value) => SidesProtectionChanged();

    private void SidesProtectionChanged()
    {
        CopyToLeftCommand.NotifyCanExecuteChanged();
        CopyToRightCommand.NotifyCanExecuteChanged();
        SaveLeftCommand.NotifyCanExecuteChanged();
        SaveRightCommand.NotifyCanExecuteChanged();
        Rebuild();
    }

    partial void OnEditingDisabledChanged(bool value)
    {
        CopyToLeftCommand.NotifyCanExecuteChanged();
        CopyToRightCommand.NotifyCanExecuteChanged();
        SaveLeftCommand.NotifyCanExecuteChanged();
        SaveRightCommand.NotifyCanExecuteChanged();
        Rebuild();
    }

    [ObservableProperty] private bool _ignoreComments;
    [ObservableProperty] private bool _ignoreTimestamps;
    [ObservableProperty] private bool _ignoreBlankLines;
    [ObservableProperty] private LanguageChoice _language = LanguageChoice.Auto;

    public static IReadOnlyList<LanguageChoice> Languages { get; } = [LanguageChoice.Auto, .. CommentSyntaxes.All.Select(s => new LanguageChoice(s))];

    /// <summary>The comment syntax in effect: the chosen language, or the one the file names suggest; null when comments are not ignored or the language is unknown.</summary>
    public CommentSyntax? ActiveSyntax => !IgnoreComments ? null : Language.Syntax ?? CommentSyntaxes.ForPath(Left.Path) ?? CommentSyntaxes.ForPath(Right.Path);

    public static WhitespaceMode[] WhitespaceModes { get; } = Enum.GetValues<WhitespaceMode>();
    public static DiffAlgorithm[] Algorithms { get; } = Enum.GetValues<DiffAlgorithm>();

    // what the panes display (rebuilt after every comparison)
    public string LeftText { get; private set; } = "";
    public string RightText { get; private set; } = "";
    public IReadOnlyList<PaneRow> LeftRows { get; private set; } = [];
    public IReadOnlyList<PaneRow> RightRows { get; private set; } = [];
    /// <summary>Unchanged lines kept visible around every difference when collapsing.</summary>
    public int ContextLines => Math.Max(0, Prefs.TextEditing.ContextLines);

    /// <summary>A hidden stretch shorter than this is simply shown (a one-line marker would save nothing).</summary>
    public const int MinFold = 4;

    private readonly HashSet<int> _expandedFolds = [];

    /// <summary>Comparison rows (for the gutter and overview map) and groups, in the coordinates of what is displayed.</summary>
    public IReadOnlyList<DiffRow> DisplayRows { get; private set; } = [];
    public IReadOnlyList<DiffGroup> DisplayGroups { get; private set; } = [];

    /// <summary>For each displayed row, the comparison row it shows, or -1 for a collapsed-lines marker.</summary>
    public IReadOnlyList<int> DisplayMap { get; private set; } = [];

    public event EventHandler? DisplayChanged;

    /// <summary>Set by the view: commit any text the user is still typing before the model is changed by a command.</summary>
    public Action? FlushEdits { get; set; }

    public string LeftTitle => LeftTitleOverride ?? (Left.Path is null ? "(no file)" : PathDisplay.Short(Left.Path) + (Left.Dirty ? "  ●" : ""));
    public string? LeftFullPath => Left.Path;
    public string RightTitle => RightTitleOverride ?? (Right.Path is null ? "(no file)" : PathDisplay.Short(Right.Path) + (Right.Dirty ? "  ●" : ""));
    public string? RightFullPath => Right.Path;
    public string LeftInfo => Left.Path is null ? "" : $"{Left.EncodingLabel} · {Left.EolLabel} · {Left.Lines.Count} lines";
    public string RightInfo => Right.Path is null ? "" : $"{Right.EncodingLabel} · {Right.EolLabel} · {Right.Lines.Count} lines";
    public bool HasDifferences => Comparison is { AreEqual: false };
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool AnyDirty => Left.Dirty || Right.Dirty;
    public string DifferenceText => Comparison is null ? "" : Comparison.AreEqual ? "No differences" : $"Difference {Math.Max(CurrentGroup, 0) + 1} of {Comparison.DifferenceCount}";

    public DiffOptions Options => new()
    {
        Whitespace = Whitespace,
        IgnoreCase = IgnoreCase,
        Algorithm = Algorithm,
        DetectMoves = DetectMoves,
        IgnorePatterns = [.. IgnorePatterns.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
        IgnoreTimestamps = IgnoreTimestamps,
        IgnoreBlankLines = IgnoreBlankLines,
        Comments = ActiveSyntax,
    };

    partial void OnWhitespaceChanged(WhitespaceMode value) => Recompare();
    partial void OnIgnoreCaseChanged(bool value) => Recompare();
    partial void OnAlgorithmChanged(DiffAlgorithm value) => Recompare();
    partial void OnDetectMovesChanged(bool value) => Recompare();

    partial void OnCollapseUnchangedChanged(bool value)
    {
        _expandedFolds.Clear();
        FlushEdits?.Invoke();
        Rebuild();
    }

    /// <summary>Show the lines behind a collapsed-lines marker (<paramref name="displayRow"/> is a 0-based displayed row).</summary>
    public void ExpandFold(int displayRow)
    {
        if (displayRow >= 0 && displayRow < DisplayMap.Count && DisplayMap[displayRow] < 0 && LeftRows.Count > displayRow && LeftRows[displayRow].FoldCount > 0)
        {
            _expandedFolds.Add(FoldStart(displayRow));
            Rebuild();
        }
    }

    private readonly Dictionary<int, int> _foldStarts = [];

    private int FoldStart(int displayRow) => _foldStarts[displayRow];
    partial void OnIgnorePatternsChanged(string value) => Recompare();
    partial void OnIgnoreCommentsChanged(bool value) => Recompare();
    partial void OnIgnoreTimestampsChanged(bool value) => Recompare();
    partial void OnIgnoreBlankLinesChanged(bool value) => Recompare();
    partial void OnLanguageChanged(LanguageChoice value) => Recompare();

    // ───────────── loading ─────────────

    public void Open(string leftPath, string rightPath)
    {
        try
        {
            Error = null;
            Left.Load(leftPath);
            Right.Load(rightPath);
            _undo.Clear();
            _redo.Clear();
            Recompare(goToFirst: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
    }

    public void OpenText(string? leftPath, IEnumerable<string> left, string? rightPath, IEnumerable<string> right)
    {
        Left.SetText(leftPath, left);
        Right.SetText(rightPath, right);
        _undo.Clear();
        _redo.Clear();
        Recompare(goToFirst: true);
    }

    /// <summary>Set by the shell: opens a tab with the given (already formatted) lines.</summary>
    public Action<string, IReadOnlyList<string>, IReadOnlyList<string>>? OpenFormattedRequested { get; set; }

    /// <summary>JSON and XML: compare the structure. Both files are rewritten into one canonical layout and opened in a new tab.</summary>
    [RelayCommand]
    private void CompareStructure()
    {
        FlushEdits?.Invoke();
        var format = StructuralNormalizer.FormatFor(Left.Path) is var f && f != StructuredFormat.None ? f : StructuralNormalizer.FormatFor(Right.Path);
        if (format == StructuredFormat.None)
        {
            Error = "Structure compare works on JSON and XML files.";
            return;
        }

        var l = StructuralNormalizer.Normalize(string.Join('\n', Left.Lines), format);
        var r = StructuralNormalizer.Normalize(string.Join('\n', Right.Lines), format);
        if (l is null || r is null)
        {
            Error = $"Cannot compare the structure: the {(l is null ? "left" : "right")} file is not valid {format.ToString().ToUpperInvariant()}.";
            return;
        }

        Error = null;
        OpenFormattedRequested?.Invoke($"{System.IO.Path.GetFileName(Left.Path ?? Right.Path)} (structure)", l, r);
    }

    [RelayCommand]
    private void Reload()
    {
        if (Left.Path is { } l && Right.Path is { } r)
        {
            Open(l, r);
        }
    }

    // ───────────── comparison and display model ─────────────

    public void Recompare(bool goToFirst = false)
    {
        if (Left.IsBinary || Right.IsBinary)
        {
            Comparison = null;
            Error = "One of the files looks binary. Text compare shows text only.";
            Rebuild();
            return;
        }

        try
        {
            Comparison = TextComparison.Compare(Left.Lines, Right.Lines, Options);
            Error = IgnoreComments && ActiveSyntax is null ? "Comments: the language of these files is unknown. Choose one in the language list." : null;
        }
        catch (System.Text.RegularExpressions.RegexParseException ex)
        {
            Error = "Invalid ignore pattern: " + ex.Message;
            Comparison = TextComparison.Compare(Left.Lines, Right.Lines, Options with { IgnorePatterns = [] });
        }

        var count = Comparison.DifferenceCount;
        _item = -1;
        CurrentGroup = count == 0 ? -1 : goToFirst ? (Prefs.NextDifference.GoToFirstOnLoad ? 0 : -1) : Math.Min(Math.Max(CurrentGroup, 0), count - 1);
        Rebuild();
    }

    /// <summary>Which comparison rows to show: all of them, or (when collapsing) the differences with some context around them.</summary>
    private List<(int Row, int Fold)> PlanDisplay(TextComparison c)
    {
        var plan = new List<(int, int)>(c.Rows.Count);
        if (!CollapseUnchanged || c.DifferenceCount == 0)
        {
            for (var i = 0; i < c.Rows.Count; i++) { plan.Add((i, 0)); }
            return plan;
        }

        var keep = new bool[c.Rows.Count];
        foreach (var g in c.Groups)
        {
            for (var i = Math.Max(0, g.FirstRow - ContextLines); i < Math.Min(keep.Length, g.FirstRow + g.RowCount + ContextLines); i++) { keep[i] = true; }
        }

        for (var i = 0; i < keep.Length;)
        {
            if (keep[i]) { plan.Add((i, 0)); i++; continue; }
            var j = i;
            while (j < keep.Length && !keep[j]) { j++; }
            var length = j - i;
            if (length < MinFold || _expandedFolds.Contains(i))
            {
                for (var k = i; k < j; k++) { plan.Add((k, 0)); }
            }
            else
            {
                plan.Add((i, length));          // one marker standing for rows i .. j-1
            }

            i = j;
        }

        return plan;
    }

    private void Rebuild()
    {
        RefreshLocks();
        var c = Comparison;
        var l = new StringBuilder();
        var r = new StringBuilder();
        var lrows = new List<PaneRow>();
        var rrows = new List<PaneRow>();
        var map = new List<int>();
        var displayRows = new List<DiffRow>();
        var firstDisplay = new Dictionary<int, int>();   // comparison row -> display row
        _foldStarts.Clear();

        if (c is not null)
        {
            var plan = PlanDisplay(c);
            for (var d = 0; d < plan.Count; d++)
            {
                var (i, fold) = plan[d];
                if (d > 0) { l.Append('\n'); r.Append('\n'); }

                if (fold > 0)
                {
                    var text = $"⋯  {fold:N0} unchanged line{(fold == 1 ? "" : "s")}  ⋯  (click to show)";
                    l.Append(text); r.Append(text);
                    var marker = new PaneRow(RowKind.Same, 0, true, -1, [], false, fold);
                    lrows.Add(marker); rrows.Add(marker);
                    map.Add(-1);
                    displayRows.Add(new DiffRow(RowKind.Same, -1, -1, -1));
                    _foldStarts[d] = i;
                    continue;
                }

                var row = c.Rows[i];
                var (lw, rw) = row.Kind == RowKind.Changed ? c.IntraLine(i) : ([], []);
                l.Append(row.Left >= 0 ? c.LeftLines[row.Left] : "");
                r.Append(row.Right >= 0 ? c.RightLines[row.Right] : "");
                lrows.Add(new PaneRow(row.Kind, row.Left + 1, row.Left < 0, row.Group, lw, row.IsMoved));
                rrows.Add(new PaneRow(row.Kind, row.Right + 1, row.Right < 0, row.Group, rw, row.IsMoved));
                map.Add(i);
                displayRows.Add(row);
                firstDisplay[i] = d;
            }

            DisplayGroups = [.. c.Groups.Select(g => g with { FirstRow = firstDisplay.GetValueOrDefault(g.FirstRow, 0) })];
        }
        else
        {
            DisplayGroups = [];
        }

        LeftText = l.ToString();
        RightText = r.ToString();
        LeftRows = lrows;
        RightRows = rrows;
        DisplayRows = displayRows;
        DisplayMap = map;
        NotifyAll();
        DisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LeftTitle));
        OnPropertyChanged(nameof(RightTitle));
        OnPropertyChanged(nameof(LeftFullPath));
        OnPropertyChanged(nameof(RightFullPath));
        OnPropertyChanged(nameof(LeftInfo));
        OnPropertyChanged(nameof(RightInfo));
        OnPropertyChanged(nameof(HasDifferences));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(AnyDirty));
        OnPropertyChanged(nameof(DifferenceText));
        Status = Comparison is null ? Status : Comparison.AreEqual ? "Files are identical" : $"{Comparison.DifferenceCount} difference{(Comparison.DifferenceCount == 1 ? "" : "s")}" + (Comparison.Moves.Count > 0 ? $" · {Comparison.Moves.Count} moved block{(Comparison.Moves.Count == 1 ? "" : "s")}" : "");
        NextDifferenceCommand.NotifyCanExecuteChanged();
        PreviousDifferenceCommand.NotifyCanExecuteChanged();
        CopyToRightCommand.NotifyCanExecuteChanged();
        CopyToLeftCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        SaveLeftCommand.NotifyCanExecuteChanged();
        SaveRightCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentGroupChanged(int value) => OnPropertyChanged(nameof(DifferenceText));

    // ───────────── navigation ─────────────

    private bool HasAny => Comparison is { DifferenceCount: > 0 };

    [RelayCommand(CanExecute = nameof(HasAny))]
    private void NextDifference() => MoveGroup(+1);

    [RelayCommand(CanExecute = nameof(HasAny))]
    private void PreviousDifference() => MoveGroup(-1);

    /// <summary>
    /// Set for a compare opened from a folder session: gives the next (+1) or previous (-1) pair of files that differ, so the user can
    /// walk through the files of the folder without going back to it.
    /// </summary>
    public Func<int, (string Left, string Right)?>? FileNavigator { get; set; }

    public bool HasFileNavigator => FileNavigator is not null;

    private void MoveFile(int step)
    {
        FlushEdits?.Invoke();
        if (FileNavigator?.Invoke(step) is { } pair)
        {
            Open(pair.Left, pair.Right);
            Notice = "";
        }
        else
        {
            Notice = Prefs.NextDifference.ShowMessagePanel ? "No more files with differences" : "";
        }
    }

    [RelayCommand]
    private void NextFile() => MoveFile(+1);

    [RelayCommand]
    private void PreviousFile() => MoveFile(-1);

    /// <summary>A message about the last navigation (no more differences, wrapped around…); empty when there is none.</summary>
    [ObservableProperty] private string _notice = "";

    /// <summary>Steps to the next or previous difference section as the next-difference preferences say: wrap around, or stop with a message.</summary>
    private void MoveGroup(int step)
    {
        var count = Comparison!.DifferenceCount;
        var next = CurrentGroup + step;
        var nd = Prefs.NextDifference;
        if (next >= 0 && next < count)
        {
            CurrentGroup = next;
            Notice = "";
        }
        else if (nd.WrapAround)
        {
            CurrentGroup = (next + count) % count;
            Notice = step > 0 ? "Wrapped to the first difference" : "Wrapped to the last difference";
        }
        else
        {
            Notice = nd.ShowMessagePanel ? (step > 0 ? "No more differences below" : "No more differences above") : "";
        }
    }

    // ───────────── individual differences (inside a line) ─────────────

    /// <summary>One thing that differs: a changed word of a line, or a whole line that exists on one side only.</summary>
    public readonly record struct DifferenceItem(int Row, Side Side, int Start, int Length, int Group);

    /// <summary>How many unchanged lines the collapsed view hides, as text for the status bar (empty when the preference or the collapse is off).</summary>
    public string FilteredLinesText
    {
        get
        {
            if (!Prefs.TextEditing.ShowFilteredLineCounts || !CollapseUnchanged) { return ""; }
            var hidden = LeftRows.Where(r => r.FoldCount > 0).Sum(r => r.FoldCount);
            return hidden == 0 ? "" : $"{hidden:N0} unchanged line{(hidden == 1 ? "" : "s")} hidden";
        }
    }

    /// <summary>The comparison row's position among the displayed rows (they differ while unchanged text is folded), or -1.</summary>
    public int DisplayRowOfComparisonRow(int row)
    {
        for (var d = 0; d < DisplayMap.Count; d++)
        {
            if (DisplayMap[d] == row) { return d; }
        }

        return -1;
    }

    /// <summary>Raised when the cursor should go to an individual difference.</summary>
    public event Action<DifferenceItem>? ItemFocusRequested;

    private int _item = -1;

    /// <summary>All the words and lines that differ, in reading order.</summary>
    public List<DifferenceItem> DifferenceItems()
    {
        var items = new List<DifferenceItem>();
        if (Comparison is not { } c) { return items; }
        for (var i = 0; i < c.Rows.Count; i++)
        {
            var row = c.Rows[i];
            switch (row.Kind)
            {
                case RowKind.Changed:
                    var (lw, rw) = c.IntraLine(i);
                    if (lw.Count == 0 && rw.Count == 0) { items.Add(new DifferenceItem(i, Side.Left, 0, c.LeftLines[row.Left].Length, row.Group)); }
                    foreach (var w in lw) { items.Add(new DifferenceItem(i, Side.Left, w.Start, w.End - w.Start, row.Group)); }
                    if (lw.Count == 0) { foreach (var w in rw) { items.Add(new DifferenceItem(i, Side.Right, w.Start, w.End - w.Start, row.Group)); } }
                    break;
                case RowKind.LeftOnly:
                    items.Add(new DifferenceItem(i, Side.Left, 0, c.LeftLines[row.Left].Length, row.Group));
                    break;
                case RowKind.RightOnly:
                    items.Add(new DifferenceItem(i, Side.Right, 0, c.RightLines[row.Right].Length, row.Group));
                    break;
            }
        }

        return items;
    }

    private bool CanStepItems => Comparison is { AreEqual: false };

    [RelayCommand(CanExecute = nameof(CanStepItems))]
    private void NextItem() => MoveItem(+1);

    [RelayCommand(CanExecute = nameof(CanStepItems))]
    private void PreviousItem() => MoveItem(-1);

    private void MoveItem(int step)
    {
        var items = DifferenceItems();
        if (items.Count == 0) { return; }
        var next = _item + step;
        if (_item < 0 && CurrentGroup >= 0)
        {
            // start from the current section when the user has not stepped through items yet
            next = step > 0 ? items.FindIndex(t => t.Group >= CurrentGroup) : items.FindLastIndex(t => t.Group <= CurrentGroup);
            if (next < 0) { next = step > 0 ? 0 : items.Count - 1; }
        }

        if (next < 0 || next >= items.Count)
        {
            if (!Prefs.NextDifference.WrapAround)
            {
                Notice = Prefs.NextDifference.ShowMessagePanel ? "No more differences" : "";
                return;
            }

            next = (next + items.Count) % items.Count;
            Notice = "Wrapped around";
        }
        else
        {
            Notice = "";
        }

        _item = next;
        var item = items[next];
        CurrentGroup = item.Group;
        ItemFocusRequested?.Invoke(item);
    }

    /// <summary>First visual row of a difference, for scrolling.</summary>
    public int RowOfGroup(int group) => group >= 0 && group < DisplayGroups.Count ? DisplayGroups[group].FirstRow : 0;

    /// <summary>
    /// Makes a (0-based) line of a side visible, expanding the collapsed stretch that hides it if necessary, and returns the
    /// displayed row, or -1 when the line does not exist.
    /// </summary>
    public int RevealLine(Side side, int line)
    {
        if (Comparison is not { } c || line < 0 || line >= (side == Side.Left ? c.LeftLines.Count : c.RightLines.Count)) { return -1; }
        var shown = DisplayRowOfLine(side, line);
        if (shown >= 0) { return shown; }

        // the line is inside a fold: find the comparison row it belongs to and open that stretch
        var row = c.Rows.Select((r, i) => (r, i)).First(t => (side == Side.Left ? t.r.Left : t.r.Right) == line).i;
        var fold = _foldStarts.Where(f => f.Value <= row).OrderByDescending(f => f.Value).FirstOrDefault();
        if (_foldStarts.Count > 0) { _expandedFolds.Add(fold.Value); }
        Rebuild();
        return DisplayRowOfLine(side, line);
    }

    /// <summary>The displayed row showing a 0-based line of a side, or -1 when that line is hidden in a collapsed region.</summary>
    public int DisplayRowOfLine(Side side, int line)
    {
        if (Comparison is not { } c) { return -1; }
        for (var d = 0; d < DisplayMap.Count; d++)
        {
            if (DisplayMap[d] < 0) { continue; }
            var row = c.Rows[DisplayMap[d]];
            if ((side == Side.Left ? row.Left : row.Right) == line) { return d; }
        }

        return -1;
    }

    // ───────────── editing operations ─────────────

    private void PushUndo()
    {
        _undo.Push(([.. Left.Lines], [.. Right.Lines], Left.Dirty, Right.Dirty));
        while (_undo.Count > UndoLimit)
        {
            var keep = _undo.Reverse().Skip(1).ToList();
            _undo.Clear();
            foreach (var k in keep) { _undo.Push(k); }
        }

        _redo.Clear();
    }

    /// <summary>Copies the lines of one difference from one side over the other side's version of it.</summary>
    public void CopyGroup(int group, Side from)
    {
        if (from == Side.Left ? RightLocked : LeftLocked) { return; }
        FlushEdits?.Invoke();
        if (Comparison is not { } c || group < 0 || group >= c.Groups.Count)
        {
            return;
        }

        var g = c.Groups[group];
        PushUndo();
        if (from == Side.Left)
        {
            Right.Lines.RemoveRange(g.RightStart, g.RightCount);
            Right.Lines.InsertRange(g.RightStart, Left.Lines.Skip(g.LeftStart).Take(g.LeftCount));
            Right.Dirty = true;
        }
        else
        {
            Left.Lines.RemoveRange(g.LeftStart, g.LeftCount);
            Left.Lines.InsertRange(g.LeftStart, Right.Lines.Skip(g.RightStart).Take(g.RightCount));
            Left.Dirty = true;
        }

        Recompare();
        // the copied difference is gone, so the same number now names the next one; without the preference stay where we are
        if (!Prefs.NextDifference.GoToNextAfterCopy) { CurrentGroup = -1; }
    }

    /// <summary>Set by the view: ask for a line and scroll to it.</summary>
    public Func<Task>? GoToLineRequested { get; set; }

    [RelayCommand]
    private Task GoToLine() => GoToLineRequested?.Invoke() ?? Task.CompletedTask;

    private bool CanCopyToRight => HasAny && CurrentGroup >= 0 && !RightLocked;
    private bool CanCopyToLeft => HasAny && CurrentGroup >= 0 && !LeftLocked;

    [RelayCommand(CanExecute = nameof(CanCopyToRight))]
    private void CopyToRight() => CopyGroup(CurrentGroup, Side.Left);

    [RelayCommand(CanExecute = nameof(CanCopyToLeft))]
    private void CopyToLeft() => CopyGroup(CurrentGroup, Side.Right);

    /// <summary>Applies text the user typed in one or both panes as a single undoable step with one re-comparison.</summary>
    public void ApplyEdits(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (LeftLocked) { left = null; }
        if (RightLocked) { right = null; }
        if (left is null && right is null)
        {
            return;
        }

        PushUndo();
        if (left is not null) { Left.Lines = [.. left]; Left.Dirty = true; }
        if (right is not null) { Right.Lines = [.. right]; Right.Dirty = true; }
        Recompare();
    }

    /// <summary>Replace a whole side's text (used when leaving the free-edit mode).</summary>
    public void ReplaceLines(Side side, IEnumerable<string> lines)
    {
        PushUndo();
        var target = side == Side.Left ? Left : Right;
        target.Lines = [.. lines];
        target.Dirty = true;
        Recompare();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        FlushEdits?.Invoke();
        if (_undo.Count == 0) { return; }
        _redo.Push(([.. Left.Lines], [.. Right.Lines], Left.Dirty, Right.Dirty));
        Restore(_undo.Pop());
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        FlushEdits?.Invoke();
        if (_redo.Count == 0) { return; }
        _undo.Push(([.. Left.Lines], [.. Right.Lines], Left.Dirty, Right.Dirty));
        Restore(_redo.Pop());
    }

    private void Restore((List<string> Left, List<string> Right, bool LDirty, bool RDirty) s)
    {
        Left.Lines = s.Left;
        Right.Lines = s.Right;
        Left.Dirty = s.LDirty;
        Right.Dirty = s.RDirty;
        Recompare();
    }

    [RelayCommand]
    private void Swap()
    {
        FlushEdits?.Invoke();
        (Left.Path, Right.Path) = (Right.Path, Left.Path);
        (Left.Lines, Right.Lines) = (Right.Lines, Left.Lines);
        (Left.Eol, Right.Eol) = (Right.Eol, Left.Eol);
        (Left.EndsWithNewline, Right.EndsWithNewline) = (Right.EndsWithNewline, Left.EndsWithNewline);
        (Left.Encoding, Right.Encoding) = (Right.Encoding, Left.Encoding);
        (Left.HasBom, Right.HasBom) = (Right.HasBom, Left.HasBom);
        (Left.Dirty, Right.Dirty) = (Right.Dirty, Left.Dirty);
        Recompare(goToFirst: true);
    }

    private bool CanSaveLeft => Left.Dirty && (Left.Path is not null || SaveTarget is not null) && !LeftLocked;
    private bool CanSaveRight => Right.Dirty && (Right.Path is not null || SaveTarget is not null) && !RightLocked;

    [RelayCommand(CanExecute = nameof(CanSaveLeft))]
    private void SaveLeft() => Save(Left);

    [RelayCommand(CanExecute = nameof(CanSaveRight))]
    private void SaveRight() => Save(Right);

    private void Save(TextSide side)
    {
        FlushEdits?.Invoke();
        try
        {
            if (SaveTarget is { } target) { side.SaveTo(target, Backup); } else { side.Save(null, Backup); }
            Error = null;
            NotifyAll();
            Status = $"Saved {SaveTarget ?? side.Path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
    }
}

/// <summary>One entry of the language list: automatic (from the file name) or a specific comment syntax.</summary>
public sealed class LanguageChoice(CommentSyntax? syntax)
{
    public static LanguageChoice Auto { get; } = new(null);

    public CommentSyntax? Syntax { get; } = syntax;

    public string Label => Syntax is null ? "Language: auto" : Syntax.DisplayName;

    public override string ToString() => Label;
}
