using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Transport;

public static class DnsSdDiscoveryOfferTxtCodec
{
    public const int MaximumPayloadBytes = 768;
    public const int MaximumTxtStringBytes = 255;
    private const int ChunkCharacters = 240;
    private const int MaximumChunkCount = 5;
    private const ushort WireVersion = 1;
    private static readonly byte[] Magic = "FSDO"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static IReadOnlyDictionary<string, string> Encode(
        SignedDiscoveryOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        byte[] payload = EncodePayload(offer);
        if (payload.Length > MaximumPayloadBytes)
        {
            throw new InvalidOperationException(
                $"The signed discovery offer exceeds the {MaximumPayloadBytes}-byte TXT payload budget.");
        }

        string encoded = Convert.ToBase64String(payload);
        int chunkCount = checked((encoded.Length + ChunkCharacters - 1) / ChunkCharacters);
        if (chunkCount is < 1 or > MaximumChunkCount)
        {
            throw new InvalidOperationException(
                "The signed discovery offer requires too many DNS-SD TXT chunks.");
        }

        var properties = ImmutableDictionary.CreateBuilder<string, string>(
            StringComparer.OrdinalIgnoreCase);
        properties.Add("txtvers", WireVersion.ToString(CultureInfo.InvariantCulture));
        properties.Add("fsc", chunkCount.ToString(CultureInfo.InvariantCulture));
        for (int index = 0; index < chunkCount; index++)
        {
            int offset = index * ChunkCharacters;
            string value = encoded.Substring(
                offset,
                Math.Min(ChunkCharacters, encoded.Length - offset));
            string key = $"fs{index.ToString(CultureInfo.InvariantCulture)}";
            if (StrictUtf8.GetByteCount($"{key}={value}") > MaximumTxtStringBytes)
            {
                throw new InvalidOperationException(
                    "A discovery TXT chunk exceeds the DNS character-string limit.");
            }

            properties.Add(key, value);
        }

        return properties.ToImmutable();
    }

    public static bool TryDecode(
        IReadOnlyDictionary<string, string> textRecords,
        [NotNullWhen(true)] out SignedDiscoveryOffer? offer)
    {
        ArgumentNullException.ThrowIfNull(textRecords);
        offer = null;
        try
        {
            if (textRecords.Count is < 3 or > MaximumChunkCount + 2)
            {
                return false;
            }

            var canonical = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach ((string key, string value) in textRecords)
            {
                if (string.IsNullOrEmpty(key)
                    || value is null
                    || StrictUtf8.GetByteCount($"{key}={value}") > MaximumTxtStringBytes
                    || !canonical.TryAdd(key, value))
                {
                    return false;
                }
            }

            if (!canonical.TryGetValue("txtvers", out string? version)
                || !StringComparer.Ordinal.Equals(version, "1")
                || !canonical.TryGetValue("fsc", out string? chunkCountText)
                || !int.TryParse(
                    chunkCountText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int chunkCount)
                || chunkCount is < 1 or > MaximumChunkCount
                || !StringComparer.Ordinal.Equals(
                    chunkCountText,
                    chunkCount.ToString(CultureInfo.InvariantCulture))
                || canonical.Count != chunkCount + 2)
            {
                return false;
            }

            var encoded = new StringBuilder(chunkCount * ChunkCharacters);
            for (int index = 0; index < chunkCount; index++)
            {
                string key = $"fs{index.ToString(CultureInfo.InvariantCulture)}";
                if (!canonical.TryGetValue(key, out string? chunk)
                    || string.IsNullOrEmpty(chunk)
                    || chunk.Length > ChunkCharacters
                    || (index < chunkCount - 1 && chunk.Length != ChunkCharacters))
                {
                    return false;
                }

                encoded.Append(chunk);
            }

            int maximumEncodedLength = ((MaximumPayloadBytes + 2) / 3) * 4;
            if (encoded.Length > maximumEncodedLength)
            {
                return false;
            }

            string encodedText = encoded.ToString();
            byte[] payload = Convert.FromBase64String(encodedText);
            if (payload.Length > MaximumPayloadBytes
                || !StringComparer.Ordinal.Equals(
                    encodedText,
                    Convert.ToBase64String(payload)))
            {
                return false;
            }

            offer = DecodePayload(payload);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or FormatException
                or OverflowException
                or DecoderFallbackException)
        {
            offer = null;
            return false;
        }
    }

