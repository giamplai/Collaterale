namespace Collaterale.Core.Folders;

public sealed class FolderComparer
{
    private static int _bufferSize = 64 * 1024;

    private static int BufferSize => _bufferSize;

    /// <summary>Bytes read at a time when two files are compared byte by byte (a preference; larger is faster on fast disks).</summary>
    public static void ConfigureBuffer(int bytes) => _bufferSize = Math.Clamp(bytes, 4096, 16 * 1024 * 1024);

    /// <summary>Backstop for link cycles the path check cannot recognise.</summary>
    private const int MaxLinkDepth = 40;

    private readonly FolderOptions _o;
    private readonly MaskSet _include;
    private readonly MaskSet _excludeFiles;
    private readonly MaskSet _excludeFolders;
    private readonly MaskSet _includeFolders;
    private readonly DateTime _now = DateTime.UtcNow;
    private readonly StringComparer _names;
    private readonly CancellationToken _ct;
    private readonly IProgress<string>? _progress;

    private FolderComparer(FolderOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        _o = options;
        _progress = progress;
        _ct = ct;
        _names = options.IgnoreNameCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _include = new MaskSet(options.Include, options.IgnoreNameCase);
        _excludeFiles = new MaskSet(options.ExcludeFiles, options.IgnoreNameCase);
        _excludeFolders = new MaskSet(options.ExcludeFolders, options.IgnoreNameCase);
        _includeFolders = new MaskSet(options.IncludeFolders, options.IgnoreNameCase);
    }

    public static FolderComparison Compare(string leftRoot, string rightRoot, FolderOptions? options = null, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        options ??= new FolderOptions();
        var c = new FolderComparer(options, progress, ct);
        var root = new FolderNode("", "", true);
        c.CompareDirs(Directory.Exists(leftRoot) ? new DirectoryInfo(leftRoot) : null, Directory.Exists(rightRoot) ? new DirectoryInfo(rightRoot) : null, root, new Where("", "", null, null), []);
        var result = new FolderComparison(leftRoot, rightRoot, root, options);
        if (options.DetectRenames)
        {
            RenameDetector.Detect(result, ct);
        }

        return result;
    }

    private sealed record Entry(string Name, FileSystemInfo? Left, FileSystemInfo? Right, Newer Filtered = Newer.None);

    /// <summary>Where the entries of a folder live: the relative path of the folder on each side, and (inside archives) the unpacked root.</summary>
    private sealed record Where(string LeftBase, string RightBase, string? LeftRoot, string? RightRoot);

    private static bool IsLink(FileSystemInfo? e) => e?.LinkTarget is not null;

    private static bool IsArchiveFile(FileSystemInfo? e) => e is FileInfo f && ArchiveExtractor.IsArchive(f.FullName);

