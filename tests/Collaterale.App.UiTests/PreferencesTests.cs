using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Collaterale.App.ViewModels;
using Collaterale.App.Views;
using Collaterale.Core.Cli;
using Collaterale.Core.Settings;

namespace Collaterale.App.UiTests;

public class OptionsTests
{
    private static MainWindowViewModel Shell() => TextCompareTests.NewShell();

    [AvaloniaFact]
    public void Options_work_on_a_copy_so_cancel_changes_nothing_and_ok_updates_the_live_preferences()
    {
        var shell = Shell();
        var live = shell.Settings.Prefs;
        var options = new OptionsViewModel(shell.Settings);
        options.Prefs.TextEditing.ContextLines = 9;
        options.Prefs.Tabs.NewSessions = OpenIn.Window;
        Assert.Equal(3, live.TextEditing.ContextLines);                 // untouched until OK

        options.ApplyTo(shell.Settings);
        Assert.Same(live, shell.Settings.Prefs);                         // the open sessions keep seeing the same object
        Assert.Equal(9, live.TextEditing.ContextLines);
        Assert.Equal(OpenIn.Window, live.Tabs.NewSessions);
    }

    [AvaloniaFact]
    public void Open_with_applications_archive_masks_and_presets_are_edited_as_lists()
    {
        var shell = Shell();
        var options = new OptionsViewModel(shell.Settings);
        options.AddApp();
        options.SelectedApp!.Name = "Editor";
        options.SelectedApp.Command = "code";
        options.AddApp();                                                // left empty: not usable, dropped
        options.Archives.First(a => a.Format == "Zip").Mask = "*.zip;*.dat";
        options.AddPreset();
        options.SelectedPreset!.Name = "Web";
        options.SelectedPreset.Include = "*.html;*.css";
        options.ApplyTo(shell.Settings);

        Assert.Equal(["Editor"], shell.Settings.Prefs.OpenWith.Select(a => a.Name));
        Assert.Equal("*.zip;*.dat", shell.Settings.Prefs.Archives!.First(a => a.Format == "Zip").Mask);
        Assert.Equal("*.html;*.css", shell.Settings.FilterPresets.Single(p => p.Name == "Web").Include);

        var again = new OptionsViewModel(shell.Settings);
        again.RestoreArchiveDefaults();
        again.ApplyTo(shell.Settings);
        Assert.Null(shell.Settings.Prefs.Archives);                      // defaults are not stored
    }

    [AvaloniaFact]
    public void A_described_file_format_reaches_the_language_list_and_the_comparison()
    {
        var shell = Shell();
        try
        {
            var options = new OptionsViewModel(shell.Settings);
            options.AddFormat();
            options.SelectedFormat!.Name = "Toy config";
            options.SelectedFormat.Extensions = "toy";
            options.SelectedFormat.LineComments = "!";
            options.SelectedFormat.Strings = "\"";
            options.AddFormat();                                                // no extension: not usable, dropped
            options.ApplyTo(shell.Settings);
            shell.ApplyPreferences();

            Assert.Equal(["Toy config"], shell.Settings.Prefs.FileFormats.Select(f => f.Name));
            Assert.Contains(TextCompareViewModel.Languages, l => l.Label == "Toy config");

            var vm = shell.NewTextCompare();
            vm.OpenText("/work/a.toy", ["name = \"alpha\" ! one"], "/work/b.toy", ["name = \"beta\" ! two"]);
            Assert.True(vm.HasDifferences);
            vm.IgnoreComments = true;
            vm.IgnoreStrings = true;
            Assert.False(vm.HasDifferences);                                    // the language came from the extension

            // taking the format away removes it from the list again
            var again = new OptionsViewModel(shell.Settings);
            again.SelectedFormat = again.FileFormats[0];
            again.RemoveFormat();
            again.ApplyTo(shell.Settings);
            shell.ApplyPreferences();
            Assert.DoesNotContain(TextCompareViewModel.Languages, l => l.Label == "Toy config");
        }
        finally
        {
            Collaterale.Core.Languages.CommentSyntaxes.ConfigureCustom([]);
            TextCompareViewModel.RefreshLanguages();
        }
    }

