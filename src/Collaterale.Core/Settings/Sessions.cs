using System.Text.Json;
using Collaterale.Core.Cli;

namespace Collaterale.Core.Settings;

/// <summary>A comparison saved under a name, so it can be opened again later (<c>collaterale "My Session"</c>).</summary>
public sealed class SavedSession
{
    public string Name { get; set; } = "";
    public LaunchKind Kind { get; set; }
    public string? Left { get; set; }
    public string? Right { get; set; }
    public string? Base { get; set; }
    public string? Output { get; set; }

    /// <summary>Name filter of a folder session.</summary>
    public string? Filters { get; set; }

    public static SavedSession From(string name, LaunchRequest req) => new()
    {
        Name = name, Kind = req.Kind, Left = req.Left, Right = req.Right, Base = req.Base, Output = req.Output, Filters = req.Options.Filters,
    };

    public LaunchRequest ToRequest() => new(Kind, Left, Right, Base, Output) { Options = Filters is { Length: > 0 } ? new LaunchOptions { Filters = Filters } : LaunchOptions.None };

    /// <summary>True when every file or folder the session names is still there.</summary>
    public bool PathsExist() => new[] { Left, Right, Base }.Where(p => !string.IsNullOrEmpty(p)).All(p => File.Exists(p) || Directory.Exists(p));
}

/// <summary>The sessions that were open together; opening it brings them all back.</summary>
public sealed class Workspace
{
    public string Name { get; set; } = "";
    public List<SavedSession> Sessions { get; set; } = [];

    /// <summary>Index of the tab that was in front.</summary>
    public int Selected { get; set; }

    /// <summary>Saved by the program when it closed, rather than by the user.</summary>
    public bool AutoSaved { get; set; }
}

/// <summary>A file of sessions and workspaces, to share with others or to import.</summary>
public static class SessionFile
{
    private sealed record Content(List<SavedSession> Sessions, List<Workspace> Workspaces);

    public static void Write(string path, IEnumerable<SavedSession> sessions, IEnumerable<Workspace> workspaces)
        => File.WriteAllText(path, JsonSerializer.Serialize(new Content([.. sessions], [.. workspaces]), new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));

    public static (List<SavedSession> Sessions, List<Workspace> Workspaces) Read(string path)
    {
        try
        {
            var c = JsonSerializer.Deserialize<Content>(File.ReadAllText(path), new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
            return (c?.Sessions ?? [], c?.Workspaces ?? []);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return ([], []);
        }
    }
}
