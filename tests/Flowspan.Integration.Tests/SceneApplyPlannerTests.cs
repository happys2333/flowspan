using System.Collections.Immutable;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class SceneApplyPlannerTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 26, 4, 0, 0, TimeSpan.Zero);
    private static readonly ActivityKind Kind =
        ActivityKind.Parse("workspace.note/v1");
    private static readonly DeviceId SourceDevice =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DeviceId TargetDevice =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task PreviewInspectsSavedOrderAndSkipsExactDestinationSlot()
    {
        ScenePlan scene = CreateScene(3);
        SceneSourceSelection exactDestination = Source(
            scene,
            1,
            scene.Activities[1].Placement);
        SceneSourceSelection remote = Source(
            scene,
            2,
            ActivityPlacement.On(SourceDevice, "source-2"));
        var preflight = new RecordingPreflight(
            [
                SceneSourceLookup.FromObservation(
                    0,
                    scene.Activities[0].ActivityId,
                    [],
                    isComplete: true),
                SceneSourceLookup.FromObservation(
                    1,
                    scene.Activities[1].ActivityId,
                    [exactDestination],
                    isComplete: true),
                SceneSourceLookup.FromObservation(
                    2,
                    scene.Activities[2].ActivityId,
                    [remote],
                    isComplete: true),
            ],
            [SceneExactSlotInspection.Observed(SceneSlotOccupancy.Empty)]);
        SceneApplyPlanner planner = CreatePlanner(preflight, itemCount: 3);

        SceneApplyPreview preview = await planner.PreviewAsync(
            scene,
            [],
            observedGroupRevision: null,
            CancellationToken.None);

        Assert.Equal(
            ["source:0", "source:1", "source:2", "slot:2"],
            preflight.Calls);
        Assert.Equal(SceneApplyAction.Blocked, preview.Items[0].Action);
        Assert.Equal(SceneApplyItemReason.SourceNotFound, preview.Items[0].Reason);
        Assert.Equal(SceneApplyAction.NoChange, preview.Items[1].Action);
        Assert.Equal(SceneApplyAction.Handoff, preview.Items[2].Action);
        Assert.Equal(CreatedAt, preview.CreatedAt);
        Assert.Equal(
            CreatedAt + SceneApplyPreview.MaximumLifetime,
            preview.ExpiresAt);
        Assert.All(
            preflight.Contexts,
            context => Assert.Equal(preview.ExpiresAt, context.Deadline));
    }

    [Fact]
    public async Task ExplicitSelectionRequiresACompleteRepreview()
    {
        ScenePlan scene = CreateScene(1);
        SceneSourceSelection selected = Source(
            scene,
            0,
            ActivityPlacement.On(SourceDevice, "selected"));
        SceneSourceSelection other = Source(
            scene,
            0,
            ActivityPlacement.On(
                DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
                "other"),
            revision: 2);
        var preflight = new RecordingPreflight(
            [
                Lookup(scene, selected, other),
                Lookup(scene, selected, other),
            ],
            [SceneExactSlotInspection.Observed(SceneSlotOccupancy.Empty)]);
        SceneApplyPlanner planner = CreatePlanner(
            preflight,
            itemCount: 1,
            previewCount: 2);

        SceneApplyPreview unresolved = await planner.PreviewAsync(
            scene,
            [],
            observedGroupRevision: null,
            CancellationToken.None);
        SceneApplyPreview resolved = await planner.PreviewAsync(
            scene,
            [selected],
            observedGroupRevision: null,
            CancellationToken.None);

        Assert.Equal(["source:0", "source:0", "slot:0"], preflight.Calls);
        Assert.Equal(SceneApplyAction.Blocked, unresolved.Items[0].Action);
        Assert.Equal(
            SceneApplyItemReason.SourceSelectionRequired,
            unresolved.Items[0].Reason);
        Assert.Equal(2, unresolved.Items[0].SourceLookup!.Candidates.Length);
        Assert.Equal(SceneApplyAction.Handoff, resolved.Items[0].Action);
        Assert.Equal(selected, resolved.Items[0].Source);
        Assert.NotEqual(
            unresolved.ParentOperationId,
            resolved.ParentOperationId);
        Assert.NotEqual(unresolved.Fingerprint, resolved.Fingerprint);
    }

    [Fact]
    public async Task PreflightFailuresBecomeRedactedPerItemBlockers()
    {
        ScenePlan scene = CreateScene(3);
        SceneSourceSelection remote = Source(
            scene,
            1,
            ActivityPlacement.On(SourceDevice, "source-1"));
        var preflight = new RecordingPreflight(
            [
                new InvalidOperationException(
                    "source-lookup-exception-canary"),
                SceneSourceLookup.FromObservation(
                    1,
                    scene.Activities[1].ActivityId,
                    [remote],
                    isComplete: true),
                SceneSourceLookup.FromObservation(
                    2,
                    scene.Activities[2].ActivityId,
                    [],
                    isComplete: true),
            ],
            [
                new IOException("slot-inspection-exception-canary"),
            ]);
        SceneApplyPlanner planner = CreatePlanner(preflight, itemCount: 3);

        SceneApplyPreview preview = await planner.PreviewAsync(
            scene,
            [],
            observedGroupRevision: null,
            CancellationToken.None);

        Assert.Equal(
            [
                SceneApplyItemReason.SourceLookupUnavailable,
                SceneApplyItemReason.DestinationUnavailable,
                SceneApplyItemReason.SourceNotFound,
            ],
            preview.Items.Select(static item => item.Reason));
        Assert.All(
            preview.Items,
            item => Assert.Equal(SceneApplyAction.Blocked, item.Action));
        Assert.DoesNotContain(
            "canary",
            preview.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ["source:0", "source:1", "slot:1", "source:2"],
            preflight.Calls);
    }

    [Theory]
    [InlineData(SceneApplyItemReason.CapabilityDenied)]
    [InlineData(SceneApplyItemReason.ProtocolUnsupported)]
    [InlineData(SceneApplyItemReason.DestinationUnavailable)]
    public async Task ExactSlotBlockerRemainsDistinctFromOccupancy(
        SceneApplyItemReason reason)
    {
        ScenePlan scene = CreateScene(1);
        SceneSourceSelection remote = Source(
            scene,
            0,
            ActivityPlacement.On(SourceDevice, "source"));
        var preflight = new RecordingPreflight(
            [
                SceneSourceLookup.FromObservation(
                    0,
                    scene.Activities[0].ActivityId,
                    [remote],
                    isComplete: true),
            ],
            [SceneExactSlotInspection.Blocked(reason)]);
        SceneApplyPlanner planner = CreatePlanner(preflight, itemCount: 1);

        SceneApplyPreview preview = await planner.PreviewAsync(
            scene,
            [],
            observedGroupRevision: null,
            CancellationToken.None);

        Assert.Equal(SceneApplyAction.Blocked, preview.Items[0].Action);
        Assert.Equal(reason, preview.Items[0].Reason);
        Assert.Equal(
            SceneSlotOccupancyKind.NotInspected,
            preview.Items[0].Occupancy.Kind);
    }

    [Fact]
    public async Task InvalidSelectionsAndDuplicateIdsFailBeforePreflight()
    {
        ScenePlan scene = CreateScene(1);
        SceneSourceSelection wrongActivity = SceneSourceSelection.Create(
            0,
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            new string('A', 64),
            Kind,
            ActivityPlacement.On(SourceDevice, "source"));
        var preflight = new RecordingPreflight([], []);
        SceneApplyPlanner selectionPlanner = CreatePlanner(
            preflight,
            itemCount: 1);

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await selectionPlanner.PreviewAsync(
                scene,
                [wrongActivity],
                observedGroupRevision: null,
                CancellationToken.None));

        OperationId duplicate = OperationId.Parse(
            "40000000-0000-0000-0000-000000000001");
        var duplicateIds = new DeterministicSceneApplyIdSource(
            [duplicate, duplicate],
            [
                CorrelationId.Parse(
                    "50000000-0000-0000-0000-000000000001"),
                CorrelationId.Parse(
                    "50000000-0000-0000-0000-000000000002"),
            ]);
        var duplicatePlanner = new SceneApplyPlanner(
            new FixedClock(CreatedAt),
            preflight,
            duplicateIds);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await duplicatePlanner.PreviewAsync(
                scene,
                [],
                observedGroupRevision: null,
                CancellationToken.None));
        Assert.Empty(preflight.Calls);
    }

    [Fact]
    public async Task InvalidObservedGroupRevisionFailsBeforePreflight()
    {
        ScenePlan scene = CreateScene(1);
        var preflight = new RecordingPreflight([], []);
        SceneApplyPlanner planner = CreatePlanner(preflight, itemCount: 1);

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await planner.PreviewAsync(
                scene,
                [],
                observedGroupRevision: 2,
                CancellationToken.None));

        Assert.Empty(preflight.Calls);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutInventingABlocker()
    {
        ScenePlan scene = CreateScene(1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var preflight = new RecordingPreflight([], []);
        SceneApplyPlanner planner = CreatePlanner(preflight, itemCount: 1);

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await planner.PreviewAsync(
                scene,
                [],
                observedGroupRevision: null,
                cancellation.Token));

        Assert.Empty(preflight.Calls);
    }

    private static SceneApplyPlanner CreatePlanner(
        ISceneApplyPreflightPort preflight,
        int itemCount,
        int previewCount = 1)
    {
        int identityCount = checked((itemCount + 1) * previewCount);
        return new SceneApplyPlanner(
            new FixedClock(CreatedAt),
            preflight,
            new DeterministicSceneApplyIdSource(
                Enumerable.Range(1, identityCount).Select(index =>
                    OperationId.From(Guid.Parse(
                        $"40000000-0000-0000-0000-{index:000000000000}"))),
                Enumerable.Range(1, identityCount).Select(index =>
                    CorrelationId.From(Guid.Parse(
                        $"50000000-0000-0000-0000-{index:000000000000}")))));
    }

    private static ScenePlan CreateScene(int itemCount)
    {
        var activities = ImmutableArray.CreateBuilder<SceneActivityPlan>(
            itemCount);
        for (int index = 0; index < itemCount; index++)
        {
            activities.Add(SceneActivityPlan.Place(
                ActivityId.From(Guid.Parse(
                    $"30000000-0000-0000-0000-{index + 1:000000000000}")),
                ActivityPlacement.On(TargetDevice, $"destination-{index}"),
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.RequireEmpty));
        }

        return ScenePlan.Create(
            SceneId.Parse("20000000-0000-0000-0000-000000000001"),
            "planner-scene-title-canary",
            activities);
    }

    private static SceneSourceLookup Lookup(
        ScenePlan scene,
        params SceneSourceSelection[] candidates) =>
        SceneSourceLookup.FromObservation(
            0,
            scene.Activities[0].ActivityId,
            candidates,
            isComplete: true);

    private static SceneSourceSelection Source(
        ScenePlan scene,
        int index,
        ActivityPlacement placement,
        long revision = 1) =>
        SceneSourceSelection.Create(
            index,
            scene.Activities[index].ActivityId,
            revision,
            new string((char)('A' + index), 64),
            Kind,
            placement);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class RecordingPreflight(
        IEnumerable<object> sourceResults,
        IEnumerable<object> slotResults) : ISceneApplyPreflightPort
    {
        private readonly Queue<object> slotResults = new(slotResults);
        private readonly Queue<object> sourceResults = new(sourceResults);

        public List<string> Calls { get; } = [];

        public List<OperationContext> Contexts { get; } = [];

        public ValueTask<SceneSourceLookup> LocateSourcesAsync(
            ActivityId activityId,
            int index,
            OperationContext childContext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add($"source:{index}");
            Contexts.Add(childContext);
            object result = sourceResults.Dequeue();
            return result is Exception exception
                ? ValueTask.FromException<SceneSourceLookup>(exception)
                : ValueTask.FromResult((SceneSourceLookup)result);
        }

        public ValueTask<SceneExactSlotInspection> InspectExactSlotAsync(
            SceneActivityPlan item,
            SceneSourceSelection source,
            OperationContext childContext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add($"slot:{source.Index}");
            Contexts.Add(childContext);
            object result = slotResults.Dequeue();
            return result is Exception exception
                ? ValueTask.FromException<SceneExactSlotInspection>(exception)
                : ValueTask.FromResult((SceneExactSlotInspection)result);
        }
    }
}
