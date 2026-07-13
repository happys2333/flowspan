using Flowspan.Domain;

namespace Flowspan.Platform;

public enum ProtectionKind
{
    Safe,
    SensitiveWindow,
    SecureInput,
    ProtectedContent,
    Unknown,
}

public sealed record ProtectionSnapshot
{
    public ProtectionSnapshot(
        ProtectionKind kind,
        DateTimeOffset observedAt,
        string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (source.Length > 80 || source.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A protection-state source must contain 1 to 80 non-control characters.",
                nameof(source));
        }

        Kind = kind;
        ObservedAt = observedAt;
        Source = source;
    }

    public ProtectionKind Kind { get; }

    public DateTimeOffset ObservedAt { get; }

    public string Source { get; }
}

public sealed class EmergencyStopLatch
{
    private int active;

    public bool IsActive => Volatile.Read(ref active) != 0;

    public void Activate() => Interlocked.Exchange(ref active, 1);

    public void ResetAfterLocalConfirmation() => Interlocked.Exchange(ref active, 0);
}

public enum RemoteInputDecision
{
    Allowed,
    EmergencyStopped,
    SessionInactive,
    NotParticipant,
    ViewOnly,
    ProtectionStateStale,
    ProtectionStateUnknown,
    SensitiveSurface,
    DriverLeaseDenied,
}

public sealed class RemoteInputPolicy
{
    public static readonly TimeSpan MaximumProtectionAge = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromMilliseconds(50);
    private readonly EmergencyStopLatch emergencyStop;

    public RemoteInputPolicy(EmergencyStopLatch emergencyStop)
    {
        ArgumentNullException.ThrowIfNull(emergencyStop);
        this.emergencyStop = emergencyStop;
    }

    public RemoteInputDecision Evaluate(
        MirrorSession session,
        DeviceId inputDeviceId,
        long driverLeaseEpoch,
        ProtectionSnapshot protection,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(inputDeviceId);
        ArgumentNullException.ThrowIfNull(protection);

        if (emergencyStop.IsActive)
        {
            return RemoteInputDecision.EmergencyStopped;
        }

        if (session.Status != MirrorSessionStatus.Active)
        {
            return RemoteInputDecision.SessionInactive;
        }

        if (!session.Participants.TryGetValue(
                inputDeviceId,
                out MirrorParticipantRole role))
        {
            return RemoteInputDecision.NotParticipant;
        }

        if (role != MirrorParticipantRole.DriverEligible)
        {
            return RemoteInputDecision.ViewOnly;
        }

        if (protection.ObservedAt > now.Add(MaximumFutureClockSkew)
            || now - protection.ObservedAt > MaximumProtectionAge)
        {
            return RemoteInputDecision.ProtectionStateStale;
        }

        if (protection.Kind == ProtectionKind.Unknown)
        {
            return RemoteInputDecision.ProtectionStateUnknown;
        }

        if (protection.Kind != ProtectionKind.Safe)
        {
            return RemoteInputDecision.SensitiveSurface;
        }

        return session.CanInjectInput(inputDeviceId, driverLeaseEpoch, now)
            ? RemoteInputDecision.Allowed
            : RemoteInputDecision.DriverLeaseDenied;
    }
}
