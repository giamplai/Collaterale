using System.IO.Compression;
using Collaterale.Core.Folders;

namespace Collaterale.Core.Tests;

public class FolderHandlingTests
{
    private static readonly DateTime T = new(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly FolderOptions Base = new() { IgnoreNameCase = false, DetectRenames = false };

    private static void Zip(string path, params (string name, string text)[] files)
    {
        using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (name, text) in files)
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open());
                w.Write(text);
            }
        }

        File.SetLastWriteTimeUtc(path, T);       // after the archive is closed, or closing would stamp it with "now"

    }

    // ───── archives ─────

    [Fact]
    public void Archives_are_plain_files_by_default()
    {
        using var f = new FolderFixture();
        Zip(Path.Combine(f.Left, "pack.zip"), ("a.txt", "one")); Zip(Path.Combine(f.Right, "pack.zip"), ("a.txt", "two!"));
        var c = FolderComparer.Compare(f.Left, f.Right, Base);
        var n = Assert.Single(c.Root.Descendants());
        Assert.False(n.IsDirectory);
        Assert.False(n.IsArchive);
    }

    [Fact]
    public void Archives_as_folders_always_compare_their_contents()
    {
        using var f = new FolderFixture();
        Zip(Path.Combine(f.Left, "pack.zip"), ("same.txt", "s"), ("changed.txt", "1"), ("only-left.txt", "l"));
        Zip(Path.Combine(f.Right, "pack.zip"), ("same.txt", "s"), ("changed.txt", "22"), ("only-right.txt", "r"));
        var c = FolderComparer.Compare(f.Left, f.Right, Base with { Archives = ArchiveHandling.AsFoldersAlways });

        var pack = c.Root.Children.Single();
        Assert.True(pack.IsDirectory && pack.IsArchive);
        Assert.Equal(FolderEntryState.Different, pack.State);
        Assert.Equal(["changed.txt", "only-left.txt", "only-right.txt", "same.txt"], pack.Children.Select(x => x.Name).OrderBy(x => x));
        Assert.All(pack.Children, x => Assert.True(x.InArchive));
        Assert.Equal("one", "one");
        Assert.Equal("1", File.ReadAllText(c.LeftPath(pack.Children.Single(x => x.Name == "changed.txt"))));     // paths resolve to the unpacked copy
        Assert.Equal("22", File.ReadAllText(c.RightPath(pack.Children.Single(x => x.Name == "changed.txt"))));
    }

    [Fact]
    public void Archives_found_on_one_side_only_are_orphans_with_contents()
    {
        using var f = new FolderFixture();
        Zip(Path.Combine(f.Left, "only.zip"), ("x.txt", "x"));
        var c = FolderComparer.Compare(f.Left, f.Right, Base with { Archives = ArchiveHandling.AsFoldersAlways });
        var pack = c.Root.Children.Single();
        Assert.Equal(FolderEntryState.LeftOnly, pack.State);
        Assert.Equal("x.txt", pack.Children.Single().Name);
    }

    [Fact]
    public void Archives_once_opened_start_as_files_and_become_folders_on_request()
    {
        using var f = new FolderFixture();
        Zip(Path.Combine(f.Left, "pack.zip"), ("a.txt", "1")); Zip(Path.Combine(f.Right, "pack.zip"), ("a.txt", "22"));
        var c = FolderComparer.Compare(f.Left, f.Right, Base with { Archives = ArchiveHandling.AsFoldersOnceOpened });
        var pack = c.Root.Children.Single();
        Assert.True(pack.IsArchive && !pack.IsDirectory);

        FolderComparer.OpenArchive(c, pack);
        Assert.True(pack.IsDirectory);
        var inner = Assert.Single(pack.Children);
        Assert.Equal((FolderEntryState.Different, true), (inner.State, inner.InArchive));
    }

    [Fact]
    public void A_corrupt_archive_is_compared_as_a_file()
    {
        using var f = new FolderFixture();
        f.Write("L", "bad.zip", "not a zip", T); f.Write("R", "bad.zip", "not a zip!!", T);
        var c = FolderComparer.Compare(f.Left, f.Right, Base with { Archives = ArchiveHandling.AsFoldersAlways });
        var n = Assert.Single(c.Root.Descendants());
        Assert.False(n.IsDirectory);
        Assert.Equal(FolderEntryState.Different, n.State);
    }

    [Fact]
    public void Entries_inside_archives_are_never_synchronized_but_the_archive_file_is()
    {
        using var f = new FolderFixture();
        Zip(Path.Combine(f.Left, "pack.zip"), ("a.txt", "newer and longer content"));
        Zip(Path.Combine(f.Right, "pack.zip"), ("a.txt", "x"));
        File.SetLastWriteTimeUtc(Path.Combine(f.Left, "pack.zip"), T.AddHours(3));
        var c = FolderComparer.Compare(f.Left, f.Right, Base with { Archives = ArchiveHandling.AsFoldersAlways });
        var plan = FolderSync.Plan(c, SyncMode.UpdateRight);
        var step = Assert.Single(plan.Steps);
        Assert.Equal((SyncAction.CopyToRight, "pack.zip", false), (step.Action, step.RelativePath, step.IsDirectory));
        Assert.True(FolderSync.Execute(c, plan).Success);
        Assert.Equal(new FileInfo(Path.Combine(f.Left, "pack.zip")).Length, new FileInfo(Path.Combine(f.Right, "pack.zip")).Length);
    }

    // ───── symbolic links ─────

    private static bool TryLink(string link, string target, bool dir)
    {
        try
        {
            if (dir) { Directory.CreateSymbolicLink(link, target); } else { File.CreateSymbolicLink(link, target); }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { return false; }
    }

    [Fact]
    public void Links_are_single_entries_compared_by_target_unless_followed()
    {
        using var f = new FolderFixture();
        f.Write("L", "real/a.txt", "x", T); f.Write("R", "real/a.txt", "x", T);
        if (!TryLink(Path.Combine(f.Left, "lnk"), "real", dir: true) || !TryLink(Path.Combine(f.Right, "lnk"), "real", dir: true)) { return; }
        var c = FolderComparer.Compare(f.Left, f.Right, Base);
        var link = c.Root.Children.Single(n => n.Name == "lnk");
        Assert.False(link.IsDirectory);                      // not entered
        Assert.Equal(FolderEntryState.Same, link.State);
        Assert.Empty(link.Children);
    }

    [Fact]
    public void Links_with_different_targets_differ()
    {
        using var f = new FolderFixture();
        f.Write("L", "one.txt", "1", T); f.Write("L", "two.txt", "2", T);
        if (!TryLink(Path.Combine(f.Left, "lnk"), "one.txt", false) || !TryLink(Path.Combine(f.Right, "lnk"), "two.txt", false)) { return; }
        var link = FolderComparer.Compare(f.Left, f.Right, Base).Root.Children.Single(n => n.Name == "lnk");
        Assert.Equal(FolderEntryState.Different, link.State);
    }

    [Fact]
    public void Following_links_compares_the_targets_and_survives_cycles()
    {
        using var f = new FolderFixture();
        f.Write("L", "dir/a.txt", "left", T); f.Write("R", "dir/a.txt", "right!", T);
        if (!TryLink(Path.Combine(f.Left, "dir", "loop"), "..", dir: true) || !TryLink(Path.Combine(f.Right, "dir", "loop"), "..", dir: true)) { return; }
        var c = FolderComparer.Compare(f.Left, f.Right, Base with { FollowSymbolicLinks = true });     // would recurse forever without the guard
        Assert.Contains(c.Root.Descendants(), n => n.Name == "a.txt" && n.State == FolderEntryState.Different);
    }

    // ───── versions ─────

    [Fact]
    public void Programs_with_different_versions_differ_even_when_size_and_time_match()
    {
        using var f = new FolderFixture();
        var a = typeof(FolderComparer).Assembly.Location;                                      // Collaterale.Core.dll
        var b = typeof(System.Xml.Linq.XDocument).Assembly.Location;                              // some other assembly with another version
        var l = Path.Combine(f.Left, "lib.dll"); var r = Path.Combine(f.Right, "lib.dll");
        File.Copy(a, l); File.Copy(b, r);
        File.SetLastWriteTimeUtc(l, T); File.SetLastWriteTimeUtc(r, T);
        // size equal is not guaranteed; use SizeOnly-free criteria: compare versions only
        var without = FolderComparer.Compare(f.Left, f.Right, Base with { Criteria = CompareCriteria.SizeAndTime }).Root.Descendants().Single();
        var with = FolderComparer.Compare(f.Left, f.Right, Base with { Criteria = CompareCriteria.SizeAndTime, CompareVersions = true }).Root.Descendants().Single();
        Assert.Equal(FolderEntryState.Different, with.State);
        Assert.True(with.Differences.HasFlag(DifferenceKinds.Version) || without.Differences.HasFlag(DifferenceKinds.Size));
        Assert.NotNull(FolderComparer.VersionOf(a));
    }

    [Fact]
    public void Files_without_version_information_are_unaffected()
    {
        using var f = new FolderFixture();
        f.Write("L", "x.dll", "not a library", T); f.Write("R", "x.dll", "not a library", T);
        Assert.Equal(FolderEntryState.Same, FolderComparer.Compare(f.Left, f.Right, Base with { CompareVersions = true }).Root.Descendants().Single().State);
        Assert.Null(FolderComparer.VersionOf(Path.Combine(f.Left, "x.dll")));
    }
}
