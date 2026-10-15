using Collaterale.Core.Diff;

namespace Collaterale.Core.Merge;

public enum MergeRegionKind
{
    /// <summary>Unchanged on both sides.</summary>
    Stable,

    /// <summary>Only the left side changed this part; take left.</summary>
    LeftChanged,

    /// <summary>Only the right side changed this part; take right.</summary>
    RightChanged,

    /// <summary>Both sides made the identical change; nothing to decide.</summary>
    BothSame,

    /// <summary>Both sides changed the same part differently: needs a decision.</summary>
    Conflict,
}

/// <summary>What to put in the result for a region.</summary>
public enum Resolution { Auto, Base, Left, Right, LeftThenRight, RightThenLeft }

/// <summary>A stretch of the merge, expressed as line ranges in each of the three versions.</summary>
public sealed record MergeRegion(MergeRegionKind Kind, int BaseStart, int BaseLength, int LeftStart, int LeftLength, int RightStart, int RightLength)
{
    public bool NeedsDecision => Kind == MergeRegionKind.Conflict;
}

/// <summary>diff3-style merge of two descendants (left, right) of a common ancestor (base).</summary>
public sealed class ThreeWayMerge
{
    private ThreeWayMerge(IReadOnlyList<string> baseLines, IReadOnlyList<string> left, IReadOnlyList<string> right, List<MergeRegion> regions)
    {
        Base = baseLines;
        Left = left;
        Right = right;
        Regions = regions;
    }

    public IReadOnlyList<string> Base { get; }
    public IReadOnlyList<string> Left { get; }
    public IReadOnlyList<string> Right { get; }
    public IReadOnlyList<MergeRegion> Regions { get; }

    public int ConflictCount => Regions.Count(r => r.Kind == MergeRegionKind.Conflict);
    public bool HasConflicts => ConflictCount > 0;

    public static ThreeWayMerge Merge(IReadOnlyList<string> baseLines, IReadOnlyList<string> left, IReadOnlyList<string> right, DiffOptions? options = null)
    {
        options ??= DiffOptions.Default;
        var keys = new LineKeys(options);
        var (bi, li) = keys.Ids(baseLines, left);
        var (_, ri) = keys.Ids(baseLines, right);
        // ids must be comparable across all three, so build them once from a shared dictionary
        var shared = new Dictionary<string, int>(StringComparer.Ordinal);
        int Id(string line) { var k = keys.Key(line); return shared.TryGetValue(k, out var v) ? v : shared[k] = shared.Count; }
        bi = [.. baseLines.Select(Id)];
        li = [.. left.Select(Id)];
        ri = [.. right.Select(Id)];

        var hl = SequenceDiff.Diff(bi, li, options.Algorithm);
        var hr = SequenceDiff.Diff(bi, ri, options.Algorithm);
        var regions = Combine(baseLines.Count, hl, hr, li, ri);
        return new ThreeWayMerge(baseLines, left, right, regions);
    }

