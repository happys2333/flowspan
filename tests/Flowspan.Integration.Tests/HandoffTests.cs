using System.Text.Json;
using Flowspan.Application;
using Flowspan.Application.Adapters;
using Flowspan.Diagnostics;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class HandoffTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandoffResumesTargetAndPreservesSource()
    {
        Fixture fixture = new();
        fixture.AuthorizeReceive();

        OperationReceipt receipt = await fixture.HandoffAsync();

        Assert.True(receipt.IsSuccess);
        Assert.True(fixture.Source.TryGetActivity(fixture.Descriptor.Id, out ActivityInstance? source));
        Assert.Equal(ActivityLifecycle.Active, source.Lifecycle);
        Assert.True(fixture.Target.TryGetActivity(fixture.Descriptor.Id, out ActivityInstance? target));
        Assert.Equal(ActivityLifecycle.Active, target.Lifecycle);
        Assert.Equal(fixture.Target.DeviceId, target.Placement.DeviceId);
        Assert.Equal(1, fixture.Adapter.CallCount);
    }

    [Fact]
    public async Task MissingCapabilityFailsBeforeAdapterOrTargetMutation()
    {
        Fixture fixture = new();

        OperationReceipt receipt = await fixture.HandoffAsync();

        Assert.False(receipt.IsSuccess);
        Assert.Equal(FailureCode.CapabilityDenied, receipt.FailureCode);
        Assert.Equal(0, fixture.Adapter.CallCount);
        Assert.False(fixture.Target.TryGetActivity(fixture.Descriptor.Id, out _));
        Assert.True(fixture.Source.TryGetActivity(fixture.Descriptor.Id, out _));
    }

    [Fact]
    public async Task RetryReturnsRecordedResultWithoutResumingTwice()
    {
        Fixture fixture = new();
        fixture.AuthorizeReceive();

        OperationReceipt first = await fixture.HandoffAsync();
        OperationReceipt replay = await fixture.HandoffAsync();

        Assert.Same(first, replay);
        Assert.Equal(1, fixture.Adapter.CallCount);
        Assert.Equal(1, fixture.TargetCatalog.Count);
        Assert.Equal(1, fixture.TargetReceipts.Count);
    }

    [Fact]
    public async Task ReusingOperationIdForDifferentRequestIsRejected()
    {
        Fixture fixture = new();
        fixture.AuthorizeReceive();

        OperationReceipt first = await fixture.HandoffAsync("main");
        OperationReceipt conflict = await fixture.HandoffAsync("secondary");

        Assert.True(first.IsSuccess);
        Assert.False(conflict.IsSuccess);
        Assert.Equal(FailureCode.OperationIdConflict, conflict.FailureCode);
        Assert.Equal(1, fixture.Adapter.CallCount);
    }

    [Fact]
    public async Task InvalidAdapterPayloadDoesNotMutateTarget()
    {
        Fixture fixture = new(payloadJson: "{\"unexpected\":true}");
        fixture.AuthorizeReceive();

        OperationReceipt receipt = await fixture.HandoffAsync();

        Assert.False(receipt.IsSuccess);
        Assert.Equal(FailureCode.DescriptorRejected, receipt.FailureCode);
        Assert.False(fixture.Target.TryGetActivity(fixture.Descriptor.Id, out _));
        Assert.True(fixture.Source.TryGetActivity(fixture.Descriptor.Id, out _));
    }

    [Fact]
    public async Task ReceiptSerializationDoesNotContainDescriptorPayload()
    {
        const string canary = "FLOWSPAN_SUPER_SECRET_CANARY";
        Fixture fixture = new(payloadJson: JsonSerializer.Serialize(new { text = canary }));
        fixture.AuthorizeReceive();

        OperationReceipt receipt = await fixture.HandoffAsync();
        string exported = ReceiptJson.Serialize(receipt);

        Assert.DoesNotContain(canary, exported, StringComparison.Ordinal);
        Assert.Contains(fixture.Descriptor.DescriptorDigest, exported, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpiredRequestIsRejectedBeforeAdapterUse()
    {
        Fixture fixture = new(deadline: Now);
        fixture.AuthorizeReceive();

        OperationReceipt receipt = await fixture.HandoffAsync();

        Assert.Equal(FailureCode.DeadlineExpired, receipt.FailureCode);
        Assert.Equal(0, fixture.Adapter.CallCount);
    }

    private sealed class Fixture
    {
        private static readonly DeviceId SourceId =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");

        private static readonly DeviceId TargetId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");

        private readonly OperationContext context;

        public Fixture(string? payloadJson = null, DateTimeOffset? deadline = null)
        {
            var clock = new TestClock(Now);
            Adapter = new CountingAdapter(new WorkspaceNoteAdapter());
            TargetCatalog = new InMemoryActivityCatalog();
            TargetReceipts = new InMemoryReceiptSink();
            Source = new FlowspanNode(
                SourceId,
                "Source",
                clock,
                new InMemoryActivityCatalog(),
                new InMemoryOperationJournal(),
                new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]),
                new InMemoryReceiptSink());
            Target = new FlowspanNode(
                TargetId,
                "Target",
                clock,
                TargetCatalog,
                new InMemoryOperationJournal(),
                new ActivityAdapterRegistry([Adapter]),
                TargetReceipts);

            Descriptor = ActivityDescriptor.Create(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ActivityKind.Parse("workspace.note/v1"),
                SourceId,
                "Test note",
                payloadJson ?? JsonSerializer.Serialize(new { text = "hello" }));
            Source.AddLocalActivity(ActivityInstance.Active(
                Descriptor,
                ActivityPlacement.On(SourceId)));
            context = OperationContext.Create(
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                deadline ?? Now.AddSeconds(30));
        }

        public FlowspanNode Source { get; }

        public FlowspanNode Target { get; }

        public ActivityDescriptor Descriptor { get; }

        public CountingAdapter Adapter { get; }

        public InMemoryActivityCatalog TargetCatalog { get; }

        public InMemoryReceiptSink TargetReceipts { get; }

        public void AuthorizeReceive() => Target.SetPeerGrant(
            Source.DeviceId,
            CapabilityGrant.Of(Capability.ActivityReceive));

        public ValueTask<OperationReceipt> HandoffAsync(string slot = "main") =>
            Source.HandoffAsync(
                Descriptor.Id,
                new DirectActivityChannel(Target),
                slot,
                context);
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class CountingAdapter(IActivityAdapter inner) : IActivityAdapter
    {
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);

        public ActivityKind Kind => inner.Kind;

        public ValueTask<ResumeActivityResult> ResumeAsync(
            ActivityDescriptor descriptor,
            ActivityPlacement placement,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            return inner.ResumeAsync(descriptor, placement, cancellationToken);
        }

        public ValueTask<CloseActivityResult> CloseAsync(
            ActivityInstance activity,
            CancellationToken cancellationToken) =>
            inner.CloseAsync(activity, cancellationToken);
    }
}
