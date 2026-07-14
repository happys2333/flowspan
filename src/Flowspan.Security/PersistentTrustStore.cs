using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Flowspan.Domain;

namespace Flowspan.Security;

public sealed class PersistentTrustStore : ITrustStore, IDisposable
{
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly ITrustPayloadStore payloadStore;
    private readonly Lock snapshotGate = new();
    private bool disposed;
    private Dictionary<DeviceId, TrustRecord> trustRecords;

    private PersistentTrustStore(
        ITrustPayloadStore payloadStore,
        IEnumerable<TrustRecord> trustRecords)
    {
        this.payloadStore = payloadStore;
        this.trustRecords = trustRecords.ToDictionary(
            static record => record.PeerIdentity.DeviceId);
    }

    public SecretStoreProtection Protection => payloadStore.Protection;

    public static async ValueTask<PersistentTrustStore> OpenAsync(
        ITrustPayloadStore payloadStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloadStore);
        byte[]? payload = await payloadStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            return new PersistentTrustStore(payloadStore, []);
        }

        try
        {
            IReadOnlyList<TrustRecord> records = TrustStorePayloadCodec.Decode(payload);
            return new PersistentTrustStore(payloadStore, records);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public bool Allows(DeviceId peerDeviceId, Capability capability)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        lock (snapshotGate)
        {
            return trustRecords.TryGetValue(peerDeviceId, out TrustRecord? record)
                && record.GrantedCapabilities.Allows(capability);
        }
    }

    public ImmutableArray<TrustedPeerSnapshot> GetSnapshot()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (snapshotGate)
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

    public async ValueTask<TrustRegistrationResult> RegisterAsync(
        TrustRecord trustRecord,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(trustRecord);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<DeviceId, TrustRecord> candidate = Snapshot();
            DeviceId peerId = trustRecord.PeerIdentity.DeviceId;
            if (candidate.TryGetValue(peerId, out TrustRecord? existing))
            {
                return existing.PeerIdentity.HasSameKey(trustRecord.PeerIdentity)
                    ? TrustRegistrationResult.AlreadyTrusted
                    : TrustRegistrationResult.IdentityChanged;
            }

            candidate.Add(peerId, trustRecord);
            await CommitAsync(candidate, cancellationToken).ConfigureAwait(false);
            return TrustRegistrationResult.Added;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async ValueTask<TrustMutationResult> RevokeAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<DeviceId, TrustRecord> candidate = Snapshot();
            if (!candidate.TryGetValue(peerDeviceId, out TrustRecord? existing))
            {
                return TrustMutationResult.PeerNotFound;
            }

            if (!StringComparer.Ordinal.Equals(
                    existing.PeerIdentity.Fingerprint,
                    expectedFingerprint))
            {
                return TrustMutationResult.IdentityChanged;
            }

            candidate.Remove(peerDeviceId);
            await CommitAsync(candidate, cancellationToken).ConfigureAwait(false);
            return TrustMutationResult.Applied;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public bool TryGet(
        DeviceId peerDeviceId,
        [NotNullWhen(true)] out TrustRecord? trustRecord)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        lock (snapshotGate)
        {
            return trustRecords.TryGetValue(peerDeviceId, out trustRecord);
        }
    }

    public async ValueTask<TrustMutationResult> UpdateCapabilitiesAsync(
        DeviceId peerDeviceId,
        string expectedFingerprint,
        CapabilityGrant capabilities,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);
        ArgumentNullException.ThrowIfNull(capabilities);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<DeviceId, TrustRecord> candidate = Snapshot();
            if (!candidate.TryGetValue(peerDeviceId, out TrustRecord? existing))
            {
                return TrustMutationResult.PeerNotFound;
            }

            if (!StringComparer.Ordinal.Equals(
                    existing.PeerIdentity.Fingerprint,
                    expectedFingerprint))
            {
                return TrustMutationResult.IdentityChanged;
            }

            candidate[peerDeviceId] = existing with
            {
                GrantedCapabilities = capabilities,
            };
            await CommitAsync(candidate, cancellationToken).ConfigureAwait(false);
            return TrustMutationResult.Applied;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private async ValueTask CommitAsync(
        Dictionary<DeviceId, TrustRecord> candidate,
        CancellationToken cancellationToken)
    {
        byte[] payload = TrustStorePayloadCodec.Encode(candidate.Values);
        try
        {
            await payloadStore.SaveAsync(payload, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        lock (snapshotGate)
        {
            trustRecords = candidate;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        mutationGate.Dispose();
    }

    private Dictionary<DeviceId, TrustRecord> Snapshot()
    {
        lock (snapshotGate)
        {
            return new Dictionary<DeviceId, TrustRecord>(trustRecords);
        }
    }
}
