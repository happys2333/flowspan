using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Flowspan.Windows.CaptureProbe;

internal readonly record struct WgcFrameGeometry(
    int SurfaceWidth, int SurfaceHeight, int ContentWidth, int ContentHeight);

internal sealed class WgcProbeAdmission
{
    private readonly object gate = new();
    private int open = 1;

    public bool IsOpen => Volatile.Read(ref open) != 0;

    public void Stop()
    {
        lock (gate) { Volatile.Write(ref open, 0); }
    }

    // Commit contains only bounded managed metadata assignment, never native
    // work. It linearizes successful frame evidence against WM_CLOSE/stop.
    public void Commit(Action commit)
    {
        lock (gate)
        {
            if (open == 0) { throw new ProbeFailureException("capture_stopped"); }
            commit();
        }
    }
}

// The fake in portable tests replaces only the native borrowed-frame boundary.
internal interface IWgcBorrowedFrame : IDisposable
{
    public WgcFrameGeometry Geometry { get; }

    public byte[] CopyContent();
}

internal static class WgcOwnedFrameReader
{
    public static byte[] Read(IWgcBorrowedFrame borrowed, Func<bool>? admissionOpen = null)
    {
        byte[]? packed = null;
        try
        {
            using (borrowed)
            {
                RequireOpen(admissionOpen);
                WgcFrameGeometry shape = borrowed.Geometry;
                if (shape.ContentWidth < 1 || shape.ContentHeight < 1
                    || shape.SurfaceWidth < shape.ContentWidth
                    || shape.SurfaceHeight < shape.ContentHeight
                    || shape.SurfaceWidth > BgraReadback.MaximumDimension
                    || shape.SurfaceHeight > BgraReadback.MaximumDimension
                    || (long)shape.ContentWidth * shape.ContentHeight * 4 > BgraReadback.MaximumBytes)
                {
                    throw new ProbeFailureException("invalid_frame_geometry");
                }

                RequireOpen(admissionOpen);
                packed = borrowed.CopyContent();
                if (packed.Length != checked(shape.ContentWidth * shape.ContentHeight * 4))
                {
                    throw new ProbeFailureException("invalid_packed_length");
                }

                RequireOpen(admissionOpen);
            }

            RequireOpen(admissionOpen);
            byte[] result = packed;
            packed = null;
            return result;
        }
        finally
        {
            if (packed is not null)
            {
                CryptographicOperations.ZeroMemory(packed);
            }
        }
    }

    private static void RequireOpen(Func<bool>? admissionOpen)
    {
        if (admissionOpen is not null && !admissionOpen())
        {
            throw new ProbeFailureException("capture_stopped");
        }
    }
}

internal sealed record WgcProbeResult(
    bool Passed, string Reason, string? FrameHash, string? MarkerHash,
    int Width, int Height, uint RowPitch, uint FeatureLevel,
    int Frames, int OwnedReferences, int ReleasedReferences, bool CaptureStarted,
    string SecondaryWindowsPolicy, int ApiResult);

internal sealed class WgcProbeState
{
    public bool NativeCleanupConfirmed { get; set; }

    public bool CaptureStarted { get; set; }

    public int FramesReceived { get; set; }
}

internal static unsafe class WgcSelfWindowProbe
{
    public const string ExpectedMarkerHash = "c836b97f55a9b91993869d4040438f5975519655fa455f07819951eb1373e54e";

