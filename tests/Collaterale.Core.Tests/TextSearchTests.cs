using Collaterale.Core.Search;

namespace Collaterale.Core.Tests;

public class TextSearchTests
{
    private static readonly string[] Lines = ["The apple falls", "snapple and applesauce", "Apple pie", "no fruit here", "apple"];

    private static SearchOptions Plain(bool matchCase = false, bool whole = false, bool wrap = true) => new(Regex: false, MatchCase: matchCase, WholeWords: whole, Wrap: wrap);

    [Fact]
    public void Plain_search_is_case_insensitive_by_default_and_finds_every_occurrence()
    {
        var all = TextSearch.All(Lines, "apple", Plain());
        Assert.Equal([(0, 4), (1, 2), (1, 12), (2, 0), (4, 0)], all.Select(m => (m.Line, m.Start)));
    }

    [Fact]
    public void Match_case_and_whole_words_narrow_the_result()
    {
        Assert.Equal([(0, 4), (1, 2), (1, 12), (4, 0)], TextSearch.All(Lines, "apple", Plain(matchCase: true)).Select(m => (m.Line, m.Start)));
        // "apple" is not a whole word inside snapple or applesauce
        Assert.Equal([(0, 4), (2, 0), (4, 0)], TextSearch.All(Lines, "apple", Plain(whole: true)).Select(m => (m.Line, m.Start)));
    }

    [Fact]
    public void Special_characters_are_literal_in_plain_mode_and_patterns_in_regex_mode()
    {
        string[] lines = ["a.b", "axb", "f(x)"];
        Assert.Single(TextSearch.All(lines, "a.b", Plain()));
        Assert.Equal(2, TextSearch.All(lines, "a.b", new SearchOptions(Regex: true)).Count);
        Assert.Single(TextSearch.All(lines, "f(x)", Plain()));
    }

    [Fact]
    public void Next_moves_forward_and_wraps_only_when_asked()
    {
        var m1 = TextSearch.Next(Lines, "apple", Plain(), line: 0, column: 0)!.Value;
        Assert.Equal((0, 4), (m1.Line, m1.Start));
        var m2 = TextSearch.Next(Lines, "apple", Plain(), m1.Line, m1.Start + 1)!.Value;
        Assert.Equal((1, 2), (m2.Line, m2.Start));
        Assert.Null(TextSearch.Next(Lines, "apple", Plain(wrap: false), line: 4, column: 1));
        var wrapped = TextSearch.Next(Lines, "apple", Plain(wrap: true), line: 4, column: 1)!.Value;
        Assert.Equal((0, 4), (wrapped.Line, wrapped.Start));
    }

    [Fact]
    public void Previous_moves_backward_and_wraps_only_when_asked()
    {
        var p = TextSearch.Previous(Lines, "apple", Plain(), line: 2, column: 0)!.Value;
        Assert.Equal((1, 12), (p.Line, p.Start));
        Assert.Null(TextSearch.Previous(Lines, "apple", Plain(wrap: false), line: 0, column: 4));
        var wrapped = TextSearch.Previous(Lines, "apple", Plain(wrap: true), line: 0, column: 4)!.Value;
        Assert.Equal((4, 0), (wrapped.Line, wrapped.Start));
    }

    [Fact]
    public void An_empty_pattern_or_no_match_finds_nothing()
    {
        Assert.Empty(TextSearch.All(Lines, "", Plain()));
        Assert.Null(TextSearch.Next(Lines, "zebra", Plain(), 0, 0));
    }

    [Fact]
    public void An_invalid_regular_expression_is_reported_not_thrown()
    {
        Assert.NotNull(TextSearch.Validate("(", new SearchOptions(Regex: true)));
        Assert.Null(TextSearch.Validate("(a)", new SearchOptions(Regex: true)));
        Assert.Null(TextSearch.Validate("(", Plain()));                       // literal in plain mode
        Assert.Empty(TextSearch.All(Lines, "(", new SearchOptions(Regex: true)));
    }

    [Fact]
    public void Regex_search_supports_anchors_and_groups()
    {
        Assert.Equal([2], TextSearch.All(Lines, "^Apple", new SearchOptions(Regex: true, MatchCase: true)).Select(m => m.Line));
        Assert.Equal([0, 1, 2, 4], TextSearch.All(Lines, @"\bapple|apple\w+", new SearchOptions(Regex: true)).Select(m => m.Line).Distinct());
    }

    [Fact]
    public void Replace_one_and_replace_all_keep_the_rest_of_the_text()
    {
        var m = TextSearch.Next(Lines, "apple", Plain(matchCase: true), 0, 0)!.Value;
        Assert.Equal("The pear falls", TextSearch.ReplaceIn(Lines[0], m, "apple", "pear", Plain(matchCase: true)));

        var (replaced, count) = TextSearch.ReplaceAll(Lines, "apple", "pear", Plain());
        Assert.Equal(5, count);
        Assert.Equal(["The pear falls", "snpear and pearsauce", "pear pie", "no fruit here", "pear"], replaced);
        Assert.Equal(Lines.Length, replaced.Count);
    }

    [Fact]
    public void Regex_replacement_can_use_groups()
    {
        var (replaced, count) = TextSearch.ReplaceAll(["id=12", "id=7"], @"id=(\d+)", "#$1", new SearchOptions(Regex: true));
        Assert.Equal(2, count);
        Assert.Equal(["#12", "#7"], replaced);
        // in plain mode "$1" is just text
        var (plain, _) = TextSearch.ReplaceAll(["a"], "a", "$1", Plain());
        Assert.Equal(["$1"], plain);
    }

    [Fact]
    public void Select_all_reports_each_line_that_contains_a_match_once()
    {
        Assert.Equal([0, 1, 2, 4], TextSearch.LinesWithMatches(Lines, "apple", Plain()));
    }
}
