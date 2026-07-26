using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flowspan.Domain;

namespace Flowspan.Application;

public interface ISceneRemoteChildStatePayloadStore
{
    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}

public sealed class SceneRemoteChildPersistenceException : IOException
{
    public SceneRemoteChildPersistenceException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PersistentSceneRemoteChildJournal :
    ISceneRemoteChildJournal,
    IDisposable
{
    public const int MaximumEntryCount = 1024;
    public const int MaximumPayloadBytes = 4 * 1024 * 1024;

    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly ISceneRemoteChildStatePayloadStore payloadStore;
    private readonly Lock snapshotGate = new();
    private bool disposed;
    private Dictionary<OperationId, SceneRemoteChildJournalEntry> entries;
    private bool requiresReload;

    private PersistentSceneRemoteChildJournal(
        ISceneRemoteChildStatePayloadStore payloadStore,
        IEnumerable<SceneRemoteChildJournalEntry> entries)
    {
        this.payloadStore = payloadStore;
        this.entries = entries.ToDictionary(static entry => entry.OperationId);
    }

    public int EntryCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            lock (snapshotGate)
            {
                return entries.Count;
            }
        }
    }

    public static async ValueTask<PersistentSceneRemoteChildJournal> OpenAsync(
        ISceneRemoteChildStatePayloadStore payloadStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloadStore);
        byte[]? payload = await payloadStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            return new PersistentSceneRemoteChildJournal(payloadStore, []);
        }

        try
        {
            return new PersistentSceneRemoteChildJournal(
                payloadStore,
                SceneRemoteChildStatePayloadCodec.Decode(payload));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public async ValueTask<SceneRemoteChildJournalStart> LoadOrStartAsync(
        SceneRemoteChildInstruction instruction,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(instruction);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReloadIfRequiredAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<OperationId, SceneRemoteChildJournalEntry> candidate = Snapshot();
            if (candidate.TryGetValue(
                    instruction.Item.ChildOperationId,
                    out SceneRemoteChildJournalEntry? existing))
            {
                return new SceneRemoteChildJournalStart(existing, WasCreated: false);
            }

            if (candidate.Count == MaximumEntryCount)
            {
                throw new InvalidOperationException(
                    "The remote Scene child journal reached its entry bound.");
            }

            SceneRemoteChildJournalEntry created =
                SceneRemoteChildJournalEntry.Started(instruction, startedAt);
            candidate.Add(created.OperationId, created);
            await SaveCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
            ReplaceSnapshot(candidate);
            return new SceneRemoteChildJournalStart(created, WasCreated: true);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async ValueTask<SceneRemoteChildJournalEntry> RecordTerminalAsync(
        SceneRemoteChildInstruction instruction,
        SceneActivityOperationResult result,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(result);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReloadIfRequiredAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<OperationId, SceneRemoteChildJournalEntry> candidate = Snapshot();
            if (!candidate.TryGetValue(
                    instruction.Item.ChildOperationId,
                    out SceneRemoteChildJournalEntry? existing)
                || existing.BindingDigest != instruction.BindingDigest)
            {
                throw new InvalidOperationException(
                    "The remote Scene child journal binding changed before completion.");
            }

            if (existing.Status == SceneRemoteChildJournalStatus.Terminal)
            {
                if (existing.Result != result)
                {
                    throw new InvalidOperationException(
                        "The remote Scene child terminal result changed.");
                }

                return existing;
            }

            SceneRemoteChildJournalEntry terminal = existing.Complete(result);
            candidate[terminal.OperationId] = terminal;
            await SaveCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
            ReplaceSnapshot(candidate);
            return terminal;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public void Dispose()
    {
        disposed = true;
        mutationGate.Dispose();
    }

    private async ValueTask ReloadIfRequiredAsync(
        CancellationToken cancellationToken)
    {
        if (!requiresReload)
        {
            return;
        }

        byte[]? payload = await payloadStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ReplaceSnapshot(payload is null
                ? []
                : SceneRemoteChildStatePayloadCodec.Decode(payload).ToDictionary(
                    static entry => entry.OperationId));
            requiresReload = false;
        }
        finally
        {
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    private async ValueTask SaveCandidateAsync(
        Dictionary<OperationId, SceneRemoteChildJournalEntry> candidate,
        CancellationToken cancellationToken)
    {
        byte[] payload = SceneRemoteChildStatePayloadCodec.Encode(candidate.Values);
        try
        {
            await payloadStore.SaveAsync(payload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            requiresReload = true;
            throw new SceneRemoteChildPersistenceException(
                "The remote Scene child journal save outcome is unknown.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private Dictionary<OperationId, SceneRemoteChildJournalEntry> Snapshot()
    {
        lock (snapshotGate)
        {
            return entries.ToDictionary(static pair => pair.Key, static pair => pair.Value);
        }
    }

    private void ReplaceSnapshot(
        Dictionary<OperationId, SceneRemoteChildJournalEntry> replacement)
    {
        lock (snapshotGate)
        {
            entries = replacement;
        }
    }
}

internal static class SceneRemoteChildStatePayloadCodec
{
    private const int FormatVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 16,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static IReadOnlyList<SceneRemoteChildJournalEntry> Decode(
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length is < 1
            or > PersistentSceneRemoteChildJournal.MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                "The remote Scene child journal payload size is invalid.");
        }

        ValidateNoDuplicateProperties(payload);
        StateDto state;
        try
        {
            state = JsonSerializer.Deserialize<StateDto>(payload, SerializerOptions)
                ?? throw new InvalidDataException(
                    "The remote Scene child journal payload is null.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidDataException(
                "The remote Scene child journal payload is malformed.",
                exception);
        }

        if (state.FormatVersion != FormatVersion
            || state.Entries is null
            || state.Entries.Length > PersistentSceneRemoteChildJournal.MaximumEntryCount)
        {
            throw new InvalidDataException(
                "The remote Scene child journal payload header is invalid.");
        }

        var operationIds = new HashSet<OperationId>();
        var entries = new List<SceneRemoteChildJournalEntry>(state.Entries.Length);
        try
        {
            foreach (EntryDto encoded in state.Entries)
            {
                SceneRemoteChildJournalEntry entry = DecodeEntry(encoded);
                if (!operationIds.Add(entry.OperationId))
                {
                    throw new InvalidDataException(
                        "The remote Scene child journal contains a duplicate Operation ID.");
                }

                entries.Add(entry);
            }
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The remote Scene child journal contains an invalid value.",
                exception);
        }

        byte[] canonical = Encode(entries);
        try
        {
            if (!payload.SequenceEqual(canonical))
            {
                throw new InvalidDataException(
                    "The remote Scene child journal payload is not canonical.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonical);
        }

        return entries;
    }

    public static byte[] Encode(
        IEnumerable<SceneRemoteChildJournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        EntryDto[] encoded = entries
            .OrderBy(static entry => entry.OperationId.Value)
            .Select(EncodeEntry)
            .ToArray();
        if (encoded.Length > PersistentSceneRemoteChildJournal.MaximumEntryCount)
        {
            throw new ArgumentOutOfRangeException(nameof(entries));
        }

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new StateDto(FormatVersion, encoded),
            SerializerOptions);
        if (payload.Length > PersistentSceneRemoteChildJournal.MaximumPayloadBytes)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new InvalidOperationException(
                "The remote Scene child journal payload exceeds its byte bound.");
        }

        return payload;
    }

    private static SceneRemoteChildJournalEntry DecodeEntry(EntryDto encoded)
    {
        OperationId operationId = OperationId.Parse(encoded.OperationId);
        CorrelationId correlationId = CorrelationId.Parse(encoded.CorrelationId);
        SceneRemoteChildJournalStatus status = encoded.Status switch
        {
            "started" => SceneRemoteChildJournalStatus.Started,
            "terminal" => SceneRemoteChildJournalStatus.Terminal,
            _ => throw new InvalidDataException(
                "The remote Scene child journal status is unsupported."),
        };
        SceneActivityOperationResult? result = encoded.Result is null
            ? null
            : DecodeResult(encoded.Result);
        return SceneRemoteChildJournalEntry.Restore(
            operationId,
            correlationId,
            encoded.BindingDigest,
            status,
            RequireUtc(encoded.StartedAt, "startedAt"),
            result);
    }

    private static SceneActivityOperationResult DecodeResult(ResultDto encoded)
    {
        ReceiptDto receipt = encoded.Receipt
            ?? throw new InvalidDataException(
                "A remote Scene child result requires a receipt.");
        ActivityKind? activityKind = receipt.ActivityKind is null
            ? null
            : ActivityKind.Parse(receipt.ActivityKind);
        OperationReceipt decodedReceipt = OperationReceipt.FromRecordedResult(
            OperationId.Parse(receipt.OperationId),
            CorrelationId.Parse(receipt.CorrelationId),
            ParseOperationKind(receipt.Kind),
            ParseOperationStatus(receipt.Status),
            DeviceId.Parse(receipt.SourceDeviceId),
            DeviceId.Parse(receipt.TargetDeviceId),
            ActivityId.Parse(receipt.ActivityId),
            activityKind,
            receipt.DescriptorDigest,
            RequireUtc(receipt.OccurredAt, "occurredAt"),
            ParseFailureCode(receipt.FailureCode));
        UndoCapsuleReference? undo = encoded.Undo is null
            ? null
            : new UndoCapsuleReference(
                UndoCapsuleId.Parse(encoded.Undo.Id),
                OperationId.Parse(encoded.Undo.OperationId),
                CorrelationId.Parse(encoded.Undo.CorrelationId),
                decodedReceipt.TargetDeviceId,
                ActivityId.Parse(encoded.Undo.TargetActivityId),
                encoded.Undo.ExpectedTargetRevision,
                encoded.Undo.TargetDescriptorDigest,
                ActivityId.Parse(encoded.Undo.IncomingActivityId),
                encoded.Undo.IncomingDescriptorDigest,
                RequireUtc(encoded.Undo.ExpiresAt, "expiresAt"));
        return SceneActivityOperationResult.Create(decodedReceipt, undo);
    }

    private static EntryDto EncodeEntry(SceneRemoteChildJournalEntry entry) => new(
        entry.OperationId.ToString(),
        entry.CorrelationId.ToString(),
        entry.BindingDigest,
        entry.Status == SceneRemoteChildJournalStatus.Started
            ? "started"
            : "terminal",
        entry.StartedAt,
        entry.Result is null ? null : EncodeResult(entry.Result));

    private static ResultDto EncodeResult(SceneActivityOperationResult result) => new(
        new ReceiptDto(
            result.Receipt.OperationId.ToString(),
            result.Receipt.CorrelationId.ToString(),
            Format(result.Receipt.Kind),
            Format(result.Receipt.Status),
            result.Receipt.SourceDeviceId.ToString(),
            result.Receipt.TargetDeviceId.ToString(),
            result.Receipt.ActivityId.ToString(),
            result.Receipt.ActivityKind?.Value,
            result.Receipt.DescriptorDigest,
            result.Receipt.OccurredAt,
            Format(result.Receipt.FailureCode)),
        result.UndoCapsule is null
            ? null
            : new UndoDto(
                result.UndoCapsule.Id.ToString(),
                result.UndoCapsule.OperationId.ToString(),
                result.UndoCapsule.CorrelationId.ToString(),
                result.UndoCapsule.TargetActivityId.ToString(),
                result.UndoCapsule.ExpectedTargetRevision,
                result.UndoCapsule.TargetDescriptorDigest,
                result.UndoCapsule.IncomingActivityId.ToString(),
                result.UndoCapsule.IncomingDescriptorDigest,
                result.UndoCapsule.ExpiresAt));

    private static void ValidateNoDuplicateProperties(ReadOnlySpan<byte> payload)
    {
        byte[] encoded = payload.ToArray();
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                encoded,
                new JsonDocumentOptions { MaxDepth = SerializerOptions.MaxDepth });
            ValidateNoDuplicateProperties(document.RootElement);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
        }
    }

    private static void ValidateNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException(
                        "The remote Scene child journal contains a duplicate field.");
                }

                ValidateNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                ValidateNoDuplicateProperties(item);
            }
        }
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string name) =>
        value.Offset == TimeSpan.Zero
            ? value
            : throw new InvalidDataException(
                $"The remote Scene child '{name}' timestamp is not UTC.");

    private static string Format(OperationKind kind) => kind switch
    {
        OperationKind.Handoff => "handoff",
        OperationKind.Move => "move",
        OperationKind.Replace => "replace",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static OperationKind ParseOperationKind(string value) => value switch
    {
        "handoff" => OperationKind.Handoff,
        "move" => OperationKind.Move,
        "replace" => OperationKind.Replace,
        _ => throw new InvalidDataException("The operation kind is unsupported."),
    };

    private static string Format(OperationStatus status) => status switch
    {
        OperationStatus.Committed => "committed",
        OperationStatus.CommittedWithWarning => "committed-with-warning",
        OperationStatus.Rejected => "rejected",
        OperationStatus.Failed => "failed",
        OperationStatus.Recovering => "recovering",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static OperationStatus ParseOperationStatus(string value) => value switch
    {
        "committed" => OperationStatus.Committed,
        "committed-with-warning" => OperationStatus.CommittedWithWarning,
        "rejected" => OperationStatus.Rejected,
        "failed" => OperationStatus.Failed,
        "recovering" => OperationStatus.Recovering,
        _ => throw new InvalidDataException("The operation status is unsupported."),
    };

    private static string Format(FailureCode failureCode) =>
        failureCode.ToString();

    private static FailureCode ParseFailureCode(string value) =>
        Enum.TryParse(value, ignoreCase: false, out FailureCode parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidDataException("The failure code is unsupported.");

    private sealed record StateDto(int FormatVersion, EntryDto[] Entries);

    private sealed record EntryDto(
        string OperationId,
        string CorrelationId,
        string BindingDigest,
        string Status,
        DateTimeOffset StartedAt,
        ResultDto? Result);

    private sealed record ResultDto(ReceiptDto? Receipt, UndoDto? Undo);

    private sealed record ReceiptDto(
        string OperationId,
        string CorrelationId,
        string Kind,
        string Status,
        string SourceDeviceId,
        string TargetDeviceId,
        string ActivityId,
        string? ActivityKind,
        string? DescriptorDigest,
        DateTimeOffset OccurredAt,
        string FailureCode);

    private sealed record UndoDto(
        string Id,
        string OperationId,
        string CorrelationId,
        string TargetActivityId,
        long ExpectedTargetRevision,
        string TargetDescriptorDigest,
        string IncomingActivityId,
        string IncomingDescriptorDigest,
        DateTimeOffset ExpiresAt);
}
