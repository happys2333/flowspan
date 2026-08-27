using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class RemoteWindowMediaChannelTests
{
    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly ActivityId ActivityId =
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task EncryptedMediaFrameRoundTripsOverLoopbackTcp()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        using TcpClient server = await accepting;
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSecureSessions();
        await using var clientChannel = new SecureRemoteWindowMediaChannel(
            client.GetStream(),
            initiator,
            SessionId,
            ActivityId);
        await using var serverChannel = new SecureRemoteWindowMediaChannel(
            server.GetStream(),
            responder,
            SessionId,
            ActivityId);
        RemoteWindowMediaFrame expected = RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Video,
            sequence: 9,
            chunkIndex: 0,
            chunkCount: 1,
            [0x10, 0x20, 0x30]);

        Task<RemoteWindowMediaFrame> receiving =
            serverChannel.ReceiveAsync().AsTask();
        await clientChannel.SendAsync(expected);
        RemoteWindowMediaFrame actual = await receiving;

        Assert.Equal(expected.SessionId, actual.SessionId);
        Assert.Equal(expected.ActivityId, actual.ActivityId);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Sequence, actual.Sequence);
        Assert.Equal(expected.ChunkIndex, actual.ChunkIndex);
        Assert.Equal(expected.ChunkCount, actual.ChunkCount);
        Assert.Equal(expected.ExportPayload(), actual.ExportPayload());
    }

    [Fact]
    public async Task AuthenticatedProtocolOnePointFiveMediaKeysProtectSecondLoopbackStream()
    {
        (AuthenticatedSession initiatorSession, AuthenticatedSession responderSession) =
            CreateAuthenticatedSessions();
        using (initiatorSession)
        using (responderSession)
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start(backlog: 1);
            var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
            Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
            using var client = new TcpClient();
            await client.ConnectAsync(endpoint.Address, endpoint.Port);
            using TcpClient server = await accepting;
            SecureFrameSession initiatorMedia = Assert.IsType<SecureFrameSession>(
                initiatorSession.RemoteWindowMediaFrames);
            SecureFrameSession responderMedia = Assert.IsType<SecureFrameSession>(
                responderSession.RemoteWindowMediaFrames);
            await using var clientChannel = new SecureRemoteWindowMediaChannel(
                client.GetStream(),
                initiatorMedia,
                SessionId,
                ActivityId);
            await using var serverChannel = new SecureRemoteWindowMediaChannel(
                server.GetStream(),
                responderMedia,
                SessionId,
                ActivityId);
            RemoteWindowMediaFrame expected = RemoteWindowMediaFrame.Create(
                SessionId,
                ActivityId,
                RemoteWindowMediaKind.Audio,
                sequence: 1,
                chunkIndex: 0,
                chunkCount: 1,
                [0x01, 0x02, 0x03, 0x04]);

            Task<RemoteWindowMediaFrame> receiving =
                serverChannel.ReceiveAsync().AsTask();
            await clientChannel.SendAsync(expected);
            RemoteWindowMediaFrame actual = await receiving;

            Assert.Equal(
                ProtocolFeatures.RemoteWindowMinimumVersion,
                initiatorSession.ProtocolVersion);
            Assert.Equal(expected.ExportPayload(), actual.ExportPayload());
            Assert.NotEqual(
                initiatorSession.SecureFrames.SessionIdentifier,
                initiatorMedia.SessionIdentifier);
        }
    }

    [Fact]
    public async Task HostileEncryptedLengthFaultsChannelBeforeAllocation()
    {
        byte[] hostilePrefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(
            hostilePrefix,
            SecureRemoteWindowMediaChannel.MaximumEncryptedFrameBytes + 1);
        var stream = new MemoryStream(hostilePrefix);
        (SecureFrameSession unused, SecureFrameSession receiver) =
            CreateSecureSessions();
        unused.Dispose();
        await using var channel = new SecureRemoteWindowMediaChannel(
            stream,
            receiver,
            SessionId,
            ActivityId);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await channel.ReceiveAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await channel.ReceiveAsync());
    }

    [Fact]
    public async Task RepeatedMediaSequenceFaultsReceiver()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        using TcpClient server = await accepting;
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSecureSessions();
        await using var clientChannel = new SecureRemoteWindowMediaChannel(
            client.GetStream(),
            initiator,
            SessionId,
            ActivityId);
        await using var serverChannel = new SecureRemoteWindowMediaChannel(
            server.GetStream(),
            responder,
            SessionId,
            ActivityId);
        RemoteWindowMediaFrame first = RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Cursor,
            sequence: 7,
            chunkIndex: 0,
            chunkCount: 1,
            [0x01]);
        RemoteWindowMediaFrame repeated = RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Cursor,
            sequence: 7,
            chunkIndex: 0,
            chunkCount: 1,
            [0x02]);

        await clientChannel.SendAsync(first);
        Assert.Equal<ulong>(7, (await serverChannel.ReceiveAsync()).Sequence);
        await clientChannel.SendAsync(repeated);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await serverChannel.ReceiveAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await serverChannel.ReceiveAsync());
    }

    [Fact]
    public async Task SlidingReceiveRateRejectsBurstAcrossOneSecondBoundary()
    {
        (SecureFrameSession sender, SecureFrameSession receiver) =
            CreateSecureSessions();
        using (sender)
        {
            var stream = new MemoryStream();
            for (ulong sequence = 1;
                sequence <= SecureRemoteWindowMediaChannel.MaximumReceiveFramesPerSecond + 1;
                sequence++)
            {
                AppendEncryptedFrame(
                    stream,
                    sender,
                    RemoteWindowMediaFrame.Create(
                        SessionId,
                        ActivityId,
                        RemoteWindowMediaKind.Cursor,
                        sequence,
                        chunkIndex: 0,
                        chunkCount: 1,
                        [0x01]));
            }

            stream.Position = 0;
            var time = new MutableTimeProvider(
                new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero));
            await using var channel = new SecureRemoteWindowMediaChannel(
                stream,
                receiver,
                SessionId,
                ActivityId,
                timeProvider: time);
            _ = await channel.ReceiveAsync();
            time.Advance(TimeSpan.FromMilliseconds(999));
            for (int index = 1;
                index < SecureRemoteWindowMediaChannel.MaximumReceiveFramesPerSecond;
                index++)
            {
                _ = await channel.ReceiveAsync();
            }

            time.Advance(TimeSpan.FromMilliseconds(1));
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await channel.ReceiveAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await channel.ReceiveAsync());
        }
    }

    [Fact]
    public async Task BlockedAcceptedWriteTimesOutAndFaultsChannel()
    {
        var stream = new BlockingWriteStream();
        var time = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero));
        (SecureFrameSession sender, SecureFrameSession unused) =
            CreateSecureSessions();
        unused.Dispose();
        await using var channel = new SecureRemoteWindowMediaChannel(
            stream,
            sender,
            SessionId,
            ActivityId,
            timeProvider: time);
        RemoteWindowMediaFrame frame = RemoteWindowMediaFrame.Create(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Audio,
            sequence: 1,
            chunkIndex: 0,
            chunkCount: 1,
            [0x01]);

        Task sending = channel.SendAsync(frame).AsTask();
        await Task.WhenAll(stream.WriteStarted, time.TimerCreated.Task);
        time.Advance(SecureRemoteWindowMediaChannel.DefaultOperationTimeout);

        await Assert.ThrowsAsync<TimeoutException>(() => sending);
        Assert.True(stream.IsDisposed);
        Assert.True(stream.PendingPayloadMatchesInitialCopy);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await channel.SendAsync(frame));
        stream.CompleteWrite();
    }

    [Fact]
    public async Task IdleReadHasNoWriteDeadlineAndCallerCancellationFaultsChannel()
    {
        var stream = new BlockingReadStream();
        var time = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero));
        (SecureFrameSession unused, SecureFrameSession receiver) =
            CreateSecureSessions();
        unused.Dispose();
        await using var channel = new SecureRemoteWindowMediaChannel(
            stream,
            receiver,
            SessionId,
            ActivityId,
            timeProvider: time);
        using var cancellation = new CancellationTokenSource();

        Task receiving = channel.ReceiveAsync(cancellation.Token).AsTask();
        await stream.ReadStarted;
        time.Advance(SecureRemoteWindowMediaChannel.DefaultOperationTimeout);
        Assert.False(receiving.IsCompleted);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiving);
        Assert.True(stream.IsDisposed);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await channel.ReceiveAsync());
    }

    [Fact]
    public async Task ConcurrentDisposersJoinAndObserveCleanupFailure()
    {
        var stream = new BlockingFailingDisposeStream();
        (SecureFrameSession session, SecureFrameSession unused) =
            CreateSecureSessions();
        unused.Dispose();
        var channel = new SecureRemoteWindowMediaChannel(
            stream,
            session,
            SessionId,
            ActivityId);
        Task first = Task.Run(async () => await channel.DisposeAsync());
        await stream.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(2));

        Task second = channel.DisposeAsync().AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        stream.AllowDispose.TrySetResult();
        InvalidOperationException firstFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        InvalidOperationException secondFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.Equal("stream cleanup failed", firstFailure.Message);
        Assert.Equal("stream cleanup failed", secondFailure.Message);
        Assert.Throws<ObjectDisposedException>(() => session.Encrypt([0x01]));
    }

    [Fact]
    public async Task CanceledNonCooperativeEncryptedReadRetainsBufferUntilIoCompletes()
    {
        var stream = new NonCooperativeFrameReadStream(frameBytes: 64);
        (SecureFrameSession unused, SecureFrameSession receiver) =
            CreateSecureSessions();
        unused.Dispose();
        var channel = new SecureRemoteWindowMediaChannel(
            stream,
            receiver,
            SessionId,
            ActivityId);
        using var cancellation = new CancellationTokenSource();
        Task receiving = channel.ReceiveAsync(cancellation.Token).AsTask();
        await stream.PayloadReadStarted.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiving);
        stream.CompleteRead();

        Assert.True(stream.PayloadWasStableUntilCompletion);
        await channel.DisposeAsync();
    }

    private static void AppendEncryptedFrame(
        Stream stream,
        SecureFrameSession sender,
        RemoteWindowMediaFrame frame)
    {
        byte[] plaintext = RemoteWindowMediaFrameCodec.Encode(frame);
        byte[] encrypted = sender.Encrypt(plaintext);
        Span<byte> prefix = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(prefix, encrypted.Length);
        stream.Write(prefix);
        stream.Write(encrypted);
        CryptographicOperations.ZeroMemory(plaintext);
        CryptographicOperations.ZeroMemory(encrypted);
    }

    private static (SecureFrameSession Initiator, SecureFrameSession Responder)
        CreateSecureSessions()
    {
        byte[] secret = Enumerable.Repeat((byte)0x33, 32).ToArray();
        byte[] transcriptHash = SHA256.HashData(
            Encoding.ASCII.GetBytes("authenticated-media-test-transcript"));
        using SecureSessionKeyMaterial material =
            SecureSessionKeyMaterial.DeriveRemoteWindowMedia(
                secret,
                transcriptHash);
        CryptographicOperations.ZeroMemory(secret);
        CryptographicOperations.ZeroMemory(transcriptHash);
        return (
            material.CreateSession(SecureSessionRole.Initiator),
            material.CreateSession(SecureSessionRole.Responder));
    }

    private static (AuthenticatedSession Initiator, AuthenticatedSession Responder)
        CreateAuthenticatedSessions()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        using EphemeralKeyAgreement initiatorAgreement = EphemeralKeyAgreement.Generate();
        using EphemeralKeyAgreement responderAgreement = EphemeralKeyAgreement.Generate();
        SessionHandshakeHello initiatorHello = SessionHandshakeHello.Create(
            SecureSessionRole.Initiator,
            initiatorIdentity.PublicIdentity,
            [ProtocolFeatures.RemoteWindowMinimumVersion],
            initiatorAgreement.ExportSubjectPublicKeyInfo(),
            Enumerable.Repeat((byte)0x11, SessionHandshakeHello.NonceLength).ToArray());
        SessionHandshakeHello responderHello = SessionHandshakeHello.Create(
            SecureSessionRole.Responder,
            responderIdentity.PublicIdentity,
            [ProtocolFeatures.RemoteWindowMinimumVersion],
            responderAgreement.ExportSubjectPublicKeyInfo(),
            Enumerable.Repeat((byte)0x22, SessionHandshakeHello.NonceLength).ToArray());
        SessionHandshakeTranscript transcript = SessionHandshakeTranscript.Create(
            initiatorHello,
            responderHello);
        SessionHandshakeAuthentication initiatorAuthentication =
            SessionHandshakeAuthentication.Create(transcript, initiatorIdentity);
        SessionHandshakeAuthentication responderAuthentication =
            SessionHandshakeAuthentication.Create(transcript, responderIdentity);
        AuthenticatedSession? initiator = null;
        try
        {
            initiator = AuthenticatedSessionHandshake.Complete(
                transcript,
                SecureSessionRole.Initiator,
                initiatorIdentity.PublicIdentity,
                responderIdentity.PublicIdentity,
                initiatorAgreement,
                responderAuthentication);
            AuthenticatedSession responder = AuthenticatedSessionHandshake.Complete(
                transcript,
                SecureSessionRole.Responder,
                responderIdentity.PublicIdentity,
                initiatorIdentity.PublicIdentity,
                responderAgreement,
                initiatorAuthentication);
            return (initiator, responder);
        }
        catch
        {
            initiator?.Dispose();
            throw;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public void Advance(TimeSpan elapsed) => utcNow = utcNow.Add(elapsed);

        public override DateTimeOffset GetUtcNow() => utcNow;
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

    private sealed class BlockingWriteStream : Stream
    {
        private readonly TaskCompletionSource writeStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource writeStopped = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private byte[]? pendingPayloadCopy;
        private ReadOnlyMemory<byte> pendingPayload;
        private int writes;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public bool IsDisposed { get; private set; }

        public bool PendingPayloadMatchesInitialCopy =>
            pendingPayloadCopy is not null
            && pendingPayload.Span.SequenceEqual(pendingPayloadCopy);

        public Task WriteStarted => writeStarted.Task;

        public void CompleteWrite() => writeStopped.TrySetResult();

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

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
            if (Interlocked.Increment(ref writes) == 1)
            {
                return ValueTask.CompletedTask;
            }

            pendingPayload = buffer;
            pendingPayloadCopy = buffer.ToArray();
            writeStarted.TrySetResult();
            return new ValueTask(writeStopped.Task);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class BlockingReadStream : Stream
    {
        private readonly TaskCompletionSource readStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> readStopped = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public bool IsDisposed { get; private set; }

        public Task ReadStarted => readStarted.Task;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            readStarted.TrySetResult();
            return new ValueTask<int>(readStopped.Task);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class BlockingFailingDisposeStream : Stream
    {
        private int disposeCalls;
        private readonly TaskCompletionSource disposeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowDispose { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public Task DisposeStarted => disposeStarted.Task;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Increment(ref disposeCalls) == 1)
            {
                disposeStarted.TrySetResult();
                AllowDispose.Task.GetAwaiter().GetResult();
                throw new InvalidOperationException("stream cleanup failed");
            }

            base.Dispose(disposing);
        }
    }

    private sealed class NonCooperativeFrameReadStream(int frameBytes) : Stream
    {
        private readonly TaskCompletionSource<int> readCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private byte[]? payloadCopy;
        private Memory<byte> pendingPayload;
        private int reads;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public Task PayloadReadStarted { get; private set; } = Task.CompletedTask;

        public bool PayloadWasStableUntilCompletion { get; private set; }

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void CompleteRead()
        {
            PayloadWasStableUntilCompletion = payloadCopy is not null
                && pendingPayload.Span.SequenceEqual(payloadCopy);
            readCompletion.TrySetResult(pendingPayload.Length);
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (Interlocked.Increment(ref reads) == 1)
            {
                BinaryPrimitives.WriteInt32BigEndian(buffer.Span, frameBytes);
                return new ValueTask<int>(buffer.Length);
            }

            buffer.Span.Fill(0x7c);
            pendingPayload = buffer;
            payloadCopy = buffer.ToArray();
            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            PayloadReadStarted = started.Task;
            started.TrySetResult();
            return new ValueTask<int>(readCompletion.Task);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }
    }
}
