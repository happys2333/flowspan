# Windows D3D11 / opt-in WGC capture ABI probe

Independent, clean-room C# feasibility tool. It is not in `Flowspan.slnx`, is not
referenced by any product project, and has no NuGet dependencies. Its portable
`net10.0` target preserves compiler warnings as errors; recommended production
analyzers are disabled only for this raw ABI experiment.

## Run

From the repository root, with the pinned .NET SDK:

```sh
dotnet build tools/Flowspan.Windows.CaptureProbe/Flowspan.Windows.CaptureProbe.csproj -c Debug
dotnet run --project tools/Flowspan.Windows.CaptureProbe/Flowspan.Windows.CaptureProbe.csproj -c Debug --no-build -- --self-test
dotnet run --project tools/Flowspan.Windows.CaptureProbe/Flowspan.Windows.CaptureProbe.csproj -c Debug --no-build
dotnet build tools/Flowspan.Windows.CaptureProbe/Flowspan.Windows.CaptureProbe.csproj -c Release
dotnet run --project tools/Flowspan.Windows.CaptureProbe/Flowspan.Windows.CaptureProbe.csproj -c Release --no-build -- --self-test
dotnet run --project tools/Flowspan.Windows.CaptureProbe/Flowspan.Windows.CaptureProbe.csproj -c Release --no-build
```

`--help` makes no native calls. `--self-test` makes no native calls and checks
padded-row copying, rejected dimensions/strides/truncation, the bounded fixed
pixel fixture, and managed 64-bit structure layout. These are managed checks,
not proof of the Windows ABI or GPU readback.

`--wgc-self-test` separately runs 12 managed WGC-boundary regressions; the
original `--self-test` remains exactly ten cases. Neither suite calls native APIs.

The default mode calls native APIs **only on Windows with an x64 process**.
Other hosts emit `probe=skip mode=warp reason=requires_windows_x64` plus explicit
no-window-capture/no-permission/no-pixel-file fields and return zero. Skip is not
a native pass. WGC is an explicit opt-in mode; unknown arguments return two.

The Windows default does not enumerate, create, inspect, or capture any window;
does not inspect or request capture/input permission; and does not write pixel
files. WARP is Microsoft's software D3D11 driver. The probe creates only a
7-by-5 in-memory BGRA texture with fixed pixels and sentinel bytes outside the
3-by-2 content rectangle. It calls:

1. `RoInitialize(RO_INIT_MULTITHREADED)` on a dedicated native owner thread;
2. `D3D11CreateDevice(WARP, BGRA_SUPPORT)` and two `CreateTexture2D` operations;
3. native `GetDesc` checks for both exact texture descriptions;
4. `CopySubresourceRegion` of only the top-left 3-by-2 content rectangle;
5. `Flush` to submit the copy, then bounded `Map(READ, DO_NOT_WAIT)` polling;
6. `RowPitch`-aware copying into 24 independently owned packed BGRA bytes;
7. exact SHA-256 validation, `Unmap`, managed buffer clearing, four owned COM
   reference releases, and balanced `RoUninitialize`.

Expected successful stdout has this shape (variable values are placeholders,
not observed Windows results):

```text
probe=pass mode=warp api=D3D11CreateDevice driver=WARP apartment=MTA source=7x5 content=3x2 bytes=24 row_pitch=<native> feature_level=<native> map_attempts=<native> owned_refs=4 released_refs=4 sha256=fc865b98e8180228df0ec6c60cd9a919aa9033ae1b802fbfefe91ede9ac2e3af window_capture_executed=false permissions_requested=0 pixel_files_written=0
```

Native row pitch is not assumed to be packed. `--self-test` separately checks a
padded-row fixture, so a runner with an unpadded native mapping still has a
deterministic padding regression. Native padding can be claimed only when the
observed row pitch is greater than 12.

Exit codes: `0` is an executed pass or explicit unsupported-host Skip; `1` is an
observed failure; `2` is unsupported arguments. Count native success only when
stdout contains `probe=pass mode=warp` for the exact source input.

## Explicit task-owned-window WGC slice

```sh
dotnet run --project tools/Flowspan.Windows.CaptureProbe/Flowspan.Windows.CaptureProbe.csproj -c Release --no-build -- --wgc-self-test
dotnet run --project tools/Flowspan.Windows.CaptureProbe/Flowspan.Windows.CaptureProbe.csproj -c Release --no-build -- --native-self-window
```

Only `--native-self-window` can create/capture a window. It requires Windows x64,
build 19041 or newer, a visible interactive window station/input desktop, DWM
composition, and supported WGC APIs. It creates one visible, nonactivating
64-by-64 popup HWND on a dedicated UI owner; it does not enumerate existing
windows, read their titles, create secondary windows, move the pointer, request
permissions, disable the OS capture border, or save pixels. The window is
task-owned, not a user-app source. An external process/job watchdog is still
required for an automated native run.

