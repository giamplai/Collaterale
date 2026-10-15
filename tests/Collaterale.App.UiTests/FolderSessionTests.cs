using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Collaterale.App.ViewModels;
using Collaterale.App.Views;
using Collaterale.Core.Folders;
using Collaterale.Core.Settings;

namespace Collaterale.App.UiTests;

public class FolderSessionTests
{
    private static async Task<(MainWindowViewModel Shell, FolderCompareViewModel Vm, MainWindow Win)> Open(ProjectFixture f, Action<FolderCompareViewModel>? tweak = null)
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewFolderCompare();
        vm.ExcludeFolders = ".git;bin";
        tweak?.Invoke(vm);
        await vm.OpenAsync(f.Left, f.Right);
        var w = new MainWindow(shell, []);
        w.Show();
        await TextCompareTests.Settle();
        return (shell, vm, w);
    }

    [AvaloniaFact]
    public async Task Each_pane_shows_its_own_side_and_leaves_an_empty_slot_for_a_missing_one()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        var helpers = vm.Rows.Single(r => r.Name == "Helpers.cs");    // only on the left
        var item = vm.Rows.Single(r => r.Name == "Item.cs");          // on both sides

        Assert.Equal("Helpers.cs", helpers.LeftName);
        Assert.Equal("", helpers.RightName);
        Assert.True(helpers.RightMissing);
        Assert.False(helpers.LeftMissing);
        Assert.Equal("Item.cs", item.LeftName);
        Assert.Equal("Item.cs", item.RightName);
    }

    [AvaloniaFact]
    public async Task Rows_use_the_color_scheme_older_gray_newer_red_orphan_purple()
    {
        using var f = new ProjectFixture();
        var (_, vm, w) = await Open(f);
        var cart = vm.Rows.Single(r => r.Name == "Cart.cs");          // right is newer
        var helpers = vm.Rows.Single(r => r.Name == "Helpers.cs");    // only on the left
        var item = vm.Rows.Single(r => r.Name == "Item.cs");          // same

        Assert.NotNull(cart.NameBrush);
        Assert.NotEqual(cart.LeftBrush, cart.RightBrush);              // older and newer side are colored differently
        Assert.Equal(cart.NameBrush, cart.RightBrush);                 // the newer side carries the "newer" color
        Assert.NotNull(helpers.NameBrush);
        Assert.NotEqual(helpers.NameBrush, cart.NameBrush);            // orphan ≠ newer
        Assert.NotEqual(item.NameBrush, cart.NameBrush);               // same: the normal text color, not a cue color
        Assert.NotEqual(item.NameBrush, helpers.NameBrush);
        Assert.Equal(Avalonia.Media.Brushes.Transparent, item.SpotBrush);
        Assert.NotEqual(Avalonia.Media.Brushes.Transparent, cart.SpotBrush);
        Assert.NotNull(vm.Rows.First(r => r.Node.IsDirectory && r.Node.ContainsDifferent).IconBrush);
        TextCompareTests.Shot(w, "folder-compare.png");
    }

    [AvaloniaFact]
    public async Task Custom_colors_replace_the_defaults()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        vm.SetColors(new FolderColors { Newer = "#00FF00", Orphan = "#0000FF" });
        vm.Rebuild();
        var cart = vm.Rows.Single(r => r.Name == "Cart.cs");
        Assert.Equal(Avalonia.Media.Color.FromRgb(0, 255, 0), ((Avalonia.Media.SolidColorBrush)cart.NameBrush!).Color);
        Assert.Equal(Avalonia.Media.Color.FromRgb(0, 0, 255), ((Avalonia.Media.SolidColorBrush)vm.Rows.Single(r => r.Name == "Helpers.cs").NameBrush!).Color);
    }

    [AvaloniaFact]
    public async Task Sorting_by_a_column_keeps_folders_first_and_flips_on_a_second_click()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        vm.ExpandAllCommand.Execute(null);
        vm.SortByCommand.Execute(FolderSortColumn.RightSize);
        var src = vm.Rows.SkipWhile(r => r.Name != "src").Skip(1).TakeWhile(r => r.Depth == 1).Where(r => !r.Node.IsDirectory).Select(r => r.Name).ToList();
        Assert.Equal(src.OrderBy(n => vm.Rows.Single(r => r.Name == n && r.Depth == 1).Node.Right?.Size ?? -1), src);

        vm.SortByCommand.Execute(FolderSortColumn.RightSize);
        Assert.True(vm.SortDescending);
        var rootNames = vm.Rows.Where(r => r.Depth == 0).Select(r => r.Node.IsDirectory).ToList();
        Assert.Equal(rootNames.OrderByDescending(d => d), rootNames);          // folders still before files
    }

    [AvaloniaFact]
    public async Task The_log_records_the_comparison_and_free_space_is_shown()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        Assert.Contains(vm.Log, l => l.Text.StartsWith("Load comparison"));
        Assert.Contains(vm.Log, l => l.Text.Contains("completed in"));
        Assert.Contains("free on", vm.LeftFree);
    }

    [AvaloniaFact]
    public async Task Swap_exchanges_the_sides_and_compares_again()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        Assert.Contains(vm.Rows, r => r.Name == "Helpers.cs" && r.State == FolderEntryState.LeftOnly);
        await vm.SwapCommand.ExecuteAsync(null);
        Assert.Equal(f.Right, vm.LeftPath);
        Assert.Contains(vm.Rows, r => r.Name == "Helpers.cs" && r.State == FolderEntryState.RightOnly);
    }

    [AvaloniaFact]
    public async Task Expand_on_load_follows_the_chosen_mode()
    {
        using var f = new ProjectFixture();
        var (_, all, _) = await Open(f, v => v.ExpandOnLoad = ExpandMode.All);
        Assert.Contains(all.Rows, r => r.Name == "Legacy.cs");           // inside src/Util, an unchanged folder: only "all" opens it
        var (_, none, _) = await Open(f, v => v.ExpandOnLoad = ExpandMode.None);
        Assert.All(none.Rows, r => Assert.Equal(0, r.Depth));
        var (_, diff, _) = await Open(f);
        Assert.Contains(diff.Rows, r => r.Name == "Cart.cs");
    }

    [AvaloniaFact]
    public async Task Disabled_editing_refuses_copy_delete_and_sync_and_protects_opened_files()
    {
        using var f = new ProjectFixture();
        var (shell, vm, _) = await Open(f);
        vm.DisableEditing = true;
        vm.SetSelection(vm.Rows.Where(r => r.Name == "docs"));
        Assert.False(vm.CopyToLeftCommand.CanExecute(null));
        Assert.False(vm.DeleteRightCommand.CanExecute(null));

        var plan = vm.PlanSync(SyncMode.MirrorToRight);
        await vm.ExecuteAsync(plan, "Mirror", askConfirmation: false);
        Assert.Contains("disabled", vm.Error);
        Assert.True(File.Exists(Path.Combine(f.Right, "docs", "README.md")));       // nothing was touched

        vm.ExpandAllCommand.Execute(null);
        vm.Activate(vm.Rows.Single(r => r.Name == "Cart.cs"));
        var text = Assert.IsType<TextCompareViewModel>(shell.SelectedTab!.Content);
        Assert.True(text.EditingDisabled);
        Assert.False(text.CopyToRightCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Settings_dialog_values_round_trip_into_the_session_options()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        var s = FolderSettingsViewModel.From(vm);
        s.Criteria = CompareCriteria.QuickThenContent;
        s.ToleranceSeconds = 30;
        s.IgnoreTimeZone = true;
        s.AttrReadOnly = true;
        s.ContentMode = ContentMode.Rules;
        s.RuleIgnoreComments = true;
        s.AlignExtensions = true;
        s.Archives = ArchiveHandling.AsFoldersAlways;
        s.IncludeFolders = "src";
        s.NewFilterKind = OtherFilterKind.LargerThan; s.NewFilterNumber = "2048";
        s.AddFilterCommand.Execute(null);
        s.Description = "release check";
        s.DisableEditing = true;
        s.AutoRefreshSeconds = 0;

        var settings = new AppSettings();
        await s.ApplyAsync(vm, settings);
        var o = vm.Options;
        Assert.Equal(CompareCriteria.QuickThenContent, o.Criteria);
        Assert.Equal(TimeSpan.FromSeconds(30), o.TimeTolerance);
        Assert.True(o.IgnoreTimeZone && o.AlignDifferentExtensions);
        Assert.Equal(FileAttributes.ReadOnly, o.CompareAttributes);
        Assert.Equal((ContentMode.Rules, true), (o.ContentMode, o.Rules.IgnoreComments));
        Assert.Equal(ArchiveHandling.AsFoldersAlways, o.Archives);
        Assert.Equal("src", o.IncludeFolders);
        Assert.Equal(2048, Assert.Single(o.OtherFilters).Number);
        Assert.True(vm.DisableEditing);
        Assert.Equal("release check", vm.Description);
        Assert.Equal("release check", vm.Tooltip);
        Assert.DoesNotContain(vm.Rows, r => r.Name == "docs");                 // include-folders "src" took effect
    }

    [AvaloniaFact]
    public async Task Presets_can_be_saved_and_applied()
    {
        using var f = new ProjectFixture();
        var (shell, vm, _) = await Open(f);
        vm.Include = "*.cs";
        vm.SaveCurrentAsPreset();
        Assert.Contains(shell.Settings.FilterPresets, p => p.Name == "*.cs" && p.ExcludeFolders == ".git;bin");

        vm.Include = "";
        await vm.ApplyPresetAsync(shell.Settings.FilterPresets.Single());
        Assert.Equal("*.cs", vm.Include);
        Assert.DoesNotContain(vm.Rows, r => r.Name == "README.md");
    }

    [AvaloniaFact]
    public async Task Aligning_two_orphans_by_hand_compares_them_with_each_other()
    {
        using var f = new FolderFixture2();
        f.Write("L", "old-name.txt", "left content"); f.Write("R", "new-name.txt", "right content!");
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewFolderCompare();
        await vm.OpenAsync(f.Left, f.Right);
        Assert.Equal(2, vm.Rows.Count);

        vm.SetSelection(vm.Rows);
        await vm.AlignSelectedAsync();
        var row = Assert.Single(vm.Rows);
        Assert.Equal(FolderEntryState.Different, row.State);
        Assert.Contains("↔ new-name.txt", row.Name);

        await vm.ClearAlignmentsCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Rows.Count);
    }

    [AvaloniaFact]
    public async Task Aligning_items_that_do_not_fit_explains_why()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        vm.SetSelection(vm.Rows.Take(1));
        await vm.AlignSelectedAsync();
        Assert.Contains("Select one item", vm.Status);
    }

    [AvaloniaFact]
    public async Task Peek_shows_the_differences_of_a_text_pair_and_the_start_of_a_single_file()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        vm.ExpandAllCommand.Execute(null);
        vm.SetSelection(vm.Rows.Where(r => r.Name == "Cart.cs"));
        var text = vm.Peek();
        Assert.Contains("class Cart { int Total; }", text);
        Assert.Contains("-class Cart {}", text);

        vm.SetSelection(vm.Rows.Where(r => r.Name == "Helpers.cs"));
        Assert.Contains("static class H {}", vm.Peek());

        vm.SetSelection(vm.Rows.Where(r => r.Name == "src"));
        Assert.Contains("file(s)", vm.Peek());
    }

    [AvaloniaFact]
    public async Task Folder_rows_show_their_total_size_and_paths_can_be_copied()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        var src = vm.Rows.Single(r => r.Name == "src");
        Assert.NotEqual("", src.LeftSize);
        Assert.NotEqual("", src.RightSize);
        var docs = vm.Rows.Single(r => r.Name == "docs");           // only on the right
        Assert.Equal("", docs.LeftSize);
        Assert.Equal("6 B", docs.RightSize);

        vm.SetSelection(vm.Rows.Where(r => r.Name == "docs"));
        Assert.EndsWith("docs", vm.SelectedPaths().Replace((char)92, (char)47).Trim());
        Assert.Contains("docs", vm.SelectedPaths());
    }

    [AvaloniaFact]
    public async Task Hidden_columns_collapse_and_the_legend_explains_the_colors()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        vm.ShowSizes = false;
        Assert.Equal(0, vm.Columns.Size.Value);
        vm.ShowWhy = true;
        Assert.True(vm.Columns.Why.Value > 0);
        Assert.Contains(vm.Legend, l => l.Text.Contains("one side only"));
        Assert.Equal(7, vm.Legend.Count);
    }

    [AvaloniaFact]
    public async Task Settings_window_opens_on_every_tab_and_renders()
    {
        using var f = new ProjectFixture();
        var (_, vm, _) = await Open(f);
        var win = new FolderSettingsWindow(FolderSettingsViewModel.From(vm));
        win.Show();
        await TextCompareTests.Settle();
        var tabs = win.FindControl<TabControl>("Tabs")!;
        for (var i = 0; i < tabs.ItemCount; i++)
        {
            tabs.SelectedIndex = i;
            await TextCompareTests.Settle();
            if (i == 1) { TextCompareTests.Shot(win, "folder-settings-comparison.png"); }
            if (i == 3) { TextCompareTests.Shot(win, "folder-settings-filters.png"); }
        }

        Assert.Equal(7, tabs.ItemCount);
        win.Close();
    }
}

internal sealed class FolderFixture2 : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "collaterale-fs-" + Guid.NewGuid().ToString("N")[..8]);
    public string Left => Path.Combine(Root, "L");
    public string Right => Path.Combine(Root, "R");

    public FolderFixture2() { Directory.CreateDirectory(Left); Directory.CreateDirectory(Right); }

    public void Write(string side, string rel, string content)
    {
        var p = Path.Combine(side == "L" ? Left : Right, rel);
        File.WriteAllText(p, content);
        File.SetLastWriteTimeUtc(p, new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } }
}
