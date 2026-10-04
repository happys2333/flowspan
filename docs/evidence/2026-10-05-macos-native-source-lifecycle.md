# Real NativeSource lifecycle prerequisite

Status: final local implementation, independent complete regression, selected
healthy native regression and saved-data replay pass. Fresh exact-commit hosted
verification remains pending; MSC task 3b.2a remains in progress. This is not
nonzero-delegate composition, production sharing or Flowspan v1 acceptance.

Base: `a07d911624c57c3a33ab9708de96d1d7838674dc`; implementation branch
`codex/v1-foundation`. The prior exact-base hosted success is an identity
baseline, never a result for this new source.

## Implemented boundary and ownership rules

Only native retain, release and current-source-check effects are injectable.
Tests execute the real NativeSource/NativeCaptureSource and the same production
Capture, rather than a second fake source state machine. Production retains
`delegate=0`, the 14.2/Arm64 candidate policy and unchanged sharing availability.

A managed-only prepared token is stored on the fully rooted Capture before its
first retain. Window and filter acquisition attempted/confirmed facts are
independent; a zero or foreign returned address is not a confirmed retain.
Unknown acquisition never authorizes releasing a borrowed address. Confirmed
independent owners receive one cleanup attempt even when another owner fails.

Parent and token use pins keep borrowed addresses and the ownership ledger
valid through admitted effects. Retain/current-check/release and use joins run
outside source gates. Closing rejects new use before cleanup; direct and active
ExecutionContext-descendant self-join reject instead of waiting for themselves.
Stale ancestry and unrelated owners do not falsely reject.

Release attempts are separate from confirmations. An unknown release is never
blindly retried; known absence remains distinct from stored diagnosis. Original
nested fatal identity and the complete Capture/token/effects/callback graph
survive unconfirmed cleanup and factory-mailbox replacement. Forced-GC rows
prove managed reachability only, not actual native-reference survival.

## Actual vertical TDD and preserved exclusions

Worker evidence is preserved under `/tmp/flowspan-native-source-20261005/`.
The following are actual behavioral failures, not predicted failures:

| RED stage | Subsequent GREEN | Behavior |
| --- | --- | --- |
| 03 | 04 | original nested fatal survives partial retain and cleanup failure |
| 05 | 06 | new admission does not wait behind a held current-check effect |
| 08 | 09 | active descendant cannot join its own source use |
| 12 | later corrected retain fixture | new admission does not wait behind a held retain effect |
| 15/16/17 | 18 | failed filter cleanup cannot skip independent window cleanup |

01 and 11 are fixture errors, not product RED. 07 is xUnit1031 compilation
failure; 13/14 are CA1513 compilation failures. 15/16 each preserve both the
real base-release failure and a separate current-check-count fixture error.
The factory already performs one pre-acquisition current check; correcting
that count does not weaken the gate assertion. Stage 23 rebinds the exact
final-first-version fixture to an isolated older stage-12 API and reproduces
the retain-gate assertion. It does not manufacture a new failure.

Capture-first rooting/handoff was already fixed by task 3b.1; its preservation
checks are not new RED. Unknown retain, known absence, ordinary/fatal current
failures, stable repeated cleanup, managed graph retention and held-release
coverage include direct-GREEN additions. The held-release review repair adds
test coverage, not a claimed new production RED-to-GREEN.

Historical stages 01–21 preserve runner templates, source/runtime inventories,
raw output, TRX and exit records, but no separate execution-time command receipt.
Later command receipts do not retrospectively strengthen that provenance.
22/28 formatting summaries are not reconstructed execution-time exit evidence;
the final verification must supply independent actual formatting results.

## Final local evidence

Final five-file SHA-256 inputs:

| File | SHA-256 |
| --- | --- |
| ScreenCaptureKit API | `6f53508e107380f1f2a7981370a6fbe283a51f09987ee0213eb20dd0c0ac7e98` |
| CaptureOperations | `f88b07c795cd0915b15c066d28cfacd1a2c43d1c45fe81f2d16ebedd56e68e96` |
| SourceOperations | `ea456fc18d89c4e0e3583b936ba2c71bdf2f9c285ac815d595ebe13f76b88998` |
| Existing CaptureTests | `c5fafdc1db089e2c9374de235052e8332017b3c5d452910c6c05dd07e540d2d2` |
| NativeSourceTests | `31b6b2a624e56a8c45c5f1a779d3a0a09564bb4f886b200a51fcc29644f39ac7` |

