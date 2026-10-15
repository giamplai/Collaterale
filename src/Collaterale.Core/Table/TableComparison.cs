using System.Globalization;
using Collaterale.Core.Diff;

namespace Collaterale.Core.Table;

public enum TableRowKind { Same, Changed, LeftOnly, RightOnly }

public enum ColumnMatch
{
    /// <summary>Column n on the left is compared with column n on the right.</summary>
    Position,

    /// <summary>Columns are matched by their header text (requires a header row); columns missing on one side are flagged.</summary>
    Header,
}

public sealed record TableOptions
{
    public bool HasHeader { get; init; } = true;

    /// <summary>Zero-based left-table column indexes that identify a row (e.g. an ID). Empty = rows are aligned by their content.</summary>
    public IReadOnlyList<int> KeyColumns { get; init; } = [];

    public ColumnMatch Columns { get; init; } = ColumnMatch.Position;
    public bool IgnoreCase { get; init; }
    public bool TrimWhitespace { get; init; }

    /// <summary>When set, cells that both parse as numbers are equal if they differ by at most this much (0 = equal numeric value, "1.0" = "1").</summary>
    public double? NumericTolerance { get; init; }

    /// <summary>Zero-based indexes (in the merged column layout) that are not compared.</summary>
    public IReadOnlySet<int> IgnoredColumns { get; init; } = new HashSet<int>();

    public static TableOptions Default { get; } = new();
}

/// <summary>One line of the result: which rows face each other and which cells differ.</summary>
public sealed record TableRow(TableRowKind Kind, int Left, int Right, bool[] CellDiffs)
{
    public bool IsDifference => Kind != TableRowKind.Same;
}

public sealed class TableComparison
{
    private TableComparison(string[] columnNames, int?[] leftIndex, int?[] rightIndex, string[][] left, string[][] right, List<TableRow> rows, TableOptions options)
    {
        ColumnNames = columnNames;
        LeftColumn = leftIndex;
        RightColumn = rightIndex;
        LeftRows = left;
        RightRows = right;
        Rows = rows;
        Options = options;
    }

    /// <summary>Display names of the merged columns (the header text, or "A", "B", … without a header).</summary>
    public IReadOnlyList<string> ColumnNames { get; }

    /// <summary>For each merged column, the index of the column in the left / right table (null when that side lacks it).</summary>
    public IReadOnlyList<int?> LeftColumn { get; }

    public IReadOnlyList<int?> RightColumn { get; }

    public IReadOnlyList<string[]> LeftRows { get; }
    public IReadOnlyList<string[]> RightRows { get; }
    public IReadOnlyList<TableRow> Rows { get; }
    public TableOptions Options { get; }

    public int ColumnCount => ColumnNames.Count;
    public int ChangedRows => Rows.Count(r => r.Kind == TableRowKind.Changed);
    public int LeftOnlyRows => Rows.Count(r => r.Kind == TableRowKind.LeftOnly);
    public int RightOnlyRows => Rows.Count(r => r.Kind == TableRowKind.RightOnly);
    public int ChangedCells => Rows.Where(r => r.Kind == TableRowKind.Changed).Sum(r => r.CellDiffs.Count(x => x));
    public bool AreEqual => Rows.All(r => !r.IsDifference) && !ColumnsDiffer;

    /// <summary>True when the two tables do not have the same set of columns (header matching).</summary>
    public bool ColumnsDiffer => LeftColumn.Any(c => c is null) || RightColumn.Any(c => c is null);

    public string? Cell(int row, int column, bool left)
    {
        var r = Rows[row];
        var rowIndex = left ? r.Left : r.Right;
        var col = (left ? LeftColumn : RightColumn)[column];
        if (rowIndex < 0 || col is null)
        {
            return null;
        }

        var cells = (left ? LeftRows : RightRows)[rowIndex];
        return col.Value < cells.Length ? cells[col.Value] : "";
    }