    [AvaloniaFact]
    public void The_commands_page_assigns_removes_and_restores_shortcuts_and_flags_clashes()
    {
        var shell = Shell();
        var options = new OptionsViewModel(shell.Settings);
        var find = options.AllCommands.Single(r => r.Info.Id == "text.find");
        var replace = options.AllCommands.Single(r => r.Info.Id == "text.replace");

        Assert.Equal("Ctrl+F", find.Shortcut);
        Assert.Null(options.Assign(find, "ctrl+shift+f", reassign: false));
        Assert.Equal("Ctrl+Shift+F", find.Shortcut);
        Assert.Equal("?", options.Assign(find, "Ctrl+", reassign: false));
        Assert.Equal("Find", options.Assign(replace, "ctrl+shift+f", reassign: false));   // taken by Find: nothing changed
        Assert.Equal("Ctrl+H", replace.Shortcut);
        Assert.Null(options.Assign(replace, "ctrl+shift+f", reassign: true));
        Assert.Equal("", find.Shortcut);                                                  // taken over

        options.RestoreShortcut(find);
        Assert.Equal("Ctrl+F", find.Shortcut);
        options.RemoveShortcut(find);
        Assert.Equal("", find.Shortcut);

        options.CommandSearch = "undo";
        Assert.All(options.Commands, c => Assert.Contains("undo", (c.Title + c.Description).ToLowerInvariant()));
        options.CommandSearch = "";
        options.CommandView = CommandView.Merge;
        Assert.All(options.Commands, c => Assert.Equal(CommandView.Merge, c.Info.View));
    }

    [AvaloniaFact]
    public async Task The_options_window_renders_every_page()
    {
        var shell = Shell();
        var options = new OptionsViewModel(shell.Settings);
        var window = new OptionsWindow(options);
        window.Show();
        await TextCompareTests.Settle();
        var tabs = window.GetVisualDescendants().OfType<Avalonia.Controls.TabControl>().First();
        Assert.True(tabs.ItemCount >= 15);
        TextCompareTests.Shot(window, "options.png");
        tabs.SelectedIndex = 7;                                          // Commands
        await TextCompareTests.Settle();
        TextCompareTests.Shot(window, "options-commands.png");
        tabs.SelectedIndex = tabs.Items.OfType<Avalonia.Controls.TabItem>().ToList().FindIndex(t => (string?)t.Header == "File formats");
        var toy = new FileFormat { Name = "Toy config", Extensions = "toy cfg2", LineComments = "! --", BlockComments = "<< >> (* *)", Strings = "\" '" };
        options.FileFormats.Add(toy);
        options.SelectedFormat = toy;
        await TextCompareTests.Settle();
        TextCompareTests.Shot(window, "options-file-formats.png");
        window.Close();
    }
}

