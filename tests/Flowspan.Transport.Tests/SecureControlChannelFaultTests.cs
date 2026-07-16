using System.Buffers.Binary;
using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class SecureControlChannelFaultTests
{
    [Fact]
    public async Task PreCancelledRekeyDoesNotWriteAdvanceOrFaultChannel()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        responder.Dispose();
        var stream = new ScriptedDuplexStream();
        await using var channel = new SecureControlChannel(
            stream,
            initiator,
            liveRekeyEnabled: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await channel.RekeyAsync(
                TimeSpan.FromSeconds(5),
                cancellation.Token));

        Assert.Equal(0, stream.WriteCalls);
        Assert.False(stream.IsDisposed);
        Assert.Equal<uint>(1, initiator.SendEpoch);
        Assert.Equal<uint>(1, initiator.ReceiveEpoch);

        await channel.SendAsync(CreateMessage());
        Assert.Equal(2, stream.WriteCalls);
    }

    [Fact]
    public async Task RekeyTimeoutAfterCommitFaultsChannelAndDestroysSession()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        responder.Dispose();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var stream = new ScriptedDuplexStream();
        var channel = new SecureControlChannel(
            stream,
            initiator,
            liveRekeyEnabled: true,
            time);

        Task rekey = channel.RekeyAsync(TimeSpan.FromSeconds(5)).AsTask();
        await time.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal<uint>(2, initiator.SendEpoch);
        Assert.Equal(2, stream.WriteCalls);

        time.Advance(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<TimeoutException>(async () => await rekey);
        Assert.True(stream.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() =>
            initiator.ExportSessionIdentifier());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await channel.SendAsync(CreateMessage()));
        await channel.DisposeAsync();
    }

    [Fact]
    public async Task RekeyDeadlineIncludesKeyUpdateWrite()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        responder.Dispose();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var stream = new ScriptedDuplexStream
        {
            BlockingWriteCall = 1,
        };
        var channel = new SecureControlChannel(
            stream,
            initiator,
            liveRekeyEnabled: true,
            time);
        Task rekey = channel.RekeyAsync(TimeSpan.FromSeconds(5)).AsTask();
        await time.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(1));

        time.Advance(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<TimeoutException>(async () => await rekey);
        Assert.Equal<uint>(1, initiator.SendEpoch);
        Assert.True(stream.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() =>
            initiator.ExportSessionIdentifier());
        await channel.DisposeAsync();
    }

    [Fact]
    public async Task RekeyCancellationAfterCommitFaultsChannelAndDestroysSession()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        responder.Dispose();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var stream = new ScriptedDuplexStream();
        var channel = new SecureControlChannel(
            stream,
            initiator,
            liveRekeyEnabled: true,
            time);
        using var cancellation = new CancellationTokenSource();

        Task rekey = channel.RekeyAsync(
            TimeSpan.FromSeconds(5),
            cancellation.Token).AsTask();
        await time.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal<uint>(2, initiator.SendEpoch);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await rekey);
        Assert.True(stream.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() =>
            initiator.ExportSessionIdentifier());
        await channel.DisposeAsync();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    public async Task RekeyTransportFailureAtEveryWriteBoundaryClosesChannel(
        int failingWriteCall,
        bool failFlush)
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        responder.Dispose();
        var injected = new IOException("Injected rekey transport failure.");
        var stream = new ScriptedDuplexStream
        {
            FailingWriteCall = failingWriteCall,
            WriteFailure = failFlush ? null : injected,
            FlushFailure = failFlush ? injected : null,
        };
        var channel = new SecureControlChannel(
            stream,
            initiator,
            liveRekeyEnabled: true);

        IOException failure = await Assert.ThrowsAsync<IOException>(async () =>
            await channel.RekeyAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(injected, failure);
        Assert.True(stream.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() =>
            initiator.ExportSessionIdentifier());
        await channel.DisposeAsync();
    }

    [Fact]
    public async Task RekeyAndCleanupFailuresPreserveBothCauses()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        responder.Dispose();
        var primary = new IOException("Injected rekey write failure.");
        var cleanup = new IOException("Injected channel cleanup failure.");
        var stream = new ScriptedDuplexStream
        {
            FailingWriteCall = 1,
            WriteFailure = primary,
            DisposeFailure = cleanup,
        };
        var channel = new SecureControlChannel(
            stream,
            initiator,
            liveRekeyEnabled: true);

        AggregateException failure =
            await Assert.ThrowsAsync<AggregateException>(async () =>
                await channel.RekeyAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal([primary, cleanup], failure.InnerExceptions);
        Assert.True(stream.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() =>
            initiator.ExportSessionIdentifier());
    }

    [Fact]
    public async Task ReceiveFailureClosesChannelAndDestroysSession()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        initiator.Dispose();
        var injected = new IOException("Injected receive failure.");
        var stream = new ScriptedDuplexStream
        {
            ReadFailure = injected,
        };
        var channel = new SecureControlChannel(stream, responder);

        IOException failure = await Assert.ThrowsAsync<IOException>(async () =>
            await channel.ReceiveAsync());

        Assert.Same(injected, failure);
        Assert.True(stream.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() =>
            responder.ExportSessionIdentifier());
        await channel.DisposeAsync();
    }

    [Fact]
    public async Task EndOfStreamDuringRekeyResponseFaultsEveryWaiter()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        responder.Dispose();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var stream = new ScriptedDuplexStream();
        var channel = new SecureControlChannel(
            stream,
            initiator,
            liveRekeyEnabled: true,
            time);
        Task rekey = channel.RekeyAsync(TimeSpan.FromSeconds(5)).AsTask();
        await time.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(1));

        EndOfStreamException receiveFailure =
            await Assert.ThrowsAsync<EndOfStreamException>(async () =>
                await channel.ReceiveAsync());
        EndOfStreamException rekeyFailure =
            await Assert.ThrowsAsync<EndOfStreamException>(async () =>
                await rekey);

        Assert.Same(receiveFailure, rekeyFailure);
        Assert.True(stream.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() =>
            initiator.ExportSessionIdentifier());
        await channel.DisposeAsync();
    }

    [Fact]
    public async Task MalformedKeyUpdateConsumesRecordThenFaultsWithoutEpochChange()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        using (initiator)
        {
            byte[] malformed = Convert.FromHexString("465352310101000000");
            byte[] wire = CreateWireFrame(initiator, malformed);
            CryptographicOperations.ZeroMemory(malformed);
            var stream = new ScriptedDuplexStream(wire);
            var channel = new SecureControlChannel(
                stream,
                responder,
                liveRekeyEnabled: true);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await channel.ReceiveAsync());

            Assert.Equal<uint>(1, responder.ReceiveEpoch);
            Assert.Equal<ulong>(1, responder.NextReceiveSequence);
            Assert.True(stream.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() =>
                responder.ExportSessionIdentifier());
            await channel.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthenticationFailureDoesNotAdvanceReceiveState()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        using (initiator)
        {
            byte[] plaintext = "tampered"u8.ToArray();
            byte[] frame = initiator.Encrypt(plaintext);
            CryptographicOperations.ZeroMemory(plaintext);
            frame[^1] ^= 0x80;
            byte[] wire = PrefixFrame(frame);
            CryptographicOperations.ZeroMemory(frame);
            var stream = new ScriptedDuplexStream(wire);
            var channel = new SecureControlChannel(stream, responder);

            await Assert.ThrowsAsync<AuthenticationTagMismatchException>(async () =>
                await channel.ReceiveAsync());

            Assert.Equal<ulong>(0, responder.NextReceiveSequence);
            Assert.Equal<uint>(1, responder.ReceiveEpoch);
            Assert.True(stream.IsDisposed);
            await channel.DisposeAsync();
        }
    }

    [Fact]
    public async Task PeerRequestResponseWriteFailureClosesHalfTransition()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        using (initiator)
        {
            byte[] update = SecureSessionKeyUpdateCodec.Encode(
                SecureSessionKeyUpdate.Create(
                    requestPeerUpdate: true,
                    nextEpoch: 2));
            byte[] wire = CreateWireFrame(initiator, update);
            CryptographicOperations.ZeroMemory(update);
            initiator.AdvanceSendEpoch(nextEpoch: 2);
            var injected = new IOException("Injected response write failure.");
            var stream = new ScriptedDuplexStream(wire)
            {
                FailingWriteCall = 1,
                WriteFailure = injected,
            };
            var channel = new SecureControlChannel(
                stream,
                responder,
                liveRekeyEnabled: true);

            IOException failure = await Assert.ThrowsAsync<IOException>(async () =>
                await channel.ReceiveAsync());

            Assert.Same(injected, failure);
            Assert.Equal<uint>(2, responder.ReceiveEpoch);
            Assert.Equal<uint>(1, responder.SendEpoch);
            Assert.True(stream.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() =>
                responder.ExportSessionIdentifier());
            await channel.DisposeAsync();
        }
    }

    private static byte[] CreateWireFrame(
        SecureFrameSession sender,
        ReadOnlySpan<byte> plaintext)
    {
        byte[] frame = sender.Encrypt(plaintext);
        try
        {
            return PrefixFrame(frame);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(frame);
        }
    }

    private static byte[] PrefixFrame(ReadOnlySpan<byte> frame)
    {
        byte[] wire = new byte[sizeof(int) + frame.Length];
        BinaryPrimitives.WriteInt32BigEndian(wire, frame.Length);
        frame.CopyTo(wire.AsSpan(sizeof(int)));
        return wire;
    }

    private static (SecureFrameSession Initiator, SecureFrameSession Responder)
        CreateSessions()
    {
        byte[] firstKey = Enumerable.Repeat((byte)0x11, 32).ToArray();
        byte[] secondKey = Enumerable.Repeat((byte)0x22, 32).ToArray();
        byte[] sessionIdentifier = Enumerable.Repeat((byte)0x33, 16).ToArray();
        try
        {
            return (
                new SecureFrameSession(
                    firstKey,
                    SecureFrameDirection.InitiatorToResponder,
                    secondKey,
                    SecureFrameDirection.ResponderToInitiator,
                    sessionIdentifier),
                new SecureFrameSession(
                    secondKey,
                    SecureFrameDirection.ResponderToInitiator,
                    firstKey,
                    SecureFrameDirection.InitiatorToResponder,
                    sessionIdentifier));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(firstKey);
            CryptographicOperations.ZeroMemory(secondKey);
            CryptographicOperations.ZeroMemory(sessionIdentifier);
        }
    }

    private static ControlMessage CreateMessage() => ControlMessage.Create(
        new ProtocolVersion(1, 3),
        ControlMessageType.Hello,
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        CorrelationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
        DateTimeOffset.UnixEpoch,
        TimeSpan.FromSeconds(30),
        "{\"fault\":false}");

    private sealed class ScriptedDuplexStream(byte[]? input = null) : Stream
    {
        private readonly byte[] input = input ?? [];
        private int inputOffset;

        public int BlockingWriteCall { get; init; }

        public Exception? DisposeFailure { get; init; }

        public int FailingWriteCall { get; init; }

        public Exception? FlushFailure { get; init; }

        public bool IsDisposed { get; private set; }

        public Exception? ReadFailure { get; init; }

        public int WriteCalls { get; private set; }

        public Exception? WriteFailure { get; init; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return FlushFailure is null
                ? Task.CompletedTask
                : Task.FromException(FlushFailure);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReadFailure is not null)
            {
                return ValueTask.FromException<int>(ReadFailure);
            }

            int read = Math.Min(buffer.Length, input.Length - inputOffset);
            input.AsMemory(inputOffset, read).CopyTo(buffer);
            inputOffset += read;
            return ValueTask.FromResult(read);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCalls++;
            if (WriteCalls == BlockingWriteCall)
            {
                return new ValueTask(Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken));
            }

            return WriteFailure is not null && WriteCalls == FailingWriteCall
                ? ValueTask.FromException(WriteFailure)
                : ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            if (disposing && DisposeFailure is not null)
            {
                throw DisposeFailure;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly Lock gate = new();
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset utcNow = utcNow;

        public TaskCompletionSource TimerCreated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Advance(TimeSpan elapsed)
        {
            List<ManualTimer> candidates;
            DateTimeOffset now;
            lock (gate)
            {
                utcNow = utcNow.Add(elapsed);
                now = utcNow;
                candidates = timers.ToList();
            }

            foreach (ManualTimer timer in candidates.Where(timer => timer.IsDue(now)))
            {
                timer.Fire(now);
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (gate)
            {
                timers.Add(timer);
            }

            TimerCreated.TrySetResult();
            return timer;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
            {
                return utcNow;
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            private DateTimeOffset dueAt = DateTimeOffset.MaxValue;
            private bool disposed;
            private TimeSpan period = Timeout.InfiniteTimeSpan;

            public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
            {
                lock (owner.gate)
                {
                    if (disposed)
                    {
                        return false;
                    }

                    dueAt = dueTime == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : owner.utcNow.Add(dueTime);
                    period = newPeriod;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner.gate)
                {
                    disposed = true;
                    owner.timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Fire(DateTimeOffset now)
            {
                lock (owner.gate)
                {
                    if (disposed || dueAt > now)
                    {
                        return;
                    }

                    dueAt = period == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : now.Add(period);
                }

                callback(state);
            }

            public bool IsDue(DateTimeOffset now)
            {
                lock (owner.gate)
                {
                    return !disposed && dueAt <= now;
                }
            }
        }
    }
}
