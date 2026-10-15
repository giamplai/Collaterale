using System.Diagnostics;
using System.Text;
using Collaterale.Core.Diff;

namespace Collaterale.Core.Tests;

public class UnifiedDiffTests
{
    private static string Join(IReadOnlyList<string> lines, bool finalNewline)
        => lines.Count == 0 ? "" : string.Join('\n', lines) + (finalNewline ? "\n" : "");

    private static (int Exit, string Err) Git(string dir, string? stdin, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardError = true, RedirectStandardOutput = true, RedirectStandardInput = stdin is not null };
        psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("core.autocrlf=false");
        foreach (var a in args) { psi.ArgumentList.Add(a); }
        using var p = Process.Start(psi)!;
        if (stdin is not null) { p.StandardInput.Write(stdin); p.StandardInput.Close(); }
        var err = p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, err);
    }

    /// <summary>Applies the patch to the left file with real git and returns the resulting bytes as text.</summary>
    private static string ApplyWithGit(string leftText, string patch)
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-ud-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "f.txt"), new UTF8Encoding(false).GetBytes(leftText));
            var (exit, err) = Git(dir, patch, "apply", "--unidiff-zero", "--whitespace=nowarn", "-");
            Assert.True(exit == 0, $"git apply failed: {err}\n--- patch ---\n{patch}");
            return new UTF8Encoding(false).GetString(File.ReadAllBytes(Path.Combine(dir, "f.txt")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Identical_inputs_produce_no_output()
        => Assert.Equal("", UnifiedDiff.Write("a/f", "b/f", ["x", "y"], ["x", "y"]));

    [Fact]
    public void Output_has_the_standard_shape()
    {
        var patch = UnifiedDiff.Write("a/f.txt", "b/f.txt", ["one", "two", "three"], ["one", "TWO", "three"], context: 1);
        Assert.Equal("--- a/f.txt\n+++ b/f.txt\n@@ -1,3 +1,3 @@\n one\n-two\n+TWO\n three\n", patch);
    }

    [Fact]
    public void Distant_changes_make_separate_hunks_and_near_ones_merge()
    {
        var left = Enumerable.Range(1, 30).Select(i => $"line{i}").ToList();
        var right = left.ToList();
        right[2] = "changed3"; right[27] = "changed28";
        Assert.Equal(2, UnifiedDiff.Write("a", "b", left, right, 3).Split('\n').Count(l => l.StartsWith("@@")));

        var near = left.ToList();
        near[2] = "x"; near[5] = "y";                      // gap of 2 lines: the context windows overlap
        Assert.Single(UnifiedDiff.Write("a", "b", left, near, 3).Split('\n'), l => l.StartsWith("@@"));
    }

    [Fact]
    public void Missing_final_newline_is_marked()
    {
        var patch = UnifiedDiff.Write("a", "b", ["x", "y"], ["x", "z"], 3, null, leftEndsWithNewline: false, rightEndsWithNewline: false);
        Assert.Contains("-y\n\\ No newline at end of file\n+z\n\\ No newline at end of file\n", patch);
    }

    [Fact]
    public void A_change_of_only_the_final_newline_is_reported()
    {
        var patch = UnifiedDiff.Write("a/f.txt", "b/f.txt", ["x", "y"], ["x", "y"], 3, null, leftEndsWithNewline: false, rightEndsWithNewline: true);
        Assert.NotEqual("", patch);
        Assert.Equal("x\ny\n", ApplyWithGit("x\ny", patch));
    }

    /// <summary>
    /// A strict reference implementation of "apply a unified diff": every hunk is placed exactly where its header says, and the
    /// header counts and new-side start must agree with the body. Unlike git it cannot be fooled by repeated lines, so it can
    /// also validate zero-context patches.
    /// </summary>
    private static (List<string> Lines, bool EndsWithNewline) ApplyStrict(List<string> left, bool leftNl, string patch)
    {
        var lines = patch.Split('\n');
        var output = new List<string>();
        var noEol = new List<bool>();    // per output line: does it lack a line ending? (only ever true for the last line)
        var p = 0;                       // next unread index in left
        var i = 0;
        while (i < lines.Length && !lines[i].StartsWith("@@", StringComparison.Ordinal)) { i++; }
        while (i < lines.Length && lines[i].StartsWith("@@", StringComparison.Ordinal))
        {
            var m = System.Text.RegularExpressions.Regex.Match(lines[i++], @"^@@ -(\d+),(\d+) \+(\d+),(\d+) @@$");
            Assert.True(m.Success, "malformed hunk header: " + lines[i - 1]);
            int oldStart = int.Parse(m.Groups[1].Value), oldCount = int.Parse(m.Groups[2].Value);
            int newStart = int.Parse(m.Groups[3].Value), newCount = int.Parse(m.Groups[4].Value);

            var copyUpTo = oldCount == 0 ? oldStart : oldStart - 1;     // zero-length old range: the start is the line *before* the hunk
            while (p < copyUpTo) { noEol.Add(p == left.Count - 1 && !leftNl); output.Add(left[p++]); }
            Assert.Equal(newCount == 0 ? output.Count : output.Count + 1, newStart);

            int seenOld = 0, seenNew = 0;
            char last = ' ';
            while (i < lines.Length && lines[i].Length > 0 && lines[i][0] != '@')
            {
                var l = lines[i++];
                if (l.StartsWith('\\'))
                {
                    if (last is '+' or ' ') { noEol[^1] = true; }          // the marker belongs to the line just before it
                    continue;
                }

                last = l[0];
                switch (l[0])
                {
                    case ' ': Assert.Equal(left[p], l[1..]); noEol.Add(false); output.Add(left[p++]); seenOld++; seenNew++; break;
                    case '-': Assert.Equal(left[p], l[1..]); p++; seenOld++; break;
                    case '+': noEol.Add(false); output.Add(l[1..]); seenNew++; break;
                    default: Assert.Fail("bad line in hunk: " + l); break;
                }
            }

            Assert.Equal((oldCount, newCount), (seenOld, seenNew));
        }

        while (p < left.Count) { noEol.Add(p == left.Count - 1 && !leftNl); output.Add(left[p++]); }
        return (output, output.Count == 0 || !noEol[^1]);
    }

    private static List<string> RandomLines(Random rnd, string[] pool) => Enumerable.Range(0, rnd.Next(0, 22)).Select(_ => pool[rnd.Next(pool.Length)]).ToList();

    private static List<string> Edit(Random rnd, List<string> l, string[] pool)
    {
        var r = l.ToList();
        for (var e = rnd.Next(0, 6); e > 0; e--)
        {
            switch (rnd.Next(3))
            {
                case 0 when r.Count > 0: r.RemoveAt(rnd.Next(r.Count)); break;
                case 1: r.Insert(rnd.Next(r.Count + 1), pool[rnd.Next(pool.Length)]); break;
                default: if (r.Count > 0) { r[rnd.Next(r.Count)] = pool[rnd.Next(pool.Length)]; } break;
            }
        }

        return r;
    }

    /// <summary>"…x", "" without a final newline is the same text as "…x" with one: a parsed file can never be in that state.</summary>
    private static bool Reachable(List<string> lines, bool endsWithNewline) => !(lines.Count > 0 && lines[^1] == "" && !endsWithNewline);

    private static readonly string[] Pool = ["alpha", "beta", "gamma", "", "delta", "  indented", "x = 1;", "x = 2;", "}", "{", "return x;"];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void A_strict_applier_reproduces_the_right_file_for_random_patches_at_every_context_size(int context)
    {
        var checkedCases = 0;
        for (var seed = 1; seed <= 12; seed++)
        {
            var rnd = new Random(seed * 7919 + context);
            for (var n = 0; n < 500; n++)
            {
                var l = RandomLines(rnd, Pool);
                var r = Edit(rnd, l, Pool);
                bool lNl = rnd.Next(4) != 0, rNl = rnd.Next(4) != 0;
                if (l.Count == 0) { lNl = true; }          // an empty file has no last line, so "ends with a newline" carries no meaning
                if (r.Count == 0) { rNl = true; }
                if (!Reachable(l, lNl) || !Reachable(r, rNl) || (l.Count == 0 && r.Count == 0)) { continue; }

                var patch = UnifiedDiff.Write("a/f.txt", "b/f.txt", l, r, context, null, lNl, rNl);
                if (l.SequenceEqual(r) && lNl == rNl) { Assert.Equal("", patch); continue; }

                var (lines, nl) = ApplyStrict(l, lNl, patch);
                Assert.True(r.SequenceEqual(lines) && rNl == nl, $"seed {seed} n {n}: patch did not reproduce the right file\n{patch}");
                checkedCases++;
            }
        }

        Assert.True(checkedCases > 2000, $"only {checkedCases} cases exercised");
    }

    [Fact]
    public void Real_git_applies_random_patches_with_context_and_reproduces_the_right_file_bytes()
    {
        var rnd = new Random(2025);
        for (var n = 0; n < 150; n++)
        {
            var l = RandomLines(rnd, Pool);
            var r = Edit(rnd, l, Pool);
            bool lNl = rnd.Next(4) != 0, rNl = rnd.Next(4) != 0;
            var ctx = new[] { 1, 3 }[rnd.Next(2)];       // with context git can tell repeated lines apart; zero-context patches are checked above
            if (!Reachable(l, lNl) || !Reachable(r, rNl)) { continue; }
            var leftText = Join(l, lNl);
            var rightText = Join(r, rNl);
            if (leftText == "" || rightText == "") { continue; }      // creating/emptying a file is a different patch form

            var patch = UnifiedDiff.Write("a/f.txt", "b/f.txt", l, r, ctx, null, lNl, rNl);
            if (leftText == rightText) { Assert.Equal("", patch); continue; }
            Assert.Equal(rightText, ApplyWithGit(leftText, patch));
        }
    }

    [Fact]
    public void Whitespace_options_decide_what_counts_as_a_change()
    {
        var left = new[] { "if (a)", "    run();" };
        var right = new[] { "if (a)", "  run();" };
        Assert.NotEqual("", UnifiedDiff.Write("a", "b", left, right));
        Assert.Equal("", UnifiedDiff.Write("a", "b", left, right, 3, new DiffOptions { Whitespace = WhitespaceMode.IgnoreAll }));
    }
}
