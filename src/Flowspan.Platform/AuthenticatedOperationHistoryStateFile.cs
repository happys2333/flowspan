using Flowspan.Application;

namespace Flowspan.Platform;

public sealed class AuthenticatedOperationHistoryStateFile :
    IOperationHistoryStatePayloadStore
{
    public const int KeyBytes = AuthenticatedReplaceStateFile.KeyBytes;
    private static readonly byte[] Magic = "FSOH"u8.ToArray();
    private readonly AuthenticatedReplaceStateFile inner;

    public AuthenticatedOperationHistoryStateFile(
        string storagePath,
        IOperationHistoryStateKeyStore keyStore)
    {
        inner = new AuthenticatedReplaceStateFile(
            storagePath,
            keyStore,
            Magic,
            OperationHistoryStorageLimits.MaximumPayloadBytes,
            "operation history");
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.SaveAsync(payload, cancellationToken);
}
