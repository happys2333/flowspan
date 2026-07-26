using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class SceneCoordinatorRoutingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly DeviceId CoordinatorDevice =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DeviceId SourceDevice =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DeviceId TargetDevice =
        DeviceId.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly ActivityId ActivityId =
        Flowspan.Domain.ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly ActivityKind Kind =
        ActivityKind.Parse("workspace.note/v1");

    [Fact]
    public async Task LocalSourceUsesLocalPortWithoutRemoteChild()
    {
        PreparationFixture fixture = CreatePreparation(CoordinatorDevice);
        var localPort = new RecordingOperationPort(
            preparation => Committed(preparation.Item));
        var channel = new RecordingChildChannel(
            SourceDevice,
            _ => throw new InvalidOperationException(
                "A local source must not send a remote child."));
        var routes = new TestRoutes([SourceDevice], channel);
        var port = new CoordinatorSceneActivityOperationPort(
            new FixedClock(Now),
            CoordinatorDevice,
            localPort,
            routes);

        SceneApplyItemResult result = await ApplyAsync(port, fixture);

        Assert.Equal(SceneApplyItemOutcome.Committed, result.Outcome);
        Assert.Equal(1, localPort.CallCount);
        Assert.Equal(0, channel.CallCount);
    }

    [Theory]
    [InlineData(
        SceneControlDeliveryStatus.NotDelivered,
        OperationStatus.Failed,
        FailureCode.PeerUnavailable)]
    [InlineData(
        SceneControlDeliveryStatus.ProtocolUnsupported,
        OperationStatus.Rejected,
        FailureCode.ProtocolIncompatible)]
    [InlineData(
        SceneControlDeliveryStatus.AcknowledgementLost,
        OperationStatus.Recovering,
        FailureCode.AcknowledgementLost)]
    public async Task RemoteDeliveryStatusReducesWithoutLocalExecution(
        SceneControlDeliveryStatus deliveryStatus,
        OperationStatus expectedStatus,
        FailureCode expectedFailure)
    {
        PreparationFixture fixture = CreatePreparation(SourceDevice);
        var localPort = new RecordingOperationPort(
            _ => throw new InvalidOperationException(
                "A remote source must not execute on the coordinator."));
        var channel = new RecordingChildChannel(
            SourceDevice,
            _ => Delivery(deliveryStatus));
        var port = new CoordinatorSceneActivityOperationPort(
            new FixedClock(Now),
            CoordinatorDevice,
            localPort,
            new TestRoutes([SourceDevice], channel));

        SceneApplyItemResult result = await ApplyAsync(port, fixture);

        Assert.Equal(OutcomeFor(expectedStatus), result.Outcome);
        Assert.Equal(expectedFailure, result.FailureCode);
        Assert.Equal(0, localPort.CallCount);
        Assert.Equal(1, channel.CallCount);
    }

    [Fact]
    public async Task MismatchedAcknowledgedResultBecomesRecovering()
    {
        PreparationFixture fixture = CreatePreparation(SourceDevice);
        SceneActivityOperationResult mismatched =
            SceneActivityOperationResult.Create(
                OperationReceipt.FromRecordedResult(
                    OperationId.Parse(
                        "99999999-9999-9999-9999-999999999999"),
                    fixture.Item.ChildCorrelationId,
                    OperationKind.Handoff,
                    OperationStatus.Committed,
                    SourceDevice,
                    TargetDevice,
                    ActivityId,
                    Kind,
                    fixture.Item.Source!.DescriptorDigest,
                    Now,
                    FailureCode.None),
                undoCapsule: null);
        var channel = new RecordingChildChannel(
            SourceDevice,
            _ => SceneChildDeliveryResult.Acknowledged(mismatched));
        var port = new CoordinatorSceneActivityOperationPort(
            new FixedClock(Now),
            CoordinatorDevice,
            new RecordingOperationPort(Committed),
            new TestRoutes([SourceDevice], channel));

        SceneApplyItemResult result = await ApplyAsync(port, fixture);

        Assert.Equal(SceneApplyItemOutcome.Recovering, result.Outcome);
        Assert.Equal(FailureCode.InternalFailure, result.FailureCode);
    }

    [Fact]
    public async Task RemoteInstructionPreservesExactParentAndItemBinding()
    {
        PreparationFixture fixture = CreatePreparation(SourceDevice);
        SceneRemoteChildInstruction? captured = null;
        var channel = new RecordingChildChannel(
            SourceDevice,
            instruction =>
            {
                captured = instruction;
                return SceneChildDeliveryResult.Acknowledged(
                    Committed(instruction.Item));
            });
        var port = new CoordinatorSceneActivityOperationPort(
            new FixedClock(Now),
            CoordinatorDevice,
            new RecordingOperationPort(Committed),
            new TestRoutes([SourceDevice], channel));

        SceneApplyItemResult result = await ApplyAsync(port, fixture);

        Assert.Equal(SceneApplyItemOutcome.Committed, result.Outcome);
        SceneRemoteChildInstruction instruction = Assert.IsType<
            SceneRemoteChildInstruction>(captured);
        Assert.Equal(CoordinatorDevice, instruction.CoordinatorDeviceId);
        Assert.Equal(fixture.Preview.SceneId, instruction.SceneId);
        Assert.Equal(fixture.Preview.SceneRevision, instruction.SceneRevision);
        Assert.Equal(fixture.Preview.SceneDigest, instruction.SceneDigest);
        Assert.Equal(fixture.Preview.Fingerprint, instruction.PreviewFingerprint);
        Assert.Equal(
            fixture.Preview.ParentOperationId,
            instruction.ParentOperationId);
        Assert.Equal(
            fixture.Preview.ParentCorrelationId,
            instruction.ParentCorrelationId);
        Assert.Equal(fixture.Item, instruction.Item);
    }

    [Theory]
    [InlineData(
        SceneControlDeliveryStatus.NotDelivered,
        OperationStatus.Failed,
        FailureCode.PeerUnavailable)]
    [InlineData(
        SceneControlDeliveryStatus.ProtocolUnsupported,
        OperationStatus.Rejected,
        FailureCode.ProtocolIncompatible)]
    [InlineData(
        SceneControlDeliveryStatus.AcknowledgementLost,
        OperationStatus.Recovering,
        FailureCode.AcknowledgementLost)]
    public async Task RemoteUndoDeliveryStatusFailsClosed(
        SceneControlDeliveryStatus deliveryStatus,
        OperationStatus expectedStatus,
        FailureCode expectedFailure)
    {
        (UndoCapsuleReference capsule, OperationContext context) =
            CreateUndoRequest();
        var channel = new RecordingChildChannel(
            TargetDevice,
            _ => throw new InvalidOperationException(),
            _ => new SceneUndoReplaceDeliveryResult(deliveryStatus, null));
        var port = new CoordinatorSceneActivityOperationPort(
            new FixedClock(Now),
            CoordinatorDevice,
            new RecordingOperationPort(Committed),
            new TestRoutes([TargetDevice], channel));

        UndoReplaceResult result = await port.UndoReplaceAsync(
            capsule,
            context,
            CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedFailure, result.FailureCode);
        Assert.Equal(1, channel.UndoCallCount);
    }

    [Fact]
    public async Task RemoteUndoPreservesExactCapsuleAndStableContext()
    {
        (UndoCapsuleReference capsule, OperationContext context) =
            CreateUndoRequest();
        var captured = new List<SceneUndoReplaceInstruction>();
        var channel = new RecordingChildChannel(
            TargetDevice,
            _ => throw new InvalidOperationException(),
            instruction =>
            {
                captured.Add(instruction);
                return SceneUndoReplaceDeliveryResult.Acknowledged(
                    UndoReplaceResult.Committed(
                        instruction.Context,
                        instruction.Capsule.Id,
                        Now));
            });
        var port = new CoordinatorSceneActivityOperationPort(
            new FixedClock(Now),
            CoordinatorDevice,
            new RecordingOperationPort(Committed),
            new TestRoutes([TargetDevice], channel));

        UndoReplaceResult first = await port.UndoReplaceAsync(
            capsule,
            context,
            CancellationToken.None);
        UndoReplaceResult replay = await port.UndoReplaceAsync(
            capsule,
            context,
            CancellationToken.None);

        Assert.Equal(OperationStatus.Committed, first.Status);
        Assert.Equal(first, replay);
        Assert.Equal(2, captured.Count);
        Assert.All(captured, instruction =>
        {
            Assert.Equal(CoordinatorDevice, instruction.CoordinatorDeviceId);
            Assert.Equal(TargetDevice, instruction.TargetDeviceId);
            Assert.Equal(capsule, instruction.Capsule);
            Assert.Equal(context, instruction.Context);
        });
        Assert.Equal(captured[0].BindingDigest, captured[1].BindingDigest);
    }

    [Fact]
    public async Task ForgedRemoteUndoResultBecomesRecovering()
    {
        (UndoCapsuleReference capsule, OperationContext context) =
            CreateUndoRequest();
        var channel = new RecordingChildChannel(
            TargetDevice,
            _ => throw new InvalidOperationException(),
            instruction => SceneUndoReplaceDeliveryResult.Acknowledged(
                UndoReplaceResult.Committed(
                    OperationContext.Create(
                        OperationId.Parse(
                            "99999999-9999-9999-9999-999999999999"),
                        instruction.Context.CorrelationId,
                        instruction.Context.Deadline),
                    instruction.Capsule.Id,
                    Now)));
        var port = new CoordinatorSceneActivityOperationPort(
            new FixedClock(Now),
            CoordinatorDevice,
            new RecordingOperationPort(Committed),
            new TestRoutes([TargetDevice], channel));

        UndoReplaceResult result = await port.UndoReplaceAsync(
            capsule,
            context,
            CancellationToken.None);

        Assert.Equal(OperationStatus.Recovering, result.Status);
        Assert.Equal(FailureCode.InternalFailure, result.FailureCode);
    }

    [Fact]
    public async Task RemotePreflightDenialDiscardsLocalSourceEvidence()
    {
        SceneSourceSelection localSource = CreateSource(
            CoordinatorDevice,
            index: 0);
        var localPeer = new FixedPreflightPeer(
            CoordinatorDevice,
            SceneSourceLookup.FromObservation(
                index: 0,
                ActivityId,
                [localSource],
                isComplete: true));
        var lookupChannel = new FixedSourceLookupChannel(
            SourceDevice,
            SceneSourceLookupDeliveryResult.Acknowledged(
                SceneSourceLookup.Unavailable(
                    index: 0,
                    ActivityId,
                    SceneApplyItemReason.CapabilityDenied)));
        var routes = new TestRoutes(
            [SourceDevice],
            childChannel: null,
            lookupChannel);
        var port = new RoutedSceneApplyPreflightPort(
            CoordinatorDevice,
            localPeer,
            routes);

        SceneSourceLookup result = await port.LocateSourcesAsync(
            ActivityId,
            index: 0,
            OperationContext.Create(
                OperationId.Parse(
                    "77777777-7777-7777-7777-777777777777"),
                CorrelationId.Parse(
                    "88888888-8888-8888-8888-888888888888"),
                Now.AddMinutes(5)),
            CancellationToken.None);

        Assert.Equal(SceneSourceLookupStatus.Unavailable, result.Status);
        Assert.Equal(SceneApplyItemReason.CapabilityDenied, result.Reason);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task TooManyRemoteParticipantsFailBeforeAnyQuery()
    {
        DeviceId[] remoteDevices = Enumerable.Range(1, ScenePlan.MaximumActivities)
            .Select(value => DeviceId.From(Guid.Parse(
                $"00000000-0000-0000-0000-{value:D12}")))
            .ToArray();
        var localPeer = new FixedPreflightPeer(
            CoordinatorDevice,
            SceneSourceLookup.FromObservation(
                index: 0,
                ActivityId,
                [],
                isComplete: true));
        var port = new RoutedSceneApplyPreflightPort(
            CoordinatorDevice,
            localPeer,
            new TestRoutes(remoteDevices));

        SceneSourceLookup result = await port.LocateSourcesAsync(
            ActivityId,
            index: 0,
            OperationContext.Create(
                OperationId.Parse(
                    "77777777-7777-7777-7777-777777777777"),
                CorrelationId.Parse(
                    "88888888-8888-8888-8888-888888888888"),
                Now.AddMinutes(5)),
            CancellationToken.None);

        Assert.Equal(SceneSourceLookupStatus.Unavailable, result.Status);
        Assert.Equal(SceneApplyItemReason.SourceLookupUnavailable, result.Reason);
        Assert.Equal(0, localPeer.LookupCallCount);
    }

    private static PreparationFixture CreatePreparation(DeviceId sourceDeviceId)
    {
        SceneSourceSelection source = CreateSource(sourceDeviceId, index: 0);
        SceneActivityPlan plan = SceneActivityPlan.Place(
            ActivityId,
            ActivityPlacement.On(TargetDevice, "focus"),
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);
        SceneApplyItemPreview item = SceneApplyItemPreview.TransferToEmpty(
            plan,
            source,
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"));
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("66666666-6666-6666-6666-666666666666"),
            "routing-test-scene",
            [plan]);
        SceneApplyPreview preview = SceneApplyPreview.Create(
            scene,
            OperationId.Parse("77777777-7777-7777-7777-777777777777"),
            CorrelationId.Parse("88888888-8888-8888-8888-888888888888"),
            Now,
            Now.AddMinutes(5),
            [item]);
        return new PreparationFixture(scene, preview, item);
    }

    private static async ValueTask<SceneApplyItemResult> ApplyAsync(
        ISceneActivityOperationPort operationPort,
        PreparationFixture fixture)
    {
        var coordinator = new SceneApplyCoordinator(
            new FixedClock(Now),
            new InMemorySceneApplyJournal(),
            operationPort);
        SceneApplyExecutionResult execution = await coordinator.ApplyAsync(
            fixture.Scene,
            fixture.Preview,
            SceneApplyApproval.Create(
                fixture.Preview.Fingerprint,
                fixture.Preview.RequiredReplaceConfirmations),
            CancellationToken.None);
        return Assert.Single(
            Assert.IsType<SceneApplyResult>(execution.Result).Items);
    }

    private static SceneSourceSelection CreateSource(
        DeviceId deviceId,
        int index) =>
        SceneSourceSelection.Create(
            index,
            ActivityId,
            revision: 7,
            new string('A', 64),
            Kind,
            ActivityPlacement.On(deviceId, "desktop"));

    private static SceneActivityOperationResult Committed(
        SceneActivityPreparation preparation) =>
        Committed(preparation.Item);

    private static SceneActivityOperationResult Committed(
        SceneApplyItemPreview item) =>
        SceneActivityOperationResult.Create(
            OperationReceipt.FromRecordedResult(
                item.ChildOperationId,
                item.ChildCorrelationId,
                OperationKind.Handoff,
                OperationStatus.Committed,
                item.Source!.DeviceId,
                item.Destination.DeviceId,
                item.ActivityId,
                item.Source.Kind,
                item.Source.DescriptorDigest,
                Now,
                FailureCode.None),
            undoCapsule: null);

    private static SceneChildDeliveryResult Delivery(
        SceneControlDeliveryStatus status) => status switch
        {
            SceneControlDeliveryStatus.NotDelivered =>
                SceneChildDeliveryResult.NotDelivered,
            SceneControlDeliveryStatus.ProtocolUnsupported =>
                SceneChildDeliveryResult.ProtocolUnsupported,
            SceneControlDeliveryStatus.AcknowledgementLost =>
                SceneChildDeliveryResult.AcknowledgementLost,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    private static SceneApplyItemOutcome OutcomeFor(OperationStatus status) =>
        status switch
        {
            OperationStatus.Rejected => SceneApplyItemOutcome.Rejected,
            OperationStatus.Failed => SceneApplyItemOutcome.Failed,
            OperationStatus.Recovering => SceneApplyItemOutcome.Recovering,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    private static (UndoCapsuleReference Capsule, OperationContext Context)
        CreateUndoRequest()
    {
        DateTimeOffset expiresAt = Now.AddMinutes(5);
        var capsule = new UndoCapsuleReference(
            UndoCapsuleId.Parse("12121212-1212-1212-1212-121212121212"),
            OperationId.Parse("13131313-1313-1313-1313-131313131313"),
            CorrelationId.Parse("14141414-1414-1414-1414-141414141414"),
            TargetDevice,
            ActivityId.Parse("15151515-1515-1515-1515-151515151515"),
            ExpectedTargetRevision: 9,
            TargetDescriptorDigest: new string('E', 64),
            IncomingActivityId: ActivityId,
            IncomingDescriptorDigest: new string('A', 64),
            expiresAt);
        OperationContext context = OperationContext.Create(
            OperationId.Parse("16161616-1616-1616-1616-161616161616"),
            CorrelationId.Parse("17171717-1717-1717-1717-171717171717"),
            expiresAt);
        return (capsule, context);
    }

    private sealed record PreparationFixture(
        ScenePlan Scene,
        SceneApplyPreview Preview,
        SceneApplyItemPreview Item);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class RecordingOperationPort(
        Func<SceneActivityPreparation, SceneActivityOperationResult> execute) :
        ISceneActivityOperationPort
    {
        public int CallCount { get; private set; }

        public ValueTask<SceneActivityOperationResult> ExecuteAsync(
            SceneActivityPreparation preparation,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(execute(preparation));
        }
    }

    private sealed class RecordingChildChannel(
        DeviceId targetDeviceId,
        Func<SceneRemoteChildInstruction, SceneChildDeliveryResult> execute,
        Func<SceneUndoReplaceInstruction, SceneUndoReplaceDeliveryResult>? undo = null) :
        ISceneChildOperationChannel
    {
        public DeviceId TargetDeviceId { get; } = targetDeviceId;

        public int CallCount { get; private set; }

        public int UndoCallCount { get; private set; }

        public ValueTask<SceneChildDeliveryResult> ExecuteChildAsync(
            DeviceId requestingDeviceId,
            SceneRemoteChildInstruction instruction,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(execute(instruction));
        }

        public ValueTask<SceneUndoReplaceDeliveryResult> UndoReplaceAsync(
            DeviceId requestingDeviceId,
            SceneUndoReplaceInstruction instruction,
            CancellationToken cancellationToken)
        {
            UndoCallCount++;
            return ValueTask.FromResult(
                undo?.Invoke(instruction)
                ?? SceneUndoReplaceDeliveryResult.ProtocolUnsupported);
        }
    }

    private sealed class FixedSourceLookupChannel(
        DeviceId targetDeviceId,
        SceneSourceLookupDeliveryResult delivery) : ISceneSourceLookupChannel
    {
        public DeviceId TargetDeviceId { get; } = targetDeviceId;

        public ValueTask<SceneSourceLookupDeliveryResult> QuerySourceAsync(
            DeviceId requestingDeviceId,
            SceneSourceLookupQuery query,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(delivery);
    }

    private sealed class FixedPreflightPeer(
        DeviceId deviceId,
        SceneSourceLookup lookup) : ISceneApplyPreflightPeer
    {
        public DeviceId DeviceId { get; } = deviceId;

        public int LookupCallCount { get; private set; }

        public ValueTask<SceneSourceLookup> LocateSourceAsync(
            DeviceId requestingDeviceId,
            ActivityId activityId,
            int index,
            OperationContext childContext,
            CancellationToken cancellationToken)
        {
            LookupCallCount++;
            return ValueTask.FromResult(lookup);
        }

        public ValueTask<SceneExactSlotInspection> InspectExactSlotAsync(
            DeviceId requestingDeviceId,
            SceneActivityPlan item,
            SceneSourceSelection source,
            OperationContext childContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(SceneExactSlotInspection.Observed(
                SceneSlotOccupancy.Empty));
    }

    private sealed class TestRoutes(
        IReadOnlyList<DeviceId> sceneParticipantDeviceIds,
        ISceneChildOperationChannel? childChannel = null,
        ISceneSourceLookupChannel? lookupChannel = null) :
        ISceneOperationRouteDirectory
    {
        public IReadOnlyList<DeviceId> GetSceneParticipantDeviceIds() =>
            sceneParticipantDeviceIds;

        public bool TryGetChannel(
            DeviceId peerDeviceId,
            out IActivityChannel? channel)
        {
            channel = null;
            return false;
        }

        public bool TryGetReplaceChannel(
            DeviceId peerDeviceId,
            out IReplaceChannel? channel)
        {
            channel = null;
            return false;
        }

        public bool TryGetSceneExactSlotChannel(
            DeviceId peerDeviceId,
            out ISceneExactSlotChannel? channel)
        {
            channel = null;
            return false;
        }

        public bool TryGetSceneSourceLookupChannel(
            DeviceId peerDeviceId,
            out ISceneSourceLookupChannel? channel)
        {
            channel = lookupChannel?.TargetDeviceId == peerDeviceId
                ? lookupChannel
                : null;
            return channel is not null;
        }

        public bool TryGetSceneChildOperationChannel(
            DeviceId peerDeviceId,
            out ISceneChildOperationChannel? channel)
        {
            channel = childChannel?.TargetDeviceId == peerDeviceId
                ? childChannel
                : null;
            return channel is not null;
        }
    }
}
