using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Flowspan.Domain;

public sealed record SwapReservationToken
{
    private SwapReservationToken(Guid value) => Value = value;

    public Guid Value { get; }

    public static SwapReservationToken From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("A swap reservation token cannot be empty.", nameof(value))
            : new SwapReservationToken(value);

    public override string ToString() => Value.ToString("D");
}

public enum SwapDecisionOutcome
{
    Commit,
    Abort,
}

public sealed record SwapDecisionParticipant
{
    private SwapDecisionParticipant(
        DeviceId deviceId,
        SwapReservationToken reservationToken)
    {
        DeviceId = deviceId;
        ReservationToken = reservationToken;
    }

    public DeviceId DeviceId { get; }

    public SwapReservationToken ReservationToken { get; }

    public static SwapDecisionParticipant Create(
        DeviceId deviceId,
        SwapReservationToken reservationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(reservationToken);
        return new SwapDecisionParticipant(deviceId, reservationToken);
    }
}

public sealed record SwapDecision
{
    private SwapDecision(
        OperationId operationId,
        SwapDecisionOutcome outcome,
        DateTimeOffset decidedAt,
        FailureCode failureCode,
        ImmutableArray<SwapDecisionParticipant> participants,
        string digest)
    {
        OperationId = operationId;
        Outcome = outcome;
        DecidedAt = decidedAt;
        FailureCode = failureCode;
        Participants = participants;
        Digest = digest;
    }

    public OperationId OperationId { get; }

    public SwapDecisionOutcome Outcome { get; }

    public DateTimeOffset DecidedAt { get; }

    public FailureCode FailureCode { get; }

    public ImmutableArray<SwapDecisionParticipant> Participants { get; }

    public ImmutableArray<SwapReservationToken> ReservationTokens =>
        Participants
            .Select(static participant => participant.ReservationToken)
            .ToImmutableArray();

    public string Digest { get; }

    public static SwapDecision Create(
        OperationId operationId,
        SwapDecisionOutcome outcome,
        DateTimeOffset decidedAt,
        IEnumerable<SwapDecisionParticipant> participants,
        FailureCode failureCode = FailureCode.None)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(participants);
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        if (!Enum.IsDefined(failureCode))
        {
            throw new ArgumentOutOfRangeException(nameof(failureCode));
        }

        if (decidedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A swap decision timestamp must be UTC.",
                nameof(decidedAt));
        }

        if ((outcome == SwapDecisionOutcome.Commit) != (failureCode == FailureCode.None))
        {
            throw new ArgumentException(
                "A swap Commit has no failure code and an Abort requires one.",
                nameof(failureCode));
        }

        ImmutableArray<SwapDecisionParticipant> ordered = participants
            .OrderBy(
                static participant => participant.DeviceId.ToString(),
                StringComparer.Ordinal)
            .ToImmutableArray();
        if (ordered.Length != 2
            || ordered.Select(static participant => participant.DeviceId)
                .Distinct()
                .Count() != 2
            || ordered.Select(static participant => participant.ReservationToken)
                .Distinct()
                .Count() != 2)
        {
            throw new ArgumentException(
                "A swap decision must identify exactly two distinct Device/token participants.",
                nameof(participants));
        }

        string material = string.Join(
            '\n',
            operationId.ToString(),
            outcome.ToString(),
            decidedAt.ToString("O", CultureInfo.InvariantCulture),
            failureCode.ToString(),
            string.Join(
                '\n',
                ordered.Select(static participant => string.Join(
                    '\n',
                    participant.DeviceId.ToString(),
                    participant.ReservationToken.ToString()))));
        string digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        return new SwapDecision(
            operationId,
            outcome,
            decidedAt,
            failureCode,
            ordered,
            digest);
    }

    public bool Includes(SwapReservationToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Participants.Any(participant => participant.ReservationToken == token);
    }

    public bool TryGetReservationToken(
        DeviceId deviceId,
        [NotNullWhen(true)] out SwapReservationToken? reservationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        SwapDecisionParticipant? participant = Participants.FirstOrDefault(
            candidate => candidate.DeviceId == deviceId);
        reservationToken = participant?.ReservationToken;
        return reservationToken is not null;
    }
}

public enum SwapReservationPhase
{
    Prepared,
    Committed,
    Aborted,
}

public sealed record SwapReservation
{
    private SwapReservation(
        OperationId operationId,
        SwapReservationToken token,
        ActivityInstance originalActivity,
        ActivityInstance incomingActivity,
        DateTimeOffset expiresAt,
        string requestDigest,
        SwapReservationPhase phase,
        string? decisionDigest)
    {
        OperationId = operationId;
        Token = token;
        OriginalActivity = originalActivity;
        IncomingActivity = incomingActivity;
        ExpiresAt = expiresAt;
        RequestDigest = requestDigest;
        Phase = phase;
        DecisionDigest = decisionDigest;
    }

