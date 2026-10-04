using System.Diagnostics.CodeAnalysis;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSRemoteWindowStreamDelegateAssociationCoordinatorTests
{
    [Fact]
    public void RetainedImmutableAssociationRoutesTerminalOnceBeforeReturningReferences()
    {
        var native = new AssociationSystem();
        var router = new MacOSRemoteWindowStreamDelegateRouter();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, router);
        Assert.True(coordinator.TryBegin(out var candidate));
        var initializer = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(candidate);
        Assert.True(initializer.MarkDelegatePublished());
        nint source = native.CreateSource();
        Assert.True(initializer.CompleteAssociation(source));
        int unavailable = 0;
        Assert.True(initializer.Activate(() =>
        {
            Assert.True(initializer.AdmissionClosed);
            Assert.True(native.HasCallbackReferences(source));
            unavailable++;
        }));

        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);
        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.StoppedWithError);

        Assert.Equal(1, unavailable);
        Assert.Null(initializer.Failure);
        Assert.Null(coordinator.Failure);
        Assert.Equal(1, native.SourceReferences(source));
        Assert.Equal(0, native.TagCallbackReferences(source));
    }

    [Fact]
    public async Task UnconfirmedPendingReleaseQuarantinesFullCaptureAndNeverRetriesThatReference()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new(1));
        Assert.True(coordinator.TryBegin(out var candidate));
        var initializer = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(candidate);
        Assert.True(initializer.MarkDelegatePublished());
        nint source = native.CreateSource();
        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);
        var injected = new InvalidOperationException("Injected unconfirmed source release.");
        native.SourceReleaseFailure = injected;

        Assert.True(initializer.RejectInitialization());

        Assert.Same(injected, coordinator.Failure);
        Assert.True(coordinator.NativeAdmissionClosed);
        Assert.Equal(1, coordinator.ChargedOwnershipCount);
        Assert.Equal(1, coordinator.UncertainOwnershipCount);
        Assert.Equal(2, native.SourceReferences(source));
        Assert.Equal(1, native.SourceReleaseAttempts);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited, await initializer.RetireAsync());
        Assert.False(initializer.ConfirmCompleteCleanup());
        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.False(initializer.CompleteAssociation(source));
        Assert.False(coordinator.TryBegin(out _));
        Assert.Equal(1, native.SourceReleaseAttempts);
        Assert.Equal(2, native.AssociationReads);
    }

    [Fact]
    public async Task ExactPublishedRejectionReleasesPendingReferenceButCannotUnpoisonConstruction()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        Assert.True(coordinator.TryBegin(out var candidate));
        var initializer = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(candidate);
        Assert.True(initializer.MarkDelegatePublished());
        nint source = native.CreateSource();
        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.True(initializer.RejectInitialization());

        Assert.Equal(1, native.SourceReferences(source));
        Assert.False(initializer.RejectInitialization());
        Assert.False(initializer.CompleteAssociation(source));
        Assert.False(coordinator.TryBegin(out _));
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited, await initializer.RetireAsync());
        Assert.True(initializer.ConfirmCompleteCleanup());
        Assert.False(coordinator.TryBegin(out _));
        Assert.Null(coordinator.Failure);
    }

    [Fact]
    public void DistinctSecondEarlySourcePoisonsInsteadOfOverwritingAndBalancesBothCallbacks()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        Assert.True(coordinator.TryBegin(out var candidate));
        var initializer = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(candidate);
        Assert.True(initializer.MarkDelegatePublished());
        nint first = native.CreateSource();
        nint second = native.CreateSource();
        coordinator.Dispatch(first, MacOSRemoteWindowDelegateSignal.Inactive);

        coordinator.Dispatch(second, MacOSRemoteWindowDelegateSignal.StoppedWithError);

        Assert.True(initializer.AdmissionClosed);
        Assert.False(initializer.CompleteAssociation(first));
        Assert.False(initializer.Activate(() => Assert.Fail("Ambiguous early sources cannot notify.")));
        Assert.False(coordinator.TryBegin(out _));
        Assert.Equal(1, native.SourceReferences(first));
        Assert.Equal(1, native.SourceReferences(second));
        Assert.Null(coordinator.Failure);
    }

    [Fact]
    public void RepeatedEarlyTerminalForSameRetainedSourceCoalescesAndBalancesExtraCallback()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        Assert.True(coordinator.TryBegin(out var candidate));
        var initializer = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(candidate);
        Assert.True(initializer.MarkDelegatePublished());
        nint source = native.CreateSource();
        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);
        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.StoppedWithError);

        Assert.Equal(2, native.SourceReferences(source));
        Assert.True(initializer.CompleteAssociation(source));
        int unavailable = 0;
        Assert.True(initializer.Activate(() => unavailable++));
        Assert.Equal(1, unavailable);
        Assert.Null(initializer.Failure);
        Assert.Equal(1, native.SourceReferences(source));
        Assert.Null(coordinator.Failure);
    }

    [Fact]
    public void UnknownOldEarlySourceCannotNotifyInitializerThatReturnsDifferentSource()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        Assert.True(coordinator.TryBegin(out var candidate));
        var initializer = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(candidate);
        Assert.True(initializer.MarkDelegatePublished());
        nint oldSource = native.CreateSource();
        nint actualSource = native.CreateSource();
        coordinator.Dispatch(oldSource, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.False(initializer.CompleteAssociation(actualSource));

        Assert.True(initializer.AdmissionClosed);
        Assert.False(initializer.Activate(() => Assert.Fail("Different source cannot notify this Capture.")));
        Assert.False(coordinator.TryBegin(out _));
        Assert.Equal(1, native.SourceReferences(oldSource));
        Assert.False(native.HasAssociation(actualSource));
        Assert.Null(coordinator.Failure);
    }

    [Fact]
    public void LosingExactInitializerNeverResnapshotsReplacementAndNilThirdReadPoisons()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        Assert.True(coordinator.TryBegin(out var candidate));
        var first = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(candidate);
        Assert.True(first.MarkDelegatePublished());
        nint firstSource = native.CreateSource();
        nint unknownOldSource = native.CreateSource();
        MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer? replacement = null;
        native.AfterAssociationRead = number =>
        {
            if (number == 2)
            {
                Assert.True(first.CompleteAssociation(firstSource));
                Assert.True(coordinator.TryBegin(out var next));
                replacement = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(next);
                Assert.True(replacement.MarkDelegatePublished());
            }
        };

        coordinator.Dispatch(unknownOldSource, MacOSRemoteWindowDelegateSignal.Inactive);

        var second = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(replacement);
        Assert.True(second.AdmissionClosed);
        Assert.False(second.Activate(() => Assert.Fail("Unknown old source cannot notify the replacement.")));
        Assert.False(second.CompleteAssociation(native.CreateSource()));
        Assert.False(coordinator.TryBegin(out _));
        Assert.Equal(1, native.SourceReferences(unknownOldSource));
        Assert.Equal(4, native.AssociationReads);
        Assert.Null(coordinator.Failure);
    }

    [Fact]
    public void NilRereadThenPublicationAndPendingClearBeforeRecordRequiresThirdTagRead()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        Assert.True(coordinator.TryBegin(out var candidate));
        var initializer = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(candidate);
        Assert.True(initializer.MarkDelegatePublished());
        nint source = native.CreateSource();
        native.AfterAssociationRead = number =>
        {
            if (number == 2)
            {
                Assert.True(initializer.CompleteAssociation(source));
            }
        };

        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.StoppedWithError);

        int unavailable = 0;
        Assert.True(initializer.Activate(() => unavailable++));
        Assert.Equal(1, unavailable);
        Assert.Null(initializer.Failure);
        Assert.Equal(4, native.AssociationReads);
        Assert.Equal(1, native.SourceReferences(source));
        Assert.Equal(0, native.TagCallbackReferences(source));
        Assert.Null(coordinator.Failure);
    }

    [Fact]
    public void NilThenExactInitializerSnapshotThenPublishedTagRereadRoutesVerifiedGeneration()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        Assert.True(coordinator.TryBegin(out var candidate));
        var initializer = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(candidate);
        Assert.True(initializer.MarkDelegatePublished());
        nint source = native.CreateSource();
        native.BeforeAssociationRead = number =>
        {
            if (number == 2)
            {
                Assert.True(initializer.CompleteAssociation(source));
            }
        };

        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.StoppedWithError);

        int unavailable = 0;
        Assert.True(initializer.Activate(() => unavailable++));
        Assert.Equal(1, unavailable);
        Assert.Null(initializer.Failure);
        Assert.Equal(3, native.AssociationReads);
        Assert.Equal(1, native.SourceReferences(source));
        Assert.Equal(0, native.TagCallbackReferences(source));
        Assert.Null(coordinator.Failure);
    }

    [Fact]
    public void EarlyNilTagRetainsExactSourceUntilSameSourceAssociationAndTerminalOnce()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        Assert.True(coordinator.TryBegin(out var candidate));
        var initializer = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(candidate);
        Assert.True(initializer.MarkDelegatePublished());
        nint source = native.CreateSource();

        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.Equal(2, native.SourceReferences(source));
        Assert.Equal(2, native.AssociationReads);
        Assert.True(initializer.CompleteAssociation(source));
        Assert.Equal(1, native.SourceReferences(source));
        int unavailable = 0;
        Assert.True(initializer.Activate(() => unavailable++));
        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.StoppedWithError);
        Assert.Equal(1, unavailable);
        Assert.Null(initializer.Failure);
        Assert.True(initializer.AdmissionClosed);
        Assert.Null(coordinator.Failure);
    }

    [Fact]
    public void AssociationPublicationRetainsSourceAcrossReentryThatReleasesCallerOwner()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        Assert.True(coordinator.TryBegin(out var candidate));
        var initializer = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(candidate);
        Assert.True(initializer.MarkDelegatePublished());
        nint source = native.CreateSource();
        native.AfterAssociate = associated =>
        {
            Assert.Equal(source, associated);
            Assert.True(native.SourceReferences(source) > 1);
            native.ReleaseCallerSource(source);
            Assert.True(native.SourceReferences(source) > 0);
        };

        Assert.True(initializer.CompleteAssociation(source));

        Assert.Null(coordinator.Failure);
        Assert.Null(initializer.Failure);
        Assert.Equal(0, native.SourceReferences(source));
        Assert.Equal(0, native.TagReferences(source));
        Assert.Equal(0, coordinator.ChargedOwnershipCount);
    }

    [Fact]
    public void SameInitializerHasOnePublicationOwnerEvenWhenAssociationReentersCompletion()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        var initializer = BeginPublished(coordinator);
        nint source = native.CreateSource();
        bool? reentrant = null;
        native.AfterAssociate = associated => reentrant = initializer.CompleteAssociation(associated);

        Assert.True(initializer.CompleteAssociation(source));

        Assert.Equal(false, reentrant);
        Assert.Equal(1, native.AssociationsPublished);
        Assert.Null(coordinator.Failure);
        Assert.Null(initializer.Failure);
        Assert.Equal(0, coordinator.ChargedOwnershipCount);
    }

    [Fact]
    public void CallbackOwnsSourceAndTagThroughHandlerReentryThatReleasesCallerSource()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        var initializer = BeginPublished(coordinator);
        nint source = native.CreateSource();
        Assert.True(initializer.CompleteAssociation(source));
        int unavailable = 0;
        Assert.True(initializer.Activate(() =>
        {
            native.ReleaseCallerSource(source);
            Assert.Equal(1, native.SourceReferences(source));
            Assert.Equal(2, native.TagReferences(source));
            unavailable++;
        }));

        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.Equal(1, unavailable);
        Assert.Null(coordinator.Failure);
        Assert.Null(initializer.Failure);
        Assert.Equal(0, native.SourceReferences(source));
        Assert.Equal(0, native.TagReferences(source));
        Assert.Equal(0, coordinator.ChargedOwnershipCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnconfirmedTagReleaseDoesNotRetryAndStillBalancesKnownIndependentSource(bool afterEffect)
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        var initializer = BeginPublished(coordinator);
        nint source = native.CreateSource();
        Assert.True(initializer.CompleteAssociation(source));
        var injected = new InvalidOperationException("Injected unconfirmed tag release.");
        native.TagReleaseFailure = injected;
        native.FaultAfterEffect = afterEffect;
        int unavailable = 0;
        Assert.True(initializer.Activate(() => unavailable++));

        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.Equal(1, unavailable);
        Assert.Same(injected, coordinator.Failure);
        Assert.True(coordinator.NativeAdmissionClosed);
        Assert.Equal(1, coordinator.ChargedOwnershipCount);
        Assert.Equal(1, coordinator.UncertainOwnershipCount);
        Assert.Equal(1, native.SourceReferences(source));
        Assert.Equal(afterEffect ? 1 : 2, native.TagReferences(source));
        Assert.Equal(2, native.TagReleaseAttempts);
        Assert.Equal(2, native.SourceReleaseAttempts);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited, await initializer.RetireAsync());
        Assert.False(initializer.ConfirmCompleteCleanup());
        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.Equal(2, native.TagReleaseAttempts);
        Assert.Equal(2, native.SourceReleaseAttempts);
        Assert.Null(initializer.Failure);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UnconfirmedAcquireRetainsBoundedOwnershipFactAndStopsNewNativeWork(bool tagRead, bool afterEffect)
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        var initializer = BeginPublished(coordinator);
        nint source = native.CreateSource();
        Assert.True(initializer.CompleteAssociation(source));
        var injected = new InvalidOperationException("Injected uncertain acquisition.");
        native.FaultAfterEffect = afterEffect;
        if (tagRead) { native.TagReadFailure = injected; }
        else { native.SourceRetainFailure = injected; }

        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.Same(injected, coordinator.Failure);
        Assert.True(coordinator.NativeAdmissionClosed);
        Assert.True(initializer.AdmissionClosed);
        Assert.False(initializer.Activate(() => Assert.Fail("Runtime ownership fault is not sharing availability.")));
        Assert.Equal(1, coordinator.ChargedOwnershipCount);
        Assert.Equal(1, coordinator.UncertainOwnershipCount);
        Assert.Equal(!tagRead && afterEffect ? 2 : 1, native.SourceReferences(source));
        Assert.Equal(tagRead && afterEffect ? 2 : 1, native.TagReferences(source));
        int calls = native.NativeCalls;
        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.False(initializer.CompleteAssociation(source));
        Assert.False(coordinator.TryBegin(out _));
        Assert.Equal(calls, native.NativeCalls);
        Assert.Null(initializer.Failure);
    }

    [Fact]
    public void UnknownNilSourceReleaseFailureChargesProcessRecordWithoutGuessingCapture()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        nint source = native.CreateSource();
        var injected = new InvalidOperationException("Unknown source ownership cannot be released with certainty.");
        native.SourceReleaseFailure = injected;

        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.Same(injected, coordinator.Failure);
        Assert.True(coordinator.NativeAdmissionClosed);
        Assert.Equal(1, coordinator.ChargedOwnershipCount);
        Assert.Equal(1, coordinator.UncertainOwnershipCount);
        Assert.Equal(2, native.SourceReferences(source));
        int calls = native.NativeCalls;
        for (int index = 0; index < 32; index++)
        {
            coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);
        }

        Assert.Equal(calls, native.NativeCalls);
        Assert.Equal(1, coordinator.ChargedOwnershipCount);
        Assert.False(coordinator.TryBegin(out _));
    }

    [Fact]
    public void SeventeenthLiveOwnershipEntryClosesRuntimeWithoutSilentlyLosingTerminal()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        var initializer = BeginPublished(coordinator);
        nint source = native.CreateSource();
        Assert.True(initializer.CompleteAssociation(source));
        int unavailable = 0;
        Assert.True(initializer.Activate(() => unavailable++));
        int nested = 0;
        int maximumCharged = 0;
        native.BeforeGenerationRead = () =>
        {
            nested++;
            maximumCharged = Math.Max(maximumCharged, coordinator.ChargedOwnershipCount);
            if (nested <= 16) { coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive); }
        };

        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.IsType<InvalidOperationException>(coordinator.Failure);
        Assert.True(coordinator.NativeAdmissionClosed);
        Assert.True(initializer.AdmissionClosed);
        Assert.Equal(16, maximumCharged);
        Assert.Equal(16, nested);
        Assert.Equal(0, unavailable);
        Assert.Equal(0, coordinator.ChargedOwnershipCount);
        Assert.Equal(0, coordinator.UncertainOwnershipCount);
        Assert.Equal(1, native.SourceReferences(source));
        Assert.Equal(1, native.TagReferences(source));
        Assert.False(coordinator.TryBegin(out _));
        Assert.Null(initializer.Failure);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Injects and checks primary fatal failure preservation at the native boundary.")]
    public void PrimaryFatalGenerationFaultSurvivesCleanupFaultWhileKnownSourceIsReleased()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        var initializer = BeginPublished(coordinator);
        nint source = native.CreateSource();
        Assert.True(initializer.CompleteAssociation(source));
        var primary = new OutOfMemoryException("Primary native-boundary allocation fault.");
        native.GenerationFailure = primary;
        native.TagReleaseFailure = new InvalidOperationException("Secondary cleanup failure.");

        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.Same(primary, coordinator.Failure);
        Assert.True(initializer.AdmissionClosed);
        Assert.True(coordinator.NativeAdmissionClosed);
        Assert.Equal(1, native.SourceReferences(source));
        Assert.Equal(1, coordinator.UncertainOwnershipCount);
        Assert.Null(initializer.Failure);
    }

    [Fact]
    public async Task HealthyCleanupReusesCaptureAndOwnershipSlotsWithoutReusingInitializer()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new(1));
        long lastGeneration = 0;
        for (int index = 0; index < 32; index++)
        {
            var initializer = BeginPublished(coordinator);
            Assert.True(initializer.Generation > lastGeneration);
            lastGeneration = initializer.Generation;
            nint source = native.CreateSource();
            Assert.True(initializer.CompleteAssociation(source));
            coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);
            Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited, await initializer.RetireAsync());
            Assert.True(initializer.ConfirmCompleteCleanup());
            Assert.False(initializer.CompleteAssociation(source));
            Assert.False(initializer.RejectInitialization());
            Assert.Equal(0, coordinator.ChargedOwnershipCount);
            Assert.Null(initializer.Failure);
            Assert.Null(coordinator.Failure);
            native.ReleaseCallerSource(source);
        }
    }

    [Fact]
    public void AllNativeOperationsRunOutsideCoordinatorStateGate()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        var initializer = BeginPublished(coordinator);
        nint source = native.CreateSource();
        native.Operation = operation =>
        {
            Task readGate = Task.Run(() => _ = coordinator.NativeAdmissionClosed);
            Assert.True(readGate.Wait(TimeSpan.FromSeconds(5)));
        };

        Assert.True(initializer.CompleteAssociation(source));
        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.Null(coordinator.Failure);
        Assert.Null(initializer.Failure);
        Assert.Equal(0, coordinator.ChargedOwnershipCount);
    }

    private static MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer BeginPublished(
        MacOSRemoteWindowStreamDelegateAssociationCoordinator coordinator)
    {
        Assert.True(coordinator.TryBegin(out var candidate));
        var initializer = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(candidate);
        Assert.True(initializer.MarkDelegatePublished());
        return initializer;
    }

    [Fact]
    public void AlreadyAdmittedRetainMayReturnAfterFaultButNextNativeWorkIsRejectedAndKnownReferenceReleased()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        var initializer = BeginPublished(coordinator);
        nint source = native.CreateSource();
        nint unknown = native.CreateSource();
        Assert.True(initializer.CompleteAssociation(source));
        var injected = new InvalidOperationException("Concurrent unknown-source ownership fault.");
        native.Operation = operation =>
        {
            if (operation == nameof(IMacOSRemoteWindowStreamDelegateAssociationApi.RetainSource))
            {
                native.Operation = null;
                native.SourceReleaseFailure = injected;
                coordinator.Dispatch(unknown, MacOSRemoteWindowDelegateSignal.Inactive);
                native.SourceReleaseFailure = null;
            }
        };

        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.Same(injected, coordinator.Failure);
        Assert.True(coordinator.NativeAdmissionClosed);
        Assert.True(initializer.AdmissionClosed);
        Assert.Equal(3, native.AssociationReads);
        Assert.Equal(1, native.SourceReferences(source));
        Assert.Equal(2, native.SourceReferences(unknown));
        Assert.Equal(1, coordinator.ChargedOwnershipCount);
        Assert.Equal(1, coordinator.UncertainOwnershipCount);
        int calls = native.NativeCalls;
        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.Equal(calls, native.NativeCalls);
        Assert.Null(initializer.Failure);
    }

    [Fact]
    public void RejectionDuringAssociationPublicationCannotResurrectOrCloseUnrelatedAssociatedCapture()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        var unrelated = BeginPublished(coordinator);
        nint unrelatedSource = native.CreateSource();
        Assert.True(unrelated.CompleteAssociation(unrelatedSource));
        int unrelatedUnavailable = 0;
        Assert.True(unrelated.Activate(() => unrelatedUnavailable++));
        var rejected = BeginPublished(coordinator);
        nint rejectedSource = native.CreateSource();
        bool? rejection = null;
        native.AfterAssociate = source =>
        {
            native.AfterAssociate = null;
            rejection = rejected.RejectInitialization();
        };

        Assert.False(rejected.CompleteAssociation(rejectedSource));

        Assert.Equal(true, rejection);
        Assert.True(rejected.AdmissionClosed);
        Assert.True(rejected.Quarantined);
        Assert.False(rejected.Activate(() => Assert.Fail("Rejected publication cannot resurrect this Capture.")));
        Assert.False(rejected.CompleteAssociation(rejectedSource));
        Assert.False(coordinator.TryBegin(out _));
        Assert.False(coordinator.NativeAdmissionClosed);
        Assert.False(unrelated.AdmissionClosed);
        Assert.False(unrelated.Quarantined);
        Assert.Equal(0, unrelatedUnavailable);
        Assert.Equal(0, coordinator.ChargedOwnershipCount);
        Assert.Equal(1, native.SourceReferences(rejectedSource));
        coordinator.Dispatch(unrelatedSource, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.Equal(1, unrelatedUnavailable);
        Assert.Null(coordinator.Failure);
        Assert.Null(rejected.Failure);
        Assert.Null(unrelated.Failure);
    }

    [Fact]
    public void DistinctActiveSourceNeitherAcquiresNativeReferencesNorOverwritesPendingTerminal()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        var initializer = BeginPublished(coordinator);
        nint terminalSource = native.CreateSource();
        nint activeSource = native.CreateSource();
        coordinator.Dispatch(terminalSource, MacOSRemoteWindowDelegateSignal.Inactive);
        int calls = native.NativeCalls;

        coordinator.Dispatch(activeSource, MacOSRemoteWindowDelegateSignal.Active);

        Assert.Equal(calls, native.NativeCalls);
        Assert.Equal(1, coordinator.ChargedOwnershipCount);
        Assert.Equal(2, native.SourceReferences(terminalSource));
        Assert.Equal(1, native.SourceReferences(activeSource));
        Assert.True(initializer.CompleteAssociation(terminalSource));
        int unavailable = 0;
        Assert.True(initializer.Activate(() => unavailable++));
        coordinator.Dispatch(terminalSource, MacOSRemoteWindowDelegateSignal.StoppedWithError);
        Assert.Equal(1, unavailable);
        Assert.Equal(0, coordinator.ChargedOwnershipCount);
        Assert.False(coordinator.NativeAdmissionClosed);
        Assert.Null(coordinator.Failure);
        Assert.Null(initializer.Failure);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Verifies the exact nested fatal fault supersedes an earlier ordinary fault without losing known cleanup.")]
    public void OrdinaryGenerationFailureEscalatesToExactNestedFatalCleanupFault()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new());
        var initializer = BeginPublished(coordinator);
        nint source = native.CreateSource();
        Assert.True(initializer.CompleteAssociation(source));
        int unavailable = 0;
        Assert.True(initializer.Activate(() => unavailable++));
        var ordinary = new InvalidOperationException("Ordinary generation failure.");
        var fatal = new OutOfMemoryException("Original nested cleanup allocation fault.");
        native.GenerationFailure = ordinary;
        native.TagReleaseFailure = new InvalidOperationException("Cleanup wrapper.",
            new AggregateException(new InvalidOperationException("Nested wrapper.", fatal)));
        Exception? failureBeforeCleanup = null;
        native.Operation = operation =>
        {
            if (operation == nameof(IMacOSRemoteWindowStreamDelegateAssociationApi.ReleaseTag))
            {
                native.Operation = null;
                failureBeforeCleanup = coordinator.Failure;
            }
        };

        coordinator.Dispatch(source, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.Same(ordinary, failureBeforeCleanup);
        Assert.Same(fatal, coordinator.Failure);
        Assert.True(coordinator.NativeAdmissionClosed);
        Assert.True(initializer.AdmissionClosed);
        Assert.Equal(0, unavailable);
        Assert.Equal(1, native.SourceReferences(source));
        Assert.Equal(2, native.SourceReleaseAttempts);
        Assert.Equal(1, coordinator.ChargedOwnershipCount);
        Assert.Equal(1, coordinator.UncertainOwnershipCount);
        Assert.Null(initializer.Failure);
    }

    [Fact]
    public void LateOldCallbackAfterCompleteCleanupCannotReoccupyOrQuarantineOldCaptureOrNotifyReplacement()
    {
        var native = new AssociationSystem();
        var coordinator = new MacOSRemoteWindowStreamDelegateAssociationCoordinator(native, new(1));
        var old = BeginPublished(coordinator);
        nint oldSource = native.CreateSource();
        nint replacementSource = native.CreateSource();
        Assert.True(old.CompleteAssociation(oldSource));
        var injected = new InvalidOperationException("Late old callback release uncertainty.");
        MacOSRemoteWindowCallbackRetirement? retirement = null;
        bool cleanup = false;
        bool reserved = false;
        bool published = false;
        bool associated = false;
        bool activated = false;
        int replacementUnavailable = 0;
        MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer? replacement = null;
        native.BeforeGenerationRead = () =>
        {
            native.BeforeGenerationRead = null;
            ValueTask<MacOSRemoteWindowCallbackRetirement> result = old.RetireAsync();
            retirement = result.IsCompletedSuccessfully ? result.Result : null;
            native.ReleaseCallerSource(oldSource);
            cleanup = old.ConfirmCompleteCleanup();
            reserved = coordinator.TryBegin(out var candidate);
            replacement = candidate;
            if (replacement is not null)
            {
                published = replacement.MarkDelegatePublished();
                associated = replacement.CompleteAssociation(replacementSource);
                activated = replacement.Activate(() => replacementUnavailable++);
            }

            native.TagReleaseFailure = injected;
        };

        coordinator.Dispatch(oldSource, MacOSRemoteWindowDelegateSignal.Inactive);

        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited, retirement);
        Assert.True(cleanup);
        Assert.True(reserved);
        Assert.True(published);
        Assert.True(associated);
        Assert.True(activated);
        var next = Assert.IsType<MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer>(replacement);
        Assert.True(next.Generation > old.Generation);
        Assert.Equal(0, replacementUnavailable);
        Assert.False(old.Quarantined);
        Assert.False(old.ConfirmCompleteCleanup());
        Assert.Same(injected, coordinator.Failure);
        Assert.True(coordinator.NativeAdmissionClosed);
        Assert.True(next.AdmissionClosed);
        Assert.Equal(0, native.SourceReferences(oldSource));
        Assert.Equal(1, native.TagReferences(oldSource));
        Assert.Equal(1, native.SourceReferences(replacementSource));
        Assert.Equal(1, native.TagReferences(replacementSource));
        Assert.Equal(1, coordinator.ChargedOwnershipCount);
        Assert.Equal(1, coordinator.UncertainOwnershipCount);
        Assert.Null(old.Failure);
        Assert.Null(next.Failure);
    }

    private sealed class AssociationSystem : IMacOSRemoteWindowStreamDelegateAssociationApi
    {
        private readonly Dictionary<nint, Source> sources = [];
        private readonly Dictionary<nint, Tag> tags = [];
        private nint next = 100;
        internal int AssociationReads { get; private set; }
        internal Action<int>? BeforeAssociationRead { get; set; }
        internal Action<int>? AfterAssociationRead { get; set; }
        internal Exception? SourceReleaseFailure { get; set; }
        internal int SourceReleaseAttempts { get; private set; }
        internal Action<nint>? AfterAssociate { get; set; }
        internal int AssociationsPublished { get; private set; }
        internal Exception? SourceRetainFailure { get; set; }
        internal Exception? TagReadFailure { get; set; }
        internal Exception? TagReleaseFailure { get; set; }
        internal Exception? GenerationFailure { get; set; }
        internal bool FaultAfterEffect { get; set; }
        internal Action? BeforeGenerationRead { get; set; }
        internal Action<string>? Operation { get; set; }
        internal int TagReleaseAttempts { get; private set; }
        internal int NativeCalls { get; private set; }

        internal nint CreateSource()
        {
            nint source = ++next;
            sources.Add(source, new Source());
            return source;
        }

        internal int SourceReferences(nint source) => sources[source].References;
        internal bool HasAssociation(nint source) => sources[source].Tag != 0;
        internal int TagCallbackReferences(nint source) => tags[sources[source].Tag].References - 1;
        internal int TagReferences(nint source) => tags[sources[source].Tag].References;
        internal void ReleaseCallerSource(nint source) => ReleaseSourceCore(source);
        internal bool HasCallbackReferences(nint source) => SourceReferences(source) > 1 && TagCallbackReferences(source) > 0;

        public void RetainSource(nint source)
        {
            EnterNative(nameof(RetainSource));
            if (SourceRetainFailure is not null && !FaultAfterEffect) { throw SourceRetainFailure; }
            sources[source].References++;
            if (SourceRetainFailure is not null) { throw SourceRetainFailure; }
        }
        public nint ReadAndRetainTag(nint retainedSource)
        {
            EnterNative(nameof(ReadAndRetainTag));
            if (TagReadFailure is not null && !FaultAfterEffect) { throw TagReadFailure; }
            Source source = sources[retainedSource];
            Assert.True(source.References > 0);
            AssociationReads++;
            BeforeAssociationRead?.Invoke(AssociationReads);
            nint observed = source.Tag;
            if (observed != 0)
            {
                tags[observed].References++;
            }

            AfterAssociationRead?.Invoke(AssociationReads);
            if (TagReadFailure is not null) { throw TagReadFailure; }
            return observed;
        }

        public long ReadGeneration(nint retainedTag)
        {
            EnterNative(nameof(ReadGeneration));
            BeforeGenerationRead?.Invoke();
            if (GenerationFailure is not null) { throw GenerationFailure; }
            Assert.True(tags[retainedTag].References > 1);
            return tags[retainedTag].Generation;
        }

        public void AssociateGeneration(nint retainedSource, long generation)
        {
            EnterNative(nameof(AssociateGeneration));
            Assert.Equal(0, sources[retainedSource].Tag);
            nint tag = ++next;
            tags.Add(tag, new Tag(generation));
            sources[retainedSource].Tag = tag;
            AssociationsPublished++;
            AfterAssociate?.Invoke(retainedSource);
        }

        public void ReleaseTag(nint retainedTag)
        {
            EnterNative(nameof(ReleaseTag));
            TagReleaseAttempts++;
            if (TagReleaseFailure is not null && !FaultAfterEffect) { throw TagReleaseFailure; }
            tags[retainedTag].References--;
            if (TagReleaseFailure is not null) { throw TagReleaseFailure; }
        }
        public void ReleaseSource(nint retainedSource)
        {
            EnterNative(nameof(ReleaseSource));
            SourceReleaseAttempts++;
            if (SourceReleaseFailure is not null && !FaultAfterEffect)
            {
                throw SourceReleaseFailure;
            }

            ReleaseSourceCore(retainedSource);
            if (SourceReleaseFailure is not null) { throw SourceReleaseFailure; }
        }

        private void EnterNative(string operation)
        {
            NativeCalls++;
            Operation?.Invoke(operation);
        }

        private void ReleaseSourceCore(nint source)
        {
            Source value = sources[source];
            Assert.True(value.References > 0);
            value.References--;
            if (value.References == 0 && value.Tag != 0)
            {
                tags[value.Tag].References--;
            }
        }

        private sealed class Source
        {
            internal int References { get; set; } = 1;
            internal nint Tag { get; set; }
        }

        private sealed class Tag(long generation)
        {
            internal int References { get; set; } = 1;
            internal long Generation { get; } = generation;
        }
    }
}
