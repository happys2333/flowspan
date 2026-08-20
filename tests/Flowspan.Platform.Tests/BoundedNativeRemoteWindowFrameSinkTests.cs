using System.Buffers;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;

namespace Flowspan.Platform.Tests;

public sealed class BoundedNativeRemoteWindowFrameSinkTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 8, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId Host =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task ExactBoundFrameTransfersToDestination()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        var destination = new RecordingFrameSink();
        using var sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            destination);
        (NativeRemoteWindowFrame frame, RecordingMemoryOwner owner) =
            CreateFrame(expected, sequence: 1);

        sink.TakeOwnership(expected, frame);

        Assert.Equal([1L], destination.Sequences);
        Assert.Equal(1, owner.DisposeCount);
        Assert.False(sink.IsClosed);
    }

    [Fact]
    public async Task DifferentOpaqueSourceIsRejectedWhenNumericGenerationsMatch()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        NativeRemoteWindowSourceUse differentSource =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        Assert.Equal(expected.OwnerGeneration, differentSource.OwnerGeneration);
        Assert.Equal(expected.SessionGeneration, differentSource.SessionGeneration);
        Assert.Equal(expected.SourceGeneration, differentSource.SourceGeneration);
        Assert.Equal(expected.GeometryRevision, differentSource.GeometryRevision);
        var destination = new RecordingFrameSink();
        using var sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            destination);
        (NativeRemoteWindowFrame frame, RecordingMemoryOwner owner) =
            CreateFrame(differentSource, sequence: 1);

        sink.TakeOwnership(differentSource, frame);

        Assert.Empty(destination.Sequences);
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public async Task PreviousSessionFrameIsRejectedForReplacementBinding()
    {
        IReadOnlyList<NativeRemoteWindowSourceUse> uses =
            await CreateSourceUsesAsync(Host, sessionCount: 2);
        NativeRemoteWindowSourceUse previous = uses[0];
        NativeRemoteWindowSourceUse current = uses[1];
        Assert.Equal(previous.ActivityId, current.ActivityId);
        Assert.Equal(previous.SourceGeneration, current.SourceGeneration);
        Assert.NotEqual(previous.SessionGeneration, current.SessionGeneration);
        var destination = new RecordingFrameSink();
        using var sink = new BoundedNativeRemoteWindowFrameSink(
            current,
            static () => true,
            destination);
        (NativeRemoteWindowFrame frame, RecordingMemoryOwner owner) =
            CreateFrame(previous, sequence: 1);

        sink.TakeOwnership(previous, frame);

        Assert.Empty(destination.Sequences);
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public async Task NonAdvancingSequenceIsRejectedAndDisposed()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        var destination = new RecordingFrameSink();
        using var sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            destination);
        (NativeRemoteWindowFrame newer, RecordingMemoryOwner newerOwner) =
            CreateFrame(expected, sequence: 2);
        (NativeRemoteWindowFrame older, RecordingMemoryOwner olderOwner) =
            CreateFrame(expected, sequence: 1);

        sink.TakeOwnership(expected, newer);
        sink.TakeOwnership(expected, older);

        Assert.Equal([2L], destination.Sequences);
        Assert.Equal(1, newerOwner.DisposeCount);
        Assert.Equal(1, olderOwner.DisposeCount);
    }

    [Fact]
    public async Task FullHandoffKeepsLatestFrameAndDisposesReplacedFrame()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        using var destination = new BlockingFirstFrameSink();
        using var sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            destination);
        (NativeRemoteWindowFrame first, RecordingMemoryOwner firstOwner) =
            CreateFrame(expected, sequence: 1);
        (NativeRemoteWindowFrame replaced, RecordingMemoryOwner replacedOwner) =
            CreateFrame(expected, sequence: 2);
        (NativeRemoteWindowFrame latest, RecordingMemoryOwner latestOwner) =
            CreateFrame(expected, sequence: 3);
        Task firstHandoff = RunOnDedicatedThread(
            () => sink.TakeOwnership(expected, first));
        Assert.True(destination.FirstFrameEntered.Wait(TimeSpan.FromSeconds(5)));

        sink.TakeOwnership(expected, replaced);
        sink.TakeOwnership(expected, latest);

        Assert.Equal(1, replacedOwner.DisposeCount);
        Assert.Equal(0, latestOwner.DisposeCount);
        destination.ReleaseFirstFrame();
        await firstHandoff.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([1L, 3L], destination.Sequences);
        Assert.Equal(1, firstOwner.DisposeCount);
        Assert.Equal(1, latestOwner.DisposeCount);
    }

    [Fact]
    public async Task DestinationFailureDisposesFrameAndClosesHandoff()
    {
        const string exceptionCanary = "FLOWSPAN-NATIVE-SINK-EXCEPTION-CANARY";
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        using var sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            new ThrowingFrameSink(exceptionCanary));
        (NativeRemoteWindowFrame failed, RecordingMemoryOwner failedOwner) =
            CreateFrame(expected, sequence: 1);
        (NativeRemoteWindowFrame late, RecordingMemoryOwner lateOwner) =
            CreateFrame(expected, sequence: 2);

        sink.TakeOwnership(expected, failed);
        sink.TakeOwnership(expected, late);

        Assert.True(sink.IsClosed);
        Assert.Equal(1, failedOwner.DisposeCount);
        Assert.Equal(1, lateOwner.DisposeCount);
        Assert.DoesNotContain(exceptionCanary, sink.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeliveryPolicyDropsFramesWithoutClosingExactBinding()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        int deliveryAllowed = 0;
        var destination = new RecordingFrameSink();
        var faults = new List<NativeRemoteWindowFrameSinkFault>();
        using var sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            () => Volatile.Read(ref deliveryAllowed) != 0,
            destination,
            faults.Add);
        (NativeRemoteWindowFrame blocked, RecordingMemoryOwner blockedOwner) =
            CreateFrame(expected, sequence: 1);

        sink.TakeOwnership(expected, blocked);
        Volatile.Write(ref deliveryAllowed, 1);
        (NativeRemoteWindowFrame allowed, RecordingMemoryOwner allowedOwner) =
            CreateFrame(expected, sequence: 2);
        sink.TakeOwnership(expected, allowed);

        Assert.False(sink.IsClosed);
        Assert.Empty(faults);
        Assert.Equal([2L], destination.Sequences);
        Assert.Equal(1, blockedOwner.DisposeCount);
        Assert.Equal(1, allowedOwner.DisposeCount);
    }

    [Fact]
    public async Task TerminalDestinationFaultIsReportedExactlyOnce()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        var faults = new List<NativeRemoteWindowFrameSinkFault>();
        using var sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            static () => true,
            new ThrowingFrameSink("FLOWSPAN-NATIVE-SINK-EXCEPTION-CANARY"),
            faults.Add);
        (NativeRemoteWindowFrame failed, RecordingMemoryOwner failedOwner) =
            CreateFrame(expected, sequence: 1);
        (NativeRemoteWindowFrame late, RecordingMemoryOwner lateOwner) =
            CreateFrame(expected, sequence: 2);

        sink.TakeOwnership(expected, failed);
        sink.TakeOwnership(expected, late);

        Assert.Equal(
            [NativeRemoteWindowFrameSinkFault.DestinationFailed],
            faults);
        Assert.Equal(1, failedOwner.DisposeCount);
        Assert.Equal(1, lateOwner.DisposeCount);
    }

    [Fact]
    public async Task QueuedFrameIsRevalidatedAfterSourceBecomesStale()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        int current = 1;
        using var destination = new BlockingFirstFrameSink();
        using var sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            () => Volatile.Read(ref current) != 0,
            destination);
        (NativeRemoteWindowFrame first, RecordingMemoryOwner firstOwner) =
            CreateFrame(expected, sequence: 1);
        (NativeRemoteWindowFrame queued, RecordingMemoryOwner queuedOwner) =
            CreateFrame(expected, sequence: 2);
        Task firstHandoff = RunOnDedicatedThread(
            () => sink.TakeOwnership(expected, first));
        Assert.True(destination.FirstFrameEntered.Wait(TimeSpan.FromSeconds(5)));
        sink.TakeOwnership(expected, queued);

        Volatile.Write(ref current, 0);
        destination.ReleaseFirstFrame();
        await firstHandoff.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(sink.IsClosed);
        Assert.Equal([1L], destination.Sequences);
        Assert.Equal(1, firstOwner.DisposeCount);
        Assert.Equal(1, queuedOwner.DisposeCount);
    }

    [Fact]
    public async Task CurrentPredicateFailureClosesAndDisposesWithoutDelivery()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        var destination = new RecordingFrameSink();
        using var sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => throw new InvalidOperationException(
                "FLOWSPAN-CURRENT-PREDICATE-CANARY"),
            destination);
        (NativeRemoteWindowFrame frame, RecordingMemoryOwner owner) =
            CreateFrame(expected, sequence: 1);

        sink.TakeOwnership(expected, frame);

        Assert.True(sink.IsClosed);
        Assert.Empty(destination.Sequences);
        Assert.Equal(1, owner.DisposeCount);
        Assert.DoesNotContain("CANARY", sink.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CloseNowReturnsBeforeBlockedDeliveryWhileDisposeStillDrains()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        using var destination = new BlockingFirstFrameSink();
        var sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            destination);
        (NativeRemoteWindowFrame first, RecordingMemoryOwner firstOwner) =
            CreateFrame(expected, sequence: 1);
        (NativeRemoteWindowFrame pending, RecordingMemoryOwner pendingOwner) =
            CreateFrame(expected, sequence: 2);
        Task firstHandoff = RunOnDedicatedThread(
            () => sink.TakeOwnership(expected, first));
        Assert.True(destination.FirstFrameEntered.Wait(TimeSpan.FromSeconds(5)));
        sink.TakeOwnership(expected, pending);
        Task immediateClose = RunOnDedicatedThread(sink.CloseNow);

        await immediateClose.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(sink.IsClosed);
        Assert.Equal(1, pendingOwner.DisposeCount);
        (NativeRemoteWindowFrame late, RecordingMemoryOwner lateOwner) =
            CreateFrame(expected, sequence: 3);
        sink.TakeOwnership(expected, late);
        Assert.Equal(1, lateOwner.DisposeCount);
        using var disposeStarted = new ManualResetEventSlim(false);
        using var disposeReturned = new ManualResetEventSlim(false);
        Task disposal = RunOnDedicatedThread(() =>
        {
            disposeStarted.Set();
            sink.Dispose();
            disposeReturned.Set();
        });
        Assert.True(disposeStarted.Wait(TimeSpan.FromSeconds(5)));

        Assert.True(SpinWait.SpinUntil(
            () => sink.DeliveryDrainWaiterCount == 1,
            TimeSpan.FromSeconds(5)));
        Assert.False(disposeReturned.IsSet);
        destination.ReleaseFirstFrame();
        await firstHandoff.WaitAsync(TimeSpan.FromSeconds(5));
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(disposeReturned.IsSet);
        Assert.Equal(1, firstOwner.DisposeCount);
    }

    [Fact]
    public async Task DestinationCanDisposeSinkWithoutDeadlock()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        BoundedNativeRemoteWindowFrameSink? sink = null;
        var destination = new CallbackFrameSink(() => sink!.Dispose());
        sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            destination);
        (NativeRemoteWindowFrame frame, RecordingMemoryOwner owner) =
            CreateFrame(expected, sequence: 1);

        Task handoff = RunOnDedicatedThread(
            () => sink.TakeOwnership(expected, frame));
        await handoff.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(sink.IsClosed);
        Assert.Equal(1, owner.DisposeCount);
        sink.Dispose();
    }

    [Fact]
    public async Task NestedDestinationCanDisposeAncestorSinkWithoutDeadlock()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        BoundedNativeRemoteWindowFrameSink? outer = null;
        using var inner = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            new CallbackFrameSink(() => outer!.Dispose()));
        (NativeRemoteWindowFrame innerFrame, RecordingMemoryOwner innerOwner) =
            CreateFrame(expected, sequence: 2);
        outer = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            new CallbackFrameSink(
                () => inner.TakeOwnership(expected, innerFrame)));
        (NativeRemoteWindowFrame outerFrame, RecordingMemoryOwner outerOwner) =
            CreateFrame(expected, sequence: 1);

        Task handoff = RunOnDedicatedThread(
            () => outer.TakeOwnership(expected, outerFrame));
        await handoff.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(outer.IsClosed);
        Assert.Equal(1, outerOwner.DisposeCount);
        Assert.Equal(1, innerOwner.DisposeCount);
        outer.Dispose();
    }

    [Fact]
    public async Task SymmetricDestinationsCanDisposeEachOtherWithoutDeadlock()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        using var destinationsEntered = new CountdownEvent(2);
        using var releaseCrossDisposals = new ManualResetEventSlim(false);
        using var firstCrossDisposeReturned = new ManualResetEventSlim(false);
        using var secondCrossDisposeReturned = new ManualResetEventSlim(false);
        BoundedNativeRemoteWindowFrameSink? first = null;
        BoundedNativeRemoteWindowFrameSink? second = null;
        first = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            new CallbackFrameSink(() =>
            {
                destinationsEntered.Signal();
                releaseCrossDisposals.Wait();
                second!.Dispose();
                firstCrossDisposeReturned.Set();
            }));
        second = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            new CallbackFrameSink(() =>
            {
                destinationsEntered.Signal();
                releaseCrossDisposals.Wait();
                first.Dispose();
                secondCrossDisposeReturned.Set();
            }));
        (NativeRemoteWindowFrame firstFrame, RecordingMemoryOwner firstOwner) =
            CreateFrame(expected, sequence: 1);
        (NativeRemoteWindowFrame secondFrame, RecordingMemoryOwner secondOwner) =
            CreateFrame(expected, sequence: 1);

        Task firstHandoff = RunOnDedicatedThread(
            () => first.TakeOwnership(expected, firstFrame));
        Task secondHandoff = RunOnDedicatedThread(
            () => second.TakeOwnership(expected, secondFrame));

        bool bothDestinationsEntered =
            destinationsEntered.Wait(TimeSpan.FromSeconds(5));
        releaseCrossDisposals.Set();
        Assert.True(bothDestinationsEntered);
        await Task.WhenAll(firstHandoff, secondHandoff)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(firstCrossDisposeReturned.IsSet);
        Assert.True(secondCrossDisposeReturned.IsSet);
        Assert.True(first.IsClosed);
        Assert.True(second.IsClosed);
        Assert.Equal(1, firstOwner.DisposeCount);
        Assert.Equal(1, secondOwner.DisposeCount);
        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public async Task ProtectionObserverAndDestinationCanDisposeEachOther()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        var protection = new InMemoryNativeProtectionSource(
            ownerGeneration: expected.OwnerGeneration,
            sessionGeneration: expected.SessionGeneration,
            sourceGeneration: expected.SourceGeneration);
        using var callbacksEntered = new CountdownEvent(2);
        using var releaseCrossDisposals = new ManualResetEventSlim(false);
        using var destinationDisposeReturned = new ManualResetEventSlim(false);
        using var observerDisposeReturned = new ManualResetEventSlim(false);
        Exception? destinationFailure = null;
        Exception? observerFailure = null;
        BoundedNativeRemoteWindowFrameSink? sink = null;
        protection.Changed += _ =>
        {
            try
            {
                callbacksEntered.Signal();
                releaseCrossDisposals.Wait();
                sink!.Dispose();
                observerDisposeReturned.Set();
            }
            catch (Exception exception)
            {
                observerFailure = exception;
            }
        };
        sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            new CallbackFrameSink(() =>
            {
                try
                {
                    callbacksEntered.Signal();
                    releaseCrossDisposals.Wait();
                    protection.Dispose();
                    destinationDisposeReturned.Set();
                }
                catch (Exception exception)
                {
                    destinationFailure = exception;
                }
            }));
        (NativeRemoteWindowFrame frame, RecordingMemoryOwner owner) =
            CreateFrame(expected, sequence: 1);

        bool published = false;
        Task publish = RunOnDedicatedThread(
            () => published = protection.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now,
                    "test-probe")));
        Task handoff = RunOnDedicatedThread(
            () => sink.TakeOwnership(expected, frame));

        try
        {
            Assert.True(callbacksEntered.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            releaseCrossDisposals.Set();
        }

        await Task.WhenAll(publish, handoff).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(published);
        Assert.Null(destinationFailure);
        Assert.Null(observerFailure);
        Assert.True(destinationDisposeReturned.IsSet);
        Assert.True(observerDisposeReturned.IsSet);
        Assert.True(sink.IsClosed);
        Assert.Equal(1, owner.DisposeCount);
        sink.Dispose();
        protection.Dispose();
    }

    [Fact]
    public async Task DestinationWorkerCanDisposeSinkWithoutDeadlock()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        BoundedNativeRemoteWindowFrameSink? sink = null;
        var destination = new CallbackFrameSink(
            () => Task.Run(() => sink!.Dispose()).GetAwaiter().GetResult());
        sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            destination);
        (NativeRemoteWindowFrame frame, RecordingMemoryOwner owner) =
            CreateFrame(expected, sequence: 1);

        Task handoff = RunOnDedicatedThread(
            () => sink.TakeOwnership(expected, frame));
        await handoff.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(sink.IsClosed);
        Assert.Equal(1, owner.DisposeCount);
        sink.Dispose();
    }

    [Fact]
    public async Task StaleDestinationContextStillDrainsLaterDelivery()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        using var releaseWorker = new ManualResetEventSlim(false);
        using var workerReturned = new ManualResetEventSlim(false);
        using var firstEntered = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);
        using var secondEntered = new ManualResetEventSlim(false);
        using var releaseSecond = new ManualResetEventSlim(false);
        BoundedNativeRemoteWindowFrameSink? sink = null;
        Task? disposal = null;
        int calls = 0;
        var destination = new CallbackFrameSink(() =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                disposal = Task.Run(() =>
                {
                    releaseWorker.Wait();
                    sink!.Dispose();
                    workerReturned.Set();
                });
                firstEntered.Set();
                releaseFirst.Wait();
                return;
            }

            secondEntered.Set();
            releaseSecond.Wait();
        });
        sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            destination);
        (NativeRemoteWindowFrame first, RecordingMemoryOwner firstOwner) =
            CreateFrame(expected, sequence: 1);
        (NativeRemoteWindowFrame second, RecordingMemoryOwner secondOwner) =
            CreateFrame(expected, sequence: 2);

        Task firstHandoff = RunOnDedicatedThread(
            () => sink.TakeOwnership(expected, first));
        Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(5)));
        sink.TakeOwnership(expected, second);
        releaseFirst.Set();
        Assert.True(secondEntered.Wait(TimeSpan.FromSeconds(5)));

        releaseWorker.Set();
        Assert.True(SpinWait.SpinUntil(
            () => sink.DeliveryDrainWaiterCount == 1,
            TimeSpan.FromSeconds(5)));
        Assert.False(workerReturned.IsSet);
        releaseSecond.Set();
        await firstHandoff.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.IsType<Task>(disposal).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(workerReturned.IsSet);
        Assert.True(sink.IsClosed);
        Assert.Equal(1, firstOwner.DisposeCount);
        Assert.Equal(1, secondOwner.DisposeCount);
        sink.Dispose();
    }

    [Fact]
    public async Task PublicProjectionDoesNotExposeExpectedSourceToken()
    {
        NativeRemoteWindowSourceUse expected =
            Assert.Single(await CreateSourceUsesAsync(Host, sessionCount: 1));
        using var sink = new BoundedNativeRemoteWindowFrameSink(
            expected,
            static () => true,
            new RecordingFrameSink());

        string serialized = JsonSerializer.Serialize(sink);

        Assert.DoesNotContain("Token", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(expected.ActivityId.ToString(), serialized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(expected.HostDeviceId.ToString(), serialized,
            StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<NativeRemoteWindowSourceUse>>
        CreateSourceUsesAsync(DeviceId hostDeviceId, int sessionCount)
    {
        using var registry = new NativeRemoteWindowSourceRegistry(hostDeviceId);
        using NativeRemoteWindowSourceRegistration registration =
            registry.RegisterGeneric(
                NativeRemoteWindowSourceMetadata.Create(
                    "Generic window",
                    "Test application",
                    NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2),
                    supportsCapture: true,
                    supportsInput: true,
                    SafeProtection()));
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
            registry.GetSnapshot());
        Assert.True(
            registry.TryAcquire(
                snapshot.Token,
                snapshot.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredLease));
        using NativeRemoteWindowSourceLease lease = Assert.IsType<
            NativeRemoteWindowSourceLease>(acquiredLease);
        var capture = new RecordingNativeCaptureBoundary();
        using var controller = new RemoteWindowSessionController(
            lease,
            ownerGeneration: 11,
            new FixedClock(),
            new DenyAllAuthorizationSource(),
            capture,
            new NoOpNativeInputBoundary(),
            new RecordingFrameSink(),
            new NoOpSharingSessionBoundary(),
            TimeSpan.FromSeconds(10));

        for (int session = 0; session < sessionCount; session++)
        {
            RemoteWindowCommandResult started =
                await controller.StartAsync(SafeProtection());
            Assert.True(started.Succeeded);
            if (session + 1 < sessionCount)
            {
                Assert.True(controller.EmergencyStop().FullyStopped);
                RemoteWindowCommandResult reset =
                    await controller.ResetAfterLocalConfirmationAsync();
                Assert.True(reset.Succeeded);
            }
        }

        return capture.SourceUses.ToArray();
    }

    private static ProtectionSnapshot SafeProtection() =>
        new(ProtectionKind.Safe, Now, "test-probe");

    private static (NativeRemoteWindowFrame Frame, RecordingMemoryOwner Owner)
        CreateFrame(NativeRemoteWindowSourceUse sourceUse, long sequence)
    {
        var owner = new RecordingMemoryOwner(length: 4);
        NativeRemoteWindowFrame frame = NativeRemoteWindowFrame.TakeOwnership(
            owner,
            payloadLength: 4,
            width: 1,
            height: 1,
            stride: 4,
            NativeRemoteWindowPixelFormat.Bgra8888,
            sourceUse.OwnerGeneration,
            sourceUse.SessionGeneration,
            sourceUse.SourceGeneration,
            sourceUse.GeometryRevision,
            sequence);
        return (frame, owner);
    }

    private static Task RunOnDedicatedThread(Action action) =>
        Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class DenyAllAuthorizationSource : IMirrorAuthorizationSource
    {
        public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId) =>
            CapabilityGrant.None;
    }

    private sealed class RecordingNativeCaptureBoundary :
        INativeRemoteWindowCaptureBoundary
    {
        public List<NativeRemoteWindowSourceUse> SourceUses { get; } = [];

        public ValueTask<LocalBoundaryResult> StartAsync(
            NativeRemoteWindowSourceUse sourceUse,
            INativeRemoteWindowFrameSink frameSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceUses.Add(sourceUse);
            return ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("native_capture_started"));
        }

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("native_capture_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("native_capture_resumed");

        public LocalBoundaryResult EmergencyStopNow() =>
            LocalBoundaryResult.Confirmed("native_capture_emergency_stopped");

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("native_capture_stopped");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpNativeInputBoundary : INativeRemoteInputBoundary
    {
        public ValueTask<LocalBoundaryResult> InjectAsync(
            NativeRemoteWindowSourceUse sourceUse,
            RemoteInputBatch batch,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("native_input_injected"));

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("native_input_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("native_input_resumed");

        public LocalBoundaryResult EmergencyStopNow() =>
            LocalBoundaryResult.Confirmed("native_input_emergency_stopped");

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("native_input_stopped");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpSharingSessionBoundary :
        ILocalSharingSessionBoundary
    {
        public LocalBoundaryResult DisconnectPeerNow(DeviceId peerDeviceId) =>
            LocalBoundaryResult.Confirmed("peer_disconnected");

        public LocalBoundaryResult DisconnectAllNow() =>
            LocalBoundaryResult.Confirmed("sessions_disconnected");
    }

    private sealed class RecordingFrameSink : INativeRemoteWindowFrameSink
    {
        private readonly List<long> sequences = [];

        public IReadOnlyList<long> Sequences
        {
            get
            {
                lock (sequences)
                {
                    return sequences.ToArray();
                }
            }
        }

        public void TakeOwnership(
            NativeRemoteWindowSourceUse sourceUse,
            NativeRemoteWindowFrame frame)
        {
            lock (sequences)
            {
                sequences.Add(frame.Sequence);
            }

            frame.Dispose();
        }
    }

    private sealed class BlockingFirstFrameSink :
        INativeRemoteWindowFrameSink,
        IDisposable
    {
        private readonly ManualResetEventSlim releaseFirstFrame = new(false);
        private readonly List<long> sequences = [];
        private int calls;

        public ManualResetEventSlim FirstFrameEntered { get; } = new(false);

        public IReadOnlyList<long> Sequences
        {
            get
            {
                lock (sequences)
                {
                    return sequences.ToArray();
                }
            }
        }

        public void TakeOwnership(
            NativeRemoteWindowSourceUse sourceUse,
            NativeRemoteWindowFrame frame)
        {
            lock (sequences)
            {
                sequences.Add(frame.Sequence);
            }

            if (Interlocked.Increment(ref calls) == 1)
            {
                FirstFrameEntered.Set();
                releaseFirstFrame.Wait();
            }

            frame.Dispose();
        }

        public void ReleaseFirstFrame() => releaseFirstFrame.Set();

        public void Dispose()
        {
            releaseFirstFrame.Set();
            releaseFirstFrame.Dispose();
            FirstFrameEntered.Dispose();
        }
    }

    private sealed class ThrowingFrameSink(string exceptionMessage) :
        INativeRemoteWindowFrameSink
    {
        public void TakeOwnership(
            NativeRemoteWindowSourceUse sourceUse,
            NativeRemoteWindowFrame frame) =>
            throw new InvalidOperationException(exceptionMessage);
    }

    private sealed class CallbackFrameSink(Action callback) :
        INativeRemoteWindowFrameSink
    {
        public void TakeOwnership(
            NativeRemoteWindowSourceUse sourceUse,
            NativeRemoteWindowFrame frame)
        {
            callback();
            frame.Dispose();
        }
    }

    private sealed class RecordingMemoryOwner(int length) : IMemoryOwner<byte>
    {
        private readonly byte[] buffer = new byte[length];
        private int disposeCount;

        public int DisposeCount => Volatile.Read(ref disposeCount);

        public Memory<byte> Memory => buffer;

        public void Dispose() => Interlocked.Increment(ref disposeCount);
    }
}
