using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Platform.Linux;
using Flowspan.Security;

namespace Flowspan.Platform.Linux.Tests;

public sealed class LinuxDeviceIdentityStoreTests
{
    [Fact]
    public async Task SecretToolBoundaryRestoresIdentityWithoutCommandLineSecret()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-store-{Guid.NewGuid():N}");
        string lockPath = Path.Combine(directory, "identity.lock");
        try
        {
            using var runner = new FakeSecretToolRunner();
            var store = new LinuxDeviceIdentityStore(
                runner,
                lockPath,
                "test-account");
            using DeviceIdentity first = await DeviceIdentityProvisioner.LoadOrCreateAsync(
                store,
                "Linux Workstation",
                () => DeviceId.Parse("11111111-1111-1111-1111-111111111111"));

            var restartedStore = new LinuxDeviceIdentityStore(
                runner,
                lockPath,
                "test-account");
            using DeviceIdentity restarted = await restartedStore.LoadAsync()
                ?? throw new InvalidOperationException("Expected a persisted identity.");

            Assert.Equal(SecretStoreProtection.OperatingSystemProtected, store.Protection);
            Assert.Equal(first.DeviceId, restarted.DeviceId);
            Assert.Equal(
                first.PublicIdentity.Fingerprint,
                restarted.PublicIdentity.Fingerprint);
            SecretToolInvocation storeInvocation = Assert.Single(
                runner.Invocations,
                static invocation => invocation.Verb == "store");
            Assert.Equal(
                [
                    "--label=Flowspan device identity",
                    "application",
                    "flowspan",
                    "kind",
                    "device-identity",
                    "account",
                    "test-account",
                ],
                storeInvocation.Arguments);
            Assert.False(storeInvocation.StandardInput.IsEmpty);
            Assert.False(runner.StoredValueStartsWithIdentityMagic);
            Assert.All(
                runner.LastStandardInput.ToArray(),
                static value => Assert.Equal(0, value));
            Assert.True(await restartedStore.DeleteAsync());
            Assert.Null(await restartedStore.LoadAsync());
            Assert.False(await restartedStore.DeleteAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ConcurrentFirstLaunchesConvergeBeforeReplacingSecretToolStore()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-store-{Guid.NewGuid():N}");
        string lockPath = Path.Combine(directory, "identity.lock");
        try
        {
            using var runner = new FakeSecretToolRunner();
            Task<DeviceIdentity> first = DeviceIdentityProvisioner.LoadOrCreateAsync(
                new LinuxDeviceIdentityStore(runner, lockPath, "concurrent"),
                "Linux Workstation",
                () => DeviceId.Parse("11111111-1111-1111-1111-111111111111"))
                .AsTask();
            Task<DeviceIdentity> second = DeviceIdentityProvisioner.LoadOrCreateAsync(
                new LinuxDeviceIdentityStore(runner, lockPath, "concurrent"),
                "Linux Workstation",
                () => DeviceId.Parse("22222222-2222-2222-2222-222222222222"))
                .AsTask();
            DeviceIdentity[] identities = await Task.WhenAll(first, second);
            try
            {
                Assert.Equal(identities[0].DeviceId, identities[1].DeviceId);
                Assert.Equal(
                    identities[0].PublicIdentity.Fingerprint,
                    identities[1].PublicIdentity.Fingerprint);
                Assert.Equal(
                    1,
                    runner.Invocations.Count(static invocation =>
                        invocation.Verb == "store"));
            }
            finally
            {
                foreach (DeviceIdentity identity in identities)
                {
                    identity.Dispose();
                }
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MalformedSecretValueIsRejectedWithoutReplacement()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-store-{Guid.NewGuid():N}");
        try
        {
            using var runner = new FakeSecretToolRunner();
            runner.SeedBase64("not-valid-base64!"u8);
            var store = new LinuxDeviceIdentityStore(
                runner,
                Path.Combine(directory, "identity.lock"),
                "corrupt");

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync());

            Assert.DoesNotContain(
                runner.Invocations,
                static invocation => invocation.Verb == "store");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SecretToolErrorIsNotMisreportedAsMissingIdentity()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-store-{Guid.NewGuid():N}");
        try
        {
            var runner = new ErrorSecretToolRunner();
            var store = new LinuxDeviceIdentityStore(
                runner,
                Path.Combine(directory, "identity.lock"),
                "backend-error");

            LinuxSecretServiceException exception =
                await Assert.ThrowsAsync<LinuxSecretServiceException>(async () =>
                    await store.LoadAsync());

            Assert.Equal(1, exception.ExitCode);
            Assert.Equal("lookup", exception.Operation);
            Assert.NotEmpty(exception.RecoveryAction);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProductionStoreRejectsUseOutsideLinux()
    {
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        var store = new LinuxDeviceIdentityStore();

        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
            await DeviceIdentityProvisioner.LoadOrCreateAsync(
                store,
                "Linux Workstation"));
    }

    [Fact]
    public async Task MissingSecretToolHasStructuredRecoveryOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-store-{Guid.NewGuid():N}");
        try
        {
            var runner = new SecretToolProcessRunner(
                Path.Combine(directory, "missing-secret-tool"));
            var store = new LinuxDeviceIdentityStore(
                runner,
                Path.Combine(directory, "identity.lock"),
                "missing-tool");

            LinuxSecretServiceException exception =
                await Assert.ThrowsAsync<LinuxSecretServiceException>(async () =>
                    await store.LoadAsync());

            Assert.Null(exception.ExitCode);
            Assert.Equal("start", exception.Operation);
            Assert.NotEmpty(exception.RecoveryAction);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProcessRunnerRejectsUnboundedOutputOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-runner-{Guid.NewGuid():N}");
        try
        {
            string script = await WriteExecutableScriptAsync(
                directory,
                "oversized.sh",
                "i=0\nwhile [ \"$i\" -lt 5000 ]; do printf x; i=$((i+1)); done\n");
            var store = new LinuxDeviceIdentityStore(
                new SecretToolProcessRunner(script),
                Path.Combine(directory, "identity.lock"),
                "oversized-output");

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProcessRunnerKillsOnCallerCancellationOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string directory = Path.Combine(
            Path.GetTempPath(),
            $"flowspan-linux-runner-{Guid.NewGuid():N}");
        try
        {
            string script = await WriteExecutableScriptAsync(
                directory,
                "blocking.sh",
                "sleep 30\n");
            var store = new LinuxDeviceIdentityStore(
                new SecretToolProcessRunner(script),
                Path.Combine(directory, "identity.lock"),
                "cancelled-process");
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(100));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await store.LoadAsync(cancellation.Token));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<string> WriteExecutableScriptAsync(
        string directory,
        string fileName,
        string body)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException();
        }

        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(path, $"#!/bin/sh\n{body}");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private sealed class FakeSecretToolRunner : ISecretToolProcessRunner, IDisposable
    {
        private readonly Lock gate = new();
        private byte[]? storedBase64;

        public List<SecretToolInvocation> Invocations { get; } = [];

        public ReadOnlyMemory<byte> LastStandardInput { get; private set; }

        public bool StoredValueStartsWithIdentityMagic
        {
            get
            {
                lock (gate)
                {
                    return storedBase64?.AsSpan().StartsWith("FSID"u8) ?? false;
                }
            }
        }

        public ValueTask<SecretToolProcessResult> RunAsync(
            SecretToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                Invocations.Add(invocation);
                return ValueTask.FromResult(invocation.Verb switch
                {
                    "lookup" => Lookup(),
                    "store" => Store(invocation.StandardInput),
                    "clear" => Clear(),
                    _ => throw new InvalidOperationException("Unexpected secret-tool verb."),
                });
            }
        }

        public void SeedBase64(ReadOnlySpan<byte> value)
        {
            lock (gate)
            {
                if (storedBase64 is not null)
                {
                    CryptographicOperations.ZeroMemory(storedBase64);
                }

                storedBase64 = value.ToArray();
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (storedBase64 is not null)
                {
                    CryptographicOperations.ZeroMemory(storedBase64);
                    storedBase64 = null;
                }
            }
        }

        private SecretToolProcessResult Clear()
        {
            if (storedBase64 is not null)
            {
                CryptographicOperations.ZeroMemory(storedBase64);
                storedBase64 = null;
            }

            return new SecretToolProcessResult(0, [], []);
        }

        private SecretToolProcessResult Lookup() => storedBase64 is null
            ? new SecretToolProcessResult(1, [], [])
            : new SecretToolProcessResult(
                0,
                [.. storedBase64, (byte)'\n'],
                []);

        private SecretToolProcessResult Store(ReadOnlyMemory<byte> standardInput)
        {
            LastStandardInput = standardInput;
            if (storedBase64 is not null)
            {
                CryptographicOperations.ZeroMemory(storedBase64);
            }

            storedBase64 = standardInput.ToArray();
            return new SecretToolProcessResult(0, [], []);
        }
    }

    private sealed class ErrorSecretToolRunner : ISecretToolProcessRunner
    {
        public ValueTask<SecretToolProcessResult> RunAsync(
            SecretToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new SecretToolProcessResult(
                1,
                [],
                "no session bus"u8.ToArray()));
        }
    }
}