public class WorkspaceAndSessionTests
{
    private static (string L, string R) Pair()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-ws-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var (l, r) = (Path.Combine(dir, "l.txt"), Path.Combine(dir, "r.txt"));
        File.WriteAllText(l, "a\nb\n");
        File.WriteAllText(r, "a\nc\n");
        return (l, r);
    }

    [AvaloniaFact]
    public void A_session_is_saved_from_the_tab_in_front_and_opens_again_by_name()
    {
        var (l, r) = Pair();
        var shell = TextCompareTests.NewShell();
        shell.OpenTextCompare(l, r);
        Assert.True(shell.SaveSession("Daily"));
        Assert.Equal((l, r), (shell.Settings.FindSession("daily")!.Left, shell.Settings.FindSession("daily")!.Right));
        Assert.Contains(shell.Home.Sessions, s => s.Name == "Daily" && s.Kind == "session");

        var tabs = shell.Tabs.Count;
        shell.Launch(LaunchParser.Parse(["Daily"]));                     // collaterale "Daily"
        Assert.Equal(tabs + 1, shell.Tabs.Count);
        Assert.IsType<TextCompareViewModel>(shell.SelectedTab!.Content);
    }

    [AvaloniaFact]
    public void The_home_tab_cannot_be_saved_and_an_unknown_name_is_reported()
    {
        var shell = TextCompareTests.NewShell();
        Assert.False(shell.SaveSession("Nothing"));
        Assert.NotNull(shell.Message);
        shell.OpenNamed("no such name");
        Assert.Contains("no such name", shell.Message);
    }

    [AvaloniaFact]
    public void A_workspace_remembers_every_tab_and_which_was_in_front()
    {
        var (l, r) = Pair();
        var shell = TextCompareTests.NewShell();
        shell.OpenTextCompare(l, r);
        shell.OpenHexCompare(l, r);
        shell.OpenTextCompare(r, l);
        shell.SelectedTab = shell.Tabs[2];                               // the hex compare
        Assert.True(shell.SaveWorkspace("Morning"));

        var w = shell.Settings.Workspaces.Single();
        Assert.Equal(3, w.Sessions.Count);
        Assert.Equal(1, w.Selected);

        var other = TextCompareTests.NewShell();
        other.Settings.SaveWorkspace(w);
        other.OpenNamed("Morning");
        Assert.Equal(4, other.Tabs.Count);                               // Home + three
        Assert.IsType<HexCompareViewModel>(other.SelectedTab!.Content);
    }

    [AvaloniaFact]
    public void Closing_the_window_keeps_a_snapshot_and_the_start_up_preference_brings_it_back()
    {
        var (l, r) = Pair();
        var shell = TextCompareTests.NewShell();
        shell.Settings.Prefs.Startup.SaveWorkspaceOnExit = "Last time";
        shell.Settings.Prefs.Startup.LoadWorkspace = "Last time";
        shell.OpenTextCompare(l, r);
        shell.SaveOnExit();
        Assert.Contains(shell.Settings.Workspaces, w => w.Name == "Last time");
        Assert.Contains(shell.Settings.Workspaces, w => w.AutoSaved);

        var next = TextCompareTests.NewShell();
        next.Settings.Workspaces.AddRange(shell.Settings.Workspaces);
        next.Settings.Prefs.Startup.LoadWorkspace = "Last time";
        next.StartupWorkspace();
        Assert.IsType<TextCompareViewModel>(next.SelectedTab!.Content);
    }

    [AvaloniaFact]
    public void A_session_whose_files_are_gone_is_skipped_with_a_message()
    {
        var shell = TextCompareTests.NewShell();
        shell.Settings.SaveSession(new SavedSession { Name = "Gone", Kind = LaunchKind.TextCompare, Left = "/no/a", Right = "/no/b" });
        var tabs = shell.Tabs.Count;
        shell.OpenNamed("Gone");
        Assert.Equal(tabs, shell.Tabs.Count);
        Assert.Contains("gone", shell.Message);
    }
}

