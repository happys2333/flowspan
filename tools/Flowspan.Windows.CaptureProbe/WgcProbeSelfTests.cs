using System.Runtime.InteropServices;

namespace Flowspan.Windows.CaptureProbe;

internal static class WgcProbeSelfTests
{
    public static int Run()
    {
        string testCase = "content_size_outside_surface";
        try
        {
            using FixtureFrame frame = new(new WgcFrameGeometry(32, 16, 33, 16));
            try
            {
                WgcOwnedFrameReader.Read(frame);
                throw new InvalidOperationException("Invalid geometry published pixels.");
            }
            catch (ProbeFailureException exception) when (exception.Reason == "invalid_frame_geometry")
            {
                if (!frame.Closed || frame.CopyCalls != 0)
                {
                    throw new InvalidOperationException("Rejected frame was copied or not closed.");
                }
            }

            Case("stop_during_copy", StopDuringCopyDoesNotPublish);
            Case("failed_close_quarantines_owners", UnconfirmedCloseQuarantinesOwners);
            Case("managed_abi_layout", VerifyAbiLayout);
            Case("confirmed_close_balances_owners", ConfirmedCloseBalancesOwners);
            Case("close_failure_clears_pixels", CloseFailureClearsUnpublishedPixels);
            Case("valid_frame_closes_before_publication", ValidFrameClosesBeforePublication);
            Case("fixed_interior_markers", MarkerInteriorIgnoresOutsidePixels);
            Case("stop_during_description", StopDuringDescriptionAvoidsCopy);
            Case("stopped_admission_rejects_commit", StoppedAdmissionCannotCommitPass);
            Case("stop_during_close", StopDuringCloseClearsPixels);
            Case("failed_qi_nonzero_outref_quarantined", FailedQueryQuarantinesNonzeroOutref);
            Console.WriteLine("wgc_self_test=pass cases=12 native_api_called=false");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"wgc_self_test=fail case={testCase} type={exception.GetType().Name}");
            return 1;
        }

