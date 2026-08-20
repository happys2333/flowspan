namespace Flowspan.Platform;

public enum NativeRemoteWindowFrameSinkFault
{
    SourceBindingLost,
    DeliveryPolicyUnavailable,
    DestinationFailed,
}

public sealed class BoundedNativeRemoteWindowFrameSink :
    INativeRemoteWindowFrameSink,
    IDisposable
{
    public const int MaximumPendingFrames = 1;

    private static readonly object SinkDeliveryActivityOwner = new();

    private readonly object gate = new();
    private Func<bool>? canDeliver;
    private int deliveryDrainWaiterCount;
    private INativeRemoteWindowFrameSink? destination;
    private bool delivering;
    private Func<IDisposable?>? enterDelivery;
    private NativeRemoteWindowSourceUse? expectedSourceUse;
    private Action<NativeRemoteWindowFrameSinkFault>? faulted;
    private long highestAcceptedSequence;
    private Func<bool>? isCurrent;
    private NativeRemoteWindowFrame? pending;
    private bool closed;

    public BoundedNativeRemoteWindowFrameSink(
        NativeRemoteWindowSourceUse expectedSourceUse,
        Func<bool> isCurrent,
        INativeRemoteWindowFrameSink destination) : this(
            expectedSourceUse,
            isCurrent,
            static () => true,
            destination,
            enterDelivery: null,
            faulted: null)
    {
    }

    public BoundedNativeRemoteWindowFrameSink(
        NativeRemoteWindowSourceUse expectedSourceUse,
        Func<bool> isCurrent,
        Func<bool> canDeliver,
        INativeRemoteWindowFrameSink destination,
        Action<NativeRemoteWindowFrameSinkFault> faulted) : this(
            expectedSourceUse,
            isCurrent,
            canDeliver,
            destination,
            enterDelivery: null,
            faulted: faulted)
    {
    }

    internal BoundedNativeRemoteWindowFrameSink(
        NativeRemoteWindowSourceUse expectedSourceUse,
        Func<bool> isCurrent,
        Func<bool> canDeliver,
        INativeRemoteWindowFrameSink destination,
        Func<IDisposable?>? enterDelivery,
        Action<NativeRemoteWindowFrameSinkFault>? faulted)
    {
        this.expectedSourceUse = expectedSourceUse
            ?? throw new ArgumentNullException(nameof(expectedSourceUse));
        this.isCurrent = isCurrent
            ?? throw new ArgumentNullException(nameof(isCurrent));
        this.canDeliver = canDeliver
            ?? throw new ArgumentNullException(nameof(canDeliver));
        this.destination = destination
            ?? throw new ArgumentNullException(nameof(destination));
        this.enterDelivery = enterDelivery;
        this.faulted = faulted;
    }

    public bool IsClosed
    {
        get
        {
            lock (gate)
            {
                return closed;
            }
        }
    }

    internal int DeliveryDrainWaiterCount
    {
        get
        {
            lock (gate)
            {
                return deliveryDrainWaiterCount;
            }
        }
    }

    public void TakeOwnership(
        NativeRemoteWindowSourceUse sourceUse,
        NativeRemoteWindowFrame frame)
    {
        ArgumentNullException.ThrowIfNull(sourceUse);
        ArgumentNullException.ThrowIfNull(frame);

        IDisposable? deliveryOperation = null;
        if (enterDelivery is not null)
        {
            try
            {
                deliveryOperation = enterDelivery();
            }
            catch (Exception)
            {
            }

            if (deliveryOperation is null)
            {
                CloseNow();
                DisposeFrame(frame);
                return;
            }
        }

        try
        {
            if (!ReadCurrent())
            {
                CloseForFault(NativeRemoteWindowFrameSinkFault.SourceBindingLost);
                DisposeFrame(frame);
                return;
            }

            bool deliveryAllowed;
            try
            {
                deliveryAllowed = ReadDeliveryAllowed();
            }
            catch (Exception)
            {
                CloseForFault(
                    NativeRemoteWindowFrameSinkFault.DeliveryPolicyUnavailable);
                DisposeFrame(frame);
                return;
            }

            NativeRemoteWindowFrame? rejectedOrReplaced = null;
            bool startDelivery = false;
            lock (gate)
            {
                if (closed
                    || expectedSourceUse is null
                    || !expectedSourceUse.MatchesExactly(sourceUse)
                    || !expectedSourceUse.Matches(frame)
                    || frame.Sequence <= highestAcceptedSequence)
                {
                    rejectedOrReplaced = frame;
                }
                else
                {
                    highestAcceptedSequence = frame.Sequence;
                    if (!deliveryAllowed)
                    {
                        rejectedOrReplaced = frame;
                    }
                    else if (delivering)
                    {
                        rejectedOrReplaced = pending;
                        pending = frame;
                    }
                    else
                    {
                        delivering = true;
                        startDelivery = true;
                    }
                }
            }

            DisposeFrame(rejectedOrReplaced);
            if (startDelivery)
            {
                Deliver(frame);
            }
        }
        finally
        {
            deliveryOperation?.Dispose();
        }
    }

    public void CloseNow()
    {
        NativeRemoteWindowFrame? frameToDispose;
        lock (gate)
        {
            closed = true;
            frameToDispose = pending;
            pending = null;
            expectedSourceUse = null;
            isCurrent = null;
            canDeliver = null;
            destination = null;
            enterDelivery = null;
            faulted = null;
        }

        DisposeFrame(frameToDispose);
    }

    public void Dispose()
    {
        CloseNow();
        WaitForDeliveryDrain();
    }

    internal bool TryCloseAndConfirmDrained()
    {
        CloseNow();
        lock (gate)
        {
            return !delivering;
        }
    }

    public override string ToString() => IsClosed
        ? "Bounded native Remote Window frame sink (closed)"
        : "Bounded native Remote Window frame sink (open)";

    private void Deliver(NativeRemoteWindowFrame frame)
    {
        NativeRemoteWindowFrame current = frame;
        while (true)
        {
            var deliveryToken = new object();
            NativeRemoteWindowDrainActivityScope deliveryScope =
                NativeRemoteWindowDrainActivityScope.Enter(
                    SinkDeliveryActivityOwner,
                    deliveryToken);
            try
            {
                if (!ReadCurrent())
                {
                    DisposeFrame(current);
                    CloseForFault(
                        NativeRemoteWindowFrameSinkFault.SourceBindingLost);
                    CompleteDelivery();
                    return;
                }

                bool deliveryAllowed;
                try
                {
                    deliveryAllowed = ReadDeliveryAllowed();
                }
                catch (Exception)
                {
                    DisposeFrame(current);
                    CloseForFault(
                        NativeRemoteWindowFrameSinkFault
                            .DeliveryPolicyUnavailable);
                    CompleteDelivery();
                    return;
                }

                if (!deliveryAllowed)
                {
                    DisposeFrame(current);
                    CompleteDeliveryAndDiscardPending();
                    return;
                }

                NativeRemoteWindowSourceUse? expected = null;
                INativeRemoteWindowFrameSink? currentDestination = null;
                bool rejectClosed;
                lock (gate)
                {
                    rejectClosed = closed
                        || expectedSourceUse is null
                        || destination is null;
                    if (!rejectClosed)
                    {
                        expected = expectedSourceUse;
                        currentDestination = destination;
                    }
                }

                if (rejectClosed)
                {
                    DisposeFrame(current);
                    CompleteDelivery();
                    return;
                }

                try
                {
                    currentDestination!.TakeOwnership(expected!, current);
                }
                catch (Exception)
                {
                    DisposeFrame(current);
                    CloseForFault(
                        NativeRemoteWindowFrameSinkFault.DestinationFailed);
                    CompleteDelivery();
                    return;
                }

                NativeRemoteWindowFrame? next;
                bool deliveryClosed;
                lock (gate)
                {
                    deliveryClosed = closed;
                    next = pending;
                    pending = null;
                    if (deliveryClosed || next is null)
                    {
                        delivering = false;
                        Monitor.PulseAll(gate);
                    }
                }

                if (deliveryClosed)
                {
                    DisposeFrame(next);
                    return;
                }

                if (next is null)
                {
                    return;
                }

                current = next;
            }
            finally
            {
                deliveryScope.Dispose();
            }
        }
    }

    private bool ReadCurrent()
    {
        Func<bool>? currentPredicate;
        lock (gate)
        {
            if (closed)
            {
                return false;
            }

            currentPredicate = isCurrent;
        }

        try
        {
            return currentPredicate?.Invoke() == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool ReadDeliveryAllowed()
    {
        Func<bool>? deliveryPredicate;
        lock (gate)
        {
            if (closed)
            {
                return false;
            }

            deliveryPredicate = canDeliver;
        }

        return deliveryPredicate?.Invoke() == true;
    }

    private void CloseForFault(NativeRemoteWindowFrameSinkFault fault)
    {
        NativeRemoteWindowFrame? frameToDispose;
        Action<NativeRemoteWindowFrameSinkFault>? callback;
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            closed = true;
            frameToDispose = pending;
            pending = null;
            expectedSourceUse = null;
            isCurrent = null;
            canDeliver = null;
            destination = null;
            enterDelivery = null;
            callback = faulted;
            faulted = null;
        }

        DisposeFrame(frameToDispose);
        try
        {
            callback?.Invoke(fault);
        }
        catch (Exception)
        {
        }
    }

    private void WaitForDeliveryDrain()
    {
        lock (gate)
        {
            while (delivering)
            {
                if (NativeRemoteWindowDrainActivityScope.IsActiveForOwner(
                        SinkDeliveryActivityOwner))
                {
                    return;
                }

                deliveryDrainWaiterCount++;
                try
                {
                    Monitor.Wait(gate);
                }
                finally
                {
                    deliveryDrainWaiterCount--;
                }
            }
        }
    }

    private void CompleteDelivery()
    {
        lock (gate)
        {
            delivering = false;
            Monitor.PulseAll(gate);
        }
    }

    private void CompleteDeliveryAndDiscardPending()
    {
        NativeRemoteWindowFrame? frameToDispose;
        lock (gate)
        {
            frameToDispose = pending;
            pending = null;
            delivering = false;
            Monitor.PulseAll(gate);
        }

        DisposeFrame(frameToDispose);
    }

    private static void DisposeFrame(NativeRemoteWindowFrame? frame)
    {
        try
        {
            frame?.Dispose();
        }
        catch (Exception)
        {
        }
    }

}
