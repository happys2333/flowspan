using System.Net;
using System.Net.Sockets;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopRemoteWindowPreparationPeerTests
{
    private static readonly DeviceId ParticipantDeviceId = DeviceId.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly DeviceId HostDeviceId = DeviceId.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly ActivityId ActivityId = ActivityId.From(
        Guid.Parse("33333333-3333-3333-3333-333333333333"));
    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.From(
            Guid.Parse("44444444-4444-4444-4444-444444444444"));

    [Fact]
    public async Task ReadyDoesNotRenderUntilExactAdmissionThenRendersMedia()
    {
        var renderer = new RecordingRenderer();
        var rendererFactory = new RecordingRendererFactory(renderer);
        await RunConnectedScenarioAsync(
            rendererFactory,
            async context =>
            {
                (RemoteWindowPreparationRequest request,
                    RemoteWindowPreparationResponse response) =
                    await context.PrepareAsync();

                Assert.Equal(RemoteWindowPreparationOutcome.Ready, response.Outcome);
                Assert.Equal(1, rendererFactory.PrepareCount);
                Assert.Equal(0, renderer.RenderCount);

                await context.PreparationPeer.CompleteAdmissionAsync(
                    request,
                    CreateAdmissionState(request),
                    default);
                Assert.Equal(0, renderer.RenderCount);

                await context.SendJpegAsync();

                await renderer.Rendered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(1, renderer.RenderCount);
                Assert.Equal((2, 2), renderer.LastSize);
            },
            renderer);
    }

    [Fact]
    public async Task RejectedAppliedAdmissionUnwindsPreparedOwners()
    {
        var renderer = new RecordingRenderer();
        var rendererFactory = new RecordingRendererFactory(renderer);
        await RunConnectedScenarioAsync(
            rendererFactory,
            async context =>
            {
                (RemoteWindowPreparationRequest request,
                    RemoteWindowPreparationResponse response) =
                    await context.PrepareAsync();
                Assert.Equal(RemoteWindowPreparationOutcome.Ready, response.Outcome);

                await Assert.ThrowsAsync<InvalidDataException>(() =>
                    context.PreparationPeer.CompleteAdmissionAsync(
                            request,
                            CreateAdmissionState(
                                request,
                                MirrorParticipantRole.DriverEligible),
                            default)
                        .AsTask());

                Assert.True(renderer.IsDisposed);
            },
            renderer);
    }

    [Fact]
    public async Task CancelledAppliedAdmissionUnwindsPreparedOwners()
    {
        var renderer = new RecordingRenderer();
        await RunConnectedScenarioAsync(
            new RecordingRendererFactory(renderer),
            async context =>
            {
                (RemoteWindowPreparationRequest request,
                    RemoteWindowPreparationResponse response) =
                    await context.PrepareAsync();
                Assert.Equal(RemoteWindowPreparationOutcome.Ready, response.Outcome);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();

                OperationCanceledException failure =
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                        context.PreparationPeer.CompleteAdmissionAsync(
                                request,
                                CreateAdmissionState(request),
                                cancellation.Token)
                            .AsTask());

                Assert.Equal(cancellation.Token, failure.CancellationToken);
                Assert.True(renderer.IsDisposed);
            },
            renderer);
    }

    [Fact]
    public async Task DisposalUnwindsOwnersWhenCancellationCallbackThrows()
    {
        var renderer = new RecordingRenderer();
        var rendererFactory = new ThrowingCancellationRendererFactory(renderer);
        await RunConnectedScenarioAsync(
            rendererFactory,
            async context =>
            {
                (_, RemoteWindowPreparationResponse response) =
                    await context.PrepareAsync();
                Assert.Equal(RemoteWindowPreparationOutcome.Ready, response.Outcome);

                Exception failure = await Assert.ThrowsAnyAsync<Exception>(() =>
                    context.PreparationPeer.DisposeAsync().AsTask());

                Assert.Contains(
                    "test cancellation callback failed",
                    failure.ToString(),
                    StringComparison.Ordinal);
                Assert.True(renderer.IsDisposed);
            },
            renderer,
            ignorePreparationPeerDisposalFailure: true);
    }

    [Fact]
    public async Task DisposalJoinsLateRendererPreparationAndDisposesItsResult()
    {
        var renderer = new RecordingRenderer();
        var rendererFactory = new BlockingRendererFactory(renderer);
        await RunConnectedScenarioAsync(
            rendererFactory,
            async context =>
            {
                Task<(RemoteWindowPreparationRequest Request,
                    RemoteWindowPreparationResponse Response)> preparing =
                    context.PrepareAsync();
                await rendererFactory.Entered.Task;

                Task disposing = context.PreparationPeer.DisposeAsync().AsTask();
                await rendererFactory.CancellationObserved.Task;
                try
                {
                    await Assert.ThrowsAsync<TimeoutException>(() =>
                        disposing.WaitAsync(TimeSpan.FromMilliseconds(100)));
                }
                finally
                {
                    rendererFactory.Release();
                }

                (_, RemoteWindowPreparationResponse response) = await preparing;
                await disposing;

                Assert.Equal(
                    RemoteWindowPreparationOutcome.Rejected,
                    response.Outcome);
                Assert.True(renderer.IsDisposed);
            },
            renderer);
    }

    [Fact]
    public async Task RejectedAcquiredLeaseIsReleased()
    {
        AuthenticatedRemoteWindowConnectionLease? rejectedLease = null;
        var renderer = new RecordingRenderer();
        await RunConnectedScenarioAsync(
            new RecordingRendererFactory(renderer),
            async context =>
            {
                RemoteWindowPreparationResponse response =
                    await context.PreparationPeer.PrepareAsync(
                        CreateRequest(),
                        default);

                Assert.Equal(
                    RemoteWindowPreparationOutcome.Rejected,
                    response.Outcome);
                Assert.Equal("media_unavailable", response.ReasonCode);
                Assert.False(Assert.IsType<
                    AuthenticatedRemoteWindowConnectionLease>(rejectedLease)
                    .IsCurrent);
            },
            decorateConnectionAcquirer: acquire =>
                (DeviceId peerDeviceId,
                    out AuthenticatedRemoteWindowConnectionLease? lease) =>
                {
                    _ = acquire(peerDeviceId, out lease);
                    rejectedLease = lease;
                    return false;
                },
            verifyPeerDisconnectCleanup: false);
    }

    [Fact]
    public async Task ReceiveFailureRacingCleanupRemainsObservable()
    {
        var renderer = new FailingAfterCancellationRenderer();
        await RunConnectedScenarioAsync(
            new RecordingRendererFactory(renderer),
            async context =>
            {
                (RemoteWindowPreparationRequest request,
                    RemoteWindowPreparationResponse response) =
                    await context.PrepareAsync();
                Assert.Equal(RemoteWindowPreparationOutcome.Ready, response.Outcome);
                await context.PreparationPeer.CompleteAdmissionAsync(
                    request,
                    CreateAdmissionState(request),
                    default);
                await context.SendJpegAsync();
                await renderer.RenderEntered.Task;

                Exception failure = await Assert.ThrowsAnyAsync<Exception>(() =>
                    context.PreparationPeer.PeerDisconnectedAsync(
                            HostDeviceId,
                            default)
                        .AsTask());

                Assert.Contains(
                    "test render failed during cleanup",
                    failure.ToString(),
                    StringComparison.Ordinal);
                Assert.True(renderer.IsDisposed);
            });
    }

    [Fact]
    public async Task PreparationAfterDisposalReturnsStoppingRejection()
    {
        await using var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            NoConnection,
            AllowDesktopRemoteWindowReceivePolicy.Instance,
            UnavailableDesktopRemoteWindowParticipantRendererFactory.Instance);
        await preparationPeer.DisposeAsync();

        RemoteWindowPreparationResponse response = await preparationPeer.PrepareAsync(
            CreateRequest(),
            default);

        Assert.Equal(RemoteWindowPreparationOutcome.Rejected, response.Outcome);
        Assert.Equal("participant_stopping", response.ReasonCode);

        static bool NoConnection(
            DeviceId peerDeviceId,
            out AuthenticatedRemoteWindowConnectionLease? lease)
        {
            lease = null;
            return false;
        }
    }

    [Fact]
    public async Task PreparationAndCleanupFailuresRemainObservableTogether()
    {
        await RunConnectedScenarioAsync(
            new FailingPreparationAndCancellationRendererFactory(),
            async context =>
            {
                Exception failure = await Assert.ThrowsAnyAsync<Exception>(() =>
                    context.PrepareAsync());

                Assert.Contains(
                    "test renderer preparation failed",
                    failure.ToString(),
                    StringComparison.Ordinal);
                Assert.Contains(
                    "test preparation cleanup failed",
                    failure.ToString(),
                    StringComparison.Ordinal);
            });
    }

    private static async Task RunConnectedScenarioAsync(
        IDesktopRemoteWindowParticipantRendererFactory rendererFactory,
        Func<ConnectedScenario, Task> test,
        RecordingRenderer? renderer = null,
        TimeProvider? timeProvider = null,
        bool ignorePreparationPeerDisposalFailure = false,
        Func<TryAcquireDesktopRemoteWindowPeerConnection,
            TryAcquireDesktopRemoteWindowPeerConnection>?
            decorateConnectionAcquirer = null,
        bool verifyPeerDisconnectCleanup = true)
    {
        using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
            ParticipantDeviceId,
            "Participant");
        using DeviceIdentity hostIdentity = DeviceIdentity.Generate(
            HostDeviceId,
            "Host");
        await using var participantRoutes = new RemoteWindowMediaRouteRegistry();
        await using var participantMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory(participantRoutes);
        await using var hostRoutes = new RemoteWindowMediaRouteRegistry();
        await using var hostMedia =
            new AuthenticatedRemoteWindowMediaSessionDirectory(hostRoutes);
        AuthenticatedActivitySessionHandler? participantHandler = null;
        TryAcquireDesktopRemoteWindowPeerConnection connectionAcquirer =
            TryAcquirePeerConnection;
        connectionAcquirer = decorateConnectionAcquirer?.Invoke(connectionAcquirer)
            ?? connectionAcquirer;
        var preparationPeer = new DesktopRemoteWindowPreparationPeer(
            ParticipantDeviceId,
            connectionAcquirer,
            AllowDesktopRemoteWindowReceivePolicy.Instance,
            rendererFactory,
            timeProvider);
        await using var preparationPeerOwner = new AsyncDisposal(
            preparationPeer.DisposeAsync,
            ignorePreparationPeerDisposalFailure);
        participantHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(ParticipantDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: participantMedia,
            remoteWindowPreparationPeer: preparationPeer);
        await using (participantHandler)
        await using (var hostHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(HostDeviceId),
            replacePeer: null,
            replaceInventoryPeer: null,
            swapPeer: null,
            remoteWindowMediaSessions: hostMedia))
        {
            ProtocolVersion version =
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion;
            (AuthenticatedTcpControlConnection participantConnection,
                AuthenticatedTcpControlConnection hostConnection) =
                await CreateControlPairAsync(
                    participantIdentity,
                    hostIdentity,
                    version);
            using var mediaListener = new TcpListener(IPAddress.Loopback, 0);
            mediaListener.Start(backlog: 1);
            var mediaEndPoint = Assert.IsType<IPEndPoint>(
                mediaListener.LocalEndpoint);
            VerifiedPeerConnectionCandidate candidate = CreateVerifiedCandidate(
                hostIdentity,
                mediaEndPoint,
                version);
            var validator = new CurrentCandidateValidator();
            await using (participantConnection)
            await using (hostConnection)
            {
                Task participantRunning = participantHandler
                    .RunWithRemoteWindowPeerAsync(
                        participantConnection,
                        candidate,
                        validator)
                    .AsTask();
                Task hostRunning = hostHandler.RunAsync(hostConnection).AsTask();
                await using AuthenticatedRemoteWindowConnectionLease hostLease =
                    await WaitForConnectionLeaseAsync(
                        hostHandler,
                        ParticipantDeviceId);
                _ = hostLease.PrepareResponderRoute(SessionId, ActivityId);
                var context = new ConnectedScenario(
                    preparationPeer,
                    hostLease,
                    mediaListener,
                    hostRoutes,
                    hostMedia);

                await test(context);

                if (verifyPeerDisconnectCleanup)
                {
                    await preparationPeer.PeerDisconnectedAsync(
                        HostDeviceId,
                        default);
                    if (renderer is not null)
                    {
                        Assert.True(renderer.IsDisposed);
                    }

                    await Assert.ThrowsAnyAsync<Exception>(() =>
                        participantRunning.WaitAsync(TimeSpan.FromSeconds(5)));
                    await Assert.ThrowsAnyAsync<Exception>(() =>
                        hostRunning.WaitAsync(TimeSpan.FromSeconds(5)));
                    if (context.AcceptingMedia is not null)
                    {
                        await context.AcceptingMedia.WaitAsync(
                            TimeSpan.FromSeconds(5));
                    }

                    Assert.False(
                        participantHandler.TryAcquireRemoteWindowPeerConnection(
                            HostDeviceId,
                            out _));
                    Assert.False(participantMedia.TryGet(HostDeviceId, out _));
                    Assert.False(hostMedia.TryGet(ParticipantDeviceId, out _));
                    Assert.Equal(0, participantRoutes.Count);
                    Assert.Equal(0, hostRoutes.Count);
                }
                else
                {
                    await participantConnection.DisposeAsync();
                    await hostConnection.DisposeAsync();
                    await Assert.ThrowsAnyAsync<Exception>(() =>
                        participantRunning.WaitAsync(TimeSpan.FromSeconds(5)));
                    await Assert.ThrowsAnyAsync<Exception>(() =>
                        hostRunning.WaitAsync(TimeSpan.FromSeconds(5)));
                }
            }
        }

        bool TryAcquirePeerConnection(
            DeviceId peerDeviceId,
            out AuthenticatedRemoteWindowConnectionLease? lease) =>
            participantHandler!.TryAcquireRemoteWindowPeerConnection(
                peerDeviceId,
                out lease);
    }

    private static RemoteWindowPreparationRequest CreateRequest()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        return RemoteWindowPreparationRequest.Create(
            CorrelationId.From(Guid.NewGuid()),
            SessionId,
            ActivityId,
            HostDeviceId,
            ParticipantDeviceId,
            MirrorParticipantRole.ViewOnly,
            now.AddSeconds(10));
    }

    private static RemoteWindowParticipantState CreateAdmissionState(
        RemoteWindowPreparationRequest request,
        MirrorParticipantRole? effectiveRole = null) =>
        RemoteWindowParticipantState.Create(
            request.CorrelationId,
            request.SessionId,
            request.ActivityId,
            request.HostDeviceId,
            request.ParticipantDeviceId,
            RemoteWindowControlAction.Admission,
            RemoteWindowControlOutcome.Applied,
            "participant_updated",
            RemoteWindowLifecycle.Active,
            RemoteWindowCaptureState.Capturing,
            participantCount: 2,
            effectiveRole ?? request.RequestedRole,
            request.HostDeviceId,
            driverLeaseEpoch: 1,
            driverLeaseExpiresAt: request.Deadline,
            ProtectionKind.Safe,
            revision: 1);

    private static async Task AcceptOwnedMediaAsync(
        TcpListener listener,
        RemoteWindowMediaRouteRegistry routes,
        AuthenticatedRemoteWindowMediaSessionDirectory sessions)
    {
        using TcpClient accepted = await listener.AcceptTcpClientAsync();
        RemoteWindowMediaAttachment attachment =
            await routes.AcceptAsync(accepted.GetStream());
        await FlowspanTcpInboundListener.RunOwnedMediaAttachmentHandlerAsync(
            attachment,
            sessions,
            CancellationToken.None);
    }

    private static async Task<AuthenticatedRemoteWindowConnectionLease>
        WaitForConnectionLeaseAsync(
        AuthenticatedActivitySessionHandler handler,
        DeviceId peerDeviceId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        AuthenticatedRemoteWindowConnectionLease? lease;
        while (!handler.TryAcquireRemoteWindowConnection(peerDeviceId, out lease))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), timeout.Token);
        }

        return Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(lease);
    }

    private static async Task<(
        AuthenticatedTcpControlConnection Participant,
        AuthenticatedTcpControlConnection Host)> CreateControlPairAsync(
        DeviceIdentity participant,
        DeviceIdentity host,
        ProtocolVersion version)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                host,
                CreateTrustRecord(participant),
                [version]).AsTask();
        AuthenticatedTcpControlConnection? participantConnection = null;
        try
        {
            participantConnection =
                await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    participant,
                    CreateTrustRecord(host),
                    [version]);
            return (participantConnection, await accepting);
        }
        catch
        {
            if (participantConnection is not null)
            {
                await participantConnection.DisposeAsync();
            }

            throw;
        }
    }

    private static TrustRecord CreateTrustRecord(DeviceIdentity identity) => new(
        identity.PublicIdentity,
        DateTimeOffset.UtcNow,
        CapabilityGrant.Of(Capability.ActivityOffer));

    private static VerifiedPeerConnectionCandidate CreateVerifiedCandidate(
        DeviceIdentity host,
        IPEndPoint endPoint,
        ProtocolVersion version)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
            host,
            endPoint.Port,
            [version],
            now.Subtract(TimeSpan.FromSeconds(1)),
            TimeSpan.FromMinutes(1),
            Enumerable.Repeat((byte)0x5a, SignedDiscoveryOffer.NonceLength)
                .ToArray());
        return VerifiedPeerConnectionCandidate.Create(
            endPoint,
            offer,
            host.PublicIdentity,
            now);
    }

    private sealed class RejectingActivityPeer(DeviceId deviceId) : IActivityPeer
    {
        public DeviceId DeviceId { get; } = deviceId;

        public ValueTask<OperationReceipt> ReceiveActivityAsync(
            DeviceId senderDeviceId,
            ActivityTransferOffer offer,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<OperationReceipt>(
                new InvalidOperationException("No Activity was expected."));
    }

    private sealed class CurrentCandidateValidator :
        IVerifiedPeerConnectionCandidateValidator
    {
        public bool IsCurrent(
            VerifiedPeerConnectionCandidate candidate,
            ProtocolVersion protocolVersion) => true;
    }

    private sealed class ConnectedScenario(
        DesktopRemoteWindowPreparationPeer preparationPeer,
        AuthenticatedRemoteWindowConnectionLease hostLease,
        TcpListener mediaListener,
        RemoteWindowMediaRouteRegistry hostRoutes,
        AuthenticatedRemoteWindowMediaSessionDirectory hostMedia)
    {
        public Task? AcceptingMedia { get; private set; }

        public DesktopRemoteWindowPreparationPeer PreparationPeer
        {
            get;
        } = preparationPeer;

        public async Task<(RemoteWindowPreparationRequest Request,
            RemoteWindowPreparationResponse Response)> PrepareAsync(
            RemoteWindowPreparationRequest? request = null)
        {
            if (AcceptingMedia is not null)
            {
                throw new InvalidOperationException(
                    "The connected test scenario was already prepared.");
            }

            request ??= CreateRequest();
            AcceptingMedia = AcceptOwnedMediaAsync(
                mediaListener,
                hostRoutes,
                hostMedia);
            RemoteWindowPreparationResponse response =
                await PreparationPeer.PrepareAsync(request, default);
            if (response.Outcome is RemoteWindowPreparationOutcome.Ready)
            {
                await hostLease.WaitForMediaAttachmentAsync();
            }

            return (request, response);
        }

        public async Task SendJpegAsync()
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "remote-window-2x2.jpg"));
            using RemoteWindowMediaFrame frame = RemoteWindowMediaFrame.Create(
                SessionId,
                ActivityId,
                RemoteWindowMediaKind.Video,
                sequence: 1,
                chunkIndex: 0,
                chunkCount: 1,
                jpeg);
            await hostLease.SendMediaAsync(frame);
        }
    }

    private sealed class AsyncDisposal(
        Func<ValueTask> dispose,
        bool ignoreFailure) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await dispose();
            }
            catch when (ignoreFailure)
            {
            }
        }
    }

    private sealed class RecordingRendererFactory(
        IDesktopRemoteWindowParticipantRenderer renderer) :
        IDesktopRemoteWindowParticipantRendererFactory
    {
        private int prepareCount;

        public int PrepareCount => Volatile.Read(ref prepareCount);

        public ValueTask<IDesktopRemoteWindowParticipantRenderer?> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref prepareCount);
            return ValueTask.FromResult<
                IDesktopRemoteWindowParticipantRenderer?>(renderer);
        }
    }

    private sealed class FailingAfterCancellationRenderer :
        IDesktopRemoteWindowParticipantRenderer
    {
        private readonly TaskCompletionSource cancellationObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int disposed;

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public TaskCompletionSource RenderEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref disposed, 1);
            return ValueTask.CompletedTask;
        }

        public async ValueTask RenderAsync(
            DesktopRemoteWindowBgraFrame frame,
            CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration =
                cancellationToken.UnsafeRegister(
                    static state => ((TaskCompletionSource)state!).TrySetResult(),
                    cancellationObserved);
            RenderEntered.TrySetResult();
            await cancellationObserved.Task;
            throw new InvalidOperationException(
                "test render failed during cleanup");
        }
    }

    private sealed class FailingPreparationAndCancellationRendererFactory :
        IDesktopRemoteWindowParticipantRendererFactory
    {
        public ValueTask<IDesktopRemoteWindowParticipantRenderer?> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken.Register(
                static () => throw new InvalidOperationException(
                    "test preparation cleanup failed"));
            throw new InvalidOperationException(
                "test renderer preparation failed");
        }
    }

    private sealed class ThrowingCancellationRendererFactory(
        RecordingRenderer renderer) :
        IDesktopRemoteWindowParticipantRendererFactory
    {
        public ValueTask<IDesktopRemoteWindowParticipantRenderer?> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken.Register(
                static () => throw new InvalidOperationException(
                    "test cancellation callback failed"));
            return ValueTask.FromResult<
                IDesktopRemoteWindowParticipantRenderer?>(renderer);
        }
    }

    private sealed class BlockingRendererFactory(RecordingRenderer renderer) :
        IDesktopRemoteWindowParticipantRendererFactory
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IDesktopRemoteWindowParticipantRenderer?>
            PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration =
                cancellationToken.UnsafeRegister(
                    static state => ((TaskCompletionSource)state!).TrySetResult(),
                    CancellationObserved);
            Entered.TrySetResult();
            await release.Task;
            return renderer;
        }

        public void Release() => release.TrySetResult();
    }

    private sealed class RecordingRenderer :
        IDesktopRemoteWindowParticipantRenderer
    {
        private int disposed;
        private int renderCount;

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public (int Width, int Height) LastSize { get; private set; }

        public int RenderCount => Volatile.Read(ref renderCount);

        public TaskCompletionSource Rendered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask RenderAsync(
            DesktopRemoteWindowBgraFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            LastSize = (frame.Width, frame.Height);
            Interlocked.Increment(ref renderCount);
            Rendered.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref disposed, 1);
            return ValueTask.CompletedTask;
        }
    }
}
