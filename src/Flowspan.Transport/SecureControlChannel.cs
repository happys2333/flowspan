using System.Buffers.Binary;
using System.Security.Cryptography;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Transport;

public sealed class SecureControlChannel : IAsyncDisposable
{
    public const int MaximumEncryptedFrameBytes =
        ControlMessageCodec.MaximumFrameBytes + 128;

    public static TimeSpan MaximumRekeyTimeout { get; } = TimeSpan.FromMinutes(2);

    public static TimeSpan DefaultRekeyTimeout { get; } = TimeSpan.FromSeconds(10);

    private const int LengthPrefixBytes = sizeof(int);
    private readonly SemaphoreSlim applicationSendGate = new(1, 1);
    private readonly bool liveRekeyEnabled;
    private readonly SemaphoreSlim receiveGate = new(1, 1);
    private readonly Lock rekeyGate = new();
    private readonly SecureFrameSession session;
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly Stream stream;
    private readonly TimeProvider timeProvider;
    private int disposed;
    private int faulted;
    private TaskCompletionSource? pendingRekeyCompletion;
    private uint? pendingRekeyEpoch;

    public SecureControlChannel(
        Stream stream,
        SecureFrameSession session,
        bool liveRekeyEnabled = false,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(session);
        if (!stream.CanRead || !stream.CanWrite)
        {
            throw new ArgumentException(
                "A secure control channel requires a readable and writable stream.",
                nameof(stream));
        }

        this.stream = stream;
        this.session = session;
        this.liveRekeyEnabled = liveRekeyEnabled;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask RekeyAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        if (!liveRekeyEnabled)
        {
            throw new InvalidOperationException(
                "Live rekey was not negotiated for this secure control channel.");
        }

        if (timeout <= TimeSpan.Zero || timeout > MaximumRekeyTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                $"A live rekey timeout must be positive and at most {MaximumRekeyTimeout}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var deadlineCancellation =
            new CancellationTokenSource(timeout, timeProvider);
        using CancellationTokenSource operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                deadlineCancellation.Token);
        CancellationToken operationToken = operationCancellation.Token;
        Task completion;
        uint targetEpoch;
        bool sendRequest;
        lock (rekeyGate)
        {
            ThrowIfUnavailable();
            if (pendingRekeyEpoch is uint pending)
            {
                targetEpoch = pending;
                completion = pendingRekeyCompletion!.Task;
                sendRequest = false;
            }
            else
            {
                if (session.SendEpoch != session.ReceiveEpoch
                    || session.SendEpoch == uint.MaxValue)
                {
                    throw new InvalidOperationException(
                        "A new live rekey requires matching non-exhausted local epochs.");
                }

                targetEpoch = session.SendEpoch + 1;
                pendingRekeyEpoch = targetEpoch;
                pendingRekeyCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                completion = pendingRekeyCompletion.Task;
                sendRequest = true;
            }
        }

        try
        {
            if (sendRequest)
            {
                await SendKeyUpdateIfBehindAsync(
                    requestPeerUpdate: true,
                    targetEpoch,
                    operationToken).ConfigureAwait(false);
                TryCompletePendingRekey();
            }

            await completion.WaitAsync(operationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            bool deadlineExpired = deadlineCancellation.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested;
            Exception failure = deadlineExpired
                ? new TimeoutException(
                    "Live rekey did not complete before the deadline.",
                    exception)
                : exception;
            Exception finalFailure = Fault(failure);
            if (ReferenceEquals(finalFailure, exception))
            {
                throw;
            }

            throw finalFailure;
        }
    }

    public async ValueTask SendAsync(
        ControlMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ThrowIfUnavailable();
        await applicationSendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        byte[]? plaintext = null;
        byte[]? encryptedFrame = null;
        bool gateHeld = false;
        try
        {
            ThrowIfUnavailable();
            plaintext = ControlMessageCodec.Encode(message);
            if (liveRekeyEnabled
                && session.ShouldRekeyBeforeSend(plaintext.Length))
            {
                await RekeyAsync(DefaultRekeyTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }

            await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateHeld = true;
            ThrowIfUnavailable();
            encryptedFrame = session.Encrypt(plaintext);
            if (encryptedFrame.Length > MaximumEncryptedFrameBytes)
            {
                throw new InvalidDataException(
                    "The encrypted control frame exceeds the transport limit.");
            }

            byte[] lengthPrefix = new byte[LengthPrefixBytes];
            BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, encryptedFrame.Length);
            await stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(encryptedFrame, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            Exception finalFailure = Fault(failure);
            if (ReferenceEquals(finalFailure, failure))
            {
                throw;
            }

            throw finalFailure;
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            if (encryptedFrame is not null)
            {
                CryptographicOperations.ZeroMemory(encryptedFrame);
            }

            if (gateHeld)
            {
                sendGate.Release();
            }

            applicationSendGate.Release();
        }
    }

    public async ValueTask<ControlMessage> ReceiveAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        await receiveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            while (true)
            {
                byte[]? encryptedFrame = null;
                byte[]? plaintext = null;
                try
                {
                    byte[] lengthPrefix = new byte[LengthPrefixBytes];
                    await stream.ReadExactlyAsync(lengthPrefix, cancellationToken)
                        .ConfigureAwait(false);
                    int frameLength = BinaryPrimitives.ReadInt32BigEndian(lengthPrefix);
                    if (frameLength is < 1 or > MaximumEncryptedFrameBytes)
                    {
                        throw new InvalidDataException(
                            $"An encrypted control frame length must be from 1 to {MaximumEncryptedFrameBytes} bytes.");
                    }

                    encryptedFrame = GC.AllocateUninitializedArray<byte>(frameLength);
                    await stream.ReadExactlyAsync(encryptedFrame, cancellationToken)
                        .ConfigureAwait(false);
                    plaintext = session.Decrypt(encryptedFrame);
                    if (liveRekeyEnabled
                        && SecureSessionKeyUpdateCodec.IsKeyUpdate(plaintext))
                    {
                        SecureSessionKeyUpdate update =
                            SecureSessionKeyUpdateCodec.Decode(plaintext);
                        uint? pendingEpoch;
                        lock (rekeyGate)
                        {
                            pendingEpoch = pendingRekeyEpoch;
                        }

                        SecureSessionPeerKeyUpdateDecision decision =
                            SecureSessionRekeyRules.EvaluatePeerUpdate(
                                update,
                                session.SendEpoch,
                                session.ReceiveEpoch,
                                pendingEpoch);
                        session.AdvanceReceiveEpoch(decision.NextReceiveEpoch);
                        TryCompletePendingRekey();
                        if (decision.SendResponse)
                        {
                            await SendKeyUpdateIfBehindAsync(
                                requestPeerUpdate: false,
                                decision.NextReceiveEpoch,
                                cancellationToken).ConfigureAwait(false);
                        }

                        continue;
                    }

                    return ControlMessageCodec.Decode(plaintext);
                }
                finally
                {
                    if (encryptedFrame is not null)
                    {
                        CryptographicOperations.ZeroMemory(encryptedFrame);
                    }

                    if (plaintext is not null)
                    {
                        CryptographicOperations.ZeroMemory(plaintext);
                    }
                }
            }
        }
        catch (Exception failure)
        {
            Exception finalFailure = Fault(failure);
            if (ReferenceEquals(finalFailure, failure))
            {
                throw;
            }

            throw finalFailure;
        }
        finally
        {
            receiveGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        FailPendingRekey(new ObjectDisposedException(nameof(SecureControlChannel)));
        Exception? cleanupFailure = null;
        try
        {
            stream.Dispose();
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }

        await receiveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await sendGate.WaitAsync().ConfigureAwait(false);
            try
            {
                try
                {
                    session.Dispose();
                }
                catch (Exception exception)
                {
                    cleanupFailure ??= exception;
                }

                try
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    cleanupFailure ??= exception;
                }
            }
            finally
            {
                sendGate.Release();
            }
        }
        finally
        {
            receiveGate.Release();
        }

