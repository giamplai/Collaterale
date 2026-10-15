using Collaterale.Core.Diff;

namespace Collaterale.Core.Tests;

public class TextComparisonTests
{
    private static TextComparison Cmp(string[] l, string[] r, DiffOptions? o = null) => TextComparison.Compare(l, r, o);

    [Theory]
    [InlineData(WhitespaceMode.Exact, "a  b ", "a  b ")]
    [InlineData(WhitespaceMode.IgnoreAmount, "a \t b ", "a b")]
    [InlineData(WhitespaceMode.IgnoreLeadingTrailing, "  a  b  ", "a  b")]
    [InlineData(WhitespaceMode.IgnoreAll, " a  b ", "ab")]
    public void Whitespace_modes_normalize_as_documented(WhitespaceMode mode, string input, string expected)
        => Assert.Equal(expected, new LineKeys(new DiffOptions { Whitespace = mode }).Key(input));

    [Fact]
    public void Case_and_patterns_are_ignored_when_asked()
    {
        var keys = new LineKeys(new DiffOptions { IgnoreCase = true, IgnorePatterns = [@"\d{4}-\d{2}-\d{2}"] });
        Assert.Equal(keys.Key("Built on 2024-01-05 by Ann"), keys.Key("BUILT ON 1999-12-31 BY ANN"));
    }

    [Fact]
    public void Identical_texts_have_no_differences()
    {
        var c = Cmp(["a", "b"], ["a", "b"]);
        Assert.True(c.AreEqual);
        Assert.All(c.Rows, r => Assert.Equal(RowKind.Same, r.Kind));
    }

    [Fact]
    public void Edited_line_is_aligned_opposite_its_old_version_even_with_an_insertion_above_it()
    {
        string[] left = ["header", "total = compute(price, tax)", "footer"];
        string[] right = ["header", "// new comment line here", "total = compute(price, discount)", "footer"];
        var c = Cmp(left, right);

        var diff = c.Rows.Where(r => r.IsDifference).ToList();
        Assert.Equal([RowKind.RightOnly, RowKind.Changed], diff.Select(r => r.Kind));
        Assert.Equal("// new comment line here", right[diff[0].Right]);
        Assert.Equal((1, 2), (diff[1].Left, diff[1].Right)); // the edited lines face each other
        Assert.Equal(1, c.DifferenceCount);
    }

    [Fact]
    public void Dissimilar_replacement_pairs_by_position_and_surplus_is_one_sided()
    {
        var c = Cmp(["x", "AAA", "BBB", "CCC", "y"], ["x", "111", "y"]);
        Assert.Equal([RowKind.Changed, RowKind.LeftOnly, RowKind.LeftOnly], c.Rows.Where(r => r.IsDifference).Select(r => r.Kind));
    }

    [Fact]
    public void Ignored_whitespace_differences_become_unimportant_rows_not_differences()
    {
        var c = Cmp(["if (a)", "  return 1;", "}"], ["if (a)", "      return 1;", "}"], new DiffOptions { Whitespace = WhitespaceMode.IgnoreAll });
        Assert.True(c.AreEqual);
        Assert.Equal([RowKind.Same, RowKind.Unimportant, RowKind.Same], c.Rows.Select(r => r.Kind));
    }

    [Fact]
    public void Ignoring_case_hides_case_only_differences()
    {
        Assert.False(Cmp(["Hello"], ["hello"]).AreEqual);
        Assert.True(Cmp(["Hello"], ["hello"], new DiffOptions { IgnoreCase = true }).AreEqual);
    }

    [Fact]
    public void Random_texts_keep_every_line_exactly_once_and_in_order()
    {
        var rnd = new Random(99);
        string[] pool = ["alpha", "beta", "gamma", "delta", "alpha beta", "beta gamma", "", "x = 1;", "x = 2;", "return x;"];
        for (var n = 0; n < 1500; n++)
        {
            var l = Enumerable.Range(0, rnd.Next(25)).Select(_ => pool[rnd.Next(pool.Length)]).ToArray();
            var r = Enumerable.Range(0, rnd.Next(25)).Select(_ => pool[rnd.Next(pool.Length)]).ToArray();
            var c = Cmp(l, r, rnd.Next(3) == 0 ? new DiffOptions { Whitespace = WhitespaceMode.IgnoreAll, IgnoreCase = true } : null);

            Assert.Equal(Enumerable.Range(0, l.Length), c.Rows.Where(x => x.Left >= 0).Select(x => x.Left));
            Assert.Equal(Enumerable.Range(0, r.Length), c.Rows.Where(x => x.Right >= 0).Select(x => x.Right));
            Assert.All(c.Rows.Where(x => x.Kind == RowKind.Same), x => Assert.Equal(l[x.Left], r[x.Right]));
            // groups tile the difference rows exactly
            var inGroups = c.Groups.SelectMany(g => Enumerable.Range(g.FirstRow, g.RowCount)).ToList();
            Assert.Equal(c.Rows.Select((x, i) => (x, i)).Where(t => t.x.IsDifference).Select(t => t.i), inGroups);
        }
    }

    [Fact]
    public void Huge_changed_blocks_fall_back_to_positional_pairing_quickly()
    {
        var l = Enumerable.Range(0, 3000).Select(i => $"left {i}").ToArray();
        var r = Enumerable.Range(0, 3000).Select(i => $"right {i}").ToArray();
        var c = Cmp(l, r);
        Assert.Equal(3000, c.Rows.Count);
        Assert.All(c.Rows, x => Assert.Equal(RowKind.Changed, x.Kind));
    }

    [Fact]
    public void Intra_line_marks_only_the_changed_words()
    {
        var (ls, rs) = IntraLineDiff.Compare("let total = price * 2;", "let total = price * 3;");
        Assert.Equal([new Span(20, 1)], ls);
        Assert.Equal([new Span(20, 1)], rs);

        var (l2, r2) = IntraLineDiff.Compare("call(a, b)", "call(a, extra, b)");
        Assert.Empty(l2);
        Assert.Equal("extra, ", "call(a, extra, b)".Substring(r2[0].Start, r2[0].Length));
    }

    [Fact]
    public void Intra_line_respects_whitespace_options()
    {
        var (ls, _) = IntraLineDiff.Compare("a  b", "a b", new DiffOptions { Whitespace = WhitespaceMode.IgnoreAmount });
        Assert.Empty(ls);
        var (ls2, _) = IntraLineDiff.Compare("a  b", "a b", new DiffOptions { Whitespace = WhitespaceMode.Exact });
        Assert.NotEmpty(ls2);
    }

    [Fact]
    public void Similarity_ranges_from_identical_to_unrelated()
    {
        Assert.Equal(1.0, IntraLineDiff.Similarity("foo bar", "foo bar"));
        Assert.Equal(0.0, IntraLineDiff.Similarity("foo", "bar"));
        Assert.InRange(IntraLineDiff.Similarity("let x = 1;", "let x = 2;"), 0.6, 0.95);
    }
}
