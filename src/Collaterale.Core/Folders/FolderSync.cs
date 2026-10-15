namespace Collaterale.Core.Folders;

public enum SyncAction { CopyToRight, CopyToLeft, DeleteLeft, DeleteRight, CreateFolderRight, CreateFolderLeft, MoveRight, MoveLeft }

public enum SyncMode
{
    /// <summary>Copy new and newer files from left to right. Nothing is deleted.</summary>
    UpdateRight,

    /// <summary>Copy new and newer files from right to left. Nothing is deleted.</summary>
    UpdateLeft,

    /// <summary>Make the right side identical to the left: copy new/changed files and delete what only exists on the right.</summary>
    MirrorToRight,

    /// <summary>Make the left side identical to the right: copy new/changed files and delete what only exists on the left.</summary>
    MirrorToLeft,

    /// <summary>Copy the newer version of each file to the other side and copy orphans both ways. Nothing is deleted.</summary>
    TwoWay,
}

public sealed record SyncStep(SyncAction Action, string RelativePath, bool IsDirectory, long Bytes, string Reason)
{
    /// <summary>The path on the left / right side when it differs from <see cref="RelativePath"/> (aligned names).</summary>
    public string? LeftRel { get; init; }

    public string? RightRel { get; init; }

    public bool IsDelete => Action is SyncAction.DeleteLeft or SyncAction.DeleteRight;

    /// <summary>For a move: where the file goes (its new relative path on the side being changed). <see cref="RelativePath"/> is where it is now.</summary>
    public string? MoveTo { get; init; }
}

/// <summary>Files the plan deliberately leaves alone, with the reason (for example both sides changed with no way to tell which is newer).</summary>
public sealed record SyncSkip(string RelativePath, string Reason);

public sealed record SyncPlan(IReadOnlyList<SyncStep> Steps, IReadOnlyList<SyncSkip> Skipped)
{
    public long BytesToCopy => Steps.Where(s => !s.IsDirectory && !s.IsDelete).Sum(s => s.Bytes);
    public int Deletes => Steps.Count(s => s.IsDelete);
    public bool IsEmpty => Steps.Count == 0;
}

public sealed record SyncFailure(SyncStep Step, string Error);

public sealed record SyncResult(int Done, IReadOnlyList<SyncFailure> Failures)
{
    public bool Success => Failures.Count == 0;
}

public static class FolderSync
{
    /// <summary>Plans a synchronization of the whole comparison, or only of <paramref name="only"/> (and everything below selected folders).</summary>
    public static SyncPlan Plan(FolderComparison c, SyncMode mode, IEnumerable<FolderNode>? only = null)
    {
        var steps = new List<SyncStep>();
        var skipped = new List<SyncSkip>();
        var nodes = only is null ? c.Root.Descendants() : Expand(only);

        foreach (var n in nodes.Where(n => !n.InArchive))               // entries inside archives are temporary copies
        {
            if (n.IsMoved && PlanMove(n, mode, steps, skipped))
            {
                continue;
            }

            switch (n.State)
            {
                case FolderEntryState.LeftOnly:
                    if (mode is SyncMode.UpdateRight or SyncMode.MirrorToRight or SyncMode.TwoWay)
                    {
                        steps.Add(Copy(n, toRight: true, "only on the left"));
                    }
                    else if (mode == SyncMode.MirrorToLeft)
                    {
                        steps.Add(Sided(new SyncStep(SyncAction.DeleteLeft, n.RelativePath, n.IsDirectory && !n.IsArchive, 0, "only on the left; mirror removes it"), n));
                    }

                    break;
                case FolderEntryState.RightOnly:
                    if (mode is SyncMode.UpdateLeft or SyncMode.MirrorToLeft or SyncMode.TwoWay)
                    {
                        steps.Add(Copy(n, toRight: false, "only on the right"));
                    }
                    else if (mode == SyncMode.MirrorToRight)
                    {
                        steps.Add(Sided(new SyncStep(SyncAction.DeleteRight, n.RelativePath, n.IsDirectory && !n.IsArchive, 0, "only on the right; mirror removes it"), n));
                    }

                    break;
                case FolderEntryState.Different when !n.IsDirectory || n.IsArchive:
                    PlanDifferent(n, mode, steps, skipped);
                    break;
                case FolderEntryState.TypeMismatch:
                    skipped.Add(new SyncSkip(n.RelativePath, "a file on one side and a folder on the other"));
                    break;
                case FolderEntryState.Error:
                    skipped.Add(new SyncSkip(n.RelativePath, n.Error ?? "could not be read"));
                    break;
            }
        }

        return new SyncPlan(Order(steps), skipped);
    }

