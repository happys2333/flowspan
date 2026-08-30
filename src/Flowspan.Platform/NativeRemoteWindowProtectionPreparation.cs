namespace Flowspan.Platform;

internal enum NativeRemoteWindowProtectionPreparationReservationStatus
{
    Reserved,
    ObservationChanged,
    ProtectionBlocked,
    SourceUnavailable,
    ReservationConflict,
}

// These callbacks run while the protection source mutation gate is held.
// Implementations must be bounded and non-blocking. They may only retain the
// registration or latch the owning host Preparation reservation; they must not
// call back into the source, wait, dispose owners, or invoke native/UI/wire work.
internal interface INativeRemoteWindowProtectionPreparationInvalidationSink
{
    public void OwnNativeRemoteWindowProtectionPreparationRegistration(
        INativeRemoteWindowProtectionPreparationRegistration registration);

    public void InvalidateNativeRemoteWindowProtectionPreparationNow();
}

// The source gate is acquired before either latch method. Implementations must
// not acquire the source gate in the reverse direction. Notify is always invoked
// after the source gate has been released and before ordinary Changed observers.
internal interface INativeRemoteWindowProtectionFormalSink
{
    public void InvalidateNativeRemoteWindowProtectionBeforeCaptureNow();

    // A null observation means the protection source was lost. Another source
    // thread may latch a newer observation before this latch's gate-out Notify
    // begins, so the sink must retain a monotonic queue or fail-closed coalesced
    // value; a single overwriteable callback argument is not sufficient.
    public void LatchNativeRemoteWindowProtectionObservationNow(
        NativeRemoteWindowProtectionObservation? observation);

    public void NotifyNativeRemoteWindowProtectionChanged();
}

internal interface IRemoteWindowCaptureStartAdmission
{
    // Called after lifecycle has become Starting and immediately before the
    // native capture boundary. Admission is synchronous and shares the source
    // mutation gate with TryPublish and source disposal.
    public bool TryAdmitCaptureStart(DateTimeOffset now);
}

internal interface INativeRemoteWindowProtectionPreparationRegistration :
    IDisposable,
    IRemoteWindowCaptureStartAdmission
{
    public long RegistrationId { get; }

    public bool IsCurrent { get; }

    public bool TryPromote(
        DateTimeOffset now,
        INativeRemoteWindowProtectionFormalSink formalSink);
}

internal sealed record NativeRemoteWindowProtectionPreparationReservationResult(
    NativeRemoteWindowProtectionPreparationReservationStatus Status,
    INativeRemoteWindowProtectionPreparationRegistration? Registration)
{
    public bool Reserved =>
        Status ==
            NativeRemoteWindowProtectionPreparationReservationStatus.Reserved
        && Registration?.IsCurrent == true;
}

internal interface INativeRemoteWindowProtectionPreparationBoundary
{
    public NativeRemoteWindowProtectionPreparationReservationResult
        TryReservePreparation(
            NativeRemoteWindowProtectionObservation expectedObservation,
            DateTimeOffset now,
            INativeRemoteWindowProtectionPreparationInvalidationSink
                invalidationSink);
}

