using System.Runtime.CompilerServices;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopRemoteWindowHostControlPeerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId HostDeviceId = DeviceId.Parse(
        "11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId ParticipantDeviceId = DeviceId.Parse(
        "22222222-2222-2222-2222-222222222222");

    private static readonly DeviceId OtherParticipantDeviceId = DeviceId.Parse(
        "33333333-3333-3333-3333-333333333333");

    private static readonly ActivityId ActivityId = ActivityId.Parse(
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly RemoteWindowSessionId ReplacementSessionId =
        RemoteWindowSessionId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static readonly RemoteWindowSessionId LatestSessionId =
        RemoteWindowSessionId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task CurrentGenerationRoutesExactParticipantDriverRequest()
    {
        using RemoteWindowSessionController controller = CreateController();
        Assert.True((await controller.StartAsync(SafeProtection())).Succeeded);
        Assert.True((await controller.AddParticipantAsync(
            ParticipantDeviceId,
            MirrorParticipantRole.DriverEligible)).Succeeded);
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        using DesktopRemoteWindowHostControlRegistration registration =
            peer.Register(
                generation: 7,
                ParticipantDeviceId,
                SessionId,
                controller);
        RemoteWindowSharingSnapshot before = controller.Snapshot;
        RemoteWindowDriverRequest request = RemoteWindowDriverRequest.Create(
            CorrelationId.From(Guid.NewGuid()),
            SessionId,
            ActivityId,
            HostDeviceId,
            ParticipantDeviceId,
            Assert.IsType<long>(before.DriverLeaseEpoch),
            TimeSpan.FromSeconds(5),
            Now.AddSeconds(2));

        RemoteWindowParticipantState state = await peer.RequestDriverAsync(
            request,
            CancellationToken.None);

        Assert.Equal(RemoteWindowControlOutcome.Applied, state.Outcome);
        Assert.Equal(ParticipantDeviceId, state.CurrentDriverDeviceId);
        Assert.Equal(ParticipantDeviceId, controller.Snapshot.CurrentDriverDeviceId);
    }

    [Fact]
    public async Task IdleAndClosedGenerationPeerDisconnectsAreNoOps()
    {
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);

        await peer.PeerDisconnectedAsync(
            ParticipantDeviceId,
            CancellationToken.None);

        using RemoteWindowSessionController controller = CreateController();
        DesktopRemoteWindowHostControlRegistration registration = peer.Register(
            generation: 7,
            ParticipantDeviceId,
            SessionId,
            controller);
        registration.CloseNow();
        await peer.PeerDisconnectedAsync(
            ParticipantDeviceId,
            CancellationToken.None);

        Assert.False(registration.IsCurrent);
        Assert.Throws<InvalidOperationException>(() => peer.SessionId);
        registration.Dispose();
    }

    [Fact]
    public async Task NonmatchingParticipantPeerDisconnectDoesNotReachCurrentGeneration()
    {
        var sessions = new RecordingSessionBoundary();
        using RemoteWindowSessionController controller = CreateController(
            authorization: new GrantingAuthorizationSource(
                CapabilityGrant.Of(Capability.MirrorView)),
            sessions: sessions);
        Assert.True((await controller.StartAsync(SafeProtection())).Succeeded);
        Assert.True((await controller.AddParticipantAsync(
            ParticipantDeviceId,
            MirrorParticipantRole.ViewOnly)).Succeeded);
        Assert.True((await controller.AddParticipantAsync(
            OtherParticipantDeviceId,
            MirrorParticipantRole.ViewOnly)).Succeeded);
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        using DesktopRemoteWindowHostControlRegistration registration =
            peer.Register(
                generation: 7,
                ParticipantDeviceId,
                SessionId,
                controller);

        await peer.PeerDisconnectedAsync(
            OtherParticipantDeviceId,
            CancellationToken.None);

        Assert.True(registration.IsCurrent);
        Assert.Contains(
            ParticipantDeviceId,
            controller.Snapshot.Participants.Keys);
        Assert.Contains(
            OtherParticipantDeviceId,
            controller.Snapshot.Participants.Keys);
        Assert.Empty(sessions.DisconnectedPeers);
    }

    [Fact]
    public async Task ExactParticipantPeerDisconnectRoutesAndDrainsBeforeReplacement()
    {
        using var sessions = new BlockingSessionBoundary();
        using RemoteWindowSessionController controller = CreateController(
            sessions: sessions);
        using RemoteWindowSessionController replacementController =
            CreateController();
        Assert.True((await controller.StartAsync(SafeProtection())).Succeeded);
        Assert.True((await controller.AddParticipantAsync(
            ParticipantDeviceId,
            MirrorParticipantRole.ViewOnly)).Succeeded);
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        using DesktopRemoteWindowHostControlRegistration registration =
            peer.Register(
                generation: 7,
                ParticipantDeviceId,
                SessionId,
                controller);
        Task disconnecting = StartDedicated(async () =>
            await peer.PeerDisconnectedAsync(
                ParticipantDeviceId,
                CancellationToken.None));
        await sessions.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var replacementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<DesktopRemoteWindowHostControlRegistration> replacing =
            StartDedicated(() =>
            {
                replacementStarted.TrySetResult();
                return peer.Register(
                    generation: 8,
                    ParticipantDeviceId,
                    ReplacementSessionId,
                    replacementController);
            });
        try
        {
            await replacementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForNoCurrentWhilePendingAsync(peer, replacing);
            Assert.False(disconnecting.IsCompleted);
            Assert.False(replacing.IsCompleted);
        }
        finally
        {
            sessions.Release();
        }

        await disconnecting.WaitAsync(TimeSpan.FromSeconds(5));
        using DesktopRemoteWindowHostControlRegistration replacement =
            await replacing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(
            ParticipantDeviceId,
            controller.Snapshot.Participants.Keys);
        Assert.Equal([ParticipantDeviceId], sessions.DisconnectedPeers);
        Assert.True(replacement.IsCurrent);
    }

    [Fact]
    public async Task StaleRegistrationCannotClearOrReceiveForNewGeneration()
    {
        using RemoteWindowSessionController oldController = CreateController();
        using RemoteWindowSessionController currentController = CreateController();
        Assert.True((await oldController.StartAsync(SafeProtection())).Succeeded);
        Assert.True((await currentController.StartAsync(SafeProtection())).Succeeded);
        Assert.True((await oldController.AddParticipantAsync(
            ParticipantDeviceId,
            MirrorParticipantRole.DriverEligible)).Succeeded);
        Assert.True((await currentController.AddParticipantAsync(
            ParticipantDeviceId,
            MirrorParticipantRole.DriverEligible)).Succeeded);
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        DesktopRemoteWindowHostControlRegistration stale = peer.Register(
            generation: 7,
            ParticipantDeviceId,
            SessionId,
            oldController);
        using DesktopRemoteWindowHostControlRegistration current = peer.Register(
            generation: 8,
            ParticipantDeviceId,
            ReplacementSessionId,
            currentController);

        stale.Dispose();

        Assert.True(peer.HasRetainedGeneration);
        Assert.True(current.IsCurrent);
        Assert.Equal(ReplacementSessionId, peer.SessionId);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await peer.RequestDriverAsync(
                CreateDriverRequest(SessionId, oldController),
                CancellationToken.None));
        RemoteWindowParticipantState state = await peer.RequestDriverAsync(
            CreateDriverRequest(ReplacementSessionId, currentController),
            CancellationToken.None);
        Assert.Equal(ParticipantDeviceId, state.CurrentDriverDeviceId);
    }

    [Fact]
    public async Task ReplacementDoesNotPublishUntilOldRoutedCallDrains()
    {
        var oldInput = new BlockingInputBoundary();
        using RemoteWindowSessionController oldController =
            CreateController(oldInput);
        using RemoteWindowSessionController replacementController =
            CreateController();
        Assert.True((await oldController.StartAsync(SafeProtection())).Succeeded);
        Assert.True((await replacementController.StartAsync(SafeProtection())).Succeeded);
        Assert.True((await oldController.AddParticipantAsync(
            ParticipantDeviceId,
            MirrorParticipantRole.DriverEligible)).Succeeded);
        Assert.True((await replacementController.AddParticipantAsync(
            ParticipantDeviceId,
            MirrorParticipantRole.DriverEligible)).Succeeded);
        Assert.True((await oldController.TransferDriverAsync(
            ParticipantDeviceId,
            TimeSpan.FromSeconds(5))).Succeeded);
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        using DesktopRemoteWindowHostControlRegistration oldRegistration =
            peer.Register(
                generation: 7,
                ParticipantDeviceId,
                SessionId,
                oldController);
        Task<RemoteWindowParticipantState> oldCall = peer.SendInputAsync(
                CreateInputRequest(SessionId, oldController),
                CancellationToken.None)
            .AsTask();
        await oldInput.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task<DesktopRemoteWindowHostControlRegistration> replacing = StartDedicated(
            () => peer.Register(
                generation: 8,
                ParticipantDeviceId,
                ReplacementSessionId,
                replacementController));

        await WaitForRegistrationDrainAsync(peer, replacing);
        Assert.False(replacing.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => peer.SessionId);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await peer.SendInputAsync(
                CreateInputRequest(SessionId, oldController),
                CancellationToken.None));

        oldInput.Release.TrySetResult();
        Assert.Equal(
            RemoteWindowControlOutcome.Applied,
            (await oldCall.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        using DesktopRemoteWindowHostControlRegistration replacement =
            await replacing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(replacement.IsCurrent);
        Assert.Equal(ReplacementSessionId, peer.SessionId);
    }

    [Fact]
    public async Task CloseNowReturnsWhileRoutedCallRemainsBlocked()
    {
        var input = new BlockingInputBoundary();
        using RemoteWindowSessionController controller = CreateController(input);
        await StartDriverAsync(controller);
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        DesktopRemoteWindowHostControlRegistration registration = peer.Register(
            generation: 7,
            ParticipantDeviceId,
            SessionId,
            controller);
        Task<RemoteWindowParticipantState> routedCall = Task.Run(async () =>
            await peer.SendInputAsync(
                CreateInputRequest(SessionId, controller),
                CancellationToken.None));
        await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        registration.CloseNow();

        Assert.False(registration.IsCurrent);
        Assert.False(routedCall.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => peer.SessionId);
        input.Release.TrySetResult();
        await routedCall.WaitAsync(TimeSpan.FromSeconds(5));
        registration.Dispose();
    }

    [Fact]
    public async Task DisposeFromRoutedCallbackDoesNotWaitForItsOwnCall()
    {
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        DesktopRemoteWindowHostControlRegistration? registration = null;
        bool? retainedDuringCallback = null;
        var input = new CallbackInputBoundary(() =>
        {
            registration!.Dispose();
            retainedDuringCallback = peer.HasRetainedGeneration;
        });
        using RemoteWindowSessionController controller = CreateController(input);
        await StartDriverAsync(controller);
        registration = peer.Register(
            generation: 7,
            ParticipantDeviceId,
            SessionId,
            controller);

        RemoteWindowParticipantState state = await Task.Run(async () =>
                await peer.SendInputAsync(
                    CreateInputRequest(SessionId, controller),
                    CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RemoteWindowControlOutcome.Applied, state.Outcome);
        Assert.True(input.CallbackReturned.Task.IsCompletedSuccessfully);
        Assert.True(retainedDuringCallback);
        Assert.False(peer.HasRetainedGeneration);
        Assert.False(registration.IsCurrent);
        Assert.Throws<InvalidOperationException>(() => peer.SessionId);
    }

    [Fact]
    public void DisposedLatestGenerationReleasesControllerAndPreservesGenerationFloor()
    {
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        WeakReference controllerReference =
            RegisterAndDisposeControllerWithoutLeakingLocals(peer);

        Assert.False(peer.HasRetainedGeneration);
        CollectGarbage();
        Assert.False(controllerReference.IsAlive);

        using RemoteWindowSessionController replacementController =
            CreateController();
        Assert.Throws<InvalidOperationException>(() => peer.Register(
            generation: 7,
            ParticipantDeviceId,
            SessionId,
            replacementController));
        using DesktopRemoteWindowHostControlRegistration replacement =
            peer.Register(
                generation: 8,
                ParticipantDeviceId,
                ReplacementSessionId,
                replacementController);
        Assert.True(replacement.IsCurrent);
    }

    [Fact]
    public async Task ExternalDisposeWaitsForRoutedCallToDrain()
    {
        var input = new BlockingInputBoundary();
        using RemoteWindowSessionController controller = CreateController(input);
        await StartDriverAsync(controller);
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        DesktopRemoteWindowHostControlRegistration registration = peer.Register(
            generation: 7,
            ParticipantDeviceId,
            SessionId,
            controller);
        Task<RemoteWindowParticipantState> routedCall = Task.Run(async () =>
            await peer.SendInputAsync(
                CreateInputRequest(SessionId, controller),
                CancellationToken.None));
        await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task disposing = StartDedicated(registration.Dispose);
        await WaitForNoCurrentWhilePendingAsync(peer, disposing);

        Assert.False(disposing.IsCompleted);
        Assert.True(peer.HasRetainedGeneration);
        input.Release.TrySetResult();
        await routedCall.WaitAsync(TimeSpan.FromSeconds(5));
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(peer.HasRetainedGeneration);
    }

    [Fact]
    public async Task ConcurrentReplacementAndDisposeShareOldCallDrain()
    {
        var oldInput = new BlockingInputBoundary();
        using RemoteWindowSessionController oldController =
            CreateController(oldInput);
        using RemoteWindowSessionController replacementController =
            CreateController();
        await StartDriverAsync(oldController);
        Assert.True((await replacementController.StartAsync(SafeProtection())).Succeeded);
        Assert.True((await replacementController.AddParticipantAsync(
            ParticipantDeviceId,
            MirrorParticipantRole.DriverEligible)).Succeeded);
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        DesktopRemoteWindowHostControlRegistration oldRegistration = peer.Register(
            generation: 7,
            ParticipantDeviceId,
            SessionId,
            oldController);
        Task<RemoteWindowParticipantState> routedCall = Task.Run(async () =>
            await peer.SendInputAsync(
                CreateInputRequest(SessionId, oldController),
                CancellationToken.None));
        await oldInput.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<DesktopRemoteWindowHostControlRegistration> replacing = StartDedicated(
            () => peer.Register(
                generation: 8,
                ParticipantDeviceId,
                ReplacementSessionId,
                replacementController));
        await WaitForNoCurrentWhilePendingAsync(peer, replacing);

        var disposeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task disposing = StartDedicated(() =>
        {
            disposeStarted.TrySetResult();
            oldRegistration.Dispose();
        });
        await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(replacing.IsCompleted);
        Assert.False(disposing.IsCompleted);

        oldInput.Release.TrySetResult();
        await routedCall.WaitAsync(TimeSpan.FromSeconds(5));
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        using DesktopRemoteWindowHostControlRegistration replacement =
            await replacing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(replacement.IsCurrent);
        Assert.Equal(ReplacementSessionId, peer.SessionId);
    }

    [Fact]
    public async Task ConcurrentRegistersPublishInStrictGenerationOrder()
    {
        var oldInput = new BlockingInputBoundary();
        using RemoteWindowSessionController oldController =
            CreateController(oldInput);
        using RemoteWindowSessionController generationEightController =
            CreateController();
        using RemoteWindowSessionController generationNineController =
            CreateController();
        await StartDriverAsync(oldController);
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        using DesktopRemoteWindowHostControlRegistration oldRegistration =
            peer.Register(
                generation: 7,
                ParticipantDeviceId,
                SessionId,
                oldController);
        Task<RemoteWindowParticipantState> routedCall = Task.Run(async () =>
            await peer.SendInputAsync(
                CreateInputRequest(SessionId, oldController),
                CancellationToken.None));
        await oldInput.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<DesktopRemoteWindowHostControlRegistration> generationEight =
            StartDedicated(() => peer.Register(
                generation: 8,
                ParticipantDeviceId,
                ReplacementSessionId,
                generationEightController));
        await WaitForNoCurrentWhilePendingAsync(peer, generationEight);
        var generationNineStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<DesktopRemoteWindowHostControlRegistration> generationNine =
            StartDedicated(() =>
            {
                generationNineStarted.TrySetResult();
                return peer.Register(
                    generation: 9,
                    ParticipantDeviceId,
                    LatestSessionId,
                    generationNineController);
            });
        await generationNineStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(generationEight.IsCompleted);
        Assert.False(generationNine.IsCompleted);

        oldInput.Release.TrySetResult();
        await routedCall.WaitAsync(TimeSpan.FromSeconds(5));
        using DesktopRemoteWindowHostControlRegistration generationEightRegistration =
            await generationEight.WaitAsync(TimeSpan.FromSeconds(5));
        using DesktopRemoteWindowHostControlRegistration generationNineRegistration =
            await generationNine.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(generationEightRegistration.IsCurrent);
        Assert.True(generationNineRegistration.IsCurrent);
        Assert.Equal(LatestSessionId, peer.SessionId);
        generationEightRegistration.Dispose();
        Assert.True(generationNineRegistration.IsCurrent);
    }

    [Fact]
    public async Task RegisterFromRoutedCallbackFailsWithoutDeadlock()
    {
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        using RemoteWindowSessionController replacementController =
            CreateController();
        Exception? replacementFailure = null;
        var input = new CallbackInputBoundary(() =>
        {
            replacementFailure = Record.Exception(() => peer.Register(
                generation: 8,
                ParticipantDeviceId,
                ReplacementSessionId,
                replacementController));
        });
        using RemoteWindowSessionController controller = CreateController(input);
        await StartDriverAsync(controller);
        using DesktopRemoteWindowHostControlRegistration registration =
            peer.Register(
                generation: 7,
                ParticipantDeviceId,
                SessionId,
                controller);

        await Task.Run(async () =>
                await peer.SendInputAsync(
                    CreateInputRequest(SessionId, controller),
                    CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<InvalidOperationException>(replacementFailure);
        Assert.True(registration.IsCurrent);
        Assert.Equal(SessionId, peer.SessionId);
    }

    [Fact]
    public async Task DisposedGenerationFailsClosedAndCannotBeReused()
    {
        using RemoteWindowSessionController controller = CreateController();
        Assert.True((await controller.StartAsync(SafeProtection())).Succeeded);
        Assert.True((await controller.AddParticipantAsync(
            ParticipantDeviceId,
            MirrorParticipantRole.DriverEligible)).Succeeded);
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        DesktopRemoteWindowHostControlRegistration registration = peer.Register(
            generation: 7,
            ParticipantDeviceId,
            SessionId,
            controller);

        registration.Dispose();

        Assert.False(registration.IsCurrent);
        Assert.Throws<InvalidOperationException>(() => peer.ActivityId);
        Assert.Throws<InvalidOperationException>(() => peer.SessionId);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await peer.RequestDriverAsync(
                CreateDriverRequest(SessionId, controller),
                CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => peer.Register(
            generation: 7,
            ParticipantDeviceId,
            SessionId,
            controller));
    }

    [Fact]
    public async Task WrongParticipantFailsClosedBeforeControllerAuthority()
    {
        using RemoteWindowSessionController controller = CreateController();
        Assert.True((await controller.StartAsync(SafeProtection())).Succeeded);
        Assert.True((await controller.AddParticipantAsync(
            ParticipantDeviceId,
            MirrorParticipantRole.DriverEligible)).Succeeded);
        var peer = new DesktopRemoteWindowHostControlPeer(HostDeviceId);
        using DesktopRemoteWindowHostControlRegistration registration =
            peer.Register(
                generation: 7,
                ParticipantDeviceId,
                SessionId,
                controller);
        RemoteWindowDriverRequest request = RemoteWindowDriverRequest.Create(
            CorrelationId.From(Guid.NewGuid()),
            SessionId,
            ActivityId,
            HostDeviceId,
            OtherParticipantDeviceId,
            Assert.IsType<long>(controller.Snapshot.DriverLeaseEpoch),
            TimeSpan.FromSeconds(5),
            Now.AddSeconds(2));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await peer.RequestDriverAsync(request, CancellationToken.None));

        Assert.Equal(HostDeviceId, controller.Snapshot.CurrentDriverDeviceId);
    }

    private static RemoteWindowDriverRequest CreateDriverRequest(
        RemoteWindowSessionId sessionId,
        RemoteWindowSessionController controller) => RemoteWindowDriverRequest.Create(
            CorrelationId.From(Guid.NewGuid()),
            sessionId,
            ActivityId,
            HostDeviceId,
            ParticipantDeviceId,
            Assert.IsType<long>(controller.Snapshot.DriverLeaseEpoch),
            TimeSpan.FromSeconds(5),
            Now.AddSeconds(2));

    private static RemoteWindowInputRequest CreateInputRequest(
        RemoteWindowSessionId sessionId,
        RemoteWindowSessionController controller) => RemoteWindowInputRequest.Create(
            CorrelationId.From(Guid.NewGuid()),
            sessionId,
            ActivityId,
            HostDeviceId,
            ParticipantDeviceId,
            Assert.IsType<long>(controller.Snapshot.DriverLeaseEpoch),
            RemoteInputBatch.Create([RemoteInputEvent.PointerMove(0.25, 0.75)]),
            Now.AddSeconds(2));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterAndDisposeControllerWithoutLeakingLocals(
        DesktopRemoteWindowHostControlPeer peer)
    {
        var controller = CreateController();
        var reference = new WeakReference(controller);
        DesktopRemoteWindowHostControlRegistration registration = peer.Register(
            generation: 7,
            ParticipantDeviceId,
            SessionId,
            controller);
        Assert.True(peer.HasRetainedGeneration);

        registration.Dispose();
        controller.Dispose();
        return reference;
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static async Task StartDriverAsync(
        RemoteWindowSessionController controller)
    {
        Assert.True((await controller.StartAsync(SafeProtection())).Succeeded);
        Assert.True((await controller.AddParticipantAsync(
            ParticipantDeviceId,
            MirrorParticipantRole.DriverEligible)).Succeeded);
        Assert.True((await controller.TransferDriverAsync(
            ParticipantDeviceId,
            TimeSpan.FromSeconds(5))).Succeeded);
    }

    private static RemoteWindowSessionController CreateController(
        IRemoteInputBoundary? input = null,
        IMirrorAuthorizationSource? authorization = null,
        ILocalSharingSessionBoundary? sessions = null) => new(
        HostDeviceId,
        ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId,
                ActivityKind.Parse("workspace.note/v1"),
                HostDeviceId,
                "Control routing test",
                JsonSerializer.Serialize(new { text = "fixture" })),
            ActivityPlacement.On(HostDeviceId),
            revision: 1),
        new FixedClock(Now),
        authorization ?? new FixedAuthorizationSource(
            CapabilityGrant.Of(Capability.MirrorView, Capability.MirrorDrive)),
        new ConfirmingCaptureBoundary(),
        input ?? new ConfirmingInputBoundary(),
        sessions ?? new ConfirmingSessionBoundary(),
        TimeSpan.FromMinutes(1));

    private static Task WaitForRegistrationDrainAsync(
        DesktopRemoteWindowHostControlPeer peer,
        Task replacement) => WaitForNoCurrentWhilePendingAsync(peer, replacement);

    private static Task StartDedicated(Action operation) => Task.Factory.StartNew(
        operation,
        CancellationToken.None,
        TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
        TaskScheduler.Default);

    private static Task<T> StartDedicated<T>(Func<T> operation) =>
        Task.Factory.StartNew(
            operation,
            CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);

    private static Task StartDedicated(Func<Task> operation) =>
        Task.Factory.StartNew(
                operation,
                CancellationToken.None,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default)
            .Unwrap();

    private static async Task WaitForNoCurrentWhilePendingAsync(
        DesktopRemoteWindowHostControlPeer peer,
        Task pending)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            Assert.False(
                pending.IsCompleted,
                "The lifetime operation completed before the routed call drained.");
            try
            {
                _ = peer.SessionId;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }
            catch (OperationCanceledException)
                when (timeout.IsCancellationRequested)
            {
                break;
            }
        }

        Assert.Fail("The lifetime operation did not retire the old generation.");
    }

    private static ProtectionSnapshot SafeProtection() => new(
        ProtectionKind.Safe,
        Now,
        "test_protection");

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FixedAuthorizationSource(CapabilityGrant grant) :
        IMirrorAuthorizationSource
    {
        public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId)
        {
            Assert.Equal(ParticipantDeviceId, peerDeviceId);
            return grant;
        }
    }

    private sealed class GrantingAuthorizationSource(CapabilityGrant grant) :
        IMirrorAuthorizationSource
    {
        public CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId) => grant;
    }

    private sealed class ConfirmingCaptureBoundary : IRemoteWindowCaptureBoundary
    {
        public ValueTask<LocalBoundaryResult> StartAsync(
            ActivityId activityId,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("capture_started"));

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("capture_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("capture_resumed");

        public LocalBoundaryResult EmergencyStopNow() =>
            LocalBoundaryResult.Confirmed("capture_emergency_stopped");

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("capture_stopped");
    }

    private sealed class ConfirmingInputBoundary : IRemoteInputBoundary
    {
        public ValueTask<LocalBoundaryResult> InjectAsync(
            RemoteInputBatch batch,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("input_injected"));

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("input_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("input_resumed");

        public LocalBoundaryResult EmergencyStopNow() =>
            LocalBoundaryResult.Confirmed("input_emergency_stopped");

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("input_stopped");
    }

    private sealed class BlockingInputBoundary : IRemoteInputBoundary
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<LocalBoundaryResult> InjectAsync(
            RemoteInputBatch batch,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return LocalBoundaryResult.Confirmed("input_injected");
        }

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("input_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("input_resumed");

        public LocalBoundaryResult EmergencyStopNow() =>
            LocalBoundaryResult.Confirmed("input_emergency_stopped");

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("input_stopped");
    }

    private sealed class CallbackInputBoundary(Action callback) :
        IRemoteInputBoundary
    {
        public TaskCompletionSource CallbackReturned { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<LocalBoundaryResult> InjectAsync(
            RemoteInputBatch batch,
            CancellationToken cancellationToken)
        {
            callback();
            CallbackReturned.TrySetResult();
            return ValueTask.FromResult(
                LocalBoundaryResult.Confirmed("input_injected"));
        }

        public LocalBoundaryResult PauseNow(MirrorPauseReason reason) =>
            LocalBoundaryResult.Confirmed("input_paused");

        public LocalBoundaryResult ResumeNow() =>
            LocalBoundaryResult.Confirmed("input_resumed");

        public LocalBoundaryResult EmergencyStopNow() =>
            LocalBoundaryResult.Confirmed("input_emergency_stopped");

        public LocalBoundaryResult StopNow() =>
            LocalBoundaryResult.Confirmed("input_stopped");
    }

    private sealed class ConfirmingSessionBoundary : ILocalSharingSessionBoundary
    {
        public LocalBoundaryResult DisconnectPeerNow(DeviceId peerDeviceId) =>
            LocalBoundaryResult.Confirmed("peer_disconnected");

        public LocalBoundaryResult DisconnectAllNow() =>
            LocalBoundaryResult.Confirmed("sessions_disconnected");
    }

    private sealed class RecordingSessionBoundary : ILocalSharingSessionBoundary
    {
        public List<DeviceId> DisconnectedPeers { get; } = [];

        public LocalBoundaryResult DisconnectPeerNow(DeviceId peerDeviceId)
        {
            DisconnectedPeers.Add(peerDeviceId);
            return LocalBoundaryResult.Confirmed("peer_disconnected");
        }

        public LocalBoundaryResult DisconnectAllNow() =>
            LocalBoundaryResult.Confirmed("sessions_disconnected");
    }

    private sealed class BlockingSessionBoundary :
        ILocalSharingSessionBoundary,
        IDisposable
    {
        private readonly ManualResetEventSlim release = new();

        public List<DeviceId> DisconnectedPeers { get; } = [];

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public LocalBoundaryResult DisconnectPeerNow(DeviceId peerDeviceId)
        {
            DisconnectedPeers.Add(peerDeviceId);
            Entered.TrySetResult();
            release.Wait();
            return LocalBoundaryResult.Confirmed("peer_disconnected");
        }

        public LocalBoundaryResult DisconnectAllNow() =>
            LocalBoundaryResult.Confirmed("sessions_disconnected");

        public void Dispose() => release.Dispose();

        public void Release() => release.Set();
    }
}
