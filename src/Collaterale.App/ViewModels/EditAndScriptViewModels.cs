using System.Text;
using Collaterale.Core.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collaterale.App.ViewModels;

/// <summary>A single text file in an editor (the Text Edit view; also what <c>collaterale -</c> opens for standard input).</summary>
public sealed partial class TextEditViewModel : ObservableObject, ISessionContent
{
    [ObservableProperty] private string? _path;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _dirty;
    [ObservableProperty] private bool _wordWrap;
    [ObservableProperty] private bool _showWhitespace;
    [ObservableProperty] private bool _readOnly;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _status = "";

    /// <summary>The program's preferences (for backups).</summary>
    public Collaterale.Core.Settings.Preferences Prefs { get; set; } = new();

    private EolStyle _eol = EolStyle.Lf;
    private bool _endsWithNewline = true;
    private Encoding _encoding = new UTF8Encoding(false);
    private bool _bom;

    /// <summary>Shown in the tab for text that has no file (standard input).</summary>
    public string? TitleOverride { get; set; }

    public string Title => TitleOverride ?? (Path is null ? "Text edit" : System.IO.Path.GetFileName(Path)) + (Dirty ? " ●" : "");

    partial void OnPathChanged(string? value) => OnPropertyChanged(nameof(Title));

    partial void OnDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(Title));
        SaveCommand.NotifyCanExecuteChanged();
    }

    public string Info => Path is null ? "(not saved)" : $"{_encoding.WebName.ToUpperInvariant()} · {_eol.ToString().ToUpperInvariant()}";

    public void Open(string path)
    {
        try
        {
            var t = TextFile.Load(path);
            if (t.IsBinary) { Error = "This file looks binary: open it in the hex view."; return; }
            (_eol, _endsWithNewline, _encoding, _bom) = (t.Eol == EolStyle.None ? EolStyle.Lf : t.Eol, t.EndsWithNewline, t.Encoding, t.HasBom);
            Text = string.Join('\n', t.Lines);
            Path = path;
            Dirty = false;
            Error = null;
            OnPropertyChanged(nameof(Info));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
    }

    public void OpenText(string text, string? title)
    {
        var (lines, eol, ends) = TextFile.Split(text);
        (_eol, _endsWithNewline) = (eol == EolStyle.None ? EolStyle.Lf : eol, ends);
        Text = string.Join('\n', lines);
        TitleOverride = title;
        Dirty = false;
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Called by the view when the user types.</summary>
    public void Edited(string text)
    {
        Text = text;
        Dirty = true;
    }

    private bool CanSave => Dirty && !ReadOnly;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (Path is null) { Error = "Choose a file name with Save as."; return; }
        SaveTo(Path);
    }

    /// <summary>Writes the text to a file, keeping the line endings and encoding it was read with.</summary>
    public bool SaveTo(string path)
    {
        try
        {
            var (lines, _, _) = TextFile.Split(Text.Replace("\r\n", "\n"));
            Collaterale.Core.Settings.Backups.Create(path, Prefs.Backup);
            File.WriteAllBytes(path, TextFile.Encode(lines, _eol, _endsWithNewline, _encoding, _bom));
            Path = path;
            TitleOverride = null;
            Dirty = false;
            Error = null;
            Status = $"Saved {path}";
            OnPropertyChanged(nameof(Info));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
            return false;
        }
    }
}

/// <summary>The output of a script run from the app.</summary>
public sealed partial class ScriptViewModel : ObservableObject, ISessionContent
{
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private bool _running = true;
    [ObservableProperty] private int? _exitCode;

    public ScriptViewModel(string name) => Name = name;

    public string Name { get; }

    public string Title => "Script: " + System.IO.Path.GetFileName(Name);

    public string ExitText => ExitCode is { } c ? (c == 0 ? "Finished" : $"Finished with exit code {c}") : "Running…";

    partial void OnExitCodeChanged(int? value) => OnPropertyChanged(nameof(ExitText));
}
