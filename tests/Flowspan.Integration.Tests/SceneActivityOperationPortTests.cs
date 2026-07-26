using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Application.Adapters;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class SceneActivityOperationPortTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 6, 0, 0, TimeSpan.Zero);
    private static readonly DeviceId SourceDevice =
        DeviceId.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly DeviceId TargetDevice =
        DeviceId.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly ActivityId IncomingActivity =
        ActivityId.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly ActivityId OccupyingActivity =
        ActivityId.Parse("30000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task HandoffRoutesThroughTheExistingNodes()
    {
        var fixture = new Fixture();
        ScenePlan scene = Fixture.CreateScene(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        fixture.GrantSourceToReceiveAtTarget();
        fixture.GrantTargetToApplyAndAcceptOffer();

        SceneApplyExecutionResult execution = await fixture.ApplyAsync(scene);

        SceneApplyResult result = Assert.IsType<SceneApplyResult>(
            execution.Result);
        Assert.Equal(SceneApplyOverallStatus.Completed, result.Status);
        Assert.Equal(
            SceneApplyItemOutcome.Committed,
            Assert.Single(result.Items).Outcome);
        Assert.True(fixture.SourceNode.TryGetActivity(
            IncomingActivity,
            out ActivityInstance? source));
        Assert.Equal(ActivityLifecycle.Active, source.Lifecycle);
        Assert.True(fixture.TargetNode.TryGetActivity(
            IncomingActivity,
            out ActivityInstance? target));
        Assert.Equal(
            ActivityPlacement.On(TargetDevice, "destination"),
            target.Placement);
    }

    [Fact]
    public async Task MoveRoutesThroughTheExistingNodesAndClosesSource()
    {
        var fixture = new Fixture();
        ScenePlan scene = Fixture.CreateScene(
            SceneSourceDisposition.MoveAfterAcknowledgement,
            SceneConflictPolicy.RequireEmpty);
        fixture.GrantSourceToReceiveAtTarget();
        fixture.GrantTargetToApplyAndAcceptOffer();

        SceneApplyExecutionResult execution = await fixture.ApplyAsync(scene);

        SceneApplyResult result = Assert.IsType<SceneApplyResult>(
            execution.Result);
        Assert.Equal(SceneApplyOverallStatus.Completed, result.Status);
        Assert.Equal(
            SceneApplyItemOutcome.Committed,
            Assert.Single(result.Items).Outcome);
        Assert.True(fixture.SourceNode.TryGetActivity(
            IncomingActivity,
            out ActivityInstance? source));
        Assert.Equal(ActivityLifecycle.Closed, source.Lifecycle);
        Assert.True(fixture.TargetNode.TryGetActivity(
            IncomingActivity,
            out ActivityInstance? target));
        Assert.Equal(ActivityLifecycle.Active, target.Lifecycle);
    }

    [Fact]
    public async Task ChangedSourceRevisionIsRejectedAsRevisionConflict()
    {
        var fixture = new Fixture();
        ScenePlan scene = Fixture.CreateScene(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        fixture.GrantSourceToReceiveAtTarget();
        fixture.GrantTargetToApplyAndAcceptOffer();

        fixture.SourceCatalog.RaceOnNextRead(static current =>
            ActivityInstance.Active(
                current.Descriptor,
                current.Placement,
                current.Revision + 1));

        SceneApplyExecutionResult execution = await fixture.ApplyAsync(scene);

        SceneApplyItemResult item = Assert.Single(
            Assert.IsType<SceneApplyResult>(execution.Result).Items);
        Assert.Equal(SceneApplyItemOutcome.Rejected, item.Outcome);
        Assert.Equal(FailureCode.RevisionConflict, item.FailureCode);
        Assert.False(fixture.TargetNode.TryGetActivity(IncomingActivity, out _));
    }

    [Fact]
    public async Task VanishedSourceIsRejectedAsActivityNotFound()
    {
        var fixture = new Fixture();
        ScenePlan scene = Fixture.CreateScene(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        fixture.GrantSourceToReceiveAtTarget();
        fixture.GrantTargetToApplyAndAcceptOffer();

        fixture.SourceCatalog.HideOnNextRead();

        SceneApplyExecutionResult execution = await fixture.ApplyAsync(scene);

        SceneApplyItemResult item = Assert.Single(
            Assert.IsType<SceneApplyResult>(execution.Result).Items);
        Assert.Equal(SceneApplyItemOutcome.Rejected, item.Outcome);
        Assert.Equal(FailureCode.ActivityNotFound, item.FailureCode);
        Assert.False(fixture.TargetNode.TryGetActivity(IncomingActivity, out _));
    }

    [Fact]
    public async Task SourceChangedAfterRecheckIsNotSentToTheTarget()
    {
        var fixture = new Fixture();
        ScenePlan scene = Fixture.CreateScene(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        fixture.GrantSourceToReceiveAtTarget();
        fixture.GrantTargetToApplyAndAcceptOffer();
        fixture.SourceCatalog.RaceOnRead(
            skip: 1,
            race: static current => ActivityInstance.Active(
                ActivityDescriptor.Create(
                    current.Descriptor.Id,
                    current.Descriptor.Kind,
                    current.Descriptor.OriginDeviceId,
                    "raced-title-canary",
                    JsonSerializer.Serialize(new
                    {
                        text = "raced-payload-canary",
                    })),
                current.Placement,
                current.Revision + 1));

        SceneApplyExecutionResult execution = await fixture.ApplyAsync(scene);

        SceneApplyItemResult item = Assert.Single(
            Assert.IsType<SceneApplyResult>(execution.Result).Items);
        Assert.Equal(SceneApplyItemOutcome.Rejected, item.Outcome);
        Assert.Equal(FailureCode.RevisionConflict, item.FailureCode);
        Assert.False(fixture.TargetNode.TryGetActivity(IncomingActivity, out _));
    }

    [Fact]
    public async Task SameDeviceDifferentSlotFailsClosedWithoutMutation()
    {
        var fixture = new Fixture();
        fixture.GrantSourceToReceiveAtTarget();
        fixture.GrantTargetToApplyAndAcceptOffer();
        fixture.GrantSourceToItself();
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("40000000-0000-0000-0000-000000000001"),
            "scene-title-canary",
            [
                SceneActivityPlan.Place(
                    IncomingActivity,
                    ActivityPlacement.On(SourceDevice, "destination"),
                    SceneSourceDisposition.PreserveSource,
                    SceneConflictPolicy.RequireEmpty),
            ]);

        SceneApplyExecutionResult execution = await fixture.ApplyAsync(scene);

        SceneApplyItemResult item = Assert.Single(
            Assert.IsType<SceneApplyResult>(execution.Result).Items);
        Assert.NotEqual(SceneApplyItemOutcome.Committed, item.Outcome);
        Assert.True(fixture.SourceNode.TryGetActivity(
            IncomingActivity,
            out ActivityInstance? preserved));
        Assert.Equal(
            ActivityPlacement.On(SourceDevice, "source"),
            preserved.Placement);
        Assert.Equal(SceneApplyItemOutcome.Rejected, item.Outcome);
        Assert.Equal(FailureCode.ActivityAlreadyExists, item.FailureCode);
    }

    [Fact]
    public async Task ReplaceCommitsThroughTheRealReplaceEndpointWithUndo()
    {
        var fixture = new Fixture(withReplace: true);
        ScenePlan scene = Fixture.CreateScene(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.ReplaceWithUndo);
        fixture.GrantSourceToReceiveAtTarget();
        fixture.GrantTargetToApplyAndAcceptOffer();
        fixture.GrantTargetToAcceptReplace();

        SceneApplyExecutionResult execution = await fixture.ApplyAsync(scene);

        SceneApplyItemResult item = Assert.Single(
            Assert.IsType<SceneApplyResult>(execution.Result).Items);
        Assert.Equal(SceneApplyItemOutcome.Committed, item.Outcome);
        UndoCapsuleReference undo = Assert.IsType<UndoCapsuleReference>(
            item.UndoCapsule);
        Assert.Equal(OccupyingActivity, undo.TargetActivityId);
        Assert.Equal(IncomingActivity, undo.IncomingActivityId);
        Assert.Equal(
            Fixture.AcceptedAt + ReplaceEndpoint.MaximumUndoRetention,
            undo.ExpiresAt);
        Assert.True(fixture.TargetNode.TryGetActivity(
            IncomingActivity,
            out ActivityInstance? replacement));
        Assert.Equal(
            ActivityPlacement.On(TargetDevice, "destination"),
            replacement.Placement);
        Assert.False(fixture.TargetNode.TryGetActivity(OccupyingActivity, out _));
        Assert.True(fixture.SourceNode.TryGetActivity(
            IncomingActivity,
            out ActivityInstance? preserved));
        Assert.Equal(ActivityLifecycle.Active, preserved.Lifecycle);
    }

    [Fact]
    public async Task SourceReceiveDenialRejectsBeforeAnyMutation()
    {
        var fixture = new Fixture();
        ScenePlan scene = Fixture.CreateScene(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        fixture.GrantTargetToApplyAndAcceptOffer();

        SceneApplyExecutionResult execution = await fixture.ApplyAsync(scene);

        SceneApplyItemResult item = Assert.Single(
            Assert.IsType<SceneApplyResult>(execution.Result).Items);
        Assert.Equal(SceneApplyItemOutcome.Rejected, item.Outcome);
        Assert.Equal(FailureCode.CapabilityDenied, item.FailureCode);
        Assert.False(fixture.TargetNode.TryGetActivity(IncomingActivity, out _));
        Assert.True(fixture.SourceNode.TryGetActivity(
            IncomingActivity,
            out ActivityInstance? preserved));
        Assert.Equal(ActivityLifecycle.Active, preserved.Lifecycle);
    }

    [Fact]
    public async Task TargetOfferDenialRejectsAndPreservesTheSource()
    {
        var fixture = new Fixture();
        ScenePlan scene = Fixture.CreateScene(
            SceneSourceDisposition.MoveAfterAcknowledgement,
            SceneConflictPolicy.RequireEmpty);
        fixture.GrantSourceToReceiveAtTarget();
        fixture.GrantTargetToApplyOnly();

        SceneApplyExecutionResult execution = await fixture.ApplyAsync(scene);

        SceneApplyItemResult item = Assert.Single(
            Assert.IsType<SceneApplyResult>(execution.Result).Items);
        Assert.Equal(SceneApplyItemOutcome.Rejected, item.Outcome);
        Assert.Equal(FailureCode.CapabilityDenied, item.FailureCode);
        Assert.False(fixture.TargetNode.TryGetActivity(IncomingActivity, out _));
        Assert.True(fixture.SourceNode.TryGetActivity(
            IncomingActivity,
            out ActivityInstance? preserved));
        Assert.Equal(ActivityLifecycle.Active, preserved.Lifecycle);
    }

    [Fact]
    public async Task ReplaceDenialRejectsAndPreservesBothActivities()
    {
        var fixture = new Fixture(withReplace: true);
        ScenePlan scene = Fixture.CreateScene(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.ReplaceWithUndo);
        fixture.GrantSourceToReceiveAtTarget();
        fixture.GrantTargetToApplyAndAcceptOffer();

        SceneApplyExecutionResult execution = await fixture.ApplyAsync(scene);

        SceneApplyItemResult item = Assert.Single(
            Assert.IsType<SceneApplyResult>(execution.Result).Items);
        Assert.Equal(SceneApplyItemOutcome.Rejected, item.Outcome);
        Assert.Equal(FailureCode.CapabilityDenied, item.FailureCode);
        Assert.Null(item.UndoCapsule);
        Assert.True(fixture.TargetNode.TryGetActivity(
            OccupyingActivity,
            out ActivityInstance? occupant));
        Assert.Equal(ActivityLifecycle.Active, occupant.Lifecycle);
        Assert.False(fixture.TargetNode.TryGetActivity(IncomingActivity, out _));
    }

    [Fact]
    public async Task MissingTargetEndpointFailsWithoutMutation()
    {
        var fixture = new Fixture(withoutTargetOperationEndpoint: true);
        ScenePlan scene = Fixture.CreateScene(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        fixture.GrantSourceToReceiveAtTarget();
        fixture.GrantTargetToApplyAndAcceptOffer();

        SceneApplyExecutionResult execution = await fixture.ApplyAsync(scene);

        SceneApplyItemResult item = Assert.Single(
            Assert.IsType<SceneApplyResult>(execution.Result).Items);
        Assert.Equal(SceneApplyItemOutcome.Failed, item.Outcome);
        Assert.Equal(FailureCode.PeerUnavailable, item.FailureCode);
        Assert.False(fixture.TargetNode.TryGetActivity(IncomingActivity, out _));
    }

    [Fact]
    public async Task RetriedApplyReplaysTerminalResultWithoutDuplicateWork()
    {
        var fixture = new Fixture();
        ScenePlan scene = Fixture.CreateScene(
            SceneSourceDisposition.MoveAfterAcknowledgement,
            SceneConflictPolicy.RequireEmpty);
        fixture.GrantSourceToReceiveAtTarget();
        fixture.GrantTargetToApplyAndAcceptOffer();

        SceneApplyExecutionResult first = await fixture.ApplyAsync(scene);
        SceneApplyExecutionResult second = await fixture.ReapplyAsync(scene);

        SceneApplyItemResult firstItem = Assert.Single(
            Assert.IsType<SceneApplyResult>(first.Result).Items);
        SceneApplyItemResult secondItem = Assert.Single(
            Assert.IsType<SceneApplyResult>(second.Result).Items);
        Assert.Equal(SceneApplyItemOutcome.Committed, firstItem.Outcome);
        Assert.Equal(firstItem, secondItem);
        Assert.True(fixture.SourceNode.TryGetActivity(
            IncomingActivity,
            out ActivityInstance? source));
        Assert.Equal(ActivityLifecycle.Closed, source.Lifecycle);
        Assert.Equal(1, fixture.TargetCatalog.Count);
    }

    private sealed class Fixture
    {
        private readonly FixedClock clock = new(Now);
        private readonly InMemorySceneApplyJournal sceneJournal = new();
        private SceneApplyPreview? lastPreview;
        private readonly DirectSceneActivityOperationPort operationPort;
        private readonly DirectSceneApplyPreflightPort preflightPort;

        public Fixture(
            bool withReplace = false,
            bool withoutTargetOperationEndpoint = false)
        {
            var backingSourceCatalog = new InMemoryActivityCatalog();
            SourceCatalog = new RacingActivityCatalog(backingSourceCatalog);
            TargetCatalog = new InMemoryActivityCatalog();
            var sourceAdapters = new ActivityAdapterRegistry(
                [new WorkspaceNoteAdapter()]);
            var targetAdapters = new ActivityAdapterRegistry(
                [new WorkspaceNoteAdapter()]);
            SourceNode = new FlowspanNode(
                SourceDevice,
                "Source",
                clock,
                SourceCatalog,
                new InMemoryOperationJournal(),
                sourceAdapters,
                NullReceiptSink.Instance);
            TargetNode = new FlowspanNode(
                TargetDevice,
                "Target",
                clock,
                TargetCatalog,
                new InMemoryOperationJournal(),
                targetAdapters,
                NullReceiptSink.Instance);
            Assert.True(SourceNode.AddLocalActivity(
                ActivityInstance.Active(
                    ActivityDescriptor.Create(
                        IncomingActivity,
                        ActivityKind.Parse("workspace.note/v1"),
                        SourceDevice,
                        "incoming-title-canary",
                        JsonSerializer.Serialize(new
                        {
                            text = "incoming-payload-canary",
                        })),
                    ActivityPlacement.On(SourceDevice, "source"),
                    revision: 4)));

            SourcePreflight = new SceneApplyPreflightEndpoint(
                SourceDevice,
                clock,
                SourceCatalog,
                sourceAdapters,
                AlwaysUndoAvailable.Instance);
            TargetPreflight = new SceneApplyPreflightEndpoint(
                TargetDevice,
                clock,
                TargetCatalog,
                targetAdapters,
                AlwaysUndoAvailable.Instance);
            if (withReplace)
            {
                Assert.True(TargetNode.AddLocalActivity(
                    ActivityInstance.Active(
                        ActivityDescriptor.Create(
                            OccupyingActivity,
                            ActivityKind.Parse("workspace.note/v1"),
                            TargetDevice,
                            "occupying-title-canary",
                            JsonSerializer.Serialize(new
                            {
                                text = "occupying-payload-canary",
                            })),
                        ActivityPlacement.On(TargetDevice, "destination"),
                        revision: 9)));
                TargetReplace = new ReplaceEndpoint(
                    TargetDevice,
                    clock,
                    TargetCatalog,
                    new InMemoryOperationJournal(),
                    targetAdapters,
                    new InMemoryReplaceStateStore(),
                    new DeterministicUndoCapsuleIdSource(
                    [
                        UndoCapsuleId.Parse(
                            "70000000-0000-0000-0000-000000000001"),
                    ]),
                    NullReceiptSink.Instance);
            }

            SourceOperation = new SceneActivityOperationEndpoint(
                SourceNode,
                SourcePreflight);
            TargetOperation = new SceneActivityOperationEndpoint(
                TargetNode,
                TargetPreflight,
                TargetReplace);
            preflightPort = new DirectSceneApplyPreflightPort(
                SourceDevice,
                [SourcePreflight, TargetPreflight]);
            operationPort = new DirectSceneActivityOperationPort(
                clock,
                SourceDevice,
                withoutTargetOperationEndpoint
                    ? [SourceOperation]
                    : [SourceOperation, TargetOperation]);
        }

        public RacingActivityCatalog SourceCatalog { get; }

        public InMemoryActivityCatalog TargetCatalog { get; }

        public FlowspanNode SourceNode { get; }

        public FlowspanNode TargetNode { get; }

        public SceneApplyPreflightEndpoint SourcePreflight { get; }

        public SceneApplyPreflightEndpoint TargetPreflight { get; }

        public ReplaceEndpoint? TargetReplace { get; }

        public static DateTimeOffset AcceptedAt => Now;

        public SceneActivityOperationEndpoint SourceOperation { get; }

        public SceneActivityOperationEndpoint TargetOperation { get; }

        public static ScenePlan CreateScene(
            SceneSourceDisposition disposition,
            SceneConflictPolicy conflictPolicy) =>
            ScenePlan.Create(
                SceneId.Parse(
                    "40000000-0000-0000-0000-000000000001"),
                "scene-title-canary",
                [
                    SceneActivityPlan.Place(
                        IncomingActivity,
                        ActivityPlacement.On(
                            TargetDevice,
                            "destination"),
                        disposition,
                        conflictPolicy),
                ]);

        public void GrantSourceToReceiveAtTarget() =>
            SourceOperation.SetPeerGrant(
                TargetDevice,
                CapabilityGrant.Of(Capability.ActivityReceive));

        public void GrantTargetToApplyOnly() =>
            TargetOperation.SetPeerGrant(
                SourceDevice,
                CapabilityGrant.Of(Capability.SceneApply));

        public void GrantTargetToAcceptReplace() =>
            TargetOperation.SetPeerGrant(
                SourceDevice,
                CapabilityGrant.Of(
                    Capability.SceneApply,
                    Capability.ActivityOffer,
                    Capability.ActivityReplace));

        public void GrantSourceToItself() =>
            SourceOperation.SetPeerGrant(
                SourceDevice,
                CapabilityGrant.Of(
                    Capability.SceneApply,
                    Capability.ActivityOffer,
                    Capability.ActivityReceive));

        public void GrantTargetToApplyAndAcceptOffer() =>
            TargetOperation.SetPeerGrant(
                SourceDevice,
                CapabilityGrant.Of(
                    Capability.SceneApply,
                    Capability.ActivityOffer));

        public async ValueTask<SceneApplyExecutionResult> ReapplyAsync(
            ScenePlan scene)
        {
            SceneApplyPreview preview = lastPreview
                ?? throw new InvalidOperationException(
                    "A Scene retry requires a previous preview.");
            var coordinator = new SceneApplyCoordinator(
                clock,
                sceneJournal,
                operationPort);
            return await coordinator.ApplyAsync(
                scene,
                preview,
                SceneApplyApproval.Create(
                    preview.Fingerprint,
                    preview.RequiredReplaceConfirmations),
                CancellationToken.None);
        }

        public async ValueTask<SceneApplyExecutionResult> ApplyAsync(
            ScenePlan scene)
        {
            var planner = new SceneApplyPlanner(
                clock,
                preflightPort,
                new DeterministicSceneApplyIdSource(
                    [
                        OperationId.Parse(
                            "50000000-0000-0000-0000-000000000001"),
                        OperationId.Parse(
                            "50000000-0000-0000-0000-000000000002"),
                    ],
                    [
                        CorrelationId.Parse(
                            "60000000-0000-0000-0000-000000000001"),
                        CorrelationId.Parse(
                            "60000000-0000-0000-0000-000000000002"),
                    ]));
            SceneApplyPreview preview = await planner.PreviewAsync(
                scene,
                [],
                observedGroupRevision: null,
                CancellationToken.None);
            SceneApplyApproval approval = SceneApplyApproval.Create(
                preview.Fingerprint,
                preview.RequiredReplaceConfirmations);
            lastPreview = preview;
            var coordinator = new SceneApplyCoordinator(
                clock,
                sceneJournal,
                operationPort);
            return await coordinator.ApplyAsync(
                scene,
                preview,
                approval,
                CancellationToken.None);
        }
    }

    private sealed class RacingActivityCatalog(InMemoryActivityCatalog inner) :
        IActivityCatalog, IActivitySnapshotSource
    {
        private bool hideOnNextRead;
        private int raceSkipCount;
        private Func<ActivityInstance, ActivityInstance>? pendingRace;

        public void RaceOnNextRead(Func<ActivityInstance, ActivityInstance> race) =>
            RaceOnRead(skip: 0, race);

        public void RaceOnRead(
            int skip,
            Func<ActivityInstance, ActivityInstance> race)
        {
            ArgumentNullException.ThrowIfNull(race);
            raceSkipCount = skip;
            pendingRace = race;
        }

        public void HideOnNextRead() => hideOnNextRead = true;

        public IReadOnlyList<ActivityInstance> GetSnapshot() => inner.GetSnapshot();

        public bool TryGet(
            ActivityId activityId,
            [NotNullWhen(true)] out ActivityInstance? activity)
        {
            if (!inner.TryGet(activityId, out activity))
            {
                return false;
            }

            if (hideOnNextRead)
            {
                hideOnNextRead = false;
                activity = null;
                return false;
            }

            Func<ActivityInstance, ActivityInstance>? race = pendingRace;
            if (race is null)
            {
                return true;
            }

            if (raceSkipCount > 0)
            {
                raceSkipCount--;
                return true;
            }

            pendingRace = null;
            ActivityInstance raced = race(activity);
            Assert.True(inner.TryUpdate(activity, raced));
            activity = raced;
            return true;
        }

        public bool TryAdd(ActivityInstance activity) => inner.TryAdd(activity);

        public bool TryUpdate(
            ActivityInstance expected,
            ActivityInstance replacement) =>
            inner.TryUpdate(expected, replacement);

        public bool TrySwapReplace(
            ActivityInstance expected,
            ActivityInstance replacement) =>
            inner.TrySwapReplace(expected, replacement);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class AlwaysUndoAvailable :
        ISceneReplaceUndoAvailability
    {
        private AlwaysUndoAvailable()
        {
        }

        public static AlwaysUndoAvailable Instance { get; } = new();

        public bool HasDurableUndoFor(ActivityInstance target)
        {
            ArgumentNullException.ThrowIfNull(target);
            return true;
        }
    }
}
