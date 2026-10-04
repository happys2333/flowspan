using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Flowspan.Platform.MacOS;

namespace Flowspan.MacOS.DelegateProbe;

// Separate process-rooted runtime for the explicit early mode. Sources are
// task-owned NSObject instances, never SCStream or AppKit objects.
internal sealed class NativeEarlyAssociationApi : IMacOSRemoteWindowStreamDelegateAssociationApi
{
    internal const int TagCapacity = 16;
    private static NativeEarlyAssociationApi? processRoot;
    private nint foundationLibrary;
    private nint objectClass;
    private nint tagClass;
    private nint deallocSelector;
    private int generationOffset;
    private Exception? failure;
    private OutOfMemoryException? fatalFailure;
    private int tagsInUse;
    private int maximumTagsInUse;
    private int tagsAllocated;
    private int tagOwnersReleased;
    private int quarantinedTags;
    private int tagDeallocEntries;
    private int tagSuperclassDeallocations;
    private int sourceOwnersAcquired;
    private int sourceOwnersReleased;
    private int sourceReferenceRetains;
    private int sourceReferenceReleases;
    private int tagReferenceRetains;
    private int tagReferenceReleases;
    private int associationReads;
    private int protocolReads;
    private int nilProtocolReads;
    private int containedFailures;

    internal nint ObjectClass => objectClass;
    internal int TagsInUse => Volatile.Read(ref tagsInUse);
    internal int MaximumTagsInUse => Volatile.Read(ref maximumTagsInUse);
    internal int TagsAllocated => Volatile.Read(ref tagsAllocated);
    internal int TagOwnersReleased => Volatile.Read(ref tagOwnersReleased);
    internal int QuarantinedTags => Volatile.Read(ref quarantinedTags);
    internal int TagDeallocEntries => Volatile.Read(ref tagDeallocEntries);
    internal int TagSuperclassDeallocations => Volatile.Read(ref tagSuperclassDeallocations);
    internal int SourceOwnersAcquired => Volatile.Read(ref sourceOwnersAcquired);
    internal int SourceOwnersReleased => Volatile.Read(ref sourceOwnersReleased);
    internal int SourceReferenceRetains => Volatile.Read(ref sourceReferenceRetains);
    internal int SourceReferenceReleases => Volatile.Read(ref sourceReferenceReleases);
    internal int TagReferenceRetains => Volatile.Read(ref tagReferenceRetains);
    internal int TagReferenceReleases => Volatile.Read(ref tagReferenceReleases);
    internal int AssociationReads => Volatile.Read(ref associationReads);
    internal int ProtocolReads => Volatile.Read(ref protocolReads);
    internal int NilProtocolReads => Volatile.Read(ref nilProtocolReads);
    internal int ContainedFailures => Volatile.Read(ref containedFailures);
    internal Exception? Failure => Volatile.Read(ref fatalFailure) ?? Volatile.Read(ref failure);
    internal Action<nint, nint>? AfterProtocolRead { get; set; }

    internal void Initialize()
    {
        NativeEarlyAssociationProbe.Check(Interlocked.CompareExchange(ref processRoot, this, null) is null,
            "one permanent early-mode native API root");
        InitializeTagClass();
    }

