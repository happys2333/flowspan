using Flowspan.Security;

namespace Flowspan.Security.Tests;

internal sealed class TestTrustPayloadStore : ITrustPayloadStore
{
    private readonly Lock gate = new();
    private byte[]? payload;

    public SecretStoreProtection Protection { get; init; } =
        SecretStoreProtection.OperatingSystemProtected;

    public bool FailNextSave { get; set; }

    public int SaveCount { get; private set; }

    public byte[]? Snapshot()
    {
        lock (gate)
        {
            return payload is null ? null : (byte[])payload.Clone();
        }
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Snapshot());
    }

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> candidate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("Injected protected payload save failure.");
            }

            payload = candidate.ToArray();
            SaveCount++;
        }

        return ValueTask.CompletedTask;
    }

    public void SetPayload(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (gate)
        {
            payload = (byte[])value.Clone();
        }
    }
}
