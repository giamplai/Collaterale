using System.Text;
using Collaterale.Core.Cli;
using Collaterale.Core.Diff;
using Collaterale.Core.Folders;
using Collaterale.Core.Settings;
using Collaterale.Core.Text;

namespace Collaterale.Core.Scripting;

/// <summary>
/// Runs a Collaterale script (<c>collaterale @script.txt</c>): one command per line, <c>#</c> starts a comment, text with spaces goes in double quotes.
/// <code>
/// load "left folder" "right folder"        compare two folders (everything below uses this comparison)
/// filter "*.cs;*.md"                        only these files
/// criteria size | time | sizetime | content how files are judged different
/// select all | diffs | same | left-only | right-only | newer-left | newer-right | none
/// copy left-&gt;right | right-&gt;left            copy the selection
/// delete left | right                       delete the selection from that side
/// sync mirror|update left-&gt;right | right-&gt;left, or sync two-way
/// report "file"                             write the differences to a text file
/// diff "a" "b"                              print a unified diff of two files
/// include "other script"                    run another script here
/// log "message"                             print a message
/// exit N                                    stop with that exit code
/// </code>
/// The whole script is checked before anything is done, so a typo late in the file never leaves a half-done job.
/// Exit codes: 0 done, 105 the script could not be loaded, 106 syntax error, 107 a command failed.
/// </summary>
public static class ScriptEngine
{
    private sealed record Command(int Line, string Name, IReadOnlyList<string> Args);

    private sealed class SyntaxError(string message) : Exception(message);

    private sealed class Failure(string message) : Exception(message);

    private sealed class Stop(int code) : Exception { public int Code { get; } = code; }

    /// <summary>Runs a script by name: as given, or (when it is a plain name that is not found) from the shared scripts folder.</summary>
    public static int RunNamed(string name, TextWriter output, ScriptPreferences prefs, string? workingDirectory = null)
    {
        var path = Path.IsPathRooted(name) ? name : Path.Combine(workingDirectory ?? Directory.GetCurrentDirectory(), name);
        if (!File.Exists(path) && !Path.IsPathRooted(name) && !string.IsNullOrWhiteSpace(prefs.SharedFolder))
        {
            var shared = Path.Combine(prefs.SharedFolder, name);
            if (File.Exists(shared)) { path = shared; }
        }

        return RunFile(path, output, prefs);
    }

    public static int RunFile(string path, TextWriter output, ScriptPreferences prefs)
    {
        if (!File.Exists(path))
        {
            output.WriteLine($"The script '{path}' does not exist.");
            return ExitCodes.ScriptLoadError;
        }

        int code;
        try
        {
            var commands = Parse(File.ReadAllText(path));
            code = new Runner(output).Run(commands, Path.GetDirectoryName(Path.GetFullPath(path))!);
        }
        catch (SyntaxError ex)
        {
            output.WriteLine(ex.Message);
            code = ExitCodes.ScriptSyntaxError;
        }
        catch (IOException ex)
        {
            output.WriteLine(ex.Message);
            code = ExitCodes.ScriptLoadError;
        }

        if (prefs.BeepWhenFinished) { output.Write('\a'); }
        return code;
    }

    // ───────────── reading ─────────────

    private static readonly Dictionary<string, (int Min, int Max)> Arity = new()
    {
        ["load"] = (2, 2), ["filter"] = (1, 1), ["criteria"] = (1, 1), ["select"] = (1, 1), ["copy"] = (1, 1), ["delete"] = (1, 1),
        ["sync"] = (1, 2), ["report"] = (1, 1), ["diff"] = (2, 2), ["include"] = (1, 1), ["log"] = (1, 1), ["exit"] = (0, 1),
    };

    private static readonly string[] Criteria = ["size", "time", "sizetime", "content"];
    private static readonly string[] Selections = ["all", "diffs", "same", "left-only", "right-only", "newer-left", "newer-right", "none"];

