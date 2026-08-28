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
    private static readonly AsyncLocal<ParticipantGeneration?> CurrentPreparer = new();
    private static readonly AsyncLocal<ParticipantGeneration?> CurrentReceiver = new();
    private readonly ParticipantGeneration?[] active = new ParticipantGeneration?[1];
    private readonly TaskCompletionSource disposalCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly IDesktopRemoteWindowReceivePolicy receivePolicy;
    private readonly IDesktopRemoteWindowParticipantRendererFactory rendererFactory;
    private readonly TimeProvider timeProvider;
    private readonly TryAcquireDesktopRemoteWindowPeerConnection tryAcquireConnection;
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
        try
        {
            policyRejection = receivePolicy.GetRejectionReason(request);
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
            if (disposed != 0)
            {
                return Rejected(request, "participant_stopping");
            }

            generation = new ParticipantGeneration(
                request,
                lifetimeCancellation.Token,
                cancellationToken);
            if (active[0] is not null)
            {
                generation.DisposeCancellation();
                return Rejected(request, "participant_busy");
            }

            active[0] = generation;
        }

        ParticipantGeneration? previousPreparer = CurrentPreparer.Value;
        CurrentPreparer.Value = generation;
        PreparationStage stage = PreparationStage.AcquiringMedia;
        try
        {
            bool acquired = tryAcquireConnection(
                request.HostDeviceId,
                out AuthenticatedRemoteWindowConnectionLease? lease);
            if (lease is not null)
            {
                generation.AttachLease(lease);
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
            await lease.ConnectInitiatorAsync(request, generation.Token)
                .ConfigureAwait(false);

            stage = PreparationStage.PreparingRenderer;
            IDesktopRemoteWindowParticipantRenderer? renderer =
                await rendererFactory.PrepareAsync(request, generation.Token)
                    .ConfigureAwait(false);
            if (renderer is null)
            {
                await CleanupGenerationAsync(
                    generation,
                    failClose: true).ConfigureAwait(false);
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
            || cancellationToken.IsCancellationRequested)
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
            await CleanupGenerationAfterFailureAsync(
                generation,
                generation.ConnectionAttempted,
                exception).ConfigureAwait(false);
            string reason = stage is PreparationStage.PreparingRenderer
                ? "renderer_start_failed"
                : stage is PreparationStage.AttachingMedia
                    ? "media_attachment_failed"
                    : "media_unavailable";
            return Rejected(request, reason);
        }
        finally
        {
            generation.CompletePreparation();
            CurrentPreparer.Value = previousPreparer;
        }
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
            await CleanupGenerationAsync(generation, failClose: true)
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

    private async Task RunReceiveLoopAsync(
        ParticipantGeneration generation,
        Task receiveStart)
    {
        await receiveStart.ConfigureAwait(false);
        ParticipantGeneration? previous = CurrentReceiver.Value;
        CurrentReceiver.Value = generation;
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
            CurrentReceiver.Value = previous;
        }
    }

    private async Task DisposeCoreAsync()
    {
        Exception? failure = CaptureCleanupFailure(lifetimeCancellation.Cancel);
        ParticipantGeneration? generation;
        lock (gate)
        {
            generation = active[0];
            active[0] = null;
        }

        if (generation is not null)
        {
            try
            {
                await CleanupGenerationAsync(generation, failClose: true)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }
        }

        failure = CombineFailures(
            failure,
            CaptureCleanupFailure(lifetimeCancellation.Dispose));

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
            if (ReferenceEquals(CurrentPreparer.Value, generation)
                || ReferenceEquals(CurrentReceiver.Value, generation))
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

        if (!ReferenceEquals(CurrentPreparer.Value, generation))
        {
            await generation.PreparationCompletion.ConfigureAwait(false);
        }

        Task? receiveTask = generation.ReceiveTask;
        if (receiveTask is not null
            && !ReferenceEquals(CurrentReceiver.Value, generation))
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
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private ParticipantGeneration? DetachGeneration(DeviceId hostDeviceId)
    {
        lock (gate)
        {
            ParticipantGeneration? generation = active[0];
            if (generation?.Request.HostDeviceId != hostDeviceId)
            {
                return null;
            }

            active[0] = null;
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
        private Exception? receiveFailure;
        private CancellationTokenRegistration revocationRegistration;
        private int revocationRegistrationAttached;

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
            CancellationTokenRegistration registration)
        {
            revocationRegistration = registration;
            Volatile.Write(ref revocationRegistrationAttached, 1);
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
        {
            if (Interlocked.Exchange(ref revocationRegistrationAttached, 0) != 0)
            {
                revocationRegistration.Dispose();
            }
        }

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

        public void SetReceiveTask(Task task) =>
            ReceiveTask = task ?? throw new ArgumentNullException(nameof(task));

        public bool TryBeginCleanup() =>
            Interlocked.CompareExchange(ref cleanupStarted, 1, 0) == 0;
    }
}
