using System.Text.Json.Serialization;
using Flowspan.Domain;

namespace Flowspan.Platform;

public sealed record NativeRemoteWindowSourceUse
{
    internal NativeRemoteWindowSourceUse(
        NativeRemoteWindowSourceToken token,
        ActivityId activityId,
        DeviceId hostDeviceId,
        long ownerGeneration,
        long sessionGeneration,
        long sourceGeneration,
        long geometryRevision)
    {
        Token = token;
        ActivityId = activityId;
        HostDeviceId = hostDeviceId;
        OwnerGeneration = ownerGeneration;
        SessionGeneration = sessionGeneration;
        SourceGeneration = sourceGeneration;
        GeometryRevision = geometryRevision;
    }

    [JsonIgnore]
    public NativeRemoteWindowSourceToken Token { get; }

    public ActivityId ActivityId { get; }

    public DeviceId HostDeviceId { get; }

    public long OwnerGeneration { get; }

    public long SessionGeneration { get; }

    public long SourceGeneration { get; }

    public long GeometryRevision { get; }

    public bool Matches(NativeRemoteWindowFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.OwnerGeneration == OwnerGeneration
            && frame.SessionGeneration == SessionGeneration
            && frame.SourceGeneration == SourceGeneration
            && frame.GeometryRevision == GeometryRevision;
    }

    public override string ToString() =>
        $"Native Remote Window source use {ActivityId} (owner {OwnerGeneration}, session {SessionGeneration}, source {SourceGeneration}, geometry {GeometryRevision})";

    internal static NativeRemoteWindowSourceUse Create(
        NativeRemoteWindowSourceSnapshot snapshot,
        long ownerGeneration,
        long sessionGeneration)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sessionGeneration, 1);
        return new NativeRemoteWindowSourceUse(
            snapshot.Token,
            snapshot.Source.ActivityId,
            snapshot.Source.HostDeviceId,
            ownerGeneration,
            sessionGeneration,
            snapshot.Source.SourceGeneration,
            snapshot.GeometryRevision);
    }

    internal bool Matches(
        NativeRemoteWindowSourceSnapshot snapshot,
        bool requireGeometryRevision)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Token.Equals(snapshot.Token)
            && ActivityId == snapshot.Source.ActivityId
            && HostDeviceId == snapshot.Source.HostDeviceId
            && SourceGeneration == snapshot.Source.SourceGeneration
            && (!requireGeometryRevision
                || GeometryRevision == snapshot.GeometryRevision);
    }

    internal bool MatchesExactly(NativeRemoteWindowSourceUse other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Token.Equals(other.Token)
            && ActivityId == other.ActivityId
            && HostDeviceId == other.HostDeviceId
            && OwnerGeneration == other.OwnerGeneration
            && SessionGeneration == other.SessionGeneration
            && SourceGeneration == other.SourceGeneration
            && GeometryRevision == other.GeometryRevision;
    }
}

public interface INativeRemoteWindowFrameSink
{
    // A non-null frame is owned by the sink when this method is called,
    // including when the frame is rejected or downstream delivery fails.
    public void TakeOwnership(
        NativeRemoteWindowSourceUse sourceUse,
        NativeRemoteWindowFrame frame);
}

public interface INativeRemoteWindowCaptureBoundary :
    IRemoteWindowCaptureGate,
    IAsyncDisposable
{
    // Implementations must resolve the host-local token and revalidate the exact
    // binding immediately before native capture becomes active.
    public ValueTask<LocalBoundaryResult> StartAsync(
        NativeRemoteWindowSourceUse sourceUse,
        INativeRemoteWindowFrameSink frameSink,
        CancellationToken cancellationToken);
}

public interface INativeRemoteInputBoundary :
    IRemoteInputGate,
    IAsyncDisposable
{
    // Implementations must revalidate the exact source and geometry immediately
    // before the first native event, and reject the whole batch before injection.
    public ValueTask<LocalBoundaryResult> InjectAsync(
        NativeRemoteWindowSourceUse sourceUse,
        RemoteInputBatch batch,
        CancellationToken cancellationToken);
}
