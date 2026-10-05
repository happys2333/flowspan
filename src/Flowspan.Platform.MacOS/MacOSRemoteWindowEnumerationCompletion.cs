namespace Flowspan.Platform.MacOS;

// This owner contains the actual staged primitive, including a factory effect
// that fails before acquisition returns. Preparation has no per-operation root
// or native copy; the caller must attach this shell before AcquireCopy.
internal sealed class MacOSRemoteWindowEnumerationCompletion : IMacOSRemoteWindowEnumerationCompletion
{
    private readonly MacOSRemoteWindowBlock block;

    private MacOSRemoteWindowEnumerationCompletion(MacOSRemoteWindowBlock block) => this.block = block;

    internal static MacOSRemoteWindowEnumerationCompletion Prepare(
        Action<nint, nint> action, Action<Exception> failure, Action completed) =>
        new(MacOSRemoteWindowBlock.Prepare(action, failure, completed));

    internal static MacOSRemoteWindowEnumerationCompletion PrepareWithOperations(
        Action<nint, nint> action, Action<Exception> failure, Action completed,
        IMacOSRemoteWindowBlockOperations operations) =>
        new(MacOSRemoteWindowBlock.PrepareWithOperations(action, operations, failure, completed));

    public void AcquireCopy() => block.AcquireCopy();
    public nint Pointer => block.Pointer;
    public bool IsReleased => block.IsReleased;
    public Exception? FirstFailure => block.FirstFailure;
    public Task NativeCaptureRetirement => block.NativeCaptureRetirement;
    public Task ManagedInvocationDrain => block.ManagedInvocationDrain;
    public void Dispose() => block.Dispose();
}
