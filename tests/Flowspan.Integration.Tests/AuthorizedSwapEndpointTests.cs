using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class AuthorizedSwapEndpointTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 16, 7, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId SourceId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId TargetId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DeviceId ThirdId =
        DeviceId.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly OperationContext Context = OperationContext.Create(
        OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        Now.AddSeconds(30));

    [Fact]
    public async Task SnapshotAndPrepareDenyWithoutIndependentSwapCapability()
    {
        Fixture fixture = new();

        SwapActivitySnapshotResult snapshot = await fixture.Authorized.QueryActivityAsync(
            SourceId,
            fixture.Query,
            default);
        SwapPrepareResult prepared = await fixture.Authorized.PrepareAsync(
            SourceId,
            fixture.Prepare,
            default);

        Assert.False(snapshot.IsSuccess);
        Assert.Equal(FailureCode.CapabilityDenied, snapshot.FailureCode);
        Assert.Null(snapshot.Activity);
        Assert.False(prepared.Prepared);
        Assert.Equal(FailureCode.CapabilityDenied, prepared.FailureCode);
        Assert.False(fixture.Endpoint.MatchesOperation(
            Context.OperationId,
            Context.CorrelationId,
            SourceId));
    }

    [Fact]
    public async Task ReplaceCapabilityDoesNotAuthorizeSwap()
    {
        Fixture fixture = new();
        fixture.Authorized.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivityReplace));

        SwapActivitySnapshotResult snapshot = await fixture.Authorized.QueryActivityAsync(
            SourceId,
            fixture.Query,
            default);
        SwapPrepareResult prepared = await fixture.Authorized.PrepareAsync(
            SourceId,
            fixture.Prepare,
            default);

        Assert.Equal(FailureCode.CapabilityDenied, snapshot.FailureCode);
        Assert.Equal(FailureCode.CapabilityDenied, prepared.FailureCode);
    }

    [Fact]
    public async Task ExpiredPrepareIsRejectedBeforeReservation()
    {
        Fixture fixture = new();
        fixture.Authorized.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivitySwap));
        SwapPrepareCommand expired = fixture.Prepare with { ExpiresAt = Now };

        SwapPrepareResult prepared = await fixture.Authorized.PrepareAsync(
            SourceId,
            expired,
            default);

        Assert.False(prepared.Prepared);
        Assert.Equal(FailureCode.DeadlineExpired, prepared.FailureCode);
        Assert.False(fixture.Endpoint.MatchesOperation(
            Context.OperationId,
            Context.CorrelationId,
            SourceId));
    }

    [Fact]
    public async Task PrepareRequiresAuthenticatedSenderToOwnIncomingActivity()
    {
        Fixture fixture = new();
        fixture.Authorized.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivitySwap));
        SwapPrepareCommand forged = fixture.Prepare with
        {
            IncomingActivity = CreateActivity(
                ThirdId,
                "ffffffff-ffff-ffff-ffff-ffffffffffff",
                "Third"),
        };

        SwapPrepareResult prepared = await fixture.Authorized.PrepareAsync(
            SourceId,
            forged,
            default);

        Assert.False(prepared.Prepared);
        Assert.Equal(FailureCode.DescriptorRejected, prepared.FailureCode);
        Assert.False(fixture.Endpoint.MatchesOperation(
            Context.OperationId,
            Context.CorrelationId,
            SourceId));
    }

    [Fact]
    public async Task NewPrepareRejectsSensitiveActivityWithoutRelyingOnSnapshot()
    {
        Fixture fixture = new();
        fixture.Authorized.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivitySwap));
        SwapPrepareCommand sensitiveOriginal = fixture.Prepare with
        {
            OriginalActivity = CreateActivity(
                TargetId,
                "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
                "Sensitive target",
                ActivitySensitivity.Sensitive),
        };
        SwapPrepareCommand sensitiveIncoming = fixture.Prepare with
        {
            IncomingActivity = CreateActivity(
                SourceId,
                "ffffffff-ffff-ffff-ffff-ffffffffffff",
                "Sensitive source",
                ActivitySensitivity.Sensitive),
        };

        SwapPrepareResult originalResult = await fixture.Authorized.PrepareAsync(
            SourceId,
            sensitiveOriginal,
            default);
        SwapPrepareResult incomingResult = await fixture.Authorized.PrepareAsync(
            SourceId,
            sensitiveIncoming,
            default);

        Assert.Equal(FailureCode.DescriptorRejected, originalResult.FailureCode);
        Assert.Equal(FailureCode.DescriptorRejected, incomingResult.FailureCode);
        Assert.False(fixture.Endpoint.MatchesOperation(
            Context.OperationId,
            Context.CorrelationId,
            SourceId));
    }

    [Fact]
    public async Task ExactPrepareReplaySurvivesExpiryAndCapabilityRevocation()
    {
        Fixture fixture = new();
        fixture.Authorized.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivitySwap));
        SwapPrepareResult initial = await fixture.Authorized.PrepareAsync(
            SourceId,
            fixture.Prepare,
            default);
        fixture.Authorized.SetPeerGrant(SourceId, CapabilityGrant.None);
        fixture.Clock.UtcNow = Context.Deadline;

        SwapPrepareResult replay = await fixture.Authorized.PrepareAsync(
            SourceId,
            fixture.Prepare,
            default);
        SwapPrepareResult wrongCorrelation = await fixture.Authorized.PrepareAsync(
            SourceId,
            fixture.Prepare with
            {
                CorrelationId = CorrelationId.Parse(
                    "99999999-9999-9999-9999-999999999999"),
            },
            default);

        Assert.True(initial.Prepared);
        Assert.True(replay.Prepared);
        Assert.Equal(fixture.TargetToken, replay.ReservationToken);
        Assert.False(wrongCorrelation.Prepared);
        Assert.Equal(FailureCode.DeadlineExpired, wrongCorrelation.FailureCode);
    }

    [Fact]
    public async Task GrantedDecisionStillRequiresRecordedCorrelationBinding()
    {
        Fixture fixture = new();
        fixture.Authorized.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivitySwap));
        Assert.True((await fixture.Authorized.PrepareAsync(
            SourceId,
            fixture.Prepare,
            default)).Prepared);

        SwapApplyResult result = await fixture.Authorized.ApplyDecisionAsync(
            SourceId,
            CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
            fixture.Commit,
            default);

        Assert.False(result.Applied);
        Assert.Equal(FailureCode.DecisionConflict, result.FailureCode);
        Assert.True(fixture.Catalog.TryGet(fixture.TargetActivity.Descriptor.Id, out _));
        Assert.False(fixture.Catalog.TryGet(fixture.SourceActivity.Descriptor.Id, out _));
    }

    [Fact]
    public async Task DecisionRequiresAuthenticatedSenderToBeRemoteParticipant()
    {
        Fixture fixture = new();
        fixture.Authorized.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivitySwap));
        Assert.True((await fixture.Authorized.PrepareAsync(
            SourceId,
            fixture.Prepare,
            default)).Prepared);
        SwapDecision forged = SwapDecision.Create(
            Context.OperationId,
            SwapDecisionOutcome.Commit,
            Now.AddSeconds(1),
            [
                SwapDecisionParticipant.Create(ThirdId, fixture.SourceToken),
                SwapDecisionParticipant.Create(TargetId, fixture.TargetToken),
            ]);

        SwapApplyResult applied = await fixture.Authorized.ApplyDecisionAsync(
            SourceId,
            Context.CorrelationId,
            forged,
            default);

        Assert.False(applied.Applied);
        Assert.Equal(FailureCode.DecisionConflict, applied.FailureCode);
        Assert.True(fixture.Catalog.TryGet(fixture.TargetActivity.Descriptor.Id, out _));
        Assert.False(fixture.Catalog.TryGet(fixture.SourceActivity.Descriptor.Id, out _));
    }

    [Fact]
    public async Task AuthorizedSnapshotReturnsOnlyExactActiveNormalActivity()
    {
        Fixture fixture = new();
        fixture.Authorized.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivitySwap));
        ActivityInstance sensitive = CreateActivity(
            TargetId,
            "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
            "Sensitive",
            ActivitySensitivity.Sensitive);
        Assert.True(fixture.Catalog.TryAdd(sensitive));

        SwapActivitySnapshotResult exact = await fixture.Authorized.QueryActivityAsync(
            SourceId,
            fixture.Query,
            default);
        SwapActivitySnapshotResult blocked = await fixture.Authorized.QueryActivityAsync(
            SourceId,
            SwapActivitySnapshotQuery.Create(
                Context,
                TargetId,
                sensitive.Descriptor.Id),
            default);
        SwapActivitySnapshotResult missing = await fixture.Authorized.QueryActivityAsync(
            SourceId,
            SwapActivitySnapshotQuery.Create(
                Context,
                TargetId,
                ActivityId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")),
            default);

        Assert.True(exact.IsSuccess);
        Assert.Equal(fixture.TargetActivity, exact.Activity);
        Assert.Equal(FailureCode.DescriptorRejected, blocked.FailureCode);
        Assert.Null(blocked.Activity);
        Assert.Equal(FailureCode.ActivityNotFound, missing.FailureCode);
        Assert.Null(missing.Activity);
    }

    [Fact]
    public async Task RevocationDeniesUnknownDecisionButAllowsRecordedConvergence()
    {
        Fixture fixture = new();
        fixture.Authorized.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivitySwap));
        Assert.True((await fixture.Authorized.PrepareAsync(
            SourceId,
            fixture.Prepare,
            default)).Prepared);
        fixture.Authorized.SetPeerGrant(SourceId, CapabilityGrant.None);

        SwapApplyResult wrongCorrelation = await fixture.Authorized.ApplyDecisionAsync(
            SourceId,
            CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
            fixture.Commit,
            default);
        SwapApplyResult committed = await fixture.Authorized.ApplyDecisionAsync(
            SourceId,
            Context.CorrelationId,
            fixture.Commit,
            default);
        SwapApplyResult replay = await fixture.Authorized.ApplyDecisionAsync(
            SourceId,
            Context.CorrelationId,
            fixture.Commit,
            default);
        SwapDecision unknownAbort = SwapDecision.Create(
            OperationId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            SwapDecisionOutcome.Abort,
            Now.AddSeconds(1),
            [
                SwapDecisionParticipant.Create(SourceId, fixture.SourceToken),
                SwapDecisionParticipant.Create(
                    TargetId,
                    SwapReservationToken.From(
                        Guid.Parse("30303030-3030-3030-3030-303030303030"))),
            ],
            FailureCode.CapabilityDenied);
        SwapApplyResult denied = await fixture.Authorized.ApplyDecisionAsync(
            SourceId,
            Context.CorrelationId,
            unknownAbort,
            default);

        Assert.False(wrongCorrelation.Applied);
        Assert.Equal(FailureCode.CapabilityDenied, wrongCorrelation.FailureCode);
        Assert.True(committed.Applied);
        Assert.Equal(SwapReservationPhase.Committed, committed.Phase);
        Assert.True(replay.Applied);
        Assert.Equal(FailureCode.CapabilityDenied, denied.FailureCode);
        Assert.False(fixture.Endpoint.MatchesOperation(
            unknownAbort.OperationId,
            Context.CorrelationId,
            SourceId));
        Assert.False(fixture.Catalog.TryGet(fixture.TargetActivity.Descriptor.Id, out _));
        Assert.True(fixture.Catalog.TryGet(fixture.SourceActivity.Descriptor.Id, out _));
    }

    private static ActivityInstance CreateActivity(
        DeviceId deviceId,
        string activityId,
        string title,
        ActivitySensitivity sensitivity = ActivitySensitivity.Normal) =>
        ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse(activityId),
                ActivityKind.Parse("workspace.note/v1"),
                deviceId,
                title,
                JsonSerializer.Serialize(new { text = title }),
                sensitivity),
            ActivityPlacement.On(deviceId, "desktop"),
            revision: 3);

    private sealed class Fixture
    {
        public Fixture()
        {
            SourceActivity = CreateActivity(
                SourceId,
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "Source");
            TargetActivity = CreateActivity(
                TargetId,
                "dddddddd-dddd-dddd-dddd-dddddddddddd",
                "Target");
            Catalog.TryAdd(TargetActivity);
            Endpoint = new InMemorySwapEndpoint(TargetId, Catalog);
            Clock = new TestClock(Now);
            Authorized = new AuthorizedSwapEndpoint(Clock, Endpoint);
            Query = SwapActivitySnapshotQuery.Create(
                Context,
                TargetId,
                TargetActivity.Descriptor.Id);
            TargetToken = SwapReservationToken.From(
                Guid.Parse("20202020-2020-2020-2020-202020202020"));
            SourceToken = SwapReservationToken.From(
                Guid.Parse("10101010-1010-1010-1010-101010101010"));
            Prepare = new SwapPrepareCommand(
                Context.OperationId,
                Context.CorrelationId,
                TargetToken,
                TargetActivity,
                SourceActivity,
                Context.Deadline);
            Commit = SwapDecision.Create(
                Context.OperationId,
                SwapDecisionOutcome.Commit,
                Now.AddSeconds(1),
                [
                    SwapDecisionParticipant.Create(SourceId, SourceToken),
                    SwapDecisionParticipant.Create(TargetId, TargetToken),
                ]);
        }

        public AuthorizedSwapEndpoint Authorized { get; }

        public InMemoryActivityCatalog Catalog { get; } = new();

        public TestClock Clock { get; }

        public SwapDecision Commit { get; }

        public InMemorySwapEndpoint Endpoint { get; }

        public SwapPrepareCommand Prepare { get; }

        public SwapActivitySnapshotQuery Query { get; }

        public ActivityInstance SourceActivity { get; }

        public SwapReservationToken SourceToken { get; }

        public ActivityInstance TargetActivity { get; }

        public SwapReservationToken TargetToken { get; }
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
