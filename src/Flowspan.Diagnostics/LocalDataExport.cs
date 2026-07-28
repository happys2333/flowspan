using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Diagnostics;

public sealed record DiagnosticBundleSource(
    string ApplicationVersion,
    string RuntimeVersion,
    string OsFamily,
    ImmutableArray<ProtocolVersion> SupportedProtocolVersions,
    ImmutableArray<ProtocolVersion> ActiveProtocolVersions,
    ImmutableArray<Capability> ActiveAuthorizedCapabilities,
    SecretStoreProtection TrustProtection,
    ImmutableArray<TrustedPeerSnapshot> TrustedPeers,
    ImmutableArray<OperationHistoryEntry> History);

public static class LocalDataExport
{
    private const int FormatVersion = 1;
    private const int RecentErrorLimit = 32;

    public static byte[] EncodeRedactedTrust(
        SecretStoreProtection protection,
        IEnumerable<TrustedPeerSnapshot> peers,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(peers);
        TrustedPeerSnapshot[] ordered = peers
            .OrderBy(static peer => peer.DeviceId.ToString(), StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length > TrustStorePayloadCodec.MaximumPeerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(peers));
        }

        return Encode(writer =>
        {
            WriteHeader(
                writer,
                "flowspan.trust-export.redacted/v1",
                exportedAt);
            writer.WriteString("protection", FormatProtection(protection));
            writer.WriteNumber("peerCount", ordered.Length);
            writer.WriteStartArray("peers");
            for (int index = 0; index < ordered.Length; index++)
            {
                TrustedPeerSnapshot peer = ordered[index];
                writer.WriteStartObject();
                writer.WriteNumber("ordinal", index);
                writer.WriteString(
                    "verifiedAt",
                    FormatTimestamp(peer.VerifiedAt.ToUniversalTime()));
                WriteCapabilities(
                    writer,
                    "capabilities",
                    peer.GrantedCapabilities.Capabilities);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    public static byte[] EncodeRedactedHistory(
        IEnumerable<OperationHistoryEntry> history,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(history);
        OperationHistoryEntry[] ordered = history
            .OrderBy(static entry => entry.Sequence)
            .ToArray();
        if (ordered.Length > OperationHistoryStorageLimits.MaximumEntryCount)
        {
            throw new ArgumentOutOfRangeException(nameof(history));
        }

        return Encode(writer =>
        {
            WriteHeader(
                writer,
                "flowspan.history-export.redacted/v1",
                exportedAt);
            writer.WriteNumber("entryCount", ordered.Length);
            writer.WriteStartArray("entries");
            for (int index = 0; index < ordered.Length; index++)
            {
                OperationHistoryEntry entry = ordered[index];
                writer.WriteStartObject();
                writer.WriteNumber("ordinal", index);
                writer.WriteString(
                    "recordedAt",
                    FormatTimestamp(entry.RecordedAt));
                writer.WriteString("kind", FormatEnum(entry.Receipt.Kind));
                writer.WriteString("status", FormatEnum(entry.Receipt.Status));
                writer.WriteString(
                    "occurredAt",
                    FormatTimestamp(entry.Receipt.OccurredAt.ToUniversalTime()));
                writer.WriteString(
                    "failureCode",
                    FormatEnum(entry.Receipt.FailureCode));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    public static byte[] EncodeRedactedDiagnostics(
        DiagnosticBundleSource source,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateSource(source);
        return Encode(writer =>
        {
            WriteHeader(
                writer,
                "flowspan.diagnostics.redacted/v1",
                exportedAt);
            writer.WriteStartObject("versions");
            writer.WriteString("application", source.ApplicationVersion);
            writer.WriteString("runtime", source.RuntimeVersion);
            writer.WriteString("osFamily", source.OsFamily);
            WriteProtocols(
                writer,
                "supportedProtocols",
                source.SupportedProtocolVersions);
            WriteProtocols(
                writer,
                "activeNegotiatedProtocols",
                source.ActiveProtocolVersions);
            writer.WriteEndObject();

            writer.WriteStartObject("trust");
            writer.WriteString(
                "protection",
                FormatProtection(source.TrustProtection));
            writer.WriteNumber("peerCount", source.TrustedPeers.Length);
            WriteCapabilities(
                writer,
                "activeAuthorizedCapabilities",
                source.ActiveAuthorizedCapabilities);
            writer.WriteEndObject();

            writer.WriteStartObject("operationState");
            writer.WriteNumber("entryCount", source.History.Length);
            writer.WriteStartArray("states");
            foreach (var group in source.History
                .GroupBy(static entry => (
                    entry.Receipt.Kind,
                    entry.Receipt.Status))
                .OrderBy(static group => group.Key.Kind)
                .ThenBy(static group => group.Key.Status))
            {
                writer.WriteStartObject();
                writer.WriteString("kind", FormatEnum(group.Key.Kind));
                writer.WriteString("status", FormatEnum(group.Key.Status));
                writer.WriteNumber("count", group.Count());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("recentErrors");
            foreach (OperationHistoryEntry entry in source.History
                .Where(static entry =>
                    entry.Receipt.FailureCode != FailureCode.None)
                .OrderByDescending(static entry => entry.Sequence)
                .Take(RecentErrorLimit))
            {
                writer.WriteStartObject();
                writer.WriteString(
                    "occurredAt",
                    FormatTimestamp(entry.Receipt.OccurredAt.ToUniversalTime()));
                writer.WriteString("kind", FormatEnum(entry.Receipt.Kind));
                writer.WriteString("status", FormatEnum(entry.Receipt.Status));
                writer.WriteString(
                    "failureCode",
                    FormatEnum(entry.Receipt.FailureCode));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
    }

    private static byte[] Encode(Action<Utf8JsonWriter> write)
    {
        var output = new ArrayBufferWriter<byte>(16 * 1024);
        byte[] payload;
        try
        {
            using (var writer = new Utf8JsonWriter(
                output,
                new JsonWriterOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    Indented = false,
                }))
            {
                write(writer);
            }

            payload = output.WrittenSpan.ToArray();
        }
        finally
        {
            output.Clear();
        }

        if (payload.Length > OperationHistoryStorageLimits.MaximumPayloadBytes)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new InvalidDataException(
                "A redacted local-data export exceeds its byte bound.");
        }

        return payload;
    }

    private static void WriteHeader(
        Utf8JsonWriter writer,
        string exportKind,
        DateTimeOffset exportedAt)
    {
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", FormatVersion);
        writer.WriteString("exportKind", exportKind);
        writer.WriteString(
            "exportedAt",
            FormatTimestamp(exportedAt));
    }

    private static void WriteCapabilities(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<Capability> capabilities)
    {
        writer.WriteStartArray(propertyName);
        foreach (Capability capability in capabilities.Distinct().Order())
        {
            writer.WriteStringValue(FormatCapability(capability));
        }

        writer.WriteEndArray();
    }

    private static void WriteProtocols(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<ProtocolVersion> versions)
    {
        writer.WriteStartArray(propertyName);
        foreach (ProtocolVersion version in versions.Distinct().Order())
        {
            writer.WriteStringValue(version.ToString());
        }

        writer.WriteEndArray();
    }

    private static string FormatCapability(Capability capability) =>
        capability switch
        {
            Capability.ActivityOffer => "activity.offer",
            Capability.ActivityReceive => "activity.receive",
            Capability.ActivityReplace => "activity.replace",
            Capability.ActivitySwap => "activity.swap",
            Capability.MirrorView => "mirror.view",
            Capability.MirrorDrive => "mirror.drive",
            Capability.FileReceive => "file.receive",
            Capability.SceneApply => "scene.apply",
            _ => throw new ArgumentOutOfRangeException(nameof(capability)),
        };

    private static string FormatEnum<T>(T value)
        where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static string FormatProtection(SecretStoreProtection protection) =>
        protection switch
        {
            SecretStoreProtection.OperatingSystemProtected => "os-protected",
            SecretStoreProtection.DegradedTestOnly => "degraded-test-only",
            _ => throw new ArgumentOutOfRangeException(nameof(protection)),
        };

    private static string FormatTimestamp(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A redacted export timestamp must be UTC.",
                nameof(value));
        }

        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static void ValidateSource(DiagnosticBundleSource source)
    {
        ValidateLabel(source.ApplicationVersion, nameof(source.ApplicationVersion));
        ValidateLabel(source.RuntimeVersion, nameof(source.RuntimeVersion));
        if (source.OsFamily is not ("windows" or "macos" or "linux" or "unsupported"))
        {
            throw new ArgumentException(
                "A diagnostic OS family is unsupported.",
                nameof(source));
        }

        ValidateProtocols(source.SupportedProtocolVersions, allowEmpty: false);
        ValidateProtocols(source.ActiveProtocolVersions, allowEmpty: true);
        if (source.ActiveAuthorizedCapabilities.IsDefault
            || source.ActiveAuthorizedCapabilities.Any(static capability =>
                !Enum.IsDefined(capability))
            || source.TrustedPeers.IsDefault
            || source.TrustedPeers.Length > TrustStorePayloadCodec.MaximumPeerCount
            || source.History.IsDefault
            || source.History.Length
                > OperationHistoryStorageLimits.MaximumEntryCount)
        {
            throw new ArgumentException(
                "A diagnostic bundle source exceeds its bounds.",
                nameof(source));
        }

        _ = FormatProtection(source.TrustProtection);
    }

    private static void ValidateLabel(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 200 || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A diagnostic version label must be bounded and printable.",
                parameterName);
        }
    }

    private static void ValidateProtocols(
        ImmutableArray<ProtocolVersion> versions,
        bool allowEmpty)
    {
        if (versions.IsDefault
            || (!allowEmpty && versions.IsEmpty)
            || versions.Length > 16
            || versions.Any(static version =>
                version.Major < 1 || version.Minor < 0))
        {
            throw new ArgumentException(
                "A diagnostic protocol-version list is invalid.",
                nameof(versions));
        }
    }
}
