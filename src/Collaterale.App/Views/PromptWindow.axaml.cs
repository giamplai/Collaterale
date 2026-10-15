using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Collaterale.App.Views;

/// <summary>Asks for a name (a session, a workspace) or lets the user pick one from a list. Closes with the text, or null when cancelled.</summary>
public partial class PromptWindow : Window
{
    private readonly bool _pick;

    public PromptWindow() : this("Name", "", "", null, "OK")
    {
    }

    public PromptWindow(string title, string message, string text, IReadOnlyList<string>? choices, string okText)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Message.Text = message;
        Ok.Content = okText;
        Input.Text = text;
        if (choices is { Count: > 0 })
        {
            _pick = true;
            Input.IsVisible = false;
            Choices.IsVisible = true;
            Choices.ItemsSource = choices;
            Choices.SelectedIndex = 0;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var value = _pick ? Choices.SelectedItem as string : Input.Text?.Trim();
        Close(string.IsNullOrEmpty(value) ? null : value);
    }

    public static Task<string?> AskAsync(Window owner, string title, string message, string text = "", IReadOnlyList<string>? choices = null, string okText = "OK")
        => new PromptWindow(title, message, text, choices, okText).ShowDialog<string?>(owner);
}
