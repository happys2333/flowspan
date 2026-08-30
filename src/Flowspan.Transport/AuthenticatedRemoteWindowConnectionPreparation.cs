namespace Flowspan.Transport;

internal enum AuthenticatedRemoteWindowConnectionPreparationReservationStatus
{
    Reserved,
    ConnectionStale,
    ReservationConflict,
}

// These callbacks run while a connection or media mutation gate is held.
// Implementations must be bounded and non-blocking. They may only retain the
// exact registration or latch the owning host Preparation reservation.
internal interface IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink
{
    public void OwnAuthenticatedRemoteWindowConnectionPreparationRegistration(
        IAuthenticatedRemoteWindowConnectionPreparationRegistration registration);

    public void InvalidateAuthenticatedRemoteWindowConnectionPreparationNow();
}

internal interface IAuthenticatedRemoteWindowConnectionPreparationRegistration :
    IDisposable
{
    public long RegistrationId { get; }

    public bool IsCurrent { get; }
}

internal sealed record
    AuthenticatedRemoteWindowConnectionPreparationReservationResult(
        AuthenticatedRemoteWindowConnectionPreparationReservationStatus Status,
        IAuthenticatedRemoteWindowConnectionPreparationRegistration? Registration)
{
    public bool Reserved =>
        Status ==
            AuthenticatedRemoteWindowConnectionPreparationReservationStatus.Reserved
        && Registration?.IsCurrent == true;
}

internal sealed class AuthenticatedRemoteWindowConnectionPreparationRegistration :
    IAuthenticatedRemoteWindowConnectionPreparationRegistration
{
    private readonly RemoteWindowConnectionGeneration generation;
    private IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink? sink;
    private readonly AuthenticatedRemoteWindowMediaSession mediaSession;
    private int active = 1;
    private int disposed;

    internal AuthenticatedRemoteWindowConnectionPreparationRegistration(
        RemoteWindowConnectionGeneration generation,
        AuthenticatedRemoteWindowMediaSession mediaSession,
        long registrationId,
        IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink sink)
    {
        this.generation = generation;
        this.mediaSession = mediaSession;
        RegistrationId = registrationId;
        this.sink = sink;
    }

    public long RegistrationId { get; }

    public bool IsCurrent => Volatile.Read(ref active) != 0
        && generation.IsPreparationRegistrationCurrent(this, mediaSession);

    internal bool IsActive => Volatile.Read(ref active) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            generation.UnregisterPreparation(this, mediaSession);
        }
    }

    internal IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink?
        Deactivate()
    {
        if (Interlocked.Exchange(ref active, 0) == 0)
        {
            return null;
        }

        return Interlocked.Exchange(ref sink, null);
    }

    internal void TransferOwnership() =>
        sink?.OwnAuthenticatedRemoteWindowConnectionPreparationRegistration(this);
}
