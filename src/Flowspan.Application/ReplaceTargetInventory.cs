using System.Collections.Concurrent;
using System.Collections.Immutable;
using Flowspan.Domain;

namespace Flowspan.Application;

public sealed record ReplaceTargetInventoryQuery
{
    private ReplaceTargetInventoryQuery(
        CorrelationId correlationId,
        DeviceId targetDeviceId,
        ActivityKind incomingKind,
        DateTimeOffset deadline)
    {
        CorrelationId = correlationId;
        TargetDeviceId = targetDeviceId;
        IncomingKind = incomingKind;
        Deadline = deadline;
    }

    public CorrelationId CorrelationId { get; }

    public DeviceId TargetDeviceId { get; }

    public ActivityKind IncomingKind { get; }

    public DateTimeOffset Deadline { get; }

    public static ReplaceTargetInventoryQuery Create(
        CorrelationId correlationId,
        DeviceId targetDeviceId,
        ActivityKind incomingKind,
        DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(targetDeviceId);
        ArgumentNullException.ThrowIfNull(incomingKind);
        if (deadline == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deadline),
                "A Replace inventory query deadline must be initialized.");
        }

        return new ReplaceTargetInventoryQuery(
            correlationId,
            targetDeviceId,
            incomingKind,
            deadline);
    }
}

public sealed record ReplaceTargetSnapshot
{
    private ReplaceTargetSnapshot(
        ActivityId activityId,
        long revision,
        string descriptorDigest,
        ActivityKind kind,
        string title,
        string placementSlot)
    {
        ActivityId = activityId;
        Revision = revision;
        DescriptorDigest = descriptorDigest;
        Kind = kind;
        Title = title;
        PlacementSlot = placementSlot;
    }

    public ActivityId ActivityId { get; }

    public long Revision { get; }

    public string DescriptorDigest { get; }

    public ActivityKind Kind { get; }

    public string Title { get; }

    public string PlacementSlot { get; }

