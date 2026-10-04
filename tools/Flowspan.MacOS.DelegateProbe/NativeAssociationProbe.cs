using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Flowspan.Platform.MacOS;

namespace Flowspan.MacOS.DelegateProbe;

// Own NSObject sources only. No ScreenCaptureKit, AppKit or permission calls.
internal sealed class NativeAssociationProbe
{
    private const int Capacity = 16;
    private static NativeAssociationProbe? processRoot;
    private static string? failedCheck;
    private readonly MacOSRemoteWindowStreamDelegateRouter router = new();
    private nint foundationLibrary;
    private nint objectClass;
    private nint tagClass;
    private nint deallocSelector;
    private nint bridge;
    private nint stoppedSelector;
    private nint activeSelector;
    private nint inactiveSelector;
    private int generationOffset;
    private int tagsInUse;
    private int maximumTagsInUse;
    private int tagsAllocated;
    private int tagOwnersReleased;
    private int quarantinedTags;
    private int sourceOwnersAcquired;
    private int sourceOwnersReleased;
    private int bridgeAllocations;
    private int tagDeallocEntries;
    private int tagSuperclassDeallocations;
    private int nativeCallbacks;
    private int callbackSourceRetains;
    private int callbackSourceReleases;
    private int callbackTagRetains;
    private int callbackTagReleases;
    private int unknownCallbacks;
    private int containedFailures;
    private int healthyGenerations;
    private int forcedCollectionRounds;
    private Exception? reverseFailure;
    private OutOfMemoryException? fatalFailure;
    internal static string? FailedCheck => Volatile.Read(ref failedCheck);
    private Exception? Failure => Volatile.Read(ref fatalFailure) ?? Volatile.Read(ref reverseFailure);

    internal static void Run()
    {
        var probe = new NativeAssociationProbe();
        Check(Interlocked.CompareExchange(ref processRoot, probe, null) is null,
            "one process-rooted association probe and router");
        probe.InitializeTagClass();
        probe.InitializeBridge();
        nint pool = Native.PushAutoreleasePool();
        try
        {
            probe.VerifyTagLifetime();
            probe.VerifyBorrowedCallbackLifetime();
            probe.VerifyUnknownAssociation();
            probe.VerifyLateGenerationIsolation();
            probe.VerifyHealthyGenerations();
            probe.VerifyIndependentTagBudget();
            probe.ForceCollection();
        }
        finally
        {
            Native.PopAutoreleasePool(pool);
            GC.KeepAlive(processRoot);
        }

        probe.ThrowIfFailure();
        Check(probe.bridgeAllocations == 1 && probe.foundationLibrary != 0
            && probe.tagsInUse == 0 && probe.quarantinedTags == 0 && probe.containedFailures == 0
            && probe.tagsAllocated == probe.tagOwnersReleased
            && probe.tagsAllocated == probe.tagDeallocEntries
            && probe.tagsAllocated == probe.tagSuperclassDeallocations
            && probe.sourceOwnersAcquired == probe.sourceOwnersReleased
            && probe.callbackSourceRetains == probe.callbackSourceReleases
            && probe.callbackTagRetains == probe.callbackTagReleases
            && probe.callbackSourceRetains == probe.nativeCallbacks
            && probe.callbackTagRetains + probe.unknownCallbacks == probe.nativeCallbacks
            && probe.maximumTagsInUse == Capacity && probe.healthyGenerations == 64,
            "complete healthy run balances all explicit native ownership and tag deallocation budgets");
        Console.WriteLine($"association_probe=pass; mode=foundation; permanent_bridges={probe.bridgeAllocations}; process_routers=1; probe_gc_handles=0; method_signatures=4; tag_budget={Capacity}; maximum_tags_in_use={probe.maximumTagsInUse}; tags_allocated={probe.tagsAllocated}; tag_owners_released={probe.tagOwnersReleased}; tag_dealloc_entries={probe.tagDeallocEntries}; tag_super_deallocations={probe.tagSuperclassDeallocations}; tags_in_use={probe.tagsInUse}; quarantined_tags={probe.quarantinedTags}; source_owners_acquired={probe.sourceOwnersAcquired}; source_owners_released={probe.sourceOwnersReleased}; native_callbacks={probe.nativeCallbacks}; unknown_callbacks={probe.unknownCallbacks}; callback_source_retains={probe.callbackSourceRetains}; callback_source_releases={probe.callbackSourceReleases}; callback_tag_retains={probe.callbackTagRetains}; callback_tag_releases={probe.callbackTagReleases}; healthy_generations={probe.healthyGenerations}; forced_gc_rounds={probe.forcedCollectionRounds}; contained_failures={probe.containedFailures}; capture_executed=false; SCStream_creations=0; AppKit_initialized=false; permissions_requested=0; pixel_reads=0; early_publication_proved=false");
    }

