using System.IO.Compression;
using System.Text.Json;

namespace Collaterale.Core.Settings;

/// <summary>Settings, colors, filter presets and sessions in one file (<c>.cpkg</c>), to move to another machine or share with a team.</summary>
public static class SettingsPackage
{
    public const string Extension = ".cpkg";
    private const string Entry = "settings.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static void Export(AppSettings settings, string path)
    {
        // what was opened (the history) is private and stays out
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Json), Json)!;
        copy.Recent = [];
        if (File.Exists(path)) { File.Delete(path); }
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var w = new StreamWriter(zip.CreateEntry(Entry).Open());
        w.Write(JsonSerializer.Serialize(copy, Json));
    }

    /// <summary>Applies a package to the settings and returns a one-line summary. Sessions and workspaces with the same name are replaced, others kept.</summary>
    public static string Import(string path, AppSettings into)
    {
        AppSettings from;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry(Entry) ?? throw new InvalidDataException("This is not a settings package: it has no settings.json.");
            using var r = new StreamReader(entry.Open());
            from = JsonSerializer.Deserialize<AppSettings>(r.ReadToEnd(), Json) ?? throw new InvalidDataException("The settings package is empty.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new InvalidDataException(ex is InvalidDataException && ex.Message.Contains("settings.json", StringComparison.Ordinal) ? ex.Message : $"'{Path.GetFileName(path)}' is not a settings package ({ex.Message}).", ex);
        }

        into.Theme = from.Theme;
        into.Whitespace = from.Whitespace;
        into.IgnoreCase = from.IgnoreCase;
        into.Algorithm = from.Algorithm;
        into.Colors = from.Colors;
        into.Prefs = from.Prefs;
        into.FolderDefaults = from.FolderDefaults;
        into.Criteria = from.Criteria;
        into.ExcludeFolders = from.ExcludeFolders;
        into.ExcludeFiles = from.ExcludeFiles;
        into.FilterPresets = from.FilterPresets;
        foreach (var s in from.Sessions) { into.SaveSession(s); }
        foreach (var w in from.Workspaces) { into.SaveWorkspace(w); }
        return $"Imported the settings, {from.Sessions.Count} session{(from.Sessions.Count == 1 ? "" : "s")} and {from.Workspaces.Count} workspace{(from.Workspaces.Count == 1 ? "" : "s")}.";
    }
}
