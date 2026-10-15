namespace Collaterale.Core.Folders;

/// <summary>CRC-32 (IEEE 802.3, the zip/PNG polynomial). Used for the checksum-based content test.</summary>
public static class Crc32
{
    private static readonly uint[] Table = Build();

    private static uint[] Build()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++) { c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; }
            t[i] = c;
        }

        return t;
    }

    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        crc = ~crc;
        foreach (var b in data) { crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8); }
        return ~crc;
    }

    public static uint OfFile(string path, CancellationToken ct = default)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
        var buf = new byte[64 * 1024];
        uint crc = 0;
        int n;
        while ((n = fs.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            crc = Update(crc, buf.AsSpan(0, n));
        }

        return crc;
    }
}