    private unsafe void InitializeBridge()
    {
        nint bridgeClass = Native.AllocateClass(objectClass, "FlowspanAssociationBridgeProbe20261005", 0);
        Check(bridgeClass != 0, "permanent stateless bridge class allocation");
        stoppedSelector = Native.Selector("stream:didStopWithError:");
        activeSelector = Native.Selector("streamDidBecomeActive:");
        inactiveSelector = Native.Selector("streamDidBecomeInactive:");
        Check(Native.AddMethod(bridgeClass, stoppedSelector,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)&Stopped,
            "v32@0:8@16@24") != 0, "permanent terminal method registration");
        Check(Native.AddMethod(bridgeClass, activeSelector,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&BecameActive,
            "v24@0:8@16") != 0, "permanent active method registration");
        Check(Native.AddMethod(bridgeClass, inactiveSelector,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&BecameInactive,
            "v24@0:8@16") != 0, "permanent inactive method registration");
        Native.RegisterClass(bridgeClass);
        foreach ((nint selector, string encoding) in new[]
        {
            (stoppedSelector, "v32@0:8@16@24"),
            (activeSelector, "v24@0:8@16"),
            (inactiveSelector, "v24@0:8@16"),
        })
        {
            nint method = Native.InstanceMethod(bridgeClass, selector);
            Check(method != 0 && Marshal.PtrToStringUTF8(Native.MethodEncoding(method)) == encoding,
                "permanent bridge runtime method encoding");
        }

