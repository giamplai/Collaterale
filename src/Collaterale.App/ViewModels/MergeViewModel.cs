using System.Text;
using Collaterale.App.Controls;
using Collaterale.Core.Diff;
using Collaterale.Core.Merge;
using Collaterale.Core.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collaterale.App.ViewModels;

public enum MergeRole { Base, Left, Right }

public enum MergeChoice { Left, Right, Base, LeftThenRight, RightThenLeft }

/// <summary>Which sections of the merge are shown; the others fold into a single marker line.</summary>
public enum MergeFilter { All, Differences, Same, Conflicts, Left, Center, Right }

/// <summary>Where "Save" writes the merged text.</summary>
public enum MergeTarget { Other, Left, Right }

public sealed partial class MergeViewModel : ObservableObject, ISessionContent
{
    private readonly Dictionary<int, MergeChoice> _decisions = [];
    private List<int> _changes = [];

    public TextSide Base { get; } = new();
    public TextSide Left { get; } = new();
    public TextSide Right { get; } = new();

    public ThreeWayMerge? Merge { get; private set; }

    /// <summary>The program's preferences; a fresh set until the shell supplies the real one.</summary>
    public Collaterale.Core.Settings.Preferences Prefs { get; set; } = new();

    /// <summary>The /nobackups switch.</summary>
    public bool NoBackups { get; set; }

    [ObservableProperty] private string? _outputPath;
    [ObservableProperty] private int _currentChange = -1;
    [ObservableProperty] private bool _resultEdited;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _status = "Open the base, left and right versions to merge";
    [ObservableProperty] private WhitespaceMode _whitespace = WhitespaceMode.Exact;
    [ObservableProperty] private bool _ignoreCase;
    [ObservableProperty] private bool _showBase = true;
    [ObservableProperty] private MergeFilter _filter = MergeFilter.All;
    [ObservableProperty] private MergeTarget _target = MergeTarget.Other;
    [ObservableProperty] private double _loadSeconds;

    private readonly HashSet<int> _expanded = [];

    public static MergeFilter[] Filters { get; } = Enum.GetValues<MergeFilter>();

    partial void OnFilterChanged(MergeFilter value)
    {
        _expanded.Clear();
        if (Merge is not null) { BuildDisplay(); }
    }

