using Collaterale.Core.Settings;

namespace Collaterale.Core.Tests;

public class BackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "collaterale-bk-" + Guid.NewGuid().ToString("N")[..8]);

    public BackupTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File_(string name, string content)
    {
        var p = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    private static readonly DateTime Now = new(2026, 1, 31, 14, 25, 30);

    [Fact]
    public void Off_and_a_missing_file_make_no_backup()
    {
        var f = File_("a.txt", "x");
        Assert.Null(Backups.Create(f, new BackupPreferences { Mode = BackupMode.Off }));
        Assert.Null(Backups.Create(Path.Combine(_dir, "none.txt"), new BackupPreferences()));
        Assert.Equal(["a.txt"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void A_suffix_backup_sits_next_to_the_file_and_is_replaced_each_time()
    {
        var f = File_("a.txt", "one");
        var p = new BackupPreferences { Mode = BackupMode.SameFolderSuffix, Suffix = ".old" };
        var b = Backups.Create(f, p)!;
        Assert.Equal(f + ".old", b);
        Assert.Equal("one", File.ReadAllText(b));
        File.WriteAllText(f, "two");
        Backups.Create(f, p);
        Assert.Equal("two", File.ReadAllText(f + ".old"));
    }

    [Fact]
    public void Numbered_backups_count_up_and_old_ones_are_dropped()
    {
        var f = File_("a.txt", "v0");
        var p = new BackupPreferences { Mode = BackupMode.SameFolderNumbered, KeepVersions = 2 };
        for (var i = 1; i <= 4; i++)
        {
            Assert.Equal(f + ".bak" + i, Backups.Create(f, p));
            File.WriteAllText(f, "v" + i);
        }

        var left = Directory.GetFiles(_dir, "a.txt.bak*").Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["a.txt.bak3", "a.txt.bak4"], left);                 // only the 2 newest remain
        Assert.Equal("v2", File.ReadAllText(f + ".bak3"));
    }

    [Fact]
    public void Timestamped_backups_use_the_clock_and_keep_the_newest()
    {
        var f = File_("a.txt", "x");
        var p = new BackupPreferences { Mode = BackupMode.SameFolderTimestamp, KeepVersions = 2 };
        Assert.Equal(f + ".20260131-142530.bak", Backups.Create(f, p, now: Now));
        Backups.Create(f, p, now: Now.AddMinutes(1));
        Backups.Create(f, p, now: Now.AddMinutes(2));
        var left = Directory.GetFiles(_dir, "a.txt.*.bak").Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["a.txt.20260131-142630.bak", "a.txt.20260131-142730.bak"], left);
    }

    [Fact]
    public void Zero_keeps_every_version()
    {
        var f = File_("a.txt", "x");
        var p = new BackupPreferences { Mode = BackupMode.SameFolderNumbered, KeepVersions = 0 };
        for (var i = 0; i < 6; i++) { Backups.Create(f, p); }
        Assert.Equal(6, Directory.GetFiles(_dir, "a.txt.bak*").Length);
    }

    [Fact]
    public void A_backup_folder_keeps_the_layout_below_the_root()
    {
        var f = File_("src/sub/a.txt", "x");
        var store = Path.Combine(_dir, "store");
        var p = new BackupPreferences { Mode = BackupMode.BackupFolder, Folder = store };
        var b = Backups.Create(f, p, root: _dir)!;
        Assert.Equal(Path.Combine(store, "src", "sub", "a.txt"), b);
        Assert.Equal("x", File.ReadAllText(b));
        // with no root only the file name is used
        var c = Backups.Create(f, p)!;
        Assert.Equal(Path.Combine(store, "a.txt"), c);
    }

    [Fact]
    public void A_backup_folder_that_is_not_set_makes_no_backup()
    {
        var f = File_("a.txt", "x");
        Assert.Null(Backups.Create(f, new BackupPreferences { Mode = BackupMode.BackupFolder, Folder = "" }));
    }

    [Fact]
    public void Backups_never_throw_when_the_target_cannot_be_written()
    {
        var f = File_("a.txt", "x");
        var blocked = File_("blocked", "i am a file, not a folder");
        var p = new BackupPreferences { Mode = BackupMode.BackupFolder, Folder = Path.Combine(blocked, "inside") };
        Assert.Null(Backups.Create(f, p));
    }
}
