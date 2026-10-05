# SourceEntry/Catalog ownership prerequisite

Status: repaired final local implementation, complete managed regressions,
selected healthy native D/R and root saved-data replays pass. Fresh exact-commit
hosted verification is pending. SCE6, nonzero delegate, production sharing and
full v1 remain open.

Exact base is `c5c5c520b99cca5d002ea1bb25642a041581bffe`; branch
`codex/v1-foundation`. Its successful 2836-case hosted checkpoint is an identity
baseline, not a result for the new source. Requirements/design/tasks are in
`specs/v1/native-remote-window/macos-source-entry-ownership/`; see
[ADR 0031](../adr/0031-bounded-macos-source-entry-ownership.md).

## Boundary

Tests execute the real SourceCatalog, SourceEntry, NativeBinding and NativeSource.
Only external native effects and the producer's returned list are controlled.
The internal native-source factory is thin; no second source state machine,
native dependency or sharing availability is introduced.

Late final-binding failure closes new Catalog admission and preserves the
original nested fatal or stable bounded ordinary failure. Permission/preflight
checks are not enumeration admission: a final closed-state check follows
reservation. Already admitted batches remain owned through settlement, while
publication and new binding acquisition recheck closure.

Independent process-static budgets are 8 Catalog lifetimes, 1024 source
obligations and 8 batch records, reserved before enumeration. The real producer
batch is bounded to 128. Existing/retiring/failed and candidate records all count;
a held binding keeps its charge after Catalog disposal. Unknown cleanup does
not authorize retry, eviction, replacement of another failed batch or return.
Source/batch counts are not a byte bound on arbitrary injected object graphs.
Fault contracts use fresh pools; production quarantine is not reset.

The first frozen candidate (stages 19–24) passes focused D/R 13/13, MacOS project
D/R 278/278, formatting and ten ordinary CLI/TRX invocations (130 Passed).
It is **superseded**, not final: the original Standards review found two GC
helpers using production Shared instead of fresh pools, and Spec found that
cancellation/overflow drain can skip a newly known source after a fallible
deduplication reread. Reports are preserved as `final-standards-review.md` and
`final-spec-review.md` in the worker directory. Repairs and new exact-source
verification are completed below. The first independent root run also records a
nonzero unchanged-input check; it is not a passing final gate.

The repair candidate is now separately frozen in `frozen-source-v2.sha256`:
29/30 focused D/R each pass 14/14; 31/32 MacOS project D/R each pass 279/279;
28 format verification exits 0 without source changes. Actual 26→27
`CancelledBatchCleansKnownOwnerWithoutRereadingPriorIndex` RED→GREEN preserves
whole test-file bytes: known source B's release attempts were empty, then the
cached/deduplicated drain attempts them independently once. GC correction (25)
is direct GREEN, not a fabricated product RED. Tests intentionally preserve only
the fresh actual ownership pool as a strong reservation root; Catalog/source/
batch observations remain weak and effect ledgers have no bypass reference.
This proves the pool's managed root graph, not native-reference survival or
dynamic use of production Shared. Final complete/native gates are recorded
below; fresh exact-commit hosted verification remains open.

## Actual staged failures and their corrections

Raw snapshots/argv/cwd/exits/TRX/source/runtime records are preserved under
`/tmp/flowspan-source-entry-20261005/`.

| Actual RED | GREEN | Observed failure |
| --- | --- | --- |
| 01 | 02 | last retired binding's unconfirmed native cleanup still allowed Refresh/enumeration |
| 03 | 04 | complete failed Catalog/source graph was collectible after caller roots left |
| 07 | 08 | a late binding fault during preflight still admitted another enumeration |
| 15 | 16 | failed batch index was read again instead of retaining unknown work and draining known owners |
| 26 | 27 | cancellation drain reread a fallible prior index and skipped independent known source B |

Each RED has an actual failed assertion and nonzero test exit, not a predicted
outcome or missing-type/compiler failure. 01/02, 07/08, 15/16 and 26/27 keep the whole
test-file bytes equal. 03/04 change a separate already-passing fixture to inject
a fresh pool; the failed GC case and its transitive helpers/effects are unchanged.
Do not claim whole-file equality for that pair or manufacture a replacement RED.

Nested-fatal/independent cleanup (05), held-binding/final-confirmed return (06),
shared Catalog capacity (09), unknown batch graph (10), source budget (11) and
batch budget (12) are direct-GREEN additions, not newly reproduced product
failures. The final 14-case inventory includes the registration-fault, gate and
concurrency additions; passing additions are not additional reproduced REDs.

