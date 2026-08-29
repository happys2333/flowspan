using System.Runtime.ExceptionServices;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Transport;

internal interface IRemoteWindowPeerStreamConnector
{
    public ValueTask<Stream> ConnectAsync(
        System.Net.IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken);
}

internal sealed class SystemRemoteWindowPeerStreamConnector :
    IRemoteWindowPeerStreamConnector
{
    private SystemRemoteWindowPeerStreamConnector()
    {
    }

    public static SystemRemoteWindowPeerStreamConnector Instance { get; } = new();

    public async ValueTask<Stream> ConnectAsync(
        System.Net.IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        DirectTcpPeerConnection connection =
            await DirectTcpPeerConnection.ConnectAsync(
                    remoteEndPoint,
                    cancellationToken)
                .ConfigureAwait(false);
        try
        {
            return connection.TakeRemoteWindowMediaStream();
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}

public sealed class AuthenticatedRemoteWindowConnectionLease : IAsyncDisposable
{
    private readonly Func<ValueTask> failClose;
    private readonly RemoteWindowConnectionGeneration generation;
    private readonly AuthenticatedRemoteWindowMediaSession mediaSession;
    private readonly IRemoteWindowPreparationChannel preparationChannel;
    private int disposed;

    internal AuthenticatedRemoteWindowConnectionLease(
        RemoteWindowConnectionGeneration generation,
        IRemoteWindowPreparationChannel preparationChannel,
        AuthenticatedRemoteWindowMediaSession mediaSession,
        Func<ValueTask> failClose)
    {
        this.generation = generation
            ?? throw new ArgumentNullException(nameof(generation));
        this.preparationChannel = preparationChannel
            ?? throw new ArgumentNullException(nameof(preparationChannel));
        this.mediaSession = mediaSession
            ?? throw new ArgumentNullException(nameof(mediaSession));
        this.failClose = failClose
            ?? throw new ArgumentNullException(nameof(failClose));
        Generation = generation.Value;
        LocalDeviceId = mediaSession.LocalDeviceId;
        PeerDeviceId = mediaSession.PeerDeviceId;
        ProtocolVersion = mediaSession.ProtocolVersion;
        PeerConnectionCandidate = generation.PeerConnectionCandidate;
    }

    public long Generation { get; }

    public bool IsCurrent => Volatile.Read(ref disposed) == 0
        && generation.IsCurrent
        && mediaSession.IsCurrent;

    public DeviceId LocalDeviceId { get; }

    public DeviceId PeerDeviceId { get; }

    public VerifiedPeerConnectionCandidate? PeerConnectionCandidate { get; }

    public ProtocolVersion ProtocolVersion { get; }

    public bool IsRevoked => generation.IsRevoked;

    public CancellationTokenRegistration RegisterRevocationCallback(
        Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return generation.RegisterRevocationCallback(callback);
    }

    public ValueTask FailCloseAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return FailCloseAdmittedOperationAsync();
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
        RemoteWindowPreparationRequest request,
        CancellationToken cancellationToken = default) =>
        await ConnectInitiatorCoreAsync(
                request,
                SystemRemoteWindowPeerStreamConnector.Instance,
                failCloseImmediately: true,
                cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask ConnectInitiatorForPreparationAsync(
        RemoteWindowPreparationRequest request,
        CancellationToken cancellationToken = default) =>
        await ConnectInitiatorCoreAsync(
                request,
                SystemRemoteWindowPeerStreamConnector.Instance,
                failCloseImmediately: false,
                cancellationToken)
            .ConfigureAwait(false);

    internal async ValueTask ConnectInitiatorAsync(
        RemoteWindowPreparationRequest request,
        IRemoteWindowPeerStreamConnector connector,
        CancellationToken cancellationToken) =>
        await ConnectInitiatorCoreAsync(
                request,
                connector,
                failCloseImmediately: true,
                cancellationToken)
            .ConfigureAwait(false);

    internal async ValueTask ConnectInitiatorForPreparationAsync(
        RemoteWindowPreparationRequest request,
        IRemoteWindowPeerStreamConnector connector,
        CancellationToken cancellationToken) =>
        await ConnectInitiatorCoreAsync(
                request,
                connector,
                failCloseImmediately: false,
                cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask ConnectInitiatorCoreAsync(
        RemoteWindowPreparationRequest request,
        IRemoteWindowPeerStreamConnector connector,
        bool failCloseImmediately,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ValidateInitiatorRequest(request);
        RemoteWindowPeerConnectionOperation? operation = null;
        Stream? ownedStream = null;
        Exception? primaryFailure = null;
        Exception? failCloseFailure = null;
        Task? failCloseTask = null;
        try
        {
            operation = generation.BeginPeerConnectionOperation(
                cancellationToken);
            VerifiedPeerConnectionCandidate candidate =
                generation.GetCurrentPeerConnectionCandidate(ProtocolVersion);
            ownedStream = await connector.ConnectAsync(
                        candidate.EndPoint,
                        operation.Token)
                    .ConfigureAwait(false);
            _ = generation.GetCurrentPeerConnectionCandidate(ProtocolVersion);
            ValueTask attaching = failCloseImmediately
                ? mediaSession.ConnectInitiatorAsync(
                    ownedStream,
                    request.SessionId,
                    request.ActivityId,
                    operation.Token)
                : mediaSession.ConnectInitiatorForPreparationAsync(
                    ownedStream,
                    request.SessionId,
                    request.ActivityId,
                    operation.Token);
            ownedStream = null;
            await attaching.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            generation.MarkFailClosePending();
            if (failCloseImmediately)
            {
                StartFailClose();
            }
        }
        finally
        {
            Exception? operationCleanupFailure = null;
            if (ownedStream is not null)
            {
                try
                {
                    await ownedStream.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    operationCleanupFailure = exception;
                }
            }

            if (operation is not null)
            {
                operationCleanupFailure = CombineFailures(
                    operationCleanupFailure,
                    operation.Complete());
            }

            primaryFailure = CombineFailures(
                primaryFailure,
                operationCleanupFailure);
        }

        if (primaryFailure is null)
        {
            return;
        }

        generation.MarkFailClosePending();
        if (failCloseImmediately)
        {
            StartFailClose();
            if (failCloseTask is not null)
            {
                try
                {
                    await failCloseTask.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failCloseFailure = CombineFailures(
                        failCloseFailure,
                        exception);
                }
            }
        }

        Exception failure = failCloseFailure is null
            ? primaryFailure!
            : new AggregateException(
                "The verified Remote Window peer connection and fail-close cleanup both failed.",
                primaryFailure!,
                failCloseFailure);
        ExceptionDispatchInfo.Capture(failure).Throw();

        void StartFailClose()
        {
            if (failCloseTask is not null || failCloseFailure is not null)
            {
                return;
            }

            try
            {
                failCloseTask = FailCloseAdmittedOperationAsync().AsTask();
            }
            catch (Exception exception)
            {
                failCloseFailure = exception;
            }
        }
    }

    internal async ValueTask ConnectInitiatorAsync(
        Stream stream,
        RemoteWindowPreparationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateInitiatorRequest(request);

        using CancellationTokenSource linked = CreateLinkedCancellation(
            cancellationToken);
        await mediaSession.ConnectInitiatorAsync(
                stream,
                request.SessionId,
                request.ActivityId,
                linked.Token)
            .ConfigureAwait(false);
    }

    private void ValidateInitiatorRequest(
        RemoteWindowPreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.HostDeviceId != PeerDeviceId
            || request.ParticipantDeviceId != LocalDeviceId)
        {
            throw new ArgumentException(
                "A participant attachment must match this authenticated connection generation.",
                nameof(request));
        }
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
                "The authenticated Remote Window connection generation is no longer current.");
        }
    }

    private ValueTask FailCloseAdmittedOperationAsync() =>
        generation.IsActiveRevocationCallback()
            ? ValueTask.CompletedTask
            : failClose();

    private static Exception? CombineFailures(
        Exception? primary,
        Exception? secondary) => (primary, secondary) switch
        {
            (null, null) => null,
            (not null, null) => primary,
            (null, not null) => secondary,
            _ => new AggregateException(
                "Remote Window peer connection cleanup failed.",
                primary!,
                secondary!),
        };
}

