namespace Flowspan.Desktop;

public enum DesktopPermissionState
{
    NotDetermined,
    Granted,
    Denied,
    Revoked,
    Unsupported,
    Unavailable,
}

public sealed record DesktopRemoteWindowPermissionSnapshot(
    DesktopPermissionState Capture,
    DesktopPermissionState Input);

public interface IDesktopRemoteWindowPermissionService : IAsyncDisposable
{
    public event Action? Changed;

    /// <summary>
    /// Returns the latest permission fact without prompting the user. Implementations
    /// must keep this read bounded; native permission prompts belong only in the
    /// explicit request methods.
    /// </summary>
    public DesktopRemoteWindowPermissionSnapshot GetSnapshot();

    /// <summary>
    /// Tries to read an atomically published, prompt-free snapshot for synchronous
    /// safety handling. Implementations that publish <see cref="Changed"/> should
    /// override this when their authoritative platform read can block.
    /// </summary>
    public bool TryGetCachedSnapshot(
        out DesktopRemoteWindowPermissionSnapshot snapshot)
    {
        snapshot = default!;
        return false;
    }

    public ValueTask<DesktopRemoteWindowPermissionSnapshot>
        RequestCapturePermissionAsync(CancellationToken cancellationToken);

    public ValueTask<DesktopRemoteWindowPermissionSnapshot>
        RequestInputPermissionAsync(CancellationToken cancellationToken);
}

internal sealed class UnavailableDesktopRemoteWindowPermissionService :
    IDesktopRemoteWindowPermissionService
{
    private static readonly DesktopRemoteWindowPermissionSnapshot Snapshot = new(
        DesktopPermissionState.Unsupported,
        DesktopPermissionState.Unsupported);

    private UnavailableDesktopRemoteWindowPermissionService()
    {
    }

    public static UnavailableDesktopRemoteWindowPermissionService Instance { get; } =
        new();

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public DesktopRemoteWindowPermissionSnapshot GetSnapshot() => Snapshot;

    public bool TryGetCachedSnapshot(
        out DesktopRemoteWindowPermissionSnapshot snapshot)
    {
        snapshot = Snapshot;
        return true;
    }

    public ValueTask<DesktopRemoteWindowPermissionSnapshot>
        RequestCapturePermissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Snapshot);
    }

    public ValueTask<DesktopRemoteWindowPermissionSnapshot>
        RequestInputPermissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Snapshot);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
