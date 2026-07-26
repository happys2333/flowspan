using Flowspan.Application;
using Flowspan.Application.Adapters;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class SceneApplyPreflightEndpointTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 5, 0, 0, TimeSpan.Zero);
    private static readonly ActivityKind Kind =
        ActivityKind.Parse("workspace.note/v1");
    private static readonly DeviceId Coordinator =
        DeviceId.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly DeviceId SourceA =
        DeviceId.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly DeviceId SourceB =
        DeviceId.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly DeviceId Target =
        DeviceId.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly ActivityId Incoming =
        ActivityId.Parse("40000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task ExactIdAggregationRequiresExplicitSelection()
    {
        SceneApplyPreflightEndpoint first = Endpoint(
            SourceA,
            [Activity(Incoming, SourceA, "source-a", revision: 2)]);
        SceneApplyPreflightEndpoint second = Endpoint(
            SourceB,
            [Activity(Incoming, SourceB, "source-b", revision: 1)]);
        Grant(first);
        Grant(second);
        var port = new DirectSceneApplyPreflightPort(
            Coordinator,
            [second, first]);

        SceneSourceLookup lookup = await port.LocateSourcesAsync(
            Incoming,
            0,
            Context(),
            CancellationToken.None);

        Assert.Equal(
            SceneSourceLookupStatus.SelectionRequired,
            lookup.Status);
        Assert.Equal(2, lookup.Candidates.Length);
        Assert.Equal(SourceA, lookup.Candidates[0].DeviceId);
        Assert.Equal(SourceB, lookup.Candidates[1].DeviceId);
        Assert.DoesNotContain(
            "source-a",
            lookup.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnyDeniedPeerDiscardsPartialSourceEvidence()
    {
        SceneApplyPreflightEndpoint allowed = Endpoint(
            SourceA,
            [Activity(Incoming, SourceA, "source-a")]);
        SceneApplyPreflightEndpoint denied = Endpoint(SourceB, []);
        Grant(allowed);
        var port = new DirectSceneApplyPreflightPort(
            Coordinator,
            [allowed, denied]);

        SceneSourceLookup lookup = await port.LocateSourcesAsync(
            Incoming,
            0,
            Context(),
            CancellationToken.None);

        Assert.Equal(SceneSourceLookupStatus.Unavailable, lookup.Status);
        Assert.Equal(SceneApplyItemReason.CapabilityDenied, lookup.Reason);
        Assert.Empty(lookup.Candidates);
    }

    [Fact]
    public async Task SensitiveSourceIsNeverDisclosed()
    {
        SceneApplyPreflightEndpoint endpoint = Endpoint(
            SourceA,
            [
                Activity(
                    Incoming,
                    SourceA,
                    "sensitive-source-slot-canary",
                    sensitivity: ActivitySensitivity.Sensitive),
            ]);
        Grant(endpoint);

        SceneSourceLookup lookup = await endpoint.LocateSourceAsync(
            Coordinator,
            Incoming,
            0,
            Context(),
            CancellationToken.None);

        Assert.Equal(SceneSourceLookupStatus.NotFound, lookup.Status);
        Assert.Empty(lookup.Candidates);
    }

    [Fact]
    public async Task ExactSlotDoesNotInferOccupancyFromAnotherSlot()
    {
        ActivityId unrelated =
            ActivityId.Parse("40000000-0000-0000-0000-000000000002");
        var availability = new RecordingUndoAvailability(isAvailable: true);
        SceneApplyPreflightEndpoint endpoint = Endpoint(
            Target,
            [Activity(unrelated, Target, "another-slot")],
            availability);
        Grant(endpoint);

        SceneExactSlotInspection inspection =
            await endpoint.InspectExactSlotAsync(
                Coordinator,
                Plan("requested-slot"),
                Source(),
                Context(),
                CancellationToken.None);

        Assert.Equal(
            SceneSlotOccupancyKind.Empty,
            inspection.Occupancy!.Kind);
        Assert.Equal(0, availability.CallCount);
    }

    [Theory]
    [InlineData(ActivitySensitivity.Sensitive)]
    [InlineData(ActivitySensitivity.Restricted)]
    public async Task ProtectedExactSlotIsOpaqueWithoutMetadata(
        ActivitySensitivity sensitivity)
    {
        ActivityId targetId =
            ActivityId.Parse("40000000-0000-0000-0000-000000000003");
        var availability = new RecordingUndoAvailability(isAvailable: true);
        SceneApplyPreflightEndpoint endpoint = Endpoint(
            Target,
            [Activity(targetId, Target, "requested-slot", sensitivity: sensitivity)],
            availability);
        Grant(endpoint);

        SceneExactSlotInspection inspection =
            await endpoint.InspectExactSlotAsync(
                Coordinator,
                Plan("requested-slot"),
                Source(),
                Context(),
                CancellationToken.None);

        Assert.Equal(
            SceneSlotOccupancyKind.Opaque,
            inspection.Occupancy!.Kind);
        Assert.Null(inspection.Occupancy.Target);
        Assert.Equal(0, availability.CallCount);
    }

    [Fact]
    public async Task MultipleExactSlotOccupantsAreAmbiguous()
    {
        SceneApplyPreflightEndpoint endpoint = Endpoint(
            Target,
            [
                Activity(
                    ActivityId.Parse(
                        "40000000-0000-0000-0000-000000000003"),
                    Target,
                    "requested-slot"),
                Activity(
                    ActivityId.Parse(
                        "40000000-0000-0000-0000-000000000004"),
                    Target,
                    "requested-slot"),
            ]);
        Grant(endpoint);

        SceneExactSlotInspection inspection =
            await endpoint.InspectExactSlotAsync(
                Coordinator,
                Plan("requested-slot"),
                Source(),
                Context(),
                CancellationToken.None);

        Assert.Equal(
            SceneSlotOccupancyKind.Ambiguous,
            inspection.Occupancy!.Kind);
        Assert.Null(inspection.Occupancy.Target);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IncompatibleOrUnsupportedOccupantIsOpaque(
        bool incompatibleKind)
    {
        ActivityKind targetKind = incompatibleKind
            ? ActivityKind.Parse("workspace.other/v1")
            : Kind;
        ActivityInstance target = Activity(
            ActivityId.Parse("40000000-0000-0000-0000-000000000003"),
            Target,
            "requested-slot",
            kind: targetKind);
        var catalog = new InMemoryActivityCatalog();
        Assert.True(catalog.TryAdd(target));
        var availability = new RecordingUndoAvailability(isAvailable: true);
        var endpoint = new SceneApplyPreflightEndpoint(
            Target,
            new FixedClock(Now),
            catalog,
            new ActivityAdapterRegistry(
                incompatibleKind ? [new WorkspaceNoteAdapter()] : []),
            availability);
        Grant(endpoint);

        SceneExactSlotInspection inspection =
            await endpoint.InspectExactSlotAsync(
                Coordinator,
                Plan("requested-slot"),
                Source(),
                Context(),
                CancellationToken.None);

        Assert.Equal(
            SceneSlotOccupancyKind.Opaque,
            inspection.Occupancy!.Kind);
        Assert.Null(inspection.Occupancy.Target);
        Assert.Equal(0, availability.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EligibleConflictCarriesOnlyExactPayloadFreeEvidence(
        bool hasDurableUndo)
    {
        ActivityId targetId =
            ActivityId.Parse("40000000-0000-0000-0000-000000000003");
        ActivityInstance target =
            Activity(targetId, Target, "requested-slot", revision: 7);
        var availability =
            new RecordingUndoAvailability(hasDurableUndo);
        SceneApplyPreflightEndpoint endpoint = Endpoint(
            Target,
            [target],
            availability);
        Grant(endpoint);

        SceneExactSlotInspection inspection =
            await endpoint.InspectExactSlotAsync(
                Coordinator,
                Plan("requested-slot"),
                Source(),
                Context(),
                CancellationToken.None);

        Assert.Equal(
            SceneSlotOccupancyKind.EligibleConflict,
            inspection.Occupancy!.Kind);
        Assert.Equal(hasDurableUndo, inspection.Occupancy.HasDurableUndoAvailability);
        Assert.Equal(targetId, inspection.Occupancy.Target!.ActivityId);
        Assert.Equal(7, inspection.Occupancy.Target.Revision);
        Assert.Equal(1, availability.CallCount);
        Assert.DoesNotContain(
            "target-title-canary",
            inspection.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevokedSceneCapabilityBlocksEachUse()
    {
        SceneApplyPreflightEndpoint endpoint = Endpoint(Target, []);
        Grant(endpoint);
        var port = new DirectSceneApplyPreflightPort(
            Coordinator,
            [endpoint]);
        endpoint.SetPeerGrant(Coordinator, CapabilityGrant.None);

        SceneSourceLookup source = await port.LocateSourcesAsync(
            Incoming,
            0,
            Context(),
            CancellationToken.None);
        SceneExactSlotInspection slot = await port.InspectExactSlotAsync(
            Plan("requested-slot"),
            Source(),
            Context(),
            CancellationToken.None);

        Assert.Equal(SceneApplyItemReason.CapabilityDenied, source.Reason);
        Assert.Equal(SceneApplyItemReason.CapabilityDenied, slot.Reason);
    }

    [Fact]
    public async Task MissingDestinationAndExpiredRequestFailClosed()
    {
        SceneApplyPreflightEndpoint sourceEndpoint = Endpoint(SourceA, []);
        Grant(sourceEndpoint);
        var port = new DirectSceneApplyPreflightPort(
            Coordinator,
            [sourceEndpoint]);

        SceneExactSlotInspection missing = await port.InspectExactSlotAsync(
            Plan("requested-slot"),
            Source(),
            Context(),
            CancellationToken.None);
        SceneSourceLookup expired = await sourceEndpoint.LocateSourceAsync(
            Coordinator,
            Incoming,
            0,
            Context(Now),
            CancellationToken.None);

        Assert.Equal(
            SceneApplyItemReason.DestinationUnavailable,
            missing.Reason);
        Assert.Equal(
            SceneApplyItemReason.SourceLookupUnavailable,
            expired.Reason);
    }

    private static SceneApplyPreflightEndpoint Endpoint(
        DeviceId deviceId,
        IEnumerable<ActivityInstance> activities,
        ISceneReplaceUndoAvailability? availability = null)
    {
        var catalog = new InMemoryActivityCatalog();
        foreach (ActivityInstance activity in activities)
        {
            Assert.True(catalog.TryAdd(activity));
        }

        return new SceneApplyPreflightEndpoint(
            deviceId,
            new FixedClock(Now),
            catalog,
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]),
            availability ?? new RecordingUndoAvailability(isAvailable: true));
    }

    private static void Grant(SceneApplyPreflightEndpoint endpoint) =>
        endpoint.SetPeerGrant(
            Coordinator,
            CapabilityGrant.Of(Capability.SceneApply));

    private static ActivityInstance Activity(
        ActivityId id,
        DeviceId deviceId,
        string slot,
        long revision = 1,
        ActivitySensitivity sensitivity = ActivitySensitivity.Normal,
        ActivityKind? kind = null)
    {
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            id,
            kind ?? Kind,
            deviceId,
            "target-title-canary",
            "{\"text\":\"target-payload-canary\"}",
            sensitivity);
        return ActivityInstance.Active(
            descriptor,
            ActivityPlacement.On(deviceId, slot),
            revision);
    }

    private static SceneActivityPlan Plan(string slot) =>
        SceneActivityPlan.Place(
            Incoming,
            ActivityPlacement.On(Target, slot),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.ReplaceWithUndo);

    private static SceneSourceSelection Source() =>
        SceneSourceSelection.Create(
            0,
            Incoming,
            1,
            new string('A', 64),
            Kind,
            ActivityPlacement.On(SourceA, "source"));

    private static OperationContext Context(
        DateTimeOffset? deadline = null) =>
        OperationContext.Create(
            OperationId.Parse(
                "50000000-0000-0000-0000-000000000001"),
            CorrelationId.Parse(
                "60000000-0000-0000-0000-000000000001"),
            deadline ?? Now.AddMinutes(5));

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class RecordingUndoAvailability(bool isAvailable) :
        ISceneReplaceUndoAvailability
    {
        public int CallCount { get; private set; }

        public bool HasDurableUndoFor(ActivityInstance target)
        {
            ArgumentNullException.ThrowIfNull(target);
            CallCount++;
            return isAvailable;
        }
    }
}
