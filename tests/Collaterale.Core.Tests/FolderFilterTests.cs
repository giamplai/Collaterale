using Collaterale.Core.Folders;

namespace Collaterale.Core.Tests;

public class FolderFilterTests
{
    private static readonly DateTime T = new(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly FolderOptions Base = new() { IgnoreNameCase = false, DetectRenames = false };

    private static string[] Names(FolderComparison c) => [.. c.Root.Descendants().Where(n => !n.IsDirectory).Select(n => n.RelativePath).OrderBy(x => x)];

    [Fact]
    public void Include_folders_keeps_only_matching_folders_at_any_depth()
    {
        using var f = new FolderFixture();
        foreach (var side in new[] { "L", "R" })
        {
            f.Write(side, "src/a.cs", "x", T); f.Write(side, "docs/b.md", "x", T); f.Write(side, "src/deep/src/c.cs", "x", T); f.Write(side, "top.txt", "x", T);
        }

        var c = FolderComparer.Compare(f.Left, f.Right, Base with { IncludeFolders = "src" });
        Assert.Equal(["src/a.cs", "top.txt"], Names(c));       // "deep" does not match "src", so it is not entered
        var c2 = FolderComparer.Compare(f.Left, f.Right, Base with { IncludeFolders = "src;deep" });
        Assert.Contains("src/deep/src/c.cs", Names(c2));
    }

    [Fact]
    public void Size_filters_hide_files_on_both_sides()
    {
        using var f = new FolderFixture();
        f.Write("L", "tiny.txt", "x", T); f.Write("R", "tiny.txt", "x", T);
        f.Write("L", "big.bin", new string('b', 5000), T); f.Write("R", "big.bin", new string('b', 5000), T);
        var c = FolderComparer.Compare(f.Left, f.Right, Base with { OtherFilters = [new OtherFilter(OtherFilterKind.LargerThan, 1000)] });
        Assert.Equal(["tiny.txt"], Names(c));
        var c2 = FolderComparer.Compare(f.Left, f.Right, Base with { OtherFilters = [new OtherFilter(OtherFilterKind.SmallerThan, 10)] });
        Assert.Equal(["big.bin"], Names(c2));
    }

    [Fact]
    public void A_file_hidden_on_one_side_only_keeps_its_counterpart_visible_and_is_marked()
    {
        using var f = new FolderFixture();
        f.Write("L", "grew.log", "a", T);                       // small on the left
        f.Write("R", "grew.log", new string('x', 5000), T);     // large on the right: the filter hides the right one
        var c = FolderComparer.Compare(f.Left, f.Right, Base with { OtherFilters = [new OtherFilter(OtherFilterKind.LargerThan, 1000)] });
        var n = Assert.Single(c.Root.Descendants());
        Assert.Equal(Newer.Right, n.FilteredSide);
        Assert.Equal(FolderEntryState.Different, n.State);
    }

    [Fact]
    public void An_orphan_that_the_filter_hides_is_simply_gone()
    {
        using var f = new FolderFixture();
        f.Write("L", "only.log", new string('x', 5000), T);
        var c = FolderComparer.Compare(f.Left, f.Right, Base with { OtherFilters = [new OtherFilter(OtherFilterKind.LargerThan, 1000)] });
        Assert.Empty(c.Root.Descendants());
    }

    [Fact]
    public void Date_filters()
    {
        using var f = new FolderFixture();
        f.Write("L", "old.txt", "x", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)); f.Write("R", "old.txt", "x", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        f.Write("L", "new.txt", "x", DateTime.UtcNow); f.Write("R", "new.txt", "x", DateTime.UtcNow);
        Assert.Equal(["new.txt"], Names(FolderComparer.Compare(f.Left, f.Right, Base with { OtherFilters = [new OtherFilter(OtherFilterKind.ModifiedBefore, Date: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc))] })));
        Assert.Equal(["old.txt"], Names(FolderComparer.Compare(f.Left, f.Right, Base with { OtherFilters = [new OtherFilter(OtherFilterKind.NewerThanDays, 30)] })));
        Assert.Equal(["new.txt"], Names(FolderComparer.Compare(f.Left, f.Right, Base with { OtherFilters = [new OtherFilter(OtherFilterKind.OlderThanDays, 30)] })));
        Assert.Equal(["old.txt"], Names(FolderComparer.Compare(f.Left, f.Right, Base with { OtherFilters = [new OtherFilter(OtherFilterKind.ModifiedAfter, Date: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc))] })));
    }

    [Fact]
    public void Attribute_filter_hides_read_only_files()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var f = new FolderFixture();
        var l = f.Write("L", "locked.txt", "x", T); f.Write("R", "locked.txt", "x", T);
        f.Write("L", "free.txt", "x", T); f.Write("R", "free.txt", "x", T);
        File.SetAttributes(l, FileAttributes.ReadOnly);
        File.SetAttributes(Path.Combine(f.Right, "locked.txt"), FileAttributes.ReadOnly);
        Assert.Equal(["free.txt"], Names(FolderComparer.Compare(f.Left, f.Right, Base with { OtherFilters = [new OtherFilter(OtherFilterKind.HasAttribute, Attributes: FileAttributes.ReadOnly)] })));
    }

    [Fact]
    public void Operating_system_files_are_hidden_by_default_and_can_be_shown()
    {
        using var f = new FolderFixture();
        f.Write("L", ".DS_Store", "x", T); f.Write("L", "Thumbs.db", "x", T); f.Write("L", "desktop.ini", "x", T); f.Write("L", "real.txt", "x", T);
        Assert.Equal(["real.txt"], Names(FolderComparer.Compare(f.Left, f.Right, Base)));
        Assert.Equal(4, Names(FolderComparer.Compare(f.Left, f.Right, Base with { ExcludeProtectedFiles = false })).Length);
    }

    [Fact]
    public void Folders_left_empty_by_other_filters_disappear()
    {
        using var f = new FolderFixture();
        f.Write("L", "logs/a.log", new string('x', 5000), T); f.Write("R", "logs/a.log", new string('x', 5000), T);
        f.Write("L", "keep/b.txt", "x", T); f.Write("R", "keep/b.txt", "x", T);
        var c = FolderComparer.Compare(f.Left, f.Right, Base with { OtherFilters = [new OtherFilter(OtherFilterKind.LargerThan, 1000)] });
        Assert.DoesNotContain(c.Root.Children, n => n.Name == "logs");
    }
}

public class FolderSizeTests
{
    private static readonly DateTime T = new(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Folders_report_the_total_size_of_what_is_shown_on_each_side_including_orphan_folders()
    {
        using var f = new FolderFixture();
        f.Write("L", "both/a.txt", "12345", T); f.Write("R", "both/a.txt", "123", T);
        f.Write("L", "both/deep/b.txt", "1234567890", T);
        f.Write("L", "orphan/c.txt", "123456", T); f.Write("L", "orphan/d.txt", "1234", T);
        var c = FolderComparer.Compare(f.Left, f.Right, new FolderOptions { IgnoreNameCase = false, DetectRenames = false });
        var both = c.Root.Children.Single(n => n.Name == "both");
        Assert.Equal((15, 3), (both.LeftBytes, both.RightBytes));
        var orphan = c.Root.Children.Single(n => n.Name == "orphan");
        Assert.Equal((10, 0), (orphan.LeftBytes, orphan.RightBytes));
        Assert.Equal(25, c.Root.LeftBytes);
    }
}
