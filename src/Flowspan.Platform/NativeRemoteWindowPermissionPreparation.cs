using Flowspan.Domain;

namespace Flowspan.Platform;

internal enum NativeRemoteWindowPermissionPreparationReservationStatus
{
    Reserved,
    SnapshotChanged,
    PermissionDenied,
    BoundaryUnavailable,
}

// This mutation-gate callback must be bounded and non-blocking. It may only
// latch the owning host Preparation reservation; it must not invoke native
// work, cleanup, UI, or arbitrary callbacks.
internal interface INativeRemoteWindowPermissionPreparationInvalidationSink
{
    // The boundary must synchronously transfer a committed registration to
    // its owner before any later operation can throw or publish invalidation.
    public void OwnNativeRemoteWindowPermissionPreparationRegistration(
        INativeRemoteWindowPermissionPreparationRegistration registration);

    public void InvalidateNativeRemoteWindowPermissionPreparationNow();
}

internal interface INativeRemoteWindowPermissionPreparationRegistration :
    IDisposable
{
    public bool IsCurrent { get; }
}

internal sealed record NativeRemoteWindowPermissionPreparationReservationResult(
    NativeRemoteWindowPermissionPreparationReservationStatus Status,
    INativeRemoteWindowPermissionPreparationRegistration? Registration)
{
    public bool Reserved =>
        Status ==
            NativeRemoteWindowPermissionPreparationReservationStatus.Reserved
        && Registration?.IsCurrent == true;
}

internal interface INativeRemoteWindowPermissionPreparationBoundary
{
    // This admission check is synchronous and prompt-free. The returned
    // registration binds the exact observed permission fact and frozen role.
    public NativeRemoteWindowPermissionPreparationReservationResult
        TryReservePreparation(
            NativeRemoteWindowPermissionSnapshot expectedSnapshot,
            MirrorParticipantRole frozenRole,
            INativeRemoteWindowPermissionPreparationInvalidationSink
                invalidationSink);
}
