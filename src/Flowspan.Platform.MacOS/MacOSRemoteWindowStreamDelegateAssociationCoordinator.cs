namespace Flowspan.Platform.MacOS;

// Success confirms exactly one acquired/released reference. A thrown operation
// leaves its ownership effect unconfirmed; never retry that reference.
// Native implementations must validate class/layout and immutable numeric tags.
internal interface IMacOSRemoteWindowStreamDelegateAssociationApi
{
    public void RetainSource(nint source);
    public nint ReadAndRetainTag(nint retainedSource);
    public long ReadGeneration(nint retainedTag);
    public void AssociateGeneration(nint retainedSource, long generation);
    public void ReleaseTag(nint retainedTag);
    public void ReleaseSource(nint retainedSource);
}

// Portable system-boundary contract, not an actual Foundation/SCStream proof.
internal sealed class MacOSRemoteWindowStreamDelegateAssociationCoordinator(
    IMacOSRemoteWindowStreamDelegateAssociationApi native,
    MacOSRemoteWindowStreamDelegateRouter router)
{
    private readonly IMacOSRemoteWindowStreamDelegateAssociationApi native = native;
    private readonly MacOSRemoteWindowStreamDelegateRouter router = router;
    private readonly object gate = new();
    private readonly ReferenceOwnership?[] ownership = new ReferenceOwnership[16];
    private readonly Initializer?[] captures = new Initializer[16];
    private Initializer? currentInitializer;
    private bool constructionPoisoned;
    private bool nativeAdmissionClosed;
    private Exception? failure;
    private OutOfMemoryException? fatalFailure;

    internal Exception? Failure => Volatile.Read(ref fatalFailure) ?? Volatile.Read(ref failure);
    // Closes new native work, not previously admitted calls or known cleanup.
    // Future Capture composition must consume this independently of any exact
    // SourceUnavailable notification; an unknown source is never misattributed.
    internal bool NativeAdmissionClosed { get { lock (gate) { return nativeAdmissionClosed; } } }
    internal int ChargedOwnershipCount { get { lock (gate) { return ownership.Count(record => record is not null); } } }
    internal int UncertainOwnershipCount
    {
        get
        {
            lock (gate)
            {
                return ownership.Count(record => record is { SourceUncertain: true } or { TagUncertain: true });
            }
        }
    }

    internal bool TryBegin(out Initializer? initializer)
    {
        lock (gate)
        {
            int slot = Array.IndexOf(captures, null);
            if (slot < 0 || nativeAdmissionClosed || constructionPoisoned || !router.TryReserve(out var registration)
                || registration is null)
            {
                initializer = null;
                return false;
            }

            initializer = new Initializer(this, registration);
            captures[slot] = initializer;
            currentInitializer = initializer;
            return true;
        }
    }

    internal void Dispatch(nint borrowedSource, MacOSRemoteWindowDelegateSignal signal)
    {
        try
        {
            DispatchCore(borrowedSource, signal);
        }
        catch (Exception exception)
        {
            RecordNativeFault(exception);
        }
    }

    private void DispatchCore(nint borrowedSource, MacOSRemoteWindowDelegateSignal signal)
    {
        if (signal is not (MacOSRemoteWindowDelegateSignal.Inactive or MacOSRemoteWindowDelegateSignal.StoppedWithError))
        {
            return;
        }

        ReferenceOwnership? record = ReserveOwnership(borrowedSource, null);
        if (record is null) { return; }
        bool transferred = false;
        try
        {
            if (!RetainSource(record) || !ReadTag(record)) { return; }
            if (record.Tag != 0) { Route(record, signal); return; }

            Initializer? snapshot;
            lock (gate)
            {
                snapshot = currentInitializer is { Published: true } ? currentInitializer : null;
            }

            if (!ReadTag(record)) { return; }
            if (record.Tag != 0) { Route(record, signal); return; }
            if (snapshot is null) { return; }

            EarlyRecord result;
            lock (gate)
            {
                if (nativeAdmissionClosed || constructionPoisoned || !ReferenceEquals(currentInitializer, snapshot))
                {
                    result = EarlyRecord.Lost;
                }
                else if (snapshot.Pending is null)
                {
                    record.Owner = snapshot;
                    record.Signal = signal;
                    snapshot.Pending = record;
                    result = EarlyRecord.Recorded;
                    transferred = true;
                }
                else
                {
                    result = snapshot.Pending.Source == borrowedSource ? EarlyRecord.Coalesced : EarlyRecord.Distinct;
                }
            }

            if (result is EarlyRecord.Recorded or EarlyRecord.Coalesced) { return; }
            if (result == EarlyRecord.Distinct) { PoisonAmbiguity(snapshot); return; }

            // Exact token lost to publication/clear. Never snapshot B.
            if (ReadTag(record))
            {
                if (record.Tag != 0) { Route(record, signal); }
                else { PoisonAmbiguity(snapshot); }
            }
        }
        finally
        {
            if (!transferred) { ReturnOwnership(record); }
        }
    }

    private ReferenceOwnership? ReserveOwnership(nint source, Initializer? owner)
    {
        lock (gate)
        {
            if (nativeAdmissionClosed) { return null; }
            int slot = Array.IndexOf(ownership, null);
            if (slot >= 0 && source != 0)
            {
                var record = new ReferenceOwnership(source, owner);
                ownership[slot] = record;
                return record;
            }
        }

        RecordNativeFault(new InvalidOperationException(source == 0
            ? "A callback source was not a valid borrowed reference."
            : "The independent native-reference ownership budget is exhausted."));
        return null;
    }

    private bool AdmitNativeWork()
    {
        lock (gate) { return !nativeAdmissionClosed; }
    }

    private bool RetainSource(ReferenceOwnership record)
    {
        if (!AdmitNativeWork()) { return false; }
        record.SourceUncertain = true;
        try
        {
            native.RetainSource(record.Source);
            record.SourceOwned = true;
            record.SourceUncertain = false;
            return true;
        }
        catch (Exception exception)
        {
            RecordNativeFault(exception);
            return false;
        }
    }

    private bool ReadTag(ReferenceOwnership record)
    {
        if (!AdmitNativeWork()) { return false; }
        record.TagUncertain = true;
        try
        {
            record.Tag = native.ReadAndRetainTag(record.Source);
            record.TagOwned = record.Tag != 0;
            record.TagUncertain = false;
            return true;
        }
        catch (Exception exception)
        {
            RecordNativeFault(exception);
            return false;
        }
    }

    private void Route(ReferenceOwnership record, MacOSRemoteWindowDelegateSignal signal)
    {
        if (!ReadGeneration(record, out long generation)) { return; }
        lock (gate)
        {
            record.Owner = captures.FirstOrDefault(candidate => candidate?.Generation == generation);
            if (nativeAdmissionClosed) { return; }
        }

        router.Dispatch(generation, signal);
    }

    private bool ReadGeneration(ReferenceOwnership record, out long generation)
    {
        generation = 0;
        if (!AdmitNativeWork()) { return false; }
        try
        {
            generation = native.ReadGeneration(record.Tag);
            if (generation <= 0)
            {
                throw new InvalidOperationException("The retained native tag did not contain a positive generation.");
            }
        }
        catch (Exception exception)
        {
            RecordNativeFault(exception);
            return false;
        }

        return true;
    }

    private void ReturnOwnership(ReferenceOwnership record)
    {
        // Confirmed independent refs get one cleanup attempt even after fault.
        // Uncertain acquisitions/releases are never retried.
        try
        {
            if (record.TagOwned && !record.TagReleaseAttempted)
            {
                record.TagReleaseAttempted = true;
                record.TagUncertain = true;
                try
                {
                    native.ReleaseTag(record.Tag);
                    record.TagOwned = false;
                    record.TagUncertain = false;
                }
                catch (Exception exception) { RecordNativeFault(exception); }
            }
        }
        finally
        {
            if (record.SourceOwned && !record.SourceReleaseAttempted)
            {
                record.SourceReleaseAttempted = true;
                record.SourceUncertain = true;
                try
                {
                    native.ReleaseSource(record.Source);
                    record.SourceOwned = false;
                    record.SourceUncertain = false;
                }
                catch (Exception exception) { RecordNativeFault(exception); }
            }

            lock (gate)
            {
                if (!record.SourceOwned && !record.SourceUncertain && !record.TagOwned && !record.TagUncertain)
                {
                    int slot = Array.IndexOf(ownership, record);
                    if (slot >= 0) { ownership[slot] = null; }
                }
            }
        }
    }

    private void RecordNativeFault(Exception exception)
    {
        lock (gate)
        {
            failure ??= exception;
            fatalFailure ??= MacOSRemoteWindowFailure.FindFatal(exception);
            nativeAdmissionClosed = true;
            constructionPoisoned = true;
            currentInitializer = null;
            foreach (Initializer? capture in captures)
            {
                if (capture is not null) { capture.Quarantined = true; }
            }
        }

        // No terminal notification is fabricated for a guessed Capture.
        router.CloseAdmissionForNativeFault();
        for (int index = 0; index < captures.Length; index++)
        {
            ReferenceOwnership? pending;
            lock (gate) { pending = captures[index]?.TakePending(); }
            if (pending is not null) { ReturnOwnership(pending); }
        }
    }

    private void PoisonAmbiguity(Initializer snapshot)
    {
        Initializer? replacement;
        ReferenceOwnership? firstPending;
        ReferenceOwnership? secondPending = null;
        lock (gate)
        {
            constructionPoisoned = true;
            replacement = currentInitializer;
            currentInitializer = null;
            snapshot.Quarantined = true;
            firstPending = snapshot.TakePending();
            if (replacement is not null && !ReferenceEquals(replacement, snapshot))
            {
                replacement.Quarantined = true;
                secondPending = replacement.TakePending();
            }
        }

        router.PoisonConstruction();
        snapshot.Registration.Quarantine();
        replacement?.Registration.Quarantine();
        if (firstPending is not null) { ReturnOwnership(firstPending); }
        if (secondPending is not null) { ReturnOwnership(secondPending); }
    }

    internal sealed class Initializer(
        MacOSRemoteWindowStreamDelegateAssociationCoordinator coordinator,
        MacOSRemoteWindowStreamDelegateRouter.Registration registration)
    {
        internal MacOSRemoteWindowStreamDelegateRouter.Registration Registration { get; } = registration;
        internal bool Published { get; private set; }
        internal bool Quarantined { get; set; }
        internal bool Completing { get; private set; }
        internal ReferenceOwnership? Pending { get; set; }
        internal long Generation => Registration.Generation;
        internal Exception? Failure => Registration.Failure;
        internal bool AdmissionClosed => Registration.AdmissionClosed;

        internal bool MarkDelegatePublished()
        {
            lock (coordinator.gate)
            {
                if (coordinator.nativeAdmissionClosed || coordinator.constructionPoisoned
                    || !ReferenceEquals(coordinator.currentInitializer, this) || !Registration.MarkDelegatePublished())
                {
                    return false;
                }

                Published = true;
                return true;
            }
        }

        internal bool CompleteAssociation(nint source)
        {
            lock (coordinator.gate)
            {
                if (coordinator.nativeAdmissionClosed || coordinator.constructionPoisoned || !Published || Completing
                    || !ReferenceEquals(coordinator.currentInitializer, this)) { return false; }
                Completing = true;
            }

            ReferenceOwnership? record = null;
            ReferenceOwnership? pending = null;
            try
            {
                record = coordinator.ReserveOwnership(source, this);
                if (record is null || !coordinator.RetainSource(record)) { return false; }
                bool mismatch;
                lock (coordinator.gate)
                {
                    mismatch = Pending is not null && Pending.Source != source;
                }

                if (mismatch) { coordinator.PoisonAmbiguity(this); return false; }
                if (!coordinator.AdmitNativeWork()) { return false; }
                coordinator.native.AssociateGeneration(source, Generation);
                if (!coordinator.ReadTag(record)) { return false; }
                if (record.Tag == 0 || !coordinator.ReadGeneration(record, out long generation) || generation != Generation)
                {
                    if (!coordinator.NativeAdmissionClosed)
                    {
                        coordinator.RecordNativeFault(new InvalidOperationException("Native publication did not prove the exact immutable generation."));
                    }

                    return false;
                }

                bool confirmed;
                lock (coordinator.gate)
                {
                    mismatch = Pending is not null && Pending.Source != source;
                    confirmed = !mismatch && !coordinator.nativeAdmissionClosed && !coordinator.constructionPoisoned
                        && ReferenceEquals(coordinator.currentInitializer, this) && Registration.ConfirmAssociation();
                    if (confirmed)
                    {
                        coordinator.currentInitializer = null;
                        pending = TakePending();
                    }
                }

                if (!confirmed) { coordinator.PoisonAmbiguity(this); return false; }
                if (pending is not null) { coordinator.router.Dispatch(Generation, pending.Signal); }
                return true;
            }
            catch (Exception exception)
            {
                coordinator.RecordNativeFault(exception);
                return false;
            }
            finally
            {
                if (pending is not null) { coordinator.ReturnOwnership(pending); }
                if (record is not null) { coordinator.ReturnOwnership(record); }
                lock (coordinator.gate) { Completing = false; }
            }
        }

        internal bool RejectInitialization()
        {
            ReferenceOwnership? pending;
            lock (coordinator.gate)
            {
                if (!ReferenceEquals(coordinator.currentInitializer, this) || !Registration.FailInitialization()) { return false; }
                coordinator.currentInitializer = null;
                coordinator.constructionPoisoned |= Published;
                pending = TakePending();
            }

            if (pending is not null) { coordinator.ReturnOwnership(pending); }
            return true;
        }

        internal ValueTask<MacOSRemoteWindowCallbackRetirement> RetireAsync()
        {
            RejectInitialization();
            return Registration.RetireAsync();
        }

        internal bool ConfirmCompleteCleanup()
        {
            lock (coordinator.gate)
            {
                if (Quarantined || coordinator.nativeAdmissionClosed || Pending is not null
                    || Completing
                    || coordinator.ownership.Any(record => ReferenceEquals(record?.Owner, this))
                    || !Registration.ConfirmCompleteCleanup()) { return false; }
                int slot = Array.IndexOf(coordinator.captures, this);
                if (slot >= 0) { coordinator.captures[slot] = null; }
                return true;
            }
        }

        internal bool Activate(Action unavailable) => Registration.Activate(unavailable);
        // State/ownership transfer only; caller holds coordinator gate.
        internal ReferenceOwnership? TakePending()
        {
            ReferenceOwnership? pending = Pending;
            Pending = null;
            return pending;
        }
    }

    internal sealed class ReferenceOwnership(nint source, Initializer? owner)
    {
        internal nint Source { get; } = source;
        internal nint Tag { get; set; }
        internal Initializer? Owner { get; set; } = owner;
        internal MacOSRemoteWindowDelegateSignal Signal { get; set; }
        internal bool SourceOwned { get; set; }
        internal bool SourceUncertain { get; set; }
        internal bool SourceReleaseAttempted { get; set; }
        internal bool TagOwned { get; set; }
        internal bool TagUncertain { get; set; }
        internal bool TagReleaseAttempted { get; set; }
    }

    private enum EarlyRecord { Recorded, Coalesced, Distinct, Lost }
}
