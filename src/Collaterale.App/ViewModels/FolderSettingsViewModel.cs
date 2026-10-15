using System.Collections.ObjectModel;
using System.Globalization;
using Collaterale.Core.Diff;
using Collaterale.Core.Folders;
using Collaterale.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collaterale.App.ViewModels;

/// <summary>A rule of the "other filters" list with a readable description.</summary>
public sealed record OtherFilterItem(OtherFilter Filter)
{
    public string Text => Filter.Kind switch
    {
        OtherFilterKind.SmallerThan => $"Hide files smaller than {Format.Size(Filter.Number)}",
        OtherFilterKind.LargerThan => $"Hide files larger than {Format.Size(Filter.Number)}",
        OtherFilterKind.ModifiedBefore => $"Hide files modified before {Filter.Date:yyyy-MM-dd}",
        OtherFilterKind.ModifiedAfter => $"Hide files modified after {Filter.Date:yyyy-MM-dd}",
        OtherFilterKind.OlderThanDays => $"Hide files not modified in the last {Filter.Number} days",
        OtherFilterKind.NewerThanDays => $"Hide files modified in the last {Filter.Number} days",
        OtherFilterKind.HasAttribute => $"Hide files that are {Filter.Attributes.ToString().ToLowerInvariant().Replace(", ", " or ")}",
        _ => Filter.ToString(),
    };
}

public sealed record AlignmentItem(AlignmentOverride Alignment)
{
    public string Text => $"{Alignment.LeftPath}   ↔   {Alignment.RightPath}";
}

/// <summary>The editable copy of everything the session settings dialog shows; nothing changes until OK.</summary>
public sealed partial class FolderSettingsViewModel : ObservableObject
{
    public static CompareCriteria[] CriteriaChoices { get; } = Enum.GetValues<CompareCriteria>();
    public static ContentMode[] ContentModes { get; } = Enum.GetValues<ContentMode>();
    public static WhitespaceMode[] WhitespaceModes { get; } = Enum.GetValues<WhitespaceMode>();
    public static ExpandMode[] ExpandModes { get; } = Enum.GetValues<ExpandMode>();
    public static ArchiveHandling[] ArchiveModes { get; } = Enum.GetValues<ArchiveHandling>();
    public static OtherFilterKind[] FilterKinds { get; } = Enum.GetValues<OtherFilterKind>();

    // ── spec ──
    [ObservableProperty] private string _leftPath = "";
    [ObservableProperty] private string _rightPath = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _disableEditing;

    // ── comparison: quick tests ──
    [ObservableProperty] private CompareCriteria _criteria;
    [ObservableProperty] private double _toleranceSeconds = 2;
    [ObservableProperty] private bool _ignoreDaylightSaving;
    [ObservableProperty] private bool _ignoreTimeZone;
    [ObservableProperty] private bool _compareNameCase;
    [ObservableProperty] private bool _attrReadOnly;
    [ObservableProperty] private bool _attrHidden;
    [ObservableProperty] private bool _attrSystem;
    [ObservableProperty] private bool _attrArchive;
    [ObservableProperty] private bool _comparePermissions;
    [ObservableProperty] private bool _alignExtensions;
    [ObservableProperty] private bool _alignUnicode;
    [ObservableProperty] private bool _detectRenames = true;

    // ── comparison: contents ──
    [ObservableProperty] private ContentMode _contentMode;
    [ObservableProperty] private WhitespaceMode _ruleWhitespace;
    [ObservableProperty] private bool _ruleIgnoreCase;
    [ObservableProperty] private bool _ruleIgnoreComments;
    [ObservableProperty] private bool _ruleIgnoreTimestamps;
    [ObservableProperty] private bool _ruleIgnoreBlankLines;
    [ObservableProperty] private bool _skipContentIfQuickSame = true;
    [ObservableProperty] private bool _overrideQuickResults = true;
    [ObservableProperty] private bool _compareVersions;

    // ── handling ──
    [ObservableProperty] private bool _recursive = true;
    [ObservableProperty] private bool _hideEmptyFolders = true;
    [ObservableProperty] private ExpandMode _expandOnLoad;
    [ObservableProperty] private ArchiveHandling _archives;
    [ObservableProperty] private bool _followSymbolicLinks;
    [ObservableProperty] private int _autoRefreshSeconds;

    // ── name filters ──
    [ObservableProperty] private string _include = "";
    [ObservableProperty] private string _excludeFiles = "";
    [ObservableProperty] private string _includeFolders = "";
    [ObservableProperty] private string _excludeFolders = "";
    [ObservableProperty] private string _presetName = "";
    [ObservableProperty] private FilterPreset? _selectedPreset;
    public ObservableCollection<FilterPreset> Presets { get; } = [];

