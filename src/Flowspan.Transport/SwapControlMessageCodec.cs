using System.Security.Cryptography;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Transport;

public static class SwapControlMessageCodec
{
    private static readonly TimeSpan ResultTimeToLive = TimeSpan.FromSeconds(30);

    internal static TimeSpan DecisionAcknowledgementTimeout => ResultTimeToLive;

    public static ControlMessage CreateSnapshotQuery(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        SwapActivitySnapshotQuery query,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        string body = JsonSerializer.Serialize(new
        {
            operationId = query.Context.OperationId.ToString(),
            deadline = query.Context.Deadline,
            targetDeviceId = query.TargetDeviceId.ToString(),
            activityId = query.ActivityId.ToString(),
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.ActivitySwapSnapshot,
            Guid.NewGuid(),
            query.Context.CorrelationId,
            senderDeviceId,
            sentAt,
            DeadlineTimeToLive(query.Context.Deadline, sentAt, "Swap snapshot"),
            body);
    }

    public static SwapActivitySnapshotQuery DecodeSnapshotQuery(
        ControlMessage message,
        DeviceId expectedTargetDeviceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedTargetDeviceId);
        RequireType(message, ControlMessageType.ActivitySwapSnapshot);
        try
        {
            JsonElement root = message.Body;
            RequireOnly(root, "operationId", "deadline", "targetDeviceId", "activityId");
            DateTimeOffset deadline = RequireUtc(root, "deadline");
            ValidateDeadline(message, deadline, "Swap snapshot");
            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            if (targetDeviceId != expectedTargetDeviceId)
            {
                throw new InvalidDataException(
                    "The Swap snapshot query targets another Device.");
            }

            return SwapActivitySnapshotQuery.Create(
                OperationContext.Create(
                    OperationId.Parse(RequireString(root, "operationId")),
                    message.CorrelationId,
                    deadline),
                targetDeviceId,
                ActivityId.Parse(RequireString(root, "activityId")));
        }
        catch (Exception exception) when (IsMalformedValue(exception))
        {
            throw new InvalidDataException(
                "The Swap snapshot query body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateSnapshotResult(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        SwapActivitySnapshotResult result,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(result);
        if (result.TargetDeviceId != senderDeviceId)
        {
            throw new ArgumentException(
                "A Swap snapshot result must be sent by its target Device.",
                nameof(senderDeviceId));
        }

        ValidateSnapshotResult(result);
        string body = JsonSerializer.Serialize(new
        {
            operationId = result.OperationId.ToString(),
            requestingDeviceId = result.RequestingDeviceId.ToString(),
            targetDeviceId = result.TargetDeviceId.ToString(),
            requestedActivityId = result.RequestedActivityId.ToString(),
            failureCode = ToWireName(result.FailureCode),
            activity = result.Activity is { } activity
                ? ToWireActivity(activity)
                : null,
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.ActivitySwapSnapshotResult,
            Guid.NewGuid(),
            result.CorrelationId,
            senderDeviceId,
            sentAt,
            ResultTimeToLive,
            body);
    }

    public static SwapActivitySnapshotResult DecodeSnapshotResult(
        ControlMessage message,
        DeviceId expectedRecipientDeviceId,
        SwapActivitySnapshotQuery expectedQuery)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedRecipientDeviceId);
        ArgumentNullException.ThrowIfNull(expectedQuery);
        RequireType(message, ControlMessageType.ActivitySwapSnapshotResult);
        if (message.CorrelationId != expectedQuery.Context.CorrelationId)
        {
            throw new InvalidDataException(
                "The Swap snapshot result correlation does not match the pending query.");
        }

        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "operationId",
                "requestingDeviceId",
                "targetDeviceId",
                "requestedActivityId",
                "failureCode",
                "activity");
            OperationId operationId = OperationId.Parse(
                RequireString(root, "operationId"));
            DeviceId requestingDeviceId = DeviceId.Parse(
                RequireString(root, "requestingDeviceId"));
            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            ActivityId requestedActivityId = ActivityId.Parse(
                RequireString(root, "requestedActivityId"));
            if (operationId != expectedQuery.Context.OperationId
                || requestingDeviceId != expectedRecipientDeviceId
                || targetDeviceId != expectedQuery.TargetDeviceId
                || targetDeviceId != message.SenderDeviceId
                || requestedActivityId != expectedQuery.ActivityId)
            {
                throw new InvalidDataException(
                    "The Swap snapshot result does not match the pending query.");
            }

            FailureCode failureCode = ParseFailureCode(
                RequireString(root, "failureCode"));
            JsonElement activityElement = RequireAny(root, "activity");
            SwapActivitySnapshotResult result;
            if (failureCode == FailureCode.None)
            {
                if (activityElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        "A successful Swap snapshot result must contain an Activity.");
                }

                ActivityInstance activity = DecodeActivity(activityElement);
                result = SwapActivitySnapshotResult.Success(
                    requestingDeviceId,
                    expectedQuery,
                    activity);
            }
            else
            {
                if (activityElement.ValueKind != JsonValueKind.Null)
                {
                    throw new InvalidDataException(
                        "A rejected Swap snapshot result must not contain an Activity.");
                }

                result = SwapActivitySnapshotResult.Rejected(
                    requestingDeviceId,
                    expectedQuery,
                    failureCode);
            }

