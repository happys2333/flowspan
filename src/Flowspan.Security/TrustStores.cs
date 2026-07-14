using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Flowspan.Domain;

namespace Flowspan.Security;

public sealed record TrustedPeerSnapshot(
    DeviceId DeviceId,
    string DisplayName,
    string Fingerprint,
    DateTimeOffset VerifiedAt,
    CapabilityGrant GrantedCapabilities);

public enum TrustMutationResult
{
    Applied,
    PeerNotFound,
    IdentityChanged,
}

public interface IPairingTrustAuthority
{
    public ValueTask<TrustRegistrationResult> RegisterAsync(
        TrustRecord trustRecord,
        CancellationToken cancellationToken = default);

    public bool TryGet(
        DeviceId peerDeviceId,
        [NotNullWhen(true)] out TrustRecord? trustRecord);
}

public interface ITrustStore : IPairingTrustAuthority
{
    public SecretStoreProtection Protection { get; }

    public ImmutableArray<TrustedPeerSnapshot> GetSnapshot();

    public bool Allows(DeviceId peerDeviceId, Capability capability);

    public ValueTask<TrustMutationResult> RevokeAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CancellationToken cancellationToken = default);

    public ValueTask<TrustMutationResult> UpdateCapabilitiesAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CapabilityGrant capabilities,
        CancellationToken cancellationToken = default);
}

public interface ITrustPayloadStore
{
    public SecretStoreProtection Protection { get; }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}
