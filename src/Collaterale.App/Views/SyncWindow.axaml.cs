using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Collaterale.App.ViewModels;
using Collaterale.Core.Folders;

namespace Collaterale.App.Views;

public sealed record SyncLine(string Verb, string Path, string Reason, bool IsDelete);

/// <summary>Pick a synchronization mode, preview every step, then confirm. Closes with the chosen plan, or null.</summary>
public partial class SyncWindow : Window
{
    private readonly FolderCompareViewModel _vm;
    private SyncPlan? _plan;

    public SyncWindow() : this(new FolderCompareViewModel())
    {
    }

    public SyncWindow(FolderCompareViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        ModeBox.ItemsSource = Enum.GetValues<SyncMode>();
        ModeBox.SelectedItem = SyncMode.UpdateRight;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
    }

    public SyncMode SelectedMode => ModeBox.SelectedItem is SyncMode m ? m : SyncMode.UpdateRight;

    private void OnModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        _plan = _vm.PlanSync(SelectedMode);
        Preview.ItemsSource = _plan.Steps.Select(s => new SyncLine(Verb(s.Action), s.MoveTo is null ? s.RelativePath : $"{s.RelativePath}  →  {s.MoveTo}", s.Reason, s.IsDelete)).ToList();
        Summary.Text = _plan.IsEmpty
            ? "Nothing to synchronize in this mode."
            : $"{_plan.Steps.Count(s => !s.IsDelete)} copy/create step(s), {_plan.Deletes} delete(s), {Format.Size(_plan.BytesToCopy)} to copy.";
        Skipped.Text = _plan.Skipped.Count == 0 ? "" : $"{_plan.Skipped.Count} item(s) left alone, for example: {_plan.Skipped[0].RelativePath} ({_plan.Skipped[0].Reason}).";
        Go.IsEnabled = !_plan.IsEmpty;
    }

    private static string Verb(SyncAction a) => a switch
    {
        SyncAction.CopyToRight => "copy  →",
        SyncAction.CopyToLeft => "←  copy",
        SyncAction.DeleteLeft => "delete (left)",
        SyncAction.DeleteRight => "delete (right)",
        SyncAction.CreateFolderRight => "new folder →",
        SyncAction.MoveRight => "rename  →",
        SyncAction.MoveLeft => "←  rename",
        _ => "← new folder",
    };

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnGo(object? sender, RoutedEventArgs e) => Close(_plan);
}
