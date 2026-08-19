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
                    ? DesktopText.Get("IdentityStartup_TestModeProtection")
                    : DesktopText.Get("IdentityStartup_Protected"),
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
                DesktopText.Get("IdentityStartup_LinuxSecretServiceSummary"),
                linuxFailure.Operation switch
                {
                    "start" => DesktopText.Get(
                        "IdentityStartup_LinuxSecretServiceInstallRecovery"),
                    "timeout" => DesktopText.Get(
                        "IdentityStartup_LinuxSecretServiceTimeoutRecovery"),
                    _ => DesktopText.Get(
                        "IdentityStartup_LinuxSecretServiceRecovery"),
                }),
            PlatformNotSupportedException => new DesktopStartupFailure(
                "identity.platform_unsupported",
                DesktopText.Get("IdentityStartup_PlatformUnsupportedSummary"),
                DesktopText.Get("IdentityStartup_PlatformUnsupportedRecovery")),
            UnauthorizedAccessException => CredentialStoreFailure(),
            IOException => CredentialStoreFailure(),
            System.Security.Cryptography.CryptographicException =>
                CredentialStoreFailure(),
            _ => new DesktopStartupFailure(
                "identity.initialization_failed",
                DesktopText.Get("IdentityStartup_FailedSummary"),
                DesktopText.Get("IdentityStartup_FailedRecovery")),
        };

        static DesktopStartupFailure CredentialStoreFailure() => new(
            "identity.credential_store_unavailable",
            DesktopText.Get("IdentityStartup_CredentialStoreSummary"),
            DesktopText.Get("IdentityStartup_CredentialStoreRecovery"));
    }
}
