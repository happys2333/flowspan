using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Transport;

public sealed class AuthenticatedRemoteWindowConnectionLease : IAsyncDisposable
{
    private readonly RemoteWindowConnectionGeneration generation;
    private readonly AuthenticatedRemoteWindowMediaSession mediaSession;
    private readonly IRemoteWindowPreparationChannel preparationChannel;
    private int disposed;

    internal AuthenticatedRemoteWindowConnectionLease(
        RemoteWindowConnectionGeneration generation,
        IRemoteWindowPreparationChannel preparationChannel,
        AuthenticatedRemoteWindowMediaSession mediaSession)
    {
        this.generation = generation
            ?? throw new ArgumentNullException(nameof(generation));
        this.preparationChannel = preparationChannel
            ?? throw new ArgumentNullException(nameof(preparationChannel));
        this.mediaSession = mediaSession
            ?? throw new ArgumentNullException(nameof(mediaSession));
        Generation = generation.Value;
        LocalDeviceId = mediaSession.LocalDeviceId;
        PeerDeviceId = mediaSession.PeerDeviceId;
        ProtocolVersion = mediaSession.ProtocolVersion;
    }

    public long Generation { get; }

    public bool IsCurrent => Volatile.Read(ref disposed) == 0
        && generation.IsCurrent
        && mediaSession.IsCurrent;

    public DeviceId LocalDeviceId { get; }

    public DeviceId PeerDeviceId { get; }

    public ProtocolVersion ProtocolVersion { get; }

    public bool IsRevoked => generation.IsRevoked;

    public CancellationTokenRegistration RegisterRevocationCallback(
        Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return generation.RegisterRevocationCallback(callback);
    }

    public RemoteWindowMediaRouteBinding PrepareResponderRoute(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        TimeSpan? lifetime = null)
    {
        ThrowIfUnavailable();
        return mediaSession.PrepareResponderRoute(
            sessionId,
            activityId,
            lifetime);
    }

    public async ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
        RemoteWindowPreparationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.HostDeviceId != LocalDeviceId
            || request.ParticipantDeviceId != PeerDeviceId)
        {
            throw new ArgumentException(
                "A host preparation must match this authenticated connection generation.",
                nameof(request));
        }

        using CancellationTokenSource linked = CreateLinkedCancellation(
            cancellationToken);
        return await preparationChannel.PrepareAsync(request, linked.Token)
            .ConfigureAwait(false);
    }

    public async ValueTask PublishAdmissionStateAsync(
        RemoteWindowParticipantState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.HostDeviceId != LocalDeviceId
            || state.ParticipantDeviceId != PeerDeviceId)
        {
            throw new ArgumentException(
                "A host admission must match this authenticated connection generation.",
                nameof(state));
        }

        using CancellationTokenSource linked = CreateLinkedCancellation(
            cancellationToken);
        await preparationChannel.PublishAdmissionStateAsync(state, linked.Token)
            .ConfigureAwait(false);
    }

    public async ValueTask ConnectInitiatorAsync(
        Stream stream,
        RemoteWindowPreparationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(request);
        if (request.HostDeviceId != PeerDeviceId
            || request.ParticipantDeviceId != LocalDeviceId)
        {
            throw new ArgumentException(
                "A participant attachment must match this authenticated connection generation.",
                nameof(request));
        }

        using CancellationTokenSource linked = CreateLinkedCancellation(
            cancellationToken);
        await mediaSession.ConnectInitiatorAsync(
                stream,
                request.SessionId,
                request.ActivityId,
                linked.Token)
            .ConfigureAwait(false);
    }

    public async ValueTask WaitForMediaAttachmentAsync(
        CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource linked = CreateLinkedCancellation(
            cancellationToken);
        await mediaSession.WaitForAttachmentAsync(linked.Token)
            .ConfigureAwait(false);
    }

    public async ValueTask SendMediaAsync(
        RemoteWindowMediaFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        using CancellationTokenSource linked = CreateLinkedCancellation(
            cancellationToken);
        await mediaSession.SendAsync(frame, linked.Token).ConfigureAwait(false);
    }

    public async ValueTask<RemoteWindowMediaFrame> ReceiveMediaAsync(
        CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource linked = CreateLinkedCancellation(
            cancellationToken);
        return await mediaSession.ReceiveAsync(linked.Token).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            generation.ReleaseLease();
        }

        return ValueTask.CompletedTask;
    }

    private CancellationTokenSource CreateLinkedCancellation(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return generation.CreateLinkedCancellation(cancellationToken);
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!generation.IsCurrent || !mediaSession.IsCurrent)
        {
            throw new InvalidOperationException(
                "The authenticated Remote Window connection generation was revoked.");
        }
    }
}

internal sealed class RemoteWindowConnectionGeneration : IDisposable
{
    private static readonly AsyncLocal<RevocationCallbackAncestry?>
        revocationCallbackAncestry = new();
    private readonly object gate = new();
    private readonly CancellationTokenSource revocation = new();
    private readonly object? revocationCallbackOwner;
    private int activeLeases;
    private bool cancellationCompleted;
    private bool ownerReleased;
    private int registrationOperations;
    private bool revoked;
    private bool revocationDisposed;

