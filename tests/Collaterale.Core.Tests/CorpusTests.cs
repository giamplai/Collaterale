using Collaterale.Core.Cli;
using Collaterale.Core.Diff;
using Collaterale.Core.Text;

namespace Collaterale.Core.Tests;

/// <summary>
/// The behavior corpus: tricky comparison cases with a human-reviewed expected alignment. Any change to the diff engine that alters
/// an alignment shows up here as a failing case. To accept an intentional change, review the new rows and run the tests with
/// COLLATERALE_UPDATE_CORPUS=1.
/// </summary>
public class CorpusTests
{
    private static string CorpusDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "corpus", "cases")))
            {
                dir = dir.Parent;
            }

            return dir is null ? throw new DirectoryNotFoundException("tests/corpus/cases not found above " + AppContext.BaseDirectory) : Path.Combine(dir.FullName, "tests", "corpus", "cases");
        }
    }

    public static IEnumerable<object[]> Cases() => Directory.GetDirectories(CorpusDir).Select(d => new object[] { Path.GetFileName(d) }).OrderBy(x => (string)x[0]);

    private static string[] ArgsOf(string dir) => File.Exists(Path.Combine(dir, "args")) ? File.ReadAllText(Path.Combine(dir, "args")).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) : [];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Alignment_matches_the_reviewed_golden_file(string name)
    {
        var dir = Path.Combine(CorpusDir, name);
        var args = new[] { "--diff", "--rows" }.Concat(ArgsOf(dir)).Concat([Path.Combine(dir, "left.txt"), Path.Combine(dir, "right.txt")]).ToArray();
        var output = new StringWriter();
        CliDiff.Run(args, output, TextWriter.Null);
        var actual = output.ToString().Replace("\r\n", "\n");
        var golden = Path.Combine(dir, "expected.rows");

        if (Environment.GetEnvironmentVariable("COLLATERALE_UPDATE_CORPUS") == "1")
        {
            File.WriteAllText(golden, actual);
            return;
        }

        Assert.Equal(File.ReadAllText(golden).Replace("\r\n", "\n"), actual);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_line_of_both_files_appears_exactly_once_and_in_order(string name)
    {
        var dir = Path.Combine(CorpusDir, name);
        var l = TextFile.Load(Path.Combine(dir, "left.txt")).Lines;
        var r = TextFile.Load(Path.Combine(dir, "right.txt")).Lines;
        var options = new DiffOptions
        {
            Whitespace = ArgsOf(dir).Contains("-w") ? WhitespaceMode.IgnoreAll : ArgsOf(dir).Contains("-b") ? WhitespaceMode.IgnoreAmount : WhitespaceMode.Exact,
            IgnoreCase = ArgsOf(dir).Contains("-i"),
        };
        var c = TextComparison.Compare(l, r, options);
        Assert.Equal(Enumerable.Range(0, l.Count), c.Rows.Where(x => x.Left >= 0).Select(x => x.Left));
        Assert.Equal(Enumerable.Range(0, r.Count), c.Rows.Where(x => x.Right >= 0).Select(x => x.Right));
    }

    [Theory]
    [InlineData("06-whitespace-only")]
    [InlineData("07-case-only")]
    [InlineData("08-final-newline")]
    [InlineData("09-crlf-vs-lf")]
    [InlineData("15-tabs-vs-spaces")]
    public void Cases_that_are_equal_under_their_options_report_no_difference(string name)
    {
        var dir = Path.Combine(CorpusDir, name);
        var args = new[] { "--diff" }.Concat(ArgsOf(dir)).Concat([Path.Combine(dir, "left.txt"), Path.Combine(dir, "right.txt")]).ToArray();
        var output = new StringWriter();
        var code = CliDiff.Run(args, output, TextWriter.Null);
        // 08 differs only by the final newline, which the unified diff does report; the others must be silent
        Assert.Equal(name == "08-final-newline" ? CliDiff.Different : CliDiff.Same, code);
    }

    [Fact]
    public void Structural_expectations_from_the_human_notes_hold()
    {
        TextComparison Load(string name)
        {
            var dir = Path.Combine(CorpusDir, name);
            return TextComparison.Compare(TextFile.Load(Path.Combine(dir, "left.txt")).Lines, TextFile.Load(Path.Combine(dir, "right.txt")).Lines);
        }

        // 04: one single contiguous insertion, braces not interleaved
        var braces = Load("04-repeated-braces");
        Assert.Equal(1, braces.DifferenceCount);
        Assert.All(braces.Rows.Where(r => r.IsDifference), r => Assert.Equal(RowKind.RightOnly, r.Kind));

        // 05: the relocated block is recognised as a move
        Assert.NotEmpty(Load("05-moved-function").Moves);

        // 12: the new "validate the checksum" line is a pure insertion and the similar lines face each other
        var steps = Load("12-block-replace");
        Assert.Contains(steps.Rows, r => r.Kind == RowKind.RightOnly && steps.RightLines[r.Right].Contains("validate the checksum"));
        Assert.Contains(steps.Rows, r => r.Kind == RowKind.Changed && steps.LeftLines[r.Left].Contains("parse the body") && steps.RightLines[r.Right].Contains("parse the body"));

        // 13, 19, 20: exactly one insertion block
        foreach (var n in new[] { "13-duplicate-lines", "19-appended-log", "20-prepended-header" })
        {
            var c = Load(n);
            Assert.Equal(1, c.DifferenceCount);
            Assert.All(c.Rows.Where(r => r.IsDifference), r => Assert.Equal(RowKind.RightOnly, r.Kind));
        }

        // 09 and 08: line endings and the final newline never make lines differ
        Assert.True(Load("09-crlf-vs-lf").AreEqual);
        Assert.True(Load("08-final-newline").AreEqual);
    }
}
