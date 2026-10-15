namespace Collaterale.Core.Imaging;

/// <summary>A decoded picture: 8-bit RGBA, straight (not premultiplied) alpha, row-major.</summary>
public sealed class RgbaImage(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels.Length == checked(width * height * 4) ? pixels : throw new ArgumentException("Pixel buffer does not match the image size.", nameof(pixels));

    public static RgbaImage Create(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> fill)
    {
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = fill(x, y);
                var i = (y * width + x) * 4;
                data[i] = r; data[i + 1] = g; data[i + 2] = b; data[i + 3] = a;
            }
        }

        return new RgbaImage(width, height, data);
    }

    public (byte R, byte G, byte B, byte A) At(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }
}

public sealed record ImageOptions
{
    /// <summary>Largest per-channel difference (0..255) that still counts as "the same". 0 = exact.</summary>
    public int Tolerance { get; init; }

    /// <summary>Compare only the colors, not transparency.</summary>
    public bool IgnoreAlpha { get; init; }

    /// <summary>Side length of the cells used to group differing pixels into rectangles.</summary>
    public int RegionCell { get; init; } = 16;

    /// <summary>Stop listing rectangles after this many (the pixel statistics still cover everything).</summary>
    public int MaxRegions { get; init; } = 5000;
}

public readonly record struct ImageRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
}

public sealed class ImageComparison
{
    private ImageComparison(int width, int height, bool sizeDiffers, byte[] mask, long differing, int maxDelta, double mse, List<ImageRect> regions, bool truncated)
    {
        Width = width;
        Height = height;
        SizeDiffers = sizeDiffers;
        Mask = mask;
        DifferingPixels = differing;
        MaxDelta = maxDelta;
        MeanSquaredError = mse;
        Regions = regions;
        RegionsTruncated = truncated;
    }

    /// <summary>The compared area: the larger of the two pictures in each direction.</summary>
    public int Width { get; }
    public int Height { get; }

    /// <summary>The two pictures have different dimensions; pixels outside the smaller one count as different.</summary>
    public bool SizeDiffers { get; }

    /// <summary>One byte per pixel of the compared area: 1 where the pictures differ.</summary>
    public byte[] Mask { get; }

    public long DifferingPixels { get; }
    public long TotalPixels => (long)Width * Height;
    public double DifferingPercent => TotalPixels == 0 ? 0 : 100.0 * DifferingPixels / TotalPixels;

    /// <summary>Largest per-channel difference found among pixels present in both pictures.</summary>
    public int MaxDelta { get; }

    public double MeanSquaredError { get; }

    /// <summary>Peak signal-to-noise ratio in dB (higher = more alike; infinity when identical).</summary>
    public double Psnr => MeanSquaredError <= 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / MeanSquaredError);

    public IReadOnlyList<ImageRect> Regions { get; }
    public bool RegionsTruncated { get; }
    public bool AreEqual => DifferingPixels == 0 && !SizeDiffers;

    public bool IsDifferent(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height && Mask[y * Width + x] != 0;

    public static ImageComparison Compare(RgbaImage a, RgbaImage b, ImageOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ImageOptions();
        int w = Math.Max(a.Width, b.Width), h = Math.Max(a.Height, b.Height);
        int cw = Math.Min(a.Width, b.Width), ch = Math.Min(a.Height, b.Height);
        var mask = new byte[checked(w * h)];
        long differing = 0;
        var maxDelta = 0;
        double sumSq = 0;
        long counted = 0;
        var tol = options.Tolerance;
        var channels = options.IgnoreAlpha ? 3 : 4;

        for (var y = 0; y < h; y++)
        {
            if ((y & 63) == 0) { ct.ThrowIfCancellationRequested(); }
            for (var x = 0; x < w; x++)
            {
                if (x >= cw || y >= ch)
                {
                    mask[y * w + x] = 1;          // only one picture reaches this pixel
                    differing++;
                    continue;
                }

                int ia = (y * a.Width + x) * 4, ib = (y * b.Width + x) * 4;
                var worst = 0;
                for (var k = 0; k < channels; k++)
                {
                    var d = Math.Abs(a.Pixels[ia + k] - b.Pixels[ib + k]);
                    if (d > worst) { worst = d; }
                    sumSq += (double)d * d;
                }

                counted += channels;
                if (worst > maxDelta) { maxDelta = worst; }
                if (worst > tol)
                {
                    mask[y * w + x] = 1;
                    differing++;
                }
            }
        }

        var mse = counted == 0 ? 0 : sumSq / counted;
        var (regions, truncated) = BuildRegions(mask, w, h, Math.Max(1, options.RegionCell), options.MaxRegions);
        return new ImageComparison(w, h, a.Width != b.Width || a.Height != b.Height, mask, differing, maxDelta, mse, regions, truncated);
    }

    /// <summary>Groups differing pixels: the area is cut into cells, cells with differences that touch (8-neighbourhood) are merged, and each group becomes its bounding box.</summary>
    private static (List<ImageRect> Regions, bool Truncated) BuildRegions(byte[] mask, int w, int h, int cell, int max)
    {
        int gw = (w + cell - 1) / cell, gh = (h + cell - 1) / cell;
        var marked = new bool[gw * gh];
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            var gy = y / cell * gw;
            for (var x = 0; x < w; x++)
            {
                if (mask[row + x] != 0) { marked[gy + x / cell] = true; }
            }
        }

        var seen = new bool[marked.Length];
        var regions = new List<ImageRect>();
        var stack = new Stack<int>();
        for (var start = 0; start < marked.Length; start++)
        {
            if (!marked[start] || seen[start]) { continue; }
            if (regions.Count >= max) { return (regions, true); }

            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                int cx = c % gw, cy = c / gw;
                minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx);
                minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= gw || ny >= gh) { continue; }
                        var n = ny * gw + nx;
                        if (marked[n] && !seen[n]) { seen[n] = true; stack.Push(n); }
                    }
                }
            }

            // tighten the box to the real differing pixels inside the cell block
            var x0 = minX * cell; var y0 = minY * cell;
            var x1 = Math.Min(w, (maxX + 1) * cell); var y1 = Math.Min(h, (maxY + 1) * cell);
            int tx0 = x1, ty0 = y1, tx1 = x0, ty1 = y0;
            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++)
                {
                    if (mask[y * w + x] == 0) { continue; }
                    if (x < tx0) { tx0 = x; }
                    if (x >= tx1) { tx1 = x + 1; }
                    if (y < ty0) { ty0 = y; }
                    if (y >= ty1) { ty1 = y + 1; }
                }
            }

            regions.Add(new ImageRect(tx0, ty0, tx1 - tx0, ty1 - ty0));
        }

        regions.Sort((p, q) => p.Y != q.Y ? p.Y.CompareTo(q.Y) : p.X.CompareTo(q.X));
        return (regions, false);
    }
}