    private static List<MergeRegion> Combine(int baseCount, List<DiffBlock> hl, List<DiffBlock> hr, int[] li, int[] ri)
    {
        var regions = new List<MergeRegion>();
        int il = 0, ir = 0;
        var basePos = 0;
        int offL = 0, offR = 0; // (version index) - (base index) for unchanged text before the current position

        while (il < hl.Count || ir < hr.Count)
        {
            // next cluster starts at the earliest remaining hunk
            var startL = il < hl.Count ? hl[il].AStart : int.MaxValue;
            var startR = ir < hr.Count ? hr[ir].AStart : int.MaxValue;
            var clusterStart = Math.Min(startL, startR);

            if (clusterStart > basePos)
            {
                regions.Add(new MergeRegion(MergeRegionKind.Stable, basePos, clusterStart - basePos,
                    basePos + offL, clusterStart - basePos, basePos + offR, clusterStart - basePos));
                basePos = clusterStart;
            }

            // grow the cluster with every hunk that touches or overlaps it, from either side
            var clusterEnd = clusterStart;
            int firstL = il, firstR = ir;
            var grew = true;
            var any = false;
            while (grew)
            {
                grew = false;
                if (il < hl.Count && Touches(hl[il], clusterStart, clusterEnd, any))
                {
                    clusterEnd = Math.Max(clusterEnd, hl[il].AEnd);
                    il++; grew = true; any = true;
                }

                if (ir < hr.Count && Touches(hr[ir], clusterStart, clusterEnd, any))
                {
                    clusterEnd = Math.Max(clusterEnd, hr[ir].AEnd);
                    ir++; grew = true; any = true;
                }
            }

            var baseLen = clusterEnd - clusterStart;
            var deltaL = hl.Skip(firstL).Take(il - firstL).Sum(h => h.BLength - h.ALength);
            var deltaR = hr.Skip(firstR).Take(ir - firstR).Sum(h => h.BLength - h.ALength);
            int leftStart = clusterStart + offL, rightStart = clusterStart + offR;
            int leftLen = baseLen + deltaL, rightLen = baseLen + deltaR;
            var leftChanged = il > firstL;
            var rightChanged = ir > firstR;

            MergeRegionKind kind;
            if (leftChanged && rightChanged)
            {
                kind = SameContent(li, leftStart, leftLen, ri, rightStart, rightLen) ? MergeRegionKind.BothSame : MergeRegionKind.Conflict;
            }
            else
            {
                kind = leftChanged ? MergeRegionKind.LeftChanged : MergeRegionKind.RightChanged;
            }

            regions.Add(new MergeRegion(kind, clusterStart, baseLen, leftStart, leftLen, rightStart, rightLen));
            offL += deltaL;
            offR += deltaR;
            basePos = clusterEnd;
        }

        if (basePos < baseCount)
        {
            regions.Add(new MergeRegion(MergeRegionKind.Stable, basePos, baseCount - basePos,
                basePos + offL, baseCount - basePos, basePos + offR, baseCount - basePos));
        }

        return regions;
    }

    /// <summary>
    /// A hunk joins the cluster when its base range overlaps it, or touches it (shared edge): two edits that meet at a line
    /// boundary cannot be ordered safely, so they are decided together. Insertions at the same point also join.
    /// </summary>
    private static bool Touches(DiffBlock h, int clusterStart, int clusterEnd, bool anyInCluster)
    {
        if (!anyInCluster)
        {
            return h.AStart == clusterStart;
        }

        return h.AStart <= clusterEnd;
    }

    private static bool SameContent(int[] a, int aStart, int aLen, int[] b, int bStart, int bLen)
    {
        if (aLen != bLen)
        {
            return false;
        }

        for (var i = 0; i < aLen; i++)
        {
            if (a[aStart + i] != b[bStart + i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Lines of one region as they would appear for the given resolution.</summary>
    public IEnumerable<string> RegionLines(MergeRegion r, Resolution resolution = Resolution.Auto)
    {
        IEnumerable<string> L() => Left.Skip(r.LeftStart).Take(r.LeftLength);
        IEnumerable<string> R() => Right.Skip(r.RightStart).Take(r.RightLength);
        IEnumerable<string> B() => Base.Skip(r.BaseStart).Take(r.BaseLength);

        return resolution switch
        {
            Resolution.Base => B(),
            Resolution.Left => L(),
            Resolution.Right => R(),
            Resolution.LeftThenRight => L().Concat(R()),
            Resolution.RightThenLeft => R().Concat(L()),
            _ => r.Kind switch
            {
                MergeRegionKind.RightChanged => R(),
                MergeRegionKind.Conflict => L(), // an undecided conflict defaults to left; callers normally supply a resolution
                _ => L(),
            },
        };
    }

    /// <summary>Builds the merged text. Regions without an explicit decision use the automatic result; conflicts become marker blocks when <paramref name="markers"/> is set.</summary>
    public List<string> Build(Func<int, MergeRegion, Resolution?>? decide = null, bool markers = true, string leftName = "left", string rightName = "right")
    {
        var output = new List<string>();
        for (var i = 0; i < Regions.Count; i++)
        {
            var r = Regions[i];
            var choice = decide?.Invoke(i, r);
            if (r.Kind == MergeRegionKind.Conflict && choice is null or Resolution.Auto && markers)
            {
                output.Add($"<<<<<<< {leftName}");
                output.AddRange(RegionLines(r, Resolution.Left));
                output.Add("=======");
                output.AddRange(RegionLines(r, Resolution.Right));
                output.Add($">>>>>>> {rightName}");
            }
            else
            {
                output.AddRange(RegionLines(r, choice ?? Resolution.Auto));
            }
        }

        return output;
    }
}
