using System.Security.Cryptography;

namespace Collaterale.Core.Folders;

/// <summary>
/// Pairs files that vanished on one side with files that appeared on the other when their content is identical.
/// Only files of equal size are ever hashed, so the cost stays proportional to the number of suspicious candidates.
/// </summary>
public static class RenameDetector
{
    /// <summary>Files larger than this are not hashed (a rename of a huge file shows up as delete + add, which is still correct).</summary>
    public const long MaxHashBytes = 2L * 1024 * 1024 * 1024;

    public static int Detect(FolderComparison c, CancellationToken ct = default)
    {
        var left = c.Root.Descendants().Where(n => !n.IsDirectory && n.State == FolderEntryState.LeftOnly && n.Left is { Size: > 0 }).ToList();
        var right = c.Root.Descendants().Where(n => !n.IsDirectory && n.State == FolderEntryState.RightOnly && n.Right is { Size: > 0 }).ToList();
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var rightBySize = right.GroupBy(n => n.Right!.Size).ToDictionary(g => g.Key, g => g.ToList());
        var used = new HashSet<FolderNode>();
        var pairs = 0;

        foreach (var l in left)
        {
            ct.ThrowIfCancellationRequested();
            if (!rightBySize.TryGetValue(l.Left!.Size, out var candidates) || l.Left.Size > MaxHashBytes)
            {
                continue;
            }

            var free = candidates.Where(r => !used.Contains(r)).ToList();
            if (free.Count == 0) { continue; }

            string? lh = null;
            FolderNode? best = null;
            var bestScore = -1;
            foreach (var r in free)
            {
                lh ??= Hash(c.LeftPath(l), ct);
                if (lh is null || lh != Hash(c.RightPath(r), ct)) { continue; }
                var score = Similarity(l, r);
                if (score > bestScore) { bestScore = score; best = r; }
            }

            if (best is not null)
            {
                l.MovePartner = best;
                best.MovePartner = l;
                used.Add(best);
                pairs++;
            }
        }

        return pairs;
    }

    /// <summary>Prefer the candidate with the same file name, then the longest shared folder suffix (identical copies in several places).</summary>
    private static int Similarity(FolderNode a, FolderNode b)
    {
        var score = string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) ? 1000 : 0;
        var pa = a.RelativePath.Split('/');
        var pb = b.RelativePath.Split('/');
        for (int i = pa.Length - 1, j = pb.Length - 1; i >= 0 && j >= 0 && string.Equals(pa[i], pb[j], StringComparison.OrdinalIgnoreCase); i--, j--)
        {
            score++;
        }

        return score;
    }

    private static string? Hash(string path, CancellationToken ct)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
            using var sha = SHA256.Create();
            var buffer = new byte[64 * 1024];
            int n;
            while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                sha.TransformBlock(buffer, 0, n, null, 0);
            }

            sha.TransformFinalBlock([], 0, 0);
            return Convert.ToHexString(sha.Hash!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
