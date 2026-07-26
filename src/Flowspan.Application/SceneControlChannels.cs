using Flowspan.Domain;

namespace Flowspan.Application;

public enum SceneControlDeliveryStatus
{
    NotDelivered,
    Acknowledged,
    AcknowledgementLost,
    ProtocolUnsupported,
}

public sealed record SceneSourceLookupDeliveryResult(
    SceneControlDeliveryStatus Status,
    SceneSourceLookup? Result)
{
    public static SceneSourceLookupDeliveryResult NotDelivered { get; } =
        new(SceneControlDeliveryStatus.NotDelivered, null);

    public static SceneSourceLookupDeliveryResult AcknowledgementLost { get; } =
        new(SceneControlDeliveryStatus.AcknowledgementLost, null);

    public static SceneSourceLookupDeliveryResult ProtocolUnsupported { get; } =
        new(SceneControlDeliveryStatus.ProtocolUnsupported, null);

    public static SceneSourceLookupDeliveryResult Acknowledged(
        SceneSourceLookup result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new SceneSourceLookupDeliveryResult(
            SceneControlDeliveryStatus.Acknowledged,
            result);
    }
}

public interface ISceneSourceLookupChannel
{
    public DeviceId TargetDeviceId { get; }

    public ValueTask<SceneSourceLookupDeliveryResult> QuerySourceAsync(
        DeviceId requestingDeviceId,
        SceneSourceLookupQuery query,
        CancellationToken cancellationToken);
}

public sealed record SceneExactSlotDeliveryResult(
    SceneControlDeliveryStatus Status,
    SceneExactSlotInspection? Result)
{
    public static SceneExactSlotDeliveryResult NotDelivered { get; } =
        new(SceneControlDeliveryStatus.NotDelivered, null);

    public static SceneExactSlotDeliveryResult AcknowledgementLost { get; } =
        new(SceneControlDeliveryStatus.AcknowledgementLost, null);

    public static SceneExactSlotDeliveryResult ProtocolUnsupported { get; } =
        new(SceneControlDeliveryStatus.ProtocolUnsupported, null);

    public static SceneExactSlotDeliveryResult Acknowledged(
        SceneExactSlotInspection result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new SceneExactSlotDeliveryResult(
            SceneControlDeliveryStatus.Acknowledged,
            result);
    }
}

public interface ISceneExactSlotChannel
{
    public DeviceId TargetDeviceId { get; }

    public ValueTask<SceneExactSlotDeliveryResult> InspectSlotAsync(
        DeviceId requestingDeviceId,
        SceneExactSlotQuery query,
        CancellationToken cancellationToken);
}

public sealed record SceneChildDeliveryResult(
    SceneControlDeliveryStatus Status,
    SceneActivityOperationResult? Result)
{
    public static SceneChildDeliveryResult NotDelivered { get; } =
        new(SceneControlDeliveryStatus.NotDelivered, null);

    public static SceneChildDeliveryResult AcknowledgementLost { get; } =
        new(SceneControlDeliveryStatus.AcknowledgementLost, null);

    public static SceneChildDeliveryResult ProtocolUnsupported { get; } =
        new(SceneControlDeliveryStatus.ProtocolUnsupported, null);

    public static SceneChildDeliveryResult Acknowledged(
        SceneActivityOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new SceneChildDeliveryResult(
            SceneControlDeliveryStatus.Acknowledged,
            result);
    }
}

public interface ISceneChildOperationChannel
{
    public DeviceId TargetDeviceId { get; }

    public ValueTask<SceneChildDeliveryResult> ExecuteChildAsync(
        DeviceId requestingDeviceId,
        SceneRemoteChildInstruction instruction,
        CancellationToken cancellationToken);
}

public interface ISceneControlPeer
{
    public DeviceId DeviceId { get; }

    public ValueTask<SceneSourceLookup> LocateSourceAsync(
        DeviceId coordinatorDeviceId,
        SceneSourceLookupQuery query,
        CancellationToken cancellationToken);

    public ValueTask<SceneExactSlotInspection> InspectExactSlotAsync(
        DeviceId coordinatorDeviceId,
        SceneExactSlotQuery query,
        CancellationToken cancellationToken);

    public ValueTask<SceneActivityOperationResult> ExecuteChildAsync(
        DeviceId coordinatorDeviceId,
        SceneRemoteChildInstruction instruction,
        CancellationToken cancellationToken);
}
