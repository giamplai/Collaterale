using Collaterale.Core.Settings;

namespace Collaterale.Core.Support;

public enum LogLevel { Summary, Detail, Warning, Debug }

public sealed record LogEntry(string Time, LogLevel Level, string Text);

/// <summary>The messages of a session, filtered by what the preferences ask to see, capped, and optionally appended to a file.</summary>
public sealed class MessageLog
{
    private readonly Func<LogPreferences> _prefs;
    private readonly Func<DateTime> _clock;

    public MessageLog(LogPreferences prefs, Func<DateTime>? clock = null) : this(() => prefs, clock)
    {
    }

    /// <summary>The preferences are asked for at every message, so a change in the options window takes effect at once.</summary>
    public MessageLog(Func<LogPreferences> prefs, Func<DateTime>? clock = null)
    {
        _prefs = prefs;
        _clock = clock ?? (() => DateTime.Now);
    }

    private readonly List<LogEntry> _lines = [];

    public IReadOnlyList<LogEntry> Lines => _lines;

    /// <summary>Raised after a message was added.</summary>
    public event Action<LogEntry>? Added;

    public void Summary(string text) => Add(LogLevel.Summary, text);
    public void Detail(string text) => Add(LogLevel.Detail, text);
    public void Warning(string text) => Add(LogLevel.Warning, text);
    public void Debug(string text) => Add(LogLevel.Debug, text);

    public void Clear() => _lines.Clear();

    private bool Wanted(LogLevel level, LogPreferences prefs) => level switch
    {
        LogLevel.Summary => prefs.Summaries,
        LogLevel.Detail => prefs.Details,
        LogLevel.Warning => prefs.Warnings,
        _ => prefs.Debug,
    };

    public void Add(LogLevel level, string text)
    {
        var prefs = _prefs();
        if (!Wanted(level, prefs)) { return; }
        var now = _clock();
        var entry = new LogEntry(now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), level, text);
        _lines.Add(entry);
        var max = Math.Max(1, prefs.MaxMessages);
        if (_lines.Count > max) { _lines.RemoveRange(0, _lines.Count - max); }
        Added?.Invoke(entry);

        if (!string.IsNullOrWhiteSpace(prefs.AutoSaveFile))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(prefs.AutoSaveFile))!);
                var tag = level switch { LogLevel.Detail => "detail: ", LogLevel.Warning => "warning: ", LogLevel.Debug => "debug: ", _ => "" };
                File.AppendAllText(prefs.AutoSaveFile, $"{now:yyyy-MM-dd HH:mm:ss}  {tag}{text}\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                // a log that cannot be saved must never get in the way of the work it describes
            }
        }
    }
}
