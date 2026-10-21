using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Collaterale.App.Controls;
using Collaterale.App.ViewModels;
using Collaterale.App.Views;

namespace Collaterale.App.UiTests;

public class FindTests
{
    private static TextCompareViewModel Open(string[] left, string[] right)
    {
        var vm = new TextCompareViewModel();
        vm.OpenText("l.txt", left, "r.txt", right);
        return vm;
    }

    private static readonly string[] L = ["alpha", "beta apple", "gamma"];
    private static readonly string[] R = ["alpha", "beta apple pie", "apple tart"];

    [AvaloniaFact]
    public void Matches_are_counted_across_both_sides_and_follow_the_options()
    {
        var vm = Open(L, R);
        vm.FindVisible = true;
        vm.FindText = "apple";
        Assert.Equal(3, vm.FindMatches.Count);
        Assert.Equal("3 matches", vm.FindStatus);
        vm.FindSides = FindSides.Left;
        Assert.Single(vm.FindMatches);
        vm.FindSides = FindSides.Both;
        vm.FindText = "APPLE";
        Assert.Equal(3, vm.FindMatches.Count);
        vm.FindMatchCase = true;
        Assert.Empty(vm.FindMatches);
        Assert.Equal("No matches", vm.FindStatus);
    }

    [AvaloniaFact]
    public void Find_next_walks_the_left_side_then_the_right_then_wraps()
    {
        var vm = Open(L, R);
        vm.FindVisible = true;
        vm.FindText = "apple";
        var seen = new List<FindHit>();
        vm.FindHitSelected += seen.Add;
        for (var i = 0; i < 4; i++) { vm.FindNextCommand.Execute(null); }
        Assert.Equal([(Side.Left, 1), (Side.Right, 1), (Side.Right, 2), (Side.Left, 1)], seen.Select(h => (h.Side, h.Line)));
    }

    [AvaloniaFact]
    public void Find_previous_goes_backwards()
    {
        var vm = Open(L, R);
        vm.FindVisible = true;
        vm.FindText = "apple";
        vm.Caret = (Side.Right, 2, 0, 0);
        var seen = new List<FindHit>();
        vm.FindHitSelected += seen.Add;
        vm.FindPreviousCommand.Execute(null);
        vm.FindPreviousCommand.Execute(null);
        Assert.Equal([(Side.Right, 1), (Side.Left, 1)], seen.Select(h => (h.Side, h.Line)));
    }

    [AvaloniaFact]
    public void Wrap_off_stops_at_the_end()
    {
        var vm = Open(L, R);
        vm.FindVisible = true;
        vm.FindText = "apple";
        vm.FindWrap = false;
        vm.Caret = (Side.Right, 2, 5, 5);
        var seen = new List<FindHit>();
        vm.FindHitSelected += seen.Add;
        vm.FindNextCommand.Execute(null);
        Assert.Empty(seen);
        Assert.Contains("Not found", vm.FindStatus);
    }

    [AvaloniaFact]
    public void Replace_all_edits_both_sides_marks_them_changed_and_can_be_undone()
    {
        var vm = Open(L, R);
        vm.FindVisible = true;
        vm.FindText = "apple";
        vm.ReplaceText = "pear";
        vm.ReplaceAllCommand.Execute(null);
        Assert.Equal("Replaced 3 occurrences", vm.FindStatus);
        Assert.Equal("beta pear", vm.Left.Lines[1]);
        Assert.Equal(["alpha", "beta pear pie", "pear tart"], vm.Right.Lines);
        Assert.True(vm.Left.Dirty && vm.Right.Dirty);
        vm.UndoCommand.Execute(null);
        Assert.Equal("beta apple", vm.Left.Lines[1]);
        Assert.Equal("apple tart", vm.Right.Lines[2]);
    }