Forced GC with caller/API/list/binding/task roots discarded proves managed graph
reachability only. Effect-ledger counters do not instrument actual Objective-C
references or establish native-fault containment.

## Final repaired local verification

The seven v2 frozen files have these SHA-256 bindings:

| File | SHA-256 |
| --- | --- |
| SourceCatalog | `636551c4f01620d15b4823dfbb08bea4ce901becef8980137d6d9f2827bc0b85` |
| SourceOwnershipPool | `341ac862e0da250db9fa9022cda168e92740940cb6c8ac7ff54da388e74e8eae` |
| ScreenCaptureKit API | `ce1394902276028acb793c529fbfda73e2f0c0cf199fb2774d0e6fc44e51012b` |
| SourceCatalogOwnershipTests | `d7be4e075e9434070020047f9aa3ad9cd3458b4b51f4e355d67e6f259c28dffc` |
| SourceCatalogTests | `1de6379b1978a7fc5c28f5b5b8507fed4d5e7abdb5e27bfaa0df9a599a67d9c3` |
| CaptureBoundaryTests | `f97ac4870f7820866b5e2ec1a458699695c472a6e7351cc412c799eb8931df1d` |
| CaptureOwnershipTests | `da2eda43ee4c70b7c5345bf1c58f51803c60bfb7000901b632b9a2ebb69b00fb` |

Final worker stages 29/30 focused each pass 14/14, 31/32 complete MacOS project
each pass 279/279, with all non-success counters zero and actual exits 0. Stage
33's ten ordinary separately recorded dotnet/vstest CLI invocations each pass
14 cases (140 total) against stage29 Debug compiled bytes, with unique TRX run
identities and unchanged source/runtime manifests. This is not ten fresh builds,
independent PID/process-group proof, a complete environment record or a
single-worker/untuned-environment guarantee.

Independent staged-data audit verifies five actual RED→GREEN pairs, final
source/runtime/command/exit/TRX records and all 265 exact-base hosted MacOS
identities plus 14 additions. Its 735-file complete saved snapshot inventory
binds base Git objects and seven overlays, not independently instrumented
compiler/dependency/environment inclusion. Root actually executes default
replay with exit 0, `FINAL_SAVED_EVIDENCE_AUDIT_PASSED_NOT_V1`, no violations
and the matching saved report hash:

```sh
python3 /tmp/flowspan-source-entry-evidence-audit-20261005/audit.py
```

Script/report SHA-256:
`1f4212783fc7bcca5b89c16f0b12e39af633dd6cf6d94495b529767f9c3e3cf3` /
`91a60341672f3303d9db2a8b28e289bd9f46521bc632682d8c49db6299b6b0e8`.
Historical/pending/v1 reports and all original raw evidence are preserved.

### Independent complete solution gates

Final `/tmp/flowspan-source-entry-root-20261005/run-02/` builds an exact c5c5c52
archive plus the seven v2 frozen files. All 30 new commands and runner actually
exit 0: locked restore, format, warnings-as-errors D/R builds/tests, TEST MODE,
simulator, solution vulnerability query, standalone build/format/defaults,
Foundation modes, strict raw/CLI/watchdog fixtures and unchanged-input checks.
Debug/Release each contain 12 TRX / 2850 Passed with every other counter zero;
MacOS is 279 each. Complete qualified identities match, old 2836 have no
removals and all 14 additions exactly match final worker TRX. Canonical inventory
SHA-256: `28d8d736eb0c3f5630f01d484a33fb7c5eb2630ecc46731c63637f94680d3298`.

All 557 monitored inputs and both configurations' 31 canonical MacOS test
runtime files match before/after; live-source audit has no mismatch. The separate
195-document snapshot is provenance, not documentation correctness or compiler
inclusion. The NuGet query covers 26 solution projects/transitive dependencies
without known reported findings, not every standalone tool or full security.
152 raw fixtures, four POSIX CLI fixtures and 12 watchdog contracts pass.
Intentional nonzero child outcomes and Darwin SIGKILL EPERM stay fail-closed;
harness exit 0/leader join is not arbitrary-descendant or native cleanup proof.

Root actually replays the default saved-data audit with exit 0,
`LOCAL_ROOT_GATES_PASSED_NOT_V1`, no violations and identical report hash:

```sh
python3 /tmp/flowspan-source-entry-root-20261005/audit-v2.py
```

