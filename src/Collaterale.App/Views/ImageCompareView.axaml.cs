using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Collaterale.App.ViewModels;

namespace Collaterale.App.Views;

public partial class ImageCompareView : UserControl
{
    private ImageCompareViewModel? _vm;

    public ImageCompareView()
    {
        InitializeComponent();
        Canvas.PixelHovered += (x, y) => _vm?.Hover(x, y);
        Canvas.SwipeChanged += s => { if (_vm is not null) { _vm.Swipe = s; } };
        Canvas.ZoomChanged += z => ZoomText.Text = $"{z * 100:0}%";
        DataContextChanged += (_, _) => Attach(DataContext as ImageCompareViewModel);
    }

    private void Attach(ImageCompareViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.ImagesChanged -= OnImagesChanged;
            _vm.PropertyChanged -= OnVmChanged;
        }

        _vm = vm;
        if (vm is null) { return; }
        vm.ImagesChanged += OnImagesChanged;
        vm.PropertyChanged += OnVmChanged;
        Apply(vm);
    }

    private void OnImagesChanged(object? sender, EventArgs e)
    {
        if (_vm is null) { return; }
        Apply(_vm);
        Canvas.FitToWindow();
        ZoomText.Text = "Fit";
        CenterRegion();
    }

    private void Apply(ImageCompareViewModel vm)
    {
        Canvas.Left = vm.LeftBitmap;
        Canvas.Right = vm.RightBitmap;
        Canvas.Diff = vm.DiffBitmap;
        Canvas.Mode = vm.Mode;
        Canvas.OverlayOpacity = vm.OverlayOpacity;
        Canvas.Swipe = vm.Swipe;
        Canvas.Regions = vm.Comparison?.Regions;
        Canvas.CurrentRegion = vm.CurrentRegion;
        Canvas.ShowRegions = vm.ShowRegions;
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_vm is null) { return; }
        switch (e.PropertyName)
        {
            case nameof(ImageCompareViewModel.Mode): Canvas.Mode = _vm.Mode; break;
            case nameof(ImageCompareViewModel.OverlayOpacity): Canvas.OverlayOpacity = _vm.OverlayOpacity; break;
            case nameof(ImageCompareViewModel.Swipe): Canvas.Swipe = _vm.Swipe; break;
            case nameof(ImageCompareViewModel.ShowRegions): Canvas.ShowRegions = _vm.ShowRegions; break;
            case nameof(ImageCompareViewModel.CurrentRegion):
                Canvas.CurrentRegion = _vm.CurrentRegion;
                CenterRegion();
                break;
        }
    }

    private void CenterRegion()
    {
        if (_vm?.Comparison is { } c && _vm.CurrentRegion >= 0 && _vm.CurrentRegion < c.Regions.Count)
        {
            // only move the view when the user is zoomed in; a fitted picture already shows everything
            if (Canvas.Zoom > 1.0001 || !IsFitted)
            {
                Canvas.CenterOn(c.Regions[_vm.CurrentRegion]);
            }
        }
    }

    private bool IsFitted => ZoomText.Text == "Fit";

    private void OnFit(object? sender, RoutedEventArgs e) { Canvas.FitToWindow(); ZoomText.Text = "Fit"; }

    private void OnZoom100(object? sender, RoutedEventArgs e) { Canvas.SetZoom100(); }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Canvas.ZoomBy(1.5);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => Canvas.ZoomBy(1 / 1.5);

    private async Task<string?> PickAsync(string title)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) { return null; }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp", "*.ico"] }, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async void OnOpenLeft(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null && await PickAsync("Left picture") is { } p) { _vm.OpenSide(Side.Left, p); }
    }

    private async void OnOpenRight(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null && await PickAsync("Right picture") is { } p) { _vm.OpenSide(Side.Right, p); }
    }
}
