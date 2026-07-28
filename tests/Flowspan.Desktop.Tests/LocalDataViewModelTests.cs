using Flowspan.Desktop;
using Flowspan.Diagnostics;
using Flowspan.Domain;

namespace Flowspan.Desktop.Tests;

public sealed class LocalDataViewModelTests
{
    [Fact]
    public async Task InitializeInspectDeleteAndClearUseTwoStepLifecycle()
    {
        var service = new FakeDesktopLocalDataService();
        service.History.Add(CreateEntry(1));
        service.History.Add(CreateEntry(2));
        service.IsHistoryWriteDegraded = true;
        await using var viewModel = new LocalDataViewModel(service);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsHistoryAvailable);
        Assert.Equal(2, viewModel.History.Count);
        Assert.Contains("2 RECEIPTS", viewModel.HistoryStatus);
        Assert.Contains("could not be completed", viewModel.HistoryDescription);
        viewModel.SelectedHistory = viewModel.History[0];
        viewModel.BeginDeleteHistory();
        Assert.True(viewModel.IsDeleteHistoryVisible);
        Assert.DoesNotContain(
            viewModel.SelectedHistory.Entry.Receipt.ActivityId.ToString(),
            viewModel.HistoryDeleteConfirmation,
            StringComparison.Ordinal);
        await viewModel.ConfirmDeleteHistoryAsync();
        Assert.Single(viewModel.History);

        viewModel.BeginClearHistory();
        Assert.True(viewModel.IsClearHistoryVisible);
        await viewModel.ConfirmClearHistoryAsync();
        Assert.Empty(viewModel.History);
    }

    [Fact]
    public async Task ExportAndDiagnosticDeleteSurfaceOnlyRedactedContent()
    {
        var service = new FakeDesktopLocalDataService();
        service.History.Add(CreateEntry(1));
        await using var viewModel = new LocalDataViewModel(service);
        await viewModel.InitializeAsync();

        await viewModel.ExportHistoryAsync();
        await viewModel.ExportDiagnosticsAsync();

        Assert.Contains("redacted", viewModel.HistoryExportPreview);
        Assert.Contains("redacted", viewModel.DiagnosticsPreview);
        DiagnosticExportItemViewModel diagnostic =
            Assert.Single(viewModel.DiagnosticExports);
        viewModel.SelectedDiagnosticExport = diagnostic;
        viewModel.BeginDeleteDiagnostic();
        Assert.True(viewModel.IsDeleteDiagnosticVisible);
        Assert.DoesNotContain(
            diagnostic.FileName,
            viewModel.DiagnosticDeleteConfirmation,
            StringComparison.Ordinal);
        await viewModel.ConfirmDeleteDiagnosticAsync();
        Assert.Empty(viewModel.DiagnosticExports);
    }

    [Fact]
    public async Task FailuresUseFixedNonEchoingText()
    {
        const string canary = "LOCAL-DATA-EXCEPTION-CANARY";
        var service = new FakeDesktopLocalDataService();
        service.History.Add(CreateEntry(1));
        service.DiagnosticFiles.Add("diagnostics-test.json");
        await using var viewModel = new LocalDataViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.SelectedHistory = Assert.Single(viewModel.History);
        viewModel.SelectedDiagnosticExport =
            Assert.Single(viewModel.DiagnosticExports);
        service.Failure = new IOException(canary);

        viewModel.BeginDeleteHistory();
        await viewModel.ConfirmDeleteHistoryAsync();
        Assert.Equal("HISTORY DELETE FAILED", viewModel.HistoryStatus);
        viewModel.BeginClearHistory();
        await viewModel.ConfirmClearHistoryAsync();
        Assert.Equal("HISTORY CLEAR FAILED", viewModel.HistoryStatus);
        await viewModel.ExportHistoryAsync();
        Assert.Equal("HISTORY EXPORT FAILED", viewModel.HistoryStatus);
        await viewModel.ExportDiagnosticsAsync();
        Assert.Equal("DIAGNOSTIC EXPORT FAILED", viewModel.DiagnosticsStatus);
        viewModel.BeginDeleteDiagnostic();
        await viewModel.ConfirmDeleteDiagnosticAsync();
        Assert.Equal("DIAGNOSTIC DELETE FAILED", viewModel.DiagnosticsStatus);
        Assert.DoesNotContain(
            canary,
            string.Join('\n',
                viewModel.HistoryDescription,
                viewModel.HistoryStatus,
                viewModel.DiagnosticsStatus),
            StringComparison.Ordinal);
    }

    private static OperationHistoryEntry CreateEntry(long sequence)
    {
        DateTimeOffset occurredAt = new(
            2026,
            7,
            28,
            8,
            checked((int)sequence),
            0,
            TimeSpan.Zero);
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.From(Guid.NewGuid()),
            ActivityKind.Parse("workspace.note/v1"),
            DeviceId.From(Guid.NewGuid()),
            "VIEW-MODEL-TITLE-CANARY",
            "{\"text\":\"VIEW-MODEL-CONTENT-CANARY\"}",
            ActivitySensitivity.Sensitive);
        OperationReceipt receipt = OperationReceipt.Failed(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()),
            OperationKind.Handoff,
            DeviceId.From(Guid.NewGuid()),
            DeviceId.From(Guid.NewGuid()),
            descriptor,
            occurredAt,
            FailureCode.PeerUnavailable);
        return new OperationHistoryEntry(
            Guid.NewGuid(),
            sequence,
            occurredAt,
            receipt);
    }
}
