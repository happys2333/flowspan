using System.Buffers.Binary;
using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class TrustStorePayloadCodecTests
{
    private static readonly DeviceId FirstPeer =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId SecondPeer =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private const string GoldenPublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE4hL+g7t+qOo9wpKA/txIxMZo" +
        "TebBMrU5dohV35yuBIj1MLNpX9FqtUqsS5o/odaOlHvyR8Nse+O7HQJZ1a5+8g==";

    [Fact]
    public void RoundTripIsCanonicalAndPreservesTrustBindings()
    {
        using DeviceIdentity first = DeviceIdentity.Generate(FirstPeer, "Alpha");
        using DeviceIdentity second = DeviceIdentity.Generate(SecondPeer, "Beta");
        TrustRecord firstRecord = new(
            first.PublicIdentity,
            new DateTimeOffset(638880768001234567, TimeSpan.Zero),
            CapabilityGrant.Of(Capability.ActivityReceive, Capability.MirrorView));
        TrustRecord secondRecord = new(
            second.PublicIdentity,
            new DateTimeOffset(638880768009876543, TimeSpan.Zero),
            CapabilityGrant.Of(Capability.MirrorDrive));

        byte[] forward = TrustStorePayloadCodec.Encode([firstRecord, secondRecord]);
        byte[] reversed = TrustStorePayloadCodec.Encode([secondRecord, firstRecord]);
        IReadOnlyList<TrustRecord> decoded = TrustStorePayloadCodec.Decode(forward);

        Assert.Equal(forward, reversed);
        Assert.Equal(2, decoded.Count);
        Assert.Equal(FirstPeer, decoded[0].PeerIdentity.DeviceId);
        Assert.Equal(firstRecord.VerifiedAt, decoded[0].VerifiedAt);
        Assert.True(decoded[0].PeerIdentity.HasSameKey(first.PublicIdentity));
        Assert.True(decoded[0].GrantedCapabilities.Allows(Capability.ActivityReceive));
        Assert.True(decoded[0].GrantedCapabilities.Allows(Capability.MirrorView));
        Assert.False(decoded[0].GrantedCapabilities.Allows(Capability.MirrorDrive));
        Assert.Equal(SecondPeer, decoded[1].PeerIdentity.DeviceId);
        Assert.Equal(secondRecord.VerifiedAt, decoded[1].VerifiedAt);
        Assert.True(decoded[1].PeerIdentity.HasSameKey(second.PublicIdentity));
    }

    [Fact]
    public void RejectsUnknownCapabilityBitsAndTrailingData()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(FirstPeer, "Alpha");
        byte[] payload = TrustStorePayloadCodec.Encode([
            new TrustRecord(
                identity.PublicIdentity,
                DateTimeOffset.UnixEpoch,
                CapabilityGrant.Of(Capability.MirrorView)),
        ]);
        byte[] unknownCapability = (byte[])payload.Clone();
        BinaryPrimitives.WriteUInt64BigEndian(
            unknownCapability.AsSpan(31, sizeof(ulong)),
            1UL << 63);
        byte[] trailing = [.. payload, 0];

        Assert.Throws<InvalidDataException>(() =>
            TrustStorePayloadCodec.Decode(unknownCapability));
        Assert.Throws<InvalidDataException>(() =>
            TrustStorePayloadCodec.Decode(trailing));
    }

    [Fact]
    public void RejectsDuplicatePeerAndResourceLimits()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(FirstPeer, "Alpha");
        TrustRecord record = new(
            identity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);

        Assert.Throws<InvalidDataException>(() =>
            TrustStorePayloadCodec.Encode([record, record]));
        Assert.Throws<InvalidDataException>(() =>
            TrustStorePayloadCodec.Decode(
                new byte[TrustStorePayloadCodec.MaximumPayloadBytes + 1]));
    }

    [Fact]
    public void GoldenFixtureFreezesVersionOneEncoding()
    {
        var identity = new PublicDeviceIdentity(
            FirstPeer,
            "Golden",
            Convert.FromBase64String(GoldenPublicKey));
        byte[] payload = TrustStorePayloadCodec.Encode([
            new TrustRecord(
                identity,
                DateTimeOffset.UnixEpoch,
                CapabilityGrant.Of(
                    Capability.ActivityReceive,
                    Capability.MirrorView)),
        ]);

        Assert.Equal(140, payload.Length);
        Assert.Equal(
            "4D4C44D1AEEFD0CA5709FE76D1B7FAA84D648FA559BC94BEA92F576F400FD65E",
            Convert.ToHexString(SHA256.HashData(payload)));
    }

    [Fact]
    public void RejectsNonCanonicalPeerOrderAndHostileHeader()
    {
        using DeviceIdentity first = DeviceIdentity.Generate(FirstPeer, "Same");
        using DeviceIdentity second = DeviceIdentity.Generate(SecondPeer, "Same");
        byte[] firstPayload = TrustStorePayloadCodec.Encode([CreateRecord(first)]);
        byte[] secondPayload = TrustStorePayloadCodec.Encode([CreateRecord(second)]);
        byte[] reversed = [
            .. firstPayload.AsSpan(0, 7),
            .. secondPayload.AsSpan(7),
            .. firstPayload.AsSpan(7),
        ];
        BinaryPrimitives.WriteUInt16BigEndian(reversed.AsSpan(5), 2);
        byte[] unknownVersion = (byte[])firstPayload.Clone();
        unknownVersion[4] = byte.MaxValue;
        byte[] wrongMagic = (byte[])firstPayload.Clone();
        wrongMagic[0] = 0;

        Assert.Throws<InvalidDataException>(() =>
            TrustStorePayloadCodec.Decode(reversed));
        Assert.Throws<InvalidDataException>(() =>
            TrustStorePayloadCodec.Decode(unknownVersion));
        Assert.Throws<InvalidDataException>(() =>
            TrustStorePayloadCodec.Decode(wrongMagic));
    }

    private static TrustRecord CreateRecord(DeviceIdentity identity) => new(
        identity.PublicIdentity,
        DateTimeOffset.UnixEpoch,
        CapabilityGrant.None);
}
