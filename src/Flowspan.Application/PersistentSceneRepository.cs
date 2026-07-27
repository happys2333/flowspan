using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Flowspan.Domain;

namespace Flowspan.Application;

public interface ISceneRepositoryStatePayloadStore
{
    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}

public sealed class SceneRepositoryPersistenceException : IOException
{
    public SceneRepositoryPersistenceException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed record SceneRepositoryEntry
{
    private SceneRepositoryEntry(
        ScenePlan scene,
        DateTimeOffset savedAt,
        string sceneDigest)
    {
        Scene = scene;
        SavedAt = savedAt;
        SceneDigest = sceneDigest;
    }

    public ScenePlan Scene { get; }

    public DateTimeOffset SavedAt { get; }

    public string SceneDigest { get; }

    public static SceneRepositoryEntry Create(
        ScenePlan scene,
        DateTimeOffset savedAt)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (savedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A Scene repository save timestamp must be UTC.",
                nameof(savedAt));
        }

        byte[] canonical = ScenePlanCodec.Encode(scene);
        try
        {
            return new SceneRepositoryEntry(
                scene,
                savedAt,
                Convert.ToHexString(SHA256.HashData(canonical)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonical);
        }
    }

    public override string ToString() =>
        $"Scene repository entry {Scene.Id} revision {Scene.Revision} saved {SavedAt:O}";
}

public sealed class PersistentSceneRepository : IDisposable
{
    public const int MaximumPayloadBytes = 4 * 1024 * 1024;
    public const int MaximumSceneCount = 64;

    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly ISceneRepositoryStatePayloadStore payloadStore;
    private readonly Lock snapshotGate = new();
    private bool disposed;
    private Dictionary<SceneId, SceneRepositoryEntry> entries;
    private bool requiresReload;

    private PersistentSceneRepository(
        ISceneRepositoryStatePayloadStore payloadStore,
        IEnumerable<SceneRepositoryEntry> entries)
    {
        this.payloadStore = payloadStore;
        this.entries = entries.ToDictionary(static entry => entry.Scene.Id);
    }

    public int SceneCount
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