public sealed partial class InMemoryNativeProtectionSource :
    INativeRemoteWindowProtectionPreparationBoundary
{
    private int activeProtectionFormalNotifications;
    private ProtectionPreparationRegistration? protectionPreparationRegistration;
    private bool protectionDisposalCleanupCommitted;
    private bool protectionDisposalFinalized;
    private Exception? protectionDisposalFailure;
    private long nextProtectionPreparationRegistrationId;

    NativeRemoteWindowProtectionPreparationReservationResult
        INativeRemoteWindowProtectionPreparationBoundary.TryReservePreparation(
            NativeRemoteWindowProtectionObservation expectedObservation,
            DateTimeOffset now,
            INativeRemoteWindowProtectionPreparationInvalidationSink
                invalidationSink)
    {
        ArgumentNullException.ThrowIfNull(expectedObservation);
        ArgumentNullException.ThrowIfNull(invalidationSink);

        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return new(
                    NativeRemoteWindowProtectionPreparationReservationStatus
                        .SourceUnavailable,
                    Registration: null);
            }

            if (latest is null
                || !IsExactProtectionObservation(latest, expectedObservation))
            {
                return new(
                    NativeRemoteWindowProtectionPreparationReservationStatus
                        .ObservationChanged,
                    Registration: null);
            }

            if (!IsFreshSafe(latest.Protection, now))
            {
                return new(
                    NativeRemoteWindowProtectionPreparationReservationStatus
                        .ProtectionBlocked,
                    Registration: null);
            }

            if (protectionPreparationRegistration is { IsActive: true })
            {
                return new(
                    NativeRemoteWindowProtectionPreparationReservationStatus
                        .ReservationConflict,
                    Registration: null);
            }

            var registration = new ProtectionPreparationRegistration(
                this,
                checked(++nextProtectionPreparationRegistrationId),
                expectedObservation,
                invalidationSink);
            protectionPreparationRegistration = registration;
            try
            {
                invalidationSink
                    .OwnNativeRemoteWindowProtectionPreparationRegistration(
                        registration);
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(
                        protectionPreparationRegistration,
                        registration))
                {
                    protectionPreparationRegistration = null;
                }

                _ = registration.Deactivate();
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(
                        FindOutOfMemoryException(exception) ?? exception)
                    .Throw();
                throw;
            }

            return new(
                NativeRemoteWindowProtectionPreparationReservationStatus
                    .Reserved,
                registration);
        }
    }

    private bool IsProtectionPreparationRegistrationCurrent(
        ProtectionPreparationRegistration registration)
    {
        lock (gate)
        {
            return Volatile.Read(ref disposed) == 0
                && registration.IsActive
                && ReferenceEquals(
                    protectionPreparationRegistration,
                    registration)
                && (registration.State ==
                        ProtectionPreparationRegistrationState.Live
                    || latest is not null
                    && IsExactProtectionObservation(
                        latest,
                        registration.ExpectedObservation));
        }
    }

    private bool TryPromoteProtectionPreparation(
        ProtectionPreparationRegistration registration,
        DateTimeOffset now,
        INativeRemoteWindowProtectionFormalSink formalSink)
    {
        Exception? failure = null;
        bool promoted = false;
        lock (gate)
        {
            if (Volatile.Read(ref disposed) == 0
                && ReferenceEquals(
                    protectionPreparationRegistration,
                    registration)
                && registration.State ==
                    ProtectionPreparationRegistrationState.Temporary
                && latest is not null
                && IsExactProtectionObservation(
                    latest,
                    registration.ExpectedObservation)
                && IsFreshSafe(latest.Protection, now))
            {
                registration.Promote(formalSink);
                promoted = true;
            }
            else if (ReferenceEquals(
                    protectionPreparationRegistration,
                    registration)
                && registration.State ==
                    ProtectionPreparationRegistrationState.Temporary)
            {
                failure = InvalidateTemporaryProtectionPreparationUnderGate();
            }
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(FindOutOfMemoryException(failure) ?? failure)
                .Throw();
        }

        return promoted;
    }

    private bool TryAdmitProtectionCaptureStart(
        ProtectionPreparationRegistration registration,
        DateTimeOffset now)
    {
        Exception? failure = null;
        bool admitted = false;
        lock (gate)
        {
            if (Volatile.Read(ref disposed) == 0
                && ReferenceEquals(
                    protectionPreparationRegistration,
                    registration)
                && registration.State ==
                    ProtectionPreparationRegistrationState.FormalPreStart
                && latest is not null
                && IsExactProtectionObservation(
                    latest,
                    registration.ExpectedObservation)
                && IsFreshSafe(latest.Protection, now))
            {
                registration.MarkLive();
                admitted = true;
            }
            else if (ReferenceEquals(
                    protectionPreparationRegistration,
                    registration)
                && registration.State ==
                    ProtectionPreparationRegistrationState.FormalPreStart)
            {
                protectionPreparationRegistration = null;
                ProtectionRegistrationCallbacks callbacks =
                    registration.Deactivate();
                if (callbacks.FormalSink is { } formalSink)
                {
                    try
                    {
                        formalSink
                            .InvalidateNativeRemoteWindowProtectionBeforeCaptureNow();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                }
            }
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(FindOutOfMemoryException(failure) ?? failure)
                .Throw();
        }

        return admitted;
    }

    private void UnregisterProtectionPreparation(
        ProtectionPreparationRegistration registration)
    {
        lock (gate)
        {
            if (ReferenceEquals(
                    protectionPreparationRegistration,
                    registration))
            {
                protectionPreparationRegistration = null;
            }

            _ = registration.Deactivate();
        }
    }

    private Exception? InvalidateTemporaryProtectionPreparationUnderGate()
    {
        ProtectionPreparationRegistration? registration =
            protectionPreparationRegistration;
        if (registration is null)
        {
            return null;
        }

        protectionPreparationRegistration = null;
        INativeRemoteWindowProtectionPreparationInvalidationSink? sink =
            registration.Deactivate().PreparationSink;
        if (sink is null)
        {
            return null;
        }

        try
        {
            sink.InvalidateNativeRemoteWindowProtectionPreparationNow();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private ProtectionMutationCallbacks
        CommitProtectionPreparationMutationUnderGate(
            NativeRemoteWindowProtectionObservation observation)
    {
        ProtectionPreparationRegistration? registration =
            protectionPreparationRegistration;
        if (registration is null)
        {
            return default;
        }

        switch (registration.State)
        {
            case ProtectionPreparationRegistrationState.Temporary:
                {
                    protectionPreparationRegistration = null;
                    INativeRemoteWindowProtectionPreparationInvalidationSink? sink =
                        registration.Deactivate().PreparationSink;
                    return new(
                        InvokePreparationInvalidation(sink),
                        FormalNotification: null);
                }
            case ProtectionPreparationRegistrationState.FormalPreStart:
                {
                    protectionPreparationRegistration = null;
                    INativeRemoteWindowProtectionFormalSink? sink =
                        registration.Deactivate().FormalSink;
                    return new(
                        InvokeFormalPreStartInvalidation(sink),
                        FormalNotification: null);
                }
            case ProtectionPreparationRegistrationState.Live:
                {
                    INativeRemoteWindowProtectionFormalSink? sink =
                        registration.FormalSink;
                    if (sink is null)
                    {
                        protectionPreparationRegistration = null;
                        _ = registration.Deactivate();
                        return default;
                    }

                    try
                    {
                        sink.LatchNativeRemoteWindowProtectionObservationNow(
                            observation);
                        return new(
                            Failure: null,
                            new(registration, sink));
                    }
                    catch (Exception exception)
                    {
                        protectionPreparationRegistration = null;
                        ProtectionRegistrationCallbacks callbacks =
                            registration.Deactivate();
                        return new(
                            exception,
                            new(registration, callbacks.FormalSink ?? sink));
                    }
                }
            default:
                protectionPreparationRegistration = null;
                _ = registration.Deactivate();
                return default;
        }
    }

    private ProtectionMutationCallbacks LoseProtectionPreparationUnderGate()
    {
        ProtectionPreparationRegistration? registration =
            protectionPreparationRegistration;
        if (registration is null)
        {
            return default;
        }

        protectionPreparationRegistration = null;
        ProtectionRegistrationCallbacks callbacks = registration.Deactivate();
        switch (callbacks.State)
        {
            case ProtectionPreparationRegistrationState.Temporary:
                return new(
                    InvokePreparationInvalidation(callbacks.PreparationSink),
                    FormalNotification: null);
            case ProtectionPreparationRegistrationState.FormalPreStart:
                return new(
                    InvokeFormalPreStartInvalidation(callbacks.FormalSink),
                    FormalNotification: null);
            case ProtectionPreparationRegistrationState.Live:
                if (callbacks.FormalSink is not { } formalSink)
                {
                    return default;
                }

                try
                {
                    formalSink
                        .LatchNativeRemoteWindowProtectionObservationNow(null);
                    return new(
                        Failure: null,
                        new(registration, formalSink));
                }
                catch (Exception exception)
                {
                    return new(
                        exception,
                        new(registration, callbacks.FormalSink ?? formalSink));
                }
            default:
                return default;
        }
    }

    private static Exception CombineProtectionFailures(
        Exception first,
        Exception second)
    {
        if (FindOutOfMemoryException(first) is { } firstFatal)
        {
            return firstFatal;
        }

        if (FindOutOfMemoryException(second) is { } secondFatal)
        {
            return secondFatal;
        }

        var failures = new List<Exception>();
        if (first is AggregateException firstAggregate)
        {
            failures.AddRange(firstAggregate.InnerExceptions);
        }
        else
        {
            failures.Add(first);
        }

        if (second is AggregateException secondAggregate)
        {
            failures.AddRange(secondAggregate.InnerExceptions);
        }
        else
        {
            failures.Add(second);
        }

        return new AggregateException(
            "One or more native Remote Window protection callbacks failed.",
            failures);
    }

    private static OutOfMemoryException? FindOutOfMemoryException(
        Exception? failure) => failure switch
        {
            OutOfMemoryException fatal => fatal,
            AggregateException aggregate => aggregate
                .Flatten()
                .InnerExceptions
                .OfType<OutOfMemoryException>()
                .FirstOrDefault(),
            _ => null,
        };

    private void BeginProtectionFormalNotificationUnderGate() =>
        activeProtectionFormalNotifications = checked(
            activeProtectionFormalNotifications + 1);

    private void CompleteProtectionFormalNotificationUnderGate(
        Exception? failure)
    {
        if (Volatile.Read(ref disposed) != 0 && failure is not null)
        {
            RecordProtectionDisposalFailureUnderGate(failure);
        }

        activeProtectionFormalNotifications--;
        TryFinalizeProtectionDisposalUnderGate();
        Monitor.PulseAll(gate);
    }

    private void RecordProtectionDisposalFailureUnderGate(Exception failure)
    {
        Exception normalized = FindOutOfMemoryException(failure) ?? failure;
        if (protectionDisposalFailure is null)
        {
            protectionDisposalFailure = normalized;
        }
        else if (!ReferenceEquals(protectionDisposalFailure, normalized))
        {
            protectionDisposalFailure = CombineProtectionFailures(
                protectionDisposalFailure,
                normalized);
        }
    }

    private void TryFinalizeProtectionDisposalUnderGate()
    {
        if (protectionDisposalCleanupCommitted
            && activeProtectionFormalNotifications == 0
            && !notificationDraining)
        {
            protectionDisposalFinalized = true;
            Monitor.PulseAll(gate);
        }
    }

    private static Exception? InvokePreparationInvalidation(
        INativeRemoteWindowProtectionPreparationInvalidationSink? sink)
    {
        if (sink is null)
        {
            return null;
        }

        try
        {
            sink.InvalidateNativeRemoteWindowProtectionPreparationNow();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Exception? InvokeFormalPreStartInvalidation(
        INativeRemoteWindowProtectionFormalSink? sink)
    {
        if (sink is null)
        {
            return null;
        }

        try
        {
            sink.InvalidateNativeRemoteWindowProtectionBeforeCaptureNow();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private void DeactivateLiveProtectionPreparationAfterNotificationFailure(
        ProtectionPreparationRegistration registration)
    {
        lock (gate)
        {
            if (ReferenceEquals(
                    protectionPreparationRegistration,
                    registration)
                && registration.State ==
                    ProtectionPreparationRegistrationState.Live)
            {
                protectionPreparationRegistration = null;
                _ = registration.Deactivate();
            }
        }
    }

    private static bool IsExactProtectionObservation(
        NativeRemoteWindowProtectionObservation current,
        NativeRemoteWindowProtectionObservation expected) =>
        current.OwnerGeneration == expected.OwnerGeneration
        && current.SessionGeneration == expected.SessionGeneration
        && current.SourceGeneration == expected.SourceGeneration
        && current.Revision == expected.Revision
        && current.Protection.Kind == expected.Protection.Kind
        && current.Protection.ObservedAt == expected.Protection.ObservedAt
        && string.Equals(
            current.Protection.Source,
            expected.Protection.Source,
            StringComparison.Ordinal);

    private static bool IsFreshSafe(
        ProtectionSnapshot snapshot,
        DateTimeOffset now) =>
        snapshot.Kind == ProtectionKind.Safe
        && (snapshot.ObservedAt <= now
            || snapshot.ObservedAt - now
                <= RemoteInputPolicy.MaximumFutureClockSkew)
        && (snapshot.ObservedAt >= now
            || now - snapshot.ObservedAt
                <= RemoteInputPolicy.MaximumProtectionAge);

    private sealed class ProtectionPreparationRegistration :
        INativeRemoteWindowProtectionPreparationRegistration
    {
        private InMemoryNativeProtectionSource? owner;
        private INativeRemoteWindowProtectionFormalSink? formalSink;
        private INativeRemoteWindowProtectionPreparationInvalidationSink? sink;
        private int state =
            (int)ProtectionPreparationRegistrationState.Temporary;

        internal ProtectionPreparationRegistration(
            InMemoryNativeProtectionSource owner,
            long registrationId,
            NativeRemoteWindowProtectionObservation expectedObservation,
            INativeRemoteWindowProtectionPreparationInvalidationSink sink)
        {
            this.owner = owner;
            RegistrationId = registrationId;
            ExpectedObservation = expectedObservation;
            this.sink = sink;
        }

        public long RegistrationId { get; }

        internal NativeRemoteWindowProtectionObservation ExpectedObservation
        { get; }

        public bool IsCurrent =>
            Volatile.Read(ref owner) is { } currentOwner
            && currentOwner.IsProtectionPreparationRegistrationCurrent(this);

        internal bool IsActive => State !=
            ProtectionPreparationRegistrationState.Inactive;

        internal INativeRemoteWindowProtectionFormalSink? FormalSink =>
            formalSink;

        internal ProtectionPreparationRegistrationState State =>
            (ProtectionPreparationRegistrationState)Volatile.Read(ref state);

        public bool TryPromote(
            DateTimeOffset now,
            INativeRemoteWindowProtectionFormalSink formalSink)
        {
            ArgumentNullException.ThrowIfNull(formalSink);
            return Volatile.Read(ref owner) is { } currentOwner
                && currentOwner.TryPromoteProtectionPreparation(
                    this,
                    now,
                    formalSink);
        }

        public bool TryAdmitCaptureStart(DateTimeOffset now) =>
            Volatile.Read(ref owner) is { } currentOwner
            && currentOwner.TryAdmitProtectionCaptureStart(this, now);

        internal void Promote(
            INativeRemoteWindowProtectionFormalSink promotedFormalSink)
        {
            formalSink = promotedFormalSink;
            sink = null;
            Volatile.Write(
                ref state,
                (int)ProtectionPreparationRegistrationState.FormalPreStart);
        }

        internal void MarkLive() => Volatile.Write(
            ref state,
            (int)ProtectionPreparationRegistrationState.Live);

        public void Dispose()
        {
            InMemoryNativeProtectionSource? currentOwner =
                Interlocked.Exchange(ref owner, null);
            if (currentOwner is null)
            {
                _ = Deactivate();
                return;
            }

            currentOwner.UnregisterProtectionPreparation(this);
        }

        internal ProtectionRegistrationCallbacks Deactivate()
        {
            ProtectionPreparationRegistrationState previous =
                (ProtectionPreparationRegistrationState)Interlocked.Exchange(
                    ref state,
                    (int)ProtectionPreparationRegistrationState.Inactive);
            if (previous == ProtectionPreparationRegistrationState.Inactive)
            {
                return default;
            }

            _ = Interlocked.Exchange(ref owner, null);
            return new(
                previous,
                Interlocked.Exchange(ref sink, null),
                Interlocked.Exchange(ref formalSink, null));
        }
    }

    private enum ProtectionPreparationRegistrationState
    {
        Inactive,
        Temporary,
        FormalPreStart,
        Live,
    }

    private readonly record struct ProtectionRegistrationCallbacks(
        ProtectionPreparationRegistrationState State,
        INativeRemoteWindowProtectionPreparationInvalidationSink?
            PreparationSink,
        INativeRemoteWindowProtectionFormalSink? FormalSink);

    private readonly record struct ProtectionFormalNotification(
        ProtectionPreparationRegistration Registration,
        INativeRemoteWindowProtectionFormalSink Sink);

    private readonly record struct ProtectionMutationCallbacks(
        Exception? Failure,
        ProtectionFormalNotification? FormalNotification);
}