    partial void OnTargetChanged(MergeTarget value)
    {
        OnPropertyChanged(nameof(TargetPath));
        OnPropertyChanged(nameof(OutputTitle));
        OnPropertyChanged(nameof(NeedsOutputPath));
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The file "Save" writes: the left or right input, or the separate output file.</summary>
    public string? TargetPath => Target switch { MergeTarget.Left => Left.Path, MergeTarget.Right => Right.Path, _ => OutputPath };

    /// <summary>Unfolds a section the filter had hidden (the marker line was clicked).</summary>
    public void ExpandRegion(int row)
    {
        if (row >= 0 && row < LeftRows.Count && LeftRows[row].FoldCount > 0)
        {
            _expanded.Add(LeftRows[row].Group);
            BuildDisplay();
        }
    }

    private bool Matches(MergeRegion r) => Filter switch
    {
        MergeFilter.All => true,
        MergeFilter.Differences => r.Kind != MergeRegionKind.Stable,
        MergeFilter.Same => r.Kind == MergeRegionKind.Stable,
        MergeFilter.Conflicts => r.Kind == MergeRegionKind.Conflict,
        MergeFilter.Left => r.Kind == MergeRegionKind.LeftChanged,
        MergeFilter.Right => r.Kind == MergeRegionKind.RightChanged,
        _ => r.Kind == MergeRegionKind.BothSame,
    };

    private static (MergeTint Left, MergeTint Base, MergeTint Right) Tints(MergeRegionKind kind) => kind switch
    {
        MergeRegionKind.LeftChanged => (MergeTint.Left, MergeTint.Base, MergeTint.None),
        MergeRegionKind.RightChanged => (MergeTint.None, MergeTint.Base, MergeTint.Right),
        MergeRegionKind.BothSame => (MergeTint.Left, MergeTint.Base, MergeTint.Right),
        MergeRegionKind.Conflict => (MergeTint.Conflict, MergeTint.Conflict, MergeTint.Conflict),
        _ => (MergeTint.None, MergeTint.None, MergeTint.None),
    };

    /// <summary>What kind of section the cursor is on, in words (shown in the status bar).</summary>
    public string CurrentDescription => Merge is null || CurrentRegion < 0 ? "" : Merge.Regions[CurrentRegion].Kind switch
    {
        MergeRegionKind.LeftChanged => "Important Left Change",
        MergeRegionKind.RightChanged => "Important Right Change",
        MergeRegionKind.BothSame => "The same change on both sides",
        MergeRegionKind.Conflict => _decisions.ContainsKey(CurrentRegion) ? "Conflict (resolved)" : "Conflict: Important Left and Right Changes",
        _ => "",
    };

    public bool HasUnresolved => UnresolvedCount > 0;
    public bool AllResolved => HasMerge && UnresolvedCount == 0;

    public string ConflictSections => Merge is null ? "" : $"{UnresolvedCount} conflict section(s)";

    public string LoadTimeText => LoadSeconds > 0 ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Load time: {LoadSeconds:0.00} seconds") : "";

    public string LeftInfo => Info(Left);
    public string BaseInfo => Info(Base);
    public string RightInfo => Info(Right);

    private static string Info(TextSide side)
    {
        if (side.Path is null || !File.Exists(side.Path)) { return side.Lines.Count == 0 ? "" : $"{side.Lines.Count:N0} lines"; }
        var f = new FileInfo(side.Path);
        return $"{f.LastWriteTime:g}   {f.Length:N0} bytes   {side.EncodingLabel}   {side.EolLabel}";
    }

    /// <summary>True once the merged text has been written to <see cref="OutputPath"/> with no conflicts left.</summary>
    [ObservableProperty] private bool _completed;

    public string Title => OutputPath is { } o ? $"Merge  {System.IO.Path.GetFileName(o)}" : "Merge";

    // what the panes show
    public string LeftText { get; private set; } = "";
    public string BaseText { get; private set; } = "";
    public string RightText { get; private set; } = "";
    public string ResultText { get; private set; } = "";
    public IReadOnlyList<PaneRow> LeftRows { get; private set; } = [];
    public IReadOnlyList<PaneRow> BaseRows { get; private set; } = [];
    public IReadOnlyList<PaneRow> RightRows { get; private set; } = [];
    public IReadOnlyList<PaneRow> ResultRows { get; private set; } = [];
    public IReadOnlyList<int> ResultStart { get; private set; } = [];
    public IReadOnlyList<int> RegionRowStart { get; private set; } = [];
    public event EventHandler? DisplayChanged;

    public int ConflictCount => Merge?.ConflictCount ?? 0;
    public int UnresolvedCount => Merge is null ? 0 : Merge.Regions.Select((r, i) => (r, i)).Count(t => t.r.Kind == MergeRegionKind.Conflict && !_decisions.ContainsKey(t.i));
    public int ChangeCount => _changes.Count;
    public bool HasMerge => Merge is not null;
    public bool CanResolve => HasMerge && CurrentChange >= 0 && !ResultEdited;

    public string ConflictText => Merge is null ? "" : ConflictCount == 0 ? "No conflicts" : UnresolvedCount == 0 ? $"All {ConflictCount} conflicts resolved" : $"{UnresolvedCount} of {ConflictCount} conflicts unresolved";
    public string ChangeText => Merge is null ? "" : ChangeCount == 0 ? "No changes" : $"Change {Math.Max(CurrentChange, 0) + 1} of {ChangeCount}";
    /// <summary>Descriptions shown instead of the paths (the /title1 … /title4 switches): left, right, center, output.</summary>
    public string?[] TitleOverrides { get; set; } = [null, null, null, null];

    public string LeftTitle => TitleOverrides[0] ?? PathDisplay.Short(Left.Path, "(left)");
    public string RightTitle => TitleOverrides[1] ?? PathDisplay.Short(Right.Path, "(right)");
    public string BaseTitle => TitleOverrides[2] ?? PathDisplay.Short(Base.Path, "(no base: merging against an empty file)");
    public string OutputTitle => TitleOverrides[3] ?? PathDisplay.Short(TargetPath, "(unsaved result)");

    public DiffOptions Options => new() { Whitespace = Whitespace, IgnoreCase = IgnoreCase };

    /// <summary>Starts from the choices remembered in the settings and writes every later change back (whitespace and case are shared with the text compare).</summary>
    public void ApplyDefaults(Collaterale.Core.Settings.AppSettings s, Action? save = null)
    {
        _remembering = true;
        Whitespace = s.Whitespace;
        IgnoreCase = s.IgnoreCase;
        ShowBase = s.MergeView.ShowBase;
        Filter = Enum.TryParse<MergeFilter>(s.MergeView.Filter, out var f) ? f : MergeFilter.All;
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
        switch (e.PropertyName)
        {
            case nameof(Whitespace): s.Whitespace = Whitespace; break;
            case nameof(IgnoreCase): s.IgnoreCase = IgnoreCase; break;
            case nameof(ShowBase): s.MergeView.ShowBase = ShowBase; break;
            case nameof(Filter): s.MergeView.Filter = Filter.ToString(); break;
            default: return;
        }

        _saveSettings?.Invoke();
    }

    partial void OnWhitespaceChanged(WhitespaceMode value) => Recompute(keepDecisions: false);
    partial void OnIgnoreCaseChanged(bool value) => Recompute(keepDecisions: false);
    partial void OnCurrentChangeChanged(int value) => NotifyState();
    partial void OnResultEditedChanged(bool value) => NotifyState();

    // ───────────── loading ─────────────

    private string? _pendingBase, _pendingLeft, _pendingRight;

    /// <summary>Choose one of the inputs from the UI; the merge starts as soon as left and right are known (base is optional).</summary>
    public void SetSource(MergeRole role, string path)
    {
        switch (role)
        {
            case MergeRole.Base: _pendingBase = path; break;
            case MergeRole.Left: _pendingLeft = path; break;
            default: _pendingRight = path; break;
        }

        if (_pendingLeft is not null && _pendingRight is not null)
        {
            Open(_pendingBase, _pendingLeft, _pendingRight, OutputPath);
        }
        else
        {
            Status = _pendingLeft is null ? "Now choose the left version" : "Now choose the right version";
        }
    }

    /// <summary>Clears the base so the merge runs against an empty ancestor.</summary>
    public void ClearBase()
    {
        _pendingBase = null;
        if (_pendingLeft is not null && _pendingRight is not null)
        {
            Open(null, _pendingLeft, _pendingRight, OutputPath);
        }
    }

    public void Open(string? basePath, string leftPath, string rightPath, string? outputPath)
    {
        (_pendingBase, _pendingLeft, _pendingRight) = (basePath, leftPath, rightPath);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Error = null;
            if (basePath is not null) { Base.Load(basePath); } else { Base.SetText(null, []); }
            Left.Load(leftPath);
            Right.Load(rightPath);
            OutputPath = outputPath;
            if (Base.IsBinary || Left.IsBinary || Right.IsBinary)
            {
                Error = "One of the files looks binary. Merge handles text only.";
                return;
            }

            Recompute(keepDecisions: false, goToFirst: true);
            LoadSeconds = clock.Elapsed.TotalSeconds;
            NotifyState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
    }

    public void OpenText(IEnumerable<string> baseLines, IEnumerable<string> left, IEnumerable<string> right, string? outputPath = null)
    {
        Base.SetText(null, baseLines);
        Left.SetText(null, left);
        Right.SetText(null, right);
        OutputPath = outputPath;
        Recompute(keepDecisions: false, goToFirst: true);
    }

    // ───────────── merge and display ─────────────

    private void Recompute(bool keepDecisions, bool goToFirst = false)
    {
        if (Left.Path is null && Left.Lines.Count == 0 && Right.Lines.Count == 0 && Base.Lines.Count == 0)
        {
            return;
        }

        if (!keepDecisions) { _decisions.Clear(); }
        ResultEdited = false;
        Merge = ThreeWayMerge.Merge(Base.Lines, Left.Lines, Right.Lines, Options);
        _changes = [.. Merge.Regions.Select((r, i) => (r, i)).Where(t => t.r.Kind != MergeRegionKind.Stable).Select(t => t.i)];

        var firstUnresolved = _changes.FindIndex(i => Merge.Regions[i].Kind == MergeRegionKind.Conflict && !_decisions.ContainsKey(i));
        CurrentChange = _changes.Count == 0 ? -1 : goToFirst ? Math.Max(firstUnresolved, 0) : Math.Min(Math.Max(CurrentChange, 0), _changes.Count - 1);
        BuildDisplay();
    }

    /// <summary>The region currently selected for navigation and resolution, or -1.</summary>
    public int CurrentRegion => CurrentChange >= 0 && CurrentChange < _changes.Count ? _changes[CurrentChange] : -1;

    private MergeChoice? Decision(int region) => _decisions.TryGetValue(region, out var c) ? c : null;

    private IEnumerable<string> ResolvedLines(int i, MergeRegion r, out bool unresolvedConflict)
    {
        unresolvedConflict = false;
        var m = Merge!;
        if (Decision(i) is { } c)
        {
            var res = c switch
            {
                MergeChoice.Left => Resolution.Left,
                MergeChoice.Right => Resolution.Right,
                MergeChoice.Base => Resolution.Base,
                MergeChoice.LeftThenRight => Resolution.LeftThenRight,
                _ => Resolution.RightThenLeft,
            };
            return m.RegionLines(r, res);
        }

        if (r.Kind == MergeRegionKind.Conflict)
        {
            unresolvedConflict = true;
            return [$"<<<<<<< {System.IO.Path.GetFileName(Left.Path) ?? "left"}", .. m.RegionLines(r, Resolution.Left), "=======", .. m.RegionLines(r, Resolution.Right), $">>>>>>> {System.IO.Path.GetFileName(Right.Path) ?? "right"}"];
        }

        return m.RegionLines(r);
    }

    private void BuildDisplay()
    {
        var m = Merge!;
        StringBuilder l = new(), b = new(), r = new(), res = new();
        List<PaneRow> lr = [], br = [], rr = [], resRows = [];
        List<int> resultStart = [], rowStart = [];
        var first = true;
        var resLine = 0;

        for (var i = 0; i < m.Regions.Count; i++)
        {
            var reg = m.Regions[i];
            var n = Math.Max(reg.LeftLength, Math.Max(reg.BaseLength, reg.RightLength));
            var (lk, bk, rk) = reg.Kind switch
            {
                MergeRegionKind.LeftChanged => (RowKind.Changed, RowKind.Same, RowKind.Same),
                MergeRegionKind.RightChanged => (RowKind.Same, RowKind.Same, RowKind.Changed),
                MergeRegionKind.BothSame => (RowKind.Changed, RowKind.LeftOnly, RowKind.Changed),
                MergeRegionKind.Conflict => (RowKind.Conflict, RowKind.Conflict, RowKind.Conflict),
                _ => (RowKind.Same, RowKind.Same, RowKind.Same),
            };

            rowStart.Add(lr.Count);
            var (lt, bt, rt) = Tints(reg.Kind);
            if (!Matches(reg) && !_expanded.Contains(i) && n > 0)
            {
                if (!first) { l.Append('\n'); b.Append('\n'); r.Append('\n'); }
                first = false;
                var marker = $"⋯  {n:N0} line{(n == 1 ? "" : "s")} hidden by the filter  ⋯  (click to show)";
                l.Append(marker); b.Append(marker); r.Append(marker);
                var fold = new PaneRow(RowKind.Same, 0, true, i, [], false, n);
                lr.Add(fold); br.Add(fold); rr.Add(fold);
            }
            else
            {
                for (var k = 0; k < n; k++)
                {
                    if (!first) { l.Append('\n'); b.Append('\n'); r.Append('\n'); }
                    first = false;
                    bool lf = k >= reg.LeftLength, bf = k >= reg.BaseLength, rf = k >= reg.RightLength;
                    l.Append(lf ? "" : Left.Lines[reg.LeftStart + k]);
                    b.Append(bf ? "" : Base.Lines[reg.BaseStart + k]);
                    r.Append(rf ? "" : Right.Lines[reg.RightStart + k]);
                    lr.Add(new PaneRow(lk, lf ? 0 : reg.LeftStart + k + 1, lf, i, [], Tint: lt));
                    br.Add(new PaneRow(bk, bf ? 0 : reg.BaseStart + k + 1, bf, i, [], Tint: bt));
                    rr.Add(new PaneRow(rk, rf ? 0 : reg.RightStart + k + 1, rf, i, [], Tint: rt));
                }
            }

            resultStart.Add(resLine);
            var lines = ResolvedLines(i, reg, out var unresolved).ToList();
            var resKind = unresolved ? RowKind.Conflict : reg.Kind == MergeRegionKind.Stable ? RowKind.Same : RowKind.Changed;
            var resTint = unresolved ? MergeTint.Conflict : Decision(i) switch
            {
                MergeChoice.Left or MergeChoice.LeftThenRight => MergeTint.Left,
                MergeChoice.Right or MergeChoice.RightThenLeft => MergeTint.Right,
                MergeChoice.Base => MergeTint.Base,
                _ => reg.Kind switch { MergeRegionKind.LeftChanged or MergeRegionKind.BothSame => MergeTint.Left, MergeRegionKind.RightChanged => MergeTint.Right, _ => MergeTint.None },
            };
            foreach (var line in lines)
            {
                if (resLine > 0) { res.Append('\n'); }
                res.Append(line);
                resRows.Add(new PaneRow(resKind, resLine + 1, false, i, [], Tint: resTint));
                resLine++;
            }
        }

        LeftText = l.ToString(); BaseText = b.ToString(); RightText = r.ToString(); ResultText = res.ToString();
        LeftRows = lr; BaseRows = br; RightRows = rr; ResultRows = resRows; ResultStart = resultStart; RegionRowStart = rowStart;
        NotifyState();
        DisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(HasMerge));
        OnPropertyChanged(nameof(ConflictText));
        OnPropertyChanged(nameof(ChangeText));
        OnPropertyChanged(nameof(UnresolvedCount));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(CanResolve));
        OnPropertyChanged(nameof(CurrentRegion));
        OnPropertyChanged(nameof(LeftTitle));
        OnPropertyChanged(nameof(RightTitle));
        OnPropertyChanged(nameof(BaseTitle));
        OnPropertyChanged(nameof(OutputTitle));
        OnPropertyChanged(nameof(TargetPath));
        OnPropertyChanged(nameof(NeedsOutputPath));
        OnPropertyChanged(nameof(CurrentDescription));
        OnPropertyChanged(nameof(ConflictSections));
        OnPropertyChanged(nameof(HasUnresolved));
        OnPropertyChanged(nameof(AllResolved));
        OnPropertyChanged(nameof(LoadTimeText));
        OnPropertyChanged(nameof(LeftInfo));
        OnPropertyChanged(nameof(BaseInfo));
        OnPropertyChanged(nameof(RightInfo));
        Status = Merge is null ? Status : UnresolvedCount > 0 ? "Choose how to resolve each conflict: take left, right, both or base" : ResultEdited ? "Result edited by hand" : ConflictCount > 0 ? "All conflicts resolved — ready to save" : "Merged cleanly — ready to save";
        NextChangeCommand.NotifyCanExecuteChanged();
        PreviousChangeCommand.NotifyCanExecuteChanged();
        NextConflictCommand.NotifyCanExecuteChanged();
        TakeLeftCommand.NotifyCanExecuteChanged();
        TakeRightCommand.NotifyCanExecuteChanged();
        TakeBothCommand.NotifyCanExecuteChanged();
        TakeBaseCommand.NotifyCanExecuteChanged();
        ResetResultCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    // ───────────── navigation ─────────────

