using System.Text.Json;
using Flowspan.Application;
using Flowspan.Application.Adapters;
using Flowspan.Diagnostics;
using Flowspan.Domain;
using Flowspan.Protocol;

var clock = new SimulatorClock(new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero));
var sourceReceipts = new InMemoryReceiptSink();
var targetReceipts = new InMemoryReceiptSink();
var sourceCatalog = new InMemoryActivityCatalog();
var targetCatalog = new InMemoryActivityCatalog();
var adapter = new WorkspaceNoteAdapter();

var source = new FlowspanNode(
    DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
    "Laptop",
    clock,
    sourceCatalog,
    new InMemoryOperationJournal(),
    new ActivityAdapterRegistry([adapter]),
    sourceReceipts);
var target = new FlowspanNode(
    DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
    "Desk",
    clock,
    targetCatalog,
    new InMemoryOperationJournal(),
    new ActivityAdapterRegistry([adapter]),
    targetReceipts);

target.SetPeerGrant(
    source.DeviceId,
    CapabilityGrant.Of(Capability.ActivityOffer));

ActivityDescriptor descriptor = ActivityDescriptor.Create(
    ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
    ActivityKind.Parse("workspace.note/v1"),
    source.DeviceId,
    "Release checklist",
    JsonSerializer.Serialize(new { text = "Verify the two-node handoff." }));
source.AddLocalActivity(ActivityInstance.Active(
    descriptor,
    ActivityPlacement.On(source.DeviceId)));

ProtocolNegotiationResult negotiation = ProtocolNegotiator.Negotiate(
    [new ProtocolVersion(1, 0)],
    [new ProtocolVersion(1, 0)]);
if (!negotiation.Succeeded)
{
    throw new InvalidOperationException("The simulator nodes have no common protocol version.");
}

OperationContext context = OperationContext.Create(
    OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
    CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
    clock.UtcNow.AddSeconds(30));
OperationReceipt receipt = await source.HandoffAsync(
    descriptor.Id,
    new DirectActivityChannel(target),
    "main",
    context);

bool sourceActive = source.TryGetActivity(descriptor.Id, out ActivityInstance? sourceActivity)
    && sourceActivity.Lifecycle == ActivityLifecycle.Active;
bool targetActive = target.TryGetActivity(descriptor.Id, out ActivityInstance? targetActivity)
    && targetActivity.Lifecycle == ActivityLifecycle.Active;

ActivityDescriptor firstSwapDescriptor = ActivityDescriptor.Create(
    ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
    ActivityKind.Parse("workspace.note/v1"),
    source.DeviceId,
    "Laptop plan",
    JsonSerializer.Serialize(new { text = "Move this plan to the desk." }));
ActivityDescriptor secondSwapDescriptor = ActivityDescriptor.Create(
    ActivityId.Parse("99999999-9999-9999-9999-999999999999"),
    ActivityKind.Parse("workspace.note/v1"),
    target.DeviceId,
    "Desk notes",
    JsonSerializer.Serialize(new { text = "Move these notes to the laptop." }));
ActivityInstance firstSwapActivity = ActivityInstance.Active(
    firstSwapDescriptor,
    ActivityPlacement.On(source.DeviceId));
ActivityInstance secondSwapActivity = ActivityInstance.Active(
    secondSwapDescriptor,
    ActivityPlacement.On(target.DeviceId));
var firstSwapCatalog = new InMemoryActivityCatalog();
var secondSwapCatalog = new InMemoryActivityCatalog();
firstSwapCatalog.TryAdd(firstSwapActivity);
secondSwapCatalog.TryAdd(secondSwapActivity);
var firstSwapEndpoint = new InMemorySwapEndpoint(source.DeviceId, firstSwapCatalog);
var secondSwapEndpoint = new InMemorySwapEndpoint(target.DeviceId, secondSwapCatalog);
var swapPayloadStore = new SimulatorSwapStatePayloadStore();
using PersistentSwapTransactionJournal swapJournal =
    await PersistentSwapTransactionJournal.OpenAsync(swapPayloadStore);
var swapCoordinator = new SwapCoordinator(
    clock,
    swapJournal,
    new DeterministicSwapTokenSource(
    [
        SwapReservationToken.From(
            Guid.Parse("12121212-1212-1212-1212-121212121212")),
        SwapReservationToken.From(
            Guid.Parse("13131313-1313-1313-1313-131313131313")),
    ]));
SwapCoordinatorResult swap = await swapCoordinator.ExecuteAsync(
    OperationContext.Create(
        OperationId.Parse("14141414-1414-1414-1414-141414141414"),
        CorrelationId.Parse("15151515-1515-1515-1515-151515151515"),
        clock.UtcNow.AddSeconds(30)),
    new DirectSwapEndpointChannel(firstSwapEndpoint),
    firstSwapDescriptor.Id,
    new DirectSwapEndpointChannel(secondSwapEndpoint),
    secondSwapDescriptor.Id);
bool swapConverged = swap.IsSuccess
    && firstSwapEndpoint.TryGetActivity(secondSwapDescriptor.Id, out _)
    && secondSwapEndpoint.TryGetActivity(firstSwapDescriptor.Id, out _)
    && !firstSwapEndpoint.TryGetActivity(firstSwapDescriptor.Id, out _)
    && !secondSwapEndpoint.TryGetActivity(secondSwapDescriptor.Id, out _);

Console.WriteLine($"Protocol: {negotiation.Version}");
Console.WriteLine($"Source preserved: {sourceActive}");
Console.WriteLine($"Target resumed: {targetActive}");
Console.WriteLine($"Atomic swap committed: {swapConverged}");
Console.WriteLine(ReceiptJson.Serialize(receipt));

return receipt.IsSuccess && sourceActive && targetActive && swapConverged ? 0 : 1;

internal sealed class SimulatorClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}

internal sealed class SimulatorSwapStatePayloadStore : ISwapStatePayloadStore
{
    private byte[]? payload;

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(payload?.ToArray());
    }

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.payload = payload.ToArray();
        return ValueTask.CompletedTask;
    }
}
