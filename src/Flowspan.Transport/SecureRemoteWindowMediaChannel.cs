using System.Buffers.Binary;
using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Transport;

public sealed class SecureRemoteWindowMediaChannel : IRemoteWindowMediaSink, IAsyncDisposable
{
    public static TimeSpan DefaultOperationTimeout { get; } = TimeSpan.FromSeconds(2);

    public static TimeSpan MaximumOperationTimeout { get; } = TimeSpan.FromSeconds(10);

    public const int MaximumReceiveBytesPerSecond = 32 * 1024 * 1024;
    public const int MaximumReceiveFramesPerSecond = 512;
    public const int MaximumEncryptedFrameBytes =
        RemoteWindowMediaFrameCodec.HeaderBytes
        + RemoteWindowMediaFrame.MaximumPayloadBytes
        + 128;

    private const int LengthPrefixBytes = sizeof(int);
    private readonly ActivityId activityId;
    private readonly TimeSpan operationTimeout;
    private readonly SemaphoreSlim receiveGate = new(1, 1);
    private readonly SecureFrameSession session;
    private readonly RemoteWindowSessionId sessionId;
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly Stream stream;
    private readonly TimeProvider timeProvider;
    private int disposed;
    private int faulted;
    private ulong lastAudioSequence;
    private ulong lastCursorSequence;
    private ulong lastVideoSequence;
    private readonly Queue<ReceiveRateSample> receiveRateSamples = [];
    private long receiveBytesInWindow;

    public SecureRemoteWindowMediaChannel(
        Stream stream,
        SecureFrameSession session,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        TimeSpan? operationTimeout = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(activityId);
        if (!stream.CanRead || !stream.CanWrite)
        {
            throw new ArgumentException(
                "A secure Remote Window media channel requires a readable and writable stream.",
                nameof(stream));
        }

        TimeSpan effectiveTimeout = operationTimeout ?? DefaultOperationTimeout;
        if (effectiveTimeout <= TimeSpan.Zero
            || effectiveTimeout > MaximumOperationTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                $"A Remote Window media operation timeout must be positive and at most {MaximumOperationTimeout}.");
        }

        this.stream = stream;
        this.session = session;
        this.sessionId = sessionId;
        this.activityId = activityId;
        this.operationTimeout = effectiveTimeout;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask SendAsync(
        RemoteWindowMediaFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ThrowIfUnavailable();
        if (frame.SessionId != sessionId || frame.ActivityId != activityId)
        {
            throw new InvalidOperationException(
                "The Remote Window media frame does not match this channel binding.");
        }

        using var deadlineCancellation =
            new CancellationTokenSource(operationTimeout, timeProvider);
        using CancellationTokenSource operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                deadlineCancellation.Token);
        CancellationToken operationToken = operationCancellation.Token;
        byte[]? plaintext = null;
        byte[]? encryptedFrame = null;
        Task? encryptedFrameWrite = null;
        bool encryptedFrameWriteObserved = false;
        bool gateHeld = false;
        try
        {
            await sendGate.WaitAsync(operationToken).ConfigureAwait(false);
            gateHeld = true;
            ThrowIfUnavailable();
            plaintext = RemoteWindowMediaFrameCodec.Encode(frame);
            encryptedFrame = session.Encrypt(plaintext);
            if (encryptedFrame.Length > MaximumEncryptedFrameBytes)
            {
                throw new InvalidDataException(
                    "The encrypted Remote Window media frame exceeds the transport limit.");
            }

            byte[] lengthPrefix = new byte[LengthPrefixBytes];
            BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, encryptedFrame.Length);
            await stream.WriteAsync(lengthPrefix, operationToken)
                .AsTask()
                .WaitAsync(operationToken)
                .ConfigureAwait(false);
            encryptedFrameWrite = stream.WriteAsync(encryptedFrame, operationToken)
                .AsTask();
            await encryptedFrameWrite
                .WaitAsync(operationToken)
                .ConfigureAwait(false);
            encryptedFrameWriteObserved = true;
            await stream.FlushAsync(operationToken)
                .WaitAsync(operationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            Exception operationFailure = deadlineCancellation.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested
                    ? new TimeoutException(
                        "The Remote Window media write timed out.",
                        failure)
                    : failure;
            Exception finalFailure = Fault(operationFailure);
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
                if (encryptedFrameWrite is not null
                    && !encryptedFrameWriteObserved)
                {
                    _ = ZeroWhenCompletedAsync(encryptedFrameWrite, encryptedFrame);
                }
                else
                {
                    CryptographicOperations.ZeroMemory(encryptedFrame);
                }
            }

