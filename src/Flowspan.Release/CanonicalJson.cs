using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flowspan.Release;

public static class CanonicalJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static byte[] Encode(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(node, Options);
        byte[] terminated = new byte[encoded.Length + 1];
        encoded.CopyTo(terminated, 0);
        terminated[^1] = (byte)'\n';
        return terminated;
    }

    public static JsonObject DecodeObject(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length is 0 or > ReleaseBounds.MaximumJsonBytes)
        {
            throw new ReleaseInputException(
                "The release JSON document is empty or oversized.");
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(
                encoded,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32,
                });
        }
        catch (JsonException exception)
        {
            throw new ReleaseInputException(
                "The release JSON document is malformed.",
                exception);
        }

        if (node is not JsonObject result
            || !encoded.SequenceEqual(Encode(result)))
        {
            throw new ReleaseInputException(
                "The release JSON document is not canonical.");
        }

        return result;
    }

    public static void RequireProperties(
        JsonObject value,
        params string[] expectedProperties)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(expectedProperties);
        string[] actual = value.Select(static property => property.Key).ToArray();
        if (!actual.SequenceEqual(expectedProperties, StringComparer.Ordinal))
        {
            throw new ReleaseInputException(
                "The release JSON document contains unexpected properties or order.");
        }
    }

    public static string ReadString(JsonObject value, string property)
    {
        try
        {
            string? result = value[property]?.GetValue<string>();
            if (string.IsNullOrEmpty(result))
            {
                throw new ReleaseInputException(
                    $"The release JSON {property} is empty.");
            }

            return result;
        }
        catch (InvalidOperationException exception)
        {
            throw new ReleaseInputException(
                $"The release JSON {property} is not a string.",
                exception);
        }
    }

    public static long ReadInt64(JsonObject value, string property)
    {
        try
        {
            return value[property]?.GetValue<long>()
                ?? throw new ReleaseInputException(
                    $"The release JSON {property} is missing.");
        }
        catch (InvalidOperationException exception)
        {
            throw new ReleaseInputException(
                $"The release JSON {property} is not an integer.",
                exception);
        }
    }

    public static JsonArray ReadArray(JsonObject value, string property) =>
        value[property] as JsonArray
        ?? throw new ReleaseInputException(
            $"The release JSON {property} is not an array.");

    public static JsonObject ReadObject(JsonObject value, string property) =>
        value[property] as JsonObject
        ?? throw new ReleaseInputException(
            $"The release JSON {property} is not an object.");

    public static bool ReadBoolean(JsonObject value, string property)
    {
        try
        {
            return value[property]?.GetValue<bool>()
                ?? throw new ReleaseInputException(
                    $"The release JSON {property} is missing.");
        }
        catch (InvalidOperationException exception)
        {
            throw new ReleaseInputException(
                $"The release JSON {property} is not a boolean.",
                exception);
        }
    }
}
