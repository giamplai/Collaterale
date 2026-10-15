using System.Text;
using Collaterale.Core.Text;

namespace Collaterale.Core.Tests;

public class TextFileTests
{
    [Theory]
    [InlineData("a\nb\nc", new[] { "a", "b", "c" }, EolStyle.Lf, false)]
    [InlineData("a\r\nb\r\n", new[] { "a", "b" }, EolStyle.CrLf, true)]
    [InlineData("a\rb\r", new[] { "a", "b" }, EolStyle.Cr, true)]
    [InlineData("a\nb\r\nc", new[] { "a", "b", "c" }, EolStyle.Mixed, false)]
    [InlineData("single", new[] { "single" }, EolStyle.None, false)]
    [InlineData("\n", new[] { "" }, EolStyle.Lf, true)]
    public void Splits_lines_and_detects_eol(string text, string[] lines, EolStyle eol, bool final)
    {
        var (l, e, f) = TextFile.Split(text);
        Assert.Equal(lines, l);
        Assert.Equal((eol, final), (e, f));
    }

    [Fact]
    public void Utf8_with_and_without_bom_roundtrips_byte_for_byte()
    {
        foreach (var bom in new[] { true, false })
        {
            var original = new UTF8Encoding(bom).GetPreamble().Concat(Encoding.UTF8.GetBytes("héllo\r\nwörld\r\n")).ToArray();
            var t = TextFile.Decode(original);
            Assert.Equal(bom, t.HasBom);
            Assert.Equal(["héllo", "wörld"], t.Lines);
            Assert.Equal(original, TextFile.Encode(t.Lines, t.Eol, t.EndsWithNewline, t.Encoding, t.HasBom));
        }
    }

    [Fact]
    public void Utf16_with_bom_is_recognised()
    {
        var bytes = new UnicodeEncoding(false, true).GetPreamble().Concat(Encoding.Unicode.GetBytes("a\nb")).ToArray();
        var t = TextFile.Decode(bytes);
        Assert.Equal(["a", "b"], t.Lines);
        Assert.Equal("UTF-16 LE", t.EncodingName);
        Assert.Equal(bytes, TextFile.Encode(t.Lines, t.Eol, t.EndsWithNewline, t.Encoding, t.HasBom));
    }

    [Fact]
    public void Utf16_without_bom_is_detected_by_nul_pattern()
    {
        var t = TextFile.Decode(Encoding.Unicode.GetBytes("hello world\nline two\n"));
        Assert.Equal(["hello world", "line two"], t.Lines);
        Assert.False(t.IsBinary);
    }

    [Fact]
    public void Invalid_utf8_falls_back_to_a_single_byte_encoding()
    {
        var t = TextFile.Decode([0x63, 0x61, 0x66, 0xE9, 0x0A]); // "café\n" in Latin-1
        Assert.Equal(["café"], t.Lines);
        Assert.Equal("iso-8859-1", t.Encoding.WebName);
    }

    [Fact]
    public void Binary_data_is_flagged_instead_of_decoded()
    {
        var t = TextFile.Decode([0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02]);
        Assert.True(t.IsBinary);
        Assert.Empty(t.Lines);
    }

    [Fact]
    public void Empty_file_is_empty_text()
    {
        var t = TextFile.Decode([]);
        Assert.Empty(t.Lines);
        Assert.False(t.IsBinary);
    }

    [Fact]
    public void Missing_final_newline_is_preserved_on_save()
    {
        var bytes = "a\nb"u8.ToArray();
        var t = TextFile.Decode(bytes);
        Assert.False(t.EndsWithNewline);
        Assert.Equal(bytes, TextFile.Encode(t.Lines, t.Eol, t.EndsWithNewline, t.Encoding, t.HasBom));
    }
}

public class LineEndingComparisonTests
{
    [Fact]
    public void Files_that_differ_only_in_line_endings_compare_equal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-eol-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "unix.txt"), "one\ntwo\nthree\n"u8.ToArray());
        File.WriteAllBytes(Path.Combine(dir, "win.txt"), "one\r\ntwo\r\nthree\r\n"u8.ToArray());
        File.WriteAllBytes(Path.Combine(dir, "mac.txt"), "one\rtwo\rthree\r"u8.ToArray());
        var u = Collaterale.Core.Text.TextFile.Load(Path.Combine(dir, "unix.txt"));
        var w = Collaterale.Core.Text.TextFile.Load(Path.Combine(dir, "win.txt"));
        var m = Collaterale.Core.Text.TextFile.Load(Path.Combine(dir, "mac.txt"));
        Assert.True(Collaterale.Core.Diff.TextComparison.Compare(u.Lines, w.Lines).AreEqual);
        Assert.True(Collaterale.Core.Diff.TextComparison.Compare(u.Lines, m.Lines).AreEqual);
        Assert.NotEqual(u.Eol, w.Eol);                           // ...while each side still reports its own style
    }
}
