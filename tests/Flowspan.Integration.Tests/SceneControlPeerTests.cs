using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flowspan.Application;
using Flowspan.Application.Adapters;
using Flowspan.Domain;

namespace Flowspan.Integration.Tests;

public sealed class SceneControlPeerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId CoordinatorId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId SourceId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DeviceId TargetId =
        DeviceId.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly ActivityId ActivityId =
        Flowspan.Domain.ActivityId.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly ActivityId OccupantId =
        Flowspan.Domain.ActivityId.Parse(
            "99999999-9999-9999-9999-999999999999");

    [Fact]
    public async Task DuplicateTerminalChildReplaysWithoutSecondOperationCall()
    {
        var clock = new FixedClock(Now);
        SceneActivityOperationEndpoint endpoint = CreateEndpoint(clock);
        endpoint.SetPeerGrant(
            CoordinatorId,
            CapabilityGrant.Of(Capability.SceneApply));
        var operationPort = new CountingOperationPort(clock);
        var peer = new SceneControlPeer(
            clock,
            endpoint,
            operationPort,
            new InMemorySceneRemoteChildJournal());
        SceneRemoteChildInstruction instruction = CreateInstruction();

        SceneActivityOperationResult first = await peer.ExecuteChildAsync(
            CoordinatorId,
            instruction,
            CancellationToken.None);
        SceneActivityOperationResult second = await peer.ExecuteChildAsync(
            CoordinatorId,
            instruction,
            CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(OperationStatus.Committed, second.Receipt.Status);
        Assert.Equal(1, operationPort.CallCount);
    }

    [Fact]
    public async Task ChangedBindingUnderSameChildOperationIdIsRejected()
    {
        var clock = new FixedClock(Now);
        SceneActivityOperationEndpoint endpoint = CreateEndpoint(clock);
        endpoint.SetPeerGrant(
            CoordinatorId,
            CapabilityGrant.Of(Capability.SceneApply));
        var operationPort = new CountingOperationPort(clock);
        var peer = new SceneControlPeer(
            clock,
            endpoint,
            operationPort,
            new InMemorySceneRemoteChildJournal());
        SceneRemoteChildInstruction original = CreateInstruction();
        SceneRemoteChildInstruction changed = CreateInstruction(
            original.Item.Source!,
            SceneSourceDisposition.MoveAfterAcknowledgement);

        SceneActivityOperationResult first = await peer.ExecuteChildAsync(
            CoordinatorId,
            original,
            CancellationToken.None);
        SceneActivityOperationResult conflict = await peer.ExecuteChildAsync(
            CoordinatorId,
            changed,
            CancellationToken.None);

        Assert.Equal(OperationStatus.Committed, first.Receipt.Status);
        Assert.Equal(OperationStatus.Rejected, conflict.Receipt.Status);
        Assert.Equal(FailureCode.OperationIdConflict, conflict.Receipt.FailureCode);
        Assert.Equal(1, operationPort.CallCount);
    }

    [Fact]
    public async Task CoordinatorSceneDenialIsTerminalBeforeOperationCall()
    {
        var clock = new FixedClock(Now);
        SceneActivityOperationEndpoint endpoint = CreateEndpoint(clock);
        var operationPort = new CountingOperationPort(clock);
        var peer = new SceneControlPeer(
            clock,
            endpoint,
            operationPort,
            new InMemorySceneRemoteChildJournal());

        SceneActivityOperationResult result = await peer.ExecuteChildAsync(
            CoordinatorId,
            CreateInstruction(),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Rejected, result.Receipt.Status);
        Assert.Equal(FailureCode.CapabilityDenied, result.Receipt.FailureCode);
        Assert.Equal(0, operationPort.CallCount);
    }

    [Fact]
    public async Task ExpiredStableDeadlineIsRejectedAndReplayedWithoutOperation()
    {
        var clock = new FixedClock(Now.AddMinutes(6));
        SceneActivityOperationEndpoint endpoint = CreateEndpoint(clock);
        endpoint.SetPeerGrant(
            CoordinatorId,
            CapabilityGrant.Of(Capability.SceneApply));
        var operationPort = new CountingOperationPort(clock);
        var peer = new SceneControlPeer(
            clock,
            endpoint,
            operationPort,
            new InMemorySceneRemoteChildJournal());
        SceneRemoteChildInstruction instruction = CreateInstruction();

        SceneActivityOperationResult first = await peer.ExecuteChildAsync(
            CoordinatorId,
            instruction,
            CancellationToken.None);
        SceneActivityOperationResult replay = await peer.ExecuteChildAsync(
            CoordinatorId,
            instruction,
            CancellationToken.None);

        Assert.Equal(OperationStatus.Rejected, first.Receipt.Status);
        Assert.Equal(FailureCode.DeadlineExpired, first.Receipt.FailureCode);
        Assert.Equal(first, replay);
        Assert.Equal(0, operationPort.CallCount);
    }

    [Fact]
    public async Task ConcurrentDuplicateRunsOperationOnlyOnce()
    {
        var clock = new FixedClock(Now);
        SceneActivityOperationEndpoint endpoint = CreateEndpoint(clock);
        endpoint.SetPeerGrant(
            CoordinatorId,
            CapabilityGrant.Of(Capability.SceneApply));
        var operationPort = new BlockingOperationPort(clock);
        var peer = new SceneControlPeer(
            clock,
            endpoint,
            operationPort,
            new InMemorySceneRemoteChildJournal());
        SceneRemoteChildInstruction instruction = CreateInstruction();

        Task<SceneActivityOperationResult> first = peer.ExecuteChildAsync(
            CoordinatorId,
            instruction,
            CancellationToken.None).AsTask();
        await operationPort.WaitUntilEnteredAsync();
        SceneActivityOperationResult duplicate = await peer.ExecuteChildAsync(
            CoordinatorId,
            instruction,
            CancellationToken.None);
        operationPort.Release();
        SceneActivityOperationResult completed = await first;

        Assert.Equal(OperationStatus.Recovering, duplicate.Receipt.Status);
        Assert.Equal(
            FailureCode.OperationInProgress,
            duplicate.Receipt.FailureCode);
        Assert.Equal(OperationStatus.Committed, completed.Receipt.Status);
        Assert.Equal(1, operationPort.CallCount);
    }

    [Fact]
    public async Task MismatchedOperationResultCannotPoisonTerminalJournal()
    {
        var clock = new FixedClock(Now);
        SceneActivityOperationEndpoint endpoint = CreateEndpoint(clock);
        endpoint.SetPeerGrant(
            CoordinatorId,
            CapabilityGrant.Of(Capability.SceneApply));
        var operationPort = new MismatchedOperationPort(clock);
        var peer = new SceneControlPeer(
            clock,
            endpoint,
            operationPort,
            new InMemorySceneRemoteChildJournal());
        SceneRemoteChildInstruction instruction = CreateInstruction();

        SceneActivityOperationResult first = await peer.ExecuteChildAsync(
            CoordinatorId,
            instruction,
            CancellationToken.None);
        SceneActivityOperationResult duplicate = await peer.ExecuteChildAsync(
            CoordinatorId,
            instruction,
            CancellationToken.None);

        Assert.Equal(OperationStatus.Recovering, first.Receipt.Status);
        Assert.Equal(FailureCode.InternalFailure, first.Receipt.FailureCode);
        Assert.Equal(OperationStatus.Recovering, duplicate.Receipt.Status);
        Assert.Equal(
            FailureCode.OperationInProgress,
            duplicate.Receipt.FailureCode);
        Assert.Equal(1, operationPort.CallCount);
    }

    [Fact]
    public async Task ReopenedPersistentJournalReplaysWithoutOperationCall()
    {
        var clock = new FixedClock(Now);
        SceneActivityOperationEndpoint endpoint = CreateEndpoint(clock);
        endpoint.SetPeerGrant(
            CoordinatorId,
            CapabilityGrant.Of(Capability.SceneApply));
        var payloadStore = new InMemoryPayloadStore();
        var firstPort = new CountingOperationPort(clock);
        SceneRemoteChildInstruction instruction = CreateInstruction();
        SceneActivityOperationResult first;
        using (PersistentSceneRemoteChildJournal firstJournal =
               await PersistentSceneRemoteChildJournal.OpenAsync(payloadStore))
        {
            var firstPeer = new SceneControlPeer(
                clock,
                endpoint,
                firstPort,
                firstJournal);
            first = await firstPeer.ExecuteChildAsync(
                CoordinatorId,
                instruction,
                CancellationToken.None);
        }

        var replayPort = new CountingOperationPort(clock);
        using PersistentSceneRemoteChildJournal reopened =
            await PersistentSceneRemoteChildJournal.OpenAsync(payloadStore);
        var replayPeer = new SceneControlPeer(
            clock,
            endpoint,
            replayPort,
            reopened);

        SceneActivityOperationResult replayed = await replayPeer.ExecuteChildAsync(
            CoordinatorId,
            instruction,
            CancellationToken.None);

        Assert.Equal(first, replayed);
        Assert.Equal(1, firstPort.CallCount);
        Assert.Equal(0, replayPort.CallCount);
        Assert.Equal(1, reopened.EntryCount);
    }

    [Fact]
    public async Task ReopenedStartedChildReturnsRecoveringWithoutOperationCall()
    {
        var clock = new FixedClock(Now);
        SceneActivityOperationEndpoint endpoint = CreateEndpoint(clock);
        endpoint.SetPeerGrant(
            CoordinatorId,
            CapabilityGrant.Of(Capability.SceneApply));
        var payloadStore = new InMemoryPayloadStore();
        var firstPort = new CountingOperationPort(
            clock,
            OperationStatus.Recovering,
            FailureCode.AcknowledgementLost);
        SceneRemoteChildInstruction instruction = CreateInstruction();
        using (PersistentSceneRemoteChildJournal firstJournal =
               await PersistentSceneRemoteChildJournal.OpenAsync(payloadStore))
        {
            var firstPeer = new SceneControlPeer(
                clock,
                endpoint,
                firstPort,
                firstJournal);
            SceneActivityOperationResult first = await firstPeer.ExecuteChildAsync(
                CoordinatorId,
                instruction,
                CancellationToken.None);
            Assert.Equal(OperationStatus.Recovering, first.Receipt.Status);
        }

        var replayPort = new CountingOperationPort(clock);
        using PersistentSceneRemoteChildJournal reopened =
            await PersistentSceneRemoteChildJournal.OpenAsync(payloadStore);
        var replayPeer = new SceneControlPeer(
            clock,
            endpoint,
            replayPort,
            reopened);

        SceneActivityOperationResult replayed = await replayPeer.ExecuteChildAsync(
            CoordinatorId,
            instruction,
            CancellationToken.None);

        Assert.Equal(OperationStatus.Recovering, replayed.Receipt.Status);
        Assert.Equal(FailureCode.OperationInProgress, replayed.Receipt.FailureCode);
        Assert.Equal(1, firstPort.CallCount);
        Assert.Equal(0, replayPort.CallCount);
    }

    [Fact]
    public async Task AmbiguousStartedSaveReloadsAsInProgressWithoutOperationCall()
    {
        var clock = new FixedClock(Now);
        SceneActivityOperationEndpoint endpoint = CreateEndpoint(clock);
        endpoint.SetPeerGrant(
            CoordinatorId,
            CapabilityGrant.Of(Capability.SceneApply));
        var payloadStore = new AmbiguousPayloadStore(failAfterSaveNumber: 1);
        var firstPort = new CountingOperationPort(clock);
        SceneRemoteChildInstruction instruction = CreateInstruction();
        using (PersistentSceneRemoteChildJournal firstJournal =
               await PersistentSceneRemoteChildJournal.OpenAsync(payloadStore))
        {
            var firstPeer = new SceneControlPeer(
                clock,
                endpoint,
                firstPort,
                firstJournal);
            SceneActivityOperationResult uncertain = await firstPeer.ExecuteChildAsync(
                CoordinatorId,
                instruction,
                CancellationToken.None);
            Assert.Equal(OperationStatus.Recovering, uncertain.Receipt.Status);
            Assert.Equal(FailureCode.InternalFailure, uncertain.Receipt.FailureCode);
        }

        var replayPort = new CountingOperationPort(clock);
        using PersistentSceneRemoteChildJournal reopened =
            await PersistentSceneRemoteChildJournal.OpenAsync(payloadStore);
        var replayPeer = new SceneControlPeer(
            clock,
            endpoint,
            replayPort,
            reopened);

        SceneActivityOperationResult replayed = await replayPeer.ExecuteChildAsync(
            CoordinatorId,
            instruction,
            CancellationToken.None);

        Assert.Equal(OperationStatus.Recovering, replayed.Receipt.Status);
        Assert.Equal(FailureCode.OperationInProgress, replayed.Receipt.FailureCode);
        Assert.Equal(0, firstPort.CallCount);
        Assert.Equal(0, replayPort.CallCount);
    }

    [Fact]
    public async Task AmbiguousTerminalSaveReloadsExactResultWithoutSecondOperation()
    {
        var clock = new FixedClock(Now);
        SceneActivityOperationEndpoint endpoint = CreateEndpoint(clock);
        endpoint.SetPeerGrant(
            CoordinatorId,
            CapabilityGrant.Of(Capability.SceneApply));
        var payloadStore = new AmbiguousPayloadStore(failAfterSaveNumber: 2);
        var firstPort = new CountingOperationPort(clock);
        SceneRemoteChildInstruction instruction = CreateInstruction();
        using (PersistentSceneRemoteChildJournal firstJournal =
               await PersistentSceneRemoteChildJournal.OpenAsync(payloadStore))
        {
            var firstPeer = new SceneControlPeer(
                clock,
                endpoint,
                firstPort,
                firstJournal);
            SceneActivityOperationResult uncertain = await firstPeer.ExecuteChildAsync(
                CoordinatorId,
                instruction,
                CancellationToken.None);
            Assert.Equal(OperationStatus.Recovering, uncertain.Receipt.Status);
            Assert.Equal(
                FailureCode.AcknowledgementLost,
                uncertain.Receipt.FailureCode);
        }

        var replayPort = new CountingOperationPort(clock);
        using PersistentSceneRemoteChildJournal reopened =
            await PersistentSceneRemoteChildJournal.OpenAsync(payloadStore);
        var replayPeer = new SceneControlPeer(
            clock,
            endpoint,
            replayPort,
            reopened);

        SceneActivityOperationResult replayed = await replayPeer.ExecuteChildAsync(
            CoordinatorId,
            instruction,
            CancellationToken.None);

        Assert.Equal(OperationStatus.Committed, replayed.Receipt.Status);
        Assert.Equal(1, firstPort.CallCount);
        Assert.Equal(0, replayPort.CallCount);
    }

    [Fact]
    public async Task PersistentJournalRejectsHostileOrMismatchedState()
    {
        var clock = new FixedClock(Now);
        SceneActivityOperationEndpoint endpoint = CreateEndpoint(clock);
        endpoint.SetPeerGrant(
            CoordinatorId,
            CapabilityGrant.Of(Capability.SceneApply));
        var payloadStore = new InMemoryPayloadStore();
        using (PersistentSceneRemoteChildJournal journal =
               await PersistentSceneRemoteChildJournal.OpenAsync(payloadStore))
        {
            var peer = new SceneControlPeer(
                clock,
                endpoint,
                new CountingOperationPort(clock),
                journal);
            SceneActivityOperationResult result = await peer.ExecuteChildAsync(
                CoordinatorId,
                CreateInstruction(),
                CancellationToken.None);
            Assert.Equal(OperationStatus.Committed, result.Receipt.Status);
        }

        byte[] valid = payloadStore.Snapshot;
        string plaintext = Encoding.UTF8.GetString(valid);
        Assert.DoesNotContain(
            "source-title-canary",
            plaintext,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "source-payload-canary",
            plaintext,
            StringComparison.Ordinal);
        byte[] unknown = Encoding.UTF8.GetBytes(plaintext.Replace(
            "\"entries\":[",
            "\"payloadJson\":\"secret-canary\",\"entries\":[",
            StringComparison.Ordinal));
        byte[] duplicate = Encoding.UTF8.GetBytes(plaintext.Replace(
            "\"formatVersion\":1",
            "\"formatVersion\":1,\"formatVersion\":1",
            StringComparison.Ordinal));
        JsonObject mismatchedNode = JsonNode.Parse(plaintext)!.AsObject();
        mismatchedNode["entries"]!.AsArray()[0]!
            .AsObject()["result"]!.AsObject()["receipt"]!
            .AsObject()["operationId"] =
                "12121212-1212-1212-1212-121212121212";
        byte[] mismatched = Encoding.UTF8.GetBytes(
            mismatchedNode.ToJsonString());

        await AssertRejectedAsync(unknown);
        await AssertRejectedAsync(duplicate);
        await AssertRejectedAsync(mismatched);

        static async Task AssertRejectedAsync(byte[] payload)
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                using PersistentSceneRemoteChildJournal journal =
                    await PersistentSceneRemoteChildJournal.OpenAsync(
                        new InMemoryPayloadStore(payload));
            });
        }
    }

    [Fact]
    public async Task RemoteChildRunsOnSelectedSourceThroughExistingActivityChannel()
    {
        using var fixture = new RoutedFixture();

        SceneActivityOperationResult result = await fixture.ExecuteAsync(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);

        Assert.Equal(OperationStatus.Committed, result.Receipt.Status);
        Assert.True(fixture.SourceNode.TryGetActivity(
            ActivityId,
            out ActivityInstance? source));
        Assert.Equal(ActivityLifecycle.Active, source.Lifecycle);
        Assert.True(fixture.TargetNode.TryGetActivity(
            ActivityId,
            out ActivityInstance? target));
        Assert.Contains("source-payload-canary", target.Descriptor.PayloadJson);
        Assert.Equal(1, fixture.Routes.ActivitySendCount);
    }

    [Fact]
    public async Task RemoteMoveClosesSourceOnlyAfterTargetAcknowledges()
    {
        using var fixture = new RoutedFixture();

        SceneActivityOperationResult result = await fixture.ExecuteAsync(
            SceneSourceDisposition.MoveAfterAcknowledgement,
            SceneConflictPolicy.RequireEmpty);

        Assert.Equal(OperationStatus.Committed, result.Receipt.Status);
        Assert.True(fixture.SourceNode.TryGetActivity(
            ActivityId,
            out ActivityInstance? source));
        Assert.Equal(ActivityLifecycle.Closed, source.Lifecycle);
        Assert.True(fixture.TargetNode.TryGetActivity(ActivityId, out _));
        Assert.Equal(1, fixture.Routes.ActivitySendCount);
    }

    [Fact]
    public async Task RemoteReplaceUsesExistingChannelAndReturnsDurableUndo()
    {
        using var fixture = new RoutedFixture(withReplace: true);

        SceneActivityOperationResult result = await fixture.ExecuteAsync(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.ReplaceWithUndo);

        Assert.Equal(OperationStatus.Committed, result.Receipt.Status);
        UndoCapsuleReference undo = Assert.IsType<UndoCapsuleReference>(
            result.UndoCapsule);
        Assert.Equal(OccupantId, undo.TargetActivityId);
        Assert.Equal(ActivityId, undo.IncomingActivityId);
        Assert.Equal(
            Now + ReplaceEndpoint.MaximumUndoRetention,
            undo.ExpiresAt);
        Assert.True(fixture.SourceNode.TryGetActivity(
            ActivityId,
            out ActivityInstance? source));
        Assert.Equal(ActivityLifecycle.Active, source.Lifecycle);
        Assert.True(fixture.TargetNode.TryGetActivity(ActivityId, out _));
        Assert.False(fixture.TargetNode.TryGetActivity(OccupantId, out _));
        Assert.Equal(1, fixture.Routes.ReplaceSendCount);
    }

    [Fact]
    public async Task LostMoveAcknowledgementReturnsRecoveringAndPreservesSource()
    {
        using var fixture = new RoutedFixture(
            dropActivityAcknowledgement: true);

        SceneActivityOperationResult uncertain = await fixture.ExecuteAsync(
            SceneSourceDisposition.MoveAfterAcknowledgement,
            SceneConflictPolicy.RequireEmpty);
        SceneActivityOperationResult duplicate = await fixture.ExecuteAsync(
            SceneSourceDisposition.MoveAfterAcknowledgement,
            SceneConflictPolicy.RequireEmpty);

        Assert.Equal(OperationStatus.Recovering, uncertain.Receipt.Status);
        Assert.Equal(
            FailureCode.AcknowledgementLost,
            uncertain.Receipt.FailureCode);
        Assert.Equal(OperationStatus.Recovering, duplicate.Receipt.Status);
        Assert.Equal(
            FailureCode.OperationInProgress,
            duplicate.Receipt.FailureCode);
        Assert.True(fixture.SourceNode.TryGetActivity(
            ActivityId,
            out ActivityInstance? source));
        Assert.Equal(ActivityLifecycle.Active, source.Lifecycle);
        Assert.True(fixture.TargetNode.TryGetActivity(ActivityId, out _));
        Assert.Equal(1, fixture.Routes.ActivitySendCount);
    }

    [Fact]
    public async Task LostReplaceAcknowledgementReturnsRecoveringWithoutRetry()
    {
        using var fixture = new RoutedFixture(
            withReplace: true,
            dropReplaceAcknowledgement: true);

        SceneActivityOperationResult uncertain = await fixture.ExecuteAsync(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.ReplaceWithUndo);
        SceneActivityOperationResult duplicate = await fixture.ExecuteAsync(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.ReplaceWithUndo);

        Assert.Equal(OperationStatus.Recovering, uncertain.Receipt.Status);
        Assert.Equal(
            FailureCode.AcknowledgementLost,
            uncertain.Receipt.FailureCode);
        Assert.Null(uncertain.UndoCapsule);
        Assert.Equal(OperationStatus.Recovering, duplicate.Receipt.Status);
        Assert.True(fixture.TargetNode.TryGetActivity(ActivityId, out _));
        Assert.False(fixture.TargetNode.TryGetActivity(OccupantId, out _));
        Assert.Equal(1, fixture.Routes.ReplaceSendCount);
    }

    [Fact]
    public async Task RemoteSourceReceiveDenialStopsBeforeTargetMutation()
    {
        using var fixture = new RoutedFixture();
        fixture.DenySourceReceive();

        SceneActivityOperationResult result = await fixture.ExecuteAsync(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);

        Assert.Equal(OperationStatus.Rejected, result.Receipt.Status);
        Assert.Equal(FailureCode.CapabilityDenied, result.Receipt.FailureCode);
        Assert.False(fixture.TargetNode.TryGetActivity(ActivityId, out _));
        Assert.Equal(0, fixture.Routes.ActivitySendCount);
    }

    [Fact]
    public async Task RemoteTargetSceneDenialStopsBeforeActivityOffer()
    {
        using var fixture = new RoutedFixture();
        fixture.DenyTargetSceneApply();

        SceneActivityOperationResult result = await fixture.ExecuteAsync(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);

        Assert.Equal(OperationStatus.Rejected, result.Receipt.Status);
        Assert.Equal(FailureCode.CapabilityDenied, result.Receipt.FailureCode);
        Assert.False(fixture.TargetNode.TryGetActivity(ActivityId, out _));
        Assert.Equal(0, fixture.Routes.ActivitySendCount);
    }

    [Fact]
    public async Task RemoteTargetOfferDenialPreservesSource()
    {
        using var fixture = new RoutedFixture();
        fixture.DenyTargetOffer();

        SceneActivityOperationResult result = await fixture.ExecuteAsync(
            SceneSourceDisposition.MoveAfterAcknowledgement,
            SceneConflictPolicy.RequireEmpty);

        Assert.Equal(OperationStatus.Rejected, result.Receipt.Status);
        Assert.Equal(FailureCode.CapabilityDenied, result.Receipt.FailureCode);
        Assert.True(fixture.SourceNode.TryGetActivity(
            ActivityId,
            out ActivityInstance? source));
        Assert.Equal(ActivityLifecycle.Active, source.Lifecycle);
        Assert.False(fixture.TargetNode.TryGetActivity(ActivityId, out _));
        Assert.Equal(1, fixture.Routes.ActivitySendCount);
    }

    [Theory]
    [InlineData(
        SceneControlDeliveryStatus.ProtocolUnsupported,
        OperationStatus.Rejected,
        FailureCode.ProtocolIncompatible)]
    [InlineData(
        SceneControlDeliveryStatus.NotDelivered,
        OperationStatus.Failed,
        FailureCode.PeerUnavailable)]
    [InlineData(
        SceneControlDeliveryStatus.AcknowledgementLost,
        OperationStatus.Failed,
        FailureCode.PeerUnavailable)]
    public async Task RemoteSlotDeliveryFailureStopsBeforeMutation(
        SceneControlDeliveryStatus deliveryStatus,
        OperationStatus expectedStatus,
        FailureCode expectedFailure)
    {
        using var fixture = new RoutedFixture(slotStatus: deliveryStatus);

        SceneActivityOperationResult result = await fixture.ExecuteAsync(
            SceneSourceDisposition.PreserveSource,
            SceneConflictPolicy.RequireEmpty);

        Assert.Equal(expectedStatus, result.Receipt.Status);
        Assert.Equal(expectedFailure, result.Receipt.FailureCode);
        Assert.False(fixture.TargetNode.TryGetActivity(ActivityId, out _));
        Assert.Equal(0, fixture.Routes.ActivitySendCount);
    }

    private static SceneActivityOperationEndpoint CreateEndpoint(IClock clock)
    {
        var catalog = new InMemoryActivityCatalog();
        var adapters = new ActivityAdapterRegistry([new WorkspaceNoteAdapter()]);
        var node = new FlowspanNode(
            SourceId,
            "Source",
            clock,
            catalog,
            new InMemoryOperationJournal(),
            adapters,
            NullReceiptSink.Instance);
        var preflight = new SceneApplyPreflightEndpoint(
            SourceId,
            clock,
            catalog,
            adapters,
            NeverUndoAvailable.Instance);
        return new SceneActivityOperationEndpoint(node, preflight, clock: clock);
    }

    private static SceneRemoteChildInstruction CreateInstruction()
    {
        SceneSourceSelection source = SceneSourceSelection.Create(
            index: 0,
            ActivityId,
            revision: 7,
            descriptorDigest: new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(SourceId, "desktop"));
        return CreateInstruction(source);
    }

    private static SceneRemoteChildInstruction CreateInstruction(
        SceneSourceSelection source,
        SceneSourceDisposition disposition =
            SceneSourceDisposition.PreserveSource,
        SceneConflictPolicy conflictPolicy =
            SceneConflictPolicy.RequireEmpty,
        SceneReplaceTargetSnapshot? replaceTarget = null)
    {
        SceneActivityPlan plan = SceneActivityPlan.Place(
            source.ActivityId,
            ActivityPlacement.On(TargetId, "focus"),
            disposition,
            conflictPolicy);
        SceneApplyItemPreview item = replaceTarget is null
            ? SceneApplyItemPreview.TransferToEmpty(
                plan,
                source,
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"))
            : SceneApplyItemPreview.Replace(
                plan,
                source,
                replaceTarget,
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        return SceneRemoteChildInstruction.Create(
            CoordinatorId,
            SceneId.Parse("abababab-abab-abab-abab-abababababab"),
            sceneRevision: 5,
            sceneDigest: new string('C', 64),
            previewFingerprint: new string('D', 64),
            OperationId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            CorrelationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            acceptedAt: Now,
            item);
    }

    private sealed class RoutedFixture : IDisposable
    {
        private readonly FixedClock clock = new(Now);
        private readonly SceneControlPeer peer;
        private readonly ReplaceEndpoint? replaceEndpoint;
        private readonly SceneActivityOperationEndpoint sourceEndpoint;
        private readonly ActivityInstance sourceActivity;
        private readonly ActivityInstance? targetActivity;
        private readonly SceneApplyPreflightEndpoint targetPreflight;

        public RoutedFixture(
            bool withReplace = false,
            bool dropActivityAcknowledgement = false,
            bool dropReplaceAcknowledgement = false,
            SceneControlDeliveryStatus slotStatus =
                SceneControlDeliveryStatus.Acknowledged)
        {
            var sourceCatalog = new InMemoryActivityCatalog();
            var targetCatalog = new InMemoryActivityCatalog();
            var sourceAdapters = new ActivityAdapterRegistry(
                [new WorkspaceNoteAdapter()]);
            var targetAdapters = new ActivityAdapterRegistry(
                [new WorkspaceNoteAdapter()]);
            SourceNode = new FlowspanNode(
                SourceId,
                "Source",
                clock,
                sourceCatalog,
                new InMemoryOperationJournal(),
                sourceAdapters,
                NullReceiptSink.Instance);
            TargetNode = new FlowspanNode(
                TargetId,
                "Target",
                clock,
                targetCatalog,
                new InMemoryOperationJournal(),
                targetAdapters,
                NullReceiptSink.Instance);
            sourceActivity = ActivityInstance.Active(
                ActivityDescriptor.Create(
                    ActivityId,
                    ActivityKind.Parse("workspace.note/v1"),
                    SourceId,
                    "source-title-canary",
                    JsonSerializer.Serialize(new
                    {
                        text = "source-payload-canary",
                    })),
                ActivityPlacement.On(SourceId, "desktop"),
                revision: 7);
            Assert.True(SourceNode.AddLocalActivity(sourceActivity));

            if (withReplace)
            {
                targetActivity = ActivityInstance.Active(
                    ActivityDescriptor.Create(
                        OccupantId,
                        sourceActivity.Descriptor.Kind,
                        TargetId,
                        "target-title-canary",
                        JsonSerializer.Serialize(new
                        {
                            text = "target-payload-canary",
                        })),
                    ActivityPlacement.On(TargetId, "focus"),
                    revision: 9);
                Assert.True(TargetNode.AddLocalActivity(targetActivity));
                replaceEndpoint = new ReplaceEndpoint(
                    TargetId,
                    clock,
                    targetCatalog,
                    new InMemoryOperationJournal(),
                    targetAdapters,
                    new InMemoryReplaceStateStore(),
                    new DeterministicUndoCapsuleIdSource(
                    [
                        UndoCapsuleId.Parse(
                            "77777777-7777-7777-7777-777777777777"),
                    ]),
                    NullReceiptSink.Instance);
                replaceEndpoint.SetPeerGrant(
                    SourceId,
                    CapabilityGrant.Of(Capability.ActivityReplace));
            }

            var sourcePreflight = new SceneApplyPreflightEndpoint(
                SourceId,
                clock,
                sourceCatalog,
                sourceAdapters,
                NeverUndoAvailable.Instance);
            targetPreflight = new SceneApplyPreflightEndpoint(
                TargetId,
                clock,
                targetCatalog,
                targetAdapters,
                withReplace
                    ? AlwaysUndoAvailable.Instance
                    : NeverUndoAvailable.Instance);
            sourceEndpoint = new SceneActivityOperationEndpoint(
                SourceNode,
                sourcePreflight,
                clock: clock);
            sourceEndpoint.SetPeerGrant(
                CoordinatorId,
                CapabilityGrant.Of(Capability.SceneApply));
            sourceEndpoint.SetPeerGrant(
                TargetId,
                CapabilityGrant.Of(Capability.ActivityReceive));
            targetPreflight.SetPeerGrant(
                CoordinatorId,
                CapabilityGrant.Of(Capability.SceneApply));
            targetPreflight.SetPeerGrant(
                SourceId,
                CapabilityGrant.Of(Capability.SceneApply));
            TargetNode.SetPeerGrant(
                SourceId,
                CapabilityGrant.Of(Capability.ActivityOffer));
            Routes = new DirectRouteDirectory(
                TargetNode,
                targetPreflight,
                replaceEndpoint,
                dropActivityAcknowledgement,
                dropReplaceAcknowledgement,
                slotStatus);
            var routedPort = new RoutedSceneActivityOperationPort(
                clock,
                sourceEndpoint,
                Routes);
            peer = new SceneControlPeer(
                clock,
                sourceEndpoint,
                routedPort,
                new InMemorySceneRemoteChildJournal());
        }

        public DirectRouteDirectory Routes { get; }

        public FlowspanNode SourceNode { get; }

        public FlowspanNode TargetNode { get; }

        public void DenySourceReceive() => sourceEndpoint.SetPeerGrant(
            TargetId,
            CapabilityGrant.None);

        public void DenyTargetSceneApply() => targetPreflight.SetPeerGrant(
            SourceId,
            CapabilityGrant.None);

        public void DenyTargetOffer() => TargetNode.SetPeerGrant(
            SourceId,
            CapabilityGrant.None);

        public async ValueTask<SceneActivityOperationResult> ExecuteAsync(
            SceneSourceDisposition disposition,
            SceneConflictPolicy conflictPolicy)
        {
            SceneSourceSelection source = SceneSourceSelection.Create(
                index: 0,
                sourceActivity.Descriptor.Id,
                sourceActivity.Revision,
                sourceActivity.Descriptor.DescriptorDigest,
                sourceActivity.Descriptor.Kind,
                sourceActivity.Placement);
            SceneReplaceTargetSnapshot? target = targetActivity is null
                ? null
                : SceneReplaceTargetSnapshot.Create(
                    targetActivity.Descriptor.Id,
                    targetActivity.Revision,
                    targetActivity.Descriptor.DescriptorDigest,
                    targetActivity.Descriptor.Kind,
                    targetActivity.Placement);
            return await peer.ExecuteChildAsync(
                CoordinatorId,
                CreateInstruction(source, disposition, conflictPolicy, target),
                CancellationToken.None);
        }

        public void Dispose() => replaceEndpoint?.Dispose();
    }

    private sealed class CountingOperationPort(
        IClock clock,
        OperationStatus status = OperationStatus.Committed,
        FailureCode failureCode = FailureCode.None) :
        ISceneActivityOperationPort
    {
        public int CallCount { get; private set; }

        public ValueTask<SceneActivityOperationResult> ExecuteAsync(
            SceneActivityPreparation preparation,
            CancellationToken cancellationToken)
        {
            CallCount++;
            SceneApplyItemPreview item = preparation.Item;
            SceneSourceSelection source = item.Source!;
            OperationKind kind = item.Action switch
            {
                SceneApplyAction.Handoff => OperationKind.Handoff,
                SceneApplyAction.Move => OperationKind.Move,
                SceneApplyAction.Replace => OperationKind.Replace,
                _ => throw new InvalidOperationException(),
            };
            return ValueTask.FromResult(SceneActivityOperationResult.Create(
                OperationReceipt.FromRecordedResult(
                    item.ChildOperationId,
                    item.ChildCorrelationId,
                    kind,
                    status,
                    source.DeviceId,
                    item.Destination.DeviceId,
                    item.ActivityId,
                    source.Kind,
                    source.DescriptorDigest,
                    clock.UtcNow,
                    failureCode),
                undoCapsule: null));
        }
    }

    private sealed class BlockingOperationPort(IClock clock) :
        ISceneActivityOperationPort
    {
        private readonly TaskCompletionSource entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);

        public async ValueTask<SceneActivityOperationResult> ExecuteAsync(
            SceneActivityPreparation preparation,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            SceneApplyItemPreview item = preparation.Item;
            SceneSourceSelection source = item.Source!;
            return SceneActivityOperationResult.Create(
                OperationReceipt.FromRecordedResult(
                    item.ChildOperationId,
                    item.ChildCorrelationId,
                    OperationKind.Handoff,
                    OperationStatus.Committed,
                    source.DeviceId,
                    item.Destination.DeviceId,
                    item.ActivityId,
                    source.Kind,
                    source.DescriptorDigest,
                    clock.UtcNow,
                    FailureCode.None),
                undoCapsule: null);
        }

        public Task WaitUntilEnteredAsync() => entered.Task;

        public void Release() => release.TrySetResult();
    }

    private sealed class MismatchedOperationPort(IClock clock) :
        ISceneActivityOperationPort
    {
        public int CallCount { get; private set; }

        public ValueTask<SceneActivityOperationResult> ExecuteAsync(
            SceneActivityPreparation preparation,
            CancellationToken cancellationToken)
        {
            CallCount++;
            SceneApplyItemPreview item = preparation.Item;
            SceneSourceSelection source = item.Source!;
            return ValueTask.FromResult(SceneActivityOperationResult.Create(
                OperationReceipt.FromRecordedResult(
                    OperationId.Parse(
                        "12121212-1212-1212-1212-121212121212"),
                    item.ChildCorrelationId,
                    OperationKind.Handoff,
                    OperationStatus.Committed,
                    source.DeviceId,
                    item.Destination.DeviceId,
                    item.ActivityId,
                    source.Kind,
                    source.DescriptorDigest,
                    clock.UtcNow,
                    FailureCode.None),
                undoCapsule: null));
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class NeverUndoAvailable : ISceneReplaceUndoAvailability
    {
        private NeverUndoAvailable()
        {
        }

        public static NeverUndoAvailable Instance { get; } = new();

        public bool HasDurableUndoFor(ActivityInstance target) => false;
    }

    private sealed class AlwaysUndoAvailable : ISceneReplaceUndoAvailability
    {
        private AlwaysUndoAvailable()
        {
        }

        public static AlwaysUndoAvailable Instance { get; } = new();

        public bool HasDurableUndoFor(ActivityInstance target) => true;
    }

    private sealed class InMemoryPayloadStore(byte[]? initial = null) :
        ISceneRemoteChildStatePayloadStore
    {
        private byte[]? payload = initial?.ToArray();

        public byte[] Snapshot => payload?.ToArray()
            ?? throw new InvalidOperationException(
                "The payload store has no saved state.");

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(payload?.ToArray());
        }

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> candidate,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            payload = candidate.ToArray();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AmbiguousPayloadStore(int failAfterSaveNumber) :
        ISceneRemoteChildStatePayloadStore
    {
        private byte[]? payload;
        private int saveCount;

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(payload?.ToArray());
        }

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> candidate,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            payload = candidate.ToArray();
            if (Interlocked.Increment(ref saveCount) == failAfterSaveNumber)
            {
                throw new IOException("Injected post-write ambiguity.");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class DirectRouteDirectory(
        FlowspanNode targetNode,
        SceneApplyPreflightEndpoint targetPreflight,
        ReplaceEndpoint? replaceEndpoint = null,
        bool dropActivityAcknowledgement = false,
        bool dropReplaceAcknowledgement = false,
        SceneControlDeliveryStatus slotStatus =
            SceneControlDeliveryStatus.Acknowledged) :
        ISceneOperationRouteDirectory
    {
        private readonly CountingActivityChannel activityChannel =
            new(targetNode, dropActivityAcknowledgement);
        private readonly DirectSlotChannel slotChannel =
            new(targetPreflight, slotStatus);
        private readonly CountingReplaceChannel? replaceChannel =
            replaceEndpoint is null
                ? null
                : new CountingReplaceChannel(
                    replaceEndpoint,
                    dropReplaceAcknowledgement);

        public int ActivitySendCount => activityChannel.SendCount;

        public int ReplaceSendCount => replaceChannel?.SendCount ?? 0;

        public IReadOnlyList<DeviceId> GetSceneParticipantDeviceIds() =>
            [targetNode.DeviceId];

        public bool TryGetChannel(
            DeviceId peerDeviceId,
            out IActivityChannel? channel)
        {
            channel = peerDeviceId == targetNode.DeviceId
                ? activityChannel
                : null;
            return channel is not null;
        }

        public bool TryGetReplaceChannel(
            DeviceId peerDeviceId,
            out IReplaceChannel? channel)
        {
            channel = peerDeviceId == targetNode.DeviceId
                ? replaceChannel
                : null;
            return channel is not null;
        }

        public bool TryGetSceneExactSlotChannel(
            DeviceId peerDeviceId,
            out ISceneExactSlotChannel? channel)
        {
            channel = peerDeviceId == targetNode.DeviceId
                ? slotChannel
                : null;
            return channel is not null;
        }

        public bool TryGetSceneSourceLookupChannel(
            DeviceId peerDeviceId,
            out ISceneSourceLookupChannel? channel)
        {
            channel = null;
            return false;
        }

        public bool TryGetSceneChildOperationChannel(
            DeviceId peerDeviceId,
            out ISceneChildOperationChannel? channel)
        {
            channel = null;
            return false;
        }
    }

    private sealed class CountingReplaceChannel(
        IReplacePeer target,
        bool dropAcknowledgement) :
        IReplaceChannel
    {
        public DeviceId TargetDeviceId => target.DeviceId;

        public int SendCount { get; private set; }

        public async ValueTask<ReplaceDeliveryResult> SendAsync(
            DeviceId senderDeviceId,
            ReplaceActivityCommand command,
            CancellationToken cancellationToken)
        {
            SendCount++;
            ReplaceOperationResult result = await target.ReplaceAsync(
                senderDeviceId,
                command,
                cancellationToken);
            return dropAcknowledgement
                ? ReplaceDeliveryResult.AcknowledgementLost
                : ReplaceDeliveryResult.Acknowledged(result);
        }
    }

    private sealed class CountingActivityChannel(
        IActivityPeer target,
        bool dropAcknowledgement) :
        IActivityChannel
    {
        public DeviceId TargetDeviceId => target.DeviceId;

        public int SendCount { get; private set; }

        public async ValueTask<ActivityDeliveryResult> SendAsync(
            DeviceId senderDeviceId,
            ActivityTransferOffer offer,
            CancellationToken cancellationToken)
        {
            SendCount++;
            OperationReceipt receipt = await target.ReceiveActivityAsync(
                senderDeviceId,
                offer,
                cancellationToken);
            return dropAcknowledgement
                ? ActivityDeliveryResult.AcknowledgementLost
                : ActivityDeliveryResult.Acknowledged(receipt);
        }
    }

    private sealed class DirectSlotChannel(
        SceneApplyPreflightEndpoint target,
        SceneControlDeliveryStatus deliveryStatus) : ISceneExactSlotChannel
    {
        public DeviceId TargetDeviceId => target.DeviceId;

        public async ValueTask<SceneExactSlotDeliveryResult> InspectSlotAsync(
            DeviceId requestingDeviceId,
            SceneExactSlotQuery query,
            CancellationToken cancellationToken)
        {
            if (deliveryStatus != SceneControlDeliveryStatus.Acknowledged)
            {
                return deliveryStatus switch
                {
                    SceneControlDeliveryStatus.NotDelivered =>
                        SceneExactSlotDeliveryResult.NotDelivered,
                    SceneControlDeliveryStatus.AcknowledgementLost =>
                        SceneExactSlotDeliveryResult.AcknowledgementLost,
                    SceneControlDeliveryStatus.ProtocolUnsupported =>
                        SceneExactSlotDeliveryResult.ProtocolUnsupported,
                    _ => throw new InvalidOperationException(),
                };
            }

            SceneExactSlotInspection result = await target.InspectExactSlotAsync(
                requestingDeviceId,
                query.Item,
                query.Source,
                query.Context,
                cancellationToken);
            return SceneExactSlotDeliveryResult.Acknowledged(result);
        }
    }
}
