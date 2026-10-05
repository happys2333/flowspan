# Initial source-producer — staged behavioral evidence

Status: implementation and final local verification complete; fresh exact-SHA
hosted gates pending. Catalog production
enumeration now forwards its bounded context; real NativeSource is rooted before
return and hands the same record to SourceEntry. Stable context/token checks
protect batch reuse and late cleanup. Finite direct no-context envelopes and the
scoped allocation/initialization/cleanup matrix now pass38 controlled-effect
cases. Final local project D/R passes317 cases; solution D/R passes2888.
Selected new task-owned macOS native D/R passes separately. These are not fresh
hosted/MSP acceptance, production-sharing or v1 completion.

Base checkpoint: `3aa099becb579395ee150eac65fbe4d8c7838c5c`.
Specification: `specs/v1/native-remote-window/macos-source-producer/`;
[ADR0032](../adr/0032-pre-reserved-macos-source-producer.md).
Raw snapshots/commands/cwd/exits/TRX/source and runtime inventories:
`/tmp/flowspan-source-producer-20261005/`.

Stages01–03 are compilation/analyzer correction candidates, not product REDs.
Stage04 `InitialRetainAfterEffectFaultKeepsDebt` actually runs and fails with
exit1,0Passed/1Failed. Before the failing usage assertion, it verifies the
original nested fatal, window reference count2, one confirmed-filter cleanup
and no guessed window release. After batch/catalog settlement it expects
`(Catalogs, Sources, Batches)=(1,128,1)` but observes `(0,0,0)`.

Stage05 actually exits0,1Passed/0Failed after pre-effect acquisition-ledger
linkage and marking the exact source/batch failed before context notification.
The same real creation core is used; controlled effects replace native calls.
The entire ProducerTests file is byte-identical between04/05; each stage's
saved input-before/after manifest is equal. Root actually inspects both exit
receipts/TRX and executes these comparisons, rather than inferring a result
from the stage directory name. Forced GC intentionally preserves only the
fresh real pool; effects/context/catalog observations remain weak. This proves
managed graph retention for that test entry, not native reference survival.

The implementation agent stopped on a reported usage-limit error after those
records were saved. Root preserves its partial edits and continues from current
files. Independent final review, complete D/R and new hosted gates have not run
for this producer implementation. SourceEntry/Catalog's successful `3aa099b`
CI remains a separate checkpoint and is not inherited here.

Root then records actual stage06 `InitialRetainFatalSurvivesConfirmedFilterCleanupFailure`
RED: exit1,1Passed/1Failed, expected the original OutOfMemoryException instance
but received no fatal because finally's filter-cleanup IOException replaced it.
Stage07 minimal correction actually exits0,2Passed/0Failed. Cleanup attempts
remain independent; preattached ledger records release attempts/confirmations,
and cleanup failure closes/charges the context before propagating the original
fatal in preference to cleanup failure. Root checks the raw TRX/exit receipts
and entire test-file equality between06/07; both test files have SHA-256
`3c7c155ab81c8fe7ec1b5ade324b818dc40d353a6d1792798bbb7d895f5169de`.
These are fresh isolated Debug snapshots based on documentation commit0a4c817
plus the five in-progress producer source/test overlays, not current hosted
verification or complete solution/native evidence.

Stage08 independently rebuilds a new isolated Debug snapshot and actually
exits0,281Passed/0Failed for the complete MacOS project. Root runs
`audit-project-debug.py` with exit0, verifying TRX result/definition/entry and
execution identities, all279 prior project identities plus exactly the two
new cases, zero removals and750 saved source/document bytes unchanged. This
is byte provenance, not compiler-inclusion proof. The deterministic saved-data
audit writes its report; script/report SHA-256:
`9558b3c3a70d7c88d14ba6f881dc415f12cac475dca35c1746578e2e7de29088` /
`e9f3cb0b09cf159040e32513c6e66c1db500b572b6410f6c573cdf1f85219236`.
Release, complete solution, final format/review, native and fresh hosted gates
remain open. The second case does not prove every two-confirmed-owner cleanup
combination; broader independent-cleanup and successful-handoff contracts remain.

Next behaviors include independent confirmed owner cleanup, allocation/init uncertainty,
nil/changed-self semantics, same-record successful handoff, complete healthy
return, exact-context admission and finite direct-call envelopes. Content/Block/
callback/dispatch/query-internal resources and whole-list settlement remain
separate. No production sharing or platform floor change is made.