    private bool HasChanges => _changes.Count > 0;

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private void NextChange() => CurrentChange = (CurrentChange + 1) % _changes.Count;

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private void PreviousChange() => CurrentChange = (CurrentChange - 1 + _changes.Count) % _changes.Count;

    /// <summary>Jumps to the next conflict that still needs a decision (wrapping around).</summary>
    [RelayCommand(CanExecute = nameof(HasChanges))]
    private void NextConflict()
    {
        if (Merge is null) { return; }
        for (var step = 1; step <= _changes.Count; step++)
        {
            var idx = (Math.Max(CurrentChange, 0) + step) % _changes.Count;
            var region = _changes[idx];
            if (Merge.Regions[region].Kind == MergeRegionKind.Conflict && !_decisions.ContainsKey(region))
            {
                CurrentChange = idx;
                return;
            }
        }
    }

    // ───────────── resolving ─────────────

    private bool CanTake => CanResolve;

    private void Take(MergeChoice choice)
    {
        if (CurrentRegion < 0 || Merge is null) { return; }
        var region = CurrentRegion;
        _decisions[region] = choice;
        BuildDisplay();
        // after deciding a conflict, move on to the next one that still needs attention
        if (Merge.Regions[region].Kind == MergeRegionKind.Conflict && UnresolvedCount > 0)
        {
            NextConflict();
        }
    }

