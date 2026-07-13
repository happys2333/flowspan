using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using Flowspan.Security;

namespace Flowspan.Platform.Linux;

public interface ISecretToolProcessRunner
{
    public ValueTask<SecretToolProcessResult> RunAsync(
        SecretToolInvocation invocation,
        CancellationToken cancellationToken = default);
}

public sealed class SecretToolInvocation
{
    public const int DefaultMaximumStandardOutputBytes = 4 * 1024;
    public const int MaximumAllowedStandardOutputBytes = 128 * 1024;

    internal SecretToolInvocation(
        string verb,
        IEnumerable<string> arguments,
        ReadOnlyMemory<byte> standardInput,
        int maximumStandardOutputBytes = DefaultMaximumStandardOutputBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);
        ArgumentNullException.ThrowIfNull(arguments);
        string[] materializedArguments = arguments.ToArray();
        if (materializedArguments.Length > 32
            || materializedArguments.Any(static argument =>
                string.IsNullOrEmpty(argument)
                || argument.Length > 256
                || argument.Contains('\0')))
        {
            throw new ArgumentException(
                "A secret-tool invocation contains invalid arguments.",
                nameof(arguments));
        }

        if (maximumStandardOutputBytes is < 1 or > MaximumAllowedStandardOutputBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumStandardOutputBytes),
                $"secret-tool stdout must be limited to 1 to {MaximumAllowedStandardOutputBytes} bytes.");
        }

        Verb = verb;
        Arguments = materializedArguments;
        StandardInput = standardInput;
        MaximumStandardOutputBytes = maximumStandardOutputBytes;
    }

    public IReadOnlyList<string> Arguments { get; }

    public ReadOnlyMemory<byte> StandardInput { get; }

    public int MaximumStandardOutputBytes { get; }

    public string Verb { get; }
}

public sealed class SecretToolProcessResult : IDisposable
{
    private bool disposed;

    public SecretToolProcessResult(
        int exitCode,
        byte[] standardOutput,
        byte[] standardError)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public int ExitCode { get; }

    public byte[] StandardError { get; }

    public byte[] StandardOutput { get; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(StandardOutput);
        CryptographicOperations.ZeroMemory(StandardError);
        disposed = true;
    }
}

public sealed class LinuxSecretServiceException : IOException
{
    internal LinuxSecretServiceException(
        string operation,
        int? exitCode,
        Exception? innerException = null)
        : base(exitCode is null
            ? $"Linux Secret Service {operation} failed before an exit code was available."
            : $"Linux Secret Service {operation} failed with exit code {exitCode}.",
            innerException)
    {
        ExitCode = exitCode;
        Operation = operation;
        RecoveryAction = operation switch
        {
            "start" => "Install secret-tool/libsecret and retry.",
            "timeout" =>
                "Check the desktop session bus and unlock prompts, then retry.",
            _ =>
                "Start and unlock a Secret Service provider, verify the desktop session bus, and retry.",
        };
    }

    public int? ExitCode { get; }

    public string Operation { get; }

    public string RecoveryAction { get; }
}

public sealed class LinuxDeviceIdentityStore : IDeviceIdentityStore
{
    public const string DefaultAccount = "primary-device";
    private readonly PayloadBackedDeviceIdentityStore inner;

    public LinuxDeviceIdentityStore()
        : this(
            new SecretToolProcessRunner(),
            GetDefaultCoordinationLockPath(),
            DefaultAccount)
    {
    }

    public LinuxDeviceIdentityStore(
        ISecretToolProcessRunner runner,
        string coordinationLockPath,
        string account = DefaultAccount)
    {
        inner = new PayloadBackedDeviceIdentityStore(
            new SecretToolIdentityPayloadStore(
                runner,
                coordinationLockPath,
                account));
    }

    public SecretStoreProtection Protection =>
        SecretStoreProtection.OperatingSystemProtected;

    public static string GetDefaultCoordinationLockPath()
    {
        string? runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtimeDirectory)
            && Path.IsPathFullyQualified(runtimeDirectory))
        {
            return Path.Combine(
                runtimeDirectory,
                "flowspan",
                "device-identity-secret-tool.lock");
        }

        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "The current user has no runtime or LocalApplicationData directory.");
        }

        return Path.GetFullPath(Path.Combine(
            localApplicationData,
            "Flowspan",
            "Security",
            "device-identity-secret-tool.lock"));
    }

    public ValueTask<bool> DeleteAsync(
        CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(cancellationToken);

    public ValueTask<DeviceIdentity?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask<bool> TrySaveNewAsync(
        DeviceIdentity identity,
        CancellationToken cancellationToken = default) =>
        inner.TrySaveNewAsync(identity, cancellationToken);
}

