using Flowspan.Platform.Linux;
using Flowspan.Security;

namespace Flowspan.Desktop;

public sealed record LocalIdentitySnapshot(
    string DisplayName,
    string DeviceId,
    string Fingerprint,
    string ProtectionLabel,
    bool IsTestMode);

public sealed record DesktopStartupFailure(
    string ReasonCode,
    string Summary,
    string RecoveryAction);

public interface IDesktopIdentityStartup : IDisposable
{
    public ValueTask<LocalIdentitySnapshot> InitializeAsync(
        CancellationToken cancellationToken = default);
}

public sealed class DesktopIdentityStartup : IDesktopIdentityStartup
{
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly IDeviceIdentityStore store;
    private readonly string displayName;
    private DeviceIdentity? identity;
    private bool disposed;

    public DesktopIdentityStartup(IDeviceIdentityStore store, string displayName)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        this.store = store;
        this.displayName = displayName.Trim();
    }

    public async ValueTask<LocalIdentitySnapshot> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            DeviceIdentity current = await GetOrCreateCoreAsync(cancellationToken)
                .ConfigureAwait(false);

            bool isTestMode = store.Protection == SecretStoreProtection.DegradedTestOnly;
            return new LocalIdentitySnapshot(
                current.DisplayName,
                current.DeviceId.ToString(),
                current.PublicIdentity.Fingerprint,
                isTestMode
                    ? "TEST MODE — identity is not persisted"
                    : "Operating-system protected",
                isTestMode);
        }
        finally
        {
            initializationGate.Release();
        }
    }

    internal async ValueTask<DeviceIdentity> GetRuntimeIdentityAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return await GetOrCreateCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            initializationGate.Release();
        }
    }

    private async ValueTask<DeviceIdentity> GetOrCreateCoreAsync(
        CancellationToken cancellationToken)
    {
        identity ??= await DeviceIdentityProvisioner.LoadOrCreateAsync(
            store,
            displayName,
            cancellationToken).ConfigureAwait(false);
        return identity;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        identity?.Dispose();
        identity = null;
        if (store is IDisposable disposableStore)
        {
            disposableStore.Dispose();
        }

        initializationGate.Dispose();
    }

    public static DesktopStartupFailure DescribeFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            LinuxSecretServiceException linuxFailure => new DesktopStartupFailure(
                "identity.linux_secret_service_unavailable",
                "The local identity could not be opened from Secret Service.",
                linuxFailure.RecoveryAction),
            PlatformNotSupportedException => new DesktopStartupFailure(
                "identity.platform_unsupported",
                "This operating system is outside the Flowspan v1 desktop scope.",
                "Run Flowspan on Windows, macOS, or Linux."),
            UnauthorizedAccessException => CredentialStoreFailure(),
            IOException => CredentialStoreFailure(),
            System.Security.Cryptography.CryptographicException =>
                CredentialStoreFailure(),
            _ => new DesktopStartupFailure(
                "identity.initialization_failed",
                "The local identity could not be initialized safely.",
                "Check the operating-system credential store and restart Flowspan."),
        };

        static DesktopStartupFailure CredentialStoreFailure() => new(
            "identity.credential_store_unavailable",
            "The operating-system credential store is unavailable.",
            "Unlock the credential store, verify this user can access it, and retry.");
    }
}
