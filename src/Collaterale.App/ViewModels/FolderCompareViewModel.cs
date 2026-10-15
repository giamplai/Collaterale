using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Collaterale.Core.Diff;
using Collaterale.Core.Folders;
using Collaterale.Core.Settings;
using Collaterale.Core.Support;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collaterale.App.ViewModels;

/// <summary>The colors of the folder view, resolved for the current theme (user choices from the settings win over the theme defaults).</summary>
public sealed class FolderPalette
{
    public IBrush? Older { get; init; }
    public IBrush? Same { get; init; }
    public IBrush? Orphan { get; init; }
    public IBrush? Newer { get; init; }
    public IBrush? Filtered { get; init; }

    public static FolderPalette Create(FolderColors? c, bool dark)
    {
        static IBrush? Pick(string? custom, string fallback)
            => new SolidColorBrush(Color.TryParse(custom, out var parsed) ? parsed : Color.Parse(fallback));

        c ??= new FolderColors();
        return new FolderPalette
        {
            Older = Pick(c.Older, dark ? "#9AA3B5" : "#8A8F98"),
            Same = Color.TryParse(c.Same, out var same) ? new SolidColorBrush(same) : new SolidColorBrush(Color.Parse(dark ? "#E6E9F2" : "#1B2030")),
            Orphan = Pick(c.Orphan, dark ? "#C58BF2" : "#8E3BB8"),
            Newer = Pick(c.Newer, dark ? "#FF6B6B" : "#D62828"),
            Filtered = Pick(c.Filtered, dark ? "#3CCBD6" : "#0E9AA7"),
        };
    }
}

/// <summary>Widths of the optional columns; shared by the header and every row so that showing or hiding a column changes them all.</summary>
public sealed partial class ColumnLayout : ObservableObject
{
    [ObservableProperty] private GridLength _size = new(96);
    [ObservableProperty] private GridLength _date = new(142);
    [ObservableProperty] private GridLength _why = new(0);
}

public sealed record LegendItem(string Text, IBrush? Brush, string Sample);

/// <summary>One visible line of the folder tree.</summary>
public sealed partial class FolderRow(FolderNode node, int depth, bool hasChildren, bool expanded, FolderPalette? palette = null, ColumnLayout? columns = null) : ObservableObject
{
    public ColumnLayout Columns { get; } = columns ?? new ColumnLayout();

    private readonly FolderPalette _p = palette ?? FolderPalette.Create(null, false);

    public FolderNode Node { get; } = node;
    public int Depth { get; } = depth;
    public bool HasChildren { get; } = hasChildren;

    [ObservableProperty] private bool _isExpanded = expanded;

    public string Name => (Node.IsMoved, Node.RightName) switch
    {
        (true, _) => $"{Node.Name}   ⇄ {Node.MovePartner!.RelativePath}",
        (_, { } right) => $"{Node.Name}   ↔ {right}",
        _ => Node.Name,
    };

    /// <summary>The name as the left pane shows it; empty when the item does not exist there.</summary>
    public string LeftName => !Node.ExistsLeft ? "" : Node.IsMoved ? $"{Node.Name}   ⇄ {Node.MovePartner!.RelativePath}" : Node.Name;

    /// <summary>The name as the right pane shows it (an aligned item keeps its own name); empty when absent.</summary>
    public string RightName => !Node.ExistsRight ? "" : Node.IsMoved ? $"{Node.Name}   ⇄ {Node.MovePartner!.RelativePath}" : Node.RightName ?? Node.Name;

    public bool LeftMissing => !Node.ExistsLeft;
    public bool RightMissing => !Node.ExistsRight;

    public double Indent => Depth * 18;
    public string Glyph => Node.IsDirectory ? (IsExpanded ? "▾" : "▸") : "";
    public string Icon => Node.IsArchive ? "▤" : Node.IsDirectory ? "🗀" : "▫";
    public FolderEntryState State => Node.State;

    public string StateGlyph => Node.IsMoved ? "⇄" : Node.State switch
    {
        FolderEntryState.Same => "=",
        FolderEntryState.Different => "≠",
        FolderEntryState.LeftOnly => "◀",
        FolderEntryState.RightOnly => "▶",
        _ => "⚠",
    };

    public string LeftSize => Node.IsDirectory && !Node.IsArchive ? (Node.ExistsLeft ? Format.Size(Node.LeftBytes) : "") : Format.Size(Node.Left?.Size);
    public string RightSize => Node.IsDirectory && !Node.IsArchive ? (Node.ExistsRight ? Format.Size(Node.RightBytes) : "") : Format.Size(Node.Right?.Size);
    public string LeftDate => Format.Date(Node.Left?.ModifiedUtc, Node.IsDirectory && !Node.IsArchive);
    public string RightDate => Format.Date(Node.Right?.ModifiedUtc, Node.IsDirectory && !Node.IsArchive);
    public bool LeftNewer => Node.Newer == Newer.Left && Node.State == FolderEntryState.Different;
    public bool RightNewer => Node.Newer == Newer.Right && Node.State == FolderEntryState.Different;

