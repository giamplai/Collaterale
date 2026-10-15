namespace Collaterale.Core.Integration;

public sealed record PatchHunk(int OldStart, int OldCount, int NewStart, int NewCount, IReadOnlyList<(char Kind, string Text)> Lines);

/// <summary>One file of a patch.</summary>
public sealed record FilePatch(string OldName, string NewName, IReadOnlyList<PatchHunk> Hunks)
{
    public bool IsNew => OldName == "/dev/null";
    public bool IsDeleted => NewName == "/dev/null";

    /// <summary>The lines the patch shows before and after the change (only the lines the hunks mention, in order).</summary>
    public (List<string> Before, List<string> After) Sides()
    {
        List<string> before = [], after = [];
        foreach (var h in Hunks)
        {
            foreach (var (kind, text) in h.Lines)
            {
                if (kind != '+') { before.Add(text); }
                if (kind != '-') { after.Add(text); }
            }
        }

        return (before, after);
    }
}

/// <summary>Reads the unified diff format (what <c>diff -u</c>, <c>git diff</c> and our own <c>--diff</c> write).</summary>
public static class PatchReader
{
    public static IReadOnlyList<FilePatch> Parse(string text)
    {
        var files = new List<FilePatch>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        string? oldName = null;
        string? newName = null;
        List<PatchHunk>? hunks = null;
        PatchHunk? current = null;
        List<(char, string)>? body = null;
        int oldRemaining = 0, newRemaining = 0;

        void CloseHunk()
        {
            if (current is not null && body is not null) { hunks!.Add(current with { Lines = body }); }
            current = null;
            body = null;
        }

        void CloseFile()
        {
            CloseHunk();
            if (oldName is not null && newName is not null && hunks is { Count: > 0 }) { files.Add(new FilePatch(oldName, newName, hunks)); }
            (oldName, newName, hunks) = (null, null, null);
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (current is not null && (oldRemaining > 0 || newRemaining > 0 || line.StartsWith('\\')))
            {
                if (line.StartsWith('\\')) { continue; }              // "\ No newline at end of file" describes the previous line
                var kind = line.Length == 0 ? ' ' : line[0];
                var content = line.Length == 0 ? "" : line[1..];
                if (kind is ' ' or '-' or '+')
                {
                    body!.Add((kind, content));
                    if (kind != '+') { oldRemaining--; }
                    if (kind != '-') { newRemaining--; }
                    continue;
                }
            }

            if (line.StartsWith("--- ", StringComparison.Ordinal) && i + 1 < lines.Length && lines[i + 1].StartsWith("+++ ", StringComparison.Ordinal))
            {
                CloseFile();
                oldName = Name(line[4..]);
                newName = Name(lines[i + 1][4..]);
                hunks = [];
                i++;
                continue;
            }

            if (line.StartsWith("@@ ", StringComparison.Ordinal) && hunks is not null && TryHeader(line, out var hunk))
            {
                CloseHunk();
                current = hunk;
                body = [];
                (oldRemaining, newRemaining) = (hunk.OldCount, hunk.NewCount);
            }
        }

        CloseFile();
        return files;
    }

    private static string Name(string raw)
    {
        var name = raw.Split('\t')[0].Trim();
        if (name == "/dev/null") { return name; }
        return name.StartsWith("a/", StringComparison.Ordinal) || name.StartsWith("b/", StringComparison.Ordinal) ? name[2..] : name;
    }

    private static bool TryHeader(string line, out PatchHunk hunk)
    {
        hunk = new PatchHunk(0, 0, 0, 0, []);
        // @@ -a[,b] +c[,d] @@
        var m = System.Text.RegularExpressions.Regex.Match(line, @"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@");
        if (!m.Success) { return false; }
        int N(int g, int dflt) => m.Groups[g].Success ? int.Parse(m.Groups[g].Value) : dflt;
        hunk = new PatchHunk(N(1, 0), N(2, 1), N(3, 0), N(4, 1), []);
        return true;
    }
}
