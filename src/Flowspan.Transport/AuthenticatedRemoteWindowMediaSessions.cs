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
                    && attachment is not null;
            }
        }
    }

    public bool IsCurrent => Volatile.Read(ref disposeStarted) == 0
        && Volatile.Read(ref responderRouteInvalidated) == 0;

    public DeviceId LocalDeviceId { get; }

    public DeviceId PeerDeviceId { get; }

    public ProtocolVersion ProtocolVersion { get; }

    internal Task Completion => disposalCompletion.Task;

    internal CancellationToken ControlStopToken => controlStopToken;

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
        RemoteWindowMediaAttachment current = RequireAttachment();
        try
        {
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
        RemoteWindowMediaAttachment current = RequireAttachment();
        try
        {
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
        if (Interlocked.CompareExchange(ref controlStopRequested, 1, 0) != 0)
        {
            return;
        }

        try
        {
            requestControlStop.Cancel();
        }
        catch (AggregateException)
        {
            // The control owner still observes cancellation and owns cleanup.
        }
        finally
        {
            controlStopCompletion.TrySetResult();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) == 0)
        {
            _ = CompleteDisposalAsync();
        }

        return new ValueTask(disposalCompletion.Task);
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
                failure = CombineFailures(
                    failure,
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
            failure = cleanupFailure;
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
                failure = CombineFailures(failure, cleanupFailure);
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
                failure = CombineFailures(failure, cleanupFailure);
            }
        }

        RequestControlStop();
        await controlStopCompletion.Task.ConfigureAwait(false);
        try
        {
            requestControlStop.Dispose();
        }
        catch (Exception cleanupFailure)
        {
            failure = CombineFailures(failure, cleanupFailure);
        }

        if (failure is null)
        {
            disposalCompletion.TrySetResult();
        }
        else
        {
            disposalCompletion.TrySetException(failure);
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
        lock (gate)
        {
            invalidated = ReferenceEquals(routeRegistration, registration)
                && Volatile.Read(ref disposeStarted) == 0;
            if (invalidated)
            {
                Volatile.Write(ref responderRouteInvalidated, 1);
            }
        }

        if (!invalidated)
        {
            return;
        }

        attachmentReady.TrySetException(new IOException(
            "The authenticated Remote Window media route ended before attachment.",
            cleanupFailure));
        RequestControlStop();
    }

    private RemoteWindowMediaAttachment RequireAttachment()
    {
        lock (gate)
        {
            ThrowIfDisposed();
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
