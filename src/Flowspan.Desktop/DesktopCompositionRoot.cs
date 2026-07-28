using System.Text;
using Flowspan.Application;
using Flowspan.Platform.Linux;
using Flowspan.Platform.MacOS;
using Flowspan.Platform.Windows;
using Flowspan.Security;

namespace Flowspan.Desktop;

public static class DesktopCompositionRoot
{
    public static WorkspaceShellViewModel CreateProduction()
    {
        string displayName = GetLocalDisplayName();
        IDeviceIdentityStore store = CreatePlatformIdentityStore();
        ITrustPayloadStore trustStore = CreatePlatformTrustPayloadStore();
        var identityStartup = new DesktopIdentityStartup(store, displayName);
        var pairingDecisions = new DesktopPairingDecisionSource();
        var trustAuthority = new PersistentDesktopTrustAuthority(trustStore);
        DesktopLocalPairingRuntime? localPairingRuntime = null;
        var localDataRuntime = new DesktopLocalDataRuntime(
            CreatePlatformOperationHistoryStatePayloadStore(),
            trustAuthority,
            () => localPairingRuntime?.GetTrustedPeerConnections() ?? []);
        var activityRuntime = new DesktopActivityRuntime(
            identityStartup.GetRuntimeIdentityAsync,
            trustAuthority.GetRuntimeCoordinatorAsync,
            replaceStatePayloadStore: CreatePlatformReplaceStatePayloadStore(),
            sceneRemoteChildStatePayloadStore:
                CreatePlatformSceneRemoteChildStatePayloadStore(),
            sceneApplyStatePayloadStore:
                CreatePlatformSceneApplyStatePayloadStore(),
            receiptSink: localDataRuntime);
        localPairingRuntime = new DesktopLocalPairingRuntime(
            new SystemDesktopLocalPairingNetworkFactory(
                identityStartup,
                trustAuthority,
                pairingDecisions,
                activityRuntime));
        var sceneRepositoryRuntime = new DesktopSceneRepositoryRuntime(
            CreatePlatformSceneRepositoryStatePayloadStore());
        return new WorkspaceShellViewModel(
            identityStartup,
            pairingDecisions,
            AvaloniaDesktopUiDispatcher.Instance,
            trustAuthority,
            localPairingRuntime,
            DesktopLocalNetworkPermissionGuide.ForCurrentPlatform(),
            activityRuntime,
            sceneRepositoryService: sceneRepositoryRuntime,
            localDataService: localDataRuntime);
    }

    public static WorkspaceShellViewModel CreateValidation() =>
        new(new DesktopIdentityStartup(
            new InMemoryDeviceIdentityStore(),
            "Flowspan CI validation device"),
            new DesktopPairingDecisionSource(),
            InlineDesktopUiDispatcher.Instance,
            new DesktopTrustAuthority(new InMemoryTrustStore()));

