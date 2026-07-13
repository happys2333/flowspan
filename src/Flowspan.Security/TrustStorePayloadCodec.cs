using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;

namespace Flowspan.Security;

public static class TrustStorePayloadCodec
{
    public const byte CurrentFormatVersion = 1;
    public const int MaximumPeerCount = 64;
    public const int MaximumPayloadBytes = 64 * 1024;
    private const int DeviceIdBytes = 16;
    private const int MaximumDisplayNameBytes = 320;
    private const int MaximumPublicKeyBytes = 1024;
    private static readonly byte[] Magic = "FSTR"u8.ToArray();
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly ulong KnownCapabilityMask = BuildKnownCapabilityMask();

    public static IReadOnlyList<TrustRecord> Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"A trust payload must contain 1 to {MaximumPayloadBytes} bytes.");
        }

        try
        {
            var reader = new TrustPayloadReader(payload);
            reader.RequireMagic(Magic);
            byte version = reader.ReadByte();
            if (version != CurrentFormatVersion)
            {
                throw new InvalidDataException(
                    $"Trust payload format version {version} is not supported.");
            }

            ushort count = reader.ReadUInt16();
            if (count > MaximumPeerCount)
            {
                throw new InvalidDataException(
                    $"A trust payload cannot contain more than {MaximumPeerCount} peers.");
            }

            var records = new List<TrustRecord>(count);
            string? previousDeviceId = null;
            for (int index = 0; index < count; index++)
            {
                Guid guid = new(reader.ReadRaw(DeviceIdBytes), bigEndian: true);
                DeviceId deviceId = DeviceId.From(guid);
                string canonicalDeviceId = deviceId.ToString();
                if (previousDeviceId is not null
                    && StringComparer.Ordinal.Compare(
                        previousDeviceId,
                        canonicalDeviceId) >= 0)
                {
                    throw new InvalidDataException(
                        "Trust payload peers are duplicated or not canonically ordered.");
                }

                previousDeviceId = canonicalDeviceId;
                long verifiedAtTicks = reader.ReadInt64();
                var verifiedAt = new DateTimeOffset(
                    verifiedAtTicks,
                    TimeSpan.Zero);
                ulong capabilityMask = reader.ReadUInt64();
                if ((capabilityMask & ~KnownCapabilityMask) != 0)
                {
                    throw new InvalidDataException(
                        "The trust payload contains an unknown capability bit.");
                }

                string displayName = StrictUtf8.GetString(reader.ReadBytes(
                    MaximumDisplayNameBytes,
                    "display name"));
                string normalizedName = DeviceIdentity.NormalizeDisplayName(displayName);
                if (!StringComparer.Ordinal.Equals(displayName, normalizedName))
                {
                    throw new InvalidDataException(
                        "The trust payload display name is not canonical.");
                }

                ReadOnlySpan<byte> publicKey = reader.ReadBytes(
                    MaximumPublicKeyBytes,
                    "public key");
                var identity = new PublicDeviceIdentity(
                    deviceId,
                    normalizedName,
                    publicKey);
                records.Add(new TrustRecord(
                    identity,
                    verifiedAt,
                    FromCapabilityMask(capabilityMask)));
            }

            reader.RequireEnd();
            return records;
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or CryptographicException
            or DecoderFallbackException
            or OverflowException)
        {
            throw new InvalidDataException("The trust payload is malformed.", exception);
        }
    }

    public static byte[] Encode(IEnumerable<TrustRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        TrustRecord[] materialized = records.ToArray();
        if (materialized.Any(static record => record is null))
        {
            throw new InvalidDataException("A trust record cannot be null.");
        }

        TrustRecord[] ordered = materialized
            .OrderBy(
                static record => record.PeerIdentity.DeviceId.ToString(),
                StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length > MaximumPeerCount)
        {
            throw new InvalidDataException(
                $"A trust payload cannot contain more than {MaximumPeerCount} peers.");
        }

        var encodedRecords = new EncodedTrustRecord[ordered.Length];
        int payloadLength = Magic.Length + sizeof(byte) + sizeof(ushort);
        string? previousDeviceId = null;
        for (int index = 0; index < ordered.Length; index++)
        {
            TrustRecord record = ordered[index];
            string deviceId = record.PeerIdentity.DeviceId.ToString();
            if (StringComparer.Ordinal.Equals(deviceId, previousDeviceId))
            {
                throw new InvalidDataException(
                    "A trust payload cannot contain a duplicate peer.");
            }

            previousDeviceId = deviceId;
            byte[] displayName = StrictUtf8.GetBytes(record.PeerIdentity.DisplayName);
            byte[] publicKey = record.PeerIdentity.ExportSubjectPublicKeyInfo();
            if (displayName.Length is < 1 or > MaximumDisplayNameBytes)
            {
                throw new InvalidDataException(
                    "The trust display name has an invalid encoded length.");
            }

            if (publicKey.Length is < 1 or > MaximumPublicKeyBytes)
            {
                throw new InvalidDataException(
                    "The trust public key has an invalid encoded length.");
            }

            ulong capabilityMask = ToCapabilityMask(record.GrantedCapabilities);
            encodedRecords[index] = new EncodedTrustRecord(
                record,
                displayName,
                publicKey,
                capabilityMask);
            payloadLength = checked(
                payloadLength
                + DeviceIdBytes
                + sizeof(long)
                + sizeof(ulong)
                + sizeof(ushort)
                + displayName.Length
                + sizeof(ushort)
                + publicKey.Length);
            if (payloadLength > MaximumPayloadBytes)
            {
                throw new InvalidDataException(
                    $"A trust payload cannot exceed {MaximumPayloadBytes} bytes.");
            }
        }

        byte[] payload = new byte[payloadLength];
        int offset = 0;
        Magic.CopyTo(payload, offset);
        offset += Magic.Length;
        payload[offset++] = CurrentFormatVersion;
        BinaryPrimitives.WriteUInt16BigEndian(
            payload.AsSpan(offset),
            checked((ushort)encodedRecords.Length));
        offset += sizeof(ushort);
        foreach (EncodedTrustRecord encoded in encodedRecords)
        {
            if (!encoded.Record.PeerIdentity.DeviceId.Value.TryWriteBytes(
                    payload.AsSpan(offset, DeviceIdBytes),
                    bigEndian: true,
                    out int written)
                || written != DeviceIdBytes)
            {
                throw new InvalidOperationException("The peer DeviceId could not be encoded.");
            }

            offset += DeviceIdBytes;
            BinaryPrimitives.WriteInt64BigEndian(
                payload.AsSpan(offset),
                encoded.Record.VerifiedAt.UtcTicks);
            offset += sizeof(long);
            BinaryPrimitives.WriteUInt64BigEndian(
                payload.AsSpan(offset),
                encoded.CapabilityMask);
            offset += sizeof(ulong);
            WriteBytes(payload, ref offset, encoded.DisplayName);
            WriteBytes(payload, ref offset, encoded.PublicKey);
        }

        return payload;
    }

    private static CapabilityGrant FromCapabilityMask(ulong mask)
    {
        Capability[] capabilities = Enum
            .GetValues<Capability>()
            .Where(capability => (mask & (1UL << checked((int)capability))) != 0)
            .ToArray();
        return CapabilityGrant.Of(capabilities);
    }

    private static ulong BuildKnownCapabilityMask()
    {
        ulong mask = 0;
        foreach (Capability capability in Enum.GetValues<Capability>())
        {
            int bit = checked((int)capability);
            if (bit is < 0 or >= sizeof(ulong) * 8)
            {
                throw new InvalidOperationException(
                    "Capability values must fit in the trust payload bit mask.");
            }

            mask |= 1UL << bit;
        }

        return mask;
    }

    private static ulong ToCapabilityMask(CapabilityGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ulong mask = 0;
        foreach (Capability capability in grant.Capabilities)
        {
            int bit = checked((int)capability);
            if (bit is < 0 or >= sizeof(ulong) * 8
                || !Enum.IsDefined(capability))
            {
                throw new InvalidDataException("A trust grant contains an unknown capability.");
            }

            mask |= 1UL << bit;
        }

        return mask;
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

    private sealed record EncodedTrustRecord(
        TrustRecord Record,
        byte[] DisplayName,
        byte[] PublicKey,
        ulong CapabilityMask);
}

