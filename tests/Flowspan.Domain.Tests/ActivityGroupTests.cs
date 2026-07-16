using Flowspan.Domain;

namespace Flowspan.Domain.Tests;

public sealed class ActivityGroupTests
{
    private static readonly ActivityId First =
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly ActivityId Second =
        ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly ActivityId Third =
        ActivityId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void CreatePreservesExplicitOrderAndDefensiveCopy()
    {
        var source = new List<ActivityId> { First, Second };

        ActivityGroup group = ActivityGroup.Create(
            GroupId.Parse("11111111-1111-1111-1111-111111111111"),
            "  Focus work  ",
            source);
        source.Reverse();
        source.Add(Third);

        Assert.Equal("Focus work", group.Name);
        Assert.Equal(1, group.Revision);
        Assert.Collection(
            group.Activities,
            activity => Assert.Equal(First, activity),
            activity => Assert.Equal(Second, activity));
    }

    [Fact]
    public void InvalidMembershipIsRejected()
    {
        GroupId groupId =
            GroupId.Parse("11111111-1111-1111-1111-111111111111");

        Assert.Throws<ArgumentException>(() => ActivityGroup.Create(
            groupId,
            "Empty",
            []));
        Assert.Throws<ArgumentException>(() => ActivityGroup.Create(
            groupId,
            "Duplicate",
            [First, First]));
        Assert.Throws<ArgumentException>(() => ActivityGroup.Create(
            groupId,
            "Null",
            [First, null!]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ActivityGroup.Create(
            groupId,
            "Over bound",
            Enumerable.Range(1, ActivityGroup.MaximumActivities + 1)
                .Select(index => ActivityId.From(Guid.Parse(
                    $"00000000-0000-0000-0000-{index:000000000000}")))));
    }

    [Fact]
    public void OverBoundMembershipStopsBeforeEnumeratingUnboundedInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ActivityGroup.Create(
            GroupId.Parse("11111111-1111-1111-1111-111111111111"),
            "Bounded",
            EnumeratePastBoundThenFail()));
    }

    [Fact]
    public void InvalidNameIsRejected()
    {
        GroupId groupId =
            GroupId.Parse("11111111-1111-1111-1111-111111111111");

        ActivityGroup valid = ActivityGroup.Create(
            groupId,
            "Valid \U0001F680 name",
            [First]);

        Assert.Throws<ArgumentException>(() => ActivityGroup.Create(
            groupId,
            " ",
            [First]));
        Assert.Throws<ArgumentException>(() => ActivityGroup.Create(
            groupId,
            "Unsafe\nname",
            [First]));
        Assert.Throws<ArgumentException>(() => ActivityGroup.Create(
            groupId,
            "\nFocus",
            [First]));
        Assert.Throws<ArgumentException>(() => ActivityGroup.Create(
            groupId,
            "Invalid \uD800 name",
            [First]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ActivityGroup.Create(
            groupId,
            new string('a', ActivityGroup.MaximumNameCharacters + 1),
            [First]));
        Assert.Equal("Valid \U0001F680 name", valid.Name);
    }

    [Fact]
    public void RevisePreservesIdentityAndAdvancesOneRevision()
    {
        GroupId groupId =
            GroupId.Parse("11111111-1111-1111-1111-111111111111");
        ActivityGroup original = ActivityGroup.Create(
            groupId,
            "Focus work",
            [First, Second]);

        ActivityGroup revised = original.Revise(
            "Deep work",
            [Third, First]);

        Assert.Equal(groupId, revised.Id);
        Assert.Equal(2, revised.Revision);
        Assert.Equal("Deep work", revised.Name);
        Assert.Collection(
            revised.Activities,
            activity => Assert.Equal(Third, activity),
            activity => Assert.Equal(First, activity));
        Assert.Equal(1, original.Revision);
        Assert.Equal("Focus work", original.Name);
    }

    [Fact]
    public void StringRepresentationRedactsNameAndMembership()
    {
        const string canary = "FLOWSPAN_GROUP_SECRET_CANARY";
        GroupId groupId =
            GroupId.Parse("11111111-1111-1111-1111-111111111111");
        ActivityGroup group = ActivityGroup.Create(
            groupId,
            canary,
            [First, Second]);

        string representation = group.ToString();

        Assert.Equal(
            $"Activity Group {groupId} revision 1 (2 Activities)",
            representation);
        Assert.DoesNotContain(canary, representation, StringComparison.Ordinal);
        Assert.DoesNotContain(
            First.ToString(),
            representation,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RestoredMaximumRevisionCannotWrap()
    {
        ActivityGroup group = ActivityGroup.Restore(
            GroupId.Parse("11111111-1111-1111-1111-111111111111"),
            long.MaxValue,
            "Focus work",
            [First]);

        Assert.Throws<OverflowException>(() => group.Revise(
            group.Name,
            group.Activities));
    }

    [Fact]
    public void GroupAndSceneIdentifiersRejectEmptyValues()
    {
        Assert.Throws<ArgumentException>(() => GroupId.From(Guid.Empty));
        Assert.Throws<ArgumentException>(() => SceneId.From(Guid.Empty));
        Assert.Equal(
            "abcdefab-cdef-abcd-efab-cdefabcdefab",
            GroupId.Parse("ABCDEFAB-CDEF-ABCD-EFAB-CDEFABCDEFAB").ToString());
        Assert.Equal(
            "abcdefab-cdef-abcd-efab-cdefabcdefab",
            SceneId.Parse("ABCDEFAB-CDEF-ABCD-EFAB-CDEFABCDEFAB").ToString());
    }

    private static IEnumerable<ActivityId> EnumeratePastBoundThenFail()
    {
        for (int index = 1; index <= ActivityGroup.MaximumActivities + 1; index++)
        {
            yield return ActivityId.From(Guid.Parse(
                $"00000000-0000-0000-0000-{index:000000000000}"));
        }

        throw new InvalidOperationException(
            "The Group enumerated beyond its first excess item.");
    }
}
