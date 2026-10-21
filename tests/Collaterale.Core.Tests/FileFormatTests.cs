using Collaterale.Core.Diff;
using Collaterale.Core.Languages;
using Collaterale.Core.Settings;

namespace Collaterale.Core.Tests;

[Collection("custom formats")]
public class FileFormatTests : IDisposable
{
    public void Dispose() => CommentSyntaxes.ConfigureCustom([]);

    private static FileFormat Toml() => new() { Name = "Toy config", Extensions = "toy; .cfg2", LineComments = "! --", BlockComments = "<< >> (* *)", Strings = "\" '" };

    [Fact]
    public void A_format_becomes_a_syntax_with_its_comments_and_strings()
    {
        var s = CommentSyntaxes.FromFormat(Toml())!;
        Assert.Equal("Toy config", s.DisplayName);
        Assert.Equal([".toy", ".cfg2"], s.Extensions);
        Assert.Equal(["!", "--"], s.LineStarts);
        Assert.Equal([("<<", ">>"), ("(*", "*)")], s.Blocks);
        Assert.Equal(["\"", "'"], s.Strings.Select(r => r.Open));
    }

    [Theory]
    [InlineData("", "toy", "!")]          // no name
    [InlineData("X", "", "!")]            // no extension
    [InlineData("X", "toy", "")]          // nothing to recognise
    public void An_unusable_format_is_ignored(string name, string ext, string line)
        => Assert.Null(CommentSyntaxes.FromFormat(new FileFormat { Name = name, Extensions = ext, LineComments = line }));

    [Fact]
    public void A_semicolon_can_be_a_comment_marker()
    {
        var s = CommentSyntaxes.FromFormat(new FileFormat { Name = "Asm", Extensions = "asm", LineComments = ";" })!;
        Assert.Equal([";"], s.LineStarts);
    }

    [Fact]
    public void A_block_comment_needs_both_an_opener_and_a_closer()
    {
        var s = CommentSyntaxes.FromFormat(new FileFormat { Name = "X", Extensions = "x", LineComments = "!", BlockComments = "{ } <<" })!;
        Assert.Equal([("{", "}")], s.Blocks);
    }

    [Fact]
    public void Configured_formats_are_found_by_extension_and_id_and_win_over_built_in_ones()
    {
        CommentSyntaxes.ConfigureCustom([Toml(), new FileFormat { Name = "My C#", Extensions = "cs", LineComments = "%" }]);
        Assert.Equal("Toy config", CommentSyntaxes.ForPath("/x/app.TOY")!.DisplayName);
        Assert.Equal("My C#", CommentSyntaxes.ForPath("/x/Program.cs")!.DisplayName);       // the user's definition replaces the built-in one for .cs
        Assert.Equal("Toy config", CommentSyntaxes.ById("custom:Toy config")!.DisplayName);
        Assert.Contains(CommentSyntaxes.All, s => s.DisplayName == "Toy config");
        CommentSyntaxes.ConfigureCustom([]);
        Assert.Equal("C#", CommentSyntaxes.ForPath("/x/Program.cs")!.DisplayName);
        Assert.Null(CommentSyntaxes.ForPath("/x/app.toy"));
    }

    [Fact]
    public void A_custom_format_drives_the_comment_and_string_options()
    {
        var syntax = CommentSyntaxes.FromFormat(Toml())!;
        var a = new[] { "name = \"alpha\" ! first", "(* note *) keep = 1" };
        var b = new[] { "name = \"beta\" ! second", "(* other *) keep = 1" };
        Assert.False(TextComparison.Compare(a, b).AreEqual);
        Assert.False(TextComparison.Compare(a, b, new DiffOptions { Comments = syntax }).AreEqual);                         // the strings still differ
        Assert.True(TextComparison.Compare(a, b, new DiffOptions { Comments = syntax, IgnoreStrings = true }).AreEqual);   // both unimportant: only code is left
    }
}
