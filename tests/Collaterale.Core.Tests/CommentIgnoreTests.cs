using Collaterale.Core.Diff;
using Collaterale.Core.Languages;

namespace Collaterale.Core.Tests;

public class CommentIgnoreTests
{
    private static string[] Strip(CommentSyntax s, params string[] lines) => [.. CommentStripper.Strip(lines, s).Select(l => l.Text)];

    [Fact]
    public void CLike_line_and_block_comments_are_removed()
    {
        Assert.Equal(["int x = 1;", "", "int y = 2;  int z;"], Strip(CommentSyntaxes.CLike,
            "int x = 1; // set x", "// only a comment", "int y = 2; /* inline */ int z;"));
    }

    [Fact]
    public void Multi_line_block_comments_blank_every_line_they_cover()
    {
        var code = CommentStripper.Strip(["a /* start", "middle", "end */ b", "c"], CommentSyntaxes.CLike);
        Assert.Equal(["a", "", " b", "c"], code.Select(c => c.Text));
        Assert.True(code[1].Ignorable);
        Assert.False(code[0].Ignorable);
    }

    [Fact]
    public void Comment_markers_inside_strings_are_code()
    {
        Assert.Equal(["var u = \"http://x.y/*z*/\";", "c = '#' + \"//\";"], Strip(CommentSyntaxes.CSharp, "var u = \"http://x.y/*z*/\"; // real comment", "c = '#' + \"//\";"));
    }

    [Fact]
    public void Escaped_quotes_do_not_end_a_string()
    {
        Assert.Equal(["s = \"a\\\" // still string\";"], Strip(CommentSyntaxes.CLike, "s = \"a\\\" // still string\"; // comment"));
    }

    [Fact]
    public void CSharp_verbatim_and_raw_strings_span_lines_and_hide_comment_markers()
    {
        var code = CommentStripper.Strip(["var a = @\"line one // not a comment", "second \"\" quote /* nor this */\";  // real", "var b = 1;"], CommentSyntaxes.CSharp);
        Assert.Equal("var a = @\"line one // not a comment", code[0].Text);
        Assert.Equal("second \"\" quote /* nor this */\";", code[1].Text);
        Assert.Equal("var b = 1;", code[2].Text);
    }

    [Fact]
    public void Python_hash_comments_and_triple_quoted_strings()
    {
        var code = CommentStripper.Strip(["x = 1  # note", "\"\"\"doc # not a comment", "still doc\"\"\"  # but this is", "y = '#'"], CommentSyntaxes.Python);
        Assert.Equal(["x = 1", "\"\"\"doc # not a comment", "still doc\"\"\"", "y = '#'"], code.Select(c => c.Text));
    }

    [Fact]
    public void Hash_languages_need_a_space_before_the_hash()
    {
        Assert.Equal(["echo ${#arr[@]}", "url: a#anchor", "key: value"], Strip(CommentSyntaxes.Hash, "echo ${#arr[@]}", "url: a#anchor", "key: value # real"));
    }

    [Fact]
    public void Markup_comments_span_lines()
    {
        var code = CommentStripper.Strip(["<a><!-- x --><b/>", "<!-- multi", "line -->", "<c/>"], CommentSyntaxes.Markup);
        Assert.Equal(["<a><b/>", "", "", "<c/>"], code.Select(c => c.Text));
        Assert.True(code[1].Ignorable && code[2].Ignorable);
    }

    [Fact]
    public void Sql_dash_comments_and_doubled_quote_strings()
    {
        Assert.Equal(["select 'it''s -- not a comment' from t;", "select 1;"], Strip(CommentSyntaxes.Sql, "select 'it''s -- not a comment' from t; -- trailing", "select 1; /* c */"));
    }

    [Fact]
    public void Lua_block_comment_is_not_taken_for_a_line_comment()
    {
        var code = CommentStripper.Strip(["a = 1 --[[ block", "still ]] b = 2", "c = 3 -- line"], CommentSyntaxes.Lua);
        Assert.Equal(["a = 1", " b = 2", "c = 3"], code.Select(c => c.Text));
    }