## Continued ownership tracers and frozen local progress

Stages09/10 prove successful NativeSource remains reachable through the fresh
real pool before batch settlement: actual2Passed/1Failed becomes3Passed.
Stages11/12 exercise real Catalog refresh and missing context forwarding:
3Passed/1Failed becomes4Passed. Stages13/14 fill all128 producer slots and
publish without another slot:4Passed/1Failed becomes5Passed. Stages15/16 show
batch settlement must not return an undisposed source:5Passed/1Failed becomes
6Passed, with pool-only GC reachability and complete healthy source return.
Stages17/18 reject stale creation contexts before allocation for both same and
replacement catalog reuse:6Passed/2Failed becomes8Passed. Exact context also
guards stale settlement of a replacement batch. Each pair has identical full
test bytes, not only unchanged method names.

Stages19–21 retain a mistaken expectation that ordinary Refresh failure throws.
The existing contract returns LocalBoundaryResult; these are preserved correction
candidates, not a valid late-failure RED→GREEN pair. Corrected stage22 uses saved
stage19 production code plus the corrected current test and actually fails0/1:
it reports capacity exhaustion rather than closed-catalog unavailability.
Stage23 passes1/1 with stable original-owner notification and exact ledger/native/
context checks; the replacement context still creates and returns its own source
normally. Both confirmed owners receive one cleanup attempt. Stages24/25 cover
zero/foreign initial-retain returns:0Passed/2Failed becomes2Passed; no source
publication or guessed window/foreign release is allowed.

Stages26/27 are an intermediate project D/R checkpoint,290Passed each, before
the reentrant-settlement tests. Root executes `audit-progress.py` with exit0,
no violations, seven valid pairs,750 source files and138 bin files per stage,
equal before/after source manifests and all saved hashes replayed. All281 stage08
identities remain, exactly9 are added, and D/R identities match. Script/report
SHA-256:
`1906baa7a413caafa0e54046a8fa51a6d567af8b14b83f853809acaa2d364c83` /
`55662aa26af67b2e9dc16c798c0d6fcc0f5bb61ce9367526c59e3b4db9f5fd24`.

A single-layer progress review identifies premature settlement during initial
allocation. Stage29 actually fails11Passed/1Failed: after reentrant settlement
and an original nested allocation fatal, usage is0 rather than(1,1,0).
Stage30 passes12/12 with identical test bytes: an unconstructed NativeSource no
longer authorizes ledger return; only confirmed terminal cleanup does. The
successful reentrant/reused-batch branch is then reproduced in stage31,
13Passed/1Failed: old Catalog incorrectly reports success. Stage32 passes14/14
after source attachment validates the still-active exact context, refusing a
late source before grafting it onto replacement slots and independently cleaning
both confirmed owners. An additional healthy published-source test verifies that
native cleanup alone cannot return its live entry/registry charge; it was already
GREEN and is not claimed as a RED.

Frozen stages33/34 actually pass293 project cases each;35/36 actually pass2864
solution cases each, with12 independent TRX files per solution run. Root executes
`audit-settlement.py` with exit0 and no violations: saved source/runtime hashes
and before/after inventories replay, every2850 prior solution identity remains,
exactly14 ProducerTests cases are added, and complete D/R identity sets match.
All four runs have the same750-file source snapshot. Script/report SHA-256:
`aea1010b6098640aedc4181943e8b045961a60ba1ac580e33f6e9729f226875c` /
`223c4a01f0dd22a4523576239b24ea24213c472026407d34034e581b3c2a2a5f`.
Byte binding does not prove compiler inclusion or independent per-testhost runtime
version. Current-code format verification and `git diff --check` also exit0.

Stage37 compiles the actual NativeCaptureProbe Release wrapper with explicit
context forwarding from the same saved source snapshot: exit0,0 warnings,
0 errors. Root executes `audit-probe-build.py` with exit0, replaying750 source
files, equal before/after manifests and all compiled runtime hashes. This is
compile-only, not ScreenCaptureKit/window/frame/device execution or the selected
healthy native regression gate.

This is progress verification, not final MSP6: finite direct envelopes,
allocation/init nil/changed-self and broader fault contracts, selected healthy
native execution, final single-layer standards/spec review and fresh exact-new-
commit all-OS CI/CodeQL remain open. Old3aa/0a4 hosted success does not transfer.
Delegate=0, macOS14.2/Arm64 candidate admission, Protection Unknown and production
sharing unavailable remain unchanged; MSC9/v1/release/Goal stay open.

