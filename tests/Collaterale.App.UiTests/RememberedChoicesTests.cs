using Avalonia.Headless.XUnit;
using Collaterale.App.ViewModels;
using Collaterale.Core.Diff;
using Collaterale.Core.Settings;

namespace Collaterale.App.UiTests;

public class RememberedChoicesTests
{
    private static string TempSettings() => Path.Combine(Path.GetTempPath(), "collaterale-remember-" + Guid.NewGuid().ToString("N")[..8] + ".json");

    [AvaloniaFact]
    public void Text_compare_choices_come_back_in_the_next_session()
    {
        var path = TempSettings();
        var first = new MainWindowViewModel(new SettingsStore(path));
        var vm = first.NewTextCompare();
        vm.Whitespace = WhitespaceMode.IgnoreAll;
        vm.IgnoreCase = true;
        vm.Details = DetailsMode.Text;
        vm.WordWrap = true;
        vm.CollapseUnchanged = true;
        vm.DetectMoves = false;

        var second = new MainWindowViewModel(new SettingsStore(path));
        var again = second.NewTextCompare();
        Assert.Equal(WhitespaceMode.IgnoreAll, again.Whitespace);
        Assert.True(again.IgnoreCase);
        Assert.Equal(DetailsMode.Text, again.Details);
        Assert.True(again.WordWrap);
        Assert.True(again.CollapseUnchanged);
        Assert.False(again.DetectMoves);
    }

    [AvaloniaFact]
    public void Merge_shares_the_whitespace_decision_and_remembers_its_own_view()
    {
        var path = TempSettings();
        var first = new MainWindowViewModel(new SettingsStore(path));
        first.NewTextCompare().Whitespace = WhitespaceMode.IgnoreLeadingTrailing;

        var merge = first.NewMerge();
        Assert.Equal(WhitespaceMode.IgnoreLeadingTrailing, merge.Whitespace);   // decided in a text compare, offered in the merge
        merge.ShowBase = false;
        merge.Filter = MergeFilter.Conflicts;
        merge.IgnoreCase = true;

        var second = new MainWindowViewModel(new SettingsStore(path));
        var again = second.NewMerge();
        Assert.False(again.ShowBase);
        Assert.Equal(MergeFilter.Conflicts, again.Filter);
        Assert.True(again.IgnoreCase);
        Assert.Equal(WhitespaceMode.IgnoreLeadingTrailing, again.Whitespace);
        Assert.True(second.NewTextCompare().IgnoreCase);                          // and the other way round
    }

    [AvaloniaFact]
    public void Ignoring_strings_changes_the_comparison_and_is_remembered()
    {
        var path = TempSettings();
        var shell = new MainWindowViewModel(new SettingsStore(path));
        var vm = shell.NewTextCompare();
        vm.OpenText("/work/a.cs", ["var s = \"hello\"; // note", "Run();"], "/work/b.cs", ["var s = \"bonjour\"; // note", "Run();"]);
        Assert.True(vm.HasDifferences);
        vm.IgnoreStrings = true;
        Assert.False(vm.HasDifferences);                       // what the string says no longer counts
        vm.IgnoreStrings = false;
        vm.IgnoreComments = true;
        Assert.True(vm.HasDifferences);                        // comments alone do not hide a changed string
        vm.IgnoreStrings = true;

        var again = new MainWindowViewModel(new SettingsStore(path)).NewTextCompare();
        Assert.True(again.IgnoreStrings);
        Assert.True(again.IgnoreComments);
    }
}
