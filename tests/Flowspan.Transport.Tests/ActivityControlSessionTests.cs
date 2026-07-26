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
            PeerId,
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
    public async Task OutboundSceneSourceLookupWaitsForExactlyBoundResult()
    {
        var connection = new FakeActivityControlConnection(
            LocalId,
            PeerId,
            ProtocolFeatures.SceneApplyMinimumVersion);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        SceneSourceLookupQuery query = SceneSourceLookupQuery.Create(
            OperationContext.Create(
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Now.AddSeconds(30)),
            PeerId,
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            index: 0);

        ValueTask<SceneSourceLookupDeliveryResult> querying =
            session.QuerySourceAsync(LocalId, query, CancellationToken.None);
        ControlMessage request = await connection.ReadSentAsync();
        SceneSourceSelection source = SceneSourceSelection.Create(
            index: 0,
            query.ActivityId,
            revision: 7,
            descriptorDigest: new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(PeerId, "desktop"));
        SceneSourceLookup expected = SceneSourceLookup.FromObservation(
            index: 0,
            query.ActivityId,
            [source],
            isComplete: true);
        connection.Receive(SceneControlMessageCodec.CreateSourceLookupResult(
            request.Version,
            PeerId,
            LocalId,
            query,
            expected,
            Now.AddSeconds(1)));

        SceneSourceLookupDeliveryResult delivered = await querying;

        Assert.Equal(SceneControlDeliveryStatus.Acknowledged, delivered.Status);
        Assert.Equal(expected, delivered.Result);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task OutboundSceneExactSlotWaitsForExactlyBoundResult()
    {
        var connection = new FakeActivityControlConnection(
            LocalId,
            PeerId,
            ProtocolFeatures.SceneApplyMinimumVersion);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        ActivityId activityId =
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SceneSourceSelection source = SceneSourceSelection.Create(
            index: 0,
            activityId,
            revision: 7,
            descriptorDigest: new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(LocalId, "desktop"));
        SceneExactSlotQuery query = SceneExactSlotQuery.Create(
            OperationContext.Create(
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Now.AddSeconds(30)),
            SceneActivityPlan.Place(
                activityId,
                ActivityPlacement.On(PeerId, "focus"),
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.RequireEmpty),
            source);
        SceneExactSlotInspection expected = SceneExactSlotInspection.Observed(
            SceneSlotOccupancy.Empty);

        ValueTask<SceneExactSlotDeliveryResult> inspecting =
            session.InspectSlotAsync(LocalId, query, CancellationToken.None);
        ControlMessage request = await connection.ReadSentAsync();
        connection.Receive(SceneControlMessageCodec.CreateExactSlotResult(
            request.Version,
            PeerId,
            LocalId,
            query,
            expected,
            Now.AddSeconds(1)));

        SceneExactSlotDeliveryResult delivered = await inspecting;

        Assert.Equal(SceneControlDeliveryStatus.Acknowledged, delivered.Status);
        Assert.Equal(expected, delivered.Result);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task OutboundRemoteSceneChildWaitsForExactlyBoundResult()
    {
        var connection = new FakeActivityControlConnection(
            LocalId,
            PeerId,
            ProtocolFeatures.SceneApplyMinimumVersion);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        DeviceId targetId =
            DeviceId.Parse("33333333-3333-3333-3333-333333333333");
        ActivityId activityId =
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        OperationId childOperationId =
            OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        CorrelationId childCorrelationId =
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        SceneSourceSelection source = SceneSourceSelection.Create(
            index: 0,
            activityId,
            revision: 7,
            descriptorDigest: new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(PeerId, "desktop"));
        SceneRemoteChildInstruction instruction =
            SceneRemoteChildInstruction.Create(
                LocalId,
                SceneId.Parse("abababab-abab-abab-abab-abababababab"),
                sceneRevision: 5,
                sceneDigest: new string('C', 64),
                previewFingerprint: new string('D', 64),
                OperationId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                CorrelationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                acceptedAt: Now,
                SceneApplyItemPreview.TransferToEmpty(
                    SceneActivityPlan.Place(
                        activityId,
                        ActivityPlacement.On(targetId, "focus"),
                        SceneSourceDisposition.PreserveSource,
                        SceneConflictPolicy.RequireEmpty),
                    source,
                    childOperationId,
                    childCorrelationId));
        SceneActivityOperationResult expected = SceneActivityOperationResult.Create(
            OperationReceipt.FromRecordedResult(
                childOperationId,
                childCorrelationId,
                OperationKind.Handoff,
                OperationStatus.Committed,
                PeerId,
                targetId,
                activityId,
                source.Kind,
                source.DescriptorDigest,
                Now.AddSeconds(1),
                FailureCode.None),
            undoCapsule: null);

        ValueTask<SceneChildDeliveryResult> executing = session.ExecuteChildAsync(
            LocalId,
            instruction,
            CancellationToken.None);
        ControlMessage request = await connection.ReadSentAsync();
        connection.Receive(SceneControlMessageCodec.CreateChildResult(
            request.Version,
            PeerId,
            LocalId,
            instruction,
            expected,
            Now.AddSeconds(1)));

        SceneChildDeliveryResult delivered = await executing;

        Assert.Equal(SceneControlDeliveryStatus.Acknowledged, delivered.Status);
        Assert.Equal(expected, delivered.Result);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task OutboundRemoteSceneUndoWaitsForExactlyBoundResult()
    {
        var connection = new FakeActivityControlConnection(
            LocalId,
            PeerId,
            ProtocolFeatures.SceneApplyMinimumVersion);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        SceneUndoReplaceInstruction instruction = CreateSceneUndoInstruction(
            LocalId,
            PeerId,
            OperationId.Parse("18181818-1818-1818-1818-181818181818"),
            CorrelationId.Parse("19191919-1919-1919-1919-191919191919"));
        UndoReplaceResult expected = UndoReplaceResult.Committed(
            instruction.Context,
            instruction.Capsule.Id,
            Now.AddSeconds(1));

        ValueTask<SceneUndoReplaceDeliveryResult> undoing =
            session.UndoReplaceAsync(
                LocalId,
                instruction,
                CancellationToken.None);
        ControlMessage request = await connection.ReadSentAsync();
        connection.Receive(SceneControlMessageCodec.CreateUndoReplaceResult(
            request.Version,
            PeerId,
            LocalId,
            instruction,
            expected,
            Now.AddSeconds(1)));

        SceneUndoReplaceDeliveryResult delivered = await undoing;

        Assert.Equal(SceneControlDeliveryStatus.Acknowledged, delivered.Status);
        Assert.Equal(expected, delivered.Result);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task InboundSceneSourceLookupRunsOnAuthenticatedLocalPeer()
    {
        var connection = new FakeActivityControlConnection(
            LocalId,
            PeerId,
            ProtocolFeatures.SceneApplyMinimumVersion);
        var scenePeer = new RecordingSceneControlPeer(LocalId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            scenePeer,
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        SceneSourceLookupQuery query = SceneSourceLookupQuery.Create(
            OperationContext.Create(
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Now.AddSeconds(30)),
            LocalId,
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            index: 0);
        connection.Receive(SceneControlMessageCodec.CreateSourceLookupQuery(
            ProtocolFeatures.SceneApplyMinimumVersion,
            PeerId,
            query,
            Now));

        Task run = session.RunAsync(stop.Token).AsTask();
        ControlMessage response = await connection.ReadSentAsync();
        SceneSourceLookup decoded =
            SceneControlMessageCodec.DecodeSourceLookupResult(
                response,
                PeerId,
                query);

        Assert.Equal(scenePeer.Result, decoded);
        Assert.Equal(PeerId, scenePeer.LastCoordinatorDeviceId);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task InboundSceneExactSlotRunsOnAuthenticatedLocalPeer()
    {
        var connection = new FakeActivityControlConnection(
            LocalId,
            PeerId,
            ProtocolFeatures.SceneApplyMinimumVersion);
        var scenePeer = new RecordingSceneControlPeer(LocalId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            scenePeer,
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        ActivityId activityId =
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SceneExactSlotQuery query = SceneExactSlotQuery.Create(
            OperationContext.Create(
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Now.AddSeconds(30)),
            SceneActivityPlan.Place(
                activityId,
                ActivityPlacement.On(LocalId, "focus"),
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.RequireEmpty),
            SceneSourceSelection.Create(
                index: 0,
                activityId,
                revision: 7,
                descriptorDigest: new string('A', 64),
                ActivityKind.Parse("workspace.note/v1"),
                ActivityPlacement.On(PeerId, "desktop")));
        connection.Receive(SceneControlMessageCodec.CreateExactSlotQuery(
            ProtocolFeatures.SceneApplyMinimumVersion,
            PeerId,
            query,
            Now));

        Task run = session.RunAsync(stop.Token).AsTask();
        ControlMessage response = await connection.ReadSentAsync();
        SceneExactSlotInspection decoded =
            SceneControlMessageCodec.DecodeExactSlotResult(
                response,
                PeerId,
                query);

        Assert.Equal(scenePeer.SlotResult, decoded);
        Assert.Equal(PeerId, scenePeer.LastCoordinatorDeviceId);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task InboundRemoteSceneChildRunsOnAuthenticatedSourcePeer()
    {
        var connection = new FakeActivityControlConnection(
            LocalId,
            PeerId,
            ProtocolFeatures.SceneApplyMinimumVersion);
        var scenePeer = new RecordingSceneControlPeer(LocalId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            scenePeer,
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        DeviceId targetId =
            DeviceId.Parse("33333333-3333-3333-3333-333333333333");
        ActivityId activityId =
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SceneSourceSelection source = SceneSourceSelection.Create(
            index: 0,
            activityId,
            revision: 7,
            descriptorDigest: new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(LocalId, "desktop"));
        SceneRemoteChildInstruction instruction =
            SceneRemoteChildInstruction.Create(
                PeerId,
                SceneId.Parse("abababab-abab-abab-abab-abababababab"),
                sceneRevision: 5,
                sceneDigest: new string('C', 64),
                previewFingerprint: new string('D', 64),
                OperationId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                CorrelationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                acceptedAt: Now,
                SceneApplyItemPreview.TransferToEmpty(
                    SceneActivityPlan.Place(
                        activityId,
                        ActivityPlacement.On(targetId, "focus"),
                        SceneSourceDisposition.PreserveSource,
                        SceneConflictPolicy.RequireEmpty),
                    source,
                    OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")));
        connection.Receive(SceneControlMessageCodec.CreateChildInstruction(
            ProtocolFeatures.SceneApplyMinimumVersion,
            PeerId,
            instruction,
            Now));

        Task run = session.RunAsync(stop.Token).AsTask();
        ControlMessage response = await connection.ReadSentAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(1));
        SceneActivityOperationResult decoded =
            SceneControlMessageCodec.DecodeChildResult(
                response,
                PeerId,
                instruction);

        Assert.Equal(scenePeer.ChildResult, decoded);
        Assert.Equal(instruction, scenePeer.LastInstruction);
        Assert.Equal(PeerId, scenePeer.LastCoordinatorDeviceId);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task InboundRemoteSceneUndoRunsOnAuthenticatedTargetPeer()
    {
        var connection = new FakeActivityControlConnection(
            LocalId,
            PeerId,
            ProtocolFeatures.SceneApplyMinimumVersion);
        var scenePeer = new RecordingSceneControlPeer(LocalId);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            scenePeer,
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        SceneUndoReplaceInstruction instruction = CreateSceneUndoInstruction(
            PeerId,
            LocalId,
            OperationId.Parse("18181818-1818-1818-1818-181818181818"),
            CorrelationId.Parse("19191919-1919-1919-1919-191919191919"));
        connection.Receive(
            SceneControlMessageCodec.CreateUndoReplaceInstruction(
                ProtocolFeatures.SceneApplyMinimumVersion,
                PeerId,
                instruction,
                Now));

        Task run = session.RunAsync(stop.Token).AsTask();
        ControlMessage response = await connection.ReadSentAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(1));
        UndoReplaceResult decoded =
            SceneControlMessageCodec.DecodeUndoReplaceResult(
                response,
                PeerId,
                instruction);

        Assert.Equal(scenePeer.UndoResult, decoded);
        Assert.Equal(instruction, scenePeer.LastUndoInstruction);
        Assert.Equal(PeerId, scenePeer.LastCoordinatorDeviceId);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task InboundRemoteSceneUndoWithoutReplaceEndpointStaysDeliverable()
    {
        var connection = new FakeActivityControlConnection(
            LocalId,
            PeerId,
            ProtocolFeatures.SceneApplyMinimumVersion);
        var catalog = new InMemoryActivityCatalog();
        FlowspanNode node = CreateNode(LocalId, "Local", catalog);
        var preflight = new SceneApplyPreflightEndpoint(
            LocalId,
            new FixedClock(Now),
            catalog,
            new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]),
            NeverSceneUndoAvailable.Instance);
        var operationEndpoint = new SceneActivityOperationEndpoint(
            node,
            preflight,
            clock: new FixedClock(Now));
        operationEndpoint.SetPeerGrant(
            PeerId,
            CapabilityGrant.Of(Capability.SceneApply));
        var scenePeer = new SceneControlPeer(
            new FixedClock(Now),
            operationEndpoint,
            new RejectingSceneOperationPort(),
            new InMemorySceneRemoteChildJournal());
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            scenePeer,
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        SceneUndoReplaceInstruction instruction = CreateSceneUndoInstruction(
            PeerId,
            LocalId,
            OperationId.Parse("18181818-1818-1818-1818-181818181818"),
            CorrelationId.Parse("19191919-1919-1919-1919-191919191919"));
        connection.Receive(
            SceneControlMessageCodec.CreateUndoReplaceInstruction(
                ProtocolFeatures.SceneApplyMinimumVersion,
                PeerId,
                instruction,
                Now));

        Task run = session.RunAsync(stop.Token).AsTask();
        ControlMessage response = await connection.ReadSentAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(1));
        UndoReplaceResult decoded =
            SceneControlMessageCodec.DecodeUndoReplaceResult(
                response,
                PeerId,
                instruction);

        Assert.Equal(OperationStatus.Failed, decoded.Status);
        Assert.Equal(FailureCode.UndoUnavailable, decoded.FailureCode);
        Assert.Equal(Now, decoded.OccurredAt);
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
    public async Task SessionEndMarksAllSentSceneRequestsAsUncertain()
    {
        var connection = new FakeActivityControlConnection(
            LocalId,
            PeerId,
            ProtocolFeatures.SceneApplyMinimumVersion);
        var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        SceneSourceLookupQuery sourceQuery = SceneSourceLookupQuery.Create(
            OperationContext.Create(
                OperationId.Parse("10101010-1010-1010-1010-101010101010"),
                CorrelationId.Parse("11111111-1111-1111-1111-111111111111"),
                Now.AddSeconds(30)),
            PeerId,
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            index: 0);
        SceneSourceSelection source = SceneSourceSelection.Create(
            index: 1,
            ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            revision: 7,
            descriptorDigest: new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(LocalId, "desktop"));
        SceneExactSlotQuery slotQuery = SceneExactSlotQuery.Create(
            OperationContext.Create(
                OperationId.Parse("20202020-2020-2020-2020-202020202020"),
                CorrelationId.Parse("22222222-2222-2222-2222-222222222222"),
                Now.AddSeconds(30)),
            SceneActivityPlan.Place(
                source.ActivityId,
                ActivityPlacement.On(PeerId, "focus"),
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.RequireEmpty),
            source);
        SceneApplyItemPreview childItem =
            SceneApplyItemPreview.TransferToEmpty(
                SceneActivityPlan.Place(
                    ActivityId.Parse(
                        "cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    ActivityPlacement.On(LocalId, "focus"),
                    SceneSourceDisposition.PreserveSource,
                    SceneConflictPolicy.RequireEmpty),
                SceneSourceSelection.Create(
                    index: 2,
                    ActivityId.Parse(
                        "cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    revision: 9,
                    descriptorDigest: new string('B', 64),
                    ActivityKind.Parse("workspace.note/v1"),
                    ActivityPlacement.On(PeerId, "desktop")),
                OperationId.Parse("30303030-3030-3030-3030-303030303030"),
                CorrelationId.Parse(
                    "33333333-3333-3333-3333-333333333333"));
        SceneRemoteChildInstruction child =
            SceneRemoteChildInstruction.Create(
                LocalId,
                SceneId.Parse("40404040-4040-4040-4040-404040404040"),
                sceneRevision: 5,
                sceneDigest: new string('C', 64),
                previewFingerprint: new string('D', 64),
                OperationId.Parse("50505050-5050-5050-5050-505050505050"),
                CorrelationId.Parse("60606060-6060-6060-6060-606060606060"),
                acceptedAt: Now,
                childItem);
        SceneUndoReplaceInstruction undo = CreateSceneUndoInstruction(
            LocalId,
            PeerId,
            OperationId.Parse("70707070-7070-7070-7070-707070707070"),
            CorrelationId.Parse("70717171-7171-7171-7171-717171717171"));

        ValueTask<SceneSourceLookupDeliveryResult> sourceSending =
            session.QuerySourceAsync(LocalId, sourceQuery, CancellationToken.None);
        ValueTask<SceneExactSlotDeliveryResult> slotSending =
            session.InspectSlotAsync(LocalId, slotQuery, CancellationToken.None);
        ValueTask<SceneChildDeliveryResult> childSending =
            session.ExecuteChildAsync(LocalId, child, CancellationToken.None);
        ValueTask<SceneUndoReplaceDeliveryResult> undoSending =
            session.UndoReplaceAsync(LocalId, undo, CancellationToken.None);
        await connection.ReadSentAsync();
        await connection.ReadSentAsync();
        await connection.ReadSentAsync();
        await connection.ReadSentAsync();

        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.Equal(
            SceneControlDeliveryStatus.AcknowledgementLost,
            (await sourceSending).Status);
        Assert.Equal(
            SceneControlDeliveryStatus.AcknowledgementLost,
            (await slotSending).Status);
        Assert.Equal(
            SceneControlDeliveryStatus.AcknowledgementLost,
            (await childSending).Status);
        Assert.Equal(
            SceneControlDeliveryStatus.AcknowledgementLost,
            (await undoSending).Status);
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
            PeerId,
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

    [Fact]
    public async Task AuthenticatedCoordinatorRoutesRemoteReplaceAndStableCompensation()
    {
        DeviceId coordinatorId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId sourceId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        DeviceId targetId = DeviceId.Parse(
            "33333333-3333-3333-3333-333333333333");
        using DeviceIdentity coordinatorIdentity =
            DeviceIdentity.Generate(coordinatorId, "Coordinator");
        using DeviceIdentity sourceIdentity =
            DeviceIdentity.Generate(sourceId, "Source");
        using DeviceIdentity targetIdentity =
            DeviceIdentity.Generate(targetId, "Target");
        ProtocolVersion version = ProtocolFeatures.SceneApplyMinimumVersion;

        using var targetListener = new TcpListener(IPAddress.Loopback, 0);
        targetListener.Start(backlog: 1);
        var targetEndpoint = Assert.IsType<IPEndPoint>(
            targetListener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> acceptingTarget =
            AuthenticatedTcpControlConnection.AcceptAsync(
                targetListener,
                targetIdentity,
                new TrustRecord(
                    sourceIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(
                        Capability.SceneApply,
                        Capability.ActivityOffer)),
                [version]).AsTask();
        await using AuthenticatedTcpControlConnection sourceToTarget =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                targetEndpoint,
                sourceIdentity,
                new TrustRecord(
                    targetIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(
                        Capability.SceneApply,
                        Capability.ActivityReceive)),
                [version]);
        await using AuthenticatedTcpControlConnection targetToSource =
            await acceptingTarget;

        using var sourceListener = new TcpListener(IPAddress.Loopback, 0);
        sourceListener.Start(backlog: 1);
        var sourceEndpoint = Assert.IsType<IPEndPoint>(
            sourceListener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> acceptingSource =
            AuthenticatedTcpControlConnection.AcceptAsync(
                sourceListener,
                sourceIdentity,
                new TrustRecord(
                    coordinatorIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(Capability.SceneApply)),
                [version]).AsTask();
        await using AuthenticatedTcpControlConnection coordinatorToSource =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                sourceEndpoint,
                coordinatorIdentity,
                new TrustRecord(
                    sourceIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(Capability.SceneApply)),
                [version]);
        await using AuthenticatedTcpControlConnection sourceToCoordinator =
            await acceptingSource;

        using var coordinatorTargetListener = new TcpListener(
            IPAddress.Loopback,
            0);
        coordinatorTargetListener.Start(backlog: 1);
        var coordinatorTargetEndpoint = Assert.IsType<IPEndPoint>(
            coordinatorTargetListener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> acceptingCoordinatorAtTarget =
            AuthenticatedTcpControlConnection.AcceptAsync(
                coordinatorTargetListener,
                targetIdentity,
                new TrustRecord(
                    coordinatorIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(Capability.SceneApply)),
                [version]).AsTask();
        await using AuthenticatedTcpControlConnection coordinatorToTarget =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                coordinatorTargetEndpoint,
                coordinatorIdentity,
                new TrustRecord(
                    targetIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(Capability.SceneApply)),
                [version]);
        await using AuthenticatedTcpControlConnection targetToCoordinator =
            await acceptingCoordinatorAtTarget;

        var coordinatorCatalog = new InMemoryActivityCatalog();
        var sourceCatalog = new InMemoryActivityCatalog();
        var targetCatalog = new InMemoryActivityCatalog();
        FlowspanNode coordinatorNode = CreateNode(
            coordinatorId,
            "Coordinator",
            coordinatorCatalog);
        FlowspanNode sourceNode = CreateNode(
            sourceId,
            "Source",
            sourceCatalog);
        FlowspanNode targetNode = CreateNode(
            targetId,
            "Target",
            targetCatalog);
        ActivityInstance sourceActivity = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ActivityKind.Parse("workspace.note/v1"),
                sourceId,
                "source-title-canary",
                JsonSerializer.Serialize(new
                {
                    text = "END-TO-END-SOURCE-PAYLOAD-CANARY",
                })),
            ActivityPlacement.On(sourceId, "desktop"),
            revision: 7);
        Assert.True(sourceNode.AddLocalActivity(sourceActivity));
        ActivityInstance targetActivity = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                ActivityKind.Parse("workspace.note/v1"),
                targetId,
                "target-title-canary",
                JsonSerializer.Serialize(new
                {
                    text = "END-TO-END-TARGET-PAYLOAD-CANARY",
                })),
            ActivityPlacement.On(targetId, "focus"),
            revision: 9);
        Assert.True(targetNode.AddLocalActivity(targetActivity));

        var adapters = new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]);
        var sourcePreflight = new SceneApplyPreflightEndpoint(
            sourceId,
            new FixedClock(Now),
            sourceCatalog,
            adapters,
            NeverSceneUndoAvailable.Instance);
        var coordinatorPreflight = new SceneApplyPreflightEndpoint(
            coordinatorId,
            new FixedClock(Now),
            coordinatorCatalog,
            adapters,
            NeverSceneUndoAvailable.Instance);
        var targetPreflight = new SceneApplyPreflightEndpoint(
            targetId,
            new FixedClock(Now),
            targetCatalog,
            adapters,
            AlwaysSceneUndoAvailable.Instance);
        var sourceOperationEndpoint = new SceneActivityOperationEndpoint(
            sourceNode,
            sourcePreflight,
            clock: new FixedClock(Now));
        var targetReplaceState = new InMemoryReplaceStateStore();
        using var targetReplaceEndpoint = new ReplaceEndpoint(
            targetId,
            new FixedClock(Now),
            targetCatalog,
            new InMemoryOperationJournal(),
            adapters,
            targetReplaceState,
            new DeterministicUndoCapsuleIdSource(
            [
                UndoCapsuleId.Parse(
                    "12121212-1212-1212-1212-121212121212"),
            ]),
            NullReceiptSink.Instance);
        var targetOperationEndpoint = new SceneActivityOperationEndpoint(
            targetNode,
            targetPreflight,
            targetReplaceEndpoint,
            new FixedClock(Now));
        var coordinatorOperationEndpoint = new SceneActivityOperationEndpoint(
            coordinatorNode,
            coordinatorPreflight,
            clock: new FixedClock(Now));
        sourceOperationEndpoint.SetPeerGrant(
            coordinatorId,
            CapabilityGrant.Of(Capability.SceneApply));
        sourceOperationEndpoint.SetPeerGrant(
            targetId,
            CapabilityGrant.Of(Capability.ActivityReceive));
        targetOperationEndpoint.SetPeerGrant(
            sourceId,
            CapabilityGrant.Of(Capability.SceneApply));
        targetOperationEndpoint.SetPeerGrant(
            coordinatorId,
            CapabilityGrant.Of(Capability.SceneApply));
        targetReplaceEndpoint.SetPeerGrant(
            sourceId,
            CapabilityGrant.Of(Capability.ActivityReplace));
        targetNode.SetPeerGrant(
            sourceId,
            CapabilityGrant.Of(Capability.ActivityOffer));

        var sourceRoutes = new ForwardingSceneOperationRouteDirectory();
        var sourceScenePeer = new SceneControlPeer(
            new FixedClock(Now),
            sourceOperationEndpoint,
            new RoutedSceneActivityOperationPort(
                new FixedClock(Now),
                sourceOperationEndpoint,
                sourceRoutes),
            new InMemorySceneRemoteChildJournal());
        var targetScenePeer = new SceneControlPeer(
            new FixedClock(Now),
            targetOperationEndpoint,
            new RejectingSceneOperationPort(),
            new InMemorySceneRemoteChildJournal());
        await using var coordinatorHandler =
            new AuthenticatedActivitySessionHandler(
                coordinatorNode,
                new FixedTimeProvider(Now));
        await using var sourceHandler =
            new AuthenticatedActivitySessionHandler(
                sourceNode,
                sourceScenePeer,
                new FixedTimeProvider(Now));
        sourceRoutes.Inner = sourceHandler;
        await using var targetHandler =
            new AuthenticatedActivitySessionHandler(
                targetNode,
                targetReplaceEndpoint,
                replaceInventoryPeer: null,
                swapPeer: null,
                timeProvider: new FixedTimeProvider(Now),
                scenePeer: targetScenePeer);
        using var stop = new CancellationTokenSource();
        Task sourceTargetRun = sourceHandler.RunAsync(
            sourceToTarget,
            stop.Token).AsTask();
        Task targetSourceRun = targetHandler.RunAsync(
            targetToSource,
            stop.Token).AsTask();
        Task coordinatorSourceRun = coordinatorHandler.RunAsync(
            coordinatorToSource,
            stop.Token).AsTask();
        Task sourceCoordinatorRun = sourceHandler.RunAsync(
            sourceToCoordinator,
            stop.Token).AsTask();
        Task coordinatorTargetRun = coordinatorHandler.RunAsync(
            coordinatorToTarget,
            stop.Token).AsTask();
        Task targetCoordinatorRun = targetHandler.RunAsync(
            targetToCoordinator,
            stop.Token).AsTask();
        Assert.True(sourceHandler.TryGetChannel(
            targetId,
            out IActivityChannel? _));
        var planner = new SceneApplyPlanner(
            new FixedClock(Now),
            new RoutedSceneApplyPreflightPort(
                coordinatorId,
                coordinatorPreflight,
                coordinatorHandler),
            new DeterministicSceneApplyIdSource(
                [
                    OperationId.Parse(
                        "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                    OperationId.Parse(
                        "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                ],
                [
                    CorrelationId.Parse(
                        "ffffffff-ffff-ffff-ffff-ffffffffffff"),
                    CorrelationId.Parse(
                        "cccccccc-cccc-cccc-cccc-cccccccccccc"),
                ]));
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("abababab-abab-abab-abab-abababababab"),
            "scene-title-canary",
            [
                SceneActivityPlan.Place(
                    sourceActivity.Descriptor.Id,
                    ActivityPlacement.On(targetId, "focus"),
                    SceneSourceDisposition.PreserveSource,
                    SceneConflictPolicy.ReplaceWithUndo),
            ]);
        SceneApplyPreview preview = await planner.PreviewAsync(
            scene,
            [],
            observedGroupRevision: null,
            CancellationToken.None);
        SceneApplyItemPreview item = Assert.Single(preview.Items);
        Assert.Equal(SceneApplyAction.Replace, item.Action);
        Assert.Equal(sourceId, item.Source?.DeviceId);
        Assert.Equal(targetActivity.Descriptor.Id, item.ReplaceTarget?.ActivityId);
        var localPort = new RoutedSceneActivityOperationPort(
            new FixedClock(Now),
            coordinatorId,
            coordinatorOperationEndpoint,
            coordinatorHandler);
        var operationPort = new CoordinatorSceneActivityOperationPort(
            new FixedClock(Now),
            coordinatorId,
            localPort,
            coordinatorHandler);
        var coordinator = new SceneApplyCoordinator(
            new FixedClock(Now),
            new InMemorySceneApplyJournal(),
            operationPort);

        SceneApplyExecutionResult execution = await coordinator.ApplyAsync(
            scene,
            preview,
            SceneApplyApproval.Create(
                preview.Fingerprint,
                preview.RequiredReplaceConfirmations),
            CancellationToken.None);

        SceneApplyResult result = Assert.IsType<SceneApplyResult>(
            execution.Result);
        Assert.Equal(SceneApplyOverallStatus.Completed, result.Status);
        Assert.Equal(
            SceneApplyItemOutcome.Committed,
            Assert.Single(result.Items).Outcome);
        UndoCapsuleReference capsule = Assert.IsType<UndoCapsuleReference>(
            Assert.Single(result.Items).UndoCapsule);
        Assert.Equal(targetId, capsule.TargetDeviceId);
        Assert.Equal(targetActivity.Descriptor.Id, capsule.TargetActivityId);
        Assert.True(sourceCatalog.TryGet(
            sourceActivity.Descriptor.Id,
            out ActivityInstance? preserved));
        Assert.Equal(ActivityLifecycle.Active, preserved.Lifecycle);
        Assert.True(targetCatalog.TryGet(
            sourceActivity.Descriptor.Id,
            out ActivityInstance? received));
        Assert.Contains(
            "END-TO-END-SOURCE-PAYLOAD-CANARY",
            received.Descriptor.PayloadJson,
            StringComparison.Ordinal);
        Assert.False(targetCatalog.TryGet(targetActivity.Descriptor.Id, out _));
        Assert.Empty(coordinatorCatalog.GetSnapshot());

        var compensator = new SceneApplyCompensator(
            new FixedClock(Now),
            operationPort);
        SceneCompensationResult compensation = await compensator.CompensateAsync(
            result,
            CancellationToken.None);
        SceneCompensationResult replay = await compensator.CompensateAsync(
            result,
            CancellationToken.None);

        Assert.Equal(SceneCompensationStatus.Completed, compensation.Status);
        Assert.Equal(compensation.Status, replay.Status);
        Assert.True(compensation.Items.SequenceEqual(replay.Items));
        Assert.Equal(
            SceneCompensationItemOutcome.Committed,
            Assert.Single(compensation.Items).Outcome);
        Assert.True(targetCatalog.TryGet(
            targetActivity.Descriptor.Id,
            out ActivityInstance? restored));
        Assert.Contains(
            "END-TO-END-TARGET-PAYLOAD-CANARY",
            restored.Descriptor.PayloadJson,
            StringComparison.Ordinal);
        Assert.False(targetCatalog.TryGet(sourceActivity.Descriptor.Id, out _));
        Assert.True(sourceCatalog.TryGet(sourceActivity.Descriptor.Id, out _));

        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sourceTargetRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => targetSourceRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinatorSourceRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sourceCoordinatorRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinatorTargetRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => targetCoordinatorRun);
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

    private static SceneUndoReplaceInstruction CreateSceneUndoInstruction(
        DeviceId coordinatorId,
        DeviceId targetId,
        OperationId operationId,
        CorrelationId correlationId)
    {
        DateTimeOffset expiresAt = Now.AddMinutes(5);
        return SceneUndoReplaceInstruction.Create(
            coordinatorId,
            new UndoCapsuleReference(
                UndoCapsuleId.Parse("12121212-1212-1212-1212-121212121212"),
                OperationId.Parse("13131313-1313-1313-1313-131313131313"),
                CorrelationId.Parse("14141414-1414-1414-1414-141414141414"),
                targetId,
                ActivityId.Parse("15151515-1515-1515-1515-151515151515"),
                ExpectedTargetRevision: 9,
                TargetDescriptorDigest: new string('E', 64),
                IncomingActivityId: ActivityId.Parse(
                    "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                IncomingDescriptorDigest: new string('A', 64),
                expiresAt),
            OperationContext.Create(operationId, correlationId, expiresAt));
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

    private sealed class NeverSceneUndoAvailable :
        ISceneReplaceUndoAvailability
    {
        private NeverSceneUndoAvailable()
        {
        }

        public static NeverSceneUndoAvailable Instance { get; } = new();

        public bool HasDurableUndoFor(ActivityInstance target) => false;
    }

    private sealed class AlwaysSceneUndoAvailable :
        ISceneReplaceUndoAvailability
    {
        private AlwaysSceneUndoAvailable()
        {
        }

        public static AlwaysSceneUndoAvailable Instance { get; } = new();

        public bool HasDurableUndoFor(ActivityInstance target) => true;
    }

    private sealed class RejectingSceneOperationPort :
        ISceneActivityOperationPort
    {
        public ValueTask<SceneActivityOperationResult> ExecuteAsync(
            SceneActivityPreparation preparation,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<SceneActivityOperationResult>(
                new InvalidOperationException(
                    "No target-local Scene child execution was expected."));
    }

    private sealed class ForwardingSceneOperationRouteDirectory :
        ISceneOperationRouteDirectory
    {
        public ISceneOperationRouteDirectory? Inner { get; set; }

        public IReadOnlyList<DeviceId> GetSceneParticipantDeviceIds() =>
            RequireInner().GetSceneParticipantDeviceIds();

        public bool TryGetChannel(
            DeviceId peerDeviceId,
            out IActivityChannel? channel) =>
            RequireInner().TryGetChannel(peerDeviceId, out channel);

        public bool TryGetReplaceChannel(
            DeviceId peerDeviceId,
            out IReplaceChannel? channel) =>
            RequireInner().TryGetReplaceChannel(peerDeviceId, out channel);

        public bool TryGetSceneExactSlotChannel(
            DeviceId peerDeviceId,
            out ISceneExactSlotChannel? channel) =>
            RequireInner().TryGetSceneExactSlotChannel(
                peerDeviceId,
                out channel);

        public bool TryGetSceneSourceLookupChannel(
            DeviceId peerDeviceId,
            out ISceneSourceLookupChannel? channel) =>
            RequireInner().TryGetSceneSourceLookupChannel(
                peerDeviceId,
                out channel);

        public bool TryGetSceneChildOperationChannel(
            DeviceId peerDeviceId,
            out ISceneChildOperationChannel? channel) =>
            RequireInner().TryGetSceneChildOperationChannel(
                peerDeviceId,
                out channel);

        private ISceneOperationRouteDirectory RequireInner() => Inner
            ?? throw new InvalidOperationException(
                "The Scene route directory is not initialized.");
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

    private sealed class RecordingSceneControlPeer : ISceneControlPeer
    {
        public RecordingSceneControlPeer(DeviceId deviceId)
        {
            DeviceId = deviceId;
            ActivityId activityId =
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            Result = SceneSourceLookup.FromObservation(
                index: 0,
                activityId,
                [
                    SceneSourceSelection.Create(
                        index: 0,
                        activityId,
                        revision: 7,
                        descriptorDigest: new string('A', 64),
                        ActivityKind.Parse("workspace.note/v1"),
                        ActivityPlacement.On(deviceId, "desktop")),
                ],
                isComplete: true);
        }

        public DeviceId DeviceId { get; }

        public DeviceId? LastCoordinatorDeviceId { get; private set; }

        public SceneSourceLookup Result { get; }

        public SceneExactSlotInspection SlotResult { get; } =
            SceneExactSlotInspection.Observed(SceneSlotOccupancy.Empty);

        public SceneActivityOperationResult? ChildResult { get; private set; }

        public SceneRemoteChildInstruction? LastInstruction { get; private set; }

        public SceneUndoReplaceInstruction? LastUndoInstruction { get; private set; }

        public UndoReplaceResult? UndoResult { get; private set; }

        public ValueTask<SceneSourceLookup> LocateSourceAsync(
            DeviceId coordinatorDeviceId,
            SceneSourceLookupQuery query,
            CancellationToken cancellationToken)
        {
            LastCoordinatorDeviceId = coordinatorDeviceId;
            return ValueTask.FromResult(Result);
        }

        public ValueTask<SceneExactSlotInspection> InspectExactSlotAsync(
            DeviceId coordinatorDeviceId,
            SceneExactSlotQuery query,
            CancellationToken cancellationToken)
        {
            LastCoordinatorDeviceId = coordinatorDeviceId;
            return ValueTask.FromResult(SlotResult);
        }

        public ValueTask<SceneActivityOperationResult> ExecuteChildAsync(
            DeviceId coordinatorDeviceId,
            SceneRemoteChildInstruction instruction,
            CancellationToken cancellationToken)
        {
            LastCoordinatorDeviceId = coordinatorDeviceId;
            LastInstruction = instruction;
            SceneSourceSelection source = instruction.Item.Source!;
            OperationKind kind = instruction.Item.Action switch
            {
                SceneApplyAction.Handoff => OperationKind.Handoff,
                SceneApplyAction.Move => OperationKind.Move,
                SceneApplyAction.Replace => OperationKind.Replace,
                _ => throw new InvalidOperationException(),
            };
            ChildResult = SceneActivityOperationResult.Create(
                OperationReceipt.FromRecordedResult(
                    instruction.Item.ChildOperationId,
                    instruction.Item.ChildCorrelationId,
                    kind,
                    OperationStatus.Committed,
                    source.DeviceId,
                    instruction.Item.Destination.DeviceId,
                    instruction.Item.ActivityId,
                    source.Kind,
                    source.DescriptorDigest,
                    Now,
                    FailureCode.None),
                undoCapsule: null);
            return ValueTask.FromResult(ChildResult);
        }

        public ValueTask<UndoReplaceResult> UndoReplaceAsync(
            DeviceId coordinatorDeviceId,
            SceneUndoReplaceInstruction instruction,
            CancellationToken cancellationToken)
        {
            LastCoordinatorDeviceId = coordinatorDeviceId;
            LastUndoInstruction = instruction;
            UndoResult = UndoReplaceResult.Committed(
                instruction.Context,
                instruction.Capsule.Id,
                Now);
            return ValueTask.FromResult(UndoResult);
        }
    }

    private sealed class FakeActivityControlConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId,
        ProtocolVersion? protocolVersion = null) : IActivityControlConnection
    {
        private readonly Channel<ControlMessage> incoming = Channel.CreateUnbounded<ControlMessage>();
        private readonly Channel<ControlMessage> outgoing = Channel.CreateUnbounded<ControlMessage>();
        private readonly List<ControlMessage> sent = [];

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } =
            protocolVersion ?? new ProtocolVersion(1, 0);

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
