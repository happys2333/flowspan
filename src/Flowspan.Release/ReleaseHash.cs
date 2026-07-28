using System.Security.Cryptography;

namespace Flowspan.Release;

public static class ReleaseHash
{
    public static string Sha256File(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length > ReleaseBounds.MaximumFileBytes)
        {
            throw new ReleaseInputException(
                "A release file exceeds the individual size bound.");
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static string Sha256Bytes(ReadOnlySpan<byte> value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    public static bool IsLowerSha256(string value) =>
        value.Length == 64
        && value.All(static character =>
            char.IsAsciiHexDigit(character)
            && !char.IsAsciiLetterUpper(character));
}
