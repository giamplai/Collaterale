using Avalonia.Controls;
using Avalonia.Interactivity;
using Collaterale.App.ViewModels;
using Collaterale.Core.Settings;

namespace Collaterale.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => FocusLastTile();
    }

    /// <summary>With "sticky tile" the tile used last is the one that has the focus, so Enter repeats it.</summary>
    private void FocusLastTile()
    {
        var tile = Shell?.Settings.Prefs.Interface is { StickySessionSelection: true, LastHomeTile: { Length: > 0 } name } ? this.FindControl<Button>(name) : null;
        tile?.Focus();
    }

    private void Remember(string tile)
    {
        if (Shell is { } shell && shell.Settings.Prefs.Interface.StickySessionSelection) { shell.Settings.Prefs.Interface.LastHomeTile = tile; shell.Persist(); }
    }

    private MainWindowViewModel? Shell => TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    private void OnNewText(object? sender, RoutedEventArgs e) { Remember("TextTile"); Shell?.NewTextTabCommand.Execute(null); }

    private void OnNewFolder(object? sender, RoutedEventArgs e) { Remember("FolderTile"); Shell?.NewFolderTabCommand.Execute(null); }

    private void OnNewHex(object? sender, RoutedEventArgs e) { Remember("HexTile"); Shell?.NewHexTabCommand.Execute(null); }

    private void OnNewTable(object? sender, RoutedEventArgs e) { Remember("TableTile"); Shell?.NewTableTabCommand.Execute(null); }

    private void OnNewImage(object? sender, RoutedEventArgs e) { Remember("ImageTile"); Shell?.NewImageTabCommand.Execute(null); }

    private void OnNewMerge(object? sender, RoutedEventArgs e) { Remember("MergeTile"); Shell?.NewMergeTabCommand.Execute(null); }

    private void OnOpenSession(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SessionEntry entry)
        {
            Shell?.OpenNamed(entry.Name);
        }
    }

    private void OnOpenRecent(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is RecentSession r)
        {
            Shell?.OpenRecentCommand.Execute(r);
        }
    }
}