    private unsafe void InitializeTagClass()
    {
        foundationLibrary = NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");
        objectClass = Native.GetClass("NSObject");
        NativeEarlyAssociationProbe.Check(objectClass != 0 && foundationLibrary != 0, "early Foundation NSObject available");
        tagClass = Native.AllocateClass(objectClass, "FlowspanEarlyAssociationTagProbe20261005", 0);
        NativeEarlyAssociationProbe.Check(tagClass != 0
            && NativeAssociationInterop.AddIvar(tagClass, "generation", 8, 3, "q") != 0,
            "early numeric tag class and log2-three ivar registration");
        deallocSelector = Native.Selector("dealloc");
        nint implementation = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&TagDealloc;
        NativeEarlyAssociationProbe.Check(Native.AddMethod(tagClass, deallocSelector, implementation, "v16@0:8") != 0,
            "early ordinary-arm64 dealloc registration");
        Native.RegisterClass(tagClass);
        nint method = Native.InstanceMethod(tagClass, deallocSelector);
        nint ivar = NativeAssociationInterop.InstanceVariable(tagClass, "generation");
        NativeEarlyAssociationProbe.Check(Native.GetClass("FlowspanEarlyAssociationTagProbe20261005") == tagClass
            && NativeAssociationInterop.Superclass(tagClass) == objectClass && method != 0
            && Marshal.PtrToStringUTF8(Native.MethodEncoding(method)) == "v16@0:8"
            && NativeAssociationInterop.MethodImplementation(method) == implementation
            && ivar != 0 && Marshal.PtrToStringUTF8(NativeAssociationInterop.IvarEncoding(ivar)) == "q",
            "early runtime tag class superclass method and ivar metadata");
        NativeEarlyAssociationProbe.Check(NativeAssociationInterop.TypeSizeAndAlignment("q", out nuint size, out nuint alignment) != 0
            && size == 8 && alignment == 8, "early numeric type size alignment");
        nint offset = NativeAssociationInterop.IvarOffset(ivar);
        NativeEarlyAssociationProbe.Check(offset >= 0 && (nuint)offset >= NativeAssociationInterop.InstanceSize(objectClass)
            && (nuint)offset % alignment == 0 && (nuint)offset + size <= NativeAssociationInterop.InstanceSize(tagClass),
            "early runtime ivar offset fits numeric subclass storage");
        generationOffset = checked((int)offset);
        NativeEarlyAssociationProbe.Check(Marshal.SizeOf<NativeAssociationInterop.ObjectiveCSuper>() == 16
            && Marshal.OffsetOf<NativeAssociationInterop.ObjectiveCSuper>(nameof(NativeAssociationInterop.ObjectiveCSuper.Receiver)) == 0
            && Marshal.OffsetOf<NativeAssociationInterop.ObjectiveCSuper>(nameof(NativeAssociationInterop.ObjectiveCSuper.Superclass)) == 8,
            "early ordinary-arm64 objc_super shape");
    }

    internal nint CreateSource()
    {
        nint source = Native.SendObject(objectClass, Native.Selector("new"));
        NativeEarlyAssociationProbe.Check(source != 0, "early source plus-one NSObject allocation");
        Interlocked.Increment(ref sourceOwnersAcquired);
        return source;
    }

