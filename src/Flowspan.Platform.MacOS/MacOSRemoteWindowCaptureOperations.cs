namespace Flowspan.Platform.MacOS;

// Capture's platform boundary, not a second capture state machine. Production
// owns the Objective-C/dispatch/Block calls; portable tests own only these
// external effects and execute the same Capture constructor and lifecycle.
internal interface IMacOSRemoteWindowCaptureSource : IDisposable
{
    public NativeRemoteWindowGeometry Geometry { get; }
    public nint Filter { get; }
    public IMacOSRemoteWindowCaptureSource RetainOwner();
    public bool IsCurrent();
}

// Native source acquisition must be staged on the rooted Capture before the
// first retain. Legacy system-boundary fakes keep the original opaque contract.
internal interface IMacOSRemoteWindowStagedCaptureSource : IMacOSRemoteWindowCaptureSource
{
    public IMacOSRemoteWindowStagedCaptureSource PrepareOwner();
    public void AcquireOwner();
}

internal interface IMacOSRemoteWindowCaptureCompletion : IDisposable
{
    public nint Pointer { get; }
    public bool IsReleased { get; }
    public Exception? FirstFailure { get; }
}

internal interface IMacOSRemoteWindowStagedCaptureCompletion : IMacOSRemoteWindowCaptureCompletion
{
    public void AcquireCopy();
    public Task CloseResourceUse();
    public bool HasActiveResourceUseAncestry { get; }
    public Task NativeCaptureRetirement { get; }
    public Task ManagedInvocationDrain { get; }
    public bool TryGetKnownLifetimeJoin(out Task? join)
    {
        join = null;
        return false;
    }
}

internal interface IMacOSRemoteWindowCaptureOperations
{
    public nint PushAutoreleasePool();
    public void PopAutoreleasePool(nint pool);
    public nint AllocateConfiguration();
    public void Configure(nint configuration, int width, int height);
    public nint AllocateOutput();
    public nint CreateSampleQueue();
    public nint AllocateStream();
    public nint InitializeStream(nint allocatedStream, nint filter, nint configuration, nint streamDelegate);
    public byte AddOutput(nint stream, nint output, nint queue, out nint error);
    public byte RemoveOutput(nint stream, nint output, out nint error);
    public IMacOSRemoteWindowCaptureCompletion CreateCompletion(
        Action<nint> action, Action<Exception> failure, Action completed);
    public nint GetCompletionSelector(bool isStart);
    public void InvokeCompletion(nint stream, nint selector, nint completion);
    public void DrainSampleQueue(nint queue);
    public void ReleaseObject(nint owner);
    public void ReleaseQueue(nint queue);
    public nint? GetSampleFrameStatus(nint sample);
    public bool IsSampleReady(nint sample);
    public IMacOSRemoteWindowNativeSample RetainSample(nint sample, int width, int height);
}
