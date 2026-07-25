using System.Collections.Immutable;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class SceneApplyResultTests
{
    private static readonly DateTimeOffset AcceptedAt =
        new(2026, 7, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AllSatisfiedItemsReduceToCompletedOrCompletedWithWarnings()
    {
        ResultFixture exact = CreateFixture(
            FixtureItem.NoChange,
            FixtureItem.Handoff);
        SceneApplyItemResult noChange = SceneApplyItemResult.FromPreviewOnly(
            exact.Preview.Items[0],
            AcceptedAt);
        SceneApplyItemResult committed = SceneApplyItemResult.FromOperation(
            exact.Preview.Items[1],
            Receipt(
                exact,
                1,
                OperationStatus.Committed,
                AcceptedAt.AddSeconds(1)),
            undoCapsule: null);

        SceneApplyResult completed = SceneApplyResult.Create(
            exact.Preview,
            AcceptedAt,
            AcceptedAt.AddSeconds(1),
            [noChange, committed]);

        ResultFixture warningFixture = CreateFixture(FixtureItem.Handoff);
        SceneApplyItemResult warning = SceneApplyItemResult.FromOperation(
            warningFixture.Preview.Items[0],
            Receipt(
                warningFixture,
                0,
                OperationStatus.CommittedWithWarning,
                AcceptedAt.AddSeconds(1)),
            undoCapsule: null);
        SceneApplyResult completedWithWarnings = SceneApplyResult.Create(
            warningFixture.Preview,
            AcceptedAt,
            AcceptedAt.AddSeconds(1),
            [warning]);

        Assert.Equal(SceneApplyOverallStatus.Completed, completed.Status);
        Assert.Equal(
            SceneApplyOverallStatus.CompletedWithWarnings,
            completedWithWarnings.Status);
    }

    [Fact]
    public void MixedSatisfiedAndTerminalBlockerIsPartiallyCompleted()
    {
        ResultFixture fixture = CreateFixture(
            FixtureItem.NoChange,
            FixtureItem.Blocked);
        SceneApplyItemResult noChange = SceneApplyItemResult.FromPreviewOnly(
            fixture.Preview.Items[0],
            AcceptedAt);
        SceneApplyItemResult blocked = SceneApplyItemResult.FromPreviewOnly(
            fixture.Preview.Items[1],
            AcceptedAt.AddTicks(1));

        SceneApplyResult result = SceneApplyResult.Create(
            fixture.Preview,
            AcceptedAt,
            AcceptedAt.AddTicks(1),
            [noChange, blocked]);

        Assert.Equal(SceneApplyOverallStatus.PartiallyCompleted, result.Status);
        Assert.Collection(
            result.Items,
            item => Assert.Equal(SceneApplyItemOutcome.NoChange, item.Outcome),
            item => Assert.Equal(SceneApplyItemOutcome.Blocked, item.Outcome));
    }

    [Fact]
    public void OnlyTerminalUnsatisfiedItemsReduceToBlocked()
    {
        ResultFixture fixture = CreateFixture(FixtureItem.Blocked);
        SceneApplyItemResult blocked = SceneApplyItemResult.FromPreviewOnly(
            fixture.Preview.Items[0],
            AcceptedAt);

        SceneApplyResult result = SceneApplyResult.Create(
            fixture.Preview,
            AcceptedAt,
            AcceptedAt,
            [blocked]);

        Assert.Equal(SceneApplyOverallStatus.Blocked, result.Status);
    }

    [Fact]
    public void RecoveringDominatesAndForbidsLaterExecutionEvidence()
    {
        ResultFixture fixture = CreateFixture(
            FixtureItem.Handoff,
            FixtureItem.Handoff);
        SceneApplyItemResult recovering = SceneApplyItemResult.FromOperation(
            fixture.Preview.Items[0],
            Receipt(
                fixture,
                0,
                OperationStatus.Recovering,
                AcceptedAt.AddSeconds(1)),
            undoCapsule: null);
        SceneApplyItemResult remainder = SceneApplyItemResult.NotAttempted(
            fixture.Preview.Items[1],
            SceneApplyItemReason.NotAttemptedAfterRecovering,
            AcceptedAt.AddSeconds(1));
        SceneApplyItemResult impossibleCommit = SceneApplyItemResult.FromOperation(
            fixture.Preview.Items[1],
            Receipt(
                fixture,
                1,
                OperationStatus.Committed,
                AcceptedAt.AddSeconds(2)),
            undoCapsule: null);

        SceneApplyResult result = SceneApplyResult.Create(
            fixture.Preview,
            AcceptedAt,
            AcceptedAt.AddSeconds(1),
            [recovering, remainder]);

        Assert.Equal(SceneApplyOverallStatus.Recovering, result.Status);
        Assert.Throws<ArgumentException>(() => SceneApplyResult.Create(
            fixture.Preview,
            AcceptedAt,
            AcceptedAt.AddSeconds(2),
            [recovering, impossibleCommit]));
    }

    [Fact]
    public void CancellationBeforeAnySatisfiedItemIsCancelledAndAfterOneIsPartial()
    {
        ResultFixture before = CreateFixture(
            FixtureItem.Handoff,
            FixtureItem.Handoff);
        SceneApplyItemResult beforeFirst = SceneApplyItemResult.NotAttempted(
            before.Preview.Items[0],
            SceneApplyItemReason.Cancelled,
            AcceptedAt);
        SceneApplyItemResult beforeSecond = SceneApplyItemResult.NotAttempted(
            before.Preview.Items[1],
            SceneApplyItemReason.Cancelled,
            AcceptedAt);
        SceneApplyResult cancelled = SceneApplyResult.Create(
            before.Preview,
            AcceptedAt,
            AcceptedAt,
            [beforeFirst, beforeSecond]);

        ResultFixture after = CreateFixture(
            FixtureItem.Handoff,
            FixtureItem.Handoff,
            FixtureItem.Handoff);
        SceneApplyItemResult committed = SceneApplyItemResult.FromOperation(
            after.Preview.Items[0],
            Receipt(
                after,
                0,
                OperationStatus.Committed,
                AcceptedAt.AddSeconds(1)),
            undoCapsule: null);
        SceneApplyItemResult afterSecond = SceneApplyItemResult.NotAttempted(
            after.Preview.Items[1],
            SceneApplyItemReason.Cancelled,
            AcceptedAt.AddSeconds(1));
        SceneApplyItemResult afterThird = SceneApplyItemResult.NotAttempted(
            after.Preview.Items[2],
            SceneApplyItemReason.Cancelled,
            AcceptedAt.AddSeconds(1));
        SceneApplyResult partial = SceneApplyResult.Create(
            after.Preview,
            AcceptedAt,
            AcceptedAt.AddSeconds(1),
            [committed, afterSecond, afterThird]);

        Assert.Equal(SceneApplyOverallStatus.Cancelled, cancelled.Status);
        Assert.Equal(SceneApplyOverallStatus.PartiallyCompleted, partial.Status);
    }

    [Fact]
    public void ReplaceCommitRequiresItsExactPayloadFreeUndoReference()
    {
        ResultFixture fixture = CreateFixture(FixtureItem.Replace);
        SceneApplyItemPreview item = fixture.Preview.Items[0];
        SceneReplaceTargetSnapshot target = Assert.IsType<SceneReplaceTargetSnapshot>(
            item.ReplaceTarget);
        SceneSourceSelection source = Assert.IsType<SceneSourceSelection>(
            item.Source);
        OperationReceipt receipt = Receipt(
            fixture,
            0,
            OperationStatus.Committed,
            AcceptedAt.AddSeconds(1));
        UndoCapsuleReference exact = new(
            UndoCapsuleId.Parse("88888888-8888-8888-8888-888888888888"),
            item.ChildOperationId,
            item.ChildCorrelationId,
            target.ActivityId,
            target.Revision,
            target.DescriptorDigest,
            item.ActivityId,
            source.DescriptorDigest,
            AcceptedAt.AddMinutes(10));

        SceneApplyItemResult committed = SceneApplyItemResult.FromOperation(
            item,
            receipt,
            exact);

        Assert.Equal(SceneApplyItemOutcome.Committed, committed.Outcome);
        Assert.Equal(exact, committed.UndoCapsule);
        Assert.Throws<ArgumentNullException>(() =>
            SceneApplyItemResult.FromOperation(
            item,
            receipt,
            undoCapsule: null));
        Assert.Throws<ArgumentException>(() => SceneApplyItemResult.FromOperation(
            item,
            receipt,
            exact with { IncomingDescriptorDigest = new string('F', 64) }));

        ResultFixture handoffFixture = CreateFixture(FixtureItem.Handoff);
        Assert.Throws<ArgumentException>(() => SceneApplyItemResult.FromOperation(
            handoffFixture.Preview.Items[0],
            Receipt(
                handoffFixture,
                0,
                OperationStatus.Committed,
                AcceptedAt.AddSeconds(1)),
            exact));
    }

    [Fact]
    public void ResultDefensivelyCopiesAt64AndRejectsExtraOrReorderedItems()
    {
        FixtureItem[] kinds = Enumerable
            .Repeat(FixtureItem.NoChange, ScenePlan.MaximumActivities)
            .ToArray();
        ResultFixture fixture = CreateFixture(kinds);
        var supplied = fixture.Preview.Items
            .Select(item => SceneApplyItemResult.FromPreviewOnly(item, AcceptedAt))
            .ToList();

        SceneApplyResult result = SceneApplyResult.Create(
            fixture.Preview,
            AcceptedAt,
            AcceptedAt,
            supplied);
        supplied.Clear();

        Assert.Equal(ScenePlan.MaximumActivities, result.Items.Length);
        var extra = result.Items.ToList();
        extra.Add(result.Items[0]);
        Assert.Throws<ArgumentException>(() => SceneApplyResult.Create(
            fixture.Preview,
            AcceptedAt,
            AcceptedAt,
            extra));
        Assert.Throws<ArgumentException>(() => SceneApplyResult.Create(
            fixture.Preview,
            AcceptedAt,
            AcceptedAt,
            result.Items.Reverse()));
    }

    [Fact]
    public void ResultRenderingOmitsSceneActivityAndPayloadCanaries()
    {
        ResultFixture fixture = CreateFixture(FixtureItem.NoChange);
        SceneApplyItemResult item = SceneApplyItemResult.FromPreviewOnly(
            fixture.Preview.Items[0],
            AcceptedAt);
        SceneApplyResult result = SceneApplyResult.Create(
            fixture.Preview,
            AcceptedAt,
            AcceptedAt,
            [item]);
        ActivityDescriptor descriptor = fixture.Descriptors[0];

        string rendering = result + "\n" + item;
        Assert.DoesNotContain("result-canary-scene", rendering);
        Assert.DoesNotContain(descriptor.Title, rendering);
        Assert.DoesNotContain(descriptor.PayloadJson, rendering);
        Assert.DoesNotContain(descriptor.DescriptorDigest, rendering);
        Assert.DoesNotContain(fixture.Preview.Items[0].Destination.Slot, rendering);
        Assert.DoesNotContain(descriptor.Id.ToString(), rendering);
    }

    [Theory]
    [InlineData(
        OperationStatus.Rejected,
        SceneApplyItemOutcome.Rejected,
        FailureCode.CapabilityDenied)]
    [InlineData(
        OperationStatus.Failed,
        SceneApplyItemOutcome.Failed,
        FailureCode.AdapterUnavailable)]
    public void ProvenTerminalOperationFailuresRemainPayloadFreeAndBlocked(
        OperationStatus operationStatus,
        SceneApplyItemOutcome expectedOutcome,
        FailureCode expectedFailure)
    {
        ResultFixture fixture = CreateFixture(FixtureItem.Handoff);
        SceneApplyItemResult item = SceneApplyItemResult.FromOperation(
            fixture.Preview.Items[0],
            Receipt(fixture, 0, operationStatus, AcceptedAt.AddSeconds(1)),
            undoCapsule: null);

        SceneApplyResult result = SceneApplyResult.Create(
            fixture.Preview,
            AcceptedAt,
            AcceptedAt.AddSeconds(1),
            [item]);

        Assert.Equal(expectedOutcome, item.Outcome);
        Assert.Equal(expectedFailure, item.FailureCode);
        Assert.Equal(SceneApplyOverallStatus.Blocked, result.Status);
        Assert.Null(item.UndoCapsule);
    }

    private static ResultFixture CreateFixture(params FixtureItem[] itemKinds)
    {
        if (itemKinds.Length is < 1 or > ScenePlan.MaximumActivities)
        {
            throw new ArgumentOutOfRangeException(nameof(itemKinds));
        }

        DeviceId sourceDevice = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId targetDevice = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityKind activityKind = ActivityKind.Parse("workspace.note/v1");
        var plans = new List<SceneActivityPlan>();
        var items = new List<SceneApplyItemPreview>();
        var descriptors = ImmutableArray.CreateBuilder<ActivityDescriptor>();
        for (int index = 0; index < itemKinds.Length; index++)
        {
            ActivityId activityId = ActivityId.From(Guid.Parse(
                $"00000000-0000-0000-0000-{index + 1:000000000000}"));
            ActivityDescriptor descriptor = ActivityDescriptor.Create(
                activityId,
                activityKind,
                sourceDevice,
                $"result-title-canary-{index}",
                $"{{\"payload-canary\":{index}}}");
            descriptors.Add(descriptor);
            SceneConflictPolicy conflictPolicy = itemKinds[index]
                == FixtureItem.Replace
                    ? SceneConflictPolicy.ReplaceWithUndo
                    : SceneConflictPolicy.RequireEmpty;
            ActivityPlacement destination = ActivityPlacement.On(
                targetDevice,
                $"result-destination-canary-{index}");
            SceneActivityPlan plan = SceneActivityPlan.Place(
                activityId,
                destination,
                SceneSourceDisposition.PreserveSource,
                conflictPolicy);
            plans.Add(plan);
            OperationId operationId = OperationId.From(Guid.Parse(
                $"10000000-0000-0000-0000-{index + 1:000000000000}"));
            CorrelationId correlationId = CorrelationId.From(Guid.Parse(
                $"20000000-0000-0000-0000-{index + 1:000000000000}"));
            if (itemKinds[index] == FixtureItem.Blocked)
            {
                SceneSourceLookup lookup = SceneSourceLookup.FromObservation(
                    index,
                    activityId,
                    [],
                    isComplete: true);
                items.Add(SceneApplyItemResolver.Resolve(
                    plan,
                    lookup,
                    explicitSelection: null,
                    occupancy: null,
                    operationId,
                    correlationId));
                continue;
            }

            ActivityPlacement sourcePlacement = itemKinds[index]
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
            SceneSourceLookup sourceLookup = SceneSourceLookup.FromObservation(
                index,
                activityId,
                [source],
                isComplete: true);
            SceneSlotOccupancy? occupancy = itemKinds[index] switch
            {
                FixtureItem.NoChange => null,
                FixtureItem.Handoff => SceneSlotOccupancy.Empty,
                FixtureItem.Replace => SceneSlotOccupancy.EligibleConflict(
                    CreateTarget(index, targetDevice, destination, activityKind)),
                _ => throw new ArgumentOutOfRangeException(nameof(itemKinds)),
            };
            items.Add(SceneApplyItemResolver.Resolve(
                plan,
                sourceLookup,
                explicitSelection: null,
                occupancy,
                operationId,
                correlationId));
        }

        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "result-canary-scene",
            plans);
        SceneApplyPreview preview = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"),
            AcceptedAt.AddMinutes(-1),
            AcceptedAt.AddMinutes(4),
            items);
        return new ResultFixture(preview, descriptors.ToImmutable());
    }

    private static SceneReplaceTargetSnapshot CreateTarget(
        int index,
        DeviceId targetDevice,
        ActivityPlacement destination,
        ActivityKind kind)
    {
        ActivityId targetId = ActivityId.From(Guid.Parse(
            $"90000000-0000-0000-0000-{index + 1:000000000000}"));
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            targetId,
            kind,
            targetDevice,
            $"target-title-canary-{index}",
            $"{{\"target-payload-canary\":{index}}}");
        return SceneReplaceTargetSnapshot.Create(
            targetId,
            2,
            descriptor.DescriptorDigest,
            kind,
            destination);
    }

    private static OperationReceipt Receipt(
        ResultFixture fixture,
        int index,
        OperationStatus status,
        DateTimeOffset occurredAt)
    {
        SceneApplyItemPreview item = fixture.Preview.Items[index];
        SceneSourceSelection source = Assert.IsType<SceneSourceSelection>(
            item.Source);
        OperationKind kind = item.Action switch
        {
            SceneApplyAction.Handoff => OperationKind.Handoff,
            SceneApplyAction.Move => OperationKind.Move,
            SceneApplyAction.Replace => OperationKind.Replace,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
        ActivityDescriptor descriptor = fixture.Descriptors[index];
        return status switch
        {
            OperationStatus.Committed => OperationReceipt.Committed(
                item.ChildOperationId,
                item.ChildCorrelationId,
                kind,
                source.DeviceId,
                item.Destination.DeviceId,
                descriptor,
                occurredAt),
            OperationStatus.CommittedWithWarning =>
                OperationReceipt.CommittedWithWarning(
                    item.ChildOperationId,
                    item.ChildCorrelationId,
                    kind,
                    source.DeviceId,
                    item.Destination.DeviceId,
                    descriptor,
                    occurredAt,
                    FailureCode.SourceCleanupFailed),
            OperationStatus.Recovering => OperationReceipt.Recovering(
                item.ChildOperationId,
                item.ChildCorrelationId,
                kind,
                source.DeviceId,
                item.Destination.DeviceId,
                descriptor,
                occurredAt,
                FailureCode.AcknowledgementLost),
            OperationStatus.Rejected => OperationReceipt.Rejected(
                item.ChildOperationId,
                item.ChildCorrelationId,
                kind,
                source.DeviceId,
                item.Destination.DeviceId,
                descriptor,
                occurredAt,
                FailureCode.CapabilityDenied),
            OperationStatus.Failed => OperationReceipt.Failed(
                item.ChildOperationId,
                item.ChildCorrelationId,
                kind,
                source.DeviceId,
                item.Destination.DeviceId,
                descriptor,
                occurredAt,
                FailureCode.AdapterUnavailable),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }

    private enum FixtureItem
    {
        NoChange,
        Handoff,
        Replace,
        Blocked,
    }

    private sealed record ResultFixture(
        SceneApplyPreview Preview,
        ImmutableArray<ActivityDescriptor> Descriptors);
}
