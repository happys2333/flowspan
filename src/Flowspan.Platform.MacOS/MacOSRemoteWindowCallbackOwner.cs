namespace Flowspan.Platform.MacOS;

internal enum MacOSRemoteWindowDelegateSignal
{
    StoppedWithError,
    Inactive,
    Active,
}

internal enum MacOSRemoteWindowCallbackRetirement
{
    ManagedInvocationsExited,
    SelfJoinRejected,
}

// Portable prerequisite only. This owner is not wired into native capture.
internal sealed class MacOSRemoteWindowCallbackOwnerPool
{
    private readonly MacOSRemoteWindowCallbackOwner[] owners;
    private readonly object gate = new();
    private int reserved;

    internal MacOSRemoteWindowCallbackOwnerPool(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 16);
        owners = new MacOSRemoteWindowCallbackOwner[capacity];
        for (int index = 0; index < capacity; index++)
        {
            owners[index] = new(this);
        }
    }

    internal bool TryReserve(out MacOSRemoteWindowCallbackOwner? owner)
    {
        lock (gate)
        {
            owner = reserved < owners.Length ? owners[reserved++] : null;
            return owner is not null;
        }
    }

    internal bool TryPublish(MacOSRemoteWindowCallbackOwner owner, nint bridge)
    {
        lock (gate)
        {
            if (bridge == 0 || Array.IndexOf(owners, owner, 0, reserved) < 0)
            {
                return false;
            }

            for (int index = 0; index < reserved; index++)
            {
                if (owners[index].Bridge == bridge)
                {
                    return false;
                }
            }

            return owner.TryAcceptPublication(bridge);
        }
    }

    internal void Dispatch(nint bridge, nint stream, MacOSRemoteWindowDelegateSignal signal)
    {
        MacOSRemoteWindowCallbackOwner? target = null;
        lock (gate)
        {
            if (bridge != 0)
            {
                for (int index = 0; index < reserved; index++)
                {
                    if (owners[index].Bridge == bridge)
                    {
                        target = owners[index];
                        break;
                    }
                }
            }
        }

        try
        {
            target?.Receive(stream, signal);
        }
        catch (Exception exception)
        {
            target?.RecordFailure(exception);
        }
    }
}

internal sealed class MacOSRemoteWindowCallbackOwner(MacOSRemoteWindowCallbackOwnerPool pool)
{
    private readonly object gate = new();
    private readonly TaskCompletionSource<MacOSRemoteWindowCallbackRetirement> retirement =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action? handler;
    private nint stream;
    private nint earlyStream;
    private bool bound;
    private bool active;
    private bool earlyTerminal;
    private bool notified;
    private bool retired;
    private bool retiring;
    private int invocations;
    private int admissionClosed;
    private Exception? failure;
    private OutOfMemoryException? fatalFailure;
    private nint bridge;
    internal Exception? Failure => Volatile.Read(ref fatalFailure) ?? Volatile.Read(ref failure);
    internal nint Bridge => Volatile.Read(ref bridge);
    internal bool AdmissionClosed
    {
        get => Volatile.Read(ref admissionClosed) != 0;
        private set => Volatile.Write(ref admissionClosed, value ? 1 : 0);
    }

    internal bool TryPublishBridge(nint bridge) => pool.TryPublish(this, bridge);

    internal bool TryAcceptPublication(nint bridge)
    {
        lock (gate)
        {
            if (Bridge != 0 || retired || retiring)
            {
                return false;
            }

            Volatile.Write(ref this.bridge, bridge);
            return true;
        }
    }

    internal bool BindStreamOnce(nint identity)
    {
        lock (gate)
        {
            if (bound || retired || Bridge == 0)
            {
                return false;
            }

            if (identity == 0 || (earlyTerminal && earlyStream != identity))
            {
                AdmissionClosed = true;
                Detach();
                return false;
            }

            stream = identity;
            bound = true;
            return true;
        }
    }