Script/report/inventory-JSON SHA-256:
`dcff3d1b5f15e04ee94d6d186e9fe2fa33c5ef418154de6e8484efbfc9bbf7cf` /
`8e50af3129825ef2c7fa92dc315ae33f667031908c775783c4108749aaca6249` /
`4420bc8292b351a152f45b8256bd9d8ac38be0ab55e75fee42c10cf8dac74c0a`.
The separate live audit also actually exits 0; its JSON SHA-256 is
`76303eaab736645a9ca2acee9a2c497001a0b213c2f712ccf1ae6a02844a3b02`.
run-01 remains exit 1/superseded: only the live OwnershipTests input changed
after review revoked that freeze; its isolated build/runtime did not drift.

### Review and selected native status

Single-layer Standards/Spec v2 repair reviews resolve their original findings
and report zero new findings; both bind all seven frozen hashes. Root rereads
the reports and matches SHA-256:
`final-standards-review-v2.md`
`e4d83e8710afef2bd8b5747237889f3017de0f9ceaad42be94fbe9ee99a7b333`;
`final-spec-review-v2.md`
`2e21bbafb93f735edf3cfc8f0cd5f914db1b94fa16da36b9ac67a0006d75378d`.
Static review is not test execution, native fault proof or security certification.

Selected actual task-owned healthy native D/R each execute from new run-02
builds with no Skip, all observer/runner/watchdog/native exits 0 and empty stderr.
Each stdout is 884 bytes: Debug records two raw markers/one boundary marker,
Release one raw marker/one boundary marker. Both pass the strict field contract;
their bytes differ and are not represented as identical or reused old execution.
Final native evidence is preserved under
`/tmp/flowspan-source-entry-native-20261005/`. Root reads the final SUMMARY and
binding, checks their hashes, then actually executes the following read-only
replay with exit 0 and matching recorded results for both configurations:

```sh
python3 /tmp/flowspan-source-entry-native-20261005/replay.py /tmp/flowspan-source-entry-native-20261005/debug-run01 /tmp/flowspan-source-entry-native-20261005/release-run01
```

Replay classification is `READ_ONLY_REPLAY_MATCH_NOT_NEW_NATIVE_EXECUTION`;
this is validation of recorded bytes, not two more native runs. An initial root
invocation used nonexistent `debug-run`/`release-run` paths and returned
FileNotFoundError/exit 1 without executing native code; the corrected invocation
above uses the recorded run directories and passes. SUMMARY/binding/replay
SHA-256 are respectively
`a3087178c633b31e80a664df656f710fa7a67be4784c22a7e5e45be552d671a3` /
`821d565248e21669bfa9f3c84e64ab84b698f0f640c4d2048bd3071befda9524` /
`ce0cf009daa86af8b25c06f699b34e51d7fd170194b231b34e88b3e5801198be`.
Debug/Release native stdout SHA-256 are
`4438d32b7bef951be5352625b7892a0bb09ad8946a61610568bf200226aa4270` /
`98ac3aee6f431a0d8e51a5c7a8821d43d0acbbf4f6702af3ece546bbb047eb33`.

All 557 root build inputs, the seven frozen/current/build-tree source triads,
both 16-file native runtime inventories and both 31-file test runtime inventories
remain byte-bound before/after. These inventories are not compiler-inclusion
proof. Host is macOS 27.0.1/arm64, SDK 10.0.301, runtime 10.0.9; no other native
OS/version/architecture is covered. Finite healthy proxy observations, zero late
deliveries and zero final owners do not establish native fault containment or
complete callback/cleanup proof. The 120-second process-group watchdog is not a
bound on arbitrary descendants. Delegate=0 and the 14.2 candidate floor remain
unchanged; protection, secure input, sensitive windows and physical Devices
remain unproven.

Fresh exact-new-commit all-OS CI/CodeQL and downloaded evidence are still required.
No c5c5c52 hosted result is inherited as this new implementation's outcome.

## Remaining scope

This pool cannot recover native ownership acquired and lost inside CreateSource
or EnumerateAsync before the producer returns a batch. Initial filter alloc/init,
window/content retain, dispatch/Block/content/list handoff, global native failure
admission, nonzero delegate composition, monotonic terminal Start, managed
retirement and complete Capture permit return remain separate work.

MSC9 aggregate/native task 6, production sharing, protection/input/Emergency Stop,
physical LAN, minimum OS/architectures, accessibility, legal/signing/installation,
release acceptance and the active long-term Goal remain open. No GitHub message
was published by this in-progress checkpoint.
