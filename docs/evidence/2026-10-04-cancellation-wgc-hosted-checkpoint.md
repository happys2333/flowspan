# Exact-checkpoint hosted CI — 2026-10-04

Exact source: `470d0f354f420b39bdc1ad5736bcb66acb01792d`, branch
`codex/v1-foundation`. Specified main implementation-branch CI and CodeQL runs
succeeded. This is a hosted managed/no-capture checkpoint, **not v1 acceptance,
production native capture, physical-device acceptance or a release**. No other
ref/run was substituted, no rerun requested, and the prior `c533246` CI failure
remains historical failure. This evidence record is not a GitHub message; the audit
made no repository edit, commit, push, dispatch/rerun/cancel or GitHub post.

## Exact runs and final jobs

Both are run237/attempt1/push on the exact SHA/branch. CI created
`2026-10-04T14:07:13Z`; final completed/success API updated
`2026-10-04T14:14:43Z`. All eight CI jobs succeeded; the configured verification gates passed.

| Workflow/job | ID | Result |
| --- | ---: | --- |
| [CI](https://github.com/happys2333/flowspan/actions/runs/37208080273) | `37208080273` | success |
| Secret scan | `111453391962` | success |
| Linux thread-loop ABI (no capture) | `111453392075` | success |
| Test (windows-latest) | `111453392080` | success |
| Test (macos-latest) | `111453392086` | success |
| Test (ubuntu-latest) | `111453392146` | success |
| Package (linux-x64) | `111454347733` | success |
| Package (win-x64) | `111454347782` | success |
| Package (osx-arm64) | `111454347820` | success |
| [CodeQL](https://github.com/happys2333/flowspan/actions/runs/37208080267) / Analyze C# | `37208080267` / `111453391714` | success |

Three Test jobs passed locked restore, full-solution format, Release
warnings-as-errors build (zero compiler warnings/errors), explicit TEST MODE
composition, protocol1.7 simulator and every configured standalone-probe
build/format/default/self-test step. Not every job log is warning-free.

| Actual test OS | Image/version |
| --- | --- |
| Ubuntu24.04.5 x64 | `ubuntu-24.04` / `20260927.320.1` |
| Windows Server2025, 10.0.26100 x64 | `windows-2025-vs2026` / `20260925.250.1` |
| macOS26.6.2, 25G83 arm64 | `macos-26-arm64` / `20260907.0351.1` |

Hosted SDK10.0.401/runtime10.0.12, not local SDK10.0.301/runtime10.0.9.
global.json permits latestFeature roll-forward. Moving hosted labels do not
represent all supported OS versions or physical devices.

## All 36 downloaded TRX independently checked

Every OS has12 project TRX files,2692 distinct test names and **2692/2692 passed**.
No duplicate/missing cases. XML total/executed/passed agree with individual
records; every other counter is0. All three inventories are identical, SHA256
`2a0063954cb74c5f57aa4ed49d92c1e2abc0f128bc040f4cc8acc584da71b7ad`.
Transport764/764 and Desktop757/757 on each OS. Six new dispatcher contracts
were individually Passed:

- `CanceledCallerPreservesOriginalTokenBeforeLinkedCancellationPropagates`.
- `UncanceledCallerPreservesOriginalReceiveEof`.
- `CallerCancellationDuringCleanupCannotRelabelAlreadyRecordedEof`.
- `CanceledCallerCannotRelabelReceiveAggregateContainingIo`.
- `CanceledCallerPreservesOriginalReceiveOutOfMemory`.
- `CanceledReceiveIoAndOwnedCleanupFailureBothRemainObservable`.

The real-TCP case
`DesktopActivityRuntimeTests.AuthenticatedRuntimesExchangeNoteAndExposeOnlyEligibleLiveTarget`
and old `RegistryDisposeStartsEveryOwnedRouteBeforeJoiningCleanup` are Passed
on all three OS, without erasing either prior failure. MacOS126/126 per OS;
candidate62/62 = SourceCatalog32 + CaptureOwnership27 + CaptureBoundary3.
All four first-frame startup regressions passed and remain a subset of62.
`trx-audit.json` preserves project counters and per-case outcomes;
`violations=[]`.

## Observed no-capture probes

Each OS executed `self_test=pass cases=10 native_api_called=false`,
`wgc_self_test=pass cases=12 native_api_called=false` and
`self_test=pass cases=17 native_calls=0`. Main CI **did not invoke opt-in WGC**.
Actual Windows default stdout:

```text
probe=pass mode=warp api=D3D11CreateDevice driver=WARP apartment=MTA source=7x5 content=3x2 bytes=24 row_pitch=12 feature_level=0xb000 map_attempts=2 owned_refs=4 released_refs=4 sha256=fc865b98e8180228df0ec6c60cd9a919aa9033ae1b802fbfefe91ede9ac2e3af window_capture_executed=false permissions_requested=0 pixel_files_written=0
```

Four acquired COM references balanced, not driver-internal leak proof.
RowPitch12 does not exercise native padding. This is in-memory WARP readback,
not WGC/native window capture/protection/input/hardware/physical acceptance.
macOS/Linux default WARP explicitly Skip, not native Pass.

Strict independent Linux ABI job installed libpipewire-0.3-0t64
1.0.5-1ubuntu3.3 amd64; library1.0.5. Downloaded native.stdout is one line:

```text
probe=pass mode=thread_loop_abi reason=none library_version=1.0.5 native_result=0 native_calls_executed=true owners_released=true loop_roundtrip=true lock_roundtrips=2 native_timespec_bytes=16 native_time_valid=true outside_loop_thread=true daemon_connections=0 portal_calls=0 stream_creations=0 hardware_operations=0 window_capture_executed=false permissions_requested=0 pixel_reads=0 pixel_writes=0
```

Anchored Pass/cleanup schema rejects Skip. Artifact records portable17,
Ubuntu image20260927.320.1, kernel6.17.0-1022-azure, SDK10.0.401/runtime10.0.12.
It proves thread-loop ABI/owner cleanup only, not portals, streams/buffers,
Wayland, capture, protected content or hardware drivers.

macOS synthetic ABI probe executed1000 copied-Block/GC/callback cycles and1000
synthetic-sample cycles; zero live contexts/retained samples. Own-window capture
explicitly skipped. Windows/Linux skip that macOS ABI mode. Every OS's
production-driver default is:

```text
native_capture_probe=skip; reason=explicit_run_required; preflight_called=false; AppKit_initialized=false; native_capture_executed=false; permissions_requested=0
```

No hosted production-driver capture in this main CI. Separate same-SHA native
runs are not substituted for the specified branch run.

## Security provenance and limits

CodeQL analysis1888869803 created14:12:01Z inside job111453391714, matching
exact SHA/ref/run/check-suite. CLI2.27.1, selected52rules/results0, API
error/warning empty, SARIF processing complete/errors null. Actual extraction
**426/426 C#**, exact Git tree426 (src225/tests178/tools23); all four probes
explicitly built under traced analysis. This is count coverage, not per-file
TRAP auditing or universal security proof. Logs:3raw/0removed/0summary
diagnostics. One runner-migration notice. Downloaded SARIF2.1.0 is GitHub API's
reconstructed representation, not byte-identical original upload; no original
invocations/raw-diagnostic payloads. Exact-ref open-alert snapshot0.

Gitleaks actually8.24.3 with `--log-opts=-1`: only last **1commit/~131960bytes**,
no leaks. Full-history checkout is not full-history scanning. SARIF208rules /
0results; generic semanticVersion is not actual executed version. Two Node
deprecation warnings retained. Not a working-tree/universal-secret-clean claim.
Full provenance, independently APPROVE-reviewed checks and raw security data:
`security/security-evidence-draft.md`, `security/security-audit.json`.

## Archives and unsigned-package handoff

First five archives fully downloaded: localbytes/hash, API digest/source and
completed-job upload logs agree. API binds all eight to this exact run/SHA/branch,
expired=false. Last three rows are **API/upload-log facts only**; complete local
package download/hash/internal verification is **pending at handoff**.

| Artifact | ID | Bytes | SHA256 | Local check |
| --- | ---: | ---: | --- | --- |
| Windows TRX | `11305127617` |625702| `6d5eaf883fdafc347bb8d8983a3405bd089b0e6ffd964cbeb09d4a0d1934bc5b` | complete |
| macOS TRX | `11305787520` |627999| `9736a7a6a47cfaa813cdc6cb246c8261a61be89312ee93df38de9c71d3ac7dc9` | complete |
| Linux TRX | `11304923064` |628868| `31d99f8e6b583bdc9ae36b753d6107dfc04df3e7c13f71366ce511756edb87c8` | complete |
| Secret SARIF | `11305722346` |6764| `b862a801cefd7f8dd553a727efa6e9c06fe940591d0e935690e32ffff834f8ed` | complete |
| Linux ABI | `11305102419` |1875| `f67102d9fce1edfa9569f12c139a7a652ab95184f17bddcbbea7f86eeae9d614` | complete |
| linux-x64 unsigned | `11305887335` |42093756| `1b4ff8abbd9aa2295bfa4d0ad6e144e091b35617c0b7805f0840a323b88807da` | pending |
| win-x64 unsigned | `11305192762` |44097224| `ecae08580ba7530411686dff0fb896b8eea44a03073ff9da42d3cc98c4b09aaa` | pending |
| osx-arm64 unsigned | `11304918188` |42925355| `dc4e1184c9dbb10922e4c38f3272bcb79488e5165389e9f3cf8d30e3821704d1` | pending |

Three package jobs passed self-contained single-file publish, content-locked
build inputs, packaged TEST MODE composition, two seals/two verifies and
same-job recursive package comparison, plus26-project direct/transitive
vulnerability query with no reported vulnerabilities. Workflow signature-state
`unsigned-test-artifact`; version0.1.237/build0.2.37. Hosted steps are not local
download verification, independent rebuild reproducibility, signing/notarizing,
installation/release/physical acceptance. Do not treat partial downloads as
complete packages. Package logs/API/upload facts and pending state in `packages/`.

## Reproduction and open gates

All evidence is under `/tmp/flowspan-checkpoint-hosted-470d0f3/`: raw API JSON,
exact workflow/global.json snapshots, archives,36 extractedTRX, job logs and
read-only verifiers. Artifact retention14days. Completed checks:

```sh
python3 /tmp/flowspan-checkpoint-hosted-470d0f3/audit_tests.py
python3 /tmp/flowspan-checkpoint-hosted-470d0f3/audit_logs.py
python3 /tmp/flowspan-checkpoint-hosted-470d0f3/security/audit_security.py
```

Read-only retrieval:

```sh
gh run view 37208080273 --repo happys2333/flowspan --json headSha,headBranch,number,attempt,status,conclusion,jobs
gh run download 37208080273 --repo happys2333/flowspan
gh api repos/happys2333/flowspan/code-scanning/analyses/1888869803
gh api -H 'Accept: application/sarif+json' repos/happys2333/flowspan/code-scanning/analyses/1888869803
```

Cancellation/EOF repair now has matching-source three-OS hosted evidence.
Native source-loss/protection/input/permission/stop ownership, production
composition, physical two-device/LAN, signed/notarized installation and other
v1 gates remain open. Task6/7/8 and the active long-term Goal must not close
because this checkpoint succeeded.