    // ── other filters ──
    [ObservableProperty] private bool _excludeProtectedFiles = true;
    [ObservableProperty] private OtherFilterKind _newFilterKind = OtherFilterKind.LargerThan;
    [ObservableProperty] private string _newFilterNumber = "1";
    [ObservableProperty] private DateTimeOffset? _newFilterDate = DateTimeOffset.Now.AddYears(-1);
    [ObservableProperty] private string _filterError = "";
    public ObservableCollection<OtherFilterItem> OtherFilters { get; } = [];

    // ── misc ──
    public ObservableCollection<AlignmentItem> Alignments { get; } = [];

    // ── appearance (global) ──
    [ObservableProperty] private string _colorOlder = "";
    [ObservableProperty] private string _colorSame = "";
    [ObservableProperty] private string _colorOrphan = "";
    [ObservableProperty] private string _colorNewer = "";
    [ObservableProperty] private string _colorFiltered = "";

    public static FolderSettingsViewModel From(FolderCompareViewModel vm)
    {
        var o = vm.Options;
        var s = new FolderSettingsViewModel
        {
            LeftPath = vm.LeftPath, RightPath = vm.RightPath, Description = vm.Description, DisableEditing = vm.DisableEditing,
            Criteria = o.Criteria, ToleranceSeconds = o.TimeTolerance.TotalSeconds, IgnoreDaylightSaving = o.IgnoreDaylightSaving, IgnoreTimeZone = o.IgnoreTimeZone,
            CompareNameCase = o.CompareNameCase, AttrReadOnly = o.CompareAttributes.HasFlag(FileAttributes.ReadOnly), AttrHidden = o.CompareAttributes.HasFlag(FileAttributes.Hidden),
            AttrSystem = o.CompareAttributes.HasFlag(FileAttributes.System), AttrArchive = o.CompareAttributes.HasFlag(FileAttributes.Archive),
            ComparePermissions = o.ComparePermissions, AlignExtensions = o.AlignDifferentExtensions, AlignUnicode = o.AlignUnicodeNormalization, DetectRenames = o.DetectRenames,
            ContentMode = o.ContentMode, RuleWhitespace = o.Rules.Whitespace, RuleIgnoreCase = o.Rules.IgnoreCase, RuleIgnoreComments = o.Rules.IgnoreComments,
            RuleIgnoreTimestamps = o.Rules.IgnoreTimestamps, RuleIgnoreBlankLines = o.Rules.IgnoreBlankLines,
            SkipContentIfQuickSame = o.SkipContentIfQuickSame, OverrideQuickResults = o.OverrideQuickResults, CompareVersions = o.CompareVersions,
            Recursive = o.Recursive, HideEmptyFolders = o.HideEmptyFolders, ExpandOnLoad = vm.ExpandOnLoad, Archives = o.Archives, FollowSymbolicLinks = o.FollowSymbolicLinks,
            AutoRefreshSeconds = vm.AutoRefreshSeconds,
            Include = o.Include, ExcludeFiles = o.ExcludeFiles, IncludeFolders = o.IncludeFolders, ExcludeFolders = o.ExcludeFolders,
            ExcludeProtectedFiles = o.ExcludeProtectedFiles,
            ColorOlder = vm.Colors.Older ?? "", ColorSame = vm.Colors.Same ?? "", ColorOrphan = vm.Colors.Orphan ?? "", ColorNewer = vm.Colors.Newer ?? "", ColorFiltered = vm.Colors.Filtered ?? "",
        };
        foreach (var p in vm.Presets) { s.Presets.Add(p); }
        foreach (var f in o.OtherFilters) { s.OtherFilters.Add(new OtherFilterItem(f)); }
        foreach (var a in o.Alignments) { s.Alignments.Add(new AlignmentItem(a)); }
        return s;
    }

    public FolderOptions ToOptions() => new()
    {
        Criteria = Criteria,
        TimeTolerance = TimeSpan.FromSeconds(Math.Max(0, ToleranceSeconds)),
        IgnoreDaylightSaving = IgnoreDaylightSaving,
        IgnoreTimeZone = IgnoreTimeZone,
        CompareNameCase = CompareNameCase,
        CompareAttributes = (AttrReadOnly ? FileAttributes.ReadOnly : 0) | (AttrHidden ? FileAttributes.Hidden : 0) | (AttrSystem ? FileAttributes.System : 0) | (AttrArchive ? FileAttributes.Archive : 0),
        ComparePermissions = ComparePermissions,
        AlignDifferentExtensions = AlignExtensions,
        AlignUnicodeNormalization = AlignUnicode,
        DetectRenames = DetectRenames,
        ContentMode = ContentMode,
        Rules = new ContentRules { Whitespace = RuleWhitespace, IgnoreCase = RuleIgnoreCase, IgnoreComments = RuleIgnoreComments, IgnoreTimestamps = RuleIgnoreTimestamps, IgnoreBlankLines = RuleIgnoreBlankLines },
        SkipContentIfQuickSame = SkipContentIfQuickSame,
        OverrideQuickResults = OverrideQuickResults,
        CompareVersions = CompareVersions,
        Recursive = Recursive,
        HideEmptyFolders = HideEmptyFolders,
        Archives = Archives,
        FollowSymbolicLinks = FollowSymbolicLinks,
        Include = Include,
        ExcludeFiles = ExcludeFiles,
        IncludeFolders = IncludeFolders,
        ExcludeFolders = ExcludeFolders,
        ExcludeProtectedFiles = ExcludeProtectedFiles,
        OtherFilters = [.. OtherFilters.Select(f => f.Filter)],
        Alignments = [.. Alignments.Select(a => a.Alignment)],
    };