public class TabAndLaunchTests
{
    private static (string L, string R) Pair()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-tl-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var (l, r) = (Path.Combine(dir, "l.txt"), Path.Combine(dir, "r.txt"));
        File.WriteAllText(l, "a\nb\n");
        File.WriteAllText(r, "a\nc\n");
        return (l, r);
    }

    [AvaloniaFact]
    public void New_sessions_can_go_to_a_window_of_their_own()
    {
        var (l, r) = Pair();
        var shell = TextCompareTests.NewShell();
        var requested = new List<SessionTab>();
        shell.NewWindowRequested = requested.Add;
        shell.OpenTextCompare(l, r);
        Assert.Equal(2, shell.Tabs.Count);                               // Home and the new tab: the default is a tab
        Assert.Empty(requested);

        shell.Settings.Prefs.Tabs.NewSessions = OpenIn.Window;
        shell.OpenTextCompare(r, l);
        Assert.Single(requested);
        Assert.Equal(2, shell.Tabs.Count);                               // it did not become a tab here
    }

    [AvaloniaFact]
    public async Task Child_sessions_follow_their_own_preference()
    {
        var root = Path.Combine(Path.GetTempPath(), "collaterale-tl-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "L")); Directory.CreateDirectory(Path.Combine(root, "R"));
        File.WriteAllText(Path.Combine(root, "L", "x.txt"), "1\n"); File.WriteAllText(Path.Combine(root, "R", "x.txt"), "2\n");
        var shell = TextCompareTests.NewShell();
        var requested = new List<SessionTab>();
        shell.NewWindowRequested = requested.Add;
        var folders = shell.NewFolderCompare();
        await folders.OpenAsync(Path.Combine(root, "L"), Path.Combine(root, "R"));

        shell.Settings.Prefs.Tabs.ChildSessions = OpenIn.Window;          // new sessions still use tabs
        folders.Activate(folders.Rows.Single(x => x.Name == "x.txt"));
        Assert.Single(requested);
        Assert.IsType<TextCompareViewModel>(requested[0].Content);
    }

    [AvaloniaFact]
    public void The_tab_bar_hides_for_one_tab_and_the_last_tab_can_close_the_window()
    {
        var (l, r) = Pair();
        var shell = TextCompareTests.NewShell();
        Assert.True(shell.ShowTabStrip);
        shell.Settings.Prefs.Tabs.HideTabBarIfSingleTab = true;
        shell.ApplyPreferences();
        Assert.False(shell.ShowTabStrip);                                // only Home
        shell.OpenTextCompare(l, r);
        Assert.True(shell.ShowTabStrip);

        var closed = 0;
        shell.CloseWindowRequested = () => closed++;
        shell.CloseTabCommand.Execute(shell.SelectedTab);
        Assert.Equal(0, closed);                                          // returns to the Home view by default
        shell.Settings.Prefs.Tabs.ClosingLastTabClosesWindow = true;
        shell.OpenTextCompare(l, r);
        shell.CloseTabCommand.Execute(shell.SelectedTab);
        Assert.Equal(1, closed);
    }

    [AvaloniaFact]
    public void A_patch_opens_as_read_only_before_and_after_views_one_per_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-tl-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var patch = Path.Combine(dir, "fix.patch");
        File.WriteAllText(patch, "--- a/one.txt\n+++ b/one.txt\n@@ -1,2 +1,2 @@\n keep\n-old\n+new\n--- a/two.txt\n+++ b/two.txt\n@@ -1 +1 @@\n-x\n+y\n");
        var shell = TextCompareTests.NewShell();
        shell.Launch(LaunchParser.Parse([patch]));
        Assert.Equal(3, shell.Tabs.Count);                                // Home + two files
        var vm = Assert.IsType<TextCompareViewModel>(shell.Tabs[1].Content);
        Assert.Equal("Patch: one.txt", vm.Title);
        Assert.True(vm.LeftReadOnly && vm.RightReadOnly);
        Assert.Equal(["keep", "old"], vm.Left.Lines);
        Assert.Equal(["keep", "new"], vm.Right.Lines);
    }

    [AvaloniaFact]
    public void Text_edit_opens_one_file_edits_and_saves_it_with_a_backup()
    {
        var (l, _) = Pair();
        var shell = TextCompareTests.NewShell();
        shell.Settings.Prefs.Backup.Mode = BackupMode.SameFolderSuffix;
        shell.Launch(LaunchParser.Parse(["/edit", l]));
        var vm = Assert.IsType<TextEditViewModel>(shell.SelectedTab!.Content);
        Assert.Equal("a\nb", vm.Text);
        vm.Edited("a\nB");
        Assert.True(vm.Dirty);
        vm.SaveCommand.Execute(null);
        Assert.False(vm.Dirty);
        Assert.Equal("a\nB\n", File.ReadAllText(l).Replace("\r\n", "\n"));
        Assert.Equal("a\nb\n", File.ReadAllText(l + ".bak").Replace("\r\n", "\n"));   // the backup preference
    }

    [AvaloniaFact]
    public void A_settings_package_on_the_command_line_is_imported()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-tl-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var src = new AppSettings();
        src.Prefs.Tabs.HideTabBarIfSingleTab = true;
        var pkg = Path.Combine(dir, "p.cpkg");
        SettingsPackage.Export(src, pkg);

        var shell = TextCompareTests.NewShell();
        shell.Launch(LaunchParser.Parse([pkg]));
        Assert.True(shell.Settings.Prefs.Tabs.HideTabBarIfSingleTab);
        Assert.Contains("Imported", shell.Message);
        shell.Launch(LaunchParser.Parse([Path.Combine(dir, "bad.cpkg")]));   // not an existing file: treated as a name
        Assert.Contains("bad.cpkg", shell.Message);
    }

    [AvaloniaFact]
    public async Task A_script_runs_in_a_tab_and_shows_its_output()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-tl-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var script = Path.Combine(dir, "s.txt");
        File.WriteAllText(script, "log \"hello from the script\"\nexit 3\n");
        var shell = TextCompareTests.NewShell();
        shell.Launch(LaunchParser.Parse(["@" + script]));
        var vm = Assert.IsType<ScriptViewModel>(shell.SelectedTab!.Content);
        for (var i = 0; i < 100 && vm.Running; i++) { await Task.Delay(30); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        Assert.Contains("hello from the script", vm.Output);
        Assert.Equal(3, vm.ExitCode);
    }
}

