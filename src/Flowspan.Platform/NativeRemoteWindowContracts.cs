using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Text.Json.Serialization;

namespace Flowspan.Platform;

public enum NativeRemoteWindowPermissionState
{
    NotDetermined,
    Granted,
    Denied,
    Revoked,
    Unsupported,
    Unavailable,
}

public sealed record NativeRemoteWindowPermissionSnapshot
{
    private NativeRemoteWindowPermissionSnapshot(
        NativeRemoteWindowPermissionState capture,
        NativeRemoteWindowPermissionState input,
        long ownerGeneration,
        long revision)
    {
        Capture = capture;
        Input = input;
        OwnerGeneration = ownerGeneration;
        Revision = revision;
    }

    public NativeRemoteWindowPermissionState Capture { get; }

    public NativeRemoteWindowPermissionState Input { get; }

    public long OwnerGeneration { get; }

    public long Revision { get; }

    public static NativeRemoteWindowPermissionSnapshot Create(
        NativeRemoteWindowPermissionState capture,
        NativeRemoteWindowPermissionState input,
        long ownerGeneration,
        long revision)
    {
        if (!Enum.IsDefined(capture))
        {
            throw new ArgumentOutOfRangeException(nameof(capture));
        }

        if (!Enum.IsDefined(input))
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(ownerGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        return new NativeRemoteWindowPermissionSnapshot(
            capture,
            input,
            ownerGeneration,
            revision);
    }

    public override string ToString() =>
        $"Native Remote Window permissions (capture {Capture}, input {Input}, owner {OwnerGeneration}, revision {Revision})";
}

public interface INativeRemoteWindowPermissionBoundary : IAsyncDisposable
{
    public event Action<NativeRemoteWindowPermissionSnapshot>? Changed;

    public NativeRemoteWindowPermissionSnapshot GetSnapshot();

    public ValueTask<NativeRemoteWindowPermissionSnapshot>
        RequestCapturePermissionAsync(CancellationToken cancellationToken);

    public ValueTask<NativeRemoteWindowPermissionSnapshot>
        RequestInputPermissionAsync(CancellationToken cancellationToken);
}

public sealed class UnavailableNativeRemoteWindowPermissionBoundary :
    INativeRemoteWindowPermissionBoundary,
    INativeRemoteWindowPermissionPreparationBoundary
{
    private static readonly NativeRemoteWindowPermissionSnapshot Snapshot =
        NativeRemoteWindowPermissionSnapshot.Create(
            NativeRemoteWindowPermissionState.Unsupported,
            NativeRemoteWindowPermissionState.Unsupported,
            ownerGeneration: 1,
            revision: 0);

    private UnavailableNativeRemoteWindowPermissionBoundary()
    {
    }

    public static UnavailableNativeRemoteWindowPermissionBoundary Instance { get; } =
        new();

    public event Action<NativeRemoteWindowPermissionSnapshot>? Changed
    {
        add { }
        remove { }
    }

    public NativeRemoteWindowPermissionSnapshot GetSnapshot() => Snapshot;

    public ValueTask<NativeRemoteWindowPermissionSnapshot>
        RequestCapturePermissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Snapshot);
    }

    public ValueTask<NativeRemoteWindowPermissionSnapshot>
        RequestInputPermissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Snapshot);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    NativeRemoteWindowPermissionPreparationReservationResult
        INativeRemoteWindowPermissionPreparationBoundary.TryReservePreparation(
            NativeRemoteWindowPermissionSnapshot expectedSnapshot,
            Flowspan.Domain.MirrorParticipantRole frozenRole,
            INativeRemoteWindowPermissionPreparationInvalidationSink
                invalidationSink)
    {
        ArgumentNullException.ThrowIfNull(expectedSnapshot);
        ArgumentNullException.ThrowIfNull(invalidationSink);
        if (!Enum.IsDefined(frozenRole))
        {
            throw new ArgumentOutOfRangeException(nameof(frozenRole));
        }

        return new(
            NativeRemoteWindowPermissionPreparationReservationStatus
                .BoundaryUnavailable,
            Registration: null);
    }
}

public enum NativeRemoteWindowPixelFormat
{
    Bgra8888,
}

public sealed class NativeRemoteWindowFrame : IDisposable
{
    public const int MaximumDimension = 16_384;
    public const int MaximumPayloadBytes = 64 * 1024 * 1024;

    private readonly IMemoryOwner<byte> owner;
    private readonly int payloadLength;
    private int disposed;

