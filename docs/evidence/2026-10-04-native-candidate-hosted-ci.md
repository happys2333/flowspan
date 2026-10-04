# Native candidate hosted CI — 2026-10-04

Exact source: `c5332462880f079a3767e7c6cf4c3d4990c457d3`, branch
`codex/v1-foundation`. This checkpoint is **failed CI**, not v1 acceptance,
production native capture, physical-device validation or a release. The separate
[macOS matching-host capture evidence](2026-10-04-macos-native-capture-candidate.md)
and [Windows WARP experiment](2026-10-04-windows-warp-readback.md) retain their
own exact-source and execution boundaries.

## Exact runs and final jobs

Both runs are number 236, attempt 1, created `2026-10-04T13:15:33Z` and bound to
the exact SHA above. No rerun was requested or used for this audit.

| Workflow/job | ID | Final result |
| --- | ---: | --- |
| [CI](https://github.com/happys2333/flowspan/actions/runs/37204976024) | `37204976024` | failure |
| Secret scan | `111444162433` | success |
| Linux thread-loop ABI (no capture) | `111444162478` | success |
| Test (windows-latest) | `111444162508` | success |
| Test (ubuntu-latest) | `111444162534` | failure |
| Test (macos-latest) | `111444162553` | success |
| Package matrix placeholder | `111445229912` | skipped |
| [CodeQL](https://github.com/happys2333/flowspan/actions/runs/37204976007) / Analyze C# | `37204976007` / `111444162141` | success |

CI completed at `2026-10-04T13:21:28Z`; CodeQL completed at
`2026-10-04T13:21:25Z`. All three test jobs passed locked restore, solution
formatting and warning-as-error build with zero build warnings/errors.
Windows/macOS also passed explicit TEST MODE composition, simulator, all four
standalone probe builds and the configured formatting/default/self-test steps.
Linux's failed test step skipped every subsequent composition/simulator/probe
step in that job. The independent Linux ABI job still ran successfully.

Actual runner images, rather than the moving matrix aliases:

| Test host | OS reported by log | Image/version |
| --- | --- | --- |
| Linux | Ubuntu 24.04.5 x64 | `ubuntu-24.04` / `20260927.320.1` |
| Windows | Windows Server 2025, `10.0.26100` x64 | `windows-2025-vs2026` / `20260925.250.1` |
| macOS | macOS 26.6.2, `25G83`, arm64 | `macos-26-arm64` / `20260907.0351.1` |

These hosted operating systems do not substitute for all supported OS versions
or physical-device coverage. `global.json` permits latest-feature roll-forward;
the runner installed SDK 10.0.401, not the local candidate's SDK 10.0.301.

## Downloaded TRX, including the failure

All 36 TRX files were independently parsed. Each OS has 12 project files, the
same 2686 distinct test names and no missing/duplicate cases. XML counters match
the individual result records.

| OS | Executed/passed | Failed | MacOS project | Candidate cases | First-frame regressions |
| --- | --- | ---: | --- | --- | --- |
| Windows | 2686/2686 | 0 | 126/126 | 62/62 | 4/4 |
| macOS | 2686/2686 | 0 | 126/126 | 62/62 | 4/4 |
| Linux | 2686/2685 | 1 | 126/126 | 62/62 | 4/4 |

All counters other than total/executed/passed/failed are zero on every OS.
The candidate set is SourceCatalog 32 + CaptureOwnership 27 + CaptureBoundary 3.
The four startup cases are a subset of those 62, **not** four additional cases:

- `SampleBeforeNativeStartSettlementIsRetainedAndDeliveredAfterConfirmation`.
- `FailedCanceledOrStoppedNativeStartReleasesPrestartSampleWithoutCopyOrDelivery`
  with `terminal` values `failure`, `cancellation` and `stop`.

All 62 names/outcomes were checked individually and the three OS inventories
match. The earlier flaky
`RegistryDisposeStartsEveryOwnedRouteBeforeJoiningCleanup` is Passed on each OS;
that does not erase its [earlier failed run](2026-10-04-media-route-disposal-test.md).

Linux Desktop has 756/757 passed. Its only failed case, in downloaded member
`Linux_net10.0_20261004131915.trx`, is:

```text
Flowspan.Desktop.Tests.DesktopActivityRuntimeTests.AuthenticatedRuntimesExchangeNoteAndExposeOnlyEligibleLiveTarget
Assert.ThrowsAny() Failure: Exception type was not compatible
Expected: typeof(System.OperationCanceledException)
Actual:   typeof(System.IO.EndOfStreamException)
---- System.IO.EndOfStreamException : Unable to read beyond the end of the stream.
DesktopActivityRuntimeTests.cs:671
SecureControlChannel.ReceiveAsync:242
AuthenticatedControlSessionDispatcher.RunAsync:97
```

Windows/macOS passed this same case. This audit reports the observed teardown
exception mismatch; it does not establish the cause, change production/test
code or replace the failure with local successful runs.

## Actual no-capture native probes

The Windows job executed D3D11 WARP readback, not WGC:

```text
self_test=pass cases=10 native_api_called=false
probe=pass mode=warp api=D3D11CreateDevice driver=WARP apartment=MTA source=7x5 content=3x2 bytes=24 row_pitch=12 feature_level=0xb000 map_attempts=2 owned_refs=4 released_refs=4 sha256=fc865b98e8180228df0ec6c60cd9a919aa9033ae1b802fbfefe91ede9ac2e3af window_capture_executed=false permissions_requested=0 pixel_files_written=0
```

Four application COM references were balanced; this is not driver-internal leak
proof. RowPitch equals the packed width, so native padded-row behavior was not
exercised. macOS passed the ten portable self-tests, then actually printed
`probe=skip mode=warp reason=requires_windows_x64`; Linux never executed those
steps after its test failure.

The separate strict Linux job installed `libpipewire-0.3-0t64`
`1.0.5-1ubuntu3.3 amd64` with no daemon/portal dependency installed by that step.
Its artifact records Ubuntu image `20260927.320.1`, kernel `6.17.0-1022-azure`,
SDK 10.0.401 and runtime 10.0.12 x64. The real library reports 1.0.5. Its
single-line downloaded `native.stdout` is:

```text
probe=pass mode=thread_loop_abi reason=none library_version=1.0.5 native_result=0 native_calls_executed=true owners_released=true loop_roundtrip=true lock_roundtrips=2 native_timespec_bytes=16 native_time_valid=true outside_loop_thread=true daemon_connections=0 portal_calls=0 stream_creations=0 hardware_operations=0 window_capture_executed=false permissions_requested=0 pixel_reads=0 pixel_writes=0
```

The job verifies one output line and an anchored Pass/cleanup schema, so an
unsupported-host Skip cannot satisfy this job. Its separate portable self-test
record is `self_test=pass cases=17 native_calls=0`. Windows/macOS also executed
those 17 portable cases; the Linux test-matrix job did not. This ABI result does
not exercise ScreenCast portal, PipeWire streams/buffers, Wayland, capture,
protected content or hardware drivers.

macOS's synthetic-only ABI probe executed 1000 copied-Block/GC/callback cycles
and 1000 synthetic-sample cycles, with zero live contexts/retained samples. It
explicitly skipped own-window capture. Windows printed that ABI probe's
unsupported-host Skip. On both Windows/macOS the production-driver probe's
default output was:

```text
native_capture_probe=skip; reason=explicit_run_required; preflight_called=false; AppKit_initialized=false; native_capture_executed=false; permissions_requested=0
```

No hosted production-driver capture was executed. Local `--run` evidence must
remain separately labeled; it cannot be inferred from a default Skip.

## Security and extraction evidence

Secret Scan used gitleaks 8.24.3. The actual log reports four commits scanned
with `--no-merges --first-parent` over
`3ccb03b03232b27d818197764365d5d2ef2d6f28^..c5332462880f079a3767e7c6cf4c3d4990c457d3`.
It reports no leaks. Downloaded SARIF 2.1.0 contains 208 rules and zero results;
its driver does not encode the tool version, which is taken from the log. This
is not a new full-history or uncommitted-content secret-scan claim.

Exact-SHA CodeQL analysis `1888762694`, created `2026-10-04T13:21:16Z`, reports
CodeQL 2.27.1, 52 rules, zero results and empty metadata error/warning strings.
Downloaded SARIF 2.1.0 has matching revision/branch/repository provenance,
52 `codeql/csharp-queries` extension rules and `results=[]`. Exact-ref open
alerts returned `[]`. Upload succeeded and processing completed.

The actual extraction log says **421 out of 421 C# files in this invocation**.
The workflow explicitly built all four standalone probe projects during traced
analysis. This closes the prior 391/398 invocation's measured coverage gap, not
a claim of universal security coverage. The log retains three raw diagnostics,
zero summary diagnostics and zero error/warning annotations. REST SARIF does
not contain invocations; zero raw diagnostics is not claimed.

## Artifact identity and reproducibility boundary

All five available archives were downloaded and SHA-256 hashed. Their local
bytes, size, API digest and completed-job upload logs agree; API metadata binds
each to this exact SHA/run and records `expired=false` at audit time.

| Artifact | ID | Bytes | Downloaded SHA-256 |
| --- | ---: | ---: | --- |
| Windows TRX | `11304637188` | 624936 | `7e8d79b714ad8590842a75a1290b6af4b06d4b905aa0cb218910691677bb6b43` |
| macOS TRX | `11304497552` | 626239 | `2b427debd51a4a366b5bb7b4b9491139c37f4b76de687e2014f9cc9b895163b0` |
| Linux TRX | `11304198127` | 628967 | `0d4abd89c5d2906bc28c14caa6b53c4c5e0b10891a08fc89fd88970341924b6a` |
| Secret SARIF | `11304750780` | 6764 | `d835abe333cd10019bd2cfdf0e5504f419be729c9e737a027684e8c5d74d7976` |
| Linux thread-loop ABI | `11303833533` | 1875 | `44e5733d321ebd1e20442d3d5945934fe892f7623d1f5c869492f23de2eb97f6` |

Package jobs depend on the full test matrix and Linux ABI job. Because Linux
tests failed, the package matrix was skipped before expansion. There are no
Linux/Windows/macOS unsigned package artifact IDs, digests or upload logs for
this exact run; previous-run packages cannot establish current packaging.
No signed/notarized build, release or physical two-device result is claimed.

Raw job logs, run/artifact JSON, archives, SARIF, parsed per-case audit JSON and
the stdlib-only read-only parser are retained locally in
`/tmp/flowspan-native-candidate-hosted-37204976024/`. Hosted artifact retention
is 14 days. Retrieve while available:

```sh
gh run view 37204976024 --repo happys2333/flowspan --json headSha,number,attempt,status,conclusion,jobs
gh api repos/happys2333/flowspan/actions/jobs/111444162534/logs
gh api repos/happys2333/flowspan/actions/jobs/111444162478/logs
gh run download 37204976024 --repo happys2333/flowspan --name test-results-Windows --name test-results-macOS --name test-results-Linux --name gitleaks-results.sarif --name linux-thread-loop-abi-evidence
gh api repos/happys2333/flowspan/code-scanning/analyses/1888762694
gh api -H 'Accept: application/sarif+json' repos/happys2333/flowspan/code-scanning/analyses/1888762694
```

Tasks 6/7/8, native safety/permission/input/UI/package gates, physical acceptance
and the v1 Goal remain open. The Linux test failure needs diagnosis and a new
exact-source hosted checkpoint; the successful substeps do not make this run
successful.
