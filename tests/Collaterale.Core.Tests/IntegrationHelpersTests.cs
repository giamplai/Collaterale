using Collaterale.Core.Integration;
using Collaterale.Core.Settings;

namespace Collaterale.Core.Tests;

public class OpenWithTests
{
    private static OpenWithApp App(string args) => new() { Name = "Editor", Command = "code", Arguments = args };

    [Fact]
    public void The_file_placeholders_are_replaced_and_quoted_groups_stay_one_argument()
    {
        Assert.Equal(["-d", "/a b/x.txt", "/c/y.txt"], OpenWith.BuildArguments(App("-d \"%1\" \"%2\""), "/a b/x.txt", "/c/y.txt"));
        Assert.Equal(["/a b/x.txt"], OpenWith.BuildArguments(App("\"%1\""), "/a b/x.txt", null));
        Assert.Equal(["--goto", "/p/x.txt:1"], OpenWith.BuildArguments(App("--goto %1:1"), "/p/x.txt", null));
    }

    [Fact]
    public void A_second_file_placeholder_without_a_second_file_is_left_out()
    {
        Assert.Equal(["-d", "/x"], OpenWith.BuildArguments(App("-d \"%1\" \"%2\""), "/x", null));
    }

    [Fact]
    public void No_arguments_means_the_file_is_passed_alone()
    {
        Assert.Equal(["/x"], OpenWith.BuildArguments(App(""), "/x", null));
    }

    [Fact]
    public void Only_apps_with_a_name_and_a_command_are_usable()
    {
        Assert.True(OpenWith.IsUsable(App("")));
        Assert.False(OpenWith.IsUsable(new OpenWithApp { Name = "", Command = "x" }));
        Assert.False(OpenWith.IsUsable(new OpenWithApp { Name = "x", Command = " " }));
    }
}

public class AutoStartTests
{
    [Fact]
    public void Linux_gets_a_desktop_entry_in_the_autostart_folder()
    {
        var e = AutoStart.EntryFor(AutoStartPlatform.Linux, "/opt/collaterale/Collaterale", "/home/u")!;
        Assert.Equal("/home/u/.config/autostart/collaterale-clipboard.desktop", e.Path);
        Assert.Contains("[Desktop Entry]", e.Content);
        Assert.Contains("Exec=\"/opt/collaterale/Collaterale\" --clipboard", e.Content);
        Assert.Contains("Type=Application", e.Content);
    }

    [Fact]
    public void Mac_gets_a_launch_agent_that_runs_at_login()
    {
        var e = AutoStart.EntryFor(AutoStartPlatform.Mac, "/Applications/Collaterale.app/Contents/MacOS/Collaterale", "/Users/u")!;
        Assert.Equal("/Users/u/Library/LaunchAgents/collaterale.clipboard.plist", e.Path);
        Assert.Contains("<key>RunAtLoad</key>", e.Content);
        Assert.Contains("<string>--clipboard</string>", e.Content);
        Assert.Contains("<string>/Applications/Collaterale.app/Contents/MacOS/Collaterale</string>", e.Content);
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore };
        using var reader = System.Xml.XmlReader.Create(new StringReader(e.Content), settings);
        System.Xml.Linq.XDocument.Load(reader);                                          // well-formed
    }

    [Fact]
    public void Windows_uses_the_per_user_run_key_not_a_file()
    {
        Assert.Null(AutoStart.EntryFor(AutoStartPlatform.Windows, "C:\\Collaterale\\Collaterale.exe", "C:\\Users\\u"));
        Assert.Equal("\"C:\\Collaterale\\Collaterale.exe\" --clipboard", AutoStart.WindowsCommand("C:\\Collaterale\\Collaterale.exe"));
    }

    [Fact]
    public void Enabling_and_disabling_writes_and_removes_the_file()
    {
        var home = Path.Combine(Path.GetTempPath(), "collaterale-as-" + Guid.NewGuid().ToString("N")[..8]);
        AutoStart.Set(AutoStartPlatform.Linux, true, "/x/Collaterale", home);
        var path = Path.Combine(home, ".config", "autostart", "collaterale-clipboard.desktop");
        Assert.True(File.Exists(path));
        Assert.True(AutoStart.IsEnabled(AutoStartPlatform.Linux, home));
        AutoStart.Set(AutoStartPlatform.Linux, false, "/x/Collaterale", home);
        Assert.False(File.Exists(path));
        Assert.False(AutoStart.IsEnabled(AutoStartPlatform.Linux, home));
        Directory.Delete(home, true);
    }
}

