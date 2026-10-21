namespace Collaterale.Core.Folders;

public enum FolderEntryState
{
    Same,
    Different,
    LeftOnly,
    RightOnly,

    /// <summary>One side is a file and the other a folder with the same name.</summary>
    TypeMismatch,

    /// <summary>The entry could not be read (permissions, vanished during the scan).</summary>
    Error,
}

public enum CompareCriteria
{
    /// <summary>Fast: equal when size and modification time match (within the tolerance).</summary>
    SizeAndTime,

    /// <summary>Equal when sizes match.</summary>
    SizeOnly,

    /// <summary>Exact: equal when the contents are identical (sizes are compared first, so most mismatches are instant). Timestamps are not looked at.</summary>
    Content,

    /// <summary>
    /// The usual combination: files with the same size and time are the same without being read; the others are read, and if their contents
    /// match they are still reported as the same (see <see cref="FolderOptions.OverrideQuickResults"/>).
    /// </summary>
    QuickThenContent,
}

/// <summary>How file contents are compared when a criterion needs them.</summary>
public enum ContentMode
{
    /// <summary>Byte by byte; stops at the first difference.</summary>
    Binary,

    /// <summary>Whole files are summed with CRC-32 and the sums compared.</summary>
    Crc,

    /// <summary>Whole files are hashed with SHA-1 and the hashes compared (a collision-resistant check, useful to verify copies and backups).</summary>
    Sha1,

    /// <summary>Whole files are hashed with SHA-256 and the hashes compared (the strongest check; slower than CRC-32).</summary>
    Sha256,

    /// <summary>
    /// As text, with <see cref="ContentRules"/>: line endings and encoding never matter, and optionally whitespace, case, comments,
    /// dates and blank lines are ignored. A file that is not text is compared byte by byte.
    /// </summary>
    Rules,
}

/// <summary>What a text-aware content comparison treats as unimportant (see <see cref="ContentMode.Rules"/>).</summary>
public sealed record ContentRules
{
    public Collaterale.Core.Diff.WhitespaceMode Whitespace { get; init; } = Collaterale.Core.Diff.WhitespaceMode.Exact;
    public bool IgnoreCase { get; init; }
    public bool IgnoreComments { get; init; }
    public bool IgnoreTimestamps { get; init; }
    public bool IgnoreBlankLines { get; init; }

    public Collaterale.Core.Diff.DiffOptions ToDiffOptionsPublic(string path) => ToDiffOptions(path);

    internal Collaterale.Core.Diff.DiffOptions ToDiffOptions(string path) => new()
    {
        Whitespace = Whitespace,
        IgnoreCase = IgnoreCase,
        IgnoreTimestamps = IgnoreTimestamps,
        IgnoreBlankLines = IgnoreBlankLines,
        Comments = IgnoreComments ? Collaterale.Core.Languages.CommentSyntaxes.ForPath(path) : null,
        DetectMoves = false,
    };
}

/// <summary>Whether archive files (zip, tar…) are looked into during a folder comparison.</summary>
public enum ArchiveHandling
{
    /// <summary>Archives are ordinary files.</summary>
    AsFiles,

    /// <summary>Archives are ordinary files until you open one; it then becomes a folder that is compared by its contents.</summary>
    AsFoldersOnceOpened,

    /// <summary>Archives are folders from the start (slower: every archive is unpacked).</summary>
    AsFoldersAlways,
}

/// <summary>Why a file counts as different (a file can differ in several ways at once).</summary>
[Flags]
public enum DifferenceKinds
{
    None = 0,
    Size = 1,
    Time = 2,
    Content = 4,
    Attributes = 8,
    Permissions = 16,
    NameCase = 32,
    Version = 64,
}

/// <summary>Which side holds the newer copy of a file that differs.</summary>
public enum Newer { None, Left, Right }

public enum OtherFilterKind
{
    /// <summary>Files smaller than <see cref="OtherFilter.Number"/> bytes.</summary>
    SmallerThan,

