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
        fixture.AuthorizeOffer();

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
        fixture.AuthorizeOffer();
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
        fixture.AuthorizeOffer();
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
        fixture.AuthorizeOffer();
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
    public async Task GeneratedDeliverySequencesPreserveMoveSafetyAndIdempotency()
    {
        ActivityDeliveryFault[] faults = Enum.GetValues<ActivityDeliveryFault>();
        foreach (ActivityDeliveryFault first in faults)
        {
            foreach (ActivityDeliveryFault second in faults)
            {
                foreach (ActivityDeliveryFault third in faults)
                {
                    ActivityDeliveryFault[] sequence =
                        [first, second, third, ActivityDeliveryFault.None];
                    string faultTrace =
                        $"Move fault trace [{string.Join(", ", sequence)}]";
                    try
                    {
                        Fixture fixture = new();
                        fixture.AuthorizeOffer();
                        var channel = new DeterministicActivityChannel(
                            fixture.Target,
                            sequence);
                        OperationReceipt? terminal = null;

                        for (int attempt = 0; attempt < sequence.Length; attempt++)
                        {
                            string attemptTrace = Describe(sequence, attempt, null);
                            OperationReceipt receipt = await ExecuteWithTraceAsync(
                                attemptTrace,
                                () => fixture.MoveAsync(channel));
                            AssertMoveInvariant(fixture, sequence, attempt, receipt);
                            if (receipt.Status is OperationStatus.Committed
                                or OperationStatus.CommittedWithWarning)
                            {
                                terminal = receipt;
                                break;
                            }
                        }

                        Assert.True(
                            terminal is { Status: OperationStatus.Committed },
                            Describe(sequence, sequence.Length, terminal));
                        string trace = Describe(sequence, sequence.Length, terminal);
                        Assert.True(fixture.TargetAdapter.ResumeCount == 1, trace);
                        Assert.True(fixture.SourceAdapter.CloseCount == 1, trace);
                    }
                    catch (Exception exception) when (!exception.Message.Contains(
                        faultTrace,
                        StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"{faultTrace}, unexpected generated-case exception.",
                            exception);
                    }
                }
            }
        }
    }

    [Fact]
    public async Task SourceCloseFailureIsCommittedWithWarning()
    {
        Fixture fixture = new(sourceCloseSucceeds: false);
        fixture.AuthorizeOffer();

        OperationReceipt receipt = await fixture.MoveAsync(
            new DirectActivityChannel(fixture.Target));

        Assert.True(receipt.IsSuccess);
        Assert.Equal(OperationStatus.CommittedWithWarning, receipt.Status);
        Assert.Equal(FailureCode.SourceCleanupFailed, receipt.FailureCode);
        AssertSourceIsActive(fixture);
        Assert.True(fixture.Target.TryGetActivity(fixture.Descriptor.Id, out _));
    }

    [Fact]
    public async Task AmbiguousCoordinatorJournalFailureReplaysMoveWithoutDuplicateWork()
    {
        var failure = new IOException("Injected failure after coordinator result.");
        Fixture fixture = new(
            sourceJournal: new ThrowAfterFirstResultOperationJournal(failure));
        fixture.AuthorizeOffer();
        var channel = new DirectActivityChannel(fixture.Target);

        IOException thrown = await Assert.ThrowsAsync<IOException>(
            async () => await fixture.MoveAsync(channel));

        Assert.Same(failure, thrown);
        Assert.Equal(1, fixture.TargetAdapter.ResumeCount);
        Assert.Equal(1, fixture.SourceAdapter.CloseCount);
        Assert.True(fixture.Source.TryGetActivity(
            fixture.Descriptor.Id,
            out ActivityInstance? source));
        Assert.Equal(ActivityLifecycle.Closed, source.Lifecycle);

        OperationReceipt replay = await fixture.MoveAsync(channel);

        Assert.Equal(OperationStatus.Committed, replay.Status);
        Assert.Equal(1, fixture.TargetAdapter.ResumeCount);
        Assert.Equal(1, fixture.SourceAdapter.CloseCount);
    }

    private static async ValueTask<T> ExecuteWithTraceAsync<T>(
        string trace,
        Func<ValueTask<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{trace} Unexpected generated-operation exception.",
                exception);
        }
    }

    private static void AssertSourceIsActive(Fixture fixture)
    {
        Assert.True(fixture.Source.TryGetActivity(fixture.Descriptor.Id, out ActivityInstance? source));
        Assert.Equal(ActivityLifecycle.Active, source.Lifecycle);
    }

    private static void AssertMoveInvariant(
        Fixture fixture,
        IReadOnlyList<ActivityDeliveryFault> sequence,
        int attempt,
        OperationReceipt receipt)
    {
        string trace = Describe(sequence, attempt, receipt);
        Assert.True(fixture.Source.TryGetActivity(
            fixture.Descriptor.Id,
            out ActivityInstance? source),
            trace);
        bool targetExists = fixture.Target.TryGetActivity(
            fixture.Descriptor.Id,
            out ActivityInstance? target);
        Assert.True(
            source.Lifecycle != ActivityLifecycle.Closed
            || targetExists && target?.Lifecycle == ActivityLifecycle.Active,
            trace);
        Assert.True(fixture.TargetAdapter.ResumeCount is >= 0 and <= 1, trace);
        Assert.True(fixture.TargetCatalog.Count is >= 0 and <= 1, trace);
        if (receipt.Status is OperationStatus.Failed or OperationStatus.Recovering)
        {
            Assert.True(source.Lifecycle == ActivityLifecycle.Active, trace);
        }
    }

    private static string Describe(
        IReadOnlyList<ActivityDeliveryFault> sequence,
        int attempt,
        OperationReceipt? receipt) =>
        $"Move fault trace [{string.Join(", ", sequence)}], attempt {attempt}, status {receipt?.Status}.";

    private sealed class Fixture
    {
        private static readonly DateTimeOffset Now =
            new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

        private readonly OperationContext context;

        public Fixture(
            bool sourceCloseSucceeds = true,
            IOperationJournal? sourceJournal = null)
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
                sourceJournal ?? new InMemoryOperationJournal(),
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

        public void AuthorizeOffer() => Target.SetPeerGrant(
            Source.DeviceId,
            CapabilityGrant.Of(Capability.ActivityOffer));

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
