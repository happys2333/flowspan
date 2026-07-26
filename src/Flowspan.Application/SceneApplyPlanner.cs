using System.Collections.Immutable;
using Flowspan.Domain;

namespace Flowspan.Application;

public interface ISceneApplyPreflightPort
{
    public ValueTask<SceneSourceLookup> LocateSourcesAsync(
        ActivityId activityId,
        int index,
        OperationContext childContext,
        CancellationToken cancellationToken);

    public ValueTask<SceneExactSlotInspection> InspectExactSlotAsync(
        SceneActivityPlan item,
        SceneSourceSelection source,
        OperationContext childContext,
        CancellationToken cancellationToken);
}

public interface ISceneApplyIdSource
{
    public OperationId CreateOperationId();

    public CorrelationId CreateCorrelationId();
}

public sealed class CryptographicSceneApplyIdSource : ISceneApplyIdSource
{
    private CryptographicSceneApplyIdSource()
    {
    }

    public static CryptographicSceneApplyIdSource Instance { get; } = new();

    public OperationId CreateOperationId() => OperationId.From(Guid.NewGuid());

    public CorrelationId CreateCorrelationId() =>
        CorrelationId.From(Guid.NewGuid());
}

public sealed class DeterministicSceneApplyIdSource : ISceneApplyIdSource
{
    private readonly Lock gate = new();
    private readonly Queue<CorrelationId> correlationIds;
    private readonly Queue<OperationId> operationIds;

    public DeterministicSceneApplyIdSource(
        IEnumerable<OperationId> operationIds,
        IEnumerable<CorrelationId> correlationIds)
    {
        ArgumentNullException.ThrowIfNull(operationIds);
        ArgumentNullException.ThrowIfNull(correlationIds);
        this.operationIds = new Queue<OperationId>(operationIds);
        this.correlationIds = new Queue<CorrelationId>(correlationIds);
    }

    public OperationId CreateOperationId()
    {
        lock (gate)
        {
            return operationIds.Count > 0
                ? operationIds.Dequeue()
                : throw new InvalidOperationException(
                    "The deterministic Scene operation ID source is empty.");
        }
    }

    public CorrelationId CreateCorrelationId()
    {
        lock (gate)
        {
            return correlationIds.Count > 0
                ? correlationIds.Dequeue()
                : throw new InvalidOperationException(
                    "The deterministic Scene correlation ID source is empty.");
        }
    }
}

public sealed class SceneApplyPlanner
{
    private readonly IClock clock;
    private readonly ISceneApplyIdSource idSource;
    private readonly ISceneApplyPreflightPort preflight;

    public SceneApplyPlanner(
        IClock clock,
        ISceneApplyPreflightPort preflight)
        : this(
            clock,
            preflight,
            CryptographicSceneApplyIdSource.Instance)
    {
    }

