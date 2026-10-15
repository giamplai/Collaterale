using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using Collaterale.App.ViewModels;
using Collaterale.Core.Merge;

namespace Collaterale.App.Controls;

/// <summary>
/// The strip beside the merged text: at the first line of each changed section it shows a "!" for an unresolved conflict and
/// two buttons, take the left version (◀) or the right one (▶), so a section can be settled without leaving the result.
/// </summary>
public sealed class MergeActionMargin(DiffPane pane, Func<int, MergeRegionKind?> kindOf, Func<int, bool> isResolved, Action<int, MergeChoice> take) : AbstractMargin
{
    private const double Size = 18;

    private static readonly Typeface Face = new("Inter,Segoe UI,DejaVu Sans,sans-serif", FontStyle.Normal, FontWeight.Bold);

    protected override Size MeasureOverride(Size availableSize) => new(Size * 3 + 8, 0);

    /// <summary>The sections whose first result line is on screen, with the rectangle of each of the three slots.</summary>
    private IEnumerable<(int Region, double Top, double Height)> Visible()
    {
        var tv = TextView;
        if (tv is null || !tv.VisualLinesValid) { yield break; }
        var previous = int.MinValue;
        foreach (var vl in tv.VisualLines)
        {
            if (pane.RowAt(vl.FirstDocumentLine.LineNumber) is not { } row) { continue; }
            // the first line of a section: its predecessor (possibly off screen) belongs to another section
            var before = pane.RowAt(vl.FirstDocumentLine.LineNumber - 1);
            previous = before?.Group ?? int.MinValue;
            if (previous != row.Group && kindOf(row.Group) is { } kind && kind != MergeRegionKind.Stable)
            {
                yield return (row.Group, vl.VisualTop - tv.VerticalOffset, vl.Height);
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        var dark = pane.IsDark;
        foreach (var (region, top, height) in Visible())
        {
            var y = top + (height - Size) / 2;
            var kind = kindOf(region);
            if (kind == MergeRegionKind.Conflict && !isResolved(region))
            {
                Glyph(context, "!", new Point(2, y), Color.FromRgb(0xE5, 0x40, 0x40), filled: true);
            }

            Glyph(context, "◀", new Point(2 + Size, y), Solid(DiffPane.TintColor(MergeTint.Left, dark)), filled: false);
            Glyph(context, "▶", new Point(2 + Size * 2, y), Solid(DiffPane.TintColor(MergeTint.Right, dark)), filled: false);
        }
    }

    private static Color Solid(Color c) => Color.FromRgb(c.R, c.G, c.B);

    private static void Glyph(DrawingContext context, string text, Point at, Color color, bool filled)
    {
        var box = new Rect(at, new Size(Size - 2, Size - 2));
        if (filled) { context.DrawRectangle(new SolidColorBrush(color), null, box, 4, 4); }
        else { context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)), new Pen(new SolidColorBrush(color), 1), box, 4, 4); }
        var t = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11, filled ? Brushes.White : new SolidColorBrush(color));
        context.DrawText(t, new Point(box.X + (box.Width - t.Width) / 2, box.Y + (box.Height - t.Height) / 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        foreach (var (region, top, height) in Visible())
        {
            var y = top + (height - Size) / 2;
            if (p.Y < y || p.Y > y + Size) { continue; }
            if (p.X >= 2 + Size && p.X < 2 + Size * 2) { take(region, MergeChoice.Left); e.Handled = true; return; }
            if (p.X >= 2 + Size * 2 && p.X < 2 + Size * 3) { take(region, MergeChoice.Right); e.Handled = true; return; }
        }

        base.OnPointerPressed(e);
    }
}
