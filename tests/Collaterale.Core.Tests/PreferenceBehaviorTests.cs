using Collaterale.Core.Folders;
using Collaterale.Core.Settings;
using Collaterale.Core.Support;

namespace Collaterale.Core.Tests;

public class ArchiveTypePreferenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "collaterale-at-" + Guid.NewGuid().ToString("N")[..8]);

    public ArchiveTypePreferenceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        ArchiveExtractor.Configure(null);
        Directory.Delete(_dir, recursive: true);
    }

    private string Touch(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void The_built_in_list_is_used_until_the_preferences_say_otherwise()
    {
        ArchiveExtractor.Configure(null);
        Assert.True(ArchiveExtractor.IsArchive(Touch("a.zip")));
        Assert.True(ArchiveExtractor.IsArchive(Touch("b.tar.gz")));
        Assert.False(ArchiveExtractor.IsArchive(Touch("c.dat")));
    }

    [Fact]
    public void A_mask_can_add_a_type_and_an_empty_mask_drops_a_format()
    {
        ArchiveExtractor.Configure([new ArchiveType { Format = "Zip", Mask = "*.zip;*.dat" }, new ArchiveType { Format = "Tar", Mask = "" }]);
        Assert.True(ArchiveExtractor.IsArchive(Touch("c.dat")));
        Assert.True(ArchiveExtractor.IsArchive(Touch("d.ZIP")));          // not case sensitive
        Assert.False(ArchiveExtractor.IsArchive(Touch("e.tar")));         // Tar has no mask: dropped
    }

    [Fact]
    public void The_format_list_names_the_formats_the_program_can_read()
    {
        Assert.Contains(ArchiveExtractor.DefaultTypes, t => t.Format == "Zip" && t.Mask.Contains("*.zip"));
        Assert.Contains(ArchiveExtractor.DefaultTypes, t => t.Format == "Tar" && t.Mask.Contains("*.tar"));
        Assert.Contains(ArchiveExtractor.DefaultTypes, t => t.Format == "Tar (gzip)" && t.Mask.Contains("*.tar.gz"));
    }
}

public class MessageLogTests
{
    private static MessageLog Log(Action<LogPreferences>? tweak = null)
    {
        var prefs = new LogPreferences();
        tweak?.Invoke(prefs);
        return new MessageLog(prefs, () => new DateTime(2026, 1, 1, 9, 30, 0));
    }

    [Fact]
    public void Each_level_is_shown_only_when_its_preference_is_on()
    {
        var log = Log(p => { p.Summaries = true; p.Details = false; p.Warnings = true; p.Debug = false; });
        log.Summary("done");
        log.Detail("each file");
        log.Warning("careful");
        log.Debug("internals");
        Assert.Equal(["done", "careful"], log.Lines.Select(l => l.Text));

        var all = Log(p => { p.Details = true; p.Debug = true; });
        all.Summary("a"); all.Detail("b"); all.Debug("c");
        Assert.Equal(3, all.Lines.Count);

        var none = Log(p => { p.Summaries = false; p.Warnings = false; });
        none.Summary("a"); none.Warning("b");
        Assert.Empty(none.Lines);
    }

    [Fact]
    public void The_oldest_messages_are_dropped_beyond_the_maximum()
    {
        var log = Log(p => p.MaxMessages = 3);
        for (var i = 1; i <= 5; i++) { log.Summary("m" + i); }
        Assert.Equal(["m3", "m4", "m5"], log.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Messages_are_stamped_with_the_time_and_marked_by_level()
    {
        var log = Log();
        log.Warning("careful");
        Assert.Equal("09:30:00", log.Lines[0].Time);
        Assert.Equal(LogLevel.Warning, log.Lines[0].Level);
    }

    [Fact]
    public void Messages_are_appended_to_the_file_when_one_is_set()
    {
        var path = Path.Combine(Path.GetTempPath(), "collaterale-log-" + Guid.NewGuid().ToString("N")[..8], "log.txt");
        var log = Log(p => p.AutoSaveFile = path);
        log.Summary("first");
        log.Warning("second");
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Equal("2026-01-01 09:30:00  first\n2026-01-01 09:30:00  warning: second\n", text);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void A_log_file_that_cannot_be_written_never_breaks_the_program()
    {
        var log = Log(p => p.AutoSaveFile = "/proc/definitely/not/writable/log.txt");
        log.Summary("still fine");
        Assert.Single(log.Lines);
    }
}

public class IncrementalSearchTests
{
    private sealed class Clock { public DateTime Now = new(2026, 1, 1); }

    private static (IncrementalSearch Search, Clock Clock) Make(int resetMs = 1000)
    {
        var clock = new Clock();
        return (new IncrementalSearch(resetMs, () => clock.Now), clock);
    }

    private static readonly string[] Names = ["alpha.txt", "apple.txt", "Banana.txt", "berry.txt", "cherry.txt"];

    [Fact]
    public void Typing_letters_narrows_the_match_and_ignores_case()
    {
        var (s, _) = Make();
        Assert.Equal(2, s.Type('b', Names, current: 0));            // first name starting with b
        Assert.Equal(3, s.Type('e', Names, current: 2));            // "be" → berry.txt
        Assert.Equal("be", s.Buffer);
    }

    [Fact]
    public void A_pause_longer_than_the_limit_starts_a_new_word()
    {
        var (s, clock) = Make(500);
        s.Type('b', Names, 0);
        clock.Now += TimeSpan.FromMilliseconds(501);
        Assert.Equal(4, s.Type('c', Names, 2));                     // "c" alone, not "bc"
        Assert.Equal("c", s.Buffer);
    }

    [Fact]
    public void Repeating_one_letter_cycles_through_the_names_that_start_with_it()
    {
        var (s, _) = Make();
        Assert.Equal(0, s.Type('a', Names, current: 4));            // from the end: wraps to the first "a"
        Assert.Equal(1, s.Type('a', Names, current: 0));            // same letter again: the next one
        Assert.Equal(0, s.Type('a', Names, current: 1));            // and back around
    }

    [Fact]
    public void No_match_keeps_the_current_item()
    {
        var (s, _) = Make();
        Assert.Equal(3, s.Type('z', Names, current: 3));
    }

    [Fact]
    public void A_zero_limit_means_every_key_starts_afresh_and_reset_clears_the_buffer()
    {
        var (s, _) = Make(0);
        s.Type('b', Names, 0);
        s.Type('e', Names, 0);
        Assert.Equal("e", s.Buffer);
        var (t, _) = Make();
        t.Type('b', Names, 0);
        t.Reset();
        Assert.Equal("", t.Buffer);
    }
}
