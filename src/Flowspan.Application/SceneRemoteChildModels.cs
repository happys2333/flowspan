using Flowspan.Domain;

namespace Flowspan.Application;

public sealed record SceneRemoteChildInstruction
{
    private SceneRemoteChildInstruction(
        DeviceId coordinatorDeviceId,
        SceneId sceneId,
        long sceneRevision,
        string sceneDigest,
        string previewFingerprint,
        OperationId parentOperationId,
        CorrelationId parentCorrelationId,
        DateTimeOffset acceptedAt,
        SceneApplyItemPreview item)
    {
        CoordinatorDeviceId = coordinatorDeviceId;
        SceneId = sceneId;
        SceneRevision = sceneRevision;
        SceneDigest = sceneDigest;
        PreviewFingerprint = previewFingerprint;
        ParentOperationId = parentOperationId;
        ParentCorrelationId = parentCorrelationId;
        AcceptedAt = acceptedAt;
        Item = item;
        BindingDigest = SceneApplyBinding.ComputeRemoteChildInstructionDigest(this);
    }

    public DeviceId CoordinatorDeviceId { get; }

    public DeviceId SourceDeviceId => Item.Source!.DeviceId;

    public DeviceId TargetDeviceId => Item.Destination.DeviceId;

    public SceneId SceneId { get; }

    public long SceneRevision { get; }

    public string SceneDigest { get; }

    public string PreviewFingerprint { get; }

    public OperationId ParentOperationId { get; }

    public CorrelationId ParentCorrelationId { get; }

    public DateTimeOffset AcceptedAt { get; }

    public string BindingDigest { get; }

    public DateTimeOffset Deadline =>
        AcceptedAt.Add(SceneApplyPreview.MaximumLifetime);

    public SceneApplyItemPreview Item { get; }

    public static SceneRemoteChildInstruction Create(
        DeviceId coordinatorDeviceId,
        SceneId sceneId,
        long sceneRevision,
        string sceneDigest,
        string previewFingerprint,
        OperationId parentOperationId,
        CorrelationId parentCorrelationId,
        DateTimeOffset acceptedAt,
        SceneApplyItemPreview item)
    {
        ArgumentNullException.ThrowIfNull(coordinatorDeviceId);
        ArgumentNullException.ThrowIfNull(sceneId);
        ArgumentOutOfRangeException.ThrowIfLessThan(sceneRevision, 1);
        string canonicalSceneDigest = SceneApplyBinding.ValidateDigest(
            sceneDigest,
            nameof(sceneDigest));
        string canonicalPreviewFingerprint = SceneApplyBinding.ValidateDigest(
            previewFingerprint,
            nameof(previewFingerprint));
        ArgumentNullException.ThrowIfNull(parentOperationId);
        ArgumentNullException.ThrowIfNull(parentCorrelationId);
        ArgumentNullException.ThrowIfNull(item);
        if (item.Action is not (
            SceneApplyAction.Handoff
            or SceneApplyAction.Move
            or SceneApplyAction.Replace)
            || item.Reason != SceneApplyItemReason.None
            || item.Source is null)
        {
            throw new ArgumentException(
                "A remote Scene child instruction requires one executable exact-source item.",
                nameof(item));
        }

        if (coordinatorDeviceId == item.Source.DeviceId)
        {
            throw new ArgumentException(
                "A remote Scene child instruction source must differ from its coordinator.",
                nameof(coordinatorDeviceId));
        }

        return new SceneRemoteChildInstruction(
            coordinatorDeviceId,
            sceneId,
            sceneRevision,
            canonicalSceneDigest,
            canonicalPreviewFingerprint,
            parentOperationId,
            parentCorrelationId,
            acceptedAt.ToUniversalTime(),
            item);
    }

    public override string ToString() =>
        $"Remote Scene child {Item.Index} ({Item.Action})";
}