    internal void ReleaseSourceOwner(nint source)
    {
        try
        {
            NativeAssociationInterop.Release(source);
            Interlocked.Increment(ref sourceOwnersReleased);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    public void RetainSource(nint source)
    {
        NativeEarlyAssociationProbe.Check(source != 0 && NativeAssociationInterop.Retain(source) == source,
            "early valid borrowed source retained exactly");
        Interlocked.Increment(ref sourceReferenceRetains);
    }

    public nint ReadAndRetainTag(nint retainedSource)
    {
        nint tag = ReadAssociation(retainedSource);
        Interlocked.Increment(ref protocolReads);
        if (tag == 0)
        {
            Interlocked.Increment(ref nilProtocolReads);
        }
        else
        {
            NativeEarlyAssociationProbe.Check(NativeAssociationInterop.Retain(tag) == tag,
                "early association tag plus-one retained exactly");
            Interlocked.Increment(ref tagReferenceRetains);
            NativeEarlyAssociationProbe.Check(NativeAssociationInterop.ObjectClass(tag) == tagClass,
                "early retained association exact tag class");
        }

        // Deterministic interleaving only after the real association read.
        AfterProtocolRead?.Invoke(retainedSource, tag);
        return tag;
    }

    public long ReadGeneration(nint retainedTag)
    {
        NativeEarlyAssociationProbe.Check(retainedTag != 0 && NativeAssociationInterop.ObjectClass(retainedTag) == tagClass,
            "early generation reads validated retained tag");
        long generation = Marshal.ReadInt64(retainedTag, generationOffset);
        NativeEarlyAssociationProbe.Check(generation > 0, "early positive immutable numeric generation");
        return generation;
    }

    public void AssociateGeneration(nint retainedSource, long generation)
    {
        NativeEarlyAssociationProbe.Check(generation > 0 && ReadAssociation(retainedSource) == 0,
            "early association remains one-time immutable");
        int used = Volatile.Read(ref tagsInUse);
        while (true)
        {
            NativeEarlyAssociationProbe.Check(used < TagCapacity, "early independent native tag budget available");
            int previous = Interlocked.CompareExchange(ref tagsInUse, used + 1, used);
            if (previous == used) break;
            used = previous;
        }

        UpdateMaximum(ref maximumTagsInUse, used + 1);
        bool charged = true;
        try
        {
            nint tag = 0;
            bool ownerAcquired = false;
            Exception? cleanupFailure = null;
            try
            {
                tag = Native.SendObject(tagClass, Native.Selector("new"));
                ownerAcquired = tag != 0;
                if (tag == 0)
                {
                    Interlocked.Decrement(ref tagsInUse);
                    charged = false;
                    NativeEarlyAssociationProbe.Check(false, "early native tag nil allocation proves absence");
                }

                Interlocked.Increment(ref tagsAllocated);
                NativeEarlyAssociationProbe.Check(NativeAssociationInterop.ObjectClass(tag) == tagClass,
                    "early allocated tag exact numeric class");
                Marshal.WriteInt64(tag, generationOffset, generation);
                NativeAssociationInterop.Associate(retainedSource, tagClass, tag, 1);
                NativeEarlyAssociationProbe.Check(ReadAssociation(retainedSource) == tag,
                    "early actual retained association publication");
            }
            catch (Exception exception)
            {
                // Preserve the publication primary/fatal before independent
                // owner cleanup can throw a different exception.
                RecordFailure(exception);
                throw;
            }
            finally
            {
                // Exactly one cleanup attempt for a confirmed allocation +1,
                // even if publication verification fails. Never retry it.
                if (ownerAcquired)
                {
                    try
                    {
                        NativeAssociationInterop.Release(tag);
                        Interlocked.Increment(ref tagOwnersReleased);
                    }
                    catch (Exception exception)
                    {
                        RecordFailure(exception);
                        cleanupFailure = exception;
                    }
                }
            }

            if (cleanupFailure is not null) ExceptionDispatchInfo.Throw(cleanupFailure);
        }
        catch
        {
            if (charged) Interlocked.Increment(ref quarantinedTags);
            throw;
        }
    }

    public void ReleaseTag(nint retainedTag)
    {
        NativeAssociationInterop.Release(retainedTag);
        Interlocked.Increment(ref tagReferenceReleases);
    }

    public void ReleaseSource(nint retainedSource)
    {
        NativeAssociationInterop.Release(retainedSource);
        Interlocked.Increment(ref sourceReferenceReleases);
    }

    private nint ReadAssociation(nint source)
    {
        nint tag = NativeAssociationInterop.Association(source, tagClass);
        Interlocked.Increment(ref associationReads);
        return tag;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void TagDealloc(nint self, nint selector)
    {
        NativeEarlyAssociationApi? api = Volatile.Read(ref processRoot);
        try
        {
            NativeEarlyAssociationProbe.Check(api is not null, "early dealloc process root");
            nint pool = Native.PushAutoreleasePool();
            try
            {
                long generation = Marshal.ReadInt64(self, api!.generationOffset);
                NativeEarlyAssociationProbe.Check(generation > 0 && selector == api.deallocSelector,
                    "early dealloc saves numeric fact before superclass call");
                Interlocked.Increment(ref api.tagDeallocEntries);
                var context = new NativeAssociationInterop.ObjectiveCSuper { Receiver = self, Superclass = api.objectClass };
                NativeAssociationInterop.SuperDealloc(ref context, selector);
                // Never read self after successful superclass deallocation.
                Interlocked.Increment(ref api.tagSuperclassDeallocations);
                Interlocked.Decrement(ref api.tagsInUse);
            }
            catch (Exception exception)
            {
                api!.RecordFailure(exception);
            }
            finally
            {
                Native.PopAutoreleasePool(pool);
            }
        }
        catch (Exception exception)
        {
            api?.RecordFailure(exception);
        }
    }

    private void RecordFailure(Exception exception)
    {
        Interlocked.Increment(ref containedFailures);
        Interlocked.CompareExchange(ref failure, exception, null);
        if (MacOSRemoteWindowFailure.FindFatal(exception) is { } fatal)
            Interlocked.CompareExchange(ref fatalFailure, fatal, null);
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
}
