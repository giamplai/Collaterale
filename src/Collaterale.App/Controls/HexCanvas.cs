using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Collaterale.App.ViewModels;
using Collaterale.Core.Diff;
using Collaterale.Core.Hex;

namespace Collaterale.App.Controls;

/// <summary>
/// Draws two hex dumps side by side, one row of N bytes per line. Only the rows on screen are drawn, so a file's size does
/// not matter; the owner supplies a scroll bar and sets <see cref="TopRow"/>.
/// </summary>
public sealed class HexCanvas : Control
{
    public const double RowHeight = 20;
    public const double HeaderHeight = 46;

    public static readonly StyledProperty<HexComparison?> ComparisonProperty = AvaloniaProperty.Register<HexCanvas, HexComparison?>(nameof(Comparison));
    public static readonly StyledProperty<int> BytesPerRowProperty = AvaloniaProperty.Register<HexCanvas, int>(nameof(BytesPerRow), 16);
    public static readonly StyledProperty<bool> ShowTextProperty = AvaloniaProperty.Register<HexCanvas, bool>(nameof(ShowText), true);
    public static readonly StyledProperty<long> TopRowProperty = AvaloniaProperty.Register<HexCanvas, long>(nameof(TopRow));
    public static readonly StyledProperty<int> CurrentRangeProperty = AvaloniaProperty.Register<HexCanvas, int>(nameof(CurrentRange), -1);
    public static readonly StyledProperty<string?> LeftTitleProperty = AvaloniaProperty.Register<HexCanvas, string?>(nameof(LeftTitle));
    public static readonly StyledProperty<string?> RightTitleProperty = AvaloniaProperty.Register<HexCanvas, string?>(nameof(RightTitle));
    public static readonly StyledProperty<string?> LeftInfoProperty = AvaloniaProperty.Register<HexCanvas, string?>(nameof(LeftInfo));
    public static readonly StyledProperty<string?> RightInfoProperty = AvaloniaProperty.Register<HexCanvas, string?>(nameof(RightInfo));
    public static readonly StyledProperty<long> SelectedOffsetProperty = AvaloniaProperty.Register<HexCanvas, long>(nameof(SelectedOffset), -1);

    private static readonly Typeface Mono = new("Cascadia Mono,JetBrains Mono,DejaVu Sans Mono,Menlo,Consolas,monospace");
    private const double FontSize = 13;
    private const double Pad = 10;
    private const double Gutter = 28;

    private double _cw = 7.8;

    static HexCanvas()
    {
        AffectsRender<HexCanvas>(LeftTitleProperty, RightTitleProperty, LeftInfoProperty, RightInfoProperty, ComparisonProperty, BytesPerRowProperty, ShowTextProperty, TopRowProperty, CurrentRangeProperty, SelectedOffsetProperty);
        AffectsMeasure<HexCanvas>(ComparisonProperty, BytesPerRowProperty, ShowTextProperty);
        FocusableProperty.OverrideDefaultValue<HexCanvas>(true);
    }

    public HexComparison? Comparison { get => GetValue(ComparisonProperty); set => SetValue(ComparisonProperty, value); }
    public int BytesPerRow { get => GetValue(BytesPerRowProperty); set => SetValue(BytesPerRowProperty, value); }
    public bool ShowText { get => GetValue(ShowTextProperty); set => SetValue(ShowTextProperty, value); }
    public long TopRow { get => GetValue(TopRowProperty); set => SetValue(TopRowProperty, value); }
    public int CurrentRange { get => GetValue(CurrentRangeProperty); set => SetValue(CurrentRangeProperty, value); }
    public string? LeftTitle { get => GetValue(LeftTitleProperty); set => SetValue(LeftTitleProperty, value); }
    public string? RightTitle { get => GetValue(RightTitleProperty); set => SetValue(RightTitleProperty, value); }
    public string? LeftInfo { get => GetValue(LeftInfoProperty); set => SetValue(LeftInfoProperty, value); }
    public string? RightInfo { get => GetValue(RightInfoProperty); set => SetValue(RightInfoProperty, value); }
    public long SelectedOffset { get => GetValue(SelectedOffsetProperty); set => SetValue(SelectedOffsetProperty, value); }

    public long TotalRows => Comparison is null ? 0 : (Comparison.Length + BytesPerRow - 1) / BytesPerRow;
    public int VisibleRows => Math.Max(1, (int)((Bounds.Height - HeaderHeight) / RowHeight));

    /// <summary>Raised when the user clicks or navigates to a byte.</summary>
    public event Action<long>? OffsetRequested;

    /// <summary>Raised with a relative row delta (mouse wheel, arrows); the owner scrolls.</summary>
    public event Action<long>? ScrollRequested;

