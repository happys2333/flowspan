using System.Buffers;
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
    INativeRemoteWindowPermissionBoundary
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

public sealed class InMemoryNativeProtectionSource : INativeProtectionSource
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
            pendingNotifications.Enqueue(
                new ProtectionNotification(observation, observers, overflow));
            drainNotifications = !notificationDraining;
            notificationDraining = true;
        }

        if (drainNotifications)
        {
            DrainNotifications();
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
        lock (gate)
        {
            if (firstDisposal)
            {
                latest = null;
                changed = null;
                pendingNotifications.Clear();
            }

            bool callbackDrainRequired = notificationDraining
                && !NativeRemoteWindowDrainActivityScope.IsActiveFor(
                    this,
                    activeCallbackToken);
            if (callbackDrainRequired
                && !NativeRemoteWindowDrainActivityScope.HasActiveAncestry())
            {
                Interlocked.Increment(ref callbackDrainWaiters);
                try
                {
                    while (notificationDraining)
                    {
                        Monitor.Wait(gate);
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref callbackDrainWaiters);
                }
            }
        }
    }

    private sealed record ProtectionNotification(
        NativeRemoteWindowProtectionObservation Observation,
        Action<NativeRemoteWindowProtectionObservation>[] Observers,
        bool Overflowed);
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

public interface ILocalEmergencyStopRegistrar : IDisposable
{
    public LocalEmergencyStopRegistrationResult TryRegister(
        long ownerGeneration,
        long sessionGeneration,
        Action<LocalEmergencyStopActivation> callback);
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
    private InMemoryLocalEmergencyStopRegistration? current;
    private int disposed;
    private object? invokingCallbackToken;
    private InMemoryLocalEmergencyStopRegistration? invokingRegistration;
    private long sequence;

    internal int CallbackDrainWaiterCount =>
        Volatile.Read(ref callbackDrainWaiters);

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

            current = new InMemoryLocalEmergencyStopRegistration(
                this,
                ownerGeneration,
                sessionGeneration,
                callback);
            return LocalEmergencyStopRegistrationResult.Confirmed(
                current,
                "emergency_stop_registered");
        }
    }

    public bool Trigger() => Trigger(LocalEmergencyStopCause.UserAction);

    public bool LoseRegistration() =>
        Trigger(LocalEmergencyStopCause.RegistrationLost);

    private bool Trigger(LocalEmergencyStopCause cause)
    {
        Action<LocalEmergencyStopActivation> callback;
        object callbackToken;
        InMemoryLocalEmergencyStopRegistration registration;
        LocalEmergencyStopActivation activation;
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0
                || current is null
                || !current.TryDeactivate(out Action<LocalEmergencyStopActivation>?
                    deactivatedCallback)
                || deactivatedCallback is null)
            {
                return false;
            }

            registration = current;
            long ownerGeneration = registration.OwnerGeneration;
            long sessionGeneration = registration.SessionGeneration;
            callback = deactivatedCallback;
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
        lock (gate)
        {
            if (firstDisposal)
            {
                current?.Deactivate();
                current = null;
            }

            WaitForCallbackDrain(invokingRegistration, invokingCallbackToken);
        }
    }

    internal void Unregister(InMemoryLocalEmergencyStopRegistration registration)
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
        InMemoryLocalEmergencyStopRegistration? expectedRegistration,
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

    internal sealed class InMemoryLocalEmergencyStopRegistration :
        ILocalEmergencyStopRegistration
    {
        private readonly InMemoryLocalEmergencyStopRegistrar registrar;
        private Action<LocalEmergencyStopActivation>? callback;
        private int current = 1;

        public InMemoryLocalEmergencyStopRegistration(
            InMemoryLocalEmergencyStopRegistrar registrar,
            long ownerGeneration,
            long sessionGeneration,
            Action<LocalEmergencyStopActivation> callback)
        {
            this.registrar = registrar;
            OwnerGeneration = ownerGeneration;
            SessionGeneration = sessionGeneration;
            this.callback = callback;
        }

        public long OwnerGeneration { get; }

        public long SessionGeneration { get; }

        public bool IsCurrent => Volatile.Read(ref current) != 0;

        public void Dispose() => registrar.Unregister(this);

        public void Deactivate()
        {
            _ = Interlocked.Exchange(ref current, 0);
            _ = Interlocked.Exchange(ref callback, null);
        }

        public bool TryDeactivate(
            out Action<LocalEmergencyStopActivation>? deactivatedCallback)
        {
            if (Interlocked.Exchange(ref current, 0) == 0)
            {
                deactivatedCallback = null;
                return false;
            }

            deactivatedCallback = Interlocked.Exchange(ref callback, null);
            return deactivatedCallback is not null;
        }

        public override string ToString() =>
            $"Local Emergency Stop registration (owner {OwnerGeneration}, current {IsCurrent})";
    }
}
