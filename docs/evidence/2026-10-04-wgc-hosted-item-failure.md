# First hosted Windows WGC probe — exact-SHA failure evidence

Date: 2026-10-04. Read-only audit; no rerun, dispatch, cancellation, repository edit, commit, push, or GitHub text publication was performed by this auditor.

## Outcome

The first hosted task-owned HWND WGC run **failed**, not skipped and not timed out. The native process returned exit code 1 while creating the owned-window GraphicsCaptureItem, before a capture session started or a frame was received. The strict native gate rejected the run. Do not replace this failure with a future successful run or weaken the gate.

- Repository: https://github.com/happys2333/flowspan
- Run: https://github.com/happys2333/flowspan/actions/runs/37208080753
- Job: https://github.com/happys2333/flowspan/actions/runs/37208080753/job/111453392931
- Workflow: Windows task-owned WGC probe
- Exact SHA: `470d0f354f420b39bdc1ad5736bcb66acb01792d`
- Ref: `refs/heads/codex/wgc-self-window-probe`
- Trigger: push; run number 1; attempt 1
- Run created: 2026-10-04T14:07:14Z
- Job started/completed: 2026-10-04T14:07:18Z / 2026-10-04T14:08:11Z
- Run and job conclusions: failure

The native supervisor step itself has the GitHub outcome success because it retained the failed child-process status and raw streams. That outcome is **not** a native capture pass. The subsequent strict gate failed on the nonzero process exit status before any success-record parsing.

## Exact raw native result

`native.stdout.raw` is empty (0 bytes). `native.stderr.raw` is exactly 341 bytes, valid UTF-8, one record followed by one CRLF, with no lone CR or LF:

```text
probe=error mode=wgc_self_window reason=owned_window_item_create_failed hresult=0x80070057 capture_session_started=false frames_received=0 window_capture_executed=false cleanup_confirmed=true process_lifetime_quarantine=false existing_windows_enumerated=0 user_titles_read=0 permissions_requested=0 pixel_files_written=0 protection=unknown
```

This is a 14-field error record, not the required 24-field success record. `0x80070057` is E_INVALIDARG; the evidence establishes the failing stage, not the root cause of the invalid argument.

Observed process status:

```json
{
  "started": true,
  "timed_out": false,
  "tree_kill_requested": false,
  "exited": true,
  "exit_code": 1,
  "raw_streams_complete": true,
  "watchdog_ms": 45000,
  "supervisor_error": false
}
```

The generic gate exception text is `Native process did not finish successfully within its external watchdog.`; the retained status specifically proves **nonzero exit**, not a watchdog timeout. No tree kill was requested. The streams drained completely.

No `native.stdout.normalized` or `verified-native-result.json` exists in the uploaded artifact, consistently with rejection before parsing. There is no frame hash, marker hash, native RowPitch, feature level, or owned/released COM-reference count from this run. Do not report those missing values as zero or as validated. `cleanup_confirmed=true` and `process_lifetime_quarantine=false` are the probe's error-path report, not independent native leak detection.

Raw stream SHA-256:

- Empty stdout: `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`
- Stderr: `a8f70e0df07a9b8cbff71016e70335fcc3f1df5e32367663848e2e3a376da8d6`

## Actual invocation and runner

```text
executable: C:\Program Files\dotnet\dotnet.exe
arguments[0]: D:\a\flowspan\flowspan\tools\Flowspan.Windows.CaptureProbe\bin\Release\net10.0\Flowspan.Windows.CaptureProbe.dll
arguments[1]: --native-self-window
cwd: D:\a\flowspan\flowspan
```

Runner facts agree between uploaded metadata and setup logs:

- Hosted runner label: windows-2025; runner name GitHub Actions 1000003732; runner agent 2.337.0.
- Microsoft Windows Server 2025 Datacenter, version 10.0.26100, build 26100, 64-bit.
- Image: windows-2025-vs2026; ImageOS win25-vs2026; image version 20260925.250.1.
- Runner/process architecture: X64; PowerShell 7.6.6.
- Actual .NET SDK: **10.0.401**, MSBuild 18.9.11+e34a38d2a; runtime host 10.0.12 x64.
- `global.json` requests 10.0.301 with `rollForward=latestFeature`, so 10.0.401 is allowed. This is not proof that the hosted SDK equals the local 10.0.301 SDK.
- Metadata explicitly sets `physical_device_evidence=false` and `production_adapter_evidence=false`.

## Steps and portable checks

| Step | GitHub outcome | Meaning |
|---|---|---|
| Checkout | success | Exact SHA checkout; metadata step also required a clean tree |
| SDK | success | Actual installed/selected SDK recorded |
| Metadata | success | SHA, runner, source/build-input manifests retained |
| Build | success | Release build, 0 Warning(s), 0 Error(s) |
| Formatting | success | Standalone format verification completed |
| Portable self-tests | success | Old 10-case and new 12-case managed tests passed |
| Native process supervisor | success | Failed child exited and raw failure evidence was retained |
| Strict native gate | **failure** | Child exit 1 rejected |
| Final outcome recording | success | Above outcomes retained |
| Artifact upload | success | Failure artifact uploaded |

Exact portable outputs:

