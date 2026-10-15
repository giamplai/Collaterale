using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Collaterale.App.Views;

/// <summary>Asks for a line number (1-based). Closes with the number, or null when cancelled.</summary>
public partial class GoToLineWindow : Window
{
    private readonly int _max;

    public GoToLineWindow() : this("left", 100)
    {
    }

    public GoToLineWindow(string side, int lineCount)
    {
        InitializeComponent();
        _max = lineCount;
        Heading.Text = $"Go to line in the {side} file";
        Hint.Text = $"1 – {lineCount:N0}";
        Opened += (_, _) => Input.Focus();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(null); } };
    }

    /// <summary>Parses user input; out-of-range numbers are clamped into the file, anything else is rejected.</summary>
    public static bool TryParse(string? text, int lineCount, out int line)
    {
        line = 0;
        if (!int.TryParse(text?.Trim().Replace(",", "").Replace(".", ""), out var n) || n < 1 || lineCount < 1)
        {
            return false;
        }

        line = Math.Min(n, lineCount);
        return true;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnGo(object? sender, RoutedEventArgs e)
    {
        if (TryParse(Input.Text, _max, out var line))
        {
            Close(line);
        }
        else
        {
            Hint.Text = $"Enter a number from 1 to {_max:N0}";
        }
    }
}
