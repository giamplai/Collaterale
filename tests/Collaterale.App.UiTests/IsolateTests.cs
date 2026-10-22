using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Collaterale.App.Controls;
using Collaterale.App.Views;
using Collaterale.App.ViewModels;
using Collaterale.Core.Diff;

namespace Collaterale.App.UiTests;

public class IsolateTests
{
    private static TextCompareViewModel Open(string[] left, string[] right)
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.DetectMoves = false;
        vm.OpenText("/work/a.txt", left, "/work/b.txt", right);
        return vm;
    }

    [AvaloniaFact]
    public void Isolating_a_line_on_one_side_leaves_it_alone_with_padding_opposite()
    {
        var vm = Open(["one", "dup", "two"], ["one", "two", "dup"]);
        vm.IsolateRows((1, 1), null);
        Assert.True(vm.HasIsolations);
        var row = vm.Comparison!.Rows.Single(r => r.Left == 1);
        Assert.Equal(-1, row.Right);
        Assert.Equal(RowKind.LeftOnly, row.Kind);
    }

    [AvaloniaFact]
    public void Selecting_both_sides_compares_them_only_with_each_other()
    {
        var vm = Open(["keep", "apple", "pear", "end"], ["keep", "x1", "x2", "end", "apple", "pear"]);
        vm.IsolateRows((4, 5), (1, 2));   // automatically apple/pear pair with the later ones on the right (rows 4-5)
        var rows = vm.Comparison!.Rows.Where(r => r.Left is 1 or 2).ToList();
        Assert.All(rows, r => Assert.True(r.Right is -1 or 1 or 2, string.Join(" | ", vm.Comparison!.Rows.Select(x => $"{x.Kind}:{x.Left}/{x.Right}"))));
    }

    [AvaloniaFact]
    public void Clearing_goes_back_to_the_automatic_alignment()
    {
        var vm = Open(["one", "dup", "two"], ["one", "two", "dup"]);
        var automatic = vm.Comparison!.Rows.ToList();
        vm.IsolateRows((1, 1), null);
        vm.ClearIsolationsCommand.Execute(null);
        Assert.False(vm.HasIsolations);
        Assert.Equal(automatic, vm.Comparison!.Rows);
    }

    [AvaloniaFact]
    public void Loading_other_text_drops_the_isolations()
    {
        var vm = Open(["one", "dup", "two"], ["one", "two", "dup"]);
        vm.IsolateRows((1, 1), null);
        vm.OpenText("/work/a.txt", ["one", "dup", "two", "three"], "/work/b.txt", ["one", "two", "dup"]);
        Assert.False(vm.HasIsolations);
    }

    [AvaloniaFact]
    public void Adding_lines_after_the_isolation_keeps_it()
    {
        var vm = Open(["one", "dup", "two"], ["one", "two", "dup"]);
        vm.IsolateRows((1, 1), null);
        vm.Left.Lines.Add("three");
        vm.Recompare();
        Assert.True(vm.HasIsolations);
    }

    [AvaloniaFact]
    public void Editing_the_isolated_line_itself_drops_it()
    {
        var vm = Open(["one", "dup", "two"], ["one", "two", "dup"]);
        vm.IsolateRows((1, 1), null);
        vm.Left.Lines[1] = "other";
        vm.Recompare();
        Assert.False(vm.HasIsolations);
    }

    [AvaloniaFact]
    public void The_command_uses_the_selection_the_view_reports()
    {
        var vm = Open(["one", "dup", "two"], ["one", "two", "dup"]);
        vm.SelectionRowsProvider = () => [(1, 1), null];
        vm.IsolateCommand.Execute(null);
        Assert.True(vm.HasIsolations);
        Assert.Contains("Isolated 1 left and 0 right line", vm.Status);
    }

    [AvaloniaFact]
    public async Task Selecting_in_the_real_panes_and_pressing_Isolate_realigns_them()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.DetectMoves = false;
        var left = new[] { "alpha", "beta", "gamma", "delta", "epsilon", "zeta" };
        var right = new[] { "alpha", "gamma", "delta", "beta", "epsilon", "zeta" };
        vm.OpenText("/work/a.txt", left, "/work/b.txt", right);
        var w = new MainWindow(shell, []);
        w.Show();
        await TextCompareTests.Settle();
        var panes = w.GetVisualDescendants().OfType<DiffPane>().ToList();

        // select "beta" in the left pane (row 1) and the same word in the right pane (row 3)
        var doc = panes[0].Document!;
        var line = doc.GetLineByNumber(2);
        panes[0].Select(line.Offset, line.Length);
        var rdoc = panes[1].Document!;
        var rline = rdoc.GetLineByNumber(vm.Comparison!.Rows.ToList().FindIndex(r => r.Right == 3) + 1);
        panes[1].Select(rline.Offset, rline.Length);
        Assert.NotNull(panes[0].SelectedRowRange());

        vm.IsolateCommand.Execute(null);
        await TextCompareTests.Settle();
        Assert.True(vm.HasIsolations);
        var beta = vm.Comparison!.Rows.Single(r => r.Left == 1);
        Assert.Equal(3, beta.Right);   // "beta" now faces "beta", and the rows between them are padding
        TextCompareTests.Shot(w, "text-compare-isolate.png");
    }
}
