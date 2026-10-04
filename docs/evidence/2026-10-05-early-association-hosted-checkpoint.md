# Early-association checkpoint — exact hosted evidence

Status: the exact commit's hosted checkpoint gates pass. This is not actual
Capture composition, native drain/no-future-callback, signed distribution or v1
acceptance. The previous `9deed36` CI failure remains a separate failed record;
this new run does not rerun or rewrite it. MSC task 2b's parent, MSC9, parent
Task 6, v1 and the active Goal remain open.

## Exact identity and preserved inputs

- Commit: `2c6f8fd9fa35f3eece53af2b38ddbb0e94b5cc7b`.
- Ref: `refs/heads/codex/v1-foundation`; both workflows triggered by `push`,
  attempt 1, run number 246.
- [CI 37232162597](https://github.com/happys2333/flowspan/actions/runs/37232162597):
  completed/success; three OS test jobs, Linux library ABI, secret scan and
  three RID package jobs all completed/success.
- [CodeQL 37232162624](https://github.com/happys2333/flowspan/actions/runs/37232162624):
  completed/success.

`/tmp/flowspan-early-hosted-2c6f8fd/` preserves initial/final API snapshots,
final per-job logs, eleven small original artifact ZIPs and exact source/workflow/
helper bytes read with `git show` at this commit, not from the dirty worktree.
Every downloaded ZIP is bound to its API artifact run/commit/repository identity,
API size/digest and upload log ID/size/digest; safe names, duplicates, symlink
flags, file bounds and uncompressed content hashes are checked without extraction.
No dump artifacts were downloaded. Three large unsigned package ZIPs are API/
hosted-log inventories only; their archive hashes and internal package contents
were not recomputed locally for this SHA.

## Full test inventory, not just totals

Windows, Linux and macOS each have 12 TRX with 2777 Passed and zero non-success
counters. All result/definition/execution/entry identities and assembly-qualified
case names match. The complete three-OS inventory equals the frozen local
Debug/Release inventory, whose sorted `assembly:testName` plus final-LF canonical
SHA-256 is `ef0952258305c78086408f1dd0539a644785c7f87dc81e02bc3661f0b63a69c8`.
The preserved source JSON has separate raw SHA-256
`e0fb45449716d1168348245576a7ae38477bcfa13549bb28aec39051de413620`.

Each OS passes all 29 coordinator, 33 router and ten pairing cases with exact
identities preserved, plus all four `ProtectionMutationAfterReservedRoute...`
rows (`Unknown`/`SecureInput` × forced revocation `False`/`True`). This is net
two additional cases over 2775: two old natural rows were renamed to include
the new `False` parameter; two forced `True` rows were added. The source-bound
test fixture repairs do not change production implementations.

## Native and gate evidence

The macOS job ran one independent hosted process for each explicit native mode.
Legacy synthetic stdout remains 277 bytes; Phase 2a remains 710 bytes and
explicitly `early_publication_proved=false`. Neither is repurposed as new
early-mode proof.

The new `--run-early-associations` process has exact 1057-byte stdout, one final
LF, no CR/NUL, empty stderr and SHA-256
`c5343ee33cbf70c92c039c316b37e6642dd66bcbe067c737519d8bdd48e8d38c`.
Native command and watchdog exits are 0; the hosted gate step succeeds. The
saved watchdog report says
normal exit, leader joined, no signals/timeouts/interruptions/failure. The gate
has an independent 30-second process deadline and two-second grace in the
exact workflow, plus the Actions step's two-minute timeout.
Here the actual gate's final success is bound by the successful hosted step and
exact byte checks, not `gate.command.exit.raw` alone: that file saves the
watchdog status before post-run validation, so the correctly rejected CLI-skip
fixture also contains 0 there. Normal success joins the leader only; it does
not prove absence of arbitrary surviving descendants.

Actual Foundation proof records ten native callbacks; four tag owner/dealloc/
superclass-dealloc lifetimes; six source owners; source reference retains/releases
11/11 and tag references 7/7; association/protocol/nil reads 26/18/11; one pending
replay and one original-generation publication-race notification; zero replacement
or ambiguity notifications; three ambiguity-scoped nil reads and two quarantined
initializers. Same-source pending, publication-race and ambiguity-poison fields
are true. Global ownership admission stays open; charged/uncertain ownership and
contained failures are zero. The two poisoned initializer permits remain
process-owned: balanced reference records do not prove complete Capture cleanup.
No SCStream, AppKit, permissions, pixels, capture or input is executed.

Every OS runs the 152 strict raw-byte fixtures (one accepted exact valid case,
151 rejected hostile cases), with the complete 44-field mutation/missing/duplicate
schema preserved. Windows skips only the POSIX-specific CLI/watchdog exercises,
not these raw fixtures. macOS and Linux also pass four end-to-end CLI fixtures
and twelve process-watchdog contracts. The CLI timeout fixture preserves exact
Pass-looking stdout and native exit 0 while rejecting the timed-out invocation.
The macOS `timeout-leader-exits-zero` and evidence-write-failure contracts retain
fail-closed127/PermissionError for final KILL of a zombie-only group; they are
not recorded as successful KILL. Descendant contracts require observed
`kill(pid,0)` disappearance before recording success. Saved reports cannot
independently re-query historical remote PIDs. A process-group watchdog is not
native delegate drain or native cleanup proof.

## Security and package boundaries

CodeQL logged 439/439 tracked C# files (228 src, 181 tests, 30 tools). Analysis
`1889707513` is bound to the exact SHA/ref/category/version/job lifetime and
completed upload through cross-evidence; the analysis API itself has no run ID.
API-reconstructed SARIF has 52 unique descriptors (24 pathproblem, 20 problem,
eight metric) and zero results, with complete processing and empty API error/
warning. Logs contain three raw diagnostics and seven interpreted diagnostic
queries; the original raw diagnostic contents/severity are not available from
the converted SARIF. CodeQL's Actions artifacts API has zero artifacts, so no
original uploaded SARIF ZIP is claimed. One check annotation is an Ubuntu
runner-image migration notice, not a security finding. Aggregate count equality
is not a per-file TRAP audit or native security proof.

Gitleaks runtime 8.24.3 actually used `--log-opts=-1`: only this exact commit's
patch, one commit, approximately 120818 bytes, zero results and 208 descriptors.
No older two-commit range, first-parent or full-history scan is inferred.

Three RID package jobs each logged two seals, two successful verifications,
same-stage recursive comparison, packaged explicit TEST MODE composition and
26-project dependency queries without a reported finding. The artifacts are
unsigned test packages, not signed installation or release acceptance. These
package observations are hosted-log/API evidence only, not local verification
of the undownloaded archive bytes or their internal contents.

## Reproducible audit

```sh
python3 /tmp/flowspan-early-hosted-2c6f8fd/audit.py
```

The offline audit replays saved bytes and exact Git objects, including the
separate CodeQL saved-data audit; it makes no network requests, build/test/native
runs, repository changes or GitHub writes. It returns exit 0 and
`HOSTED_CHECKPOINT_GATES_PASSED_NOT_V1`, with no acceptance violations for this
bounded checkpoint.

Audit script SHA-256:
`c86a4d36d787ce851259e5617670e1b1d4c5b383c84aebc14ebf7fb7fd425c68`.
JSON SHA-256:
`036e361296b09330a6de60c382ec01f984f94ff3e4eda153998558ff512359d5`.
Independent saved-raw review has zero material evidence-integrity findings:
`/tmp/flowspan-early-hosted-2c6f8fd/independent-review.md`, SHA-256
`22f175f51e6a3ece19bbffe52f75f76d19c6e524ef5fc20bdc165869df372225`.
It separately validates all 36 TRX identities, exact fixture mutations,
eleven ZIP/API/upload bindings and archive layout; the two gate-status/leader-
join limitations above are explicit caveats, not suppressed findings. Root also
replayed the frozen auditor successfully with the same JSON digest.
CodeQL audit script/JSON SHA-256:
`73ce55af17d0aaf912eb5f9047a13b63aed9e055d1d427b2cb3001c457793fed` /
`430a283a06cfd3b19776d792176278b94fb475358808c893075609c8072a1f51`.

Actual Capture composition, source-loss, protected windows/secure input,
independent Emergency Stop, native drain, physical devices, minimum OS,
complete UX/accessibility, signed installation and final release acceptance
remain open. This record does not complete or pause the Goal.
