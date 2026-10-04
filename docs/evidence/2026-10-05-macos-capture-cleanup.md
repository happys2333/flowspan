# Same-Capture cleanup prerequisite — local checkpoint

Status: frozen same-Capture portable contracts, complete local Debug/Release
regression and selected actual macOS capture checks pass. Final pressure/evidence
audit passes; fresh exact-commit hosted verification remains
pending. This is MSC task 3b.1, not
nonzero delegate composition, production sharing or v1 acceptance.

## Scope and frozen source

Base: `473c625e566a7f603ec54e6ee0e1baaa8b070c76`, branch
`codex/v1-foundation`. Only the API and Capture tests change; the operations
interface is included in the frozen boundary but unchanged.

| File | SHA-256 |
| --- | --- |
| `src/Flowspan.Platform.MacOS/MacOSRemoteWindowScreenCaptureKitApi.cs` | `37e3f675973adda0145681d03d562719989e9abcf2a2891dbbe9204cd1619656` |
| `src/Flowspan.Platform.MacOS/MacOSRemoteWindowCaptureOperations.cs` | `423533d5f4cffaf7343153b6e002c8a40ff1be054a640371e8a250ec24394630` |
| `tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowScreenCaptureKitCaptureTests.cs` | `b45323a08f3ea705f53bb7c8aeb3d9c134a1c0dc27a4d6562fe3142aeaefae6a` |

The same production Capture claims cleanup under short gates and performs
external completion/object/queue/source releases outside them. Each known owner
has at most one cleanup attempt, separate from confirmed release. Unconfirmed
effects retain the complete GCHandle graph and are not retried. Output-index
removal matches the exact Capture atomically; neither uncertain address
retention nor repeated cleanup can overwrite/remove a replacement's routing.

A managed shell is rooted before source retain and per-Capture native work.
Until source retain returns, the source is borrowed and cannot be disposed by
this Capture. Unknown retain conservatively preserves the shell. Invalid
geometry and early configuration failures use the same owned cleanup path;
factory diagnosis and original nested fatal identity survive failed cleanup.
Cleanup faults close delivery and reject cached successful Start. Confirmed
Block release and contained diagnostic failure remain independent.

Production remains `delegate=0`, macOS 14.2/Arm64 candidate admission, unknown
protection and unchanged sharing availability. NativeSource's own algorithms,
partial native retain-token staging and other acquisition-before-assignment
uncertainty are not solved by this Capture-only patch. Physical IsDrained is
still sample/Block readiness, not complete owner release or delegate drain.

## Actual vertical TDD and final local contracts

Evidence: `/tmp/flowspan-capture-cleanup-20261005/`. Final lanes are only
`29-final-debug` and `30-final-release`: each passes 32 focused cases and the
complete MacOS project at 238/238, with actual command exit 0, saved source
snapshots and equal before/after owned inputs/runtime inventories.

| RED | GREEN | Observable behavior |
| --- | --- | --- |
| 02 | 03 | independent same-owner observer returns while object/source release is held |
| 04 | 05 | consumed object release is not retried |
| 06 | 07 | consumed queue release is not retried |
| 08 | 09 | consumed completion release is not retried |
| 10 | 11 | uncertain source cleanup remains charged on retry |
| 12 | 13 | early configuration failure retains a durable owner |
| 14 | 16 | invalid geometry retains factory diagnosis and uncertain source |
| 17 | 18 | four cleanup-fault rows reject cached successful Start |
| 23 | 24 | old uncertain cleanup preserves replacement output routing |
| 27 | 28 | unknown source-retain cleanup rethrows the original nested OOM |

01, 15 and 19 are compiler-only exclusions, not behavioral RED. Supplemental
direct-GREEN coverage includes a no-inlining weak-reference helper and forced GC
after factory-slot replacement, borrowed-source protection, healthy invalid
geometry, release-fatal identity, confirmed Block/diagnosis separation and
rejected incumbent-index registration. Tests do not reset quarantine roots.
Successful 21/22 and 25/26 lanes are superseded, not final evidence. A denied
direct stage-12 shell invocation occurred before the actual bash test run;
it is not a product test result. The ten actual RED rounds contain 14 failed
executions across ten distinct final case rows; five added rows are direct GREEN.

Final `31-normal-pressure` has 20 fresh dotnet leader PIDs and 20 distinct TRX
run IDs across four lanes, all 640 case executions Passed. Actual execution-time
argv/cwd/helper/PID/environment records bind the runs to stage 30's Release
artifacts, with no inherited ThreadPool overrides recorded. The 277-entry worker
artifact inventory matches before/after; this is not 277 executed dependencies
or a complete compiler-input manifest. Root's separate full input proof follows.
This is normal-launcher portable pressure, not single-worker or native pressure.

Root actually replays the final worker audit with exit 0,
`EVIDENCE_AUDIT_PASSED` and no errors:

```sh
python3 /tmp/flowspan-capture-cleanup-20261005/audit.py
```

Script SHA-256: `6008a61f280c57d8513585daad5183bb14425ea37f44ad68a990b50f1833341d`.
JSON SHA-256: `41c94ce0342ca90c82ed74106380c19e807bb075d68cebfb0a5b6a1fae44cbc4`.
Historical stage invocation argv/environment are not independently saved;
their runner bytes, inputs, outputs and raw exit records remain available.
The new pressure manifests do not retrospectively strengthen that provenance.

## Independent complete root gates

