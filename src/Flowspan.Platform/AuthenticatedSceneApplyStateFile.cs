using Flowspan.Application;

namespace Flowspan.Platform;

public sealed class AuthenticatedSceneApplyStateFile :
    ISceneApplyStatePayloadStore
{
    public const int KeyBytes = AuthenticatedReplaceStateFile.KeyBytes;
    private static readonly byte[] Magic = "FSAF"u8.ToArray();
    private readonly AuthenticatedReplaceStateFile inner;

    public AuthenticatedSceneApplyStateFile(
        string storagePath,
        ISceneApplyStateKeyStore keyStore)
    {
        inner = new AuthenticatedReplaceStateFile(
            storagePath,
            keyStore,
            Magic,
            PersistentSceneApplyJournal.MaximumPayloadBytes,
            "Scene apply");
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.SaveAsync(payload, cancellationToken);
}
