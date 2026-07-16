using Flowspan.Application;

namespace Flowspan.Platform;

public sealed class AuthenticatedSwapEndpointStateFile :
    ISwapEndpointStatePayloadStore
{
    public const int KeyBytes = AuthenticatedReplaceStateFile.KeyBytes;
    private static readonly byte[] Magic = "FSEF"u8.ToArray();
    private readonly AuthenticatedReplaceStateFile inner;

    public AuthenticatedSwapEndpointStateFile(
        string storagePath,
        ISwapEndpointStateKeyStore keyStore)
    {
        inner = new AuthenticatedReplaceStateFile(
            storagePath,
            keyStore,
            Magic,
            PersistentSwapEndpointJournal.MaximumPayloadBytes,
            "Swap endpoint");
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.SaveAsync(payload, cancellationToken);
}
