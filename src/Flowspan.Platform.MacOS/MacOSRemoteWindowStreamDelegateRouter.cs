namespace Flowspan.Platform.MacOS;

// Portable routing only. The native boundary must prove the generation's source.
internal sealed class MacOSRemoteWindowStreamDelegateRouter
{
    private readonly Registration?[] registrations;
    private readonly object gate = new();
    private long lastGeneration;
    private Registration? initializer;
    private bool poisoned;

    internal MacOSRemoteWindowStreamDelegateRouter(int capacity = 16, long initialGeneration = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 16);
        ArgumentOutOfRangeException.ThrowIfNegative(initialGeneration);
        registrations = new Registration[capacity];
        lastGeneration = initialGeneration;
    }

    internal bool TryReserve(out Registration? registration)
    {
        lock (gate)
        {
            if (initializer is not null || poisoned || lastGeneration == long.MaxValue)
            {
                registration = null;
                return false;
            }

            int slot = Array.IndexOf(registrations, null);
            registration = slot < 0 ? null : new Registration(this, ++lastGeneration);
            if (registration is not null)
            {
                registrations[slot] = registration;
                initializer = registration;
            }

            return registration is not null;
        }
    }

    internal void Dispatch(long generation, MacOSRemoteWindowDelegateSignal signal)
    {
        Registration? target = null;
        lock (gate)
        {
            foreach (Registration? candidate in registrations)
            {
                if (candidate?.Generation == generation)
                {
                    target = candidate;
                    break;
                }
            }
        }

        target?.Receive(signal);
    }

    internal sealed class Registration(MacOSRemoteWindowStreamDelegateRouter router, long generation)
    {
        private Action? handler;
        private bool published;
        private bool associated;
        private bool active;
        private bool terminal;
        private bool retired;
        private bool retiring;
        private bool cleanupConfirmed;
        private bool quarantined;
        private int invocations;
        private int admissionClosed;
        private Exception? failure;
        private OutOfMemoryException? fatalFailure;
        private readonly TaskCompletionSource<MacOSRemoteWindowCallbackRetirement> retirement =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal long Generation { get; } = generation;
        internal Exception? Failure => Volatile.Read(ref fatalFailure) ?? Volatile.Read(ref failure);
        internal bool AdmissionClosed
        {
            get => Volatile.Read(ref admissionClosed) != 0;
            private set => Volatile.Write(ref admissionClosed, value ? 1 : 0);
        }
        internal bool MarkDelegatePublished()
        {
            lock (router.gate)
            {
                if (published || retired || quarantined || !ReferenceEquals(router.initializer, this))
                {
                    return false;
                }

                published = true;
                return true;
            }
        }

        internal bool ConfirmAssociation()
        {
            lock (router.gate)
            {
                if (!published || associated || retired || quarantined || !ReferenceEquals(router.initializer, this))
                {
                    return false;
                }

                associated = true;
                router.initializer = null;
                return true;
            }
        }

        internal bool FailInitialization()
        {
            lock (router.gate)
            {
                if (!ReferenceEquals(router.initializer, this))
                {
                    return false;
                }

                AdmissionClosed = true;
                AbandonInitializerCore();
                return true;
            }
        }
        internal bool Activate(Action sourceUnavailable)
        {
            ArgumentNullException.ThrowIfNull(sourceUnavailable);
            bool notify;
            lock (router.gate)
            {
                if (!associated || active || retired || retiring || quarantined)
                {
                    return false;
                }

                active = true;
                handler = sourceUnavailable;
                notify = terminal;
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

        internal ValueTask<MacOSRemoteWindowCallbackRetirement> RetireAsync()
        {
            if (CallbackScope.Contains(this)
                || NativeRemoteWindowDrainActivityScope.IsActiveForOwner(this))
            {
                AdmissionClosed = true;
                return ValueTask.FromResult(MacOSRemoteWindowCallbackRetirement.SelfJoinRejected);
            }

            bool complete;
            lock (router.gate)
            {
                AdmissionClosed = true;
                retiring = true;
                AbandonInitializerCore();
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

        internal bool ConfirmCompleteCleanup()
        {
            lock (router.gate)
            {
                if (!retired || cleanupConfirmed || quarantined || !retirement.Task.IsCompletedSuccessfully)
                {
                    return false;
                }

                int slot = Array.IndexOf(router.registrations, this);
                if (slot < 0)
                {
                    return false;
                }

                cleanupConfirmed = true;
                router.registrations[slot] = null;
                return true;
            }
        }

        internal void Quarantine()
        {
            lock (router.gate)
            {
                if (cleanupConfirmed)
                {
                    return;
                }

                AdmissionClosed = true;
                quarantined = true;
                AbandonInitializerCore();
            }
        }

        internal void Receive(MacOSRemoteWindowDelegateSignal signal)
        {
            Action? notify;
            lock (router.gate)
            {
                if (!published || retired || retiring || AdmissionClosed
                    || signal is not (MacOSRemoteWindowDelegateSignal.StoppedWithError
                    or MacOSRemoteWindowDelegateSignal.Inactive))
                {
                    return;
                }

                AdmissionClosed = true;
                terminal = true;
                notify = handler;
                if (notify is not null)
                {
                    invocations++;
                }
            }

            if (notify is not null)
            {
                Invoke(notify);
            }
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
                    // Keep the primary fault even if scope restoration later fails.
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
                    lock (router.gate)
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

        private void RecordFailure(Exception exception)
        {
            AdmissionClosed = true;
            Interlocked.CompareExchange(ref failure, exception, null);
            if (MacOSRemoteWindowFailure.FindFatal(exception) is { } fatal)
            {
                Interlocked.CompareExchange(ref fatalFailure, fatal, null);
            }
        }

        private void Detach()
        {
            retired = true;
            handler = null;
        }

        // Called only under the shared gate; cannot affect a later initializer.
        private void AbandonInitializerCore()
        {
            if (ReferenceEquals(router.initializer, this))
            {
                router.poisoned |= published;
                router.initializer = null;
            }
        }

        private sealed class CallbackScope : IDisposable
        {
            [ThreadStatic] private static CallbackScope? current;
            private readonly Registration owner;
            private readonly CallbackScope? previous;

            internal CallbackScope(Registration owner)
            {
                this.owner = owner;
                previous = current;
                current = this;
            }

            internal static bool Contains(Registration owner)
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
}
