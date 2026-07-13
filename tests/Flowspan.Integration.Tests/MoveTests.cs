using System.Text.Json;
using Flowspan.Application;
using Flowspan.Diagnostics;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class MoveTests
{
    [Fact]
    public async Task TargetResumePrecedesSourceClose()
    {
        Fixture fixture = new();
        fixture.AuthorizeReceive();

        OperationReceipt receipt = await fixture.MoveAsync(
            new DirectActivityChannel(fixture.Target));

        Assert.True(receipt.IsSuccess);
        Assert.Equal(OperationKind.Move, receipt.Kind);
        Assert.Equal(["target.resume", "source.close"], fixture.Events);
        Assert.True(fixture.Source.TryGetActivity(fixture.Descriptor.Id, out ActivityInstance? source));
        Assert.Equal(ActivityLifecycle.Closed, source.Lifecycle);
        Assert.True(fixture.Target.TryGetActivity(fixture.Descriptor.Id, out ActivityInstance? target));
        Assert.Equal(ActivityLifecycle.Active, target.Lifecycle);
    }

    [Fact]
    public async Task TargetRejectionLeavesSourceActive()
    {
        Fixture fixture = new();

        OperationReceipt receipt = await fixture.MoveAsync(
            new DirectActivityChannel(fixture.Target));

        Assert.Equal(FailureCode.CapabilityDenied, receipt.FailureCode);
        Assert.Empty(fixture.Events);
        AssertSourceIsActive(fixture);
        Assert.False(fixture.Target.TryGetActivity(fixture.Descriptor.Id, out _));
    }

    [Fact]
    public async Task LostAcknowledgementRecoversWithoutDuplicateResume()
    {
        Fixture fixture = new();
        fixture.AuthorizeReceive();
        var channel = new DeterministicActivityChannel(
            fixture.Target,
            [ActivityDeliveryFault.DropAcknowledgement, ActivityDeliveryFault.None]);

        OperationReceipt uncertain = await fixture.MoveAsync(channel);

        Assert.Equal(OperationStatus.Recovering, uncertain.Status);
        Assert.Equal(FailureCode.AcknowledgementLost, uncertain.FailureCode);
        AssertSourceIsActive(fixture);
        Assert.True(fixture.Target.TryGetActivity(fixture.Descriptor.Id, out _));
        Assert.Equal(1, fixture.TargetAdapter.ResumeCount);
        Assert.Equal(0, fixture.SourceAdapter.CloseCount);

        OperationReceipt recovered = await fixture.MoveAsync(channel);

        Assert.Equal(OperationStatus.Committed, recovered.Status);
        Assert.Equal(1, fixture.TargetAdapter.ResumeCount);
        Assert.Equal(1, fixture.SourceAdapter.CloseCount);
        Assert.True(fixture.Source.TryGetActivity(fixture.Descriptor.Id, out ActivityInstance? source));
        Assert.Equal(ActivityLifecycle.Closed, source.Lifecycle);
    }

    [Fact]
    public async Task DeliveryFailureIsRetryableAndPreservesSource()
    {
        Fixture fixture = new();
        fixture.AuthorizeReceive();
        var channel = new DeterministicActivityChannel(
            fixture.Target,
            [ActivityDeliveryFault.DropBeforeDelivery, ActivityDeliveryFault.None]);

        OperationReceipt failed = await fixture.MoveAsync(channel);

        Assert.Equal(OperationStatus.Failed, failed.Status);
        Assert.Equal(FailureCode.PeerUnavailable, failed.FailureCode);
        AssertSourceIsActive(fixture);
        Assert.Equal(0, fixture.TargetAdapter.ResumeCount);

        OperationReceipt retried = await fixture.MoveAsync(channel);

        Assert.Equal(OperationStatus.Committed, retried.Status);
        Assert.Equal(1, fixture.TargetAdapter.ResumeCount);
        Assert.Equal(1, fixture.SourceAdapter.CloseCount);
    }

    [Fact]
    public async Task DuplicateDeliveryIsIdempotentAtTarget()
    {
        Fixture fixture = new();
        fixture.AuthorizeReceive();
        var channel = new DeterministicActivityChannel(
            fixture.Target,
            [ActivityDeliveryFault.DuplicateDelivery]);

        OperationReceipt receipt = await fixture.MoveAsync(channel);

        Assert.Equal(OperationStatus.Committed, receipt.Status);
        Assert.Equal(1, fixture.TargetAdapter.ResumeCount);
        Assert.Equal(1, fixture.TargetCatalog.Count);
        Assert.Equal(1, fixture.SourceAdapter.CloseCount);
    }

    [Fact]
    public async Task SourceCloseFailureIsCommittedWithWarning()
    {
        Fixture fixture = new(sourceCloseSucceeds: false);
        fixture.AuthorizeReceive();

        OperationReceipt receipt = await fixture.MoveAsync(
            new DirectActivityChannel(fixture.Target));

        Assert.True(receipt.IsSuccess);
        Assert.Equal(OperationStatus.CommittedWithWarning, receipt.Status);
        Assert.Equal(FailureCode.SourceCleanupFailed, receipt.FailureCode);
        AssertSourceIsActive(fixture);
        Assert.True(fixture.Target.TryGetActivity(fixture.Descriptor.Id, out _));
    }

    private static void AssertSourceIsActive(Fixture fixture)
    {
        Assert.True(fixture.Source.TryGetActivity(fixture.Descriptor.Id, out ActivityInstance? source));
        Assert.Equal(ActivityLifecycle.Active, source.Lifecycle);
    }

    private sealed class Fixture
    {
        private static readonly DateTimeOffset Now =
            new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

        private readonly OperationContext context;

        public Fixture(bool sourceCloseSucceeds = true)
        {
            var clock = new TestClock(Now);
            Events = [];
            SourceAdapter = new RecordingAdapter(
                "source",
                Events,
                closeSucceeds: sourceCloseSucceeds);
            TargetAdapter = new RecordingAdapter("target", Events);
            TargetCatalog = new InMemoryActivityCatalog();

            DeviceId sourceId =
                DeviceId.Parse("11111111-1111-1111-1111-111111111111");
            DeviceId targetId =
                DeviceId.Parse("22222222-2222-2222-2222-222222222222");
            Source = new FlowspanNode(
                sourceId,
                "Source",
                clock,
                new InMemoryActivityCatalog(),
                new InMemoryOperationJournal(),
                new ActivityAdapterRegistry([SourceAdapter]),
                new InMemoryReceiptSink());
            Target = new FlowspanNode(
                targetId,
                "Target",
                clock,
                TargetCatalog,
                new InMemoryOperationJournal(),
                new ActivityAdapterRegistry([TargetAdapter]),
                new InMemoryReceiptSink());

            Descriptor = ActivityDescriptor.Create(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ActivityKind.Parse("workspace.note/v1"),
                sourceId,
                "Move test",
                JsonSerializer.Serialize(new { text = "move me" }));
            Source.AddLocalActivity(ActivityInstance.Active(
                Descriptor,
                ActivityPlacement.On(sourceId)));
            context = OperationContext.Create(
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Now.AddSeconds(30));
        }

        public List<string> Events { get; }

        public FlowspanNode Source { get; }

        public FlowspanNode Target { get; }

        public ActivityDescriptor Descriptor { get; }

        public RecordingAdapter SourceAdapter { get; }

        public RecordingAdapter TargetAdapter { get; }

        public InMemoryActivityCatalog TargetCatalog { get; }

        public void AuthorizeReceive() => Target.SetPeerGrant(
            Source.DeviceId,
            CapabilityGrant.Of(Capability.ActivityReceive));

        public ValueTask<OperationReceipt> MoveAsync(IActivityChannel channel) =>
            Source.MoveAsync(Descriptor.Id, channel, "main", context);
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class RecordingAdapter(
        string name,
        List<string> events,
        bool closeSucceeds = true) : IActivityAdapter
    {
        private int closeCount;
        private int resumeCount;

        public int CloseCount => Volatile.Read(ref closeCount);

        public int ResumeCount => Volatile.Read(ref resumeCount);

        public ActivityKind Kind { get; } = ActivityKind.Parse("workspace.note/v1");

        public ValueTask<ResumeActivityResult> ResumeAsync(
            ActivityDescriptor descriptor,
            ActivityPlacement placement,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref resumeCount);
            events.Add($"{name}.resume");
            return ValueTask.FromResult(ResumeActivityResult.Success);
        }

        public ValueTask<CloseActivityResult> CloseAsync(
            ActivityInstance activity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref closeCount);
            events.Add($"{name}.close");
            return ValueTask.FromResult(
                closeSucceeds
                    ? CloseActivityResult.Success
                    : CloseActivityResult.Failed(FailureCode.SourceCleanupFailed));
        }
    }
}
