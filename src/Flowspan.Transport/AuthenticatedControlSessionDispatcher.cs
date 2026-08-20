using System.Runtime.ExceptionServices;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Transport;

internal sealed class AuthenticatedControlSessionDispatcher : IAsyncDisposable
{
    private readonly Func<CancellationToken, ValueTask<ControlMessage>> receive;
    private readonly RoutedConnection routedConnection;

    public AuthenticatedControlSessionDispatcher(
        AuthenticatedTcpControlConnection connection)
        : this(
            connection?.LocalDeviceId
                ?? throw new ArgumentNullException(nameof(connection)),
            connection.PeerIdentity.DeviceId,
            connection.ProtocolVersion,
            connection.ReceiveAsync,
            connection.SendAsync)
    {
    }

    internal AuthenticatedControlSessionDispatcher(
        DeviceId localDeviceId,
        DeviceId peerDeviceId,
        ProtocolVersion protocolVersion,
        Func<CancellationToken, ValueTask<ControlMessage>> receive,
        Func<ControlMessage, CancellationToken, ValueTask> send)
    {
        ArgumentNullException.ThrowIfNull(localDeviceId);
        ArgumentNullException.ThrowIfNull(peerDeviceId);
        this.receive = receive ?? throw new ArgumentNullException(nameof(receive));
        ArgumentNullException.ThrowIfNull(send);
        routedConnection = new RoutedConnection(
            localDeviceId,
            peerDeviceId,
            protocolVersion,
            send);
        ActivityConnection = routedConnection;
        RemoteWindowConnection = ProtocolFeatures.SupportsRemoteWindow(protocolVersion)
            ? routedConnection
            : null;
    }

    public IActivityControlConnection ActivityConnection { get; }

    public IRemoteWindowControlConnection? RemoteWindowConnection { get; }

    internal bool HasStartedStopping => routedConnection.HasStartedStopping;

    internal ValueTask StopSendsAsync() => routedConnection.StopAsync();

    public ValueTask DisposeAsync() => routedConnection.DisposeAsync();

    public async ValueTask RunAsync(
        ActivityControlSession activitySession,
        RemoteWindowControlSession? remoteWindowSession,
        Func<IDisposable> enterSessionCall,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activitySession);
        ArgumentNullException.ThrowIfNull(enterSessionCall);
        if ((RemoteWindowConnection is null) != (remoteWindowSession is null))
        {
            throw new ArgumentException(
                "The Remote Window session must match negotiated protocol support.",
                nameof(remoteWindowSession));
        }

