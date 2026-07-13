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
    CapabilityGrant.Of(Capability.ActivityReceive));

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
    target,
    "main",
    context);

bool sourceActive = source.TryGetActivity(descriptor.Id, out ActivityInstance? sourceActivity)
    && sourceActivity.Lifecycle == ActivityLifecycle.Active;
bool targetActive = target.TryGetActivity(descriptor.Id, out ActivityInstance? targetActivity)
    && targetActivity.Lifecycle == ActivityLifecycle.Active;

Console.WriteLine($"Protocol: {negotiation.Version}");
Console.WriteLine($"Source preserved: {sourceActive}");
Console.WriteLine($"Target resumed: {targetActive}");
Console.WriteLine(ReceiptJson.Serialize(receipt));

return receipt.IsSuccess && sourceActive && targetActive ? 0 : 1;

internal sealed class SimulatorClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}
