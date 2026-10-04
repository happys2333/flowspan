# Windows capture feasibility, 2026-10-04

Status: read-only API/source research; no Windows native execution. Task 7 and
production host readiness remain open. This is a next-slice input, not an ADR
accepting an untested native ABI.

## Smallest next slice

Keep the existing portable `net10.0` Platform project and first prove a narrow
C# direct-COM path on `win-x64`, with an independent task-owned-window probe.
There is no demonstrated need for a C++ helper. Do not change the Desktop host
factory or classify sources Safe merely because capture works.

1. Bind the existing source registry token to a retained GraphicsCaptureItem,
   HWND, owning PID, process creation time and geometry. Use generic labels,
   without reading titles. HWND/PID/process creation time alone cannot detect
   same-process HWND reuse. The specific item's Closed event permanently retires
   its generation; Start must not recreate an item from the old HWND.
2. One dedicated MTA owner creates/releases capture, D3D and COM objects; it
   never changes the Avalonia UI apartment. Create a BGRA D3D11 device, its
   WinRT device, a two-buffer CreateFreeThreaded pool and the retained item.
3. Begin with bounded TryGetNextFrame polling, which the official API permits,
   avoiding the high-frequency FrameArrived reverse-COM ABI. A generation-bound,
   nonthrowing Closed callback still requires explicit ownership/drain proof.
4. While the native frame is borrowed, obtain its D3D11 texture, validate format,
   dimensions, ContentSize and the existing 64-MiB ceiling. Copy only the legal
   content rectangle into a staging texture, then Map READ/DO_NOT_WAIT and copy
   by RowPitch into a clearing, tightly packed BGRA owner. Unmap and close the
   native frame before delivering the owned copy. Pool pixels outside ContentSize
   are undefined; copying the entire texture can expose stale content after a
   resize. Use finite retries/drop for still-drawing and terminate on device loss.
5. Keep immediate-context access on its one owner thread. Pause/Stop/Emergency
   Stop synchronously close delivery, while a separately owned task joins native
   Close, worker/callback drain and final COM release. Failed/blocked cleanup
   retains retiring owners. A delivery-latch confirmation is not native cleanup
   confirmation (ADR 0028/0029).

## Toolchain choices

Direct COM preserves `net10.0` and needs no added runtime package, but the narrow
WinRT event delegate, vtable and reference-counting ABI must be experimentally
verified against SDK declarations before integration. Non-`win-x64` native ABI
starts Unsupported; portable contract tests still build/run everywhere.

Ordinary Windows SDK projections use a versioned Windows TFM, for example
`net10.0-windows10.0.19041.0`, and its SDK reference pack. A portable Desktop
cannot directly reference that Windows-only target without an explicit target
strategy. Do not silently select a different project TFM by build-host OS.

If direct COM is not maintainable, investigate CsWinRT embedded generation as
the next option: it can keep plain `net10.0` when projected types remain internal.
Pin the package, SDK WinMD and generated inputs. The researched package is
Microsoft.Windows.CsWinRT 2.3.1; `cswinrt.exe` generation/re-generation belongs in
a Windows job, not an assumed cross-host executable. Recheck package provenance,
licensing and supported tooling before adding it.

The API combination requires build 18362: CreateFreeThreaded was introduced at
17763 and CreateForWindow at 18362. This is an API lower bound, not the .NET 10
product OS policy or proof of successful execution on those versions.

## Security and evidence

WGC IsSupported, a successfully captured frame, black pixels, and WDA_NONE do not
prove Protection Safe. Failed display-affinity/desktop/session observations are
Unknown. WGC frames have no Desktop Duplication ProtectedContentMaskedOut field;
do not borrow that other API's contract. Keep OS capture borders and do not
request Borderless. When available, explicitly keep IncludeSecondaryWindows
false; do not include Flowspan overlays or extra windows.

The initial candidate may advertise capture but input=false and protection
Unknown. A reliable Safe observation for generic windows, source/desktop loss,
independent Emergency Stop and real package behavior remain separate gates.
Never bypass target opt-out, switch the user's desktop or acquire SYSTEM access.

Evidence must remain split:

- Three-OS contracts: stale/ABA generation, geometry/stride, copy/Map failure,
  stop-before-delivery, repeated Dispose and blocked cleanup.
- Matching Windows native D3D/WARP known-pixel staging-copy smoke: only an actual
  executed hash check is native proof.
- Explicit self-window WGC smoke: create/capture only the task's window, read no
  existing titles and save no pixels. Noninteractive desktop, unsupported API or
  absent frames are Skip/unfinished, not capture success.

Hosted jobs do not establish UAC/lock/protected-app behavior, deny/revoke,
physical two-device operation, independent stop or signed install lifecycle.

## Fixed source inputs

- [SDK capture IDL](https://github.com/microsoft/win32metadata/blob/5c5efbc01d4c87f6830ec304d42777991d533154/generation/WinSDK/RecompiledIdlHeaders/winrt/windows.graphics.capture.idl)
- [SDK HWND interop](https://github.com/microsoft/win32metadata/blob/5c5efbc01d4c87f6830ec304d42777991d533154/generation/WinSDK/RecompiledIdlHeaders/um/Windows.Graphics.Capture.Interop.h)
- [CsWinRT embedded documentation](https://github.com/microsoft/CsWinRT/blob/d4649f92300aa3cb2dfd23a66074199488d85973/docs/embedded.md)
- [Microsoft MIT C# WGC example](https://github.com/microsoft/winappCli/blob/e18dcfaff7ad1ee5830f75494bb46e01521c1aff/src/winapp-CLI/WinApp.UIAutomation/Capture/WgcCapture.cs)
- [Official capture guide](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture)
- [Capture item Closed](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscaptureitem.closed)
- [IsWindow handle-reuse warning](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iswindow)
- [D3D11 Map](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-map)

These permissive/public inputs inform a clean-room implementation. Do not copy
GPL core code, or inherit an example's lifetime/resize/cleanup assumptions without
contract and native evidence.
