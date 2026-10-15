using Collaterale.Core.Search;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collaterale.App.ViewModels;

public enum FindSides { Both, Left, Right }

/// <summary>One occurrence in a side's real lines (zero-based line and column).</summary>
public readonly record struct FindHit(Side Side, int Line, int Start, int Length);

/// <summary>Find and replace in the two sides of a text compare.</summary>
public partial class TextCompareViewModel
{
    [ObservableProperty] private bool _findVisible;
    [ObservableProperty] private bool _replaceMode;
    [ObservableProperty] private string _findText = "";
    [ObservableProperty] private string _replaceText = "";
    [ObservableProperty] private bool _findRegex;
    [ObservableProperty] private bool _findMatchCase;
    [ObservableProperty] private bool _findWholeWords;
    [ObservableProperty] private bool _findWrap = true;
    [ObservableProperty] private bool _findCloseAutomatically;
    [ObservableProperty] private FindSides _findSides = FindSides.Both;
    [ObservableProperty] private string _findStatus = "";

    public static FindSides[] FindSideChoices { get; } = Enum.GetValues<FindSides>();

    /// <summary>Searches the user made before, newest first (the drop-down of the find box).</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> FindHistory { get; } = [];

    /// <summary>Where the cursor is, in real line numbers; set by the view. Start and End differ while text is selected.</summary>
    public (Side Side, int Line, int StartColumn, int EndColumn) Caret { get; set; } = (Side.Left, 0, 0, 0);

    /// <summary>The text under the cursor (the selection, or the word), for "initialize text to find".</summary>
    public Func<string>? CurrentWordProvider { get; set; }

    /// <summary>When on, opening the panel fills the find box with the current word.</summary>
    public bool InitFindFromWord { get => _initFind ?? Prefs.TextEditing.InitFindFromWord; set => _initFind = value; }

    private bool? _initFind;

    /// <summary>Raised when a match should be selected in a pane.</summary>
    public event Action<FindHit>? FindHitSelected;

    /// <summary>Every match of the current search, for highlighting; empty when the panel is closed.</summary>
    public IReadOnlyList<FindHit> FindMatches { get; private set; } = [];

    public event EventHandler? FindMatchesChanged;

    private FindHit? _lastHit;

    private SearchOptions SearchOptions => new(FindRegex, FindMatchCase, FindWholeWords, FindWrap);

    partial void OnFindTextChanged(string value) => RefreshFindMatches();
    partial void OnFindRegexChanged(bool value) => RefreshFindMatches();
    partial void OnFindMatchCaseChanged(bool value) => RefreshFindMatches();
    partial void OnFindWholeWordsChanged(bool value) => RefreshFindMatches();
    partial void OnFindSidesChanged(FindSides value) => RefreshFindMatches();
    partial void OnFindVisibleChanged(bool value) => RefreshFindMatches();

    [RelayCommand]
    private void ShowFind(bool replace = false)
    {
        ReplaceMode = replace;
        if (InitFindFromWord && CurrentWordProvider?.Invoke() is { Length: > 0 } word && !word.Contains('\n')) { FindText = word; }
        FindVisible = true;
    }

    [RelayCommand]
    private void ShowReplace() => ShowFind(replace: true);

    [RelayCommand]
    private void CloseFind() => FindVisible = false;

    /// <summary>The lines the search runs over, with where each side starts.</summary>
    private (List<string> Lines, int RightOffset) SearchSpace() => FindSides switch
    {
        FindSides.Left => ([.. Left.Lines], 0),
        FindSides.Right => ([.. Right.Lines], int.MinValue),
        _ => ([.. Left.Lines, .. Right.Lines], Left.Lines.Count),
    };

    private FindHit ToHit(SearchMatch m, int rightOffset) => FindSides switch
    {
        FindSides.Left => new FindHit(Side.Left, m.Line, m.Start, m.Length),
        FindSides.Right => new FindHit(Side.Right, m.Line, m.Start, m.Length),
        _ => m.Line < rightOffset ? new FindHit(Side.Left, m.Line, m.Start, m.Length) : new FindHit(Side.Right, m.Line - rightOffset, m.Start, m.Length),
    };

    /// <summary>The cursor as a position in the search space (one side, or the left side followed by the right).</summary>
    private (int Line, int Column) SpacePosition(bool end, int rightOffset)
    {
        var (side, line, start, stop) = Caret;
        var column = end ? stop : start;
        return FindSides switch
        {
            FindSides.Left => (side == Side.Left ? line : 0, side == Side.Left ? column : 0),
            FindSides.Right => (side == Side.Right ? line : 0, side == Side.Right ? column : 0),
            _ => (side == Side.Left ? line : rightOffset + line, column),
        };
    }

