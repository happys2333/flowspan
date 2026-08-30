using System.Runtime.ExceptionServices;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Protocol;
using Flowspan.Transport;

namespace Flowspan.Desktop;

internal interface IDesktopRemoteWindowHostConnection :
    IRemoteWindowMediaSink,
    IAsyncDisposable
{
    public DeviceId LocalDeviceId { get; }

    public DeviceId PeerDeviceId { get; }

    public ProtocolVersion ProtocolVersion { get; }

    public bool IsCurrent { get; }

    public string AuthenticatedPeerFingerprint { get; }

    public IDisposable RegisterRevocationCallback(Action callback);

    public void PrepareResponderRoute(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        IRemoteWindowHostPreparationAdmission admission,
        TimeSpan lifetime);

    public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
        RemoteWindowPreparationRequest request,
        IRemoteWindowHostPreparationAdmission admission,
        CancellationToken cancellationToken);

    public ValueTask WaitForMediaAttachmentAsync(
        CancellationToken cancellationToken);

    public ValueTask PublishAdmissionStateAsync(
        RemoteWindowParticipantState state,
        CancellationToken cancellationToken);

    public ValueTask FailCloseAsync();
}

internal sealed class AuthenticatedDesktopRemoteWindowHostConnection(
    AuthenticatedRemoteWindowConnectionLease lease) :
    IDesktopRemoteWindowHostConnection
{
    private readonly AuthenticatedRemoteWindowConnectionLease lease = lease
        ?? throw new ArgumentNullException(nameof(lease));

    public DeviceId LocalDeviceId => lease.LocalDeviceId;

    public DeviceId PeerDeviceId => lease.PeerDeviceId;

    public ProtocolVersion ProtocolVersion => lease.ProtocolVersion;

    public bool IsCurrent => lease.IsCurrent;

    public string AuthenticatedPeerFingerprint =>
        lease.AuthenticatedPeerFingerprint
        ?? throw new InvalidOperationException(
            "The authenticated Remote Window connection has no peer fingerprint.");

    public IDisposable RegisterRevocationCallback(Action callback) =>
        lease.RegisterRevocationCallback(callback);

    public void PrepareResponderRoute(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        IRemoteWindowHostPreparationAdmission admission,
        TimeSpan lifetime) =>
        _ = lease.PrepareResponderRoute(
            sessionId,
            activityId,
            admission,
            lifetime);

    public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
        RemoteWindowPreparationRequest request,
        IRemoteWindowHostPreparationAdmission admission,
        CancellationToken cancellationToken) =>
        lease.PrepareReservedAsync(request, admission, cancellationToken);

    public ValueTask WaitForMediaAttachmentAsync(
        CancellationToken cancellationToken) =>
        lease.WaitForMediaAttachmentAsync(cancellationToken);

    public ValueTask PublishAdmissionStateAsync(
        RemoteWindowParticipantState state,
        CancellationToken cancellationToken) =>
        lease.PublishAdmissionStateAsync(state, cancellationToken);

    public ValueTask SendAsync(
        RemoteWindowMediaFrame frame,
        CancellationToken cancellationToken = default) =>
        lease.SendMediaAsync(frame, cancellationToken);

    public ValueTask FailCloseAsync() => lease.FailCloseAsync();

    public ValueTask DisposeAsync() => lease.DisposeAsync();
}

internal sealed class BorrowedDesktopRemoteWindowMediaSink(
    IDesktopRemoteWindowHostConnection connection) : IRemoteWindowMediaSink
{
    private readonly IDesktopRemoteWindowHostConnection connection = connection
        ?? throw new ArgumentNullException(nameof(connection));

    public ValueTask SendAsync(
        RemoteWindowMediaFrame frame,
        CancellationToken cancellationToken = default) =>
        connection.SendAsync(frame, cancellationToken);
}

internal sealed record DesktopRemoteWindowHostStartRequest
{
    public DesktopRemoteWindowHostStartRequest(
        NativeRemoteWindowSourceLease sourceLease,
        long ownerGeneration,
        IDesktopRemoteWindowHostConnection connection,
        INativeProtectionSource protection,
        MirrorParticipantRole role)
    {
        SourceLease = sourceLease
            ?? throw new ArgumentNullException(nameof(sourceLease));
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerGeneration, 1);
        OwnerGeneration = ownerGeneration;
        Connection = connection
            ?? throw new ArgumentNullException(nameof(connection));
        Protection = protection
            ?? throw new ArgumentNullException(nameof(protection));
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        Role = role;
        if (!SourceLease.TryGetCurrentSnapshot(
                out NativeRemoteWindowSourceSnapshot? snapshot)
            || snapshot is null)
        {
            throw new ArgumentException(
                "A Remote Window host start requires a current source lease.",
                nameof(sourceLease));
        }

        Source = snapshot;
    }

    public IDesktopRemoteWindowHostConnection Connection { get; }

    public long OwnerGeneration { get; }

    public INativeProtectionSource Protection { get; }

    public MirrorParticipantRole Role { get; }

    public NativeRemoteWindowSourceSnapshot Source { get; }

    public NativeRemoteWindowSourceLease SourceLease { get; }
}

internal sealed class DesktopRemoteWindowFrameAdmissionSink :
    INativeRemoteWindowFrameSink,
    IDisposable
{
    private const int Pending = 0;
    private const int Open = 1;
    private const int Closed = 2;

    private readonly INativeRemoteWindowFrameSink destination;
    private readonly object gate = new();
    private int state = Pending;

    public DesktopRemoteWindowFrameAdmissionSink(
        INativeRemoteWindowFrameSink destination) =>
        this.destination = destination
            ?? throw new ArgumentNullException(nameof(destination));

    public bool IsOpen
    {
        get
        {
            lock (gate)
            {
                return state == Open;
            }
        }
    }

    public bool TryOpen()
    {
        lock (gate)
        {
            if (state == Closed)
            {
                return false;
            }

            state = Open;
            return true;
        }
    }

    public void CloseNow()
    {
        lock (gate)
        {
            state = Closed;
        }
    }

    public void TakeOwnership(
        NativeRemoteWindowSourceUse sourceUse,
        NativeRemoteWindowFrame frame)
    {
        ArgumentNullException.ThrowIfNull(sourceUse);
        ArgumentNullException.ThrowIfNull(frame);
        bool admitted;
        lock (gate)
        {
            admitted = state == Open;
        }

        if (!admitted)
        {
            frame.Dispose();
            return;
        }

        destination.TakeOwnership(sourceUse, frame);
    }

    public void Dispose() => CloseNow();
}

internal sealed class DesktopRemoteWindowHostCoordinator : IAsyncDisposable
{
    private const long FirstSessionGeneration = 1;