`/tmp/flowspan-capture-cleanup-root-20261005/run-01/` builds an independent
exact-base Git archive plus the three frozen source files. Thirty fresh
commands and the runner itself record exit 0: locked restore, format,
warning-as-error Debug/Release builds/tests, explicit TEST MODE, simulator,
vulnerability query, standalone tool builds/formats/defaults, Foundation
helpers, raw/CLI/watchdog fixtures and input/diff checks.

Debug/Release each contain 12 TRX / 2809 total, executed and Passed cases,
all non-success counters zero and identical complete qualified inventories.
Compared with the prior inventory, no cases are removed and exactly 15 Capture
cases are added. Prior outcomes are not reused. Canonical inventory SHA-256:
`d354c07f1fe1a99d78561ecc7a4a775242651e81b7f7a39a239425b0557fc8cb`.

All 553 inputs match before/after: 550 exact base blobs plus three frozen
snapshots. Archive bytes are checked against captured Git blob IDs. Each
configuration's 31 canonical MacOS runtime assets match before/after. The
186-file documentation snapshot is provenance only, not compiled-input or
documentation-correctness proof. Original root bin/obj, conflict siblings and
older dirty worktrees are not changed, copied or read by this independent run.

The solution vulnerability query covers 26 projects including transitive
dependencies, with no reported known vulnerabilities or errors; it is not full
security clearance or an individually scoped query of all standalone tools.
Fresh Foundation helpers pass their no-capture contracts. The 152 raw fixtures,
four CLI contracts and 12 watchdog contracts pass; intentional nonzero child
outcomes and Darwin EPERM failures remain failures. Leader join is not universal
descendant/native cleanup. No actual window capture belongs to this root gate.

Root actually replayed the saved-data audit with exit 0,
`LOCAL_ROOT_GATES_PASSED_NOT_V1` and no violations:

```sh
python3 /tmp/flowspan-capture-cleanup-root-20261005/audit.py
```

Default replay is independent of future root Git/source/bin/obj state.
Script SHA-256: `a4cd428bf3a6e1ca2aa221a8ff0a74828007135daab57257fefca730ecbbf77d`.
Default JSON: `f0906c780557b00bce01ae42869868dbb4d7b0ba8066c17920fd686732296d32`.
Case-inventory JSON: `f6629037e89fcee78bb658830e323c239a7f30f04dcd00bba9a254f73d2a02ed`.

## Actual macOS regression and static review

New task-owned `--run` executions use the independent root build-tree's Debug
and Release NativeCaptureProbe binaries, not old built outputs. Each actual
observer, runner, watchdog and native exit is zero, without Skip. Evidence:
`/tmp/flowspan-capture-cleanup-native-20261005/{debug-run01,release-run01}/`.

Root independently replays both raw validators successfully and compares the
outputs byte-for-byte: each is 884 bytes with empty stderr, SHA-256
`98ac3aee6f431a0d8e51a5c7a8821d43d0acbbf4f6702af3ece546bbb047eb33`.
Complete lifecycle/security records pass: 128x128/scale two, raw and boundary
marker frames, Start/Stop/sample readiness, generation binding, hidden-window
invalidation, original own-process exclusion and finite late-delivery checks.
Permission requests, title reads and pixel writes are reported zero. These are
reviewed-tool facts, not independent system-call instrumentation.

The 553 compiled-snapshot inputs are checked with actual successful shasum
commands before/after. Source snapshots, freeze receipt and current code agree.
Each run separately retains unchanged 276-entry conservative source and 16-entry
canonical runtime inventories. Debug/Release gate-report SHA-256:
`c22697d24b09eb34244fdc1832348ba3c43e76645d8f710eb791d345fd471b2a` /
`3b274bf35b776ed9c12bce8d02f19615d99d3314463d051e813d58750ebae81d`.
Native summary/input-binding JSON SHA-256:
`72283a7c6c7890ff208d6e52f15c34da764aaa37ba081602487fcad2d8d346c6` /
`cf845b305fea857aabf5d7d8634dd31b2cfe3df3772ed765ccfad289612ce1b3`.

This healthy `delegate=0` run is not native failure injection, NativeSource
partial-ownership verification, delegate drain or physical two-Device proof.
Global enumeration metadata, TCC TOCTOU, same-process window-ID ABA, Protection
Unknown, finite observation and watchdog/cleanup distinctions remain explicit.

Final static two-axis review reads the frozen working-tree diff against
`473c625`: Standards 0, Spec 0. Earlier signatures are superseded. Root rereads
the final source/report and confirms the address-reuse and original-fatal
findings are repaired. The report is
`/tmp/flowspan-capture-cleanup-20261005/final-preservation-review.md`, SHA-256
`d1f9d761d65c8e75c3d84bc50bb783f3386ccb410d69bb9b33749b41397f589d`.
Static review is not execution evidence or external security certification.

## Remaining gates

Fresh exact-SHA Windows/macOS/Linux CI and CodeQL remain pending for this patch;
the prior [473c625 hosted success](2026-10-05-capture-boundary-hosted-checkpoint.md)
cannot verify changed source. Actual NativeSource lifecycle/acquisition,
nonzero delegate/initializer/runtime admission, terminal versus Start completion,
managed retirement and bounded complete-cleanup permit return remain task 3b.2.
Native SCStream fault injection, MSC2b/MSC6/MSC9 aggregate acceptance, Task 4/6,
production sharing, protection/input/Emergency Stop, physical LAN, minimum
OS/architectures, accessibility, signing and v1/Goal acceptance remain open.
