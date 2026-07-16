using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedSessionFinishedExchangeTests
{
    [Theory]
    [InlineData(SecureSessionRole.Initiator)]
    [InlineData(SecureSessionRole.Responder)]
    public async Task FinishedSendFailureClosesTransportAndDestroysSessionKeys(
        SecureSessionRole localRole)
    {
        using FinishedSessionPair pair = FinishedSessionPair.Create();
        AuthenticatedSession localSession = pair.GetSession(localRole);
        var transport = new ScriptedHandshakeTransport
        {
            SendFailure = new IOException("Injected Finished send failure."),
        };
        if (localRole == SecureSessionRole.Responder)
        {
            transport.Enqueue(pair.CreatePeerFinishedFrame(localRole));
        }

        await Assert.ThrowsAsync<IOException>(async () =>
            await ConfirmAsync(localRole, transport, localSession, pair.Transcript));

        Assert.True(transport.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() =>
            localSession.SecureFrames.ExportSessionIdentifier());
    }

    [Fact]
    public async Task FinishedAndTransportCleanupFailuresPreserveBothCauses()
    {
        using FinishedSessionPair pair = FinishedSessionPair.Create();
        var sendFailure = new IOException("Injected Finished send failure.");
        var cleanupFailure = new IOException("Injected transport cleanup failure.");
        var transport = new ScriptedHandshakeTransport
        {
            SendFailure = sendFailure,
            DisposeFailure = cleanupFailure,
        };

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(
            async () => await AuthenticatedSessionFinishedExchange
                .ConfirmAsInitiatorAsync(
                    transport,
                    pair.Initiator,
                    pair.Transcript,
                    CancellationToken.None));

        Assert.Equal([sendFailure, cleanupFailure], failure.InnerExceptions);
        Assert.True(transport.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() =>
            pair.Initiator.SecureFrames.ExportSessionIdentifier());
    }

    [Theory]
    [InlineData(SecureSessionRole.Initiator, FinishedReceiveFault.Missing)]
    [InlineData(SecureSessionRole.Initiator, FinishedReceiveFault.Tampered)]
    [InlineData(SecureSessionRole.Initiator, FinishedReceiveFault.WrongBinding)]
    [InlineData(SecureSessionRole.Responder, FinishedReceiveFault.Missing)]
    [InlineData(SecureSessionRole.Responder, FinishedReceiveFault.Tampered)]
    [InlineData(SecureSessionRole.Responder, FinishedReceiveFault.WrongBinding)]
    public async Task FinishedReceiveFailureClosesBeforeControlUpgrade(
        SecureSessionRole localRole,
        FinishedReceiveFault fault)
    {
        using FinishedSessionPair pair = FinishedSessionPair.Create();
        AuthenticatedSession localSession = pair.GetSession(localRole);
        var transport = new ScriptedHandshakeTransport();
        if (fault == FinishedReceiveFault.Missing)
        {
            transport.ReceiveFailure = new EndOfStreamException(
                "Injected missing Finished frame.");
        }
        else
        {
            transport.Enqueue(pair.CreatePeerFinishedFrame(localRole, fault));
        }

        Exception failure = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await ConfirmAsync(localRole, transport, localSession, pair.Transcript));

        if (fault == FinishedReceiveFault.Missing)
        {
            Assert.IsType<EndOfStreamException>(failure);
        }
        else
        {
            var handshakeFailure = Assert.IsType<SessionHandshakeException>(failure);
            Assert.Equal(
                SessionHandshakeFailure.InvalidPeerFinished,
                handshakeFailure.Failure);
        }

        Assert.True(transport.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() =>
            localSession.SecureFrames.ExportSessionIdentifier());
    }

    private static ValueTask ConfirmAsync(
        SecureSessionRole localRole,
        IAuthenticatedHandshakeTransport transport,
        AuthenticatedSession authenticatedSession,
        SessionHandshakeTranscript transcript) => localRole switch
        {
            SecureSessionRole.Initiator =>
                AuthenticatedSessionFinishedExchange.ConfirmAsInitiatorAsync(
                    transport,
                    authenticatedSession,
                    transcript,
                    CancellationToken.None),
            SecureSessionRole.Responder =>
                AuthenticatedSessionFinishedExchange.ConfirmAsResponderAsync(
                    transport,
                    authenticatedSession,
                    transcript,
                    CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(localRole)),
        };

    public enum FinishedReceiveFault
    {
        None,
        Missing,
        Tampered,
        WrongBinding,
    }

    private sealed class ScriptedHandshakeTransport :
        IAuthenticatedHandshakeTransport
    {
        private readonly Queue<byte[]> received = [];

        public bool IsDisposed { get; private set; }

        public Exception? DisposeFailure { get; init; }

        public Exception? ReceiveFailure { get; set; }

        public Exception? SendFailure { get; init; }

        public void Enqueue(byte[] message) => received.Enqueue(message);

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return DisposeFailure is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(DisposeFailure);
        }

        public ValueTask<byte[]> ReceiveHandshakeAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ReceiveFailure is not null
                ? ValueTask.FromException<byte[]>(ReceiveFailure)
                : ValueTask.FromResult(received.Dequeue());
        }

        public ValueTask SendHandshakeAsync(
            ReadOnlyMemory<byte> message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return SendFailure is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(SendFailure);
        }
    }

    private sealed class FinishedSessionPair : IDisposable
    {
        private readonly DeviceIdentity initiatorIdentity;
        private readonly DeviceIdentity responderIdentity;

        private FinishedSessionPair(
            DeviceIdentity initiatorIdentity,
            DeviceIdentity responderIdentity,
            SessionHandshakeTranscript transcript,
            AuthenticatedSession initiator,
            AuthenticatedSession responder)
        {
            this.initiatorIdentity = initiatorIdentity;
            this.responderIdentity = responderIdentity;
            Transcript = transcript;
            Initiator = initiator;
            Responder = responder;
        }

        public AuthenticatedSession Initiator { get; }

        public AuthenticatedSession Responder { get; }

        public SessionHandshakeTranscript Transcript { get; }

        public static FinishedSessionPair Create()
        {
            DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
                DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
                "Laptop");
            DeviceIdentity responderIdentity = DeviceIdentity.Generate(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Desk");
            try
            {
                using EphemeralKeyAgreement initiatorAgreement =
                    EphemeralKeyAgreement.Generate();
                using EphemeralKeyAgreement responderAgreement =
                    EphemeralKeyAgreement.Generate();
                ProtocolVersion version =
                    ProtocolFeatures.SecureSessionFinishedMinimumVersion;
                SessionHandshakeHello initiatorHello = SessionHandshakeHello.Create(
                    SecureSessionRole.Initiator,
                    initiatorIdentity.PublicIdentity,
                    [version],
                    initiatorAgreement.ExportSubjectPublicKeyInfo(),
                    Enumerable.Repeat(
                        (byte)0x11,
                        SessionHandshakeHello.NonceLength).ToArray());
                SessionHandshakeHello responderHello = SessionHandshakeHello.Create(
                    SecureSessionRole.Responder,
                    responderIdentity.PublicIdentity,
                    [version],
                    responderAgreement.ExportSubjectPublicKeyInfo(),
                    Enumerable.Repeat(
                        (byte)0x22,
                        SessionHandshakeHello.NonceLength).ToArray());
                SessionHandshakeTranscript transcript =
                    SessionHandshakeTranscript.Create(
                        initiatorHello,
                        responderHello);
                SessionHandshakeAuthentication initiatorAuthentication =
                    SessionHandshakeAuthentication.Create(
                        transcript,
                        initiatorIdentity);
                SessionHandshakeAuthentication responderAuthentication =
                    SessionHandshakeAuthentication.Create(
                        transcript,
                        responderIdentity);
                AuthenticatedSession initiator =
                    AuthenticatedSessionHandshake.Complete(
                        transcript,
                        SecureSessionRole.Initiator,
                        initiatorIdentity.PublicIdentity,
                        responderIdentity.PublicIdentity,
                        initiatorAgreement,
                        responderAuthentication);
                AuthenticatedSession responder =
                    AuthenticatedSessionHandshake.Complete(
                        transcript,
                        SecureSessionRole.Responder,
                        responderIdentity.PublicIdentity,
                        initiatorIdentity.PublicIdentity,
                        responderAgreement,
                        initiatorAuthentication);
                return new FinishedSessionPair(
                    initiatorIdentity,
                    responderIdentity,
                    transcript,
                    initiator,
                    responder);
            }
            catch
            {
                initiatorIdentity.Dispose();
                responderIdentity.Dispose();
                throw;
            }
        }

        public byte[] CreatePeerFinishedFrame(
            SecureSessionRole localRole,
            FinishedReceiveFault fault = FinishedReceiveFault.None)
        {
            SecureSessionRole peerRole = localRole switch
            {
                SecureSessionRole.Initiator => SecureSessionRole.Responder,
                SecureSessionRole.Responder => SecureSessionRole.Initiator,
                _ => throw new ArgumentOutOfRangeException(nameof(localRole)),
            };
            AuthenticatedSession peer = GetSession(peerRole);
            byte[] transcriptHash = Transcript.ExportHash();
            byte[] sessionIdentifier =
                peer.SecureFrames.ExportSessionIdentifier();
            byte[]? plaintext = null;
            try
            {
                if (fault == FinishedReceiveFault.WrongBinding)
                {
                    sessionIdentifier[0] ^= 0x01;
                }

                SessionHandshakeFinished finished = SessionHandshakeFinished.Create(
                    peerRole,
                    transcriptHash,
                    sessionIdentifier);
                plaintext = SessionHandshakeWireCodec.EncodeFinished(finished);
                byte[] encrypted = peer.SecureFrames.Encrypt(plaintext);
                if (fault == FinishedReceiveFault.Tampered)
                {
                    encrypted[^1] ^= 0x01;
                }

                return encrypted;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(transcriptHash);
                CryptographicOperations.ZeroMemory(sessionIdentifier);
                if (plaintext is not null)
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
        }

        public AuthenticatedSession GetSession(SecureSessionRole role) => role switch
        {
            SecureSessionRole.Initiator => Initiator,
            SecureSessionRole.Responder => Responder,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

        public void Dispose()
        {
            Initiator.Dispose();
            Responder.Dispose();
            initiatorIdentity.Dispose();
            responderIdentity.Dispose();
        }
    }
}
