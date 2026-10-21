using Collaterale.Core.Diff;
using Collaterale.Core.Folders;
using Collaterale.Core.Merge;
using Collaterale.Core.Text;

namespace Collaterale.Core.Cli;

/// <summary>Exit codes of command-line runs that finish without a window.</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int BinarySame = 1;
    public const int RulesSame = 2;
    public const int BinaryDifferent = 11;
    public const int Similar = 12;
    public const int RulesDifferent = 13;
    public const int ConflictsDetected = 14;
    public const int Unknown = 100;
    public const int ConflictsNotWritten = 101;
    public const int ScriptLoadError = 105;
    public const int ScriptSyntaxError = 106;
    public const int ScriptFailed = 107;
}

/// <summary>Commands that need no window: <c>/qc</c> quick comparisons and <c>/automerge</c>.</summary>
public static class CliHeadless
{
    /// <summary>Runs the request without a window when it is one of those commands; false means "open the app".</summary>
    public static bool TryRun(LaunchRequest req, TextWriter output, TextWriter error, out int code, Collaterale.Core.Settings.AppSettings? settings = null)
    {
        code = 0;
        if (req.Kind == LaunchKind.Script && req.Left is not null)
        {
            code = Collaterale.Core.Scripting.ScriptEngine.RunNamed(req.Left, output, settings?.Prefs.Scripts ?? new Collaterale.Core.Settings.ScriptPreferences());
            return true;
        }

        if (req.Kind == LaunchKind.Package && req.Left is not null && req.Options.Silent && settings is not null)
        {
            code = ImportPackage(req.Left, output, error, settings);
            return true;
        }

        if (req.Options.QuickCompare is not null && req.Kind is not (LaunchKind.Error or LaunchKind.Help or LaunchKind.Version))
        {
            code = QuickCompare(req, output, error);
            return true;
        }

        if (req.Options.AutoMerge && req.Kind == LaunchKind.Merge)
        {
            return AutoMerge(req, output, error, out code);
        }

        return false;
    }

    private static int ImportPackage(string path, TextWriter output, TextWriter error, Collaterale.Core.Settings.AppSettings settings)
    {
        try
        {
            output.WriteLine(Collaterale.Core.Settings.SettingsPackage.Import(path, settings));
            new Collaterale.Core.Settings.SettingsStore(Collaterale.Core.Settings.SettingsStore.DefaultPath).Save(settings);
            return ExitCodes.Success;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine(ex.Message);
            return ExitCodes.Unknown;
        }
    }

    /// <summary>The quick compare dialog's sentence for an exit code, and whether the files count as the same.</summary>
    public static (string Text, bool Same) Describe(int code) => code switch
    {
        ExitCodes.BinarySame => ("The files are identical, byte for byte.", true),
        ExitCodes.RulesSame => ("The files are identical.", true),
        ExitCodes.Similar => ("The files are similar: they differ only in ways the comparison rules ignore (for example line endings).", true),
        ExitCodes.BinaryDifferent => ("The files are different.", false),
        ExitCodes.RulesDifferent => ("The files are different: the text rules found differences.", false),
        _ => ("The files could not be compared.", false),
    };

    /// <summary>Whether the startup quick-compare dialog should be shown for a comparison with this result: only for matching files when "open the view if different" is on.</summary>
    public static bool ShowQuickDialog(int code, bool openViewIfDifferent) => !openViewIfDifferent || Describe(code).Same;

    /// <summary>Like <see cref="TryRun"/> but always produces a code (an unknown command is error 100).</summary>
    public static int Run(LaunchRequest req, TextWriter output, TextWriter error)
        => TryRun(req, output, error, out var code) ? code : ExitCodes.Unknown;

    // ───────────── quick compare ─────────────

