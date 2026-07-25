using System.Security.Cryptography;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class SceneApplyPreviewTests
{
    [Fact]
    public void CreateNoChangePreviewBindsSceneAndDefensivelyCopiesItems()
    {
        DeviceId destination = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityId activityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SceneActivityPlan plan = SceneActivityPlan.Place(
            activityId,
            ActivityPlacement.On(destination, "main-🚀"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "Focus layout",
            [plan]);
        SceneSourceSelection source = SceneSourceSelection.Create(
            0,
            activityId,
            7,
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(destination, "main-🚀"));
        SceneApplyItemPreview item = SceneApplyItemPreview.NoChange(
            plan,
            source,
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));
        var suppliedItems = new List<SceneApplyItemPreview> { item };
        DateTimeOffset createdAt = new(2026, 7, 25, 8, 0, 0, TimeSpan.Zero);

        SceneApplyPreview preview = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("66666666-6666-6666-6666-666666666666"),
            CorrelationId.Parse("77777777-7777-7777-7777-777777777777"),
            createdAt,
            createdAt.AddMinutes(5),
            suppliedItems);
        suppliedItems.Clear();

        Assert.Equal(scene.Id, preview.SceneId);
        Assert.Equal(scene.Revision, preview.SceneRevision);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(ScenePlanCodec.Encode(scene))),
            preview.SceneDigest);
        SceneApplyItemPreview retained = Assert.Single(preview.Items);
        Assert.Equal(SceneApplyAction.NoChange, retained.Action);
        Assert.Equal(source, retained.Source);
        Assert.Matches("^[0-9A-F]{64}$", preview.Fingerprint);
        Assert.Equal(
            "6080446C319F41BE8EDEC1E2C17291ED6365AAB38484B963946EE00A647B0A69",
            preview.Fingerprint);
        Assert.Equal(
            preview.Fingerprint,
            SceneApplyPreview.Create(
                scene,
                preview.ParentOperationId,
                preview.ParentCorrelationId,
                createdAt,
                createdAt.AddMinutes(5),
                [item]).Fingerprint);
        Assert.DoesNotContain(scene.Name, preview.ToString());
        Assert.DoesNotContain(plan.Placement.Slot, preview.ToString());
        Assert.DoesNotContain(source.ActivityId.ToString(), source.ToString());
        Assert.DoesNotContain(source.DescriptorDigest, source.ToString());
        Assert.DoesNotContain(source.Placement.Slot, source.ToString());
    }

    [Fact]
    public void ExactApprovalIsValidForUnchangedUnexpiredNoChangePreview()
    {
        (ScenePlan scene, SceneApplyPreview preview) = CreateNoChangePreview();
        SceneApplyApproval approval = SceneApplyApproval.Create(
            preview.Fingerprint,
            []);

        SceneApplyApprovalStatus status = SceneApplyApprovalVerifier.Validate(
            scene,
            preview,
            approval,
            preview.ExpiresAt.AddTicks(-1));

        Assert.Equal(SceneApplyApprovalStatus.Valid, status);
        Assert.Empty(approval.ReplaceConfirmations);
    }

    [Fact]
    public void ApprovalRejectsExpiredChangedSceneAndAnotherPreviewFingerprint()
    {
        (ScenePlan scene, SceneApplyPreview preview) = CreateNoChangePreview();
        SceneApplyApproval exact = SceneApplyApproval.Create(
            preview.Fingerprint,
            []);
        SceneApplyApproval anotherPreview = SceneApplyApproval.Create(
            "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF",
            []);
        ScenePlan changed = scene.Revise(scene.Name, scene.Activities);

        Assert.Equal(
            SceneApplyApprovalStatus.Expired,
            SceneApplyApprovalVerifier.Validate(
                scene,
                preview,
                exact,
                preview.ExpiresAt));
        Assert.Equal(
            SceneApplyApprovalStatus.SceneChanged,
            SceneApplyApprovalVerifier.Validate(
                changed,
                preview,
                exact,
                preview.CreatedAt));
        Assert.Equal(
            SceneApplyApprovalStatus.PreviewMismatch,
            SceneApplyApprovalVerifier.Validate(
                scene,
                preview,
                anotherPreview,
                preview.CreatedAt));
    }

    [Fact]
    public void ReplacePreviewRequiresItsExactTargetConfirmation()
    {
        DeviceId sourceDevice = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId targetDevice = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityId incomingId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        ActivityKind kind = ActivityKind.Parse("workspace.note/v1");
        SceneActivityPlan plan = SceneActivityPlan.Place(
            incomingId,
            ActivityPlacement.On(targetDevice, "main"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.ReplaceWithUndo);
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "Focus layout",
            [plan]);
        SceneSourceSelection source = SceneSourceSelection.Create(
            0,
            incomingId,
            3,
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            kind,
            ActivityPlacement.On(sourceDevice, "source"));
        SceneReplaceTargetSnapshot target = SceneReplaceTargetSnapshot.Create(
            ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            9,
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            kind,
            plan.Placement);
        SceneApplyItemPreview item = SceneApplyItemPreview.Replace(
            plan,
            source,
            target,
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));
        DateTimeOffset createdAt = new(2026, 7, 25, 8, 0, 0, TimeSpan.Zero);
        SceneApplyPreview preview = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("66666666-6666-6666-6666-666666666666"),
            CorrelationId.Parse("77777777-7777-7777-7777-777777777777"),
            createdAt,
            createdAt.AddMinutes(5),
            [item]);

        SceneApplyApproval missing = SceneApplyApproval.Create(
            preview.Fingerprint,
            []);
        SceneReplaceConfirmation exact = Assert.Single(
            preview.RequiredReplaceConfirmations);
        SceneApplyApproval confirmed = SceneApplyApproval.Create(
            preview.Fingerprint,
            [exact]);
        SceneApplyApproval wrongTarget = SceneApplyApproval.Create(
            preview.Fingerprint,
            [SceneReplaceConfirmation.Create(
                exact.Index,
                "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC")]);

        Assert.Equal(
            SceneApplyApprovalStatus.ReplaceConfirmationMismatch,
            SceneApplyApprovalVerifier.Validate(
                scene,
                preview,
                missing,
                createdAt));
        Assert.Equal(
            SceneApplyApprovalStatus.Valid,
            SceneApplyApprovalVerifier.Validate(
                scene,
                preview,
                confirmed,
                createdAt));
        Assert.Equal(
            SceneApplyApprovalStatus.ReplaceConfirmationMismatch,
            SceneApplyApprovalVerifier.Validate(
                scene,
                preview,
                wrongTarget,
                createdAt));
    }

    [Fact]
    public void EmptySlotsMapSourceDispositionToHandoffAndMoveInSavedOrder()
    {
        DeviceId sourceDevice = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId targetDevice = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityKind kind = ActivityKind.Parse("workspace.note/v1");
        SceneActivityPlan handoffPlan = SceneActivityPlan.Place(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityPlacement.On(targetDevice, "main"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        SceneActivityPlan movePlan = SceneActivityPlan.Place(
            ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ActivityPlacement.On(targetDevice, "side"),
            SceneSourceDisposition.MoveAfterAcknowledgement,
            SceneConflictPolicy.ReplaceWithUndo);
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "Focus layout",
            [handoffPlan, movePlan]);
        SceneApplyItemPreview handoff = SceneApplyItemPreview.TransferToEmpty(
            handoffPlan,
            SceneSourceSelection.Create(
                0,
                handoffPlan.ActivityId,
                1,
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                kind,
                ActivityPlacement.On(sourceDevice, "source-main")),
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));
        SceneApplyItemPreview move = SceneApplyItemPreview.TransferToEmpty(
            movePlan,
            SceneSourceSelection.Create(
                1,
                movePlan.ActivityId,
                2,
                "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                kind,
                ActivityPlacement.On(sourceDevice, "source-side")),
            OperationId.Parse("66666666-6666-6666-6666-666666666666"),
            CorrelationId.Parse("77777777-7777-7777-7777-777777777777"));
        DateTimeOffset createdAt = new(2026, 7, 25, 8, 0, 0, TimeSpan.Zero);

        SceneApplyPreview preview = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("88888888-8888-8888-8888-888888888888"),
            CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
            createdAt,
            createdAt.AddMinutes(5),
            [handoff, move]);

        Assert.Collection(
            preview.Items,
            item => Assert.Equal(SceneApplyAction.Handoff, item.Action),
            item => Assert.Equal(SceneApplyAction.Move, item.Action));
        Assert.All(
            preview.Items,
            item => Assert.Equal(SceneSlotOccupancy.Empty, item.Occupancy));
    }

    [Fact]
    public void OccupiedMovePlusReplaceIsABlockedNonDestructivePreview()
    {
        DeviceId sourceDevice = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId targetDevice = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityKind kind = ActivityKind.Parse("workspace.note/v1");
        SceneActivityPlan plan = SceneActivityPlan.Place(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityPlacement.On(targetDevice, "target-canary-slot"),
            SceneSourceDisposition.MoveAfterAcknowledgement,
            SceneConflictPolicy.ReplaceWithUndo);
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "Focus layout",
            [plan]);
        SceneSourceSelection source = SceneSourceSelection.Create(
            0,
            plan.ActivityId,
            3,
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            kind,
            ActivityPlacement.On(sourceDevice, "source-canary-slot"));
        SceneReplaceTargetSnapshot target = SceneReplaceTargetSnapshot.Create(
            ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            9,
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            kind,
            plan.Placement);
        SceneApplyItemPreview blocked = SceneApplyItemPreview.BlockedByOccupancy(
            plan,
            source,
            SceneSlotOccupancy.EligibleConflict(target),
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));
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
        Assert.Equal(SceneApplyItemReason.UnsafeMoveReplace, item.Reason);
        Assert.Empty(preview.RequiredReplaceConfirmations);
        Assert.DoesNotContain("target-canary-slot", item.ToString());
        Assert.DoesNotContain(target.DescriptorDigest, item.ToString());
    }

    [Fact]
    public void KnownChangedGroupRevisionIsBoundAsWarningWithoutLiveExpansion()
    {
        DeviceId destination = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityId activityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        ActivityGroup group = ActivityGroup.Create(
            GroupId.Parse("11111111-1111-1111-1111-111111111111"),
            "Focus group",
            [activityId]);
        SceneActivityPlan plan = SceneActivityPlan.Place(
            activityId,
            ActivityPlacement.On(destination, "main"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        ScenePlan scene = ScenePlan.CreateFromGroup(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "Focus layout",
            group,
            [plan]);
        SceneSourceSelection source = SceneSourceSelection.Create(
            0,
            activityId,
            1,
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ActivityKind.Parse("workspace.note/v1"),
            plan.Placement);
        SceneApplyItemPreview item = SceneApplyItemPreview.NoChange(
            plan,
            source,
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));
        DateTimeOffset createdAt = new(2026, 7, 25, 8, 0, 0, TimeSpan.Zero);

        SceneApplyPreview stale = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("66666666-6666-6666-6666-666666666666"),
            CorrelationId.Parse("77777777-7777-7777-7777-777777777777"),
            createdAt,
            createdAt.AddMinutes(5),
            [item],
            observedGroupRevision: 2);
        SceneApplyPreview current = SceneApplyPreview.Create(
            scene,
            stale.ParentOperationId,
            stale.ParentCorrelationId,
            createdAt,
            createdAt.AddMinutes(5),
            [item],
            observedGroupRevision: group.Revision);

        Assert.NotNull(stale.GroupRevisionWarning);
        Assert.Equal(group.Id, stale.GroupRevisionWarning.GroupId);
        Assert.Equal(group.Revision, stale.GroupRevisionWarning.BoundRevision);
        Assert.Equal(2, stale.GroupRevisionWarning.ObservedRevision);
        Assert.Null(current.GroupRevisionWarning);
        Assert.NotEqual(current.Fingerprint, stale.Fingerprint);
        Assert.Single(stale.Items);
    }

    [Fact]
    public void ParentAndChildOperationIdentitiesMustBeDistinct()
    {
        (ScenePlan scene, SceneApplyPreview preview) = CreateNoChangePreview();
        SceneApplyItemPreview item = Assert.Single(preview.Items);

        Assert.Throws<ArgumentException>(() => SceneApplyPreview.Create(
            scene,
            item.ChildOperationId,
            preview.ParentCorrelationId,
            preview.CreatedAt,
            preview.ExpiresAt,
            [item]));
        Assert.Throws<ArgumentException>(() => SceneApplyPreview.Create(
            scene,
            preview.ParentOperationId,
            item.ChildCorrelationId,
            preview.CreatedAt,
            preview.ExpiresAt,
            [item]));
    }

    [Fact]
    public void PreviewAcceptsExactly64ItemsAndRejectsTheNextSourceIndex()
    {
        DeviceId device = DeviceId.Parse(
            "60000000-0000-0000-0000-000000000000");
        ActivityKind kind = ActivityKind.Parse("workspace.note/v1");
        var plans = new List<SceneActivityPlan>();
        var items = new List<SceneApplyItemPreview>();
        for (int index = 0; index < ScenePlan.MaximumActivities; index++)
        {
            ActivityId activityId = ActivityId.From(Guid.Parse(
                $"00000000-0000-0000-0000-{index + 1:000000000000}"));
            SceneActivityPlan plan = SceneActivityPlan.Place(
                activityId,
                ActivityPlacement.On(device, $"slot-{index}"),
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.RequireEmpty);
            plans.Add(plan);
            items.Add(SceneApplyItemPreview.NoChange(
                plan,
                SceneSourceSelection.Create(
                    index,
                    activityId,
                    index + 1,
                    Convert.ToHexString(SHA256.HashData(
                        activityId.Value.ToByteArray())),
                    kind,
                    plan.Placement),
                OperationId.From(Guid.Parse(
                    $"10000000-0000-0000-0000-{index + 1:000000000000}")),
                CorrelationId.From(Guid.Parse(
                    $"20000000-0000-0000-0000-{index + 1:000000000000}"))));
        }

        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("50000000-0000-0000-0000-000000000000"),
            "Maximum Scene",
            plans);
        DateTimeOffset createdAt = new(2026, 7, 25, 8, 0, 0, TimeSpan.Zero);
        SceneApplyPreview preview = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("30000000-0000-0000-0000-000000000000"),
            CorrelationId.Parse("40000000-0000-0000-0000-000000000000"),
            createdAt,
            createdAt.AddMinutes(5),
            items);

        Assert.Equal(ScenePlan.MaximumActivities, preview.Items.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SceneSourceSelection.Create(
                ScenePlan.MaximumActivities,
                plans[0].ActivityId,
                1,
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                kind,
                plans[0].Placement));
    }

    [Fact]
    public void NonCanonicalBindingsAndInvalidPreviewWindowsAreRejected()
    {
        (ScenePlan scene, SceneApplyPreview preview) = CreateNoChangePreview();
        SceneApplyItemPreview item = Assert.Single(preview.Items);
        SceneSourceSelection source = Assert.IsType<SceneSourceSelection>(
            item.Source);

        Assert.Throws<ArgumentException>(() => SceneSourceSelection.Create(
            0,
            item.ActivityId,
            1,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            source.Kind,
            source.Placement));
        Assert.Throws<ArgumentException>(() => SceneApplyApproval.Create(
            preview.Fingerprint.ToLowerInvariant(),
            []));
        Assert.Throws<ArgumentOutOfRangeException>(() => SceneApplyPreview.Create(
            scene,
            preview.ParentOperationId,
            preview.ParentCorrelationId,
            preview.CreatedAt,
            preview.CreatedAt.Add(SceneApplyPreview.MaximumLifetime).AddTicks(1),
            [item]));
        Assert.Throws<ArgumentException>(() => SceneApplyPreview.Create(
            scene,
            preview.ParentOperationId,
            preview.ParentCorrelationId,
            preview.CreatedAt,
            preview.ExpiresAt,
            [item],
            observedGroupRevision: 2));
    }

    private static (ScenePlan Scene, SceneApplyPreview Preview)
        CreateNoChangePreview()
    {
        DeviceId destination = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        ActivityId activityId = ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SceneActivityPlan plan = SceneActivityPlan.Place(
            activityId,
            ActivityPlacement.On(destination, "main"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "Focus layout",
            [plan]);
        SceneSourceSelection source = SceneSourceSelection.Create(
            0,
            activityId,
            1,
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ActivityKind.Parse("workspace.note/v1"),
            plan.Placement);
        SceneApplyItemPreview item = SceneApplyItemPreview.NoChange(
            plan,
            source,
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));
        DateTimeOffset createdAt = new(2026, 7, 25, 8, 0, 0, TimeSpan.Zero);
        SceneApplyPreview preview = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("66666666-6666-6666-6666-666666666666"),
            CorrelationId.Parse("77777777-7777-7777-7777-777777777777"),
            createdAt,
            createdAt.Add(SceneApplyPreview.MaximumLifetime),
            [item]);
        return (scene, preview);
    }
}
