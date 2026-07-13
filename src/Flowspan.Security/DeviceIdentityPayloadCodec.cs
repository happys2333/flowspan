using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;

namespace Flowspan.Security;

public static class DeviceIdentityPayloadCodec
{
    public const byte CurrentFormatVersion = 1;
    public const int MaximumPayloadBytes = 1024;
    private const int DeviceIdBytes = 16;
    private const int MaximumDisplayNameBytes = 320;
    private const int MaximumPrivateKeyBytes = 512;
    private static readonly byte[] Magic = "FSID"u8.ToArray();
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static byte[] Encode(DeviceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        byte[] displayName = StrictUtf8.GetBytes(identity.DisplayName);
        byte[] privateKey = identity.ExportPkcs8ForSecretStore();
        try
        {
            if (displayName.Length is < 1 or > MaximumDisplayNameBytes)
            {
                throw new InvalidDataException(
                    "The identity display name has an invalid encoded length.");
            }

            if (privateKey.Length is < 1 or > MaximumPrivateKeyBytes)
            {
                throw new InvalidDataException(
                    "The identity private key has an invalid encoded length.");
            }

            int payloadLength = checked(
                Magic.Length
                + sizeof(byte)
                + DeviceIdBytes
                + sizeof(ushort)
                + displayName.Length
                + sizeof(ushort)
                + privateKey.Length);
            if (payloadLength > MaximumPayloadBytes)
            {
                throw new InvalidDataException(
                    $"An identity payload cannot exceed {MaximumPayloadBytes} bytes.");
            }

            byte[] payload = new byte[payloadLength];
            int offset = 0;
            Magic.CopyTo(payload, offset);
            offset += Magic.Length;
            payload[offset++] = CurrentFormatVersion;
            if (!identity.DeviceId.Value.TryWriteBytes(
                    payload.AsSpan(offset, DeviceIdBytes),
                    bigEndian: true,
                    out int deviceIdBytesWritten)
                || deviceIdBytesWritten != DeviceIdBytes)
            {
                throw new InvalidOperationException("The device ID could not be encoded.");
            }

            offset += DeviceIdBytes;
            WriteBytes(payload, ref offset, displayName);
            WriteBytes(payload, ref offset, privateKey);
            return payload;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    public static DeviceIdentity Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"An identity payload must contain 1 to {MaximumPayloadBytes} bytes.");
        }

        try
        {
            var reader = new IdentityPayloadReader(payload);
            reader.RequireMagic(Magic);
            byte version = reader.ReadByte();
            if (version != CurrentFormatVersion)
            {
                throw new InvalidDataException(
                    $"Identity payload format version {version} is not supported.");
            }

            Guid deviceGuid = new(reader.ReadRaw(DeviceIdBytes), bigEndian: true);
            if (deviceGuid == Guid.Empty)
            {
                throw new InvalidDataException("The identity payload device ID is empty.");
            }

            string displayName = StrictUtf8.GetString(
                reader.ReadBytes(MaximumDisplayNameBytes, "display name"));
            string normalizedDisplayName = DeviceIdentity.NormalizeDisplayName(displayName);
            if (!StringComparer.Ordinal.Equals(displayName, normalizedDisplayName))
            {
                throw new InvalidDataException(
                    "The identity payload display name is not canonical.");
            }

            ReadOnlySpan<byte> privateKey = reader.ReadBytes(
                MaximumPrivateKeyBytes,
                "private key");
            reader.RequireEnd();
            return DeviceIdentity.ImportPkcs8(
                DeviceId.From(deviceGuid),
                normalizedDisplayName,
                privateKey);
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or CryptographicException
            or OverflowException)
        {
            throw new InvalidDataException("The identity payload is malformed.", exception);
        }
    }

    private static void WriteBytes(
        Span<byte> destination,
        ref int offset,
        ReadOnlySpan<byte> value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(
            destination[offset..],
            checked((ushort)value.Length));
        offset += sizeof(ushort);
        value.CopyTo(destination[offset..]);
        offset += value.Length;
    }
}

internal ref struct IdentityPayloadReader
{
    private readonly ReadOnlySpan<byte> source;
    private int offset;

    public IdentityPayloadReader(ReadOnlySpan<byte> source) => this.source = source;

    public byte ReadByte()
    {
        EnsureAvailable(sizeof(byte));
        return source[offset++];
    }

    public ReadOnlySpan<byte> ReadBytes(int maximumBytes, string fieldName)
    {
        EnsureAvailable(sizeof(ushort));
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(source[offset..]);
        offset += sizeof(ushort);
        if (count is 0 || count > maximumBytes)
        {
            throw new InvalidDataException(
                $"The identity payload {fieldName} length is invalid.");
        }

        return ReadRaw(count);
    }

    public ReadOnlySpan<byte> ReadRaw(int count)
    {
        EnsureAvailable(count);
        ReadOnlySpan<byte> value = source.Slice(offset, count);
        offset += count;
        return value;
    }

    public void RequireEnd()
    {
        if (offset != source.Length)
        {
            throw new InvalidDataException("The identity payload contains trailing data.");
        }
    }

    public void RequireMagic(ReadOnlySpan<byte> expected)
    {
        if (!ReadRaw(expected.Length).SequenceEqual(expected))
        {
            throw new InvalidDataException("The identity payload magic is invalid.");
        }
    }

    private void EnsureAvailable(int count)
    {
        if (count < 0 || source.Length - offset < count)
        {
            throw new InvalidDataException("The identity payload ended unexpectedly.");
        }
    }
}
