using System.Text;

namespace Collaterale.Core.Diff;

/// <summary>Writes a comparison as a standard unified diff (the format <c>git apply</c> and <c>patch</c> read).</summary>
public static class UnifiedDiff
{
    public static string Write(
        string leftName, string rightName,
        IReadOnlyList<string> left, IReadOnlyList<string> right,
        int context = 3, DiffOptions? options = null,
        bool leftEndsWithNewline = true, bool rightEndsWithNewline = true)
    {
        // A missing final newline is part of the last line's identity (as in git): "end" and "end\n" are different lines.
        // Marking it before comparing turns every such case into an ordinary change.
        const string NoEol = "\u0000no-eol";
        var leftCmp = leftEndsWithNewline || left.Count == 0 ? left : [.. left.Take(left.Count - 1), left[^1] + NoEol];
        var rightCmp = rightEndsWithNewline || right.Count == 0 ? right : [.. right.Take(right.Count - 1), right[^1] + NoEol];
        var cmp = TextComparison.Compare(leftCmp, rightCmp, (options ?? DiffOptions.Default) with { DetectMoves = false });
        var changes = cmp.Groups.Select(g => new Change(g.LeftStart, g.LeftCount, g.RightStart, g.RightCount)).ToList();
        if (changes.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder();
        sb.Append("--- ").Append(leftName).Append('\n');
        sb.Append("+++ ").Append(rightName).Append('\n');

        // merge changes whose context windows touch into hunks
        var i = 0;
        while (i < changes.Count)
        {
            var first = i;
            var last = i;
            while (last + 1 < changes.Count && changes[last + 1].LeftStart - (changes[last].LeftStart + changes[last].LeftCount) <= 2 * context)
            {
                last++;
            }

            WriteHunk(sb, changes, first, last, left, right, context, leftEndsWithNewline, rightEndsWithNewline);
            i = last + 1;
        }

        return sb.ToString();
    }

    /// <summary>One difference: left lines removed, right lines added (positions are zero-based line indexes).</summary>
    private sealed record Change(int LeftStart, int LeftCount, int RightStart, int RightCount);

    private static void WriteHunk(StringBuilder sb, List<Change> changes, int first, int last, IReadOnlyList<string> left, IReadOnlyList<string> right, int context, bool lNl, bool rNl)
    {
        var a = changes[first];
        var b = changes[last];
        var leftFrom = Math.Max(0, a.LeftStart - context);
        var leftTo = Math.Min(left.Count, b.LeftStart + b.LeftCount + context);          // exclusive
        var rightFrom = a.RightStart - (a.LeftStart - leftFrom);                          // equal runs have equal length on both sides

        var body = new StringBuilder();
        int lCount = 0, rCount = 0;
        var li = leftFrom;
        for (var c = first; c <= last; c++)
        {
            var ch = changes[c];
            for (; li < ch.LeftStart; li++)                      // unchanged context before this change
            {
                AppendLine(body, ' ', left[li], li == left.Count - 1 && !lNl);
                lCount++; rCount++;
            }

            for (var k = 0; k < ch.LeftCount; k++)
            {
                var idx = ch.LeftStart + k;
                AppendLine(body, '-', left[idx], idx == left.Count - 1 && !lNl);
                lCount++;
            }

            for (var k = 0; k < ch.RightCount; k++)
            {
                var idx = ch.RightStart + k;
                AppendLine(body, '+', right[idx], idx == right.Count - 1 && !rNl);
                rCount++;
            }

            li = ch.LeftStart + ch.LeftCount;
        }

        for (; li < leftTo; li++)                                // trailing context
        {
            AppendLine(body, ' ', left[li], li == left.Count - 1 && !lNl);
            lCount++; rCount++;
        }

        var lStart = lCount == 0 ? leftFrom : leftFrom + 1;
        var rStart = rCount == 0 ? Math.Max(rightFrom, 0) : Math.Max(rightFrom, 0) + 1;
        sb.Append($"@@ -{lStart},{lCount} +{rStart},{rCount} @@\n").Append(body);
    }

    private static void AppendLine(StringBuilder sb, char prefix, string text, bool noNewlineMarker)
    {
        sb.Append(prefix).Append(text).Append('\n');
        if (noNewlineMarker)
        {
            sb.Append("\\ No newline at end of file\n");
        }
    }
}