        void Case(string name, Action verify)
        {
            testCase = name;
            verify();
        }
    }

    private static void StopDuringCloseClearsPixels()
    {
        WgcProbeAdmission admission = new();
        byte[] pixels = [10, 20, 30, 255];
        FixtureFrame frame = new(new WgcFrameGeometry(1, 1, 1, 1), () => pixels, admission.Stop);
        try
        {
            WgcOwnedFrameReader.Read(frame, () => admission.IsOpen);
            throw new InvalidOperationException("Frame closed after stop but published pixels.");
        }
        catch (ProbeFailureException exception) when (exception.Reason == "capture_stopped")
        {
            if (!frame.Closed || pixels.Any(value => value != 0))
            {
                throw new InvalidOperationException("Post-close stop did not clear pixels.");
            }
        }
    }

    private static void FailedQueryQuarantinesNonzeroOutref()
    {
        FixtureCom api = new(queryResult: unchecked((int)0x80004005));
        WgcComOwners owners = new(api);
        owners.Own(1);
        try
        {
            owners.Close(1);
            throw new InvalidOperationException("Failed QI was accepted as cleanup.");
        }
        catch (ProbeFailureException exception) when (exception.Reason == "native_cleanup_unconfirmed")
        {
            owners.ReleaseAll();
            if (!owners.Quarantined || owners.OwnedReferences != 2 || api.Releases != 0)
            {
                throw new InvalidOperationException("Failed-QI outref escaped quarantine.");
            }
        }
    }

    private static void StoppedAdmissionCannotCommitPass()
    {
        WgcProbeAdmission admission = new();
        admission.Stop();
        bool published = false;
        try
        {
            admission.Commit(() => published = true);
            throw new InvalidOperationException("Stopped capture committed successful evidence.");
        }
        catch (ProbeFailureException exception) when (exception.Reason == "capture_stopped")
        {
            if (published) { throw new InvalidOperationException("Stop lost frame evidence arbitration."); }
        }
    }

    private static void StopDuringDescriptionAvoidsCopy()
    {
        bool open = true;
        FixtureFrame frame = new(new WgcFrameGeometry(1, 1, 1, 1), describe: () =>
        {
            open = false;
            return new WgcFrameGeometry(1, 1, 1, 1);
        });
        try
        {
            WgcOwnedFrameReader.Read(frame, () => open);
            throw new InvalidOperationException("Stopped description published pixels.");
        }
        catch (ProbeFailureException exception) when (exception.Reason == "capture_stopped")
        {
            if (!frame.Closed || frame.CopyCalls != 0)
            {
                throw new InvalidOperationException("Description resumed into copy after stop.");
            }
        }
    }

    private static void ConfirmedCloseBalancesOwners()
    {
        FixtureCom api = new(closeResult: 0);
        WgcComOwners owners = new(api);
        owners.Own(1);
        owners.Close(1);
        owners.ReleaseAll();
        owners.ReleaseAll();
        if (owners.Quarantined || owners.OwnedReferences != 2
            || owners.ReleasedReferences != 2 || api.Releases != 2)
        {
            throw new InvalidOperationException("Confirmed cleanup did not balance native owners.");
        }
    }

    private static void CloseFailureClearsUnpublishedPixels()
    {
        byte[] pixels = [10, 20, 30, 255];
        FixtureFrame frame = new(new WgcFrameGeometry(1, 1, 1, 1), () => pixels,
            () => throw new ProbeFailureException("native_cleanup_unconfirmed"));
        try
        {
            WgcOwnedFrameReader.Read(frame);
            throw new InvalidOperationException("Unclosed native frame published pixels.");
        }
        catch (ProbeFailureException exception) when (exception.Reason == "native_cleanup_unconfirmed")
        {
            if (pixels.Any(value => value != 0))
            {
                throw new InvalidOperationException("Failed-close pixels were not cleared.");
            }
        }
    }

    private static void ValidFrameClosesBeforePublication()
    {
        byte[] pixels = [10, 20, 30, 255];
        FixtureFrame frame = new(new WgcFrameGeometry(2, 2, 1, 1), () => pixels);
        byte[] owned = WgcOwnedFrameReader.Read(frame);
        byte[] expected = [10, 20, 30, 255];
        if (!frame.Closed || frame.CopyCalls != 1 || !owned.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidOperationException("Valid owned content was not published after close.");
        }

        System.Security.Cryptography.CryptographicOperations.ZeroMemory(owned);
    }

    private static void MarkerInteriorIgnoresOutsidePixels()
    {
        byte[] fixture = Enumerable.Repeat((byte)77, 64 * 64 * 4).ToArray();
        byte[][] colors = [[0, 0, 255, 255], [0, 255, 0, 255], [255, 0, 0, 255], [255, 255, 255, 255]];
        try
        {
            for (int marker = 0; marker < 4; marker++)
            {
                int left = 8 + marker % 2 * 32;
                int top = 8 + marker / 2 * 32;
                for (int y = top; y < top + 8; y++)
                {
                    for (int x = left; x < left + 8; x++)
                    {
                        colors[marker].CopyTo(fixture, (y * 64 + x) * 4);
                    }
                }
            }

            if (WgcSelfWindowProbe.HashMarkers(fixture, 64, 64) != WgcSelfWindowProbe.ExpectedMarkerHash)
            {
                throw new InvalidOperationException("Fixed interior marker fixture changed.");
            }
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(fixture); }
    }

    public static void VerifyAbiLayout()
    {
        if (IntPtr.Size != 8 || Marshal.SizeOf<SizeInt32>() != 8
            || Marshal.SizeOf<WindowClass>() != 72 || Marshal.SizeOf<WindowMessage>() != 48
            || Marshal.SizeOf<WindowRect>() != 16 || Marshal.SizeOf<PaintStruct>() != 72
            || Marshal.SizeOf<UserObjectFlags>() != 12
            || Marshal.OffsetOf<WindowClass>(nameof(WindowClass.Procedure)) != 8
            || Marshal.OffsetOf<WindowMessage>(nameof(WindowMessage.Lparam)) != 24
            || Marshal.OffsetOf<PaintStruct>(nameof(PaintStruct.Reserved)) != 36)
        {
            throw new InvalidOperationException("WGC/Win32 64-bit ABI layout mismatch.");
        }
    }

    private static void UnconfirmedCloseQuarantinesOwners()
    {
        FixtureCom api = new();
        WgcComOwners owners = new(api);
        owners.Own(1);
        try
        {
            owners.Close(1);
            throw new InvalidOperationException("Failed native Close was confirmed.");
        }
        catch (ProbeFailureException exception) when (exception.Reason == "native_cleanup_unconfirmed")
        {
            owners.ReleaseAll();
            if (!owners.Quarantined || api.Releases != 0 || owners.ReleasedReferences != 0
                || owners.OwnedReferences != 2)
            {
                throw new InvalidOperationException("Unconfirmed native owners were released.");
            }
        }
    }

    private sealed class FixtureCom(int closeResult = unchecked((int)0x80004005), int queryResult = 0) : IWgcComApi
    {
        public int Releases { get; private set; }

        public int QueryClosable(nint instance, out nint closable) { closable = 2; return queryResult; }

        public int Close(nint closable) => closeResult;

        public uint Release(nint instance) { Releases++; return 0; }
    }

    private static void StopDuringCopyDoesNotPublish()
    {
        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim resume = new();
        int open = 1;
        byte[] nativeCopy = [1, 2, 3, 255];
        FixtureFrame frame = new(new WgcFrameGeometry(1, 1, 1, 1), () =>
        {
            entered.Set();
            if (!resume.Wait(TimeSpan.FromSeconds(3)))
            {
                throw new InvalidOperationException("Fixture copy did not resume.");
            }

            return nativeCopy;
        });
        Exception? failure = null;
        byte[]? published = null;
        Thread worker = new(() =>
        {
            try { published = WgcOwnedFrameReader.Read(frame, () => Volatile.Read(ref open) != 0); }
            catch (Exception exception) { failure = exception; }
        })
        { IsBackground = true };
        worker.Start();
        if (!entered.Wait(TimeSpan.FromSeconds(3)))
        {
            resume.Set();
            throw new InvalidOperationException("Fixture copy did not start.");
        }

        Volatile.Write(ref open, 0);
        resume.Set();
        if (!worker.Join(TimeSpan.FromSeconds(3))
            || published is not null || !frame.Closed
            || failure is not ProbeFailureException { Reason: "capture_stopped" }
            || nativeCopy.Any(value => value != 0))
        {
            throw new InvalidOperationException("Late copy escaped stop or was not cleared and closed.");
        }
    }

    private sealed class FixtureFrame(WgcFrameGeometry geometry, Func<byte[]>? copy = null,
        Action? closing = null, Func<WgcFrameGeometry>? describe = null) : IWgcBorrowedFrame
    {
        public WgcFrameGeometry Geometry => describe?.Invoke() ?? geometry;

        public int CopyCalls { get; private set; }

        public bool Closed { get; private set; }

        public byte[] CopyContent()
        {
            CopyCalls++;
            return copy?.Invoke() ?? new byte[checked(geometry.ContentWidth * geometry.ContentHeight * 4)];
        }

        public void Dispose() { closing?.Invoke(); Closed = true; }
    }
}