The dedicated MTA native owner creates a retained capture item from that exact
HWND, a WARP device and WinRT D3D device, a two-buffer `CreateFreeThreaded` pool,
and a capture session. Cursor capture is disabled. When Session6 is available,
`IncludeSecondaryWindows` is explicitly false; only `E_NOINTERFACE` accepts the
older API's documented default false, reported as `documented_default_false`.
No hidden-border permission is requested. Frame acquisition uses bounded
`TryGetNextFrame` polling, with no reverse-COM frame or source-Closed callbacks.

The borrowed frame's native texture format/shape and ContentSize are checked
before copying. Only the valid content rectangle is copied into staging, then
read with bounded `Map(READ, DO_NOT_WAIT)` and RowPitch-aware packing. Unmap and
native frame Close complete before any owned pixels can escape the reader.
Every success/rejection/exception path attempts native frame Close; an
unconfirmed Close quarantines the graph, never masquerading as closed. Admission is
checked before/after description, copy, and Close. A shared stop/commit lock
linearizes successful metadata against `WM_CLOSE`; hash work never holds it.

Four 8-by-8 interior markers, red/green/blue/white, must hash to
`c836b97f55a9b91993869d4040438f5975519655fa455f07819951eb1373e54e`.
Outside pixels are excluded from this expectation, avoiding claims about DWM
edges or undefined regions. The whole-frame hash is reported only after marker
verification, not treated as a platform-invariant expected value. Packed pixels
and extracted marker buffers are cleared; no pixels are logged or written.

Eight-second no-frame expiry may produce an explicit zero-exit Skip only after
native session/pool Close, all COM releases, RoUninitialize, UI owner join,
HWND destruction, class unregister, DPI restoration, and GDI cleanup are
confirmed. Unsupported host/version, unavailable interactive desktop, WGC
IsSupported=false, and pre-Start `REGDB_E_CLASSNOTREG` are explicit Skips. Wrong
pixels, invalid geometry, other HRESULTs/ABI faults, stop, or uncertain cleanup
are failures, never Skips. Native work has a twenty-second owner join bound;
unconfirmed native cleanup leaves its graph and HWND quarantined until this
standalone process exits, without pretending that owner references were released.
`WM_CLOSE` closes admission only; even the internal finish message cannot
destroy the HWND before native cleanup is confirmed.

Successful stdout begins `probe=pass mode=wgc_self_window`, includes verified
marker/frame hashes, observed geometry/pitch/COM counts and
`cleanup_confirmed=true`, and always reports `protection=unknown`. Skip is not
proof of WGC capture. No real Windows WGC result is recorded yet; a successful
compile, portable fixture, default WARP pass, or hosted job alone is not that
evidence. Current CI defaults must not pass the opt-in argument.

The independent `Windows task-owned WGC probe` workflow is opt-in through
`workflow_dispatch` once registered on the default branch, or a deliberate push
of an exact implementation commit to `codex/wgc-self-window-probe`. No other
push branch triggers that workflow. It preserves raw stdout/stderr and runner,
source-manifest and exact-commit metadata, applies an external child-process
watchdog, and rejects every Skip or incomplete success record. A hosted pass is
still only task-owned-window WGC evidence, not physical-device or production
acceptance. The normal `codex/v1-foundation` CI never requests native WGC capture.

### Threat and source-lifetime boundary

The poll-only simplification is valid only because this tool owns the HWND
and controls its destruction. It does not establish production source-loss,
Closed callback drain, HWND ABA/generation protection, resize/recreate,
permission deny/revoke, protected content, secure input/desktop, Emergency Stop,
hardware GPU, or physical-device behavior. It does not change the production
factory or Remote Window readiness; capture success never means Protection Safe.
Hostile same-session messages and arbitrary native faults are not contained as
a production sandbox. The finish-message gate prevents early owned-HWND
destruction through that message, not all external source-loss mechanisms.

## Ownership, bounds, and limits

The immediate context is used only on its owner thread. Every nonzero owned
native out pointer is released, including HRESULT failure paths. Release counts
prove this tool balanced its four acquired references; they do not prove the
driver has no internal references, GPU allocations, or leaks.

Map polling has a two-second monotonic bound. A native call itself cannot be
cancelled; the main thread has a fifteen-second join bound. Timeout exits this
standalone process nonzero without releasing owners still borrowed by native
work. It is process-lifetime quarantine, not production cleanup or recovery.
Automation should also apply an external process/job watchdog. A native access
violation or invalid vtable can terminate the process; managed catch is not
claimed to contain arbitrary native faults.