    [RelayCommand(CanExecute = nameof(CanTake))]
    private void TakeLeft() => Take(MergeChoice.Left);

    [RelayCommand(CanExecute = nameof(CanTake))]
    private void TakeRight() => Take(MergeChoice.Right);

    [RelayCommand(CanExecute = nameof(CanTake))]
    private void TakeBoth() => Take(MergeChoice.LeftThenRight);

    [RelayCommand(CanExecute = nameof(CanTake))]
    private void TakeBase() => Take(MergeChoice.Base);

    /// <summary>Resolves one particular section (the buttons beside the result), not only the current one.</summary>
    public void TakeRegion(int region, MergeChoice choice)
    {
        if (Merge is null || region < 0 || region >= Merge.Regions.Count || ResultEdited) { return; }
        var change = _changes.IndexOf(region);
        if (change >= 0) { CurrentChange = change; }
        _decisions[region] = choice;
        BuildDisplay();
    }

    /// <summary>The titles changed (after /title switches): tell the view.</summary>
    public void RefreshTitles() => NotifyState();

    public MergeRegionKind? KindOf(int region) => Merge is not null && region >= 0 && region < Merge.Regions.Count ? Merge.Regions[region].Kind : null;

    public bool IsResolved(int region) => _decisions.ContainsKey(region);