    private NativeRemoteWindowFrame(
        IMemoryOwner<byte> owner,
        int payloadLength,
        int width,
        int height,
        int stride,
        NativeRemoteWindowPixelFormat pixelFormat,
        long ownerGeneration,
        long sessionGeneration,
        long sourceGeneration,
        long geometryRevision,
        long sequence)
    {
        this.owner = owner;
        this.payloadLength = payloadLength;
        Width = width;
        Height = height;
        Stride = stride;
        PixelFormat = pixelFormat;
        OwnerGeneration = ownerGeneration;
        SessionGeneration = sessionGeneration;
        SourceGeneration = sourceGeneration;
        GeometryRevision = geometryRevision;
        Sequence = sequence;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public NativeRemoteWindowPixelFormat PixelFormat { get; }

    public long OwnerGeneration { get; }

    public long SessionGeneration { get; }

    public long SourceGeneration { get; }

    public long GeometryRevision { get; }

    public long Sequence { get; }

    [JsonIgnore]
    public ReadOnlyMemory<byte> Pixels
    {
        get
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref disposed) != 0,
                this);
            return owner.Memory[..payloadLength];
        }
    }

    public static NativeRemoteWindowFrame TakeOwnership(
        IMemoryOwner<byte> owner,
        int payloadLength,
        int width,
        int height,
        int stride,
        NativeRemoteWindowPixelFormat pixelFormat,
        long ownerGeneration,
        long sessionGeneration,
        long sourceGeneration,
        long geometryRevision,
        long sequence)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (payloadLength is < 1 or > MaximumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadLength));
        }

        if (width is < 1 or > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height is < 1 or > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (!Enum.IsDefined(pixelFormat))
        {
            throw new ArgumentOutOfRangeException(nameof(pixelFormat));
        }

        int minimumStride = checked(width * 4);
        long requiredLength = checked((long)stride * height);
        if (stride < minimumStride
            || requiredLength != payloadLength
            || owner.Memory.Length < payloadLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stride),
                "A native Remote Window frame must contain one exact bounded pixel plane.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(ownerGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sessionGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(geometryRevision, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        return new NativeRemoteWindowFrame(
            owner,
            payloadLength,
            width,
            height,
            stride,
            pixelFormat,
            ownerGeneration,
            sessionGeneration,
            sourceGeneration,
            geometryRevision,
            sequence);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            owner.Dispose();
        }
    }

    public override string ToString() =>
        $"Native Remote Window frame ({Width}x{Height}, {PixelFormat}, {payloadLength} bytes, sequence {Sequence})";
}

public sealed record NativeRemoteWindowProtectionObservation
{
    internal NativeRemoteWindowProtectionObservation(
        ProtectionSnapshot protection,
        long ownerGeneration,
        long sessionGeneration,
        long sourceGeneration,
        long revision)
    {
        Protection = protection;
        OwnerGeneration = ownerGeneration;
        SessionGeneration = sessionGeneration;
        SourceGeneration = sourceGeneration;
        Revision = revision;
    }

    public ProtectionSnapshot Protection { get; }

    public long OwnerGeneration { get; }

    public long SessionGeneration { get; }

    public long SourceGeneration { get; }

    public long Revision { get; }

    public static NativeRemoteWindowProtectionObservation Create(
        ProtectionSnapshot protection,
        long ownerGeneration,
        long sessionGeneration,
        long sourceGeneration,
        long revision)
    {
        ArgumentNullException.ThrowIfNull(protection);
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sessionGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        return new NativeRemoteWindowProtectionObservation(
            protection,
            ownerGeneration,
            sessionGeneration,
            sourceGeneration,
            revision);
    }

    public override string ToString() =>
        $"Native Remote Window protection ({Protection.Kind}, owner {OwnerGeneration}, session {SessionGeneration}, source {SourceGeneration}, revision {Revision})";
}

public interface INativeProtectionSource : IDisposable
{
    public event Action<NativeRemoteWindowProtectionObservation>? Changed;

    public bool TryGetLatest(
        out NativeRemoteWindowProtectionObservation? observation);
}

public sealed partial class InMemoryNativeProtectionSource : INativeProtectionSource
{
    public const int MaximumPendingNotifications = 8;

    private readonly object gate = new();
    private readonly long ownerGeneration;
    private readonly Queue<ProtectionNotification> pendingNotifications = [];
    private readonly long sourceGeneration;
    private readonly long sessionGeneration;
    private object? activeCallbackToken;
    private int callbackDrainWaiters;
    private Action<NativeRemoteWindowProtectionObservation>? changed;
    private int disposed;
    private NativeRemoteWindowProtectionObservation? latest;
    private bool notificationDraining;
    private bool notificationOverflowed;
    private long revision;

