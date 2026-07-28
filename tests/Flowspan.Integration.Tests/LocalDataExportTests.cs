using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Flowspan.Diagnostics;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Integration.Tests;

public sealed class LocalDataExportTests
{
    private static readonly DateTimeOffset ExportedAt =
        new(2026, 7, 28, 4, 5, 6, TimeSpan.Zero);

    [Fact]
    public void TrustExportContainsOnlyRedactedLifecycleFields()
    {
        TrustedPeerSnapshot peer = CreatePeer();

        byte[] payload = LocalDataExport.EncodeRedactedTrust(
            SecretStoreProtection.OperatingSystemProtected,
            [peer],
            ExportedAt);
        string json = Encoding.UTF8.GetString(payload);

        Assert.Contains("flowspan.trust-export.redacted/v1", json);
        Assert.Contains("activity.offer", json);
        Assert.DoesNotContain("TRUST-NAME-CANARY", json, StringComparison.Ordinal);
        Assert.DoesNotContain(peer.DeviceId.ToString(), json, StringComparison.Ordinal);
        Assert.DoesNotContain("TRUST-FINGERPRINT-CANARY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("publicKey", json, StringComparison.OrdinalIgnoreCase);
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        AssertProperties(
            root,
            "formatVersion", "exportKind", "exportedAt", "protection",
            "peerCount", "peers");
        Assert.Equal(1, root.GetProperty("peerCount").GetInt32());
        AssertProperties(
            root.GetProperty("peers")[0],
            "ordinal", "verifiedAt", "capabilities");
    }

    [Fact]
    public void HistoryExportOmitsEveryRawReceiptIdentifierAndDescriptor()
    {
        OperationReceipt receipt = CreateReceipt();
        var entry = new OperationHistoryEntry(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            7,
            receipt.OccurredAt,
            receipt);

        string json = Encoding.UTF8.GetString(
            LocalDataExport.EncodeRedactedHistory([entry], ExportedAt));

        Assert.Contains("flowspan.history-export.redacted/v1", json);
        Assert.Contains("peerUnavailable", json);
        Assert.DoesNotContain(entry.EntryId.ToString(), json, StringComparison.Ordinal);
        Assert.DoesNotContain(receipt.OperationId.ToString(), json, StringComparison.Ordinal);
        Assert.DoesNotContain(receipt.CorrelationId.ToString(), json, StringComparison.Ordinal);
        Assert.DoesNotContain(receipt.ActivityId.ToString(), json, StringComparison.Ordinal);
        Assert.DoesNotContain(receipt.SourceDeviceId.ToString(), json, StringComparison.Ordinal);
        Assert.DoesNotContain(receipt.TargetDeviceId.ToString(), json, StringComparison.Ordinal);
        Assert.DoesNotContain(receipt.DescriptorDigest!, json, StringComparison.Ordinal);
        Assert.DoesNotContain("HISTORY-CONTENT-CANARY", json, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        AssertProperties(
            root,
            "formatVersion", "exportKind", "exportedAt", "entryCount",
            "entries");
        AssertProperties(
            root.GetProperty("entries")[0],
            "ordinal", "recordedAt", "kind", "status", "occurredAt",
            "failureCode");
    }

    [Fact]
    public void DiagnosticsReportActualSessionStateWithoutIdentityCanaries()
    {
        TrustedPeerSnapshot peer = CreatePeer();
        OperationReceipt receipt = CreateReceipt();
        var entry = new OperationHistoryEntry(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            1,
            receipt.OccurredAt,
            receipt);
        var source = new DiagnosticBundleSource(
            "1.0.0-test",
            ".NET 10.0.0",
            "macos",
            ProtocolFeatures.ProductionSupportedVersions,
            [new ProtocolVersion(1, 4)],
            [Capability.ActivityOffer],
            SecretStoreProtection.OperatingSystemProtected,
            [peer],
            [entry]);

        byte[] payload = LocalDataExport.EncodeRedactedDiagnostics(
            source,
            ExportedAt);
        string json = Encoding.UTF8.GetString(payload);

        Assert.Contains("flowspan.diagnostics.redacted/v1", json);
        Assert.Contains("activeNegotiatedProtocols\":[\"1.4\"]", json);
        Assert.Contains("activeAuthorizedCapabilities\":[\"activity.offer\"]", json);
        Assert.Contains("peerUnavailable", json);
        Assert.DoesNotContain(peer.DeviceId.ToString(), json, StringComparison.Ordinal);
        Assert.DoesNotContain(receipt.ActivityId.ToString(), json, StringComparison.Ordinal);
        Assert.DoesNotContain(receipt.OperationId.ToString(), json, StringComparison.Ordinal);
        Assert.DoesNotContain("TRUST-NAME-CANARY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("TRUST-FINGERPRINT-CANARY", json, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        AssertProperties(
            root,
            "formatVersion", "exportKind", "exportedAt", "versions", "trust",
            "operationState");
        AssertProperties(
            root.GetProperty("versions"),
            "application", "runtime", "osFamily", "supportedProtocols",
            "activeNegotiatedProtocols");
        AssertProperties(
            root.GetProperty("trust"),
            "protection", "peerCount", "activeAuthorizedCapabilities");
        AssertProperties(
            root.GetProperty("operationState"),
            "entryCount", "states", "recentErrors");
        AssertProperties(
            root.GetProperty("operationState").GetProperty("states")[0],
            "kind", "status", "count");
        AssertProperties(
            root.GetProperty("operationState").GetProperty("recentErrors")[0],
            "occurredAt", "kind", "status", "failureCode");
    }

    [Fact]
    public void DiagnosticsDoNotInferActiveNegotiationFromTrust()
    {
        var source = new DiagnosticBundleSource(
            "1.0.0-test",
            ".NET 10.0.0",
            "linux",
            ProtocolFeatures.ProductionSupportedVersions,
            [],
            [],
            SecretStoreProtection.DegradedTestOnly,
            [CreatePeer()],
            []);

        string json = Encoding.UTF8.GetString(
            LocalDataExport.EncodeRedactedDiagnostics(source, ExportedAt));

        Assert.Contains("activeNegotiatedProtocols\":[]", json);
        Assert.Contains("activeAuthorizedCapabilities\":[]", json);
    }

    private static void AssertProperties(
        JsonElement element,
        params string[] expected) =>
        Assert.Equal(
            expected,
            element.EnumerateObject().Select(static property => property.Name));

    private static TrustedPeerSnapshot CreatePeer() => new(
        DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
        "TRUST-NAME-CANARY",
        "TRUST-FINGERPRINT-CANARY",
        new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero),
        CapabilityGrant.Of(
            Capability.ActivityOffer,
            Capability.SceneApply));

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
            new DateTimeOffset(2026, 7, 28, 4, 0, 0, TimeSpan.Zero),
            FailureCode.PeerUnavailable);
    }
}
