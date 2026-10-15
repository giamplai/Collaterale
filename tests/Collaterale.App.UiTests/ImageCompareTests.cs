using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Collaterale.App.Controls;
using Collaterale.App.ViewModels;
using Collaterale.App.Views;
using Collaterale.Core.Imaging;

namespace Collaterale.App.UiTests;

public class ImageCompareTests
{
    /// <summary>A mock application window: title bar, sidebar, content lines and one button.</summary>
    private static RgbaImage Mock(bool changed)
    {
        const int W = 480, H = 320;
        return RgbaImage.Create(W, H, (x, y) =>
        {
            (byte, byte, byte, byte) c = (245, 246, 250, 255);                                  // window background
            if (y < 28) { c = (52, 61, 94, 255); }                                              // title bar
            else if (x < 110) { c = (232, 235, 244, 255); }                                     // sidebar
            if (y is >= 60 and < 68 && x is >= 130 and < 420) { c = (170, 176, 196, 255); }     // text lines
            if (y is >= 84 and < 92 && x is >= 130 and < 380) { c = (170, 176, 196, 255); }
            if (y is >= 108 and < 116 && x is >= 130 and < 400) { c = (170, 176, 196, 255); }
            // the button: blue and bottom-left in the old version, green and moved right in the new one
            var (bx, by, bc) = changed ? (330, 250, ((byte)46, (byte)170, (byte)110)) : (140, 250, ((byte)61, (byte)90, (byte)224));
            if (x >= bx && x < bx + 110 && y >= by && y < by + 34) { c = (bc.Item1, bc.Item2, bc.Item3, 255); }
            if (changed && y is >= 140 and < 170 && x is >= 130 and < 440) { c = (255, 224, 140, 255); }   // a new warning banner
            return c;
        });
    }

