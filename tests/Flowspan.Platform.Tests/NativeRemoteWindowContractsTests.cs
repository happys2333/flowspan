using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Flowspan.Domain;
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
    public void UnavailablePermissionBoundaryRejectsPreparationWithoutPrompt()
    {
        UnavailableNativeRemoteWindowPermissionBoundary boundary =
            UnavailableNativeRemoteWindowPermissionBoundary.Instance;
        NativeRemoteWindowPermissionSnapshot expected = boundary.GetSnapshot();
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
        Assert.False(result.Reserved);
        Assert.Null(result.Registration);
        Assert.Null(sink.Registration);
        Assert.Equal(0, sink.Count);
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
    public void FreshSafeProtectionCanBeReservedExactly()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var sink = new RecordingProtectionPreparationInvalidationSink();

        NativeRemoteWindowProtectionPreparationReservationResult result =
            ((INativeRemoteWindowProtectionPreparationBoundary)source)
            .TryReservePreparation(expected!, Now, sink);

        Assert.Equal(
            NativeRemoteWindowProtectionPreparationReservationStatus.Reserved,
            result.Status);
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    result.Registration);
        Assert.Same(registration, sink.Registration);
        Assert.True(result.Reserved);
        Assert.True(registration.IsCurrent);
        Assert.True(registration.RegistrationId > 0);

        registration.Dispose();
        registration.Dispose();

        Assert.False(registration.IsCurrent);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void ProtectionMutationInvalidatesReservationBeforeOrdinaryObserver()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var order = new List<int>();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            null!;
        var sink = new RecordingProtectionPreparationInvalidationSink(() =>
        {
            Assert.False(registration.IsCurrent);
            Assert.True(
                source.TryGetLatest(
                    out NativeRemoteWindowProtectionObservation? committed));
            Assert.Equal(
                ProtectionKind.SecureInput,
                committed?.Protection.Kind);
            order.Add(1);
        });
        registration = Assert.IsAssignableFrom<
            INativeRemoteWindowProtectionPreparationRegistration>(
                ((INativeRemoteWindowProtectionPreparationBoundary)source)
                .TryReservePreparation(expected!, Now, sink)
                .Registration);
        source.Changed += _ =>
        {
            Assert.Equal(1, sink.Count);
            order.Add(2);
        };

        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now.AddMilliseconds(1),
                    "secure-input-probe")));

        Assert.False(registration.IsCurrent);
        Assert.Equal(1, sink.Count);
        Assert.Equal([1, 2], order);
    }

    [Fact]
    public void ProtectionReservationPromotesAndAdmitsExactCaptureStart()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        var formalSink = new RecordingProtectionFormalSink();

        Assert.True(registration.TryPromote(Now, formalSink));
        Assert.True(registration.IsCurrent);
        Assert.True(registration.TryAdmitCaptureStart(Now));
        Assert.True(registration.IsCurrent);
        Assert.False(registration.TryAdmitCaptureStart(Now));
        Assert.Equal(0, preparationSink.Count);
        Assert.Equal(0, formalSink.PreStartInvalidationCount);
        Assert.Equal(0, formalSink.LatchCount);
        Assert.Equal(0, formalSink.NotifyCount);
    }

    [Fact]
    public void FormalPreStartMutationInvalidatesBeforeCaptureAndObserver()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        var order = new List<int>();
        RecordingProtectionFormalSink? formalSink = null;
        formalSink = new RecordingProtectionFormalSink(
            invalidatingPreStart: () =>
            {
                Assert.False(registration.IsCurrent);
                order.Add(1);
            });
        Assert.True(registration.TryPromote(Now, formalSink));
        source.Changed += _ =>
        {
            Assert.Equal(1, formalSink.PreStartInvalidationCount);
            order.Add(2);
        };

        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now.AddMilliseconds(1),
                    "secure-input-probe")));

        Assert.False(registration.IsCurrent);
        Assert.False(registration.TryAdmitCaptureStart(Now));
        Assert.Equal(0, preparationSink.Count);
        Assert.Equal(1, formalSink.PreStartInvalidationCount);
        Assert.Equal(0, formalSink.LatchCount);
        Assert.Equal(0, formalSink.NotifyCount);
        Assert.Equal([1, 2], order);
    }

    [Fact]
    public void LiveMutationLatchesThenNotifiesBeforeOrdinaryObserver()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        var order = new List<int>();
        NativeRemoteWindowProtectionObservation? latched = null;
        var formalSink = new RecordingProtectionFormalSink(
            latching: observation =>
            {
                Assert.True(registration.IsCurrent);
                Assert.True(
                    source.TryGetLatest(
                        out NativeRemoteWindowProtectionObservation? current));
                Assert.Same(observation, current);
                latched = observation;
                order.Add(1);
            },
            notifying: () =>
            {
                Assert.NotNull(latched);
                order.Add(2);
            });
        Assert.True(registration.TryPromote(Now, formalSink));
        Assert.True(registration.TryAdmitCaptureStart(Now));
        source.Changed += observation =>
        {
            Assert.Same(latched, observation);
            Assert.Equal(1, formalSink.NotifyCount);
            order.Add(3);
        };

        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now.AddMilliseconds(1),
                    "secure-input-probe")));

        Assert.True(registration.IsCurrent);
        Assert.Equal(0, preparationSink.Count);
        Assert.Equal(0, formalSink.PreStartInvalidationCount);
        Assert.Equal(1, formalSink.LatchCount);
        Assert.Equal(1, formalSink.NotifyCount);
        Assert.Equal(ProtectionKind.SecureInput, latched?.Protection.Kind);
        Assert.Equal([1, 2, 3], order);

        registration.Dispose();
    }

    [Fact]
    public void ProtectionSourceDisposeInvalidatesTemporaryOwnerAndReplaysFailure()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var failure = new IOException("protection-disposal-failure");
        INativeRemoteWindowProtectionPreparationRegistration registration =
            null!;
        var sink = new RecordingProtectionPreparationInvalidationSink(
            invalidating: () => Assert.False(registration.IsCurrent),
            invalidationFailure: failure);
        registration = Assert.IsAssignableFrom<
            INativeRemoteWindowProtectionPreparationRegistration>(
                ((INativeRemoteWindowProtectionPreparationBoundary)source)
                .TryReservePreparation(expected!, Now, sink)
                .Registration);

        IOException first = Assert.Throws<IOException>(source.Dispose);
        IOException repeated = Assert.Throws<IOException>(source.Dispose);

        Assert.Same(failure, first);
        Assert.Same(first, repeated);
        Assert.False(registration.IsCurrent);
        Assert.Equal(1, sink.Count);
        Assert.False(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? afterDispose));
        Assert.Null(afterDispose);
    }

    [Fact]
    public void ProtectionReservationRequiresEveryExactObservationField()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        var protection = new ProtectionSnapshot(
            ProtectionKind.Safe,
            Now,
            "reservation-probe");
        Assert.True(source.TryPublish(protection));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? current));
        NativeRemoteWindowProtectionObservation[] mismatches =
        [
            NativeRemoteWindowProtectionObservation.Create(
                protection,
                ownerGeneration: 7,
                current!.SessionGeneration,
                current.SourceGeneration,
                current.Revision),
            NativeRemoteWindowProtectionObservation.Create(
                protection,
                current!.OwnerGeneration,
                sessionGeneration: 7,
                current.SourceGeneration,
                current.Revision),
            NativeRemoteWindowProtectionObservation.Create(
                protection,
                current!.OwnerGeneration,
                current.SessionGeneration,
                sourceGeneration: 7,
                current.Revision),
            NativeRemoteWindowProtectionObservation.Create(
                protection,
                current!.OwnerGeneration,
                current.SessionGeneration,
                current.SourceGeneration,
                checked(current.Revision + 1)),
            NativeRemoteWindowProtectionObservation.Create(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    protection.ObservedAt,
                    protection.Source),
                current!.OwnerGeneration,
                current.SessionGeneration,
                current.SourceGeneration,
                current.Revision),
            NativeRemoteWindowProtectionObservation.Create(
                new ProtectionSnapshot(
                    protection.Kind,
                    protection.ObservedAt.AddTicks(1),
                    protection.Source),
                current!.OwnerGeneration,
                current.SessionGeneration,
                current.SourceGeneration,
                current.Revision),
            NativeRemoteWindowProtectionObservation.Create(
                new ProtectionSnapshot(
                    protection.Kind,
                    protection.ObservedAt,
                    "different-probe"),
                current!.OwnerGeneration,
                current.SessionGeneration,
                current.SourceGeneration,
                current.Revision),
        ];
        INativeRemoteWindowProtectionPreparationBoundary boundary = source;

        foreach (
            NativeRemoteWindowProtectionObservation mismatch in mismatches)
        {
            NativeRemoteWindowProtectionPreparationReservationResult result =
                boundary.TryReservePreparation(
                    mismatch,
                    Now,
                    new RecordingProtectionPreparationInvalidationSink());

            Assert.Equal(
                NativeRemoteWindowProtectionPreparationReservationStatus
                    .ObservationChanged,
                result.Status);
            Assert.Null(result.Registration);
        }
    }

    [Theory]
    [InlineData(ProtectionKind.SensitiveWindow)]
    [InlineData(ProtectionKind.SecureInput)]
    [InlineData(ProtectionKind.ProtectedContent)]
    [InlineData(ProtectionKind.Unknown)]
    public void UnsafeProtectionCannotBeReserved(ProtectionKind kind)
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(kind, Now, "unsafe-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));

        NativeRemoteWindowProtectionPreparationReservationResult result =
            ((INativeRemoteWindowProtectionPreparationBoundary)source)
            .TryReservePreparation(
                expected!,
                Now,
                new RecordingProtectionPreparationInvalidationSink());

        Assert.Equal(
            NativeRemoteWindowProtectionPreparationReservationStatus
                .ProtectionBlocked,
            result.Status);
        Assert.Null(result.Registration);
    }

    [Theory]
    [InlineData(-500, true)]
    [InlineData(-501, false)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void ProtectionReservationUsesInclusiveFreshnessBoundaries(
        int observedOffsetMilliseconds,
        bool expectedReserved)
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now.AddMilliseconds(observedOffsetMilliseconds),
                    "freshness-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));

        NativeRemoteWindowProtectionPreparationReservationResult result =
            ((INativeRemoteWindowProtectionPreparationBoundary)source)
            .TryReservePreparation(
                expected!,
                Now,
                new RecordingProtectionPreparationInvalidationSink());

        Assert.Equal(expectedReserved, result.Reserved);
        Assert.Equal(
            expectedReserved
                ? NativeRemoteWindowProtectionPreparationReservationStatus
                    .Reserved
                : NativeRemoteWindowProtectionPreparationReservationStatus
                    .ProtectionBlocked,
            result.Status);
        result.Registration?.Dispose();
    }

    [Fact]
    public void TemporaryInvalidationUnwrapsNestedOutOfMemoryAfterObserver()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var fatal = new InjectedProtectionOutOfMemoryException(
            "temporary-protection-fatal");
        var composite = new AggregateException(
            new IOException("temporary-protection-cleanup"),
            new AggregateException(fatal));
        var sink = new RecordingProtectionPreparationInvalidationSink(
            invalidationFailure: composite);
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(expected!, Now, sink)
                    .Registration);
        int observerCount = 0;
        source.Changed += _ => observerCount++;

        OutOfMemoryException thrown = Assert.ThrowsAny<OutOfMemoryException>(() =>
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now.AddMilliseconds(1),
                    "secure-input-probe")));

        Assert.Same(fatal, thrown);
        Assert.False(registration.IsCurrent);
        Assert.Equal(1, sink.Count);
        Assert.Equal(1, observerCount);
    }

    [Fact]
    public void FormalPreStartInvalidationUnwrapsNestedOutOfMemoryAfterObserver()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        var fatal = new InjectedProtectionOutOfMemoryException(
            "formal-protection-fatal");
        var formalSink = new RecordingProtectionFormalSink(
            preStartFailure: new AggregateException(
                new IOException("formal-protection-cleanup"),
                new AggregateException(fatal)));
        Assert.True(registration.TryPromote(Now, formalSink));
        int observerCount = 0;
        source.Changed += _ => observerCount++;

        OutOfMemoryException thrown = Assert.ThrowsAny<OutOfMemoryException>(() =>
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now.AddMilliseconds(1),
                    "secure-input-probe")));

        Assert.Same(fatal, thrown);
        Assert.False(registration.IsCurrent);
        Assert.Equal(1, formalSink.PreStartInvalidationCount);
        Assert.Equal(1, observerCount);
    }

    [Fact]
    public void LiveSourceLossUnwrapsNestedOutOfMemoryAndReplaysIt()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        var fatal = new InjectedProtectionOutOfMemoryException(
            "live-source-loss-fatal");
        var formalSink = new RecordingProtectionFormalSink(
            latchFailure: new AggregateException(
                new IOException("live-source-loss-cleanup"),
                new AggregateException(fatal)));
        Assert.True(registration.TryPromote(Now, formalSink));
        Assert.True(registration.TryAdmitCaptureStart(Now));

        OutOfMemoryException first = Assert.ThrowsAny<OutOfMemoryException>(
            source.Dispose);
        OutOfMemoryException repeated = Assert.ThrowsAny<OutOfMemoryException>(
            source.Dispose);

        Assert.Same(fatal, first);
        Assert.Same(first, repeated);
        Assert.False(registration.IsCurrent);
        Assert.Equal(1, formalSink.LatchCount);
        Assert.Null(formalSink.Observation);
        Assert.Equal(1, formalSink.NotifyCount);
    }

    [Fact]
    public void OwnerClaimRollbackAllowsReplacementAndLateDisposeIsAbaSafe()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var ownerFailure = new IOException("protection-owner-claim-failure");
        var failingSink = new RecordingProtectionPreparationInvalidationSink(
            ownershipFailure: ownerFailure);

        IOException thrown = Assert.Throws<IOException>(() =>
            ((INativeRemoteWindowProtectionPreparationBoundary)source)
            .TryReservePreparation(expected!, Now, failingSink));
        INativeRemoteWindowProtectionPreparationRegistration failed =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    failingSink.Registration);
        var replacementSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration replacement =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        replacementSink)
                    .Registration);

        failed.Dispose();

        Assert.Same(ownerFailure, thrown);
        Assert.False(failed.IsCurrent);
        Assert.True(replacement.IsCurrent);
        Assert.True(replacement.RegistrationId > failed.RegistrationId);
        Assert.Same(replacement, replacementSink.Registration);

        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now.AddMilliseconds(1),
                    "secure-input-probe")));

        Assert.False(replacement.IsCurrent);
        Assert.Equal(0, failingSink.Count);
        Assert.Equal(1, replacementSink.Count);
    }

    [Fact]
    public void SafeUnsafeSafeAbaCannotReviveOldProtectionReservation()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        var safe = new ProtectionSnapshot(
            ProtectionKind.Safe,
            Now,
            "reservation-probe");
        Assert.True(source.TryPublish(safe));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? firstSafe));
        var firstSink = new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration first =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(firstSafe!, Now, firstSink)
                    .Registration);

        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now.AddMilliseconds(1),
                    "secure-input-probe")));
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now.AddMilliseconds(2),
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? secondSafe));

        NativeRemoteWindowProtectionPreparationReservationResult stale =
            ((INativeRemoteWindowProtectionPreparationBoundary)source)
            .TryReservePreparation(
                firstSafe!,
                Now.AddMilliseconds(2),
                new RecordingProtectionPreparationInvalidationSink());

        Assert.False(first.IsCurrent);
        Assert.Equal(1, firstSink.Count);
        Assert.Equal(
            checked(firstSafe!.Revision + 2),
            secondSafe?.Revision);
        Assert.Equal(
            NativeRemoteWindowProtectionPreparationReservationStatus
                .ObservationChanged,
            stale.Status);
        Assert.Null(stale.Registration);
    }

    [Fact]
    public async Task ReservationCommitBlocksMutationUntilOwnerClaimReturns()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        using var ownerClaimEntered = new ManualResetEventSlim();
        using var releaseOwnerClaim = new ManualResetEventSlim();
        var sink = new RecordingProtectionPreparationInvalidationSink(
            owning: _ =>
            {
                ownerClaimEntered.Set();
                if (!releaseOwnerClaim.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException(
                        "Timed out releasing protection owner claim.");
                }
            });
        Task<NativeRemoteWindowProtectionPreparationReservationResult>
            reservation = RunOnDedicatedThread(() =>
                ((INativeRemoteWindowProtectionPreparationBoundary)source)
                .TryReservePreparation(expected!, Now, sink));
        Assert.True(ownerClaimEntered.Wait(TimeSpan.FromSeconds(5)));

        Task<bool> mutation = RunOnDedicatedThread(() =>
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now.AddMilliseconds(1),
                    "secure-input-probe")));
        try
        {
            Task first = await Task.WhenAny(
                mutation,
                Task.Delay(TimeSpan.FromMilliseconds(100)));
            Assert.NotSame(mutation, first);
        }
        finally
        {
            releaseOwnerClaim.Set();
        }

        NativeRemoteWindowProtectionPreparationReservationResult reserved =
            await reservation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await mutation.WaitAsync(TimeSpan.FromSeconds(5)));
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    reserved.Registration);
        Assert.Equal(
            NativeRemoteWindowProtectionPreparationReservationStatus.Reserved,
            reserved.Status);
        Assert.False(registration.IsCurrent);
        Assert.Equal(1, sink.Count);
    }

    [Fact]
    public async Task MutationCommitBlocksReservationUntilInvalidationReturns()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expectedSafe));
        using var invalidationEntered = new ManualResetEventSlim();
        using var releaseInvalidation = new ManualResetEventSlim();
        var existingSink = new RecordingProtectionPreparationInvalidationSink(
            invalidating: () =>
            {
                invalidationEntered.Set();
                if (!releaseInvalidation.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException(
                        "Timed out releasing protection invalidation.");
                }
            });
        _ = Assert.IsAssignableFrom<
            INativeRemoteWindowProtectionPreparationRegistration>(
                ((INativeRemoteWindowProtectionPreparationBoundary)source)
                .TryReservePreparation(expectedSafe!, Now, existingSink)
                .Registration);
        var unsafeProtection = new ProtectionSnapshot(
            ProtectionKind.SecureInput,
            Now.AddMilliseconds(1),
            "secure-input-probe");
        NativeRemoteWindowProtectionObservation expectedUnsafe =
            NativeRemoteWindowProtectionObservation.Create(
                unsafeProtection,
                expectedSafe!.OwnerGeneration,
                expectedSafe.SessionGeneration,
                expectedSafe.SourceGeneration,
                checked(expectedSafe.Revision + 1));
        Task<bool> mutation = RunOnDedicatedThread(
            () => source.TryPublish(unsafeProtection));
        Assert.True(invalidationEntered.Wait(TimeSpan.FromSeconds(5)));
        var newSink = new RecordingProtectionPreparationInvalidationSink();
        Task<NativeRemoteWindowProtectionPreparationReservationResult>
            reservation = RunOnDedicatedThread(() =>
                ((INativeRemoteWindowProtectionPreparationBoundary)source)
                .TryReservePreparation(
                    expectedUnsafe,
                    Now.AddMilliseconds(1),
                    newSink));

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

        Assert.True(await mutation.WaitAsync(TimeSpan.FromSeconds(5)));
        NativeRemoteWindowProtectionPreparationReservationResult rejected =
            await reservation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            NativeRemoteWindowProtectionPreparationReservationStatus
                .ProtectionBlocked,
            rejected.Status);
        Assert.Null(rejected.Registration);
        Assert.Equal(1, existingSink.Count);
        Assert.Equal(0, newSink.Count);
    }

    [Fact]
    public void TemporarySinkFailureCannotBlockObserverOrCommittedProtection()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var order = new List<int>();
        var failure = new IOException("temporary-invalidation-failure");
        var sink = new RecordingProtectionPreparationInvalidationSink(
            invalidating: () => order.Add(1),
            invalidationFailure: failure);
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(expected!, Now, sink)
                    .Registration);
        source.Changed += observation =>
        {
            Assert.Equal(ProtectionKind.SecureInput, observation.Protection.Kind);
            Assert.False(registration.IsCurrent);
            order.Add(2);
        };

        IOException thrown = Assert.Throws<IOException>(() =>
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now.AddMilliseconds(1),
                    "secure-input-probe")));

        Assert.Same(failure, thrown);
        Assert.Equal([1, 2], order);
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? committed));
        Assert.Equal(ProtectionKind.SecureInput, committed?.Protection.Kind);
        Assert.Equal(1, sink.Count);
    }

    [Fact]
    public void LiveLatchAndNotifyFailuresAggregateBeforeOrdinaryObserver()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        var order = new List<int>();
        var latchFailure = new IOException("formal-latch-failure");
        var notifyFailure = new InvalidOperationException(
            "formal-notify-failure");
        var formalSink = new RecordingProtectionFormalSink(
            latching: _ => order.Add(1),
            notifying: () => order.Add(2),
            latchFailure: latchFailure,
            notifyFailure: notifyFailure);
        Assert.True(registration.TryPromote(Now, formalSink));
        Assert.True(registration.TryAdmitCaptureStart(Now));
        source.Changed += _ =>
        {
            Assert.Equal(1, formalSink.LatchCount);
            Assert.Equal(1, formalSink.NotifyCount);
            order.Add(3);
        };

        AggregateException thrown = Assert.Throws<AggregateException>(() =>
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now.AddMilliseconds(1),
                    "secure-input-probe")));

        Assert.Equal([latchFailure, notifyFailure], thrown.InnerExceptions);
        Assert.Equal([1, 2, 3], order);
        Assert.False(registration.IsCurrent);
        Assert.Equal(1, formalSink.LatchCount);
        Assert.Equal(1, formalSink.NotifyCount);
    }

    [Fact]
    public void ProtectionSourceDisposeInvalidatesFormalPreStartOwner()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        var formalSink = new RecordingProtectionFormalSink(
            invalidatingPreStart: () => Assert.False(registration.IsCurrent));
        Assert.True(registration.TryPromote(Now, formalSink));

        source.Dispose();
        source.Dispose();

        Assert.False(registration.IsCurrent);
        Assert.Equal(0, preparationSink.Count);
        Assert.Equal(1, formalSink.PreStartInvalidationCount);
        Assert.Equal(0, formalSink.LatchCount);
        Assert.Equal(0, formalSink.NotifyCount);
    }

    [Fact]
    public void ProtectionSourceDisposeLatchesAndNotifiesLiveLoss()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        var order = new List<int>();
        RecordingProtectionFormalSink? formalSink = null;
        formalSink = new RecordingProtectionFormalSink(
            latching: observation =>
            {
                Assert.Null(observation);
                Assert.False(registration.IsCurrent);
                Assert.False(
                    source.TryGetLatest(
                        out NativeRemoteWindowProtectionObservation? afterLoss));
                Assert.Null(afterLoss);
                order.Add(1);
            },
            notifying: () =>
            {
                Assert.Equal(1, formalSink!.LatchCount);
                order.Add(2);
            });
        Assert.True(registration.TryPromote(Now, formalSink));
        Assert.True(registration.TryAdmitCaptureStart(Now));

        source.Dispose();
        source.Dispose();

        Assert.False(registration.IsCurrent);
        Assert.Equal(1, formalSink.LatchCount);
        Assert.Null(formalSink.Observation);
        Assert.Equal(1, formalSink.NotifyCount);
        Assert.Equal([1, 2], order);
    }

    [Fact]
    public void LiveSourceLossFailuresAggregateAndReplayStableInstance()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        var latchFailure = new IOException("source-loss-latch-failure");
        var notifyFailure = new InvalidOperationException(
            "source-loss-notify-failure");
        var formalSink = new RecordingProtectionFormalSink(
            latchFailure: latchFailure,
            notifyFailure: notifyFailure);
        Assert.True(registration.TryPromote(Now, formalSink));
        Assert.True(registration.TryAdmitCaptureStart(Now));

        AggregateException first = Assert.Throws<AggregateException>(
            source.Dispose);
        AggregateException repeated = Assert.Throws<AggregateException>(
            source.Dispose);

        Assert.Same(first, repeated);
        Assert.Equal([latchFailure, notifyFailure], first.InnerExceptions);
        Assert.False(registration.IsCurrent);
        Assert.Equal(1, formalSink.LatchCount);
        Assert.Equal(1, formalSink.NotifyCount);
    }

    [Fact]
    public void PromotionRevalidatesFreshnessAndInvalidatesTemporaryOwner()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        var formalSink = new RecordingProtectionFormalSink();

        Assert.False(
            registration.TryPromote(
                Now.Add(RemoteInputPolicy.MaximumProtectionAge).AddTicks(1),
                formalSink));

        Assert.False(registration.IsCurrent);
        Assert.Equal(1, preparationSink.Count);
        Assert.Equal(0, formalSink.PreStartInvalidationCount);
        Assert.Equal(0, formalSink.LatchCount);
        Assert.Equal(0, formalSink.NotifyCount);
    }

    [Fact]
    public void CaptureStartAdmissionRevalidatesFreshnessUnderSourceGate()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        var formalSink = new RecordingProtectionFormalSink();
        Assert.True(registration.TryPromote(Now, formalSink));

        Assert.False(
            registration.TryAdmitCaptureStart(
                Now.Add(RemoteInputPolicy.MaximumProtectionAge).AddTicks(1)));

        Assert.False(registration.IsCurrent);
        Assert.Equal(0, preparationSink.Count);
        Assert.Equal(1, formalSink.PreStartInvalidationCount);
        Assert.Equal(0, formalSink.LatchCount);
        Assert.Equal(0, formalSink.NotifyCount);
    }

    [Fact]
    public void ProtectionSourceRejectsASecondConcurrentReservation()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var firstSink = new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration first =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(expected!, Now, firstSink)
                    .Registration);
        var secondSink = new RecordingProtectionPreparationInvalidationSink();

        NativeRemoteWindowProtectionPreparationReservationResult second =
            ((INativeRemoteWindowProtectionPreparationBoundary)source)
            .TryReservePreparation(expected!, Now, secondSink);

        Assert.Equal(
            NativeRemoteWindowProtectionPreparationReservationStatus
                .ReservationConflict,
            second.Status);
        Assert.Null(second.Registration);
        Assert.True(first.IsCurrent);
        Assert.Equal(0, firstSink.Count);
        Assert.Equal(0, secondSink.Count);
        first.Dispose();
    }

    [Fact]
    public async Task PromotionAndMutationSelectExactlyOneInvalidationOwner()
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            using var source = new InMemoryNativeProtectionSource(
                ownerGeneration: 3,
                sessionGeneration: 5,
                sourceGeneration: 4);
            Assert.True(
                source.TryPublish(
                    new ProtectionSnapshot(
                        ProtectionKind.Safe,
                        Now,
                        "reservation-probe")));
            Assert.True(
                source.TryGetLatest(
                    out NativeRemoteWindowProtectionObservation? expected));
            var preparationSink =
                new RecordingProtectionPreparationInvalidationSink();
            INativeRemoteWindowProtectionPreparationRegistration registration =
                Assert.IsAssignableFrom<
                    INativeRemoteWindowProtectionPreparationRegistration>(
                        ((INativeRemoteWindowProtectionPreparationBoundary)
                            source)
                        .TryReservePreparation(
                            expected!,
                            Now,
                            preparationSink)
                        .Registration);
            var formalSink = new RecordingProtectionFormalSink();
            using var start = new ManualResetEventSlim();
            Task<bool> promotion = RunOnDedicatedThread(() =>
            {
                start.Wait();
                return registration.TryPromote(Now, formalSink);
            });
            Task<bool> mutation = RunOnDedicatedThread(() =>
            {
                start.Wait();
                return source.TryPublish(
                    new ProtectionSnapshot(
                        ProtectionKind.SecureInput,
                        Now.AddMilliseconds(1),
                        "secure-input-probe"));
            });

            start.Set();
            bool promoted = await promotion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(await mutation.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.False(registration.IsCurrent);
            Assert.False(registration.TryAdmitCaptureStart(Now));
            Assert.Equal(promoted ? 0 : 1, preparationSink.Count);
            Assert.Equal(
                promoted ? 1 : 0,
                formalSink.PreStartInvalidationCount);
            Assert.Equal(
                1,
                preparationSink.Count
                    + formalSink.PreStartInvalidationCount);
            Assert.Equal(0, formalSink.LatchCount);
            Assert.Equal(0, formalSink.NotifyCount);
        }
    }

    [Fact]
    public async Task ReversedConcurrentNotifiesPreserveEveryLiveObservation()
    {
        using var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        using var firstNotifyEntered = new ManualResetEventSlim();
        using var releaseFirstNotify = new ManualResetEventSlim();
        using var secondNotifyDrained = new ManualResetEventSlim();
        var formalSink = new QueuedProtectionFormalSink(
            firstNotifyEntered,
            releaseFirstNotify,
            secondNotifyDrained);
        Assert.True(registration.TryPromote(Now, formalSink));
        Assert.True(registration.TryAdmitCaptureStart(Now));
        var ordinaryRevisions = new List<long>();
        source.Changed += observation =>
        {
            lock (ordinaryRevisions)
            {
                Assert.Contains(
                    formalSink.Delivered,
                    delivered =>
                        delivered?.Revision == observation.Revision);
                ordinaryRevisions.Add(observation.Revision);
            }
        };

        Task<bool> unsafeMutation = RunOnDedicatedThread(() =>
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now.AddMilliseconds(1),
                    "secure-input-probe")));
        Assert.True(firstNotifyEntered.Wait(TimeSpan.FromSeconds(5)));
        Task<bool> safeMutation = RunOnDedicatedThread(() =>
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now.AddMilliseconds(2),
                    "safe-restoration-probe")));

        try
        {
            Assert.True(secondNotifyDrained.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(await safeMutation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(unsafeMutation.IsCompleted);
            Assert.Equal(
                [ProtectionKind.SecureInput, ProtectionKind.Safe],
                formalSink.Delivered
                    .Select(static observation => observation!.Protection.Kind)
                    .ToArray());
        }
        finally
        {
            releaseFirstNotify.Set();
        }

        Assert.True(await unsafeMutation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal([2L, 3L], ordinaryRevisions);
        Assert.Equal([2L, 3L], formalSink.Delivered
            .Select(static observation => observation!.Revision)
            .ToArray());
        Assert.True(registration.IsCurrent);
        registration.Dispose();
    }

    [Fact]
    public async Task LiveSourceDisposeDoesNotReturnBeforeLossNotifyReturns()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        using var notifyEntered = new ManualResetEventSlim();
        using var releaseNotify = new ManualResetEventSlim();
        var formalSink = new RecordingProtectionFormalSink(
            notifying: () =>
            {
                notifyEntered.Set();
                if (!releaseNotify.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException(
                        "Timed out releasing source-loss notification.");
                }
            });
        Assert.True(registration.TryPromote(Now, formalSink));
        Assert.True(registration.TryAdmitCaptureStart(Now));

        Task disposal = RunOnDedicatedThread(source.Dispose);
        Assert.True(notifyEntered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            Task first = await Task.WhenAny(
                disposal,
                Task.Delay(TimeSpan.FromMilliseconds(100)));
            Assert.NotSame(disposal, first);
            Assert.False(registration.IsCurrent);
            Assert.Equal(1, formalSink.LatchCount);
            Assert.Null(formalSink.Observation);
        }
        finally
        {
            releaseNotify.Set();
        }

        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, formalSink.NotifyCount);
    }

    [Fact]
    public async Task LiveFormalCallbackCanDisposeItsOwnSourceWithoutDeadlock()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        int notifications = 0;
        var formalSink = new RecordingProtectionFormalSink(
            notifying: () =>
            {
                if (Interlocked.Increment(ref notifications) == 1)
                {
                    source.Dispose();
                }
            });
        Assert.True(registration.TryPromote(Now, formalSink));
        Assert.True(registration.TryAdmitCaptureStart(Now));

        Task<bool> mutation = RunOnDedicatedThread(() =>
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now.AddMilliseconds(1),
                    "secure-input-probe")));

        Assert.True(await mutation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(registration.IsCurrent);
        Assert.Equal(2, formalSink.LatchCount);
        Assert.Equal(2, formalSink.NotifyCount);
        Assert.Equal(2, Volatile.Read(ref notifications));
        Assert.False(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? afterDispose));
        Assert.Null(afterDispose);
        source.Dispose();
    }

    [Fact]
    public async Task DisposeDrainsAnOlderInFlightFormalNotifyBeforeReturning()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        using var firstNotifyEntered = new ManualResetEventSlim();
        using var releaseFirstNotify = new ManualResetEventSlim();
        using var lossNotifyDrained = new ManualResetEventSlim();
        var formalSink = new QueuedProtectionFormalSink(
            firstNotifyEntered,
            releaseFirstNotify,
            lossNotifyDrained);
        Assert.True(registration.TryPromote(Now, formalSink));
        Assert.True(registration.TryAdmitCaptureStart(Now));
        Task<bool> mutation = RunOnDedicatedThread(() =>
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now.AddMilliseconds(1),
                    "secure-input-probe")));
        Assert.True(firstNotifyEntered.Wait(TimeSpan.FromSeconds(5)));

        Task disposal = RunOnDedicatedThread(source.Dispose);
        try
        {
            Assert.True(lossNotifyDrained.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(
                SpinWait.SpinUntil(
                    () => formalSink.Delivered.Length == 2,
                    TimeSpan.FromSeconds(5)));
            Assert.False(mutation.IsCompleted);
            Task first = await Task.WhenAny(
                disposal,
                Task.Delay(TimeSpan.FromMilliseconds(100)));
            Assert.NotSame(disposal, first);
        }
        finally
        {
            releaseFirstNotify.Set();
        }

        Assert.True(await mutation.WaitAsync(TimeSpan.FromSeconds(5)));
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(registration.IsCurrent);
        Assert.Equal(2, formalSink.Delivered.Length);
        Assert.Equal(ProtectionKind.SecureInput, formalSink.Delivered[0]
            ?.Protection.Kind);
        Assert.Null(formalSink.Delivered[1]);
    }

    [Fact]
    public async Task DisposeReplaysFailureFromOlderInFlightFormalNotify()
    {
        var source = new InMemoryNativeProtectionSource(
            ownerGeneration: 3,
            sessionGeneration: 5,
            sourceGeneration: 4);
        Assert.True(
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.Safe,
                    Now,
                    "reservation-probe")));
        Assert.True(
            source.TryGetLatest(
                out NativeRemoteWindowProtectionObservation? expected));
        var preparationSink =
            new RecordingProtectionPreparationInvalidationSink();
        INativeRemoteWindowProtectionPreparationRegistration registration =
            Assert.IsAssignableFrom<
                INativeRemoteWindowProtectionPreparationRegistration>(
                    ((INativeRemoteWindowProtectionPreparationBoundary)source)
                    .TryReservePreparation(
                        expected!,
                        Now,
                        preparationSink)
                    .Registration);
        using var firstNotifyEntered = new ManualResetEventSlim();
        using var releaseFirstNotify = new ManualResetEventSlim();
        using var lossNotifyDrained = new ManualResetEventSlim();
        var failure = new IOException("older-formal-notify-failure");
        var formalSink = new QueuedProtectionFormalSink(
            firstNotifyEntered,
            releaseFirstNotify,
            lossNotifyDrained,
            failure);
        Assert.True(registration.TryPromote(Now, formalSink));
        Assert.True(registration.TryAdmitCaptureStart(Now));
        Task<bool> mutation = RunOnDedicatedThread(() =>
            source.TryPublish(
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    Now.AddMilliseconds(1),
                    "secure-input-probe")));
        Assert.True(firstNotifyEntered.Wait(TimeSpan.FromSeconds(5)));
        Task disposal = RunOnDedicatedThread(source.Dispose);
        Assert.True(lossNotifyDrained.Wait(TimeSpan.FromSeconds(5)));

        releaseFirstNotify.Set();

        IOException mutationFailure = await Assert.ThrowsAsync<IOException>(
            async () => await mutation.WaitAsync(TimeSpan.FromSeconds(5)));
        IOException disposalFailure = await Assert.ThrowsAsync<IOException>(
            async () => await disposal.WaitAsync(TimeSpan.FromSeconds(5)));
        IOException repeated = Assert.Throws<IOException>(source.Dispose);
        Assert.Same(failure, mutationFailure);
        Assert.Same(mutationFailure, disposalFailure);
        Assert.Same(disposalFailure, repeated);
        Assert.False(registration.IsCurrent);
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
    public void EmergencyReadinessReservationInstallsNoCallbackUntilPromotion()
    {
        using var registrar = new InMemoryLocalEmergencyStopRegistrar();
        var invalidated = new RecordingEmergencyStopReadinessInvalidationSink();
        LocalEmergencyStopActivation? activation = null;

        LocalEmergencyStopReadinessReservationResult reserved =
            registrar.TryReserveReadiness(
                ownerGeneration: 7,
                sessionGeneration: 9,
                invalidated);
        ILocalEmergencyStopReadinessReservation reservation =
            Assert.IsAssignableFrom<ILocalEmergencyStopReadinessReservation>(
                reserved.Reservation);

        Assert.True(reserved.Reserved);
        Assert.True(reservation.IsCurrent);
        Assert.Equal(7, reservation.OwnerGeneration);
        Assert.Equal(9, reservation.SessionGeneration);
        Assert.False(registrar.Trigger());
        Assert.Equal(0, invalidated.Count);
        Assert.Equal(
            "emergency_stop_registration_conflict",
            registrar.CheckReadiness().ReasonCode);
        Assert.False(registrar.TryRegister(1, 1, _ => { }).Registered);

        LocalEmergencyStopRegistrationResult promoted = reservation.TryPromote(
            observed => activation = observed);

        Assert.True(promoted.Registered);
        Assert.NotSame(reservation, promoted.Registration);
        Assert.True(registrar.Trigger());
        Assert.False(reservation.IsCurrent);
        Assert.Equal(LocalEmergencyStopCause.UserAction, activation?.Cause);
        Assert.Equal(7, activation?.OwnerGeneration);
        Assert.Equal(9, activation?.SessionGeneration);
        Assert.Equal(0, invalidated.Count);
    }

    [Fact]
    public void ReleasedEmergencyReadinessReservationCannotPromoteOrAffectAbaReplacement()
    {
        using var registrar = new InMemoryLocalEmergencyStopRegistrar();
        var firstInvalidated =
            new RecordingEmergencyStopReadinessInvalidationSink();
        LocalEmergencyStopReadinessReservationResult first =
            registrar.TryReserveReadiness(1, 1, firstInvalidated);
        ILocalEmergencyStopReadinessReservation firstReservation =
            Assert.IsAssignableFrom<ILocalEmergencyStopReadinessReservation>(
                first.Reservation);

        firstReservation.Dispose();
        var secondInvalidated =
            new RecordingEmergencyStopReadinessInvalidationSink();
        LocalEmergencyStopReadinessReservationResult second =
            registrar.TryReserveReadiness(1, 1, secondInvalidated);
        ILocalEmergencyStopReadinessReservation secondReservation =
            Assert.IsAssignableFrom<ILocalEmergencyStopReadinessReservation>(
                second.Reservation);
        LocalEmergencyStopRegistrationResult stalePromotion =
            firstReservation.TryPromote(_ => { });
        firstReservation.Dispose();

        Assert.True(second.Reserved);
        Assert.True(secondReservation.IsCurrent);
        Assert.False(stalePromotion.Registered);
        Assert.Equal(
            "emergency_stop_readiness_stale",
            stalePromotion.Boundary.ReasonCode);
        Assert.Equal(0, firstInvalidated.Count);
        Assert.Equal(0, secondInvalidated.Count);
    }

    [Fact]
    public void EmergencyReadinessLossInvalidatesBeforeReplacementAndRejectsPromotion()
    {
        using var registrar = new InMemoryLocalEmergencyStopRegistrar();
        var invalidated = new RecordingEmergencyStopReadinessInvalidationSink();
        LocalEmergencyStopReadinessReservationResult reserved =
            registrar.TryReserveReadiness(3, 5, invalidated);
        ILocalEmergencyStopReadinessReservation reservation =
            Assert.IsAssignableFrom<ILocalEmergencyStopReadinessReservation>(
                reserved.Reservation);

        Assert.True(registrar.LoseRegistration());
        LocalEmergencyStopRegistrationResult promoted = reservation.TryPromote(
            _ => { });
        LocalEmergencyStopReadinessReservationResult replacement =
            registrar.TryReserveReadiness(
                3,
                5,
                new RecordingEmergencyStopReadinessInvalidationSink());

        Assert.Equal(1, invalidated.Count);
        Assert.False(reservation.IsCurrent);
        Assert.False(promoted.Registered);
        Assert.Equal(
            "emergency_stop_readiness_stale",
            promoted.Boundary.ReasonCode);
        Assert.True(replacement.Reserved);
        replacement.Reservation?.Dispose();
    }

    [Fact]
    public void PromotedEmergencyReadinessLossUsesFormalCallbackNotPreparationSink()
    {
        using var registrar = new InMemoryLocalEmergencyStopRegistrar();
        var invalidated = new RecordingEmergencyStopReadinessInvalidationSink();
        LocalEmergencyStopActivation? activation = null;
        LocalEmergencyStopReadinessReservationResult reserved =
            registrar.TryReserveReadiness(3, 5, invalidated);
        ILocalEmergencyStopReadinessReservation reservation =
            Assert.IsAssignableFrom<ILocalEmergencyStopReadinessReservation>(
                reserved.Reservation);
        LocalEmergencyStopRegistrationResult promoted = reservation.TryPromote(
            observed => activation = observed);

        Assert.True(promoted.Registered);
        Assert.True(registrar.LoseRegistration());

        Assert.Equal(0, invalidated.Count);
        Assert.Equal(LocalEmergencyStopCause.RegistrationLost, activation?.Cause);
        Assert.Equal(3, activation?.OwnerGeneration);
        Assert.Equal(5, activation?.SessionGeneration);
        Assert.False(reservation.IsCurrent);
    }

    [Fact]
    public void EmergencyRegistrarDisposalSignalsPromotedRegistrationLoss()
    {
        var registrar = new InMemoryLocalEmergencyStopRegistrar();
        var invalidated = new RecordingEmergencyStopReadinessInvalidationSink();
        LocalEmergencyStopActivation? activation = null;
        LocalEmergencyStopReadinessReservationResult reserved =
            registrar.TryReserveReadiness(3, 5, invalidated);
        ILocalEmergencyStopReadinessReservation reservation =
            Assert.IsAssignableFrom<ILocalEmergencyStopReadinessReservation>(
                reserved.Reservation);
        LocalEmergencyStopRegistrationResult promoted = reservation.TryPromote(
            observed => activation = observed);
        ILocalEmergencyStopRegistration registration = Assert.IsAssignableFrom<
            ILocalEmergencyStopRegistration>(promoted.Registration);

        registrar.Dispose();

        Assert.Equal(0, invalidated.Count);
        Assert.Equal(LocalEmergencyStopCause.RegistrationLost, activation?.Cause);
        Assert.Equal(3, activation?.OwnerGeneration);
        Assert.Equal(5, activation?.SessionGeneration);
        Assert.False(registration.IsCurrent);
        Assert.False(reservation.IsCurrent);
        registrar.Dispose();
    }

    [Fact]
    public async Task EmergencyReadinessPromotionAndLossLinearizeToOneOwner()
    {
        for (int attempt = 0; attempt < 64; attempt++)
        {
            using var registrar = new InMemoryLocalEmergencyStopRegistrar();
            var invalidated =
                new RecordingEmergencyStopReadinessInvalidationSink();
            int activationCount = 0;
            LocalEmergencyStopReadinessReservationResult reserved =
                registrar.TryReserveReadiness(3, 5, invalidated);
            ILocalEmergencyStopReadinessReservation reservation =
                Assert.IsAssignableFrom<ILocalEmergencyStopReadinessReservation>(
                    reserved.Reservation);
            using var start = new Barrier(participantCount: 3);
            Task<LocalEmergencyStopRegistrationResult> promotion =
                RunOnDedicatedThread(() =>
                {
                    start.SignalAndWait();
                    return reservation.TryPromote(
                        _ => Interlocked.Increment(ref activationCount));
                });
            Task<bool> loss = RunOnDedicatedThread(() =>
            {
                start.SignalAndWait();
                return registrar.LoseRegistration();
            });

            start.SignalAndWait();
            LocalEmergencyStopRegistrationResult promoted = await promotion
                .WaitAsync(TimeSpan.FromSeconds(5));
            bool lost = await loss.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(lost);
            Assert.Equal(
                1,
                invalidated.Count + Volatile.Read(ref activationCount));
            if (promoted.Boundary.Succeeded)
            {
                Assert.Equal(0, invalidated.Count);
                Assert.Equal(1, Volatile.Read(ref activationCount));
            }
            else
            {
                Assert.Equal("emergency_stop_readiness_stale", promoted.Boundary.ReasonCode);
                Assert.Equal(1, invalidated.Count);
                Assert.Equal(0, Volatile.Read(ref activationCount));
            }

            Assert.False(reservation.IsCurrent);
        }
    }

    [Fact]
    public void EmergencyRegistrarDisposalInvalidatesReservedReadinessExactlyOnce()
    {
        var registrar = new InMemoryLocalEmergencyStopRegistrar();
        var invalidated = new RecordingEmergencyStopReadinessInvalidationSink();
        LocalEmergencyStopReadinessReservationResult reserved =
            registrar.TryReserveReadiness(1, 1, invalidated);
        ILocalEmergencyStopReadinessReservation reservation =
            Assert.IsAssignableFrom<ILocalEmergencyStopReadinessReservation>(
                reserved.Reservation);

        registrar.Dispose();
        registrar.Dispose();

        Assert.Equal(1, invalidated.Count);
        Assert.False(reservation.IsCurrent);
        Assert.False(reservation.TryPromote(_ => { }).Registered);
    }

    [Fact]
    public void EmergencyRegistrarRetainsReadinessInvalidationFailureAcrossDispose()
    {
        var registrar = new InMemoryLocalEmergencyStopRegistrar();
        var injected = new IOException(
            "emergency readiness disposal invalidation failure");
        var invalidated = new RecordingEmergencyStopReadinessInvalidationSink(
            injected);
        LocalEmergencyStopReadinessReservationResult reserved =
            registrar.TryReserveReadiness(1, 1, invalidated);
        ILocalEmergencyStopReadinessReservation reservation =
            Assert.IsAssignableFrom<ILocalEmergencyStopReadinessReservation>(
                reserved.Reservation);

        IOException first = Assert.Throws<IOException>(registrar.Dispose);
        IOException repeated = Assert.Throws<IOException>(registrar.Dispose);
        LocalEmergencyStopReadinessReservationResult afterDispose =
            registrar.TryReserveReadiness(
                2,
                2,
                new RecordingEmergencyStopReadinessInvalidationSink());

        Assert.Same(injected, first);
        Assert.Same(injected, repeated);
        Assert.Equal(1, invalidated.Count);
        Assert.False(reservation.IsCurrent);
        Assert.False(afterDispose.Reserved);
        Assert.Equal(
            "emergency_stop_registrar_unavailable",
            afterDispose.Boundary.ReasonCode);
    }

    [Fact]
    public void EmergencyReadinessSinkFailureDoesNotRetainTheRegistrarSlot()
    {
        using var registrar = new InMemoryLocalEmergencyStopRegistrar();
        var injected = new IOException(
            "emergency readiness invalidation failure");
        var invalidated = new RecordingEmergencyStopReadinessInvalidationSink(
            injected);
        LocalEmergencyStopReadinessReservationResult reserved =
            registrar.TryReserveReadiness(1, 1, invalidated);
        ILocalEmergencyStopReadinessReservation reservation =
            Assert.IsAssignableFrom<ILocalEmergencyStopReadinessReservation>(
                reserved.Reservation);

        IOException failure = Assert.Throws<IOException>(() =>
        {
            _ = registrar.LoseRegistration();
        });
        LocalEmergencyStopReadinessReservationResult replacement =
            registrar.TryReserveReadiness(
                2,
                2,
                new RecordingEmergencyStopReadinessInvalidationSink());

        Assert.Same(injected, failure);
        Assert.Equal(1, invalidated.Count);
        Assert.False(reservation.IsCurrent);
        Assert.True(replacement.Reserved);
        replacement.Reservation?.Dispose();
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

    private sealed class RecordingEmergencyStopReadinessInvalidationSink(
        Exception? failure = null) :
        ILocalEmergencyStopReadinessInvalidationSink
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public void InvalidateEmergencyStopReadinessNow()
        {
            Interlocked.Increment(ref count);
            if (failure is not null)
            {
                throw failure;
            }
        }
    }

    private sealed class RecordingPermissionPreparationInvalidationSink :
        INativeRemoteWindowPermissionPreparationInvalidationSink
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public INativeRemoteWindowPermissionPreparationRegistration?
            Registration
        { get; private set; }

        public void OwnNativeRemoteWindowPermissionPreparationRegistration(
            INativeRemoteWindowPermissionPreparationRegistration registration) =>
            Registration = registration;

        public void InvalidateNativeRemoteWindowPermissionPreparationNow() =>
            Interlocked.Increment(ref count);
    }

    private sealed class RecordingProtectionPreparationInvalidationSink(
        Action? invalidating = null,
        Exception? invalidationFailure = null,
        Exception? ownershipFailure = null,
        Action<INativeRemoteWindowProtectionPreparationRegistration>? owning =
            null) :
        INativeRemoteWindowProtectionPreparationInvalidationSink
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public INativeRemoteWindowProtectionPreparationRegistration?
            Registration
        { get; private set; }

        public void OwnNativeRemoteWindowProtectionPreparationRegistration(
            INativeRemoteWindowProtectionPreparationRegistration registration)
        {
            Registration = registration;
            owning?.Invoke(registration);
            if (ownershipFailure is not null)
            {
                throw ownershipFailure;
            }
        }

        public void InvalidateNativeRemoteWindowProtectionPreparationNow()
        {
            Interlocked.Increment(ref count);
            invalidating?.Invoke();
            if (invalidationFailure is not null)
            {
                throw invalidationFailure;
            }
        }
    }

    private sealed class RecordingProtectionFormalSink(
        Action? invalidatingPreStart = null,
        Action<NativeRemoteWindowProtectionObservation?>? latching = null,
        Action? notifying = null,
        Exception? preStartFailure = null,
        Exception? latchFailure = null,
        Exception? notifyFailure = null) :
        INativeRemoteWindowProtectionFormalSink
    {
        private int latchCount;
        private int notifyCount;
        private int preStartInvalidationCount;

        public int LatchCount => Volatile.Read(ref latchCount);

        public int NotifyCount => Volatile.Read(ref notifyCount);

        public int PreStartInvalidationCount =>
            Volatile.Read(ref preStartInvalidationCount);

        public NativeRemoteWindowProtectionObservation? Observation
        { get; private set; }

        public void InvalidateNativeRemoteWindowProtectionBeforeCaptureNow()
        {
            Interlocked.Increment(ref preStartInvalidationCount);
            invalidatingPreStart?.Invoke();
            if (preStartFailure is not null)
            {
                throw preStartFailure;
            }
        }

        public void LatchNativeRemoteWindowProtectionObservationNow(
            NativeRemoteWindowProtectionObservation? observation)
        {
            Observation = observation;
            Interlocked.Increment(ref latchCount);
            latching?.Invoke(observation);
            if (latchFailure is not null)
            {
                throw latchFailure;
            }
        }

        public void NotifyNativeRemoteWindowProtectionChanged()
        {
            Interlocked.Increment(ref notifyCount);
            notifying?.Invoke();
            if (notifyFailure is not null)
            {
                throw notifyFailure;
            }
        }
    }

    private sealed class QueuedProtectionFormalSink(
        ManualResetEventSlim firstNotifyEntered,
        ManualResetEventSlim releaseFirstNotify,
        ManualResetEventSlim secondNotifyDrained,
        Exception? firstNotifyFailure = null) :
        INativeRemoteWindowProtectionFormalSink
    {
        private readonly List<NativeRemoteWindowProtectionObservation?> delivered =
            [];
        private readonly object gate = new();
        private int notifyCalls;
        private readonly Queue<NativeRemoteWindowProtectionObservation?> pending =
            [];

        public NativeRemoteWindowProtectionObservation?[] Delivered
        {
            get
            {
                lock (gate)
                {
                    return delivered.ToArray();
                }
            }
        }

        public void InvalidateNativeRemoteWindowProtectionBeforeCaptureNow() =>
            throw new InvalidOperationException(
                "The live queue cannot receive a pre-start invalidation.");

        public void LatchNativeRemoteWindowProtectionObservationNow(
            NativeRemoteWindowProtectionObservation? observation)
        {
            lock (gate)
            {
                pending.Enqueue(observation);
            }
        }

        public void NotifyNativeRemoteWindowProtectionChanged()
        {
            int call = Interlocked.Increment(ref notifyCalls);
            if (call == 1)
            {
                firstNotifyEntered.Set();
                if (!releaseFirstNotify.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException(
                        "Timed out releasing the first protection notification.");
                }
            }

            lock (gate)
            {
                while (pending.TryDequeue(
                    out NativeRemoteWindowProtectionObservation? observation))
                {
                    delivered.Add(observation);
                }
            }

            if (call == 2)
            {
                secondNotifyDrained.Set();
            }

            else if (call == 1 && firstNotifyFailure is not null)
            {
                throw firstNotifyFailure;
            }
        }
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

    private sealed class InjectedProtectionOutOfMemoryException(
        string message) : OutOfMemoryException(message);
}
