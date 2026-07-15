using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Application.Adapters;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class ReplaceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CaptureFailureRejectsBeforeIncomingResumeOrTargetMutation()
    {
        Fixture fixture = new(captureSucceeds: false);
        fixture.AuthorizeReplace();

        ReplaceOperationResult result = await fixture.ReplaceAsync();

        Assert.Equal(OperationStatus.Rejected, result.Receipt.Status);
        Assert.Equal(FailureCode.UndoUnavailable, result.Receipt.FailureCode);
        Assert.Null(result.UndoCapsule);
        Assert.Equal(1, fixture.Adapter.CaptureCount);
        Assert.Equal(0, fixture.Adapter.ResumeCount);
        Assert.Equal(0, fixture.UndoCapsules.Count);
        Assert.True(fixture.Catalog.TryGet(
            fixture.Original.Descriptor.Id,
            out ActivityInstance? preserved));
        Assert.Same(fixture.Original, preserved);
        Assert.False(fixture.Catalog.TryGet(fixture.Incoming.Id, out _));
    }

    [Fact]
    public async Task SuccessfulReplaceCapturesBoundUndoBeforeIncomingResume()
    {
        Fixture fixture = new(captureSucceeds: true);
        fixture.AuthorizeReplace();

        ReplaceOperationResult result = await fixture.ReplaceAsync();

        Assert.Equal(OperationStatus.Committed, result.Receipt.Status);
        Assert.Equal(OperationKind.Replace, result.Receipt.Kind);
        Assert.Equal(["capture", "resume"], fixture.Adapter.Events);
        UndoCapsuleReference capsule = Assert.IsType<UndoCapsuleReference>(result.UndoCapsule);
        Assert.Equal(
            OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            capsule.OperationId);
        Assert.Equal(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            capsule.CorrelationId);
        Assert.Equal(fixture.Original.Descriptor.Id, capsule.TargetActivityId);
        Assert.Equal(fixture.Original.Revision, capsule.ExpectedTargetRevision);
        Assert.Equal(
            fixture.Original.Descriptor.DescriptorDigest,
            capsule.TargetDescriptorDigest);
        Assert.Equal(fixture.Incoming.DescriptorDigest, capsule.IncomingDescriptorDigest);
        Assert.Equal(Now.AddMinutes(10), capsule.ExpiresAt);
        Assert.Equal(1, fixture.UndoCapsules.Count);
        Assert.False(fixture.Catalog.TryGet(fixture.Original.Descriptor.Id, out _));
        Assert.True(fixture.Catalog.TryGet(
            fixture.Incoming.Id,
            out ActivityInstance? replacement));
        Assert.Equal(8, replacement.Revision);
        Assert.Equal(ActivityLifecycle.Active, replacement.Lifecycle);
    }

    [Fact]
    public async Task SuccessfulUndoRestoresOriginalAsANewRevision()
    {
        Fixture fixture = new(captureSucceeds: true);
        fixture.AuthorizeReplace();
        ReplaceOperationResult replaced = await fixture.ReplaceAsync();
        UndoCapsuleReference capsule = Assert.IsType<UndoCapsuleReference>(replaced.UndoCapsule);

        UndoReplaceResult undone = await fixture.Endpoint.UndoReplaceAsync(
            capsule.Id,
            OperationContext.Create(
                OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
                Now.AddSeconds(30)));

        Assert.Equal(OperationStatus.Committed, undone.Status);
        Assert.Equal(FailureCode.None, undone.FailureCode);
        Assert.Equal(["capture", "resume", "restore"], fixture.Adapter.Events);
        Assert.Equal(1, fixture.Adapter.RestoreCount);
        Assert.False(fixture.Catalog.TryGet(fixture.Incoming.Id, out _));
        Assert.True(fixture.Catalog.TryGet(
            fixture.Original.Descriptor.Id,
            out ActivityInstance? restored));
        Assert.Equal(fixture.Original.Descriptor, restored.Descriptor);
        Assert.Equal(9, restored.Revision);
        Assert.Equal(ActivityLifecycle.Active, restored.Lifecycle);
    }

    [Fact]
    public async Task UndoRetryReturnsRecordedResultWithoutRestoringTwice()
    {
        Fixture fixture = new(captureSucceeds: true);
        fixture.AuthorizeReplace();
        ReplaceOperationResult replaced = await fixture.ReplaceAsync();
        UndoCapsuleReference capsule = Assert.IsType<UndoCapsuleReference>(replaced.UndoCapsule);
        OperationContext context = OperationContext.Create(
            OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
            Now.AddSeconds(30));

        UndoReplaceResult first = await fixture.Endpoint.UndoReplaceAsync(
            capsule.Id,
            context);
        UndoReplaceResult replay = await fixture.Endpoint.UndoReplaceAsync(
            capsule.Id,
            context);

        Assert.Same(first, replay);
        Assert.Equal(OperationStatus.Committed, replay.Status);
        Assert.Equal(1, fixture.Adapter.RestoreCount);
        Assert.True(fixture.Catalog.TryGet(
            fixture.Original.Descriptor.Id,
            out ActivityInstance? restored));
        Assert.Equal(9, restored.Revision);
    }

    [Fact]
    public async Task ConsumedCapsuleRejectsDifferentUndoOperation()
    {
        Fixture fixture = new(captureSucceeds: true);
        fixture.AuthorizeReplace();
        ReplaceOperationResult replaced = await fixture.ReplaceAsync();
        UndoCapsuleReference capsule = Assert.IsType<UndoCapsuleReference>(replaced.UndoCapsule);
        await fixture.Endpoint.UndoReplaceAsync(
            capsule.Id,
            OperationContext.Create(
                OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
                Now.AddSeconds(30)));

        UndoReplaceResult second = await fixture.Endpoint.UndoReplaceAsync(
            capsule.Id,
            OperationContext.Create(
                OperationId.Parse("12121212-1212-1212-1212-121212121212"),
                CorrelationId.Parse("34343434-3434-3434-3434-343434343434"),
                Now.AddSeconds(30)));

        Assert.Equal(OperationStatus.Rejected, second.Status);
        Assert.Equal(FailureCode.UndoCapsuleConsumed, second.FailureCode);
        Assert.Equal(1, fixture.Adapter.RestoreCount);
    }

    [Fact]
    public async Task ExpiredCapsuleRejectsBeforeRestoreAndPreservesReplacement()
    {
        Fixture fixture = new(captureSucceeds: true);
        fixture.AuthorizeReplace();
        ReplaceOperationResult replaced = await fixture.ReplaceAsync();
        UndoCapsuleReference capsule = Assert.IsType<UndoCapsuleReference>(replaced.UndoCapsule);
        fixture.Clock.UtcNow = capsule.ExpiresAt;

        UndoReplaceResult result = await fixture.Endpoint.UndoReplaceAsync(
            capsule.Id,
            OperationContext.Create(
                OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
                Now.AddMinutes(20)));

        Assert.Equal(OperationStatus.Rejected, result.Status);
        Assert.Equal(FailureCode.UndoCapsuleExpired, result.FailureCode);
        Assert.Equal(0, fixture.Adapter.RestoreCount);
        Assert.True(fixture.Catalog.TryGet(fixture.Incoming.Id, out _));
        Assert.False(fixture.Catalog.TryGet(fixture.Original.Descriptor.Id, out _));
    }

    [Fact]
    public async Task RevisionConflictRejectsBeforeCaptureOrMutation()
    {
        Fixture fixture = new(captureSucceeds: true, expectedRevision: 6);
        fixture.AuthorizeReplace();

        ReplaceOperationResult result = await fixture.ReplaceAsync();

        Assert.Equal(OperationStatus.Rejected, result.Receipt.Status);
        Assert.Equal(FailureCode.RevisionConflict, result.Receipt.FailureCode);
        Assert.Equal(0, fixture.Adapter.CaptureCount);
        Assert.Equal(0, fixture.Adapter.ResumeCount);
        Assert.Equal(0, fixture.UndoCapsules.Count);
        Assert.True(fixture.Catalog.TryGet(
            fixture.Original.Descriptor.Id,
            out ActivityInstance? preserved));
        Assert.Same(fixture.Original, preserved);
    }

    [Fact]
    public async Task MismatchedCapturedStateRejectsBeforeIncomingResume()
    {
        Fixture fixture = new(captureSucceeds: true, capturedStateMatches: false);
        fixture.AuthorizeReplace();

        ReplaceOperationResult result = await fixture.ReplaceAsync();

        Assert.Equal(OperationStatus.Rejected, result.Receipt.Status);
        Assert.Equal(FailureCode.UndoCapsuleInvalid, result.Receipt.FailureCode);
        Assert.Equal(1, fixture.Adapter.CaptureCount);
        Assert.Equal(0, fixture.Adapter.ResumeCount);
        Assert.Equal(0, fixture.UndoCapsules.Count);
        Assert.True(fixture.Catalog.TryGet(fixture.Original.Descriptor.Id, out _));
    }

    [Fact]
    public async Task WorkspaceNoteAdapterSupportsBoundedReplaceAndUndo()
    {
        DeviceId sourceId =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId targetId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        var catalog = new InMemoryActivityCatalog();
        ActivityInstance original = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ActivityKind.Parse("workspace.note/v1"),
                targetId,
                "Original note",
                JsonSerializer.Serialize(new { text = "keep me" })),
            ActivityPlacement.On(targetId, "main"),
            revision: 7);
        ActivityDescriptor incoming = ActivityDescriptor.Create(
            ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ActivityKind.Parse("workspace.note/v1"),
            sourceId,
            "Incoming note",
            JsonSerializer.Serialize(new { text = "replace with me" }));
        Assert.True(catalog.TryAdd(original));
        using var endpoint = new ReplaceEndpoint(
            targetId,
            new TestClock(Now),
            catalog,
            new InMemoryOperationJournal(),
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]),
            new InMemoryUndoCapsuleStore(),
            new DeterministicUndoCapsuleIdSource(
            [
                UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            ]),
            NullReceiptSink.Instance);
        endpoint.SetPeerGrant(
            sourceId,
            CapabilityGrant.Of(Capability.ActivityReplace));

        ReplaceOperationResult replaced = await endpoint.ReplaceAsync(
            sourceId,
            ReplaceActivityCommand.Create(
                OperationContext.Create(
                    OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    Now.AddSeconds(30)),
                original.Descriptor.Id,
                original.Revision,
                original.Descriptor.DescriptorDigest,
                incoming,
                ActivityPlacement.On(targetId, "main"),
                Now.AddMinutes(10)));
        UndoCapsuleReference capsule = Assert.IsType<UndoCapsuleReference>(replaced.UndoCapsule);
        UndoReplaceResult undone = await endpoint.UndoReplaceAsync(
            capsule.Id,
            OperationContext.Create(
                OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
                Now.AddSeconds(30)));

        Assert.Equal(OperationStatus.Committed, replaced.Receipt.Status);
        Assert.Equal(OperationStatus.Committed, undone.Status);
        Assert.True(catalog.TryGet(original.Descriptor.Id, out ActivityInstance? restored));
        Assert.Equal(original.Descriptor, restored.Descriptor);
        Assert.Equal(9, restored.Revision);
    }

    [Fact]
    public async Task ReplaceRetryReturnsRecordedReceiptAndCapsuleWithoutRepeatingWork()
    {
        Fixture fixture = new(captureSucceeds: true);
        fixture.AuthorizeReplace();

        ReplaceOperationResult first = await fixture.ReplaceAsync();
        ReplaceOperationResult replay = await fixture.ReplaceAsync();

        Assert.Same(first.Receipt, replay.Receipt);
        Assert.Same(first.UndoCapsule, replay.UndoCapsule);
        Assert.Equal(OperationStatus.Committed, replay.Receipt.Status);
        Assert.Equal(1, fixture.Adapter.CaptureCount);
        Assert.Equal(1, fixture.Adapter.ResumeCount);
        Assert.Equal(1, fixture.UndoCapsules.Count);
        Assert.Equal(1, fixture.Catalog.Count);
    }

    [Fact]
    public async Task CapsuleStoreFailureBlocksBeforeIncomingResume()
    {
        Fixture fixture = new(
            captureSucceeds: true,
            undoCapsuleStore: new RejectingUndoCapsuleStore());
        fixture.AuthorizeReplace();

        ReplaceOperationResult result = await fixture.ReplaceAsync();

        Assert.Equal(OperationStatus.Rejected, result.Receipt.Status);
        Assert.Equal(FailureCode.UndoUnavailable, result.Receipt.FailureCode);
        Assert.Null(result.UndoCapsule);
        Assert.Equal(1, fixture.Adapter.CaptureCount);
        Assert.Equal(0, fixture.Adapter.ResumeCount);
        Assert.True(fixture.Catalog.TryGet(
            fixture.Original.Descriptor.Id,
            out ActivityInstance? preserved));
        Assert.Same(fixture.Original, preserved);
    }

    [Fact]
    public async Task TargetDescriptorMismatchRejectsBeforeCaptureOrMutation()
    {
        Fixture fixture = new(
            captureSucceeds: true,
            expectedTargetDescriptorDigest: new string('A', 64));
        fixture.AuthorizeReplace();

        ReplaceOperationResult result = await fixture.ReplaceAsync();

        Assert.Equal(OperationStatus.Rejected, result.Receipt.Status);
        Assert.Equal(FailureCode.RevisionConflict, result.Receipt.FailureCode);
        Assert.Equal(0, fixture.Adapter.CaptureCount);
        Assert.Equal(0, fixture.Adapter.ResumeCount);
        Assert.True(fixture.Catalog.TryGet(
            fixture.Original.Descriptor.Id,
            out ActivityInstance? preserved));
        Assert.Same(fixture.Original, preserved);
    }

    [Fact]
    public async Task ActivityOfferCapabilityDoesNotAuthorizeReplace()
    {
        Fixture fixture = new(captureSucceeds: true);
        fixture.Endpoint.SetPeerGrant(
            Fixture.SourceId,
            CapabilityGrant.Of(Capability.ActivityOffer));

        ReplaceOperationResult result = await fixture.ReplaceAsync();

        Assert.Equal(OperationStatus.Rejected, result.Receipt.Status);
        Assert.Equal(FailureCode.CapabilityDenied, result.Receipt.FailureCode);
        Assert.Equal(0, fixture.Adapter.CaptureCount);
        Assert.Equal(0, fixture.Adapter.ResumeCount);
        Assert.Equal(0, fixture.UndoCapsules.Count);
        Assert.True(fixture.Catalog.TryGet(fixture.Original.Descriptor.Id, out _));
    }

    private sealed class Fixture
    {
        public static readonly DeviceId SourceId =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");

        private static readonly DeviceId TargetId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");

        private readonly ReplaceActivityCommand command;

        public Fixture(
            bool captureSucceeds,
            long? expectedRevision = null,
            bool capturedStateMatches = true,
            IUndoCapsuleStore? undoCapsuleStore = null,
            string? expectedTargetDescriptorDigest = null)
        {
            Clock = new TestClock(Now);
            Catalog = new InMemoryActivityCatalog();
            UndoCapsules = new InMemoryUndoCapsuleStore();
            Adapter = new RecordingReplaceAdapter(captureSucceeds, capturedStateMatches);
            Endpoint = new ReplaceEndpoint(
                TargetId,
                Clock,
                Catalog,
                new InMemoryOperationJournal(),
                new ActivityAdapterRegistry([Adapter]),
                undoCapsuleStore ?? UndoCapsules,
                new DeterministicUndoCapsuleIdSource(
                [
                    UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                ]),
                NullReceiptSink.Instance);

            Original = ActivityInstance.Active(
                CreateDescriptor(
                    "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                    TargetId,
                    "Original note",
                    "keep me"),
                ActivityPlacement.On(TargetId, "main"),
                revision: 7);
            Incoming = CreateDescriptor(
                "dddddddd-dddd-dddd-dddd-dddddddddddd",
                SourceId,
                "Incoming note",
                "replace with me");
            Assert.True(Catalog.TryAdd(Original));

            command = ReplaceActivityCommand.Create(
                OperationContext.Create(
                    OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    Now.AddSeconds(30)),
                Original.Descriptor.Id,
                expectedRevision ?? Original.Revision,
                expectedTargetDescriptorDigest ?? Original.Descriptor.DescriptorDigest,
                Incoming,
                ActivityPlacement.On(TargetId, "main"),
                Now.AddMinutes(10));
        }

        public InMemoryActivityCatalog Catalog { get; }

        public TestClock Clock { get; }

        public InMemoryUndoCapsuleStore UndoCapsules { get; }

        public RecordingReplaceAdapter Adapter { get; }

        public ReplaceEndpoint Endpoint { get; }

        public ActivityInstance Original { get; }

        public ActivityDescriptor Incoming { get; }

        public void AuthorizeReplace() => Endpoint.SetPeerGrant(
            SourceId,
            CapabilityGrant.Of(Capability.ActivityReplace));

        public ValueTask<ReplaceOperationResult> ReplaceAsync() =>
            Endpoint.ReplaceAsync(SourceId, command);

        private static ActivityDescriptor CreateDescriptor(
            string activityId,
            DeviceId originDeviceId,
            string title,
            string text) => ActivityDescriptor.Create(
                ActivityId.Parse(activityId),
                ActivityKind.Parse("workspace.note/v1"),
                originDeviceId,
                title,
                JsonSerializer.Serialize(new { text }));
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class RejectingUndoCapsuleStore : IUndoCapsuleStore
    {
        public bool TryAdd(UndoCapsule capsule)
        {
            ArgumentNullException.ThrowIfNull(capsule);
            return false;
        }

        public bool TryGet(
            UndoCapsuleId capsuleId,
            [NotNullWhen(true)] out UndoCapsule? capsule)
        {
            ArgumentNullException.ThrowIfNull(capsuleId);
            capsule = null;
            return false;
        }

        public bool TryGetByOperation(
            OperationId operationId,
            [NotNullWhen(true)] out UndoCapsule? capsule)
        {
            ArgumentNullException.ThrowIfNull(operationId);
            capsule = null;
            return false;
        }

        public bool TryRemove(UndoCapsuleId capsuleId)
        {
            ArgumentNullException.ThrowIfNull(capsuleId);
            return false;
        }
    }

    private sealed class RecordingReplaceAdapter(
        bool captureSucceeds,
        bool capturedStateMatches) :
        IReplaceActivityAdapter
    {
        private int captureCount;
        private int restoreCount;
        private int resumeCount;

        public int CaptureCount => Volatile.Read(ref captureCount);

        public int ResumeCount => Volatile.Read(ref resumeCount);

        public int RestoreCount => Volatile.Read(ref restoreCount);

        public List<string> Events { get; } = [];

        public ActivityKind Kind { get; } = ActivityKind.Parse("workspace.note/v1");

        public ValueTask<CaptureUndoResult> CaptureUndoAsync(
            ActivityInstance activity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref captureCount);
            Events.Add("capture");
            ActivityDescriptor preserved = capturedStateMatches
                ? activity.Descriptor
                : ActivityDescriptor.Create(
                    ActivityId.Parse("abababab-abab-abab-abab-abababababab"),
                    activity.Descriptor.Kind,
                    activity.Descriptor.OriginDeviceId,
                    activity.Descriptor.Title,
                    activity.Descriptor.PayloadJson,
                    activity.Descriptor.Sensitivity);
            return ValueTask.FromResult(
                captureSucceeds
                    ? CaptureUndoResult.Success(preserved)
                    : CaptureUndoResult.Rejected(FailureCode.UndoUnavailable));
        }

        public ValueTask<ResumeActivityResult> ResumeAsync(
            ActivityDescriptor descriptor,
            ActivityPlacement placement,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref resumeCount);
            Events.Add("resume");
            return ValueTask.FromResult(ResumeActivityResult.Success);
        }

        public ValueTask<CloseActivityResult> CloseAsync(
            ActivityInstance activity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CloseActivityResult.Success);
        }

        public ValueTask<RestoreActivityResult> RestoreAsync(
            UndoCapsule capsule,
            ActivityPlacement placement,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref restoreCount);
            Events.Add("restore");
            return ValueTask.FromResult(RestoreActivityResult.Success);
        }
    }
}
