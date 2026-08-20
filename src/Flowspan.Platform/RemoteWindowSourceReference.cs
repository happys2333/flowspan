using Flowspan.Domain;

namespace Flowspan.Platform;

public sealed record RemoteWindowSourceReference
{
    public const int MaximumDisplayNameCharacters = 120;

    private RemoteWindowSourceReference(
        ActivityId activityId,
        DeviceId hostDeviceId,
        string displayName,
        long sourceGeneration,
        ActivityKind? semanticActivityKind)
    {
        ActivityId = activityId;
        HostDeviceId = hostDeviceId;
        DisplayName = displayName;
        SourceGeneration = sourceGeneration;
        SemanticActivityKind = semanticActivityKind;
    }

    public ActivityId ActivityId { get; }

    public DeviceId HostDeviceId { get; }

    public string DisplayName { get; }

    public long SourceGeneration { get; }

    public ActivityKind? SemanticActivityKind { get; }

    public bool IsSemanticActivity => SemanticActivityKind is not null;

    public static RemoteWindowSourceReference CreateGeneric(
        ActivityId activityId,
        DeviceId hostDeviceId,
        string displayName,
        long sourceGeneration) => Create(
            activityId,
            hostDeviceId,
            displayName,
            sourceGeneration,
            semanticActivityKind: null);

    public static RemoteWindowSourceReference FromActiveActivity(
        ActivityInstance activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (activity.Lifecycle != ActivityLifecycle.Active)
        {
            throw new ArgumentException(
                "A Remote Window source requires an active semantic Activity.",
                nameof(activity));
        }

        return Create(
            activity.Descriptor.Id,
            activity.Placement.DeviceId,
            activity.Descriptor.Title,
            activity.Revision,
            activity.Descriptor.Kind);
    }

    internal RemoteWindowSourceReference WithDisplayName(string displayName) =>
        Create(
            ActivityId,
            HostDeviceId,
            displayName,
            SourceGeneration,
            SemanticActivityKind);

    public override string ToString() =>
        $"Remote Window source {ActivityId} (generation {SourceGeneration})";

    private static RemoteWindowSourceReference Create(
        ActivityId activityId,
        DeviceId hostDeviceId,
        string displayName,
        long sourceGeneration,
        ActivityKind? semanticActivityKind)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentNullException.ThrowIfNull(hostDeviceId);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceGeneration, 1);

        string normalizedDisplayName = RemoteWindowSourceText.Normalize(
            displayName,
            nameof(displayName),
            MaximumDisplayNameCharacters,
            "Remote Window source display name");

        return new RemoteWindowSourceReference(
            activityId,
            hostDeviceId,
            normalizedDisplayName,
            sourceGeneration,
            semanticActivityKind);
    }
}

internal static class RemoteWindowSourceText
{
    public static string Normalize(
        string value,
        string parameterName,
        int maximumCharacters,
        string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > maximumCharacters)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"A {fieldName} cannot exceed {maximumCharacters} characters.");
        }

        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"A {fieldName} must contain non-control text.",
                parameterName);
        }

        for (int index = 0; index < normalized.Length; index++)
        {
            char character = normalized[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= normalized.Length
                    || !char.IsLowSurrogate(normalized[index + 1]))
                {
                    throw new ArgumentException(
                        $"A {fieldName} must contain well-formed UTF-16 text.",
                        parameterName);
                }

                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                throw new ArgumentException(
                    $"A {fieldName} must contain well-formed UTF-16 text.",
                    parameterName);
            }
        }

        return normalized;
    }
}