    internal bool Activate(Action sourceUnavailable)
    {
        ArgumentNullException.ThrowIfNull(sourceUnavailable);
        bool notify;
        lock (gate)
        {
            if (!bound || active || retired)
            {
                return false;
            }

            active = true;
            handler = sourceUnavailable;
            notify = earlyTerminal && earlyStream == stream;
            notified = notify;
            if (notify)
            {
                invocations++;
            }
        }

        if (notify)
        {
            Invoke(sourceUnavailable);
        }

        return true;
    }

    internal void Receive(nint identity, MacOSRemoteWindowDelegateSignal signal)
    {
        Action notify;
        lock (gate)
        {
            if (retired || retiring || identity == 0
                || signal is not (MacOSRemoteWindowDelegateSignal.StoppedWithError
                    or MacOSRemoteWindowDelegateSignal.Inactive))
            {
                return;
            }

            if (!active && !earlyTerminal)
            {
                if (bound && identity != stream)
                {
                    AdmissionClosed = true;
                    Detach();
                    return;
                }

                earlyTerminal = true;
                earlyStream = identity;
                AdmissionClosed = true;
                return;
            }

            if (AdmissionClosed || notified || identity != stream || handler is null)
            {
                return;
            }

            AdmissionClosed = true;
            notified = true;
            notify = handler;
            invocations++;
        }

        Invoke(notify);
    }

    private void Invoke(Action notify)
    {
        try
        {
            using var ancestry = new CallbackScope(this);
            using var descendants = NativeRemoteWindowDrainActivityScope.Enter(this, this);
            try
            {
                notify();
            }
            catch (Exception exception)
            {
                // Preserve the primary fault before scope restoration can fail.
                RecordFailure(exception);
            }
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }
        finally
        {
            try
            {
                bool complete;
                lock (gate)
                {
                    invocations--;
                    complete = retiring && invocations == 0;
                    if (complete)
                    {
                        Detach();
                    }
                }

                if (complete)
                {
                    retirement.TrySetResult(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited);
                }
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
            }
        }
    }

    internal ValueTask<MacOSRemoteWindowCallbackRetirement> RetireAsync()
    {
        if (CallbackScope.Contains(this)
            || NativeRemoteWindowDrainActivityScope.IsActiveForOwner(this))
        {
            AdmissionClosed = true;
            return ValueTask.FromResult(MacOSRemoteWindowCallbackRetirement.SelfJoinRejected);
        }

        bool complete;
        lock (gate)
        {
            AdmissionClosed = true;
            retiring = true;
            complete = invocations == 0;
            if (complete)
            {
                Detach();
            }
        }

        if (complete)
        {
            retirement.TrySetResult(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited);
        }

        return new(retirement.Task);
    }

    private void Detach()
    {
        retired = true;
        handler = null;
        stream = 0;
        earlyStream = 0;
    }

    internal void RecordFailure(Exception exception)
    {
        AdmissionClosed = true;
        Interlocked.CompareExchange(ref failure, exception, null);
        if (FindFatal(exception) is { } fatal)
        {
            Interlocked.CompareExchange(ref fatalFailure, fatal, null);
        }
    }

    private static OutOfMemoryException? FindFatal(Exception exception)
    {
        if (exception is OutOfMemoryException fatal)
        {
            return fatal;
        }

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                if (FindFatal(inner) is { } nested)
                {
                    return nested;
                }
            }

            return null;
        }

        return exception.InnerException is { } cause ? FindFatal(cause) : null;
    }

    private sealed class CallbackScope : IDisposable
    {
        [ThreadStatic] private static CallbackScope? current;
        private readonly MacOSRemoteWindowCallbackOwner owner;
        private readonly CallbackScope? previous;

        internal CallbackScope(MacOSRemoteWindowCallbackOwner owner)
        {
            this.owner = owner;
            previous = current;
            current = this;
        }

        internal static bool Contains(MacOSRemoteWindowCallbackOwner owner)
        {
            for (var scope = current; scope is not null; scope = scope.previous)
            {
                if (ReferenceEquals(scope.owner, owner))
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose() => current = previous;
    }
}