    private static byte[] EncodePayload(SignedDiscoveryOffer offer)
    {
        var writer = new DiscoveryBuffer();
        writer.WriteRaw(Magic);
        writer.WriteUInt16(WireVersion);
        writer.WriteUtf8(offer.DeviceId.ToString());
        writer.WriteUtf8(offer.DisplayName);
        writer.WriteUtf8(offer.IdentityFingerprint);
        writer.WriteUInt16(offer.Port);
        writer.WriteUInt16(checked((ushort)offer.ProtocolVersions.Length));
        foreach (ProtocolVersion version in offer.ProtocolVersions)
        {
            writer.WriteUInt32(checked((uint)version.Major));
            writer.WriteUInt32(checked((uint)version.Minor));
        }

        writer.WriteUtf8(offer.IssuedAt.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteUtf8(offer.ExpiresAt.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteBytes(offer.ExportNonce());
        writer.WriteBytes(offer.ExportSignature());
        return writer.ToArray();
    }

    private static SignedDiscoveryOffer DecodePayload(ReadOnlySpan<byte> payload)
    {
        var reader = new DiscoveryPayloadReader(payload);
        reader.Expect(Magic);
        if (reader.ReadUInt16() != WireVersion)
        {
            throw new FormatException("Unsupported discovery TXT payload version.");
        }

        string encodedDeviceId = reader.ReadUtf8(64);
        DeviceId deviceId = DeviceId.Parse(encodedDeviceId);
        if (!StringComparer.Ordinal.Equals(encodedDeviceId, deviceId.ToString()))
        {
            throw new FormatException("The discovery device ID is not canonical.");
        }
        string displayName = reader.ReadUtf8(320);
        string fingerprint = reader.ReadUtf8(64);
        ushort port = reader.ReadUInt16();
        ushort versionCount = reader.ReadUInt16();
        if (versionCount is < 1 or > 16)
        {
            throw new FormatException("Invalid discovery protocol-version count.");
        }

        var versions = ImmutableArray.CreateBuilder<ProtocolVersion>(versionCount);
        for (int index = 0; index < versionCount; index++)
        {
            uint major = reader.ReadUInt32();
            uint minor = reader.ReadUInt32();
            if (major > int.MaxValue || minor > int.MaxValue)
            {
                throw new FormatException("A discovery protocol version is out of range.");
            }

            versions.Add(new ProtocolVersion(checked((int)major), checked((int)minor)));
        }

        DateTimeOffset issuedAt = ReadTimestamp(reader.ReadUtf8(64));
        DateTimeOffset expiresAt = ReadTimestamp(reader.ReadUtf8(64));
        byte[] nonce = reader.ReadBytes(SignedDiscoveryOffer.NonceLength);
        byte[] signature = reader.ReadBytes(64);
        reader.EnsureComplete();
        return SignedDiscoveryOffer.ImportUntrusted(
            deviceId,
            displayName,
            fingerprint,
            port,
            versions,
            issuedAt,
            expiresAt,
            nonce,
            signature);
    }

    private static DateTimeOffset ReadTimestamp(string value)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset timestamp)
            || !StringComparer.Ordinal.Equals(
                value,
                timestamp.ToString("O", CultureInfo.InvariantCulture)))
        {
            throw new FormatException("A discovery timestamp is not canonical.");
        }

        return timestamp;
    }

    private ref struct DiscoveryPayloadReader
    {
        private readonly ReadOnlySpan<byte> payload;
        private int offset;

        public DiscoveryPayloadReader(ReadOnlySpan<byte> payload)
        {
            this.payload = payload;
        }

        public void EnsureComplete()
        {
            if (offset != payload.Length)
            {
                throw new FormatException("The discovery payload has trailing bytes.");
            }
        }

        public void Expect(ReadOnlySpan<byte> expected)
        {
            if (!ReadRaw(expected.Length).SequenceEqual(expected))
            {
                throw new FormatException("The discovery payload magic is invalid.");
            }
        }

        public byte[] ReadBytes(int maximumLength)
        {
            uint length = ReadUInt32();
            if (length > maximumLength)
            {
                throw new FormatException("A discovery field exceeds its limit.");
            }

            return ReadRaw(checked((int)length)).ToArray();
        }

        public ushort ReadUInt16()
        {
            ReadOnlySpan<byte> value = ReadRaw(sizeof(ushort));
            return BinaryPrimitives.ReadUInt16BigEndian(value);
        }

        public uint ReadUInt32()
        {
            ReadOnlySpan<byte> value = ReadRaw(sizeof(uint));
            return BinaryPrimitives.ReadUInt32BigEndian(value);
        }

        public string ReadUtf8(int maximumBytes) =>
            StrictUtf8.GetString(ReadBytes(maximumBytes));

        private ReadOnlySpan<byte> ReadRaw(int length)
        {
            if (length < 0 || offset > payload.Length - length)
            {
                throw new FormatException("The discovery payload is truncated.");
            }

            ReadOnlySpan<byte> value = payload.Slice(offset, length);
            offset += length;
            return value;
        }
    }
}