    public OperationId OperationId { get; }

    public SwapReservationToken Token { get; }

    public ActivityInstance OriginalActivity { get; }

    public ActivityInstance IncomingActivity { get; }

    public DateTimeOffset ExpiresAt { get; }

    public string RequestDigest { get; }

    public SwapReservationPhase Phase { get; }

    public string? DecisionDigest { get; }

    public static SwapReservation Prepare(
        OperationId operationId,
        SwapReservationToken token,
        ActivityInstance originalActivity,
        ActivityInstance incomingActivity,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(originalActivity);
        ArgumentNullException.ThrowIfNull(incomingActivity);
        if (originalActivity.Lifecycle != ActivityLifecycle.Active
            || incomingActivity.Lifecycle != ActivityLifecycle.Active)
        {
            throw new InvalidOperationException(
                "Only active Activities can be reserved for swap.");
        }

        if (originalActivity.Descriptor.Id == incomingActivity.Descriptor.Id)
        {
            throw new ArgumentException(
                "A swap endpoint must receive a different Activity.",
                nameof(incomingActivity));
        }

        if (originalActivity.Placement.DeviceId
            == incomingActivity.Placement.DeviceId)
        {
            throw new ArgumentException(
                "A swap reservation must exchange Activities from different devices.",
                nameof(incomingActivity));
        }

        if (expiresAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A swap reservation expiry must be UTC.",
                nameof(expiresAt));
        }

        string digest = ComputeRequestDigest(
            operationId,
            originalActivity,
            incomingActivity,
            expiresAt);
        return new SwapReservation(
            operationId,
            token,
            originalActivity,
            incomingActivity,
            expiresAt,
            digest,
            SwapReservationPhase.Prepared,
            null);
    }

    public bool MatchesRequest(
        ActivityInstance originalActivity,
        ActivityInstance incomingActivity,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(originalActivity);
        ArgumentNullException.ThrowIfNull(incomingActivity);
        string candidate = ComputeRequestDigest(
            OperationId,
            originalActivity,
            incomingActivity,
            expiresAt);
        return StringComparer.Ordinal.Equals(RequestDigest, candidate);
    }

    public SwapReservation ApplyDecision(SwapDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.OperationId != OperationId
            || !decision.TryGetReservationToken(
                OriginalActivity.Placement.DeviceId,
                out SwapReservationToken? participantToken)
            || participantToken != Token)
        {
            throw new InvalidOperationException(
                "The swap decision does not match this reservation.");
        }

        if (Phase != SwapReservationPhase.Prepared)
        {
            return StringComparer.Ordinal.Equals(DecisionDigest, decision.Digest)
                ? this
                : throw new InvalidOperationException(
                    "A terminal swap reservation cannot accept a different decision.");
        }

        if (decision.Outcome == SwapDecisionOutcome.Commit
            && decision.DecidedAt > ExpiresAt)
        {
            throw new InvalidOperationException(
                "A commit decision cannot be created after reservation expiry.");
        }

        return new SwapReservation(
            OperationId,
            Token,
            OriginalActivity,
            IncomingActivity,
            ExpiresAt,
            RequestDigest,
            decision.Outcome == SwapDecisionOutcome.Commit
                ? SwapReservationPhase.Committed
                : SwapReservationPhase.Aborted,
            decision.Digest);
    }

    public ActivityInstance CreateCommittedReplacement()
    {
        if (Phase != SwapReservationPhase.Committed)
        {
            throw new InvalidOperationException(
                "Only a committed reservation has a replacement Activity.");
        }

        return ActivityInstance.Active(
            IncomingActivity.Descriptor,
            OriginalActivity.Placement,
            checked(IncomingActivity.Revision + 1));
    }

    private static string ComputeRequestDigest(
        OperationId operationId,
        ActivityInstance originalActivity,
        ActivityInstance incomingActivity,
        DateTimeOffset expiresAt)
    {
        string material = string.Join(
            '\n',
            operationId.ToString(),
            originalActivity.Descriptor.Id.ToString(),
            originalActivity.Descriptor.DescriptorDigest,
            originalActivity.Revision.ToString(CultureInfo.InvariantCulture),
            originalActivity.Placement.DeviceId.ToString(),
            originalActivity.Placement.Slot,
            incomingActivity.Descriptor.Id.ToString(),
            incomingActivity.Descriptor.DescriptorDigest,
            incomingActivity.Revision.ToString(CultureInfo.InvariantCulture),
            incomingActivity.Placement.DeviceId.ToString(),
            incomingActivity.Placement.Slot,
            expiresAt.ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
