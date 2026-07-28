using NuGet.Versioning;

namespace Flowspan.Release;

public sealed record ReleaseContext
{
    private ReleaseContext(
        string version,
        string buildVersion,
        string commit,
        Uri repository,
        ReleaseTarget target,
        DateTimeOffset sourceTimestamp,
        string channel,
        string minimumVersion,
        string displayVersion,
        Uri downloadBase,
        string builderId,
        string invocationId)
    {
        Version = version;
        BuildVersion = buildVersion;
        Commit = commit;
        Repository = repository;
        Target = target;
        SourceTimestamp = sourceTimestamp;
        Channel = channel;
        MinimumVersion = minimumVersion;
        DisplayVersion = displayVersion;
        DownloadBase = downloadBase;
        BuilderId = builderId;
        InvocationId = invocationId;
    }

    public string Version { get; }

    public string BuildVersion { get; }

    public string Commit { get; }

    public Uri Repository { get; }

    public ReleaseTarget Target { get; }

    public DateTimeOffset SourceTimestamp { get; }

    public string Channel { get; }

    public string MinimumVersion { get; }

    public string DisplayVersion { get; }

    public Uri DownloadBase { get; }

    public string BuilderId { get; }

    public string InvocationId { get; }

    public static ReleaseContext Create(
        string version,
        string buildVersion,
        string commit,
        string repository,
        string rid,
        long sourceDateEpoch,
        string channel,
        string minimumVersion,
        string downloadBase,
        string builderId,
        string invocationId)
    {
        NuGetVersion semanticVersion = RequireSemanticVersion(
            version,
            "version");
        RequireCommit(commit);
        Uri repositoryUri = RequireHttpsUri(repository, "repository");
        ReleaseTarget target = ReleaseTarget.Parse(rid);
        if (target.IsMacOS)
        {
            RequireMacBuildVersion(buildVersion);
        }
        else
        {
            RequireBuildVersion(buildVersion);
        }

        DateTimeOffset timestamp = RequireSourceTimestamp(sourceDateEpoch);
        RequireToken(channel, "channel", maximumLength: 32);
        _ = RequireSemanticVersion(minimumVersion, "minimum version");
        Uri downloadBaseUri = RequireHttpsUri(downloadBase, "download base");
        RequireText(builderId, "builder ID");
        RequireText(invocationId, "invocation ID");

        return new ReleaseContext(
            version,
            buildVersion,
            commit,
            repositoryUri,
            target,
            timestamp,
            channel,
            minimumVersion,
            $"{semanticVersion.Major}.{semanticVersion.Minor}.{semanticVersion.Patch}",
            downloadBaseUri,
            builderId,
            invocationId);
    }

    public string GetPackageStem(string signatureState)
    {
        string suffix = signatureState == SignatureStates.Verified
            ? "signed"
            : "unsigned-test";
        return $"flowspan-{Version}-{Target.Rid}-{suffix}";
    }

    private static void RequireToken(
        string value,
        string field,
        int maximumLength)
    {
        RequireText(value, field, maximumLength);
        if (!char.IsAsciiLetterOrDigit(value[0])
            || !char.IsAsciiLetterOrDigit(value[^1])
            || value.Any(static character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '.' and not '-'))
        {
            throw new ReleaseInputException(
                $"The release {field} contains unsupported characters.");
        }
    }

    private static NuGetVersion RequireSemanticVersion(
        string value,
        string field)
    {
        RequireToken(value, field, maximumLength: 64);
        if (!NuGetVersion.TryParse(value, out NuGetVersion? version)
            || !StringComparer.Ordinal.Equals(
                version.ToNormalizedString(),
                value))
        {
            throw new ReleaseInputException(
                $"The release {field} is not a canonical semantic version.");
        }

        return version;
    }

    private static void RequireBuildVersion(string value) =>
        RequireNumericVersion(value, "build version");

    private static void RequireMacBuildVersion(string value)
    {
        RequireNumericVersion(value, "macOS build version");
        string[] segments = value.Split('.');
        if (segments.Length > 3
            || segments[0].Length > 4
            || segments.Skip(1).Any(static segment => segment.Length > 2))
        {
            throw new ReleaseInputException(
                "The macOS build version exceeds Apple component bounds.");
        }
    }

    private static void RequireNumericVersion(string value, string field)
    {
        RequireText(value, field, maximumLength: 32);
        string[] segments = value.Split('.', StringSplitOptions.None);
        if (segments.Length is < 1 or > 4
            || segments.Any(static segment =>
                segment.Length == 0
                || segment.Any(static character => !char.IsAsciiDigit(character))))
        {
            throw new ReleaseInputException(
                $"The release {field} must contain one to four numeric segments.");
        }
    }

    private static void RequireCommit(string value)
    {
        if (value.Length != 40
            || value.Any(static character =>
                !char.IsAsciiHexDigit(character)
                || char.IsAsciiLetterUpper(character)))
        {
            throw new ReleaseInputException(
                "The release commit must be a lowercase 40-character SHA-1.");
        }
    }

    private static Uri RequireHttpsUri(string value, string field)
    {
        RequireText(value, field);
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ReleaseInputException(
                $"The release {field} must be an absolute HTTPS URI.");
        }

        return uri;
    }

    private static DateTimeOffset RequireSourceTimestamp(long value)
    {
        if (value is < ReleaseBounds.MinimumSourceDateEpoch
            or > ReleaseBounds.MaximumSourceDateEpoch)
        {
            throw new ReleaseInputException(
                "SOURCE_DATE_EPOCH is outside the supported release range.");
        }

        return DateTimeOffset.FromUnixTimeSeconds(value);
    }

    private static void RequireText(
        string value,
        string field,
        int maximumLength = ReleaseBounds.MaximumTextLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value.Any(static character => char.IsControl(character)))
        {
            throw new ReleaseInputException(
                $"The release {field} is empty, oversized, or contains controls.");
        }
    }
}
