using System.Text.Json;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Protocol;

namespace Flowspan.Transport;

public sealed record RemoteWindowAdmissionRequest
{
    private RemoteWindowAdmissionRequest(
        CorrelationId correlationId,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId,
        DeviceId participantDeviceId,
        MirrorParticipantRole requestedRole,
        DateTimeOffset deadline)
    {
        CorrelationId = correlationId;
        SessionId = sessionId;
        ActivityId = activityId;
        HostDeviceId = hostDeviceId;
        ParticipantDeviceId = participantDeviceId;
        RequestedRole = requestedRole;
        Deadline = deadline;
    }

    public ActivityId ActivityId { get; }

    public CorrelationId CorrelationId { get; }

    public DateTimeOffset Deadline { get; }

    public DeviceId HostDeviceId { get; }

    public DeviceId ParticipantDeviceId { get; }

    public MirrorParticipantRole RequestedRole { get; }

    public RemoteWindowSessionId SessionId { get; }

    public static RemoteWindowAdmissionRequest Create(
        CorrelationId correlationId,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId,
        DeviceId participantDeviceId,
        MirrorParticipantRole requestedRole,
        DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(hostDeviceId);
        ArgumentNullException.ThrowIfNull(participantDeviceId);
        if (hostDeviceId == participantDeviceId)
        {
            throw new ArgumentException(
                "A Remote Window participant must be remote from its host.",
                nameof(participantDeviceId));
        }

        if (!Enum.IsDefined(requestedRole))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedRole));
        }

        if (deadline.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A Remote Window command deadline must be a canonical UTC timestamp.",
                nameof(deadline));
        }

        return new RemoteWindowAdmissionRequest(
            correlationId,
            sessionId,
            activityId,
            hostDeviceId,
            participantDeviceId,
            requestedRole,
            deadline);
    }
}

public sealed record RemoteWindowDriverRequest
{
    private RemoteWindowDriverRequest(
        CorrelationId correlationId,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId,
        DeviceId participantDeviceId,
        long expectedEpoch,
        TimeSpan leaseDuration,
        DateTimeOffset deadline)
    {
        CorrelationId = correlationId;
        SessionId = sessionId;
        ActivityId = activityId;
        HostDeviceId = hostDeviceId;
        ParticipantDeviceId = participantDeviceId;
        ExpectedEpoch = expectedEpoch;
        LeaseDuration = leaseDuration;
        Deadline = deadline;
    }

    public ActivityId ActivityId { get; }

    public CorrelationId CorrelationId { get; }

    public DateTimeOffset Deadline { get; }

    public long ExpectedEpoch { get; }

    public DeviceId HostDeviceId { get; }

    public TimeSpan LeaseDuration { get; }

    public DeviceId ParticipantDeviceId { get; }

    public RemoteWindowSessionId SessionId { get; }

    public static RemoteWindowDriverRequest Create(
        CorrelationId correlationId,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId,
        DeviceId participantDeviceId,
        long expectedEpoch,
        TimeSpan leaseDuration,
        DateTimeOffset deadline)
    {
        ValidateCommandIdentity(
            correlationId,
            sessionId,
            activityId,
            hostDeviceId,
            participantDeviceId,
            deadline);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedEpoch);
        if (leaseDuration < DriverLease.MinimumDuration
            || leaseDuration > DriverLease.MaximumDuration
            || !double.IsInteger(leaseDuration.TotalMilliseconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "A Remote Window Driver request has an invalid lease duration.");
        }

        return new RemoteWindowDriverRequest(
            correlationId,
            sessionId,
            activityId,
            hostDeviceId,
            participantDeviceId,
            expectedEpoch,
            leaseDuration,
            deadline);
    }

    private static void ValidateCommandIdentity(
        CorrelationId correlationId,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId,
        DeviceId participantDeviceId,
        DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(hostDeviceId);
        ArgumentNullException.ThrowIfNull(participantDeviceId);
        if (hostDeviceId == participantDeviceId)
        {
            throw new ArgumentException(
                "A Remote Window participant must be remote from its host.",
                nameof(participantDeviceId));
        }

        if (deadline.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A Remote Window command deadline must be a canonical UTC timestamp.",
                nameof(deadline));
        }
    }
}

public sealed record RemoteWindowInputRequest
{
    private RemoteWindowInputRequest(
        CorrelationId correlationId,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId,
        DeviceId participantDeviceId,
        long leaseEpoch,
        RemoteInputBatch batch,
        DateTimeOffset deadline)
    {
        CorrelationId = correlationId;
        SessionId = sessionId;
        ActivityId = activityId;
        HostDeviceId = hostDeviceId;
        ParticipantDeviceId = participantDeviceId;
        LeaseEpoch = leaseEpoch;
        Batch = batch;
        Deadline = deadline;
    }

    public ActivityId ActivityId { get; }

    public RemoteInputBatch Batch { get; }

    public CorrelationId CorrelationId { get; }

    public DateTimeOffset Deadline { get; }

    public DeviceId HostDeviceId { get; }

    public long LeaseEpoch { get; }

    public DeviceId ParticipantDeviceId { get; }