    private void CompareDirs(DirectoryInfo? left, DirectoryInfo? right, FolderNode node, Where at, HashSet<string> visiting)
    {
        _ct.ThrowIfCancellationRequested();
        _progress?.Report(node.RelativePath);

        var (l, hiddenL) = Entries(left);
        var (r, hiddenR) = Entries(right);
        var entries = Pair(l, r, at, hiddenL, hiddenR);
        entries.Sort(new EntryOrder());
        var inArchive = at.LeftRoot is not null || at.RightRoot is not null;

        foreach (var (name, le, re, filtered) in entries)
        {
            var rel = Join(node.RelativePath, name);
            var leftRel = Join(at.LeftBase, le?.Name ?? name);
            var rightRel = Join(at.RightBase, re?.Name ?? name);
            var rightName = re is not null && le is not null && re.Name != le.Name ? re.Name : null;

            FolderNode Place(FolderNode n)
            {
                n.LeftRelativePath = leftRel;
                n.RightRelativePath = rightRel;
                n.RightName = rightName;
                n.FilteredSide = filtered;
                n.InArchive = inArchive;
                n.LeftRoot = at.LeftRoot;
                n.RightRoot = at.RightRoot;
                return n;
            }

            // symbolic links are single entries unless asked to follow them
            if (!_o.FollowSymbolicLinks && (IsLink(le) || IsLink(re)))
            {
                node.Children.Add(Place(CompareLinks(name, rel, le, re)));
                continue;
            }

            var isDir = (le is DirectoryInfo) || (re is DirectoryInfo);
            if (le is DirectoryInfo && re is FileInfo || le is FileInfo && re is DirectoryInfo)
            {
                node.Children.Add(Place(new FolderNode(name, rel, false) { State = FolderEntryState.TypeMismatch, Left = Snap(le), Right = Snap(re) }));
                continue;
            }

            var archives = !isDir && (le is not null || re is not null) && (le is null || IsArchiveFile(le)) && (re is null || IsArchiveFile(re));
            if (archives && _o.Archives == ArchiveHandling.AsFoldersAlways && TryUnpack(le, re, out var lx, out var rx))
            {
                var folder = Place(new FolderNode(name, rel, true) { IsArchive = true, Left = Snap(le), Right = Snap(re) });
                CompareDirs(lx is null ? null : new DirectoryInfo(lx), rx is null ? null : new DirectoryInfo(rx), folder, new Where("", "", lx ?? at.LeftRoot, rx ?? at.RightRoot), visiting);
                folder.State = le is null ? FolderEntryState.RightOnly : re is null ? FolderEntryState.LeftOnly : Aggregate(folder);
                if (folder.Left is { } fl && folder.Right is { } fr) { folder.Newer = NewerSide(fl.ModifiedUtc - fr.ModifiedUtc); }
                node.Children.Add(folder);
                continue;
            }

            if (isDir)
            {
                var child = Place(new FolderNode(name, rel, true));
                if (_o.Recursive && Enter(le as DirectoryInfo, visiting, out var keyL) && Enter(re as DirectoryInfo, visiting, out var keyR))
                {
                    CompareDirs(le as DirectoryInfo, re as DirectoryInfo, child, new Where(leftRel, rightRel, at.LeftRoot, at.RightRoot), visiting);
                    if (keyL is not null) { visiting.Remove(keyL); }
                    if (keyR is not null) { visiting.Remove(keyR); }
                }

                child.State = le is null ? FolderEntryState.RightOnly : re is null ? FolderEntryState.LeftOnly : Aggregate(child);
                if (_o.HideEmptyFolders && child.Children.Count == 0 && (_include.IsEmpty is false || _excludeFiles.IsEmpty is false || _excludeFolders.IsEmpty is false || _includeFolders.IsEmpty is false || _o.OtherFilters.Count > 0) && child.State != FolderEntryState.Error)
                {
                    continue;
                }

                node.Children.Add(child);
            }
            else
            {
                var file = Place(CompareFiles(name, rel, le as FileInfo, re as FileInfo, (le?.Name ?? name, re?.Name ?? name)));
                file.IsArchive = archives && _o.Archives == ArchiveHandling.AsFoldersOnceOpened;
                node.Children.Add(file);
            }
        }

        node.State = Aggregate(node);
        node.ContainsDifferent = node.Children.Any(c => c.ContainsDifferent || (!c.IsDirectory || c.IsArchive) && c.State is FolderEntryState.Different or FolderEntryState.TypeMismatch or FolderEntryState.Error);
        node.LeftBytes = node.Children.Where(c => c.ExistsLeft).Sum(c => c.IsDirectory && !c.IsArchive ? c.LeftBytes : c.Left?.Size ?? 0);
        node.RightBytes = node.Children.Where(c => c.ExistsRight).Sum(c => c.IsDirectory && !c.IsArchive ? c.RightBytes : c.Right?.Size ?? 0);
        node.ContainsOrphan = node.Children.Any(c => c.ContainsOrphan || c.State is FolderEntryState.LeftOnly or FolderEntryState.RightOnly);
    }

    /// <summary>While following links: refuse to enter a folder that is already being compared higher up (a link that points back at its own parent).</summary>
    private bool Enter(DirectoryInfo? dir, HashSet<string> visiting, out string? key)
    {
        key = null;
        if (dir is null || dir.LinkTarget is null) { return true; }
        if (visiting.Count >= MaxLinkDepth) { return false; }
        try
        {
            key = Path.GetFullPath((dir.ResolveLinkTarget(returnFinalTarget: true) ?? dir).FullName);
        }
        catch (IOException)
        {
            return false;
        }

        return visiting.Add(key);
    }

