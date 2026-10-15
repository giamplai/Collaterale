using Collaterale.Core.Table;
using Collaterale.Core.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collaterale.App.ViewModels;

public enum DelimiterChoice { Auto, Comma, Semicolon, Tab, Pipe }

public sealed partial class TableCompareViewModel : ObservableObject, ISessionContent
{
    public const int MaxMeasuredRows = 800;

    private ParsedTable? _left, _right;
    private List<int> _differences = [];

    [ObservableProperty] private string? _leftPath;
    [ObservableProperty] private string? _rightPath;
    [ObservableProperty] private TableComparison? _comparison;
    [ObservableProperty] private DelimiterChoice _delimiter = DelimiterChoice.Auto;
    [ObservableProperty] private bool _hasHeader = true;
    [ObservableProperty] private string _keyColumns = "";
    [ObservableProperty] private ColumnMatch _columnMatch = ColumnMatch.Position;
    [ObservableProperty] private bool _ignoreCase;
    [ObservableProperty] private bool _trim;
    [ObservableProperty] private string _tolerance = "";
    [ObservableProperty] private bool _differencesOnly;
    [ObservableProperty] private int _currentRow = -1;
    [ObservableProperty] private string _status = "Open two CSV or TSV files to compare their cells";
    [ObservableProperty] private string? _error;

    public static DelimiterChoice[] Delimiters { get; } = Enum.GetValues<DelimiterChoice>();
    public static ColumnMatch[] ColumnMatches { get; } = Enum.GetValues<ColumnMatch>();

    /// <summary>Rows that are shown (all, or only the differing ones), as indexes into the comparison.</summary>
    public IReadOnlyList<int> VisibleRows { get; private set; } = [];

    /// <summary>Suggested width of each merged column, in characters.</summary>
    public IReadOnlyList<int> ColumnWidths { get; private set; } = [];

    public event EventHandler? DataChanged;

    public string Title => (LeftPath, RightPath) switch
    {
        (null, null) => "Table compare",
        (var l, var r) => $"{(l is null ? "…" : Path.GetFileName(l))}  ↔  {(r is null ? "…" : Path.GetFileName(r))}  (table)",
    };

    public string LeftTitle => PathDisplay.Short(LeftPath, "(no file)");
    public string RightTitle => PathDisplay.Short(RightPath, "(no file)");
    public bool HasComparison => Comparison is not null;
    public string DifferenceText => Comparison is null ? "" : _differences.Count == 0 ? (Comparison.ColumnsDiffer ? "Columns differ" : "Identical") : CurrentRow < 0 ? $"{_differences.Count:N0} differing rows" : $"Row difference {Math.Max(_differences.IndexOf(CurrentRow), 0) + 1:N0} of {_differences.Count:N0}";
    public string ShapeText => Comparison is null ? "" : $"{Comparison.ColumnCount} columns · {Comparison.Rows.Count:N0} rows";
    public string SummaryText => Comparison is null ? "" : $"{Comparison.ChangedRows:N0} changed rows ({Comparison.ChangedCells:N0} cells) · {Comparison.LeftOnlyRows:N0} only left · {Comparison.RightOnlyRows:N0} only right";
    public IReadOnlyList<string> ColumnNames => Comparison?.ColumnNames ?? [];

    public TableOptions Options => new()
    {
        HasHeader = HasHeader,
        KeyColumns = ParseKeys(KeyColumns),
        Columns = ColumnMatch,
        IgnoreCase = IgnoreCase,
        TrimWhitespace = Trim,
        NumericTolerance = double.TryParse(Tolerance, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t) && t >= 0 ? t : null,
    };

