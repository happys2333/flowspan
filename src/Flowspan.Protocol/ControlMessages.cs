using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Flowspan.Domain;

namespace Flowspan.Protocol;

public enum ControlMessageType
{
    Hello,
    ActivityTransfer,
    ActivityReplaceInventory,
    ActivityReplaceInventoryResult,
    ActivityReplace,
    ActivityReplaceResult,
    ActivitySwapSnapshot,
    ActivitySwapSnapshotResult,
    ActivitySwapPrepare,
    ActivitySwapPrepareResult,
    ActivitySwapDecision,
    ActivitySwapDecisionResult,
    OperationReceipt,
    SceneSourceLookup,
    SceneSourceLookupResult,
    SceneSlotInspection,
    SceneSlotInspectionResult,
    SceneChildOperation,
    SceneChildOperationResult,
}

public sealed record ControlMessage
{
    public const string Magic = "FSPN";
    public const int MaximumBodyBytes = 192 * 1024;
    public const int MaximumTimeToLiveMilliseconds = 5 * 60 * 1000;

    private ControlMessage(
        ProtocolVersion version,
        ControlMessageType type,
        Guid messageId,
        CorrelationId correlationId,
        DeviceId senderDeviceId,
        DateTimeOffset sentAt,
        int timeToLiveMilliseconds,
        string bodyDigest,
        JsonElement body)
    {
        Version = version;
        Type = type;
        MessageId = messageId;
        CorrelationId = correlationId;
        SenderDeviceId = senderDeviceId;
        SentAt = sentAt;
        TimeToLiveMilliseconds = timeToLiveMilliseconds;
        BodyDigest = bodyDigest;
        Body = body;
    }

    public ProtocolVersion Version { get; }

    public ControlMessageType Type { get; }

    public Guid MessageId { get; }

    public CorrelationId CorrelationId { get; }

    public DeviceId SenderDeviceId { get; }

    public DateTimeOffset SentAt { get; }

    public int TimeToLiveMilliseconds { get; }

    public string BodyDigest { get; }

    public JsonElement Body { get; }

    public static ControlMessage Create(
        ProtocolVersion version,
        ControlMessageType type,
        Guid messageId,
        CorrelationId correlationId,
        DeviceId senderDeviceId,
        DateTimeOffset sentAt,
        TimeSpan timeToLive,
        string bodyJson)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(senderDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyJson);

        if (version.Major < 1 || version.Minor < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                "A control message protocol version must be initialized.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "The control message type is unknown.");
        }

