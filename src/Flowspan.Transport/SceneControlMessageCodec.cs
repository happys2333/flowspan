using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Transport;

public static class SceneControlMessageCodec
{
    private static readonly TimeSpan ResultTimeToLive = TimeSpan.FromSeconds(30);

    public static ControlMessage CreateSourceLookupQuery(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        SceneSourceLookupQuery query,
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
            index = query.Index,
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.SceneSourceLookup,
            Guid.NewGuid(),
            query.Context.CorrelationId,
            senderDeviceId,
            sentAt,
            DeadlineTimeToLive(query.Context.Deadline, sentAt, "Scene source lookup"),
            body);
    }

    public static SceneSourceLookupQuery DecodeSourceLookupQuery(
        ControlMessage message,
        DeviceId expectedTargetDeviceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedTargetDeviceId);
        RequireType(message, ControlMessageType.SceneSourceLookup);
        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "operationId",
                "deadline",
                "targetDeviceId",
                "activityId",
                "index");
            DateTimeOffset deadline = RequireUtc(root, "deadline");
            ValidateDeadline(message, deadline, "Scene source lookup");
            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            if (targetDeviceId != expectedTargetDeviceId)
            {
                throw new InvalidDataException(
                    "The Scene source lookup targets another Device.");
            }

            return SceneSourceLookupQuery.Create(
                OperationContext.Create(
                    OperationId.Parse(RequireString(root, "operationId")),
                    message.CorrelationId,
                    deadline),
                targetDeviceId,
                ActivityId.Parse(RequireString(root, "activityId")),
                RequireInt32(root, "index"));
        }
        catch (Exception exception) when (IsMalformedValue(exception))
        {
            throw new InvalidDataException(
                "The Scene source lookup body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateSourceLookupResult(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        DeviceId requestingDeviceId,
        SceneSourceLookupQuery query,
        SceneSourceLookup result,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(result);
        if (senderDeviceId != query.TargetDeviceId)
        {
            throw new ArgumentException(
                "A Scene source lookup result must be sent by its target Device.",
                nameof(senderDeviceId));
        }

        if (result.Index != query.Index
            || result.ActivityId != query.ActivityId
            || result.Candidates.Any(candidate =>
                candidate.DeviceId != senderDeviceId))
        {
            throw new ArgumentException(
                "A Scene source lookup result must match its query and source Device.",
                nameof(result));
        }

        if (sentAt.ToUniversalTime() > query.Context.Deadline.ToUniversalTime())
        {
            throw new ArgumentOutOfRangeException(
                nameof(sentAt),
                "A Scene source lookup result cannot be sent after its query deadline.");
        }

        string body = JsonSerializer.Serialize(new
        {
            operationId = query.Context.OperationId.ToString(),
            queryDeadline = query.Context.Deadline,
            requestingDeviceId = requestingDeviceId.ToString(),
            targetDeviceId = query.TargetDeviceId.ToString(),
            activityId = result.ActivityId.ToString(),
            index = result.Index,
            status = ToWireName(result.Status),
            reason = ToWireName(result.Reason),
            candidates = result.Candidates.Select(static candidate => new
            {
                deviceId = candidate.DeviceId.ToString(),
                revision = candidate.Revision,
                descriptorDigest = candidate.DescriptorDigest,
                kind = candidate.Kind.Value,
                placementSlot = candidate.Placement.Slot,
            }),
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.SceneSourceLookupResult,
            Guid.NewGuid(),
            query.Context.CorrelationId,
            senderDeviceId,
            sentAt,
            ResultTimeToLive,
            body);
    }

    public static SceneSourceLookup DecodeSourceLookupResult(
        ControlMessage message,
        DeviceId expectedRecipientDeviceId,
        SceneSourceLookupQuery expectedQuery)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedRecipientDeviceId);
        ArgumentNullException.ThrowIfNull(expectedQuery);
        RequireType(message, ControlMessageType.SceneSourceLookupResult);
        if (message.CorrelationId != expectedQuery.Context.CorrelationId)
        {
            throw new InvalidDataException(
                "The Scene source lookup result correlation does not match its query.");
        }

        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "operationId",
                "queryDeadline",
                "requestingDeviceId",
                "targetDeviceId",
                "activityId",
                "index",
                "status",
                "reason",
                "candidates");
            OperationId operationId = OperationId.Parse(
                RequireString(root, "operationId"));
            DateTimeOffset queryDeadline = RequireUtc(root, "queryDeadline");
            DeviceId requestingDeviceId = DeviceId.Parse(
                RequireString(root, "requestingDeviceId"));
            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            ActivityId activityId = ActivityId.Parse(
                RequireString(root, "activityId"));
            int index = RequireInt32(root, "index");
            if (operationId != expectedQuery.Context.OperationId
                || queryDeadline != expectedQuery.Context.Deadline
                || requestingDeviceId != expectedRecipientDeviceId
                || targetDeviceId != expectedQuery.TargetDeviceId
                || targetDeviceId != message.SenderDeviceId
                || activityId != expectedQuery.ActivityId
                || index != expectedQuery.Index
                || message.SentAt > queryDeadline)
            {
                throw new InvalidDataException(
                    "The Scene source lookup result does not match its authenticated query.");
            }

            JsonElement candidatesElement = Require(
                root,
                "candidates",
                JsonValueKind.Array);
            var candidates = new List<SceneSourceSelection>();
            foreach (JsonElement candidateElement in candidatesElement.EnumerateArray())
            {
                if (candidates.Count == ScenePlan.MaximumActivities)
                {
                    throw new InvalidDataException(
                        "The Scene source lookup result exceeds the candidate bound.");
                }

                RequireOnly(
                    candidateElement,
                    "deviceId",
                    "revision",
                    "descriptorDigest",
                    "kind",
                    "placementSlot");
                DeviceId candidateDeviceId = DeviceId.Parse(
                    RequireString(candidateElement, "deviceId"));
                if (candidateDeviceId != targetDeviceId)
                {
                    throw new InvalidDataException(
                        "A Scene source candidate belongs to another Device.");
                }

                candidates.Add(SceneSourceSelection.Create(
                    index,
                    activityId,
                    RequireInt64(candidateElement, "revision"),
                    RequireString(candidateElement, "descriptorDigest"),
                    ActivityKind.Parse(RequireString(candidateElement, "kind")),
                    ActivityPlacement.On(
                        candidateDeviceId,
                        RequireString(candidateElement, "placementSlot"))));
            }

            SceneSourceLookupStatus status = ParseSourceLookupStatus(
                RequireString(root, "status"));
            SceneApplyItemReason reason = ParseReason(
                RequireString(root, "reason"));
            SceneSourceLookup result = status == SceneSourceLookupStatus.Unavailable
                ? SceneSourceLookup.Unavailable(index, activityId, reason)
                : SceneSourceLookup.FromObservation(
                    index,
                    activityId,
                    candidates,
                    isComplete: true);
            if (result.Status != status || result.Reason != reason)
            {
                throw new InvalidDataException(
                    "The Scene source lookup status does not match its candidates.");
            }

            return result;
        }
        catch (Exception exception) when (IsMalformedValue(exception))
        {
            throw new InvalidDataException(
                "The Scene source lookup result body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateExactSlotQuery(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        SceneExactSlotQuery query,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        string body = JsonSerializer.Serialize(new
        {
            operationId = query.Context.OperationId.ToString(),
            deadline = query.Context.Deadline,
            targetDeviceId = query.TargetDeviceId.ToString(),
            index = query.Source.Index,
            activityId = query.Item.ActivityId.ToString(),
            destinationSlot = query.Item.Placement.Slot,
            sourceDisposition = ToWireName(query.Item.SourceDisposition),
            conflictPolicy = ToWireName(query.Item.ConflictPolicy),
            source = ToWireSource(query.Source),
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.SceneSlotInspection,
            Guid.NewGuid(),
            query.Context.CorrelationId,
            senderDeviceId,
            sentAt,
            DeadlineTimeToLive(query.Context.Deadline, sentAt, "Scene exact-slot query"),
            body);
    }

    public static SceneExactSlotQuery DecodeExactSlotQuery(
        ControlMessage message,
        DeviceId expectedTargetDeviceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedTargetDeviceId);
        RequireType(message, ControlMessageType.SceneSlotInspection);
        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "operationId",
                "deadline",
                "targetDeviceId",
                "index",
                "activityId",
                "destinationSlot",
                "sourceDisposition",
                "conflictPolicy",
                "source");
            DateTimeOffset deadline = RequireUtc(root, "deadline");
            ValidateDeadline(message, deadline, "Scene exact-slot query");
            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            if (targetDeviceId != expectedTargetDeviceId)
            {
                throw new InvalidDataException(
                    "The Scene exact-slot query targets another Device.");
            }

            int index = RequireInt32(root, "index");
            ActivityId activityId = ActivityId.Parse(
                RequireString(root, "activityId"));
            SceneActivityPlan item = SceneActivityPlan.Place(
                activityId,
                ActivityPlacement.On(
                    targetDeviceId,
                    RequireString(root, "destinationSlot")),
                ParseSourceDisposition(RequireString(root, "sourceDisposition")),
                ParseConflictPolicy(RequireString(root, "conflictPolicy")));
            SceneSourceSelection source = DecodeSource(
                Require(root, "source", JsonValueKind.Object),
                index,
                activityId);
            return SceneExactSlotQuery.Create(
                OperationContext.Create(
                    OperationId.Parse(RequireString(root, "operationId")),
                    message.CorrelationId,
                    deadline),
                item,
                source);
        }
        catch (Exception exception) when (IsMalformedValue(exception))
        {
            throw new InvalidDataException(
                "The Scene exact-slot query body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateExactSlotResult(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        DeviceId requestingDeviceId,
        SceneExactSlotQuery query,
        SceneExactSlotInspection result,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(result);
        if (senderDeviceId != query.TargetDeviceId)
        {
            throw new ArgumentException(
                "A Scene exact-slot result must be sent by its target Device.",
                nameof(senderDeviceId));
        }

        if (sentAt.ToUniversalTime() > query.Context.Deadline.ToUniversalTime())
        {
            throw new ArgumentOutOfRangeException(
                nameof(sentAt),
                "A Scene exact-slot result cannot be sent after its query deadline.");
        }

        ValidateExactSlotResult(query, result);
        string body = JsonSerializer.Serialize(new
        {
            operationId = query.Context.OperationId.ToString(),
            queryDeadline = query.Context.Deadline,
            requestingDeviceId = requestingDeviceId.ToString(),
            targetDeviceId = query.TargetDeviceId.ToString(),
            index = query.Source.Index,
            activityId = query.Item.ActivityId.ToString(),
            destinationSlot = query.Item.Placement.Slot,
            sourceDisposition = ToWireName(query.Item.SourceDisposition),
            conflictPolicy = ToWireName(query.Item.ConflictPolicy),
            source = ToWireSource(query.Source),
            reason = ToWireName(result.Reason),
            occupancy = result.Occupancy is { } occupancy
                ? ToWireOccupancy(occupancy)
                : null,
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.SceneSlotInspectionResult,
            Guid.NewGuid(),
            query.Context.CorrelationId,
            senderDeviceId,
            sentAt,
            ResultTimeToLive,
            body);
    }

    public static SceneExactSlotInspection DecodeExactSlotResult(
        ControlMessage message,
        DeviceId expectedRecipientDeviceId,
        SceneExactSlotQuery expectedQuery)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedRecipientDeviceId);
        ArgumentNullException.ThrowIfNull(expectedQuery);
        RequireType(message, ControlMessageType.SceneSlotInspectionResult);
        if (message.CorrelationId != expectedQuery.Context.CorrelationId)
        {
            throw new InvalidDataException(
                "The Scene exact-slot result correlation does not match its query.");
        }

        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "operationId",
                "queryDeadline",
                "requestingDeviceId",
                "targetDeviceId",
                "index",
                "activityId",
                "destinationSlot",
                "sourceDisposition",
                "conflictPolicy",
                "source",
                "reason",
                "occupancy");
            OperationId operationId = OperationId.Parse(
                RequireString(root, "operationId"));
            DateTimeOffset queryDeadline = RequireUtc(root, "queryDeadline");
            DeviceId requestingDeviceId = DeviceId.Parse(
                RequireString(root, "requestingDeviceId"));
            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            int index = RequireInt32(root, "index");
            ActivityId activityId = ActivityId.Parse(
                RequireString(root, "activityId"));
            SceneSourceSelection source = DecodeSource(
                Require(root, "source", JsonValueKind.Object),
                index,
                activityId);
            if (operationId != expectedQuery.Context.OperationId
                || queryDeadline != expectedQuery.Context.Deadline
                || requestingDeviceId != expectedRecipientDeviceId
                || targetDeviceId != expectedQuery.TargetDeviceId
                || targetDeviceId != message.SenderDeviceId
                || index != expectedQuery.Source.Index
                || activityId != expectedQuery.Item.ActivityId
                || RequireString(root, "destinationSlot")
                    != expectedQuery.Item.Placement.Slot
                || ParseSourceDisposition(
                    RequireString(root, "sourceDisposition"))
                    != expectedQuery.Item.SourceDisposition
                || ParseConflictPolicy(RequireString(root, "conflictPolicy"))
                    != expectedQuery.Item.ConflictPolicy
                || source != expectedQuery.Source
                || message.SentAt > queryDeadline)
            {
                throw new InvalidDataException(
                    "The Scene exact-slot result does not match its authenticated query.");
            }

            SceneApplyItemReason reason = ParseReason(
                RequireString(root, "reason"));
            JsonElement occupancyElement = RequireAny(root, "occupancy");
            SceneExactSlotInspection result;
            if (reason == SceneApplyItemReason.None)
            {
                if (occupancyElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        "A successful Scene exact-slot result requires occupancy evidence.");
                }

                result = SceneExactSlotInspection.Observed(
                    DecodeOccupancy(occupancyElement, expectedQuery));
            }
            else
            {
                if (occupancyElement.ValueKind != JsonValueKind.Null)
                {
                    throw new InvalidDataException(
                        "A blocked Scene exact-slot result cannot contain occupancy evidence.");
                }

                result = SceneExactSlotInspection.Blocked(reason);
            }

            ValidateExactSlotResult(expectedQuery, result);
            return result;
        }
        catch (Exception exception) when (IsMalformedValue(exception))
        {
            throw new InvalidDataException(
                "The Scene exact-slot result body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateChildInstruction(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        SceneRemoteChildInstruction instruction,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(instruction);
        if (senderDeviceId != instruction.CoordinatorDeviceId)
        {
            throw new ArgumentException(
                "A remote Scene child instruction must be sent by its coordinator.",
                nameof(senderDeviceId));
        }

        SceneApplyItemPreview item = instruction.Item;
        string body = JsonSerializer.Serialize(new
        {
            coordinatorDeviceId = instruction.CoordinatorDeviceId.ToString(),
            sourceDeviceId = instruction.SourceDeviceId.ToString(),
            targetDeviceId = instruction.TargetDeviceId.ToString(),
            sceneId = instruction.SceneId.ToString(),
            sceneRevision = instruction.SceneRevision,
            sceneDigest = instruction.SceneDigest,
            previewFingerprint = instruction.PreviewFingerprint,
            parentOperationId = instruction.ParentOperationId.ToString(),
            parentCorrelationId = instruction.ParentCorrelationId.ToString(),
            acceptedAt = instruction.AcceptedAt,
            deadline = instruction.Deadline,
            index = item.Index,
            activityId = item.ActivityId.ToString(),
            destinationSlot = item.Destination.Slot,
            sourceDisposition = ToWireName(item.SourceDisposition),
            conflictPolicy = ToWireName(item.ConflictPolicy),
            childOperationId = item.ChildOperationId.ToString(),
            childCorrelationId = item.ChildCorrelationId.ToString(),
            action = ToWireName(item.Action),
            source = ToWireSource(item.Source!),
            target = item.ReplaceTarget is { } target
                ? ToWireTarget(target)
                : null,
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.SceneChildOperation,
            Guid.NewGuid(),
            item.ChildCorrelationId,
            senderDeviceId,
            sentAt,
            DeadlineTimeToLive(
                instruction.Deadline,
                sentAt,
                "remote Scene child instruction"),
            body);
    }

    public static SceneRemoteChildInstruction DecodeChildInstruction(
        ControlMessage message,
        DeviceId expectedSourceDeviceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedSourceDeviceId);
        RequireType(message, ControlMessageType.SceneChildOperation);
        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "coordinatorDeviceId",
                "sourceDeviceId",
                "targetDeviceId",
                "sceneId",
                "sceneRevision",
                "sceneDigest",
                "previewFingerprint",
                "parentOperationId",
                "parentCorrelationId",
                "acceptedAt",
                "deadline",
                "index",
                "activityId",
                "destinationSlot",
                "sourceDisposition",
                "conflictPolicy",
                "childOperationId",
                "childCorrelationId",
                "action",
                "source",
                "target");
            DeviceId coordinatorDeviceId = DeviceId.Parse(
                RequireString(root, "coordinatorDeviceId"));
            DeviceId sourceDeviceId = DeviceId.Parse(
                RequireString(root, "sourceDeviceId"));
            DeviceId targetDeviceId = DeviceId.Parse(
                RequireString(root, "targetDeviceId"));
            CorrelationId childCorrelationId = CorrelationId.Parse(
                RequireString(root, "childCorrelationId"));
            DateTimeOffset acceptedAt = RequireUtc(root, "acceptedAt");
            DateTimeOffset deadline = RequireUtc(root, "deadline");
            ValidateDeadline(message, deadline, "remote Scene child instruction");
            if (coordinatorDeviceId != message.SenderDeviceId
                || sourceDeviceId != expectedSourceDeviceId
                || childCorrelationId != message.CorrelationId)
            {
                throw new InvalidDataException(
                    "The remote Scene child participants or correlation do not match the authenticated envelope.");
            }

            int index = RequireInt32(root, "index");
            ActivityId activityId = ActivityId.Parse(
                RequireString(root, "activityId"));
            SceneSourceSelection source = DecodeSource(
                Require(root, "source", JsonValueKind.Object),
                index,
                activityId);
            if (source.DeviceId != sourceDeviceId)
            {
                throw new InvalidDataException(
                    "The remote Scene child source snapshot belongs to another Device.");
            }

            SceneActivityPlan plan = SceneActivityPlan.Place(
                activityId,
                ActivityPlacement.On(
                    targetDeviceId,
                    RequireString(root, "destinationSlot")),
                ParseSourceDisposition(RequireString(root, "sourceDisposition")),
                ParseConflictPolicy(RequireString(root, "conflictPolicy")));
            OperationId childOperationId = OperationId.Parse(
                RequireString(root, "childOperationId"));
            SceneApplyAction action = ParseAction(
                RequireString(root, "action"));
            JsonElement targetElement = RequireAny(root, "target");
            SceneApplyItemPreview item;
            if (action == SceneApplyAction.Replace)
            {
                if (targetElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        "A remote Scene Replace instruction requires exact target evidence.");
                }

                item = SceneApplyItemPreview.Replace(
                    plan,
                    source,
                    DecodeTarget(targetElement, plan.Placement),
                    childOperationId,
                    childCorrelationId);
            }
            else
            {
                if (targetElement.ValueKind != JsonValueKind.Null)
                {
                    throw new InvalidDataException(
                        "A remote Scene transfer instruction cannot contain Replace target evidence.");
                }

                item = SceneApplyItemPreview.TransferToEmpty(
                    plan,
                    source,
                    childOperationId,
                    childCorrelationId);
                if (item.Action != action)
                {
                    throw new InvalidDataException(
                        "The remote Scene child action conflicts with its source disposition.");
                }
            }

            SceneRemoteChildInstruction instruction =
                SceneRemoteChildInstruction.Create(
                    coordinatorDeviceId,
                    SceneId.Parse(RequireString(root, "sceneId")),
                    RequireInt64(root, "sceneRevision"),
                    RequireString(root, "sceneDigest"),
                    RequireString(root, "previewFingerprint"),
                    OperationId.Parse(RequireString(root, "parentOperationId")),
                    CorrelationId.Parse(RequireString(root, "parentCorrelationId")),
                    acceptedAt,
                    item);
            if (instruction.Deadline != deadline)
            {
                throw new InvalidDataException(
                    "The remote Scene child deadline is not derived from its accepted time.");
            }

            return instruction;
        }
        catch (Exception exception) when (IsMalformedValue(exception))
        {
            throw new InvalidDataException(
                "The remote Scene child instruction body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateChildResult(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        DeviceId requestingDeviceId,
        SceneRemoteChildInstruction instruction,
        SceneActivityOperationResult result,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(result);
        if (senderDeviceId != instruction.SourceDeviceId
            || requestingDeviceId != instruction.CoordinatorDeviceId)
        {
            throw new ArgumentException(
                "A remote Scene child result participants must match its instruction.",
                nameof(senderDeviceId));
        }

        ValidateChildResult(instruction, result);
        if (sentAt.ToUniversalTime() < result.Receipt.OccurredAt.ToUniversalTime())
        {
            throw new ArgumentOutOfRangeException(
                nameof(sentAt),
                "A remote Scene child result cannot be sent before it occurred.");
        }

        SceneApplyItemPreview item = instruction.Item;
        string body = JsonSerializer.Serialize(new
        {
            coordinatorDeviceId = instruction.CoordinatorDeviceId.ToString(),
            sourceDeviceId = instruction.SourceDeviceId.ToString(),
            targetDeviceId = instruction.TargetDeviceId.ToString(),
            sceneId = instruction.SceneId.ToString(),
            sceneRevision = instruction.SceneRevision,
            sceneDigest = instruction.SceneDigest,
            previewFingerprint = instruction.PreviewFingerprint,
            parentOperationId = instruction.ParentOperationId.ToString(),
            parentCorrelationId = instruction.ParentCorrelationId.ToString(),
            acceptedAt = instruction.AcceptedAt,
            deadline = instruction.Deadline,
            index = item.Index,
            activityId = item.ActivityId.ToString(),
            childOperationId = item.ChildOperationId.ToString(),
            childCorrelationId = item.ChildCorrelationId.ToString(),
            action = ToWireName(item.Action),
            receipt = ToWireReceipt(result.Receipt),
            undoCapsule = result.UndoCapsule is { } undo
                ? ToWireUndo(undo)
                : null,
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.SceneChildOperationResult,
            Guid.NewGuid(),
            item.ChildCorrelationId,
            senderDeviceId,
            sentAt,
            ResultTimeToLive,
            body);
    }

    public static SceneActivityOperationResult DecodeChildResult(
        ControlMessage message,
        DeviceId expectedRecipientDeviceId,
        SceneRemoteChildInstruction expectedInstruction)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedRecipientDeviceId);
        ArgumentNullException.ThrowIfNull(expectedInstruction);
        RequireType(message, ControlMessageType.SceneChildOperationResult);
        if (message.CorrelationId
            != expectedInstruction.Item.ChildCorrelationId)
        {
            throw new InvalidDataException(
                "The remote Scene child result correlation does not match its instruction.");
        }

        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "coordinatorDeviceId",
                "sourceDeviceId",
                "targetDeviceId",
                "sceneId",
                "sceneRevision",
                "sceneDigest",
                "previewFingerprint",
                "parentOperationId",
                "parentCorrelationId",
                "acceptedAt",
                "deadline",
                "index",
                "activityId",
                "childOperationId",
                "childCorrelationId",
                "action",
                "receipt",
                "undoCapsule");
            if (DeviceId.Parse(RequireString(root, "coordinatorDeviceId"))
                    != expectedRecipientDeviceId
                || expectedRecipientDeviceId
                    != expectedInstruction.CoordinatorDeviceId
                || DeviceId.Parse(RequireString(root, "sourceDeviceId"))
                    != expectedInstruction.SourceDeviceId
                || message.SenderDeviceId
                    != expectedInstruction.SourceDeviceId
                || DeviceId.Parse(RequireString(root, "targetDeviceId"))
                    != expectedInstruction.TargetDeviceId
                || SceneId.Parse(RequireString(root, "sceneId"))
                    != expectedInstruction.SceneId
                || RequireInt64(root, "sceneRevision")
                    != expectedInstruction.SceneRevision
                || RequireString(root, "sceneDigest")
                    != expectedInstruction.SceneDigest
                || RequireString(root, "previewFingerprint")
                    != expectedInstruction.PreviewFingerprint
                || OperationId.Parse(RequireString(root, "parentOperationId"))
                    != expectedInstruction.ParentOperationId
                || CorrelationId.Parse(
                    RequireString(root, "parentCorrelationId"))
                    != expectedInstruction.ParentCorrelationId
                || RequireUtc(root, "acceptedAt")
                    != expectedInstruction.AcceptedAt
                || RequireUtc(root, "deadline")
                    != expectedInstruction.Deadline
                || RequireInt32(root, "index")
                    != expectedInstruction.Item.Index
                || ActivityId.Parse(RequireString(root, "activityId"))
                    != expectedInstruction.Item.ActivityId
                || OperationId.Parse(RequireString(root, "childOperationId"))
                    != expectedInstruction.Item.ChildOperationId
                || CorrelationId.Parse(
                    RequireString(root, "childCorrelationId"))
                    != expectedInstruction.Item.ChildCorrelationId
                || ParseAction(RequireString(root, "action"))
                    != expectedInstruction.Item.Action)
            {
                throw new InvalidDataException(
                    "The remote Scene child result does not match its authenticated instruction.");
            }

            OperationReceipt receipt = DecodeReceipt(
                Require(root, "receipt", JsonValueKind.Object));
            JsonElement undoElement = RequireAny(root, "undoCapsule");
            UndoCapsuleReference? undo = undoElement.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.Object => DecodeUndo(undoElement),
                _ => throw new InvalidDataException(
                    "The remote Scene child undo reference has the wrong type."),
            };
            SceneActivityOperationResult result =
                SceneActivityOperationResult.Create(receipt, undo);
            ValidateChildResult(expectedInstruction, result);
            if (receipt.OccurredAt > message.SentAt)
            {
                throw new InvalidDataException(
                    "The remote Scene child result predates its receipt outcome.");
            }

            return result;
        }
        catch (Exception exception) when (IsMalformedValue(exception))
        {
            throw new InvalidDataException(
                "The remote Scene child result body is malformed.",
                exception);
        }
    }

    private static TimeSpan DeadlineTimeToLive(
        DateTimeOffset deadline,
        DateTimeOffset sentAt,
        string purpose)
    {
        DateTimeOffset canonicalDeadline = deadline.ToUniversalTime();
        DateTimeOffset canonicalSentAt = sentAt.ToUniversalTime();
        TimeSpan remaining = canonicalDeadline - canonicalSentAt;
        if (remaining <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sentAt),
                $"A {purpose} message must be sent before its deadline.");
        }

        double milliseconds = Math.Ceiling(remaining.TotalMilliseconds);
        if (milliseconds > ControlMessage.MaximumTimeToLiveMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deadline),
                $"The {purpose} deadline exceeds the control envelope lifetime limit.");
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static void ValidateDeadline(
        ControlMessage message,
        DateTimeOffset deadline,
        string purpose)
    {
        DateTimeOffset envelopeExpiry = message.SentAt
            .AddMilliseconds(message.TimeToLiveMilliseconds);
        if (deadline <= message.SentAt || deadline > envelopeExpiry)
        {
            throw new InvalidDataException(
                $"The {purpose} deadline is outside the authenticated envelope lifetime.");
        }
    }

    private static void RequireType(
        ControlMessage message,
        ControlMessageType expected)
    {
        if (message.Type != expected)
        {
            throw new InvalidDataException(
                $"The control message is not a '{expected}' message.");
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

    private static JsonElement Require(
        JsonElement parent,
        string name,
        JsonValueKind kind)
    {
        if (!parent.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != kind)
        {
            throw new InvalidDataException(
                $"The '{name}' field is missing or has the wrong type.");
        }

        return value;
    }

    private static JsonElement RequireAny(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            throw new InvalidDataException($"The '{name}' field is missing.");
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
        if (!parent.TryGetProperty(name, out JsonElement value)
            || value.ValueKind is not (
                JsonValueKind.True
                or JsonValueKind.False))
        {
            throw new InvalidDataException(
                $"The '{name}' field is missing or has the wrong type.");
        }

        return value.GetBoolean();
    }

    private static int RequireInt32(JsonElement parent, string name)
    {
        JsonElement value = Require(parent, name, JsonValueKind.Number);
        return value.TryGetInt32(out int parsed)
            ? parsed
            : throw new InvalidDataException($"The '{name}' field is not an integer.");
    }

    private static long RequireInt64(JsonElement parent, string name)
    {
        JsonElement value = Require(parent, name, JsonValueKind.Number);
        return value.TryGetInt64(out long parsed)
            ? parsed
            : throw new InvalidDataException($"The '{name}' field is not an integer.");
    }

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

    private static object ToWireSource(SceneSourceSelection source) => new
    {
        deviceId = source.DeviceId.ToString(),
        revision = source.Revision,
        descriptorDigest = source.DescriptorDigest,
        kind = source.Kind.Value,
        placementSlot = source.Placement.Slot,
    };

    private static SceneSourceSelection DecodeSource(
        JsonElement source,
        int index,
        ActivityId activityId)
    {
        RequireOnly(
            source,
            "deviceId",
            "revision",
            "descriptorDigest",
            "kind",
            "placementSlot");
        DeviceId sourceDeviceId = DeviceId.Parse(
            RequireString(source, "deviceId"));
        return SceneSourceSelection.Create(
            index,
            activityId,
            RequireInt64(source, "revision"),
            RequireString(source, "descriptorDigest"),
            ActivityKind.Parse(RequireString(source, "kind")),
            ActivityPlacement.On(
                sourceDeviceId,
                RequireString(source, "placementSlot")));
    }

    private static object ToWireOccupancy(SceneSlotOccupancy occupancy) => new
    {
        kind = ToWireName(occupancy.Kind),
        hasDurableUndoAvailability = occupancy.HasDurableUndoAvailability,
        target = occupancy.Target is { } target
            ? ToWireTarget(target)
            : null,
    };

    private static object ToWireTarget(SceneReplaceTargetSnapshot target) => new
    {
        activityId = target.ActivityId.ToString(),
        revision = target.Revision,
        descriptorDigest = target.DescriptorDigest,
        kind = target.Kind.Value,
        deviceId = target.DeviceId.ToString(),
        placementSlot = target.Placement.Slot,
    };

    private static SceneReplaceTargetSnapshot DecodeTarget(
        JsonElement target,
        ActivityPlacement expectedPlacement)
    {
        RequireOnly(
            target,
            "activityId",
            "revision",
            "descriptorDigest",
            "kind",
            "deviceId",
            "placementSlot");
        SceneReplaceTargetSnapshot decoded = SceneReplaceTargetSnapshot.Create(
            ActivityId.Parse(RequireString(target, "activityId")),
            RequireInt64(target, "revision"),
            RequireString(target, "descriptorDigest"),
            ActivityKind.Parse(RequireString(target, "kind")),
            ActivityPlacement.On(
                DeviceId.Parse(RequireString(target, "deviceId")),
                RequireString(target, "placementSlot")));
        if (decoded.Placement != expectedPlacement)
        {
            throw new InvalidDataException(
                "The Scene target snapshot does not occupy the expected exact slot.");
        }

        return decoded;
    }

    private static object ToWireReceipt(OperationReceipt receipt) => new
    {
        operationId = receipt.OperationId.ToString(),
        correlationId = receipt.CorrelationId.ToString(),
        kind = ToWireName(receipt.Kind),
        status = ToWireName(receipt.Status),
        sourceDeviceId = receipt.SourceDeviceId.ToString(),
        targetDeviceId = receipt.TargetDeviceId.ToString(),
        activityId = receipt.ActivityId.ToString(),
        activityKind = receipt.ActivityKind?.Value,
        descriptorDigest = receipt.DescriptorDigest,
        occurredAt = receipt.OccurredAt,
        failureCode = ToWireName(receipt.FailureCode),
    };

    private static OperationReceipt DecodeReceipt(JsonElement receipt)
    {
        RequireOnly(
            receipt,
            "operationId",
            "correlationId",
            "kind",
            "status",
            "sourceDeviceId",
            "targetDeviceId",
            "activityId",
            "activityKind",
            "descriptorDigest",
            "occurredAt",
            "failureCode");
        string? activityKindValue = ReadOptionalString(receipt, "activityKind");
        return OperationReceipt.FromRecordedResult(
            OperationId.Parse(RequireString(receipt, "operationId")),
            CorrelationId.Parse(RequireString(receipt, "correlationId")),
            ParseOperationKind(RequireString(receipt, "kind")),
            ParseOperationStatus(RequireString(receipt, "status")),
            DeviceId.Parse(RequireString(receipt, "sourceDeviceId")),
            DeviceId.Parse(RequireString(receipt, "targetDeviceId")),
            ActivityId.Parse(RequireString(receipt, "activityId")),
            activityKindValue is null
                ? null
                : ActivityKind.Parse(activityKindValue),
            ReadOptionalString(receipt, "descriptorDigest"),
            RequireUtc(receipt, "occurredAt"),
            ParseFailureCode(RequireString(receipt, "failureCode")));
    }

    private static object ToWireUndo(UndoCapsuleReference undo) => new
    {
        id = undo.Id.ToString(),
        operationId = undo.OperationId.ToString(),
        correlationId = undo.CorrelationId.ToString(),
        targetActivityId = undo.TargetActivityId.ToString(),
        expectedTargetRevision = undo.ExpectedTargetRevision,
        targetDescriptorDigest = undo.TargetDescriptorDigest,
        incomingActivityId = undo.IncomingActivityId.ToString(),
        incomingDescriptorDigest = undo.IncomingDescriptorDigest,
        expiresAt = undo.ExpiresAt,
    };

    private static UndoCapsuleReference DecodeUndo(JsonElement undo)
    {
        RequireOnly(
            undo,
            "id",
            "operationId",
            "correlationId",
            "targetActivityId",
            "expectedTargetRevision",
            "targetDescriptorDigest",
            "incomingActivityId",
            "incomingDescriptorDigest",
            "expiresAt");
        return new UndoCapsuleReference(
            UndoCapsuleId.Parse(RequireString(undo, "id")),
            OperationId.Parse(RequireString(undo, "operationId")),
            CorrelationId.Parse(RequireString(undo, "correlationId")),
            ActivityId.Parse(RequireString(undo, "targetActivityId")),
            RequireInt64(undo, "expectedTargetRevision"),
            RequireString(undo, "targetDescriptorDigest"),
            ActivityId.Parse(RequireString(undo, "incomingActivityId")),
            RequireString(undo, "incomingDescriptorDigest"),
            RequireUtc(undo, "expiresAt"));
    }

    private static void ValidateChildResult(
        SceneRemoteChildInstruction instruction,
        SceneActivityOperationResult result)
    {
        SceneApplyItemPreview item = instruction.Item;
        SceneSourceSelection source = item.Source!;
        OperationReceipt receipt = result.Receipt;
        OperationKind expectedKind = item.Action switch
        {
            SceneApplyAction.Handoff => OperationKind.Handoff,
            SceneApplyAction.Move => OperationKind.Move,
            SceneApplyAction.Replace => OperationKind.Replace,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        if (receipt.OperationId != item.ChildOperationId
            || receipt.CorrelationId != item.ChildCorrelationId
            || receipt.Kind != expectedKind
            || receipt.SourceDeviceId != source.DeviceId
            || receipt.TargetDeviceId != item.Destination.DeviceId
            || receipt.ActivityId != item.ActivityId
            || (receipt.ActivityKind is not null
                && receipt.ActivityKind != source.Kind)
            || (receipt.DescriptorDigest is not null
                && receipt.DescriptorDigest != source.DescriptorDigest))
        {
            throw new ArgumentException(
                "A remote Scene child receipt does not match its instruction.",
                nameof(result));
        }

        UndoCapsuleReference? undo = result.UndoCapsule;
        if (item.Action != SceneApplyAction.Replace || !receipt.IsSuccess)
        {
            if (undo is not null)
            {
                throw new ArgumentException(
                    "Only a successful remote Scene Replace can return undo evidence.",
                    nameof(result));
            }

            return;
        }

        SceneReplaceTargetSnapshot target = item.ReplaceTarget
            ?? throw new ArgumentException(
                "A remote Scene Replace instruction requires target evidence.",
                nameof(instruction));
        if (undo is null
            || undo.OperationId != item.ChildOperationId
            || undo.CorrelationId != item.ChildCorrelationId
            || undo.TargetActivityId != target.ActivityId
            || undo.TargetDescriptorDigest != target.DescriptorDigest
            || undo.IncomingActivityId != item.ActivityId
            || undo.IncomingDescriptorDigest != source.DescriptorDigest)
        {
            throw new ArgumentException(
                "A successful remote Scene Replace requires its exact undo reference.",
                nameof(result));
        }
    }

    private static SceneSlotOccupancy DecodeOccupancy(
        JsonElement occupancy,
        SceneExactSlotQuery query)
    {
        RequireOnly(
            occupancy,
            "kind",
            "hasDurableUndoAvailability",
            "target");
        SceneSlotOccupancyKind kind = ParseOccupancyKind(
            RequireString(occupancy, "kind"));
        bool hasDurableUndoAvailability = RequireBoolean(
            occupancy,
            "hasDurableUndoAvailability");
        JsonElement targetElement = RequireAny(occupancy, "target");
        if (kind == SceneSlotOccupancyKind.EligibleConflict)
        {
            if (targetElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "An eligible Scene conflict requires exact target evidence.");
            }

            SceneReplaceTargetSnapshot target = DecodeTarget(
                targetElement,
                query.Item.Placement);
            return SceneSlotOccupancy.EligibleConflict(
                target,
                hasDurableUndoAvailability);
        }

        if (hasDurableUndoAvailability
            || targetElement.ValueKind != JsonValueKind.Null)
        {
            throw new InvalidDataException(
                "Only an eligible Scene conflict can carry target or undo evidence.");
        }

        return kind switch
        {
            SceneSlotOccupancyKind.Empty => SceneSlotOccupancy.Empty,
            SceneSlotOccupancyKind.Opaque => SceneSlotOccupancy.Opaque,
            SceneSlotOccupancyKind.Ambiguous => SceneSlotOccupancy.Ambiguous,
            _ => throw new InvalidDataException(
                "The Scene slot occupancy kind is unsupported."),
        };
    }

    private static void ValidateExactSlotResult(
        SceneExactSlotQuery query,
        SceneExactSlotInspection result)
    {
        if (result.Occupancy?.Target is { } target
            && target.Placement != query.Item.Placement)
        {
            throw new ArgumentException(
                "A Scene exact-slot result target must occupy the requested slot.",
                nameof(result));
        }
    }

    private static bool IsMalformedValue(Exception exception) => exception is
        ArgumentException
        or FormatException
        or JsonException
        or OverflowException;

    private static string ToWireName(SceneSourceLookupStatus status) => status switch
    {
        SceneSourceLookupStatus.NotFound => "not-found",
        SceneSourceLookupStatus.UniqueSource => "unique-source",
        SceneSourceLookupStatus.SelectionRequired => "selection-required",
        SceneSourceLookupStatus.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static SceneSourceLookupStatus ParseSourceLookupStatus(string value) =>
        value switch
        {
            "not-found" => SceneSourceLookupStatus.NotFound,
            "unique-source" => SceneSourceLookupStatus.UniqueSource,
            "selection-required" => SceneSourceLookupStatus.SelectionRequired,
            "unavailable" => SceneSourceLookupStatus.Unavailable,
            _ => throw new InvalidDataException(
                "The Scene source lookup status is unsupported."),
        };

    private static string ToWireName(SceneApplyItemReason reason) => reason switch
    {
        SceneApplyItemReason.None => "none",
        SceneApplyItemReason.SourceNotFound => "source-not-found",
        SceneApplyItemReason.SourceSelectionRequired => "source-selection-required",
        SceneApplyItemReason.SourceLookupUnavailable => "source-lookup-unavailable",
        SceneApplyItemReason.CapabilityDenied => "capability-denied",
        SceneApplyItemReason.ProtocolUnsupported => "protocol-unsupported",
        SceneApplyItemReason.DestinationUnavailable => "destination-unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static SceneApplyItemReason ParseReason(string value) => value switch
    {
        "none" => SceneApplyItemReason.None,
        "source-not-found" => SceneApplyItemReason.SourceNotFound,
        "source-selection-required" => SceneApplyItemReason.SourceSelectionRequired,
        "source-lookup-unavailable" => SceneApplyItemReason.SourceLookupUnavailable,
        "capability-denied" => SceneApplyItemReason.CapabilityDenied,
        "protocol-unsupported" => SceneApplyItemReason.ProtocolUnsupported,
        "destination-unavailable" => SceneApplyItemReason.DestinationUnavailable,
        _ => throw new InvalidDataException(
            "The Scene source lookup reason is unsupported."),
    };

    private static string ToWireName(SceneSourceDisposition disposition) =>
        disposition switch
        {
            SceneSourceDisposition.PreserveSource => "preserve-source",
            SceneSourceDisposition.MoveAfterAcknowledgement =>
                "move-after-acknowledgement",
            _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
        };

    private static SceneSourceDisposition ParseSourceDisposition(string value) =>
        value switch
        {
            "preserve-source" => SceneSourceDisposition.PreserveSource,
            "move-after-acknowledgement" =>
                SceneSourceDisposition.MoveAfterAcknowledgement,
            _ => throw new InvalidDataException(
                "The Scene source disposition is unsupported."),
        };

    private static string ToWireName(SceneConflictPolicy policy) => policy switch
    {
        SceneConflictPolicy.RequireEmpty => "require-empty",
        SceneConflictPolicy.ReplaceWithUndo => "replace-with-undo",
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };

    private static SceneConflictPolicy ParseConflictPolicy(string value) =>
        value switch
        {
            "require-empty" => SceneConflictPolicy.RequireEmpty,
            "replace-with-undo" => SceneConflictPolicy.ReplaceWithUndo,
            _ => throw new InvalidDataException(
                "The Scene conflict policy is unsupported."),
        };

    private static string ToWireName(SceneSlotOccupancyKind kind) => kind switch
    {
        SceneSlotOccupancyKind.Empty => "empty",
        SceneSlotOccupancyKind.EligibleConflict => "eligible-conflict",
        SceneSlotOccupancyKind.Opaque => "opaque",
        SceneSlotOccupancyKind.Ambiguous => "ambiguous",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static SceneSlotOccupancyKind ParseOccupancyKind(string value) =>
        value switch
        {
            "empty" => SceneSlotOccupancyKind.Empty,
            "eligible-conflict" => SceneSlotOccupancyKind.EligibleConflict,
            "opaque" => SceneSlotOccupancyKind.Opaque,
            "ambiguous" => SceneSlotOccupancyKind.Ambiguous,
            _ => throw new InvalidDataException(
                "The Scene slot occupancy kind is unsupported."),
        };

    private static string ToWireName(SceneApplyAction action) => action switch
    {
        SceneApplyAction.Handoff => "handoff",
        SceneApplyAction.Move => "move",
        SceneApplyAction.Replace => "replace",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private static SceneApplyAction ParseAction(string value) => value switch
    {
        "handoff" => SceneApplyAction.Handoff,
        "move" => SceneApplyAction.Move,
        "replace" => SceneApplyAction.Replace,
        _ => throw new InvalidDataException(
            "The remote Scene child action is unsupported."),
    };

    private static string ToWireName(OperationKind kind) => kind switch
    {
        OperationKind.Handoff => "handoff",
        OperationKind.Move => "move",
        OperationKind.Replace => "replace",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static OperationKind ParseOperationKind(string value) => value switch
    {
        "handoff" => OperationKind.Handoff,
        "move" => OperationKind.Move,
        "replace" => OperationKind.Replace,
        _ => throw new InvalidDataException(
            "The remote Scene child operation kind is unsupported."),
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

    private static OperationStatus ParseOperationStatus(string value) => value switch
    {
        "committed" => OperationStatus.Committed,
        "committed-with-warning" => OperationStatus.CommittedWithWarning,
        "rejected" => OperationStatus.Rejected,
        "failed" => OperationStatus.Failed,
        "recovering" => OperationStatus.Recovering,
        _ => throw new InvalidDataException(
            "The remote Scene child operation status is unsupported."),
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
        _ => throw new InvalidDataException(
            "The remote Scene child failure code is unsupported."),
    };
}