    public static TableComparison Compare(ParsedTable left, ParsedTable right, TableOptions? options = null)
    {
        options ??= TableOptions.Default;
        string[][] lrows = [.. left.Rows], rrows = [.. right.Rows];

        string[] lhead = [], rhead = [];
        if (options.HasHeader)
        {
            if (lrows.Length > 0) { lhead = lrows[0]; lrows = lrows[1..]; }
            if (rrows.Length > 0) { rhead = rrows[0]; rrows = rrows[1..]; }
        }

        var (names, lcol, rcol) = MapColumns(left.ColumnCount, right.ColumnCount, lhead, rhead, options);
        var rows = options.KeyColumns.Count > 0
            ? AlignByKey(lrows, rrows, lcol, rcol, options)
            : AlignByContent(lrows, rrows, lcol, rcol, options);

        // cell level differences for rows that exist on both sides
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Left < 0 || r.Right < 0)
            {
                continue;
            }

            var diffs = new bool[names.Length];
            var any = false;
            for (var c = 0; c < names.Length; c++)
            {
                if (options.IgnoredColumns.Contains(c)) { continue; }
                // matched by header: a column only one side has is a structural difference (ColumnsDiffer), not a difference in every row
                if (options.Columns == ColumnMatch.Header && (lcol[c] is null || rcol[c] is null)) { continue; }
                var a = CellAt(lrows[r.Left], lcol[c]);
                var b = CellAt(rrows[r.Right], rcol[c]);
                if (!CellsEqual(a, b, options)) { diffs[c] = any = true; }
            }

