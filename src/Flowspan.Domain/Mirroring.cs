using System.Collections.Immutable;

namespace Flowspan.Domain;

public sealed record DriverLease
{
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(5);

    private DriverLease(
        ActivityId activityId,
        DeviceId ownerDeviceId,
        DeviceId? holderDeviceId,
        long epoch,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        bool emergencyStopped)
    {
        ActivityId = activityId;
        OwnerDeviceId = ownerDeviceId;
        HolderDeviceId = holderDeviceId;
        Epoch = epoch;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        EmergencyStopped = emergencyStopped;
    }

    public ActivityId ActivityId { get; }

    public DeviceId OwnerDeviceId { get; }

    public DeviceId? HolderDeviceId { get; }

    public long Epoch { get; }

    public DateTimeOffset IssuedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public bool EmergencyStopped { get; }

    public static DriverLease IssueToOwner(
        ActivityId activityId,
        DeviceId ownerDeviceId,
        DateTimeOffset now,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(ownerDeviceId);
        ValidateDuration(duration);

        return new DriverLease(
            activityId,
            ownerDeviceId,
            ownerDeviceId,
            1,
            now,
            now.Add(duration),
            emergencyStopped: false);
    }

    public bool Authorizes(DeviceId deviceId, long epoch, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        return !EmergencyStopped
            && HolderDeviceId == deviceId
            && Epoch == epoch
            && now >= IssuedAt
            && now < ExpiresAt;
    }

    public DriverLease TransferTo(
        DeviceId newHolderDeviceId,
        DateTimeOffset now,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(newHolderDeviceId);
        EnsureMonotonicTime(now);
        ValidateDuration(duration);
        if (EmergencyStopped)
        {
            throw new InvalidOperationException(
                "Driver authority cannot transfer while emergency stop is active.");
        }

        return Next(newHolderDeviceId, now, duration, emergencyStopped: false);
    }

    public DriverLease ReturnExpiredToOwner(DateTimeOffset now, TimeSpan duration)
    {
        EnsureMonotonicTime(now);
        ValidateDuration(duration);
        if (EmergencyStopped || now < ExpiresAt)
        {
            return this;
        }

        return Next(OwnerDeviceId, now, duration, emergencyStopped: false);
    }

    public DriverLease EmergencyStop(DateTimeOffset now)
    {
        EnsureMonotonicTime(now);
        return new DriverLease(
            ActivityId,
            OwnerDeviceId,
            null,
            checked(Epoch + 1),
            now,
            now,
            emergencyStopped: true);
    }

    public DriverLease ResumeOwner(
        DateTimeOffset now,
        TimeSpan duration)
    {
        EnsureMonotonicTime(now);
        ValidateDuration(duration);
        return Next(OwnerDeviceId, now, duration, emergencyStopped: false);
    }

    private DriverLease Next(
        DeviceId holderDeviceId,
        DateTimeOffset now,
        TimeSpan duration,
        bool emergencyStopped) => new(
            ActivityId,
            OwnerDeviceId,
            holderDeviceId,
            checked(Epoch + 1),
            now,
            now.Add(duration),
            emergencyStopped);

    private void EnsureMonotonicTime(DateTimeOffset now)
    {
        if (now < IssuedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                "Driver lease time cannot move backwards.");
        }
    }

    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration < MinimumDuration || duration > MaximumDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                $"A driver lease must last from {MinimumDuration.TotalMilliseconds} milliseconds to {MaximumDuration.TotalMinutes} minutes.");
        }
    }
}

public enum MirrorParticipantRole
{
    ViewOnly,
    DriverEligible,
}

public enum MirrorSessionStatus
{
    Active,
    EmergencyStopped,
    Ended,
}

public sealed class MirrorSession
{
    private readonly ImmutableDictionary<DeviceId, MirrorParticipantRole> participants;

    private MirrorSession(
        ActivityId activityId,
        DeviceId hostDeviceId,
        ImmutableDictionary<DeviceId, MirrorParticipantRole> participants,
        DriverLease driverLease,
        MirrorSessionStatus status)
    {
        ActivityId = activityId;
        HostDeviceId = hostDeviceId;
        this.participants = participants;
        DriverLease = driverLease;
        Status = status;
    }

    public ActivityId ActivityId { get; }

