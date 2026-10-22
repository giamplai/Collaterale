using Collaterale.Core.Diff;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collaterale.App.ViewModels;

/// <summary>
/// Isolate: the selected lines are realigned on their own. Lines selected on one side only stand alone with empty padding opposite;
/// lines selected on both sides are compared only with each other. It untangles places where the automatic alignment paired the wrong lines.
/// </summary>
public partial class TextCompareViewModel
{
    private readonly List<Isolation> _isolations = [];
    private (int Left, int Right) _isolatedLineCounts;

    public bool HasIsolations => _isolations.Count > 0;

    /// <summary>Set by the view: the display rows selected in each pane (null when a pane has no selection), or just the caret row when nothing is selected.</summary>
    public Func<(int First, int Last)?[]>? SelectionRowsProvider { get; set; }

    [RelayCommand]
    private void Isolate()
    {
        var sel = SelectionRowsProvider?.Invoke();
        if (sel is { Length: 2 }) { IsolateRows(sel[0], sel[1]); }
        else { Status = "Select the lines to isolate first"; }
    }

    private (Side Side, int First, int Last)? _alignAnchor;

    /// <summary>True between choosing "Align with…" and clicking the line to align with on the other pane.</summary>
    public bool AlignPending => _alignAnchor is not null;

    /// <summary>Align with…: remembers the current line, then the next line clicked on the other pane is placed opposite it.</summary>
    [RelayCommand]
    private void AlignWith()
    {
        var sel = SelectionRowsProvider?.Invoke();
        if (sel is not { Length: 2 }) { return; }
        // the pane that holds the selection (or the cursor) is the one the line is taken from
        var (side, range) = sel[0] is { } l ? (Side.Left, l) : (Side.Right, sel[1]!.Value);
        _alignAnchor = (side, range.First, range.Last);
        OnPropertyChanged(nameof(AlignPending));
        var n = range.Last - range.First + 1;
        Status = $"Align: now click the first line of the {(side == Side.Left ? "right" : "left")} pane to put opposite {(n > 1 ? $"these {n} lines" : "it")} (Esc cancels)";
    }

    public void CancelAlign()
    {
        if (_alignAnchor is null) { return; }
        _alignAnchor = null;
        OnPropertyChanged(nameof(AlignPending));
        Status = "Align cancelled";
    }

    /// <summary>Called when the cursor lands on a row: if an alignment is waiting for its second line and this is the other pane, finish it.</summary>
    private void TryCompleteAlign(Side side, int row)
    {
        if (_alignAnchor is not { } a || a.Side == side) { return; }
        _alignAnchor = null;
        OnPropertyChanged(nameof(AlignPending));
        // the same number of rows as the chosen stretch, starting at the clicked one, face it
        (int, int)? mine = (a.First, a.Last), other = (row, row + (a.Last - a.First));
        if (a.Side == Side.Left) { IsolateRows(mine, other); } else { IsolateRows(other, mine); }
    }

    /// <summary>Removes only the manual alignment (isolation or Align with…) that holds the line under the cursor or the selected lines.</summary>
    [RelayCommand]
    private void ClearAlignmentHere()
    {
        if (Comparison is not { } c || SelectionRowsProvider?.Invoke() is not { Length: 2 } sel) { return; }
        var before = _isolations.Count;
        foreach (var (range, leftSide) in new[] { (sel[0], true), (sel[1], false) })
        {
            if (range is not { } r) { continue; }
            for (var d = Math.Max(0, r.First); d <= Math.Min(r.Last, DisplayMap.Count - 1); d++)
            {
                if (DisplayMap[d] < 0 || DisplayMap[d] >= c.Rows.Count) { continue; }
                var line = leftSide ? c.Rows[DisplayMap[d]].Left : c.Rows[DisplayMap[d]].Right;
                if (line < 0) { continue; }
                _isolations.RemoveAll(o => leftSide ? line >= o.LeftStart && line < o.LeftEnd : line >= o.RightStart && line < o.RightEnd);
            }
        }

        if (_isolations.Count == before) { Status = "No manual alignment on this line"; return; }
        OnPropertyChanged(nameof(HasIsolations));
        Recompare();
        Status = "Manual alignment removed from this line";
    }

