namespace Flowspan.Platform.MacOS;

// The original reserved batch owns this graph before enumeration effects.
// This first tracer stages content ownership; primitive Block/dispatch/drain
// obligations remain separate unverified work within the enumeration slice.
internal sealed class MacOSRemoteWindowEnumerationOwnershipLedger(
    MacOSRemoteWindowSourceCreationContext context,
    IMacOSRemoteWindowEnumerationOperations operations)
{
    private int contentLifecycleEnded;
    private int callbackAdmitted;
    private int activeInvocations;
    private readonly TaskCompletionSource<bool> invocationsExited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal MacOSRemoteWindowSourceCreationContext Context { get; } = context;
    internal IMacOSRemoteWindowEnumerationOperations Operations { get; } = operations;
    internal IMacOSRemoteWindowEnumerationCompletion? Completion;
    internal nint BorrowedContent;
    internal bool ContentRetainAttempted;
    internal bool ContentRetainConfirmed;
    internal nint ContentOwner;
    internal bool ContentReleaseAttempted;
    internal bool ContentReleaseConfirmed;
    internal Exception? Failure;
    internal bool IsContentLifecycleEnded => Volatile.Read(ref contentLifecycleEnded) != 0;
    internal void EndContentLifecycle() => Volatile.Write(ref contentLifecycleEnded, 1);
    internal bool TryAdmitCallback() => Interlocked.CompareExchange(ref callbackAdmitted, 1, 0) == 0;
    internal void EnterInvocation() => Interlocked.Increment(ref activeInvocations);
    internal void ExitInvocation()
    {
        if (Interlocked.Decrement(ref activeInvocations) == 0)
        {
            invocationsExited.TrySetResult(true);
        }
    }
    // Joins the callback set through its first idle boundary, not native copies.
    // Native retirement must independently exclude later ABI entry (task4).
    internal Task InvocationsExited => invocationsExited.Task;
}