        ValidateTypeVersion(version, type);

        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("A message ID cannot be empty.", nameof(messageId));
        }

        int ttlMilliseconds = ValidateTimeToLive(timeToLive);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyJson);
        if (bodyBytes.Length > MaximumBodyBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bodyJson),
                $"A control message body cannot exceed {MaximumBodyBytes} bytes.");
        }

        using JsonDocument document = JsonDocument.Parse(
            bodyBytes,
            new JsonDocumentOptions { MaxDepth = ControlMessageCodec.MaximumJsonDepth });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "A control message body must be a JSON object.",
                nameof(bodyJson));
        }

        CanonicalJson.ValidateNoDuplicateProperties(document.RootElement);
        JsonElement body = document.RootElement.Clone();
        string digest = CanonicalJson.ComputeDigest(body);

        return new ControlMessage(
            version,
            type,
            messageId,
            correlationId,
            senderDeviceId,
            sentAt,
            ttlMilliseconds,
            digest,
            body);
    }

    internal static ControlMessage FromDecoded(
        ProtocolVersion version,
        ControlMessageType type,
        Guid messageId,
        CorrelationId correlationId,
        DeviceId senderDeviceId,
        DateTimeOffset sentAt,
        int timeToLiveMilliseconds,
        string bodyDigest,
        JsonElement body)
    {
        ValidateTypeVersion(version, type);
        return new ControlMessage(
            version,
            type,
            messageId,
            correlationId,
            senderDeviceId,
            sentAt,
            ValidateTimeToLive(TimeSpan.FromMilliseconds(timeToLiveMilliseconds)),
            bodyDigest,
            body);
    }

    private static void ValidateTypeVersion(
        ProtocolVersion version,
        ControlMessageType type)
    {
        bool swapMessage = type is
            ControlMessageType.ActivitySwapSnapshot
            or ControlMessageType.ActivitySwapSnapshotResult
            or ControlMessageType.ActivitySwapPrepare
            or ControlMessageType.ActivitySwapPrepareResult
            or ControlMessageType.ActivitySwapDecision
            or ControlMessageType.ActivitySwapDecisionResult;
        if (swapMessage && !ProtocolFeatures.SupportsActivitySwap(version))
        {
            throw new ArgumentException(
                $"The '{type}' control message requires protocol {ProtocolFeatures.ActivitySwapMinimumVersion} or later.",
                nameof(version));
        }

        bool sceneMessage = type is
            ControlMessageType.SceneSourceLookup
            or ControlMessageType.SceneSourceLookupResult
            or ControlMessageType.SceneSlotInspection
            or ControlMessageType.SceneSlotInspectionResult
            or ControlMessageType.SceneChildOperation
            or ControlMessageType.SceneChildOperationResult;
        if (sceneMessage && !ProtocolFeatures.SupportsSceneApply(version))
        {
            throw new ArgumentException(
                $"The '{type}' control message requires protocol {ProtocolFeatures.SceneApplyMinimumVersion} or later.",
                nameof(version));
        }
    }

    private static int ValidateTimeToLive(TimeSpan timeToLive)
    {
        double milliseconds = timeToLive.TotalMilliseconds;
        if (!double.IsInteger(milliseconds)
            || milliseconds is < 1 or > MaximumTimeToLiveMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeToLive),
                $"A control message TTL must be a whole number from 1 to {MaximumTimeToLiveMilliseconds} milliseconds.");
        }

        return checked((int)milliseconds);
    }
}

public static class ControlMessageCodec
{
    public const int MaximumFrameBytes = 256 * 1024;
    public const int MaximumJsonDepth = 32;

