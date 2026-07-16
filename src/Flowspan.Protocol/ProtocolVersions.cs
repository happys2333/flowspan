namespace Flowspan.Protocol;

public readonly record struct ProtocolVersion : IComparable<ProtocolVersion>
{
    public ProtocolVersion(int major, int minor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(major, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        Major = major;
        Minor = minor;
    }

    public int Major { get; }

    public int Minor { get; }

    public int CompareTo(ProtocolVersion other)
    {
        int majorComparison = Major.CompareTo(other.Major);
        return majorComparison != 0 ? majorComparison : Minor.CompareTo(other.Minor);
    }

    public static bool operator <(ProtocolVersion left, ProtocolVersion right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(ProtocolVersion left, ProtocolVersion right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(ProtocolVersion left, ProtocolVersion right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(ProtocolVersion left, ProtocolVersion right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() => $"{Major}.{Minor}";
}

public static class ProtocolFeatures
{
    public static ProtocolVersion ActivitySwapMinimumVersion { get; } = new(1, 1);

    public static ProtocolVersion SecureSessionFinishedMinimumVersion { get; } = new(1, 2);

    public static ProtocolVersion SecureSessionRekeyMinimumVersion { get; } = new(1, 3);

    public static bool SupportsActivitySwap(ProtocolVersion version) =>
        version.Major == ActivitySwapMinimumVersion.Major
        && version.Minor >= ActivitySwapMinimumVersion.Minor;

    public static bool RequiresSecureSessionFinished(ProtocolVersion version) =>
        version.Major == SecureSessionFinishedMinimumVersion.Major
        && version.Minor >= SecureSessionFinishedMinimumVersion.Minor;

    public static bool SupportsLiveRekey(ProtocolVersion version) =>
        version.Major == SecureSessionRekeyMinimumVersion.Major
        && version.Minor >= SecureSessionRekeyMinimumVersion.Minor;
}

public readonly record struct ProtocolNegotiationResult(
    bool Succeeded,
    ProtocolVersion Version,
    ProtocolNegotiationFailure Failure)
{
    public static ProtocolNegotiationResult Compatible(ProtocolVersion version) =>
        new(true, version, ProtocolNegotiationFailure.None);

    public static ProtocolNegotiationResult Incompatible { get; } =
        new(false, default, ProtocolNegotiationFailure.NoCommonVersion);
}

public enum ProtocolNegotiationFailure
{
    None,
    NoCommonVersion,
}

public static class ProtocolNegotiator
{
    public static ProtocolNegotiationResult Negotiate(
        IEnumerable<ProtocolVersion> localVersions,
        IEnumerable<ProtocolVersion> remoteVersions)
    {
        ArgumentNullException.ThrowIfNull(localVersions);
        ArgumentNullException.ThrowIfNull(remoteVersions);

        HashSet<ProtocolVersion> remote = remoteVersions.ToHashSet();
        ProtocolVersion? selected = localVersions
            .Where(remote.Contains)
            .Distinct()
            .OrderDescending()
            .Cast<ProtocolVersion?>()
            .FirstOrDefault();

        return selected is { } version
            ? ProtocolNegotiationResult.Compatible(version)
            : ProtocolNegotiationResult.Incompatible;
    }
}