            ValidateSnapshotResult(result);
            return result;
        }
        catch (Exception exception) when (IsMalformedValue(exception))
        {
            throw new InvalidDataException(
                "The Swap snapshot result body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreatePrepare(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        SwapPrepareCommand command,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(command);
        SwapReservation request = CreateReservation(command);
        string body = JsonSerializer.Serialize(new
        {
            operationId = command.OperationId.ToString(),
            targetDeviceId = command.OriginalActivity.Placement.DeviceId.ToString(),
            reservationToken = command.ReservationToken.ToString(),
            expiresAt = command.ExpiresAt,
            requestDigest = request.RequestDigest,
            originalActivity = ToWireActivity(command.OriginalActivity),
            incomingActivity = ToWireActivity(command.IncomingActivity),
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.ActivitySwapPrepare,
            Guid.NewGuid(),
            command.CorrelationId,
            senderDeviceId,
            sentAt,
            DeadlineTimeToLive(command.ExpiresAt, sentAt, "Swap Prepare"),
            body);
    }

    public static SwapPrepareCommand DecodePrepare(
        ControlMessage message,
        DeviceId expectedTargetDeviceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedTargetDeviceId);
        RequireType(message, ControlMessageType.ActivitySwapPrepare);
        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "operationId",
                "targetDeviceId",
                "reservationToken",
                "expiresAt",
                "requestDigest",
                "originalActivity",
                "incomingActivity");
            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            if (targetDeviceId != expectedTargetDeviceId)
            {
                throw new InvalidDataException("The Swap Prepare targets another Device.");
            }

            DateTimeOffset expiresAt = RequireUtc(root, "expiresAt");
            ValidateDeadline(message, expiresAt, "Swap Prepare");
            var command = new SwapPrepareCommand(
                OperationId.Parse(RequireString(root, "operationId")),
                message.CorrelationId,
                SwapReservationToken.From(
                    RequireGuid(root, "reservationToken")),
                DecodeActivity(Require(root, "originalActivity", JsonValueKind.Object)),
                DecodeActivity(Require(root, "incomingActivity", JsonValueKind.Object)),
                expiresAt);
            if (command.OriginalActivity.Placement.DeviceId != targetDeviceId)
            {
                throw new InvalidDataException(
                    "The Swap Prepare original Activity belongs to another Device.");
            }

            RequireDigestMatch(
                RequireDigest(root, "requestDigest"),
                CreateReservation(command).RequestDigest,
                "The Swap Prepare request digest does not match.");
            return command;
        }
        catch (Exception exception) when (IsMalformedValue(exception))
        {
            throw new InvalidDataException("The Swap Prepare body is malformed.", exception);
        }
    }

    public static ControlMessage CreatePrepareResult(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        DeviceId requestingDeviceId,
        SwapPrepareCommand command,
        SwapPrepareResult result,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);
        if (command.OriginalActivity.Placement.DeviceId != senderDeviceId)
        {
            throw new ArgumentException(
                "A Swap Prepare result must be sent by its target Device.",
                nameof(senderDeviceId));
        }

        ValidatePrepareResult(command, result);
        string body = JsonSerializer.Serialize(new
        {
            operationId = command.OperationId.ToString(),
            requestingDeviceId = requestingDeviceId.ToString(),
            targetDeviceId = senderDeviceId.ToString(),
            prepared = result.Prepared,
            failureCode = ToWireName(result.FailureCode),
            reservationToken = result.ReservationToken?.ToString(),
            requestDigest = CreateReservation(command).RequestDigest,
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.ActivitySwapPrepareResult,
            Guid.NewGuid(),
            command.CorrelationId,
            senderDeviceId,
            sentAt,
            ResultTimeToLive,
            body);
    }

    public static SwapPrepareResult DecodePrepareResult(
        ControlMessage message,
        DeviceId expectedRecipientDeviceId,
        SwapPrepareCommand expectedCommand)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedRecipientDeviceId);
        ArgumentNullException.ThrowIfNull(expectedCommand);
        RequireType(message, ControlMessageType.ActivitySwapPrepareResult);
        if (message.CorrelationId != expectedCommand.CorrelationId)
        {
            throw new InvalidDataException(
                "The Swap Prepare result correlation does not match the pending request.");
        }

        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "operationId",
                "requestingDeviceId",
                "targetDeviceId",
                "prepared",
                "failureCode",
                "reservationToken",
                "requestDigest");
            if (OperationId.Parse(RequireString(root, "operationId"))
                    != expectedCommand.OperationId
                || DeviceId.Parse(RequireString(root, "requestingDeviceId"))
                    != expectedRecipientDeviceId
                || DeviceId.Parse(RequireString(root, "targetDeviceId"))
                    != message.SenderDeviceId
                || message.SenderDeviceId
                    != expectedCommand.OriginalActivity.Placement.DeviceId)
            {
                throw new InvalidDataException(
                    "The Swap Prepare result does not match the pending request.");
            }

            RequireDigestMatch(
                RequireDigest(root, "requestDigest"),
                CreateReservation(expectedCommand).RequestDigest,
                "The Swap Prepare result request digest does not match.");
            bool prepared = RequireBoolean(root, "prepared");
            FailureCode failureCode = ParseFailureCode(
                RequireString(root, "failureCode"));
            string? tokenValue = ReadOptionalString(root, "reservationToken");
            SwapPrepareResult result = prepared
                ? SwapPrepareResult.Success(
                    SwapReservationToken.From(ParseGuid(tokenValue, "reservationToken")))
                : SwapPrepareResult.Rejected(failureCode);
            ValidatePrepareResult(expectedCommand, result);
            return result;
        }
        catch (Exception exception) when (IsMalformedValue(exception))
        {
            throw new InvalidDataException(
                "The Swap Prepare result body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateDecision(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        CorrelationId correlationId,
        DeviceId targetDeviceId,
        SwapDecision decision,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ArgumentNullException.ThrowIfNull(decision);
        if (!decision.TryGetReservationToken(targetDeviceId, out _))
        {
            throw new ArgumentException(
                "A Swap decision does not include its target Device.",
                nameof(targetDeviceId));
        }

        string body = JsonSerializer.Serialize(new
        {
            operationId = decision.OperationId.ToString(),
            targetDeviceId = targetDeviceId.ToString(),
            outcome = ToWireName(decision.Outcome),
            decidedAt = decision.DecidedAt,
            failureCode = ToWireName(decision.FailureCode),
            decisionDigest = decision.Digest,
            participants = decision.Participants.Select(static participant => new
            {
                deviceId = participant.DeviceId.ToString(),
                reservationToken = participant.ReservationToken.ToString(),
            }),
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.ActivitySwapDecision,
            Guid.NewGuid(),
            correlationId,
            senderDeviceId,
            sentAt,
            ResultTimeToLive,
            body);
    }

    public static SwapDecision DecodeDecision(
        ControlMessage message,
        DeviceId expectedTargetDeviceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedTargetDeviceId);
        RequireType(message, ControlMessageType.ActivitySwapDecision);
        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "operationId",
                "targetDeviceId",
                "outcome",
                "decidedAt",
                "failureCode",
                "decisionDigest",
                "participants");
            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            if (targetDeviceId != expectedTargetDeviceId)
            {
                throw new InvalidDataException("The Swap decision targets another Device.");
            }

            JsonElement participantsElement = Require(
                root,
                "participants",
                JsonValueKind.Array);
            if (participantsElement.GetArrayLength() != 2)
            {
                throw new InvalidDataException(
                    "A Swap decision must contain exactly two participants.");
            }

            SwapDecisionParticipant[] participants = participantsElement
                .EnumerateArray()
                .Select(static participant =>
                {
                    RequireOnly(participant, "deviceId", "reservationToken");
                    return SwapDecisionParticipant.Create(
                        DeviceId.Parse(RequireString(participant, "deviceId")),
                        SwapReservationToken.From(
                            RequireGuid(participant, "reservationToken")));
                })
                .ToArray();
            SwapDecision decision = SwapDecision.Create(
                OperationId.Parse(RequireString(root, "operationId")),
                ParseDecisionOutcome(RequireString(root, "outcome")),
                RequireUtc(root, "decidedAt"),
                participants,
                ParseFailureCode(RequireString(root, "failureCode")));
            if (!decision.TryGetReservationToken(targetDeviceId, out _))
            {
                throw new InvalidDataException(
                    "The Swap decision does not bind the target Device.");
            }

            RequireDigestMatch(
                RequireDigest(root, "decisionDigest"),
                decision.Digest,
                "The Swap decision digest does not match.");
            return decision;
        }
        catch (Exception exception) when (IsMalformedValue(exception))
        {
            throw new InvalidDataException("The Swap decision body is malformed.", exception);
        }
    }

    public static ControlMessage CreateDecisionResult(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        DeviceId requestingDeviceId,
        CorrelationId correlationId,
        SwapDecision decision,
        SwapApplyResult result,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(result);
        if (!decision.TryGetReservationToken(senderDeviceId, out _))
        {
            throw new ArgumentException(
                "A Swap decision result must be sent by a decision participant.",
                nameof(senderDeviceId));
        }

        ValidateApplyResult(decision, result);
        string body = JsonSerializer.Serialize(new
        {
            operationId = decision.OperationId.ToString(),
            requestingDeviceId = requestingDeviceId.ToString(),
            targetDeviceId = senderDeviceId.ToString(),
            applied = result.Applied,
            failureCode = ToWireName(result.FailureCode),
            phase = result.Phase is { } phase ? ToWireName(phase) : null,
            decisionDigest = decision.Digest,
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.ActivitySwapDecisionResult,
            Guid.NewGuid(),
            correlationId,
            senderDeviceId,
            sentAt,
            ResultTimeToLive,
            body);
    }

    public static SwapApplyResult DecodeDecisionResult(
        ControlMessage message,
        DeviceId expectedRecipientDeviceId,
        CorrelationId expectedCorrelationId,
        DeviceId expectedTargetDeviceId,
        SwapDecision expectedDecision)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedRecipientDeviceId);
        ArgumentNullException.ThrowIfNull(expectedCorrelationId);
        ArgumentNullException.ThrowIfNull(expectedTargetDeviceId);
        ArgumentNullException.ThrowIfNull(expectedDecision);
        RequireType(message, ControlMessageType.ActivitySwapDecisionResult);
        if (message.CorrelationId != expectedCorrelationId)
        {
            throw new InvalidDataException(
                "The Swap decision result correlation does not match the pending request.");
        }

        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "operationId",
                "requestingDeviceId",
                "targetDeviceId",
                "applied",
                "failureCode",
                "phase",
                "decisionDigest");
            if (OperationId.Parse(RequireString(root, "operationId"))
                    != expectedDecision.OperationId
                || DeviceId.Parse(RequireString(root, "requestingDeviceId"))
                    != expectedRecipientDeviceId
                || DeviceId.Parse(RequireString(root, "targetDeviceId"))
                    != expectedTargetDeviceId
                || message.SenderDeviceId != expectedTargetDeviceId)
            {
                throw new InvalidDataException(
                    "The Swap decision result does not match the pending request.");
            }

            RequireDigestMatch(
                RequireDigest(root, "decisionDigest"),
                expectedDecision.Digest,
                "The Swap decision result digest does not match.");
            bool applied = RequireBoolean(root, "applied");
            FailureCode failureCode = ParseFailureCode(
                RequireString(root, "failureCode"));
            string? phaseValue = ReadOptionalString(root, "phase");
            SwapApplyResult result = applied
                ? SwapApplyResult.Success(ParseReservationPhase(phaseValue))
                : SwapApplyResult.Rejected(failureCode);
            ValidateApplyResult(expectedDecision, result);
            return result;
        }
        catch (Exception exception) when (IsMalformedValue(exception))
        {
            throw new InvalidDataException(
                "The Swap decision result body is malformed.",
                exception);
        }
    }

    private static SwapReservation CreateReservation(SwapPrepareCommand command) =>
        SwapReservation.Prepare(
            command.OperationId,
            command.ReservationToken,
            command.OriginalActivity,
            command.IncomingActivity,
            command.ExpiresAt);

    private static object ToWireActivity(ActivityInstance activity) => new
    {
        id = activity.Descriptor.Id.ToString(),
        kind = activity.Descriptor.Kind.Value,
        originDeviceId = activity.Descriptor.OriginDeviceId.ToString(),
        title = activity.Descriptor.Title,
        payloadJson = activity.Descriptor.PayloadJson,
        payloadDigest = activity.Descriptor.PayloadDigest,
        descriptorDigest = activity.Descriptor.DescriptorDigest,
        sensitivity = ToWireName(activity.Descriptor.Sensitivity),
        placementDeviceId = activity.Placement.DeviceId.ToString(),
        placementSlot = activity.Placement.Slot,
        revision = activity.Revision,
        lifecycle = ToWireName(activity.Lifecycle),
    };

    private static ActivityInstance DecodeActivity(JsonElement value)
    {
        RequireOnly(
            value,
            "id",
            "kind",
            "originDeviceId",
            "title",
            "payloadJson",
            "payloadDigest",
            "descriptorDigest",
            "sensitivity",
            "placementDeviceId",
            "placementSlot",
            "revision",
            "lifecycle");
        if (RequireString(value, "lifecycle") != "active")
        {
            throw new InvalidDataException("Only an active Activity can cross Swap control.");
        }

        string claimedPayloadDigest = RequireDigest(value, "payloadDigest");
        string claimedDescriptorDigest = RequireDigest(value, "descriptorDigest");
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse(RequireString(value, "id")),
            ActivityKind.Parse(RequireString(value, "kind")),
            DeviceId.Parse(RequireString(value, "originDeviceId")),
            RequireString(value, "title"),
            RequireString(value, "payloadJson"),
            ParseSensitivity(RequireString(value, "sensitivity")));
        RequireDigestMatch(
            claimedPayloadDigest,
            descriptor.PayloadDigest,
            "The Swap Activity payload digest does not match.");
        RequireDigestMatch(
            claimedDescriptorDigest,
            descriptor.DescriptorDigest,
            "The Swap Activity descriptor digest does not match.");
        long revision = RequireInt64(value, "revision");
        if (revision < 1)
        {
            throw new InvalidDataException("A Swap Activity revision must be positive.");
        }

        return ActivityInstance.Active(
            descriptor,
            ActivityPlacement.On(
                DeviceId.Parse(RequireString(value, "placementDeviceId")),
                RequireString(value, "placementSlot")),
            revision);
    }

    private static void ValidateSnapshotResult(SwapActivitySnapshotResult result)
    {
        if (result.IsSuccess != (result.Activity is not null))
        {
            throw new InvalidDataException(
                "A successful Swap snapshot requires an Activity and a rejection must omit it.");
        }

        if (result.Activity is { } activity
            && (activity.Descriptor.Id != result.RequestedActivityId
                || activity.Placement.DeviceId != result.TargetDeviceId
                || activity.Lifecycle != ActivityLifecycle.Active
                || activity.Descriptor.Sensitivity != ActivitySensitivity.Normal))
        {
            throw new InvalidDataException(
                "The Swap snapshot Activity does not match its result binding.");
        }
    }

    private static void ValidatePrepareResult(
        SwapPrepareCommand command,
        SwapPrepareResult result)
    {
        if (result.Prepared)
        {
            if (result.FailureCode != FailureCode.None
                || result.ReservationToken != command.ReservationToken)
            {
                throw new InvalidDataException(
                    "A prepared Swap result must return the exact reservation token.");
            }
        }
        else if (result.FailureCode == FailureCode.None
                 || result.ReservationToken is not null)
        {
            throw new InvalidDataException(
                "A rejected Swap Prepare result requires a failure and no token.");
        }
    }

    private static void ValidateApplyResult(
        SwapDecision decision,
        SwapApplyResult result)
    {
        if (result.Applied)
        {
            SwapReservationPhase expected = decision.Outcome == SwapDecisionOutcome.Commit
                ? SwapReservationPhase.Committed
                : SwapReservationPhase.Aborted;
            if (result.FailureCode != FailureCode.None || result.Phase != expected)
            {
                throw new InvalidDataException(
                    "An applied Swap decision result has the wrong terminal phase.");
            }
        }
        else if (result.FailureCode == FailureCode.None || result.Phase is not null)
        {
            throw new InvalidDataException(
                "A rejected Swap decision result requires a failure and no phase.");
        }
    }

    private static TimeSpan DeadlineTimeToLive(
        DateTimeOffset deadline,
        DateTimeOffset sentAt,
        string operation)
    {
        TimeSpan remaining = deadline - sentAt;
        if (deadline.Offset != TimeSpan.Zero
            || remaining <= TimeSpan.Zero
            || Math.Ceiling(remaining.TotalMilliseconds)
                > ControlMessage.MaximumTimeToLiveMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deadline),
                $"The {operation} deadline is outside the control envelope lifetime.");
        }

        return TimeSpan.FromMilliseconds(Math.Ceiling(remaining.TotalMilliseconds));
    }

    private static void ValidateDeadline(
        ControlMessage message,
        DateTimeOffset deadline,
        string operation)
    {
        DateTimeOffset envelopeExpiry = message.SentAt.AddMilliseconds(
            message.TimeToLiveMilliseconds);
        if (deadline <= message.SentAt || deadline > envelopeExpiry)
        {
            throw new InvalidDataException(
                $"The {operation} deadline is outside the authenticated envelope lifetime.");
        }
    }

    private static void RequireType(ControlMessage message, ControlMessageType type)
    {
        if (message.Type != type)
        {
            throw new InvalidDataException(
                $"The control message is not '{type}'.");
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

    private static JsonElement RequireAny(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            throw new InvalidDataException($"The required '{name}' field is missing.");
        }

        return value;
    }

    private static string RequireString(JsonElement parent, string name) =>
        Require(parent, name, JsonValueKind.String).GetString()
        ?? throw new InvalidDataException($"The '{name}' field is null.");

    private static string? ReadOptionalString(JsonElement parent, string name)
    {
        JsonElement value = RequireAny(parent, name);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString()
                ?? throw new InvalidDataException($"The '{name}' field is null."),
            _ => throw new InvalidDataException(
                $"The '{name}' field has the wrong type."),
        };
    }

    private static bool RequireBoolean(JsonElement parent, string name)
    {
        JsonElement value = RequireAny(parent, name);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"The '{name}' field is not Boolean.");
        }

        return value.GetBoolean();
    }

    private static long RequireInt64(JsonElement parent, string name)
    {
        JsonElement value = Require(parent, name, JsonValueKind.Number);
        return value.TryGetInt64(out long parsed)
            ? parsed
            : throw new InvalidDataException($"The '{name}' field is not an integer.");
    }

    private static Guid RequireGuid(JsonElement parent, string name) =>
        ParseGuid(RequireString(parent, name), name);

    private static Guid ParseGuid(string? value, string name) =>
        Guid.TryParseExact(value, "D", out Guid parsed) && parsed != Guid.Empty
            ? parsed
            : throw new InvalidDataException($"The '{name}' field is not a valid ID.");

    private static DateTimeOffset RequireUtc(JsonElement parent, string name)
    {
        JsonElement value = Require(parent, name, JsonValueKind.String);
        if (!value.TryGetDateTimeOffset(out DateTimeOffset parsed)
            || parsed.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException($"The '{name}' field is not a UTC timestamp.");
        }

        return parsed;
    }

    private static string RequireDigest(JsonElement parent, string name)
    {
        string value = RequireString(parent, name);
        if (value.Length != 64 || !value.All(char.IsAsciiHexDigit))
        {
            throw new InvalidDataException(
                $"The '{name}' field is not a 32-byte hexadecimal digest.");
        }

        return value.ToUpperInvariant();
    }

    private static void RequireDigestMatch(
        string claimed,
        string actual,
        string message)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(claimed),
                Convert.FromHexString(actual)))
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

    private static bool IsMalformedValue(Exception exception) => exception is
        ArgumentException
        or FormatException
        or JsonException
        or OverflowException;

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

    private static string ToWireName(ActivityLifecycle lifecycle) => lifecycle switch
    {
        ActivityLifecycle.Active => "active",
        ActivityLifecycle.Suspended => "suspended",
        ActivityLifecycle.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(lifecycle)),
    };

    private static string ToWireName(SwapDecisionOutcome outcome) => outcome switch
    {
        SwapDecisionOutcome.Commit => "commit",
        SwapDecisionOutcome.Abort => "abort",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    private static SwapDecisionOutcome ParseDecisionOutcome(string value) => value switch
    {
        "commit" => SwapDecisionOutcome.Commit,
        "abort" => SwapDecisionOutcome.Abort,
        _ => throw new InvalidDataException("The Swap decision outcome is unsupported."),
    };

    private static string ToWireName(SwapReservationPhase phase) => phase switch
    {
        SwapReservationPhase.Prepared => "prepared",
        SwapReservationPhase.Committed => "committed",
        SwapReservationPhase.Aborted => "aborted",
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };

    private static SwapReservationPhase ParseReservationPhase(string? value) => value switch
    {
        "prepared" => SwapReservationPhase.Prepared,
        "committed" => SwapReservationPhase.Committed,
        "aborted" => SwapReservationPhase.Aborted,
        _ => throw new InvalidDataException("The Swap reservation phase is unsupported."),
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