internal ref struct TrustPayloadReader
{
    private readonly ReadOnlySpan<byte> source;
    private int offset;

    public TrustPayloadReader(ReadOnlySpan<byte> source) => this.source = source;

    public byte ReadByte()
    {
        EnsureAvailable(sizeof(byte));
        return source[offset++];
    }

    public ReadOnlySpan<byte> ReadBytes(int maximumBytes, string fieldName)
    {
        ushort count = ReadUInt16();
        if (count is 0 || count > maximumBytes)
        {
            throw new InvalidDataException(
                $"The trust payload {fieldName} length is invalid.");
        }

        return ReadRaw(count);
    }

    public long ReadInt64()
    {
        EnsureAvailable(sizeof(long));
        long value = BinaryPrimitives.ReadInt64BigEndian(source[offset..]);
        offset += sizeof(long);
        return value;
    }

    public ReadOnlySpan<byte> ReadRaw(int count)
    {
        EnsureAvailable(count);
        ReadOnlySpan<byte> value = source.Slice(offset, count);
        offset += count;
        return value;
    }

    public ushort ReadUInt16()
    {
        EnsureAvailable(sizeof(ushort));
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(source[offset..]);
        offset += sizeof(ushort);
        return value;
    }

    public ulong ReadUInt64()
    {
        EnsureAvailable(sizeof(ulong));
        ulong value = BinaryPrimitives.ReadUInt64BigEndian(source[offset..]);
        offset += sizeof(ulong);
        return value;
    }

    public void RequireEnd()
    {
        if (offset != source.Length)
        {
            throw new InvalidDataException("The trust payload contains trailing data.");
        }
    }

    public void RequireMagic(ReadOnlySpan<byte> expected)
    {
        if (!ReadRaw(expected.Length).SequenceEqual(expected))
        {
            throw new InvalidDataException("The trust payload magic is invalid.");
        }
    }

    private void EnsureAvailable(int count)
    {
        if (count < 0 || source.Length - offset < count)
        {
            throw new InvalidDataException("The trust payload ended unexpectedly.");
        }
    }
}
