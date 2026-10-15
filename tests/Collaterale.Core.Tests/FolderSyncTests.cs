using Collaterale.Core.Folders;

namespace Collaterale.Core.Tests;

/// <summary>Synchronous progress sink (Progress&lt;T&gt; would post to a synchronization context).</summary>
internal sealed class SyncProgress : IProgress<SyncStep>
{
    public List<SyncStep> Steps { get; } = [];

    public void Report(SyncStep value) => Steps.Add(value);
}

public class FolderSyncTests
{
    private static readonly FolderOptions O = new() { IgnoreNameCase = false };
    private static readonly DateTime T = new(2024, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>left: new.txt, newer.txt(newer), older.txt(older), same.txt, dir/inner.txt ; right: gone.txt, newer.txt, older.txt, same.txt</summary>
    private static FolderFixture Scenario()
    {
        var f = new FolderFixture();
        f.Write("L", "new.txt", "n", T);
        f.Write("L", "newer.txt", "left-new", T.AddHours(2)); f.Write("R", "newer.txt", "old", T);
        f.Write("L", "older.txt", "old", T); f.Write("R", "older.txt", "right-new", T.AddHours(2));
        f.Write("L", "same.txt", "s", T); f.Write("R", "same.txt", "s", T);
        f.Write("L", "dir/inner.txt", "i", T);
        f.Write("R", "gone.txt", "g", T);
        return f;
    }

    private static string[] Paths(SyncPlan p, SyncAction a) => [.. p.Steps.Where(s => s.Action == a).Select(s => s.RelativePath).OrderBy(x => x)];

    [Fact]
    public void Update_right_copies_new_and_newer_but_never_deletes()
    {
        using var f = Scenario();
        var p = FolderSync.Plan(FolderComparer.Compare(f.Left, f.Right, O), SyncMode.UpdateRight);
        Assert.Equal(["dir/inner.txt", "new.txt", "newer.txt"], Paths(p, SyncAction.CopyToRight));
        Assert.Equal(["dir"], Paths(p, SyncAction.CreateFolderRight));
        Assert.Equal(0, p.Deletes);
        Assert.Contains(p.Skipped, s => s.RelativePath == "older.txt"); // destination is newer: protected
    }

    [Fact]
    public void Mirror_to_right_makes_right_identical_and_deletes_extras()
    {
        using var f = Scenario();
        var c = FolderComparer.Compare(f.Left, f.Right, O);
        var p = FolderSync.Plan(c, SyncMode.MirrorToRight);
        Assert.Equal(["gone.txt"], Paths(p, SyncAction.DeleteRight));
        Assert.Contains("older.txt", Paths(p, SyncAction.CopyToRight)); // mirror overrides "newer wins"

        var result = FolderSync.Execute(c, p);
        Assert.True(result.Success);
        var after = FolderComparer.Compare(f.Left, f.Right, O);
        Assert.True(after.Summary.AreEqual);
        Assert.False(File.Exists(Path.Combine(f.Right, "gone.txt")));
    }

    [Fact]
    public void Two_way_sends_each_newer_file_to_the_other_side_and_orphans_both_ways()
    {
        using var f = Scenario();
        var c = FolderComparer.Compare(f.Left, f.Right, O);
        var p = FolderSync.Plan(c, SyncMode.TwoWay);
        Assert.Contains("newer.txt", Paths(p, SyncAction.CopyToRight));
        Assert.Contains("older.txt", Paths(p, SyncAction.CopyToLeft));
        Assert.Contains("gone.txt", Paths(p, SyncAction.CopyToLeft));
        Assert.Equal(0, p.Deletes);

        FolderSync.Execute(c, p);
        Assert.True(FolderComparer.Compare(f.Left, f.Right, O).Summary.AreEqual);
        Assert.Equal("right-new", File.ReadAllText(Path.Combine(f.Left, "older.txt")));
    }

    [Fact]
    public void Same_time_but_different_content_is_skipped_not_guessed()
    {
        using var f = new FolderFixture();
        f.Write("L", "x.txt", "short", T); f.Write("R", "x.txt", "much longer", T);
        var p = FolderSync.Plan(FolderComparer.Compare(f.Left, f.Right, O), SyncMode.TwoWay);
        Assert.True(p.IsEmpty);
        Assert.Single(p.Skipped);
    }

    [Fact]
    public void Copies_preserve_modification_time_and_overwrite_read_only_files()
    {
        using var f = new FolderFixture();
        f.Write("L", "a.txt", "new content", T.AddHours(1));
        var target = f.Write("R", "a.txt", "old", T);
        File.SetAttributes(target, FileAttributes.ReadOnly);

        var c = FolderComparer.Compare(f.Left, f.Right, O);
        var r = FolderSync.Execute(c, FolderSync.Plan(c, SyncMode.UpdateRight));
        Assert.True(r.Success, string.Join(';', r.Failures.Select(x => x.Error)));
        Assert.Equal("new content", File.ReadAllText(target));
        Assert.Equal(T.AddHours(1), File.GetLastWriteTimeUtc(target));
    }

    [Fact]
    public void Selection_based_plans_copy_and_delete_exactly_what_was_chosen()
    {
        using var f = Scenario();
        var c = FolderComparer.Compare(f.Left, f.Right, O);
        var dir = c.Root.Children.Single(n => n.Name == "dir");

        var copy = FolderSync.PlanFor(c, [dir], SyncAction.CopyToRight);
        Assert.Equal(["dir/inner.txt"], Paths(copy, SyncAction.CopyToRight));
        Assert.Equal(["dir"], Paths(copy, SyncAction.CreateFolderRight));

        var del = FolderSync.PlanFor(c, [c.Root.Children.Single(n => n.Name == "gone.txt")], SyncAction.DeleteRight);
        FolderSync.Execute(c, del);
        Assert.False(File.Exists(Path.Combine(f.Right, "gone.txt")));

        var impossible = FolderSync.PlanFor(c, [c.Root.Children.Single(n => n.Name == "new.txt")], SyncAction.CopyToLeft);
        Assert.True(impossible.IsEmpty);   // new.txt does not exist on the right, so there is nothing to copy left
        Assert.Single(impossible.Skipped);
    }

    [Fact]
    public void Deleting_a_folder_removes_it_after_its_children_are_ordered_first()
    {
        using var f = new FolderFixture();
        f.Write("R", "gone/deep/file.txt", "x");
        var c = FolderComparer.Compare(f.Left, f.Right, O);
        var plan = FolderSync.Plan(c, SyncMode.MirrorToRight);
        Assert.True(plan.Steps.All(s => s.IsDelete));
        Assert.Equal(plan.Steps.Select(s => s.RelativePath).OrderByDescending(p => p.Count(ch => ch == '/')).ToList(), plan.Steps.Select(s => s.RelativePath).ToList());
        Assert.True(FolderSync.Execute(c, plan).Success);
        Assert.False(Directory.Exists(Path.Combine(f.Right, "gone")));
    }

    [Fact]
    public void A_step_that_would_escape_the_root_is_refused()
    {
        using var f = new FolderFixture();
        f.Write("L", "a.txt", "x");
        var c = FolderComparer.Compare(f.Left, f.Right, O);
        var evil = new SyncPlan([new SyncStep(SyncAction.CopyToRight, "../../escaped.txt", false, 1, "test")], []);
        var r = FolderSync.Execute(c, evil);
        Assert.False(r.Success);
        Assert.Contains("outside", r.Failures[0].Error);
    }

    [Fact]
    public void Execution_can_be_cancelled_and_reports_progress()
    {
        using var f = Scenario();
        var c = FolderComparer.Compare(f.Left, f.Right, O);
        var plan = FolderSync.Plan(c, SyncMode.UpdateRight);
        var seen = new SyncProgress();
        FolderSync.Execute(c, plan, seen);
        Assert.Equal(plan.Steps, seen.Steps); // every step is reported, in order
        var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => FolderSync.Execute(c, plan, null, cts.Token));
    }
}

