using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;

namespace Collaterale.App.Controls;

/// <summary>
/// A small line icon (drawn here, in the text color) followed by a label, for toolbar buttons. Emoji glyphs look different —
/// and often far too big and colorful — on every platform; these stay 14 px and follow the theme.
/// </summary>
public sealed class IconLabel : StackPanel
{
    // 24 x 24 grid, drawn with a round 1.7 px line
    private static readonly Dictionary<string, string> Shapes = new()
    {
        ["save"] = "M5 3h11l3 3v15H5z M8 3v6h7V3 M8 21v-7h8v7",
        ["comment"] = "M4 5h16v11H10l-4 4v-4H4z",
        ["clock"] = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18 M12 7v5l3 2",
        ["search"] = "M10.5 4a6.5 6.5 0 1 0 0 13a6.5 6.5 0 1 0 0-13 M15.5 15.5L20 20",
        ["eye"] = "M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6",
    };

    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<IconLabel, string?>(nameof(Icon));
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<IconLabel, string?>(nameof(Text));

    private readonly Path _path = new() { Width = 14, Height = 14, Stretch = Stretch.Uniform, StrokeThickness = 1.7, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private readonly TextBlock _text = new() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

    public IconLabel()
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal;
        Spacing = 6;
        _path.Bind(Shape.StrokeProperty, this.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
        Children.Add(_path);
        Children.Add(_text);
    }

    public string? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconProperty)
        {
            _path.Data = Icon is { } k && Shapes.TryGetValue(k, out var d) ? Geometry.Parse(d) : null;
            _path.IsVisible = _path.Data is not null;
        }
        else if (change.Property == TextProperty)
        {
            _text.Text = Text;
            _text.IsVisible = !string.IsNullOrEmpty(Text);
        }
    }
}
