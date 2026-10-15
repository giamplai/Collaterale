using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Collaterale.Core.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collaterale.App.ViewModels;

public enum ImageViewMode { SideBySide, Overlay, Difference, Swipe }

public sealed partial class ImageCompareViewModel : ObservableObject, ISessionContent
{
    private RgbaImage? _left, _right;

    [ObservableProperty] private string? _leftPath;
    [ObservableProperty] private string? _rightPath;
    [ObservableProperty] private ImageComparison? _comparison;
    [ObservableProperty] private ImageViewMode _mode = ImageViewMode.Difference;
    [ObservableProperty] private double _overlayOpacity = 0.5;
    [ObservableProperty] private double _swipe = 0.5;
    [ObservableProperty] private int _tolerance;
    [ObservableProperty] private bool _ignoreAlpha;
    [ObservableProperty] private bool _showRegions = true;
    [ObservableProperty] private int _currentRegion = -1;
    [ObservableProperty] private string _status = "Open two pictures to compare them";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _pixelInfo = "";

    public static ImageViewMode[] Modes { get; } = Enum.GetValues<ImageViewMode>();

    public Bitmap? LeftBitmap { get; private set; }
    public Bitmap? RightBitmap { get; private set; }
    public Bitmap? DiffBitmap { get; private set; }

    public event EventHandler? ImagesChanged;

    public string Title => (LeftPath, RightPath) switch
    {
        (null, null) => "Picture compare",
        (var l, var r) => $"{(l is null ? "…" : Path.GetFileName(l))}  ↔  {(r is null ? "…" : Path.GetFileName(r))}  (picture)",
    };

    public string LeftTitle => PathDisplay.Short(LeftPath, "(no picture)");
    public string RightTitle => PathDisplay.Short(RightPath, "(no picture)");
    public string LeftInfo => _left is null ? "" : $"{_left.Width} × {_left.Height}";
    public string RightInfo => _right is null ? "" : $"{_right.Width} × {_right.Height}";
    public bool HasComparison => Comparison is not null;
    public RgbaImage? LeftImage => _left;
    public RgbaImage? RightImage => _right;

    public string StatsText
    {
        get
        {
            if (Comparison is not { } c) { return ""; }
            if (c.AreEqual) { return "The pictures are identical"; }
            var size = c.SizeDiffers ? $"Different sizes ({LeftInfo} vs {RightInfo}) · " : "";
            var psnr = double.IsPositiveInfinity(c.Psnr) ? "" : $" · PSNR {c.Psnr:F1} dB";
            return $"{size}{c.Regions.Count:N0}{(c.RegionsTruncated ? "+" : "")} region(s) · {c.DifferingPixels:N0} pixels ({c.DifferingPercent:F2}%) · max Δ {c.MaxDelta}{psnr}";
        }
    }

    public string RegionText => Comparison is null ? "" : Comparison.Regions.Count == 0 ? "" : CurrentRegion < 0 ? $"{Comparison.Regions.Count} regions" : $"Region {CurrentRegion + 1} of {Comparison.Regions.Count}";

    public ImageOptions Options => new() { Tolerance = Tolerance, IgnoreAlpha = IgnoreAlpha };

    // ───────────── loading ─────────────

