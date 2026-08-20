using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Flowspan.Domain;

namespace Flowspan.Platform;

public interface INativeRemoteWindowSourceCatalog
{
    public IReadOnlyList<NativeRemoteWindowSourceSnapshot> GetSnapshot();

    public bool TryAcquire(
        NativeRemoteWindowSourceToken token,
        long sourceGeneration,
        out NativeRemoteWindowSourceLease? lease);
}

public sealed class NativeRemoteWindowSourceToken :
    IEquatable<NativeRemoteWindowSourceToken>
{
    private readonly Guid value;

    private NativeRemoteWindowSourceToken(Guid value) => this.value = value;

    internal static NativeRemoteWindowSourceToken Create() => new(Guid.NewGuid());

    public bool Equals(NativeRemoteWindowSourceToken? other) =>
        other is not null && value == other.value;

    public override bool Equals(object? obj) =>
        obj is NativeRemoteWindowSourceToken other && Equals(other);

    public override int GetHashCode() => value.GetHashCode();

    public override string ToString() => "native-source-token";
}

public sealed record NativeRemoteWindowSourceSnapshot
{
    internal NativeRemoteWindowSourceSnapshot(
        NativeRemoteWindowSourceToken token,
        RemoteWindowSourceReference source,
        NativeRemoteWindowSourceMetadata metadata,
        long geometryRevision)
    {
        Token = token;
        Source = source;
        Metadata = metadata;
        GeometryRevision = geometryRevision;
    }

    [JsonIgnore]
    public NativeRemoteWindowSourceToken Token { get; }

    public RemoteWindowSourceReference Source { get; }

    public NativeRemoteWindowSourceMetadata Metadata { get; }

    public long GeometryRevision { get; }

    public override string ToString() =>
        $"Native Remote Window source {Source.ActivityId} (generation {Source.SourceGeneration})";
}

public sealed class NativeRemoteWindowSourceLease : IDisposable
{
    private readonly NativeRemoteWindowSourceLeaseState state;
    private int disposed;

    internal NativeRemoteWindowSourceLease(
        NativeRemoteWindowSourceLeaseState state) => this.state = state;

    public NativeRemoteWindowSourceSnapshot Snapshot => state.Snapshot;

    public RemoteWindowSourceReference Source => Snapshot.Source;

    public bool IsCurrent => Volatile.Read(ref disposed) == 0 && state.IsCurrent;

    public bool TryGetCurrentSnapshot(
        out NativeRemoteWindowSourceSnapshot? snapshot)
    {
        if (Volatile.Read(ref disposed) != 0 || !state.IsCurrent)
        {
            snapshot = null;
            return false;
        }

        NativeRemoteWindowSourceSnapshot current = state.Snapshot;
        if (Volatile.Read(ref disposed) != 0 || !state.IsCurrent)
        {
            snapshot = null;
            return false;
        }

        snapshot = current;
        return true;
    }

    public bool TryRetain(out NativeRemoteWindowSourceLease? retainedLease)
    {
        if (Volatile.Read(ref disposed) != 0 || !state.IsCurrent)
        {
            retainedLease = null;
            return false;
        }

        retainedLease = new NativeRemoteWindowSourceLease(state);
        if (Volatile.Read(ref disposed) == 0 && state.IsCurrent)
        {
            return true;
        }

        retainedLease.Dispose();
        retainedLease = null;
        return false;
    }

    public bool TryRegisterInvalidationCallback(
        Action callback,
        out NativeRemoteWindowSourceInvalidationRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (Volatile.Read(ref disposed) != 0
            || !state.TryRegisterInvalidationCallback(
                callback,
                out registration))
        {
            registration = null;
            return false;
        }

        if (Volatile.Read(ref disposed) == 0)
        {
            return true;
        }

        registration?.Dispose();
        registration = null;
        return false;
    }

    public bool TryAcquireUseScope(
        long sourceGeneration,
        long? geometryRevision,
        out NativeRemoteWindowSourceUseScope? scope)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceGeneration, 1);
        if (geometryRevision.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(
                geometryRevision.Value,
                1);
        }

        if (Volatile.Read(ref disposed) != 0
            || !state.TryAcquireUseScope(
                sourceGeneration,
                geometryRevision,
                out scope))
        {
            scope = null;
            return false;
        }

        if (Volatile.Read(ref disposed) == 0)
        {
            return true;
        }

        scope?.Dispose();
        scope = null;
        return false;
    }

    public void Dispose() => Interlocked.Exchange(ref disposed, 1);

    public override string ToString() =>
        $"Native Remote Window source lease {Source.ActivityId} (current {IsCurrent})";
}