    [Fact]
    public void Ini_only_comments_a_whole_line()
    {
        Assert.Equal(["key=a;b#c", "", "other=1"], Strip(CommentSyntaxes.Ini, "key=a;b#c", "; comment", "other=1"));
    }

    [Theory]
    [InlineData("a.cs", "csharp")]
    [InlineData("A.TS", "javascript")]
    [InlineData("x.py", "python")]
    [InlineData("page.HTML", "markup")]
    [InlineData("q.sql", "sql")]
    [InlineData("Makefile", "hash")]
    [InlineData(".gitignore", "hash")]
    [InlineData("main.go", "c-like")]
    public void Syntax_is_chosen_from_the_file_name(string name, string id) => Assert.Equal(id, CommentSyntaxes.ForPath("/x/" + name)?.Id);

    [Fact]
    public void Unknown_extensions_have_no_syntax() => Assert.Null(CommentSyntaxes.ForPath("data.xyz"));

    // ───── effect on the comparison ─────

    private static TextComparison Cmp(string[] l, string[] r, CommentSyntax? s = null) => TextComparison.Compare(l, r, new DiffOptions { Comments = s ?? CommentSyntaxes.CSharp, DetectMoves = false });

    [Fact]
    public void A_changed_trailing_comment_is_not_a_difference()
    {
        var c = Cmp(["int x = 1; // old note", "int y;"], ["int x = 1; // new note", "int y;"]);
        Assert.True(c.AreEqual);
        Assert.Equal(RowKind.Unimportant, c.Rows[0].Kind);
        Assert.Equal(RowKind.Same, c.Rows[1].Kind);
    }

    [Fact]
    public void An_added_comment_only_line_is_shown_but_is_not_a_difference()
    {
        var c = Cmp(["a;", "b;"], ["a;", "// new explanation", "b;"]);
        Assert.True(c.AreEqual);
        Assert.Equal(3, c.Rows.Count);
        Assert.Equal(RowKind.Unimportant, c.Rows[1].Kind);
        Assert.Equal(-1, c.Rows[1].Left);
        Assert.Equal(1, c.Rows[1].Right);
    }

    [Fact]
    public void A_real_code_change_next_to_a_comment_change_is_still_reported()
    {
        var c = Cmp(["// header v1", "x = 1;", "y = 2;"], ["// header v2", "x = 9;", "y = 2;"]);
        Assert.Equal(1, c.DifferenceCount);
        Assert.Equal(RowKind.Unimportant, c.Rows[0].Kind);
        Assert.Equal(RowKind.Changed, c.Rows[1].Kind);
    }

    [Fact]
    public void Commenting_out_code_is_a_difference()
    {
        var c = Cmp(["x = 1;", "y = 2;"], ["x = 1;", "// y = 2;"]);
        Assert.Equal(1, c.DifferenceCount);
    }

    [Fact]
    public void Intra_line_highlight_covers_code_only_and_maps_back_to_the_full_line()
    {
        var c = Cmp(["total = a + b; // sum"], ["total = a - b; // difference"]);
        Assert.Equal(1, c.DifferenceCount);
        var (l, r) = c.IntraLine(0);
        var left = c.LeftLines[0]; var right = c.RightLines[0];
        Assert.All(l, s => Assert.DoesNotContain("sum", left.Substring(s.Start, s.Length)));
        Assert.Contains(l, s => left.Substring(s.Start, s.Length).Contains('+'));
        Assert.Contains(r, s => right.Substring(s.Start, s.Length).Contains('-'));
        Assert.All(r, s => Assert.DoesNotContain("difference", right.Substring(s.Start, s.Length)));
    }

    [Fact]
    public void Without_a_syntax_comments_are_ordinary_text()
        => Assert.False(TextComparison.Compare(["a // 1"], ["a // 2"]).AreEqual);

