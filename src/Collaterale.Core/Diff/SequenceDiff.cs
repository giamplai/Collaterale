namespace Collaterale.Core.Diff;

/// <summary>A changed region: <c>a[AStart..AStart+ALength)</c> is replaced by <c>b[BStart..BStart+BLength)</c>.</summary>
public readonly record struct DiffBlock(int AStart, int ALength, int BStart, int BLength)
{
    public int AEnd => AStart + ALength;
    public int BEnd => BStart + BLength;
    public bool IsInsertion => ALength == 0;
    public bool IsDeletion => BLength == 0;
}

public enum DiffAlgorithm
{
    /// <summary>Minimal edit script (shortest), the classic choice.</summary>
    Myers,

    /// <summary>Anchors on lines that occur exactly once on both sides; usually more readable for source code, not always minimal.</summary>
    Patience,

    /// <summary>
    /// Anchors on the rarest lines of the old text instead of only unique ones, so repeated lines (braces, blank lines) do not
    /// confuse it. Usually the best readability on source code; falls back to Myers when every line is too common.
    /// </summary>
    Histogram,
}

/// <summary>Diffs two integer sequences (lines mapped to ids). Everything else in the app builds on this.</summary>
public static class SequenceDiff
{
    /// <summary>Beyond this many edits per region the search stops being exact and splits heuristically, keeping huge unrelated files fast.</summary>
    public const int DefaultMaxEdits = 4000;

    /// <summary>From this combined length on, lines that exist on one side only are removed before the search (see <see cref="DiffReduced"/>).</summary>
    public const int ReduceAbove = 10_000;

    public static List<DiffBlock> Diff(int[] a, int[] b, DiffAlgorithm algorithm = DiffAlgorithm.Myers, int maxEdits = DefaultMaxEdits, int reduceAbove = ReduceAbove)
    {
        if (a.Length + b.Length >= reduceAbove && DiffReduced(a, b, algorithm, maxEdits) is { } reduced)
        {
            return reduced;
        }

        var blocks = new List<DiffBlock>();
        var ctx = new Context(a, b, blocks, maxEdits);
        switch (algorithm)
        {
            case DiffAlgorithm.Patience:
                Patience(ctx, 0, a.Length, 0, b.Length);
                break;
            case DiffAlgorithm.Histogram:
                Histogram(ctx, 0, a.Length, 0, b.Length);
                break;
            default:
                Myers(ctx, 0, a.Length, 0, b.Length);
                break;
        }

        return Normalize(blocks);
    }

    /// <summary>
    /// Large inputs: a line that does not occur on the other side can never be part of a common subsequence, so it is certainly
    /// changed. Removing those lines first shrinks the problem a lot when the texts differ much (a re-formatted or regenerated
    /// file is otherwise the slowest case for the exact search), and the result maps back to the original line numbers.
    /// Returns null when almost every line is shared anyway (nothing to gain).
    /// </summary>
    private static List<DiffBlock>? DiffReduced(int[] a, int[] b, DiffAlgorithm algorithm, int maxEdits)
    {
        var inA = new HashSet<int>(a);
        var inB = new HashSet<int>(b);
        var keptA = new List<int>(a.Length);
        var keptB = new List<int>(b.Length);
        for (var i = 0; i < a.Length; i++) { if (inB.Contains(a[i])) { keptA.Add(i); } }
        for (var j = 0; j < b.Length; j++) { if (inA.Contains(b[j])) { keptB.Add(j); } }
        if ((keptA.Count + keptB.Count) * 10L > (a.Length + b.Length) * 8L)
        {
            return null;
        }

        var ra = new int[keptA.Count];
        var rb = new int[keptB.Count];
        for (var k = 0; k < ra.Length; k++) { ra[k] = a[keptA[k]]; }
        for (var k = 0; k < rb.Length; k++) { rb[k] = b[keptB[k]]; }

        var changedA = new bool[a.Length];
        var changedB = new bool[b.Length];
        Array.Fill(changedA, true);
        Array.Fill(changedB, true);
        var matchedA = new bool[ra.Length];
        var matchedB = new bool[rb.Length];
        Array.Fill(matchedA, true);
        Array.Fill(matchedB, true);
        foreach (var blk in Diff(ra, rb, algorithm, maxEdits, reduceAbove: int.MaxValue))
        {
            for (var k = blk.AStart; k < blk.AEnd; k++) { matchedA[k] = false; }
            for (var k = blk.BStart; k < blk.BEnd; k++) { matchedB[k] = false; }
        }

        for (var k = 0; k < ra.Length; k++) { if (matchedA[k]) { changedA[keptA[k]] = false; } }
        for (var k = 0; k < rb.Length; k++) { if (matchedB[k]) { changedB[keptB[k]] = false; } }

        // the unchanged lines of both sides pair up one to one, in order: walk them in step and emit the runs between them
        var blocks = new List<DiffBlock>();
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            int sx = x, sy = y;
            while (x < a.Length && changedA[x]) { x++; }
            while (y < b.Length && changedB[y]) { y++; }
            if (x > sx || y > sy) { blocks.Add(new DiffBlock(sx, x - sx, sy, y - sy)); }
            if (x < a.Length && y < b.Length) { x++; y++; }
        }