    /// <summary>Files larger than <see cref="OtherFilter.Number"/> bytes.</summary>
    LargerThan,

    /// <summary>Files last modified before <see cref="OtherFilter.Date"/>.</summary>
    ModifiedBefore,

    /// <summary>Files last modified after <see cref="OtherFilter.Date"/>.</summary>
    ModifiedAfter,

    /// <summary>Files not modified within the last <see cref="OtherFilter.Number"/> days.</summary>
    OlderThanDays,

    /// <summary>Files modified within the last <see cref="OtherFilter.Number"/> days.</summary>
    NewerThanDays,

    /// <summary>Files that have any of the given <see cref="OtherFilter.Attributes"/> (hidden, read-only…).</summary>
    HasAttribute,
}

/// <summary>
/// A rule that hides files by something other than their name. A file matching <b>any</b> rule is hidden. If a rule hides a file on one
/// side but not its counterpart on the other, both stay visible so the pair is not mistaken for an orphan, and the hidden one is marked.
/// </summary>
public sealed record OtherFilter(OtherFilterKind Kind, long Number = 0, DateTime? Date = null, FileAttributes Attributes = 0)
{
    internal bool Matches(FileInfo f, DateTime nowUtc)
    {
        var modified = f.LastWriteTimeUtc;
        return Kind switch
        {
            OtherFilterKind.SmallerThan => f.Length < Number,
            OtherFilterKind.LargerThan => f.Length > Number,
            OtherFilterKind.ModifiedBefore => Date is { } d && modified < d.ToUniversalTime(),
            OtherFilterKind.ModifiedAfter => Date is { } d2 && modified > d2.ToUniversalTime(),
            OtherFilterKind.OlderThanDays => modified < nowUtc.AddDays(-Number),
            OtherFilterKind.NewerThanDays => modified >= nowUtc.AddDays(-Number),
            OtherFilterKind.HasAttribute => (f.Attributes & Attributes) != 0,
            _ => false,
        };
    }
}

/// <summary>A saved set of name filters.</summary>
public sealed record FilterPreset(string Name, string Include = "", string ExcludeFiles = "", string IncludeFolders = "", string ExcludeFolders = "");

/// <summary>Forces two entries to be compared with each other although their names do not match.</summary>
public sealed record AlignmentOverride(string LeftPath, string RightPath);

public sealed record FileSnapshot(long Size, DateTime ModifiedUtc, bool IsSymlink, FileAttributes Attributes = 0, UnixFileMode? Mode = null);

public sealed record FolderOptions
{
    public CompareCriteria Criteria { get; init; } = CompareCriteria.SizeAndTime;

    /// <summary>Times closer than this count as equal (FAT stores 2-second resolution; copies across file systems round).</summary>
    public TimeSpan TimeTolerance { get; init; } = TimeSpan.FromSeconds(2);

    public bool Recursive { get; init; } = true;

    /// <summary>Compare names ignoring case. Default follows the platform (case-sensitive only on Linux).</summary>
    public bool IgnoreNameCase { get; init; } = !OperatingSystem.IsLinux();

    /// <summary>File masks to include, separated by ';' (e.g. "*.cs;*.md"). Empty = everything.</summary>
    public string Include { get; init; } = "";

    /// <summary>File masks to exclude, separated by ';' (e.g. "*.tmp;*.log").</summary>
    public string ExcludeFiles { get; init; } = "";

    /// <summary>Folder names or masks to keep, separated by ';'. When set, only folders matching one of them are entered (at any depth). Empty = every folder.</summary>
    public string IncludeFolders { get; init; } = "";

    /// <summary>Folder names or masks to skip entirely (e.g. ".git;node_modules;bin;obj").</summary>
    public string ExcludeFolders { get; init; } = "";

    /// <summary>Drop folders that end up with no visible entries after filtering.</summary>
    public bool HideEmptyFolders { get; init; } = true;

    // ── quick tests (need only the folder listing) ──

    /// <summary>A difference of exactly one hour (either way) is ignored: file systems store local time differently across daylight saving changes.</summary>
    public bool IgnoreDaylightSaving { get; init; }