    public RemoteWindowSessionId SessionId { get; }

    public static RemoteWindowInputRequest Create(
        CorrelationId correlationId,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId,
        DeviceId participantDeviceId,
        long leaseEpoch,
        RemoteInputBatch batch,
        DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(hostDeviceId);
        ArgumentNullException.ThrowIfNull(participantDeviceId);
        ArgumentNullException.ThrowIfNull(batch);
        if (hostDeviceId == participantDeviceId)
        {
            throw new ArgumentException(
                "A Remote Window participant must be remote from its host.",
                nameof(participantDeviceId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaseEpoch);
        if (deadline.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A Remote Window command deadline must be a canonical UTC timestamp.",
                nameof(deadline));
        }

        return new RemoteWindowInputRequest(
            correlationId,
            sessionId,
            activityId,
            hostDeviceId,
            participantDeviceId,
            leaseEpoch,
            batch,
            deadline);
    }
}

public sealed record RemoteWindowDisconnectRequest
{
    private RemoteWindowDisconnectRequest(
        CorrelationId correlationId,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId,
        DeviceId participantDeviceId,
        long lastKnownRevision,
        string reasonCode,
        DateTimeOffset deadline)
    {
        CorrelationId = correlationId;
        SessionId = sessionId;
        ActivityId = activityId;
        HostDeviceId = hostDeviceId;
        ParticipantDeviceId = participantDeviceId;
        LastKnownRevision = lastKnownRevision;
        ReasonCode = reasonCode;
        Deadline = deadline;
    }

    public ActivityId ActivityId { get; }

    public CorrelationId CorrelationId { get; }

    public DateTimeOffset Deadline { get; }

    public DeviceId HostDeviceId { get; }

    public long LastKnownRevision { get; }

    public DeviceId ParticipantDeviceId { get; }

    public string ReasonCode { get; }

    public RemoteWindowSessionId SessionId { get; }

    public static RemoteWindowDisconnectRequest Create(
        CorrelationId correlationId,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId,
        DeviceId participantDeviceId,
        long lastKnownRevision,
        string reasonCode,
        DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(hostDeviceId);
        ArgumentNullException.ThrowIfNull(participantDeviceId);
        if (hostDeviceId == participantDeviceId)
        {
            throw new ArgumentException(
                "A Remote Window participant must be remote from its host.",
                nameof(participantDeviceId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(lastKnownRevision);
        string validatedReason = ValidateReasonCode(reasonCode);
        if (deadline.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A Remote Window command deadline must be a canonical UTC timestamp.",
                nameof(deadline));
        }

        return new RemoteWindowDisconnectRequest(
            correlationId,
            sessionId,
            activityId,
            hostDeviceId,
            participantDeviceId,
            lastKnownRevision,
            validatedReason,
            deadline);
    }

    internal static string ValidateReasonCode(string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        string normalized = reasonCode.Trim();
        if (normalized.Length > 80
            || !char.IsAsciiLetterLower(normalized[0])
            || normalized.Any(static character =>
                !char.IsAsciiLetterLower(character)
                && !char.IsAsciiDigit(character)
                && character is not '.' and not '_'))
        {
            throw new ArgumentException(
                "A Remote Window reason code must contain lowercase ASCII letters, digits, dots, or underscores.",
                nameof(reasonCode));
        }

        return normalized;
    }
}

public enum RemoteWindowControlAction
{
    Admission,
    Driver,
    Input,
    Disconnect,
    StateChanged,
}

public enum RemoteWindowControlOutcome
{
    Applied,
    AlreadyApplied,
    Rejected,
}

public sealed record RemoteWindowParticipantState
{
    private RemoteWindowParticipantState(
        CorrelationId correlationId,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId,
        DeviceId participantDeviceId,
        RemoteWindowControlAction action,
        RemoteWindowControlOutcome outcome,
        string reasonCode,
        RemoteWindowLifecycle lifecycle,
        RemoteWindowCaptureState captureState,
        int participantCount,
        MirrorParticipantRole? effectiveRole,
        DeviceId? currentDriverDeviceId,
        long? driverLeaseEpoch,
        DateTimeOffset? driverLeaseExpiresAt,
        ProtectionKind protectionKind,
        long revision)
    {
        CorrelationId = correlationId;
        SessionId = sessionId;
        ActivityId = activityId;
        HostDeviceId = hostDeviceId;
        ParticipantDeviceId = participantDeviceId;
        Action = action;
        Outcome = outcome;
        ReasonCode = reasonCode;
        Lifecycle = lifecycle;
        CaptureState = captureState;
        ParticipantCount = participantCount;
        EffectiveRole = effectiveRole;
        CurrentDriverDeviceId = currentDriverDeviceId;
        DriverLeaseEpoch = driverLeaseEpoch;
        DriverLeaseExpiresAt = driverLeaseExpiresAt;
        ProtectionKind = protectionKind;
        Revision = revision;
    }

    public RemoteWindowControlAction Action { get; }

    public ActivityId ActivityId { get; }

    public RemoteWindowCaptureState CaptureState { get; }

    public CorrelationId CorrelationId { get; }

    public DeviceId? CurrentDriverDeviceId { get; }

    public long? DriverLeaseEpoch { get; }

    public DateTimeOffset? DriverLeaseExpiresAt { get; }

    public MirrorParticipantRole? EffectiveRole { get; }

    public DeviceId HostDeviceId { get; }

    public RemoteWindowLifecycle Lifecycle { get; }

    public RemoteWindowControlOutcome Outcome { get; }

    public int ParticipantCount { get; }

    public DeviceId ParticipantDeviceId { get; }

    public ProtectionKind ProtectionKind { get; }

    public string ReasonCode { get; }

    public long Revision { get; }

    public RemoteWindowSessionId SessionId { get; }

    public static RemoteWindowParticipantState Create(
        CorrelationId correlationId,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId,
        DeviceId participantDeviceId,
        RemoteWindowControlAction action,
        RemoteWindowControlOutcome outcome,
        string reasonCode,
        RemoteWindowLifecycle lifecycle,
        RemoteWindowCaptureState captureState,
        int participantCount,
        MirrorParticipantRole? effectiveRole,
        DeviceId? currentDriverDeviceId,
        long? driverLeaseEpoch,
        DateTimeOffset? driverLeaseExpiresAt,
        ProtectionKind protectionKind,
        long revision)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(hostDeviceId);
        ArgumentNullException.ThrowIfNull(participantDeviceId);
        if (hostDeviceId == participantDeviceId)
        {
            throw new ArgumentException(
                "A Remote Window participant must be remote from its host.",
                nameof(participantDeviceId));
        }

        if (!Enum.IsDefined(action)
            || !Enum.IsDefined(outcome)
            || !Enum.IsDefined(lifecycle)
            || !Enum.IsDefined(captureState)
            || !Enum.IsDefined(protectionKind)
            || effectiveRole is { } role && !Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(
                nameof(action),
                "A Remote Window state contains an unknown enum value.");
        }

        if (participantCount is < 0 or > RemoteWindowSessionController.MaximumParticipants)
        {
            throw new ArgumentOutOfRangeException(nameof(participantCount));
        }

        if (driverLeaseEpoch is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(driverLeaseEpoch));
        }

        if (driverLeaseExpiresAt is { } expiresAt
            && expiresAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A Driver expiry must be a canonical UTC timestamp.",
                nameof(driverLeaseExpiresAt));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        return new RemoteWindowParticipantState(
            correlationId,
            sessionId,
            activityId,
            hostDeviceId,
            participantDeviceId,
            action,
            outcome,
            RemoteWindowDisconnectRequest.ValidateReasonCode(reasonCode),
            lifecycle,
            captureState,
            participantCount,
            effectiveRole,
            currentDriverDeviceId,
            driverLeaseEpoch,
            driverLeaseExpiresAt,
            protectionKind,
            revision);
    }
}

public static class RemoteWindowControlMessageCodec
{
    public static readonly TimeSpan MaximumCommandTimeToLive = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan MaximumInputTimeToLive = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan StateTimeToLive = TimeSpan.FromSeconds(5);

