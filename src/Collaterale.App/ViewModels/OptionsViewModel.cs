using System.Collections.ObjectModel;
using System.Text.Json;
using Collaterale.Core.Folders;
using Collaterale.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collaterale.App.ViewModels;

/// <summary>One command of the Commands page: its shortcut as typed, and where it appears.</summary>
public sealed partial class CommandRow(CommandInfo info, string shortcut) : ObservableObject
{
    public CommandInfo Info { get; } = info;
    public string Title => Info.Title;
    public string Description => Info.Description;
    public string ViewName => Info.View.ToString();
    public string? Default => Info.Default;

    [ObservableProperty] private string _shortcut = shortcut;
}

/// <summary>An editable name filter preset (the record in the settings is immutable).</summary>
public sealed class PresetRow
{
    public string Name { get; set; } = "";
    public string Include { get; set; } = "";
    public string ExcludeFiles { get; set; } = "";
    public string IncludeFolders { get; set; } = "";
    public string ExcludeFolders { get; set; } = "";

    public FilterPreset ToPreset() => new(Name, Include, ExcludeFiles, IncludeFolders, ExcludeFolders);
}

/// <summary>
/// The options window works on a copy of the preferences and writes it back only when OK is pressed, so Cancel really cancels.
/// </summary>
public sealed partial class OptionsViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly AppSettings _settings;

    public OptionsViewModel(AppSettings settings)
    {
        _settings = settings;
        Prefs = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(settings.Prefs, Json), Json)!;
        foreach (var a in Prefs.OpenWith) { OpenWith.Add(a); }
        foreach (var t in Prefs.Archives ?? [.. ArchiveExtractor.DefaultTypes.Select(d => new ArchiveType { Format = d.Format, Mask = d.Mask })]) { Archives.Add(t); }
        foreach (var p in settings.FilterPresets) { Presets.Add(new PresetRow { Name = p.Name, Include = p.Include, ExcludeFiles = p.ExcludeFiles, IncludeFolders = p.IncludeFolders, ExcludeFolders = p.ExcludeFolders }); }
        foreach (var c in CommandCatalog.All)
        {
            AllCommands.Add(new CommandRow(c, CommandCatalog.Effective(Prefs, c.Id) ?? ""));
        }

        RefreshCommands();
    }

    public Preferences Prefs { get; }

    public ObservableCollection<OpenWithApp> OpenWith { get; } = [];
    public ObservableCollection<ArchiveType> Archives { get; } = [];
    public ObservableCollection<PresetRow> Presets { get; } = [];
    public List<CommandRow> AllCommands { get; } = [];
    public ObservableCollection<CommandRow> Commands { get; } = [];

    [ObservableProperty] private string _commandSearch = "";
    [ObservableProperty] private CommandView? _commandView;
    [ObservableProperty] private OpenWithApp? _selectedApp;
    [ObservableProperty] private PresetRow? _selectedPreset;

    public static OpenIn[] OpenInChoices { get; } = Enum.GetValues<OpenIn>();
    public static QuickCompareKind[] QuickCompareChoices { get; } = Enum.GetValues<QuickCompareKind>();
    public static BackupMode[] BackupModes { get; } = Enum.GetValues<BackupMode>();
    public static Confirmation[] Confirmations { get; } = Enum.GetValues<Confirmation>();
    public static ComparisonPriority[] Priorities { get; } = Enum.GetValues<ComparisonPriority>();
    public static CommandView?[] ViewChoices { get; } = [null, .. Enum.GetValues<CommandView>().Cast<CommandView?>()];

    partial void OnCommandSearchChanged(string value) => RefreshCommands();
    partial void OnCommandViewChanged(CommandView? value) => RefreshCommands();

    private void RefreshCommands()
    {
        Commands.Clear();
        var wanted = CommandCatalog.Search(CommandSearch, CommandView).Select(c => c.Id).ToHashSet();
        foreach (var row in AllCommands.Where(r => wanted.Contains(r.Info.Id))) { Commands.Add(row); }
    }

    public void AddApp()
    {
        var app = new OpenWithApp { Name = "New application", Command = "", Arguments = "\"%1\"" };
        OpenWith.Add(app);
        SelectedApp = app;
    }

    public void RemoveApp()
    {
        if (SelectedApp is { } a) { OpenWith.Remove(a); SelectedApp = null; }
    }

    public void AddPreset()
    {
        var row = new PresetRow { Name = $"Filter {Presets.Count + 1}", Include = "*.*" };
        Presets.Add(row);
        SelectedPreset = row;
    }

    public void RemovePreset()
    {
        if (SelectedPreset is { } p) { Presets.Remove(p); SelectedPreset = null; }
    }

    public void RestoreArchiveDefaults()
    {
        Archives.Clear();
        foreach (var d in ArchiveExtractor.DefaultTypes) { Archives.Add(new ArchiveType { Format = d.Format, Mask = d.Mask }); }
    }

    /// <summary>
    /// Tries to give a command a shortcut. Returns the title of the command that already uses it (nothing is changed then, unless
    /// <paramref name="reassign"/> is set), or null when it worked; "?" when the shortcut is not valid.
    /// </summary>
    public string? Assign(CommandRow row, string typed, bool reassign)
    {
        var result = CommandCatalog.Assign(Prefs, row.Info.Id, typed, reassign);
        switch (result.Status)
        {
            case AssignResult.Invalid:
                return "?";
            case AssignResult.Taken:
                return AllCommands.First(r => r.Info.Id == result.Other).Title;
        }

        foreach (var r in AllCommands) { r.Shortcut = CommandCatalog.Effective(Prefs, r.Info.Id) ?? ""; }
        return null;
    }

    public void RemoveShortcut(CommandRow row)
    {
        // an empty shortcut is stored as "no shortcut", which is different from "the default"
        var b = Prefs.Commands.FirstOrDefault(c => c.Command == row.Info.Id);
        if (b is null) { Prefs.Commands.Add(b = new CommandBinding { Command = row.Info.Id }); }
        b.Shortcut = "";
        row.Shortcut = "";
    }

    public void RestoreShortcut(CommandRow row)
    {
        CommandCatalog.Restore(Prefs, row.Info.Id);
        row.Shortcut = CommandCatalog.Effective(Prefs, row.Info.Id) ?? "";
    }

    /// <summary>Writes the copy back to the settings.</summary>
    public void ApplyTo(AppSettings settings)
    {
        Prefs.OpenWith = [.. OpenWith.Where(Collaterale.Core.Integration.OpenWith.IsUsable)];
        var defaults = ArchiveExtractor.DefaultTypes;
        var custom = Archives.Select(a => new ArchiveType { Format = a.Format, Mask = a.Mask.Trim() }).ToList();
        Prefs.Archives = custom.Select(a => (a.Format, a.Mask)).SequenceEqual(defaults.Select(a => (a.Format, a.Mask))) ? null : custom;
        // the sessions that are open keep their reference to the same Preferences object, so it is updated in place
        var live = settings.Prefs;
        (live.Startup, live.Tabs, live.TextEditing, live.NextDifference, live.Backup, live.FileOperations) = (Prefs.Startup, Prefs.Tabs, Prefs.TextEditing, Prefs.NextDifference, Prefs.Backup, Prefs.FileOperations);
        (live.Archives, live.Commands, live.OpenWith, live.Log, live.Metrics, live.Scripts) = (Prefs.Archives, Prefs.Commands, Prefs.OpenWith, Prefs.Log, Prefs.Metrics, Prefs.Scripts);
        (live.Editor, live.Files, live.Interface, live.Updates, live.SharedSessionsFile) = (Prefs.Editor, Prefs.Files, Prefs.Interface, Prefs.Updates, Prefs.SharedSessionsFile);
        settings.FilterPresets = [.. Presets.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => p.ToPreset())];
    }
}
