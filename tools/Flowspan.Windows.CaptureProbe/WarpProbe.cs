using System.Diagnostics;
using System.Security.Cryptography;

namespace Flowspan.Windows.CaptureProbe;

internal sealed record WarpProbeResult(
    string Sha256,
    uint RowPitch,
    uint FeatureLevel,
    int OwnedReferences,
    int ReleasedReferences,
    int MapAttempts);

internal static unsafe class WarpProbe
{
    public static WarpProbeResult Run()
    {
        if (!OperatingSystem.IsWindows()
            || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                != System.Runtime.InteropServices.Architecture.X64)
        {
            throw new PlatformNotSupportedException();
        }

        ProbeSelfTests.VerifyAbiLayout();
        nint device = 0;
        nint context = 0;
        nint source = 0;
        nint staging = 0;
        int ownedReferences = 0;
        int releasedReferences = 0;
        bool mapped = false;
        bool initialized = false;
        byte[] sourcePixels = KnownPixels.CreateSource();
        byte[]? packed = null;
        string? hash = null;
        uint rowPitch = 0;
        uint featureLevel = 0;
        int mapAttempts = 0;
        try
        {
            int initialization = D3D11Native.RoInitialize(1); // RO_INIT_MULTITHREADED
            RequireSuccess(initialization, "mta_initialize_failed");
            initialized = true;
            int createDevice = D3D11Native.D3D11CreateDevice(
                adapter: 0,
                D3D11Native.DriverTypeWarp,
                software: 0,
                D3D11Native.CreateDeviceBgraSupport,
                featureLevels: 0,
                featureLevelCount: 0,
                D3D11Native.SdkVersion,
                out device,
                out featureLevel,
                out context);
            ownedReferences += Count(device) + Count(context);
            RequireSuccess(createDevice, "warp_device_create_failed");
            RequirePointer(device);
            RequirePointer(context);

            Texture2DDescription sourceDescription = Description(
                KnownPixels.SourceWidth, KnownPixels.SourceHeight,
                D3D11Native.UsageDefault, cpuAccessFlags: 0);
            fixed (byte* sourceData = sourcePixels)
            {
                SubresourceData initialData = new()
                {
                    Data = (nint)sourceData,
                    RowPitch = KnownPixels.SourceWidth * 4,
                    SlicePitch = 0,
                };
                int createSource = D3D11Native.CreateTexture2D(
                    device, &sourceDescription, &initialData, &source);
                ownedReferences += Count(source);
                RequireSuccess(createSource, "source_texture_create_failed");
                RequirePointer(source);
            }

            Texture2DDescription actualSource = default;
            D3D11Native.GetDescription(source, &actualSource);
            VerifyDescription(actualSource, sourceDescription);

            Texture2DDescription stagingDescription = Description(
                KnownPixels.ContentWidth, KnownPixels.ContentHeight,
                D3D11Native.UsageStaging, D3D11Native.CpuAccessRead);
            int createStaging = D3D11Native.CreateTexture2D(
                device, &stagingDescription, null, &staging);
            ownedReferences += Count(staging);
            RequireSuccess(createStaging, "staging_texture_create_failed");
            RequirePointer(staging);
            Texture2DDescription actualStaging = default;
            D3D11Native.GetDescription(staging, &actualStaging);
            VerifyDescription(actualStaging, stagingDescription);

            TextureBox content = new()
            {
                Right = KnownPixels.ContentWidth,
                Bottom = KnownPixels.ContentHeight,
                Back = 1,
            };
            D3D11Native.CopySubresourceRegion(context, staging, source, &content);
            // Submit the copy before bounded nonblocking Map polling. Flush
            // submits commands; it does not itself confirm GPU completion.
            D3D11Native.Flush(context);

            MappedSubresource readback = default;
            long started = Stopwatch.GetTimestamp();
            while (true)
            {
                mapAttempts++;
                int mapResult = D3D11Native.Map(context, staging, &readback);
                if (mapResult == D3D11Native.WasStillDrawing)
                {
                    if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(2))
                    {
                        throw new ProbeFailureException("readback_not_ready");
                    }

                    Thread.Sleep(1);
                    continue;
                }

                RequireSuccess(mapResult, "staging_map_failed");
                mapped = true;
                break;
            }

            RequirePointer(readback.Data);
            rowPitch = readback.RowPitch;
            if (rowPitch < KnownPixels.ContentWidth * 4
                || rowPitch > BgraReadback.MaximumBytes / KnownPixels.ContentHeight)
            {
                throw new ProbeFailureException("invalid_row_pitch");
            }

            int nativeLength = checked(
                ((KnownPixels.ContentHeight - 1) * (int)rowPitch)
                + (KnownPixels.ContentWidth * 4));
            packed = BgraReadback.CopyRows(
                new ReadOnlySpan<byte>((void*)readback.Data, nativeLength),
                KnownPixels.ContentWidth,
                KnownPixels.ContentHeight,
                checked((int)rowPitch));
            hash = KnownPixels.Hash(packed);
            if (hash != KnownPixels.ExpectedSha256)
            {
                throw new ProbeFailureException("known_pixels_mismatch");
            }
        }
        finally
        {
            if (mapped)
            {
                D3D11Native.Unmap(context, staging);
            }

            if (packed is not null)
            {
                CryptographicOperations.ZeroMemory(packed);
            }

            CryptographicOperations.ZeroMemory(sourcePixels);
            Release(ref staging, ref releasedReferences);
            Release(ref source, ref releasedReferences);
            Release(ref context, ref releasedReferences);
            Release(ref device, ref releasedReferences);
            if (initialized)
            {
                D3D11Native.RoUninitialize();
            }
        }