    public InMemoryNativeProtectionSource(
        long ownerGeneration,
        long sessionGeneration,
        long sourceGeneration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sessionGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceGeneration, 1);
        this.ownerGeneration = ownerGeneration;
        this.sessionGeneration = sessionGeneration;
        this.sourceGeneration = sourceGeneration;
    }

    internal int CallbackDrainWaiterCount =>
        Volatile.Read(ref callbackDrainWaiters);

    public event Action<NativeRemoteWindowProtectionObservation>? Changed
    {
        add
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(
                    Volatile.Read(ref disposed) != 0,
                    this);
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

    public bool TryPublish(ProtectionSnapshot protection)
    {
        ArgumentNullException.ThrowIfNull(protection);
        NativeRemoteWindowProtectionObservation observation;
        Action<NativeRemoteWindowProtectionObservation>[] observers;
        bool drainNotifications;
        Exception? preparationFailure;
        ProtectionFormalNotification? formalNotification;
        ProtectionNotification notification;
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return false;
            }

            observation = NativeRemoteWindowProtectionObservation.Create(
                protection,
                ownerGeneration,
                sessionGeneration,
                sourceGeneration,
                checked(++revision));
            observers = changed?.GetInvocationList()
                .Cast<Action<NativeRemoteWindowProtectionObservation>>()
                .ToArray() ?? [];
            bool overflow = notificationOverflowed
                || pendingNotifications.Count >= MaximumPendingNotifications;
            if (overflow)
            {
                notificationOverflowed = true;
                observation = NativeRemoteWindowProtectionObservation.Create(
                    new ProtectionSnapshot(
                        ProtectionKind.Unknown,
                        protection.ObservedAt,
                        "notification_overflow"),
                    ownerGeneration,
                    sessionGeneration,
                    sourceGeneration,
                    checked(++revision));
                pendingNotifications.Clear();
            }

            latest = observation;
            ProtectionMutationCallbacks preparation =
                CommitProtectionPreparationMutationUnderGate(observation);
            preparationFailure = preparation.Failure;
            formalNotification = preparation.FormalNotification;
            notification = new(
                observation,
                observers,
                overflow,
                formalNotification is null);
            pendingNotifications.Enqueue(notification);
            if (formalNotification is not null)
            {
                BeginProtectionFormalNotificationUnderGate();
            }

            drainNotifications = notification.Ready
                && !notificationDraining;
            if (drainNotifications)
            {
                notificationDraining = true;
            }
        }

        if (formalNotification is { } formal)
        {
            NativeRemoteWindowDrainActivityScope? callbackScope = null;
            try
            {
                callbackScope = NativeRemoteWindowDrainActivityScope.Enter(
                    this,
                    notification);
                formal.Sink.NotifyNativeRemoteWindowProtectionChanged();
            }
            catch (Exception exception)
            {
                DeactivateLiveProtectionPreparationAfterNotificationFailure(
                    formal.Registration);
                preparationFailure = preparationFailure is null
                    ? exception
                    : CombineProtectionFailures(
                        preparationFailure,
                        exception);
            }
            finally
            {
                callbackScope?.Dispose();
                lock (gate)
                {
                    notification.Ready = true;
                    if (!notificationDraining
                        && pendingNotifications.Count != 0)
                    {
                        notificationDraining = true;
                        drainNotifications = true;
                    }

                    CompleteProtectionFormalNotificationUnderGate(
                        preparationFailure);
                }
            }
        }

        if (drainNotifications)
        {
            DrainNotifications();
        }

        if (preparationFailure is not null)
        {
            ExceptionDispatchInfo.Capture(
                    FindOutOfMemoryException(preparationFailure)
                        ?? preparationFailure)
                .Throw();
        }

        return true;
    }

    private void DrainNotifications()
    {
        while (true)
        {
            ProtectionNotification notification;
            lock (gate)
            {
                if (Volatile.Read(ref disposed) != 0)
                {
                    pendingNotifications.Clear();
                }

                if (pendingNotifications.Count == 0)
                {
                    notificationDraining = false;
                    TryFinalizeProtectionDisposalUnderGate();
                    Monitor.PulseAll(gate);
                    return;
                }

                if (!pendingNotifications.Peek().Ready)
                {
                    notificationDraining = false;
                    TryFinalizeProtectionDisposalUnderGate();
                    Monitor.PulseAll(gate);
                    return;
                }

                notification = pendingNotifications.Dequeue();
            }

            foreach (Action<NativeRemoteWindowProtectionObservation> observer in
                notification.Observers)
            {
                object callbackToken = new();
                lock (gate)
                {
                    if (Volatile.Read(ref disposed) != 0)
                    {
                        break;
                    }

                    activeCallbackToken = callbackToken;
                }

                using NativeRemoteWindowDrainActivityScope callbackScope =
                    NativeRemoteWindowDrainActivityScope.Enter(
                        this,
                        callbackToken);
                try
                {
                    observer(notification.Observation);
                }
                catch (Exception)
                {
                }
                finally
                {
                    lock (gate)
                    {
                        if (ReferenceEquals(activeCallbackToken, callbackToken))
                        {
                            activeCallbackToken = null;
                        }
                    }
                }
            }

            if (notification.Overflowed)
            {
                lock (gate)
                {
                    if (!pendingNotifications.Any(
                            static pending => pending.Overflowed))
                    {
                        notificationOverflowed = false;
                    }
                }
            }
        }
    }

    public bool TryGetLatest(
        out NativeRemoteWindowProtectionObservation? observation)
    {
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0 || latest is null)
            {
                observation = null;
                return false;
            }

            observation = latest;
            return true;
        }
    }

    public void Dispose()
    {
        bool firstDisposal = Interlocked.Exchange(ref disposed, 1) == 0;
        Exception? failure;
        ProtectionFormalNotification? formalNotification = null;
        if (!firstDisposal)
        {
            lock (gate)
            {
                while (!protectionDisposalFinalized
                    && !NativeRemoteWindowDrainActivityScope
                        .HasActiveAncestry())
                {
                    Monitor.Wait(gate);
                }

                failure = protectionDisposalFailure;
            }

            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(
                        FindOutOfMemoryException(failure) ?? failure)
                    .Throw();
            }

            return;
        }

        lock (gate)
        {
            latest = null;
            changed = null;
            pendingNotifications.Clear();
            ProtectionMutationCallbacks preparation =
                LoseProtectionPreparationUnderGate();
            if (preparation.Failure is not null)
            {
                RecordProtectionDisposalFailureUnderGate(
                    preparation.Failure);
            }

            formalNotification = preparation.FormalNotification;
        }

        if (formalNotification is { } formal)
        {
            Exception? notificationFailure = null;
            NativeRemoteWindowDrainActivityScope? callbackScope = null;
            try
            {
                callbackScope = NativeRemoteWindowDrainActivityScope.Enter(
                    this,
                    formal);
                formal.Sink.NotifyNativeRemoteWindowProtectionChanged();
            }
            catch (Exception exception)
            {
                notificationFailure = exception;
            }
            finally
            {
                callbackScope?.Dispose();
            }

            if (notificationFailure is not null)
            {
                lock (gate)
                {
                    RecordProtectionDisposalFailureUnderGate(
                        notificationFailure);
                }
            }
        }

        lock (gate)
        {
            protectionDisposalCleanupCommitted = true;
            TryFinalizeProtectionDisposalUnderGate();
            bool canWait = !NativeRemoteWindowDrainActivityScope
                .HasActiveAncestry();
            bool countOrdinaryDrainWaiter = canWait && notificationDraining;
            if (countOrdinaryDrainWaiter)
            {
                Interlocked.Increment(ref callbackDrainWaiters);
            }

            try
            {
                while (!protectionDisposalFinalized && canWait)
                {
                    Monitor.Wait(gate);
                }
            }
            finally
            {
                if (countOrdinaryDrainWaiter)
                {
                    Interlocked.Decrement(ref callbackDrainWaiters);
                }
            }

            failure = protectionDisposalFailure;
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(
                    FindOutOfMemoryException(failure) ?? failure)
                .Throw();
        }
    }

    private sealed class ProtectionNotification(
        NativeRemoteWindowProtectionObservation observation,
        Action<NativeRemoteWindowProtectionObservation>[] observers,
        bool overflowed,
        bool ready)
    {
        public NativeRemoteWindowProtectionObservation Observation { get; } =
            observation;

        public Action<NativeRemoteWindowProtectionObservation>[] Observers
        { get; } = observers;

        public bool Overflowed { get; } = overflowed;

        public bool Ready { get; set; } = ready;
    }
}