    public static WgcProbeResult Run(SelfWindow window, WgcProbeState state)
    {
        ProbeSelfTests.VerifyAbiLayout();
        WgcProbeSelfTests.VerifyAbiLayout();
        WgcComOwners owners = new(new NativeWgcComApi());
        bool initialized = false;
        nint pool = 0;
        nint session = 0;
        bool captureStarted = false;
        bool passed = false;
        string reason = "no_frames_before_deadline";
        string? frameHash = null;
        string? markerHash = null;
        int width = 0;
        int height = 0;
        uint rowPitch = 0;
        uint featureLevel = 0;
        int frames = 0;
        string secondaryWindowsPolicy = "not_created";
        int apiResult = 0;
        try
        {
            WgcNative.Require(D3D11Native.RoInitialize(1), "mta_initialize_failed");
            initialized = true;
            nint statics = WgcNative.ActivationFactory(
                "Windows.Graphics.Capture.GraphicsCaptureSession", WgcNative.SessionStaticsIid, owners);
            byte supported = 0;
            WgcNative.Require(WgcNative.IsSupported(statics, &supported), "is_supported_failed");
            if (supported == 0) { reason = "wgc_not_supported"; }
            else
            {
                nint itemInterop = WgcNative.ActivationFactory(
                    "Windows.Graphics.Capture.GraphicsCaptureItem", WgcNative.ItemInteropIid, owners);
                nint item = 0;
                int itemResult = WgcNative.CreateForWindow(itemInterop, window.Handle, &item);
                owners.Own(item);
                WgcNative.Require(itemResult, "owned_window_item_create_failed");
                WgcNative.RequirePointer(item);
                SizeInt32 size = default;
                WgcNative.Require(WgcNative.ItemSize(item, &size), "item_size_failed");
                if (size.Width != 64 || size.Height != 64)
                {
                    throw new ProbeFailureException("owned_window_size_mismatch");
                }

                int deviceResult = D3D11Native.D3D11CreateDevice(
                    0, D3D11Native.DriverTypeWarp, 0, D3D11Native.CreateDeviceBgraSupport,
                    0, 0, D3D11Native.SdkVersion, out nint device, out featureLevel, out nint context);
                owners.Own(device);
                owners.Own(context);
                WgcNative.Require(deviceResult, "warp_device_create_failed");
                WgcNative.RequirePointer(device);
                WgcNative.RequirePointer(context);
                nint dxgi = WgcNative.QueryOwned(device, WgcNative.DxgiDeviceIid, owners);
                int bridgeResult = WgcNative.CreateDirect3D11DeviceFromDXGIDevice(dxgi, out nint inspectable);
                owners.Own(inspectable);
                WgcNative.Require(bridgeResult, "winrt_device_bridge_failed");
                WgcNative.RequirePointer(inspectable);
                nint winrtDevice = WgcNative.QueryOwned(inspectable, WgcNative.WinrtDeviceIid, owners);
                nint poolStatics = WgcNative.ActivationFactory(
                    "Windows.Graphics.Capture.Direct3D11CaptureFramePool", WgcNative.PoolStatics2Iid, owners);
                int poolResult = WgcNative.CreatePool(poolStatics, winrtDevice, size, &pool);
                owners.Own(pool);
                WgcNative.Require(poolResult, "frame_pool_create_failed");
                WgcNative.RequirePointer(pool);
                int sessionResult = WgcNative.CreateSession(pool, item, &session);
                owners.Own(session);
                WgcNative.Require(sessionResult, "capture_session_create_failed");
                WgcNative.RequirePointer(session);
                nint session2 = WgcNative.QueryOwned(session, WgcNative.Session2Iid, owners);
                WgcNative.Require(WgcNative.SetCursor(session2, 0), "cursor_disable_failed");
                int secondaryQuery = WgcNative.QueryInterface(session, WgcNative.Session6Iid, out nint session6);
                owners.Own(session6);
                if (secondaryQuery == WgcNative.NoInterface)
                {
                    // The older public API's documented default is false.
                    secondaryWindowsPolicy = "documented_default_false";
                }
                else
                {
                    WgcNative.Require(secondaryQuery, "secondary_windows_query_failed");
                    WgcNative.RequirePointer(session6);
                    WgcNative.Require(WgcNative.SetSecondaryWindows(session6, 0), "secondary_windows_disable_failed");
                    secondaryWindowsPolicy = "explicit_false";
                }

                if (!window.AdmissionOpen) { throw new ProbeFailureException("capture_stopped"); }
                WgcNative.Require(WgcNative.Start(session), "capture_start_failed");
                captureStarted = true;
                state.CaptureStarted = true;
                long started = Stopwatch.GetTimestamp();
                while (window.AdmissionOpen
                    && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(8))
                {
                    nint frame = 0;
                    int next = WgcNative.NextFrame(pool, &frame);
                    owners.Own(frame);
                    if (next < 0)
                    {
                        // A failure outref is still an owner, and a returned
                        // frame must be closed before graph release.
                        if (frame != 0) { owners.Close(frame); }
                        WgcNative.Require(next, "try_get_next_frame_failed");
                    }

                    if (frame == 0) { Thread.Sleep(10); continue; }
                    frames++;
                    state.FramesReceived = frames;
                    NativeBorrowedFrame borrowed = new(frame, device, context, owners);
                    byte[] packed = WgcOwnedFrameReader.Read(borrowed, () => window.AdmissionOpen);
                    try
                    {
                        width = borrowed.Geometry.ContentWidth;
                        height = borrowed.Geometry.ContentHeight;
                        rowPitch = borrowed.RowPitch;
                        markerHash = HashMarkers(packed, width, height);
                        if (markerHash == ExpectedMarkerHash)
                        {
                            string candidateHash = Convert.ToHexStringLower(SHA256.HashData(packed));
                            window.CommitFrame(() =>
                            {
                                frameHash = candidateHash;
                                passed = true;
                                reason = "known_markers_verified";
                            });
                            break;
                        }
                    }
                    finally { CryptographicOperations.ZeroMemory(packed); }
                    Thread.Sleep(10);
                }

                if (!passed && !window.AdmissionOpen) { throw new ProbeFailureException("capture_stopped"); }
                if (!passed && frames > 0) { throw new ProbeFailureException("known_markers_mismatch"); }
            }
        }
        catch (ProbeFailureException exception) when (!captureStarted
            && exception.Reason == "activation_factory_failed"
            && exception.NativeResult == WgcNative.ClassNotRegistered)
        {
            reason = "wgc_activation_api_unavailable";
            apiResult = exception.NativeResult;
        }
        finally
        {
            // Any uncertain Close leaves the entire graph live until process
            // exit. Never destroy its HWND or call RoUninitialize in that case.
            if (!owners.Quarantined)
            {
                if (session != 0) { owners.Close(session); }
                if (pool != 0) { owners.Close(pool); }
                owners.ReleaseAll();
                if (initialized) { D3D11Native.RoUninitialize(); }
                state.NativeCleanupConfirmed = true;
            }

            if (owners.Quarantined) { throw new ProbeFailureException("native_cleanup_unconfirmed"); }
        }

        if (owners.OwnedReferences != owners.ReleasedReferences)
        {
            throw new ProbeFailureException("owned_reference_imbalance");
        }

        return new WgcProbeResult(passed, reason, frameHash, markerHash, width, height,
            rowPitch, featureLevel, frames, owners.OwnedReferences, owners.ReleasedReferences, captureStarted,
            secondaryWindowsPolicy, apiResult);
    }

