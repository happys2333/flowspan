using System.Collections.Immutable;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class SceneCompensationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExplicitCompensationUsesReverseOrderAndStableRetryIdentity()
    {
        CompensationFixture fixture = CreateFixture();
        var port = new ScriptedUndoPort(
            fixture.IndexByCapsule,
            static (context, capsule) => UndoReplaceResult.Committed(
                context,
                capsule.Id,
                Now));
        var compensator = new SceneApplyCompensator(
            new MutableClock(Now),
            port);

        SceneCompensationResult first = await compensator.CompensateAsync(
            fixture.Result,
            CancellationToken.None);
        SceneCompensationResult retry = await compensator.CompensateAsync(
            fixture.Result,
            CancellationToken.None);

        Assert.Equal(SceneCompensationStatus.Completed, first.Status);
        Assert.Equal([2, 0], first.Items.Select(static item => item.SceneIndex));
        Assert.Equal([2, 0, 2, 0], port.CalledIndices);
        Assert.True(first.Items.SequenceEqual(retry.Items));
        Assert.Equal(
            fixture.Result.Items[2].UndoCapsule!.ExpiresAt,
            port.Contexts[0].Deadline);
        Assert.Equal(port.Contexts[0], port.Contexts[2]);
        Assert.Equal(port.Contexts[1], port.Contexts[3]);
    }

    [Theory]
    [InlineData(OperationStatus.Rejected, FailureCode.RevisionConflict)]
    [InlineData(OperationStatus.Rejected, FailureCode.UndoCapsuleConsumed)]
    [InlineData(OperationStatus.Failed, FailureCode.AdapterUnavailable)]
    [InlineData(OperationStatus.Recovering, FailureCode.OperationInProgress)]
    public async Task OneUnsuccessfulUndoDoesNotHideOrSkipIndependentUndo(
        OperationStatus status,
        FailureCode failureCode)
    {
        CompensationFixture fixture = CreateFixture();
        var port = new ScriptedUndoPort(
            fixture.IndexByCapsule,
            (context, capsule) => fixture.IndexByCapsule[capsule.Id] == 2
                ? UndoReplaceResult.FromRecordedResult(
                    context.OperationId,
                    context.CorrelationId,
                    capsule.Id,
                    status,
                    failureCode,
                    Now)
                : UndoReplaceResult.Committed(context, capsule.Id, Now));
        var compensator = new SceneApplyCompensator(
            new MutableClock(Now),
            port);

        SceneCompensationResult result = await compensator.CompensateAsync(
            fixture.Result,
            CancellationToken.None);

        Assert.Equal([2, 0], port.CalledIndices);
        Assert.Equal(
            status == OperationStatus.Recovering
                ? SceneCompensationStatus.Recovering
                : SceneCompensationStatus.PartiallyCompleted,
            result.Status);
        Assert.Equal(
            OutcomeFor(status),
            result.Items[0].Outcome);
        Assert.Equal(failureCode, result.Items[0].FailureCode);
        Assert.Equal(
            SceneCompensationItemOutcome.Committed,
            result.Items[1].Outcome);
    }

    [Fact]
    public async Task ExpiredUndoIsReportedWithoutCallingTarget()
    {
        CompensationFixture fixture = CreateFixture(
            firstExpiry: Now.AddMinutes(10),
            secondExpiry: Now.AddMinutes(1));
        var port = new ScriptedUndoPort(
            fixture.IndexByCapsule,
            static (context, capsule) => UndoReplaceResult.Committed(
                context,
                capsule.Id,
                Now.AddMinutes(2)));
        var compensator = new SceneApplyCompensator(
            new MutableClock(Now.AddMinutes(2)),
            port);

        SceneCompensationResult result = await compensator.CompensateAsync(
            fixture.Result,
            CancellationToken.None);

        Assert.Equal([0], port.CalledIndices);
        Assert.Equal(SceneCompensationStatus.PartiallyCompleted, result.Status);
        Assert.Equal(
            SceneCompensationItemOutcome.Rejected,
            result.Items[0].Outcome);
        Assert.Equal(
            FailureCode.UndoCapsuleExpired,
            result.Items[0].FailureCode);
        Assert.Equal(
            SceneCompensationItemOutcome.Committed,
            result.Items[1].Outcome);
    }

    [Fact]
    public async Task CancellationDuringUndoIsRecoveringAndCancelsRemainder()
    {
        CompensationFixture fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        var port = new CancellingUndoPort(
            fixture.IndexByCapsule,
            cancellation);
        var compensator = new SceneApplyCompensator(
            new MutableClock(Now),
            port);

        SceneCompensationResult result = await compensator.CompensateAsync(
            fixture.Result,
            cancellation.Token);

        Assert.Equal([2], port.CalledIndices);
        Assert.Equal(SceneCompensationStatus.Recovering, result.Status);
        Assert.Equal(
            SceneCompensationItemOutcome.Recovering,
            result.Items[0].Outcome);
        Assert.Equal(
            FailureCode.AcknowledgementLost,
            result.Items[0].FailureCode);
        Assert.Equal(
            SceneCompensationItemOutcome.Cancelled,
            result.Items[1].Outcome);
    }

    [Fact]
    public async Task SceneWithoutCommittedReplaceHasNothingToUndo()
    {
        CompensationFixture fixture = CreateFixture();
        SceneApplyItemResult handoff = fixture.Result.Items[1];
        SceneApplyItemResult firstFailed = FailedReplace(
            fixture.Preview.Items[0]);
        SceneApplyItemResult secondFailed = FailedReplace(
            fixture.Preview.Items[2]);
        SceneApplyResult resultWithoutReplace = SceneApplyResult.Create(
            fixture.Preview,
            fixture.Result.AcceptedAt,
            fixture.Result.UpdatedAt,
            [
                firstFailed,
                handoff,
                secondFailed,
            ]);
        var port = new ScriptedUndoPort(
            fixture.IndexByCapsule,
            static (_, _) => throw new InvalidOperationException());
        var compensator = new SceneApplyCompensator(
            new MutableClock(Now),
            port);

        SceneCompensationResult compensation = await compensator.CompensateAsync(
            resultWithoutReplace,
            CancellationToken.None);

        Assert.Equal(SceneCompensationStatus.NothingToUndo, compensation.Status);
        Assert.Empty(compensation.Items);
        Assert.Empty(port.CalledIndices);
    }

    private static SceneApplyItemResult FailedReplace(
        SceneApplyItemPreview item)
    {
        SceneSourceSelection source = item.Source!;
        OperationReceipt receipt = OperationReceipt.FromRecordedResult(
            item.ChildOperationId,
            item.ChildCorrelationId,
            OperationKind.Replace,
            OperationStatus.Failed,
            source.DeviceId,
            item.Destination.DeviceId,
            item.ActivityId,
            source.Kind,
            source.DescriptorDigest,
            Now,
            FailureCode.AdapterUnavailable);
        return SceneApplyItemResult.FromOperation(
            item,
            receipt,
            undoCapsule: null);
    }

    private static CompensationFixture CreateFixture(
        DateTimeOffset? firstExpiry = null,
        DateTimeOffset? secondExpiry = null)
    {
        DeviceId sourceDevice = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId targetDevice = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityKind kind = ActivityKind.Parse("workspace.note/v1");
        var plans = new List<SceneActivityPlan>();
        var previewItems = new List<SceneApplyItemPreview>();
        var results = new List<SceneApplyItemResult>();
        var indexByCapsule = ImmutableDictionary.CreateBuilder<
            UndoCapsuleId,
            int>();
        for (int index = 0; index < 3; index++)
        {
            ActivityId activityId = ActivityId.From(Guid.Parse(
                $"aaaaaaaa-aaaa-aaaa-aaaa-{index + 1:D12}"));
            SceneActivityPlan plan = SceneActivityPlan.Place(
                activityId,
                ActivityPlacement.On(targetDevice, $"target-{index}"),
                SceneSourceDisposition.PreserveSource,
                index == 1
                    ? SceneConflictPolicy.RequireEmpty
                    : SceneConflictPolicy.ReplaceWithUndo);
            plans.Add(plan);
            SceneSourceSelection source = SceneSourceSelection.Create(
                index,
                activityId,
                revision: 3,
                new string((char)('A' + index), 64),
                kind,
                ActivityPlacement.On(sourceDevice, $"source-{index}"));
            OperationId operationId = OperationId.From(Guid.Parse(
                $"bbbbbbbb-bbbb-bbbb-bbbb-{index + 1:D12}"));
            CorrelationId correlationId = CorrelationId.From(Guid.Parse(
                $"cccccccc-cccc-cccc-cccc-{index + 1:D12}"));
            if (index == 1)
            {
                previewItems.Add(SceneApplyItemPreview.TransferToEmpty(
                    plan,
                    source,
                    operationId,
                    correlationId));
                continue;
            }

            SceneReplaceTargetSnapshot target =
                SceneReplaceTargetSnapshot.Create(
                    ActivityId.From(Guid.Parse(
                        $"dddddddd-dddd-dddd-dddd-{index + 1:D12}")),
                    revision: 7,
                    new string((char)('D' + index), 64),
                    kind,
                    plan.Placement);
            previewItems.Add(SceneApplyItemPreview.Replace(
                plan,
                source,
                target,
                operationId,
                correlationId));
        }

        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            "compensation-scene-canary",
            plans);
        SceneApplyPreview preview = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
            Now.AddMinutes(-1),
            Now.AddMinutes(4),
            previewItems);
        for (int index = 0; index < preview.Items.Length; index++)
        {
            SceneApplyItemPreview item = preview.Items[index];
            SceneSourceSelection source = item.Source!;
            OperationReceipt receipt = OperationReceipt.FromRecordedResult(
                item.ChildOperationId,
                item.ChildCorrelationId,
                item.Action == SceneApplyAction.Replace
                    ? OperationKind.Replace
                    : OperationKind.Handoff,
                OperationStatus.Committed,
                source.DeviceId,
                item.Destination.DeviceId,
                item.ActivityId,
                source.Kind,
                source.DescriptorDigest,
                Now,
                FailureCode.None);
            if (item.Action == SceneApplyAction.Handoff)
            {
                results.Add(SceneApplyItemResult.FromOperation(
                    item,
                    receipt,
                    undoCapsule: null));
                continue;
            }

            SceneReplaceTargetSnapshot target = item.ReplaceTarget!;
            UndoCapsuleId capsuleId = UndoCapsuleId.From(Guid.Parse(
                $"12345678-1234-1234-1234-{index + 1:D12}"));
            DateTimeOffset expiresAt = index == 0
                ? firstExpiry ?? Now.AddMinutes(10)
                : secondExpiry ?? Now.AddMinutes(10);
            var capsule = new UndoCapsuleReference(
                capsuleId,
                item.ChildOperationId,
                item.ChildCorrelationId,
                item.Destination.DeviceId,
                target.ActivityId,
                target.Revision,
                target.DescriptorDigest,
                item.ActivityId,
                source.DescriptorDigest,
                expiresAt);
            indexByCapsule.Add(capsuleId, index);
            results.Add(SceneApplyItemResult.FromOperation(
                item,
                receipt,
                capsule));
        }

        SceneApplyResult result = SceneApplyResult.Create(
            preview,
            Now,
            Now,
            results);
        return new CompensationFixture(
            preview,
            result,
            indexByCapsule.ToImmutable());
    }

    private static SceneCompensationItemOutcome OutcomeFor(
        OperationStatus status) => status switch
        {
            OperationStatus.Rejected =>
                SceneCompensationItemOutcome.Rejected,
            OperationStatus.Failed => SceneCompensationItemOutcome.Failed,
            OperationStatus.Recovering =>
                SceneCompensationItemOutcome.Recovering,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    private sealed record CompensationFixture(
        SceneApplyPreview Preview,
        SceneApplyResult Result,
        ImmutableDictionary<UndoCapsuleId, int> IndexByCapsule);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class ScriptedUndoPort(
        ImmutableDictionary<UndoCapsuleId, int> indexByCapsule,
        Func<OperationContext, UndoCapsuleReference, UndoReplaceResult> undo) :
        ISceneActivityOperationPort
    {
        public List<int> CalledIndices { get; } = [];

        public List<OperationContext> Contexts { get; } = [];

        public ValueTask<SceneActivityOperationResult> ExecuteAsync(
            SceneActivityPreparation preparation,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<SceneActivityOperationResult>(
                new InvalidOperationException(
                    "Compensation must not execute a forward Scene operation."));

        public ValueTask<UndoReplaceResult> UndoReplaceAsync(
            UndoCapsuleReference capsule,
            OperationContext context,
            CancellationToken cancellationToken)
        {
            CalledIndices.Add(indexByCapsule[capsule.Id]);
            Contexts.Add(context);
            return ValueTask.FromResult(undo(context, capsule));
        }
    }

    private sealed class CancellingUndoPort(
        ImmutableDictionary<UndoCapsuleId, int> indexByCapsule,
        CancellationTokenSource cancellation) : ISceneActivityOperationPort
    {
        public List<int> CalledIndices { get; } = [];

        public ValueTask<SceneActivityOperationResult> ExecuteAsync(
            SceneActivityPreparation preparation,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<SceneActivityOperationResult>(
                new InvalidOperationException());

        public ValueTask<UndoReplaceResult> UndoReplaceAsync(
            UndoCapsuleReference capsule,
            OperationContext context,
            CancellationToken cancellationToken)
        {
            CalledIndices.Add(indexByCapsule[capsule.Id]);
            cancellation.Cancel();
            return ValueTask.FromCanceled<UndoReplaceResult>(
                cancellation.Token);
        }
    }
}