internal sealed class RemoteWindowConnectionGeneration : IDisposable
{
    private static readonly AsyncLocal<RevocationCallbackAncestry?>
        revocationCallbackAncestry = new();
    private readonly object gate = new();
    private readonly CancellationTokenSource revocation = new();
    private readonly object? revocationCallbackOwner;
    private readonly IVerifiedPeerConnectionCandidateValidator? candidateValidator;
    private readonly VerifiedPeerConnectionCandidate? peerConnectionCandidate;
    private int activeLeases;
    private int activePeerConnections;
    private bool cancellationCompleted;
    private bool ownerReleased;
    private int registrationOperations;
    private bool failClosePending;
    private bool revoked;
    private bool revocationDisposed;
    private TaskCompletionSource peerConnectionsDrained =
        CreateCompletedSignal();

    internal RemoteWindowConnectionGeneration(
        long value,
        object? revocationCallbackOwner = null,
        VerifiedPeerConnectionCandidate? peerConnectionCandidate = null,
        IVerifiedPeerConnectionCandidateValidator? candidateValidator = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        if ((peerConnectionCandidate is null) != (candidateValidator is null))
        {
            throw new ArgumentException(
                "A verified peer candidate and validator must be supplied together.");
        }

        Value = value;
        this.revocationCallbackOwner = revocationCallbackOwner;
        this.peerConnectionCandidate = peerConnectionCandidate;
        this.candidateValidator = candidateValidator;
    }

