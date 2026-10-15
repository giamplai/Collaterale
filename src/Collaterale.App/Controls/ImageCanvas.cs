using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using Collaterale.App.ViewModels;
using Collaterale.Core.Imaging;

namespace Collaterale.App.Controls;

/// <summary>
/// Shows two pictures with a shared view (pan with the mouse, zoom with the wheel). Modes: side by side, overlay with opacity,
/// difference highlight, and a swipe curtain. Pixels are drawn crisp when zoomed in.
/// </summary>
public sealed class ImageCanvas : Control
{
    public static readonly StyledProperty<Bitmap?> LeftProperty = AvaloniaProperty.Register<ImageCanvas, Bitmap?>(nameof(Left));
    public static readonly StyledProperty<Bitmap?> RightProperty = AvaloniaProperty.Register<ImageCanvas, Bitmap?>(nameof(Right));
    public static readonly StyledProperty<Bitmap?> DiffProperty = AvaloniaProperty.Register<ImageCanvas, Bitmap?>(nameof(Diff));
    public static readonly StyledProperty<ImageViewMode> ModeProperty = AvaloniaProperty.Register<ImageCanvas, ImageViewMode>(nameof(Mode));
    public static readonly StyledProperty<double> OverlayOpacityProperty = AvaloniaProperty.Register<ImageCanvas, double>(nameof(OverlayOpacity), 0.5);
    public static readonly StyledProperty<double> SwipeProperty = AvaloniaProperty.Register<ImageCanvas, double>(nameof(Swipe), 0.5);
    public static readonly StyledProperty<double> ZoomProperty = AvaloniaProperty.Register<ImageCanvas, double>(nameof(Zoom), 1);
    public static readonly StyledProperty<IReadOnlyList<ImageRect>?> RegionsProperty = AvaloniaProperty.Register<ImageCanvas, IReadOnlyList<ImageRect>?>(nameof(Regions));
    public static readonly StyledProperty<int> CurrentRegionProperty = AvaloniaProperty.Register<ImageCanvas, int>(nameof(CurrentRegion), -1);
    public static readonly StyledProperty<bool> ShowRegionsProperty = AvaloniaProperty.Register<ImageCanvas, bool>(nameof(ShowRegions), true);

    private Point _pan;                     // top-left of the picture area, in control coordinates (for the left panel in side-by-side mode)
    private bool _fit = true;
    private Point? _dragStart;
    private Point _panAtDragStart;
    private bool _draggingSwipe;

    static ImageCanvas()
    {
        AffectsRender<ImageCanvas>(LeftProperty, RightProperty, DiffProperty, ModeProperty, OverlayOpacityProperty, SwipeProperty, ZoomProperty, RegionsProperty, CurrentRegionProperty, ShowRegionsProperty);
        FocusableProperty.OverrideDefaultValue<ImageCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<ImageCanvas>(true);
    }

