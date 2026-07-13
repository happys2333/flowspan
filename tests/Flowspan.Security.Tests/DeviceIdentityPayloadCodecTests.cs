using System.Buffers.Binary;
using System.Text;
using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class DeviceIdentityPayloadCodecTests
{
    [Fact]
    public void EncodedIdentityRoundTripsThroughSecretPayload()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "  Café Laptop  ");

        byte[] payload = DeviceIdentityPayloadCodec.Encode(identity);
        using DeviceIdentity decoded = DeviceIdentityPayloadCodec.Decode(payload);

        Assert.Equal(identity.DeviceId, decoded.DeviceId);
        Assert.Equal("Café Laptop", decoded.DisplayName);
        Assert.Equal(
            identity.PublicIdentity.Fingerprint,
            decoded.PublicIdentity.Fingerprint);
    }

    [Fact]
    public void NonCanonicalDisplayNameEncodingIsRejected()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "É");
        byte[] canonical = DeviceIdentityPayloadCodec.Encode(identity);
        const int nameLengthOffset = 4 + 1 + 16;
        const int nameOffset = nameLengthOffset + sizeof(ushort);
        int canonicalNameLength = BinaryPrimitives.ReadUInt16BigEndian(
            canonical.AsSpan(nameLengthOffset));
        byte[] nonCanonicalName = Encoding.UTF8.GetBytes("E\u0301");
        byte[] nonCanonical = new byte[
            canonical.Length - canonicalNameLength + nonCanonicalName.Length];
        canonical.AsSpan(0, nameLengthOffset).CopyTo(nonCanonical);
        BinaryPrimitives.WriteUInt16BigEndian(
            nonCanonical.AsSpan(nameLengthOffset),
            checked((ushort)nonCanonicalName.Length));
        nonCanonicalName.CopyTo(nonCanonical, nameOffset);
        canonical.AsSpan(nameOffset + canonicalNameLength).CopyTo(
            nonCanonical.AsSpan(nameOffset + nonCanonicalName.Length));

        Assert.Throws<InvalidDataException>(() =>
            DeviceIdentityPayloadCodec.Decode(nonCanonical));
    }

    [Fact]
    public void HostilePayloadShapesAreRejected()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        byte[] encoded = DeviceIdentityPayloadCodec.Encode(identity);
        const int versionOffset = 4;
        const int deviceIdOffset = versionOffset + 1;
        const int nameLengthOffset = deviceIdOffset + 16;
        const int nameOffset = nameLengthOffset + sizeof(ushort);
        int nameLength = BinaryPrimitives.ReadUInt16BigEndian(
            encoded.AsSpan(nameLengthOffset));
        int privateKeyOffset = nameOffset + nameLength + sizeof(ushort);
        var hostilePayloads = new List<byte[]>
        {
            Array.Empty<byte>(),
            new byte[DeviceIdentityPayloadCodec.MaximumPayloadBytes + 1],
            encoded[..^1],
            encoded.Append((byte)0).ToArray(),
            Mutate(encoded, 0, 0),
            Mutate(encoded, versionOffset, 0xff),
            MutateRange(encoded, deviceIdOffset, 16, 0),
            Mutate(encoded, nameOffset, 0xff),
            Mutate(encoded, privateKeyOffset, 0),
        };

        foreach (byte[] hostilePayload in hostilePayloads)
        {
            Assert.Throws<InvalidDataException>(() =>
                DeviceIdentityPayloadCodec.Decode(hostilePayload));
        }
    }

    private static byte[] Mutate(byte[] source, int offset, byte value)
    {
        byte[] mutated = (byte[])source.Clone();
        mutated[offset] = value;
        return mutated;
    }

    private static byte[] MutateRange(
        byte[] source,
        int offset,
        int count,
        byte value)
    {
        byte[] mutated = (byte[])source.Clone();
        mutated.AsSpan(offset, count).Fill(value);
        return mutated;
    }
}
