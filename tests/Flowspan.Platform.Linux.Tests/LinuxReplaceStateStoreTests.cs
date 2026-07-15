using System.Security.Cryptography;
using System.Text;
using Flowspan.Platform.Linux;

namespace Flowspan.Platform.Linux.Tests;

public sealed class LinuxReplaceStateStoreTests
{
    [Fact]
    public async Task SecretServiceKeyAndAuthenticatedFileRoundTripWithoutPlaintext()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-replace-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "replace-state.fsrf");
        string keyLockPath = Path.Combine(directory, "replace-key.lock");
        byte[] payload = Encoding.UTF8.GetBytes(
            "{\"descriptor\":\"LINUX-REPLACE-PLAINTEXT-CANARY\"}");
        using var runner = new FakeSecretToolRunner();
        try
        {
            var first = new LinuxReplaceStatePayloadStore(
                statePath,
                runner,
                keyLockPath);
            await first.SaveAsync(payload);

            Assert.DoesNotContain(
                "LINUX-REPLACE-PLAINTEXT-CANARY",
                Encoding.UTF8.GetString(await File.ReadAllBytesAsync(statePath)),
                StringComparison.Ordinal);
            Assert.Equal(1, runner.StoreCount);
            Assert.All(runner.StoredBase64.Span.ToArray(), static value =>
                Assert.NotEqual((byte)'{', value));

            var restarted = new LinuxReplaceStatePayloadStore(
                statePath,
                runner,
                keyLockPath);
            byte[]? restored = await restarted.LoadAsync();

            Assert.Equal(payload, restored);
            Assert.Equal(1, runner.StoreCount);
            Assert.NotEqual(
                LinuxReplaceStateKeyStore.GetDefaultCoordinationLockPath(),
                LinuxTrustPayloadStore.GetDefaultCoordinationLockPath());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeSecretToolRunner : ISecretToolProcessRunner, IDisposable
    {
        private byte[]? storedBase64;

        public int StoreCount { get; private set; }

        public ReadOnlyMemory<byte> StoredBase64 => storedBase64;

        public ValueTask<SecretToolProcessResult> RunAsync(
            SecretToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(invocation.Verb switch
            {
                "lookup" => storedBase64 is null
                    ? new SecretToolProcessResult(1, [], [])
                    : new SecretToolProcessResult(
                        0,
                        [.. storedBase64, (byte)'\n'],
                        []),
                "store" => Store(invocation.StandardInput),
                _ => throw new InvalidOperationException("Unexpected secret-tool verb."),
            });
        }

        public void Dispose()
        {
            if (storedBase64 is not null)
            {
                CryptographicOperations.ZeroMemory(storedBase64);
                storedBase64 = null;
            }
        }

        private SecretToolProcessResult Store(ReadOnlyMemory<byte> standardInput)
        {
            StoreCount++;
            if (storedBase64 is not null)
            {
                CryptographicOperations.ZeroMemory(storedBase64);
            }

            storedBase64 = standardInput.ToArray();
            return new SecretToolProcessResult(0, [], []);
        }
    }
}
