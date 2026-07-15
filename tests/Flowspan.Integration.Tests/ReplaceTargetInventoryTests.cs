using System.Text.Json;
using Flowspan.Application;
using Flowspan.Application.Adapters;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class ReplaceTargetInventoryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId SourceId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId TargetId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void RejectedInventoryRequiresInitializedCaptureTime()
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReplaceTargetInventoryResult.Rejected(
                SourceId,
                query,
                default,
                FailureCode.CapabilityDenied));
    }

    [Fact]
    public void SuccessfulInventoryRequiresInitializedCaptureTime()
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReplaceTargetInventoryResult.Success(
                SourceId,
                query,
                default,
                [],
                isTruncated: false));
    }

    [Fact]
    public void SuccessfulInventoryRejectsDuplicateTargetIds()
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));
        ReplaceTargetSnapshot target = ReplaceTargetSnapshot.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            revision: 7,
            new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            "Target",
            "desktop");

        Assert.Throws<ArgumentException>(() =>
            ReplaceTargetInventoryResult.Success(
                SourceId,
                query,
                Now,
                [target, target],
                isTruncated: false));
    }

    [Fact]
    public void SuccessfulInventoryRequiresFullPageWhenTruncated()
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));
        ReplaceTargetSnapshot target = ReplaceTargetSnapshot.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            revision: 7,
            new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            "Target",
            "desktop");

        Assert.Throws<ArgumentException>(() =>
            ReplaceTargetInventoryResult.Success(
                SourceId,
                query,
                Now,
                [target],
                isTruncated: true));
    }

    [Fact]
    public void SuccessfulInventoryStopsEnumeratingAfterDetectingOversizedPage()
    {
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReplaceTargetInventoryResult.Success(
                SourceId,
                query,
                Now,
                OversizedTargets(),
                isTruncated: false));

        static IEnumerable<ReplaceTargetSnapshot> OversizedTargets()
        {
            for (int index = 1; index <= 65; index++)
            {
                yield return ReplaceTargetSnapshot.Create(
                    ActivityId.Parse($"00000000-0000-0000-0000-{index:D12}"),
                    revision: 1,
                    new string('A', 64),
                    ActivityKind.Parse("workspace.note/v1"),
                    $"Target {index}",
                    "desktop");
            }

            throw new InvalidOperationException(
                "The bounded model must not enumerate a 66th target.");
        }
    }

    [Fact]
    public void ReplaceTargetSnapshotRejectsControlCharactersInPlacementSlot()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReplaceTargetSnapshot.Create(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                revision: 7,
                new string('A', 64),
                ActivityKind.Parse("workspace.note/v1"),
                "Target",
                "desktop\nforged-status"));
    }

    [Fact]
    public async Task UnauthorizedPeerCannotObserveEvenNormalTargetMetadata()
    {
        var catalog = new InMemoryActivityCatalog();
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            TargetId,
            "Private project title",
            JsonSerializer.Serialize(new { text = "private target payload" }));
        Assert.True(catalog.TryAdd(ActivityInstance.Active(
            descriptor,
            ActivityPlacement.On(TargetId, "desktop"),
            revision: 7)));
        var endpoint = new ReplaceTargetInventoryEndpoint(
            TargetId,
            new FixedClock(Now),
            catalog,
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]));
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));

        ReplaceTargetInventoryResult result = await endpoint.QueryAsync(
            SourceId,
            query,
            CancellationToken.None);

        Assert.Equal(FailureCode.CapabilityDenied, result.FailureCode);
        Assert.Empty(result.Targets);
    }

    [Fact]
    public async Task ExpiredInventoryQueryRejectsWithoutTargetMetadata()
    {
        var catalog = new InMemoryActivityCatalog();
        Assert.True(catalog.TryAdd(CreateActivity(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "Expired query target")));
        var endpoint = new ReplaceTargetInventoryEndpoint(
            TargetId,
            new FixedClock(Now),
            catalog,
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]));
        endpoint.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivityReplace));
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Now);

        ReplaceTargetInventoryResult result = await endpoint.QueryAsync(
            SourceId,
            query,
            CancellationToken.None);

        Assert.Equal(FailureCode.DeadlineExpired, result.FailureCode);
        Assert.Empty(result.Targets);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public async Task UnsupportedIncomingKindRejectsWithoutTargetMetadata()
    {
        var catalog = new InMemoryActivityCatalog();
        Assert.True(catalog.TryAdd(CreateActivity(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "Supported target")));
        var endpoint = new ReplaceTargetInventoryEndpoint(
            TargetId,
            new FixedClock(Now),
            catalog,
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]));
        endpoint.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivityReplace));
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetId,
            ActivityKind.Parse("workspace.unsupported/v1"),
            Now.AddSeconds(30));

        ReplaceTargetInventoryResult result = await endpoint.QueryAsync(
            SourceId,
            query,
            CancellationToken.None);

        Assert.Equal(FailureCode.AdapterUnavailable, result.FailureCode);
        Assert.Empty(result.Targets);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public async Task IncomingKindWithoutReplaceAdapterRejectsWithoutTargetMetadata()
    {
        ActivityKind handoffOnlyKind = ActivityKind.Parse("workspace.handoff-only/v1");
        var endpoint = new ReplaceTargetInventoryEndpoint(
            TargetId,
            new FixedClock(Now),
            new InMemoryActivityCatalog(),
            new ActivityAdapterRegistry(
            [
                new InventoryOnlyActivityAdapter(handoffOnlyKind),
            ]));
        endpoint.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivityReplace));
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetId,
            handoffOnlyKind,
            Now.AddSeconds(30));

        ReplaceTargetInventoryResult result = await endpoint.QueryAsync(
            SourceId,
            query,
            CancellationToken.None);

        Assert.Equal(FailureCode.AdapterUnavailable, result.FailureCode);
        Assert.Empty(result.Targets);
    }

    [Fact]
    public async Task InventoryUsesOneClockSampleForDecisionAndCapture()
    {
        var clock = new SequenceClock(Now, Now.AddMinutes(1));
        var endpoint = new ReplaceTargetInventoryEndpoint(
            TargetId,
            clock,
            new InMemoryActivityCatalog(),
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]));
        endpoint.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivityReplace));
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));

        ReplaceTargetInventoryResult result = await endpoint.QueryAsync(
            SourceId,
            query,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, result.CapturedAt);
        Assert.Equal(1, clock.ReadCount);
    }

    [Fact]
    public async Task AuthorizedInventoryProjectsExactTargetBindingWithoutPayload()
    {
        var catalog = new InMemoryActivityCatalog();
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Replaceable note",
            JsonSerializer.Serialize(new { text = "TARGET-PAYLOAD-CANARY" }));
        Assert.True(catalog.TryAdd(ActivityInstance.Active(
            descriptor,
            ActivityPlacement.On(TargetId, "desk-one"),
            revision: 7)));
        var endpoint = new ReplaceTargetInventoryEndpoint(
            TargetId,
            new FixedClock(Now),
            catalog,
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]));
        endpoint.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivityReplace));
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));

        ReplaceTargetInventoryResult result = await endpoint.QueryAsync(
            SourceId,
            query,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        ReplaceTargetSnapshot target = Assert.Single(result.Targets);
        Assert.Equal(descriptor.Id, target.ActivityId);
        Assert.Equal(7, target.Revision);
        Assert.Equal(descriptor.DescriptorDigest, target.DescriptorDigest);
        Assert.Equal(descriptor.Kind, target.Kind);
        Assert.Equal(descriptor.Title, target.Title);
        Assert.Equal("desk-one", target.PlacementSlot);
        string serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("TARGET-PAYLOAD-CANARY", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(descriptor.PayloadDigest, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(descriptor.OriginDeviceId.ToString(), serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorizedInventoryOmitsEveryIneligibleTargetClass()
    {
        var catalog = new InMemoryActivityCatalog();
        ActivityInstance eligible = CreateActivity(
            "10000000-0000-0000-0000-000000000000",
            "Eligible target");
        ActivityInstance sensitive = CreateActivity(
            "20000000-0000-0000-0000-000000000000",
            "Sensitive target",
            ActivitySensitivity.Sensitive);
        ActivityInstance restricted = CreateActivity(
            "30000000-0000-0000-0000-000000000000",
            "Restricted target",
            ActivitySensitivity.Restricted);
        ActivityInstance closed = CreateActivity(
            "40000000-0000-0000-0000-000000000000",
            "Closed target").Close();
        ActivityInstance nonLocal = CreateActivity(
            "50000000-0000-0000-0000-000000000000",
            "Other device target",
            placementDeviceId: SourceId);
        ActivityInstance unsupported = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("60000000-0000-0000-0000-000000000000"),
                ActivityKind.Parse("workspace.unsupported/v1"),
                TargetId,
                "Unsupported target",
                JsonSerializer.Serialize(new { text = "unsupported" })),
            ActivityPlacement.On(TargetId, "desktop"));
        foreach (ActivityInstance activity in new[]
                 {
                     eligible,
                     sensitive,
                     restricted,
                     closed,
                     nonLocal,
                     unsupported,
                 })
        {
            Assert.True(catalog.TryAdd(activity));
        }

        var endpoint = new ReplaceTargetInventoryEndpoint(
            TargetId,
            new FixedClock(Now),
            catalog,
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]));
        endpoint.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivityReplace));
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));

        ReplaceTargetInventoryResult result = await endpoint.QueryAsync(
            SourceId,
            query,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(eligible.Descriptor.Id, Assert.Single(result.Targets).ActivityId);
    }

    [Fact]
    public async Task AuthorizedInventoryOmitsTargetsIncompatibleWithIncomingKind()
    {
        ActivityKind otherKind = ActivityKind.Parse("workspace.other/v1");
        var catalog = new InMemoryActivityCatalog();
        ActivityInstance compatible = CreateActivity(
            "10000000-0000-0000-0000-000000000000",
            "Compatible target");
        ActivityInstance incompatible = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("20000000-0000-0000-0000-000000000000"),
                otherKind,
                TargetId,
                "Incompatible target",
                JsonSerializer.Serialize(new { value = "other" })),
            ActivityPlacement.On(TargetId, "desktop"));
        Assert.True(catalog.TryAdd(compatible));
        Assert.True(catalog.TryAdd(incompatible));
        var endpoint = new ReplaceTargetInventoryEndpoint(
            TargetId,
            new FixedClock(Now),
            catalog,
            new ActivityAdapterRegistry(
            [
                new WorkspaceNoteAdapter(),
                new InventoryOnlyReplaceAdapter(otherKind),
            ]));
        endpoint.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivityReplace));
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));

        ReplaceTargetInventoryResult result = await endpoint.QueryAsync(
            SourceId,
            query,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(compatible.Descriptor.Id, Assert.Single(result.Targets).ActivityId);
    }

    [Fact]
    public async Task AuthorizedInventoryIsCanonicallyOrderedBoundedAndExplicitlyTruncated()
    {
        var catalog = new InMemoryActivityCatalog();
        foreach (int index in Enumerable.Range(1, 66).Reverse())
        {
            Assert.True(catalog.TryAdd(CreateActivity(
                $"00000000-0000-0000-0000-{index:D12}",
                $"Target {index}")));
        }

        var endpoint = new ReplaceTargetInventoryEndpoint(
            TargetId,
            new FixedClock(Now),
            catalog,
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]));
        endpoint.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivityReplace));
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TargetId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));

        ReplaceTargetInventoryResult result = await endpoint.QueryAsync(
            SourceId,
            query,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsTruncated);
        Assert.Equal(ReplaceTargetInventoryResult.MaximumTargets, result.Targets.Length);
        Assert.Equal(
            Enumerable.Range(1, ReplaceTargetInventoryResult.MaximumTargets)
                .Select(index => $"00000000-0000-0000-0000-{index:D12}"),
            result.Targets.Select(target => target.ActivityId.ToString()));
    }

    private static ActivityInstance CreateActivity(
        string activityId,
        string title,
        ActivitySensitivity sensitivity = ActivitySensitivity.Normal,
        DeviceId? placementDeviceId = null) => ActivityInstance.Active(
        ActivityDescriptor.Create(
            ActivityId.Parse(activityId),
            ActivityKind.Parse("workspace.note/v1"),
            TargetId,
            title,
            JsonSerializer.Serialize(new { text = title }),
            sensitivity),
        ActivityPlacement.On(placementDeviceId ?? TargetId, "desktop"));

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class SequenceClock(params DateTimeOffset[] values) : IClock
    {
        private int reads;

        public int ReadCount => Volatile.Read(ref reads);

        public DateTimeOffset UtcNow
        {
            get
            {
                int index = Interlocked.Increment(ref reads) - 1;
                return values[Math.Min(index, values.Length - 1)];
            }
        }
    }

    private sealed class InventoryOnlyReplaceAdapter(ActivityKind kind) :
        IReplaceActivityAdapter
    {
        public ActivityKind Kind { get; } = kind;

        public ValueTask<ResumeActivityResult> ResumeAsync(
            ActivityDescriptor descriptor,
            ActivityPlacement placement,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inventory must not invoke the Adapter.");

        public ValueTask<CloseActivityResult> CloseAsync(
            ActivityInstance activity,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inventory must not invoke the Adapter.");

        public ValueTask<CaptureUndoResult> CaptureUndoAsync(
            ActivityInstance activity,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inventory must not invoke the Adapter.");

        public ValueTask<RestoreActivityResult> RestoreAsync(
            UndoCapsule capsule,
            ActivityPlacement placement,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inventory must not invoke the Adapter.");
    }

    private sealed class InventoryOnlyActivityAdapter(ActivityKind kind) :
        IActivityAdapter
    {
        public ActivityKind Kind { get; } = kind;

        public ValueTask<ResumeActivityResult> ResumeAsync(
            ActivityDescriptor descriptor,
            ActivityPlacement placement,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inventory must not invoke the Adapter.");

        public ValueTask<CloseActivityResult> CloseAsync(
            ActivityInstance activity,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inventory must not invoke the Adapter.");
    }
}
