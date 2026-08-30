using Flowspan.Domain;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSNativeRemoteWindowPermissionBoundaryTests
{
    [Fact]
    public void GrantedViewOnlySnapshotCanBeReservedWithoutPrompt()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot expected = boundary.GetSnapshot();
        var sink = new RecordingPermissionPreparationInvalidationSink();

        NativeRemoteWindowPermissionPreparationReservationResult result =
            ((INativeRemoteWindowPermissionPreparationBoundary)boundary)
            .TryReservePreparation(
                expected,
                MirrorParticipantRole.ViewOnly,
                sink);

        Assert.Equal(
            NativeRemoteWindowPermissionPreparationReservationStatus.Reserved,
            result.Status);
        Assert.True(result.Reserved);
        INativeRemoteWindowPermissionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowPermissionPreparationRegistration>(
                    result.Registration);
        Assert.Same(registration, sink.Registration);
        Assert.True(registration.IsCurrent);
        Assert.Equal(1, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);

        registration.Dispose();

        Assert.False(registration.IsCurrent);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void OwnerClaimFailureRollsBackRegistrationAndAllowsRetry()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot expected = boundary.GetSnapshot();
        var failure = new IOException("permission-owner-claim-failure");
        var failingSink = new RecordingPermissionPreparationInvalidationSink(
            ownershipFailure: failure);
        INativeRemoteWindowPermissionPreparationBoundary preparationBoundary =
            boundary;

        IOException thrown = Assert.Throws<IOException>(() =>
            preparationBoundary.TryReservePreparation(
                expected,
                MirrorParticipantRole.ViewOnly,
                failingSink));

        Assert.Same(failure, thrown);
        Assert.False(failingSink.Registration?.IsCurrent);
        Assert.Equal(0, failingSink.Count);
        var replacementSink =
            new RecordingPermissionPreparationInvalidationSink();
        NativeRemoteWindowPermissionPreparationReservationResult replacement =
            preparationBoundary.TryReservePreparation(
                expected,
                MirrorParticipantRole.ViewOnly,
                replacementSink);
        Assert.True(replacement.Reserved);
        Assert.Same(replacement.Registration, replacementSink.Registration);
    }

    [Fact]
    public void PermissionMutationInvalidatesReservationBeforeChangedObserver()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot expected = boundary.GetSnapshot();
        var sink = new RecordingPermissionPreparationInvalidationSink();
        NativeRemoteWindowPermissionPreparationReservationResult result =
            ((INativeRemoteWindowPermissionPreparationBoundary)boundary)
            .TryReservePreparation(
                expected,
                MirrorParticipantRole.ViewOnly,
                sink);
        INativeRemoteWindowPermissionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowPermissionPreparationRegistration>(
                    result.Registration);
        bool observerSawInvalidation = false;
        boundary.Changed += _ =>
        {
            observerSawInvalidation = !registration.IsCurrent
                && sink.Count == 1;
        };
        interop.PreflightResult = false;

        NativeRemoteWindowPermissionSnapshot revoked = boundary.GetSnapshot();

        Assert.Equal(
            NativeRemoteWindowPermissionState.Revoked,
            revoked.Capture);
        Assert.False(registration.IsCurrent);
        Assert.Equal(1, sink.Count);
        Assert.True(observerSawInvalidation);
    }

    [Fact]
    public async Task DisposeInvalidatesAllPermissionReservationsInOrder()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot expected = boundary.GetSnapshot();
        var order = new List<int>();
        var firstSink = new RecordingPermissionPreparationInvalidationSink(
            () => order.Add(1));
        var secondSink = new RecordingPermissionPreparationInvalidationSink(
            () => order.Add(2));
        INativeRemoteWindowPermissionPreparationBoundary preparationBoundary =
            boundary;
        INativeRemoteWindowPermissionPreparationRegistration first =
            Assert.IsAssignableFrom<
                INativeRemoteWindowPermissionPreparationRegistration>(
                    preparationBoundary.TryReservePreparation(
                        expected,
                        MirrorParticipantRole.ViewOnly,
                        firstSink).Registration);
        INativeRemoteWindowPermissionPreparationRegistration second =
            Assert.IsAssignableFrom<
                INativeRemoteWindowPermissionPreparationRegistration>(
                    preparationBoundary.TryReservePreparation(
                        expected,
                        MirrorParticipantRole.ViewOnly,
                        secondSink).Registration);

        await boundary.DisposeAsync();

        Assert.False(first.IsCurrent);
        Assert.False(second.IsCurrent);
        Assert.Equal(1, firstSink.Count);
        Assert.Equal(1, secondSink.Count);
        Assert.Equal([1, 2], order);
    }

    [Fact]
    public void DriverEligibleReservationReportsUnavailableInputCapability()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot expected = boundary.GetSnapshot();
        var sink = new RecordingPermissionPreparationInvalidationSink();

        NativeRemoteWindowPermissionPreparationReservationResult result =
            ((INativeRemoteWindowPermissionPreparationBoundary)boundary)
            .TryReservePreparation(
                expected,
                MirrorParticipantRole.DriverEligible,
                sink);

        Assert.Equal(
            NativeRemoteWindowPermissionPreparationReservationStatus
                .BoundaryUnavailable,
            result.Status);
        Assert.False(result.Reserved);
        Assert.Null(result.Registration);
        Assert.Equal(0, sink.Count);
        Assert.Equal(1, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public void PreparationReservationRequiresTheExactPermissionSnapshot()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(
            interop,
            ownerGeneration: 7);
        NativeRemoteWindowPermissionSnapshot current = boundary.GetSnapshot();
        NativeRemoteWindowPermissionSnapshot[] mismatches =
        [
            NativeRemoteWindowPermissionSnapshot.Create(
                current.Capture,
                current.Input,
                ownerGeneration: 8,
                current.Revision),
            NativeRemoteWindowPermissionSnapshot.Create(
                current.Capture,
                current.Input,
                current.OwnerGeneration,
                checked(current.Revision + 1)),
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Denied,
                current.Input,
                current.OwnerGeneration,
                current.Revision),
            NativeRemoteWindowPermissionSnapshot.Create(
                current.Capture,
                NativeRemoteWindowPermissionState.Granted,
                current.OwnerGeneration,
                current.Revision),
        ];
        var sink = new RecordingPermissionPreparationInvalidationSink();
        INativeRemoteWindowPermissionPreparationBoundary preparationBoundary =
            boundary;

        foreach (NativeRemoteWindowPermissionSnapshot mismatch in mismatches)
        {
            NativeRemoteWindowPermissionPreparationReservationResult result =
                preparationBoundary.TryReservePreparation(
                    mismatch,
                    MirrorParticipantRole.ViewOnly,
                    sink);

            Assert.Equal(
                NativeRemoteWindowPermissionPreparationReservationStatus
                    .SnapshotChanged,
                result.Status);
            Assert.Null(result.Registration);
        }

        Assert.Equal(0, sink.Count);
        Assert.Equal(1, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public async Task ViewOnlyReservationRequiresGrantedCapturePermission()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = false,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot denied =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);
        var sink = new RecordingPermissionPreparationInvalidationSink();

        NativeRemoteWindowPermissionPreparationReservationResult result =
            ((INativeRemoteWindowPermissionPreparationBoundary)boundary)
            .TryReservePreparation(
                denied,
                MirrorParticipantRole.ViewOnly,
                sink);

        Assert.Equal(
            NativeRemoteWindowPermissionPreparationReservationStatus
                .PermissionDenied,
            result.Status);
        Assert.False(result.Reserved);
        Assert.Null(result.Registration);
        Assert.Equal(0, sink.Count);
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(1, interop.RequestCalls);
    }

    [Fact]
    public void SamePermissionFactPreservesRevisionAndReservation()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot granted = boundary.GetSnapshot();
        var sink = new RecordingPermissionPreparationInvalidationSink();
        NativeRemoteWindowPermissionPreparationReservationResult result =
            ((INativeRemoteWindowPermissionPreparationBoundary)boundary)
            .TryReservePreparation(
                granted,
                MirrorParticipantRole.ViewOnly,
                sink);
        using INativeRemoteWindowPermissionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowPermissionPreparationRegistration>(
                    result.Registration);

        NativeRemoteWindowPermissionSnapshot repeated = boundary.GetSnapshot();

        Assert.Same(granted, repeated);
        Assert.Equal(granted.Revision, repeated.Revision);
        Assert.True(registration.IsCurrent);
        Assert.Equal(0, sink.Count);
        Assert.Equal(2, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public void RevokedThenRegrantedPermissionDoesNotReviveOldReservation()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot firstGrant = boundary.GetSnapshot();
        var sink = new RecordingPermissionPreparationInvalidationSink();
        NativeRemoteWindowPermissionPreparationReservationResult firstResult =
            ((INativeRemoteWindowPermissionPreparationBoundary)boundary)
            .TryReservePreparation(
                firstGrant,
                MirrorParticipantRole.ViewOnly,
                sink);
        INativeRemoteWindowPermissionPreparationRegistration firstRegistration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowPermissionPreparationRegistration>(
                    firstResult.Registration);
        interop.PreflightResult = false;
        NativeRemoteWindowPermissionSnapshot revoked = boundary.GetSnapshot();
        interop.PreflightResult = true;

        NativeRemoteWindowPermissionSnapshot secondGrant = boundary.GetSnapshot();
        NativeRemoteWindowPermissionPreparationReservationResult staleResult =
            ((INativeRemoteWindowPermissionPreparationBoundary)boundary)
            .TryReservePreparation(
                firstGrant,
                MirrorParticipantRole.ViewOnly,
                new RecordingPermissionPreparationInvalidationSink());

        Assert.Equal(1, firstGrant.Revision);
        Assert.Equal(2, revoked.Revision);
        Assert.Equal(3, secondGrant.Revision);
        Assert.Equal(
            NativeRemoteWindowPermissionState.Granted,
            secondGrant.Capture);
        Assert.False(firstRegistration.IsCurrent);
        Assert.Equal(1, sink.Count);
        Assert.Equal(
            NativeRemoteWindowPermissionPreparationReservationStatus
                .SnapshotChanged,
            staleResult.Status);
        Assert.Null(staleResult.Registration);
    }

    [Fact]
    public async Task ReservationThatWinsRaceIsInvalidatedByLaterCommit()
    {
        using var mutationObserved = new ManualResetEventSlim();
        using var releaseMutation = new ManualResetEventSlim();
        int observation = 0;
        var interop = new RecordingScreenCapturePermissionInterop
        {
            Preflight = () =>
            {
                if (Interlocked.Increment(ref observation) == 1)
                {
                    return true;
                }

                mutationObserved.Set();
                if (!releaseMutation.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException(
                        "Timed out releasing permission mutation.");
                }

                return false;
            },
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot granted = boundary.GetSnapshot();
        var sink = new RecordingPermissionPreparationInvalidationSink();
        Task<NativeRemoteWindowPermissionSnapshot> mutation = Task.Run(
            boundary.GetSnapshot);
        Assert.True(mutationObserved.Wait(TimeSpan.FromSeconds(5)));

        NativeRemoteWindowPermissionPreparationReservationResult result;
        try
        {
            result =
                ((INativeRemoteWindowPermissionPreparationBoundary)boundary)
                .TryReservePreparation(
                    granted,
                    MirrorParticipantRole.ViewOnly,
                    sink);
        }
        finally
        {
            releaseMutation.Set();
        }

        NativeRemoteWindowPermissionSnapshot revoked =
            await mutation.WaitAsync(TimeSpan.FromSeconds(5));
        INativeRemoteWindowPermissionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowPermissionPreparationRegistration>(
                    result.Registration);
        Assert.Equal(
            NativeRemoteWindowPermissionPreparationReservationStatus.Reserved,
            result.Status);
        Assert.Equal(
            NativeRemoteWindowPermissionState.Revoked,
            revoked.Capture);
        Assert.False(registration.IsCurrent);
        Assert.Equal(1, sink.Count);
        Assert.Equal(2, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public async Task CommitThatWinsRaceRejectsReservationAgainstNewDeniedFact()
    {
        using var invalidationEntered = new ManualResetEventSlim();
        using var releaseInvalidation = new ManualResetEventSlim();
        using var reservationAttempted = new ManualResetEventSlim();
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot granted = boundary.GetSnapshot();
        var oldSink = new RecordingPermissionPreparationInvalidationSink(() =>
        {
            invalidationEntered.Set();
            if (!releaseInvalidation.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException(
                    "Timed out releasing permission invalidation.");
            }
        });
        NativeRemoteWindowPermissionPreparationReservationResult oldResult =
            ((INativeRemoteWindowPermissionPreparationBoundary)boundary)
            .TryReservePreparation(
                granted,
                MirrorParticipantRole.ViewOnly,
                oldSink);
        INativeRemoteWindowPermissionPreparationRegistration oldRegistration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowPermissionPreparationRegistration>(
                    oldResult.Registration);
        NativeRemoteWindowPermissionSnapshot expectedRevoked =
            NativeRemoteWindowPermissionSnapshot.Create(
                NativeRemoteWindowPermissionState.Revoked,
                NativeRemoteWindowPermissionState.Unsupported,
                granted.OwnerGeneration,
                checked(granted.Revision + 1));
        interop.PreflightResult = false;
        Task<NativeRemoteWindowPermissionSnapshot> mutation = Task.Run(
            boundary.GetSnapshot);
        Assert.True(invalidationEntered.Wait(TimeSpan.FromSeconds(5)));
        var newSink = new RecordingPermissionPreparationInvalidationSink();
        Task<NativeRemoteWindowPermissionPreparationReservationResult>
            reservation = Task.Run(() =>
            {
                reservationAttempted.Set();
                return ((INativeRemoteWindowPermissionPreparationBoundary)
                    boundary).TryReservePreparation(
                        expectedRevoked,
                        MirrorParticipantRole.ViewOnly,
                        newSink);
            });
        Assert.True(reservationAttempted.Wait(TimeSpan.FromSeconds(5)));

        try
        {
            Task first = await Task.WhenAny(
                reservation,
                Task.Delay(TimeSpan.FromMilliseconds(100)));
            Assert.NotSame(reservation, first);
        }
        finally
        {
            releaseInvalidation.Set();
        }

        NativeRemoteWindowPermissionSnapshot revoked =
            await mutation.WaitAsync(TimeSpan.FromSeconds(5));
        NativeRemoteWindowPermissionPreparationReservationResult result =
            await reservation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expectedRevoked, revoked);
        Assert.False(oldRegistration.IsCurrent);
        Assert.Equal(1, oldSink.Count);
        Assert.Equal(
            NativeRemoteWindowPermissionPreparationReservationStatus
                .PermissionDenied,
            result.Status);
        Assert.Null(result.Registration);
        Assert.Equal(0, newSink.Count);
    }

    [Fact]
    public void SinkFailureCannotBlockOtherInvalidationsOrChangedObservers()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot granted = boundary.GetSnapshot();
        var order = new List<int>();
        var failure = new IOException("permission-invalidation-failure");
        INativeRemoteWindowPermissionPreparationRegistration first = null!;
        INativeRemoteWindowPermissionPreparationRegistration second = null!;
        var firstSink = new RecordingPermissionPreparationInvalidationSink(
            () =>
            {
                Assert.False(first.IsCurrent);
                Assert.False(second.IsCurrent);
                order.Add(1);
            },
            failure);
        var secondSink = new RecordingPermissionPreparationInvalidationSink(
            () =>
            {
                Assert.False(first.IsCurrent);
                Assert.False(second.IsCurrent);
                order.Add(2);
            });
        INativeRemoteWindowPermissionPreparationBoundary preparationBoundary =
            boundary;
        first = Assert.IsAssignableFrom<
            INativeRemoteWindowPermissionPreparationRegistration>(
                preparationBoundary.TryReservePreparation(
                    granted,
                    MirrorParticipantRole.ViewOnly,
                    firstSink).Registration);
        second = Assert.IsAssignableFrom<
            INativeRemoteWindowPermissionPreparationRegistration>(
                preparationBoundary.TryReservePreparation(
                    granted,
                    MirrorParticipantRole.ViewOnly,
                    secondSink).Registration);
        boundary.Changed += snapshot =>
        {
            Assert.Equal(
                NativeRemoteWindowPermissionState.Revoked,
                snapshot.Capture);
            Assert.False(first.IsCurrent);
            Assert.False(second.IsCurrent);
            order.Add(3);
        };
        interop.PreflightResult = false;

        IOException thrown = Assert.Throws<IOException>(boundary.GetSnapshot);

        Assert.Same(failure, thrown);
        Assert.False(first.IsCurrent);
        Assert.False(second.IsCurrent);
        Assert.Equal(1, firstSink.Count);
        Assert.Equal(1, secondSink.Count);
        Assert.Equal([1, 2, 3], order);
        NativeRemoteWindowPermissionSnapshot committed = boundary.GetSnapshot();
        Assert.Equal(
            NativeRemoteWindowPermissionState.Revoked,
            committed.Capture);
        Assert.Equal(2, committed.Revision);
    }

    [Fact]
    public void MultipleSinkFailuresAreAggregatedInRegistrationOrder()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot granted = boundary.GetSnapshot();
        var firstFailure = new IOException("first-permission-failure");
        var secondFailure = new InvalidOperationException(
            "second-permission-failure");
        INativeRemoteWindowPermissionPreparationBoundary preparationBoundary =
            boundary;
        INativeRemoteWindowPermissionPreparationRegistration first =
            Assert.IsAssignableFrom<
                INativeRemoteWindowPermissionPreparationRegistration>(
                    preparationBoundary.TryReservePreparation(
                        granted,
                        MirrorParticipantRole.ViewOnly,
                        new RecordingPermissionPreparationInvalidationSink(
                            failure: firstFailure)).Registration);
        INativeRemoteWindowPermissionPreparationRegistration second =
            Assert.IsAssignableFrom<
                INativeRemoteWindowPermissionPreparationRegistration>(
                    preparationBoundary.TryReservePreparation(
                        granted,
                        MirrorParticipantRole.ViewOnly,
                        new RecordingPermissionPreparationInvalidationSink(
                            failure: secondFailure)).Registration);
        interop.PreflightResult = false;

        AggregateException aggregate = Assert.Throws<AggregateException>(
            boundary.GetSnapshot);

        Assert.Equal([firstFailure, secondFailure], aggregate.InnerExceptions);
        Assert.False(first.IsCurrent);
        Assert.False(second.IsCurrent);
        Assert.Equal(
            NativeRemoteWindowPermissionState.Revoked,
            boundary.GetSnapshot().Capture);
    }

    [Fact]
    public async Task DisposeRetainsStableInvalidationFailure()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot granted = boundary.GetSnapshot();
        var failure = new IOException("dispose-permission-failure");
        var sink = new RecordingPermissionPreparationInvalidationSink(
            failure: failure);
        INativeRemoteWindowPermissionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowPermissionPreparationRegistration>(
                    ((INativeRemoteWindowPermissionPreparationBoundary)boundary)
                    .TryReservePreparation(
                        granted,
                        MirrorParticipantRole.ViewOnly,
                        sink).Registration);

        IOException first = await Assert.ThrowsAsync<IOException>(
            async () => await boundary.DisposeAsync());
        IOException repeated = await Assert.ThrowsAsync<IOException>(
            async () => await boundary.DisposeAsync());

        Assert.Same(failure, first);
        Assert.Same(first, repeated);
        Assert.False(registration.IsCurrent);
        Assert.Equal(1, sink.Count);
    }

    [Fact]
    public async Task DisposeRetainsStableOutOfMemoryFailure()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot granted = boundary.GetSnapshot();
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var failure = new OutOfMemoryException(
            "fatal-dispose-permission-invalidation-failure");
