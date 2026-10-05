namespace Flowspan.Platform.MacOS;

// One inert one-object-argument primitive owner. Capture must attach this exact
// graph before AcquireCopy; the controlled-effects observer only observes it.
internal sealed class MacOSRemoteWindowCaptureCompletion : IMacOSRemoteWindowStagedCaptureCompletion
{
    private readonly MacOSRemoteWindowBlock block;
    private readonly Action? completedResourceUseExited;
    private readonly object resourceUseGate = new();
    private readonly TaskCompletionSource resourceUseJoin = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool resourceUseClosed;
    private int activeResourceUsers;

    private MacOSRemoteWindowCaptureCompletion(Action<nint> action, Action<Exception> failure,
        Action completed, IMacOSRemoteWindowBlockOperations? operations, Action? completedResourceUseExited = null)
    {
        this.completedResourceUseExited = completedResourceUseExited;
        // One fixed admission owner covers all three callbacks. Preparation is
        // inert, so this exact owner exists before native acquisition/entry.
        Action<nint> guardedAction = value => InvokeResourceUse(action, value);
        Action<Exception> guardedFailure = exception => InvokeResourceUse(failure, exception);
        Action guardedCompleted = () => InvokeResourceUse(completed);
        block = operations is null
            ? MacOSRemoteWindowBlock.Prepare(guardedAction, guardedFailure, guardedCompleted)
            : MacOSRemoteWindowBlock.PrepareWithOperations(guardedAction, operations, guardedFailure, guardedCompleted);
    }

    internal MacOSRemoteWindowBlock Primitive => block;

    internal static MacOSRemoteWindowCaptureCompletion Create(
        Action<nint> action, Action<Exception> failure, Action completed)
    {
        var owner = new MacOSRemoteWindowCaptureCompletion(action, failure, completed, operations: null);
        return owner;
    }

    internal static MacOSRemoteWindowCaptureCompletion CreateWithOperations(
        Action<nint> action, Action<Exception> failure, Action completed,
        IMacOSRemoteWindowBlockOperations operations,
        Action<MacOSRemoteWindowCaptureCompletion>? prepared = null,
        Action? completedResourceUseExited = null)
    {
        var owner = new MacOSRemoteWindowCaptureCompletion(action, failure, completed, operations, completedResourceUseExited);
        prepared?.Invoke(owner);
        return owner;
    }

    public void AcquireCopy() => block.AcquireCopy();
    public bool HasActiveResourceUseAncestry => NativeRemoteWindowDrainActivityScope.IsActiveForOwner(this);

    public Task CloseResourceUse()
    {
        lock (resourceUseGate)
        {
            resourceUseClosed = true;
            if (activeResourceUsers == 0) { resourceUseJoin.TrySetResult(); }
            return resourceUseJoin.Task;
        }
    }

    private bool TryEnterResourceUse()
    {
        lock (resourceUseGate)
        {
            if (resourceUseClosed) { return false; }
            activeResourceUsers++;
            return true;
        }
    }

    private void ExitResourceUse()
    {
        lock (resourceUseGate)
        {
            activeResourceUsers--;
            // Open first-idle is never cached as terminal resource-use join.
            if (resourceUseClosed && activeResourceUsers == 0) { resourceUseJoin.TrySetResult(); }
        }
    }

    private void InvokeResourceUse<T>(Action<T> callback, T value)
    {
        if (!TryEnterResourceUse()) { return; }
        try
        {
            using var ancestry = NativeRemoteWindowDrainActivityScope.Enter(this, this);
            callback(value);
        }
        finally { ExitResourceUse(); }
    }

    private void InvokeResourceUse(Action callback)
    {
        if (!TryEnterResourceUse()) { return; }
        try
        {
            using var ancestry = NativeRemoteWindowDrainActivityScope.Enter(this, this);
            callback();
        }
        finally
        {
            ExitResourceUse();
            // Default-off controlled-effects observation only. The original
            // callback exception still propagates to the real primitive ABI.
            completedResourceUseExited?.Invoke();
        }
    }

    public nint Pointer => block.Pointer;
    public bool IsReleased => block.IsReleased;
    public Exception? FirstFailure => block.FirstFailure;
    public Task NativeCaptureRetirement => block.NativeCaptureRetirement;
    public Task ManagedInvocationDrain => block.ManagedInvocationDrain;
    public bool TryGetKnownLifetimeJoin(out Task? join)
    {
        join = null;
        lock (resourceUseGate)
        {
            if (!resourceUseClosed || activeResourceUsers != 0 || !resourceUseJoin.Task.IsCompletedSuccessfully)
            {
                return false;
            }
        }

        return block.TryGetKnownLifetimeJoin(out join);
    }
    public void Dispose() => block.Dispose();
}
