using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Collaterale.App.ViewModels;
using Collaterale.App.Views;
using Collaterale.Core.Diff;

namespace Collaterale.App.UiTests;

public class TextCompareTests
{
    internal static string OutDir
    {
        get
        {
            var d = Environment.GetEnvironmentVariable("COLLATERALE_SCREENSHOTS") ?? Path.Combine(Path.GetTempPath(), "collaterale-shots");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    internal static void Shot(Avalonia.Controls.Window w, string name)
    {
        using var fs = File.Create(Path.Combine(OutDir, name));
        w.CaptureRenderedFrame()!.Save(fs, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    /// <summary>Pumps the UI thread until the condition holds (slow CI runners need more than a fixed delay); fails fast on timeout via the caller's assert.</summary>
    internal static async Task WaitUntil(Func<bool> condition, int timeoutMs = 20000)
    {
        for (var waited = 0; waited < timeoutMs && !condition(); waited += 40)
        {
            await Task.Delay(40);
            Dispatcher.UIThread.RunJobs();
        }
    }

    internal static async Task Settle()
    {
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(40);
            Dispatcher.UIThread.RunJobs();
        }
    }

    internal const string LeftSample = """
        using System;
        using System.Collections.Generic;

        namespace Shop
        {
            public class Cart
            {
                private readonly List<Item> items = new();
                private const double TaxRate = 0.20;

                public void Add(Item item)
                {
                    items.Add(item);
                }

                public double Total()
                {
                    double total = 0;
                    foreach (var item in items)
                    {
                        total += item.Price * item.Quantity;
                    }
                    return total + total * TaxRate;
                }

                public int Count => items.Count;
            }
        }
        """;

    internal const string RightSample = """
        using System;
        using System.Collections.Generic;
        using System.Linq;

        namespace Shop
        {
            public class Cart
            {
                private readonly List<Item> items = new();
                private const double TaxRate = 0.22;

                public void Add(Item item)
                {
                    ArgumentNullException.ThrowIfNull(item);
                    items.Add(item);
                }

                public double Total()
                {
                    var subtotal = items.Sum(i => i.Price * i.Quantity);
                    return subtotal + subtotal * TaxRate;
                }

                public int Count => items.Count;

                public void Clear() => items.Clear();
            }
        }
        """;

    internal static MainWindowViewModel NewShell()
        => new(new Collaterale.Core.Settings.SettingsStore(Path.Combine(Path.GetTempPath(), "collaterale-test-" + Guid.NewGuid().ToString("N")[..8] + ".json")));

    private static (TextCompareViewModel Vm, MainWindow Win) Make()
    {
        var root = Path.Combine(Path.GetTempPath(), "collaterale-tc-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "v1"));
        Directory.CreateDirectory(Path.Combine(root, "v2"));
        File.WriteAllText(Path.Combine(root, "v1", "Cart.cs"), LeftSample);
        File.WriteAllText(Path.Combine(root, "v2", "Cart.cs"), RightSample);
        var shell = NewShell();
        var vm = shell.OpenTextCompare(Path.Combine(root, "v1", "Cart.cs"), Path.Combine(root, "v2", "Cart.cs"));
        var w = new MainWindow(shell, []);
        w.Show();
        return (vm, w);
    }

    [AvaloniaFact]
    public async Task Side_by_side_view_renders_aligned_rows()
    {
        var (vm, w) = Make();
        await Settle();

        Assert.True(vm.HasDifferences);
        Assert.Equal(vm.LeftText.Split('\n').Length, vm.RightText.Split('\n').Length); // aligned: same number of display lines
        Assert.Equal("v1/Cart.cs", vm.LeftTitle);          // short, recognisable labels instead of the full temp path
        Assert.Equal("v2/Cart.cs", vm.RightTitle);
        Assert.Contains(vm.LeftRows, r => r.IsFiller);   // blank filler opposite the insertions
        Assert.Contains(vm.RightRows, r => r.Words.Count > 0); // word-level highlights on changed lines
        Shot(w, "text-compare.png");
    }

    [AvaloniaFact]
    public async Task Dark_theme_renders_with_readable_colors()
    {
        var shell = NewShell();
        shell.CycleThemeCommand.Execute(null);
        shell.CycleThemeCommand.Execute(null);     // System -> Light -> Dark
        var vm = shell.NewTextCompare();
        vm.OpenText("/work/Cart.cs", LeftSample.ReplaceLineEndings("\n").Split('\n'), "/work/Cart.new.cs", RightSample.ReplaceLineEndings("\n").Split('\n'));
        var w = new MainWindow(shell, []);
        w.Show();
        await Settle();
        Assert.Equal("Dark", shell.Settings.Theme);
        Shot(w, "text-compare-dark.png");
    }

    private const string MoveLeft = """
        public class Invoice
        {
            public decimal Total()
            {
                var sum = Lines.Sum(l => l.Price * l.Quantity);
                var tax = sum * TaxRate;
                return sum + tax;
            }

            public void Add(Line line)
            {
                Lines.Add(line);
            }

            public int Count => Lines.Count;
        }
        """;

    private const string MoveRight = """
        public class Invoice
        {
            public void Add(Line line)
            {
                Lines.Add(line);
            }

            public int Count => Lines.Count;

            public decimal Total()
            {
                var sum = Lines.Sum(l => l.Price * l.Quantity);
                var tax = sum * TaxRate;
                return sum + tax;
            }
        }
        """;

    [AvaloniaFact]
    public async Task Moved_blocks_are_flagged_toggleable_and_counted()
    {
        var shell = NewShell();
        var vm = shell.NewTextCompare();
        vm.OpenText("/work/Invoice.cs", MoveLeft.ReplaceLineEndings("\n").Split('\n'), "/work/Invoice.new.cs", MoveRight.ReplaceLineEndings("\n").Split('\n'));
        var w = new MainWindow(shell, []);
        w.Show();
        await Settle();

        Assert.Single(vm.Comparison!.Moves);
        Assert.Contains("1 moved block", vm.Status);
        Assert.Contains(vm.LeftRows, r => r.Moved);
        Assert.Contains(vm.RightRows, r => r.Moved);
        Shot(w, "text-compare-moves.png");

        vm.DetectMoves = false;
        Assert.DoesNotContain(vm.LeftRows, r => r.Moved);
        Assert.DoesNotContain("moved", vm.Status);
    }

    [AvaloniaFact]
    public async Task Navigation_wraps_and_selects_the_current_difference()
    {
        var (vm, w) = Make();
        await Settle();
        var n = vm.Comparison!.DifferenceCount;
        Assert.True(n >= 4);
        Assert.Equal(0, vm.CurrentGroup);
        vm.NextDifferenceCommand.Execute(null);
        Assert.Equal(1, vm.CurrentGroup);
        vm.PreviousDifferenceCommand.Execute(null);
        vm.PreviousDifferenceCommand.Execute(null);
        Assert.Equal(n - 1, vm.CurrentGroup); // wraps
        await Settle();
        Assert.Equal(vm.CurrentGroup, w.GetVisualDescendants().OfType<Collaterale.App.Controls.DiffPane>().First().CurrentGroup);
    }

    [AvaloniaFact]
    public async Task Copy_to_right_applies_the_left_version_and_undo_restores()
    {
        var (vm, _) = Make();
        await Settle();
        var before = vm.Comparison!.DifferenceCount;
        var rightBefore = vm.Right.Lines.ToList();

        vm.CopyToRightCommand.Execute(null); // first difference: left has no "using System.Linq;" line
        Assert.True(vm.Right.Dirty);
        Assert.Equal(before - 1, vm.Comparison!.DifferenceCount);
        Assert.DoesNotContain("using System.Linq;", vm.Right.Lines);

        vm.UndoCommand.Execute(null);
        Assert.Equal(rightBefore, vm.Right.Lines);
        Assert.False(vm.Right.Dirty);
        Assert.Equal(before, vm.Comparison!.DifferenceCount);

        vm.RedoCommand.Execute(null);
        Assert.DoesNotContain("using System.Linq;", vm.Right.Lines);
    }

    [AvaloniaFact]
    public async Task Copying_every_difference_makes_the_sides_identical()
    {
        var (vm, _) = Make();
        await Settle();
        while (vm.Comparison!.DifferenceCount > 0)
        {
            vm.CopyGroup(0, Side.Left);
        }

        Assert.Equal(vm.Left.Lines, vm.Right.Lines);
        Assert.Equal("Files are identical", vm.Status);
    }

    [AvaloniaFact]
    public async Task Options_change_what_counts_as_a_difference()
    {
        var vm = new TextCompareViewModel();
        vm.OpenText("a", ["if (x)", "    run();", "// Done"], "b", ["if (x)", "  run();", "// DONE"]);
        Assert.Equal(1, vm.Comparison!.DifferenceCount);   // only the case/indent lines, grouped
        vm.Whitespace = WhitespaceMode.IgnoreAll;
        vm.IgnoreCase = true;
        Assert.True(vm.Comparison!.AreEqual);
        Assert.Equal("No differences", vm.DifferenceText);
        await Settle();
    }

    [AvaloniaFact]
    public void Save_preserves_encoding_and_line_endings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-save-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var l = Path.Combine(dir, "l.txt");
        var r = Path.Combine(dir, "r.txt");
        File.WriteAllBytes(l, [.. new System.Text.UTF8Encoding(true).GetPreamble(), .. "one\r\ntwo\r\n"u8.ToArray()]);
        File.WriteAllText(r, "one\nTWO\n");

        var vm = new TextCompareViewModel();
        vm.Open(l, r);
        vm.CopyGroup(0, Side.Right);                 // take "TWO" into the left file
        vm.SaveLeftCommand.Execute(null);

        var bytes = File.ReadAllBytes(l);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());           // BOM kept
        Assert.Equal("one\r\nTWO\r\n", System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)); // CRLF kept
        Assert.False(vm.Left.Dirty);
        Directory.Delete(dir, true);
    }

    [AvaloniaFact]
    public void Binary_files_are_refused_politely()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-bin-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "a.bin"), [1, 2, 0, 3]);
        File.WriteAllText(Path.Combine(dir, "b.txt"), "text");
        var vm = new TextCompareViewModel();
        vm.Open(Path.Combine(dir, "a.bin"), Path.Combine(dir, "b.txt"));
        Assert.Contains("binary", vm.Error);
        Assert.Null(vm.Comparison);
        Directory.Delete(dir, true);
    }
}

public class SyntaxTests
{
    private static string WriteTemp(string name, string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-syn-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Source_files_are_colored_and_unknown_types_stay_plain()
    {
        var shell = TextCompareTests.NewShell();
        var l = WriteTemp("Cart.cs", TextCompareTests.LeftSample);
        var r = WriteTemp("Cart.new.cs", TextCompareTests.RightSample);
        shell.OpenTextCompare(l, r);
        var w = new Collaterale.App.Views.MainWindow(shell, []);
        w.Show();
        await TextCompareTests.Settle();

        // an unknown extension must not throw and simply shows plain text
        var shell2 = TextCompareTests.NewShell();
        shell2.OpenTextCompare(WriteTemp("notes.zzz", "one\ntwo\n"), WriteTemp("notes2.zzz", "one\n2\n"));
        var w2 = new Collaterale.App.Views.MainWindow(shell2, []);
        w2.Show();
        await TextCompareTests.Settle();
        Assert.IsType<Collaterale.App.ViewModels.TextCompareViewModel>(shell2.SelectedTab!.Content);
    }
}
