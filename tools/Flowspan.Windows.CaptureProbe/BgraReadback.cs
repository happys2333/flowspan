using System.Security.Cryptography;

namespace Flowspan.Windows.CaptureProbe;

internal static class BgraReadback
{
    public const int MaximumDimension = 16_384;
    public const int MaximumBytes = 64 * 1024 * 1024;

    public static byte[] CopyRows(
        ReadOnlySpan<byte> mapped,
        int width,
        int height,
        int rowPitch)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaximumDimension);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaximumDimension);
        int rowBytes = checked(width * 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(rowPitch, rowBytes);
        long planeLength = checked((long)rowPitch * height);
        if (planeLength > MaximumBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(rowPitch));
        }

        int requiredLength = checked(((height - 1) * rowPitch) + rowBytes);
        if (mapped.Length < requiredLength)
        {
            throw new ArgumentException("Mapped rows are truncated.", nameof(mapped));
        }

        byte[] packed = new byte[checked(rowBytes * height)];
        try
        {
            for (int row = 0; row < height; row++)
            {
                mapped.Slice(checked(row * rowPitch), rowBytes)
                    .CopyTo(packed.AsSpan(row * rowBytes, rowBytes));
            }

            return packed;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(packed);
            throw;
        }
    }
}