    private static (string Left, string Right, string Dir) MakeFiles(RgbaImage? left = null, RgbaImage? right = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-img-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(dir, "v1")); Directory.CreateDirectory(Path.Combine(dir, "v2"));
        var (l, r) = (Path.Combine(dir, "v1", "home.png"), Path.Combine(dir, "v2", "home.png"));
        File.WriteAllBytes(l, ImageLoader.EncodePng(left ?? Mock(false)));
        File.WriteAllBytes(r, ImageLoader.EncodePng(right ?? Mock(true)));
        return (l, r, dir);
    }

    [AvaloniaFact]
    public void Png_encode_and_decode_roundtrip_exactly()
    {
        var img = Mock(true);
        using var ms = new MemoryStream(ImageLoader.EncodePng(img));
        var back = ImageLoader.Decode(ms);
        Assert.Equal((img.Width, img.Height), (back.Width, back.Height));
        Assert.True(ImageComparison.Compare(img, back).AreEqual);
    }

    [AvaloniaFact]
    public async Task Differences_are_found_as_regions_and_rendered()
    {
        var (l, r, _) = MakeFiles();
        var shell = TextCompareTests.NewShell();
        var vm = shell.OpenImageCompare(l, r);
        var w = new MainWindow(shell, []);
        w.Show();
        await TextCompareTests.Settle();

        var c = vm.Comparison!;
        Assert.False(c.AreEqual);
        Assert.False(c.SizeDiffers);
        Assert.True(c.Regions.Count >= 3);                         // old button, new button, new banner
        Assert.Contains(c.Regions, rg => rg.Contains(150, 260));   // where the old button was
        Assert.Contains(c.Regions, rg => rg.Contains(340, 260));   // where the new button is
        Assert.Contains(c.Regions, rg => rg.Contains(200, 150));   // the banner
        Assert.Contains("region", vm.StatsText);
        Assert.Equal("v1/home.png", vm.LeftTitle);
        Assert.Equal("480 × 320", vm.LeftInfo);
        TextCompareTests.Shot(w, "image-compare.png");
    }

    [AvaloniaFact]
    public async Task All_four_view_modes_render()
    {
        var (l, r, _) = MakeFiles();
        var shell = TextCompareTests.NewShell();
        var vm = shell.OpenImageCompare(l, r);
        var w = new MainWindow(shell, []);
        w.Show();
        foreach (var mode in Enum.GetValues<ImageViewMode>())
        {
            vm.Mode = mode;
            await TextCompareTests.Settle();
            Assert.NotNull(w.CaptureRenderedFrame());
            if (mode == ImageViewMode.Swipe) { TextCompareTests.Shot(w, "image-compare-swipe.png"); }
        }

        Assert.Equal(ImageViewMode.Swipe, w.GetVisualDescendants().OfType<ImageCanvas>().Single().Mode);
    }

    [AvaloniaFact]
    public async Task Tolerance_hides_noise_but_not_real_changes()
    {
        var baseImg = Mock(false);
        // add faint "compression noise" to every pixel of the right picture
        var noisy = RgbaImage.Create(baseImg.Width, baseImg.Height, (x, y) =>
        {
            var p = baseImg.At(x, y);
            var n = (x * 7 + y * 13) % 5 - 2;                      // −2..+2
            return ((byte)Math.Clamp(p.R + n, 0, 255), (byte)Math.Clamp(p.G + n, 0, 255), (byte)Math.Clamp(p.B + n, 0, 255), p.A);
        });
        var (l, r, _) = MakeFiles(baseImg, noisy);
        var shell = TextCompareTests.NewShell();
        var vm = shell.OpenImageCompare(l, r);
        await TextCompareTests.Settle();
        Assert.True(vm.Comparison!.DifferingPixels > 10_000);       // noise everywhere

        vm.Tolerance = 3;
        Assert.True(vm.Comparison!.AreEqual);                       // gone

        var (l2, r2, _) = MakeFiles(Mock(false), Mock(true));
        var vm2 = shell.OpenImageCompare(l2, r2);
        vm2.Tolerance = 3;
        Assert.False(vm2.Comparison!.AreEqual);                      // the real changes are far bigger than the tolerance
    }

    [AvaloniaFact]
    public async Task Region_navigation_wraps_and_hover_describes_pixels()
    {
        var (l, r, _) = MakeFiles();
        var shell = TextCompareTests.NewShell();
        var vm = shell.OpenImageCompare(l, r);
        await TextCompareTests.Settle();
        var n = vm.Comparison!.Regions.Count;
        Assert.Equal(0, vm.CurrentRegion);
        for (var i = 0; i < n; i++) { vm.NextRegionCommand.Execute(null); }
        Assert.Equal(0, vm.CurrentRegion);
        vm.PreviousRegionCommand.Execute(null);
        Assert.Equal(n - 1, vm.CurrentRegion);

        vm.Hover(150, 260);                                         // inside the old button: blue on the left, background on the right
        Assert.Contains("(150, 260)", vm.PixelInfo);
        Assert.Contains("#3D5AE0", vm.PixelInfo);
        Assert.Contains("≠", vm.PixelInfo);
        vm.Hover(5, 5);                                             // title bar: identical
        Assert.DoesNotContain("≠", vm.PixelInfo);
        vm.Hover(-1, -1);
        Assert.Equal("", vm.PixelInfo);
    }

    [AvaloniaFact]
    public void Different_sizes_are_reported()
    {
        var small = RgbaImage.Create(100, 80, (_, _) => (200, 200, 200, 255));
        var big = RgbaImage.Create(120, 80, (_, _) => (200, 200, 200, 255));
        var (l, r, _) = MakeFiles(small, big);
        var vm = TextCompareTests.NewShell().OpenImageCompare(l, r);
        Assert.True(vm.Comparison!.SizeDiffers);
        Assert.Contains("Different sizes", vm.StatsText);
    }

    [AvaloniaFact]
    public void Identical_pictures_say_so()
    {
        var (l, r, _) = MakeFiles(Mock(false), Mock(false));
        var vm = TextCompareTests.NewShell().OpenImageCompare(l, r);
        Assert.Equal("The pictures are identical", vm.StatsText);
        Assert.Empty(vm.Comparison!.Regions);
        Assert.Null(vm.DiffBitmap);
    }

    [AvaloniaFact]
    public void A_file_that_is_not_a_picture_gives_a_clear_error()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-imgbad-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "notes.png"), "this is not a picture");
        var (l, _, _) = MakeFiles();
        var vm = TextCompareTests.NewShell().OpenImageCompare(l, Path.Combine(dir, "notes.png"));
        Assert.Contains("not a picture", vm.Error);
        Assert.Null(vm.Comparison);
    }

    [AvaloniaFact]
    public async Task Zoom_and_fit_do_not_break_rendering()
    {
        var (l, r, _) = MakeFiles();
        var shell = TextCompareTests.NewShell();
        shell.OpenImageCompare(l, r);
        var w = new MainWindow(shell, []);
        w.Show();
        await TextCompareTests.Settle();
        var canvas = w.GetVisualDescendants().OfType<ImageCanvas>().Single();
        canvas.ZoomBy(2); canvas.SetZoom100(); canvas.CenterOn(new ImageRect(300, 240, 50, 40));
        await TextCompareTests.Settle();
        Assert.NotNull(w.CaptureRenderedFrame());
        canvas.FitToWindow();
        await TextCompareTests.Settle();
        Assert.NotNull(w.CaptureRenderedFrame());
    }

    [AvaloniaFact]
    public void Launch_with_image_flag_opens_a_picture_session()
    {
        var (l, r, _) = MakeFiles();
        var shell = TextCompareTests.NewShell();
        shell.Launch(Collaterale.Core.Cli.LaunchParser.Parse(["--image", l, r]));
        Assert.IsType<ImageCompareViewModel>(shell.SelectedTab!.Content);
    }
}
