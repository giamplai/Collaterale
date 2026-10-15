using Avalonia.Headless.XUnit;
using Collaterale.App.ViewModels;
using Collaterale.Core.Cli;

namespace Collaterale.App.UiTests;

public class CommandLineLaunchTests
{
    private static (string L, string R) Pair()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-cll-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var (l, r) = (Path.Combine(dir, "l.txt"), Path.Combine(dir, "r.txt"));
        File.WriteAllText(l, "a\nb\n");
        File.WriteAllText(r, "a\nB\n");
        return (l, r);
    }

    [AvaloniaFact]
    public async Task Read_only_titles_and_save_target_switches_shape_the_text_compare()
    {
        var (l, r) = Pair();
        var target = l + ".out";
        var shell = TextCompareTests.NewShell();
        shell.Launch(LaunchParser.Parse(["/ro2", "/title1=Mine", "/title2=Theirs", $"/savetarget={target}", l, r]));
        var vm = Assert.IsType<TextCompareViewModel>(shell.SelectedTab!.Content);
        await TextCompareTests.Settle();

        Assert.True(vm.RightReadOnly);
        Assert.False(vm.LeftReadOnly);
        Assert.Equal("Mine", vm.LeftTitle);
        Assert.Equal("Theirs", vm.RightTitle);

        vm.CurrentGroup = 0;
        vm.CopyGroup(0, Side.Left);        // copying into the protected right side is refused
        Assert.Equal("B", vm.Right.Lines[1]);
        vm.CopyGroup(0, Side.Right);       // the other direction is allowed
        Assert.Equal("B", vm.Left.Lines[1]);

        vm.SaveLeftCommand.Execute(null);
        Assert.Equal("a\nB\n", File.ReadAllText(target).Replace("\r\n", "\n"));
        Assert.Equal("a\nb\n", File.ReadAllText(l).Replace("\r\n", "\n"));   // the opened file is untouched
    }

    [AvaloniaFact]
    public async Task Folders_open_with_the_name_filter_and_expanded_when_asked()
    {
        var root = Path.Combine(Path.GetTempPath(), "collaterale-cll-" + Guid.NewGuid().ToString("N")[..8]);
        var (l, r) = (Path.Combine(root, "L"), Path.Combine(root, "R"));
        Directory.CreateDirectory(Path.Combine(l, "sub")); Directory.CreateDirectory(Path.Combine(r, "sub"));
        File.WriteAllText(Path.Combine(l, "sub", "a.cs"), "1"); File.WriteAllText(Path.Combine(r, "sub", "a.cs"), "2");
        File.WriteAllText(Path.Combine(l, "sub", "b.md"), "1"); File.WriteAllText(Path.Combine(r, "sub", "b.md"), "2");
        var shell = TextCompareTests.NewShell();
        shell.Launch(LaunchParser.Parse(["/filters=*.cs", "/expandall", l, r]));
        var vm = Assert.IsType<FolderCompareViewModel>(shell.SelectedTab!.Content);
        await TextCompareTests.Settle();
        await Task.Delay(500);
        await TextCompareTests.Settle();

        Assert.Equal("*.cs", vm.Include);
        Assert.Contains(vm.Rows, x => x.Name == "a.cs");
        Assert.DoesNotContain(vm.Rows, x => x.Name == "b.md");
    }
}
