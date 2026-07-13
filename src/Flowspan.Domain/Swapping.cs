using System.Collections.Immutable;
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

public sealed record SwapDecision
{
    private SwapDecision(
        OperationId operationId,
        SwapDecisionOutcome outcome,
        DateTimeOffset decidedAt,
        ImmutableArray<SwapReservationToken> reservationTokens,
        string digest)
    {
        OperationId = operationId;
        Outcome = outcome;
        DecidedAt = decidedAt;
        ReservationTokens = reservationTokens;
        Digest = digest;
    }

    public OperationId OperationId { get; }

    public SwapDecisionOutcome Outcome { get; }

    public DateTimeOffset DecidedAt { get; }

    public ImmutableArray<SwapReservationToken> ReservationTokens { get; }

    public string Digest { get; }

    public static SwapDecision Create(
        OperationId operationId,
        SwapDecisionOutcome outcome,
        DateTimeOffset decidedAt,
        IEnumerable<SwapReservationToken> reservationTokens)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(reservationTokens);

        ImmutableArray<SwapReservationToken> tokens = reservationTokens
            .Distinct()
            .OrderBy(static token => token.ToString(), StringComparer.Ordinal)
            .ToImmutableArray();
        if (tokens.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A swap decision must identify at least one reservation.",
                nameof(reservationTokens));
        }

        if (outcome == SwapDecisionOutcome.Commit && tokens.Length != 2)
        {
            throw new ArgumentException(
                "A swap commit decision must identify exactly two reservations.",
                nameof(reservationTokens));
        }

        string material = string.Join(
            '\n',
            operationId.ToString(),
            outcome.ToString(),
            decidedAt.ToString("O", CultureInfo.InvariantCulture),
            string.Join('\n', tokens.Select(static token => token.ToString())));
        string digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        return new SwapDecision(operationId, outcome, decidedAt, tokens, digest);
    }

    public bool Includes(SwapReservationToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ReservationTokens.Contains(token);
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
        if (decision.OperationId != OperationId || !decision.Includes(Token))
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
            incomingActivity.Descriptor.Id.ToString(),
            incomingActivity.Descriptor.DescriptorDigest,
            incomingActivity.Revision.ToString(CultureInfo.InvariantCulture),
            expiresAt.ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