public sealed class NativeRemoteWindowSourceUseScope : IDisposable
{
    private readonly NativeRemoteWindowDrainActivityScope activityScope;
    private readonly NativeRemoteWindowSourceLeaseState state;
    private int disposed;

    internal NativeRemoteWindowSourceUseScope(
        NativeRemoteWindowSourceLeaseState state)
    {
        this.state = state;
        activityScope = NativeRemoteWindowDrainActivityScope.Enter(
            state,
            new object());
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        activityScope.Dispose();
        state.ReleaseUseScope();
    }

    public override string ToString() =>
        "Native Remote Window source use scope";
}

public sealed class NativeRemoteWindowSourceInvalidationRegistration : IDisposable
{
    private readonly NativeRemoteWindowSourceLeaseState state;
    private Action? callback;
    private bool callbackInFlight;
    private int callbackDrainWaiters;
    private object? callbackToken;
    private bool disposed;

    internal NativeRemoteWindowSourceInvalidationRegistration(
        NativeRemoteWindowSourceLeaseState state,
        Action callback)
    {
        this.state = state;
        this.callback = callback;
    }

    public bool IsCurrent => state.IsInvalidationRegistrationCurrent(this);

    internal int CallbackDrainWaiterCount =>
        Volatile.Read(ref callbackDrainWaiters);

    public void Dispose() => state.UnregisterInvalidationCallback(this);

    internal void Deactivate()
    {
        disposed = true;
        callback = null;
    }

    internal bool TryBeginCallback(
        out Action? invalidationCallback,
        out object? activeCallbackToken)
    {
        if (disposed || callback is null)
        {
            invalidationCallback = null;
            activeCallbackToken = null;
            return false;
        }

        invalidationCallback = callback;
        callback = null;
        callbackInFlight = true;
        callbackToken = new object();
        activeCallbackToken = callbackToken;
        return true;
    }

    internal bool CallbackInFlight => callbackInFlight;

    internal object? CallbackToken => callbackToken;

    internal bool Disposed => disposed;

    internal void BeginCallbackDrainWait() =>
        Interlocked.Increment(ref callbackDrainWaiters);

    internal void EndCallbackDrainWait() =>
        Interlocked.Decrement(ref callbackDrainWaiters);

    internal void CompleteCallback(object activeCallbackToken)
    {
        if (ReferenceEquals(callbackToken, activeCallbackToken))
        {
            callbackInFlight = false;
            callbackToken = null;
        }
    }

    public override string ToString() =>
        $"Native Remote Window source invalidation registration (current {IsCurrent})";
}

public sealed class NativeRemoteWindowSourceRegistration : IDisposable
{
    private readonly NativeRemoteWindowSourceRegistry registry;
    private readonly NativeRemoteWindowSourceLeaseState state;
    private int disposed;

    internal NativeRemoteWindowSourceRegistration(
        NativeRemoteWindowSourceRegistry registry,
        NativeRemoteWindowSourceLeaseState state)
    {
        this.registry = registry;
        this.state = state;
    }

    public NativeRemoteWindowSourceSnapshot Snapshot => state.Snapshot;

    public RemoteWindowSourceReference Source => Snapshot.Source;

    public bool TryUpdate(NativeRemoteWindowSourceMetadata metadata) =>
        Volatile.Read(ref disposed) == 0 && registry.TryUpdate(state, metadata);

    internal int InvalidationDrainWaiterCount =>
        state.InvalidationDrainWaiterCount;

    public void Dispose()
    {
        Interlocked.Exchange(ref disposed, 1);
        registry.Unregister(state);
    }

    public override string ToString() =>
        $"Native Remote Window source registration {Source.ActivityId}";
}