        return blocks;
    }

    /// <summary>Sorts blocks and merges blocks that touch, so callers always see maximal changed regions.</summary>
    private static List<DiffBlock> Normalize(List<DiffBlock> blocks)
    {
        blocks.Sort((x, y) => x.AStart != y.AStart ? x.AStart.CompareTo(y.AStart) : x.BStart.CompareTo(y.BStart));
        var result = new List<DiffBlock>(blocks.Count);
        foreach (var blk in blocks)
        {
            if (blk.ALength == 0 && blk.BLength == 0)
            {
                continue;
            }

            if (result.Count > 0 && result[^1].AEnd == blk.AStart && result[^1].BEnd == blk.BStart)
            {
                var last = result[^1];
                result[^1] = new DiffBlock(last.AStart, last.ALength + blk.ALength, last.BStart, last.BLength + blk.BLength);
            }
            else
            {
                result.Add(blk);
            }
        }

        return result;
    }

    private sealed class Context(int[] a, int[] b, List<DiffBlock> blocks, int maxEdits)
    {
        public readonly int[] A = a;
        public readonly int[] B = b;
        public readonly List<DiffBlock> Blocks = blocks;
        public readonly int MaxEdits = maxEdits;
        public int[] Vf = [];
        public int[] Vb = [];
    }

    // ───────────────────────────── Myers (linear space) ─────────────────────────────

    private static void Myers(Context c, int aLo, int aHi, int bLo, int bHi)
    {
        // common prefix / suffix cost nothing and shrink the problem
        while (aLo < aHi && bLo < bHi && c.A[aLo] == c.B[bLo]) { aLo++; bLo++; }
        while (aLo < aHi && bLo < bHi && c.A[aHi - 1] == c.B[bHi - 1]) { aHi--; bHi--; }

        if (aLo == aHi && bLo == bHi)
        {
            return;
        }

        if (aLo == aHi || bLo == bHi)
        {
            c.Blocks.Add(new DiffBlock(aLo, aHi - aLo, bLo, bHi - bLo));
            return;
        }

        var (x0, y0, x1, y1) = MiddleSnake(c, aLo, aHi, bLo, bHi);
        var n = aHi - aLo;
        var m = bHi - bLo;

        // Guard: a split that does not shrink the problem would recurse forever, so treat the region as replaced.
        if ((x0 == 0 && y0 == 0 && x1 == n && y1 == m) || (x0 == 0 && y0 == 0 && x1 == 0 && y1 == 0) || (x0 == n && y0 == m))
        {
            c.Blocks.Add(new DiffBlock(aLo, n, bLo, m));
            return;
        }

        Myers(c, aLo, aLo + x0, bLo, bLo + y0);
        Myers(c, aLo + x1, aHi, bLo + y1, bHi);
    }

    /// <summary>Finds the middle snake of the edit graph; returns its start and end relative to the region origin.</summary>
    private static (int X0, int Y0, int X1, int Y1) MiddleSnake(Context c, int aLo, int aHi, int bLo, int bHi)
    {
        int n = aHi - aLo, m = bHi - bLo;
        var delta = n - m;
        var odd = (delta & 1) != 0;
        var max = Math.Min((n + m + 1) / 2, c.MaxEdits);
        var offset = max + 2;
        var size = 2 * max + 5;
        if (c.Vf.Length < size)
        {
            c.Vf = new int[size];
            c.Vb = new int[size];
        }

        var vf = c.Vf;
        var vb = c.Vb;
        vf[offset + 1] = 0;
        vb[offset + 1] = 0;
        int bestX = 0, bestY = 0, bestSum = -1;

        for (var d = 0; d <= max; d++)
        {
            for (var k = -d; k <= d; k += 2)
            {
                var idx = offset + k;
                var x = k == -d || (k != d && vf[idx - 1] < vf[idx + 1]) ? vf[idx + 1] : vf[idx - 1] + 1;
                var y = x - k;
                if (x > n || y > m || y < 0)
                {
                    // off the grid: clamp by marking an unreachable-but-harmless value
                    vf[idx] = Math.Min(x, n);
                    continue;
                }

                int x0 = x, y0 = y;
                while (x < n && y < m && c.A[aLo + x] == c.B[bLo + y]) { x++; y++; }
                vf[idx] = x;
                if (x + y > bestSum) { bestSum = x + y; bestX = x; bestY = y; }

                if (odd && k >= delta - (d - 1) && k <= delta + (d - 1) && x + vb[offset + (delta - k)] >= n)
                {
                    return (x0, y0, x, y);
                }
            }

            for (var k = -d; k <= d; k += 2)
            {
                var idx = offset + k;
                var x = k == -d || (k != d && vb[idx - 1] < vb[idx + 1]) ? vb[idx + 1] : vb[idx - 1] + 1;
                var y = x - k;
                if (x > n || y > m || y < 0)
                {
                    vb[idx] = Math.Min(x, n);
                    continue;
                }

                int x0 = x, y0 = y;
                while (x < n && y < m && c.A[aHi - 1 - x] == c.B[bHi - 1 - y]) { x++; y++; }
                vb[idx] = x;

                if (!odd && k >= delta - d && k <= delta + d && x + vf[offset + (delta - k)] >= n)
                {
                    return (n - x, m - y, n - x0, m - y0);
                }
            }
        }

        // Cost cap reached: split at the furthest point reached so far (an approximation, never wrong, just not minimal).
        return (bestX, bestY, bestX, bestY);
    }

    // ───────────────────────────── Patience ─────────────────────────────

    private static void Patience(Context c, int aLo, int aHi, int bLo, int bHi)
    {
        while (aLo < aHi && bLo < bHi && c.A[aLo] == c.B[bLo]) { aLo++; bLo++; }
        while (aLo < aHi && bLo < bHi && c.A[aHi - 1] == c.B[bHi - 1]) { aHi--; bHi--; }

        if (aLo == aHi || bLo == bHi)
        {
            if (aLo != aHi || bLo != bHi)
            {
                c.Blocks.Add(new DiffBlock(aLo, aHi - aLo, bLo, bHi - bLo));
            }

            return;
        }

        var anchors = UniqueCommonAnchors(c, aLo, aHi, bLo, bHi);
        if (anchors.Count == 0)
        {
            Myers(c, aLo, aHi, bLo, bHi); // nothing unique to hold on to: exact algorithm for this stretch
            return;
        }

        int pa = aLo, pb = bLo;
        foreach (var (ai, bi) in anchors)
        {
            Patience(c, pa, ai, pb, bi);
            pa = ai + 1;
            pb = bi + 1;
        }

        Patience(c, pa, aHi, pb, bHi);
    }

    /// <summary>Lines occurring exactly once in both ranges, reduced to the longest increasing subsequence by position.</summary>
    private static List<(int A, int B)> UniqueCommonAnchors(Context c, int aLo, int aHi, int bLo, int bHi)
    {
        var countA = new Dictionary<int, (int Count, int Index)>();
        for (var i = aLo; i < aHi; i++)
        {
            countA[c.A[i]] = countA.TryGetValue(c.A[i], out var e) ? (e.Count + 1, e.Index) : (1, i);
        }

        var countB = new Dictionary<int, (int Count, int Index)>();
        for (var j = bLo; j < bHi; j++)
        {
            countB[c.B[j]] = countB.TryGetValue(c.B[j], out var e) ? (e.Count + 1, e.Index) : (1, j);
        }

        // candidate pairs in order of A
        var pairs = new List<(int A, int B)>();
        for (var i = aLo; i < aHi; i++)
        {
            if (countA[c.A[i]].Count == 1 && countB.TryGetValue(c.A[i], out var eb) && eb.Count == 1)
            {
                pairs.Add((i, eb.Index));
            }
        }

        if (pairs.Count == 0)
        {
            return pairs;
        }

        // longest increasing subsequence on the B positions (patience sorting with back pointers)
        var tails = new List<int>();        // index into pairs of the smallest tail for each length
        var prev = new int[pairs.Count];
        for (var p = 0; p < pairs.Count; p++)
        {
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (pairs[tails[mid]].B < pairs[p].B) { lo = mid + 1; } else { hi = mid; }
            }

            prev[p] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count) { tails.Add(p); } else { tails[lo] = p; }
        }

        var result = new List<(int A, int B)>(tails.Count);
        for (var p = tails[^1]; p >= 0; p = prev[p])
        {
            result.Add(pairs[p]);
        }

        result.Reverse();
        return result;
    }

    // ───────────────────────────── Histogram ─────────────────────────────

    /// <summary>A line occurring more often than this in the old text is too common to anchor on.</summary>
    private const int MaxChain = 64;

    private static void Histogram(Context c, int aLo, int aHi, int bLo, int bHi)
    {
        while (aLo < aHi && bLo < bHi && c.A[aLo] == c.B[bLo]) { aLo++; bLo++; }
        while (aLo < aHi && bLo < bHi && c.A[aHi - 1] == c.B[bHi - 1]) { aHi--; bHi--; }

        if (aLo == aHi || bLo == bHi)
        {
            if (aLo != aHi || bLo != bHi)
            {
                c.Blocks.Add(new DiffBlock(aLo, aHi - aLo, bLo, bHi - bLo));
            }

            return;
        }

        // positions of every line in the old range
        var positions = new Dictionary<int, List<int>>();
        for (var i = aLo; i < aHi; i++)
        {
            if (!positions.TryGetValue(c.A[i], out var list))
            {
                positions[c.A[i]] = list = [];
            }

            list.Add(i);
        }

        int bestA = -1, bestB = -1, bestLen = 0;
        var lowest = MaxChain + 1; // rarity of the best match so far (fewer occurrences = better anchor)
        var anyCommon = false;

        for (var j = bLo; j < bHi; j++)
        {
            if (!positions.TryGetValue(c.B[j], out var occ))
            {
                continue;
            }

            anyCommon = true;
            if (occ.Count > lowest)
            {
                continue;
            }

            var reach = j + 1; // furthest B position covered by any match grown from this line
            foreach (var i in occ)
            {
                // grow the match both ways, tracking the rarest line inside it
                int sa = i, sb = j, ea = i + 1, eb = j + 1;
                var rare = occ.Count;
                while (sa > aLo && sb > bLo && c.A[sa - 1] == c.B[sb - 1])
                {
                    sa--; sb--;
                    rare = Math.Min(rare, positions[c.A[sa]].Count);
                }

                while (ea < aHi && eb < bHi && c.A[ea] == c.B[eb])
                {
                    rare = Math.Min(rare, positions[c.A[ea]].Count);
                    ea++; eb++;
                }

                reach = Math.Max(reach, eb);
                var len = ea - sa;
                if (len > bestLen || rare < lowest)
                {
                    if (rare < lowest || len > bestLen)
                    {
                        bestA = sa; bestB = sb; bestLen = len; lowest = Math.Min(lowest, rare);
                    }
                }
            }

            // Lines inside the run just examined would only rediscover the same match: skip to its end.
            j = reach - 1;
        }

        if (!anyCommon)
        {
            c.Blocks.Add(new DiffBlock(aLo, aHi - aLo, bLo, bHi - bLo));
            return;
        }

        if (bestLen == 0 || lowest > MaxChain)
        {
            Myers(c, aLo, aHi, bLo, bHi); // everything in common is too frequent to be a useful anchor
            return;
        }

        Histogram(c, aLo, bestA, bLo, bestB);
        Histogram(c, bestA + bestLen, aHi, bestB + bestLen, bHi);
    }
}
