using System.Text.Json;
using Flowspan.Domain;

namespace Flowspan.Application.Adapters;

public sealed class WorkspaceNoteAdapter : IActivityAdapter
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

        if (descriptor.Kind != Kind)
        {
            return ValueTask.FromResult(
                ResumeActivityResult.Rejected(FailureCode.DescriptorRejected));
        }

        using JsonDocument document = JsonDocument.Parse(descriptor.PayloadJson);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("text", out JsonElement textElement)
            || textElement.ValueKind != JsonValueKind.String
            || textElement.GetString() is not string text
            || text.Length > MaximumTextCharacters
            || root.EnumerateObject().Any(static property => property.Name != "text"))
        {
            return ValueTask.FromResult(
                ResumeActivityResult.Rejected(FailureCode.DescriptorRejected));
        }

        return ValueTask.FromResult(ResumeActivityResult.Success);
    }
}
