using System.Formats.Tar;
using System.IO.Compression;
using Collaterale.Core.Cli;
using Collaterale.Core.Folders;

namespace Collaterale.Core.Tests;

public class ArchiveTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "collaterale-at-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    private static string MakeZip(string dir, string name, params (string path, string text)[] files)
    {
        var p = Path.Combine(dir, name);
        using var z = ZipFile.Open(p, ZipArchiveMode.Create);
        foreach (var (path, text) in files)
        {
            using var w = new StreamWriter(z.CreateEntry(path).Open());
            w.Write(text);
        }

        return p;
    }

    [Fact]
    public void Zip_is_unpacked_with_its_folder_structure()
    {
        var d = TempDir();
        var zip = MakeZip(d, "a.zip", ("top.txt", "t"), ("sub/inner.txt", "i"));
        var root = ArchiveExtractor.Extract(zip);
        Assert.Equal("t", File.ReadAllText(Path.Combine(root, "top.txt")));
        Assert.Equal("i", File.ReadAllText(Path.Combine(root, "sub", "inner.txt")));
        Assert.Equal("a.zip", Path.GetFileName(root));
    }

    [Fact]
    public void Entries_escaping_the_folder_are_ignored()
    {
        var d = TempDir();
        var zip = MakeZip(d, "evil.zip", ("../escaped.txt", "x"), ("ok.txt", "y"));
        var root = ArchiveExtractor.Extract(zip);
        Assert.True(File.Exists(Path.Combine(root, "ok.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "escaped.txt")));
    }

    [Fact]
    public void Tar_gz_is_unpacked()
    {
        var d = TempDir();
        var src = Path.Combine(d, "src"); Directory.CreateDirectory(Path.Combine(src, "x"));
        File.WriteAllText(Path.Combine(src, "x", "f.txt"), "tar-content");
        var tgz = Path.Combine(d, "a.tar.gz");
        using (var o = File.Create(tgz))
        using (var gz = new GZipStream(o, CompressionLevel.Fastest))
        {
            TarFile.CreateFromDirectory(src, gz, includeBaseDirectory: false);
        }

        var root = ArchiveExtractor.Extract(tgz);
        Assert.Equal("tar-content", File.ReadAllText(Path.Combine(root, "x", "f.txt")));
    }

    [Fact]
    public void Two_archives_are_routed_to_archive_compare_and_compare_as_folders()
    {
        var d = TempDir();
        var a = MakeZip(d, "a.zip", ("same.txt", "s"), ("changed.txt", "1"));
        var b = MakeZip(d, "b.zip", ("same.txt", "s"), ("changed.txt", "22"), ("new.txt", "n"));
        Assert.Equal(LaunchKind.ArchiveCompare, CompareRouter.ForFiles(a, b));
        Assert.Equal(LaunchKind.ArchiveCompare, LaunchParser.Parse([a, b]).Kind);

        var c = FolderComparer.Compare(ArchiveExtractor.Extract(a), ArchiveExtractor.Extract(b), new FolderOptions());
        Assert.Equal(1, c.Summary.RightOnly);
        Assert.Equal(1, c.Summary.Different);
        Assert.Equal(1, c.Summary.Same);
    }

    [Fact]
    public void A_zip_against_a_plain_file_is_not_an_archive_compare()
    {
        var d = TempDir();
        var a = MakeZip(d, "a.zip", ("f", "x"));
        var t = Path.Combine(d, "t.txt"); File.WriteAllText(t, "hello");
        Assert.NotEqual(LaunchKind.ArchiveCompare, CompareRouter.ForFiles(a, t));
    }
}
