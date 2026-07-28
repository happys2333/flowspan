using System.Collections.Immutable;
using Flowspan.Desktop;
using Flowspan.Diagnostics;

namespace Flowspan.Desktop.Tests;

internal sealed class FakeDesktopLocalDataService : IDesktopLocalDataService
{
    private readonly IList<string>? lifecycleOrder;

    public FakeDesktopLocalDataService(IList<string>? lifecycleOrder = null) =>
        this.lifecycleOrder = lifecycleOrder;

    public List<OperationHistoryEntry> History { get; } = [];
    public List<string> DiagnosticFiles { get; } = [];

    public int TrustExportCount { get; private set; }
    public Exception? Failure { get; set; }
    public bool IsReady { get; private set; }
    public bool IsHistoryWriteDegraded { get; set; }

    public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailure();
        lifecycleOrder?.Add("local-data-init");
        IsReady = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask<ImmutableArray<OperationHistoryEntry>> GetHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailure();
        return ValueTask.FromResult(History
            .OrderBy(static entry => entry.Sequence)
            .ToImmutableArray());
    }

    public ValueTask<bool> DeleteHistoryAsync(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailure();
        int removed = History.RemoveAll(entry => entry.EntryId == entryId);
        return ValueTask.FromResult(removed > 0);
    }

    public ValueTask<bool> ClearHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailure();
        bool changed = History.Count > 0;
        History.Clear();
        return ValueTask.FromResult(changed);
    }

    public ValueTask<DesktopRedactedExportResult> ExportTrustAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailure();
        TrustExportCount++;
        return ValueTask.FromResult(new DesktopRedactedExportResult(
            "/exports/trust.json",
            "{\"exportKind\":\"flowspan.trust-export.redacted/v1\"}"));
    }

    public ValueTask<DesktopRedactedExportResult> ExportHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailure();
        return ValueTask.FromResult(new DesktopRedactedExportResult(
            "/exports/history.json",
            "{\"exportKind\":\"flowspan.history-export.redacted/v1\"}"));
    }

    public ValueTask<string> PreviewDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailure();
        return ValueTask.FromResult(
            "{\"exportKind\":\"flowspan.diagnostics.redacted/v1\"}");
    }

    public ValueTask<DesktopRedactedExportResult> ExportDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailure();
        const string fileName = "diagnostics-test.json";
        if (!DiagnosticFiles.Contains(fileName, StringComparer.Ordinal))
        {
            DiagnosticFiles.Add(fileName);
        }

        return ValueTask.FromResult(new DesktopRedactedExportResult(
            $"/exports/{fileName}",
            "{\"exportKind\":\"flowspan.diagnostics.redacted/v1\"}"));
    }

    public IReadOnlyList<string> ListDiagnosticExports()
    {
        ThrowIfFailure();
        return DiagnosticFiles.ToArray();
    }

    public ValueTask<bool> DeleteDiagnosticExportAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailure();
        return ValueTask.FromResult(DiagnosticFiles.Remove(fileName));
    }

    public ValueTask DisposeAsync()
    {
        lifecycleOrder?.Add("local-data");
        return ValueTask.CompletedTask;
    }

    private void ThrowIfFailure()
    {
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
