namespace Collaterale.Core.Cli;

public enum LaunchKind { Home, TextCompare, FolderCompare, Merge, Help, Version, Error, HexCompare, TableCompare, ImageCompare, ArchiveCompare, TextEdit, Script, Package, Patch, Stdin, Named, FolderSync, Clipboard }

/// <summary>What the command line asks the app to do.</summary>
public sealed record LaunchRequest(LaunchKind Kind, string? Left = null, string? Right = null, string? Base = null, string? Output = null, string? Message = null)
{
    /// <summary>The switches that came with the paths.</summary>
    public LaunchOptions Options { get; init; } = LaunchOptions.None;

    public static string HelpText => """
        Usage:
          collaterale                                   open the home screen
          collaterale <left> <right>                    compare two files or two folders; the view is chosen from the files
                                                    (pictures, .csv/.tsv tables, archives, binary files, otherwise text)
          collaterale --text <left> <right>             always compare as text
          collaterale --diff <left> <right> [-U N] [-w] [-i] [--histogram]
                                                    print a unified diff and exit (no window)
          collaterale --hex <left> <right>              compare two files byte by byte
          collaterale --table <left> <right>            compare two CSV/TSV files cell by cell
          collaterale --image <left> <right>            compare two pictures pixel by pixel
          collaterale --merge --base B --left L --right R --output O
                                                    three-way merge; the result is written to O
          collaterale --select-left <path>              remember a file or folder as the left side (for file managers)
          collaterale --compare-selected <path>         compare the remembered left side with <path>
          collaterale --help | --version

        Exit codes: 0 = success (merge result saved), 1 = closed without saving / error.

        Git integration (add to your git config):
          git config --global diff.tool collaterale
          git config --global difftool.collaterale.cmd 'collaterale "$LOCAL" "$REMOTE"'
          git config --global merge.tool collaterale
          git config --global mergetool.collaterale.cmd 'collaterale --merge --base "$BASE" --left "$LOCAL" --right "$REMOTE" --output "$MERGED"'
          git config --global mergetool.collaterale.trustExitCode true
        """;
}

public static class LaunchParser
{
    /// <summary>
    /// Text to print for <c>--help</c> / <c>--version</c> (no window is needed for these), or null for everything else.
    /// </summary>
    public static string? Informational(IReadOnlyList<string> args, string productName, string version)
        => Parse(args).Kind switch
        {
            LaunchKind.Help => LaunchRequest.HelpText,
            LaunchKind.Version => $"{productName} {version}",
            _ => null,
        };

    // switch name → (key, how it takes a value)
    private enum Arg { None, Required, Optional }

    private static readonly Dictionary<string, (string Key, Arg Value)> Switches = BuildSwitches();