    private static List<Command> Parse(string text)
    {
        var commands = new List<Command>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var tokens = Tokenize(lines[i], i + 1);
            if (tokens.Count == 0) { continue; }
            var name = tokens[0].ToLowerInvariant();
            var args = tokens.Skip(1).ToList();
            if (!Arity.TryGetValue(name, out var arity)) { throw new SyntaxError($"line {i + 1}: unknown command '{tokens[0]}'."); }
            if (args.Count < arity.Min || args.Count > arity.Max) { throw new SyntaxError($"line {i + 1}: '{name}' takes {(arity.Min == arity.Max ? arity.Min.ToString() : $"{arity.Min} to {arity.Max}")} argument(s), got {args.Count}."); }
            Validate(name, args, i + 1);
            commands.Add(new Command(i + 1, name, args));
        }

        return commands;
    }

    private static void Validate(string name, List<string> args, int line)
    {
        void Fail(string what) => throw new SyntaxError($"line {line}: {what}");
        switch (name)
        {
            case "criteria" when !Criteria.Contains(args[0].ToLowerInvariant()): Fail($"'{args[0]}' is not one of {string.Join(", ", Criteria)}."); break;
            case "select" when !Selections.Contains(args[0].ToLowerInvariant()): Fail($"'{args[0]}' is not one of {string.Join(", ", Selections)}."); break;
            case "copy" when args[0] is not ("left->right" or "right->left"): Fail("copy needs left->right or right->left."); break;
            case "delete" when args[0] is not ("left" or "right"): Fail("delete needs left or right."); break;
            case "sync":
                var kind = args[0].ToLowerInvariant();
                if (kind == "two-way" ? args.Count != 1 : kind is not ("mirror" or "update") || args.Count != 2 || args[1] is not ("left->right" or "right->left"))
                {
                    Fail("sync needs: mirror|update left->right|right->left, or two-way.");
                }

                break;
            case "exit" when args.Count == 1 && !int.TryParse(args[0], out _): Fail("exit needs a number."); break;
        }
    }

    private static List<string> Tokenize(string line, int number)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var inToken = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                var end = line.IndexOf('"', i + 1);
                if (end < 0) { throw new SyntaxError($"line {number}: a quote is not closed."); }
                sb.Append(line, i + 1, end - i - 1);
                inToken = true;
                i = end;
            }
            else if (c == '#' && !inToken)
            {
                break;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inToken) { tokens.Add(sb.ToString()); sb.Clear(); inToken = false; }
            }
            else
            {
                sb.Append(c);
                inToken = true;
            }
        }

        if (inToken) { tokens.Add(sb.ToString()); }
        return tokens;
    }

    // ───────────── running ─────────────

    private sealed class Runner(TextWriter output)
    {
        private FolderComparison? _comparison;
        private (string Left, string Right)? _roots;
        private string _include = "";
        private CompareCriteria _criteria = CompareCriteria.SizeAndTime;
        private List<FolderNode> _selection = [];

        public int Run(List<Command> commands, string baseDirectory)
        {
            try
            {
                foreach (var c in commands) { Execute(c, baseDirectory); }
                return ExitCodes.Success;
            }
            catch (Stop stop)
            {
                return stop.Code;
            }
            catch (Failure ex)
            {
                output.WriteLine(ex.Message);
                return ExitCodes.ScriptFailed;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                output.WriteLine(ex.Message);
                return ExitCodes.ScriptFailed;
            }
        }

        private string Resolve(string path, string baseDirectory) => Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path);

        private FolderComparison Loaded => _comparison ?? throw new Failure("Nothing is loaded yet: start with a 'load' command.");

        private void Reload()
        {
            if (_roots is not { } r) { return; }
            _comparison = FolderComparer.Compare(r.Left, r.Right, new FolderOptions { Criteria = _criteria, Include = _include });
            _selection = [];
        }

        private void Execute(Command c, string baseDirectory)
        {
            var a = c.Args;
            switch (c.Name)
            {
                case "load":
                    var (l, r) = (Resolve(a[0], baseDirectory), Resolve(a[1], baseDirectory));
                    foreach (var p in new[] { l, r })
                    {
                        if (!Directory.Exists(p)) { throw new Failure($"line {c.Line}: the folder '{p}' cannot be loaded."); }
                    }

                    _roots = (l, r);
                    Reload();
                    break;
                case "filter":
                    _include = a[0];
                    Reload();
                    break;
                case "criteria":
                    _criteria = a[0].ToLowerInvariant() switch { "size" => CompareCriteria.SizeOnly, "content" => CompareCriteria.Content, _ => CompareCriteria.SizeAndTime };
                    Reload();
                    break;
                case "select":
                    var files = Loaded.Root.Descendants().Where(n => !n.IsDirectory).ToList();
                    _selection = a[0].ToLowerInvariant() switch
                    {
                        "all" => files,
                        "diffs" => [.. files.Where(n => n.State == FolderEntryState.Different)],
                        "same" => [.. files.Where(n => n.State == FolderEntryState.Same)],
                        "left-only" => [.. files.Where(n => n.State == FolderEntryState.LeftOnly)],
                        "right-only" => [.. files.Where(n => n.State == FolderEntryState.RightOnly)],
                        "newer-left" => [.. files.Where(n => n.State == FolderEntryState.Different && n.Newer == Newer.Left)],
                        "newer-right" => [.. files.Where(n => n.State == FolderEntryState.Different && n.Newer == Newer.Right)],
                        _ => [],
                    };
                    break;
                case "copy":
                    Apply(FolderSync.PlanFor(Loaded, _selection, a[0] == "left->right" ? SyncAction.CopyToRight : SyncAction.CopyToLeft), c.Line);
                    break;
                case "delete":
                    Apply(FolderSync.PlanFor(Loaded, _selection, a[0] == "left" ? SyncAction.DeleteLeft : SyncAction.DeleteRight), c.Line);
                    break;
                case "sync":
                    var mode = a[0].ToLowerInvariant() switch
                    {
                        "two-way" => SyncMode.TwoWay,
                        "mirror" => a[1] == "left->right" ? SyncMode.MirrorToRight : SyncMode.MirrorToLeft,
                        _ => a[1] == "left->right" ? SyncMode.UpdateRight : SyncMode.UpdateLeft,
                    };
                    Apply(FolderSync.Plan(Loaded, mode), c.Line);
                    break;
                case "report":
                    WriteReport(Resolve(a[0], baseDirectory));
                    break;
                case "diff":
                    PrintDiff(Resolve(a[0], baseDirectory), Resolve(a[1], baseDirectory), c.Line);
                    break;
                case "include":
                    var inner = Resolve(a[0], baseDirectory);
                    if (!File.Exists(inner)) { throw new Failure($"line {c.Line}: the script '{inner}' does not exist."); }
                    try
                    {
                        foreach (var ic in Parse(File.ReadAllText(inner))) { Execute(ic, Path.GetDirectoryName(inner)!); }
                    }
                    catch (SyntaxError ex)
                    {
                        throw new Failure($"{Path.GetFileName(inner)}: {ex.Message}");
                    }

                    break;
                case "log":
                    output.WriteLine(a[0]);
                    break;
                case "exit":
                    throw new Stop(a.Count == 1 ? int.Parse(a[0]) : 0);
            }
        }

        private void Apply(SyncPlan plan, int line)
        {
            var result = FolderSync.Execute(Loaded, plan);
            if (!result.Success) { throw new Failure($"line {line}: {result.Failures.Count} operation(s) failed: {result.Failures[0].Error}"); }
            Reload();
        }

        private void WriteReport(string path)
        {
            var c = Loaded;
            var sb = new StringBuilder();
            var s = c.Summary;
            sb.Append("Collaterale report\n");
            sb.Append($"Left:  {c.LeftRoot}\nRight: {c.RightRoot}\n");
            sb.Append($"{s.Same} same, {s.Different} different, {s.LeftOnly} only on the left, {s.RightOnly} only on the right\n\n");
            foreach (var n in c.Root.Descendants().Where(n => !n.IsDirectory && n.State != FolderEntryState.Same))
            {
                var glyph = n.State switch { FolderEntryState.Different => "≠", FolderEntryState.LeftOnly => "◀", FolderEntryState.RightOnly => "▶", _ => "⚠" };
                sb.Append(glyph).Append(' ').Append(n.RelativePath).Append('\n');
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, sb.ToString());
        }

        private void PrintDiff(string left, string right, int line)
        {
            foreach (var p in new[] { left, right })
            {
                if (!File.Exists(p)) { throw new Failure($"line {line}: the file '{p}' does not exist."); }
            }

            var (a, b) = (TextFile.Load(left), TextFile.Load(right));
            output.Write(UnifiedDiff.Write(left, right, a.Lines, b.Lines, 3, null, a.EndsWithNewline, b.EndsWithNewline));
        }
    }
}