    private bool TryUnpack(FileSystemInfo? le, FileSystemInfo? re, out string? lx, out string? rx)
    {
        lx = rx = null;
        try
        {
            if (le is not null) { lx = ArchiveExtractor.Extract(le.FullName, _ct); }
            if (re is not null) { rx = ArchiveExtractor.Extract(re.FullName, _ct); }
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;                       // not really an archive: it is compared as a file
        }
    }

    /// <summary>Unpacks an archive that was shown as a file and compares its contents, turning the row into a folder (archive handling "once opened").</summary>
    public static void OpenArchive(FolderComparison c, FolderNode node, CancellationToken ct = default)
    {
        if (!node.IsArchive || node.IsDirectory) { return; }
        var cmp = new FolderComparer(c.Options, null, ct);
        var (le, re) = (node.ExistsLeft ? new FileInfo(c.LeftPath(node)) : null, node.ExistsRight ? new FileInfo(c.RightPath(node)) : null);
        if (!cmp.TryUnpack(le, re, out var lx, out var rx)) { return; }
        node.BecomeFolder();
        node.Children.Clear();
        cmp.CompareDirs(lx is null ? null : new DirectoryInfo(lx), rx is null ? null : new DirectoryInfo(rx), node, new Where("", "", lx ?? node.LeftRoot, rx ?? node.RightRoot), []);
        node.State = node.State == FolderEntryState.LeftOnly || node.State == FolderEntryState.RightOnly ? node.State : Aggregate(node);
        foreach (var d in node.Descendants()) { d.InArchive = true; }
    }

    /// <summary>Links compared as links: equal when they point at the same target.</summary>
    private FolderNode CompareLinks(string name, string rel, FileSystemInfo? l, FileSystemInfo? r)
    {
        var node = new FolderNode(name, rel, false) { Left = Snap(l), Right = Snap(r) };
        if (l is null) { node.State = FolderEntryState.RightOnly; }
        else if (r is null) { node.State = FolderEntryState.LeftOnly; }
        else if (IsLink(l) && IsLink(r) && l.LinkTarget == r.LinkTarget) { node.State = FolderEntryState.Same; }
        else { node.State = FolderEntryState.Different; node.Differences = DifferenceKinds.Content; }
        return node;
    }

    private static string Join(string parent, string name) => parent.Length == 0 ? name : parent + "/" + name;