Worker stages 41/42 each actually pass 59 focused cases; 43/44 each pass the
complete MacOS project at 265/265, with zero skipped and command exit 0. These
build from an isolated exact-base archive plus the five final snapshots under
`39-isolated-explicit-admission/checkout`, not the repository's bin/obj or the
older live fixture. The final test snapshot and live file compare byte-for-byte.
The 27 added cases preserve the historical 32 focused / 238 project identities.

Stage 45 runs ten separately recorded ordinary test CLI invocations against
stage 42's final Release artifacts; all 590 case executions pass. Saved input,
runtime, argv/cwd and distinct TRX run identities are bound before/after. Three
recorded processor/ThreadPool tuning variables are unset; this is not a complete
environment record, independent PID/process-group proof or single-worker test.
Final stage 46 records actual whitespace/diff verification and snapshot/live
comparison success. Earlier successful
versions, including root run-01/run-02, remain superseded, not final evidence.

Final stages 40–44 additionally bind 516 selected source/configuration entries
to exact-base Git blobs or the five frozen snapshots. This extension-filtered
inventory is not a complete compiler/dependency-source/environment manifest;
complete hidden bin/obj inventories do not mean every file was executed.

Root actually replays the saved-data worker audit with exit 0,
`SAVED_EVIDENCE_AUDIT_PASSED_NOT_V1` and no errors:

```sh
python3 /tmp/flowspan-native-source-20261005/audit.py
```

Script / report / case-inventory SHA-256:
`8077cebf41e76a62d02d4d038cbc8786f10a8bc709571b05664f7160fd1cbb31` /
`e58a0a248351a06a33c325335b884868c7523defaf3cb3bf7743a332c8f63546` /
`1c0bd0f0e3b28fe800b1edb99f09d842ef3e2dbcefa0431a2c9b055275ad9d44`.
Default replay does not read mutable future repository source/runtime. There
is no stage 47; stages 23/37 are historical fixture bindings for the existing
retain defect, not actual RED of the entire new final test file.

### Independent complete solution gate

`/tmp/flowspan-native-source-root-20261005/run-03/` independently builds an
exact-base Git archive plus the five frozen files. The runner and all 30 fresh
commands actually exit 0: locked restore, solution formatting, warnings-as-errors
Debug/Release build/test, explicit TEST MODE, simulator, vulnerability query,
standalone tool builds/formats/defaults, Foundation helpers, raw/CLI/watchdog
fixtures and input/diff checks.

Debug/Release each contain 12 TRX / 2836 total, executed and Passed cases, with
every non-success counter zero. Complete qualified inventories match; historical
2809 identities have no removals and the 27 additions match the final worker
TRX. MacOS project/focused counts are 265/59 per configuration. Canonical full
inventory SHA-256:
`9e556460b9c149d3a51453759b5d33d74f75422e492329aa965e42197baffb85`.

All 555 captured inputs match before/after: 550 exact-base blobs plus five frozen
snapshots. Archive bytes are checked against captured Git blob IDs. Both
configurations' 31 canonical MacOS runtime assets remain unchanged. Document
snapshots provide provenance only, not documentation-correctness proof. The
repository's original bin/obj, conflict siblings and old worktrees are untouched.

The vulnerability query covers 26 solution projects with transitive dependencies
and reports no known findings/errors; it is not a complete security clearance or
an individually scoped audit of every standalone tool. Fresh Foundation modes
prove their no-capture contracts. The 152 raw fixtures, four POSIX CLI fixtures
and 12 watchdog contracts pass. Intentional nonzero child outcomes and Darwin
SIGKILL EPERM remain fail-closed failures; leader join is not all-descendant or
native cleanup proof. Default probe Skip is not actual Capture success.

Root actually replays the final saved-data audit with exit 0,
`LOCAL_ROOT_GATES_PASSED_NOT_V1` and no violations:

```sh
python3 /tmp/flowspan-native-source-root-20261005/audit.py
```

