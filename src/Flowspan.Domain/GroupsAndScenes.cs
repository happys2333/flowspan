using System.Collections.Immutable;
using System.Text;

namespace Flowspan.Domain;

public sealed record ActivityGroup
{
    public const int MaximumActivities = 64;
    public const int MaximumNameCharacters = 120;

    private ActivityGroup(
        GroupId id,
        string name,
        long revision,
        ImmutableArray<ActivityId> activities)
    {
        Id = id;
        Name = name;
        Revision = revision;
        Activities = activities;
    }

    public GroupId Id { get; }

    public long Revision { get; }

    public string Name { get; }

    public ImmutableArray<ActivityId> Activities { get; }

    public static ActivityGroup Create(
        GroupId id,
        string name,
        IEnumerable<ActivityId> activities) =>
        Create(id, name, revision: 1, activities);

    public static ActivityGroup Restore(
        GroupId id,
        long revision,
        string name,
        IEnumerable<ActivityId> activities) =>
        Create(id, name, revision, activities);

    public ActivityGroup Revise(
        string name,
        IEnumerable<ActivityId> activities) =>
        Create(Id, name, checked(Revision + 1), activities);

    public override string ToString() =>
        $"Activity Group {Id} revision {Revision} ({Activities.Length} Activities)";

    private static ActivityGroup Create(
        GroupId id,
        string name,
        long revision,
        IEnumerable<ActivityId> activities)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);

        if (!DomainText.IsWellFormedUtf16(name))
        {
            throw new ArgumentException(
                "An Activity Group name must contain well-formed Unicode text.",
                nameof(name));
        }

        if (name.Any(char.IsControl))
        {
            throw new ArgumentException(
                "An Activity Group name cannot contain control characters.",
                nameof(name));
        }

        string normalizedName = name.Trim();
        if (normalizedName.Length > MaximumNameCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(name),
                $"An Activity Group name cannot exceed {MaximumNameCharacters} characters.");
        }

        ImmutableArray<ActivityId> ordered = BoundedDomainCollection.Materialize(
            activities,
            MaximumActivities,
            nameof(activities),
            $"An Activity Group cannot contain more than {MaximumActivities} Activities.");
        if (ordered.IsEmpty)
        {
            throw new ArgumentException(
                "An Activity Group must contain at least one Activity.",
                nameof(activities));
        }

        if (ordered.Any(static activity => activity is null)
            || ordered.Distinct().Count() != ordered.Length)
        {
            throw new ArgumentException(
                "An Activity Group must contain distinct non-null Activity IDs.",
                nameof(activities));
        }

        return new ActivityGroup(
            id,
            normalizedName,
            revision,
            ordered);
    }

}

public enum SceneSourceDisposition
{
    PreserveSource,
    MoveAfterAcknowledgement,
}

public enum SceneConflictPolicy
{
    RequireEmpty,
    ReplaceWithUndo,
}

public sealed record SceneActivityPlan
{
    private SceneActivityPlan(
        ActivityId activityId,
        ActivityPlacement placement,
        SceneSourceDisposition sourceDisposition,
        SceneConflictPolicy conflictPolicy)
    {
        ActivityId = activityId;
        Placement = placement;
        SourceDisposition = sourceDisposition;
        ConflictPolicy = conflictPolicy;
    }

    public ActivityId ActivityId { get; }

    public ActivityPlacement Placement { get; }

    public SceneSourceDisposition SourceDisposition { get; }

    public SceneConflictPolicy ConflictPolicy { get; }

    public static SceneActivityPlan Place(
        ActivityId activityId,
        ActivityPlacement placement,
        SceneSourceDisposition sourceDisposition,
        SceneConflictPolicy conflictPolicy)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(placement);
        if (!DomainText.IsWellFormedUtf16(placement.Slot))
        {
            throw new ArgumentException(
                "A Scene placement slot must contain well-formed Unicode text.",
                nameof(placement));
        }

        if (placement.Slot.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A Scene placement slot cannot contain control characters.",
                nameof(placement));
        }

        if (!Enum.IsDefined(sourceDisposition))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceDisposition));
        }

        if (!Enum.IsDefined(conflictPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(conflictPolicy));
        }

        return new SceneActivityPlan(
            activityId,
            placement,
            sourceDisposition,
            conflictPolicy);
    }
}

public sealed record SceneGroupBinding
{
    private SceneGroupBinding(GroupId groupId, long groupRevision)
    {
        GroupId = groupId;
        GroupRevision = groupRevision;
    }

    public GroupId GroupId { get; }

    public long GroupRevision { get; }

    public static SceneGroupBinding Create(GroupId groupId, long groupRevision)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentOutOfRangeException.ThrowIfLessThan(groupRevision, 1);
        return new SceneGroupBinding(groupId, groupRevision);
    }
}

public sealed record ScenePlan
{
    public const int CurrentFormatVersion = 1;
    public const int MaximumActivities = 64;
    public const int MaximumNameCharacters = 120;

    private ScenePlan(
        SceneId id,
        long revision,
        string name,
        SceneGroupBinding? groupBinding,
        ImmutableArray<SceneActivityPlan> activities)
    {
        Id = id;
        Revision = revision;
        Name = name;
        GroupBinding = groupBinding;
        Activities = activities;
    }

