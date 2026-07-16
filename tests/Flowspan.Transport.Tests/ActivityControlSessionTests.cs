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
    public async Task CanceledRunNormalizesConnectionEofToCancellation()
    {
        var connection = new CancellationEndsWithEofActivityControlConnection(
            LocalId,
            PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        await connection.ReadStarted.WaitAsync(TimeSpan.FromSeconds(1));

        stop.Cancel();

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.IsType<EndOfStreamException>(failure.InnerException);
    }

    [Fact]
    public async Task RunningSessionPreservesConnectionEof()
    {
        var connection = new ImmediateEofActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));

        EndOfStreamException failure = await Assert.ThrowsAsync<EndOfStreamException>(
            () => session.RunAsync().AsTask());

        Assert.Equal("The peer closed the control channel.", failure.Message);
    }

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
    public async Task OutboundReplaceWaitsForExactlyBoundPayloadFreeResult()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new RejectingReplacePeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        ReplaceActivityCommand command = CreateReplaceCommand(LocalId, PeerId);

        ValueTask<ReplaceDeliveryResult> sending = session.SendAsync(
            LocalId,
            command,
            CancellationToken.None);
        ControlMessage request = await connection.ReadSentAsync();
        OperationReceipt receipt = OperationReceipt.Committed(
            command.Context.OperationId,
            command.Context.CorrelationId,
            OperationKind.Replace,
            LocalId,
            PeerId,
            command.IncomingDescriptor,
            Now.AddSeconds(1));
        var capsule = new UndoCapsuleReference(
            UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            command.Context.OperationId,
            command.Context.CorrelationId,
            command.TargetActivityId,
            command.ExpectedTargetRevision,
            command.ExpectedTargetDescriptorDigest,
            command.IncomingDescriptor.Id,
            command.IncomingDescriptor.DescriptorDigest,
            command.UndoExpiresAt);
        var expected = new ReplaceOperationResult(receipt, capsule);
        connection.Receive(ActivityControlMessageCodec.CreateReplaceResult(
            request.Version,
            PeerId,
            expected,
            Now.AddSeconds(1)));

        ReplaceDeliveryResult delivered = await sending;

        Assert.Equal(ActivityDeliveryStatus.Acknowledged, delivered.Status);
        Assert.Equal(expected, delivered.Result);
        Assert.DoesNotContain(
            "preserve target secret",
            connection.LastSentBody(ControlMessageType.ActivityReplace),
            StringComparison.Ordinal);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task OutboundReplaceInventoryWaitsForExactlyBoundResult()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            PeerId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));

        ValueTask<ReplaceTargetInventoryDeliveryResult> querying =
            session.QueryAsync(LocalId, query, CancellationToken.None);
        ControlMessage request = await connection.ReadSentAsync();
        ReplaceTargetSnapshot target = ReplaceTargetSnapshot.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            revision: 7,
            new string('A', 64),
            query.IncomingKind,
            "Remote target",
            "desktop");
        ReplaceTargetInventoryResult expected =
            ReplaceTargetInventoryResult.Success(
                LocalId,
                query,
                Now.AddSeconds(1),
                [target],
                isTruncated: false);
        connection.Receive(ActivityControlMessageCodec.CreateReplaceInventoryResult(
            request.Version,
            PeerId,
            expected,
            Now.AddSeconds(1)));

        ReplaceTargetInventoryDeliveryResult delivered = await querying;

        Assert.Equal(ActivityDeliveryStatus.Acknowledged, delivered.Status);
        Assert.NotNull(delivered.Result);
        Assert.Equal(expected.CorrelationId, delivered.Result.CorrelationId);
        Assert.Equal(expected.RequestingDeviceId, delivered.Result.RequestingDeviceId);
        Assert.Equal(expected.TargetDeviceId, delivered.Result.TargetDeviceId);
        Assert.Equal(expected.IncomingKind, delivered.Result.IncomingKind);
        Assert.Equal(expected.QueryDeadline, delivered.Result.QueryDeadline);
        Assert.Equal(expected.CapturedAt, delivered.Result.CapturedAt);
        Assert.Equal(expected.FailureCode, delivered.Result.FailureCode);
        Assert.Equal(expected.IsTruncated, delivered.Result.IsTruncated);
        Assert.Equal(expected.Targets.ToArray(), delivered.Result.Targets.ToArray());
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task PendingInventoryReservesCorrelationAcrossOperationTypes()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        CorrelationId correlationId =
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            correlationId,
            PeerId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));
        ValueTask<ReplaceTargetInventoryDeliveryResult> querying =
            session.QueryAsync(LocalId, query, CancellationToken.None);
        _ = await connection.ReadSentAsync();
        ActivityTransferOffer template = CreateOffer(LocalId, PeerId);
        ActivityTransferOffer collision = ActivityTransferOffer.Create(
            OperationKind.Handoff,
            OperationContext.Create(
                OperationId.From(Guid.NewGuid()),
                correlationId,
                Now.AddSeconds(30)),
            template.Descriptor,
            template.TargetPlacement);

        ValueTask<ActivityDeliveryResult> colliding = session.SendAsync(
            LocalId,
            collision,
            CancellationToken.None);
        bool completedImmediately = colliding.IsCompleted;
        stop.Cancel();
        _ = await querying;
        Exception? collisionFailure = await Record.ExceptionAsync(
            () => colliding.AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.True(completedImmediately);
        Assert.IsType<InvalidOperationException>(collisionFailure);
    }

    [Fact]
    public async Task PendingTransferReservesCorrelationAgainstReplace()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new RejectingReplacePeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        CorrelationId correlationId =
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        ActivityTransferOffer template = CreateOffer(LocalId, PeerId);
        ActivityTransferOffer transfer = ActivityTransferOffer.Create(
            OperationKind.Handoff,
            OperationContext.Create(
                OperationId.From(Guid.NewGuid()),
                correlationId,
                Now.AddSeconds(30)),
            template.Descriptor,
            template.TargetPlacement);
        ValueTask<ActivityDeliveryResult> sending = session.SendAsync(
            LocalId,
            transfer,
            CancellationToken.None);
        _ = await connection.ReadSentAsync();
        ReplaceActivityCommand replaceTemplate =
            CreateReplaceCommand(LocalId, PeerId);
        ReplaceActivityCommand collision = ReplaceActivityCommand.Create(
            OperationContext.Create(
                OperationId.From(Guid.NewGuid()),
                correlationId,
                Now.AddSeconds(30)),
            replaceTemplate.TargetActivityId,
            replaceTemplate.ExpectedTargetRevision,
            replaceTemplate.ExpectedTargetDescriptorDigest,
            replaceTemplate.IncomingDescriptor,
            replaceTemplate.TargetPlacement,
            replaceTemplate.UndoExpiresAt);

        ValueTask<ReplaceDeliveryResult> colliding = session.SendAsync(
            LocalId,
            collision,
            CancellationToken.None);
        bool completedImmediately = colliding.IsCompleted;
        stop.Cancel();
        _ = await sending;
        Exception? collisionFailure = await Record.ExceptionAsync(
            () => colliding.AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.True(completedImmediately);
        Assert.IsType<InvalidOperationException>(collisionFailure);
    }

    [Fact]
    public async Task PendingReplaceReservesCorrelationAgainstInventory()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new RejectingReplacePeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        CorrelationId correlationId =
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        ReplaceActivityCommand template = CreateReplaceCommand(LocalId, PeerId);
        ReplaceActivityCommand replace = ReplaceActivityCommand.Create(
            OperationContext.Create(
                OperationId.From(Guid.NewGuid()),
                correlationId,
                Now.AddSeconds(30)),
            template.TargetActivityId,
            template.ExpectedTargetRevision,
            template.ExpectedTargetDescriptorDigest,
            template.IncomingDescriptor,
            template.TargetPlacement,
            template.UndoExpiresAt);
        ValueTask<ReplaceDeliveryResult> sending = session.SendAsync(
            LocalId,
            replace,
            CancellationToken.None);
        _ = await connection.ReadSentAsync();
        ReplaceTargetInventoryQuery collision = ReplaceTargetInventoryQuery.Create(
            correlationId,
            PeerId,
            template.IncomingDescriptor.Kind,
            Now.AddSeconds(30));

        ValueTask<ReplaceTargetInventoryDeliveryResult> colliding =
            session.QueryAsync(LocalId, collision, CancellationToken.None);
        bool completedImmediately = colliding.IsCompleted;
        stop.Cancel();
        _ = await sending;
        Exception? collisionFailure = await Record.ExceptionAsync(
            () => colliding.AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.True(completedImmediately);
        Assert.IsType<InvalidOperationException>(collisionFailure);
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
    public async Task InboundReplaceUsesAuthenticatedPeerAndReturnsBoundUndoReference()
    {
        var catalog = new InMemoryActivityCatalog();
        ActivityDescriptor originalDescriptor = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            LocalId,
            "Target note",
            JsonSerializer.Serialize(new { text = "preserve target secret" }));
        ActivityInstance original = ActivityInstance.Active(
            originalDescriptor,
            ActivityPlacement.On(LocalId, "desktop"),
            revision: 7);
        Assert.True(catalog.TryAdd(original));
        using var endpoint = new ReplaceEndpoint(
            LocalId,
            new FixedClock(Now),
            catalog,
            new InMemoryOperationJournal(),
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]),
            new InMemoryReplaceStateStore(),
            new DeterministicUndoCapsuleIdSource(
            [
                UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            ]),
            NullReceiptSink.Instance);
        endpoint.SetPeerGrant(
            PeerId,
            CapabilityGrant.Of(Capability.ActivityReplace));
        ActivityDescriptor incoming = ActivityDescriptor.Create(
            ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ActivityKind.Parse("workspace.note/v1"),
            PeerId,
            "Incoming note",
            JsonSerializer.Serialize(new { text = "incoming secret" }));
        ReplaceActivityCommand command = ReplaceActivityCommand.Create(
            OperationContext.Create(
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Now.AddSeconds(30)),
            originalDescriptor.Id,
            original.Revision,
            originalDescriptor.DescriptorDigest,
            incoming,
            ActivityPlacement.On(LocalId, "desktop"),
            Now.AddMinutes(10));
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            endpoint,
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();

        connection.Receive(ActivityControlMessageCodec.CreateReplace(
            new ProtocolVersion(1, 0),
            PeerId,
            command,
            Now));
        ControlMessage response = await connection.ReadSentAsync();
        ReplaceOperationResult result =
            ActivityControlMessageCodec.DecodeReplaceResult(
                response,
                PeerId,
                command.Context.CorrelationId);

        Assert.Equal(OperationStatus.Committed, result.Receipt.Status);
        Assert.NotNull(result.UndoCapsule);
        Assert.False(catalog.TryGet(originalDescriptor.Id, out _));
        Assert.True(catalog.TryGet(incoming.Id, out ActivityInstance? replacement));
        Assert.Equal(8, replacement.Revision);
        Assert.DoesNotContain(
            "preserve target secret",
            response.Body.GetRawText(),
            StringComparison.Ordinal);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task InboundReplaceInventoryUsesAuthenticatedPeerAndReturnsSnapshot()
    {
        var catalog = new InMemoryActivityCatalog();
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            LocalId,
            "Target note",
            JsonSerializer.Serialize(new { text = "target secret" }));
        Assert.True(catalog.TryAdd(ActivityInstance.Active(
            descriptor,
            ActivityPlacement.On(LocalId, "desktop"),
            revision: 7)));
        var inventoryPeer = new ReplaceTargetInventoryEndpoint(
            LocalId,
            new FixedClock(Now),
            catalog,
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]));
        inventoryPeer.SetPeerGrant(
            PeerId,
            CapabilityGrant.Of(Capability.ActivityReplace));
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            replacePeer: null,
            replaceInventoryPeer: inventoryPeer,
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            LocalId,
            descriptor.Kind,
            Now.AddSeconds(30));

        connection.Receive(ActivityControlMessageCodec.CreateReplaceInventoryQuery(
            new ProtocolVersion(1, 0),
            PeerId,
            query,
            Now));
        ControlMessage response = await connection.ReadSentAsync();
        ReplaceTargetInventoryResult result =
            ActivityControlMessageCodec.DecodeReplaceInventoryResult(
                response,
                PeerId,
                query);

        Assert.True(result.IsSuccess);
        ReplaceTargetSnapshot target = Assert.Single(result.Targets);
        Assert.Equal(descriptor.Id, target.ActivityId);
        Assert.DoesNotContain(
            "target secret",
            response.Body.GetRawText(),
            StringComparison.Ordinal);
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
    public async Task SessionEndMarksSentButUnacknowledgedReplaceAsUncertain()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new RejectingReplacePeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();

        ValueTask<ReplaceDeliveryResult> sending = session.SendAsync(
            LocalId,
            CreateReplaceCommand(LocalId, PeerId),
            CancellationToken.None);
        _ = await connection.ReadSentAsync();
        stop.Cancel();

        ReplaceDeliveryResult result = await sending;
        Assert.Equal(ActivityDeliveryStatus.AcknowledgementLost, result.Status);
        Assert.Null(result.Result);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task SessionEndMarksSentButUnacknowledgedInventoryAsUncertain()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            PeerId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));

        ValueTask<ReplaceTargetInventoryDeliveryResult> querying =
            session.QueryAsync(LocalId, query, CancellationToken.None);
        _ = await connection.ReadSentAsync();
        stop.Cancel();

        ReplaceTargetInventoryDeliveryResult result = await querying;
        Assert.Equal(ActivityDeliveryStatus.AcknowledgementLost, result.Status);
        Assert.Null(result.Result);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task SessionStopDuringInventoryRegistrationCannotStrandPendingQuery()
    {
        var connection = new RegistrationRaceActivityControlConnection(
            LocalId,
            PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            PeerId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));
        Task<ReplaceTargetInventoryDeliveryResult> querying = Task.Run(async () =>
            await session.QueryAsync(LocalId, query, CancellationToken.None));
        await connection.ValidationReached.WaitAsync(TimeSpan.FromSeconds(1));

        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        connection.ReleaseValidation();
        Exception? completionFailure = await Record.ExceptionAsync(
            () => querying.WaitAsync(TimeSpan.FromMilliseconds(200)));
        await session.DisposeAsync();
        ReplaceTargetInventoryDeliveryResult result = await querying;

        Assert.Null(completionFailure);
        Assert.Equal(ActivityDeliveryStatus.NotDelivered, result.Status);
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
    public async Task UnsolicitedOrWrongCorrelationInventoryResultFaultsClosed()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        ReplaceTargetInventoryQuery pendingQuery = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            PeerId,
            ActivityKind.Parse("workspace.note/v1"),
            Now.AddSeconds(30));
        ValueTask<ReplaceTargetInventoryDeliveryResult> querying =
            session.QueryAsync(LocalId, pendingQuery, CancellationToken.None);
        _ = await connection.ReadSentAsync();
        ReplaceTargetInventoryQuery wrongQuery = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            PeerId,
            pendingQuery.IncomingKind,
            pendingQuery.Deadline);
        ReplaceTargetInventoryResult unsolicited =
            ReplaceTargetInventoryResult.Success(
                LocalId,
                wrongQuery,
                Now,
                [],
                isTruncated: false);

        connection.Receive(
            ActivityControlMessageCodec.CreateReplaceInventoryResult(
                new ProtocolVersion(1, 0),
                PeerId,
                unsolicited,
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
            (await querying).Status);
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
    public async Task ReplaceResultForDifferentTargetSnapshotFaultsClosed()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new RejectingReplacePeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        ReplaceActivityCommand command = CreateReplaceCommand(LocalId, PeerId);
        ValueTask<ReplaceDeliveryResult> sending = session.SendAsync(
            LocalId,
            command,
            CancellationToken.None);
        _ = await connection.ReadSentAsync();
        OperationReceipt receipt = OperationReceipt.Committed(
            command.Context.OperationId,
            command.Context.CorrelationId,
            OperationKind.Replace,
            LocalId,
            PeerId,
            command.IncomingDescriptor,
            Now);
        var forgedCapsule = new UndoCapsuleReference(
            UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            command.Context.OperationId,
            command.Context.CorrelationId,
            ActivityId.From(Guid.NewGuid()),
            command.ExpectedTargetRevision,
            command.ExpectedTargetDescriptorDigest,
            command.IncomingDescriptor.Id,
            command.IncomingDescriptor.DescriptorDigest,
            command.UndoExpiresAt);

        connection.Receive(ActivityControlMessageCodec.CreateReplaceResult(
            new ProtocolVersion(1, 0),
            PeerId,
            new ReplaceOperationResult(receipt, forgedCapsule),
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
        Assert.False(sourceHandler.IsReplaceEndpointAvailable);
        Assert.False(targetHandler.IsReplaceEndpointAvailable);
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

    [Fact]
    public async Task RealAuthenticatedLoopbackReplacesTargetWithBoundUndoReference()
    {
        using DeviceIdentityFixture identities = new();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var listenerEndpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                identities.Target,
                new TrustRecord(
                    identities.Source.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(Capability.ActivityReplace)),
                [new ProtocolVersion(1, 0)]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                listenerEndpoint,
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
        ActivityDescriptor originalDescriptor = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            identities.Target.DeviceId,
            "Target note",
            JsonSerializer.Serialize(new { text = "preserve target secret" }));
        ActivityInstance original = ActivityInstance.Active(
            originalDescriptor,
            ActivityPlacement.On(identities.Target.DeviceId, "desktop"),
            revision: 7);
        Assert.True(targetCatalog.TryAdd(original));
        using var replaceEndpoint = new ReplaceEndpoint(
            identities.Target.DeviceId,
            new FixedClock(Now),
            targetCatalog,
            new InMemoryOperationJournal(),
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]),
            new InMemoryReplaceStateStore(),
            new DeterministicUndoCapsuleIdSource(
            [
                UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            ]),
            NullReceiptSink.Instance);
        replaceEndpoint.SetPeerGrant(
            identities.Source.DeviceId,
            CapabilityGrant.Of(Capability.ActivityReplace));
        ActivityDescriptor incoming = ActivityDescriptor.Create(
            ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ActivityKind.Parse("workspace.note/v1"),
            identities.Source.DeviceId,
            "Incoming note",
            JsonSerializer.Serialize(new { text = "incoming secret" }));
        ReplaceActivityCommand command = ReplaceActivityCommand.Create(
            OperationContext.Create(
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Now.AddSeconds(30)),
            originalDescriptor.Id,
            original.Revision,
            originalDescriptor.DescriptorDigest,
            incoming,
            ActivityPlacement.On(identities.Target.DeviceId, "desktop"),
            Now.AddMinutes(10));
        await using var sourceHandler = new AuthenticatedActivitySessionHandler(
            source,
            replacePeer: null,
            new FixedTimeProvider(Now));
        await using var targetHandler = new AuthenticatedActivitySessionHandler(
            target,
            replaceEndpoint,
            new FixedTimeProvider(Now));
        Assert.False(sourceHandler.IsReplaceEndpointAvailable);
        Assert.True(targetHandler.IsReplaceEndpointAvailable);
        using var stop = new CancellationTokenSource();
        Task sourceRun = sourceHandler.RunAsync(sourceConnection, stop.Token).AsTask();
        Task targetRun = targetHandler.RunAsync(targetConnection, stop.Token).AsTask();
        Assert.True(sourceHandler.TryGetReplaceChannel(
            identities.Target.DeviceId,
            out IReplaceChannel? channel));
        Assert.NotNull(channel);

        ReplaceDeliveryResult delivered = await channel.SendAsync(
            identities.Source.DeviceId,
            command,
            CancellationToken.None);

        Assert.Equal(ActivityDeliveryStatus.Acknowledged, delivered.Status);
        Assert.Equal(OperationStatus.Committed, delivered.Result?.Receipt.Status);
        Assert.NotNull(delivered.Result?.UndoCapsule);
        Assert.False(targetCatalog.TryGet(originalDescriptor.Id, out _));
        Assert.True(targetCatalog.TryGet(incoming.Id, out _));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => targetRun);
    }

    [Fact]
    public async Task RealAuthenticatedLoopbackQueriesPayloadFreeReplaceInventory()
    {
        using DeviceIdentityFixture identities = new();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var listenerEndpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                identities.Target,
                new TrustRecord(
                    identities.Source.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(Capability.ActivityReplace)),
                [new ProtocolVersion(1, 0)]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                listenerEndpoint,
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
        ActivityDescriptor targetDescriptor = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            identities.Target.DeviceId,
            "Remote target",
            JsonSerializer.Serialize(new { text = "REMOTE-INVENTORY-PAYLOAD-CANARY" }));
        Assert.True(targetCatalog.TryAdd(ActivityInstance.Active(
            targetDescriptor,
            ActivityPlacement.On(identities.Target.DeviceId, "desktop"),
            revision: 7)));
        var inventoryEndpoint = new ReplaceTargetInventoryEndpoint(
            identities.Target.DeviceId,
            new FixedClock(Now),
            targetCatalog,
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]));
        inventoryEndpoint.SetPeerGrant(
            identities.Source.DeviceId,
            CapabilityGrant.Of(Capability.ActivityReplace));
        await using var sourceHandler = new AuthenticatedActivitySessionHandler(
            source,
            new FixedTimeProvider(Now));
        await using var targetHandler = new AuthenticatedActivitySessionHandler(
            target,
            replacePeer: null,
            replaceInventoryPeer: inventoryEndpoint,
            new FixedTimeProvider(Now));
        Assert.False(sourceHandler.IsReplaceEndpointAvailable);
        Assert.False(targetHandler.IsReplaceEndpointAvailable);
        using var stop = new CancellationTokenSource();
        Task sourceRun = sourceHandler.RunAsync(sourceConnection, stop.Token).AsTask();
        Task targetRun = targetHandler.RunAsync(targetConnection, stop.Token).AsTask();
        Assert.True(sourceHandler.TryGetReplaceInventoryChannel(
            identities.Target.DeviceId,
            out IReplaceTargetInventoryChannel? channel));
        Assert.NotNull(channel);
        ReplaceTargetInventoryQuery query = ReplaceTargetInventoryQuery.Create(
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            identities.Target.DeviceId,
            targetDescriptor.Kind,
            Now.AddSeconds(30));

        ReplaceTargetInventoryDeliveryResult delivered = await channel.QueryAsync(
            identities.Source.DeviceId,
            query,
            CancellationToken.None);

        Assert.Equal(ActivityDeliveryStatus.Acknowledged, delivered.Status);
        Assert.True(delivered.Result?.IsSuccess);
        Assert.Equal(
            targetDescriptor.Id,
            Assert.Single(delivered.Result!.Targets).ActivityId);
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

    private static ReplaceActivityCommand CreateReplaceCommand(
        DeviceId sourceId,
        DeviceId targetId)
    {
        ActivityDescriptor target = ActivityDescriptor.Create(
            ActivityId.From(Guid.NewGuid()),
            ActivityKind.Parse("workspace.note/v1"),
            targetId,
            "Target note",
            JsonSerializer.Serialize(new { text = "preserve target secret" }));
        ActivityDescriptor incoming = ActivityDescriptor.Create(
            ActivityId.From(Guid.NewGuid()),
            ActivityKind.Parse("workspace.note/v1"),
            sourceId,
            "Incoming note",
            JsonSerializer.Serialize(new { text = "incoming secret" }));
        return ReplaceActivityCommand.Create(
            OperationContext.Create(
                OperationId.From(Guid.NewGuid()),
                CorrelationId.From(Guid.NewGuid()),
                Now.AddSeconds(30)),
            target.Id,
            expectedTargetRevision: 7,
            target.DescriptorDigest,
            incoming,
            ActivityPlacement.On(targetId, "desktop"),
            Now.AddMinutes(10));
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

    private sealed class RejectingReplacePeer(DeviceId deviceId) : IReplacePeer
    {
        public DeviceId DeviceId { get; } = deviceId;

        public ValueTask<ReplaceOperationResult> ReplaceAsync(
            DeviceId senderDeviceId,
            ReplaceActivityCommand command,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<ReplaceOperationResult>(
                new InvalidOperationException("No inbound Replace was expected."));
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

    private sealed class RegistrationRaceActivityControlConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) : IActivityControlConnection
    {
        private readonly TaskCompletionSource releaseValidation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource validationReached = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId
        {
            get
            {
                validationReached.TrySetResult();
                releaseValidation.Task.GetAwaiter().GetResult();
                return peerDeviceId;
            }
        }

        public ProtocolVersion ProtocolVersion { get; } = new(1, 0);

        public Task ValidationReached => validationReached.Task;

        public async ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("An infinite delay unexpectedly completed.");
        }

        public void ReleaseValidation() => releaseValidation.TrySetResult();

        public ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class CancellationEndsWithEofActivityControlConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) : IActivityControlConnection
    {
        private readonly TaskCompletionSource readStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } = new(1, 0);

        public Task ReadStarted => readStarted.Task;

        public async ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            readStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new EndOfStreamException(
                    "The peer closed while the local session was stopping.");
            }

            throw new InvalidOperationException("An infinite delay unexpectedly completed.");
        }

        public ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class ImmediateEofActivityControlConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) : IActivityControlConnection
    {
        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } = new(1, 0);

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ControlMessage>(
                new EndOfStreamException("The peer closed the control channel."));

        public ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
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
