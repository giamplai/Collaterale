using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Collaterale.Core.Diff;
using Collaterale.Core.Table;

namespace Collaterale.App.Controls;

/// <summary>
/// Draws two tables side by side with shared column widths, one row per comparison row. Rows are drawn on demand
/// (only those on screen), so tables with hundreds of thousands of rows scroll smoothly.
/// </summary>
public sealed class TableCanvas : Control
{
    public const double RowHeight = 24;
    public const double HeaderHeight = 54;

    public static readonly StyledProperty<TableComparison?> ComparisonProperty = AvaloniaProperty.Register<TableCanvas, TableComparison?>(nameof(Comparison));
    public static readonly StyledProperty<IReadOnlyList<int>?> VisibleRowsProperty = AvaloniaProperty.Register<TableCanvas, IReadOnlyList<int>?>(nameof(VisibleRows));
    public static readonly StyledProperty<IReadOnlyList<int>?> ColumnWidthsProperty = AvaloniaProperty.Register<TableCanvas, IReadOnlyList<int>?>(nameof(ColumnWidths));
    public static readonly StyledProperty<long> TopRowProperty = AvaloniaProperty.Register<TableCanvas, long>(nameof(TopRow));
    public static readonly StyledProperty<int> CurrentRowProperty = AvaloniaProperty.Register<TableCanvas, int>(nameof(CurrentRow), -1);
    public static readonly StyledProperty<string?> LeftTitleProperty = AvaloniaProperty.Register<TableCanvas, string?>(nameof(LeftTitle));
    public static readonly StyledProperty<string?> RightTitleProperty = AvaloniaProperty.Register<TableCanvas, string?>(nameof(RightTitle));

    private static readonly Typeface Mono = new("Cascadia Mono,JetBrains Mono,DejaVu Sans Mono,Menlo,Consolas,monospace");
    private static readonly Typeface Ui = new("Inter");
    private static readonly Typeface UiBold = new("Inter", FontStyle.Normal, FontWeight.SemiBold);
    private const double FontSize = 13;
    private const double NumberWidth = 56;
    private const double Gutter = 24;

    private double _cw = 7.8;

    static TableCanvas()
    {
        AffectsRender<TableCanvas>(ComparisonProperty, VisibleRowsProperty, ColumnWidthsProperty, TopRowProperty, CurrentRowProperty, LeftTitleProperty, RightTitleProperty);
        AffectsMeasure<TableCanvas>(ComparisonProperty, ColumnWidthsProperty);
        FocusableProperty.OverrideDefaultValue<TableCanvas>(true);
    }

    public TableComparison? Comparison { get => GetValue(ComparisonProperty); set => SetValue(ComparisonProperty, value); }
    public IReadOnlyList<int>? VisibleRows { get => GetValue(VisibleRowsProperty); set => SetValue(VisibleRowsProperty, value); }
    public IReadOnlyList<int>? ColumnWidths { get => GetValue(ColumnWidthsProperty); set => SetValue(ColumnWidthsProperty, value); }
    public long TopRow { get => GetValue(TopRowProperty); set => SetValue(TopRowProperty, value); }
    public int CurrentRow { get => GetValue(CurrentRowProperty); set => SetValue(CurrentRowProperty, value); }
    public string? LeftTitle { get => GetValue(LeftTitleProperty); set => SetValue(LeftTitleProperty, value); }
    public string? RightTitle { get => GetValue(RightTitleProperty); set => SetValue(RightTitleProperty, value); }

    public int TotalRows => VisibleRows?.Count ?? 0;
    public int ViewRows => Math.Max(1, (int)((Bounds.Height - HeaderHeight) / RowHeight));

    public event Action<int>? RowClicked;
    public event Action<long>? ScrollRequested;

    private double SideWidth => NumberWidth + (ColumnWidths?.Sum() ?? 0) * _cw;

    protected override Size MeasureOverride(Size availableSize)
    {
        Measure();
        return new Size(SideWidth * 2 + Gutter + 8, 0);
    }

    private void Measure() => _cw = new FormattedText("0000000000", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, Brushes.Black).Width / 10;

    private double SideX(bool left) => left ? 4 : 4 + SideWidth + Gutter;

    private double ColumnX(bool left, int col)
    {
        var x = SideX(left) + NumberWidth;
        for (var i = 0; i < col; i++) { x += ColumnWidths![i] * _cw; }
        return x;
    }