public enum LocalEmergencyStopCause
{
    UserAction,
    RegistrationLost,
}

public sealed record LocalEmergencyStopActivation
{
    private LocalEmergencyStopActivation(
        long ownerGeneration,
        long sessionGeneration,
        long sequence,
        LocalEmergencyStopCause cause)
    {
        OwnerGeneration = ownerGeneration;
        SessionGeneration = sessionGeneration;
        Sequence = sequence;
        Cause = cause;
    }

    public long OwnerGeneration { get; }

    public long SessionGeneration { get; }

    public long Sequence { get; }

    public LocalEmergencyStopCause Cause { get; }

    public static LocalEmergencyStopActivation Create(
        long ownerGeneration,
        long sessionGeneration,
        long sequence,
        LocalEmergencyStopCause cause)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sessionGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        if (!Enum.IsDefined(cause))
        {
            throw new ArgumentOutOfRangeException(nameof(cause));
        }

        return new LocalEmergencyStopActivation(
            ownerGeneration,
            sessionGeneration,
            sequence,
            cause);
    }

    public override string ToString() =>
        $"Local Emergency Stop activation ({Cause}, owner {OwnerGeneration}, session {SessionGeneration}, sequence {Sequence})";
}

public interface ILocalEmergencyStopRegistration : IDisposable
{
    public long OwnerGeneration { get; }

    public long SessionGeneration { get; }

    public bool IsCurrent { get; }
}

public interface ILocalEmergencyStopReadinessInvalidationSink
{
    // This mutation-gate callback must be bounded and non-blocking. It may only
    // latch the owning host Preparation reservation; it must not invoke native
    // work, cleanup, UI, or arbitrary callbacks.
    public void InvalidateEmergencyStopReadinessNow();
}