    public Bitmap? Left { get => GetValue(LeftProperty); set => SetValue(LeftProperty, value); }
    public Bitmap? Right { get => GetValue(RightProperty); set => SetValue(RightProperty, value); }
    public Bitmap? Diff { get => GetValue(DiffProperty); set => SetValue(DiffProperty, value); }
    public ImageViewMode Mode { get => GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public double OverlayOpacity { get => GetValue(OverlayOpacityProperty); set => SetValue(OverlayOpacityProperty, value); }
    public double Swipe { get => GetValue(SwipeProperty); set => SetValue(SwipeProperty, value); }
    public double Zoom { get => GetValue(ZoomProperty); set { _fit = false; SetValue(ZoomProperty, Math.Clamp(value, 0.02, 64)); } }
    public IReadOnlyList<ImageRect>? Regions { get => GetValue(RegionsProperty); set => SetValue(RegionsProperty, value); }
    public int CurrentRegion { get => GetValue(CurrentRegionProperty); set => SetValue(CurrentRegionProperty, value); }
    public bool ShowRegions { get => GetValue(ShowRegionsProperty); set => SetValue(ShowRegionsProperty, value); }

    /// <summary>Raised with image coordinates while the mouse moves (−1,−1 when it leaves).</summary>
    public event Action<int, int>? PixelHovered;

    /// <summary>Raised when the user drags the swipe curtain.</summary>
    public event Action<double>? SwipeChanged;

    /// <summary>Raised when the user zooms; the owner may update a zoom label.</summary>
    public event Action<double>? ZoomChanged;

    private double ImageWidth => Math.Max(Left?.PixelSize.Width ?? 0, Right?.PixelSize.Width ?? 0);
    private double ImageHeight => Math.Max(Left?.PixelSize.Height ?? 0, Right?.PixelSize.Height ?? 0);
    private bool IsSideBySide => Mode == ImageViewMode.SideBySide;
    private double PanelWidth => IsSideBySide ? Bounds.Width / 2 : Bounds.Width;

    public void FitToWindow()
    {
        _fit = true;
        InvalidateVisual();
    }

    public void SetZoom100() => SetZoomCentered(1);

    public void ZoomBy(double factor) => SetZoomCentered(Math.Clamp(EffectiveZoom * factor, 0.02, 64));

    private void SetZoomCentered(double zoom)
    {
        var center = new Point(PanelWidth / 2, Bounds.Height / 2);
        ZoomAround(center, zoom);
    }

    /// <summary>Move the view so a pixel rectangle is centred (used by next/previous region).</summary>
    public void CenterOn(ImageRect r)
    {
        if (_fit) { _fit = false; }
        var z = EffectiveZoom;
        _pan = new Point(PanelWidth / 2 - (r.X + r.Width / 2.0) * z, Bounds.Height / 2 - (r.Y + r.Height / 2.0) * z);
        InvalidateVisual();
    }

    private double EffectiveZoom
    {
        get
        {
            if (!_fit || ImageWidth == 0) { return GetValue(ZoomProperty); }
            var fit = Math.Min((PanelWidth - 16) / ImageWidth, (Bounds.Height - 16) / ImageHeight);
            return Math.Clamp(fit <= 0 ? 1 : fit, 0.02, 64);
        }
    }

    private Point EffectivePan
    {
        get
        {
            if (!_fit) { return _pan; }
            var z = EffectiveZoom;
            return new Point((PanelWidth - ImageWidth * z) / 2, (Bounds.Height - ImageHeight * z) / 2);
        }
    }

    private void ZoomAround(Point anchor, double newZoom)
    {
        var oldZoom = EffectiveZoom;
        var pan = EffectivePan;
        // keep the image point under the anchor fixed
        var ix = (anchor.X - pan.X) / oldZoom;
        var iy = (anchor.Y - pan.Y) / oldZoom;
        _fit = false;
        SetValue(ZoomProperty, newZoom);
        _pan = new Point(anchor.X - ix * newZoom, anchor.Y - iy * newZoom);
        ZoomChanged?.Invoke(newZoom);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        ctx.FillRectangle(new SolidColorBrush(dark ? Color.FromRgb(0x14, 0x17, 0x1E) : Color.FromRgb(0xE4, 0xE7, 0xEE)), new Rect(Bounds.Size));
        if (Left is null && Right is null)
        {
            return;
        }

        var z = EffectiveZoom;
        var pan = EffectivePan;
        // scoped to this drawing pass: changing the control's own property here would invalidate it mid-render
        using var interpolation = ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = z >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality });

