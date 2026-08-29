using System.Runtime.InteropServices;

namespace Flowspan.Platform.MacOS;

public interface IMacOSScreenCapturePermissionInterop
{
    public bool IsSupported { get; }

    public bool PreflightScreenCaptureAccess();

    public bool RequestScreenCaptureAccess();
}

public sealed class MacOSNativeRemoteWindowPermissionBoundary :
    INativeRemoteWindowPermissionBoundary
{
    private readonly object gate = new();
    private readonly IMacOSScreenCapturePermissionInterop interop;
    private readonly long ownerGeneration;
    private Action<NativeRemoteWindowPermissionSnapshot>? changed;
    private long committedOperationSequence;
    private NativeRemoteWindowPermissionSnapshot current;
    private bool disposed;
    private NativeRemoteWindowPermissionState lastConclusiveCapture =
        NativeRemoteWindowPermissionState.NotDetermined;
    private long nextOperationSequence;

    public MacOSNativeRemoteWindowPermissionBoundary()
        : this(new CoreGraphicsScreenCapturePermissionInterop())
    {
    }

    public MacOSNativeRemoteWindowPermissionBoundary(
        IMacOSScreenCapturePermissionInterop interop,
        long ownerGeneration = 1)
    {
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerGeneration, 1);
        this.interop = interop;
        this.ownerGeneration = ownerGeneration;
        current = NativeRemoteWindowPermissionSnapshot.Create(
            NativeRemoteWindowPermissionState.NotDetermined,
            NativeRemoteWindowPermissionState.Unsupported,
            ownerGeneration,
            revision: 0);
    }

    public event Action<NativeRemoteWindowPermissionSnapshot>? Changed
    {
        add
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                changed += value;
            }
        }
        remove
        {
            lock (gate)
            {
                changed -= value;
            }
        }
    }

    public NativeRemoteWindowPermissionSnapshot GetSnapshot()
    {
        long operationSequence = BeginOperation();
        CapturePermissionObservation observation;
        try
        {
            observation = !interop.IsSupported
                ? CapturePermissionObservation.Unsupported
                : interop.PreflightScreenCaptureAccess()
                    ? CapturePermissionObservation.PreflightGranted
                    : CapturePermissionObservation.PreflightAbsent;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            observation = CapturePermissionObservation.Unavailable;
        }

        return CommitObservation(operationSequence, observation);
    }

    public ValueTask<NativeRemoteWindowPermissionSnapshot>
        RequestCapturePermissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long operationSequence = BeginOperation();
        CapturePermissionObservation observation;
        try
        {
            observation = !interop.IsSupported
                ? CapturePermissionObservation.Unsupported
                : interop.RequestScreenCaptureAccess()
                    ? CapturePermissionObservation.ExplicitGranted
                    : CapturePermissionObservation.ExplicitDenied;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            observation = CapturePermissionObservation.Unavailable;
        }

        return ValueTask.FromResult(
            CommitObservation(operationSequence, observation));
    }

    public ValueTask<NativeRemoteWindowPermissionSnapshot>
        RequestInputPermissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return ValueTask.FromResult(current);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            disposed = true;
            changed = null;
        }

        return ValueTask.CompletedTask;
    }

    private long BeginOperation()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return checked(++nextOperationSequence);
        }
    }

    private NativeRemoteWindowPermissionSnapshot CommitObservation(
        long operationSequence,
        CapturePermissionObservation observation)
    {
        Action<NativeRemoteWindowPermissionSnapshot>[] observers = [];
        NativeRemoteWindowPermissionSnapshot snapshot;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (operationSequence <= committedOperationSequence)
            {
                return current;
            }

            committedOperationSequence = operationSequence;
            NativeRemoteWindowPermissionState capture = observation switch
            {
                CapturePermissionObservation.PreflightGranted or
                    CapturePermissionObservation.ExplicitGranted =>
                    NativeRemoteWindowPermissionState.Granted,
                CapturePermissionObservation.PreflightAbsent =>
                    MapAbsentPreflight(),
                CapturePermissionObservation.ExplicitDenied =>
                    NativeRemoteWindowPermissionState.Denied,
                CapturePermissionObservation.Unsupported =>
                    NativeRemoteWindowPermissionState.Unsupported,
                _ => NativeRemoteWindowPermissionState.Unavailable,
            };
            if (current.Capture == capture)
            {
                return current;
            }

            if (capture is not NativeRemoteWindowPermissionState.Unavailable
                and not NativeRemoteWindowPermissionState.Unsupported)
            {
                lastConclusiveCapture = capture;
            }

            current = NativeRemoteWindowPermissionSnapshot.Create(
                capture,
                NativeRemoteWindowPermissionState.Unsupported,
                ownerGeneration,
                checked(current.Revision + 1));
            snapshot = current;
            observers = changed?.GetInvocationList()
                .Cast<Action<NativeRemoteWindowPermissionSnapshot>>()
                .ToArray() ?? [];
        }

        foreach (Action<NativeRemoteWindowPermissionSnapshot> observer in observers)
        {
            try
            {
                observer(snapshot);
            }
            catch (Exception)
            {
            }
        }

        return snapshot;
    }

    private NativeRemoteWindowPermissionState MapAbsentPreflight() =>
        lastConclusiveCapture switch
        {
            NativeRemoteWindowPermissionState.NotDetermined =>
                NativeRemoteWindowPermissionState.NotDetermined,
            NativeRemoteWindowPermissionState.Granted =>
                NativeRemoteWindowPermissionState.Revoked,
            NativeRemoteWindowPermissionState.Revoked =>
                NativeRemoteWindowPermissionState.Revoked,
            _ => NativeRemoteWindowPermissionState.Denied,
        };

    private enum CapturePermissionObservation
    {
        PreflightGranted,
        PreflightAbsent,
        ExplicitGranted,
        ExplicitDenied,
        Unsupported,
        Unavailable,
    }
}

internal sealed partial class CoreGraphicsScreenCapturePermissionInterop :
    IMacOSScreenCapturePermissionInterop
{
    private const string CoreGraphicsFramework =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    public bool IsSupported =>
        OperatingSystem.IsMacOSVersionAtLeast(10, 15);

    public bool PreflightScreenCaptureAccess() =>
        CGPreflightScreenCaptureAccess();

    public bool RequestScreenCaptureAccess() =>
        CGRequestScreenCaptureAccess();

    [LibraryImport(CoreGraphicsFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool CGPreflightScreenCaptureAccess();

    [LibraryImport(CoreGraphicsFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool CGRequestScreenCaptureAccess();
}
