using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Collaterale.App.ViewModels;
using Collaterale.App.Views;
using Collaterale.Core.Folders;

namespace Collaterale.App.UiTests;

public sealed class ProjectFixture : IDisposable
{
    private static readonly DateTime T = new(2025, 3, 10, 9, 30, 0, DateTimeKind.Utc);
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "collaterale-fold-" + Guid.NewGuid().ToString("N")[..8]);
    public string Left => Path.Combine(Root, "shop-v1");
    public string Right => Path.Combine(Root, "shop-v2");

    public ProjectFixture()
    {
        W(Left, "src/Cart.cs", "class Cart {}", T);                       W(Right, "src/Cart.cs", "class Cart { int Total; }", T.AddDays(3));
        W(Left, "src/Item.cs", "class Item {}", T);                       W(Right, "src/Item.cs", "class Item {}", T);
        W(Left, "src/Util/Helpers.cs", "static class H {}", T);
        W(Left, "src/Util/Legacy.cs", "// old", T);                       W(Right, "src/Util/Legacy.cs", "// old", T);
        W(Right, "docs/README.md", "# Shop", T);
        W(Left, "assets/logo.svg", "<svg/>", T);                          W(Right, "assets/logo.svg", "<svg/>", T);
        W(Left, "tests/CartTests.cs", "tests v1", T.AddDays(2));          W(Right, "tests/CartTests.cs", "t", T);
        W(Left, "bin/Debug/shop.dll", "binary", T);                       W(Right, "bin/Debug/shop.dll", "binary2", T);
        W(Left, ".git/HEAD", "ref", T);
    }

    private static void W(string root, string rel, string content, DateTime time)
    {
        var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        File.SetLastWriteTimeUtc(p, time);
    }

    public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } }
}

