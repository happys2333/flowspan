using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Flowspan.Platform;
using SkiaSharp;

namespace Flowspan.Desktop;

internal readonly record struct DesktopRemoteWindowJpegProfile(
    int ScaleNumerator,
    int ScaleDenominator,
    int Quality);

internal enum DesktopRemoteWindowJpegEncodingStatus
{
    Encoded,
    UnsupportedPixelFormat,
    InvalidPixelPlane,
    EncodingFailed,
    PayloadLimitExceeded,
}

internal sealed class DesktopRemoteWindowEncodedJpeg : IDisposable
{
    private byte[]? payload;
    private readonly int payloadLength;

    internal DesktopRemoteWindowEncodedJpeg(
        byte[] payload,
        int width,
        int height,
        DesktopRemoteWindowJpegProfile profile)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length is < 1 or > DesktopRemoteWindowJpegCodec.MaximumEncodedBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        this.payload = payload;
        payloadLength = payload.Length;
        Width = width;
        Height = height;
        Profile = profile;
    }

    public int Height { get; }

    public ReadOnlyMemory<byte> Payload => GetPayload();

    public int PayloadLength => payloadLength;

    public DesktopRemoteWindowJpegProfile Profile { get; }

    public int Quality => Profile.Quality;

    public int ScaleDenominator => Profile.ScaleDenominator;

    public int ScaleNumerator => Profile.ScaleNumerator;

    public int Width { get; }

    public void Dispose()
    {
        byte[]? owned = Interlocked.Exchange(ref payload, null);
        if (owned is not null)
        {
            CryptographicOperations.ZeroMemory(owned);
        }
    }

    public byte[] ExportPayload() => GetPayload().ToArray();

    public override string ToString() =>
        $"Remote Window JPEG ({Width}x{Height}, quality {Quality}, {PayloadLength} bytes)";

    private byte[] GetPayload() => Volatile.Read(ref payload)
        ?? throw new ObjectDisposedException(nameof(DesktopRemoteWindowEncodedJpeg));
}

internal sealed class DesktopRemoteWindowJpegEncodingResult
{
    private DesktopRemoteWindowJpegEncodingResult(
        DesktopRemoteWindowJpegEncodingStatus status,
        DesktopRemoteWindowEncodedJpeg? frame)
    {
        if ((status is DesktopRemoteWindowJpegEncodingStatus.Encoded) != (frame is not null))
        {
            throw new ArgumentException(
                "An encoded JPEG result must contain exactly one successful frame.",
                nameof(frame));
        }

        Status = status;
        Frame = frame;
    }

    public DesktopRemoteWindowEncodedJpeg? Frame { get; }

    public DesktopRemoteWindowJpegEncodingStatus Status { get; }

    public bool Succeeded => Status is DesktopRemoteWindowJpegEncodingStatus.Encoded;

    internal static DesktopRemoteWindowJpegEncodingResult Encoded(
        DesktopRemoteWindowEncodedJpeg frame) =>
        new(DesktopRemoteWindowJpegEncodingStatus.Encoded, frame);

    internal static DesktopRemoteWindowJpegEncodingResult Failed(
        DesktopRemoteWindowJpegEncodingStatus status)
    {
        if (status is DesktopRemoteWindowJpegEncodingStatus.Encoded)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return new DesktopRemoteWindowJpegEncodingResult(status, frame: null);
    }
}

internal enum DesktopRemoteWindowJpegDecodingStatus
{
    Decoded,
    EmptyPayload,
    PayloadLimitExceeded,
    InvalidData,
    UnsupportedFormat,
    MultipleFrames,
    UnsupportedOrientation,
    InvalidDimensions,
    PixelLimitExceeded,
    UnsupportedConversion,
    IncompleteInput,
}

internal sealed class DesktopRemoteWindowBgraFrame : IDisposable
{
    private byte[]? pixels;
    private readonly int pixelLength;
    private readonly NativeRemoteWindowPixelFormat pixelFormat;

