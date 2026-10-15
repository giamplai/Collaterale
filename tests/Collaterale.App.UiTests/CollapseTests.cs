using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Collaterale.App.Controls;
using Collaterale.App.ViewModels;
using Collaterale.App.Views;
using Collaterale.Core.Diff;

namespace Collaterale.App.UiTests;

public class CollapseTests
{
    /// <summary>A 200-line settings file; the right version changes line 20 and line 150.</summary>
    private static (string[] Left, string[] Right) Files()
    {
        var left = Enumerable.Range(1, 200).Select(i => $"setting_{i:000} = value_{i:000}").ToArray();
        var right = left.ToArray();
        right[19] = "setting_020 = CHANGED";
        right[149] = "setting_150 = ALSO_CHANGED";
        return (left, right);
    }

    private static (TextCompareViewModel Vm, MainWindow Win) Open(string[]? l = null, string[]? r = null)
    {
        var (left, right) = Files();
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.OpenText("v1/settings.ini", l ?? left, "v2/settings.ini", r ?? right);
        var w = new MainWindow(shell, []);
        w.Show();
        return (vm, w);
    }

    [AvaloniaFact]
    public async Task Collapsing_keeps_three_lines_of_context_around_each_difference()
    {
        var (vm, w) = Open();
        await TextCompareTests.Settle();
        Assert.Equal(200, vm.LeftRows.Count);                         // not collapsed by default
        Assert.DoesNotContain(vm.LeftRows, x => x.FoldCount > 0);

        vm.CollapseUnchanged = true;
        await TextCompareTests.Settle();
        var folds = vm.LeftRows.Where(x => x.FoldCount > 0).ToList();
        Assert.Equal(3, folds.Count);                                 // before the first change, between the two, after the last
        Assert.Equal(200, vm.LeftRows.Count(x => x.FoldCount == 0) + folds.Sum(x => x.FoldCount));   // every line is either shown or counted in a fold
        // 3 lines of context on each side of both edits: (3+1+3) × 2 = 14 real rows + 3 markers
        Assert.Equal(14, vm.LeftRows.Count(x => x.FoldCount == 0));
        Assert.Equal(17, vm.LeftRows.Count);
        Assert.Equal(vm.LeftText.Split('\n').Length, vm.RightText.Split('\n').Length);
        Assert.Contains("unchanged lines", vm.LeftText);
        TextCompareTests.Shot(w, "text-compare-collapsed.png");
    }

    [AvaloniaFact]
    public async Task Groups_gutter_and_navigation_use_the_shortened_coordinates()
    {
        var (vm, w) = Open();
        vm.CollapseUnchanged = true;
        await TextCompareTests.Settle();

        Assert.Equal(2, vm.DisplayGroups.Count);
        foreach (var g in vm.DisplayGroups)
        {
            Assert.True(g.FirstRow < vm.LeftRows.Count);
            Assert.Equal(RowKind.Changed, vm.DisplayRows[g.FirstRow].Kind);            // the display row really is the changed row
            Assert.False(vm.LeftRows[g.FirstRow].IsFiller);
        }

        Assert.True(vm.DisplayGroups[1].FirstRow < 20);                              // line 150 now sits near the top of a short list
        Assert.Equal(vm.DisplayGroups[1].FirstRow, vm.RowOfGroup(1));

        var gutter = w.GetVisualDescendants().OfType<DiffGutter>().Single();
        Assert.Equal(vm.DisplayGroups, gutter.Groups);
        Assert.Equal(vm.DisplayRows, gutter.Rows);
    }

    [AvaloniaFact]
    public async Task Clicking_a_marker_expands_just_that_stretch()
    {
        var (vm, _) = Open();
        vm.CollapseUnchanged = true;
        await TextCompareTests.Settle();
        var markerRow = vm.LeftRows.ToList().FindIndex(x => x.FoldCount > 0 && x.FoldCount > 100);   // the long stretch between the two edits
        Assert.True(markerRow >= 0);

        vm.ExpandFold(markerRow);
        Assert.Equal(2, vm.LeftRows.Count(x => x.FoldCount > 0));                    // the other two markers stay folded
        Assert.True(vm.LeftRows.Count > 100);
        Assert.Equal(200, vm.LeftRows.Count(x => x.FoldCount == 0) + vm.LeftRows.Where(x => x.FoldCount > 0).Sum(x => x.FoldCount));
    }

    [AvaloniaFact]
    public async Task Turning_it_off_restores_the_full_view_and_editing()
    {
        var (vm, w) = Open();
        var panes = w.GetVisualDescendants().OfType<DiffPane>().ToList();
        vm.CollapseUnchanged = true;
        await TextCompareTests.Settle();
        Assert.All(panes.Take(2), p => Assert.True(p.IsReadOnly));                   // typing is not possible while lines are hidden

        vm.CollapseUnchanged = false;
        await TextCompareTests.Settle();
        Assert.Equal(200, vm.LeftRows.Count);
        Assert.All(panes.Take(2), p => Assert.False(p.IsReadOnly));
        Assert.DoesNotContain(vm.DisplayMap, m => m < 0);
    }

    [AvaloniaFact]
    public async Task Copying_a_difference_still_works_while_collapsed_and_the_collapse_is_kept()
    {
        var (vm, _) = Open();
        vm.CollapseUnchanged = true;
        await TextCompareTests.Settle();
        vm.CopyGroup(0, Side.Left);                                                  // undo the first edit on the right
        Assert.Equal("setting_020 = value_020", vm.Right.Lines[19]);
        Assert.Equal(1, vm.Comparison!.DifferenceCount);
        Assert.True(vm.CollapseUnchanged);
        Assert.Contains(vm.LeftRows, x => x.FoldCount > 0);
        Assert.Single(vm.DisplayGroups);
    }

