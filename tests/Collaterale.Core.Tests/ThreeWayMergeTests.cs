using System.Diagnostics;
using Collaterale.Core.Merge;

namespace Collaterale.Core.Tests;

public class ThreeWayMergeTests
{
    private static ThreeWayMerge M(string[] b, string[] l, string[] r) => ThreeWayMerge.Merge(b, l, r);

    [Fact]
    public void The_base_tells_an_addition_from_a_deletion()
    {
        // "x" is on the left only. Absent from the base it was added there; present in the base and gone from the right it was deleted there.
        var added = M(["a", "b"], ["a", "x", "b"], ["a", "b"]);
        Assert.Equal(["a", "x", "b"], added.Build());
        var deleted = M(["a", "x", "b"], ["a", "x", "b"], ["a", "b"]);
        Assert.Equal(["a", "b"], deleted.Build());
        Assert.False(added.HasConflicts || deleted.HasConflicts);
    }

    [Fact]
    public void The_same_line_changed_differently_is_a_conflict_but_the_same_change_is_not()
    {
        Assert.True(M(["a"], ["L"], ["R"]).HasConflicts);
        var same = M(["a"], ["Z"], ["Z"]);
        Assert.False(same.HasConflicts);
        Assert.Equal(["Z"], same.Build());
    }

    [Fact]
    public void Non_overlapping_changes_merge_cleanly()
    {
        var m = M(["a", "b", "c", "d", "e"], ["a", "B", "c", "d", "e"], ["a", "b", "c", "D", "e"]);
        Assert.False(m.HasConflicts);
        Assert.Equal(["a", "B", "c", "D", "e"], m.Build());
        Assert.Equal([MergeRegionKind.Stable, MergeRegionKind.LeftChanged, MergeRegionKind.Stable, MergeRegionKind.RightChanged, MergeRegionKind.Stable], m.Regions.Select(r => r.Kind));
    }

    [Fact]
    public void Same_change_on_both_sides_is_not_a_conflict()
    {
        var m = M(["a", "b", "c"], ["a", "X", "c"], ["a", "X", "c"]);
        Assert.False(m.HasConflicts);
        Assert.Contains(m.Regions, r => r.Kind == MergeRegionKind.BothSame);
        Assert.Equal(["a", "X", "c"], m.Build());
    }

    [Fact]
    public void Different_changes_to_the_same_line_conflict_and_render_markers()
    {
        var m = M(["a", "b", "c"], ["a", "LEFT", "c"], ["a", "RIGHT", "c"]);
        Assert.Equal(1, m.ConflictCount);
        Assert.Equal(["a", "<<<<<<< left", "LEFT", "=======", "RIGHT", ">>>>>>> right", "c"], m.Build());
    }

    [Fact]
    public void Conflicts_can_be_resolved_per_region()
    {
        var m = M(["a", "b", "c"], ["a", "LEFT", "c"], ["a", "RIGHT", "c"]);
        Assert.Equal(["a", "RIGHT", "c"], m.Build((_, r) => r.NeedsDecision ? Resolution.Right : null));
        Assert.Equal(["a", "LEFT", "RIGHT", "c"], m.Build((_, r) => r.NeedsDecision ? Resolution.LeftThenRight : null));
        Assert.Equal(["a", "b", "c"], m.Build((_, r) => r.NeedsDecision ? Resolution.Base : null));
    }

    [Fact]
    public void Delete_versus_edit_conflicts()
    {
        var m = M(["a", "b", "c"], ["a", "c"], ["a", "B2", "c"]);
        Assert.Equal(1, m.ConflictCount);
    }

    [Fact]
    public void Insertions_at_different_places_merge()
    {
        var m = M(["a", "b", "c"], ["a", "L", "b", "c"], ["a", "b", "c", "R"]);
        Assert.False(m.HasConflicts);
        Assert.Equal(["a", "L", "b", "c", "R"], m.Build());
    }

    [Fact]
    public void Insertions_at_the_same_place_conflict()
    {
        Assert.True(M(["a", "b"], ["a", "L", "b"], ["a", "R", "b"]).HasConflicts);
    }

    [Fact]
    public void Empty_base_with_two_different_files_is_one_conflict()
    {
        var m = M([], ["x"], ["y"]);
        Assert.Equal(1, m.ConflictCount);
    }

    [Fact]
    public void Identical_inputs_are_one_stable_region()
    {
        var m = M(["a", "b"], ["a", "b"], ["a", "b"]);
        Assert.False(m.HasConflicts);
        Assert.Equal(["a", "b"], m.Build());
    }

    [Fact]
    public void Ignoring_whitespace_lets_a_reindent_merge_with_a_real_edit()
    {
        var o = new Collaterale.Core.Diff.DiffOptions { Whitespace = Collaterale.Core.Diff.WhitespaceMode.IgnoreAll };
        var m = ThreeWayMerge.Merge(["if (x)", "  go();"], ["if (x)", "      go();"], ["if (x)", "  go(now);"], o);
        Assert.False(m.HasConflicts);
    }

    // ───────────── oracle: compare with `git merge-file` on random edits of unique lines ─────────────

