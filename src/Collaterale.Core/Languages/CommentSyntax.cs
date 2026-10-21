using Collaterale.Core.Settings;

namespace Collaterale.Core.Languages;

/// <summary>A kind of string literal: where it starts and ends, how a quote inside it is written, and whether it may span lines.</summary>
public sealed record StringRule(string Open, string Close, char Escape = '\\', bool DoubledCloseEscapes = false, bool Multiline = false);

/// <summary>How comments (and string literals, which must not be mistaken for comments) are written in one family of languages.</summary>
public sealed record CommentSyntax(
    string Id,
    string DisplayName,
    string[] Extensions,
    string[] LineStarts,
    (string Open, string Close)[] Blocks,
    StringRule[] Strings,
    bool LineStartNeedsSpaceBefore = false,
    bool LineStartOnly = false)
{
    public override string ToString() => DisplayName;
}

/// <summary>Known comment syntaxes and the lookup from a file name to one of them.</summary>
public static class CommentSyntaxes
{
    private static readonly StringRule Dq = new("\"", "\"");
    private static readonly StringRule Sq = new("'", "'");
    private static readonly StringRule Triple = new("\"\"\"", "\"\"\"", Multiline: true);
    private static readonly StringRule Backtick = new("`", "`", Multiline: true);

    public static readonly CommentSyntax CLike = new("c-like", "C, C++, Java, Go, Rust, Swift, Kotlin…",
        [".c", ".h", ".cc", ".cpp", ".cxx", ".hpp", ".hh", ".java", ".go", ".rs", ".swift", ".kt", ".kts", ".scala", ".dart", ".groovy", ".proto", ".m", ".mm", ".gradle"],
        ["//"], [("/*", "*/")], [Triple, Dq, Sq, Backtick]);

    public static readonly CommentSyntax CSharp = new("csharp", "C#",
        [".cs", ".csx"],
        ["//"], [("/*", "*/")],
        [new StringRule("$@\"", "\"", DoubledCloseEscapes: true, Multiline: true), new StringRule("@\"", "\"", DoubledCloseEscapes: true, Multiline: true), Triple, Dq, Sq]);

    public static readonly CommentSyntax JavaScript = new("javascript", "JavaScript, TypeScript",
        [".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx", ".mts", ".cts", ".vue", ".svelte"],
        ["//"], [("/*", "*/")], [Dq, Sq, Backtick]);

    public static readonly CommentSyntax Jsonc = new("jsonc", "JSON with comments",
        [".jsonc", ".json5"],
        ["//"], [("/*", "*/")], [Dq]);

    public static readonly CommentSyntax Php = new("php", "PHP",
        [".php", ".phtml"],
        ["//", "#"], [("/*", "*/")], [Dq, Sq]);

    public static readonly CommentSyntax Css = new("css", "CSS, SCSS, Less",
        [".css", ".scss", ".less", ".sass"],
        ["//"], [("/*", "*/")], [Dq, Sq]);

    public static readonly CommentSyntax Python = new("python", "Python",
        [".py", ".pyw", ".pyi"],
        ["#"], [], [new StringRule("\"\"\"", "\"\"\"", Multiline: true), new StringRule("'''", "'''", Multiline: true), Dq, Sq]);

    public static readonly CommentSyntax Hash = new("hash", "Shell, Ruby, YAML, TOML, Makefile, Perl…",
        [".sh", ".bash", ".zsh", ".fish", ".rb", ".pl", ".pm", ".yml", ".yaml", ".toml", ".mk", ".cmake", ".r", ".ps1", ".psm1", ".conf", ".gitignore", ".dockerignore", ".env", ".tf"],
        ["#"], [], [Dq, Sq], LineStartNeedsSpaceBefore: true);

    public static readonly CommentSyntax Markup = new("markup", "HTML, XML, SVG, XAML",
        [".html", ".htm", ".xhtml", ".xml", ".svg", ".xaml", ".axaml", ".csproj", ".vbproj", ".fsproj", ".props", ".targets", ".config", ".resx", ".plist", ".xsl", ".xslt", ".xsd", ".nuspec", ".md"],
        [], [("<!--", "-->")], []);

    public static readonly CommentSyntax Sql = new("sql", "SQL",
        [".sql", ".ddl", ".psql"],
        ["--"], [("/*", "*/")], [new StringRule("'", "'", Escape: '\0', DoubledCloseEscapes: true), Dq]);

    public static readonly CommentSyntax Lua = new("lua", "Lua",
        [".lua"],
        ["--"], [("--[[", "]]")], [Dq, Sq]);

    public static readonly CommentSyntax Ini = new("ini", "INI, properties",
        [".ini", ".cfg", ".properties", ".desktop"],
        [";", "#"], [], [], LineStartOnly: true);

    public static readonly CommentSyntax Haskell = new("haskell", "Haskell",
        [".hs", ".lhs"],
        ["--"], [("{-", "-}")], [Dq]);

    public static readonly IReadOnlyList<CommentSyntax> BuiltIn = [CLike, CSharp, JavaScript, Jsonc, Php, Css, Python, Hash, Markup, Sql, Lua, Ini, Haskell];

    private static IReadOnlyList<CommentSyntax> _custom = [];

    /// <summary>The built-in syntaxes followed by the ones the user described.</summary>
    public static IReadOnlyList<CommentSyntax> All => [.. BuiltIn, .. _custom];

    /// <summary>The syntaxes the user described (see <see cref="ConfigureCustom"/>).</summary>
    public static IReadOnlyList<CommentSyntax> Custom => _custom;

    /// <summary>Makes the user's file formats known; unusable ones (no name, no extension, nothing to recognise) are left out.</summary>
    public static void ConfigureCustom(IEnumerable<FileFormat> formats)
        => _custom = [.. formats.Select(FromFormat).OfType<CommentSyntax>()];

    private static string[] Items(string text, params char[] extra)
        => [.. text.Split([' ', '\t', '\n', '\r', .. extra], StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>The syntax a user-described format stands for, or null when it has no name, no extension or nothing that could be recognised.</summary>
    public static CommentSyntax? FromFormat(FileFormat f)
    {
        var extensions = Items(f.Extensions, ';', ',').Select(e => e.StartsWith('.') ? e : "." + e).ToArray();
        var lines = Items(f.LineComments);
        var words = Items(f.BlockComments);
        var blocks = Enumerable.Range(0, words.Length / 2).Select(k => (words[2 * k], words[2 * k + 1])).ToArray();   // opener, closer, opener, closer…
        var strings = Items(f.Strings).Select(q => new StringRule(q, q)).ToArray();
        if (string.IsNullOrWhiteSpace(f.Name) || extensions.Length == 0 || lines.Length + blocks.Length + strings.Length == 0) { return null; }
        return new CommentSyntax("custom:" + f.Name.Trim(), f.Name.Trim(), extensions, lines, blocks, strings);
    }

    /// <summary>The syntax for a file name by its extension (or by the whole name for dot-files such as <c>.gitignore</c>); null when unknown.</summary>
    public static CommentSyntax? ForPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) { return null; }
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(name);
        if (ext.Length == 0 && name.StartsWith('.')) { ext = name; }
        if (ext.Length == 0)
        {
            return name.ToLowerInvariant() switch { "makefile" or "dockerfile" or "gemfile" or "rakefile" or "cmakelists.txt" => Hash, _ => null };
        }

        // the user's own definitions come first, so they can also replace a built-in one for an extension
        return _custom.Concat(BuiltIn).FirstOrDefault(s => s.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
    }

    public static CommentSyntax? ById(string? id) => All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
}