public sealed class NativeRemoteWindowSourceRegistry :
    INativeRemoteWindowSourceCatalog,
    IDisposable
{
    public const int MaximumSources = 128;

    private readonly Dictionary<NativeRemoteWindowSourceToken,
        NativeRemoteWindowSourceLeaseState> entries = [];
    private readonly object gate = new();
    private readonly DeviceId hostDeviceId;
    private readonly HashSet<NativeRemoteWindowSourceLeaseState>
        invalidatingStates = [];
    private NativeRemoteWindowSourceLeaseState[] disposalStates = [];
    private int disposed;
    private long nextGeneration;

    public NativeRemoteWindowSourceRegistry(DeviceId hostDeviceId) =>
        this.hostDeviceId = hostDeviceId
            ?? throw new ArgumentNullException(nameof(hostDeviceId));

    public NativeRemoteWindowSourceRegistration RegisterGeneric(
        NativeRemoteWindowSourceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        lock (gate)
        {
            ThrowIfDisposed();
            if (entries.Count >= MaximumSources - invalidatingStates.Count)
            {
                throw new InvalidOperationException(
                    $"A native Remote Window source registry cannot contain more than {MaximumSources} sources.");
            }

            long generation = checked(++nextGeneration);
            RemoteWindowSourceReference source =
                RemoteWindowSourceReference.CreateGeneric(
                    ActivityId.From(Guid.NewGuid()),
                    hostDeviceId,
                    metadata.DisplayName,
                    generation);
            var snapshot = new NativeRemoteWindowSourceSnapshot(
                NativeRemoteWindowSourceToken.Create(),
                source,
                metadata,
                geometryRevision: 1);
            var state = new NativeRemoteWindowSourceLeaseState(
                snapshot,
                OnInvalidationDrained);
            entries.Add(snapshot.Token, state);
            return new NativeRemoteWindowSourceRegistration(this, state);
        }
    }

    public IReadOnlyList<NativeRemoteWindowSourceSnapshot> GetSnapshot()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            return entries.Values
                .Select(static state => state.Snapshot)
                .OrderBy(static snapshot => snapshot.Source.ActivityId.ToString(),
                    StringComparer.Ordinal)
                .ToImmutableArray();
        }
    }

    public bool TryAcquire(
        NativeRemoteWindowSourceToken token,
        long sourceGeneration,
        out NativeRemoteWindowSourceLease? lease)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceGeneration, 1);
        lock (gate)
        {
            ThrowIfDisposed();
            if (entries.TryGetValue(token, out NativeRemoteWindowSourceLeaseState? state)
                && state.IsCurrent
                && state.Snapshot.Source.SourceGeneration == sourceGeneration)
            {
                lease = new NativeRemoteWindowSourceLease(state);
                return true;
            }

            lease = null;
            return false;
        }
    }

    public void Dispose()
    {
        NativeRemoteWindowSourceLeaseState[] states;
        lock (gate)
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                Volatile.Write(ref disposed, 1);
                foreach (NativeRemoteWindowSourceLeaseState state in entries.Values)
                {
                    invalidatingStates.Add(state);
                    state.BeginInvalidation();
                }

                entries.Clear();
                disposalStates = invalidatingStates.ToArray();
            }

            states = disposalStates;
        }

        foreach (NativeRemoteWindowSourceLeaseState state in states)
        {
            state.DrainInvalidation();
        }
    }

    internal void Unregister(NativeRemoteWindowSourceLeaseState state)
    {
        lock (gate)
        {
            if (entries.TryGetValue(
                    state.Snapshot.Token,
                    out NativeRemoteWindowSourceLeaseState? current)
                && ReferenceEquals(current, state))
            {
                invalidatingStates.Add(state);
                state.BeginInvalidation();
                entries.Remove(state.Snapshot.Token);
            }
        }

        state.DrainInvalidation();
    }

    internal bool TryUpdate(
        NativeRemoteWindowSourceLeaseState state,
        NativeRemoteWindowSourceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        bool securityBindingChanged = false;
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return false;
            }

            NativeRemoteWindowSourceSnapshot current = state.Snapshot;
            if (!entries.TryGetValue(
                    current.Token,
                    out NativeRemoteWindowSourceLeaseState? registered)
                || !ReferenceEquals(registered, state)
                || !state.IsCurrent)
            {
                return false;
            }

            NativeRemoteWindowSourceMetadata currentMetadata = current.Metadata;
            securityBindingChanged =
                currentMetadata.OwningApplicationName
                    != metadata.OwningApplicationName
                || currentMetadata.Geometry != metadata.Geometry
                || currentMetadata.SupportsCapture != metadata.SupportsCapture
                || currentMetadata.SupportsInput != metadata.SupportsInput
                || currentMetadata.Protection != metadata.Protection;
            if (securityBindingChanged)
            {
                invalidatingStates.Add(state);
                state.BeginInvalidation();
                entries.Remove(current.Token);
            }
        }

        if (securityBindingChanged)
        {
            state.DrainInvalidation();
            return false;
        }

        return state.TryUpdate(metadata);
    }

    private void OnInvalidationDrained(NativeRemoteWindowSourceLeaseState state)
    {
        lock (gate)
        {
            invalidatingStates.Remove(state);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(
        Volatile.Read(ref disposed) != 0,
        this);
}

internal sealed class NativeRemoteWindowSourceLeaseState
{
    private readonly object gate = new();
    private readonly List<NativeRemoteWindowSourceInvalidationRegistration>
        invalidationRegistrations = [];
    private readonly Action<NativeRemoteWindowSourceLeaseState>
        invalidationDrained;
    private int activeUseScopes;
    private object? activeInvalidationCallbackToken;
    private int current = 1;
    private bool invalidationCallbacksClaimed;
    private int invalidationDrainWaiters;
    private bool invalidationDraining;
    private NativeRemoteWindowSourceSnapshot snapshot;
    private bool useAdmissionBlocked;

    public NativeRemoteWindowSourceLeaseState(
        NativeRemoteWindowSourceSnapshot snapshot,
        Action<NativeRemoteWindowSourceLeaseState> invalidationDrained)
    {
        this.snapshot = snapshot;
        this.invalidationDrained = invalidationDrained;
    }

    public NativeRemoteWindowSourceSnapshot Snapshot => Volatile.Read(ref snapshot);

    public bool IsCurrent => Volatile.Read(ref current) != 0;

    public int InvalidationDrainWaiterCount
    {
        get
        {
            lock (gate)
            {
                return invalidationDrainWaiters;
            }
        }
    }

    public bool TryAcquireUseScope(
        long sourceGeneration,
        long? geometryRevision,
        out NativeRemoteWindowSourceUseScope? scope)
    {
        lock (gate)
        {
            NativeRemoteWindowSourceSnapshot currentSnapshot = snapshot;
            if (Volatile.Read(ref current) == 0
                || useAdmissionBlocked
                || currentSnapshot.Source.SourceGeneration != sourceGeneration
                || geometryRevision.HasValue
                && currentSnapshot.GeometryRevision != geometryRevision.Value)
            {
                scope = null;
                return false;
            }

            activeUseScopes = checked(activeUseScopes + 1);
            scope = new NativeRemoteWindowSourceUseScope(this);
            return true;
        }
    }

    public void ReleaseUseScope()
    {
        NativeRemoteWindowSourceInvalidationRegistration[]? registrations = null;
        lock (gate)
        {
            if (activeUseScopes <= 0)
            {
                throw new InvalidOperationException(
                    "A native Remote Window source use scope was released without a matching acquisition.");
            }

            activeUseScopes--;
            if (activeUseScopes == 0)
            {
                if (invalidationDraining && !invalidationCallbacksClaimed)
                {
                    registrations = ClaimInvalidationCallbacks();
                }

                Monitor.PulseAll(gate);
            }
        }

        if (registrations is not null)
        {
            DrainInvalidationCallbacks(registrations);
        }
    }

    public bool TryRegisterInvalidationCallback(
        Action callback,
        out NativeRemoteWindowSourceInvalidationRegistration? registration)
    {
        lock (gate)
        {
            if (Volatile.Read(ref current) == 0)
            {
                registration = null;
                return false;
            }

            registration = new NativeRemoteWindowSourceInvalidationRegistration(
                this,
                callback);
            invalidationRegistrations.Add(registration);
            return true;
        }
    }

    public bool IsInvalidationRegistrationCurrent(
        NativeRemoteWindowSourceInvalidationRegistration registration)
    {
        lock (gate)
        {
            return Volatile.Read(ref current) != 0
                && !registration.Disposed
                && invalidationRegistrations.Contains(registration);
        }
    }

    public void UnregisterInvalidationCallback(
        NativeRemoteWindowSourceInvalidationRegistration registration)
    {
        lock (gate)
        {
            registration.Deactivate();
            invalidationRegistrations.Remove(registration);
            bool callbackDrainRequired = registration.CallbackInFlight
                && !NativeRemoteWindowDrainActivityScope.IsActiveFor(
                    this,
                    registration.CallbackToken);
            if (callbackDrainRequired
                && !NativeRemoteWindowDrainActivityScope.HasActiveAncestry())
            {
                registration.BeginCallbackDrainWait();
                try
                {
                    while (registration.CallbackInFlight)
                    {
                        Monitor.Wait(gate);
                    }
                }
                finally
                {
                    registration.EndCallbackDrainWait();
                }
            }
        }
    }

    public void Invalidate()
    {
        BeginInvalidation();
        DrainInvalidation();
    }

    public void BeginInvalidation()
    {
        lock (gate)
        {
            if (Volatile.Read(ref current) == 0)
            {
                return;
            }

            Volatile.Write(ref current, 0);
            useAdmissionBlocked = true;
            invalidationDraining = true;
            Monitor.PulseAll(gate);
        }
    }

    public void DrainInvalidation()
    {
        NativeRemoteWindowSourceInvalidationRegistration[]? registrations = null;
        lock (gate)
        {
            if (!invalidationDraining)
            {
                return;
            }

            if (NativeRemoteWindowDrainActivityScope.IsActiveForOwner(this))
            {
                return;
            }

            if ((activeUseScopes > 0 || invalidationCallbacksClaimed)
                && NativeRemoteWindowDrainActivityScope.HasActiveAncestry())
            {
                return;
            }

            while (activeUseScopes > 0 && !invalidationCallbacksClaimed)
            {
                WaitForInvalidationProgress();
            }

            if (invalidationCallbacksClaimed)
            {
                WaitForInvalidationDrain();
                return;
            }

            registrations = ClaimInvalidationCallbacks();
        }

        DrainInvalidationCallbacks(registrations);
    }

    private NativeRemoteWindowSourceInvalidationRegistration[]
        ClaimInvalidationCallbacks()
    {
        invalidationCallbacksClaimed = true;
        NativeRemoteWindowSourceInvalidationRegistration[] registrations =
            invalidationRegistrations.ToArray();
        invalidationRegistrations.Clear();
        return registrations;
    }

    private void DrainInvalidationCallbacks(
        NativeRemoteWindowSourceInvalidationRegistration[] registrations)
    {
        try
        {
            foreach (
                NativeRemoteWindowSourceInvalidationRegistration registration in
                registrations)
            {
                Action? callback;
                object? callbackToken;
                lock (gate)
                {
                    if (!registration.TryBeginCallback(
                            out callback,
                            out callbackToken)
                        || callback is null
                        || callbackToken is null)
                    {
                        continue;
                    }

                    activeInvalidationCallbackToken = callbackToken;
                }

                using NativeRemoteWindowDrainActivityScope callbackScope =
                    NativeRemoteWindowDrainActivityScope.Enter(
                        this,
                        callbackToken);
                try
                {
                    callback();
                }
                catch (Exception)
                {
                }
                finally
                {
                    lock (gate)
                    {
                        registration.CompleteCallback(callbackToken);
                        if (ReferenceEquals(
                                activeInvalidationCallbackToken,
                                callbackToken))
                        {
                            activeInvalidationCallbackToken = null;
                        }

                        Monitor.PulseAll(gate);
                    }
                }
            }
        }
        finally
        {
            lock (gate)
            {
                invalidationDraining = false;
                Monitor.PulseAll(gate);
            }

            invalidationDrained(this);
        }
    }

    private void WaitForInvalidationDrain()
    {
        if (NativeRemoteWindowDrainActivityScope.IsActiveFor(
                this,
                activeInvalidationCallbackToken))
        {
            return;
        }

        while (invalidationDraining)
        {
            WaitForInvalidationProgress();
        }
    }

    private void WaitForInvalidationProgress()
    {
        invalidationDrainWaiters++;
        try
        {
            Monitor.Wait(gate);
        }
        finally
        {
            invalidationDrainWaiters--;
        }
    }

    public bool TryUpdate(NativeRemoteWindowSourceMetadata metadata)
    {
        lock (gate)
        {
            if (Volatile.Read(ref current) == 0
                || NativeRemoteWindowDrainActivityScope.IsActiveForOwner(this)
                || (activeUseScopes > 0
                    && NativeRemoteWindowDrainActivityScope.HasActiveAncestry()))
            {
                return false;
            }

            useAdmissionBlocked = true;
            try
            {
                while (activeUseScopes > 0 && Volatile.Read(ref current) != 0)
                {
                    Monitor.Wait(gate);
                }

                if (Volatile.Read(ref current) == 0)
                {
                    return false;
                }

                NativeRemoteWindowSourceSnapshot currentSnapshot = snapshot;
                long geometryRevision = currentSnapshot.GeometryRevision;
                if (currentSnapshot.Metadata.Geometry != metadata.Geometry)
                {
                    geometryRevision = checked(geometryRevision + 1);
                }

                Volatile.Write(
                    ref snapshot,
                    new NativeRemoteWindowSourceSnapshot(
                        currentSnapshot.Token,
                        currentSnapshot.Source.WithDisplayName(
                            metadata.DisplayName),
                        metadata,
                        geometryRevision));
                return true;
            }
            finally
            {
                if (Volatile.Read(ref current) != 0)
                {
                    useAdmissionBlocked = false;
                }

                Monitor.PulseAll(gate);
            }
        }
    }

}

public sealed record NativeRemoteWindowGeometry
{
    public const double MaximumCoordinateMagnitude = 1_000_000;
    public const double MaximumDimension = 65_536;
    public const double MinimumScaleFactor = 0.125;
    public const double MaximumScaleFactor = 16;

    private NativeRemoteWindowGeometry(
        double x,
        double y,
        double width,
        double height,
        double scaleFactor)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        ScaleFactor = scaleFactor;
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public double ScaleFactor { get; }

    public static NativeRemoteWindowGeometry Create(
        double x,
        double y,
        double width,
        double height,
        double scaleFactor)
    {
        if (!double.IsFinite(x)
            || !double.IsFinite(y)
            || Math.Abs(x) > MaximumCoordinateMagnitude
            || Math.Abs(y) > MaximumCoordinateMagnitude)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                "Remote Window geometry coordinates must be finite and bounded.");
        }

        if (!double.IsFinite(width)
            || !double.IsFinite(height)
            || width is <= 0 or > MaximumDimension
            || height is <= 0 or > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Remote Window geometry dimensions must be positive, finite, and bounded.");
        }

        if (!double.IsFinite(scaleFactor)
            || scaleFactor is < MinimumScaleFactor or > MaximumScaleFactor)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scaleFactor),
                "A Remote Window geometry scale factor is outside the supported bound.");
        }

        return new NativeRemoteWindowGeometry(x, y, width, height, scaleFactor);
    }
}

