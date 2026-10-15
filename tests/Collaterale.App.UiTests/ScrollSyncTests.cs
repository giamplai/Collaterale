using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia;
using Collaterale.App.Controls;
using Collaterale.App.Views;

namespace Collaterale.App.UiTests;

public class ScrollSyncTests
{
    private static string[] Lines(int n, string tag) => [.. Enumerable.Range(1, n).Select(i => $"line {i} {tag}")];

    [AvaloniaFact]
    public async Task Panes_scroll_together_and_the_overview_map_scrolls_them()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        var left = Lines(200, "a"); var right = Lines(200, "a"); right[100] = "changed";
        vm.OpenText("/work/a.txt", left, "/work/b.txt", right);
        var w = new MainWindow(shell, []);
        w.Show();
        await TextCompareTests.Settle();
        var panes = w.GetVisualDescendants().OfType<DiffPane>().ToList();
        var map = w.GetVisualDescendants().OfType<OverviewMap>().First();

        panes[0].ScrollToVerticalOffset(300);
        await TextCompareTests.Settle();
        Assert.True(panes[0].VerticalOffset > 100, $"left at {panes[0].VerticalOffset}");
        Assert.Equal(panes[0].VerticalOffset, panes[1].VerticalOffset, 1);

        panes[1].ScrollToVerticalOffset(600);
        await TextCompareTests.Settle();
        Assert.Equal(panes[1].VerticalOffset, panes[0].VerticalOffset, 1);

        var before = panes[0].VerticalOffset;
        var p = map.TranslatePoint(new Avalonia.Point(map.Bounds.Width / 2, map.Bounds.Height * 0.1), w)!.Value;
        w.MouseDown(p, Avalonia.Input.MouseButton.Left);
        w.MouseUp(p, Avalonia.Input.MouseButton.Left);
        await TextCompareTests.Settle();
        Assert.NotEqual(before, panes[0].VerticalOffset);
        Assert.Equal(panes[0].VerticalOffset, panes[1].VerticalOffset, 1);
    }
}
