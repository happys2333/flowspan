using static Flowspan.Desktop.DesktopRemoteWindowHostCoordinator;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopRemoteWindowCleanupConfirmationTests
{
    [Fact]
    public Task ProviderFailureDoesNotWaitForAnInFlightTimeoutCommit() =>
        StartIndependentWorker(VerifyProviderFailureDoesNotWaitForAnInFlightTimeoutCommit);

    private static void VerifyProviderFailureDoesNotWaitForAnInFlightTimeoutCommit()
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
        Task starting = StartIndependentWorker(operation.Start);
        try
        {
            WaitForCompletion(starting);
            Assert.True(commitEntered.IsSet);
            Assert.False(operation.Completion.IsCompleted);
            Assert.Same(providerFailure, setupDiagnostic);

            // The provider completed real cleanup before Start installed the
            // observer, so Start can return only after it reaches the commit wait.
            Assert.True(realCleanup.Task.IsCompleted);
            Assert.False(realCommitted.Task.IsCompleted);
            releaseCommit.Set();

            CleanupConfirmationResult outcome = WaitForCompletion(operation.Completion);
            Assert.Equal(CleanupConfirmationStatus.Timeout, outcome.Status);
            Assert.Contains("host_cleanup_timeout", Assert.IsType<InvalidOperationException>(outcome.Failure).Message);
            Assert.Same(ownerFailure, WaitForCompletion(realCommitted.Task));
            Assert.Same(outcome, WaitForCompletion(operation.Completion));
            WaitForCompletion(time.CallbackTask);
        }
        finally
        {
            releaseCommit.Set();
            realCleanup.TrySetResult(null);
            try
            {
                WaitForCompletion(starting);
            }
            finally
            {
                // Start owns callback publication; read the latest task only
                // after attempting its join, even when that join throws.
                WaitForCompletion(time.CallbackTask);
            }
        }
    }

    // Each synchronous blocker owns a thread instead of requiring another
    // shared-pool worker to start before it can release its current worker.
    private static Task StartIndependentWorker(Action action) =>
        Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static void WaitForCompletion(Task task)
    {
        try
        {
            if (!task.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException(
                    "The test worker did not complete within its original five-second bound.");
            }
        }
        catch (AggregateException) when (task.IsFaulted || task.IsCanceled)
        {
            // Keep the same original exception identity as the former await.
            task.GetAwaiter().GetResult();
        }

        task.GetAwaiter().GetResult();
    }

    private static TResult WaitForCompletion<TResult>(Task<TResult> task)
    {
        WaitForCompletion((Task)task);
        return task.GetAwaiter().GetResult();
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
            CallbackTask = StartIndependentWorker(() => callback(state));
            if (!commitEntered.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The timeout callback did not reach its commit boundary.");
            }

            beforeThrow();
            throw failure;
        }
    }
}
