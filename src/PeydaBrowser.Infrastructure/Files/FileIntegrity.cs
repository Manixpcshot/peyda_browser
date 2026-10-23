using System.Security.Cryptography;

namespace PeydaBrowser.Infrastructure.Files;

/// <summary>SHA-256 helpers used to verify downloaded update files against the
/// published SHA256SUMS.txt before anything is executed.</summary>
public static class FileIntegrity
{
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(filePath);
        return await ComputeSha256Async(stream, ct).ConfigureAwait(false);
    }

    public static async Task<string> ComputeSha256Async(Stream stream, CancellationToken ct = default)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Constant-time comparison of a file's hash with an expected hex value.</summary>
    public static async Task<bool> VerifySha256Async(string filePath, string expectedHex, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedHex)) return false;
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedHex.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        await using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var actual = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Parses a SHA256SUMS.txt file ("&lt;hash&gt;  &lt;filename&gt;" per line).</summary>
    public static IReadOnlyDictionary<string, string> ParseSha256Sums(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Length == 64)
                result[parts[1].Trim().TrimStart('*')] = parts[0].ToLowerInvariant();
        }
        return result;
    }
}
