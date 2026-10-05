# Real NativeSource lifecycle — exact hosted checkpoint

Status: the complete default offline audit actually exits 0 with
`HOSTED_CHECKPOINT_GATES_PASSED_NOT_V1` and no violations. This closes only MSC task 3b.2a; it is not v1
acceptance and does not enable production sharing.

## Exact execution

Commit `c5c5c520b99cca5d002ea1bb25642a041581bffe`, branch
`codex/v1-foundation`, push event, attempt 1, run number 249:

- CI https://github.com/happys2333/flowspan/actions/runs/37245409580 — success,
  all eight jobs success.
- CodeQL https://github.com/happys2333/flowspan/actions/runs/37245409628 —
  success.

No dispatch, retry, issue, PR, comment, review or discussion was published.
Collector source reads use `git show` at the exact commit; no later task 3b
working-tree source is used. The committed local checkpoint is
`docs/evidence/2026-10-05-macos-native-source-lifecycle.md`.


## NativeSource scope and remaining boundary

Only native retain, release and current-source-check effects are injectable;
the tests execute real NativeSource/NativeCaptureSource and the same production
Capture. A managed-only prepared token is stored on the fully rooted Capture
before first retain. Window/filter attempts and confirmations are independent;
zero/foreign returned addresses are not confirmed ownership, and unknown
acquisition does not authorize release of borrowed addresses.

Parent/token use pins preserve the ledger through admitted effects. Native
effects and use joins run outside source gates; closing rejects new admission.
Direct/active ExecutionContext-descendant self-join rejects, while stale ancestry
and unrelated owners do not falsely reject. Confirmed independent owners each
receive one cleanup attempt even when another fails. Unknown release is not
blindly retried; original nested fatal and the complete owned graph survive
uncertain cleanup and factory-mailbox replacement. Forced-GC checks establish
managed reachability, not actual native-reference survival.

Production remains delegate=0, macOS 14.2/Arm64 candidate policy, unknown
protection and unchanged sharing availability. Initial CreateSource
filter alloc/init/window retain, enumeration/content/list handoff, durable
catalog/source/batch roots and independent bounded budgets remain open.
Nonzero delegate/exact initializer/global-fault admission, terminal Start,
managed retirement and complete-cleanup permit return are separate work.
Physical IsDrained remains sample/Block readiness, not complete owner release
or delegate drain. Local behavioral RED and healthy task-owned native runs
are separately scoped evidence, not hosted native fault injection.

## Full portable regression

Windows, macOS and Linux each have 12 raw TRX files with 2836 total, executed
and Passed cases; all non-success counters are zero. Result, definition,
execution and entry identities agree; test/execution identities are unique.
The complete qualified inventories match across all three hosted OSes and
saved final root Debug/Release raw TRX. Canonical sorted `assembly:testName`
plus LF SHA-256 is
`9e556460b9c149d3a51453759b5d33d74f75422e492329aa965e42197baffb85`.

Each hosted OS's Release MacOS project has 265 Passed cases; saved local
Debug/Release agrees.
All 59 same-Capture cases, 29 association-coordinator cases, 33 router cases,
10 pairing-decision cases and all four ProtectionMutation rows pass with exact
identities preserved. The new Capture cases are the only 27 additions to the
saved historical baseline list; no old hosted gate result is imported as a
current-SHA result. All 555 root source/build/helper inputs match this exact
commit and their saved before/after manifests. Run-03 freeze binds all five
source hashes and every one of the 27 additions; local inventory JSON SHA-256
is ad773b29562259ea11bd63c08191147063e085685673cb8c05de3607a5099b07. The collector preserves those
555 exact input files plus two exact committed evidence documents.

## Native helpers and strict gates

The synthetic delegate, Foundation association and Foundation early modes each
execute once in this hosted macOS job. They explicitly report no SCStream,
Capture, AppKit, permissions or pixel reads. Early stdout is exact LF-only
1057 bytes (SHA-256
`c5343ee33cbf70c92c039c316b37e6642dd66bcbe067c737519d8bdd48e8d38c`),
stderr is empty, native/watchdog statuses are zero and the strict gate step
succeeds. Source/tag ownership counts and poisoned permits retain their
bounded-helper meaning, not actual Capture or delegate-drain proof.

Each OS has all 152 strict raw fixtures (one exact accepted, 151 rejected).
macOS/Linux additionally have four POSIX CLI fixtures and 12 watchdog
contracts. Windows POSIX skips remain explicit. `gate.command.exit.raw` is
the pre-validation watchdog status, not independent final CLI-exit proof;
rejected CLI outcomes rely on the saved successful harness assertions.
Leader join is not proof that all arbitrary descendants vanished. Darwin
zombie-only SIGKILL EPERM remains fail-closed nonzero 127.