public interface ILocalEmergencyStopReadinessReservation : IDisposable
{
    public long OwnerGeneration { get; }

    public long SessionGeneration { get; }

    public bool IsCurrent { get; }

    public LocalEmergencyStopRegistrationResult TryPromote(
        Action<LocalEmergencyStopActivation> callback);
}

public interface ILocalEmergencyStopRegistrar : IDisposable
{
    // This prompt-free probe must not claim registration ownership. Callers
    // must still register and revalidate the exact generation before authority.
    public LocalBoundaryResult CheckReadiness();

    public LocalEmergencyStopReadinessReservationResult TryReserveReadiness(
        long ownerGeneration,
        long sessionGeneration,
        ILocalEmergencyStopReadinessInvalidationSink invalidationSink);

    public LocalEmergencyStopRegistrationResult TryRegister(
        long ownerGeneration,
        long sessionGeneration,
        Action<LocalEmergencyStopActivation> callback);
}

public sealed record LocalEmergencyStopReadinessReservationResult
{
    private LocalEmergencyStopReadinessReservationResult(
        LocalBoundaryResult boundary,
        ILocalEmergencyStopReadinessReservation? reservation)
    {
        Boundary = boundary;
        Reservation = reservation;
    }

    public LocalBoundaryResult Boundary { get; }

    public ILocalEmergencyStopReadinessReservation? Reservation { get; }

    public bool Reserved => Boundary.Succeeded
        && Reservation?.IsCurrent == true;

    public static LocalEmergencyStopReadinessReservationResult Confirmed(
        ILocalEmergencyStopReadinessReservation reservation,
        string reasonCode)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        if (!reservation.IsCurrent)
        {
            throw new ArgumentException(
                "A confirmed local Emergency Stop readiness reservation must be current.",
                nameof(reservation));
        }

        return new LocalEmergencyStopReadinessReservationResult(
            LocalBoundaryResult.Confirmed(reasonCode),
            reservation);
    }

    public static LocalEmergencyStopReadinessReservationResult Rejected(
        string reasonCode) => new(
            LocalBoundaryResult.Failed(reasonCode),
            reservation: null);

    public override string ToString() =>
        $"Local Emergency Stop readiness reservation ({Boundary.Status}, {Boundary.ReasonCode})";
}

public sealed record LocalEmergencyStopRegistrationResult
{
    private LocalEmergencyStopRegistrationResult(
        LocalBoundaryResult boundary,
        ILocalEmergencyStopRegistration? registration)
    {
        Boundary = boundary;
        Registration = registration;
    }

    public LocalBoundaryResult Boundary { get; }

    public ILocalEmergencyStopRegistration? Registration { get; }

    public bool Registered => Boundary.Succeeded
        && Registration?.IsCurrent == true;

    public static LocalEmergencyStopRegistrationResult Confirmed(
        ILocalEmergencyStopRegistration registration,
        string reasonCode)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (!registration.IsCurrent)
        {
            throw new ArgumentException(
                "A confirmed local Emergency Stop registration must be current.",
                nameof(registration));
        }

        return new LocalEmergencyStopRegistrationResult(
            LocalBoundaryResult.Confirmed(reasonCode),
            registration);
    }

    public static LocalEmergencyStopRegistrationResult Rejected(
        string reasonCode) => new(
            LocalBoundaryResult.Failed(reasonCode),
            registration: null);

    public override string ToString() =>
        $"Local Emergency Stop registration ({Boundary.Status}, {Boundary.ReasonCode})";
}