Script SHA-256:
`69c285c0fcdf02c6a2bc7b5e5e31e3eb5605207bf01a7615ba0cdde4a9aae62c`.
Default report / inventory JSON SHA-256:
`878b9407602195b3a4699c2d4f5882972d08658e208ef05d58d00b112d0c47b1` /
`ad773b29562259ea11bd63c08191147063e085685673cb8c05de3607a5099b07`.
Default replay does not depend on future worktree Git/source/bin/obj state.

## Selected actual macOS regression and review

New task-owned healthy `--run` Debug/Release each execute once from the final
run-03 independent build tree. Actual observer/runner/watchdog/native exits are
all zero, with no Skip. Preserved evidence:
`/tmp/flowspan-native-source-native-20261005/{debug-run01,release-run01}/`.
Both fresh stdout files are byte-identical 884 bytes, SHA-256
`98ac3aee6f431a0d8e51a5c7a8821d43d0acbbf4f6702af3ece546bbb047eb33`,
with empty stderr. All lifecycle/security fields pass: 128x128/scale-two marker
frames, Start/Stop race and sample readiness, generation binding, own-process
exclusion, hidden-source invalidation and finite late-delivery observations.
Permission requests, title reads and pixel writes are reported zero; these are
reviewed-tool facts, not independent system-call instrumentation.

The 555 build inputs are checked with actual successful commands before/after;
all five frozen/current/build-tree files compare byte-for-byte, including the
test file outside the reused harness's fixed test scanner. The separate 277-entry
source and 16-entry canonical runtime inventories are unchanged. These are
byte bindings, not compiler-inclusion proof. Native summary/binding SHA-256:
`5f93e9d1fea890d2195a24854ff05f590b3d964f96e5b630ff2f531f4f4ee7bf` /
`f07c4c2a3e5433e4ec29931059576f08e0d103f0c889b00f9fab53eb9212bc1b`.

Root replays the strict validator through its read-only function and checks
observed exits, saved validation/gate equality and equal fresh raw outputs:

```sh
python3 /tmp/flowspan-native-source-native-20261005/root-replay.py
```

Actual exit is 0; script SHA-256:
`b95627a49a1f38addf1b5e7a5056ef7ce5013112ffbb2febdfa467371cb0baf1`.
The direct validator CLI initially refuses both existing validation artifacts
with FileExistsError; those nonzero invocations are preservation guards, not
product failures or successful replays. No saved native evidence is overwritten.

This healthy regression and portable injected effects do not establish
Objective-C/ScreenCaptureKit fault containment. Global enumeration metadata,
TCC preflight/enum TOCTOU, same-process window-ID ABA, Unknown protection,
sensitive windows and secure input remain unproven. Finite sample-delivery-proxy
observation and zero owner counts are not all-native-callback or complete-cleanup
proof; the watchdog bounds its task process group only.

Final static Standards/Spec review has zero remaining findings and binds all
five final SHA-256 inputs. Root rereads both reports and verifies their hashes:
`/tmp/flowspan-native-source-20261005/final-standards-review-v2.md` /
`final-spec-review-v2.md`, SHA-256
`cf78e0042c907069cc8da66a163a69aad4b01c8ed9b5046a12f3f61428889786` /
`e60ceab7a369197f110ac9294c7efdd9232628b80d4cc9ab498d29473b5093b6`.
The first review's P2 and superseded candidate reports remain preserved. This
static sign-off is not test execution, native proof or security certification.

## Remaining gates

Fresh exact-new-commit Windows/macOS/Linux CI, CodeQL and independent downloaded
inventory/artifact audits remain required. No prior a07d911 result closes them.

Initial CreateSource filter alloc/init/window retain, enumeration/content/list
handoff, durable catalog/source/batch roots and independent bounded budgets
remain open. Nonzero delegate/exact initializer/global-fault admission,
terminal Start, managed retirement, complete-cleanup permit return, real native
faults and aggregate MSC2b/MSC6/MSC9 acceptance remain separate work.

Task 4/6, production sharing, protection/input/Emergency Stop, physical LAN,
minimum OS/architectures, accessibility, legal/signing/installation/release,
full v1 acceptance and the active long-term Goal remain open. No GitHub issue,
PR, comment, review or discussion was published by this checkpoint.
