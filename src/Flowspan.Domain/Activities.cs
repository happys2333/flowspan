using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Flowspan.Domain;

public sealed record ActivityKind
{
    private ActivityKind(string value) => Value = value;

    public string Value { get; }

    public static ActivityKind Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string[] parts = value.Split("/v", StringSplitOptions.None);
        bool validPrefix = parts.Length == 2
            && parts[0].Length is > 0 and <= 64
            && char.IsAsciiLetterLower(parts[0][0])
            && parts[0].All(static character =>
                char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character is '.' or '-');
        bool validVersion = parts.Length == 2
            && parts[1].Length > 0
            && parts[1].All(char.IsAsciiDigit)
            && parts[1][0] != '0'
            && int.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int version)
            && version > 0
            && version <= 9999;

        if (!validPrefix || !validVersion)
        {
            throw new FormatException(
                "An Activity kind must match '<lowercase-name>/v<positive-version>'.");
        }

        return new ActivityKind(value);
    }

    public override string ToString() => Value;
}

public enum ActivitySensitivity
{
    Normal,
    Sensitive,
    Restricted,
}

public sealed record ActivityDescriptor
{
    public const int MaximumPayloadBytes = 64 * 1024;
    public const int MaximumTitleCharacters = 120;

    private ActivityDescriptor(
        ActivityId id,
        ActivityKind kind,
        DeviceId originDeviceId,
        string title,
        string payloadJson,
        string payloadDigest,
        string descriptorDigest,
        ActivitySensitivity sensitivity)
    {
        Id = id;
        Kind = kind;
        OriginDeviceId = originDeviceId;
        Title = title;
        PayloadJson = payloadJson;
        PayloadDigest = payloadDigest;
        DescriptorDigest = descriptorDigest;
        Sensitivity = sensitivity;
    }

    public ActivityId Id { get; }

    public ActivityKind Kind { get; }

    public DeviceId OriginDeviceId { get; }

    public string Title { get; }

    public string PayloadJson { get; }

    public string PayloadDigest { get; }

    public string DescriptorDigest { get; }

    public ActivitySensitivity Sensitivity { get; }

    public static ActivityDescriptor Create(
        ActivityId id,
        ActivityKind kind,
        DeviceId originDeviceId,
        string title,
        string payloadJson,
        ActivitySensitivity sensitivity = ActivitySensitivity.Normal)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(originDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        string normalizedTitle = title.Trim();
        if (normalizedTitle.Length > MaximumTitleCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(title),
                $"An Activity title cannot exceed {MaximumTitleCharacters} characters.");
        }

        if (normalizedTitle.Any(char.IsControl))
        {
            throw new ArgumentException(
                "An Activity title cannot contain control characters.",
                nameof(title));
        }

        int payloadSize = Encoding.UTF8.GetByteCount(payloadJson);
        if (payloadSize > MaximumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payloadJson),
                $"An Activity descriptor payload cannot exceed {MaximumPayloadBytes} bytes.");
        }

        using JsonDocument document = JsonDocument.Parse(
            payloadJson,
            new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "An Activity descriptor payload must be a JSON object.",
                nameof(payloadJson));
        }

        byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        string payloadDigest = Convert.ToHexString(SHA256.HashData(payloadBytes));
        string descriptorDigest = ComputeDescriptorDigest(
            id,
            kind,
            originDeviceId,
            normalizedTitle,
            payloadDigest,
            sensitivity);

        return new ActivityDescriptor(
            id,
            kind,
            originDeviceId,
            normalizedTitle,
            payloadJson,
            payloadDigest,
            descriptorDigest,
            sensitivity);
    }

    public override string ToString() =>
        $"{Kind.Value} Activity {Id} ({Sensitivity}, descriptor {DescriptorDigest})";

    private static string ComputeDescriptorDigest(
        ActivityId id,
        ActivityKind kind,
        DeviceId originDeviceId,
        string title,
        string payloadDigest,
        ActivitySensitivity sensitivity)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendField(hash, id.ToString());
        AppendField(hash, kind.Value);
        AppendField(hash, originDeviceId.ToString());
        AppendField(hash, title);
        AppendField(hash, payloadDigest);
        AppendField(hash, sensitivity.ToString());
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendField(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

public sealed record ActivityPlacement
{
    private ActivityPlacement(DeviceId deviceId, string slot)
    {
        DeviceId = deviceId;
        Slot = slot;
    }

    public DeviceId DeviceId { get; }

    public string Slot { get; }

    public static ActivityPlacement On(DeviceId deviceId, string slot = "default")
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);

        string normalizedSlot = slot.Trim();
        if (normalizedSlot.Length > 80)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slot),
                "A placement slot cannot exceed 80 characters.");
        }

        return new ActivityPlacement(deviceId, normalizedSlot);
    }
}

public enum ActivityLifecycle
{
    Active,
    Suspended,
    Closed,
}

public sealed record ActivityInstance
{
    private ActivityInstance(
        ActivityDescriptor descriptor,
        ActivityPlacement placement,
        long revision,
        ActivityLifecycle lifecycle)
    {
        Descriptor = descriptor;
        Placement = placement;
        Revision = revision;
        Lifecycle = lifecycle;
    }

    public ActivityDescriptor Descriptor { get; }

    public ActivityPlacement Placement { get; }

    public long Revision { get; }

    public ActivityLifecycle Lifecycle { get; }

    public static ActivityInstance Active(
        ActivityDescriptor descriptor,
        ActivityPlacement placement,
        long revision = 1)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);

        return new ActivityInstance(
            descriptor,
            placement,
            revision,
            ActivityLifecycle.Active);
    }

    public ActivityInstance Close()
    {
        if (Lifecycle == ActivityLifecycle.Closed)
        {
            return this;
        }

        return new ActivityInstance(
            Descriptor,
            Placement,
            checked(Revision + 1),
            ActivityLifecycle.Closed);
    }
}
