using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;

namespace Flowspan.Desktop;

internal sealed class DesktopSceneRepositoryRuntime :
    IDesktopSceneRepositoryService
{
    private readonly string exportDirectory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ISceneRepositoryStatePayloadStore? payloadStore;
    private readonly TimeProvider timeProvider;
    private bool disposed;
    private bool initialized;
    private bool reopenRequired;
    private PersistentSceneRepository? repository;

    public DesktopSceneRepositoryRuntime(
        ISceneRepositoryStatePayloadStore? payloadStore,
        TimeProvider? timeProvider = null,
        string? exportDirectory = null)
    {
        this.payloadStore = payloadStore;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.exportDirectory = exportDirectory ?? GetDefaultExportDirectory();
    }

    public bool IsSceneRepositoryReady => !disposed && repository is not null;

    public async ValueTask InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (payloadStore is null || initialized)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized)
            {
                return;
            }

            try
            {
                repository = await PersistentSceneRepository.OpenAsync(
                    payloadStore,
                    cancellationToken).ConfigureAwait(false);
                initialized = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                repository?.Dispose();
                repository = null;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<ImmutableArray<SceneRepositoryEntry>> ListScenesAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PersistentSceneRepository open = await EnsureOpenAsync(
                cancellationToken).ConfigureAwait(false);
            return open.Snapshot();
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<SceneRepositoryEntry> SaveSceneAsync(
        ScenePlan scene,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PersistentSceneRepository open = await EnsureOpenAsync(
                cancellationToken).ConfigureAwait(false);
            try
            {
                return await open.SaveAsync(
                    scene,
                    timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (SceneRepositoryPersistenceException)
            {
                reopenRequired = true;
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<bool> DeleteSceneAsync(
        SceneId sceneId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(sceneId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PersistentSceneRepository open = await EnsureOpenAsync(
                cancellationToken).ConfigureAwait(false);
            try
            {
                return await open.DeleteAsync(sceneId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SceneRepositoryPersistenceException)
            {
                reopenRequired = true;
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<DesktopSceneExportResult?> ExportSceneAsync(
        SceneId sceneId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(sceneId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PersistentSceneRepository open = await EnsureOpenAsync(
                cancellationToken).ConfigureAwait(false);
            SceneRepositoryEntry? entry = open.Snapshot()
                .FirstOrDefault(candidate => candidate.Scene.Id == sceneId);
            if (entry is null)
            {
                return null;
            }

            DateTimeOffset exportedAt = timeProvider.GetUtcNow();
            byte[] content = SceneRepositoryExport.EncodeRedacted(
                entry,
                exportedAt);
            string fileName = string.Create(
                CultureInfo.InvariantCulture,
                $"scene-export-{entry.Scene.Id}-{exportedAt.UtcDateTime:yyyyMMdd'T'HHmmssfff'Z'}-{Guid.NewGuid():N}.json");
            string fullPath = await RedactedExportFile.WriteAsync(
                exportDirectory,
                fileName,
                content,
                cancellationToken).ConfigureAwait(false);
            return new DesktopSceneExportResult(
                fullPath,
                Encoding.UTF8.GetString(content));
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            disposed = true;
            repository?.Dispose();
            repository = null;
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }

    public static string GetDefaultExportDirectory()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "The current user has no LocalApplicationData directory.");
        }

        return Path.GetFullPath(Path.Combine(
            localApplicationData,
            "Flowspan",
            "Exports"));
    }

    private async ValueTask<PersistentSceneRepository> EnsureOpenAsync(
        CancellationToken cancellationToken)
    {
        if (payloadStore is null)
        {
            throw new PlatformNotSupportedException(
                "The Scene repository is not configured by this desktop service.");
        }

        if (reopenRequired && repository is not null)
        {
            repository.Dispose();
            repository = null;
        }

        if (repository is null)
        {
            repository = await PersistentSceneRepository.OpenAsync(
                payloadStore,
                cancellationToken).ConfigureAwait(false);
            reopenRequired = false;
            initialized = true;
        }

        return repository;
    }
}
