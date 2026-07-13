using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Platform.Linux;
using Flowspan.Security;

namespace Flowspan.Platform.Linux.Tests;

public sealed class LinuxTrustPayloadStoreTests
{
    private static readonly DeviceId PeerId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void DefaultCoordinationPathIsSeparateFromIdentity()
    {
        string trust = LinuxTrustPayloadStore.GetDefaultCoordinationLockPath();
        string identity = LinuxDeviceIdentityStore.GetDefaultCoordinationLockPath();

        Assert.True(Path.IsPathFullyQualified(trust));
        Assert.NotEqual(identity, trust);
        Assert.EndsWith(
            "trust-secret-tool.lock",
            trust,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecretToolBoundaryAtomicallyReplacesTrustThroughStandardInput()
    {
        string directory = CreateTemporaryDirectoryName();
        try
        {
            using var runner = new FakeSecretToolRunner();
            var store = new LinuxTrustPayloadStore(
                runner,
                Path.Combine(directory, "trust.lock"),
                "test-trust");

            await store.SaveAsync("FSTR-first"u8.ToArray());
            await store.SaveAsync("FSTR-second"u8.ToArray());
            byte[] loaded = await store.LoadAsync()
                ?? throw new InvalidOperationException("Expected a trust payload.");

            Assert.Equal("FSTR-second"u8.ToArray(), loaded);
            Assert.Equal(2, runner.Invocations.Count(
                static invocation => invocation.Verb == "store"));
            Assert.DoesNotContain(
                runner.Invocations,
                static invocation => invocation.Verb == "clear");
            SecretToolInvocation lastStore = runner.Invocations.Last(
                static invocation => invocation.Verb == "store");
            Assert.Equal(
                [
                    "--label=Flowspan trust repository",
                    "application",
                    "flowspan",
                    "kind",
                    "trust",
                    "account",
                    "test-trust",
                ],
                lastStore.Arguments);
            Assert.False(lastStore.StandardInput.Span.StartsWith("FSTR"u8));
            SecretToolInvocation lookup = runner.Invocations.Last(
                static invocation => invocation.Verb == "lookup");
            Assert.True(
                lookup.MaximumStandardOutputBytes >
                SecretToolInvocation.DefaultMaximumStandardOutputBytes);
            Assert.All(
                runner.LastStandardInput.ToArray(),
                static value => Assert.Equal(0, value));
            Assert.Equal(
                SecretStoreProtection.OperatingSystemProtected,
                store.Protection);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task PersistentTrustRoundTripsThroughSecretToolBoundary()
    {
        string directory = CreateTemporaryDirectoryName();
        try
        {
            using DeviceIdentity identity = DeviceIdentity.Generate(PeerId, "Desk");
            using var runner = new FakeSecretToolRunner();
            var payloadStore = new LinuxTrustPayloadStore(
                runner,
                Path.Combine(directory, "trust.lock"),
                "persistent");
            using PersistentTrustStore first =
                await PersistentTrustStore.OpenAsync(payloadStore);
            await first.RegisterAsync(CreateRecord(identity));
            using PersistentTrustStore restarted =
                await PersistentTrustStore.OpenAsync(payloadStore);

            Assert.True(restarted.Allows(PeerId, Capability.MirrorView));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task CancelledContendedReplacePreservesCommittedSecret()
    {
        string directory = CreateTemporaryDirectoryName();
        string lockPath = Path.Combine(directory, "trust.lock");
        try
        {
            using var runner = new FakeSecretToolRunner();
            var store = new LinuxTrustPayloadStore(runner, lockPath, "cancelled");
            byte[] committed = "committed"u8.ToArray();
            await store.SaveAsync(committed);
            await using var heldLock = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(50));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await store.SaveAsync("replacement"u8.ToArray(), cancellation.Token));
            byte[] loaded = await store.LoadAsync()
                ?? throw new InvalidOperationException("Expected the old snapshot.");

            Assert.Equal(committed, loaded);
            Assert.Equal(1, runner.Invocations.Count(
                static invocation => invocation.Verb == "store"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ProductionStoreRejectsOtherPlatformsAndMissingToolIsStructured()
    {
        string directory = CreateTemporaryDirectoryName();
        try
        {
            if (!OperatingSystem.IsLinux())
            {
                var store = new LinuxTrustPayloadStore();
                await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
                    await store.LoadAsync());
                return;
            }

            var missing = new LinuxTrustPayloadStore(
                new SecretToolProcessRunner(Path.Combine(directory, "missing-secret-tool")),
                Path.Combine(directory, "trust.lock"),
                "missing-tool");
            LinuxSecretServiceException exception =
                await Assert.ThrowsAsync<LinuxSecretServiceException>(async () =>
                    await missing.LoadAsync());

            Assert.Equal("start", exception.Operation);
            Assert.Null(exception.ExitCode);
            Assert.NotEmpty(exception.RecoveryAction);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task TrustLookupAllowsBoundedOutputAboveIdentityLimitOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string directory = CreateTemporaryDirectoryName();
        try
        {
            string script = await WriteExecutableScriptAsync(
                directory,
                "large-valid.sh",
                "awk 'BEGIN { for (i=0; i<1667; i++) printf \"AAAA\"; printf \"\\n\" }'\n");
            var store = new LinuxTrustPayloadStore(
                new SecretToolProcessRunner(script),
                Path.Combine(directory, "trust.lock"),
                "large-valid");

            byte[] payload = await store.LoadAsync()
                ?? throw new InvalidOperationException("Expected a decoded payload.");

            Assert.Equal(5001, payload.Length);
            Assert.All(payload, static value => Assert.Equal(0, value));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task TrustLookupRejectsOutputAboveItsOwnBoundOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string directory = CreateTemporaryDirectoryName();
        try
        {
            string script = await WriteExecutableScriptAsync(
                directory,
                "too-large.sh",
                "awk 'BEGIN { for (i=0; i<90000; i++) printf \"A\" }'\n");
            var store = new LinuxTrustPayloadStore(
                new SecretToolProcessRunner(script),
                Path.Combine(directory, "trust.lock"),
                "too-large");

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static TrustRecord CreateRecord(DeviceIdentity identity) => new(
        identity.PublicIdentity,
        DateTimeOffset.UnixEpoch,
        CapabilityGrant.Of(Capability.MirrorView));

    private static string CreateTemporaryDirectoryName() => Path.Combine(
        Path.GetTempPath(),
        $"flowspan-linux-trust-{Guid.NewGuid():N}");

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

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeSecretToolRunner : ISecretToolProcessRunner, IDisposable
    {
        private byte[]? storedBase64;

        public List<SecretToolInvocation> Invocations { get; } = [];

        public ReadOnlyMemory<byte> LastStandardInput { get; private set; }

        public ValueTask<SecretToolProcessResult> RunAsync(
            SecretToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invocations.Add(invocation);
            return ValueTask.FromResult(invocation.Verb switch
            {
                "lookup" => Lookup(),
                "store" => Store(invocation.StandardInput),
                "clear" => Clear(),
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
}
