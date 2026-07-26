using Flowspan.Application;

namespace Flowspan.Platform;

public sealed class AuthenticatedSceneRemoteChildStateFile :
    ISceneRemoteChildStatePayloadStore
{
    public const int KeyBytes = AuthenticatedReplaceStateFile.KeyBytes;
    private static readonly byte[] Magic = "FSRC"u8.ToArray();
    private readonly AuthenticatedReplaceStateFile inner;

    public AuthenticatedSceneRemoteChildStateFile(
        string storagePath,
        ISceneRemoteChildStateKeyStore keyStore)
    {
        inner = new AuthenticatedReplaceStateFile(
            storagePath,
            keyStore,
            Magic,
            PersistentSceneRemoteChildJournal.MaximumPayloadBytes,
            "Scene remote child");
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.SaveAsync(payload, cancellationToken);
}
