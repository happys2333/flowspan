using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Transport;

namespace Flowspan.Desktop;

internal sealed class DesktopRemoteWindowHostControlPeer :
    IRemoteWindowControlPeer
{
    private readonly AsyncLocal<ActiveCallScope?> activeCall = new();
    private readonly object gate = new();
    private readonly DeviceId hostDeviceId;
    private readonly object replacementGate = new();
    private RegistrationEntry? current;
    private RegistrationEntry? latestEntry;
    private long latestGeneration;

    public DesktopRemoteWindowHostControlPeer(DeviceId hostDeviceId) =>
        this.hostDeviceId = hostDeviceId
            ?? throw new ArgumentNullException(nameof(hostDeviceId));

    public ActivityId ActivityId => GetCurrentPeer().ActivityId;

    public DeviceId HostDeviceId => hostDeviceId;

    public RemoteWindowSessionId SessionId => GetCurrentPeer().SessionId;

    internal bool HasRetainedGeneration
    {
        get
        {
            lock (gate)
            {
                return latestEntry is not null;
            }
        }
    }

    public DesktopRemoteWindowHostControlRegistration Register(
        long generation,
        DeviceId participantDeviceId,
        RemoteWindowSessionId sessionId,
        RemoteWindowSessionController controller)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(generation, 1);
        ArgumentNullException.ThrowIfNull(participantDeviceId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(controller);
        if (participantDeviceId == hostDeviceId)
        {
            throw new ArgumentException(
                "A Remote Window host control participant must be remote.",
                nameof(participantDeviceId));
        }

        var peer = new RemoteWindowControllerControlPeer(sessionId, controller);
        if (peer.HostDeviceId != hostDeviceId)
        {
            throw new ArgumentException(
                "The Remote Window controller must represent this host.",
                nameof(controller));
        }

        if (IsActiveCall())
        {
            throw new InvalidOperationException(
                "A Remote Window host control generation cannot be replaced from one of its routed calls.");
        }

        var entry = new RegistrationEntry(
            generation,
            participantDeviceId,
            peer);
        lock (replacementGate)
        {
            Task? drain;
            lock (gate)
            {
                if (generation <= latestGeneration)
                {
                    throw new InvalidOperationException(
                        "A Remote Window host control generation must be newer than every prior registration.");
                }

                latestGeneration = generation;
                if (latestEntry is not { } previous)
                {
                    current = entry;
                    latestEntry = entry;
                    return new DesktopRemoteWindowHostControlRegistration(
                        this,
                        entry);
                }

                Retire(previous);
                drain = previous.DrainCompletion.Task;
            }

            drain.GetAwaiter().GetResult();
            lock (gate)
            {
                current = entry;
                latestEntry = entry;
            }
        }

        return new DesktopRemoteWindowHostControlRegistration(this, entry);
    }

    public async ValueTask<RemoteWindowParticipantState> AdmitAsync(
        RemoteWindowAdmissionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RegistrationEntry entry = Acquire(
            request.SessionId,
            request.ActivityId,
            request.HostDeviceId,
            request.ParticipantDeviceId);
        ActiveCallScope scope = EnterActiveCall(entry);
        try
        {
            return await entry.Peer.AdmitAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteActiveCall(entry, scope);
        }
    }

    public async ValueTask<RemoteWindowParticipantState> RequestDriverAsync(
        RemoteWindowDriverRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RegistrationEntry entry = Acquire(
            request.SessionId,
            request.ActivityId,
            request.HostDeviceId,
            request.ParticipantDeviceId);
        ActiveCallScope scope = EnterActiveCall(entry);
        try
        {
            return await entry.Peer.RequestDriverAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteActiveCall(entry, scope);
        }
    }

    public async ValueTask<RemoteWindowParticipantState> SendInputAsync(
        RemoteWindowInputRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RegistrationEntry entry = Acquire(
            request.SessionId,
            request.ActivityId,
            request.HostDeviceId,
            request.ParticipantDeviceId);
        ActiveCallScope scope = EnterActiveCall(entry);
        try
        {
            return await entry.Peer.SendInputAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteActiveCall(entry, scope);
        }
    }

    public async ValueTask<RemoteWindowParticipantState> DisconnectAsync(
        RemoteWindowDisconnectRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RegistrationEntry entry = Acquire(
            request.SessionId,
            request.ActivityId,
            request.HostDeviceId,
            request.ParticipantDeviceId);
        ActiveCallScope scope = EnterActiveCall(entry);
        try
        {
            return await entry.Peer.DisconnectAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteActiveCall(entry, scope);
        }
    }

    public async ValueTask PeerDisconnectedAsync(
        DeviceId peerDeviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        RegistrationEntry? entry = TryAcquire(peerDeviceId);
        if (entry is null)
        {
            return;
        }

        ActiveCallScope scope = EnterActiveCall(entry);
        try
        {
            await entry.Peer.PeerDisconnectedAsync(
                    peerDeviceId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteActiveCall(entry, scope);
        }
    }

    internal bool IsCurrent(RegistrationEntry entry)
    {
        lock (gate)
        {
            return ReferenceEquals(current, entry) && !entry.Retired;
        }
    }

    internal void CloseNow(RegistrationEntry entry)
    {
        lock (gate)
        {
            Retire(entry);
        }
    }

    internal void WaitForDrain(RegistrationEntry entry)
    {
        if (!IsActiveCall(entry))
        {
            entry.DrainCompletion.Task.GetAwaiter().GetResult();
        }
    }

    private RemoteWindowControllerControlPeer GetCurrentPeer()
    {
        lock (gate)
        {
            return current is { Retired: false } entry
                ? entry.Peer
                : throw new InvalidOperationException(
                    "No Remote Window host control generation is registered.");
        }
    }

    private RegistrationEntry Acquire(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId requestHostDeviceId,
        DeviceId participantDeviceId)
    {
        lock (gate)
        {
            RegistrationEntry entry = RequireCurrent();
            if (entry.Peer.SessionId != sessionId
                || entry.Peer.ActivityId != activityId
                || hostDeviceId != requestHostDeviceId
                || entry.ParticipantDeviceId != participantDeviceId)
            {
                throw BindingFailure();
            }

            entry.ActiveCalls++;
            return entry;
        }
    }

    private RegistrationEntry? TryAcquire(DeviceId participantDeviceId)
    {
        lock (gate)
        {
            if (current is not { Retired: false } entry
                || entry.ParticipantDeviceId != participantDeviceId)
            {
                return null;
            }

            entry.ActiveCalls++;
            return entry;
        }
    }

    private RegistrationEntry RequireCurrent() => current is
    { Retired: false } entry
        ? entry
        : throw new InvalidDataException(
            "No current Remote Window host control generation accepts this command.");

    private ActiveCallScope EnterActiveCall(RegistrationEntry entry)
    {
        ActiveCallScope? previous = activeCall.Value;
        var scope = new ActiveCallScope(entry, previous);
        activeCall.Value = scope;
        return scope;
    }

    private void CompleteActiveCall(
        RegistrationEntry entry,
        ActiveCallScope scope)
    {
        scope.Deactivate();
        activeCall.Value = scope.Previous;
        Release(entry);
    }

    private void Release(RegistrationEntry entry)
    {
        TaskCompletionSource? completed = null;
        lock (gate)
        {
            entry.ActiveCalls--;
            completed = CompleteRetirementIfDrained(entry);
        }

        completed?.TrySetResult();
    }

    private void Retire(RegistrationEntry entry)
    {
        entry.Retired = true;
        if (ReferenceEquals(current, entry))
        {
            current = null;
        }

        CompleteRetirementIfDrained(entry)?.TrySetResult();
    }

    private TaskCompletionSource? CompleteRetirementIfDrained(
        RegistrationEntry entry)
    {
        if (!entry.Retired || entry.ActiveCalls != 0)
        {
            return null;
        }

        if (ReferenceEquals(latestEntry, entry))
        {
            latestEntry = null;
        }

        return entry.DrainCompletion;
    }

    private bool IsActiveCall(RegistrationEntry? entry = null)
    {
        for (ActiveCallScope? scope = activeCall.Value;
            scope is not null;
            scope = scope.Previous)
        {
            if (scope.IsActive
                && (entry is null || ReferenceEquals(scope.Entry, entry)))
            {
                return true;
            }
        }

        return false;
    }

    private static InvalidDataException BindingFailure() => new(
        "The Remote Window command does not match the current host control generation.");

    internal sealed class RegistrationEntry(
        long generation,
        DeviceId participantDeviceId,
        RemoteWindowControllerControlPeer peer)
    {
        public int ActiveCalls { get; set; }

        public TaskCompletionSource DrainCompletion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public long Generation { get; } = generation;

        public DeviceId ParticipantDeviceId { get; } = participantDeviceId;

        public RemoteWindowControllerControlPeer Peer { get; } = peer;

        public bool Retired { get; set; }
    }

    private sealed class ActiveCallScope(
        RegistrationEntry entry,
        ActiveCallScope? previous)
    {
        private int active = 1;

        public RegistrationEntry Entry { get; } = entry;

        public bool IsActive => Volatile.Read(ref active) != 0;

        public ActiveCallScope? Previous { get; } = previous;

        public void Deactivate() => Volatile.Write(ref active, 0);
    }
}

internal sealed class DesktopRemoteWindowHostControlRegistration : IDisposable
{
    private readonly DesktopRemoteWindowHostControlPeer.RegistrationEntry entry;
    private readonly DesktopRemoteWindowHostControlPeer owner;

    internal DesktopRemoteWindowHostControlRegistration(
        DesktopRemoteWindowHostControlPeer owner,
        DesktopRemoteWindowHostControlPeer.RegistrationEntry entry)
    {
        this.owner = owner;
        this.entry = entry;
    }

    public bool IsCurrent => owner.IsCurrent(entry);

    public void CloseNow()
    {
        owner.CloseNow(entry);
    }

    public void Dispose()
    {
        CloseNow();
        owner.WaitForDrain(entry);
    }
}