public sealed class LinuxTrustPayloadStore : ITrustPayloadStore
{
    public const string DefaultAccount = "trust-snapshot";
    private readonly SecretToolProtectedPayloadStore inner;

    public LinuxTrustPayloadStore()
        : this(
            new SecretToolProcessRunner(),
            GetDefaultCoordinationLockPath(),
            DefaultAccount)
    {
    }

    public LinuxTrustPayloadStore(
        ISecretToolProcessRunner runner,
        string coordinationLockPath,
        string account = DefaultAccount)
    {
        inner = new SecretToolProtectedPayloadStore(
            runner,
            coordinationLockPath,
            account,
            "trust",
            "Flowspan trust repository",
            TrustStorePayloadCodec.MaximumPayloadBytes);
    }

    public SecretStoreProtection Protection =>
        SecretStoreProtection.OperatingSystemProtected;

    public static string GetDefaultCoordinationLockPath()
    {
        string? runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtimeDirectory)
            && Path.IsPathFullyQualified(runtimeDirectory))
        {
            return Path.Combine(
                runtimeDirectory,
                "flowspan",
                "trust-secret-tool.lock");
        }

        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "The current user has no runtime or LocalApplicationData directory.");
        }

        return Path.GetFullPath(Path.Combine(
            localApplicationData,
            "Flowspan",
            "Security",
            "trust-secret-tool.lock"));
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask SaveAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.SaveReplacingAsync(payload, cancellationToken);
}

internal sealed class SecretToolIdentityPayloadStore : IDeviceIdentityPayloadStore
{
    private readonly SecretToolProtectedPayloadStore inner;

    public SecretToolIdentityPayloadStore(
        ISecretToolProcessRunner runner,
        string coordinationLockPath,
        string account)
    {
        inner = new SecretToolProtectedPayloadStore(
            runner,
            coordinationLockPath,
            account,
            "device-identity",
            "Flowspan device identity",
            DeviceIdentityPayloadCodec.MaximumPayloadBytes);
    }

    public SecretStoreProtection Protection =>
        SecretStoreProtection.OperatingSystemProtected;

    public ValueTask<bool> DeleteAsync(
        CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(cancellationToken);

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask<bool> TrySaveNewAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        inner.TrySaveNewAsync(payload, cancellationToken);
}

internal sealed class SecretToolProtectedPayloadStore
{
    private const int CoordinationLockAttempts = 500;
    private static readonly TimeSpan CoordinationLockRetryDelay =
        TimeSpan.FromMilliseconds(10);
    private readonly string[] attributes;
    private readonly string coordinationLockPath;
    private readonly string label;
    private readonly int maximumEncodedBytes;
    private readonly int maximumPayloadBytes;
    private readonly string payloadKind;
    private readonly ISecretToolProcessRunner runner;