    internal DesktopRemoteWindowBgraFrame(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        int stride = checked(width * 4);
        if (pixels.Length != checked(stride * height)
            || pixels.Length > DesktopRemoteWindowJpegCodec.MaximumDecodedBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(pixels));
        }

        this.pixels = pixels;
        pixelLength = pixels.Length;
        Width = width;
        Height = height;
        Stride = stride;
        pixelFormat = NativeRemoteWindowPixelFormat.Bgra8888;
    }

    public int Height { get; }

    public NativeRemoteWindowPixelFormat PixelFormat => pixelFormat;

    public ReadOnlyMemory<byte> Pixels => GetPixels();

    public int Stride { get; }

    public int Width { get; }

    public void Dispose()
    {
        byte[]? owned = Interlocked.Exchange(ref pixels, null);
        if (owned is not null)
        {
            CryptographicOperations.ZeroMemory(owned);
        }
    }

    public override string ToString() =>
        $"Decoded Remote Window frame ({Width}x{Height}, {pixelLength} BGRA bytes)";

    private byte[] GetPixels() => Volatile.Read(ref pixels)
        ?? throw new ObjectDisposedException(nameof(DesktopRemoteWindowBgraFrame));
}

internal sealed class DesktopRemoteWindowJpegDecodingResult
{
    private DesktopRemoteWindowJpegDecodingResult(
        DesktopRemoteWindowJpegDecodingStatus status,
        DesktopRemoteWindowBgraFrame? frame)
    {
        if ((status is DesktopRemoteWindowJpegDecodingStatus.Decoded) != (frame is not null))
        {
            throw new ArgumentException(
                "A decoded JPEG result must contain exactly one successful frame.",
                nameof(frame));
        }

        Status = status;
        Frame = frame;
    }

    public DesktopRemoteWindowBgraFrame? Frame { get; }

    public DesktopRemoteWindowJpegDecodingStatus Status { get; }

    public bool Succeeded => Status is DesktopRemoteWindowJpegDecodingStatus.Decoded;

    internal static DesktopRemoteWindowJpegDecodingResult Decoded(
        DesktopRemoteWindowBgraFrame frame) =>
        new(DesktopRemoteWindowJpegDecodingStatus.Decoded, frame);

    internal static DesktopRemoteWindowJpegDecodingResult Failed(
        DesktopRemoteWindowJpegDecodingStatus status)
    {
        if (status is DesktopRemoteWindowJpegDecodingStatus.Decoded)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return new DesktopRemoteWindowJpegDecodingResult(status, frame: null);
    }
}

internal static class DesktopRemoteWindowJpegCodec
{
    public const int MaximumDecodedBytes = 64 * 1024 * 1024;
    public const int MaximumDecodedPixels = 16_777_216;
    public const int MaximumEncodedBytes = 1024 * 1024;

    private static readonly IReadOnlyList<DesktopRemoteWindowJpegProfile>
        Profiles = Array.AsReadOnly<DesktopRemoteWindowJpegProfile>(
        [
            new(1, 1, 82),
            new(1, 1, 68),
            new(1, 1, 54),
            new(3, 4, 68),
            new(3, 4, 54),
            new(1, 2, 68),
            new(1, 2, 54),
        ]);

    private static readonly SKSamplingOptions ScaleSampling =
        new(SKFilterMode.Linear, SKMipmapMode.None);

    internal static IReadOnlyList<DesktopRemoteWindowJpegProfile>
        EncodingProfiles => Profiles;

    public static DesktopRemoteWindowJpegEncodingResult Encode(
        NativeRemoteWindowFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.PixelFormat is not NativeRemoteWindowPixelFormat.Bgra8888)
        {
            return DesktopRemoteWindowJpegEncodingResult.Failed(
                DesktopRemoteWindowJpegEncodingStatus.UnsupportedPixelFormat);
        }

