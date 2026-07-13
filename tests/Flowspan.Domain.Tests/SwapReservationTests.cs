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
            [FirstToken, SecondToken]);

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
            [FirstToken, SecondToken]);

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
            [FirstToken, SecondToken]);

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
            [FirstToken]);
        SwapReservation aborted = prepared.ApplyDecision(abort);

        Assert.Same(aborted, aborted.ApplyDecision(abort));

        SwapDecision commit = SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Commit,
            Now.AddSeconds(1),
            [FirstToken, SecondToken]);
        Assert.Throws<InvalidOperationException>(() => aborted.ApplyDecision(commit));
    }

    [Fact]
    public void CommitDecisionRequiresTwoDistinctReservations()
    {
        Assert.Throws<ArgumentException>(() => SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Commit,
            Now,
            [FirstToken]));
        Assert.Throws<ArgumentException>(() => SwapDecision.Create(
            Operation,
            SwapDecisionOutcome.Commit,
            Now,
            [FirstToken, FirstToken]));
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
