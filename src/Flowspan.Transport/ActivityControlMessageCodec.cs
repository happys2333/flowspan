using System.Security.Cryptography;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Transport;

public static class ActivityControlMessageCodec
{
    private static readonly TimeSpan ReceiptTimeToLive = TimeSpan.FromSeconds(30);

    public static ControlMessage CreateReplaceInventoryQuery(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        ReplaceTargetInventoryQuery query,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        TimeSpan untilDeadline = query.Deadline - sentAt;
        if (untilDeadline <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sentAt),
                "A Replace inventory query must be sent before its deadline.");
        }

        double ttlMilliseconds = Math.Ceiling(untilDeadline.TotalMilliseconds);
        if (ttlMilliseconds > ControlMessage.MaximumTimeToLiveMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                "A Replace inventory deadline exceeds the control envelope lifetime limit.");
        }

        string body = JsonSerializer.Serialize(new
        {
            deadline = query.Deadline,
            incomingKind = query.IncomingKind.Value,
            targetDeviceId = query.TargetDeviceId.ToString(),
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.ActivityReplaceInventory,
            Guid.NewGuid(),
            query.CorrelationId,
            senderDeviceId,
            sentAt,
            TimeSpan.FromMilliseconds(ttlMilliseconds),
            body);
    }

    public static ReplaceTargetInventoryQuery DecodeReplaceInventoryQuery(
        ControlMessage message,
        DeviceId expectedTargetDeviceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedTargetDeviceId);
        if (message.Type != ControlMessageType.ActivityReplaceInventory)
        {
            throw new InvalidDataException(
                "The control message is not a Replace inventory query.");
        }

        try
        {
            JsonElement root = message.Body;
            RequireOnly(root, "deadline", "incomingKind", "targetDeviceId");
            DateTimeOffset deadline = RequireDateTimeOffset(root, "deadline");
            DateTimeOffset envelopeExpiry = message.SentAt.AddMilliseconds(
                message.TimeToLiveMilliseconds);
            if (deadline <= message.SentAt || deadline > envelopeExpiry)
            {
                throw new InvalidDataException(
                    "The Replace inventory deadline is outside the authenticated envelope lifetime.");
            }

            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            if (targetDeviceId != expectedTargetDeviceId)
            {
                throw new InvalidDataException(
                    "The Replace inventory query targets another device.");
            }

            return ReplaceTargetInventoryQuery.Create(
                message.CorrelationId,
                targetDeviceId,
                ActivityKind.Parse(RequireString(root, "incomingKind")),
                deadline);
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or JsonException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The Replace inventory query body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateReplaceInventoryResult(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        ReplaceTargetInventoryResult result,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(result);
        if (senderDeviceId != result.TargetDeviceId)
        {
            throw new ArgumentException(
                "A Replace inventory result sender must match its target device.",
                nameof(senderDeviceId));
        }

        if (sentAt < result.CapturedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sentAt),
                "A Replace inventory result cannot be sent before it was captured.");
        }

        string body = JsonSerializer.Serialize(new
        {
            capturedAt = result.CapturedAt,
            failureCode = ToWireName(result.FailureCode),
            incomingKind = result.IncomingKind.Value,
            isTruncated = result.IsTruncated,
            queryDeadline = result.QueryDeadline,
            requestingDeviceId = result.RequestingDeviceId.ToString(),
            targetDeviceId = result.TargetDeviceId.ToString(),
            targets = result.Targets.Select(static target => new
            {
                activityId = target.ActivityId.ToString(),
                descriptorDigest = target.DescriptorDigest,
                kind = target.Kind.Value,
                placementSlot = target.PlacementSlot,
                revision = target.Revision,
                title = target.Title,
            }),
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.ActivityReplaceInventoryResult,
            Guid.NewGuid(),
            result.CorrelationId,
            senderDeviceId,
            sentAt,
            ReceiptTimeToLive,
            body);
    }

    public static ReplaceTargetInventoryResult DecodeReplaceInventoryResult(
        ControlMessage message,
        DeviceId expectedRecipientDeviceId,
        ReplaceTargetInventoryQuery expectedQuery)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedRecipientDeviceId);
        ArgumentNullException.ThrowIfNull(expectedQuery);
        if (message.Type != ControlMessageType.ActivityReplaceInventoryResult)
        {
            throw new InvalidDataException(
                "The control message is not a Replace inventory result.");
        }

        try
        {
            if (message.CorrelationId != expectedQuery.CorrelationId)
            {
                throw new InvalidDataException(
                    "The Replace inventory result correlation does not match its query.");
            }

            JsonElement root = message.Body;
            RequireOnly(
                root,
                "capturedAt",
                "failureCode",
                "incomingKind",
                "isTruncated",
                "queryDeadline",
                "requestingDeviceId",
                "targetDeviceId",
                "targets");
            DeviceId requestingDeviceId = DeviceId.Parse(
                RequireString(root, "requestingDeviceId"));
            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            if (requestingDeviceId != expectedRecipientDeviceId
                || targetDeviceId != message.SenderDeviceId
                || targetDeviceId != expectedQuery.TargetDeviceId)
            {
                throw new InvalidDataException(
                    "The Replace inventory result participants do not match the authenticated query.");
            }

            ActivityKind incomingKind = ActivityKind.Parse(
                RequireString(root, "incomingKind"));
            DateTimeOffset queryDeadline = RequireDateTimeOffset(
                root,
                "queryDeadline");
            if (incomingKind != expectedQuery.IncomingKind
                || queryDeadline != expectedQuery.Deadline)
            {
                throw new InvalidDataException(
                    "The Replace inventory result purpose does not match its query.");
            }

            DateTimeOffset capturedAt = RequireDateTimeOffset(root, "capturedAt");
            if (capturedAt > message.SentAt)
            {
                throw new InvalidDataException(
                    "The Replace inventory result predates its claimed capture time.");
            }

            FailureCode failureCode = ParseFailureCode(
                RequireString(root, "failureCode"));
            bool isTruncated = RequireBoolean(root, "isTruncated");
            JsonElement targetsElement = Require(
                root,
                "targets",
                JsonValueKind.Array);
            if (targetsElement.GetArrayLength()
                > ReplaceTargetInventoryResult.MaximumTargets)
            {
                throw new InvalidDataException(
                    "The Replace inventory result exceeds its target limit.");
            }

            var targets = new List<ReplaceTargetSnapshot>(
                targetsElement.GetArrayLength());
            foreach (JsonElement target in targetsElement.EnumerateArray())
            {
                RequireOnly(
                    target,
                    "activityId",
                    "descriptorDigest",
                    "kind",
                    "placementSlot",
                    "revision",
                    "title");
                targets.Add(ReplaceTargetSnapshot.Create(
                    ActivityId.Parse(RequireString(target, "activityId")),
                    RequireInt64(target, "revision"),
                    RequireDigest(target, "descriptorDigest"),
                    ActivityKind.Parse(RequireString(target, "kind")),
                    RequireString(target, "title"),
                    RequireString(target, "placementSlot")));
            }

            if (failureCode == FailureCode.None)
            {
                return ReplaceTargetInventoryResult.Success(
                    requestingDeviceId,
                    expectedQuery,
                    capturedAt,
                    targets,
                    isTruncated);
            }

            if (targets.Count != 0 || isTruncated)
            {
                throw new InvalidDataException(
                    "A rejected Replace inventory result cannot disclose targets.");
            }

            return ReplaceTargetInventoryResult.Rejected(
                requestingDeviceId,
                expectedQuery,
                capturedAt,
                failureCode);
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or JsonException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The Replace inventory result body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateTransfer(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        ActivityTransferOffer offer,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(offer);
        TimeSpan untilDeadline = offer.Context.Deadline - sentAt;
        if (untilDeadline <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sentAt),
                "An Activity transfer must be sent before its operation deadline.");
        }

        double ttlMilliseconds = Math.Ceiling(untilDeadline.TotalMilliseconds);
        if (ttlMilliseconds > ControlMessage.MaximumTimeToLiveMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offer),
                "An Activity operation deadline exceeds the control envelope lifetime limit.");
        }

        var timeToLive = TimeSpan.FromMilliseconds(ttlMilliseconds);
        string body = JsonSerializer.Serialize(new
        {
            operationId = offer.Context.OperationId.ToString(),
            operationKind = ToWireName(offer.Kind),
            deadline = offer.Context.Deadline,
            targetDeviceId = offer.TargetPlacement.DeviceId.ToString(),
            targetSlot = offer.TargetPlacement.Slot,
            requestDigest = offer.RequestDigest,
            activity = new
            {
                id = offer.Descriptor.Id.ToString(),
                kind = offer.Descriptor.Kind.Value,
                originDeviceId = offer.Descriptor.OriginDeviceId.ToString(),
                title = offer.Descriptor.Title,
                payloadJson = offer.Descriptor.PayloadJson,
                payloadDigest = offer.Descriptor.PayloadDigest,
                descriptorDigest = offer.Descriptor.DescriptorDigest,
                sensitivity = ToWireName(offer.Descriptor.Sensitivity),
            },
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.ActivityTransfer,
            Guid.NewGuid(),
            offer.Context.CorrelationId,
            senderDeviceId,
            sentAt,
            timeToLive,
            body);
    }

    public static ActivityTransferOffer DecodeTransfer(
        ControlMessage message,
        DeviceId expectedTargetDeviceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedTargetDeviceId);
        if (message.Type != ControlMessageType.ActivityTransfer)
        {
            throw new InvalidDataException("The control message is not an Activity transfer.");
        }

        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "operationId",
                "operationKind",
                "deadline",
                "targetDeviceId",
                "targetSlot",
                "requestDigest",
                "activity");
            OperationId operationId = OperationId.Parse(RequireString(root, "operationId"));
            OperationKind kind = ParseOperationKind(RequireString(root, "operationKind"));
            DateTimeOffset deadline = RequireDateTimeOffset(root, "deadline");
            DateTimeOffset envelopeExpiry = message.SentAt.AddMilliseconds(
                message.TimeToLiveMilliseconds);
            if (deadline <= message.SentAt || deadline > envelopeExpiry)
            {
                throw new InvalidDataException(
                    "The Activity operation deadline is outside the authenticated envelope lifetime.");
            }

            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            if (targetDeviceId != expectedTargetDeviceId)
            {
                throw new InvalidDataException(
                    "The Activity transfer targets another device.");
            }

            string targetSlot = RequireString(root, "targetSlot");
            string claimedRequestDigest = RequireDigest(root, "requestDigest");
            JsonElement activity = Require(root, "activity", JsonValueKind.Object);
            RequireOnly(
                activity,
                "id",
                "kind",
                "originDeviceId",
                "title",
                "payloadJson",
                "payloadDigest",
                "descriptorDigest",
                "sensitivity");
            string claimedPayloadDigest = RequireDigest(activity, "payloadDigest");
            string claimedDescriptorDigest = RequireDigest(
                activity,
                "descriptorDigest");
            ActivityDescriptor descriptor = ActivityDescriptor.Create(
                ActivityId.Parse(RequireString(activity, "id")),
                ActivityKind.Parse(RequireString(activity, "kind")),
                DeviceId.Parse(RequireString(activity, "originDeviceId")),
                RequireString(activity, "title"),
                RequireString(activity, "payloadJson"),
                ParseSensitivity(RequireString(activity, "sensitivity")));
            RequireDigestMatch(
                claimedPayloadDigest,
                descriptor.PayloadDigest,
                "The Activity payload digest does not match.");
            RequireDigestMatch(
                claimedDescriptorDigest,
                descriptor.DescriptorDigest,
                "The Activity descriptor digest does not match.");

            var context = OperationContext.Create(
                operationId,
                message.CorrelationId,
                deadline);
            ActivityTransferOffer offer = ActivityTransferOffer.Create(
                kind,
                context,
                descriptor,
                ActivityPlacement.On(targetDeviceId, targetSlot));
            RequireDigestMatch(
                claimedRequestDigest,
                offer.RequestDigest,
                "The Activity transfer request digest does not match.");
            return offer;
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or JsonException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The Activity transfer body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateReplace(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        ReplaceActivityCommand command,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(command);
        TimeSpan untilDeadline = command.Context.Deadline - sentAt;
        if (untilDeadline <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sentAt),
                "An Activity Replace must be sent before its operation deadline.");
        }

        double ttlMilliseconds = Math.Ceiling(untilDeadline.TotalMilliseconds);
        if (ttlMilliseconds > ControlMessage.MaximumTimeToLiveMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "An Activity Replace deadline exceeds the control envelope lifetime limit.");
        }

        string body = JsonSerializer.Serialize(new
        {
            operationId = command.Context.OperationId.ToString(),
            deadline = command.Context.Deadline,
            targetDeviceId = command.TargetPlacement.DeviceId.ToString(),
            targetActivityId = command.TargetActivityId.ToString(),
            expectedTargetRevision = command.ExpectedTargetRevision,
            expectedTargetDescriptorDigest = command.ExpectedTargetDescriptorDigest,
            targetSlot = command.TargetPlacement.Slot,
            undoExpiresAt = command.UndoExpiresAt,
            requestDigest = command.RequestDigest,
            incomingActivity = new
            {
                id = command.IncomingDescriptor.Id.ToString(),
                kind = command.IncomingDescriptor.Kind.Value,
                originDeviceId = command.IncomingDescriptor.OriginDeviceId.ToString(),
                title = command.IncomingDescriptor.Title,
                payloadJson = command.IncomingDescriptor.PayloadJson,
                payloadDigest = command.IncomingDescriptor.PayloadDigest,
                descriptorDigest = command.IncomingDescriptor.DescriptorDigest,
                sensitivity = ToWireName(command.IncomingDescriptor.Sensitivity),
            },
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.ActivityReplace,
            Guid.NewGuid(),
            command.Context.CorrelationId,
            senderDeviceId,
            sentAt,
            TimeSpan.FromMilliseconds(ttlMilliseconds),
            body);
    }

    public static ReplaceActivityCommand DecodeReplace(
        ControlMessage message,
        DeviceId expectedTargetDeviceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedTargetDeviceId);
        if (message.Type != ControlMessageType.ActivityReplace)
        {
            throw new InvalidDataException("The control message is not an Activity Replace.");
        }

        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "operationId",
                "deadline",
                "targetDeviceId",
                "targetActivityId",
                "expectedTargetRevision",
                "expectedTargetDescriptorDigest",
                "targetSlot",
                "undoExpiresAt",
                "requestDigest",
                "incomingActivity");
            OperationId operationId = OperationId.Parse(RequireString(root, "operationId"));
            DateTimeOffset deadline = RequireDateTimeOffset(root, "deadline");
            DateTimeOffset envelopeExpiry = message.SentAt.AddMilliseconds(
                message.TimeToLiveMilliseconds);
            if (deadline <= message.SentAt || deadline > envelopeExpiry)
            {
                throw new InvalidDataException(
                    "The Replace deadline is outside the authenticated envelope lifetime.");
            }

            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            if (targetDeviceId != expectedTargetDeviceId)
            {
                throw new InvalidDataException("The Activity Replace targets another device.");
            }

            ActivityId targetActivityId = ActivityId.Parse(
                RequireString(root, "targetActivityId"));
            long expectedTargetRevision = RequireInt64(root, "expectedTargetRevision");
            if (expectedTargetRevision < 1)
            {
                throw new InvalidDataException(
                    "The expected target revision must be positive.");
            }

            string expectedTargetDescriptorDigest = RequireDigest(
                root,
                "expectedTargetDescriptorDigest");
            string targetSlot = RequireString(root, "targetSlot");
            DateTimeOffset undoExpiresAt = RequireDateTimeOffset(root, "undoExpiresAt");
            if (undoExpiresAt <= message.SentAt
                || undoExpiresAt - message.SentAt > ReplaceEndpoint.MaximumUndoRetention)
            {
                throw new InvalidDataException(
                    "The Replace undo expiry is outside the supported retention window.");
            }

            string claimedRequestDigest = RequireDigest(root, "requestDigest");
            JsonElement activity = Require(
                root,
                "incomingActivity",
                JsonValueKind.Object);
            RequireOnly(
                activity,
                "id",
                "kind",
                "originDeviceId",
                "title",
                "payloadJson",
                "payloadDigest",
                "descriptorDigest",
                "sensitivity");
            string claimedPayloadDigest = RequireDigest(activity, "payloadDigest");
            string claimedDescriptorDigest = RequireDigest(activity, "descriptorDigest");
            ActivityDescriptor incoming = ActivityDescriptor.Create(
                ActivityId.Parse(RequireString(activity, "id")),
                ActivityKind.Parse(RequireString(activity, "kind")),
                DeviceId.Parse(RequireString(activity, "originDeviceId")),
                RequireString(activity, "title"),
                RequireString(activity, "payloadJson"),
                ParseSensitivity(RequireString(activity, "sensitivity")));
            RequireDigestMatch(
                claimedPayloadDigest,
                incoming.PayloadDigest,
                "The incoming Activity payload digest does not match.");
            RequireDigestMatch(
                claimedDescriptorDigest,
                incoming.DescriptorDigest,
                "The incoming Activity descriptor digest does not match.");

            ReplaceActivityCommand command = ReplaceActivityCommand.Create(
                OperationContext.Create(
                    operationId,
                    message.CorrelationId,
                    deadline),
                targetActivityId,
                expectedTargetRevision,
                expectedTargetDescriptorDigest,
                incoming,
                ActivityPlacement.On(targetDeviceId, targetSlot),
                undoExpiresAt);
            RequireDigestMatch(
                claimedRequestDigest,
                command.RequestDigest,
                "The Activity Replace request digest does not match.");
            return command;
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or JsonException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The Activity Replace body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateReplaceResult(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        ReplaceOperationResult result,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(result);
        OperationReceipt receipt = result.Receipt;
        if (receipt.Kind != OperationKind.Replace)
        {
            throw new ArgumentException(
                "An Activity Replace result must contain a Replace receipt.",
                nameof(result));
        }

        if (senderDeviceId != receipt.TargetDeviceId)
        {
            throw new ArgumentException(
                "An Activity Replace result must be sent by its target device.",
                nameof(senderDeviceId));
        }

        ValidateReplaceCapsuleBinding(result);
        object? undoCapsule = result.UndoCapsule is UndoCapsuleReference capsule
            ? new
            {
                id = capsule.Id.ToString(),
                targetActivityId = capsule.TargetActivityId.ToString(),
                expectedTargetRevision = capsule.ExpectedTargetRevision,
                targetDescriptorDigest = capsule.TargetDescriptorDigest,
                incomingActivityId = capsule.IncomingActivityId.ToString(),
                incomingDescriptorDigest = capsule.IncomingDescriptorDigest,
                expiresAt = capsule.ExpiresAt,
            }
            : null;
        string body = JsonSerializer.Serialize(new
        {
            operationId = receipt.OperationId.ToString(),
            status = ToWireName(receipt.Status),
            sourceDeviceId = receipt.SourceDeviceId.ToString(),
            targetDeviceId = receipt.TargetDeviceId.ToString(),
            incomingActivityId = receipt.ActivityId.ToString(),
            incomingActivityKind = receipt.ActivityKind?.Value,
            incomingDescriptorDigest = receipt.DescriptorDigest,
            occurredAt = receipt.OccurredAt,
            failureCode = ToWireName(receipt.FailureCode),
            undoCapsule,
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.ActivityReplaceResult,
            Guid.NewGuid(),
            receipt.CorrelationId,
            senderDeviceId,
            sentAt,
            ReceiptTimeToLive,
            body);
    }

    public static ReplaceOperationResult DecodeReplaceResult(
        ControlMessage message,
        DeviceId expectedRecipientDeviceId,
        CorrelationId expectedCorrelationId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedRecipientDeviceId);
        ArgumentNullException.ThrowIfNull(expectedCorrelationId);
        if (message.Type != ControlMessageType.ActivityReplaceResult)
        {
            throw new InvalidDataException(
                "The control message is not an Activity Replace result.");
        }

        if (message.CorrelationId != expectedCorrelationId)
        {
            throw new InvalidDataException(
                "The Activity Replace result correlation does not match the pending request.");
        }

        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "operationId",
                "status",
                "sourceDeviceId",
                "targetDeviceId",
                "incomingActivityId",
                "incomingActivityKind",
                "incomingDescriptorDigest",
                "occurredAt",
                "failureCode",
                "undoCapsule");
            OperationId operationId = OperationId.Parse(RequireString(root, "operationId"));
            DeviceId sourceDeviceId = DeviceId.Parse(
                RequireString(root, "sourceDeviceId"));
            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            if (sourceDeviceId != expectedRecipientDeviceId
                || targetDeviceId != message.SenderDeviceId)
            {
                throw new InvalidDataException(
                    "The Activity Replace result participants do not match the authenticated channel.");
            }

            ActivityId incomingActivityId = ActivityId.Parse(
                RequireString(root, "incomingActivityId"));
            ActivityKind? incomingActivityKind = ReadOptionalString(
                root,
                "incomingActivityKind") is string kindValue
                    ? ActivityKind.Parse(kindValue)
                    : null;
            string? incomingDescriptorDigest = ReadOptionalString(
                root,
                "incomingDescriptorDigest");
            if (incomingDescriptorDigest is not null)
            {
                ValidateDigest(incomingDescriptorDigest, "incomingDescriptorDigest");
            }

            OperationReceipt receipt = OperationReceipt.FromRecordedResult(
                operationId,
                message.CorrelationId,
                OperationKind.Replace,
                ParseStatus(RequireString(root, "status")),
                sourceDeviceId,
                targetDeviceId,
                incomingActivityId,
                incomingActivityKind,
                incomingDescriptorDigest,
                RequireDateTimeOffset(root, "occurredAt"),
                ParseFailureCode(RequireString(root, "failureCode")));
            UndoCapsuleReference? capsule = DecodeUndoCapsule(
                root,
                operationId,
                message.CorrelationId,
                targetDeviceId);
            var result = new ReplaceOperationResult(receipt, capsule);
            ValidateReplaceCapsuleBinding(result);
            return result;
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or JsonException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The Activity Replace result body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateReceipt(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        OperationReceipt receipt,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(receipt);
        if (senderDeviceId != receipt.TargetDeviceId)
        {
            throw new ArgumentException(
                "An operation receipt must be sent by its target device.",
                nameof(senderDeviceId));
        }

        string body = JsonSerializer.Serialize(new
        {
            operationId = receipt.OperationId.ToString(),
            operationKind = ToWireName(receipt.Kind),
            status = ToWireName(receipt.Status),
            sourceDeviceId = receipt.SourceDeviceId.ToString(),
            targetDeviceId = receipt.TargetDeviceId.ToString(),
            activityId = receipt.ActivityId.ToString(),
            activityKind = receipt.ActivityKind?.Value,
            descriptorDigest = receipt.DescriptorDigest,
            occurredAt = receipt.OccurredAt,
            failureCode = ToWireName(receipt.FailureCode),
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.OperationReceipt,
            Guid.NewGuid(),
            receipt.CorrelationId,
            senderDeviceId,
            sentAt,
            ReceiptTimeToLive,
            body);
    }

    public static OperationReceipt DecodeReceipt(
        ControlMessage message,
        DeviceId expectedRecipientDeviceId,
        CorrelationId expectedCorrelationId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedRecipientDeviceId);
        ArgumentNullException.ThrowIfNull(expectedCorrelationId);
        if (message.Type != ControlMessageType.OperationReceipt)
        {
            throw new InvalidDataException("The control message is not an operation receipt.");
        }

        if (message.CorrelationId != expectedCorrelationId)
        {
            throw new InvalidDataException(
                "The operation receipt correlation does not match the pending request.");
        }

        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "operationId",
                "operationKind",
                "status",
                "sourceDeviceId",
                "targetDeviceId",
                "activityId",
                "activityKind",
                "descriptorDigest",
                "occurredAt",
                "failureCode");
            OperationId operationId = OperationId.Parse(RequireString(root, "operationId"));
            DeviceId sourceDeviceId = DeviceId.Parse(
                RequireString(root, "sourceDeviceId"));
            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            if (sourceDeviceId != expectedRecipientDeviceId
                || targetDeviceId != message.SenderDeviceId)
            {
                throw new InvalidDataException(
                    "The operation receipt participants do not match the authenticated channel.");
            }

            ActivityKind? activityKind = ReadOptionalString(root, "activityKind") is
                string kindValue
                ? ActivityKind.Parse(kindValue)
                : null;
            string? descriptorDigest = ReadOptionalString(root, "descriptorDigest");
            if (descriptorDigest is not null)
            {
                ValidateDigest(descriptorDigest, "descriptorDigest");
            }

            OperationReceipt receipt = OperationReceipt.FromRecordedResult(
                operationId,
                message.CorrelationId,
                ParseOperationKind(RequireString(root, "operationKind")),
                ParseStatus(RequireString(root, "status")),
                sourceDeviceId,
                targetDeviceId,
                ActivityId.Parse(RequireString(root, "activityId")),
                activityKind,
                descriptorDigest,
                RequireDateTimeOffset(root, "occurredAt"),
                ParseFailureCode(RequireString(root, "failureCode")));
            return receipt;
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or JsonException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The operation receipt body is malformed.",
                exception);
        }
    }

    private static JsonElement Require(
        JsonElement parent,
        string name,
        JsonValueKind kind)
    {
        if (!parent.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != kind)
        {
            throw new InvalidDataException(
                $"The required '{name}' field is missing or has the wrong type.");
        }

        return value;
    }

    private static string RequireString(JsonElement parent, string name) =>
        Require(parent, name, JsonValueKind.String).GetString()
        ?? throw new InvalidDataException($"The '{name}' field is null.");

    private static string? ReadOptionalString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            throw new InvalidDataException($"The required '{name}' field is missing.");
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString()
                ?? throw new InvalidDataException($"The '{name}' field is null."),
            _ => throw new InvalidDataException(
                $"The '{name}' field has the wrong type."),
        };
    }

    private static UndoCapsuleReference? DecodeUndoCapsule(
        JsonElement root,
        OperationId operationId,
        CorrelationId correlationId,
        DeviceId targetDeviceId)
    {
        if (!root.TryGetProperty("undoCapsule", out JsonElement value))
        {
            throw new InvalidDataException(
                "The required 'undoCapsule' field is missing.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "The 'undoCapsule' field has the wrong type.");
        }

        RequireOnly(
            value,
            "id",
            "targetActivityId",
            "expectedTargetRevision",
            "targetDescriptorDigest",
            "incomingActivityId",
            "incomingDescriptorDigest",
            "expiresAt");
        long expectedTargetRevision = RequireInt64(value, "expectedTargetRevision");
        if (expectedTargetRevision < 1)
        {
            throw new InvalidDataException(
                "The undo capsule target revision must be positive.");
        }

        return new UndoCapsuleReference(
            UndoCapsuleId.Parse(RequireString(value, "id")),
            operationId,
            correlationId,
            targetDeviceId,
            ActivityId.Parse(RequireString(value, "targetActivityId")),
            expectedTargetRevision,
            RequireDigest(value, "targetDescriptorDigest"),
            ActivityId.Parse(RequireString(value, "incomingActivityId")),
            RequireDigest(value, "incomingDescriptorDigest"),
            RequireUtcDateTimeOffset(value, "expiresAt"));
    }

    private static void ValidateReplaceCapsuleBinding(ReplaceOperationResult result)
    {
        bool committed = result.Receipt.Status == OperationStatus.Committed;
        if (committed != (result.UndoCapsule is not null))
        {
            throw new InvalidDataException(
                "A committed Replace must include undo metadata and other outcomes must not.");
        }

        if (result.UndoCapsule is not UndoCapsuleReference capsule)
        {
            return;
        }

        if (capsule.OperationId != result.Receipt.OperationId
            || capsule.CorrelationId != result.Receipt.CorrelationId
            || capsule.TargetDeviceId != result.Receipt.TargetDeviceId
            || capsule.IncomingActivityId != result.Receipt.ActivityId
            || result.Receipt.DescriptorDigest is not string receiptDigest
            || !DigestsEqual(capsule.IncomingDescriptorDigest, receiptDigest))
        {
            throw new InvalidDataException(
                "The undo capsule metadata is not bound to the Replace receipt.");
        }
    }

    private static DateTimeOffset RequireDateTimeOffset(
        JsonElement parent,
        string name)
    {
        JsonElement value = Require(parent, name, JsonValueKind.String);
        return value.TryGetDateTimeOffset(out DateTimeOffset parsed)
            ? parsed
            : throw new InvalidDataException($"The '{name}' field is not a timestamp.");
    }

    private static DateTimeOffset RequireUtcDateTimeOffset(
        JsonElement parent,
        string name)
    {
        DateTimeOffset parsed = RequireDateTimeOffset(parent, name);
        return parsed.Offset == TimeSpan.Zero
            ? parsed
            : throw new InvalidDataException(
                $"The '{name}' field must be a canonical UTC timestamp.");
    }

    private static long RequireInt64(JsonElement parent, string name)
    {
        JsonElement value = Require(parent, name, JsonValueKind.Number);
        return value.TryGetInt64(out long parsed)
            ? parsed
            : throw new InvalidDataException($"The '{name}' field is not an integer.");
    }

    private static bool RequireBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(
                $"The required '{name}' field is missing or has the wrong type.");
        }

        return value.GetBoolean();
    }

    private static string RequireDigest(JsonElement parent, string name)
    {
        string digest = RequireString(parent, name);
        ValidateDigest(digest, name);
        return digest;
    }

    private static void ValidateDigest(string digest, string name)
    {
        if (digest.Length != 64 || !digest.All(char.IsAsciiHexDigit))
        {
            throw new InvalidDataException(
                $"The '{name}' field is not a 32-byte hexadecimal digest.");
        }
    }

    private static void RequireDigestMatch(
        string claimed,
        string actual,
        string message)
    {
        byte[] claimedBytes = Convert.FromHexString(claimed);
        byte[] actualBytes = Convert.FromHexString(actual);
        if (!CryptographicOperations.FixedTimeEquals(claimedBytes, actualBytes))
        {
            throw new InvalidDataException(message);
        }
    }

    private static bool DigestsEqual(string first, string second) =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(first),
            Convert.FromHexString(second));

    private static void RequireOnly(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The message body must be an object.");
        }

        var expected = names.ToHashSet(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!expected.Remove(property.Name))
            {
                throw new InvalidDataException(
                    $"The message body contains unsupported field '{property.Name}'.");
            }
        }

        if (expected.Count > 0)
        {
            throw new InvalidDataException("The message body is missing required fields.");
        }
    }

    private static string ToWireName(OperationKind kind) => kind switch
    {
        OperationKind.Handoff => "handoff",
        OperationKind.Move => "move",
        OperationKind.Replace => "replace",
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind),
            kind,
            "Only Activity transfer operations have a wire representation here."),
    };

    private static OperationKind ParseOperationKind(string value) => value switch
    {
        "handoff" => OperationKind.Handoff,
        "move" => OperationKind.Move,
        "replace" => OperationKind.Replace,
        _ => throw new InvalidDataException("The Activity operation kind is unsupported."),
    };

    private static string ToWireName(ActivitySensitivity sensitivity) => sensitivity switch
    {
        ActivitySensitivity.Normal => "normal",
        ActivitySensitivity.Sensitive => "sensitive",
        ActivitySensitivity.Restricted => "restricted",
        _ => throw new ArgumentOutOfRangeException(nameof(sensitivity)),
    };

    private static ActivitySensitivity ParseSensitivity(string value) => value switch
    {
        "normal" => ActivitySensitivity.Normal,
        "sensitive" => ActivitySensitivity.Sensitive,
        "restricted" => ActivitySensitivity.Restricted,
        _ => throw new InvalidDataException("The Activity sensitivity is unsupported."),
    };

    private static string ToWireName(OperationStatus status) => status switch
    {
        OperationStatus.Committed => "committed",
        OperationStatus.CommittedWithWarning => "committed-with-warning",
        OperationStatus.Rejected => "rejected",
        OperationStatus.Failed => "failed",
        OperationStatus.Recovering => "recovering",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static OperationStatus ParseStatus(string value) => value switch
    {
        "committed" => OperationStatus.Committed,
        "committed-with-warning" => OperationStatus.CommittedWithWarning,
        "rejected" => OperationStatus.Rejected,
        "failed" => OperationStatus.Failed,
        "recovering" => OperationStatus.Recovering,
        _ => throw new InvalidDataException("The operation status is unsupported."),
    };

    private static string ToWireName(FailureCode failureCode) => failureCode switch
    {
        FailureCode.None => "none",
        FailureCode.ActivityNotFound => "activity-not-found",
        FailureCode.ActivityAlreadyExists => "activity-already-exists",
        FailureCode.CapabilityDenied => "capability-denied",
        FailureCode.DeadlineExpired => "deadline-expired",
        FailureCode.DescriptorRejected => "descriptor-rejected",
        FailureCode.AdapterUnavailable => "adapter-unavailable",
        FailureCode.OperationIdConflict => "operation-id-conflict",
        FailureCode.OperationInProgress => "operation-in-progress",
        FailureCode.ProtocolIncompatible => "protocol-incompatible",
        FailureCode.PeerUnavailable => "peer-unavailable",
        FailureCode.AcknowledgementLost => "acknowledgement-lost",
        FailureCode.SourceCleanupFailed => "source-cleanup-failed",
        FailureCode.RevisionConflict => "revision-conflict",
        FailureCode.ReservationConflict => "reservation-conflict",
        FailureCode.ReservationExpired => "reservation-expired",
        FailureCode.DecisionConflict => "decision-conflict",
        FailureCode.UndoUnavailable => "undo-unavailable",
        FailureCode.UndoCapsuleInvalid => "undo-capsule-invalid",
        FailureCode.UndoCapsuleExpired => "undo-capsule-expired",
        FailureCode.UndoCapsuleNotFound => "undo-capsule-not-found",
        FailureCode.UndoCapsuleConsumed => "undo-capsule-consumed",
        FailureCode.InternalFailure => "internal-failure",
        _ => throw new ArgumentOutOfRangeException(nameof(failureCode)),
    };

    private static FailureCode ParseFailureCode(string value) => value switch
    {
        "none" => FailureCode.None,
        "activity-not-found" => FailureCode.ActivityNotFound,
        "activity-already-exists" => FailureCode.ActivityAlreadyExists,
        "capability-denied" => FailureCode.CapabilityDenied,
        "deadline-expired" => FailureCode.DeadlineExpired,
        "descriptor-rejected" => FailureCode.DescriptorRejected,
        "adapter-unavailable" => FailureCode.AdapterUnavailable,
        "operation-id-conflict" => FailureCode.OperationIdConflict,
        "operation-in-progress" => FailureCode.OperationInProgress,
        "protocol-incompatible" => FailureCode.ProtocolIncompatible,
        "peer-unavailable" => FailureCode.PeerUnavailable,
        "acknowledgement-lost" => FailureCode.AcknowledgementLost,
        "source-cleanup-failed" => FailureCode.SourceCleanupFailed,
        "revision-conflict" => FailureCode.RevisionConflict,
        "reservation-conflict" => FailureCode.ReservationConflict,
        "reservation-expired" => FailureCode.ReservationExpired,
        "decision-conflict" => FailureCode.DecisionConflict,
        "undo-unavailable" => FailureCode.UndoUnavailable,
        "undo-capsule-invalid" => FailureCode.UndoCapsuleInvalid,
        "undo-capsule-expired" => FailureCode.UndoCapsuleExpired,
        "undo-capsule-not-found" => FailureCode.UndoCapsuleNotFound,
        "undo-capsule-consumed" => FailureCode.UndoCapsuleConsumed,
        "internal-failure" => FailureCode.InternalFailure,
        _ => throw new InvalidDataException("The failure code is unsupported."),
    };
}
