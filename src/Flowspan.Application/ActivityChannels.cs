using Flowspan.Domain;

namespace Flowspan.Application;

public sealed class DirectActivityChannel : IActivityChannel
{
    private readonly IActivityPeer target;

    public DirectActivityChannel(IActivityPeer target)
    {
        ArgumentNullException.ThrowIfNull(target);
        this.target = target;
    }

    public DeviceId TargetDeviceId => target.DeviceId;

    public async ValueTask<ActivityDeliveryResult> SendAsync(
        DeviceId senderDeviceId,
        ActivityTransferOffer offer,
        CancellationToken cancellationToken)
    {
        OperationReceipt receipt = await target
            .ReceiveActivityAsync(senderDeviceId, offer, cancellationToken)
            .ConfigureAwait(false);
        return ActivityDeliveryResult.Acknowledged(receipt);
    }
}

public enum ActivityDeliveryFault
{
    None,
    DropBeforeDelivery,
    DropAcknowledgement,
    DuplicateDelivery,
}

public sealed class DeterministicActivityChannel : IActivityChannel
{
    private readonly Lock gate = new();
    private readonly Queue<ActivityDeliveryFault> faults;
    private readonly IActivityPeer target;

    public DeterministicActivityChannel(
        IActivityPeer target,
        IEnumerable<ActivityDeliveryFault> faults)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(faults);
        this.target = target;
        this.faults = new Queue<ActivityDeliveryFault>(faults);
    }

    public DeviceId TargetDeviceId => target.DeviceId;

    public async ValueTask<ActivityDeliveryResult> SendAsync(
        DeviceId senderDeviceId,
        ActivityTransferOffer offer,
        CancellationToken cancellationToken)
    {
        ActivityDeliveryFault fault;
        lock (gate)
        {
            fault = faults.Count > 0 ? faults.Dequeue() : ActivityDeliveryFault.None;
        }

        if (fault == ActivityDeliveryFault.DropBeforeDelivery)
        {
            return ActivityDeliveryResult.NotDelivered;
        }

        OperationReceipt receipt = await target
            .ReceiveActivityAsync(senderDeviceId, offer, cancellationToken)
            .ConfigureAwait(false);

        if (fault == ActivityDeliveryFault.DropAcknowledgement)
        {
            return ActivityDeliveryResult.AcknowledgementLost;
        }

        if (fault == ActivityDeliveryFault.DuplicateDelivery)
        {
            receipt = await target
                .ReceiveActivityAsync(senderDeviceId, offer, cancellationToken)
                .ConfigureAwait(false);
        }

        return ActivityDeliveryResult.Acknowledged(receipt);
    }
}
