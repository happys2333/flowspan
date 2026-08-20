using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedControlSessionCleanupTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BothChildSessionsAreDisposedWhenBothCancellationCallbacksThrow()
    {
        DeviceId localDeviceId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId peerDeviceId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        var connection = new StubConnection(localDeviceId, peerDeviceId);
        var activitySession = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(localDeviceId));
        var remoteWindowSession = new RemoteWindowControlSession(connection);
        int activityCallbackCount = 0;
        int remoteWindowCallbackCount = 0;
        using CancellationTokenRegistration activityRegistration =
            activitySession.LifetimeCancellationToken.Register(() =>
            {
                Interlocked.Increment(ref activityCallbackCount);
                throw new ExpectedCleanupException("activity");
            });
        using CancellationTokenRegistration remoteWindowRegistration =
            remoteWindowSession.RegisterLifetimeCancellationCallback(() =>
            {
                Interlocked.Increment(ref remoteWindowCallbackCount);
                throw new ExpectedCleanupException("remote-window");
            });

        Exception? failure = await AuthenticatedActivitySessionHandler
            .DisposeSessionsAsync(activitySession, remoteWindowSession);

        AggregateException aggregate = Assert.IsType<AggregateException>(failure);
        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Equal(1, activityCallbackCount);
        Assert.Equal(1, remoteWindowCallbackCount);
    }

    [Fact]
    public async Task ThrowingActivityCancellationStillCompletesPendingCommand()
    {
        DeviceId localDeviceId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId peerDeviceId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        var connection = new SignalingActivityConnection(
            localDeviceId,
            peerDeviceId);
        await using var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(localDeviceId),
            new FixedTimeProvider(Now));
        session.StartDispatch();
        Task<ReplaceTargetInventoryDeliveryResult> querying = session.QueryAsync(
            localDeviceId,
            ReplaceTargetInventoryQuery.Create(
                CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                peerDeviceId,
                ActivityKind.Parse("workspace.note/v1"),
                Now.AddSeconds(30)),
            CancellationToken.None).AsTask();
        await connection.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using CancellationTokenRegistration registration =
            session.LifetimeCancellationToken.Register(() =>
                throw new ExpectedCleanupException("activity"));

        _ = Assert.Throws<AggregateException>(session.Cancel);

        Assert.Equal(
            ActivityDeliveryStatus.AcknowledgementLost,
            (await querying.WaitAsync(TimeSpan.FromSeconds(5))).Status);
    }

    [Fact]
    public async Task AllRegistrationCleanupFailuresAreObserved()
    {
        Task first = Task.FromException(
            new ExpectedCleanupException("first"));
        Task second = Task.FromException(
            new ExpectedCleanupException("second"));

        Exception[] failures = await AuthenticatedActivitySessionHandler
            .CollectCompletionFailuresAsync([first, second]);

        Assert.Equal(2, failures.Length);
        Assert.Contains(failures, failure => failure.Message == "first");
        Assert.Contains(failures, failure => failure.Message == "second");
    }

    [Fact]
    public async Task OwnedDispatcherPreservesRunAndStopFailures()
    {
        DeviceId localDeviceId = DeviceId.Parse(
            "11111111-1111-1111-1111-111111111111");
        DeviceId peerDeviceId = DeviceId.Parse(
            "22222222-2222-2222-2222-222222222222");
        var sendStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new AuthenticatedControlSessionDispatcher(
            localDeviceId,
            peerDeviceId,
            new ProtocolVersion(1, 4),
            async _ =>
            {
                await sendStarted.Task;
                return CreateUnroutedMessage(localDeviceId);
            },
            async (_, cancellationToken) =>
            {
                Task pending = Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                using CancellationTokenRegistration registration =
                    cancellationToken.Register(() =>
                        throw new ExpectedCleanupException("routed-stop"));
                sendStarted.TrySetResult();
                await pending;
            });
        var session = new ActivityControlSession(
            dispatcher.ActivityConnection,
            new RejectingActivityPeer(localDeviceId));
        Task sending = dispatcher.ActivityConnection.SendAsync(
            CreateUnroutedMessage(localDeviceId),
            CancellationToken.None).AsTask();

        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            AuthenticatedActivitySessionHandler.RunWithOwnedDispatcherAsync(
                dispatcher,
                () => dispatcher.RunAsync(
                    session,
                    remoteWindowSession: null,
                    static () => NullDisposable.Instance)).AsTask());
        await Assert.ThrowsAsync<IOException>(() => sending);
        await session.DisposeAsync();
        Exception[] flattened = failure is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions.ToArray()
            : [failure];

        Assert.Contains(
            flattened,
            exception => exception is InvalidDataException
                && exception.Message.Contains(
                    "not valid after the handshake",
                    StringComparison.Ordinal));
        Assert.Contains(
            flattened,
            exception => exception is ExpectedCleanupException
                && exception.Message == "routed-stop");
    }

    private static ControlMessage CreateUnroutedMessage(DeviceId senderDeviceId) =>
        ControlMessage.Create(
            new ProtocolVersion(1, 4),
            ControlMessageType.Hello,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CorrelationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            senderDeviceId,
            Now,
            TimeSpan.FromSeconds(30),
            "{}");

    private sealed class ExpectedCleanupException(string child) : Exception(child);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class NullDisposable : IDisposable
    {
        public static NullDisposable Instance { get; } = new();

        public void Dispose()
        {
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
                new InvalidOperationException("No Activity request is expected."));
    }

    private sealed class StubConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) :
        IActivityControlConnection,
        IRemoteWindowControlConnection
    {
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
            ValueTask.FromException(new InvalidOperationException());
    }

    private sealed class SignalingActivityConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) : IActivityControlConnection
    {
        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } = new(1, 4);

        public TaskCompletionSource Sent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ControlMessage>(new InvalidOperationException());

        public ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default)
        {
            Sent.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