    private int OffsetDigits => Math.Max(8, (int)Math.Ceiling(Math.Log2(Math.Max(Comparison?.Length ?? 1, 2) + 1) / 4.0));

    private double SideChars => OffsetDigits + 2 + 3 * BytesPerRow + 1 + (ShowText ? BytesPerRow : 0);

    private double SideWidth => SideChars * _cw;

    protected override Size MeasureOverride(Size availableSize)
    {
        MeasureChar();
        return new Size(Pad * 2 + SideWidth * 2 + Gutter, 0);
    }

    private void MeasureChar()
    {
        var ft = new FormattedText("0000000000", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, Brushes.Black);
        _cw = ft.Width / 10;
    }

    private double SideX(Side side) => side == Side.Left ? Pad : Pad + SideWidth + Gutter;

    private double HexX(Side side) => SideX(side) + (OffsetDigits + 2) * _cw;

    private double TextX(Side side) => HexX(side) + (3 * BytesPerRow + 1) * _cw;

    public override void Render(DrawingContext ctx)
    {
        MeasureChar();
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var fg = new SolidColorBrush(dark ? Color.FromRgb(0xDD, 0xE2, 0xEE) : Color.FromRgb(0x1F, 0x24, 0x33));
        var muted = new SolidColorBrush(dark ? Color.FromRgb(0x8B, 0x93, 0xA3) : Color.FromRgb(0x7A, 0x82, 0x92));
        var filler = new SolidColorBrush(dark ? Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00));
        var accent = new SolidColorBrush(dark ? Color.FromRgb(0x9D, 0xB4, 0xFF) : Color.FromRgb(0x3D, 0x5A, 0xE0));

        if (Comparison is not { } c)
        {
            return;
        }

        var bpr = BytesPerRow;
        var rows = VisibleRows + 1;
        var digits = OffsetDigits;

        DrawHeader(ctx, dark, fg, muted);
        for (var i = 0; i < rows; i++)
        {
            var row = TopRow + i;
            if (row >= TotalRows)
            {
                break;
            }

            var y = HeaderHeight + i * RowHeight;
            var rowOffset = row * bpr;

            foreach (var side in new[] { Side.Left, Side.Right })
            {
                var data = side == Side.Left ? c.Left : c.Right;
                var other = side == Side.Left ? c.Right : c.Left;
                var hex = new StringBuilder(bpr * 3);
                var text = new StringBuilder(bpr);

                for (var k = 0; k < bpr; k++)
                {
                    var off = rowOffset + k;
                    var present = off < data.Length;
                    hex.Append(present ? data[off].ToString("X2", CultureInfo.InvariantCulture) : "  ").Append(' ');
                    text.Append(present ? (data[off] is >= 32 and < 127 ? (char)data[off] : '.') : ' ');

                    var otherPresent = off < other.Length;
                    var differs = off < c.Length && c.IsDifferent(off);
                    if (!present && off < c.Length)
                    {
                        FillBytes(ctx, side, y, k, filler);                         // this file has ended: grey placeholder
                    }
                    else if (present && differs)
                    {
                        var kind = otherPresent ? RowKind.Changed : (side == Side.Left ? RowKind.LeftOnly : RowKind.RightOnly);
                        FillBytes(ctx, side, y, k, new SolidColorBrush(DiffPane.BackgroundFor(kind, side == Side.Left, dark)));
                    }
                }

                var line = new FormattedText(rowOffset.ToString("X" + digits, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, muted);
                ctx.DrawText(line, new Point(SideX(side), y + 2));
                ctx.DrawText(new FormattedText(hex.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, fg), new Point(HexX(side), y + 2));
                if (ShowText)
                {
                    ctx.DrawText(new FormattedText(text.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, fg), new Point(TextX(side), y + 2));
                }
            }
        }

        DrawCurrentRange(ctx, c, accent);
        DrawSelection(ctx, accent);
    }

