using Collaterale.Core.Folders;

namespace Collaterale.Core.Tests;

public sealed class FolderFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "collaterale-fc-" + Guid.NewGuid().ToString("N")[..8]);
    public string Left => Path.Combine(Root, "L");
    public string Right => Path.Combine(Root, "R");

    public FolderFixture() { Directory.CreateDirectory(Left); Directory.CreateDirectory(Right); }

    public string Write(string side, string rel, string content, DateTime? time = null)
    {
        var p = Path.Combine(side == "L" ? Left : Right, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        File.SetLastWriteTimeUtc(p, time ?? new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        return p;
    }

    public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } }
}

public class FolderComparerTests
{
    private static FolderNode Find(FolderComparison c, string rel) => c.Root.Descendants().Single(n => n.RelativePath == rel);

    private static readonly FolderOptions CaseSensitive = new() { IgnoreNameCase = false };

    [Fact]
    public void Classifies_same_different_and_orphans()
    {
        using var f = new FolderFixture();
        f.Write("L", "same.txt", "x"); f.Write("R", "same.txt", "x");
        f.Write("L", "diff.txt", "short"); f.Write("R", "diff.txt", "much longer");
        f.Write("L", "only-left.txt", "l");
        f.Write("R", "only-right.txt", "r");

        var c = FolderComparer.Compare(f.Left, f.Right, CaseSensitive);
        Assert.Equal(FolderEntryState.Same, Find(c, "same.txt").State);
        Assert.Equal(FolderEntryState.Different, Find(c, "diff.txt").State);
        Assert.Equal(FolderEntryState.LeftOnly, Find(c, "only-left.txt").State);
        Assert.Equal(FolderEntryState.RightOnly, Find(c, "only-right.txt").State);
        Assert.Equal(new FolderSummary(1, 1, 1, 1, 0), c.Summary);
        Assert.False(c.Summary.AreEqual);
    }

    [Fact]
    public void Time_tolerance_and_newer_side()
    {
        using var f = new FolderFixture();
        var t = new DateTime(2024, 5, 5, 10, 0, 0, DateTimeKind.Utc);
        f.Write("L", "a.txt", "x", t); f.Write("R", "a.txt", "x", t.AddSeconds(1));      // within 2 s tolerance
        f.Write("L", "b.txt", "x", t.AddMinutes(5)); f.Write("R", "b.txt", "x", t);        // left is newer
        f.Write("L", "c.txt", "x", t); f.Write("R", "c.txt", "x", t.AddHours(1));          // right is newer

        var c = FolderComparer.Compare(f.Left, f.Right, CaseSensitive);
        Assert.Equal(FolderEntryState.Same, Find(c, "a.txt").State);
        Assert.Equal((FolderEntryState.Different, Newer.Left), (Find(c, "b.txt").State, Find(c, "b.txt").Newer));
        Assert.Equal((FolderEntryState.Different, Newer.Right), (Find(c, "c.txt").State, Find(c, "c.txt").Newer));
    }

    [Fact]
    public void Content_criteria_ignores_timestamps_but_sees_same_size_edits()
    {
        using var f = new FolderFixture();
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        f.Write("L", "same.txt", "abc", t); f.Write("R", "same.txt", "abc", t.AddDays(30));   // times differ, bytes equal
        f.Write("L", "edit.txt", "abc", t); f.Write("R", "edit.txt", "abd", t);               // same size and time, different byte

        var byTime = FolderComparer.Compare(f.Left, f.Right, CaseSensitive);
        Assert.Equal(FolderEntryState.Different, Find(byTime, "same.txt").State);
        Assert.Equal(FolderEntryState.Same, Find(byTime, "edit.txt").State); // the weakness of the fast mode

        var byContent = FolderComparer.Compare(f.Left, f.Right, CaseSensitive with { Criteria = CompareCriteria.Content });
        Assert.Equal(FolderEntryState.Same, Find(byContent, "same.txt").State);
        Assert.Equal(FolderEntryState.Different, Find(byContent, "edit.txt").State);
    }

    [Fact]
    public void Nested_folders_aggregate_and_orphan_folders_list_their_children()
    {
        using var f = new FolderFixture();
        f.Write("L", "src/a/x.cs", "1"); f.Write("R", "src/a/x.cs", "1");
        f.Write("L", "src/b/y.cs", "1"); f.Write("R", "src/b/y.cs", "22");
        f.Write("L", "onlyleft/z/deep.txt", "d");

        var c = FolderComparer.Compare(f.Left, f.Right, CaseSensitive);
        Assert.Equal(FolderEntryState.Same, Find(c, "src/a").State);
        Assert.Equal(FolderEntryState.Different, Find(c, "src/b").State);
        Assert.Equal(FolderEntryState.Different, Find(c, "src").State);
        Assert.Equal(FolderEntryState.LeftOnly, Find(c, "onlyleft").State);
        Assert.Equal(FolderEntryState.LeftOnly, Find(c, "onlyleft/z/deep.txt").State);
    }

    [Fact]
    public void Filters_include_exclude_files_and_folders()
    {
        using var f = new FolderFixture();
        foreach (var side in new[] { "L", "R" })
        {
            f.Write(side, "keep.cs", "x"); f.Write(side, "skip.log", "x"); f.Write(side, "bin/out.dll", "x"); f.Write(side, "docs/readme.md", "x");
        }

        var c = FolderComparer.Compare(f.Left, f.Right, CaseSensitive with { Include = "*.cs;*.md", ExcludeFolders = "bin" });
        var paths = c.Root.Descendants().Select(n => n.RelativePath).OrderBy(p => p).ToList();
        Assert.Equal(["docs", "docs/readme.md", "keep.cs"], paths);

        var c2 = FolderComparer.Compare(f.Left, f.Right, CaseSensitive with { ExcludeFiles = "*.log;*.dll" });
        Assert.DoesNotContain(c2.Root.Descendants(), n => n.Name is "skip.log" or "out.dll");
    }