    /// <summary>Property: sprinkling comments over a program (anywhere outside strings) must never create a difference, whatever the seed.</summary>
    [Fact]
    public void Randomly_inserted_comments_never_create_differences()
    {
        string[] code =
        [
            "using System;", "namespace Demo {", "  class P {", "    static void Main() {", "      var s = \"// not a comment\";", "      Console.WriteLine(s + \"/* nor this */\");",
            "      for (int i = 0; i < 10; i++) { Run(i); }", "    }", "  }", "}",
        ];
        for (var seed = 0; seed < 200; seed++)
        {
            var rnd = new Random(seed);
            var noisy = new List<string>();
            foreach (var line in code)
            {
                if (rnd.Next(4) == 0) { noisy.Add("// note " + rnd.Next(1000)); }
                if (rnd.Next(5) == 0) { noisy.Add("/* block " + rnd.Next(1000)); noisy.Add("   continues */"); }
                var l = line;
                if (rnd.Next(3) == 0 && !line.Contains('"')) { l += " // trailing " + rnd.Next(1000); }
                noisy.Add(l);
            }

            var c = Cmp(code, [.. noisy]);
            Assert.True(c.AreEqual, $"seed {seed}: {c.DifferenceCount} differences");
        }
    }

    [Fact]
    public void Comment_stripping_is_linear_enough_for_large_files()
    {
        var lines = Enumerable.Range(0, 300_000).Select(i => i % 7 == 0 ? $"// comment {i}" : $"var v{i} = {i}; // c{i}").ToArray();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var c = TextComparison.Compare(lines, lines.Select(l => l + " ").ToArray(), new DiffOptions { Comments = CommentSyntaxes.CSharp, DetectMoves = false });
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed}");
        Assert.True(c.AreEqual);
    }
}

public class TimestampIgnoreTests
{
    private static bool Equal(string a, string b) => TextComparison.Compare([a], [b], new DiffOptions { IgnoreTimestamps = true, DetectMoves = false }).AreEqual;

    [Theory]
    [InlineData("built 2024-03-01T08:15:30Z by ci", "built 2025-12-24T23:59:01+01:00 by ci")]
    [InlineData("log 2024-03-01 08:15:30,123 INFO start", "log 2024-03-02 09:00:00,001 INFO start")]
    [InlineData("Date: Fri, 01 Mar 2024 08:15:30 GMT", "Date: Mon, 15 Jul 2030 23:00:00 +0200")]
    [InlineData("saved on March 1st, 2024 at 8:15 PM", "saved on May 3, 2031 at 11:02 AM")]
    [InlineData("modified 01/03/2024", "modified 31.12.2030")]
    [InlineData("took 12:34:56.789 total", "took 00:00:01.5 total")]
    public void Lines_differing_only_by_a_timestamp_are_equal(string a, string b)
    {
        Assert.True(Equal(a, b));
        Assert.False(TextComparison.Compare([a], [b]).AreEqual);   // and without the option they differ
    }

    [Theory]
    [InlineData("version 1.2.3", "version 1.2.4")]
    [InlineData("user=alice at 2024-03-01", "user=bob at 2024-03-01")]
    [InlineData("port 8080", "port 8081")]
    public void Other_differences_are_kept(string a, string b) => Assert.False(Equal(a, b));

    [Fact]
    public void Unimportant_rows_are_marked_when_the_raw_text_differs()
    {
        var c = TextComparison.Compare(["t 2024-01-01"], ["t 2025-02-02"], new DiffOptions { IgnoreTimestamps = true });
        Assert.Equal(RowKind.Unimportant, c.Rows[0].Kind);
    }
}

public class BlankLineIgnoreTests
{
    private static TextComparison Cmp(string[] l, string[] r) => TextComparison.Compare(l, r, new DiffOptions { IgnoreBlankLines = true, DetectMoves = false });

    [Fact]
    public void Added_and_removed_blank_lines_are_not_differences()
    {
        var c = Cmp(["a", "", "b", "c"], ["a", "b", "", "  ", "c"]);
        Assert.True(c.AreEqual);
        Assert.Contains(c.Rows, r => r.Kind == RowKind.Unimportant && r.Left == 1);
        Assert.Equal(0, c.Rows.Count(r => r.IsDifference));
    }