        bridge = Native.SendObject(bridgeClass, Native.Selector("new"));
        if (bridge != 0) Interlocked.Increment(ref bridgeAllocations);
        Check(bridge != 0 && NativeAssociationInterop.InstanceSize(bridgeClass)
            == NativeAssociationInterop.InstanceSize(objectClass), "one plus-one stateless permanent bridge");
    }

    private void VerifyBorrowedCallbackLifetime()
    {
        Check(router.TryReserve(out var registration) && registration is not null, "callback Capture permit reservation");
        nint source = CreateSource();
        bool sourceOwned = true;
        int notifications = 0;
        int retainsBefore = callbackSourceRetains;
        int tagsBefore = callbackTagRetains;
        int deallocationsBefore = tagSuperclassDeallocations;
        try
        {
            Check(registration!.MarkDelegatePublished() && TryAssociate(source, registration.Generation)
                && registration.ConfirmAssociation(), "callback immutable association publication");
            Check(registration.Activate(() =>
            {
                Check(callbackSourceRetains == retainsBefore + 1 && callbackTagRetains == tagsBefore + 1,
                    "callback owns source and tag before handler reentry");
                sourceOwned = false;
                ReleaseSourceOwner(source);
                Check(tagSuperclassDeallocations == deallocationsBefore,
                    "callback references keep association alive after handler releases caller source owner");
                Interlocked.Increment(ref notifications);
            }), "callback handler activation");
            ForceCollection();
            Emit(source, MacOSRemoteWindowDelegateSignal.StoppedWithError);
            ThrowIfFailure(registration);
            Check(registration.Failure is null && Failure is null,
                "callback and router failures remain externally observable");
            Check(notifications == 1 && !sourceOwned && registration.AdmissionClosed
                && tagsInUse == 0 && tagSuperclassDeallocations == deallocationsBefore + 1
                && callbackSourceRetains == callbackSourceReleases && callbackTagRetains == callbackTagReleases,
                "callback finally balances native ownership after releasing caller-owned source");
            Retire(registration);
        }
        finally
        {
            if (sourceOwned) ReleaseSourceOwner(source);
        }
    }

    private void VerifyUnknownAssociation()
    {
        Check(router.TryReserve(out var registration) && registration is not null, "unknown-tag current permit reservation");
        nint current = CreateSource();
        nint unknown = CreateSource();
        int notifications = 0;
        try
        {
            Check(registration!.MarkDelegatePublished() && TryAssociate(current, registration.Generation)
                && registration.ConfirmAssociation() && registration.Activate(() => notifications++),
                "unknown-tag current generation activation");
            Emit(unknown, MacOSRemoteWindowDelegateSignal.Inactive);
            ThrowIfFailure(registration);
            Check(Failure is null && registration.Failure is null
                && !registration.AdmissionClosed && notifications == 0,
                "unknown untagged source is rejected without guessing current generation or failing known source");
            Emit(current, MacOSRemoteWindowDelegateSignal.Inactive);
            Check(notifications == 1 && registration.AdmissionClosed, "known numeric tag remains independently routable");
            Retire(registration);
        }
        finally
        {
            ReleaseSourceOwner(unknown);
            ReleaseSourceOwner(current);
        }
    }

    private void VerifyLateGenerationIsolation()
    {
        Check(router.TryReserve(out var oldRegistration) && oldRegistration is not null,
            "old-generation permit reservation");
        nint oldSource = CreateSource();
        nint currentSource = 0;
        int oldNotifications = 0;
        int currentNotifications = 0;
        try
        {
            Publish(oldSource, oldRegistration!);
            Check(oldRegistration!.Activate(() => oldNotifications++), "old-generation native handler activation");
            Retire(oldRegistration);
            Check(router.TryReserve(out var current) && current is not null
                && current.Generation > oldRegistration.Generation,
                "complete managed cleanup reuses a permit but never a generation");
            currentSource = CreateSource();
            Publish(currentSource, current!);
            Check(current!.Activate(() => currentNotifications++), "replacement native handler activation");
            ForceCollection();
            Emit(currentSource, MacOSRemoteWindowDelegateSignal.Active);
            Emit(oldSource, MacOSRemoteWindowDelegateSignal.Active);
            Emit(oldSource, MacOSRemoteWindowDelegateSignal.StoppedWithError);
            Emit(oldSource, MacOSRemoteWindowDelegateSignal.Inactive);
            ThrowIfFailure(current);
            ThrowIfFailure(oldRegistration);
            Check(ReadGeneration(oldSource) == oldRegistration.Generation
                && currentNotifications == 0 && oldNotifications == 0 && !current.AdmissionClosed
                && current.Failure is null && oldRegistration.Failure is null && Failure is null,
                "native late old generation cannot target a replacement registration");
            Emit(currentSource, MacOSRemoteWindowDelegateSignal.Inactive);
            Emit(currentSource, MacOSRemoteWindowDelegateSignal.Active);
            Check(currentNotifications == 1 && current.AdmissionClosed,
                "exact replacement native inactive closes admission and active cannot resurrect it");
            Retire(current);
        }
        finally
        {
            if (currentSource != 0) ReleaseSourceOwner(currentSource);
            ReleaseSourceOwner(oldSource);
        }
    }

    private void Publish(nint source, MacOSRemoteWindowStreamDelegateRouter.Registration registration)
    {
        Check(registration.MarkDelegatePublished() && TryAssociate(source, registration.Generation)
            && registration.ConfirmAssociation(), "exact immutable numeric association publication");
    }

    private void VerifyHealthyGenerations()
    {
        long previousGeneration = 0;
        for (int index = 0; index < 64; index++)
        {
            Check(router.TryReserve(out var registration) && registration is not null
                && registration.Generation > previousGeneration, "healthy generations monotonically advance");
            previousGeneration = registration!.Generation;
            nint source = CreateSource();
            int deallocationsBefore = tagSuperclassDeallocations;
            int notifications = 0;
            try
            {
                Publish(source, registration);
                Check(registration.Activate(() => notifications++), "healthy generation activation");
                if (index % 8 == 0) ForceCollection();
                Emit(source, MacOSRemoteWindowDelegateSignal.Active);
                Emit(source, MacOSRemoteWindowDelegateSignal.Inactive);
                Emit(source, MacOSRemoteWindowDelegateSignal.StoppedWithError);
                ThrowIfFailure(registration);
                Check(notifications == 1 && registration.AdmissionClosed && registration.Failure is null
                    && Failure is null && ReadGeneration(source) == registration.Generation,
                    "repeated healthy native generations retain exact immutable routing and terminal-once delivery");
                Retire(registration);
            }
            finally
            {
                ReleaseSourceOwner(source);
            }

            Check(tagsInUse == 0 && tagSuperclassDeallocations == deallocationsBefore + 1,
                "each healthy source deallocation releases exactly its independent tag permit");
            Interlocked.Increment(ref healthyGenerations);
        }
    }

    private void VerifyIndependentTagBudget()
    {
        var retainedSources = new nint[Capacity];
        nint replacementSource = 0;
        int deallocationsBefore = tagSuperclassDeallocations;
        try
        {
            for (int index = 0; index < retainedSources.Length; index++)
            {
                Check(router.TryReserve(out var registration) && registration is not null,
                    "retired native tags do not consume completely cleaned managed permits");
                retainedSources[index] = CreateSource();
                Publish(retainedSources[index], registration!);
                Retire(registration!);
            }

            Check(tagsInUse == Capacity && maximumTagsInUse == Capacity
                && tagSuperclassDeallocations == deallocationsBefore,
                "sixteen live sources retain sixteen native tags after managed retirement");
            Check(router.TryReserve(out var replacement) && replacement is not null,
                "independent native tag budget can exhaust while managed Capture permit remains available");
            replacementSource = CreateSource();
            int allocationsBefore = tagsAllocated;
            Check(!TryAssociate(replacementSource, replacement!.Generation) && tagsInUse == Capacity
                && tagsAllocated == allocationsBefore
                && NativeAssociationInterop.Association(replacementSource, tagClass) == 0,
                "seventeenth native tag allocation is rejected before native allocation");
            Emit(replacementSource, MacOSRemoteWindowDelegateSignal.Inactive);
            ThrowIfFailure(replacement);
            Check(Failure is null && !replacement.AdmissionClosed,
                "exhausted untagged source has no authority to dispatch to reserved generation");
            nint released = retainedSources[0];
            retainedSources[0] = 0;
            ReleaseSourceOwner(released);
            Check(tagsInUse == Capacity - 1 && tagSuperclassDeallocations == deallocationsBefore + 1,
                "actual source deallocation and successful tag superclass dealloc return one native permit");
            Publish(replacementSource, replacement);
            int notifications = 0;
            Check(replacement.Activate(() => notifications++), "recovered native tag budget activation");
            ForceCollection();
            foreach (nint source in retainedSources)
            {
                if (source == 0) continue;
                Emit(source, MacOSRemoteWindowDelegateSignal.Active);
                Emit(source, MacOSRemoteWindowDelegateSignal.StoppedWithError);
                Emit(source, MacOSRemoteWindowDelegateSignal.Inactive);
            }

            ThrowIfFailure(replacement);
            Check(notifications == 0 && !replacement.AdmissionClosed && replacement.Failure is null
                && Failure is null && tagsInUse == Capacity,
                "retained retired native tags cannot route late callbacks to budget-reuse generation");
            Emit(replacementSource, MacOSRemoteWindowDelegateSignal.Active);
            Emit(replacementSource, MacOSRemoteWindowDelegateSignal.Inactive);
            Check(notifications == 1 && replacement.AdmissionClosed, "reused native tag permit routes only new exact generation");
            Retire(replacement);
        }
        finally
        {
            if (replacementSource != 0) ReleaseSourceOwner(replacementSource);
            foreach (nint source in retainedSources)
            {
                if (source != 0) ReleaseSourceOwner(source);
            }
        }

        Check(tagsInUse == 0 && tagSuperclassDeallocations == deallocationsBefore + Capacity + 1,
            "all retained native budget sources eventually complete actual tag superclass deallocation");
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
        Check(self == bridge && source != 0, "callback uses exact permanent bridge and valid task-owned source");
        nint pool = Native.PushAutoreleasePool();
        nint retainedSource = 0;
        nint retainedTag = 0;
        try
        {
            retainedSource = NativeAssociationInterop.Retain(source);
            Check(retainedSource == source, "callback acquires valid borrowed source plus-one");
            Interlocked.Increment(ref callbackSourceRetains);
            nint tag = NativeAssociationInterop.Association(retainedSource, tagClass);
            if (tag == 0)
            {
                // Phase 2a deliberately has no early initializer protocol.
                // Unknown identities are rejected, never guessed or retained as facts.
                Interlocked.Increment(ref unknownCallbacks);
                return;
            }
            retainedTag = NativeAssociationInterop.Retain(tag);
            Check(retainedTag == tag && NativeAssociationInterop.ObjectClass(retainedTag) == tagClass,
                "callback acquires exact numeric tag plus-one");
            Interlocked.Increment(ref callbackTagRetains);
            long generation = Marshal.ReadInt64(retainedTag, generationOffset);
            Check(generation > 0, "callback reads positive immutable generation");
            router.Dispatch(generation, signal);
            Check(ReadGeneration(retainedSource) == generation
                && Marshal.ReadInt64(retainedTag, generationOffset) == generation,
                "callback acquired native references stay valid through handler release reentry");
        }
        finally
        {
            try
            {
                if (retainedTag != 0)
                {
                    NativeAssociationInterop.Release(retainedTag);
                    Interlocked.Increment(ref callbackTagReleases);
                }
            }
            finally
            {
                try
                {
                    if (retainedSource != 0)
                    {
                        NativeAssociationInterop.Release(retainedSource);
                        Interlocked.Increment(ref callbackSourceReleases);
                    }
                }
                finally
                {
                    Native.PopAutoreleasePool(pool);
                }
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Stopped(nint self, nint selector, nint source, nint error) =>
        Reverse(self, source, MacOSRemoteWindowDelegateSignal.StoppedWithError);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void BecameActive(nint self, nint selector, nint source) =>
        Reverse(self, source, MacOSRemoteWindowDelegateSignal.Active);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void BecameInactive(nint self, nint selector, nint source) =>
        Reverse(self, source, MacOSRemoteWindowDelegateSignal.Inactive);

    private static void Reverse(nint self, nint source, MacOSRemoteWindowDelegateSignal signal)
    {
        NativeAssociationProbe? probe = Volatile.Read(ref processRoot);
        try
        {
            Check(probe is not null, "callback process root is permanent");
            probe!.Receive(self, source, signal);
        }
        catch (Exception exception)
        {
            probe?.RecordFailure(exception);
        }
    }

    private static void Retire(MacOSRemoteWindowStreamDelegateRouter.Registration registration)
    {
        Check(registration.RetireAsync().AsTask().GetAwaiter().GetResult()
            == MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
            "managed retirement joins admitted invocations without claiming native drain");
        Check(registration.Failure is null && registration.ConfirmCompleteCleanup(),
            "complete simulated Capture cleanup permits bounded registry reuse");
    }

    private void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Interlocked.Increment(ref forcedCollectionRounds);
    }

    private unsafe void InitializeTagClass()
    {
        foundationLibrary = NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");
        objectClass = Native.GetClass("NSObject");
        Check(objectClass != 0 && foundationLibrary != 0, "Foundation NSObject is available");
        tagClass = Native.AllocateClass(objectClass, "FlowspanAssociationTagProbe20261005", 0);
        Check(tagClass != 0, "numeric tag class allocation");
        Check(NativeAssociationInterop.AddIvar(tagClass, "generation", 8, 3, "q") != 0,
            "numeric-only tag ivar registration with log2 alignment three");
        deallocSelector = Native.Selector("dealloc");
        nint implementation = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&TagDealloc;
        Check(Native.AddMethod(tagClass, deallocSelector, implementation, "v16@0:8") != 0,
            "ordinary-arm64 tag dealloc registration");
        Native.RegisterClass(tagClass);
        Check(Native.GetClass("FlowspanAssociationTagProbe20261005") == tagClass
            && NativeAssociationInterop.Superclass(tagClass) == objectClass,
            "tag superclass is verified NSObject");
        nint method = Native.InstanceMethod(tagClass, deallocSelector);
        Check(method != 0 && Marshal.PtrToStringUTF8(Native.MethodEncoding(method)) == "v16@0:8"
            && NativeAssociationInterop.MethodImplementation(method) == implementation,
            "runtime tag dealloc implementation and encoding match");
        nint ivar = NativeAssociationInterop.InstanceVariable(tagClass, "generation");
        Check(ivar != 0 && Marshal.PtrToStringUTF8(NativeAssociationInterop.IvarEncoding(ivar)) == "q",
            "runtime tag ivar is signed 64-bit generation");
        Check(NativeAssociationInterop.TypeSizeAndAlignment("q", out nuint size, out nuint alignment) != 0
            && size == 8 && alignment == 8, "runtime numeric generation size and alignment");
        nint offset = NativeAssociationInterop.IvarOffset(ivar);
        nuint instanceSize = NativeAssociationInterop.InstanceSize(tagClass);
        Check(offset >= 0 && (nuint)offset >= NativeAssociationInterop.InstanceSize(objectClass)
            && (nuint)offset % alignment == 0 && (nuint)offset + size <= instanceSize,
            "runtime generation offset fits aligned subclass storage");
        generationOffset = checked((int)offset);
        Check(Marshal.SizeOf<NativeAssociationInterop.ObjectiveCSuper>() == 16
            && Marshal.OffsetOf<NativeAssociationInterop.ObjectiveCSuper>(nameof(NativeAssociationInterop.ObjectiveCSuper.Receiver)) == 0
            && Marshal.OffsetOf<NativeAssociationInterop.ObjectiveCSuper>(nameof(NativeAssociationInterop.ObjectiveCSuper.Superclass)) == 8,
            "ordinary-arm64 objc_super layout matches compiler shape evidence");
    }

    private void VerifyTagLifetime()
    {
        Check(router.TryReserve(out var registration) && registration is not null, "tracer Capture permit reservation");
        nint source = CreateSource();
        bool sourceOwned = true;
        try
        {
            Check(registration!.MarkDelegatePublished(), "tracer publication marked");
            Check(TryAssociate(source, registration.Generation), "immutable retained numeric association");
            Check(registration.ConfirmAssociation(), "tracer exact association confirmation");
            Check(registration.RetireAsync().AsTask().GetAwaiter().GetResult()
                == MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
                "tracer managed retirement is not native drain");
            Check(registration.ConfirmCompleteCleanup(), "tracer cleanup of simulated Capture owner");
            Check(tagsInUse == 1 && tagDeallocEntries == 0 && tagSuperclassDeallocations == 0,
                "tag budget stays charged after managed retirement while source lives");
            Check(ReadGeneration(source) == registration.Generation,
                "managed retirement leaves the immutable native association intact");
            sourceOwned = false;
            ReleaseSourceOwner(source);
            Check(tagsInUse == 0 && tagDeallocEntries == 1 && tagSuperclassDeallocations == 1,
                "source release returns tag budget only after actual superclass dealloc");
        }
        finally
        {
            if (sourceOwned) ReleaseSourceOwner(source);
        }
    }

    private bool TryAssociate(nint source, long generation)
    {
        Check(generation > 0 && NativeAssociationInterop.Association(source, tagClass) == 0,
            "association publication is immutable and generation is positive");
        int used = Volatile.Read(ref tagsInUse);
        while (true)
        {
            if (used >= Capacity) return false;
            int observed = Interlocked.CompareExchange(ref tagsInUse, used + 1, used);
            if (observed == used) break;
            used = observed;
        }

        UpdateMaximum(ref maximumTagsInUse, used + 1);
        bool charged = true;
        try
        {
            nint tag = Native.SendObject(tagClass, Native.Selector("new"));
            if (tag == 0)
            {
                // A successful nil return proves that no tag was allocated.
                Interlocked.Decrement(ref tagsInUse);
                charged = false;
                Check(false, "native tag allocation returned nil with confirmed absence");
            }

            Interlocked.Increment(ref tagsAllocated);
            Check(NativeAssociationInterop.ObjectClass(tag) == tagClass, "real numeric tag allocation exact class");
            Marshal.WriteInt64(tag, generationOffset, generation);
            NativeAssociationInterop.Associate(source, tagClass, tag, 1);
            Check(NativeAssociationInterop.Association(source, tagClass) == tag,
                "native association publication retains exact immutable numeric tag");
            NativeAssociationInterop.Release(tag);
            Interlocked.Increment(ref tagOwnersReleased);
            return true;
        }
        catch
        {
            // Unknown allocation/publication/release outcome is not absence.
            // Keep its independent permit charged; the failed probe cannot pass.
            if (charged) Interlocked.Increment(ref quarantinedTags);
            throw;
        }
    }

    private long ReadGeneration(nint source)
    {
        nint tag = NativeAssociationInterop.Association(source, tagClass);
        Check(tag != 0 && NativeAssociationInterop.ObjectClass(tag) == tagClass, "exact native numeric tag class");
        return Marshal.ReadInt64(tag, generationOffset);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void TagDealloc(nint self, nint selector)
    {
        NativeAssociationProbe? probe = Volatile.Read(ref processRoot);
        try
        {
            Check(probe is not null, "tag deallocation retains process root");
            long generation = Marshal.ReadInt64(self, probe!.generationOffset);
            Check(generation > 0 && selector == probe.deallocSelector, "tag dealloc saves only valid numeric fact");
            Interlocked.Increment(ref probe.tagDeallocEntries);
            var context = new NativeAssociationInterop.ObjectiveCSuper
            {
                Receiver = self,
                Superclass = probe.objectClass,
            };
            NativeAssociationInterop.SuperDealloc(ref context, selector);
            // The object is now gone: only process-rooted counters may be touched.
            Interlocked.Increment(ref probe.tagSuperclassDeallocations);
            Interlocked.Decrement(ref probe.tagsInUse);
        }
        catch (Exception exception)
        {
            probe?.RecordFailure(exception);
        }
    }

    private nint CreateSource()
    {
        nint source = Native.SendObject(objectClass, Native.Selector("new"));
        Check(source != 0, "plus-one task-owned NSObject source allocation");
        Interlocked.Increment(ref sourceOwnersAcquired);
        return source;
    }

    private void ReleaseSourceOwner(nint source)
    {
        NativeAssociationInterop.Release(source);
        Interlocked.Increment(ref sourceOwnersReleased);
    }

    private void RecordFailure(Exception exception)
    {
        Interlocked.Increment(ref containedFailures);
        Interlocked.CompareExchange(ref reverseFailure, exception, null);
        if (MacOSRemoteWindowFailure.FindFatal(exception) is { } fatal)
        {
            Interlocked.CompareExchange(ref fatalFailure, fatal, null);
        }
    }

    private void ThrowIfFailure(MacOSRemoteWindowStreamDelegateRouter.Registration? registration = null)
    {
        Exception? registrationFailure = registration?.Failure;
        Exception? failure = Volatile.Read(ref fatalFailure)
            ?? (registrationFailure is null ? null : MacOSRemoteWindowFailure.FindFatal(registrationFailure))
            ?? Failure ?? registrationFailure;
        if (failure is not null) ExceptionDispatchInfo.Throw(failure);
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        int observed = Volatile.Read(ref maximum);
        while (value > observed)
        {
            int previous = Interlocked.CompareExchange(ref maximum, value, observed);
            if (previous == observed) return;
            observed = previous;
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            Interlocked.CompareExchange(ref failedCheck, name, null);
            throw new InvalidOperationException(name);
        }
    }
}
