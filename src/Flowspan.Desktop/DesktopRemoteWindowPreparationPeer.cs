using System.Runtime.ExceptionServices;
using Flowspan.Domain;
using Flowspan.Transport;

namespace Flowspan.Desktop;

internal delegate bool TryAcquireDesktopRemoteWindowPeerConnection(
    DeviceId peerDeviceId,
    out AuthenticatedRemoteWindowConnectionLease? lease);

internal interface IDesktopRemoteWindowReceivePolicy
{
    public string? GetRejectionReason(RemoteWindowPreparationRequest request);
}

internal sealed class AllowDesktopRemoteWindowReceivePolicy :
    IDesktopRemoteWindowReceivePolicy
{
    private AllowDesktopRemoteWindowReceivePolicy()
    {
    }

    public static AllowDesktopRemoteWindowReceivePolicy Instance { get; } = new();

    public string? GetRejectionReason(RemoteWindowPreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return null;
    }
}

internal sealed class UnavailableDesktopRemoteWindowReceivePolicy :
    IDesktopRemoteWindowReceivePolicy
{
    private UnavailableDesktopRemoteWindowReceivePolicy()
    {
    }

    public static UnavailableDesktopRemoteWindowReceivePolicy Instance { get; } =
        new();

    public string? GetRejectionReason(RemoteWindowPreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return "renderer_unavailable";
    }
}

internal interface IDesktopRemoteWindowParticipantRendererFactory
{
    public ValueTask<IDesktopRemoteWindowParticipantRenderer?> PrepareAsync(
        RemoteWindowPreparationRequest request,
        CancellationToken cancellationToken);
}

internal interface IDesktopRemoteWindowParticipantRenderer : IAsyncDisposable
{
    public ValueTask RenderAsync(
        DesktopRemoteWindowBgraFrame frame,
        CancellationToken cancellationToken);
}

internal sealed class UnavailableDesktopRemoteWindowParticipantRendererFactory :
    IDesktopRemoteWindowParticipantRendererFactory
{
    private UnavailableDesktopRemoteWindowParticipantRendererFactory()
    {
    }

    public static UnavailableDesktopRemoteWindowParticipantRendererFactory Instance
    {
        get;
    } = new();

    public ValueTask<IDesktopRemoteWindowParticipantRenderer?> PrepareAsync(
        RemoteWindowPreparationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<
            IDesktopRemoteWindowParticipantRenderer?>(null);
    }
}

