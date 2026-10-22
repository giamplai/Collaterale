using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Collaterale.App.Controls;
using Collaterale.App.ViewModels;
using Collaterale.App.Views;
using Collaterale.Core.Diff;

namespace Collaterale.App.UiTests;

public class EditingTests
{
    private static (TextCompareViewModel Vm, MainWindow Win, DiffPane Left, DiffPane Right) Open(string[] left, string[] right)
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewTextCompare();
        vm.OpenText("/work/a.txt", left, "/work/b.txt", right);
        var w = new MainWindow(shell, []);
        w.Show();
        var panes = w.GetVisualDescendants().OfType<DiffPane>().ToList();
        return (vm, w, panes[0], panes[1]);
    }

    /// <summary>Waits long enough for the debounced commit (450 ms) to run and the UI to settle.</summary>
    private static async Task Commit() { await Task.Delay(700); await TextCompareTests.Settle(); }

    private static void Replace(DiffPane pane, string oldText, string newText)
    {
        var doc = pane.Document;
        var i = doc.Text.IndexOf(oldText, StringComparison.Ordinal);
        Assert.True(i >= 0, $"'{oldText}' not found in the pane");
        doc.Replace(i, oldText.Length, newText);
    }

    [AvaloniaFact]
    public async Task Clicking_and_dragging_in_the_line_number_margin_selects_whole_rows()
    {
        var (_, w, left, _) = Open(["alpha", "beta", "gamma", "delta"], ["alpha", "beta", "gamma", "delta"]);
        await TextCompareTests.Settle();
        var margin = left.TextArea.LeftMargins.First(m => m.GetType().Name == "RowNumberMargin");
        Assert.NotNull(margin.Cursor);
        var tv = left.TextArea.TextView;
        var lineH = tv.DefaultLineHeight;
        var x = 4.0;
        Point At(int row) => margin.TranslatePoint(new Point(x, lineH * (row + 0.5)), w)!.Value;

        w.MouseDown(At(1), MouseButton.Left);
        Assert.Equal("beta\n", left.TextArea.Selection.GetText());
        w.MouseMove(At(2));
        Assert.Equal("beta\ngamma\n", left.TextArea.Selection.GetText());
        TextCompareTests.Shot(w, "text-compare-line-select.png");
        w.MouseUp(At(2), MouseButton.Left);
        left.SelectRows(3, 3);
        Assert.Equal("delta", left.TextArea.Selection.GetText());   // the last line has no line break to include
    }

    [AvaloniaFact]
    public async Task Text_details_show_the_two_lines_touching_with_the_changed_words_marked()
    {
        var (vm, w, left, _) = Open(
            ["<?php", "    if ($cartid && $shopcart->resellercode !== $reseller->ResellerCode) {", "}"],
            ["<?php", "    if (!$cartid || ($shopcart->resellercode !== $reseller->ResellerCode)) {", "}"]);
        vm.Details = DetailsMode.Text;
        vm.ShowWhitespace = true;
        await TextCompareTests.Settle();
        left.TextArea.Caret.Line = 2;
        await TextCompareTests.Settle();
        var lines = w.GetVisualDescendants().OfType<DetailLine>().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Contains("$cartid", lines[0].Text);
        Assert.Contains("!$cartid", lines[1].Text);
        TextCompareTests.Shot(w, "text-compare-text-details.png");
    }

    [AvaloniaFact]
    public async Task Selecting_lines_shows_an_arrow_in_the_gutter_that_carries_them_to_the_other_side()
    {
        var (vm, w, left, _) = Open(["alpha", "beta", "gamma", "delta"], ["alpha", "BETA", "gamma", "delta"]);
        await TextCompareTests.Settle();
        var margin = left.TextArea.LeftMargins.First(m => m.GetType().Name == "RowNumberMargin");
        var lineH = left.TextArea.TextView.DefaultLineHeight;
        w.MouseDown(margin.TranslatePoint(new Point(4, lineH * 1.5), w)!.Value, MouseButton.Left);   // the row of "beta"
        w.MouseUp(margin.TranslatePoint(new Point(4, lineH * 1.5), w)!.Value, MouseButton.Left);
        await TextCompareTests.Settle();
        TextCompareTests.Shot(w, "text-compare-selection-arrow.png");

        var gutter = w.GetVisualDescendants().OfType<DiffGutter>().First();
        var arrow = gutter.TranslatePoint(new Point(gutter.Bounds.Width - DiffGutter.ButtonSize / 2 - 3, lineH * 1.5 + 2), w)!.Value;
        w.MouseDown(arrow, MouseButton.Left);
        w.MouseUp(arrow, MouseButton.Left);
        await TextCompareTests.Settle();
        Assert.Equal(["alpha", "beta", "gamma", "delta"], vm.Right.Lines);
    }

    [AvaloniaFact]
    public async Task Edited_state_is_visible_with_unsaved_marker_and_enabled_save()
    {
        var root = Path.Combine(Path.GetTempPath(), "collaterale-edited-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "v1")); Directory.CreateDirectory(Path.Combine(root, "v2"));
        File.WriteAllText(Path.Combine(root, "v1", "Cart.cs"), TextCompareTests.LeftSample);
        File.WriteAllText(Path.Combine(root, "v2", "Cart.cs"), TextCompareTests.RightSample);
        var shell = TextCompareTests.NewShell();
        var vm = shell.OpenTextCompare(Path.Combine(root, "v1", "Cart.cs"), Path.Combine(root, "v2", "Cart.cs"));
        var w = new MainWindow(shell, []);
        w.Show();
        var left = w.GetVisualDescendants().OfType<DiffPane>().First();
        await TextCompareTests.Settle();

        Replace(left, "TaxRate = 0.20", "TaxRate = 0.22");     // bring the left file in line with the right one by typing
        await Commit();

        Assert.True(vm.Left.Dirty);
        Assert.EndsWith("●", vm.LeftTitle);
        Assert.True(vm.SaveLeftCommand.CanExecute(null));
        Assert.Equal(4, vm.Comparison!.DifferenceCount);       // the TaxRate difference is gone
        TextCompareTests.Shot(w, "text-compare-edited.png");
    }

    [AvaloniaFact]
    public async Task Typing_in_a_pane_updates_the_model_and_the_differences()
    {
        var (vm, _, _, right) = Open(["a", "b", "c"], ["a", "b", "c"]);
        await TextCompareTests.Settle();
        Assert.True(vm.Comparison!.AreEqual);

        Replace(right, "b", "B changed");
        Assert.False(vm.Right.Dirty);               // committed after the pause, not on every keystroke
        await Commit();

        Assert.Equal(["a", "B changed", "c"], vm.Right.Lines);
        Assert.True(vm.Right.Dirty);
        Assert.Equal(1, vm.Comparison!.DifferenceCount);
        Assert.Equal("a\nB changed\nc", vm.RightText);
        Assert.Contains(vm.RightRows, r => r.Kind == RowKind.Changed);
    }

    [AvaloniaFact]
    public async Task Typing_into_a_filler_line_creates_a_real_line()
    {
        var (vm, _, left, _) = Open(["one", "three"], ["one", "two", "three"]);
        await TextCompareTests.Settle();
        Assert.Contains(vm.LeftRows, r => r.IsFiller);          // the left pane has a blank filler opposite "two"

        var fillerLine = vm.LeftRows.ToList().FindIndex(r => r.IsFiller) + 1;
        var offset = left.Document.GetLineByNumber(fillerLine).Offset;
        left.Document.Insert(offset, "two");
        await Commit();

        Assert.Equal(["one", "two", "three"], vm.Left.Lines);
        Assert.True(vm.Comparison!.AreEqual);                   // the typed line now matches the right side
    }

    [AvaloniaFact]
    public async Task Untouched_fillers_never_leak_into_the_saved_lines()
    {
        var (vm, _, left, _) = Open(["a", "d"], ["a", "b", "c", "d"]);
        await TextCompareTests.Settle();
        Replace(left, "d", "D!");                               // edit a real line; two fillers sit above it
        await Commit();
        Assert.Equal(["a", "D!"], vm.Left.Lines);               // exactly the real lines, no blank rows from the fillers
    }

    [AvaloniaFact]
    public async Task Deleting_and_adding_lines_is_reflected()
    {
        var (vm, _, _, right) = Open(["x", "y", "z"], ["x", "y", "z"]);
        await TextCompareTests.Settle();
        Replace(right, "y\n", "");                              // remove a whole line
        await Commit();
        Assert.Equal(["x", "z"], vm.Right.Lines);
        Assert.Equal(1, vm.Comparison!.DifferenceCount);

        right.Document.Insert(right.Document.TextLength, "\nnew tail");
        await Commit();
        Assert.Equal(["x", "z", "new tail"], vm.Right.Lines);
    }

    [AvaloniaFact]
    public async Task Edits_in_both_panes_at_once_are_both_kept()
    {
        var (vm, _, left, right) = Open(["a", "keep1", "keep2", "b"], ["a", "keep1", "keep2", "b"]);
        await TextCompareTests.Settle();
        Replace(left, "a", "A-left");
        Replace(right, "b", "B-right");
        await Commit();
        Assert.Equal(["A-left", "keep1", "keep2", "b"], vm.Left.Lines);
        Assert.Equal(["a", "keep1", "keep2", "B-right"], vm.Right.Lines);
        Assert.Equal(2, vm.Comparison!.Groups.Count);          // two separate edits, separated by unchanged lines
    }

    [AvaloniaFact]
    public async Task One_undo_reverts_one_pause_of_typing()
    {
        var (vm, _, _, right) = Open(["a", "b"], ["a", "b"]);
        await TextCompareTests.Settle();
        Replace(right, "b", "typed");
        await Commit();
        Assert.Equal(["a", "typed"], vm.Right.Lines);

        vm.UndoCommand.Execute(null);
        Assert.Equal(["a", "b"], vm.Right.Lines);
        Assert.False(vm.Right.Dirty);
        vm.RedoCommand.Execute(null);
        Assert.Equal(["a", "typed"], vm.Right.Lines);
    }

    [AvaloniaFact]
    public async Task Ctrl_Z_in_the_editor_runs_the_comparison_undo()
    {
        var (vm, w, _, right) = Open(["a", "b"], ["a", "b"]);
        await TextCompareTests.Settle();
        right.TextArea.Focus();   // what a click in the pane does
        Replace(right, "b", "typed");
        await Commit();
        Assert.Equal(["a", "typed"], vm.Right.Lines);

        w.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
        await TextCompareTests.Settle();
        Assert.Equal(["a", "b"], vm.Right.Lines);
    }

    [AvaloniaFact]
    public async Task The_caret_stays_on_the_same_line_when_the_layout_changes_around_it()
    {
        var (vm, _, _, right) = Open(["first", "second", "third"], ["first", "second", "third"]);
        await TextCompareTests.Settle();
        var line2 = right.Document.GetLineByNumber(2);
        right.TextArea.Caret.Line = 2;
        right.TextArea.Caret.Column = 4;
        Replace(right, "second", "second edited");
        await Commit();

        Assert.Equal(["first", "second edited", "third"], vm.Right.Lines);
        var (modelLine, column) = right.GetCaretModelPosition();
        Assert.Equal(1, modelLine);                 // still on the second real line after the rebuild
        Assert.Equal("second edited".Length, column); // the editor moves a caret inside a replaced span to its end
        Assert.NotNull(line2);
    }

    [AvaloniaFact]
    public async Task Edited_text_is_saved_with_the_original_encoding_and_line_endings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-edit-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var (l, r) = (Path.Combine(dir, "l.txt"), Path.Combine(dir, "r.txt"));
        File.WriteAllText(l, "one\r\ntwo\r\n");
        File.WriteAllText(r, "one\r\ntwo\r\n");
        var shell = TextCompareTests.NewShell();
        var vm = shell.OpenTextCompare(l, r);
        var w = new MainWindow(shell, []);
        w.Show();
        var right = w.GetVisualDescendants().OfType<DiffPane>().ToList()[1];
        await TextCompareTests.Settle();

        Replace(right, "two", "TWO");
        vm.SaveRightCommand.Execute(null);          // the save must flush the pending typing first
        Assert.Equal("one\r\nTWO\r\n", File.ReadAllText(r));
        Assert.False(vm.Right.Dirty);
        Directory.Delete(dir, true);
    }

    [AvaloniaFact]
    public async Task Clicking_a_gutter_button_with_pending_typing_applies_the_typing_first()
    {
        var (vm, _, _, right) = Open(["a", "b"], ["a", "b"]);
        await TextCompareTests.Settle();
        Replace(right, "a", "typed");
        vm.FlushEdits?.Invoke();                    // what a command does before it touches the model
        Assert.Equal(["typed", "b"], vm.Right.Lines);
    }
}
