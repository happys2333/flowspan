namespace Flowspan.Platform.MacOS;

// Only external system effects are replaceable. Both production and portable
// contracts execute EnumerateCoreAsync and the existing CreateSourceCore.
internal interface IMacOSRemoteWindowEnumerationOperations
{
    public bool PreflightCaptureAccess();
    public bool HasExistingApplication();
    public void InitializeRuntime();
    public IMacOSRemoteWindowEnumerationCompletion PrepareCompletion(
        Action<nint, nint> action, Action<Exception> failure, Action completed);
    public nint PushAutoreleasePool();
    public void PopAutoreleasePool(nint pool);
    public void Dispatch(nint completion);
    public nint RetainContent(nint content);
    public void ReleaseContent(nint content);
    public nint GetWindows(nint content);
    public nuint GetWindowCount(nint windows);
    public nint GetWindow(nint windows, nuint index);
    public IMacOSRemoteWindowSourceCreationOperations SourceCreationOperations { get; }
}

// Preparation is inert. Acquisition, owned release, physical capture retirement
// and the terminal managed invocation drain are independent lifetime facts.
internal interface IMacOSRemoteWindowEnumerationCompletion : IDisposable
{
    public void AcquireCopy();
    public nint Pointer { get; }
    public bool IsReleased { get; }
    public Exception? FirstFailure { get; }
    public Task NativeCaptureRetirement { get; }
    public Task ManagedInvocationDrain { get; }
}
