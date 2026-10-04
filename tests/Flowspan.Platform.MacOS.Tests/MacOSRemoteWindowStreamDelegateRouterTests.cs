using System.Diagnostics.CodeAnalysis;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSRemoteWindowStreamDelegateRouterTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    [Fact]
    public void TerminalWithoutSampleClosesAdmissionBeforeOnceOnlyNotification()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter();
        Assert.True(router.TryReserve(out var candidate));
        var registration = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(candidate);
        Assert.True(registration.MarkDelegatePublished());
        Assert.True(registration.ConfirmAssociation());
        int unavailable = 0;
        Assert.True(registration.Activate(() =>
        {
            Assert.True(registration.AdmissionClosed);
            unavailable++;
        }));

        router.Dispatch(registration.Generation, MacOSRemoteWindowDelegateSignal.StoppedWithError);
        router.Dispatch(registration.Generation, MacOSRemoteWindowDelegateSignal.Inactive);
        router.Dispatch(registration.Generation, MacOSRemoteWindowDelegateSignal.Active);

        Assert.True(registration.AdmissionClosed);
        Assert.Equal(1, unavailable);
    }

    [Fact]
    public void TerminalBeforeAssociationIsDeliveredOnceAfterActivationWithoutReopening()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter();
        Assert.True(router.TryReserve(out var candidate));
        var registration = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(candidate);
        Assert.True(registration.MarkDelegatePublished());
        router.Dispatch(registration.Generation, MacOSRemoteWindowDelegateSignal.Inactive);
        router.Dispatch(registration.Generation, MacOSRemoteWindowDelegateSignal.StoppedWithError);
        Assert.True(registration.AdmissionClosed);
        Assert.True(registration.ConfirmAssociation());
        int unavailable = 0;
        Assert.True(registration.Activate(() => unavailable++));

        router.Dispatch(registration.Generation, MacOSRemoteWindowDelegateSignal.Active);

        Assert.True(registration.AdmissionClosed);
        Assert.Equal(1, unavailable);
        Assert.False(registration.Activate(() => unavailable++));
    }

    [Fact]
    public void InitializerContentionRejectsUntilImmutableAssociationIsConfirmed()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter(2);
        Assert.True(router.TryReserve(out var candidate));
        var first = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(candidate);
        Assert.False(router.TryReserve(out var busy));
        Assert.Null(busy);
        Assert.False(first.ConfirmAssociation());
        Assert.True(first.MarkDelegatePublished());
        Assert.False(first.MarkDelegatePublished());
        Assert.False(router.TryReserve(out busy));
        Assert.True(first.ConfirmAssociation());
        Assert.False(first.ConfirmAssociation());
        Assert.True(router.TryReserve(out var replacement));
        Assert.NotNull(replacement);
        Assert.False(first.ConfirmAssociation());
        Assert.False(router.TryReserve(out busy));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializationFailureRetainsPermitAndPoisonsOnlyPublishedAmbiguity(bool published)
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter(2);
        Assert.True(router.TryReserve(out var candidate));
        var failed = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(candidate);
        if (published)
        {
            Assert.True(failed.MarkDelegatePublished());
        }

        Assert.True(failed.FailInitialization());
        Assert.True(failed.AdmissionClosed);
        Assert.False(failed.ConfirmAssociation());
        Assert.False(failed.MarkDelegatePublished());
        Assert.False(failed.FailInitialization());
        Assert.False(failed.Activate(() => Assert.Fail("Failed initialization cannot activate.")));
        Assert.Equal(!published, router.TryReserve(out var next));
        if (!published)
        {
            var replacement = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(next);
            Assert.True(replacement.FailInitialization());
            Assert.False(router.TryReserve(out _));
        }
        else
        {
            Assert.Null(next);
        }
    }

    [Fact]
    public void GenerationExhaustionRejectsRatherThanWrappingToANonpositiveGeneration()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter(2, long.MaxValue - 1);
        Assert.True(router.TryReserve(out var candidate));
        var last = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(candidate);
        Assert.Equal(long.MaxValue, last.Generation);
        Assert.True(last.MarkDelegatePublished());
        Assert.True(last.ConfirmAssociation());

        Assert.False(router.TryReserve(out var exhausted));
        Assert.Null(exhausted);
        Assert.False(router.TryReserve(out exhausted));
        Assert.Null(exhausted);
    }

    [Fact]
    public async Task RetirementClosesAdmissionBeforeJoiningAdmittedHandler()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter();
        var registration = ReserveAssociated(router);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ManualResetEventSlim();
        Assert.True(registration.Activate(() =>
        {
            entered.TrySetResult();
            // Only the observer releases the admitted handler. A timed wait here
            // can expire while its continuation is queued and falsify the join.
            release.Wait();
            handlerExited.SetResult();
        }));
        var callback = new DedicatedCallback(() => router.Dispatch(registration.Generation, MacOSRemoteWindowDelegateSignal.Inactive));
        Task<MacOSRemoteWindowCallbackRetirement>? retirement = null;
        try
        {
            await entered.Task.WaitAsync(TestTimeout);
            retirement = registration.RetireAsync().AsTask();
            Assert.True(registration.AdmissionClosed);
            Assert.False(release.IsSet);
            Assert.False(handlerExited.Task.IsCompleted);
            Assert.False(retirement.IsCompleted);
        }
        finally
        {
            release.Set();
            callback.JoinAndDisposeGate(release);
            if (retirement is not null)
            {
                Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
                    await retirement.WaitAsync(TestTimeout));
            }
        }

        Assert.Null(registration.Failure);
        Assert.True(handlerExited.Task.IsCompletedSuccessfully);
        Assert.False(registration.Activate(() => Assert.Fail("Retired generation cannot activate.")));
    }

    [Fact]
    public async Task DirectHandlerSelfJoinIsRejectedWithoutWaiting()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter();
        var registration = ReserveAssociated(router);
        MacOSRemoteWindowCallbackRetirement? observed = null;
        Assert.True(registration.Activate(() =>
        {
            var selfJoin = registration.RetireAsync();
            if (selfJoin.IsCompletedSuccessfully)
            {
                observed = selfJoin.Result;
            }
        }));

        router.Dispatch(registration.Generation, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.Equal(MacOSRemoteWindowCallbackRetirement.SelfJoinRejected, observed);
        Assert.True(registration.AdmissionClosed);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
            await registration.RetireAsync());
    }

    [Fact]
    public async Task ActiveExecutionContextDescendantSelfJoinIsRejectedWithoutWaiting()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter();
        var registration = ReserveAssociated(router);
        MacOSRemoteWindowCallbackRetirement? observed = null;
        Assert.True(registration.Activate(() =>
        {
            observed = Task.Run(() =>
            {
                var selfJoin = registration.RetireAsync();
                return selfJoin.IsCompletedSuccessfully
                    ? (MacOSRemoteWindowCallbackRetirement?)selfJoin.Result
                    : null;
            }).GetAwaiter().GetResult();
        }));

        router.Dispatch(registration.Generation, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.Equal(MacOSRemoteWindowCallbackRetirement.SelfJoinRejected, observed);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
            await registration.RetireAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [SuppressMessage("Usage", "CA2201", Justification = "Preserves injected fatal allocation failures through reverse-entry containment.")]
    public async Task HandlerFailureIsContainedAndOriginalFatalRemainsObservable(int kind)
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter();
        var registration = ReserveAssociated(router);
        Exception original = kind == 0
            ? new InvalidOperationException("injected ordinary handler fault")
            : new OutOfMemoryException("injected fatal handler fault");
        Exception thrown = original;
        if (kind == 2)
        {
            for (int depth = 0; depth < 65; depth++)
            {
                thrown = new AggregateException(new InvalidOperationException(), thrown);
            }
        }

        Assert.True(registration.Activate(() => throw thrown));

        Assert.Null(Record.Exception(() => router.Dispatch(registration.Generation, MacOSRemoteWindowDelegateSignal.Inactive)));

        Assert.True(registration.AdmissionClosed);
        Assert.Same(original, registration.Failure);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
            await registration.RetireAsync());
    }

    [Fact]
    public async Task CompleteCleanupReturnsPermitExactlyOnceWithoutReusingGeneration()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter(1);
        var first = ReserveAssociated(router);
        Assert.False(first.ConfirmCompleteCleanup());
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited, await first.RetireAsync());
        Assert.False(router.TryReserve(out _));
        Assert.True(first.ConfirmCompleteCleanup());
        Assert.False(first.ConfirmCompleteCleanup());
        Assert.True(router.TryReserve(out var candidate));
        var next = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(candidate);
        Assert.Equal(first.Generation + 1, next.Generation);
        Assert.False(first.MarkDelegatePublished());
        Assert.False(first.ConfirmAssociation());
        Assert.False(first.FailInitialization());
        Assert.False(first.ConfirmCompleteCleanup());
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited, await first.RetireAsync());
        Assert.False(router.TryReserve(out _));
        Assert.True(next.MarkDelegatePublished());
        Assert.True(next.ConfirmAssociation());
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited, await next.RetireAsync());
        Assert.True(next.ConfirmCompleteCleanup());
        var third = ReserveAssociated(router);
        Assert.Equal(next.Generation + 1, third.Generation);
        await third.RetireAsync();
        Assert.True(third.ConfirmCompleteCleanup());
    }

    [Fact]
    public async Task QuarantinedRegistrationPermanentlyRetainsPermitAfterManagedRetirement()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter(1);
        var registration = ReserveAssociated(router);
        registration.Quarantine();
        Assert.True(registration.AdmissionClosed);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
            await registration.RetireAsync());

        Assert.False(registration.ConfirmCompleteCleanup());
        Assert.False(router.TryReserve(out _));
        registration.Quarantine();
        Assert.False(registration.ConfirmCompleteCleanup());
        Assert.False(registration.Activate(() => Assert.Fail("Quarantine cannot activate.")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetiringUnconfirmedInitializationPreservesPublicationPoisonAfterCleanup(bool published)
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter(2);
        Assert.True(router.TryReserve(out var candidate));
        var retiring = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(candidate);
        if (published)
        {
            Assert.True(retiring.MarkDelegatePublished());
        }

        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited, await retiring.RetireAsync());
        Assert.True(retiring.AdmissionClosed);
        Assert.True(retiring.ConfirmCompleteCleanup());
        Assert.False(retiring.ConfirmAssociation());
        Assert.False(retiring.FailInitialization());
        Assert.Equal(!published, router.TryReserve(out var next));
        if (!published)
        {
            var replacement = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(next);
            Assert.False(retiring.FailInitialization());
            Assert.True(replacement.MarkDelegatePublished());
            Assert.True(replacement.ConfirmAssociation());
            await replacement.RetireAsync();
            Assert.True(replacement.ConfirmCompleteCleanup());
            Assert.True(router.TryReserve(out _));
        }
        else
        {
            Assert.Null(next);
        }
    }

    [Fact]
    public async Task UnknownRetiredUnpublishedAndActiveSignalsCannotAffectCurrentGeneration()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter(2);
        var retired = ReserveAssociated(router);
        int retiredNotifications = 0;
        Assert.True(retired.Activate(() => retiredNotifications++));
        await retired.RetireAsync();
        Assert.True(retired.ConfirmCompleteCleanup());
        Assert.True(router.TryReserve(out var candidate));
        var current = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(candidate);
        router.Dispatch(retired.Generation, MacOSRemoteWindowDelegateSignal.Inactive);
        router.Dispatch(0, MacOSRemoteWindowDelegateSignal.Inactive);
        router.Dispatch(-1, MacOSRemoteWindowDelegateSignal.Inactive);
        router.Dispatch(long.MaxValue, MacOSRemoteWindowDelegateSignal.StoppedWithError);
        router.Dispatch(current.Generation, MacOSRemoteWindowDelegateSignal.Active);
        router.Dispatch(current.Generation, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.False(current.AdmissionClosed);
        Assert.Equal(0, retiredNotifications);
        Assert.True(current.MarkDelegatePublished());
        Assert.True(current.ConfirmAssociation());
        int unavailable = 0;
        Assert.True(current.Activate(() => unavailable++));
        router.Dispatch(current.Generation, (MacOSRemoteWindowDelegateSignal)999);
        router.Dispatch(current.Generation, MacOSRemoteWindowDelegateSignal.Active);
        Assert.False(current.AdmissionClosed);
        Assert.Equal(0, unavailable);
        router.Dispatch(current.Generation, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.True(current.AdmissionClosed);
        Assert.Equal(1, unavailable);
        await current.RetireAsync();
        Assert.True(current.ConfirmCompleteCleanup());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(16)]
    public async Task ConstructingActiveRetiredAndQuarantinedRegistrationsShareBoundedCaptureBudget(int capacity)
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter(capacity);
        var registrations = new List<MacOSRemoteWindowStreamDelegateRouter.Registration>();
        for (int index = 0; index < capacity; index++)
        {
            var registration = ReserveAssociated(router);
            Assert.Equal(index + 1, registration.Generation);
            Assert.True(registration.Activate(() => Assert.Fail("No terminal was sent.")));
            registrations.Add(registration);
        }

        Assert.False(router.TryReserve(out _));
        foreach (var registration in registrations)
        {
            await registration.RetireAsync();
        }

        Assert.False(router.TryReserve(out _));
        Assert.True(registrations[0].ConfirmCompleteCleanup());
        var replacement = ReserveAssociated(router);
        Assert.Equal(capacity + 1, replacement.Generation);
        Assert.False(router.TryReserve(out _));
        replacement.Quarantine();
        await replacement.RetireAsync();
        Assert.False(replacement.ConfirmCompleteCleanup());
        foreach (var registration in registrations.Skip(1))
        {
            registration.Quarantine();
            Assert.False(registration.ConfirmCompleteCleanup());
        }

        Assert.False(router.TryReserve(out _));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(17)]
    public void InvalidCaptureBudgetCannotConstructRouter(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MacOSRemoteWindowStreamDelegateRouter(capacity));
    }

    [Fact]
    public void NegativeGenerationSeedCannotConstructRouter()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MacOSRemoteWindowStreamDelegateRouter(1, -1));
    }

    [Fact]
    public async Task ContendingInitializersFailFastWithoutConsumingGenerations()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter(2);
        Assert.True(router.TryReserve(out var candidate));
        var first = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(candidate);
        Task<bool>[] attempts = Enumerable.Range(0, 64)
            .Select(index => Task.Run(() => router.TryReserve(out _)))
            .ToArray();
        bool[] accepted = await Task.WhenAll(attempts).WaitAsync(TestTimeout);
        Assert.All(accepted, Assert.False);
        Assert.True(first.MarkDelegatePublished());
        Assert.True(first.ConfirmAssociation());
        var second = ReserveAssociated(router);
        Assert.Equal(first.Generation + 1, second.Generation);
        await first.RetireAsync();
        await second.RetireAsync();
        Assert.True(first.ConfirmCompleteCleanup());
        Assert.True(second.ConfirmCompleteCleanup());
    }

    [Fact]
    public async Task CapturedExecutionContextAfterHandlerExitMayRetireNormally()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter();
        var registration = ReserveAssociated(router);
        var exitObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<MacOSRemoteWindowCallbackRetirement>? descendant = null;
        Assert.True(registration.Activate(() => descendant = Task.Run(async () =>
        {
            await exitObserved.Task;
            return await registration.RetireAsync();
        })));
        router.Dispatch(registration.Generation, MacOSRemoteWindowDelegateSignal.Inactive);
        exitObserved.SetResult();
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
            await Assert.IsAssignableFrom<Task<MacOSRemoteWindowCallbackRetirement>>(descendant).WaitAsync(TestTimeout));
        Assert.True(registration.ConfirmCompleteCleanup());
    }

    [Fact]
    public async Task HandlerCanReserveNestedGenerationWithoutRoutingGateOrAncestryLoss()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter(2);
        var first = ReserveAssociated(router);
        MacOSRemoteWindowStreamDelegateRouter.Registration? nested = null;
        MacOSRemoteWindowCallbackRetirement? firstSelfJoin = null;
        int nestedNotifications = 0;
        Assert.True(first.Activate(() =>
        {
            nested = Task.Run(() => ReserveAssociated(router)).WaitAsync(TestTimeout).GetAwaiter().GetResult();
            Assert.True(nested.Activate(() =>
            {
                nestedNotifications++;
                var selfJoin = first.RetireAsync();
                if (selfJoin.IsCompletedSuccessfully)
                {
                    firstSelfJoin = selfJoin.Result;
                }
            }));
            router.Dispatch(nested.Generation, MacOSRemoteWindowDelegateSignal.Inactive);
            var nestedRetirement = nested.RetireAsync();
            if (nestedRetirement.IsCompletedSuccessfully)
            {
                Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited, nestedRetirement.Result);
            }
            else
            {
                Assert.Fail("The nested managed handler has already exited.");
            }
        }));

        router.Dispatch(first.Generation, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.Null(first.Failure);
        Assert.Equal(1, nestedNotifications);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.SelfJoinRejected, firstSelfJoin);
        await first.RetireAsync();
        Assert.True(first.ConfirmCompleteCleanup());
        Assert.True(Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(nested).ConfirmCompleteCleanup());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuarantiningInitializerRetainsPermitAndPoisonsOnlyPublishedAmbiguity(bool published)
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter(2);
        Assert.True(router.TryReserve(out var candidate));
        var registration = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(candidate);
        if (published)
        {
            Assert.True(registration.MarkDelegatePublished());
        }

        registration.Quarantine();
        Assert.False(registration.MarkDelegatePublished());
        Assert.False(registration.ConfirmAssociation());
        await registration.RetireAsync();
        Assert.False(registration.ConfirmCompleteCleanup());
        Assert.Equal(!published, router.TryReserve(out var next));
        if (!published)
        {
            var replacement = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(next);
            Assert.True(replacement.FailInitialization());
            Assert.False(router.TryReserve(out _));
        }
    }

    [Fact]
    public async Task UnpublishedFailureNeedsRetirementAndExplicitCleanupBeforePermitReuse()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter(1);
        Assert.True(router.TryReserve(out var candidate));
        var registration = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(candidate);
        Assert.True(registration.FailInitialization());
        Assert.False(registration.ConfirmCompleteCleanup());
        Assert.False(router.TryReserve(out _));
        await registration.RetireAsync();
        Assert.False(router.TryReserve(out _));
        Assert.True(registration.ConfirmCompleteCleanup());
        var replacement = ReserveAssociated(router);
        Assert.Equal(registration.Generation + 1, replacement.Generation);
        await replacement.RetireAsync();
        Assert.True(replacement.ConfirmCompleteCleanup());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Usage", "CA2201", Justification = "Exercises injected early notification allocation failure.")]
    public async Task EarlyTerminalActivationContainsHandlerFault(bool fatal)
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter();
        Assert.True(router.TryReserve(out var candidate));
        var registration = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(candidate);
        Assert.True(registration.MarkDelegatePublished());
        router.Dispatch(registration.Generation, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.True(registration.ConfirmAssociation());
        Exception failure = fatal ? new OutOfMemoryException("injected early fatal") : new InvalidOperationException();
        Assert.Null(Record.Exception(() => Assert.True(registration.Activate(() => throw failure))));
        Assert.Same(failure, registration.Failure);
        Assert.True(registration.AdmissionClosed);
        await registration.RetireAsync();
        Assert.True(registration.ConfirmCompleteCleanup());
    }

    [Fact]
    public async Task ConcurrentDuplicateTerminalsAndRetirementJoinOneManagedNotification()
    {
        var router = new MacOSRemoteWindowStreamDelegateRouter(1);
        for (int cycle = 0; cycle < 32; cycle++)
        {
            var registration = ReserveAssociated(router);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new ManualResetEventSlim();
            int notifications = 0;
            Assert.True(registration.Activate(() =>
            {
                Interlocked.Increment(ref notifications);
                entered.TrySetResult();
                release.Wait();
            }));
            var callback = new DedicatedCallback(() => router.Dispatch(registration.Generation, MacOSRemoteWindowDelegateSignal.Inactive));
            Task<MacOSRemoteWindowCallbackRetirement>[] retirementTasks = [];
            try
            {
                await entered.Task.WaitAsync(TestTimeout);
                Task[] duplicates = Enumerable.Range(0, 16)
                    .Select(index => Task.Run(() => router.Dispatch(registration.Generation,
                        index % 2 == 0 ? MacOSRemoteWindowDelegateSignal.StoppedWithError : MacOSRemoteWindowDelegateSignal.Inactive)))
                    .ToArray();
                retirementTasks = Enumerable.Range(0, 8)
                    .Select(_ => registration.RetireAsync().AsTask())
                    .ToArray();
                await Task.WhenAll(duplicates).WaitAsync(TestTimeout);
                Assert.All(retirementTasks, task => Assert.False(task.IsCompleted));
                Assert.False(registration.ConfirmCompleteCleanup());
                Assert.False(router.TryReserve(out _));
            }
            finally
            {
                release.Set();
                callback.JoinAndDisposeGate(release);
                var results = await Task.WhenAll(retirementTasks).WaitAsync(TestTimeout);
                Assert.All(results, result => Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited, result));
            }

            Assert.Null(registration.Failure);
            Assert.Equal(1, notifications);
            Assert.True(registration.ConfirmCompleteCleanup());
            Assert.False(registration.ConfirmCompleteCleanup());
        }
    }

    private static MacOSRemoteWindowStreamDelegateRouter.Registration ReserveAssociated(
        MacOSRemoteWindowStreamDelegateRouter router)
    {
        Assert.True(router.TryReserve(out var candidate));
        var registration = Assert.IsType<MacOSRemoteWindowStreamDelegateRouter.Registration>(candidate);
        Assert.True(registration.MarkDelegatePublished());
        Assert.True(registration.ConfirmAssociation());
        return registration;
    }

    // Blocking a test-owned thread leaves pool capacity for the async observer
    // and duplicate dispatches. Every caller releases its gate in finally.
    private sealed class DedicatedCallback
    {
        private readonly Thread thread;
        private Exception? failure;

        internal DedicatedCallback(Action callback)
        {
            thread = new Thread(() =>
            {
                try
                {
                    callback();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            })
            { IsBackground = true };
            thread.Start();
        }

        internal void JoinAndDisposeGate(ManualResetEventSlim release)
        {
            bool exited = thread.Join(TestTimeout);
            if (exited)
            {
                release.Dispose();
            }

            // If exit is unconfirmed, retain the already-signaled gate rather
            // than make the surviving background thread use a disposed one.
            Assert.True(exited, "The explicitly released callback thread did not exit.");
            Assert.Null(failure);
        }
    }
}