public class NavigationPreferenceTests
{
    private static TextCompareViewModel Open(string[] left, string[] right)
    {
        var vm = new TextCompareViewModel();
        vm.OpenText("l.txt", left, "r.txt", right);
        return vm;
    }

    private static readonly string[] L = ["a", "b", "c", "d", "e", "f", "g"];
    private static readonly string[] R = ["a", "B", "c", "d", "E", "f", "G"];

    [AvaloniaFact]
    public void Next_and_previous_wrap_around_or_stop_with_a_message()
    {
        var vm = Open(L, R);
        Assert.Equal(3, vm.Comparison!.DifferenceCount);
        Assert.Equal(0, vm.CurrentGroup);
        vm.NextDifferenceCommand.Execute(null); vm.NextDifferenceCommand.Execute(null);
        Assert.Equal(2, vm.CurrentGroup);
        vm.NextDifferenceCommand.Execute(null);
        Assert.Equal(0, vm.CurrentGroup);                                 // wrapped
        Assert.Contains("Wrapped", vm.Notice);

        vm.Prefs.NextDifference.WrapAround = false;
        vm.CurrentGroup = 2;
        vm.NextDifferenceCommand.Execute(null);
        Assert.Equal(2, vm.CurrentGroup);
        Assert.Contains("No more", vm.Notice);
        vm.Prefs.NextDifference.ShowMessagePanel = false;
        vm.NextDifferenceCommand.Execute(null);
        Assert.Equal("", vm.Notice);                                       // stays quiet when the message is off
        vm.CurrentGroup = 0;
        vm.PreviousDifferenceCommand.Execute(null);
        Assert.Equal(0, vm.CurrentGroup);
    }

    [AvaloniaFact]
    public void Going_to_the_first_difference_on_load_is_a_preference()
    {
        var vm = new TextCompareViewModel();
        vm.Prefs.NextDifference.GoToFirstOnLoad = false;
        vm.OpenText("l", L, "r", R);
        Assert.Equal(-1, vm.CurrentGroup);
        vm.NextDifferenceCommand.Execute(null);
        Assert.Equal(0, vm.CurrentGroup);
    }

    [AvaloniaFact]
    public void After_a_copy_the_next_difference_is_selected_unless_the_preference_is_off()
    {
        var vm = Open(L, R);
        vm.CopyGroup(0, Side.Right);
        Assert.Equal(3 - 1, vm.Comparison!.DifferenceCount);
        Assert.Equal(0, vm.CurrentGroup);                                  // the next one moved into place

        var quiet = Open(L, R);
        quiet.Prefs.NextDifference.GoToNextAfterCopy = false;
        quiet.CopyGroup(0, Side.Right);
        Assert.Equal(-1, quiet.CurrentGroup);
    }

