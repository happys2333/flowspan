using Flowspan.Application;

namespace Flowspan.Platform;

public sealed class AuthenticatedSceneRepositoryStateFile :
    ISceneRepositoryStatePayloadStore
{
    public const int KeyBytes = AuthenticatedReplaceStateFile.KeyBytes;
    private static readonly byte[] Magic = "FSCR"u8.ToArray();
    private readonly AuthenticatedReplaceStateFile inner;

    public AuthenticatedSceneRepositoryStateFile(
        string storagePath,
        ISceneRepositoryStateKeyStore keyStore)
    {
        inner = new AuthenticatedReplaceStateFile(
            storagePath,
            keyStore,
            Magic,
            PersistentSceneRepository.MaximumPayloadBytes,
            "Scene repository");
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.SaveAsync(payload, cancellationToken);
}
