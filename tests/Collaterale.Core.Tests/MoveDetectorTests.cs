using System.Diagnostics;
using Collaterale.Core.Diff;

namespace Collaterale.Core.Tests;

public class MoveDetectorTests
{
    private static string[] Lines(params string[] l) => l;

    private static readonly string[] Block =
    [
        "    public void Save(Order order)",
        "    {",
        "        repository.Add(order);",
        "        repository.Commit();",
        "    }",
    ];

    /// <summary>A file where <see cref="Block"/> sits near the top (left) or near the bottom (right).</summary>
    private static (string[] Left, string[] Right) MovedPair(int filler = 12)
    {
        var tail = Enumerable.Range(0, filler).Select(i => $"    // unrelated member number {i}").ToArray();
        string[] head = ["class Orders", "{"];
        string[] left = [.. head, .. Block, .. tail, "}"];
        string[] right = [.. head, .. tail, .. Block, "}"];
        return (left, right);
    }

    [Fact]
    public void A_block_moved_within_the_file_is_flagged_on_both_sides()
    {
        var (l, r) = MovedPair();
        var c = TextComparison.Compare(l, r);
        var move = Assert.Single(c.Moves);
        Assert.Equal(Block.Length, move.LineCount);
        Assert.Equal(2, move.LeftLine);                         // where it was removed
        Assert.Equal(2 + 12, move.RightLine);                   // where it reappeared
        var flagged = c.Rows.Where(x => x.IsMoved).ToList();
        Assert.Equal(2 * Block.Length, flagged.Count);          // five rows on each side
        Assert.All(flagged, x => Assert.True(x.Kind is RowKind.LeftOnly or RowKind.RightOnly));
        Assert.Equal(2, c.DifferenceCount);                     // still two differences: copy and navigation are unchanged
    }

    [Fact]
    public void Moves_can_be_switched_off()
    {
        var (l, r) = MovedPair();
        var c = TextComparison.Compare(l, r, new DiffOptions { DetectMoves = false });
        Assert.Empty(c.Moves);
        Assert.DoesNotContain(c.Rows, x => x.IsMoved);
    }

    [Fact]
    public void Plain_additions_and_deletions_are_not_moves()
    {
        var c = TextComparison.Compare(["a", "b", "c", "d", "e", "f", "g"], ["a", "b", "x", "y", "z", "q", "g"]);
        Assert.Empty(c.Moves);
    }

    [Fact]
    public void Short_or_trivial_runs_are_ignored()
    {
        // two lines moved: below the minimum
        var two = TextComparison.Compare(["alpha long line one", "beta long line two", "m1", "m2", "m3", "tail"], ["m1", "m2", "m3", "alpha long line one", "beta long line two", "tail"]);
        Assert.Empty(two.Moves);
        Assert.DoesNotContain(two.Rows, x => x.IsMoved);

        // three closing braces carry almost no text: not worth calling a move
        var braces = TextComparison.Compare(["a", "}", "}", "}", "b", "c", "d", "e", "f"], ["a", "b", "c", "d", "e", "f", "}", "}", "}"]);
        Assert.Empty(braces.Moves);
    }

    [Fact]
    public void A_moved_block_with_one_edited_line_still_matches_the_unchanged_runs()
    {
        var (l, r) = MovedPair();
        var edited = r.ToArray();
        var i = Array.IndexOf(edited, "        repository.Commit();");
        edited[i] = "        repository.CommitAsync();";
        var c = TextComparison.Compare(l, edited);
        Assert.True(c.Moves.Count >= 1);                         // the lines before the edit still form a run
        Assert.True(c.Moves.Sum(m => m.LineCount) >= 3);
    }

    [Fact]
    public void Two_different_blocks_get_two_ids_and_no_line_is_used_twice()
    {
        string[] a = ["A1 first block text here", "A2 first block text here", "A3 first block text here", "mid", "B1 second block text here", "B2 second block text here", "B3 second block text here", "end"];
        string[] b = ["mid", "B1 second block text here", "B2 second block text here", "B3 second block text here", "A1 first block text here", "A2 first block text here", "A3 first block text here", "end"];
        var c = TextComparison.Compare(a, b);
        var ids = c.Rows.Where(x => x.IsMoved).Select(x => x.Move).Distinct().ToList();
        Assert.True(ids.Count >= 1);
        Assert.All(c.Moves, m => Assert.Equal(m.LineCount, c.Rows.Count(x => x.Move == m.Id && x.Kind == RowKind.LeftOnly)));
        Assert.All(c.Moves, m => Assert.Equal(m.LineCount, c.Rows.Count(x => x.Move == m.Id && x.Kind == RowKind.RightOnly)));
    }

    [Fact]
    public void Whitespace_options_apply_to_move_matching()
    {
        var (l, r) = MovedPair();
        var reindented = r.Select(x => x.StartsWith("        repository") ? "      " + x.TrimStart() : x).ToArray();
        Assert.DoesNotContain(TextComparison.Compare(l, reindented).Moves, m => m.LineCount == Block.Length); // exact: the indentation differs
        var ignoring = TextComparison.Compare(l, reindented, new DiffOptions { Whitespace = WhitespaceMode.IgnoreAll });
        Assert.Contains(ignoring.Moves, m => m.LineCount == Block.Length);
    }

    [Fact]
    public void Large_files_with_moves_stay_fast()
    {
        var lines = Enumerable.Range(0, 20_000).Select(i => $"line number {i} with some words").ToArray();
        var moved = lines.Skip(1000).Take(200).ToArray();
        var right = lines.Take(1000).Concat(lines.Skip(1200)).Concat(moved).ToArray();
        var sw = Stopwatch.StartNew();
        var c = TextComparison.Compare(lines, right);
        sw.Stop();
        Assert.Contains(c.Moves, m => m.LineCount == 200);
        Assert.True(sw.ElapsedMilliseconds < 4000, $"took {sw.ElapsedMilliseconds} ms");
    }
}
