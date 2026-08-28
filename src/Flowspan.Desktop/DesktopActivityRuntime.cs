using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Application.Adapters;
using Flowspan.Domain;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop;

internal sealed record DesktopActivityNetworkBindings(
    AuthenticatedActivitySessionHandler SessionHandler,
    AuthenticatedRemoteWindowMediaSessionDirectory RemoteWindowMediaSessions);

internal interface IDesktopRemoteWindowMediaSessionOwner : IAsyncDisposable
{
    public AuthenticatedRemoteWindowMediaSessionDirectory SessionDirectory { get; }
}

internal sealed class DesktopActivityRuntime :
    IDesktopActivityService,
    IDesktopSceneApplyService
{
    private static readonly TimeSpan OperationLifetime = TimeSpan.FromSeconds(30);
    private readonly TaskCompletionSource disposalCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<CancellationToken, ValueTask<DeviceIdentity>> getIdentity;
    private readonly Func<CancellationToken, ValueTask<TrustSessionCoordinator>> getTrust;
    private readonly Func<TimeProvider, IDesktopRemoteWindowMediaSessionOwner>
        createRemoteWindowMediaSessions;
    private readonly Action<AuthenticatedActivitySessionHandler>?
        onSessionHandlerCreated;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly IReplaceStatePayloadStore? replaceStatePayloadStore;
    private readonly IReceiptSink receiptSink;
    private readonly ISceneRemoteChildStatePayloadStore?
        sceneRemoteChildStatePayloadStore;
    private readonly ISceneApplyStatePayloadStore? sceneApplyStatePayloadStore;
    private readonly TimeProvider timeProvider;
    private InMemoryActivityCatalog? catalog;
    private AuthenticatedActivitySessionHandler? handler;
    private FlowspanNode? node;
    private ReplaceEndpoint? replaceEndpoint;
    private PersistentReplaceStateStore? replaceState;
    private PersistentSceneRemoteChildJournal? sceneRemoteChildJournal;
    private PersistentSceneApplyJournal? sceneApplyJournal;
    private SceneApplyPlanner? sceneApplyPlanner;
    private SceneApplyCoordinator? sceneApplyCoordinator;
    private SceneApplyCompensator? sceneApplyCompensator;
    private IDesktopRemoteWindowMediaSessionOwner?
        remoteWindowMediaSessions;
    private TrustSessionCoordinator? trust;
    private int disposed;

    public DesktopActivityRuntime(
        Func<CancellationToken, ValueTask<DeviceIdentity>> getIdentity,
        Func<CancellationToken, ValueTask<TrustSessionCoordinator>> getTrust,
        TimeProvider? timeProvider = null,
        IReplaceStatePayloadStore? replaceStatePayloadStore = null,
        ISceneRemoteChildStatePayloadStore?
            sceneRemoteChildStatePayloadStore = null,
        ISceneApplyStatePayloadStore? sceneApplyStatePayloadStore = null,
        IReceiptSink? receiptSink = null)
        : this(
            getIdentity,
            getTrust,
            timeProvider,
            replaceStatePayloadStore,
            sceneRemoteChildStatePayloadStore,
            sceneApplyStatePayloadStore,
            receiptSink,
            static provider => new DesktopRemoteWindowMediaSessionOwner(provider),
            onSessionHandlerCreated: null)
    {
    }

    internal DesktopActivityRuntime(
        Func<CancellationToken, ValueTask<DeviceIdentity>> getIdentity,
        Func<CancellationToken, ValueTask<TrustSessionCoordinator>> getTrust,
        TimeProvider? timeProvider,
        IReplaceStatePayloadStore? replaceStatePayloadStore,
        ISceneRemoteChildStatePayloadStore?
            sceneRemoteChildStatePayloadStore,
        ISceneApplyStatePayloadStore? sceneApplyStatePayloadStore,
        IReceiptSink? receiptSink,
        Func<TimeProvider, IDesktopRemoteWindowMediaSessionOwner>
            createRemoteWindowMediaSessions,
        Action<AuthenticatedActivitySessionHandler>? onSessionHandlerCreated = null)
    {
        ArgumentNullException.ThrowIfNull(getIdentity);
        ArgumentNullException.ThrowIfNull(getTrust);
        ArgumentNullException.ThrowIfNull(createRemoteWindowMediaSessions);
        this.getIdentity = getIdentity;
        this.getTrust = getTrust;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.createRemoteWindowMediaSessions = createRemoteWindowMediaSessions;
        this.onSessionHandlerCreated = onSessionHandlerCreated;
        this.replaceStatePayloadStore = replaceStatePayloadStore;
        this.sceneRemoteChildStatePayloadStore =
            sceneRemoteChildStatePayloadStore;
        this.sceneApplyStatePayloadStore = sceneApplyStatePayloadStore;
        this.receiptSink = receiptSink ?? NullReceiptSink.Instance;
    }

    public event Action? Changed;

    public bool IsReady => Volatile.Read(ref node) is not null;

    public bool SupportsSemanticResume(string activityKind) =>
        string.Equals(
            activityKind,
            WorkspaceNoteKind.Value,
            StringComparison.Ordinal);

    public bool IsDestructiveReplaceAvailable
    {
        get
        {
            if (Volatile.Read(ref disposed) != 0
                || node is null
                || replaceEndpoint is null
                || handler?.IsReplaceEndpointAvailable != true)
            {
                return false;
            }

            DesktopReplaceRecoveryResult recovery = GetReplaceRecoveryState();
            return recovery.IsAvailable
                && !recovery.Records.Any(static record => record.IsRecoveryRequired);
        }
    }

    public bool IsSceneApplyReady =>
        Volatile.Read(ref disposed) == 0
        && Volatile.Read(ref sceneApplyPlanner) is not null
        && Volatile.Read(ref sceneApplyCoordinator) is not null
        && Volatile.Read(ref sceneApplyCompensator) is not null;

    public async ValueTask<SceneApplyPreview> PreviewSceneAsync(
        ScenePlan scene,
        IEnumerable<SceneSourceSelection> selectedSources,
        long? observedGroupRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(selectedSources);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        SceneApplyPlanner current = sceneApplyPlanner
            ?? throw new InvalidOperationException(
                "The protected Scene Apply runtime is unavailable.");
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        return await current.PreviewAsync(
            scene,
            selectedSources,
            observedGroupRevision,
            linked.Token).ConfigureAwait(false);
    }

    public async ValueTask<SceneApplyExecutionResult> ApplySceneAsync(
        ScenePlan scene,
        SceneApplyPreview preview,
        SceneApplyApproval approval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(approval);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        SceneApplyCoordinator current = sceneApplyCoordinator
            ?? throw new InvalidOperationException(
                "The protected Scene Apply runtime is unavailable.");
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        try
        {
            return await current.ApplyAsync(
                scene,
                preview,
                approval,
                linked.Token).ConfigureAwait(false);
        }
        finally
        {
            PublishChanged();
        }
    }

    public async ValueTask<SceneCompensationResult> CompensateSceneAsync(
        SceneApplyResult applyResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applyResult);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        SceneApplyCompensator current = sceneApplyCompensator
            ?? throw new InvalidOperationException(
                "The protected Scene compensation runtime is unavailable.");
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        try
        {
            return await current.CompensateAsync(
                applyResult,
                linked.Token).ConfigureAwait(false);
        }
        finally
        {
            PublishChanged();
        }
    }

    public DesktopActivitySnapshot CreateWorkspaceNote(
        string title,
        string text,
        ActivitySensitivity sensitivity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!Enum.IsDefined(sensitivity))
        {
            throw new ArgumentOutOfRangeException(nameof(sensitivity));
        }

        if (text.Length is < 1 or > WorkspaceNoteAdapter.MaximumTextCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"A portable note must contain 1 to {WorkspaceNoteAdapter.MaximumTextCharacters} characters.");
        }

        FlowspanNode currentNode = node
            ?? throw new InvalidOperationException(
                "The Activity runtime is not initialized.");
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.From(Guid.NewGuid()),
            WorkspaceNoteKind,
            currentNode.DeviceId,
            title,
            JsonSerializer.Serialize(new { text }),
            sensitivity);
        var activity = ActivityInstance.Active(
            descriptor,
            ActivityPlacement.On(currentNode.DeviceId, "desktop"));
        if (!currentNode.AddLocalActivity(activity))
        {
            throw new InvalidOperationException(
                "The generated Activity ID already exists.");
        }

        DesktopActivitySnapshot snapshot = CreateSnapshot(activity);
        PublishChanged();
        return snapshot;
    }

    public ImmutableArray<DesktopActivitySnapshot> GetActivities()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return catalog?.Snapshot()
            .Where(static activity => activity.Lifecycle == ActivityLifecycle.Active)
            .Select(CreateSnapshot)
            .ToImmutableArray() ?? [];
    }

    public ImmutableArray<DesktopActivityTargetSnapshot> GetTargets()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        AuthenticatedActivitySessionHandler? currentHandler = handler;
        TrustSessionCoordinator? currentTrust = trust;
        if (currentHandler is null || currentTrust is null)
        {
            return [];
        }

        var targets = ImmutableArray.CreateBuilder<DesktopActivityTargetSnapshot>();
        foreach (DeviceId peerDeviceId in currentHandler.GetConnectedPeers())
        {
            if (currentTrust.TryGetCurrentTrust(
                    peerDeviceId,
                    out TrustRecord? record)
                && record.GrantedCapabilities.Allows(Capability.ActivityReceive))
            {
                targets.Add(new DesktopActivityTargetSnapshot(
                    peerDeviceId,
                    record.PeerIdentity.DisplayName));
            }
        }

        return targets.ToImmutable();
    }

    public ImmutableArray<DesktopActivityTargetSnapshot> GetRemoteWindowTargets(
        MirrorParticipantRole role)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (role is not MirrorParticipantRole.ViewOnly
            and not MirrorParticipantRole.DriverEligible)
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        AuthenticatedActivitySessionHandler? currentHandler = handler;
        TrustSessionCoordinator? currentTrust = trust;
        if (currentHandler is null || currentTrust is null)
        {
            return [];
        }

        var targets = ImmutableArray.CreateBuilder<DesktopActivityTargetSnapshot>();
        foreach (DeviceId peerDeviceId in currentHandler.GetConnectedPeers())
        {
            if (currentHandler.TryGetRemoteWindowChannel(peerDeviceId, out _)
                && currentTrust.TryGetCurrentTrust(
                    peerDeviceId,
                    out TrustRecord? record)
                && record.GrantedCapabilities.Allows(Capability.MirrorView)
                && (role == MirrorParticipantRole.ViewOnly
                    || record.GrantedCapabilities.Allows(Capability.MirrorDrive)))
            {
                targets.Add(new DesktopActivityTargetSnapshot(
                    peerDeviceId,
                    record.PeerIdentity.DisplayName));
            }
        }

        return targets.ToImmutable();
    }

    internal bool TryGetRemoteWindowChannel(
        DeviceId peerDeviceId,
        out IRemoteWindowControlChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        AuthenticatedActivitySessionHandler? current = handler;
        if (Volatile.Read(ref disposed) == 0
            && current is not null)
        {
            return current.TryGetRemoteWindowChannel(peerDeviceId, out channel);
        }

        channel = null;
        return false;
    }

    public DesktopReplaceRecoveryResult GetReplaceRecoveryState()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        PersistentReplaceStateStore? current = replaceState;
        if (current is null)
        {
            return DesktopReplaceRecoveryResult.Unavailable;
        }

        try
        {
            FlowspanNode? currentNode = node;
            InMemoryActivityCatalog? currentCatalog = catalog;
            if (currentNode is null || currentCatalog is null)
            {
                return DesktopReplaceRecoveryResult.Unavailable;
            }

            ReplaceRecoverySnapshot snapshot =
                current.GetRecoverySnapshot(timeProvider.GetUtcNow());
            HashSet<UndoCapsuleId> availableCapsules = snapshot.Records
                .Where(static record =>
                    record.Kind == ReplaceRecoveryOperationKind.Replace
                    && record.UndoAvailability == ReplaceUndoAvailability.Available
                    && record.CapsuleId is not null)
                .Select(static record => record.CapsuleId!)
                .ToHashSet();
            ReplaceRestartRecoveryPlan plan = current.GetRestartRecoveryPlan(
                currentNode.DeviceId);
            ImmutableArray<UndoCapsuleId> undoable = plan.UndoCandidates
                .Where(candidate => availableCapsules.Contains(candidate.CapsuleId))
                .Where(candidate => currentCatalog.TryGet(
                    candidate.ExactReplacement.Descriptor.Id,
                    out ActivityInstance? exact)
                    && exact == candidate.ExactReplacement)
                .Select(static candidate => candidate.CapsuleId)
                .OrderBy(static capsuleId => capsuleId.ToString(), StringComparer.Ordinal)
                .ToImmutableArray();
            return DesktopReplaceRecoveryResult.Available(
                snapshot,
                undoable);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return DesktopReplaceRecoveryResult.Unavailable;
        }
    }

    public async ValueTask<OperationReceipt> HandoffAsync(
        ActivityId activityId,
        DeviceId targetDeviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        OutboundOperationPreparation preparation = PrepareOutboundOperation(
            activityId,
            targetDeviceId,
            OperationKind.Handoff);
        if (preparation.FailureReceipt is not null)
        {
            receiptSink.Write(preparation.FailureReceipt);
            return preparation.FailureReceipt;
        }

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        return await preparation.Node.HandoffAsync(
            activityId,
            preparation.Channel!,
            "desktop",
            preparation.Context,
            linked.Token).ConfigureAwait(false);
    }

    public async ValueTask<UndoReplaceResult> UndoReplaceAsync(
        UndoCapsuleId capsuleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capsuleId);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        OperationContext context = OperationContext.Create(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()),
            timeProvider.GetUtcNow().Add(OperationLifetime));
        ReplaceEndpoint? current = replaceEndpoint;
        if (current is null || !CanAttemptTargetLocalUndo(capsuleId))
        {
            return UndoReplaceResult.Failed(
                context,
                capsuleId,
                FailureCode.UndoUnavailable,
                timeProvider.GetUtcNow());
        }

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        try
        {
            return await current.UndoReplaceAsync(
                capsuleId,
                context,
                linked.Token).ConfigureAwait(false);
        }
        finally
        {
            PublishChanged();
        }
    }

    private bool CanAttemptTargetLocalUndo(UndoCapsuleId capsuleId)
    {
        PersistentReplaceStateStore? currentState = replaceState;
        InMemoryActivityCatalog? currentCatalog = catalog;
        FlowspanNode? currentNode = node;
        if (currentState is null || currentCatalog is null || currentNode is null)
        {
            return false;
        }

        try
        {
            DesktopReplaceRecoveryResult recovery = GetReplaceRecoveryState();
            if (!recovery.IsAvailable)
            {
                return false;
            }

            if (recovery.UndoableCapsuleIds.Contains(capsuleId))
            {
                return true;
            }

            ReplaceRestartRecoveryPlan plan = currentState.GetRestartRecoveryPlan(
                currentNode.DeviceId);
            if (plan.IsBlockedByUnresolvedOperation
                || !currentState.TryGet(capsuleId, out UndoCapsule? capsule)
                || capsule is null)
            {
                return false;
            }

            if (capsule.ExpiresAt <= timeProvider.GetUtcNow())
            {
                return true;
            }

            return !currentCatalog.TryGet(
                    capsule.ReplacementActivity.Descriptor.Id,
                    out ActivityInstance? exact)
                || exact != capsule.ReplacementActivity;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    public async ValueTask<OperationReceipt> MoveAsync(
        ActivityId activityId,
        DeviceId targetDeviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        OutboundOperationPreparation preparation = PrepareOutboundOperation(
            activityId,
            targetDeviceId,
            OperationKind.Move);
        if (preparation.FailureReceipt is not null)
        {
            receiptSink.Write(preparation.FailureReceipt);
            return preparation.FailureReceipt;
        }

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        OperationReceipt receipt = await preparation.Node.MoveAsync(
            activityId,
            preparation.Channel!,
            "desktop",
            preparation.Context,
            linked.Token).ConfigureAwait(false);
        if (receipt.Status == OperationStatus.Committed)
        {
            PublishChanged();
        }

        return receipt;
    }

    public async ValueTask<DesktopReplaceTargetInventoryResult> GetReplaceTargetsAsync(
        ActivityId incomingActivityId,
        DeviceId targetDeviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incomingActivityId);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        FlowspanNode currentNode = node
            ?? throw new InvalidOperationException(
                "The Activity runtime is not initialized.");
        if (!currentNode.TryGetActivity(
                incomingActivityId,
                out ActivityInstance? incoming)
            || incoming.Lifecycle != ActivityLifecycle.Active)
        {
            return DesktopReplaceTargetInventoryResult.Failed(
                FailureCode.ActivityNotFound);
        }

        TrustSessionCoordinator? currentTrust = trust;
        if (currentTrust is null
            || !currentTrust.TryGetCurrentTrust(
                targetDeviceId,
                out TrustRecord? record)
            || !record.GrantedCapabilities.Allows(Capability.ActivityReceive))
        {
            return DesktopReplaceTargetInventoryResult.Failed(
                FailureCode.CapabilityDenied);
        }

        if (handler is null
            || !handler.TryGetReplaceInventoryChannel(
                targetDeviceId,
                out IReplaceTargetInventoryChannel? channel)
            || channel is null)
        {
            return DesktopReplaceTargetInventoryResult.Failed(
                FailureCode.PeerUnavailable);
        }

        var query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.From(Guid.NewGuid()),
            targetDeviceId,
            incoming.Descriptor.Kind,
            timeProvider.GetUtcNow().Add(OperationLifetime));
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        ReplaceTargetInventoryDeliveryResult delivered = await channel.QueryAsync(
            currentNode.DeviceId,
            query,
            linked.Token).ConfigureAwait(false);
        if (delivered.Status != ActivityDeliveryStatus.Acknowledged
            || delivered.Result is not ReplaceTargetInventoryResult result)
        {
            return DesktopReplaceTargetInventoryResult.Failed(
                delivered.Status == ActivityDeliveryStatus.AcknowledgementLost
                    ? FailureCode.AcknowledgementLost
                    : FailureCode.PeerUnavailable);
        }

        if (!result.IsSuccess)
        {
            return DesktopReplaceTargetInventoryResult.Failed(result.FailureCode);
        }

        return new DesktopReplaceTargetInventoryResult(
            FailureCode.None,
            result.IsTruncated,
            result.CapturedAt,
            result.Targets
                .Select(target => new DesktopReplaceTargetSnapshot(
                    targetDeviceId,
                    target.ActivityId,
                    target.Title,
                    target.Kind.Value,
                    target.Revision,
                    target.DescriptorDigest,
                    target.PlacementSlot))
                .ToImmutableArray());
    }

    public async ValueTask<DesktopReplaceOperationResult> ReplaceAsync(
        ActivityId incomingActivityId,
        DesktopReplaceTargetSnapshot selectedTarget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incomingActivityId);
        ArgumentNullException.ThrowIfNull(selectedTarget);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        FlowspanNode currentNode = node
            ?? throw new InvalidOperationException(
                "The Activity runtime is not initialized.");
        if (!IsDestructiveReplaceAvailable)
        {
            return DesktopReplaceOperationResult.NotDelivered(
                FailureCode.UndoUnavailable,
                timeProvider.GetUtcNow());
        }

        if (!currentNode.TryGetActivity(
                incomingActivityId,
                out ActivityInstance? incoming)
            || incoming.Lifecycle != ActivityLifecycle.Active)
        {
            return DesktopReplaceOperationResult.NotDelivered(
                FailureCode.ActivityNotFound,
                timeProvider.GetUtcNow());
        }

        DesktopReplaceTargetInventoryResult refreshed;
        try
        {
            refreshed = await GetReplaceTargetsAsync(
                incomingActivityId,
                selectedTarget.DeviceId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return DesktopReplaceOperationResult.NotDelivered(
                FailureCode.PeerUnavailable,
                timeProvider.GetUtcNow());
        }
        if (!refreshed.IsSuccess)
        {
            return DesktopReplaceOperationResult.NotDelivered(
                refreshed.FailureCode,
                timeProvider.GetUtcNow());
        }

        DesktopReplaceTargetSnapshot? exact = refreshed.Targets.SingleOrDefault(
            target => target.ActivityId == selectedTarget.ActivityId);
        if (exact is null || !IsExactReplaceTarget(exact, selectedTarget))
        {
            return DesktopReplaceOperationResult.NotDelivered(
                FailureCode.RevisionConflict,
                timeProvider.GetUtcNow());
        }

        if (!currentNode.TryGetActivity(incomingActivityId, out ActivityInstance? live)
            || live != incoming
            || live.Lifecycle != ActivityLifecycle.Active)
        {
            return DesktopReplaceOperationResult.NotDelivered(
                FailureCode.RevisionConflict,
                timeProvider.GetUtcNow());
        }

        TrustSessionCoordinator? currentTrust = trust;
        if (currentTrust is null
            || !currentTrust.TryGetCurrentTrust(
                selectedTarget.DeviceId,
                out TrustRecord? record)
            || !record.GrantedCapabilities.Allows(Capability.ActivityReceive))
        {
            return DesktopReplaceOperationResult.NotDelivered(
                FailureCode.CapabilityDenied,
                timeProvider.GetUtcNow());
        }

        if (!IsDestructiveReplaceAvailable)
        {
            return DesktopReplaceOperationResult.NotDelivered(
                FailureCode.UndoUnavailable,
                timeProvider.GetUtcNow());
        }

        if (handler is null
            || !handler.TryGetReplaceChannel(
                selectedTarget.DeviceId,
                out IReplaceChannel? channel)
            || channel is null)
        {
            return DesktopReplaceOperationResult.NotDelivered(
                FailureCode.PeerUnavailable,
                timeProvider.GetUtcNow());
        }

        DateTimeOffset sendingAt = timeProvider.GetUtcNow();
        OperationContext context = OperationContext.Create(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()),
            sendingAt.Add(OperationLifetime));
        ReplaceActivityCommand command = ReplaceActivityCommand.Create(
            context,
            exact.ActivityId,
            exact.Revision,
            exact.DescriptorDigest,
            live.Descriptor,
            ActivityPlacement.On(exact.DeviceId, exact.PlacementSlot),
            sendingAt.Add(ReplaceEndpoint.MaximumUndoRetention));
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        ReplaceDeliveryResult delivered;
        try
        {
            delivered = await channel.SendAsync(
                currentNode.DeviceId,
                command,
                linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return DesktopReplaceOperationResult.AcknowledgementLost(
                context,
                timeProvider.GetUtcNow());
        }
        DesktopReplaceOperationResult result = delivered.Status switch
        {
            ActivityDeliveryStatus.Acknowledged
                when delivered.Result is not null =>
                DesktopReplaceOperationResult.Acknowledged(delivered.Result),
            ActivityDeliveryStatus.AcknowledgementLost =>
                DesktopReplaceOperationResult.AcknowledgementLost(
                    context,
                    timeProvider.GetUtcNow()),
            _ => DesktopReplaceOperationResult.NotDelivered(
                context,
                FailureCode.PeerUnavailable,
                timeProvider.GetUtcNow()),
        };
        if (result.Receipt is not null)
        {
            receiptSink.Write(result.Receipt);
        }

        return result;
    }

    public async ValueTask InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        await initializationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (node is not null)
            {
                return;
            }

            DeviceIdentity identity = await getIdentity(linked.Token)
                .ConfigureAwait(false);
            TrustSessionCoordinator coordinator = await getTrust(linked.Token)
                .ConfigureAwait(false);
            var newCatalog = new InMemoryActivityCatalog();
            var workspaceNoteAdapter = new WorkspaceNoteAdapter();
            var adapterRegistry = new ActivityAdapterRegistry(
                [workspaceNoteAdapter]);
            var newNode = new FlowspanNode(
                identity.DeviceId,
                identity.DisplayName,
                new TimeProviderClock(timeProvider),
                newCatalog,
                new InMemoryOperationJournal(),
                adapterRegistry,
                receiptSink);
            var authorizedPeer = new TrustBoundActivityPeer(
                newNode,
                coordinator,
                PublishChanged);
            var inventoryEndpoint = new ReplaceTargetInventoryEndpoint(
                identity.DeviceId,
                new TimeProviderClock(timeProvider),
                newCatalog,
                adapterRegistry);
            var authorizedInventoryPeer = new TrustBoundReplaceInventoryPeer(
                inventoryEndpoint,
                coordinator);
            PersistentReplaceStateStore? newReplaceState = null;
            ReplaceEndpoint? newReplaceEndpoint = null;
            if (replaceStatePayloadStore is not null)
            {
                try
                {
                    newReplaceState = await PersistentReplaceStateStore.OpenAsync(
                        replaceStatePayloadStore,
                        linked.Token).ConfigureAwait(false);
                    ReplaceRestartRecoveryPlan recoveryPlan =
                        newReplaceState.GetRestartRecoveryPlan(identity.DeviceId);
                    foreach (ActivityInstance activity in recoveryPlan.CurrentActivities)
                    {
                        if (activity.Descriptor.Kind != workspaceNoteAdapter.Kind)
                        {
                            throw new InvalidDataException(
                                "The protected Replace state contains an unsupported restart Activity kind.");
                        }

                        ResumeActivityResult resumed = await workspaceNoteAdapter
                            .ResumeAsync(
                                activity.Descriptor,
                                activity.Placement,
                                linked.Token)
                            .ConfigureAwait(false);
                        if (!resumed.Succeeded || !newCatalog.TryAdd(activity))
                        {
                            throw new InvalidDataException(
                                "The protected Replace state could not reconstruct an exact semantic Activity frontier.");
                        }
                    }

                    newReplaceEndpoint = new ReplaceEndpoint(
                        identity.DeviceId,
                        new TimeProviderClock(timeProvider),
                        newCatalog,
                        newReplaceState,
                        adapterRegistry,
                        newReplaceState,
                        new CryptographicUndoCapsuleIdSource(),
                        receiptSink);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    newReplaceEndpoint?.Dispose();
                    newReplaceState?.Dispose();
                    newReplaceEndpoint = null;
                    newReplaceState = null;
                }
            }

            PersistentSceneRemoteChildJournal? newSceneRemoteChildJournal = null;
            PersistentSceneApplyJournal? newSceneApplyJournal = null;
            SceneApplyPreflightEndpoint? scenePreflight = null;
            SceneActivityOperationEndpoint? sceneEndpoint = null;
            ISceneControlPeer? authorizedScenePeer = null;
            var sceneRoutes = new DeferredSceneOperationRouteDirectory();
            if (sceneRemoteChildStatePayloadStore is not null)
            {
                try
                {
                    newSceneRemoteChildJournal =
                        await PersistentSceneRemoteChildJournal.OpenAsync(
                            sceneRemoteChildStatePayloadStore,
                            linked.Token).ConfigureAwait(false);
                    scenePreflight = new SceneApplyPreflightEndpoint(
                        identity.DeviceId,
                        new TimeProviderClock(timeProvider),
                        newCatalog,
                        adapterRegistry,
                        new DesktopSceneReplaceUndoAvailability(
                            identity.DeviceId,
                            newReplaceState));
                    sceneEndpoint = new SceneActivityOperationEndpoint(
                        newNode,
                        scenePreflight,
                        newReplaceEndpoint,
                        new TimeProviderClock(timeProvider));
                    var scenePeer = new SceneControlPeer(
                        new TimeProviderClock(timeProvider),
                        sceneEndpoint,
                        new RoutedSceneActivityOperationPort(
                            new TimeProviderClock(timeProvider),
                            sceneEndpoint,
                            sceneRoutes),
                        newSceneRemoteChildJournal);
                    authorizedScenePeer = new TrustBoundSceneControlPeer(
                        scenePeer,
                        sceneEndpoint,
                        coordinator);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    newSceneRemoteChildJournal?.Dispose();
                    newSceneRemoteChildJournal = null;
                    authorizedScenePeer = null;
                    sceneEndpoint = null;
                    scenePreflight = null;
                }
            }

            if (sceneApplyStatePayloadStore is not null)
            {
                try
                {
                    newSceneApplyJournal =
                        await PersistentSceneApplyJournal.OpenAsync(
                            sceneApplyStatePayloadStore,
                            linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    newSceneApplyJournal?.Dispose();
                    newSceneApplyJournal = null;
                }
            }

            AuthenticatedActivitySessionHandler? newHandler = null;
            IDesktopRemoteWindowMediaSessionOwner? newRemoteWindowMediaSessions = null;
            SceneApplyPlanner? newSceneApplyPlanner = null;
            SceneApplyCoordinator? newSceneApplyCoordinator = null;
            SceneApplyCompensator? newSceneApplyCompensator = null;
            try
            {
                newRemoteWindowMediaSessions =
                    createRemoteWindowMediaSessions(timeProvider)
                    ?? throw new InvalidOperationException(
                        "The Remote Window media session owner factory returned null.");
                IReplacePeer? authorizedReplacePeer = newReplaceEndpoint is null
                    || newReplaceState is null
                    ? null
                    : new TrustBoundReplacePeer(
                        newReplaceEndpoint,
                        newReplaceState,
                        coordinator,
                        PublishChanged,
                        timeProvider);
                newHandler = new AuthenticatedActivitySessionHandler(
                    authorizedPeer,
                    authorizedReplacePeer,
                    authorizedInventoryPeer,
                    swapPeer: null,
                    timeProvider,
                    authorizedScenePeer,
                    remoteWindowMediaSessions:
                        newRemoteWindowMediaSessions.SessionDirectory);
                onSessionHandlerCreated?.Invoke(newHandler);
                if (authorizedScenePeer is not null)
                {
                    sceneRoutes.SetInner(newHandler);
                }

                if (authorizedScenePeer is not null
                    && scenePreflight is not null
                    && sceneEndpoint is not null
                    && newSceneApplyJournal is not null)
                {
                    var clock = new TimeProviderClock(timeProvider);
                    var localOperationPort = new RoutedSceneActivityOperationPort(
                        clock,
                        identity.DeviceId,
                        sceneEndpoint,
                        newHandler);
                    var operationPort = new CoordinatorSceneActivityOperationPort(
                        clock,
                        identity.DeviceId,
                        localOperationPort,
                        newHandler);
                    newSceneApplyPlanner = new SceneApplyPlanner(
                        clock,
                        new RoutedSceneApplyPreflightPort(
                            identity.DeviceId,
                            scenePreflight,
                            newHandler));
                    newSceneApplyCoordinator = new SceneApplyCoordinator(
                        clock,
                        newSceneApplyJournal,
                        operationPort);
                    newSceneApplyCompensator = new SceneApplyCompensator(
                        clock,
                        operationPort);
                }
            }
            catch (Exception initializationFailure)
            {
                var failures = new List<Exception> { initializationFailure };
                if (newHandler is not null)
                {
                    await CaptureCleanupFailureAsync(
                        newHandler.DisposeAsync,
                        failures).ConfigureAwait(false);
                }

                if (newRemoteWindowMediaSessions is not null)
                {
                    await CaptureCleanupFailureAsync(
                        newRemoteWindowMediaSessions.DisposeAsync,
                        failures).ConfigureAwait(false);
                }

                if (newSceneApplyJournal is not null)
                {
                    CaptureCleanupFailure(newSceneApplyJournal.Dispose, failures);
                }

                if (newSceneRemoteChildJournal is not null)
                {
                    CaptureCleanupFailure(
                        newSceneRemoteChildJournal.Dispose,
                        failures);
                }

                if (newReplaceEndpoint is not null)
                {
                    CaptureCleanupFailure(newReplaceEndpoint.Dispose, failures);
                }

                if (newReplaceState is not null)
                {
                    CaptureCleanupFailure(newReplaceState.Dispose, failures);
                }

                if (failures.Count > 1)
                {
                    throw new AggregateException(
                        "Desktop Activity runtime initialization and cleanup failed.",
                        failures);
                }

                ExceptionDispatchInfo.Capture(initializationFailure).Throw();
                throw;
            }
            AuthenticatedActivitySessionHandler initializedHandler = newHandler
                ?? throw new InvalidOperationException(
                    "The Activity session handler was not constructed.");
            initializedHandler.Changed += OnHandlerChanged;
            coordinator.Changed += OnTrustChanged;
            catalog = newCatalog;
            trust = coordinator;
            handler = initializedHandler;
            remoteWindowMediaSessions = newRemoteWindowMediaSessions;
            replaceEndpoint = newReplaceEndpoint;
            replaceState = newReplaceState;
            sceneRemoteChildJournal = newSceneRemoteChildJournal;
            sceneApplyJournal = newSceneApplyJournal;
            sceneApplyPlanner = newSceneApplyPlanner;
            sceneApplyCoordinator = newSceneApplyCoordinator;
            sceneApplyCompensator = newSceneApplyCompensator;
            Volatile.Write(ref node, newNode);
        }
        finally
        {
            initializationGate.Release();
        }

        PublishChanged();
    }

    internal async ValueTask<AuthenticatedActivitySessionHandler>
        GetSessionHandlerAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        return handler
            ?? throw new InvalidOperationException(
                "The Activity session handler was not initialized.");
    }

    internal async ValueTask<DesktopActivityNetworkBindings>
        GetNetworkBindingsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        AuthenticatedActivitySessionHandler currentHandler = handler
            ?? throw new InvalidOperationException(
                "The Activity session handler was not initialized.");
        AuthenticatedRemoteWindowMediaSessionDirectory currentMediaSessions =
            remoteWindowMediaSessions?.SessionDirectory
            ?? throw new InvalidOperationException(
                "The Remote Window media directory was not initialized.");
        return new DesktopActivityNetworkBindings(
            currentHandler,
            currentMediaSessions);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref disposed, 1, 0) == 0)
        {
            _ = CompleteDisposalAsync();
        }

        return new ValueTask(disposalCompletion.Task);
    }

    private async Task CompleteDisposalAsync()
    {
        try
        {
            await DisposeResourcesAsync().ConfigureAwait(false);
            disposalCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            disposalCompletion.TrySetException(exception);
        }
    }

    private async Task DisposeResourcesAsync()
    {
        var failures = new List<Exception>();
        CaptureCleanupFailure(lifetimeCancellation.Cancel, failures);
        bool gateEntered = false;
        try
        {
            await initializationGate.WaitAsync().ConfigureAwait(false);
            gateEntered = true;
            AuthenticatedActivitySessionHandler? current = handler;
            handler = null;
            TrustSessionCoordinator? currentTrust = trust;
            trust = null;
            if (currentTrust is not null)
            {
                CaptureCleanupFailure(
                    () => currentTrust.Changed -= OnTrustChanged,
                    failures);
            }

            if (current is not null)
            {
                CaptureCleanupFailure(
                    () => current.Changed -= OnHandlerChanged,
                    failures);
                await CaptureCleanupFailureAsync(
                    current.DisposeAsync,
                    failures).ConfigureAwait(false);
            }

            IDesktopRemoteWindowMediaSessionOwner? currentMediaSessions =
                remoteWindowMediaSessions;
            remoteWindowMediaSessions = null;
            if (currentMediaSessions is not null)
            {
                await CaptureCleanupFailureAsync(
                    currentMediaSessions.DisposeAsync,
                    failures).ConfigureAwait(false);
            }

            Volatile.Write(ref node, null);
            ReplaceEndpoint? currentReplaceEndpoint = replaceEndpoint;
            replaceEndpoint = null;
            if (currentReplaceEndpoint is not null)
            {
                CaptureCleanupFailure(currentReplaceEndpoint.Dispose, failures);
            }

            PersistentReplaceStateStore? currentReplaceState = replaceState;
            replaceState = null;
            if (currentReplaceState is not null)
            {
                CaptureCleanupFailure(currentReplaceState.Dispose, failures);
            }

            PersistentSceneRemoteChildJournal? currentSceneJournal =
                sceneRemoteChildJournal;
            sceneRemoteChildJournal = null;
            if (currentSceneJournal is not null)
            {
                CaptureCleanupFailure(currentSceneJournal.Dispose, failures);
            }

            sceneApplyPlanner = null;
            sceneApplyCoordinator = null;
            sceneApplyCompensator = null;
            PersistentSceneApplyJournal? currentSceneApplyJournal =
                sceneApplyJournal;
            sceneApplyJournal = null;
            if (currentSceneApplyJournal is not null)
            {
                CaptureCleanupFailure(currentSceneApplyJournal.Dispose, failures);
            }

            catalog = null;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            if (gateEntered)
            {
                CaptureCleanupFailure(
                    () => _ = initializationGate.Release(),
                    failures);
            }

            CaptureCleanupFailure(initializationGate.Dispose, failures);
            CaptureCleanupFailure(lifetimeCancellation.Dispose, failures);
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(
                "Desktop Activity runtime cleanup failed.",
                failures);
        }
    }

    private static void CaptureCleanupFailure(
        Action cleanup,
        List<Exception> failures)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static async ValueTask CaptureCleanupFailureAsync(
        Func<ValueTask> cleanup,
        List<Exception> failures)
    {
        try
        {
            await cleanup().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static ActivityKind WorkspaceNoteKind { get; } =
        ActivityKind.Parse("workspace.note/v1");

    private sealed class DesktopRemoteWindowMediaSessionOwner(
        TimeProvider timeProvider) : IDesktopRemoteWindowMediaSessionOwner
    {
        public AuthenticatedRemoteWindowMediaSessionDirectory SessionDirectory { get; } =
            new(timeProvider: timeProvider);

        public ValueTask DisposeAsync() => SessionDirectory.DisposeAsync();
    }

    private OutboundOperationPreparation PrepareOutboundOperation(
        ActivityId activityId,
        DeviceId targetDeviceId,
        OperationKind kind)
    {
        FlowspanNode currentNode = node
            ?? throw new InvalidOperationException(
                "The Activity runtime is not initialized.");
        OperationContext context = OperationContext.Create(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()),
            timeProvider.GetUtcNow().Add(OperationLifetime));
        if (!currentNode.TryGetActivity(activityId, out ActivityInstance? activity))
        {
            return new OutboundOperationPreparation(
                currentNode,
                context,
                null,
                OperationReceipt.RejectedMissingActivity(
                    context.OperationId,
                    context.CorrelationId,
                    kind,
                    currentNode.DeviceId,
                    targetDeviceId,
                    activityId,
                    timeProvider.GetUtcNow()));
        }

        TrustSessionCoordinator? currentTrust = trust;
        if (currentTrust is null
            || !currentTrust.TryGetCurrentTrust(
                targetDeviceId,
                out TrustRecord? record)
            || !record.GrantedCapabilities.Allows(Capability.ActivityReceive))
        {
            return new OutboundOperationPreparation(
                currentNode,
                context,
                null,
                OperationReceipt.Rejected(
                    context.OperationId,
                    context.CorrelationId,
                    kind,
                    currentNode.DeviceId,
                    targetDeviceId,
                    activity.Descriptor,
                    timeProvider.GetUtcNow(),
                    FailureCode.CapabilityDenied));
        }

        if (handler is null
            || !handler.TryGetChannel(
                targetDeviceId,
                out IActivityChannel? channel)
            || channel is null)
        {
            return new OutboundOperationPreparation(
                currentNode,
                context,
                null,
                OperationReceipt.Failed(
                    context.OperationId,
                    context.CorrelationId,
                    kind,
                    currentNode.DeviceId,
                    targetDeviceId,
                    activity.Descriptor,
                    timeProvider.GetUtcNow(),
                    FailureCode.PeerUnavailable));
        }

        return new OutboundOperationPreparation(
            currentNode,
            context,
            channel,
            null);
    }

    private static DesktopActivitySnapshot CreateSnapshot(ActivityInstance activity) => new(
        activity.Descriptor.Id,
        activity.Descriptor.Title,
        activity.Descriptor.Kind.Value,
        activity.Descriptor.Sensitivity,
        activity.Lifecycle);

    private static bool IsExactReplaceTarget(
        DesktopReplaceTargetSnapshot current,
        DesktopReplaceTargetSnapshot selected) =>
        current.DeviceId == selected.DeviceId
        && current.ActivityId == selected.ActivityId
        && current.Revision == selected.Revision
        && StringComparer.OrdinalIgnoreCase.Equals(
            current.DescriptorDigest,
            selected.DescriptorDigest)
        && StringComparer.Ordinal.Equals(current.Kind, selected.Kind)
        && StringComparer.Ordinal.Equals(
            current.PlacementSlot,
            selected.PlacementSlot)
        && StringComparer.Ordinal.Equals(current.Title, selected.Title);

    private void OnHandlerChanged() => PublishChanged();

    private void OnTrustChanged() => PublishChanged();

    private void PublishChanged()
    {
        foreach (Action subscriber in Changed?.GetInvocationList().Cast<Action>() ?? [])
        {
            try
            {
                subscriber();
            }
            catch
            {
                // Presentation observers cannot own Activity/session lifetime.
            }
        }
    }

    private sealed class TimeProviderClock(TimeProvider timeProvider) : IClock
    {
        public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
    }

    private sealed class DesktopSceneReplaceUndoAvailability(
        DeviceId deviceId,
        PersistentReplaceStateStore? replaceState) :
        ISceneReplaceUndoAvailability
    {
        public bool HasDurableUndoFor(ActivityInstance target)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (replaceState is null
                || target.Placement.DeviceId != deviceId
                || replaceState.CapsuleCount
                    >= PersistentReplaceStateStore.MaximumCapsuleCount)
            {
                return false;
            }

            try
            {
                return !replaceState.GetRestartRecoveryPlan(deviceId)
                    .IsBlockedByUnresolvedOperation;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return false;
            }
        }
    }

    private sealed class DeferredSceneOperationRouteDirectory :
        ISceneOperationRouteDirectory
    {
        private ISceneOperationRouteDirectory? inner;

        public void SetInner(ISceneOperationRouteDirectory routeDirectory)
        {
            ArgumentNullException.ThrowIfNull(routeDirectory);
            if (Interlocked.CompareExchange(
                    ref inner,
                    routeDirectory,
                    comparand: null) is not null)
            {
                throw new InvalidOperationException(
                    "The Scene operation route directory is already initialized.");
            }
        }

        public bool TryGetChannel(
            DeviceId peerDeviceId,
            out IActivityChannel? channel)
        {
            ISceneOperationRouteDirectory? current = Volatile.Read(ref inner);
            if (current is not null)
            {
                return current.TryGetChannel(peerDeviceId, out channel);
            }

            channel = null;
            return false;
        }

        public IReadOnlyList<DeviceId> GetSceneParticipantDeviceIds()
        {
            ISceneOperationRouteDirectory? current = Volatile.Read(ref inner);
            return current?.GetSceneParticipantDeviceIds() ?? [];
        }

        public bool TryGetReplaceChannel(
            DeviceId peerDeviceId,
            out IReplaceChannel? channel)
        {
            ISceneOperationRouteDirectory? current = Volatile.Read(ref inner);
            if (current is not null)
            {
                return current.TryGetReplaceChannel(peerDeviceId, out channel);
            }

            channel = null;
            return false;
        }

        public bool TryGetSceneExactSlotChannel(
            DeviceId peerDeviceId,
            out ISceneExactSlotChannel? channel)
        {
            ISceneOperationRouteDirectory? current = Volatile.Read(ref inner);
            if (current is not null)
            {
                return current.TryGetSceneExactSlotChannel(
                    peerDeviceId,
                    out channel);
            }

            channel = null;
            return false;
        }

        public bool TryGetSceneSourceLookupChannel(
            DeviceId peerDeviceId,
            out ISceneSourceLookupChannel? channel)
        {
            ISceneOperationRouteDirectory? current = Volatile.Read(ref inner);
            if (current is not null)
            {
                return current.TryGetSceneSourceLookupChannel(
                    peerDeviceId,
                    out channel);
            }

            channel = null;
            return false;
        }

        public bool TryGetSceneChildOperationChannel(
            DeviceId peerDeviceId,
            out ISceneChildOperationChannel? channel)
        {
            ISceneOperationRouteDirectory? current = Volatile.Read(ref inner);
            if (current is not null)
            {
                return current.TryGetSceneChildOperationChannel(
                    peerDeviceId,
                    out channel);
            }

            channel = null;
            return false;
        }
    }

    private sealed record OutboundOperationPreparation(
        FlowspanNode Node,
        OperationContext Context,
        IActivityChannel? Channel,
        OperationReceipt? FailureReceipt);

    private sealed class TrustBoundActivityPeer(
        FlowspanNode node,
        TrustSessionCoordinator trust,
        Action activityChanged) : IActivityPeer
    {
        public DeviceId DeviceId => node.DeviceId;

        public async ValueTask<OperationReceipt> ReceiveActivityAsync(
            DeviceId senderDeviceId,
            ActivityTransferOffer offer,
            CancellationToken cancellationToken)
        {
            CapabilityGrant grant = trust.TryGetCurrentTrust(
                senderDeviceId,
                out TrustRecord? record)
                ? record.GrantedCapabilities
                : CapabilityGrant.None;
            node.SetPeerGrant(senderDeviceId, grant);
            OperationReceipt receipt = await node.ReceiveActivityAsync(
                senderDeviceId,
                offer,
                cancellationToken).ConfigureAwait(false);
            if (receipt.IsSuccess)
            {
                activityChanged();
            }

            return receipt;
        }
    }

    private sealed class TrustBoundReplaceInventoryPeer(
        ReplaceTargetInventoryEndpoint endpoint,
        TrustSessionCoordinator trust) : IReplaceTargetInventoryPeer
    {
        public DeviceId DeviceId => endpoint.DeviceId;

        public ValueTask<ReplaceTargetInventoryResult> QueryAsync(
            DeviceId requestingDeviceId,
            ReplaceTargetInventoryQuery query,
            CancellationToken cancellationToken)
        {
            CapabilityGrant grant = trust.TryGetCurrentTrust(
                requestingDeviceId,
                out TrustRecord? record)
                ? record.GrantedCapabilities
                : CapabilityGrant.None;
            endpoint.SetPeerGrant(requestingDeviceId, grant);
            return endpoint.QueryAsync(
                requestingDeviceId,
                query,
                cancellationToken);
        }
    }

    private sealed class TrustBoundReplacePeer(
        ReplaceEndpoint endpoint,
        PersistentReplaceStateStore replaceState,
        TrustSessionCoordinator trust,
        Action activityChanged,
        TimeProvider timeProvider) : IReplacePeer
    {
        public DeviceId DeviceId => endpoint.DeviceId;

        public async ValueTask<ReplaceOperationResult> ReplaceAsync(
            DeviceId senderDeviceId,
            ReplaceActivityCommand command,
            CancellationToken cancellationToken)
        {
            CapabilityGrant grant = trust.TryGetCurrentTrust(
                senderDeviceId,
                out TrustRecord? record)
                ? record.GrantedCapabilities
                : CapabilityGrant.None;
            endpoint.SetPeerGrant(senderDeviceId, grant);
            ReplaceRestartRecoveryPlan recoveryPlan;
            try
            {
                recoveryPlan = replaceState.GetRestartRecoveryPlan(endpoint.DeviceId);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return Failed(FailureCode.UndoUnavailable);
            }

            if (recoveryPlan.IsBlockedByUnresolvedOperation)
            {
                return Failed(FailureCode.OperationInProgress);
            }

            try
            {
                return await endpoint.ReplaceAsync(
                    senderDeviceId,
                    command,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                activityChanged();
            }

            ReplaceOperationResult Failed(FailureCode failureCode) => new(
                OperationReceipt.Failed(
                    command.Context.OperationId,
                    command.Context.CorrelationId,
                    OperationKind.Replace,
                    senderDeviceId,
                    endpoint.DeviceId,
                    command.IncomingDescriptor,
                    timeProvider.GetUtcNow(),
                    failureCode),
                null);
        }
    }

    private sealed class TrustBoundSceneControlPeer(
        SceneControlPeer peer,
        SceneActivityOperationEndpoint endpoint,
        TrustSessionCoordinator trust) : ISceneControlPeer
    {
        public DeviceId DeviceId => peer.DeviceId;

        public ValueTask<SceneSourceLookup> LocateSourceAsync(
            DeviceId coordinatorDeviceId,
            SceneSourceLookupQuery query,
            CancellationToken cancellationToken)
        {
            Refresh(coordinatorDeviceId);
            return peer.LocateSourceAsync(
                coordinatorDeviceId,
                query,
                cancellationToken);
        }

        public ValueTask<SceneExactSlotInspection> InspectExactSlotAsync(
            DeviceId coordinatorDeviceId,
            SceneExactSlotQuery query,
            CancellationToken cancellationToken)
        {
            Refresh(coordinatorDeviceId);
            return peer.InspectExactSlotAsync(
                coordinatorDeviceId,
                query,
                cancellationToken);
        }

        public ValueTask<SceneActivityOperationResult> ExecuteChildAsync(
            DeviceId coordinatorDeviceId,
            SceneRemoteChildInstruction instruction,
            CancellationToken cancellationToken)
        {
            Refresh(coordinatorDeviceId);
            Refresh(instruction.TargetDeviceId);
            return peer.ExecuteChildAsync(
                coordinatorDeviceId,
                instruction,
                cancellationToken);
        }

        public ValueTask<UndoReplaceResult> UndoReplaceAsync(
            DeviceId coordinatorDeviceId,
            SceneUndoReplaceInstruction instruction,
            CancellationToken cancellationToken)
        {
            Refresh(coordinatorDeviceId);
            return peer.UndoReplaceAsync(
                coordinatorDeviceId,
                instruction,
                cancellationToken);
        }

        private void Refresh(DeviceId peerDeviceId)
        {
            CapabilityGrant grant = trust.TryGetCurrentTrust(
                peerDeviceId,
                out TrustRecord? record)
                ? record.GrantedCapabilities
                : CapabilityGrant.None;
            endpoint.SetPeerGrant(peerDeviceId, grant);
        }
    }
}
