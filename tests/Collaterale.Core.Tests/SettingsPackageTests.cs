using Collaterale.Core.Cli;
using Collaterale.Core.Settings;

namespace Collaterale.Core.Tests;

public class SettingsPackageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "collaterale-pkg-" + Guid.NewGuid().ToString("N")[..8]);

    public SettingsPackageTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void An_exported_package_carries_the_preferences_colors_presets_and_sessions_but_not_the_history()
    {
        var src = new AppSettings { Theme = "Dark" };
        src.Prefs.Backup.Mode = BackupMode.SameFolderNumbered;
        src.Colors.Orphan = "#112233";
        src.FilterPresets.Add(new Collaterale.Core.Folders.FilterPreset("Code", "*.cs", "", "", ""));
        src.SaveSession(new SavedSession { Name = "S", Kind = LaunchKind.FolderCompare, Left = "/a", Right = "/b" });
        src.SaveWorkspace(new Workspace { Name = "W", Sessions = [new SavedSession { Name = "x" }] });
        src.AddRecent(new RecentSession(LaunchKind.TextCompare, "/secret/a", "/secret/b", null, null, DateTime.UtcNow));

        var file = Path.Combine(_dir, "mine.cpkg");
        SettingsPackage.Export(src, file);

        var dst = new AppSettings();
        var summary = SettingsPackage.Import(file, dst);
        Assert.Equal("Dark", dst.Theme);
        Assert.Equal(BackupMode.SameFolderNumbered, dst.Prefs.Backup.Mode);
        Assert.Equal("#112233", dst.Colors.Orphan);
        Assert.Equal("Code", dst.FilterPresets.Single().Name);
        Assert.Equal("S", dst.Sessions.Single().Name);
        Assert.Equal("W", dst.Workspaces.Single().Name);
        Assert.Empty(dst.Recent);                                   // paths you opened are private: never exported
        Assert.Contains("1 session", summary);
        Assert.Contains("1 workspace", summary);
    }

    [Fact]
    public void Importing_keeps_sessions_the_package_does_not_mention_and_replaces_those_it_does()
    {
        var src = new AppSettings();
        src.SaveSession(new SavedSession { Name = "Shared", Left = "/new" });
        var file = Path.Combine(_dir, "p.cpkg");
        SettingsPackage.Export(src, file);

        var dst = new AppSettings();
        dst.SaveSession(new SavedSession { Name = "shared", Left = "/old" });
        dst.SaveSession(new SavedSession { Name = "Mine", Left = "/mine" });
        SettingsPackage.Import(file, dst);
        Assert.Equal(["Mine", "Shared"], dst.Sessions.Select(s => s.Name).Order());
        Assert.Equal("/new", dst.FindSession("shared")!.Left);
    }

    [Fact]
    public void A_file_that_is_not_a_package_is_refused_with_a_message()
    {
        var bad = Path.Combine(_dir, "bad.cpkg");
        File.WriteAllText(bad, "this is not a zip");
        var ex = Assert.Throws<InvalidDataException>(() => SettingsPackage.Import(bad, new AppSettings()));
        Assert.Contains("settings package", ex.Message);
        Assert.Throws<InvalidDataException>(() => SettingsPackage.Import(Path.Combine(_dir, "missing.cpkg"), new AppSettings()));
    }

    [Fact]
    public void A_package_without_the_settings_entry_is_refused()
    {
        var zipPath = Path.Combine(_dir, "empty.cpkg");
        using (var z = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create)) { z.CreateEntry("other.txt"); }
        Assert.Throws<InvalidDataException>(() => SettingsPackage.Import(zipPath, new AppSettings()));
    }
}
