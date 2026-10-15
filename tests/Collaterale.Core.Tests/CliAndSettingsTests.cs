using Collaterale.Core.Cli;
using Collaterale.Core.Diff;
using Collaterale.Core.Settings;

namespace Collaterale.Core.Tests;

public class CliAndSettingsTests
{
    private static string Tmp(bool dir = false)
    {
        var p = Path.Combine(Path.GetTempPath(), "collaterale-cli-" + Guid.NewGuid().ToString("N")[..8]);
        if (dir) { Directory.CreateDirectory(p); } else { File.WriteAllText(p, "x"); }
        return p;
    }

    [Fact]
    public void No_arguments_open_home_and_flags_are_recognised()
    {
        Assert.Equal(LaunchKind.Home, LaunchParser.Parse([]).Kind);
        Assert.Equal(LaunchKind.Help, LaunchParser.Parse(["--help"]).Kind);
        Assert.Equal(LaunchKind.Version, LaunchParser.Parse(["-v"]).Kind);
    }

    [Fact]
    public void Two_files_two_folders_and_mismatches()
    {
        var (f1, f2, d1, d2) = (Tmp(), Tmp(), Tmp(true), Tmp(true));
        Assert.Equal(new LaunchRequest(LaunchKind.TextCompare, f1, f2), LaunchParser.Parse([f1, f2]));
        Assert.Equal(new LaunchRequest(LaunchKind.FolderCompare, d1, d2), LaunchParser.Parse([d1, d2]));
        Assert.Equal(LaunchKind.Error, LaunchParser.Parse([f1, d1]).Kind);
        Assert.Contains("does not exist", LaunchParser.Parse([f1, "/no/such/file"]).Message);
        Assert.Contains("two paths", LaunchParser.Parse([f1]).Message);
    }

    [Fact]
    public void Merge_arguments_in_any_order_with_optional_base()
    {
        var (b, l, r) = (Tmp(), Tmp(), Tmp());
        var req = LaunchParser.Parse(["--merge", "--output", "/tmp/out.txt", "--right", r, "--left", l, "--base", b]);
        Assert.Equal(new LaunchRequest(LaunchKind.Merge, l, r, b, "/tmp/out.txt"), req);

        var noBase = LaunchParser.Parse(["--merge", "--left", l, "--right", r, "--output", "/tmp/o"]);
        Assert.Equal((LaunchKind.Merge, (string?)null), (noBase.Kind, noBase.Base));
    }

    [Fact]
    public void Merge_errors_are_explained()
    {
        Assert.Contains("--output", LaunchParser.Parse(["--merge", "--left", "a"]).Message);
        Assert.Contains("Unknown", LaunchParser.Parse(["--merge", "--bogus"]).Message);
        Assert.Contains("does not exist", LaunchParser.Parse(["--merge", "--left", "/nope", "--right", "/nope2", "--output", "/o"]).Message);
    }

    [Fact]
    public void Help_and_version_are_answered_without_a_window()
    {
        Assert.Contains("Usage:", LaunchParser.Informational(["--help"], "Collaterale", "1.2.3"));
        Assert.Equal("Collaterale 1.2.3", LaunchParser.Informational(["--version"], "Collaterale", "1.2.3"));
        Assert.Null(LaunchParser.Informational([], "Collaterale", "1.2.3"));
        Assert.Null(LaunchParser.Informational(["a", "b"], "Collaterale", "1.2.3"));
    }

    [Fact]
    public void Help_text_documents_the_git_integration()
        => Assert.Contains("mergetool.collaterale.cmd", LaunchRequest.HelpText);

    [Fact]
    public void Recent_sessions_are_mru_deduplicated_and_capped()
    {
        var s = new AppSettings();
        for (var i = 0; i < 30; i++) { s.AddRecent(new RecentSession(LaunchKind.TextCompare, $"/a{i}", "/b", null, null, DateTime.UtcNow)); }
        s.AddRecent(new RecentSession(LaunchKind.TextCompare, "/a5", "/b", null, null, DateTime.UtcNow));
        Assert.Equal("/a5", s.Recent[0].Left);
        Assert.Equal(AppSettings.MaxRecent, s.Recent.Count);
        Assert.Single(s.Recent, r => r.Left == "/a5");
        Assert.Equal("a5  ↔  b", s.Recent[0].Title);
    }

    [Fact]
    public void Settings_roundtrip_including_enums_and_survive_corruption()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-set-" + Guid.NewGuid().ToString("N")[..8]);
        var store = new SettingsStore(Path.Combine(dir, "s.json"));
        Assert.Equal("System", store.Load().Theme);

