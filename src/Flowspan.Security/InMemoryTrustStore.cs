using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Flowspan.Domain;

namespace Flowspan.Security;

public enum TrustRegistrationResult
{
    Added,
    AlreadyTrusted,
    IdentityChanged,
}

public sealed class InMemoryTrustStore : ITrustStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<DeviceId, TrustRecord> trustRecords = [];

    public SecretStoreProtection Protection { get; } =
        SecretStoreProtection.DegradedTestOnly;

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

    public ValueTask<TrustRegistrationResult> RegisterAsync(
        TrustRecord trustRecord,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Register(trustRecord));
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

    public ImmutableArray<TrustedPeerSnapshot> GetSnapshot()
    {
        lock (gate)
        {
            return trustRecords.Values
                .OrderBy(
                    static record => record.PeerIdentity.DeviceId.ToString(),
                    StringComparer.Ordinal)
                .Select(static record => new TrustedPeerSnapshot(
                    record.PeerIdentity.DeviceId,
                    record.PeerIdentity.DisplayName,
                    record.PeerIdentity.Fingerprint,
                    record.VerifiedAt,
                    record.GrantedCapabilities))
                .ToImmutableArray();
        }
    }

    public bool TryUpdateCapabilities(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CapabilityGrant capabilities)
        => UpdateCapabilities(
            peerDeviceId,
            expectedFingerprint,
            capabilities) == TrustMutationResult.Applied;

    private TrustMutationResult UpdateCapabilities(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CapabilityGrant capabilities)
    {
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);
        ArgumentNullException.ThrowIfNull(capabilities);
        lock (gate)
        {
            if (!trustRecords.TryGetValue(peerDeviceId, out TrustRecord? existing))
            {
                return TrustMutationResult.PeerNotFound;
            }

            if (!StringComparer.Ordinal.Equals(
                    existing.PeerIdentity.Fingerprint,
                    expectedFingerprint))
            {
                return TrustMutationResult.IdentityChanged;
            }

            trustRecords[peerDeviceId] = existing with
            {
                GrantedCapabilities = capabilities,
            };
            return TrustMutationResult.Applied;
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

    public ValueTask<TrustMutationResult> RevokeAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);
        lock (gate)
        {
            if (!trustRecords.TryGetValue(peerDeviceId, out TrustRecord? existing))
            {
                return ValueTask.FromResult(TrustMutationResult.PeerNotFound);
            }

            if (!StringComparer.Ordinal.Equals(
                    existing.PeerIdentity.Fingerprint,
                    expectedFingerprint))
            {
                return ValueTask.FromResult(TrustMutationResult.IdentityChanged);
            }

            trustRecords.Remove(peerDeviceId);
            return ValueTask.FromResult(TrustMutationResult.Applied);
        }
    }

    public ValueTask<TrustMutationResult> UpdateCapabilitiesAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CapabilityGrant capabilities,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(UpdateCapabilities(
            peerDeviceId,
            expectedFingerprint,
            capabilities));
    }
}
