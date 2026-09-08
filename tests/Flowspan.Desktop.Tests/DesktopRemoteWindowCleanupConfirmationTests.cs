using static Flowspan.Desktop.DesktopRemoteWindowHostCoordinator;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopRemoteWindowCleanupConfirmationTests
{
    [Fact]
    public async Task ProviderFailureDoesNotWaitForAnInFlightTimeoutCommit()
    {
        using var commitEntered = new ManualResetEventSlim();
        using var releaseCommit = new ManualResetEventSlim();
        var realCleanup = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var realCommitted = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var providerFailure = new IOException("provider failed after cross-thread callback");
        var ownerFailure = new IOException("late owner failure");
        var time = new ConcurrentFailingTimeProvider(
            commitEntered,
            providerFailure,
            () => realCleanup.SetResult(ownerFailure));
        Exception? setupDiagnostic = null;
        var operation = new CleanupConfirmationOperation(
            realCleanup.Task,
            time,
            TimeSpan.FromSeconds(10),
            _ =>
            {
                commitEntered.Set();
                releaseCommit.Wait();
            },
            failure => realCommitted.TrySetResult(failure),
            failure => setupDiagnostic = failure,
            _ => throw new InvalidOperationException("No timer was returned to release."));
        Task starting = Task.Run(operation.Start);
        try
        {
            await starting.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(commitEntered.IsSet);
            Assert.False(operation.Completion.IsCompleted);
            Assert.Same(providerFailure, setupDiagnostic);

            // The provider completed real cleanup before Start installed the
            // observer, so Start can return only after it reaches the commit wait.
            Assert.True(realCleanup.Task.IsCompleted);
            Assert.False(realCommitted.Task.IsCompleted);
            releaseCommit.Set();

            CleanupConfirmationResult outcome = await operation.Completion
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(CleanupConfirmationStatus.Timeout, outcome.Status);
            Assert.Contains("host_cleanup_timeout", Assert.IsType<InvalidOperationException>(outcome.Failure).Message);
            Assert.Same(ownerFailure, await realCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(outcome, await operation.Completion);
            await time.CallbackTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseCommit.Set();
            realCleanup.TrySetResult(null);
            await starting.WaitAsync(TimeSpan.FromSeconds(5));
            await time.CallbackTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class ConcurrentFailingTimeProvider(
        ManualResetEventSlim commitEntered,
        Exception failure,
        Action beforeThrow) : TimeProvider
    {
        public Task CallbackTask { get; private set; } = Task.CompletedTask;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            CallbackTask = Task.Run(() => callback(state));
            if (!commitEntered.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The timeout callback did not reach its commit boundary.");
            }

            beforeThrow();
            throw failure;
        }
    }
}