    internal RemoteWindowConnectionGeneration(
        long value,
        object? revocationCallbackOwner = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        Value = value;
        this.revocationCallbackOwner = revocationCallbackOwner;
    }

    internal bool IsCurrent
    {
        get
        {
            lock (gate)
            {
                return !revoked;
            }
        }
    }

    internal bool IsRevoked
    {
        get
        {
            lock (gate)
            {
                return revoked;
            }
        }
    }

    internal long Value { get; }

    internal CancellationTokenRegistration RegisterRevocationCallback(
        Action callback)
    {
        CancellationToken token;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(revocationDisposed, revocation);
            registrationOperations++;
            token = revocation.Token;
        }

        try
        {
            return token.UnsafeRegister(
                static state =>
                {
                    var invocation = (RevocationCallbackInvocation)state!;
                    invocation.Generation.InvokeRevocationCallback(
                        invocation.Callback);
                },
                new RevocationCallbackInvocation(this, callback));
        }
        finally
        {
            bool dispose;
            lock (gate)
            {
                registrationOperations--;
                dispose = ClaimRevocationDisposalIfReady();
            }

            if (dispose)
            {
                revocation.Dispose();
            }
        }
    }

    internal static bool IsActiveRevocationCallback(object owner)
    {
        for (RevocationCallbackAncestry? ancestry =
                 revocationCallbackAncestry.Value;
            ancestry is not null;
            ancestry = ancestry.Previous)
        {
            if (ancestry.Marker.IsActive
                && ReferenceEquals(ancestry.Marker.Owner, owner))
            {
                return true;
            }
        }

        return false;
    }

    internal bool TryAcquire(
        IRemoteWindowPreparationChannel preparationChannel,
        AuthenticatedRemoteWindowMediaSession mediaSession,
        out AuthenticatedRemoteWindowConnectionLease? lease)
    {
        lock (gate)
        {
            if (revoked || ownerReleased || !mediaSession.IsCurrent)
            {
                lease = null;
                return false;
            }

            activeLeases++;
            lease = new AuthenticatedRemoteWindowConnectionLease(
                this,
                preparationChannel,
                mediaSession);
            return true;
        }
    }

    internal CancellationTokenSource CreateLinkedCancellation(
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (revoked || ownerReleased)
            {
                throw new InvalidOperationException(
                    "The authenticated Remote Window connection generation was revoked.");
            }

            return CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                revocation.Token);
        }
    }

    internal Exception? RevokeAndReleaseOwner()
    {
        bool cancel;
        lock (gate)
        {
            if (ownerReleased)
            {
                return null;
            }

            cancel = !revoked;
            revoked = true;
            ownerReleased = true;
        }

        Exception? failure = null;
        if (cancel)
        {
            try
            {
                revocation.Cancel();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        bool dispose;
        lock (gate)
        {
            cancellationCompleted = true;
            dispose = ClaimRevocationDisposalIfReady();
        }

        if (dispose)
        {
            try
            {
                revocation.Dispose();
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(
                        "Remote Window generation revocation and cleanup failed.",
                        failure,
                        exception);
            }
        }

        return failure;
    }

    public void Dispose()
    {
        Exception? failure = RevokeAndReleaseOwner();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(failure)
                .Throw();
        }
    }

    internal void ReleaseLease()
    {
        bool dispose;
        lock (gate)
        {
            if (activeLeases <= 0)
            {
                throw new InvalidOperationException(
                    "A Remote Window connection lease was released more than once.");
            }

            activeLeases--;
            dispose = ClaimRevocationDisposalIfReady();
        }

        if (dispose)
        {
            revocation.Dispose();
        }
    }

    private void InvokeRevocationCallback(Action callback)
    {
        object? owner = revocationCallbackOwner;
        if (owner is null)
        {
            callback();
            return;
        }

        var marker = new RevocationCallbackMarker(owner);
        RevocationCallbackAncestry? inherited =
            revocationCallbackAncestry.Value;
        revocationCallbackAncestry.Value = new RevocationCallbackAncestry(
            marker,
            inherited);
        marker.Activate();
        try
        {
            callback();
        }
        finally
        {
            marker.Deactivate();
            revocationCallbackAncestry.Value = inherited;
        }
    }

    private sealed record RevocationCallbackInvocation(
        RemoteWindowConnectionGeneration Generation,
        Action Callback);

    private sealed class RevocationCallbackAncestry(
        RevocationCallbackMarker marker,
        RevocationCallbackAncestry? previous)
    {
        public RevocationCallbackMarker Marker { get; } = marker;

        public RevocationCallbackAncestry? Previous { get; } = previous;
    }

    private sealed class RevocationCallbackMarker(object owner)
    {
        private int active;

        public bool IsActive => Volatile.Read(ref active) > 0;

        public object Owner { get; } = owner;

        public void Activate() => Interlocked.Increment(ref active);

        public void Deactivate() => Interlocked.Decrement(ref active);
    }

    private bool ClaimRevocationDisposalIfReady()
    {
        if (revocationDisposed
            || !ownerReleased
            || !cancellationCompleted
            || registrationOperations != 0
            || activeLeases != 0)
        {
            return false;
        }

        revocationDisposed = true;
        return true;
    }
}