    public SceneApplyPlanner(
        IClock clock,
        ISceneApplyPreflightPort preflight,
        ISceneApplyIdSource idSource)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.preflight = preflight
            ?? throw new ArgumentNullException(nameof(preflight));
        this.idSource = idSource
            ?? throw new ArgumentNullException(nameof(idSource));
    }

    public async ValueTask<SceneApplyPreview> PreviewAsync(
        ScenePlan scene,
        IEnumerable<SceneSourceSelection> selectedSources,
        long? observedGroupRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ValidateObservedGroupRevision(scene, observedGroupRevision);
        ImmutableDictionary<int, SceneSourceSelection> selections =
            CopySelections(scene, selectedSources);
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset createdAt = clock.UtcNow.ToUniversalTime();
        if (createdAt > DateTimeOffset.MaxValue - SceneApplyPreview.MaximumLifetime)
        {
            throw new InvalidOperationException(
                "The current time cannot represent a bounded Scene preview.");
        }

        DateTimeOffset expiresAt =
            createdAt + SceneApplyPreview.MaximumLifetime;
        SceneApplyIdentities identities = CreateIdentities(
            scene.Activities.Length);
        var items = ImmutableArray.CreateBuilder<SceneApplyItemPreview>(
            scene.Activities.Length);
        for (int index = 0; index < scene.Activities.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SceneActivityPlan plan = scene.Activities[index];
            OperationContext childContext = OperationContext.Create(
                identities.ChildOperationIds[index],
                identities.ChildCorrelationIds[index],
                expiresAt);
            SceneSourceLookup lookup = await LocateSourcesAsync(
                plan,
                index,
                childContext,
                cancellationToken).ConfigureAwait(false);
            selections.TryGetValue(
                index,
                out SceneSourceSelection? selectedSource);
            SceneSourceSelection? source =
                SceneApplyItemResolver.ResolveSource(
                    lookup,
                    selectedSource,
                    occupancy: null);
            if (source is null || source.Placement == plan.Placement)
            {
                items.Add(SceneApplyItemResolver.Resolve(
                    plan,
                    lookup,
                    selectedSource,
                    occupancy: null,
                    childContext.OperationId,
                    childContext.CorrelationId));
                continue;
            }

            SceneExactSlotInspection inspection = await InspectExactSlotAsync(
                plan,
                source,
                childContext,
                cancellationToken).ConfigureAwait(false);
            if (inspection.IsBlocked)
            {
                items.Add(SceneApplyItemPreview.BlockedBeforeOccupancy(
                    plan,
                    source,
                    inspection.Reason,
                    childContext.OperationId,
                    childContext.CorrelationId));
                continue;
            }

            SceneSlotOccupancy occupancy = inspection.Occupancy
                ?? throw new InvalidOperationException(
                    "A successful exact-slot inspection requires occupancy evidence.");
            items.Add(SceneApplyItemResolver.Resolve(
                plan,
                lookup,
                selectedSource,
                occupancy,
                childContext.OperationId,
                childContext.CorrelationId));
        }

        return SceneApplyPreview.Create(
            scene,
            identities.ParentOperationId,
            identities.ParentCorrelationId,
            createdAt,
            expiresAt,
            items.MoveToImmutable(),
            observedGroupRevision);
    }

    private static void ValidateObservedGroupRevision(
        ScenePlan scene,
        long? observedGroupRevision)
    {
        if (observedGroupRevision is null)
        {
            return;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(
            observedGroupRevision.Value,
            1);
        if (scene.GroupBinding is null)
        {
            throw new ArgumentException(
                "An observed Group revision requires a Group-derived Scene.",
                nameof(observedGroupRevision));
        }
    }

    private static ImmutableDictionary<int, SceneSourceSelection>
        CopySelections(
            ScenePlan scene,
            IEnumerable<SceneSourceSelection> selectedSources)
    {
        ArgumentNullException.ThrowIfNull(selectedSources);
        var selections =
            ImmutableDictionary.CreateBuilder<int, SceneSourceSelection>();
        foreach (SceneSourceSelection selection in selectedSources)
        {
            if (selection is null)
            {
                throw new ArgumentException(
                    "Scene source selections must be non-null.",
                    nameof(selectedSources));
            }

            if (selection.Index >= scene.Activities.Length
                || selection.ActivityId
                    != scene.Activities[selection.Index].ActivityId
                || !selections.TryAdd(selection.Index, selection))
            {
                throw new ArgumentException(
                    "Scene source selections must be unique and match saved Scene items.",
                    nameof(selectedSources));
            }
        }

        return selections.ToImmutable();
    }

    private SceneApplyIdentities CreateIdentities(int itemCount)
    {
        OperationId parentOperationId = idSource.CreateOperationId()
            ?? throw new InvalidOperationException(
                "The Scene operation ID source returned null.");
        CorrelationId parentCorrelationId = idSource.CreateCorrelationId()
            ?? throw new InvalidOperationException(
                "The Scene correlation ID source returned null.");
        var operationIds = new HashSet<OperationId> { parentOperationId };
        var correlationIds = new HashSet<CorrelationId> { parentCorrelationId };
        var childOperationIds = ImmutableArray.CreateBuilder<OperationId>(
            itemCount);
        var childCorrelationIds = ImmutableArray.CreateBuilder<CorrelationId>(
            itemCount);
        for (int index = 0; index < itemCount; index++)
        {
            OperationId operationId = idSource.CreateOperationId()
                ?? throw new InvalidOperationException(
                    "The Scene operation ID source returned null.");
            CorrelationId correlationId = idSource.CreateCorrelationId()
                ?? throw new InvalidOperationException(
                    "The Scene correlation ID source returned null.");
            if (!operationIds.Add(operationId)
                || !correlationIds.Add(correlationId))
            {
                throw new InvalidOperationException(
                    "The Scene ID source returned a duplicate identity.");
            }

            childOperationIds.Add(operationId);
            childCorrelationIds.Add(correlationId);
        }

        return new SceneApplyIdentities(
            parentOperationId,
            parentCorrelationId,
            childOperationIds.MoveToImmutable(),
            childCorrelationIds.MoveToImmutable());
    }

    private async ValueTask<SceneSourceLookup> LocateSourcesAsync(
        SceneActivityPlan plan,
        int index,
        OperationContext childContext,
        CancellationToken cancellationToken)
    {
        try
        {
            return await preflight.LocateSourcesAsync(
                plan.ActivityId,
                index,
                childContext,
                cancellationToken).ConfigureAwait(false)
                ?? SceneSourceLookup.Unavailable(
                    index,
                    plan.ActivityId,
                    SceneApplyItemReason.SourceLookupUnavailable);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return SceneSourceLookup.Unavailable(
                index,
                plan.ActivityId,
                SceneApplyItemReason.SourceLookupUnavailable);
        }
    }

    private async ValueTask<SceneExactSlotInspection> InspectExactSlotAsync(
        SceneActivityPlan plan,
        SceneSourceSelection source,
        OperationContext childContext,
        CancellationToken cancellationToken)
    {
        try
        {
            return await preflight.InspectExactSlotAsync(
                plan,
                source,
                childContext,
                cancellationToken).ConfigureAwait(false)
                ?? SceneExactSlotInspection.Blocked(
                    SceneApplyItemReason.DestinationUnavailable);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return SceneExactSlotInspection.Blocked(
                SceneApplyItemReason.DestinationUnavailable);
        }
    }

    private sealed record SceneApplyIdentities(
        OperationId ParentOperationId,
        CorrelationId ParentCorrelationId,
        ImmutableArray<OperationId> ChildOperationIds,
        ImmutableArray<CorrelationId> ChildCorrelationIds);
}
