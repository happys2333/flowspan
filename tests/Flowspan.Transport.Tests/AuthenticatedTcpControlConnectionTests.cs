using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedTcpControlConnectionTests
{
    [Fact]
    public async Task ProtocolOnePointTwoConfirmsKeysBeforeControlUpgrade()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var initiatorTrust = new TrustRecord(
            responderIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        var responderTrust = new TrustRecord(
            initiatorIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        ProtocolVersion version = ProtocolFeatures.SecureSessionFinishedMinimumVersion;
        Task<AuthenticatedTcpControlConnection> accept =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                responderIdentity,
                responderTrust,
                [version]).AsTask();

        await using AuthenticatedTcpControlConnection initiator =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                initiatorIdentity,
                initiatorTrust,
                [version]);
        await using AuthenticatedTcpControlConnection responder = await accept;

        Assert.Equal(version, initiator.ProtocolVersion);
        Assert.Equal<ulong>(1, initiator.NextSecureSendSequence);
        Assert.Equal<ulong>(1, initiator.NextSecureReceiveSequence);
        Assert.Equal<ulong>(1, responder.NextSecureSendSequence);
        Assert.Equal<ulong>(1, responder.NextSecureReceiveSequence);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await initiator.RekeyAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task ProtocolOnePointThreeRepeatedAndCrossedRekeysKeepTrafficBound()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var initiatorTrust = new TrustRecord(
            responderIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        var responderTrust = new TrustRecord(
            initiatorIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        ProtocolVersion version = ProtocolFeatures.SecureSessionRekeyMinimumVersion;
        Task<AuthenticatedTcpControlConnection> accept =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                responderIdentity,
                responderTrust,
                [version]).AsTask();

        await using AuthenticatedTcpControlConnection initiator =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                initiatorIdentity,
                initiatorTrust,
                [version]);
        await using AuthenticatedTcpControlConnection responder = await accept;
        for (uint epoch = 2; epoch <= 4; epoch++)
        {
            Task<ControlMessage> responderReceive;
            Task<ControlMessage> initiatorReceive;
            if (epoch == 3)
            {
                Task initiatorRekey = initiator.RekeyAsync(
                    TimeSpan.FromSeconds(2)).AsTask();
                Task responderRekey = responder.RekeyAsync(
                    TimeSpan.FromSeconds(2)).AsTask();
                await WaitForSendEpochAsync(
                    initiator,
                    initiatorRekey,
                    expectedEpoch: epoch);
                await WaitForSendEpochAsync(
                    responder,
                    responderRekey,
                    expectedEpoch: epoch);
                responderReceive = responder.ReceiveAsync().AsTask();
                initiatorReceive = initiator.ReceiveAsync().AsTask();
                await Task.WhenAll(initiatorRekey, responderRekey);
            }
            else
            {
                responderReceive = responder.ReceiveAsync().AsTask();
                initiatorReceive = initiator.ReceiveAsync().AsTask();
                if (epoch == 2)
                {
                    await initiator.RekeyAsync(TimeSpan.FromSeconds(2));
                }
                else
                {
                    await responder.RekeyAsync(TimeSpan.FromSeconds(2));
                }
            }

            await WaitForSendEpochAsync(
                initiator,
                Task.CompletedTask,
                expectedEpoch: epoch);
            await WaitForSendEpochAsync(
                responder,
                Task.CompletedTask,
                expectedEpoch: epoch);
            Assert.Equal(epoch, initiator.SecureSendEpoch);
            Assert.Equal(epoch, initiator.SecureReceiveEpoch);
            Assert.Equal(epoch, responder.SecureSendEpoch);
            Assert.Equal(epoch, responder.SecureReceiveEpoch);
            ControlMessage request = ControlMessage.Create(
                version,
                ControlMessageType.Hello,
                Guid.Parse($"00000000-0000-0000-0000-{epoch:000000000000}"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                initiatorIdentity.DeviceId,
                new DateTimeOffset(2026, 7, 16, 12, 0, checked((int)epoch), TimeSpan.Zero),
                TimeSpan.FromSeconds(30),
                $"{{\"epoch\":{epoch}}}");
            ControlMessage response = ControlMessage.Create(
                version,
                ControlMessageType.Hello,
                Guid.Parse($"00000000-0000-0000-0001-{epoch:000000000000}"),
                CorrelationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                responderIdentity.DeviceId,
                new DateTimeOffset(2026, 7, 16, 12, 1, checked((int)epoch), TimeSpan.Zero),
                TimeSpan.FromSeconds(30),
                $"{{\"epoch\":{epoch}}}");
            await initiator.SendAsync(request);
            await responder.SendAsync(response);

            Assert.Equal(request.BodyDigest, (await responderReceive).BodyDigest);
            Assert.Equal(response.BodyDigest, (await initiatorReceive).BodyDigest);
        }
    }

    [Fact]
    public async Task InterruptedProtocolOnePointThreeRekeyRequiresFreshHandshake()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var initiatorTrust = new TrustRecord(
            responderIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        var responderTrust = new TrustRecord(
            initiatorIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        ProtocolVersion version = ProtocolFeatures.SecureSessionRekeyMinimumVersion;

        Task<AuthenticatedTcpControlConnection> firstAccept =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                responderIdentity,
                responderTrust,
                [version]).AsTask();
        await using (AuthenticatedTcpControlConnection firstInitiator =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                initiatorIdentity,
                initiatorTrust,
                [version]))
        await using (AuthenticatedTcpControlConnection firstResponder =
            await firstAccept)
        {
            Task failedReceive = firstInitiator.ReceiveAsync().AsTask();
            Task failedRekey = firstInitiator.RekeyAsync(
                TimeSpan.FromSeconds(2)).AsTask();
            await WaitForSendEpochAsync(
                firstInitiator,
                failedRekey,
                expectedEpoch: 2);

            await firstResponder.DisposeAsync();

            await Assert.ThrowsAnyAsync<IOException>(async () =>
                await failedReceive);
            await Assert.ThrowsAnyAsync<IOException>(async () =>
                await failedRekey);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await firstInitiator.SendAsync(CreateMessage(
                    version,
                    initiatorIdentity.DeviceId,
                    "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                    "{\"stale\":true}")));
        }

        Task<AuthenticatedTcpControlConnection> freshAccept =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                responderIdentity,
                responderTrust,
                [version]).AsTask();
        await using AuthenticatedTcpControlConnection freshInitiator =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                initiatorIdentity,
                initiatorTrust,
                [version]);
        await using AuthenticatedTcpControlConnection freshResponder =
            await freshAccept;

        Assert.Equal<uint>(1, freshInitiator.SecureSendEpoch);
        Assert.Equal<uint>(1, freshInitiator.SecureReceiveEpoch);
        Assert.Equal<uint>(1, freshResponder.SecureSendEpoch);
        Assert.Equal<uint>(1, freshResponder.SecureReceiveEpoch);
        Assert.Equal<ulong>(1, freshInitiator.NextSecureSendSequence);
        Assert.Equal<ulong>(1, freshResponder.NextSecureReceiveSequence);
        ControlMessage freshMessage = CreateMessage(
            version,
            initiatorIdentity.DeviceId,
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            "{\"freshHandshake\":true}");
        Task<ControlMessage> freshReceive = freshResponder.ReceiveAsync().AsTask();
        await freshInitiator.SendAsync(freshMessage);
        Assert.Equal(freshMessage.BodyDigest, (await freshReceive).BodyDigest);
    }

    [Fact]
    public async Task TrustedPeersEstablishVersionAndIdentityBoundEncryptedChannel()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var initiatorTrust = new TrustRecord(
            responderIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        var responderTrust = new TrustRecord(
            initiatorIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accept =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                responderIdentity,
                responderTrust,
                [new ProtocolVersion(1, 1)]).AsTask();

        await using AuthenticatedTcpControlConnection initiator =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                initiatorIdentity,
                initiatorTrust,
                [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)]);
        await using AuthenticatedTcpControlConnection responder = await accept;
        Assert.Equal<ulong>(0, initiator.NextSecureSendSequence);
        Assert.Equal<ulong>(0, initiator.NextSecureReceiveSequence);
        Assert.Equal<ulong>(0, responder.NextSecureSendSequence);
        Assert.Equal<ulong>(0, responder.NextSecureReceiveSequence);
        ControlMessage request = ControlMessage.Create(
            new ProtocolVersion(1, 1),
            ControlMessageType.Hello,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            initiatorIdentity.DeviceId,
            new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero),
            TimeSpan.FromSeconds(30),
            "{\"authenticated\":true}");

        await initiator.SendAsync(request);
        ControlMessage received = await responder.ReceiveAsync();

        Assert.Equal(new ProtocolVersion(1, 1), initiator.ProtocolVersion);
        Assert.Equal(initiator.ProtocolVersion, responder.ProtocolVersion);
        Assert.Equal(responderIdentity.DeviceId, initiator.PeerIdentity.DeviceId);
        Assert.Equal(initiatorIdentity.DeviceId, responder.PeerIdentity.DeviceId);
        Assert.Equal(request.BodyDigest, received.BodyDigest);

        ControlMessage wrongVersion = ControlMessage.Create(
            new ProtocolVersion(1, 0),
            ControlMessageType.Hello,
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            initiatorIdentity.DeviceId,
            new DateTimeOffset(2026, 7, 13, 8, 0, 1, TimeSpan.Zero),
            TimeSpan.FromSeconds(30),
            "{\"wrongVersion\":true}");
        ControlMessage wrongSender = ControlMessage.Create(
            new ProtocolVersion(1, 1),
            ControlMessageType.Hello,
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            responderIdentity.DeviceId,
            new DateTimeOffset(2026, 7, 13, 8, 0, 1, TimeSpan.Zero),
            TimeSpan.FromSeconds(30),
            "{\"wrongSender\":true}");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await initiator.SendAsync(wrongVersion));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await initiator.SendAsync(wrongSender));
    }

    [Fact]
    public async Task TrustedDeviceIdWithChangedKeyCannotUpgradeConnection()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        DeviceId responderId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        using DeviceIdentity trustedResponder = DeviceIdentity.Generate(
            responderId,
            "Desk");
        using DeviceIdentity changedResponder = DeviceIdentity.Generate(
            responderId,
            "Desk");
        var initiatorTrust = new TrustRecord(
            trustedResponder.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        var responderTrust = new TrustRecord(
            initiatorIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<AuthenticatedTcpControlConnection> accept =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                changedResponder,
                responderTrust,
                [new ProtocolVersion(1, 0)],
                timeout.Token).AsTask();

        SessionHandshakeException exception =
            await Assert.ThrowsAsync<SessionHandshakeException>(async () =>
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    initiatorIdentity,
                    initiatorTrust,
                    [new ProtocolVersion(1, 0)],
                    timeout.Token));
        Exception? responderFailure = await Record.ExceptionAsync(async () =>
            await accept);

        Assert.Equal(SessionHandshakeFailure.PeerIdentityChanged, exception.Failure);
        Assert.NotNull(responderFailure);
    }

    [Fact]
    public async Task ProtocolOnePointTwoMissingFinishedTimesOutBeforeUpgrade()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var initiatorTrust = new TrustRecord(
            responderIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        ProtocolVersion version = ProtocolFeatures.SecureSessionFinishedMinimumVersion;
        using var stopResponder = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var initiatorFinishedReceived = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task responder = RunManualResponderAsync(
            listener,
            responderIdentity,
            initiatorIdentity.PublicIdentity,
            version,
            ManualFinishedResponse.Omit,
            initiatorFinishedReceived,
            stopResponder.Token);
        Task<AuthenticatedTcpControlConnection> connecting =
            AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                initiatorIdentity,
                initiatorTrust,
                [version],
                TimeSpan.FromSeconds(1)).AsTask();

        await initiatorFinishedReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await connecting);

        stopResponder.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await responder);
    }

    [Fact]
    public async Task ProtocolOnePointTwoTamperedFinishedFailsBeforeUpgrade()
    {
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var initiatorTrust = new TrustRecord(
            responderIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        ProtocolVersion version = ProtocolFeatures.SecureSessionFinishedMinimumVersion;
        Task responder = RunManualResponderAsync(
            listener,
            responderIdentity,
            initiatorIdentity.PublicIdentity,
            version,
            ManualFinishedResponse.Tamper,
            finishedReceived: null,
            cancellationToken: CancellationToken.None);

        SessionHandshakeException failure =
            await Assert.ThrowsAsync<SessionHandshakeException>(async () =>
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    initiatorIdentity,
                    initiatorTrust,
                    [version],
                    TimeSpan.FromSeconds(2)));
        await responder;

        Assert.Equal(SessionHandshakeFailure.InvalidPeerFinished, failure.Failure);
    }

    [Fact]
    public async Task SilentConnectedPeerCannotHoldHandshakeOpenIndefinitely()
    {
        using DeviceIdentity expectedPeer = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        using DeviceIdentity responderIdentity = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Desk");
        var responderTrust = new TrustRecord(
            expectedPeer.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accept =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                responderIdentity,
                responderTrust,
                [new ProtocolVersion(1, 0)],
                TimeSpan.FromMilliseconds(100)).AsTask();
        using var silentPeer = new TcpClient(AddressFamily.InterNetwork);
        await silentPeer.ConnectAsync(endpoint);

        await Assert.ThrowsAsync<TimeoutException>(async () => await accept);
    }

    private static async Task RunManualResponderAsync(
        TcpListener listener,
        DeviceIdentity responderIdentity,
        PublicDeviceIdentity initiatorIdentity,
        ProtocolVersion version,
        ManualFinishedResponse response,
        TaskCompletionSource? finishedReceived,
        CancellationToken cancellationToken)
    {
        await using DirectTcpPeerConnection connection =
            await DirectTcpPeerConnection.AcceptAsync(listener, cancellationToken);
        using EphemeralKeyAgreement agreement = EphemeralKeyAgreement.Generate();
        byte[] initiatorHelloMessage =
            await connection.ReceiveHandshakeAsync(cancellationToken);
        SessionHandshakeHello initiatorHello;
        try
        {
            initiatorHello = SessionHandshakeWireCodec.DecodeHello(
                initiatorHelloMessage,
                initiatorIdentity);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(initiatorHelloMessage);
        }

        SessionHandshakeHello responderHello = SessionHandshakeHello.Create(
            SecureSessionRole.Responder,
            responderIdentity.PublicIdentity,
            [version],
            agreement.ExportSubjectPublicKeyInfo(),
            Enumerable.Repeat((byte)0x22, SessionHandshakeHello.NonceLength)
                .ToArray());
        await SendHandshakeAsync(
            connection,
            SessionHandshakeWireCodec.EncodeHello(responderHello),
            cancellationToken);
        SessionHandshakeTranscript transcript = SessionHandshakeTranscript.Create(
            initiatorHello,
            responderHello);
        byte[] initiatorAuthenticationMessage =
            await connection.ReceiveHandshakeAsync(cancellationToken);
        SessionHandshakeAuthentication initiatorAuthentication;
        try
        {
            initiatorAuthentication =
                SessionHandshakeWireCodec.DecodeAuthentication(
                    initiatorAuthenticationMessage);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(initiatorAuthenticationMessage);
        }

        using AuthenticatedSession authenticated =
            AuthenticatedSessionHandshake.Complete(
                transcript,
                SecureSessionRole.Responder,
                responderIdentity.PublicIdentity,
                initiatorIdentity,
                agreement,
                initiatorAuthentication);
        SessionHandshakeAuthentication responderAuthentication =
            SessionHandshakeAuthentication.Create(transcript, responderIdentity);
        await SendHandshakeAsync(
            connection,
            SessionHandshakeWireCodec.EncodeAuthentication(responderAuthentication),
            cancellationToken);
        await ReceiveAndVerifyInitiatorFinishedAsync(
            connection,
            authenticated,
            transcript,
            cancellationToken);
        finishedReceived?.TrySetResult();

        if (response == ManualFinishedResponse.Omit)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return;
        }

        byte[] transcriptHash = transcript.ExportHash();
        byte[] sessionIdentifier =
            authenticated.SecureFrames.ExportSessionIdentifier();
        byte[]? plaintext = null;
        byte[]? encrypted = null;
        try
        {
            SessionHandshakeFinished finished = SessionHandshakeFinished.Create(
                SecureSessionRole.Responder,
                transcriptHash,
                sessionIdentifier);
            plaintext = SessionHandshakeWireCodec.EncodeFinished(finished);
            encrypted = authenticated.SecureFrames.Encrypt(plaintext);
            encrypted[^1] ^= 0x01;
            await connection.SendHandshakeAsync(encrypted, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(transcriptHash);
            CryptographicOperations.ZeroMemory(sessionIdentifier);
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            if (encrypted is not null)
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }
        }
    }

    private static async Task ReceiveAndVerifyInitiatorFinishedAsync(
        DirectTcpPeerConnection connection,
        AuthenticatedSession authenticated,
        SessionHandshakeTranscript transcript,
        CancellationToken cancellationToken)
    {
        byte[] encrypted = await connection.ReceiveHandshakeAsync(cancellationToken);
        byte[]? plaintext = null;
        byte[]? transcriptHash = null;
        byte[]? sessionIdentifier = null;
        try
        {
            plaintext = authenticated.SecureFrames.Decrypt(encrypted);
            SessionHandshakeFinished finished =
                SessionHandshakeWireCodec.DecodeFinished(plaintext);
            transcriptHash = transcript.ExportHash();
            sessionIdentifier =
                authenticated.SecureFrames.ExportSessionIdentifier();
            Assert.True(finished.Matches(
                SecureSessionRole.Initiator,
                transcriptHash,
                sessionIdentifier));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            if (transcriptHash is not null)
            {
                CryptographicOperations.ZeroMemory(transcriptHash);
            }

            if (sessionIdentifier is not null)
            {
                CryptographicOperations.ZeroMemory(sessionIdentifier);
            }
        }
    }

    private static async Task SendHandshakeAsync(
        DirectTcpPeerConnection connection,
        byte[] message,
        CancellationToken cancellationToken)
    {
        try
        {
            await connection.SendHandshakeAsync(message, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(message);
        }
    }

    private static ControlMessage CreateMessage(
        ProtocolVersion version,
        DeviceId senderDeviceId,
        string messageId,
        string bodyJson) => ControlMessage.Create(
            version,
            ControlMessageType.Hello,
            Guid.Parse(messageId),
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            senderDeviceId,
            new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero),
            TimeSpan.FromSeconds(30),
            bodyJson);

    private static async Task WaitForSendEpochAsync(
        AuthenticatedTcpControlConnection connection,
        Task rekey,
        uint expectedEpoch)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            while (connection.SecureSendEpoch != expectedEpoch)
            {
                if (rekey.IsCompleted)
                {
                    await rekey;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(1), timeout.Token);
            }
        }
        catch (OperationCanceledException exception)
            when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The rekey did not commit send epoch {expectedEpoch} before the test deadline.",
                exception);
        }
    }

    private enum ManualFinishedResponse
    {
        Omit,
        Tamper,
    }
}