    [Fact]
    public void Folders_emptied_by_filters_are_hidden_unless_asked_otherwise()
    {
        using var f = new FolderFixture();
        f.Write("L", "only-logs/a.log", "x"); f.Write("R", "only-logs/a.log", "x");
        f.Write("L", "k.txt", "x"); f.Write("R", "k.txt", "x");

        Assert.DoesNotContain(FolderComparer.Compare(f.Left, f.Right, CaseSensitive with { ExcludeFiles = "*.log" }).Root.Descendants(), n => n.Name == "only-logs");
        Assert.Contains(FolderComparer.Compare(f.Left, f.Right, CaseSensitive with { ExcludeFiles = "*.log", HideEmptyFolders = false }).Root.Descendants(), n => n.Name == "only-logs");
    }

    [Fact]
    public void File_versus_folder_with_the_same_name_is_a_type_mismatch()
    {
        using var f = new FolderFixture();
        f.Write("L", "thing", "i am a file");
        f.Write("R", "thing/inner.txt", "i am inside a folder");
        var c = FolderComparer.Compare(f.Left, f.Right, CaseSensitive);
        Assert.Equal(FolderEntryState.TypeMismatch, Find(c, "thing").State);
    }

    [Fact]
    public void Name_case_sensitivity_is_configurable()
    {
        using var f = new FolderFixture();
        f.Write("L", "Readme.TXT", "x");
        f.Write("R", "readme.txt", "x");
        Assert.Equal(2, FolderComparer.Compare(f.Left, f.Right, new FolderOptions { IgnoreNameCase = false }).Root.Children.Count);
        var ci = FolderComparer.Compare(f.Left, f.Right, new FolderOptions { IgnoreNameCase = true });
        Assert.Single(ci.Root.Children);
        Assert.Equal(FolderEntryState.Same, ci.Root.Children[0].State);
    }

    [Fact]
    public void Folders_come_before_files_and_each_group_is_sorted()
    {
        using var f = new FolderFixture();
        f.Write("L", "b.txt", "x"); f.Write("L", "A.txt", "x"); f.Write("L", "zdir/f", "x"); f.Write("L", "adir/f", "x");
        var names = FolderComparer.Compare(f.Left, f.Right, CaseSensitive).Root.Children.Select(n => n.Name).ToList();
        Assert.Equal(["adir", "zdir", "A.txt", "b.txt"], names);
    }

    [Fact]
    public void Missing_root_means_everything_is_one_sided()
    {
        using var f = new FolderFixture();
        f.Write("L", "a.txt", "x");
        var c = FolderComparer.Compare(f.Left, Path.Combine(f.Root, "does-not-exist"), CaseSensitive);
        Assert.Equal(FolderEntryState.LeftOnly, Find(c, "a.txt").State);
    }

    [Fact]
    public void Non_recursive_compare_stays_at_the_top_level()
    {
        using var f = new FolderFixture();
        f.Write("L", "top.txt", "x"); f.Write("L", "sub/deep.txt", "x");
        var c = FolderComparer.Compare(f.Left, f.Right, CaseSensitive with { Recursive = false });
        Assert.DoesNotContain(c.Root.Descendants(), n => n.Name == "deep.txt");
    }

    [Fact]
    public void Cancellation_stops_a_scan()
    {
        using var f = new FolderFixture();
        for (var i = 0; i < 50; i++) { f.Write("L", $"d{i}/f.txt", "x"); }
        var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => FolderComparer.Compare(f.Left, f.Right, CaseSensitive, null, cts.Token));
    }

    [Fact]
    public void Ten_thousand_files_compare_quickly()
    {
        using var f = new FolderFixture();
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 10_000; i++)
        {
            var rel = $"d{i % 100}/f{i}.txt";
            f.Write("L", rel, "content" + i, t);
            f.Write("R", rel, "content" + i, t);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var c = FolderComparer.Compare(f.Left, f.Right, CaseSensitive);
        sw.Stop();
        Assert.Equal(10_000, c.Summary.Same);
        Assert.True(sw.ElapsedMilliseconds < 8000, $"took {sw.ElapsedMilliseconds} ms");
    }
}

public class MaskSetTests
{
    [Theory]
    [InlineData("*.cs", "Program.cs", true)]
    [InlineData("*.cs", "Program.csx", false)]
    [InlineData("a?c", "abc", true)]
    [InlineData("a?c", "ac", false)]
    [InlineData("*.cs;*.md", "README.md", true)]
    [InlineData("*.cs|*.md", "x.md", true)]
    [InlineData("file(1).txt", "file(1).txt", true)] // regex metacharacters are literal
    public void Masks_match(string masks, string name, bool expected)
        => Assert.Equal(expected, new MaskSet(masks, false).IsMatch(name));

    [Fact]
    public void Case_insensitive_masks_and_empty_set() 
    {
        Assert.True(new MaskSet("*.CS", true).IsMatch("a.cs"));
        Assert.True(new MaskSet("", true).IsEmpty);
    }
}
