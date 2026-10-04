using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Flowspan.Windows.CaptureProbe;

internal static class ProbeSelfTests
{
    public static int Run()
    {
        try
        {
            byte[] padded = [1, 2, 3, 4, 5, 6, 7, 8, 255, 255, 255, 255,
                9, 10, 11, 12, 13, 14, 15, 16];
            byte[] expected = [1, 2, 3, 4, 5, 6, 7, 8,
                9, 10, 11, 12, 13, 14, 15, 16];
            byte[] actual = BgraReadback.CopyRows(padded, 2, 2, 12);
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                throw new InvalidOperationException("Padded rows must produce exact packed BGRA.");
            }

            ExpectArgumentFailure(() => BgraReadback.CopyRows([], 0, 1, 4));
            ExpectArgumentFailure(() => BgraReadback.CopyRows([], 1, 0, 4));
            ExpectArgumentFailure(() => BgraReadback.CopyRows([], 16_385, 1, 65_540));
            ExpectArgumentFailure(() => BgraReadback.CopyRows([], 1, 16_385, 4));
            ExpectArgumentFailure(() => BgraReadback.CopyRows([], 1, 1, 3));
            ExpectArgumentFailure(() => BgraReadback.CopyRows([], 1, 2, int.MaxValue));
            ExpectArgumentFailure(() => BgraReadback.CopyRows([1, 2, 3], 1, 1, 4));
            VerifyAbiLayout();
            byte[] known = KnownPixels.CreateExpected();
            byte[] source = KnownPixels.CreateSource();
            byte[] packedSource = BgraReadback.CopyRows(
                source, KnownPixels.ContentWidth, KnownPixels.ContentHeight,
                KnownPixels.SourceWidth * 4);
            try
            {
                if (KnownPixels.Hash(known) != KnownPixels.ExpectedSha256
                    || !packedSource.AsSpan().SequenceEqual(known))
                {
                    throw new InvalidOperationException("Known cropped pixels changed.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(known);
                CryptographicOperations.ZeroMemory(source);
                CryptographicOperations.ZeroMemory(packedSource);
                CryptographicOperations.ZeroMemory(actual);
                CryptographicOperations.ZeroMemory(expected);
                CryptographicOperations.ZeroMemory(padded);
            }

            Console.WriteLine("self_test=pass cases=10 native_api_called=false");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"self_test=fail type={exception.GetType().Name}");
            return 1;
        }
    }

    public static void VerifyAbiLayout()
    {
        if (IntPtr.Size != 8
            || Marshal.SizeOf<Texture2DDescription>() != 44
            || Marshal.SizeOf<SubresourceData>() != 16
            || Marshal.SizeOf<MappedSubresource>() != 16
            || Marshal.SizeOf<TextureBox>() != 24
            || Marshal.OffsetOf<Texture2DDescription>(nameof(Texture2DDescription.Usage)) != 28
            || Marshal.OffsetOf<Texture2DDescription>(nameof(Texture2DDescription.MiscFlags)) != 40
            || Marshal.OffsetOf<MappedSubresource>(nameof(MappedSubresource.RowPitch)) != 8
            || Marshal.OffsetOf<MappedSubresource>(nameof(MappedSubresource.DepthPitch)) != 12
            || Marshal.OffsetOf<SubresourceData>(nameof(SubresourceData.RowPitch)) != 8
            || Marshal.OffsetOf<SubresourceData>(nameof(SubresourceData.SlicePitch)) != 12)
        {
            throw new InvalidOperationException("Only the checked 64-bit ABI layout is supported.");
        }
    }

    private static void ExpectArgumentFailure(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("Invalid readback shape must be rejected.");
    }
}
