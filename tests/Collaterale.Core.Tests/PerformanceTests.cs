using System.Diagnostics;
using Collaterale.Core.Diff;
using Collaterale.Core.Languages;
using Collaterale.Core.Merge;
using Xunit.Abstractions;

namespace Collaterale.Core.Tests;

/// <summary>Guards against accidental quadratic behaviour. Bounds are generous (CI machines are slow); they catch order-of-magnitude regressions, not micro changes.</summary>
public class PerformanceTests(ITestOutputHelper output)
{
    private static string[] Program(int n) => [.. Enumerable.Range(0, n).Select(i => i % 5 == 0 ? $"    // step {i}" : $"    var value{i % 997} = Compute({i}, \"item-{i}\");")];

    private static string[] Edited(string[] src, int edits, int seed)
    {
        var rnd = new Random(seed);
        var list = src.ToList();
        for (var k = 0; k < edits; k++)
        {
            var at = rnd.Next(list.Count);
            switch (rnd.Next(3))
            {
                case 0: list[at] += " // edited"; break;
                case 1: list.Insert(at, $"    inserted({k});"); break;
                default: list.RemoveAt(at); break;
            }
        }

        return [.. list];
    }

    private T Time<T>(string what, Func<T> action, double limitSeconds)
    {
        var sw = Stopwatch.StartNew();
        var result = action();
        sw.Stop();
        output.WriteLine($"{what}: {sw.Elapsed.TotalMilliseconds:F0} ms");
        Assert.True(sw.Elapsed.TotalSeconds < limitSeconds, $"{what} took {sw.Elapsed.TotalSeconds:F1}s (limit {limitSeconds}s)");
        return result;
    }

    [Fact]
    public void Identical_large_files()
    {
        var a = Program(500_000);
        var c = Time("compare 500k identical lines", () => TextComparison.Compare(a, a), 5);
        Assert.True(c.AreEqual);
    }

    [Theory]
    [InlineData(DiffAlgorithm.Myers)]
    [InlineData(DiffAlgorithm.Patience)]
    [InlineData(DiffAlgorithm.Histogram)]
    public void Large_file_with_scattered_edits(DiffAlgorithm algorithm)
    {
        var a = Program(200_000);
        var b = Edited(a, 2000, 1);
        var c = Time($"compare 200k lines, 2000 edits, {algorithm}", () => TextComparison.Compare(a, b, new DiffOptions { Algorithm = algorithm }), 15);
        Assert.True(c.DifferenceCount > 1000);
    }

    [Fact]
    public void A_file_where_every_line_changed_is_not_the_slow_case()
    {
        var a = Program(200_000);
        var b = a.Select(l => l + " ").ToArray();                  // e.g. trailing whitespace added everywhere, or a full reformat
        var c = Time("compare 200k lines, every line changed", () => TextComparison.Compare(a, b), 3);
        Assert.Equal(1, c.DifferenceCount);
    }

    [Fact]
    public void Large_file_with_all_smart_rules_on()
    {
        var a = Program(200_000);
        var b = Edited(a, 2000, 2);
        var options = new DiffOptions { Comments = CommentSyntaxes.CSharp, IgnoreTimestamps = true, Whitespace = WhitespaceMode.IgnoreAmount, IgnoreCase = true };
        Time("compare 200k lines, comments+dates+whitespace+case", () => TextComparison.Compare(a, b, options), 20);
    }

    [Fact]
    public void Three_way_merge_of_large_files()
    {
        var baseLines = Program(200_000);
        var left = Edited(baseLines, 800, 3);
        var right = Edited(baseLines, 800, 4);
        var m = Time("3-way merge, 200k lines, 800 edits per side", () => ThreeWayMerge.Merge(baseLines, left, right), 15);
        Assert.True(m.Regions.Count > 100);
        Time("build merged text", () => m.Build((_, _) => Resolution.Left), 3);
    }

    [Fact]
    public void Heavily_repeated_lines_stay_fast()
    {
        var a = Enumerable.Range(0, 100_000).Select(i => i % 3 == 0 ? "}" : i % 3 == 1 ? "{" : "x").ToArray();
        var b = Edited(a, 500, 5);
        Time("compare 100k highly repetitive lines", () => TextComparison.Compare(a, b), 20);
    }

    [Fact]
    public void Structure_normalization_of_a_large_json()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 100_000).Select(i => $"{{\"id\":{i},\"name\":\"n{i}\",\"tags\":[\"a\",\"b\"]}}")) + "]";
        var lines = Time("normalize 100k JSON objects", () => StructuralNormalizer.Normalize(json, StructuredFormat.Json), 10);
        Assert.NotNull(lines);
    }
}