    public SceneId Id { get; }

    public long Revision { get; }

    public int FormatVersion { get; } = CurrentFormatVersion;

    public string Name { get; }

    public SceneGroupBinding? GroupBinding { get; }

    public ImmutableArray<SceneActivityPlan> Activities { get; }

    public static ScenePlan Create(
        SceneId id,
        string name,
        IEnumerable<SceneActivityPlan> activities) =>
        Create(id, name, revision: 1, groupBinding: null, activities);

    public static ScenePlan Restore(
        SceneId id,
        long revision,
        string name,
        SceneGroupBinding? groupBinding,
        IEnumerable<SceneActivityPlan> activities) =>
        Create(id, name, revision, groupBinding, activities);

    public ScenePlan Revise(
        string name,
        IEnumerable<SceneActivityPlan> activities)
    {
        if (GroupBinding is not null)
        {
            throw new InvalidOperationException(
                "A Group-derived Scene must be revised from an exact Group revision.");
        }

        return Create(
            Id,
            name,
            checked(Revision + 1),
            groupBinding: null,
            activities);
    }

    public ScenePlan ReviseFromGroup(
        string name,
        ActivityGroup group,
        IEnumerable<SceneActivityPlan> activities)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (GroupBinding is null || GroupBinding.GroupId != group.Id)
        {
            throw new InvalidOperationException(
                "A Group-derived Scene can only be revised from the same Group ID.");
        }

        ScenePlan scene = Create(
            Id,
            name,
            checked(Revision + 1),
            SceneGroupBinding.Create(group.Id, group.Revision),
            activities);
        ValidateGroupOrder(group, scene.Activities, nameof(activities));
        return scene;
    }

    public override string ToString()
    {
        string group = GroupBinding is null
            ? string.Empty
            : $" for Activity Group {GroupBinding.GroupId} revision {GroupBinding.GroupRevision}";
        return $"Scene {Id} format {FormatVersion} revision {Revision}{group} ({Activities.Length} Activities)";
    }

    private static ScenePlan Create(
        SceneId id,
        string name,
        long revision,
        SceneGroupBinding? groupBinding,
        IEnumerable<SceneActivityPlan> activities)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);

        if (!DomainText.IsWellFormedUtf16(name))
        {
            throw new ArgumentException(
                "A Scene name must contain well-formed Unicode text.",
                nameof(name));
        }

        if (name.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A Scene name cannot contain control characters.",
                nameof(name));
        }

        string normalizedName = name.Trim();
        if (normalizedName.Length > MaximumNameCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(name),
                $"A Scene name cannot exceed {MaximumNameCharacters} characters.");
        }

        ImmutableArray<SceneActivityPlan> ordered =
            BoundedDomainCollection.Materialize(
                activities,
                MaximumActivities,
                nameof(activities),
                $"A Scene plan cannot contain more than {MaximumActivities} Activities.");
        if (ordered.IsEmpty)
        {
            throw new ArgumentException(
                "A Scene plan must contain at least one Activity.",
                nameof(activities));
        }

        if (ordered.Any(static activity => activity is null)
            || ordered.Select(static activity => activity.ActivityId)
                .Distinct()
                .Count() != ordered.Length)
        {
            throw new ArgumentException(
                "A Scene plan must contain distinct non-null Activity plans.",
                nameof(activities));
        }

        return new ScenePlan(
            id,
            revision,
            normalizedName,
            groupBinding,
            ordered);
    }

    public static ScenePlan CreateFromGroup(
        SceneId id,
        string name,
        ActivityGroup group,
        IEnumerable<SceneActivityPlan> activities)
    {
        ArgumentNullException.ThrowIfNull(group);
        ScenePlan scene = Create(
            id,
            name,
            revision: 1,
            SceneGroupBinding.Create(group.Id, group.Revision),
            activities);
        ValidateGroupOrder(group, scene.Activities, nameof(activities));

        return scene;
    }

    private static void ValidateGroupOrder(
        ActivityGroup group,
        ImmutableArray<SceneActivityPlan> activities,
        string parameterName)
    {
        if (!group.Activities.SequenceEqual(
            activities.Select(static activity => activity.ActivityId)))
        {
            throw new ArgumentException(
                "A Group-derived Scene must contain the Group's exact Activity order.",
                parameterName);
        }
    }
}

internal static class BoundedDomainCollection
{
    public static ImmutableArray<T> Materialize<T>(
        IEnumerable<T> source,
        int maximumCount,
        string parameterName,
        string failureMessage)
    {
        var builder = ImmutableArray.CreateBuilder<T>();
        foreach (T item in source)
        {
            if (builder.Count == maximumCount)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    failureMessage);
            }

            builder.Add(item);
        }

        return builder.ToImmutable();
    }
}

internal static class DomainText
{
    public static bool IsWellFormedUtf16(string value)
    {
        ReadOnlySpan<char> remaining = value;
        while (!remaining.IsEmpty)
        {
            System.Buffers.OperationStatus status = Rune.DecodeFromUtf16(
                remaining,
                out _,
                out int charactersConsumed);
            if (status != System.Buffers.OperationStatus.Done)
            {
                return false;
            }

            remaining = remaining[charactersConsumed..];
        }

        return true;
    }
}
