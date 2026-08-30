using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Transport;

public sealed class AuthenticatedRemoteWindowMediaSessionDirectory :
    IRemoteWindowMediaAttachmentHandler,
    IAsyncDisposable
{
    private readonly TaskCompletionSource disposalCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock gate = new();
    private readonly bool ownsRoutes;
    private readonly RemoteWindowMediaRouteRegistry routes;
    private readonly Dictionary<DeviceId, AuthenticatedRemoteWindowMediaSession>
        sessions = [];
    private bool disposed;

    public AuthenticatedRemoteWindowMediaSessionDirectory(
        int maximumRoutes = RemoteWindowMediaRouteRegistry.DefaultMaximumRoutes,
        TimeProvider? timeProvider = null) : this(
            new RemoteWindowMediaRouteRegistry(maximumRoutes, timeProvider),
            ownsRoutes: true)
    {
    }

    internal AuthenticatedRemoteWindowMediaSessionDirectory(
        RemoteWindowMediaRouteRegistry routes) : this(routes, ownsRoutes: false)
    {
    }

    private AuthenticatedRemoteWindowMediaSessionDirectory(
        RemoteWindowMediaRouteRegistry routes,
        bool ownsRoutes)
    {
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        this.ownsRoutes = ownsRoutes;
    }

    public event Action? Changed;

    internal RemoteWindowMediaRouteRegistry Routes => routes;

    public bool TryGet(
        DeviceId peerDeviceId,
        out AuthenticatedRemoteWindowMediaSession? session)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        lock (gate)
        {
            if (!disposed
                && sessions.TryGetValue(peerDeviceId, out session)
                && session.IsCurrent)
            {
                return true;
            }

            session = null;
            return false;
        }
    }

    public async ValueTask HandleAsync(
        RemoteWindowMediaAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        AuthenticatedRemoteWindowMediaSession session;
        lock (gate)
        {
            if (disposed
                || !sessions.TryGetValue(
                    attachment.Binding.InitiatorDeviceId,
                    out session!)
                || !session.TryAcceptResponderAttachment(attachment))
            {
                throw new InvalidDataException(
                    "The Remote Window media attachment has no live owning control connection.");
            }
        }

        PublishChanged();
        try
        {
            await session.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            session.RequestControlStop();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        AuthenticatedRemoteWindowMediaSession[]? active = null;
        lock (gate)
        {
            if (!disposed)
            {
                disposed = true;
                active = sessions.Values.ToArray();
                sessions.Clear();
            }
        }

        if (active is not null)
        {
            Task[] cleanup = active
                .Select(static session => session.DisposeAsync().AsTask())
                .ToArray();
            _ = CompleteDisposalAsync(cleanup);
            PublishChanged();
        }

        return new ValueTask(disposalCompletion.Task);
    }

    internal async ValueTask<AuthenticatedRemoteWindowMediaSessionRegistration>
        RegisterAsync(
        AuthenticatedTcpControlConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!ProtocolFeatures.SupportsRemoteWindowMediaRoute(connection.ProtocolVersion))
        {
            throw new InvalidOperationException(
                $"A connection-owned media session requires protocol {ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion} or later.");
        }

        SecureFrameSession ownedMediaSession =
            connection.TakeRemoteWindowMediaFrames();
        AuthenticatedRemoteWindowMediaSession? session = null;
        try
        {
            session = new AuthenticatedRemoteWindowMediaSession(
                connection.LocalDeviceId,
                connection.PeerIdentity.DeviceId,
                connection.ProtocolVersion,
                routes,
                ownedMediaSession);
            ownedMediaSession = null!;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!sessions.TryAdd(session.PeerDeviceId, session))
                {
                    throw new InvalidDataException(
                        "A peer already has a live authenticated media control connection.");
                }
            }

            PublishChanged();
            return new AuthenticatedRemoteWindowMediaSessionRegistration(
                this,
                session);
        }
        catch (Exception registrationFailure)
        {
            Exception? cleanupFailure = null;
            if (session is not null)
            {
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    cleanupFailure = failure;
                }
            }
            else
            {
                try
                {
                    ownedMediaSession.Dispose();
                }
                catch (Exception failure)
                {
                    cleanupFailure = failure;
                }
            }

            if (cleanupFailure is not null)
            {
                throw new AggregateException(
                    "Authenticated Remote Window media registration and cleanup both failed.",
                    registrationFailure,
                    cleanupFailure);
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(registrationFailure)
                .Throw();
            throw;
        }
    }

    internal ValueTask UnregisterAsync(
        AuthenticatedRemoteWindowMediaSession session)
    {
        lock (gate)
        {
            if (sessions.TryGetValue(
                    session.PeerDeviceId,
                    out AuthenticatedRemoteWindowMediaSession? current)
                && ReferenceEquals(current, session))
            {
                sessions.Remove(session.PeerDeviceId);
            }
        }

        ValueTask disposal = session.DisposeAsync();
        PublishChanged();
        return disposal;
    }

    private async Task CompleteDisposalAsync(Task[] cleanup)
    {
        var failures = new List<Exception>();
        foreach (Task task in cleanup)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }

        if (ownsRoutes)
        {
            try
            {
                await routes.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }

        if (failures.Count == 0)
        {
            disposalCompletion.TrySetResult();
        }
        else
        {
            disposalCompletion.TrySetException(failures.Count == 1
                ? failures[0]
                : new AggregateException(
                    "Authenticated Remote Window media session cleanup failed.",
                    failures));
        }
    }

    private void PublishChanged()
    {
        foreach (Action subscriber in
                 Changed?.GetInvocationList().Cast<Action>() ?? [])
        {
            try
            {
                subscriber();
            }
            catch
            {
                // Observers cannot own an authenticated media session lifetime.
            }
        }
    }
}