    /// <summary>What differs, in words (size, time, content, attributes…); empty for files that do not differ.</summary>
    public string Why
    {
        get
        {
            if (Node.State != FolderEntryState.Different || Node.Differences == DifferenceKinds.None) { return Node.FilteredSide != Newer.None ? "filtered on the " + (Node.FilteredSide == Newer.Left ? "left" : "right") : ""; }
            return string.Join(", ", Enum.GetValues<DifferenceKinds>().Where(k => k != DifferenceKinds.None && Node.Differences.HasFlag(k)).Select(k => k switch
            {
                DifferenceKinds.NameCase => "name case",
                _ => k.ToString().ToLowerInvariant(),
            }));
        }
    }

    // colors: gray = older, default = same, purple = only on one side, red = newer or different, teal = shown only because of its counterpart
    public IBrush? NameBrush => Node.State switch
    {
        FolderEntryState.LeftOnly or FolderEntryState.RightOnly => _p.Orphan,
        FolderEntryState.Different when !Node.IsDirectory || Node.IsArchive => _p.Newer,
        FolderEntryState.TypeMismatch or FolderEntryState.Error => _p.Newer,
        _ => _p.Same,
    };

    public IBrush? IconBrush => Node.IsDirectory && !Node.IsArchive
        ? (Node.State is FolderEntryState.LeftOnly or FolderEntryState.RightOnly || Node.ContainsOrphan && !Node.ContainsDifferent ? _p.Orphan : Node.ContainsDifferent ? _p.Newer : _p.Older)
        : NameBrush;

    public IBrush? LeftBrush => SideBrush(left: true);
    public IBrush? RightBrush => SideBrush(left: false);

    private IBrush? SideBrush(bool left)
    {
        if (Node.IsDirectory && !Node.IsArchive) { return null; }
        if (!(left ? Node.ExistsLeft : Node.ExistsRight)) { return null; }
        if (Node.FilteredSide == (left ? Newer.Left : Newer.Right)) { return _p.Filtered; }
        return Node.State switch
        {
            FolderEntryState.LeftOnly or FolderEntryState.RightOnly => _p.Orphan,
            FolderEntryState.Different => Node.Newer == Newer.None || Node.Newer == (left ? Newer.Left : Newer.Right) ? _p.Newer : _p.Older,
            FolderEntryState.Same => _p.Same,
            _ => _p.Newer,
        };
    }

    /// <summary>The small color spot before the name: visible even when a selection hides the text colors.</summary>
    public IBrush SpotBrush => Node.FilteredSide != Newer.None ? _p.Filtered! : Node.State switch
    {
        FolderEntryState.Different or FolderEntryState.TypeMismatch or FolderEntryState.Error => _p.Newer!,
        FolderEntryState.LeftOnly or FolderEntryState.RightOnly => _p.Orphan!,
        _ => Brushes.Transparent,
    };

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(Glyph));
}

public static class Format
{
    public static string Size(long? bytes)
    {
        if (bytes is null) { return ""; }
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var v = (double)bytes.Value;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{v.ToString(v >= 100 ? "0" : "0.#", CultureInfo.CurrentCulture)} {units[u]}";
    }

    public static string Date(DateTime? utc, bool isDirectory)
        => utc is null || isDirectory ? "" : utc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
}

/// <summary>Which folders are opened when a comparison is loaded.</summary>
public enum ExpandMode { Differences, All, None }

public enum FolderSortColumn { Name, LeftSize, LeftDate, RightSize, RightDate }

public sealed record LogLine(string Time, string Text);

public sealed partial class FolderCompareViewModel : ObservableObject, ISessionContent
{
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private bool _expandedInitialised;
    private IReadOnlyList<FolderRow> _selection = [];
    private CancellationTokenSource? _cts;

    public string Title => (LeftPath, RightPath) switch
    {
        ("", "") => "Folder compare",
        (var l, var r) => $"{Last(l)}  ↔  {Last(r)}",
    };