public class SyncBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "collaterale-sb-" + Guid.NewGuid().ToString("N")[..8]);

    public SyncBackupTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "L"));
        Directory.CreateDirectory(Path.Combine(_root, "R"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Put(string side, string name, string text) => File.WriteAllText(Path.Combine(_root, side, name), text);

    [Fact]
    public void Overwritten_and_deleted_files_are_backed_up_but_new_ones_are_not()
    {
        Put("L", "changed.txt", "new");
        Put("R", "changed.txt", "old");
        Put("L", "added.txt", "a");
        Put("R", "gone.txt", "bye");
        var c = Collaterale.Core.Folders.FolderComparer.Compare(Path.Combine(_root, "L"), Path.Combine(_root, "R"), new Collaterale.Core.Folders.FolderOptions { Criteria = Collaterale.Core.Folders.CompareCriteria.Content });
        var plan = Collaterale.Core.Folders.FolderSync.Plan(c, Collaterale.Core.Folders.SyncMode.MirrorToRight);
        var seen = new List<string>();
        var result = Collaterale.Core.Folders.FolderSync.Execute(c, plan, backup: (path, root) =>
        {
            seen.Add(Path.GetFileName(path));
            Collaterale.Core.Settings.Backups.Create(path, new Collaterale.Core.Settings.BackupPreferences { Mode = Collaterale.Core.Settings.BackupMode.SameFolderSuffix }, root);
        });

        Assert.True(result.Success);
        Assert.Equal(["changed.txt", "gone.txt"], seen.Order());
        Assert.Equal("old", File.ReadAllText(Path.Combine(_root, "R", "changed.txt.bak")));
        Assert.Equal("bye", File.ReadAllText(Path.Combine(_root, "R", "gone.txt.bak")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(_root, "R", "changed.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "R", "added.txt.bak")));
    }
}

public class OpticalCopyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "collaterale-oc-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Func<string, bool> _original = FolderSync.IsOptical;

    public OpticalCopyTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "L"));
        Directory.CreateDirectory(Path.Combine(_root, "R"));
    }

    public void Dispose()
    {
        FolderSync.IsOptical = _original;
        FolderSync.RemoveReadOnlyFromOptical = true;
        foreach (var f in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) { File.SetAttributes(f, FileAttributes.Normal); }
        Directory.Delete(_root, recursive: true);
    }

    private bool CopyReadOnlyFile()
    {
        var src = Path.Combine(_root, "L", "disc.txt");
        File.WriteAllText(src, "x");
        File.SetAttributes(src, FileAttributes.ReadOnly);
        var c = FolderComparer.Compare(Path.Combine(_root, "L"), Path.Combine(_root, "R"));
        FolderSync.Execute(c, FolderSync.Plan(c, SyncMode.UpdateRight));
        return File.GetAttributes(Path.Combine(_root, "R", "disc.txt")).HasFlag(FileAttributes.ReadOnly);
    }

    [Fact]
    public void Copies_from_an_optical_disc_lose_the_read_only_flag_when_asked()
    {
        FolderSync.IsOptical = _ => true;
        Assert.False(CopyReadOnlyFile());
    }

    [Fact]
    public void The_flag_is_kept_when_the_preference_is_off_or_the_source_is_not_optical()
    {
        FolderSync.IsOptical = _ => true;
        FolderSync.RemoveReadOnlyFromOptical = false;
        Assert.True(CopyReadOnlyFile());
    }

    [Fact]
    public void A_normal_disk_is_not_optical()
    {
        Assert.False(_original(_root));
    }
}
