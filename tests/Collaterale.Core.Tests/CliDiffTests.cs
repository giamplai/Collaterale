using Collaterale.Core.Cli;

namespace Collaterale.Core.Tests;

public class CliDiffTests
{
    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = CliDiff.Run(args, o, e);
        return (code, o.ToString().Replace("\r\n", "\n"), e.ToString().Replace("\r\n", "\n"));
    }

    private static string Tmp(string name, string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-cd-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void Identical_files_print_nothing_and_exit_zero()
    {
        var (a, b) = (Tmp("a.txt", "x\ny\n"), Tmp("b.txt", "x\ny\n"));
        Assert.Equal((0, "", ""), Run("--diff", a, b));
    }

    [Fact]
    public void Different_files_print_a_unified_diff_and_exit_one()
    {
        var (a, b) = (Tmp("a.txt", "one\ntwo\nthree\n"), Tmp("b.txt", "one\nTWO\nthree\n"));
        var (code, output, _) = Run("--diff", a, b, "-U", "1");
        Assert.Equal(1, code);
        Assert.Contains($"--- {a}\n+++ {b}\n@@ -1,3 +1,3 @@\n one\n-two\n+TWO\n three\n", output);
    }

    [Fact]
    public void Options_change_what_is_reported()
    {
        var (a, b) = (Tmp("a.txt", "if (x)\n    go();\n"), Tmp("b.txt", "IF (X)\n  go();\n"));
        Assert.Equal(1, Run("--diff", a, b).Exit);
        Assert.Equal(0, Run("--diff", a, b, "-w", "-i").Exit);
        Assert.Equal(1, Run("--diff", a, b, "-w").Exit);          // case still differs
    }

    [Fact]
    public void Brief_mode_only_says_the_files_differ()
    {
        var (a, b) = (Tmp("a.txt", "a\n"), Tmp("b.txt", "b\n"));
        var (code, output, _) = Run("--diff", a, b, "-q");
        Assert.Equal(1, code);
        Assert.Equal($"Files {a} and {b} differ\n", output);
    }

    [Fact]
    public void Algorithms_can_be_chosen_and_all_describe_the_same_change()
    {
        var (a, b) = (Tmp("a.txt", "a\nb\nc\n"), Tmp("b.txt", "a\nB\nc\n"));
        foreach (var flag in new[] { "--patience", "--histogram" })
        {
            Assert.Contains("-b\n+B", Run("--diff", a, b, flag).Out);
        }
    }

    [Fact]
    public void Binary_files_are_reported_not_dumped()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-cdb-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "a"), [1, 0, 2]);
        File.WriteAllBytes(Path.Combine(dir, "b"), [1, 0, 3]);
        File.WriteAllBytes(Path.Combine(dir, "c"), [1, 0, 2]);
        var (code, output, _) = Run("--diff", Path.Combine(dir, "a"), Path.Combine(dir, "b"));
        Assert.Equal(1, code);
        Assert.Contains("Binary files", output);
        Assert.Equal(0, Run("--diff", Path.Combine(dir, "a"), Path.Combine(dir, "c")).Exit);
    }

    [Fact]
    public void Folders_list_differences_like_diff_dash_r()
    {
        var root = Path.Combine(Path.GetTempPath(), "collaterale-cdf-" + Guid.NewGuid().ToString("N")[..8]);
        foreach (var d in new[] { "L", "R" }) { Directory.CreateDirectory(Path.Combine(root, d, "sub")); }
        File.WriteAllText(Path.Combine(root, "L", "same.txt"), "s"); File.WriteAllText(Path.Combine(root, "R", "same.txt"), "s");
        File.WriteAllText(Path.Combine(root, "L", "sub", "diff.txt"), "left side"); File.WriteAllText(Path.Combine(root, "R", "sub", "diff.txt"), "right");
        File.WriteAllText(Path.Combine(root, "L", "only-left.txt"), "x");
        Directory.CreateDirectory(Path.Combine(root, "R", "newdir")); File.WriteAllText(Path.Combine(root, "R", "newdir", "inside.txt"), "y");
        foreach (var f in new[] { "same.txt" }) { File.SetLastWriteTimeUtc(Path.Combine(root, "L", f), DateTime.UtcNow.AddDays(-3)); File.SetLastWriteTimeUtc(Path.Combine(root, "R", f), DateTime.UtcNow.AddDays(-3)); }

        var (code, output, _) = Run("--diff", Path.Combine(root, "L"), Path.Combine(root, "R"), "--content");
        Assert.Equal(1, code);
        Assert.Contains($"Only in {Path.Combine(root, "L")}: only-left.txt", output);
        Assert.Contains($"Only in {Path.Combine(root, "R")}: newdir", output);
        Assert.DoesNotContain("inside.txt", output);              // inside a one-sided folder: not listed separately
        Assert.Contains("diff.txt", output);
        Assert.DoesNotContain("same.txt", output);
    }

    [Fact]
    public void Errors_exit_two_with_a_message()
    {
        var a = Tmp("a.txt", "x");
        var (code, _, err) = Run("--diff", a, "/no/such/path");
        Assert.Equal(2, code);
        Assert.Contains("does not exist", err);
        Assert.Equal(2, Run("--diff", a).Exit);
        Assert.Equal(2, Run("--diff", a, a, "--bogus").Exit);
        Assert.Equal(2, Run("--diff", a, a, "-U", "x").Exit);
        Assert.Equal(2, Run("--diff", Path.GetTempPath(), a).Exit);          // folder vs file
    }

    [Fact]
    public void Help_is_available_and_the_diff_flag_is_detected()
    {
        Assert.Contains("Usage:", Run("--diff", "--help").Out);
        Assert.True(CliDiff.Wants(["--diff", "a", "b"]));
        Assert.False(CliDiff.Wants(["a", "b"]));
    }

    [Fact]
    public void Ignore_comments_uses_the_language_of_the_file_name()
    {
        var (a, b) = (Tmp("a.cs", "int x = 1; // old\n// gone\nint y;\n"), Tmp("b.cs", "int x = 1; // new\nint y;\n"));
        Assert.Equal(1, Run("--diff", a, b).Exit);
        Assert.Equal((0, "", ""), Run("--diff", "--ignore-comments", a, b));
    }

    [Fact]
    public void Ignore_comments_for_an_unknown_extension_warns_and_compares_normally()
    {
        var (a, b) = (Tmp("a.xyz", "x // 1\n"), Tmp("b.xyz", "x // 2\n"));
        var (code, _, err) = Run("--diff", "--ignore-comments", a, b);
        Assert.Equal(1, code);
        Assert.Contains("use --lang", err);
        Assert.Equal(0, Run("--diff", "--lang", "c-like", a, b).Exit);
    }

    [Fact]
    public void Unknown_language_is_an_error() => Assert.Equal(2, Run("--diff", "--lang", "klingon", "a", "b").Exit);

    [Fact]
    public void Ignore_timestamps_hides_date_only_changes()
    {
        var (a, b) = (Tmp("a.log", "built 2024-01-01 10:00:00 ok\n"), Tmp("b.log", "built 2025-05-05 23:59:59 ok\n"));
        Assert.Equal(1, Run("--diff", a, b).Exit);
        Assert.Equal(0, Run("--diff", "--ignore-timestamps", a, b).Exit);
    }

    [Fact]
    public void Structure_ignores_json_layout_and_key_order_but_not_values()
    {
        var a = Tmp("a.json", "{\"b\":1,\"a\":[1,2]}");
        var same = Tmp("b.json", "{\n \"a\": [1, 2],\n \"b\": 1\n}\n");
        var other = Tmp("c.json", "{\"a\":[1,3],\"b\":1}");
        Assert.Equal(1, Run("--diff", a, same).Exit);
        Assert.Equal(0, Run("--diff", "--structure", a, same).Exit);
        var (code, output, _) = Run("--diff", "--structure", a, other);
        Assert.Equal(1, code);
        Assert.Contains("-    2", output);
        Assert.Contains("+    3", output);
    }

    [Fact]
    public void Structure_on_a_text_file_falls_back_with_a_note()
    {
        var (a, b) = (Tmp("a.txt", "x\n"), Tmp("b.txt", "y\n"));
        var (code, _, err) = Run("--diff", "--structure", a, b);
        Assert.Equal(1, code);
        Assert.Contains("not a JSON or XML file", err);
    }
}
