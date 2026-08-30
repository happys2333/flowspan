using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Platform;
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
    public async Task ConnectionPreparationReservationBindsExactGenerationAndMediaUntilDisposed()
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
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var sink = new RecordingConnectionPreparationSink();

            AuthenticatedRemoteWindowConnectionPreparationReservationResult result =
                lease.TryReservePreparation(sink);

            Assert.Equal(
                AuthenticatedRemoteWindowConnectionPreparationReservationStatus.Reserved,
                result.Status);
            IAuthenticatedRemoteWindowConnectionPreparationRegistration registration =
                Assert.IsAssignableFrom<
                    IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                    result.Registration);
            Assert.Same(registration, sink.OwnedRegistration);
            Assert.True(registration.IsCurrent);
            Assert.True(result.Reserved);

            registration.Dispose();

            Assert.False(registration.IsCurrent);
            Assert.Equal(0, sink.InvalidationCount);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task OwnerClaimThrowAllowsAbaReplacementAndLateOldDisposeCannotClearIt()
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
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var injected = new IOException("connection owner claim canary");
            var failingSink = new RecordingConnectionPreparationSink(
                owning: () => throw injected);

            IOException failure = Assert.Throws<IOException>(() =>
                lease.TryReservePreparation(failingSink));

            Assert.Same(injected, failure);
            IAuthenticatedRemoteWindowConnectionPreparationRegistration stale =
                Assert.IsAssignableFrom<
                    IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                    failingSink.OwnedRegistration);
            Assert.False(stale.IsCurrent);
            var replacementSink = new RecordingConnectionPreparationSink();
            IAuthenticatedRemoteWindowConnectionPreparationRegistration replacement =
                Assert.IsAssignableFrom<
                    IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                    lease.TryReservePreparation(replacementSink).Registration);
            Assert.True(replacement.IsCurrent);
            Assert.True(replacement.RegistrationId > stale.RegistrationId);

            stale.Dispose();

            Assert.True(replacement.IsCurrent);
            replacement.Dispose();
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task ConnectionRevocationInvalidatesPreparationBeforeOrdinaryCallbacks()
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
            var timeline = new List<string>();
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var sink = new RecordingConnectionPreparationSink(
                () => timeline.Add("preparation.invalidate"));
            AuthenticatedRemoteWindowConnectionPreparationReservationResult result =
                lease.TryReservePreparation(sink);
            using IDisposable callback = lease.RegisterRevocationCallback(
                () => timeline.Add("connection.revoked"));

            Exception? failure = generation.RevokeAndReleaseOwner();

            Assert.Null(failure);
            Assert.Equal(
                ["preparation.invalidate", "connection.revoked"],
                timeline);
            Assert.Equal(1, sink.InvalidationCount);
            Assert.False(Assert.IsAssignableFrom<
                IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                result.Registration).IsCurrent);
        }
    }

    [Fact]
    public async Task MediaControlStopInvokesConnectionRevocationCallbackExactlyOnce()
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
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            int callbackCount = 0;
            bool? currentObservedByCallback = null;
            using IDisposable callback = lease.RegisterRevocationCallback(() =>
            {
                Interlocked.Increment(ref callbackCount);
                currentObservedByCallback = lease.IsCurrent;
            });

            mediaSession.RequestControlStop();

            Assert.Equal(1, callbackCount);
            Assert.False(currentObservedByCallback);
            Assert.False(lease.IsCurrent);
            Assert.Null(generation.RevokeAndReleaseOwner());
            Assert.Equal(1, callbackCount);
        }
    }

    [Fact]
    public void ConnectionRevocationRegistrationSetupFailureRollsBackAndPreservesBothFailures()
    {
        var timeline = new List<string>();
        var setupFailure = new IOException("media registration setup canary");
        var rollbackFailure = new InvalidOperationException(
            "generation registration rollback canary");

        AggregateException failure = Assert.Throws<AggregateException>(() =>
            AuthenticatedRemoteWindowConnectionRevocationRegistration.Register(
                static () => { },
                _ =>
                {
                    timeline.Add("generation.register");
                    return new RecordingDisposable(() =>
                    {
                        timeline.Add("generation.dispose");
                        throw rollbackFailure;
                    });
                },
                _ =>
                {
                    timeline.Add("media.register");
                    throw setupFailure;
                }));

        Assert.Equal(
            ["generation.register", "media.register", "generation.dispose"],
            timeline);
        Assert.Collection(
            failure.Flatten().InnerExceptions,
            item => Assert.Same(setupFailure, item),
            item => Assert.Same(rollbackFailure, item));
    }

    [Fact]
    public void ConnectionRevocationRegistrationConstructionFailureRollsBackInReverseOrder()
    {
        var timeline = new List<string>();
        var setupFailure = new IOException(
            "composite registration construction canary");

        IOException failure = Assert.Throws<IOException>(() =>
            AuthenticatedRemoteWindowConnectionRevocationRegistration.Register(
                static () => { },
                _ =>
                {
                    timeline.Add("generation.register");
                    return new RecordingDisposable(
                        () => timeline.Add("generation.dispose"));
                },
                _ =>
                {
                    timeline.Add("media.register");
                    return new RecordingDisposable(
                        () => timeline.Add("media.dispose"));
                },
                (_, _) =>
                {
                    timeline.Add("composite.create");
                    throw setupFailure;
                }));

        Assert.Same(setupFailure, failure);
        Assert.Equal(
            [
                "generation.register",
                "media.register",
                "composite.create",
                "media.dispose",
                "generation.dispose",
            ],
            timeline);
    }

    [Fact]
    public void ConnectionRevocationRegistrationConstructionOutOfMemoryRollsBackBothHandlesBeforeEscaping()
    {
        var timeline = new List<string>();
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var fatal = new OutOfMemoryException(
            "composite registration construction fatal canary");
#pragma warning restore CA2201

        OutOfMemoryException failure = Assert.Throws<OutOfMemoryException>(() =>
            AuthenticatedRemoteWindowConnectionRevocationRegistration.Register(
                static () => { },
                _ => new RecordingDisposable(
                    () => timeline.Add("generation.dispose")),
                _ => new RecordingDisposable(
                    () => timeline.Add("media.dispose")),
                (_, _) => throw fatal));

        Assert.Same(fatal, failure);
        Assert.Equal(
            ["media.dispose", "generation.dispose"],
            timeline);
    }

    [Fact]
    public void ConnectionRevocationRegistrationDisposesInReverseOrderAndReplaysCleanupFailure()
    {
        var timeline = new List<string>();
        var mediaFailure = new IOException("media registration cleanup canary");
        var generationFailure = new InvalidOperationException(
            "generation registration cleanup canary");
        IDisposable registration =
            AuthenticatedRemoteWindowConnectionRevocationRegistration.Register(
                static () => { },
                _ =>
                {
                    timeline.Add("generation.register");
                    return new RecordingDisposable(() =>
                    {
                        timeline.Add("generation.dispose");
                        throw generationFailure;
                    });
                },
                _ =>
                {
                    timeline.Add("media.register");
                    return new RecordingDisposable(() =>
                    {
                        timeline.Add("media.dispose");
                        throw mediaFailure;
                    });
                });

        AggregateException first = Assert.Throws<AggregateException>(
            registration.Dispose);
        AggregateException second = Assert.Throws<AggregateException>(
            registration.Dispose);

        Assert.Same(first, second);
        Assert.Equal(
            [
                "generation.register",
                "media.register",
                "media.dispose",
                "generation.dispose",
            ],
            timeline);
        Assert.Collection(
            first.Flatten().InnerExceptions,
            item => Assert.Same(mediaFailure, item),
            item => Assert.Same(generationFailure, item));
    }

    [Fact]
    public void ConnectionRevocationRegistrationCleanupEscapesExactOutOfMemoryAfterBothDisposals()
    {
        var timeline = new List<string>();
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var fatal = new OutOfMemoryException(
            "media registration cleanup fatal canary");
#pragma warning restore CA2201
        var generationFailure = new IOException(
            "generation registration cleanup canary");
        IDisposable registration =
            AuthenticatedRemoteWindowConnectionRevocationRegistration.Register(
                static () => { },
                _ => new RecordingDisposable(() =>
                {
                    timeline.Add("generation.dispose");
                    throw generationFailure;
                }),
                _ => new RecordingDisposable(() =>
                {
                    timeline.Add("media.dispose");
                    throw fatal;
                }));

        OutOfMemoryException first = Assert.Throws<OutOfMemoryException>(
            registration.Dispose);
        OutOfMemoryException second = Assert.Throws<OutOfMemoryException>(
            registration.Dispose);

        Assert.Same(fatal, first);
        Assert.Same(first, second);
        Assert.Equal(
            ["media.dispose", "generation.dispose"],
            timeline);
    }

    [Fact]
    public async Task MediaRevocationCallbackCanDisposeItsCompositeRegistration()
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
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            int callbackCount = 0;
            IDisposable? registration = null;
            registration = lease.RegisterRevocationCallback(() =>
            {
                Interlocked.Increment(ref callbackCount);
                registration!.Dispose();
            });

            mediaSession.RequestControlStop();

            Assert.Equal(1, callbackCount);
            registration.Dispose();
            Assert.Null(generation.RevokeAndReleaseOwner());
            Assert.Equal(1, callbackCount);
        }
    }

    [Fact]
    public async Task MediaRevocationCallbackCanFailCloseWithoutRecursiveCleanup()
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
            var releaseCleanup = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int cleanupCount = 0;
            var generation = new RemoteWindowConnectionGeneration(
                value: 1,
                revocationCallbackOwner: new object());
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                () =>
                {
                    Interlocked.Increment(ref cleanupCount);
                    return new ValueTask(releaseCleanup.Task);
                },
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            Task? callbackCleanup = null;
            using IDisposable registration = lease.RegisterRevocationCallback(
                () => callbackCleanup = lease.FailCloseAsync().AsTask());

            try
            {
                mediaSession.RequestControlStop();

                Assert.NotNull(callbackCleanup);
                Assert.True(callbackCleanup.IsCompletedSuccessfully);
                Assert.Equal(0, cleanupCount);
            }
            finally
            {
                releaseCleanup.TrySetResult();
            }

            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task ConcurrentGenerationAndMediaRevocationInvokeCallbackExactlyOnce()
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
            var generation = new RemoteWindowConnectionGeneration(
                value: 1,
                revocationCallbackOwner: new object());
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var callbackEntered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCallback = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int callbackCount = 0;
            using IDisposable registration = lease.RegisterRevocationCallback(() =>
            {
                Interlocked.Increment(ref callbackCount);
                callbackEntered.TrySetResult();
                releaseCallback.Task.GetAwaiter().GetResult();
            });
            Task mediaStopping = Task.Run(mediaSession.RequestControlStop);

            try
            {
                await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Task<Exception?> generationRevoking = Task.Run(
                    generation.RevokeAndReleaseOwner);

                Assert.Null(await generationRevoking.WaitAsync(
                    TimeSpan.FromSeconds(5)));
                Assert.Equal(1, callbackCount);
            }
            finally
            {
                releaseCallback.TrySetResult();
            }

            await mediaStopping.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, callbackCount);
        }
    }

    [Fact]
    public async Task RegistrationAfterMediaRevocationInvokesSynchronouslyOnce()
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
            var generation = new RemoteWindowConnectionGeneration(
                value: 1,
                revocationCallbackOwner: new object());
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            mediaSession.RequestControlStop();
            int callbackCount = 0;

            using IDisposable registration = lease.RegisterRevocationCallback(
                () => Interlocked.Increment(ref callbackCount));

            Assert.Equal(1, callbackCount);
            Assert.Null(generation.RevokeAndReleaseOwner());
            Assert.Equal(1, callbackCount);
        }
    }

    [Fact]
    public async Task RegistrationAfterGenerationRevocationInvokesSynchronouslyOnce()
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
            var generation = new RemoteWindowConnectionGeneration(
                value: 1,
                revocationCallbackOwner: new object());
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            Assert.Null(generation.RevokeAndReleaseOwner());
            int callbackCount = 0;

            using IDisposable registration = lease.RegisterRevocationCallback(
                () => Interlocked.Increment(ref callbackCount));

            Assert.Equal(1, callbackCount);
            mediaSession.RequestControlStop();
            Assert.Equal(1, callbackCount);
        }
    }

    [Fact]
    public async Task ExplicitFailCloseInvalidatesPreparationBeforeCleanupStarts()
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
            var timeline = new List<string>();
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                () =>
                {
                    timeline.Add("connection.cleanup");
                    return ValueTask.CompletedTask;
                },
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var sink = new RecordingConnectionPreparationSink(
                () => timeline.Add("preparation.invalidate"));
            AuthenticatedRemoteWindowConnectionPreparationReservationResult result =
                lease.TryReservePreparation(sink);

            await lease.FailCloseAsync();

            Assert.Equal(
                ["preparation.invalidate", "connection.cleanup"],
                timeline);
            Assert.False(Assert.IsAssignableFrom<
                IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                result.Registration).IsCurrent);
            Assert.False(lease.IsCurrent);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task NonFatalPreparationSinkAndFailCloseFailuresShareOrderedCleanup()
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
            var timeline = new List<string>();
            var sinkFailure = new IOException("connection preparation sink canary");
            var cleanupFailure = new InvalidOperationException(
                "connection fail-close cleanup canary");
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                () =>
                {
                    timeline.Add("connection.cleanup");
                    return ValueTask.FromException(cleanupFailure);
                },
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var sink = new RecordingConnectionPreparationSink(() =>
            {
                timeline.Add("preparation.invalidate");
                throw sinkFailure;
            });
            _ = lease.TryReservePreparation(sink);

            Task first = lease.FailCloseAsync().AsTask();
            Task second = lease.FailCloseAsync().AsTask();
            AggregateException failure = await Assert.ThrowsAsync<
                AggregateException>(() => first);

            Assert.Same(first, second);
            Assert.Equal(
                ["preparation.invalidate", "connection.cleanup"],
                timeline);
            Assert.Collection(
                failure.Flatten().InnerExceptions,
                item => Assert.Same(sinkFailure, item),
                item => Assert.Same(cleanupFailure, item));
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task CompositeRevocationCleanupEscapesExactOutOfMemoryAfterAllCleanup()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var fatal = new OutOfMemoryException(
            "connection revocation timer cleanup canary");
