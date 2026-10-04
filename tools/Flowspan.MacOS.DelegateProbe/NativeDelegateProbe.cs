using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Flowspan.Platform.MacOS;

namespace Flowspan.MacOS.DelegateProbe;

// Synthetic NSObject messages only: no SCStream, AppKit, content or permission API.
internal sealed class NativeDelegateProbe
{
    private const int Capacity = 4;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private static NativeDelegateProbe? processRoot;
    private readonly MacOSRemoteWindowCallbackOwnerPool owners = new(Capacity);
    private readonly PublishedBridge?[] bridges = new PublishedBridge[Capacity];
    private int published;
    private int callbacks;
    private int activeReverseEntries;
    private int maximumReverseEntries;
    private Exception? reverseFailure;
    private nint bridgeClass;
    private nint stoppedSelector;
    private nint activeSelector;
    private nint inactiveSelector;
    private nint globalQueue;
    private nint foundationLibrary;

    internal static void Run()
    {
        var probe = new NativeDelegateProbe();
        Check(Interlocked.CompareExchange(ref processRoot, probe, null) is null,
            "one process-rooted native probe pool");
        probe.Initialize();
        nint pool = Native.PushAutoreleasePool();
        try
        {
            for (int index = 0; index < Capacity; index++)
            {
                probe.PublishBridge();
            }

            ForceCollection();
            probe.VerifyEarlyTerminal();
            probe.VerifyConcurrentBlockedRetirement();
            probe.VerifyLateTombstoneIsolation();
            probe.VerifyEarlyBindingMismatch();
            Check(!probe.owners.TryReserve(out var exhausted) && exhausted is null,
                "published tombstones consume the fixed pool budget");
            ForceCollection();
            for (int index = 0; index < probe.published; index++)
            {
                var bridge = probe.Bridge(index);
                probe.Emit(bridge, (nint)(1000 + index), MacOSRemoteWindowDelegateSignal.Active);
                Check(bridge.Owner.Failure is null && bridge.Owner.AdmissionClosed,
                    "retired owner remains a closed, fault-free tombstone");
            }

            Check(Volatile.Read(ref probe.reverseFailure) is null,
                "all reverse and GCD callbacks contained their faults");
            Check(Volatile.Read(ref probe.callbacks) >= 1
                && Volatile.Read(ref probe.maximumReverseEntries) >= 2,
                "actual native callbacks ran concurrently");
            Check(probe.published is >= 1 and <= 16 && probe.foundationLibrary != 0,
                "bounded bridges and native library remain process-rooted");
            Console.WriteLine($"delegate_probe=pass; mode=synthetic; method_signatures=3; native_callbacks={Volatile.Read(ref probe.callbacks)}; managed_invocations_exited=true; published_bridges={probe.published}; published_bridges_retained=true; capture_executed=false; SCStream_creations=0; AppKit_initialized=false; permissions_requested=0; pixel_reads=0");
        }
        finally
        {
            Native.PopAutoreleasePool(pool);
            // NSObject bridges have +1 ownership and immutable pool bindings.
            // Never release a published address, even if the probe fails.
            GC.KeepAlive(processRoot);
        }
    }

