using System.Collections.Concurrent;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopPairingDecisionSourceTests
{
    [Fact]
    public async Task DisposeWaitsForActiveCancellationPublication()
    {
        Task<PairingDecision> decision = await RunOnDedicatedThread(
            ExerciseDisposalWithActivePublication);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => decision);
    }

    private static Task<PairingDecision> ExerciseDisposalWithActivePublication()
    {
        using DeviceIdentity peer = CreatePeer("Peer desk");
        using var cancellation = new CancellationTokenSource();
        using var releasePublication = new ManualResetEventSlim();
        using var publicationEntered = new ManualResetEventSlim();
        using var disposalEntered = new ManualResetEventSlim();
        Task publication = Task.CompletedTask;
        using var source = new DesktopPairingDecisionSource(
            publish => publication = RunOnDedicatedThread(publish));
        source.PromptChanged += OnPromptChanged;
        Task<PairingDecision> decision = source.DecideAsync(
            CreateRequest(peer, "111111"),
            cancellation.Token).AsTask();

        Task? disposing = null;
        Task? observeDisposal = null;
        Thread? disposingThread = null;
        int returnedBeforePublicationReleased = 0;
        try
        {
            cancellation.Cancel();
            WaitForStage(publicationEntered, "cancellation publication entry");
            disposing = RunOnDedicatedThread(() =>
            {
                Volatile.Write(ref disposingThread, Thread.CurrentThread);
                disposalEntered.Set();
                source.Dispose();
            });
            observeDisposal = disposing.ContinueWith(
                _ =>
                {
                    if (!releasePublication.IsSet)
                    {
                        Interlocked.Exchange(
                            ref returnedBeforePublicationReleased,
                            1);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            WaitForStage(disposalEntered, "disposal worker entry");
            Assert.True(SpinWait.SpinUntil(
                () => disposing.IsCompleted
                    || (Volatile.Read(ref disposingThread)!.ThreadState
                        & ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5)));
            Assert.False(disposing.IsCompleted);
            Assert.False(releasePublication.IsSet);
        }
        finally
        {
            releasePublication.Set();
            source.Dispose();
            CompleteOwnedWork(publication);
            if (disposing is not null)
            {
                CompleteOwnedWork(disposing);
            }

            if (observeDisposal is not null)
            {
                CompleteOwnedWork(observeDisposal);
            }
        }

        Assert.Equal(0, Volatile.Read(ref returnedBeforePublicationReleased));

        return decision;

        void OnPromptChanged(
            object? sender,
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            if (eventArgs.Kind == DesktopPairingPromptChangeKind.Canceled)
            {
                publicationEntered.Set();
                releasePublication.Wait();
            }
        }
    }

    [Fact]
    public async Task CancellationCoalescingRetainsTheHighestAllocatedSequence()
    {
        CancellationExercise exercise = await RunOnDedicatedThread(
            () => ExerciseCancellationCoalescing());

        foreach (Task<PairingDecision> decision in exercise.Decisions)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => decision);
        }

        Assert.Contains(
            exercise.Observed,
            static change => change.Kind == DesktopPairingPromptChangeKind.Canceled
                && change.Sequence == 4);
    }

    [Theory]
    [InlineData("before-worker-capture")]
    [InlineData("before-publication")]
    [InlineData("cancellation-join")]
    public async Task CancellationCoalescingFixtureDrainsPublicationAfterInjectedFailure(
        string failurePoint)
    {
        var capturedWorker = new TaskCompletionSource<Action>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var injectedFailureReached = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int publicationRuns = 0;
        int publicationAttempts = 0;
        int publicationStarted = 0;
        Task<CancellationExercise> exercise = RunOnDedicatedThread(
                () => ExerciseCancellationCoalescing(
                    publish =>
                    {
                        Action runOnce = () =>
                        {
                            if (Interlocked.CompareExchange(
                                    ref publicationStarted,
                                    1,
                                    0) == 0)
                            {
                                Interlocked.Increment(ref publicationRuns);
                                publish();
                            }
                        };
                        capturedWorker.TrySetResult(runOnce);
                        return () =>
                        {
                            Interlocked.Increment(ref publicationAttempts);
                            runOnce();
                        };
                    },
                    failurePoint == "before-publication" ? ThrowInjectedFailure : null,
                    failurePoint == "before-worker-capture" ? ThrowInjectedFailure : null,
                    failurePoint == "cancellation-join" ? ThrowInjectedFailure : null));
        try
        {
            await capturedWorker.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await injectedFailureReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

            if (failurePoint == "cancellation-join")
            {
                AggregateException exception = await Assert.ThrowsAsync<AggregateException>(
                    () => exercise.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.IsType<InjectedFixtureFailureException>(
                    Assert.Single(exception.InnerExceptions));
            }
            else
            {
                await Assert.ThrowsAsync<InjectedFixtureFailureException>(
                    () => exercise.WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }
        finally
        {
            // Rescue the deliberately broken fixture during RED verification.
            // A passing fixture has already drained its worker exactly once.
            if (capturedWorker.Task.IsCompletedSuccessfully)
            {
                (await capturedWorker.Task)();
            }

            try
            {
                await exercise.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (InjectedFixtureFailureException)
            {
            }
            catch (AggregateException exception) when (
                exception.InnerExceptions.Count == 1
                && exception.InnerException is InjectedFixtureFailureException)
            {
            }
        }

        Assert.Equal(1, Volatile.Read(ref publicationRuns));
        Assert.Equal(1, Volatile.Read(ref publicationAttempts));

        void ThrowInjectedFailure()
        {
            injectedFailureReached.TrySetResult();
            throw new InjectedFixtureFailureException();
        }
    }

    private static CancellationExercise ExerciseCancellationCoalescing(
            Func<Action, Action>? decoratePublication = null,
            Action? beforePublication = null,
            Action? beforeWorkerCapture = null,
            Action? afterFirstQueueReleased = null)
    {
        using DeviceIdentity peer = CreatePeer("Peer desk");
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        using var releaseFirstQueue = new ManualResetEventSlim();
        using var firstQueueReached = new ManualResetEventSlim();
        using var workerCaptured = new ManualResetEventSlim();
        var publicationGate = new Lock();
        Action? pendingPublication = null;
        bool publicationReleased = false;
        var observed = new ConcurrentQueue<DesktopPairingPromptChangedEventArgs>();
        int firstCancellationPaused = 0;
        using var source = new DesktopPairingDecisionSource(
            SchedulePublication,
            BeforeCancellationChangeQueued);
        source.PromptChanged += (_, eventArgs) => observed.Enqueue(eventArgs);

        Task<PairingDecision> first = source.DecideAsync(
            CreateRequest(peer, "111111"),
            firstCancellation.Token).AsTask();
        Task cancelFirst = RunOnDedicatedThread(firstCancellation.Cancel);
        Task<PairingDecision> second;
        try
        {
            WaitForStage(firstQueueReached, "first cancellation queue entry");
            beforeWorkerCapture?.Invoke();
            second = source.DecideAsync(
                CreateRequest(peer, "222222"),
                secondCancellation.Token).AsTask();
            secondCancellation.Cancel();
            WaitForStage(workerCaptured, "cancellation publication capture");
            beforePublication?.Invoke();
        }
        finally
        {
            releaseFirstQueue.Set();
            try
            {
                CompleteOwnedWork(cancelFirst);
            }
            finally
            {
                Action? publication;
                lock (publicationGate)
                {
                    publicationReleased = true;
                    publication = pendingPublication;
                    pendingPublication = null;
                }

                publication?.Invoke();
            }
        }

        return new CancellationExercise(observed, [first, second]);

        void SchedulePublication(Action publish)
        {
            Action publication = decoratePublication?.Invoke(publish) ?? publish;
            bool publishNow;
            lock (publicationGate)
            {
                publishNow = publicationReleased;
                if (!publishNow)
                {
                    pendingPublication = publication;
                }
            }

            workerCaptured.Set();
            if (publishNow)
            {
                publication();
            }
        }

        void BeforeCancellationChangeQueued(
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            if (eventArgs.Kind != DesktopPairingPromptChangeKind.Canceled
                || Interlocked.CompareExchange(
                    ref firstCancellationPaused,
                    1,
                    0) != 0)
            {
                return;
            }

            firstQueueReached.Set();
            releaseFirstQueue.Wait();
            afterFirstQueueReleased?.Invoke();
        }
    }

    [Fact]
    public async Task DelayedCancellationKeepsSequenceAndCoalescesToLatestChange()
    {
        CancellationExercise exercise = await RunOnDedicatedThread(
            ExerciseDelayedCancellation);
        foreach (Task<PairingDecision> decision in exercise.Decisions)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => decision);
        }

        Assert.Equal(
            [
                (DesktopPairingPromptChangeKind.Opened, 1L),
                (DesktopPairingPromptChangeKind.Opened, 3L),
                (DesktopPairingPromptChangeKind.Opened, 5L),
                (DesktopPairingPromptChangeKind.Canceled, 2L),
                (DesktopPairingPromptChangeKind.Canceled, 6L),
            ],
            exercise.Observed.Select(static change => (change.Kind, change.Sequence)));
    }

    private static CancellationExercise ExerciseDelayedCancellation()
    {
        using DeviceIdentity peer = CreatePeer("Peer desk");
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        using var thirdCancellation = new CancellationTokenSource();
        using var releaseFirstCancellation = new ManualResetEventSlim();
        using var firstCancellationEntered = new ManualResetEventSlim();
        using var latestCancellationPublished = new ManualResetEventSlim();
        var publicationWorkers = new ConcurrentQueue<Task>();
        using var source = new DesktopPairingDecisionSource(
            publish => publicationWorkers.Enqueue(RunOnDedicatedThread(publish)));
        var observed = new ConcurrentQueue<DesktopPairingPromptChangedEventArgs>();
        long firstCanceledSequence = 0;
        int blockedCancellation = 0;
        source.PromptChanged += BlockFirstCancellation;
        source.PromptChanged += RecordChange;

        Task<PairingDecision> first = source.DecideAsync(
            CreateRequest(peer, "111111"),
            firstCancellation.Token).AsTask();
        Task<PairingDecision> second;
        Task<PairingDecision> third;
        try
        {
            firstCancellation.Cancel();
            WaitForStage(firstCancellationEntered, "first cancellation publication entry");

            second = source.DecideAsync(
                CreateRequest(peer, "222222"),
                secondCancellation.Token).AsTask();
            secondCancellation.Cancel();
            third = source.DecideAsync(
                CreateRequest(peer, "333333"),
                thirdCancellation.Token).AsTask();
            thirdCancellation.Cancel();

            releaseFirstCancellation.Set();
            WaitForStage(latestCancellationPublished, "latest cancellation publication");
        }
        finally
        {
            releaseFirstCancellation.Set();
            source.Dispose();
            foreach (Task worker in publicationWorkers)
            {
                CompleteOwnedWork(worker);
            }
        }

        return new CancellationExercise(observed, [first, second, third]);

        void BlockFirstCancellation(
            object? sender,
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            if (eventArgs.Kind != DesktopPairingPromptChangeKind.Canceled
                || Interlocked.CompareExchange(ref blockedCancellation, 1, 0) != 0)
            {
                return;
            }

            Volatile.Write(ref firstCanceledSequence, eventArgs.Sequence);
            firstCancellationEntered.Set();
            releaseFirstCancellation.Wait();
        }

        void RecordChange(
            object? sender,
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            observed.Enqueue(eventArgs);
            if (eventArgs.Kind == DesktopPairingPromptChangeKind.Canceled
                && eventArgs.Sequence > Volatile.Read(ref firstCanceledSequence))
            {
                latestCancellationPublished.Set();
            }
        }
    }

    [Fact]
    public async Task VerifiedRequestWaitsForAndReturnsExplicitLocalGrant()
    {
        using DeviceIdentity peer = CreatePeer("Peer desk");
        using var source = new DesktopPairingDecisionSource();
        DateTimeOffset expiry = DateTimeOffset.Parse(
            "2026-07-14T02:00:00+00:00",
            System.Globalization.CultureInfo.InvariantCulture);

        Task<PairingDecision> decision = source.DecideAsync(
            new PairingConfirmationRequest(
                peer.PublicIdentity,
                new ProtocolVersion(1, 0),
                "123456",
                expiry)).AsTask();
        DesktopPairingPrompt prompt = Assert.IsType<DesktopPairingPrompt>(
            source.CurrentPrompt);

        Assert.False(decision.IsCompleted);
        Assert.Equal("Peer desk", prompt.PeerDisplayName);
        Assert.Equal(peer.DeviceId.ToString(), prompt.PeerDeviceId);
        Assert.Equal(peer.PublicIdentity.Fingerprint, prompt.PeerFingerprint);
        Assert.Equal("123456", prompt.ShortAuthenticationString);
        Assert.Equal("1.0", prompt.ProtocolVersion);
        Assert.Equal(expiry, prompt.ExpiresAt);

        Assert.True(source.TryAccept(
            prompt.PromptId,
            CapabilityGrant.Of(Capability.ActivityReceive)));

        PairingDecision accepted = await decision;
        Assert.True(accepted.Accepted);
        Assert.True(accepted.CapabilitiesGrantedToPeer.Allows(
            Capability.ActivityReceive));
        Assert.False(accepted.CapabilitiesGrantedToPeer.Allows(
            Capability.ActivityOffer));
        Assert.Null(source.CurrentPrompt);
    }

    [Fact]
    public async Task ConcurrentRequestIsRejectedWithoutReplacingVisiblePeer()
    {
        using DeviceIdentity firstPeer = CreatePeer("First peer");
        using DeviceIdentity secondPeer = DeviceIdentity.Generate(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Second peer");
        using var source = new DesktopPairingDecisionSource();

        Task<PairingDecision> first = source.DecideAsync(
            CreateRequest(firstPeer, "111111")).AsTask();
        DesktopPairingPrompt firstPrompt = Assert.IsType<DesktopPairingPrompt>(
            source.CurrentPrompt);

        PairingDecision second = await source.DecideAsync(
            CreateRequest(secondPeer, "222222"));

        Assert.False(second.Accepted);
        Assert.Equal(firstPrompt.PromptId, source.CurrentPrompt?.PromptId);
        Assert.Equal("First peer", source.CurrentPrompt?.PeerDisplayName);
        Assert.True(source.TryReject(firstPrompt.PromptId));
        Assert.False((await first).Accepted);
    }

    [Fact]
    public async Task CancellationClearsPromptAndStaleCommandCannotResolveNextRequest()
    {
        using DeviceIdentity firstPeer = CreatePeer("First peer");
        using DeviceIdentity nextPeer = DeviceIdentity.Generate(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Next peer");
        using var source = new DesktopPairingDecisionSource();
        using var cancellation = new CancellationTokenSource();
        Task<PairingDecision> first = source.DecideAsync(
            CreateRequest(firstPeer, "111111"),
            cancellation.Token).AsTask();
        Guid stalePromptId = Assert.IsType<DesktopPairingPrompt>(
            source.CurrentPrompt).PromptId;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Null(source.CurrentPrompt);

        Task<PairingDecision> next = source.DecideAsync(
            CreateRequest(nextPeer, "222222")).AsTask();
        DesktopPairingPrompt nextPrompt = Assert.IsType<DesktopPairingPrompt>(
            source.CurrentPrompt);
        Assert.False(source.TryAccept(stalePromptId, CapabilityGrant.None));
        Assert.False(next.IsCompleted);
        Assert.True(source.TryReject(nextPrompt.PromptId));
        Assert.False((await next).Accepted);
    }

    [Fact]
    public async Task InvalidAuthenticationStringCannotOpenPrompt()
    {
        using DeviceIdentity peer = CreatePeer("Peer desk");
        using var source = new DesktopPairingDecisionSource();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await source.DecideAsync(new PairingConfirmationRequest(
                peer.PublicIdentity,
                new ProtocolVersion(1, 0),
                "12A456",
                DateTimeOffset.UtcNow.AddMinutes(1))));

        Assert.Null(source.CurrentPrompt);
    }

    private static PairingConfirmationRequest CreateRequest(
        DeviceIdentity peer,
        string code) => new(
        peer.PublicIdentity,
        new ProtocolVersion(1, 0),
        code,
        DateTimeOffset.UtcNow.AddMinutes(1));

    private static DeviceIdentity CreatePeer(string displayName) =>
        DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            displayName);

    // An async LongRunning delegate protects only its synchronous prefix. These
    // controllers must remain synchronous until every test-owned barrier drains.
    private static Task<T> RunOnDedicatedThread<T>(Func<T> action) =>
        Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static Task RunOnDedicatedThread(Action action) =>
        Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static void WaitForStage(ManualResetEventSlim stage, string name)
    {
        if (!stage.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException($"Timed out waiting for {name}.");
        }
    }

    private static void CompleteOwnedWork(Task task)
    {
        try
        {
            if (!task.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("A dedicated test worker did not drain.");
            }
        }
        catch (AggregateException)
        {
            // Preserve the worker's original exception rather than Task.Wait's
            // additional aggregate wrapper (including cancellation callback faults).
            task.GetAwaiter().GetResult();
        }
    }

    private sealed record CancellationExercise(
        ConcurrentQueue<DesktopPairingPromptChangedEventArgs> Observed,
        Task<PairingDecision>[] Decisions);

    private sealed class InjectedFixtureFailureException : Exception;
}