public sealed record NativeRemoteWindowSourceMetadata
{
    public const int MaximumApplicationNameCharacters = 120;

    private NativeRemoteWindowSourceMetadata(
        string displayName,
        string owningApplicationName,
        NativeRemoteWindowGeometry geometry,
        bool supportsCapture,
        bool supportsInput,
        ProtectionSnapshot protection)
    {
        DisplayName = displayName;
        OwningApplicationName = owningApplicationName;
        Geometry = geometry;
        SupportsCapture = supportsCapture;
        SupportsInput = supportsInput;
        Protection = protection;
    }

    public string DisplayName { get; }

    public string OwningApplicationName { get; }

    public NativeRemoteWindowGeometry Geometry { get; }

    public bool SupportsCapture { get; }

    public bool SupportsInput { get; }

    public ProtectionSnapshot Protection { get; }

    public static NativeRemoteWindowSourceMetadata Create(
        string displayName,
        string owningApplicationName,
        NativeRemoteWindowGeometry geometry,
        bool supportsCapture,
        bool supportsInput,
        ProtectionSnapshot protection)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(protection);

        string normalizedDisplayName = RemoteWindowSourceText.Normalize(
            displayName,
            nameof(displayName),
            RemoteWindowSourceReference.MaximumDisplayNameCharacters,
            "Remote Window source display name");
        string normalizedApplicationName = RemoteWindowSourceText.Normalize(
            owningApplicationName,
            nameof(owningApplicationName),
            MaximumApplicationNameCharacters,
            "native Remote Window application name");

        return new NativeRemoteWindowSourceMetadata(
            normalizedDisplayName,
            normalizedApplicationName,
            geometry,
            supportsCapture,
            supportsInput,
            protection);
    }

    public override string ToString() =>
        $"Native Remote Window metadata (capture {SupportsCapture}, input {SupportsInput}, protection {Protection.Kind})";
}
