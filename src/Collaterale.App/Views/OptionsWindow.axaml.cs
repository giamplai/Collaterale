using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Collaterale.App.ViewModels;
using Collaterale.Core.Settings;

namespace Collaterale.App.Views;

public partial class OptionsWindow : Window
{
    public OptionsWindow() : this(new OptionsViewModel(new AppSettings()))
    {
    }

    public OptionsWindow(OptionsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && e.Source is not TextBox) { Close(false); }
        };
    }

    private OptionsViewModel Vm => (OptionsViewModel)DataContext!;

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnAddApp(object? sender, RoutedEventArgs e) => Vm.AddApp();

    private void OnRemoveApp(object? sender, RoutedEventArgs e) => Vm.RemoveApp();

    private void OnAddPreset(object? sender, RoutedEventArgs e) => Vm.AddPreset();

    private void OnRemovePreset(object? sender, RoutedEventArgs e) => Vm.RemovePreset();

    private void OnRestoreArchives(object? sender, RoutedEventArgs e) => Vm.RestoreArchiveDefaults();

    private static CommandRow? RowOf(object? sender) => (sender as Control)?.DataContext as CommandRow;

    private void OnRemoveShortcut(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) { Vm.RemoveShortcut(row); }
    }

    private void OnDefaultShortcut(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) { Vm.RestoreShortcut(row); }
    }

    /// <summary>Turns the key the user pressed into a shortcut for the command, asking before taking it from another command.</summary>
    private async void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (RowOf(sender) is not { } row) { return; }
        e.Handled = true;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Tab or Key.Escape) { return; }
        var mods = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) { mods.Add("Ctrl"); }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { mods.Add("Alt"); }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { mods.Add("Shift"); }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) { mods.Add("Meta"); }
        var key = e.Key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)e.Key - (int)Key.D0).ToString(),
            Key.Back => "Backspace",
            _ => e.Key.ToString(),
        };
        var typed = string.Join('+', mods.Append(key));
        var taken = Vm.Assign(row, typed, reassign: false);
        if (taken is null || taken == "?") { return; }
        if (await ConfirmWindow.AskAsync(this, "Shortcut already used", $"{typed} is already assigned to “{taken}”. Take it over?", [], "Reassign"))
        {
            Vm.Assign(row, typed, reassign: true);
        }
    }

    private async void OnShellIntegration(object? sender, RoutedEventArgs e)
    {
        var message = Collaterale.Core.Integration.ShellIntegration.InstallForCurrentUser(Environment.ProcessPath ?? "collaterale");
        await ConfirmWindow.AskAsync(this, "File-manager integration", message, [], "OK");
    }
}