#pragma warning restore CA2201
        var time = new ManualTimeProvider(
            now,
            timerDisposeFailure: fatal);
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
            var timeline = new List<string>();
            var sinkFailure = new IOException(
                "connection preparation sink cleanup canary");
            var generation = new RemoteWindowConnectionGeneration(
                value: 1,
                timeProvider: time);
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var sink = new RecordingConnectionPreparationSink(() =>
            {
                timeline.Add("preparation.invalidate");
                throw sinkFailure;
            });
            _ = lease.TryReservePreparation(sink);
            using IDisposable callback = lease.RegisterRevocationCallback(
                () => timeline.Add("connection.revoked"));
            Assert.True(lease.TryDeferFailCloseUntilPreparationDeadline(
                CreateRequest(now.AddSeconds(1))));

            OutOfMemoryException failure = Assert.Throws<OutOfMemoryException>(
                generation.RevokeAndReleaseOwner);

            Assert.Same(fatal, failure);
            Assert.Equal(
                ["preparation.invalidate", "connection.revoked"],
                timeline);
            Assert.Equal(1, sink.InvalidationCount);
            Assert.True(lease.IsRevoked);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task ConnectionRevocationBeforeRouteAdmissionDoesNotCrossRegistry()
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
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var admission = new RecordingHostPreparationAdmission();
            var sink = new RecordingConnectionPreparationSink();
            IAuthenticatedRemoteWindowConnectionPreparationRegistration registration =
                Assert.IsAssignableFrom<
                    IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                    lease.TryReservePreparation(sink).Registration);
            Assert.Null(generation.RevokeAndReleaseOwner());

            Assert.Throws<InvalidOperationException>(() =>
                lease.PrepareResponderRoute(
                    SessionId,
                    ActivityId,
                    registration,
                    admission));

            Assert.Equal(1, sink.InvalidationCount);
            Assert.Equal(0, admission.RouteAdmissionCount);
            Assert.Equal(0, routes.Count);
        }
    }

    [Fact]
    public async Task ActiveConnectionPreparationRequiresItsExactRouteOwner()
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
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var sink = new RecordingConnectionPreparationSink();
            IAuthenticatedRemoteWindowConnectionPreparationRegistration registration =
                Assert.IsAssignableFrom<
                    IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                    lease.TryReservePreparation(sink).Registration);
            var admission = new RecordingHostPreparationAdmission();

            Assert.Throws<InvalidOperationException>(() =>
                lease.PrepareResponderRoute(SessionId, ActivityId));
            Assert.Equal(0, routes.Count);

            _ = lease.PrepareResponderRoute(
                SessionId,
                ActivityId,
                registration,
                admission);

            Assert.Equal(1, admission.RouteAdmissionCount);
            Assert.Equal(1, routes.Count);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task ActiveConnectionPreparationRequiresItsExactPrepareSendOwner()
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
            var channel = new AdmissionRecordingReservedPreparationChannel();
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                channel,
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var sink = new RecordingConnectionPreparationSink();
            IAuthenticatedRemoteWindowConnectionPreparationRegistration registration =
                Assert.IsAssignableFrom<
                    IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                    lease.TryReservePreparation(sink).Registration);
            var admission = new RecordingHostPreparationAdmission();
            RemoteWindowPreparationRequest request = CreateHostPreparation();

            RemoteWindowPreparationDeliveryResult unreserved =
                await lease.PrepareReservedAsync(
                    request,
                    admission,
                    CancellationToken.None);
            Assert.Equal(
                RemoteWindowControlDeliveryStatus.NotDelivered,
                unreserved.Status);
            Assert.Equal(0, channel.WireAdmissionCount);

            RemoteWindowPreparationDeliveryResult reserved =
                await lease.PrepareReservedAsync(
                    request,
                    registration,
                    admission,
                    CancellationToken.None);

            Assert.Equal(
                RemoteWindowControlDeliveryStatus.Acknowledged,
                reserved.Status);
            Assert.Equal(1, channel.WireAdmissionCount);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task ActiveConnectionPreparationBlocksPublicPrepareAfterChannelEntry()
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
            var channel = new GatedReservedPreparationChannel();
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                channel,
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);

            Task<RemoteWindowPreparationDeliveryResult> preparing =
                lease.PrepareAsync(
                        CreateHostPreparation(),
                        CancellationToken.None)
                    .AsTask();
            await channel.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var sink = new RecordingConnectionPreparationSink();
            IAuthenticatedRemoteWindowConnectionPreparationRegistration registration =
                Assert.IsAssignableFrom<
                    IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                    lease.TryReservePreparation(sink).Registration);

            channel.ReleaseWireAdmission.TrySetResult();
            RemoteWindowPreparationDeliveryResult result =
                await preparing.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(
                RemoteWindowControlDeliveryStatus.NotDelivered,
                result.Status);
            Assert.Equal(0, channel.PublicPrepareCount);
            Assert.Equal(1, channel.ReservedPrepareCount);
            Assert.Equal(1, channel.WireAdmissionCount);
            Assert.Equal(0, channel.WireSendCount);
            Assert.True(registration.IsCurrent);
            registration.Dispose();
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task ActiveConnectionPreparationBlocksPublicPrepareSendAtWireAdmission()
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
            using var callerCancellation = new CancellationTokenSource();
            var wire = new LeasePreparationWireConnection(
                LocalDeviceId,
                PeerDeviceId)
            {
                BlockReadUntilCancelled = true,
                CallerCancellation = callerCancellation,
            };
            var session = new RemoteWindowControlSession(
                wire,
                timeProvider: TimeProvider.System);
            session.StartDispatch();
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                session,
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? firstAcquired));
            Assert.True(generation.TryAcquire(
                session,
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? secondAcquired));
            await using AuthenticatedRemoteWindowConnectionLease reservationOwner =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(
                    firstAcquired);
            await using AuthenticatedRemoteWindowConnectionLease publicCaller =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(
                    secondAcquired);
            var sink = new RecordingConnectionPreparationSink();
            IAuthenticatedRemoteWindowConnectionPreparationRegistration registration =
                Assert.IsAssignableFrom<
                    IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                    reservationOwner.TryReservePreparation(sink).Registration);

            try
            {
                RemoteWindowPreparationDeliveryResult result =
                    await publicCaller.PrepareAsync(
                        CreateHostPreparation(),
                        callerCancellation.Token);

                Assert.Equal(
                    RemoteWindowControlDeliveryStatus.NotDelivered,
                    result.Status);
                Assert.Equal(0, wire.SendCount);
                Assert.True(registration.IsCurrent);
            }
            finally
            {
                registration.Dispose();
                callerCancellation.Cancel();
                session.Cancel();
                await session.StopDispatchAsync().AsTask().WaitAsync(
                    TimeSpan.FromSeconds(5));
                await session.DisposeAsync();
                Assert.Null(generation.RevokeAndReleaseOwner());
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicPrepareFailsClosedWithoutReservedWireAdmission(
        bool activeConnectionPreparation)
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
            var channel = new RecordingNonReservedPreparationChannel();
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                channel,
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            IAuthenticatedRemoteWindowConnectionPreparationRegistration?
                registration = activeConnectionPreparation
                    ? Assert.IsAssignableFrom<
                        IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                        lease.TryReservePreparation(
                            new RecordingConnectionPreparationSink()).Registration)
                    : null;

            RemoteWindowPreparationDeliveryResult result =
                await lease.PrepareAsync(
                    CreateHostPreparation(),
                    CancellationToken.None);

            Assert.Equal(
                RemoteWindowControlDeliveryStatus.NotDelivered,
                result.Status);
            Assert.Equal(0, channel.PrepareCount);
            Assert.Equal(
                activeConnectionPreparation,
                registration?.IsCurrent == true);
            registration?.Dispose();
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task AdmittedRouteOperationDrainsBeforeMediaCleanup()
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
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var admission = new RecordingHostPreparationAdmission
            {
                BlockRouteCompletion = true,
            };
            var sink = new RecordingConnectionPreparationSink();
            IAuthenticatedRemoteWindowConnectionPreparationRegistration registration =
                Assert.IsAssignableFrom<
                    IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                    lease.TryReservePreparation(sink).Registration);
            Task<RemoteWindowMediaRouteBinding> selecting = Task.Run(() =>
                lease.PrepareResponderRoute(
                    SessionId,
                    ActivityId,
                    registration,
                    admission));
            await admission.RouteCompletionEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(5));

            Assert.Null(generation.RevokeAndReleaseOwner());
            Task draining = generation.WaitForRemoteWindowOperationsAsync();
            try
            {
                Assert.False(draining.IsCompleted);
                Assert.Equal(1, routes.Count);
                Assert.Equal(1, sink.InvalidationCount);
                Assert.False(registration.IsCurrent);
            }
            finally
            {
                admission.ReleaseRouteCompletion.TrySetResult();
            }

            _ = await selecting.WaitAsync(TimeSpan.FromSeconds(5));
            await draining.WaitAsync(TimeSpan.FromSeconds(5));
            await mediaSession.DisposeAsync();
            Assert.Equal(0, routes.Count);
        }
    }

    [Fact]
    public async Task RouteSideEffectThenCompletionThrowRetainsFailureAndDrains()
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
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var injected = new IOException("route completion canary");
            var admission = new RecordingHostPreparationAdmission
            {
                RouteCompletionFailure = injected,
            };

            IOException failure = Assert.Throws<IOException>(() =>
                lease.PrepareResponderRoute(SessionId, ActivityId, admission));

            Assert.Same(injected, failure);
            Assert.Equal(1, admission.RouteFailureCount);
            Assert.Equal(1, routes.Count);
            Assert.Null(generation.RevokeAndReleaseOwner());
            await generation.WaitForRemoteWindowOperationsAsync().WaitAsync(
                TimeSpan.FromSeconds(5));
            await mediaSession.DisposeAsync();
            Assert.Equal(0, routes.Count);
        }
    }

    [Fact]
    public async Task PublicRouteOperationCannotBeClaimedTwice()
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
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);

            _ = lease.PrepareResponderRoute(SessionId, ActivityId);
            Assert.Throws<InvalidOperationException>(() =>
                lease.PrepareResponderRoute(SessionId, ActivityId));

            Assert.Equal(1, routes.Count);
            Assert.Null(generation.RevokeAndReleaseOwner());
            await generation.WaitForRemoteWindowOperationsAsync().WaitAsync(
                TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ConnectionPreparationCannotReserveAfterRouteClaimed()
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
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            _ = lease.PrepareResponderRoute(SessionId, ActivityId);
            var sink = new RecordingConnectionPreparationSink();

            AuthenticatedRemoteWindowConnectionPreparationReservationResult result =
                lease.TryReservePreparation(sink);

            Assert.Equal(
                AuthenticatedRemoteWindowConnectionPreparationReservationStatus
                    .ReservationConflict,
                result.Status);
            Assert.Null(result.Registration);
            Assert.Null(sink.OwnedRegistration);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task AdmissionCallerCancellationRestoresOriginalTokenAcrossLease()
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
            var channel = new BlockingAdmissionPreparationChannel();
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                channel,
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            using var cancellation = new CancellationTokenSource();

            Task publishing = lease.PublishAdmissionStateAsync(
                    CreateAdmissionState(),
                    cancellation.Token)
                .AsTask();
            CancellationToken linkedToken = await channel.Entered.Task.WaitAsync(
                TimeSpan.FromSeconds(5));
            Assert.NotEqual(cancellation.Token, linkedToken);
            cancellation.Cancel();

            OperationCanceledException failure = await Assert.ThrowsAnyAsync<
                OperationCanceledException>(async () => await publishing);

            Assert.Equal(cancellation.Token, failure.CancellationToken);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task ReservedPrepareCallerCancellationRestoresOriginalTokenAcrossLease()
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
            var channel = new BlockingReservedPreparationChannel();
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                channel,
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            using var cancellation = new CancellationTokenSource();

            Task preparing = lease.PrepareReservedAsync(
                    CreateHostPreparation(),
                    new RecordingHostPreparationAdmission(),
                    cancellation.Token)
                .AsTask();
            CancellationToken linkedToken = await channel.Entered.Task.WaitAsync(
                TimeSpan.FromSeconds(5));
            Assert.NotEqual(cancellation.Token, linkedToken);
            cancellation.Cancel();

            OperationCanceledException failure = await Assert.ThrowsAnyAsync<
                OperationCanceledException>(async () => await preparing);

            Assert.Equal(cancellation.Token, failure.CancellationToken);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task ReservedPrepareForeignCancellationIsNotRelabeled()
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
            using var callerCancellation = new CancellationTokenSource();
            using var foreignCancellation = new CancellationTokenSource();
            var injected = new OperationCanceledException(
                "reserved prepare foreign cancellation canary",
                innerException: null,
                foreignCancellation.Token);
            var channel = new ThrowingReservedPreparationChannel(
                callerCancellation,
                injected);
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                channel,
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);

            OperationCanceledException failure = await Assert.ThrowsAnyAsync<
                OperationCanceledException>(async () =>
                await lease.PrepareReservedAsync(
                    CreateHostPreparation(),
                    new RecordingHostPreparationAdmission(),
                    callerCancellation.Token));

            Assert.Same(injected, failure);
            Assert.True(callerCancellation.IsCancellationRequested);
            Assert.Equal(foreignCancellation.Token, failure.CancellationToken);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task ReservedPrepareCallerCancellationSurvivesRealSessionWireLinks()
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
            var wire = new LeasePreparationWireConnection(
                LocalDeviceId,
                PeerDeviceId)
            {
                BlockUntilCancelled = true,
            };
            var session = new RemoteWindowControlSession(
                wire,
                timeProvider: TimeProvider.System);
            session.StartDispatch();
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                session,
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            using var cancellation = new CancellationTokenSource();

            try
            {
                Task preparing = lease.PrepareReservedAsync(
                        CreateHostPreparation(),
                        new RecordingHostPreparationAdmission(),
                        cancellation.Token)
                    .AsTask();
                CancellationToken wireToken = await wire.Entered.Task.WaitAsync(
                    TimeSpan.FromSeconds(5));
                Assert.NotEqual(cancellation.Token, wireToken);
                cancellation.Cancel();

                OperationCanceledException failure = await Assert.ThrowsAnyAsync<
                    OperationCanceledException>(async () => await preparing);

                Assert.Equal(cancellation.Token, failure.CancellationToken);
                Assert.Equal(1, wire.SendCount);
            }
            finally
            {
                session.Cancel();
                await session.StopDispatchAsync().AsTask().WaitAsync(
                    TimeSpan.FromSeconds(5));
                await session.DisposeAsync();
                Assert.Null(generation.RevokeAndReleaseOwner());
            }
        }
    }

    [Fact]
    public async Task ReservedPrepareForeignWireCancellationKeepsIdentity()
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
            using var callerCancellation = new CancellationTokenSource();
            using var foreignCancellation = new CancellationTokenSource();
            var injected = new OperationCanceledException(
                "real session foreign wire cancellation canary",
                innerException: null,
                foreignCancellation.Token);
            var wire = new LeasePreparationWireConnection(
                LocalDeviceId,
                PeerDeviceId)
            {
                CallerCancellation = callerCancellation,
                Failure = injected,
            };
            var session = new RemoteWindowControlSession(
                wire,
                timeProvider: TimeProvider.System);
            session.StartDispatch();
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                session,
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);

            try
            {
                OperationCanceledException failure = await Assert.ThrowsAnyAsync<
                    OperationCanceledException>(async () =>
                    await lease.PrepareReservedAsync(
                        CreateHostPreparation(),
                        new RecordingHostPreparationAdmission(),
                        callerCancellation.Token));

                Assert.Same(injected, failure);
                Assert.Equal(foreignCancellation.Token, failure.CancellationToken);
                Assert.True(callerCancellation.IsCancellationRequested);
                Assert.Equal(1, wire.SendCount);
            }
            finally
            {
                session.Cancel();
                await session.StopDispatchAsync().AsTask().WaitAsync(
                    TimeSpan.FromSeconds(5));
                await session.DisposeAsync();
                Assert.Null(generation.RevokeAndReleaseOwner());
            }
        }
    }

    [Fact]
    public async Task ForeignAdmissionCancellationIsNotRelabeledAsCallerCancellation()
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
            using var callerCancellation = new CancellationTokenSource();
            using var foreignCancellation = new CancellationTokenSource();
            var injected = new OperationCanceledException(
                "FLOWSPAN_FOREIGN_LEASE_ADMISSION_CANARY",
                innerException: null,
                foreignCancellation.Token);
            var channel = new ThrowingAdmissionPreparationChannel(
                callerCancellation,
                injected);
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                channel,
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);

            OperationCanceledException failure = await Assert.ThrowsAnyAsync<
                OperationCanceledException>(async () =>
                await lease.PublishAdmissionStateAsync(
                    CreateAdmissionState(),
                    callerCancellation.Token));

            Assert.Same(injected, failure);
            Assert.True(callerCancellation.IsCancellationRequested);
            Assert.Equal(foreignCancellation.Token, failure.CancellationToken);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

    [Fact]
    public async Task DeferredFailCloseCommitInvalidatesPreparationBeforeDeadline()
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
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                static () => ValueTask.CompletedTask,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            await using AuthenticatedRemoteWindowConnectionLease lease =
                Assert.IsType<AuthenticatedRemoteWindowConnectionLease>(acquired);
            var sink = new RecordingConnectionPreparationSink();
            AuthenticatedRemoteWindowConnectionPreparationReservationResult result =
                lease.TryReservePreparation(sink);

            Assert.True(lease.TryDeferFailCloseUntilPreparationDeadline(
                CreateRequest(now.AddSeconds(1))));

            Assert.Equal(1, sink.InvalidationCount);
            Assert.False(Assert.IsAssignableFrom<
                IAuthenticatedRemoteWindowConnectionPreparationRegistration>(
                result.Registration).IsCurrent);
            Assert.False(lease.IsCurrent);
            Assert.Null(generation.RevokeAndReleaseOwner());
        }
    }

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
            using IDisposable registration =
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
            using IDisposable registration =
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

    private static RemoteWindowPreparationRequest CreateHostPreparation()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        deadline = deadline.AddTicks(
            -(deadline.Ticks % TimeSpan.TicksPerMillisecond));
        return RemoteWindowPreparationRequest.Create(
            CorrelationId.From(
                Guid.Parse("66666666-6666-6666-6666-666666666666")),
            SessionId,
            ActivityId,
            LocalDeviceId,
            PeerDeviceId,
            MirrorParticipantRole.ViewOnly,
            deadline);
    }

    private static RemoteWindowParticipantState CreateAdmissionState() =>
        RemoteWindowParticipantState.Create(
            CorrelationId.From(
                Guid.Parse("55555555-5555-5555-5555-555555555555")),
            SessionId,
            ActivityId,
            LocalDeviceId,
            PeerDeviceId,
            RemoteWindowControlAction.Admission,
            RemoteWindowControlOutcome.Applied,
            "participant_admitted",
            RemoteWindowLifecycle.Active,
            RemoteWindowCaptureState.Capturing,
            participantCount: 1,
            MirrorParticipantRole.ViewOnly,
            LocalDeviceId,
            driverLeaseEpoch: 1,
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            ProtectionKind.Safe,
            revision: 1);

    private sealed class ManualTimeProvider(
        DateTimeOffset utcNow,
        bool throwOnTimerDispose = false,
        Exception? timerDisposeFailure = null) : TimeProvider
    {
        private readonly Lock gate = new();
        private readonly bool throwOnTimerDispose = throwOnTimerDispose;
        private readonly Exception? timerDisposeFailure = timerDisposeFailure;
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

                if (owner.timerDisposeFailure is { } failure)
                {
                    global::System.Runtime.ExceptionServices.ExceptionDispatchInfo
                        .Capture(failure)
                        .Throw();
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

    private sealed class RecordingConnectionPreparationSink(
        Action? invalidated = null,
        Action? owning = null) :
        IAuthenticatedRemoteWindowConnectionPreparationInvalidationSink
    {
        private int invalidationCount;

        public int InvalidationCount => Volatile.Read(ref invalidationCount);

        public IAuthenticatedRemoteWindowConnectionPreparationRegistration?
            OwnedRegistration
        { get; private set; }

        public void InvalidateAuthenticatedRemoteWindowConnectionPreparationNow()
        {
            Interlocked.Increment(ref invalidationCount);
            invalidated?.Invoke();
        }

        public void OwnAuthenticatedRemoteWindowConnectionPreparationRegistration(
            IAuthenticatedRemoteWindowConnectionPreparationRegistration registration)
        {
            OwnedRegistration = registration;
            owning?.Invoke();
        }
    }

    private sealed class RecordingDisposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
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

    private sealed class RecordingNonReservedPreparationChannel :
        IRemoteWindowPreparationChannel
    {
        private int prepareCount;

        public DeviceId ParticipantDeviceId => PeerDeviceId;

        public int PrepareCount => Volatile.Read(ref prepareCount);

        public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref prepareCount);
            return ValueTask.FromResult(
                RemoteWindowPreparationDeliveryResult.Acknowledged(
                    RemoteWindowPreparationResponse.Create(
                        request,
                        RemoteWindowPreparationOutcome.Ready,
                        "participant_ready")));
        }

        public ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingHostPreparationAdmission :
        IRemoteWindowHostPreparationAdmission
    {
        private int routeAdmissionCount;
        private int routeFailureCount;

        public bool BlockRouteCompletion { get; init; }

        public TaskCompletionSource RouteCompletionEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseRouteCompletion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? RouteCompletionFailure { get; init; }

        public int RouteAdmissionCount => Volatile.Read(ref routeAdmissionCount);

        public int RouteFailureCount => Volatile.Read(ref routeFailureCount);

        public bool TryAdmitRouteSelection(DateTimeOffset now)
        {
            Interlocked.Increment(ref routeAdmissionCount);
            return true;
        }

        public bool CompleteRouteSelection()
        {
            RouteCompletionEntered.TrySetResult();
            if (BlockRouteCompletion)
            {
                ReleaseRouteCompletion.Task.GetAwaiter().GetResult();
            }

            if (RouteCompletionFailure is { } failure)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(failure)
                    .Throw();
            }

            return true;
        }

        public bool TryFailRouteSelection()
        {
            Interlocked.Increment(ref routeFailureCount);
            return true;
        }

        public bool TryAdmitPrepareSend(
            RemoteWindowPreparationRequest request,
            DateTimeOffset now) => true;
    }

    private sealed class BlockingReservedPreparationChannel :
        IRemoteWindowPreparationChannel,
        IReservedRemoteWindowPreparationChannel
    {
        public TaskCompletionSource<CancellationToken> Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public DeviceId ParticipantDeviceId => PeerDeviceId;

        public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async ValueTask<RemoteWindowPreparationDeliveryResult>
            PrepareReservedAsync(
            RemoteWindowPreparationRequest request,
            IRemoteWindowHostPreparationAdmission admission,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult(cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return RemoteWindowPreparationDeliveryResult.NotDelivered;
        }

        public ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class AdmissionRecordingReservedPreparationChannel :
        IRemoteWindowPreparationChannel,
        IReservedRemoteWindowPreparationChannel
    {
        private int wireAdmissionCount;

        public DeviceId ParticipantDeviceId => PeerDeviceId;

        public int WireAdmissionCount => Volatile.Read(ref wireAdmissionCount);

        public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<RemoteWindowPreparationDeliveryResult>
            PrepareReservedAsync(
                RemoteWindowPreparationRequest request,
                IRemoteWindowHostPreparationAdmission admission,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!admission.TryAdmitPrepareSend(request, DateTimeOffset.UtcNow))
            {
                return ValueTask.FromResult(
                    RemoteWindowPreparationDeliveryResult.NotDelivered);
            }

            Interlocked.Increment(ref wireAdmissionCount);
            return ValueTask.FromResult(
                RemoteWindowPreparationDeliveryResult.Acknowledged(
                    RemoteWindowPreparationResponse.Create(
                        request,
                        RemoteWindowPreparationOutcome.Ready,
                        "participant_ready")));
        }

        public ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class GatedReservedPreparationChannel :
        IRemoteWindowPreparationChannel,
        IReservedRemoteWindowPreparationChannel
    {
        private int publicPrepareCount;
        private int reservedPrepareCount;
        private int wireAdmissionCount;
        private int wireSendCount;

        public DeviceId ParticipantDeviceId => PeerDeviceId;

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int PublicPrepareCount => Volatile.Read(ref publicPrepareCount);

        public TaskCompletionSource ReleaseWireAdmission { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReservedPrepareCount => Volatile.Read(ref reservedPrepareCount);

        public int WireAdmissionCount => Volatile.Read(ref wireAdmissionCount);

        public int WireSendCount => Volatile.Read(ref wireSendCount);

        public async ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref publicPrepareCount);
            Entered.TrySetResult();
            await ReleaseWireAdmission.Task.WaitAsync(cancellationToken);
            Interlocked.Increment(ref wireSendCount);
            return CreateAcknowledgedResult(request);
        }

        public async ValueTask<RemoteWindowPreparationDeliveryResult>
            PrepareReservedAsync(
                RemoteWindowPreparationRequest request,
                IRemoteWindowHostPreparationAdmission admission,
                CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref reservedPrepareCount);
            Entered.TrySetResult();
            await ReleaseWireAdmission.Task.WaitAsync(cancellationToken);
            Interlocked.Increment(ref wireAdmissionCount);
            if (!admission.TryAdmitPrepareSend(request, DateTimeOffset.UtcNow))
            {
                return RemoteWindowPreparationDeliveryResult.NotDelivered;
            }

            Interlocked.Increment(ref wireSendCount);
            return CreateAcknowledgedResult(request);
        }

        public ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static RemoteWindowPreparationDeliveryResult
            CreateAcknowledgedResult(RemoteWindowPreparationRequest request) =>
            RemoteWindowPreparationDeliveryResult.Acknowledged(
                RemoteWindowPreparationResponse.Create(
                    request,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"));
    }

    private sealed class ThrowingReservedPreparationChannel(
        CancellationTokenSource callerCancellation,
        OperationCanceledException failure) :
        IRemoteWindowPreparationChannel,
        IReservedRemoteWindowPreparationChannel
    {
        public DeviceId ParticipantDeviceId => PeerDeviceId;

        public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<RemoteWindowPreparationDeliveryResult>
            PrepareReservedAsync(
            RemoteWindowPreparationRequest request,
            IRemoteWindowHostPreparationAdmission admission,
            CancellationToken cancellationToken)
        {
            callerCancellation.Cancel();
            return ValueTask.FromException<RemoteWindowPreparationDeliveryResult>(
                failure);
        }

        public ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class LeasePreparationWireConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) : IRemoteWindowControlConnection
    {
        private int sendCount;

        public bool BlockReadUntilCancelled { get; init; }

        public bool BlockUntilCancelled { get; init; }

        public CancellationTokenSource? CallerCancellation { get; init; }

        public TaskCompletionSource<CancellationToken> Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? Failure { get; init; }

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } =
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion;

        public int SendCount => Volatile.Read(ref sendCount);

        public async ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            if (BlockReadUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            throw new NotSupportedException();
        }

        public async ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref sendCount);
            Entered.TrySetResult(cancellationToken);
            CallerCancellation?.Cancel();
            if (Failure is { } failure)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(failure)
                    .Throw();
            }

            if (BlockUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }
    }

    private sealed class BlockingAdmissionPreparationChannel :
        IRemoteWindowPreparationChannel
    {
        public TaskCompletionSource<CancellationToken> Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public DeviceId ParticipantDeviceId => PeerDeviceId;

        public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult(cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class ThrowingAdmissionPreparationChannel(
        CancellationTokenSource callerCancellation,
        OperationCanceledException failure) :
        IRemoteWindowPreparationChannel
    {
        public DeviceId ParticipantDeviceId => PeerDeviceId;

        public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken)
        {
            callerCancellation.Cancel();
            return ValueTask.FromException(failure);
        }
    }
}