    public FolderColors ToColors() => new()
    {
        Older = Clean(ColorOlder), Same = Clean(ColorSame), Orphan = Clean(ColorOrphan), Newer = Clean(ColorNewer), Filtered = Clean(ColorFiltered),
    };

    private static string? Clean(string s) => Avalonia.Media.Color.TryParse(s.Trim(), out _) ? s.Trim() : null;

    /// <summary>Writes the dialog's values into the session (and the global colors), then compares again if something affecting the result changed.</summary>
    public async Task ApplyAsync(FolderCompareViewModel vm, AppSettings settings)
    {
        var o = ToOptions();
        const bool changed = true;               // cheap enough to always look again; OK means the user wants the new rules applied
        vm.Description = Description;
        vm.DisableEditing = DisableEditing;
        vm.ExpandOnLoad = ExpandOnLoad;
        vm.AutoRefreshSeconds = AutoRefreshSeconds;
        vm.Advanced = o;
        vm.Criteria = o.Criteria;
        vm.Include = o.Include;
        vm.ExcludeFiles = o.ExcludeFiles;
        vm.ExcludeFolders = o.ExcludeFolders;
        vm.IncludeFolders = o.IncludeFolders;
        settings.Colors = ToColors();
        vm.SetColors(settings.Colors);
        settings.FilterPresets.Clear();
        settings.FilterPresets.AddRange(Presets);
        if (changed && Directory.Exists(vm.LeftPath) && Directory.Exists(vm.RightPath)) { await vm.CompareAsync(); } else { vm.Rebuild(); }
    }

    // ── name filters ──

    partial void OnSelectedPresetChanged(FilterPreset? value)
    {
        if (value is null) { return; }
        Include = value.Include;
        ExcludeFiles = value.ExcludeFiles;
        IncludeFolders = value.IncludeFolders;
        ExcludeFolders = value.ExcludeFolders;
        PresetName = value.Name;
    }

    [RelayCommand]
    private void AddPreset()
    {
        var name = PresetName.Trim();
        if (name.Length == 0) { name = string.IsNullOrWhiteSpace(Include) ? "Filter " + (Presets.Count + 1) : Include; }
        var existing = Presets.FirstOrDefault(p => p.Name == name);
        if (existing is not null) { Presets.Remove(existing); }
        var preset = new FilterPreset(name, Include, ExcludeFiles, IncludeFolders, ExcludeFolders);
        Presets.Add(preset);
        PresetName = name;
    }

    [RelayCommand]
    private void RemovePreset()
    {
        if (SelectedPreset is { } p) { Presets.Remove(p); }
    }

    [RelayCommand]
    private void ClearNameFilters()
    {
        Include = ExcludeFiles = IncludeFolders = ExcludeFolders = "";
    }

    // ── other filters ──

    [RelayCommand]
    private void AddFilter()
    {
        FilterError = "";
        OtherFilter f;
        switch (NewFilterKind)
        {
            case OtherFilterKind.ModifiedBefore or OtherFilterKind.ModifiedAfter:
                if (NewFilterDate is not { } d) { FilterError = "Pick a date."; return; }
                f = new OtherFilter(NewFilterKind, Date: d.UtcDateTime);
                break;
            case OtherFilterKind.HasAttribute:
                f = new OtherFilter(NewFilterKind, Attributes: FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);
                break;
            default:
                if (!long.TryParse(NewFilterNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0) { FilterError = "Enter a whole number."; return; }
                f = new OtherFilter(NewFilterKind, n);
                break;
        }

        OtherFilters.Add(new OtherFilterItem(f));
    }

    [RelayCommand]
    private void RemoveFilter(OtherFilterItem? item)
    {
        if (item is not null) { OtherFilters.Remove(item); }
    }

    [RelayCommand]
    private void RemoveAlignment(AlignmentItem? item)
    {
        if (item is not null) { Alignments.Remove(item); }
    }

    [RelayCommand]
    private void ResetColors() => ColorOlder = ColorSame = ColorOrphan = ColorNewer = ColorFiltered = "";
}
