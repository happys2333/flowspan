using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Desktop;

public sealed record DesktopPairingPrompt(
    Guid PromptId,
    string PeerDisplayName,
    string PeerDeviceId,
    string PeerFingerprint,
    string ProtocolVersion,
    string ShortAuthenticationString,
    DateTimeOffset ExpiresAt);

public enum DesktopPairingPromptChangeKind
{
    Opened,
    Accepted,
    Rejected,
    Canceled,
    Disposed,
}

public sealed class DesktopPairingPromptChangedEventArgs(
    long sequence,
    DesktopPairingPromptChangeKind kind) : EventArgs
{
    public DesktopPairingPromptChangeKind Kind { get; } = kind;

    public long Sequence { get; } = sequence;
}

public sealed class DesktopPairingDecisionSource : IPairingDecisionSource, IDisposable
{
    private static readonly AsyncLocal<CancellationPublicationLease?>
        CurrentCancellationPublication = new();

    private readonly Action<DesktopPairingPromptChangedEventArgs>?
        beforeCancellationChangeQueued;
    private readonly Lock cancellationPublicationGate = new();
    private readonly Lock gate = new();
    private readonly Action<Action> scheduleCancellationPublication;
    private CancellationPublicationLease? activeCancellationPublication;
    private ActivePrompt? activePrompt;
    private bool cancellationPublicationClosed;
    private TaskCompletionSource? cancellationPublicationDrainCompletion;
    private bool cancellationPublicationWorkerRunning;
    private bool disposed;
    private DesktopPairingPromptChangedEventArgs? pendingCancellationChange;
    private long sequence;

    public DesktopPairingDecisionSource()
        : this(static publish => _ = Task.Run(publish))
    {
    }

    internal DesktopPairingDecisionSource(
        Action<Action> scheduleCancellationPublication,
        Action<DesktopPairingPromptChangedEventArgs>?
            beforeCancellationChangeQueued = null)
    {
        ArgumentNullException.ThrowIfNull(scheduleCancellationPublication);
        this.scheduleCancellationPublication = scheduleCancellationPublication;
        this.beforeCancellationChangeQueued = beforeCancellationChangeQueued;
    }

    public event EventHandler<DesktopPairingPromptChangedEventArgs>? PromptChanged;

    public DesktopPairingPrompt? CurrentPrompt
    {
        get
        {
            lock (gate)
            {
                return activePrompt?.Prompt;
            }
        }
    }

