using System.Security.Cryptography;

namespace Flowspan.Windows.CaptureProbe;

internal static class KnownPixels
{
    public const int SourceWidth = 7;
    public const int SourceHeight = 5;
    public const int ContentWidth = 3;
    public const int ContentHeight = 2;
    public const string ExpectedSha256 =
        "fc865b98e8180228df0ec6c60cd9a919aa9033ae1b802fbfefe91ede9ac2e3af";

    public static byte[] CreateExpected() =>
        [1, 2, 3, 255, 18, 9, 6, 255, 35, 16, 9, 255,
        32, 15, 8, 255, 49, 22, 11, 255, 66, 29, 14, 255];

    public static byte[] CreateSource()
    {
        byte[] source = new byte[SourceWidth * SourceHeight * 4];
        source.AsSpan().Fill(0xEE);
        byte[] expected = CreateExpected();
        try
        {
            for (int row = 0; row < ContentHeight; row++)
            {
                expected.AsSpan(row * ContentWidth * 4, ContentWidth * 4)
                    .CopyTo(source.AsSpan(row * SourceWidth * 4));
            }

            return source;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    public static string Hash(ReadOnlySpan<byte> pixels) =>
        Convert.ToHexStringLower(SHA256.HashData(pixels));
}
