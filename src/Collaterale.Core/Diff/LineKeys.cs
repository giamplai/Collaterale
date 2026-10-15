using System.Text;
using System.Text.RegularExpressions;

namespace Collaterale.Core.Diff;

/// <summary>Turns a line into the key used for equality, according to <see cref="DiffOptions"/>.</summary>
public sealed class LineKeys(DiffOptions options)
{
    private readonly Regex[] _patterns = options.CompiledPatterns();
    private readonly Regex[] _timestamps = options.CompiledTimestampPatterns();

    public string Key(string line)
    {
        if (options.IsExact)
        {
            return line;
        }

        foreach (var p in _patterns)
        {
            line = p.Replace(line, string.Empty);
        }

        // dates and times always contain a digit: most lines of source code and prose do not need any regex at all
        if (_timestamps.Length > 0 && line.AsSpan().ContainsAnyInRange('0', '9'))
        {
            foreach (var p in _timestamps)
            {
                line = p.Replace(line, string.Empty);
            }
        }

        line = options.Whitespace switch
        {
            WhitespaceMode.IgnoreLeadingTrailing => line.Trim(),
            WhitespaceMode.IgnoreAmount => CollapseWhitespace(line),
            WhitespaceMode.IgnoreAll => RemoveWhitespace(line),
            _ => line,
        };

        return options.IgnoreCase ? line.ToUpperInvariant() : line;
    }

    private static string CollapseWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        var pendingSpace = false;
        foreach (var ch in s.Trim())
        {
            if (char.IsWhiteSpace(ch)) { pendingSpace = true; continue; }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string RemoveWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (!char.IsWhiteSpace(ch)) { sb.Append(ch); }
        }

        return sb.ToString();
    }

    /// <summary>Maps lines of both sides to shared integer ids so they can go through <see cref="SequenceDiff"/>.</summary>
    public (int[] Left, int[] Right) Ids(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var byRaw = options.IsExact ? null : new Dictionary<string, int>(StringComparer.Ordinal);   // a line seen before keeps its id without normalizing it again
        int Id(string line)
        {
            if (byRaw is not null && byRaw.TryGetValue(line, out var known))
            {
                return known;
            }

            var key = Key(line);
            if (!ids.TryGetValue(key, out var id))
            {
                ids[key] = id = ids.Count;
            }

            byRaw?.Add(line, id);
            return id;
        }

        return ([.. left.Select(Id)], [.. right.Select(Id)]);
    }
}
