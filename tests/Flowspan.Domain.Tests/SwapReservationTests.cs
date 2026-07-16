using Flowspan.Domain;

namespace Flowspan.Domain.Tests;

public sealed class SwapReservationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

    private static readonly OperationId Operation =
        OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly SwapReservationToken FirstToken =
        SwapReservationToken.From(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));

    private static readonly SwapReservationToken SecondToken =
        SwapReservationToken.From(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));

    [Fact]
    public void CommitCreatesIncomingActivityAtOriginalPlacement()
    {
        (ActivityInstance first, ActivityInstance second) = CreateActivities();
        SwapReservation prepared = SwapReservation.Prepare(
            Operation,
            FirstToken,
            first,
            second,
            Now.AddSeconds(30));
        SwapDecision decision = SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Commit,
            Now.AddSeconds(1),
            [Participant(first, FirstToken), Participant(second, SecondToken)]);

        SwapReservation committed = prepared.ApplyDecision(decision);
        ActivityInstance replacement = committed.CreateCommittedReplacement();

        Assert.Equal(SwapReservationPhase.Committed, committed.Phase);
        Assert.Equal(second.Descriptor, replacement.Descriptor);
        Assert.Equal(first.Placement, replacement.Placement);
        Assert.Equal(second.Revision + 1, replacement.Revision);
    }

    [Fact]
    public void CommitDecisionCreatedAfterExpiryIsRejected()
    {
        (ActivityInstance first, ActivityInstance second) = CreateActivities();
        SwapReservation prepared = SwapReservation.Prepare(
            Operation,
            FirstToken,
            first,
            second,
            Now.AddSeconds(2));
        SwapDecision late = SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Commit,
            Now.AddSeconds(3),
            [Participant(first, FirstToken), Participant(second, SecondToken)]);

        Assert.Throws<InvalidOperationException>(() => prepared.ApplyDecision(late));
        Assert.Equal(SwapReservationPhase.Prepared, prepared.Phase);
    }

    [Fact]
    public void TimelyCommitCanBeAppliedDuringLaterRecovery()
    {
        (ActivityInstance first, ActivityInstance second) = CreateActivities();
        SwapReservation prepared = SwapReservation.Prepare(
            Operation,
            FirstToken,
            first,
            second,
            Now.AddSeconds(2));
        SwapDecision timely = SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Commit,
            Now.AddSeconds(1),
            [Participant(first, FirstToken), Participant(second, SecondToken)]);

        SwapReservation recovered = prepared.ApplyDecision(timely);

        Assert.Equal(SwapReservationPhase.Committed, recovered.Phase);
    }

    [Fact]
    public void ReplayedDecisionIsIdempotentButDifferentTerminalDecisionConflicts()
    {
        (ActivityInstance first, ActivityInstance second) = CreateActivities();
        SwapReservation prepared = SwapReservation.Prepare(
            Operation,
            FirstToken,
            first,
            second,
            Now.AddSeconds(30));
        SwapDecision abort = SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Abort,
            Now.AddSeconds(1),
            [Participant(first, FirstToken), Participant(second, SecondToken)],
            FailureCode.RevisionConflict);
        SwapReservation aborted = prepared.ApplyDecision(abort);

        Assert.Same(aborted, aborted.ApplyDecision(abort));

        SwapDecision commit = SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Commit,
            Now.AddSeconds(1),
            [Participant(first, FirstToken), Participant(second, SecondToken)]);
        Assert.Throws<InvalidOperationException>(() => aborted.ApplyDecision(commit));
    }

    [Fact]
    public void CommitDecisionRequiresTwoDistinctReservations()
    {
        (ActivityInstance first, ActivityInstance second) = CreateActivities();
        Assert.Throws<ArgumentException>(() => SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Commit,
            Now,
            [Participant(first, FirstToken)]));
        Assert.Throws<ArgumentException>(() => SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Commit,
            Now,
            [Participant(first, FirstToken), Participant(second, FirstToken)]));
        Assert.Throws<ArgumentException>(() => SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Commit,
            Now,
            [Participant(first, FirstToken), Participant(first, SecondToken)]));
    }

    [Fact]
    public void AbortRequiresAndBindsFailureReason()
    {
        (ActivityInstance first, ActivityInstance second) = CreateActivities();
        SwapDecisionParticipant[] participants =
        [
            Participant(first, FirstToken),
            Participant(second, SecondToken),
        ];

        Assert.Throws<ArgumentException>(() => SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Abort,
            Now,
            participants));

        SwapDecision revisionAbort = SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Abort,
            Now,
            participants,
            FailureCode.RevisionConflict);
        SwapDecision unavailableAbort = SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Abort,
            Now,
            participants,
            FailureCode.PeerUnavailable);

        Assert.Equal(FailureCode.RevisionConflict, revisionAbort.FailureCode);
        Assert.NotEqual(revisionAbort.Digest, unavailableAbort.Digest);
        Assert.True(revisionAbort.TryGetReservationToken(
            first.Placement.DeviceId,
            out SwapReservationToken? token));
        Assert.Equal(FirstToken, token);
    }

    [Fact]
    public void PrepareRequestDigestCoversExpectedRevision()
    {
        (ActivityInstance first, ActivityInstance second) = CreateActivities();
        SwapReservation prepared = SwapReservation.Prepare(
            Operation,
            FirstToken,
            first,
            second,
            Now.AddSeconds(30));
        ActivityInstance changedRevision = ActivityInstance.Active(
            first.Descriptor,
            first.Placement,
            first.Revision + 1);

        Assert.False(prepared.MatchesRequest(
            changedRevision,
            second,
            Now.AddSeconds(30)));
    }

    [Fact]
    public void PrepareRequestDigestCoversBothPlacements()
    {
        (ActivityInstance first, ActivityInstance second) = CreateActivities();
        SwapReservation prepared = SwapReservation.Prepare(
            Operation,
            FirstToken,
            first,
            second,
            Now.AddSeconds(30));
        ActivityInstance changedPlacement = ActivityInstance.Active(
            first.Descriptor,
            ActivityPlacement.On(first.Placement.DeviceId, "other-slot"),
            first.Revision);

        Assert.False(prepared.MatchesRequest(
            changedPlacement,
            second,
            Now.AddSeconds(30)));
    }

    [Fact]
    public void DecisionTokenMustBeBoundToReservationDevice()
    {
        (ActivityInstance first, ActivityInstance second) = CreateActivities();
        SwapReservation prepared = SwapReservation.Prepare(
            Operation,
            FirstToken,
            first,
            second,
            Now.AddSeconds(30));
        SwapDecision swappedBindings = SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Commit,
            Now.AddSeconds(1),
            [Participant(first, SecondToken), Participant(second, FirstToken)]);

        Assert.Throws<InvalidOperationException>(() =>
            prepared.ApplyDecision(swappedBindings));
    }

    private static SwapDecisionParticipant Participant(
        ActivityInstance activity,
        SwapReservationToken token) => SwapDecisionParticipant.Create(
            activity.Placement.DeviceId,
            token);

    private static (ActivityInstance First, ActivityInstance Second) CreateActivities()
    {
        DeviceId firstDevice =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId secondDevice =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        ActivityDescriptor firstDescriptor = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            firstDevice,
            "First",
            "{\"text\":\"first\"}");
        ActivityDescriptor secondDescriptor = ActivityDescriptor.Create(
            ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ActivityKind.Parse("workspace.note/v1"),
            secondDevice,
            "Second",
            "{\"text\":\"second\"}");
        return (
            ActivityInstance.Active(firstDescriptor, ActivityPlacement.On(firstDevice)),
            ActivityInstance.Active(secondDescriptor, ActivityPlacement.On(secondDevice)));
    }
}