    private void RefreshFindMatches()
    {
        var hits = new List<FindHit>();
        if (FindVisible && FindText.Length > 0 && TextSearch.Validate(FindText, SearchOptions) is null)
        {
            if (FindSides != FindSides.Right) { hits.AddRange(TextSearch.All(Left.Lines, FindText, SearchOptions).Select(m => new FindHit(Side.Left, m.Line, m.Start, m.Length))); }
            if (FindSides != FindSides.Left) { hits.AddRange(TextSearch.All(Right.Lines, FindText, SearchOptions).Select(m => new FindHit(Side.Right, m.Line, m.Start, m.Length))); }
        }

        FindMatches = hits;
        _lastHit = null;
        FindStatus = TextSearch.Validate(FindText, SearchOptions) is { } bad ? "Not a valid regular expression: " + bad
            : FindText.Length == 0 ? "" : hits.Count == 0 ? "No matches" : $"{hits.Count:N0} match{(hits.Count == 1 ? "" : "es")}";
        FindMatchesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Remember()
    {
        if (FindText.Length == 0) { return; }
        FindHistory.Remove(FindText);
        FindHistory.Insert(0, FindText);
        while (FindHistory.Count > 20) { FindHistory.RemoveAt(FindHistory.Count - 1); }
    }

    private void Select(FindHit hit)
    {
        _lastHit = hit;
        Caret = (hit.Side, hit.Line, hit.Start, hit.Start + hit.Length);
        FindHitSelected?.Invoke(hit);
    }

    [RelayCommand]
    public void FindNext()
    {
        FlushEdits?.Invoke();
        if (FindText.Length == 0 || TextSearch.Validate(FindText, SearchOptions) is not null) { return; }
        Remember();
        var (lines, right) = SearchSpace();
        var (line, col) = SpacePosition(end: true, right);
        if (TextSearch.Next(lines, FindText, SearchOptions, Math.Clamp(line, 0, Math.Max(lines.Count - 1, 0)), col) is { } m)
        {
            Select(ToHit(m, right));
            FindStatus = "";
            if (FindCloseAutomatically) { FindVisible = false; }
        }
        else
        {
            FindStatus = FindWrap ? "Not found" : "Not found (end reached)";
        }
    }

    [RelayCommand]
    public void FindPrevious()
    {
        FlushEdits?.Invoke();
        if (FindText.Length == 0 || TextSearch.Validate(FindText, SearchOptions) is not null) { return; }
        Remember();
        var (lines, right) = SearchSpace();
        var (line, col) = SpacePosition(end: false, right);
        if (TextSearch.Previous(lines, FindText, SearchOptions, Math.Clamp(line, 0, Math.Max(lines.Count - 1, 0)), col) is { } m)
        {
            Select(ToHit(m, right));
            FindStatus = "";
            if (FindCloseAutomatically) { FindVisible = false; }
        }
        else
        {
            FindStatus = FindWrap ? "Not found" : "Not found (start reached)";
        }
    }

    /// <summary>Selects the first line holding a match and reports how many lines hold one.</summary>
    [RelayCommand]
    private void SelectAllMatches()
    {
        var lines = FindMatches.Select(h => (h.Side, h.Line)).Distinct().Count();
        FindStatus = lines == 0 ? "No matches" : $"{lines:N0} line{(lines == 1 ? "" : "s")} contain a match (all highlighted)";
        if (FindMatches.Count > 0) { Select(FindMatches[0]); }
    }

    private bool CanReplaceIn(Side side) => !(side == Side.Left ? LeftLocked : RightLocked);

    /// <summary>Replaces the selected match (then moves on to the next one); with nothing selected it just finds the next match.</summary>
    [RelayCommand]
    private void Replace()
    {
        if (_lastHit is { } hit && Caret.Side == hit.Side && Caret.Line == hit.Line && Caret.StartColumn == hit.Start && CanReplaceIn(hit.Side))
        {
            var side = hit.Side == Side.Left ? Left : Right;
            if (hit.Line < side.Lines.Count)
            {
                PushUndo();
                side.Lines[hit.Line] = TextSearch.ReplaceIn(side.Lines[hit.Line], new SearchMatch(hit.Line, hit.Start, hit.Length), FindText, ReplaceText, SearchOptions);
                side.Dirty = true;
                Caret = (hit.Side, hit.Line, hit.Start + ReplaceText.Length, hit.Start + ReplaceText.Length);
                Recompare();
                RefreshFindMatches();
            }
        }

        FindNext();
    }

    [RelayCommand]
    private void ReplaceAll()
    {
        FlushEdits?.Invoke();
        if (FindText.Length == 0 || TextSearch.Validate(FindText, SearchOptions) is not null) { return; }
        Remember();
        var total = 0;
        var undoPushed = false;
        foreach (var (side, which) in new[] { (Left, Side.Left), (Right, Side.Right) })
        {
            if (!CanReplaceIn(which) || (FindSides == FindSides.Left && which == Side.Right) || (FindSides == FindSides.Right && which == Side.Left)) { continue; }
            var (lines, count) = TextSearch.ReplaceAll(side.Lines, FindText, ReplaceText, SearchOptions);
            if (count == 0) { continue; }
            if (!undoPushed) { PushUndo(); undoPushed = true; }
            side.Lines = lines;
            side.Dirty = true;
            total += count;
        }

        if (total > 0) { Recompare(); }
        RefreshFindMatches();
        FindStatus = total == 0 ? "Nothing to replace" : $"Replaced {total:N0} occurrence{(total == 1 ? "" : "s")}";
    }

    /// <summary>The display row that shows one real line of a side, or -1 when it is folded away.</summary>
    public int RowOfLine(Side side, int line)
    {
        var rows = side == Side.Left ? LeftRows : RightRows;
        for (var i = 0; i < rows.Count; i++)
        {
            if (!rows[i].IsFiller && rows[i].FoldCount == 0 && rows[i].LineNumber == line + 1) { return i; }
        }

        return -1;
    }

    /// <summary>The real line (zero-based) shown on a display row; for a filler, the line that follows it.</summary>
    public int LineOfRow(Side side, int row)
    {
        var rows = side == Side.Left ? LeftRows : RightRows;
        for (var i = Math.Clamp(row, 0, Math.Max(rows.Count - 1, 0)); i < rows.Count; i++)
        {
            if (!rows[i].IsFiller && rows[i].LineNumber > 0) { return rows[i].LineNumber - 1; }
        }

        return (side == Side.Left ? Left.Lines.Count : Right.Lines.Count);
    }
}
