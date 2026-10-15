using System.Diagnostics;
using Collaterale.Core.Diff;

namespace Collaterale.Core.Tests;

public class SequenceDiffTests
{
    /// <summary>Rebuilds b from a by replaying the blocks; proves the diff is a valid edit script.</summary>
    private static int[] Apply(int[] a, int[] b, List<DiffBlock> blocks)
    {
        var result = new List<int>();
        var pa = 0;
        foreach (var blk in blocks)
        {
            Assert.True(blk.AStart >= pa, "blocks must be ordered and non-overlapping");
            for (; pa < blk.AStart; pa++) { result.Add(a[pa]); }
            result.AddRange(b.Skip(blk.BStart).Take(blk.BLength));
            pa = blk.AEnd;
        }

        for (; pa < a.Length; pa++) { result.Add(a[pa]); }
        return [.. result];
    }

    private static int LcsLength(int[] a, int[] b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }

        return dp[a.Length, b.Length];
    }

    private static int EditCost(List<DiffBlock> blocks) => blocks.Sum(x => x.ALength + x.BLength);

    private static int[] Random(Random r, int maxLen, int alphabet) => Enumerable.Range(0, r.Next(maxLen + 1)).Select(_ => r.Next(alphabet)).ToArray();

    [Fact]
    public void Identical_and_empty_inputs()
    {
        Assert.Empty(SequenceDiff.Diff([], []));
        Assert.Empty(SequenceDiff.Diff([1, 2, 3], [1, 2, 3]));
        Assert.Equal([new DiffBlock(0, 0, 0, 2)], SequenceDiff.Diff([], [7, 8]));
        Assert.Equal([new DiffBlock(0, 2, 0, 0)], SequenceDiff.Diff([7, 8], []));
    }

    [Fact]
    public void Simple_known_cases()
    {
        // one line changed in the middle
        Assert.Equal([new DiffBlock(1, 1, 1, 1)], SequenceDiff.Diff([1, 2, 3], [1, 9, 3]));
        // insertion and deletion
        Assert.Equal([new DiffBlock(2, 0, 2, 1)], SequenceDiff.Diff([1, 2, 3], [1, 2, 9, 3]));
        Assert.Equal([new DiffBlock(1, 1, 1, 0)], SequenceDiff.Diff([1, 2, 3], [1, 3]));
    }

    [Theory]
    [InlineData(DiffAlgorithm.Myers)]
    [InlineData(DiffAlgorithm.Patience)]
    [InlineData(DiffAlgorithm.Histogram)]
    public void Random_inputs_reconstruct_exactly(DiffAlgorithm algo)
    {
        var rnd = new Random(12345);
        for (var i = 0; i < 4000; i++)
        {
            var a = Random(rnd, 40, rnd.Next(1, 7));
            var b = Random(rnd, 40, rnd.Next(1, 7));
            var blocks = SequenceDiff.Diff(a, b, algo);
            Assert.Equal(b, Apply(a, b, blocks));
        }
    }

    [Fact]
    public void Myers_is_minimal_against_a_brute_force_lcs()
    {
        var rnd = new Random(777);
        for (var i = 0; i < 4000; i++)
        {
            var a = Random(rnd, 30, rnd.Next(1, 6));
            var b = Random(rnd, 30, rnd.Next(1, 6));
            var cost = EditCost(SequenceDiff.Diff(a, b));
            var minimal = a.Length + b.Length - 2 * LcsLength(a, b);
            Assert.True(cost == minimal, $"not minimal for a=[{string.Join(',', a)}] b=[{string.Join(',', b)}]: {cost} vs {minimal}");
        }
    }

    [Fact]
    public void Patience_prefers_unique_anchors_over_repeated_lines()
    {
        // classic: braces repeat, the unique "name" lines should anchor the diff
        int[] a = [1, 10, 2, 3, 11, 1, 20, 2, 3, 21];  // 1,2,3 repeated blocks; 10/11/20/21 unique
        int[] b = [1, 10, 2, 3, 11, 1, 20, 2, 99, 3, 21];
        var blocks = SequenceDiff.Diff(a, b, DiffAlgorithm.Patience);
        Assert.Equal(b, Apply(a, b, blocks));
        Assert.Equal([new DiffBlock(8, 0, 8, 1)], blocks); // just the inserted line
    }

    [Fact]
    public void Large_mostly_equal_files_are_fast()
    {
        var a = Enumerable.Range(0, 200_000).ToArray();
        var b = a.ToList();
        for (var i = 1000; i < b.Count; i += 4000) { b[i] = -i; }  // 50 scattered edits
        b.RemoveRange(150_000, 30);
        var sw = Stopwatch.StartNew();
        var blocks = SequenceDiff.Diff(a, [.. b]);
        sw.Stop();
        Assert.Equal([.. b], Apply(a, [.. b], blocks));
        Assert.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Completely_different_large_files_stay_fast_thanks_to_the_cost_cap()
    {
        var a = Enumerable.Range(0, 30_000).ToArray();
        var b = Enumerable.Range(100_000, 30_000).ToArray();
        var sw = Stopwatch.StartNew();
        var blocks = SequenceDiff.Diff(a, b);
        sw.Stop();
        Assert.Equal(b, Apply(a, b, blocks));
        Assert.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Heavy_overlap_with_repetition_stays_valid_under_the_cost_cap()
    {
        var rnd = new Random(5);
        var a = Enumerable.Range(0, 20_000).Select(_ => rnd.Next(50)).ToArray();
        var b = Enumerable.Range(0, 20_000).Select(_ => rnd.Next(50)).ToArray();
        var sw = Stopwatch.StartNew();
        var blocks = SequenceDiff.Diff(a, b);
        sw.Stop();
        Assert.Equal(b, Apply(a, b, blocks));
        Assert.True(sw.ElapsedMilliseconds < 8000, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Histogram_keeps_a_moved_function_together_where_myers_may_split_it()
    {
        // two functions that share many repeated lines (braces=1, blank=2); the second one gets an extra statement
        int[] a = [10, 1, 11, 12, 1, 2, 20, 1, 21, 22, 1, 2];
        int[] b = [10, 1, 11, 12, 1, 2, 20, 1, 21, 99, 22, 1, 2];
        var blocks = SequenceDiff.Diff(a, b, DiffAlgorithm.Histogram);
        Assert.Equal(b, Apply(a, b, blocks));
        Assert.Equal([new DiffBlock(9, 0, 9, 1)], blocks);   // only the inserted statement differs
    }

    [Fact]
    public void Histogram_on_all_common_lines_falls_back_to_myers_and_stays_correct()
    {
        var a = Enumerable.Repeat(1, 200).Concat([2]).ToArray();   // every line is "too common" to anchor on
        var b = Enumerable.Repeat(1, 150).Concat([3, 2]).ToArray();
        Assert.Equal(b, Apply(a, b, SequenceDiff.Diff(a, b, DiffAlgorithm.Histogram)));
    }

    [Fact]
    public void Histogram_is_fast_on_large_inputs()
    {
        var a = Enumerable.Range(0, 100_000).Select(i => i % 5000).ToArray();     // lots of repetition
        var b = a.ToArray();
        for (var i = 500; i < b.Length; i += 9000) { b[i] = -1; }
        var sw = Stopwatch.StartNew();
        var blocks = SequenceDiff.Diff(a, b, DiffAlgorithm.Histogram);
        sw.Stop();
        Assert.Equal(b, Apply(a, b, blocks));
        Assert.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds} ms");
    }
}

public class ReducedDiffTests
{
    private static int[] Apply(int[] a, int[] b, List<DiffBlock> blocks)
    {
        var result = new List<int>();
        var pos = 0;
        foreach (var blk in blocks)
        {
            result.AddRange(a[pos..blk.AStart]);
            result.AddRange(b[blk.BStart..blk.BEnd]);
            pos = blk.AEnd;
        }

        result.AddRange(a[pos..]);
        return [.. result];
    }

    [Fact]
    public void Reduced_diff_always_turns_a_into_b_and_is_as_short_as_the_plain_diff()
    {
        for (var seed = 0; seed < 300; seed++)
        {
            var rnd = new Random(seed);
            var alphabet = rnd.Next(2, 40);
            var a = Enumerable.Range(0, rnd.Next(0, 120)).Select(_ => rnd.Next(alphabet)).ToArray();
            var b = Enumerable.Range(0, rnd.Next(0, 120)).Select(_ => rnd.Next(rnd.Next(2) == 0 ? alphabet : alphabet * 2)).ToArray();

            var reduced = SequenceDiff.Diff(a, b, DiffAlgorithm.Myers, reduceAbove: 0);
            Assert.Equal(b, Apply(a, b, reduced));

            // optimality: same total number of changed lines as the plain search (both are minimal)
            var plain = SequenceDiff.Diff(a, b, DiffAlgorithm.Myers, reduceAbove: int.MaxValue);
            Assert.Equal(plain.Sum(x => x.ALength + x.BLength), reduced.Sum(x => x.ALength + x.BLength));
        }
    }

    [Theory]
    [InlineData(DiffAlgorithm.Patience)]
    [InlineData(DiffAlgorithm.Histogram)]
    public void Reduction_works_with_every_algorithm(DiffAlgorithm algorithm)
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var rnd = new Random(seed);
            var a = Enumerable.Range(0, rnd.Next(0, 150)).Select(_ => rnd.Next(30)).ToArray();
            var b = Enumerable.Range(0, rnd.Next(0, 150)).Select(_ => rnd.Next(60)).ToArray();
            Assert.Equal(b, Apply(a, b, SequenceDiff.Diff(a, b, algorithm, reduceAbove: 0)));
        }
    }

    [Fact]
    public void Completely_different_large_files_are_instant()
    {
        var a = Enumerable.Range(0, 400_000).ToArray();
        var b = Enumerable.Range(400_000, 400_000).ToArray();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var blocks = SequenceDiff.Diff(a, b);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed}");
        var only = Assert.Single(blocks);
        Assert.Equal(new DiffBlock(0, 400_000, 0, 400_000), only);
    }
}
