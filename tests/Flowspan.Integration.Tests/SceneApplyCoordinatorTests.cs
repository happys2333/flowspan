using System.Collections.Immutable;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class SceneApplyCoordinatorTests
{
    private static readonly DateTimeOffset AcceptedAt =
        new(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BlockedNoChangeAndTerminalFailureContinueInSavedOrder()
    {
        CoordinatorFixture fixture = CreateFixture(
            FixtureItem.Blocked,
            FixtureItem.NoChange,
            FixtureItem.Handoff,
            FixtureItem.Handoff);
        var clock = new MutableClock(AcceptedAt);
        var journal = new InMemorySceneApplyJournal();
        var port = new ScriptedOperationPort(
            fixture.Descriptors,
            clock,
            new Dictionary<int, OperationStatus>
            {
                [2] = OperationStatus.Failed,
                [3] = OperationStatus.Committed,
            });
        var coordinator = new SceneApplyCoordinator(clock, journal, port);

        SceneApplyExecutionResult execution = await coordinator.ApplyAsync(
            fixture.Scene,
            fixture.Preview,
            fixture.Approval,
            CancellationToken.None);

        SceneApplyResult result = Assert.IsType<SceneApplyResult>(execution.Result);
        Assert.Equal(SceneApplyApprovalStatus.Valid, execution.ApprovalStatus);
        Assert.Equal(SceneApplyOverallStatus.PartiallyCompleted, result.Status);
        Assert.Equal([2, 3], port.CalledIndices);
        Assert.Collection(
            result.Items,
            item => Assert.Equal(SceneApplyItemOutcome.Blocked, item.Outcome),
            item => Assert.Equal(SceneApplyItemOutcome.NoChange, item.Outcome),
            item => Assert.Equal(SceneApplyItemOutcome.Failed, item.Outcome),
            item => Assert.Equal(SceneApplyItemOutcome.Committed, item.Outcome));
        Assert.True((await journal.LoadAsync(
            fixture.Preview.ParentOperationId,
            CancellationToken.None))?.IsCompleted);
    }

    [Fact]
    public async Task RecoveringStopsBeforeEveryLaterItem()
    {
        CoordinatorFixture fixture = CreateFixture(
            FixtureItem.Handoff,
            FixtureItem.Handoff,
            FixtureItem.Handoff);
        var clock = new MutableClock(AcceptedAt);
        var journal = new InMemorySceneApplyJournal();
        var port = new ScriptedOperationPort(
            fixture.Descriptors,
            clock,
            new Dictionary<int, OperationStatus>
            {
                [0] = OperationStatus.Recovering,
                [1] = OperationStatus.Committed,
                [2] = OperationStatus.Committed,
            });
        var coordinator = new SceneApplyCoordinator(clock, journal, port);

        SceneApplyExecutionResult execution = await coordinator.ApplyAsync(
            fixture.Scene,
            fixture.Preview,
            fixture.Approval,
            CancellationToken.None);

        SceneApplyResult result = Assert.IsType<SceneApplyResult>(execution.Result);
        Assert.Equal(SceneApplyOverallStatus.Recovering, result.Status);
        Assert.Equal([0], port.CalledIndices);
        Assert.Collection(
            result.Items,
            item => Assert.Equal(SceneApplyItemOutcome.Recovering, item.Outcome),
            item => Assert.Equal(
                SceneApplyItemReason.NotAttemptedAfterRecovering,
                item.Reason),
            item => Assert.Equal(
                SceneApplyItemReason.NotAttemptedAfterRecovering,
                item.Reason));
    }

    [Fact]
    public async Task UnexpectedOperationExceptionBecomesRecoveringWithoutGuessing()
    {
        CoordinatorFixture fixture = CreateFixture(
            FixtureItem.Handoff,
            FixtureItem.Handoff);
        var clock = new MutableClock(AcceptedAt);
        var journal = new InMemorySceneApplyJournal();
        var port = new ScriptedOperationPort(
            fixture.Descriptors,
            clock,
            statuses: ImmutableDictionary<int, OperationStatus>.Empty,
            throwOnIndex: 0);
        var coordinator = new SceneApplyCoordinator(clock, journal, port);

        SceneApplyExecutionResult execution = await coordinator.ApplyAsync(
            fixture.Scene,
            fixture.Preview,
            fixture.Approval,
            CancellationToken.None);

        SceneApplyResult result = Assert.IsType<SceneApplyResult>(execution.Result);
        Assert.Equal(SceneApplyOverallStatus.Recovering, result.Status);
        Assert.Equal(FailureCode.InternalFailure, result.Items[0].FailureCode);
        Assert.Equal(SceneApplyItemOutcome.NotAttempted, result.Items[1].Outcome);
        Assert.Equal([0], port.CalledIndices);
    }

    [Fact]
    public async Task CancellationBeforeFirstItemMutatesNoActivityAndIsDurable()
    {
        CoordinatorFixture fixture = CreateFixture(
            FixtureItem.Handoff,
            FixtureItem.Handoff);
        var clock = new MutableClock(AcceptedAt);
        var journal = new InMemorySceneApplyJournal();
        var port = new ScriptedOperationPort(
            fixture.Descriptors,
            clock,
            statuses: ImmutableDictionary<int, OperationStatus>.Empty);
        var coordinator = new SceneApplyCoordinator(clock, journal, port);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        SceneApplyExecutionResult execution = await coordinator.ApplyAsync(
            fixture.Scene,
            fixture.Preview,
            fixture.Approval,
            cancellation.Token);

        SceneApplyResult result = Assert.IsType<SceneApplyResult>(execution.Result);
        Assert.Equal(SceneApplyOverallStatus.Cancelled, result.Status);
        Assert.Empty(port.CalledIndices);
        Assert.All(
            result.Items,
            item => Assert.Equal(SceneApplyItemReason.Cancelled, item.Reason));
        Assert.True((await journal.LoadAsync(
            fixture.Preview.ParentOperationId,
            CancellationToken.None))?.IsCompleted);
    }

    [Fact]
    public async Task AcceptedAttemptReplaysAfterPreviewExpiryWithoutAnotherOperation()
    {
        CoordinatorFixture fixture = CreateFixture(FixtureItem.Handoff);
        var clock = new MutableClock(AcceptedAt);
        var journal = new InMemorySceneApplyJournal();
        var firstPort = new ScriptedOperationPort(
            fixture.Descriptors,
            clock,
            new Dictionary<int, OperationStatus>
            {
                [0] = OperationStatus.Committed,
            });
        var first = new SceneApplyCoordinator(clock, journal, firstPort);
        SceneApplyExecutionResult initial = await first.ApplyAsync(
            fixture.Scene,
            fixture.Preview,
            fixture.Approval,
            CancellationToken.None);
        clock.UtcNow = fixture.Preview.ExpiresAt.AddHours(1);
        var replayPort = new ScriptedOperationPort(
            fixture.Descriptors,
            clock,
            statuses: ImmutableDictionary<int, OperationStatus>.Empty,
            throwOnIndex: 0);
        var replay = new SceneApplyCoordinator(clock, journal, replayPort);

        SceneApplyExecutionResult repeated = await replay.ApplyAsync(
            fixture.Scene,
            fixture.Preview,
            fixture.Approval,
            CancellationToken.None);

        Assert.Equal(SceneApplyOverallStatus.Completed, initial.Result?.Status);
        Assert.Equal(SceneApplyOverallStatus.Completed, repeated.Result?.Status);
        Assert.Empty(replayPort.CalledIndices);
        Assert.Equal(
            initial.Result?.Items[0].ChildOperationId,
            repeated.Result?.Items[0].ChildOperationId);
    }

    [Fact]
    public async Task StartedWithoutTerminalOutcomeRecoversOnRestartWithoutPortCall()
    {
        CoordinatorFixture fixture = CreateFixture(
            FixtureItem.Handoff,
            FixtureItem.Handoff);
        var clock = new MutableClock(AcceptedAt);
        var journal = new InMemorySceneApplyJournal();
        await journal.CreateAsync(
            fixture.Preview,
            AcceptedAt,
            CancellationToken.None);
        await journal.RecordItemStartedAsync(
            fixture.Preview.ParentOperationId,
            0,
            AcceptedAt,
            CancellationToken.None);
        var port = new ScriptedOperationPort(
            fixture.Descriptors,
            clock,
            statuses: ImmutableDictionary<int, OperationStatus>.Empty,
            throwOnIndex: 0);
        var coordinator = new SceneApplyCoordinator(clock, journal, port);

        SceneApplyExecutionResult execution = await coordinator.ApplyAsync(
            fixture.Scene,
            fixture.Preview,
            fixture.Approval,
            CancellationToken.None);

        SceneApplyResult result = Assert.IsType<SceneApplyResult>(execution.Result);
        Assert.Equal(SceneApplyOverallStatus.Recovering, result.Status);
        Assert.Equal(FailureCode.OperationInProgress, result.Items[0].FailureCode);
        Assert.Empty(port.CalledIndices);
    }

    [Fact]
    public async Task InvalidApprovalCreatesNoAttemptAndCallsNoOperation()
    {
        CoordinatorFixture fixture = CreateFixture(FixtureItem.Handoff);
        var clock = new MutableClock(AcceptedAt);
        var journal = new InMemorySceneApplyJournal();
        var port = new ScriptedOperationPort(
            fixture.Descriptors,
            clock,
            statuses: ImmutableDictionary<int, OperationStatus>.Empty);
        var coordinator = new SceneApplyCoordinator(clock, journal, port);
        SceneApplyApproval wrong = SceneApplyApproval.Create(
            new string('F', 64),
            []);

        SceneApplyExecutionResult execution = await coordinator.ApplyAsync(
            fixture.Scene,
            fixture.Preview,
            wrong,
            CancellationToken.None);

        Assert.Equal(
            SceneApplyApprovalStatus.PreviewMismatch,
            execution.ApprovalStatus);
        Assert.Null(execution.Result);
        Assert.Equal(0, journal.EntryCount);
        Assert.Empty(port.CalledIndices);
    }

    private static CoordinatorFixture CreateFixture(params FixtureItem[] kinds)
    {
        DeviceId sourceDevice = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId targetDevice = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityKind activityKind = ActivityKind.Parse("workspace.note/v1");
        var plans = new List<SceneActivityPlan>();
        var previews = new List<SceneApplyItemPreview>();
        var descriptors = ImmutableDictionary.CreateBuilder<
            ActivityId,
            ActivityDescriptor>();
        for (int index = 0; index < kinds.Length; index++)
        {
            ActivityId activityId = ActivityId.From(Guid.Parse(
                $"00000000-0000-0000-0000-{index + 1:000000000000}"));
            ActivityDescriptor descriptor = ActivityDescriptor.Create(
                activityId,
                activityKind,
                sourceDevice,
                $"coordinator-title-canary-{index}",
                $"{{\"coordinator-payload-canary\":{index}}}");
            descriptors.Add(activityId, descriptor);
            ActivityPlacement destination = ActivityPlacement.On(
                targetDevice,
                $"coordinator-destination-canary-{index}");
            SceneActivityPlan plan = SceneActivityPlan.Place(
                activityId,
                destination,
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.RequireEmpty);
            plans.Add(plan);
            OperationId operationId = OperationId.From(Guid.Parse(
                $"10000000-0000-0000-0000-{index + 1:000000000000}"));
            CorrelationId correlationId = CorrelationId.From(Guid.Parse(
                $"20000000-0000-0000-0000-{index + 1:000000000000}"));
            if (kinds[index] == FixtureItem.Blocked)
            {
                previews.Add(SceneApplyItemResolver.Resolve(
                    plan,
                    SceneSourceLookup.FromObservation(
                        index,
                        activityId,
                        [],
                        isComplete: true),
                    explicitSelection: null,
                    occupancy: null,
                    operationId,
                    correlationId));
                continue;
            }

            ActivityPlacement sourcePlacement = kinds[index]
                == FixtureItem.NoChange
                    ? destination
                    : ActivityPlacement.On(sourceDevice, $"source-{index}");
            SceneSourceSelection source = SceneSourceSelection.Create(
                index,
                activityId,
                1,
                descriptor.DescriptorDigest,
                activityKind,
                sourcePlacement);
            previews.Add(SceneApplyItemResolver.Resolve(
                plan,
                SceneSourceLookup.FromObservation(
                    index,
                    activityId,
                    [source],
                    isComplete: true),
                explicitSelection: null,
                kinds[index] == FixtureItem.NoChange
                    ? null
                    : SceneSlotOccupancy.Empty,
                operationId,
                correlationId));
        }

        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "coordinator-scene-canary",
            plans);
        SceneApplyPreview preview = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"),
            AcceptedAt.AddMinutes(-1),
            AcceptedAt.AddMinutes(4),
            previews);
        SceneApplyApproval approval = SceneApplyApproval.Create(
            preview.Fingerprint,
            preview.RequiredReplaceConfirmations);
        return new CoordinatorFixture(
            scene,
            preview,
            approval,
            descriptors.ToImmutable());
    }

    private enum FixtureItem
    {
        Blocked,
        NoChange,
        Handoff,
    }

    private sealed record CoordinatorFixture(
        ScenePlan Scene,
        SceneApplyPreview Preview,
        SceneApplyApproval Approval,
        ImmutableDictionary<ActivityId, ActivityDescriptor> Descriptors);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class ScriptedOperationPort(
        ImmutableDictionary<ActivityId, ActivityDescriptor> descriptors,
        MutableClock clock,
        IReadOnlyDictionary<int, OperationStatus> statuses,
        int? throwOnIndex = null) : ISceneActivityOperationPort
    {
        public List<int> CalledIndices { get; } = [];

        public ValueTask<SceneActivityOperationResult> ExecuteAsync(
            SceneActivityPreparation preparation,
            CancellationToken cancellationToken)
        {
            CalledIndices.Add(preparation.Item.Index);
            if (preparation.Item.Index == throwOnIndex)
            {
                throw new InvalidOperationException("operation-exception-canary");
            }

            cancellationToken.ThrowIfCancellationRequested();
            SceneApplyItemPreview item = preparation.Item;
            SceneSourceSelection source = Assert.IsType<SceneSourceSelection>(
                item.Source);
            ActivityDescriptor descriptor = descriptors[item.ActivityId];
            OperationStatus status = statuses[item.Index];
            OperationReceipt receipt = status switch
            {
                OperationStatus.Committed => OperationReceipt.Committed(
                    item.ChildOperationId,
                    item.ChildCorrelationId,
                    OperationKind.Handoff,
                    source.DeviceId,
                    item.Destination.DeviceId,
                    descriptor,
                    clock.UtcNow),
                OperationStatus.Failed => OperationReceipt.Failed(
                    item.ChildOperationId,
                    item.ChildCorrelationId,
                    OperationKind.Handoff,
                    source.DeviceId,
                    item.Destination.DeviceId,
                    descriptor,
                    clock.UtcNow,
                    FailureCode.AdapterUnavailable),
                OperationStatus.Recovering => OperationReceipt.Recovering(
                    item.ChildOperationId,
                    item.ChildCorrelationId,
                    OperationKind.Handoff,
                    source.DeviceId,
                    item.Destination.DeviceId,
                    descriptor,
                    clock.UtcNow,
                    FailureCode.AcknowledgementLost),
                _ => throw new InvalidOperationException(
                    "The scripted Scene operation status is unsupported."),
            };
            return ValueTask.FromResult(
                SceneActivityOperationResult.Create(receipt, undoCapsule: null));
        }
    }
}
