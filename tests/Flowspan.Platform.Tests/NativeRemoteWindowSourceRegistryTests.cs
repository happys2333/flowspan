using System.Collections.Concurrent;
using System.Text.Json;
using Flowspan.Domain;
using Flowspan.Platform;

namespace Flowspan.Platform.Tests;

public sealed class NativeRemoteWindowSourceRegistryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 8, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId Host =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void ClosingRegisteredSourceInvalidatesEveryExactLease()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        using NativeRemoteWindowSourceRegistration registration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
            registry.GetSnapshot());
        Assert.Equal(snapshot, registration.Snapshot);
        Assert.True(
            registry.TryAcquire(
                snapshot.Token,
                snapshot.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredLease));
        NativeRemoteWindowSourceLease lease = Assert.IsType<
            NativeRemoteWindowSourceLease>(acquiredLease);
        using (lease)
        {
            Assert.True(lease.IsCurrent);
            Assert.Equal(snapshot.Source, lease.Source);

            registration.Dispose();

            Assert.False(lease.IsCurrent);
            Assert.Empty(registry.GetSnapshot());
            Assert.False(
                registry.TryAcquire(
                    snapshot.Token,
                    snapshot.Source.SourceGeneration,
                    out NativeRemoteWindowSourceLease? staleLease));
            Assert.Null(staleLease);
        }
    }

    [Fact]
    public void InvalidationCommitsBeforeCallbacksAndIsolatesObserverFailure()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration sourceRegistration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
            registry.GetSnapshot());
        Assert.True(
            registry.TryAcquire(
                snapshot.Token,
                snapshot.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredLease));
        using NativeRemoteWindowSourceLease lease = Assert.IsType<
            NativeRemoteWindowSourceLease>(acquiredLease);
        int siblingCalls = 0;
        Assert.True(
            lease.TryRegisterInvalidationCallback(
                () =>
                {
                    Assert.False(lease.IsCurrent);
                    Assert.False(
                        lease.TryRegisterInvalidationCallback(
                            static () => { },
                            out NativeRemoteWindowSourceInvalidationRegistration?
                                lateRegistration));
                    Assert.Null(lateRegistration);
                    throw new InvalidOperationException(
                        "source_invalidation_callback_failure_canary");
                },
                out NativeRemoteWindowSourceInvalidationRegistration?
                    firstRegistration));
        using NativeRemoteWindowSourceInvalidationRegistration first =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                firstRegistration);
        Assert.True(
            lease.TryRegisterInvalidationCallback(
                () => siblingCalls++,
                out NativeRemoteWindowSourceInvalidationRegistration?
                    siblingRegistration));
        using NativeRemoteWindowSourceInvalidationRegistration sibling =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                siblingRegistration);

        sourceRegistration.Dispose();

        Assert.Equal(1, siblingCalls);
        Assert.False(first.IsCurrent);
        Assert.False(sibling.IsCurrent);
        Assert.Empty(registry.GetSnapshot());
    }

    [Fact]
    public async Task InvalidationCallbackUnregisterDrainsExternallyWithoutSelfDeadlock()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration sourceRegistration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
            registry.GetSnapshot());
        Assert.True(
            registry.TryAcquire(
                snapshot.Token,
                snapshot.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredLease));
        using NativeRemoteWindowSourceLease lease = Assert.IsType<
            NativeRemoteWindowSourceLease>(acquiredLease);
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        using var selfDisposeReturned = new ManualResetEventSlim();
        using var externalDisposeStarted = new ManualResetEventSlim();
        using var externalDisposeReturned = new ManualResetEventSlim();
        NativeRemoteWindowSourceInvalidationRegistration? registration = null;
        Assert.True(
            lease.TryRegisterInvalidationCallback(
                () =>
                {
                    callbackEntered.Set();
                    releaseCallback.Wait();
                    registration?.Dispose();
                    selfDisposeReturned.Set();
                },
                out registration));
        NativeRemoteWindowSourceInvalidationRegistration callbackRegistration =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                registration);
        Task invalidate = RunOnDedicatedThread(sourceRegistration.Dispose);
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));
        Task externalDispose = RunOnDedicatedThread(() =>
        {
            externalDisposeStarted.Set();
            callbackRegistration.Dispose();
            externalDisposeReturned.Set();
        });
        Assert.True(externalDisposeStarted.Wait(TimeSpan.FromSeconds(5)));

        Assert.True(
            SpinWait.SpinUntil(
                () => callbackRegistration.CallbackDrainWaiterCount == 1,
                TimeSpan.FromSeconds(5)));
        Assert.False(externalDisposeReturned.IsSet);
        releaseCallback.Set();

        await invalidate.WaitAsync(TimeSpan.FromSeconds(5));
        await externalDispose.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(selfDisposeReturned.Wait(TimeSpan.FromSeconds(5)));
        callbackRegistration.Dispose();
    }

    [Fact]
    public async Task InvalidationCallbackWorkerCanDisposeRegistrationWithoutDeadlock()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration sourceRegistration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        using NativeRemoteWindowSourceLease lease = AcquireLease(
            registry,
            sourceRegistration.Snapshot);
        NativeRemoteWindowSourceInvalidationRegistration? registration = null;
        Assert.True(
            lease.TryRegisterInvalidationCallback(
                () => Task.Run(() => registration!.Dispose())
                    .GetAwaiter()
                    .GetResult(),
                out registration));
        NativeRemoteWindowSourceInvalidationRegistration callbackRegistration =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                registration);

        Task invalidation = RunOnDedicatedThread(sourceRegistration.Dispose);

        await invalidation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(callbackRegistration.IsCurrent);
        callbackRegistration.Dispose();
    }

    [Fact]
    public async Task StaleInvalidationCallbackContextWaitsForLaterRegistration()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration sourceRegistration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        using NativeRemoteWindowSourceLease lease = AcquireLease(
            registry,
            sourceRegistration.Snapshot);
        using var releaseWorker = new ManualResetEventSlim();
        using var workerReturned = new ManualResetEventSlim();
        using var secondCallbackEntered = new ManualResetEventSlim();
        using var releaseSecondCallback = new ManualResetEventSlim();
        Task? disposal = null;
        NativeRemoteWindowSourceInvalidationRegistration? secondRegistration = null;
        Assert.True(
            lease.TryRegisterInvalidationCallback(
                () => disposal = Task.Run(() =>
                {
                    releaseWorker.Wait();
                    secondRegistration!.Dispose();
                    workerReturned.Set();
                }),
                out NativeRemoteWindowSourceInvalidationRegistration?
                    firstRegistration));
        using NativeRemoteWindowSourceInvalidationRegistration first =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                firstRegistration);
        Assert.True(
            lease.TryRegisterInvalidationCallback(
                () =>
                {
                    secondCallbackEntered.Set();
                    releaseSecondCallback.Wait();
                },
                out secondRegistration));
        NativeRemoteWindowSourceInvalidationRegistration second =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                secondRegistration);
        Task invalidation = RunOnDedicatedThread(sourceRegistration.Dispose);
        Assert.True(secondCallbackEntered.Wait(TimeSpan.FromSeconds(5)));

        releaseWorker.Set();
        Assert.True(
            SpinWait.SpinUntil(
                () => second.CallbackDrainWaiterCount == 1,
                TimeSpan.FromSeconds(5)));

        Assert.False(workerReturned.IsSet);
        releaseSecondCallback.Set();
        await invalidation.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.IsType<Task>(disposal).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(workerReturned.IsSet);
        second.Dispose();
    }

    [Fact]
    public async Task NestedInvalidationCanDisposeAncestorRegistration()
    {
        using var ancestorRegistry = new NativeRemoteWindowSourceRegistry(Host);
        using var nestedRegistry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration ancestorSource =
            ancestorRegistry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceRegistration nestedSource =
            nestedRegistry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(10, 0, 1280, 720, 2)));
        using NativeRemoteWindowSourceLease ancestorLease = AcquireLease(
            ancestorRegistry,
            ancestorSource.Snapshot);
        using NativeRemoteWindowSourceLease nestedLease = AcquireLease(
            nestedRegistry,
            nestedSource.Snapshot);
        using var nestedDisposeReturned = new ManualResetEventSlim();
        NativeRemoteWindowSourceInvalidationRegistration? ancestorCallback = null;
        Assert.True(
            ancestorLease.TryRegisterInvalidationCallback(
                nestedSource.Dispose,
                out ancestorCallback));
        NativeRemoteWindowSourceInvalidationRegistration ancestorRegistration =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                ancestorCallback);
        Assert.True(
            nestedLease.TryRegisterInvalidationCallback(
                () =>
                {
                    ancestorRegistration.Dispose();
                    nestedDisposeReturned.Set();
                },
                out NativeRemoteWindowSourceInvalidationRegistration?
                    nestedCallback));
        using NativeRemoteWindowSourceInvalidationRegistration nestedRegistration =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                nestedCallback);

        Task invalidating = RunOnDedicatedThread(ancestorSource.Dispose);

        await invalidating.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(nestedDisposeReturned.IsSet);
        Assert.False(ancestorRegistration.IsCurrent);
        ancestorRegistration.Dispose();
        nestedSource.Dispose();
    }

    [Fact]
    public async Task SourceInvalidationClosesUseAdmissionBeforeDrainingExistingUse()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration sourceRegistration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
            registry.GetSnapshot());
        Assert.True(
            registry.TryAcquire(
                snapshot.Token,
                snapshot.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredLease));
        using NativeRemoteWindowSourceLease lease = Assert.IsType<
            NativeRemoteWindowSourceLease>(acquiredLease);
        Assert.True(
            lease.TryAcquireUseScope(
                snapshot.Source.SourceGeneration,
                snapshot.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? acquiredScope));
        NativeRemoteWindowSourceUseScope scope = Assert.IsType<
            NativeRemoteWindowSourceUseScope>(acquiredScope);
        using var callbackInvoked = new ManualResetEventSlim();
        Assert.True(
            lease.TryRegisterInvalidationCallback(
                callbackInvoked.Set,
                out NativeRemoteWindowSourceInvalidationRegistration?
                    callbackRegistration));
        using NativeRemoteWindowSourceInvalidationRegistration callback =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                callbackRegistration);
        Task invalidate = RunOnDedicatedThread(sourceRegistration.Dispose);
        Assert.True(
            SpinWait.SpinUntil(
                () => !lease.IsCurrent,
                TimeSpan.FromSeconds(5)));

        Assert.False(
            lease.TryAcquireUseScope(
                snapshot.Source.SourceGeneration,
                snapshot.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? rejectedScope));
        Assert.Null(rejectedScope);
        Assert.False(callbackInvoked.IsSet);
        Assert.False(invalidate.IsCompleted);

        scope.Dispose();

        await invalidate.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(callbackInvoked.IsSet);
    }

    [Fact]
    public async Task ConcurrentSourceRegistrationDisposeJoinsInvalidationDrain()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration sourceRegistration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceSnapshot snapshot = sourceRegistration.Snapshot;
        using NativeRemoteWindowSourceLease lease = AcquireLease(registry, snapshot);
        Assert.True(
            lease.TryAcquireUseScope(
                snapshot.Source.SourceGeneration,
                snapshot.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? acquiredScope));
        NativeRemoteWindowSourceUseScope scope = Assert.IsType<
            NativeRemoteWindowSourceUseScope>(acquiredScope);
        using var callbackInvoked = new ManualResetEventSlim();
        Assert.True(
            lease.TryRegisterInvalidationCallback(
                callbackInvoked.Set,
                out NativeRemoteWindowSourceInvalidationRegistration?
                    callbackRegistration));
        using NativeRemoteWindowSourceInvalidationRegistration callback =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                callbackRegistration);
        Task firstDispose = RunOnDedicatedThread(sourceRegistration.Dispose);
        Assert.True(
            SpinWait.SpinUntil(
                () => sourceRegistration.InvalidationDrainWaiterCount == 1,
                TimeSpan.FromSeconds(5)));
        Assert.False(lease.IsCurrent);
        Assert.False(firstDispose.IsCompleted);
        Task secondDispose = RunOnDedicatedThread(sourceRegistration.Dispose);

        try
        {
            Assert.True(
                SpinWait.SpinUntil(
                    () => sourceRegistration.InvalidationDrainWaiterCount == 2,
                    TimeSpan.FromSeconds(5)));
            Assert.False(secondDispose.IsCompleted);
            Assert.False(callbackInvoked.IsSet);
        }
        finally
        {
            scope.Dispose();
        }

        await Task.WhenAll(firstDispose, secondDispose)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(callbackInvoked.IsSet);
    }

    [Fact]
    public void ReentrantSourceInvalidationDefersCallbackUntilUseScopeRelease()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration sourceRegistration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
            registry.GetSnapshot());
        Assert.True(
            registry.TryAcquire(
                snapshot.Token,
                snapshot.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredLease));
        using NativeRemoteWindowSourceLease lease = Assert.IsType<
            NativeRemoteWindowSourceLease>(acquiredLease);
        Assert.True(
            lease.TryAcquireUseScope(
                snapshot.Source.SourceGeneration,
                snapshot.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? acquiredScope));
        NativeRemoteWindowSourceUseScope scope = Assert.IsType<
            NativeRemoteWindowSourceUseScope>(acquiredScope);
        int callbackCalls = 0;
        Assert.True(
            lease.TryRegisterInvalidationCallback(
                () => callbackCalls++,
                out NativeRemoteWindowSourceInvalidationRegistration?
                    callbackRegistration));
        using NativeRemoteWindowSourceInvalidationRegistration callback =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                callbackRegistration);

        sourceRegistration.Dispose();

        Assert.False(lease.IsCurrent);
        Assert.Equal(0, callbackCalls);

        scope.Dispose();

        Assert.Equal(1, callbackCalls);
    }

    [Fact]
    public async Task SecurityMetadataChangeRetiresSourceAfterDrainingExistingUse()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        using NativeRemoteWindowSourceRegistration registration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceSnapshot before = Assert.Single(
            registry.GetSnapshot());
        Assert.True(
            registry.TryAcquire(
                before.Token,
                before.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredLease));
        using NativeRemoteWindowSourceLease lease = Assert.IsType<
            NativeRemoteWindowSourceLease>(acquiredLease);
        Assert.True(
            lease.TryAcquireUseScope(
                before.Source.SourceGeneration,
                before.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? acquiredScope));
        NativeRemoteWindowSourceUseScope scope = Assert.IsType<
            NativeRemoteWindowSourceUseScope>(acquiredScope);
        using var updateStarted = new ManualResetEventSlim();
        Task<bool> update = RunOnDedicatedThread(() =>
        {
            updateStarted.Set();
            return registration.TryUpdate(
                Metadata(
                    NativeRemoteWindowGeometry.Create(
                        10,
                        20,
                        1440,
                        900,
                        2)));
        });
        Assert.True(updateStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(
            SpinWait.SpinUntil(
                () =>
                {
                    if (!lease.TryAcquireUseScope(
                            before.Source.SourceGeneration,
                            before.GeometryRevision,
                            out NativeRemoteWindowSourceUseScope? probe))
                    {
                        return true;
                    }

                    Assert.IsType<NativeRemoteWindowSourceUseScope>(probe)
                        .Dispose();
                    return false;
                },
                TimeSpan.FromSeconds(5)));
        Assert.False(update.IsCompleted);

        scope.Dispose();

        Assert.False(await update.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(lease.IsCurrent);
        Assert.Empty(registry.GetSnapshot());
        Assert.False(
            lease.TryAcquireUseScope(
                before.Source.SourceGeneration,
                before.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? staleScope));
        Assert.Null(staleScope);
    }

    [Fact]
    public void RetainedLeaseHasIndependentHandleLifetime()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        using NativeRemoteWindowSourceRegistration registration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
            registry.GetSnapshot());
        Assert.True(
            registry.TryAcquire(
                snapshot.Token,
                snapshot.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredLease));
        NativeRemoteWindowSourceLease original = Assert.IsType<
            NativeRemoteWindowSourceLease>(acquiredLease);
        Assert.True(
            original.TryRetain(
                out NativeRemoteWindowSourceLease? retainedLease));
        NativeRemoteWindowSourceLease retained = Assert.IsType<
            NativeRemoteWindowSourceLease>(retainedLease);

        original.Dispose();

        Assert.False(original.IsCurrent);
        Assert.True(retained.IsCurrent);
        Assert.False(
            original.TryAcquireUseScope(
                snapshot.Source.SourceGeneration,
                snapshot.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? originalScope));
        Assert.Null(originalScope);
        Assert.True(
            retained.TryAcquireUseScope(
                snapshot.Source.SourceGeneration,
                snapshot.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? retainedScope));
        retainedScope?.Dispose();

        retained.Dispose();

        Assert.False(retained.IsCurrent);
        Assert.False(
            retained.TryAcquireUseScope(
                snapshot.Source.SourceGeneration,
                snapshot.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? disposedScope));
        Assert.Null(disposedScope);
        Assert.False(
            retained.TryRegisterInvalidationCallback(
                static () => { },
                out NativeRemoteWindowSourceInvalidationRegistration?
                    disposedCallback));
        Assert.Null(disposedCallback);
        retained.Dispose();
    }

    [Fact]
    public async Task RegistryDisposalMarksEverySourceStaleBeforeAnyCallback()
    {
        var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration firstSource =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceRegistration secondSource =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(10, 0, 1280, 720, 2)));
        Assert.True(
            registry.TryAcquire(
                firstSource.Snapshot.Token,
                firstSource.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredFirstLease));
        using NativeRemoteWindowSourceLease firstLease = Assert.IsType<
            NativeRemoteWindowSourceLease>(acquiredFirstLease);
        Assert.True(
            registry.TryAcquire(
                secondSource.Snapshot.Token,
                secondSource.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredSecondLease));
        using NativeRemoteWindowSourceLease secondLease = Assert.IsType<
            NativeRemoteWindowSourceLease>(acquiredSecondLease);
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        Assert.True(
            firstLease.TryRegisterInvalidationCallback(
                () =>
                {
                    callbackEntered.Set();
                    releaseCallback.Wait();
                },
                out NativeRemoteWindowSourceInvalidationRegistration?
                    callbackRegistration));
        using NativeRemoteWindowSourceInvalidationRegistration callback =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                callbackRegistration);
        Task dispose = RunOnDedicatedThread(registry.Dispose);
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));

        Assert.False(firstLease.IsCurrent);
        Assert.False(secondLease.IsCurrent);

        releaseCallback.Set();
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        firstSource.Dispose();
        secondSource.Dispose();
        registry.Dispose();
    }

    [Fact]
    public async Task ConcurrentRegistryDisposeJoinsInvalidationDrain()
    {
        var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration sourceRegistration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceSnapshot snapshot = sourceRegistration.Snapshot;
        using NativeRemoteWindowSourceLease lease = AcquireLease(registry, snapshot);
        Assert.True(
            lease.TryAcquireUseScope(
                snapshot.Source.SourceGeneration,
                snapshot.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? acquiredScope));
        NativeRemoteWindowSourceUseScope scope = Assert.IsType<
            NativeRemoteWindowSourceUseScope>(acquiredScope);
        using var callbackInvoked = new ManualResetEventSlim();
        Assert.True(
            lease.TryRegisterInvalidationCallback(
                callbackInvoked.Set,
                out NativeRemoteWindowSourceInvalidationRegistration?
                    callbackRegistration));
        using NativeRemoteWindowSourceInvalidationRegistration callback =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                callbackRegistration);
        Task firstDispose = RunOnDedicatedThread(registry.Dispose);
        Assert.True(
            SpinWait.SpinUntil(
                () => sourceRegistration.InvalidationDrainWaiterCount == 1,
                TimeSpan.FromSeconds(5)));
        Assert.False(lease.IsCurrent);
        Assert.False(firstDispose.IsCompleted);
        Task secondDispose = RunOnDedicatedThread(registry.Dispose);

        try
        {
            Assert.True(
                SpinWait.SpinUntil(
                    () => sourceRegistration.InvalidationDrainWaiterCount == 2,
                    TimeSpan.FromSeconds(5)));
            Assert.False(secondDispose.IsCompleted);
            Assert.False(callbackInvoked.IsSet);
        }
        finally
        {
            scope.Dispose();
        }

        await Task.WhenAll(firstDispose, secondDispose)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(callbackInvoked.IsSet);
        sourceRegistration.Dispose();
        registry.Dispose();
    }

    [Fact]
    public async Task RegistryDisposeJoinsRegistrationInvalidationAlreadyInFlight()
    {
        var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration sourceRegistration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceSnapshot snapshot = sourceRegistration.Snapshot;
        using NativeRemoteWindowSourceLease lease = AcquireLease(registry, snapshot);
        Assert.True(
            lease.TryAcquireUseScope(
                snapshot.Source.SourceGeneration,
                snapshot.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? acquiredScope));
        NativeRemoteWindowSourceUseScope scope = Assert.IsType<
            NativeRemoteWindowSourceUseScope>(acquiredScope);
        using var callbackInvoked = new ManualResetEventSlim();
        Assert.True(
            lease.TryRegisterInvalidationCallback(
                callbackInvoked.Set,
                out NativeRemoteWindowSourceInvalidationRegistration?
                    callbackRegistration));
        using NativeRemoteWindowSourceInvalidationRegistration callback =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                callbackRegistration);
        Task sourceDispose = RunOnDedicatedThread(sourceRegistration.Dispose);
        Assert.True(
            SpinWait.SpinUntil(
                () => sourceRegistration.InvalidationDrainWaiterCount == 1,
                TimeSpan.FromSeconds(5)));
        Assert.False(sourceDispose.IsCompleted);
        Task registryDispose = RunOnDedicatedThread(registry.Dispose);

        try
        {
            Assert.True(
                SpinWait.SpinUntil(
                    () => sourceRegistration.InvalidationDrainWaiterCount == 2,
                    TimeSpan.FromSeconds(5)));
            Assert.False(registryDispose.IsCompleted);
            Assert.False(callbackInvoked.IsSet);
        }
        finally
        {
            scope.Dispose();
        }

        await Task.WhenAll(sourceDispose, registryDispose)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(callbackInvoked.IsSet);
        sourceRegistration.Dispose();
        registry.Dispose();
    }

    [Fact]
    public async Task ConcurrentInvalidationCallbacksCanCloseForeignRegistrations()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration firstSource =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceRegistration secondSource =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(10, 0, 1280, 720, 2)));
        using NativeRemoteWindowSourceLease firstLease = AcquireLease(
            registry,
            firstSource.Snapshot);
        using NativeRemoteWindowSourceLease secondLease = AcquireLease(
            registry,
            secondSource.Snapshot);
        using var callbacksEntered = new CountdownEvent(2);
        using var closeForeignRegistrations = new ManualResetEventSlim();
        NativeRemoteWindowSourceInvalidationRegistration? firstCallback = null;
        NativeRemoteWindowSourceInvalidationRegistration? secondCallback = null;
        Assert.True(
            firstLease.TryRegisterInvalidationCallback(
                () =>
                {
                    callbacksEntered.Signal();
                    closeForeignRegistrations.Wait();
                    secondCallback!.Dispose();
                    secondSource.Dispose();
                },
                out firstCallback));
        Assert.True(
            secondLease.TryRegisterInvalidationCallback(
                () =>
                {
                    callbacksEntered.Signal();
                    closeForeignRegistrations.Wait();
                    firstCallback!.Dispose();
                    firstSource.Dispose();
                },
                out secondCallback));
        NativeRemoteWindowSourceInvalidationRegistration firstRegistration =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                firstCallback);
        NativeRemoteWindowSourceInvalidationRegistration secondRegistration =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                secondCallback);
        Task firstDispose = RunOnDedicatedThread(firstSource.Dispose);
        Task secondDispose = RunOnDedicatedThread(secondSource.Dispose);

        try
        {
            Assert.True(callbacksEntered.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            closeForeignRegistrations.Set();
        }

        await Task.WhenAll(firstDispose, secondDispose)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(firstLease.IsCurrent);
        Assert.False(secondLease.IsCurrent);
        Assert.False(firstRegistration.IsCurrent);
        Assert.False(secondRegistration.IsCurrent);
        firstRegistration.Dispose();
        secondRegistration.Dispose();
        firstSource.Dispose();
        secondSource.Dispose();
    }

    [Fact]
    public async Task ConcurrentForeignUseScopesCanRetireEachOther()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration firstSource =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceRegistration secondSource =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(10, 0, 1280, 720, 2)));
        using NativeRemoteWindowSourceLease firstLease = AcquireLease(
            registry,
            firstSource.Snapshot);
        using NativeRemoteWindowSourceLease secondLease = AcquireLease(
            registry,
            secondSource.Snapshot);
        using var scopesEntered = new CountdownEvent(2);
        using var closeForeignSources = new ManualResetEventSlim();
        Task firstWorker = RunOnDedicatedThread(
            () =>
            {
                Assert.True(
                    firstLease.TryAcquireUseScope(
                        firstSource.Source.SourceGeneration,
                        firstSource.Snapshot.GeometryRevision,
                        out NativeRemoteWindowSourceUseScope? acquiredScope));
                using NativeRemoteWindowSourceUseScope scope = Assert.IsType<
                    NativeRemoteWindowSourceUseScope>(acquiredScope);
                scopesEntered.Signal();
                closeForeignSources.Wait();
                secondSource.Dispose();
            });
        Task secondWorker = RunOnDedicatedThread(
            () =>
            {
                Assert.True(
                    secondLease.TryAcquireUseScope(
                        secondSource.Source.SourceGeneration,
                        secondSource.Snapshot.GeometryRevision,
                        out NativeRemoteWindowSourceUseScope? acquiredScope));
                using NativeRemoteWindowSourceUseScope scope = Assert.IsType<
                    NativeRemoteWindowSourceUseScope>(acquiredScope);
                scopesEntered.Signal();
                closeForeignSources.Wait();
                firstSource.Dispose();
            });

        try
        {
            Assert.True(scopesEntered.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            closeForeignSources.Set();
        }

        await Task.WhenAll(firstWorker, secondWorker)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(firstLease.IsCurrent);
        Assert.False(secondLease.IsCurrent);
        firstSource.Dispose();
        secondSource.Dispose();
    }

    [Fact]
    public async Task SourceUseAndProtectionCallbackCanCloseEachOtherWithoutDeadlock()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        using NativeRemoteWindowSourceRegistration registration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        using NativeRemoteWindowSourceLease lease = AcquireLease(
            registry,
            registration.Snapshot);
        using var protection = new InMemoryNativeProtectionSource(
            ownerGeneration: 1,
            sessionGeneration: 1,
            registration.Source.SourceGeneration);
        using var activitiesEntered = new CountdownEvent(2);
        using var invokeCrossClosures = new ManualResetEventSlim();
        Exception? callbackFailure = null;
        protection.Changed += _ =>
        {
            try
            {
                activitiesEntered.Signal();
                invokeCrossClosures.Wait();
                registration.Dispose();
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(
                    ref callbackFailure,
                    exception,
                    comparand: null);
                throw;
            }
        };
        Task useWorker = RunOnDedicatedThread(
            () =>
            {
                Assert.True(
                    lease.TryAcquireUseScope(
                        registration.Source.SourceGeneration,
                        registration.Snapshot.GeometryRevision,
                        out NativeRemoteWindowSourceUseScope? acquiredScope));
                using NativeRemoteWindowSourceUseScope scope = Assert.IsType<
                    NativeRemoteWindowSourceUseScope>(acquiredScope);
                activitiesEntered.Signal();
                invokeCrossClosures.Wait();
                protection.Dispose();
            });
        Task<bool> protectionWorker = RunOnDedicatedThread(
            () => protection.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now,
                    "test-probe")));

        try
        {
            Assert.True(activitiesEntered.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            invokeCrossClosures.Set();
        }

        await Task.WhenAll(useWorker, protectionWorker)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await protectionWorker);
        Assert.Null(Volatile.Read(ref callbackFailure));
        Assert.False(lease.IsCurrent);
        Assert.False(
            protection.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? observation));
        Assert.Null(observation);
    }

    [Fact]
    public async Task InvalidationAndProtectionCallbacksCanCloseEachOtherWithoutDeadlock()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        using NativeRemoteWindowSourceRegistration sourceRegistration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        using NativeRemoteWindowSourceLease lease = AcquireLease(
            registry,
            sourceRegistration.Snapshot);
        using var protection = new InMemoryNativeProtectionSource(
            ownerGeneration: 1,
            sessionGeneration: 1,
            sourceRegistration.Source.SourceGeneration);
        using var protectionCallbackEntered = new ManualResetEventSlim();
        using var protectionDisposeReturned = new ManualResetEventSlim();
        Exception? invalidationCallbackFailure = null;
        Exception? protectionCallbackFailure = null;
        Assert.True(
            lease.TryRegisterInvalidationCallback(
                () =>
                {
                    try
                    {
                        protectionCallbackEntered.Wait();
                        protection.Dispose();
                        protectionDisposeReturned.Set();
                    }
                    catch (Exception exception)
                    {
                        Interlocked.CompareExchange(
                            ref invalidationCallbackFailure,
                            exception,
                            comparand: null);
                        throw;
                    }
                },
                out NativeRemoteWindowSourceInvalidationRegistration?
                    callbackRegistration));
        using NativeRemoteWindowSourceInvalidationRegistration callback =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                callbackRegistration);
        protection.Changed += _ =>
        {
            try
            {
                protectionCallbackEntered.Set();
                sourceRegistration.Dispose();
                protectionDisposeReturned.Wait();
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(
                    ref protectionCallbackFailure,
                    exception,
                    comparand: null);
                throw;
            }
        };
        Task sourceDispose = RunOnDedicatedThread(sourceRegistration.Dispose);
        Task<bool> protectionPublish = RunOnDedicatedThread(
            () => protection.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now,
                    "test-probe")));

        await Task.WhenAll(sourceDispose, protectionPublish)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await protectionPublish);
        Assert.Null(Volatile.Read(ref invalidationCallbackFailure));
        Assert.Null(Volatile.Read(ref protectionCallbackFailure));
        Assert.False(lease.IsCurrent);
        Assert.False(callback.IsCurrent);
        Assert.False(
            protection.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? observation));
        Assert.Null(observation);
    }

    [Fact]
    public async Task ForeignSourceUsesCannotDeadlockCrossSourceDisplayUpdates()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        using NativeRemoteWindowSourceRegistration firstRegistration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        using NativeRemoteWindowSourceRegistration secondRegistration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(10, 0, 1280, 720, 2)));
        using NativeRemoteWindowSourceLease firstLease = AcquireLease(
            registry,
            firstRegistration.Snapshot);
        using NativeRemoteWindowSourceLease secondLease = AcquireLease(
            registry,
            secondRegistration.Snapshot);
        using var usesEntered = new CountdownEvent(2);
        using var updatesCompleted = new CountdownEvent(2);
        using var updateForeignSource = new ManualResetEventSlim();
        using var releaseUseScopes = new ManualResetEventSlim();
        Task<bool> firstWorker = RunOnDedicatedThread(
            () =>
            {
                Assert.True(
                    firstLease.TryAcquireUseScope(
                        firstRegistration.Source.SourceGeneration,
                        firstRegistration.Snapshot.GeometryRevision,
                        out NativeRemoteWindowSourceUseScope? acquiredScope));
                using NativeRemoteWindowSourceUseScope scope = Assert.IsType<
                    NativeRemoteWindowSourceUseScope>(acquiredScope);
                usesEntered.Signal();
                updateForeignSource.Wait();
                bool updated = secondRegistration.TryUpdate(
                    Metadata(
                        secondRegistration.Snapshot.Metadata.Geometry,
                        displayName: "Second renamed by first"));
                updatesCompleted.Signal();
                releaseUseScopes.Wait();
                return updated;
            });
        Task<bool> secondWorker = RunOnDedicatedThread(
            () =>
            {
                Assert.True(
                    secondLease.TryAcquireUseScope(
                        secondRegistration.Source.SourceGeneration,
                        secondRegistration.Snapshot.GeometryRevision,
                        out NativeRemoteWindowSourceUseScope? acquiredScope));
                using NativeRemoteWindowSourceUseScope scope = Assert.IsType<
                    NativeRemoteWindowSourceUseScope>(acquiredScope);
                usesEntered.Signal();
                updateForeignSource.Wait();
                bool updated = firstRegistration.TryUpdate(
                    Metadata(
                        firstRegistration.Snapshot.Metadata.Geometry,
                        displayName: "First renamed by second"));
                updatesCompleted.Signal();
                releaseUseScopes.Wait();
                return updated;
            });

        try
        {
            Assert.True(usesEntered.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            updateForeignSource.Set();
        }

        try
        {
            Assert.True(updatesCompleted.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            releaseUseScopes.Set();
        }

        bool[] crossUpdateResults = await Task.WhenAll(firstWorker, secondWorker)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(crossUpdateResults, Assert.False);
        Assert.True(firstLease.IsCurrent);
        Assert.True(secondLease.IsCurrent);
        Assert.True(
            firstLease.TryAcquireUseScope(
                firstRegistration.Source.SourceGeneration,
                firstRegistration.Snapshot.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? probeScope));
        Assert.IsType<NativeRemoteWindowSourceUseScope>(probeScope).Dispose();
        Assert.True(
            firstRegistration.TryUpdate(
                Metadata(
                    firstRegistration.Snapshot.Metadata.Geometry,
                    displayName: "First renamed externally")));
        Assert.Equal(
            "First renamed externally",
            firstRegistration.Snapshot.Source.DisplayName);
    }

    [Fact]
    public async Task ReentrantGeometryUpdateRetiresSourceWithoutWaitingOnItsOwnUseScope()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        using NativeRemoteWindowSourceRegistration registration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceSnapshot before = Assert.Single(
            registry.GetSnapshot());
        Assert.True(
            registry.TryAcquire(
                before.Token,
                before.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredLease));
        using NativeRemoteWindowSourceLease lease = Assert.IsType<
            NativeRemoteWindowSourceLease>(acquiredLease);
        Assert.True(
            lease.TryAcquireUseScope(
                before.Source.SourceGeneration,
                before.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? acquiredScope));
        NativeRemoteWindowSourceUseScope scope = Assert.IsType<
            NativeRemoteWindowSourceUseScope>(acquiredScope);
        int callbackCalls = 0;
        Assert.True(
            lease.TryRegisterInvalidationCallback(
                () => callbackCalls++,
                out NativeRemoteWindowSourceInvalidationRegistration?
                    callbackRegistration));
        using NativeRemoteWindowSourceInvalidationRegistration callback =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                callbackRegistration);
        NativeRemoteWindowSourceMetadata replacement = Metadata(
            NativeRemoteWindowGeometry.Create(10, 20, 1440, 900, 2));

        bool reentrantResult = await Task.Run(
                () => registration.TryUpdate(replacement))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(reentrantResult);
        Assert.False(lease.IsCurrent);
        Assert.Empty(registry.GetSnapshot());
        Assert.Equal(0, callbackCalls);
        Assert.False(
            lease.TryAcquireUseScope(
                before.Source.SourceGeneration,
                before.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? staleScope));
        Assert.Null(staleScope);

        scope.Dispose();

        Assert.Equal(1, callbackCalls);
        Assert.False(registration.TryUpdate(replacement));
    }

    [Fact]
    public async Task SecurityMetadataChangeRetiresSourceFromAncestorUseScope()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        using NativeRemoteWindowSourceRegistration ancestorRegistration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        using NativeRemoteWindowSourceRegistration nestedRegistration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(10, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceSnapshot ancestorSnapshot =
            ancestorRegistration.Snapshot;
        NativeRemoteWindowSourceSnapshot nestedSnapshot =
            nestedRegistration.Snapshot;
        using NativeRemoteWindowSourceLease ancestorLease = AcquireLease(
            registry,
            ancestorSnapshot);
        using NativeRemoteWindowSourceLease nestedLease = AcquireLease(
            registry,
            nestedSnapshot);
        Assert.True(
            ancestorLease.TryAcquireUseScope(
                ancestorSnapshot.Source.SourceGeneration,
                ancestorSnapshot.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? acquiredAncestorScope));
        using NativeRemoteWindowSourceUseScope ancestorScope = Assert.IsType<
            NativeRemoteWindowSourceUseScope>(acquiredAncestorScope);
        Assert.True(
            nestedLease.TryAcquireUseScope(
                nestedSnapshot.Source.SourceGeneration,
                nestedSnapshot.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? acquiredNestedScope));
        using NativeRemoteWindowSourceUseScope nestedScope = Assert.IsType<
            NativeRemoteWindowSourceUseScope>(acquiredNestedScope);
        int callbackCalls = 0;
        Assert.True(
            ancestorLease.TryRegisterInvalidationCallback(
                () => callbackCalls++,
                out NativeRemoteWindowSourceInvalidationRegistration?
                    callbackRegistration));
        using NativeRemoteWindowSourceInvalidationRegistration callback =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                callbackRegistration);

        bool updateResult = await Task.Run(
                () => ancestorRegistration.TryUpdate(
                    Metadata(
                        NativeRemoteWindowGeometry.Create(
                            20,
                            20,
                            1440,
                            900,
                            2))))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(updateResult);
        Assert.False(ancestorLease.IsCurrent);
        Assert.True(nestedLease.IsCurrent);
        Assert.Equal(nestedSnapshot, Assert.Single(registry.GetSnapshot()));
        Assert.Equal(0, callbackCalls);

        nestedScope.Dispose();

        Assert.Equal(0, callbackCalls);

        ancestorScope.Dispose();

        Assert.Equal(1, callbackCalls);
    }

    [Fact]
    public void DisplayNameUpdatePreservesExactSourceBinding()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceMetadata initial = Metadata(
            NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2));
        using NativeRemoteWindowSourceRegistration registration =
            registry.RegisterGeneric(initial);
        NativeRemoteWindowSourceSnapshot before = Assert.Single(
            registry.GetSnapshot());
        Assert.True(
            registry.TryAcquire(
                before.Token,
                before.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredLease));
        using NativeRemoteWindowSourceLease lease = Assert.IsType<
            NativeRemoteWindowSourceLease>(acquiredLease);

        Assert.True(
            registration.TryUpdate(
                Metadata(initial.Geometry, displayName: "Renamed window")));

        NativeRemoteWindowSourceSnapshot after = Assert.Single(
            registry.GetSnapshot());
        Assert.Equal(before.Token, after.Token);
        Assert.Equal(before.Source.ActivityId, after.Source.ActivityId);
        Assert.Equal(before.Source.SourceGeneration, after.Source.SourceGeneration);
        Assert.Equal(1, before.GeometryRevision);
        Assert.Equal(1, after.GeometryRevision);
        Assert.Equal("Renamed window", after.Source.DisplayName);
        Assert.Equal(after, lease.Snapshot);
        Assert.True(lease.IsCurrent);
    }

    [Fact]
    public void EverySecurityMetadataChangeRetiresTheExactSourceFromItsUseContext()
    {
        NativeRemoteWindowGeometry geometry =
            NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2);
        NativeRemoteWindowSourceMetadata[] replacements =
        [
            Metadata(
                geometry,
                owningApplicationName: "Replacement application"),
            Metadata(
                NativeRemoteWindowGeometry.Create(10, 20, 1440, 900, 2)),
            Metadata(geometry, supportsCapture: false),
            Metadata(geometry, supportsInput: false),
            Metadata(
                geometry,
                protection: new ProtectionSnapshot(
                    ProtectionKind.ProtectedContent,
                    Now,
                    "test-probe")),
        ];

        foreach (NativeRemoteWindowSourceMetadata replacement in replacements)
        {
            using var registry = new NativeRemoteWindowSourceRegistry(Host);
            using NativeRemoteWindowSourceRegistration registration =
                registry.RegisterGeneric(Metadata(geometry));
            using NativeRemoteWindowSourceLease lease = AcquireLease(
                registry,
                registration.Snapshot);
            Assert.True(
                lease.TryAcquireUseScope(
                    registration.Source.SourceGeneration,
                    registration.Snapshot.GeometryRevision,
                    out NativeRemoteWindowSourceUseScope? acquiredScope));
            using NativeRemoteWindowSourceUseScope scope = Assert.IsType<
                NativeRemoteWindowSourceUseScope>(acquiredScope);

            Assert.False(registration.TryUpdate(replacement));
            Assert.False(lease.IsCurrent);
            Assert.Empty(registry.GetSnapshot());
        }
    }

    [Fact]
    public void StaleGenerationCannotAcquireCurrentToken()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        using NativeRemoteWindowSourceRegistration registration =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
            registry.GetSnapshot());

        bool acquired = registry.TryAcquire(
            snapshot.Token,
            checked(snapshot.Source.SourceGeneration + 1),
            out NativeRemoteWindowSourceLease? lease);

        Assert.False(acquired);
        Assert.Null(lease);
    }

    [Fact]
    public void CallbackAfterSourceCloseCannotRepublishMetadata()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration registration = registry.RegisterGeneric(
            Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        registration.Dispose();

        bool updated = registration.TryUpdate(
            Metadata(NativeRemoteWindowGeometry.Create(10, 10, 1280, 720, 2)));

        Assert.False(updated);
        Assert.Empty(registry.GetSnapshot());
    }

    [Fact]
    public void RegistryDisposalInvalidatesLeasesAndRejectsLaterUse()
    {
        var registry = new NativeRemoteWindowSourceRegistry(Host);
        NativeRemoteWindowSourceRegistration registration = registry.RegisterGeneric(
            Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
            registry.GetSnapshot());
        Assert.True(
            registry.TryAcquire(
                snapshot.Token,
                snapshot.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredLease));
        using NativeRemoteWindowSourceLease lease = Assert.IsType<
            NativeRemoteWindowSourceLease>(acquiredLease);

        registry.Dispose();

        Assert.False(lease.IsCurrent);
        Assert.False(
            registration.TryUpdate(
                Metadata(NativeRemoteWindowGeometry.Create(1, 1, 1280, 720, 2))));
        Assert.Throws<ObjectDisposedException>(() => registry.GetSnapshot());
        registration.Dispose();
        registry.Dispose();
    }

    [Fact]
    public void InvalidatingStatesRemainInsideRegistryCapacityBound()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        var retainedUses = new List<NativeRemoteWindowSourceUseScope>();
        try
        {
            for (int index = 0;
                index < NativeRemoteWindowSourceRegistry.MaximumSources;
                index++)
            {
                NativeRemoteWindowSourceRegistration registration =
                    registry.RegisterGeneric(
                        Metadata(
                            NativeRemoteWindowGeometry.Create(
                                index,
                                0,
                                1280,
                                720,
                                2)));
                using NativeRemoteWindowSourceLease lease = AcquireLease(
                    registry,
                    registration.Snapshot);
                Assert.True(
                    lease.TryAcquireUseScope(
                        registration.Source.SourceGeneration,
                        registration.Snapshot.GeometryRevision,
                        out NativeRemoteWindowSourceUseScope? acquiredScope));
                retainedUses.Add(Assert.IsType<
                    NativeRemoteWindowSourceUseScope>(acquiredScope));
                registration.Dispose();
            }

            Assert.Empty(registry.GetSnapshot());
            Assert.Throws<InvalidOperationException>(
                () => registry.RegisterGeneric(
                    Metadata(
                        NativeRemoteWindowGeometry.Create(
                            0,
                            0,
                            1280,
                            720,
                            2))));
        }
        finally
        {
            for (int index = retainedUses.Count - 1; index >= 0; index--)
            {
                retainedUses[index].Dispose();
            }
        }

        using NativeRemoteWindowSourceRegistration afterDrain =
            registry.RegisterGeneric(
                Metadata(NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
        Assert.Single(registry.GetSnapshot());
    }

    [Fact]
    public void RegistryRejectsSourceBeyondBoundWithoutDisturbingCurrentEntries()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        var registrations = new List<NativeRemoteWindowSourceRegistration>();
        try
        {
            for (int index = 0; index < NativeRemoteWindowSourceRegistry.MaximumSources;
                index++)
            {
                registrations.Add(
                    registry.RegisterGeneric(
                        Metadata(
                            NativeRemoteWindowGeometry.Create(
                                index,
                                0,
                                1280,
                                720,
                                2))));
            }

            Assert.Throws<InvalidOperationException>(
                () => registry.RegisterGeneric(
                    Metadata(
                        NativeRemoteWindowGeometry.Create(
                            0,
                            0,
                            1280,
                            720,
                            2))));
            Assert.Equal(
                NativeRemoteWindowSourceRegistry.MaximumSources,
                registry.GetSnapshot().Count);
        }
        finally
        {
            foreach (NativeRemoteWindowSourceRegistration registration in
                registrations)
            {
                registration.Dispose();
            }
        }
    }

    [Fact]
    public void HostileGeometryIsRejectedBeforeRegistration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NativeRemoteWindowGeometry.Create(double.NaN, 0, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NativeRemoteWindowGeometry.Create(0, double.PositiveInfinity, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NativeRemoteWindowGeometry.Create(0, 0, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NativeRemoteWindowGeometry.Create(
                0,
                0,
                NativeRemoteWindowGeometry.MaximumDimension + 1,
                1,
                1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NativeRemoteWindowGeometry.Create(
                0,
                0,
                1,
                1,
                NativeRemoteWindowGeometry.MaximumScaleFactor + 1));
    }

    [Fact]
    public void ConcurrentAcquireUpdateAndCloseLeavesNoCurrentLease()
    {
        for (int iteration = 0; iteration < 50; iteration++)
        {
            using var registry = new NativeRemoteWindowSourceRegistry(Host);
            NativeRemoteWindowSourceRegistration registration =
                registry.RegisterGeneric(
                    Metadata(
                        NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2)));
            NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
                registry.GetSnapshot());
            var leases = new ConcurrentBag<NativeRemoteWindowSourceLease>();

            Parallel.Invoke(
                () =>
                {
                    for (int attempt = 0; attempt < 20; attempt++)
                    {
                        if (registry.TryAcquire(
                                snapshot.Token,
                                snapshot.Source.SourceGeneration,
                                out NativeRemoteWindowSourceLease? lease))
                        {
                            leases.Add(Assert.IsType<
                                NativeRemoteWindowSourceLease>(lease));
                        }
                    }
                },
                () =>
                {
                    for (int update = 0; update < 20; update++)
                    {
                        _ = registration.TryUpdate(
                            Metadata(
                                NativeRemoteWindowGeometry.Create(
                                    update,
                                    0,
                                    1280,
                                    720,
                                    2)));
                    }
                },
                registration.Dispose);

            Assert.Empty(registry.GetSnapshot());
            Assert.All(leases, static lease => Assert.False(lease.IsCurrent));
            foreach (NativeRemoteWindowSourceLease lease in leases)
            {
                lease.Dispose();
            }
        }
    }

    [Fact]
    public void SourceTokenIsOpaqueAndDiagnosticsOmitDisplayMetadata()
    {
        using var registry = new NativeRemoteWindowSourceRegistry(Host);
        using NativeRemoteWindowSourceRegistration registration =
            registry.RegisterGeneric(
                NativeRemoteWindowSourceMetadata.Create(
                    "title_canary",
                    "application_canary",
                    NativeRemoteWindowGeometry.Create(0, 0, 1280, 720, 2),
                    supportsCapture: true,
                    supportsInput: true,
                    new ProtectionSnapshot(
                        ProtectionKind.Safe,
                        Now,
                        "test-probe")));
        NativeRemoteWindowSourceSnapshot snapshot = Assert.Single(
            registry.GetSnapshot());
        Assert.True(
            registry.TryAcquire(
                snapshot.Token,
                snapshot.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredLease));
        using NativeRemoteWindowSourceLease lease = Assert.IsType<
            NativeRemoteWindowSourceLease>(acquiredLease);
        Assert.True(
            lease.TryRegisterInvalidationCallback(
                static () => { },
                out NativeRemoteWindowSourceInvalidationRegistration?
                    callbackRegistration));
        using NativeRemoteWindowSourceInvalidationRegistration callback =
            Assert.IsType<NativeRemoteWindowSourceInvalidationRegistration>(
                callbackRegistration);
        Assert.True(
            lease.TryAcquireUseScope(
                snapshot.Source.SourceGeneration,
                snapshot.GeometryRevision,
                out NativeRemoteWindowSourceUseScope? acquiredScope));
        using NativeRemoteWindowSourceUseScope scope = Assert.IsType<
            NativeRemoteWindowSourceUseScope>(acquiredScope);

        string serializedSnapshot = JsonSerializer.Serialize(snapshot);
        string serializedCallback = JsonSerializer.Serialize(callback);
        string serializedScope = JsonSerializer.Serialize(scope);

        Assert.DoesNotContain("\"Token\"", serializedSnapshot);
        Assert.DoesNotContain("Token", serializedCallback);
        Assert.DoesNotContain("Token", serializedScope);
        Assert.Equal("{}", JsonSerializer.Serialize(snapshot.Token));
        Assert.Equal("native-source-token", snapshot.Token.ToString());
        Assert.DoesNotContain("title_canary", snapshot.ToString());
        Assert.DoesNotContain("application_canary", snapshot.ToString());
        Assert.DoesNotContain("title_canary", registration.ToString());
        Assert.DoesNotContain("application_canary", lease.ToString());
        Assert.DoesNotContain("title_canary", serializedCallback);
        Assert.DoesNotContain("application_canary", serializedCallback);
        Assert.DoesNotContain("title_canary", callback.ToString());
        Assert.DoesNotContain("application_canary", scope.ToString());
        Assert.DoesNotContain("title_canary", snapshot.Metadata.ToString());
        Assert.DoesNotContain(
            "application_canary",
            snapshot.Metadata.ToString());
    }

    private static NativeRemoteWindowSourceMetadata Metadata(
        NativeRemoteWindowGeometry geometry,
        string displayName = "Generic window",
        string owningApplicationName = "Test application",
        bool supportsCapture = true,
        bool supportsInput = true,
        ProtectionSnapshot? protection = null) =>
        NativeRemoteWindowSourceMetadata.Create(
            displayName,
            owningApplicationName,
            geometry,
            supportsCapture,
            supportsInput,
            protection
                ?? new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "test-probe"));

    private static NativeRemoteWindowSourceLease AcquireLease(
        NativeRemoteWindowSourceRegistry registry,
        NativeRemoteWindowSourceSnapshot snapshot)
    {
        Assert.True(
            registry.TryAcquire(
                snapshot.Token,
                snapshot.Source.SourceGeneration,
                out NativeRemoteWindowSourceLease? acquiredLease));
        return Assert.IsType<NativeRemoteWindowSourceLease>(acquiredLease);
    }

    private static Task RunOnDedicatedThread(Action action)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Factory.StartNew(
                action,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }

    private static Task<T> RunOnDedicatedThread<T>(Func<T> action)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Factory.StartNew(
                action,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }
}