    public static async ValueTask<PersistentSceneRepository> OpenAsync(
        ISceneRepositoryStatePayloadStore payloadStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloadStore);
        byte[]? payload = await payloadStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            return new PersistentSceneRepository(payloadStore, []);
        }

        try
        {
            return new PersistentSceneRepository(
                payloadStore,
                SceneRepositoryStatePayloadCodec.Decode(payload));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public ImmutableArray<SceneRepositoryEntry> Snapshot()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (snapshotGate)
        {
            return entries.Values
                .OrderBy(
                    static entry => entry.Scene.Id.ToString(),
                    StringComparer.Ordinal)
                .ToImmutableArray();
        }
    }

    public async ValueTask<SceneRepositoryEntry> SaveAsync(
        ScenePlan scene,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        SceneRepositoryEntry proposed = SceneRepositoryEntry.Create(
            scene,
            savedAt);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfReloadRequired();
            Dictionary<SceneId, SceneRepositoryEntry> candidate = CopyEntries();
            if (candidate.TryGetValue(
                    scene.Id,
                    out SceneRepositoryEntry? existing))
            {
                if (scene.Revision == existing.Scene.Revision
                    && StringComparer.Ordinal.Equals(
                        proposed.SceneDigest,
                        existing.SceneDigest))
                {
                    return existing;
                }

                if (scene.Revision <= existing.Scene.Revision)
                {
                    throw new InvalidOperationException(
                        "A stored Scene can only be replaced by a strictly greater revision.");
                }
            }
            else if (candidate.Count >= MaximumSceneCount)
            {
                throw new InvalidOperationException(
                    $"The Scene repository cannot contain more than {MaximumSceneCount} Scenes.");
            }

            candidate[scene.Id] = proposed;
            await SaveAndPublishAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
            return proposed;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async ValueTask<bool> DeleteAsync(
        SceneId sceneId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(sceneId);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfReloadRequired();
            Dictionary<SceneId, SceneRepositoryEntry> candidate = CopyEntries();
            if (!candidate.Remove(sceneId))
            {
                return false;
            }

            await SaveAndPublishAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
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

    private Dictionary<SceneId, SceneRepositoryEntry> CopyEntries()
    {
        lock (snapshotGate)
        {
            return new Dictionary<SceneId, SceneRepositoryEntry>(entries);
        }
    }

    private async ValueTask SaveAndPublishAsync(
        Dictionary<SceneId, SceneRepositoryEntry> candidate,
        CancellationToken cancellationToken)
    {
        byte[] payload = SceneRepositoryStatePayloadCodec.Encode(
            candidate.Values);
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

            throw new SceneRepositoryPersistenceException(
                "The durable Scene repository state could not be saved.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        lock (snapshotGate)
        {
            entries = candidate;
        }
    }

    private void ThrowIfReloadRequired()
    {
        if (requiresReload)
        {
            throw new SceneRepositoryPersistenceException(
                "The Scene repository must be reopened after an ambiguous save failure.",
                new IOException("The prior durable save outcome is unknown."));
        }
    }
}

internal static class SceneRepositoryStatePayloadCodec
{
    private const int CurrentFormatVersion = 1;
    private const int InitialEncodeCapacityBytes = 64 * 1024;
    private const int MaximumDocumentDepth = 12;

    public static IReadOnlyList<SceneRepositoryEntry> Decode(
        ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty
            || payload.Length > PersistentSceneRepository.MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"A Scene repository state payload must contain 1 to {PersistentSceneRepository.MaximumPayloadBytes} bytes.");
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
                    "A Scene repository state payload must be a JSON object.");
            }

            ValidateProperties(root, "formatVersion", "scenes");
            int formatVersion = root.GetProperty("formatVersion").GetInt32();
            if (formatVersion != CurrentFormatVersion)
            {
                throw new InvalidDataException(
                    "The Scene repository state payload has an unsupported envelope version.");
            }

            JsonElement scenes = root.GetProperty("scenes");
            if (scenes.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "Scene repository entries must be a JSON array.");
            }

            var decoded = new List<SceneRepositoryEntry>();
            string? previousSceneId = null;
            foreach (JsonElement entry in scenes.EnumerateArray())
            {
                if (decoded.Count >= PersistentSceneRepository.MaximumSceneCount)
                {
                    throw new InvalidDataException(
                        $"The Scene repository cannot contain more than {PersistentSceneRepository.MaximumSceneCount} Scenes.");
                }

                SceneRepositoryEntry restored = DecodeEntry(entry);
                string sceneId = restored.Scene.Id.ToString();
                if (previousSceneId is not null
                    && StringComparer.Ordinal.Compare(
                        previousSceneId,
                        sceneId) >= 0)
                {
                    throw new InvalidDataException(
                        "Scene repository entries are duplicated or not canonically ordered.");
                }

                previousSceneId = sceneId;
                decoded.Add(restored);
            }

            return decoded;
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or InvalidOperationException
            or JsonException
            or KeyNotFoundException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The Scene repository state payload is malformed.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(owned);
        }
    }

    public static byte[] Encode(IEnumerable<SceneRepositoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        SceneRepositoryEntry[] ordered = entries
            .OrderBy(
                static entry => entry.Scene.Id.ToString(),
                StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length > PersistentSceneRepository.MaximumSceneCount
            || ordered.Select(static entry => entry.Scene.Id)
                .Distinct()
                .Count() != ordered.Length)
        {
            throw new InvalidDataException(
                "Scene repository entries exceed bounds or contain duplicate Scene IDs.");
        }

        var output = new ArrayBufferWriter<byte>(InitialEncodeCapacityBytes);
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
                writer.WriteStartArray("scenes");
                foreach (SceneRepositoryEntry entry in ordered)
                {
                    writer.WriteStartObject();
                    writer.WriteString(
                        "savedAt",
                        FormatTimestamp(entry.SavedAt));
                    writer.WritePropertyName("scene");
                    WriteCanonicalScene(writer, entry.Scene);
                    writer.WriteEndObject();
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

        if (payload.Length > PersistentSceneRepository.MaximumPayloadBytes)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new InvalidDataException(
                $"A Scene repository state payload cannot exceed {PersistentSceneRepository.MaximumPayloadBytes} bytes.");
        }

        return payload;
    }

    private static void WriteCanonicalScene(
        Utf8JsonWriter writer,
        ScenePlan scene)
    {
        byte[] canonical = ScenePlanCodec.Encode(scene);
        try
        {
            writer.WriteRawValue(canonical, skipInputValidation: false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonical);
        }
    }

    private static SceneRepositoryEntry DecodeEntry(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "A Scene repository entry must be a JSON object.");
        }

        ValidateProperties(entry, "savedAt", "scene");
        DateTimeOffset savedAt = ParseTimestamp(
            entry.GetProperty("savedAt").GetString());
        JsonElement scene = entry.GetProperty("scene");
        if (scene.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "A stored Scene must be a JSON object.");
        }

        byte[] canonical = Encoding.UTF8.GetBytes(scene.GetRawText());
        byte[]? reencoded = null;
        try
        {
            ScenePlan restored = ScenePlanCodec.Decode(canonical);
            reencoded = ScenePlanCodec.Encode(restored);
            if (!canonical.AsSpan().SequenceEqual(reencoded))
            {
                throw new InvalidDataException(
                    "A stored Scene is not in canonical form.");
            }

            return SceneRepositoryEntry.Create(restored, savedAt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonical);
            if (reencoded is not null)
            {
                CryptographicOperations.ZeroMemory(reencoded);
            }
        }
    }

    private static void ValidateProperties(
        JsonElement element,
        params string[] expectedNames)
    {
        var expected = new HashSet<string>(expectedNames, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!expected.Contains(property.Name) || !seen.Add(property.Name))
            {
                throw new InvalidDataException(
                    "The Scene repository state contains an unknown or duplicate property.");
            }
        }

        if (!seen.SetEquals(expected))
        {
            throw new InvalidDataException(
                "The Scene repository state is missing a required property.");
        }
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        if (timestamp.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException(
                "Scene repository timestamps must be canonical UTC.");
        }

        return timestamp.ToString("O", CultureInfo.InvariantCulture);
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
            || timestamp.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException(
                "A Scene repository savedAt timestamp is not canonical UTC.");
        }

        return timestamp;
    }
}
