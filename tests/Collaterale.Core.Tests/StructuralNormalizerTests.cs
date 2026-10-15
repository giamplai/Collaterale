using Collaterale.Core.Diff;
using Collaterale.Core.Languages;

namespace Collaterale.Core.Tests;

public class StructuralNormalizerTests
{
    private static string[] J(string s, bool sort = true) => StructuralNormalizer.Normalize(s, StructuredFormat.Json, sort)!;
    private static string[] X(string s) => StructuralNormalizer.Normalize(s, StructuredFormat.Xml)!;

    [Fact]
    public void Json_formatting_and_key_order_do_not_matter()
    {
        var a = "{\"b\":1,\"a\":{\"y\":[1,2],\"x\":true}}";
        var b = "{\n    \"a\": { \"x\": true,\n  \"y\": [1, 2] },\n\n  \"b\": 1 }";
        Assert.Equal(J(a), J(b));
        Assert.True(TextComparison.Compare(J(a), J(b)).AreEqual);
    }

    [Fact]
    public void Json_array_order_and_values_are_real_changes()
    {
        Assert.NotEqual(J("{\"a\":[1,2]}"), J("{\"a\":[2,1]}"));
        Assert.NotEqual(J("{\"a\":1}"), J("{\"a\":2}"));
        Assert.Equal(1, TextComparison.Compare(J("{\"a\":1,\"b\":2,\"c\":3}"), J("{\"c\":3,\"b\":99,\"a\":1}")).DifferenceCount);
    }

    [Fact]
    public void Json_key_sorting_can_be_turned_off()
        => Assert.NotEqual(J("{\"a\":1,\"b\":2}", sort: false), J("{\"b\":2,\"a\":1}", sort: false));

    [Fact]
    public void Json_with_comments_and_trailing_commas_is_accepted_and_numbers_keep_their_spelling()
    {
        var n = J("{ /* c */ \"n\": 1.50, \"e\": 1E3, }");
        Assert.Contains("  \"n\": 1.50", n);
        Assert.Contains("  \"e\": 1E3,", n);
    }

    [Fact]
    public void Unicode_is_not_escaped_away() => Assert.Contains("  \"s\": \"è → ü\"", J("{\"s\":\"\\u00e8 → ü\"}"));

    [Fact]
    public void Invalid_json_or_xml_returns_null()
    {
        Assert.Null(StructuralNormalizer.Normalize("{ nope", StructuredFormat.Json));
        Assert.Null(StructuralNormalizer.Normalize("<a><b></a>", StructuredFormat.Xml));
        Assert.Null(StructuralNormalizer.Normalize("x", StructuredFormat.None));
    }

    [Fact]
    public void Xml_layout_and_attribute_order_do_not_matter()
    {
        var a = "<r><i b=\"2\" a=\"1\">text</i><e/></r>";
        var b = "<r>\n  <i a=\"1\"\n     b=\"2\">text</i>\n\n  <e></e>\n</r>";
        Assert.Equal(X(a), X(b));
    }

    [Fact]
    public void Xml_content_changes_remain_visible()
    {
        var c = TextComparison.Compare(X("<r><i>one</i><i>two</i></r>"), X("<r><i>one</i><i>TWO</i></r>"));
        Assert.Equal(1, c.DifferenceCount);
    }

    [Fact]
    public void Xml_with_a_dtd_is_read_without_fetching_anything() => Assert.NotNull(StructuralNormalizer.Normalize("<?xml version=\"1.0\"?><!DOCTYPE r SYSTEM \"http://example.invalid/x.dtd\"><r/>", StructuredFormat.Xml));

    [Theory]
    [InlineData("a.json", StructuredFormat.Json)]
    [InlineData("x/B.XML", StructuredFormat.Xml)]
    [InlineData("app.csproj", StructuredFormat.Xml)]
    [InlineData("readme.md", StructuredFormat.None)]
    public void Format_comes_from_the_extension(string name, StructuredFormat f) => Assert.Equal(f, StructuralNormalizer.FormatFor(name));
}