    private static string? GitMergeFile(string[] baseL, string[] left, string[] right, out int exit)
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-mf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            string F(string name, string[] lines) { var p = Path.Combine(dir, name); File.WriteAllText(p, string.Concat(lines.Select(l => l + "\n"))); return p; }
            var (l, b, r) = (F("l", left), F("b", baseL), F("r", right));
            var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "merge-file", "-p", "-L", "left", "-L", "base", "-L", "right", l, b, r }) { psi.ArgumentList.Add(a); }
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            exit = p.ExitCode;
            return o.Replace("\r\n", "\n");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string[] Edit(Random rnd, string[] baseLines, ref int counter)
    {
        var lines = baseLines.ToList();
        for (var e = rnd.Next(0, 4); e > 0 && lines.Count > 0; e--)
        {
            var i = rnd.Next(lines.Count);
            switch (rnd.Next(3))
            {
                case 0: lines[i] = $"edit{counter++}"; break;
                case 1: lines.RemoveAt(i); break;
                default: lines.Insert(i, $"ins{counter++}"); break;
            }
        }

        return [.. lines];
    }

    [Fact]
    public void Agrees_with_git_merge_file_on_clean_merges_and_conflict_detection()
    {
        var rnd = new Random(2024);
        int agree = 0, mineCleanGitConflict = 0, mineConflictGitClean = 0, cleanChecked = 0;
        for (var n = 0; n < 600; n++)
        {
            var baseL = Enumerable.Range(0, rnd.Next(4, 22)).Select(i => $"base{i}").ToArray();
            var c = 1000;
            var left = Edit(rnd, baseL, ref c);
            var right = Edit(rnd, baseL, ref c);

            var mine = M(baseL, left, right);
            var git = GitMergeFile(baseL, left, right, out var exit);
            var gitClean = exit == 0;

            if (mine.HasConflicts == !gitClean)
            {
                agree++;
                if (gitClean)
                {
                    cleanChecked++;
                    Assert.Equal(git, string.Concat(mine.Build().Select(l => l + "\n")));
                }
            }
            else if (mine.HasConflicts) { mineConflictGitClean++; }
            else { mineCleanGitConflict++; }
        }

        // Clean results must be byte-identical when both agree; disagreement must be rare and one-directional (we are never *less* safe).
        Assert.True(cleanChecked > 100, $"too few clean merges compared: {cleanChecked}");
        Assert.Equal(0, mineCleanGitConflict);
        Assert.True(mineConflictGitClean <= 600 * 0.03, $"mine=conflict/git=clean in {mineConflictGitClean} of 600 (agree {agree})");
    }

    // ───────────── properties that must hold even with heavily repeated lines (braces, blanks) ─────────────

    private static string[] Noisy(Random rnd, int n) => [.. Enumerable.Range(0, n).Select(_ => rnd.Next(5) switch { 0 => "}", 1 => "", 2 => "{", _ => "stmt" + rnd.Next(3) })];

    private static string[] Mutate(Random rnd, string[] src)
    {
        var l = src.ToList();
        for (var e = rnd.Next(0, 5); e > 0; e--)
        {
            var at = l.Count == 0 ? 0 : rnd.Next(l.Count);
            switch (rnd.Next(3))
            {
                case 0 when l.Count > 0: l[at] = "mut" + rnd.Next(4); break;
                case 1 when l.Count > 0: l.RemoveAt(at); break;
                default: l.Insert(at, rnd.Next(2) == 0 ? "}" : "new" + rnd.Next(4)); break;
            }
        }

        return [.. l];
    }

    [Fact]
    public void When_one_side_did_not_change_the_other_side_wins_cleanly()
    {
        for (var seed = 0; seed < 1000; seed++)
        {
            var rnd = new Random(seed);
            var baseL = Noisy(rnd, rnd.Next(0, 30));
            var changed = Mutate(rnd, baseL);

            var a = M(baseL, baseL, changed);
            Assert.False(a.HasConflicts);
            Assert.Equal(changed, a.Build());

            var b = M(baseL, changed, baseL);
            Assert.False(b.HasConflicts);
            Assert.Equal(changed, b.Build());

            var both = M(baseL, changed, changed);          // identical edits on both sides are not a conflict
            Assert.False(both.HasConflicts);
            Assert.Equal(changed, both.Build());
        }
    }

    [Fact]
    public void Swapping_the_sides_swaps_the_outcome_consistently()
    {
        for (var seed = 0; seed < 1000; seed++)
        {
            var rnd = new Random(seed);
            var baseL = Noisy(rnd, rnd.Next(0, 30));
            var l = Mutate(rnd, baseL);
            var r = Mutate(rnd, baseL);

            var ab = M(baseL, l, r);
            var ba = M(baseL, r, l);
            Assert.Equal(ab.ConflictCount, ba.ConflictCount);
            if (!ab.HasConflicts)
            {
                Assert.Equal(ab.Build(), ba.Build());          // a clean merge does not depend on which side is "left"
            }
        }
    }

    [Fact]
    public void Regions_tile_every_version_exactly_once()
    {
        for (var seed = 0; seed < 1000; seed++)
        {
            var rnd = new Random(seed);
            var baseL = Noisy(rnd, rnd.Next(0, 30));
            var l = Mutate(rnd, baseL);
            var r = Mutate(rnd, baseL);
            var m = M(baseL, l, r);
            Assert.Equal(baseL.Length, m.Regions.Sum(x => x.BaseLength));
            Assert.Equal(l.Length, m.Regions.Sum(x => x.LeftLength));
            Assert.Equal(r.Length, m.Regions.Sum(x => x.RightLength));
            int bp = 0, lp = 0, rp = 0;
            foreach (var x in m.Regions)
            {
                Assert.Equal((bp, lp, rp), (x.BaseStart, x.LeftStart, x.RightStart));
                bp += x.BaseLength; lp += x.LeftLength; rp += x.RightLength;
            }
        }
    }
}
