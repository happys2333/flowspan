using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Application;
using Flowspan.Diagnostics;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Desktop;

internal sealed class DesktopLocalDataRuntime :
    IDesktopLocalDataService,
    IReceiptSink
{
    private const string DiagnosticsPrefix = "diagnostics-";
    private readonly string exportDirectory;
    private readonly Func<ImmutableArray<DesktopTrustedPeerConnectionSnapshot>>
        getConnections;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly IOperationHistoryStatePayloadStore payloadStore;
    private readonly TimeProvider timeProvider;
    private readonly IDesktopTrustAuthority trustAuthority;
    private bool disposed;
    private PersistentOperationHistory? history;
    private int historyWriteDegraded;
    private bool reopenRequired;

    public DesktopLocalDataRuntime(
        IOperationHistoryStatePayloadStore payloadStore,
        IDesktopTrustAuthority trustAuthority,
        Func<ImmutableArray<DesktopTrustedPeerConnectionSnapshot>>
            getConnections,
        TimeProvider? timeProvider = null,
        string? exportDirectory = null)
    {
        this.payloadStore = payloadStore
            ?? throw new ArgumentNullException(nameof(payloadStore));
        this.trustAuthority = trustAuthority
            ?? throw new ArgumentNullException(nameof(trustAuthority));
        this.getConnections = getConnections
            ?? throw new ArgumentNullException(nameof(getConnections));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.exportDirectory = exportDirectory
            ?? DesktopSceneRepositoryRuntime.GetDefaultExportDirectory();
    }

    public bool IsReady => !disposed && Volatile.Read(ref history) is not null;

    public bool IsHistoryWriteDegraded =>
        Volatile.Read(ref historyWriteDegraded) != 0;

    public async ValueTask InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ = await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref historyWriteDegraded, 0);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Write(OperationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        try
        {
            AppendReceiptAsync(receipt).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Volatile.Write(ref historyWriteDegraded, 1);
        }
    }

    public async ValueTask<ImmutableArray<OperationHistoryEntry>> GetHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PersistentOperationHistory open = await EnsureOpenAsync(
                cancellationToken).ConfigureAwait(false);
            return open.Snapshot();
        }
        finally
        {
            gate.Release();
        }
    }

    public ValueTask<bool> DeleteHistoryAsync(
        Guid entryId,
        CancellationToken cancellationToken = default) =>
        MutateHistoryAsync(
            (open, token) => open.DeleteAsync(entryId, token),
            cancellationToken);

    public ValueTask<bool> ClearHistoryAsync(
        CancellationToken cancellationToken = default) =>
        MutateHistoryAsync(
            static (open, token) => open.ClearAsync(token),
            cancellationToken);

    public async ValueTask<DesktopRedactedExportResult> ExportTrustAsync(
        CancellationToken cancellationToken = default)
    {
        DesktopTrustSnapshot trust = await trustAuthority.InitializeAsync(
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset exportedAt = timeProvider.GetUtcNow();
        byte[] content = LocalDataExport.EncodeRedactedTrust(
            trust.Protection,
            trust.TrustedPeers,
            exportedAt);
        return await WriteExportAsync(
            "trust-export",
            exportedAt,
            content,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<DesktopRedactedExportResult> ExportHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        ImmutableArray<OperationHistoryEntry> snapshot = await GetHistoryAsync(
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset exportedAt = timeProvider.GetUtcNow();
        byte[] content = LocalDataExport.EncodeRedactedHistory(
            snapshot,
            exportedAt);
        return await WriteExportAsync(
            "history-export",
            exportedAt,
            content,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<string> PreviewDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset exportedAt = timeProvider.GetUtcNow();
        DiagnosticBundleSource source = await CreateDiagnosticSourceAsync(
            cancellationToken).ConfigureAwait(false);
        byte[] content = LocalDataExport.EncodeRedactedDiagnostics(
            source,
            exportedAt);
        try
        {
            return Encoding.UTF8.GetString(content);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    public async ValueTask<DesktopRedactedExportResult> ExportDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset exportedAt = timeProvider.GetUtcNow();
        DiagnosticBundleSource source = await CreateDiagnosticSourceAsync(
            cancellationToken).ConfigureAwait(false);
        byte[] content = LocalDataExport.EncodeRedactedDiagnostics(
            source,
            exportedAt);
        return await WriteExportAsync(
            DiagnosticsPrefix.TrimEnd('-'),
            exportedAt,
            content,
            cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<string> ListDiagnosticExports()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return RedactedExportFile.ListFiles(
            exportDirectory,
            DiagnosticsPrefix);
    }

    public ValueTask<bool> DeleteDiagnosticExportAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return RedactedExportFile.DeleteAsync(
            exportDirectory,
            DiagnosticsPrefix,
            fileName,
            cancellationToken);
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
            history?.Dispose();
            history = null;
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }

    private async ValueTask AppendReceiptAsync(OperationReceipt receipt)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            PersistentOperationHistory open = history
                ?? await EnsureOpenAsync().ConfigureAwait(false);
            try
            {
                _ = await open.AppendAsync(receipt).ConfigureAwait(false);
            }
            catch (OperationHistoryPersistenceException)
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

    private async ValueTask<bool> MutateHistoryAsync(
        Func<PersistentOperationHistory, CancellationToken, ValueTask<bool>>
            mutation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PersistentOperationHistory open = history
                ?? await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                bool changed = await mutation(open, cancellationToken)
                    .ConfigureAwait(false);
                return changed;
            }
            catch (OperationHistoryPersistenceException)
            {
                reopenRequired = true;
                Volatile.Write(ref historyWriteDegraded, 1);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async ValueTask<PersistentOperationHistory> EnsureOpenAsync(
        CancellationToken cancellationToken = default)
    {
        if (reopenRequired && history is not null)
        {
            history.Dispose();
            history = null;
        }

        if (history is null)
        {
            history = await PersistentOperationHistory.OpenAsync(
                payloadStore,
                cancellationToken).ConfigureAwait(false);
            reopenRequired = false;
        }

        return history;
    }

    private async ValueTask<DiagnosticBundleSource> CreateDiagnosticSourceAsync(
        CancellationToken cancellationToken)
    {
        ImmutableArray<OperationHistoryEntry> historySnapshot =
            await GetHistoryAsync(cancellationToken).ConfigureAwait(false);
        DesktopTrustSnapshot trust = await trustAuthority.InitializeAsync(
            cancellationToken).ConfigureAwait(false);
        ImmutableArray<DesktopTrustedPeerConnectionSnapshot> connections =
            getConnections();
        HashSet<DeviceId> activePeers = connections
            .Where(static connection =>
                connection.State
                    == DesktopTrustedPeerConnectionState.AuthenticatedIdle)
            .Select(static connection => connection.DeviceId)
            .ToHashSet();
        ImmutableArray<ProtocolVersion> activeProtocols = connections
            .Where(connection => activePeers.Contains(connection.DeviceId))
            .SelectMany(static connection => connection.ActiveProtocolVersions)
            .Distinct()
            .Order()
            .ToImmutableArray();
        ImmutableArray<Capability> activeAuthorizedCapabilities =
            trust.TrustedPeers
            .Where(peer => activePeers.Contains(peer.DeviceId))
            .SelectMany(static peer =>
                peer.GrantedCapabilities.Capabilities)
            .Distinct()
            .Order()
            .ToImmutableArray();
        return new DiagnosticBundleSource(
            GetApplicationVersion(),
            RuntimeInformation.FrameworkDescription,
            GetOsFamily(),
            ProtocolFeatures.ProductionSupportedVersions,
            activeProtocols,
            activeAuthorizedCapabilities,
            trust.Protection,
            trust.TrustedPeers,
            historySnapshot);
    }

    private async ValueTask<DesktopRedactedExportResult> WriteExportAsync(
        string kind,
        DateTimeOffset exportedAt,
        byte[] content,
        CancellationToken cancellationToken)
    {
        try
        {
            string fileName = string.Create(
                CultureInfo.InvariantCulture,
                $"{kind}-{exportedAt.UtcDateTime:yyyyMMdd'T'HHmmssfff'Z'}-{Guid.NewGuid():N}.json");
            string fullPath = await RedactedExportFile.WriteAsync(
                exportDirectory,
                fileName,
                content,
                cancellationToken).ConfigureAwait(false);
            return new DesktopRedactedExportResult(
                fullPath,
                Encoding.UTF8.GetString(content));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    private static string GetApplicationVersion()
    {
        Assembly assembly = typeof(DesktopLocalDataRuntime).Assembly;
        return assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unversioned";
    }

    private static string GetOsFamily()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        return "unsupported";
    }
}