    public static ControlMessage CreateAdmission(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        RemoteWindowAdmissionRequest request,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(request);
        if (senderDeviceId != request.ParticipantDeviceId)
        {
            throw new ArgumentException(
                "A Remote Window admission must be sent by its participant.",
                nameof(senderDeviceId));
        }

        string body = JsonSerializer.Serialize(new
        {
            activityId = request.ActivityId.ToString(),
            deadline = request.Deadline,
            hostDeviceId = request.HostDeviceId.ToString(),
            participantDeviceId = request.ParticipantDeviceId.ToString(),
            requestedRole = ToWireName(request.RequestedRole),
            sessionId = request.SessionId.ToString(),
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.RemoteWindowAdmission,
            Guid.NewGuid(),
            request.CorrelationId,
            senderDeviceId,
            sentAt,
            DeadlineTimeToLive(request.Deadline, sentAt),
            body);
    }

    public static RemoteWindowAdmissionRequest DecodeAdmission(
        ControlMessage message,
        DeviceId expectedHostDeviceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedHostDeviceId);
        RequireType(message, ControlMessageType.RemoteWindowAdmission);
        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "activityId",
                "deadline",
                "hostDeviceId",
                "participantDeviceId",
                "requestedRole",
                "sessionId");
            DateTimeOffset deadline = RequireUtc(root, "deadline");
            ValidateDeadline(message, deadline);
            DeviceId hostDeviceId = DeviceId.Parse(
                RequireString(root, "hostDeviceId"));
            DeviceId participantDeviceId = DeviceId.Parse(
                RequireString(root, "participantDeviceId"));
            if (hostDeviceId != expectedHostDeviceId
                || participantDeviceId != message.SenderDeviceId)
            {
                throw new InvalidDataException(
                    "The Remote Window admission does not match its authenticated participants.");
            }