internal sealed class DesktopRemoteWindowPreparationPeer :
    IRemoteWindowPreparationPeer,
    IAsyncDisposable
{
    private static readonly AsyncLocal<ParticipantCallbackScope?> CurrentPreparer = new();
    private static readonly AsyncLocal<ParticipantCallbackScope?> CurrentReceiver = new();
    private readonly ParticipantGeneration?[] active = new ParticipantGeneration?[1];
    private readonly TaskCompletionSource disposalCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly IDesktopRemoteWindowReceivePolicy receivePolicy;
    private readonly IDesktopRemoteWindowParticipantRendererFactory rendererFactory;
    private readonly TimeProvider timeProvider;
    private readonly TryAcquireDesktopRemoteWindowPeerConnection tryAcquireConnection;
    private ParticipantGeneration? pendingResponseCleanup;
    private ParticipantGeneration? ownedGeneration;
    private long receiveEpoch;
    private Task receiveStopTask = Task.CompletedTask;
    private ParticipantGeneration? receiveStopGeneration;
    private int disposed;

    public DesktopRemoteWindowPreparationPeer(
        DeviceId participantDeviceId,
        TryAcquireDesktopRemoteWindowPeerConnection tryAcquireConnection,
        IDesktopRemoteWindowReceivePolicy receivePolicy,
        IDesktopRemoteWindowParticipantRendererFactory rendererFactory,
        TimeProvider? timeProvider = null)
    {
        ParticipantDeviceId = participantDeviceId
            ?? throw new ArgumentNullException(nameof(participantDeviceId));
        this.tryAcquireConnection = tryAcquireConnection
            ?? throw new ArgumentNullException(nameof(tryAcquireConnection));
        this.receivePolicy = receivePolicy
            ?? throw new ArgumentNullException(nameof(receivePolicy));
        this.rendererFactory = rendererFactory
            ?? throw new ArgumentNullException(nameof(rendererFactory));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DeviceId ParticipantDeviceId { get; }

    public async ValueTask<RemoteWindowPreparationResponse> PrepareAsync(
        RemoteWindowPreparationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateParticipantBinding(request);
        if (timeProvider.GetUtcNow() >= request.Deadline)
        {
            return Rejected(request, "preparation_expired");
        }

        string? policyRejection;
        long preparationEpoch;
        lock (gate)
        {
            preparationEpoch = receiveEpoch;
        }

        try
        {
            policyRejection = BoundPolicyRejection(
                receivePolicy.GetRejectionReason(request));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            policyRejection = "renderer_unavailable";
        }

        if (policyRejection is not null)
        {
            return Rejected(request, policyRejection);
        }

        ParticipantGeneration generation;
        lock (gate)
        {
            if (disposed != 0 || preparationEpoch != receiveEpoch)
            {
                return Rejected(request, "participant_stopping");
            }

            generation = new ParticipantGeneration(
                request,
                lifetimeCancellation.Token,
                cancellationToken);
            if (ownedGeneration is not null || !receiveStopTask.IsCompleted)
            {
                generation.DisposeCancellation();
                return Rejected(request, "participant_busy");
            }

            active[0] = generation;
            ownedGeneration = generation;
        }

        ParticipantCallbackScope? previousPreparer = CurrentPreparer.Value;
        var preparationScope = new ParticipantCallbackScope(generation, previousPreparer);
        CurrentPreparer.Value = preparationScope;
        PreparationStage stage = PreparationStage.AcquiringMedia;
        try
        {
            AuthenticatedRemoteWindowConnectionLease? lease = null;
            bool acquired;
            try
            {
                acquired = tryAcquireConnection(
                    request.HostDeviceId,
                    out lease);
            }
            finally
            {
                if (lease is not null)
                {
                    generation.AttachLease(lease);
                }
            }

            if (!acquired
                || lease is null
                || lease.LocalDeviceId != ParticipantDeviceId
                || lease.PeerDeviceId != request.HostDeviceId
                || !lease.IsCurrent)
            {
                await CleanupGenerationAsync(
                    generation,
                    failClose: false).ConfigureAwait(false);
                return Rejected(request, "media_unavailable");
            }

            generation.AttachRevocationRegistration(
                lease.RegisterRevocationCallback(generation.Cancel));
            generation.Token.ThrowIfCancellationRequested();

            stage = PreparationStage.AttachingMedia;
            generation.MarkConnectionAttempted();
            await lease.ConnectInitiatorForPreparationAsync(
                    request,
                    generation.Token)
                .ConfigureAwait(false);

            stage = PreparationStage.PreparingRenderer;
            IDesktopRemoteWindowParticipantRenderer? renderer =
                await rendererFactory.PrepareAsync(request, generation.Token)
                    .ConfigureAwait(false);
            if (renderer is null)
            {
                var unavailable = new InvalidOperationException(
                    "The Remote Window renderer factory returned no renderer.");
                if (TryDeferResponseCleanup(generation, unavailable))
                {
                    return Rejected(request, "renderer_unavailable");
                }

                await CleanupGenerationAfterFailureAsync(
                    generation,
                    failClose: true,
                    unavailable).ConfigureAwait(false);
                return Rejected(request, "renderer_unavailable");
            }

            generation.AttachRenderer(renderer);
            generation.Token.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (!ReferenceEquals(active[0], generation)
                    || disposed != 0
                    || generation.Token.IsCancellationRequested
                    || timeProvider.GetUtcNow() >= request.Deadline
                    || !lease.IsCurrent)
                {
                    throw new OperationCanceledException(generation.Token);
                }

                generation.MarkReady();
            }

            return RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready");
        }
        catch (OperationCanceledException exception) when (
            generation.Token.IsCancellationRequested
            || cancellationToken.IsCancellationRequested
            || timeProvider.GetUtcNow() >= request.Deadline)
        {
            string reason = GetCancellationReason(request);
            await CleanupGenerationAfterFailureAsync(
                generation,
                generation.ConnectionAttempted,
                exception).ConfigureAwait(false);
            return Rejected(request, reason);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            string reason = stage is PreparationStage.PreparingRenderer
                ? "renderer_start_failed"
                : stage is PreparationStage.AttachingMedia
                    ? "media_attachment_failed"
                    : "media_unavailable";
            if ((stage is PreparationStage.AttachingMedia
                    or PreparationStage.PreparingRenderer)
                && TryDeferResponseCleanup(generation, exception))
            {
                return Rejected(request, reason);
            }

            await CleanupGenerationAfterFailureAsync(
                generation,
                generation.ConnectionAttempted,
                exception).ConfigureAwait(false);
            return Rejected(request, reason);
        }
        finally
        {
            generation.CompletePreparation();
            preparationScope.Exit();
            CurrentPreparer.Value = previousPreparer;
        }
    }

    public async ValueTask CompletePreparationResponseAsync(
        RemoteWindowPreparationResponse response,
        bool responseCommitted)
    {
        ArgumentNullException.ThrowIfNull(response);
        ValidateParticipantBinding(response.Request);
        ParticipantGeneration? generation;
        lock (gate)
        {
            generation = pendingResponseCleanup;
            if (generation is null || generation.Request != response.Request)
            {
                return;
            }

            if (response.Outcome is not RemoteWindowPreparationOutcome.Rejected)
            {
                throw new InvalidDataException(
                    "A pending Remote Window preparation cleanup requires a rejected response.");
            }

            pendingResponseCleanup = null;
        }

        Exception primaryFailure = generation.PreparationFailure
            ?? new InvalidOperationException(
                "The rejected Remote Window preparation lost its primary failure.");
        await CleanupGenerationAfterFailureAsync(
                generation,
                failClose: !responseCommitted,
                primaryFailure)
            .ConfigureAwait(false);
    }

    private async ValueTask CleanupGenerationAfterFailureAsync(
        ParticipantGeneration generation,
        bool failClose,
        Exception primaryFailure)
    {
        try
        {
            await CleanupGenerationAsync(generation, failClose)
                .ConfigureAwait(false);
        }
        catch (Exception cleanupFailure)
        {
            Exception combined = CombineFailures(primaryFailure, cleanupFailure)
                ?? primaryFailure;
            ExceptionDispatchInfo.Capture(combined).Throw();
        }
    }

    public async ValueTask CompleteAdmissionAsync(
        RemoteWindowPreparationRequest request,
        RemoteWindowParticipantState state,
        CancellationToken cancellationToken)
    {
        ValidateParticipantBinding(request);
        ValidateAdmissionBinding(request, state);
        ParticipantGeneration generation;
        bool applied = state.Outcome is
            RemoteWindowControlOutcome.Applied
            or RemoteWindowControlOutcome.AlreadyApplied;
        bool admissionCancelled = false;
        bool admissionInvalid = false;
        TaskCompletionSource? receiveStart = null;
        lock (gate)
        {
            generation = active[0]
                ?? throw new InvalidDataException(
                    "The Remote Window participant has no prepared generation.");
            if (generation.Request != request || !generation.IsReady)
            {
                throw new InvalidDataException(
                    "The Remote Window admission does not match the prepared generation.");
            }

            if (!applied)
            {
                active[0] = null;
            }
            else
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    active[0] = null;
                    admissionCancelled = true;
                }
                else if (disposed != 0
                    || generation.Token.IsCancellationRequested
                    || timeProvider.GetUtcNow() >= request.Deadline
                    || generation.Lease?.IsCurrent != true
                    || state.EffectiveRole != request.RequestedRole)
                {
                    active[0] = null;
                    admissionInvalid = true;
                }
                else
                {
                    receiveStart = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    generation.MarkAdmitted();
                    generation.SetReceiveTask(RunReceiveLoopAsync(
                        generation,
                        receiveStart.Task));
                }
            }
        }

        receiveStart?.TrySetResult();

        if (admissionCancelled)
        {
            await CleanupGenerationAfterFailureAsync(
                    generation,
                    failClose: true,
                    new OperationCanceledException(cancellationToken))
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (!applied || admissionInvalid)
        {
            await CleanupGenerationAsync(generation, failClose: true)
                .ConfigureAwait(false);
        }

        if (admissionInvalid)
        {
            throw new InvalidDataException(
                "The Remote Window admission is no longer current for the prepared generation.");
        }
    }

    public async ValueTask PeerDisconnectedAsync(
        DeviceId hostDeviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hostDeviceId);
        cancellationToken.ThrowIfCancellationRequested();
        ParticipantGeneration? generation = DetachGeneration(hostDeviceId);
        if (generation is not null)
        {
            await CleanupTerminalGenerationAsync(generation)
                .ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref disposed, 1, 0) == 0)
        {
            _ = DisposeCoreAsync();
        }

        return new ValueTask(disposalCompletion.Task);
    }

    // This is the external local-user stop path. Protocol callbacks keep using
    // their existing generation cleanup so that they never join their own
    // authenticated control dispatcher.
    public ValueTask StopReceivingAsync()
    {
        ParticipantGeneration? generation;
        TaskCompletionSource completion;
        lock (gate)
        {
            if (!receiveStopTask.IsCompleted)
            {
                return receiveStopGeneration is { } stoppingGeneration
                    && HasActiveCallback(stoppingGeneration)
                        ? ValueTask.CompletedTask
                        : new ValueTask(receiveStopTask);
            }

            receiveEpoch = checked(receiveEpoch + 1);
            generation = ownedGeneration;
            active[0] = null;
            pendingResponseCleanup = null;
            if (generation is null)
            {
                return new ValueTask(receiveStopTask);
            }

            completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            receiveStopTask = completion.Task;
            receiveStopGeneration = generation;
        }
        bool calledFromCallback = HasActiveCallback(generation);
        // The facade may defer its own callback's wait. The one real stop
        // worker always has independent ancestry and joins preparation,
        // rendering, and the exact authenticated registration to completion.
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(() => CompleteReceiveStopAsync(generation, completion));
        }

        return calledFromCallback
                ? ValueTask.CompletedTask
                : new ValueTask(completion.Task);
    }

    private async Task CompleteReceiveStopAsync(
        ParticipantGeneration generation,
        TaskCompletionSource completion)
    {
        try
        {
            await StopGenerationExternallyAsync(generation).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private async Task StopGenerationExternallyAsync(ParticipantGeneration generation)
    {
        Exception? failure = CaptureCleanupFailure(generation.Cancel);
        AuthenticatedRemoteWindowConnectionLease? earlyLease = generation.Lease;
        Task? earlyClose = null;
        try
        {
            earlyClose = earlyLease?.FailCloseAsync().AsTask();
        }
        catch (ObjectDisposedException) when (generation.IsCleanupStarted)
        {
            // The existing cleanup already released this borrowed lease.
        }
        catch (Exception exception)
        {
            failure = CombineFailures(failure, exception);
        }
        failure = CombineFailures(failure,
            await CaptureCleanupFailureAsync(() => CleanupTerminalGenerationAsync(generation))
                .ConfigureAwait(false));
        if (earlyClose is not null)
        {
            failure = CombineFailures(failure,
                await CaptureCleanupFailureAsync(() => new ValueTask(earlyClose))
                    .ConfigureAwait(false));
        }

        AuthenticatedRemoteWindowConnectionLease? finalLease = generation.Lease;
        if (finalLease is not null)
        {
            failure = CombineFailures(failure,
                await CaptureCleanupFailureAsync(finalLease.WaitForConnectionClosedAsync)
                    .ConfigureAwait(false));
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private async Task RunReceiveLoopAsync(
        ParticipantGeneration generation,
        Task receiveStart)
    {
        await receiveStart.ConfigureAwait(false);
        ParticipantCallbackScope? previous = CurrentReceiver.Value;
        var receiveScope = new ParticipantCallbackScope(generation, previous);
        CurrentReceiver.Value = receiveScope;
        try
        {
            AuthenticatedRemoteWindowConnectionLease lease = generation.Lease
                ?? throw new InvalidOperationException(
                    "The admitted Remote Window participant lost its media lease.");
            IDesktopRemoteWindowParticipantRenderer renderer = generation.Renderer
                ?? throw new InvalidOperationException(
                    "The admitted Remote Window participant lost its renderer.");
            var assembler = new RemoteWindowVideoFrameAssembler(
                generation.Request.SessionId,
                generation.Request.ActivityId);
            generation.AttachAssembler(assembler);
            while (true)
            {
                RemoteWindowMediaFrame frame = await lease.ReceiveMediaAsync(
                        generation.Token)
                    .ConfigureAwait(false);
                RemoteWindowVideoFrameAssembly? assembly = assembler.Add(frame);
                if (assembly is null)
                {
                    continue;
                }

                using (assembly)
                {
                    DesktopRemoteWindowJpegDecodingResult decoded =
                        DesktopRemoteWindowJpegCodec.Decode(assembly.Payload);
                    if (!decoded.Succeeded || decoded.Frame is null)
                    {
                        throw new InvalidDataException(
                            "The Remote Window participant received an invalid video frame.");
                    }

                    using DesktopRemoteWindowBgraFrame renderedFrame = decoded.Frame;
                    await renderer.RenderAsync(renderedFrame, generation.Token)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (generation.Token.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            generation.RecordReceiveFailure(exception);
            if (!generation.IsCleanupStarted)
            {
                try
                {
                    await CleanupGenerationAsync(generation, failClose: true)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // The generation cleanup retains the combined terminal failure.
                }
            }
        }
        finally
        {
            receiveScope.Exit();
            CurrentReceiver.Value = previous;
        }
    }

    private async Task DisposeCoreAsync()
    {
        Exception? failure = CaptureCleanupFailure(lifetimeCancellation.Cancel);
        ParticipantGeneration? generation;
        Exception? preparationFailure = null;
        bool terminalCleanupFailed = false;
        lock (gate)
        {
            generation = ownedGeneration;
            active[0] = null;
            if (ReferenceEquals(pendingResponseCleanup, generation))
            {
                pendingResponseCleanup = null;
            }
        }

        if (generation is not null)
        {
            preparationFailure = generation.PreparationFailure;
            try
            {
                await CleanupTerminalGenerationAsync(generation)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                terminalCleanupFailed = true;
                failure = CombineFailures(failure, exception);
            }

        }

        Task stopping;
        lock (gate)
        {
            stopping = receiveStopTask;
        }

        if (generation is null
            || !HasActiveCallback(generation))
        {
            failure = CombineFailures(failure,
                await CaptureCleanupFailureAsync(() => new ValueTask(stopping))
                    .ConfigureAwait(false));
        }

        failure = CombineFailures(
            failure,
            CaptureCleanupFailure(lifetimeCancellation.Dispose));
        if (!terminalCleanupFailed
            && preparationFailure is not null
            && failure is not null)
        {
            failure = CombineFailures(preparationFailure, failure);
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

    private async ValueTask CleanupGenerationAsync(
        ParticipantGeneration generation,
        bool failClose)
    {
        DetachGeneration(generation);
        if (!generation.TryBeginCleanup())
        {
            if (HasActiveCallback(generation))
            {
                return;
            }

            await generation.CleanupCompletion.ConfigureAwait(false);
            return;
        }

        Exception? failure = null;
        try
        {
            generation.Cancel();
        }
        catch (Exception exception)
        {
            failure = CombineFailures(failure, exception);
        }

        if (!IsActiveFor(CurrentPreparer.Value, generation))
        {
            await generation.PreparationCompletion.ConfigureAwait(false);
        }

        Task? receiveTask = generation.ReceiveTask;
        if (receiveTask is not null
            && !IsActiveFor(CurrentReceiver.Value, generation))
        {
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (generation.Token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }
        }

        failure = CombineFailures(failure, generation.ReceiveFailure);

        failure = CombineFailures(
            failure,
            CaptureCleanupFailure(generation.DisposeAssembler));
        failure = CombineFailures(
            failure,
            await CaptureCleanupFailureAsync(generation.DisposeRendererAsync)
                .ConfigureAwait(false));
        failure = CombineFailures(
            failure,
            CaptureCleanupFailure(generation.DisposeRevocationRegistration));

        AuthenticatedRemoteWindowConnectionLease? lease = generation.Lease;
        if (failClose && lease is not null && !lease.IsRevoked)
        {
            failure = CombineFailures(
                failure,
                await CaptureCleanupFailureAsync(lease.FailCloseAsync)
                    .ConfigureAwait(false));
        }

        if (lease is not null)
        {
            failure = CombineFailures(
                failure,
                await CaptureCleanupFailureAsync(lease.DisposeAsync)
                    .ConfigureAwait(false));
        }

        failure = CombineFailures(
            failure,
            CaptureCleanupFailure(generation.DisposeCancellation));
        generation.CompleteCleanup(failure);
        lock (gate)
        {
            if (ReferenceEquals(ownedGeneration, generation))
            {
                ownedGeneration = null;
            }
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private ValueTask CleanupTerminalGenerationAsync(
        ParticipantGeneration generation)
    {
        Exception? primaryFailure = generation.PreparationFailure;
        return primaryFailure is null
            ? CleanupGenerationAsync(generation, failClose: true)
            : CleanupGenerationAfterFailureAsync(
                generation,
                failClose: true,
                primaryFailure);
    }

    private ParticipantGeneration? DetachGeneration(DeviceId hostDeviceId)
    {
        lock (gate)
        {
            ParticipantGeneration? generation =
                active[0] ?? pendingResponseCleanup ?? ownedGeneration;
            if (generation?.Request.HostDeviceId != hostDeviceId)
            {
                return null;
            }

            active[0] = null;
            if (ReferenceEquals(pendingResponseCleanup, generation))
            {
                pendingResponseCleanup = null;
            }

            return generation;
        }
    }

    private void DetachGeneration(ParticipantGeneration generation)
    {
        lock (gate)
        {
            if (ReferenceEquals(active[0], generation))
            {
                active[0] = null;
            }
        }
    }

    private bool TryDeferResponseCleanup(
        ParticipantGeneration generation,
        Exception primaryFailure)
    {
        lock (gate)
        {
            if (disposed != 0
                || !ReferenceEquals(active[0], generation)
                || generation.Token.IsCancellationRequested
                || timeProvider.GetUtcNow() >= generation.Request.Deadline
                || pendingResponseCleanup is not null
                || generation.Lease is not { } lease
                || !lease.TryDeferFailCloseUntilPreparationDeadline(
                    generation.Request))
            {
                return false;
            }

            generation.RecordPreparationFailure(primaryFailure);
            pendingResponseCleanup = generation;
            return true;
        }
    }

    private string GetCancellationReason(RemoteWindowPreparationRequest request)
    {
        if (timeProvider.GetUtcNow() >= request.Deadline)
        {
            return "preparation_expired";
        }

        return Volatile.Read(ref disposed) != 0
            || lifetimeCancellation.IsCancellationRequested
                ? "participant_stopping"
                : "preparation_cancelled";
    }

    private void ValidateParticipantBinding(RemoteWindowPreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ParticipantDeviceId != ParticipantDeviceId)
        {
            throw new InvalidDataException(
                "The Remote Window preparation targets another participant Device.");
        }
    }

    private static void ValidateAdmissionBinding(
        RemoteWindowPreparationRequest request,
        RemoteWindowParticipantState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.CorrelationId != request.CorrelationId
            || state.SessionId != request.SessionId
            || state.ActivityId != request.ActivityId
            || state.HostDeviceId != request.HostDeviceId
            || state.ParticipantDeviceId != request.ParticipantDeviceId
            || state.Action is not RemoteWindowControlAction.Admission)
        {
            throw new InvalidDataException(
                "The Remote Window admission does not match its preparation binding.");
        }
    }

    private static RemoteWindowPreparationResponse Rejected(
        RemoteWindowPreparationRequest request,
        string reasonCode) =>
        RemoteWindowPreparationResponse.Create(
            request,
            RemoteWindowPreparationOutcome.Rejected,
            reasonCode);

    private static string? BoundPolicyRejection(string? reasonCode) =>
        reasonCode switch
        {
            null => null,
            "renderer_unavailable" or "role_unsupported" => reasonCode,
            _ => "renderer_unavailable",
        };

    private static Exception? CaptureCleanupFailure(Action cleanup)
    {
        try
        {
            cleanup();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async ValueTask<Exception?> CaptureCleanupFailureAsync(
        Func<ValueTask> cleanup)
    {
        try
        {
            await cleanup().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Exception? CombineFailures(
        Exception? first,
        Exception? second) => (first, second) switch
        {
            (null, null) => null,
            (not null, null) => first,
            (null, not null) => second,
            _ => new AggregateException(
                "Remote Window participant cleanup failed.",
                first!,
                second!),
        };

    private static bool HasActiveCallback(ParticipantGeneration generation) =>
        IsActiveFor(CurrentPreparer.Value, generation)
        || IsActiveFor(CurrentReceiver.Value, generation);

    private static bool IsActiveFor(
        ParticipantCallbackScope? scope,
        ParticipantGeneration generation)
    {
        for (ParticipantCallbackScope? current = scope;
            current is not null;
            current = current.Previous)
        {
            if (current.IsActive && ReferenceEquals(current.Generation, generation))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ParticipantCallbackScope(
        ParticipantGeneration generation,
        ParticipantCallbackScope? previous)
    {
        private int active = 1;

        public ParticipantGeneration Generation { get; } = generation;

        public ParticipantCallbackScope? Previous { get; } = previous;

        public bool IsActive => Volatile.Read(ref active) != 0;

        public void Exit() => Volatile.Write(ref active, 0);
    }

    private enum PreparationStage
    {
        AcquiringMedia,
        AttachingMedia,
        PreparingRenderer,
    }

    private sealed class ParticipantGeneration
    {
        private readonly CancellationTokenSource cancellation;
        private readonly TaskCompletionSource cleanupCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource preparationCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private RemoteWindowVideoFrameAssembler? assembler;
        private int cleanupStarted;
        private int connectionAttempted;
        private int admitted;
        private int ready;
        private Exception? preparationFailure;
        private Exception? receiveFailure;
        private IDisposable? revocationRegistration;

        public ParticipantGeneration(
            RemoteWindowPreparationRequest request,
            CancellationToken lifetimeCancellation,
            CancellationToken operationCancellation)
        {
            Request = request;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeCancellation,
                operationCancellation);
        }

        public Task CleanupCompletion => cleanupCompletion.Task;

        public bool ConnectionAttempted =>
            Volatile.Read(ref connectionAttempted) != 0;

        public bool IsCleanupStarted => Volatile.Read(ref cleanupStarted) != 0;

        public bool IsReady => Volatile.Read(ref ready) != 0;

        public AuthenticatedRemoteWindowConnectionLease? Lease { get; private set; }

        public Task? ReceiveTask { get; private set; }

        public Task PreparationCompletion => preparationCompletion.Task;

        public Exception? PreparationFailure => Volatile.Read(
            ref preparationFailure);

        public Exception? ReceiveFailure => Volatile.Read(ref receiveFailure);

        public IDesktopRemoteWindowParticipantRenderer? Renderer { get; private set; }

        public RemoteWindowPreparationRequest Request { get; }

        public CancellationToken Token => cancellation.Token;

        public void AttachAssembler(RemoteWindowVideoFrameAssembler value) =>
            assembler = value ?? throw new ArgumentNullException(nameof(value));

        public void AttachLease(AuthenticatedRemoteWindowConnectionLease value) =>
            Lease = value ?? throw new ArgumentNullException(nameof(value));

        public void AttachRenderer(
            IDesktopRemoteWindowParticipantRenderer value) =>
            Renderer = value ?? throw new ArgumentNullException(nameof(value));

        public void AttachRevocationRegistration(
            IDisposable registration)
        {
            ArgumentNullException.ThrowIfNull(registration);
            if (Interlocked.CompareExchange(
                    ref revocationRegistration,
                    registration,
                    null) is not null)
            {
                throw new InvalidOperationException(
                    "A Remote Window participant generation already owns a connection revocation registration.");
            }
        }

        public void Cancel() => cancellation.Cancel();

        public void CompleteCleanup(Exception? failure)
        {
            if (failure is null)
            {
                cleanupCompletion.TrySetResult();
            }
            else
            {
                cleanupCompletion.TrySetException(failure);
            }
        }

        public void CompletePreparation() => preparationCompletion.TrySetResult();

        public void DisposeAssembler() =>
            Interlocked.Exchange(ref assembler, null)?.Dispose();

        public void DisposeCancellation() => cancellation.Dispose();

        public async ValueTask DisposeRendererAsync()
        {
            IDesktopRemoteWindowParticipantRenderer? current = Renderer;
            Renderer = null;
            if (current is not null)
            {
                await current.DisposeAsync().ConfigureAwait(false);
            }
        }

        public void DisposeRevocationRegistration()
            => Interlocked.Exchange(ref revocationRegistration, null)?.Dispose();

        public void MarkAdmitted()
        {
            if (Interlocked.CompareExchange(ref admitted, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    "The Remote Window participant generation was already admitted.");
            }
        }

        public void MarkConnectionAttempted() =>
            Volatile.Write(ref connectionAttempted, 1);

        public void MarkReady() => Volatile.Write(ref ready, 1);

        public void RecordReceiveFailure(Exception exception) =>
            Interlocked.CompareExchange(ref receiveFailure, exception, null);

        public void RecordPreparationFailure(Exception exception) =>
            Interlocked.CompareExchange(ref preparationFailure, exception, null);

        public void SetReceiveTask(Task task) =>
            ReceiveTask = task ?? throw new ArgumentNullException(nameof(task));

        public bool TryBeginCleanup() =>
            Interlocked.CompareExchange(ref cleanupStarted, 1, 0) == 0;
    }
}
