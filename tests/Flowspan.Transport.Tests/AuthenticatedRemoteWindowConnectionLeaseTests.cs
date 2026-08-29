using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedRemoteWindowConnectionLeaseTests
{
    private static readonly DeviceId LocalDeviceId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId PeerDeviceId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.From(
            Guid.Parse("33333333-3333-3333-3333-333333333333"));

    private static readonly ActivityId ActivityId = ActivityId.From(
        Guid.Parse("44444444-4444-4444-4444-444444444444"));

    [Fact]
    public async Task DeferredFailCloseClosesAtPreparationDeadlineAfterLeaseDisposal()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        var time = new ManualTimeProvider(now);
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions();
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        await using (var mediaSession = new AuthenticatedRemoteWindowMediaSession(
            LocalDeviceId,
            PeerDeviceId,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            routes,
            ownedFrames))
        {
            var generation = new RemoteWindowConnectionGeneration(
                value: 1,
                timeProvider: time);
            int failCloseCount = 0;
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                () =>
                {
                    Interlocked.Increment(ref failCloseCount);
                    return ValueTask.CompletedTask;
                },
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            int revocationCallbackCount = 0;
            using CancellationTokenRegistration registration =
                lease.RegisterRevocationCallback(
                    () => Interlocked.Increment(ref revocationCallbackCount));
            RemoteWindowPreparationRequest request = CreateRequest(
                now.AddSeconds(1));

            Assert.True(lease.TryDeferFailCloseUntilPreparationDeadline(request));
            Assert.True(lease.TryDeferFailCloseUntilPreparationDeadline(request));

            Assert.False(lease.IsCurrent);
            Assert.False(lease.IsRevoked);
            Assert.Equal(0, revocationCallbackCount);
            Assert.Equal(0, failCloseCount);
            Assert.False(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? replacement));
            Assert.Null(replacement);

            await lease.DisposeAsync();
            time.Advance(TimeSpan.FromMilliseconds(999));
            Assert.Equal(0, failCloseCount);
            time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(1, failCloseCount);

            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task InvalidDeferredFailCloseDeadlineDoesNotPoisonGeneration(
        int deadlineOffsetSeconds)
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        var time = new ManualTimeProvider(now);
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions();
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        await using (var mediaSession = new AuthenticatedRemoteWindowMediaSession(
            LocalDeviceId,
            PeerDeviceId,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            routes,
            ownedFrames))
        {
            var generation = new RemoteWindowConnectionGeneration(
                value: 1,
                timeProvider: time);
            int failCloseCount = 0;
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                () =>
                {
                    Interlocked.Increment(ref failCloseCount);
                    return ValueTask.CompletedTask;
                },
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            RemoteWindowPreparationRequest request = CreateRequest(
                now.AddSeconds(deadlineOffsetSeconds));

            Assert.False(
                lease.TryDeferFailCloseUntilPreparationDeadline(request));

            Assert.True(lease.IsCurrent);
            Assert.False(lease.IsRevoked);
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? replacement));
            await using (replacement)
            {
                Assert.NotNull(replacement);
            }

            time.Advance(TimeSpan.FromSeconds(20));
            Assert.Equal(0, failCloseCount);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task ConflictingDeferredFailCloseCannotReplaceOrExtendDeadline()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        var time = new ManualTimeProvider(now);
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions();
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        await using (var mediaSession = new AuthenticatedRemoteWindowMediaSession(
            LocalDeviceId,
            PeerDeviceId,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            routes,
            ownedFrames))
        {
            var generation = new RemoteWindowConnectionGeneration(
                value: 1,
                timeProvider: time);
            int failCloseCount = 0;
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                () =>
                {
                    Interlocked.Increment(ref failCloseCount);
                    return ValueTask.CompletedTask;
                },
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            RemoteWindowPreparationRequest original = CreateRequest(
                now.AddSeconds(1));
            RemoteWindowPreparationRequest conflicting = CreateRequest(
                now.AddSeconds(5));

            Assert.True(
                lease.TryDeferFailCloseUntilPreparationDeadline(original));
            Assert.False(
                lease.TryDeferFailCloseUntilPreparationDeadline(conflicting));
            Assert.True(
                lease.TryDeferFailCloseUntilPreparationDeadline(original));

            time.Advance(TimeSpan.FromMilliseconds(999));
            Assert.Equal(0, failCloseCount);
            time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(1, failCloseCount);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeadlineAndExplicitFailCloseShareOneCleanup(
        bool deadlineFirst)
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        var time = new ManualTimeProvider(now);
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions();
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        await using (var mediaSession = new AuthenticatedRemoteWindowMediaSession(
            LocalDeviceId,
            PeerDeviceId,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            routes,
            ownedFrames))
        {
            var generation = new RemoteWindowConnectionGeneration(
                value: 1,
                timeProvider: time);
            int failCloseCount = 0;
            var releaseCleanup = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                () =>
                {
                    Interlocked.Increment(ref failCloseCount);
                    return new ValueTask(releaseCleanup.Task);
                },
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            Assert.True(lease.TryDeferFailCloseUntilPreparationDeadline(
                CreateRequest(now.AddSeconds(1))));

            Task explicitFailClose;
            if (deadlineFirst)
            {
                time.Advance(TimeSpan.FromSeconds(1));
                explicitFailClose = lease.FailCloseAsync().AsTask();
            }
            else
            {
                explicitFailClose = lease.FailCloseAsync().AsTask();
                time.Advance(TimeSpan.FromSeconds(1));
            }

            Assert.Equal(1, failCloseCount);
            Assert.False(explicitFailClose.IsCompleted);
            releaseCleanup.TrySetResult();
            await explicitFailClose.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, failCloseCount);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task TimerDisposalFailureDoesNotSkipOrLoseFailCloseFailure()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        var time = new ManualTimeProvider(
            now,
            throwOnTimerDispose: true);
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions();
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        await using (var mediaSession = new AuthenticatedRemoteWindowMediaSession(
            LocalDeviceId,
            PeerDeviceId,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            routes,
            ownedFrames))
        {
            var generation = new RemoteWindowConnectionGeneration(
                value: 1,
                timeProvider: time);
            int failCloseCount = 0;
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                () =>
                {
                    Interlocked.Increment(ref failCloseCount);
                    return ValueTask.FromException(
                        new InvalidOperationException(
                            "test fail-close cleanup failed"));
                },
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            Assert.True(lease.TryDeferFailCloseUntilPreparationDeadline(
                CreateRequest(now.AddSeconds(1))));

            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() =>
                lease.FailCloseAsync().AsTask());

            Assert.Equal(1, failCloseCount);
            Assert.Contains(
                "test timer disposal failed",
                failure.ToString(),
                StringComparison.Ordinal);
            Assert.Contains(
                "test fail-close cleanup failed",
                failure.ToString(),
                StringComparison.Ordinal);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task ExplicitFailClosePoisonsBeforeSharedCleanupCompletes()
    {
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions();
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        await using (var mediaSession = new AuthenticatedRemoteWindowMediaSession(
            LocalDeviceId,
            PeerDeviceId,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            routes,
            ownedFrames))
        {
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            var releaseCleanup = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int failCloseCount = 0;
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                () =>
                {
                    Interlocked.Increment(ref failCloseCount);
                    return new ValueTask(releaseCleanup.Task);
                },
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);

            Task failClosing = lease.FailCloseAsync().AsTask();

            Assert.False(lease.IsCurrent);
            Assert.False(failClosing.IsCompleted);
            Assert.Equal(1, failCloseCount);
            Assert.False(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? replacement));
            Assert.Null(replacement);
            releaseCleanup.TrySetResult();
            await failClosing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, failCloseCount);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Theory]
    [InlineData(TimerSetupFailure.UtcNow)]
    [InlineData(TimerSetupFailure.CreateThrows)]
    [InlineData(TimerSetupFailure.CreateReturnsNull)]
    public async Task DeferredFailCloseTimerSetupFailureDoesNotPoisonGeneration(
        TimerSetupFailure setupFailure)
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        var time = new FaultingTimerTimeProvider(now, setupFailure);
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions();
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        await using (var mediaSession = new AuthenticatedRemoteWindowMediaSession(
            LocalDeviceId,
            PeerDeviceId,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            routes,
            ownedFrames))
        {
            var generation = new RemoteWindowConnectionGeneration(
                value: 1,
                timeProvider: time);
            int failCloseCount = 0;
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                () =>
                {
                    Interlocked.Increment(ref failCloseCount);
                    return ValueTask.CompletedTask;
                },
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);

            Assert.False(lease.TryDeferFailCloseUntilPreparationDeadline(
                CreateRequest(now.AddSeconds(1))));

            Assert.True(lease.IsCurrent);
            Assert.Equal(0, failCloseCount);
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? replacement));
            await using (replacement)
            {
                Assert.NotNull(replacement);
            }

            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task OwnerRevocationCancelsDeferredDeadlineFailClose()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        var time = new ManualTimeProvider(now);
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions();
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        await using (var mediaSession = new AuthenticatedRemoteWindowMediaSession(
            LocalDeviceId,
            PeerDeviceId,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            routes,
            ownedFrames))
        {
            var generation = new RemoteWindowConnectionGeneration(
                value: 1,
                timeProvider: time);
            int failCloseCount = 0;
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                () =>
                {
                    Interlocked.Increment(ref failCloseCount);
                    return ValueTask.CompletedTask;
                },
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            Assert.True(lease.TryDeferFailCloseUntilPreparationDeadline(
                CreateRequest(now.AddSeconds(1))));

            Assert.Null(generation.RevokeAndReleaseOwner());
            time.Advance(TimeSpan.FromSeconds(2));

            Assert.True(lease.IsRevoked);
            Assert.Equal(0, failCloseCount);
        }
    }

    [Fact]
    public async Task DeadlineFailCloseFailureRemainsOnSharedCleanupTask()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        var time = new ManualTimeProvider(now);
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions();
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        await using (var mediaSession = new AuthenticatedRemoteWindowMediaSession(
            LocalDeviceId,
            PeerDeviceId,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            routes,
            ownedFrames))
        {
            var generation = new RemoteWindowConnectionGeneration(
                value: 1,
                timeProvider: time);
            int failCloseCount = 0;
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                () =>
                {
                    Interlocked.Increment(ref failCloseCount);
                    return ValueTask.FromException(
                        new InvalidOperationException(
                            "test deferred fail-close failed"));
                },
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            Assert.True(lease.TryDeferFailCloseUntilPreparationDeadline(
                CreateRequest(now.AddSeconds(1))));

            time.Advance(TimeSpan.FromSeconds(1));
            Task first = lease.FailCloseAsync().AsTask();
            Task second = lease.FailCloseAsync().AsTask();
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => first);

            Assert.Same(first, second);
            Assert.Equal(1, failCloseCount);
            Assert.Contains(
                "test deferred fail-close failed",
                failure.ToString(),
                StringComparison.Ordinal);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task RevocationRegistrationKeepsGenerationAliveUntilCallbacksReturn()
    {
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions();
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        await using (var mediaSession = new AuthenticatedRemoteWindowMediaSession(
            LocalDeviceId,
            PeerDeviceId,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            routes,
            ownedFrames))
        {
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            AuthenticatedRemoteWindowConnectionLease lease = Assert.IsType<
                AuthenticatedRemoteWindowConnectionLease>(acquired);
            bool callbackObservedRevocation = false;
            using CancellationTokenRegistration registration =
                lease.RegisterRevocationCallback(() =>
                {
                    lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    callbackObservedRevocation = lease.IsRevoked;
                });

            Exception? failure = generation.RevokeAndReleaseOwner();

            Assert.Null(failure);
            Assert.True(callbackObservedRevocation);
            Assert.True(lease.IsRevoked);
        }
    }

    [Fact]
    public void CompletedRevocationRegistrationReleasesOwnerFromCallerContext()
    {
        WeakReference owner = CreateCompletedRevocationRegistration();

        AssertCollected(owner);
    }

    [Fact]
    public async Task ReturnedCallbackContextIsInactiveWhileAnotherCallbackRuns()
    {
        var owner = new object();
        var generation = new RemoteWindowConnectionGeneration(
            value: 1,
            owner);
        var copiedContextReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCopiedContext = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var copiedContextIsActive = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingCallbackStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlockingCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration blocking =
            generation.RegisterRevocationCallback(() =>
            {
                blockingCallbackStarted.TrySetResult();
                releaseBlockingCallback.Task.GetAwaiter().GetResult();
            });
        using CancellationTokenRegistration copying =
            generation.RegisterRevocationCallback(() =>
                _ = ObserveFromCopiedContextAsync());
        Task<Exception?> revoking = Task.Run(
            generation.RevokeAndReleaseOwner);

        try
        {
            await copiedContextReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await blockingCallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            releaseCopiedContext.TrySetResult();

            Assert.False(await copiedContextIsActive.Task.WaitAsync(
                TimeSpan.FromSeconds(5)));
        }
        finally
        {
            releaseCopiedContext.TrySetResult();
            releaseBlockingCallback.TrySetResult();
        }

        Assert.Null(await revoking.WaitAsync(TimeSpan.FromSeconds(5)));

        async Task ObserveFromCopiedContextAsync()
        {
            copiedContextReady.TrySetResult();
            await releaseCopiedContext.Task;
            copiedContextIsActive.TrySetResult(
                RemoteWindowConnectionGeneration.IsActiveRevocationCallback(
                    owner));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateCompletedRevocationRegistration()
    {
        var owner = new object();
        var weakOwner = new WeakReference(owner);
        var generation = new RemoteWindowConnectionGeneration(
            value: 1,
            owner);
        using CancellationTokenRegistration registration =
            generation.RegisterRevocationCallback(static () => { });

        Assert.Null(generation.RevokeAndReleaseOwner());
        GC.KeepAlive(owner);
        return weakOwner;
    }

    private static void AssertCollected(WeakReference reference)
    {
        for (int attempt = 0; attempt < 3 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(reference.IsAlive);
    }

    private static (SecureFrameSession Owned, SecureFrameSession Counterpart)
        CreateSecureSessions()
    {
        byte[] secret = SHA256.HashData(BitConverter.GetBytes(0x75));
        byte[] transcriptHash = SHA256.HashData(
            Encoding.ASCII.GetBytes("connection-generation-revocation"));
        using SecureSessionKeyMaterial material =
            SecureSessionKeyMaterial.DeriveRemoteWindowMedia(
                secret,
                transcriptHash);
        CryptographicOperations.ZeroMemory(secret);
        CryptographicOperations.ZeroMemory(transcriptHash);
        return (
            material.CreateSession(SecureSessionRole.Initiator),
            material.CreateSession(SecureSessionRole.Responder));
    }

    public enum TimerSetupFailure
    {
        UtcNow,
        CreateThrows,
        CreateReturnsNull,
    }

    private sealed class FaultingTimerTimeProvider(
        DateTimeOffset utcNow,
        TimerSetupFailure setupFailure) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            setupFailure is TimerSetupFailure.UtcNow
                ? throw new InvalidOperationException(
                    "test UTC clock failed")
                : utcNow;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) => setupFailure switch
            {
                TimerSetupFailure.CreateThrows =>
                    throw new InvalidOperationException(
                        "test timer creation failed"),
                TimerSetupFailure.CreateReturnsNull => null!,
                _ => throw new InvalidOperationException(
                    "The timer should not be created after a UTC clock failure."),
            };
    }

    private static RemoteWindowPreparationRequest CreateRequest(
        DateTimeOffset deadline) => RemoteWindowPreparationRequest.Create(
        CorrelationId.From(Guid.NewGuid()),
        SessionId,
        ActivityId,
        PeerDeviceId,
        LocalDeviceId,
        MirrorParticipantRole.ViewOnly,
        deadline);

    private sealed class ManualTimeProvider(
        DateTimeOffset utcNow,
        bool throwOnTimerDispose = false) : TimeProvider
    {
        private readonly Lock gate = new();
        private readonly bool throwOnTimerDispose = throwOnTimerDispose;
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

                if (owner.throwOnTimerDispose)
                {
                    throw new InvalidOperationException(
                        "test timer disposal failed");
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

    private sealed class UnusedPreparationChannel :
        IRemoteWindowPreparationChannel
    {
        public DeviceId ParticipantDeviceId => PeerDeviceId;

        public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