public sealed class AuthenticatedRemoteWindowMediaSession :
    IRemoteWindowMediaSink,
    IAsyncDisposable
{
    private readonly TaskCompletionSource attachmentReady = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource controlStopCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource disposalCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock gate = new();
    private readonly CancellationTokenSource requestControlStop = new();
    private readonly RemoteWindowMediaRouteRegistry routes;
    private readonly CancellationToken controlStopToken;
    private RemoteWindowMediaAttachment? attachment;
    private RemoteWindowMediaRouteBinding? binding;
    private bool borrowedResponderAttachment;
    private int controlStopRequested;
    private int disposeStarted;
    private InitiatorConnectOperation? initiatorConnect;
    private SecureFrameSession? mediaSession;
    private int responderRouteInvalidated;
    private Task? responderRouteObservation;
    private RemoteWindowMediaRouteRegistration? routeRegistration;
    private AuthenticatedRemoteWindowConnectionPreparationRegistration?
        preparationRegistration;
    private Exception? preparationInvalidationFailure;

    internal AuthenticatedRemoteWindowMediaSession(
        DeviceId localDeviceId,
        DeviceId peerDeviceId,
        ProtocolVersion protocolVersion,
        RemoteWindowMediaRouteRegistry routes,
        SecureFrameSession ownedMediaSession)
    {
        LocalDeviceId = localDeviceId
            ?? throw new ArgumentNullException(nameof(localDeviceId));
        PeerDeviceId = peerDeviceId
            ?? throw new ArgumentNullException(nameof(peerDeviceId));
        if (localDeviceId == peerDeviceId)
        {
            throw new ArgumentException(
                "An authenticated media session requires two distinct devices.",
                nameof(peerDeviceId));
        }

        if (!ProtocolFeatures.SupportsRemoteWindowMediaRoute(protocolVersion))
        {
            throw new ArgumentOutOfRangeException(nameof(protocolVersion));
        }

        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        mediaSession = ownedMediaSession
            ?? throw new ArgumentNullException(nameof(ownedMediaSession));
        controlStopToken = requestControlStop.Token;
        ProtocolVersion = protocolVersion;
    }

    public RemoteWindowMediaRouteBinding? Binding
    {
        get
        {
            lock (gate)
            {
                return binding;
            }
        }
    }

    public bool IsAttached
    {
        get
        {
            lock (gate)
            {
                return Volatile.Read(ref disposeStarted) == 0
                    && Volatile.Read(ref responderRouteInvalidated) == 0
                    && Volatile.Read(ref controlStopRequested) == 0
                    && attachment is not null;
            }
        }
    }

    public bool IsCurrent => Volatile.Read(ref disposeStarted) == 0
        && Volatile.Read(ref responderRouteInvalidated) == 0
        && Volatile.Read(ref controlStopRequested) == 0;

    public DeviceId LocalDeviceId { get; }

    public DeviceId PeerDeviceId { get; }

    public ProtocolVersion ProtocolVersion { get; }

    internal Task Completion => disposalCompletion.Task;

    internal CancellationToken ControlStopToken => controlStopToken;

    internal AuthenticatedRemoteWindowConnectionPreparationReservationStatus
        TryCommitPreparationRegistration(
            AuthenticatedRemoteWindowConnectionPreparationRegistration registration,
            Action commitGenerationSlot,
            Action rollBackGenerationSlot)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(commitGenerationSlot);
        ArgumentNullException.ThrowIfNull(rollBackGenerationSlot);
        lock (gate)
        {
            if (Volatile.Read(ref disposeStarted) != 0
                || Volatile.Read(ref responderRouteInvalidated) != 0
                || Volatile.Read(ref controlStopRequested) != 0)
            {
                _ = registration.Deactivate();
                return AuthenticatedRemoteWindowConnectionPreparationReservationStatus
                    .ConnectionStale;
            }

            if (preparationRegistration?.IsActive == true)
            {
                _ = registration.Deactivate();
                return AuthenticatedRemoteWindowConnectionPreparationReservationStatus
                    .ReservationConflict;
            }

            preparationRegistration = registration;
            commitGenerationSlot();
            try
            {
                registration.TransferOwnership();
            }
            catch
            {
                preparationRegistration = null;
                rollBackGenerationSlot();
                _ = registration.Deactivate();
                throw;
            }

            return AuthenticatedRemoteWindowConnectionPreparationReservationStatus
                .Reserved;
        }
    }

    internal bool IsPreparationRegistrationCurrent(
        AuthenticatedRemoteWindowConnectionPreparationRegistration registration)
    {
        lock (gate)
        {
            return Volatile.Read(ref disposeStarted) == 0
                && Volatile.Read(ref responderRouteInvalidated) == 0
                && Volatile.Read(ref controlStopRequested) == 0
                && registration.IsActive
                && ReferenceEquals(preparationRegistration, registration);
        }
    }

    internal void UnregisterPreparationRegistration(
        AuthenticatedRemoteWindowConnectionPreparationRegistration registration)
    {
        lock (gate)
        {
            if (ReferenceEquals(preparationRegistration, registration))
            {
                preparationRegistration = null;
            }
        }
    }

    internal bool TryAdmitPreparationOperation(
        AuthenticatedRemoteWindowConnectionPreparationRegistration? registration,
        Func<bool> admit)
    {
        ArgumentNullException.ThrowIfNull(admit);
        lock (gate)
        {
            if (Volatile.Read(ref disposeStarted) != 0
                || Volatile.Read(ref responderRouteInvalidated) != 0
                || Volatile.Read(ref controlStopRequested) != 0)
            {
                return false;
            }

            bool hasActivePreparation = preparationRegistration?.IsActive == true;
            if (hasActivePreparation
                    && !ReferenceEquals(preparationRegistration, registration)
                || !hasActivePreparation && registration is not null)
            {
                return false;
            }

            return admit();
        }
    }

    public RemoteWindowMediaRouteBinding PrepareResponderRoute(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(activityId);
        try
        {
            lock (gate)
            {
                ThrowIfDisposed();
                ThrowIfRouteSelected();
                SecureFrameSession owned = mediaSession
                    ?? throw new InvalidOperationException(
                        "The connection-owned media session has already selected a route role.");
                RemoteWindowMediaRouteBinding prepared = CreateBinding(
                    PeerDeviceId,
                    LocalDeviceId,
                    sessionId,
                    activityId,
                    owned);
                mediaSession = null;
                RemoteWindowMediaRouteRegistration registration =
                    routes.RegisterOwnedRoute(
                    prepared,
                    owned,
                    lifetime);
                routeRegistration = registration;
                binding = prepared;
                responderRouteObservation =
                    ObserveResponderRouteCleanupAsync(registration);
                return prepared;
            }
        }
        catch
        {
            RequestControlStop();
            throw;
        }
    }

    public ValueTask ConnectInitiatorAsync(
        Stream stream,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        CancellationToken cancellationToken = default) =>
        ConnectInitiatorCoreAsync(
            stream,
            sessionId,
            activityId,
            requestControlStopOnFailure: true,
            cancellationToken);

    internal ValueTask ConnectInitiatorForPreparationAsync(
        Stream stream,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        CancellationToken cancellationToken = default) =>
        ConnectInitiatorCoreAsync(
            stream,
            sessionId,
            activityId,
            requestControlStopOnFailure: false,
            cancellationToken);

    private ValueTask ConnectInitiatorCoreAsync(
        Stream stream,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        bool requestControlStopOnFailure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(activityId);
        SecureFrameSession owned;
        RemoteWindowMediaRouteBinding prepared;
        InitiatorConnectOperation operation;
        lock (gate)
        {
            ThrowIfDisposed();
            ThrowIfRouteSelected();
            owned = mediaSession
                ?? throw new InvalidOperationException(
                    "The connection-owned media session has already selected a route role.");
            prepared = CreateBinding(
                LocalDeviceId,
                PeerDeviceId,
                sessionId,
                activityId,
                owned);
            operation = new InitiatorConnectOperation(stream);
            mediaSession = null;
            binding = prepared;
            initiatorConnect = operation;
        }

        return new ValueTask(CompleteInitiatorConnectAsync(
            operation,
            owned,
            prepared,
            requestControlStopOnFailure,
            cancellationToken));
    }

    private async Task CompleteInitiatorConnectAsync(
        InitiatorConnectOperation operation,
        SecureFrameSession owned,
        RemoteWindowMediaRouteBinding prepared,
        bool requestControlStopOnFailure,
        CancellationToken cancellationToken)
    {
        RemoteWindowMediaAttachment? connected = null;
        CancellationTokenSource? linkedCancellation = null;
        Exception? failure = null;
        Exception? cleanupFailure = null;
        try
        {
            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                operation.StopToken);
            connected = await RemoteWindowMediaAttachment.ConnectAsync(
                operation.Stream,
                prepared,
                owned,
                linkedCancellation.Token).ConfigureAwait(false);
            lock (gate)
            {
                ThrowIfDisposed();

                attachment = connected;
                connected = null;
                attachmentReady.TrySetResult();
            }
        }
        catch (Exception connectFailure)
        {
            failure = connectFailure;
            if (requestControlStopOnFailure)
            {
                RequestControlStop();
            }
        }
        finally
        {
            if (connected is not null)
            {
                try
                {
                    await connected.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception attachmentCleanupFailure)
                {
                    cleanupFailure = CombineFailures(
                        cleanupFailure,
                        attachmentCleanupFailure);
                }
            }

            if (linkedCancellation is not null)
            {
                try
                {
                    linkedCancellation.Dispose();
                }
                catch (Exception cancellationCleanupFailure)
                {
                    cleanupFailure = CombineFailures(
                        cleanupFailure,
                        cancellationCleanupFailure);
                }
            }

            cleanupFailure = await operation
                .BeginSettlementAsync(cleanupFailure)
                .ConfigureAwait(false);
            lock (gate)
            {
                if (ReferenceEquals(initiatorConnect, operation))
                {
                    initiatorConnect = null;
                }
            }

            if (cleanupFailure is not null)
            {
                failure = CombineFailures(failure, cleanupFailure);
            }

            if (failure is not null)
            {
                attachmentReady.TrySetException(failure);
            }

            operation.CompleteSettlement();
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(failure)
                .Throw();
        }
    }

    public async ValueTask WaitForAttachmentAsync(
        CancellationToken cancellationToken = default) =>
        await attachmentReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask<RemoteWindowMediaFrame> ReceiveAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            RemoteWindowMediaAttachment current = RequireAttachment();
            return await current.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            RequestControlStop();
            throw;
        }
    }

    public async ValueTask SendAsync(
        RemoteWindowMediaFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        try
        {
            RemoteWindowMediaAttachment current = RequireAttachment();
            await current.SendAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            RequestControlStop();
            throw;
        }
    }

    internal bool TryAcceptResponderAttachment(
        RemoteWindowMediaAttachment candidate)
    {
        lock (gate)
        {
            if (Volatile.Read(ref disposeStarted) != 0
                || Volatile.Read(ref responderRouteInvalidated) != 0
                || Volatile.Read(ref controlStopRequested) != 0
                || attachment is not null
                || routeRegistration is null
                || !routeRegistration.IsAttached
                || binding != candidate.Binding)
            {
                return false;
            }

            attachment = candidate;
            borrowedResponderAttachment = true;
            attachmentReady.TrySetResult();
            return true;
        }
    }

    internal void RequestControlStop()
    {
        bool signalControlStop;
        lock (gate)
        {
            signalControlStop = TryCommitControlStopUnderGate();
        }

        SignalCommittedControlStop(signalControlStop);
    }

    public ValueTask DisposeAsync()
    {
        bool signalControlStop = false;
        bool startDisposal = false;
        lock (gate)
        {
            if (Volatile.Read(ref disposeStarted) == 0)
            {
                Volatile.Write(ref disposeStarted, 1);
                startDisposal = true;
                signalControlStop = TryCommitControlStopUnderGate();
            }
        }

        if (startDisposal)
        {
            SignalCommittedControlStop(signalControlStop);
            _ = CompleteDisposalAsync();
        }

        return new ValueTask(disposalCompletion.Task);
    }

    private bool TryCommitControlStopUnderGate()
    {
        if (Volatile.Read(ref controlStopRequested) != 0)
        {
            return false;
        }

        Volatile.Write(ref controlStopRequested, 1);
        Exception? invalidationFailure = InvalidatePreparationUnderGate();
        if (invalidationFailure is not null)
        {
            preparationInvalidationFailure = CombineFailures(
                preparationInvalidationFailure,
                invalidationFailure);
        }

        return true;
    }

    private void SignalCommittedControlStop(bool signalControlStop)
    {
        if (!signalControlStop)
        {
            return;
        }

        try
        {
            requestControlStop.Cancel();
        }
        catch (AggregateException cancellationFailure)
        {
            OutOfMemoryException? fatal = cancellationFailure
                .Flatten()
                .InnerExceptions
                .OfType<OutOfMemoryException>()
                .FirstOrDefault();
            if (fatal is not null)
            {
                lock (gate)
                {
                    preparationInvalidationFailure = CombineFailures(
                        preparationInvalidationFailure,
                        fatal);
                }
            }

            // Every control owner was still invoked and owns its cleanup.
        }
        finally
        {
            controlStopCompletion.TrySetResult();
        }
    }

    private RemoteWindowMediaRouteBinding CreateBinding(
        DeviceId initiatorDeviceId,
        DeviceId responderDeviceId,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        SecureFrameSession ownedMediaSession) =>
        RemoteWindowMediaRouteBinding.Create(
            ProtocolVersion,
            initiatorDeviceId,
            responderDeviceId,
            RemoteWindowMediaRouteId.FromSession(ownedMediaSession),
            sessionId,
            activityId);

    private async Task CompleteDisposalAsync()
    {
        Exception? failure = null;
        OutOfMemoryException? fatal = null;
        await controlStopCompletion.Task.ConfigureAwait(false);
        Exception? controlStopFailure;
        lock (gate)
        {
            controlStopFailure = preparationInvalidationFailure;
            preparationInvalidationFailure = null;
        }

        AccumulateCleanupFailure(ref failure, ref fatal, controlStopFailure);
        RemoteWindowMediaRouteRegistration? registration;
        RemoteWindowMediaAttachment? ownedAttachment;
        InitiatorConnectOperation? pendingConnect;
        Task? routeObservation;
        SecureFrameSession? unusedSession;
        lock (gate)
        {
            registration = routeRegistration;
            routeRegistration = null;
            ownedAttachment = borrowedResponderAttachment ? null : attachment;
            attachment = null;
            pendingConnect = initiatorConnect;
            routeObservation = responderRouteObservation;
            unusedSession = mediaSession;
            mediaSession = null;
        }

        attachmentReady.TrySetException(new ObjectDisposedException(
            nameof(AuthenticatedRemoteWindowMediaSession)));
        pendingConnect?.RequestStop();

        if (pendingConnect is not null)
        {
            await pendingConnect.Settled.ConfigureAwait(false);
            if (pendingConnect.CleanupFailure is not null)
            {
                AccumulateCleanupFailure(
                    ref failure,
                    ref fatal,
                    pendingConnect.CleanupFailure);
            }
        }

        try
        {
            if (registration is not null)
            {
                await registration.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception cleanupFailure)
        {
            AccumulateCleanupFailure(ref failure, ref fatal, cleanupFailure);
        }

        if (routeObservation is not null)
        {
            await routeObservation.ConfigureAwait(false);
        }

        if (ownedAttachment is not null)
        {
            try
            {
                await ownedAttachment.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                AccumulateCleanupFailure(ref failure, ref fatal, cleanupFailure);
            }
        }

        if (unusedSession is not null)
        {
            try
            {
                unusedSession.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                AccumulateCleanupFailure(ref failure, ref fatal, cleanupFailure);
            }
        }

        try
        {
            requestControlStop.Dispose();
        }
        catch (Exception cleanupFailure)
        {
            AccumulateCleanupFailure(ref failure, ref fatal, cleanupFailure);
        }

        if (fatal is not null)
        {
            disposalCompletion.TrySetException(fatal);
        }
        else if (failure is null)
        {
            disposalCompletion.TrySetResult();
        }
        else
        {
            disposalCompletion.TrySetException(failure);
        }
    }

    private static void AccumulateCleanupFailure(
        ref Exception? failure,
        ref OutOfMemoryException? fatal,
        Exception? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        fatal ??= candidate switch
        {
            OutOfMemoryException outOfMemory => outOfMemory,
            AggregateException aggregate => aggregate
                .Flatten()
                .InnerExceptions
                .OfType<OutOfMemoryException>()
                .FirstOrDefault(),
            _ => null,
        };
        if (candidate is not OutOfMemoryException)
        {
            failure = CombineFailures(failure, candidate);
        }
    }

    private static Exception CombineFailures(
        Exception? first,
        Exception second) => first is null
        ? second
        : new AggregateException(
            "Authenticated Remote Window media cleanup failed.",
            first,
            second);

    private async Task ObserveResponderRouteCleanupAsync(
        RemoteWindowMediaRouteRegistration registration)
    {
        await Task.Yield();
        Exception? cleanupFailure = null;
        try
        {
            await registration.CleanupCompletion.ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            cleanupFailure = failure;
        }

        bool invalidated;
        bool signalControlStop = false;
        lock (gate)
        {
            invalidated = ReferenceEquals(routeRegistration, registration)
                && Volatile.Read(ref disposeStarted) == 0;
            if (invalidated)
            {
                Volatile.Write(ref responderRouteInvalidated, 1);
                signalControlStop = TryCommitControlStopUnderGate();
            }
        }

        if (!invalidated)
        {
            return;
        }

        SignalCommittedControlStop(signalControlStop);
        attachmentReady.TrySetException(new IOException(
            "The authenticated Remote Window media route ended before attachment.",
            cleanupFailure));
    }

    private Exception? InvalidatePreparationUnderGate()
    {
        AuthenticatedRemoteWindowConnectionPreparationRegistration? registration =
            preparationRegistration;
        preparationRegistration = null;
        IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink? sink =
            registration?.Deactivate();
        if (sink is null)
        {
            return null;
        }

        try
        {
            sink.InvalidateAuthenticatedRemoteWindowConnectionPreparationNow();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private RemoteWindowMediaAttachment RequireAttachment()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref responderRouteInvalidated) != 0
                || Volatile.Read(ref controlStopRequested) != 0)
            {
                throw new InvalidOperationException(
                    "The authenticated Remote Window media session is no longer current.");
            }

            return attachment
                ?? throw new InvalidOperationException(
                    "The authenticated Remote Window media attachment is not ready.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposeStarted) != 0,
            this);

    private void ThrowIfRouteSelected()
    {
        if (binding is not null)
        {
            throw new InvalidOperationException(
                "The connection-owned media session already has a route binding.");
        }
    }

    private sealed class InitiatorConnectOperation : IDisposable
    {
        private readonly object gate = new();
        private readonly TaskCompletionSource settled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource stop = new();
        private Exception? cleanupFailure;
        private bool settlementStarted;
        private Task? stopping;

        public InitiatorConnectOperation(Stream stream) =>
            Stream = stream ?? throw new ArgumentNullException(nameof(stream));

        public Exception? CleanupFailure
        {
            get
            {
                lock (gate)
                {
                    return cleanupFailure;
                }
            }
        }

        public Task Settled => settled.Task;

        public Stream Stream { get; }

        public CancellationToken StopToken => stop.Token;

        public async Task<Exception?> BeginSettlementAsync(
            Exception? additionalCleanupFailure)
        {
            Task? stopCompletion;
            lock (gate)
            {
                settlementStarted = true;
                stopCompletion = stopping;
            }

            if (stopCompletion is not null)
            {
                await stopCompletion.ConfigureAwait(false);
            }

            lock (gate)
            {
                cleanupFailure = CombineFailures(
                    cleanupFailure,
                    additionalCleanupFailure);
                try
                {
                    Dispose();
                }
                catch (Exception cancellationCleanupFailure)
                {
                    cleanupFailure = CombineFailures(
                        cleanupFailure,
                        cancellationCleanupFailure);
                }

                return cleanupFailure;
            }
        }

        public void CompleteSettlement() => settled.TrySetResult();

        public void Dispose() => stop.Dispose();

        public void RequestStop()
        {
            lock (gate)
            {
                if (settlementStarted || stopping is not null)
                {
                    return;
                }

                stopping = StopAsync();
            }
        }

        private async Task StopAsync()
        {
            await Task.Yield();
            Exception? failure = null;
            Task cancellation;
            try
            {
                cancellation = stop.CancelAsync();
            }
            catch (Exception cancellationFailure)
            {
                failure = cancellationFailure;
                cancellation = Task.CompletedTask;
            }

            try
            {
                Stream.Dispose();
            }
            catch (Exception streamCleanupFailure)
            {
                failure = CombineFailures(failure, streamCleanupFailure);
            }

            try
            {
                await cancellation.ConfigureAwait(false);
            }
            catch (Exception cancellationFailure)
            {
                failure = CombineFailures(failure, cancellationFailure);
            }

            lock (gate)
            {
                cleanupFailure = CombineFailures(cleanupFailure, failure);
            }
        }

        private static Exception? CombineFailures(
            Exception? first,
            Exception? second) => second is null
                ? first
                : AuthenticatedRemoteWindowMediaSession.CombineFailures(
                    first,
                    second);
    }
}

internal sealed class AuthenticatedRemoteWindowMediaSessionRegistration :
    IAsyncDisposable
{
    private readonly AuthenticatedRemoteWindowMediaSessionDirectory directory;
    private readonly AuthenticatedRemoteWindowMediaSession session;
    private readonly CancellationToken controlStopToken;
    private int disposeStarted;

    internal AuthenticatedRemoteWindowMediaSessionRegistration(
        AuthenticatedRemoteWindowMediaSessionDirectory directory,
        AuthenticatedRemoteWindowMediaSession session)
    {
        this.directory = directory;
        this.session = session;
        controlStopToken = session.ControlStopToken;
    }

    internal CancellationToken ControlStopToken => controlStopToken;

    internal AuthenticatedRemoteWindowMediaSession Session => session;

    public ValueTask DisposeAsync() =>
        Interlocked.Exchange(ref disposeStarted, 1) == 0
            ? directory.UnregisterAsync(session)
            : session.DisposeAsync();
}
