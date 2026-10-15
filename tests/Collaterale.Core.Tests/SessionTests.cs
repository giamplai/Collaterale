using Collaterale.Core.Cli;
using Collaterale.Core.Settings;

namespace Collaterale.Core.Tests;

public class SessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "collaterale-ss-" + Guid.NewGuid().ToString("N")[..8]);

    public SessionTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static SavedSession S(string name, string left = "/a", string right = "/b", LaunchKind kind = LaunchKind.FolderCompare)
        => new() { Name = name, Kind = kind, Left = left, Right = right };

    [Fact]
    public void Saving_a_session_under_an_existing_name_replaces_it()
    {
        var s = new AppSettings();
        s.SaveSession(S("Project", "/a", "/b"));
        s.SaveSession(S("Other"));
        s.SaveSession(S("project", "/x", "/y"));                    // names are not case sensitive
        Assert.Equal(2, s.Sessions.Count);
        Assert.Equal("/x", s.FindSession("PROJECT")!.Left);
        Assert.True(s.DeleteSession("other"));
        Assert.False(s.DeleteSession("other"));
        Assert.Single(s.Sessions);
    }

    [Fact]
    public void A_name_finds_a_session_first_then_a_workspace()
    {
        var s = new AppSettings();
        s.SaveSession(S("Daily"));
        s.SaveWorkspace(new Workspace { Name = "Daily", Sessions = [S("inner")] });
        s.SaveWorkspace(new Workspace { Name = "Morning", Sessions = [S("one"), S("two")], Selected = 1 });

        Assert.Equal(S("Daily").Name, s.Resolve("daily").Sessions.Single().Name);
        var w = s.Resolve("Morning");
        Assert.Equal(["one", "two"], w.Sessions.Select(x => x.Name));
        Assert.Equal(1, w.Selected);
        Assert.Empty(s.Resolve("nothing").Sessions);
    }

    [Fact]
    public void A_session_converts_to_the_request_that_opens_it_and_back()
    {
        var req = new LaunchRequest(LaunchKind.Merge, "/l", "/r", "/b", "/o");
        var saved = SavedSession.From("M", req);
        Assert.Equal(("/l", "/r", "/b", "/o", LaunchKind.Merge), (saved.Left, saved.Right, saved.Base, saved.Output, saved.Kind));
        Assert.Equal(req, saved.ToRequest());
    }

    [Fact]
    public void Auto_saved_workspaces_are_pruned_to_the_preferred_number_and_keep_the_newest()
    {
        var s = new AppSettings();
        s.Prefs.Metrics.MaxAutoSavedSessions = 3;
        s.SaveWorkspace(new Workspace { Name = "Mine", Sessions = [S("x")] });
        for (var i = 0; i < 5; i++) { s.AutoSaveWorkspace(new Workspace { Sessions = [S("s" + i)] }, new DateTime(2026, 1, 1, 10, i, 0)); }

        var auto = s.Workspaces.Where(w => w.AutoSaved).Select(w => w.Name).ToList();
        Assert.Equal(3, auto.Count);
        Assert.Equal("Autosave 2026-01-01 10:04", auto[0]);          // newest first
        Assert.Contains(s.Workspaces, w => w.Name == "Mine");         // saved by hand: untouched
        s.Prefs.Metrics.MaxAutoSavedSessions = 0;                     // 0 turns the feature off
        s.AutoSaveWorkspace(new Workspace { Sessions = [S("more")] }, new DateTime(2026, 1, 2));
        Assert.DoesNotContain(s.Workspaces, w => w.AutoSaved);
    }

    [Fact]
    public void Sessions_that_are_still_valid_are_the_ones_whose_paths_exist()
    {
        var real = Path.Combine(_dir, "real.txt");
        File.WriteAllText(real, "x");
        Assert.True(S("ok", real, real, LaunchKind.TextCompare).PathsExist());
        Assert.False(S("gone", real, "/no/such", LaunchKind.TextCompare).PathsExist());
    }

    [Fact]
    public void Shared_sessions_come_from_a_file_and_cannot_be_changed_here()
    {
        var shared = Path.Combine(_dir, "shared.json");
        var src = new AppSettings();
        src.SaveSession(S("Team", "/t1", "/t2"));
        SessionFile.Write(shared, src.Sessions, src.Workspaces);

        var s = new AppSettings();
        s.Prefs.SharedSessionsFile = shared;
        s.SaveSession(S("Mine"));
        var all = s.AllSessions().ToList();
        Assert.Equal(["Mine", "Team"], all.Select(x => x.Session.Name).Order());
        Assert.True(all.Single(x => x.Session.Name == "Team").Shared);
        Assert.False(all.Single(x => x.Session.Name == "Mine").Shared);
        Assert.Equal("/t1", s.FindSession("Team")!.Left);           // shared sessions can be used
        Assert.False(s.DeleteSession("Team"));                       // but not removed
        Assert.Single(s.Sessions);
    }

    [Fact]
    public void A_missing_or_broken_shared_file_is_ignored()
    {
        var s = new AppSettings();
        s.Prefs.SharedSessionsFile = Path.Combine(_dir, "none.json");
        Assert.Empty(s.AllSessions());
        var bad = Path.Combine(_dir, "bad.json");
        File.WriteAllText(bad, "{ not json");
        s.Prefs.SharedSessionsFile = bad;
        Assert.Empty(s.AllSessions());
    }
}