    private static int QuickCompare(LaunchRequest req, TextWriter output, TextWriter error)
    {
        var kind = req.Options.QuickCompare!.ToLowerInvariant();
        if (kind is not ("" or "size" or "crc" or "sha1" or "sha256" or "binary"))
        {
            error.WriteLine($"Unknown quick compare type '{kind}'; use size, crc, sha1, sha256 or binary.");
            return ExitCodes.Unknown;
        }

        if (req.Left is null || req.Right is null || !File.Exists(req.Left) || !File.Exists(req.Right))
        {
            error.WriteLine("A quick compare needs two files.");
            return ExitCodes.Unknown;
        }

        try
        {
            switch (kind)
            {
                case "size":
                    return new FileInfo(req.Left).Length == new FileInfo(req.Right).Length ? ExitCodes.BinarySame : ExitCodes.BinaryDifferent;
                case "crc":
                    return Crc32.OfFile(req.Left) == Crc32.OfFile(req.Right) && new FileInfo(req.Left).Length == new FileInfo(req.Right).Length ? ExitCodes.BinarySame : ExitCodes.BinaryDifferent;
                case "sha1":
                    return FileHash.SameHash(req.Left, req.Right, System.Security.Cryptography.HashAlgorithmName.SHA1) ? ExitCodes.BinarySame : ExitCodes.BinaryDifferent;
                case "sha256":
                    return FileHash.SameHash(req.Left, req.Right, System.Security.Cryptography.HashAlgorithmName.SHA256) ? ExitCodes.BinarySame : ExitCodes.BinaryDifferent;
                case "binary":
                    return SameBytes(req.Left, req.Right) ? ExitCodes.BinarySame : ExitCodes.BinaryDifferent;
                default:
                    if (SameBytes(req.Left, req.Right)) { return ExitCodes.RulesSame; }
                    var (a, b) = (TextFile.Load(req.Left), TextFile.Load(req.Right));
                    if (a.IsBinary || b.IsBinary) { return ExitCodes.RulesDifferent; }
                    return TextComparison.Compare(a.Lines, b.Lines).AreEqual ? ExitCodes.Similar : ExitCodes.RulesDifferent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine(ex.Message);
            return ExitCodes.Unknown;
        }
    }

    private static bool SameBytes(string a, string b, int buffer = 81920)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) { return false; }
        using var x = File.OpenRead(a);
        using var y = File.OpenRead(b);
        var size = Math.Clamp(buffer, 4096, 16 * 1024 * 1024);
        var (bx, by) = (new byte[size], new byte[size]);
        int n;
        while ((n = x.Read(bx, 0, bx.Length)) > 0)
        {
            var got = 0;
            while (got < n)
            {
                var m = y.Read(by, got, n - got);
                if (m == 0) { return false; }
                got += m;
            }

            if (!bx.AsSpan(0, n).SequenceEqual(by.AsSpan(0, n))) { return false; }
        }

        return true;
    }

    // ───────────── automatic merge ─────────────

    private static bool AutoMerge(LaunchRequest req, TextWriter output, TextWriter error, out int code)
    {
        code = ExitCodes.Unknown;
        var o = req.Options;
        if (req.Output is null)
        {
            error.WriteLine("/automerge needs an output file: give it as the fourth file or with /mergeoutput.");
            return true;
        }

        try
        {
            var (l, r) = (TextFile.Load(req.Left!), TextFile.Load(req.Right!));
            var b = req.Base is null ? TextContent.Empty : TextFile.Load(req.Base);
            if (l.IsBinary || r.IsBinary || b.IsBinary)
            {
                error.WriteLine("Only text files can be merged.");
                return true;
            }

            var options = o.IgnoreUnimportant ? DiffOptions.Default with { Whitespace = WhitespaceMode.IgnoreAll } : DiffOptions.Default;
            var merge = ThreeWayMerge.Merge(b.Lines, l.Lines, r.Lines, options);
            var favored = o.FavorLeft ? Resolution.Left : o.FavorRight ? Resolution.Right : (Resolution?)null;
            if (merge.HasConflicts && favored is null && o.ReviewConflicts)
            {
                return false;   // let a Text Merge window settle the conflicts
            }

            var lines = merge.Build((_, region) => region.Kind switch
            {
                MergeRegionKind.Conflict => favored,
                MergeRegionKind.Stable when o.IgnoreUnimportant && o.FavorRight => Resolution.Right,
                _ => null,
            }, markers: merge.HasConflicts && favored is null && o.Force, leftName: req.Left!, rightName: req.Right!);

            if (merge.HasConflicts && favored is null && !o.Force)
            {
                output.WriteLine($"{merge.ConflictCount} conflict(s); the output was not written.");
                code = ExitCodes.ConflictsNotWritten;
                return true;
            }

            if (File.Exists(req.Output) && !o.NoBackups) { File.Copy(req.Output, req.Output + ".bak", overwrite: true); }
            File.WriteAllBytes(req.Output, TextFile.Encode(lines, l.Eol, l.EndsWithNewline, l.Encoding, l.HasBom));
            code = merge.HasConflicts && favored is null ? ExitCodes.ConflictsDetected : ExitCodes.Success;
            if (code == ExitCodes.ConflictsDetected) { output.WriteLine($"{merge.ConflictCount} conflict(s) were written with markers."); }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine(ex.Message);
            code = ExitCodes.Unknown;
            return true;
        }
    }
}
