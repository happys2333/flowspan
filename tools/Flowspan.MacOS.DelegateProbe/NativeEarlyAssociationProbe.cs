using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Flowspan.Platform.MacOS;

namespace Flowspan.MacOS.DelegateProbe;

internal sealed class NativeEarlyAssociationProbe
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private static NativeEarlyAssociationProbe? processRoot;
    private static string? failedCheck;
    private readonly NativeEarlyAssociationApi native = new();
    private readonly MacOSRemoteWindowStreamDelegateRouter router = new();
    private readonly MacOSRemoteWindowStreamDelegateAssociationCoordinator coordinator;
    private readonly MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer?[] knownInitializers = new MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer[16];
    private nint bridge;
    private nint stoppedSelector;
    private nint activeSelector;
    private nint inactiveSelector;
    private int nativeCallbacks;
    private int containedFailures;
    private int pendingReplayNotifications;
    private int publicationRaceNotifications;
    private int replacementNotifications;
    private int forcedCollectionRounds;
    private int bridgeAllocations;
    private bool publicationRaceProved;
    private bool ambiguityPoisonProved;
    private int ambiguityNotifications;
    private int ambiguityNilReads;
    private int quarantinedInitializers;
    private Exception? reverseFailure;
    private OutOfMemoryException? fatalFailure;

    private NativeEarlyAssociationProbe() => coordinator = new(native, router);
    internal static string? FailedCheck => Volatile.Read(ref failedCheck);

    internal static void Run()
    {
        var probe = new NativeEarlyAssociationProbe();
        Check(Interlocked.CompareExchange(ref processRoot, probe, null) is null, "one permanent early probe root");
        try
        {
            VerifyManagedFailureSelection();
            probe.native.Initialize();
            probe.InitializeBridge();
            nint pool = Native.PushAutoreleasePool();
            try
            {
                probe.VerifyEarlyTerminal();
                probe.VerifyPublicationRace();
                probe.VerifyUnprovedIdentityPoison();
            }
            finally
            {
                Native.PopAutoreleasePool(pool);
                GC.KeepAlive(processRoot);
            }
        }
        catch
        {
            // Assertions must not mask a failure already contained by any
            // native/managed owner, especially an original nested fatal.
            probe.ThrowIfFailure();
            throw;
        }

        probe.ThrowIfFailure();
        probe.VerifyBalancedOwnership();
        Console.WriteLine($"early_association_probe=pass; mode=foundation; permanent_bridges={probe.bridgeAllocations}; process_routers=1; process_coordinators=1; probe_gc_handles=0; method_signatures=4; tag_budget={NativeEarlyAssociationApi.TagCapacity}; maximum_tags_in_use={probe.native.MaximumTagsInUse}; tags_allocated={probe.native.TagsAllocated}; tag_owners_released={probe.native.TagOwnersReleased}; tag_dealloc_entries={probe.native.TagDeallocEntries}; tag_super_deallocations={probe.native.TagSuperclassDeallocations}; tags_in_use={probe.native.TagsInUse}; quarantined_tags={probe.native.QuarantinedTags}; source_owners_acquired={probe.native.SourceOwnersAcquired}; source_owners_released={probe.native.SourceOwnersReleased}; source_reference_retains={probe.native.SourceReferenceRetains}; source_reference_releases={probe.native.SourceReferenceReleases}; tag_reference_retains={probe.native.TagReferenceRetains}; tag_reference_releases={probe.native.TagReferenceReleases}; association_reads={probe.native.AssociationReads}; protocol_reads={probe.native.ProtocolReads}; nil_protocol_reads={probe.native.NilProtocolReads}; native_callbacks={probe.nativeCallbacks}; charged_ownership={probe.coordinator.ChargedOwnershipCount}; uncertain_ownership={probe.coordinator.UncertainOwnershipCount}; native_admission_closed={probe.coordinator.NativeAdmissionClosed.ToString().ToLowerInvariant()}; pending_replay_notifications={probe.pendingReplayNotifications}; publication_race_notifications={probe.publicationRaceNotifications}; replacement_notifications={probe.replacementNotifications}; ambiguity_notifications={probe.ambiguityNotifications}; ambiguity_nil_reads={probe.ambiguityNilReads}; quarantined_initializers={probe.quarantinedInitializers}; same_source_pending_proved=true; publication_race_proved={probe.publicationRaceProved.ToString().ToLowerInvariant()}; ambiguity_poison_proved={probe.ambiguityPoisonProved.ToString().ToLowerInvariant()}; forced_gc_rounds={probe.forcedCollectionRounds}; contained_failures={probe.containedFailures + probe.native.ContainedFailures}; capture_executed=false; SCStream_creations=0; AppKit_initialized=false; permissions_requested=0; pixel_reads=0");
    }

    private unsafe void InitializeBridge()
    {
        nint bridgeClass = Native.AllocateClass(native.ObjectClass, "FlowspanEarlyAssociationBridgeProbe20261005", 0);
        Check(bridgeClass != 0, "early stateless bridge class allocation");
        stoppedSelector = Native.Selector("stream:didStopWithError:");
        activeSelector = Native.Selector("streamDidBecomeActive:");
        inactiveSelector = Native.Selector("streamDidBecomeInactive:");
        Check(Native.AddMethod(bridgeClass, stoppedSelector,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)&Stopped, "v32@0:8@16@24") != 0
            && Native.AddMethod(bridgeClass, activeSelector,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Active, "v24@0:8@16") != 0
            && Native.AddMethod(bridgeClass, inactiveSelector,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Inactive, "v24@0:8@16") != 0,
            "early three typed native method registration");
        Native.RegisterClass(bridgeClass);
        foreach ((nint selector, string encoding) in new[]
        {
            (stoppedSelector, "v32@0:8@16@24"), (activeSelector, "v24@0:8@16"), (inactiveSelector, "v24@0:8@16"),
        })
        {
            nint method = Native.InstanceMethod(bridgeClass, selector);
            Check(method != 0 && Marshal.PtrToStringUTF8(Native.MethodEncoding(method)) == encoding,
                "early bridge runtime method encodings");
        }

        bridge = Native.SendObject(bridgeClass, Native.Selector("new"));
        if (bridge != 0) Interlocked.Increment(ref bridgeAllocations);
        Check(bridge != 0 && NativeAssociationInterop.InstanceSize(bridgeClass)
            == NativeAssociationInterop.InstanceSize(native.ObjectClass), "one permanent stateless early bridge plus-one");
    }

    private void VerifyEarlyTerminal()
    {
        Check(TryBegin(out var initializer) && initializer is not null
            && initializer.MarkDelegatePublished(), "early exact initializer begins before callback");
        nint source = native.CreateSource();
        bool sourceOwned = true;
        int deallocationsBefore = native.TagSuperclassDeallocations;
        try
        {
            Emit(source, MacOSRemoteWindowDelegateSignal.Inactive);
            ThrowIfFailure(initializer);
            Check(coordinator.ChargedOwnershipCount == 1 && coordinator.UncertainOwnershipCount == 0
                && native.SourceReferenceRetains - native.SourceReferenceReleases == 1
                && initializer!.Pending?.Source == source,
                "early pending fact owns its retained exact real source");
            Emit(source, MacOSRemoteWindowDelegateSignal.StoppedWithError);
            int retainsBeforeActive = native.SourceReferenceRetains;
            Emit(source, MacOSRemoteWindowDelegateSignal.Active);
            Check(coordinator.ChargedOwnershipCount == 1 && initializer!.Pending?.Source == source
                && native.SourceReferenceRetains == retainsBeforeActive,
                "same-source terminals coalesce and native Active does not occupy pending storage");
            ForceCollection();
            sourceOwned = false;
            native.ReleaseSourceOwner(source);
            Check(native.TagSuperclassDeallocations == deallocationsBefore,
                "retained pending source survives release of the caller source owner");
            Check(initializer!.CompleteAssociation(source), "early exact native association completes");
            Check(initializer.Pending is null && coordinator.ChargedOwnershipCount == 0
                && native.TagSuperclassDeallocations == deallocationsBefore + 1,
                "completion clears and releases pending plus publication references after actual tag deallocation");
            Check(initializer.Activate(() => pendingReplayNotifications++), "early exact handler activation");
            Check(pendingReplayNotifications == 1 && initializer.AdmissionClosed,
                "early nil association terminal is replayed once after exact publication");
            Retire(initializer);
        }
        finally
        {
            if (sourceOwned) native.ReleaseSourceOwner(source);
        }
    }

    private void VerifyPublicationRace()
    {
        Check(TryBegin(out var original) && original is not null
            && original.MarkDelegatePublished(), "race exact original initializer published");
        nint source = native.CreateSource();
        nint replacementSource = native.CreateSource();
        using var secondNil = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        int nilReads = 0;
        int thirdReads = 0;
        int stage = 0;
        Task? callback = null;
        MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer? replacement = null;
        native.AfterProtocolRead = (readSource, tag) =>
        {
            if (readSource != source) return;
            if (tag == 0 && Interlocked.Increment(ref nilReads) == 2)
            {
                Check(Interlocked.CompareExchange(ref stage, 1, 0) == 0,
                    "race second real nil read finishes before controlled pause");
                secondNil.Set();
                Check(resume.Wait(Deadline), "race callback resumes after controlled publication and replacement");
            }
            else if (tag != 0 && Volatile.Read(ref stage) == 3)
            {
                Check(native.ReadGeneration(tag) == original!.Generation,
                    "race third real read verifies original immutable generation");
                Interlocked.Increment(ref thirdReads);
                Check(Interlocked.CompareExchange(ref stage, 4, 3) == 3,
                    "race third real read occurs strictly after replacement admission");
            }
        };

        try
        {
            callback = Task.Run(() => Emit(source, MacOSRemoteWindowDelegateSignal.Inactive));
            Check(secondNil.Wait(Deadline), "race observes actual first and second nil protocol reads");
            ForceCollection();
            Check(nilReads == 2 && original!.Pending is null && coordinator.ChargedOwnershipCount == 1,
                "race paused callback retains real source before losing pending record attempt");
            Check(original!.CompleteAssociation(source) && original.Pending is null
                && original.Activate(() => Interlocked.Increment(ref publicationRaceNotifications)),
                "race publishes exact association and clears initializer before callback records early fact");
            Check(Interlocked.CompareExchange(ref stage, 2, 1) == 1,
                "race publication and pending clear precede replacement");
            Check(TryBegin(out replacement) && replacement is not null
                && replacement.Generation > original.Generation && replacement.MarkDelegatePublished(),
                "race replacement initializer is live before old callback returns second nil");
            Check(Interlocked.CompareExchange(ref stage, 3, 2) == 2,
                "race replacement admitted before old exact-token recording attempt");
            resume.Set();
            callback.WaitAsync(Deadline).GetAwaiter().GetResult();
            ThrowIfFailure(original);
            ThrowIfFailure(replacement);
            Check(stage == 4 && nilReads == 2 && thirdReads == 1 && publicationRaceNotifications == 1
                && replacementNotifications == 0 && original.AdmissionClosed && !replacement!.AdmissionClosed
                && replacement.Pending is null && coordinator.ChargedOwnershipCount == 0,
                "race losing old token re-reads actual original tag and never snapshots replacement");
            Check(replacement!.CompleteAssociation(replacementSource)
                && replacement.Activate(() => Interlocked.Increment(ref replacementNotifications)),
                "race unaffected replacement remains independently associable and activatable");
            Emit(source, MacOSRemoteWindowDelegateSignal.StoppedWithError);
            Emit(source, MacOSRemoteWindowDelegateSignal.Active);
            Emit(replacementSource, MacOSRemoteWindowDelegateSignal.Active);
            Check(publicationRaceNotifications == 1 && replacementNotifications == 0 && !replacement.AdmissionClosed,
                "race original terminal-once and Active preserve replacement admission");
            Retire(original);
            Retire(replacement);
            publicationRaceProved = true;
        }
        finally
        {
            resume.Set();
            if (callback is not null) callback.WaitAsync(Deadline).GetAwaiter().GetResult();
            native.AfterProtocolRead = null;
            try { native.ReleaseSourceOwner(replacementSource); }
            finally { native.ReleaseSourceOwner(source); }
        }
    }

    private void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Interlocked.Increment(ref forcedCollectionRounds);
    }

    private void VerifyUnprovedIdentityPoison()
    {
        Check(TryBegin(out var original) && original is not null
            && original.MarkDelegatePublished(), "ambiguity original initializer published");
        nint unknownSource = native.CreateSource();
        nint returnedSource = native.CreateSource();
        nint replacementSource = native.CreateSource();
        using var secondNil = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        int stage = 0;
        Task? callback = null;
        MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer? replacement = null;
        native.AfterProtocolRead = (readSource, tag) =>
        {
            if (readSource != unknownSource) return;
            Check(tag == 0, "ambiguity unknown real source never receives an invented numeric tag");
            int reads = Interlocked.Increment(ref ambiguityNilReads);
            if (reads == 2)
            {
                Check(Interlocked.CompareExchange(ref stage, 1, 0) == 0,
                    "ambiguity second actual nil read precedes publication");
                secondNil.Set();
                Check(resume.Wait(Deadline), "ambiguity resumes only after exact initializer replacement");
            }
            else if (reads == 3)
            {
                Check(Interlocked.CompareExchange(ref stage, 4, 3) == 3,
                    "ambiguity third actual nil read follows replacement admission");
            }
        };

        try
        {
            callback = Task.Run(() => Emit(unknownSource, MacOSRemoteWindowDelegateSignal.StoppedWithError));
            Check(secondNil.Wait(Deadline) && ambiguityNilReads == 2 && original!.Pending is null,
                "ambiguity old unknown callback pauses before exact early fact recording");
            Check(original!.CompleteAssociation(returnedSource) && original.Pending is null
                && original.Activate(() => Interlocked.Increment(ref ambiguityNotifications)),
                "ambiguity distinct returned source publishes while old borrowed source remains untagged");
            Check(Interlocked.CompareExchange(ref stage, 2, 1) == 1,
                "ambiguity actual publication clears original initializer");
            Check(TryBegin(out replacement) && replacement is not null
                && replacement.MarkDelegatePublished(), "ambiguity live replacement initializer published");
            Check(Interlocked.CompareExchange(ref stage, 3, 2) == 2,
                "ambiguity replacement admitted before old callback loses its token");
            resume.Set();
            callback.WaitAsync(Deadline).GetAwaiter().GetResult();
            ThrowIfFailure(original);
            ThrowIfFailure(replacement);
            Check(stage == 4 && ambiguityNilReads == 3 && ambiguityNotifications == 0 && replacementNotifications == 0
                && original.Quarantined && replacement!.Quarantined
                && original.AdmissionClosed && replacement.AdmissionClosed
                && !coordinator.NativeAdmissionClosed && coordinator.Failure is null
                && coordinator.ChargedOwnershipCount == 0 && coordinator.UncertainOwnershipCount == 0,
                "unproved old source identity poisons related exact initializers without misattributed terminal delivery");
            int readsBefore = native.AssociationReads;
            Check(!replacement!.CompleteAssociation(replacementSource)
                && !replacement.Activate(() => Interlocked.Increment(ref replacementNotifications))
                && !TryBegin(out var denied) && denied is null
                && native.AssociationReads == readsBefore,
                "ambiguity poison blocks later construction and publication without further native work");
            native.AfterProtocolRead = null;
            Emit(unknownSource, MacOSRemoteWindowDelegateSignal.Inactive);
            Emit(returnedSource, MacOSRemoteWindowDelegateSignal.Inactive);
            Check(original.RetireAsync().AsTask().GetAwaiter().GetResult()
                == MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited
                && replacement.RetireAsync().AsTask().GetAwaiter().GetResult()
                == MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
                "ambiguity retirement joins managed handlers without returning quarantined permits");
            Check(ambiguityNotifications == 0 && replacementNotifications == 0
                && !original.ConfirmCompleteCleanup() && !replacement.ConfirmCompleteCleanup(),
                "identity quarantine remains charged and later actual callbacks notify neither initializer");
            quarantinedInitializers = (original.Quarantined ? 1 : 0) + (replacement.Quarantined ? 1 : 0);
            ambiguityPoisonProved = true;
        }
        finally
        {
            resume.Set();
            if (callback is not null) callback.WaitAsync(Deadline).GetAwaiter().GetResult();
            native.AfterProtocolRead = null;
            try { native.ReleaseSourceOwner(replacementSource); }
            finally
            {
                try { native.ReleaseSourceOwner(returnedSource); }
                finally { native.ReleaseSourceOwner(unknownSource); }
            }
        }
    }

    private void Emit(nint source, MacOSRemoteWindowDelegateSignal signal)
    {
        switch (signal)
        {
            case MacOSRemoteWindowDelegateSignal.StoppedWithError:
                Native.SendTerminal(bridge, stoppedSelector, source, 0);
                break;
            case MacOSRemoteWindowDelegateSignal.Active:
                Native.SendObservation(bridge, activeSelector, source);
                break;
            case MacOSRemoteWindowDelegateSignal.Inactive:
                Native.SendObservation(bridge, inactiveSelector, source);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(signal));
        }
    }

    private void Receive(nint self, nint source, MacOSRemoteWindowDelegateSignal signal)
    {
        Interlocked.Increment(ref nativeCallbacks);
        Check(self == bridge && source != 0, "early typed callback exact bridge and valid source");
        nint pool = Native.PushAutoreleasePool();
        try
        {
            coordinator.Dispatch(source, signal);
        }
        finally
        {
            Native.PopAutoreleasePool(pool);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Stopped(nint self, nint selector, nint source, nint error) =>
        Reverse(self, source, MacOSRemoteWindowDelegateSignal.StoppedWithError);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Active(nint self, nint selector, nint source) =>
        Reverse(self, source, MacOSRemoteWindowDelegateSignal.Active);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Inactive(nint self, nint selector, nint source) =>
        Reverse(self, source, MacOSRemoteWindowDelegateSignal.Inactive);

    private static void Reverse(nint self, nint source, MacOSRemoteWindowDelegateSignal signal)
    {
        NativeEarlyAssociationProbe? probe = Volatile.Read(ref processRoot);
        try
        {
            Check(probe is not null, "early callback permanent process root");
            probe!.Receive(self, source, signal);
        }
        catch (Exception exception)
        {
            if (probe is not null)
            {
                Interlocked.Increment(ref probe.containedFailures);
                Interlocked.CompareExchange(ref probe.reverseFailure, exception, null);
                if (MacOSRemoteWindowFailure.FindFatal(exception) is { } fatal)
                    Interlocked.CompareExchange(ref probe.fatalFailure, fatal, null);
            }
        }
    }

    private void Retire(MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer initializer)
    {
        Check(initializer.RetireAsync().AsTask().GetAwaiter().GetResult()
            == MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited, "early managed handler retirement only");
        ThrowIfFailure(initializer);
        Check(initializer.ConfirmCompleteCleanup(), "early complete task-owned simulated Capture cleanup");
    }

    private void VerifyBalancedOwnership() => Check(native.TagsInUse == 0 && native.QuarantinedTags == 0
        && native.TagsAllocated == native.TagOwnersReleased && native.TagsAllocated == native.TagDeallocEntries
        && native.TagsAllocated == native.TagSuperclassDeallocations
        && native.SourceOwnersAcquired == native.SourceOwnersReleased
        && native.SourceReferenceRetains == native.SourceReferenceReleases
        && native.TagReferenceRetains == native.TagReferenceReleases
        && coordinator.ChargedOwnershipCount == 0 && coordinator.UncertainOwnershipCount == 0
        && !coordinator.NativeAdmissionClosed && containedFailures == 0 && native.ContainedFailures == 0,
        "early explicit native reference and ownership records balanced after actual deallocation");

    private void ThrowIfFailure(MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer? initializer = null)
    {
        Exception? exception = PreferFailure(Volatile.Read(ref fatalFailure), native.Failure);
        exception = PreferFailure(exception, Volatile.Read(ref reverseFailure));
        exception = PreferFailure(exception, coordinator.Failure);
        exception = PreferFailure(exception, initializer?.Failure);
        // A handler can record a fatal and return through Activate successfully.
        // Keep every exact scenario token rooted, even after complete cleanup,
        // so a later failing assertion/finally cannot hide that stored failure.
        foreach (var known in knownInitializers)
        {
            exception = PreferFailure(exception, known?.Failure);
        }

        if (exception is not null) ExceptionDispatchInfo.Throw(exception);
    }

    private bool TryBegin(out MacOSRemoteWindowStreamDelegateAssociationCoordinator.Initializer? initializer)
    {
        if (!coordinator.TryBegin(out initializer) || initializer is null) return false;
        int slot = Array.IndexOf(knownInitializers, null);
        Check(slot >= 0, "bounded exact probe initializer failure roots available");
        knownInitializers[slot] = initializer;
        return true;
    }

    private static Exception? PreferFailure(Exception? primary, Exception? candidate)
    {
        if (primary is not null && MacOSRemoteWindowFailure.FindFatal(primary) is { } primaryFatal) return primaryFatal;
        if (candidate is not null && MacOSRemoteWindowFailure.FindFatal(candidate) is { } candidateFatal) return candidateFatal;
        return primary ?? candidate;
    }

    [SuppressMessage("Usage", "CA2201", Justification = "Managed-only regression checks original nested fatal identity selection; it does not inject a native fault.")]
    private static void VerifyManagedFailureSelection()
    {
        var ordinary = new InvalidOperationException("Managed-only earlier ordinary failure.");
        var fatal = new OutOfMemoryException("Managed-only original fatal identity.");
        var nested = new AggregateException(new InvalidOperationException("Managed-only wrapper.", fatal));
        Check(ReferenceEquals(PreferFailure(ordinary, nested), fatal)
            && ReferenceEquals(PreferFailure(fatal, ordinary), fatal)
            && ReferenceEquals(PreferFailure(ordinary, null), ordinary),
            "managed-only failure selector preserves original nested fatal before earlier ordinary");
    }

    internal static void Check(bool condition, string name)
    {
        if (!condition)
        {
            Interlocked.CompareExchange(ref failedCheck, name, null);
            throw new InvalidOperationException(name);
        }
    }
}