The generic row-copy helper rejects either dimension above 16,384, invalid
row pitch, a native plane above 64 MiB, and truncated rows before allocating.
Only the fixed 24-byte output is created in native mode. The software fixture
and output are cleared after validation; no pixels are logged.

The default WARP slice does not exercise WinRT capture items, reverse COM event
delegates, real capture, HWND/PID identity, window replacement, user permissions,
protected content, secure desktop, Emergency Stop, hardware GPU drivers,
physical devices, display changes, sleep, packaged identity, or resource leak
detectors. It does not change production Remote Window readiness. Windows
execution must be proven by a matching-host run actually producing and checking
these pixels; a successful macOS/Linux compile is not that evidence. Existing
WARP-only Windows evidence is recorded separately in
`../../docs/evidence/2026-10-04-windows-warp-readback.md`; it is not WGC evidence.

## Fixed ABI sources

No GPL source was used. Constants, structure layout, and method signatures come
from the Windows SDK declarations at fixed `microsoft/win32metadata` commit
`5c5efbc01d4c87f6830ec304d42777991d533154`:

- [d3d11.h](https://github.com/microsoft/win32metadata/blob/5c5efbc01d4c87f6830ec304d42777991d533154/generation/WinSDK/RecompiledIdlHeaders/um/d3d11.h)
- [d3dcommon.h](https://github.com/microsoft/win32metadata/blob/5c5efbc01d4c87f6830ec304d42777991d533154/generation/WinSDK/RecompiledIdlHeaders/um/d3dcommon.h)
- [dxgiformat.h](https://github.com/microsoft/win32metadata/blob/5c5efbc01d4c87f6830ec304d42777991d533154/generation/WinSDK/RecompiledIdlHeaders/shared/dxgiformat.h)
- [D3D11 Map](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-map)
- [CopySubresourceRegion](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-copysubresourceregion)
- [D3D11 Flush](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-flush)
- [RoInitialize](https://learn.microsoft.com/en-us/windows/win32/api/roapi/nf-roapi-roinitialize)
- [WGC interfaces](https://github.com/microsoft/win32metadata/blob/5c5efbc01d4c87f6830ec304d42777991d533154/generation/WinSDK/RecompiledIdlHeaders/winrt/windows.graphics.capture.h)
- [HWND capture interop](https://github.com/microsoft/win32metadata/blob/5c5efbc01d4c87f6830ec304d42777991d533154/generation/WinSDK/RecompiledIdlHeaders/um/Windows.Graphics.Capture.Interop.h)
- [WinRT D3D interop](https://github.com/microsoft/win32metadata/blob/5c5efbc01d4c87f6830ec304d42777991d533154/generation/WinSDK/RecompiledIdlHeaders/um/windows.graphics.directx.direct3d11.interop.h)
- [WinUser.h](https://github.com/microsoft/win32metadata/blob/5c5efbc01d4c87f6830ec304d42777991d533154/generation/WinSDK/RecompiledIdlHeaders/um/WinUser.h)
- [wingdi.h](https://github.com/microsoft/win32metadata/blob/5c5efbc01d4c87f6830ec304d42777991d533154/generation/WinSDK/RecompiledIdlHeaders/um/wingdi.h)
- [IncludeSecondaryWindows](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.includesecondarywindows)

The checked x64 slots are `IUnknown.Release=2`, `Device.CreateTexture2D=5`,
`Texture2D.GetDesc=10`, and `Context.Map=14`, `Unmap=15`,
`CopySubresourceRegion=46`, `Flush=111`. Required structure sizes are
`TEXTURE2D_DESC=44`, `SUBRESOURCE_DATA=16`, `MAPPED_SUBRESOURCE=16`, and `BOX=24`.
The independently checked managed layouts on another 64-bit OS do not prove
the native Windows call conventions.

WGC methods return HRESULTs; WinRT boolean is one byte. `SizeInt32` is two signed
int32 values and is passed by value to CreateFreeThreaded. Closable methods are
called only on a QI-acquired `IClosable` pointer, never a default interface.
HSTRINGs use WindowsDeleteString, not COM Release. WGC/Win32 source signatures,
GUIDs, vtable slots and COM ownership received independent static review;
this is not native execution or leak-detection evidence.

Local commands and per-source SHA-256 values are recorded in
`evidence/2026-10-04-local.md`; they deliberately contain no fabricated Windows
stdout. Recompute source hashes after any implementation edit.
The subsequent opt-in WGC slice is recorded separately in
`evidence/2026-10-04-wgc-local.md`.
