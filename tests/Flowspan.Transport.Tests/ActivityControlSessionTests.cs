using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using Flowspan.Application;
using Flowspan.Application.Adapters;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class ActivityControlSessionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 14, 16, 30, 0, TimeSpan.Zero);

    private static readonly DeviceId LocalId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId PeerId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task OutboundTransferWaitsForMatchingPayloadFreeReceipt()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        ActivityTransferOffer offer = CreateOffer(LocalId, PeerId);

        ValueTask<ActivityDeliveryResult> sending = session.SendAsync(
            LocalId,
            offer,
            CancellationToken.None);
        ControlMessage transfer = await connection.ReadSentAsync();
        OperationReceipt receipt = OperationReceipt.Committed(
            offer.Context.OperationId,
            offer.Context.CorrelationId,
            offer.Kind,
            LocalId,
            PeerId,
            offer.Descriptor,
            Now.AddSeconds(1));
        connection.Receive(ActivityControlMessageCodec.CreateReceipt(
            transfer.Version,
            PeerId,
            receipt,
            Now.AddSeconds(1)));

        ActivityDeliveryResult result = await sending;

        Assert.Equal(ActivityDeliveryStatus.Acknowledged, result.Status);
        Assert.Equal(receipt, result.Receipt);
        Assert.DoesNotContain(
            "portable secret",
            connection.LastSentBody(ControlMessageType.OperationReceipt),
            StringComparison.Ordinal);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task InboundTransferUsesAuthenticatedPeerAndReturnsReceipt()
    {
        var catalog = new InMemoryActivityCatalog();
        var target = new FlowspanNode(
            LocalId,
            "Target",
            new FixedClock(Now),
            catalog,
            new InMemoryOperationJournal(),
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]),
            NullReceiptSink.Instance);
        target.SetPeerGrant(
            PeerId,
            CapabilityGrant.Of(Capability.ActivityOffer));
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            target,
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        ActivityTransferOffer offer = CreateOffer(PeerId, LocalId);

        connection.Receive(ActivityControlMessageCodec.CreateTransfer(
            new ProtocolVersion(1, 0),
            PeerId,
            offer,
            Now));
        ControlMessage response = await connection.ReadSentAsync();
        OperationReceipt receipt = ActivityControlMessageCodec.DecodeReceipt(
            response,
            PeerId,
            offer.Context.CorrelationId);

        Assert.True(receipt.IsSuccess);
        Assert.True(catalog.TryGet(offer.Descriptor.Id, out ActivityInstance? resumed));
        Assert.Equal(LocalId, resumed.Placement.DeviceId);
        Assert.Equal(ActivityLifecycle.Active, resumed.Lifecycle);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task SessionEndMarksSentButUnacknowledgedTransferAsUncertain()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();

        ValueTask<ActivityDeliveryResult> sending = session.SendAsync(
            LocalId,
            CreateOffer(LocalId, PeerId),
            CancellationToken.None);
        _ = await connection.ReadSentAsync();
        stop.Cancel();

        ActivityDeliveryResult result = await sending;
        Assert.Equal(ActivityDeliveryStatus.AcknowledgementLost, result.Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task UnsolicitedOrWrongCorrelationReceiptFaultsClosed()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        ActivityTransferOffer offer = CreateOffer(LocalId, PeerId);
        ValueTask<ActivityDeliveryResult> sending = session.SendAsync(
            LocalId,
            offer,
            CancellationToken.None);
        _ = await connection.ReadSentAsync();
        OperationReceipt receipt = OperationReceipt.Committed(
            offer.Context.OperationId,
            CorrelationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            offer.Kind,
            LocalId,
            PeerId,
            offer.Descriptor,
            Now);

        connection.Receive(ActivityControlMessageCodec.CreateReceipt(
            new ProtocolVersion(1, 0),
            PeerId,
            receipt,
            Now));

        Exception? runFailure = await Record.ExceptionAsync(
            () => run.WaitAsync(TimeSpan.FromSeconds(1)));
        if (!run.IsCompleted)
        {
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }

        Assert.IsType<InvalidDataException>(runFailure);
        Assert.Equal(
            ActivityDeliveryStatus.AcknowledgementLost,
            (await sending).Status);
    }

    [Fact]
    public async Task ReceiptForDifferentActivityFaultsClosed()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        ActivityTransferOffer offer = CreateOffer(LocalId, PeerId);
        ValueTask<ActivityDeliveryResult> sending = session.SendAsync(
            LocalId,
            offer,
            CancellationToken.None);
        _ = await connection.ReadSentAsync();
        ActivityDescriptor differentDescriptor = ActivityDescriptor.Create(
            ActivityId.From(Guid.NewGuid()),
            offer.Descriptor.Kind,
            LocalId,
            "Different note",
            JsonSerializer.Serialize(new { text = "different payload" }));
        OperationReceipt receipt = OperationReceipt.Committed(
            offer.Context.OperationId,
            offer.Context.CorrelationId,
            offer.Kind,
            LocalId,
            PeerId,
            differentDescriptor,
            Now);

        connection.Receive(ActivityControlMessageCodec.CreateReceipt(
            new ProtocolVersion(1, 0),
            PeerId,
            receipt,
            Now));

        Exception? runFailure = await Record.ExceptionAsync(
            () => run.WaitAsync(TimeSpan.FromSeconds(1)));
        if (!run.IsCompleted)
        {
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }

        Assert.IsType<InvalidDataException>(runFailure);
        Assert.Equal(
            ActivityDeliveryStatus.AcknowledgementLost,
            (await sending).Status);
    }

    [Fact]
    public async Task RealAuthenticatedLoopbackHandsOffAndPreservesSource()
    {
        using DeviceIdentityFixture identities = new();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                identities.Target,
                new TrustRecord(
                    identities.Source.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(Capability.ActivityOffer)),
                [new ProtocolVersion(1, 0)]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                identities.Source,
                new TrustRecord(
                    identities.Target.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(Capability.ActivityReceive)),
                [new ProtocolVersion(1, 0)]);
        await using AuthenticatedTcpControlConnection targetConnection = await accepting;
        var sourceCatalog = new InMemoryActivityCatalog();
        var targetCatalog = new InMemoryActivityCatalog();
        FlowspanNode source = CreateNode(
            identities.Source.DeviceId,
            "Source",
            sourceCatalog);
        FlowspanNode target = CreateNode(
            identities.Target.DeviceId,
            "Target",
            targetCatalog);
        target.SetPeerGrant(
            identities.Source.DeviceId,
            CapabilityGrant.Of(Capability.ActivityOffer));
        ActivityTransferOffer offer = CreateOffer(
            identities.Source.DeviceId,
            identities.Target.DeviceId);
        source.AddLocalActivity(ActivityInstance.Active(
            offer.Descriptor,
            ActivityPlacement.On(identities.Source.DeviceId)));
        await using var sourceHandler = new AuthenticatedActivitySessionHandler(
            source,
            new FixedTimeProvider(Now));
        await using var targetHandler = new AuthenticatedActivitySessionHandler(
            target,
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task sourceRun = sourceHandler.RunAsync(sourceConnection, stop.Token).AsTask();
        Task targetRun = targetHandler.RunAsync(targetConnection, stop.Token).AsTask();
        Assert.True(sourceHandler.TryGetChannel(
            identities.Target.DeviceId,
            out IActivityChannel? channel));
        Assert.NotNull(channel);

        OperationReceipt receipt = await source.HandoffAsync(
            offer.Descriptor.Id,
            channel,
            "desktop",
            offer.Context);

        Assert.True(receipt.IsSuccess);
        Assert.True(sourceCatalog.TryGet(offer.Descriptor.Id, out ActivityInstance? sourceCopy));
        Assert.True(targetCatalog.TryGet(offer.Descriptor.Id, out ActivityInstance? targetCopy));
        Assert.Equal(ActivityLifecycle.Active, sourceCopy.Lifecycle);
        Assert.Equal(ActivityLifecycle.Active, targetCopy.Lifecycle);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => targetRun);
    }

    private static ActivityTransferOffer CreateOffer(
        DeviceId sourceId,
        DeviceId targetId)
    {
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.From(Guid.NewGuid()),
            ActivityKind.Parse("workspace.note/v1"),
            sourceId,
            "Portable note",
            JsonSerializer.Serialize(new { text = "portable secret" }));
        return ActivityTransferOffer.Create(
            OperationKind.Handoff,
            OperationContext.Create(
                OperationId.From(Guid.NewGuid()),
                CorrelationId.From(Guid.NewGuid()),
                Now.AddSeconds(30)),
            descriptor,
            ActivityPlacement.On(targetId, "desktop"));
    }

    private static FlowspanNode CreateNode(
        DeviceId deviceId,
        string name,
        InMemoryActivityCatalog catalog) => new(
        deviceId,
        name,
        new FixedClock(Now),
        catalog,
        new InMemoryOperationJournal(),
        new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]),
        NullReceiptSink.Instance);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RejectingActivityPeer(DeviceId deviceId) : IActivityPeer
    {
        public DeviceId DeviceId { get; } = deviceId;

        public ValueTask<OperationReceipt> ReceiveActivityAsync(
            DeviceId senderDeviceId,
            ActivityTransferOffer offer,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<OperationReceipt>(
                new InvalidOperationException("No inbound transfer was expected."));
    }

    private sealed class FakeActivityControlConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) : IActivityControlConnection
    {
        private readonly Channel<ControlMessage> incoming = Channel.CreateUnbounded<ControlMessage>();
        private readonly Channel<ControlMessage> outgoing = Channel.CreateUnbounded<ControlMessage>();
        private readonly List<ControlMessage> sent = [];

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } = new(1, 0);

        public void Receive(ControlMessage message) =>
            incoming.Writer.TryWrite(message);

        public string LastSentBody(ControlMessageType type) => sent
            .LastOrDefault(message => message.Type == type)?.Body.GetRawText()
            ?? string.Empty;

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            incoming.Reader.ReadAsync(cancellationToken);

        public async ValueTask<ControlMessage> ReadSentAsync() =>
            await outgoing.Reader.ReadAsync();

        public ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sent.Add(message);
            outgoing.Writer.TryWrite(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DeviceIdentityFixture : IDisposable
    {
        public DeviceIdentityFixture()
        {
            Source = DeviceIdentity.Generate(LocalId, "Source");
            Target = DeviceIdentity.Generate(PeerId, "Target");
        }

        public DeviceIdentity Source { get; }

        public DeviceIdentity Target { get; }

        public void Dispose()
        {
            Source.Dispose();
            Target.Dispose();
        }
    }
}