    private void DrawHeader(DrawingContext ctx, bool dark, IBrush fg, IBrush muted)
    {
        ctx.FillRectangle(new SolidColorBrush(dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0x00, 0x00, 0x00)), new Rect(0, 0, Math.Max(Bounds.Width, Pad * 2 + SideWidth * 2 + Gutter), HeaderHeight));
        foreach (var (side, title, info) in new[] { (Side.Left, LeftTitle, LeftInfo), (Side.Right, RightTitle, RightInfo) })
        {
            var t = new FormattedText(title ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold), 13, fg) { MaxTextWidth = SideWidth - 8, MaxTextHeight = 20, Trimming = TextTrimming.CharacterEllipsis };
            ctx.DrawText(t, new Point(SideX(side), 6));
            var i = new FormattedText(info ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 11, muted);
            ctx.DrawText(i, new Point(SideX(side), 25));
        }
    }

    private void FillBytes(DrawingContext ctx, Side side, double y, int col, IBrush brush)
    {
        ctx.FillRectangle(brush, new Rect(HexX(side) + col * 3 * _cw - 1, y, 3 * _cw, RowHeight));
        if (ShowText)
        {
            ctx.FillRectangle(brush, new Rect(TextX(side) + col * _cw, y, _cw, RowHeight));
        }
    }

    private void DrawCurrentRange(DrawingContext ctx, HexComparison c, IBrush accent)
    {
        if (CurrentRange < 0 || CurrentRange >= c.Ranges.Count)
        {
            return;
        }

        var r = c.Ranges[CurrentRange];
        var pen = new Pen(accent, 1.5);
        var bpr = BytesPerRow;
        var firstRow = r.Offset / bpr;
        var lastRow = (r.End - 1) / bpr;
        for (var row = Math.Max(firstRow, TopRow); row <= Math.Min(lastRow, TopRow + VisibleRows); row++)
        {
            var from = row == firstRow ? (int)(r.Offset % bpr) : 0;
            var to = row == lastRow ? (int)((r.End - 1) % bpr) : bpr - 1;
            var y = HeaderHeight + (row - TopRow) * RowHeight;
            foreach (var side in new[] { Side.Left, Side.Right })
            {
                ctx.DrawRectangle(pen, new Rect(HexX(side) + from * 3 * _cw - 1, y + 0.5, (to - from + 1) * 3 * _cw - _cw + 1, RowHeight - 1));
            }
        }
    }

    private void DrawSelection(DrawingContext ctx, IBrush accent)
    {
        if (SelectedOffset < 0)
        {
            return;
        }

        var row = SelectedOffset / BytesPerRow;
        if (row < TopRow || row > TopRow + VisibleRows)
        {
            return;
        }

        var col = (int)(SelectedOffset % BytesPerRow);
        var y = HeaderHeight + (row - TopRow) * RowHeight;
        var fill = new SolidColorBrush(Color.FromArgb(0x40, 0x60, 0x70, 0xFF));
        foreach (var side in new[] { Side.Left, Side.Right })
        {
            ctx.FillRectangle(fill, new Rect(HexX(side) + col * 3 * _cw - 1, y, 2 * _cw + 2, RowHeight));
            if (ShowText)
            {
                ctx.FillRectangle(fill, new Rect(TextX(side) + col * _cw, y, _cw, RowHeight));
            }
        }
    }

    /// <summary>Maps a point to a byte offset, or -1 when it is not over any bytes.</summary>
    public long OffsetAt(Point p)
    {
        if (p.Y < HeaderHeight)
        {
            return -1;
        }

        var row = TopRow + (long)((p.Y - HeaderHeight) / RowHeight);
        foreach (var side in new[] { Side.Left, Side.Right })
        {
            var hx = HexX(side);
            var col = (int)Math.Floor((p.X - hx) / (3 * _cw));
            if (p.X >= hx && col >= 0 && col < BytesPerRow)
            {
                return row * BytesPerRow + col;
            }

            if (ShowText)
            {
                var tx = TextX(side);
                col = (int)Math.Floor((p.X - tx) / _cw);
                if (p.X >= tx && col >= 0 && col < BytesPerRow)
                {
                    return row * BytesPerRow + col;
                }
            }
        }

        return -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var off = OffsetAt(e.GetPosition(this));
        if (off >= 0 && Comparison is { } c && off < c.Length)
        {
            OffsetRequested?.Invoke(off);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ScrollRequested?.Invoke((long)Math.Round(-e.Delta.Y * 3));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Comparison is null)
        {
            return;
        }

        var cur = SelectedOffset < 0 ? TopRow * BytesPerRow : SelectedOffset;
        long? target = e.Key switch
        {
            Key.Left => cur - 1,
            Key.Right => cur + 1,
            Key.Up => cur - BytesPerRow,
            Key.Down => cur + BytesPerRow,
            Key.PageUp => cur - (long)VisibleRows * BytesPerRow,
            Key.PageDown => cur + (long)VisibleRows * BytesPerRow,
            Key.Home => e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 0 : cur - cur % BytesPerRow,
            Key.End => e.KeyModifiers.HasFlag(KeyModifiers.Control) ? Comparison.Length - 1 : cur - cur % BytesPerRow + BytesPerRow - 1,
            _ => null,
        };

        if (target is { } t)
        {
            OffsetRequested?.Invoke(Math.Clamp(t, 0, Comparison.Length - 1));
            e.Handled = true;
        }
    }
}
