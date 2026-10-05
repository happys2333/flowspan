using Flowspan.Platform;
using Flowspan.Platform.MacOS;

namespace Flowspan.MacOS.NativeCaptureProbe;

// The underlying driver may enumerate permitted sources. Only this process's
// single task-created window can reach the probe's catalog or capture factory.
internal sealed class OwnWindowApi(MacOSRemoteWindowScreenCaptureKitApi driver, uint windowId) :
    IMacOSRemoteWindowNativeApi
{
    private int sampleDeliveryCalls;

    public bool IsSupported => driver.IsSupported;

    internal int SampleDeliveryCalls => Volatile.Read(ref sampleDeliveryCalls);

    public bool PreflightCaptureAccess() => driver.PreflightCaptureAccess();

    public ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateAsync() =>
        FilterSourcesAsync(driver.EnumerateAsync());

    public ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateAsync(
        MacOSRemoteWindowSourceCreationContext context) =>
        FilterSourcesAsync(driver.EnumerateAsync(context));

    private async ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> FilterSourcesAsync(
        ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> enumeration)
    {
        IReadOnlyList<IMacOSRemoteWindowNativeSource> sources = await enumeration.ConfigureAwait(false);
        try
        {
            List<IMacOSRemoteWindowNativeSource> own = [];
            foreach (IMacOSRemoteWindowNativeSource source in sources)
            {
                if (IsTaskWindow(source)) own.Add(source);
                else source.Dispose();
            }
            return own;
        }
        catch
        {
            // Production sources have idempotent release. A failed filtered
            // list still settles every source returned by the native driver.
            foreach (IMacOSRemoteWindowNativeSource source in sources) source.Dispose();
            throw;
        }
    }

    public bool IsCurrent(IMacOSRemoteWindowNativeSource source) =>
        IsTaskWindow(source) && driver.IsCurrent(source);

    public IMacOSRemoteWindowNativeCapture CreateCapture(
        IMacOSRemoteWindowNativeSource source,
        Action<IMacOSRemoteWindowNativeSample> takeSampleOwnership,
        Action sourceUnavailable)
    {
        Program.Require(IsTaskWindow(source), "capture_exact_task_window_id_and_pid");
        return driver.CreateCapture(source, sample =>
        {
            Interlocked.Increment(ref sampleDeliveryCalls);
            takeSampleOwnership(sample);
        }, sourceUnavailable);
    }

    public bool TryTakeFailedCapture(Exception failure,
        out IMacOSRemoteWindowNativeCapture? capture) =>
        driver.TryTakeFailedCapture(failure, out capture);

    private bool IsTaskWindow(IMacOSRemoteWindowNativeSource source) =>
        source.Identity.WindowId == windowId && source.Identity.ProcessId == Environment.ProcessId;
}

internal abstract class MarkerObserver(NativeRemoteWindowGeometry geometry)
{
    private Exception? failure;
    private int matched;

    internal Exception? Failure => Volatile.Read(ref failure);

    internal int Matched => Volatile.Read(ref matched);

    internal int Width { get; private set; }

    internal int Height { get; private set; }

    protected void Observe(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        Program.Require(width == (int)Math.Ceiling(geometry.Width * geometry.ScaleFactor)
            && height == (int)Math.Ceiling(geometry.Height * geometry.ScaleFactor)
            && stride == width * 4 && pixels.Length == stride * height,
            "tightly_packed_exact_window_frame");
        Program.Require(MatchMarkers(pixels, width, height, stride, false)
            || MatchMarkers(pixels, width, height, stride, true), "task_four_color_markers");
        Width = width;
        Height = height;
        Interlocked.Increment(ref matched);
    }

    protected void RecordFailure(Exception exception) =>
        Interlocked.CompareExchange(ref failure, exception, null);

    private static bool MatchMarkers(ReadOnlySpan<byte> pixels,
        int width, int height, int stride, bool flipped)
    {
        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            int x = (quadrant % 2 * 2 + 1) * width / 4;
            int y = (quadrant / 2 * 2 + 1) * height / 4;
            if (flipped) y = height - 1 - y;
            int index = y * stride + x * 4;
            if (pixels[index] != (quadrant is 2 or 3 ? 255 : 0)
                || pixels[index + 1] != (quadrant is 1 or 3 ? 255 : 0)
                || pixels[index + 2] != (quadrant is 0 or 3 ? 255 : 0)
                || pixels[index + 3] != 255) return false;
        }
        return true;
    }
}

internal sealed class RawSamples(NativeRemoteWindowGeometry geometry) : MarkerObserver(geometry)
{
    private int unavailable;

    internal int UnavailableCount => Volatile.Read(ref unavailable);

    internal void Unavailable() => Interlocked.Increment(ref unavailable);

    internal void TakeOwnership(IMacOSRemoteWindowNativeSample sample)
    {
        try
        {
            using (sample)
            {
                if (!sample.TryCopyPixels(out MacOSRemoteWindowPixelBuffer? pixels) || pixels is null) return;
                using (pixels.Owner)
                {
                    Observe(pixels.Owner.Memory.Span[..pixels.Length], pixels.Width, pixels.Height, pixels.Stride);
                }
            }
        }
        catch (Exception exception) { RecordFailure(exception); }
    }
}

internal sealed class MarkerSink(NativeRemoteWindowSourceUse expectedUse, NativeRemoteWindowGeometry geometry) :
    MarkerObserver(geometry), INativeRemoteWindowFrameSink
{
    public void TakeOwnership(NativeRemoteWindowSourceUse sourceUse, NativeRemoteWindowFrame frame)
    {
        try
        {
            using (frame)
            {
                Program.Require(ReferenceEquals(sourceUse, expectedUse) && expectedUse.Matches(frame)
                    && frame.PixelFormat == NativeRemoteWindowPixelFormat.Bgra8888,
                    "boundary_frame_generation_and_format");
                Observe(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);
            }
        }
        catch (Exception exception) { RecordFailure(exception); }
    }
}