    /// <summary>Plans copying or deleting exactly the chosen entries (a copy of a folder includes everything below it).</summary>
    public static SyncPlan PlanFor(FolderComparison c, IEnumerable<FolderNode> selection, SyncAction action)
    {
        var steps = new List<SyncStep>();
        var skipped = new List<SyncSkip>();
        foreach (var n in Expand(selection).Where(n => !n.InArchive))
        {
            var fromRight = action is SyncAction.CopyToLeft;
            switch (action)
            {
                case SyncAction.CopyToRight when !n.ExistsLeft:
                case SyncAction.CopyToLeft when !n.ExistsRight:
                    skipped.Add(new SyncSkip(n.RelativePath, fromRight ? "does not exist on the right" : "does not exist on the left"));
                    break;
                case SyncAction.CopyToRight or SyncAction.CopyToLeft:
                    if (n.State == FolderEntryState.Same && !n.IsDirectory)
                    {
                        break; // nothing to do
                    }

                    steps.Add(Copy(n, action == SyncAction.CopyToRight, "selected"));
                    break;
                case SyncAction.DeleteLeft when n.ExistsLeft:
                case SyncAction.DeleteRight when n.ExistsRight:
                    steps.Add(Sided(new SyncStep(action, n.RelativePath, n.IsDirectory && !n.IsArchive, 0, "selected"), n));
                    break;
            }
        }

        return new SyncPlan(Order(steps), skipped);
    }

    /// <summary>
    /// A file renamed or moved (same content, different path). Mirror modes apply it as a rename on the destination instead of
    /// deleting and re-copying; two-way sync leaves it alone (it cannot know which name is right); update modes copy as usual.
    /// Returns true when the node was handled here.
    /// </summary>
    private static bool PlanMove(FolderNode n, SyncMode mode, List<SyncStep> steps, List<SyncSkip> skipped)
    {
        var partner = n.MovePartner!;
        var leftSide = n.State == FolderEntryState.LeftOnly ? n : partner;          // the file as it exists on the left
        var rightSide = n.State == FolderEntryState.LeftOnly ? partner : n;
        switch (mode)
        {
            case SyncMode.MirrorToRight:
                if (n == leftSide)                                                    // plan once, from the left node
                {
                    steps.Add(Sided(new SyncStep(SyncAction.MoveRight, rightSide.RelativePath, false, 0, "renamed or moved on the left; the right is renamed to match") { MoveTo = leftSide.RightRelativePath }, rightSide));
                }

                return true;
            case SyncMode.MirrorToLeft:
                if (n == rightSide)
                {
                    steps.Add(Sided(new SyncStep(SyncAction.MoveLeft, leftSide.RelativePath, false, 0, "renamed or moved on the right; the left is renamed to match") { MoveTo = rightSide.LeftRelativePath }, leftSide));
                }

                return true;
            case SyncMode.TwoWay:
                if (n == leftSide)
                {
                    skipped.Add(new SyncSkip(leftSide.RelativePath, $"renamed or moved ({rightSide.RelativePath} on the other side); use Mirror to apply the rename"));
                }

                return true;
            default:
                return false;                                                         // Update modes: treat as a new file, as before
        }
    }

    private static void PlanDifferent(FolderNode n, SyncMode mode, List<SyncStep> steps, List<SyncSkip> skipped)
    {
        switch (mode)
        {
            case SyncMode.MirrorToRight:
                steps.Add(Copy(n, true, "differs; mirror takes the left version"));
                break;
            case SyncMode.MirrorToLeft:
                steps.Add(Copy(n, false, "differs; mirror takes the right version"));
                break;
            case SyncMode.UpdateRight when n.Newer == Newer.Left:
            case SyncMode.TwoWay when n.Newer == Newer.Left:
                steps.Add(Copy(n, true, "left is newer"));
                break;
            case SyncMode.UpdateLeft when n.Newer == Newer.Right:
            case SyncMode.TwoWay when n.Newer == Newer.Right:
                steps.Add(Copy(n, false, "right is newer"));
                break;
            case SyncMode.UpdateRight or SyncMode.UpdateLeft:
                skipped.Add(new SyncSkip(n.RelativePath, n.Newer == Newer.None ? "differs but has the same time; cannot tell which is right" : "the destination is newer"));
                break;
            default:
                skipped.Add(new SyncSkip(n.RelativePath, "differs but has the same time; cannot tell which is newer"));
                break;
        }
    }

    private static SyncStep Copy(FolderNode n, bool toRight, string reason)
    {
        var bytes = (toRight ? n.Left : n.Right)?.Size ?? 0;
        var isFolder = n.IsDirectory && !n.IsArchive;       // an archive is one file, even when it is being looked into
        var action = isFolder
            ? (toRight ? SyncAction.CreateFolderRight : SyncAction.CreateFolderLeft)
            : (toRight ? SyncAction.CopyToRight : SyncAction.CopyToLeft);
        return Sided(new SyncStep(action, n.RelativePath, isFolder, bytes, reason), n);
    }

    private static SyncStep Sided(SyncStep s, FolderNode n) => s with { LeftRel = n.LeftRelativePath, RightRel = n.RightRelativePath };