        if (cleanupFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(cleanupFailure)
                .Throw();
        }
    }

    internal Exception RejectPeerMessage(Exception failure) => Fault(failure);

    private async ValueTask SendKeyUpdateIfBehindAsync(
        bool requestPeerUpdate,
        uint targetEpoch,
        CancellationToken cancellationToken)
    {
        await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        byte[]? plaintext = null;
        byte[]? encryptedFrame = null;
        try
        {
            ThrowIfUnavailable();
            if (session.SendEpoch >= targetEpoch)
            {
                TryCompletePendingRekey();
                return;
            }

            if (session.SendEpoch == uint.MaxValue
                || session.SendEpoch + 1 != targetEpoch)
            {
                throw new InvalidDataException(
                    "The local KeyUpdate target contains an epoch gap.");
            }

            SecureSessionKeyUpdate update = SecureSessionKeyUpdate.Create(
                requestPeerUpdate,
                targetEpoch);
            plaintext = SecureSessionKeyUpdateCodec.Encode(update);
            encryptedFrame = session.Encrypt(plaintext);
            byte[] lengthPrefix = new byte[LengthPrefixBytes];
            BinaryPrimitives.WriteInt32BigEndian(
                lengthPrefix,
                encryptedFrame.Length);
            await stream.WriteAsync(lengthPrefix, cancellationToken)
                .ConfigureAwait(false);
            await stream.WriteAsync(encryptedFrame, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            session.AdvanceSendEpoch(targetEpoch);
            TryCompletePendingRekey();
        }
        catch (Exception failure)
        {
            Exception finalFailure = Fault(failure);
            if (ReferenceEquals(finalFailure, failure))
            {
                throw;
            }

            throw finalFailure;
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            if (encryptedFrame is not null)
            {
                CryptographicOperations.ZeroMemory(encryptedFrame);
            }

            sendGate.Release();
        }
    }

    private void TryCompletePendingRekey()
    {
        TaskCompletionSource? completion = null;
        lock (rekeyGate)
        {
            if (pendingRekeyEpoch is uint targetEpoch
                && session.SendEpoch >= targetEpoch
                && session.ReceiveEpoch >= targetEpoch)
            {
                completion = pendingRekeyCompletion;
                pendingRekeyCompletion = null;
                pendingRekeyEpoch = null;
            }
        }

        completion?.TrySetResult();
    }

    private void FailPendingRekey(Exception failure)
    {
        TaskCompletionSource? completion;
        lock (rekeyGate)
        {
            completion = pendingRekeyCompletion;
            pendingRekeyCompletion = null;
            pendingRekeyEpoch = null;
        }

        completion?.TrySetException(failure);
    }

    private Exception Fault(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (Interlocked.Exchange(ref faulted, 1) == 0)
        {
            List<Exception>? cleanupFailures = null;
            try
            {
                session.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                cleanupFailures = [cleanupFailure];
            }

            try
            {
                stream.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                cleanupFailures ??= [];
                cleanupFailures.Add(cleanupFailure);
            }

            if (cleanupFailures is not null)
            {
                failure = new AggregateException(
                    "Secure control operation and channel cleanup both failed.",
                    new[] { failure }.Concat(cleanupFailures));
            }

            FailPendingRekey(failure);
        }

        return failure;
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        if (Volatile.Read(ref faulted) != 0)
        {
            throw new InvalidOperationException(
                "The secure control channel is faulted and cannot be reused.");
        }
    }
}
