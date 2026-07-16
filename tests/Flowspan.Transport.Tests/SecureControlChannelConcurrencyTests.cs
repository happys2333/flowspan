using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class SecureControlChannelConcurrencyTests
{
    [Fact]
    public async Task ConcurrentApplicationSendsCannotConsumeReservedRekeyFrame()
    {
        const int messageCount = 24;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<DirectTcpPeerConnection> accept =
            DirectTcpPeerConnection.AcceptAsync(listener).AsTask();
        await using DirectTcpPeerConnection client =
            await DirectTcpPeerConnection.ConnectAsync(endpoint);
        await using DirectTcpPeerConnection server = await accept;
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateBoundedSessions(maximumFramesPerEpoch: 4);
        await using SecureControlChannel clientChannel =
            client.UpgradeToSecureControl(initiator, liveRekeyEnabled: true);
        await using SecureControlChannel serverChannel =
            server.UpgradeToSecureControl(responder, liveRekeyEnabled: true);
        using var cancellation = new CancellationTokenSource();
        Task clientReceive = clientChannel.ReceiveAsync(cancellation.Token).AsTask();
        var receivedIds = new HashSet<Guid>();
        Task serverDrain = Task.Run(async () =>
        {
            for (int index = 0; index < messageCount; index++)
            {
                ControlMessage received = await serverChannel.ReceiveAsync(
                    cancellation.Token);
                Assert.True(receivedIds.Add(received.MessageId));
            }
        });
        var start = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task[] sends = Enumerable.Range(1, messageCount)
            .Select(index => Task.Run(async () =>
            {
                await start.Task;
                await clientChannel.SendAsync(CreateMessage(index));
            }))
            .ToArray();

        start.TrySetResult();
        await Task.WhenAll(sends);
        await serverDrain;

        Assert.Equal(messageCount, receivedIds.Count);
        Assert.True(initiator.SendEpoch > 1);
        Assert.Equal(initiator.SendEpoch, responder.ReceiveEpoch);
        Assert.Equal(initiator.ReceiveEpoch, responder.SendEpoch);
        cancellation.Cancel();
        await IgnoreFailureAsync(clientReceive);
    }

    [Fact]
    public async Task LegacyReaderRejectsKeyUpdateAsInvalidApplicationPlaintext()
    {
        (SecureFrameSession sender, SecureFrameSession receiver) = CreateSessions();
        using SecureFrameSession senderOwner = sender;
        byte[] plaintext = SecureSessionKeyUpdateCodec.Encode(
            SecureSessionKeyUpdate.Create(
                requestPeerUpdate: true,
                nextEpoch: 2));
        byte[] frame = sender.Encrypt(plaintext);
        CryptographicOperations.ZeroMemory(plaintext);
        byte[] wire = new byte[sizeof(int) + frame.Length];
        BinaryPrimitives.WriteInt32BigEndian(wire, frame.Length);
        frame.CopyTo(wire, sizeof(int));
        CryptographicOperations.ZeroMemory(frame);
        var channel = new SecureControlChannel(
            new MemoryStream(wire),
            receiver,
            liveRekeyEnabled: false);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await channel.ReceiveAsync());
        Assert.Equal<uint>(1, receiver.ReceiveEpoch);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await channel.ReceiveAsync());
        await channel.DisposeAsync();
    }

    [Fact]
    public async Task DuplicateLocalRekeyRequestsCoalesceAtOneTargetEpoch()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<DirectTcpPeerConnection> accept =
            DirectTcpPeerConnection.AcceptAsync(listener).AsTask();
        await using DirectTcpPeerConnection client =
            await DirectTcpPeerConnection.ConnectAsync(endpoint);
        await using DirectTcpPeerConnection server = await accept;
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        await using SecureControlChannel clientChannel =
            client.UpgradeToSecureControl(initiator, liveRekeyEnabled: true);
        await using SecureControlChannel serverChannel =
            server.UpgradeToSecureControl(responder, liveRekeyEnabled: true);
        using var cancellation = new CancellationTokenSource();
        Task clientReceive = clientChannel.ReceiveAsync(cancellation.Token).AsTask();
        Task serverReceive = serverChannel.ReceiveAsync(cancellation.Token).AsTask();

        Task first = clientChannel.RekeyAsync(TimeSpan.FromSeconds(2)).AsTask();
        Task duplicate = clientChannel.RekeyAsync(TimeSpan.FromSeconds(2)).AsTask();
        await Task.WhenAll(first, duplicate);

        Assert.Equal<uint>(2, initiator.SendEpoch);
        Assert.Equal<uint>(2, initiator.ReceiveEpoch);
        Assert.Equal<uint>(2, responder.SendEpoch);
        Assert.Equal<uint>(2, responder.ReceiveEpoch);
        cancellation.Cancel();
        await IgnoreFailureAsync(clientReceive);
        await IgnoreFailureAsync(serverReceive);
    }

    [Fact]
    public async Task LocalRekeyCoalescesWithAuthenticatedPeerRequestInProgress()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        using SecureFrameSession initiatorOwner = initiator;
        initiator.AdvanceSendEpoch(nextEpoch: 2);
        responder.AdvanceReceiveEpoch(nextEpoch: 2);
        var stream = new MemoryStream();
        await using var channel = new SecureControlChannel(
            stream,
            responder,
            liveRekeyEnabled: true);

        await channel.RekeyAsync(TimeSpan.FromSeconds(1));

        Assert.Equal<uint>(2, responder.SendEpoch);
        Assert.Equal<uint>(2, responder.ReceiveEpoch);
        byte[] wire = stream.ToArray();
        int frameLength = BinaryPrimitives.ReadInt32BigEndian(
            wire.AsSpan(0, sizeof(int)));
        Assert.Equal(sizeof(int) + frameLength, wire.Length);
        byte[] plaintext = initiator.Decrypt(
            wire.AsSpan(sizeof(int), frameLength));
        try
        {
            SecureSessionKeyUpdate response =
                SecureSessionKeyUpdateCodec.Decode(plaintext);
            Assert.False(response.RequestPeerUpdate);
            Assert.Equal<uint>(2, response.NextEpoch);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    [Fact]
    public async Task LegacyChannelFaultsAtUsageBoundWithoutSendingKeyUpdate()
    {
        (SecureFrameSession sender, SecureFrameSession receiver) =
            CreateBoundedSessions(maximumFramesPerEpoch: 2);
        using SecureFrameSession receiverOwner = receiver;
        var stream = new MemoryStream();
        var channel = new SecureControlChannel(
            stream,
            sender,
            liveRekeyEnabled: false);

        await channel.SendAsync(CreateMessage(1));
        await channel.SendAsync(CreateMessage(2));
        await Assert.ThrowsAsync<CryptographicException>(async () =>
            await channel.SendAsync(CreateMessage(3)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await channel.SendAsync(CreateMessage(4)));

        byte[] wire = stream.ToArray();
        int offset = 0;
        int applicationFrames = 0;
        while (offset < wire.Length)
        {
            int frameLength = BinaryPrimitives.ReadInt32BigEndian(
                wire.AsSpan(offset, sizeof(int)));
            offset += sizeof(int);
            byte[] plaintext = receiver.Decrypt(
                wire.AsSpan(offset, frameLength));
            try
            {
                Assert.False(SecureSessionKeyUpdateCodec.IsKeyUpdate(plaintext));
                _ = ControlMessageCodec.Decode(plaintext);
                applicationFrames++;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            offset += frameLength;
        }

        Assert.Equal(2, applicationFrames);
        await channel.DisposeAsync();
    }

    [Fact]
    public async Task DisposeInterruptsPendingReceiveBeforeWaitingForItsGate()
    {
        (SecureFrameSession unused, SecureFrameSession receiver) = CreateSessions();
        unused.Dispose();
        var stream = new DisposeAwareBlockingStream();
        var channel = new SecureControlChannel(stream, receiver);
        Task receive = channel.ReceiveAsync().AsTask();
        await stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Task dispose = channel.DisposeAsync().AsTask();

        try
        {
            await dispose.WaitAsync(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAnyAsync<Exception>(async () => await receive);
        }
        finally
        {
            stream.ReleaseRead.TrySetResult();
            await IgnoreFailureAsync(receive);
            await IgnoreFailureAsync(dispose);
            await channel.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeCannotDeadlockWithPeerKeyUpdateResponse()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        using SecureFrameSession initiatorOwner = initiator;
        byte[] plaintext = SecureSessionKeyUpdateCodec.Encode(
            SecureSessionKeyUpdate.Create(
                requestPeerUpdate: true,
                nextEpoch: 2));
        byte[] frame = initiator.Encrypt(plaintext);
        initiator.AdvanceSendEpoch(nextEpoch: 2);
        CryptographicOperations.ZeroMemory(plaintext);
        byte[] wire = new byte[sizeof(int) + frame.Length];
        BinaryPrimitives.WriteInt32BigEndian(wire, frame.Length);
        frame.CopyTo(wire, sizeof(int));
        CryptographicOperations.ZeroMemory(frame);
        var stream = new PausedFinalReadStream(wire);
        var channel = new SecureControlChannel(
            stream,
            responder,
            liveRekeyEnabled: true);
        using var cancellation = new CancellationTokenSource();
        Task receive = channel.ReceiveAsync(cancellation.Token).AsTask();
        await stream.FinalReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Task dispose = channel.DisposeAsync().AsTask();
        stream.ReleaseFinalRead.TrySetResult();

        try
        {
            await dispose.WaitAsync(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAnyAsync<Exception>(async () => await receive);
        }
        finally
        {
            cancellation.Cancel();
            stream.ReleaseFinalRead.TrySetResult();
            try
            {
                await IgnoreFailureAsync(receive);
                await IgnoreFailureAsync(dispose);
            }
            finally
            {
                await channel.DisposeAsync();
            }
        }
    }

    private static async Task IgnoreFailureAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception)
        {
        }
    }

    private static (SecureFrameSession Initiator, SecureFrameSession Responder)
        CreateSessions()
    {
        byte[] secret = Enumerable.Repeat((byte)0x33, 32).ToArray();
        byte[] transcriptHash = SHA256.HashData("rekey-lock-order"u8);
        try
        {
            using SecureSessionKeyMaterial material = SecureSessionKeyMaterial.Derive(
                secret,
                transcriptHash);
            return (
                material.CreateSession(SecureSessionRole.Initiator),
                material.CreateSession(SecureSessionRole.Responder));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(transcriptHash);
        }
    }

    private static (SecureFrameSession Initiator, SecureFrameSession Responder)
        CreateBoundedSessions(ulong maximumFramesPerEpoch)
    {
        byte[] initiatorKey = Enumerable.Repeat((byte)0x11, 32).ToArray();
        byte[] responderKey = Enumerable.Repeat((byte)0x22, 32).ToArray();
        byte[] sessionIdentifier = Enumerable.Repeat((byte)0x33, 16).ToArray();
        try
        {
            return (
                new SecureFrameSession(
                    initiatorKey,
                    SecureFrameDirection.InitiatorToResponder,
                    responderKey,
                    SecureFrameDirection.ResponderToInitiator,
                    sessionIdentifier,
                    maximumFramesPerEpoch),
                new SecureFrameSession(
                    responderKey,
                    SecureFrameDirection.ResponderToInitiator,
                    initiatorKey,
                    SecureFrameDirection.InitiatorToResponder,
                    sessionIdentifier,
                    maximumFramesPerEpoch));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(initiatorKey);
            CryptographicOperations.ZeroMemory(responderKey);
            CryptographicOperations.ZeroMemory(sessionIdentifier);
        }
    }

    private static ControlMessage CreateMessage(int sequence) => ControlMessage.Create(
        new ProtocolVersion(1, 2),
        ControlMessageType.Hello,
        Guid.Parse($"00000000-0000-0000-0000-{sequence:000000000000}"),
        CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
        new DateTimeOffset(2026, 7, 16, 12, 0, sequence, TimeSpan.Zero),
        TimeSpan.FromSeconds(30),
        $"{{\"sequence\":{sequence}}}");

    private sealed class PausedFinalReadStream(byte[] input) : Stream
    {
        private int offset;

        public TaskCompletionSource FinalReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFinalRead { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

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

        public override int Read(byte[] buffer, int bufferOffset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (offset == input.Length)
            {
                return 0;
            }

            int read = Math.Min(buffer.Length, input.Length - offset);
            input.AsMemory(offset, read).CopyTo(buffer);
            offset += read;
            if (offset == input.Length)
            {
                FinalReadStarted.TrySetResult();
                await ReleaseFinalRead.Task.WaitAsync(cancellationToken);
            }

            return read;
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class DisposeAwareBlockingStream : Stream
    {
        public TaskCompletionSource ReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseRead { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

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

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await ReleaseRead.Task.WaitAsync(cancellationToken);
            return 0;
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseRead.TrySetResult();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            ReleaseRead.TrySetResult();
            await base.DisposeAsync();
        }
    }
}
