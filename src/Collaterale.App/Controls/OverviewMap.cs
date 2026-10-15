using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Collaterale.Core.Diff;

namespace Collaterale.App.Controls;

/// <summary>A miniature of the whole comparison: colored marks where differences are, and the visible window. Click or drag to scroll.</summary>
public sealed class OverviewMap : Control
{
    public static readonly StyledProperty<IReadOnlyList<DiffRow>?> RowsProperty = AvaloniaProperty.Register<OverviewMap, IReadOnlyList<DiffRow>?>(nameof(Rows));
    public static readonly StyledProperty<double> ViewportStartProperty = AvaloniaProperty.Register<OverviewMap, double>(nameof(ViewportStart));
    public static readonly StyledProperty<double> ViewportSizeProperty = AvaloniaProperty.Register<OverviewMap, double>(nameof(ViewportSize), 1);

    static OverviewMap() => AffectsRender<OverviewMap>(RowsProperty, ViewportStartProperty, ViewportSizeProperty);

    public IReadOnlyList<DiffRow>? Rows { get => GetValue(RowsProperty); set => SetValue(RowsProperty, value); }

    /// <summary>Top of the visible window as a fraction (0..1) of all rows.</summary>
    public double ViewportStart { get => GetValue(ViewportStartProperty); set => SetValue(ViewportStartProperty, value); }

    public double ViewportSize { get => GetValue(ViewportSizeProperty); set => SetValue(ViewportSizeProperty, value); }

    /// <summary>Raised with the fraction (0..1) the user wants to centre on.</summary>
    public event Action<double>? ScrollRequested;

    public override void Render(DrawingContext ctx)
    {
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        ctx.FillRectangle(new SolidColorBrush(dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0x00, 0x00, 0x00)), new Rect(Bounds.Size));
        if (Rows is not { Count: > 0 } rows)
        {
            return;
        }

        var h = Bounds.Height;
        var total = rows.Count;
        // draw runs of equal kind as single rectangles; at least 2px high so single-line changes stay visible
        var i = 0;
        while (i < total)
        {
            var kind = rows[i].Kind;
            var moved = rows[i].IsMoved;
            var j = i;
            while (j < total && rows[j].Kind == kind && rows[j].IsMoved == moved) { j++; }
            if (kind is not (RowKind.Same or RowKind.Unimportant))
            {
                var y = i * h / total;
                var c = DiffPane.BackgroundFor(kind, true, dark, moved);
                var solid = Color.FromArgb(0xE0, c.R, c.G, c.B);
                ctx.FillRectangle(new SolidColorBrush(solid), new Rect(2, y, Bounds.Width - 4, Math.Max((j - i) * h / total, 2)));
            }

            i = j;
        }

        var vy = ViewportStart * h;
        var vh = Math.Max(ViewportSize * h, 6);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x25, 0x60, 0x70, 0xFF)), new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0x60, 0x70, 0xFF)), 1), new Rect(0.5, vy, Bounds.Width - 1, vh));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Request(e.GetPosition(this).Y);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            Request(e.GetPosition(this).Y);
        }
    }

    private void Request(double y) => ScrollRequested?.Invoke(Math.Clamp(y / Math.Max(Bounds.Height, 1), 0, 1));
}
