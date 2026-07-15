using System.Security.Cryptography;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Transport;

public static class ActivityControlMessageCodec
{
    private static readonly TimeSpan ReceiptTimeToLive = TimeSpan.FromSeconds(30);

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

    private static DateTimeOffset RequireDateTimeOffset(
        JsonElement parent,
        string name)
    {
        JsonElement value = Require(parent, name, JsonValueKind.String);
        return value.TryGetDateTimeOffset(out DateTimeOffset parsed)
            ? parsed
            : throw new InvalidDataException($"The '{name}' field is not a timestamp.");
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
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind),
            kind,
            "Only Activity transfer operations have a wire representation here."),
    };

    private static OperationKind ParseOperationKind(string value) => value switch
    {
        "handoff" => OperationKind.Handoff,
        "move" => OperationKind.Move,
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
        "internal-failure" => FailureCode.InternalFailure,
        _ => throw new InvalidDataException("The failure code is unsupported."),
    };
}
