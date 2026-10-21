using Collaterale.Core.Diff;
using Collaterale.Core.Folders;

namespace Collaterale.Core.Tests;

public class FolderCriteriaTests
{
    private static readonly DateTime T = new(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static FolderOptions O(Action<FolderOptionsBuilder>? _ = null) => new() { IgnoreNameCase = false };
    private static FolderNode Only(FolderComparison c) => c.Root.Descendants().Single(n => !n.IsDirectory);
    private sealed class FolderOptionsBuilder;

    [Fact]
    public void Crc32_matches_the_known_check_value()
    {
        Assert.Equal(0xCBF43926u, Crc32.Update(0, "123456789"u8));
        Assert.Equal(0u, Crc32.Update(0, []));
    }

    [Theory]
    [InlineData(0, true)]      // identical
    [InlineData(1, true)]      // +1 h: daylight saving
    [InlineData(-1, true)]
    [InlineData(2, false)]     // 2 h is not a DST change
    public void Daylight_saving_allowance_ignores_exactly_one_hour(int hours, bool same)
    {
        using var f = new FolderFixture();
        f.Write("L", "a.txt", "x", T); f.Write("R", "a.txt", "x", T.AddHours(hours));
        var c = FolderComparer.Compare(f.Left, f.Right, O() with { IgnoreDaylightSaving = true });
        Assert.Equal(same, Only(c).State == FolderEntryState.Same);
    }

    [Fact]
    public void Daylight_saving_allowance_still_sees_other_differences()
    {
        using var f = new FolderFixture();
        f.Write("L", "a.txt", "x", T); f.Write("R", "a.txt", "x", T.AddHours(1).AddMinutes(10));
        Assert.Equal(FolderEntryState.Different, Only(FolderComparer.Compare(f.Left, f.Right, O() with { IgnoreDaylightSaving = true })).State);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(-9, true)]
    [InlineData(3, true)]
    public void Time_zone_allowance_ignores_whole_hours(int hours, bool same)
    {
        using var f = new FolderFixture();
        f.Write("L", "a.txt", "x", T); f.Write("R", "a.txt", "x", T.AddHours(hours));
        var c = FolderComparer.Compare(f.Left, f.Right, O() with { IgnoreTimeZone = true });
        Assert.Equal(same, Only(c).State == FolderEntryState.Same);
        Assert.Equal(Newer.None, Only(c).Newer);
    }

    [Fact]
    public void Time_zone_allowance_leaves_minutes_visible()
    {
        using var f = new FolderFixture();
        f.Write("L", "a.txt", "x", T.AddMinutes(7)); f.Write("R", "a.txt", "x", T.AddHours(5));
        var n = Only(FolderComparer.Compare(f.Left, f.Right, O() with { IgnoreTimeZone = true }));
        Assert.Equal((FolderEntryState.Different, Newer.Left), (n.State, n.Newer));
    }

    [Fact]
    public void Differences_say_why()
    {
        using var f = new FolderFixture();
        f.Write("L", "size.txt", "a", T); f.Write("R", "size.txt", "abc", T);
        f.Write("L", "time.txt", "a", T); f.Write("R", "time.txt", "b", T.AddMinutes(5));
        var c = FolderComparer.Compare(f.Left, f.Right, O());
        Assert.Equal(DifferenceKinds.Size, c.Root.Descendants().Single(n => n.Name == "size.txt").Differences);
        Assert.Equal(DifferenceKinds.Time, c.Root.Descendants().Single(n => n.Name == "time.txt").Differences);
    }

    [Fact]
    public void Name_case_is_a_difference_only_when_asked()
    {
        using var f = new FolderFixture();
        f.Write("L", "Readme.TXT", "x", T); f.Write("R", "readme.txt", "x", T);
        var insensitive = new FolderOptions { IgnoreNameCase = true };
        Assert.Equal(FolderEntryState.Same, Only(FolderComparer.Compare(f.Left, f.Right, insensitive)).State);
        var n = Only(FolderComparer.Compare(f.Left, f.Right, insensitive with { CompareNameCase = true }));
        Assert.Equal((FolderEntryState.Different, DifferenceKinds.NameCase), (n.State, n.Differences));
    }

    [Fact]
    public void Attributes_are_compared_only_for_the_selected_ones()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var f = new FolderFixture();
        var l = f.Write("L", "a.txt", "x", T); var r = f.Write("R", "a.txt", "x", T);
        File.SetAttributes(r, File.GetAttributes(r) | FileAttributes.ReadOnly);
        Assert.Equal(FolderEntryState.Same, Only(FolderComparer.Compare(f.Left, f.Right, O())).State);
        Assert.Equal(FolderEntryState.Same, Only(FolderComparer.Compare(f.Left, f.Right, O() with { CompareAttributes = FileAttributes.Hidden })).State);
        var n = Only(FolderComparer.Compare(f.Left, f.Right, O() with { CompareAttributes = FileAttributes.ReadOnly }));
        Assert.Equal((FolderEntryState.Different, DifferenceKinds.Attributes), (n.State, n.Differences));
        _ = l;
    }