    private static Dictionary<string, (string, Arg)> BuildSwitches()
    {
        var t = new Dictionary<string, (string, Arg)>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, Arg value, params string[] names) { foreach (var n in names) { t[n] = (key, value); } }
        Add("automerge", Arg.None, "automerge");
        Add("center", Arg.Required, "center");
        Add("closescript", Arg.None, "closescript");
        Add("edit", Arg.None, "edit");
        Add("expandall", Arg.None, "expandall");
        Add("favorleft", Arg.None, "favorleft");
        Add("favorright", Arg.None, "favorright");
        Add("filters", Arg.Required, "filters");
        Add("force", Arg.None, "force");
        Add("fv", Arg.Required, "fv", "fileviewer");
        Add("iu", Arg.None, "iu", "ignoreunimportant");
        Add("mergeoutput", Arg.Required, "mergeoutput");
        Add("nobackups", Arg.None, "nobackups");
        Add("qc", Arg.Optional, "qc", "quickcompare");
        Add("reviewconflicts", Arg.None, "reviewconflicts");
        Add("ro", Arg.None, "ro", "readonly");
        Add("ro1", Arg.None, "ro1", "lro", "leftreadonly");
        Add("ro2", Arg.None, "ro2", "rro", "rightreadonly");
        Add("savetarget", Arg.Required, "savetarget");
        Add("silent", Arg.None, "silent");
        Add("solo", Arg.None, "solo");
        Add("sync", Arg.None, "sync");
        Add("title1", Arg.Required, "title1", "lefttitle");
        Add("title2", Arg.Required, "title2", "righttitle");
        Add("title3", Arg.Required, "title3", "centertitle");
        Add("title4", Arg.Required, "title4", "outputtitle");
        Add("vcs1", Arg.Required, "vcs1", "vcsleft");
        Add("vcs2", Arg.Required, "vcs2", "vcsright");
        Add("vcs3", Arg.Required, "vcs3", "vcscenter");
        Add("vcs4", Arg.Required, "vcs4", "vcsoutput");
        return t;
    }

    private static bool IsHelp(string a) => a is "--help" or "-h" or "-?" or "/?" or "/h" or "/help" or "-help";

    /// <summary>
    /// Separates the switches (written <c>-x</c>, <c>--x</c> or <c>/x</c>, with <c>=value</c> or a following value) from the paths.
    /// A token that names an existing file or folder is always a path, so <c>/tmp/a</c> on Linux is never mistaken for a switch.
    /// </summary>
    private static (LaunchOptions Options, List<string> Remaining, string? Viewer, string? Error) Extract(IReadOnlyList<string> args)
    {
        var found = new Dictionary<string, string?>();
        var rest = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            var prefix = token.StartsWith("--", StringComparison.Ordinal) ? 2 : token.StartsWith('-') || token.StartsWith('/') ? 1 : 0;
            if (prefix == 0 || token.Length == prefix || (token[0] == '/' && (File.Exists(token) || Directory.Exists(token))))
            {
                rest.Add(token);
                continue;
            }

            var body = token[prefix..];
            var eq = body.IndexOf('=');
            var name = eq >= 0 ? body[..eq] : body;
            if (!Switches.TryGetValue(name, out var sw))
            {
                rest.Add(token);
                continue;
            }

            string? value = eq >= 0 ? body[(eq + 1)..] : null;
            if (sw.Value == Arg.Required && value is null)
            {
                if (i + 1 >= args.Count) { return (LaunchOptions.None, rest, null, $"The switch '{token}' needs a value."); }
                value = args[++i];
            }

            found[sw.Key] = value ?? "";
        }

        if (found.Count == 0) { return (LaunchOptions.None, rest, null, null); }

        bool Has(string k) => found.ContainsKey(k);
        string? Val(string k) => found.TryGetValue(k, out var v) ? v : null;
        var ro = Has("ro");
        var options = new LaunchOptions
        {
            AutoMerge = Has("automerge"), Force = Has("force"), ReviewConflicts = Has("reviewconflicts"), IgnoreUnimportant = Has("iu"),
            FavorLeft = Has("favorleft"), FavorRight = Has("favorright"), NoBackups = Has("nobackups"),
            ReadOnlyLeft = ro || Has("ro1"), ReadOnlyRight = ro || Has("ro2"),
            Silent = Has("silent"), Solo = Has("solo"), ExpandAll = Has("expandall"), CloseScript = Has("closescript"),
            Filters = Val("filters"), SaveTarget = Val("savetarget"), QuickCompare = Val("qc"),
            Titles = [Val("title1"), Val("title2"), Val("title3"), Val("title4")],
            Vcs = [Val("vcs1"), Val("vcs2"), Val("vcs3"), Val("vcs4")],
        };
        // the extra values travel with the options through these keys
        rest.Add("\0center=" + (Val("center") ?? ""));
        rest.Add("\0mergeoutput=" + (Val("mergeoutput") ?? ""));
        rest.Add("\0fv=" + (Val("fv") ?? ""));
        rest.Add("\0edit=" + (Has("edit") ? "1" : ""));
        rest.Add("\0sync=" + (Has("sync") ? "1" : ""));
        return (options, rest, Val("fv"), null);
    }

    public static LaunchRequest Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new LaunchRequest(LaunchKind.Home);
        }

        if (args.Any(IsHelp))
        {
            return new LaunchRequest(LaunchKind.Help);
        }

        if (args.Any(a => a is "--version" or "-v"))
        {
            return new LaunchRequest(LaunchKind.Version);
        }

        if (args.Count == 1 && args[0] is "--clipboard" or "-clipboard" or "/clipboard") { return new LaunchRequest(LaunchKind.Clipboard); }

        var (options, rest, _, problem) = Extract(args);
        if (problem is not null) { return Error(problem); }

        string? Take(string key)
        {
            var hit = rest.FirstOrDefault(r => r.StartsWith("\0" + key + "=", StringComparison.Ordinal));
            if (hit is null) { return null; }
            rest.Remove(hit);
            return hit.Length > key.Length + 2 ? hit[(key.Length + 2)..] : null;
        }

        var center = Take("center");
        var mergeOutput = Take("mergeoutput");
        var viewer = Take("fv");
        var edit = Take("edit") is not null;
        var sync = Take("sync") is not null;
        var req = rest.Contains("--merge") ? ParseMerge(rest) : ParseCompare(rest, center, mergeOutput, viewer, edit, sync);
        return req with { Options = options };
    }

    private static readonly Dictionary<string, LaunchKind> Viewers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Text Compare"] = LaunchKind.TextCompare, ["Text Merge"] = LaunchKind.Merge, ["Table Compare"] = LaunchKind.TableCompare,
        ["Hex Compare"] = LaunchKind.HexCompare, ["Media Compare"] = LaunchKind.ImageCompare, ["Picture Compare"] = LaunchKind.ImageCompare,
        ["Folder Compare"] = LaunchKind.FolderCompare, ["Folder Merge"] = LaunchKind.FolderCompare, ["Folder Sync"] = LaunchKind.FolderSync,
        ["Text Edit"] = LaunchKind.TextEdit, ["Text Patch"] = LaunchKind.Patch,
    };

    private static LaunchRequest ParseCompare(IReadOnlyList<string> args, string? center, string? mergeOutput, string? viewer, bool edit, bool sync)
    {
        var hex = args.Contains("--hex");
        var forceText = args.Contains("--text");
        var table = args.Contains("--table");
        var image = args.Contains("--image");
        var paths = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();

        LaunchKind? forced = hex ? LaunchKind.HexCompare : table ? LaunchKind.TableCompare : image ? LaunchKind.ImageCompare : forceText ? LaunchKind.TextCompare : null;
        if (!string.IsNullOrEmpty(viewer))
        {
            if (!Viewers.TryGetValue(viewer, out var fv)) { return Error($"Unknown view '{viewer}'. Use one of: {string.Join(", ", Viewers.Keys)}."); }
            forced = fv;
        }

        if (edit) { forced = LaunchKind.TextEdit; }

        // a pair plus /center (or /mergeoutput) is a merge, like three or four files
        if (paths.Count == 2 && (center is not null || mergeOutput is not null) && forced is null or LaunchKind.Merge)
        {
            paths.Add(center ?? "");
            if (center is null) { paths.RemoveAt(2); }
        }

        if (paths.Count == 1)
        {
            var one = paths[0];
            if (one == "-") { return new LaunchRequest(LaunchKind.Stdin); }
            if (one.StartsWith('@') && one.Length > 1) { return File.Exists(one[1..]) ? new LaunchRequest(LaunchKind.Script, one[1..]) : Error($"The script '{one[1..]}' does not exist."); }
            var exists = File.Exists(one);
            if (exists && forced == LaunchKind.TextEdit) { return new LaunchRequest(LaunchKind.TextEdit, one); }
            if (exists && one.EndsWith(".cpkg", StringComparison.OrdinalIgnoreCase)) { return new LaunchRequest(LaunchKind.Package, one); }
            if (exists && (one.EndsWith(".diff", StringComparison.OrdinalIgnoreCase) || one.EndsWith(".patch", StringComparison.OrdinalIgnoreCase)) || exists && forced == LaunchKind.Patch) { return new LaunchRequest(LaunchKind.Patch, one); }
            if (!exists && !Directory.Exists(one) && forced is null)
            {
                // a plain name is a saved session or workspace; anything that looks like a path is a file that is not there
                return one.Contains(Path.DirectorySeparatorChar) || one.Contains('/') ? Error($"'{one}' does not exist.") : new LaunchRequest(LaunchKind.Named, one);
            }
        }

        if (paths.Count == 0 && forced is not null)
        {
            return new LaunchRequest(forced.Value);
        }

        if (paths.Count is 3 or 4 || paths.Count == 2 && forced == LaunchKind.Merge)
        {
            var (l, r) = (paths[0], paths[1]);
            var b = paths.Count > 2 ? paths[2] : center;
            var o = paths.Count == 4 ? paths[3] : mergeOutput;
            foreach (var (name, p) in new[] { ("left", l), ("right", r) }.Concat(b is null ? [] : [("center", b)]))
            {
                if (!File.Exists(p)) { return Error($"The {name} file '{p}' does not exist."); }
            }

            return new LaunchRequest(LaunchKind.Merge, l, r, b, o);
        }

        if (paths.Count != 2)
        {
            return Error($"Expected two paths to compare, got {paths.Count}. Try --help.");
        }

        var (left, right) = (paths[0], paths[1]);
        bool lf = File.Exists(left), rf = File.Exists(right), ld = Directory.Exists(left), rd = Directory.Exists(right);
        if (lf && rf)
        {
            return new LaunchRequest(forced ?? CompareRouter.ForFiles(left, right), left, right);
        }

        if (ld && rd)
        {
            return new LaunchRequest(sync || forced == LaunchKind.FolderSync ? LaunchKind.FolderSync : LaunchKind.FolderCompare, left, right);
        }

        if (!(lf || ld))
        {
            return Error($"'{left}' does not exist.");
        }

        if (!(rf || rd))
        {
            return Error($"'{right}' does not exist.");
        }

        return Error("Cannot compare a file with a folder.");
    }

    private static LaunchRequest ParseMerge(IReadOnlyList<string> args)
    {
        string? b = null, l = null, r = null, o = null;
        for (var i = 0; i < args.Count; i++)
        {
            string? Next() => i + 1 < args.Count ? args[++i] : null;
            switch (args[i])
            {
                case "--base": b = Next(); break;
                case "--left": l = Next(); break;
                case "--right": r = Next(); break;
                case "--output": o = Next(); break;
                case "--merge": break;
                default: return Error($"Unknown merge option '{args[i]}'. Try --help.");
            }
        }

        if (l is null || r is null || o is null)
        {
            return Error("A merge needs --left, --right and --output (and usually --base).");
        }

        foreach (var (name, p) in new[] { ("left", l), ("right", r) }.Concat(b is null ? [] : [("base", b)]))
        {
            if (!File.Exists(p))
            {
                return Error($"The {name} file '{p}' does not exist.");
            }
        }

        return new LaunchRequest(LaunchKind.Merge, l, r, b, o);
    }

    private static LaunchRequest Error(string message) => new(LaunchKind.Error, Message: message);
}
