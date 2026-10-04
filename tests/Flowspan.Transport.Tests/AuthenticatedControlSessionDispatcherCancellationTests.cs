using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedControlSessionDispatcherCancellationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static readonly DeviceId LocalId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId PeerId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task CanceledCallerPreservesOriginalTokenBeforeLinkedCancellationPropagates()
    {
        var receiveFailure = new EndOfStreamException("control-channel-closed");

        (Exception failure, CancellationToken callerToken) =
            await RunWithBlockedCallerPropagationAsync(receiveFailure);

        var cancellation = Assert.IsType<OperationCanceledException>(failure);
        Assert.Equal(callerToken, cancellation.CancellationToken);
        Assert.Same(receiveFailure, cancellation.InnerException);
    }

    [Fact]
    public async Task UncanceledCallerPreservesOriginalReceiveEof()
    {
        var receiveFailure = new EndOfStreamException("control-channel-closed");
        using var caller = new CancellationTokenSource();
        await using var dispatcher = new AuthenticatedControlSessionDispatcher(
            LocalId,
            PeerId,
            new ProtocolVersion(1, 4),
            _ => ValueTask.FromException<ControlMessage>(receiveFailure),
            static (_, _) => ValueTask.CompletedTask);
        await using var session = new ActivityControlSession(
            dispatcher.ActivityConnection,
            new RejectingActivityPeer(LocalId));

        EndOfStreamException failure = await Assert.ThrowsAsync<EndOfStreamException>(
            () => dispatcher.RunAsync(
                session,
                remoteWindowSession: null,
                static () => NullDisposable.Instance,
                originalCallerCancellationToken: caller.Token).AsTask().WaitAsync(Timeout));

        Assert.False(caller.IsCancellationRequested);
        Assert.Same(receiveFailure, failure);
    }

    [Fact]
    public async Task CallerCancellationDuringCleanupCannotRelabelAlreadyRecordedEof()
    {
        var receiveFailure = new EndOfStreamException("control-channel-closed");
        var cleanupStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = new CancellationTokenSource();
        using var handlerLinked = CancellationTokenSource.CreateLinkedTokenSource(
            caller.Token);
        await using var dispatcher = new AuthenticatedControlSessionDispatcher(
            LocalId,
            PeerId,
            new ProtocolVersion(1, 4),
            _ => ValueTask.FromException<ControlMessage>(receiveFailure),
            static (_, _) => ValueTask.CompletedTask);
        await using var session = new ActivityControlSession(
            dispatcher.ActivityConnection,
            new RejectingActivityPeer(LocalId));
        Task run = dispatcher.RunAsync(
            session,
            remoteWindowSession: null,
            static () => NullDisposable.Instance,
            beginOwnedCleanup: () =>
            {
                cleanupStarted.TrySetResult();
                return new ValueTask(releaseCleanup.Task);
            },
            cancellationToken: handlerLinked.Token,
            originalCallerCancellationToken: caller.Token).AsTask();
        try
        {
            await cleanupStarted.Task.WaitAsync(Timeout);
            Assert.False(caller.IsCancellationRequested);
            Assert.False(run.IsCompleted);

            caller.Cancel();
            releaseCleanup.TrySetResult();

            EndOfStreamException failure =
                await Assert.ThrowsAsync<EndOfStreamException>(() =>
                    run.WaitAsync(Timeout));
            Assert.Same(receiveFailure, failure);
        }
        finally
        {
            releaseCleanup.TrySetResult();
            _ = await Record.ExceptionAsync(() => run.WaitAsync(Timeout));
        }
    }

    [Fact]
    public async Task CanceledCallerCannotRelabelReceiveAggregateContainingIo()
    {
        var receiveFailure = new AggregateException(
            new EndOfStreamException("control-channel-closed"),
            new InvalidOperationException("independent-receive-failure"));

        (Exception failure, _) =
            await RunWithBlockedCallerPropagationAsync(receiveFailure);

        Assert.Same(receiveFailure, Assert.IsType<AggregateException>(failure));
    }

    [Fact]
    public async Task CanceledCallerPreservesOriginalReceiveOutOfMemory()
    {
#pragma warning disable CA2201 // Intentional fatal-runtime injection at the receive boundary.
        var receiveFailure = new OutOfMemoryException("receive-exhausted");
#pragma warning restore CA2201

        (Exception failure, _) =
            await RunWithBlockedCallerPropagationAsync(receiveFailure);

        Assert.Same(receiveFailure, Assert.IsType<OutOfMemoryException>(failure));
    }

    [Fact]
    public async Task CanceledReceiveIoAndOwnedCleanupFailureBothRemainObservable()
    {
        var receiveFailure = new EndOfStreamException("control-channel-closed");
        var cleanupFailure = new InvalidOperationException("owned-cleanup-failed");

        (Exception failure, CancellationToken callerToken) =
            await RunWithBlockedCallerPropagationAsync(
                receiveFailure,
                beginOwnedCleanup: () => throw cleanupFailure);

        var aggregate = Assert.IsType<AggregateException>(failure);
        Assert.Collection(
            aggregate.InnerExceptions,
            item =>
            {
                var cancellation = Assert.IsType<OperationCanceledException>(item);
                Assert.Equal(callerToken, cancellation.CancellationToken);
                Assert.Same(receiveFailure, cancellation.InnerException);
            },
            item => Assert.Same(cleanupFailure, item));
    }

    private static async Task<(Exception Failure, CancellationToken CallerToken)>
        RunWithBlockedCallerPropagationAsync(
            Exception receiveFailure,
            Func<ValueTask>? beginOwnedCleanup = null)
    {
        var receiveStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReceive = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationCallbackStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCancellationCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = new CancellationTokenSource();
        CancellationToken callerToken = caller.Token;
        using var handlerLinked = CancellationTokenSource.CreateLinkedTokenSource(
            callerToken);
        CancellationToken readToken = default;
        await using var dispatcher = new AuthenticatedControlSessionDispatcher(
            LocalId,
            PeerId,
            new ProtocolVersion(1, 4),
            async cancellationToken =>
            {
                readToken = cancellationToken;
                receiveStarted.TrySetResult();
                await releaseReceive.Task;
                throw receiveFailure;
            },
            static (_, _) => ValueTask.CompletedTask);
        await using var session = new ActivityControlSession(
            dispatcher.ActivityConnection,
            new RejectingActivityPeer(LocalId));
        Task run = dispatcher.RunAsync(
            session,
            remoteWindowSession: null,
            static () => NullDisposable.Instance,
            beginOwnedCleanup: beginOwnedCleanup,
            cancellationToken: handlerLinked.Token,
            originalCallerCancellationToken: callerToken).AsTask();
        // Cancellation callbacks run newest first. This registration holds the
        // caller's cancellation before its older handler-link callback can run.
        using CancellationTokenRegistration cancellationBarrier = callerToken.Register(
            () =>
            {
                cancellationCallbackStarted.TrySetResult();
                releaseCancellationCallback.Task.WaitAsync(Timeout)
                    .GetAwaiter().GetResult();
            });
        Task? canceling = null;
        try
        {
            await receiveStarted.Task.WaitAsync(Timeout);
            canceling = Task.Run(() => caller.Cancel());
            await cancellationCallbackStarted.Task.WaitAsync(Timeout);
            Assert.True(callerToken.IsCancellationRequested);
            Assert.False(handlerLinked.IsCancellationRequested);
            Assert.True(readToken.CanBeCanceled);
            Assert.False(readToken.IsCancellationRequested);

            releaseReceive.TrySetResult();
            Exception? failure = await Record.ExceptionAsync(() => run.WaitAsync(Timeout));

            Assert.NotNull(failure);
            return (failure, callerToken);
        }
        finally
        {
            releaseReceive.TrySetResult();
            releaseCancellationCallback.TrySetResult();
            try
            {
                if (canceling is not null)
                {
                    await canceling.WaitAsync(Timeout);
                }
            }
            finally
            {
                _ = await Record.ExceptionAsync(() => run.WaitAsync(Timeout));
            }
        }
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
