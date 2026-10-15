using System.Text.Json;

namespace Collaterale.Core.Integration;

/// <summary>The recent texts that were on the clipboard, newest first, so two of them can be compared.</summary>
public sealed class ClipboardHistory
{
    private readonly string _path;
    private readonly int _max;
    private List<string> _entries = [];

    public ClipboardHistory(string path, int max = 10)
    {
        _path = path;
        _max = Math.Max(2, max);
        try
        {
            if (File.Exists(path)) { _entries = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? []; }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _entries = [];
        }
    }

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Collaterale", "clipboard.json");

    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Remembers a clipboard text; false when it is empty or the same as the newest entry.</summary>
    public bool Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || (_entries.Count > 0 && _entries[0] == text)) { return false; }
        _entries.Insert(0, text);
        if (_entries.Count > _max) { _entries.RemoveRange(_max, _entries.Count - _max); }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the history is a convenience; not being able to keep it must not get in the way
        }

        return true;
    }

    /// <summary>The newest entry and the one before it, or null when there are fewer than two.</summary>
    public (string Newest, string Previous)? LastTwo() => _entries.Count >= 2 ? (_entries[0], _entries[1]) : null;
}