            rows[i] = r with { Kind = any ? TableRowKind.Changed : TableRowKind.Same, CellDiffs = diffs };
        }

        return new TableComparison(names, lcol, rcol, lrows, rrows, rows, options);
    }

    private static string? CellAt(string[] row, int? col) => col is null ? null : col.Value < row.Length ? row[col.Value] : "";

    private static (string[] Names, int?[] Left, int?[] Right) MapColumns(int lcount, int rcount, string[] lhead, string[] rhead, TableOptions o)
    {
        if (o.Columns == ColumnMatch.Header && o.HasHeader && lhead.Length > 0 && rhead.Length > 0)
        {
            var names = new List<string>();
            var l = new List<int?>();
            var r = new List<int?>();
            var used = new HashSet<int>();
            var cmp = o.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            for (var i = 0; i < lhead.Length; i++)
            {
                var j = Array.FindIndex(rhead, (h) => cmp.Equals(h.Trim(), lhead[i].Trim()));
                while (j >= 0 && used.Contains(j))
                {
                    j = Array.FindIndex(rhead, j + 1, h => cmp.Equals(h.Trim(), lhead[i].Trim()));
                }

                if (j >= 0) { used.Add(j); }
                names.Add(lhead[i]);
                l.Add(i);
                r.Add(j >= 0 ? j : null);
            }

            for (var j = 0; j < rhead.Length; j++)
            {
                if (!used.Contains(j))
                {
                    names.Add(rhead[j]);
                    l.Add(null);
                    r.Add(j);
                }
            }

            return ([.. names], [.. l], [.. r]);
        }

        var n = Math.Max(lcount, rcount);
        var nm = Enumerable.Range(0, n).Select(i => i < lhead.Length && lhead[i].Length > 0 ? lhead[i] : i < rhead.Length && rhead[i].Length > 0 ? rhead[i] : ColumnLetter(i)).ToArray();
        return (nm, [.. Enumerable.Range(0, n).Select(i => i < lcount ? (int?)i : null)], [.. Enumerable.Range(0, n).Select(i => i < rcount ? (int?)i : null)]);
    }

    public static string ColumnLetter(int index)
    {
        var s = "";
        for (var i = index; i >= 0; i = i / 26 - 1) { s = (char)('A' + i % 26) + s; }
        return s;
    }

    public static bool CellsEqual(string? a, string? b, TableOptions o)
    {
        if (a is null || b is null) { return a is null && b is null; }
        if (o.TrimWhitespace) { a = a.Trim(); b = b.Trim(); }
        if (o.NumericTolerance is { } tol
            && double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return Math.Abs(x - y) <= tol;
        }

        return string.Equals(a, b, o.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string KeyOf(string[] row, int?[] col, IReadOnlyList<int> keys, TableOptions o)
    {
        var parts = keys.Select(k => CellAt(row, k < col.Length ? col[k] : null) ?? "");
        var key = string.Join('\u001f', parts.Select(p => o.TrimWhitespace ? p.Trim() : p));
        return o.IgnoreCase ? key.ToUpperInvariant() : key;
    }

    private static List<TableRow> AlignByKey(string[][] l, string[][] r, int?[] lcol, int?[] rcol, TableOptions o)
    {
        // duplicates of a key are matched in order of appearance
        var rightByKey = new Dictionary<string, Queue<int>>();
        for (var j = 0; j < r.Length; j++)
        {
            var k = KeyOf(r[j], rcol, o.KeyColumns, o);
            if (!rightByKey.TryGetValue(k, out var q)) { rightByKey[k] = q = new Queue<int>(); }
            q.Enqueue(j);
        }

        var matchOfLeft = new int[l.Length];
        var rightMatched = new bool[r.Length];
        for (var i = 0; i < l.Length; i++)
        {
            matchOfLeft[i] = -1;
            if (rightByKey.TryGetValue(KeyOf(l[i], lcol, o.KeyColumns, o), out var q) && q.Count > 0)
            {
                var j = q.Dequeue();
                matchOfLeft[i] = j;
                rightMatched[j] = true;
            }
        }

        // left order; a right-only row goes right after the left row whose partner is the closest preceding right row
        var rows = new List<TableRow>();
        var pendingRight = new List<int>();
        var emptyDiffs = Array.Empty<bool>();
        var nextRight = 0;
        void FlushRightUpTo(int limit)
        {
            while (nextRight < limit)
            {
                if (!rightMatched[nextRight]) { rows.Add(new TableRow(TableRowKind.RightOnly, -1, nextRight, emptyDiffs)); }
                nextRight++;
            }
        }

        for (var i = 0; i < l.Length; i++)
        {
            if (matchOfLeft[i] >= 0)
            {
                FlushRightUpTo(matchOfLeft[i]);
                rows.Add(new TableRow(TableRowKind.Same, i, matchOfLeft[i], emptyDiffs));
                nextRight = Math.Max(nextRight, matchOfLeft[i] + 1);
            }
            else
            {
                rows.Add(new TableRow(TableRowKind.LeftOnly, i, -1, emptyDiffs));
            }
        }

        FlushRightUpTo(r.Length);
        _ = pendingRight;
        return rows;
    }

    private static List<TableRow> AlignByContent(string[][] l, string[][] r, int?[] lcol, int?[] rcol, TableOptions o)
    {
        // Reuse the text comparison: a row becomes one line of its normalized cells.
        string Flatten(string[] row, int?[] col)
        {
            var cells = new List<string>(col.Length);
            for (var c = 0; c < col.Length; c++)
            {
                if (o.IgnoredColumns.Contains(c)) { continue; }
                if (o.Columns == ColumnMatch.Header && (lcol[c] is null || rcol[c] is null)) { continue; }
                var v = CellAt(row, col[c]) ?? "";
                cells.Add(o.TrimWhitespace ? v.Trim() : v);
            }

            return string.Join('\t', cells);
        }

        var tc = TextComparison.Compare(
            [.. l.Select(x => Flatten(x, lcol))], [.. r.Select(x => Flatten(x, rcol))],
            new DiffOptions { IgnoreCase = o.IgnoreCase, DetectMoves = false });

        var empty = Array.Empty<bool>();
        return [.. tc.Rows.Select(row => new TableRow(
            row.Kind switch { RowKind.LeftOnly => TableRowKind.LeftOnly, RowKind.RightOnly => TableRowKind.RightOnly, RowKind.Changed => TableRowKind.Changed, _ => TableRowKind.Same },
            row.Left, row.Right, empty))];
    }
}