    [AvaloniaFact]
    public async Task Hidden_lines_are_reported_as_not_displayed()
    {
        var (vm, _) = Open();
        vm.CollapseUnchanged = true;
        await TextCompareTests.Settle();
        Assert.Equal(-1, vm.DisplayRowOfLine(Side.Left, 100));                       // line 101 is inside a fold
        Assert.True(vm.DisplayRowOfLine(Side.Left, 19) >= 0);                        // the changed line itself is shown
        Assert.True(vm.DisplayRowOfLine(Side.Left, 17) >= 0);                        // and so is its context
        vm.CollapseUnchanged = false;
        Assert.Equal(100, vm.DisplayRowOfLine(Side.Left, 100));
    }

    [AvaloniaFact]
    public async Task Identical_files_and_short_gaps_are_not_collapsed()
    {
        var (left, _) = Files();
        var (vm, _) = Open(left, left);
        vm.CollapseUnchanged = true;
        await TextCompareTests.Settle();
        Assert.Equal(200, vm.LeftRows.Count);                                        // nothing to focus on: show the file

        var near = left.ToArray();
        near[10] = "x"; near[19] = "y";                                              // gap of 8 lines, minus 2×3 context = 2 hidden: below the fold minimum
        var (vm2, _) = Open(left, near);
        vm2.CollapseUnchanged = true;
        await TextCompareTests.Settle();
        var rows = vm2.LeftRows.ToList();
        var firstChange = rows.FindIndex(x => x.Kind == RowKind.Changed);
        var lastChange = rows.FindLastIndex(x => x.Kind == RowKind.Changed);
        Assert.DoesNotContain(rows.Skip(firstChange).Take(lastChange - firstChange + 1), x => x.FoldCount > 0);   // nothing folded between the two edits
        Assert.Equal(2, rows.Count(x => x.FoldCount > 0));                                                        // only the leading and trailing stretches
    }

    [AvaloniaFact]
    public async Task Marker_rows_render_in_both_themes_without_errors()
    {
        var shell = TextCompareTests.NewShell();
        shell.CycleThemeCommand.Execute(null); shell.CycleThemeCommand.Execute(null);
        var (left, right) = Files();
        var vm = shell.NewTextCompare();
        vm.OpenText("a", left, "b", right);
        vm.CollapseUnchanged = true;
        var w = new MainWindow(shell, []);
        w.Show();
        await TextCompareTests.Settle();
        Assert.NotNull(w.CaptureRenderedFrame());
    }
}

public class GoToLineTests
{
    [Theory]
    [InlineData("42", 100, 42)]
    [InlineData("  7 ", 100, 7)]
    [InlineData("1,234", 5000, 1234)]
    [InlineData("500", 100, 100)]        // beyond the end: clamped to the last line
    public void Valid_numbers_are_accepted_and_clamped(string text, int count, int expected)
    {
        Assert.True(GoToLineWindow.TryParse(text, count, out var line));
        Assert.Equal(expected, line);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    public void Invalid_input_is_rejected(string text) => Assert.False(GoToLineWindow.TryParse(text, 100, out _));

    private static (TextCompareViewModel Vm, MainWindow Win, DiffPane Left) Open()
    {
        var left = Enumerable.Range(1, 200).Select(i => $"row {i:000}").ToArray();
        var right = left.ToArray();
        right[19] = "row 020 CHANGED"; right[149] = "row 150 CHANGED";
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.OpenText("a.txt", left, "b.txt", right);
        var w = new MainWindow(shell, []);
        w.Show();
        return (vm, w, w.GetVisualDescendants().OfType<DiffPane>().First());
    }

    [AvaloniaFact]
    public async Task A_visible_line_is_found_in_a_plain_view()
    {
        var (vm, _, _) = Open();
        await TextCompareTests.Settle();
        Assert.Equal(99, vm.RevealLine(Side.Left, 99));
        Assert.Equal(-1, vm.RevealLine(Side.Left, 500));        // there is no such line
        Assert.Equal(-1, vm.RevealLine(Side.Left, -1));
    }

    [AvaloniaFact]
    public async Task A_line_hidden_in_a_collapsed_stretch_opens_that_stretch_only()
    {
        var (vm, _, _) = Open();
        vm.CollapseUnchanged = true;
        await TextCompareTests.Settle();
        Assert.Equal(-1, vm.DisplayRowOfLine(Side.Left, 99));     // line 100 is hidden

        var row = vm.RevealLine(Side.Left, 99);
        Assert.True(row >= 0);
        Assert.Equal("row 100", vm.LeftText.Split('\n')[row]);
        Assert.Equal(2, vm.LeftRows.Count(x => x.FoldCount > 0));  // the middle stretch opened; the leading and trailing ones did not
    }

    [AvaloniaFact]
    public async Task ShowLine_scrolls_and_puts_the_caret_on_the_line()
    {
        var (vm, w, left) = Open();
        await TextCompareTests.Settle();
        var view = w.GetVisualDescendants().OfType<TextCompareView>().Single();
        view.ShowLine(Side.Left, 120);
        await TextCompareTests.Settle();
        Assert.Equal(121, left.TextArea.Caret.Line);
        Assert.Equal("row 121", left.Document.GetText(left.Document.GetLineByNumber(left.TextArea.Caret.Line)));

        vm.CollapseUnchanged = true;                              // from a collapsed view the same request expands what it needs
        await TextCompareTests.Settle();
        view.ShowLine(Side.Right, 60);
        await TextCompareTests.Settle();
        var rightPane = w.GetVisualDescendants().OfType<DiffPane>().ToList()[1];
        Assert.Equal("row 061", rightPane.Document.GetText(rightPane.Document.GetLineByNumber(rightPane.TextArea.Caret.Line)));
    }
}
