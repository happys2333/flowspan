namespace Flowspan.Platform;

internal sealed class NativeRemoteWindowDrainActivityScope : IDisposable
{
    private static readonly AsyncLocal<NativeRemoteWindowDrainActivityScope?>
        Current = new();

    private readonly object owner;
    private readonly NativeRemoteWindowDrainActivityScope? previous;
    private readonly object token;
    private int active = 1;

    private NativeRemoteWindowDrainActivityScope(object owner, object token)
    {
        this.owner = owner;
        this.token = token;
        previous = Current.Value;
        Current.Value = this;
    }

    internal static NativeRemoteWindowDrainActivityScope Enter(
        object owner,
        object token)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(token);
        return new NativeRemoteWindowDrainActivityScope(owner, token);
    }

    internal static bool HasActiveAncestry()
    {
        for (NativeRemoteWindowDrainActivityScope? scope = Current.Value;
            scope is not null;
            scope = scope.previous)
        {
            if (scope.IsActive)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsActiveFor(object owner, object? token)
    {
        if (token is null)
        {
            return false;
        }

        for (NativeRemoteWindowDrainActivityScope? scope = Current.Value;
            scope is not null;
            scope = scope.previous)
        {
            if (scope.IsActive
                && ReferenceEquals(scope.owner, owner)
                && ReferenceEquals(scope.token, token))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsActiveForOwner(object owner)
    {
        for (NativeRemoteWindowDrainActivityScope? scope = Current.Value;
            scope is not null;
            scope = scope.previous)
        {
            if (scope.IsActive && ReferenceEquals(scope.owner, owner))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsActive => Volatile.Read(ref active) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref active, 0) == 0)
        {
            return;
        }

        if (ReferenceEquals(Current.Value, this))
        {
            Current.Value = previous;
        }
    }
}
