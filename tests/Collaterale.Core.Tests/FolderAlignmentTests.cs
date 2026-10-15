using Collaterale.Core.Folders;

namespace Collaterale.Core.Tests;

public class FolderAlignmentTests
{
    private static readonly DateTime T = new(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly FolderOptions Base = new() { IgnoreNameCase = false, DetectRenames = false };

    private static List<FolderNode> Files(FolderComparison c) => [.. c.Root.Descendants().Where(n => !n.IsDirectory)];

    [Fact]
    public void Different_extensions_are_lined_up_only_when_asked()
    {
        using var f = new FolderFixture();
        f.Write("L", "report.doc", "version one", T); f.Write("R", "report.pdf", "version two, longer", T);
        Assert.Equal(2, Files(FolderComparer.Compare(f.Left, f.Right, Base)).Count);

        var n = Assert.Single(Files(FolderComparer.Compare(f.Left, f.Right, Base with { AlignDifferentExtensions = true })));
        Assert.Equal("report.doc", n.Name);
        Assert.Equal("report.pdf", n.RightName);
        Assert.Equal(FolderEntryState.Different, n.State);
        Assert.Equal("report.pdf", n.RightRelativePath);
    }

    [Fact]
    public void Extension_alignment_needs_exactly_one_candidate_on_each_side()
    {
        using var f = new FolderFixture();
        f.Write("L", "data.a", "1", T); f.Write("R", "data.b", "1", T); f.Write("R", "data.c", "1", T);
        var files = Files(FolderComparer.Compare(f.Left, f.Right, Base with { AlignDifferentExtensions = true }));
        Assert.Equal(3, files.Count);                       // ambiguous: nothing is guessed
        Assert.All(files, n => Assert.Null(n.RightName));
    }

    [Fact]
    public void Identical_names_win_over_extension_alignment()
    {
        using var f = new FolderFixture();
        f.Write("L", "x.txt", "a", T); f.Write("R", "x.txt", "a", T); f.Write("R", "x.md", "b", T);
        var files = Files(FolderComparer.Compare(f.Left, f.Right, Base with { AlignDifferentExtensions = true }));
        Assert.Equal(2, files.Count);
        Assert.Contains(files, n => n.Name == "x.txt" && n.State == FolderEntryState.Same);
        Assert.Contains(files, n => n.Name == "x.md" && n.State == FolderEntryState.RightOnly);
    }

    [Fact]
    public void Unicode_forms_of_the_same_name_are_lined_up_when_asked()
    {
        using var f = new FolderFixture();
        var composed = "café.txt";            // é as one code point
        var decomposed = "café.txt";         // e + combining acute
        f.Write("L", composed, "x", T); f.Write("R", decomposed, "x", T);
        Assert.Equal(2, Files(FolderComparer.Compare(f.Left, f.Right, Base)).Count);
        var n = Assert.Single(Files(FolderComparer.Compare(f.Left, f.Right, Base with { AlignUnicodeNormalization = true })));
        Assert.Equal(FolderEntryState.Same, n.State);
        Assert.Equal(decomposed, n.RightName);
    }

    [Fact]
    public void Hand_made_alignment_pairs_any_two_entries_of_the_same_folder()
    {
        using var f = new FolderFixture();
        f.Write("L", "sub/old-name.txt", "left text", T); f.Write("R", "sub/new-name.txt", "right text!", T);
        var o = Base with { Alignments = [new AlignmentOverride("sub/old-name.txt", "sub/new-name.txt")] };
        var n = Assert.Single(Files(FolderComparer.Compare(f.Left, f.Right, o)));
        Assert.Equal(FolderEntryState.Different, n.State);
        Assert.Equal("sub/new-name.txt", n.RightRelativePath);
        Assert.Equal("sub/old-name.txt", n.LeftRelativePath);
    }

    [Fact]
    public void Alignment_across_folders_is_ignored()
    {
        using var f = new FolderFixture();
        f.Write("L", "a/x.txt", "1", T); f.Write("R", "b/y.txt", "1", T);
        var o = Base with { Alignments = [new AlignmentOverride("a/x.txt", "b/y.txt")] };
        Assert.Equal(2, Files(FolderComparer.Compare(f.Left, f.Right, o)).Count);
    }

    [Fact]
    public void Copying_an_aligned_pair_overwrites_the_other_sides_own_file_name()
    {
        using var f = new FolderFixture();
        f.Write("L", "report.doc", "fresh content", T.AddHours(5)); f.Write("R", "report.pdf", "stale", T);
        var c = FolderComparer.Compare(f.Left, f.Right, Base with { AlignDifferentExtensions = true });
        var plan = FolderSync.Plan(c, SyncMode.UpdateRight);
        Assert.True(FolderSync.Execute(c, plan).Success);
        Assert.Equal("fresh content", File.ReadAllText(Path.Combine(f.Right, "report.pdf")));
        Assert.False(File.Exists(Path.Combine(f.Right, "report.doc")));          // no stray copy under the left name
    }

    [Fact]
    public void Case_insensitive_matching_copies_to_the_real_name_on_the_other_side()
    {
        using var f = new FolderFixture();
        f.Write("L", "Docs/README.md", "new text here", T.AddHours(5)); f.Write("R", "docs/readme.md", "old", T);
        var c = FolderComparer.Compare(f.Left, f.Right, new FolderOptions { IgnoreNameCase = true, DetectRenames = false });
        var n = Assert.Single(Files(c));
        Assert.Equal("docs/readme.md", n.RightRelativePath);
        Assert.Equal("Docs/README.md", n.LeftRelativePath);
        Assert.True(FolderSync.Execute(c, FolderSync.Plan(c, SyncMode.UpdateRight)).Success);
        Assert.Equal("new text here", File.ReadAllText(Path.Combine(f.Right, "docs", "readme.md")));
        Assert.Single(Directory.GetFileSystemEntries(f.Right));                    // no second "Docs" folder appeared
    }
}
