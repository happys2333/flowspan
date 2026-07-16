using System.Text.Encodings.Web;
using System.Text.Json;
using Flowspan.Domain;

namespace Flowspan.Application;

public static class ScenePlanCodec
{
    public const int MaximumEncodedBytes = 32 * 1024;

    public static byte[] Encode(ScenePlan scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
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
            writer.WriteNumber("formatVersion", scene.FormatVersion);
            writer.WriteString("sceneId", scene.Id.ToString());
            writer.WriteNumber("revision", scene.Revision);
            writer.WriteString("name", scene.Name);
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

            writer.WriteStartArray("activities");
            foreach (SceneActivityPlan activity in scene.Activities)
            {
                writer.WriteStartObject();
                writer.WriteString("activityId", activity.ActivityId.ToString());
                writer.WriteString(
                    "deviceId",
                    activity.Placement.DeviceId.ToString());
                writer.WriteString("slot", activity.Placement.Slot);
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

        byte[] encoded = output.ToArray();
        if (encoded.Length > MaximumEncodedBytes)
        {
            throw new InvalidOperationException(
                $"The canonical Scene plan exceeds {MaximumEncodedBytes} bytes.");
        }

        return encoded;
    }

    public static ScenePlan Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.IsEmpty || encoded.Length > MaximumEncodedBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(encoded),
                $"A Scene plan must contain from 1 through {MaximumEncodedBytes} bytes.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                encoded.ToArray(),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 8,
                });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "A Scene plan must be a JSON object.");
            }

            ValidateProperties(
                root,
                "formatVersion",
                "sceneId",
                "revision",
                "name",
                "group",
                "activities");

            int formatVersion = root.GetProperty("formatVersion").GetInt32();
            if (formatVersion != ScenePlan.CurrentFormatVersion)
            {
                throw new InvalidDataException(
                    $"Scene format version {formatVersion} is not supported.");
            }

            SceneId sceneId = SceneId.From(ReadCanonicalGuid(root, "sceneId"));
            long revision = root.GetProperty("revision").GetInt64();
            string name = root.GetProperty("name").GetString()
                ?? throw new InvalidDataException("A Scene name is required.");
            SceneGroupBinding? groupBinding = DecodeGroup(
                root.GetProperty("group"));
            JsonElement activitiesElement = root.GetProperty("activities");
            if (activitiesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "Scene activities must be a JSON array.");
            }

            var activities = new List<SceneActivityPlan>();
            foreach (JsonElement activity in activitiesElement.EnumerateArray())
            {
                activities.Add(DecodeActivity(activity));
            }

            return ScenePlan.Restore(
                sceneId,
                revision,
                name,
                groupBinding,
                activities);
        }
        catch (Exception exception) when (exception is JsonException
            or KeyNotFoundException
            or FormatException
            or ArgumentException
            or InvalidOperationException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The Scene plan is malformed.",
                exception);
        }
    }

    private static SceneGroupBinding? DecodeGroup(JsonElement group)
    {
        if (group.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (group.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "A Scene Group binding must be an object or null.");
        }

        ValidateProperties(group, "groupId", "revision");

        GroupId groupId = GroupId.From(ReadCanonicalGuid(group, "groupId"));
        long revision = group.GetProperty("revision").GetInt64();
        return SceneGroupBinding.Create(groupId, revision);
    }

    private static SceneActivityPlan DecodeActivity(JsonElement activity)
    {
        if (activity.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "A Scene Activity plan must be a JSON object.");
        }

        ValidateProperties(
            activity,
            "activityId",
            "deviceId",
            "slot",
            "sourceDisposition",
            "conflictPolicy");

        ActivityId activityId = ActivityId.From(
            ReadCanonicalGuid(activity, "activityId"));
        DeviceId deviceId = DeviceId.From(
            ReadCanonicalGuid(activity, "deviceId"));
        string slot = activity.GetProperty("slot").GetString()
            ?? throw new InvalidDataException("A placement slot is required.");
        string sourceDisposition = activity.GetProperty("sourceDisposition")
            .GetString()
            ?? throw new InvalidDataException(
                "A source disposition is required.");
        string conflictPolicy = activity.GetProperty("conflictPolicy")
            .GetString()
            ?? throw new InvalidDataException("A conflict policy is required.");
        return SceneActivityPlan.Place(
            activityId,
            ActivityPlacement.On(deviceId, slot),
            ParseSourceDisposition(sourceDisposition),
            ParseConflictPolicy(conflictPolicy));
    }

    private static void ValidateProperties(
        JsonElement element,
        params string[] expectedNames)
    {
        var expected = new HashSet<string>(expectedNames, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!expected.Contains(property.Name) || !seen.Add(property.Name))
            {
                throw new InvalidDataException(
                    "The Scene plan contains an unknown or duplicate property.");
            }
        }

        if (!seen.SetEquals(expected))
        {
            throw new InvalidDataException(
                "The Scene plan is missing a required property.");
        }
    }

    private static Guid ReadCanonicalGuid(
        JsonElement element,
        string propertyName)
    {
        string value = element.GetProperty(propertyName).GetString()
            ?? throw new InvalidDataException(
                $"The '{propertyName}' identifier is required.");
        if (!Guid.TryParseExact(value, "D", out Guid parsed)
            || !StringComparer.Ordinal.Equals(value, parsed.ToString("D")))
        {
            throw new InvalidDataException(
                $"The '{propertyName}' identifier is not canonical.");
        }

        return parsed;
    }

    private static SceneSourceDisposition ParseSourceDisposition(string value) =>
        value switch
        {
            "preserve-source" => SceneSourceDisposition.PreserveSource,
            "move-after-acknowledgement" =>
                SceneSourceDisposition.MoveAfterAcknowledgement,
            _ => throw new InvalidDataException(
                "The Scene source disposition is not supported."),
        };

    private static SceneConflictPolicy ParseConflictPolicy(string value) =>
        value switch
        {
            "require-empty" => SceneConflictPolicy.RequireEmpty,
            "replace-with-undo" => SceneConflictPolicy.ReplaceWithUndo,
            _ => throw new InvalidDataException(
                "The Scene conflict policy is not supported."),
        };

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