    /// <summary>
    /// Lines up the two listings: by name first, then the hand-made alignments, then (when asked) names that are the same
    /// except for Unicode form or extension. Anything left over is an orphan.
    /// </summary>
    private List<Entry> Pair(Dictionary<string, FileSystemInfo> l, Dictionary<string, FileSystemInfo> r, Where dir, HashSet<string> hiddenL, HashSet<string> hiddenR)
    {
        var result = new List<Entry>(Math.Max(l.Count, r.Count));
        var takenL = new HashSet<string>(_names);
        var takenR = new HashSet<string>(_names);

        foreach (var (name, le) in l)
        {
            if (r.TryGetValue(name, out var re))
            {
                result.Add(new Entry(le.Name, le, re));
                takenL.Add(name);
                takenR.Add(name);
            }
        }

        void Join2(string ln, string rn)
        {
            result.Add(new Entry(l[ln].Name, l[ln], r[rn]));
            takenL.Add(ln);
            takenR.Add(rn);
        }

        foreach (var o in _o.Alignments)
        {
            if (!SameFolder(o.LeftPath, dir.LeftBase) || !SameFolder(o.RightPath, dir.RightBase)) { continue; }
            var ln = o.LeftPath[(o.LeftPath.LastIndexOf('/') + 1)..];
            var rn = o.RightPath[(o.RightPath.LastIndexOf('/') + 1)..];
            if (l.ContainsKey(ln) && r.ContainsKey(rn) && !takenL.Contains(ln) && !takenR.Contains(rn)) { Join2(ln, rn); }
        }

        if (_o.AlignUnicodeNormalization)
        {
            var byForm = r.Keys.Where(n => !takenR.Contains(n)).GroupBy(n => n.Normalize(System.Text.NormalizationForm.FormC), _names).ToDictionary(g => g.Key, g => g.ToList(), _names);
            foreach (var ln in l.Keys.Where(n => !takenL.Contains(n)).ToList())
            {
                if (byForm.TryGetValue(ln.Normalize(System.Text.NormalizationForm.FormC), out var cands) && cands.Count(c => !takenR.Contains(c)) == 1)
                {
                    Join2(ln, cands.Single(c => !takenR.Contains(c)));
                }
            }
        }

        if (_o.AlignDifferentExtensions)
        {
            static string Stem(string n) => Path.GetFileNameWithoutExtension(n);
            var freeL = l.Where(e => e.Value is FileInfo && !takenL.Contains(e.Key)).ToList();
            var freeR = r.Where(e => e.Value is FileInfo && !takenR.Contains(e.Key)).ToList();
            foreach (var (ln, _) in freeL)
            {
                var sameStemL = freeL.Count(x => _names.Equals(Stem(x.Key), Stem(ln)));
                var cands = freeR.Where(x => !takenR.Contains(x.Key) && _names.Equals(Stem(x.Key), Stem(ln))).ToList();
                if (sameStemL == 1 && cands.Count == 1 && Stem(ln).Length > 0) { Join2(ln, cands[0].Key); }
            }
        }

        foreach (var (name, le) in l) { if (!takenL.Contains(name)) { result.Add(new Entry(le.Name, le, null)); } }
        foreach (var (name, re) in r) { if (!takenR.Contains(name)) { result.Add(new Entry(re.Name, null, re)); } }

        // hidden by an "other" filter: dropped, unless the counterpart on the other side is visible (then both are shown)
        return [.. result.Where(e => !IsHidden(e.Left, hiddenL) && !IsHidden(e.Right, hiddenR) || (e.Left is not null && e.Right is not null && !(IsHidden(e.Left, hiddenL) && IsHidden(e.Right, hiddenR))))
            .Select(e => e with { Filtered = IsHidden(e.Left, hiddenL) ? Newer.Left : IsHidden(e.Right, hiddenR) ? Newer.Right : Newer.None })];

        static bool IsHidden(FileSystemInfo? e, HashSet<string> hidden) => e is not null && hidden.Contains(e.Name);

        static bool SameFolder(string path, string folder)
        {
            var i = path.LastIndexOf('/');
            return (i < 0 ? "" : path[..i]) == folder;
        }
    }

    private static FolderEntryState Aggregate(FolderNode dir)
    {
        if (dir.Children.Count == 0)
        {
            return FolderEntryState.Same;
        }

        return dir.Children.All(c => c.State == FolderEntryState.Same) ? FolderEntryState.Same : FolderEntryState.Different;
    }

