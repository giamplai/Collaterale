using System.Diagnostics;
using Collaterale.Core.Imaging;

namespace Collaterale.Core.Tests;

public class ImageComparisonTests
{
    private static RgbaImage Solid(int w, int h, byte r = 10, byte g = 20, byte b = 30, byte a = 255) => RgbaImage.Create(w, h, (_, _) => (r, g, b, a));

    private static RgbaImage With(RgbaImage src, params (int X, int Y, byte R, byte G, byte B)[] changes)
    {
        var copy = (byte[])src.Pixels.Clone();
        foreach (var (x, y, r, g, b) in changes)
        {
            var i = (y * src.Width + x) * 4;
            copy[i] = r; copy[i + 1] = g; copy[i + 2] = b;
        }

        return new RgbaImage(src.Width, src.Height, copy);
    }

    [Fact]
    public void Identical_images_are_equal_with_infinite_psnr()
    {
        var c = ImageComparison.Compare(Solid(8, 8), Solid(8, 8));
        Assert.True(c.AreEqual);
        Assert.Equal(0, c.DifferingPixels);
        Assert.Empty(c.Regions);
        Assert.True(double.IsPositiveInfinity(c.Psnr));
    }

    [Fact]
    public void A_single_changed_pixel_is_found_exactly()
    {
        var a = Solid(40, 30);
        var c = ImageComparison.Compare(a, With(a, (12, 7, 200, 20, 30)));
        Assert.Equal(1, c.DifferingPixels);
        Assert.True(c.IsDifferent(12, 7));
        Assert.False(c.IsDifferent(11, 7));
        Assert.Equal(190, c.MaxDelta);
        Assert.Equal([new ImageRect(12, 7, 1, 1)], c.Regions);
    }

    [Fact]
    public void Tolerance_ignores_small_color_differences()
    {
        var a = Solid(10, 10);
        var b = With(a, (3, 3, 13, 20, 30), (4, 4, 40, 20, 30));       // deltas of 3 and 30
        Assert.Equal(2, ImageComparison.Compare(a, b).DifferingPixels);
        Assert.Equal(1, ImageComparison.Compare(a, b, new ImageOptions { Tolerance = 5 }).DifferingPixels);
        Assert.Equal(0, ImageComparison.Compare(a, b, new ImageOptions { Tolerance = 30 }).DifferingPixels);
    }

    [Fact]
    public void Alpha_can_be_ignored()
    {
        var a = Solid(4, 4, a: 255);
        var b = Solid(4, 4, a: 128);
        Assert.Equal(16, ImageComparison.Compare(a, b).DifferingPixels);
        Assert.True(ImageComparison.Compare(a, b, new ImageOptions { IgnoreAlpha = true }).AreEqual);
    }

    [Fact]
    public void Different_sizes_count_the_extra_area_as_different()
    {
        var c = ImageComparison.Compare(Solid(10, 10), Solid(12, 10));
        Assert.True(c.SizeDiffers);
        Assert.False(c.AreEqual);
        Assert.Equal((12, 10), (c.Width, c.Height));
        Assert.Equal(20, c.DifferingPixels);                  // the two extra columns
        Assert.False(c.IsDifferent(5, 5));
        Assert.True(c.IsDifferent(11, 0));
    }

    [Fact]
    public void Nearby_changes_form_one_region_and_far_apart_ones_form_two()
    {
        var a = Solid(200, 100);
        var b = With(a, (10, 10, 255, 0, 0), (14, 12, 255, 0, 0), (150, 80, 255, 0, 0));
        var c = ImageComparison.Compare(a, b);
        Assert.Equal(2, c.Regions.Count);
        Assert.Equal(new ImageRect(10, 10, 5, 3), c.Regions[0]);      // tight box around the two close pixels
        Assert.Equal(new ImageRect(150, 80, 1, 1), c.Regions[1]);
    }

    [Fact]
    public void Touching_cells_across_a_cell_boundary_are_merged()
    {
        var a = Solid(64, 16);
        var b = With(a, (15, 5, 0, 0, 0), (16, 5, 0, 0, 0));          // adjacent pixels on either side of a 16 px cell edge
        var c = ImageComparison.Compare(a, b, new ImageOptions { RegionCell = 16 });
        Assert.Single(c.Regions);
        Assert.Equal(new ImageRect(15, 5, 2, 1), c.Regions[0]);
    }

    [Fact]
    public void Statistics_reflect_how_different_the_images_are()
    {
        var a = Solid(10, 10, 100, 100, 100);
        var slight = Solid(10, 10, 102, 100, 100);
        var big = Solid(10, 10, 200, 200, 200);
        Assert.True(ImageComparison.Compare(a, slight).Psnr > ImageComparison.Compare(a, big).Psnr);
        Assert.Equal(100.0, ImageComparison.Compare(a, big).DifferingPercent);
    }

    [Fact]
    public void Region_count_is_capped()
    {
        var a = Solid(400, 400);
        var changes = Enumerable.Range(0, 20).SelectMany(i => Enumerable.Range(0, 20).Select(j => (i * 20 + 5, j * 20 + 5, (byte)255, (byte)0, (byte)0))).ToArray();
        var c = ImageComparison.Compare(a, With(a, changes), new ImageOptions { RegionCell = 4, MaxRegions = 50 });
        Assert.True(c.RegionsTruncated);
        Assert.Equal(50, c.Regions.Count);
        Assert.Equal(400, c.DifferingPixels);                  // statistics still cover everything
    }

    [Fact]
    public void Mismatched_buffers_are_rejected()
        => Assert.Throws<ArgumentException>(() => new RgbaImage(2, 2, new byte[10]));

    [Fact]
    public void Large_images_compare_quickly()
    {
        var a = Solid(4000, 3000);
        var b = With(a, (10, 10, 0, 0, 0), (3990, 2990, 0, 0, 0));
        var sw = Stopwatch.StartNew();
        var c = ImageComparison.Compare(a, b);
        sw.Stop();
        Assert.Equal(2, c.DifferingPixels);
        Assert.Equal(2, c.Regions.Count);
        Assert.True(sw.ElapsedMilliseconds < 4000, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Cancellation_is_honoured()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => ImageComparison.Compare(Solid(200, 200), Solid(200, 200), null, cts.Token));
    }
}