    public static ReplaceTargetSnapshot Create(
        ActivityId activityId,
        long revision,
        string descriptorDigest,
        ActivityKind kind,
        string title,
        string placementSlot)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptorDigest);
        if (descriptorDigest.Length != 64
            || !descriptorDigest.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException(
                "A Replace target descriptor digest must be a 32-byte hexadecimal value.",
                nameof(descriptorDigest));
        }

        ArgumentNullException.ThrowIfNull(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        string normalizedTitle = title.Trim();
        if (normalizedTitle.Length > ActivityDescriptor.MaximumTitleCharacters
            || normalizedTitle.Any(char.IsControl))
        {
            throw new ArgumentOutOfRangeException(
                nameof(title),
                "A Replace target title must be bounded Activity display metadata.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(placementSlot);
        string normalizedSlot = placementSlot.Trim();
        if (normalizedSlot.Length > 80 || normalizedSlot.Any(char.IsControl))
        {
            throw new ArgumentOutOfRangeException(
                nameof(placementSlot),
                "A Replace target placement slot must be bounded display metadata.");
        }

        return new ReplaceTargetSnapshot(
            activityId,
            revision,
            descriptorDigest.ToUpperInvariant(),
            kind,
            normalizedTitle,
            normalizedSlot);
    }
}

public sealed record ReplaceTargetInventoryResult
{
    public const int MaximumTargets = 64;

    private ReplaceTargetInventoryResult(
        CorrelationId correlationId,
        DeviceId requestingDeviceId,
        DeviceId targetDeviceId,
        ActivityKind incomingKind,
        DateTimeOffset queryDeadline,
        DateTimeOffset capturedAt,
        FailureCode failureCode,
        bool isTruncated,
        ImmutableArray<ReplaceTargetSnapshot> targets)
    {
        CorrelationId = correlationId;
        RequestingDeviceId = requestingDeviceId;
        TargetDeviceId = targetDeviceId;
        IncomingKind = incomingKind;
        QueryDeadline = queryDeadline;
        CapturedAt = capturedAt;
        FailureCode = failureCode;
        IsTruncated = isTruncated;
        Targets = targets;
    }

    public CorrelationId CorrelationId { get; }

    public DeviceId RequestingDeviceId { get; }

    public DeviceId TargetDeviceId { get; }

    public ActivityKind IncomingKind { get; }

    public DateTimeOffset QueryDeadline { get; }

    public DateTimeOffset CapturedAt { get; }

    public FailureCode FailureCode { get; }

    public bool IsTruncated { get; }

    public ImmutableArray<ReplaceTargetSnapshot> Targets { get; }

    public bool IsSuccess => FailureCode == FailureCode.None;

    public static ReplaceTargetInventoryResult Rejected(
        DeviceId requestingDeviceId,
        ReplaceTargetInventoryQuery query,
        DateTimeOffset capturedAt,
        FailureCode failureCode)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        if (capturedAt == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capturedAt),
                "A Replace inventory capture time must be initialized.");
        }

        if (failureCode == FailureCode.None)
        {
            throw new ArgumentException(
                "A rejected Replace inventory result must have a failure code.",
                nameof(failureCode));
        }

        return new ReplaceTargetInventoryResult(
            query.CorrelationId,
            requestingDeviceId,
            query.TargetDeviceId,
            query.IncomingKind,
            query.Deadline,
            capturedAt,
            failureCode,
            false,
            []);
    }

    public static ReplaceTargetInventoryResult Success(
        DeviceId requestingDeviceId,
        ReplaceTargetInventoryQuery query,
        DateTimeOffset capturedAt,
        IEnumerable<ReplaceTargetSnapshot> targets,
        bool isTruncated)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(targets);
        if (capturedAt == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capturedAt),
                "A Replace inventory capture time must be initialized.");
        }

        if (capturedAt > query.Deadline)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capturedAt),
                "A successful Replace inventory must be captured by its query deadline.");
        }

        ReplaceTargetSnapshot[] snapshot = targets
            .Take(MaximumTargets + 1)
            .ToArray();
        if (snapshot.Length > MaximumTargets)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targets),
                $"A Replace inventory cannot contain more than {MaximumTargets} targets.");
        }

        if (isTruncated && snapshot.Length != MaximumTargets)
        {
            throw new ArgumentException(
                "A truncated Replace inventory must contain a full target page.",
                nameof(isTruncated));
        }

        for (int index = 0; index < snapshot.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(snapshot[index]);
            if (index > 0
                && StringComparer.Ordinal.Compare(
                    snapshot[index - 1].ActivityId.ToString(),
                    snapshot[index].ActivityId.ToString()) >= 0)
            {
                throw new ArgumentException(
                    "Replace inventory targets must be uniquely ordered by Activity ID.",
                    nameof(targets));
            }
        }

        return new ReplaceTargetInventoryResult(
            query.CorrelationId,
            requestingDeviceId,
            query.TargetDeviceId,
            query.IncomingKind,
            query.Deadline,
            capturedAt,
            FailureCode.None,
            isTruncated,
            snapshot.ToImmutableArray());
    }
}

public interface IReplaceTargetInventoryPeer
{
    public DeviceId DeviceId { get; }

    public ValueTask<ReplaceTargetInventoryResult> QueryAsync(
        DeviceId requestingDeviceId,
        ReplaceTargetInventoryQuery query,
        CancellationToken cancellationToken);
}

public interface IReplaceTargetInventoryChannel
{
    public DeviceId TargetDeviceId { get; }

    public ValueTask<ReplaceTargetInventoryDeliveryResult> QueryAsync(
        DeviceId requestingDeviceId,
        ReplaceTargetInventoryQuery query,
        CancellationToken cancellationToken);
}

public readonly record struct ReplaceTargetInventoryDeliveryResult(
    ActivityDeliveryStatus Status,
    ReplaceTargetInventoryResult? Result)
{
    public static ReplaceTargetInventoryDeliveryResult Acknowledged(
        ReplaceTargetInventoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ReplaceTargetInventoryDeliveryResult(
            ActivityDeliveryStatus.Acknowledged,
            result);
    }

    public static ReplaceTargetInventoryDeliveryResult NotDelivered { get; } =
        new(ActivityDeliveryStatus.NotDelivered, null);

    public static ReplaceTargetInventoryDeliveryResult AcknowledgementLost { get; } =
        new(ActivityDeliveryStatus.AcknowledgementLost, null);
}

