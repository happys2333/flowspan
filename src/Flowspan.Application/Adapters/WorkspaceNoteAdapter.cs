using System.Text.Json;
using Flowspan.Domain;

namespace Flowspan.Application.Adapters;

public sealed class WorkspaceNoteAdapter : IReplaceActivityAdapter
{
    public const int MaximumTextCharacters = 16 * 1024;

    public ActivityKind Kind { get; } = ActivityKind.Parse("workspace.note/v1");

    public ValueTask<ResumeActivityResult> ResumeAsync(
        ActivityDescriptor descriptor,
        ActivityPlacement placement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(placement);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsValidDescriptor(descriptor))
        {
            return ValueTask.FromResult(
                ResumeActivityResult.Rejected(FailureCode.DescriptorRejected));
        }

        return ValueTask.FromResult(ResumeActivityResult.Success);
    }

    public ValueTask<CloseActivityResult> CloseAsync(
        ActivityInstance activity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activity);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(CloseActivityResult.Success);
    }

    public ValueTask<CaptureUndoResult> CaptureUndoAsync(
        ActivityInstance activity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activity);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            activity.Lifecycle == ActivityLifecycle.Active
            && IsValidDescriptor(activity.Descriptor)
                ? CaptureUndoResult.Success(activity.Descriptor)
                : CaptureUndoResult.Rejected(FailureCode.UndoUnavailable));
    }

    public ValueTask<RestoreActivityResult> RestoreAsync(
        UndoCapsule capsule,
        ActivityPlacement placement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capsule);
        ArgumentNullException.ThrowIfNull(placement);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            placement == capsule.OriginalActivity.Placement
            && IsValidDescriptor(capsule.OriginalActivity.Descriptor)
                ? RestoreActivityResult.Success
                : RestoreActivityResult.Rejected(FailureCode.UndoCapsuleInvalid));
    }

    private bool IsValidDescriptor(ActivityDescriptor descriptor)
    {
        if (descriptor.Kind != Kind)
        {
            return false;
        }

        using JsonDocument document = JsonDocument.Parse(descriptor.PayloadJson);
        JsonElement root = document.RootElement;
        return root.TryGetProperty("text", out JsonElement textElement)
            && textElement.ValueKind == JsonValueKind.String
            && textElement.GetString() is string text
            && text.Length is >= 1 and <= MaximumTextCharacters
            && root.EnumerateObject().All(static property => property.Name == "text");
    }
}
