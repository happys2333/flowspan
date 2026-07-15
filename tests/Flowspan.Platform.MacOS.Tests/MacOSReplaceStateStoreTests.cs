using System.Text;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSReplaceStateStoreTests
{
    [Fact]
    public async Task KeychainKeyAndAuthenticatedFileRoundTripWithoutPlaintext()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-macos-replace-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "replace-state.fsrf");
        byte[] payload = Encoding.UTF8.GetBytes(
            "{\"descriptor\":\"MACOS-REPLACE-PLAINTEXT-CANARY\"}");
        var keychain = new FakeMacOSKeychain();
        try
        {
            var first = new MacOSReplaceStatePayloadStore(statePath, keychain);
            await first.SaveAsync(payload);

            Assert.DoesNotContain(
                "MACOS-REPLACE-PLAINTEXT-CANARY",
                Encoding.UTF8.GetString(await File.ReadAllBytesAsync(statePath)),
                StringComparison.Ordinal);
            Assert.Equal(1, keychain.AddCount);
            Assert.Equal(0, keychain.UpdateCount);

            var restarted = new MacOSReplaceStatePayloadStore(statePath, keychain);
            byte[]? restored = await restarted.LoadAsync();

            Assert.Equal(payload, restored);
            Assert.Equal(1, keychain.AddCount);
            Assert.NotEqual(
                MacOSReplaceStateKeyStore.DefaultService,
                MacOSTrustPayloadStore.DefaultService);
            Assert.NotEqual(
                MacOSReplaceStateKeyStore.DefaultService,
                MacOSDeviceIdentityStore.DefaultService);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProductionKeyStoreUsesSecurityFrameworkOnMacOSOnly()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-macos-native-replace-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "replace-state.fsrf");
        string service = $"app.flowspan.tests.replace-state.{Guid.NewGuid():N}";
        const string account = "native-key";
        var keychain = new SecurityFrameworkKeychain();
        var store = new MacOSReplaceStatePayloadStore(
            statePath,
            new MacOSReplaceStateKeyStore(keychain, service, account));
        byte[] payload = "MACOS-NATIVE-REPLACE-STATE"u8.ToArray();
        try
        {
            if (!OperatingSystem.IsMacOS())
            {
                await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
                    await store.SaveAsync(payload));
                return;
            }

            await store.SaveAsync(payload);
            byte[]? restored = await new MacOSReplaceStatePayloadStore(
                statePath,
                new MacOSReplaceStateKeyStore(keychain, service, account)).LoadAsync();
            Assert.Equal(payload, restored);
        }
        finally
        {
            if (OperatingSystem.IsMacOS())
            {
                keychain.DeleteGenericPassword(service, account);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class FakeMacOSKeychain : IMacOSKeychain
    {
        private readonly Dictionary<(string Service, string Account), byte[]> values = [];

        public int AddCount { get; private set; }

        public int UpdateCount { get; private set; }

        public bool DeleteGenericPassword(string service, string account) =>
            values.Remove((service, account));

        public byte[]? LoadGenericPassword(string service, string account) =>
            values.TryGetValue((service, account), out byte[]? value)
                ? value.ToArray()
                : null;

        public bool TryAddGenericPassword(
            string service,
            string account,
            ReadOnlyMemory<byte> value)
        {
            AddCount++;
            return values.TryAdd((service, account), value.ToArray());
        }

        public bool UpdateGenericPassword(
            string service,
            string account,
            ReadOnlyMemory<byte> value)
        {
            UpdateCount++;
            if (!values.ContainsKey((service, account)))
            {
                return false;
            }

            values[(service, account)] = value.ToArray();
            return true;
        }
    }
}