    public DeviceId HostDeviceId { get; }

    public IReadOnlyDictionary<DeviceId, MirrorParticipantRole> Participants => participants;

    public DriverLease DriverLease { get; }

    public MirrorSessionStatus Status { get; }

    public static MirrorSession Start(
        ActivityId activityId,
        DeviceId hostDeviceId,
        DateTimeOffset now,
        TimeSpan ownerLeaseDuration)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(hostDeviceId);

        ImmutableDictionary<DeviceId, MirrorParticipantRole> participants =
            ImmutableDictionary<DeviceId, MirrorParticipantRole>.Empty.Add(
                hostDeviceId,
                MirrorParticipantRole.DriverEligible);
        return new MirrorSession(
            activityId,
            hostDeviceId,
            participants,
            DriverLease.IssueToOwner(activityId, hostDeviceId, now, ownerLeaseDuration),
            MirrorSessionStatus.Active);
    }

    public bool CanView(DeviceId deviceId)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        return Status == MirrorSessionStatus.Active && participants.ContainsKey(deviceId);
    }

    public bool CanInjectInput(DeviceId deviceId, long epoch, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        return Status == MirrorSessionStatus.Active
            && participants.TryGetValue(deviceId, out MirrorParticipantRole role)
            && role == MirrorParticipantRole.DriverEligible
            && DriverLease.Authorizes(deviceId, epoch, now);
    }

    public MirrorSession AddParticipant(
        DeviceId deviceId,
        MirrorParticipantRole role)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        EnsureActive();
        return Copy(participants.SetItem(deviceId, role), DriverLease, Status);
    }

    public MirrorSession RemoveParticipant(
        DeviceId deviceId,
        DateTimeOffset now,
        TimeSpan ownerLeaseDuration)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        EnsureActive();
        if (deviceId == HostDeviceId)
        {
            throw new InvalidOperationException(
                "The host cannot be removed from an active mirror session.");
        }

        ImmutableDictionary<DeviceId, MirrorParticipantRole> updated =
            participants.Remove(deviceId);
        DriverLease lease = DriverLease.HolderDeviceId == deviceId
            ? DriverLease.ResumeOwner(now, ownerLeaseDuration)
            : DriverLease;
        return Copy(updated, lease, Status);
    }

    public MirrorSession TransferDriver(
        DeviceId deviceId,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        EnsureActive();
        if (!participants.TryGetValue(deviceId, out MirrorParticipantRole role)
            || role != MirrorParticipantRole.DriverEligible)
        {
            throw new InvalidOperationException(
                "Only a driver-eligible mirror participant can receive driver authority.");
        }

        return Copy(
            participants,
            DriverLease.TransferTo(deviceId, now, leaseDuration),
            Status);
    }

    public MirrorSession RefreshExpiredLease(
        DateTimeOffset now,
        TimeSpan ownerLeaseDuration) => Copy(
            participants,
            DriverLease.ReturnExpiredToOwner(now, ownerLeaseDuration),
            Status);

    public MirrorSession EmergencyStop(DateTimeOffset now) => Copy(
        participants,
        DriverLease.EmergencyStop(now),
        MirrorSessionStatus.EmergencyStopped);

    public MirrorSession ResumeOwner(DateTimeOffset now, TimeSpan ownerLeaseDuration)
    {
        if (Status != MirrorSessionStatus.EmergencyStopped)
        {
            throw new InvalidOperationException(
                "Only an emergency-stopped mirror session needs an explicit resume.");
        }

        return Copy(
            participants,
            DriverLease.ResumeOwner(now, ownerLeaseDuration),
            MirrorSessionStatus.Active);
    }

    public MirrorSession End(DateTimeOffset now) => new(
        ActivityId,
        HostDeviceId,
        participants,
        DriverLease.EmergencyStop(now),
        MirrorSessionStatus.Ended);

    private MirrorSession Copy(
        ImmutableDictionary<DeviceId, MirrorParticipantRole> updatedParticipants,
        DriverLease driverLease,
        MirrorSessionStatus status) => new(
            ActivityId,
            HostDeviceId,
            updatedParticipants,
            driverLease,
            status);

    private void EnsureActive()
    {
        if (Status != MirrorSessionStatus.Active)
        {
            throw new InvalidOperationException("The mirror session is not active.");
        }
    }
}
