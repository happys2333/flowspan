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
    internal SecretToolInvocation(
        string verb,
        IEnumerable<string> arguments,
        ReadOnlyMemory<byte> standardInput)
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

        Verb = verb;
        Arguments = materializedArguments;
        StandardInput = standardInput;
    }

    public IReadOnlyList<string> Arguments { get; }

    public ReadOnlyMemory<byte> StandardInput { get; }

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

    public SecretStoreProtection Protection => inner.Protection;

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

internal sealed class SecretToolIdentityPayloadStore : IDeviceIdentityPayloadStore
{
    private const int CoordinationLockAttempts = 500;
    private static readonly TimeSpan CoordinationLockRetryDelay =
        TimeSpan.FromMilliseconds(10);
    private readonly string[] attributes;
    private readonly string coordinationLockPath;
    private readonly ISecretToolProcessRunner runner;

    public SecretToolIdentityPayloadStore(
        ISecretToolProcessRunner runner,
        string coordinationLockPath,
        string account)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinationLockPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        if (account.Length > 200 || account.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A Secret Service account must contain 1 to 200 non-control characters.",
                nameof(account));
        }

        this.runner = runner;
        this.coordinationLockPath = Path.GetFullPath(coordinationLockPath);
        attributes =
        [
            "application",
            "flowspan",
            "kind",
            "device-identity",
            "account",
            account,
        ];
    }

    public SecretStoreProtection Protection =>
        SecretStoreProtection.OperatingSystemProtected;

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

    public async ValueTask<bool> TrySaveNewAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (payload.IsEmpty || payload.Length > DeviceIdentityPayloadCodec.MaximumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"An identity payload must contain 1 to {DeviceIdentityPayloadCodec.MaximumPayloadBytes} bytes.");
        }

        await using FileStream coordinationLock = await AcquireLockAsync(
            cancellationToken).ConfigureAwait(false);
        byte[]? existing = await LookupAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            CryptographicOperations.ZeroMemory(existing);
            return false;
        }

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
                    "The identity payload could not be encoded for Secret Service.");
            }

            string[] storeArguments = ["--label=Flowspan device identity", .. attributes];
            using SecretToolProcessResult result = await runner.RunAsync(
                new SecretToolInvocation("store", storeArguments, encoded),
                cancellationToken).ConfigureAwait(false);
            RequireSuccess(result, "store");
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
        }
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
            "Timed out waiting for exclusive access to the Linux identity store.",
            lastFailure);
    }

    private async ValueTask<byte[]?> LookupAsync(
        CancellationToken cancellationToken)
    {
        using SecretToolProcessResult result = await runner.RunAsync(
            new SecretToolInvocation("lookup", attributes, ReadOnlyMemory<byte>.Empty),
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

        if (encodedLength is < 1 or > 2048)
        {
            throw new InvalidDataException(
                "The Secret Service identity value has an invalid encoded length.");
        }

        OperationStatus status = Base64.DecodeFromUtf8InPlace(
            result.StandardOutput.AsSpan(0, encodedLength),
            out int bytesWritten);
        if (status != OperationStatus.Done
            || bytesWritten is < 1 or > DeviceIdentityPayloadCodec.MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                "The Secret Service identity value is not valid bounded Base64.");
        }

        return result.StandardOutput.AsSpan(0, bytesWritten).ToArray();
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
