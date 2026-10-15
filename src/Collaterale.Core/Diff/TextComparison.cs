using Collaterale.Core.Languages;

namespace Collaterale.Core.Diff;

public enum RowKind
{
    /// <summary>Identical on both sides.</summary>
    Same,

    /// <summary>Equal under the active ignore options, but the raw text differs (e.g. only whitespace). Not counted as a difference.</summary>
    Unimportant,

    /// <summary>A line present on both sides whose content differs.</summary>
    Changed,

    LeftOnly,
    RightOnly,

    /// <summary>Used by merge views for regions both sides changed differently. Never produced by a plain comparison.</summary>
    Conflict,
}

/// <summary>One visual row of the side-by-side view. <see cref="Left"/>/<see cref="Right"/> are line indexes, or -1 when that side is blank.</summary>
public readonly record struct DiffRow(RowKind Kind, int Left, int Right, int Group, int Move = -1)
{
    /// <summary>True when this one-sided row belongs to a block that was moved elsewhere in the file.</summary>
    public bool IsMoved => Move >= 0;

    public bool IsDifference => Kind is RowKind.Changed or RowKind.LeftOnly or RowKind.RightOnly;
}

/// <summary>A maximal run of differing rows (one "difference" for navigation and copy actions).</summary>
public sealed record DiffGroup(int Index, int FirstRow, int RowCount, int LeftStart, int LeftCount, int RightStart, int RightCount);

/// <summary>The complete result of comparing two texts line by line, ready for display.</summary>
public sealed class TextComparison
{
    private readonly CodeLine[]? _codeLeft;
    private readonly CodeLine[]? _codeRight;

    private TextComparison(IReadOnlyList<string> left, IReadOnlyList<string> right, DiffOptions options, List<DiffRow> rows, List<DiffGroup> groups, IReadOnlyList<MovedBlock> moves, CodeLine[]? codeLeft, CodeLine[]? codeRight)
    {
        _codeLeft = codeLeft;
        _codeRight = codeRight;
        Moves = moves;
        LeftLines = left;
        RightLines = right;
        Options = options;
        Rows = rows;
        Groups = groups;
    }

    public IReadOnlyList<string> LeftLines { get; }
    public IReadOnlyList<string> RightLines { get; }
    public DiffOptions Options { get; }
    public IReadOnlyList<DiffRow> Rows { get; }
    public IReadOnlyList<DiffGroup> Groups { get; }

    /// <summary>Blocks detected as moved (also flagged on the rows themselves).</summary>
    public IReadOnlyList<MovedBlock> Moves { get; }

    public int DifferenceCount => Groups.Count;
    public bool AreEqual => Groups.Count == 0;

    /// <summary>Word-level highlight ranges for a changed row (empty for other kinds).</summary>
    public (List<Span> Left, List<Span> Right) IntraLine(int row)
    {
        var r = Rows[row];
        if (r.Kind != RowKind.Changed)
        {
            return ([], []);
        }

        if (_codeLeft is null || _codeRight is null)
        {
            return IntraLineDiff.Compare(LeftLines[r.Left], RightLines[r.Right], Options);
        }

        // comments are ignored: highlight the code only, then translate the ranges back onto the full line
        var (cl, cr) = (_codeLeft[r.Left], _codeRight[r.Right]);
        var (l, rr) = IntraLineDiff.Compare(cl.Text, cr.Text, Options with { Comments = null });
        return ([.. l.Select(sp => { var (s, n) = cl.ToOriginal(sp.Start, sp.Length); return new Span(s, n); })],
                [.. rr.Select(sp => { var (s, n) = cr.ToOriginal(sp.Start, sp.Length); return new Span(s, n); })]);
    }

    public static TextComparison Compare(IReadOnlyList<string> left, IReadOnlyList<string> right, DiffOptions? options = null)
    {
        options ??= DiffOptions.Default;
        var requested = options;
        CodeLine[]? codeL = null, codeR = null;
        var cmpLeft = left;
        var cmpRight = right;
        if (options.Comments is { } syntax)
        {
            // compare the code with its comments removed; the rows still point at the original lines
            codeL = CommentStripper.Strip(left, syntax);
            codeR = CommentStripper.Strip(right, syntax);
            cmpLeft = [.. codeL.Select(c => c.Text)];
            cmpRight = [.. codeR.Select(c => c.Text)];
            options = options with { Comments = null };
        }

        // Lines that cannot matter (comment-only, blank when asked) take no part in the matching at all: if they did, a blank line
        // could be matched with a different blank line and drag real lines into a bogus change. They are put back afterwards.
        bool[]? skipL = null, skipR = null;
        if (codeL is not null || options.IgnoreBlankLines)
        {
            skipL = SkippedLines(left, codeL, options.IgnoreBlankLines);
            skipR = SkippedLines(right, codeR, options.IgnoreBlankLines);
        }

        var keys = new LineKeys(options);
        List<DiffRow> rows;
        List<DiffGroup> groups;
        if (skipL is null || skipR is null)
        {
            (rows, groups) = BuildRows(left, right, cmpLeft, cmpRight, options, keys, exactRaw: options.IsExact && codeL is null);
        }
        else
        {
            (rows, groups) = BuildRowsFiltered(left, right, cmpLeft, cmpRight, skipL, skipR, options, keys);
        }

        var moves = options.DetectMoves ? MoveDetector.Detect(rows, cmpLeft, cmpRight, keys) : [];
        return new TextComparison(left, right, requested, rows, groups, moves, codeL, codeR);
    }

