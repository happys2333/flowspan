using System.Collections.Concurrent;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Protocol;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class RemoteWindowControlSessionConcurrencyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId ParticipantId = DeviceId.Parse(
        "11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId HostId = DeviceId.Parse(
        "22222222-2222-2222-2222-222222222222");

    private static readonly ActivityId ActivityId = ActivityId.Parse(
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task StopWaitsForStartedSendAndRejectsLaterCommands()
    {
        var connection = new BlockingSendConnection(ParticipantId, HostId);
        await using var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        Task<RemoteWindowControlDeliveryResult> sending = session.AdmitAsync(
            CreateAdmission("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            CancellationToken.None).AsTask();
        await connection.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task stopping = session.StopDispatchAsync().AsTask();
        Assert.False(stopping.IsCompleted);
        RemoteWindowControlDeliveryResult later = await session.AdmitAsync(
            CreateAdmission("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            CancellationToken.None);

        Assert.Equal(RemoteWindowControlDeliveryStatus.NotDelivered, later.Status);
        Assert.Equal(1, connection.SendCount);
        connection.ReleaseSend.TrySetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
    }

    [Fact]
    public async Task DisposeWaitsForStartedSend()
    {
        var connection = new BlockingSendConnection(ParticipantId, HostId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        Task<RemoteWindowControlDeliveryResult> sending = session.AdmitAsync(
            CreateAdmission("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            CancellationToken.None).AsTask();
        await connection.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task disposing = session.DisposeAsync().AsTask();

        Assert.False(disposing.IsCompleted);
        connection.ReleaseSend.TrySetResult();
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task StopNotifiesPeerDisconnectOnlyOnce()
    {
        var connection = new RecordingConnection(ParticipantId, HostId);
        var peer = new CountingDisconnectPeer(
            SessionId,
            ActivityId,
            ParticipantId);
        await using var session = new RemoteWindowControlSession(
            connection,
            peer,
            new FixedTimeProvider(Now));
        session.StartDispatch();

        await session.StopDispatchAsync();
        await session.StopDispatchAsync();

        Assert.Equal(1, peer.DisconnectCount);
    }

    [Fact]
    public async Task DisposalIsReentrantFromLifetimeCancellationCallback()
    {
        var connection = new RecordingConnection(ParticipantId, HostId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        bool nestedDisposeCompleted = false;
        using CancellationTokenRegistration registration =
            session.RegisterLifetimeCancellationCallback(() =>
                nestedDisposeCompleted = Task.Run(async () =>
                {
                    await session.DisposeAsync();
                    return true;
                }).GetAwaiter().GetResult());

        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(nestedDisposeCompleted);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task DisposalWaitsForCancellationCallbackAndCtsDisposal()
    {
        var connection = new RecordingConnection(ParticipantId, HostId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        var callbackStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration =
            session.RegisterLifetimeCancellationCallback(() =>
            {
                callbackStarted.TrySetResult();
                releaseCallback.Task.GetAwaiter().GetResult();
            });
        Task cancelling = Task.Run(session.Cancel);
        await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task disposing = session.DisposeAsync().AsTask();

        Assert.False(disposing.IsCompleted);
        releaseCallback.TrySetResult();
        await cancelling.WaitAsync(TimeSpan.FromSeconds(5));
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<ObjectDisposedException>(() =>
            _ = session.LifetimeCancellationToken);
    }

    [Fact]
    public async Task DisposalIsReentrantFromActiveSendDelegate()
    {
        var connection = new CallbackSendConnection(ParticipantId, HostId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        bool nestedDisposeCompleted = false;
        connection.Callback = (_, _) =>
        {
            nestedDisposeCompleted = session.DisposeAsync().AsTask()
                .IsCompletedSuccessfully;
            return ValueTask.CompletedTask;
        };
        session.StartDispatch();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.AdmitAsync(
                CreateAdmission("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                CancellationToken.None).AsTask());
        await session.DisposeAsync();

        Assert.True(nestedDisposeCompleted);
    }

    [Fact]
    public async Task StopIsReentrantFromPeerDisconnectCallback()
    {
        var connection = new RecordingConnection(ParticipantId, HostId);
        var peer = new ReentrantStopPeer(SessionId, ActivityId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            peer,
            new FixedTimeProvider(Now));
        peer.Session = session;
        session.StartDispatch();

        await session.StopDispatchAsync().AsTask().WaitAsync(
            TimeSpan.FromSeconds(5));
        await session.DisposeAsync();

        Assert.True(peer.NestedStopCompleted);
    }

    [Fact]
    public async Task CopiedCancellationContextJoinsDisposalAfterCallbackReturns()
    {
        var connection = new BlockingSendConnection(ParticipantId, HostId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        Task<RemoteWindowControlDeliveryResult> sending = session.AdmitAsync(
            CreateAdmission("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            CancellationToken.None).AsTask();
        await connection.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var copiedContextReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCopiedContext = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var nestedDisposalCompleted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var nestedDisposalReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration =
            session.RegisterLifetimeCancellationCallback(() =>
                _ = DisposeFromCopiedContextAsync());

        Task disposing = session.DisposeAsync().AsTask();
        await copiedContextReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseCopiedContext.TrySetResult();

        Assert.False(await nestedDisposalCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5)));
        Assert.False(nestedDisposalReturned.Task.IsCompleted);
        connection.ReleaseSend.TrySetResult();
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        await nestedDisposalReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);

        async Task DisposeFromCopiedContextAsync()
        {
            copiedContextReady.TrySetResult();
            await releaseCopiedContext.Task;
            Task nestedDisposal = session.DisposeAsync().AsTask();
            nestedDisposalCompleted.TrySetResult(
                nestedDisposal.IsCompletedSuccessfully);
            await nestedDisposal;
            nestedDisposalReturned.TrySetResult();
        }
    }

    [Fact]
    public async Task ConcurrentCommandsNeverExceedPendingCapacity()
    {
        const int commandCount = 64;
        using var startBarrier = new Barrier(commandCount + 1);
        var connection = new BarrierSendConnection(
            ParticipantId,
            HostId,
            startBarrier);
        await using var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        Task<RemoteWindowControlDeliveryResult>[] commands = Enumerable.Range(
                0,
                commandCount)
            .Select(index => Task.Factory.StartNew(
                async () => await session.AdmitAsync(
                    CreateAdmission(CreateCorrelationId(index)),
                    CancellationToken.None),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap())
            .ToArray();

        startBarrier.SignalAndWait(TimeSpan.FromSeconds(5));
        await connection.CapacityReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.InRange(
            connection.SendCount,
            1,
            RemoteWindowControlSession.MaximumPendingCommands);
        session.Cancel();
        connection.ReleaseSends.TrySetResult();
        foreach (Task<RemoteWindowControlDeliveryResult> command in commands)
        {
            try
            {
                _ = await command.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [Fact]
    public async Task ThrowingStateObserverDoesNotCloseDispatchOrBlockLaterObserver()
    {
        var connection = new RecordingConnection(ParticipantId, HostId);
        await using var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        RemoteWindowAdmissionRequest admission =
            CreateAdmission("cccccccc-cccc-cccc-cccc-cccccccccccc");
        Task<RemoteWindowControlDeliveryResult> admitting = session.AdmitAsync(
            admission,
            CancellationToken.None).AsTask();
        ControlMessage request = await connection.Sent.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
        RemoteWindowParticipantState acknowledged = CreateState(
            admission.CorrelationId,
            RemoteWindowControlAction.Admission,
            revision: 1);
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateState(
                ProtocolFeatures.RemoteWindowMinimumVersion,
                HostId,
                acknowledged,
                Now),
            CancellationToken.None);
        Assert.Equal(
            RemoteWindowControlDeliveryStatus.Acknowledged,
            (await admitting).Status);
        var observed = new TaskCompletionSource<RemoteWindowParticipantState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += _ => throw new InvalidOperationException(
            "observer failure");
        session.StateChanged += state => observed.TrySetResult(state);
        RemoteWindowParticipantState published = CreateState(
            CorrelationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            RemoteWindowControlAction.StateChanged,
            revision: 2);

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateState(
                ProtocolFeatures.RemoteWindowMinimumVersion,
                HostId,
                published,
                Now),
            CancellationToken.None);

        Assert.Equal(
            published,
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(ControlMessageType.RemoteWindowAdmission, request.Type);
    }

    private static RemoteWindowAdmissionRequest CreateAdmission(
        string correlationId) => CreateAdmission(CorrelationId.Parse(correlationId));

    private static RemoteWindowAdmissionRequest CreateAdmission(
        CorrelationId correlationId) => RemoteWindowAdmissionRequest.Create(
            correlationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(10));

    private static RemoteWindowParticipantState CreateState(
        CorrelationId correlationId,
        RemoteWindowControlAction action,
        long revision) => RemoteWindowParticipantState.Create(
            correlationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            action,
            RemoteWindowControlOutcome.Applied,
            action == RemoteWindowControlAction.Admission
                ? "participant_admitted"
                : "state_changed",
            RemoteWindowLifecycle.Active,
            RemoteWindowCaptureState.Capturing,
            participantCount: 2,
            MirrorParticipantRole.ViewOnly,
            HostId,
            driverLeaseEpoch: 1,
            Now.AddMinutes(1),
            ProtectionKind.Safe,
            revision);

    private static CorrelationId CreateCorrelationId(int index) =>
        CorrelationId.Parse($"{index + 1:x8}-0000-0000-0000-000000000001");

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class BlockingSendConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) : IRemoteWindowControlConnection
    {
        private int sendCount;

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } =
            ProtocolFeatures.RemoteWindowMinimumVersion;

        public TaskCompletionSource ReleaseSend { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SendCount => Volatile.Read(ref sendCount);

        public TaskCompletionSource SendStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ControlMessage>(new InvalidOperationException());

        public async ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref sendCount);
            SendStarted.TrySetResult();
            await ReleaseSend.Task;
        }
    }

    private sealed class BarrierSendConnection : IRemoteWindowControlConnection
    {
        private readonly DeviceId localDeviceId;
        private readonly ConcurrentDictionary<int, byte> synchronizedThreads = new();
        private readonly Barrier startBarrier;
        private int sendCount;

        public BarrierSendConnection(
            DeviceId localDeviceId,
            DeviceId peerDeviceId,
            Barrier startBarrier)
        {
            this.localDeviceId = localDeviceId;
            PeerDeviceId = peerDeviceId;
            this.startBarrier = startBarrier;
        }

        public TaskCompletionSource CapacityReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DeviceId LocalDeviceId
        {
            get
            {
                if (synchronizedThreads.TryAdd(
                    Environment.CurrentManagedThreadId,
                    0))
                {
                    startBarrier.SignalAndWait(TimeSpan.FromSeconds(5));
                }

                return localDeviceId;
            }
        }

        public DeviceId PeerDeviceId { get; }

        public ProtocolVersion ProtocolVersion { get; } =
            ProtocolFeatures.RemoteWindowMinimumVersion;

        public TaskCompletionSource ReleaseSends { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SendCount => Volatile.Read(ref sendCount);

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ControlMessage>(new InvalidOperationException());

        public async ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default)
        {
            int count = Interlocked.Increment(ref sendCount);
            if (count >= RemoteWindowControlSession.MaximumPendingCommands)
            {
                CapacityReached.TrySetResult();
            }

            await ReleaseSends.Task;
        }
    }

    private sealed class CallbackSendConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) : IRemoteWindowControlConnection
    {
        public Func<ControlMessage, CancellationToken, ValueTask>? Callback { get; set; }

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } =
            ProtocolFeatures.RemoteWindowMinimumVersion;

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ControlMessage>(new InvalidOperationException());

        public ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default) =>
            (Callback ?? throw new InvalidOperationException(
                "The send callback was not configured."))(
                message,
                cancellationToken);
    }

    private sealed class RecordingConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) : IRemoteWindowControlConnection
    {
        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } =
            ProtocolFeatures.RemoteWindowMinimumVersion;

        public TaskCompletionSource<ControlMessage> Sent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ControlMessage>(new InvalidOperationException());

        public ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default)
        {
            Sent.TrySetResult(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReentrantStopPeer(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId) : IRemoteWindowControlPeer
    {
        public ActivityId ActivityId { get; } = activityId;

        public DeviceId HostDeviceId { get; } = hostDeviceId;

        public bool NestedStopCompleted { get; private set; }

        public RemoteWindowControlSession? Session { get; set; }

        public RemoteWindowSessionId SessionId { get; } = sessionId;

        public ValueTask<RemoteWindowParticipantState> AdmitAsync(
            RemoteWindowAdmissionRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> RequestDriverAsync(
            RemoteWindowDriverRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> SendInputAsync(
            RemoteWindowInputRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> DisconnectAsync(
            RemoteWindowDisconnectRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask PeerDisconnectedAsync(
            DeviceId peerDeviceId,
            CancellationToken cancellationToken)
        {
            NestedStopCompleted = (Session ?? throw new InvalidOperationException(
                "The session was not configured.")).StopDispatchAsync().AsTask()
                .IsCompletedSuccessfully;
            return ValueTask.CompletedTask;
        }

        private static ValueTask<RemoteWindowParticipantState> NeverCalled() =>
            ValueTask.FromException<RemoteWindowParticipantState>(
                new InvalidOperationException("No Remote Window command was expected."));
    }

    private sealed class CountingDisconnectPeer(
        RemoteWindowSessionId sessionId,
        ActivityId activityId,
        DeviceId hostDeviceId) : IRemoteWindowControlPeer
    {
        private int disconnectCount;

        public ActivityId ActivityId { get; } = activityId;

        public int DisconnectCount => Volatile.Read(ref disconnectCount);

        public DeviceId HostDeviceId { get; } = hostDeviceId;

        public RemoteWindowSessionId SessionId { get; } = sessionId;

        public ValueTask<RemoteWindowParticipantState> AdmitAsync(
            RemoteWindowAdmissionRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> RequestDriverAsync(
            RemoteWindowDriverRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> SendInputAsync(
            RemoteWindowInputRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask<RemoteWindowParticipantState> DisconnectAsync(
            RemoteWindowDisconnectRequest request,
            CancellationToken cancellationToken) => NeverCalled();

        public ValueTask PeerDisconnectedAsync(
            DeviceId peerDeviceId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref disconnectCount);
            return ValueTask.CompletedTask;
        }

        private static ValueTask<RemoteWindowParticipantState> NeverCalled() =>
            ValueTask.FromException<RemoteWindowParticipantState>(
                new InvalidOperationException("No Remote Window command was expected."));
    }
}
