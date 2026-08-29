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
    public async Task DisposeNotifiesControlPeerDisconnectExactlyOnce()
    {
        var connection = new RecordingConnection(ParticipantId, HostId);
        var peer = new CountingDisconnectPeer(
            SessionId,
            ActivityId,
            ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            peer,
            new FixedTimeProvider(Now));
        session.StartDispatch();

        await session.DisposeAsync();
        await session.DisposeAsync();

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

    [Fact]
    public async Task FinalAdmissionCompletesPreparedParticipantBeforePublishingBinding()
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new RecordingPreparationPeer(ParticipantId);
        await using var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        session.StartDispatch();
        RemoteWindowPreparationRequest request =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        var observed = new TaskCompletionSource<RemoteWindowParticipantState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += state => observed.TrySetResult(state);

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                request,
                Now),
            CancellationToken.None);
        ControlMessage ready = await connection.Sent.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
        Assert.Equal(ControlMessageType.RemoteWindowReady, ready.Type);
        Assert.Equal(0, peer.CompletedAdmissionCount);

        RemoteWindowParticipantState admission = CreateState(
            request.CorrelationId,
            RemoteWindowControlAction.Admission,
            revision: 1);
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateState(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                admission,
                Now),
            CancellationToken.None);

        await peer.AdmissionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, peer.CompletedAdmissionCount);
        Assert.Equal(request, peer.CompletedRequest);
        Assert.Equal(admission, peer.CompletedState);
        Assert.Equal(admission, await observed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await session.StopDispatchAsync();
    }

    [Theory]
    [InlineData(RemoteWindowPreparationOutcome.Ready)]
    [InlineData(RemoteWindowPreparationOutcome.Rejected)]
    public async Task PreparationResponseCompletionRunsAfterWireSendCommits(
        RemoteWindowPreparationOutcome outcome)
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new ResponseCompletionPreparationPeer(ParticipantId, outcome);
        await using var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        session.StartDispatch();
        var sendStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int sendReturned = 0;
        connection.SendCallback = async (_, cancellationToken) =>
        {
            sendStarted.TrySetResult();
            await releaseSend.Task.WaitAsync(cancellationToken);
            Volatile.Write(ref sendReturned, 1);
        };
        peer.ResponseCompletionCallback = (_, responseCommitted) =>
        {
            peer.SendReturnedAtCompletion = Volatile.Read(ref sendReturned) != 0;
            peer.ResponseCommittedAtCompletion = responseCommitted;
            return ValueTask.CompletedTask;
        };

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                CreatePreparation(),
                Now),
            CancellationToken.None);
        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(peer.ResponseCompletionCalled.Task.IsCompleted);
        releaseSend.TrySetResult();
        await peer.ResponseCompletionCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(peer.SendReturnedAtCompletion);
        Assert.True(peer.ResponseCommittedAtCompletion);
        Assert.Equal(1, peer.ResponseCompletionCount);
        session.Cancel();
        await session.StopDispatchAsync();
    }

    [Fact]
    public async Task CommittedPreparationRejectionWaitsForPeerToCloseConnection()
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new ResponseCompletionPreparationPeer(
            ParticipantId,
            RemoteWindowPreparationOutcome.Rejected);
        await using var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        session.StartDispatch();

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                CreatePreparation(),
                Now),
            CancellationToken.None);
        await peer.ResponseCompletionCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(peer.ResponseCommittedAtCompletion);
        Assert.False(session.LifetimeCancellationToken.IsCancellationRequested);
        session.Cancel();
        await session.StopDispatchAsync();
    }

    [Fact]
    public async Task CommittedPreparationRejectionCancelsAtOriginalDeadline()
    {
        var time = new ManualTimeProvider(Now);
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new ResponseCompletionPreparationPeer(
            ParticipantId,
            RemoteWindowPreparationOutcome.Rejected);
        await using var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time,
            preparationPeer: peer);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration cancellationRegistration =
            session.LifetimeCancellationToken.Register(
                () => cancellationObserved.TrySetResult());
        session.StartDispatch();

        try
        {
            await session.DispatchAsync(
                RemoteWindowControlMessageCodec.CreatePrepare(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    HostId,
                    CreatePreparation(deadline: Now.AddSeconds(1)),
                    Now),
                CancellationToken.None);
            await peer.ResponseCompletionCalled.Task.WaitAsync(
                TimeSpan.FromSeconds(5));

            Assert.True(peer.ResponseCommittedAtCompletion);
            Assert.False(session.LifetimeCancellationToken.IsCancellationRequested);
            time.Advance(TimeSpan.FromSeconds(1));
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(session.LifetimeCancellationToken.IsCancellationRequested);
        }
        finally
        {
            session.Cancel();
            await session.StopDispatchAsync().AsTask().WaitAsync(
                TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task PreparationResponseCompletionRunsWhenSendIsNotAdmitted()
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new ResponseCompletionPreparationPeer(
            ParticipantId,
            RemoteWindowPreparationOutcome.Ready);
        await using var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        peer.PrepareCallback = session.Cancel;
        session.StartDispatch();

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                CreatePreparation(),
                Now),
            CancellationToken.None);
        await peer.ResponseCompletionCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(peer.ResponseCommittedAtCompletion);
        Assert.Equal(1, peer.ResponseCompletionCount);
        Assert.Equal(0, connection.SendCount);
        await session.StopDispatchAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PreparationSendAndResponseCompletionFailuresAreBothObserved()
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new ResponseCompletionPreparationPeer(
            ParticipantId,
            RemoteWindowPreparationOutcome.Ready);
        var sendFailure = new IOException("CANARY_RESPONSE_SEND_FAILURE");
        var completionFailure = new InvalidOperationException(
            "CANARY_RESPONSE_COMPLETION_FAILURE");
        connection.SendCallback = (_, _) => ValueTask.FromException(sendFailure);
        peer.ResponseCompletionCallback = (_, responseCommitted) =>
        {
            peer.ResponseCommittedAtCompletion = responseCommitted;
            return ValueTask.FromException(completionFailure);
        };
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        session.StartDispatch();

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                CreatePreparation(),
                Now),
            CancellationToken.None);
        await peer.ResponseCompletionCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(
            () => session.StopDispatchAsync().AsTask());
        Assert.False(peer.ResponseCommittedAtCompletion);
        Assert.Equal(1, peer.ResponseCompletionCount);
        Assert.Contains(sendFailure, failure.Flatten().InnerExceptions);
        Assert.Contains(completionFailure, failure.Flatten().InnerExceptions);
        await Assert.ThrowsAsync<AggregateException>(
            () => session.DisposeAsync().AsTask());
    }

    [Theory]
    [InlineData(RemoteWindowPreparationOutcome.Ready)]
    [InlineData(RemoteWindowPreparationOutcome.Rejected)]
    public async Task PreparationResponseCompletionCanStopOwningSessionWithoutSelfWaiting(
        RemoteWindowPreparationOutcome outcome)
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new ResponseCompletionPreparationPeer(
            ParticipantId,
            outcome);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        var stopReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        peer.ResponseCompletionCallback = async (_, _) =>
        {
            await session.StopDispatchAsync();
            stopReturned.TrySetResult();
        };
        session.StartDispatch();

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                CreatePreparation(),
                Now),
            CancellationToken.None);

        await stopReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopDispatchAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, peer.ResponseCompletionCount);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task PreparationDeadlineCancelsBlockedParticipantBoundary()
    {
        var time = new ManualTimeProvider(Now);
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new DeadlineBlockingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time,
            preparationPeer: peer);
        session.StartDispatch();
        RemoteWindowPreparationRequest request =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(1));

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                request,
                Now),
            CancellationToken.None);
        await peer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await peer.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            peer.Release.TrySetResult();
            try
            {
                await session.StopDispatchAsync();
            }
            catch (InvalidDataException)
            {
                // The pre-fix worker observes expiry only after the test releases it.
            }

            try
            {
                await session.DisposeAsync();
            }
            catch (InvalidDataException)
            {
                // The pre-fix worker failure remains observable during disposal.
            }
        }
    }

    [Fact]
    public async Task StopCancelsAndNotifiesPreparationPeerBeforeJoiningWorker()
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new DisconnectReleasedPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        session.StartDispatch();
        RemoteWindowPreparationRequest request =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                request,
                Now),
            CancellationToken.None);
        await peer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task stopping = session.StopDispatchAsync().AsTask();
        try
        {
            Assert.Equal(1, peer.DisconnectCount);
            Assert.True(peer.PrepareCancellationRequestedAtDisconnect);
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            peer.Release.TrySetResult();
            try
            {
                await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }

            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task StopCancelsSynchronouslyBlockingPreparationWithoutGateDeadlock()
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new SynchronouslyBlockingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        session.StartDispatch();
        RemoteWindowPreparationRequest request =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                request,
                Now),
            CancellationToken.None);
        await peer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task stopping = Task.Run(async () => await session.StopDispatchAsync());
        try
        {
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(peer.CancellationWon);
            Assert.Equal(0, connection.SendCount);
        }
        finally
        {
            peer.Release.Set();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            await session.DisposeAsync();
            peer.Dispose();
        }
    }

    [Fact]
    public async Task StopCompletesWhenPreparationCleanupWaitsForWorkerExit()
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new WorkerExitAwaitingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        session.StartDispatch();
        RemoteWindowPreparationRequest request =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                request,
                Now),
            CancellationToken.None);
        await peer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task stopping = session.StopDispatchAsync().AsTask();

        await peer.CleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await peer.WorkerExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await peer.CleanupCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task PreparationCrossingDeadlineByOneMillisecondCannotStartParticipantBoundary()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new RecordingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time,
            preparationPeer: peer);
        session.StartDispatch();
        RemoteWindowPreparationRequest request =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddMilliseconds(1));
        time.ScheduleCallback(
            2,
            () => time.Advance(TimeSpan.FromMilliseconds(2)));

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => session.DispatchAsync(
                RemoteWindowControlMessageCodec.CreatePrepare(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    HostId,
                    request,
                    Now),
                CancellationToken.None).AsTask());
            Assert.Equal(0, peer.PrepareCount);
        }
        finally
        {
            session.Cancel();
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task InboundPreparationCannotBeReservedAfterStopWinsRace()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new RecordingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time,
            preparationPeer: peer);
        session.StartDispatch();
        RemoteWindowPreparationRequest request =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        Task? stopping = null;
        time.ScheduleCallback(
            2,
            () => stopping = session.StopDispatchAsync().AsTask());

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                session.DispatchAsync(
                    RemoteWindowControlMessageCodec.CreatePrepare(
                        ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                        HostId,
                        request,
                        Now),
                    CancellationToken.None).AsTask());
            await Assert.IsType<Task>(stopping).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, peer.PrepareCount);
            Assert.Equal(0, connection.SendCount);
        }
        finally
        {
            session.Cancel();
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task OutboundPreparationCannotBeReservedAfterStopWinsRace()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time);
        session.StartDispatch();
        RemoteWindowPreparationRequest request =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        Task? stopping = null;
        time.ScheduleCallback(
            2,
            () => stopping = session.StopDispatchAsync().AsTask());

        RemoteWindowPreparationDeliveryResult result = await session.PrepareAsync(
            request,
            CancellationToken.None);
        await Assert.IsType<Task>(stopping).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RemoteWindowControlDeliveryStatus.NotDelivered, result.Status);
        Assert.Equal(0, connection.SendCount);
        Assert.Equal(2, time.ReadCount);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task PreparationCallbackCanStopItsOwningSessionWithoutSelfWaiting()
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new ReentrantStopPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        peer.Session = session;
        session.StartDispatch();
        RemoteWindowPreparationRequest request =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                request,
                Now),
            CancellationToken.None);

        await peer.StopReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopDispatchAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task InboundPreparationRejectsCorrelationOwnedByOrdinaryCommand()
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new RecordingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        Task<RemoteWindowControlDeliveryResult> admitting = session.AdmitAsync(
            CreateAdmission(correlationId),
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));

        await Assert.ThrowsAsync<InvalidDataException>(() => session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                preparation,
                Now),
            CancellationToken.None).AsTask());

        session.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => admitting);
        await session.StopDispatchAsync();
        await session.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalInboundPreparationRejectsDuplicateOrConflictingPrepare(
        bool conflicting)
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new RecordingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        session.StartDispatch();
        RemoteWindowPreparationRequest first =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                first,
                Now),
            CancellationToken.None);
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateState(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                CreateState(
                    first.CorrelationId,
                    RemoteWindowControlAction.Admission,
                    revision: 1),
                Now),
            CancellationToken.None);
        await peer.AdmissionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        RemoteWindowPreparationRequest second = conflicting
            ? RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.DriverEligible,
                Now.AddSeconds(10))
            : first;

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => session.DispatchAsync(
                RemoteWindowControlMessageCodec.CreatePrepare(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    HostId,
                    second,
                    Now),
                CancellationToken.None).AsTask());

            Assert.Equal(1, peer.PrepareCount);
            Assert.Equal(1, peer.CompletedAdmissionCount);
            Assert.Equal(1, connection.SendCount);
        }
        finally
        {
            session.Cancel();
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentOutboundPreparationsReserveOnlyOneTransaction()
    {
        var connection = new PreparationConnection(HostId, ParticipantId);
        var releaseSend = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.SendCallback = async (_, cancellationToken) =>
            await releaseSend.Task.WaitAsync(cancellationToken);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        var contendersReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var startContenders = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int readyCount = 0;

        Task<RemoteWindowPreparationDeliveryResult> RunContenderAsync(
            RemoteWindowPreparationRequest request) => Task.Run(async () =>
        {
            if (Interlocked.Increment(ref readyCount) == 2)
            {
                contendersReady.TrySetResult();
            }

            await startContenders.Task;
            return await session.PrepareAsync(request, CancellationToken.None);
        });

        Task<RemoteWindowPreparationDeliveryResult> first = RunContenderAsync(
            CreatePreparation());
        Task<RemoteWindowPreparationDeliveryResult> second = RunContenderAsync(
            CreatePreparation("dddddddd-dddd-dddd-dddd-dddddddddddd"));

        try
        {
            await contendersReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
            startContenders.TrySetResult();
            Task<RemoteWindowPreparationDeliveryResult> rejected =
                await Task.WhenAny(first, second).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                rejected.WaitAsync(TimeSpan.FromSeconds(5)));
            Task<RemoteWindowPreparationDeliveryResult> reserved =
                ReferenceEquals(rejected, first) ? second : first;

            Assert.False(reserved.IsCompleted);
            Assert.Equal(1, connection.SendCount);
            releaseSend.TrySetResult();
            await connection.CallbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            session.Cancel();
            RemoteWindowPreparationDeliveryResult result = await reserved.WaitAsync(
                TimeSpan.FromSeconds(5));
            Assert.NotEqual(
                RemoteWindowControlDeliveryStatus.Acknowledged,
                result.Status);
        }
        finally
        {
            startContenders.TrySetResult();
            releaseSend.TrySetResult();
            session.Cancel();
            _ = await Record.ExceptionAsync(async () =>
                await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5)));
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task OrdinaryCommandRejectsCorrelationOwnedByInboundPreparation()
    {
        var connection = new PreparationConnection(ParticipantId, HostId)
        {
            FailSubsequentSends = true,
        };
        var peer = new RecordingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                preparation,
                Now),
            CancellationToken.None);
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.AdmitAsync(
            CreateAdmission(correlationId),
            CancellationToken.None).AsTask());
        Assert.Equal(1, connection.SendCount);

        session.Cancel();
        await session.StopDispatchAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task OutboundPreparationRejectsCorrelationOwnedByOrdinaryCommand()
    {
        var connection = new PreparationConnection(ParticipantId, HostId)
        {
            FailSubsequentSends = true,
        };
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        Task<RemoteWindowControlDeliveryResult> admitting = session.AdmitAsync(
            CreateAdmission(correlationId),
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                ParticipantId,
                HostId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.PrepareAsync(
            preparation,
            CancellationToken.None).AsTask());
        Assert.Equal(1, connection.SendCount);

        session.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => admitting);
        await session.StopDispatchAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task InboundOrdinaryCommandRejectsPreparationCorrelation()
    {
        var connection = new PreparationConnection(HostId, ParticipantId);
        var controlPeer = new CountingDisconnectPeer(SessionId, ActivityId, HostId);
        var session = new RemoteWindowControlSession(
            connection,
            controlPeer,
            new FixedTimeProvider(Now));
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        Task<RemoteWindowPreparationDeliveryResult> preparing = session.PrepareAsync(
            preparation,
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidDataException>(() => session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateAdmission(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                ParticipantId,
                CreateAdmission(correlationId),
                Now),
            CancellationToken.None).AsTask());

        session.Cancel();
        Assert.Equal(
            RemoteWindowControlDeliveryStatus.AcknowledgementLost,
            (await preparing).Status);
        await session.StopDispatchAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task AdmissionBeforeReadySendBeginsCannotCompleteParticipant()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new DeadlineBlockingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time,
            preparationPeer: peer);
        var stopped = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration =
            session.RegisterLifetimeCancellationCallback(() => stopped.TrySetResult());
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                preparation,
                Now),
            CancellationToken.None);
        await peer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Exception? earlyAdmissionFailure = null;
        ControlMessage admission = RemoteWindowControlMessageCodec.CreateState(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            HostId,
            CreateState(
                correlationId,
                RemoteWindowControlAction.Admission,
                revision: 1),
            Now);
        time.ScheduleCallback(2, () =>
        {
            try
            {
                session.DispatchAsync(admission, CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
            {
                earlyAdmissionFailure = exception;
                session.Cancel();
            }
        });

        peer.Release.TrySetResult();
        Task winner = await Task.WhenAny(peer.AdmissionCompleted.Task, stopped.Task)
            .WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            Assert.Same(stopped.Task, winner);
            Assert.IsType<InvalidDataException>(earlyAdmissionFailure);
            Assert.Equal(0, peer.CompletedAdmissionCount);
            Assert.Equal(0, connection.SendCount);
        }
        finally
        {
            session.Cancel();
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task AdmissionMayArriveAfterReadyIsExposedBeforeSendReturns()
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new RecordingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        RemoteWindowParticipantState admission = CreateState(
            correlationId,
            RemoteWindowControlAction.Admission,
            revision: 1);
        connection.SendCallback = (message, cancellationToken) =>
            message.Type is ControlMessageType.RemoteWindowReady
                ? session.DispatchAsync(
                    RemoteWindowControlMessageCodec.CreateState(
                        ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                        HostId,
                        admission,
                        Now),
                    cancellationToken)
                : ValueTask.CompletedTask;

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                preparation,
                Now),
            CancellationToken.None);
        Exception? callbackFailure = await connection.CallbackCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        try
        {
            Assert.Null(callbackFailure);
            await peer.AdmissionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, peer.CompletedAdmissionCount);
            Assert.Equal(admission, peer.CompletedState);
        }
        finally
        {
            session.Cancel();
            try
            {
                await session.StopDispatchAsync();
            }
            catch (InvalidDataException)
            {
            }

            try
            {
                await session.DisposeAsync();
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    [Fact]
    public async Task AdmissionBufferedDuringFailedReadySendIsNeverCompleted()
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new RecordingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        var stopped = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration =
            session.RegisterLifetimeCancellationCallback(() => stopped.TrySetResult());
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        RemoteWindowParticipantState admission = CreateState(
            correlationId,
            RemoteWindowControlAction.Admission,
            revision: 1);
        connection.SendCallback = async (_, cancellationToken) =>
        {
            await session.DispatchAsync(
                RemoteWindowControlMessageCodec.CreateState(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    HostId,
                    admission,
                    Now),
                cancellationToken);
            throw new IOException("The readiness frame failed after exposure.");
        };

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                preparation,
                Now),
            CancellationToken.None);
        Exception? sendFailure = await connection.CallbackCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            Assert.IsType<IOException>(sendFailure);
            Assert.Equal(0, peer.CompletedAdmissionCount);
            Assert.False(peer.AdmissionCompleted.Task.IsCompleted);
        }
        finally
        {
            await Assert.ThrowsAsync<IOException>(() =>
                session.StopDispatchAsync().AsTask());
            await Assert.ThrowsAsync<IOException>(() =>
                session.DisposeAsync().AsTask());
        }
    }

    [Fact]
    public async Task AdmissionCompletionCannotPublishWhenTimeCheckReentrantlyStopsSession()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new DeadlineCrossingCompletionPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time,
            preparationPeer: peer);
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        var observed = new TaskCompletionSource<RemoteWindowParticipantState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += state => observed.TrySetResult(state);
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                preparation,
                Now),
            CancellationToken.None);
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task finalizing = session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateState(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                CreateState(
                    correlationId,
                    RemoteWindowControlAction.Admission,
                    revision: 1),
                Now),
            CancellationToken.None).AsTask();
        await peer.CompletionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        time.ScheduleCallback(1, session.Cancel);
        peer.ReleaseCompletion.TrySetResult();

        await finalizing;
        await session.StopDispatchAsync();
        Assert.False(observed.Task.IsCompleted);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task AdmissionAfterReadyCannotCompleteWhenStopWinsFinalCommit()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new RecordingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time,
            preparationPeer: peer);
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                preparation,
                Now),
            CancellationToken.None);
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForTimeReadsToSettleAsync(time, minimumReadCount: 8);

        Task? stopping = null;
        time.ScheduleCallback(
            3,
            () => stopping = session.StopDispatchAsync().AsTask());

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => session.DispatchAsync(
                RemoteWindowControlMessageCodec.CreateState(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    HostId,
                    CreateState(
                        correlationId,
                        RemoteWindowControlAction.Admission,
                        revision: 1),
                    Now),
                CancellationToken.None).AsTask());
            await Assert.IsType<Task>(stopping).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, peer.CompletedAdmissionCount);
            Assert.False(peer.AdmissionCompleted.Task.IsCompleted);
        }
        finally
        {
            session.Cancel();
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task AdmissionCannotInvokeParticipantWhenStopWinsBoundaryStart()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new RecordingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time,
            preparationPeer: peer);
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                preparation,
                Now),
            CancellationToken.None);
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForTimeReadsToSettleAsync(time, minimumReadCount: 8);

        var stopStarted = new TaskCompletionSource<Task>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        time.ScheduleCallback(4, () =>
        {
            Task stopping = session.StopDispatchAsync().AsTask();
            stopStarted.TrySetResult(stopping);
        });

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateState(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                CreateState(
                    correlationId,
                    RemoteWindowControlAction.Admission,
                    revision: 1),
                Now),
            CancellationToken.None);
        Task stopping = await stopStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, peer.CompletedAdmissionCount);
        Assert.False(peer.AdmissionCompleted.Task.IsCompleted);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task FinalAdmissionObserverCanStopOwningSessionWithoutSelfWaiting()
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new RecordingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        var observerReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += _ =>
        {
            session.StopDispatchAsync().AsTask().GetAwaiter().GetResult();
            observerReturned.TrySetResult();
        };
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                preparation,
                Now),
            CancellationToken.None);
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateState(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                CreateState(
                    correlationId,
                    RemoteWindowControlAction.Admission,
                    revision: 1),
                Now),
            CancellationToken.None);

        await observerReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopDispatchAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, peer.CompletedAdmissionCount);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task UnsolicitedReadyIsRejectedWithoutPreparation()
    {
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation = CreatePreparation();

        await Assert.ThrowsAsync<InvalidDataException>(() => session.DispatchAsync(
            CreateReady(preparation),
            CancellationToken.None).AsTask());

        Assert.Equal(0, connection.SendCount);
        await session.StopDispatchAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task CrossRequestReadyCannotCompleteActivePreparation()
    {
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        RemoteWindowPreparationRequest active = CreatePreparation();
        RemoteWindowPreparationRequest other = CreatePreparation(
            "dddddddd-dddd-dddd-dddd-dddddddddddd");
        Task<RemoteWindowPreparationDeliveryResult> preparing = session.PrepareAsync(
            active,
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => session.DispatchAsync(
                CreateReady(other),
                CancellationToken.None).AsTask());
            Assert.False(preparing.IsCompleted);

            await session.DispatchAsync(
                CreateReady(active),
                CancellationToken.None);
            RemoteWindowPreparationDeliveryResult result = await preparing;

            Assert.Equal(
                RemoteWindowControlDeliveryStatus.Acknowledged,
                result.Status);
            Assert.Equal(active, Assert.IsType<RemoteWindowPreparationResponse>(
                result.Response).Request);
            Assert.Equal(1, connection.SendCount);
        }
        finally
        {
            session.Cancel();
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task TerminalOutboundPreparationRejectsDuplicateReady()
    {
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation = CreatePreparation();
        Task<RemoteWindowPreparationDeliveryResult> preparing = session.PrepareAsync(
            preparation,
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ControlMessage ready = CreateReady(preparation);
        await session.DispatchAsync(ready, CancellationToken.None);
        RemoteWindowPreparationDeliveryResult acknowledged = await preparing;

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => session.DispatchAsync(
                ready,
                CancellationToken.None).AsTask());

            Assert.Equal(
                RemoteWindowControlDeliveryStatus.Acknowledged,
                acknowledged.Status);
            Assert.Equal(
                preparation,
                Assert.IsType<RemoteWindowPreparationResponse>(
                    acknowledged.Response).Request);
            Assert.Equal(1, connection.SendCount);
        }
        finally
        {
            session.Cancel();
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalPreparationRejectsDelayedReadyReplay(
        bool expireDeadline)
    {
        var time = new ManualTimeProvider(Now);
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time);
        var stopped = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration =
            session.RegisterLifetimeCancellationCallback(() => stopped.TrySetResult());
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation = CreatePreparation(
            deadline: Now.AddSeconds(expireDeadline ? 1 : 10));
        Task<RemoteWindowPreparationDeliveryResult> preparing = session.PrepareAsync(
            preparation,
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            if (expireDeadline)
            {
                time.Advance(TimeSpan.FromSeconds(1));
            }
            else
            {
                session.Cancel();
            }

            RemoteWindowPreparationDeliveryResult terminal = await preparing.WaitAsync(
                TimeSpan.FromSeconds(5));
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(
                RemoteWindowControlDeliveryStatus.AcknowledgementLost,
                terminal.Status);
            Assert.Null(terminal.Response);
            if (!expireDeadline)
            {
                Assert.True(time.GetUtcNow() < preparation.Deadline);
            }

            InvalidDataException replayFailure =
                await Assert.ThrowsAsync<InvalidDataException>(() =>
                    session.DispatchAsync(
                        CreateReady(preparation),
                        CancellationToken.None).AsTask());
            Assert.Equal(
                "A delayed Remote Window readiness result was rejected by the terminal preparation tombstone.",
                replayFailure.Message);
            Assert.Equal(1, connection.SendCount);
        }
        finally
        {
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task OutboundFinalAdmissionCannotChangePreparedRole()
    {
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation = CreatePreparation();
        Task<RemoteWindowPreparationDeliveryResult> preparing = session.PrepareAsync(
            preparation,
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DispatchAsync(
            CreateReady(preparation),
            CancellationToken.None);
        Assert.Equal(
            RemoteWindowControlDeliveryStatus.Acknowledged,
            (await preparing).Status);
        RemoteWindowParticipantState changedRole = CreateState(
            preparation.CorrelationId,
            RemoteWindowControlAction.Admission,
            revision: 1,
            MirrorParticipantRole.DriverEligible);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                session.PublishAdmissionStateAsync(
                    changedRole,
                    CancellationToken.None).AsTask());
            Assert.Equal(1, connection.SendCount);

            await session.PublishAdmissionStateAsync(
                CreateState(
                    preparation.CorrelationId,
                    RemoteWindowControlAction.Admission,
                    revision: 1),
                CancellationToken.None);
            Assert.Equal(2, connection.SendCount);
        }
        finally
        {
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task InboundFinalAdmissionCannotChangePreparedRole()
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new RecordingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        var observed = new TaskCompletionSource<RemoteWindowParticipantState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += state => observed.TrySetResult(state);
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation = CreatePreparation();
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                preparation,
                Now),
            CancellationToken.None);
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        RemoteWindowParticipantState changedRole = CreateState(
            preparation.CorrelationId,
            RemoteWindowControlAction.Admission,
            revision: 1,
            MirrorParticipantRole.DriverEligible);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => session.DispatchAsync(
                RemoteWindowControlMessageCodec.CreateState(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    HostId,
                    changedRole,
                    Now),
                CancellationToken.None).AsTask());
            Assert.Equal(0, peer.CompletedAdmissionCount);
            Assert.False(observed.Task.IsCompleted);
            Assert.Equal(1, connection.SendCount);

            RemoteWindowParticipantState exact = CreateState(
                preparation.CorrelationId,
                RemoteWindowControlAction.Admission,
                revision: 1);
            await session.DispatchAsync(
                RemoteWindowControlMessageCodec.CreateState(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    HostId,
                    exact,
                    Now),
                CancellationToken.None);
            await peer.AdmissionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, peer.CompletedAdmissionCount);
            Assert.Equal(exact, peer.CompletedState);
            Assert.Equal(exact, await observed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            session.Cancel();
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task AdmissionCompletionReturningAfterDeadlineCannotPublishBinding()
    {
        var time = new ManualTimeProvider(Now);
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new DeadlineCrossingCompletionPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time,
            preparationPeer: peer);
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(1));
        var observed = new TaskCompletionSource<RemoteWindowParticipantState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += state => observed.TrySetResult(state);
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                preparation,
                Now),
            CancellationToken.None);
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task finalizing = session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateState(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                CreateState(
                    correlationId,
                    RemoteWindowControlAction.Admission,
                    revision: 1),
                Now),
            CancellationToken.None).AsTask();
        await peer.CompletionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        time.Advance(TimeSpan.FromSeconds(1));
        peer.ReleaseCompletion.TrySetResult();

        await finalizing;
        await session.StopDispatchAsync();
        Assert.False(observed.Task.IsCompleted);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ReadyBeforePrepareSendBeginsCannotAcknowledgePreparation()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time);
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        ControlMessage ready = RemoteWindowControlMessageCodec.CreateReady(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            ParticipantId,
            RemoteWindowPreparationResponse.Create(
                preparation,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready"),
            Now);
        Exception? earlyReadyFailure = null;
        time.ScheduleCallback(2, () =>
        {
            try
            {
                session.DispatchAsync(ready, CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
            {
                earlyReadyFailure = exception;
                session.Cancel();
            }
        });

        RemoteWindowPreparationDeliveryResult result = await session.PrepareAsync(
            preparation,
            CancellationToken.None);

        try
        {
            Assert.Equal(RemoteWindowControlDeliveryStatus.NotDelivered, result.Status);
            Assert.IsType<InvalidDataException>(earlyReadyFailure);
            Assert.Equal(0, connection.SendCount);
        }
        finally
        {
            session.Cancel();
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task PrepareDoesNotEnterWireAfterDeadlineAtSendAdmission()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time);
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        time.ScheduleCallback(
            4,
            () => time.Advance(TimeSpan.FromSeconds(10)));

        RemoteWindowPreparationDeliveryResult result = await session.PrepareAsync(
            preparation,
            CancellationToken.None);

        try
        {
            Assert.Equal(RemoteWindowControlDeliveryStatus.NotDelivered, result.Status);
            Assert.Null(result.Response);
            Assert.Equal(0, connection.SendCount);
        }
        finally
        {
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task PreparePropagatesCallerCancellationAtSendAdmission()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time);
        using var cancellation = new CancellationTokenSource();
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        time.ScheduleCallback(4, cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.PrepareAsync(preparation, cancellation.Token).AsTask());

        Assert.Equal(0, connection.SendCount);
        await session.StopDispatchAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ReadyDuringPrepareSendAcknowledgesOnlyAfterSendCompletes()
    {
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        var readyDispatched = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.SendCallback = async (_, cancellationToken) =>
        {
            await session.DispatchAsync(
                RemoteWindowControlMessageCodec.CreateReady(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    ParticipantId,
                    RemoteWindowPreparationResponse.Create(
                        preparation,
                        RemoteWindowPreparationOutcome.Ready,
                        "participant_ready"),
                    Now),
                cancellationToken);
            readyDispatched.TrySetResult();
            await releaseSend.Task.WaitAsync(cancellationToken);
        };
        Task<RemoteWindowPreparationDeliveryResult> preparing = session.PrepareAsync(
            preparation,
            CancellationToken.None).AsTask();
        await readyDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(preparing.IsCompleted);
        releaseSend.TrySetResult();
        RemoteWindowPreparationDeliveryResult result = await preparing;

        try
        {
            Assert.Equal(RemoteWindowControlDeliveryStatus.Acknowledged, result.Status);
            Assert.Equal(
                RemoteWindowPreparationOutcome.Ready,
                Assert.IsType<RemoteWindowPreparationResponse>(result.Response).Outcome);
        }
        finally
        {
            session.Cancel();
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(RemoteWindowPreparationOutcome.Ready, 2)]
    [InlineData(RemoteWindowPreparationOutcome.Rejected, 1)]
    public async Task CommittedPreparationResponseCannotBeReversedByLaterDeadlineRead(
        RemoteWindowPreparationOutcome outcome,
        int readsUntilDeadline)
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time);
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        connection.SendCallback = async (_, cancellationToken) =>
        {
            await session.DispatchAsync(
                RemoteWindowControlMessageCodec.CreateReady(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    ParticipantId,
                    RemoteWindowPreparationResponse.Create(
                        preparation,
                        outcome,
                        outcome is RemoteWindowPreparationOutcome.Ready
                            ? "participant_ready"
                            : "participant_busy"),
                    Now),
                cancellationToken);
            time.ScheduleCallback(
                readsUntilDeadline,
                () => time.Advance(TimeSpan.FromSeconds(10)));
        };

        RemoteWindowPreparationDeliveryResult result = await session.PrepareAsync(
            preparation,
            CancellationToken.None);

        try
        {
            Assert.Equal(RemoteWindowControlDeliveryStatus.Acknowledged, result.Status);
            Assert.Equal(
                outcome,
                Assert.IsType<RemoteWindowPreparationResponse>(result.Response).Outcome);
        }
        finally
        {
            session.Cancel();
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task RejectionDuringPrepareSendReturnsBeforeExplicitConnectionClose()
    {
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        connection.SendCallback = async (_, cancellationToken) =>
        {
            await session.DispatchAsync(
                RemoteWindowControlMessageCodec.CreateReady(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    ParticipantId,
                    RemoteWindowPreparationResponse.Create(
                        preparation,
                        RemoteWindowPreparationOutcome.Rejected,
                        "participant_busy"),
                    Now),
                CancellationToken.None);
        };

        try
        {
            RemoteWindowPreparationDeliveryResult result = await session.PrepareAsync(
                preparation,
                CancellationToken.None);

            Assert.Equal(RemoteWindowControlDeliveryStatus.Acknowledged, result.Status);
            RemoteWindowPreparationResponse response =
                Assert.IsType<RemoteWindowPreparationResponse>(result.Response);
            Assert.Equal(RemoteWindowPreparationOutcome.Rejected, response.Outcome);
            Assert.Equal("participant_busy", response.ReasonCode);
            Assert.False(session.LifetimeCancellationToken.IsCancellationRequested);
        }
        finally
        {
            session.Cancel();
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task CommittedOutboundRejectionNeverCancelsBeforeCallerObservesResponse()
    {
        const int attempts = 64;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            var connection = new PreparationConnection(HostId, ParticipantId);
            var session = new RemoteWindowControlSession(
                connection,
                timeProvider: new FixedTimeProvider(Now));
            session.StartDispatch();
            RemoteWindowPreparationRequest preparation =
                RemoteWindowPreparationRequest.Create(
                    CorrelationId.From(Guid.NewGuid()),
                    SessionId,
                    ActivityId,
                    HostId,
                    ParticipantId,
                    MirrorParticipantRole.ViewOnly,
                    Now.AddSeconds(10));
            Task<RemoteWindowPreparationDeliveryResult> preparing =
                session.PrepareAsync(preparation, CancellationToken.None).AsTask();
            await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

            try
            {
                await Task.Run(async () => await session.DispatchAsync(
                    RemoteWindowControlMessageCodec.CreateReady(
                        ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                        ParticipantId,
                        RemoteWindowPreparationResponse.Create(
                            preparation,
                            RemoteWindowPreparationOutcome.Rejected,
                            "participant_busy"),
                        Now),
                    CancellationToken.None));
                RemoteWindowPreparationDeliveryResult result =
                    await preparing.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.Equal(
                    RemoteWindowControlDeliveryStatus.Acknowledged,
                    result.Status);
                Assert.Equal(
                    RemoteWindowPreparationOutcome.Rejected,
                    Assert.IsType<RemoteWindowPreparationResponse>(result.Response)
                        .Outcome);
                Assert.False(session.LifetimeCancellationToken.IsCancellationRequested);
            }
            finally
            {
                session.Cancel();
                await session.StopDispatchAsync();
                await session.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task ReadyDuringFailedPrepareSendCannotAcknowledgePreparation()
    {
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        var stopped = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration =
            session.RegisterLifetimeCancellationCallback(() => stopped.TrySetResult());
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        connection.SendCallback = async (_, cancellationToken) =>
        {
            await session.DispatchAsync(
                RemoteWindowControlMessageCodec.CreateReady(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    ParticipantId,
                    RemoteWindowPreparationResponse.Create(
                        preparation,
                        RemoteWindowPreparationOutcome.Ready,
                        "participant_ready"),
                    Now),
                cancellationToken);
            throw new IOException("The preparation frame failed after exposure.");
        };

        RemoteWindowPreparationDeliveryResult result = await session.PrepareAsync(
            preparation,
            CancellationToken.None);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            Assert.Equal(RemoteWindowControlDeliveryStatus.NotDelivered, result.Status);
            Assert.Null(result.Response);
        }
        finally
        {
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task BufferedReadyCannotAuthorizeAdmissionBeforePrepareSendCommits()
    {
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        Exception? publicationFailure = null;
        connection.SendCallback = async (message, cancellationToken) =>
        {
            if (message.Type is not ControlMessageType.RemoteWindowPrepare)
            {
                return;
            }

            await session.DispatchAsync(
                RemoteWindowControlMessageCodec.CreateReady(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    ParticipantId,
                    RemoteWindowPreparationResponse.Create(
                        preparation,
                        RemoteWindowPreparationOutcome.Ready,
                        "participant_ready"),
                    Now),
                cancellationToken);
            publicationFailure = await Record.ExceptionAsync(() =>
                session.PublishAdmissionStateAsync(
                    CreateState(
                        correlationId,
                        RemoteWindowControlAction.Admission,
                        revision: 1),
                    cancellationToken).AsTask());
            throw new IOException("The preparation frame failed after exposure.");
        };

        RemoteWindowPreparationDeliveryResult result = await session.PrepareAsync(
            preparation,
            CancellationToken.None);

        try
        {
            Assert.Equal(RemoteWindowControlDeliveryStatus.NotDelivered, result.Status);
            Assert.Null(result.Response);
            Assert.IsType<InvalidOperationException>(publicationFailure);
            Assert.Equal(1, connection.SendCount);
        }
        finally
        {
            await session.StopDispatchAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReadyDuringPrepareSendCannotAcknowledgeAfterStopWinsCommit()
    {
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        connection.SendCallback = async (_, cancellationToken) =>
        {
            await session.DispatchAsync(
                RemoteWindowControlMessageCodec.CreateReady(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    ParticipantId,
                    RemoteWindowPreparationResponse.Create(
                        preparation,
                        RemoteWindowPreparationOutcome.Ready,
                        "participant_ready"),
                    Now),
                cancellationToken);
            await session.StopDispatchAsync();
        };

        RemoteWindowPreparationDeliveryResult result = await session.PrepareAsync(
            preparation,
            CancellationToken.None);

        Assert.NotEqual(RemoteWindowControlDeliveryStatus.Acknowledged, result.Status);
        Assert.Null(result.Response);
        await session.StopDispatchAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ReadyAfterPrepareSendCannotAcknowledgeWhenStopWinsFinalCommit()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time);
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        Task<RemoteWindowPreparationDeliveryResult> preparing = session.PrepareAsync(
            preparation,
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForTimeReadsToSettleAsync(time, minimumReadCount: 5);

        Task? stopping = null;
        time.ScheduleCallback(
            2,
            () => stopping = session.StopDispatchAsync().AsTask());

        await Assert.ThrowsAsync<InvalidDataException>(() => session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateReady(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                ParticipantId,
                RemoteWindowPreparationResponse.Create(
                    preparation,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"),
                Now),
            CancellationToken.None).AsTask());
        await Assert.IsType<Task>(stopping).WaitAsync(TimeSpan.FromSeconds(5));
        RemoteWindowPreparationDeliveryResult result = await preparing;

        Assert.Equal(
            RemoteWindowControlDeliveryStatus.AcknowledgementLost,
            result.Status);
        Assert.Null(result.Response);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ReadyAfterPrepareSendCannotAcknowledgeWhenDeadlineWinsFinalCommit()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time);
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        Task<RemoteWindowPreparationDeliveryResult> preparing = session.PrepareAsync(
            preparation,
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForTimeReadsToSettleAsync(time, minimumReadCount: 5);

        time.ScheduleCallback(
            2,
            () => time.Advance(TimeSpan.FromSeconds(10)));

        await Assert.ThrowsAsync<InvalidDataException>(() => session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateReady(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                ParticipantId,
                RemoteWindowPreparationResponse.Create(
                    preparation,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"),
                Now),
            CancellationToken.None).AsTask());
        RemoteWindowPreparationDeliveryResult result = await preparing;

        Assert.Equal(
            RemoteWindowControlDeliveryStatus.AcknowledgementLost,
            result.Status);
        Assert.Null(result.Response);
        await session.StopDispatchAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task RejectionAfterPrepareSendIsLostWhenStopWinsBeforeFinalCommit()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time);
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        Task<RemoteWindowPreparationDeliveryResult> preparing = session.PrepareAsync(
            preparation,
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task? stopping = null;
        time.ScheduleCallback(
            1,
            () => stopping = session.StopDispatchAsync().AsTask());

        await Assert.ThrowsAsync<InvalidDataException>(() => session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateReady(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                ParticipantId,
                RemoteWindowPreparationResponse.Create(
                    preparation,
                    RemoteWindowPreparationOutcome.Rejected,
                    "participant_busy"),
                Now),
            CancellationToken.None).AsTask());
        await Assert.IsType<Task>(stopping).WaitAsync(TimeSpan.FromSeconds(5));
        RemoteWindowPreparationDeliveryResult result = await preparing;

        Assert.Equal(
            RemoteWindowControlDeliveryStatus.AcknowledgementLost,
            result.Status);
        Assert.Null(result.Response);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task AdmissionDuringReadySendCannotCompleteAfterStopWinsCommit()
    {
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new RecordingPreparationPeer(ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now),
            preparationPeer: peer);
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        connection.SendCallback = async (_, cancellationToken) =>
        {
            await session.DispatchAsync(
                RemoteWindowControlMessageCodec.CreateState(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    HostId,
                    CreateState(
                        correlationId,
                        RemoteWindowControlAction.Admission,
                        revision: 1),
                    Now),
                cancellationToken);
            await session.StopDispatchAsync();
        };

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                preparation,
                Now),
            CancellationToken.None);
        await session.StopDispatchAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, peer.CompletedAdmissionCount);
        Assert.False(peer.AdmissionCompleted.Task.IsCompleted);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ReadyDoesNotEnterWireAfterDeadlineAtSendAdmission()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(ParticipantId, HostId);
        var peer = new RecordingPreparationPeer(ParticipantId)
        {
            PrepareCallback = () => time.ScheduleCallback(
                3,
                () => time.Advance(TimeSpan.FromSeconds(1))),
        };
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time,
            preparationPeer: peer);
        var stopped = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration =
            session.RegisterLifetimeCancellationCallback(() => stopped.TrySetResult());
        session.StartDispatch();
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(1));

        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                preparation,
                Now),
            CancellationToken.None);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, peer.PrepareCount);
        Assert.Equal(0, connection.SendCount);
        await session.StopDispatchAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task AdmissionPublicationFailsWhenStopStartsInsideWireSend()
    {
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        Task<RemoteWindowPreparationDeliveryResult> preparing = session.PrepareAsync(
            preparation,
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateReady(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                ParticipantId,
                RemoteWindowPreparationResponse.Create(
                    preparation,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"),
                Now),
            CancellationToken.None);
        Assert.Equal(
            RemoteWindowControlDeliveryStatus.Acknowledged,
            (await preparing).Status);
        connection.SendCallback = (_, _) => session.StopDispatchAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.PublishAdmissionStateAsync(
                CreateState(
                    correlationId,
                    RemoteWindowControlAction.Admission,
                    revision: 1),
                CancellationToken.None).AsTask());

        await session.StopDispatchAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task AdmissionPublicationFailsWhenDeadlineCrossesInsideWireSend()
    {
        var time = new ManualTimeProvider(Now);
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time);
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(1));
        Task<RemoteWindowPreparationDeliveryResult> preparing = session.PrepareAsync(
            preparation,
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateReady(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                ParticipantId,
                RemoteWindowPreparationResponse.Create(
                    preparation,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"),
                Now),
            CancellationToken.None);
        Assert.Equal(
            RemoteWindowControlDeliveryStatus.Acknowledged,
            (await preparing).Status);
        connection.SendCallback = (_, _) =>
        {
            time.Advance(TimeSpan.FromSeconds(1));
            return ValueTask.CompletedTask;
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.PublishAdmissionStateAsync(
                CreateState(
                    correlationId,
                    RemoteWindowControlAction.Admission,
                    revision: 1),
                CancellationToken.None).AsTask());

        await session.StopDispatchAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task AdmissionDoesNotEnterWireAfterDeadlineAtSendAdmission()
    {
        var time = new ReentrantTimeProvider(Now);
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time);
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        Task<RemoteWindowPreparationDeliveryResult> preparing = session.PrepareAsync(
            preparation,
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateReady(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                ParticipantId,
                RemoteWindowPreparationResponse.Create(
                    preparation,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"),
                Now),
            CancellationToken.None);
        Assert.Equal(
            RemoteWindowControlDeliveryStatus.Acknowledged,
            (await preparing).Status);
        await WaitForTimeReadsToSettleAsync(time, minimumReadCount: 7);
        time.ScheduleCallback(
            3,
            () => time.Advance(TimeSpan.FromSeconds(10)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.PublishAdmissionStateAsync(
                CreateState(
                    correlationId,
                    RemoteWindowControlAction.Admission,
                    revision: 1),
                CancellationToken.None).AsTask());

        Assert.Equal(1, connection.SendCount);
        await session.StopDispatchAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task AdmissionPublicationCannotCommitAfterCallerCancellationDuringSend()
    {
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(10));
        Task<RemoteWindowPreparationDeliveryResult> preparing = session.PrepareAsync(
            preparation,
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateReady(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                ParticipantId,
                RemoteWindowPreparationResponse.Create(
                    preparation,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"),
                Now),
            CancellationToken.None);
        Assert.Equal(
            RemoteWindowControlDeliveryStatus.Acknowledged,
            (await preparing).Status);
        using var cancellation = new CancellationTokenSource();
        connection.SendCallback = (_, _) =>
        {
            cancellation.Cancel();
            return ValueTask.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.PublishAdmissionStateAsync(
                CreateState(
                    correlationId,
                    RemoteWindowControlAction.Admission,
                    revision: 1),
                cancellation.Token).AsTask());

        Assert.Equal(2, connection.SendCount);
        await session.StopDispatchAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ReadyWithoutAdmissionCancelsConnectionAtPreparationDeadline()
    {
        var time = new ManualTimeProvider(Now);
        var connection = new PreparationConnection(HostId, ParticipantId);
        var session = new RemoteWindowControlSession(
            connection,
            timeProvider: time);
        var stopped = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration =
            session.RegisterLifetimeCancellationCallback(() => stopped.TrySetResult());
        session.StartDispatch();
        CorrelationId correlationId = CorrelationId.Parse(
            "cccccccc-cccc-cccc-cccc-cccccccccccc");
        RemoteWindowPreparationRequest preparation =
            RemoteWindowPreparationRequest.Create(
                correlationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddSeconds(1));
        Task<RemoteWindowPreparationDeliveryResult> preparing = session.PrepareAsync(
            preparation,
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DispatchAsync(
            RemoteWindowControlMessageCodec.CreateReady(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                ParticipantId,
                RemoteWindowPreparationResponse.Create(
                    preparation,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"),
                Now),
            CancellationToken.None);
        Assert.Equal(
            RemoteWindowControlDeliveryStatus.Acknowledged,
            (await preparing).Status);

        time.Advance(TimeSpan.FromSeconds(1));

        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopDispatchAsync();
        await session.DisposeAsync();
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

    private static RemoteWindowPreparationRequest CreatePreparation(
        string correlationId = "cccccccc-cccc-cccc-cccc-cccccccccccc",
        MirrorParticipantRole requestedRole = MirrorParticipantRole.ViewOnly,
        DateTimeOffset? deadline = null) => RemoteWindowPreparationRequest.Create(
            CorrelationId.Parse(correlationId),
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            requestedRole,
            deadline ?? Now.AddSeconds(10));

    private static ControlMessage CreateReady(
        RemoteWindowPreparationRequest preparation,
        RemoteWindowPreparationOutcome outcome = RemoteWindowPreparationOutcome.Ready) =>
        RemoteWindowControlMessageCodec.CreateReady(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            ParticipantId,
            RemoteWindowPreparationResponse.Create(
                preparation,
                outcome,
                outcome is RemoteWindowPreparationOutcome.Ready
                    ? "participant_ready"
                    : "participant_busy"),
            Now);

    private static RemoteWindowParticipantState CreateState(
        CorrelationId correlationId,
        RemoteWindowControlAction action,
        long revision,
        MirrorParticipantRole effectiveRole = MirrorParticipantRole.ViewOnly) =>
        RemoteWindowParticipantState.Create(
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
            effectiveRole,
            HostId,
            driverLeaseEpoch: 1,
            Now.AddMinutes(1),
            ProtectionKind.Safe,
            revision);

    private static CorrelationId CreateCorrelationId(int index) =>
        CorrelationId.Parse($"{index + 1:x8}-0000-0000-0000-000000000001");

    private static async Task WaitForTimeReadsToSettleAsync(
        ReentrantTimeProvider time,
        int minimumReadCount)
    {
        int stableYields = 0;
        int observed = time.ReadCount;
        while (observed < minimumReadCount || stableYields < 8)
        {
            await Task.Yield();
            int current = time.ReadCount;
            if (current == observed && current >= minimumReadCount)
            {
                stableYields++;
            }
            else
            {
                observed = current;
                stableYields = 0;
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ReentrantTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private Action? callback;
        private int readCount;
        private int readsUntilCallback;
        private DateTimeOffset utcNow = initialUtcNow;

        public int ReadCount => Volatile.Read(ref readCount);

        public void Advance(TimeSpan elapsed) => utcNow = utcNow.Add(elapsed);

        public void ScheduleCallback(int readsUntilCallback, Action callback)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(readsUntilCallback, 1);
            ArgumentNullException.ThrowIfNull(callback);
            Volatile.Write(ref this.readsUntilCallback, readsUntilCallback);
            Volatile.Write(ref this.callback, callback);
        }

        public override DateTimeOffset GetUtcNow()
        {
            Interlocked.Increment(ref readCount);
            Action? scheduled = Volatile.Read(ref callback);
            if (scheduled is not null
                && Interlocked.Decrement(ref readsUntilCallback) == 0
                && ReferenceEquals(
                    Interlocked.CompareExchange(ref callback, null, scheduled),
                    scheduled))
            {
                scheduled();
            }

            return utcNow;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly Lock gate = new();
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset utcNow = utcNow;

        public void Advance(TimeSpan elapsed)
        {
            List<ManualTimer> candidates;
            DateTimeOffset now;
            lock (gate)
            {
                utcNow = utcNow.Add(elapsed);
                now = utcNow;
                candidates = timers.ToList();
            }

            foreach (ManualTimer timer in candidates.Where(timer => timer.IsDue(now)))
            {
                timer.Fire(now);
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (gate)
            {
                timers.Add(timer);
            }

            return timer;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
            {
                return utcNow;
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            private DateTimeOffset dueAt = DateTimeOffset.MaxValue;
            private bool disposed;
            private TimeSpan period = Timeout.InfiniteTimeSpan;

            public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
            {
                lock (owner.gate)
                {
                    if (disposed)
                    {
                        return false;
                    }

                    dueAt = dueTime == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : owner.utcNow.Add(dueTime);
                    period = newPeriod;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner.gate)
                {
                    disposed = true;
                    owner.timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Fire(DateTimeOffset now)
            {
                lock (owner.gate)
                {
                    if (disposed || dueAt > now)
                    {
                        return;
                    }

                    dueAt = period == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : now.Add(period);
                }

                callback(state);
            }

            public bool IsDue(DateTimeOffset now)
            {
                lock (owner.gate)
                {
                    return !disposed && dueAt <= now;
                }
            }
        }
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

    private sealed class PreparationConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) : IRemoteWindowControlConnection
    {
        private int sendCount;

        public bool FailSubsequentSends { get; init; }

        public TaskCompletionSource<Exception?> CallbackCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } =
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion;

        public TaskCompletionSource<ControlMessage> Sent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SendCount => Volatile.Read(ref sendCount);

        public Func<ControlMessage, CancellationToken, ValueTask>? SendCallback
        {
            get;
            set;
        }

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ControlMessage>(new InvalidOperationException());

        public async ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default)
        {
            int count = Interlocked.Increment(ref sendCount);
            if (FailSubsequentSends && count > 1)
            {
                throw new IOException("A second wire send was not expected.");
            }

            Sent.TrySetResult(message);
            if (SendCallback is null)
            {
                return;
            }

            try
            {
                await SendCallback(message, cancellationToken);
                CallbackCompleted.TrySetResult(null);
            }
            catch (Exception exception)
            {
                CallbackCompleted.TrySetResult(exception);
                throw;
            }
        }
    }

    private sealed class RecordingPreparationPeer(DeviceId participantDeviceId) :
        IRemoteWindowPreparationPeer
    {
        private int completedAdmissionCount;
        private int prepareCount;

        public TaskCompletionSource AdmissionCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CompletedAdmissionCount =>
            Volatile.Read(ref completedAdmissionCount);

        public int PrepareCount => Volatile.Read(ref prepareCount);

        public Action? PrepareCallback { get; init; }

        public RemoteWindowPreparationRequest? CompletedRequest { get; private set; }

        public RemoteWindowParticipantState? CompletedState { get; private set; }

        public DeviceId ParticipantDeviceId { get; } = participantDeviceId;

        public ValueTask<RemoteWindowPreparationResponse> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref prepareCount);
            PrepareCallback?.Invoke();
            return ValueTask.FromResult(
                RemoteWindowPreparationResponse.Create(
                    request,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"));
        }

        public ValueTask CompleteAdmissionAsync(
            RemoteWindowPreparationRequest request,
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken)
        {
            CompletedRequest = request;
            CompletedState = state;
            Interlocked.Increment(ref completedAdmissionCount);
            AdmissionCompleted.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask PeerDisconnectedAsync(
            DeviceId hostDeviceId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class ResponseCompletionPreparationPeer(
        DeviceId participantDeviceId,
        RemoteWindowPreparationOutcome outcome) : IRemoteWindowPreparationPeer
    {
        private int responseCompletionCount;

        public DeviceId ParticipantDeviceId { get; } = participantDeviceId;

        public Action? PrepareCallback { get; set; }

        public Func<RemoteWindowPreparationResponse, bool, ValueTask>?
            ResponseCompletionCallback
        { get; set; }

        public TaskCompletionSource ResponseCompletionCalled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ResponseCompletionCount =>
            Volatile.Read(ref responseCompletionCount);

        public bool ResponseCommittedAtCompletion { get; set; }

        public bool SendReturnedAtCompletion { get; set; }

        public ValueTask<RemoteWindowPreparationResponse> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            PrepareCallback?.Invoke();
            return ValueTask.FromResult(
                RemoteWindowPreparationResponse.Create(
                    request,
                    outcome,
                    outcome is RemoteWindowPreparationOutcome.Ready
                        ? "participant_ready"
                        : "participant_busy"));
        }

        public async ValueTask CompletePreparationResponseAsync(
            RemoteWindowPreparationResponse response,
            bool responseCommitted)
        {
            Interlocked.Increment(ref responseCompletionCount);
            ResponseCommittedAtCompletion = responseCommitted;
            try
            {
                if (ResponseCompletionCallback is not null)
                {
                    await ResponseCompletionCallback(response, responseCommitted);
                }
            }
            finally
            {
                ResponseCompletionCalled.TrySetResult();
            }
        }

        public ValueTask CompleteAdmissionAsync(
            RemoteWindowPreparationRequest request,
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask PeerDisconnectedAsync(
            DeviceId hostDeviceId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class DeadlineBlockingPreparationPeer(DeviceId participantDeviceId) :
        IRemoteWindowPreparationPeer
    {
        private int completedAdmissionCount;

        public TaskCompletionSource AdmissionCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CompletedAdmissionCount =>
            Volatile.Read(ref completedAdmissionCount);

        public DeviceId ParticipantDeviceId { get; } = participantDeviceId;

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<RemoteWindowPreparationResponse> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult();
                throw;
            }

            return RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready");
        }

        public ValueTask CompleteAdmissionAsync(
            RemoteWindowPreparationRequest request,
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref completedAdmissionCount);
            AdmissionCompleted.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask PeerDisconnectedAsync(
            DeviceId hostDeviceId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class SynchronouslyBlockingPreparationPeer(
        DeviceId participantDeviceId) : IRemoteWindowPreparationPeer, IDisposable
    {
        private int cancellationWon;

        public bool CancellationWon => Volatile.Read(ref cancellationWon) != 0;

        public DeviceId ParticipantDeviceId { get; } = participantDeviceId;

        public ManualResetEvent Release { get; } = new(false);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() => Release.Dispose();

        public ValueTask<RemoteWindowPreparationResponse> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            int winner = WaitHandle.WaitAny(
                [cancellationToken.WaitHandle, Release]);
            if (winner == 0)
            {
                Interlocked.Exchange(ref cancellationWon, 1);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return ValueTask.FromResult(
                RemoteWindowPreparationResponse.Create(
                    request,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"));
        }

        public ValueTask CompleteAdmissionAsync(
            RemoteWindowPreparationRequest request,
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask PeerDisconnectedAsync(
            DeviceId hostDeviceId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class DisconnectReleasedPreparationPeer(
        DeviceId participantDeviceId) : IRemoteWindowPreparationPeer
    {
        private CancellationToken preparationCancellationToken;
        private int disconnectCount;

        public int DisconnectCount => Volatile.Read(ref disconnectCount);

        public DeviceId ParticipantDeviceId { get; } = participantDeviceId;

        public bool PrepareCancellationRequestedAtDisconnect { get; private set; }

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<RemoteWindowPreparationResponse> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            preparationCancellationToken = cancellationToken;
            Started.TrySetResult();
            await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready");
        }

        public ValueTask CompleteAdmissionAsync(
            RemoteWindowPreparationRequest request,
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask PeerDisconnectedAsync(
            DeviceId hostDeviceId,
            CancellationToken cancellationToken)
        {
            PrepareCancellationRequestedAtDisconnect =
                preparationCancellationToken.IsCancellationRequested;
            Interlocked.Increment(ref disconnectCount);
            Release.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class WorkerExitAwaitingPreparationPeer(
        DeviceId participantDeviceId) : IRemoteWindowPreparationPeer
    {
        public TaskCompletionSource CleanupCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CleanupStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DeviceId ParticipantDeviceId { get; } = participantDeviceId;

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource WorkerExited { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<RemoteWindowPreparationResponse> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                WorkerExited.TrySetResult();
            }

            return RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready");
        }

        public ValueTask CompleteAdmissionAsync(
            RemoteWindowPreparationRequest request,
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async ValueTask PeerDisconnectedAsync(
            DeviceId hostDeviceId,
            CancellationToken cancellationToken)
        {
            CleanupStarted.TrySetResult();
            await WorkerExited.Task;
            CleanupCompleted.TrySetResult();
        }
    }

    private sealed class ReentrantStopPreparationPeer(DeviceId participantDeviceId) :
        IRemoteWindowPreparationPeer
    {
        public DeviceId ParticipantDeviceId { get; } = participantDeviceId;

        public RemoteWindowControlSession? Session { get; set; }

        public TaskCompletionSource StopReturned { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<RemoteWindowPreparationResponse> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            await (Session ?? throw new InvalidOperationException(
                "The session was not configured.")).StopDispatchAsync();
            StopReturned.TrySetResult();
            cancellationToken.ThrowIfCancellationRequested();
            return RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready");
        }

        public ValueTask CompleteAdmissionAsync(
            RemoteWindowPreparationRequest request,
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask PeerDisconnectedAsync(
            DeviceId hostDeviceId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class DeadlineCrossingCompletionPeer(
        DeviceId participantDeviceId) : IRemoteWindowPreparationPeer
    {
        public TaskCompletionSource CompletionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DeviceId ParticipantDeviceId { get; } = participantDeviceId;

        public TaskCompletionSource ReleaseCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<RemoteWindowPreparationResponse> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                RemoteWindowPreparationResponse.Create(
                    request,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"));

        public async ValueTask CompleteAdmissionAsync(
            RemoteWindowPreparationRequest request,
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken)
        {
            CompletionStarted.TrySetResult();
            await ReleaseCompletion.Task;
        }

        public ValueTask PeerDisconnectedAsync(
            DeviceId hostDeviceId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
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
