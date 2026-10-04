using System.Reflection;
using System.Runtime.InteropServices;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Platform.MacOS;
using N = Flowspan.Platform.MacOS.MacOSRemoteWindowObjectiveCInterop;

namespace Flowspan.MacOS.NativeCaptureProbe;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || (args.Length == 1 && args[0] == "--help"))
        {
            Console.WriteLine("Usage: dotnet run --project tools/Flowspan.MacOS.NativeCaptureProbe -- --run");
            Console.WriteLine("native_capture_probe=skip; reason=explicit_run_required; preflight_called=false; AppKit_initialized=false; native_capture_executed=false; permissions_requested=0");
            return 0;
        }
        if (args.Length != 1 || args[0] != "--run")
        {
            Console.Error.WriteLine("native_capture_probe=fail; reason=unknown_arguments; preflight_called=false; native_capture_executed=false");
            return 2;
        }

        var nativeApi = MacOSRemoteWindowScreenCaptureKitApi.Instance;
        if (!nativeApi.IsSupported)
        {
            Console.WriteLine("native_capture_probe=skip; reason=requires_supported_arm64_macos; preflight_called=false; native_capture_executed=false; permissions_requested=0");
            return 0;
        }
        if (!nativeApi.PreflightCaptureAccess())
        {
            Console.WriteLine("native_capture_probe=skip; reason=permission_not_granted; preflight_called=true; AppKit_initialized=false; native_capture_executed=false; permissions_requested=0");
            return 0;
        }

        try
        {
            Validate(nativeApi);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"native_capture_probe=fail; exception={exception.GetType().Name}; check={exception.Message}; cleanup_proven=false");
            return 1;
        }
    }

    private static void Validate(MacOSRemoteWindowScreenCaptureKitApi productionApi)
    {
        Require(TaskWindow.ApplicationPointer == 0, "tool_begins_without_application");
        IReadOnlyList<IMacOSRemoteWindowNativeSource> absentSources = Await(productionApi.EnumerateAsync().AsTask());
        try
        {
            Require(absentSources.Count == 0 && TaskWindow.ApplicationPointer == 0,
                "driver_preserves_absent_application");
        }
        finally { DisposeSources(absentSources); }

        Require(CaptureRootCount() == 0, "initial_capture_roots_zero");
        Require(MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount == 0,
            "initial_retained_capture_owners_zero");
        string? result = null;
        nint pool = N.objc_autoreleasePoolPush();
        try
        {
            using var window = new TaskWindow();
            IReadOnlyList<IMacOSRemoteWindowNativeSource> productionSources = Await(productionApi.EnumerateAsync().AsTask());
            try
            {
                Require(productionSources.All(source => source.Identity.ProcessId != Environment.ProcessId),
                    "production_excludes_own_process");
            }
            finally { DisposeSources(productionSources); }

            var driver = new MacOSRemoteWindowScreenCaptureKitApi([window.WindowId]);
            var ownApi = new OwnWindowApi(driver, window.WindowId);
            IReadOnlyList<IMacOSRemoteWindowNativeSource> sources = Await(ownApi.EnumerateAsync().AsTask());
            try
            {
                Require(sources.Count == 1, "exact_task_source_enumerated");
                IMacOSRemoteWindowNativeSource source = sources[0];
                Require(source.Identity.ProcessStartSeconds > 0
                    && source.Identity.ProcessStartMicroseconds < 1_000_000,
                    "fresh_process_instance_identity");
                Require(source.Geometry.Width == 64 && source.Geometry.Height == 64
                    && ownApi.IsCurrent(source), "task_source_current_geometry");
                var raw = new RawSamples(source.Geometry);
                VerifyRawCapture(ownApi, source, raw);
                VerifyStartStopRace(ownApi, source);
                VerifyBoundary(ownApi, source.Geometry);

                int deliveryCallsAtDrain = ownApi.SampleDeliveryCalls;
                TaskWindow.Pump(TimeSpan.FromMilliseconds(600));
                Require(ownApi.SampleDeliveryCalls == deliveryCallsAtDrain, "no_late_sample_delivery_calls_after_drain");
                window.Hide();
                long visibilityDeadline = Environment.TickCount64 + 3_000;
                while (ownApi.IsCurrent(source) && Environment.TickCount64 < visibilityDeadline)
                {
                    TaskWindow.Pump(TimeSpan.FromMilliseconds(20));
                }
                Require(!ownApi.IsCurrent(source), "hidden_task_source_invalidated");
                Require(CaptureRootCount() == 0, "final_capture_roots_zero");
                Require(MacOSRemoteWindowScreenCaptureKitApi.RetainedCaptureOwnerCount == 0,
                    "final_retained_capture_owners_zero");
                result = $"native_capture_probe=pass; runtime={RuntimeInformation.FrameworkDescription}; arch={RuntimeInformation.ProcessArchitecture}; own_window_only=true; identity_guard=window_id_and_pid; fresh_process_instance=true; raw_frame={raw.Width}x{raw.Height}; scale={source.Geometry.ScaleFactor}; raw_marker_frames={raw.Matched}; catalog_boundary_frame=pass; boundary_stop_completion=pass; start_stop_race=pass; independent_drain_fact=pass; absent_application_preserved=true; own_process_excluded_by_default=true; hidden_window_invalidated=true; late_sample_delivery_calls=0; late_observation_ms=600; capture_roots=0; retained_capture_owners=0; permissions_requested=0; titles_read=0; pixels_written=0";
            }
            finally { DisposeSources(sources); }
        }
        finally { N.objc_autoreleasePoolPop(pool); }
        Require(result is not null, "probe_result_set_after_verification");
        Console.WriteLine(result);
    }

    private static void VerifyRawCapture(OwnWindowApi api,
        IMacOSRemoteWindowNativeSource source, RawSamples samples)
    {
        IMacOSRemoteWindowNativeCapture capture = api.CreateCapture(source, samples.TakeOwnership, samples.Unavailable);
        try
        {
            Require(!capture.IsDrained, "new_capture_drain_unproven");
            Require(Await(capture.StartAsync().AsTask()), "raw_capture_started");
            WaitForMarker(() => samples.Matched > 0, () => samples.Failure);
            Require(samples.UnavailableCount == 0, "raw_source_stayed_available");
            Require(Await(capture.StopAndDrainAsync().AsTask()) && capture.IsDrained,
                "raw_capture_drained");
            int deliveryCallsAtStop = api.SampleDeliveryCalls;
            TaskWindow.Pump(TimeSpan.FromMilliseconds(600));
            Require(api.SampleDeliveryCalls == deliveryCallsAtStop, "raw_no_late_sample_delivery_calls");
            Require(samples.UnavailableCount == 0, "raw_source_stayed_available_through_drain");
            if (samples.Failure is { } failure) throw failure;
            Console.WriteLine($"native_raw=pass; frame={samples.Width}x{samples.Height}; marker_frames={samples.Matched}; stop_and_drain=joined; independent_drain_fact=true; late_sample_delivery_calls=0; observation_ms=600");
        }
        finally { DrainAndDispose(capture); }
    }

    private static void VerifyStartStopRace(OwnWindowApi api,
        IMacOSRemoteWindowNativeSource source)
    {
        IMacOSRemoteWindowNativeCapture capture = api.CreateCapture(source,
            sample => sample.Dispose(), () => { });
        try
        {
            Task<bool> start = capture.StartAsync().AsTask();
            Task<bool> stop = capture.StopAndDrainAsync().AsTask();
            Require(Await(start), "racing_start_settled");
            Require(Await(stop) && capture.IsDrained, "racing_stop_joined_start_and_drained");
        }
        finally { DrainAndDispose(capture); }
    }

    private static void VerifyBoundary(OwnWindowApi api, NativeRemoteWindowGeometry geometry)
    {
        var catalog = new MacOSRemoteWindowSourceCatalog(DeviceId.From(Guid.NewGuid()), api);
        var boundary = new MacOSRemoteWindowCaptureBoundary(catalog);
        try
        {
            Require(Await(catalog.RefreshAsync().AsTask()).Succeeded, "catalog_refreshed");
            IReadOnlyList<NativeRemoteWindowSourceSnapshot> snapshots = catalog.GetSnapshot();
            Require(snapshots.Count == 1, "catalog_exact_task_source_only");
            NativeRemoteWindowSourceUse sourceUse = NativeRemoteWindowSourceUse.Create(snapshots[0], 1, 1);
            var sink = new MarkerSink(sourceUse, geometry);
            Require(Await(boundary.StartAsync(sourceUse, sink, CancellationToken.None).AsTask()).Succeeded,
                "boundary_capture_started");
            WaitForMarker(() => sink.Matched > 0, () => sink.Failure);
            Require(boundary.StopNow().Succeeded, "boundary_delivery_closed");
            Task stopCompletion = boundary.StopCompletion;
            Await(stopCompletion);
            Require(stopCompletion.IsCompletedSuccessfully, "boundary_stop_completion_joined");
            int framesAtStop = sink.Matched;
            int deliveryCallsAtStop = api.SampleDeliveryCalls;
            TaskWindow.Pump(TimeSpan.FromMilliseconds(600));
            Require(sink.Matched == framesAtStop && api.SampleDeliveryCalls == deliveryCallsAtStop,
                "boundary_no_late_frames_or_sample_delivery_calls");
            if (sink.Failure is { } failure) throw failure;
            Console.WriteLine($"catalog_boundary=pass; frame={sink.Width}x{sink.Height}; marker_frames={sink.Matched}; generation_binding=true; stop_completion=joined; late_frames=0; late_sample_delivery_calls=0; observation_ms=600");
        }
        finally
        {
            _ = boundary.StopNow();
            Await(boundary.StopCompletion);
            Await(boundary.DisposeAsync().AsTask());
            Await(catalog.DisposeAsync().AsTask());
        }
    }

    private static void DrainAndDispose(IMacOSRemoteWindowNativeCapture capture)
    {
        Require(Await(capture.StopAndDrainAsync().AsTask()) && capture.IsDrained,
            "cleanup_native_drain_proven");
        capture.Dispose();
    }

    private static void DisposeSources(IReadOnlyList<IMacOSRemoteWindowNativeSource> sources)
    {
        foreach (IMacOSRemoteWindowNativeSource source in sources) source.Dispose();
    }

    private static void WaitForMarker(Func<bool> ready, Func<Exception?> failure)
    {
        long deadline = Environment.TickCount64 + 10_000;
        while (!ready() && failure() is null && Environment.TickCount64 < deadline)
        {
            TaskWindow.Pump(TimeSpan.FromMilliseconds(20));
        }
        if (failure() is { } exception) throw exception;
        Require(ready(), "task_marker_frame_received_before_deadline");
    }

    private static T Await<T>(Task<T> task)
    {
        Await((Task)task);
        return task.GetAwaiter().GetResult();
    }

    private static void Await(Task task)
    {
        long deadline = Environment.TickCount64 + 15_000;
        while (!task.IsCompleted && Environment.TickCount64 < deadline)
        {
            TaskWindow.Pump(TimeSpan.FromMilliseconds(20));
        }
        if (!task.IsCompleted) throw new TimeoutException("probe_observation_timeout_cleanup_unproven");
        task.GetAwaiter().GetResult();
    }

    // Diagnostic only: fail closed if the implementation shape changes. This
    // avoids adding a production API solely for this standalone test process.
    private static int CaptureRootCount()
    {
        Type capture = typeof(MacOSRemoteWindowScreenCaptureKitApi).GetNestedType("Capture", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("capture_root_diagnostic_unavailable");
        object roots = capture.GetField("Roots", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException("capture_root_diagnostic_unavailable");
        return (int)(roots.GetType().GetProperty("Count")?.GetValue(roots)
            ?? throw new InvalidOperationException("capture_root_diagnostic_unavailable"));
    }

    internal static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }
}