```text
self_test=pass cases=10 native_api_called=false
wgc_self_test=pass cases=12 native_api_called=false
```

These managed self-tests are not Windows capture, WGC/DWM, hardware GPU, or native lifetime fault-injection evidence.

## Source identity and archive integrity

The runner's checkout log, metadata source SHA, workflow SHA, run API head SHA, and artifact API workflow-run SHA all identify the same exact SHA above. The manifest SHA is:

```text
d426f32f279ce47a0be67d2eb7a49eb9a278b1e409d6edf8e207764e63de5b7d  source-files.sha256
```

A local read-only audit recomputed the SHA-256 of the exact downloaded manifest bytes and matched it to that value. It also independently hashed all 11 exact-commit source/project blobs using `git show <SHA>:<path>` and matched every runner manifest entry. The four recorded build inputs (global.json, Directory.Build.props, Directory.Packages.props, windows-wgc-probe.yml) also match exact-commit blob hashes. Source newline metadata reports index/worktree LF with eol=lf for the tool; raw runtime stream CRLF was retained separately.

Precisely, `d426...` is the digest of the 1,317-byte LF `source-files.sha256` content. The separate 86-byte `source-manifest.sha256` file contains that digest; its own file digest is `69200cef54d12bd2942ac1ead922b70c4193fa6b102a491a3ec77f282fd2e1fc`. These are distinct objects and must not be conflated.

Artifact:

- ID: 11305421866
- Name: `windows-wgc-self-window-470d0f354f420b39bdc1ad5736bcb66acb01792d-1`
- Exactly 5315 bytes; 14 retained files
- SHA-256: `1a9def85ac14dfd93539861e843d9f9075ebada2ba092999116ac7fa8a9343cf`
- Downloaded archive hash/bytes, artifact API digest/bytes, and upload action log digest/bytes **all agree**.
- Created 2026-10-04T14:08:07Z; hosted expiry 2026-10-18T14:08:06Z.
- Full downloaded run-log archive: 28034 bytes, local SHA-256 `ed97ee21c2b08baa2be8e221e245e415aa29a1e8a9e35c018555d34bd2f34bd0`. GitHub does not supply an equivalent log-archive digest here; this is a local integrity identifier.

All original archives and extracted files remain untouched at:

```text
/tmp/flowspan-wgc-hosted-470d0f3/
  artifacts-api.json
  run-api.json
  jobs-api.json
  artifact-11305421866.zip
  run-37208080753-logs.zip
  artifact/
  logs/
  audit.json
  evidence-draft.md
```

`audit.json` retains exact input hashes, archive and raw-stream integrity, metadata, the 14 error fields, process status, step outcomes, and an empty audit-mismatch list.

## Independent read-only cross-check

A second agent independently checked the retained files and exact-commit blobs. It found no archive-byte or source-binding mismatch: both ZIPs passed integrity checks; all 14 artifact extracted files and all extracted log files matched their corresponding ZIP members byte-for-byte; all 11 source/project and four build-input hashes matched the exact commit. It independently confirmed the failed child exit, preserved CRLF error record, absent verified-native result, actual SDK/image facts, and the distinction between the successful supervisor step and failed capture gate. This cross-check is evidence-integrity review, not additional native execution.

## Read-only diagnostic follow-up (not a confirmed repair)

Two independent source checks match the current default-interface IID,
`IGraphicsCaptureItemInterop : IUnknown` slot 3, `HWND, REFIID, void**` arguments
and HRESULT return to the fixed Windows SDK. There is no current evidence for
changing the IID or ABI signature.

The leading falsifiable hypothesis is this probe's `WS_EX_TOOLWINDOW` style.
A Microsoft sample maintainer historically explained that tool windows were
not eligible to create capture items in that implementation:
[maintainer statement](https://github.com/microsoft/Windows.UI.Composition-Win32-Samples/issues/48#issuecomment-1358071827).
That is historical implementation evidence, not a current Server 2025 contract
or a confirmed cause of this run. The next minimal experiment can change only
the owned HWND exStyle from `0x80` to `0`, preserving popup geometry, nonactivating
display, every ABI call and the strict marker/cleanup gate. It has **not** been
implemented or executed in this checkpoint.

A new-window shell-registration race and the actual HWND visibility/root,
geometry, cloaking, affinity or hosted-desktop state remain alternatives. Any
added instrumentation must inspect only this task-owned HWND, never enumerate
user windows or read titles. Do not turn E_INVALIDARG into Skip or substitute
whole-screen capture. WARP device, frame pool and readback are after the failing
stage and were not reached by this native invocation.

## Acceptance limits

The attempted scope is only the probe's own 64×64 HWND and a WARP-backed capture path. It does not enumerate user windows or read user titles; the error record reports permissions_requested=0 and pixel_files_written=0. Protection remains **unknown**.

This run does **not** satisfy the strict 24-field success gate, known-marker verification, frames≥1, balanced owned_refs/released_refs, generic-application capture, input, protected-window/secure-desktop safety, native Emergency Stop, production adapter readiness, physical-device acceptance, signed release, or v1 acceptance. Task 7/native Windows capture evidence remains open. Any diagnostic fix and future rerun need a new exact-source record while preserving this initial failure.
