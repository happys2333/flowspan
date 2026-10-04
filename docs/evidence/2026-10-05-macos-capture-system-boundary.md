# Same-Capture native system boundary — local checkpoint

Status: final portable local contracts, complete local regression and selected
real macOS task-owned capture checks pass.
Fresh exact-commit Windows/macOS/Linux hosted verification is pending. This
extracts the existing Capture's system boundary; it does not implement nonzero
stream-delegate composition or grant production sharing availability. MSC task
3, MSC6/MSC9 release debts, Task 6, physical/release acceptance and the Goal
remain open.

## Scope and frozen source

Base commit: `2c6f8fd9fa35f3eece53af2b38ddbb0e94b5cc7b`, implementation branch
`codex/v1-foundation`. This checkpoint changes these three C# files:

| File | Final SHA-256 |
| --- | --- |
| `src/Flowspan.Platform.MacOS/MacOSRemoteWindowScreenCaptureKitApi.cs` | `9bcded04e7b94017ea737d7fcc97d1e84805965ba5ad9e70bf48f8f7f87f4f7f` |
| `src/Flowspan.Platform.MacOS/MacOSRemoteWindowCaptureOperations.cs` | `423533d5f4cffaf7343153b6e002c8a40ff1be054a640371e8a250ec24394630` |
| `tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowScreenCaptureKitCaptureTests.cs` | `e16dc57d7dfdcab2ccc19e3f819ce0b336cb375869138627d932e151bff01464` |

The injected internal factory constructs the same private production Capture.
Native allocation/configuration, output/queue, stream, pool, completion,
release and sample operations form the narrow external-effect boundary.
Retained-source and copied-Block owners have small adapters; wrappers are
allocated before acquiring their native +1 references. Allocation and later
configuration are distinct, preserving a visible owner when a setter fails.
The unmanaged sample trampoline and portable tests invoke one managed sample
core. Enumeration, permissions and window-system APIs are not replaced.

Production remains `delegate=0`, macOS 14.2/Arm64 candidate admission, unknown
protection and unchanged sharing availability. Existing lifecycle/native-call
ordering is preserved. This refactor does not fix native releases under the
Capture state gate or blind retry risk after uncertain release; those are
explicit remaining task 3b/MSC6/MSC9 work.

## Same-state-machine contracts and pressure

Final worker stages are only `23-final-debug` and `24-final-release`, each
passing 17 focused cases and the complete MacOS project at 223/223. Saved source
snapshots and before/after three-file manifests bind these stages. The generated
artifact inventories contain 277 entries each, including hidden files; this
is not a claim that every entry is an executed runtime dependency or a complete
dependency-source manifest. Root's full input/solution proof is separate below.

The 17 cases across nine methods exercise healthy Start/Stop/Dispose, known
configuration-owner failure, settled completion versus callback exit, actual
failed-construction handoff, blocked serial queue barrier, full Stop-failure
quarantine, sample transfer/filtering and consumer-fault containment. Fixture
publication/exit handshakes use small locks and execute external callbacks
outside them. Blocking factory observers use synchronous dedicated controllers
and owner-finally barrier release. A quarantined Capture/source root deliberately
remains retained; tests do not reset global roots or fabricate successful Stop.

Final stage `25-pressure` runs 20 distinct fresh processes in four concurrent
lanes, using final Release artifacts and normal ThreadPool configuration.
All 340 case executions pass, with 20 distinct process/run identities, every
watchdog exit 0, empty stderr and unchanged saved runtime inventory. This is
portable normal-configuration pressure, not single-worker or native pressure.

All new behavioral cases were directly GREEN. There is no new assertion-level
RED→GREEN claim. Stage 01 is an IDE0040 compiler exclusion; adapter-allocation
and fixture-handshake repairs came from static review, not runtime OOM or race
reproduction. Stages 10/11, 13/14 and 17/18 are superseded versions; stage 20
pressure binds old tests, not this final source. There was no stage 12 execution.

Failed diagnostic stages 15/16/19/22 are preserved: each timed out without TRX,
with watchdog 127 and final SIGKILL errno 1. Their child exits are 143/143/143/0;
the last child exit 0 after timeout is not Pass. Cause remains unlocalized.
Stage 21's normal launcher success does not rewrite them. Unverified optional
ThreadPool diagnostic modes were removed from the final fixture. None is
product-case RED or successful single-worker proof.

