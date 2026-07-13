using System.Collections.Immutable;

namespace Flowspan.Platform;

public enum PlatformFamily
{
    Windows,
    MacOS,
    Linux,
}

public enum PlatformCapability
{
    ScreenCapture,
    RemoteInput,
    ProtectedSurfaceDetection,
    SecureInputDetection,
    SecretStorage,
    EmergencyStop,
}

public enum CapabilityAvailability
{
    Available,
    PermissionRequired,
    Denied,
    Unsupported,
    Degraded,
}

public sealed record PlatformCapabilityStatus
{
    private PlatformCapabilityStatus(
        PlatformCapability capability,
        CapabilityAvailability availability,
        string reasonCode,
        string? recoveryAction)
    {
        Capability = capability;
        Availability = availability;
        ReasonCode = reasonCode;
        RecoveryAction = recoveryAction;
    }

    public PlatformCapability Capability { get; }

    public CapabilityAvailability Availability { get; }

    public string ReasonCode { get; }

    public string? RecoveryAction { get; }

    public bool IsAvailable => Availability == CapabilityAvailability.Available;

    public static PlatformCapabilityStatus Create(
        PlatformCapability capability,
        CapabilityAvailability availability,
        string reasonCode,
        string? recoveryAction = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        string normalizedReason = reasonCode.Trim();
        bool validReason = normalizedReason.Length <= 80
            && char.IsAsciiLetterLower(normalizedReason[0])
            && normalizedReason.All(static character =>
                char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character is '.' or '_');
        if (!validReason)
        {
            throw new ArgumentException(
                "A platform reason code must contain lowercase ASCII letters, digits, dots, or underscores.",
                nameof(reasonCode));
        }

        string? normalizedRecovery = string.IsNullOrWhiteSpace(recoveryAction)
            ? null
            : recoveryAction.Trim();
        if (normalizedRecovery is { Length: > 200 })
        {
            throw new ArgumentOutOfRangeException(
                nameof(recoveryAction),
                "A recovery action cannot exceed 200 characters.");
        }

        if (availability is CapabilityAvailability.PermissionRequired
            or CapabilityAvailability.Denied
            or CapabilityAvailability.Degraded
            && normalizedRecovery is null)
        {
            throw new ArgumentException(
                "A permission, denial, or degradation status must include a recovery action.",
                nameof(recoveryAction));
        }

        return new PlatformCapabilityStatus(
            capability,
            availability,
            normalizedReason,
            normalizedRecovery);
    }
}

public sealed class PlatformCapabilitySnapshot
{
    private readonly ImmutableDictionary<PlatformCapability, PlatformCapabilityStatus> statuses;

    public PlatformCapabilitySnapshot(
        PlatformFamily platform,
        DateTimeOffset observedAt,
        IEnumerable<PlatformCapabilityStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        Platform = platform;
        ObservedAt = observedAt;
        PlatformCapabilityStatus[] materialized = statuses.ToArray();
        if (materialized.Select(static status => status.Capability).Distinct().Count()
            != materialized.Length)
        {
            throw new ArgumentException(
                "A platform capability snapshot cannot contain duplicates.",
                nameof(statuses));
        }

        this.statuses = materialized.ToImmutableDictionary(
            static status => status.Capability);
        PlatformCapability[] missing = Enum.GetValues<PlatformCapability>()
            .Where(capability => !this.statuses.ContainsKey(capability))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"A platform capability snapshot must report every capability; missing: {string.Join(", ", missing)}.",
                nameof(statuses));
        }
    }

    public PlatformFamily Platform { get; }

    public DateTimeOffset ObservedAt { get; }

    public IReadOnlyDictionary<PlatformCapability, PlatformCapabilityStatus> Statuses => statuses;

    public PlatformCapabilityStatus Get(PlatformCapability capability) => statuses[capability];
}
