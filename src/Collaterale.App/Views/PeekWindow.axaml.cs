using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Collaterale.App.Views;

/// <summary>A quick, read-only look at the selected file pair (the differences of text files, or the start of a single file).</summary>
public partial class PeekWindow : Window
{
    public PeekWindow()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); }
        };
    }

    public PeekWindow(string title, string text) : this()
    {
        Title = "Peek — " + title;
        Body.Text = text;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
