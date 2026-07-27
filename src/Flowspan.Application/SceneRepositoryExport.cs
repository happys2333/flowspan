using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Flowspan.Domain;

namespace Flowspan.Application;

public static class SceneRepositoryExport
{
    public const string ExportKind = "flowspan.scene-export.redacted/v1";
    public const int CurrentFormatVersion = 1;

    public static byte[] EncodeRedacted(
        SceneRepositoryEntry entry,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (exportedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A Scene export timestamp must be UTC.",
                nameof(exportedAt));
        }

        ScenePlan scene = entry.Scene;
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            output,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = false,
            }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", CurrentFormatVersion);
            writer.WriteString("exportKind", ExportKind);
            writer.WriteString("exportedAt", FormatTimestamp(exportedAt));
            writer.WriteString("sceneId", scene.Id.ToString());
            writer.WriteNumber("sceneRevision", scene.Revision);
            writer.WriteNumber("sceneFormatVersion", scene.FormatVersion);
            writer.WriteString("sceneDigest", entry.SceneDigest);
            writer.WriteString("savedAt", FormatTimestamp(entry.SavedAt));
            writer.WritePropertyName("group");
            if (scene.GroupBinding is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStartObject();
                writer.WriteString(
                    "groupId",
                    scene.GroupBinding.GroupId.ToString());
                writer.WriteNumber(
                    "revision",
                    scene.GroupBinding.GroupRevision);
                writer.WriteEndObject();
            }

            writer.WriteNumber("activityCount", scene.Activities.Length);
            writer.WriteStartArray("activities");
            for (int index = 0; index < scene.Activities.Length; index++)
            {
                SceneActivityPlan activity = scene.Activities[index];
                writer.WriteStartObject();
                writer.WriteNumber("index", index);
                writer.WriteString(
                    "sourceDisposition",
                    Format(activity.SourceDisposition));
                writer.WriteString(
                    "conflictPolicy",
                    Format(activity.ConflictPolicy));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return output.ToArray();
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToString("O", CultureInfo.InvariantCulture);

    private static string Format(SceneSourceDisposition disposition) =>
        disposition switch
        {
            SceneSourceDisposition.PreserveSource => "preserve-source",
            SceneSourceDisposition.MoveAfterAcknowledgement =>
                "move-after-acknowledgement",
            _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
        };

    private static string Format(SceneConflictPolicy policy) => policy switch
    {
        SceneConflictPolicy.RequireEmpty => "require-empty",
        SceneConflictPolicy.ReplaceWithUndo => "replace-with-undo",
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };
}
