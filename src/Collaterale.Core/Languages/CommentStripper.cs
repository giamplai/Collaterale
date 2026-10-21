using System.Text;

namespace Collaterale.Core.Languages;

/// <summary>One line with its comments taken out. <see cref="Map"/> relates positions in <see cref="Text"/> to positions in the original line (null when nothing was removed).</summary>
public readonly record struct CodeLine(string Text, bool Ignorable, int[]? Map)
{
    /// <summary>Converts a character range of <see cref="Text"/> back to a range of the original line.</summary>
    public (int Start, int Length) ToOriginal(int start, int length)
    {
        if (Map is null || length <= 0) { return (start, length); }
        var s = Map[start];
        var e = Map[start + length - 1] + 1;
        return (s, e - s);
    }
}

/// <summary>
/// Removes comments from source text while respecting string literals and multi-line comments or strings,
/// so that "only a comment changed" can be told apart from "the code changed". One output line per input line.
/// </summary>
public static class CommentStripper
{
    private enum Mode { Code, Block, MultiString }

    /// <summary>Takes out comments (default), the text inside string literals, or both. Comments are always <i>recognised</i>, so a quote inside one never starts a string.</summary>
    public static CodeLine[] Strip(IReadOnlyList<string> lines, CommentSyntax syntax, bool comments = true, bool stringContents = false)
    {
        var result = new CodeLine[lines.Count];
        var mode = Mode.Code;
        var blockClose = "";
        StringRule? multi = null;
        var removed = new List<(int Start, int End)>();

        // longest opener first, so that """ wins over " and $@" over @"
        var strings = syntax.Strings.OrderByDescending(s => s.Open.Length).ToArray();
        var blocks = syntax.Blocks.OrderByDescending(b => b.Open.Length).ToArray();
        var lineStarts = syntax.LineStarts.OrderByDescending(s => s.Length).ToArray();

        for (var n = 0; n < lines.Count; n++)
        {
            var line = lines[n];
            removed.Clear();
            var i = 0;

            while (i < line.Length)
            {
                if (mode == Mode.Block)
                {
                    var close = line.IndexOf(blockClose, i, StringComparison.Ordinal);
                    if (close < 0) { if (comments) { removed.Add((i, line.Length)); } i = line.Length; break; }
                    var end = close + blockClose.Length;
                    if (comments) { removed.Add((i, end)); }
                    i = end;
                    mode = Mode.Code;
                    continue;
                }

                if (mode == Mode.MultiString)
                {
                    var from = i;
                    i = ScanString(line, i, multi!, out var closed);
                    if (stringContents) { removed.Add((from, closed ? i - multi!.Close.Length : i)); }
                    if (closed) { mode = Mode.Code; }
                    continue;
                }

                var rule = strings.FirstOrDefault(s => string.CompareOrdinal(line, i, s.Open, 0, s.Open.Length) == 0);
                if (rule is not null)
                {
                    var contentStart = i + rule.Open.Length;
                    i = ScanString(line, contentStart, rule, out var closed);
                    if (stringContents) { removed.Add((contentStart, closed ? i - rule.Close.Length : i)); }
                    if (!closed && rule.Multiline) { mode = Mode.MultiString; multi = rule; }
                    continue;
                }

                var blk = blocks.FirstOrDefault(b => string.CompareOrdinal(line, i, b.Open, 0, b.Open.Length) == 0);
                if (blk.Open is not null)
                {
                    var close = line.IndexOf(blk.Close, i + blk.Open.Length, StringComparison.Ordinal);
                    if (close < 0) { if (comments) { removed.Add((i, line.Length)); } mode = Mode.Block; blockClose = blk.Close; i = line.Length; break; }
                    var end = close + blk.Close.Length;
                    if (comments) { removed.Add((i, end)); }
                    i = end;
                    continue;
                }

                if (StartsLineComment(line, i, lineStarts, syntax))
                {
                    if (comments) { removed.Add((i, line.Length)); }
                    break;
                }

                i++;
            }

            result[n] = removed.Count == 0 ? new CodeLine(line, false, null) : Build(line, removed);
        }

        return result;
    }

    private static bool StartsLineComment(string line, int i, string[] starts, CommentSyntax syntax)
    {
        foreach (var s in starts)
        {
            if (string.CompareOrdinal(line, i, s, 0, s.Length) != 0) { continue; }
            if (syntax.LineStartOnly && line.AsSpan(0, i).Trim().Length != 0) { continue; }
            if (syntax.LineStartNeedsSpaceBefore && i > 0 && !char.IsWhiteSpace(line[i - 1])) { continue; }
            return true;
        }

        return false;
    }

    private static CodeLine Build(string line, List<(int Start, int End)> removed)
    {
        var sb = new StringBuilder(line.Length);
        var map = new List<int>(line.Length + 1);
        var r = 0;
        for (var k = 0; k < line.Length; k++)
        {
            while (r < removed.Count && k >= removed[r].End) { r++; }
            if (r < removed.Count && k >= removed[r].Start) { continue; }
            sb.Append(line[k]);
            map.Add(k);
        }

        var text = sb.ToString();
        var trimmed = text.TrimEnd();
        map.RemoveRange(trimmed.Length, map.Count - trimmed.Length);
        map.Add(line.Length);                   // end position, so that a span reaching the end of the text maps cleanly
        var ignorable = line.AsSpan().Trim().Length > 0 && trimmed.AsSpan().Trim().Length == 0;
        return new CodeLine(trimmed, ignorable, [.. map]);
    }

    /// <summary>Scans from just after a string opener; returns the position after the closing quote (or the end of the line) and whether it closed.</summary>
    private static int ScanString(string line, int i, StringRule rule, out bool closed)
    {
        closed = false;
        while (i < line.Length)
        {
            if (rule.Escape != '\0' && line[i] == rule.Escape && !rule.DoubledCloseEscapes)
            {
                i += 2;
                continue;
            }

            if (string.CompareOrdinal(line, i, rule.Close, 0, rule.Close.Length) == 0)
            {
                if (rule.DoubledCloseEscapes && string.CompareOrdinal(line, i + rule.Close.Length, rule.Close, 0, rule.Close.Length) == 0)
                {
                    i += rule.Close.Length * 2;       // "" inside a verbatim string, '' inside an SQL string
                    continue;
                }

                closed = true;
                return i + rule.Close.Length;
            }

            i++;
        }

        return line.Length;
    }
}