    private unsafe void Initialize()
    {
        foundationLibrary = NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");
        nint superclass = Native.GetClass("NSObject");
        Check(superclass != 0, "NSObject is available without AppKit");
        bridgeClass = Native.AllocateClass(superclass, "FlowspanSyntheticDelegateProbe20261004", 0);
        Check(bridgeClass != 0, "synthetic delegate bridge class allocation");
        stoppedSelector = Native.Selector("stream:didStopWithError:");
        activeSelector = Native.Selector("streamDidBecomeActive:");
        inactiveSelector = Native.Selector("streamDidBecomeInactive:");
        Check(Native.AddMethod(bridgeClass, stoppedSelector,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)&Stopped,
            "v32@0:8@16@24") != 0, "terminal method registration");
        Check(Native.AddMethod(bridgeClass, activeSelector,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&BecameActive,
            "v24@0:8@16") != 0, "active method registration");
        Check(Native.AddMethod(bridgeClass, inactiveSelector,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&BecameInactive,
            "v24@0:8@16") != 0, "inactive method registration");
        Native.RegisterClass(bridgeClass);
        VerifyMethod(stoppedSelector, "v32@0:8@16@24");
        VerifyMethod(activeSelector, "v24@0:8@16");
        VerifyMethod(inactiveSelector, "v24@0:8@16");
        globalQueue = Native.GlobalQueue(0, 0);
        Check(globalQueue != 0, "concurrent GCD queue is available");
    }

    private void VerifyMethod(nint selector, string encoding)
    {
        nint method = Native.InstanceMethod(bridgeClass, selector);
        Check(method != 0
            && Marshal.PtrToStringUTF8(Native.MethodEncoding(method)) == encoding,
            "native registered method encoding matches SDK declaration");
    }

    private void PublishBridge()
    {
        Check(owners.TryReserve(out var owner) && owner is not null, "fixed owner slot reservation");
        nint address = Native.SendObject(bridgeClass, Native.Selector("new"));
        Check(address != 0, "synthetic NSObject allocation");
        Check(owner!.TryPublishBridge(address), "immutable native self binding");
        bridges[published++] = new(owner, address);
    }

    private void VerifyEarlyTerminal()
    {
        var bridge = Bridge(0);
        int notifications = 0;
        DispatchOnGcd(bridge, 101, MacOSRemoteWindowDelegateSignal.Inactive)
            .WaitAsync(Deadline).GetAwaiter().GetResult();
        Check(bridge.Owner.AdmissionClosed && notifications == 0, "early terminal closes admission before handler installation");
        Check(bridge.Owner.BindStreamOnce(101), "early terminal exact stream binding");
        Check(!bridge.Owner.BindStreamOnce(101), "stream binding is one-time");
        Check(bridge.Owner.Activate(() => Interlocked.Increment(ref notifications)), "early terminal handler installation");
        Check(notifications == 1, "early terminal replays once outside its gate");
        Emit(bridge, 101, MacOSRemoteWindowDelegateSignal.Active);
        Check(bridge.Owner.AdmissionClosed && notifications == 1, "active observation never restores admission");
        VerifyRetired(bridge);
        Emit(bridge, 101, MacOSRemoteWindowDelegateSignal.StoppedWithError);
        Check(notifications == 1, "late tombstone ignores a terminal callback");
    }

    private void VerifyConcurrentBlockedRetirement()
    {
        var bridge = Bridge(1);
        Check(bridge.Owner.BindStreamOnce(202), "blocked owner exact stream binding");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        int notifications = 0;
        Check(bridge.Owner.Activate(() =>
        {
            Interlocked.Increment(ref notifications);
            var direct = bridge.Owner.RetireAsync();
            Check(direct.IsCompletedSuccessfully && direct.Result == MacOSRemoteWindowCallbackRetirement.SelfJoinRejected,
                "direct callback retirement rejects self-join");
            var descendant = Task.Run(() =>
            {
                var result = bridge.Owner.RetireAsync();
                return result.IsCompletedSuccessfully ? result.Result : (MacOSRemoteWindowCallbackRetirement?)null;
            }).WaitAsync(Deadline).GetAwaiter().GetResult();
            Check(descendant == MacOSRemoteWindowCallbackRetirement.SelfJoinRejected,
                "ExecutionContext descendant retirement rejects self-join");
            entered.TrySetResult();
            Check(release.Wait(Deadline), "blocked native callback handler was released");
        }), "blocked native handler installation");
        Task blocked = DispatchOnGcd(bridge, 202, MacOSRemoteWindowDelegateSignal.StoppedWithError);
        Task<MacOSRemoteWindowCallbackRetirement>? retirement = null;
        try
        {
            entered.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
            var concurrent = new Task[48];
            for (int index = 0; index < concurrent.Length; index++)
            {
                concurrent[index] = DispatchOnGcd(bridge, 202,
                    (MacOSRemoteWindowDelegateSignal)(index % 3));
            }

            Task.WhenAll(concurrent).WaitAsync(Deadline).GetAwaiter().GetResult();
            Check(notifications == 1 && Volatile.Read(ref maximumReverseEntries) >= 2,
                "concurrent native callbacks return while one handler is blocked");
            retirement = bridge.Owner.RetireAsync().AsTask();
            Check(bridge.Owner.AdmissionClosed && !retirement.IsCompleted,
                "retirement closes admission before waiting for admitted handler exit");
        }
        finally
        {
            release.Set();
            blocked.WaitAsync(Deadline).GetAwaiter().GetResult();
            if (retirement is not null)
            {
                Check(retirement.WaitAsync(Deadline).GetAwaiter().GetResult()
                    == MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
                    "retirement joins admitted managed invocations only");
            }
        }

        Check(bridge.Owner.Failure is null && notifications == 1, "native terminal without sample notifies once and preserves no fault");
        VerifyRetired(bridge);
    }

    private void VerifyLateTombstoneIsolation()
    {
        var retired = Bridge(1);
        var current = Bridge(2);
        int notifications = 0;
        Check(!current.Owner.TryPublishBridge(retired.Address), "published self address cannot be overwritten");
        Check(current.Owner.BindStreamOnce(303), "replacement owner exact stream binding");
        Check(current.Owner.Activate(() => Interlocked.Increment(ref notifications)), "replacement handler installation");
        Emit(retired, 303, MacOSRemoteWindowDelegateSignal.Inactive);
        Emit(retired, -1, MacOSRemoteWindowDelegateSignal.StoppedWithError);
        Emit(current, 999, MacOSRemoteWindowDelegateSignal.StoppedWithError);
        Check(!current.Owner.AdmissionClosed && notifications == 0, "late and wrong-stream callbacks do not affect replacement owner");
        Emit(current, 303, MacOSRemoteWindowDelegateSignal.Inactive);
        Check(current.Owner.AdmissionClosed && notifications == 1, "exact replacement callback closes admission");
        VerifyRetired(current);
    }

    private void VerifyEarlyBindingMismatch()
    {
        var bridge = Bridge(3);
        DispatchOnGcd(bridge, 404, MacOSRemoteWindowDelegateSignal.StoppedWithError)
            .WaitAsync(Deadline).GetAwaiter().GetResult();
        Check(!bridge.Owner.BindStreamOnce(405) && !bridge.Owner.Activate(() =>
            throw new InvalidOperationException("Mismatched stream must never install a handler.")),
            "early stream mismatch retires the published owner");
        VerifyRetired(bridge);
    }

    private static void VerifyRetired(PublishedBridge bridge)
    {
        Check(bridge.Owner.RetireAsync().AsTask().WaitAsync(Deadline).GetAwaiter().GetResult()
            == MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
            "retirement reports managed invocation exits, not native drain");
        Check(bridge.Owner.Failure is null, "retirement does not hide callback faults");
    }

    private PublishedBridge Bridge(int index) => bridges[index]
        ?? throw new InvalidOperationException("Published native bridge is absent.");

    private void Emit(PublishedBridge bridge, nint stream, MacOSRemoteWindowDelegateSignal signal)
    {
        switch (signal)
        {
            case MacOSRemoteWindowDelegateSignal.StoppedWithError:
                Native.SendTerminal(bridge.Address, stoppedSelector, stream, -1);
                break;
            case MacOSRemoteWindowDelegateSignal.Inactive:
                Native.SendObservation(bridge.Address, inactiveSelector, stream);
                break;
            case MacOSRemoteWindowDelegateSignal.Active:
                Native.SendObservation(bridge.Address, activeSelector, stream);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(signal));
        }
    }

    private unsafe Task DispatchOnGcd(PublishedBridge bridge, nint stream, MacOSRemoteWindowDelegateSignal signal)
    {
        var work = new GcdWork(this, bridge, stream, signal);
        var root = GCHandle.Alloc(work);
        try
        {
            Native.DispatchAsync(globalQueue, GCHandle.ToIntPtr(root),
                (nint)(delegate* unmanaged[Cdecl]<nint, void>)&RunGcdWork);
        }
        catch
        {
            root.Free();
            throw;
        }

        return work.Completion.Task;
    }

    private void Receive(nint self, nint stream, MacOSRemoteWindowDelegateSignal signal)
    {
        Interlocked.Increment(ref callbacks);
        int active = Interlocked.Increment(ref activeReverseEntries);
        int previous = Volatile.Read(ref maximumReverseEntries);
        while (active > previous)
        {
            int observed = Interlocked.CompareExchange(ref maximumReverseEntries, active, previous);
            if (observed == previous) break;
            previous = observed;
        }

        try
        {
            // Compare only; never message or dereference stream/error pointers.
            owners.Dispatch(self, stream, signal);
        }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(ref reverseFailure, exception, null);
        }
        finally
        {
            Interlocked.Decrement(ref activeReverseEntries);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Stopped(nint self, nint selector, nint stream, nint error) =>
        Reverse(self, stream, MacOSRemoteWindowDelegateSignal.StoppedWithError);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void BecameActive(nint self, nint selector, nint stream) =>
        Reverse(self, stream, MacOSRemoteWindowDelegateSignal.Active);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void BecameInactive(nint self, nint selector, nint stream) =>
        Reverse(self, stream, MacOSRemoteWindowDelegateSignal.Inactive);

    private static void Reverse(nint self, nint stream, MacOSRemoteWindowDelegateSignal signal)
    {
        try { Volatile.Read(ref processRoot)?.Receive(self, stream, signal); }
        catch (Exception exception)
        {
            var probe = Volatile.Read(ref processRoot);
            if (probe is not null) Interlocked.CompareExchange(ref probe.reverseFailure, exception, null);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void RunGcdWork(nint context)
    {
        GcdWork? work = null;
        try
        {
            work = GCHandle.FromIntPtr(context).Target as GcdWork;
            Check(work is not null, "native GCD context remains rooted");
            work!.Probe.Emit(work.Bridge, work.Stream, work.Signal);
        }
        catch (Exception exception)
        {
            var probe = Volatile.Read(ref processRoot);
            if (probe is not null) Interlocked.CompareExchange(ref probe.reverseFailure, exception, null);
        }
        finally
        {
            try
            {
                GCHandle.FromIntPtr(context).Free();
                work?.Completion.TrySetResult();
            }
            catch (Exception exception)
            {
                var probe = Volatile.Read(ref processRoot);
                if (probe is not null) Interlocked.CompareExchange(ref probe.reverseFailure, exception, null);
            }
        }
    }

    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }

    private sealed record PublishedBridge(MacOSRemoteWindowCallbackOwner Owner, nint Address);

    private sealed record GcdWork(NativeDelegateProbe Probe, PublishedBridge Bridge,
        nint Stream, MacOSRemoteWindowDelegateSignal Signal)
    {
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
