using System.Collections.Immutable;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Desktop.Tests;

public sealed class SceneApplyViewModelTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 14, 0, 0, TimeSpan.Zero);
    private static readonly DeviceId SourceDevice =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DeviceId AlternateSourceDevice =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DeviceId TargetDevice =
        DeviceId.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly ActivityKind Kind =
        ActivityKind.Parse("workspace.note/v1");

    [Fact]
    public async Task PreviewShowsSavedOrderExactReplaceAndWithheldOpaqueOccupant()
    {
        Fixture fixture = CreateFixture();
        var service = new RecordingSceneService(fixture);
        using var viewModel = new SceneApplyViewModel(
            service,
            new FixedTimeProvider(Now));
        viewModel.SelectScene(fixture.Scene, currentGroupRevision: 2);

        await viewModel.PreviewAsync();

        Assert.Equal("PREVIEW READY — BLOCKERS PRESENT", viewModel.PreviewStatus);
        Assert.Contains("saved revision 1", viewModel.StaleGroupWarning);
        Assert.Collection(
            viewModel.PreviewItems,
            item =>
            {
                Assert.Equal("ITEM 1", item.ItemLabel);
                Assert.Equal("BLOCKED", item.Action);
                Assert.Contains("metadata withheld", item.ReplaceTargetDescription);
                Assert.DoesNotContain(
                    "PROTECTED-TITLE-CANARY",
                    item.ReplaceTargetDescription,
                    StringComparison.Ordinal);
            },
            item =>
            {
                Assert.Equal("ITEM 2", item.ItemLabel);
                Assert.Equal("REPLACE WITH UNDO", item.Action);
                Assert.Equal("SOURCE STAYS OPEN", item.SourceDisposition);
                Assert.Contains(
                    fixture.ReplaceTarget.ActivityId.ToString(),
                    item.ReplaceTargetDescription);
                Assert.Contains(
                    fixture.ReplaceTarget.DescriptorDigest,
                    item.ReplaceTargetDescription);
                Assert.False(item.IsReplaceConfirmed);
            });
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public async Task ExactConfirmationsGateTruthfulPartialResultAndCompensation()
    {
        Fixture fixture = CreateFixture();
        var service = new RecordingSceneService(fixture);
        using var viewModel = new SceneApplyViewModel(
            service,
            new FixedTimeProvider(Now));
        viewModel.SelectScene(fixture.Scene, currentGroupRevision: 2);
        await viewModel.PreviewAsync();

        viewModel.HasAcknowledgedApply = true;
        Assert.False(viewModel.CanApply);
        viewModel.PreviewItems[1].IsReplaceConfirmed = true;
        Assert.True(viewModel.CanApply);

        await viewModel.ApplyAsync();

        Assert.Equal("SCENE PARTIALLY COMPLETED", viewModel.ResultStatus);
        Assert.Equal(fixture.Preview.RequiredReplaceConfirmations, service.LastApproval?.ReplaceConfirmations);
        Assert.Collection(
            viewModel.ResultItems,
            item => Assert.Equal("Blocked", item.Outcome),
            item =>
            {
                Assert.Equal("Committed", item.Outcome);
                Assert.Equal(fixture.Capsule.Id.ToString(), item.UndoCapsule);
            });
        Assert.False(viewModel.CanCompensate);
        viewModel.HasAcknowledgedCompensation = true;
        Assert.True(viewModel.CanCompensate);

        await viewModel.CompensateAsync();

        Assert.Equal("COMPENSATION COMPLETED", viewModel.CompensationStatus);
        DesktopSceneCompensationItem item = Assert.Single(
            viewModel.CompensationItems);
        Assert.Equal("ITEM 2", item.ItemLabel);
        Assert.Equal("COMMITTED", item.Outcome);
        Assert.False(viewModel.HasAcknowledgedCompensation);
    }

    [Fact]
    public async Task ExpiryDisablesApplyAndPresentationRedactsExceptions()
    {
        Fixture fixture = CreateFixture();
        var time = new MutableTimeProvider(Now);
        var service = new RecordingSceneService(fixture);
        using var viewModel = new SceneApplyViewModel(service, time);
        viewModel.SelectScene(fixture.Scene, currentGroupRevision: 2);
        await viewModel.PreviewAsync();
        viewModel.HasAcknowledgedApply = true;
        viewModel.PreviewItems[1].IsReplaceConfirmed = true;
        Assert.True(viewModel.CanApply);

        time.UtcNow = fixture.Preview.ExpiresAt;
        viewModel.RefreshExpiryState();

        Assert.False(viewModel.CanApply);
        Assert.Equal("PREVIEW EXPIRED", viewModel.PreviewStatus);
        Assert.Contains("EXPIRED", viewModel.PreviewExpiry);

        service.PreviewException = new InvalidOperationException(
            "EXCEPTION-PAYLOAD-TITLE-CANARY");
        time.UtcNow = Now;
        await viewModel.PreviewAsync();

        Assert.Equal("PREVIEW UNAVAILABLE", viewModel.PreviewStatus);
        Assert.DoesNotContain(
            "EXCEPTION-PAYLOAD-TITLE-CANARY",
            viewModel.PreviewDescription,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitSourceSelectionRegeneratesTheCompletePreview()
    {
        ActivityId activityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("abababab-abab-abab-abab-abababababab"),
            "Source selection Scene",
            [
                SceneActivityPlan.Place(
                    activityId,
                    ActivityPlacement.On(TargetDevice, "focus"),
                    SceneSourceDisposition.PreserveSource,
                    SceneConflictPolicy.RequireEmpty),
            ]);
        SceneSourceSelection first = CreateSource(
            index: 0,
            activityId,
            SourceDevice,
            "source-a");
        SceneSourceSelection second = CreateSource(
            index: 0,
            activityId,
            AlternateSourceDevice,
            "source-b");
        SceneApplyPreview selectionRequired = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("10101010-1010-1010-1010-101010101010"),
            CorrelationId.Parse("11111111-1111-1111-1111-111111111111"),
            Now,
            Now.AddMinutes(5),
            [
                SceneApplyItemPreview.BlockedBySourceLookup(
                    scene.Activities[0],
                    SceneSourceLookup.FromObservation(
                        index: 0,
                        activityId,
                        [first, second],
                        isComplete: true),
                    OperationId.Parse("12121212-1212-1212-1212-121212121212"),
                    CorrelationId.Parse("13131313-1313-1313-1313-131313131313")),
            ]);
        SceneApplyPreview resolved = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("20202020-2020-2020-2020-202020202020"),
            CorrelationId.Parse("21212121-2121-2121-2121-212121212121"),
            Now,
            Now.AddMinutes(5),
            [
                SceneApplyItemPreview.TransferToEmpty(
                    scene.Activities[0],
                    second,
                    OperationId.Parse("22222222-2323-2323-2323-232323232323"),
                    CorrelationId.Parse("24242424-2424-2424-2424-242424242424")),
            ]);
        var service = new QueuedPreviewSceneService(
            selectionRequired,
            resolved);
        using var viewModel = new SceneApplyViewModel(
            service,
            new FixedTimeProvider(Now));
        viewModel.SelectScene(scene);

        await viewModel.PreviewAsync();
        DesktopSceneApplyItemViewModel item = Assert.Single(viewModel.PreviewItems);
        Assert.True(item.CanSelectSource);
        item.SelectedSource = item.SourceOptions[1];
        Assert.True(viewModel.CanRepreview);

        await viewModel.RepreviewAsync();

        Assert.Equal(second, Assert.Single(service.LastSelections));
        Assert.Equal("HANDOFF", Assert.Single(viewModel.PreviewItems).Action);
        Assert.DoesNotContain(
            "SourceSelectionRequired",
            viewModel.PreviewDescription,
            StringComparison.Ordinal);
    }

    private static Fixture CreateFixture()
    {
        ActivityId blockedActivity = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        ActivityId incomingActivity = ActivityId.Parse(
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        ActivityGroup group = ActivityGroup.Create(
            GroupId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            "Ordered group",
            [blockedActivity, incomingActivity]);
        SceneActivityPlan blockedPlan = SceneActivityPlan.Place(
            blockedActivity,
            ActivityPlacement.On(TargetDevice, "protected-slot"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.ReplaceWithUndo);
        SceneActivityPlan replacePlan = SceneActivityPlan.Place(
            incomingActivity,
            ActivityPlacement.On(TargetDevice, "focus"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.ReplaceWithUndo);
        ScenePlan scene = ScenePlan.CreateFromGroup(
            SceneId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "Presentation Scene",
            group,
            [blockedPlan, replacePlan]);
        SceneSourceSelection blockedSource = CreateSource(
            0,
            blockedActivity,
            SourceDevice,
            "blocked-source");
        SceneSourceSelection replaceSource = CreateSource(
            1,
            incomingActivity,
            SourceDevice,
            "replace-source");
        SceneReplaceTargetSnapshot replaceTarget =
            SceneReplaceTargetSnapshot.Create(
                ActivityId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                revision: 9,
                descriptorDigest: new string('E', 64),
                Kind,
                replacePlan.Placement);
        SceneApplyItemPreview blocked =
            SceneApplyItemPreview.BlockedByOccupancy(
                blockedPlan,
                blockedSource,
                SceneSlotOccupancy.Opaque,
                OperationId.Parse("10101010-1010-1010-1010-101010101010"),
                CorrelationId.Parse("11111111-1111-1111-1111-111111111111"));
        SceneApplyItemPreview replace = SceneApplyItemPreview.Replace(
            replacePlan,
            replaceSource,
            replaceTarget,
            OperationId.Parse("12121212-1212-1212-1212-121212121212"),
            CorrelationId.Parse("13131313-1313-1313-1313-131313131313"));
        SceneApplyPreview preview = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("14141414-1414-1414-1414-141414141414"),
            CorrelationId.Parse("15151515-1515-1515-1515-151515151515"),
            Now,
            Now.AddMinutes(5),
            [blocked, replace],
            observedGroupRevision: 2);
        var capsule = new UndoCapsuleReference(
            UndoCapsuleId.Parse("16161616-1616-1616-1616-161616161616"),
            replace.ChildOperationId,
            replace.ChildCorrelationId,
            TargetDevice,
            replaceTarget.ActivityId,
            replaceTarget.Revision,
            replaceTarget.DescriptorDigest,
            incomingActivity,
            replaceSource.DescriptorDigest,
            Now.AddMinutes(10));
        SceneApplyItemResult blockedResult =
            SceneApplyItemResult.FromPreviewOnly(blocked, Now.AddSeconds(2));
        SceneApplyItemResult replaceResult = SceneApplyItemResult.FromOperation(
            replace,
            OperationReceipt.FromRecordedResult(
                replace.ChildOperationId,
                replace.ChildCorrelationId,
                OperationKind.Replace,
                OperationStatus.Committed,
                SourceDevice,
                TargetDevice,
                incomingActivity,
                Kind,
                replaceSource.DescriptorDigest,
                Now.AddSeconds(2),
                FailureCode.None),
            capsule);
        SceneApplyResult result = SceneApplyResult.Create(
            preview,
            Now.AddSeconds(1),
            Now.AddSeconds(2),
            [blockedResult, replaceResult]);
        OperationContext undoContext = SceneApplyCompensator.CreateStableContext(
            result,
            replaceResult,
            capsule);
        UndoReplaceResult undo = UndoReplaceResult.Committed(
            undoContext,
            capsule.Id,
            Now.AddSeconds(3));
        SceneCompensationResult compensation = SceneCompensationResult.Create(
            result.ParentOperationId,
            [
                SceneCompensationItemResult.FromUndo(
                    replace.Index,
                    TargetDevice,
                    undo),
            ]);
        return new Fixture(
            scene,
            preview,
            result,
            compensation,
            replaceTarget,
            capsule);
    }

    private static SceneSourceSelection CreateSource(
        int index,
        ActivityId activityId,
        DeviceId deviceId,
        string slot) => SceneSourceSelection.Create(
        index,
        activityId,
        revision: 7,
        descriptorDigest: new string((char)('A' + index), 64),
        Kind,
        ActivityPlacement.On(deviceId, slot));

    private sealed record Fixture(
        ScenePlan Scene,
        SceneApplyPreview Preview,
        SceneApplyResult Result,
        SceneCompensationResult Compensation,
        SceneReplaceTargetSnapshot ReplaceTarget,
        UndoCapsuleReference Capsule);

    private sealed class RecordingSceneService(Fixture fixture) :
        IDesktopSceneApplyService
    {
        public bool IsSceneApplyReady => true;

        public SceneApplyApproval? LastApproval { get; private set; }

        public Exception? PreviewException { get; set; }

        public ValueTask<SceneApplyPreview> PreviewSceneAsync(
            ScenePlan scene,
            IEnumerable<SceneSourceSelection> selectedSources,
            long? observedGroupRevision,
            CancellationToken cancellationToken = default) =>
            PreviewException is null
                ? ValueTask.FromResult(fixture.Preview)
                : ValueTask.FromException<SceneApplyPreview>(PreviewException);

        public ValueTask<SceneApplyExecutionResult> ApplySceneAsync(
            ScenePlan scene,
            SceneApplyPreview preview,
            SceneApplyApproval approval,
            CancellationToken cancellationToken = default)
        {
            LastApproval = approval;
            return ValueTask.FromResult(
                SceneApplyExecutionResult.Accepted(fixture.Result));
        }

        public ValueTask<SceneCompensationResult> CompensateSceneAsync(
            SceneApplyResult applyResult,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(fixture.Compensation);
    }

    private sealed class QueuedPreviewSceneService(
        params SceneApplyPreview[] previews) : IDesktopSceneApplyService
    {
        private readonly Queue<SceneApplyPreview> queue = new(previews);

        public bool IsSceneApplyReady => true;

        public ImmutableArray<SceneSourceSelection> LastSelections { get; private set; }

        public ValueTask<SceneApplyPreview> PreviewSceneAsync(
            ScenePlan scene,
            IEnumerable<SceneSourceSelection> selectedSources,
            long? observedGroupRevision,
            CancellationToken cancellationToken = default)
        {
            LastSelections = selectedSources.ToImmutableArray();
            return ValueTask.FromResult(queue.Dequeue());
        }

        public ValueTask<SceneApplyExecutionResult> ApplySceneAsync(
            ScenePlan scene,
            SceneApplyPreview preview,
            SceneApplyApproval approval,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public ValueTask<SceneCompensationResult> CompensateSceneAsync(
            SceneApplyResult applyResult,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