        int tightStride = checked(frame.Width * 4);
        ReadOnlyMemory<byte> sourcePixels = frame.Pixels;
        if (frame.Stride < tightStride
            || sourcePixels.Length != checked(frame.Stride * frame.Height))
        {
            return DesktopRemoteWindowJpegEncodingResult.Failed(
                DesktopRemoteWindowJpegEncodingStatus.InvalidPixelPlane);
        }

        SKBitmap source = CreateOpaqueSourceBitmap(
            sourcePixels.Span,
            frame.Width,
            frame.Height,
            frame.Stride,
            tightStride);
        if (source.IsEmpty)
        {
            source.Dispose();
            return DesktopRemoteWindowJpegEncodingResult.Failed(
                DesktopRemoteWindowJpegEncodingStatus.EncodingFailed);
        }

        SKBitmap? scaled = null;
        int scaledWidth = 0;
        int scaledHeight = 0;
        try
        {
            return EncodeUsingProfiles(
                frame.Width,
                frame.Height,
                TryEncodeCandidate);
        }
        finally
        {
            try
            {
                ClearAndDisposeBitmap(scaled);
            }
            finally
            {
                ClearAndDisposeBitmap(source);
            }
        }

        DesktopRemoteWindowJpegEncodingResult TryEncodeCandidate(
            DesktopRemoteWindowJpegProfile profile,
            int candidateWidth,
            int candidateHeight)
        {
            SKBitmap candidate;
            if (candidateWidth == frame.Width && candidateHeight == frame.Height)
            {
                candidate = source;
            }
            else
            {
                if (scaled is null
                    || scaledWidth != candidateWidth
                    || scaledHeight != candidateHeight)
                {
                    SKBitmap? previous = scaled;
                    scaled = null;
                    ClearAndDisposeBitmap(previous);
                    scaled = CreateScaledBitmap(
                        source,
                        candidateWidth,
                        candidateHeight);
                    scaledWidth = candidateWidth;
                    scaledHeight = candidateHeight;
                }

                if (scaled.IsEmpty)
                {
                    return DesktopRemoteWindowJpegEncodingResult.Failed(
                        DesktopRemoteWindowJpegEncodingStatus.EncodingFailed);
                }

                candidate = scaled;
            }

            using var output = new BoundedSkiaWriteStream(MaximumEncodedBytes);
            bool encoded;
            try
            {
                encoded = candidate.Encode(
                    output,
                    SKEncodedImageFormat.Jpeg,
                    profile.Quality);
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException)
            {
                return DesktopRemoteWindowJpegEncodingResult.Failed(
                    DesktopRemoteWindowJpegEncodingStatus.EncodingFailed);
            }

            if (output.ExceededCapacity)
            {
                return DesktopRemoteWindowJpegEncodingResult.Failed(
                    DesktopRemoteWindowJpegEncodingStatus.PayloadLimitExceeded);
            }

            if (output.WriteFailed
                || !encoded
                || output.BoundedBytesWritten == 0)
            {
                return DesktopRemoteWindowJpegEncodingResult.Failed(
                    DesktopRemoteWindowJpegEncodingStatus.EncodingFailed);
            }

            return DesktopRemoteWindowJpegEncodingResult.Encoded(
                new DesktopRemoteWindowEncodedJpeg(
                    output.ExportPayload(),
                    candidateWidth,
                    candidateHeight,
                    profile));
        }
    }

    internal static DesktopRemoteWindowJpegEncodingResult EncodeUsingProfiles(
        int sourceWidth,
        int sourceHeight,
        Func<
            DesktopRemoteWindowJpegProfile,
            int,
            int,
            DesktopRemoteWindowJpegEncodingResult> tryEncodeCandidate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceHeight, 1);
        ArgumentNullException.ThrowIfNull(tryEncodeCandidate);

        bool exceededPayloadLimit = false;
        bool failedToEncode = false;
        foreach (DesktopRemoteWindowJpegProfile profile in Profiles)
        {
            int candidateWidth = GetScaledDimension(sourceWidth, profile);
            int candidateHeight = GetScaledDimension(sourceHeight, profile);
            DesktopRemoteWindowJpegEncodingResult result = tryEncodeCandidate(
                profile,
                candidateWidth,
                candidateHeight);
            ArgumentNullException.ThrowIfNull(result);
            if (result.Succeeded)
            {
                DesktopRemoteWindowEncodedJpeg frame = result.Frame!;
                if (frame.Profile != profile
                    || frame.Width != candidateWidth
                    || frame.Height != candidateHeight)
                {
                    throw new InvalidOperationException(
                        "A successful JPEG candidate must describe the attempted profile.");
                }

                return result;
            }

            exceededPayloadLimit |= result.Status is
                DesktopRemoteWindowJpegEncodingStatus.PayloadLimitExceeded;
            failedToEncode |= result.Status is not
                DesktopRemoteWindowJpegEncodingStatus.PayloadLimitExceeded;
        }

        return DesktopRemoteWindowJpegEncodingResult.Failed(
            failedToEncode || !exceededPayloadLimit
                ? DesktopRemoteWindowJpegEncodingStatus.EncodingFailed
                : DesktopRemoteWindowJpegEncodingStatus.PayloadLimitExceeded);
    }

    public static DesktopRemoteWindowJpegDecodingResult Decode(
        ReadOnlyMemory<byte> encoded)
    {
        if (encoded.IsEmpty)
        {
            return DesktopRemoteWindowJpegDecodingResult.Failed(
                DesktopRemoteWindowJpegDecodingStatus.EmptyPayload);
        }

        if (encoded.Length > MaximumEncodedBytes)
        {
            return DesktopRemoteWindowJpegDecodingResult.Failed(
                DesktopRemoteWindowJpegDecodingStatus.PayloadLimitExceeded);
        }

        byte[] stableEncoded = encoded.ToArray();
        try
        {
            return DecodeStableEncoded(stableEncoded);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(stableEncoded);
        }
    }

    private static DesktopRemoteWindowJpegDecodingResult DecodeStableEncoded(
        byte[] stableEncoded)
    {
        if (stableEncoded.Length < 2
            || stableEncoded[0] != 0xff
            || stableEncoded[1] != 0xd8)
        {
            return DesktopRemoteWindowJpegDecodingResult.Failed(
                DesktopRemoteWindowJpegDecodingStatus.UnsupportedFormat);
        }

        DesktopRemoteWindowJpegDecodingStatus envelopeStatus =
            ValidateJpegEnvelope(stableEncoded);
        if (envelopeStatus is not DesktopRemoteWindowJpegDecodingStatus.Decoded)
        {
            return DesktopRemoteWindowJpegDecodingResult.Failed(envelopeStatus);
        }

        using SKData data = SKData.CreateCopy(stableEncoded);
        try
        {
            return DecodeSkiaData(data);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data.Span);
        }
    }

    private static DesktopRemoteWindowJpegDecodingResult DecodeSkiaData(
        SKData data)
    {
        SKCodec? codec;
        try
        {
            codec = SKCodec.Create(data);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return DesktopRemoteWindowJpegDecodingResult.Failed(
                DesktopRemoteWindowJpegDecodingStatus.InvalidData);
        }

        if (codec is null)
        {
            return DesktopRemoteWindowJpegDecodingResult.Failed(
                DesktopRemoteWindowJpegDecodingStatus.InvalidData);
        }

        using (codec)
        {
            if (codec.EncodedFormat is not SKEncodedImageFormat.Jpeg)
            {
                return DesktopRemoteWindowJpegDecodingResult.Failed(
                    DesktopRemoteWindowJpegDecodingStatus.UnsupportedFormat);
            }

            SKImageInfo sourceInfo = codec.Info;
            DesktopRemoteWindowJpegDecodingStatus metadataStatus =
                ValidateMetadata(
                    codec.FrameCount,
                    codec.EncodedOrigin is SKEncodedOrigin.TopLeft,
                    sourceInfo.Width,
                    sourceInfo.Height);
            if (metadataStatus is not DesktopRemoteWindowJpegDecodingStatus.Decoded)
            {
                return DesktopRemoteWindowJpegDecodingResult.Failed(
                    metadataStatus);
            }

            int stride = checked(sourceInfo.Width * 4);
            int decodedByteCount = checked(stride * sourceInfo.Height);
            var targetInfo = new SKImageInfo(
                sourceInfo.Width,
                sourceInfo.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Opaque);
            if (targetInfo.RowBytes != stride
                || targetInfo.BytesSize != decodedByteCount)
            {
                return DesktopRemoteWindowJpegDecodingResult.Failed(
                    DesktopRemoteWindowJpegDecodingStatus.UnsupportedConversion);
            }

            byte[] pixels = GC.AllocateUninitializedArray<byte>(decodedByteCount);
            bool transferredPixels = false;
            try
            {
                SKCodecResult result;
                try
                {
                    result = codec.GetPixels(targetInfo, pixels);
                }
                catch (Exception exception) when (
                    exception is ArgumentException or InvalidOperationException)
                {
                    return DesktopRemoteWindowJpegDecodingResult.Failed(
                        DesktopRemoteWindowJpegDecodingStatus.InvalidData);
                }

                if (result is not SKCodecResult.Success)
                {
                    return DesktopRemoteWindowJpegDecodingResult.Failed(
                        MapDecodeFailure(result));
                }

                DiscardAlpha(pixels);
                DesktopRemoteWindowBgraFrame frame = new(
                    pixels,
                    sourceInfo.Width,
                    sourceInfo.Height);
                DesktopRemoteWindowJpegDecodingResult decoded =
                    DesktopRemoteWindowJpegDecodingResult.Decoded(frame);
                transferredPixels = true;
                return decoded;
            }
            finally
            {
                if (!transferredPixels)
                {
                    CryptographicOperations.ZeroMemory(pixels);
                }
            }
        }
    }

    internal static DesktopRemoteWindowJpegDecodingStatus ValidateMetadata(
        int frameCount,
        bool topLeft,
        int width,
        int height)
    {
        if (frameCount != 0)
        {
            return DesktopRemoteWindowJpegDecodingStatus.MultipleFrames;
        }

        if (!topLeft)
        {
            return DesktopRemoteWindowJpegDecodingStatus.UnsupportedOrientation;
        }

        if (width is < 1 or > NativeRemoteWindowFrame.MaximumDimension
            || height is < 1 or > NativeRemoteWindowFrame.MaximumDimension)
        {
            return DesktopRemoteWindowJpegDecodingStatus.InvalidDimensions;
        }

        long pixelCount = checked((long)width * height);
        if (pixelCount > MaximumDecodedPixels)
        {
            return DesktopRemoteWindowJpegDecodingStatus.PixelLimitExceeded;
        }

        return DesktopRemoteWindowJpegDecodingStatus.Decoded;
    }

    private static SKBitmap CreateOpaqueSourceBitmap(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        int sourceStride,
        int tightStride)
    {
        var info = new SKImageInfo(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Opaque);
        var bitmap = new SKBitmap(info, tightStride);
        bool transferred = false;
        try
        {
            Span<byte> destination = bitmap.GetPixelSpan();
            if (destination.Length < checked(tightStride * height))
            {
                return new SKBitmap();
            }

            for (int row = 0; row < height; row++)
            {
                source.Slice(row * sourceStride, tightStride).CopyTo(
                    destination.Slice(row * tightStride, tightStride));
            }

            DiscardAlpha(destination[..checked(tightStride * height)]);
            transferred = true;
            return bitmap;
        }
        finally
        {
            if (!transferred)
            {
                ClearAndDisposeBitmap(bitmap);
            }
        }
    }

    private static SKBitmap CreateScaledBitmap(
        SKBitmap source,
        int width,
        int height)
    {
        var info = new SKImageInfo(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Opaque);
        var scaled = new SKBitmap(info, checked(width * 4));
        bool transferred = false;
        try
        {
            if (!source.ScalePixels(scaled, ScaleSampling))
            {
                return new SKBitmap();
            }

            transferred = true;
            return scaled;
        }
        finally
        {
            if (!transferred)
            {
                ClearAndDisposeBitmap(scaled);
            }
        }
    }

    private static void DiscardAlpha(Span<byte> pixels)
    {
        for (int index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = byte.MaxValue;
        }
    }

    private static void ClearAndDisposeBitmap(SKBitmap? bitmap)
    {
        if (bitmap is null)
        {
            return;
        }

        try
        {
            CryptographicOperations.ZeroMemory(bitmap.GetPixelSpan());
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    internal static int GetScaledDimension(
        int dimension,
        DesktopRemoteWindowJpegProfile profile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            profile.ScaleNumerator,
            1,
            nameof(profile));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            profile.ScaleDenominator,
            1,
            nameof(profile));
        if (profile.ScaleNumerator > profile.ScaleDenominator)
        {
            throw new ArgumentOutOfRangeException(
                nameof(profile),
                "A Remote Window JPEG profile cannot enlarge a frame.");
        }

        return Math.Max(
            1,
            checked(dimension * profile.ScaleNumerator)
                / profile.ScaleDenominator);
    }

    private static DesktopRemoteWindowJpegDecodingStatus MapDecodeFailure(
        SKCodecResult result) => result switch
        {
            SKCodecResult.IncompleteInput =>
                DesktopRemoteWindowJpegDecodingStatus.IncompleteInput,
            SKCodecResult.InvalidConversion
                or SKCodecResult.InvalidScale
                or SKCodecResult.Unimplemented =>
                DesktopRemoteWindowJpegDecodingStatus.UnsupportedConversion,
            _ => DesktopRemoteWindowJpegDecodingStatus.InvalidData,
        };

    internal static DesktopRemoteWindowJpegDecodingStatus ValidateJpegEnvelope(
        ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length < 4 || encoded[0] != 0xff || encoded[1] != 0xd8)
        {
            return DesktopRemoteWindowJpegDecodingStatus.InvalidData;
        }

        int offset = 2;
        bool insideScan = false;
        bool sawScan = false;
        while (offset < encoded.Length)
        {
            byte marker = 0;
            if (insideScan)
            {
                bool foundMarker = false;
                while (offset < encoded.Length)
                {
                    if (encoded[offset++] != 0xff)
                    {
                        continue;
                    }

                    while (offset < encoded.Length && encoded[offset] == 0xff)
                    {
                        offset++;
                    }

                    if (offset >= encoded.Length)
                    {
                        return DesktopRemoteWindowJpegDecodingStatus.IncompleteInput;
                    }

                    marker = encoded[offset++];
                    if (marker == 0x00 || marker is >= 0xd0 and <= 0xd7)
                    {
                        continue;
                    }

                    foundMarker = true;
                    break;
                }

                if (!foundMarker)
                {
                    return DesktopRemoteWindowJpegDecodingStatus.IncompleteInput;
                }
            }
            else
            {
                if (encoded[offset++] != 0xff)
                {
                    return DesktopRemoteWindowJpegDecodingStatus.InvalidData;
                }

                while (offset < encoded.Length && encoded[offset] == 0xff)
                {
                    offset++;
                }

                if (offset >= encoded.Length)
                {
                    return DesktopRemoteWindowJpegDecodingStatus.IncompleteInput;
                }

                marker = encoded[offset++];
                if (marker == 0x00 || marker is >= 0xd0 and <= 0xd7)
                {
                    return DesktopRemoteWindowJpegDecodingStatus.InvalidData;
                }
            }

            if (marker == 0xd9)
            {
                if (!sawScan)
                {
                    return DesktopRemoteWindowJpegDecodingStatus.InvalidData;
                }

                return offset == encoded.Length
                    ? DesktopRemoteWindowJpegDecodingStatus.Decoded
                    : DesktopRemoteWindowJpegDecodingStatus.InvalidData;
            }

            if (marker is 0xd8 or 0x01)
            {
                return DesktopRemoteWindowJpegDecodingStatus.InvalidData;
            }

            DesktopRemoteWindowJpegDecodingStatus segmentStatus =
                SkipJpegSegment(encoded, ref offset);
            if (segmentStatus is not DesktopRemoteWindowJpegDecodingStatus.Decoded)
            {
                return segmentStatus;
            }

            insideScan = marker == 0xda;
            sawScan |= insideScan;
        }

        return DesktopRemoteWindowJpegDecodingStatus.IncompleteInput;
    }

    private static DesktopRemoteWindowJpegDecodingStatus SkipJpegSegment(
        ReadOnlySpan<byte> encoded,
        ref int offset)
    {
        if (encoded.Length - offset < 2)
        {
            return DesktopRemoteWindowJpegDecodingStatus.IncompleteInput;
        }

        int segmentLength = (encoded[offset] << 8) | encoded[offset + 1];
        if (segmentLength < 2)
        {
            return DesktopRemoteWindowJpegDecodingStatus.InvalidData;
        }

        if (segmentLength > encoded.Length - offset)
        {
            return DesktopRemoteWindowJpegDecodingStatus.IncompleteInput;
        }

        offset += segmentLength;
        return DesktopRemoteWindowJpegDecodingStatus.Decoded;
    }

    private sealed class BoundedSkiaWriteStream(int capacity) :
        SKAbstractManagedWStream
    {
        private byte[]? buffer = ArrayPool<byte>.Shared.Rent(capacity);
        private int bytesWritten;

        public int BoundedBytesWritten => bytesWritten;

        public bool ExceededCapacity { get; private set; }

        public bool WriteFailed { get; private set; }

        public byte[] ExportPayload()
        {
            if (ExceededCapacity
                || bytesWritten < 1
                || bytesWritten > capacity)
            {
                throw new InvalidOperationException(
                    "A failed bounded JPEG write has no exportable payload.");
            }

            byte[] current = Volatile.Read(ref buffer)
                ?? throw new ObjectDisposedException(nameof(BoundedSkiaWriteStream));
            return current.AsSpan(0, bytesWritten).ToArray();
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                base.Dispose(disposing);
            }
            finally
            {
                byte[]? owned = Interlocked.Exchange(ref buffer, null);
                if (owned is not null)
                {
                    ArrayPool<byte>.Shared.Return(owned, clearArray: true);
                }
            }
        }

        protected override IntPtr OnBytesWritten() => new(bytesWritten);

        protected override void OnFlush()
        {
        }

        protected override bool OnWrite(IntPtr source, IntPtr size)
        {
            long requested = size.ToInt64();
            if (requested < 0 || (requested > 0 && source == IntPtr.Zero))
            {
                WriteFailed = true;
                return false;
            }

            if (requested > capacity - bytesWritten)
            {
                ExceededCapacity = true;
                return false;
            }

            int count = checked((int)requested);
            if (count > 0)
            {
                byte[]? current = Volatile.Read(ref buffer);
                if (current is null)
                {
                    WriteFailed = true;
                    return false;
                }

                Marshal.Copy(source, current, bytesWritten, count);
                bytesWritten += count;
            }

            return true;
        }
    }
}