            return RemoteWindowAdmissionRequest.Create(
                message.CorrelationId,
                RemoteWindowSessionId.Parse(RequireString(root, "sessionId")),
                ActivityId.Parse(RequireString(root, "activityId")),
                hostDeviceId,
                participantDeviceId,
                ParseParticipantRole(RequireString(root, "requestedRole")),
                deadline);
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or JsonException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The Remote Window admission body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateDriverRequest(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        RemoteWindowDriverRequest request,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(request);
        if (senderDeviceId != request.ParticipantDeviceId)
        {
            throw new ArgumentException(
                "A Remote Window Driver request must be sent by its participant.",
                nameof(senderDeviceId));
        }

        string body = JsonSerializer.Serialize(new
        {
            activityId = request.ActivityId.ToString(),
            deadline = request.Deadline,
            expectedEpoch = request.ExpectedEpoch,
            hostDeviceId = request.HostDeviceId.ToString(),
            leaseDurationMs = checked((int)request.LeaseDuration.TotalMilliseconds),
            participantDeviceId = request.ParticipantDeviceId.ToString(),
            sessionId = request.SessionId.ToString(),
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.RemoteWindowDriver,
            Guid.NewGuid(),
            request.CorrelationId,
            senderDeviceId,
            sentAt,
            DeadlineTimeToLive(request.Deadline, sentAt),
            body);
    }

    public static RemoteWindowDriverRequest DecodeDriverRequest(
        ControlMessage message,
        DeviceId expectedHostDeviceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedHostDeviceId);
        RequireType(message, ControlMessageType.RemoteWindowDriver);
        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "activityId",
                "deadline",
                "expectedEpoch",
                "hostDeviceId",
                "leaseDurationMs",
                "participantDeviceId",
                "sessionId");
            DateTimeOffset deadline = RequireUtc(root, "deadline");
            ValidateDeadline(message, deadline);
            DeviceId hostDeviceId = DeviceId.Parse(
                RequireString(root, "hostDeviceId"));
            DeviceId participantDeviceId = DeviceId.Parse(
                RequireString(root, "participantDeviceId"));
            if (hostDeviceId != expectedHostDeviceId
                || participantDeviceId != message.SenderDeviceId)
            {
                throw new InvalidDataException(
                    "The Remote Window Driver request does not match its authenticated participants.");
            }

            return RemoteWindowDriverRequest.Create(
                message.CorrelationId,
                RemoteWindowSessionId.Parse(RequireString(root, "sessionId")),
                ActivityId.Parse(RequireString(root, "activityId")),
                hostDeviceId,
                participantDeviceId,
                RequireInt64(root, "expectedEpoch"),
                TimeSpan.FromMilliseconds(RequireInt32(root, "leaseDurationMs")),
                deadline);
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or JsonException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The Remote Window Driver request body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateInputRequest(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        RemoteWindowInputRequest request,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(request);
        if (senderDeviceId != request.ParticipantDeviceId)
        {
            throw new ArgumentException(
                "Remote Window input must be sent by its participant.",
                nameof(senderDeviceId));
        }

        string body = JsonSerializer.Serialize(new
        {
            activityId = request.ActivityId.ToString(),
            deadline = request.Deadline,
            events = request.Batch.Events.Select(ToWireEvent),
            hostDeviceId = request.HostDeviceId.ToString(),
            leaseEpoch = request.LeaseEpoch,
            participantDeviceId = request.ParticipantDeviceId.ToString(),
            sessionId = request.SessionId.ToString(),
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.RemoteWindowInput,
            Guid.NewGuid(),
            request.CorrelationId,
            senderDeviceId,
            sentAt,
            DeadlineTimeToLive(
                request.Deadline,
                sentAt,
                MaximumInputTimeToLive),
            body);
    }

    public static RemoteWindowInputRequest DecodeInputRequest(
        ControlMessage message,
        DeviceId expectedHostDeviceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedHostDeviceId);
        RequireType(message, ControlMessageType.RemoteWindowInput);
        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "activityId",
                "deadline",
                "events",
                "hostDeviceId",
                "leaseEpoch",
                "participantDeviceId",
                "sessionId");
            DateTimeOffset deadline = RequireUtc(root, "deadline");
            ValidateDeadline(message, deadline, MaximumInputTimeToLive);
            DeviceId hostDeviceId = DeviceId.Parse(
                RequireString(root, "hostDeviceId"));
            DeviceId participantDeviceId = DeviceId.Parse(
                RequireString(root, "participantDeviceId"));
            if (hostDeviceId != expectedHostDeviceId
                || participantDeviceId != message.SenderDeviceId)
            {
                throw new InvalidDataException(
                    "Remote Window input does not match its authenticated participants.");
            }

            JsonElement events = Require(root, "events", JsonValueKind.Array);
            if (events.GetArrayLength() is < 1 or > RemoteInputBatch.MaximumEvents)
            {
                throw new InvalidDataException(
                    "Remote Window input exceeds the event bound.");
            }

