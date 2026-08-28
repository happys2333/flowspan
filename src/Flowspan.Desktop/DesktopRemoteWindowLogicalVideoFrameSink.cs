using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Transport;

namespace Flowspan.Desktop;

internal enum DesktopRemoteWindowLogicalVideoFrameSinkFault
{
    EncoderFailed,
    WireSequenceExhausted,
    LogicalFrameCreationFailed,
    SenderFailed,
    SenderCancelledUnexpectedly,
}

internal sealed class DesktopRemoteWindowLogicalVideoFrameSink :
    INativeRemoteWindowFrameSink,
    IAsyncDisposable
{
    private readonly ActivityId activityId;
    private readonly Func<NativeRemoteWindowFrame,
        DesktopRemoteWindowJpegEncodingResult> encode;
    private readonly Lock gate = new();
    private readonly RemoteWindowLogicalVideoFrameSender sender;
    private readonly RemoteWindowSessionId sessionId;
    private Action<DesktopRemoteWindowJpegEncodingStatus>? encodingDropped;
    private ExpectedSourceBinding? expectedBinding;
    private Action<DesktopRemoteWindowLogicalVideoFrameSinkFault>? faulted;
    private bool closed;
    private ulong nextWireSequence;
    private bool wireSequenceAvailable = true;

    internal DesktopRemoteWindowLogicalVideoFrameSink(
        NativeRemoteWindowSourceUse expectedSourceUse,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        RemoteWindowLogicalVideoFrameSender sender,
        Func<NativeRemoteWindowFrame,
            DesktopRemoteWindowJpegEncodingResult>? encode = null,
        Action<DesktopRemoteWindowJpegEncodingStatus>? encodingDropped = null,
        Action<DesktopRemoteWindowLogicalVideoFrameSinkFault>? faulted = null,
        ulong initialWireSequence = 1) : this(
        ExpectedSourceBinding.FromUse(expectedSourceUse),
        sessionId,
        activityId,
        sender,
        encode,
        encodingDropped,
        faulted,
        initialWireSequence)
    {
    }

    internal DesktopRemoteWindowLogicalVideoFrameSink(
        NativeRemoteWindowSourceSnapshot expectedSource,
        long ownerGeneration,
        long expectedSessionGeneration,
        RemoteWindowSessionId sessionId,
        RemoteWindowLogicalVideoFrameSender sender,
        Func<NativeRemoteWindowFrame,
            DesktopRemoteWindowJpegEncodingResult>? encode = null,
        Action<DesktopRemoteWindowJpegEncodingStatus>? encodingDropped = null,
        Action<DesktopRemoteWindowLogicalVideoFrameSinkFault>? faulted = null,
        ulong initialWireSequence = 1) : this(
        ExpectedSourceBinding.FromSnapshot(
            expectedSource,
            ownerGeneration,
            expectedSessionGeneration),
        sessionId,
        expectedSource?.Source.ActivityId!,
        sender,
        encode,
        encodingDropped,
        faulted,
        initialWireSequence)
    {
    }

    private DesktopRemoteWindowLogicalVideoFrameSink(
        ExpectedSourceBinding expectedBinding,
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        RemoteWindowLogicalVideoFrameSender sender,
        Func<NativeRemoteWindowFrame,
            DesktopRemoteWindowJpegEncodingResult>? encode,
        Action<DesktopRemoteWindowJpegEncodingStatus>? encodingDropped,
        Action<DesktopRemoteWindowLogicalVideoFrameSinkFault>? faulted,
        ulong initialWireSequence)
    {
        this.expectedBinding = expectedBinding;
        this.sessionId = sessionId
            ?? throw new ArgumentNullException(nameof(sessionId));
        this.activityId = activityId
            ?? throw new ArgumentNullException(nameof(activityId));
        if (activityId != expectedBinding.ActivityId)
        {
            throw new ArgumentException(
                "The logical video Activity must match the exact native source binding.",
                nameof(activityId));
        }

        this.sender = sender
            ?? throw new ArgumentNullException(nameof(sender));
        this.encode = encode ?? DesktopRemoteWindowJpegCodec.Encode;
        this.encodingDropped = encodingDropped;
        this.faulted = faulted;
        ArgumentOutOfRangeException.ThrowIfZero(initialWireSequence);
        nextWireSequence = initialWireSequence;
    }

    internal bool IsClosed
    {
        get
        {
            lock (gate)
            {
                if (closed)
                {
                    return true;
                }
            }

            return sender.IsClosed;
        }
    }

    public void TakeOwnership(
        NativeRemoteWindowSourceUse sourceUse,
        NativeRemoteWindowFrame frame)
    {
        ArgumentNullException.ThrowIfNull(sourceUse);
        ArgumentNullException.ThrowIfNull(frame);
        try
        {
            if (!Accepts(sourceUse, frame))
            {
                return;
            }

            DesktopRemoteWindowJpegEncodingResult encoding;
            try
            {
                encoding = encode(frame);
            }
            catch (Exception)
            {
                CloseForFault(
                    DesktopRemoteWindowLogicalVideoFrameSinkFault.EncoderFailed);
                return;
            }

            using DesktopRemoteWindowEncodedJpeg? encoded = encoding.Frame;
            if (!encoding.Succeeded || encoded is null)
            {
                NotifyEncodingDropIfOpen(encoding.Status);
                return;
            }

            int chunkCount = checked(
                (encoded.PayloadLength
                    + RemoteWindowMediaFrame.MaximumPayloadBytes - 1)
                / RemoteWindowMediaFrame.MaximumPayloadBytes);
            if (!TryReserveWireSequences(
                    chunkCount,
                    out ulong firstSequence,
                    out bool exhausted,
                    out Action<DesktopRemoteWindowLogicalVideoFrameSinkFault>?
                        exhaustionObserver))
            {
                if (exhausted)
                {
                    sender.StopNow();
                    NotifyFault(
                        exhaustionObserver,
                        DesktopRemoteWindowLogicalVideoFrameSinkFault
                            .WireSequenceExhausted);
                }

                return;
            }

            RemoteWindowLogicalVideoFrame logicalFrame;
            try
            {
                logicalFrame = RemoteWindowLogicalVideoFrame.Create(
                    sessionId,
                    activityId,
                    firstSequence,
                    encoded.Payload.Span);
            }
            catch (Exception)
            {
                CloseForFault(
                    DesktopRemoteWindowLogicalVideoFrameSinkFault
                        .LogicalFrameCreationFailed);
                return;
            }

            Task<RemoteWindowLogicalVideoFrameOutcome>? completion = null;
            var transferred = false;
            try
            {
                completion = sender.TakeOwnership(logicalFrame);
                transferred = true;
            }
            catch (Exception)
            {
                CloseForFault(
                    DesktopRemoteWindowLogicalVideoFrameSinkFault.SenderFailed);
            }
            finally
            {
                if (!transferred)
                {
                    logicalFrame.Dispose();
                }
            }

            if (completion is not null)
            {
                _ = ObserveSubmissionAsync(completion);
            }
        }
        finally
        {
            frame.Dispose();
        }
    }

    public void StopNow()
    {
        lock (gate)
        {
            CloseState();
        }

        sender.StopNow();
    }

    public ValueTask DisposeAsync()
    {
        StopNow();
        return sender.DisposeAsync();
    }

    private bool Accepts(
        NativeRemoteWindowSourceUse sourceUse,
        NativeRemoteWindowFrame frame)
    {
        lock (gate)
        {
            return !closed
                && expectedBinding is not null
                && expectedBinding.Matches(sourceUse)
                && sourceUse.Matches(frame);
        }
    }

    private void CloseForFault(
        DesktopRemoteWindowLogicalVideoFrameSinkFault fault)
    {
        Action<DesktopRemoteWindowLogicalVideoFrameSinkFault>? observer;
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            observer = faulted;
            CloseState();
        }

        sender.StopNow();
        NotifyFault(observer, fault);
    }

    private void CloseState()
    {
        closed = true;
        expectedBinding = null;
        encodingDropped = null;
        faulted = null;
    }

    private void NotifyEncodingDropIfOpen(
        DesktopRemoteWindowJpegEncodingStatus status)
    {
        Action<DesktopRemoteWindowJpegEncodingStatus>? observer;
        lock (gate)
        {
            observer = closed ? null : encodingDropped;
        }

        if (observer is null)
        {
            return;
        }

        try
        {
            observer(status);
        }
        catch (Exception)
        {
        }
    }

    private async Task ObserveSubmissionAsync(
        Task<RemoteWindowLogicalVideoFrameOutcome> completion)
    {
        RemoteWindowLogicalVideoFrameOutcome outcome;
        try
        {
            outcome = await completion.ConfigureAwait(false);
        }
        catch (Exception)
        {
            CloseForFault(
                DesktopRemoteWindowLogicalVideoFrameSinkFault.SenderFailed);
            return;
        }

        if (outcome is RemoteWindowLogicalVideoFrameOutcome.Failed)
        {
            CloseForFault(
                DesktopRemoteWindowLogicalVideoFrameSinkFault.SenderFailed);
        }
        else if (outcome is RemoteWindowLogicalVideoFrameOutcome.Cancelled)
        {
            CloseForFault(
                DesktopRemoteWindowLogicalVideoFrameSinkFault
                    .SenderCancelledUnexpectedly);
        }
    }

    private bool TryReserveWireSequences(
        int chunkCount,
        out ulong firstSequence,
        out bool exhausted,
        out Action<DesktopRemoteWindowLogicalVideoFrameSinkFault>? observer)
    {
        lock (gate)
        {
            firstSequence = 0;
            exhausted = false;
            observer = null;
            if (closed)
            {
                return false;
            }

            ulong additionalSequences = checked((ulong)(chunkCount - 1));
            if (!wireSequenceAvailable
                || nextWireSequence > ulong.MaxValue - additionalSequences)
            {
                exhausted = true;
                observer = faulted;
                CloseState();
                return false;
            }

            firstSequence = nextWireSequence;
            ulong finalSequence = firstSequence + additionalSequences;
            if (finalSequence == ulong.MaxValue)
            {
                wireSequenceAvailable = false;
            }
            else
            {
                nextWireSequence = finalSequence + 1;
            }

            return true;
        }
    }

    private static void NotifyFault(
        Action<DesktopRemoteWindowLogicalVideoFrameSinkFault>? observer,
        DesktopRemoteWindowLogicalVideoFrameSinkFault fault)
    {
        if (observer is null)
        {
            return;
        }

        try
        {
            observer(fault);
        }
        catch (Exception)
        {
        }
    }

    private sealed record ExpectedSourceBinding(
        NativeRemoteWindowSourceToken Token,
        ActivityId ActivityId,
        DeviceId HostDeviceId,
        long OwnerGeneration,
        long SessionGeneration,
        long SourceGeneration,
        long GeometryRevision)
    {
        internal static ExpectedSourceBinding FromUse(
            NativeRemoteWindowSourceUse sourceUse)
        {
            ArgumentNullException.ThrowIfNull(sourceUse);
            return new ExpectedSourceBinding(
                sourceUse.Token,
                sourceUse.ActivityId,
                sourceUse.HostDeviceId,
                sourceUse.OwnerGeneration,
                sourceUse.SessionGeneration,
                sourceUse.SourceGeneration,
                sourceUse.GeometryRevision);
        }

        internal static ExpectedSourceBinding FromSnapshot(
            NativeRemoteWindowSourceSnapshot snapshot,
            long ownerGeneration,
            long sessionGeneration)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentOutOfRangeException.ThrowIfLessThan(ownerGeneration, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(sessionGeneration, 1);
            return new ExpectedSourceBinding(
                snapshot.Token,
                snapshot.Source.ActivityId,
                snapshot.Source.HostDeviceId,
                ownerGeneration,
                sessionGeneration,
                snapshot.Source.SourceGeneration,
                snapshot.GeometryRevision);
        }

        internal bool Matches(NativeRemoteWindowSourceUse actual) =>
            Token.Equals(actual.Token)
            && ActivityId == actual.ActivityId
            && HostDeviceId == actual.HostDeviceId
            && OwnerGeneration == actual.OwnerGeneration
            && SessionGeneration == actual.SessionGeneration
            && SourceGeneration == actual.SourceGeneration
            && GeometryRevision == actual.GeometryRevision;
    }
}