        switch (Mode)
        {
            case ImageViewMode.SideBySide:
                DrawPanel(ctx, Left, new Rect(0, 0, Bounds.Width / 2 - 1, Bounds.Height), pan, z, dark);
                DrawPanel(ctx, Right, new Rect(Bounds.Width / 2 + 1, 0, Bounds.Width / 2 - 1, Bounds.Height), pan, z, dark);
                break;
            case ImageViewMode.Overlay:
                DrawChecker(ctx, new Rect(Bounds.Size), pan, z, dark);
                DrawBitmap(ctx, Left, pan, z, 1);
                DrawBitmap(ctx, Right, pan, z, OverlayOpacity);
                break;
            case ImageViewMode.Difference:
                // a plain backdrop (not the checkerboard) keeps the faded picture readable behind the highlights
                ctx.FillRectangle(new SolidColorBrush(dark ? Color.FromRgb(0x1E, 0x22, 0x2C) : Colors.White), new Rect(pan.X, pan.Y, ImageWidth * z, ImageHeight * z));
                DrawBitmap(ctx, Left, pan, z, 0.5);
                DrawBitmap(ctx, Diff, pan, z, 1);
                break;
            case ImageViewMode.Swipe:
                DrawChecker(ctx, new Rect(Bounds.Size), pan, z, dark);
                DrawBitmap(ctx, Left, pan, z, 1);
                var x = Bounds.Width * Swipe;
                using (ctx.PushClip(new Rect(x, 0, Bounds.Width - x, Bounds.Height)))
                {
                    DrawBitmap(ctx, Right, pan, z, 1);
                }

                ctx.DrawLine(new Pen(Brushes.White, 3), new Point(x, 0), new Point(x, Bounds.Height));
                ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x3D, 0x5A, 0xE0)), 1.5), new Point(x, 0), new Point(x, Bounds.Height));
                ctx.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0x3D, 0x5A, 0xE0)), 2), new Point(x, Bounds.Height / 2), 11, 11);
                break;
        }

        if (ShowRegions && Regions is { Count: > 0 } regions)
        {
            DrawRegions(ctx, regions, pan, z);
        }
    }

    private void DrawPanel(DrawingContext ctx, Bitmap? bmp, Rect panel, Point pan, double z, bool dark)
    {
        using (ctx.PushClip(panel))
        {
            DrawChecker(ctx, panel, new Point(panel.X + pan.X, pan.Y), z, dark);
            DrawBitmap(ctx, bmp, new Point(panel.X + pan.X, pan.Y), z, 1);
            if (ShowRegions && Regions is { Count: > 0 } regions)
            {
                DrawRegions(ctx, regions, new Point(panel.X + pan.X, pan.Y), z);
            }
        }
    }

    private static void DrawBitmap(DrawingContext ctx, Bitmap? bmp, Point pan, double z, double opacity)
    {
        if (bmp is null) { return; }
        var dest = new Rect(pan.X, pan.Y, bmp.PixelSize.Width * z, bmp.PixelSize.Height * z);
        using (opacity < 1 ? ctx.PushOpacity(opacity) : default)
        {
            ctx.DrawImage(bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), dest);
        }
    }

    /// <summary>A light checkerboard under transparent areas, only where a picture is.</summary>
    private void DrawChecker(DrawingContext ctx, Rect clip, Point pan, double z, bool dark)
    {
        if (ImageWidth == 0) { return; }
        var area = new Rect(pan.X, pan.Y, ImageWidth * z, ImageHeight * z).Intersect(clip);
        if (area.Width <= 0 || area.Height <= 0) { return; }
        var a = new SolidColorBrush(dark ? Color.FromRgb(0x2A, 0x2F, 0x3B) : Color.FromRgb(0xFF, 0xFF, 0xFF));
        var b = new SolidColorBrush(dark ? Color.FromRgb(0x22, 0x26, 0x31) : Color.FromRgb(0xEE, 0xF0, 0xF4));
        ctx.FillRectangle(a, area);
        const double s = 12;
        for (var y = Math.Floor((area.Top - pan.Y) / s) * s + pan.Y; y < area.Bottom; y += s)
        {
            for (var x = Math.Floor((area.Left - pan.X) / s) * s + pan.X; x < area.Right; x += s)
            {
                if ((((int)Math.Round((x - pan.X) / s) + (int)Math.Round((y - pan.Y) / s)) & 1) == 1)
                {
                    ctx.FillRectangle(b, new Rect(x, y, s, s).Intersect(area));
                }
            }
        }
    }

    private void DrawRegions(DrawingContext ctx, IReadOnlyList<ImageRect> regions, Point pan, double z)
    {
        var normal = new Pen(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0x3D, 0x9A)), 1.2);
        var current = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20)), 2.5);
        for (var i = 0; i < regions.Count; i++)
        {
            var r = regions[i];
            var rect = new Rect(pan.X + r.X * z - 3, pan.Y + r.Y * z - 3, Math.Max(r.Width * z, 2) + 6, Math.Max(r.Height * z, 2) + 6);
            if (rect.Right < 0 || rect.Bottom < 0 || rect.X > Bounds.Width || rect.Y > Bounds.Height) { continue; }
            ctx.DrawRectangle(null, i == CurrentRegion ? current : normal, rect, 3, 3);
        }
    }

    // ───────────── interaction ─────────────

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        var panelAnchor = IsSideBySide && p.X > Bounds.Width / 2 ? new Point(p.X - Bounds.Width / 2, p.Y) : p;
        ZoomAround(panelAnchor, Math.Clamp(EffectiveZoom * Math.Pow(1.15, e.Delta.Y), 0.02, 64));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var p = e.GetPosition(this);
        if (Mode == ImageViewMode.Swipe && Math.Abs(p.X - Bounds.Width * Swipe) < 14)
        {
            _draggingSwipe = true;
        }
        else
        {
            _dragStart = p;
            _fit = false;
            _pan = EffectivePan;
            _panAtDragStart = _pan;
        }

        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_draggingSwipe)
        {
            SwipeChanged?.Invoke(Math.Clamp(p.X / Math.Max(Bounds.Width, 1), 0, 1));
        }
        else if (_dragStart is { } s)
        {
            _pan = new Point(_panAtDragStart.X + (p.X - s.X), _panAtDragStart.Y + (p.Y - s.Y));
            InvalidateVisual();
        }

        var panelX = IsSideBySide && p.X > Bounds.Width / 2 ? p.X - Bounds.Width / 2 : p.X;
        var z = EffectiveZoom;
        var pan = EffectivePan;
        var ix = (int)Math.Floor((panelX - pan.X) / z);
        var iy = (int)Math.Floor((p.Y - pan.Y) / z);
        PixelHovered?.Invoke(ix >= 0 && ix < ImageWidth && iy >= 0 && iy < ImageHeight ? ix : -1, ix >= 0 && ix < ImageWidth && iy >= 0 && iy < ImageHeight ? iy : -1);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragStart = null;
        _draggingSwipe = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        PixelHovered?.Invoke(-1, -1);
    }
}
