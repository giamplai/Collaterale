using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Collaterale.App.ViewModels;
using Collaterale.Core.Diff;

namespace Collaterale.App.Controls;

/// <summary>
/// The strip between the two panes: draws a band for every difference at the height of its rows and offers
/// copy-left / copy-right arrows. Hit testing is done by hand so thousands of differences stay cheap.
/// </summary>
public sealed class DiffGutter : Control
{
    public const double ButtonSize = 22;

    public static readonly StyledProperty<IReadOnlyList<DiffGroup>?> GroupsProperty = AvaloniaProperty.Register<DiffGutter, IReadOnlyList<DiffGroup>?>(nameof(Groups));
    public static readonly StyledProperty<IReadOnlyList<DiffRow>?> RowsProperty = AvaloniaProperty.Register<DiffGutter, IReadOnlyList<DiffRow>?>(nameof(Rows));
    public static readonly StyledProperty<int> CurrentGroupProperty = AvaloniaProperty.Register<DiffGutter, int>(nameof(CurrentGroup), -1);

    private TextView? _view;

    static DiffGutter() => AffectsRender<DiffGutter>(GroupsProperty, RowsProperty, CurrentGroupProperty);

    public IReadOnlyList<DiffGroup>? Groups { get => GetValue(GroupsProperty); set => SetValue(GroupsProperty, value); }

    public IReadOnlyList<DiffRow>? Rows { get => GetValue(RowsProperty); set => SetValue(RowsProperty, value); }

    public int CurrentGroup { get => GetValue(CurrentGroupProperty); set => SetValue(CurrentGroupProperty, value); }

    public event Action<int, Side>? CopyRequested;
    public event Action<int>? GroupSelected;

    /// <summary>The text view whose scroll position and line heights define where rows are drawn.</summary>
    public void Attach(TextView view)
    {
        if (_view is not null)
        {
            _view.ScrollOffsetChanged -= OnScroll;
            _view.VisualLinesChanged -= OnScroll;
        }

        _view = view;
        view.ScrollOffsetChanged += OnScroll;
        view.VisualLinesChanged += OnScroll;
    }

    private void OnScroll(object? sender, EventArgs e) => InvalidateVisual();

    private (double Top, double Bottom)? Extent(DiffGroup g)
    {
        if (_view is null || !_view.VisualLinesValid || _view.Document is null)
        {
            return null;
        }

        var first = g.FirstRow + 1;
        var last = g.FirstRow + g.RowCount;
        if (first > _view.Document.LineCount)
        {
            return null;
        }

        var top = _view.GetVisualTopByDocumentLine(first) - _view.VerticalOffset;
        var lastLine = Math.Min(last, _view.Document.LineCount);
        var bottom = _view.GetVisualTopByDocumentLine(lastLine) - _view.VerticalOffset + _view.DefaultLineHeight;
        return (top, bottom);
    }

    public override void Render(DrawingContext ctx)
    {
        if (Groups is not { Count: > 0 } groups || Rows is not { } rows || _view is null)
        {
            return;
        }

        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var accent = dark ? Color.FromRgb(0x9D, 0xB4, 0xFF) : Color.FromRgb(0x3D, 0x5A, 0xE0);
        var textBrush = new SolidColorBrush(dark ? Color.FromRgb(0xE6, 0xE9, 0xF0) : Color.FromRgb(0x1F, 0x24, 0x33));

        for (var gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi];
            if (Extent(g) is not { } e || e.Bottom < 0 || e.Top > Bounds.Height)
            {
                continue;
            }

            var kind = rows[g.FirstRow].Kind;
            var moved = Enumerable.Range(g.FirstRow, g.RowCount).Count(r => rows[r].IsMoved) * 2 >= g.RowCount;   // mostly moved lines: show it as a move
            var color = PaneColor(kind, dark, moved);
            var current = gi == CurrentGroup;
            var band = new Rect(0, e.Top, Bounds.Width, Math.Max(e.Bottom - e.Top, 4));
            ctx.FillRectangle(new SolidColorBrush(color), band);
            if (current)
            {
                ctx.DrawRectangle(new Pen(new SolidColorBrush(accent), 1.5), band.Deflate(0.75));
            }

            DrawButton(ctx, ButtonRect(band, Side.Left), "‹", textBrush, dark);
            DrawButton(ctx, ButtonRect(band, Side.Right), "›", textBrush, dark);
        }
    }

    private static Color PaneColor(RowKind kind, bool dark, bool moved)
    {
        var c = DiffPane.BackgroundFor(kind, true, dark, moved);
        return Color.FromArgb((byte)Math.Min(255, c.A + 0x20), c.R, c.G, c.B);
    }

    /// <summary>The "left" button copies the right side over to the left (arrow points left), and vice versa.</summary>
    private Rect ButtonRect(Rect band, Side arrowPointsTo)
    {
        var y = band.Top + Math.Max(0, Math.Min(2, (band.Height - ButtonSize) / 2));
        return arrowPointsTo == Side.Left ? new Rect(3, y, ButtonSize, ButtonSize) : new Rect(Bounds.Width - ButtonSize - 3, y, ButtonSize, ButtonSize);
    }

    private static void DrawButton(DrawingContext ctx, Rect r, string glyph, IBrush text, bool dark)
    {
        ctx.DrawRectangle(new SolidColorBrush(dark ? Color.FromArgb(0xE0, 0x2B, 0x31, 0x40) : Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)),
            new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x88, 0x99)), 1), r, 5, 5);
        var ft = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 16, text);
        ctx.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Y + (r.Height - ft.Height) / 2 - 1));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Groups is not { Count: > 0 } groups || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var p = e.GetPosition(this);
        for (var gi = 0; gi < groups.Count; gi++)
        {
            if (Extent(groups[gi]) is not { } ext)
            {
                continue;
            }

            var band = new Rect(0, ext.Top, Bounds.Width, Math.Max(ext.Bottom - ext.Top, 4));
            if (!band.Contains(p))
            {
                continue;
            }

            if (ButtonRect(band, Side.Left).Contains(p))
            {
                CopyRequested?.Invoke(gi, Side.Right);   // arrow points left: take the right side's lines
            }
            else if (ButtonRect(band, Side.Right).Contains(p))
            {
                CopyRequested?.Invoke(gi, Side.Left);    // arrow points right: take the left side's lines
            }
            else
            {
                GroupSelected?.Invoke(gi);
            }

            e.Handled = true;
            return;
        }
    }
}
