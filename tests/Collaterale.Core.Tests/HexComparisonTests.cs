using System.Diagnostics;
using Collaterale.Core.Hex;

namespace Collaterale.Core.Tests;

public class HexComparisonTests
{
    [Fact]
    public void Identical_buffers_have_no_ranges()
    {
        var c = HexComparison.Compare([1, 2, 3], [1, 2, 3]);
        Assert.True(c.AreEqual);
        Assert.Empty(c.Ranges);
    }

    [Fact]
    public void Runs_of_differing_bytes_are_grouped()
    {
        var c = HexComparison.Compare([1, 2, 3, 4, 5, 6, 7], [1, 9, 9, 4, 5, 8, 7]);
        Assert.Equal([new HexRange(1, 2), new HexRange(5, 1)], c.Ranges);
        Assert.Equal(3, c.DifferingBytes);
    }

    [Fact]
    public void The_extra_tail_of_the_longer_file_is_a_difference()
    {
        var c = HexComparison.Compare([1, 2, 3], [1, 2, 3, 4, 5]);
        Assert.Equal([new HexRange(3, 2)], c.Ranges);
        Assert.Equal(5, c.Length);
        var rev = HexComparison.Compare([1, 2, 3, 4, 5], [1, 2, 3]);
        Assert.Equal([new HexRange(3, 2)], rev.Ranges);
    }

    [Fact]
    public void A_difference_touching_the_end_merges_with_the_tail()
    {
        var c = HexComparison.Compare([1, 2, 3], [1, 9, 9, 7, 7]);
        Assert.Equal([new HexRange(1, 4)], c.Ranges);     // bytes 1..2 differ, 3..4 exist only on the right
    }

    [Fact]
    public void Empty_against_data()
    {
        Assert.Equal([new HexRange(0, 3)], HexComparison.Compare([], [1, 2, 3]).Ranges);
        Assert.True(HexComparison.Compare([], []).AreEqual);
    }

    [Fact]
    public void Lookups_find_ranges_by_offset()
    {
        var c = HexComparison.Compare(new byte[100], Enumerable.Range(0, 100).Select(i => i is 10 or 11 or 50 or 90 ? (byte)1 : (byte)0).ToArray());
        Assert.Equal(3, c.Ranges.Count);
        Assert.Equal(0, c.RangeAt(11));
        Assert.Equal(-1, c.RangeAt(12));
        Assert.Equal(1, c.NextRange(12));
        Assert.Equal(2, c.NextRange(51));
        Assert.Equal(-1, c.NextRange(91));
        Assert.True(c.IsDifferent(90));
        Assert.False(c.IsDifferent(89));
    }

    [Fact]
    public void Matches_a_naive_reference_on_random_buffers()
    {
        var rnd = new Random(31);
        for (var n = 0; n < 400; n++)
        {
            var a = new byte[rnd.Next(0, 300)];
            var b = new byte[rnd.Next(0, 300)];
            rnd.NextBytes(a);
            Array.Copy(a, b, Math.Min(a.Length, b.Length));
            for (var k = 0; k < rnd.Next(0, 8) && b.Length > 0; k++) { b[rnd.Next(b.Length)] = (byte)rnd.Next(256); }

            var c = HexComparison.Compare(a, b);
            for (long i = 0; i < c.Length; i++)
            {
                var expected = i >= a.Length || i >= b.Length || a[i] != b[i];
                Assert.Equal(expected, c.IsDifferent(i));
            }
        }
    }

    [Fact]
    public void Large_mostly_equal_buffers_are_fast()
    {
        var a = new byte[100 * 1024 * 1024];
        var b = a.ToArray();
        b[1_000] = 1; b[50_000_000] = 1; b[a.Length - 1] = 1;
        var sw = Stopwatch.StartNew();
        var c = HexComparison.Compare(a, b);
        sw.Stop();
        Assert.Equal(3, c.Ranges.Count);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Completely_different_buffers_are_capped_not_exploded()
    {
        var a = new byte[1_000_000];
        var b = Enumerable.Range(0, a.Length).Select(i => (byte)(i % 2 == 0 ? 1 : 0)).ToArray();   // every other byte differs
        var c = HexComparison.Compare(a, b);
        Assert.True(c.Truncated);
        Assert.Equal(HexComparison.MaxRanges, c.Ranges.Count);
    }

    [Fact]
    public void Cancellation_is_honoured()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => HexComparison.Compare(new byte[10], new byte[10], cts.Token));
    }
}
