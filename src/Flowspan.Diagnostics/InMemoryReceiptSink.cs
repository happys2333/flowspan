using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Diagnostics;

public sealed class InMemoryReceiptSink : IReceiptSink
{
    private readonly ConcurrentQueue<OperationReceipt> receipts = new();

    public int Count => receipts.Count;

    public void Write(OperationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        receipts.Enqueue(receipt);
    }

    public IReadOnlyList<OperationReceipt> Snapshot() => receipts.ToArray();
}

public static class ReceiptJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(OperationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var redacted = new
        {
            operationId = receipt.OperationId.ToString(),
            correlationId = receipt.CorrelationId.ToString(),
            kind = receipt.Kind,
            status = receipt.Status,
            sourceDeviceId = receipt.SourceDeviceId.ToString(),
            targetDeviceId = receipt.TargetDeviceId.ToString(),
            activityId = receipt.ActivityId.ToString(),
            activityKind = receipt.ActivityKind?.Value,
            descriptorDigest = receipt.DescriptorDigest,
            receipt.OccurredAt,
            failureCode = receipt.FailureCode,
        };

        return JsonSerializer.Serialize(redacted, Options);
    }
}
