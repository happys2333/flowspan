using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Transport;

public sealed class SignedDiscoveryOffer
{
    public const int NonceLength = 16;
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromSeconds(5);
    private static readonly byte[] Context = Encoding.ASCII.GetBytes("FLOWSPAN-DISCOVERY-V1");
    private readonly byte[] nonce;
    private readonly byte[] signature;

    private SignedDiscoveryOffer(
        DeviceId deviceId,
        string displayName,
        string identityFingerprint,
        ushort port,
        ImmutableArray<ProtocolVersion> protocolVersions,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        byte[] nonce,
        string offerDigest,
        byte[] signature)
    {
        DeviceId = deviceId;
        DisplayName = displayName;
        IdentityFingerprint = identityFingerprint;
        Port = port;
        ProtocolVersions = protocolVersions;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        this.nonce = nonce;
        OfferDigest = offerDigest;
        this.signature = signature;
    }

    public DeviceId DeviceId { get; }

    public string DisplayName { get; }

    public string IdentityFingerprint { get; }

    public ushort Port { get; }

    public ImmutableArray<ProtocolVersion> ProtocolVersions { get; }

    public DateTimeOffset IssuedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public string OfferDigest { get; }

    public static SignedDiscoveryOffer Create(
        DeviceIdentity identity,
        int port,
        IEnumerable<ProtocolVersion> protocolVersions,
        DateTimeOffset issuedAt,
        TimeSpan lifetime,
        ReadOnlySpan<byte> nonce)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(protocolVersions);
        if (port is < 1 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                $"A discovery offer lifetime must be positive and at most {MaximumLifetime.TotalMinutes} minutes.");
        }

        if (nonce.Length != NonceLength)
        {
            throw new ArgumentException(
                $"A discovery nonce must contain exactly {NonceLength} bytes.",
                nameof(nonce));
        }

        ImmutableArray<ProtocolVersion> versions = protocolVersions
            .Distinct()
            .Order()
            .ToImmutableArray();
        if (versions.IsDefaultOrEmpty || versions.Length > 16
            || versions.Any(static version => version.Major < 1 || version.Minor < 0))
        {
            throw new ArgumentException(
                "A discovery offer must contain 1 to 16 initialized protocol versions.",
                nameof(protocolVersions));
        }

        DateTimeOffset expiresAt = issuedAt.Add(lifetime);
        byte[] nonceBytes = nonce.ToArray();
        byte[] encoded = EncodeUnsigned(
            identity.PublicIdentity.DeviceId,
            identity.PublicIdentity.DisplayName,
            identity.PublicIdentity.Fingerprint,
            checked((ushort)port),
            versions,
            issuedAt,
            expiresAt,
            nonceBytes);
        byte[] hash = SHA256.HashData(encoded);
        byte[] signature = identity.SignHash(hash);
        string digest = Convert.ToHexString(hash);
        CryptographicOperations.ZeroMemory(hash);

        return new SignedDiscoveryOffer(
            identity.DeviceId,
            identity.DisplayName,
            identity.PublicIdentity.Fingerprint,
            checked((ushort)port),
            versions,
            issuedAt,
            expiresAt,
            nonceBytes,
            digest,
            signature);
    }

    public bool Verify(PublicDeviceIdentity identity, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.DeviceId != DeviceId
            || !StringComparer.Ordinal.Equals(identity.DisplayName, DisplayName)
            || !StringComparer.Ordinal.Equals(identity.Fingerprint, IdentityFingerprint)
            || now < IssuedAt.Subtract(MaximumFutureClockSkew)
            || now >= ExpiresAt
            || ExpiresAt - IssuedAt > MaximumLifetime)
        {
            return false;
        }

        byte[] encoded = EncodeUnsigned(
            DeviceId,
            DisplayName,
            IdentityFingerprint,
            Port,
            ProtocolVersions,
            IssuedAt,
            ExpiresAt,
            nonce);
        byte[] hash = SHA256.HashData(encoded);
        bool digestMatches = StringComparer.Ordinal.Equals(
            OfferDigest,
            Convert.ToHexString(hash));
        bool valid = digestMatches && identity.VerifyHash(hash, signature);
        CryptographicOperations.ZeroMemory(hash);
        return valid;
    }

    public byte[] ExportNonce() => (byte[])nonce.Clone();

    public byte[] ExportSignature() => (byte[])signature.Clone();

    private static byte[] EncodeUnsigned(
        DeviceId deviceId,
        string displayName,
        string identityFingerprint,
        ushort port,
        ImmutableArray<ProtocolVersion> versions,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        ReadOnlySpan<byte> nonce)
    {
        var writer = new DiscoveryBuffer();
        writer.WriteRaw(Context);
        writer.WriteUtf8(deviceId.ToString());
        writer.WriteUtf8(displayName);
        writer.WriteUtf8(identityFingerprint);
        writer.WriteUInt16(port);
        writer.WriteUInt16(checked((ushort)versions.Length));
        foreach (ProtocolVersion version in versions)
        {
            writer.WriteUInt32(checked((uint)version.Major));
            writer.WriteUInt32(checked((uint)version.Minor));
        }

        writer.WriteUtf8(issuedAt.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteUtf8(expiresAt.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteBytes(nonce);
        return writer.ToArray();
    }
}

public sealed record DiscoveredPeer(
    SignedDiscoveryOffer Offer,
    PublicDeviceIdentity CandidateIdentity);

public enum DiscoveryPublishResult
{
    Added,
    Refreshed,
    Duplicate,
    Stale,
    IdentityChanged,
    Invalid,
}

public sealed class InMemoryDiscoveryDirectory
{
    private readonly Lock gate = new();
    private readonly Dictionary<DeviceId, DiscoveredPeer> peers = [];

    public DiscoveryPublishResult Publish(
        SignedDiscoveryOffer offer,
        PublicDeviceIdentity candidateIdentity,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(candidateIdentity);
        if (!offer.Verify(candidateIdentity, now))
        {
            return DiscoveryPublishResult.Invalid;
        }

        lock (gate)
        {
            RemoveExpired(now);
            if (peers.TryGetValue(offer.DeviceId, out DiscoveredPeer? existing))
            {
                if (!existing.CandidateIdentity.HasSameKey(candidateIdentity))
                {
                    return DiscoveryPublishResult.IdentityChanged;
                }

                if (StringComparer.Ordinal.Equals(
                    existing.Offer.OfferDigest,
                    offer.OfferDigest))
                {
                    return DiscoveryPublishResult.Duplicate;
                }

                if (offer.IssuedAt <= existing.Offer.IssuedAt)
                {
                    return DiscoveryPublishResult.Stale;
                }

                peers[offer.DeviceId] = new DiscoveredPeer(offer, candidateIdentity);
                return DiscoveryPublishResult.Refreshed;
            }

            peers.Add(offer.DeviceId, new DiscoveredPeer(offer, candidateIdentity));
            return DiscoveryPublishResult.Added;
        }
    }

    public IReadOnlyList<DiscoveredPeer> Snapshot(DateTimeOffset now)
    {
        lock (gate)
        {
            RemoveExpired(now);
            return peers.Values
                .OrderBy(static peer => peer.Offer.DeviceId.ToString(), StringComparer.Ordinal)
                .ToArray();
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        DeviceId[] expired = peers
            .Where(entry => now >= entry.Value.Offer.ExpiresAt)
            .Select(static entry => entry.Key)
            .ToArray();
        foreach (DeviceId deviceId in expired)
        {
            peers.Remove(deviceId);
        }
    }
}

internal sealed class DiscoveryBuffer
{
    private readonly ArrayBufferWriter<byte> buffer = new();

    public void WriteUInt16(ushort value)
    {
        Span<byte> destination = buffer.GetSpan(sizeof(ushort));
        BinaryPrimitives.WriteUInt16BigEndian(destination, value);
        buffer.Advance(sizeof(ushort));
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> destination = buffer.GetSpan(sizeof(uint));
        BinaryPrimitives.WriteUInt32BigEndian(destination, value);
        buffer.Advance(sizeof(uint));
    }

    public void WriteUtf8(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteBytes(Encoding.UTF8.GetBytes(value));
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        WriteUInt32(checked((uint)value.Length));
        WriteRaw(value);
    }

    public void WriteRaw(ReadOnlySpan<byte> value)
    {
        value.CopyTo(buffer.GetSpan(value.Length));
        buffer.Advance(value.Length);
    }

    public byte[] ToArray() => buffer.WrittenSpan.ToArray();
}
