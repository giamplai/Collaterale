using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Collaterale.Core.Diff;

namespace Collaterale.App.Controls;

/// <summary>
/// One editable line of the details panel: tinted like the pane rows, with the words that differ from the facing line
/// highlighted and, optionally, spaces, tabs and the line end drawn as symbols.
/// </summary>
public sealed class DetailLine : TextEditor
{
    private RowKind _kind = RowKind.Same;
    private bool _left;
    private IReadOnlyList<Span> _words = [];

    public DetailLine()
    {
        FontFamily = new FontFamily("Cascadia Mono,JetBrains Mono,Fira Code,DejaVu Sans Mono,Menlo,Consolas,monospace");
        FontSize = 13;
        Padding = new Thickness(4, 2);
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.HighlightCurrentLine = false;
        Options.AllowScrollBelowDocument = false;
        TextArea.TextView.BackgroundRenderers.Add(new LineTint(this));
        TextArea.TextView.LineTransformers.Add(new WordMarks(this));
        TextArea.AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    protected override Type StyleKeyOverride => typeof(TextEditor);

    /// <summary>Raised when the user finishes editing the line (Enter).</summary>
    public event EventHandler? Committed;

    public bool ShowWhitespace
    {
        get => Options.ShowSpaces;
        set => Options.ShowSpaces = Options.ShowTabs = Options.ShowEndOfLine = value;
    }

    /// <summary>Replaces the line and its decoration; <paramref name="left"/> picks which side's color a one-sided row uses.</summary>
    public void Show(string text, bool hasLine, bool left, RowKind kind, IReadOnlyList<Span> words)
    {
        (_left, _kind, _words) = (left, hasLine ? kind : RowKind.Same, hasLine ? words : []);
        if (Text != text) { Text = text; }
        TextArea.TextView.Redraw();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            Committed?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private bool IsDark => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;

    private sealed class LineTint(DetailLine owner) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext context)
        {
            var color = DiffPane.BackgroundFor(owner._kind, owner._left, owner.IsDark);
            if (color.A == 0) { return; }
            context.FillRectangle(new SolidColorBrush(color), new Rect(textView.Bounds.Size));
        }
    }

    private sealed class WordMarks(DetailLine owner) : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            if (owner._words.Count == 0) { return; }
            var dark = owner.IsDark;
            var brush = new SolidColorBrush(dark ? Color.FromArgb(0xA0, 0xFF, 0x70, 0x70) : Color.FromArgb(0x90, 0xF0, 0x60, 0x60));
            foreach (var w in owner._words)
            {
                var a = line.Offset + Math.Min(w.Start, line.Length);
                var b = line.Offset + Math.Min(w.End, line.Length);
                if (b > a) { ChangeLinePart(a, b, e => e.TextRunProperties.SetBackgroundBrush(brush)); }
            }
        }
    }
}