public class FolderCompareTests
{
    private static async Task<(MainWindowViewModel Shell, FolderCompareViewModel Vm, MainWindow Win)> Open(ProjectFixture f, bool excludeBin = true)
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewFolderCompare();
        if (excludeBin) { vm.ExcludeFolders = ".git;bin"; }
        await vm.OpenAsync(f.Left, f.Right);
        var w = new MainWindow(shell, []);
        w.Show();
        await TextCompareTests.Settle();
        return (shell, vm, w);
    }

    [AvaloniaFact]
    public async Task Tree_shows_states_counts_and_skips_excluded_folders()
    {
        using var f = new ProjectFixture();
        var (_, vm, w) = await Open(f, excludeBin: false);

        var names = vm.Rows.Select(r => r.Name).ToList();
        Assert.DoesNotContain(".git", names);                      // excluded by the default folder filter
        Assert.Contains("shop.dll", names);                        // bin is not excluded by default
        Assert.Contains("Cart.cs", names);
        Assert.Equal("≠ Different 3", vm.DifferentText);           // Cart.cs, CartTests.cs and shop.dll
        Assert.Equal("◀ Left only 1", vm.LeftOnlyText);
        Assert.Equal("▶ Right only 1", vm.RightOnlyText);
        Assert.Equal("= Same 3", vm.SameText);                     // Item.cs, Legacy.cs, logo.svg
        Assert.Equal(FolderEntryState.Different, vm.Rows.Single(r => r.Name == "Cart.cs").State);
        TextCompareTests.Shot(w, "folder-compare.png");
    }

    [AvaloniaFact]
    public async Task Filters_hide_rows_and_folders_with_nothing_visible()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        vm.ShowSame = false;
        Assert.DoesNotContain(vm.Rows, r => r.Name is "Item.cs" or "Legacy.cs" or "logo.svg");
        Assert.DoesNotContain(vm.Rows, r => r.Name == "assets");   // folder only held identical files
        vm.ShowDifferent = false;
        vm.ShowRightOnly = false;
        Assert.Equal(["src", "Util", "Helpers.cs"], vm.Rows.Select(r => r.Name));
    }

    [AvaloniaFact]
    public async Task Folders_with_differences_start_open_and_can_be_collapsed()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        var src = vm.Rows.Single(r => r.Name == "src");
        Assert.True(src.IsExpanded);
        var before = vm.Rows.Count;

        vm.Toggle(src);
        Assert.True(vm.Rows.Count < before);
        Assert.False(vm.Rows.Single(r => r.Name == "src").IsExpanded);

        vm.ExpandAllCommand.Execute(null);
        Assert.Contains(vm.Rows, r => r.Name == "Helpers.cs");
        vm.CollapseAllCommand.Execute(null);
        Assert.All(vm.Rows, r => Assert.Equal(0, r.Depth));
    }

    [AvaloniaFact]
    public async Task Copy_right_asks_first_and_cancelling_changes_nothing()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        var asked = new List<(string Title, int Lines)>();
        vm.Confirm = (t, m, lines) => { asked.Add((t, lines.Count)); return Task.FromResult(false); };

        vm.SetSelection([vm.Rows.Single(r => r.Name == "Helpers.cs")]);
        await vm.CopyToRightCommand.ExecuteAsync(null);
        Assert.Single(asked);
        Assert.False(File.Exists(Path.Combine(f.Right, "src", "Util", "Helpers.cs")));

        vm.Confirm = (_, _, _) => Task.FromResult(true);
        await vm.CopyToRightCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(f.Right, "src", "Util", "Helpers.cs")));
        Assert.Equal("= Same 4", vm.SameText);                     // the tree was re-compared
    }

    [AvaloniaFact]
    public async Task Delete_is_confirmed_and_removes_only_the_chosen_side()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        vm.Confirm = (_, m, _) => { Assert.Contains("cannot be recovered", m); return Task.FromResult(true); };
        vm.SetSelection([vm.Rows.Single(r => r.Name == "README.md")]);
        await vm.DeleteRightCommand.ExecuteAsync(null);
        Assert.False(File.Exists(Path.Combine(f.Right, "docs", "README.md")));
        Assert.True(File.Exists(Path.Combine(f.Left, "src", "Cart.cs")));
    }

    [AvaloniaFact]
    public async Task Sync_plan_is_previewed_and_runs_without_a_second_prompt()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        var prompts = 0;
        vm.Confirm = (_, _, _) => { prompts++; return Task.FromResult(true); };

        var plan = vm.PlanSync(SyncMode.TwoWay);
        Assert.Contains(plan.Steps, s => s.Action == SyncAction.CopyToRight && s.RelativePath == "tests/CartTests.cs"); // left newer
        Assert.Contains(plan.Steps, s => s.Action == SyncAction.CopyToLeft && s.RelativePath == "src/Cart.cs");        // right newer
        Assert.Equal(0, plan.Deletes);

        await vm.ExecuteAsync(plan, "Synchronize", askConfirmation: false);
        Assert.Equal(0, prompts);
        Assert.True(vm.Comparison!.Summary.AreEqual);
        Assert.Contains("Done", vm.Status);
    }

    [AvaloniaFact]
    public async Task Activating_a_differing_file_opens_a_text_compare_tab_and_folders_toggle()
    {
        using var f = new ProjectFixture();
        var (shell, vm, _) = await Open(f);
        var tabsBefore = shell.Tabs.Count;

        vm.Activate(vm.Rows.Single(r => r.Name == "Cart.cs"));
        Assert.Equal(tabsBefore + 1, shell.Tabs.Count);
        var text = Assert.IsType<TextCompareViewModel>(shell.SelectedTab!.Content);
        Assert.True(text.HasDifferences);

        vm.Activate(vm.Rows.Single(r => r.Name == "tests"));      // a folder: toggles instead
        Assert.Equal(tabsBefore + 1, shell.Tabs.Count);
    }

    [AvaloniaFact]
    public async Task Missing_folder_reports_an_error_and_launching_two_folders_works_from_the_shell()
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewFolderCompare();
        vm.LeftPath = "/definitely/not/here";
        vm.RightPath = Path.GetTempPath();
        await vm.CompareAsync();
        Assert.Contains("left folder does not exist", vm.Error);

        using var f = new ProjectFixture();
        shell.Launch(new Collaterale.Core.Cli.LaunchRequest(Collaterale.Core.Cli.LaunchKind.FolderCompare, f.Left, f.Right));
        await Task.Delay(500);
        Assert.Contains(shell.Tabs, t => t.Content is FolderCompareViewModel { HasComparison: true });
    }

    [AvaloniaFact]
    public void Sizes_and_dates_are_human_readable()
    {
        var dec = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        Assert.Equal("512 B", Format.Size(512));
        Assert.Equal($"1{dec}5 KB", Format.Size(1536));            // numbers follow the user's culture
        Assert.Equal("120 MB", Format.Size(120L * 1024 * 1024));
        Assert.Equal("", Format.Size(null));
        Assert.Equal("", Format.Date(null, false));
    }

    [AvaloniaFact]
    public async Task Sync_dialog_previews_each_mode()
    {
        using var f = new ProjectFixture();
        var (_, vm, w) = await Open(f);
        var dlg = new SyncWindow(vm);
        dlg.Show();
        await TextCompareTests.Settle();
        dlg.FindControl<ComboBox>("ModeBox")!.SelectedItem = SyncMode.MirrorToRight;
        await TextCompareTests.Settle();
        Assert.Contains("delete", dlg.FindControl<TextBlock>("Summary")!.Text, StringComparison.OrdinalIgnoreCase);
        TextCompareTests.Shot(dlg, "sync-dialog.png");
        dlg.Close();
    }
}