#pragma warning restore CA2201
        var sink = new RecordingPermissionPreparationInvalidationSink(
            failure: failure);
        INativeRemoteWindowPermissionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowPermissionPreparationRegistration>(
                    ((INativeRemoteWindowPermissionPreparationBoundary)boundary)
                    .TryReservePreparation(
                        granted,
                        MirrorParticipantRole.ViewOnly,
                        sink).Registration);

        OutOfMemoryException first = await Assert.ThrowsAsync<OutOfMemoryException>(
            async () => await boundary.DisposeAsync());
        OutOfMemoryException repeated =
            await Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await boundary.DisposeAsync());

        Assert.Same(failure, first);
        Assert.Same(first, repeated);
        Assert.False(registration.IsCurrent);
        Assert.Equal(1, sink.Count);
    }

    [Fact]
    public void OutOfMemoryFromInvalidationEscapesRawAfterAllDeactivation()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot granted = boundary.GetSnapshot();
#pragma warning disable CA2201 // Intentional fatal-runtime injection.
        var failure = new OutOfMemoryException(
            "fatal-permission-invalidation-failure");
#pragma warning restore CA2201
        INativeRemoteWindowPermissionPreparationBoundary preparationBoundary =
            boundary;
        INativeRemoteWindowPermissionPreparationRegistration first =
            Assert.IsAssignableFrom<
                INativeRemoteWindowPermissionPreparationRegistration>(
                    preparationBoundary.TryReservePreparation(
                        granted,
                        MirrorParticipantRole.ViewOnly,
                        new RecordingPermissionPreparationInvalidationSink(
                            failure: failure)).Registration);
        INativeRemoteWindowPermissionPreparationRegistration second =
            Assert.IsAssignableFrom<
                INativeRemoteWindowPermissionPreparationRegistration>(
                    preparationBoundary.TryReservePreparation(
                        granted,
                        MirrorParticipantRole.ViewOnly,
                        new RecordingPermissionPreparationInvalidationSink())
                    .Registration);
        interop.PreflightResult = false;

        OutOfMemoryException thrown = Assert.Throws<OutOfMemoryException>(
            boundary.GetSnapshot);

        Assert.Same(failure, thrown);
        Assert.False(first.IsCurrent);
        Assert.False(second.IsCurrent);
        Assert.Equal(
            NativeRemoteWindowPermissionState.Revoked,
            boundary.GetSnapshot().Capture);
    }

    [Fact]
    public async Task DisposedBoundaryRejectsPreparationAsUnavailable()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot expected = boundary.GetSnapshot();
        await boundary.DisposeAsync();
        var sink = new RecordingPermissionPreparationInvalidationSink();

        NativeRemoteWindowPermissionPreparationReservationResult result =
            ((INativeRemoteWindowPermissionPreparationBoundary)boundary)
            .TryReservePreparation(
                expected,
                MirrorParticipantRole.ViewOnly,
                sink);

        Assert.Equal(
            NativeRemoteWindowPermissionPreparationReservationStatus
                .BoundaryUnavailable,
            result.Status);
        Assert.Null(result.Registration);
        Assert.Equal(0, sink.Count);
        Assert.Equal(1, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public void SnapshotPreflightsCaptureWithoutRequestingPermission()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        NativeRemoteWindowPermissionSnapshot snapshot = boundary.GetSnapshot();

        Assert.Equal(NativeRemoteWindowPermissionState.Granted, snapshot.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Unsupported, snapshot.Input);
        Assert.Equal(1, snapshot.OwnerGeneration);
        Assert.Equal(1, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public void InitialAbsentPreflightRemainsNotDetermined()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = false,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        NativeRemoteWindowPermissionSnapshot snapshot = boundary.GetSnapshot();

        Assert.Equal(
            NativeRemoteWindowPermissionState.NotDetermined,
            snapshot.Capture);
        Assert.Equal(1, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Theory]
    [InlineData(true, NativeRemoteWindowPermissionState.Granted)]
    [InlineData(false, NativeRemoteWindowPermissionState.Denied)]
    public async Task ExplicitCaptureRequestMapsNativeDecision(
        bool nativeDecision,
        NativeRemoteWindowPermissionState expectedState)
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = nativeDecision,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        NativeRemoteWindowPermissionSnapshot snapshot =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);

        Assert.Equal(expectedState, snapshot.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Unsupported, snapshot.Input);
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(1, interop.RequestCalls);
    }

    [Fact]
    public async Task GrantedCaptureBecomesRevokedWhenPreflightLosesAccess()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = true,
            PreflightResult = false,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot granted =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);

        NativeRemoteWindowPermissionSnapshot revoked = boundary.GetSnapshot();

        Assert.Equal(NativeRemoteWindowPermissionState.Granted, granted.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Revoked, revoked.Capture);
        Assert.True(revoked.Revision > granted.Revision);
        Assert.Equal(1, interop.RequestCalls);
        Assert.Equal(1, interop.PreflightCalls);
    }

    [Fact]
    public async Task RevokedCaptureStaysRevokedWhilePreflightRemainsAbsent()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = true,
            PreflightResult = false,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        _ = await boundary.RequestCapturePermissionAsync(CancellationToken.None);
        NativeRemoteWindowPermissionSnapshot first = boundary.GetSnapshot();

        NativeRemoteWindowPermissionSnapshot second = boundary.GetSnapshot();

        Assert.Equal(NativeRemoteWindowPermissionState.Revoked, first.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Revoked, second.Capture);
        Assert.Equal(2, interop.PreflightCalls);
    }

    [Fact]
    public async Task RepeatedDeniedFactDoesNotAdvanceRevisionOrPublishChange()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = false,
            PreflightResult = false,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        var changes = new List<NativeRemoteWindowPermissionSnapshot>();
        boundary.Changed += changes.Add;
        NativeRemoteWindowPermissionSnapshot denied =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);

        NativeRemoteWindowPermissionSnapshot repeated = boundary.GetSnapshot();

        Assert.Equal(NativeRemoteWindowPermissionState.Denied, denied.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Denied, repeated.Capture);
        Assert.Same(denied, repeated);
        Assert.Equal(denied.Revision, repeated.Revision);
        Assert.Single(changes);
        Assert.Same(denied, changes[0]);
    }

    [Fact]
    public async Task TemporaryFailureDoesNotForgetPriorGrant()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = true,
            PreflightResult = false,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot granted =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);
        interop.PreflightFailure = new IOException("temporary-native-failure");
        NativeRemoteWindowPermissionSnapshot unavailable = boundary.GetSnapshot();
        interop.PreflightFailure = null;

        NativeRemoteWindowPermissionSnapshot recovered = boundary.GetSnapshot();

        Assert.Equal(NativeRemoteWindowPermissionState.Granted, granted.Capture);
        Assert.Equal(
            NativeRemoteWindowPermissionState.Unavailable,
            unavailable.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Revoked, recovered.Capture);
    }

    [Fact]
    public async Task OlderPreflightCannotOverwriteNewerDeniedRequest()
    {
        using var preflightEntered = new ManualResetEventSlim();
        using var releasePreflight = new ManualResetEventSlim();
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = false,
            Preflight = () =>
            {
                preflightEntered.Set();
                if (!releasePreflight.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Timed out releasing preflight.");
                }

                return true;
            },
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        var changes = new List<NativeRemoteWindowPermissionSnapshot>();
        boundary.Changed += changes.Add;
        Task<NativeRemoteWindowPermissionSnapshot> older = Task.Run(
            boundary.GetSnapshot);
        Assert.True(preflightEntered.Wait(TimeSpan.FromSeconds(5)));

        NativeRemoteWindowPermissionSnapshot denied;
        try
        {
            denied = await boundary.RequestCapturePermissionAsync(
                CancellationToken.None);
        }
        finally
        {
            releasePreflight.Set();
        }

        NativeRemoteWindowPermissionSnapshot stale =
            await older.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(NativeRemoteWindowPermissionState.Denied, denied.Capture);
        Assert.Same(denied, stale);
        Assert.Single(changes);
        Assert.Same(denied, changes[0]);
    }

    [Fact]
    public async Task OlderDeniedRequestCannotOverwriteNewerGrantedPreflight()
    {
        using var requestEntered = new ManualResetEventSlim();
        using var releaseRequest = new ManualResetEventSlim();
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
            Request = () =>
            {
                requestEntered.Set();
                if (!releaseRequest.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Timed out releasing request.");
                }

                return false;
            },
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        var changes = new List<NativeRemoteWindowPermissionSnapshot>();
        boundary.Changed += changes.Add;
        Task<NativeRemoteWindowPermissionSnapshot> older = Task.Run(
            async () => await boundary.RequestCapturePermissionAsync(
                CancellationToken.None));
        Assert.True(requestEntered.Wait(TimeSpan.FromSeconds(5)));

        NativeRemoteWindowPermissionSnapshot granted;
        try
        {
            granted = boundary.GetSnapshot();
        }
        finally
        {
            releaseRequest.Set();
        }

        NativeRemoteWindowPermissionSnapshot stale =
            await older.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(NativeRemoteWindowPermissionState.Granted, granted.Capture);
        Assert.Same(granted, stale);
        Assert.Single(changes);
        Assert.Same(granted, changes[0]);
    }

    [Fact]
    public async Task OlderGrantCannotRestoreNewerRevokedPreflight()
    {
        using var requestEntered = new ManualResetEventSlim();
        using var releaseRequest = new ManualResetEventSlim();
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = true,
            PreflightResult = false,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        var changes = new List<NativeRemoteWindowPermissionSnapshot>();
        boundary.Changed += changes.Add;
        NativeRemoteWindowPermissionSnapshot granted =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);
        interop.Request = () =>
        {
            requestEntered.Set();
            if (!releaseRequest.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Timed out releasing request.");
            }

            return true;
        };
        Task<NativeRemoteWindowPermissionSnapshot> older = Task.Run(
            async () => await boundary.RequestCapturePermissionAsync(
                CancellationToken.None));
        Assert.True(requestEntered.Wait(TimeSpan.FromSeconds(5)));

        NativeRemoteWindowPermissionSnapshot revoked;
        try
        {
            revoked = boundary.GetSnapshot();
        }
        finally
        {
            releaseRequest.Set();
        }

        NativeRemoteWindowPermissionSnapshot stale =
            await older.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(NativeRemoteWindowPermissionState.Granted, granted.Capture);
        Assert.Equal(NativeRemoteWindowPermissionState.Revoked, revoked.Capture);
        Assert.Equal(1, granted.Revision);
        Assert.Equal(2, revoked.Revision);
        Assert.Same(revoked, stale);
        Assert.Equal([granted, revoked], changes);
    }

    [Fact]
    public void ThrowingObserverCannotBlockLaterSafetyObserver()
    {
        const string canary = "FLOWSPAN_PERMISSION_OBSERVER_CANARY";
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot? safetyObservation = null;
        boundary.Changed += _ => throw new InvalidOperationException(canary);
        boundary.Changed += snapshot => safetyObservation = snapshot;

        NativeRemoteWindowPermissionSnapshot published = boundary.GetSnapshot();

        Assert.Same(published, safetyObservation);
        Assert.Equal(1, published.Revision);
        Assert.Equal(NativeRemoteWindowPermissionState.Granted, published.Capture);
    }

    [Fact]
    public void ObserverCanReenterPermissionReadWithoutStateLockDeadlock()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        NativeRemoteWindowPermissionSnapshot? reentered = null;
        boundary.Changed += snapshot =>
        {
            if (snapshot.Capture == NativeRemoteWindowPermissionState.Granted)
            {
                interop.PreflightResult = false;
                reentered = boundary.GetSnapshot();
            }
        };

        NativeRemoteWindowPermissionSnapshot initial = boundary.GetSnapshot();

        Assert.Equal(NativeRemoteWindowPermissionState.Granted, initial.Capture);
        Assert.Equal(1, initial.Revision);
        Assert.NotNull(reentered);
        Assert.Equal(NativeRemoteWindowPermissionState.Revoked, reentered.Capture);
        Assert.Equal(2, reentered.Revision);
    }

    [Fact]
    public async Task DisposedBoundaryRejectsNewOperationsBeforeNativeCalls()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
            RequestResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        await boundary.DisposeAsync();
        await boundary.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(boundary.GetSnapshot);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await boundary.RequestCapturePermissionAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await boundary.RequestInputPermissionAsync(CancellationToken.None));
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public async Task DisposeRejectsBlockedNativeCompletionWithoutNotification()
    {
        using var preflightEntered = new ManualResetEventSlim();
        using var releasePreflight = new ManualResetEventSlim();
        var interop = new RecordingScreenCapturePermissionInterop
        {
            Preflight = () =>
            {
                preflightEntered.Set();
                if (!releasePreflight.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Timed out releasing preflight.");
                }

                return true;
            },
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        int notifications = 0;
        Action<NativeRemoteWindowPermissionSnapshot> observer =
            _ => notifications++;
        boundary.Changed += observer;
        Task<NativeRemoteWindowPermissionSnapshot> reading = Task.Run(
            boundary.GetSnapshot);
        Assert.True(preflightEntered.Wait(TimeSpan.FromSeconds(5)));

        try
        {
            await boundary.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() =>
                boundary.Changed += _ => { });
            boundary.Changed -= observer;
        }
        finally
        {
            releasePreflight.Set();
        }

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await reading.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, notifications);
        Assert.Equal(1, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public async Task PreCancellationWinsAfterDisposeWithoutNativeCalls()
    {
        var interop = new RecordingScreenCapturePermissionInterop();
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        await boundary.DisposeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        OperationCanceledException capture =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await boundary.RequestCapturePermissionAsync(
                    cancellation.Token));
        OperationCanceledException input =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await boundary.RequestInputPermissionAsync(
                    cancellation.Token));

        Assert.Equal(cancellation.Token, capture.CancellationToken);
        Assert.Equal(cancellation.Token, input.CancellationToken);
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public async Task UnsupportedRuntimeDoesNotCrossNativeBoundary()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            IsSupported = false,
            PreflightResult = true,
            RequestResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        NativeRemoteWindowPermissionSnapshot preflight = boundary.GetSnapshot();
        NativeRemoteWindowPermissionSnapshot capture =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);
        NativeRemoteWindowPermissionSnapshot input =
            await boundary.RequestInputPermissionAsync(CancellationToken.None);

        Assert.Equal(
            NativeRemoteWindowPermissionState.Unsupported,
            preflight.Capture);
        Assert.Equal(
            NativeRemoteWindowPermissionState.Unsupported,
            capture.Capture);
        Assert.Equal(
            NativeRemoteWindowPermissionState.Unsupported,
            input.Input);
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public void PreflightFailureReturnsRedactedUnavailableFact()
    {
        const string canary = "FLOWSPAN_CAPTURE_PREFLIGHT_CANARY";
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightFailure = new IOException(canary),
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        NativeRemoteWindowPermissionSnapshot snapshot = boundary.GetSnapshot();

        Assert.Equal(
            NativeRemoteWindowPermissionState.Unavailable,
            snapshot.Capture);
        Assert.DoesNotContain(canary, snapshot.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public async Task RequestFailureReturnsRedactedUnavailableFact()
    {
        const string canary = "FLOWSPAN_CAPTURE_REQUEST_CANARY";
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestFailure = new EntryPointNotFoundException(canary),
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        NativeRemoteWindowPermissionSnapshot snapshot =
            await boundary.RequestCapturePermissionAsync(CancellationToken.None);

        Assert.Equal(
            NativeRemoteWindowPermissionState.Unavailable,
            snapshot.Capture);
        Assert.DoesNotContain(canary, snapshot.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(1, interop.RequestCalls);
    }

    [Fact]
    public async Task PreCancelledCaptureRequestDoesNotCrossNativeBoundary()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            RequestResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        OperationCanceledException cancellationException =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await boundary.RequestCapturePermissionAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, cancellationException.CancellationToken);
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public async Task InputRequestStaysUnsupportedWithoutScreenCaptureCall()
    {
        var interop = new RecordingScreenCapturePermissionInterop
        {
            PreflightResult = true,
            RequestResult = true,
        };
        var boundary = new MacOSNativeRemoteWindowPermissionBoundary(interop);

        NativeRemoteWindowPermissionSnapshot snapshot =
            await boundary.RequestInputPermissionAsync(CancellationToken.None);

        Assert.Equal(NativeRemoteWindowPermissionState.Unsupported, snapshot.Input);
        Assert.Equal(0, interop.PreflightCalls);
        Assert.Equal(0, interop.RequestCalls);
    }

    [Fact]
    public async Task ProductionBoundaryPreflightsCoreGraphicsOnMatchingHost()
    {
        await using var boundary =
            new MacOSNativeRemoteWindowPermissionBoundary();

        NativeRemoteWindowPermissionSnapshot snapshot = boundary.GetSnapshot();

        if (OperatingSystem.IsMacOSVersionAtLeast(10, 15))
        {
            Assert.True(
                snapshot.Capture is NativeRemoteWindowPermissionState.Granted
                    or NativeRemoteWindowPermissionState.NotDetermined,
                $"Unexpected matching-host capture state: {snapshot.Capture}");
        }
        else
        {
            Assert.Equal(
                NativeRemoteWindowPermissionState.Unsupported,
                snapshot.Capture);
        }

        Assert.Equal(NativeRemoteWindowPermissionState.Unsupported, snapshot.Input);
    }

    private sealed class RecordingScreenCapturePermissionInterop :
        IMacOSScreenCapturePermissionInterop
    {
        public bool IsSupported { get; init; } = true;

        public bool PreflightResult { get; set; }

        public Func<bool>? Preflight { get; init; }

        public Exception? PreflightFailure { get; set; }

        public bool RequestResult { get; init; }

        public Func<bool>? Request { get; set; }

        public Exception? RequestFailure { get; init; }

        public int PreflightCalls { get; private set; }

        public int RequestCalls { get; private set; }

        public bool PreflightScreenCaptureAccess()
        {
            PreflightCalls++;
            if (PreflightFailure is not null)
            {
                throw PreflightFailure;
            }

            return Preflight?.Invoke() ?? PreflightResult;
        }

        public bool RequestScreenCaptureAccess()
        {
            RequestCalls++;
            if (RequestFailure is not null)
            {
                throw RequestFailure;
            }

            return Request?.Invoke() ?? RequestResult;
        }
    }

    private sealed class RecordingPermissionPreparationInvalidationSink(
        Action? invalidating = null,
        Exception? failure = null,
        Exception? ownershipFailure = null) :
        INativeRemoteWindowPermissionPreparationInvalidationSink
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public INativeRemoteWindowPermissionPreparationRegistration?
            Registration
        { get; private set; }

        public void OwnNativeRemoteWindowPermissionPreparationRegistration(
            INativeRemoteWindowPermissionPreparationRegistration registration)
        {
            Registration = registration;
            if (ownershipFailure is not null)
            {
                throw ownershipFailure;
            }
        }

        public void InvalidateNativeRemoteWindowPermissionPreparationNow()
        {
            Interlocked.Increment(ref count);
            invalidating?.Invoke();
            if (failure is not null)
            {
                throw failure;
            }
        }
    }
}
