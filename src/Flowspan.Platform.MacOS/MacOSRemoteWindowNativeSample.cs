using System.Buffers;
using System.Security.Cryptography;
using N = Flowspan.Platform.MacOS.MacOSRemoteWindowObjectiveCInterop;

namespace Flowspan.Platform.MacOS;

internal sealed unsafe class MacOSRemoteWindowNativeSample :
    IMacOSRemoteWindowNativeSample
{
    private const uint Bgra8888 = 0x42475241;
    private const nuint ReadOnlyLock = 1;

    private readonly object gate = new();
    private readonly int expectedWidth;
    private readonly int expectedHeight;
    private nint sample;

    public MacOSRemoteWindowSampleRetention Retention { get; } = new();

    // The caller transfers one already-retained CMSampleBuffer reference. Even
    // an invalid geometry retains that ownership until Dispose.
    internal MacOSRemoteWindowNativeSample(
        nint retainedSample,
        int expectedWidth,
        int expectedHeight)
    {
        sample = retainedSample;
        this.expectedWidth = expectedWidth;
        this.expectedHeight = expectedHeight;
    }

    public bool TryCopyPixels(out MacOSRemoteWindowPixelBuffer? pixels)
    {
        pixels = null;
        lock (gate)
        {
            if (sample == 0
                || expectedWidth is < 1 or > NativeRemoteWindowFrame.MaximumDimension
                || expectedHeight is < 1 or > NativeRemoteWindowFrame.MaximumDimension
                || (long)expectedWidth * expectedHeight * 4
                    > NativeRemoteWindowFrame.MaximumPayloadBytes
                || N.CMSampleBufferIsValid(sample) == 0
                || N.CMSampleBufferDataIsReady(sample) == 0)
            {
                return false;
            }

            nint attachments = N.CMSampleBufferGetSampleAttachmentsArray(sample, 0);
            nint statusKey = N.FrameStatusKey;
            if (attachments == 0 || statusKey == 0 || N.CFArrayGetCount(attachments) < 1)
            {
                return false;
            }

            nint attachment = N.CFArrayGetValueAtIndex(attachments, 0);
            if (attachment == 0)
            {
                return false;
            }

            nint status = N.CFDictionaryGetValue(attachment, statusKey);
            if (status == 0 || N.GetNInt(status, N.Sel("integerValue")) != 0)
            {
                return false;
            }

            nint image = N.CMSampleBufferGetImageBuffer(sample);
            if (image == 0
                || N.CVPixelBufferGetPixelFormatType(image) != Bgra8888
                || N.CVPixelBufferIsPlanar(image) != 0
                || N.CVPixelBufferGetWidth(image) != (nuint)expectedWidth
                || N.CVPixelBufferGetHeight(image) != (nuint)expectedHeight)
            {
                return false;
            }

            int packedStride = expectedWidth * 4;
            int payloadLength = packedStride * expectedHeight;
            nuint nativeStride = N.CVPixelBufferGetBytesPerRow(image);
            nuint nativeDataSize = N.CVPixelBufferGetDataSize(image);
            if (nativeStride < (nuint)packedStride
                || nativeStride > NativeRemoteWindowFrame.MaximumPayloadBytes
                || nativeDataSize > NativeRemoteWindowFrame.MaximumPayloadBytes
                || (ulong)nativeStride * (uint)expectedHeight > nativeDataSize)
            {
                return false;
            }

            if (N.CVPixelBufferLockBaseAddress(image, ReadOnlyLock) != 0)
            {
                return false;
            }

            ClearingPixelOwner? owner = null;
            try
            {
                bool unlocked = false;
                try
                {
                    nint baseAddress = N.CVPixelBufferGetBaseAddress(image);
                    if (baseAddress == 0)
                    {
                        return false;
                    }

                    owner = new ClearingPixelOwner(payloadLength);
                    Span<byte> destination = owner.Memory.Span;
                    int rowStride = (int)nativeStride;
                    for (int row = 0; row < expectedHeight; row++)
                    {
                        var source = new ReadOnlySpan<byte>(
                            (byte*)baseAddress + (row * rowStride),
                            packedStride);
                        source.CopyTo(destination.Slice(
                            row * packedStride,
                            packedStride));
                    }
                }
                finally
                {
                    unlocked = N.CVPixelBufferUnlockBaseAddress(
                        image,
                        ReadOnlyLock) == 0;
                }

                if (!unlocked)
                {
                    return false;
                }

                pixels = new MacOSRemoteWindowPixelBuffer(
                    owner,
                    payloadLength,
                    expectedWidth,
                    expectedHeight,
                    packedStride);
                owner = null;
                return true;
            }
            finally
            {
                owner?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            nint retainedSample = sample;
            if (retainedSample != 0)
            {
                N.CFRelease(retainedSample);
                sample = 0;
            }
        }
    }

    private sealed class ClearingPixelOwner : IMemoryOwner<byte>
    {
        private readonly int length;
        private byte[]? buffer;

        internal ClearingPixelOwner(int length)
        {
            this.length = length;
            buffer = ArrayPool<byte>.Shared.Rent(length);
        }

        public Memory<byte> Memory
        {
            get
            {
                byte[]? current = Volatile.Read(ref buffer);
                ObjectDisposedException.ThrowIf(current is null, this);
                return current.AsMemory(0, length);
            }
        }

        public void Dispose()
        {
            byte[]? rented = Interlocked.Exchange(ref buffer, null);
            if (rented is not null)
            {
                CryptographicOperations.ZeroMemory(rented);
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }
    }
}