    private FolderNode CompareFiles(string name, string rel, FileInfo? l, FileInfo? r, (string Left, string Right) names)
    {
        var node = new FolderNode(name, rel, false) { Left = Snap(l), Right = Snap(r) };
        if (l is null) { node.State = FolderEntryState.RightOnly; return node; }
        if (r is null) { node.State = FolderEntryState.LeftOnly; return node; }

        try
        {
            var (ls, rs) = (node.Left!, node.Right!);
            node.Newer = NewerSide(ls.ModifiedUtc - rs.ModifiedUtc);

            // quick tests: only what the folder listing already told us
            var quick = DifferenceKinds.None;
            // with text rules, size says nothing (CRLF vs LF files differ in size and are still the same text)
            var sizeIsEvidence = _o.ContentMode != ContentMode.Rules || _o.Criteria is CompareCriteria.SizeOnly or CompareCriteria.SizeAndTime;
            if (sizeIsEvidence && ls.Size != rs.Size) { quick |= DifferenceKinds.Size; }
            if (_o.Criteria is CompareCriteria.SizeAndTime or CompareCriteria.QuickThenContent && node.Newer != Newer.None) { quick |= DifferenceKinds.Time; }
            if (_o.CompareAttributes != 0 && (ls.Attributes & _o.CompareAttributes) != (rs.Attributes & _o.CompareAttributes)) { quick |= DifferenceKinds.Attributes; }
            if (_o.ComparePermissions && ls.Mode != rs.Mode) { quick |= DifferenceKinds.Permissions; }
            if (_o.CompareVersions && VersionOf(l.FullName) is var lv && VersionOf(r.FullName) is var rv && lv != rv) { quick |= DifferenceKinds.Version; }
            if (_o.CompareNameCase && !string.Equals(names.Left, names.Right, StringComparison.Ordinal) && string.Equals(names.Left, names.Right, StringComparison.OrdinalIgnoreCase)) { quick |= DifferenceKinds.NameCase; }

            var found = quick;
            switch (_o.Criteria)
            {
                case CompareCriteria.SizeOnly or CompareCriteria.SizeAndTime:
                    break;
                case CompareCriteria.Content:
                    found &= ~DifferenceKinds.Time;
                    if (!found.HasFlag(DifferenceKinds.Size) && !ContentEqual(l.FullName, r.FullName)) { found |= DifferenceKinds.Content; }
                    break;
                default: // QuickThenContent
                    if (quick == DifferenceKinds.None && _o.SkipContentIfQuickSame) { break; }
                    var sameContent = (!sizeIsEvidence || ls.Size == rs.Size) && ContentEqual(l.FullName, r.FullName);
                    if (sameContent)
                    {
                        found = _o.OverrideQuickResults ? DifferenceKinds.None : quick;
                    }
                    else
                    {
                        found = quick | DifferenceKinds.Content;
                    }

                    break;
            }

            node.Differences = found;
            node.State = found == DifferenceKinds.None ? FolderEntryState.Same : FolderEntryState.Different;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            node.State = FolderEntryState.Error;
            node.Error = ex.Message;
        }

        return node;
    }