public class PatchReaderTests
{
    private const string Patch = """
        diff --git a/src/a.txt b/src/a.txt
        index 111..222 100644
        --- a/src/a.txt
        +++ b/src/a.txt
        @@ -1,4 +1,4 @@
         one
        -two
        +TWO
         three
         four
        @@ -10,3 +10,4 @@
         ten
        +ten and a half
         eleven
         twelve
        --- /dev/null
        +++ b/new.txt
        @@ -0,0 +1,2 @@
        +hello
        +world
        """;

    [Fact]
    public void A_patch_is_split_into_files_with_their_names_and_hunks()
    {
        var files = PatchReader.Parse(Patch.Replace("\r\n", "\n"));
        Assert.Equal(2, files.Count);
        Assert.Equal(("src/a.txt", "src/a.txt"), (files[0].OldName, files[0].NewName));
        Assert.Equal(2, files[0].Hunks.Count);
        Assert.Equal((1, 10), (files[0].Hunks[0].OldStart, files[0].Hunks[1].OldStart));
        Assert.Equal(("/dev/null", "new.txt"), (files[1].OldName, files[1].NewName));
        Assert.True(files[1].IsNew);
    }

    [Fact]
    public void Each_file_becomes_the_text_before_and_after_the_change()
    {
        var files = PatchReader.Parse(Patch.Replace("\r\n", "\n"));
        var (before, after) = files[0].Sides();
        Assert.Equal(["one", "two", "three", "four", "ten", "eleven", "twelve"], before);
        Assert.Equal(["one", "TWO", "three", "four", "ten", "ten and a half", "eleven", "twelve"], after);
        var (b2, a2) = files[1].Sides();
        Assert.Empty(b2);
        Assert.Equal(["hello", "world"], a2);
    }

    [Fact]
    public void Text_that_is_not_a_patch_gives_no_files()
    {
        Assert.Empty(PatchReader.Parse("just some words\nnothing here\n"));
        Assert.Empty(PatchReader.Parse(""));
    }

    [Fact]
    public void A_no_newline_marker_is_not_a_line_and_git_prefixes_are_removed_from_names()
    {
        var p = "--- a/x\n+++ b/x\n@@ -1 +1 @@\n-old\n\\ No newline at end of file\n+new\n\\ No newline at end of file\n";
        var f = PatchReader.Parse(p).Single();
        Assert.Equal(("x", "x"), (f.OldName, f.NewName));
        var (before, after) = f.Sides();
        Assert.Equal(["old"], before);
        Assert.Equal(["new"], after);
    }

    [Fact]
    public void The_patch_written_by_our_own_diff_reads_back_to_the_same_sides()
    {
        string[] a = ["x", "y", "z"], b = ["x", "Y", "z", "w"];
        var text = Collaterale.Core.Diff.UnifiedDiff.Write("a.txt", "b.txt", a, b);
        var f = PatchReader.Parse(text).Single();
        var (before, after) = f.Sides();
        Assert.Equal(a, before);
        Assert.Equal(b, after);
    }
}

public class ShellIntegrationTests
{
    [Fact]
    public void Linux_gets_a_dolphin_service_menu_and_a_nautilus_script_pointing_at_the_program()
    {
        var files = ShellIntegration.FilesFor(AutoStartPlatform.Linux, "/opt/collaterale/Collaterale", "/home/u");
        var dolphin = files.Single(f => f.Path.EndsWith("collaterale-dolphin.desktop"));
        var nautilus = files.Single(f => f.Path.Contains("nautilus"));
        Assert.StartsWith("/home/u/.local/share/", dolphin.Path);
        Assert.Contains("Exec=\"/opt/collaterale/Collaterale\" %F", dolphin.Content);
        Assert.Contains("\"/opt/collaterale/Collaterale\"", nautilus.Content);
        Assert.True(nautilus.Executable);
        Assert.False(dolphin.Executable);
    }

