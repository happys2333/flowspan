using System.Security.Cryptography;
using Flowspan.Security;

namespace Flowspan.Transport;

internal static class AuthenticatedSessionFinishedExchange
{
    public static ValueTask ConfirmAsInitiatorAsync(
        IAuthenticatedHandshakeTransport transport,
        AuthenticatedSession authenticated,
        SessionHandshakeTranscript transcript,
        CancellationToken cancellationToken) => ConfirmAsync(
            transport,
            authenticated,
            transcript,
            SecureSessionRole.Initiator,
            SecureSessionRole.Responder,
            sendFirst: true,
            cancellationToken);

    public static ValueTask ConfirmAsResponderAsync(
        IAuthenticatedHandshakeTransport transport,
        AuthenticatedSession authenticated,
        SessionHandshakeTranscript transcript,
        CancellationToken cancellationToken) => ConfirmAsync(
            transport,
            authenticated,
            transcript,
            SecureSessionRole.Responder,
            SecureSessionRole.Initiator,
            sendFirst: false,
            cancellationToken);

    private static async ValueTask ConfirmAsync(
        IAuthenticatedHandshakeTransport transport,
        AuthenticatedSession authenticated,
        SessionHandshakeTranscript transcript,
        SecureSessionRole localRole,
        SecureSessionRole expectedPeerRole,
        bool sendFirst,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(authenticated);
        ArgumentNullException.ThrowIfNull(transcript);
        try
        {
            if (sendFirst)
            {
                await SendFinishedAsync(
                    transport,
                    authenticated,
                    transcript,
                    localRole,
                    cancellationToken).ConfigureAwait(false);
                await ReceiveAndVerifyFinishedAsync(
                    transport,
                    authenticated,
                    transcript,
                    expectedPeerRole,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            await ReceiveAndVerifyFinishedAsync(
                transport,
                authenticated,
                transcript,
                expectedPeerRole,
                cancellationToken).ConfigureAwait(false);
            await SendFinishedAsync(
                transport,
                authenticated,
                transcript,
                localRole,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            authenticated.Dispose();
            try
            {
                await transport.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Finished confirmation and transport cleanup both failed.",
                    failure,
                    cleanupFailure);
            }

            throw;
        }
    }

    private static async ValueTask SendFinishedAsync(
        IAuthenticatedHandshakeTransport transport,
        AuthenticatedSession authenticated,
        SessionHandshakeTranscript transcript,
        SecureSessionRole localRole,
        CancellationToken cancellationToken)
    {
        byte[] transcriptHash = transcript.ExportHash();
        byte[] sessionIdentifier =
            authenticated.SecureFrames.ExportSessionIdentifier();
        byte[]? plaintext = null;
        byte[]? encrypted = null;
        try
        {
            SessionHandshakeFinished finished = SessionHandshakeFinished.Create(
                localRole,
                transcriptHash,
                sessionIdentifier);
            plaintext = SessionHandshakeWireCodec.EncodeFinished(finished);
            encrypted = authenticated.SecureFrames.Encrypt(plaintext);
            await transport.SendHandshakeAsync(encrypted, cancellationToken)
                .ConfigureAwait(false);
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

    private static async ValueTask ReceiveAndVerifyFinishedAsync(
        IAuthenticatedHandshakeTransport transport,
        AuthenticatedSession authenticated,
        SessionHandshakeTranscript transcript,
        SecureSessionRole expectedPeerRole,
        CancellationToken cancellationToken)
    {
        byte[] encrypted = await transport.ReceiveHandshakeAsync(cancellationToken)
            .ConfigureAwait(false);
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
            if (!finished.Matches(
                expectedPeerRole,
                transcriptHash,
                sessionIdentifier))
            {
                throw new SessionHandshakeException(
                    SessionHandshakeFailure.InvalidPeerFinished,
                    "The peer Finished message does not match the authenticated session.");
            }
        }
        catch (SessionHandshakeException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            CryptographicException
            or InvalidDataException
            or ArgumentException)
        {
            throw new SessionHandshakeException(
                SessionHandshakeFailure.InvalidPeerFinished,
                "The peer Finished message could not be authenticated.",
                exception);
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
}
