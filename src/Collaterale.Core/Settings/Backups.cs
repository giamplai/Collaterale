using System.Globalization;

namespace Collaterale.Core.Settings;

/// <summary>Keeps a copy of a file before it is overwritten, according to the backup preferences.</summary>
public static class Backups
{
    /// <summary>
    /// Copies <paramref name="path"/> as the preferences say and returns where the copy went, or null when no backup was made
    /// (backups off, no such file, or the copy could not be written: a backup problem never stops the real operation).
    /// </summary>
    /// <param name="root">For <see cref="BackupMode.BackupFolder"/>: the folder the file is relative to, so the layout is kept.</param>
    public static string? Create(string path, BackupPreferences prefs, string? root = null, DateTime? now = null)
    {
        if (prefs.Mode == BackupMode.Off || !File.Exists(path)) { return null; }
        try
        {
            string target;
            switch (prefs.Mode)
            {
                case BackupMode.SameFolderSuffix:
                    target = path + (prefs.Suffix.Length > 0 ? prefs.Suffix : ".bak");
                    break;
                case BackupMode.SameFolderNumbered:
                    target = path + ".bak" + (Numbers(path).DefaultIfEmpty(0).Max() + 1).ToString(CultureInfo.InvariantCulture);
                    break;
                case BackupMode.SameFolderTimestamp:
                    target = path + "." + (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".bak";
                    break;
                default:
                    if (string.IsNullOrWhiteSpace(prefs.Folder)) { return null; }
                    var relative = root is not null && Path.GetFullPath(path).StartsWith(Path.GetFullPath(root), StringComparison.Ordinal)
                        ? Path.GetRelativePath(root, path)
                        : Path.GetFileName(path);
                    target = Path.Combine(prefs.Folder, relative);
                    break;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, overwrite: true);
            Prune(path, prefs);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static IEnumerable<int> Numbers(string path)
    {
        var prefix = Path.GetFileName(path) + ".bak";
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(path)!, prefix + "*"))
        {
            if (int.TryParse(Path.GetFileName(f)[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)) { yield return n; }
        }
    }

    /// <summary>Deletes the oldest numbered or time-stamped backups beyond the number to keep.</summary>
    private static void Prune(string path, BackupPreferences prefs)
    {
        if (prefs.KeepVersions <= 0) { return; }
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        List<string> versions;
        if (prefs.Mode == BackupMode.SameFolderNumbered)
        {
            versions = [.. Directory.GetFiles(dir, name + ".bak*").Where(f => int.TryParse(Path.GetFileName(f)[(name.Length + 4)..], NumberStyles.None, CultureInfo.InvariantCulture, out _))
                .OrderBy(f => int.Parse(Path.GetFileName(f)[(name.Length + 4)..], CultureInfo.InvariantCulture))];
        }
        else if (prefs.Mode == BackupMode.SameFolderTimestamp)
        {
            versions = [.. Directory.GetFiles(dir, name + ".*.bak").OrderBy(f => f, StringComparer.Ordinal)];   // the stamp sorts as time
        }
        else
        {
            return;
        }

        foreach (var old in versions.Take(Math.Max(0, versions.Count - prefs.KeepVersions)))
        {
            File.Delete(old);
        }
    }
}
