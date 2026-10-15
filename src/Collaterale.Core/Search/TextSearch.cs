using System.Text.RegularExpressions;

namespace Collaterale.Core.Search;

/// <param name="Regex">Interpret the pattern as a regular expression.</param>
/// <param name="MatchCase">Only match text with the same case.</param>
/// <param name="WholeWords">The match must not touch word characters on either side.</param>
/// <param name="Wrap">Continue at the other end of the text when one end is reached.</param>
public sealed record SearchOptions(bool Regex = false, bool MatchCase = false, bool WholeWords = false, bool Wrap = true);

/// <summary>A match: zero-based line and column, and its length in characters.</summary>
public readonly record struct SearchMatch(int Line, int Start, int Length);

/// <summary>Searching and replacing in a list of lines (one search at a time; matches never span lines).</summary>
public static class TextSearch
{
    /// <summary>An explanation when the pattern is not a valid regular expression, otherwise null.</summary>
    public static string? Validate(string pattern, SearchOptions options)
    {
        if (!options.Regex) { return null; }
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    private static Regex? Build(string pattern, SearchOptions o)
    {
        if (string.IsNullOrEmpty(pattern)) { return null; }
        var body = o.Regex ? pattern : Regex.Escape(pattern);
        if (o.WholeWords) { body = $@"(?<!\w)(?:{body})(?!\w)"; }
        try
        {
            return new Regex(body, RegexOptions.CultureInvariant | (o.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase), TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IEnumerable<SearchMatch> InLine(Regex re, string text, int line)
    {
        foreach (Match m in re.Matches(text))
        {
            if (m.Length > 0) { yield return new SearchMatch(line, m.Index, m.Length); }
        }
    }

    public static IReadOnlyList<SearchMatch> All(IReadOnlyList<string> lines, string pattern, SearchOptions options)
    {
        if (Build(pattern, options) is not { } re) { return []; }
        var result = new List<SearchMatch>();
        for (var i = 0; i < lines.Count; i++) { result.AddRange(InLine(re, lines[i], i)); }
        return result;
    }

    /// <summary>The first match that starts at or after the given position.</summary>
    public static SearchMatch? Next(IReadOnlyList<string> lines, string pattern, SearchOptions options, int line, int column)
    {
        if (Build(pattern, options) is not { } re || lines.Count == 0) { return null; }
        for (var step = 0; step <= lines.Count; step++)
        {
            var i = line + step;
            if (i >= lines.Count)
            {
                if (!options.Wrap) { return null; }
                i -= lines.Count;
            }

            if (step == lines.Count && i != line) { break; }
            foreach (var m in InLine(re, lines[i], i))
            {
                var first = step == 0;
                var wrappedBack = step == lines.Count;      // the starting line again: only the part before the start
                if (first && m.Start < column) { continue; }
                if (wrappedBack && m.Start >= column) { continue; }
                return m;
            }
        }

        return null;
    }

    /// <summary>The last match that starts before the given position.</summary>
    public static SearchMatch? Previous(IReadOnlyList<string> lines, string pattern, SearchOptions options, int line, int column)
    {
        if (Build(pattern, options) is not { } re || lines.Count == 0) { return null; }
        for (var step = 0; step <= lines.Count; step++)
        {
            var i = line - step;
            if (i < 0)
            {
                if (!options.Wrap) { return null; }
                i += lines.Count;
            }

            var matches = InLine(re, lines[i], i).ToList();
            for (var k = matches.Count - 1; k >= 0; k--)
            {
                var m = matches[k];
                if (step == 0 && m.Start >= column) { continue; }
                if (step == lines.Count && m.Start < column) { continue; }
                return m;
            }
        }

        return null;
    }

    /// <summary>Indexes of the lines that contain at least one match (the "select all" button).</summary>
    public static IReadOnlyList<int> LinesWithMatches(IReadOnlyList<string> lines, string pattern, SearchOptions options)
        => [.. All(lines, pattern, options).Select(m => m.Line).Distinct()];

    /// <summary>The line with one match replaced; in regex mode <c>$1</c> style group references work.</summary>
    public static string ReplaceIn(string text, SearchMatch match, string pattern, string replacement, SearchOptions options)
    {
        var piece = text.Substring(match.Start, match.Length);
        var with = options.Regex && Build(pattern, options) is { } re ? re.Replace(piece, replacement, 1) : replacement;
        // a regex that needs context (anchors, lookarounds) may not match the isolated piece: fall back to the text as is
        if (options.Regex && !Build(pattern, options)!.IsMatch(piece)) { with = replacement; }
        return string.Concat(text.AsSpan(0, match.Start), with, text.AsSpan(match.Start + match.Length));
    }

    public static (List<string> Lines, int Count) ReplaceAll(IReadOnlyList<string> lines, string pattern, string replacement, SearchOptions options)
    {
        var result = new List<string>(lines.Count);
        var count = 0;
        if (Build(pattern, options) is not { } re) { return ([.. lines], 0); }
        foreach (var line in lines)
        {
            var n = 0;
            var text = re.Replace(line, m => { if (m.Length == 0) { return m.Value; } n++; return options.Regex ? m.Result(replacement) : replacement; });
            count += n;
            result.Add(text);
        }

        return (result, count);
    }
}
