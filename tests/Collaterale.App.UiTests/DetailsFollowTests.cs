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
}
