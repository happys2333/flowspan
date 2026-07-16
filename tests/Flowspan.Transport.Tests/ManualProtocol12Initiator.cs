using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public enum ManualInitiatorFinishedBehavior
{
    Omit,
    Tamper,
    WrongBinding,
}

internal static class ManualProtocol12Initiator
{
    public static async Task RunUntilServerClosesAsync(
        IPEndPoint endpoint,
        DeviceIdentity initiatorIdentity,
        PublicDeviceIdentity responderIdentity,
        ManualInitiatorFinishedBehavior behavior,
        CancellationToken cancellationToken)
    {
        await using DirectTcpPeerConnection connection =
            await DirectTcpPeerConnection.ConnectAsync(endpoint, cancellationToken);
        using EphemeralKeyAgreement agreement = EphemeralKeyAgreement.Generate();
        ProtocolVersion version =
            ProtocolFeatures.SecureSessionFinishedMinimumVersion;
        SessionHandshakeHello initiatorHello = SessionHandshakeHello.Create(
            SecureSessionRole.Initiator,
            initiatorIdentity.PublicIdentity,
            [version],
            agreement.ExportSubjectPublicKeyInfo(),
            Enumerable.Repeat(
                (byte)0x11,
                SessionHandshakeHello.NonceLength).ToArray());
        await SendHandshakeAsync(
            connection,
            SessionHandshakeWireCodec.EncodeHello(initiatorHello),
            cancellationToken);

        byte[] responderHelloMessage =
            await connection.ReceiveHandshakeAsync(cancellationToken);
        SessionHandshakeHello responderHello;
        try
        {
            responderHello = SessionHandshakeWireCodec.DecodeHello(
                responderHelloMessage,
                responderIdentity);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(responderHelloMessage);
        }

        SessionHandshakeTranscript transcript = SessionHandshakeTranscript.Create(
            initiatorHello,
            responderHello);
        SessionHandshakeAuthentication initiatorAuthentication =
            SessionHandshakeAuthentication.Create(transcript, initiatorIdentity);
        await SendHandshakeAsync(
            connection,
            SessionHandshakeWireCodec.EncodeAuthentication(
                initiatorAuthentication),
            cancellationToken);

        byte[] responderAuthenticationMessage =
            await connection.ReceiveHandshakeAsync(cancellationToken);
        SessionHandshakeAuthentication responderAuthentication;
        try
        {
            responderAuthentication =
                SessionHandshakeWireCodec.DecodeAuthentication(
                    responderAuthenticationMessage);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(responderAuthenticationMessage);
        }

        using AuthenticatedSession authenticated =
            AuthenticatedSessionHandshake.Complete(
                transcript,
                SecureSessionRole.Initiator,
                initiatorIdentity.PublicIdentity,
                responderIdentity,
                agreement,
                responderAuthentication);
        if (behavior != ManualInitiatorFinishedBehavior.Omit)
        {
            await SendInvalidFinishedAsync(
                connection,
                authenticated,
                transcript,
                behavior,
                cancellationToken);
        }

        await RequireServerCloseAsync(connection, cancellationToken);
    }

    private static async Task SendInvalidFinishedAsync(
        DirectTcpPeerConnection connection,
        AuthenticatedSession authenticated,
        SessionHandshakeTranscript transcript,
        ManualInitiatorFinishedBehavior behavior,
        CancellationToken cancellationToken)
    {
        byte[] transcriptHash = transcript.ExportHash();
        byte[] sessionIdentifier =
            authenticated.SecureFrames.ExportSessionIdentifier();
        byte[]? plaintext = null;
        byte[]? encrypted = null;
        try
        {
            if (behavior == ManualInitiatorFinishedBehavior.WrongBinding)
            {
                sessionIdentifier[0] ^= 0x01;
            }

            SessionHandshakeFinished finished = SessionHandshakeFinished.Create(
                SecureSessionRole.Initiator,
                transcriptHash,
                sessionIdentifier);
            plaintext = SessionHandshakeWireCodec.EncodeFinished(finished);
            encrypted = authenticated.SecureFrames.Encrypt(plaintext);
            if (behavior == ManualInitiatorFinishedBehavior.Tamper)
            {
                encrypted[^1] ^= 0x01;
            }

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

    private static async Task RequireServerCloseAsync(
        DirectTcpPeerConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] unexpected =
                await connection.ReceiveHandshakeAsync(cancellationToken);
            CryptographicOperations.ZeroMemory(unexpected);
            throw new InvalidOperationException(
                "The responder sent a handshake frame after invalid Finished input.");
        }
        catch (Exception exception) when (exception is
            EndOfStreamException
            or IOException
            or SocketException)
        {
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
}
