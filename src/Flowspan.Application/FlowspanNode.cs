using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Flowspan.Domain;

namespace Flowspan.Application;

public sealed class FlowspanNode : IActivityPeer
{
    private readonly ActivityAdapterRegistry adapterRegistry;
    private readonly IActivityCatalog catalog;
    private readonly IClock clock;
    private readonly IOperationJournal journal;
    private readonly IReceiptSink receiptSink;
    private readonly ConcurrentDictionary<DeviceId, CapabilityGrant> peerGrants = new();

    public FlowspanNode(
        DeviceId deviceId,
        string displayName,
        IClock clock,
        IActivityCatalog catalog,
        IOperationJournal journal,
        ActivityAdapterRegistry adapterRegistry,
        IReceiptSink receiptSink)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(adapterRegistry);
        ArgumentNullException.ThrowIfNull(receiptSink);

        DeviceId = deviceId;
        DisplayName = displayName.Trim();
        this.clock = clock;
        this.catalog = catalog;
        this.journal = journal;
        this.adapterRegistry = adapterRegistry;
        this.receiptSink = receiptSink;
    }

    public DeviceId DeviceId { get; }

    public string DisplayName { get; }

    public void SetPeerGrant(DeviceId peerDeviceId, CapabilityGrant grant)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentNullException.ThrowIfNull(grant);
        peerGrants[peerDeviceId] = grant;
    }

    public bool AddLocalActivity(ActivityInstance activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (activity.Placement.DeviceId != DeviceId)
        {
            throw new ArgumentException(
                "A local Activity must be placed on this node.",
                nameof(activity));
        }

        return catalog.TryAdd(activity);
    }

    public bool TryGetActivity(
        ActivityId activityId,
        [NotNullWhen(true)] out ActivityInstance? activity) =>
        catalog.TryGet(activityId, out activity);

    public ValueTask<OperationReceipt> HandoffAsync(
        ActivityId activityId,
        IActivityChannel channel,
        string targetSlot,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        HandoffAsync(
            activityId,
            channel,
            targetSlot,
            context,
            expectedSource: null,
            cancellationToken);

    public async ValueTask<OperationReceipt> HandoffAsync(
        ActivityId activityId,
        IActivityChannel channel,
        string targetSlot,
        OperationContext context,
        ActivityInstance? expectedSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(context);

        if (!TryReadExpectedSource(
                activityId,
                expectedSource,
                out ActivityInstance? sourceActivity,
                out bool sourceChanged))
        {
            if (sourceChanged)
            {
                OperationReceipt conflicted = OperationReceipt.Rejected(
                    context.OperationId,
                    context.CorrelationId,
                    OperationKind.Handoff,
                    DeviceId,
                    channel.TargetDeviceId,
                    expectedSource!.Descriptor,
                    clock.UtcNow,
                    FailureCode.RevisionConflict);
                receiptSink.Write(conflicted);
                return conflicted;
            }

            OperationReceipt missing = OperationReceipt.RejectedMissingActivity(
                context.OperationId,
                context.CorrelationId,
                OperationKind.Handoff,
                DeviceId,
                channel.TargetDeviceId,
                activityId,
                clock.UtcNow);
            receiptSink.Write(missing);
            return missing;
        }

        ActivityPlacement targetPlacement = ActivityPlacement.On(channel.TargetDeviceId, targetSlot);
        ActivityTransferOffer offer = ActivityTransferOffer.Create(
            OperationKind.Handoff,
            context,
            sourceActivity.Descriptor,
            targetPlacement);

        ActivityDeliveryResult delivery = await channel
            .SendAsync(DeviceId, offer, cancellationToken)
            .ConfigureAwait(false);
        OperationReceipt receipt = ResolveDelivery(delivery, offer);
        receiptSink.Write(receipt);
        return receipt;
    }

    public ValueTask<OperationReceipt> MoveAsync(
        ActivityId activityId,
        IActivityChannel channel,
        string targetSlot,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        MoveAsync(
            activityId,
            channel,
            targetSlot,
            context,
            expectedSource: null,
            cancellationToken);

    public async ValueTask<OperationReceipt> MoveAsync(
        ActivityId activityId,
        IActivityChannel channel,
        string targetSlot,
        OperationContext context,
        ActivityInstance? expectedSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(context);

        if (!TryReadExpectedSource(
                activityId,
                expectedSource,
                out ActivityInstance? sourceActivity,
                out bool sourceChanged))
        {
            if (sourceChanged)
            {
                OperationReceipt conflicted = OperationReceipt.Rejected(
                    context.OperationId,
                    context.CorrelationId,
                    OperationKind.Move,
                    DeviceId,
                    channel.TargetDeviceId,
                    expectedSource!.Descriptor,
                    clock.UtcNow,
                    FailureCode.RevisionConflict);
                receiptSink.Write(conflicted);
                return conflicted;
            }

            OperationReceipt missing = OperationReceipt.RejectedMissingActivity(
                context.OperationId,
                context.CorrelationId,
                OperationKind.Move,
                DeviceId,
                channel.TargetDeviceId,
                activityId,
                clock.UtcNow);
            receiptSink.Write(missing);
            return missing;
        }

        ActivityPlacement targetPlacement = ActivityPlacement.On(channel.TargetDeviceId, targetSlot);
        ActivityTransferOffer offer = ActivityTransferOffer.Create(
            OperationKind.Move,
            context,
            sourceActivity.Descriptor,
            targetPlacement);
        string coordinatorDigest = offer.BindAuthenticatedSender(DeviceId);

        JournalExecutionResult execution = await journal.ExecuteOnceAsync(
            context.OperationId,
            coordinatorDigest,
            ExecuteMoveAsync,
            cancellationToken).ConfigureAwait(false);
        OperationReceipt receipt;
        if (execution.IsConflict)
        {
            receipt = OperationReceipt.Rejected(
                context.OperationId,
                context.CorrelationId,
                OperationKind.Move,
                DeviceId,
                channel.TargetDeviceId,
                sourceActivity.Descriptor,
                clock.UtcNow,
                FailureCode.OperationIdConflict);
        }
        else
        {
            receipt = execution.Receipt
                ?? throw new InvalidOperationException(
                    "The coordinator journal returned no operation receipt.");
        }

        if (!execution.WasReplay || execution.IsConflict)
        {
            receiptSink.Write(receipt);
        }

        return receipt;

        async ValueTask<OperationReceipt> ExecuteMoveAsync(CancellationToken innerToken)
        {
            ActivityDeliveryResult delivery = await channel
                .SendAsync(DeviceId, offer, innerToken)
                .ConfigureAwait(false);
            OperationReceipt targetReceipt = ResolveDelivery(delivery, offer);
            if (!targetReceipt.IsSuccess)
            {
                return targetReceipt;
            }

            if (!adapterRegistry.TryFind(sourceActivity.Descriptor.Kind, out IActivityAdapter? adapter)
                || adapter is null)
            {
                return OperationReceipt.CommittedWithWarning(
                    context.OperationId,
                    context.CorrelationId,
                    OperationKind.Move,
                    DeviceId,
                    channel.TargetDeviceId,
                    sourceActivity.Descriptor,
                    clock.UtcNow,
                    FailureCode.SourceCleanupFailed);
            }

            CloseActivityResult closeResult = await adapter
                .CloseAsync(sourceActivity, innerToken)
                .ConfigureAwait(false);
            if (!closeResult.Succeeded
                || !catalog.TryUpdate(sourceActivity, sourceActivity.Close()))
            {
                return OperationReceipt.CommittedWithWarning(
                    context.OperationId,
                    context.CorrelationId,
                    OperationKind.Move,
                    DeviceId,
                    channel.TargetDeviceId,
                    sourceActivity.Descriptor,
                    clock.UtcNow,
                    FailureCode.SourceCleanupFailed);
            }

            return OperationReceipt.Committed(
                context.OperationId,
                context.CorrelationId,
                OperationKind.Move,
                DeviceId,
                channel.TargetDeviceId,
                sourceActivity.Descriptor,
                clock.UtcNow);
        }
    }

    public async ValueTask<OperationReceipt> ReceiveActivityAsync(
        DeviceId senderDeviceId,
        ActivityTransferOffer offer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(offer);

        JournalExecutionResult execution = await journal.ExecuteOnceAsync(
            offer.Context.OperationId,
            offer.BindAuthenticatedSender(senderDeviceId),
            ExecuteHandoffAsync,
            cancellationToken).ConfigureAwait(false);

        if (execution.IsConflict)
        {
            OperationReceipt conflict = Reject(FailureCode.OperationIdConflict);
            receiptSink.Write(conflict);
            return conflict;
        }

        return execution.Receipt
            ?? throw new InvalidOperationException("The journal returned no operation receipt.");

        async ValueTask<OperationReceipt> ExecuteHandoffAsync(CancellationToken innerToken)
        {
            if (offer.Context.Deadline <= clock.UtcNow)
            {
                return RecordRejection(FailureCode.DeadlineExpired);
            }

            if (!peerGrants.TryGetValue(senderDeviceId, out CapabilityGrant? grant)
                || !grant.Allows(Capability.ActivityOffer))
            {
                return RecordRejection(FailureCode.CapabilityDenied);
            }

            if (catalog.TryGet(offer.Descriptor.Id, out _))
            {
                return RecordRejection(FailureCode.ActivityAlreadyExists);
            }

            if (!adapterRegistry.TryFind(offer.Descriptor.Kind, out IActivityAdapter? adapter)
                || adapter is null)
            {
                return RecordRejection(FailureCode.AdapterUnavailable);
            }

            ResumeActivityResult result = await adapter
                .ResumeAsync(offer.Descriptor, offer.TargetPlacement, innerToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return RecordRejection(result.FailureCode);
            }

            ActivityInstance resumed = ActivityInstance.Active(
                offer.Descriptor,
                offer.TargetPlacement);
            if (!catalog.TryAdd(resumed))
            {
                return RecordRejection(FailureCode.ActivityAlreadyExists);
            }

            OperationReceipt committed = OperationReceipt.Committed(
                offer.Context.OperationId,
                offer.Context.CorrelationId,
                offer.Kind,
                senderDeviceId,
                DeviceId,
                offer.Descriptor,
                clock.UtcNow);
            receiptSink.Write(committed);
            return committed;
        }

        OperationReceipt RecordRejection(FailureCode failureCode)
        {
            OperationReceipt rejected = Reject(failureCode);
            receiptSink.Write(rejected);
            return rejected;
        }

        OperationReceipt Reject(FailureCode failureCode) => OperationReceipt.Rejected(
            offer.Context.OperationId,
            offer.Context.CorrelationId,
            offer.Kind,
            senderDeviceId,
            DeviceId,
            offer.Descriptor,
            clock.UtcNow,
            failureCode);
    }

    private bool TryReadExpectedSource(
        ActivityId activityId,
        ActivityInstance? expectedSource,
        [NotNullWhen(true)] out ActivityInstance? sourceActivity,
        out bool sourceChanged)
    {
        sourceChanged = false;
        if (!catalog.TryGet(activityId, out sourceActivity)
            || sourceActivity is null)
        {
            sourceActivity = null;
            return false;
        }

        if (expectedSource is not null && sourceActivity != expectedSource)
        {
            sourceActivity = null;
            sourceChanged = true;
            return false;
        }

        return true;
    }

    private OperationReceipt ResolveDelivery(
        ActivityDeliveryResult delivery,
        ActivityTransferOffer offer) => delivery.Status switch
        {
            ActivityDeliveryStatus.Acknowledged => delivery.Receipt
                ?? throw new InvalidOperationException(
                    "An acknowledged delivery must include a receipt."),
            ActivityDeliveryStatus.NotDelivered => OperationReceipt.Failed(
                offer.Context.OperationId,
                offer.Context.CorrelationId,
                offer.Kind,
                DeviceId,
                offer.TargetPlacement.DeviceId,
                offer.Descriptor,
                clock.UtcNow,
                FailureCode.PeerUnavailable),
            ActivityDeliveryStatus.AcknowledgementLost => OperationReceipt.Recovering(
                offer.Context.OperationId,
                offer.Context.CorrelationId,
                offer.Kind,
                DeviceId,
                offer.TargetPlacement.DeviceId,
                offer.Descriptor,
                clock.UtcNow,
                FailureCode.AcknowledgementLost),
            _ => throw new ArgumentOutOfRangeException(
                nameof(delivery),
                delivery.Status,
                "Unknown Activity delivery status."),
        };
}
