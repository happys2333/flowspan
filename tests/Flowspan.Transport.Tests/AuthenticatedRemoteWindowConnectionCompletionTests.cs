using System.Net;
using System.Net.Sockets;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedRemoteWindowConnectionCompletionTests
{
    private static readonly DeviceId HostId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId ParticipantId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalConnectionWaitJoinsManagedRegistrationAfterLeaseDisposal(
        bool disconnectFails)
    {
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(HostId, "Host");
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            ParticipantId,
            "Participant");
        var injected = new IOException("registration cleanup canary");
        var peer = new GatedDisconnectPreparationPeer
        {
            DisconnectFailure = disconnectFails ? injected : null,
        };
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new UnusedActivityPeer(),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions,
            remoteWindowPreparationPeer: peer);
        (AuthenticatedTcpControlConnection hostConnection,
            AuthenticatedTcpControlConnection participantConnection) =
            await CreateControlPairAsync(hostIdentity, participantIdentity);
        await using (hostConnection)
        await using (participantConnection)
        {
            Task running = handler.RunAsync(participantConnection).AsTask();
            AuthenticatedRemoteWindowConnectionLease lease =
                await WaitForLeaseAsync(handler);
            Task externalWait = lease.WaitForConnectionClosedAsync().AsTask();
            try
            {
                Assert.True(lease.IsCurrent);
                Assert.True(handler.TryGetChannel(HostId, out _));
                await lease.FailCloseAsync().AsTask().WaitAsync(
                    TimeSpan.FromSeconds(5));
                await peer.DisconnectEntered.Task.WaitAsync(
                    TimeSpan.FromSeconds(5));
                await lease.DisposeAsync();

                Assert.False(running.IsCompleted);
                Assert.False(externalWait.IsCompleted);
                Assert.Same(
                    externalWait,
                    lease.WaitForConnectionClosedAsync().AsTask());
                Assert.False(mediaSessions.TryGet(HostId, out _));

                peer.ReleaseDisconnect.TrySetResult();
                if (disconnectFails)
                {
                    IOException failure = await Assert.ThrowsAsync<IOException>(() =>
                        externalWait.WaitAsync(TimeSpan.FromSeconds(5)));
                    Assert.Same(injected, failure);
                    Assert.NotNull(await Record.ExceptionAsync(() =>
                        running.WaitAsync(TimeSpan.FromSeconds(5))));
                }
                else
                {
                    await externalWait.WaitAsync(TimeSpan.FromSeconds(5));
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                        running.WaitAsync(TimeSpan.FromSeconds(5)));
                }

                Assert.False(handler.TryGetChannel(HostId, out _));
                Assert.Empty(handler.GetConnectedPeers());
            }
            finally
            {
                peer.ReleaseDisconnect.TrySetResult();
                if (lease.IsCurrent)
                {
                    await lease.FailCloseAsync();
                }

                await lease.DisposeAsync();
                _ = await Record.ExceptionAsync(() =>
                    running.WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }
    }

    [Fact]
    public async Task ConnectionWaitRemainsBoundToOriginalRegistrationWhenPeerReconnects()
    {
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(HostId, "Host");
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            ParticipantId,
            "Participant");
        var peer = new GatedDisconnectPreparationPeer();
        peer.ReleaseDisconnect.TrySetResult();
        await using var routes = new RemoteWindowMediaRouteRegistry();
        await using var mediaSessions =
            new AuthenticatedRemoteWindowMediaSessionDirectory(routes);
        await using var handler = new AuthenticatedActivitySessionHandler(
            new UnusedActivityPeer(),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: mediaSessions,
            remoteWindowPreparationPeer: peer);
        (AuthenticatedTcpControlConnection oldHostConnection,
            AuthenticatedTcpControlConnection oldParticipantConnection) =
            await CreateControlPairAsync(hostIdentity, participantIdentity);
        await using (oldHostConnection)
        await using (oldParticipantConnection)
        {
            Task oldRun = handler.RunAsync(oldParticipantConnection).AsTask();
            AuthenticatedRemoteWindowConnectionLease oldLease =
                await WaitForLeaseAsync(handler);
            await oldLease.FailCloseAsync().AsTask().WaitAsync(
                TimeSpan.FromSeconds(5));
            await oldLease.DisposeAsync();
            await oldLease.WaitForConnectionClosedAsync().AsTask().WaitAsync(
                TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                oldRun.WaitAsync(TimeSpan.FromSeconds(5)));
            (AuthenticatedTcpControlConnection replacementHostConnection,
                AuthenticatedTcpControlConnection replacementParticipantConnection) =
                await CreateControlPairAsync(hostIdentity, participantIdentity);
            await using (replacementHostConnection)
            await using (replacementParticipantConnection)
            {
                Task replacementRun = handler.RunAsync(
                        replacementParticipantConnection)
                    .AsTask();
                AuthenticatedRemoteWindowConnectionLease replacementLease =
                    await WaitForLeaseAsync(handler);
                try
                {
                    Task replacementWait = replacementLease
                        .WaitForConnectionClosedAsync().AsTask();

                    Assert.True(replacementLease.Generation > oldLease.Generation);
                    Assert.True(oldLease.WaitForConnectionClosedAsync()
                        .AsTask().IsCompletedSuccessfully);
                    Assert.False(replacementWait.IsCompleted);
                    Assert.True(replacementLease.IsCurrent);

                    await replacementLease.FailCloseAsync().AsTask().WaitAsync(
                        TimeSpan.FromSeconds(5));
                    await replacementLease.DisposeAsync();
                    await replacementWait.WaitAsync(TimeSpan.FromSeconds(5));
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                        replacementRun.WaitAsync(TimeSpan.FromSeconds(5)));
                }
                finally
                {
                    if (replacementLease.IsCurrent)
                    {
                        await replacementLease.FailCloseAsync();
                    }

                    await replacementLease.DisposeAsync();
                    _ = await Record.ExceptionAsync(() =>
                        replacementRun.WaitAsync(TimeSpan.FromSeconds(5)));
                }
            }
        }
    }

    private static async Task<(
        AuthenticatedTcpControlConnection Host,
        AuthenticatedTcpControlConnection Participant)> CreateControlPairAsync(
        DeviceIdentity hostIdentity,
        DeviceIdentity participantIdentity)
    {
        var hostTrust = new TrustRecord(
            participantIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        var participantTrust = new TrustRecord(
            hostIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        ProtocolVersion version =
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion;
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                participantIdentity,
                participantTrust,
                [version]).AsTask();
        AuthenticatedTcpControlConnection host =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                hostIdentity,
                hostTrust,
                [version]);
        try
        {
            return (host, await accepting);
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private static async Task<AuthenticatedRemoteWindowConnectionLease>
        WaitForLeaseAsync(AuthenticatedActivitySessionHandler handler)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        AuthenticatedRemoteWindowConnectionLease? lease;
        while (!handler.TryAcquireRemoteWindowConnection(HostId, out lease))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), timeout.Token);
        }

        return Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(lease);
    }

    private sealed class UnusedActivityPeer : IActivityPeer
    {
        public DeviceId DeviceId => ParticipantId;

        public ValueTask<OperationReceipt> ReceiveActivityAsync(
            DeviceId senderDeviceId,
            ActivityTransferOffer offer,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<OperationReceipt>(
                new InvalidOperationException("No Activity was expected."));
    }

    private sealed class GatedDisconnectPreparationPeer :
        IRemoteWindowPreparationPeer
    {
        public DeviceId ParticipantDeviceId => ParticipantId;

        public TaskCompletionSource DisconnectEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseDisconnect { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public IOException? DisconnectFailure { get; init; }

        public ValueTask<RemoteWindowPreparationResponse> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<RemoteWindowPreparationResponse>(
                new InvalidOperationException("No Preparation was expected."));

        public ValueTask CompleteAdmissionAsync(
            RemoteWindowPreparationRequest request,
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(
                new InvalidOperationException("No Admission was expected."));

        public async ValueTask PeerDisconnectedAsync(
            DeviceId hostDeviceId,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HostId, hostDeviceId);
            DisconnectEntered.TrySetResult();
            await ReleaseDisconnect.Task;
            if (DisconnectFailure is { } failure)
            {
                throw failure;
            }
        }
    }
}