    [AvaloniaFact]
    public void Individual_differences_step_through_the_changed_words_inside_lines()
    {
        var vm = Open(["one two three four five"], ["one TWO three FOUR five"]);
        Assert.Equal(1, vm.Comparison!.DifferenceCount);                  // one section...
        var items = vm.DifferenceItems();
        Assert.True(items.Count >= 2);                                    // ...but several words differ in it
        var seen = new List<TextCompareViewModel.DifferenceItem>();
        vm.ItemFocusRequested += seen.Add;
        vm.NextItemCommand.Execute(null);
        vm.NextItemCommand.Execute(null);
        Assert.Equal(2, seen.Count);
        Assert.True(seen[1].Start > seen[0].Start);
        vm.PreviousItemCommand.Execute(null);
        Assert.Equal(seen[0], seen[2]);
    }

    [AvaloniaFact]
    public void The_context_lines_preference_decides_how_much_stays_around_a_difference()
    {
        var many = Enumerable.Range(0, 40).Select(i => "line " + i).ToArray();
        var changed = many.ToArray();
        changed[20] = "CHANGED";
        var vm = Open(many, changed);
        vm.CollapseUnchanged = true;
        vm.Prefs.TextEditing.ContextLines = 3;
        vm.PreferencesChanged();
        var visibleAt3 = vm.LeftRows.Count(r => r.FoldCount == 0);
        vm.Prefs.TextEditing.ContextLines = 8;
        vm.PreferencesChanged();
        var visibleAt8 = vm.LeftRows.Count(r => r.FoldCount == 0);
        Assert.True(visibleAt8 > visibleAt3);
        Assert.Contains("unchanged lines hidden", vm.FilteredLinesText);
        vm.Prefs.TextEditing.ShowFilteredLineCounts = false;
        vm.PreferencesChanged();
        Assert.Equal("", vm.FilteredLinesText);
    }
}

