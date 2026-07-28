using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;
using DomainOperationStatus = Flowspan.Domain.OperationStatus;

namespace Flowspan.Diagnostics;

public sealed record OperationHistoryEntry(
    Guid EntryId,
    long Sequence,
    DateTimeOffset RecordedAt,
    OperationReceipt Receipt);

public sealed class OperationHistoryPersistenceException : IOException
{
    public OperationHistoryPersistenceException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PersistentOperationHistory : IDisposable
{
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly IOperationHistoryStatePayloadStore payloadStore;
    private readonly Lock snapshotGate = new();
    private bool disposed;
    private ImmutableArray<OperationHistoryEntry> entries;
    private long nextSequence;
    private bool requiresReload;

    private PersistentOperationHistory(
        IOperationHistoryStatePayloadStore payloadStore,
        OperationHistoryState state)
    {
        this.payloadStore = payloadStore;
        entries = state.Entries;
        nextSequence = state.NextSequence;
    }

    public static async ValueTask<PersistentOperationHistory> OpenAsync(
        IOperationHistoryStatePayloadStore payloadStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloadStore);
        byte[]? payload = await payloadStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            return new PersistentOperationHistory(
                payloadStore,
                new OperationHistoryState(1, []));
        }

        try
        {
            return new PersistentOperationHistory(
                payloadStore,
                OperationHistoryStatePayloadCodec.Decode(payload));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public ImmutableArray<OperationHistoryEntry> Snapshot()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (snapshotGate)
        {
            return entries;
        }
    }

    public async ValueTask<OperationHistoryEntry> AppendAsync(
        OperationReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(receipt);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfReloadRequired();
            long sequence = nextSequence;
            long candidateNext = checked(sequence + 1);
            var entry = new OperationHistoryEntry(
                Guid.NewGuid(),
                sequence,
                receipt.OccurredAt.ToUniversalTime(),
                receipt);
            ImmutableArray<OperationHistoryEntry> current = CopyEntries();
            if (current.Length == OperationHistoryStorageLimits.MaximumEntryCount)
            {
                current = current.RemoveAt(0);
            }

            ImmutableArray<OperationHistoryEntry> candidate = current.Add(entry);
            await SaveAndPublishAsync(
                new OperationHistoryState(candidateNext, candidate),
                cancellationToken).ConfigureAwait(false);
            return entry;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async ValueTask<bool> DeleteAsync(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfEqual(entryId, Guid.Empty);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfReloadRequired();
            ImmutableArray<OperationHistoryEntry> current = CopyEntries();
            int index = FindEntryIndex(current, entryId);
            if (index < 0)
            {
                return false;
            }

            await SaveAndPublishAsync(
                new OperationHistoryState(nextSequence, current.RemoveAt(index)),
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async ValueTask<bool> ClearAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfReloadRequired();
            if (CopyEntries().IsEmpty)
            {
                return false;
            }

            await SaveAndPublishAsync(
                new OperationHistoryState(nextSequence, []),
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        mutationGate.Dispose();
    }

    private ImmutableArray<OperationHistoryEntry> CopyEntries()
    {
        lock (snapshotGate)
        {
            return entries;
        }
    }

    private static int FindEntryIndex(
        ImmutableArray<OperationHistoryEntry> current,
        Guid entryId)
    {
        for (int index = 0; index < current.Length; index++)
        {
            if (current[index].EntryId == entryId)
            {
                return index;
            }
        }

        return -1;
    }

    private async ValueTask SaveAndPublishAsync(
        OperationHistoryState candidate,
        CancellationToken cancellationToken)
    {
        byte[] payload = OperationHistoryStatePayloadCodec.Encode(candidate);
        try
        {
            await payloadStore.SaveAsync(payload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            requiresReload = true;
            if (exception is OperationCanceledException)
            {
                throw;
            }

            throw new OperationHistoryPersistenceException(
                "The durable operation history could not be saved.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        lock (snapshotGate)
        {
            entries = candidate.Entries;
            nextSequence = candidate.NextSequence;
        }
    }

    private void ThrowIfReloadRequired()
    {
        if (requiresReload)
        {
            throw new OperationHistoryPersistenceException(
                "Operation history must be reopened after an ambiguous save failure.",
                new IOException("The prior durable save outcome is unknown."));
        }
    }
}

internal sealed record OperationHistoryState(
    long NextSequence,
    ImmutableArray<OperationHistoryEntry> Entries);

internal static class OperationHistoryStatePayloadCodec
{
    private const int CurrentFormatVersion = 1;
    private const int MaximumDocumentDepth = 8;

    public static OperationHistoryState Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty
            || payload.Length > OperationHistoryStorageLimits.MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                "An operation history payload has an invalid length.");
        }

        byte[] owned = payload.ToArray();
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                owned,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = MaximumDocumentDepth,
                });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "An operation history payload must be an object.");
            }