    /// <summary>Differences that are a whole number of hours are ignored (copies made across time zones).</summary>
    public bool IgnoreTimeZone { get; init; }

    /// <summary>Files whose names differ only in capitalization are different (only meaningful when names are otherwise compared ignoring case).</summary>
    public bool CompareNameCase { get; init; }

    /// <summary>File attributes that must match (read-only, hidden, system, archive…); zero compares none. Copying a file changes its archive bit, so this is off by default.</summary>
    public FileAttributes CompareAttributes { get; init; }

    /// <summary>Unix permissions (user/group/other read, write, execute, sticky…) must match. Ignored on Windows.</summary>
    public bool ComparePermissions { get; init; }

    // ── tests that read files ──

    public ContentMode ContentMode { get; init; } = ContentMode.Binary;

    public ContentRules Rules { get; init; } = new();

    /// <summary>With <see cref="CompareCriteria.QuickThenContent"/>: files that pass the quick tests are not read at all.</summary>
    public bool SkipContentIfQuickSame { get; init; } = true;

    /// <summary>With <see cref="CompareCriteria.QuickThenContent"/>: files whose contents match are the same even if the quick tests (time, attributes…) said otherwise.</summary>
    public bool OverrideQuickResults { get; init; } = true;

    // ── other filters ──

    /// <summary>Files hidden by something other than their name (size, date, attributes).</summary>
    public IReadOnlyList<OtherFilter> OtherFilters { get; init; } = [];

    /// <summary>Hide operating-system files nobody compares on purpose: hidden+system files on Windows, <c>.DS_Store</c> and <c>._*</c> on macOS, <c>Thumbs.db</c> and <c>desktop.ini</c>.</summary>
    public bool ExcludeProtectedFiles { get; init; } = true;

    // ── handling ──

    public ArchiveHandling Archives { get; init; } = ArchiveHandling.AsFiles;

    /// <summary>Show symbolic links as their targets (files or folders, with the target's size and date). Off: a link is one entry, equal to another link with the same target.</summary>
    public bool FollowSymbolicLinks { get; init; }

    /// <summary>Files that carry version information (programs and libraries) differ when their versions differ. Works for .NET assemblies everywhere and for any executable on Windows.</summary>
    public bool CompareVersions { get; init; }

    // ── alignment ──

    /// <summary>Line up files whose names are the same except for the extension (<c>report.doc</c> with <c>report.pdf</c>) when each has exactly one candidate.</summary>
    public bool AlignDifferentExtensions { get; init; }

    /// <summary>Line up names that are the same text in different Unicode forms (a macOS name <c>é</c> as <c>e</c>+accent with a Linux or Windows <c>é</c>).</summary>
    public bool AlignUnicodeNormalization { get; init; }

    /// <summary>Pairs chosen by hand: the entry at the first path (left) is lined up with the entry at the second path (right). Both must be in the same folder.</summary>
    public IReadOnlyList<AlignmentOverride> Alignments { get; init; } = [];

    /// <summary>Recognise files that exist only on one side under different names but have identical content (renames and moves).</summary>
    public bool DetectRenames { get; init; } = true;
}

/// <summary>One file or folder in the result tree.</summary>
public sealed class FolderNode(string name, string relativePath, bool isDirectory)
{
    public string Name { get; } = name;

    /// <summary>Path relative to the compared roots, always with '/' separators.</summary>
    public string RelativePath { get; } = relativePath;

    public bool IsDirectory { get; private set; } = isDirectory;

    /// <summary>An archive file (zip, tar…): a file row, or a folder row once it is being looked into.</summary>
    public bool IsArchive { get; set; }

    /// <summary>True for everything inside an archive. Those entries are temporary copies: they are compared but never synchronized.</summary>
    public bool InArchive { get; set; }

    /// <summary>For entries inside an archive: the folder the archive was unpacked into on each side (paths are relative to it).</summary>
    public string? LeftRoot { get; set; }

    public string? RightRoot { get; set; }

