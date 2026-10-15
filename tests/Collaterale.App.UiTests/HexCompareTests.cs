using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Collaterale.App.Controls;
using Collaterale.App.ViewModels;
using Collaterale.App.Views;
using Collaterale.Core.Hex;

namespace Collaterale.App.UiTests;

public class HexCompareTests
{
    /// <summary>A small fake file format: magic, a few header fields, some text and a table of numbers.</summary>
    private static byte[] Sample(int version, int checksum, int tableValue)
    {
        var ms = new MemoryStream();
        ms.Write("COLT"u8);
        ms.WriteByte((byte)version); ms.WriteByte(0); ms.WriteByte(0); ms.WriteByte(0);
        ms.Write(BitConverter.GetBytes(checksum));
        ms.Write("Hello, binary world! This text sits in the middle of the file."u8);
        for (var i = 0; i < 48; i++) { ms.Write(BitConverter.GetBytes(i == 20 ? tableValue : i * 3)); }
        ms.Write("END!"u8);
        return ms.ToArray();
    }

    private static (MainWindowViewModel Shell, HexCompareViewModel Vm, MainWindow Win) Make(byte[] left, byte[] right)
    {
        var shell = TextCompareTests.NewShell();
        var vm = shell.NewHexCompare();
        vm.OpenBytes("v1/firmware.bin", left, "v2/firmware.bin", right);
        var w = new MainWindow(shell, []);
        w.Show();
        return (shell, vm, w);
    }

    [AvaloniaFact]
    public async Task Hex_view_renders_both_dumps_with_differences_highlighted()
    {
        var (_, vm, w) = Make(Sample(1, 0x1234, 60), Sample(2, 0x9999, 4242));
        await TextCompareTests.Settle();

        Assert.True(vm.HasDifferences);
        Assert.True(vm.Comparison!.Ranges.Count >= 3);                    // version, checksum, table entry
        Assert.Equal(0, vm.CurrentRange);
        Assert.Equal("Difference 1 of " + vm.Comparison.Ranges.Count, vm.DifferenceText);

        vm.Select(vm.Comparison.Ranges[0].Offset);                        // select the version byte so the inspector shows something
        await TextCompareTests.Settle();
        TextCompareTests.Shot(w, "hex-compare.png");
        Assert.Contains(vm.LeftInspector, l => l.Label == "Unsigned" && l.Value == "1");
        Assert.Contains(vm.RightInspector, l => l.Label == "Unsigned" && l.Value == "2");
    }

    [AvaloniaFact]
    public async Task Navigation_wraps_and_selects_the_start_of_each_run()
    {
        var (_, vm, _) = Make(Sample(1, 1, 5), Sample(2, 2, 6));
        await TextCompareTests.Settle();
        var n = vm.Comparison!.Ranges.Count;
        var start = vm.CurrentRange;
        for (var i = 0; i < n; i++) { vm.NextDifferenceCommand.Execute(null); }
        Assert.Equal(start, vm.CurrentRange);
        vm.PreviousDifferenceCommand.Execute(null);
        Assert.Equal(n - 1, vm.CurrentRange);
        Assert.Equal(vm.Comparison.Ranges[n - 1].Offset, vm.SelectedOffset);
    }

    [AvaloniaFact]
    public async Task Copying_a_run_overwrites_the_other_side_and_undo_restores_it()
    {
        var left = Sample(1, 1, 5);
        var right = Sample(2, 2, 6);
        var (_, vm, _) = Make(left, right);
        await TextCompareTests.Settle();
        var runs = vm.Comparison!.Ranges.Count;

        vm.CopyToRightCommand.Execute(null);                              // first difference: left's version byte goes right
        Assert.True(vm.Right.Dirty);
        Assert.Equal(runs - 1, vm.Comparison!.Ranges.Count);
        Assert.Equal(1, vm.Right.Data[4]);

        vm.UndoCommand.Execute(null);
        Assert.Equal(right, vm.Right.Data);
        Assert.False(vm.Right.Dirty);
        Assert.Equal(runs, vm.Comparison!.Ranges.Count);
    }