            if (gateHeld)
            {
                sendGate.Release();
            }
        }
    }

    public async ValueTask<RemoteWindowMediaFrame> ReceiveAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        byte[]? encryptedFrame = null;
        byte[]? plaintext = null;
        bool gateHeld = false;
        try
        {
            await receiveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateHeld = true;
            ThrowIfUnavailable();
            byte[] lengthPrefix = new byte[LengthPrefixBytes];
            await stream.ReadExactlyAsync(lengthPrefix, cancellationToken)
                .AsTask()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            int frameLength = BinaryPrimitives.ReadInt32BigEndian(lengthPrefix);
            if (frameLength is < 1 or > MaximumEncryptedFrameBytes)
            {
                throw new InvalidDataException(
                    $"An encrypted Remote Window media frame length must be from 1 to {MaximumEncryptedFrameBytes} bytes.");
            }

            encryptedFrame = GC.AllocateUninitializedArray<byte>(frameLength);
            await stream.ReadExactlyAsync(encryptedFrame, cancellationToken)
                .AsTask()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            plaintext = session.Decrypt(encryptedFrame);
            RemoteWindowMediaFrame frame = RemoteWindowMediaFrameCodec.Decode(
                plaintext,
                sessionId,
                activityId);
            ValidateAndRecordReceiveRate(frame);
            ValidateAndAdvanceMediaSequence(frame);
            return frame;
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
            if (encryptedFrame is not null)
            {
                CryptographicOperations.ZeroMemory(encryptedFrame);
            }

            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            if (gateHeld)
            {
                receiveGate.Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

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
                    "Secure media operation and channel cleanup both failed.",
                    new[] { failure }.Concat(cleanupFailures));
            }
        }

        return failure;
    }

    private static async Task ZeroWhenCompletedAsync(Task operation, byte[] buffer)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        if (Volatile.Read(ref faulted) != 0)
        {
            throw new InvalidOperationException(
                "The secure Remote Window media channel is faulted and cannot be reused.");
        }
    }

    private void ValidateAndAdvanceMediaSequence(RemoteWindowMediaFrame frame)
    {
        ulong lastSequence = frame.Kind switch
        {
            RemoteWindowMediaKind.Video => lastVideoSequence,
            RemoteWindowMediaKind.Audio => lastAudioSequence,
            RemoteWindowMediaKind.Cursor => lastCursorSequence,
            _ => throw new InvalidDataException(
                "The Remote Window media kind is unsupported."),
        };
        if (frame.Sequence <= lastSequence)
        {
            throw new InvalidDataException(
                "The Remote Window media sequence is a replay or rollback.");
        }

        switch (frame.Kind)
        {
            case RemoteWindowMediaKind.Video:
                lastVideoSequence = frame.Sequence;
                break;
            case RemoteWindowMediaKind.Audio:
                lastAudioSequence = frame.Sequence;
                break;
            case RemoteWindowMediaKind.Cursor:
                lastCursorSequence = frame.Sequence;
                break;
            default:
                throw new InvalidDataException(
                    "The Remote Window media kind is unsupported.");
        }
    }

    private void ValidateAndRecordReceiveRate(RemoteWindowMediaFrame frame)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset cutoff = now.AddSeconds(-1);
        while (receiveRateSamples.TryPeek(out ReceiveRateSample sample)
            && sample.ReceivedAt < cutoff)
        {
            receiveRateSamples.Dequeue();
            receiveBytesInWindow -= sample.PayloadBytes;
        }

        int nextFrames = checked(receiveRateSamples.Count + 1);
        long nextBytes = checked(receiveBytesInWindow + frame.PayloadLength);
        if (nextFrames > MaximumReceiveFramesPerSecond
            || nextBytes > MaximumReceiveBytesPerSecond)
        {
            throw new InvalidDataException(
                "The Remote Window media receive rate exceeds the channel limit.");
        }

        receiveRateSamples.Enqueue(new ReceiveRateSample(now, frame.PayloadLength));
        receiveBytesInWindow = nextBytes;
    }

    private readonly record struct ReceiveRateSample(
        DateTimeOffset ReceivedAt,
        int PayloadBytes);
}