    internal void BecomeFolder() => IsDirectory = true;

    private string? _leftRelativePath;
    private string? _rightRelativePath;

    /// <summary>Where this entry is (or, if it is missing there, would be) on the left, relative to the left root. Differs from <see cref="RelativePath"/> when names were aligned across a case, extension or normalization difference.</summary>
    public string LeftRelativePath { get => _leftRelativePath ?? RelativePath; set => _leftRelativePath = value; }

    /// <summary>The same for the right side.</summary>
    public string RightRelativePath { get => _rightRelativePath ?? RelativePath; set => _rightRelativePath = value; }

    /// <summary>The right-hand name when it differs from <see cref="Name"/> (aligned despite another extension, case or Unicode form); otherwise null.</summary>
    public string? RightName { get; set; }

    public FolderEntryState State { get; set; }
    public FileSnapshot? Left { get; set; }
    public FileSnapshot? Right { get; set; }
    public Newer Newer { get; set; }
    public string? Error { get; set; }

    /// <summary>The side whose file was hidden by an <see cref="OtherFilter"/> but is shown because its counterpart is not hidden; None otherwise.</summary>
    public Newer FilteredSide { get; set; }

    /// <summary>For folders: the total size of the files below it on each side (the files that are shown, after filtering).</summary>
    public long LeftBytes { get; set; }

    public long RightBytes { get; set; }

    /// <summary>For folders: something below differs between the sides / exists on one side only. Lets a folder be colored like its contents.</summary>
    public bool ContainsDifferent { get; set; }

    public bool ContainsOrphan { get; set; }

    /// <summary>In what ways a file that differs differs.</summary>
    public DifferenceKinds Differences { get; set; }

    /// <summary>For a one-sided file that was renamed or moved: the file on the other side with the same content.</summary>
    public FolderNode? MovePartner { get; set; }

    public bool IsMoved => MovePartner is not null;
    public List<FolderNode> Children { get; } = [];

    public bool ExistsLeft => State is not FolderEntryState.RightOnly && (Left is not null || IsDirectory);
    public bool ExistsRight => State is not FolderEntryState.LeftOnly && (Right is not null || IsDirectory);

    /// <summary>All descendants (depth first), not including this node.</summary>
    public IEnumerable<FolderNode> Descendants()
    {
        foreach (var c in Children)
        {
            yield return c;
            foreach (var d in c.Descendants())
            {
                yield return d;
            }
        }
    }
}

public sealed record FolderSummary(int Same, int Different, int LeftOnly, int RightOnly, int Other)
{
    public int Total => Same + Different + LeftOnly + RightOnly + Other;
    public bool AreEqual => Different + LeftOnly + RightOnly + Other == 0;
}

public sealed class FolderComparison(string leftRoot, string rightRoot, FolderNode root, FolderOptions options)
{
    public string LeftRoot { get; } = leftRoot;
    public string RightRoot { get; } = rightRoot;
    public FolderNode Root { get; } = root;
    public FolderOptions Options { get; } = options;

    /// <summary>Counts of files (not folders) by state.</summary>
    public FolderSummary Summary
    {
        get
        {
            int same = 0, diff = 0, lo = 0, ro = 0, other = 0;
            foreach (var n in Root.Descendants().Where(n => !n.IsDirectory))
            {
                switch (n.State)
                {
                    case FolderEntryState.Same: same++; break;
                    case FolderEntryState.Different: diff++; break;
                    case FolderEntryState.LeftOnly: lo++; break;
                    case FolderEntryState.RightOnly: ro++; break;
                    default: other++; break;
                }
            }

            return new FolderSummary(same, diff, lo, ro, other);
        }
    }

    public string LeftPath(FolderNode n) => Path.Combine(n.LeftRoot ?? LeftRoot, n.LeftRelativePath.Replace('/', Path.DirectorySeparatorChar));
    public string RightPath(FolderNode n) => Path.Combine(n.RightRoot ?? RightRoot, n.RightRelativePath.Replace('/', Path.DirectorySeparatorChar));
}
