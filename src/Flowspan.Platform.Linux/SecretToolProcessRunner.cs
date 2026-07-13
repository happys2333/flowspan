using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Flowspan.Platform.Linux;

public sealed class SecretToolProcessRunner : ISecretToolProcessRunner
{
    private const int MaximumCapturedErrorBytes = 4 * 1024;
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);
    private readonly string executablePath;

    public SecretToolProcessRunner(string executablePath = "secret-tool")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (executablePath.Contains('\0'))
        {
            throw new ArgumentException(
                "The secret-tool executable path contains a null character.",
                nameof(executablePath));
        }

        this.executablePath = executablePath;
    }

    public async ValueTask<SecretToolProcessResult> RunAsync(
        SecretToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "secret-tool identity storage is available only on Linux.");
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(invocation),
        };
        try
        {
            if (!process.Start())
            {
                throw new LinuxSecretServiceException("start", exitCode: null);
            }
        }
        catch (Win32Exception exception)
        {
            throw new LinuxSecretServiceException(
                "start",
                exitCode: null,
                exception);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(ProcessTimeout);
        CancellationToken operationToken = timeout.Token;
        Task<byte[]> standardOutput = ReadBoundedAsync(
            process.StandardOutput.BaseStream,
            invocation.MaximumStandardOutputBytes,
            operationToken);
        Task<byte[]> standardError = ReadBoundedAsync(
            process.StandardError.BaseStream,
            MaximumCapturedErrorBytes,
            operationToken);
        Exception? inputFailure = null;
        try
        {
            try
            {
                if (!invocation.StandardInput.IsEmpty)
                {
                    await process.StandardInput.BaseStream.WriteAsync(
                        invocation.StandardInput,
                        operationToken).ConfigureAwait(false);
                    await process.StandardInput.BaseStream.FlushAsync(operationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (IOException exception)
            {
                inputFailure = exception;
            }
            finally
            {
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(operationToken).ConfigureAwait(false);
            byte[] output = await standardOutput.ConfigureAwait(false);
            byte[] error = await standardError.ConfigureAwait(false);
            if (inputFailure is not null && process.ExitCode == 0)
            {
                CryptographicOperations.ZeroMemory(output);
                CryptographicOperations.ZeroMemory(error);
                throw new IOException(
                    "secret-tool closed standard input before reading the secret.",
                    inputFailure);
            }

            return new SecretToolProcessResult(process.ExitCode, output, error);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await ClearCompletedOutputAsync(standardOutput).ConfigureAwait(false);
            await ClearCompletedOutputAsync(standardError).ConfigureAwait(false);
            throw new LinuxSecretServiceException(
                "timeout",
                exitCode: null,
                exception);
        }
        catch
        {
            TryKill(process);
            await ClearCompletedOutputAsync(standardOutput).ConfigureAwait(false);
            await ClearCompletedOutputAsync(standardError).ConfigureAwait(false);
            throw;
        }
    }

    private ProcessStartInfo CreateStartInfo(SecretToolInvocation invocation)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(invocation.Verb);
        foreach (string argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumCapturedBytes,
        CancellationToken cancellationToken)
    {
        byte[] captured = new byte[maximumCapturedBytes];
        byte[] buffer = new byte[1024];
        int capturedLength = 0;
        bool exceeded = false;
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                int available = maximumCapturedBytes - capturedLength;
                int copyLength = Math.Min(available, read);
                if (copyLength > 0)
                {
                    buffer.AsSpan(0, copyLength).CopyTo(
                        captured.AsSpan(capturedLength));
                    capturedLength += copyLength;
                }

                exceeded |= copyLength != read;
            }

            if (exceeded)
            {
                throw new InvalidDataException(
                    $"secret-tool output exceeded {maximumCapturedBytes} bytes.");
            }

            return captured.AsSpan(0, capturedLength).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(captured);
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static async Task ClearCompletedOutputAsync(Task<byte[]> outputTask)
    {
        try
        {
            byte[] output = await outputTask.ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(output);
        }
        catch
        {
            // The primary operation reports the failure; output tasks are drained here.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is
            InvalidOperationException
            or Win32Exception)
        {
            // The process exited between the status check and kill request.
        }
    }
}
