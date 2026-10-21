using System.Security.Cryptography;

namespace Collaterale.Core.Folders;

/// <summary>Cryptographic checksums of whole files, for the checksum-based content tests (SHA-1 and SHA-256).</summary>
public static class FileHash
{
    public static byte[] Sha1(string path, CancellationToken ct = default) => Of(path, HashAlgorithmName.SHA1, ct);

    public static byte[] Sha256(string path, CancellationToken ct = default) => Of(path, HashAlgorithmName.SHA256, ct);

    public static byte[] Of(string path, HashAlgorithmName algorithm, CancellationToken ct = default)
    {
        using var hash = IncrementalHash.CreateHash(algorithm);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
        var buf = new byte[64 * 1024];
        int n;
        while ((n = fs.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            hash.AppendData(buf, 0, n);
        }

        return hash.GetHashAndReset();
    }

    /// <summary>True when both files have the same checksum under the algorithm.</summary>
    public static bool SameHash(string a, string b, HashAlgorithmName algorithm, CancellationToken ct = default)
        => Of(a, algorithm, ct).AsSpan().SequenceEqual(Of(b, algorithm, ct));
}
