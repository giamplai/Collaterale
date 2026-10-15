using System.Formats.Tar;
using System.IO.Compression;

namespace Collaterale.Core.Folders;

/// <summary>
/// Opens archives (zip, tar, tar.gz) as folders by unpacking them into a per-session temporary directory,
/// so the folder compare can browse them. Entries that would land outside the target folder are skipped.
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>The formats the program reads and the file masks that stand for each, until the preferences say otherwise.</summary>
    public static IReadOnlyList<Collaterale.Core.Settings.ArchiveType> DefaultTypes { get; } =
    [
        new() { Format = "Zip", Mask = "*.zip;*.jar;*.war;*.nupkg" },
        new() { Format = "Tar", Mask = "*.tar" },
        new() { Format = "Tar (gzip)", Mask = "*.tar.gz;*.tgz" },
    ];

    private static string[] Extensions = ExtensionsOf(DefaultTypes);

    private static string[] ExtensionsOf(IEnumerable<Collaterale.Core.Settings.ArchiveType> types)
        => [.. types.SelectMany(t => t.Mask.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(m => m.TrimStart('*'))
            .Where(m => m.Length > 0)];

    /// <summary>Uses the file masks of the preferences (null = the built-in list). A format with an empty mask is not recognized.</summary>
    public static void Configure(IEnumerable<Collaterale.Core.Settings.ArchiveType>? types) => Extensions = ExtensionsOf(types ?? DefaultTypes);

    private static readonly string SessionRoot = Path.Combine(Path.GetTempPath(), "collaterale-archives-" + Environment.ProcessId);

    public static bool IsArchive(string path) => File.Exists(path) && Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>Unpacks the archive into a fresh folder named after it and returns that folder.</summary>
    public static string Extract(string archive, CancellationToken ct = default)
    {
        var dest = Path.Combine(SessionRoot, Guid.NewGuid().ToString("N")[..8], Path.GetFileName(archive));
        Directory.CreateDirectory(dest);
        using var fs = File.OpenRead(archive);
        var magic = new byte[4];
        var n = fs.Read(magic, 0, 4);
        fs.Position = 0;

        if (n >= 4 && magic[0] == 'P' && magic[1] == 'K')
        {
            ExtractZip(fs, dest, ct);
        }
        else if (n >= 2 && magic[0] == 0x1f && magic[1] == 0x8b)
        {
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            ExtractTar(gz, dest, ct);
        }
        else
        {
            ExtractTar(fs, dest, ct);
        }

        return dest;
    }

    public static void CleanUp()
    {
        try
        {
            if (Directory.Exists(SessionRoot)) { Directory.Delete(SessionRoot, recursive: true); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // temp files; the OS will clear them eventually
        }
    }

    private static void ExtractZip(Stream s, string dest, CancellationToken ct)
    {
        using var zip = new ZipArchive(s, ZipArchiveMode.Read);
        var root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        foreach (var e in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(dest, e.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.Ordinal))
            {
                continue;                                         // zip-slip: ignore entries that escape the folder
            }

            if (e.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, overwrite: true);
        }
    }

    private static void ExtractTar(Stream s, string dest, CancellationToken ct)
    {
        using var tar = new TarReader(s);
        var root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        while (tar.GetNextEntry() is { } e)
        {
            ct.ThrowIfCancellationRequested();
            if (e.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory) || e.DataStream is null && e.EntryType != TarEntryType.Directory)
            {
                continue;                                         // links, devices… are not part of a content comparison
            }

            var target = Path.GetFullPath(Path.Combine(dest, e.Name.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.Ordinal))
            {
                continue;
            }

            if (e.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var o = File.Create(target)) { e.DataStream!.CopyTo(o); }
            File.SetLastWriteTimeUtc(target, e.ModificationTime.UtcDateTime);
        }
    }
}
