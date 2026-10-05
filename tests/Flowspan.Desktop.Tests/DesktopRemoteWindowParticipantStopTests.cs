using Flowspan.Domain;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed partial class DesktopRemoteWindowPreparationPeerTests
{
    [Fact]
    public async Task DelayedPreparationChildStopJoinsRendererAndConnectionAfterPreparationReturns()
    {
        var renderer = new ParticipantStopBlockingRenderer();
        var factory = new DelayedPreparationChildStopFactory(renderer);
        AuthenticatedRemoteWindowConnectionLease? participantLease = null;

        await RunConnectedScenarioAsync(
            factory,
            async context =>
            {
                factory.PreparationPeer = context.PreparationPeer;
                try
                {
                    (_, RemoteWindowPreparationResponse response) =
                        await context.PrepareAsync();
                    Assert.Equal(RemoteWindowPreparationOutcome.Ready, response.Outcome);
                    Assert.False(factory.ChildStop.IsCompleted);

                    factory.ReleaseChildStop();
                    Task stopping = await factory.StopReturned.Task.WaitAsync(
                        TimeSpan.FromSeconds(5));
                    await renderer.DisposeEntered.Task.WaitAsync(
                        TimeSpan.FromSeconds(5));

                    Assert.False(stopping.IsCompleted);
                    Assert.False(renderer.IsDisposed);

                    renderer.ReleaseDisposal();
                    await factory.ChildStop.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.True(stopping.IsCompletedSuccessfully);
                    Assert.True(renderer.IsDisposed);
                    AuthenticatedRemoteWindowConnectionLease lease =
                        Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(
                            participantLease);
                    Assert.False(lease.IsCurrent);
                    Task connectionClosed = lease.WaitForConnectionClosedAsync().AsTask();
                    Assert.True(connectionClosed.IsCompletedSuccessfully);
                }
                finally
                {
                    factory.ReleaseChildStop();
                    renderer.ReleaseDisposal();
                    await factory.ChildStop.WaitAsync(TimeSpan.FromSeconds(5));
                    await context.PreparationPeer.StopReceivingAsync().AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5));
                }
            },
            decorateConnectionAcquirer: acquire =>
                (DeviceId peerDeviceId,
                    out AuthenticatedRemoteWindowConnectionLease? lease) =>
                {
                    bool acquired = acquire(peerDeviceId, out lease);
                    participantLease = lease;
                    return acquired;
                });
    }

    [Fact]
    public Task PreparationCallbackStopRetainsLateRendererUntilExternalStopDrains() =>
        RunPreparationCallbackStopRetainsLateRendererAsync(preparationOwnsCleanup: true);

    [Fact]
    public Task PreparationCallbackStopJoinsExternallyClaimedLateRendererCleanup() =>
        RunPreparationCallbackStopRetainsLateRendererAsync(preparationOwnsCleanup: false);

    private static async Task RunPreparationCallbackStopRetainsLateRendererAsync(
        bool preparationOwnsCleanup)
    {
        var renderer = new ParticipantStopBlockingRenderer();
        using var factory = new StopInsidePreparationFactory(renderer);
        AuthenticatedRemoteWindowConnectionLease? participantLease = null;

        await RunConnectedScenarioAsync(
            factory,
            async context =>
            {
                factory.PreparationPeer = context.PreparationPeer;
                Task<(RemoteWindowPreparationRequest Request,
                    RemoteWindowPreparationResponse Response)> preparing =
                    context.PrepareAsync();
                Task disconnecting = Task.CompletedTask;
                try
                {
                    bool callbackStopCompleted = await factory.StopReturned.Task.WaitAsync(
                        TimeSpan.FromSeconds(5));
                    Assert.True(callbackStopCompleted);
                    await factory.CancellationObserved.Task.WaitAsync(
                        TimeSpan.FromSeconds(5));
                    Assert.False(preparing.IsCompleted);
                    Task stopping = context.PreparationPeer.StopReceivingAsync().AsTask();
                    Assert.False(stopping.IsCompleted);
                    Assert.False(renderer.IsDisposed);

                    if (!preparationOwnsCleanup)
                    {
                        // The stop worker is still inside the controlled cancellation
                        // callback, so this external caller claims cleanup first.
                        disconnecting = context.PreparationPeer.PeerDisconnectedAsync(
                            HostDeviceId,
                            default).AsTask();
                        Assert.False(disconnecting.IsCompleted);
                    }

                    factory.ReleasePreparation();
                    await renderer.DisposeEntered.Task.WaitAsync(
                        TimeSpan.FromSeconds(5));
                    Assert.False(stopping.IsCompleted);
                    Assert.False(renderer.IsDisposed);
                    if (preparationOwnsCleanup)
                    {
                        Assert.False(preparing.IsCompleted);
                    }
                    else
                    {
                        await preparing.WaitAsync(TimeSpan.FromSeconds(5));
                        Assert.True(preparing.IsCompletedSuccessfully);
                        Assert.False(disconnecting.IsCompleted);
                    }

                    factory.ReleaseCancellation();
                    renderer.ReleaseDisposal();
                    (_, RemoteWindowPreparationResponse response) =
                        await preparing.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Equal(RemoteWindowPreparationOutcome.Rejected, response.Outcome);
                    Assert.Equal("preparation_cancelled", response.ReasonCode);
                    await disconnecting.WaitAsync(TimeSpan.FromSeconds(5));
                    await stopping.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.True(renderer.IsDisposed);
                    AuthenticatedRemoteWindowConnectionLease lease =
                        Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(
                            participantLease);
                    Assert.False(lease.IsCurrent);
                    Task connectionClosed = lease.WaitForConnectionClosedAsync().AsTask();
                    Assert.True(connectionClosed.IsCompletedSuccessfully);
                }
                finally
                {
                    factory.ReleaseCancellation();
                    factory.ReleasePreparation();
                    renderer.ReleaseDisposal();
                    Task finalStop = context.PreparationPeer.StopReceivingAsync().AsTask();
                    await Task.WhenAll(
                        preparing.WaitAsync(TimeSpan.FromSeconds(5)),
                        disconnecting.WaitAsync(TimeSpan.FromSeconds(5)),
                        factory.CallbackStop.WaitAsync(TimeSpan.FromSeconds(5)),
                        finalStop.WaitAsync(TimeSpan.FromSeconds(5)));
                }
            },
            decorateConnectionAcquirer: acquire =>
                (DeviceId peerDeviceId,
                    out AuthenticatedRemoteWindowConnectionLease? lease) =>
                {
                    bool acquired = acquire(peerDeviceId, out lease);
                    participantLease = lease;
                    return acquired;
                });
    }

    private sealed class DelayedPreparationChildStopFactory(
        IDesktopRemoteWindowParticipantRenderer renderer) :
        IDesktopRemoteWindowParticipantRendererFactory
    {
        private readonly TaskCompletionSource releaseChildStop = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ChildStop { get; private set; } = Task.CompletedTask;

        public DesktopRemoteWindowPreparationPeer? PreparationPeer { get; set; }

        public TaskCompletionSource<Task> StopReturned { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<IDesktopRemoteWindowParticipantRenderer?> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChildStop = Task.Run(
                async () =>
                {
                    await releaseChildStop.Task.ConfigureAwait(false);
                    Task stopping = PreparationPeer!.StopReceivingAsync().AsTask();
                    StopReturned.TrySetResult(stopping);
                    await stopping.ConfigureAwait(false);
                },
                CancellationToken.None);
            return ValueTask.FromResult<
                IDesktopRemoteWindowParticipantRenderer?>(renderer);
        }

        public void ReleaseChildStop() => releaseChildStop.TrySetResult();
    }

    private sealed class StopInsidePreparationFactory(
        IDesktopRemoteWindowParticipantRenderer renderer) :
        IDesktopRemoteWindowParticipantRendererFactory,
        IDisposable
    {
        private readonly ManualResetEventSlim releaseCancellation = new(false);
        private readonly TaskCompletionSource releasePreparation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration cancellationRegistration;

        public DesktopRemoteWindowPreparationPeer? PreparationPeer { get; set; }

        public Task CallbackStop { get; private set; } = Task.CompletedTask;

        public TaskCompletionSource CancellationObserved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> StopReturned { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IDesktopRemoteWindowParticipantRenderer?> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Returning the late renderer must not dispose a registration whose
            // callback deliberately holds the independent stop worker.
            cancellationRegistration = cancellationToken.UnsafeRegister(
                static state => ((StopInsidePreparationFactory)state!).ObserveCancellation(),
                this);
            CallbackStop = PreparationPeer!.StopReceivingAsync().AsTask();
            StopReturned.TrySetResult(CallbackStop.IsCompletedSuccessfully);
            await releasePreparation.Task.ConfigureAwait(false);
            return renderer;
        }

        public void ReleasePreparation() => releasePreparation.TrySetResult();

        public void ReleaseCancellation() => releaseCancellation.Set();

        public void Dispose()
        {
            ReleaseCancellation();
            ReleasePreparation();
            cancellationRegistration.Dispose();
            releaseCancellation.Dispose();
        }

        private void ObserveCancellation()
        {
            CancellationObserved.TrySetResult();
            releaseCancellation.Wait();
        }
    }

    private sealed class ParticipantStopBlockingRenderer :
        IDesktopRemoteWindowParticipantRenderer
    {
        private readonly TaskCompletionSource releaseDisposal = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int disposed;

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public TaskCompletionSource DisposeEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeAsync()
        {
            DisposeEntered.TrySetResult();
            await releaseDisposal.Task.ConfigureAwait(false);
            Volatile.Write(ref disposed, 1);
        }

        public ValueTask RenderAsync(
            DesktopRemoteWindowBgraFrame frame,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException(
                "The stopping participant must not render a frame."));

        public void ReleaseDisposal() => releaseDisposal.TrySetResult();
    }
}
