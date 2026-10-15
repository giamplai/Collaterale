using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Collaterale.App.ViewModels;

namespace Collaterale.App.Views;

public partial class FolderSettingsWindow : Window
{
    public FolderSettingsWindow()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(false); }
        };
    }

    public FolderSettingsWindow(FolderSettingsViewModel vm) : this() => DataContext = vm;

    /// <summary>Set when the user chose "Use as defaults for new sessions".</summary>
    public bool SaveAsDefaults { get; private set; }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnSaveDefaults(object? sender, RoutedEventArgs e)
    {
        SaveAsDefaults = true;
        Close(true);
    }

    private void OnRemoveFilter(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FolderSettingsViewModel vm && (sender as Control)?.Tag is OtherFilterItem item) { vm.RemoveFilterCommand.Execute(item); }
    }

    private void OnRemoveAlignment(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FolderSettingsViewModel vm && (sender as Control)?.Tag is AlignmentItem item) { vm.RemoveAlignmentCommand.Execute(item); }
    }
}
