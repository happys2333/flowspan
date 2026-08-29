using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Flowspan.Platform;

namespace Flowspan.Platform.Tests;

public sealed class NativeRemoteWindowContractsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UnavailablePermissionBoundaryNeverManufacturesGrant()
    {
        UnavailableNativeRemoteWindowPermissionBoundary boundary =
            UnavailableNativeRemoteWindowPermissionBoundary.Instance;

        NativeRemoteWindowPermissionSnapshot initial = boundary.GetSnapshot();
        NativeRemoteWindowPermissionSnapshot capture =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);
        NativeRemoteWindowPermissionSnapshot input =
            await boundary.RequestInputPermissionAsync(CancellationToken.None);

        Assert.Equal(NativeRemoteWindowPermissionState.Unsupported, initial.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Unsupported, initial.Input);
        Assert.Equal(1, initial.OwnerGeneration);
        Assert.Same(initial, capture);
        Assert.Same(initial, input);
    }

    [Fact]
    public async Task UnavailablePermissionBoundaryHonorsCancellation()
    {
        UnavailableNativeRemoteWindowPermissionBoundary boundary =
            UnavailableNativeRemoteWindowPermissionBoundary.Instance;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await boundary.RequestCapturePermissionAsync(
                cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await boundary.RequestInputPermissionAsync(
                cancellation.Token));
    }

    [Fact]
    public void NativePlatformContractsCanBeConstructedByAnExternalAssembly()
    {
        NativeRemoteWindowProtectionObservation observation =
            NativeRemoteWindowProtectionObservation.Create(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now,
                    "external-probe"),
                ownerGeneration: 3,
                sessionGeneration: 5,
                sourceGeneration: 4,
                revision: 6);
        LocalEmergencyStopActivation activation =
            LocalEmergencyStopActivation.Create(
                ownerGeneration: 3,
                sessionGeneration: 5,
                sequence: 7,
                LocalEmergencyStopCause.RegistrationLost);
        using var registration = new ExternalEmergencyStopRegistration(
            ownerGeneration: 3,
            sessionGeneration: 5);
        LocalEmergencyStopRegistrationResult registrationResult =
            LocalEmergencyStopRegistrationResult.Confirmed(
                registration,
                "external_registration_confirmed");

        Assert.Equal(5, observation.SessionGeneration);
        Assert.Equal(4, observation.SourceGeneration);
        Assert.Equal(LocalEmergencyStopCause.RegistrationLost, activation.Cause);
        Assert.True(registrationResult.Registered);
        registration.Dispose();
        Assert.False(registrationResult.Registered);
        Assert.False(
            LocalEmergencyStopRegistrationResult.Rejected(
                "external_registration_failed").Registered);
    }

    [Fact]
    public void DisposingCapturedFrameReleasesOwnerOnceAndPreventsPixelReuse()
    {
        var owner = new RecordingMemoryOwner(32);
        NativeRemoteWindowFrame frame = NativeRemoteWindowFrame.TakeOwnership(
            owner,
            payloadLength: 32,
            width: 4,
            height: 2,
            stride: 16,
            NativeRemoteWindowPixelFormat.Bgra8888,
            ownerGeneration: 3,
            sessionGeneration: 8,
            sourceGeneration: 4,
            geometryRevision: 5,
            sequence: 6);

        Assert.Equal(32, frame.Pixels.Length);
        Assert.Equal(3, frame.OwnerGeneration);
        Assert.Equal(8, frame.SessionGeneration);
        Assert.Equal(4, frame.SourceGeneration);
        Assert.Equal(5, frame.GeometryRevision);
        Assert.Equal(6, frame.Sequence);
        Assert.DoesNotContain("System.Byte", frame.ToString());

        frame.Dispose();
        frame.Dispose();

        Assert.Equal(1, owner.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => _ = frame.Pixels);
    }

    [Fact]
    public void CapturedFrameJsonOmitsOwnedPixelPlane()
    {
        var owner = new RecordingMemoryOwner(4);
        "FLOW"u8.CopyTo(owner.Memory.Span);
        using NativeRemoteWindowFrame frame = NativeRemoteWindowFrame.TakeOwnership(
            owner,
            payloadLength: 4,
            width: 1,
            height: 1,
            stride: 4,
            NativeRemoteWindowPixelFormat.Bgra8888,
            ownerGeneration: 3,
            sessionGeneration: 8,
            sourceGeneration: 4,
            geometryRevision: 5,
            sequence: 6);

        string serialized = JsonSerializer.Serialize(frame);

        Assert.DoesNotContain("Pixels", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RkxPVw==", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectedFrameDoesNotTakeOwnershipOfCallerBuffer()
    {
        var owner = new RecordingMemoryOwner(32);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => NativeRemoteWindowFrame.TakeOwnership(
                owner,
                payloadLength: 31,
                width: 4,
                height: 2,
                stride: 16,
                NativeRemoteWindowPixelFormat.Bgra8888,
                ownerGeneration: 3,
                sessionGeneration: 8,
                sourceGeneration: 4,
                geometryRevision: 5,
                sequence: 6));

        Assert.Equal(0, owner.DisposeCount);
        owner.Dispose();
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public void FrameRejectsMissingSessionGenerationWithoutTakingOwnership()
    {
        var owner = new RecordingMemoryOwner(32);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => NativeRemoteWindowFrame.TakeOwnership(
                owner,
                payloadLength: 32,
                width: 4,
                height: 2,
                stride: 16,
                NativeRemoteWindowPixelFormat.Bgra8888,
                ownerGeneration: 3,
                sessionGeneration: 0,
                sourceGeneration: 4,
                geometryRevision: 5,
                sequence: 6));

        Assert.Equal(0, owner.DisposeCount);
        owner.Dispose();
    }

    [Fact]
    public void ProtectionSourceCommitsBeforeObserversAndRejectsLateCallbacks()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        int deliveries = 0;
        source.Changed += observation =>
        {
            Assert.True(
                source.TryGetLatest(
                    out NativeRemoteWindowProtectionObservation? current));
            Assert.Same(observation, current);
            throw new InvalidOperationException("observer_failure_canary");
        };
        source.Changed += _ => deliveries++;

        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now,
                    "test-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? published));
        Assert.Equal(3, published?.OwnerGeneration);
        Assert.Equal(5, published?.SessionGeneration);
        Assert.Equal(4, published?.SourceGeneration);
        Assert.Equal(1, published?.Revision);
        Assert.Equal(1, deliveries);

        source.Dispose();

        Assert.False(
            source.TryPublish(
                new ProtectionSnapshot(ProtectionKind.Safe, Now, "late-probe")));
        Assert.False(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? afterDispose));
        Assert.Null(afterDispose);
        Assert.Equal(1, deliveries);
    }

    [Fact]
    public async Task ProtectionDisposeWaitsForAnInFlightObserver()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        using var observerEntered = new ManualResetEventSlim();
        using var releaseObserver = new ManualResetEventSlim();
        using var disposeStarted = new ManualResetEventSlim();
        using var disposeReturned = new ManualResetEventSlim();
        source.Changed += _ =>
        {
            observerEntered.Set();
            releaseObserver.Wait();
        };
        Task<bool> publish = RunOnDedicatedThread(
            () => source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now,
                    "test-probe")));
        Assert.True(observerEntered.Wait(TimeSpan.FromSeconds(5)));
        Task dispose = RunOnDedicatedThread(() =>
        {
            disposeStarted.Set();
            source.Dispose();
            disposeReturned.Set();
        });
        Assert.True(disposeStarted.Wait(TimeSpan.FromSeconds(5)));

        Assert.True(
            SpinWait.SpinUntil(
                () => source.CallbackDrainWaiterCount == 1,
                TimeSpan.FromSeconds(5)));
        Assert.False(disposeReturned.IsSet);
        releaseObserver.Set();
        bool publishResult = await publish;
        await dispose;

        Assert.True(publishResult);
    }

    [Fact]
    public async Task ProtectionCallbackWorkerCanDisposeSourceWithoutDeadlock()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        source.Changed += _ =>
            Task.Run(source.Dispose).GetAwaiter().GetResult();

        Task<bool> publish = RunOnDedicatedThread(
            () => source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now,
                    "test-probe")));

        Assert.True(await publish.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? afterDispose));
        Assert.Null(afterDispose);
        source.Dispose();
    }

    [Fact]
    public async Task ConcurrentProtectionObserversCanDisposeEachOthersSources()
    {
        var first = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        var second = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 6);
        using var observersEntered = new CountdownEvent(2);
        using var releaseCrossDisposals = new ManualResetEventSlim();
        using var firstObserverReturned = new ManualResetEventSlim();
        using var secondObserverReturned = new ManualResetEventSlim();
        Exception? firstObserverFailure = null;
        Exception? secondObserverFailure = null;
        first.Changed += _ =>
        {
            try
            {
                observersEntered.Signal();
                releaseCrossDisposals.Wait();
                second.Dispose();
            }
            catch (Exception exception)
            {
                firstObserverFailure = exception;
            }
            finally
            {
                firstObserverReturned.Set();
            }
        };
        second.Changed += _ =>
        {
            try
            {
                observersEntered.Signal();
                releaseCrossDisposals.Wait();
                first.Dispose();
            }
            catch (Exception exception)
            {
                secondObserverFailure = exception;
            }
            finally
            {
                secondObserverReturned.Set();
            }
        };

        Task<bool> firstPublish = RunOnDedicatedThread(
            () => first.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now,
                    "first-probe")));
        Task<bool> secondPublish = RunOnDedicatedThread(
            () => second.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.ProtectedContent,
                    Now,
                    "second-probe")));

        try
        {
            Assert.True(observersEntered.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            releaseCrossDisposals.Set();
        }

        bool[] published = await Task.WhenAll(firstPublish, secondPublish)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(published, Assert.True);
        Assert.True(firstObserverReturned.IsSet);
        Assert.True(secondObserverReturned.IsSet);
        Assert.Null(firstObserverFailure);
        Assert.Null(secondObserverFailure);
        Assert.False(
            first.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? firstAfterDispose));
        Assert.Null(firstAfterDispose);
        Assert.False(
            second.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? secondAfterDispose));
        Assert.Null(secondAfterDispose);
        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public async Task StaleProtectionCallbackContextWaitsForLaterObserver()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        using var releaseWorker = new ManualResetEventSlim();
        using var workerReturned = new ManualResetEventSlim();
        using var secondObserverEntered = new ManualResetEventSlim();
        using var releaseSecondObserver = new ManualResetEventSlim();
        Task? disposal = null;
        int callbacks = 0;
        source.Changed += _ =>
        {
            if (Interlocked.Increment(ref callbacks) == 1)
            {
                disposal = Task.Run(() =>
                {
                    releaseWorker.Wait();
                    source.Dispose();
                    workerReturned.Set();
                });
                return;
            }

            secondObserverEntered.Set();
            releaseSecondObserver.Wait();
        };
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now,
                    "first-probe")));
        Task<bool> secondPublish = RunOnDedicatedThread(
            () => source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.ProtectedContent,
                    Now.AddMilliseconds(1),
                    "second-probe")));
        Assert.True(secondObserverEntered.Wait(TimeSpan.FromSeconds(5)));

        releaseWorker.Set();
        Assert.True(
            SpinWait.SpinUntil(
                () => source.CallbackDrainWaiterCount == 1,
                TimeSpan.FromSeconds(5)));
        Assert.False(workerReturned.IsSet);
        releaseSecondObserver.Set();
        Assert.True(await secondPublish.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.IsType<Task>(disposal).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(workerReturned.IsSet);
        source.Dispose();
    }

    [Fact]
    public async Task UnsafeProtectionCommitsWhileEarlierObserverIsBlocked()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        using var firstObserverEntered = new ManualResetEventSlim();
        using var releaseFirstObserver = new ManualResetEventSlim();
        using var selfDisposeReturned = new ManualResetEventSlim();
        int callback = 0;
        source.Changed += observation =>
        {
            Interlocked.Increment(ref callback);
            if (observation.Revision == 1)
            {
                firstObserverEntered.Set();
                releaseFirstObserver.Wait();
                return;
            }

            source.Dispose();
            selfDisposeReturned.Set();
        };
        Task<bool> firstPublish = RunOnDedicatedThread(
            () => source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now,
                    "first-probe")));
        Assert.True(firstObserverEntered.Wait(TimeSpan.FromSeconds(5)));
        Task<bool> secondPublish = RunOnDedicatedThread(
            () => source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.ProtectedContent,
                    Now,
                    "second-probe")));

        Assert.True(await secondPublish.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? latest));
        Assert.Equal(ProtectionKind.ProtectedContent, latest?.Protection.Kind);
        Assert.Equal(2, latest?.Revision);
        Assert.Equal(1, Volatile.Read(ref callback));
        releaseFirstObserver.Set();

        Assert.True(await firstPublish.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(selfDisposeReturned.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, Volatile.Read(ref callback));
        Assert.False(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? afterDispose));
        Assert.Null(afterDispose);
    }

    [Fact]
    public async Task ProtectionNotificationOverflowCoalescesToFailClosedUnknown()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        using var firstObserverEntered = new ManualResetEventSlim();
        using var releaseFirstObserver = new ManualResetEventSlim();
        var delivered = new List<NativeRemoteWindowProtectionObservation>();
        source.Changed += observation =>
        {
            lock (delivered)
            {
                delivered.Add(observation);
            }

            if (observation.Revision == 1)
            {
                firstObserverEntered.Set();
                releaseFirstObserver.Wait();
            }
        };
        Task<bool> firstPublish = RunOnDedicatedThread(
            () => source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "first-probe")));
        Assert.True(firstObserverEntered.Wait(TimeSpan.FromSeconds(5)));

        for (int index = 0;
            index < InMemoryNativeProtectionSource.MaximumPendingNotifications + 4;
            index++)
        {
            Assert.True(
                source.TryPublish(
                    new ProtectionSnapshot(
                        ProtectionKind.Safe,
                        Now.AddMilliseconds(index + 1),
                        "queued-probe")));
        }

        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? overflowed));
        Assert.Equal(ProtectionKind.Unknown, overflowed?.Protection.Kind);
        Assert.Equal("notification_overflow", overflowed?.Protection.Source);
        releaseFirstObserver.Set();
        Assert.True(await firstPublish.WaitAsync(TimeSpan.FromSeconds(5)));

        lock (delivered)
        {
            Assert.Contains(
                delivered,
                static observation =>
                    observation.Protection.Kind == ProtectionKind.Unknown);
            Assert.InRange(
                delivered.Count,
                2,
                InMemoryNativeProtectionSource.MaximumPendingNotifications + 1);
        }

        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now.AddSeconds(1),
                    "fresh-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? recovered));
        Assert.Equal(ProtectionKind.Safe, recovered?.Protection.Kind);
    }

    [Fact]
    public void ProtectionSelfDisposeStopsSiblingObserverDelivery()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        int siblingCalls = 0;
        source.Changed += _ => source.Dispose();
        source.Changed += _ => siblingCalls++;

        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now,
                    "test-probe")));

        Assert.Equal(0, siblingCalls);
        source.Dispose();
    }

    [Fact]
    public async Task ProtectionDisposeFromNestedCallbackDoesNotWaitForAncestorDrain()
    {
        var ancestor = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        var nested = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 6);
        bool ancestorDisposed = false;
        bool nestedPublished = false;
        int siblingCalls = 0;
        ancestor.Changed += _ => nestedPublished = nested.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.ProtectedContent,
                    Now,
                    "nested-probe"));
        ancestor.Changed += _ => siblingCalls++;
        nested.Changed += _ =>
        {
            ancestor.Dispose();
            ancestorDisposed = true;
        };

        Task<bool> publish = RunOnDedicatedThread(
            () => ancestor.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now,
                    "ancestor-probe")));

        Assert.True(await publish.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(nestedPublished);
        Assert.True(ancestorDisposed);
        Assert.Equal(0, siblingCalls);
        Assert.False(
            ancestor.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? afterDispose));
        Assert.Null(afterDispose);

        ancestor.Dispose();
        nested.Dispose();
    }

    [Fact]
    public void EmergencyRegistrationClosesBeforeCallbackAndIsOneShot()
    {
        using var registrar = new InMemoryLocalEmergencyStopRegistrar();
        ILocalEmergencyStopRegistration? registration = null;
        LocalEmergencyStopActivation? observed = null;
        LocalEmergencyStopRegistrationResult result = registrar.TryRegister(
            ownerGeneration: 7,
            sessionGeneration: 9,
            activation =>
            {
                Assert.False(registration?.IsCurrent);
                observed = activation;
                throw new InvalidOperationException("stop_callback_failure_canary");
            });
        registration = Assert.IsAssignableFrom<ILocalEmergencyStopRegistration>(
            result.Registration);

        Assert.True(result.Registered);
        Assert.True(registration.IsCurrent);
        Assert.Equal(9, registration.SessionGeneration);
        Assert.True(registrar.Trigger());
        Assert.False(registration.IsCurrent);
        Assert.Equal(7, observed?.OwnerGeneration);
        Assert.Equal(9, observed?.SessionGeneration);
        Assert.Equal(LocalEmergencyStopCause.UserAction, observed?.Cause);
        Assert.Equal(1, observed?.Sequence);
        Assert.False(registrar.Trigger());

        registration.Dispose();
    }

    [Fact]
    public void EmergencyReadinessTracksConflictReleaseAndRegistrarDisposal()
    {
        var registrar = new InMemoryLocalEmergencyStopRegistrar();

        LocalBoundaryResult initial = registrar.CheckReadiness();
        LocalEmergencyStopRegistrationResult registered = registrar.TryRegister(
            ownerGeneration: 1,
            sessionGeneration: 1,
            _ => { });
        using ILocalEmergencyStopRegistration registration = Assert.IsAssignableFrom<
            ILocalEmergencyStopRegistration>(registered.Registration);
        LocalBoundaryResult conflict = registrar.CheckReadiness();
        registration.Dispose();
        LocalBoundaryResult released = registrar.CheckReadiness();
        registrar.Dispose();
        LocalBoundaryResult unavailable = registrar.CheckReadiness();

        Assert.True(initial.Succeeded);
        Assert.Equal("emergency_stop_ready", initial.ReasonCode);
        Assert.False(conflict.Succeeded);
        Assert.Equal("emergency_stop_registration_conflict", conflict.ReasonCode);
        Assert.True(released.Succeeded);
        Assert.Equal("emergency_stop_ready", released.ReasonCode);
        Assert.False(unavailable.Succeeded);
        Assert.Equal(
            "emergency_stop_registrar_unavailable",
            unavailable.ReasonCode);
    }

    [Fact]
    public void EmergencyRegistrationLossTriggersNamedFailClosedActivation()
    {
        using var registrar = new InMemoryLocalEmergencyStopRegistrar();
        LocalEmergencyStopActivation? observed = null;
        LocalEmergencyStopRegistrationResult result = registrar.TryRegister(
            ownerGeneration: 7,
            sessionGeneration: 9,
            activation => observed = activation);
        using ILocalEmergencyStopRegistration registration =
            Assert.IsAssignableFrom<ILocalEmergencyStopRegistration>(
                result.Registration);

        Assert.True(registrar.LoseRegistration());

        Assert.False(registration.IsCurrent);
        Assert.Equal(LocalEmergencyStopCause.RegistrationLost, observed?.Cause);
        Assert.Equal(7, observed?.OwnerGeneration);
        Assert.Equal(9, observed?.SessionGeneration);
        Assert.False(registrar.LoseRegistration());
    }

    [Fact]
    public void EmergencyRegistrarReportsConflictWithoutReplacingCurrentAction()
    {
        using var registrar = new InMemoryLocalEmergencyStopRegistrar();
        LocalEmergencyStopRegistrationResult first = registrar.TryRegister(
            ownerGeneration: 1,
            sessionGeneration: 1,
            _ => { });
        ILocalEmergencyStopRegistration registration = Assert.IsAssignableFrom<
            ILocalEmergencyStopRegistration>(first.Registration);

        LocalEmergencyStopRegistrationResult conflict = registrar.TryRegister(
            ownerGeneration: 2,
            sessionGeneration: 2,
            _ => { });

        Assert.False(conflict.Registered);
        Assert.Equal(
            "emergency_stop_registration_conflict",
            conflict.Boundary.ReasonCode);
        Assert.Null(conflict.Registration);
        Assert.True(registration.IsCurrent);

        registration.Dispose();
        LocalEmergencyStopRegistrationResult replacement = registrar.TryRegister(
            ownerGeneration: 2,
            sessionGeneration: 2,
            _ => { });
        Assert.True(replacement.Registered);
        replacement.Registration?.Dispose();
    }

    [Fact]
    public async Task EmergencyRegistrarRejectsReplacementUntilCallbackDrains()
    {
        using var registrar = new InMemoryLocalEmergencyStopRegistrar();
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        LocalEmergencyStopRegistrationResult first = registrar.TryRegister(
            ownerGeneration: 1,
            sessionGeneration: 1,
            _ =>
            {
                callbackEntered.Set();
                releaseCallback.Wait();
            });
        using ILocalEmergencyStopRegistration registration =
            Assert.IsAssignableFrom<ILocalEmergencyStopRegistration>(
                first.Registration);
        Task<bool> trigger = RunOnDedicatedThread(registrar.Trigger);
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));

        LocalEmergencyStopRegistrationResult conflict = registrar.TryRegister(
            ownerGeneration: 2,
            sessionGeneration: 2,
            _ => { });
        releaseCallback.Set();

        Assert.True(await trigger);
        Assert.False(conflict.Registered);
        Assert.Equal(
            "emergency_stop_registration_conflict",
            conflict.Boundary.ReasonCode);
        LocalEmergencyStopRegistrationResult replacement = registrar.TryRegister(
            ownerGeneration: 2,
            sessionGeneration: 2,
            _ => { });
        Assert.True(replacement.Registered);
        replacement.Registration?.Dispose();
    }

    [Fact]
    public async Task EmergencyRegistrarDisposeWaitsForCallbackAndSelfDisposeDoesNotDeadlock()
    {
        var registrar = new InMemoryLocalEmergencyStopRegistrar();
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        using var disposeStarted = new ManualResetEventSlim();
        using var disposeReturned = new ManualResetEventSlim();
        ILocalEmergencyStopRegistration? registration = null;
        LocalEmergencyStopRegistrationResult result = registrar.TryRegister(
            ownerGeneration: 1,
            sessionGeneration: 1,
            _ =>
            {
                callbackEntered.Set();
                releaseCallback.Wait();
                registration?.Dispose();
            });
        registration = Assert.IsAssignableFrom<ILocalEmergencyStopRegistration>(
            result.Registration);
        Task<bool> trigger = RunOnDedicatedThread(registrar.Trigger);
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));
        Task dispose = RunOnDedicatedThread(() =>
        {
            disposeStarted.Set();
            registrar.Dispose();
            disposeReturned.Set();
        });
        Assert.True(disposeStarted.Wait(TimeSpan.FromSeconds(5)));

        Assert.True(
            SpinWait.SpinUntil(
                () => registrar.CallbackDrainWaiterCount == 1,
                TimeSpan.FromSeconds(5)));
        Assert.False(disposeReturned.IsSet);
        releaseCallback.Set();

        bool triggered = await trigger.WaitAsync(TimeSpan.FromSeconds(5));
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(triggered);
        registration.Dispose();
        registrar.Dispose();
    }

    [Fact]
    public async Task EmergencyCallbackWorkerCanDisposeRegistrationWithoutDeadlock()
    {
        using var registrar = new InMemoryLocalEmergencyStopRegistrar();
        ILocalEmergencyStopRegistration? registration = null;
        LocalEmergencyStopRegistrationResult result = registrar.TryRegister(
            ownerGeneration: 1,
            sessionGeneration: 1,
            _ => Task.Run(() => registration!.Dispose())
                .GetAwaiter()
                .GetResult());
        registration = Assert.IsAssignableFrom<ILocalEmergencyStopRegistration>(
            result.Registration);

        Task<bool> trigger = RunOnDedicatedThread(registrar.Trigger);

        Assert.True(await trigger.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(registration.IsCurrent);
        registration.Dispose();
    }

    [Fact]
    public async Task ConcurrentEmergencyCallbacksCanDisposeEachOthersRegistrations()
    {
        var firstRegistrar = new InMemoryLocalEmergencyStopRegistrar();
        var secondRegistrar = new InMemoryLocalEmergencyStopRegistrar();
        using var callbacksEntered = new CountdownEvent(2);
        using var releaseCrossDisposals = new ManualResetEventSlim();
        using var firstCallbackReturned = new ManualResetEventSlim();
        using var secondCallbackReturned = new ManualResetEventSlim();
        Exception? firstCallbackFailure = null;
        Exception? secondCallbackFailure = null;
        ILocalEmergencyStopRegistration? firstRegistration = null;
        ILocalEmergencyStopRegistration? secondRegistration = null;
        LocalEmergencyStopRegistrationResult firstResult =
            firstRegistrar.TryRegister(
                ownerGeneration: 1,
                sessionGeneration: 1,
                _ =>
                {
                    try
                    {
                        callbacksEntered.Signal();
                        releaseCrossDisposals.Wait();
                        secondRegistration!.Dispose();
                    }
                    catch (Exception exception)
                    {
                        firstCallbackFailure = exception;
                    }
                    finally
                    {
                        firstCallbackReturned.Set();
                    }
                });
        firstRegistration = Assert.IsAssignableFrom<
            ILocalEmergencyStopRegistration>(firstResult.Registration);
        LocalEmergencyStopRegistrationResult secondResult =
            secondRegistrar.TryRegister(
                ownerGeneration: 2,
                sessionGeneration: 2,
                _ =>
                {
                    try
                    {
                        callbacksEntered.Signal();
                        releaseCrossDisposals.Wait();
                        firstRegistration.Dispose();
                    }
                    catch (Exception exception)
                    {
                        secondCallbackFailure = exception;
                    }
                    finally
                    {
                        secondCallbackReturned.Set();
                    }
                });
        secondRegistration = Assert.IsAssignableFrom<
            ILocalEmergencyStopRegistration>(secondResult.Registration);

        Task<bool> firstTrigger = RunOnDedicatedThread(firstRegistrar.Trigger);
        Task<bool> secondTrigger = RunOnDedicatedThread(secondRegistrar.Trigger);

        try
        {
            Assert.True(callbacksEntered.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            releaseCrossDisposals.Set();
        }

        bool[] triggered = await Task.WhenAll(firstTrigger, secondTrigger)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(triggered, Assert.True);
        Assert.True(firstCallbackReturned.IsSet);
        Assert.True(secondCallbackReturned.IsSet);
        Assert.Null(firstCallbackFailure);
        Assert.Null(secondCallbackFailure);
        Assert.False(firstRegistration.IsCurrent);
        Assert.False(secondRegistration.IsCurrent);
        firstRegistration.Dispose();
        secondRegistration.Dispose();
        firstRegistrar.Dispose();
        secondRegistrar.Dispose();
    }

    [Fact]
    public async Task ProtectionAndEmergencyCallbacksCanDisposeEachOthersBoundaries()
    {
        var protectionSource = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        var emergencyRegistrar = new InMemoryLocalEmergencyStopRegistrar();
        using var callbacksEntered = new CountdownEvent(2);
        using var releaseCrossDisposals = new ManualResetEventSlim();
        using var protectionCallbackReturned = new ManualResetEventSlim();
        using var emergencyCallbackReturned = new ManualResetEventSlim();
        Exception? protectionCallbackFailure = null;
        Exception? emergencyCallbackFailure = null;
        protectionSource.Changed += _ =>
        {
            try
            {
                callbacksEntered.Signal();
                releaseCrossDisposals.Wait();
                emergencyRegistrar.Dispose();
            }
            catch (Exception exception)
            {
                protectionCallbackFailure = exception;
            }
            finally
            {
                protectionCallbackReturned.Set();
            }
        };
        LocalEmergencyStopRegistrationResult registrationResult =
            emergencyRegistrar.TryRegister(
                ownerGeneration: 3,
                sessionGeneration: 5,
                _ =>
                {
                    try
                    {
                        callbacksEntered.Signal();
                        releaseCrossDisposals.Wait();
                        protectionSource.Dispose();
                    }
                    catch (Exception exception)
                    {
                        emergencyCallbackFailure = exception;
                    }
                    finally
                    {
                        emergencyCallbackReturned.Set();
                    }
                });
        ILocalEmergencyStopRegistration registration = Assert.IsAssignableFrom<
            ILocalEmergencyStopRegistration>(registrationResult.Registration);

        Task<bool> publish = RunOnDedicatedThread(
            () => protectionSource.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now,
                    "cross-boundary-probe")));
        Task<bool> trigger = RunOnDedicatedThread(emergencyRegistrar.Trigger);

        try
        {
            Assert.True(callbacksEntered.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            releaseCrossDisposals.Set();
        }

        bool[] completed = await Task.WhenAll(publish, trigger)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(completed, Assert.True);
        Assert.True(protectionCallbackReturned.IsSet);
        Assert.True(emergencyCallbackReturned.IsSet);
        Assert.Null(protectionCallbackFailure);
        Assert.Null(emergencyCallbackFailure);
        Assert.False(
            protectionSource.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? afterDispose));
        Assert.Null(afterDispose);
        Assert.False(registration.IsCurrent);
        LocalEmergencyStopRegistrationResult afterRegistrarDispose =
            emergencyRegistrar.TryRegister(
                ownerGeneration: 6,
                sessionGeneration: 7,
                _ => { });
        Assert.False(afterRegistrarDispose.Registered);
        Assert.Equal(
            "emergency_stop_registrar_unavailable",
            afterRegistrarDispose.Boundary.ReasonCode);
        registration.Dispose();
        protectionSource.Dispose();
        emergencyRegistrar.Dispose();
    }

    [Fact]
    public async Task StaleEmergencyCallbackContextWaitsForLaterRegistration()
    {
        var registrar = new InMemoryLocalEmergencyStopRegistrar();
        using var releaseWorker = new ManualResetEventSlim();
        using var workerReturned = new ManualResetEventSlim();
        using var secondCallbackEntered = new ManualResetEventSlim();
        using var releaseSecondCallback = new ManualResetEventSlim();
        Task? disposal = null;
        LocalEmergencyStopRegistrationResult first = registrar.TryRegister(
            ownerGeneration: 1,
            sessionGeneration: 1,
            _ => disposal = Task.Run(() =>
            {
                releaseWorker.Wait();
                registrar.Dispose();
                workerReturned.Set();
            }));
        using ILocalEmergencyStopRegistration firstRegistration =
            Assert.IsAssignableFrom<ILocalEmergencyStopRegistration>(
                first.Registration);
        Assert.True(registrar.Trigger());
        LocalEmergencyStopRegistrationResult second = registrar.TryRegister(
            ownerGeneration: 2,
            sessionGeneration: 2,
            _ =>
            {
                secondCallbackEntered.Set();
                releaseSecondCallback.Wait();
            });
        using ILocalEmergencyStopRegistration secondRegistration =
            Assert.IsAssignableFrom<ILocalEmergencyStopRegistration>(
                second.Registration);
        Task<bool> secondTrigger = RunOnDedicatedThread(registrar.Trigger);
        Assert.True(secondCallbackEntered.Wait(TimeSpan.FromSeconds(5)));

        releaseWorker.Set();
        Assert.True(
            SpinWait.SpinUntil(
                () => registrar.CallbackDrainWaiterCount == 1,
                TimeSpan.FromSeconds(5)));
        Assert.False(workerReturned.IsSet);
        releaseSecondCallback.Set();
        Assert.True(await secondTrigger.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.IsType<Task>(disposal).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(workerReturned.IsSet);
        registrar.Dispose();
    }

    [Fact]
    public async Task EmergencyRegistrationDisposeFromNestedCallbackDoesNotWaitForAncestorDrain()
    {
        var ancestorRegistrar = new InMemoryLocalEmergencyStopRegistrar();
        var nestedRegistrar = new InMemoryLocalEmergencyStopRegistrar();
        ILocalEmergencyStopRegistration? ancestorRegistration = null;
        bool ancestorRegistrationDisposed = false;
        bool nestedTriggered = false;
        LocalEmergencyStopRegistrationResult nestedResult =
            nestedRegistrar.TryRegister(
                ownerGeneration: 2,
                sessionGeneration: 2,
                _ =>
                {
                    ancestorRegistration!.Dispose();
                    ancestorRegistrationDisposed = true;
                });
        ILocalEmergencyStopRegistration nestedRegistration =
            Assert.IsAssignableFrom<ILocalEmergencyStopRegistration>(
                nestedResult.Registration);
        LocalEmergencyStopRegistrationResult ancestorResult =
            ancestorRegistrar.TryRegister(
                ownerGeneration: 1,
                sessionGeneration: 1,
                _ => nestedTriggered = nestedRegistrar.Trigger());
        ancestorRegistration =
            Assert.IsAssignableFrom<ILocalEmergencyStopRegistration>(
                ancestorResult.Registration);

        Task<bool> trigger = RunOnDedicatedThread(ancestorRegistrar.Trigger);

        Assert.True(await trigger.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(nestedTriggered);
        Assert.True(ancestorRegistrationDisposed);
        Assert.False(ancestorRegistration.IsCurrent);
        Assert.False(nestedRegistration.IsCurrent);

        ancestorRegistration.Dispose();
        nestedRegistration.Dispose();
        ancestorRegistrar.Dispose();
        nestedRegistrar.Dispose();
    }

    [Fact]
    public async Task EmergencyRegistrarDisposeFromNestedCallbackDoesNotWaitForAncestorDrain()
    {
        var ancestorRegistrar = new InMemoryLocalEmergencyStopRegistrar();
        var nestedRegistrar = new InMemoryLocalEmergencyStopRegistrar();
        bool ancestorRegistrarDisposed = false;
        bool nestedTriggered = false;
        LocalEmergencyStopRegistrationResult nestedResult =
            nestedRegistrar.TryRegister(
                ownerGeneration: 2,
                sessionGeneration: 2,
                _ =>
                {
                    ancestorRegistrar.Dispose();
                    ancestorRegistrarDisposed = true;
                });
        ILocalEmergencyStopRegistration nestedRegistration =
            Assert.IsAssignableFrom<ILocalEmergencyStopRegistration>(
                nestedResult.Registration);
        LocalEmergencyStopRegistrationResult ancestorResult =
            ancestorRegistrar.TryRegister(
                ownerGeneration: 1,
                sessionGeneration: 1,
                _ => nestedTriggered = nestedRegistrar.Trigger());
        ILocalEmergencyStopRegistration ancestorRegistration =
            Assert.IsAssignableFrom<ILocalEmergencyStopRegistration>(
                ancestorResult.Registration);

        Task<bool> trigger = RunOnDedicatedThread(ancestorRegistrar.Trigger);

        Assert.True(await trigger.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(nestedTriggered);
        Assert.True(ancestorRegistrarDisposed);
        Assert.False(ancestorRegistration.IsCurrent);
        Assert.False(nestedRegistration.IsCurrent);
        LocalEmergencyStopRegistrationResult afterDispose =
            ancestorRegistrar.TryRegister(
                ownerGeneration: 3,
                sessionGeneration: 3,
                _ => { });
        Assert.False(afterDispose.Registered);
        Assert.Equal(
            "emergency_stop_registrar_unavailable",
            afterDispose.Boundary.ReasonCode);

        ancestorRegistration.Dispose();
        nestedRegistration.Dispose();
        ancestorRegistrar.Dispose();
        nestedRegistrar.Dispose();
    }

    [Fact]
    public void TriggeredEmergencyRegistrationReleasesCallbackState()
    {
        (
            InMemoryLocalEmergencyStopRegistrar registrar,
            ILocalEmergencyStopRegistration registration,
            WeakReference callbackState) = CreateRegistrationWithWeakCallback();
        using (registrar)
        using (registration)
        {
            Assert.True(registrar.Trigger());

            AssertCollected(callbackState);
        }
    }

    [Fact]
    public void DisposedEmergencyRegistrationReleasesCallbackState()
    {
        (
            InMemoryLocalEmergencyStopRegistrar registrar,
            ILocalEmergencyStopRegistration registration,
            WeakReference callbackState) = CreateRegistrationWithWeakCallback();
        using (registrar)
        {
            registration.Dispose();

            AssertCollected(callbackState);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (
        InMemoryLocalEmergencyStopRegistrar Registrar,
        ILocalEmergencyStopRegistration Registration,
        WeakReference CallbackState) CreateRegistrationWithWeakCallback()
    {
        var callbackState = new object();
        var weakCallbackState = new WeakReference(callbackState);
        var registrar = new InMemoryLocalEmergencyStopRegistrar();
        LocalEmergencyStopRegistrationResult result = registrar.TryRegister(
            ownerGeneration: 1,
            sessionGeneration: 1,
            _ => GC.KeepAlive(callbackState));
        ILocalEmergencyStopRegistration registration = Assert.IsAssignableFrom<
            ILocalEmergencyStopRegistration>(result.Registration);
        return (registrar, registration, weakCallbackState);
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

    private static Task RunOnDedicatedThread(Action action) =>
        Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static Task<T> RunOnDedicatedThread<T>(Func<T> action) =>
        Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private sealed class RecordingMemoryOwner(int length) : IMemoryOwner<byte>
    {
        private readonly byte[] buffer = new byte[length];

        public int DisposeCount { get; private set; }

        public Memory<byte> Memory => buffer;

        public void Dispose() => DisposeCount++;
    }

    private sealed class ExternalEmergencyStopRegistration(
        long ownerGeneration,
        long sessionGeneration) : ILocalEmergencyStopRegistration
    {
        private int current = 1;

        public long OwnerGeneration { get; } = ownerGeneration;

        public long SessionGeneration { get; } = sessionGeneration;

        public bool IsCurrent => Volatile.Read(ref current) != 0;

        public void Dispose() => Interlocked.Exchange(ref current, 0);
    }
}