            return RemoteWindowInputRequest.Create(
                message.CorrelationId,
                RemoteWindowSessionId.Parse(RequireString(root, "sessionId")),
                ActivityId.Parse(RequireString(root, "activityId")),
                hostDeviceId,
                participantDeviceId,
                RequireInt64(root, "leaseEpoch"),
                RemoteInputBatch.Create(events.EnumerateArray().Select(ParseInputEvent)),
                deadline);
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or JsonException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The Remote Window input body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateDisconnect(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        RemoteWindowDisconnectRequest request,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(request);
        if (senderDeviceId != request.ParticipantDeviceId)
        {
            throw new ArgumentException(
                "A Remote Window disconnect must be sent by its participant.",
                nameof(senderDeviceId));
        }

        string body = JsonSerializer.Serialize(new
        {
            activityId = request.ActivityId.ToString(),
            deadline = request.Deadline,
            hostDeviceId = request.HostDeviceId.ToString(),
            lastKnownRevision = request.LastKnownRevision,
            participantDeviceId = request.ParticipantDeviceId.ToString(),
            reasonCode = request.ReasonCode,
            sessionId = request.SessionId.ToString(),
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.RemoteWindowDisconnect,
            Guid.NewGuid(),
            request.CorrelationId,
            senderDeviceId,
            sentAt,
            DeadlineTimeToLive(request.Deadline, sentAt),
            body);
    }

    public static RemoteWindowDisconnectRequest DecodeDisconnect(
        ControlMessage message,
        DeviceId expectedHostDeviceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedHostDeviceId);
        RequireType(message, ControlMessageType.RemoteWindowDisconnect);
        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "activityId",
                "deadline",
                "hostDeviceId",
                "lastKnownRevision",
                "participantDeviceId",
                "reasonCode",
                "sessionId");
            DateTimeOffset deadline = RequireUtc(root, "deadline");
            ValidateDeadline(message, deadline);
            DeviceId hostDeviceId = DeviceId.Parse(
                RequireString(root, "hostDeviceId"));
            DeviceId participantDeviceId = DeviceId.Parse(
                RequireString(root, "participantDeviceId"));
            if (hostDeviceId != expectedHostDeviceId
                || participantDeviceId != message.SenderDeviceId)
            {
                throw new InvalidDataException(
                    "The Remote Window disconnect does not match its authenticated participants.");
            }