    private static readonly HashSet<string> VersionExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".dll", ".ocx", ".sys", ".scr", ".cpl", ".mui", ".drv" };

    /// <summary>The version recorded in a program or library, or null when it has none (or is not one).</summary>
    public static string? VersionOf(string path)
    {
        if (!VersionExtensions.Contains(Path.GetExtension(path))) { return null; }
        try
        {
            var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrEmpty(v.FileVersion)) { return v.FileVersion; }
            return System.Reflection.AssemblyName.GetAssemblyName(path).Version?.ToString();
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException or FileLoadException)
        {
            return null;
        }
    }

    /// <summary>Which side is newer after the daylight-saving / time-zone allowances.</summary>
    private Newer NewerSide(TimeSpan delta)
    {
        if (_o.IgnoreTimeZone)
        {
            delta -= TimeSpan.FromHours(Math.Round(delta.TotalHours));
        }
        else if (_o.IgnoreDaylightSaving && Math.Abs(Math.Abs(delta.TotalHours) - 1) * 3600 <= _o.TimeTolerance.TotalSeconds)
        {
            delta -= TimeSpan.FromHours(Math.Sign(delta.TotalHours));
        }

        return delta > _o.TimeTolerance ? Newer.Left : -delta > _o.TimeTolerance ? Newer.Right : Newer.None;
    }

    private bool ContentEqual(string a, string b) => _o.ContentMode switch
    {
        ContentMode.Crc => Crc32.OfFile(a, _ct) == Crc32.OfFile(b, _ct),
        ContentMode.Rules => RulesEqual(a, b),
        _ => BytesEqual(a, b),
    };

    private bool RulesEqual(string a, string b)
    {
        var ta = Collaterale.Core.Text.TextFile.Load(a);
        var tb = Collaterale.Core.Text.TextFile.Load(b);
        if (ta.IsBinary || tb.IsBinary)
        {
            return BytesEqual(a, b);
        }

        return Collaterale.Core.Diff.TextComparison.Compare(ta.Lines, tb.Lines, _o.Rules.ToDiffOptions(a)).AreEqual;
    }

    private bool BytesEqual(string a, string b)
    {
        using var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize);
        using var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize);
        var ba = new byte[BufferSize];
        var bb = new byte[BufferSize];
        while (true)
        {
            _ct.ThrowIfCancellationRequested();
            var na = ReadFully(fa, ba);
            var nb = ReadFully(fb, bb);
            if (na != nb || !ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb)))
            {
                return false;
            }

            if (na == 0)
            {
                return true;
            }
        }
    }

    private static int ReadFully(Stream s, byte[] buf)
    {
        var total = 0;
        while (total < buf.Length)
        {
            var n = s.Read(buf, total, buf.Length - total);
            if (n == 0) { break; }
            total += n;
        }

        return total;
    }

    private static UnixFileMode? Mode(FileSystemInfo e)
    {
        if (OperatingSystem.IsWindows()) { return null; }
        try { return e.UnixFileMode; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private FileSnapshot? Snap(FileSystemInfo? e)
    {
        if (e is not null && _o.FollowSymbolicLinks && e.LinkTarget is not null)
        {
            try { e = e.ResolveLinkTarget(returnFinalTarget: true) ?? e; }
            catch (IOException) { /* a broken link keeps its own numbers */ }
        }

        return Describe(e);
    }

    private static FileSnapshot? Describe(FileSystemInfo? e)
        => e switch
        {
            FileInfo f => new FileSnapshot(f.Length, f.LastWriteTimeUtc, f.LinkTarget is not null, f.Attributes, Mode(f)),
            DirectoryInfo d => new FileSnapshot(0, d.LastWriteTimeUtc, d.LinkTarget is not null, d.Attributes, Mode(d)),
            _ => null,
        };

    /// <summary>
    /// Entries of a directory that pass the name filters, keyed by name, plus the names of files hidden by the "other" filters
    /// (kept in the dictionary so that a counterpart on the other side can still pull them back in).
    /// </summary>
    private (Dictionary<string, FileSystemInfo> Entries, HashSet<string> Hidden) Entries(DirectoryInfo? dir)
    {
        var d = new Dictionary<string, FileSystemInfo>(_names);
        var hidden = new HashSet<string>(_names);
        if (dir is null)
        {
            return (d, hidden);
        }

        try
        {
            foreach (var e in dir.EnumerateFileSystemInfos())
            {
                if (e is DirectoryInfo sub)
                {
                    if (_excludeFolders.IsMatch(sub.Name)) { continue; }
                    if (!_includeFolders.IsEmpty && !_includeFolders.IsMatch(sub.Name)) { continue; }
                }
                else
                {
                    if (_excludeFiles.IsMatch(e.Name)) { continue; }
                    if (!_include.IsEmpty && !_include.IsMatch(e.Name)) { continue; }
                    if (e is FileInfo file && IsFilteredOut(file)) { hidden.Add(e.Name); }
                }

                d[e.Name] = e;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // an unreadable directory simply shows up empty; the parent keeps its other entries
        }

        return (d, hidden);
    }

    private bool IsFilteredOut(FileInfo f)
    {
        if (_o.ExcludeProtectedFiles && IsProtected(f)) { return true; }
        foreach (var filter in _o.OtherFilters)
        {
            if (filter.Matches(f, _now)) { return true; }
        }

        return false;
    }

    /// <summary>Operating-system housekeeping files: hidden+system on Windows, Finder and Explorer droppings elsewhere.</summary>
    internal static bool IsProtected(FileInfo f)
    {
        const FileAttributes hiddenSystem = FileAttributes.Hidden | FileAttributes.System;
        if ((f.Attributes & hiddenSystem) == hiddenSystem) { return true; }
        var n = f.Name;
        return n.Equals(".DS_Store", StringComparison.Ordinal) || n.StartsWith("._", StringComparison.Ordinal) && n.Length > 2 && f.Length <= 4096
            || n.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) || n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Folders first, then files; each group alphabetical (case-insensitive for readability).</summary>
    private sealed class EntryOrder : IComparer<Entry>
    {
        public int Compare(Entry? x, Entry? y)
        {
            var dx = x!.Left is DirectoryInfo || x.Right is DirectoryInfo;
            var dy = y!.Left is DirectoryInfo || y.Right is DirectoryInfo;
            if (dx != dy) { return dx ? -1 : 1; }
            var c = StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
            return c != 0 ? c : StringComparer.Ordinal.Compare(x.Name, y.Name);
        }
    }
}