    private static IDeviceIdentityStore CreatePlatformIdentityStore()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsDeviceIdentityStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSDeviceIdentityStore();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxDeviceIdentityStore();
        }

        return new UnsupportedDeviceIdentityStore();
    }

    private static ITrustPayloadStore CreatePlatformTrustPayloadStore()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsTrustPayloadStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSTrustPayloadStore();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxTrustPayloadStore();
        }

        return new UnsupportedTrustPayloadStore();
    }

    private static IReplaceStatePayloadStore CreatePlatformReplaceStatePayloadStore()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsReplaceStatePayloadStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSReplaceStatePayloadStore();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxReplaceStatePayloadStore();
        }

        return new UnsupportedReplaceStatePayloadStore();
    }

    private static ISceneRemoteChildStatePayloadStore
        CreatePlatformSceneRemoteChildStatePayloadStore()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsSceneRemoteChildStatePayloadStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSSceneRemoteChildStatePayloadStore();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxSceneRemoteChildStatePayloadStore();
        }

        return new UnsupportedSceneRemoteChildStatePayloadStore();
    }

    private static ISceneApplyStatePayloadStore
        CreatePlatformSceneApplyStatePayloadStore()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsSceneApplyStatePayloadStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSSceneApplyStatePayloadStore();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxSceneApplyStatePayloadStore();
        }

        return new UnsupportedSceneApplyStatePayloadStore();
    }

    private static ISceneRepositoryStatePayloadStore
        CreatePlatformSceneRepositoryStatePayloadStore()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsSceneRepositoryStatePayloadStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSSceneRepositoryStatePayloadStore();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxSceneRepositoryStatePayloadStore();
        }

        return new UnsupportedSceneRepositoryStatePayloadStore();
    }

    private static IOperationHistoryStatePayloadStore
        CreatePlatformOperationHistoryStatePayloadStore()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsOperationHistoryStatePayloadStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSOperationHistoryStatePayloadStore();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxOperationHistoryStatePayloadStore();
        }

        return new UnsupportedOperationHistoryStatePayloadStore();
    }

    private static string GetLocalDisplayName()
    {
        string candidate = Environment.MachineName.Normalize(NormalizationForm.FormC);
        char[] characters = candidate
            .Where(static character => !char.IsControl(character))
            .Take(80)
            .ToArray();
        return characters.Length == 0 ? "Flowspan device" : new string(characters);
    }

    private sealed class UnsupportedDeviceIdentityStore : IDeviceIdentityStore
    {
        public SecretStoreProtection Protection =>
            SecretStoreProtection.OperatingSystemProtected;

        public ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromException<bool>(CreateException());

        public ValueTask<DeviceIdentity?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<DeviceIdentity?>(CreateException());

        public ValueTask<bool> TrySaveNewAsync(
            DeviceIdentity identity,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<bool>(CreateException());

        private static PlatformNotSupportedException CreateException() =>
            new("The desktop shell supports Windows, macOS, and Linux only.");
    }

    private sealed class UnsupportedTrustPayloadStore : ITrustPayloadStore
    {
        public SecretStoreProtection Protection =>
            SecretStoreProtection.OperatingSystemProtected;

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<byte[]?>(CreateException());

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(CreateException());

        private static PlatformNotSupportedException CreateException() =>
            new("The desktop shell supports Windows, macOS, and Linux only.");
    }

    private sealed class UnsupportedReplaceStatePayloadStore : IReplaceStatePayloadStore
    {
        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<byte[]?>(CreateException());

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(CreateException());

        private static PlatformNotSupportedException CreateException() =>
            new("Protected Replace state supports Windows, macOS, and Linux only.");
    }

    private sealed class UnsupportedSceneRemoteChildStatePayloadStore :
        ISceneRemoteChildStatePayloadStore
    {
        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<byte[]?>(CreateException());

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(CreateException());

        private static PlatformNotSupportedException CreateException() =>
            new(
                "Protected Scene remote child state supports Windows, macOS, and Linux only.");
    }

    private sealed class UnsupportedSceneApplyStatePayloadStore :
        ISceneApplyStatePayloadStore
    {
        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<byte[]?>(CreateException());

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(CreateException());

        private static PlatformNotSupportedException CreateException() =>
            new(
                "Protected Scene Apply state supports Windows, macOS, and Linux only.");
    }

    private sealed class UnsupportedSceneRepositoryStatePayloadStore :
        ISceneRepositoryStatePayloadStore
    {
        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<byte[]?>(CreateException());

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(CreateException());

        private static PlatformNotSupportedException CreateException() =>
            new(
                "Protected Scene repository state supports Windows, macOS, and Linux only.");
    }

    private sealed class UnsupportedOperationHistoryStatePayloadStore :
        IOperationHistoryStatePayloadStore
    {
        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<byte[]?>(CreateException());

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(CreateException());

        private static PlatformNotSupportedException CreateException() =>
            new(
                "Protected operation history supports Windows, macOS, and Linux only.");
    }
}
