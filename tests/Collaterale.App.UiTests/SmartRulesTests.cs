using Avalonia.Headless.XUnit;
using Collaterale.App.ViewModels;
using Collaterale.App.Views;

namespace Collaterale.App.UiTests;

public class SmartRulesTests
{
    private const string Old = """
        // Cart pricing — v1
        public double Total()
        {
            double total = 0;   // running sum
            foreach (var item in items)
            {
                total += item.Price;   /* before tax */
            }
            /*
               TODO: discounts
            */
            return total * 1.20;   // 20% tax
        }
        // generated 2024-03-01 08:15:30
        """;

    private const string New = """
        // Cart pricing — v2, reviewed by Anna
        public double Total()
        {
            double total = 0;
            foreach (var item in items)
            {
                total += item.Price * item.Quantity;   /* before tax */
            }
            // discounts are handled upstream now
            return total * 1.22;   // 22% tax
        }
        // generated 2025-11-20 17:42:09
        """;

    private static string[] Lines(string s) => s.ReplaceLineEndings("\n").Split('\n');

    [AvaloniaFact]
    public async Task Ignoring_comments_leaves_only_the_code_changes()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.OpenText("/work/Cart.cs", Lines(Old), "/work/Cart.v2.cs", Lines(New));
        var w = new MainWindow(shell, []);
        w.Show();
        await TextCompareTests.Settle();
        var all = vm.Comparison!.DifferenceCount;

        vm.IgnoreComments = true;
        await TextCompareTests.Settle();
        Assert.Null(vm.Error);
        Assert.Equal(2, vm.Comparison!.DifferenceCount);        // the price line and the tax line; everything else was a comment
        Assert.True(vm.Comparison.DifferenceCount < all);

        vm.IgnoreTimestamps = true;
        await TextCompareTests.Settle();
        Assert.Equal(2, vm.Comparison!.DifferenceCount);         // the "generated" line is a comment here anyway

        TextCompareTests.Shot(w, "text-compare-rules.png");
    }

    [AvaloniaFact]
    public async Task An_unknown_language_is_reported_and_choosing_one_fixes_it()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.OpenText("/work/notes.xyz", ["a // 1"], "/work/notes2.xyz", ["a // 2"]);
        vm.IgnoreComments = true;
        await TextCompareTests.Settle();
        Assert.Contains("language", vm.Error);
        Assert.True(vm.Comparison!.DifferenceCount == 1);

        vm.Language = TextCompareViewModel.Languages.Single(l => l.Syntax?.Id == "c-like");
        await TextCompareTests.Settle();
        Assert.Null(vm.Error);
        Assert.True(vm.Comparison!.AreEqual);
    }

    [AvaloniaFact]
    public async Task Dates_can_be_ignored_in_plain_text()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.OpenText("/work/a.log", ["start 2024-01-01 10:00:00", "ok"], "/work/b.log", ["start 2025-06-06 22:30:15", "ok"]);
        Assert.Equal(1, vm.Comparison!.DifferenceCount);
        vm.IgnoreTimestamps = true;
        await TextCompareTests.Settle();
        Assert.True(vm.Comparison!.AreEqual);
    }

    [AvaloniaFact]
    public async Task Structure_compare_opens_a_formatted_tab_without_formatting_noise()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.OpenText("/work/a.json", ["{\"name\":\"collaterale\",\"tags\":[\"a\",\"b\"],\"v\":1}"], "/work/b.json", ["{", "  \"v\": 2,", "  \"tags\": [\"a\", \"b\"],", "  \"name\": \"collaterale\"", "}"]);
        var tabs = shell.Tabs.Count;
        Assert.True(vm.Comparison!.DifferenceCount >= 1);

        vm.CompareStructureCommand.Execute(null);
        await TextCompareTests.Settle();
        Assert.Equal(tabs + 1, shell.Tabs.Count);
        var formatted = Assert.IsType<TextCompareViewModel>(shell.SelectedTab!.Content);
        Assert.Equal("a.json (structure)", formatted.Title);
        Assert.Equal(1, formatted.Comparison!.DifferenceCount);          // only "v" changed
    }

    [AvaloniaFact]
    public void Structure_compare_refuses_other_files_and_invalid_json()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.OpenText("/work/a.txt", ["x"], "/work/b.txt", ["y"]);
        vm.CompareStructureCommand.Execute(null);
        Assert.Contains("JSON and XML", vm.Error);

        vm.OpenText("/work/a.json", ["{ broken"], "/work/b.json", ["{}"]);
        vm.CompareStructureCommand.Execute(null);
        Assert.Contains("not valid JSON", vm.Error);
    }

    [AvaloniaFact]
    public async Task Blank_lines_can_be_ignored_from_the_rules_panel()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.OpenText("/work/a.txt", ["one", "", "two", "three"], "/work/b.txt", ["one", "two", "", "", "three"]);
        Assert.False(vm.Comparison!.AreEqual);
        vm.IgnoreBlankLines = true;
        await TextCompareTests.Settle();
        Assert.True(vm.Comparison!.AreEqual);
        Assert.Equal("No differences", vm.DifferenceText);
        Assert.Equal(vm.LeftText.Split('\n').Length, vm.RightText.Split('\n').Length);   // still aligned row for row
    }
}