    private static string Last(string p) => string.IsNullOrWhiteSpace(p) ? "…" : Path.GetFileName(p.TrimEnd('/', '\\')) is { Length: > 0 } n ? n : p;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private string _leftPath = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private string _rightPath = "";
    [ObservableProperty] private CompareCriteria _criteria = CompareCriteria.SizeAndTime;
    [ObservableProperty] private string _include = "";
    [ObservableProperty] private string _excludeFiles = "";
    [ObservableProperty] private string _excludeFolders = ".git;.svn;.hg;node_modules";
    [ObservableProperty] private string _includeFolders = "";
    [ObservableProperty] private ExpandMode _expandOnLoad = ExpandMode.Differences;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title)), NotifyPropertyChangedFor(nameof(Tooltip))] private string _description = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanAct))] private bool _disableEditing;
    [ObservableProperty] private int _autoRefreshSeconds;
    [ObservableProperty] private FolderSortColumn _sortColumn = FolderSortColumn.Name;
    [ObservableProperty] private bool _sortDescending;
    [ObservableProperty] private bool _showLog = true;
    [ObservableProperty] private bool _showSizes = true;
    [ObservableProperty] private bool _showDates = true;
    [ObservableProperty] private bool _showWhy;
    [ObservableProperty] private string _leftFree = "";
    [ObservableProperty] private string _rightFree = "";
    [ObservableProperty] private bool _showSame = true;
    [ObservableProperty] private bool _showDifferent = true;
    [ObservableProperty] private bool _showLeftOnly = true;
    [ObservableProperty] private bool _showRightOnly = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Choose two folders to compare";
    [ObservableProperty] private string? _error;

    public static CompareCriteria[] CriteriaChoices { get; } = Enum.GetValues<CompareCriteria>();

    /// <summary>Everything the settings dialog can set beyond the quick fields above (those override the matching members).</summary>
    public FolderOptions Advanced { get; set; } = new();

    /// <summary>Saved name-filter sets (shared with the application settings).</summary>
    public List<FilterPreset> Presets { get; private set; } = [];

    public FolderColors Colors { get; private set; } = new();

    public void SetColors(FolderColors colors) => Colors = colors;

    public ObservableCollection<LogLine> Log { get; } = [];

    /// <summary>Set by the view: opens the synchronize window (for <c>/sync</c>).</summary>
    public Action? SyncRequested { get; set; }

    public ColumnLayout Columns { get; } = new();

    /// <summary>The application settings (presets and colors live there); set by the shell.</summary>
    public AppSettings AppSettings { get; private set; } = new();

    /// <summary>Set by the shell: write the application settings to disk now.</summary>
    public Action? RequestSaveSettings { get; set; }

    public string PeekTitle => _selection.Count > 0 ? _selection[0].Node.Name : "";

    /// <summary>Loads a saved filter set into this session and compares again.</summary>
    public async Task ApplyPresetAsync(FilterPreset p)
    {
        Include = p.Include;
        ExcludeFiles = p.ExcludeFiles;
        IncludeFolders = p.IncludeFolders;
        ExcludeFolders = p.ExcludeFolders;
        AddLog($"Applied filter preset \"{p.Name}\"");
        if (Directory.Exists(LeftPath) && Directory.Exists(RightPath)) { await CompareAsync(); }
    }

    public void SaveCurrentAsPreset()
    {
        var name = string.IsNullOrWhiteSpace(Include) ? $"Filter {Presets.Count + 1}" : Include.Trim();
        Presets.RemoveAll(x => x.Name == name);
        Presets.Add(new FilterPreset(name, Include, ExcludeFiles, IncludeFolders, ExcludeFolders));
        RequestSaveSettings?.Invoke();
        AddLog($"Saved filter preset \"{name}\"");
    }

    partial void OnShowSizesChanged(bool value) => Columns.Size = new GridLength(value ? 96 : 0);

    partial void OnShowDatesChanged(bool value) => Columns.Date = new GridLength(value ? 142 : 0);

    partial void OnShowWhyChanged(bool value) => Columns.Why = new GridLength(value ? 170 : 0);

    /// <summary>What the colors and symbols of the tree mean, in the colors currently in use.</summary>
    public IReadOnlyList<LegendItem> Legend
    {
        get
        {
            var p = Palette();
            return
            [
                new LegendItem("The older copy of a file that differs, or a folder whose contents are identical", p.Older, "Aa"),
                new LegendItem("Same on both sides", p.Same, "Aa"),
                new LegendItem("Exists on one side only (an orphan); a folder with orphans is purple too", p.Orphan, "Aa"),
                new LegendItem("The newer copy, or a file that differs; a folder holding such files is red", p.Newer, "Aa"),
                new LegendItem("Hidden by a filter, shown because its counterpart on the other side is not", p.Filtered, "Aa"),
                new LegendItem("Center column: ≠ different, = same, ◀ left only, ▶ right only, ⇄ renamed or moved, ⚠ type mismatch or error", null, "≠"),
                new LegendItem("▤ an archive; double-click to look inside when archives are handled as folders once opened", null, "▤"),
            ];
        }
    }

    public string Tooltip => Description.Length > 0 ? Description : $"{LeftPath}\n{RightPath}";

    public FolderComparison? Comparison { get; private set; }
    public BulkCollection<FolderRow> Rows { get; } = [];

    /// <summary>Set by the shell: open two files in the comparison view that suits them (text, table, hex or picture).</summary>
    public Action<string, string>? OpenTextRequested { get; set; }

    /// <summary>Set by the view: asks the user before copying over or deleting. Returns true to proceed.</summary>
    public Func<string, string, IReadOnlyList<string>, Task<bool>>? Confirm { get; set; }

    public FolderSummary? Summary => Comparison?.Summary;
    public string SameText => $"= Same {Summary?.Same ?? 0}";
    public string DifferentText => $"≠ Different {Summary?.Different ?? 0}";
    public string LeftOnlyText => $"◀ Left only {Summary?.LeftOnly ?? 0}";
    public string RightOnlyText => $"▶ Right only {Summary?.RightOnly ?? 0}";
    public int SelectedCount => _selection.Count;
    public bool HasSelection => _selection.Count > 0;
    public bool HasComparison => Comparison is not null;

    /// <summary>The program's preferences (backups, confirmations…); a fresh set until the shell supplies the real one.</summary>
    public Preferences Prefs { get; set; } = new();

    public void ApplyDefaults(AppSettings s)
    {
        Criteria = s.Criteria;
        ExcludeFolders = s.ExcludeFolders;
        ExcludeFiles = s.ExcludeFiles;
        AppSettings = s;
        Prefs = s.Prefs;
        Presets = s.FilterPresets;
        Colors = s.Colors;
        if (s.FolderDefaults is { } d)
        {
            Advanced = d;
            Criteria = d.Criteria;
            Include = d.Include;
            ExcludeFiles = d.ExcludeFiles;
            ExcludeFolders = d.ExcludeFolders;
            IncludeFolders = d.IncludeFolders;
        }
    }

    partial void OnShowSameChanged(bool value) => Rebuild();
    partial void OnShowDifferentChanged(bool value) => Rebuild();
    partial void OnShowLeftOnlyChanged(bool value) => Rebuild();
    partial void OnShowRightOnlyChanged(bool value) => Rebuild();

    public FolderOptions Options => Advanced with
    {
        Criteria = Criteria,
        Include = Include,
        ExcludeFiles = ExcludeFiles,
        ExcludeFolders = ExcludeFolders,
        IncludeFolders = IncludeFolders,
    };

    // ───────────── comparing ─────────────

    public async Task OpenAsync(string left, string right)
    {
        LeftPath = left;
        RightPath = right;
        await CompareAsync();
    }

    /// <summary>Runs background work at the priority the preferences ask for, and puts the thread back the way it was.</summary>
    private static T WithPriority<T>(ComparisonPriority p, Func<T> work)
    {
        var thread = Thread.CurrentThread;
        var old = thread.Priority;
        try
        {
            thread.Priority = p switch { ComparisonPriority.Low => ThreadPriority.BelowNormal, ComparisonPriority.High => ThreadPriority.AboveNormal, _ => ThreadPriority.Normal };
            return work();
        }
        finally
        {
            thread.Priority = old;
        }
    }

    [RelayCommand]
    public async Task CompareAsync()
    {
        if (!Directory.Exists(LeftPath) || !Directory.Exists(RightPath))
        {
            Error = !Directory.Exists(LeftPath) ? $"The left folder does not exist: {LeftPath}" : $"The right folder does not exist: {RightPath}";
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Error = null;
        IsBusy = true;
        Status = "Comparing…";
        AddLog($"Load comparison: {LeftPath} ↔ {RightPath}");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var (l, r, opt) = (LeftPath, RightPath, Options);
            var priority = Prefs.Files.Priority;
            Comparison = await Task.Run(() => WithPriority(priority, () => FolderComparer.Compare(l, r, opt, null, ct)), ct);
            if (!_expandedInitialised)
            {
                // open the folders the user wants opened
                var open = ExpandOnLoad switch
                {
                    ExpandMode.All => Comparison.Root.Descendants().Where(n => n.IsDirectory),
                    ExpandMode.None => [],
                    _ => Comparison.Root.Descendants().Where(n => n.IsDirectory && n.State != FolderEntryState.Same),
                };
                foreach (var d in open)
                {
                    _expanded.Add(d.RelativePath);
                }

                _expandedInitialised = true;
            }

            Rebuild();
            NotifySummary();
            UpdateFreeSpace();
            Status = Comparison.Summary.AreEqual ? "The folders are identical" : $"{Comparison.Summary.Different} different, {Comparison.Summary.LeftOnly} only on the left, {Comparison.Summary.RightOnly} only on the right";
            AddLog($"Comparison completed in {clock.Elapsed.TotalSeconds:0.00} seconds — {Status}");
        }
        catch (OperationCanceledException)
        {
            Status = "Comparison cancelled";
            AddLog("Comparison stopped");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelCompare() => _cts?.Cancel();

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(SameText));
        OnPropertyChanged(nameof(DifferentText));
        OnPropertyChanged(nameof(LeftOnlyText));
        OnPropertyChanged(nameof(RightOnlyText));
        OnPropertyChanged(nameof(HasComparison));
    }

    // ───────────── tree flattening ─────────────

    private bool Passes(FolderNode n) => n.State switch
    {
        FolderEntryState.Same => ShowSame,
        FolderEntryState.Different => ShowDifferent,
        FolderEntryState.LeftOnly => ShowLeftOnly,
        FolderEntryState.RightOnly => ShowRightOnly,
        _ => true,
    };

    /// <summary>A folder is shown when it, or anything below it, passes the filter.</summary>
    private bool Visible(FolderNode n) => n.IsDirectory ? (n.Children.Any(Visible) || (n.Children.Count == 0 && Passes(n))) : Passes(n);

    public void Rebuild()
    {
        var rows = new List<FolderRow>();
        if (Comparison is not null)
        {
            Flatten(Comparison.Root.Children, 0, rows);
        }

        Rows.ReplaceAll(rows);
    }

    private FolderPalette Palette() => FolderPalette.Create(Colors, Application.Current?.ActualThemeVariant == ThemeVariant.Dark);

    /// <summary>Folders stay first; within each group the chosen column decides.</summary>
    private IEnumerable<FolderNode> Sorted(IEnumerable<FolderNode> nodes)
    {
        var groups = nodes.GroupBy(n => n.IsDirectory && !n.IsArchive ? 0 : 1).OrderBy(g => g.Key);
        foreach (var g in groups)
        {
            IOrderedEnumerable<FolderNode> ordered = SortColumn switch
            {
                FolderSortColumn.LeftSize => SortDescending ? g.OrderByDescending(n => n.Left?.Size ?? -1) : g.OrderBy(n => n.Left?.Size ?? -1),
                FolderSortColumn.RightSize => SortDescending ? g.OrderByDescending(n => n.Right?.Size ?? -1) : g.OrderBy(n => n.Right?.Size ?? -1),
                FolderSortColumn.LeftDate => SortDescending ? g.OrderByDescending(n => n.Left?.ModifiedUtc ?? DateTime.MinValue) : g.OrderBy(n => n.Left?.ModifiedUtc ?? DateTime.MinValue),
                FolderSortColumn.RightDate => SortDescending ? g.OrderByDescending(n => n.Right?.ModifiedUtc ?? DateTime.MinValue) : g.OrderBy(n => n.Right?.ModifiedUtc ?? DateTime.MinValue),
                _ => SortDescending ? g.OrderByDescending(n => n.Name, StringComparer.OrdinalIgnoreCase) : g.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase),
            };
            foreach (var n in ordered) { yield return n; }
        }
    }

    [RelayCommand]
    private void SortBy(FolderSortColumn column)
    {
        SortDescending = SortColumn == column && !SortDescending;
        SortColumn = column;
        Rebuild();
    }

    private void Flatten(IEnumerable<FolderNode> nodes, int depth, List<FolderRow> output, FolderPalette? palette = null)
    {
        palette ??= Palette();
        foreach (var n in Sorted(nodes.Where(Visible)))
        {
            var expanded = n.IsDirectory && _expanded.Contains(n.RelativePath);
            output.Add(new FolderRow(n, depth, n.IsDirectory && n.Children.Count > 0, expanded, palette, Columns));
            if (expanded)
            {
                Flatten(n.Children, depth + 1, output, palette);
            }
        }
    }

    public void Toggle(FolderRow row)
    {
        if (!row.HasChildren)
        {
            return;
        }

        if (!_expanded.Remove(row.Node.RelativePath))
        {
            _expanded.Add(row.Node.RelativePath);
        }

        Rebuild();
    }

    [RelayCommand]
    private void ExpandAll()
    {
        if (Comparison is null) { return; }
        foreach (var d in Comparison.Root.Descendants().Where(n => n.IsDirectory)) { _expanded.Add(d.RelativePath); }
        Rebuild();
    }

    [RelayCommand]
    private void CollapseAll()
    {
        _expanded.Clear();
        Rebuild();
    }

    // ───────────── selection and actions ─────────────

    public void SetSelection(IEnumerable<FolderRow> rows)
    {
        _selection = [.. rows];
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasSingleSelection));
        CopyToRightCommand.NotifyCanExecuteChanged();
        CopyToLeftCommand.NotifyCanExecuteChanged();
        DeleteLeftCommand.NotifyCanExecuteChanged();
        DeleteRightCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Double-click: folders expand, differing text files open in a compare tab, one-sided files do nothing.</summary>
    public void Activate(FolderRow row)
    {
        if (row.Node.IsDirectory)
        {
            Toggle(row);
        }
        else if (row.Node.IsArchive && Comparison is { } ac)
        {
            _ = OpenArchiveAsync(ac, row.Node);
        }
        else if (Comparison is { } c && row.Node.State is FolderEntryState.Different or FolderEntryState.Same && row.Node.ExistsLeft && row.Node.ExistsRight)
        {
            OpenTextRequested?.Invoke(c.LeftPath(row.Node), c.RightPath(row.Node));
        }
    }

    /// <summary>
    /// The pair of files that comes after (or before) the given one among the files that differ, in the order of the tree.
    /// Null when there is none: at the end without wrap-around, or no other file differs.
    /// </summary>
    public (string Left, string Right)? NeighbourDifferentFile(string leftPath, int step)
    {
        if (Comparison is not { } c) { return null; }
        var prefs = Prefs.NextDifference;
        var files = c.Root.Descendants().Where(n => !n.IsDirectory && !n.InArchive && n.State == FolderEntryState.Different && n.ExistsLeft && n.ExistsRight).ToList();
        var at = files.FindIndex(n => string.Equals(c.LeftPath(n), leftPath, StringComparison.Ordinal));
        if (at < 0) { return null; }
        var folder = Path.GetDirectoryName(c.LeftPath(files[at]));
        var candidates = prefs.LimitFilesToCurrentFolder ? files.Where(n => Path.GetDirectoryName(c.LeftPath(n)) == folder).ToList() : files;
        var index = candidates.FindIndex(n => c.LeftPath(n) == leftPath);
        var next = index + step;
        if (next < 0 || next >= candidates.Count)
        {
            if (!prefs.WrapAround || candidates.Count < 2) { return null; }
            next = (next + candidates.Count) % candidates.Count;
        }

        var n2 = candidates[next];
        return (c.LeftPath(n2), c.RightPath(n2));
    }

    public bool CanAct => HasSelection && Comparison is not null && !IsBusy && !DisableEditing;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task CopyToRightAsync() => RunSelectionAsync(SyncAction.CopyToRight);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task CopyToLeftAsync() => RunSelectionAsync(SyncAction.CopyToLeft);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task DeleteLeftAsync() => RunSelectionAsync(SyncAction.DeleteLeft);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task DeleteRightAsync() => RunSelectionAsync(SyncAction.DeleteRight);

    private async Task RunSelectionAsync(SyncAction action)
    {
        if (Comparison is not { } c)
        {
            return;
        }

        var plan = FolderSync.PlanFor(c, _selection.Select(r => r.Node), action);
        await ExecuteAsync(plan, action switch
        {
            SyncAction.CopyToRight => "Copy to the right",
            SyncAction.CopyToLeft => "Copy to the left",
            SyncAction.DeleteLeft => "Delete from the left",
            _ => "Delete from the right",
        });
    }

    public SyncPlan PlanSync(SyncMode mode) => Comparison is { } c ? FolderSync.Plan(c, mode) : new SyncPlan([], []);

    /// <summary>Shows a confirmation with the full list of steps, runs the plan in the background, then re-compares.</summary>
    public async Task ExecuteAsync(SyncPlan plan, string title, bool askConfirmation = true)
    {
        if (Comparison is not { } c)
        {
            return;
        }

        if (DisableEditing)
        {
            Error = "Editing is disabled for this session (Session settings → Spec). Nothing was changed.";
            return;
        }

        if (plan.IsEmpty)
        {
            Status = plan.Skipped.Count > 0 ? $"Nothing to do ({plan.Skipped.Count} skipped: {plan.Skipped[0].Reason})" : "Nothing to do";
            return;
        }

        var lines = plan.Steps.Select(s => $"{Describe(s.Action)}  {s.RelativePath}{(s.MoveTo is null ? "" : "  →  " + s.MoveTo)}").ToList();
        var summary = $"{plan.Steps.Count(s => !s.IsDelete)} copy/create step(s), {plan.Deletes} delete(s), {Format.Size(plan.BytesToCopy)} to copy.";
        if (plan.Deletes > 0) { summary += " Deleted items cannot be recovered."; }

        if (askConfirmation && Confirm is not null && NeedsConfirmation(c, plan) && !await Confirm(title, summary, lines))
        {
            return;
        }

        IsBusy = true;
        Status = "Working…";
        try
        {
            var backupPrefs = Prefs.Backup;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var step in plan.Steps) { AddLog($"{Describe(step.Action)}  {step.RelativePath}", LogLevel.Detail); }
            var result = await Task.Run(() => FolderSync.Execute(c, plan, backup: (path, root) => Backups.Create(path, backupPrefs, root)));
            foreach (var f in result.Failures) { AddLog($"{f.Step.RelativePath}: {f.Error}", LogLevel.Warning); }
            AddLog($"{title}: {result.Done} step(s) done, {result.Failures.Count} failed ({clock.Elapsed.TotalSeconds:0.0} s)");
            if (Prefs.Files.BeepAfterLongOperations && clock.Elapsed > TimeSpan.FromSeconds(3)) { Console.Write('\a'); }
            await CompareAsync();
            Error = result.Success ? null : $"{result.Failures.Count} step(s) failed: {result.Failures[0].Error}";
            Status = result.Success ? $"Done: {result.Done} step(s)" : $"Done with errors: {result.Done} ok, {result.Failures.Count} failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ───────────── log, free space ─────────────

    private MessageLog? _messages;

    /// <summary>Adds a line to the log panel, if the log preferences ask for that kind of message.</summary>
    public void AddLog(string text, LogLevel level = LogLevel.Summary)
    {
        if (_messages is null)
        {
            _messages = new MessageLog(() => Prefs.Log);
            _messages.Added += e =>
            {
                Log.Add(new LogLine(e.Time, e.Level == LogLevel.Warning ? "⚠ " + e.Text : e.Text));
                var max = Math.Max(1, Prefs.Log.MaxMessages);
                while (Log.Count > max) { Log.RemoveAt(0); }
            };
        }

        _messages.Add(level, text);
    }

    /// <summary>Whether a plan needs the user's confirmation, by the file-operation preferences: deletes, moves, overwrites and plain copies are asked about separately.</summary>
    private bool NeedsConfirmation(FolderComparison c, SyncPlan plan)
    {
        var f = Prefs.FileOperations;
        foreach (var s in plan.Steps)
        {
            if (s.IsDelete) { if (f.ConfirmDelete) { return true; } continue; }
            if (s.Action is SyncAction.MoveLeft or SyncAction.MoveRight) { if (f.ConfirmMove) { return true; } continue; }
            if (s.Action is SyncAction.CopyToRight or SyncAction.CopyToLeft)
            {
                var (root, rel) = s.Action == SyncAction.CopyToRight ? (c.RightRoot, s.RightRel ?? s.RelativePath) : (c.LeftRoot, s.LeftRel ?? s.RelativePath);
                var overwrites = !s.IsDirectory && File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (overwrites ? f.ConfirmOverwrite : f.ConfirmCopy) { return true; }
            }
        }

        return false;
    }

    /// <summary>The synchronize command can be turned off for good (No to all); this says why when it is.</summary>
    public bool SyncAllowed(out string reason)
    {
        reason = "Synchronizing is turned off in Options ▸ File operations (Synchronize confirmations: No to all).";
        return Prefs.FileOperations.SyncConfirmation != Confirmation.NoToAll;
    }

    private void UpdateFreeSpace()
    {
        LeftFree = FreeSpace(LeftPath);
        RightFree = FreeSpace(RightPath);
    }

    private static string FreeSpace(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var drive = DriveInfo.GetDrives().Where(d => full.StartsWith(d.Name, StringComparison.Ordinal) && d.IsReady).OrderByDescending(d => d.Name.Length).FirstOrDefault();
            return drive is null ? "" : $"{Format.Size(drive.AvailableFreeSpace)} free on {drive.Name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "";
        }
    }

    // ───────────── session commands ─────────────

    /// <summary>Exchange the two sides (folders and hand-made alignments) and compare again.</summary>
    [RelayCommand]
    private async Task SwapAsync()
    {
        (LeftPath, RightPath) = (RightPath, LeftPath);
        Advanced = Advanced with { Alignments = [.. Advanced.Alignments.Select(a => new AlignmentOverride(a.RightPath, a.LeftPath))] };
        _expandedInitialised = false;
        if (Directory.Exists(LeftPath) && Directory.Exists(RightPath)) { await CompareAsync(); }
    }

    private async Task OpenArchiveAsync(FolderComparison c, FolderNode node)
    {
        IsBusy = true;
        Status = $"Opening {node.Name}…";
        try
        {
            await Task.Run(() => FolderComparer.OpenArchive(c, node));
            _expanded.Add(node.RelativePath);
            NotifySummary();
            Rebuild();
            Status = $"Opened {node.Name}";
            AddLog($"Opened archive {node.RelativePath}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Pairs one left-only and one right-only entry of the same folder so that they are compared with each other.</summary>
    [RelayCommand]
    public async Task AlignSelectedAsync()
    {
        var l = _selection.Select(r => r.Node).FirstOrDefault(n => n.State == FolderEntryState.LeftOnly);
        var r = _selection.Select(r => r.Node).FirstOrDefault(n => n.State == FolderEntryState.RightOnly);
        if (_selection.Count != 2 || l is null || r is null)
        {
            Status = "Select one item that exists only on the left and one that exists only on the right, then align them.";
            return;
        }

        static string Parent(string path) => path.LastIndexOf('/') is var i and >= 0 ? path[..i] : "";
        if (Parent(l.RelativePath) != Parent(r.RelativePath))
        {
            Status = "Items can only be aligned with items in the same folder.";
            return;
        }

        Advanced = Advanced with { Alignments = [.. Advanced.Alignments, new AlignmentOverride(l.LeftRelativePath, r.RightRelativePath)] };
        AddLog($"Aligned {l.Name} with {r.Name}");
        await CompareAsync();
    }

    [RelayCommand]
    private async Task ClearAlignmentsAsync()
    {
        if (Advanced.Alignments.Count == 0) { return; }
        Advanced = Advanced with { Alignments = [] };
        await CompareAsync();
    }

    public bool HasSingleSelection => _selection.Count == 1;

    /// <summary>Opens the selected item's folder in the system file manager.</summary>
    [RelayCommand]
    private void Reveal(string side)
    {
        if (Comparison is not { } c || _selection.Count == 0) { return; }
        var n = _selection[0].Node;
        var path = side == "left" ? (n.ExistsLeft ? c.LeftPath(n) : null) : (n.ExistsRight ? c.RightPath(n) : null);
        if (path is null) { Status = $"It does not exist on the {side}."; return; }
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (folder is null) { return; }
        try
        {
            if (OperatingSystem.IsWindows()) { System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\""); }
            else if (OperatingSystem.IsMacOS()) { System.Diagnostics.Process.Start("open", [folder]); }
            else { System.Diagnostics.Process.Start("xdg-open", [folder]); }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Error = $"Cannot open the file manager: {ex.Message}";
        }
    }

    /// <summary>The full path of the selected item on each side, one per line (for the clipboard).</summary>
    public string SelectedPaths()
        => Comparison is { } c ? string.Join('\n', _selection.Select(r => r.Node).SelectMany(n => new[] { n.ExistsLeft ? c.LeftPath(n) : null, n.ExistsRight ? c.RightPath(n) : null }).Where(p => p is not null)) : "";

    /// <summary>A quick look at the selected entry: the differences between a pair of text files, or the start of a one-sided one.</summary>
    public string Peek()
    {
        if (Comparison is not { } c || _selection.Count == 0) { return "Select a file to peek at its contents."; }
        var n = _selection[0].Node;
        var sb = new StringBuilder();
        try
        {
            if (n.IsDirectory && !n.IsArchive)
            {
                var files = n.Descendants().Where(d => !d.IsDirectory).ToList();
                return $"{n.RelativePath}/\n{files.Count} file(s): {files.Count(f => f.State == FolderEntryState.Different)} different, {files.Count(f => f.State == FolderEntryState.LeftOnly)} only on the left, {files.Count(f => f.State == FolderEntryState.RightOnly)} only on the right, {files.Count(f => f.State == FolderEntryState.Same)} same.";
            }

            string? left = n.ExistsLeft ? c.LeftPath(n) : null, right = n.ExistsRight ? c.RightPath(n) : null;
            sb.AppendLine(n.RelativePath);
            sb.AppendLine($"  left : {(left is null ? "—" : $"{Format.Size(n.Left?.Size)}, {Format.Date(n.Left?.ModifiedUtc, false)}")}");
            sb.AppendLine($"  right: {(right is null ? "—" : $"{Format.Size(n.Right?.Size)}, {Format.Date(n.Right?.ModifiedUtc, false)}")}");
            if (n.Differences != DifferenceKinds.None) { sb.AppendLine($"  differs in: {new FolderRow(n, 0, false, false).Why}"); }
            sb.AppendLine();

            var tl = left is null ? null : Collaterale.Core.Text.TextFile.Load(left);
            var tr = right is null ? null : Collaterale.Core.Text.TextFile.Load(right);
            if (tl is { IsBinary: true } || tr is { IsBinary: true })
            {
                sb.Append("Binary file — use Hex compare to look inside.");
            }
            else if (tl is not null && tr is not null)
            {
                var patch = UnifiedDiff.Write("left/" + n.Name, "right/" + n.Name, tl.Lines, tr.Lines, 2, Options.ContentMode == ContentMode.Rules ? Options.Rules.ToDiffOptionsPublic(left!) : null, tl.EndsWithNewline, tr.EndsWithNewline);
                sb.Append(patch.Length == 0 ? "The files have the same text." : Truncate(patch, 400));
            }
            else
            {
                var only = (tl ?? tr)!;
                sb.Append(Truncate(string.Join('\n', only.Lines), 120));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            sb.Append(ex.Message);
        }

        return sb.ToString();

        static string Truncate(string text, int lines)
        {
            var parts = text.Split('\n');
            return parts.Length <= lines ? text : string.Join('\n', parts.Take(lines)) + $"\n… ({parts.Length - lines} more lines)";
        }
    }

    // ───────────── automatic refresh ─────────────

    private DispatcherTimer? _refresh;

    partial void OnAutoRefreshSecondsChanged(int value)
    {
        _refresh?.Stop();
        _refresh = null;
        if (value <= 0) { return; }
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, value)) };
        _refresh.Tick += (_, _) =>
        {
            if (!IsBusy && HasComparison && Directory.Exists(LeftPath) && Directory.Exists(RightPath)) { _ = CompareAsync(); }
        };
        _refresh.Start();
    }

    partial void OnDisableEditingChanged(bool value)
    {
        CopyToRightCommand.NotifyCanExecuteChanged();
        CopyToLeftCommand.NotifyCanExecuteChanged();
        DeleteLeftCommand.NotifyCanExecuteChanged();
        DeleteRightCommand.NotifyCanExecuteChanged();
    }

    private static string Describe(SyncAction a) => a switch
    {
        SyncAction.CopyToRight => "copy →",
        SyncAction.CopyToLeft => "← copy",
        SyncAction.DeleteLeft => "delete (left)",
        SyncAction.DeleteRight => "delete (right)",
        SyncAction.CreateFolderRight => "new folder →",
        SyncAction.MoveRight => "rename →",
        SyncAction.MoveLeft => "← rename",
        _ => "← new folder",
    };
}
