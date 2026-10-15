namespace Collaterale.Core.Diff;

/// <summary>A run of lines deleted at <see cref="LeftLine"/> and inserted at <see cref="RightLine"/>.</summary>
public sealed record MovedBlock(int Id, int LeftLine, int RightLine, int LineCount);

/// <summary>
/// Finds blocks that vanished in one place and reappeared in another. Works on the one-sided rows of a finished comparison:
/// every window of consecutive deleted lines is looked up (by hash) among the inserted lines and grown as far as it matches.
/// </summary>
public static class MoveDetector
{
    /// <summary>Shorter runs are too likely to be coincidence (a few closing braces).</summary>
    public const int MinLines = 3;

    /// <summary>A run must also contain this many non-whitespace characters to count.</summary>
    public const int MinCharacters = 24;

    /// <summary>Beyond this many one-sided lines per side the search is skipped to keep huge rewrites fast.</summary>
    private const int MaxLines = 200_000;

    public static IReadOnlyList<MovedBlock> Detect(List<DiffRow> rows, IReadOnlyList<string> left, IReadOnlyList<string> right, LineKeys keys)
    {
        // one-sided rows in file order: (row index, original line, key)
        var del = new List<(int Row, int Line, string Key)>();
        var ins = new List<(int Row, int Line, string Key)>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Kind == RowKind.LeftOnly) { del.Add((i, rows[i].Left, keys.Key(left[rows[i].Left]))); }
            else if (rows[i].Kind == RowKind.RightOnly) { ins.Add((i, rows[i].Right, keys.Key(right[rows[i].Right]))); }
        }

        if (del.Count < MinLines || ins.Count < MinLines || del.Count > MaxLines || ins.Count > MaxLines)
        {
            return [];
        }

        // index every MinLines-long window of consecutive inserted lines
        var index = new Dictionary<long, List<int>>();
        for (var p = 0; p + MinLines <= ins.Count; p++)
        {
            if (!Adjacent(ins, p, MinLines)) { continue; }
            var h = Hash(ins, p);
            if (!index.TryGetValue(h, out var list)) { index[h] = list = []; }
            list.Add(p);
        }

        var usedDel = new bool[del.Count];
        var usedIns = new bool[ins.Count];
        var moves = new List<MovedBlock>();

        for (var p = 0; p + MinLines <= del.Count; p++)
        {
            if (usedDel[p] || !Adjacent(del, p, MinLines) || !index.TryGetValue(Hash(del, p), out var candidates))
            {
                continue;
            }

            int bestLen = 0, bestQ = -1;
            foreach (var q in candidates)
            {
                if (usedIns[q]) { continue; }
                var len = 0;
                while (p + len < del.Count && q + len < ins.Count
                       && !usedDel[p + len] && !usedIns[q + len]
                       && del[p + len].Key == ins[q + len].Key
                       && (len == 0 || (del[p + len].Line == del[p + len - 1].Line + 1 && ins[q + len].Line == ins[q + len - 1].Line + 1)))
                {
                    len++;
                }

                if (len > bestLen) { bestLen = len; bestQ = q; }
            }

            if (bestLen < MinLines || Characters(del, p, bestLen) < MinCharacters)
            {
                continue;
            }

            var id = moves.Count;
            for (var k = 0; k < bestLen; k++)
            {
                usedDel[p + k] = usedIns[bestQ + k] = true;
                rows[del[p + k].Row] = rows[del[p + k].Row] with { Move = id };
                rows[ins[bestQ + k].Row] = rows[ins[bestQ + k].Row] with { Move = id };
            }

            moves.Add(new MovedBlock(id, del[p].Line, ins[bestQ].Line, bestLen));
            p += bestLen - 1;
        }

        return moves;
    }

    /// <summary>True when the window's lines are consecutive in the original file (a window must not straddle two separate edits).</summary>
    private static bool Adjacent(List<(int Row, int Line, string Key)> l, int start, int count)
    {
        for (var k = 1; k < count; k++)
        {
            if (l[start + k].Line != l[start + k - 1].Line + 1) { return false; }
        }

        return true;
    }

    private static long Hash(List<(int Row, int Line, string Key)> l, int start)
    {
        long h = 17;
        for (var k = 0; k < MinLines; k++)
        {
            h = unchecked(h * 1_000_003 + StringComparer.Ordinal.GetHashCode(l[start + k].Key));
        }

        return h;
    }

    private static int Characters(List<(int Row, int Line, string Key)> l, int start, int count)
    {
        var n = 0;
        for (var k = 0; k < count; k++)
        {
            foreach (var ch in l[start + k].Key) { if (!char.IsWhiteSpace(ch)) { n++; } }
        }

        return n;
    }
}