    [Fact]
    public void Unix_permissions_are_compared_when_asked()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var f = new FolderFixture();
        var l = f.Write("L", "run.sh", "x", T); var r = f.Write("R", "run.sh", "x", T);
        File.SetUnixFileMode(l, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.SetUnixFileMode(r, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Assert.Equal(FolderEntryState.Same, Only(FolderComparer.Compare(f.Left, f.Right, O())).State);
        var n = Only(FolderComparer.Compare(f.Left, f.Right, O() with { ComparePermissions = true }));
        Assert.Equal(DifferenceKinds.Permissions, n.Differences);
    }

    // ───── content tests ─────

    [Theory]
    [InlineData(ContentMode.Binary)]
    [InlineData(ContentMode.Crc)]
    [InlineData(ContentMode.Rules)]
    public void Content_criteria_see_real_differences_and_ignore_time(ContentMode mode)
    {
        using var f = new FolderFixture();
        f.Write("L", "same.txt", "hello", T); f.Write("R", "same.txt", "hello", T.AddDays(3));
        f.Write("L", "diff.txt", "hello", T); f.Write("R", "diff.txt", "hellp", T);
        var c = FolderComparer.Compare(f.Left, f.Right, O() with { Criteria = CompareCriteria.Content, ContentMode = mode });
        Assert.Equal(FolderEntryState.Same, c.Root.Descendants().Single(n => n.Name == "same.txt").State);
        var d = c.Root.Descendants().Single(n => n.Name == "diff.txt");
        Assert.Equal((FolderEntryState.Different, DifferenceKinds.Content), (d.State, d.Differences));
    }

    [Fact]
    public void Rules_mode_ignores_line_endings_whitespace_and_comments_as_configured()
    {
        using var f = new FolderFixture();
        File.WriteAllBytes(Path.Combine(f.Left, "a.cs"), "int a = 1; // one\r\nint b = 2;\r\n"u8.ToArray());
        File.WriteAllBytes(Path.Combine(f.Right, "a.cs"), "int a = 1;   // uno\nint  b = 2;\n"u8.ToArray());
        var baseOptions = O() with { Criteria = CompareCriteria.Content, ContentMode = ContentMode.Rules };

        Assert.Equal(FolderEntryState.Different, Only(FolderComparer.Compare(f.Left, f.Right, baseOptions)).State);   // comments and spaces differ
        var rules = new ContentRules { Whitespace = WhitespaceMode.IgnoreAmount, IgnoreComments = true };
        Assert.Equal(FolderEntryState.Same, Only(FolderComparer.Compare(f.Left, f.Right, baseOptions with { Rules = rules })).State);
        Assert.Equal(FolderEntryState.Different, Only(FolderComparer.Compare(f.Left, f.Right, baseOptions with { ContentMode = ContentMode.Binary, Rules = rules })).State);
    }

    [Fact]
    public void Rules_mode_can_ignore_what_string_literals_say()
    {
        using var f = new FolderFixture();
        File.WriteAllText(Path.Combine(f.Left, "m.cs"), "Log(\"starting\"); // go\n");
        File.WriteAllText(Path.Combine(f.Right, "m.cs"), "Log(\"demarrage\"); // go\n");
        var o = O() with { Criteria = CompareCriteria.Content, ContentMode = ContentMode.Rules };
        Assert.Equal(FolderEntryState.Different, Only(FolderComparer.Compare(f.Left, f.Right, o)).State);
        Assert.Equal(FolderEntryState.Same, Only(FolderComparer.Compare(f.Left, f.Right, o with { Rules = new ContentRules { IgnoreStrings = true } })).State);
        File.WriteAllText(Path.Combine(f.Right, "m.cs"), "Log(\"demarrage\"); // allez\n");
        Assert.Equal(FolderEntryState.Different, Only(FolderComparer.Compare(f.Left, f.Right, o with { Rules = new ContentRules { IgnoreStrings = true } })).State);   // the comment still counts
    }

    [Fact]
    public void Rules_mode_treats_line_endings_as_unimportant_always()
    {
        using var f = new FolderFixture();
        File.WriteAllBytes(Path.Combine(f.Left, "t.txt"), "a\r\nb\r\n"u8.ToArray());
        File.WriteAllBytes(Path.Combine(f.Right, "t.txt"), "a\nb\n"u8.ToArray());
        var o = O() with { Criteria = CompareCriteria.Content, ContentMode = ContentMode.Rules };
        Assert.Equal(FolderEntryState.Same, Only(FolderComparer.Compare(f.Left, f.Right, o)).State);
        Assert.Equal(FolderEntryState.Different, Only(FolderComparer.Compare(f.Left, f.Right, o with { ContentMode = ContentMode.Binary })).State);
    }

    [Fact]
    public void Rules_mode_falls_back_to_bytes_for_binary_files()
    {
        using var f = new FolderFixture();
        var data = Enumerable.Range(0, 400).Select(i => (byte)(i % 3 == 0 ? 0 : i)).ToArray();
        File.WriteAllBytes(Path.Combine(f.Left, "b.bin"), data);
        data[399] ^= 0xFF;
        File.WriteAllBytes(Path.Combine(f.Right, "b.bin"), data);
        Assert.Equal(FolderEntryState.Different, Only(FolderComparer.Compare(f.Left, f.Right, O() with { Criteria = CompareCriteria.Content, ContentMode = ContentMode.Rules })).State);
    }

    [Fact]
    public void Quick_then_content_reads_only_files_that_fail_the_quick_tests()
    {
        using var f = new FolderFixture();
        f.Write("L", "same.txt", "abc", T); f.Write("R", "same.txt", "abc", T);                    // quick: same
        f.Write("L", "touched.txt", "abc", T); f.Write("R", "touched.txt", "abc", T.AddHours(5));   // quick: time differs, content same
        f.Write("L", "edited.txt", "abc", T); f.Write("R", "edited.txt", "abd", T.AddHours(5));     // both differ
        var o = O() with { Criteria = CompareCriteria.QuickThenContent };

        var c = FolderComparer.Compare(f.Left, f.Right, o);
        Assert.Equal(FolderEntryState.Same, c.Root.Descendants().Single(n => n.Name == "touched.txt").State);   // overridden
        var e = c.Root.Descendants().Single(n => n.Name == "edited.txt");
        Assert.Equal(DifferenceKinds.Time | DifferenceKinds.Content, e.Differences);

        var strict = FolderComparer.Compare(f.Left, f.Right, o with { OverrideQuickResults = false });
        var t = strict.Root.Descendants().Single(n => n.Name == "touched.txt");
        Assert.Equal((FolderEntryState.Different, DifferenceKinds.Time), (t.State, t.Differences));
    }

    [Fact]
    public void Skipping_content_for_quick_matches_can_be_turned_off()
    {
        using var f = new FolderFixture();
        f.Write("L", "a.txt", "abc", T); f.Write("R", "a.txt", "abd", T);                          // same size and time, different bytes
        var o = O() with { Criteria = CompareCriteria.QuickThenContent };
        Assert.Equal(FolderEntryState.Same, Only(FolderComparer.Compare(f.Left, f.Right, o)).State);
        Assert.Equal(FolderEntryState.Different, Only(FolderComparer.Compare(f.Left, f.Right, o with { SkipContentIfQuickSame = false })).State);
    }
}
