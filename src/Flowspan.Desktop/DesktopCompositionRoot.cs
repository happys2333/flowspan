using System.Text;
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
        return new WorkspaceShellViewModel(
            new DesktopIdentityStartup(store, displayName));
    }

    public static WorkspaceShellViewModel CreateValidation() =>
        new(new DesktopIdentityStartup(
            new InMemoryDeviceIdentityStore(),
            "Flowspan CI validation device"));

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
}
