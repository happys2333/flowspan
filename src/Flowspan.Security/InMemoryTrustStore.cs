using System.Diagnostics.CodeAnalysis;
using Flowspan.Domain;

namespace Flowspan.Security;

public enum TrustRegistrationResult
{
    Added,
    AlreadyTrusted,
    IdentityChanged,
}

public sealed class InMemoryTrustStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<DeviceId, TrustRecord> trustRecords = [];

    public TrustRegistrationResult Register(TrustRecord trustRecord)
    {
        ArgumentNullException.ThrowIfNull(trustRecord);
        lock (gate)
        {
            if (trustRecords.TryGetValue(
                    trustRecord.PeerIdentity.DeviceId,
                    out TrustRecord? existing))
            {
                return existing.PeerIdentity.HasSameKey(trustRecord.PeerIdentity)
                    ? TrustRegistrationResult.AlreadyTrusted
                    : TrustRegistrationResult.IdentityChanged;
            }

            trustRecords.Add(trustRecord.PeerIdentity.DeviceId, trustRecord);
            return TrustRegistrationResult.Added;
        }
    }

    public bool TryGet(
        DeviceId peerDeviceId,
        [NotNullWhen(true)] out TrustRecord? trustRecord)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        lock (gate)
        {
            return trustRecords.TryGetValue(peerDeviceId, out trustRecord);
        }
    }

    public bool Allows(DeviceId peerDeviceId, Capability capability)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        lock (gate)
        {
            return trustRecords.TryGetValue(peerDeviceId, out TrustRecord? record)
                && record.GrantedCapabilities.Allows(capability);
        }
    }

    public bool TryUpdateCapabilities(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CapabilityGrant capabilities)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);
        ArgumentNullException.ThrowIfNull(capabilities);
        lock (gate)
        {
            if (!trustRecords.TryGetValue(peerDeviceId, out TrustRecord? existing)
                || !StringComparer.Ordinal.Equals(
                    existing.PeerIdentity.Fingerprint,
                    expectedFingerprint))
            {
                return false;
            }

            trustRecords[peerDeviceId] = existing with
            {
                GrantedCapabilities = capabilities,
            };
            return true;
        }
    }

    public bool Revoke(DeviceId peerDeviceId)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        lock (gate)
        {
            return trustRecords.Remove(peerDeviceId);
        }
    }
}
