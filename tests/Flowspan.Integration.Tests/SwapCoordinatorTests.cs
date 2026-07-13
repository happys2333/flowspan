using System.Diagnostics.CodeAnalysis;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class SwapCoordinatorTests
{
    [Fact]
    public async Task HappyPathExchangesBothPlacements()
    {
        Fixture fixture = new();

        SwapCoordinatorResult result = await fixture.ExecuteAsync(
            new DirectSwapEndpointChannel(fixture.FirstEndpoint),
            new DirectSwapEndpointChannel(fixture.SecondEndpoint));

        Assert.True(result.IsSuccess);
        fixture.AssertSwapped();
        Assert.True(fixture.Decisions.TryGet(fixture.Context.OperationId, out SwapDecision? decision));
        Assert.Equal(SwapDecisionOutcome.Commit, decision.Outcome);
    }

    [Fact]
    public async Task PrepareFailureAbortsPreparedEndpointAndChangesNeitherActivity()
    {
        Fixture fixture = new();
        var rejectedSecond = new RejectPrepareChannel(
            fixture.SecondEndpoint,
            FailureCode.RevisionConflict);

        SwapCoordinatorResult result = await fixture.ExecuteAsync(
            new DirectSwapEndpointChannel(fixture.FirstEndpoint),
            rejectedSecond);

        Assert.Equal(OperationStatus.Rejected, result.Status);
        Assert.Equal(FailureCode.RevisionConflict, result.FailureCode);
        fixture.AssertOriginals();
        Assert.True(fixture.FirstEndpoint.TryGetReservation(
            fixture.Context.OperationId,
            out SwapReservation? reservation));
        Assert.Equal(SwapReservationPhase.Aborted, reservation.Phase);
        Assert.True(fixture.Decisions.TryGet(fixture.Context.OperationId, out SwapDecision? decision));
        Assert.Equal(SwapDecisionOutcome.Abort, decision.Outcome);
    }

    [Fact]
    public async Task LostSecondPrepareAcknowledgementExplicitlyAbortsBothReservations()
    {
        Fixture fixture = new();
        var firstChannel = new DirectSwapEndpointChannel(fixture.FirstEndpoint);
        var secondChannel = new DeterministicSwapEndpointChannel(
            fixture.SecondEndpoint,
            [ActivityDeliveryFault.DropAcknowledgement],
            []);

        SwapCoordinatorResult result = await fixture.ExecuteAsync(
            firstChannel,
            secondChannel);

        Assert.Equal(OperationStatus.Rejected, result.Status);
        Assert.Equal(FailureCode.AcknowledgementLost, result.FailureCode);
        fixture.AssertOriginals();
        Assert.True(fixture.FirstEndpoint.TryGetReservation(
            fixture.Context.OperationId,
            out SwapReservation? firstReservation));
        Assert.True(fixture.SecondEndpoint.TryGetReservation(
            fixture.Context.OperationId,
            out SwapReservation? secondReservation));
        Assert.Equal(SwapReservationPhase.Aborted, firstReservation.Phase);
        Assert.Equal(SwapReservationPhase.Aborted, secondReservation.Phase);
        Assert.True(fixture.Decisions.TryGet(
            fixture.Context.OperationId,
            out SwapDecision? decision));
        Assert.Equal(SwapDecisionOutcome.Abort, decision.Outcome);
        Assert.Equal(2, decision.ReservationTokens.Length);
    }

    [Fact]
    public async Task DroppedCommitDeliveryRecoversFromDurableDecision()
    {
        Fixture fixture = new();
        var firstChannel = new DeterministicSwapEndpointChannel(
            fixture.FirstEndpoint,
            [ActivityDeliveryFault.DropBeforeDelivery, ActivityDeliveryFault.None]);
        var secondChannel = new DirectSwapEndpointChannel(fixture.SecondEndpoint);

        SwapCoordinatorResult uncertain = await fixture.ExecuteAsync(
            firstChannel,
            secondChannel);

        Assert.Equal(OperationStatus.Recovering, uncertain.Status);
        Assert.Equal(FailureCode.PeerUnavailable, uncertain.FailureCode);
        Assert.True(fixture.FirstEndpoint.TryGetActivity(
            fixture.FirstActivity.Descriptor.Id,
            out _));
        Assert.True(fixture.SecondEndpoint.TryGetActivity(
            fixture.FirstActivity.Descriptor.Id,
            out _));

        SwapCoordinatorResult recovered = await fixture.Coordinator.RecoverAsync(
            fixture.Context.OperationId,
            firstChannel,
            secondChannel);

        Assert.Equal(OperationStatus.Committed, recovered.Status);
        fixture.AssertSwapped();
    }

    [Fact]
    public async Task LostCommitAcknowledgementIsRecoveringEvenIfBothApplied()
    {
        Fixture fixture = new();
        var firstChannel = new DeterministicSwapEndpointChannel(
            fixture.FirstEndpoint,
            [ActivityDeliveryFault.DropAcknowledgement, ActivityDeliveryFault.None]);
        var secondChannel = new DirectSwapEndpointChannel(fixture.SecondEndpoint);

        SwapCoordinatorResult uncertain = await fixture.ExecuteAsync(
            firstChannel,
            secondChannel);

        Assert.Equal(OperationStatus.Recovering, uncertain.Status);
        Assert.Equal(FailureCode.AcknowledgementLost, uncertain.FailureCode);
        fixture.AssertSwapped();

        SwapCoordinatorResult recovered = await fixture.Coordinator.RecoverAsync(
            fixture.Context.OperationId,
            firstChannel,
            secondChannel);

        Assert.Equal(OperationStatus.Committed, recovered.Status);
        fixture.AssertSwapped();
    }

    [Fact]
    public async Task DuplicateCommitDeliveryIsIdempotent()
    {
        Fixture fixture = new();
        var firstChannel = new DeterministicSwapEndpointChannel(
            fixture.FirstEndpoint,
            [ActivityDeliveryFault.DuplicateDelivery]);
        var secondChannel = new DeterministicSwapEndpointChannel(
            fixture.SecondEndpoint,
            [ActivityDeliveryFault.DuplicateDelivery]);

        SwapCoordinatorResult result = await fixture.ExecuteAsync(
            firstChannel,
            secondChannel);

        Assert.Equal(OperationStatus.Committed, result.Status);
        fixture.AssertSwapped();
    }

    [Fact]
    public async Task ExecuteRetryUsesRecordedDecisionWithoutNewTokens()
    {
        Fixture fixture = new();
        var firstChannel = new DirectSwapEndpointChannel(fixture.FirstEndpoint);
        var secondChannel = new DirectSwapEndpointChannel(fixture.SecondEndpoint);
        await fixture.ExecuteAsync(firstChannel, secondChannel);

        SwapCoordinatorResult replay = await fixture.ExecuteAsync(
            firstChannel,
            secondChannel);

        Assert.Equal(OperationStatus.Committed, replay.Status);
        fixture.AssertSwapped();
    }

    private sealed class Fixture
    {
        private static readonly DateTimeOffset Now =
            new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

        public Fixture()
        {
            DeviceId firstDevice =
                DeviceId.Parse("11111111-1111-1111-1111-111111111111");
            DeviceId secondDevice =
                DeviceId.Parse("22222222-2222-2222-2222-222222222222");
            FirstActivity = CreateActivity(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                firstDevice,
                "First",
                "first");
            SecondActivity = CreateActivity(
                "dddddddd-dddd-dddd-dddd-dddddddddddd",
                secondDevice,
                "Second",
                "second");

            var firstCatalog = new InMemoryActivityCatalog();
            var secondCatalog = new InMemoryActivityCatalog();
            firstCatalog.TryAdd(FirstActivity);
            secondCatalog.TryAdd(SecondActivity);
            FirstEndpoint = new InMemorySwapEndpoint(firstDevice, firstCatalog);
            SecondEndpoint = new InMemorySwapEndpoint(secondDevice, secondCatalog);
            Decisions = new InMemorySwapDecisionJournal();
            Coordinator = new SwapCoordinator(
                new TestClock(Now),
                Decisions,
                new DeterministicSwapTokenSource(
                [
                    SwapReservationToken.From(
                        Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")),
                    SwapReservationToken.From(
                        Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")),
                ]));
            Context = OperationContext.Create(
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Now.AddSeconds(30));
        }

        public ActivityInstance FirstActivity { get; }

        public ActivityInstance SecondActivity { get; }

        public InMemorySwapEndpoint FirstEndpoint { get; }

        public InMemorySwapEndpoint SecondEndpoint { get; }

        public InMemorySwapDecisionJournal Decisions { get; }

        public SwapCoordinator Coordinator { get; }

        public OperationContext Context { get; }

        public ValueTask<SwapCoordinatorResult> ExecuteAsync(
            ISwapEndpointChannel first,
            ISwapEndpointChannel second) => Coordinator.ExecuteAsync(
                Context,
                first,
                FirstActivity.Descriptor.Id,
                second,
                SecondActivity.Descriptor.Id);

        public void AssertOriginals()
        {
            Assert.True(FirstEndpoint.TryGetActivity(FirstActivity.Descriptor.Id, out _));
            Assert.True(SecondEndpoint.TryGetActivity(SecondActivity.Descriptor.Id, out _));
        }

        public void AssertSwapped()
        {
            Assert.True(FirstEndpoint.TryGetActivity(
                SecondActivity.Descriptor.Id,
                out ActivityInstance? onFirst));
            Assert.Equal(FirstEndpoint.DeviceId, onFirst.Placement.DeviceId);
            Assert.True(SecondEndpoint.TryGetActivity(
                FirstActivity.Descriptor.Id,
                out ActivityInstance? onSecond));
            Assert.Equal(SecondEndpoint.DeviceId, onSecond.Placement.DeviceId);
            Assert.False(FirstEndpoint.TryGetActivity(FirstActivity.Descriptor.Id, out _));
            Assert.False(SecondEndpoint.TryGetActivity(SecondActivity.Descriptor.Id, out _));
        }

        private static ActivityInstance CreateActivity(
            string activityId,
            DeviceId deviceId,
            string title,
            string text)
        {
            ActivityDescriptor descriptor = ActivityDescriptor.Create(
                ActivityId.Parse(activityId),
                ActivityKind.Parse("workspace.note/v1"),
                deviceId,
                title,
                $"{{\"text\":\"{text}\"}}");
            return ActivityInstance.Active(descriptor, ActivityPlacement.On(deviceId));
        }
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class RejectPrepareChannel(
        ISwapEndpoint target,
        FailureCode failureCode) : ISwapEndpointChannel
    {
        public DeviceId TargetDeviceId => target.DeviceId;

        public bool TryGetActivity(
            ActivityId activityId,
            [NotNullWhen(true)] out ActivityInstance? activity) =>
            target.TryGetActivity(activityId, out activity);

        public ValueTask<SwapDeliveryResult<SwapPrepareResult>> PrepareAsync(
            SwapPrepareCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(SwapDelivery.Acknowledged(
                SwapPrepareResult.Rejected(failureCode)));
        }

        public async ValueTask<SwapDeliveryResult<SwapApplyResult>> ApplyDecisionAsync(
            SwapDecision decision,
            CancellationToken cancellationToken)
        {
            SwapApplyResult response = await target.ApplyDecisionAsync(
                decision,
                cancellationToken);
            return SwapDelivery.Acknowledged(response);
        }
    }
}
