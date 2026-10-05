using System.Buffers;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Flowspan.Platform.MacOS.Tests")]
[assembly: InternalsVisibleTo("Flowspan.MacOS.NativeCaptureProbe")]
[assembly: InternalsVisibleTo("Flowspan.MacOS.DelegateProbe")]

namespace Flowspan.Platform.MacOS;

// These identities and owners never leave the local platform assembly. Native
// implementations must not expose window titles, process names, or handles.
internal readonly record struct MacOSRemoteWindowNativeIdentity(
    uint WindowId,
    int ProcessId,
    ulong ProcessStartSeconds,
    ulong ProcessStartMicroseconds)
{
    public override string ToString() => "macOS exact local window identity";
}

internal interface IMacOSRemoteWindowNativeSource : IDisposable
{
    public MacOSRemoteWindowNativeIdentity Identity { get; }

    public NativeRemoteWindowGeometry Geometry { get; }
}

internal interface IMacOSRemoteWindowNativeSample : IDisposable
{
    // Construct this node before taking native sample ownership. Its getter
    // must not throw; failure cleanup links it without allocating a ledger.
    public MacOSRemoteWindowSampleRetention Retention { get; }

    // A successful copy transfers a bounded, clearing pixel owner to the caller.
    // The sample remains borrowed until Dispose, including failed copies.
    public bool TryCopyPixels(out MacOSRemoteWindowPixelBuffer? pixels);
}

internal sealed class MacOSRemoteWindowSampleRetention
{
    internal bool IsLinked { get; set; }

    internal IMacOSRemoteWindowNativeSample? Sample { get; set; }

    internal MacOSRemoteWindowSampleRetention? Next { get; set; }
}

internal sealed record MacOSRemoteWindowPixelBuffer(
    IMemoryOwner<byte> Owner,
    int Length,
    int Width,
    int Height,
    int Stride);

internal interface IMacOSRemoteWindowNativeCapture : IDisposable
{
    // Physical drain proof is independent of an operation's fatal diagnostics.
    // Fakes which do not provide that proof remain conservatively unconfirmed.
    public bool IsDrained => false;

    public ValueTask<bool> StartAsync();

    // Success proves Start settled, native Stop, output removal and its serial
    // sample queue barrier. Failure must retain every native callback owner.
    public ValueTask<bool> StopAndDrainAsync();
}

internal interface IMacOSRemoteWindowNativeApi
{
    public bool IsSupported { get; }

    public bool PreflightCaptureAccess();

    public ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateAsync();

    public ValueTask<IReadOnlyList<IMacOSRemoteWindowNativeSource>> EnumerateAsync(
        MacOSRemoteWindowSourceCreationContext context) => EnumerateAsync();

    public bool IsCurrent(IMacOSRemoteWindowNativeSource source);

    public IMacOSRemoteWindowNativeCapture CreateCapture(
        IMacOSRemoteWindowNativeSource source,
        Action<IMacOSRemoteWindowNativeSample> takeSampleOwnership,
        Action sourceUnavailable);

    // A synchronous failed construction may still own asynchronous native
    // rollback. Transfer that owner without wrapping the original failure.
    public bool TryTakeFailedCapture(
        Exception failure,
        out IMacOSRemoteWindowNativeCapture? capture)
    {
        capture = null;
        return false;
    }
}