    public override void Render(DrawingContext ctx)
    {
        Measure();
        if (Comparison is not { } c || ColumnWidths is not { } widths || VisibleRows is not { } rows)
        {
            return;
        }

        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var fg = new SolidColorBrush(dark ? Color.FromRgb(0xDD, 0xE2, 0xEE) : Color.FromRgb(0x1F, 0x24, 0x33));
        var muted = new SolidColorBrush(dark ? Color.FromRgb(0x8B, 0x93, 0xA3) : Color.FromRgb(0x7A, 0x82, 0x92));
        var line = new Pen(new SolidColorBrush(dark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1E, 0x00, 0x00, 0x00)), 1);
        var accent = new SolidColorBrush(dark ? Color.FromRgb(0x9D, 0xB4, 0xFF) : Color.FromRgb(0x3D, 0x5A, 0xE0));
        var filler = new SolidColorBrush(dark ? Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00));

        DrawHeader(ctx, c, widths, dark, fg, muted);

        var visible = ViewRows + 1;
        for (var i = 0; i < visible && TopRow + i < rows.Count; i++)
        {
            var rowIndex = rows[(int)(TopRow + i)];
            var row = c.Rows[rowIndex];
            var y = HeaderHeight + i * RowHeight;

            foreach (var left in new[] { true, false })
            {
                var x0 = SideX(left);
                var hasRow = (left ? row.Left : row.Right) >= 0;
                var rowBrush = row.Kind switch
                {
                    TableRowKind.LeftOnly when left => new SolidColorBrush(DiffPane.BackgroundFor(RowKind.LeftOnly, true, dark)),
                    TableRowKind.RightOnly when !left => new SolidColorBrush(DiffPane.BackgroundFor(RowKind.RightOnly, true, dark)),
                    TableRowKind.Changed => new SolidColorBrush(Color.FromArgb(0x18, 0x6C, 0x7B, 0xE8)),
                    _ => null,
                };

                if (!hasRow)
                {
                    ctx.FillRectangle(filler, new Rect(x0, y, SideWidth, RowHeight));
                    continue;
                }

                if (rowBrush is not null) { ctx.FillRectangle(rowBrush, new Rect(x0, y, SideWidth, RowHeight)); }

                var number = (left ? row.Left : row.Right) + 1;
                var nt = new FormattedText(number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize - 1, muted);
                ctx.DrawText(nt, new Point(x0 + NumberWidth - nt.Width - 8, y + (RowHeight - nt.Height) / 2));

                for (var col = 0; col < widths.Count; col++)
                {
                    var text = c.Cell(rowIndex, col, left);
                    var x = ColumnX(left, col);
                    var w = widths[col] * _cw;
                    if (text is null)
                    {
                        ctx.FillRectangle(filler, new Rect(x, y, w, RowHeight));          // this side has no such column
                        continue;
                    }

                    if (row.Kind == TableRowKind.Changed && col < row.CellDiffs.Length && row.CellDiffs[col])
                    {
                        ctx.FillRectangle(new SolidColorBrush(DiffPane.BackgroundFor(RowKind.Changed, left, dark)), new Rect(x, y, w, RowHeight));
                        ctx.FillRectangle(accent, new Rect(x, y, 2.5, RowHeight));
                    }

                    var ft = new FormattedText(Visible(text), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, fg) { MaxTextWidth = Math.Max(w - 10, 1), MaxTextHeight = RowHeight, Trimming = TextTrimming.CharacterEllipsis };
                    ctx.DrawText(ft, new Point(x + 6, y + (RowHeight - ft.Height) / 2));
                }
            }

            ctx.DrawLine(line, new Point(0, y + RowHeight), new Point(SideWidth * 2 + Gutter + 8, y + RowHeight));
            if (rowIndex == CurrentRow)
            {
                var pen = new Pen(accent, 1.5);
                foreach (var left in new[] { true, false })
                {
                    ctx.DrawRectangle(pen, new Rect(SideX(left) + 0.75, y + 0.75, SideWidth - 1.5, RowHeight - 1.5));
                }
            }
        }
    }

    /// <summary>One-line view of a cell value: line breaks and tabs would break the grid.</summary>
    private static string Visible(string s) => s.Replace("\r\n", "↵").Replace('\n', '↵').Replace('\r', '↵').Replace('\t', ' ');

    private void DrawHeader(DrawingContext ctx, TableComparison c, IReadOnlyList<int> widths, bool dark, IBrush fg, IBrush muted)
    {
        var band = new SolidColorBrush(dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0x00, 0x00, 0x00));
        ctx.FillRectangle(band, new Rect(0, 0, SideWidth * 2 + Gutter + 8, HeaderHeight));
        foreach (var left in new[] { true, false })
        {
            var title = new FormattedText(left ? LeftTitle ?? "" : RightTitle ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiBold, 13, fg) { MaxTextWidth = SideWidth - 8, MaxTextHeight = 20, Trimming = TextTrimming.CharacterEllipsis };
            ctx.DrawText(title, new Point(SideX(left) + 4, 4));
            for (var col = 0; col < widths.Count; col++)
            {
                var present = (left ? c.LeftColumn : c.RightColumn)[col] is not null;
                var name = new FormattedText(c.ColumnNames[col], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiBold, 12, present ? fg : muted) { MaxTextWidth = Math.Max(widths[col] * _cw - 8, 1), MaxTextHeight = 20, Trimming = TextTrimming.CharacterEllipsis };
                ctx.DrawText(name, new Point(ColumnX(left, col) + 6, 30));
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var p = e.GetPosition(this);
        if (p.Y < HeaderHeight || VisibleRows is not { } rows)
        {
            return;
        }

        var idx = TopRow + (int)((p.Y - HeaderHeight) / RowHeight);
        if (idx >= 0 && idx < rows.Count)
        {
            RowClicked?.Invoke(rows[(int)idx]);
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
        long? delta = e.Key switch { Key.Up => -1, Key.Down => 1, Key.PageUp => -ViewRows, Key.PageDown => ViewRows, _ => null };
        if (delta is { } d)
        {
            ScrollRequested?.Invoke(d);
            e.Handled = true;
        }
    }
}
