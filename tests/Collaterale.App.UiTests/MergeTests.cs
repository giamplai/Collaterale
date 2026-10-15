using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Collaterale.App.Controls;
using Collaterale.App.ViewModels;
using Collaterale.App.Views;
using Collaterale.Core.Cli;
using Collaterale.Core.Merge;

namespace Collaterale.App.UiTests;

public class MergeTests
{
    // a small config file, edited on two branches
    private static readonly string[] Base =
    [
        "[server]", "host = localhost", "port = 8080", "timeout = 30", "",
        "[logging]", "level = info", "file = app.log", "",
        "[features]", "beta = false", "cache = true",
    ];

    private static string[] Left => [.. Base.Select(l => l switch
    {
        "port = 8080" => "port = 9090",          // conflicts with the right side
        "level = info" => "level = debug",        // only the left changed this
        _ => l,
    })];

    private static string[] Right => [.. Base.Select(l => l switch
    {
        "port = 8080" => "port = 8443",          // conflicts with the left side
        "beta = false" => "beta = true",          // only the right changed this
        _ => l,
    })];

    private static MergeViewModel NewMerge(string? output = null)
    {
        var vm = new MergeViewModel();
        vm.OpenText(Base, Left, Right, output);
        return vm;
    }

    [AvaloniaFact]
    public void Clean_changes_merge_automatically_and_the_conflict_shows_markers()
    {
        var vm = NewMerge();
        Assert.Equal(1, vm.ConflictCount);
        Assert.Equal(1, vm.UnresolvedCount);
        Assert.Equal(3, vm.ChangeCount);                                  // port (conflict), level (left), beta (right)
        var result = vm.ResultText.Split('\n');
        Assert.Contains("level = debug", result);                          // left-only change applied
        Assert.Contains("beta = true", result);                            // right-only change applied
        Assert.Contains(result, l => l.StartsWith("<<<<<<<"));
        Assert.Contains("port = 9090", result);
        Assert.Contains("port = 8443", result);
        Assert.Equal("1 of 1 conflicts unresolved", vm.ConflictText);
    }

    [AvaloniaFact]
    public void The_three_aligned_panes_always_have_the_same_number_of_rows()
    {
        var vm = NewMerge();
        var n = vm.LeftText.Split('\n').Length;
        Assert.Equal(n, vm.BaseText.Split('\n').Length);
        Assert.Equal(n, vm.RightText.Split('\n').Length);
        Assert.Equal(n, vm.LeftRows.Count);
    }

    [AvaloniaFact]
    public void Starts_on_the_first_unresolved_conflict_and_choices_resolve_it()
    {
        var vm = NewMerge();
        Assert.Equal(0, vm.CurrentChange);            // the port conflict is the first change in the file and the first unresolved conflict
        Assert.True(vm.Merge!.Regions[vm.CurrentRegion].Kind == MergeRegionKind.Conflict);

        vm.TakeRightCommand.Execute(null);
        Assert.Equal(0, vm.UnresolvedCount);
        Assert.Equal("All 1 conflicts resolved", vm.ConflictText);
        var result = vm.ResultText.Split('\n');
        Assert.Contains("port = 8443", result);
        Assert.DoesNotContain("port = 9090", result);
        Assert.DoesNotContain(result, l => l.StartsWith("<<<<<<<"));
    }

    [AvaloniaFact]
    public void Each_choice_produces_the_documented_lines()
    {
        foreach (var (command, expected) in new (Action<MergeViewModel>, string[])[]
        {
            (v => v.TakeLeftCommand.Execute(null), ["port = 9090"]),
            (v => v.TakeRightCommand.Execute(null), ["port = 8443"]),
            (v => v.TakeBothCommand.Execute(null), ["port = 9090", "port = 8443"]),
            (v => v.TakeBaseCommand.Execute(null), ["port = 8080"]),
            (v => v.TakeBothReversed(), ["port = 8443", "port = 9090"]),
        })
        {
            var vm = NewMerge();
            command(vm);
            var lines = vm.ResultText.Split('\n');
            var i = Array.FindIndex(lines, l => l.StartsWith("port"));
            Assert.Equal(expected, lines.Skip(i).Take(expected.Length));
        }
    }

    [AvaloniaFact]
    public void Non_conflicting_changes_can_be_overridden_too()
    {
        var vm = NewMerge();
        vm.NextChangeCommand.Execute(null);           // level (left-only)
        Assert.Equal(MergeRegionKind.LeftChanged, vm.Merge!.Regions[vm.CurrentRegion].Kind);
        vm.TakeBaseCommand.Execute(null);
        Assert.Contains("level = info", vm.ResultText.Split('\n'));   // the left edit is rejected
    }

