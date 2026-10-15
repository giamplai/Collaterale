namespace Collaterale.Core.Diff;

/// <summary>A highlighted character range inside one line.</summary>
public readonly record struct Span(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>Word-level difference between two versions of a line: which character ranges changed on each side.</summary>
public static class IntraLineDiff
{
    public static (List<Span> Left, List<Span> Right) Compare(string left, string right, DiffOptions? options = null)
    {
        options ??= DiffOptions.Default;
        var lt = Tokenizer.Tokens(left);
        var rt = Tokenizer.Tokens(right);
        string TokenKey(string text, Tokenizer.Token t)
        {
            var piece = text.Substring(t.Start, t.Length);
            var isSpace = char.IsWhiteSpace(piece[0]);
            if (isSpace)
            {
                return options.Whitespace switch
                {
                    WhitespaceMode.Exact => piece,
                    _ => " ", // any whitespace token is one "space" unit when whitespace is not significant
                };
            }

            return options.IgnoreCase ? piece.ToUpperInvariant() : piece;
        }

        // Tokens that are insignificant (whitespace under IgnoreAll) are dropped from the comparison but keep their positions.
        bool Significant(string text, Tokenizer.Token t) => !(options.Whitespace == WhitespaceMode.IgnoreAll && char.IsWhiteSpace(text[t.Start]));

        var li = lt.Where(t => Significant(left, t)).ToList();
        var ri = rt.Where(t => Significant(right, t)).ToList();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int Id(string k) => ids.TryGetValue(k, out var v) ? v : ids[k] = ids.Count;
        var a = li.Select(t => Id(TokenKey(left, t))).ToArray();
        var b = ri.Select(t => Id(TokenKey(right, t))).ToArray();

        var blocks = SequenceDiff.Diff(a, b);
        var ls = new List<Span>();
        var rs = new List<Span>();
        foreach (var blk in blocks)
        {
            AddRange(ls, li, blk.AStart, blk.ALength);
            AddRange(rs, ri, blk.BStart, blk.BLength);
        }

        return (ls, rs);
    }

    private static void AddRange(List<Span> spans, List<Tokenizer.Token> tokens, int start, int count)
    {
        if (count == 0)
        {
            return;
        }

        var first = tokens[start];
        var last = tokens[start + count - 1];
        var span = new Span(first.Start, last.Start + last.Length - first.Start);
        // merge with the previous span when only an insignificant gap separates them
        if (spans.Count > 0 && span.Start <= spans[^1].End)
        {
            var prev = spans[^1];
            spans[^1] = new Span(prev.Start, Math.Max(prev.End, span.End) - prev.Start);
        }
        else
        {
            spans.Add(span);
        }
    }

    /// <summary>Token-multiset similarity in [0,1] (Dice coefficient); used to decide which modified lines belong together.</summary>
    public static double Similarity(string a, string b, DiffOptions? options = null)
    {
        options ??= DiffOptions.Default;
        if (a.Length == 0 && b.Length == 0)
        {
            return 1;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        foreach (var t in Tokenizer.Tokens(a))
        {
            var piece = a.Substring(t.Start, t.Length);
            if (char.IsWhiteSpace(piece[0])) { continue; }
            piece = options.IgnoreCase ? piece.ToUpperInvariant() : piece;
            counts[piece] = counts.GetValueOrDefault(piece) + 1;
            total++;
        }

        var common = 0;
        var other = 0;
        foreach (var t in Tokenizer.Tokens(b))
        {
            var piece = b.Substring(t.Start, t.Length);
            if (char.IsWhiteSpace(piece[0])) { continue; }
            piece = options.IgnoreCase ? piece.ToUpperInvariant() : piece;
            other++;
            if (counts.TryGetValue(piece, out var c) && c > 0)
            {
                counts[piece] = c - 1;
                common++;
            }
        }

        return total + other == 0 ? 1 : 2.0 * common / (total + other);
    }
}