    private static IEnumerable<FolderNode> Expand(IEnumerable<FolderNode> nodes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in nodes)
        {
            if (seen.Add(n.RelativePath))
            {
                yield return n;
            }

            foreach (var d in n.Descendants())
            {
                if (seen.Add(d.RelativePath))
                {
                    yield return d;
                }
            }
        }
    }

    /// <summary>Folders must exist before their files are copied into them, and be deleted after their contents.</summary>
    private static List<SyncStep> Order(List<SyncStep> steps)
    {
        // a move's destination folder must exist: create folders for them too, then move/copy, then delete
        var creates = steps.Where(s => s.Action is SyncAction.CreateFolderLeft or SyncAction.CreateFolderRight).OrderBy(s => s.RelativePath.Count(ch => ch == '/'));
        var copies = steps.Where(s => s.Action is SyncAction.CopyToLeft or SyncAction.CopyToRight or SyncAction.MoveLeft or SyncAction.MoveRight);
        var deletes = steps.Where(s => s.IsDelete).OrderByDescending(s => s.RelativePath.Count(ch => ch == '/'));
        return [.. creates, .. copies, .. deletes];
    }

    // ───────────────────────── execution ─────────────────────────

    /// <summary>
    /// Runs the plan. <paramref name="backup"/> is called with the path of a file (and the root it belongs to) just before the file is
    /// overwritten or deleted, so a copy of it can be kept.
    /// </summary>
    public static SyncResult Execute(FolderComparison c, SyncPlan plan, IProgress<SyncStep>? progress = null, CancellationToken ct = default, Action<string, string>? backup = null)
    {
        var failures = new List<SyncFailure>();
        var done = 0;
        foreach (var step in plan.Steps)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(step);
            try
            {
                Run(c, step, backup);
                done++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new SyncFailure(step, ex.Message));
            }
        }

        return new SyncResult(done, failures);
    }

    private static void Run(FolderComparison c, SyncStep s, Action<string, string>? backup)
    {
        var left = Safe(c.LeftRoot, (s.LeftRel ?? s.RelativePath).Replace('/', Path.DirectorySeparatorChar));
        var right = Safe(c.RightRoot, (s.RightRel ?? s.RelativePath).Replace('/', Path.DirectorySeparatorChar));

        switch (s.Action)
        {
            case SyncAction.CreateFolderRight: Directory.CreateDirectory(right); break;
            case SyncAction.CreateFolderLeft: Directory.CreateDirectory(left); break;
            case SyncAction.CopyToRight: CopyFile(left, right, p => backup?.Invoke(p, c.RightRoot)); break;
            case SyncAction.CopyToLeft: CopyFile(right, left, p => backup?.Invoke(p, c.LeftRoot)); break;
            case SyncAction.MoveRight: MoveFile(right, Safe(c.RightRoot, s.MoveTo!.Replace('/', Path.DirectorySeparatorChar))); break;
            case SyncAction.MoveLeft: MoveFile(left, Safe(c.LeftRoot, s.MoveTo!.Replace('/', Path.DirectorySeparatorChar))); break;
            case SyncAction.DeleteLeft: if (!s.IsDirectory) { backup?.Invoke(left, c.LeftRoot); } Delete(left, s.IsDirectory); break;
            case SyncAction.DeleteRight: if (!s.IsDirectory) { backup?.Invoke(right, c.RightRoot); } Delete(right, s.IsDirectory); break;
        }
    }

    /// <summary>Resolves a relative path under a root and refuses anything that would escape it.</summary>
    private static string Safe(string root, string rel)
    {
        var full = Path.GetFullPath(Path.Combine(root, rel));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.Ordinal))
        {
            throw new IOException($"Refusing to touch a path outside the compared folder: {rel}");
        }

        return full;
    }

    private static void CopyFile(string from, string to, Action<string>? beforeOverwrite = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (File.Exists(to))
        {
            beforeOverwrite?.Invoke(to);
            File.SetAttributes(to, FileAttributes.Normal); // read-only destinations are overwritten like any other
        }

        File.Copy(from, to, overwrite: true);
        File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(from)); // keep times so the next compare says "same"
        // files on a CD or DVD are all read-only; the copies need not be
        if (RemoveReadOnlyFromOptical && IsOptical(from)) { File.SetAttributes(to, File.GetAttributes(to) & ~FileAttributes.ReadOnly); }
    }

    /// <summary>The "remove the read-only flag when copying from a CD or DVD" preference.</summary>
    public static bool RemoveReadOnlyFromOptical { get; set; } = true;

    /// <summary>Tells whether a path is on an optical disc; replaceable so the behavior can be tested without a drive.</summary>
    public static Func<string, bool> IsOptical { get; set; } = path =>
    {
        try
        {
            var full = Path.GetFullPath(path);
            return DriveInfo.GetDrives().Where(d => full.StartsWith(d.Name, StringComparison.Ordinal)).OrderByDescending(d => d.Name.Length).FirstOrDefault()?.DriveType == DriveType.CDRom;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    };

    private static void MoveFile(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to, overwrite: false);          // never clobber an existing file: the plan said this name was free
    }

    private static void Delete(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            if (Directory.Exists(path)) { Directory.Delete(path, recursive: true); }
        }
        else if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }
}
