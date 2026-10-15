using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Collaterale.App.ViewModels;

namespace Collaterale.App.Views;

public partial class HexCompareView : UserControl
{
    private HexCompareViewModel? _vm;
    private bool _updatingScroll;

    public HexCompareView()
    {
        InitializeComponent();
        Canvas.OffsetRequested += offset => _vm?.Select(offset);
        Canvas.ScrollRequested += rows => ScrollBy(rows);
        VScroll.ValueChanged += (_, e) =>
        {
            if (!_updatingScroll)
            {
                Canvas.TopRow = (long)Math.Round(e.NewValue);
            }
        };
        Canvas.SizeChanged += (_, _) => UpdateScrollBar();
        DataContextChanged += (_, _) => Attach(DataContext as HexCompareViewModel);
    }

    private void Attach(HexCompareViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.DataChanged -= OnDataChanged;
            _vm.PropertyChanged -= OnVmChanged;
        }

        _vm = vm;
        if (vm is null)
        {
            return;
        }

        vm.DataChanged += OnDataChanged;
        vm.PropertyChanged += OnVmChanged;
        OnDataChanged(vm, EventArgs.Empty);
    }

    private void OnDataChanged(object? sender, EventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        Canvas.Comparison = _vm.Comparison;
        Canvas.BytesPerRow = _vm.BytesPerRow;
        Canvas.ShowText = _vm.ShowText;
        Canvas.CurrentRange = _vm.CurrentRange;
        Canvas.SelectedOffset = _vm.SelectedOffset;
        Canvas.LeftTitle = _vm.LeftTitle;
        Canvas.RightTitle = _vm.RightTitle;
        Canvas.LeftInfo = _vm.LeftInfo;
        Canvas.RightInfo = _vm.RightInfo;
        UpdateScrollBar();
        if (_vm.CurrentRange >= 0)
        {
            RevealRange(_vm.CurrentRange);
        }
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(HexCompareViewModel.CurrentRange):
                Canvas.CurrentRange = _vm.CurrentRange;
                if (_vm.CurrentRange >= 0) { RevealRange(_vm.CurrentRange); }
                break;
            case nameof(HexCompareViewModel.SelectedOffset):
                Canvas.SelectedOffset = _vm.SelectedOffset;
                Reveal(_vm.SelectedOffset);
                break;
            case nameof(HexCompareViewModel.LeftTitle):
            case nameof(HexCompareViewModel.RightTitle):
                Canvas.LeftTitle = _vm.LeftTitle;
                Canvas.RightTitle = _vm.RightTitle;
                break;
        }
    }

    private void UpdateScrollBar()
    {
        _updatingScroll = true;
        try
        {
            var max = Math.Max(0, Canvas.TotalRows - Canvas.VisibleRows);
            VScroll.Maximum = max;
            VScroll.ViewportSize = Canvas.VisibleRows;
            VScroll.LargeChange = Math.Max(1, Canvas.VisibleRows - 1);
            VScroll.SmallChange = 1;
            Canvas.TopRow = Math.Min(Canvas.TopRow, max);
            VScroll.Value = Canvas.TopRow;
        }
        finally
        {
            _updatingScroll = false;
        }
    }

    private void ScrollBy(long rows)
    {
        var max = Math.Max(0, Canvas.TotalRows - Canvas.VisibleRows);
        Canvas.TopRow = Math.Clamp(Canvas.TopRow + rows, 0, max);
        UpdateScrollBar();
    }

    private void Reveal(long offset)
    {
        if (offset < 0)
        {
            return;
        }

        var row = offset / Canvas.BytesPerRow;
        if (row < Canvas.TopRow || row >= Canvas.TopRow + Canvas.VisibleRows - 1)
        {
            var max = Math.Max(0, Canvas.TotalRows - Canvas.VisibleRows);
            Canvas.TopRow = Math.Clamp(row - Canvas.VisibleRows / 3, 0, max);
            UpdateScrollBar();
        }
    }

    private void RevealRange(int index)
    {
        if (_vm?.Comparison is { } c && index >= 0 && index < c.Ranges.Count)
        {
            Reveal(c.Ranges[index].Offset);
        }
    }

    private void OnGoKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm is not null && HexCompareViewModel.TryParseOffset(GoBox.Text ?? "", out var offset))
        {
            _vm.Select(offset);
            Canvas.Focus();
            e.Handled = true;
        }
    }

    private async Task<string?> PickAsync(string title)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) { return null; }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async void OnOpenLeft(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null && await PickAsync("Left file") is { } p) { _vm.OpenSide(Side.Left, p); }
    }

    private async void OnOpenRight(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null && await PickAsync("Right file") is { } p) { _vm.OpenSide(Side.Right, p); }
    }
}
