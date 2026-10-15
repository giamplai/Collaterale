using System.Text;

namespace Collaterale.Core.Text;

public enum EolStyle { None, Lf, CrLf, Cr, Mixed }

/// <summary>A decoded text file together with everything needed to write it back without unwanted changes.</summary>
public sealed record TextContent(IReadOnlyList<string> Lines, EolStyle Eol, bool EndsWithNewline, Encoding Encoding, bool HasBom, bool IsBinary)
{
    public static TextContent Empty { get; } = new([], EolStyle.None, false, new UTF8Encoding(false), false, false);

    public string EolString => Eol switch { EolStyle.CrLf => "\r\n", EolStyle.Cr => "\r", _ => "\n" };

    public string EncodingName => Encoding.CodePage switch
    {
        65001 => HasBom ? "UTF-8 with BOM" : "UTF-8",
        1200 => "UTF-16 LE",
        1201 => "UTF-16 BE",
        12000 => "UTF-32 LE",
        12001 => "UTF-32 BE",
        _ => Encoding.WebName.ToUpperInvariant(),
    };
}

public static class TextFile
{
    private const int BinarySniffBytes = 8000;

    public static TextContent Load(string path) => Decode(File.ReadAllBytes(path));

    public static TextContent Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return TextContent.Empty;
        }

        var (encoding, bomLength) = Detect(bytes);
        if (IsBinary(bytes, encoding))
        {
            return new TextContent([], EolStyle.None, false, encoding, false, IsBinary: true);
        }

        var text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        var (lines, eol, endsWithNewline) = Split(text);
        return new TextContent(lines, eol, endsWithNewline, encoding, bomLength > 0, false);
    }

    /// <summary>Encodes lines back to bytes using the original encoding, BOM and line-ending style.</summary>
    public static byte[] Encode(IReadOnlyList<string> lines, EolStyle eol, bool endsWithNewline, Encoding encoding, bool bom)
    {
        var sep = eol switch { EolStyle.CrLf => "\r\n", EolStyle.Cr => "\r", _ => "\n" };
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            sb.Append(lines[i]);
            if (i < lines.Count - 1 || endsWithNewline)
            {
                sb.Append(sep);
            }
        }

        var body = encoding.GetBytes(sb.ToString());
        if (!bom)
        {
            return body;
        }

        var preamble = encoding.GetPreamble();
        return [.. preamble, .. body];
    }

    public static (Encoding Encoding, int BomLength) Detect(byte[] b)
    {
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0 && b[3] == 0) { return (new UTF32Encoding(false, true), 4); }
        if (b.Length >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 0xFE && b[3] == 0xFF) { return (new UTF32Encoding(true, true), 4); }
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { return (new UTF8Encoding(true), 3); }
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { return (new UnicodeEncoding(false, true), 2); }
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { return (new UnicodeEncoding(true, true), 2); }

        // BOM-less UTF-16: lots of NUL bytes, consistently on odd (LE) or even (BE) positions
        var sniff = Math.Min(b.Length, BinarySniffBytes);
        int evenNul = 0, oddNul = 0;
        for (var i = 0; i < sniff; i++)
        {
            if (b[i] == 0) { if ((i & 1) == 0) { evenNul++; } else { oddNul++; } }
        }

        // ASCII-heavy UTF-16 has NULs in about half of one byte parity; a stray NUL in a binary blob must not qualify
        if (sniff >= 4 && oddNul > sniff / 4 && evenNul == 0) { return (new UnicodeEncoding(false, false), 0); }
        if (sniff >= 4 && evenNul > sniff / 4 && oddNul == 0) { return (new UnicodeEncoding(true, false), 0); }

        try
        {
            new UTF8Encoding(false, true).GetCharCount(b);
            return (new UTF8Encoding(false), 0);
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8: legacy single-byte text. Windows-1252 is the most common; it maps every byte except five.
            return (Encoding.Latin1, 0);
        }
    }

    private static bool IsBinary(byte[] b, Encoding encoding)
    {
        if (encoding is UnicodeEncoding or UTF32Encoding)
        {
            return false; // NULs are expected
        }

        var n = Math.Min(b.Length, BinarySniffBytes);
        for (var i = 0; i < n; i++)
        {
            if (b[i] == 0)
            {
                return true;
            }
        }

        return false;
    }

    public static (List<string> Lines, EolStyle Eol, bool EndsWithNewline) Split(string text)
    {
        var lines = new List<string>();
        int lf = 0, crlf = 0, cr = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\n')
            {
                lines.Add(text[start..i]);
                lf++;
                start = i + 1;
            }
            else if (ch == '\r')
            {
                lines.Add(text[start..i]);
                if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; } else { cr++; }
                start = i + 1;
            }
        }

        var endsWithNewline = start == text.Length && text.Length > 0;
        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        var kinds = (lf > 0 ? 1 : 0) + (crlf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        var eol = kinds > 1 ? EolStyle.Mixed : crlf > 0 ? EolStyle.CrLf : cr > 0 ? EolStyle.Cr : lf > 0 ? EolStyle.Lf : EolStyle.None;
        return (lines, eol, endsWithNewline);
    }
}