    private static bool[] SkippedLines(IReadOnlyList<string> lines, CodeLine[]? code, bool blank)
    {
        var skip = new bool[lines.Count];
        for (var i = 0; i < skip.Length; i++)
        {
            skip[i] = (code is not null && code[i].Ignorable) || (blank && string.IsNullOrWhiteSpace(lines[i]));
        }

        return skip;
    }

    private static (List<DiffRow>, List<DiffGroup>) BuildRows(IReadOnlyList<string> left, IReadOnlyList<string> right, IReadOnlyList<string> cmpLeft, IReadOnlyList<string> cmpRight, DiffOptions options, LineKeys keys, bool exactRaw)
    {
        var (a, b) = keys.Ids(cmpLeft, cmpRight);
        var blocks = SequenceDiff.Diff(a, b, options.Algorithm);

        var rows = new List<DiffRow>(Math.Max(left.Count, right.Count) + 16);
        var groups = new List<DiffGroup>(blocks.Count);
        int pa = 0, pb = 0;

        void AddEqual(int count)
        {
            for (var k = 0; k < count; k++)
            {
                var kind = exactRaw || left[pa] == right[pb] ? RowKind.Same : RowKind.Unimportant;
                rows.Add(new DiffRow(kind, pa++, pb++, -1));
            }
        }

        foreach (var blk in blocks)
        {
            AddEqual(blk.AStart - pa);
            var first = rows.Count;
            var group = groups.Count;
            foreach (var (l, r) in RowAligner.Align(cmpLeft, blk.AStart, blk.ALength, cmpRight, blk.BStart, blk.BLength, options))
            {
                var kind = l >= 0 && r >= 0 ? RowKind.Changed : l >= 0 ? RowKind.LeftOnly : RowKind.RightOnly;
                rows.Add(new DiffRow(kind, l, r, group));
            }

            groups.Add(new DiffGroup(group, first, rows.Count - first, blk.AStart, blk.ALength, blk.BStart, blk.BLength));
            pa = blk.AEnd;
            pb = blk.BEnd;
        }

        AddEqual(a.Length - pa);
        return (rows, groups);
    }

    private static (List<DiffRow>, List<DiffGroup>) BuildRowsFiltered(IReadOnlyList<string> left, IReadOnlyList<string> right, IReadOnlyList<string> cmpLeft, IReadOnlyList<string> cmpRight, bool[] skipL, bool[] skipR, DiffOptions options, LineKeys keys)
    {
        var keepL = Enumerable.Range(0, left.Count).Where(i => !skipL[i]).ToArray();
        var keepR = Enumerable.Range(0, right.Count).Where(i => !skipR[i]).ToArray();
        var fl = new string[keepL.Length];
        var fr = new string[keepR.Length];
        for (var k = 0; k < fl.Length; k++) { fl[k] = cmpLeft[keepL[k]]; }
        for (var k = 0; k < fr.Length; k++) { fr[k] = cmpRight[keepR[k]]; }

        var (a, b) = keys.Ids(fl, fr);
        var blocks = SequenceDiff.Diff(a, b, options.Algorithm);

        var rows = new List<DiffRow>(Math.Max(left.Count, right.Count) + 16);
        var groups = new List<DiffGroup>(blocks.Count);
        int pa = 0, pb = 0;                       // next original line not yet shown, per side
        var groupFirst = -1;
        int groupPa = 0, groupPb = 0;

        // lines before (toL, toR) that were left out of the matching: shown as unimportant rows, paired by position
        void FlushSkipped(int toL, int toR)
        {
            int nl = toL - pa, nr = toR - pb, both = Math.Min(nl, nr);
            for (var k = 0; k < both; k++) { rows.Add(new DiffRow(RowKind.Unimportant, pa + k, pb + k, -1)); }
            for (var k = both; k < nl; k++) { rows.Add(new DiffRow(RowKind.Unimportant, pa + k, -1, -1)); }
            for (var k = both; k < nr; k++) { rows.Add(new DiffRow(RowKind.Unimportant, -1, pb + k, -1)); }
            pa = Math.Max(pa, toL);
            pb = Math.Max(pb, toR);
        }

        void Emit(RowKind kind, int l, int r, int group)
        {
            FlushSkipped(l >= 0 ? l : pa, r >= 0 ? r : pb);
            if (group >= 0 && groupFirst < 0)
            {
                groupFirst = rows.Count;
                groupPa = pa;
                groupPb = pb;
            }

            rows.Add(new DiffRow(kind, l, r, group));
            if (l >= 0) { pa = l + 1; }
            if (r >= 0) { pb = r + 1; }
        }

        int fa = 0, fb = 0;
        void AddEqual(int count)
        {
            for (var k = 0; k < count; k++, fa++, fb++)
            {
                var (l, r) = (keepL[fa], keepR[fb]);
                Emit(left[l] == right[r] ? RowKind.Same : RowKind.Unimportant, l, r, -1);
            }
        }

        foreach (var blk in blocks)
        {
            AddEqual(blk.AStart - fa);
            groupFirst = -1;
            var group = groups.Count;
            foreach (var (l, r) in RowAligner.Align(fl, blk.AStart, blk.ALength, fr, blk.BStart, blk.BLength, options))
            {
                var kind = l >= 0 && r >= 0 ? RowKind.Changed : l >= 0 ? RowKind.LeftOnly : RowKind.RightOnly;
                Emit(kind, l >= 0 ? keepL[l] : -1, r >= 0 ? keepR[r] : -1, group);
            }

            groups.Add(new DiffGroup(group, groupFirst, rows.Count - groupFirst, groupPa, pa - groupPa, groupPb, pb - groupPb));
            fa = blk.AEnd;
            fb = blk.BEnd;
        }

        AddEqual(a.Length - fa);
        FlushSkipped(left.Count, right.Count);
        return (rows, groups);
    }
}