    public void Open(string left, string right)
    {
        try
        {
            Error = null;
            _left = ImageLoader.Load(left);
            _right = ImageLoader.Load(right);
            LeftPath = left;
            RightPath = right;
            Recompare(goToFirst: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Error = ex.Message;
        }
    }

    public void OpenImages(string? leftName, RgbaImage left, string? rightName, RgbaImage right)
    {
        _left = left;
        _right = right;
        LeftPath = leftName;
        RightPath = rightName;
        Recompare(goToFirst: true);
    }

    private string? _pendingLeft, _pendingRight;

    public void OpenSide(Side side, string path)
    {
        if (side == Side.Left) { _pendingLeft = path; } else { _pendingRight = path; }
        _pendingLeft ??= LeftPath;
        _pendingRight ??= RightPath;
        if (_pendingLeft is not null && _pendingRight is not null)
        {
            Open(_pendingLeft, _pendingRight);
        }
        else
        {
            Status = side == Side.Left ? "Now choose the right picture" : "Now choose the left picture";
        }
    }

    [RelayCommand]
    private void Reload()
    {
        if (LeftPath is not null && RightPath is not null) { Open(LeftPath, RightPath); }
    }

    private void Recompare(bool goToFirst = false)
    {
        if (_left is null || _right is null) { return; }
        Comparison = ImageComparison.Compare(_left, _right, Options);
        LeftBitmap = ToBitmap(_left);
        RightBitmap = ToBitmap(_right);
        DiffBitmap = Comparison.DifferingPixels == 0 ? null : ToBitmap(MaskImage(Comparison));
        CurrentRegion = Comparison.Regions.Count == 0 ? -1 : goToFirst || CurrentRegion >= Comparison.Regions.Count ? 0 : Math.Max(CurrentRegion, 0);
        Status = Comparison.AreEqual ? "The pictures are identical" : "Differences found";
        NotifyAll();
        ImagesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LeftTitle));
        OnPropertyChanged(nameof(RightTitle));
        OnPropertyChanged(nameof(LeftInfo));
        OnPropertyChanged(nameof(RightInfo));
        OnPropertyChanged(nameof(HasComparison));
        OnPropertyChanged(nameof(StatsText));
        OnPropertyChanged(nameof(RegionText));
        NextRegionCommand.NotifyCanExecuteChanged();
        PreviousRegionCommand.NotifyCanExecuteChanged();
    }

    partial void OnToleranceChanged(int value) => Recompare();
    partial void OnIgnoreAlphaChanged(bool value) => Recompare();
    partial void OnCurrentRegionChanged(int value) => OnPropertyChanged(nameof(RegionText));

    // ───────────── regions ─────────────

    private bool HasRegions => Comparison is { Regions.Count: > 0 };

    [RelayCommand(CanExecute = nameof(HasRegions))]
    private void NextRegion() => CurrentRegion = (CurrentRegion + 1) % Comparison!.Regions.Count;

    [RelayCommand(CanExecute = nameof(HasRegions))]
    private void PreviousRegion() => CurrentRegion = (CurrentRegion - 1 + Comparison!.Regions.Count) % Comparison.Regions.Count;

    /// <summary>Text for the status bar while hovering a pixel: coordinates, both colors and how far apart they are.</summary>
    public void Hover(int x, int y)
    {
        if (_left is null || _right is null || x < 0 || y < 0) { PixelInfo = ""; return; }
        string Describe(RgbaImage img) => x < img.Width && y < img.Height ? $"#{img.At(x, y).R:X2}{img.At(x, y).G:X2}{img.At(x, y).B:X2}{(img.At(x, y).A < 255 ? $" α{img.At(x, y).A}" : "")}" : "—";
        PixelInfo = $"({x}, {y})   left {Describe(_left)}   right {Describe(_right)}{(Comparison?.IsDifferent(x, y) == true ? "   ≠" : "")}";
    }

    // ───────────── bitmaps ─────────────

    /// <summary>The differing pixels as a translucent magenta overlay; everything else is transparent.</summary>
    public static RgbaImage MaskImage(ImageComparison c)
    {
        var data = new byte[c.Width * c.Height * 4];
        for (var i = 0; i < c.Mask.Length; i++)
        {
            if (c.Mask[i] != 0)
            {
                var o = i * 4;
                data[o] = 255; data[o + 1] = 0; data[o + 2] = 140; data[o + 3] = 230;
            }
        }

        return new RgbaImage(c.Width, c.Height, data);
    }

    public static WriteableBitmap ToBitmap(RgbaImage image)
    {
        var bmp = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var fb = bmp.Lock();
        var rowBytes = image.Width * 4;
        for (var y = 0; y < image.Height; y++)
        {
            Marshal.Copy(image.Pixels, y * rowBytes, fb.Address + y * fb.RowBytes, rowBytes);
        }

        return bmp;
    }
}