Linux actual thread-loop ABI succeeds with no daemon, portal or capture.
Standalone production capture probes run their no-capture defaults in CI;
Skip is not actual capture success. The selected task-owned local macOS
Capture runs are separate evidence in the committed local document, not
hosted executions or nonzero-delegate composition.

## Security and packaging scope

CodeQL logs report 443/443 C# files scanned and three raw diagnostic messages.
The exact Git tree also has 443 tracked C# files. Its selected exact-run API
SARIF representation has 52 unique rule descriptors and zero results. It is
API-reconstructed SARIF, not an original Actions SARIF ZIP; that representation
does not preserve the raw diagnostic-message contents. Counts are not a proof
of full semantic coverage, safe native interop or absence of all vulnerabilities.

Gitleaks 8.24.3 actually uses `--log-opts=-1`: one exact checkout commit patch,
approximately 84314 bytes, 208 unique descriptors and zero results. Full-history
checkout is not a full-history scan; working tree/dependencies are not covered.

All three unsigned package jobs seal and verify twice, compare the two package
outputs, validate explicit TEST MODE and query 26 solution projects including
transitive dependencies with no reported known vulnerabilities. Three large
unsigned package ZIPs are not downloaded, locally hashed, expanded or internally
verified. This is hosted/API/log evidence, not signed installation or release.

## Preserved raw evidence and replay

`/tmp/flowspan-native-source-hosted-c5c5c52/` retains API metadata, eight CI logs,
24 local raw TRX, 557 exact source/document files, 11 small artifact ZIPs and
exact CodeQL API/log bytes. All 14 artifact API/upload size and digest facts
are bound to this run; the 11 downloaded ZIPs additionally have matching local
SHA-256/size and safe bounded member validation. Package archives have only
API/upload binding, with no local package-content claim.

Default replay uses saved bytes plus exact Git objects only:

```sh
python3 /tmp/flowspan-native-source-hosted-c5c5c52/audit.py
```

Frozen main audit script SHA-256:
`f0f166f42f07ec392a9c9f42ef9912c0cacb12725dff3a07d09d1c4bd58d3511`.
Root independently actually replays the same main audit with exit 0, no
violations and the identical report hash. Root also separately replays the
CodeQL sub-audit with exit 0 and its identical saved JSON. Main JSON is 1491954 bytes, SHA-256
`067d613473439056e485e73ecb9253051ce9a1ba9bb27f170991c8a5023c916b`.
Collector SHA-256:
`30dfbd8458c6532dc61cfecc8bf988d95c489fbdce970571d58ca55cf7b70a45`.
Full three-hosted-OS outcome inventory JSON SHA-256:
`47486c6ebbfa3afef85ac852f607aa8b000f4d91921f54884838ea2b451fcbf8`.

CodeQL sub-audit actually exits 0 with
`CODEQL_HOSTED_SCOPE_GATES_PASSED_NOT_V1`, no violations and stdout identical
to its saved JSON. Its script SHA-256 is
`0c73a89e1b4184b0b8eda1d11b863968540d5a005bc50b967f706513e742393d`;
JSON SHA-256 is
`d9ca1e1783625700096c6f3675f1d4ecd237d7737c241ff36c1c3cdbad57e1f9`.
Exact analysis ID is 1890175326, job ID 111562212533, CodeQL version 2.27.1;
tracked C# split is src 230 / tests 183 / tools 30. The complete API SARIF
representation is 231092 bytes and the raw job log is 186825 bytes.
The saved CodeQL hash/workflow/tree inventory also matches this audit's saved
source and exact Git objects; this is not per-file CodeQL database proof.

Independent saved-data/static integrity review has zero material findings; prior review conclusions are not imported.
The current review independently checks the full TRX identities, 555 input bindings, 443 C#
hashes, all 14 API/upload receipts and the 11 ZIPs' 1195 safe bounded CRC-checked
members. It does not rerun product tests or native capture. Report:
`/tmp/flowspan-native-source-hosted-c5c5c52/independent-review.md`, SHA-256
`043a5f27fdcf22a39345bc71dc8a1ef07f90ab0f8f11f257e4fe0bf3d00efb25`.
Original local failed diagnostic, timeout and skip evidence remains preserved
in its original directories; this audit does not rewrite it or establish a
new hosted RED-to-GREEN or successful single-worker/native-fault test. Local NativeSource behavioral RED-to-GREEN belongs only to the exact committed local evidence.

The remaining source acquisition/catalog/budget seams, nonzero-delegate composition, MSC6/MSC9 aggregate gates, task 6, production sharing, physical
devices, minimum OS/architectures, protected/secure input, independent Emergency
Stop, accessibility, signing, release acceptance, v1 and the Goal remain open.
