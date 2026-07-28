using System.Collections.Immutable;
using Flowspan.Diagnostics;

namespace Flowspan.Desktop;

public sealed record DesktopRedactedExportResult(
    string FullPath,
    string RedactedContent);

public interface IDesktopLocalDataService : IAsyncDisposable
{
    public bool IsReady { get; }

    public bool IsHistoryWriteDegraded { get; }

    public ValueTask InitializeAsync(
        CancellationToken cancellationToken = default);

    public ValueTask<ImmutableArray<OperationHistoryEntry>> GetHistoryAsync(
        CancellationToken cancellationToken = default);

    public ValueTask<bool> DeleteHistoryAsync(
        Guid entryId,
        CancellationToken cancellationToken = default);

    public ValueTask<bool> ClearHistoryAsync(
        CancellationToken cancellationToken = default);

    public ValueTask<DesktopRedactedExportResult> ExportTrustAsync(
        CancellationToken cancellationToken = default);

    public ValueTask<DesktopRedactedExportResult> ExportHistoryAsync(
        CancellationToken cancellationToken = default);

    public ValueTask<string> PreviewDiagnosticsAsync(
        CancellationToken cancellationToken = default);

    public ValueTask<DesktopRedactedExportResult> ExportDiagnosticsAsync(
        CancellationToken cancellationToken = default);

    public IReadOnlyList<string> ListDiagnosticExports();

    public ValueTask<bool> DeleteDiagnosticExportAsync(
        string fileName,
        CancellationToken cancellationToken = default);
}

internal sealed class UnavailableDesktopLocalDataService :
    IDesktopLocalDataService
{
    public static UnavailableDesktopLocalDataService Instance { get; } = new();

    public bool IsReady => false;

    public bool IsHistoryWriteDegraded => false;

    public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromException(CreateException());

    public ValueTask<ImmutableArray<OperationHistoryEntry>> GetHistoryAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<ImmutableArray<OperationHistoryEntry>>(
            CreateException());

    public ValueTask<bool> DeleteHistoryAsync(
        Guid entryId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<bool>(CreateException());

    public ValueTask<bool> ClearHistoryAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<bool>(CreateException());

    public ValueTask<DesktopRedactedExportResult> ExportTrustAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<DesktopRedactedExportResult>(CreateException());

    public ValueTask<DesktopRedactedExportResult> ExportHistoryAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<DesktopRedactedExportResult>(CreateException());

    public ValueTask<string> PreviewDiagnosticsAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<string>(CreateException());

    public ValueTask<DesktopRedactedExportResult> ExportDiagnosticsAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<DesktopRedactedExportResult>(CreateException());

    public IReadOnlyList<string> ListDiagnosticExports() => [];

    public ValueTask<bool> DeleteDiagnosticExportAsync(
        string fileName,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<bool>(CreateException());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static PlatformNotSupportedException CreateException() => new(
        "Protected local history is not configured for this desktop session.");
}
