using Collaterale.Core.Folders;

namespace Collaterale.Core.Tests;

public class RenameDetectionTests
{
    private static readonly FolderOptions O = new() { IgnoreNameCase = false };
    private static readonly DateTime T = new(2024, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Same_content_under_a_new_name_is_paired()
    {
        using var f = new FolderFixture();
        f.Write("L", "old-name.txt", "payload", T);
        f.Write("R", "docs/new-name.txt", "payload", T);
        var c = FolderComparer.Compare(f.Left, f.Right, O);
        var l = c.Root.Descendants().Single(n => n.Name == "old-name.txt");
        var r = c.Root.Descendants().Single(n => n.Name == "new-name.txt");
        Assert.Same(r, l.MovePartner);
        Assert.Same(l, r.MovePartner);
    }

    [Fact]
    public void Different_content_of_equal_size_is_not_paired()
    {
        using var f = new FolderFixture();
        f.Write("L", "a.txt", "aaaa", T);
        f.Write("R", "b.txt", "bbbb", T);
        var c = FolderComparer.Compare(f.Left, f.Right, O);
        Assert.DoesNotContain(c.Root.Descendants(), n => n.IsMoved);
    }

    [Fact]
    public void Disabled_option_pairs_nothing()
    {
        using var f = new FolderFixture();
        f.Write("L", "a.txt", "same", T);
        f.Write("R", "b.txt", "same", T);
        var c = FolderComparer.Compare(f.Left, f.Right, new FolderOptions { IgnoreNameCase = false, DetectRenames = false });
        Assert.DoesNotContain(c.Root.Descendants(), n => n.IsMoved);
    }

    [Fact]
    public void Prefers_the_candidate_with_the_same_file_name()
    {
        using var f = new FolderFixture();
        f.Write("L", "x/report.txt", "same", T);
        f.Write("R", "y/other.txt", "same", T);
        f.Write("R", "z/report.txt", "same", T);
        var c = FolderComparer.Compare(f.Left, f.Right, O);
        var l = c.Root.Descendants().Single(n => n.State == FolderEntryState.LeftOnly && !n.IsDirectory);
        Assert.Equal("z/report.txt", l.MovePartner!.RelativePath);
    }

    [Fact]
    public void Mirror_to_right_renames_instead_of_copy_and_delete()
    {
        using var f = new FolderFixture();
        f.Write("L", "sub/new.txt", "payload", T);
        f.Write("R", "old.txt", "payload", T);
        var c = FolderComparer.Compare(f.Left, f.Right, O);
        var p = FolderSync.Plan(c, SyncMode.MirrorToRight);
        var step = Assert.Single(p.Steps, s => s.Action == SyncAction.MoveRight);
        Assert.Equal("old.txt", step.RelativePath);
        Assert.Equal("sub/new.txt", step.MoveTo);
        Assert.DoesNotContain(p.Steps, s => s.Action is SyncAction.CopyToRight or SyncAction.DeleteRight);

        Assert.True(FolderSync.Execute(c, p).Success);
        Assert.True(FolderComparer.Compare(f.Left, f.Right, O).Summary.AreEqual);
        Assert.False(File.Exists(Path.Combine(f.Right, "old.txt")));
    }

    [Fact]
    public void Mirror_to_left_renames_the_left()
    {
        using var f = new FolderFixture();
        f.Write("L", "old.txt", "payload", T);
        f.Write("R", "new.txt", "payload", T);
        var c = FolderComparer.Compare(f.Left, f.Right, O);
        var p = FolderSync.Plan(c, SyncMode.MirrorToLeft);
        Assert.Single(p.Steps, s => s.Action == SyncAction.MoveLeft);
        Assert.True(FolderSync.Execute(c, p).Success);
        Assert.True(File.Exists(Path.Combine(f.Left, "new.txt")));
        Assert.False(File.Exists(Path.Combine(f.Left, "old.txt")));
    }

    [Fact]
    public void Update_right_still_copies_the_new_name()
    {
        using var f = new FolderFixture();
        f.Write("L", "new.txt", "payload", T);
        f.Write("R", "old.txt", "payload", T);
        var p = FolderSync.Plan(FolderComparer.Compare(f.Left, f.Right, O), SyncMode.UpdateRight);
        Assert.Single(p.Steps, s => s.Action == SyncAction.CopyToRight);
        Assert.DoesNotContain(p.Steps, s => s.Action == SyncAction.MoveRight);
    }

    [Fact]
    public void Two_way_leaves_renames_to_the_user()
    {
        using var f = new FolderFixture();
        f.Write("L", "a.txt", "payload", T);
        f.Write("R", "b.txt", "payload", T);
        var p = FolderSync.Plan(FolderComparer.Compare(f.Left, f.Right, O), SyncMode.TwoWay);
        Assert.Empty(p.Steps);
        Assert.Single(p.Skipped);
    }
}
