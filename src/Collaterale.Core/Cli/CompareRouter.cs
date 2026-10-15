using Collaterale.Core.Folders;
using Collaterale.Core.Text;

namespace Collaterale.Core.Cli;

/// <summary>Chooses the comparison view that suits a pair of files, so the user does not have to ask for it.</summary>
public static class CompareRouter
{
    private static readonly HashSet<string> Pictures = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico" };
    private static readonly HashSet<string> Tables = new(StringComparer.OrdinalIgnoreCase) { ".csv", ".tsv" };

    public static LaunchKind ForFiles(string left, string right)
    {
        if (ArchiveExtractor.IsArchive(left) && ArchiveExtractor.IsArchive(right)) { return LaunchKind.ArchiveCompare; }
        var (le, re) = (Path.GetExtension(left), Path.GetExtension(right));
        if (Pictures.Contains(le) && Pictures.Contains(re)) { return LaunchKind.ImageCompare; }
        if (Tables.Contains(le) && Tables.Contains(re)) { return LaunchKind.TableCompare; }
        return LooksBinary(left) || LooksBinary(right) ? LaunchKind.HexCompare : LaunchKind.TextCompare;
    }

    /// <summary>True when the start of the file looks like binary data (the same test the text view uses to refuse a file).</summary>
    public static bool LooksBinary(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var buffer = new byte[8000];
            var n = fs.Read(buffer, 0, buffer.Length);
            return n > 0 && TextFile.Decode(buffer.AsSpan(0, n).ToArray()).IsBinary;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;   // unreadable: let the chosen view report the real error
        }
    }
}
