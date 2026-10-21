using Collaterale.Core.Diff;
using Collaterale.Core.Languages;

namespace Collaterale.Core.Tests;

public class StringIgnoreTests
{
    private static DiffOptions Strings(CommentSyntax s) => new() { IgnoreStrings = true, Language = s };

    private static string[] Strip(CommentSyntax s, bool comments, bool strings, params string[] lines)
        => [.. CommentStripper.Strip(lines, s, comments, strings).Select(l => l.Text)];

    [Fact]
    public void String_contents_are_emptied_but_the_quotes_stay()
    {
        Assert.Equal(["var x = \"\";", "var c = '' + \"\";"], Strip(CommentSyntaxes.CSharp, false, true, "var x = \"hello\";", "var c = 'a' + \"w \\\" q\";"));
    }

    [Fact]
    public void Comments_are_kept_when_only_strings_are_unimportant()
    {
        Assert.Equal(["x = \"\"; // keep me", "y = \"\"; /* and me */"], Strip(CommentSyntaxes.CLike, false, true, "x = \"a\"; // keep me", "y = \"b\"; /* and me */"));
    }

    [Fact]
    public void A_comment_marker_inside_a_string_is_still_not_a_comment()
    {
        Assert.Equal(["u = \"\"; // real"], Strip(CommentSyntaxes.CLike, false, true, "u = \"http://x\"; // real"));
    }

    [Fact]
    public void Both_unimportant_removes_comments_and_string_contents()
    {
        Assert.Equal(["x = \"\";"], Strip(CommentSyntaxes.CLike, true, true, "x = \"abc\"; // note"));
    }

    [Fact]
    public void Multi_line_strings_lose_their_content_on_every_line()
    {
        var code = CommentStripper.Strip(["doc = \"\"\"first", "second", "third\"\"\"", "y = 1"], CommentSyntaxes.Python, false, true);
        Assert.Equal(["doc = \"\"\"", "", "\"\"\"", "y = 1"], code.Select(c => c.Text));
        Assert.True(code[1].Ignorable);
        Assert.False(code[0].Ignorable);
    }

    [Fact]
    public void Lines_that_differ_only_inside_strings_are_not_differences()
    {
        var a = new[] { "var greeting = \"hello\";", "Run(1);" };
        var b = new[] { "var greeting = \"bonjour\";", "Run(1);" };
        Assert.False(TextComparison.Compare(a, b).AreEqual);
        var c = TextComparison.Compare(a, b, Strings(CommentSyntaxes.CSharp));
        Assert.True(c.AreEqual);
        Assert.Equal(RowKind.Unimportant, c.Rows[0].Kind);
    }

    [Fact]
    public void A_change_in_the_code_is_still_a_difference()
    {
        var c = TextComparison.Compare(["var x = \"a\";"], ["var y = \"b\";"], Strings(CommentSyntaxes.CSharp));
        Assert.False(c.AreEqual);
    }

    [Fact]
    public void Comments_still_count_when_only_strings_are_unimportant()
    {
        var c = TextComparison.Compare(["x(); // one"], ["x(); // two"], Strings(CommentSyntaxes.CLike));
        Assert.False(c.AreEqual);
    }

    [Fact]
    public void Strings_and_comments_together_leave_only_the_code()
    {
        var o = new DiffOptions { IgnoreStrings = true, Comments = CommentSyntaxes.CSharp };
        var c = TextComparison.Compare(["x = \"a\"; // one", "y();"], ["x = \"b\"; // two", "y();"], o);
        Assert.True(c.AreEqual);
    }

    [Fact]
    public void Word_highlighting_survives_ignored_strings()
    {
        var c = TextComparison.Compare(["a(\"x\", 1);"], ["a(\"y\", 2);"], Strings(CommentSyntaxes.CSharp));
        Assert.False(c.AreEqual);
        var (left, right) = c.IntraLine(0);
        Assert.NotEmpty(left);
        Assert.NotEmpty(right);
    }

    [Fact]
    public void Without_a_language_the_option_does_nothing()
    {
        var c = TextComparison.Compare(["x = \"a\""], ["x = \"b\""], new DiffOptions { IgnoreStrings = true });
        Assert.False(c.AreEqual);
    }
}
