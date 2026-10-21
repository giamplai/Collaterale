using Collaterale.Core.Diff;
using Collaterale.Core.Folders;
using Collaterale.Core.Languages;
using Collaterale.Core.Text;

namespace Collaterale.Core.Cli;

/// <summary>Headless <c>collaterale --diff left right</c>: prints a unified diff (files) or a summary (folders) and returns a diff-style exit code.</summary>
public static class CliDiff
{
    public const int Same = 0;
    public const int Different = 1;
    public const int Trouble = 2;

    public static bool Wants(IReadOnlyList<string> args) => args.Contains("--diff");

    public static string Usage => """
        Usage: collaterale --diff <left> <right> [options]

          -U N, --context N       lines of context around changes (default 3)
          -b                      ignore changes in the amount of whitespace
          -w                      ignore all whitespace
          -B                      ignore leading and trailing whitespace
          -i                      ignore case
          --ignore-comments       ignore comments (the language comes from the file name, or --lang)
          --ignore-strings        ignore the text inside string literals (the language comes from the file name, or --lang)
          --lang ID               comment syntax: c-like, csharp, javascript, python, hash, markup, sql, lua, ini, php, css, haskell, jsonc
          --ignore-timestamps     ignore dates and times
          --ignore-blank-lines    added or removed empty lines are not differences
          --structure             JSON and XML: compare the structure (formatting and key/attribute order are ignored)
          --patience              use the patience algorithm
          --histogram             use the histogram algorithm
          --rows                  print the side-by-side alignment, one line per row
          -q, --brief             only say whether files differ
          --content               (folders) compare file contents instead of size and date

        Exit codes: 0 = no differences, 1 = differences, 2 = an error.
        """;

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        var context = 3;
        var brief = false;
        var rows = false;
        var content = false;
        var options = new DiffOptions { DetectMoves = false };
        var smart = new Smart();
        var paths = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--diff": break;
                case "-U" or "--context":
                    if (i + 1 >= args.Count || !int.TryParse(args[++i], out context) || context < 0) { return Fail(error, "-U needs a non-negative number of lines."); }
                    break;
                case "-b": options = options with { Whitespace = WhitespaceMode.IgnoreAmount }; break;
                case "-w": options = options with { Whitespace = WhitespaceMode.IgnoreAll }; break;
                case "-B": options = options with { Whitespace = WhitespaceMode.IgnoreLeadingTrailing }; break;
                case "-i": options = options with { IgnoreCase = true }; break;
                case "--ignore-comments": smart.Comments = true; break;
                case "--ignore-strings": smart.Strings = true; break;
                case "--ignore-blank-lines": options = options with { IgnoreBlankLines = true }; break;
                case "--ignore-timestamps": options = options with { IgnoreTimestamps = true }; break;
                case "--structure": smart.Structure = true; break;
                case "--lang":
                    if (i + 1 >= args.Count || CommentSyntaxes.ById(args[++i]) is not { } chosen) { return Fail(error, "--lang needs one of: " + string.Join(", ", CommentSyntaxes.All.Select(x => x.Id)) + "."); }
                    smart.Syntax = chosen;
                    smart.Comments = true;
                    break;
                case "--patience": options = options with { Algorithm = DiffAlgorithm.Patience }; break;
                case "--histogram": options = options with { Algorithm = DiffAlgorithm.Histogram }; break;
                case "-q" or "--brief": brief = true; break;
                case "--content": content = true; break;
                case "--rows": rows = true; options = options with { DetectMoves = true }; break;
                case "-h" or "--help": output.WriteLine(Usage); return Same;
                default:
                    if (args[i].StartsWith('-') && args[i].Length > 1) { return Fail(error, $"Unknown option '{args[i]}'. Try --help."); }
                    paths.Add(args[i]);
                    break;
            }
        }

        if (paths.Count != 2)
        {
            return Fail(error, "Expected exactly two paths to compare. Try --help.");
        }

        try
        {
            var (l, r) = (paths[0], paths[1]);
            if (Directory.Exists(l) && Directory.Exists(r))
            {
                return DiffFolders(l, r, content, output);
            }

            if (File.Exists(l) && File.Exists(r))
            {
                return rows ? RowsFiles(l, r, options, smart, error, output) : DiffFiles(l, r, context, options, smart, error, brief, output);
            }

            return Fail(error, !(File.Exists(l) || Directory.Exists(l)) ? $"'{l}' does not exist." : !(File.Exists(r) || Directory.Exists(r)) ? $"'{r}' does not exist." : "Cannot compare a file with a folder.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(error, ex.Message);
        }
    }

    /// <summary>The "smart" options of one run: comment syntax and structural comparison need the file names, so they are resolved per pair.</summary>
    private sealed class Smart
    {
        public bool Comments { get; set; }
        public bool Strings { get; set; }
        public bool Structure { get; set; }
        public CommentSyntax? Syntax { get; set; }

        /// <summary>True when the last <see cref="Apply"/> compared normalized text, where a missing final newline is formatting, not a change.</summary>
        public bool Normalized { get; private set; }

        public (IReadOnlyList<string> Left, IReadOnlyList<string> Right, DiffOptions Options) Apply(string leftPath, string rightPath, IReadOnlyList<string> left, IReadOnlyList<string> right, DiffOptions options, TextWriter error)
        {
            if (Structure)
            {
                var format = StructuralNormalizer.FormatFor(leftPath) is var f && f != StructuredFormat.None ? f : StructuralNormalizer.FormatFor(rightPath);
                var nl = format == StructuredFormat.None ? null : StructuralNormalizer.Normalize(string.Join('\n', left), format);
                var nr = format == StructuredFormat.None ? null : StructuralNormalizer.Normalize(string.Join('\n', right), format);
                if (nl is not null && nr is not null) { (left, right) = (nl, nr); Normalized = true; }
                else { error.WriteLine(format == StructuredFormat.None ? "--structure: not a JSON or XML file, comparing as text." : "--structure: a file could not be parsed, comparing as text."); }
            }

            if (Comments || Strings)
            {
                var syntax = Syntax ?? CommentSyntaxes.ForPath(leftPath) ?? CommentSyntaxes.ForPath(rightPath);
                if (syntax is null) { error.WriteLine($"{(Comments ? "--ignore-comments" : "--ignore-strings")}: the language is unknown for this file name; use --lang."); }
                else { options = options with { Language = syntax, Comments = Comments ? syntax : null, IgnoreStrings = Strings }; }
            }

            return (left, right, options);
        }
    }

    private static int DiffFiles(string left, string right, int context, DiffOptions options, Smart smart, TextWriter error, bool brief, TextWriter output)
    {
        var a = TextFile.Load(left);
        var b = TextFile.Load(right);
        if (a.IsBinary || b.IsBinary)
        {
            var same = File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));
            if (!same) { output.WriteLine($"Binary files {left} and {right} differ"); }
            return same ? Same : Different;
        }

        var (la, lb, o) = smart.Apply(left, right, a.Lines, b.Lines, options, error);
        var patch = UnifiedDiff.Write(left, right, la, lb, context, o, a.EndsWithNewline || smart.Normalized, b.EndsWithNewline || smart.Normalized);
        if (patch.Length == 0)
        {
            return Same;
        }

        if (brief) { output.WriteLine($"Files {left} and {right} differ"); }
        else { output.Write(patch); }
        return Different;
    }

    /// <summary>
    /// The alignment as text, one row per line: <c>kind  left-no  right-no | text</c>, where kind is
    /// <c>=</c> same, <c>~</c> same under the ignore options, <c>*</c> changed, <c>-</c> left only, <c>+</c> right only;
    /// a moved block is shown as <c>m-</c> / <c>m+</c>. Meant for comparing alignment decisions between tools and for regression tests.
    /// </summary>
    private static int RowsFiles(string left, string right, DiffOptions options, Smart smart, TextWriter error, TextWriter output)
    {
        var a = TextFile.Load(left);
        var b = TextFile.Load(right);
        if (a.IsBinary || b.IsBinary)
        {
            output.WriteLine("binary");
            return File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right)) ? Same : Different;
        }

        var (la, lb, o) = smart.Apply(left, right, a.Lines, b.Lines, options, error);
        var cmp = TextComparison.Compare(la, lb, o);
        foreach (var row in cmp.Rows)
        {
            var kind = row.Kind switch
            {
                RowKind.Same => "=",
                RowKind.Unimportant => "~",
                RowKind.Changed => "*",
                RowKind.LeftOnly => row.IsMoved ? "m-" : "-",
                _ => row.IsMoved ? "m+" : "+",
            };
            var text = row.Left >= 0 ? a.Lines[row.Left] : b.Lines[row.Right];
            var l = row.Left >= 0 ? (row.Left + 1).ToString() : "·";
            var r = row.Right >= 0 ? (row.Right + 1).ToString() : "·";
            output.WriteLine($"{kind,-2} {l,4} {r,4} | {text}");
            if (row.Kind == RowKind.Changed)
            {
                output.WriteLine($"{"",-2} {"",4} {"",4} | {b.Lines[row.Right]}");   // the right-hand version of a changed row
            }
        }

        return cmp.AreEqual ? Same : Different;
    }

    private static int DiffFolders(string left, string right, bool content, TextWriter output)
    {
        var cmp = FolderComparer.Compare(left, right, new FolderOptions { Criteria = content ? CompareCriteria.Content : CompareCriteria.SizeAndTime, ExcludeFolders = ".git;.svn;.hg" });
        var any = false;
        foreach (var n in cmp.Root.Descendants().Where(n => !n.IsDirectory || n.State is FolderEntryState.LeftOnly or FolderEntryState.RightOnly).OrderBy(n => n.RelativePath, StringComparer.Ordinal))
        {
            switch (n.State)
            {
                case FolderEntryState.LeftOnly when ParentIsAlsoOneSided(cmp, n) == false:
                    output.WriteLine($"Only in {Parent(left, n)}: {n.Name}"); any = true; break;
                case FolderEntryState.RightOnly when ParentIsAlsoOneSided(cmp, n) == false:
                    output.WriteLine($"Only in {Parent(right, n)}: {n.Name}"); any = true; break;
                case FolderEntryState.Different:
                    output.WriteLine($"Files {cmp.LeftPath(n)} and {cmp.RightPath(n)} differ"); any = true; break;
                case FolderEntryState.TypeMismatch:
                    output.WriteLine($"File types differ: {cmp.LeftPath(n)} and {cmp.RightPath(n)}"); any = true; break;
            }
        }

        return any ? Different : Same;
    }

    private static string Parent(string root, FolderNode n)
    {
        var i = n.RelativePath.LastIndexOf('/');
        return i < 0 ? root : Path.Combine(root, n.RelativePath[..i].Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Entries inside a folder that is itself only on one side are not listed separately (like <c>diff -r</c>).</summary>
    private static bool ParentIsAlsoOneSided(FolderComparison c, FolderNode n)
    {
        var i = n.RelativePath.LastIndexOf('/');
        if (i < 0) { return false; }
        var parentPath = n.RelativePath[..i];
        var parent = c.Root.Descendants().FirstOrDefault(d => d.RelativePath == parentPath);
        return parent is not null && parent.State == n.State;
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine("collaterale: " + message);
        return Trouble;
    }
}