    [Fact]
    public void Installing_writes_the_files_and_uninstalling_removes_them()
    {
        var home = Path.Combine(Path.GetTempPath(), "collaterale-si-" + Guid.NewGuid().ToString("N")[..8]);
        var message = ShellIntegration.Install(AutoStartPlatform.Linux, "/x/Collaterale", home);
        Assert.Contains("installed", message);
        var written = ShellIntegration.FilesFor(AutoStartPlatform.Linux, "/x/Collaterale", home);
        Assert.All(written, f => Assert.True(File.Exists(f.Path)));
        if (!OperatingSystem.IsWindows())
        {
            var nautilus = written.Single(f => f.Executable);
            Assert.True((File.GetUnixFileMode(nautilus.Path) & UnixFileMode.UserExecute) != 0);
        }

        Assert.True(ShellIntegration.IsInstalled(AutoStartPlatform.Linux, home));
        ShellIntegration.Uninstall(AutoStartPlatform.Linux, "/x/Collaterale", home);
        Assert.All(written, f => Assert.False(File.Exists(f.Path)));
        Assert.False(ShellIntegration.IsInstalled(AutoStartPlatform.Linux, home));
        Directory.Delete(home, true);
    }

    [Fact]
    public void Windows_gets_explorer_commands_for_files_and_folders_under_the_current_user_key()
    {
        var keys = ShellIntegration.WindowsKeys("C:\\Collaterale\\Collaterale.exe");
        Assert.Contains(keys, k => k.SubKey == @"Software\Classes\*\shell\CollateraleSelect\command" && k.Value == "\"C:\\Collaterale\\Collaterale.exe\" --select-left \"%1\"");
        Assert.Contains(keys, k => k.SubKey == @"Software\Classes\Directory\shell\CollateraleCompare\command" && k.Value.Contains("--compare-selected"));
        Assert.All(keys, k => Assert.StartsWith(@"Software\Classes\", k.SubKey));
    }

    [Fact]
    public void Mac_is_integrated_through_the_app_bundle_and_says_so()
    {
        Assert.Empty(ShellIntegration.FilesFor(AutoStartPlatform.Mac, "/Applications/Collaterale.app/Contents/MacOS/Collaterale", "/Users/u"));
        Assert.Contains("Finder", ShellIntegration.Install(AutoStartPlatform.Mac, "/x", "/Users/u"));
    }
}

public class ClipboardHistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "collaterale-ch-" + Guid.NewGuid().ToString("N")[..8]);

    public ClipboardHistoryTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void New_entries_go_first_and_repeats_and_blanks_are_ignored()
    {
        var h = new ClipboardHistory(Path.Combine(_dir, "c.json"), max: 5);
        Assert.True(h.Add("one"));
        Assert.False(h.Add("one"));                 // the same text again is not a new entry
        Assert.False(h.Add("   "));
        Assert.False(h.Add(null));
        Assert.True(h.Add("two"));
        Assert.Equal(["two", "one"], h.Entries);
        Assert.True(h.Add("one"));                  // an older text copied again is new news
        Assert.Equal(["one", "two", "one"], h.Entries);
    }

    [Fact]
    public void The_list_is_capped_and_survives_a_restart()
    {
        var path = Path.Combine(_dir, "c.json");
        var h = new ClipboardHistory(path, max: 3);
        for (var i = 1; i <= 5; i++) { h.Add("entry " + i); }
        Assert.Equal(["entry 5", "entry 4", "entry 3"], h.Entries);

        var again = new ClipboardHistory(path, max: 3);
        Assert.Equal(h.Entries, again.Entries);
        Assert.Equal(("entry 5", "entry 4"), again.LastTwo());
    }

    [Fact]
    public void Fewer_than_two_entries_give_no_pair()
    {
        var h = new ClipboardHistory(Path.Combine(_dir, "c.json"), max: 3);
        Assert.Null(h.LastTwo());
        h.Add("only");
        Assert.Null(h.LastTwo());
    }

    [Fact]
    public void A_broken_history_file_starts_empty()
    {
        var path = Path.Combine(_dir, "c.json");
        File.WriteAllText(path, "{ nope");
        Assert.Empty(new ClipboardHistory(path, max: 3).Entries);
    }
}
