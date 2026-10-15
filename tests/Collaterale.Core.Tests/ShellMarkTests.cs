using Collaterale.Core.Cli;

namespace Collaterale.Core.Tests;

public class ShellMarkTests
{
    [Fact]
    public void Select_then_compare_pairs_the_two_paths()
    {
        using var f = new FolderFixture();
        f.Write("L", "a.txt", "a", DateTime.UtcNow); f.Write("R", "b.txt", "b", DateTime.UtcNow);
        var marker = Path.Combine(f.Left, "..", "marker.txt");
        var a = Path.Combine(f.Left, "a.txt"); var b = Path.Combine(f.Right, "b.txt");

        Assert.True(ShellMark.Handle([ShellMark.SelectLeft, a], marker, out var r1, out _, out var failed1));
        Assert.Null(r1); Assert.False(failed1);

        Assert.True(ShellMark.Handle([ShellMark.CompareSelected, b], marker, out var r2, out _, out var failed2));
        Assert.False(failed2);
        Assert.Equal([Path.GetFullPath(a), Path.GetFullPath(b)], r2!);
    }

    [Fact]
    public void Compare_without_a_selection_fails_with_a_hint()
    {
        Assert.True(ShellMark.Handle([ShellMark.CompareSelected, "/tmp"], "/nonexistent/marker.txt", out var r, out var msg, out var failed));
        Assert.True(failed); Assert.Null(r); Assert.Contains("Select as left side", msg);
    }

    [Fact]
    public void Selecting_a_missing_path_fails_and_other_commands_are_ignored()
    {
        Assert.True(ShellMark.Handle([ShellMark.SelectLeft, "/no/such/file"], Path.GetTempFileName(), out _, out _, out var failed));
        Assert.True(failed);
        Assert.False(ShellMark.Handle(["a", "b"], "x", out _, out _, out _));
    }
}
