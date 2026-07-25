using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class SceneApplyPreflightModelTests
{
    [Fact]
    public void CompleteSourceLookupClassifiesZeroOneAndManyWithoutSelectingMany()
    {
        ActivityId activityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SceneSourceSelection first = Source(
            activityId,
            "11111111-1111-1111-1111-111111111111",
            "alpha",
            revision: 1,
            'A');
        SceneSourceSelection second = Source(
            activityId,
            "22222222-2222-2222-2222-222222222222",
            "beta",
            revision: 99,
            'B');
        var supplied = new List<SceneSourceSelection> { second, first };

        SceneSourceLookup none = SceneSourceLookup.FromObservation(
            0,
            activityId,
            [],
            isComplete: true);
        SceneSourceLookup one = SceneSourceLookup.FromObservation(
            0,
            activityId,
            [second],
            isComplete: true);
        SceneSourceLookup many = SceneSourceLookup.FromObservation(
            0,
            activityId,
            supplied,
            isComplete: true);
        supplied.Clear();

        Assert.Equal(SceneSourceLookupStatus.NotFound, none.Status);
        Assert.Equal(SceneApplyItemReason.SourceNotFound, none.Reason);
        Assert.Empty(none.Candidates);
        Assert.Null(none.UniqueSource);

        Assert.Equal(SceneSourceLookupStatus.UniqueSource, one.Status);
        Assert.Equal(SceneApplyItemReason.None, one.Reason);
        Assert.Equal(second, one.UniqueSource);

        Assert.Equal(SceneSourceLookupStatus.SelectionRequired, many.Status);
        Assert.Equal(SceneApplyItemReason.SourceSelectionRequired, many.Reason);
        Assert.Null(many.UniqueSource);
        Assert.Collection(
            many.Candidates,
            candidate => Assert.Equal(first, candidate),
            candidate => Assert.Equal(second, candidate));
    }

    [Fact]
    public void IncompleteOrOverBoundSourceLookupPublishesNoPartialChoiceSet()
    {
        ActivityId activityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SceneSourceSelection canary = Source(
            activityId,
            "11111111-1111-1111-1111-111111111111",
            "partial-canary-slot",
            revision: 1,
            'A');
        var overBound = new List<SceneSourceSelection>();
        for (int index = 0; index <= ScenePlan.MaximumActivities; index++)
        {
            overBound.Add(Source(
                activityId,
                "11111111-1111-1111-1111-111111111111",
                $"slot-{index:D2}",
                revision: index + 1,
                (char)('A' + (index % 6))));
        }

        SceneSourceLookup incomplete = SceneSourceLookup.FromObservation(
            0,
            activityId,
            [canary],
            isComplete: false);
        SceneSourceLookup tooMany = SceneSourceLookup.FromObservation(
            0,
            activityId,
            overBound,
            isComplete: true);

        Assert.Equal(SceneSourceLookupStatus.Unavailable, incomplete.Status);
        Assert.Equal(SceneApplyItemReason.SourceLookupUnavailable, incomplete.Reason);
        Assert.Empty(incomplete.Candidates);
        Assert.Equal(SceneSourceLookupStatus.Unavailable, tooMany.Status);
        Assert.Equal(SceneApplyItemReason.SourceLookupUnavailable, tooMany.Reason);
        Assert.Empty(tooMany.Candidates);
        Assert.DoesNotContain("partial-canary-slot", incomplete.ToString());
    }

    [Fact]
    public void CompleteSourceLookupRejectsDuplicateOrMismatchedEvidence()
    {
        ActivityId requested = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        ActivityId another = ActivityId.Parse(
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        SceneSourceSelection exact = Source(
            requested,
            "11111111-1111-1111-1111-111111111111",
            "main",
            revision: 1,
            'A');
        SceneSourceSelection wrongActivity = Source(
            another,
            "22222222-2222-2222-2222-222222222222",
            "side",
            revision: 1,
            'B');

        Assert.Throws<ArgumentException>(() =>
            SceneSourceLookup.FromObservation(
                0,
                requested,
                [exact, exact],
                isComplete: true));
        Assert.Throws<ArgumentException>(() =>
            SceneSourceLookup.FromObservation(
                0,
                requested,
                [wrongActivity],
                isComplete: true));
    }

    [Fact]
    public void SourceBlockerNeedsNoInventedSelectionAndIsBoundIntoPreview()
    {
        SceneActivityPlan plan = Plan(SceneConflictPolicy.RequireEmpty);
        SceneSourceLookup lookup = SceneSourceLookup.FromObservation(
            0,
            plan.ActivityId,
            [],
            isComplete: true);
        SceneApplyItemPreview blocked = SceneApplyItemPreview.BlockedBySourceLookup(
            plan,
            lookup,
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "Focus layout",
            [plan]);
        DateTimeOffset createdAt = new(2026, 7, 25, 8, 0, 0, TimeSpan.Zero);

        SceneApplyPreview preview = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("66666666-6666-6666-6666-666666666666"),
            CorrelationId.Parse("77777777-7777-7777-7777-777777777777"),
            createdAt,
            createdAt.AddMinutes(5),
            [blocked]);

        SceneApplyItemPreview item = Assert.Single(preview.Items);
        Assert.Equal(SceneApplyAction.Blocked, item.Action);
        Assert.Equal(SceneApplyItemReason.SourceNotFound, item.Reason);
        Assert.Null(item.Source);
        Assert.Same(lookup, item.SourceLookup);
        Assert.Equal(SceneSlotOccupancyKind.NotInspected, item.Occupancy.Kind);
        Assert.Null(item.Occupancy.Target);
        Assert.DoesNotContain(plan.Placement.Slot, item.ToString());
        Assert.Matches("^[0-9A-F]{64}$", preview.Fingerprint);
    }

    [Fact]
    public void ExactSlotOccupancyOwnsTargetOnlyForEligibleConflict()
    {
        SceneActivityPlan plan = Plan(SceneConflictPolicy.RequireEmpty);
        SceneSourceSelection source = Source(
            plan.ActivityId,
            "11111111-1111-1111-1111-111111111111",
            "source",
            revision: 1,
            'A');
        SceneReplaceTargetSnapshot target = Target(plan);

        SceneSlotOccupancy eligible = SceneSlotOccupancy.EligibleConflict(target);

        Assert.Equal(SceneSlotOccupancyKind.EligibleConflict, eligible.Kind);
        Assert.Equal(target, eligible.Target);
        Assert.Equal(SceneSlotOccupancyKind.Empty, SceneSlotOccupancy.Empty.Kind);
        Assert.Null(SceneSlotOccupancy.Empty.Target);
        Assert.Equal(SceneSlotOccupancyKind.Opaque, SceneSlotOccupancy.Opaque.Kind);
        Assert.Null(SceneSlotOccupancy.Opaque.Target);
        Assert.Equal(
            SceneSlotOccupancyKind.Ambiguous,
            SceneSlotOccupancy.Ambiguous.Kind);
        Assert.Null(SceneSlotOccupancy.Ambiguous.Target);

        SceneApplyItemPreview occupied = SceneApplyItemPreview.BlockedByOccupancy(
            plan,
            source,
            eligible,
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));
        Assert.Equal(SceneApplyItemReason.DestinationOccupied, occupied.Reason);
        Assert.Equal(target, occupied.ReplaceTarget);
        Assert.DoesNotContain(target.DescriptorDigest, eligible.ToString());
        Assert.DoesNotContain(target.Placement.Slot, eligible.ToString());
    }

    [Theory]
    [InlineData(SceneSlotOccupancyKind.Opaque, SceneApplyItemReason.OpaqueOccupancy)]
    [InlineData(SceneSlotOccupancyKind.Ambiguous, SceneApplyItemReason.AmbiguousOccupancy)]
    public void NonDisclosingOccupancyBlocksWithoutTargetMetadata(
        SceneSlotOccupancyKind kind,
        SceneApplyItemReason expectedReason)
    {
        SceneActivityPlan plan = Plan(SceneConflictPolicy.ReplaceWithUndo);
        SceneSourceSelection source = Source(
            plan.ActivityId,
            "11111111-1111-1111-1111-111111111111",
            "source",
            revision: 1,
            'A');
        SceneSlotOccupancy occupancy = kind switch
        {
            SceneSlotOccupancyKind.Opaque => SceneSlotOccupancy.Opaque,
            SceneSlotOccupancyKind.Ambiguous => SceneSlotOccupancy.Ambiguous,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        SceneApplyItemPreview blocked = SceneApplyItemPreview.BlockedByOccupancy(
            plan,
            source,
            occupancy,
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));

        Assert.Equal(SceneApplyAction.Blocked, blocked.Action);
        Assert.Equal(expectedReason, blocked.Reason);
        Assert.Null(blocked.ReplaceTarget);
        Assert.Null(blocked.Occupancy.Target);
    }

    [Fact]
    public void ResolverNeverChoosesAmongManyAndRequiresAnExactCurrentChoice()
    {
        SceneActivityPlan plan = Plan(SceneConflictPolicy.RequireEmpty);
        SceneSourceSelection chosen = SceneSourceSelection.Create(
            0,
            plan.ActivityId,
            1,
            new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            plan.Placement);
        SceneSourceSelection another = Source(
            plan.ActivityId,
            "11111111-1111-1111-1111-111111111111",
            "source",
            revision: 99,
            'B');
        SceneSourceLookup lookup = SceneSourceLookup.FromObservation(
            0,
            plan.ActivityId,
            [another, chosen],
            isComplete: true);
        OperationId operationId = OperationId.Parse(
            "44444444-4444-4444-4444-444444444444");
        CorrelationId correlationId = CorrelationId.Parse(
            "55555555-5555-5555-5555-555555555555");

        SceneApplyItemPreview unresolved = SceneApplyItemResolver.Resolve(
            plan,
            lookup,
            explicitSelection: null,
            occupancy: null,
            operationId,
            correlationId);
        SceneApplyItemPreview noChange = SceneApplyItemResolver.Resolve(
            plan,
            lookup,
            chosen,
            occupancy: null,
            operationId,
            correlationId);
        SceneApplyItemPreview handoff = SceneApplyItemResolver.Resolve(
            plan,
            lookup,
            another,
            SceneSlotOccupancy.Empty,
            operationId,
            correlationId);
        SceneSourceSelection changedSnapshot = SceneSourceSelection.Create(
            0,
            chosen.ActivityId,
            chosen.Revision + 1,
            new string('C', 64),
            chosen.Kind,
            chosen.Placement);

        Assert.Equal(SceneApplyItemReason.SourceSelectionRequired, unresolved.Reason);
        Assert.Null(unresolved.Source);
        Assert.Equal(SceneApplyAction.NoChange, noChange.Action);
        Assert.Equal(chosen, noChange.Source);
        Assert.Equal(SceneApplyAction.Handoff, handoff.Action);
        Assert.Equal(another, handoff.Source);
        Assert.Throws<ArgumentException>(() => SceneApplyItemResolver.Resolve(
            plan,
            lookup,
            changedSnapshot,
            occupancy: null,
            operationId,
            correlationId));
    }

    [Theory]
    [InlineData(
        SceneSourceDisposition.PreserveSource,
        SceneConflictPolicy.RequireEmpty,
        SceneSlotOccupancyKind.Empty,
        SceneApplyAction.Handoff,
        SceneApplyItemReason.None)]
    [InlineData(
        SceneSourceDisposition.MoveAfterAcknowledgement,
        SceneConflictPolicy.RequireEmpty,
        SceneSlotOccupancyKind.Empty,
        SceneApplyAction.Move,
        SceneApplyItemReason.None)]
    [InlineData(
        SceneSourceDisposition.PreserveSource,
        SceneConflictPolicy.ReplaceWithUndo,
        SceneSlotOccupancyKind.Empty,
        SceneApplyAction.Handoff,
        SceneApplyItemReason.None)]
    [InlineData(
        SceneSourceDisposition.MoveAfterAcknowledgement,
        SceneConflictPolicy.ReplaceWithUndo,
        SceneSlotOccupancyKind.Empty,
        SceneApplyAction.Move,
        SceneApplyItemReason.None)]
    [InlineData(
        SceneSourceDisposition.PreserveSource,
        SceneConflictPolicy.RequireEmpty,
        SceneSlotOccupancyKind.EligibleConflict,
        SceneApplyAction.Blocked,
        SceneApplyItemReason.DestinationOccupied)]
    [InlineData(
        SceneSourceDisposition.MoveAfterAcknowledgement,
        SceneConflictPolicy.RequireEmpty,
        SceneSlotOccupancyKind.EligibleConflict,
        SceneApplyAction.Blocked,
        SceneApplyItemReason.DestinationOccupied)]
    [InlineData(
        SceneSourceDisposition.PreserveSource,
        SceneConflictPolicy.ReplaceWithUndo,
        SceneSlotOccupancyKind.EligibleConflict,
        SceneApplyAction.Replace,
        SceneApplyItemReason.None)]
    [InlineData(
        SceneSourceDisposition.MoveAfterAcknowledgement,
        SceneConflictPolicy.ReplaceWithUndo,
        SceneSlotOccupancyKind.EligibleConflict,
        SceneApplyAction.Blocked,
        SceneApplyItemReason.UnsafeMoveReplace)]
    [InlineData(
        SceneSourceDisposition.PreserveSource,
        SceneConflictPolicy.ReplaceWithUndo,
        SceneSlotOccupancyKind.Opaque,
        SceneApplyAction.Blocked,
        SceneApplyItemReason.OpaqueOccupancy)]
    [InlineData(
        SceneSourceDisposition.MoveAfterAcknowledgement,
        SceneConflictPolicy.RequireEmpty,
        SceneSlotOccupancyKind.Ambiguous,
        SceneApplyAction.Blocked,
        SceneApplyItemReason.AmbiguousOccupancy)]
    public void ResolverImplementsTheClosedPolicyMatrix(
        SceneSourceDisposition disposition,
        SceneConflictPolicy conflictPolicy,
        SceneSlotOccupancyKind occupancyKind,
        SceneApplyAction expectedAction,
        SceneApplyItemReason expectedReason)
    {
        SceneActivityPlan plan = SceneActivityPlan.Place(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityPlacement.On(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "destination"),
            disposition,
            conflictPolicy);
        SceneSourceSelection source = Source(
            plan.ActivityId,
            "11111111-1111-1111-1111-111111111111",
            "source",
            revision: 1,
            'A');
        SceneSourceLookup lookup = SceneSourceLookup.FromObservation(
            0,
            plan.ActivityId,
            [source],
            isComplete: true);
        SceneSlotOccupancy occupancy = occupancyKind switch
        {
            SceneSlotOccupancyKind.Empty => SceneSlotOccupancy.Empty,
            SceneSlotOccupancyKind.EligibleConflict =>
                SceneSlotOccupancy.EligibleConflict(Target(plan)),
            SceneSlotOccupancyKind.Opaque => SceneSlotOccupancy.Opaque,
            SceneSlotOccupancyKind.Ambiguous => SceneSlotOccupancy.Ambiguous,
            _ => throw new ArgumentOutOfRangeException(nameof(occupancyKind)),
        };

        SceneApplyItemPreview result = SceneApplyItemResolver.Resolve(
            plan,
            lookup,
            explicitSelection: null,
            occupancy,
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));

        Assert.Equal(expectedAction, result.Action);
        Assert.Equal(expectedReason, result.Reason);
    }

    [Fact]
    public void ReplaceRequiresCurrentDurableUndoAvailability()
    {
        SceneActivityPlan plan = Plan(SceneConflictPolicy.ReplaceWithUndo);
        SceneSourceSelection source = Source(
            plan.ActivityId,
            "11111111-1111-1111-1111-111111111111",
            "source",
            revision: 1,
            'A');
        SceneSourceLookup lookup = SceneSourceLookup.FromObservation(
            0,
            plan.ActivityId,
            [source],
            isComplete: true);
        SceneSlotOccupancy unavailable = SceneSlotOccupancy.EligibleConflict(
            Target(plan),
            hasDurableUndoAvailability: false);

        SceneApplyItemPreview blocked = SceneApplyItemResolver.Resolve(
            plan,
            lookup,
            explicitSelection: null,
            unavailable,
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));

        Assert.False(unavailable.HasDurableUndoAvailability);
        Assert.Equal(SceneApplyAction.Blocked, blocked.Action);
        Assert.Equal(SceneApplyItemReason.UndoUnavailable, blocked.Reason);
        Assert.Equal(unavailable.Target, blocked.ReplaceTarget);
    }

    [Theory]
    [InlineData(SceneApplyItemReason.CapabilityDenied)]
    [InlineData(SceneApplyItemReason.ProtocolUnsupported)]
    [InlineData(SceneApplyItemReason.DestinationUnavailable)]
    public void PreInspectionFailureBindsSelectedSourceWithoutInventingOccupancy(
        SceneApplyItemReason reason)
    {
        SceneActivityPlan plan = Plan(SceneConflictPolicy.RequireEmpty);
        SceneSourceSelection source = Source(
            plan.ActivityId,
            "11111111-1111-1111-1111-111111111111",
            "source",
            revision: 1,
            'A');

        SceneApplyItemPreview blocked =
            SceneApplyItemPreview.BlockedBeforeOccupancy(
                plan,
                source,
                reason,
                OperationId.Parse("44444444-4444-4444-4444-444444444444"),
                CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));

        Assert.Equal(SceneApplyAction.Blocked, blocked.Action);
        Assert.Equal(reason, blocked.Reason);
        Assert.Equal(source, blocked.Source);
        Assert.Equal(SceneSlotOccupancyKind.NotInspected, blocked.Occupancy.Kind);
        Assert.Null(blocked.ReplaceTarget);
    }

    [Theory]
    [InlineData(SceneApplyItemReason.CapabilityDenied)]
    [InlineData(SceneApplyItemReason.ProtocolUnsupported)]
    public void SourceLookupAuthorizationFailureDisclosesNoCandidates(
        SceneApplyItemReason reason)
    {
        SceneActivityPlan plan = Plan(SceneConflictPolicy.RequireEmpty);

        SceneSourceLookup lookup = SceneSourceLookup.Unavailable(
            0,
            plan.ActivityId,
            reason);
        SceneApplyItemPreview blocked = SceneApplyItemResolver.Resolve(
            plan,
            lookup,
            explicitSelection: null,
            occupancy: null,
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));

        Assert.Empty(lookup.Candidates);
        Assert.Equal(reason, blocked.Reason);
        Assert.Null(blocked.Source);
    }

    private static SceneActivityPlan Plan(SceneConflictPolicy conflictPolicy) =>
        SceneActivityPlan.Place(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityPlacement.On(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "destination"),
            SceneSourceDisposition.PreserveSource,
            conflictPolicy);

    private static SceneSourceSelection Source(
        ActivityId activityId,
        string deviceId,
        string slot,
        long revision,
        char digestCharacter) =>
        SceneSourceSelection.Create(
            0,
            activityId,
            revision,
            new string(digestCharacter, 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(DeviceId.Parse(deviceId), slot));

    private static SceneReplaceTargetSnapshot Target(SceneActivityPlan plan) =>
        SceneReplaceTargetSnapshot.Create(
            ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            2,
            new string('B', 64),
            ActivityKind.Parse("workspace.note/v1"),
            plan.Placement);
}