    [Fact]
    public void Replacing_a_blank_line_with_text_is_a_difference()
    {
        var c = Cmp(["a", "", "b"], ["a", "x", "b"]);
        Assert.Equal(1, c.DifferenceCount);
        Assert.Contains(c.Rows, r => r.Kind == RowKind.RightOnly);
    }

    [Fact]
    public void Without_the_option_blank_lines_count()
        => Assert.False(TextComparison.Compare(["a", "", "b"], ["a", "b"]).AreEqual);

    [Fact]
    public void Real_changes_next_to_blank_line_changes_are_still_found()
    {
        var c = Cmp(["a", "", "b = 1", "c"], ["a", "b = 2", "", "", "c"]);
        Assert.Equal(1, c.DifferenceCount);
    }

    [Fact]
    public void Works_together_with_comment_rules()
    {
        var c = TextComparison.Compare(["a;", "", "// c", "b;"], ["a;", "b;"], new DiffOptions { IgnoreBlankLines = true, Comments = Collaterale.Core.Languages.CommentSyntaxes.CSharp });
        Assert.True(c.AreEqual);
    }
}

public class FilteredRowInvariantTests
{
    private static string[] Random(Random rnd, int n)
        => [.. Enumerable.Range(0, n).Select(_ => rnd.Next(6) switch
        {
            0 => "",
            1 => "   ",
            2 => "// note " + rnd.Next(3),
            3 => "x" + rnd.Next(4) + "; // c" + rnd.Next(3),
            _ => "line" + rnd.Next(5) + ";",
        })];

    [Fact]
    public void Every_line_appears_once_in_order_and_groups_match_their_rows()
    {
        var options = new DiffOptions { IgnoreBlankLines = true, Comments = Collaterale.Core.Languages.CommentSyntaxes.CSharp, DetectMoves = false };
        for (var seed = 0; seed < 500; seed++)
        {
            var rnd = new Random(seed);
            var l = Random(rnd, rnd.Next(0, 40));
            var r = Random(rnd, rnd.Next(0, 40));
            var c = TextComparison.Compare(l, r, options);

            Assert.Equal(Enumerable.Range(0, l.Length), c.Rows.Where(x => x.Left >= 0).Select(x => x.Left));
            Assert.Equal(Enumerable.Range(0, r.Length), c.Rows.Where(x => x.Right >= 0).Select(x => x.Right));

            foreach (var g in c.Groups)
            {
                var span = c.Rows.Skip(g.FirstRow).Take(g.RowCount).ToList();
                Assert.All(span.Where(x => x.Group >= 0), x => Assert.Equal(g.Index, x.Group));
                Assert.Equal(g.LeftCount, span.Count(x => x.Left >= 0));
                Assert.Equal(g.RightCount, span.Count(x => x.Right >= 0));
                if (g.LeftCount > 0) { Assert.Equal(g.LeftStart, span.First(x => x.Left >= 0).Left); }
                if (g.RightCount > 0) { Assert.Equal(g.RightStart, span.First(x => x.Right >= 0).Right); }
                Assert.True(span.First().IsDifference && span.Last().IsDifference);   // a group starts and ends on real differences
            }

            // and the answer agrees with comparing the lines that can matter, with the unimportant ones removed beforehand
            static string[] Meaningful(string[] lines) => [.. lines.Where(x => x.Trim().Length > 0 && !x.TrimStart().StartsWith("//")).Select(x => x.Contains("//") ? x[..x.IndexOf("//", StringComparison.Ordinal)].TrimEnd() : x)];
            var reference = TextComparison.Compare(Meaningful(l), Meaningful(r), new DiffOptions { DetectMoves = false });
            Assert.Equal(reference.DifferenceCount, c.DifferenceCount);
            Assert.Equal(reference.AreEqual, c.AreEqual);
        }
    }
}