    /// <summary>"0,2" or column letters/headers: zero-based indexes, "A,C" letters, or exact header names.</summary>
    private IReadOnlyList<int> ParseKeys(string text)
    {
        var result = new List<int>();
        foreach (var part in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var n) && n >= 1) { result.Add(n - 1); continue; }          // 1-based numbers, like a spreadsheet user would type
            var byName = ColumnNames.ToList().FindIndex(c => string.Equals(c, part, StringComparison.OrdinalIgnoreCase));
            if (byName >= 0) { result.Add(byName); continue; }
            if (part.All(char.IsLetter) && part.Length <= 3)
            {
                var idx = part.ToUpperInvariant().Aggregate(0, (a, ch) => a * 26 + (ch - 'A' + 1)) - 1;
                if (idx >= 0) { result.Add(idx); }
            }
        }

        return result;
    }

    // ───────────── loading ─────────────

    public void Open(string left, string right)
    {
        try
        {
            Error = null;
            LeftPath = left;
            RightPath = right;
            Reload();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
    }

    public void OpenText(string? leftName, string leftText, string? rightName, string rightText)
    {
        LeftPath = leftName;
        RightPath = rightName;
        Parse(leftText, rightText);
    }

    [RelayCommand]
    private void Reload()
    {
        if (LeftPath is null || RightPath is null) { return; }
        var l = TextFile.Load(LeftPath);
        var r = TextFile.Load(RightPath);
        if (l.IsBinary || r.IsBinary)
        {
            Error = "One of the files looks binary. Table compare reads text files (CSV, TSV, …).";
            return;
        }

        Parse(string.Join('\n', l.Lines), string.Join('\n', r.Lines));
    }

    private string _leftText = "", _rightText = "";

    private void Parse(string leftText, string rightText)
    {
        _leftText = leftText;
        _rightText = rightText;
        Recompare(goToFirst: true);
    }

    private char? DelimiterChar => Delimiter switch
    {
        DelimiterChoice.Comma => ',',
        DelimiterChoice.Semicolon => ';',
        DelimiterChoice.Tab => '\t',
        DelimiterChoice.Pipe => '|',
        _ => null,
    };

    private void Recompare(bool goToFirst = false)
    {
        if (LeftPath is null && RightPath is null && _leftText.Length == 0 && _rightText.Length == 0) { return; }
        _left = CsvParser.Parse(_leftText, DelimiterChar);
        _right = CsvParser.Parse(_rightText, DelimiterChar ?? CsvParser.Detect(_rightText));
        Comparison = TableComparison.Compare(_left, _right, Options);
        _differences = [.. Comparison.Rows.Select((r, i) => (r, i)).Where(t => t.r.IsDifference).Select(t => t.i)];
        MeasureColumns();
        RebuildVisible();
        CurrentRow = _differences.Count == 0 ? -1 : goToFirst || !_differences.Contains(CurrentRow) ? _differences[0] : CurrentRow;
        Status = Comparison.AreEqual ? "The tables are identical" : SummaryText;
        NotifyAll();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MeasureColumns()
    {
        var c = Comparison!;
        var widths = new int[c.ColumnCount];
        for (var col = 0; col < widths.Length; col++)
        {
            widths[col] = Math.Max(4, c.ColumnNames[col].Length + 2);
            for (var r = 0; r < Math.Min(c.Rows.Count, MaxMeasuredRows); r++)
            {
                foreach (var left in new[] { true, false })
                {
                    var v = c.Cell(r, col, left);
                    if (v is not null) { widths[col] = Math.Max(widths[col], Math.Min(v.Length + 2, 40)); }
                }
            }
        }

        ColumnWidths = widths;
    }

    private void RebuildVisible()
    {
        VisibleRows = Comparison is null ? [] : DifferencesOnly ? _differences : [.. Enumerable.Range(0, Comparison.Rows.Count)];
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LeftTitle));
        OnPropertyChanged(nameof(RightTitle));
        OnPropertyChanged(nameof(HasComparison));
        OnPropertyChanged(nameof(DifferenceText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(ShapeText));
        OnPropertyChanged(nameof(ColumnNames));
        NextDifferenceCommand.NotifyCanExecuteChanged();
        PreviousDifferenceCommand.NotifyCanExecuteChanged();
    }

    // option changes re-run the comparison
    partial void OnDelimiterChanged(DelimiterChoice value) => Recompare(goToFirst: true);
    partial void OnHasHeaderChanged(bool value) => Recompare(goToFirst: true);
    partial void OnKeyColumnsChanged(string value) => Recompare(goToFirst: true);
    partial void OnColumnMatchChanged(ColumnMatch value) => Recompare(goToFirst: true);
    partial void OnIgnoreCaseChanged(bool value) => Recompare(goToFirst: true);
    partial void OnTrimChanged(bool value) => Recompare(goToFirst: true);
    partial void OnToleranceChanged(string value) => Recompare(goToFirst: true);

    partial void OnDifferencesOnlyChanged(bool value)
    {
        RebuildVisible();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCurrentRowChanged(int value) => OnPropertyChanged(nameof(DifferenceText));

    // ───────────── navigation ─────────────

    private bool HasAny => _differences.Count > 0;

    [RelayCommand(CanExecute = nameof(HasAny))]
    private void NextDifference()
    {
        var i = _differences.FindIndex(d => d > CurrentRow);
        CurrentRow = i >= 0 ? _differences[i] : _differences[0];
    }

    [RelayCommand(CanExecute = nameof(HasAny))]
    private void PreviousDifference()
    {
        var i = _differences.FindLastIndex(d => d < CurrentRow);
        CurrentRow = i >= 0 ? _differences[i] : _differences[^1];
    }

    public void SelectRow(int comparisonRow) => CurrentRow = comparisonRow;

    private string? _pendingLeft, _pendingRight;

    /// <summary>Choose one side from the UI; the comparison starts when both are known.</summary>
    public void OpenSide(Side side, string path)
    {
        if (side == Side.Left) { _pendingLeft = path; } else { _pendingRight = path; }
        _pendingLeft ??= LeftPath;
        _pendingRight ??= RightPath;
        if (_pendingLeft is not null && _pendingRight is not null)
        {
            Open(_pendingLeft, _pendingRight);
        }
        else
        {
            Status = side == Side.Left ? "Now choose the right file" : "Now choose the left file";
            if (side == Side.Left) { LeftPath = path; } else { RightPath = path; }
            OnPropertyChanged(nameof(LeftTitle));
            OnPropertyChanged(nameof(RightTitle));
        }
    }
}
