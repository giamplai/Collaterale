using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Document;
using AvaloniaEdit.Search;
using Collaterale.App.ViewModels;

namespace Collaterale.App.Views;

public partial class TextEditView : UserControl
{
    private TextEditViewModel? _vm;
    private bool _internal;

    public TextEditView()
    {
        InitializeComponent();
        SearchPanel.Install(Editor);
        DataContextChanged += (_, _) => Attach(DataContext as TextEditViewModel);
    }

    private void Attach(TextEditViewModel? vm)
    {
        if (_vm is not null) { _vm.PropertyChanged -= OnChanged; }
        _vm = vm;
        if (vm is null) { return; }
        vm.PropertyChanged += OnChanged;
        Load();
        Editor.TextChanged += (_, _) =>
        {
            if (!_internal) { _vm?.Edited(Editor.Document.Text); }
        };
        Editor.WordWrap = vm.WordWrap;
        Editor.IsReadOnly = vm.ReadOnly;
        Editor.Options.ShowSpaces = Editor.Options.ShowTabs = Editor.Options.ShowEndOfLine = vm.ShowWhitespace;
    }

    private void Load()
    {
        _internal = true;
        Editor.Document = new TextDocument(_vm?.Text ?? "");
        _internal = false;
    }

    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_vm is null) { return; }
        switch (e.PropertyName)
        {
            case nameof(TextEditViewModel.WordWrap): Editor.WordWrap = _vm.WordWrap; break;
            case nameof(TextEditViewModel.ShowWhitespace): Editor.Options.ShowSpaces = Editor.Options.ShowTabs = Editor.Options.ShowEndOfLine = _vm.ShowWhitespace; break;
            case nameof(TextEditViewModel.ReadOnly): Editor.IsReadOnly = _vm.ReadOnly; break;
            case nameof(TextEditViewModel.Text) when Editor.Document.Text != _vm.Text: Load(); break;
        }
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not { } top) { return; }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open file", AllowMultiple = false });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) { _vm.Open(path); }
    }

    private async void OnSaveAs(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not { } top) { return; }
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save as" });
        if (file?.TryGetLocalPath() is { } path) { _vm.SaveTo(path); }
    }
}
