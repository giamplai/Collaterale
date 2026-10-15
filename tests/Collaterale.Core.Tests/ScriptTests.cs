using Collaterale.Core.Cli;
using Collaterale.Core.Scripting;
using Collaterale.Core.Settings;

namespace Collaterale.Core.Tests;

public class ScriptTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "collaterale-sc-" + Guid.NewGuid().ToString("N")[..8]);
    private string L => Path.Combine(_dir, "L");
    private string R => Path.Combine(_dir, "R");

    public ScriptTests()
    {
        Directory.CreateDirectory(L);
        Directory.CreateDirectory(R);
        Write(L, "same.txt", "same");
        Write(R, "same.txt", "same");
        Write(L, "changed.txt", "old content");
        Write(R, "changed.txt", "new content!");
        Write(L, "only-left.cs", "1");
        Write(R, "only-right.md", "2");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static void Write(string dir, string name, string content) => File.WriteAllText(Path.Combine(dir, name), content);

    private (int Code, string Out) Run(string script, ScriptPreferences? prefs = null, string? workFolder = null)
    {
        var path = Path.Combine(_dir, "script.txt");
        File.WriteAllText(path, script.Replace("{L}", L).Replace("{R}", R).Replace("{D}", _dir));
        var o = new StringWriter();
        var code = ScriptEngine.RunFile(path, o, prefs ?? new ScriptPreferences());
        return (code, o.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void A_script_that_loads_and_reports_writes_the_report_and_succeeds()
    {
        var (code, _) = Run("""
            # compare the folders and write a report
            load "{L}" "{R}"
            report "{D}/report.txt"
            """);
        Assert.Equal(0, code);
        var report = File.ReadAllText(Path.Combine(_dir, "report.txt"));
        Assert.Contains("changed.txt", report);
        Assert.Contains("only-left.cs", report);
        Assert.Contains("only-right.md", report);
        Assert.DoesNotContain("same.txt", report);
    }

    [Fact]
    public void Filters_and_criteria_change_what_is_compared()
    {
        var (code, _) = Run("""
            load "{L}" "{R}"
            filter "*.txt"
            criteria content
            report "{D}/r.txt"
            """);
        Assert.Equal(0, code);
        var report = File.ReadAllText(Path.Combine(_dir, "r.txt"));
        Assert.Contains("changed.txt", report);
        Assert.DoesNotContain("only-left.cs", report);
    }

    [Fact]
    public void Select_and_copy_change_the_folders()
    {
        var (code, _) = Run("""
            load "{L}" "{R}"
            select left-only
            copy left->right
            """);
        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(R, "only-left.cs")));
        Assert.False(File.Exists(Path.Combine(L, "only-right.md")));
    }

    [Fact]
    public void Sync_runs_a_mirror()
    {
        var (code, _) = Run("""
            load "{L}" "{R}"
            sync mirror left->right
            """);
        Assert.Equal(0, code);
        Assert.False(File.Exists(Path.Combine(R, "only-right.md")));
        Assert.Equal("old content", File.ReadAllText(Path.Combine(R, "changed.txt")));
    }

    [Fact]
    public void Delete_removes_the_selection_from_one_side()
    {
        var (code, _) = Run("""
            load "{L}" "{R}"
            select right-only
            delete right
            """);
        Assert.Equal(0, code);
        Assert.False(File.Exists(Path.Combine(R, "only-right.md")));
    }

    [Fact]
    public void Log_prints_and_variables_in_the_environment_are_not_expanded_by_surprise()
    {
        var (code, output) = Run("log \"hello $HOME world\"");
        Assert.Equal(0, code);
        Assert.Equal("hello $HOME world\n", output);
    }

    [Fact]
    public void A_missing_script_is_error_105()
    {
        Assert.Equal(105, ScriptEngine.RunFile(Path.Combine(_dir, "nope.txt"), TextWriter.Null, new ScriptPreferences()));
    }

    [Fact]
    public void Unknown_commands_and_bad_arguments_are_a_syntax_error_106_naming_the_line()
    {
        var (code, output) = Run("load \"{L}\" \"{R}\"\nfrobnicate now\n");
        Assert.Equal(106, code);
        Assert.Contains("line 2", output);
        Assert.Equal(106, Run("load only-one-argument").Code);
        Assert.Equal(106, Run("criteria bogus").Code);
        Assert.Equal(106, Run("log \"unterminated").Code);
    }

    [Fact]
    public void Nothing_runs_when_a_later_line_has_a_syntax_error()
    {
        Write(L, "marker.txt", "x");
        var (code, _) = Run("load \"{L}\" \"{R}\"\nselect all\ncopy left->right\nbogus line\n");
        Assert.Equal(106, code);
        Assert.False(File.Exists(Path.Combine(R, "marker.txt")));      // the whole script is checked before anything is done
    }

    [Fact]
    public void A_folder_that_cannot_be_loaded_is_error_107()
    {
        var (code, output) = Run("load \"{D}/missing\" \"{R}\"");
        Assert.Equal(107, code);
        Assert.Contains("missing", output);
    }

    [Fact]
    public void A_command_before_load_fails_with_107()
    {
        Assert.Equal(107, Run("select all").Code);
    }

    [Fact]
    public void Exit_stops_the_script_with_the_given_code()
    {
        var (code, _) = Run("exit 7\nbogus");           // the syntax is still checked first
        Assert.Equal(106, code);
        Assert.Equal(7, Run("exit 7\nlog \"never\"").Code);
    }

    [Fact]
    public void Relative_script_names_are_found_in_the_shared_folder()
    {
        var shared = Path.Combine(_dir, "shared");
        Directory.CreateDirectory(shared);
        File.WriteAllText(Path.Combine(shared, "common.txt"), "log \"from shared\"");
        var o = new StringWriter();
        var code = ScriptEngine.RunNamed("common.txt", o, new ScriptPreferences { SharedFolder = shared }, workingDirectory: _dir);
        Assert.Equal(0, code);
        Assert.Equal("from shared", o.ToString().Trim());
        Assert.Equal(105, ScriptEngine.RunNamed("common.txt", o, new ScriptPreferences(), workingDirectory: _dir));
    }

    [Fact]
    public void A_script_can_run_another_with_include()
    {
        File.WriteAllText(Path.Combine(_dir, "inner.txt"), "log \"inner\"");
        var (code, output) = Run("log \"outer\"\ninclude \"{D}/inner.txt\"\nlog \"done\"");
        Assert.Equal(0, code);
        Assert.Equal("outer\ninner\ndone\n", output);
    }

    [Fact]
    public void Text_compare_in_a_script_reports_through_the_log()
    {
        Write(_dir, "a.txt", "x\ny\n");
        Write(_dir, "b.txt", "x\nz\n");
        var (code, output) = Run("diff \"{D}/a.txt\" \"{D}/b.txt\"");
        Assert.Equal(0, code);
        Assert.Contains("-y", output);
        Assert.Contains("+z", output);
    }
}
