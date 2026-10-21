using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Collaterale.App.Controls;
using Collaterale.App.Views;
using Collaterale.App.ViewModels;

namespace Collaterale.App.UiTests;

public class MergeConvenienceTests
{
    private static readonly string[] Base = ["[server]", "host = localhost", "port = 8080", "", "[logging]", "level = info", "", "[features]", "beta = false"];

    private static MergeViewModel Open()
    {
        var left = Base.Select(l => l switch { "port = 8080" => "port = 9090", "level = info" => "level = debug", _ => l }).ToArray();
        var right = Base.Select(l => l switch { "port = 8080" => "port = 8443", "beta = false" => "beta = true", _ => l }).ToArray();
        var vm = new MergeViewModel();
        vm.OpenText(Base, left, right);
        return vm;
    }

    [AvaloniaFact]
    public void Accepting_an_automatic_change_counts_it_and_moves_on()
    {
        var vm = Open();
        Assert.Equal(2, vm.AutomaticCount);                       // level (left) and beta (right); port is a conflict
        Assert.Equal("0 of 2 automatic changes accepted", vm.ReviewText);
        Assert.False(vm.AcceptSectionCommand.CanExecute(null));   // the cursor starts on the conflict, which cannot be "just accepted"

        vm.NextChangeCommand.Execute(null);                       // level: a left-only change
        Assert.True(vm.AcceptSectionCommand.CanExecute(null));
        vm.AcceptSectionCommand.Execute(null);
        Assert.Equal(1, vm.AcceptedCount);
        Assert.Equal("1 of 2 automatic changes accepted", vm.ReviewText);
        Assert.Contains("level = debug", vm.ResultText);          // the result is what the automatic merge gave
        Assert.Equal(1, vm.UnresolvedCount);                      // the conflict is untouched
    }

    [AvaloniaFact]
    public void Accept_all_automatic_leaves_conflicts_open()
    {
        var vm = Open();
        vm.AcceptAllAutomaticCommand.Execute(null);
        Assert.Equal(2, vm.AcceptedCount);
        Assert.Equal(1, vm.UnresolvedCount);
        Assert.False(vm.AcceptAllAutomaticCommand.CanExecute(null));
    }

    [AvaloniaTheory]
    [InlineData("left", "port = 9090")]
    [InlineData("right", "port = 8443")]
    [InlineData("center", "port = 8080")]
    public void Resolve_all_settles_every_open_conflict_one_way(string how, string expected)
    {
        var vm = Open();
        (how switch { "left" => vm.ResolveAllLeftCommand, "right" => vm.ResolveAllRightCommand, _ => vm.ResolveAllCenterCommand }).Execute(null);
        Assert.Equal(0, vm.UnresolvedCount);
        Assert.Contains(expected, vm.ResultText);
    }

    [AvaloniaFact]
    public void Resolve_all_both_keeps_left_then_right()
    {
        var vm = Open();
        vm.ResolveAllBothCommand.Execute(null);
        var lines = vm.ResultText.Split('\n').ToList();
        Assert.Equal(lines.IndexOf("port = 9090") + 1, lines.IndexOf("port = 8443"));
    }

    [AvaloniaFact]
    public void Undo_choice_returns_one_section_to_where_it_was()
    {
        var vm = Open();
        vm.TakeLeftCommand.Execute(null);                         // resolves the conflict on the cursor
        Assert.Equal(0, vm.UnresolvedCount);
        vm.NextChangeCommand.Execute(null);
        vm.AcceptSectionCommand.Execute(null);

        vm.CurrentChange = 0;                                     // back on the conflict, which now has a choice
        Assert.True(vm.ResetSectionCommand.CanExecute(null));
        vm.ResetSectionCommand.Execute(null);
        Assert.Equal(1, vm.UnresolvedCount);                      // the conflict is open again
        Assert.Equal(1, vm.AcceptedCount);                        // the accepted automatic change stays accepted
        Assert.False(vm.ResetSectionCommand.CanExecute(null));    // nothing left to undo on this section
    }

    [AvaloniaFact]
    public void Undoing_every_choice_restores_the_automatic_merge()
    {
        var vm = Open();
        var automatic = vm.ResultText;
        vm.ResolveAllRightCommand.Execute(null);
        vm.AcceptAllAutomaticCommand.Execute(null);
        Assert.NotEqual(automatic, vm.ResultText);
        vm.ResetChoicesCommand.Execute(null);
        Assert.Equal(automatic, vm.ResultText);
        Assert.Equal(1, vm.UnresolvedCount);
        Assert.Equal(0, vm.AcceptedCount);
    }

    [AvaloniaFact]
    public void The_details_panel_shows_the_current_line_of_each_version()
    {
        var vm = Open();
        var row = vm.LeftText.Split('\n').ToList().IndexOf("port = 9090");
        Assert.True(row >= 0);
        vm.TopCaretRow = row;
        Assert.Equal("port = 9090", vm.LeftDetailText);
        Assert.Equal("port = 8080", vm.BaseDetailText);
        Assert.Equal("port = 8443", vm.RightDetailText);
        Assert.Equal($"Left · line {row + 1}", vm.LeftDetailLabel);
        vm.ResultCaretRow = 0;
        Assert.Equal("[server]", vm.ResultDetailText);
        Assert.False(vm.ShowTextDetails);
        vm.Details = DetailsMode.Text;
        Assert.True(vm.ShowTextDetails);
    }

    [AvaloniaFact]
    public async Task The_panel_and_the_new_buttons_render_in_the_merge_window()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewMerge();
        var left = Base.Select(l => l switch { "port = 8080" => "port = 9090", "level = info" => "level = debug", _ => l }).ToArray();
        var right = Base.Select(l => l switch { "port = 8080" => "port = 8443", "beta = false" => "beta = true", _ => l }).ToArray();
        vm.OpenText(Base, left, right);
        vm.Details = DetailsMode.Text;
        var w = new MainWindow(shell, []);
        w.Show();
        await TextCompareTests.Settle();
        var panes = w.GetVisualDescendants().OfType<DiffPane>().ToList();
        vm.AcceptAllAutomaticCommand.Execute(null);
        panes[0].TextArea.Focus();
        panes[0].TextArea.Caret.Line = 3;
        await TextCompareTests.Settle();
        Assert.Contains("line 3", vm.LeftDetailLabel);
        TextCompareTests.Shot(w, "merge-details.png");
    }
}
