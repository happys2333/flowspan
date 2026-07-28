using System.Collections.Immutable;
using Flowspan.Application;
using Flowspan.Desktop;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopLocalDataRuntimeTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 28, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReceiptSinkPersistsAcrossRuntimeRestart()
    {
        var store = new MemoryHistoryStore();
        await using var authority =
            new DesktopTrustAuthority(new InMemoryTrustStore());
        OperationReceipt receipt = CreateReceipt();
        await using (var runtime = new DesktopLocalDataRuntime(
            store,
            authority,
            static () => []))
        {
            await runtime.InitializeAsync();
            runtime.Write(receipt);
            Assert.Equal(receipt, Assert.Single(
                await runtime.GetHistoryAsync()).Receipt);
        }

        await using var reopened = new DesktopLocalDataRuntime(
            store,
            authority,
            static () => []);
        await reopened.InitializeAsync();
        Assert.Equal(receipt, Assert.Single(
            await reopened.GetHistoryAsync()).Receipt);
    }

    [Fact]
    public async Task ReceiptPersistenceFailureNeverEscapesProductSink()
    {
        var store = new MemoryHistoryStore();
        await using var authority =
            new DesktopTrustAuthority(new InMemoryTrustStore());
        await using var runtime = new DesktopLocalDataRuntime(
            store,
            authority,
            static () => []);
        await runtime.InitializeAsync();
        store.FailNextSave = true;
        OperationReceipt receipt = CreateReceipt();

        Exception? failure = Record.Exception(() => runtime.Write(receipt));
        runtime.Write(receipt);

        Assert.Null(failure);
        Assert.True(runtime.IsHistoryWriteDegraded);
        Assert.Empty(await runtime.GetHistoryAsync());
        Assert.True(runtime.IsHistoryWriteDegraded);
        runtime.Write(receipt);
        Assert.Equal(
            receipt,
            Assert.Single(await runtime.GetHistoryAsync()).Receipt);
        Assert.True(runtime.IsHistoryWriteDegraded);
    }

    [Fact]
    public async Task ExportsAreRedactedAndDiagnosticFilesCanBeDeleted()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-local-data-runtime-{Guid.NewGuid():N}");
        var historyStore = new MemoryHistoryStore();
        var trustStore = new InMemoryTrustStore();
        TrustRecord trust = CreateTrustRecord();
        trustStore.Register(trust);
        await using var authority = new DesktopTrustAuthority(trustStore);
        ImmutableArray<DesktopTrustedPeerConnectionSnapshot> connections =
        [
            new DesktopTrustedPeerConnectionSnapshot(
                trust.PeerIdentity.DeviceId,
                trust.PeerIdentity.DisplayName,
                trust.PeerIdentity.Fingerprint,
                DesktopTrustedPeerConnectionState.AuthenticatedIdle,
                null,
                null,
                null)
            {
                ActiveProtocolVersions = [new ProtocolVersion(1, 4)],
            },
        ];
        try
        {
            await using var runtime = new DesktopLocalDataRuntime(
                historyStore,
                authority,
                () => connections,
                new FixedTimeProvider(FixedNow),
                directory);
            await runtime.InitializeAsync();
            OperationReceipt receipt = CreateReceipt();
            runtime.Write(receipt);

            DesktopRedactedExportResult trustExport =
                await runtime.ExportTrustAsync();
            DesktopRedactedExportResult historyExport =
                await runtime.ExportHistoryAsync();
            DesktopRedactedExportResult diagnostics =
                await runtime.ExportDiagnosticsAsync();

            Assert.DoesNotContain(
                "TRUST-NAME-CANARY",
                trustExport.RedactedContent,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                trust.PeerIdentity.DeviceId.ToString(),
                diagnostics.RedactedContent,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                receipt.ActivityId.ToString(),
                historyExport.RedactedContent,
                StringComparison.Ordinal);
            Assert.Contains(
                "activeNegotiatedProtocols\":[\"1.4\"]",
                diagnostics.RedactedContent,
                StringComparison.Ordinal);
            string diagnosticFile = Assert.Single(
                runtime.ListDiagnosticExports());
            Assert.True(await runtime.DeleteDiagnosticExportAsync(
                diagnosticFile));
            Assert.Empty(runtime.ListDiagnosticExports());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static TrustRecord CreateTrustRecord()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "TRUST-NAME-CANARY");
        return new TrustRecord(
            peer.PublicIdentity,
            FixedNow.AddDays(-1),
            CapabilityGrant.Of(
                Capability.ActivityOffer,
                Capability.SceneApply));
    }

    private static OperationReceipt CreateReceipt()
    {
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "HISTORY-TITLE-CANARY",
            "{\"text\":\"HISTORY-CONTENT-CANARY\"}",
            ActivitySensitivity.Sensitive);
        return OperationReceipt.Failed(
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"),
            OperationKind.Move,
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            descriptor,
            FixedNow,
            FailureCode.PeerUnavailable);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MemoryHistoryStore :
        IOperationHistoryStatePayloadStore
    {
        private byte[]? payload;

        public bool FailNextSave { get; set; }

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(payload?.ToArray());
        }

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> candidate,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailNextSave)
            {
                FailNextSave = false;
                return ValueTask.FromException(
                    new IOException("history-save-canary"));
            }

            payload = candidate.ToArray();
            return ValueTask.CompletedTask;
        }
    }
}