Worker offline replay:

```sh
python3 /tmp/flowspan-capture-seam-20261005/audit.py
```

Root actually replayed it with exit 0, `EVIDENCE_AUDIT_PASSED` and zero errors.
Script SHA-256 `aad781a4f7635ac0dbc228b80a9cd21bacc0dc26ec04f1f5ac985c3d49066a06`;
JSON SHA-256 `54ee21f0b063b8a04eceb213c3722c98b085fe0920350cbfe54c96419828aca0`.
`process-commands.md` is an audit-time tool-call transcript, not an execution-time
argv/environment manifest. Historical failures are part of this successful
integrity audit, not erased by it.

## Complete root regression

Final root run is `/tmp/flowspan-capture-seam-root-20261005/run-02/`, not run 01.
Thirty top-level commands have saved actual exit-zero records: locked restore,
format, warning-as-error Debug/Release builds/tests, explicit TEST MODE,
simulator, vulnerability query, four tool builds/formats, native-probe Debug
build, no-capture defaults, Foundation proofs, gate/watchdog contracts, portable
Windows self-tests and input/diff checks.

Each configuration has 12 complete TRX files, 2794 total/executed/passed and all
non-success counters zero. Result, definition, execution and entry identities
agree; the complete Debug/Release inventories match. Compared with the exact
`2c6f8fd` hosted macOS inventory, only the 17 new Capture cases were added, with
no removed cases. Canonical qualified-inventory SHA-256:
`ed4b8030d226714fb950a6ff566e967e96ac8164079e084f1b28237ab4be00cd`.

All 553 source/build/helper input bytes match before/after. Each saved canonical
MacOS test runtime has 31 assets. Pre-existing numbered whitespace cloud-sync
conflict siblings are excluded and listed by name only (421 Debug, 1025
Release); their bytes were not read by the final snapshot and originals were
not changed or deleted. Run 01 passed commands through diff-check but was
owner-stopped with exit 143 during a mistaken conflict-file snapshot. Its partial
records remain, and it is not complete final gate evidence.

The including-transitive vulnerability query covers 26 solution projects and
reports no errors or known vulnerable packages. It is not a separately scoped
query for every standalone tool, a full-history secret scan or native audit.

Foundation synthetic/Phase 2a/early mode actually run under independent
30-second process deadlines in this root checkpoint; they remain no-capture
proofs. The early mode retains exact 1057-byte output/empty stderr and its strict
gate passes. Separately, 152 raw-byte fixtures, four POSIX CLI fixtures and 12
watchdog contracts pass. Intended hostile fixtures retain nonzero child/status
facts; the Darwin zombie-only KILL EPERM paths stay fail-closed 127. Harness
success does not turn those child failures into 0 or prove native cleanup.

Root offline replay (the default uses saved bytes and exact Git objects, so
later worktree changes do not invalidate historical replay):

```sh
python3 /tmp/flowspan-capture-seam-root-20261005/audit.py
python3 /tmp/flowspan-capture-seam-root-20261005/audit.py --verify-working-tree
```

Root actually executed both with exit 0, `LOCAL_ROOT_GATES_PASSED_NOT_V1` and
no violations. Default reconstructs all 553 inputs from 550 exact-base Git
blobs plus the three frozen source snapshots. The optional mode separately
checks current source, path sets, input bytes and canonical runtimes; its JSON
does not overwrite the stable saved-data report.

Script SHA-256 `3c44aaa6e832819ca560a3c4a19ffbdaed7d52091395b9eea1bbc88225404522`;
default JSON `191f7397e25a22fa57aac1f6117a4a846ba18b50d216e2cb6c2084f8d2fd481e`;
working-tree JSON `4485a0e400cf28a4b56841b70da228ba921347ad20964fa7be0c8744f001eeea`;
case inventory JSON `befd4384d07836f78c77e3e400424e8a76664b1a82a61b10908b34650dba2741`.
Independent saved-data/Standards/Spec audit review has zero current findings:
`/tmp/flowspan-capture-seam-root-20261005/audit-independent-review.md`, SHA-256
`e5b15d6f24d88169dd78116a2df82615eeb69d675d39e4d61afb6c59891c3701`.

## Actual task-owned macOS regression