    [AvaloniaFact]
    public async Task Copying_every_run_makes_the_files_identical_including_different_lengths()
    {
        var left = Sample(1, 1, 5).Concat(new byte[] { 9, 9, 9 }).ToArray();   // longer on the left
        var right = Sample(2, 2, 6);
        var (_, vm, _) = Make(left, right);
        await TextCompareTests.Settle();
        var guard = 0;
        while (vm.Comparison!.Ranges.Count > 0 && guard++ < 50) { vm.CopyRange(0, Side.Left); }
        Assert.Equal(left, vm.Right.Data);
        Assert.True(vm.Comparison.AreEqual);

        // and the other way: copying from the shorter side shortens the target
        var (_, vm2, _) = Make(left, right);
        guard = 0;
        while (vm2.Comparison!.Ranges.Count > 0 && guard++ < 50) { vm2.CopyRange(0, Side.Right); }
        Assert.Equal(right, vm2.Left.Data);
    }

    [AvaloniaFact]
    public async Task Save_writes_the_edited_bytes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-hex-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "a.bin"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(dir, "b.bin"), [1, 9, 3, 4]);
        var shell = TextCompareTests.NewShell();
        var vm = shell.OpenHexCompare(Path.Combine(dir, "a.bin"), Path.Combine(dir, "b.bin"));
        await TextCompareTests.Settle();

        vm.CopyToRightCommand.Execute(null);
        vm.SaveRightCommand.Execute(null);
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(Path.Combine(dir, "b.bin")));
        Assert.False(vm.Right.Dirty);
        Directory.Delete(dir, true);
    }

    [Theory]
    [InlineData("0x1A40", 0x1A40L)]
    [InlineData("1a40h", 0x1A40L)]
    [InlineData("  6720 ", 6720L)]
    [InlineData("0xFF_FF", 0xFFFFL)]
    public void Offsets_parse_as_hex_or_decimal(string text, long expected)
    {
        Assert.True(HexCompareViewModel.TryParseOffset(text, out var v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("zz")]
    [InlineData("0x")]
    public void Bad_offsets_are_rejected(string text) => Assert.False(HexCompareViewModel.TryParseOffset(text, out _));

    [AvaloniaFact]
    public async Task Inspector_reads_multibyte_values_and_handles_the_end_of_the_file()
    {
        var (_, vm, _) = Make([0x01, 0x00, 0x00, 0x00, 0xFF], [0x01, 0x00, 0x00, 0x00]);
        await TextCompareTests.Settle();
        vm.Select(0);
        Assert.Contains(vm.LeftInspector, l => l.Label == "UInt32 LE / BE" && l.Value == "1 / 16777216");
        vm.Select(4);                                                      // only the left file has a byte here
        Assert.Contains(vm.LeftInspector, l => l.Label == "Hex" && l.Value == "FF");
        Assert.Contains(vm.RightInspector, l => l.Value.Contains("no data"));
    }

    [AvaloniaFact]
    public async Task Bytes_per_row_and_scrolling_work_on_a_large_file()
    {
        var a = new byte[20 * 1024 * 1024];
        var b = a.ToArray();
        b[10_000_000] = 1;
        var (_, vm, w) = Make(a, b);
        await TextCompareTests.Settle();
        var canvas = w.GetVisualDescendants().OfType<HexCanvas>().Single();

        Assert.Single(vm.Comparison!.Ranges);
        Assert.Equal(20 * 1024 * 1024 / 16, canvas.TotalRows);
        vm.NextDifferenceCommand.Execute(null);
        await TextCompareTests.Settle();
        Assert.InRange(canvas.TopRow, 10_000_000 / 16 - canvas.VisibleRows, 10_000_000 / 16);   // scrolled to the difference

        vm.BytesPerRow = 32;
        await TextCompareTests.Settle();
        Assert.Equal(20 * 1024 * 1024 / 32, canvas.TotalRows);
        var diffRow = 10_000_000 / 32;                                      // the difference must still be on screen after the layout change
        Assert.InRange(diffRow, canvas.TopRow, canvas.TopRow + canvas.VisibleRows);
    }

    [AvaloniaFact]
    public void Launch_with_hex_flag_opens_a_hex_session()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-hexcli-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "a"), [1]); File.WriteAllBytes(Path.Combine(dir, "b"), [2]);
        var shell = TextCompareTests.NewShell();
        shell.Launch(Collaterale.Core.Cli.LaunchParser.Parse(["--hex", Path.Combine(dir, "a"), Path.Combine(dir, "b")]));
        Assert.IsType<HexCompareViewModel>(shell.SelectedTab!.Content);
        Directory.Delete(dir, true);
    }
}
