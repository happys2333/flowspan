using System.Diagnostics.CodeAnalysis;
using Flowspan.Domain;

namespace Flowspan.Security;

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

    public bool Allows(DeviceId peerDeviceId, Capability capability);

    public ValueTask<bool> RevokeAsync(
        DeviceId peerDeviceId,
        CancellationToken cancellationToken = default);

    public ValueTask<bool> TryUpdateCapabilitiesAsync(
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
