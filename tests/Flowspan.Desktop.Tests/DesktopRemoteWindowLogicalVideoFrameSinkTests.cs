using System.Buffers;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopRemoteWindowLogicalVideoFrameSinkTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        8,
        28,
        12,
        0,
        0,
        TimeSpan.Zero);

    private static readonly DeviceId HostDeviceId = DeviceId.Parse(
        "11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId PeerDeviceId = DeviceId.Parse(
        "22222222-2222-2222-2222-222222222222");

    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse(
            "33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task ExactNativeFrameEncodesAndSendsOneLogicalVideoFrame()
    {
        NativeRemoteWindowSourceUse sourceUse = Assert.Single(
            await CreateSourceUsesAsync(sessionCount: 1));
        var budget = new RemoteWindowMediaSessionBudget();
        var media = new RecordingMediaSink();
        var sender = new RemoteWindowLogicalVideoFrameSender(
            budget,
            PeerDeviceId,
            media);
        await using var sink = new DesktopRemoteWindowLogicalVideoFrameSink(
            sourceUse,
            SessionId,
            sourceUse.ActivityId,
            sender);
        (NativeRemoteWindowFrame frame, RecordingMemoryOwner owner) =
            CreateFrame(sourceUse, nativeSequence: 41, width: 2, height: 2);

        sink.TakeOwnership(sourceUse, frame);

        await media.WaitForSendCountAsync(1);
        CapturedMediaFrame sent = Assert.Single(media.Sends);
        Assert.Equal(RemoteWindowMediaKind.Video, sent.Kind);
        Assert.Equal<ulong>(1, sent.Sequence);
        Assert.Equal<ushort>(0, sent.ChunkIndex);
        Assert.Equal<ushort>(1, sent.ChunkCount);
        Assert.Equal(0xff, sent.Payload[0]);
        Assert.Equal(0xd8, sent.Payload[1]);
        Assert.Equal(1, owner.DisposeCount);

        await sink.DisposeAsync();
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Fact]
    public async Task ConstructorRejectsActivityOutsideExactSourceBinding()
    {
        NativeRemoteWindowSourceUse sourceUse = Assert.Single(
            await CreateSourceUsesAsync(sessionCount: 1));
        var sender = new RemoteWindowLogicalVideoFrameSender(
            new RemoteWindowMediaSessionBudget(),
            PeerDeviceId,
            new RecordingMediaSink());

        Assert.Throws<ArgumentException>(() =>
            new DesktopRemoteWindowLogicalVideoFrameSink(
                sourceUse,
                SessionId,
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                sender));

        await sender.DisposeAsync();
    }

    [Fact]
    public async Task SnapshotConstructorBindsTheExpectedFirstSessionUse()
    {
        (NativeRemoteWindowSourceSnapshot snapshot,
            IReadOnlyList<NativeRemoteWindowSourceUse> uses) =
            await CreateSourceFixtureAsync(sessionCount: 1);
        NativeRemoteWindowSourceUse sourceUse = Assert.Single(uses);
        var media = new RecordingMediaSink();
        var sender = new RemoteWindowLogicalVideoFrameSender(
            new RemoteWindowMediaSessionBudget(),
            PeerDeviceId,
            media);
        await using var sink = new DesktopRemoteWindowLogicalVideoFrameSink(
            snapshot,
            sourceUse.OwnerGeneration,
            sourceUse.SessionGeneration,
            SessionId,
            sender,
            static _ => EncodedPayload(1));

        sink.TakeOwnership(
            sourceUse,
            CreateFrame(sourceUse, 73, 1, 1).Frame);

        await media.WaitForSendCountAsync(1);
        Assert.Equal<ulong>(1, Assert.Single(media.Sends).Sequence);
    }

    [Fact]
    public async Task ExactBindingAndEncodingDropConsumeEveryNativeOwnerWithoutSending()
    {
        NativeRemoteWindowSourceUse expected = Assert.Single(
            await CreateSourceUsesAsync(sessionCount: 1));
        NativeRemoteWindowSourceUse foreign = Assert.Single(
            await CreateSourceUsesAsync(sessionCount: 1));
        Assert.NotEqual(expected.Token, foreign.Token);
        Assert.Equal(expected.OwnerGeneration, foreign.OwnerGeneration);
        Assert.Equal(expected.SessionGeneration, foreign.SessionGeneration);
        Assert.Equal(expected.SourceGeneration, foreign.SourceGeneration);
        Assert.Equal(expected.GeometryRevision, foreign.GeometryRevision);
        var budget = new RemoteWindowMediaSessionBudget();
        var media = new RecordingMediaSink();
        var sender = new RemoteWindowLogicalVideoFrameSender(
            budget,
            PeerDeviceId,
            media);
        var drops = new List<DesktopRemoteWindowJpegEncodingStatus>();
        await using var sink = new DesktopRemoteWindowLogicalVideoFrameSink(
            expected,
            SessionId,
            expected.ActivityId,
            sender,
            static _ => DesktopRemoteWindowJpegEncodingResult.Failed(
                DesktopRemoteWindowJpegEncodingStatus.InvalidPixelPlane),
            drops.Add);
        (NativeRemoteWindowFrame foreignFrame, RecordingMemoryOwner foreignOwner) =
            CreateFrame(foreign, nativeSequence: 1, width: 1, height: 1);
        (NativeRemoteWindowFrame rejectedFrame, RecordingMemoryOwner rejectedOwner) =
            CreateFrame(expected, nativeSequence: 2, width: 1, height: 1);

        sink.TakeOwnership(foreign, foreignFrame);
        sink.TakeOwnership(expected, rejectedFrame);

        Assert.Empty(media.Sends);
        Assert.Equal(1, foreignOwner.DisposeCount);
        Assert.Equal(1, rejectedOwner.DisposeCount);
        Assert.Equal(
            [DesktopRemoteWindowJpegEncodingStatus.InvalidPixelPlane],
            drops);
    }

    [Fact]
    public async Task WireSequenceIgnoresNativeSequenceAndReservesActualChunkRanges()
    {
        int maximumChunk = RemoteWindowMediaFrame.MaximumPayloadBytes;
        int[] payloadLengths =
        [
            1,
            maximumChunk + 1,
            RemoteWindowVideoFrameChunker.MaximumLogicalFrameBytes,
        ];
        var encodingIndex = 0;
        NativeRemoteWindowSourceUse sourceUse = Assert.Single(
            await CreateSourceUsesAsync(sessionCount: 1));
        var media = new RecordingMediaSink();
        var sender = new RemoteWindowLogicalVideoFrameSender(
            new RemoteWindowMediaSessionBudget(),
            PeerDeviceId,
            media);
        await using var sink = new DesktopRemoteWindowLogicalVideoFrameSink(
            sourceUse,
            SessionId,
            sourceUse.ActivityId,
            sender,
            _ => EncodedPayload(payloadLengths[encodingIndex++]));

        for (var index = 0; index < payloadLengths.Length; index++)
        {
            (NativeRemoteWindowFrame frame, RecordingMemoryOwner owner) =
                CreateFrame(
                    sourceUse,
                    nativeSequence: 900 + index,
                    width: 1,
                    height: 1);
            sink.TakeOwnership(sourceUse, frame);
            int expectedChunks = payloadLengths
                .Take(index + 1)
                .Sum(static length => checked(
                    (length + RemoteWindowMediaFrame.MaximumPayloadBytes - 1)
                    / RemoteWindowMediaFrame.MaximumPayloadBytes));
            await media.WaitForSendCountAsync(expectedChunks);
            Assert.Equal(1, owner.DisposeCount);
        }

        Assert.Equal(
            Enumerable.Range(1, 19).Select(static value => checked((ulong)value)),
            media.Sends.Select(static sent => sent.Sequence));
        Assert.Equal<ushort>(1, media.Sends[0].ChunkCount);
        Assert.All(
            media.Sends.Skip(1).Take(2),
            static sent => Assert.Equal<ushort>(2, sent.ChunkCount));
        Assert.All(
            media.Sends.Skip(3),
            static sent => Assert.Equal<ushort>(16, sent.ChunkCount));
    }

    [Fact]
    public async Task PendingEncodedFrameIsLatestWinsOnTheWire()
    {
        NativeRemoteWindowSourceUse sourceUse = Assert.Single(
            await CreateSourceUsesAsync(sessionCount: 1));
        var media = new BlockingFirstMediaSink();
        var sender = new RemoteWindowLogicalVideoFrameSender(
            new RemoteWindowMediaSessionBudget(),
            PeerDeviceId,
            media);
        await using var sink = new DesktopRemoteWindowLogicalVideoFrameSink(
            sourceUse,
            SessionId,
            sourceUse.ActivityId,
            sender,
            static _ => EncodedPayload(1));

        sink.TakeOwnership(
            sourceUse,
            CreateFrame(sourceUse, 1, 1, 1).Frame);
        await media.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(5));
        sink.TakeOwnership(
            sourceUse,
            CreateFrame(sourceUse, 2, 1, 1).Frame);
        sink.TakeOwnership(
            sourceUse,
            CreateFrame(sourceUse, 3, 1, 1).Frame);
        media.ReleaseFirst();

        await media.WaitForSendCountAsync(2);
        Assert.Equal([1UL, 3UL], media.Sequences);
    }

    [Fact]
    public async Task SenderFailureClosesBridgeAndReportsOneTerminalFault()
    {
        NativeRemoteWindowSourceUse sourceUse = Assert.Single(
            await CreateSourceUsesAsync(sessionCount: 1));
        var budget = new RemoteWindowMediaSessionBudget();
        var media = new FailingMediaSink();
        var sender = new RemoteWindowLogicalVideoFrameSender(
            budget,
            PeerDeviceId,
            media);
        var faulted = new TaskCompletionSource<
            DesktopRemoteWindowLogicalVideoFrameSinkFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sink = new DesktopRemoteWindowLogicalVideoFrameSink(
            sourceUse,
            SessionId,
            sourceUse.ActivityId,
            sender,
            static _ => EncodedPayload(1),
            encodingDropped: null,
            fault => faulted.TrySetResult(fault));
        (NativeRemoteWindowFrame frame, RecordingMemoryOwner owner) =
            CreateFrame(sourceUse, 1, 1, 1);

        sink.TakeOwnership(sourceUse, frame);

        Assert.Equal(
            DesktopRemoteWindowLogicalVideoFrameSinkFault.SenderFailed,
            await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(sink.IsClosed);
        Assert.Equal(1, owner.DisposeCount);
        (NativeRemoteWindowFrame late, RecordingMemoryOwner lateOwner) =
            CreateFrame(sourceUse, 2, 1, 1);
        sink.TakeOwnership(sourceUse, late);
        Assert.Equal(1, lateOwner.DisposeCount);

        await sink.DisposeAsync();
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Fact]
    public async Task WireSequenceExhaustionFailsClosedWithoutEscapingCaptureCallback()
    {
        NativeRemoteWindowSourceUse sourceUse = Assert.Single(
            await CreateSourceUsesAsync(sessionCount: 1));
        var media = new RecordingMediaSink();
        var sender = new RemoteWindowLogicalVideoFrameSender(
            new RemoteWindowMediaSessionBudget(),
            PeerDeviceId,
            media);
        var faults = new List<DesktopRemoteWindowLogicalVideoFrameSinkFault>();
        await using var sink = new DesktopRemoteWindowLogicalVideoFrameSink(
            sourceUse,
            SessionId,
            sourceUse.ActivityId,
            sender,
            static _ => EncodedPayload(
                RemoteWindowMediaFrame.MaximumPayloadBytes + 1),
            encodingDropped: null,
            faults.Add,
            initialWireSequence: ulong.MaxValue);
        (NativeRemoteWindowFrame frame, RecordingMemoryOwner owner) =
            CreateFrame(sourceUse, 1, 1, 1);

        Exception? escaped = Record.Exception(
            () => sink.TakeOwnership(sourceUse, frame));

        Assert.Null(escaped);
        Assert.True(sink.IsClosed);
        Assert.Equal(1, owner.DisposeCount);
        Assert.Empty(media.Sends);
        Assert.Equal(
            [DesktopRemoteWindowLogicalVideoFrameSinkFault.WireSequenceExhausted],
            faults);
    }

    [Fact]
    public async Task StopNowDoesNotWaitForNonCooperativeSendAndDisposeDrainsBudget()
    {
        NativeRemoteWindowSourceUse sourceUse = Assert.Single(
            await CreateSourceUsesAsync(sessionCount: 1));
        var budget = new RemoteWindowMediaSessionBudget();
        var media = new NonCooperativeMediaSink();
        var sender = new RemoteWindowLogicalVideoFrameSender(
            budget,
            PeerDeviceId,
            media);
        var faults = new List<DesktopRemoteWindowLogicalVideoFrameSinkFault>();
        var sink = new DesktopRemoteWindowLogicalVideoFrameSink(
            sourceUse,
            SessionId,
            sourceUse.ActivityId,
            sender,
            static _ => EncodedPayload(1),
            encodingDropped: null,
            faults.Add);
        (NativeRemoteWindowFrame frame, RecordingMemoryOwner owner) =
            CreateFrame(sourceUse, 1, 1, 1);
        sink.TakeOwnership(sourceUse, frame);
        await media.SendStarted.WaitAsync(TimeSpan.FromSeconds(5));

        await Task.Run(sink.StopNow).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(sink.IsClosed);
        Assert.Equal(1, owner.DisposeCount);
        Assert.Empty(faults);
        Task disposal = sink.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        media.Release();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(faults);
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    [Fact]
    public async Task StopNowDoesNotWaitForEncoderAndLateEncodingCannotSubmit()
    {
        NativeRemoteWindowSourceUse sourceUse = Assert.Single(
            await CreateSourceUsesAsync(sessionCount: 1));
        var budget = new RemoteWindowMediaSessionBudget();
        var media = new RecordingMediaSink();
        var sender = new RemoteWindowLogicalVideoFrameSender(
            budget,
            PeerDeviceId,
            media);
        var encoderEntered = NewCompletion();
        var releaseEncoder = NewCompletion();
        var faults = new List<DesktopRemoteWindowLogicalVideoFrameSinkFault>();
        var sink = new DesktopRemoteWindowLogicalVideoFrameSink(
            sourceUse,
            SessionId,
            sourceUse.ActivityId,
            sender,
            _ =>
            {
                encoderEntered.TrySetResult();
                releaseEncoder.Task.GetAwaiter().GetResult();
                return EncodedPayload(1);
            },
            encodingDropped: null,
            faults.Add);
        (NativeRemoteWindowFrame frame, RecordingMemoryOwner owner) =
            CreateFrame(sourceUse, 1, 1, 1);
        Task delivering = Task.Factory.StartNew(
            () => sink.TakeOwnership(sourceUse, frame),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        await encoderEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Task.Run(sink.StopNow).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(sink.IsClosed);
        Assert.False(delivering.IsCompleted);
        Assert.Empty(faults);
        releaseEncoder.TrySetResult();
        await delivering.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, owner.DisposeCount);
        Assert.Empty(media.Sends);
        await sink.DisposeAsync();
        Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, budget.Snapshot);
    }

    private static DesktopRemoteWindowJpegEncodingResult EncodedPayload(
        int payloadLength) => DesktopRemoteWindowJpegEncodingResult.Encoded(
        new DesktopRemoteWindowEncodedJpeg(
            Enumerable.Repeat((byte)0x5a, payloadLength).ToArray(),
            width: 1,
            height: 1,
            new DesktopRemoteWindowJpegProfile(1, 1, 82)));

    private static async Task<IReadOnlyList<NativeRemoteWindowSourceUse>>
        CreateSourceUsesAsync(int sessionCount) =>
        (await CreateSourceFixtureAsync(sessionCount)).Uses;

    private static async Task<(NativeRemoteWindowSourceSnapshot Snapshot,
        IReadOnlyList<NativeRemoteWindowSourceUse> Uses)>
        CreateSourceFixtureAsync(int sessionCount)
    {
        using var registry = new NativeRemoteWindowSourceRegistry(HostDeviceId);
        using NativeRemoteWindowSourceRegistration registration =
            registry.RegisterGeneric(
                NativeRemoteWindowSourceMetadata.Create(
                    "Generic window",
                    "Test application",
                    NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2),
                    supportsCapture: true,
                    supportsInput: true,
                    SafeProtection()));
        NativeRemoteWindowSourceSnapshot snapshot = registration.Snapshot;
        Assert.True(registry.TryAcquire(
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
            new DiscardingFrameSink(),
            new NoOpSharingSessionBoundary(),
            TimeSpan.FromSeconds(10));

        for (var index = 0; index < sessionCount; index++)
        {
            RemoteWindowCommandResult started =
                await controller.StartAsync(SafeProtection());
            Assert.True(started.Succeeded);
            if (index + 1 < sessionCount)
            {
                Assert.True(controller.EmergencyStop().FullyStopped);
                Assert.True((await controller.ResetAfterLocalConfirmationAsync())
                    .Succeeded);
            }
        }

        return (snapshot, capture.SourceUses.ToArray());
    }

    private static (NativeRemoteWindowFrame Frame, RecordingMemoryOwner Owner)
        CreateFrame(
            NativeRemoteWindowSourceUse sourceUse,
            long nativeSequence,
            int width,
            int height)
    {
        int stride = checked(width * 4);
        int length = checked(stride * height);
        var owner = new RecordingMemoryOwner(length);
        Span<byte> pixels = owner.Memory.Span;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = checked((byte)(index + 1));
            pixels[index + 1] = checked((byte)(index + 2));
            pixels[index + 2] = checked((byte)(index + 3));
            pixels[index + 3] = 0xff;
        }

        return (
            NativeRemoteWindowFrame.TakeOwnership(
                owner,
                length,
                width,
                height,
                stride,
                NativeRemoteWindowPixelFormat.Bgra8888,
                sourceUse.OwnerGeneration,
                sourceUse.SessionGeneration,
                sourceUse.SourceGeneration,
                sourceUse.GeometryRevision,
                nativeSequence),
            owner);
    }

    private static ProtectionSnapshot SafeProtection() => new(
        ProtectionKind.Safe,
        Now,
        "test-probe");

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

    private sealed class DiscardingFrameSink : INativeRemoteWindowFrameSink
    {
        public void TakeOwnership(
            NativeRemoteWindowSourceUse sourceUse,
            NativeRemoteWindowFrame frame) => frame.Dispose();
    }

    private sealed class NoOpSharingSessionBoundary :
        ILocalSharingSessionBoundary
    {
        public LocalBoundaryResult DisconnectPeerNow(DeviceId peerDeviceId) =>
            LocalBoundaryResult.Confirmed("peer_disconnected");

        public LocalBoundaryResult DisconnectAllNow() =>
            LocalBoundaryResult.Confirmed("sessions_disconnected");
    }

    private sealed class RecordingMemoryOwner(int length) : IMemoryOwner<byte>
    {
        private byte[]? buffer = new byte[length];

        public int DisposeCount { get; private set; }

        public Memory<byte> Memory => buffer ?? throw new ObjectDisposedException(
            nameof(RecordingMemoryOwner));

        public void Dispose()
        {
            DisposeCount++;
            buffer = null;
        }
    }

    private sealed record CapturedMediaFrame(
        RemoteWindowMediaKind Kind,
        ulong Sequence,
        ushort ChunkIndex,
        ushort ChunkCount,
        byte[] Payload);

    private sealed class RecordingMediaSink : IRemoteWindowMediaSink
    {
        private readonly Lock gate = new();
        private TaskCompletionSource changed = NewCompletion();

        public IReadOnlyList<CapturedMediaFrame> Sends { get; private set; } = [];

        public ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                Sends =
                [
                    .. Sends,
                    new CapturedMediaFrame(
                        frame.Kind,
                        frame.Sequence,
                        frame.ChunkIndex,
                        frame.ChunkCount,
                        frame.ExportPayload()),
                ];
                TaskCompletionSource completed = changed;
                changed = NewCompletion();
                completed.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }

        public async Task WaitForSendCountAsync(int expected)
        {
            while (true)
            {
                Task wait;
                lock (gate)
                {
                    if (Sends.Count >= expected)
                    {
                        return;
                    }

                    wait = changed.Task;
                }

                await wait.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private sealed class BlockingFirstMediaSink : IRemoteWindowMediaSink
    {
        private readonly Lock gate = new();
        private readonly TaskCompletionSource firstSendStarted = NewCompletion();
        private readonly TaskCompletionSource releaseFirst = NewCompletion();
        private TaskCompletionSource changed = NewCompletion();
        private int sends;

        public Task FirstSendStarted => firstSendStarted.Task;

        public IReadOnlyList<ulong> Sequences { get; private set; } = [];

        public void ReleaseFirst() => releaseFirst.TrySetResult();

        public async ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                Sequences = [.. Sequences, frame.Sequence];
                TaskCompletionSource completed = changed;
                changed = NewCompletion();
                completed.TrySetResult();
            }

            if (Interlocked.Increment(ref sends) == 1)
            {
                firstSendStarted.TrySetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }
        }

        public async Task WaitForSendCountAsync(int expected)
        {
            while (true)
            {
                Task wait;
                lock (gate)
                {
                    if (Sequences.Count >= expected)
                    {
                        return;
                    }

                    wait = changed.Task;
                }

                await wait.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private sealed class FailingMediaSink : IRemoteWindowMediaSink
    {
        public ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(
                new InvalidOperationException("injected-media-failure"));
    }

    private sealed class NonCooperativeMediaSink : IRemoteWindowMediaSink
    {
        private readonly TaskCompletionSource release = NewCompletion();
        private readonly TaskCompletionSource sendStarted = NewCompletion();

        public Task SendStarted => sendStarted.Task;

        public void Release() => release.TrySetResult();

        public async ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            sendStarted.TrySetResult();
            await release.Task;
        }
    }

    private static TaskCompletionSource NewCompletion() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);
}
