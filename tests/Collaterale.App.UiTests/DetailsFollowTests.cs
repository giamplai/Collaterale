using Avalonia.Headless.XUnit;
using Collaterale.App.ViewModels;

namespace Collaterale.App.UiTests;

public class DetailsFollowTests
{
    [AvaloniaFact]
    public void The_details_panel_shows_the_two_lines_that_face_each_other_whichever_pane_has_the_cursor()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.DetectMoves = false;
        // the right file has an extra line in the middle, so the rows below it face lines with different numbers
        vm.OpenText("/work/a.txt", ["one", "two", "three", "four"], "/work/b.txt", ["one", "two", "NEW", "three", "four"]);
        vm.Details = DetailsMode.Text;

        var row = vm.RightRows.ToList().FindIndex(r => r.LineNumber == 5);   // right line "four" (the last row)
        vm.CaretMoved(Side.Right, row);
        Assert.Equal("four", vm.RightDetailText);
        Assert.Equal("four", vm.LeftDetailText);                             // the left pane's cursor did not move, yet it shows what faces "four"
        Assert.Equal("Left · line 4", vm.LeftDetailLabel);
        Assert.Equal("Right · line 5", vm.RightDetailLabel);

        vm.CaretMoved(Side.Left, 0);
        Assert.Equal(("one", "one"), (vm.LeftDetailText, vm.RightDetailText));
    }

    [AvaloniaFact]
    public void A_side_with_only_padding_on_the_row_shows_no_line()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.DetectMoves = false;
        vm.OpenText("/work/a.txt", ["one", "two"], "/work/b.txt", ["one", "NEW", "two"]);
        var row = vm.RightRows.ToList().FindIndex(r => r.LineNumber == 2);   // the inserted line: padding on the left
        vm.CaretMoved(Side.Right, row);
        Assert.Equal("NEW", vm.RightDetailText);
        Assert.Equal("", vm.LeftDetailText);
        Assert.Equal("Left · (no line here)", vm.LeftDetailLabel);
    }

    [AvaloniaFact]
    public void The_details_panel_marks_the_words_that_differ_on_a_changed_row()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.OpenText("/work/a.txt", ["if ($cartid && $a !== $b)"], "/work/b.txt", ["if (!$cartid || $a !== $b)"]);
        vm.CaretMoved(Side.Left, 0);
        Assert.Equal(Collaterale.Core.Diff.RowKind.Changed, vm.DetailKind);
        Assert.NotEmpty(vm.LeftDetailWords);
        Assert.NotEmpty(vm.RightDetailWords);
    }

    [AvaloniaFact]
    public void Align_with_puts_the_chosen_line_opposite_the_clicked_one()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.DetectMoves = false;
        string[] left = ["a1", "a2", "x", "a3", "a4"], right = ["a1", "a2", "a3", "a4", "x"];
        vm.OpenText("/work/a.txt", left, "/work/b.txt", right);
        vm.SelectionRowsProvider = () => [(2, 2), null];            // the line "x" of the left pane
        vm.AlignWithCommand.Execute(null);
        Assert.True(vm.AlignPending);
        vm.CaretMoved(Side.Left, 2);                                 // the same pane: still waiting
        Assert.True(vm.AlignPending);
        var row = vm.RightRows.ToList().FindIndex(r => r.LineNumber == 5);
        vm.CaretMoved(Side.Right, row);                              // the right "x"
        Assert.False(vm.AlignPending);
        Assert.True(vm.HasIsolations);
        var facing = vm.Comparison!.Rows.First(r => r.Left == 2);
        Assert.Equal(4, facing.Right);
    }

    [AvaloniaFact]
    public void Align_with_can_be_cancelled()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.OpenText("/work/a.txt", ["a", "b"], "/work/b.txt", ["a", "c"]);
        vm.SelectionRowsProvider = () => [(0, 0), null];
        vm.AlignWithCommand.Execute(null);
        vm.CancelAlign();
        Assert.False(vm.AlignPending);
        vm.CaretMoved(Side.Right, 1);
        Assert.False(vm.HasIsolations);
    }

    [AvaloniaFact]
    public void Align_with_a_selected_stretch_puts_the_whole_stretch_opposite_the_clicked_line()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.DetectMoves = false;
        string[] left = ["a1", "x", "y", "a2", "a3"], right = ["a1", "a2", "a3", "x", "y"];
        vm.OpenText("/work/a.txt", left, "/work/b.txt", right);
        vm.SelectionRowsProvider = () => [(1, 2), null];            // "x", "y" of the left pane
        vm.AlignWithCommand.Execute(null);
        vm.CaretMoved(Side.Right, vm.RightRows.ToList().FindIndex(r => r.LineNumber == 4));   // the right "x"
        var rows = vm.Comparison!.Rows;
        Assert.Equal(3, rows.First(r => r.Left == 1).Right);         // x faces x
        Assert.Equal(4, rows.First(r => r.Left == 2).Right);         // y faces y
    }

    [AvaloniaFact]
    public void Clear_alignment_here_removes_only_the_alignment_holding_the_line()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.DetectMoves = false;
        string[] left = ["a1", "x", "a2", "a3", "y", "a4"], right = ["a1", "a2", "x", "a3", "a4", "y"];
        vm.OpenText("/work/a.txt", left, "/work/b.txt", right);
        vm.IsolateRows((1, 1), (2, 2));                              // x with x
        vm.IsolateRows((4, 4), (5, 5));                              // y with y
        Assert.True(vm.HasIsolations);
        vm.SelectionRowsProvider = () => [(vm.Comparison!.Rows.ToList().FindIndex(r => r.Left == 1), vm.Comparison.Rows.ToList().FindIndex(r => r.Left == 1)), null];
        vm.ClearAlignmentHereCommand.Execute(null);
        Assert.True(vm.HasIsolations);                               // the y alignment is still there
        vm.ClearAlignmentHereCommand.Execute(null);
        Assert.Equal("No manual alignment on this line", vm.Status);
    }

    [AvaloniaFact]
    public void Isolations_follow_an_edit_instead_of_disappearing()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.DetectMoves = false;
        string[] left = ["a1", "x", "a2", "a3", "y", "a4", "z1", "z2"], right = ["a1", "a2", "x", "a3", "a4", "y", "z1", "z2"];
        vm.OpenText("/work/a.txt", left, "/work/b.txt", right);
        int RowOf(string text, bool leftSide) => (leftSide ? vm.LeftRows : vm.RightRows).ToList().FindIndex(r => !r.IsFiller && (leftSide ? vm.Left.Lines : vm.Right.Lines)[r.LineNumber - 1] == text);
        vm.IsolateRows((RowOf("x", true), RowOf("x", true)), (RowOf("x", false), RowOf("x", false)));
        vm.IsolateRows((RowOf("y", true), RowOf("y", true)), (RowOf("y", false), RowOf("y", false)));
        // an edit above both, on the left: a line is inserted
        vm.Left.Lines.Insert(0, "new");
        vm.Recompare();
        Assert.True(vm.HasIsolations);
        var rows = vm.Comparison!.Rows;
        Assert.Equal(2, rows.First(r => r.Left == 2).Right);         // x (now line 2 on the left) still faces x
        Assert.Equal(5, rows.First(r => r.Left == 5).Right);         // and y still faces y
        // an edit that touches one of them drops only that one
        vm.Left.Lines[1] = "changed";
        vm.Recompare();
        Assert.True(vm.HasIsolations);
    }

    [AvaloniaFact]
    public void Selected_rows_can_be_copied_to_the_other_side()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.DetectMoves = false;
        vm.OpenText("/work/a.txt", ["one", "two", "three"], "/work/b.txt", ["one", "TWO", "three"]);
        vm.CopyRows(Side.Left, 1, 1);
        Assert.Equal(["one", "two", "three"], vm.Right.Lines);
        Assert.True(vm.Right.Dirty);
        // copying a line that has nothing facing it inserts it
        vm.OpenText("/work/a.txt", ["one", "extra", "three"], "/work/b.txt", ["one", "three"]);
        vm.CopyRows(Side.Left, 1, 1);
        Assert.Equal(["one", "extra", "three"], vm.Right.Lines);
    }
}