After root's frozen Debug/Release builds, the standalone native probe actually
executed `--run` once in each configuration on macOS 27.0.1/build 26A434,
ordinary arm64, SDK 10.0.301/runtime .NET 10.0.9. Neither run is Skip. Evidence:
`/tmp/flowspan-capture-native-regression-20261005/{debug-run01,release-run01}/`.
Their outer observers independently record actual runner exit 0; runner,
watchdog and native exits are all 0. The process deadline is 120 seconds, grace
two seconds and final leader-join bound five seconds. Actual child executions
finish in approximately 4.82/4.98 seconds, without signals, errors, timeout or
interruption. Computed `gate.return-status.raw` alone is not actual CLI exit.

Both native stdout files are identical 884-byte, three-record LF-only outputs,
SHA-256 `98ac3aee6f431a0d8e51a5c7a8821d43d0acbbf4f6702af3ece546bbb047eb33`;
stderr is empty. The validator checks every field, duplicate/missing/extra
records, byte normalization and independent process facts. It reports
`PASS_SELECTED_TASK_OWNED_NATIVE_CHECKS_NOT_V1`. Root independently replayed
both raw validations and byte comparison successfully without another capture.

The tool captures its own 64x64-point window as a tightly packed 128x128 frame
at scale two; raw and catalog/boundary each report one marker frame. Start/Stop
race, independent sample drain, generation binding, boundary StopCompletion,
absent-application preservation, default own-process exclusion, hidden-window
invalidation, finite 600-ms zero-late-delivery checks and final zero Capture/
retained-owner counts pass. Reported permission requests, title reads and pixel
writes are zero. These are reviewed-tool facts, not independent syscall
instrumentation. The late counter covers downstream sample delivery, not every
native ABI/sample entry, and two zero counts alone are not full cleanup proof.

Both runs preserve identical before/after 276-entry conservative source/build
inventories and 16 canonical runtime assets from the probe's deps graph. Extra
runtime siblings are only listed by name, never read/removed; this is not a
clean package or compiler-inclusion proof. Source inventory SHA-256:
`c1fb2918fb61dec1453d58d8edfd1913b4dae2689a9c32aeee9fcbc425cdc088`.
Debug/Release gate report SHA-256:
`8b5d3972b343c7495a0bc3d3acaa11a6b2113f2befe3935f941e528c155514b2` /
`25bbd878b6ce688c8b0e6d235f0d82ac3c7f2d130d7eb299094b5c30ebdd21ce`.
Both validation JSON digests are
`f434b4eadb4b55475605396f4df06157109889522998191ed61dd5db0d9216af`.

Temporary runner/validator each have independent static review with no remaining
remediation finding and 13 passing synthetic CLI contracts. Synthetic artifacts
are labeled synthetic even in nested validation; they are never native proof.
Runner SHA-256 `489ddcc563f6ae8c378966306e0c9347ea1d14ac513b4203f13a5abfed2e0f60`;
validator `ae2b2d4d6d70d296bfae5ffcb9c56637b2e604a683225e7fb311ad435dd94b21`.
Full limits and observed runtime digests are in that directory's `SUMMARY.md`.
Its SHA-256 is
`c3d3033d8e1c27d29b9e7e563cc0d2d387fd4476c991732a0fe789eef1c8d0c7`.

Global enumeration can expose other-window metadata; task-only capture does
not isolate it. Permission revocation/consent UI TOCTOU, same-process window-ID
ABA and native protection (still Unknown) remain unproved. These healthy local
runs inject no native Start/Stop/output/barrier faults and prove no nonzero
delegate, native delegate drain, minimum-OS or Windows/Linux/physical behavior.

## Static review and remaining gates

Standards: zero new hard violations or judgement-level blockers. The final
fixture preserves dedicated controllers, finally joins and shared-lock
handshakes; the threat model explicitly retains old gate/retry debts.

Spec: zero P1/P2 findings for task 3a. Same Capture, ownership ordering,
completion-exit distinction, failed factory/barrier, quarantine and sample
contracts match its scope. Both reviews read the final hashes above and did
not execute native APIs or tests.

Actual nonzero-delegate Capture, terminal without sample, global native fault
admission, complete-cleanup permit return, source-loss/Stop-error fault injection,
native sample-entry versus sink-frame counts, minimum OS/architectures,
protected/secure input, independent Emergency Stop, physical devices,
accessibility, signed installation and v1 release remain independently open.