    [AvaloniaFact]
    public void Navigation_wraps_and_next_conflict_skips_resolved_ones()
    {
        var vm = NewMerge();
        var n = vm.ChangeCount;
        var start = vm.CurrentChange;
        for (var i = 0; i < n; i++) { vm.NextChangeCommand.Execute(null); }
        Assert.Equal(start, vm.CurrentChange);               // a full lap wraps back to where it began
        vm.PreviousChangeCommand.Execute(null);
        Assert.Equal((start - 1 + n) % n, vm.CurrentChange);
        vm.NextChangeCommand.Execute(null);
        Assert.Equal(start, vm.CurrentChange);

        vm.NextConflictCommand.Execute(null);
        Assert.Equal(MergeRegionKind.Conflict, vm.Merge!.Regions[vm.CurrentRegion].Kind);
        vm.TakeLeftCommand.Execute(null);
        vm.NextConflictCommand.Execute(null);          // nothing unresolved left: stays put, no exception
        Assert.Equal(0, vm.UnresolvedCount);
    }

    [AvaloniaFact]
    public void Editing_the_result_by_hand_disables_resolving_until_reset()
    {
        var vm = NewMerge();
        vm.NotifyResultEdited("completely custom\ntext");
        Assert.True(vm.ResultEdited);
        Assert.False(vm.CanResolve);
        Assert.False(vm.TakeLeftCommand.CanExecute(null));

        vm.ResetResultCommand.Execute(null);
        Assert.False(vm.ResultEdited);
        Assert.True(vm.CanResolve);
        Assert.Contains(vm.ResultText.Split('\n'), l => l.StartsWith("<<<<<<<"));
    }

    [AvaloniaFact]
    public void Save_is_blocked_while_conflicts_remain_unless_markers_are_allowed()
    {
        var path = Path.Combine(Path.GetTempPath(), "collaterale-merge-" + Guid.NewGuid().ToString("N")[..8] + ".ini");
        var vm = NewMerge(path);

        Assert.False(vm.TrySave(allowMarkers: false));
        Assert.Contains("unresolved", vm.Error);
        Assert.False(File.Exists(path));

        Assert.True(vm.TrySave(allowMarkers: true));
        Assert.Contains("<<<<<<<", File.ReadAllText(path));
        Assert.False(vm.Completed);                       // markers saved: git must not consider this merged

        vm.TakeLeftCommand.Execute(null);
        Assert.True(vm.TrySave(allowMarkers: false));
        Assert.True(vm.Completed);
        Assert.DoesNotContain("<<<<<<<", File.ReadAllText(path));
        File.Delete(path);
    }

    [AvaloniaFact]
    public void Saved_result_keeps_the_line_endings_and_bom_of_the_input()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-mfile-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        byte[] Enc(string[] lines) => [.. new System.Text.UTF8Encoding(true).GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n")];
        File.WriteAllBytes(Path.Combine(dir, "base"), Enc(Base));
        File.WriteAllBytes(Path.Combine(dir, "left"), Enc(Left));
        File.WriteAllBytes(Path.Combine(dir, "right"), Enc(Right));

        var vm = new MergeViewModel();
        vm.Open(Path.Combine(dir, "base"), Path.Combine(dir, "left"), Path.Combine(dir, "right"), Path.Combine(dir, "out"));
        vm.TakeBothCommand.Execute(null);
        Assert.True(vm.TrySave(false));

        var bytes = File.ReadAllBytes(Path.Combine(dir, "out"));
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
        var text = System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains("port = 9090\r\nport = 8443\r\n", text);
        Directory.Delete(dir, true);
    }

    [AvaloniaFact]
    public void Resolve_all_picks_a_side_for_every_remaining_conflict()
    {
        var vm = NewMerge();
        vm.ResolveAllConflicts(MergeChoice.Left);
        Assert.Equal(0, vm.UnresolvedCount);
        Assert.Contains("port = 9090", vm.ResultText.Split('\n'));
    }

    [AvaloniaFact]
    public async Task Merge_view_renders_four_panes_with_the_current_conflict_outlined()
    {
        var root = Path.Combine(Path.GetTempPath(), "collaterale-mview-" + Guid.NewGuid().ToString("N")[..8]);
        foreach (var (dir, lines) in new[] { ("base", Base), ("ours", Left), ("theirs", Right) })
        {
            Directory.CreateDirectory(Path.Combine(root, dir));
            File.WriteAllLines(Path.Combine(root, dir, "app.ini"), lines);
        }

        var shell = TextCompareTests.NewShell();
        var vm = shell.OpenMerge(Path.Combine(root, "base", "app.ini"), Path.Combine(root, "ours", "app.ini"), Path.Combine(root, "theirs", "app.ini"), Path.Combine(root, "merged", "app.ini"));
        Assert.Equal("ours/app.ini", vm.LeftTitle);
        var w = new MainWindow(shell, []);
        w.Show();
        await TextCompareTests.Settle();

        var panes = w.GetVisualDescendants().OfType<DiffPane>().ToList();
        Assert.Equal(4, panes.Count);
        Assert.All(panes, p => Assert.Equal(vm.CurrentRegion, p.CurrentGroup));
        TextCompareTests.Shot(w, "merge.png");

        vm.TakeLeftCommand.Execute(null);
        await TextCompareTests.Settle();
        Assert.Contains("resolved", vm.ConflictText);
    }