    public void TakeBothReversed() => Take(MergeChoice.RightThenLeft);

    /// <summary>Accepts one side for every region that is still undecided conflict.</summary>
    public void ResolveAllConflicts(MergeChoice choice)
    {
        if (Merge is null) { return; }
        for (var i = 0; i < Merge.Regions.Count; i++)
        {
            if (Merge.Regions[i].Kind == MergeRegionKind.Conflict && !_decisions.ContainsKey(i)) { _decisions[i] = choice; }
        }

        BuildDisplay();
    }

    /// <summary>Called by the view when the user types in the result pane.</summary>
    public void NotifyResultEdited(string text)
    {
        ResultText = text;
        ResultEdited = true;
        ResultRows = [];
        Status = "Result edited by hand";
        SaveCommand.NotifyCanExecuteChanged();
        ResetResultCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(ResultEdited))]
    private void ResetResult()
    {
        ResultEdited = false;
        BuildDisplay();
    }

    // ───────────── saving ─────────────

    private bool CanSave => HasMerge;

    /// <summary>Writes the result. Refuses (returns false) while conflicts are unresolved unless <paramref name="allowMarkers"/> is set.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => TrySave(allowMarkers: false);

    public bool NeedsOutputPath => TargetPath is null;

    public bool TrySave(bool allowMarkers)
    {
        if (Merge is null || TargetPath is null)
        {
            return false;
        }

        var hasMarkers = !ResultEdited && UnresolvedCount > 0;
        if (hasMarkers && !allowMarkers)
        {
            Error = $"{UnresolvedCount} conflict(s) are still unresolved. Resolve them, or save with conflict markers.";
            return false;
        }

        try
        {
            var text = ResultText;
            var (lines, _, _) = TextFile.Split(text);
            var template = Left.Lines.Count > 0 || Left.Path is not null ? Left : Right;
            if (!NoBackups) { Collaterale.Core.Settings.Backups.Create(TargetPath, Prefs.Backup); }
            File.WriteAllBytes(TargetPath, TextFile.Encode(lines, template.Eol, template.EndsWithNewline, template.Encoding, template.HasBom));
            Error = null;
            Completed = !hasMarkers;
            Status = hasMarkers ? $"Saved with conflict markers: {TargetPath}" : $"Saved {TargetPath}";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
            return false;
        }
    }
}
