using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Collaterale.App.Views;

/// <summary>A confirmation that can list exactly what is about to happen. Closes with true to proceed.</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow() : this("Confirm", "", [], "OK")
    {
    }

    public ConfirmWindow(string title, string message, IReadOnlyList<string> details, string okText)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Message.Text = message;
        Ok.Content = okText;
        Details.ItemsSource = details;
        DetailsBox.IsVisible = details.Count > 0;
        if (details.Count == 0)
        {
            Height = 220;
        }

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(false);
            }
        };
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    public static Task<bool> AskAsync(Window owner, string title, string message, IReadOnlyList<string> details, string okText = "Continue")
        => new ConfirmWindow(title, message, details, okText).ShowDialog<bool>(owner);
}