    [AvaloniaFact]
    public void Rows_are_tinted_by_whose_change_they_carry()
    {
        var vm = NewMerge();
        var m = vm.Merge!;
        int Region(MergeRegionKind k) => m.Regions.Select((r, i) => (r, i)).First(t => t.r.Kind == k).i;
        var (conflict, left, right) = (Region(MergeRegionKind.Conflict), Region(MergeRegionKind.LeftChanged), Region(MergeRegionKind.RightChanged));

        Assert.All(vm.LeftRows.Where(r => r.Group == left), r => Assert.Equal(Collaterale.App.Controls.MergeTint.Left, r.Tint));
        Assert.All(vm.RightRows.Where(r => r.Group == left), r => Assert.Equal(Collaterale.App.Controls.MergeTint.None, r.Tint));
        Assert.All(vm.RightRows.Where(r => r.Group == right), r => Assert.Equal(Collaterale.App.Controls.MergeTint.Right, r.Tint));
        Assert.All(vm.LeftRows.Concat(vm.BaseRows).Concat(vm.RightRows).Where(r => r.Group == conflict), r => Assert.Equal(Collaterale.App.Controls.MergeTint.Conflict, r.Tint));
        Assert.All(vm.ResultRows.Where(r => r.Group == conflict), r => Assert.Equal(Collaterale.App.Controls.MergeTint.Conflict, r.Tint));

        vm.TakeRegion(conflict, MergeChoice.Right);                       // the buttons beside the result settle one section
        Assert.All(vm.ResultRows.Where(r => r.Group == conflict), r => Assert.Equal(Collaterale.App.Controls.MergeTint.Right, r.Tint));
        Assert.Equal(0, vm.UnresolvedCount);
    }

    [AvaloniaFact]
    public void A_filter_folds_the_other_sections_and_keeps_the_panes_aligned()
    {
        var vm = NewMerge();
        var all = vm.LeftRows.Count;
        vm.Filter = MergeFilter.Conflicts;
        Assert.True(vm.LeftRows.Count < all);
        Assert.Equal(vm.LeftRows.Count, vm.BaseRows.Count);
        Assert.Equal(vm.LeftRows.Count, vm.RightRows.Count);
        Assert.Contains(vm.LeftRows, r => r.FoldCount > 0);
        Assert.DoesNotContain(vm.LeftRows.Where(r => r.FoldCount == 0), r => r.Tint is Collaterale.App.Controls.MergeTint.Left or Collaterale.App.Controls.MergeTint.Right);

        var fold = vm.LeftRows.ToList().FindIndex(r => r.FoldCount > 0);
        var region = vm.LeftRows[fold].Group;
        vm.ExpandRegion(fold);                                              // clicking a marker shows that section again
        Assert.DoesNotContain(vm.LeftRows, r => r.FoldCount > 0 && r.Group == region);
        Assert.Contains(vm.LeftRows, r => r.FoldCount > 0);                 // the others stay folded
        vm.Filter = MergeFilter.All;
        Assert.Equal(all, vm.LeftRows.Count);
        Assert.DoesNotContain(vm.LeftRows, r => r.FoldCount > 0);
    }

    [AvaloniaFact]
    public void Save_can_target_the_left_or_the_right_file_and_describes_the_current_section()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-mt-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string F(string n, string[] lines) { var p = Path.Combine(dir, n); File.WriteAllLines(p, lines); return p; }
        var vm = new MergeViewModel();
        vm.Open(F("b", Base), F("l", Left), F("r", Right), null);
        Assert.True(vm.NeedsOutputPath);                                    // "Other" with no path yet
        vm.Target = MergeTarget.Left;
        Assert.False(vm.NeedsOutputPath);
        Assert.Equal(Path.Combine(dir, "l"), vm.TargetPath);

        Assert.Contains("Conflict", vm.CurrentDescription);
        Assert.Equal("1 conflict section(s)", vm.ConflictSections);
        vm.ResolveAllConflicts(MergeChoice.Right);
        Assert.True(vm.TrySave(false));
        Assert.Contains("port = 8443", File.ReadAllText(Path.Combine(dir, "l")));   // the left file now holds the merge
        Assert.Contains("port = 8080", File.ReadAllText(Path.Combine(dir, "b")));   // the base is untouched
        Directory.Delete(dir, true);
    }

    [AvaloniaFact]
    public void Command_line_merge_decides_the_exit_code()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-cli-merge-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "b"), Base);
        File.WriteAllLines(Path.Combine(dir, "l"), Left);
        File.WriteAllLines(Path.Combine(dir, "r"), Right);

        var shell = TextCompareTests.NewShell();
        Assert.Equal(0, shell.ExitCode);                                // no merge started: nothing to fail
        shell.Launch(new LaunchRequest(LaunchKind.Merge, Path.Combine(dir, "l"), Path.Combine(dir, "r"), Path.Combine(dir, "b"), Path.Combine(dir, "out")));
        Assert.Equal(1, shell.ExitCode);                                // started but not saved

        var vm = shell.CommandLineMerge!;
        vm.TakeLeftCommand.Execute(null);
        Assert.Equal(1, shell.ExitCode);                                // resolved but not saved yet
        vm.TrySave(false);
        Assert.Equal(0, shell.ExitCode);                                // saved cleanly: git may trust it
        Directory.Delete(dir, true);
    }
}
