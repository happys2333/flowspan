using System.Globalization;

namespace Flowspan.Linux.CaptureProbe;

internal static class ProbeOutput
{
    public const int MaximumVersionBytes = 64;

    public static bool IsValidVersion(string value)
    {
        if (value.Length is < 1 or > MaximumVersionBytes)
        {
            return false;
        }

        ReadOnlySpan<char> all = value;
        int suffixIndex = all.IndexOfAny('-', '+');
        ReadOnlySpan<char> numeric = suffixIndex < 0 ? all : all[..suffixIndex];
        if (suffixIndex >= 0)
        {
            ReadOnlySpan<char> suffix = all[(suffixIndex + 1)..];
            if (suffix.Length is < 1 or > 32)
            {
                return false;
            }

            foreach (char character in suffix)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-')
                {
                    return false;
                }
            }
        }

        for (int index = 0; index < 3; index++)
        {
            int separator = numeric.IndexOf('.');
            if ((index < 2 && separator < 0) || (index == 2 && separator >= 0))
            {
                return false;
            }

            ReadOnlySpan<char> part = separator < 0 ? numeric : numeric[..separator];
            if (part.Length is < 1 or > 9
                || !uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }

            numeric = separator < 0 ? [] : numeric[(separator + 1)..];
        }

        return true;
    }

    public static string Format(ProbeResult result)
    {
        string status = result.Status switch
        {
            ProbeStatus.Pass => "pass",
            ProbeStatus.Skip => "skip",
            _ => "fail",
        };
        string reason = result.Reason switch
        {
            ProbeReason.None => "none",
            ProbeReason.UnsupportedHost => "requires_linux_ordinary_x64_or_arm64",
            ProbeReason.LibraryMissing => "libpipewire_missing",
            ProbeReason.SymbolMissing => "required_symbol_missing",
            ProbeReason.InvalidVersion => "library_version_invalid",
            ProbeReason.AllocationFailed => "thread_loop_allocation_failed",
            ProbeReason.UnderlyingLoopMissing => "underlying_loop_missing",
            ProbeReason.StartFailed => "thread_loop_start_failed",
            ProbeReason.InThreadInvalid => "outside_thread_fact_invalid",
            ProbeReason.TimeFailed => "thread_loop_get_time_failed",
            ProbeReason.TimeInvalid => "native_time_observation_invalid",
            ProbeReason.ManagedFailure => "managed_failure",
            ProbeReason.CleanupUnconfirmed => "native_cleanup_unconfirmed",
            ProbeReason.WorkerTimeout => "native_worker_timeout",
            _ => "unknown_failure",
        };
        string version = IsValidVersion(result.LibraryVersion) ? result.LibraryVersion : "unknown";
        string nativeExecuted = result.Reason == ProbeReason.WorkerTimeout
            ? "unconfirmed"
            : result.NativeCallsExecuted ? "true" : "false";
        string ownersReleased = result.OwnersReleased ? "true" : "false";
        string facts = result.Status == ProbeStatus.Pass
            ? " loop_roundtrip=true lock_roundtrips=2 native_timespec_bytes=16 native_time_valid=true outside_loop_thread=true"
            : string.Empty;
        return $"probe={status} mode=thread_loop_abi reason={reason} library_version={version}"
            + $" native_result={result.NativeResult.ToString(CultureInfo.InvariantCulture)} native_calls_executed={nativeExecuted} owners_released={ownersReleased}"
            + facts
            + " daemon_connections=0 portal_calls=0 stream_creations=0 hardware_operations=0"
            + " window_capture_executed=false permissions_requested=0 pixel_reads=0 pixel_writes=0";
    }
}
