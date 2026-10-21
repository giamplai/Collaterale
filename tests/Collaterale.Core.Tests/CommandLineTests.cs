using Collaterale.Core.Cli;

namespace Collaterale.Core.Tests;

public class CommandLineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "collaterale-cl-" + Guid.NewGuid().ToString("N")[..8]);

    public CommandLineTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string F(string name, string content = "x\n")
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    private static LaunchRequest Parse(params string[] args) => LaunchParser.Parse(args);

    // ───────────── switches ─────────────

    [Theory]
    [InlineData("-automerge")]
    [InlineData("--automerge")]
    [InlineData("/automerge")]
    public void A_switch_can_be_written_with_one_dash_two_dashes_or_a_slash(string sw)
    {
        var o = Parse(sw, F("l"), F("r"), F("c")).Options;
        Assert.True(o.AutoMerge);
    }

    [Fact]
    public void A_path_that_looks_like_a_switch_is_still_a_path()
    {
        // on Linux and macOS "/tmp/..." is a path; only "/name" with a known name and no such file is a switch
        Assert.Equal(LaunchKind.TextCompare, Parse(F("a"), F("b")).Kind);
        var o = Parse("/silent", F("a"), F("b")).Options;
        Assert.True(o.Silent);
    }

    [Fact]
    public void Switches_that_take_a_value_accept_equals_or_a_following_argument()
    {
        var req = Parse("/filters=*.cs;*.md", "/title1=Mine", "-title2", "Theirs", F("a"), F("b"));
        Assert.Equal("*.cs;*.md", req.Options.Filters);
        Assert.Equal("Mine", req.Options.Titles[0]);
        Assert.Equal("Theirs", req.Options.Titles[1]);
        Assert.Equal(LaunchKind.TextCompare, req.Kind);
    }

    [Fact]
    public void Title_and_version_control_aliases_share_the_same_slots()
    {
        var o = Parse("/lefttitle=L", "/righttitle=R", "/centertitle=C", "/outputtitle=O", "/vcsleft=vl", "/vcs2=vr", "/vcscenter=vc", "/vcs4=vo", F("a"), F("b")).Options;
        Assert.Equal(["L", "R", "C", "O"], o.Titles);
        Assert.Equal(["vl", "vr", "vc", "vo"], o.Vcs);
    }

    [Fact]
    public void Read_only_switches_apply_to_one_side_or_all()
    {
        Assert.True(Parse("/ro1", F("a"), F("b")).Options is { ReadOnlyLeft: true, ReadOnlyRight: false });
        Assert.True(Parse("/rro", F("a"), F("b")).Options is { ReadOnlyLeft: false, ReadOnlyRight: true });
        Assert.True(Parse("/ro", F("a"), F("b")).Options is { ReadOnlyLeft: true, ReadOnlyRight: true });
        Assert.True(Parse("/leftreadonly", F("a"), F("b")).Options.ReadOnlyLeft);
    }

    [Fact]
    public void Flags_are_collected()
    {
        var o = Parse("/force", "/iu", "/favorleft", "/nobackups", "/solo", "/expandall", "/reviewconflicts", "/closescript", F("a"), F("b")).Options;
        Assert.True(o.Force && o.IgnoreUnimportant && o.FavorLeft && o.NoBackups && o.Solo && o.ExpandAll && o.ReviewConflicts && o.CloseScript);
        Assert.False(o.FavorRight);
    }

    [Fact]
    public void Quick_compare_has_a_type_or_defaults_to_rules()
    {
        Assert.Equal("crc", Parse("/qc=crc", F("a"), F("b")).Options.QuickCompare);
        Assert.Equal("", Parse("/qc", F("a"), F("b")).Options.QuickCompare);
        Assert.Equal("size", Parse("/quickcompare=size", F("a"), F("b")).Options.QuickCompare);
        Assert.Null(Parse(F("a"), F("b")).Options.QuickCompare);
    }

    // ───────────── what to open ─────────────

    [Fact]
    public void Three_files_open_a_merge_with_left_right_and_center()
    {
        var (l, r, c) = (F("l"), F("r"), F("c"));
        var req = Parse(l, r, c);
        Assert.Equal((LaunchKind.Merge, l, r, c, (string?)null), (req.Kind, req.Left, req.Right, req.Base, req.Output));
    }

    [Fact]
    public void Four_files_add_the_output()
    {
        var (l, r, c, o) = (F("l"), F("r"), F("c"), Path.Combine(_dir, "out"));
        var req = Parse(l, r, c, o);
        Assert.Equal((LaunchKind.Merge, l, r, c, o), (req.Kind, req.Left, req.Right, req.Base, req.Output));
    }

    [Fact]
    public void Center_and_merge_output_switches_complete_a_two_file_command()
    {
        var (l, r, c) = (F("l"), F("r"), F("c"));
        var out1 = Path.Combine(_dir, "o");
        var req = Parse($"/center={c}", $"/mergeoutput={out1}", l, r);
        Assert.Equal((LaunchKind.Merge, l, r, c, out1), (req.Kind, req.Left, req.Right, req.Base, req.Output));
    }

    [Fact]
    public void A_script_a_package_a_patch_and_stdin_have_their_own_kinds()
    {
        var script = F("run.txt");
        Assert.Equal((LaunchKind.Script, script), (Parse("@" + script).Kind, Parse("@" + script).Left));
        var pkg = F("settings.cpkg");
        Assert.Equal(LaunchKind.Package, Parse(pkg).Kind);
        var patch = F("fix.patch");
        Assert.Equal(LaunchKind.Patch, Parse(patch).Kind);
        Assert.Equal(LaunchKind.Patch, Parse(F("x.diff")).Kind);
        Assert.Equal(LaunchKind.Stdin, Parse("-").Kind);
    }

    [Fact]
    public void A_name_that_is_not_a_path_is_a_session_or_workspace_name()
    {
        var req = Parse("My Session");
        Assert.Equal((LaunchKind.Named, "My Session"), (req.Kind, req.Left));
    }

    [Theory]
    [InlineData("Text Compare", LaunchKind.TextCompare)]
    [InlineData("Hex Compare", LaunchKind.HexCompare)]
    [InlineData("Table Compare", LaunchKind.TableCompare)]
    [InlineData("Picture Compare", LaunchKind.ImageCompare)]
    [InlineData("Media Compare", LaunchKind.ImageCompare)]
    [InlineData("Text Merge", LaunchKind.Merge)]
    public void The_file_viewer_switch_forces_the_view(string name, LaunchKind kind)
    {
        var req = kind == LaunchKind.Merge ? Parse($"/fv={name}", F("a"), F("b"), F("c")) : Parse($"/fv={name}", F("a"), F("b"));
        Assert.Equal(kind, req.Kind);
    }

    [Fact]
    public void The_file_viewer_can_be_given_as_a_separate_argument_too()
    {
        Assert.Equal(LaunchKind.HexCompare, Parse("-fileviewer", "Hex Compare", F("a"), F("b")).Kind);
    }

    [Fact]
    public void Edit_opens_a_single_file_for_editing_and_sync_opens_the_sync_view()
    {
        var a = F("a");
        Assert.Equal((LaunchKind.TextEdit, a), (Parse("/edit", a).Kind, Parse("/edit", a).Left));
        var d1 = Directory.CreateDirectory(Path.Combine(_dir, "d1")).FullName;
        var d2 = Directory.CreateDirectory(Path.Combine(_dir, "d2")).FullName;
        Assert.Equal(LaunchKind.FolderSync, Parse("/sync", d1, d2).Kind);
        Assert.Equal(LaunchKind.FolderCompare, Parse("/expandall", d1, d2).Kind);
    }

    [Fact]
    public void Existing_forms_still_work()
    {
        Assert.Equal(LaunchKind.HexCompare, Parse("--hex", F("a"), F("b")).Kind);
        var m = Parse("--merge", "--left", F("l"), "--right", F("r"), "--output", "/tmp/o");
        Assert.Equal(LaunchKind.Merge, m.Kind);
        Assert.Equal(LaunchKind.Help, Parse("/?").Kind);
        Assert.Equal(LaunchKind.Help, Parse("/help").Kind);
    }

    // ───────────── quick compare and its exit codes ─────────────

    private static int Quick(string kind, string a, string b) => CliHeadless.Run(LaunchParser.Parse([kind, a, b]), TextWriter.Null, TextWriter.Null);

    [Fact]
    public void Binary_quick_compare_reports_same_1_or_different_11()
    {
        Assert.Equal(1, Quick("/qc=binary", F("a", "x\n"), F("b", "x\n")));
        Assert.Equal(11, Quick("/qc=binary", F("c", "x\n"), F("d", "y\n")));
    }

    [Fact]
    public void Size_and_crc_quick_compare_use_the_binary_codes()
    {
        Assert.Equal(1, Quick("/qc=size", F("a", "abc"), F("b", "xyz")));           // same size
        Assert.Equal(11, Quick("/qc=size", F("c", "abc"), F("d", "abcd")));
        Assert.Equal(1, Quick("/qc=crc", F("e", "abc"), F("f", "abc")));
        Assert.Equal(11, Quick("/qc=crc", F("g", "abc"), F("h", "abd")));
    }

    [Fact]
    public void Sha_quick_compares_use_the_binary_codes()
    {
        foreach (var (kind, tag) in new[] { ("/qc=sha1", "sha1"), ("/qc=sha256", "sha256") })
        {
            Assert.Equal(1, Quick(kind, F(tag + "-same-a", "abc"), F(tag + "-same-b", "abc")));
            Assert.Equal(11, Quick(kind, F(tag + "-diff-a", "abc"), F(tag + "-diff-b", "abd")));
        }
    }

    [Fact]
    public void Rules_quick_compare_separates_same_similar_and_different()
    {
        Assert.Equal(2, Quick("/qc", F("a", "x\ny\n"), F("b", "x\ny\n")));            // bytes equal
        Assert.Equal(12, Quick("/qc", F("c", "x\ny\n"), F("d", "x\r\ny\r\n")));       // only line endings differ
        Assert.Equal(13, Quick("/qc", F("e", "x\ny\n"), F("f", "x\nz\n")));
    }

    [Fact]
    public void An_unknown_quick_compare_type_or_missing_file_is_error_100()
    {
        Assert.Equal(100, Quick("/qc=bogus", F("a"), F("b")));
        Assert.Equal(100, CliHeadless.Run(new LaunchRequest(LaunchKind.TextCompare, "/no/such", "/no/such2") { Options = new LaunchOptions { QuickCompare = "binary" } }, TextWriter.Null, TextWriter.Null));
    }

    // ───────────── automatic merge ─────────────

    private (string L, string R, string C, string Out) Trio(string l, string r, string c) => (F("l.txt", l), F("r.txt", r), F("c.txt", c), Path.Combine(_dir, "out.txt"));

    private static int Auto(string[] extra, (string L, string R, string C, string Out) t, out string stdout)
    {
        var w = new StringWriter();
        var args = extra.Concat(["/automerge", $"/mergeoutput={t.Out}", t.L, t.R, t.C]).ToArray();
        var code = CliHeadless.Run(LaunchParser.Parse(args), w, w);
        stdout = w.ToString();
        return code;
    }

    [Fact]
    public void Automerge_without_conflicts_writes_the_output_and_returns_zero()
    {
        var t = Trio("a\nb\nc\nX\n", "A\nb\nc\n", "a\nb\nc\n");
        Assert.Equal(0, Auto([], t, out _));
        Assert.Equal("A\nb\nc\nX\n", File.ReadAllText(t.Out).Replace("\r\n", "\n"));
    }

    [Fact]
    public void Automerge_with_conflicts_does_not_write_and_returns_101()
    {
        var t = Trio("L\n", "R\n", "base\n");
        Assert.Equal(101, Auto([], t, out _));
        Assert.False(File.Exists(t.Out));
    }

    [Fact]
    public void Force_writes_conflict_markers_and_returns_14()
    {
        var t = Trio("L\n", "R\n", "base\n");
        Assert.Equal(14, Auto(["/force"], t, out _));
        var text = File.ReadAllText(t.Out).Replace("\r\n", "\n");
        Assert.Contains("<<<<<<<", text);
        Assert.Contains("L\n=======\nR\n", text);
    }

    [Fact]
    public void Favor_left_or_right_resolves_conflicts_by_taking_that_side()
    {
        var t = Trio("L\n", "R\n", "base\n");
        Assert.Equal(0, Auto(["/favorleft"], t, out _));
        Assert.Equal("L\n", File.ReadAllText(t.Out).Replace("\r\n", "\n"));
        File.Delete(t.Out);
        Assert.Equal(0, Auto(["/favorright"], t, out _));
        Assert.Equal("R\n", File.ReadAllText(t.Out).Replace("\r\n", "\n"));
    }

    [Fact]
    public void Ignore_unimportant_resolves_a_whitespace_only_conflict_from_the_favored_side()
    {
        var t = Trio("a  b\n", "a b\n", "a b\n");   // left differs from base only by spaces; right is unchanged
        Assert.Equal(0, Auto(["/iu", "/favorleft"], t, out _));
    }

    [Fact]
    public void Review_conflicts_leaves_the_decision_to_the_window()
    {
        var t = Trio("L\n", "R\n", "base\n");
        var req = LaunchParser.Parse(["/automerge", "/reviewconflicts", $"/mergeoutput={t.Out}", t.L, t.R, t.C]);
        Assert.False(CliHeadless.TryRun(req, TextWriter.Null, TextWriter.Null, out _));   // the app opens a Text Merge view instead
    }

    [Fact]
    public void Automerge_does_not_overwrite_a_backup_when_nobackups_is_set_and_makes_one_otherwise()
    {
        var t = Trio("a\nb\n", "a\nb\n", "a\nb\n");
        File.WriteAllText(t.Out, "old\n");
        Assert.Equal(0, Auto([], t, out _));
        Assert.True(File.Exists(t.Out + ".bak"));
        File.Delete(t.Out + ".bak");
        Assert.Equal(0, Auto(["/nobackups"], t, out _));
        Assert.False(File.Exists(t.Out + ".bak"));
    }
}

public class HeadlessScriptAndPackageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "collaterale-hs-" + Guid.NewGuid().ToString("N")[..8]);

    public HeadlessScriptAndPackageTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_script_on_the_command_line_runs_without_a_window_and_returns_its_code()
    {
        var script = Path.Combine(_dir, "s.txt");
        File.WriteAllText(script, "log \"hi\"\nexit 9\n");
        var o = new StringWriter();
        Assert.True(CliHeadless.TryRun(LaunchParser.Parse(["@" + script]), o, TextWriter.Null, out var code));
        Assert.Equal(9, code);
        Assert.Equal("hi", o.ToString().Trim());
    }

    [Fact]
    public void A_silent_package_is_imported_with_no_window()
    {
        var src = new Collaterale.Core.Settings.AppSettings { Theme = "Dark" };
        var pkg = Path.Combine(_dir, "p.cpkg");
        Collaterale.Core.Settings.SettingsPackage.Export(src, pkg);
        var settings = new Collaterale.Core.Settings.AppSettings();
        var req = LaunchParser.Parse(["/silent", pkg]);
        Assert.Equal(LaunchKind.Package, req.Kind);
        // (saving goes to the user's real settings path, so only the parse and routing are checked here)
        Assert.True(req.Options.Silent);
        Assert.False(CliHeadless.TryRun(LaunchParser.Parse([pkg]), TextWriter.Null, TextWriter.Null, out _, settings));   // not silent: the app opens its import dialog
    }
}

public class QuickDialogTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(12, true)]
    [InlineData(11, false)]
    [InlineData(13, false)]
    [InlineData(100, false)]
    public void Matching_or_similar_files_count_as_the_same(int code, bool same)
        => Assert.Equal(same, CliHeadless.Describe(code).Same);

    [Fact]
    public void The_dialog_is_skipped_for_different_files_only_when_asked()
    {
        Assert.False(CliHeadless.ShowQuickDialog(11, openViewIfDifferent: true));      // straight to the view
        Assert.True(CliHeadless.ShowQuickDialog(11, openViewIfDifferent: false));
        Assert.True(CliHeadless.ShowQuickDialog(1, openViewIfDifferent: true));        // identical files: tell the user instead of a view full of nothing
    }

    [Fact]
    public void Every_sentence_is_a_full_sentence()
    {
        foreach (var code in new[] { 1, 2, 11, 12, 13, 100 }) { Assert.EndsWith(".", CliHeadless.Describe(code).Text); }
    }
}