    public ValueTask<PairingDecision> DecideAsync(
        PairingConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.PeerIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.ShortAuthenticationString);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.ProtocolVersion.Major < 1
            || request.ProtocolVersion.Minor < 0
            || request.ShortAuthenticationString.Length != 6
            || request.ShortAuthenticationString.Any(static character =>
                !char.IsAsciiDigit(character)))
        {
            throw new ArgumentException(
                "A desktop pairing request requires one six-digit authentication string.",
                nameof(request));
        }

        ActivePrompt pending;
        DesktopPairingPromptChangedEventArgs opened;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (activePrompt is not null)
            {
                return ValueTask.FromResult(PairingDecision.Reject);
            }

            var prompt = new DesktopPairingPrompt(
                Guid.NewGuid(),
                request.PeerIdentity.DisplayName,
                request.PeerIdentity.DeviceId.ToString(),
                request.PeerIdentity.Fingerprint,
                request.ProtocolVersion.ToString(),
                request.ShortAuthenticationString,
                request.ExpiresAt);
            pending = new ActivePrompt(
                prompt,
                new TaskCompletionSource<PairingDecision>(
                    TaskCreationOptions.RunContinuationsAsynchronously));
            activePrompt = pending;
            opened = NextChange(DesktopPairingPromptChangeKind.Opened);
        }

        var cancellationState = new PromptCancellation(
            this,
            pending,
            cancellationToken);
        CancellationTokenRegistration cancellationRegistration =
            cancellationToken.UnsafeRegister(
                static state =>
                {
                    var cancellation = (PromptCancellation)state!;
                    cancellation.Owner.Cancel(
                        cancellation.Prompt,
                        cancellation.Token);
                },
                cancellationState);
        Publish(opened);
        return new ValueTask<PairingDecision>(AwaitDecisionAsync(
            pending.Completion.Task,
            cancellationRegistration));
    }

    public bool TryAccept(Guid promptId, CapabilityGrant capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return TryResolve(
            promptId,
            new PairingDecision(accepted: true, capabilities),
            DesktopPairingPromptChangeKind.Accepted);
    }

    public bool TryReject(Guid promptId) => TryResolve(
        promptId,
        PairingDecision.Reject,
        DesktopPairingPromptChangeKind.Rejected);

    internal void RunWithCancellationPublicationsDeferred(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (cancellationPublicationGate)
        {
            action();
        }
    }

    public void Dispose()
    {
        ActivePrompt? pending;
        DesktopPairingPromptChangedEventArgs? change;
        bool firstDisposal = false;
        lock (gate)
        {
            if (!disposed)
            {
                disposed = true;
                firstDisposal = true;
                pending = activePrompt;
                activePrompt = null;
                change = pending is null
                    ? null
                    : NextChange(DesktopPairingPromptChangeKind.Disposed);
            }
            else
            {
                pending = null;
                change = null;
            }
        }

        CloseCancellationPublications();
        if (firstDisposal && pending is not null)
        {
            pending.Completion.TrySetResult(PairingDecision.Reject);
            Publish(change!);
        }
    }

    private static async Task<PairingDecision> AwaitDecisionAsync(
        Task<PairingDecision> decision,
        CancellationTokenRegistration cancellationRegistration)
    {
        using (cancellationRegistration)
        {
            return await decision.ConfigureAwait(false);
        }
    }

    private void Cancel(ActivePrompt pending, CancellationToken cancellationToken)
    {
        DesktopPairingPromptChangedEventArgs? change;
        lock (gate)
        {
            if (!ReferenceEquals(activePrompt, pending))
            {
                return;
            }

            activePrompt = null;
            change = NextChange(DesktopPairingPromptChangeKind.Canceled);
        }

        pending.Completion.TrySetCanceled(cancellationToken);
        beforeCancellationChangeQueued?.Invoke(change);
        QueueCancellationChange(change);
    }

    private void QueueCancellationChange(
        DesktopPairingPromptChangedEventArgs eventArgs)
    {
        bool startWorker = false;
        lock (cancellationPublicationGate)
        {
            if (cancellationPublicationClosed)
            {
                return;
            }

            if (pendingCancellationChange is null
                || eventArgs.Sequence > pendingCancellationChange.Sequence)
            {
                pendingCancellationChange = eventArgs;
            }

            if (!cancellationPublicationWorkerRunning)
            {
                cancellationPublicationWorkerRunning = true;
                startWorker = true;
            }
        }

        if (startWorker)
        {
            scheduleCancellationPublication(PublishCancellationChanges);
        }
    }

    private void PublishCancellationChanges()
    {
        while (true)
        {
            DesktopPairingPromptChangedEventArgs? change;
            CancellationPublicationLease lease;
            TaskCompletionSource? drainToComplete = null;
            lock (cancellationPublicationGate)
            {
                if (cancellationPublicationClosed)
                {
                    pendingCancellationChange = null;
                }

                change = pendingCancellationChange;
                pendingCancellationChange = null;
                if (change is null)
                {
                    cancellationPublicationWorkerRunning = false;
                    drainToComplete = cancellationPublicationDrainCompletion;
                    lease = null!;
                }
                else
                {
                    lease = new CancellationPublicationLease(this);
                    activeCancellationPublication = lease;
                }
            }

            if (change is null)
            {
                drainToComplete?.TrySetResult();
                return;
            }

            CancellationPublicationLease? previous =
                CurrentCancellationPublication.Value;
            CurrentCancellationPublication.Value = lease;
            try
            {
                Publish(change);
            }
            finally
            {
                lease.Deactivate();
                CurrentCancellationPublication.Value = previous;
                lock (cancellationPublicationGate)
                {
                    if (ReferenceEquals(activeCancellationPublication, lease))
                    {
                        activeCancellationPublication = null;
                    }
                }
            }
        }
    }

    private void CloseCancellationPublications()
    {
        CancellationPublicationLease? callerLease =
            CurrentCancellationPublication.Value;
        Task drainTask;
        lock (cancellationPublicationGate)
        {
            cancellationPublicationClosed = true;
            pendingCancellationChange = null;
            if (callerLease is not null
                && callerLease.Active
                && ReferenceEquals(callerLease.Owner, this)
                && ReferenceEquals(activeCancellationPublication, callerLease))
            {
                return;
            }

            if (!cancellationPublicationWorkerRunning)
            {
                return;
            }

            cancellationPublicationDrainCompletion ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            drainTask = cancellationPublicationDrainCompletion.Task;
        }

        drainTask.GetAwaiter().GetResult();
    }

    private DesktopPairingPromptChangedEventArgs NextChange(
        DesktopPairingPromptChangeKind kind) => new(++sequence, kind);

    private void Publish(DesktopPairingPromptChangedEventArgs eventArgs)
    {
        foreach (EventHandler<DesktopPairingPromptChangedEventArgs> subscriber in
                 PromptChanged?.GetInvocationList()
                     .Cast<EventHandler<DesktopPairingPromptChangedEventArgs>>() ?? [])
        {
            try
            {
                subscriber(this, eventArgs);
            }
            catch
            {
                // A presentation subscriber cannot weaken or complete a pairing decision.
            }

            if (eventArgs.Kind == DesktopPairingPromptChangeKind.Canceled
                && IsCancellationPublicationClosed())
            {
                break;
            }
        }
    }

    private bool IsCancellationPublicationClosed()
    {
        lock (cancellationPublicationGate)
        {
            return cancellationPublicationClosed;
        }
    }

    private bool TryResolve(
        Guid promptId,
        PairingDecision decision,
        DesktopPairingPromptChangeKind kind)
    {
        ActivePrompt pending;
        DesktopPairingPromptChangedEventArgs change;
        lock (gate)
        {
            if (disposed
                || activePrompt is not { } current
                || current.Prompt.PromptId != promptId)
            {
                return false;
            }

            pending = current;
            activePrompt = null;
            change = NextChange(kind);
        }

        if (!pending.Completion.TrySetResult(decision))
        {
            return false;
        }

        Publish(change);
        return true;
    }

    private sealed record ActivePrompt(
        DesktopPairingPrompt Prompt,
        TaskCompletionSource<PairingDecision> Completion);

    private sealed record PromptCancellation(
        DesktopPairingDecisionSource Owner,
        ActivePrompt Prompt,
        CancellationToken Token);

    private sealed class CancellationPublicationLease(
        DesktopPairingDecisionSource owner)
    {
        private int active = 1;

        public bool Active => Volatile.Read(ref active) != 0;

        public DesktopPairingDecisionSource Owner { get; } = owner;

        public void Deactivate() => Volatile.Write(ref active, 0);
    }
}
