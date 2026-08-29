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

    public bool TryDeferFailCloseUntilPreparationDeadline(
        RemoteWindowPreparationRequest request)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ValidateInitiatorRequest(request);
        return generation.TryDeferFailCloseUntilPreparationDeadline(
            request,
            failClose);
    }

    public RemoteWindowMediaRouteBinding PrepareResponderRoute(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        TimeSpan? lifetime = null) => PrepareResponderRouteCore(
            sessionId,
            activityId,
            lifetime,
            admission: null);

    internal RemoteWindowMediaRouteBinding PrepareResponderRoute(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        IRemoteWindowHostPreparationAdmission admission,
        TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(admission);
        return PrepareResponderRouteCore(
            sessionId,
            activityId,
            lifetime,
            admission);
    }

    private RemoteWindowMediaRouteBinding PrepareResponderRouteCore(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        TimeSpan? lifetime,
        IRemoteWindowHostPreparationAdmission? admission)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!generation.TryBeginResponderRouteOperation(
                admission,
                out RemoteWindowResponderRouteOperation? operation)
            || operation is null)
        {
            throw new InvalidOperationException(
                "The authenticated Remote Window connection generation did not admit responder route selection.");
        }

        try
        {
            if (!mediaSession.IsCurrent)
            {
                throw new InvalidOperationException(
                    "The authenticated Remote Window media session is no longer current.");
            }

            RemoteWindowMediaRouteBinding binding =
                mediaSession.PrepareResponderRoute(
                    sessionId,
                    activityId,
                    lifetime);
            if (admission is not null && !admission.CompleteRouteSelection())
            {
                throw new InvalidOperationException(
                    "The host Preparation reservation became terminal during responder route selection.");
            }

            return binding;
        }
        catch
        {
            _ = admission?.TryFailRouteSelection();
            throw;
        }
        finally
        {
            operation.Complete();
        }
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

    internal async ValueTask<RemoteWindowPreparationDeliveryResult>
        PrepareReservedAsync(
        RemoteWindowPreparationRequest request,
        IRemoteWindowHostPreparationAdmission admission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(admission);
        if (request.HostDeviceId != LocalDeviceId
            || request.ParticipantDeviceId != PeerDeviceId)
        {
            throw new ArgumentException(
                "A host preparation must match this authenticated connection generation.",
                nameof(request));
        }

        if (preparationChannel is not IReservedRemoteWindowPreparationChannel reserved)
        {
            throw new InvalidOperationException(
                "The authenticated Remote Window preparation channel does not support host reservation admission.");
        }

        using CancellationTokenSource linked = CreateLinkedCancellation(
            cancellationToken);
        try
        {
            return await reserved.PrepareReservedAsync(
                    request,
                    admission,
                    linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (
            cancellationToken.IsCancellationRequested
            && exception.CancellationToken == linked.Token)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
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
        try
        {
            await preparationChannel.PublishAdmissionStateAsync(
                    state,
                    linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (
            cancellationToken.IsCancellationRequested
            && exception.CancellationToken == linked.Token)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
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
        bool failCloseDeferred = false;
        bool failCloseDeferralAttempted = false;
        bool failCloseDeferralEligible = !failCloseImmediately;
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
            bool cancellationMatched = exception is OperationCanceledException
                && (cancellationToken.IsCancellationRequested
                    || operation?.Token.IsCancellationRequested == true);
            failCloseDeferralEligible &= !cancellationMatched;
            if (failCloseDeferralEligible)
            {
                failCloseDeferralAttempted = true;
                failCloseDeferred =
                    generation.TryDeferFailCloseUntilPreparationDeadline(
                        request,
                        failClose);
            }

            if (failCloseImmediately || !failCloseDeferred)
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

        if (failCloseDeferralEligible && !failCloseDeferralAttempted)
        {
            failCloseDeferralAttempted = true;
            failCloseDeferred =
                generation.TryDeferFailCloseUntilPreparationDeadline(
                    request,
                    failClose);
        }

        if (failCloseImmediately || !failCloseDeferred)
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
            : generation.FailCloseAsync(failClose);

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
    private readonly TimeProvider timeProvider;
    private int activeLeases;
    private int activePeerConnections;
    private int activeResponderRoutes;
    private bool cancellationCompleted;
    private RemoteWindowPreparationRequest? deferredFailCloseRequest;
    private Func<ValueTask>? deferredFailCloseOperation;
    private ITimer? deferredFailCloseTimer;
    private Task? failCloseTask;
    private bool ownerReleased;
    private int registrationOperations;
    private bool failClosePending;
    private bool revoked;
    private bool revocationDisposed;
    private TaskCompletionSource peerConnectionsDrained =
        CreateCompletedSignal();
    private bool responderRouteClaimed;
    private TaskCompletionSource responderRoutesDrained =
        CreateCompletedSignal();

    internal RemoteWindowConnectionGeneration(
        long value,
        object? revocationCallbackOwner = null,
        VerifiedPeerConnectionCandidate? peerConnectionCandidate = null,
        IVerifiedPeerConnectionCandidateValidator? candidateValidator = null,
        TimeProvider? timeProvider = null)
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
        this.timeProvider = timeProvider ?? TimeProvider.System;
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

    internal bool TryBeginResponderRouteOperation(
        IRemoteWindowHostPreparationAdmission? admission,
        out RemoteWindowResponderRouteOperation? operation)
    {
        var candidate = new RemoteWindowResponderRouteOperation(this);
        var drain = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (revoked
                || failClosePending
                || ownerReleased
                || responderRouteClaimed
                || activeResponderRoutes != 0)
            {
                operation = null;
                return false;
            }

            DateTimeOffset admissionTime = timeProvider.GetUtcNow();
            if (admission is not null
                && !admission.TryAdmitRouteSelection(admissionTime))
            {
                operation = null;
                return false;
            }

            responderRouteClaimed = true;
            activeResponderRoutes = 1;
            responderRoutesDrained = drain;
            operation = candidate;
            return true;
        }
    }

    internal Task WaitForRemoteWindowOperationsAsync()
    {
        Task peerConnections;
        Task responderRoutes;
        lock (gate)
        {
            peerConnections = peerConnectionsDrained.Task;
            responderRoutes = responderRoutesDrained.Task;
        }

        return peerConnections.IsCompletedSuccessfully
            ? responderRoutes
            : responderRoutes.IsCompletedSuccessfully
                ? peerConnections
                : Task.WhenAll(peerConnections, responderRoutes);
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

    internal void CompleteResponderRoute()
    {
        TaskCompletionSource completion;
        bool dispose;
        lock (gate)
        {
            if (activeResponderRoutes != 1)
            {
                throw new InvalidOperationException(
                    "A Remote Window responder route operation completed more than once.");
            }

            activeResponderRoutes = 0;
            completion = responderRoutesDrained;
            dispose = ClaimRevocationDisposalIfReady();
        }

        completion.TrySetResult();
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
        ITimer? deferredTimer;
        lock (gate)
        {
            if (ownerReleased)
            {
                return null;
            }

            cancel = !revoked;
            revoked = true;
            ownerReleased = true;
            deferredTimer = deferredFailCloseTimer;
            deferredFailCloseTimer = null;
        }

        Exception? failure = deferredTimer is null
            ? null
            : CaptureFailure(deferredTimer.Dispose);
        if (cancel)
        {
            try
            {
                revocation.Cancel();
            }
            catch (Exception exception)
            {
                failure = CombineGenerationFailures(failure, exception);
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

    internal bool TryDeferFailCloseUntilPreparationDeadline(
        RemoteWindowPreparationRequest request,
        Func<ValueTask> failClose)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(failClose);
        lock (gate)
        {
            if (revoked || ownerReleased)
            {
                return false;
            }

            if (deferredFailCloseRequest is not null)
            {
                return deferredFailCloseRequest == request;
            }

            if (failClosePending || failCloseTask is not null)
            {
                return false;
            }

            try
            {
                DateTimeOffset now = timeProvider.GetUtcNow();
                TimeSpan remaining = request.Deadline - now;
                if (remaining <= TimeSpan.Zero
                    || remaining
                        > RemoteWindowControlMessageCodec.MaximumCommandTimeToLive)
                {
                    return false;
                }

                failClosePending = true;
                deferredFailCloseRequest = request;
                deferredFailCloseOperation = failClose;
                ITimer? timer = timeProvider.CreateTimer(
                    static state =>
                        ((RemoteWindowConnectionGeneration)state!)
                        .OnDeferredFailCloseDeadline(),
                    this,
                    remaining,
                    Timeout.InfiniteTimeSpan);
                if (timer is null)
                {
                    deferredFailCloseRequest = null;
                    deferredFailCloseOperation = null;
                    failClosePending = false;
                    return false;
                }

                deferredFailCloseTimer = timer;
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                deferredFailCloseRequest = null;
                deferredFailCloseOperation = null;
                failClosePending = false;
                return false;
            }
        }
    }

    internal ValueTask FailCloseAsync(Func<ValueTask> failClose)
    {
        ArgumentNullException.ThrowIfNull(failClose);
        Task task;
        TaskCompletionSource? completion = null;
        ITimer? deferredTimer = null;
        lock (gate)
        {
            if (failCloseTask is null)
            {
                failClosePending = true;
                completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                failCloseTask = completion.Task;
                deferredTimer = deferredFailCloseTimer;
                deferredFailCloseTimer = null;
            }

            task = failCloseTask;
        }

        Exception? timerFailure = deferredTimer is null
            ? null
            : CaptureFailure(deferredTimer.Dispose);
        if (completion is not null)
        {
            _ = CompleteFailCloseAsync(
                completion,
                failClose,
                timerFailure);
        }

        return new ValueTask(task);
    }

    private void OnDeferredFailCloseDeadline()
    {
        Func<ValueTask>? operation;
        lock (gate)
        {
            operation = revoked || ownerReleased
                ? null
                : deferredFailCloseOperation;
        }

        if (operation is not null)
        {
            Task failClosing = FailCloseAsync(operation).AsTask();
            _ = failClosing.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static async Task CompleteFailCloseAsync(
        TaskCompletionSource completion,
        Func<ValueTask> failClose,
        Exception? timerFailure)
    {
        Exception? failure = timerFailure;
        try
        {
            await failClose().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = CombineGenerationFailures(failure, exception);
        }

        if (failure is null)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(failure);
        }
    }

    private static Exception? CombineGenerationFailures(
        Exception? first,
        Exception? second) => (first, second) switch
        {
            (null, null) => null,
            (not null, null) => first,
            (null, not null) => second,
            _ => new AggregateException(
                "Remote Window generation cleanup failed.",
                first!,
                second!),
        };

    private static Exception? CaptureFailure(Action? action)
    {
        if (action is null)
        {
            return null;
        }

        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
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
            || activeResponderRoutes != 0
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

internal sealed class RemoteWindowResponderRouteOperation
{
    private RemoteWindowConnectionGeneration? generation;

    internal RemoteWindowResponderRouteOperation(
        RemoteWindowConnectionGeneration generation) =>
        this.generation = generation;

    internal void Complete()
    {
        RemoteWindowConnectionGeneration? owner = Interlocked.Exchange(
            ref generation,
            null);
        if (owner is null)
        {
            throw new InvalidOperationException(
                "A Remote Window responder route operation completed more than once.");
        }

        owner.CompleteResponderRoute();
    }
}
