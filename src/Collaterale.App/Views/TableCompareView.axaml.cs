using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Collaterale.App.ViewModels;

namespace Collaterale.App.Views;

public partial class TableCompareView : UserControl
{
    private TableCompareViewModel? _vm;
    private bool _updating;

    public TableCompareView()
    {
        InitializeComponent();
        Canvas.RowClicked += row => _vm?.SelectRow(row);
        Canvas.ScrollRequested += rows => ScrollBy(rows);
        VScroll.ValueChanged += (_, e) => { if (!_updating) { Canvas.TopRow = (long)Math.Round(e.NewValue); } };
        Canvas.SizeChanged += (_, _) => UpdateScrollBar();
        DataContextChanged += (_, _) => Attach(DataContext as TableCompareViewModel);
    }

    private void Attach(TableCompareViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.DataChanged -= OnDataChanged;
            _vm.PropertyChanged -= OnVmChanged;
        }

        _vm = vm;
        if (vm is null) { return; }
        vm.DataChanged += OnDataChanged;
        vm.PropertyChanged += OnVmChanged;
        OnDataChanged(vm, EventArgs.Empty);
    }

    private void OnDataChanged(object? sender, EventArgs e)
    {
        if (_vm is null) { return; }
        Canvas.Comparison = _vm.Comparison;
        Canvas.ColumnWidths = _vm.ColumnWidths;
        Canvas.VisibleRows = _vm.VisibleRows;
        Canvas.LeftTitle = _vm.LeftTitle;
        Canvas.RightTitle = _vm.RightTitle;
        Canvas.CurrentRow = _vm.CurrentRow;
        UpdateScrollBar();
        Reveal(_vm.CurrentRow);
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_vm is null || e.PropertyName != nameof(TableCompareViewModel.CurrentRow)) { return; }
        Canvas.CurrentRow = _vm.CurrentRow;
        Reveal(_vm.CurrentRow);
    }

    private void UpdateScrollBar()
    {
        _updating = true;
        try
        {
            var max = Math.Max(0, Canvas.TotalRows - Canvas.ViewRows);
            VScroll.Maximum = max;
            VScroll.ViewportSize = Canvas.ViewRows;
            VScroll.LargeChange = Math.Max(1, Canvas.ViewRows - 1);
            VScroll.SmallChange = 1;
            Canvas.TopRow = Math.Min(Canvas.TopRow, max);
            VScroll.Value = Canvas.TopRow;
        }
        finally
        {
            _updating = false;
        }
    }

    private void ScrollBy(long rows)
    {
        var max = Math.Max(0, Canvas.TotalRows - Canvas.ViewRows);
        Canvas.TopRow = Math.Clamp(Canvas.TopRow + rows, 0, max);
        UpdateScrollBar();
    }

    /// <summary>Scrolls so the given comparison row is on screen.</summary>
    private void Reveal(int comparisonRow)
    {
        if (comparisonRow < 0 || Canvas.VisibleRows is not { } rows) { return; }
        var display = rows is IList<int> list ? list.IndexOf(comparisonRow) : rows.ToList().IndexOf(comparisonRow);
        if (display < 0) { return; }
        if (display < Canvas.TopRow || display >= Canvas.TopRow + Canvas.ViewRows - 1)
        {
            var max = Math.Max(0, Canvas.TotalRows - Canvas.ViewRows);
            Canvas.TopRow = Math.Clamp(display - Canvas.ViewRows / 3, 0, max);
            UpdateScrollBar();
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
        if (_vm is not null && await PickAsync("Left table") is { } p) { _vm.OpenSide(Side.Left, p); }
    }

    private async void OnOpenRight(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null && await PickAsync("Right table") is { } p) { _vm.OpenSide(Side.Right, p); }
    }
}
