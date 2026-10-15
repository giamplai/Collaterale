namespace Collaterale.Core.Hex;

/// <summary>A run of bytes that differ (or exist on one side only) starting at <see cref="Offset"/>.</summary>
public readonly record struct HexRange(long Offset, long Length)
{
    public long End => Offset + Length;

    public bool Contains(long offset) => offset >= Offset && offset < End;
}

/// <summary>
/// Byte-for-byte comparison of two binary buffers by position. Differences are runs of consecutive offsets where the
/// bytes differ, or where only one file has data (the longer file's tail).
/// </summary>
public sealed class HexComparison
{
    /// <summary>Stop recording differences after this many runs (a random-vs-random comparison would otherwise produce millions).</summary>
    public const int MaxRanges = 200_000;

    private readonly List<HexRange> _ranges;

    private HexComparison(byte[] left, byte[] right, List<HexRange> ranges, bool truncated)
    {
        Left = left;
        Right = right;
        _ranges = ranges;
        Truncated = truncated;
    }

    public byte[] Left { get; }
    public byte[] Right { get; }
    public IReadOnlyList<HexRange> Ranges => _ranges;

    /// <summary>True when more than <see cref="MaxRanges"/> runs exist; later differences are not listed.</summary>
    public bool Truncated { get; }

    public long Length => Math.Max(Left.Length, Right.Length);
    public bool AreEqual => _ranges.Count == 0;

    /// <summary>Total number of differing bytes among the recorded runs.</summary>
    public long DifferingBytes => _ranges.Sum(r => r.Length);

    public static HexComparison Compare(byte[] left, byte[] right, CancellationToken ct = default)
    {
        var ranges = new List<HexRange>();
        var common = Math.Min(left.Length, right.Length);
        long pos = 0;
        var truncated = false;

        while (pos < common)
        {
            ct.ThrowIfCancellationRequested();

            // skip equal stretches with the vectorized helper
            var equal = left.AsSpan((int)pos, (int)Math.Min(common - pos, int.MaxValue)).CommonPrefixLength(right.AsSpan((int)pos, (int)Math.Min(common - pos, int.MaxValue)));
            pos += equal;
            if (pos >= common)
            {
                break;
            }

            var start = pos;
            while (pos < common && left[pos] != right[pos])
            {
                pos++;
            }

            if (ranges.Count >= MaxRanges)
            {
                truncated = true;
                break;
            }

            ranges.Add(new HexRange(start, pos - start));
        }

        if (!truncated && left.Length != right.Length)
        {
            // the tail that only one side has is one more difference; merge it with a run that touches the boundary
            var tail = new HexRange(common, Math.Abs((long)left.Length - right.Length));
            if (ranges.Count > 0 && ranges[^1].End == common)
            {
                ranges[^1] = new HexRange(ranges[^1].Offset, ranges[^1].Length + tail.Length);
            }
            else
            {
                ranges.Add(tail);
            }
        }

        return new HexComparison(left, right, ranges, truncated);
    }

    /// <summary>Index of the run containing <paramref name="offset"/>, or -1.</summary>
    public int RangeAt(long offset)
    {
        int lo = 0, hi = _ranges.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var r = _ranges[mid];
            if (r.Contains(offset)) { return mid; }
            if (offset < r.Offset) { hi = mid - 1; } else { lo = mid + 1; }
        }

        return -1;
    }

    /// <summary>Index of the first run that starts at or after <paramref name="offset"/>, or -1 when none.</summary>
    public int NextRange(long offset)
    {
        int lo = 0, hi = _ranges.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_ranges[mid].Offset < offset) { lo = mid + 1; } else { hi = mid; }
        }

        return lo < _ranges.Count ? lo : -1;
    }

    public bool IsDifferent(long offset) => RangeAt(offset) >= 0;
}
