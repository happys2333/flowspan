using System.Net;
using System.Net.Sockets;
using Flowspan.Application;
using Flowspan.Diagnostics;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopActivityRuntimeTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 14, 17, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId SourceId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId TargetId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task CreatesOnlyBoundedPortableNotesAfterProtectedIdentityInitialization()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(SourceId, "Source");
        var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        await using var runtime = CreateRuntime(identity, trust);

        await runtime.InitializeAsync();
        DesktopActivitySnapshot created = runtime.CreateWorkspaceNote(
            "Plan",
            "portable body",
            ActivitySensitivity.Sensitive);

        DesktopActivitySnapshot snapshot = Assert.Single(runtime.GetActivities());
        Assert.Equal(created, snapshot);
        Assert.Equal("workspace.note/v1", snapshot.Kind);
        Assert.Equal(ActivitySensitivity.Sensitive, snapshot.Sensitivity);
        Assert.Equal(ActivityLifecycle.Active, snapshot.Lifecycle);
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.CreateWorkspaceNote(
            "Too large",
            new string('x', 16 * 1024 + 1),
            ActivitySensitivity.Normal));
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task SceneEndpointRequiresDurableRemoteChildJournal()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(SourceId, "Source");
        var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        await using var available = CreateRuntime(
            identity,
            trust,
            sceneRemoteChildStatePayloadStore:
                new MemorySceneRemoteChildStatePayloadStore());

        await available.InitializeAsync();

        AuthenticatedActivitySessionHandler handler =
            await available.GetSessionHandlerAsync();
        Assert.True(handler.IsSceneEndpointAvailable);
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task ProtectedSceneApplyRuntimePreviewsAndAppliesExactLocalNoChange()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(SourceId, "Source");
        var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        await using var runtime = CreateRuntime(
            identity,
            trust,
            sceneRemoteChildStatePayloadStore:
                new MemorySceneRemoteChildStatePayloadStore(),
            sceneApplyStatePayloadStore:
                new MemorySceneApplyStatePayloadStore());
        await runtime.InitializeAsync();
        DesktopActivitySnapshot activity = runtime.CreateWorkspaceNote(
            "Local Scene note",
            "SCENE-LOCAL-PAYLOAD-CANARY",
            ActivitySensitivity.Normal);
        ScenePlan scene = ScenePlan.Create(
            SceneId.Parse("abababab-abab-abab-abab-abababababab"),
            "Local no-change Scene",
            [
                SceneActivityPlan.Place(
                    activity.ActivityId,
                    ActivityPlacement.On(SourceId, "desktop"),
                    SceneSourceDisposition.PreserveSource,
                    SceneConflictPolicy.RequireEmpty),
            ]);
        DesktopActivityRuntime sceneService = runtime;

        SceneApplyPreview preview = await sceneService.PreviewSceneAsync(
            scene,
            [],
            observedGroupRevision: null);
        SceneApplyExecutionResult execution = await sceneService.ApplySceneAsync(
            scene,
            preview,
            SceneApplyApproval.Create(
                preview.Fingerprint,
                preview.RequiredReplaceConfirmations));

        Assert.True(sceneService.IsSceneApplyReady);
        Assert.Equal(SceneApplyAction.NoChange, Assert.Single(preview.Items).Action);
        SceneApplyResult result = Assert.IsType<SceneApplyResult>(execution.Result);
        Assert.Equal(SceneApplyOverallStatus.Completed, result.Status);
        Assert.Equal(
            SceneApplyItemOutcome.NoChange,
            Assert.Single(result.Items).Outcome);
        Assert.Equal(activity, Assert.Single(runtime.GetActivities()));
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task SceneApplyJournalFailureLeavesRemoteSceneEndpointAvailable()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(SourceId, "Source");
        var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        await using var runtime = CreateRuntime(
            identity,
            trust,
            sceneRemoteChildStatePayloadStore:
                new MemorySceneRemoteChildStatePayloadStore(),
            sceneApplyStatePayloadStore:
                new FailingSceneApplyStatePayloadStore());

        await runtime.InitializeAsync();

        Assert.True(runtime.IsReady);
        Assert.False(((IDesktopSceneApplyService)runtime).IsSceneApplyReady);
        AuthenticatedActivitySessionHandler handler =
            await runtime.GetSessionHandlerAsync();
        Assert.True(handler.IsSceneEndpointAvailable);
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task SceneJournalFailureLeavesActivityRuntimeReadyButSceneClosed()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(SourceId, "Source");
        var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        await using var runtime = CreateRuntime(
            identity,
            trust,
            sceneRemoteChildStatePayloadStore:
                new FailingSceneRemoteChildStatePayloadStore());

        await runtime.InitializeAsync();

        Assert.True(runtime.IsReady);
        AuthenticatedActivitySessionHandler handler =
            await runtime.GetSessionHandlerAsync();
        Assert.False(handler.IsSceneEndpointAvailable);
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task LocalReceiveGrantIsRequiredBeforeAnyOutboundPayload()
    {
        using DeviceIdentity source = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity target = DeviceIdentity.Generate(TargetId, "Target");
        var store = new InMemoryTrustStore();
        store.Register(new TrustRecord(
            target.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        var trust = new TrustSessionCoordinator(store);
        var receipts = new InMemoryReceiptSink();
        await using var runtime = CreateRuntime(
            source,
            trust,
            receiptSink: receipts);
        await runtime.InitializeAsync();
        DesktopActivitySnapshot activity = runtime.CreateWorkspaceNote(
            "Plan",
            "must not leave",
            ActivitySensitivity.Normal);

        OperationReceipt receipt = await runtime.HandoffAsync(
            activity.ActivityId,
            TargetId);

        Assert.Equal(OperationStatus.Rejected, receipt.Status);
        Assert.Equal(FailureCode.CapabilityDenied, receipt.FailureCode);
        Assert.Equal(receipt, Assert.Single(receipts.Snapshot()));
        Assert.Empty(runtime.GetTargets());
        Assert.Equal(ActivityLifecycle.Active, Assert.Single(runtime.GetActivities()).Lifecycle);
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task LocalReceiveGrantIsRequiredBeforeAnyMovePayload()
    {
        using DeviceIdentity source = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity target = DeviceIdentity.Generate(TargetId, "Target");
        var store = new InMemoryTrustStore();
        store.Register(new TrustRecord(
            target.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        var trust = new TrustSessionCoordinator(store);
        await using var runtime = CreateRuntime(source, trust);
        await runtime.InitializeAsync();
        DesktopActivitySnapshot activity = runtime.CreateWorkspaceNote(
            "Plan",
            "must not leave",
            ActivitySensitivity.Normal);

        OperationReceipt receipt = await runtime.MoveAsync(
            activity.ActivityId,
            TargetId);

        Assert.Equal(OperationKind.Move, receipt.Kind);
        Assert.Equal(OperationStatus.Rejected, receipt.Status);
        Assert.Equal(FailureCode.CapabilityDenied, receipt.FailureCode);
        Assert.Empty(runtime.GetTargets());
        Assert.Equal(ActivityLifecycle.Active, Assert.Single(runtime.GetActivities()).Lifecycle);
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task MoveWithoutLiveAuthenticatedChannelKeepsSourceActive()
    {
        using DeviceIdentity source = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity target = DeviceIdentity.Generate(TargetId, "Target");
        var store = new InMemoryTrustStore();
        store.Register(new TrustRecord(
            target.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        var trust = new TrustSessionCoordinator(store);
        await using var runtime = CreateRuntime(source, trust);
        await runtime.InitializeAsync();
        DesktopActivitySnapshot activity = runtime.CreateWorkspaceNote(
            "Plan",
            "must remain local",
            ActivitySensitivity.Normal);

        OperationReceipt receipt = await runtime.MoveAsync(
            activity.ActivityId,
            TargetId);

        Assert.Equal(OperationKind.Move, receipt.Kind);
        Assert.Equal(OperationStatus.Failed, receipt.Status);
        Assert.Equal(FailureCode.PeerUnavailable, receipt.FailureCode);
        Assert.Equal(activity, Assert.Single(runtime.GetActivities()));
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task AuthenticatedRuntimesExchangeNoteAndExposeOnlyEligibleLiveTarget()
    {
        using DeviceIdentity sourceIdentity = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity targetIdentity = DeviceIdentity.Generate(TargetId, "Peer desk");
        var sourceStore = new InMemoryTrustStore();
        sourceStore.Register(new TrustRecord(
            targetIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        var targetStore = new InMemoryTrustStore();
        targetStore.Register(new TrustRecord(
            sourceIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        var sourceTrust = new TrustSessionCoordinator(sourceStore);
        var targetTrust = new TrustSessionCoordinator(targetStore);
        await using var source = CreateRuntime(sourceIdentity, sourceTrust);
        await using var target = CreateRuntime(targetIdentity, targetTrust);
        await source.InitializeAsync();
        await target.InitializeAsync();
        AuthenticatedActivitySessionHandler sourceHandler =
            await source.GetSessionHandlerAsync();
        AuthenticatedActivitySessionHandler targetHandler =
            await target.GetSessionHandlerAsync();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                targetIdentity,
                new TrustRecord(
                    sourceIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                sourceIdentity,
                new TrustRecord(
                    targetIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]);
        await using AuthenticatedTcpControlConnection targetConnection = await accepting;
        using var stop = new CancellationTokenSource();
        Task sourceRun = sourceHandler.RunAsync(sourceConnection, stop.Token).AsTask();
        Task targetRun = targetHandler.RunAsync(targetConnection, stop.Token).AsTask();
        DesktopActivityTargetSnapshot liveTarget = Assert.Single(source.GetTargets());
        Assert.Equal("Peer desk", liveTarget.DisplayName);
        DesktopActivitySnapshot activity = source.CreateWorkspaceNote(
            "Release plan",
            "portable body",
            ActivitySensitivity.Normal);

        OperationReceipt receipt = await source.HandoffAsync(
            activity.ActivityId,
            liveTarget.DeviceId);

        Assert.True(receipt.IsSuccess);
        Assert.Equal(ActivityLifecycle.Active, Assert.Single(source.GetActivities()).Lifecycle);
        Assert.Equal("Release plan", Assert.Single(target.GetActivities()).Title);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => targetRun);
        Assert.Empty(source.GetTargets());
        await sourceTrust.DisposeAsync();
        await targetTrust.DisposeAsync();
    }

    [Fact]
    public async Task AuthenticatedRuntimesMoveOnlyAfterVerifiedTargetReceipt()
    {
        using DeviceIdentity sourceIdentity = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity targetIdentity = DeviceIdentity.Generate(TargetId, "Peer desk");
        var sourceStore = new InMemoryTrustStore();
        sourceStore.Register(new TrustRecord(
            targetIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        var targetStore = new InMemoryTrustStore();
        targetStore.Register(new TrustRecord(
            sourceIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        var sourceTrust = new TrustSessionCoordinator(sourceStore);
        var targetTrust = new TrustSessionCoordinator(targetStore);
        await using var source = CreateRuntime(sourceIdentity, sourceTrust);
        await using var target = CreateRuntime(targetIdentity, targetTrust);
        await source.InitializeAsync();
        await target.InitializeAsync();
        AuthenticatedActivitySessionHandler sourceHandler =
            await source.GetSessionHandlerAsync();
        AuthenticatedActivitySessionHandler targetHandler =
            await target.GetSessionHandlerAsync();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                targetIdentity,
                new TrustRecord(
                    sourceIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                sourceIdentity,
                new TrustRecord(
                    targetIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]);
        await using AuthenticatedTcpControlConnection targetConnection = await accepting;
        using var stop = new CancellationTokenSource();
        Task sourceRun = sourceHandler.RunAsync(sourceConnection, stop.Token).AsTask();
        Task targetRun = targetHandler.RunAsync(targetConnection, stop.Token).AsTask();
        DesktopActivityTargetSnapshot liveTarget = Assert.Single(source.GetTargets());
        DesktopActivitySnapshot activity = source.CreateWorkspaceNote(
            "Release plan",
            "portable body",
            ActivitySensitivity.Normal);

        OperationReceipt receipt = await source.MoveAsync(
            activity.ActivityId,
            liveTarget.DeviceId);

        Assert.True(receipt.IsSuccess);
        Assert.Equal(OperationKind.Move, receipt.Kind);
        Assert.Empty(source.GetActivities());
        Assert.Equal("Release plan", Assert.Single(target.GetActivities()).Title);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => targetRun);
        await sourceTrust.DisposeAsync();
        await targetTrust.DisposeAsync();
    }

    [Fact]
    public async Task AuthenticatedTargetRejectionKeepsMoveSourceActive()
    {
        using DeviceIdentity sourceIdentity = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity targetIdentity = DeviceIdentity.Generate(TargetId, "Peer desk");
        var sourceStore = new InMemoryTrustStore();
        sourceStore.Register(new TrustRecord(
            targetIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        var targetStore = new InMemoryTrustStore();
        targetStore.Register(new TrustRecord(
            sourceIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        var sourceTrust = new TrustSessionCoordinator(sourceStore);
        var targetTrust = new TrustSessionCoordinator(targetStore);
        await using var source = CreateRuntime(sourceIdentity, sourceTrust);
        await using var target = CreateRuntime(targetIdentity, targetTrust);
        await source.InitializeAsync();
        await target.InitializeAsync();
        AuthenticatedActivitySessionHandler sourceHandler =
            await source.GetSessionHandlerAsync();
        AuthenticatedActivitySessionHandler targetHandler =
            await target.GetSessionHandlerAsync();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                targetIdentity,
                new TrustRecord(
                    sourceIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                sourceIdentity,
                new TrustRecord(
                    targetIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]);
        await using AuthenticatedTcpControlConnection targetConnection = await accepting;
        using var stop = new CancellationTokenSource();
        Task sourceRun = sourceHandler.RunAsync(sourceConnection, stop.Token).AsTask();
        Task targetRun = targetHandler.RunAsync(targetConnection, stop.Token).AsTask();
        DesktopActivityTargetSnapshot liveTarget = Assert.Single(source.GetTargets());
        DesktopActivitySnapshot activity = source.CreateWorkspaceNote(
            "Release plan",
            "portable body",
            ActivitySensitivity.Normal);
        Assert.True(targetStore.TryUpdateCapabilities(
            SourceId,
            sourceIdentity.PublicIdentity.Fingerprint,
            CapabilityGrant.None));

        OperationReceipt receipt = await source.MoveAsync(
            activity.ActivityId,
            liveTarget.DeviceId);

        Assert.Equal(OperationKind.Move, receipt.Kind);
        Assert.Equal(OperationStatus.Rejected, receipt.Status);
        Assert.Equal(FailureCode.CapabilityDenied, receipt.FailureCode);
        Assert.Equal(activity, Assert.Single(source.GetActivities()));
        Assert.Empty(target.GetActivities());
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => targetRun);
        await sourceTrust.DisposeAsync();
        await targetTrust.DisposeAsync();
    }

    [Fact]
    public async Task AuthenticatedRuntimesHonorCurrentReplaceGrantWithoutMutation()
    {
        using DeviceIdentity sourceIdentity = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity targetIdentity = DeviceIdentity.Generate(TargetId, "Peer desk");
        var sourceStore = new InMemoryTrustStore();
        sourceStore.Register(new TrustRecord(
            targetIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        var targetStore = new InMemoryTrustStore();
        targetStore.Register(new TrustRecord(
            sourceIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReplace)));
        var sourceTrust = new TrustSessionCoordinator(sourceStore);
        var targetTrust = new TrustSessionCoordinator(targetStore);
        await using var source = CreateRuntime(
            sourceIdentity,
            sourceTrust,
            new MemoryReplaceStatePayloadStore());
        await using var target = CreateRuntime(
            targetIdentity,
            targetTrust,
            new MemoryReplaceStatePayloadStore());
        await source.InitializeAsync();
        await target.InitializeAsync();
        DesktopActivitySnapshot incoming = source.CreateWorkspaceNote(
            "Incoming note",
            "incoming body",
            ActivitySensitivity.Normal);
        DesktopActivitySnapshot existing = target.CreateWorkspaceNote(
            "Existing target",
            "target body",
            ActivitySensitivity.Normal);
        AuthenticatedActivitySessionHandler sourceHandler =
            await source.GetSessionHandlerAsync();
        AuthenticatedActivitySessionHandler targetHandler =
            await target.GetSessionHandlerAsync();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                targetIdentity,
                new TrustRecord(
                    sourceIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                sourceIdentity,
                new TrustRecord(
                    targetIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]);
        await using AuthenticatedTcpControlConnection targetConnection = await accepting;
        using var stop = new CancellationTokenSource();
        Task sourceRun = sourceHandler.RunAsync(sourceConnection, stop.Token).AsTask();
        Task targetRun = targetHandler.RunAsync(targetConnection, stop.Token).AsTask();

        DesktopReplaceTargetInventoryResult result =
            await source.GetReplaceTargetsAsync(incoming.ActivityId, TargetId);

        Assert.True(result.IsSuccess);
        Assert.True(
            ((IDesktopActivityService)source).IsDestructiveReplaceAvailable);
        Assert.True(
            ((IDesktopActivityService)target).IsDestructiveReplaceAvailable);
        DesktopReplaceTargetSnapshot snapshot = Assert.Single(result.Targets);
        Assert.Equal(existing.ActivityId, snapshot.ActivityId);
        Assert.Equal("Existing target", snapshot.Title);
        Assert.Equal(1, snapshot.Revision);
        Assert.True(await targetTrust.TryUpdateCapabilitiesAsync(
            SourceId,
            sourceIdentity.PublicIdentity.Fingerprint,
            CapabilityGrant.None));

        DesktopReplaceOperationResult denied = await source.ReplaceAsync(
            incoming.ActivityId,
            snapshot);

        Assert.Equal(ActivityDeliveryStatus.NotDelivered, denied.DeliveryStatus);
        Assert.Equal(FailureCode.CapabilityDenied, denied.FailureCode);
        Assert.Null(denied.Receipt);
        Assert.Null(denied.UndoCapsule);

        DesktopReplaceTargetInventoryResult revoked =
            await source.GetReplaceTargetsAsync(incoming.ActivityId, TargetId);

        Assert.Equal(FailureCode.CapabilityDenied, revoked.FailureCode);
        Assert.Empty(revoked.Targets);
        Assert.True(sourceHandler.TryGetReplaceChannel(
            TargetId,
            out IReplaceChannel? rawChannel));
        Assert.NotNull(rawChannel);
        ActivityDescriptor rawIncoming = ActivityDescriptor.Create(
            incoming.ActivityId,
            ActivityKind.Parse(incoming.Kind),
            SourceId,
            incoming.Title,
            "{\"text\":\"incoming body\"}");
        OperationContext rawContext = OperationContext.Create(
            OperationId.Parse("66666666-6666-6666-6666-666666666666"),
            CorrelationId.Parse("77777777-7777-7777-7777-777777777777"),
            Now.AddSeconds(30));

        ReplaceDeliveryResult rawDenied = await rawChannel.SendAsync(
            SourceId,
            ReplaceActivityCommand.Create(
                rawContext,
                snapshot.ActivityId,
                snapshot.Revision,
                snapshot.DescriptorDigest,
                rawIncoming,
                ActivityPlacement.On(TargetId, snapshot.PlacementSlot),
                Now.AddMinutes(10)),
            CancellationToken.None);

        Assert.Equal(ActivityDeliveryStatus.Acknowledged, rawDenied.Status);
        Assert.Equal(
            OperationStatus.Rejected,
            rawDenied.Result?.Receipt.Status);
        Assert.Equal(
            FailureCode.CapabilityDenied,
            rawDenied.Result?.Receipt.FailureCode);
        Assert.Null(rawDenied.Result?.UndoCapsule);
        ReplaceRecoveryRecord deniedRecord = Assert.Single(
            target.GetReplaceRecoveryState().Records);
        Assert.Equal(rawContext.OperationId, deniedRecord.OperationId);
        Assert.Equal(FailureCode.CapabilityDenied, deniedRecord.FailureCode);
        Assert.Equal(existing, Assert.Single(target.GetActivities()));
        Assert.Equal(incoming, Assert.Single(source.GetActivities()));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => targetRun);
        await sourceTrust.DisposeAsync();
        await targetTrust.DisposeAsync();
    }

    [Fact]
    public async Task ReplaceRevalidatesExactTargetBeforeSendingDestructiveCommand()
    {
        using DeviceIdentity sourceIdentity = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity targetIdentity = DeviceIdentity.Generate(TargetId, "Peer desk");
        var sourceStore = new InMemoryTrustStore();
        sourceStore.Register(new TrustRecord(
            targetIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        var targetStore = new InMemoryTrustStore();
        targetStore.Register(new TrustRecord(
            sourceIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReplace)));
        var sourceTrust = new TrustSessionCoordinator(sourceStore);
        var targetTrust = new TrustSessionCoordinator(targetStore);
        var sourceReplaceState = new MemoryReplaceStatePayloadStore();
        var targetReplaceState = new MemoryReplaceStatePayloadStore();
        UndoCapsule targetCapsule = await CreateCommittedReplaceStateAsync(
            targetReplaceState);
        await using var source = CreateRuntime(
            sourceIdentity,
            sourceTrust,
            sourceReplaceState);
        await using var target = CreateRuntime(
            targetIdentity,
            targetTrust,
            targetReplaceState);
        await source.InitializeAsync();
        await target.InitializeAsync();
        DesktopActivitySnapshot incoming = source.CreateWorkspaceNote(
            "Incoming note",
            "incoming body",
            ActivitySensitivity.Normal);
        AuthenticatedActivitySessionHandler sourceHandler =
            await source.GetSessionHandlerAsync();
        AuthenticatedActivitySessionHandler targetHandler =
            await target.GetSessionHandlerAsync();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                targetIdentity,
                new TrustRecord(
                    sourceIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                sourceIdentity,
                new TrustRecord(
                    targetIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]);
        await using AuthenticatedTcpControlConnection targetConnection = await accepting;
        using var stop = new CancellationTokenSource();
        using var operationTimeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(5));
        Task sourceRun = sourceHandler.RunAsync(sourceConnection, stop.Token).AsTask();
        Task targetRun = targetHandler.RunAsync(targetConnection, stop.Token).AsTask();
        try
        {
            DesktopReplaceTargetSnapshot staleTarget = Assert.Single(
                (await source.GetReplaceTargetsAsync(
                    incoming.ActivityId,
                    TargetId,
                    operationTimeout.Token)).Targets);
            UndoReplaceResult interveningUndo = await target.UndoReplaceAsync(
                targetCapsule.Id,
                operationTimeout.Token);
            Assert.True(interveningUndo.IsSuccess);
            var sourceBefore = source.GetActivities();
            var targetBefore = target.GetActivities();
            DesktopReplaceRecoveryResult recoveryBefore =
                target.GetReplaceRecoveryState();

            DesktopReplaceOperationResult result = await source.ReplaceAsync(
                incoming.ActivityId,
                staleTarget,
                operationTimeout.Token);

            Assert.Equal(ActivityDeliveryStatus.NotDelivered, result.DeliveryStatus);
            Assert.Equal(FailureCode.RevisionConflict, result.FailureCode);
            Assert.Null(result.OperationId);
            Assert.Null(result.CorrelationId);
            Assert.Null(result.Receipt);
            Assert.Null(result.UndoCapsule);
            Assert.Equal(sourceBefore.ToArray(), source.GetActivities().ToArray());
            Assert.Equal(targetBefore.ToArray(), target.GetActivities().ToArray());
            DesktopReplaceRecoveryResult recoveryAfter =
                target.GetReplaceRecoveryState();
            Assert.Equal(
                recoveryBefore.Records.ToArray(),
                recoveryAfter.Records.ToArray());
            Assert.Equal(
                recoveryBefore.UndoableCapsuleIds.ToArray(),
                recoveryAfter.UndoableCapsuleIds.ToArray());
        }
        finally
        {
            stop.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => targetRun);
        await sourceTrust.DisposeAsync();
        await targetTrust.DisposeAsync();
    }

    [Fact]
    public async Task AuthenticatedDesktopReplaceProjectsReceiptAndUndoCapsule()
    {
        using DeviceIdentity sourceIdentity = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity targetIdentity = DeviceIdentity.Generate(TargetId, "Peer desk");
        var sourceStore = new InMemoryTrustStore();
        sourceStore.Register(new TrustRecord(
            targetIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        var targetStore = new InMemoryTrustStore();
        targetStore.Register(new TrustRecord(
            sourceIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReplace)));
        var sourceTrust = new TrustSessionCoordinator(sourceStore);
        var targetTrust = new TrustSessionCoordinator(targetStore);
        await using var source = CreateRuntime(
            sourceIdentity,
            sourceTrust,
            new MemoryReplaceStatePayloadStore());
        await using var target = CreateRuntime(
            targetIdentity,
            targetTrust,
            new MemoryReplaceStatePayloadStore());
        await source.InitializeAsync();
        await target.InitializeAsync();
        DesktopActivitySnapshot incoming = source.CreateWorkspaceNote(
            "Incoming note",
            "incoming body",
            ActivitySensitivity.Normal);
        DesktopActivitySnapshot existing = target.CreateWorkspaceNote(
            "Existing target",
            "target body",
            ActivitySensitivity.Normal);
        AuthenticatedActivitySessionHandler sourceHandler =
            await source.GetSessionHandlerAsync();
        AuthenticatedActivitySessionHandler targetHandler =
            await target.GetSessionHandlerAsync();
        Assert.True(sourceHandler.IsReplaceEndpointAvailable);
        Assert.True(targetHandler.IsReplaceEndpointAvailable);
        Assert.True(((IDesktopActivityService)source).IsDestructiveReplaceAvailable);
        Assert.True(((IDesktopActivityService)target).IsDestructiveReplaceAvailable);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                targetIdentity,
                new TrustRecord(
                    sourceIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                sourceIdentity,
                new TrustRecord(
                    targetIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]);
        await using AuthenticatedTcpControlConnection targetConnection = await accepting;
        using var stop = new CancellationTokenSource();
        using var operationTimeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(5));
        Task sourceRun = sourceHandler.RunAsync(sourceConnection, stop.Token).AsTask();
        Task targetRun = targetHandler.RunAsync(targetConnection, stop.Token).AsTask();
        try
        {
            DesktopReplaceTargetSnapshot selected = Assert.Single(
                (await source.GetReplaceTargetsAsync(
                    incoming.ActivityId,
                    TargetId,
                    operationTimeout.Token)).Targets);
            int targetChangeCount = 0;
            target.Changed += () => targetChangeCount++;

            DesktopReplaceOperationResult result = await source.ReplaceAsync(
                incoming.ActivityId,
                selected,
                operationTimeout.Token);

            Assert.Equal(ActivityDeliveryStatus.Acknowledged, result.DeliveryStatus);
            Assert.Equal(OperationStatus.Committed, result.Status);
            Assert.Equal(FailureCode.None, result.FailureCode);
            Assert.True(result.IsSuccess);
            OperationReceipt receipt = Assert.IsType<OperationReceipt>(result.Receipt);
            UndoCapsuleReference capsule =
                Assert.IsType<UndoCapsuleReference>(result.UndoCapsule);
            Assert.Equal(result.OperationId, receipt.OperationId);
            Assert.Equal(result.CorrelationId, receipt.CorrelationId);
            Assert.Equal(OperationKind.Replace, receipt.Kind);
            Assert.Equal(SourceId, receipt.SourceDeviceId);
            Assert.Equal(TargetId, receipt.TargetDeviceId);
            Assert.Equal(incoming.ActivityId, receipt.ActivityId);
            Assert.Equal(selected.ActivityId, capsule.TargetActivityId);
            Assert.Equal(selected.Revision, capsule.ExpectedTargetRevision);
            Assert.Equal(selected.DescriptorDigest, capsule.TargetDescriptorDigest);
            Assert.Equal(incoming.ActivityId, capsule.IncomingActivityId);
            Assert.Equal(Now.Add(ReplaceEndpoint.MaximumUndoRetention), capsule.ExpiresAt);
            Assert.Equal(incoming, Assert.Single(source.GetActivities()));
            DesktopActivitySnapshot replacement = Assert.Single(target.GetActivities());
            Assert.Equal(incoming.ActivityId, replacement.ActivityId);
            Assert.Equal(incoming.Title, replacement.Title);
            Assert.NotEqual(existing.ActivityId, replacement.ActivityId);
            DesktopReplaceRecoveryResult recovery = target.GetReplaceRecoveryState();
            ReplaceRecoveryRecord record = Assert.Single(recovery.Records);
            Assert.Equal(OperationStatus.Committed, record.Status);
            Assert.Equal(capsule.Id, record.CapsuleId);
            Assert.Contains(capsule.Id, recovery.UndoableCapsuleIds);
            Assert.True(targetChangeCount > 0);
        }
        finally
        {
            stop.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => targetRun);
        await sourceTrust.DisposeAsync();
        await targetTrust.DisposeAsync();
    }

    [Fact]
    public async Task UnresolvedTargetRecoveryRejectsNewReplaceWithoutNewJournalEntry()
    {
        using DeviceIdentity sourceIdentity = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity targetIdentity = DeviceIdentity.Generate(TargetId, "Peer desk");
        var sourceStore = new InMemoryTrustStore();
        sourceStore.Register(new TrustRecord(
            targetIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReceive)));
        var targetStore = new InMemoryTrustStore();
        targetStore.Register(new TrustRecord(
            sourceIdentity.PublicIdentity,
            Now,
            CapabilityGrant.Of(Capability.ActivityReplace)));
        var sourceTrust = new TrustSessionCoordinator(sourceStore);
        var targetTrust = new TrustSessionCoordinator(targetStore);
        var sourceReplaceState = new MemoryReplaceStatePayloadStore();
        var targetReplaceState = new MemoryReplaceStatePayloadStore();
        OperationId unresolvedOperationId =
            OperationId.Parse("88888888-8888-8888-8888-888888888888");
        using (PersistentReplaceStateStore state =
               await PersistentReplaceStateStore.OpenAsync(targetReplaceState))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await state.ExecuteOnceAsync(
                    unresolvedOperationId,
                    new string('D', 64),
                    _ => ValueTask.FromException<OperationReceipt>(
                        new InvalidOperationException("Injected pending boundary.")),
                    CancellationToken.None));
        }

        await using var source = CreateRuntime(
            sourceIdentity,
            sourceTrust,
            sourceReplaceState);
        await using var target = CreateRuntime(
            targetIdentity,
            targetTrust,
            targetReplaceState);
        await source.InitializeAsync();
        await target.InitializeAsync();
        DesktopActivitySnapshot incoming = source.CreateWorkspaceNote(
            "Incoming note",
            "incoming body",
            ActivitySensitivity.Normal);
        DesktopActivitySnapshot existing = target.CreateWorkspaceNote(
            "Existing target",
            "target body",
            ActivitySensitivity.Normal);
        AuthenticatedActivitySessionHandler sourceHandler =
            await source.GetSessionHandlerAsync();
        AuthenticatedActivitySessionHandler targetHandler =
            await target.GetSessionHandlerAsync();
        Assert.True(targetHandler.IsReplaceEndpointAvailable);
        Assert.False(((IDesktopActivityService)target).IsDestructiveReplaceAvailable);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                targetIdentity,
                new TrustRecord(
                    sourceIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                sourceIdentity,
                new TrustRecord(
                    targetIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [new ProtocolVersion(1, 0)]);
        await using AuthenticatedTcpControlConnection targetConnection = await accepting;
        using var stop = new CancellationTokenSource();
        using var operationTimeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(5));
        Task sourceRun = sourceHandler.RunAsync(sourceConnection, stop.Token).AsTask();
        Task targetRun = targetHandler.RunAsync(targetConnection, stop.Token).AsTask();
        try
        {
            DesktopReplaceTargetSnapshot selected = Assert.Single(
                (await source.GetReplaceTargetsAsync(
                    incoming.ActivityId,
                    TargetId,
                    operationTimeout.Token)).Targets);
            DesktopReplaceRecoveryResult before = target.GetReplaceRecoveryState();

            DesktopReplaceOperationResult result = await source.ReplaceAsync(
                incoming.ActivityId,
                selected,
                operationTimeout.Token);

            Assert.Equal(ActivityDeliveryStatus.Acknowledged, result.DeliveryStatus);
            Assert.Equal(OperationStatus.Failed, result.Status);
            Assert.Equal(FailureCode.OperationInProgress, result.FailureCode);
            Assert.Null(result.UndoCapsule);
            Assert.Equal(incoming, Assert.Single(source.GetActivities()));
            Assert.Equal(existing, Assert.Single(target.GetActivities()));
            DesktopReplaceRecoveryResult after = target.GetReplaceRecoveryState();
            Assert.Equal(before.Records.ToArray(), after.Records.ToArray());
            Assert.Equal(
                unresolvedOperationId,
                Assert.Single(after.Records).OperationId);
        }
        finally
        {
            stop.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => targetRun);
        await sourceTrust.DisposeAsync();
        await targetTrust.DisposeAsync();
    }

    [Fact]
    public async Task MissingReceiveGrantRejectsInventoryAndReplaceBeforeChannelLookup()
    {
        using DeviceIdentity sourceIdentity = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity targetIdentity = DeviceIdentity.Generate(TargetId, "Peer desk");
        var sourceStore = new InMemoryTrustStore();
        sourceStore.Register(new TrustRecord(
            targetIdentity.PublicIdentity,
            Now,
            CapabilityGrant.None));
        var sourceTrust = new TrustSessionCoordinator(sourceStore);
        await using var source = CreateRuntime(
            sourceIdentity,
            sourceTrust,
            new MemoryReplaceStatePayloadStore());
        await source.InitializeAsync();
        DesktopActivitySnapshot incoming = source.CreateWorkspaceNote(
            "Incoming note",
            "incoming body",
            ActivitySensitivity.Normal);

        DesktopReplaceTargetInventoryResult result =
            await source.GetReplaceTargetsAsync(incoming.ActivityId, TargetId);

        Assert.Equal(FailureCode.CapabilityDenied, result.FailureCode);
        Assert.Empty(result.Targets);
        var selected = new DesktopReplaceTargetSnapshot(
            TargetId,
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Unavailable target",
            "workspace.note/v1",
            1,
            new string('A', 64),
            "desktop");

        DesktopReplaceOperationResult replace = await source.ReplaceAsync(
            incoming.ActivityId,
            selected);

        Assert.Equal(ActivityDeliveryStatus.NotDelivered, replace.DeliveryStatus);
        Assert.Equal(FailureCode.CapabilityDenied, replace.FailureCode);
        Assert.Null(replace.OperationId);
        Assert.Null(replace.CorrelationId);
        Assert.Null(replace.Receipt);
        Assert.Null(replace.UndoCapsule);
        Assert.Equal(incoming, Assert.Single(source.GetActivities()));
        await sourceTrust.DisposeAsync();
    }

    [Fact]
    public async Task LoadsProtectedReplaceRecoveryAndComposesDestructiveEndpoint()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        ActivityDescriptor incoming = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            SourceId,
            "Secret title",
            "{\"text\":\"secret body\"}");
        OperationId operationId =
            OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        CorrelationId correlationId =
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        using (PersistentReplaceStateStore state =
               await PersistentReplaceStateStore.OpenAsync(payloadStore))
        {
            await state.ExecuteOnceAsync(
                operationId,
                new string('A', 64),
                _ => ValueTask.FromResult(OperationReceipt.Rejected(
                    operationId,
                    correlationId,
                    OperationKind.Replace,
                    SourceId,
                    TargetId,
                    incoming,
                    Now,
                    FailureCode.CapabilityDenied)),
                CancellationToken.None);
        }

        using DeviceIdentity identity = DeviceIdentity.Generate(TargetId, "Target");
        var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        await using var runtime = CreateRuntime(identity, trust, payloadStore);

        await runtime.InitializeAsync();

        DesktopReplaceRecoveryResult recovery = runtime.GetReplaceRecoveryState();
        Assert.True(recovery.IsAvailable);
        ReplaceRecoveryRecord record = Assert.Single(recovery.Records);
        Assert.Equal(operationId, record.OperationId);
        Assert.Equal(OperationStatus.Rejected, record.Status);
        Assert.Equal(FailureCode.CapabilityDenied, record.FailureCode);
        AuthenticatedActivitySessionHandler handler =
            await runtime.GetSessionHandlerAsync();
        Assert.True(handler.IsReplaceEndpointAvailable);
        Assert.True(((IDesktopActivityService)runtime).IsDestructiveReplaceAvailable);
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task StartupReconstructsExactSemanticReplacementAndExposesPeerEndpoint()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        UndoCapsule capsule = await CreateCommittedReplaceStateAsync(payloadStore);

        using DeviceIdentity identity = DeviceIdentity.Generate(TargetId, "Target");
        var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        await using var runtime = CreateRuntime(identity, trust, payloadStore);

        await runtime.InitializeAsync();

        DesktopActivitySnapshot activity = Assert.Single(runtime.GetActivities());
        Assert.Equal(capsule.ReplacementActivity.Descriptor.Id, activity.ActivityId);
        Assert.Equal(capsule.ReplacementActivity.Descriptor.Title, activity.Title);
        DesktopReplaceRecoveryResult recovery = runtime.GetReplaceRecoveryState();
        Assert.Equal(capsule.Id, Assert.Single(recovery.UndoableCapsuleIds));
        AuthenticatedActivitySessionHandler handler =
            await runtime.GetSessionHandlerAsync();
        Assert.True(handler.IsReplaceEndpointAvailable);
        Assert.True(((IDesktopActivityService)runtime).IsDestructiveReplaceAvailable);
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task TargetLocalUndoAfterRestartRestoresOriginalAndRecordsConsumption()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        UndoCapsule capsule = await CreateCommittedReplaceStateAsync(payloadStore);
        using DeviceIdentity identity = DeviceIdentity.Generate(TargetId, "Target");
        var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        await using var runtime = CreateRuntime(identity, trust, payloadStore);
        await runtime.InitializeAsync();

        UndoReplaceResult result = await runtime.UndoReplaceAsync(capsule.Id);

        Assert.Equal(OperationStatus.Committed, result.Status);
        Assert.Equal(FailureCode.None, result.FailureCode);
        DesktopActivitySnapshot activity = Assert.Single(runtime.GetActivities());
        Assert.Equal(capsule.OriginalActivity.Descriptor.Id, activity.ActivityId);
        Assert.Equal(capsule.OriginalActivity.Descriptor.Title, activity.Title);
        DesktopReplaceRecoveryResult recovery = runtime.GetReplaceRecoveryState();
        ReplaceRecoveryRecord replace = Assert.Single(
            recovery.Records,
            record => record.Kind == ReplaceRecoveryOperationKind.Replace);
        ReplaceRecoveryRecord undo = Assert.Single(
            recovery.Records,
            record => record.Kind == ReplaceRecoveryOperationKind.Undo);
        Assert.Equal(ReplaceUndoAvailability.Consumed, replace.UndoAvailability);
        Assert.Equal(OperationStatus.Committed, undo.Status);
        Assert.Empty(recovery.UndoableCapsuleIds);
        AuthenticatedActivitySessionHandler handler =
            await runtime.GetSessionHandlerAsync();
        Assert.True(handler.IsReplaceEndpointAvailable);
        Assert.True(((IDesktopActivityService)runtime).IsDestructiveReplaceAvailable);

        UndoReplaceResult consumed = await runtime.UndoReplaceAsync(capsule.Id);

        Assert.Equal(OperationStatus.Rejected, consumed.Status);
        Assert.Equal(FailureCode.UndoCapsuleConsumed, consumed.FailureCode);
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task PersistedPendingBoundarySuppressesRestartCatalogAndUndoAction()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        UndoCapsule capsule = await CreateCommittedReplaceStateAsync(payloadStore);
        OperationId pendingOperationId =
            OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        using (PersistentReplaceStateStore state =
               await PersistentReplaceStateStore.OpenAsync(payloadStore))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await state.ExecuteOnceAsync(
                    pendingOperationId,
                    new string('B', 64),
                    _ => ValueTask.FromException<OperationReceipt>(
                        new InvalidOperationException("Injected crash boundary.")),
                    CancellationToken.None));
        }

        using DeviceIdentity identity = DeviceIdentity.Generate(TargetId, "Target");
        var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        await using var runtime = CreateRuntime(identity, trust, payloadStore);

        await runtime.InitializeAsync();

        Assert.Empty(runtime.GetActivities());
        DesktopReplaceRecoveryResult recovery = runtime.GetReplaceRecoveryState();
        Assert.True(recovery.IsAvailable);
        Assert.Empty(recovery.UndoableCapsuleIds);
        Assert.Contains(
            recovery.Records,
            record => record.OperationId == pendingOperationId
                && record.IsRecoveryRequired);
        Assert.Contains(
            recovery.Records,
            record => record.CapsuleId == capsule.Id
                && record.UndoAvailability == ReplaceUndoAvailability.Available);
        AuthenticatedActivitySessionHandler handler =
            await runtime.GetSessionHandlerAsync();
        Assert.True(handler.IsReplaceEndpointAvailable);
        Assert.False(((IDesktopActivityService)runtime).IsDestructiveReplaceAvailable);
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task PendingBoundaryRejectsDirectTargetLocalUndoBeforeJournaling()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        UndoCapsule capsule = await CreateCommittedReplaceStateAsync(payloadStore);
        OperationId pendingOperationId =
            OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        using (PersistentReplaceStateStore state =
               await PersistentReplaceStateStore.OpenAsync(payloadStore))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await state.ExecuteOnceAsync(
                    pendingOperationId,
                    new string('B', 64),
                    _ => ValueTask.FromException<OperationReceipt>(
                        new InvalidOperationException("Injected crash boundary.")),
                    CancellationToken.None));
        }

        using DeviceIdentity identity = DeviceIdentity.Generate(TargetId, "Target");
        var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        await using var runtime = CreateRuntime(identity, trust, payloadStore);
        await runtime.InitializeAsync();

        UndoReplaceResult result = await runtime.UndoReplaceAsync(capsule.Id);

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal(FailureCode.UndoUnavailable, result.FailureCode);
        DesktopReplaceRecoveryResult recovery = runtime.GetReplaceRecoveryState();
        Assert.DoesNotContain(
            recovery.Records,
            record => record.Kind == ReplaceRecoveryOperationKind.Undo);
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task ExpiredCapsuleReconstructsCurrentNoteButOffersNoUndoAction()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        UndoCapsule capsule = await CreateCommittedReplaceStateAsync(payloadStore);
        using DeviceIdentity identity = DeviceIdentity.Generate(TargetId, "Target");
        var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        await using var runtime = CreateRuntime(
            identity,
            trust,
            payloadStore,
            Now.AddMinutes(11));

        await runtime.InitializeAsync();

        Assert.Equal(
            capsule.ReplacementActivity.Descriptor.Id,
            Assert.Single(runtime.GetActivities()).ActivityId);
        DesktopReplaceRecoveryResult recovery = runtime.GetReplaceRecoveryState();
        Assert.Empty(recovery.UndoableCapsuleIds);
        ReplaceRecoveryRecord replace = Assert.Single(recovery.Records);
        Assert.Equal(ReplaceUndoAvailability.Expired, replace.UndoAvailability);

        UndoReplaceResult result = await runtime.UndoReplaceAsync(capsule.Id);

        Assert.Equal(OperationStatus.Rejected, result.Status);
        Assert.Equal(FailureCode.UndoCapsuleExpired, result.FailureCode);
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task KnownStaleCapsulePreservesRevisionConflictReason()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        UndoCapsule capsule = await CreateCommittedReplaceStateAsync(payloadStore);
        OperationContext staleContext = OperationContext.Create(
            OperationId.Parse("12121212-1212-1212-1212-121212121212"),
            CorrelationId.Parse("13131313-1313-1313-1313-131313131313"),
            Now.AddSeconds(30));
        using (PersistentReplaceStateStore state =
               await PersistentReplaceStateStore.OpenAsync(payloadStore))
        {
            UndoJournalPreparation prepared = await state.PrepareUndoAsync(
                capsule.Id,
                staleContext.OperationId,
                new string('C', 64));
            Assert.Equal(UndoJournalPreparationStatus.Prepared, prepared.Status);
            await state.CompleteUndoAsync(
                staleContext.OperationId,
                UndoReplaceResult.Rejected(
                    staleContext,
                    capsule.Id,
                    FailureCode.RevisionConflict,
                    Now));
        }

        using DeviceIdentity identity = DeviceIdentity.Generate(TargetId, "Target");
        var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        await using var runtime = CreateRuntime(identity, trust, payloadStore);
        await runtime.InitializeAsync();
        Assert.Empty(runtime.GetActivities());
        Assert.Empty(runtime.GetReplaceRecoveryState().UndoableCapsuleIds);

        UndoReplaceResult result = await runtime.UndoReplaceAsync(capsule.Id);

        Assert.Equal(OperationStatus.Rejected, result.Status);
        Assert.Equal(FailureCode.RevisionConflict, result.FailureCode);
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task UnknownCapsuleRejectsBeforeJournaling()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        await CreateCommittedReplaceStateAsync(payloadStore);
        UndoCapsuleId unknown =
            UndoCapsuleId.Parse("14141414-1414-1414-1414-141414141414");
        using DeviceIdentity identity = DeviceIdentity.Generate(TargetId, "Target");
        var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        await using var runtime = CreateRuntime(identity, trust, payloadStore);
        await runtime.InitializeAsync();

        UndoReplaceResult result = await runtime.UndoReplaceAsync(unknown);

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal(FailureCode.UndoUnavailable, result.FailureCode);
        Assert.DoesNotContain(
            runtime.GetReplaceRecoveryState().Records,
            record => record.Kind == ReplaceRecoveryOperationKind.Undo);
        await trust.DisposeAsync();
    }

    [Fact]
    public async Task ReplaceRecoveryLoadFailureKeepsOtherActivityWorkAvailable()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(SourceId, "Source");
        var trust = new TrustSessionCoordinator(new InMemoryTrustStore());
        await using var runtime = CreateRuntime(
            identity,
            trust,
            new FailingReplaceStatePayloadStore());

        await runtime.InitializeAsync();
        DesktopActivitySnapshot note = runtime.CreateWorkspaceNote(
            "Still available",
            "Replace recovery failed closed",
            ActivitySensitivity.Normal);

        Assert.True(runtime.IsReady);
        Assert.Equal(note, Assert.Single(runtime.GetActivities()));
        Assert.False(runtime.GetReplaceRecoveryState().IsAvailable);
        AuthenticatedActivitySessionHandler handler =
            await runtime.GetSessionHandlerAsync();
        Assert.False(handler.IsReplaceEndpointAvailable);
        Assert.False(((IDesktopActivityService)runtime).IsDestructiveReplaceAvailable);
        await trust.DisposeAsync();
    }

    private static DesktopActivityRuntime CreateRuntime(
        DeviceIdentity identity,
        TrustSessionCoordinator trust,
        IReplaceStatePayloadStore? replaceStatePayloadStore = null,
        DateTimeOffset? utcNow = null,
        ISceneRemoteChildStatePayloadStore?
            sceneRemoteChildStatePayloadStore = null,
        ISceneApplyStatePayloadStore? sceneApplyStatePayloadStore = null,
        IReceiptSink? receiptSink = null) => new(
        cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(identity);
        },
        cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(trust);
        },
        new FixedTimeProvider(utcNow ?? Now),
        replaceStatePayloadStore,
        sceneRemoteChildStatePayloadStore,
        sceneApplyStatePayloadStore,
        receiptSink);

    private static async Task<UndoCapsule> CreateCommittedReplaceStateAsync(
        IReplaceStatePayloadStore payloadStore)
    {
        ActivityInstance original = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ActivityKind.Parse("workspace.note/v1"),
                TargetId,
                "Original note",
                "{\"text\":\"original body\"}"),
            ActivityPlacement.On(TargetId, "desktop"),
            revision: 4);
        ActivityInstance replacement = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                ActivityKind.Parse("workspace.note/v1"),
                SourceId,
                "Incoming note",
                "{\"text\":\"incoming body\"}"),
            ActivityPlacement.On(TargetId, "desktop"),
            revision: 5);
        UndoCapsule capsule = UndoCapsule.Create(
            UndoCapsuleId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            OperationContext.Create(
                OperationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                CorrelationId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                Now.AddSeconds(30)),
            SourceId,
            TargetId,
            original,
            replacement,
            Now,
            Now.AddMinutes(10));
        using PersistentReplaceStateStore state =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        Assert.True(await state.TryAddAsync(capsule));
        await state.ExecuteOnceAsync(
            capsule.OperationId,
            new string('A', 64),
            _ => ValueTask.FromResult(OperationReceipt.Committed(
                capsule.OperationId,
                capsule.CorrelationId,
                OperationKind.Replace,
                SourceId,
                TargetId,
                replacement.Descriptor,
                Now)),
            CancellationToken.None);
        return capsule;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class MemoryReplaceStatePayloadStore : IReplaceStatePayloadStore
    {
        private byte[]? payload;

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(payload?.ToArray());
        }

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            payload = value.ToArray();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingReplaceStatePayloadStore : IReplaceStatePayloadStore
    {
        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<byte[]?>(
                new IOException("Injected protected Replace state failure."));

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(
                new IOException("Injected protected Replace state failure."));
    }

    private sealed class MemorySceneRemoteChildStatePayloadStore :
        ISceneRemoteChildStatePayloadStore
    {
        private byte[]? payload;

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(payload?.ToArray());
        }

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            payload = value.ToArray();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingSceneRemoteChildStatePayloadStore :
        ISceneRemoteChildStatePayloadStore
    {
        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<byte[]?>(
                new IOException(
                    "Injected protected Scene remote child state failure."));

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(
                new IOException(
                    "Injected protected Scene remote child state failure."));
    }

    private sealed class MemorySceneApplyStatePayloadStore :
        ISceneApplyStatePayloadStore
    {
        private byte[]? payload;

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(payload?.ToArray());
        }

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            payload = value.ToArray();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingSceneApplyStatePayloadStore :
        ISceneApplyStatePayloadStore
    {
        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<byte[]?>(
                new IOException(
                    "Injected protected Scene Apply state failure."));

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(
                new IOException(
                    "Injected protected Scene Apply state failure."));
    }
}
