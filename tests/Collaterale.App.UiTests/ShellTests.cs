using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Collaterale.App.ViewModels;
using Collaterale.App.Views;
using Collaterale.Core.Cli;

namespace Collaterale.App.UiTests;

public class ShellTests
{
    private static string File2(string content)
    {
        var p = Path.Combine(Path.GetTempPath(), "collaterale-shell-" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        File.WriteAllText(p, content);
        return p;
    }

    [AvaloniaFact]
    public async Task Home_screen_renders_with_tiles_and_recents()
    {
        var shell = TextCompareTests.NewShell();
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-home-" + Guid.NewGuid().ToString("N")[..8], "projects", "shop");
        Directory.CreateDirectory(dir);
        var (a, b) = (Path.Combine(dir, "Cart.cs"), Path.Combine(dir, "Cart.new.cs"));
        File.WriteAllText(a, "one\ntwo\n"); File.WriteAllText(b, "one\n2\n");
        shell.OpenTextCompare(a, b);                  // creates a recent entry
        shell.SelectedTab = shell.Tabs[0];
        var w = new MainWindow(shell, []);
        w.Show();
        await TextCompareTests.Settle();

        Assert.Equal(2, shell.Tabs.Count);
        Assert.Single(shell.Home.Recent);
        Assert.Equal("≋", shell.Tabs[1].Glyph);
        TextCompareTests.Shot(w, "home.png");
    }

    [AvaloniaFact]
    public void Tabs_open_select_and_close()
    {
        var shell = TextCompareTests.NewShell();
        Assert.Single(shell.Tabs);
        Assert.False(shell.Tabs[0].Closable);

        var vm = shell.NewTextCompare();
        Assert.Equal(2, shell.Tabs.Count);
        Assert.Same(shell.Tabs[1], shell.SelectedTab);
        Assert.Equal("Text compare", vm.Title);

        shell.CloseTabCommand.Execute(shell.Tabs[0]);   // home cannot be closed
        Assert.Equal(2, shell.Tabs.Count);
        shell.CloseTabCommand.Execute(shell.Tabs[1]);
        Assert.Single(shell.Tabs);
        Assert.Same(shell.Tabs[0], shell.SelectedTab);
    }

    [AvaloniaFact]
    public void Tab_title_follows_the_loaded_files()
    {
        var shell = TextCompareTests.NewShell();
        var (a, b) = (File2("x"), File2("y"));
        var vm = shell.OpenTextCompare(a, b);
        Assert.Equal($"{Path.GetFileName(a)}  ↔  {Path.GetFileName(b)}", vm.Title);
        Assert.Equal(vm.Title, shell.SelectedTab!.Content.Title);
    }

    [AvaloniaFact]
    public void Dropping_two_files_compares_them_and_other_counts_explain()
    {
        var shell = TextCompareTests.NewShell();
        var (a, b) = (File2("1"), File2("2"));
        shell.Drop([a, b]);
        Assert.Equal(2, shell.Tabs.Count);

        shell.Drop([a]);
        Assert.Contains("exactly two", shell.Message);
    }

    [AvaloniaFact]
    public void Launch_reports_errors_and_remembers_sessions()
    {
        var shell = TextCompareTests.NewShell();
        shell.Launch(new LaunchRequest(LaunchKind.Error, Message: "boom"));
        Assert.Equal("boom", shell.Message);

        var (a, b) = (File2("1"), File2("2"));
        shell.Launch(new LaunchRequest(LaunchKind.TextCompare, a, b));
        Assert.Null(shell.Message);
        Assert.Equal(a, shell.Settings.Recent[0].Left);
    }

    [AvaloniaFact]
    public void Theme_cycles_and_persists()
    {
        var path = Path.Combine(Path.GetTempPath(), "collaterale-theme-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        var shell = new MainWindowViewModel(new Collaterale.Core.Settings.SettingsStore(path));
        shell.CycleThemeCommand.Execute(null);
        shell.CycleThemeCommand.Execute(null);
        Assert.Equal("Dark", shell.Settings.Theme);
        Assert.Equal("Dark", new Collaterale.Core.Settings.SettingsStore(path).Load().Theme);
    }

    [AvaloniaFact]
    public void Opening_from_the_command_line_args_starts_a_comparison()
    {
        var shell = TextCompareTests.NewShell();
        var (a, b) = (File2("1"), File2("2"));
        var w = new MainWindow(shell, [a, b]);
        w.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, shell.Tabs.Count);
    }
}
