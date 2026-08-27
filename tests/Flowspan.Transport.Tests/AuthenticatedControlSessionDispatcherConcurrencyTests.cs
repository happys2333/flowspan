using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedControlSessionDispatcherConcurrencyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 4, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId LocalId = DeviceId.Parse(
        "11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId PeerId = DeviceId.Parse(
        "22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task DispatcherShutdownDrainsStartedActivitySend()
    {
        var connection = new BlockingDispatcherConnection();
        await using var dispatcher = new AuthenticatedControlSessionDispatcher(
            LocalId,
            PeerId,
            new ProtocolVersion(1, 4),
            connection.ReceiveAsync,
            connection.SendAsync);
        await using var session = new ActivityControlSession(
            dispatcher.ActivityConnection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = dispatcher.RunAsync(
            session,
            remoteWindowSession: null,
            static () => NullDisposable.Instance,
            cancellationToken: stop.Token).AsTask();
        await connection.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<ActivityDeliveryResult> sending = session.SendAsync(
            LocalId,
            CreateOffer(),
            CancellationToken.None).AsTask();
        await connection.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task stoppingSends = dispatcher.StopSendsAsync().AsTask();

        Assert.False(stoppingSends.IsCompleted);
        connection.ReleaseSend.TrySetResult();
        await stoppingSends.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(
            ActivityDeliveryStatus.AcknowledgementLost,
            (await sending.WaitAsync(TimeSpan.FromSeconds(5))).Status);
    }

    [Fact]
    public async Task OwnedCleanupRevokesBeforeDispatcherStopAndDrainsAfterLocalStop()
    {
        var receiveStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReceive = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sendStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCleanup = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var receiveFailure = new InvalidDataException("receive-primary");
        bool dispatcherWasStoppingWhenCleanupStarted = true;
        await using var dispatcher = new AuthenticatedControlSessionDispatcher(
            LocalId,
            PeerId,
            new ProtocolVersion(1, 4),
            async cancellationToken =>
            {
                _ = cancellationToken;
                receiveStarted.TrySetResult();
                await releaseReceive.Task;
                throw receiveFailure;
            },
            (_, _) =>
            {
                sendStarted.TrySetResult();
                return ValueTask.CompletedTask;
            });
        await using var session = new ActivityControlSession(
            dispatcher.ActivityConnection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        Task run = dispatcher.RunAsync(
            session,
            remoteWindowSession: null,
            static () => NullDisposable.Instance,
            beginOwnedCleanup: () =>
            {
                dispatcherWasStoppingWhenCleanupStarted =
                    dispatcher.HasStartedStopping;
                cleanupStarted.TrySetResult();
                return new ValueTask(allowCleanup.Task);
            }).AsTask();
        await receiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<ActivityDeliveryResult> sending = session.SendAsync(
            LocalId,
            CreateOffer(),
            CancellationToken.None).AsTask();
        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        releaseReceive.TrySetResult();
        await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ActivityDeliveryResult delivery = await sending.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.False(dispatcherWasStoppingWhenCleanupStarted);
        Assert.True(dispatcher.HasStartedStopping);
        Assert.False(session.SupportsSwap);
        Assert.Equal(
            ActivityDeliveryStatus.AcknowledgementLost,
            delivery.Status);
        Assert.False(run.IsCompleted);
        allowCleanup.TrySetResult();
        InvalidDataException failure =
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(receiveFailure, failure);
    }

    [Fact]
    public async Task RunPreservesReceiveOwnedCleanupAndRoutedStopFailures()
    {
        var receiveStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReceive = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sendStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var receiveFailure = new InvalidDataException("receive-primary");
        var ownedCleanupFailure = new InvalidOperationException("owned-cleanup");
        var routedStopFailure = new InvalidOperationException("routed-stop");
        var dispatcher = new AuthenticatedControlSessionDispatcher(
            LocalId,
            PeerId,
            new ProtocolVersion(1, 4),
            async cancellationToken =>
            {
                _ = cancellationToken;
                receiveStarted.TrySetResult();
                await releaseReceive.Task;
                throw receiveFailure;
            },
            async (_, cancellationToken) =>
            {
                Task pending = Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                using CancellationTokenRegistration registration =
                    cancellationToken.Register(() => throw routedStopFailure);
                sendStarted.TrySetResult();
                await pending;
            });
        var session = new ActivityControlSession(
            dispatcher.ActivityConnection,
            new RejectingActivityPeer(LocalId),
            new FixedTimeProvider(Now));
        Task run = dispatcher.RunAsync(
            session,
            remoteWindowSession: null,
            static () => NullDisposable.Instance,
            beginOwnedCleanup: () => throw ownedCleanupFailure).AsTask();
        await receiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task sending = dispatcher.ActivityConnection.SendAsync(
            CreateMessage(),
            CancellationToken.None).AsTask();
        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        releaseReceive.TrySetResult();

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<IOException>(() =>
            sending.WaitAsync(TimeSpan.FromSeconds(5)));
        IReadOnlyCollection<Exception> failures =
            failure.Flatten().InnerExceptions;
        Assert.Contains(failures, item => ReferenceEquals(item, receiveFailure));
        Assert.Contains(failures, item => ReferenceEquals(item, ownedCleanupFailure));
        Assert.Contains(failures, item => ReferenceEquals(item, routedStopFailure));

        AggregateException repeatedStopFailure =
            await Assert.ThrowsAsync<AggregateException>(() =>
                dispatcher.DisposeAsync().AsTask());
        Assert.Contains(
            repeatedStopFailure.Flatten().InnerExceptions,
            item => ReferenceEquals(item, routedStopFailure));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task DispatcherStopIsReentrantFromSendCancellationCallback()
    {
        var sendStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool nestedDisposeCompleted = false;
        AuthenticatedControlSessionDispatcher? dispatcher = null;
        dispatcher = new AuthenticatedControlSessionDispatcher(
            LocalId,
            PeerId,
            new ProtocolVersion(1, 4),
            _ => ValueTask.FromException<ControlMessage>(
                new InvalidOperationException()),
            async (message, cancellationToken) =>
            {
                _ = message;
                Task pending = Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                using CancellationTokenRegistration registration =
                    cancellationToken.Register(() =>
                        nestedDisposeCompleted = Task.Run(async () =>
                        {
                            await dispatcher!.DisposeAsync();
                            return true;
                        }).GetAwaiter().GetResult());
                sendStarted.TrySetResult();
                await pending;
            });
        await using (dispatcher)
        {
            Task sending = dispatcher.ActivityConnection.SendAsync(
                ControlMessage.Create(
                    new ProtocolVersion(1, 4),
                    ControlMessageType.ActivityTransfer,
                    Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                    CorrelationId.Parse(
                        "cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    LocalId,
                    Now,
                    TimeSpan.FromSeconds(30),
                    "{}"),
                CancellationToken.None).AsTask();
            await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await dispatcher.StopSendsAsync().AsTask().WaitAsync(
                TimeSpan.FromSeconds(5));

            await Assert.ThrowsAsync<IOException>(() => sending);
            Assert.True(nestedDisposeCompleted);
        }
    }

    [Fact]
    public async Task DispatcherStopIsReentrantFromActiveSendDelegate()
    {
        bool nestedDisposeCompleted = false;
        AuthenticatedControlSessionDispatcher? dispatcher = null;
        dispatcher = new AuthenticatedControlSessionDispatcher(
            LocalId,
            PeerId,
            new ProtocolVersion(1, 4),
            _ => ValueTask.FromException<ControlMessage>(
                new InvalidOperationException()),
            (_, _) =>
            {
                nestedDisposeCompleted = dispatcher!.DisposeAsync().AsTask()
                    .IsCompletedSuccessfully;
                return ValueTask.CompletedTask;
            });

        await dispatcher.ActivityConnection.SendAsync(
            CreateMessage(),
            CancellationToken.None);
        await dispatcher.DisposeAsync();

        Assert.True(nestedDisposeCompleted);
    }

    [Fact]
    public async Task NestedSendAncestryRemainsReentrantAfterInnerSendReturns()
    {
        var copiedContextReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCopiedContext = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var nestedDisposalReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool nestedDisposeCompleted = false;
        int sendCount = 0;
        AuthenticatedControlSessionDispatcher? dispatcher = null;
        dispatcher = new AuthenticatedControlSessionDispatcher(
            LocalId,
            PeerId,
            new ProtocolVersion(1, 4),
            _ => ValueTask.FromException<ControlMessage>(
                new InvalidOperationException()),
            async (_, _) =>
            {
                if (Interlocked.Increment(ref sendCount) == 1)
                {
                    await dispatcher!.ActivityConnection.SendAsync(
                        CreateMessage(),
                        CancellationToken.None);
                    await copiedContextReady.Task;
                    releaseCopiedContext.TrySetResult();
                    await nestedDisposalReturned.Task;
                    return;
                }

                _ = DisposeFromCopiedInnerContextAsync();
            });

        await dispatcher.ActivityConnection.SendAsync(
            CreateMessage(),
            CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.DisposeAsync();

        Assert.True(nestedDisposeCompleted);

        async Task DisposeFromCopiedInnerContextAsync()
        {
            copiedContextReady.TrySetResult();
            await releaseCopiedContext.Task;
            nestedDisposeCompleted = dispatcher!.DisposeAsync().AsTask()
                .IsCompletedSuccessfully;
            await dispatcher.DisposeAsync();
            nestedDisposalReturned.TrySetResult();
        }
    }

    [Fact]
    public async Task CopiedSendCancellationContextJoinsStopAfterCallbackReturns()
    {
        var firstSendStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSendStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var copiedContextReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCopiedContext = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondSend = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var nestedDisposalCompleted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var nestedDisposalReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int sendCount = 0;
        AuthenticatedControlSessionDispatcher? dispatcher = null;
        dispatcher = new AuthenticatedControlSessionDispatcher(
            LocalId,
            PeerId,
            new ProtocolVersion(1, 4),
            _ => ValueTask.FromException<ControlMessage>(
                new InvalidOperationException()),
            async (message, cancellationToken) =>
            {
                _ = message;
                if (Interlocked.Increment(ref sendCount) == 1)
                {
                    Task pending = Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken);
                    using CancellationTokenRegistration registration =
                        cancellationToken.Register(() =>
                            _ = DisposeFromCopiedContextAsync());
                    firstSendStarted.TrySetResult();
                    await pending;
                    return;
                }

                secondSendStarted.TrySetResult();
                await releaseSecondSend.Task;
            });
        await using (dispatcher)
        {
            try
            {
                Task firstSend = dispatcher.ActivityConnection.SendAsync(
                    CreateMessage(),
                    CancellationToken.None).AsTask();
                Task secondSend = dispatcher.ActivityConnection.SendAsync(
                    CreateMessage(),
                    CancellationToken.None).AsTask();
                await firstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await secondSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

                Task stopping = dispatcher.StopSendsAsync().AsTask();
                await copiedContextReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.ThrowsAsync<IOException>(() => firstSend);
                releaseCopiedContext.TrySetResult();

                Assert.False(await nestedDisposalCompleted.Task.WaitAsync(
                    TimeSpan.FromSeconds(5)));
                Assert.False(nestedDisposalReturned.Task.IsCompleted);
                releaseSecondSend.TrySetResult();
                await secondSend;
                await stopping.WaitAsync(TimeSpan.FromSeconds(5));
                await nestedDisposalReturned.Task.WaitAsync(
                    TimeSpan.FromSeconds(5));
            }
            finally
            {
                releaseCopiedContext.TrySetResult();
                releaseSecondSend.TrySetResult();
            }
        }

        async Task DisposeFromCopiedContextAsync()
        {
            copiedContextReady.TrySetResult();
            await releaseCopiedContext.Task;
            Task nestedDisposal = dispatcher!.DisposeAsync().AsTask();
            nestedDisposalCompleted.TrySetResult(
                nestedDisposal.IsCompletedSuccessfully);
            await nestedDisposal;
            nestedDisposalReturned.TrySetResult();
        }
    }

    private static ActivityTransferOffer CreateOffer()
    {
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            LocalId,
            "Portable note",
            JsonSerializer.Serialize(new { text = "portable" }));
        return ActivityTransferOffer.Create(
            OperationKind.Handoff,
            OperationContext.Create(
                OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Now.AddSeconds(30)),
            descriptor,
            ActivityPlacement.On(PeerId, "desktop"));
    }

    private static ControlMessage CreateMessage() => ControlMessage.Create(
        new ProtocolVersion(1, 4),
        ControlMessageType.ActivityTransfer,
        Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        LocalId,
        Now,
        TimeSpan.FromSeconds(30),
        "{}");

    private sealed class BlockingDispatcherConnection
    {
        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSend { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SendStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ControlMessage> ReceiveAsync(
            CancellationToken cancellationToken)
        {
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The blocking read returned.");
        }

        public async ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken)
        {
            SendStarted.TrySetResult();
            await ReleaseSend.Task;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RejectingActivityPeer(DeviceId deviceId) : IActivityPeer
    {
        public DeviceId DeviceId { get; } = deviceId;

        public ValueTask<OperationReceipt> ReceiveActivityAsync(
            DeviceId senderDeviceId,
            ActivityTransferOffer offer,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<OperationReceipt>(
                new InvalidOperationException("No inbound Activity was expected."));
    }

    private sealed class NullDisposable : IDisposable
    {
        public static NullDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
