using System.Net;
using System.Net.Sockets;
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
        await using var runtime = CreateRuntime(source, trust);
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
        await using var source = CreateRuntime(sourceIdentity, sourceTrust);
        await using var target = CreateRuntime(targetIdentity, targetTrust);
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
        DesktopReplaceTargetSnapshot snapshot = Assert.Single(result.Targets);
        Assert.Equal(existing.ActivityId, snapshot.ActivityId);
        Assert.Equal("Existing target", snapshot.Title);
        Assert.Equal(1, snapshot.Revision);
        Assert.True(await targetTrust.TryUpdateCapabilitiesAsync(
            SourceId,
            sourceIdentity.PublicIdentity.Fingerprint,
            CapabilityGrant.None));

        DesktopReplaceTargetInventoryResult revoked =
            await source.GetReplaceTargetsAsync(incoming.ActivityId, TargetId);

        Assert.Equal(FailureCode.CapabilityDenied, revoked.FailureCode);
        Assert.Empty(revoked.Targets);
        Assert.Equal(existing, Assert.Single(target.GetActivities()));
        Assert.Equal(incoming, Assert.Single(source.GetActivities()));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => targetRun);
        await sourceTrust.DisposeAsync();
        await targetTrust.DisposeAsync();
    }

    [Fact]
    public async Task MissingReceiveGrantRejectsReplaceInventoryBeforeChannelLookup()
    {
        using DeviceIdentity sourceIdentity = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity targetIdentity = DeviceIdentity.Generate(TargetId, "Peer desk");
        var sourceStore = new InMemoryTrustStore();
        sourceStore.Register(new TrustRecord(
            targetIdentity.PublicIdentity,
            Now,
            CapabilityGrant.None));
        var sourceTrust = new TrustSessionCoordinator(sourceStore);
        await using var source = CreateRuntime(sourceIdentity, sourceTrust);
        await source.InitializeAsync();
        DesktopActivitySnapshot incoming = source.CreateWorkspaceNote(
            "Incoming note",
            "incoming body",
            ActivitySensitivity.Normal);

        DesktopReplaceTargetInventoryResult result =
            await source.GetReplaceTargetsAsync(incoming.ActivityId, TargetId);

        Assert.Equal(FailureCode.CapabilityDenied, result.FailureCode);
        Assert.Empty(result.Targets);
        await sourceTrust.DisposeAsync();
    }

    private static DesktopActivityRuntime CreateRuntime(
        DeviceIdentity identity,
        TrustSessionCoordinator trust) => new(
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
        new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
