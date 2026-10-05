namespace Flowspan.Platform.MacOS;

// The original reserved batch owns this graph before enumeration effects.
// The completion shell is inert until attached here. Caller-owned release and
// terminal native/managed lifetime proof are separate from content use ending.
internal sealed class MacOSRemoteWindowEnumerationOwnershipLedger(
    MacOSRemoteWindowSourceCreationContext context,
    IMacOSRemoteWindowEnumerationOperations operations)
{
    private int enumerationLifecycleEnded;
    private int callbackAdmission;
    private int activeInvocations;
    private readonly TaskCompletionSource<bool> invocationsExited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource failureObserved =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? failure;
    internal MacOSRemoteWindowSourceCreationContext Context { get; } = context;
    internal IMacOSRemoteWindowEnumerationOperations Operations { get; } = operations;
    internal IMacOSRemoteWindowEnumerationCompletion? Completion;
    internal bool CompletionReleaseAttempted;
    internal bool CompletionReleaseConfirmed;
    internal bool DispatchAttempted;
    internal bool DispatchConfirmed;
    internal bool FirstPoolAcquireAttempted;
    internal bool FirstPoolAcquireConfirmed;
    internal nint FirstPoolOwner;
    internal bool FirstPoolReleaseAttempted;
    internal bool FirstPoolReleaseConfirmed;
    internal bool SecondPoolAcquireAttempted;
    internal bool SecondPoolAcquireConfirmed;
    internal nint SecondPoolOwner;
    internal bool SecondPoolReleaseAttempted;
    internal bool SecondPoolReleaseConfirmed;
    internal nint BorrowedContent;
    internal bool ContentRetainAttempted;
    internal bool ContentRetainConfirmed;
    internal nint ContentOwner;
    internal bool ContentReleaseAttempted;
    internal bool ContentReleaseConfirmed;
    internal Exception? Failure => Volatile.Read(ref failure);
    internal Task FailureObserved => failureObserved.Task;
    internal void RecordFailure(Exception exception)
    {
        Interlocked.CompareExchange(ref failure, exception, null);
        // Publish before the possibly fallible outward context/sink observer.
        // A last-copy failure may leave retirement permanently unconfirmed.
        failureObserved.TrySetResult();
    }
    internal bool IsEnumerationLifecycleEnded => Volatile.Read(ref enumerationLifecycleEnded) != 0;
    internal void EndEnumerationLifecycle() => Volatile.Write(ref enumerationLifecycleEnded, 1);
    internal bool TryAdmitCallback() => Interlocked.CompareExchange(ref callbackAdmission, 1, 0) == 0;
    internal bool HasAdmittedCallback => Volatile.Read(ref callbackAdmission) is 1 or 3;
    internal void CloseCallbackAdmission()
    {
        // 0=open, 1=admitted, 2=closed without admission, 3=closed after admission.
        // Admission and closure share the same CAS; a context check alone races.
        while (true)
        {
            int observed = Volatile.Read(ref callbackAdmission);
            if (observed >= 2
                || Interlocked.CompareExchange(ref callbackAdmission, observed + 2, observed) == observed)
            {
                return;
            }
        }
    }
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