    [AvaloniaFact]
    public void Replace_one_replaces_the_selected_match_and_moves_on()
    {
        var vm = Open(L, R);
        vm.FindVisible = true;
        vm.FindText = "apple";
        vm.ReplaceText = "pear";
        vm.FindNextCommand.Execute(null);                       // selects the left match
        vm.ReplaceCommand.Execute(null);                        // replaces it and selects the next
        Assert.Equal("beta pear", vm.Left.Lines[1]);
        Assert.Equal("beta apple pie", vm.Right.Lines[1]);      // not yet
        Assert.Equal((Side.Right, 1), (vm.Caret.Side, vm.Caret.Line));
    }

    [AvaloniaFact]
    public void A_protected_side_is_never_replaced_into()
    {
        var vm = Open(L, R);
        vm.RightReadOnly = true;
        vm.FindVisible = true;
        vm.FindText = "apple";
        vm.ReplaceText = "pear";
        vm.ReplaceAllCommand.Execute(null);
        Assert.Equal("beta pear", vm.Left.Lines[1]);
        Assert.Equal("beta apple pie", vm.Right.Lines[1]);
        vm.EditingDisabled = true;
        vm.FindText = "pear";
        vm.ReplaceText = "plum";
        vm.ReplaceAllCommand.Execute(null);
        Assert.Equal("beta pear", vm.Left.Lines[1]);
    }

    [AvaloniaFact]
    public void A_bad_regular_expression_is_reported_and_the_find_box_remembers_searches()
    {
        var vm = Open(L, R);
        vm.FindVisible = true;
        vm.FindRegex = true;
        vm.FindText = "(";
        Assert.StartsWith("Not a valid regular expression", vm.FindStatus);
        vm.FindNextCommand.Execute(null);                       // does nothing, does not throw
        vm.FindText = "a.p";
        vm.FindNextCommand.Execute(null);
        Assert.Equal(["a.p"], vm.FindHistory);
    }

    [AvaloniaFact]
    public void Opening_the_panel_takes_the_word_under_the_cursor()
    {
        var vm = Open(L, R);
        vm.CurrentWordProvider = () => "gamma";
        vm.ShowFindCommand.Execute(false);
        Assert.True(vm.FindVisible);
        Assert.Equal("gamma", vm.FindText);
        vm.InitFindFromWord = false;
        vm.FindText = "keep";
        vm.ShowFindCommand.Execute(false);
        Assert.Equal("keep", vm.FindText);
    }

    [AvaloniaFact]
    public void The_details_panel_shows_the_cursor_line_as_text_and_as_hex_and_edits_it()
    {
        var vm = Open(L, R);
        var row = vm.RowOfLine(Side.Left, 1);
        vm.CaretMoved(Side.Left, row);
        Assert.Equal("beta apple", vm.LeftDetailText);
        var facing = vm.RightRows[row];                                      // the right side shows what faces the left line on that row
        Assert.Equal(facing.IsFiller ? "" : vm.Right.Lines[facing.LineNumber - 1], vm.RightDetailText);
        Assert.Equal("Left · line 2", vm.LeftDetailLabel);
        Assert.StartsWith("0000  62 65 74 61 20 61 70 70 6C 65", vm.LeftHexText);
        Assert.EndsWith("beta apple", vm.LeftHexText);

        vm.SetLine(Side.Left, 1, "beta mango");
        Assert.Equal("beta mango", vm.Left.Lines[1]);
        Assert.True(vm.Left.Dirty);
        vm.UndoCommand.Execute(null);
        Assert.Equal("beta apple", vm.Left.Lines[1]);
        vm.EditingDisabled = true;
        vm.SetLine(Side.Left, 1, "nope");
        Assert.Equal("beta apple", vm.Left.Lines[1]);
    }

    [AvaloniaFact]
    public void Long_lines_are_split_into_sixteen_byte_hex_rows()
    {
        var side = new TextSide();
        side.SetText(null, [new string('a', 20)]);
        var rows = TextCompareViewModel.HexOf(side, 0).Split('\n');
        Assert.Equal(2, rows.Length);
        Assert.StartsWith("0010  61 61 61 61 ", rows[1]);
    }

