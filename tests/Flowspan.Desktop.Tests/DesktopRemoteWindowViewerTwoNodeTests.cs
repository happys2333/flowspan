using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed partial class DesktopRemoteWindowManagedTwoNodeTracerTests
{
    [Fact]
    public async Task ViewOnlyViewerCopiesAuthenticatedTcpPixelsAndStopDrainsBothNodes()
    {
        HeadlessUnitTestSession headless = RemoteWindowViewerHeadlessApp.Session;
        await headless.Dispatch<int>(async () =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using DeviceIdentity hostIdentity = DeviceIdentity.Generate(HostDeviceId, "Host");
            using DeviceIdentity participantIdentity = DeviceIdentity.Generate(
                ParticipantDeviceId,
                "Participant");
            CapabilityGrant hostToParticipant = CapabilityGrant.Of(
                Capability.MirrorView,
                Capability.MirrorDrive);
            CapabilityGrant participantToHost = CapabilityGrant.Of(Capability.ActivityOffer);
            await using TrustSessionCoordinator hostTrust = CreateTrust(
                participantIdentity,
                hostToParticipant);
            await using TrustSessionCoordinator participantTrust = CreateTrust(
                hostIdentity,
                participantToHost);
            await using var hostMedia = new AuthenticatedRemoteWindowMediaSessionDirectory();
            await using var participantMedia =
                new AuthenticatedRemoteWindowMediaSessionDirectory();
            var controlPeer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
            await using var hostHandler = new AuthenticatedActivitySessionHandler(
                new RejectingActivityPeer(HostDeviceId),
                replacePeer: null,
                replaceInventoryPeer: null,
                swapPeer: null,
                timeProvider: FixedTimeProvider.Instance,
                remoteWindowPeer: controlPeer,
                remoteWindowMediaSessions: hostMedia);
            DesktopRemoteWindowPreparationPeer? preparationPeer = null;
            int stopReceivingCount = 0;
            await using var viewer = new RemoteWindowViewerViewModel(
                AvaloniaDesktopUiDispatcher.Instance,
                StopReceivingAsync,
                FixedTimeProvider.Instance);
            viewer.EnableReceiving();
            Assert.True(viewer.IsReceivingEnabled);
            Assert.Null(viewer.ImageSource);
            var rendererFactory = new ObservingViewerRendererFactory(viewer);
            AuthenticatedActivitySessionHandler? participantHandler = null;
            preparationPeer = new DesktopRemoteWindowPreparationPeer(
                ParticipantDeviceId,
                TryAcquireParticipantConnection,
                viewer,
                rendererFactory,
                FixedTimeProvider.Instance);
            await using var preparationPeerOwner = preparationPeer;
            participantHandler = new AuthenticatedActivitySessionHandler(
                new RejectingActivityPeer(ParticipantDeviceId),
                replacePeer: null,
                replaceInventoryPeer: null,
                swapPeer: null,
                timeProvider: FixedTimeProvider.Instance,
                remoteWindowMediaSessions: participantMedia,
                remoteWindowPreparationPeer: preparationPeer);
            await using var participantHandlerOwner = participantHandler;
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start(backlog: 8);
            var endpoint = Assert.IsType<IPEndPoint>(socket.LocalEndpoint);
            var resolver = new DesktopRemoteWindowPeerEndpointResolver(
                participantTrust,
                () => ImmutableArray.Create(CreateCandidate(hostIdentity, endpoint)),
                FixedTimeProvider.Instance);
            var participantSessionHandler = new DesktopRemoteWindowPeerSessionHandler(
                participantHandler,
                resolver);
            var listener = CreateListener(
                socket,
                hostIdentity,
                hostTrust,
                hostHandler,
                hostMedia,
                timeProvider: FixedTimeProvider.Instance);
            using var listenerStop = new CancellationTokenSource();
            using var participantSessionStop = new CancellationTokenSource();
            Task listenerRun = listener.RunAsync(listenerStop.Token).AsTask();
            AuthenticatedTcpControlConnection? participantConnection = null;
            Task? participantRun = null;
            var capture = new RecordingCaptureBoundary();
            var input = new RecordingInputBoundary();
            var permissions = new RecordingPermissionBoundary(
                NativeRemoteWindowPermissionSnapshot.Create(
                    NativeRemoteWindowPermissionState.Granted,
                    NativeRemoteWindowPermissionState.Granted,
                    ownerGeneration: 1,
                    revision: 1));
            var sessions = new RecordingSharingSessionBoundary();
            using var emergencyStops = new RecordingEmergencyStopRegistrar();
            using var sources = new NativeRemoteWindowSourceRegistry(HostDeviceId);
            using NativeRemoteWindowSourceRegistration sourceRegistration =
                sources.RegisterGeneric(CreateMetadata());
            using NativeRemoteWindowSourceLease sourceLease = AcquireLease(
                sources,
                sourceRegistration.Snapshot);
            var protection = new RecordingProtectionSource(
                NativeRemoteWindowProtectionObservation.Create(
                    SafeNow(),
                    ownerGeneration: 1,
                    sessionGeneration: 1,
                    sourceRegistration.Source.SourceGeneration,
                    revision: 1));
            await using var coordinator = new DesktopRemoteWindowHostCoordinator(
                new FixedClock(Now),
                permissions,
                new TrustMirrorAuthorizationSource(hostTrust),
                capture,
                input,
                sessions,
                emergencyStops,
                controlPeer,
                ownerLeaseDuration: TimeSpan.FromSeconds(30),
                preparationLifetime: TimeSpan.FromSeconds(10));

            try
            {
                participantConnection = await AuthenticatedTcpControlConnection.ConnectAsync(
                    endpoint,
                    participantIdentity,
                    new TrustRecord(hostIdentity.PublicIdentity, Now, participantToHost),
                    [Version],
                    cancellationToken: deadline.Token);
                Assert.Equal(new ProtocolVersion(1, 7), participantConnection.ProtocolVersion);
                participantRun = participantSessionHandler.RunAsync(
                    participantConnection,
                    participantSessionStop.Token).AsTask();
                AuthenticatedRemoteWindowConnectionLease hostLease =
                    await WaitForConnectionLeaseOnChangeAsync(
                        hostHandler,
                        ParticipantDeviceId,
                        deadline.Token);
                var hostConnection = new ViewerObservingHostConnection(
                    new AuthenticatedDesktopRemoteWindowHostConnection(hostLease),
                    viewer,
                    rendererFactory,
                    capture);

                RemoteWindowCommandResult started = await coordinator.StartAsync(
                    new DesktopRemoteWindowHostStartRequest(
                        sourceLease,
                        ownerGeneration: 1,
                        hostConnection,
                        protection,
                        MirrorParticipantRole.ViewOnly),
                    deadline.Token);

                Assert.Equal(RemoteWindowCommandStatus.Applied, started.Status);
                Assert.True(hostConnection.ReadyObserved);
                Assert.True(hostConnection.AttachmentObserved);
                Assert.True(hostConnection.AdmissionPublished);
                Assert.Equal(1, rendererFactory.PrepareCount);
                Assert.Equal(MirrorParticipantRole.ViewOnly, rendererFactory.Request!.RequestedRole);
                Assert.Null(viewer.ImageSource);
                Assert.False(viewer.IsViewing);
                Assert.False(rendererFactory.Rendered.Task.IsCompleted);
                Assert.Equal(1, capture.PreAdmissionFrameDisposeCount);
                Assert.Equal(0, hostConnection.MediaSendCount);
                Assert.True(hostMedia.TryGet(
                    ParticipantDeviceId,
                    out AuthenticatedRemoteWindowMediaSession? attachedHostMedia));
                Assert.True(participantMedia.TryGet(
                    HostDeviceId,
                    out AuthenticatedRemoteWindowMediaSession? attachedParticipantMedia));
                Assert.True(attachedHostMedia!.IsAttached);
                Assert.True(attachedParticipantMedia!.IsAttached);
                Assert.Equal(attachedHostMedia.Binding, attachedParticipantMedia.Binding);
                Assert.Equal(Version, attachedHostMedia.ProtocolVersion);
                Assert.Equal(Version, attachedParticipantMedia.ProtocolVersion);

                RemoteWindowMediaSessionBudget activeBudget = Assert.IsType<
                    RemoteWindowMediaSessionBudget>(coordinator.ActiveMediaBudget);
                TrackingMemoryOwner sourceFrame = await capture.EmitFrameAsync(2, deadline.Token);
                await rendererFactory.Rendered.Task.WaitAsync(deadline.Token);
                await WaitForDecodedFrameZeroAsync(rendererFactory, deadline.Token);

                Assert.Equal(1, sourceFrame.DisposeCount);
                AssertOpaqueRed(rendererFactory.RenderedPixels);
                Assert.Equal(16, rendererFactory.DecodedPixels.Length);
                Assert.All(rendererFactory.DecodedPixels.ToArray(), value => Assert.Equal(0, value));
                Assert.True(viewer.IsViewing);
                WriteableBitmap displayed = Assert.IsType<WriteableBitmap>(viewer.ImageSource);
                using (var framebuffer = displayed.Lock())
                {
                    Assert.Equal(2, framebuffer.Size.Width);
                    Assert.Equal(2, framebuffer.Size.Height);
                    var displayedPixels = new byte[16];
                    for (int row = 0; row < 2; row++)
                    {
                        Marshal.Copy(
                            framebuffer.Address + (row * framebuffer.RowBytes),
                            displayedPixels,
                            row * 8,
                            8);
                    }

                    Assert.Equal(rendererFactory.RenderedPixels, displayedPixels);
                    AssertOpaqueRed(displayedPixels);
                }

                Assert.True(hostConnection.MediaSendCount >= 1);
                Assert.Equal(0, hostConnection.MediaSentBeforeAdmissionCount);
                IRemoteWindowControlChannel participantChannel =
                    await WaitForRemoteWindowChannelAsync(
                        participantHandler,
                        HostDeviceId,
                        deadline.Token);
                RemoteWindowSharingSnapshot snapshot = Assert.IsType<
                    RemoteWindowSharingSnapshot>(coordinator.Snapshot);
                Assert.Equal(MirrorParticipantRole.ViewOnly, snapshot.Participants[ParticipantDeviceId]);
                RemoteWindowControlDeliveryResult driverRequest =
                    await participantChannel.RequestDriverAsync(
                        RemoteWindowDriverRequest.Create(
                            CorrelationId.From(Guid.NewGuid()),
                            controlPeer.SessionId,
                            controlPeer.ActivityId,
                            HostDeviceId,
                            ParticipantDeviceId,
                            Assert.IsType<long>(snapshot.DriverLeaseEpoch),
                            TimeSpan.FromSeconds(10),
                            Now.AddSeconds(5)),
                        deadline.Token);
                RemoteWindowParticipantState rejectedDriver = Assert.IsType<
                    RemoteWindowParticipantState>(driverRequest.State);
                Assert.Equal(RemoteWindowControlDeliveryStatus.Acknowledged, driverRequest.Status);
                Assert.Equal(RemoteWindowControlOutcome.Rejected, rejectedDriver.Outcome);
                Assert.Equal("participant_not_driver_eligible", rejectedDriver.ReasonCode);
                Assert.Equal(MirrorParticipantRole.ViewOnly, rejectedDriver.EffectiveRole);
                Assert.Equal(HostDeviceId, rejectedDriver.CurrentDriverDeviceId);
                Assert.Empty(input.Batches);

                await viewer.StopReceivingAsync().AsTask().WaitAsync(deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                Assert.Equal(1, stopReceivingCount);
                Assert.False(viewer.IsReceivingEnabled);
                Assert.False(viewer.IsViewing);
                Assert.Null(viewer.ImageSource);
                await ObserveSessionStopAsync(participantRun);
                deadline.Token.ThrowIfCancellationRequested();
                await WaitForCleanupOnChangeAsync(
                    hostHandler,
                    participantHandler,
                    hostMedia,
                    participantMedia,
                    deadline.Token);
                await WaitForViewerStoppedHostAsync(
                    coordinator,
                    activeBudget,
                    capture,
                    protection,
                    permissions,
                    emergencyStops,
                    controlPeer,
                    deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();

                Assert.Null(coordinator.Snapshot);
                Assert.Null(coordinator.TerminalFailure);
                Assert.Equal(RemoteWindowMediaBudgetSnapshot.Empty, activeBudget.Snapshot);
                Assert.True(rendererFactory.IsDisposed);
                Assert.False(capture.HasCurrentCapture);
                Assert.True(capture.StopCount + capture.EmergencyStopCount >= 1);
                Assert.True(input.StopCount + input.EmergencyStopCount >= 1);
                Assert.True(sessions.DisconnectAllCount >= 1);
                Assert.True(protection.IsDisposed);
                Assert.Equal(0, permissions.ObserverCount);
                Assert.Equal(0, permissions.CurrentPreparationReservationCount);
                Assert.False(emergencyStops.HasCurrentRegistration);
                Assert.False(hostMedia.TryGet(ParticipantDeviceId, out _));
                Assert.False(participantMedia.TryGet(HostDeviceId, out _));
                Assert.Equal(0, hostMedia.Routes.Count);
                Assert.Equal(0, participantMedia.Routes.Count);
                Assert.Empty(hostHandler.GetConnectedPeers());
                Assert.Empty(participantHandler.GetConnectedPeers());
                Assert.False(hostHandler.TryAcquireRemoteWindowConnection(ParticipantDeviceId, out _));
                Assert.False(participantHandler.TryAcquireRemoteWindowPeerConnection(HostDeviceId, out _));
                Assert.False(hostHandler.TryGetRemoteWindowChannel(ParticipantDeviceId, out _));
                Assert.False(participantHandler.TryGetRemoteWindowChannel(HostDeviceId, out _));
                Assert.False(participantHandler.TryGetRemoteWindowPreparationChannel(HostDeviceId, out _));
                Assert.False(controlPeer.HasRetainedGeneration);
            }
            finally
            {
                try
                {
                    participantSessionStop.Cancel();
                    if (participantConnection is not null)
                    {
                        await participantConnection.DisposeAsync();
                    }

                    if (participantRun is not null)
                    {
                        await ObserveSessionStopAsync(participantRun);
                    }
                }
                finally
                {
                    listenerStop.Cancel();
                    await ObserveListenerStopAsync(listenerRun, listenerStop.Token);
                }
            }

            return 0;

            bool TryAcquireParticipantConnection(
                DeviceId peerDeviceId,
                out AuthenticatedRemoteWindowConnectionLease? lease) =>
                participantHandler!.TryAcquireRemoteWindowPeerConnection(peerDeviceId, out lease);

            ValueTask StopReceivingAsync()
            {
                stopReceivingCount++;
                return preparationPeer!.StopReceivingAsync();
            }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(45));
    }

    private static async Task WaitForDecodedFrameZeroAsync(
        ObservingViewerRendererFactory rendererFactory,
        CancellationToken cancellationToken)
    {
        while (rendererFactory.DecodedPixels.Span.ContainsAnyExcept((byte)0))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
        }
    }

    private static async Task WaitForViewerStoppedHostAsync(
        DesktopRemoteWindowHostCoordinator coordinator,
        RemoteWindowMediaSessionBudget activeBudget,
        RecordingCaptureBoundary capture,
        RecordingProtectionSource protection,
        RecordingPermissionBoundary permissions,
        RecordingEmergencyStopRegistrar emergencyStops,
        DesktopRemoteWindowHostControlPeer controlPeer,
        CancellationToken cancellationToken)
    {
        while (coordinator.Snapshot is not null
            || coordinator.HasRetiringGeneration
            || activeBudget.Snapshot != RemoteWindowMediaBudgetSnapshot.Empty
            || capture.HasCurrentCapture
            || !protection.IsDisposed
            || permissions.ObserverCount != 0
            || permissions.CurrentPreparationReservationCount != 0
            || emergencyStops.HasCurrentRegistration
            || controlPeer.HasRetainedGeneration)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
        }
    }

    private sealed class ObservingViewerRendererFactory(RemoteWindowViewerViewModel viewer) :
        IDesktopRemoteWindowParticipantRendererFactory
    {
        public int PrepareCount { get; private set; }

        public RemoteWindowPreparationRequest? Request { get; private set; }

        public ReadOnlyMemory<byte> DecodedPixels { get; private set; }

        public byte[] RenderedPixels { get; private set; } = [];

        public bool IsDisposed { get; private set; }

        public TaskCompletionSource Rendered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IDesktopRemoteWindowParticipantRenderer?> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            PrepareCount++;
            Assert.Null(viewer.ImageSource);
            IDesktopRemoteWindowParticipantRenderer renderer =
                Assert.IsAssignableFrom<IDesktopRemoteWindowParticipantRenderer>(
                    await viewer.PrepareAsync(request, cancellationToken));
            Assert.Null(viewer.ImageSource);
            return new ObservingViewerRenderer(this, renderer);
        }

        private sealed class ObservingViewerRenderer(
            ObservingViewerRendererFactory owner,
            IDesktopRemoteWindowParticipantRenderer inner) :
            IDesktopRemoteWindowParticipantRenderer
        {
            public async ValueTask RenderAsync(
                DesktopRemoteWindowBgraFrame frame,
                CancellationToken cancellationToken)
            {
                owner.DecodedPixels = frame.Pixels;
                owner.RenderedPixels = frame.Pixels.ToArray();
                await inner.RenderAsync(frame, cancellationToken);
                owner.Rendered.TrySetResult();
            }

            public async ValueTask DisposeAsync()
            {
                await inner.DisposeAsync();
                owner.IsDisposed = true;
            }
        }
    }

    private sealed class ViewerObservingHostConnection(
        AuthenticatedDesktopRemoteWindowHostConnection inner,
        RemoteWindowViewerViewModel viewer,
        ObservingViewerRendererFactory rendererFactory,
        RecordingCaptureBoundary capture) : IDesktopRemoteWindowHostConnection
    {
        private int admissionPublished;
        private int mediaSendCount;
        private int mediaSentBeforeAdmissionCount;

        public DeviceId LocalDeviceId => inner.LocalDeviceId;

        public DeviceId PeerDeviceId => inner.PeerDeviceId;

        public ProtocolVersion ProtocolVersion => inner.ProtocolVersion;

        public bool IsCurrent => inner.IsCurrent;

        public string AuthenticatedPeerFingerprint => inner.AuthenticatedPeerFingerprint;

        public bool ReadyObserved { get; private set; }

        public bool AttachmentObserved { get; private set; }

        public bool AdmissionPublished => Volatile.Read(ref admissionPublished) != 0;

        public int MediaSendCount => Volatile.Read(ref mediaSendCount);

        public int MediaSentBeforeAdmissionCount => Volatile.Read(ref mediaSentBeforeAdmissionCount);

        public AuthenticatedRemoteWindowConnectionPreparationReservationResult TryReservePreparation(
            IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink sink) =>
            inner.TryReservePreparation(sink);

        public IDisposable RegisterRevocationCallback(Action callback) =>
            inner.RegisterRevocationCallback(callback);

        public void PrepareResponderRoute(
            RemoteWindowSessionId sessionId,
            ActivityId activityId,
            IRemoteWindowHostPreparationAdmission admission,
            TimeSpan lifetime) =>
            inner.PrepareResponderRoute(sessionId, activityId, admission, lifetime);

        public async ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            IRemoteWindowHostPreparationAdmission admission,
            CancellationToken cancellationToken)
        {
            Assert.Equal(0, capture.StartCount);
            Assert.Null(viewer.ImageSource);
            RemoteWindowPreparationDeliveryResult result = await inner.PrepareAsync(
                request,
                admission,
                cancellationToken);
            Assert.Equal(RemoteWindowControlDeliveryStatus.Acknowledged, result.Status);
            Assert.Equal(RemoteWindowPreparationOutcome.Ready, result.Response!.Outcome);
            Assert.Equal(1, rendererFactory.PrepareCount);
            Assert.Null(viewer.ImageSource);
            Assert.False(rendererFactory.Rendered.Task.IsCompleted);
            ReadyObserved = true;
            return result;
        }

        public async ValueTask WaitForMediaAttachmentAsync(CancellationToken cancellationToken)
        {
            await inner.WaitForMediaAttachmentAsync(cancellationToken);
            Assert.True(ReadyObserved);
            Assert.Null(viewer.ImageSource);
            Assert.Equal(0, capture.StartCount);
            AttachmentObserved = true;
        }

        public async ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken)
        {
            Assert.True(AttachmentObserved);
            Assert.True(capture.PreAdmissionFrameDisposed);
            Assert.Equal(1, capture.StartCount);
            Assert.Null(viewer.ImageSource);
            Assert.False(rendererFactory.Rendered.Task.IsCompleted);
            await inner.PublishAdmissionStateAsync(state, cancellationToken);
            Assert.Null(viewer.ImageSource);
            Volatile.Write(ref admissionPublished, 1);
        }

        public ValueTask SendAsync(
            RemoteWindowMediaFrame frame,
            CancellationToken cancellationToken = default)
        {
            if (!AdmissionPublished)
            {
                Interlocked.Increment(ref mediaSentBeforeAdmissionCount);
            }

            Interlocked.Increment(ref mediaSendCount);
            return inner.SendAsync(frame, cancellationToken);
        }

        public ValueTask FailCloseAsync() => inner.FailCloseAsync();

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