    private readonly IDesktopRemoteWindowHostAuthorizationSource authorization;
    private readonly INativeRemoteWindowCaptureBoundary capture;
    private readonly IClock clock;
    private readonly object callbackOwner = new();
    private readonly DesktopRemoteWindowHostControlPeer controlPeer;
    private readonly TaskCompletionSource disposalCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ILocalEmergencyStopRegistrar emergencyStops;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly INativeRemoteInputBoundary input;
    private readonly TimeSpan ownerLeaseDuration;
    private readonly INativeRemoteWindowPermissionBoundary permissions;
    private readonly TimeSpan preparationLifetime;
    private readonly ILocalSharingSessionBoundary sessions;
    private readonly object terminalFailureGate = new();
    private RuntimeGeneration? active;
    private int disposed;
    private long nextControlGeneration;
    private long nextPreparationGeneration;
    private Exception? terminalFailure;

    public DesktopRemoteWindowHostCoordinator(
        IClock clock,
        INativeRemoteWindowPermissionBoundary permissions,
        IDesktopRemoteWindowHostAuthorizationSource authorization,
        INativeRemoteWindowCaptureBoundary capture,
        INativeRemoteInputBoundary input,
        ILocalSharingSessionBoundary sessions,
        ILocalEmergencyStopRegistrar emergencyStops,
        DesktopRemoteWindowHostControlPeer controlPeer,
        TimeSpan ownerLeaseDuration,
        TimeSpan preparationLifetime)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.permissions = permissions
            ?? throw new ArgumentNullException(nameof(permissions));
        this.authorization = authorization
            ?? throw new ArgumentNullException(nameof(authorization));
        this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
        this.input = input ?? throw new ArgumentNullException(nameof(input));
        this.sessions = sessions
            ?? throw new ArgumentNullException(nameof(sessions));
        this.emergencyStops = emergencyStops
            ?? throw new ArgumentNullException(nameof(emergencyStops));
        this.controlPeer = controlPeer
            ?? throw new ArgumentNullException(nameof(controlPeer));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            ownerLeaseDuration,
            TimeSpan.Zero);

        if (preparationLifetime <= TimeSpan.Zero
            || preparationLifetime
                > RemoteWindowControlMessageCodec.MaximumCommandTimeToLive)
        {
            throw new ArgumentOutOfRangeException(nameof(preparationLifetime));
        }

        this.ownerLeaseDuration = ownerLeaseDuration;
        this.preparationLifetime = preparationLifetime;
    }

    public RemoteWindowSharingSnapshot? Snapshot => Volatile.Read(ref active)?
        .Controller.Snapshot;

    internal RemoteWindowMediaSessionBudget? ActiveMediaBudget =>
        Volatile.Read(ref active)?.MediaBudget;

    internal Exception? TerminalFailure
    {
        get
        {
            lock (terminalFailureGate)
            {
                return terminalFailure;
            }
        }
    }

    public async ValueTask<RemoteWindowCommandResult> StartAsync(
        DesktopRemoteWindowHostStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        RuntimeGeneration? generation = null;
        DesktopRemoteWindowFrameAdmissionSink? pendingAdmission = null;
        DesktopRemoteWindowLogicalVideoFrameSink? pendingMedia = null;
        RemoteWindowSessionController? pendingController = null;
        try
        {
            ThrowIfDisposed();
            if (TerminalFailure is not null)
            {
                throw StartFailure("host_cleanup_unconfirmed");
            }

            if (active is not null)
            {
                throw StartFailure("host_session_busy");
            }

            NativeRemoteWindowSourceSnapshot source = ValidateCurrentHostFacts(
                request,
                generation: null,
                out NativeRemoteWindowPermissionSnapshot initialPermission);
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = CanonicalUtc(clock.UtcNow);
            RemoteWindowSessionId sessionId = RemoteWindowSessionId.From(
                Guid.NewGuid());
            CorrelationId correlationId = CorrelationId.From(Guid.NewGuid());
            DateTimeOffset deadline = now.Add(preparationLifetime);
            RemoteWindowPreparationRequest preparation =
                RemoteWindowPreparationRequest.Create(
                    correlationId,
                    sessionId,
                    source.Source.ActivityId,
                    source.Source.HostDeviceId,
                    request.Connection.PeerDeviceId,
                    request.Role,
                    deadline);
            RuntimeGeneration? mediaFaultGeneration = null;
            var mediaBudget = new RemoteWindowMediaSessionBudget();
            var mediaSender = new RemoteWindowLogicalVideoFrameSender(
                mediaBudget,
                request.Connection.PeerDeviceId,
                new BorrowedDesktopRemoteWindowMediaSink(request.Connection));
            try
            {
                pendingMedia = new DesktopRemoteWindowLogicalVideoFrameSink(
                    source,
                    request.OwnerGeneration,
                    FirstSessionGeneration,
                    sessionId,
                    mediaSender,
                    faulted: fault => OnMediaFault(
                        mediaFaultGeneration,
                        fault));
                mediaSender = null!;
            }
            finally
            {
                if (mediaSender is not null)
                {
                    await mediaSender.DisposeAsync().ConfigureAwait(false);
                }
            }

            pendingAdmission = new DesktopRemoteWindowFrameAdmissionSink(
                pendingMedia);
            pendingController = new RemoteWindowSessionController(
                request.SourceLease,
                request.OwnerGeneration,
                clock,
                authorization,
                capture,
                input,
                pendingAdmission,
                sessions,
                ownerLeaseDuration);
            generation = new RuntimeGeneration(
                request,
                pendingController,
                pendingAdmission,
                pendingMedia,
                mediaBudget,
                sessionId,
                correlationId,
                deadline,
                initialPermission,
                GetNextPreparationGeneration(),
                preparation,
                callbackOwner);
            RemoteWindowSessionController controller = pendingController;
            pendingController = null;
            pendingAdmission = null;
            pendingMedia = null;
            mediaFaultGeneration = generation;
            if (!request.SourceLease.TryRegisterPreparationReservation(
                    generation.PreparationReservation,
                    out NativeRemoteWindowSourcePreparationRegistration?
                        sourcePreparation)
                || sourcePreparation is null)
            {
                throw StartFailure("native_source_stale");
            }

            generation.SourcePreparationRegistration = sourcePreparation;
            RegisterEarlySafetyObservers(generation);
            await ReserveAuthorizationPreparationAsync(
                    generation,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _ = ReadCurrentSafeProtection(generation, source);
            cancellationToken.ThrowIfCancellationRequested();
            _ = ValidateCurrentHostFacts(request, generation, out _);
            cancellationToken.ThrowIfCancellationRequested();
            ReserveEmergencyStopReadiness(generation);
            cancellationToken.ThrowIfCancellationRequested();
            _ = ValidateCurrentHostFacts(request, generation, out _);
            cancellationToken.ThrowIfCancellationRequested();
            EnsurePreparationIsCurrent(generation);
            if (!generation.PreparationReservation.TryArm(
                    CanonicalUtc(clock.UtcNow)))
            {
                throw StartFailure(GetPreparationReason(generation));
            }

            try
            {
                request.Connection.PrepareResponderRoute(
                    sessionId,
                    source.Source.ActivityId,
                    generation.PreparationReservation,
                    preparationLifetime);
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException
                && IsPreparationFactTerminal(generation))
            {
                throw StartFailure(GetPreparationReason(generation));
            }

            RemoteWindowPreparationDeliveryResult delivery;
            try
            {
                delivery = await request.Connection.PrepareAsync(
                            preparation,
                            generation.PreparationReservation,
                            cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (
                cancellationToken.IsCancellationRequested
                && exception.CancellationToken == cancellationToken)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException
                && IsPreparationFactTerminal(generation))
            {
                throw StartFailure(GetPreparationReason(generation));
            }

            if (generation.PreparationReservation.Snapshot.Termination is not null)
            {
                throw StartFailure(GetPreparationReason(generation));
            }

            RemoteWindowPreparationResponse ready = RequirePreparationResponse(
                preparation,
                delivery);
            if (!generation.PreparationReservation.TryMatchReady(
                    ready,
                    CanonicalUtc(clock.UtcNow)))
            {
                throw StartFailure(GetPreparationReason(generation));
            }

            await request.Connection.WaitForMediaAttachmentAsync(cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            EnsurePreparationIsCurrent(generation);
            source = ValidateCurrentHostFacts(
                request,
                generation,
                out _);
            RegisterProtectionObserver(generation);
            ProtectionSnapshot initialProtection =
                ReadCurrentSafeProtection(generation, source);
            cancellationToken.ThrowIfCancellationRequested();
            PromoteEmergencyStopReadiness(generation);
            cancellationToken.ThrowIfCancellationRequested();
            source = ValidateCurrentHostFacts(request, generation, out _);
            initialProtection = ReadCurrentSafeProtection(generation, source);
            NativeRemoteWindowSourcePreparationRegistration sourceReservation =
                generation.SourcePreparationRegistration
                ?? throw StartFailure("native_source_stale");
            if (!sourceReservation.IsCurrent)
            {
                _ = generation.PreparationReservation.TryInvalidate(
                    RemoteWindowHostPreparationFact.Source);
            }

            IDesktopRemoteWindowHostAuthorizationRegistration
                authorizationReservation =
                    generation.AuthorizationPreparationRegistration
                    ?? throw StartFailure("mirror_capability_denied");
            if (!authorizationReservation.IsCurrent)
            {
                _ = generation.PreparationReservation.TryInvalidate(
                    RemoteWindowHostPreparationFact.Authorization);
            }

            if (!generation.PreparationReservation.TryPromote(
                    CanonicalUtc(clock.UtcNow)))
            {
                throw StartFailure(GetPreparationReason(generation));
            }

            sourceReservation.Dispose();
            generation.SourcePreparationRegistration = null;
            await authorizationReservation.DisposeAsync().ConfigureAwait(false);
            generation.AuthorizationPreparationRegistration = null;

            RemoteWindowCommandResult started = await controller.StartAsync(
                    initialProtection,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!started.Succeeded)
            {
                throw StartFailure(started.ReasonCode);
            }

            RemoteWindowCommandResult admitted =
                await controller.AddParticipantAsync(
                        request.Connection.PeerDeviceId,
                        request.Role,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (!admitted.Succeeded
                || !admitted.Snapshot.Participants.TryGetValue(
                    request.Connection.PeerDeviceId,
                    out MirrorParticipantRole effectiveRole)
                || effectiveRole != request.Role)
            {
                throw StartFailure(admitted.ReasonCode);
            }

            generation.ControlRegistration = controlPeer.Register(
                GetNextControlGeneration(),
                request.Connection.PeerDeviceId,
                sessionId,
                controller);

            RemoteWindowParticipantState admissionState = CreateAdmissionState(
                generation,
                admitted);
            _ = ValidateCurrentHostFacts(request, generation, out _);
            _ = ReadCurrentSafeProtection(generation, source);
            EnsureFinalAdmissionIsCurrent(generation);
            try
            {
                await request.Connection.PublishAdmissionStateAsync(
                        admissionState,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (
                cancellationToken.IsCancellationRequested
                && exception.CancellationToken == cancellationToken)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                generation.CloseAdmissionNow();
                throw StartFailure("host_admission_publish_failed");
            }

            _ = ValidateCurrentHostFacts(request, generation, out _);
            _ = ReadCurrentSafeProtection(generation, source);
            EnsureFinalAdmissionIsCurrent(generation);
            if (!generation.Admission.TryOpen())
            {
                throw StartFailure("host_admission_stale");
            }

            active = generation;
            generation = null;
            return admitted;
        }
        catch (Exception failure)
        {
            if (generation is not null)
            {
                Exception? cleanupFailure = await CleanupAsync(generation)
                    .ConfigureAwait(false);
                if (cleanupFailure is not null)
                {
                    RecordCleanupFailure(generation, cleanupFailure);
                    throw new AggregateException(
                        "Remote Window host start and cleanup both failed.",
                        failure,
                        cleanupFailure);
                }
            }
            else
            {
                Exception? cleanupFailure = await CleanupUnstartedAsync(
                        request,
                        pendingController,
                        pendingAdmission,
                        pendingMedia)
                    .ConfigureAwait(false);
                if (cleanupFailure is not null)
                {
                    RecordTerminalFailure(cleanupFailure);
                    throw new AggregateException(
                        "Remote Window host validation and cleanup both failed.",
                        failure,
                        cleanupFailure);
                }
            }

            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public RemoteWindowEmergencyStopResult EmergencyStop()
    {
        ThrowIfDisposed();
        RuntimeGeneration generation = Volatile.Read(ref active)
            ?? throw new InvalidOperationException(
                "No Remote Window host session is active.");
        generation.CloseAdmissionNow();
        return generation.Controller.EmergencyStop();
    }

    public async ValueTask<RemoteWindowStopResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        if (RuntimeGeneration.HasActiveCallbackAncestry(callbackOwner))
        {
            throw new InvalidOperationException(
                "A Remote Window host stop cannot wait from a generation callback.");
        }

        ThrowIfDisposed();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            RuntimeGeneration generation = active
                ?? throw new InvalidOperationException(
                    "No Remote Window host session is active.");
            active = null;
            generation.CloseAdmissionNow();
            RemoteWindowStopResult? result = null;
            Exception? stopFailure = null;
            try
            {
                result = await generation.Controller.StopAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                stopFailure = exception;
            }

            Exception? cleanupFailure = await CleanupAsync(
                    generation,
                    controllerAlreadyStopped: result is not null)
                .ConfigureAwait(false);
            if (result is { FullyStopped: false })
            {
                RecordTerminalFailure(CreateUnconfirmedStopFailure(
                    "stop",
                    result.CaptureBoundary,
                    result.InputBoundary,
                    result.SessionBoundary));
            }

            if (stopFailure is not null && cleanupFailure is not null)
            {
                RecordCleanupFailure(generation, cleanupFailure);
                throw new AggregateException(
                    "Remote Window host stop and cleanup both failed.",
                    stopFailure,
                    cleanupFailure);
            }

            if (stopFailure is not null)
            {
                ExceptionDispatchInfo.Capture(stopFailure).Throw();
            }

            if (cleanupFailure is not null)
            {
                RecordCleanupFailure(generation, cleanupFailure);
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }

            return result
                ?? throw new InvalidOperationException(
                    "Remote Window host stop completed without a result.");
        }
        finally
        {
            gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        bool calledFromCallback = RuntimeGeneration.HasActiveCallbackAncestry(
            callbackOwner);
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            _ = CompleteDisposalAsync();
        }

        return calledFromCallback
            ? ValueTask.CompletedTask
            : new ValueTask(disposalCompletion.Task);
    }

    private async Task CompleteDisposalAsync()
    {
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
            disposalCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            disposalCompletion.TrySetException(exception);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            RuntimeGeneration? generation = active;
            active = null;
            if (generation is not null)
            {
                Exception? cleanupFailure = await CleanupAsync(generation)
                    .ConfigureAwait(false);
                if (cleanupFailure is not null)
                {
                    RecordCleanupFailure(generation, cleanupFailure);
                }
            }

            if (TerminalFailure is { } terminal)
            {
                ExceptionDispatchInfo.Capture(terminal).Throw();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private void RegisterEarlySafetyObservers(RuntimeGeneration generation)
    {
        generation.PermissionChanged = snapshot =>
        {
            if (!generation.TryEnterCallback(
                    out RuntimeGeneration.CallbackLease? callback)
                || callback is null)
            {
                return;
            }

            using (callback)
            {
                if (snapshot.OwnerGeneration
                    != generation.Request.OwnerGeneration)
                {
                    try
                    {
                        snapshot = permissions.GetSnapshot();
                    }
                    catch (Exception exception) when (
                        exception is not OutOfMemoryException)
                    {
                        _ = generation.PreparationReservation.TryInvalidate(
                            RemoteWindowHostPreparationFact.Permission);
                        RequestTerminalShutdown(
                            generation,
                            failCloseImmediately: true);
                        return;
                    }
                }

                if (!generation.TryAcceptPermissionSnapshot(
                        snapshot,
                        out bool permissionsAllow)
                    || permissionsAllow)
                {
                    return;
                }

                _ = generation.PreparationReservation.TryInvalidate(
                    RemoteWindowHostPreparationFact.Permission);
                RequestTerminalShutdown(
                    generation,
                    failCloseImmediately: true);
            }
        };
        permissions.Changed += generation.PermissionChanged;
        generation.PermissionObserverRegistered = true;
        generation.ConnectionRevocation = generation.Request.Connection
            .RegisterRevocationCallback(() => OnConnectionRevoked(generation));
    }

    private void OnConnectionRevoked(RuntimeGeneration generation)
    {
        if (!generation.TryEnterCallback(
                out RuntimeGeneration.CallbackLease? callback)
            || callback is null)
        {
            return;
        }

        using (callback)
        {
            _ = generation.PreparationReservation.TryInvalidate(
                RemoteWindowHostPreparationFact.Connection);
            RequestTerminalShutdown(generation, failCloseImmediately: false);
        }
    }

    private void OnMediaFault(
        RuntimeGeneration? generation,
        DesktopRemoteWindowLogicalVideoFrameSinkFault fault)
    {
        _ = fault;
        if (generation is null)
        {
            return;
        }

        if (!generation.TryEnterCallback(
                out RuntimeGeneration.CallbackLease? callback)
            || callback is null)
        {
            return;
        }

        using (callback)
        {
            RequestTerminalShutdown(generation, failCloseImmediately: true);
        }
    }

    private void RequestTerminalShutdown(
        RuntimeGeneration generation,
        bool failCloseImmediately)
    {
        if (Interlocked.Exchange(ref generation.TerminalShutdownStarted, 1) != 0)
        {
            return;
        }

        try
        {
            generation.CloseAdmissionNow();
        }
        catch (Exception exception)
        {
            RecordTerminalFailure(exception);
        }

        try
        {
            RemoteWindowEmergencyStopResult stopped =
                generation.Controller.EmergencyStop();
            if (!stopped.FullyStopped)
            {
                RecordTerminalFailure(CreateUnconfirmedStopFailure(
                    "emergency stop",
                    stopped.CaptureBoundary,
                    stopped.InputBoundary,
                    stopped.SessionBoundary));
            }
        }
        catch (Exception exception)
        {
            RecordTerminalFailure(exception);
        }

        if (failCloseImmediately)
        {
            _ = generation.EnsureConnectionFailClosedAsync();
        }

        _ = ThreadPool.UnsafeQueueUserWorkItem(
            static workItem =>
                _ = workItem.Coordinator.CleanupAfterTerminalSignalAsync(
                    workItem.Generation),
            new TerminalCleanupWorkItem(this, generation),
            preferLocal: false);
    }

    private async Task CleanupAfterTerminalSignalAsync(
        RuntimeGeneration generation)
    {
        var entered = false;
        try
        {
            await gate.WaitAsync().ConfigureAwait(false);
            entered = true;
            if (ReferenceEquals(active, generation))
            {
                active = null;
            }

            Exception? cleanupFailure = await CleanupAsync(generation)
                .ConfigureAwait(false);
            if (cleanupFailure is not null)
            {
                RecordCleanupFailure(generation, cleanupFailure);
            }
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref disposed) != 0)
        {
        }
        catch (Exception exception)
        {
            RecordTerminalFailure(exception);
        }
        finally
        {
            if (entered)
            {
                gate.Release();
            }
        }
    }

    private void RegisterProtectionObserver(RuntimeGeneration generation)
    {
        generation.ProtectionChanged = observation =>
        {
            if (!generation.TryEnterCallback(
                    out RuntimeGeneration.CallbackLease? callback)
                || callback is null)
            {
                return;
            }

            using (callback)
            {
                if (!MatchesProtectionIdentity(generation, observation)
                    || !generation.TryAcceptProtectionRevision(
                        observation.Revision))
                {
                    return;
                }

                if (!IsFreshSafe(observation.Protection, clock.UtcNow))
                {
                    generation.CloseAdmissionNow();
                }

                _ = generation.Controller.ApplyProtectionSnapshot(
                    observation.Protection);
            }
        };
        generation.Request.Protection.Changed += generation.ProtectionChanged;
        generation.ProtectionObserverRegistered = true;
    }

    private async ValueTask ReserveAuthorizationPreparationAsync(
        RuntimeGeneration generation,
        CancellationToken cancellationToken)
    {
        string authenticatedPeerFingerprint;
        try
        {
            authenticatedPeerFingerprint =
                generation.Request.Connection.AuthenticatedPeerFingerprint;
            if (string.IsNullOrWhiteSpace(authenticatedPeerFingerprint))
            {
                throw new InvalidOperationException(
                    "The authenticated peer fingerprint is unavailable.");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw StartFailure("authenticated_connection_stale");
        }

        DesktopRemoteWindowHostAuthorizationReservationResult reservation;
        try
        {
            reservation = await authorization.TryReservePreparationAsync(
                    generation.Request.Connection.PeerDeviceId,
                    authenticatedPeerFingerprint,
                    generation.Request.Role,
                    generation.PreparationReservation,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (
            cancellationToken.IsCancellationRequested
            && exception.CancellationToken == cancellationToken)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw StartFailure("mirror_authorization_unavailable");
        }

        IDesktopRemoteWindowHostAuthorizationRegistration? owner =
            reservation.Registration;
        if (owner is not null)
        {
            generation.AuthorizationPreparationRegistration = owner;
        }

        if (!reservation.Reserved || owner is null || !owner.IsCurrent)
        {
            _ = generation.PreparationReservation.TryInvalidate(
                RemoteWindowHostPreparationFact.Authorization);
            throw StartFailure(reservation.Boundary.Succeeded
                ? GetPreparationReason(generation)
                : reservation.Boundary.ReasonCode);
        }
    }

    private void ReserveEmergencyStopReadiness(RuntimeGeneration generation)
    {
        LocalEmergencyStopReadinessReservationResult reservation;
        try
        {
            reservation = emergencyStops.TryReserveReadiness(
                generation.Request.OwnerGeneration,
                FirstSessionGeneration,
                generation.PreparationReservation);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw StartFailure("emergency_stop_readiness_unavailable");
        }

        ILocalEmergencyStopReadinessReservation? owner =
            reservation.Reservation;
        if (owner is not null)
        {
            generation.EmergencyStopReadinessReservation = owner;
        }

        if (!reservation.Reserved
            || owner is null
            || owner.OwnerGeneration != generation.Request.OwnerGeneration
            || owner.SessionGeneration != FirstSessionGeneration)
        {
            _ = generation.PreparationReservation.TryInvalidate(
                RemoteWindowHostPreparationFact.EmergencyStop);
            throw StartFailure(reservation.Boundary.Succeeded
                ? GetPreparationReason(generation)
                : reservation.Boundary.ReasonCode);
        }
    }

    private static void PromoteEmergencyStopReadiness(
        RuntimeGeneration generation)
    {
        ILocalEmergencyStopReadinessReservation readiness =
            generation.EmergencyStopReadinessReservation
            ?? throw StartFailure("emergency_stop_readiness_unavailable");
        LocalEmergencyStopRegistrationResult registration;
        try
        {
            registration = readiness.TryPromote(activation =>
            {
                if (activation.OwnerGeneration
                        != generation.Request.OwnerGeneration
                    || activation.SessionGeneration
                        != FirstSessionGeneration)
                {
                    return;
                }

                _ = generation.PreparationReservation.TryInvalidate(
                    RemoteWindowHostPreparationFact.EmergencyStop);
                generation.CloseAdmissionNow();
                _ = generation.Controller.EmergencyStop();
            });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw StartFailure("emergency_stop_registration_failed");
        }

        ILocalEmergencyStopRegistration? formalOwner = registration.Registration;
        if (formalOwner is not null)
        {
            generation.EmergencyStopRegistration = formalOwner;
            generation.EmergencyStopReadinessReservation = null;
        }

        if (!registration.Registered
            || formalOwner is null
            || formalOwner.OwnerGeneration != generation.Request.OwnerGeneration
            || formalOwner.SessionGeneration != FirstSessionGeneration)
        {
            _ = generation.PreparationReservation.TryInvalidate(
                RemoteWindowHostPreparationFact.EmergencyStop);
            throw StartFailure(GetPreparationReason(generation));
        }
    }

    private NativeRemoteWindowSourceSnapshot ValidateCurrentHostFacts(
        DesktopRemoteWindowHostStartRequest request,
        RuntimeGeneration? generation,
        out NativeRemoteWindowPermissionSnapshot permission)
    {
        if (!request.SourceLease.TryGetCurrentSnapshot(
                out NativeRemoteWindowSourceSnapshot? current)
            || current is null
            || !request.Source.Token.Equals(current.Token)
            || request.Source.Source.ActivityId != current.Source.ActivityId
            || request.Source.Source.HostDeviceId != current.Source.HostDeviceId
            || request.Source.Source.SourceGeneration
                != current.Source.SourceGeneration
            || request.Source.GeometryRevision != current.GeometryRevision)
        {
            throw StartFailure("native_source_stale");
        }

        if (!current.Metadata.SupportsCapture
            || request.Role == MirrorParticipantRole.DriverEligible
            && !current.Metadata.SupportsInput)
        {
            throw StartFailure("native_source_unsupported");
        }

        bool connectionCurrent;
        ProtocolVersion protocolVersion;
        DeviceId localDeviceId;
        DeviceId peerDeviceId;
        try
        {
            connectionCurrent = request.Connection.IsCurrent;
            protocolVersion = request.Connection.ProtocolVersion;
            localDeviceId = request.Connection.LocalDeviceId;
            peerDeviceId = request.Connection.PeerDeviceId;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            generation?.CloseAdmissionNow();
            throw StartFailure("authenticated_connection_stale");
        }

        bool protocolSupported =
            ProtocolFeatures.SupportsRemoteWindowPreparation(protocolVersion);
        if (!connectionCurrent
            || !protocolSupported
            || localDeviceId != current.Source.HostDeviceId
            || peerDeviceId == current.Source.HostDeviceId)
        {
            throw StartFailure(
                !protocolSupported
                    ? "remote_window_protocol_unsupported"
                    : "authenticated_connection_stale");
        }

        try
        {
            permission = permissions.GetSnapshot();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            generation?.CloseAdmissionNow();
            throw StartFailure("native_permission_unavailable");
        }

        bool permissionsAllow;
        if (generation is null)
        {
            permissionsAllow = permission.OwnerGeneration
                    == request.OwnerGeneration
                && PermissionsAllow(permission, request.Role);
        }
        else
        {
            _ = generation.TryAcceptPermissionSnapshot(
                permission,
                out permissionsAllow);
        }

        if (!permissionsAllow)
        {
            throw StartFailure("native_permission_denied");
        }

        CapabilityGrant grant;
        try
        {
            grant = authorization.GetCurrentGrant(
                peerDeviceId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw StartFailure("mirror_authorization_unavailable");
        }

        if (!grant.Allows(Capability.MirrorView)
            || request.Role == MirrorParticipantRole.DriverEligible
            && !grant.Allows(Capability.MirrorDrive))
        {
            throw StartFailure("mirror_capability_denied");
        }

        return current;
    }

    private ProtectionSnapshot ReadCurrentSafeProtection(
        RuntimeGeneration generation,
        NativeRemoteWindowSourceSnapshot source)
    {
        NativeRemoteWindowProtectionObservation? observation;
        bool observed;
        try
        {
            observed = generation.Request.Protection.TryGetLatest(
                out observation);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            generation.CloseAdmissionNow();
            throw StartFailure("native_protection_not_safe");
        }

        if (!observed
            || observation is null
            || !MatchesProtectionIdentity(generation, observation)
            || observation.SourceGeneration != source.Source.SourceGeneration
            || !generation.TryAcceptProtectionRevision(observation.Revision)
            || !IsFreshSafe(observation.Protection, clock.UtcNow))
        {
            generation.CloseAdmissionNow();
            throw StartFailure("native_protection_not_safe");
        }

        return observation.Protection;
    }

    private static bool MatchesProtectionIdentity(
        RuntimeGeneration generation,
        NativeRemoteWindowProtectionObservation observation) =>
        observation.OwnerGeneration == generation.Request.OwnerGeneration
        && observation.SessionGeneration == FirstSessionGeneration
        && observation.SourceGeneration
            == generation.Request.Source.Source.SourceGeneration;

    private static bool PermissionsAllow(
        NativeRemoteWindowPermissionSnapshot snapshot,
        MirrorParticipantRole role) =>
        snapshot.Capture == NativeRemoteWindowPermissionState.Granted
        && (role != MirrorParticipantRole.DriverEligible
            || snapshot.Input == NativeRemoteWindowPermissionState.Granted);

    private static bool IsFreshSafe(
        ProtectionSnapshot snapshot,
        DateTimeOffset now) =>
        snapshot.Kind == ProtectionKind.Safe
        && snapshot.ObservedAt <= now.Add(RemoteInputPolicy.MaximumFutureClockSkew)
        && now - snapshot.ObservedAt <= RemoteInputPolicy.MaximumProtectionAge;

    private static RemoteWindowPreparationResponse RequirePreparationResponse(
        RemoteWindowPreparationRequest request,
        RemoteWindowPreparationDeliveryResult delivery)
    {
        if (delivery.Status != RemoteWindowControlDeliveryStatus.Acknowledged
            || delivery.Response is not { } response
            || response.Request != request)
        {
            throw StartFailure("remote_window_prepare_not_acknowledged");
        }

        return response;
    }

    private static string GetPreparationReason(RuntimeGeneration generation) =>
        generation.PreparationReservation.Snapshot.Termination?.ReasonCode
        ?? "host_preparation_stale";

    private static bool IsPreparationFactTerminal(
        RuntimeGeneration generation) =>
        generation.PreparationReservation.Snapshot.Termination?.Fact is not null;

    private static RemoteWindowParticipantState CreateAdmissionState(
        RuntimeGeneration generation,
        RemoteWindowCommandResult admitted)
    {
        RemoteWindowSharingSnapshot snapshot = admitted.Snapshot;
        snapshot.Participants.TryGetValue(
            generation.Request.Connection.PeerDeviceId,
            out MirrorParticipantRole effectiveRole);
        return RemoteWindowParticipantState.Create(
            generation.CorrelationId,
            generation.SessionId,
            snapshot.ActivityId,
            snapshot.HostDeviceId,
            generation.Request.Connection.PeerDeviceId,
            RemoteWindowControlAction.Admission,
            admitted.Status == RemoteWindowCommandStatus.Applied
                ? RemoteWindowControlOutcome.Applied
                : RemoteWindowControlOutcome.AlreadyApplied,
            admitted.ReasonCode,
            snapshot.Lifecycle,
            snapshot.CaptureState,
            snapshot.Participants.Count,
            effectiveRole,
            snapshot.CurrentDriverDeviceId,
            snapshot.DriverLeaseEpoch,
            snapshot.DriverLeaseExpiresAt?.ToUniversalTime(),
            snapshot.ProtectionKind,
            snapshot.Revision);
    }

    private void EnsurePreparationIsCurrent(RuntimeGeneration generation)
    {
        if (CanonicalUtc(clock.UtcNow) < generation.Deadline)
        {
            return;
        }

        generation.CloseAdmissionNow();
        throw StartFailure("preparation_expired");
    }

    private static void EnsureFinalAdmissionIsCurrent(
        RuntimeGeneration generation)
    {
        RemoteWindowSharingSnapshot snapshot = generation.Controller.Snapshot;
        if (generation.EmergencyStopRegistration?.IsCurrent != true
            || generation.ControlRegistration?.IsCurrent != true
            || snapshot.Lifecycle != RemoteWindowLifecycle.Active
            || snapshot.CaptureState != RemoteWindowCaptureState.Capturing
            || !snapshot.Participants.TryGetValue(
                generation.Request.Connection.PeerDeviceId,
                out MirrorParticipantRole role)
            || role != generation.Request.Role)
        {
            generation.CloseAdmissionNow();
            throw StartFailure("host_admission_stale");
        }
    }

    private Task<Exception?> CleanupAsync(
        RuntimeGeneration generation,
        bool controllerAlreadyStopped = false) =>
        generation.EnsureCleanupAsync(
            () => CleanupCoreAsync(generation, controllerAlreadyStopped));

    private async Task<Exception?> CleanupCoreAsync(
        RuntimeGeneration generation,
        bool controllerAlreadyStopped)
    {
        Task callbackDrain = generation.RetireCallbacks();
        var failures = new List<Exception>();
        CaptureFailure(failures, generation.CloseAdmissionNow);
        CaptureFailure(
            failures,
            () => generation.SourcePreparationRegistration?.Dispose());
        CaptureFailure(failures, generation.PreparationReservation.Dispose);
        if (generation.AuthorizationPreparationRegistration is { } authorization)
        {
            await CaptureFailureAsync(failures, authorization.DisposeAsync)
                .ConfigureAwait(false);
        }

        CaptureFailure(failures, () => generation.ConnectionRevocation?.Dispose());
        if (generation.ProtectionObserverRegistered)
        {
            CaptureFailure(
                failures,
                () => generation.Request.Protection.Changed -=
                    generation.ProtectionChanged);
        }

        if (generation.PermissionObserverRegistered)
        {
            CaptureFailure(
                failures,
                () => permissions.Changed -= generation.PermissionChanged);
        }

        await callbackDrain.ConfigureAwait(false);

        if (!controllerAlreadyStopped)
        {
            try
            {
                RemoteWindowStopResult stopped =
                    await generation.Controller.StopAsync().ConfigureAwait(false);
                if (!stopped.FullyStopped)
                {
                    failures.Add(CreateUnconfirmedStopFailure(
                        "cleanup stop",
                        stopped.CaptureBoundary,
                        stopped.InputBoundary,
                        stopped.SessionBoundary));
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        CaptureFailure(
            failures,
            () => generation.EmergencyStopReadinessReservation?.Dispose());
        CaptureFailure(
            failures,
            () => generation.EmergencyStopRegistration?.Dispose());

        Task<Exception?>? failClose =
            generation.GetStartedConnectionFailCloseTask();
        if (generation.PreparationReservation.Snapshot.RouteMayBeOwned)
        {
            failClose ??= generation.EnsureConnectionFailClosedAsync();
        }

        if (failClose is not null)
        {
            Exception? failCloseFailure =
                await failClose.ConfigureAwait(false);
            if (failCloseFailure is not null)
            {
                failures.Add(failCloseFailure);
            }
        }

        await CaptureFailureAsync(failures, generation.Media.DisposeAsync)
            .ConfigureAwait(false);
        CaptureFailure(failures, () => generation.ControlRegistration?.Dispose());
        CaptureFailure(failures, generation.Controller.Dispose);
        CaptureFailure(failures, generation.Request.Protection.Dispose);

        await CaptureFailureAsync(
                failures,
                generation.Request.Connection.DisposeAsync)
            .ConfigureAwait(false);
        CaptureFailure(failures, generation.Admission.Dispose);
        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException(
                "Remote Window host cleanup failed.",
                failures),
        };
    }

    private static async ValueTask<Exception?> CleanupUnstartedAsync(
        DesktopRemoteWindowHostStartRequest request,
        RemoteWindowSessionController? controller,
        DesktopRemoteWindowFrameAdmissionSink? admission,
        DesktopRemoteWindowLogicalVideoFrameSink? media)
    {
        var failures = new List<Exception>();
        CaptureFailure(failures, () => admission?.CloseNow());
        CaptureFailure(failures, () => controller?.Dispose());
        if (media is not null)
        {
            await CaptureFailureAsync(failures, media.DisposeAsync)
                .ConfigureAwait(false);
        }
        CaptureFailure(failures, request.Protection.Dispose);
        await CaptureFailureAsync(failures, request.Connection.DisposeAsync)
            .ConfigureAwait(false);
        CaptureFailure(failures, () => admission?.Dispose());
        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException(
                "Remote Window host pre-start cleanup failed.",
                failures),
        };
    }

    private static void CaptureFailure(List<Exception> failures, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static async ValueTask CaptureFailureAsync(
        List<Exception> failures,
        Func<ValueTask> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static DateTimeOffset CanonicalUtc(DateTimeOffset value)
    {
        DateTimeOffset utc = value.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMillisecond));
    }

    private static InvalidOperationException CreateUnconfirmedStopFailure(
        string operation,
        LocalBoundaryResult capture,
        LocalBoundaryResult input,
        LocalBoundaryResult sessions) => new(
        $"Remote Window host {operation} was not fully confirmed "
        + $"(capture={capture.ReasonCode}, input={input.ReasonCode}, "
        + $"sessions={sessions.ReasonCode}).");

    private void RecordTerminalFailure(Exception failure)
    {
        lock (terminalFailureGate)
        {
            terminalFailure = terminalFailure is null
                ? failure
                : new AggregateException(
                    "Multiple Remote Window terminal cleanup failures occurred.",
                    terminalFailure,
                    failure);
        }
    }

    private void RecordCleanupFailure(
        RuntimeGeneration generation,
        Exception failure)
    {
        if (Interlocked.Exchange(ref generation.CleanupFailureRecorded, 1) == 0)
        {
            RecordTerminalFailure(failure);
        }
    }

    private long GetNextControlGeneration()
    {
        long generation = Interlocked.Increment(ref nextControlGeneration);
        if (generation < 1)
        {
            throw new InvalidOperationException(
                "Remote Window host control generation capacity was exhausted.");
        }

        return generation;
    }

    private long GetNextPreparationGeneration()
    {
        long generation = Interlocked.Increment(ref nextPreparationGeneration);
        if (generation < 1)
        {
            throw new InvalidOperationException(
                "Remote Window host Preparation generation capacity was exhausted.");
        }

        return generation;
    }

    private static InvalidOperationException StartFailure(string reasonCode) =>
        new($"Remote Window host start failed ({reasonCode}).");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(
        Volatile.Read(ref disposed) != 0,
        this);

    private sealed class RuntimeGeneration(
        DesktopRemoteWindowHostStartRequest request,
        RemoteWindowSessionController controller,
        DesktopRemoteWindowFrameAdmissionSink admission,
        DesktopRemoteWindowLogicalVideoFrameSink media,
        RemoteWindowMediaSessionBudget mediaBudget,
        RemoteWindowSessionId sessionId,
        CorrelationId correlationId,
        DateTimeOffset deadline,
        NativeRemoteWindowPermissionSnapshot initialPermission,
        long preparationGeneration,
        RemoteWindowPreparationRequest preparation,
        object callbackOwner)
    {
        private static readonly AsyncLocal<CallbackScope?> CallbackAncestry = new();
        private NativeRemoteWindowPermissionSnapshot acceptedPermission =
            initialPermission;
        private readonly HashSet<CallbackLease> activeCallbacks = [];
        private readonly object callbackGate = new();
        private readonly object callbackOwner = callbackOwner;
        private readonly object protectionGate = new();
        private readonly object permissionGate = new();
        private readonly object connectionFailCloseGate = new();
        private readonly object cleanupGate = new();
        private bool callbacksRetired;
        private Task<Exception?>? cleanup;
        private Task<Exception?>? connectionFailClose;
        private long protectionRevision;

        public DesktopRemoteWindowFrameAdmissionSink Admission { get; } = admission;

        public IDesktopRemoteWindowHostAuthorizationRegistration?
            AuthorizationPreparationRegistration
        {
            get;
            set;
        }

        public int TerminalShutdownStarted;

        public int CleanupFailureRecorded;

        public ILocalEmergencyStopRegistration? EmergencyStopRegistration
        {
            get;
            set;
        }

        public ILocalEmergencyStopReadinessReservation?
            EmergencyStopReadinessReservation
        {
            get;
            set;
        }

        public IDisposable? ConnectionRevocation { get; set; }

        public DesktopRemoteWindowHostControlRegistration? ControlRegistration
        {
            get;
            set;
        }

        public RemoteWindowSessionController Controller { get; } = controller;

        public CorrelationId CorrelationId { get; } = correlationId;

        public DateTimeOffset Deadline { get; } = deadline;

        public DesktopRemoteWindowLogicalVideoFrameSink Media { get; } = media;

        public RemoteWindowMediaSessionBudget MediaBudget { get; } = mediaBudget;

        public RemoteWindowHostPreparationReservation PreparationReservation
        {
            get;
        } = new(
            preparationGeneration,
            preparation,
            RemoteWindowHostPreparationEpochBundle.Create());

        public Action<NativeRemoteWindowPermissionSnapshot>? PermissionChanged
        {
            get;
            set;
        }

        public bool PermissionObserverRegistered { get; set; }

        public Action<NativeRemoteWindowProtectionObservation>? ProtectionChanged
        {
            get;
            set;
        }

        public bool ProtectionObserverRegistered { get; set; }

        public DesktopRemoteWindowHostStartRequest Request { get; } = request;

        public NativeRemoteWindowSourcePreparationRegistration?
            SourcePreparationRegistration
        {
            get;
            set;
        }

        public RemoteWindowSessionId SessionId { get; } = sessionId;

        public void CloseAdmissionNow()
        {
            ControlRegistration?.CloseNow();
            Admission.CloseNow();
            Media.StopNow();
        }

        public bool TryEnterCallback(out CallbackLease? callback)
        {
            lock (callbackGate)
            {
                if (callbacksRetired)
                {
                    callback = null;
                    return false;
                }

                callback = new CallbackLease(this);
                callback.Activate();
                _ = activeCallbacks.Add(callback);
                return true;
            }
        }

        public Task RetireCallbacks()
        {
            CallbackLease[] callbacks;
            lock (callbackGate)
            {
                callbacksRetired = true;
                callbacks = [.. activeCallbacks];
            }

            return callbacks.Length == 0
                ? Task.CompletedTask
                : Task.WhenAll(callbacks.Select(
                    static callback => callback.Completion));
        }

        public static bool HasActiveCallbackAncestry(object owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            for (CallbackScope? scope = CallbackAncestry.Value;
                scope is not null;
                scope = scope.Previous)
            {
                if (scope.IsActive && ReferenceEquals(scope.Owner, owner))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryAcceptPermissionSnapshot(
            NativeRemoteWindowPermissionSnapshot snapshot,
            out bool permissionsAllow)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            lock (permissionGate)
            {
                if (snapshot.OwnerGeneration != Request.OwnerGeneration)
                {
                    permissionsAllow = false;
                    return true;
                }

                if (snapshot.Revision < acceptedPermission.Revision)
                {
                    permissionsAllow = PermissionsAllow(
                        acceptedPermission,
                        Request.Role);
                    return false;
                }

                acceptedPermission = snapshot;
                permissionsAllow = PermissionsAllow(snapshot, Request.Role);
                return true;
            }
        }

        public bool TryAcceptProtectionRevision(long revision)
        {
            lock (protectionGate)
            {
                if (revision < protectionRevision)
                {
                    return false;
                }

                protectionRevision = revision;
                return true;
            }
        }

        public Task<Exception?> EnsureConnectionFailClosedAsync()
        {
            lock (connectionFailCloseGate)
            {
                return connectionFailClose ??= FailCloseAsync(Request.Connection);
            }
        }

        public Task<Exception?>? GetStartedConnectionFailCloseTask()
        {
            lock (connectionFailCloseGate)
            {
                return connectionFailClose;
            }
        }

        public Task<Exception?> EnsureCleanupAsync(
            Func<Task<Exception?>> cleanupFactory)
        {
            TaskCompletionSource<Exception?> completion;
            lock (cleanupGate)
            {
                if (cleanup is not null)
                {
                    return cleanup;
                }

                completion = new TaskCompletionSource<Exception?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                cleanup = completion.Task;
            }

            _ = CompleteCleanupAsync(cleanupFactory, completion);
            return completion.Task;
        }

        private static async Task CompleteCleanupAsync(
            Func<Task<Exception?>> cleanupFactory,
            TaskCompletionSource<Exception?> completion)
        {
            try
            {
                completion.TrySetResult(
                    await cleanupFactory().ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                completion.TrySetResult(exception);
            }
        }

        private static async Task<Exception?> FailCloseAsync(
            IDesktopRemoteWindowHostConnection connection)
        {
            try
            {
                await connection.FailCloseAsync().ConfigureAwait(false);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private void ExitCallback(CallbackLease callback)
        {
            lock (callbackGate)
            {
                _ = activeCallbacks.Remove(callback);
            }
        }

        public sealed class CallbackLease(RuntimeGeneration owner) : IDisposable
        {
            private readonly TaskCompletionSource completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly CallbackScope scope = new(
                owner.callbackOwner,
                CallbackAncestry.Value);
            private RuntimeGeneration? owner = owner;

            public Task Completion => completion.Task;

            public void Dispose()
            {
                RuntimeGeneration? current = Interlocked.Exchange(
                    ref owner,
                    null);
                if (current is null)
                {
                    return;
                }

                scope.Deactivate();
                if (ReferenceEquals(CallbackAncestry.Value, scope))
                {
                    CallbackAncestry.Value = scope.Previous;
                }

                current.ExitCallback(this);
                completion.TrySetResult();
            }

            public void Activate()
            {
                scope.Activate();
                CallbackAncestry.Value = scope;
            }
        }

        private sealed class CallbackScope(
            object owner,
            CallbackScope? previous)
        {
            private int active;

            public bool IsActive => Volatile.Read(ref active) != 0;

            public object Owner { get; } = owner;

            public CallbackScope? Previous { get; } = previous;

            public void Activate() => Volatile.Write(ref active, 1);

            public void Deactivate() => Volatile.Write(ref active, 0);
        }
    }

    private sealed record TerminalCleanupWorkItem(
        DesktopRemoteWindowHostCoordinator Coordinator,
        RuntimeGeneration Generation);
}