        Exception? failure = null;
        bool activityStarted = false;
        bool remoteWindowStarted = false;
        try
        {
            activitySession.StartDispatch();
            activityStarted = true;
            remoteWindowSession?.StartDispatch();
            remoteWindowStarted = remoteWindowSession is not null;
            using CancellationTokenSource linked = remoteWindowSession is null
                ? CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    activitySession.LifetimeCancellationToken)
                : CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    activitySession.LifetimeCancellationToken,
                    remoteWindowSession.LifetimeCancellationToken);
            try
            {
                while (true)
                {
                    ControlMessage message = await receive(linked.Token)
                        .ConfigureAwait(false);
                    switch (GetFamily(message.Type))
                    {
                        case ControlMessageFamily.Activity:
                            {
                                using IDisposable sessionCall = enterSessionCall();
                                await activitySession.DispatchAsync(message, linked.Token)
                                    .ConfigureAwait(false);
                                break;
                            }
                        case ControlMessageFamily.RemoteWindow
                            when remoteWindowSession is not null:
                            {
                                using IDisposable sessionCall = enterSessionCall();
                                await remoteWindowSession.DispatchAsync(message, linked.Token)
                                    .ConfigureAwait(false);
                                break;
                            }
                        default:
                            throw new InvalidDataException(
                                "The authenticated control message has no negotiated route.");
                    }
                }
            }
            catch (IOException exception) when (linked.IsCancellationRequested)
            {
                failure = new OperationCanceledException(
                    "The authenticated control session was stopped.",
                    exception,
                    linked.Token);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        Exception? cleanupFailure = null;
        try
        {
            await routedConnection.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }

        if (activityStarted)
        {
            activitySession.StopDispatch();
        }

        if (remoteWindowStarted)
        {
            try
            {
                using IDisposable sessionCall = enterSessionCall();
                await remoteWindowSession!.StopDispatchAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupFailure = CombineFailures(cleanupFailure, exception);
            }
        }

        if (cleanupFailure is not null)
        {
            failure = CombineFailures(failure, cleanupFailure);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static Exception CombineFailures(
        Exception? first,
        Exception second) => first is null
            ? second
            : new AggregateException(
                "The authenticated control session and its cleanup failed.",
                first,
                second);

    private static ControlMessageFamily GetFamily(ControlMessageType type) => type switch
    {
        ControlMessageType.ActivityTransfer
            or ControlMessageType.ActivityReplaceInventory
            or ControlMessageType.ActivityReplaceInventoryResult
            or ControlMessageType.ActivityReplace
            or ControlMessageType.ActivityReplaceResult
            or ControlMessageType.ActivitySwapSnapshot
            or ControlMessageType.ActivitySwapSnapshotResult
            or ControlMessageType.ActivitySwapPrepare
            or ControlMessageType.ActivitySwapPrepareResult
            or ControlMessageType.ActivitySwapDecision
            or ControlMessageType.ActivitySwapDecisionResult
            or ControlMessageType.OperationReceipt
            or ControlMessageType.SceneSourceLookup
            or ControlMessageType.SceneSourceLookupResult
            or ControlMessageType.SceneSlotInspection
            or ControlMessageType.SceneSlotInspectionResult
            or ControlMessageType.SceneChildOperation
            or ControlMessageType.SceneChildOperationResult
            or ControlMessageType.SceneUndoReplace
            or ControlMessageType.SceneUndoReplaceResult => ControlMessageFamily.Activity,
        ControlMessageType.RemoteWindowAdmission
            or ControlMessageType.RemoteWindowDriver
            or ControlMessageType.RemoteWindowInput
            or ControlMessageType.RemoteWindowDisconnect
            or ControlMessageType.RemoteWindowState => ControlMessageFamily.RemoteWindow,
        _ => throw new InvalidDataException(
            "The authenticated control message type is not valid after the handshake."),
    };

    private enum ControlMessageFamily
    {
        Activity,
        RemoteWindow,
    }

    private sealed class RoutedConnection :
        IActivityControlConnection,
        IRemoteWindowControlConnection,
        IAsyncDisposable
    {
        private readonly AsyncLocal<SendCallScope?> activeSendCall = new();
        private readonly object gate = new();
        private readonly CancellationTokenSource lifetimeCancellation = new();
        private readonly Func<ControlMessage, CancellationToken, ValueTask> send;
        private readonly TaskCompletionSource stopCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int activeSends;
        private TaskCompletionSource? sendDrainCompletion;
        private int stopStarted;
        private bool stopped;

        public RoutedConnection(
            DeviceId localDeviceId,
            DeviceId peerDeviceId,
            ProtocolVersion protocolVersion,
            Func<ControlMessage, CancellationToken, ValueTask> send)
        {
            LocalDeviceId = localDeviceId;
            PeerDeviceId = peerDeviceId;
            ProtocolVersion = protocolVersion;
            this.send = send;
        }

        public DeviceId LocalDeviceId { get; }

        public DeviceId PeerDeviceId { get; }

        public ProtocolVersion ProtocolVersion { get; }

        public bool HasStartedStopping => Volatile.Read(ref stopStarted) != 0;

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ControlMessage>(new InvalidOperationException(
                "Only the authenticated connection dispatcher may read production control messages."));

        public async ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default)
        {
            CancellationTokenSource linked;
            SendCallScope? inheritedScope = activeSendCall.Value;
            var currentScope = new SendCallScope(this, inheritedScope);
            lock (gate)
            {
                if (stopped)
                {
                    throw new IOException(
                        "The authenticated control connection has stopped sending.");
                }

                linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    lifetimeCancellation.Token);
                activeSends++;
            }

            activeSendCall.Value = currentScope;
            try
            {
                try
                {
                    await send(message, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception) when (
                    lifetimeCancellation.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested)
                {
                    throw new IOException(
                        "The authenticated control connection stopped during send.",
                        exception);
                }
            }
            finally
            {
                currentScope.Deactivate();
                activeSendCall.Value = inheritedScope;
                linked.Dispose();
                CompleteSend();
            }
        }

        public ValueTask StopAsync()
        {
            bool calledFromActiveSend = IsActiveSendCall();
            if (Interlocked.CompareExchange(ref stopStarted, 1, 0) == 0)
            {
                _ = CompleteStopAsync();
            }

            return calledFromActiveSend
                ? ValueTask.CompletedTask
                : new ValueTask(stopCompletion.Task);
        }

        public ValueTask DisposeAsync() => StopAsync();

        private async Task CompleteStopAsync()
        {
            Exception? failure = null;
            Task? sendDrain;
            lock (gate)
            {
                stopped = true;
                if (activeSends == 0)
                {
                    sendDrain = null;
                }
                else
                {
                    sendDrainCompletion ??= new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    sendDrain = sendDrainCompletion.Task;
                }
            }

            try
            {
                lifetimeCancellation.Cancel();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (sendDrain is not null)
            {
                try
                {
                    await sendDrain.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failure = CombineFailures(failure, exception);
                }
            }

            try
            {
                lifetimeCancellation.Dispose();
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }

            if (failure is null)
            {
                stopCompletion.TrySetResult();
            }
            else
            {
                stopCompletion.TrySetException(failure);
            }
        }

        private void CompleteSend()
        {
            TaskCompletionSource? completed = null;
            lock (gate)
            {
                activeSends--;
                if (activeSends == 0)
                {
                    completed = sendDrainCompletion;
                }
            }

            completed?.TrySetResult();
        }

        private bool IsActiveSendCall()
        {
            for (SendCallScope? scope = activeSendCall.Value;
                scope is not null;
                scope = scope.Previous)
            {
                if (scope.IsActive && ReferenceEquals(scope.Owner, this))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class SendCallScope(
            RoutedConnection owner,
            SendCallScope? previous)
        {
            private int active = 1;

            public bool IsActive => Volatile.Read(ref active) != 0;

            public RoutedConnection Owner { get; } = owner;

            public SendCallScope? Previous { get; } = previous;

            public void Deactivate() => Volatile.Write(ref active, 0);
        }
    }
}
