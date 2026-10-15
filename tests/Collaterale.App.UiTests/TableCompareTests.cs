using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Collaterale.App.Controls;
using Collaterale.App.ViewModels;
using Collaterale.App.Views;
using Collaterale.Core.Table;

namespace Collaterale.App.UiTests;

public class TableCompareTests
{
    private const string Old = """
        order_id,customer,product,qty,unit_price,status
        1001,Acme Corp,Widget,10,2.50,shipped
        1002,Globex,Gadget,3,19.99,shipped
        1003,Initech,Sprocket,25,0.80,pending
        1004,Umbrella,Widget,7,2.50,shipped
        1005,Hooli,Gizmo,1,149.00,cancelled
        1006,Stark Ind.,Widget,100,2.40,shipped
        """;

    private const string New = """
        order_id,customer,product,qty,unit_price,status
        1001,Acme Corp,Widget,10,2.50,shipped
        1002,Globex,Gadget,4,19.99,shipped
        1003,Initech,Sprocket,25,0.85,shipped
        1005,Hooli,Gizmo,1,149.00,cancelled
        1006,Stark Industries,Widget,100,2.40,shipped
        1007,Wayne Ent.,Gadget,2,19.99,pending
        """;

    private static (MainWindowViewModel Shell, TableCompareViewModel Vm, MainWindow Win) Make(Action<TableCompareViewModel>? configure = null)
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTableCompare();
        configure?.Invoke(vm);
        vm.OpenText("v1/orders.csv", Old.ReplaceLineEndings("\n"), "v2/orders.csv", New.ReplaceLineEndings("\n"));
        var w = new MainWindow(shell, []);
        w.Show();
        return (shell, vm, w);
    }

    [AvaloniaFact]
    public async Task Rows_matched_by_key_show_changed_cells_and_orphans_and_render()
    {
        var (_, vm, w) = Make(v => v.KeyColumns = "1");
        await TextCompareTests.Settle();

        var c = vm.Comparison!;
        Assert.Equal(["order_id", "customer", "product", "qty", "unit_price", "status"], c.ColumnNames);
        Assert.Equal(1, c.LeftOnlyRows);                       // order 1004 was removed
        Assert.Equal(1, c.RightOnlyRows);                      // order 1007 is new
        Assert.Equal(3, c.ChangedRows);                        // 1002 (qty), 1003 (price+status), 1006 (customer name)
        Assert.Equal(4, c.ChangedCells);
        Assert.Contains("3 changed rows", vm.SummaryText);
        TextCompareTests.Shot(w, "table-compare.png");
    }

    [AvaloniaFact]
    public async Task Without_a_key_rows_are_aligned_by_content()
    {
        var (_, vm, _) = Make();
        await TextCompareTests.Settle();
        Assert.True(vm.Comparison!.ChangedRows + vm.Comparison.LeftOnlyRows + vm.Comparison.RightOnlyRows >= 3);
    }

    [AvaloniaFact]
    public async Task Navigation_walks_the_differing_rows_and_wraps()
    {
        var (_, vm, _) = Make(v => v.KeyColumns = "order_id");   // header names work as keys too
        await TextCompareTests.Settle();
        var diffs = vm.Comparison!.Rows.Select((r, i) => (r, i)).Where(t => t.r.IsDifference).Select(t => t.i).ToList();
        Assert.Equal(diffs[0], vm.CurrentRow);
        vm.NextDifferenceCommand.Execute(null);
        Assert.Equal(diffs[1], vm.CurrentRow);
        for (var i = 1; i < diffs.Count; i++) { vm.NextDifferenceCommand.Execute(null); }
        Assert.Equal(diffs[0], vm.CurrentRow);                   // wrapped
        vm.PreviousDifferenceCommand.Execute(null);
        Assert.Equal(diffs[^1], vm.CurrentRow);
        Assert.StartsWith("Row difference", vm.DifferenceText);
    }

    [AvaloniaFact]
    public async Task Differences_only_hides_unchanged_rows()
    {
        var (_, vm, _) = Make(v => v.KeyColumns = "1");
        await TextCompareTests.Settle();
        var all = vm.VisibleRows.Count;
        vm.DifferencesOnly = true;
        Assert.True(vm.VisibleRows.Count < all);
        Assert.All(vm.VisibleRows, i => Assert.True(vm.Comparison!.Rows[i].IsDifference));
        vm.DifferencesOnly = false;
        Assert.Equal(all, vm.VisibleRows.Count);
    }

    [AvaloniaFact]
    public async Task Options_change_the_result()
    {
        var (_, vm, _) = Make(v => v.KeyColumns = "1");
        await TextCompareTests.Settle();
        Assert.Equal(3, vm.Comparison!.ChangedRows);

        vm.Tolerance = "0.1";                                    // 0.80 -> 0.85 is within tolerance now, the status change still differs
        Assert.Equal(3, vm.Comparison!.ChangedRows);
        Assert.Equal(3, vm.Comparison.ChangedCells);             // price cell no longer counted
        vm.IgnoreCase = true;
        vm.Trim = true;
        Assert.Equal(3, vm.Comparison!.ChangedRows);
    }

    [AvaloniaFact]
    public void Key_columns_accept_numbers_letters_and_header_names()
    {
        foreach (var key in new[] { "1", "A", "order_id", "ORDER_ID" })
        {
            var vm = new TableCompareViewModel { KeyColumns = key };
            vm.OpenText("a", Old, "b", New);
            Assert.Equal(1, vm.Comparison!.LeftOnlyRows);        // each spelling selects the same column
        }
    }

    [AvaloniaFact]
    public void Files_with_different_delimiters_are_each_detected()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-tbl-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.csv"), "id;name;price\n1;Ann;9,50\n2;Bob;3,20\n");
        File.WriteAllText(Path.Combine(dir, "b.tsv"), "id\tname\tprice\n1\tAnn\t9,50\n2\tBobby\t3,20\n");
        var shell = TextCompareTests.NewShell();
        var vm = shell.OpenTableCompare(Path.Combine(dir, "a.csv"), Path.Combine(dir, "b.tsv"));
        Assert.Equal(3, vm.Comparison!.ColumnCount);
        Assert.Equal(1, vm.Comparison.ChangedRows);               // Bob -> Bobby; each file was split with its own delimiter
        Assert.Equal("a.csv  ↔  b.tsv  (table)", vm.Title);

        vm.Delimiter = DelimiterChoice.Semicolon;                 // forcing one delimiter on both changes the result
        Assert.Equal(1, vm.Comparison!.RightColumn.Count(c => c is not null));   // the TSV file is now one wide column
        Directory.Delete(dir, true);
    }

    [AvaloniaFact]
    public async Task A_large_table_scrolls_and_stays_responsive()
    {
        var sb1 = new System.Text.StringBuilder("id,a,b\n");
        var sb2 = new System.Text.StringBuilder("id,a,b\n");
        for (var i = 0; i < 200_000; i++)
        {
            sb1.Append($"{i},x{i},{i}\n");
            sb2.Append($"{i},x{i},{(i == 150_000 ? -1 : i)}\n");
        }

        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTableCompare();
        vm.KeyColumns = "1";
        vm.OpenText("big1.csv", sb1.ToString(), "big2.csv", sb2.ToString());
        var w = new MainWindow(shell, []);
        w.Show();
        await TextCompareTests.Settle();

        var canvas = w.GetVisualDescendants().OfType<TableCanvas>().Single();
        Assert.Equal(200_000, vm.VisibleRows.Count);
        Assert.Equal(1, vm.Comparison!.ChangedRows);
        Assert.Equal(150_000, vm.CurrentRow);
        Assert.InRange(canvas.TopRow, 150_000 - canvas.ViewRows, 150_000);   // scrolled to the only difference
    }

    [AvaloniaFact]
    public void Launch_with_table_flag_opens_a_table_session()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-tblcli-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.csv"), "a,b\n1,2\n"); File.WriteAllText(Path.Combine(dir, "b.csv"), "a,b\n1,3\n");
        var shell = TextCompareTests.NewShell();
        shell.Launch(Collaterale.Core.Cli.LaunchParser.Parse(["--table", Path.Combine(dir, "a.csv"), Path.Combine(dir, "b.csv")]));
        Assert.IsType<TableCompareViewModel>(shell.SelectedTab!.Content);
        Directory.Delete(dir, true);
    }
}