    public static string HashMarkers(ReadOnlySpan<byte> packed, int width, int height)
    {
        if (width != 64 || height != 64 || packed.Length != 64 * 64 * 4)
        {
            throw new ProbeFailureException("marker_geometry_mismatch");
        }

        byte[] markers = new byte[4 * 8 * 8 * 4];
        try
        {
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                int left = 8 + (quadrant % 2 * 32);
                int top = 8 + (quadrant / 2 * 32);
                for (int row = 0; row < 8; row++)
                {
                    packed.Slice(((top + row) * width + left) * 4, 8 * 4)
                        .CopyTo(markers.AsSpan((quadrant * 64 + row * 8) * 4, 8 * 4));
                }
            }

            return Convert.ToHexStringLower(SHA256.HashData(markers));
        }
        finally { CryptographicOperations.ZeroMemory(markers); }
    }

    private sealed class NativeBorrowedFrame(
        nint frame, nint device, nint context, WgcComOwners owners) : IWgcBorrowedFrame
    {
        private readonly nint surface;
        private nint access;
        private readonly nint texture;
        private readonly nint staging;
        private bool described;
        private bool closed;
        private WgcFrameGeometry geometry;

        public uint RowPitch { get; private set; }

        public WgcFrameGeometry Geometry
        {
            get
            {
                if (described) { return geometry; }
                SizeInt32 content = default;
                WgcNative.Require(WgcNative.FrameContentSize(frame, &content), "frame_content_size_failed");
                int surfaceResult;
                fixed (nint* output = &surface) { surfaceResult = WgcNative.FrameSurface(frame, output); }
                owners.Own(surface);
                WgcNative.Require(surfaceResult, "frame_surface_failed");
                WgcNative.RequirePointer(surface);
                access = WgcNative.QueryOwned(surface, WgcNative.DxgiAccessIid, owners);
                int textureResult;
                fixed (nint* output = &texture) { textureResult = WgcNative.NativeTexture(access, output); }
                owners.Own(texture);
                WgcNative.Require(textureResult, "native_texture_failed");
                WgcNative.RequirePointer(texture);
                Texture2DDescription description = default;
                D3D11Native.GetDescription(texture, &description);
                if (description.Format != D3D11Native.FormatBgra8Unorm
                    || description.MipLevels != 1 || description.ArraySize != 1
                    || description.SampleCount != 1 || description.SampleQuality != 0
                    || description.Width > BgraReadback.MaximumDimension
                    || description.Height > BgraReadback.MaximumDimension)
                {
                    throw new ProbeFailureException("captured_texture_description_invalid");
                }

                geometry = new((int)description.Width, (int)description.Height, content.Width, content.Height);
                described = true;
                return geometry;
            }
        }

        public byte[] CopyContent()
        {
            WgcFrameGeometry size = Geometry;
            Texture2DDescription description = new()
            {
                Width = (uint)size.ContentWidth,
                Height = (uint)size.ContentHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = D3D11Native.FormatBgra8Unorm,
                SampleCount = 1,
                Usage = D3D11Native.UsageStaging,
                CpuAccessFlags = D3D11Native.CpuAccessRead,
            };
            int created;
            fixed (nint* output = &staging) { created = D3D11Native.CreateTexture2D(device, &description, null, output); }
            owners.Own(staging);
            WgcNative.Require(created, "staging_texture_create_failed");
            WgcNative.RequirePointer(staging);
            TextureBox box = new() { Right = (uint)size.ContentWidth, Bottom = (uint)size.ContentHeight, Back = 1 };
            D3D11Native.CopySubresourceRegion(context, staging, texture, &box);
            D3D11Native.Flush(context);
            MappedSubresource mapped = default;
            bool mapSucceeded = false;
            try
            {
                long started = Stopwatch.GetTimestamp();
                while (true)
                {
                    int result = D3D11Native.Map(context, staging, &mapped);
                    if (result == D3D11Native.WasStillDrawing)
                    {
                        if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(2))
                        {
                            throw new ProbeFailureException("readback_not_ready");
                        }

                        Thread.Sleep(1);
                        continue;
                    }

                    WgcNative.Require(result, "staging_map_failed");
                    mapSucceeded = true;
                    break;
                }

                WgcNative.RequirePointer(mapped.Data);
                if (mapped.RowPitch < (uint)size.ContentWidth * 4
                    || (long)mapped.RowPitch * size.ContentHeight > BgraReadback.MaximumBytes)
                {
                    throw new ProbeFailureException("invalid_row_pitch");
                }

                RowPitch = mapped.RowPitch;
                int length = checked((size.ContentHeight - 1) * (int)mapped.RowPitch + size.ContentWidth * 4);
                return BgraReadback.CopyRows(new ReadOnlySpan<byte>((void*)mapped.Data, length),
                    size.ContentWidth, size.ContentHeight, (int)mapped.RowPitch);
            }
            finally { if (mapSucceeded) { D3D11Native.Unmap(context, staging); } }
        }

        public void Dispose()
        {
            if (closed) { return; }
            owners.Close(frame);
            closed = true;
            if (staging != 0) { owners.ReleaseOwned(staging); }
            if (texture != 0) { owners.ReleaseOwned(texture); }
            if (access != 0) { owners.ReleaseOwned(access); }
            if (surface != 0) { owners.ReleaseOwned(surface); }
            owners.ReleaseOwned(frame);
        }
    }
}