            return RemoteWindowDisconnectRequest.Create(
                message.CorrelationId,
                RemoteWindowSessionId.Parse(RequireString(root, "sessionId")),
                ActivityId.Parse(RequireString(root, "activityId")),
                hostDeviceId,
                participantDeviceId,
                RequireInt64(root, "lastKnownRevision"),
                RequireString(root, "reasonCode"),
                deadline);
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or JsonException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The Remote Window disconnect body is malformed.",
                exception);
        }
    }

    public static ControlMessage CreateState(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        RemoteWindowParticipantState state,
        DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentNullException.ThrowIfNull(state);
        if (senderDeviceId != state.HostDeviceId)
        {
            throw new ArgumentException(
                "Remote Window state must be sent by its host.",
                nameof(senderDeviceId));
        }

        string body = JsonSerializer.Serialize(new
        {
            action = ToWireName(state.Action),
            activityId = state.ActivityId.ToString(),
            captureState = ToWireName(state.CaptureState),
            currentDriverDeviceId = state.CurrentDriverDeviceId?.ToString(),
            driverLeaseEpoch = state.DriverLeaseEpoch,
            driverLeaseExpiresAt = state.DriverLeaseExpiresAt,
            effectiveRole = state.EffectiveRole is { } role
                ? ToWireName(role)
                : null,
            hostDeviceId = state.HostDeviceId.ToString(),
            lifecycle = ToWireName(state.Lifecycle),
            outcome = ToWireName(state.Outcome),
            participantCount = state.ParticipantCount,
            participantDeviceId = state.ParticipantDeviceId.ToString(),
            protectionKind = ToWireName(state.ProtectionKind),
            reasonCode = state.ReasonCode,
            revision = state.Revision,
            sessionId = state.SessionId.ToString(),
        });
        return ControlMessage.Create(
            version,
            ControlMessageType.RemoteWindowState,
            Guid.NewGuid(),
            state.CorrelationId,
            senderDeviceId,
            sentAt,
            StateTimeToLive,
            body);
    }

    public static RemoteWindowParticipantState DecodeState(
        ControlMessage message,
        DeviceId expectedParticipantDeviceId,
        RemoteWindowSessionId expectedSessionId,
        ActivityId expectedActivityId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedParticipantDeviceId);
        ArgumentNullException.ThrowIfNull(expectedSessionId);
        ArgumentNullException.ThrowIfNull(expectedActivityId);
        RemoteWindowParticipantState state = DecodeStateCore(
            message,
            expectedParticipantDeviceId);
        if (state.SessionId != expectedSessionId
            || state.ActivityId != expectedActivityId)
        {
            throw new InvalidDataException(
                "Remote Window state does not match its authenticated live session.");
        }

        return state;
    }

    public static RemoteWindowParticipantState DecodePublishedState(
        ControlMessage message,
        DeviceId expectedParticipantDeviceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expectedParticipantDeviceId);
        RemoteWindowParticipantState state = DecodeStateCore(
            message,
            expectedParticipantDeviceId);
        if (state.Action != RemoteWindowControlAction.StateChanged)
        {
            throw new InvalidDataException(
                "An unsolicited Remote Window state must describe a state change.");
        }

        return state;
    }

    private static RemoteWindowParticipantState DecodeStateCore(
        ControlMessage message,
        DeviceId expectedParticipantDeviceId)
    {
        RequireType(message, ControlMessageType.RemoteWindowState);
        try
        {
            JsonElement root = message.Body;
            RequireOnly(
                root,
                "action",
                "activityId",
                "captureState",
                "currentDriverDeviceId",
                "driverLeaseEpoch",
                "driverLeaseExpiresAt",
                "effectiveRole",
                "hostDeviceId",
                "lifecycle",
                "outcome",
                "participantCount",
                "participantDeviceId",
                "protectionKind",
                "reasonCode",
                "revision",
                "sessionId");
            RemoteWindowSessionId sessionId = RemoteWindowSessionId.Parse(
                RequireString(root, "sessionId"));
            ActivityId activityId = ActivityId.Parse(
                RequireString(root, "activityId"));
            DeviceId hostDeviceId = DeviceId.Parse(
                RequireString(root, "hostDeviceId"));
            DeviceId participantDeviceId = DeviceId.Parse(
                RequireString(root, "participantDeviceId"));
            if (hostDeviceId != message.SenderDeviceId
                || participantDeviceId != expectedParticipantDeviceId)
            {
                throw new InvalidDataException(
                    "Remote Window state does not match its authenticated participants.");
            }

            return RemoteWindowParticipantState.Create(
                message.CorrelationId,
                sessionId,
                activityId,
                hostDeviceId,
                participantDeviceId,
                ParseControlAction(RequireString(root, "action")),
                ParseControlOutcome(RequireString(root, "outcome")),
                RequireString(root, "reasonCode"),
                ParseLifecycle(RequireString(root, "lifecycle")),
                ParseCaptureState(RequireString(root, "captureState")),
                RequireInt32(root, "participantCount"),
                ParseNullableParticipantRole(root, "effectiveRole"),
                ParseNullableDeviceId(root, "currentDriverDeviceId"),
                ParseNullableInt64(root, "driverLeaseEpoch"),
                ParseNullableUtc(root, "driverLeaseExpiresAt"),
                ParseProtectionKind(RequireString(root, "protectionKind")),
                RequireInt64(root, "revision"));
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or JsonException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The Remote Window state body is malformed.",
                exception);
        }
    }

    private static TimeSpan DeadlineTimeToLive(
        DateTimeOffset deadline,
        DateTimeOffset sentAt,
        TimeSpan? maximum = null)
    {
        TimeSpan maximumTimeToLive = maximum ?? MaximumCommandTimeToLive;
        TimeSpan ttl = deadline - sentAt.ToUniversalTime();
        if (ttl <= TimeSpan.Zero || ttl > maximumTimeToLive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deadline),
                $"A Remote Window command deadline must be after send time and no more than {maximumTimeToLive.TotalSeconds} seconds later.");
        }

        return ttl;
    }

    private static void ValidateDeadline(
        ControlMessage message,
        DateTimeOffset deadline,
        TimeSpan? maximum = null)
    {
        TimeSpan maximumTimeToLive = maximum ?? MaximumCommandTimeToLive;
        TimeSpan ttl = deadline - message.SentAt.ToUniversalTime();
        if (ttl <= TimeSpan.Zero
            || ttl > maximumTimeToLive
            || ttl != TimeSpan.FromMilliseconds(message.TimeToLiveMilliseconds))
        {
            throw new InvalidDataException(
                "The Remote Window command deadline does not match its envelope.");
        }
    }

    private static void RequireType(
        ControlMessage message,
        ControlMessageType expected)
    {
        if (message.Type != expected
            || !ProtocolFeatures.SupportsRemoteWindow(message.Version))
        {
            throw new InvalidDataException(
                "The control message is not a supported Remote Window message.");
        }
    }

    private static DateTimeOffset RequireUtc(JsonElement parent, string name)
    {
        JsonElement value = Require(parent, name, JsonValueKind.String);
        if (!value.TryGetDateTimeOffset(out DateTimeOffset parsed)
            || parsed.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException(
                $"The '{name}' field must be a canonical UTC timestamp.");
        }

        return parsed;
    }

    private static string RequireString(JsonElement parent, string name)
    {
        JsonElement value = Require(parent, name, JsonValueKind.String);
        return value.GetString()
            ?? throw new InvalidDataException($"The '{name}' field is null.");
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

    private static long? ParseNullableInt64(JsonElement parent, string name)
    {
        JsonElement value = RequirePresent(parent, name);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long parsed)
                ? parsed
                : throw new InvalidDataException(
                    $"The '{name}' field must be an integer or null.");
    }

    private static DeviceId? ParseNullableDeviceId(JsonElement parent, string name)
    {
        JsonElement value = RequirePresent(parent, name);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => DeviceId.Parse(
                value.GetString()
                ?? throw new InvalidDataException($"The '{name}' field is null.")),
            _ => throw new InvalidDataException(
                $"The '{name}' field must be a Device ID or null."),
        };
    }

    private static DateTimeOffset? ParseNullableUtc(JsonElement parent, string name)
    {
        JsonElement value = RequirePresent(parent, name);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String
            || !value.TryGetDateTimeOffset(out DateTimeOffset parsed)
            || parsed.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException(
                $"The '{name}' field must be a canonical UTC timestamp or null.");
        }

        return parsed;
    }

    private static MirrorParticipantRole? ParseNullableParticipantRole(
        JsonElement parent,
        string name)
    {
        JsonElement value = RequirePresent(parent, name);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => ParseParticipantRole(
                value.GetString()
                ?? throw new InvalidDataException($"The '{name}' field is null.")),
            _ => throw new InvalidDataException(
                $"The '{name}' field must be a participant role or null."),
        };
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

    private static JsonElement RequirePresent(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value)
            ? value
            : throw new InvalidDataException(
                $"The required '{name}' field is missing.");

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

    private static string ToWireName(MirrorParticipantRole role) => role switch
    {
        MirrorParticipantRole.ViewOnly => "view-only",
        MirrorParticipantRole.DriverEligible => "driver-eligible",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static MirrorParticipantRole ParseParticipantRole(string value) => value switch
    {
        "view-only" => MirrorParticipantRole.ViewOnly,
        "driver-eligible" => MirrorParticipantRole.DriverEligible,
        _ => throw new InvalidDataException(
            "The Remote Window participant role is unsupported."),
    };

    private static IReadOnlyDictionary<string, object> ToWireEvent(
        RemoteInputEvent input) => input.Kind switch
        {
            RemoteInputEventKind.HidKeyDown or RemoteInputEventKind.HidKeyUp =>
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["kind"] = ToWireName(input.Kind),
                    ["usageId"] = input.HidUsageId,
                    ["usagePage"] = input.HidUsagePage,
                },
            RemoteInputEventKind.PointerMove =>
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["kind"] = ToWireName(input.Kind),
                    ["x"] = input.NormalizedX,
                    ["y"] = input.NormalizedY,
                },
            RemoteInputEventKind.PointerButtonDown
                or RemoteInputEventKind.PointerButtonUp =>
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["button"] = ToWireName(input.PointerButton!.Value),
                    ["kind"] = ToWireName(input.Kind),
                },
            RemoteInputEventKind.Scroll =>
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["horizontalDelta"] = input.HorizontalScroll,
                    ["kind"] = ToWireName(input.Kind),
                    ["verticalDelta"] = input.VerticalScroll,
                },
            _ => throw new ArgumentOutOfRangeException(
                nameof(input),
                input.Kind,
                "The remote input event kind is unsupported."),
        };

    private static RemoteInputEvent ParseInputEvent(JsonElement input)
    {
        string kind = RequireString(input, "kind");
        return kind switch
        {
            "hid-key-down" => ParseHid(input, down: true),
            "hid-key-up" => ParseHid(input, down: false),
            "pointer-move" => ParsePointerMove(input),
            "pointer-button-down" => ParsePointerButton(input, down: true),
            "pointer-button-up" => ParsePointerButton(input, down: false),
            "scroll" => ParseScroll(input),
            _ => throw new InvalidDataException(
                "The remote input event kind is unsupported."),
        };
    }

    private static RemoteInputEvent ParseHid(JsonElement input, bool down)
    {
        RequireOnly(input, "kind", "usageId", "usagePage");
        ushort usagePage = checked((ushort)RequireInt32(input, "usagePage"));
        ushort usageId = checked((ushort)RequireInt32(input, "usageId"));
        return down
            ? RemoteInputEvent.HidKeyDown(usagePage, usageId)
            : RemoteInputEvent.HidKeyUp(usagePage, usageId);
    }

    private static RemoteInputEvent ParsePointerMove(JsonElement input)
    {
        RequireOnly(input, "kind", "x", "y");
        return RemoteInputEvent.PointerMove(
            RequireDouble(input, "x"),
            RequireDouble(input, "y"));
    }

    private static RemoteInputEvent ParsePointerButton(JsonElement input, bool down)
    {
        RequireOnly(input, "button", "kind");
        RemotePointerButton button = ParsePointerButton(
            RequireString(input, "button"));
        return down
            ? RemoteInputEvent.PointerButtonDown(button)
            : RemoteInputEvent.PointerButtonUp(button);
    }

    private static RemoteInputEvent ParseScroll(JsonElement input)
    {
        RequireOnly(input, "horizontalDelta", "kind", "verticalDelta");
        return RemoteInputEvent.Scroll(
            RequireInt32(input, "horizontalDelta"),
            RequireInt32(input, "verticalDelta"));
    }

    private static double RequireDouble(JsonElement parent, string name)
    {
        JsonElement value = Require(parent, name, JsonValueKind.Number);
        return value.TryGetDouble(out double parsed)
            ? parsed
            : throw new InvalidDataException($"The '{name}' field is not a number.");
    }

    private static string ToWireName(RemoteInputEventKind kind) => kind switch
    {
        RemoteInputEventKind.HidKeyDown => "hid-key-down",
        RemoteInputEventKind.HidKeyUp => "hid-key-up",
        RemoteInputEventKind.PointerMove => "pointer-move",
        RemoteInputEventKind.PointerButtonDown => "pointer-button-down",
        RemoteInputEventKind.PointerButtonUp => "pointer-button-up",
        RemoteInputEventKind.Scroll => "scroll",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string ToWireName(RemotePointerButton button) => button switch
    {
        RemotePointerButton.Primary => "primary",
        RemotePointerButton.Secondary => "secondary",
        RemotePointerButton.Middle => "middle",
        RemotePointerButton.Back => "back",
        RemotePointerButton.Forward => "forward",
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    private static RemotePointerButton ParsePointerButton(string value) => value switch
    {
        "primary" => RemotePointerButton.Primary,
        "secondary" => RemotePointerButton.Secondary,
        "middle" => RemotePointerButton.Middle,
        "back" => RemotePointerButton.Back,
        "forward" => RemotePointerButton.Forward,
        _ => throw new InvalidDataException(
            "The remote pointer button is unsupported."),
    };

    private static string ToWireName(RemoteWindowControlAction action) => action switch
    {
        RemoteWindowControlAction.Admission => "admission",
        RemoteWindowControlAction.Driver => "driver",
        RemoteWindowControlAction.Input => "input",
        RemoteWindowControlAction.Disconnect => "disconnect",
        RemoteWindowControlAction.StateChanged => "state-changed",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private static RemoteWindowControlAction ParseControlAction(string value) => value switch
    {
        "admission" => RemoteWindowControlAction.Admission,
        "driver" => RemoteWindowControlAction.Driver,
        "input" => RemoteWindowControlAction.Input,
        "disconnect" => RemoteWindowControlAction.Disconnect,
        "state-changed" => RemoteWindowControlAction.StateChanged,
        _ => throw new InvalidDataException(
            "The Remote Window control action is unsupported."),
    };

    private static string ToWireName(RemoteWindowControlOutcome outcome) => outcome switch
    {
        RemoteWindowControlOutcome.Applied => "applied",
        RemoteWindowControlOutcome.AlreadyApplied => "already-applied",
        RemoteWindowControlOutcome.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    private static RemoteWindowControlOutcome ParseControlOutcome(string value) => value switch
    {
        "applied" => RemoteWindowControlOutcome.Applied,
        "already-applied" => RemoteWindowControlOutcome.AlreadyApplied,
        "rejected" => RemoteWindowControlOutcome.Rejected,
        _ => throw new InvalidDataException(
            "The Remote Window control outcome is unsupported."),
    };

    private static string ToWireName(RemoteWindowLifecycle lifecycle) => lifecycle switch
    {
        RemoteWindowLifecycle.Idle => "idle",
        RemoteWindowLifecycle.Starting => "starting",
        RemoteWindowLifecycle.Active => "active",
        RemoteWindowLifecycle.ProtectionPaused => "protection-paused",
        RemoteWindowLifecycle.EmergencyStopped => "emergency-stopped",
        RemoteWindowLifecycle.Ended => "ended",
        RemoteWindowLifecycle.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(lifecycle)),
    };

    private static RemoteWindowLifecycle ParseLifecycle(string value) => value switch
    {
        "idle" => RemoteWindowLifecycle.Idle,
        "starting" => RemoteWindowLifecycle.Starting,
        "active" => RemoteWindowLifecycle.Active,
        "protection-paused" => RemoteWindowLifecycle.ProtectionPaused,
        "emergency-stopped" => RemoteWindowLifecycle.EmergencyStopped,
        "ended" => RemoteWindowLifecycle.Ended,
        "unavailable" => RemoteWindowLifecycle.Unavailable,
        _ => throw new InvalidDataException(
            "The Remote Window lifecycle is unsupported."),
    };

    private static string ToWireName(RemoteWindowCaptureState state) => state switch
    {
        RemoteWindowCaptureState.Stopped => "stopped",
        RemoteWindowCaptureState.Starting => "starting",
        RemoteWindowCaptureState.Capturing => "capturing",
        RemoteWindowCaptureState.Paused => "paused",
        RemoteWindowCaptureState.Unconfirmed => "unconfirmed",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static RemoteWindowCaptureState ParseCaptureState(string value) => value switch
    {
        "stopped" => RemoteWindowCaptureState.Stopped,
        "starting" => RemoteWindowCaptureState.Starting,
        "capturing" => RemoteWindowCaptureState.Capturing,
        "paused" => RemoteWindowCaptureState.Paused,
        "unconfirmed" => RemoteWindowCaptureState.Unconfirmed,
        _ => throw new InvalidDataException(
            "The Remote Window capture state is unsupported."),
    };

    private static string ToWireName(ProtectionKind kind) => kind switch
    {
        ProtectionKind.Safe => "safe",
        ProtectionKind.SensitiveWindow => "sensitive-window",
        ProtectionKind.SecureInput => "secure-input",
        ProtectionKind.ProtectedContent => "protected-content",
        ProtectionKind.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static ProtectionKind ParseProtectionKind(string value) => value switch
    {
        "safe" => ProtectionKind.Safe,
        "sensitive-window" => ProtectionKind.SensitiveWindow,
        "secure-input" => ProtectionKind.SecureInput,
        "protected-content" => ProtectionKind.ProtectedContent,
        "unknown" => ProtectionKind.Unknown,
        _ => throw new InvalidDataException(
            "The Remote Window protection kind is unsupported."),
    };
}
