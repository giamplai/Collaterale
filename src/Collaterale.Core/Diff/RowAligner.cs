namespace Collaterale.Core.Diff;

/// <summary>
/// Lays out one changed block as rows. Lines that look alike are paired (so an edited line sits opposite its old version
/// even when other lines were inserted around it); the rest are paired by position or shown on one side only.
/// </summary>
public static class RowAligner
{
    /// <summary>Lines below this word-similarity are not considered "the same line, edited".</summary>
    public const double PairThreshold = 0.45;

    /// <summary>Beyond this many candidate pairs the similarity search is skipped and lines are paired by position.</summary>
    private const int MaxSimilarityCells = 250_000;

    public static List<(int Left, int Right)> Align(IReadOnlyList<string> left, int ls, int ln, IReadOnlyList<string> right, int rs, int rn, DiffOptions options)
    {
        var rows = new List<(int, int)>(Math.Max(ln, rn));
        if (ln == 0 || rn == 0)
        {
            for (var i = 0; i < ln; i++) { rows.Add((ls + i, -1)); }
            for (var j = 0; j < rn; j++) { rows.Add((-1, rs + j)); }
            return rows;
        }

        var pairs = (long)ln * rn <= MaxSimilarityCells ? SimilarPairs(left, ls, ln, right, rs, rn, options) : [];

        int pi = 0, pj = 0;
        foreach (var (i, j) in pairs.Append((ln, rn)))
        {
            EmitGap(rows, ls, pi, i, rs, pj, j);
            if (i < ln)
            {
                rows.Add((ls + i, rs + j));
            }

            pi = i + 1;
            pj = j + 1;
        }

        return rows;
    }

    /// <summary>Unmatched lines between two similarity matches: pair them by position, then spill the remainder.</summary>
    private static void EmitGap(List<(int, int)> rows, int ls, int i0, int i1, int rs, int j0, int j1)
    {
        int l = i1 - i0, r = j1 - j0;
        var both = Math.Min(l, r);
        for (var k = 0; k < both; k++) { rows.Add((ls + i0 + k, rs + j0 + k)); }
        for (var k = both; k < l; k++) { rows.Add((ls + i0 + k, -1)); }
        for (var k = both; k < r; k++) { rows.Add((-1, rs + j0 + k)); }
    }

    /// <summary>Order-preserving matching that maximizes total similarity (weighted LCS), keeping only pairs above the threshold.</summary>
    private static List<(int I, int J)> SimilarPairs(IReadOnlyList<string> left, int ls, int ln, IReadOnlyList<string> right, int rs, int rn, DiffOptions options)
    {
        var lt = Enumerable.Range(0, ln).Select(i => WordCounts(left[ls + i], options)).ToArray();
        var rt = Enumerable.Range(0, rn).Select(j => WordCounts(right[rs + j], options)).ToArray();

        // Words that occur in most lines of the block ("step", "the", an indentation token…) say little about which lines belong
        // together; rare words say a lot. Weight every word by how few lines of the block contain it (inverse document frequency).
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (counts, _) in lt.Concat(rt))
        {
            foreach (var w in counts.Keys) { df[w] = df.GetValueOrDefault(w) + 1; }
        }

        var total = ln + rn;
        double Weight(string w) => Math.Log(1 + (double)total / df[w]);

        var dp = new double[ln + 1, rn + 1];
        var sim = new double[ln, rn];
        for (var i = 1; i <= ln; i++)
        {
            for (var j = 1; j <= rn; j++)
            {
                var s = Dice(lt[i - 1], rt[j - 1], Weight);
                sim[i - 1, j - 1] = s;
                var best = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                if (s >= PairThreshold)
                {
                    best = Math.Max(best, dp[i - 1, j - 1] + s);
                }

                dp[i, j] = best;
            }
        }

        var result = new List<(int, int)>();
        int x = ln, y = rn;
        while (x > 0 && y > 0)
        {
            if (sim[x - 1, y - 1] >= PairThreshold && Math.Abs(dp[x, y] - (dp[x - 1, y - 1] + sim[x - 1, y - 1])) < 1e-9)
            {
                result.Add((x - 1, y - 1));
                x--;
                y--;
            }
            else if (dp[x - 1, y] >= dp[x, y - 1])
            {
                x--;
            }
            else
            {
                y--;
            }
        }

        result.Reverse();
        return result;
    }

    private static (Dictionary<string, int> Counts, int Total) WordCounts(string line, DiffOptions options)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        foreach (var t in Tokenizer.Tokens(line))
        {
            var piece = line.Substring(t.Start, t.Length);
            if (char.IsWhiteSpace(piece[0])) { continue; }
            if (options.IgnoreCase) { piece = piece.ToUpperInvariant(); }
            d[piece] = d.GetValueOrDefault(piece) + 1;
            total++;
        }

        return (d, total);
    }

    /// <summary>Weighted Dice similarity: shared weight relative to the combined weight of both lines.</summary>
    private static double Dice((Dictionary<string, int> Counts, int Total) a, (Dictionary<string, int> Counts, int Total) b, Func<string, double> weight)
    {
        if (a.Total + b.Total == 0)
        {
            return 1; // both are blank/whitespace-only: treat as the same line
        }

        double wa = 0, wb = 0, shared = 0;
        foreach (var (k, v) in a.Counts) { wa += weight(k) * v; }
        foreach (var (k, v) in b.Counts) { wb += weight(k) * v; }

        var (small, big) = a.Counts.Count <= b.Counts.Count ? (a.Counts, b.Counts) : (b.Counts, a.Counts);
        foreach (var (k, v) in small)
        {
            if (big.TryGetValue(k, out var w)) { shared += weight(k) * Math.Min(v, w); }
        }

        return wa + wb <= 0 ? 1 : 2.0 * shared / (wa + wb);
    }
}
