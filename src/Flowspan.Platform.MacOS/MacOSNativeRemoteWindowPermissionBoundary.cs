using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Flowspan.Domain;

namespace Flowspan.Platform.MacOS;

public interface IMacOSScreenCapturePermissionInterop
{
    public bool IsSupported { get; }

    public bool PreflightScreenCaptureAccess();

    public bool RequestScreenCaptureAccess();
}

public sealed class MacOSNativeRemoteWindowPermissionBoundary :
    INativeRemoteWindowPermissionBoundary,
    INativeRemoteWindowPermissionPreparationBoundary
{
    private readonly object gate = new();
    private readonly IMacOSScreenCapturePermissionInterop interop;
    private readonly long ownerGeneration;
    private readonly Dictionary<long, PermissionPreparationRegistration>
        preparationRegistrations = [];
    private Action<NativeRemoteWindowPermissionSnapshot>? changed;
    private long committedOperationSequence;
    private NativeRemoteWindowPermissionSnapshot current;
    private Exception? disposalFailure;
    private bool disposed;
    private NativeRemoteWindowPermissionState lastConclusiveCapture =
        NativeRemoteWindowPermissionState.NotDetermined;
    private long nextOperationSequence;
    private long nextPreparationRegistrationId;

    public MacOSNativeRemoteWindowPermissionBoundary()
        : this(new CoreGraphicsScreenCapturePermissionInterop())
    {
    }

    public MacOSNativeRemoteWindowPermissionBoundary(
        IMacOSScreenCapturePermissionInterop interop,
        long ownerGeneration = 1)
    {
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerGeneration, 1);
        this.interop = interop;
        this.ownerGeneration = ownerGeneration;
        current = NativeRemoteWindowPermissionSnapshot.Create(
            NativeRemoteWindowPermissionState.NotDetermined,
            NativeRemoteWindowPermissionState.Unsupported,
            ownerGeneration,
            revision: 0);
    }

    public event Action<NativeRemoteWindowPermissionSnapshot>? Changed
    {
        add
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                changed += value;
            }
        }
        remove
        {
            lock (gate)
            {
                changed -= value;
            }
        }
    }

    public NativeRemoteWindowPermissionSnapshot GetSnapshot()
    {
        long operationSequence = BeginOperation();
        CapturePermissionObservation observation;
        try
        {
            observation = !interop.IsSupported
                ? CapturePermissionObservation.Unsupported
                : interop.PreflightScreenCaptureAccess()
                    ? CapturePermissionObservation.PreflightGranted
                    : CapturePermissionObservation.PreflightAbsent;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            observation = CapturePermissionObservation.Unavailable;
        }

        return CommitObservation(operationSequence, observation);
    }

    public ValueTask<NativeRemoteWindowPermissionSnapshot>
        RequestCapturePermissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long operationSequence = BeginOperation();
        CapturePermissionObservation observation;
        try
        {
            observation = !interop.IsSupported
                ? CapturePermissionObservation.Unsupported
                : interop.RequestScreenCaptureAccess()
                    ? CapturePermissionObservation.ExplicitGranted
                    : CapturePermissionObservation.ExplicitDenied;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            observation = CapturePermissionObservation.Unavailable;
        }

        return ValueTask.FromResult(
            CommitObservation(operationSequence, observation));
    }

    public ValueTask<NativeRemoteWindowPermissionSnapshot>
        RequestInputPermissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return ValueTask.FromResult(current);
        }
    }

    public ValueTask DisposeAsync()
    {
        Exception? failure;
        lock (gate)
        {
            if (disposed)
            {
                failure = disposalFailure;
            }
            else
            {
                disposed = true;
                changed = null;
                try
                {
                    disposalFailure = InvalidatePreparationsUnderGate();
                }
                catch (Exception exception)
                {
                    disposalFailure = exception;
                }

                failure = disposalFailure;
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return ValueTask.CompletedTask;
    }

    NativeRemoteWindowPermissionPreparationReservationResult
        INativeRemoteWindowPermissionPreparationBoundary.TryReservePreparation(
            NativeRemoteWindowPermissionSnapshot expectedSnapshot,
            MirrorParticipantRole frozenRole,
            INativeRemoteWindowPermissionPreparationInvalidationSink
                invalidationSink)
    {
        ArgumentNullException.ThrowIfNull(expectedSnapshot);
        ArgumentNullException.ThrowIfNull(invalidationSink);
        if (!Enum.IsDefined(frozenRole))
        {
            throw new ArgumentOutOfRangeException(nameof(frozenRole));
        }

        lock (gate)
        {
            if (disposed)
            {
                return new(
                    NativeRemoteWindowPermissionPreparationReservationStatus
                        .BoundaryUnavailable,
                    Registration: null);
            }

            if (!IsExactSnapshot(current, expectedSnapshot))
            {
                return new(
                    NativeRemoteWindowPermissionPreparationReservationStatus
                        .SnapshotChanged,
                    Registration: null);
            }

            NativeRemoteWindowPermissionPreparationReservationStatus?
                rejectionStatus = GetPermissionRejectionStatus(
                    current,
                    frozenRole);
            if (rejectionStatus.HasValue)
            {
                return new(
                    rejectionStatus.Value,
                    Registration: null);
            }

            long registrationId = checked(++nextPreparationRegistrationId);
            var registration = new PermissionPreparationRegistration(
                this,
                registrationId,
                invalidationSink);
            preparationRegistrations.Add(registrationId, registration);
            try
            {
                invalidationSink
                    .OwnNativeRemoteWindowPermissionPreparationRegistration(
                        registration);
            }
            catch
            {
                preparationRegistrations.Remove(registrationId);
                _ = registration.Deactivate();
                throw;
            }

            return new(
                NativeRemoteWindowPermissionPreparationReservationStatus
                    .Reserved,
                registration);
        }
    }

    private long BeginOperation()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return checked(++nextOperationSequence);
        }
    }

    private NativeRemoteWindowPermissionSnapshot CommitObservation(
        long operationSequence,
        CapturePermissionObservation observation)
    {
        Action<NativeRemoteWindowPermissionSnapshot>[] observers = [];
        Exception? preparationFailure = null;
        NativeRemoteWindowPermissionSnapshot snapshot;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (operationSequence <= committedOperationSequence)
            {
                return current;
            }

            committedOperationSequence = operationSequence;
            NativeRemoteWindowPermissionState capture = observation switch
            {
                CapturePermissionObservation.PreflightGranted or
                    CapturePermissionObservation.ExplicitGranted =>
                    NativeRemoteWindowPermissionState.Granted,
                CapturePermissionObservation.PreflightAbsent =>
                    MapAbsentPreflight(),
                CapturePermissionObservation.ExplicitDenied =>
                    NativeRemoteWindowPermissionState.Denied,
                CapturePermissionObservation.Unsupported =>
                    NativeRemoteWindowPermissionState.Unsupported,
                _ => NativeRemoteWindowPermissionState.Unavailable,
            };
            if (current.Capture == capture)
            {
                return current;
            }

            if (capture is not NativeRemoteWindowPermissionState.Unavailable
                and not NativeRemoteWindowPermissionState.Unsupported)
            {
                lastConclusiveCapture = capture;
            }

            current = NativeRemoteWindowPermissionSnapshot.Create(
                capture,
                NativeRemoteWindowPermissionState.Unsupported,
                ownerGeneration,
                checked(current.Revision + 1));
            snapshot = current;
            preparationFailure = InvalidatePreparationsUnderGate();
            observers = changed?.GetInvocationList()
                .Cast<Action<NativeRemoteWindowPermissionSnapshot>>()
                .ToArray() ?? [];
        }

        foreach (Action<NativeRemoteWindowPermissionSnapshot> observer in observers)
        {
            try
            {
                observer(snapshot);
            }
            catch (Exception)
            {
            }
        }

        if (preparationFailure is not null)
        {
            ExceptionDispatchInfo.Capture(preparationFailure).Throw();
        }

        return snapshot;
    }

    private Exception? InvalidatePreparationsUnderGate()
    {
        PermissionPreparationRegistration[] registrations =
            preparationRegistrations
                .OrderBy(static entry => entry.Key)
                .Select(static entry => entry.Value)
                .ToArray();
        preparationRegistrations.Clear();
        var sinks = new List<
            INativeRemoteWindowPermissionPreparationInvalidationSink>(
                registrations.Length);
        foreach (PermissionPreparationRegistration registration in registrations)
        {
            INativeRemoteWindowPermissionPreparationInvalidationSink? sink =
                registration.Deactivate();
            if (sink is not null)
            {
                sinks.Add(sink);
            }
        }

        var failures = new List<Exception>();
        foreach (
            INativeRemoteWindowPermissionPreparationInvalidationSink sink in sinks)
        {
            try
            {
                sink.InvalidateNativeRemoteWindowPermissionPreparationNow();
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException)
            {
                failures.Add(exception);
            }
        }

        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException(
                "One or more native Remote Window permission Preparation reservations failed to invalidate.",
                failures),
        };
    }

    private NativeRemoteWindowPermissionState MapAbsentPreflight() =>
        lastConclusiveCapture switch
        {
            NativeRemoteWindowPermissionState.NotDetermined =>
                NativeRemoteWindowPermissionState.NotDetermined,
            NativeRemoteWindowPermissionState.Granted =>
                NativeRemoteWindowPermissionState.Revoked,
            NativeRemoteWindowPermissionState.Revoked =>
                NativeRemoteWindowPermissionState.Revoked,
            _ => NativeRemoteWindowPermissionState.Denied,
        };

    private static bool IsExactSnapshot(
        NativeRemoteWindowPermissionSnapshot current,
        NativeRemoteWindowPermissionSnapshot expected) =>
        current.OwnerGeneration == expected.OwnerGeneration
        && current.Revision == expected.Revision
        && current.Capture == expected.Capture
        && current.Input == expected.Input;

    private static
        NativeRemoteWindowPermissionPreparationReservationStatus?
        GetPermissionRejectionStatus(
            NativeRemoteWindowPermissionSnapshot snapshot,
            MirrorParticipantRole role)
    {
        if (snapshot.Capture != NativeRemoteWindowPermissionState.Granted)
        {
            return IsUnavailable(snapshot.Capture)
                ? NativeRemoteWindowPermissionPreparationReservationStatus
                    .BoundaryUnavailable
                : NativeRemoteWindowPermissionPreparationReservationStatus
                    .PermissionDenied;
        }

        if (role == MirrorParticipantRole.DriverEligible
            && snapshot.Input != NativeRemoteWindowPermissionState.Granted)
        {
            return IsUnavailable(snapshot.Input)
                ? NativeRemoteWindowPermissionPreparationReservationStatus
                    .BoundaryUnavailable
                : NativeRemoteWindowPermissionPreparationReservationStatus
                    .PermissionDenied;
        }

        return null;
    }

    private static bool IsUnavailable(
        NativeRemoteWindowPermissionState state) =>
        state is NativeRemoteWindowPermissionState.Unsupported
            or NativeRemoteWindowPermissionState.Unavailable;

    private bool IsPreparationRegistrationCurrent(
        PermissionPreparationRegistration registration)
    {
        lock (gate)
        {
            return !disposed
                && registration.IsActive
                && preparationRegistrations.TryGetValue(
                    registration.RegistrationId,
                    out PermissionPreparationRegistration? currentRegistration)
                && ReferenceEquals(currentRegistration, registration);
        }
    }

    private void UnregisterPreparation(
        PermissionPreparationRegistration registration)
    {
        lock (gate)
        {
            if (preparationRegistrations.TryGetValue(
                    registration.RegistrationId,
                    out PermissionPreparationRegistration? currentRegistration)
                && ReferenceEquals(currentRegistration, registration))
            {
                preparationRegistrations.Remove(registration.RegistrationId);
            }

            registration.Deactivate();
        }
    }

    private sealed class PermissionPreparationRegistration(
        MacOSNativeRemoteWindowPermissionBoundary boundary,
        long registrationId,
        INativeRemoteWindowPermissionPreparationInvalidationSink
            invalidationSink) :
        INativeRemoteWindowPermissionPreparationRegistration
    {
        private MacOSNativeRemoteWindowPermissionBoundary? owner = boundary;
        private INativeRemoteWindowPermissionPreparationInvalidationSink? sink =
            invalidationSink;
        private int active = 1;

        public long RegistrationId { get; } = registrationId;

        public bool IsCurrent =>
            Volatile.Read(ref owner) is { } currentOwner
            && currentOwner.IsPreparationRegistrationCurrent(this);

        public bool IsActive => Volatile.Read(ref active) != 0;

        public void Dispose()
        {
            MacOSNativeRemoteWindowPermissionBoundary? currentOwner =
                Interlocked.Exchange(ref owner, null);
            if (currentOwner is null)
            {
                _ = Deactivate();
                return;
            }

            currentOwner.UnregisterPreparation(this);
        }

        public INativeRemoteWindowPermissionPreparationInvalidationSink?
            Deactivate()
        {
            if (Interlocked.Exchange(ref active, 0) == 0)
            {
                return null;
            }

            _ = Interlocked.Exchange(ref owner, null);
            return Interlocked.Exchange(ref sink, null);
        }
    }

    private enum CapturePermissionObservation
    {
        PreflightGranted,
        PreflightAbsent,
        ExplicitGranted,
        ExplicitDenied,
        Unsupported,
        Unavailable,
    }
}

internal sealed partial class CoreGraphicsScreenCapturePermissionInterop :
    IMacOSScreenCapturePermissionInterop
{
    private const string CoreGraphicsFramework =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    public bool IsSupported =>
        OperatingSystem.IsMacOSVersionAtLeast(10, 15);

    public bool PreflightScreenCaptureAccess() =>
        CGPreflightScreenCaptureAccess();

    public bool RequestScreenCaptureAccess() =>
        CGRequestScreenCaptureAccess();

    [LibraryImport(CoreGraphicsFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool CGPreflightScreenCaptureAccess();

    [LibraryImport(CoreGraphicsFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool CGRequestScreenCaptureAccess();
}
