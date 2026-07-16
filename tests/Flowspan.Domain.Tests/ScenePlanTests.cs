using Flowspan.Domain;

namespace Flowspan.Domain.Tests;

public sealed class ScenePlanTests
{
    private static readonly ActivityId FirstActivity =
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly ActivityId SecondActivity =
        ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly DeviceId Laptop =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId Desktop =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void CreateIndividualPlanPreservesOrderAndDefensiveCopy()
    {
        SceneActivityPlan first = SceneActivityPlan.Place(
            FirstActivity,
            ActivityPlacement.On(Desktop, "main"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        SceneActivityPlan second = SceneActivityPlan.Place(
            SecondActivity,
            ActivityPlacement.On(Laptop, "side"),
            SceneSourceDisposition.MoveAfterAcknowledgement,
            SceneConflictPolicy.ReplaceWithUndo);
        var source = new List<SceneActivityPlan> { first, second };

        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "  Focus layout  ",
            source);
        source.Reverse();

        Assert.Equal(ScenePlan.CurrentFormatVersion, scene.FormatVersion);
        Assert.Equal(1, scene.Revision);
        Assert.Equal("Focus layout", scene.Name);
        Assert.Null(scene.GroupBinding);
        Assert.Collection(
            scene.Activities,
            activity => Assert.Same(first, activity),
            activity => Assert.Same(second, activity));
    }

    [Fact]
    public void UndefinedSceneActivityPoliciesAreRejected()
    {
        ActivityPlacement placement = ActivityPlacement.On(Desktop, "main");

        Assert.Throws<ArgumentOutOfRangeException>(() => SceneActivityPlan.Place(
            FirstActivity,
            placement,
            (SceneSourceDisposition)int.MaxValue,
            SceneConflictPolicy.RequireEmpty));
        Assert.Throws<ArgumentOutOfRangeException>(() => SceneActivityPlan.Place(
            FirstActivity,
            placement,
            SceneSourceDisposition.PreserveSource,
            (SceneConflictPolicy)int.MaxValue));
    }

    [Fact]
    public void InvalidScenePlacementSlotsAreRejected()
    {
        ActivityPlacement validPlacement = ActivityPlacement.On(
            Desktop,
            "valid-\U0001F680-slot");
        SceneActivityPlan valid = SceneActivityPlan.Place(
            FirstActivity,
            validPlacement,
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        Assert.Throws<ArgumentException>(() => ActivityPlacement.On(
            Desktop,
            "ma\nin"));
        Assert.Throws<ArgumentException>(() => ActivityPlacement.On(
            Desktop,
            "\tmain"));
        Assert.Throws<ArgumentException>(() => ActivityPlacement.On(
            Desktop,
            "main\n"));
        Assert.Equal("valid-\U0001F680-slot", valid.Placement.Slot);
        Assert.Throws<ArgumentException>(() => SceneActivityPlan.Place(
            FirstActivity,
            ActivityPlacement.On(Desktop, "invalid-\uD800-slot"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty));
    }

    [Fact]
    public void InvalidSceneMembershipIsRejected()
    {
        SceneId sceneId =
            SceneId.Parse("33333333-3333-3333-3333-333333333333");
        SceneActivityPlan first = CreatePlan(FirstActivity, Desktop, "main");

        Assert.Throws<ArgumentException>(() => ScenePlan.Create(
            sceneId,
            "Empty",
            []));
        Assert.Throws<ArgumentException>(() => ScenePlan.Create(
            sceneId,
            "Duplicate",
            [first, CreatePlan(FirstActivity, Laptop, "side")]));
        Assert.Throws<ArgumentException>(() => ScenePlan.Create(
            sceneId,
            "Null",
            [first, null!]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScenePlan.Create(
            sceneId,
            "Over bound",
            Enumerable.Range(1, ScenePlan.MaximumActivities + 1)
                .Select(index => CreatePlan(
                    ActivityId.From(Guid.Parse(
                        $"00000000-0000-0000-0000-{index:000000000000}")),
                    Desktop,
                    $"slot-{index}"))));
    }

    [Fact]
    public void OverBoundSceneStopsBeforeEnumeratingUnboundedInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScenePlan.Create(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "Bounded",
            EnumeratePlansPastBoundThenFail()));
    }

    [Fact]
    public void InvalidSceneNameIsRejected()
    {
        SceneId sceneId =
            SceneId.Parse("33333333-3333-3333-3333-333333333333");
        SceneActivityPlan activity =
            CreatePlan(FirstActivity, Desktop, "main");

        ScenePlan valid = ScenePlan.Create(
            sceneId,
            "Valid \U0001F680 Scene",
            [activity]);

        Assert.Throws<ArgumentException>(() => ScenePlan.Create(
            sceneId,
            " ",
            [activity]));
        Assert.Throws<ArgumentException>(() => ScenePlan.Create(
            sceneId,
            "Unsafe\nname",
            [activity]));
        Assert.Throws<ArgumentException>(() => ScenePlan.Create(
            sceneId,
            "\tScene",
            [activity]));
        Assert.Throws<ArgumentException>(() => ScenePlan.Create(
            sceneId,
            "Invalid \uD800 name",
            [activity]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScenePlan.Create(
            sceneId,
            new string('a', ScenePlan.MaximumNameCharacters + 1),
            [activity]));
        Assert.Equal("Valid \U0001F680 Scene", valid.Name);
    }

    [Fact]
    public void CreateFromGroupBindsExactGroupRevisionAndOrder()
    {
        ActivityGroup group = ActivityGroup.Create(
            GroupId.Parse("44444444-4444-4444-4444-444444444444"),
            "Focus group",
            [FirstActivity, SecondActivity]).Revise(
                "Focus group",
                [FirstActivity, SecondActivity]);
        SceneActivityPlan first =
            CreatePlan(FirstActivity, Desktop, "main");
        SceneActivityPlan second =
            CreatePlan(SecondActivity, Laptop, "side");

        ScenePlan scene = ScenePlan.CreateFromGroup(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "Group layout",
            group,
            [first, second]);

        Assert.NotNull(scene.GroupBinding);
        Assert.Equal(group.Id, scene.GroupBinding.GroupId);
        Assert.Equal(group.Revision, scene.GroupBinding.GroupRevision);
        Assert.Collection(
            scene.Activities,
            activity => Assert.Same(first, activity),
            activity => Assert.Same(second, activity));
    }

    [Fact]
    public void CreateFromGroupRejectsMembershipOrderMismatch()
    {
        ActivityGroup group = ActivityGroup.Create(
            GroupId.Parse("44444444-4444-4444-4444-444444444444"),
            "Focus group",
            [FirstActivity, SecondActivity]);

        Assert.Throws<ArgumentException>(() => ScenePlan.CreateFromGroup(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "Mismatched layout",
            group,
            [
                CreatePlan(SecondActivity, Laptop, "side"),
                CreatePlan(FirstActivity, Desktop, "main"),
            ]));
    }

    [Fact]
    public void ReviseIndividualPlanPreservesIdentityAndAdvancesOneRevision()
    {
        SceneId sceneId =
            SceneId.Parse("33333333-3333-3333-3333-333333333333");
        ScenePlan original = ScenePlan.Create(
            sceneId,
            "Focus layout",
            [CreatePlan(FirstActivity, Desktop, "main")]);
        SceneActivityPlan updated =
            CreatePlan(SecondActivity, Laptop, "side");

        ScenePlan revised = original.Revise("Updated layout", [updated]);

        Assert.Equal(sceneId, revised.Id);
        Assert.Equal(2, revised.Revision);
        Assert.Equal(ScenePlan.CurrentFormatVersion, revised.FormatVersion);
        Assert.Equal("Updated layout", revised.Name);
        Assert.Null(revised.GroupBinding);
        Assert.Collection(
            revised.Activities,
            activity => Assert.Same(updated, activity));
        Assert.Equal(1, original.Revision);
    }

    [Fact]
    public void ReviseFromGroupRebindsExactNewGroupRevision()
    {
        ActivityGroup originalGroup = ActivityGroup.Create(
            GroupId.Parse("44444444-4444-4444-4444-444444444444"),
            "Focus group",
            [FirstActivity, SecondActivity]);
        ScenePlan original = ScenePlan.CreateFromGroup(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "Group layout",
            originalGroup,
            [
                CreatePlan(FirstActivity, Desktop, "main"),
                CreatePlan(SecondActivity, Laptop, "side"),
            ]);
        ActivityGroup revisedGroup = originalGroup.Revise(
            "Focus group",
            [SecondActivity, FirstActivity]);
        SceneActivityPlan second =
            CreatePlan(SecondActivity, Desktop, "main");
        SceneActivityPlan first =
            CreatePlan(FirstActivity, Laptop, "side");

        ScenePlan revised = original.ReviseFromGroup(
            "Revised group layout",
            revisedGroup,
            [second, first]);

        Assert.Equal(original.Id, revised.Id);
        Assert.Equal(2, revised.Revision);
        Assert.NotNull(revised.GroupBinding);
        Assert.Equal(revisedGroup.Id, revised.GroupBinding.GroupId);
        Assert.Equal(revisedGroup.Revision, revised.GroupBinding.GroupRevision);
        Assert.Collection(
            revised.Activities,
            activity => Assert.Same(second, activity),
            activity => Assert.Same(first, activity));
    }

    [Fact]
    public void StringRepresentationRedactsNameSlotsAndActivityIds()
    {
        const string canary = "FLOWSPAN_SCENE_SECRET_CANARY";
        ActivityGroup group = ActivityGroup.Create(
            GroupId.Parse("44444444-4444-4444-4444-444444444444"),
            "Focus group",
            [FirstActivity]);
        SceneId sceneId =
            SceneId.Parse("33333333-3333-3333-3333-333333333333");
        ScenePlan scene = ScenePlan.CreateFromGroup(
            sceneId,
            canary,
            group,
            [CreatePlan(FirstActivity, Desktop, canary)]);

        string representation = scene.ToString();

        Assert.Equal(
            $"Scene {sceneId} format 1 revision 1 for Activity Group {group.Id} revision 1 (1 Activities)",
            representation);
        Assert.DoesNotContain(canary, representation, StringComparison.Ordinal);
        Assert.DoesNotContain(
            FirstActivity.ToString(),
            representation,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RestoredMaximumSceneRevisionCannotWrap()
    {
        ScenePlan scene = ScenePlan.Restore(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            long.MaxValue,
            "Focus layout",
            groupBinding: null,
            [CreatePlan(FirstActivity, Desktop, "main")]);

        Assert.Throws<OverflowException>(() => scene.Revise(
            scene.Name,
            scene.Activities));
    }

    [Fact]
    public void GroupDerivedRevisionRequiresTheSameBoundGroup()
    {
        ActivityGroup group = ActivityGroup.Create(
            GroupId.Parse("44444444-4444-4444-4444-444444444444"),
            "Focus group",
            [FirstActivity]);
        ScenePlan grouped = ScenePlan.CreateFromGroup(
            SceneId.Parse("33333333-3333-3333-3333-333333333333"),
            "Group layout",
            group,
            [CreatePlan(FirstActivity, Desktop, "main")]);
        ActivityGroup otherGroup = ActivityGroup.Create(
            GroupId.Parse("55555555-5555-5555-5555-555555555555"),
            "Other group",
            [FirstActivity]);
        ScenePlan individual = ScenePlan.Create(
            SceneId.Parse("66666666-6666-6666-6666-666666666666"),
            "Individual layout",
            [CreatePlan(FirstActivity, Desktop, "main")]);

        Assert.Throws<InvalidOperationException>(() => grouped.Revise(
            grouped.Name,
            grouped.Activities));
        Assert.Throws<InvalidOperationException>(() => grouped.ReviseFromGroup(
            grouped.Name,
            otherGroup,
            grouped.Activities));
        Assert.Throws<InvalidOperationException>(() => individual.ReviseFromGroup(
            individual.Name,
            group,
            individual.Activities));
    }

    private static SceneActivityPlan CreatePlan(
        ActivityId activityId,
        DeviceId deviceId,
        string slot) => SceneActivityPlan.Place(
            activityId,
            ActivityPlacement.On(deviceId, slot),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);

    private static IEnumerable<SceneActivityPlan>
        EnumeratePlansPastBoundThenFail()
    {
        for (int index = 1; index <= ScenePlan.MaximumActivities + 1; index++)
        {
            yield return CreatePlan(
                ActivityId.From(Guid.Parse(
                    $"00000000-0000-0000-0000-{index:000000000000}")),
                Desktop,
                $"slot-{index}");
        }

        throw new InvalidOperationException(
            "The Scene enumerated beyond its first excess item.");
    }
}