        var s = new AppSettings { Theme = "Dark", Whitespace = WhitespaceMode.IgnoreAll, Algorithm = DiffAlgorithm.Patience, IgnoreCase = true };
        s.AddRecent(new RecentSession(LaunchKind.Merge, "/l", "/r", "/b", "/o", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        store.Save(s);
        var back = store.Load();
        Assert.Equal((WhitespaceMode.IgnoreAll, DiffAlgorithm.Patience, true, "Dark"), (back.Whitespace, back.Algorithm, back.IgnoreCase, back.Theme));
        Assert.Equal(LaunchKind.Merge, back.Recent.Single().Kind);

        File.WriteAllText(Path.Combine(dir, "s.json"), "{ nope");
        Assert.Equal("System", store.Load().Theme);
        Directory.Delete(dir, true);
    }
}

public class FolderSettingsPersistenceTests
{
    [Fact]
    public void Advanced_folder_options_survive_a_save_and_load()
    {
        var path = Path.Combine(Path.GetTempPath(), "collaterale-set-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        var store = new Collaterale.Core.Settings.SettingsStore(path);
        var s = new Collaterale.Core.Settings.AppSettings
        {
            FolderDefaults = new Collaterale.Core.Folders.FolderOptions
            {
                Criteria = Collaterale.Core.Folders.CompareCriteria.QuickThenContent,
                ContentMode = Collaterale.Core.Folders.ContentMode.Rules,
                Rules = new Collaterale.Core.Folders.ContentRules { IgnoreComments = true, Whitespace = Collaterale.Core.Diff.WhitespaceMode.IgnoreAmount },
                CompareAttributes = FileAttributes.ReadOnly | FileAttributes.Hidden,
                IgnoreTimeZone = true,
                OtherFilters = [new Collaterale.Core.Folders.OtherFilter(Collaterale.Core.Folders.OtherFilterKind.LargerThan, 1234)],
                Alignments = [new Collaterale.Core.Folders.AlignmentOverride("a.txt", "b.txt")],
                Archives = Collaterale.Core.Folders.ArchiveHandling.AsFoldersOnceOpened,
            },
            Colors = new Collaterale.Core.Settings.FolderColors { Orphan = "#112233" },
            FilterPresets = [new Collaterale.Core.Folders.FilterPreset("Sources", "*.cs;*.md", ExcludeFolders: "bin;obj")],
        };
        store.Save(s);

        var back = store.Load();
        var o = back.FolderDefaults!;
        Assert.Equal(Collaterale.Core.Folders.CompareCriteria.QuickThenContent, o.Criteria);
        Assert.Equal(Collaterale.Core.Folders.ContentMode.Rules, o.ContentMode);
        Assert.True(o.Rules.IgnoreComments);
        Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Hidden, o.CompareAttributes);
        Assert.Equal(1234, Assert.Single(o.OtherFilters).Number);
        Assert.Equal("b.txt", Assert.Single(o.Alignments).RightPath);
        Assert.Equal(Collaterale.Core.Folders.ArchiveHandling.AsFoldersOnceOpened, o.Archives);
        Assert.Equal("#112233", back.Colors.Orphan);
        Assert.Equal("bin;obj", Assert.Single(back.FilterPresets).ExcludeFolders);
    }
}

public class PreferencesTests
{
    [Fact]
    public void Defaults_match_the_documented_behavior()
    {
        var p = new Preferences();
        Assert.Equal(OpenIn.Tab, p.Tabs.NewSessions);
        Assert.True(p.TextEditing.AutoIndent && p.TextEditing.BackspaceUnindents && p.TextEditing.InitFindFromWord);
        Assert.Equal(3, p.TextEditing.ContextLines);
        Assert.True(p.NextDifference.WrapAround && p.NextDifference.ShowMessagePanel && p.NextDifference.GoToFirstOnLoad);
        Assert.Equal(BackupMode.Off, p.Backup.Mode);
        Assert.True(p.FileOperations.ConfirmDelete);
        Assert.Equal(500, p.Log.MaxMessages);
        Assert.Equal(20, p.Metrics.MaxRecentItems);
        Assert.Null(p.Archives);
    }

    [Fact]
    public void Preferences_roundtrip_through_the_settings_file_as_readable_names()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-prefs-" + Guid.NewGuid().ToString("N")[..8]);
        var path = Path.Combine(dir, "s.json");
        var store = new SettingsStore(path);
        var s = new AppSettings();
        s.Prefs.Tabs.NewSessions = OpenIn.Window;
        s.Prefs.Backup.Mode = BackupMode.BackupFolder;
        s.Prefs.Backup.Folder = "/var/backups";
        s.Prefs.OpenWith.Add(new OpenWithApp { Name = "Editor", Command = "code", Arguments = "-d \"%1\" \"%2\"" });
        s.Prefs.Commands.Add(new CommandBinding { Command = "text.find", Shortcut = "Ctrl+Shift+F" });
        s.Prefs.Archives = [new ArchiveType { Format = "Zip", Mask = "*.zip;*.jar" }];
        s.Prefs.Files.Priority = ComparisonPriority.Low;
        store.Save(s);

        Assert.Contains("\"BackupFolder\"", File.ReadAllText(path));        // names, not numbers, so the file can be edited by hand
        var back = store.Load().Prefs;
        Assert.Equal(OpenIn.Window, back.Tabs.NewSessions);
        Assert.Equal((BackupMode.BackupFolder, "/var/backups"), (back.Backup.Mode, back.Backup.Folder));
        Assert.Equal("code", back.OpenWith.Single().Command);
        Assert.Equal("Ctrl+Shift+F", back.Commands.Single().Shortcut);
        Assert.Equal("*.zip;*.jar", back.Archives!.Single().Mask);
        Assert.Equal(ComparisonPriority.Low, back.Files.Priority);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Older_files_with_numeric_enums_and_no_preferences_still_load()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-prefs-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "s.json");
        File.WriteAllText(path, "{ \"Theme\": \"Dark\", \"Whitespace\": 2 }");
        var s = new SettingsStore(path).Load();
        Assert.Equal("Dark", s.Theme);
        Assert.Equal((WhitespaceMode)2, s.Whitespace);
        Assert.True(s.Prefs.TextEditing.AutoIndent);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void The_recent_list_length_follows_the_preference()
    {
        var s = new AppSettings();
        s.Prefs.Metrics.MaxRecentItems = 3;
        for (var i = 0; i < 8; i++) { s.AddRecent(new RecentSession(LaunchKind.TextCompare, "a" + i, "b", null, null, DateTime.UtcNow)); }
        Assert.Equal(3, s.Recent.Count);
        Assert.Equal("a7", s.Recent[0].Left);
    }
}
