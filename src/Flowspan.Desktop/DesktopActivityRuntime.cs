using System.Collections.Immutable;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Application.Adapters;
using Flowspan.Domain;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop;

internal sealed class DesktopActivityRuntime : IDesktopActivityService
{
    private static readonly TimeSpan OperationLifetime = TimeSpan.FromSeconds(30);
    private readonly Func<CancellationToken, ValueTask<DeviceIdentity>> getIdentity;
    private readonly Func<CancellationToken, ValueTask<TrustSessionCoordinator>> getTrust;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly TimeProvider timeProvider;
    private InMemoryActivityCatalog? catalog;
    private AuthenticatedActivitySessionHandler? handler;
    private FlowspanNode? node;
    private TrustSessionCoordinator? trust;
    private int disposed;

    public DesktopActivityRuntime(
        Func<CancellationToken, ValueTask<DeviceIdentity>> getIdentity,
        Func<CancellationToken, ValueTask<TrustSessionCoordinator>> getTrust,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(getIdentity);
        ArgumentNullException.ThrowIfNull(getTrust);
        this.getIdentity = getIdentity;
        this.getTrust = getTrust;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event Action? Changed;

    public bool IsReady => Volatile.Read(ref node) is not null;

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

    public async ValueTask<OperationReceipt> HandoffAsync(
        ActivityId activityId,
        DeviceId targetDeviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        FlowspanNode currentNode = node
            ?? throw new InvalidOperationException(
                "The Activity runtime is not initialized.");
        var context = OperationContext.Create(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()),
            timeProvider.GetUtcNow().Add(OperationLifetime));
        if (!currentNode.TryGetActivity(activityId, out ActivityInstance? activity))
        {
            return OperationReceipt.RejectedMissingActivity(
                context.OperationId,
                context.CorrelationId,
                OperationKind.Handoff,
                currentNode.DeviceId,
                targetDeviceId,
                activityId,
                timeProvider.GetUtcNow());
        }

        TrustSessionCoordinator? currentTrust = trust;
        if (currentTrust is null
            || !currentTrust.TryGetCurrentTrust(
                targetDeviceId,
                out TrustRecord? record)
            || !record.GrantedCapabilities.Allows(Capability.ActivityReceive))
        {
            return OperationReceipt.Rejected(
                context.OperationId,
                context.CorrelationId,
                OperationKind.Handoff,
                currentNode.DeviceId,
                targetDeviceId,
                activity.Descriptor,
                timeProvider.GetUtcNow(),
                FailureCode.CapabilityDenied);
        }

        if (handler is null
            || !handler.TryGetChannel(
                targetDeviceId,
                out IActivityChannel? channel)
            || channel is null)
        {
            return OperationReceipt.Failed(
                context.OperationId,
                context.CorrelationId,
                OperationKind.Handoff,
                currentNode.DeviceId,
                targetDeviceId,
                activity.Descriptor,
                timeProvider.GetUtcNow(),
                FailureCode.PeerUnavailable);
        }

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        return await currentNode.HandoffAsync(
            activityId,
            channel,
            "desktop",
            context,
            linked.Token).ConfigureAwait(false);
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
            var newNode = new FlowspanNode(
                identity.DeviceId,
                identity.DisplayName,
                new TimeProviderClock(timeProvider),
                newCatalog,
                new InMemoryOperationJournal(),
                new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]),
                NullReceiptSink.Instance);
            var authorizedPeer = new TrustBoundActivityPeer(
                newNode,
                coordinator,
                PublishChanged);
            var newHandler = new AuthenticatedActivitySessionHandler(
                authorizedPeer,
                timeProvider);
            newHandler.Changed += OnHandlerChanged;
            catalog = newCatalog;
            trust = coordinator;
            handler = newHandler;
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetimeCancellation.Cancel();
        await initializationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            AuthenticatedActivitySessionHandler? current = handler;
            handler = null;
            if (current is not null)
            {
                current.Changed -= OnHandlerChanged;
                await current.DisposeAsync().ConfigureAwait(false);
            }

            Volatile.Write(ref node, null);
            catalog = null;
            trust = null;
        }
        finally
        {
            initializationGate.Release();
            initializationGate.Dispose();
            lifetimeCancellation.Dispose();
        }
    }

    private static ActivityKind WorkspaceNoteKind { get; } =
        ActivityKind.Parse("workspace.note/v1");

    private static DesktopActivitySnapshot CreateSnapshot(ActivityInstance activity) => new(
        activity.Descriptor.Id,
        activity.Descriptor.Title,
        activity.Descriptor.Kind.Value,
        activity.Descriptor.Sensitivity,
        activity.Lifecycle);

    private void OnHandlerChanged() => PublishChanged();

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
}
