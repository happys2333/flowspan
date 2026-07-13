using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;

namespace Flowspan.Application;

public sealed record OperationContext
{
    private OperationContext(
        OperationId operationId,
        CorrelationId correlationId,
        DateTimeOffset deadline)
    {
        OperationId = operationId;
        CorrelationId = correlationId;
        Deadline = deadline;
    }

    public OperationId OperationId { get; }

    public CorrelationId CorrelationId { get; }

    public DateTimeOffset Deadline { get; }

    public static OperationContext Create(
        OperationId operationId,
        CorrelationId correlationId,
        DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(correlationId);
        return new OperationContext(operationId, correlationId, deadline);
    }
}

public sealed record ActivityTransferOffer
{
    private ActivityTransferOffer(
        OperationKind kind,
        OperationContext context,
        ActivityDescriptor descriptor,
        ActivityPlacement targetPlacement,
        string requestDigest)
    {
        Kind = kind;
        Context = context;
        Descriptor = descriptor;
        TargetPlacement = targetPlacement;
        RequestDigest = requestDigest;
    }

    public OperationKind Kind { get; }

    public OperationContext Context { get; }

    public ActivityDescriptor Descriptor { get; }

    public ActivityPlacement TargetPlacement { get; }

    public string RequestDigest { get; }

    public static ActivityTransferOffer Create(
        OperationKind kind,
        OperationContext context,
        ActivityDescriptor descriptor,
        ActivityPlacement targetPlacement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(targetPlacement);
        if (kind is not (OperationKind.Handoff or OperationKind.Move))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "An Activity transfer offer must be a handoff or move.");
        }

        string digestInput = string.Join(
            '\n',
            kind.ToString(),
            context.OperationId.ToString(),
            descriptor.Id.ToString(),
            descriptor.Kind.Value,
            descriptor.DescriptorDigest,
            targetPlacement.DeviceId.ToString(),
            targetPlacement.Slot,
            context.Deadline.ToString("O", CultureInfo.InvariantCulture));
        string requestDigest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(digestInput)));

        return new ActivityTransferOffer(
            kind,
            context,
            descriptor,
            targetPlacement,
            requestDigest);
    }

    public string BindAuthenticatedSender(DeviceId senderDeviceId)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        string material = string.Join('\n', senderDeviceId.ToString(), RequestDigest);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