    private static async Task<(TextCompareViewModel Vm, MainWindow Win)> Show()
    {
        var root = Path.Combine(Path.GetTempPath(), "collaterale-find-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Cart.cs"), TextCompareTests.LeftSample);
        File.WriteAllText(Path.Combine(root, "Cart2.cs"), TextCompareTests.RightSample);
        var shell = TextCompareTests.NewShell();
        var vm = shell.OpenTextCompare(Path.Combine(root, "Cart.cs"), Path.Combine(root, "Cart2.cs"));
        var w = new MainWindow(shell, []);
        w.Show();
        await TextCompareTests.Settle();
        return (vm, w);
    }

    [AvaloniaFact]
    public async Task The_find_panel_highlights_matches_and_selects_the_hit_in_the_pane()
    {
        var (vm, w) = await Show();
        vm.ShowReplaceCommand.Execute(null);
        vm.FindText = "total";
        await TextCompareTests.Settle();

        var panes = w.GetVisualDescendants().OfType<DiffPane>().ToList();
        Assert.Equal(2, panes.Count);
        vm.FindNextCommand.Execute(null);
        await TextCompareTests.Settle();
        var left = panes[0];
        Assert.Equal("total", left.TextArea.Selection.GetText().ToLowerInvariant());
        TextCompareTests.Shot(w, "text-compare-find.png");
    }

    [AvaloniaFact]
    public async Task Whitespace_wrap_over_under_and_details_change_the_view()
    {
        var (vm, w) = await Show();
        var panes = w.GetVisualDescendants().OfType<DiffPane>().ToList();
        var left = panes[0];

        vm.ShowWhitespace = true;
        Assert.True(left.Options.ShowSpaces && left.Options.ShowTabs && left.Options.ShowEndOfLine);
        vm.WordWrap = true;
        Assert.True(left.WordWrap);

        var leftBox = w.GetVisualDescendants().OfType<Avalonia.Controls.Border>().First(b => b.Name == "LeftBox");
        var rightBox = w.GetVisualDescendants().OfType<Avalonia.Controls.Border>().First(b => b.Name == "RightBox");
        Assert.Equal(Avalonia.Controls.Grid.GetColumn(leftBox) + 2, Avalonia.Controls.Grid.GetColumn(rightBox));
        vm.OverUnder = true;
        await TextCompareTests.Settle();
        Assert.Equal(Avalonia.Controls.Grid.GetColumn(leftBox), Avalonia.Controls.Grid.GetColumn(rightBox));
        Assert.True(Avalonia.Controls.Grid.GetRow(rightBox) > Avalonia.Controls.Grid.GetRow(leftBox));
        TextCompareTests.Shot(w, "text-compare-over-under.png");

        vm.OverUnder = false;
        vm.Details = DetailsMode.Hex;
        left.TextArea.Caret.Line = 8;
        await TextCompareTests.Settle();
        Assert.Contains("Left", vm.LeftDetailLabel);
        Assert.False(string.IsNullOrEmpty(vm.LeftHexText));
        TextCompareTests.Shot(w, "text-compare-details.png");
    }
}