## Finite direct envelopes and final local implementation

Stages38/39 record real direct-lifetime RED→GREEN (14Passed/1Failed becomes
15Passed): a returned real NativeSource must retain its same-pool source/owner
charge through healthy batch settlement and return it only after confirmed
Dispose. Stage40 removes the nullable creation-context bypass without changing
test bytes. Subsequent contracts independently exhaust owner, batch and128-slot
source capacity before entry/effects; verify full empty-capacity return; preserve
pool-only live-source and unknown acquisition graphs through GC; and isolate a
late failed direct source from a reused replacement batch. No synthetic Catalog,
second pool, eviction, finalizer or timeout return is introduced.

Stage42 is a preserved CA2012 test-code analyzer candidate, not behavioral RED.
Stage47 actually fails24Passed/1Failed because nil allocation still enters init;
48 passes25 with identical test bytes after skipping init on nil allocation.
Nil initialization consumes its receiver without producer release; changed self
owns only the returned filter. Init after-effect failures for nil/same/replaced
self retain debt and never release a guessed receiver/result. Independent known
owner cleanup covers either/both release failures; rejected geometry/currentness
and original query fatal plus optional later cleanup failure are also checked.

Two-axis single-layer review finds one MSP3 direct-diagnosis mismatch. Stage62
actually fails37Passed/1Failed: an ordinary identity IOException escapes rather
than the required bounded diagnosis. Stage63 passes38 with identical test bytes
after preallocating a fixed no-inner diagnosis before effects and preserving the
original nested fatal. Standards and Spec remediation review have no remaining
implementation finding. Pre-fix54–57 passes are retained as superseded progress,
not final-code verification. Invalid build-command60/61 (MSB1001) are preserved
and never counted as product RED or native execution.

Final63/64 focused D/R each pass38;65/66 project D/R each pass317;67/68 complete
solutions each contain12 TRX/2888 Passed, with every2850 baseline identity
retained plus exactly38 producer cases. Full D/R qualified inventories match.
All six phases use the same750-file frozen snapshot, manifest SHA-256
`dff4296649cf379b362abac5b06534867eb578d260dede29f29f51cc3a5fb4eb`;
each solution runtime inventory contains1525 files. Snapshots use0a4c817 plus
seven implementation/test overlays: archived documentation is not asserted to
equal current documentation, nor do byte inventories prove compiler inclusion.

Root actually executes the independent saved-data v2 audit, exit0,
violations=[]; its immutable report matches the independent replay. Script/report
SHA-256:
`7e02249c39f3cd3fdc830e5b906e15750e6beb331607d3319399fd7dab88010f` /
`f9b61af231f5d380a5148af3cba207925d5d7a4060e35a26dbf216ce400360fc`.
The old audit/progress report digests remain unchanged. Stage71 records actual
locked-mode solution restore, format verify and diff check, each exit0.

Final69/70 separately build the real NativeCaptureProbe D/R from that same
snapshot, actual exit0 with0 warnings/errors. New native evidence is under
`/tmp/flowspan-source-producer-native-20261005/`, not inherited from3aa099b.
Strict external observer/runner/watchdog executes each new `--run` once:
Debug/Release both PASS_SELECTED_TASK_OWNED_NATIVE_CHECKS_NOT_V1, no Skip, all
actual observer/runner/watchdog/native exits0. Both884-byte stdout files have
SHA-256 `98ac3aee6f431a0d8e51a5c7a8821d43d0acbbf4f6702af3ece546bbb047eb33`.
Root actually runs the new read-only `replay.py` for both records, exit0 and
matching validation. Seven changed code/test files match current/frozen/both
build trees before and after, alongside complete source/build-runtime bindings.

This verifies healthy capture of the task-owned64-point window on this macOS
host only. Global enumeration metadata, preflight/TCC TOCTOU, same-process
window-ID ABA, secure-input/protection, whole native fault containment, content/
Block/callback obligations, physical devices, all-OS native execution, production
UI and signing/release remain open. Delegate=0 and Protection Unknown remain.
Tasks1–4 close locally; task5 requires new exact-SHA CI/CodeQL and independently
downloaded complete inventories. The complete MSP slice, MSC9, v1 and Goal do
not close yet.