    public SecretToolProtectedPayloadStore(
        ISecretToolProcessRunner runner,
        string coordinationLockPath,
        string account,
        string payloadKind,
        string label,
        int maximumPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinationLockPath);
        ValidateIdentifier(account, nameof(account));
        ValidateIdentifier(payloadKind, nameof(payloadKind));
        ValidateIdentifier(label, nameof(label));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPayloadBytes);
        int encodedBytes = Base64.GetMaxEncodedToUtf8Length(maximumPayloadBytes);
        if (encodedBytes + 2 > SecretToolInvocation.MaximumAllowedStandardOutputBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPayloadBytes),
                "The payload's bounded Base64 form exceeds the secret-tool output limit.");
        }

        this.runner = runner;
        this.coordinationLockPath = Path.GetFullPath(coordinationLockPath);
        this.payloadKind = payloadKind;
        this.label = label;
        this.maximumPayloadBytes = maximumPayloadBytes;
        maximumEncodedBytes = encodedBytes;
        attributes =
        [
            "application",
            "flowspan",
            "kind",
            payloadKind,
            "account",
            account,
        ];
    }

    public async ValueTask<bool> DeleteAsync(
        CancellationToken cancellationToken = default)
    {
        await using FileStream coordinationLock = await AcquireLockAsync(
            cancellationToken).ConfigureAwait(false);
        byte[]? existing = await LookupAsync(cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        CryptographicOperations.ZeroMemory(existing);
        using SecretToolProcessResult result = await runner.RunAsync(
            new SecretToolInvocation("clear", attributes, ReadOnlyMemory<byte>.Empty),
            cancellationToken).ConfigureAwait(false);
        RequireSuccess(result, "clear");
        return true;
    }

    public ValueTask<byte[]?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        LookupAsync(cancellationToken);

    public async ValueTask SaveReplacingAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ValidatePayload(payload);
        await using FileStream coordinationLock = await AcquireLockAsync(
            cancellationToken).ConfigureAwait(false);
        await StoreAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> TrySaveNewAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ValidatePayload(payload);
        await using FileStream coordinationLock = await AcquireLockAsync(
            cancellationToken).ConfigureAwait(false);
        byte[]? existing = await LookupAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            CryptographicOperations.ZeroMemory(existing);
            return false;
        }

        await StoreAsync(payload, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<FileStream> AcquireLockAsync(
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(coordinationLockPath)
            ?? throw new InvalidOperationException(
                "The Secret Service coordination lock has no parent directory.");
        Directory.CreateDirectory(directory);
        IOException? lastFailure = null;
        for (int attempt = 0; attempt < CoordinationLockAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    coordinationLockPath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.OpenOrCreate,
                        Access = FileAccess.ReadWrite,
                        Share = FileShare.None,
                        Options = FileOptions.Asynchronous,
                    });
            }
            catch (IOException exception)
            {
                lastFailure = exception;
            }

            await Task.Delay(CoordinationLockRetryDelay, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new IOException(
            $"Timed out waiting for exclusive access to the Linux {payloadKind} store.",
            lastFailure);
    }

    private async ValueTask<byte[]?> LookupAsync(
        CancellationToken cancellationToken)
    {
        using SecretToolProcessResult result = await runner.RunAsync(
            new SecretToolInvocation(
                "lookup",
                attributes,
                ReadOnlyMemory<byte>.Empty,
                maximumEncodedBytes + 2),
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 1
            && result.StandardOutput.Length == 0
            && result.StandardError.Length == 0)
        {
            return null;
        }

        RequireSuccess(result, "lookup");
        int encodedLength = result.StandardOutput.Length;
        if (encodedLength > 0 && result.StandardOutput[encodedLength - 1] == (byte)'\n')
        {
            encodedLength--;
            if (encodedLength > 0
                && result.StandardOutput[encodedLength - 1] == (byte)'\r')
            {
                encodedLength--;
            }
        }

        if (encodedLength is < 1 || encodedLength > maximumEncodedBytes)
        {
            throw new InvalidDataException(
                $"The Secret Service {payloadKind} value has an invalid encoded length.");
        }

        OperationStatus status = Base64.DecodeFromUtf8InPlace(
            result.StandardOutput.AsSpan(0, encodedLength),
            out int bytesWritten);
        if (status != OperationStatus.Done
            || bytesWritten is < 1
            || bytesWritten > maximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"The Secret Service {payloadKind} value is not valid bounded Base64.");
        }

        return result.StandardOutput.AsSpan(0, bytesWritten).ToArray();
    }

    private async ValueTask StoreAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        int encodedLength = Base64.GetMaxEncodedToUtf8Length(payload.Length);
        byte[] encoded = new byte[encodedLength];
        try
        {
            OperationStatus status = Base64.EncodeToUtf8(
                payload.Span,
                encoded,
                out int consumed,
                out int written,
                isFinalBlock: true);
            if (status != OperationStatus.Done
                || consumed != payload.Length
                || written != encoded.Length)
            {
                throw new InvalidDataException(
                    $"The {payloadKind} payload could not be encoded for Secret Service.");
            }

            string[] storeArguments = [$"--label={label}", .. attributes];
            using SecretToolProcessResult result = await runner.RunAsync(
                new SecretToolInvocation("store", storeArguments, encoded),
                cancellationToken).ConfigureAwait(false);
            RequireSuccess(result, "store");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
        }
    }

    private void ValidatePayload(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > maximumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"A {payloadKind} payload must contain 1 to {maximumPayloadBytes} bytes.");
        }
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 200 || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A Secret Service identifier must contain 1 to 200 non-control characters.",
                parameterName);
        }
    }

    private static void RequireSuccess(
        SecretToolProcessResult result,
        string operation)
    {
        if (result.ExitCode != 0)
        {
            throw new LinuxSecretServiceException(operation, result.ExitCode);
        }
    }
}
