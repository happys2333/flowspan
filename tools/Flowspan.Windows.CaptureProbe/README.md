# Windows D3D11 capture ABI probe

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

The default mode calls native APIs **only on Windows with an x64 process**.
Other hosts emit `probe=skip mode=warp reason=requires_windows_x64` plus explicit
no-window-capture/no-permission/no-pixel-file fields and return zero. Skip is not
a native pass. The first slice does not implement WGC or
`--native-self-window`; unknown arguments return two.

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

This first slice does not exercise WinRT capture items, reverse COM event
delegates, real capture, HWND/PID identity, window replacement, user permissions,
protected content, secure desktop, Emergency Stop, hardware GPU drivers,
physical devices, display changes, sleep, packaged identity, or resource leak
detectors. It does not change production Remote Window readiness. Windows
execution remains unverified until a matching-host run actually produces and
checks these pixels; a successful macOS/Linux compile is not that evidence.

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

The checked x64 slots are `IUnknown.Release=2`, `Device.CreateTexture2D=5`,
`Texture2D.GetDesc=10`, and `Context.Map=14`, `Unmap=15`,
`CopySubresourceRegion=46`, `Flush=111`. Required structure sizes are
`TEXTURE2D_DESC=44`, `SUBRESOURCE_DATA=16`, `MAPPED_SUBRESOURCE=16`, and `BOX=24`.
The independently checked managed layouts on another 64-bit OS do not prove
the native Windows call conventions.

Local commands and per-source SHA-256 values are recorded in
`evidence/2026-10-04-local.md`; they deliberately contain no fabricated Windows
stdout. Recompute source hashes after any implementation edit.