    public static byte[] Encode(ControlMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.Default,
                Indented = false,
            }))
        {
            writer.WriteStartObject();
            writer.WriteString("magic", ControlMessage.Magic);
            writer.WritePropertyName("protocol");
            writer.WriteStartObject();
            writer.WriteNumber("major", message.Version.Major);
            writer.WriteNumber("minor", message.Version.Minor);
            writer.WriteEndObject();
            writer.WriteString("type", ToWireName(message.Type));
            writer.WriteString("messageId", message.MessageId);
            writer.WriteString("correlationId", message.CorrelationId.ToString());
            writer.WriteString("senderDeviceId", message.SenderDeviceId.ToString());
            writer.WriteString("sentAt", message.SentAt);
            writer.WriteNumber("ttlMs", message.TimeToLiveMilliseconds);
            writer.WriteString("bodyDigest", message.BodyDigest);
            writer.WritePropertyName("body");
            CanonicalJson.Write(writer, message.Body);
            writer.WriteEndObject();
        }

        if (buffer.WrittenCount > MaximumFrameBytes)
        {
            throw new InvalidDataException(
                $"The encoded control frame exceeds {MaximumFrameBytes} bytes.");
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static ControlMessage Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.IsEmpty || frame.Length > MaximumFrameBytes)
        {
            throw new InvalidDataException(
                $"A control frame must contain 1 to {MaximumFrameBytes} bytes.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                frame.ToArray(),
                new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("A control frame root must be an object.");
            }

            CanonicalJson.ValidateNoDuplicateProperties(root);
            RequireString(root, "magic", out string magic);
            if (!StringComparer.Ordinal.Equals(magic, ControlMessage.Magic))
            {
                throw new InvalidDataException("The control frame magic is invalid.");
            }

            JsonElement protocol = Require(root, "protocol", JsonValueKind.Object);
            int major = RequireInt32(protocol, "major");
            int minor = RequireInt32(protocol, "minor");
            var version = new ProtocolVersion(major, minor);
            RequireString(root, "type", out string typeName);
            ControlMessageType type = FromWireName(typeName);
            Guid messageId = RequireGuid(root, "messageId");
            CorrelationId correlationId = CorrelationId.From(
                RequireGuid(root, "correlationId"));
            DeviceId senderDeviceId = DeviceId.From(RequireGuid(root, "senderDeviceId"));
            DateTimeOffset sentAt = RequireDateTimeOffset(root, "sentAt");
            int ttl = RequireInt32(root, "ttlMs");
            RequireString(root, "bodyDigest", out string claimedDigest);
            JsonElement bodyElement = Require(root, "body", JsonValueKind.Object);
            if (Encoding.UTF8.GetByteCount(bodyElement.GetRawText())
                > ControlMessage.MaximumBodyBytes)
            {
                throw new InvalidDataException("The control message body is too large.");
            }

            ValidateDigest(claimedDigest, bodyElement);
            return ControlMessage.FromDecoded(
                version,
                type,
                messageId,
                correlationId,
                senderDeviceId,
                sentAt,
                ttl,
                claimedDigest,
                bodyElement.Clone());
        }
        catch (Exception exception) when (exception is
            JsonException
            or KeyNotFoundException
            or FormatException
            or OverflowException
            or ArgumentException)
        {
            throw new InvalidDataException("The control frame is malformed.", exception);
        }
    }

    private static JsonElement Require(
        JsonElement parent,
        string propertyName,
        JsonValueKind kind)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != kind)
        {
            throw new InvalidDataException(
                $"The required '{propertyName}' property is missing or has the wrong type.");
        }

        return value;
    }

    private static void RequireString(
        JsonElement parent,
        string propertyName,
        out string value)
    {
        JsonElement element = Require(parent, propertyName, JsonValueKind.String);
        value = element.GetString()
            ?? throw new InvalidDataException($"The '{propertyName}' property is null.");
    }

    private static int RequireInt32(JsonElement parent, string propertyName)
    {
        JsonElement element = Require(parent, propertyName, JsonValueKind.Number);
        if (!element.TryGetInt32(out int value))
        {
            throw new InvalidDataException($"The '{propertyName}' property is not an integer.");
        }

        return value;
    }

    private static Guid RequireGuid(JsonElement parent, string propertyName)
    {
        RequireString(parent, propertyName, out string value);
        return Guid.TryParseExact(value, "D", out Guid parsed) && parsed != Guid.Empty
            ? parsed
            : throw new InvalidDataException($"The '{propertyName}' property is not a valid ID.");
    }

    private static DateTimeOffset RequireDateTimeOffset(
        JsonElement parent,
        string propertyName)
    {
        JsonElement element = Require(parent, propertyName, JsonValueKind.String);
        return element.TryGetDateTimeOffset(out DateTimeOffset value)
            ? value
            : throw new InvalidDataException(
                $"The '{propertyName}' property is not a timestamp with an offset.");
    }

    private static void ValidateDigest(string claimedDigest, JsonElement body)
    {
        byte[] claimed;
        try
        {
            claimed = Convert.FromHexString(claimedDigest);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The body digest is not hexadecimal.", exception);
        }

        byte[] actual = Convert.FromHexString(CanonicalJson.ComputeDigest(body));
        if (claimed.Length != actual.Length
            || !CryptographicOperations.FixedTimeEquals(claimed, actual))
        {
            throw new InvalidDataException("The control message body digest does not match.");
        }
    }

    private static string ToWireName(ControlMessageType type) => type switch
    {
        ControlMessageType.Hello => "hello",
        ControlMessageType.ActivityTransfer => "activity.transfer",
        ControlMessageType.ActivityReplaceInventory => "activity.replace.inventory",
        ControlMessageType.ActivityReplaceInventoryResult => "activity.replace.inventory.result",
        ControlMessageType.ActivityReplace => "activity.replace",
        ControlMessageType.ActivityReplaceResult => "activity.replace.result",
        ControlMessageType.ActivitySwapSnapshot => "activity.swap.snapshot",
        ControlMessageType.ActivitySwapSnapshotResult => "activity.swap.snapshot.result",
        ControlMessageType.ActivitySwapPrepare => "activity.swap.prepare",
        ControlMessageType.ActivitySwapPrepareResult => "activity.swap.prepare.result",
        ControlMessageType.ActivitySwapDecision => "activity.swap.decision",
        ControlMessageType.ActivitySwapDecisionResult => "activity.swap.decision.result",
        ControlMessageType.OperationReceipt => "operation.receipt",
        ControlMessageType.SceneSourceLookup => "scene.source.lookup",
        ControlMessageType.SceneSourceLookupResult => "scene.source.lookup.result",
        ControlMessageType.SceneSlotInspection => "scene.slot.inspection",
        ControlMessageType.SceneSlotInspectionResult => "scene.slot.inspection.result",
        ControlMessageType.SceneChildOperation => "scene.child.operation",
        ControlMessageType.SceneChildOperationResult => "scene.child.operation.result",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown message type."),
    };

    private static ControlMessageType FromWireName(string type) => type switch
    {
        "hello" => ControlMessageType.Hello,
        "activity.transfer" => ControlMessageType.ActivityTransfer,
        "activity.replace.inventory" => ControlMessageType.ActivityReplaceInventory,
        "activity.replace.inventory.result" => ControlMessageType.ActivityReplaceInventoryResult,
        "activity.replace" => ControlMessageType.ActivityReplace,
        "activity.replace.result" => ControlMessageType.ActivityReplaceResult,
        "activity.swap.snapshot" => ControlMessageType.ActivitySwapSnapshot,
        "activity.swap.snapshot.result" => ControlMessageType.ActivitySwapSnapshotResult,
        "activity.swap.prepare" => ControlMessageType.ActivitySwapPrepare,
        "activity.swap.prepare.result" => ControlMessageType.ActivitySwapPrepareResult,
        "activity.swap.decision" => ControlMessageType.ActivitySwapDecision,
        "activity.swap.decision.result" => ControlMessageType.ActivitySwapDecisionResult,
        "operation.receipt" => ControlMessageType.OperationReceipt,
        "scene.source.lookup" => ControlMessageType.SceneSourceLookup,
        "scene.source.lookup.result" => ControlMessageType.SceneSourceLookupResult,
        "scene.slot.inspection" => ControlMessageType.SceneSlotInspection,
        "scene.slot.inspection.result" => ControlMessageType.SceneSlotInspectionResult,
        "scene.child.operation" => ControlMessageType.SceneChildOperation,
        "scene.child.operation.result" => ControlMessageType.SceneChildOperationResult,
        _ => throw new InvalidDataException($"The control message type '{type}' is unknown."),
    };
}

internal static class CanonicalJson
{
    public static string ComputeDigest(JsonElement element) =>
        Convert.ToHexString(SHA256.HashData(Serialize(element)));

    public static void ValidateNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException(
                        $"The JSON property '{property.Name}' appears more than once.");
                }

                ValidateNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                ValidateNoDuplicateProperties(item);
            }
        }
    }

    public static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        ArgumentNullException.ThrowIfNull(writer);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element
                    .EnumerateObject()
                    .OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Write(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException("Undefined JSON values are not supported.");
        }
    }

    private static byte[] Serialize(JsonElement element)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions { Encoder = JavaScriptEncoder.Default }))
        {
            Write(writer, element);
        }

        return buffer.WrittenSpan.ToArray();
    }
}