public sealed class InMemoryLocalEmergencyStopRegistrar :
    ILocalEmergencyStopRegistrar
{
    private readonly object gate = new();
    private int callbackDrainWaiters;
    private InMemoryLocalEmergencyStopOwner? current;
    private Exception? disposalFailure;
    private int disposed;
    private object? invokingCallbackToken;
    private InMemoryLocalEmergencyStopOwner? invokingRegistration;
    private long sequence;

    internal int CallbackDrainWaiterCount =>
        Volatile.Read(ref callbackDrainWaiters);

    public LocalBoundaryResult CheckReadiness()
    {
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return LocalBoundaryResult.Failed(
                    "emergency_stop_registrar_unavailable");
            }

            return current?.IsCurrent == true || invokingRegistration is not null
                ? LocalBoundaryResult.Failed(
                    "emergency_stop_registration_conflict")
                : LocalBoundaryResult.Confirmed("emergency_stop_ready");
        }
    }

    public LocalEmergencyStopReadinessReservationResult TryReserveReadiness(
        long ownerGeneration,
        long sessionGeneration,
        ILocalEmergencyStopReadinessInvalidationSink invalidationSink)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sessionGeneration, 1);
        ArgumentNullException.ThrowIfNull(invalidationSink);
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return LocalEmergencyStopReadinessReservationResult.Rejected(
                    "emergency_stop_registrar_unavailable");
            }

            if (current?.IsCurrent == true || invokingRegistration is not null)
            {
                return LocalEmergencyStopReadinessReservationResult.Rejected(
                    "emergency_stop_registration_conflict");
            }

            current = InMemoryLocalEmergencyStopOwner.CreateReserved(
                this,
                ownerGeneration,
                sessionGeneration,
                invalidationSink);
            return LocalEmergencyStopReadinessReservationResult.Confirmed(
                current.ReadinessReservation,
                "emergency_stop_readiness_reserved");
        }
    }

    public LocalEmergencyStopRegistrationResult TryRegister(
        long ownerGeneration,
        long sessionGeneration,
        Action<LocalEmergencyStopActivation> callback)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sessionGeneration, 1);
        ArgumentNullException.ThrowIfNull(callback);
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return Rejected("emergency_stop_registrar_unavailable");
            }

            if (current?.IsCurrent == true || invokingRegistration is not null)
            {
                return Rejected("emergency_stop_registration_conflict");
            }

            current = InMemoryLocalEmergencyStopOwner.CreateRegistered(
                this,
                ownerGeneration,
                sessionGeneration,
                callback);
            return LocalEmergencyStopRegistrationResult.Confirmed(
                current.Registration,
                "emergency_stop_registered");
        }
    }

    internal LocalEmergencyStopRegistrationResult TryPromoteReadiness(
        InMemoryLocalEmergencyStopReadinessReservation reservation,
        Action<LocalEmergencyStopActivation> callback)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(callback);
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return Rejected("emergency_stop_registrar_unavailable");
            }

            InMemoryLocalEmergencyStopOwner owner = reservation.Owner;
            if (!ReferenceEquals(current, owner)
                || !owner.TryPromoteUnderRegistrarGate(callback))
            {
                return Rejected("emergency_stop_readiness_stale");
            }

            return LocalEmergencyStopRegistrationResult.Confirmed(
                owner.Registration,
                "emergency_stop_registered");
        }
    }

    public bool Trigger() => Trigger(LocalEmergencyStopCause.UserAction);

    public bool LoseRegistration() =>
        Trigger(LocalEmergencyStopCause.RegistrationLost);

    private bool Trigger(LocalEmergencyStopCause cause)
    {
        Action<LocalEmergencyStopActivation>? callback = null;
        object? callbackToken = null;
        InMemoryLocalEmergencyStopOwner? registration = null;
        LocalEmergencyStopActivation? activation = null;
        Exception? readinessFailure = null;
        bool readinessInvalidated = false;
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0 || current is null)
            {
                return false;
            }

            registration = current;
            if (registration.IsReadinessReserved)
            {
                if (cause != LocalEmergencyStopCause.RegistrationLost)
                {
                    return false;
                }

                if (!registration.TryDeactivateReadinessUnderRegistrarGate(
                        out ILocalEmergencyStopReadinessInvalidationSink?
                            invalidationSink))
                {
                    return false;
                }

                current = null;
                readinessInvalidated = true;
                try
                {
                    invalidationSink!.InvalidateEmergencyStopReadinessNow();
                }
                catch (Exception exception) when (
                    exception is not OutOfMemoryException)
                {
                    readinessFailure = exception;
                }
                finally
                {
                    Monitor.PulseAll(gate);
                }
            }
            else if (registration.TryDeactivateRegisteredUnderRegistrarGate(
                    out callback)
                && callback is not null)
            {
                long ownerGeneration = registration.OwnerGeneration;
                long sessionGeneration = registration.SessionGeneration;
                current = null;
                invokingRegistration = registration;
                callbackToken = new object();
                invokingCallbackToken = callbackToken;
                activation = LocalEmergencyStopActivation.Create(
                    ownerGeneration,
                    sessionGeneration,
                    checked(++sequence),
                    cause);
            }
            else
            {
                return false;
            }
        }

        if (readinessFailure is not null)
        {
            ExceptionDispatchInfo.Capture(readinessFailure).Throw();
        }

        if (readinessInvalidated)
        {
            return true;
        }

        if (callback is null
            || callbackToken is null
            || registration is null
            || activation is null)
        {
            throw new InvalidOperationException(
                "A local Emergency Stop activation lost its registered owner.");
        }

        using NativeRemoteWindowDrainActivityScope callbackScope =
            NativeRemoteWindowDrainActivityScope.Enter(this, callbackToken);
        try
        {
            callback(activation);
        }
        catch (Exception)
        {
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(invokingRegistration, registration)
                    && ReferenceEquals(invokingCallbackToken, callbackToken))
                {
                    invokingRegistration = null;
                    invokingCallbackToken = null;
                    Monitor.PulseAll(gate);
                }
            }
        }

        return true;
    }

    public void Dispose()
    {
        bool firstDisposal = Interlocked.Exchange(ref disposed, 1) == 0;
        Action<LocalEmergencyStopActivation>? callback = null;
        object? callbackToken = null;
        InMemoryLocalEmergencyStopOwner? callbackRegistration = null;
        LocalEmergencyStopActivation? activation = null;
        Exception? failure;
        lock (gate)
        {
            if (firstDisposal)
            {
                InMemoryLocalEmergencyStopOwner? owner = current;
                if (owner?.TryDeactivateReadinessUnderRegistrarGate(
                        out ILocalEmergencyStopReadinessInvalidationSink?
                            invalidationSink) == true)
                {
                    current = null;
                    try
                    {
                        invalidationSink!.InvalidateEmergencyStopReadinessNow();
                    }
                    catch (Exception exception) when (
                        exception is not OutOfMemoryException)
                    {
                        disposalFailure = exception;
                    }
                    finally
                    {
                        Monitor.PulseAll(gate);
                    }
                }
                else
                {
                    if (owner?.TryDeactivateRegisteredUnderRegistrarGate(
                            out callback) == true
                        && callback is not null)
                    {
                        current = null;
                        callbackRegistration = owner;
                        invokingRegistration = owner;
                        callbackToken = new object();
                        invokingCallbackToken = callbackToken;
                        activation = LocalEmergencyStopActivation.Create(
                            owner.OwnerGeneration,
                            owner.SessionGeneration,
                            checked(++sequence),
                            LocalEmergencyStopCause.RegistrationLost);
                    }
                    else
                    {
                        owner?.Deactivate();
                        current = null;
                    }
                }
            }

            if (callback is null)
            {
                WaitForCallbackDrain(
                    invokingRegistration,
                    invokingCallbackToken);
            }

            failure = disposalFailure;
        }

        if (callback is not null
            && callbackToken is not null
            && callbackRegistration is not null
            && activation is not null)
        {
            using NativeRemoteWindowDrainActivityScope callbackScope =
                NativeRemoteWindowDrainActivityScope.Enter(this, callbackToken);
            try
            {
                callback(activation);
            }
            catch (Exception)
            {
            }
            finally
            {
                lock (gate)
                {
                    if (ReferenceEquals(
                            invokingRegistration,
                            callbackRegistration)
                        && ReferenceEquals(
                            invokingCallbackToken,
                            callbackToken))
                    {
                        invokingRegistration = null;
                        invokingCallbackToken = null;
                        Monitor.PulseAll(gate);
                    }
                }
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal void Unregister(InMemoryLocalEmergencyStopOwner registration)
    {
        lock (gate)
        {
            registration.Deactivate();
            if (ReferenceEquals(current, registration))
            {
                current = null;
            }

            if (ReferenceEquals(invokingRegistration, registration))
            {
                WaitForCallbackDrain(registration, invokingCallbackToken);
            }
        }
    }

    private void WaitForCallbackDrain(
        InMemoryLocalEmergencyStopOwner? expectedRegistration,
        object? expectedCallbackToken)
    {
        bool callbackInFlight = expectedRegistration is not null
            && expectedCallbackToken is not null
            && ReferenceEquals(invokingRegistration, expectedRegistration)
            && ReferenceEquals(invokingCallbackToken, expectedCallbackToken);
        if (!callbackInFlight)
        {
            return;
        }

        bool callbackDrainRequired =
            !NativeRemoteWindowDrainActivityScope.IsActiveFor(
                this,
                expectedCallbackToken);
        if (!callbackDrainRequired
            || NativeRemoteWindowDrainActivityScope.HasActiveAncestry())
        {
            return;
        }

        Interlocked.Increment(ref callbackDrainWaiters);
        try
        {
            while (expectedRegistration is not null
                && expectedCallbackToken is not null
                && ReferenceEquals(invokingRegistration, expectedRegistration)
                && ReferenceEquals(invokingCallbackToken, expectedCallbackToken))
            {
                Monitor.Wait(gate);
            }
        }
        finally
        {
            Interlocked.Decrement(ref callbackDrainWaiters);
        }
    }

    private static LocalEmergencyStopRegistrationResult Rejected(
        string reasonCode) =>
        LocalEmergencyStopRegistrationResult.Rejected(reasonCode);

    internal sealed class InMemoryLocalEmergencyStopOwner : IDisposable
    {
        private enum OwnerState
        {
            Reserved = 1,
            Registered = 2,
            Inactive = 3,
        }

        private readonly InMemoryLocalEmergencyStopRegistrar registrar;
        private Action<LocalEmergencyStopActivation>? callback;
        private ILocalEmergencyStopReadinessInvalidationSink? invalidationSink;
        private int state;

        private readonly InMemoryLocalEmergencyStopReadinessReservation
            readinessReservation;
        private readonly InMemoryLocalEmergencyStopRegistration registration;

        private InMemoryLocalEmergencyStopOwner(
            InMemoryLocalEmergencyStopRegistrar registrar,
            long ownerGeneration,
            long sessionGeneration,
            OwnerState state,
            Action<LocalEmergencyStopActivation>? callback,
            ILocalEmergencyStopReadinessInvalidationSink? invalidationSink)
        {
            this.registrar = registrar;
            OwnerGeneration = ownerGeneration;
            SessionGeneration = sessionGeneration;
            this.state = (int)state;
            this.callback = callback;
            this.invalidationSink = invalidationSink;
            readinessReservation = new(this);
            registration = new(this);
        }

        public static InMemoryLocalEmergencyStopOwner CreateRegistered(
            InMemoryLocalEmergencyStopRegistrar registrar,
            long ownerGeneration,
            long sessionGeneration,
            Action<LocalEmergencyStopActivation> callback) => new(
                registrar,
                ownerGeneration,
                sessionGeneration,
                OwnerState.Registered,
                callback,
                invalidationSink: null);

        public static InMemoryLocalEmergencyStopOwner CreateReserved(
            InMemoryLocalEmergencyStopRegistrar registrar,
            long ownerGeneration,
            long sessionGeneration,
            ILocalEmergencyStopReadinessInvalidationSink invalidationSink) => new(
                registrar,
                ownerGeneration,
                sessionGeneration,
                OwnerState.Reserved,
                callback: null,
                invalidationSink);

        public long OwnerGeneration { get; }

        public long SessionGeneration { get; }

        public bool IsCurrent => Volatile.Read(ref state)
            is (int)OwnerState.Reserved or (int)OwnerState.Registered;

        public InMemoryLocalEmergencyStopReadinessReservation
            ReadinessReservation => readinessReservation;

        public InMemoryLocalEmergencyStopRegistration Registration =>
            registration;

        internal bool IsReadinessReserved =>
            Volatile.Read(ref state) == (int)OwnerState.Reserved;

        public void Dispose() => registrar.Unregister(this);

        public LocalEmergencyStopRegistrationResult TryPromote(
            Action<LocalEmergencyStopActivation> callback) =>
            registrar.TryPromoteReadiness(readinessReservation, callback);

        public void Deactivate()
        {
            Volatile.Write(ref state, (int)OwnerState.Inactive);
            _ = Interlocked.Exchange(ref callback, null);
            _ = Interlocked.Exchange(ref invalidationSink, null);
        }

        public bool TryDeactivateReadinessUnderRegistrarGate(
            out ILocalEmergencyStopReadinessInvalidationSink? sink)
        {
            if (Volatile.Read(ref state) != (int)OwnerState.Reserved)
            {
                sink = null;
                return false;
            }

            Volatile.Write(ref state, (int)OwnerState.Inactive);
            sink = Interlocked.Exchange(ref invalidationSink, null);
            _ = Interlocked.Exchange(ref callback, null);
            return sink is not null;
        }

        public bool TryDeactivateRegisteredUnderRegistrarGate(
            out Action<LocalEmergencyStopActivation>? deactivatedCallback)
        {
            if (Volatile.Read(ref state) != (int)OwnerState.Registered)
            {
                deactivatedCallback = null;
                return false;
            }

            Volatile.Write(ref state, (int)OwnerState.Inactive);
            deactivatedCallback = Interlocked.Exchange(ref callback, null);
            return deactivatedCallback is not null;
        }

        public bool TryPromoteUnderRegistrarGate(
            Action<LocalEmergencyStopActivation> promotedCallback)
        {
            if (Volatile.Read(ref state) != (int)OwnerState.Reserved
                || invalidationSink is null)
            {
                return false;
            }

            callback = promotedCallback;
            invalidationSink = null;
            Volatile.Write(ref state, (int)OwnerState.Registered);
            return true;
        }

        public override string ToString() =>
            $"Local Emergency Stop owner (owner {OwnerGeneration}, state {(OwnerState)Volatile.Read(ref state)})";
    }

    internal sealed class InMemoryLocalEmergencyStopReadinessReservation(
        InMemoryLocalEmergencyStopOwner owner) :
        ILocalEmergencyStopReadinessReservation
    {
        internal InMemoryLocalEmergencyStopOwner Owner { get; } = owner;

        public bool IsCurrent => Owner.IsReadinessReserved;

        public long OwnerGeneration => Owner.OwnerGeneration;

        public long SessionGeneration => Owner.SessionGeneration;

        public void Dispose() => Owner.Dispose();

        public LocalEmergencyStopRegistrationResult TryPromote(
            Action<LocalEmergencyStopActivation> callback) =>
            Owner.TryPromote(callback);
    }

    internal sealed class InMemoryLocalEmergencyStopRegistration(
        InMemoryLocalEmergencyStopOwner owner) :
        ILocalEmergencyStopRegistration
    {
        public bool IsCurrent => owner.IsCurrent && !owner.IsReadinessReserved;

        public long OwnerGeneration => owner.OwnerGeneration;

        public long SessionGeneration => owner.SessionGeneration;

        public void Dispose() => owner.Dispose();
    }
}