public class DiskChangeTests
{
    private static (TextCompareViewModel Vm, string Left) Open()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-dc-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var (l, r) = (Path.Combine(dir, "l.txt"), Path.Combine(dir, "r.txt"));
        File.WriteAllText(l, "a\nb\n");
        File.WriteAllText(r, "a\nc\n");
        var vm = new TextCompareViewModel();
        vm.Open(l, r);
        return (vm, l);
    }

    private static void Touch(string path, string text)
    {
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));      // clearly newer, whatever the file system's resolution
    }

    [AvaloniaFact]
    public void A_file_changed_on_disk_asks_once_and_then_stays_quiet()
    {
        var (vm, left) = Open();
        var asked = new List<Side>();
        vm.DiskChanged += asked.Add;
        vm.CheckDisk();
        Assert.Empty(asked);                                                // nothing changed yet

        Touch(left, "a\nCHANGED\n");
        vm.CheckDisk();
        Assert.Equal([Side.Left], asked);
        vm.CheckDisk();
        Assert.Single(asked);                                               // not asked again for the same change
        Assert.Equal("b", vm.Left.Lines[1]);                                // and nothing was reloaded behind the user's back
    }

    [AvaloniaFact]
    public void Auto_reload_replaces_the_text_when_there_are_no_unsaved_changes()
    {
        var (vm, left) = Open();
        vm.Prefs.Files.AutoReloadUnlessEdited = true;
        Touch(left, "a\nCHANGED\n");
        vm.CheckDisk();
        Assert.Equal("CHANGED", vm.Left.Lines[1]);
        Assert.False(vm.ChangedOnDisk_Left());
    }

    [AvaloniaFact]
    public void Auto_reload_never_discards_unsaved_edits()
    {
        var (vm, left) = Open();
        vm.Prefs.Files.AutoReloadUnlessEdited = true;
        vm.SetLine(Side.Left, 1, "my edit");
        Touch(left, "a\nCHANGED\n");
        var asked = new List<Side>();
        vm.DiskChanged += asked.Add;
        vm.CheckDisk();
        Assert.Equal("my edit", vm.Left.Lines[1]);
        Assert.Equal([Side.Left], asked);
    }

    [AvaloniaFact]
    public void The_check_can_be_turned_off()
    {
        var (vm, left) = Open();
        vm.Prefs.Files.CheckFilesChangedOnDisk = false;
        Touch(left, "a\nCHANGED\n");
        var asked = new List<Side>();
        vm.DiskChanged += asked.Add;
        vm.CheckDisk();
        Assert.Empty(asked);
    }

    [AvaloniaFact]
    public void Saving_here_does_not_count_as_a_change_by_someone_else()
    {
        var (vm, _) = Open();
        vm.SetLine(Side.Left, 1, "mine");
        vm.SaveLeftCommand.Execute(null);
        var asked = new List<Side>();
        vm.DiskChanged += asked.Add;
        vm.CheckDisk();
        Assert.Empty(asked);
    }
}

internal static class TextCompareTestExtensions
{
    public static bool ChangedOnDisk_Left(this TextCompareViewModel vm) => vm.Left.ChangedOnDisk;
}

public class LockTests
{
    private static (TextCompareViewModel Vm, string Left, string Right) Open()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-lk-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var (l, r) = (Path.Combine(dir, "l.txt"), Path.Combine(dir, "r.txt"));
        File.WriteAllText(l, "a\nb\n");
        File.WriteAllText(r, "a\nc\n");
        var vm = new TextCompareViewModel();
        vm.Open(l, r);
        return (vm, l, r);
    }

    [AvaloniaFact]
    public void A_file_that_is_read_only_on_disk_cannot_be_edited_copied_into_or_saved_and_the_status_says_why()
    {
        if (OperatingSystem.IsWindows() is false && Environment.UserName == "root") { return; }   // root writes anywhere; the lock is by attribute here
        var (vm, left, _) = Open();
        Assert.Equal("", vm.LockText);
        File.SetAttributes(left, FileAttributes.ReadOnly);
        vm.ReloadCommand.Execute(null);                                 // the view re-reads and re-checks
        Assert.True(vm.LeftLocked);
        Assert.False(vm.RightLocked);
        Assert.Contains("Left: the left file is read-only", vm.LockText);

        vm.CopyGroup(0, Side.Right);                                    // copying into the locked left side is refused
        Assert.Equal("b", vm.Left.Lines[1]);
        vm.CopyGroup(0, Side.Left);                                     // the free right side can still be changed
        Assert.Equal("b", vm.Right.Lines[1]);
        File.SetAttributes(left, FileAttributes.Normal);
    }

    [AvaloniaFact]
    public void Disabled_editing_and_protected_sides_are_reported_in_words()
    {
        var (vm, _, _) = Open();
        vm.RightReadOnly = true;
        Assert.Equal("the right side is protected (read-only)", vm.LockReason(Side.Right));
        Assert.Null(vm.LockReason(Side.Left));
        Assert.StartsWith("🔒 Right:", vm.LockText);
        vm.EditingDisabled = true;
        Assert.Equal("editing is disabled for this session", vm.LockReason(Side.Left));
        Assert.Equal("🔒 Editing is disabled for this session", vm.LockText);
    }
}