    [RelayCommand]
    private void ClearIsolations()
    {
        if (_isolations.Count == 0) { return; }
        _isolations.Clear();
        OnPropertyChanged(nameof(HasIsolations));
        Recompare();
        Status = "Isolation removed: the alignment is automatic again";
    }

    /// <summary>Isolates the lines on the given display rows of each side (null = nothing selected on that side).</summary>
    public void IsolateRows((int First, int Last)? left, (int First, int Last)? right)
    {
        if (Comparison is not { } c || (left is null && right is null)) { return; }

        // the comparison rows each selection spans (display rows can be fold markers, which stand for hidden rows)
        (int From, int To)? Span((int First, int Last)? sel)
        {
            if (sel is not { } s) { return null; }
            int from = -1, to = -1;
            for (var d = Math.Max(0, s.First); d <= Math.Min(s.Last, DisplayMap.Count - 1); d++)
            {
                if (DisplayMap[d] < 0) { continue; }
                if (from < 0) { from = DisplayMap[d]; }
                to = DisplayMap[d];
            }

            return from < 0 ? null : (from, to);
        }

        static (int Start, int Count) Lines(IReadOnlyList<DiffRow> rows, (int From, int To) span, bool leftSide)
        {
            int first = -1, count = 0;
            for (var r = span.From; r <= span.To; r++)
            {
                var line = leftSide ? rows[r].Left : rows[r].Right;
                if (line < 0) { continue; }
                if (first < 0) { first = line; }
                count++;
            }

            return (first, count);
        }

        // where a side's text continues at a row: the next line of that side at or after it
        int InsertionPoint(IReadOnlyList<DiffRow> rows, int row, bool leftSide, int total)
        {
            for (var r = row; r < rows.Count; r++)
            {
                var line = leftSide ? rows[r].Left : rows[r].Right;
                if (line >= 0) { return line; }
            }

            return total;
        }

        var (ls, rs) = (Span(left), Span(right));
        if (ls is null && rs is null) { return; }
        var anchor = (ls ?? rs)!.Value;
        var (lstart, lcount) = ls is { } a ? Lines(c.Rows, a, true) : (InsertionPoint(c.Rows, anchor.From, true, c.LeftLines.Count), 0);
        var (rstart, rcount) = rs is { } b ? Lines(c.Rows, b, false) : (InsertionPoint(c.Rows, anchor.From, false, c.RightLines.Count), 0);
        if (lcount == 0 && rcount == 0) { Status = "Nothing to isolate: the selection holds no lines"; return; }
        if (lcount == 0) { lstart = InsertionPoint(c.Rows, rs!.Value.From, true, c.LeftLines.Count); }
        if (rcount == 0) { rstart = InsertionPoint(c.Rows, ls!.Value.From, false, c.RightLines.Count); }

        var added = new Isolation(lstart, lcount, rstart, rcount);
        // an older isolation that overlaps the new one, or that would end up out of order with it, is replaced
        _isolations.RemoveAll(o =>
        {
            var before = o.LeftEnd <= added.LeftStart && o.RightEnd <= added.RightStart;
            var after = added.LeftEnd <= o.LeftStart && added.RightEnd <= o.RightStart;
            return !before && !after;
        });
        _isolations.Add(added);
        _isolations.Sort((x, y) => x.LeftStart != y.LeftStart ? x.LeftStart.CompareTo(y.LeftStart) : x.RightStart.CompareTo(y.RightStart));
        _isolatedLineCounts = (Left.Lines.Count, Right.Lines.Count);
        OnPropertyChanged(nameof(HasIsolations));
        Recompare();
        Status = $"Isolated {lcount} left and {rcount} right line{(lcount + rcount == 1 ? "" : "s")}";
    }

    /// <summary>The line numbers of an isolation stay valid only while no line is added or removed; then the automatic alignment returns.</summary>
    private void DropStaleIsolations()
    {
        if (_isolations.Count > 0 && (Left.Lines.Count, Right.Lines.Count) != _isolatedLineCounts)
        {
            _isolations.Clear();
            OnPropertyChanged(nameof(HasIsolations));
        }
    }
}