public sealed class ReplaceTargetInventoryEndpoint : IReplaceTargetInventoryPeer
{
    public const int MaximumTargets = ReplaceTargetInventoryResult.MaximumTargets;

    private readonly ActivityAdapterRegistry adapterRegistry;
    private readonly IClock clock;
    private readonly ConcurrentDictionary<DeviceId, CapabilityGrant> peerGrants = new();
    private readonly IActivitySnapshotSource snapshotSource;

    public ReplaceTargetInventoryEndpoint(
        DeviceId deviceId,
        IClock clock,
        IActivitySnapshotSource snapshotSource,
        ActivityAdapterRegistry adapterRegistry)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(snapshotSource);
        ArgumentNullException.ThrowIfNull(adapterRegistry);
        DeviceId = deviceId;
        this.clock = clock;
        this.snapshotSource = snapshotSource;
        this.adapterRegistry = adapterRegistry;
    }

    public DeviceId DeviceId { get; }

    public void SetPeerGrant(DeviceId peerDeviceId, CapabilityGrant grant)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentNullException.ThrowIfNull(grant);
        peerGrants[peerDeviceId] = grant;
    }

    public ValueTask<ReplaceTargetInventoryResult> QueryAsync(
        DeviceId requestingDeviceId,
        ReplaceTargetInventoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestingDeviceId);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (query.TargetDeviceId != DeviceId)
        {
            throw new ArgumentException(
                "A Replace inventory query targets another device.",
                nameof(query));
        }

        DateTimeOffset capturedAt = clock.UtcNow;
        FailureCode failureCode = query.Deadline <= capturedAt
            ? FailureCode.DeadlineExpired
            : !peerGrants.TryGetValue(requestingDeviceId, out CapabilityGrant? grant)
                || !grant.Allows(Capability.ActivityReplace)
                ? FailureCode.CapabilityDenied
                : FailureCode.None;
        ReplaceTargetInventoryResult result;
        if (failureCode != FailureCode.None)
        {
            result = ReplaceTargetInventoryResult.Rejected(
                requestingDeviceId,
                query,
                capturedAt,
                failureCode);
        }
        else if (!adapterRegistry.TryFind(
                     query.IncomingKind,
                     out IActivityAdapter? incomingAdapter)
                 || incomingAdapter is not IReplaceActivityAdapter)
        {
            result = ReplaceTargetInventoryResult.Rejected(
                requestingDeviceId,
                query,
                capturedAt,
                FailureCode.AdapterUnavailable);
        }
        else
        {
            ReplaceTargetSnapshot[] available = snapshotSource.GetSnapshot()
                .Where(activity => IsEligible(activity, query.IncomingKind))
                .OrderBy(
                    static activity => activity.Descriptor.Id.ToString(),
                    StringComparer.Ordinal)
                .Select(static activity => ReplaceTargetSnapshot.Create(
                    activity.Descriptor.Id,
                    activity.Revision,
                    activity.Descriptor.DescriptorDigest,
                    activity.Descriptor.Kind,
                    activity.Descriptor.Title,
                    activity.Placement.Slot))
                .ToArray();
            result = ReplaceTargetInventoryResult.Success(
                requestingDeviceId,
                query,
                capturedAt,
                available.Take(MaximumTargets),
                available.Length > MaximumTargets);
        }

        return ValueTask.FromResult(result);
    }

    private bool IsEligible(ActivityInstance activity, ActivityKind incomingKind) =>
        activity.Placement.DeviceId == DeviceId
        && activity.Lifecycle == ActivityLifecycle.Active
        && activity.Descriptor.Sensitivity == ActivitySensitivity.Normal
        && activity.Descriptor.Kind == incomingKind
        && adapterRegistry.TryFind(
            activity.Descriptor.Kind,
            out IActivityAdapter? adapter)
        && adapter is IReplaceActivityAdapter;
}