        if (ownedReferences != releasedReferences || hash is null)
        {
            throw new ProbeFailureException("owned_reference_imbalance");
        }

        return new WarpProbeResult(
            hash, rowPitch, featureLevel,
            ownedReferences, releasedReferences, mapAttempts);
    }

    private static Texture2DDescription Description(
        uint width, uint height, uint usage, uint cpuAccessFlags) => new()
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = D3D11Native.FormatBgra8Unorm,
            SampleCount = 1,
            Usage = usage,
            CpuAccessFlags = cpuAccessFlags,
        };

    private static void VerifyDescription(
        Texture2DDescription actual, Texture2DDescription expected)
    {
        if (actual.Width != expected.Width || actual.Height != expected.Height
            || actual.MipLevels != 1 || actual.ArraySize != 1
            || actual.Format != D3D11Native.FormatBgra8Unorm
            || actual.SampleCount != 1 || actual.SampleQuality != 0
            || actual.Usage != expected.Usage || actual.BindFlags != 0
            || actual.CpuAccessFlags != expected.CpuAccessFlags
            || actual.MiscFlags != 0)
        {
            throw new ProbeFailureException("texture_description_mismatch");
        }
    }

    private static void RequireSuccess(int result, string reason)
    {
        if (result < 0)
        {
            throw new ProbeFailureException(reason, result);
        }
    }

    private static void RequirePointer(nint value)
    {
        if (value == 0)
        {
            throw new ProbeFailureException("missing_native_owner");
        }
    }

    private static int Count(nint value) => value == 0 ? 0 : 1;

    private static void Release(ref nint value, ref int releases)
    {
        nint owned = value;
        value = 0;
        if (owned != 0)
        {
            D3D11Native.Release(owned);
            releases++;
        }
    }
}

internal sealed class ProbeFailureException : Exception
{
    public ProbeFailureException(string reason, int nativeResult = 0)
        : base(reason)
    {
        Reason = reason;
        NativeResult = nativeResult;
    }

    public string Reason { get; }
    public int NativeResult { get; }
}