public class BackupWiringTests
{
    private static string Dir()
    {
        var d = Path.Combine(Path.GetTempPath(), "collaterale-bw-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    [AvaloniaFact]
    public void Saving_a_text_compare_keeps_a_backup_of_the_old_file_when_the_preference_is_on()
    {
        var d = Dir();
        var (l, r) = (Path.Combine(d, "l.txt"), Path.Combine(d, "r.txt"));
        File.WriteAllText(l, "a\nb\n");
        File.WriteAllText(r, "a\nc\n");
        var vm = new TextCompareViewModel();
        vm.Prefs.Backup.Mode = BackupMode.SameFolderNumbered;
        vm.Open(l, r);
        vm.CopyGroup(0, Side.Right);
        vm.SaveLeftCommand.Execute(null);
        Assert.Equal("a\nb\n", File.ReadAllText(l + ".bak1").Replace("\r\n", "\n"));
        Assert.Equal("a\nc\n", File.ReadAllText(l).Replace("\r\n", "\n"));
    }

    [AvaloniaFact]
    public void The_nobackups_switch_wins_over_the_preference()
    {
        var d = Dir();
        var (l, r) = (Path.Combine(d, "l.txt"), Path.Combine(d, "r.txt"));
        File.WriteAllText(l, "a\nb\n");
        File.WriteAllText(r, "a\nc\n");
        var shell = TextCompareTests.NewShell();
        shell.Settings.Prefs.Backup.Mode = BackupMode.SameFolderSuffix;
        shell.Launch(LaunchParser.Parse(["/nobackups", l, r]));
        var vm = (TextCompareViewModel)shell.SelectedTab!.Content;
        vm.CopyGroup(0, Side.Right);
        vm.SaveLeftCommand.Execute(null);
        Assert.False(File.Exists(l + ".bak"));
    }

    [AvaloniaFact]
    public void A_merge_saved_over_an_existing_file_is_backed_up()
    {
        var d = Dir();
        var (b, l, r, o) = (Path.Combine(d, "b"), Path.Combine(d, "l"), Path.Combine(d, "r"), Path.Combine(d, "out"));
        File.WriteAllText(b, "x\n"); File.WriteAllText(l, "x\n"); File.WriteAllText(r, "y\n"); File.WriteAllText(o, "old\n");
        var vm = new MergeViewModel();
        vm.Prefs.Backup.Mode = BackupMode.SameFolderSuffix;
        vm.Open(b, l, r, o);
        Assert.True(vm.TrySave(false));
        Assert.Equal("old\n", File.ReadAllText(o + ".bak").Replace("\r\n", "\n"));
    }

    [AvaloniaFact]
    public async Task A_folder_synchronize_backs_up_what_it_overwrites_and_the_log_follows_the_log_preferences()
    {
        var d = Dir();
        Directory.CreateDirectory(Path.Combine(d, "L")); Directory.CreateDirectory(Path.Combine(d, "R"));
        File.WriteAllText(Path.Combine(d, "L", "f.txt"), "new");
        File.WriteAllText(Path.Combine(d, "R", "f.txt"), "old!");
        File.SetLastWriteTimeUtc(Path.Combine(d, "L", "f.txt"), DateTime.UtcNow.AddHours(1));       // the left one is the newer, so it is copied over
        var vm = new FolderCompareViewModel();
        vm.Prefs.Backup.Mode = BackupMode.SameFolderSuffix;
        vm.Prefs.Log.Details = true;
        await vm.OpenAsync(Path.Combine(d, "L"), Path.Combine(d, "R"));
        var plan = vm.PlanSync(Collaterale.Core.Folders.SyncMode.UpdateRight);
        await vm.ExecuteAsync(plan, "Synchronize", askConfirmation: false);

        Assert.Equal("old!", File.ReadAllText(Path.Combine(d, "R", "f.txt.bak")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(d, "R", "f.txt")));
        Assert.Contains(vm.Log, l => l.Text.Contains("f.txt"));                 // the detail line, because details are on
        vm.Prefs.Log.Details = false;
        var count = vm.Log.Count;
        vm.AddLog("each file", Collaterale.Core.Support.LogLevel.Detail);
        Assert.Equal(count, vm.Log.Count);                                       // hidden when off
    }

    [AvaloniaFact]
    public async Task Synchronize_can_be_turned_off_and_confirmations_follow_the_preferences()
    {
        var d = Dir();
        Directory.CreateDirectory(Path.Combine(d, "L")); Directory.CreateDirectory(Path.Combine(d, "R"));
        File.WriteAllText(Path.Combine(d, "L", "only.txt"), "x");
        var vm = new FolderCompareViewModel();
        await vm.OpenAsync(Path.Combine(d, "L"), Path.Combine(d, "R"));
        var asked = 0;
        vm.Confirm = (_, _, _) => { asked++; return Task.FromResult(true); };
        var plan = vm.PlanSync(Collaterale.Core.Folders.SyncMode.UpdateRight);

        await vm.ExecuteAsync(plan, "Copy");
        Assert.Equal(1, asked);                                                  // a plain copy is asked about by default

        File.Delete(Path.Combine(d, "R", "only.txt"));
        await vm.CompareAsync();
        vm.Prefs.FileOperations.ConfirmCopy = false;
        await vm.ExecuteAsync(vm.PlanSync(Collaterale.Core.Folders.SyncMode.UpdateRight), "Copy");
        Assert.Equal(1, asked);                                                  // turned off: not asked again
        Assert.True(File.Exists(Path.Combine(d, "R", "only.txt")));

        Assert.True(vm.SyncAllowed(out _));
        vm.Prefs.FileOperations.SyncConfirmation = Confirmation.NoToAll;
        Assert.False(vm.SyncAllowed(out var reason));
        Assert.Contains("turned off", reason);
    }
}

public class NextFileTests
{
    private static async Task<(MainWindowViewModel Shell, FolderCompareViewModel Folder)> Open()
    {
        var root = Path.Combine(Path.GetTempPath(), "collaterale-nf-" + Guid.NewGuid().ToString("N")[..8]);
        foreach (var side in new[] { "L", "R" }) { Directory.CreateDirectory(Path.Combine(root, side, "sub")); }
        void Pair(string rel, string left, string right)
        {
            File.WriteAllText(Path.Combine(root, "L", rel), left);
            File.WriteAllText(Path.Combine(root, "R", rel), right);
        }

        Pair("a.txt", "1\n", "2\n");
        Pair("b.txt", "same\n", "same\n");
        Pair("c.txt", "1\n", "2\n");
        Pair("sub/d.txt", "1\n", "2\n");
        var shell = TextCompareTests.NewShell();
        var folder = shell.NewFolderCompare();
        folder.Criteria = Collaterale.Core.Folders.CompareCriteria.Content;
        await folder.OpenAsync(Path.Combine(root, "L"), Path.Combine(root, "R"));
        folder.Activate(folder.Rows.Single(r => r.Name == "a.txt"));
        return (shell, folder);
    }

    [AvaloniaFact]
    public async Task Next_file_walks_through_the_files_that_differ_skipping_those_that_do_not()
    {
        var (shell, _) = await Open();
        var text = Assert.IsType<TextCompareViewModel>(shell.SelectedTab!.Content);
        Assert.True(text.HasFileNavigator);
        Assert.EndsWith("a.txt", text.Left.Path);
        text.NextFileCommand.Execute(null);
        Assert.EndsWith("c.txt", text.Left.Path);                       // b.txt is identical: skipped
        text.NextFileCommand.Execute(null);
        Assert.EndsWith("d.txt", text.Left.Path);
        text.PreviousFileCommand.Execute(null);
        Assert.EndsWith("c.txt", text.Left.Path);
    }

    [AvaloniaFact]
    public async Task Wrap_around_and_the_current_folder_limit_follow_the_preferences()
    {
        var (shell, folder) = await Open();
        var text = (TextCompareViewModel)shell.SelectedTab!.Content;
        // the tree lists folders first, so the files that differ are: sub/d.txt, a.txt, c.txt
        text.PreviousFileCommand.Execute(null);
        Assert.EndsWith("d.txt", text.Left.Path);
        text.PreviousFileCommand.Execute(null);                         // before the first: wraps to the last
        Assert.EndsWith("c.txt", text.Left.Path);

        shell.Settings.Prefs.NextDifference.WrapAround = false;
        text.NextFileCommand.Execute(null);                             // after the last: stays
        Assert.EndsWith("c.txt", text.Left.Path);
        Assert.Contains("No more files", text.Notice);
        text.Open(folder.Comparison!.LeftPath(folder.Comparison.Root.Descendants().Single(n => n.RelativePath == "sub/d.txt")), folder.Comparison.RightPath(folder.Comparison.Root.Descendants().Single(n => n.RelativePath == "sub/d.txt")));

        shell.Settings.Prefs.NextDifference.WrapAround = true;
        shell.Settings.Prefs.NextDifference.LimitFilesToCurrentFolder = true;
        Assert.Null(folder.NeighbourDifferentFile(text.Left.Path!, +1));   // d.txt is alone in sub/: nowhere to go (a single file does not wrap onto itself)
    }
}

public class MergeSwitchTests
{
    [AvaloniaFact]
    public void Titles_and_nobackups_from_the_command_line_apply_to_a_merge()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-ms-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string F(string n, string t) { var p = Path.Combine(dir, n); File.WriteAllText(p, t); return p; }
        var (l, r, c) = (F("l", "a\n"), F("r", "b\n"), F("c", "a\n"));
        var shell = TextCompareTests.NewShell();
        shell.Launch(LaunchParser.Parse(["/title1=Mine", "/title2=Theirs", "/vcs3=origin/main:file", "/title4=Merged", "/nobackups", l, r, c]));
        var vm = shell.CommandLineMerge!;
        Assert.Equal(("Mine", "Theirs", "origin/main:file", "Merged"), (vm.LeftTitle, vm.RightTitle, vm.BaseTitle, vm.OutputTitle));
        Assert.True(vm.NoBackups);
    }
}
