using Collaterale.Core.Diff;

namespace Collaterale.Core.Tests;

public class IsolationTests
{
    private static string[] L(params string[] x) => x;

    /// <summary>Every line of each side is on exactly one row and rows keep the order of both files.</summary>
    private static void AssertWellFormed(TextComparison c)
    {
        int nextL = 0, nextR = 0;
        foreach (var row in c.Rows)
        {
            if (row.Left >= 0) { Assert.Equal(nextL++, row.Left); }
            if (row.Right >= 0) { Assert.Equal(nextR++, row.Right); }
        }

        Assert.Equal(c.LeftLines.Count, nextL);
        Assert.Equal(c.RightLines.Count, nextR);
        for (var g = 0; g < c.Groups.Count; g++)
        {
            Assert.Equal(g, c.Groups[g].Index);
            Assert.All(Enumerable.Range(c.Groups[g].FirstRow, c.Groups[g].RowCount), r => Assert.Equal(g, c.Rows[r].Group));
        }
    }

    [Fact]
    public void Without_isolations_nothing_changes()
    {
        var a = L("a", "b", "c", "d");
        var b = L("a", "x", "c", "d");
        var plain = TextComparison.Compare(a, b);
        var same = TextComparison.Compare(a, b, new DiffOptions { Isolations = [] });
        Assert.Equal(plain.Rows, same.Rows);
    }

    [Fact]
    public void Isolated_lines_that_have_a_twin_elsewhere_are_not_paired_with_it()
    {
        // normally "dup" on the left pairs with "dup" on the right; isolated, it is forced to stand alone
        var a = L("one", "dup", "two");
        var b = L("one", "two", "dup");
        var iso = TextComparison.Compare(a, b, new DiffOptions { DetectMoves = false, Isolations = [new Isolation(1, 1, 2, 0)] });
        AssertWellFormed(iso);
        var row = iso.Rows.Single(r => r.Left == 1);
        Assert.Equal(RowKind.LeftOnly, row.Kind);
        Assert.Equal(-1, row.Right);
    }

    [Fact]
    public void Two_selected_ranges_are_compared_only_with_each_other()
    {
        var a = L("keep", "apple", "pear", "end");
        var b = L("keep", "x1", "x2", "end", "apple", "pear");
        var iso = TextComparison.Compare(a, b, new DiffOptions { DetectMoves = false, Isolations = [new Isolation(1, 2, 1, 2)] });
        AssertWellFormed(iso);
        // left [apple, pear] sits level with right [x1, x2] although "apple"/"pear" also exist further down on the right
        var paired = iso.Rows.Where(r => r.Left is 1 or 2).ToList();
        Assert.All(paired, r => Assert.InRange(r.Right, -1, 2));
        Assert.All(paired, r => Assert.NotEqual(RowKind.Same, r.Kind));
    }

    [Fact]
    public void The_isolated_block_is_its_own_difference()
    {
        var a = L("a", "b", "c", "d", "e");
        var b = L("a", "X", "c", "Y", "e");
        var iso = TextComparison.Compare(a, b, new DiffOptions { Isolations = [new Isolation(1, 3, 1, 3)] });
        AssertWellFormed(iso);
        Assert.True(iso.Groups.Count >= 1);
        Assert.Equal(1, iso.Groups[0].LeftStart);
    }

    [Fact]
    public void Invalid_or_overlapping_isolations_are_ignored_not_fatal()
    {
        var a = L("a", "b", "c");
        var b = L("a", "b", "c");
        var iso = TextComparison.Compare(a, b, new DiffOptions { Isolations = [new Isolation(5, 2, 0, 0), new Isolation(0, 2, 0, 2), new Isolation(1, 1, 1, 1)] });
        AssertWellFormed(iso);
        Assert.True(iso.AreEqual);
    }

    [Fact]
    public void Random_texts_with_random_isolations_stay_well_formed_and_keep_isolated_lines_apart()
    {
        for (var seed = 0; seed < 300; seed++)
        {
            var rnd = new Random(seed);
            var a = Enumerable.Range(0, rnd.Next(0, 25)).Select(_ => "t" + rnd.Next(6)).ToArray();
            var b = Enumerable.Range(0, rnd.Next(0, 25)).Select(_ => "t" + rnd.Next(6)).ToArray();
            var isos = new List<Isolation>();
            int la = 0, lb = 0;
            while (rnd.Next(2) == 0 && la <= a.Length && lb <= b.Length)
            {
                var ls = rnd.Next(la, a.Length + 1);
                var rs = rnd.Next(lb, b.Length + 1);
                var lc = rnd.Next(0, Math.Min(4, a.Length - ls) + 1);
                var rc = rnd.Next(0, Math.Min(4, b.Length - rs) + 1);
                isos.Add(new Isolation(ls, lc, rs, rc));
                (la, lb) = (ls + lc, rs + rc);
            }

            var c = TextComparison.Compare(a, b, new DiffOptions { Isolations = isos });
            AssertWellFormed(c);
            foreach (var iso in isos)
            {
                foreach (var row in c.Rows.Where(r => r.Left >= iso.LeftStart && r.Left < iso.LeftStart + iso.LeftCount && r.Right >= 0))
                {
                    Assert.InRange(row.Right, iso.RightStart, iso.RightStart + iso.RightCount - 1);
                }

                foreach (var row in c.Rows.Where(r => r.Right >= iso.RightStart && r.Right < iso.RightStart + iso.RightCount && r.Left >= 0))
                {
                    Assert.InRange(row.Left, iso.LeftStart, iso.LeftStart + iso.LeftCount - 1);
                }
            }
        }
    }
}