    internal bool IsCurrent
    {
        get
        {
            lock (gate)
            {
                return !revoked && !failClosePending;
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

    internal VerifiedPeerConnectionCandidate? PeerConnectionCandidate =>
        peerConnectionCandidate;

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

    internal bool IsActiveRevocationCallback()
    {
        for (RevocationCallbackAncestry? ancestry =
                 revocationCallbackAncestry.Value;
            ancestry is not null;
            ancestry = ancestry.Previous)
        {
            if (ancestry.Marker.IsActive
                && ReferenceEquals(ancestry.Marker.Generation, this))
            {
                return true;
            }
        }

        return false;
    }

    internal bool TryAcquire(
        IRemoteWindowPreparationChannel preparationChannel,
        AuthenticatedRemoteWindowMediaSession mediaSession,
        Func<ValueTask> failClose,
        out AuthenticatedRemoteWindowConnectionLease? lease) => TryAcquire(
            preparationChannel,
            mediaSession,
            failClose,
            requireVerifiedPeer: false,
            out lease);

    internal bool TryAcquire(
        IRemoteWindowPreparationChannel preparationChannel,
        AuthenticatedRemoteWindowMediaSession mediaSession,
        Func<ValueTask> failClose,
        bool requireVerifiedPeer,
        out AuthenticatedRemoteWindowConnectionLease? lease)
    {
        if (requireVerifiedPeer
            && !IsPeerConnectionCandidateCurrent(mediaSession.ProtocolVersion))
        {
            lease = null;
            return false;
        }

        lock (gate)
        {
            if (revoked
                || failClosePending
                || ownerReleased
                || !mediaSession.IsCurrent)
            {
                lease = null;
                return false;
            }

            activeLeases++;
            lease = new AuthenticatedRemoteWindowConnectionLease(
                this,
                preparationChannel,
                mediaSession,
                failClose);
            return true;
        }
    }

    internal bool IsPeerConnectionCandidateCurrent(
        ProtocolVersion protocolVersion)
    {
        VerifiedPeerConnectionCandidate? candidate = peerConnectionCandidate;
        IVerifiedPeerConnectionCandidateValidator? validator = candidateValidator;
        lock (gate)
        {
            if (revoked
                || failClosePending
                || ownerReleased
                || candidate is null
                || validator is null)
            {
                return false;
            }
        }

        try
        {
            if (!validator.IsCurrent(candidate, protocolVersion))
            {
                return false;
            }

            lock (gate)
            {
                return !revoked && !failClosePending && !ownerReleased;
            }
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    internal VerifiedPeerConnectionCandidate GetCurrentPeerConnectionCandidate(
        ProtocolVersion protocolVersion)
    {
        if (!IsPeerConnectionCandidateCurrent(protocolVersion))
        {
            throw new InvalidOperationException(
                "The verified Remote Window peer endpoint is no longer current for this connection generation.");
        }

        return peerConnectionCandidate!;
    }

    internal RemoteWindowPeerConnectionOperation BeginPeerConnectionOperation(
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (revoked || failClosePending || ownerReleased)
            {
                throw new InvalidOperationException(
                    "The authenticated Remote Window connection generation is no longer current.");
            }

            if (peerConnectionCandidate is null || candidateValidator is null)
            {
                throw new InvalidOperationException(
                    "The authenticated Remote Window connection generation has no verified peer endpoint.");
            }

            if (activePeerConnections != 0)
            {
                throw new InvalidOperationException(
                    "The authenticated Remote Window connection generation already has a pending peer connection.");
            }

            peerConnectionsDrained = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            activePeerConnections = 1;
            try
            {
                return new RemoteWindowPeerConnectionOperation(
                    this,
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        revocation.Token));
            }
            catch
            {
                activePeerConnections = 0;
                peerConnectionsDrained.TrySetResult();
                throw;
            }
        }
    }

    internal Task WaitForPeerConnectionsAsync()
    {
        lock (gate)
        {
            return peerConnectionsDrained.Task;
        }
    }

    internal void CompletePeerConnection(Exception? cleanupFailure)
    {
        TaskCompletionSource completion;
        bool dispose;
        lock (gate)
        {
            if (activePeerConnections != 1)
            {
                throw new InvalidOperationException(
                    "A Remote Window peer connection operation completed more than once.");
            }

            activePeerConnections = 0;
            completion = peerConnectionsDrained;
            dispose = ClaimRevocationDisposalIfReady();
        }

        if (cleanupFailure is null)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(cleanupFailure);
        }

        if (dispose)
        {
            revocation.Dispose();
        }
    }

    internal CancellationTokenSource CreateLinkedCancellation(
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (revoked || failClosePending || ownerReleased)
            {
                throw new InvalidOperationException(
                    "The authenticated Remote Window connection generation is no longer current.");
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

    internal void MarkFailClosePending()
    {
        lock (gate)
        {
            if (!revoked && !ownerReleased)
            {
                failClosePending = true;
            }
        }
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

        var marker = new RevocationCallbackMarker(this, owner);
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

    private sealed class RevocationCallbackMarker(
        RemoteWindowConnectionGeneration generation,
        object owner)
    {
        private int active;

        public bool IsActive => Volatile.Read(ref active) > 0;

        public RemoteWindowConnectionGeneration Generation { get; } = generation;

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
            || activePeerConnections != 0
            || activeLeases != 0)
        {
            return false;
        }

        revocationDisposed = true;
        return true;
    }

    private static TaskCompletionSource CreateCompletedSignal()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        completion.TrySetResult();
        return completion;
    }
}

internal sealed class RemoteWindowPeerConnectionOperation
{
    private readonly CancellationTokenSource cancellation;
    private RemoteWindowConnectionGeneration? generation;

    internal RemoteWindowPeerConnectionOperation(
        RemoteWindowConnectionGeneration generation,
        CancellationTokenSource cancellation)
    {
        this.generation = generation;
        this.cancellation = cancellation;
    }

    internal CancellationToken Token => cancellation.Token;

    internal Exception? Complete()
    {
        RemoteWindowConnectionGeneration? owner = Interlocked.Exchange(
            ref generation,
            null);
        if (owner is null)
        {
            throw new InvalidOperationException(
                "A Remote Window peer connection operation completed more than once.");
        }

        Exception? cleanupFailure = null;
        try
        {
            cancellation.Dispose();
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }

        owner.CompletePeerConnection(cleanupFailure);
        return cleanupFailure;
    }
}