            ValidateProperties(root, "formatVersion", "nextSequence", "entries");
            if (root.GetProperty("formatVersion").GetInt32()
                != CurrentFormatVersion)
            {
                throw new InvalidDataException(
                    "The operation history version is unsupported.");
            }

            long nextSequence = root.GetProperty("nextSequence").GetInt64();
            if (nextSequence < 1)
            {
                throw new InvalidDataException(
                    "The operation history sequence frontier is invalid.");
            }

            JsonElement entriesElement = root.GetProperty("entries");
            if (entriesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "Operation history entries must be an array.");
            }

            var entries = ImmutableArray.CreateBuilder<OperationHistoryEntry>();
            long previousSequence = 0;
            var entryIds = new HashSet<Guid>();
            foreach (JsonElement element in entriesElement.EnumerateArray())
            {
                if (entries.Count >= OperationHistoryStorageLimits.MaximumEntryCount)
                {
                    throw new InvalidDataException(
                        "Operation history exceeds its entry bound.");
                }

                OperationHistoryEntry entry = DecodeEntry(element);
                if (entry.Sequence <= previousSequence
                    || !entryIds.Add(entry.EntryId))
                {
                    throw new InvalidDataException(
                        "Operation history entries are duplicated or misordered.");
                }

                previousSequence = entry.Sequence;
                entries.Add(entry);
            }

            if (previousSequence >= nextSequence)
            {
                throw new InvalidDataException(
                    "The operation history sequence frontier is stale.");
            }

            return new OperationHistoryState(nextSequence, entries.ToImmutable());
        }
        catch (Exception exception) when (exception is
            ArgumentException or FormatException or InvalidOperationException
            or JsonException or KeyNotFoundException or OverflowException)
        {
            throw new InvalidDataException(
                "The operation history payload is malformed.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(owned);
        }
    }

    public static byte[] Encode(OperationHistoryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateState(state);
        var output = new ArrayBufferWriter<byte>(64 * 1024);
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
                writer.WriteStartObject();
                writer.WriteNumber("formatVersion", CurrentFormatVersion);
                writer.WriteNumber("nextSequence", state.NextSequence);
                writer.WriteStartArray("entries");
                foreach (OperationHistoryEntry entry in state.Entries)
                {
                    WriteEntry(writer, entry);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
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
                "An operation history payload exceeds its byte bound.");
        }

        return payload;
    }

    private static OperationHistoryEntry DecodeEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "An operation history entry must be an object.");
        }

        ValidateProperties(
            element,
            "entryId",
            "sequence",
            "recordedAt",
            "receipt");
        Guid entryId = ParseGuid(element.GetProperty("entryId").GetString());
        long sequence = element.GetProperty("sequence").GetInt64();
        if (sequence < 1)
        {
            throw new InvalidDataException(
                "An operation history sequence must be positive.");
        }

        DateTimeOffset recordedAt = ParseTimestamp(
            element.GetProperty("recordedAt").GetString());
        OperationReceipt receipt = DecodeReceipt(element.GetProperty("receipt"));
        if (recordedAt != receipt.OccurredAt.ToUniversalTime())
        {
            throw new InvalidDataException(
                "An operation history timestamp does not match its receipt.");
        }

        return new OperationHistoryEntry(entryId, sequence, recordedAt, receipt);
    }

    private static OperationReceipt DecodeReceipt(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "An operation history receipt must be an object.");
        }

        ValidateProperties(
            element,
            "operationId",
            "correlationId",
            "kind",
            "status",
            "sourceDeviceId",
            "targetDeviceId",
            "activityId",
            "activityKind",
            "descriptorDigest",
            "occurredAt",
            "failureCode");
        OperationId operationId = OperationId.From(ParseGuid(
            element.GetProperty("operationId").GetString()));
        CorrelationId correlationId = CorrelationId.From(ParseGuid(
            element.GetProperty("correlationId").GetString()));
        OperationKind kind = ParseEnum<OperationKind>(
            element.GetProperty("kind").GetString());
        DomainOperationStatus status = ParseEnum<DomainOperationStatus>(
            element.GetProperty("status").GetString());
        DeviceId source = DeviceId.From(ParseGuid(
            element.GetProperty("sourceDeviceId").GetString()));
        DeviceId target = DeviceId.From(ParseGuid(
            element.GetProperty("targetDeviceId").GetString()));
        ActivityId activity = ActivityId.From(ParseGuid(
            element.GetProperty("activityId").GetString()));
        string? activityKindValue = GetNullableString(
            element.GetProperty("activityKind"));
        ActivityKind? activityKind = activityKindValue is null
            ? null
            : ActivityKind.Parse(activityKindValue);
        if (activityKindValue is not null
            && !StringComparer.Ordinal.Equals(activityKind!.Value, activityKindValue))
        {
            throw new InvalidDataException(
                "An operation history Activity kind is not canonical.");
        }

        string? descriptorDigest = GetNullableString(
            element.GetProperty("descriptorDigest"));
        if (descriptorDigest is not null
            && (descriptorDigest.Length != 64
                || !descriptorDigest.All(character =>
                    character is >= '0' and <= '9' or >= 'A' and <= 'F')))
        {
            throw new InvalidDataException(
                "An operation history descriptor digest is not canonical.");
        }

        DateTimeOffset occurredAt = ParseTimestamp(
            element.GetProperty("occurredAt").GetString());
        FailureCode failureCode = ParseEnum<FailureCode>(
            element.GetProperty("failureCode").GetString());
        return OperationReceipt.FromRecordedResult(
            operationId,
            correlationId,
            kind,
            status,
            source,
            target,
            activity,
            activityKind,
            descriptorDigest,
            occurredAt,
            failureCode);
    }

    private static void WriteEntry(
        Utf8JsonWriter writer,
        OperationHistoryEntry entry)
    {
        writer.WriteStartObject();
        writer.WriteString("entryId", FormatGuid(entry.EntryId));
        writer.WriteNumber("sequence", entry.Sequence);
        writer.WriteString("recordedAt", FormatTimestamp(entry.RecordedAt));
        writer.WritePropertyName("receipt");
        WriteReceipt(writer, entry.Receipt);
        writer.WriteEndObject();
    }

    private static void WriteReceipt(
        Utf8JsonWriter writer,
        OperationReceipt receipt)
    {
        writer.WriteStartObject();
        writer.WriteString("operationId", receipt.OperationId.ToString());
        writer.WriteString("correlationId", receipt.CorrelationId.ToString());
        writer.WriteString("kind", FormatEnum(receipt.Kind));
        writer.WriteString("status", FormatEnum(receipt.Status));
        writer.WriteString("sourceDeviceId", receipt.SourceDeviceId.ToString());
        writer.WriteString("targetDeviceId", receipt.TargetDeviceId.ToString());
        writer.WriteString("activityId", receipt.ActivityId.ToString());
        if (receipt.ActivityKind is null)
        {
            writer.WriteNull("activityKind");
            writer.WriteNull("descriptorDigest");
        }
        else
        {
            writer.WriteString("activityKind", receipt.ActivityKind.Value);
            writer.WriteString("descriptorDigest", receipt.DescriptorDigest);
        }

        writer.WriteString("occurredAt", FormatTimestamp(
            receipt.OccurredAt.ToUniversalTime()));
        writer.WriteString("failureCode", FormatEnum(receipt.FailureCode));
        writer.WriteEndObject();
    }

    private static void ValidateState(OperationHistoryState state)
    {
        if (state.NextSequence < 1
            || state.Entries.Length
                > OperationHistoryStorageLimits.MaximumEntryCount)
        {
            throw new InvalidDataException(
                "Operation history state exceeds its bounds.");
        }

        var ids = new HashSet<Guid>();
        long previousSequence = 0;
        foreach (OperationHistoryEntry entry in state.Entries)
        {
            if (entry is null
                || entry.EntryId == Guid.Empty
                || !ids.Add(entry.EntryId)
                || entry.Sequence <= previousSequence
                || entry.RecordedAt.Offset != TimeSpan.Zero
                || entry.RecordedAt != entry.Receipt.OccurredAt.ToUniversalTime())
            {
                throw new InvalidDataException(
                    "Operation history entries are invalid or misordered.");
            }

            string? digest = entry.Receipt.DescriptorDigest;
            if (digest is not null
                && (digest.Length != 64
                    || !digest.All(character =>
                        character is >= '0' and <= '9' or >= 'A' and <= 'F')))
            {
                throw new InvalidDataException(
                    "An operation history digest is not canonical.");
            }

            previousSequence = entry.Sequence;
        }

        if (previousSequence >= state.NextSequence)
        {
            throw new InvalidDataException(
                "The operation history sequence frontier is stale.");
        }
    }

    private static string FormatEnum<T>(T value)
        where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static string FormatGuid(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new InvalidDataException(
                "An operation history ID cannot be empty.");
        }

        return value.ToString("D");
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException(
                "Operation history timestamps must be UTC.");
        }

        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string? GetNullableString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => throw new InvalidDataException(
                "An operation history nullable string is invalid."),
        };

    private static Guid ParseGuid(string? value)
    {
        if (value is null
            || !Guid.TryParseExact(value, "D", out Guid parsed)
            || parsed == Guid.Empty
            || !StringComparer.Ordinal.Equals(value, parsed.ToString("D")))
        {
            throw new InvalidDataException(
                "An operation history ID is not canonical.");
        }

        return parsed;
    }

    private static T ParseEnum<T>(string? value)
        where T : struct, Enum
    {
        if (value is null)
        {
            throw new InvalidDataException(
                "An operation history enum value is missing.");
        }

        foreach (T candidate in Enum.GetValues<T>())
        {
            if (StringComparer.Ordinal.Equals(FormatEnum(candidate), value))
            {
                return candidate;
            }
        }

        throw new InvalidDataException(
            "An operation history enum value is unsupported.");
    }

    private static DateTimeOffset ParseTimestamp(string? value)
    {
        if (value is null
            || !DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset timestamp)
            || timestamp.Offset != TimeSpan.Zero
            || !StringComparer.Ordinal.Equals(value, FormatTimestamp(timestamp)))
        {
            throw new InvalidDataException(
                "An operation history timestamp is not canonical UTC.");
        }

        return timestamp;
    }

    private static void ValidateProperties(
        JsonElement element,
        params string[] expectedNames)
    {
        int index = 0;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (index >= expectedNames.Length
                || !StringComparer.Ordinal.Equals(
                    property.Name,
                    expectedNames[index]))
            {
                throw new InvalidDataException(
                    "Operation history properties are unknown, duplicated, or misordered.");
            }

            index++;
        }

        if (index != expectedNames.Length)
        {
            throw new InvalidDataException(
                "Operation history is missing a required property.");
        }
    }
}
