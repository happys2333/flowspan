using Flowspan.Domain;

namespace Flowspan.Application;

public sealed record SceneUndoReplaceInstruction
{
    private SceneUndoReplaceInstruction(
        DeviceId coordinatorDeviceId,
        UndoCapsuleReference capsule,
        OperationContext context)
    {
        CoordinatorDeviceId = coordinatorDeviceId;
        Capsule = capsule;
        Context = context;
        BindingDigest = SceneApplyBinding.ComputeUndoReplaceInstructionDigest(this);
    }

    public DeviceId CoordinatorDeviceId { get; }

    public DeviceId TargetDeviceId => Capsule.TargetDeviceId;

    public UndoCapsuleReference Capsule { get; }

    public OperationContext Context { get; }

    public string BindingDigest { get; }

    public static SceneUndoReplaceInstruction Create(
        DeviceId coordinatorDeviceId,
        UndoCapsuleReference capsule,
        OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(coordinatorDeviceId);
        ArgumentNullException.ThrowIfNull(capsule);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(capsule.Id);
        ArgumentNullException.ThrowIfNull(capsule.OperationId);
        ArgumentNullException.ThrowIfNull(capsule.CorrelationId);
        ArgumentNullException.ThrowIfNull(capsule.TargetDeviceId);
        ArgumentNullException.ThrowIfNull(capsule.TargetActivityId);
        ArgumentNullException.ThrowIfNull(capsule.IncomingActivityId);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            capsule.ExpectedTargetRevision,
            1);
        SceneApplyBinding.ValidateDigest(
            capsule.TargetDescriptorDigest,
            nameof(capsule));
        SceneApplyBinding.ValidateDigest(
            capsule.IncomingDescriptorDigest,
            nameof(capsule));
        if (coordinatorDeviceId == capsule.TargetDeviceId)
        {
            throw new ArgumentException(
                "A remote Scene undo target must differ from its coordinator.",
                nameof(capsule));
        }

        DateTimeOffset expiresAt = capsule.ExpiresAt.ToUniversalTime();
        if (capsule.ExpiresAt.Offset != TimeSpan.Zero
            || context.Deadline.Offset != TimeSpan.Zero
            || context.Deadline != expiresAt)
        {
            throw new ArgumentException(
                "A remote Scene undo deadline must be the canonical capsule expiry.",
                nameof(context));
        }

        return new SceneUndoReplaceInstruction(
            coordinatorDeviceId,
            capsule,
            context);
    }

    public override string ToString() =>
        $"Remote Scene undo {Capsule.Id} on {TargetDeviceId}";
}
