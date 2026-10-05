# Enumeration hosted gates and regression repair — 2026-10-05

Status: both local repair checkpoints complete; fresh exact-SHA hosted acceptance
pending. Goal active.

CI `37281400039` for `65589d2418bdc32f2cb387c98cc3eeb5d3670b60`
failed: Windows two-peer inbound authentication produced client EOF; Linux
participant Stop timed out while awaiting preparation at original line94.
macOS passed and CodeQL `37281400042` succeeded. Neither partial result closes
MEP8. The failure's original line94 is **not** the cancellation-observer wait.
See [repair requirements](../../specs/v1/hosted-regression-reliability/requirements.md).

The failed candidate's complete available9 artifact ZIPs, run/job logs, source
archive/tree and CodeQL API/SARIF are preserved separately under
`/tmp/flowspan-enumeration-composition-hosted-20261005/old65589d2/`.
Downloaded Windows/Linux each have12 TRX /2947 Passed +1 Failed; macOS has
12 TRX /2948 Passed. Skipped package jobs have no runner log; the attempted404
receipt is retained, not called a successful download. Root actually replayed
the failure-integrity auditor, exit0, and its report is byte-identical to the
independent report (SHA256
`043f093636ae3dbae97e54eb549f0af6a8d009e2e20da5a5bb1639682cfde8ad`).
Its classification is `PRESERVED_FAILED_CANDIDATE_NOT_FINAL_SUCCESS`, not a green
CI result.

## New Block gate local validation

Three-OS CI now includes standalone locked restore, format, warnings-as-errors
build and a byte-exact default no-native gate. Windows expected output is CRLF;
Linux/macOS is LF. No raw-output normalization is used. macOS executes primitive
and enumeration modes separately under the existing45+2 POSIX watchdog; strict
records, all exits and terminal watchdog facts must match, with no Skip.
Default/native artifacts are uploaded even on failure. CodeQL builds BlockProbe.

Root actually ran locked restore/format/Release build (0 warnings/errors), the
workflow validator (2 YAML,50 shell blocks,14 default fixtures,144 native gate
fixtures), and both embedded macOS native steps: all actual exits0, empty native
stderr. Raw local evidence: `/tmp/flowspan-block-gate-root-20261005-run01/`.
This is local uncommitted-workflow validation, not exact-new-SHA hosted evidence.
Both static review axes finish with0 unresolved concrete gate findings.

Validator SHA256:
`0023b927dc044668ec5a913775e68226d92cc539bcb159c84a30a5be1140ace8`.
CI workflow SHA256:
`a1a6bb17ba4bfc1bea5cf28c468043c1872fb21f260a7f2147bed332c6ecd0b9`.
CodeQL workflow SHA256:
`3b037fefa960f91137caa4d59528f895bad8ffc15ca66c4addce62834c06553d`.

The enumeration Block mode still uses controlled external effects and explicitly
reports `sck_executed=false`, `capture_proved=false`, `v1_acceptance=false`.
Process watchdog success is not native cleanup proof.

## Actual repair and final local gates

The participant failure is a test-only dependency cycle: when preparation owns
late renderer cleanup, preparation cannot return before the fixture releases
renderer disposal. An actual deterministic preparation-wins RED preceded the
repair. The original Fact remains; one new Fact forces the other legal cleanup
owner. Both retain cancellation/rejection, blocked Stop and exact connection
closure assertions, release all barriers first and join every started task.
Local D/R class/project pass26/763 each;40 fresh processes pass80 executions.
Evidence: `/tmp/flowspan-hosted-stop-diagnosis-20261005/SUMMARY.md`.
Root saved-data replay exits0 and matches the independent report byte-for-byte
(SHA256 `91364a24fb6768a6415e526b72db4c678453fca1f632368e6506d774409adbba`).

The inbound success fixture's2-second System deadline can expire under controlled
scheduling, reproducing server Timeout/client same-stack EOF. This is a local
mechanism, not unique proof of the original Windows event. Clock injection now
reaches actual inbound authentication; existing public callers keep System and
the10-second default/2-minute maximum. The original two-peer Fact keeps its
2-second configured budget with test time held. Two new real-socket Facts cover
exact expiry/recovery and caller-cancellation priority, timer release and EOF.
An actual compiled RED with the seam not yet wired precedes GREEN. Its source
copy is later reconstruction bound to execution-time hashes, not simultaneous
archiving. Final02 Transport D/R pass766 each; under deliberate scheduling
blockage System fails6/6, controlled clock and original Fact each pass6/6.
Superseded candidates and setup/format failures are disclosed in
`/tmp/flowspan-hosted-inbound-diagnosis-20261005/SUMMARY.md`.
Root identity replay and final source hash verification actually exit0.

Fresh frozen `repair01-debug`/`repair01-release` under
`/tmp/flowspan-hosted-repair-local-20261005/` each pass **12 TRX /2951 Passed**,
all non-success counters0. Every prior2948 qualified identity remains, with
exactly3 new Facts. D/R complete qualified inventories and775 selected source
inputs match. Each1540-file complete bin runtime binds before/after/actual.
Solution and standalone BlockProbe locked restore/format pass. Single-layer
saved/current audits and actual root replays each exit0 with0 violations;
corresponding reports are byte-identical. The current audit also matches the
complete live selected source and Git state **before these documentary updates**.
No full live-tree equality is claimed after updating this record/tasks.

Recorder SHA256:
`2253085b6728224d5816e174553a9f33be88c743ce22e432521a3fb3f52f9250`.
Auditor SHA256:
`a8ed579dd4f70ea32c376742c73691ec610efe8adead26fc47d60e1cc1a2ede5`.
Saved report SHA256:
`fb2a2102c1303828b6ee781cb2fe0cfe2c06dc826b1202223ed01481d23ac357`.
Current report SHA256:
`8c5ccc72d8161e30ca3dfdbf88f0240c3602280069b70804f37cfc174abb54ce`.

Read-only native-input continuity independently checks8 project references,
their complete selected folders and shared configuration:158 paths/bytes match
the earlier actual native campaign's frozen source, with0 added/removed/changed.
Root replay exits0 and matches the independent report (SHA256
`6e3683129f1deac6d123850bc0a90352156ec1f811f936cfeab6e107f62da572`).
This is selected-input continuity, not expanded compiler-input evaluation,
external SDK/cache binding, a fresh native run, or proof the whole repair tree
was tested by those native executions. Final static Standards/Spec reviews each
have0 unresolved concrete findings. Fresh exact-SHA hosted evidence remains open.

## Preserved subsequent hosted failure

The fresh exact `f73f5fc3da2b17ad798f3e46083a8632f5e8d686` attempt1 has CI
`37285404195` failure and CodeQL `37285404060` success. Linux/macOS each have
12 TRX/2951 Passed; Windows has12 TRX/2950 Passed+1 Failed in the original
`DesktopRemoteWindowCleanupConfirmationTests.ProviderFailureDoesNotWaitForAnInFlightTimeoutCommit`.
Its TimeoutException is at line38, with finally at62. Packaging was skipped;
Windows Block steps after the failing test were not executed. These are not
accepted all-OS hosted gates.

All12 available artifact ZIPs, both run-log ZIPs, executed job logs, exact-source
archive/Git-tree reconstruction, API/SARIF and download receipts are retained
under `/tmp/flowspan-enumeration-composition-hosted-20261005/`. The independent
failed-candidate integrity replay and root's actual read-only replay each exit0,
with byte-identical report SHA256
`1245589b6077821ad3f3381fc84c51dc1ffcb6d573b688daf7002c824e15d9a2`.
This proves preservation/integrity, not CI success. At this failed hosted
checkpoint, HRR5 added the fixture scheduling dependency; new repair02 source/
runtime/identity freezes and fresh exact-SHA hosted verification were required.

The controlled single-worker harness actually fails waiting for Start while its
callback is still queued; replacing only the two blocking workers with dedicated
synchronous workers passes the original pending-commit, provider/late-owner
identity and stable timeout assertions. Three repeated differential pairs agree.
This demonstrates a scheduling mechanism, not the unique original Windows
trigger or an unchanged-fixture-byte RED/GREEN. Production code is unchanged.

The original Fact now runs its controller, Start and callback on synchronous
`LongRunning`/`TaskScheduler.Default` workers, retaining all five-second budgets.
Root and both review axes caught an intermediate finally task-snapshot gap; the
final version releases barriers, joins Start, then reads and joins its latest
callback in nested finally. Earlier WhenAll candidate runs are superseded, not
final acceptance. Final focused D/R each pass1/1; five fresh processes per
configuration each pass1/1, with source/two-DLL before/after binding. Formatting
and both final static review axes pass, with0 unresolved findings. Final test
SHA256: `4952b0791ce6ebdb8f65c79cb0d6c94d6acda724f728d332bb1dab2d80eb04a3`.
Raw diagnosis evidence: `/tmp/flowspan-hosted-cleanup-confirmation-20261005/`.

Fresh `repair02-debug`/`repair02-release`, explicitly based on full `f73f5fc`,
each pass12 TRX/2951 Passed with every non-success counter0. All2951 prior
qualified identities remain, with0 added/removed. D/R/quality match all775
selected source files; each1540-file complete runtime binds before/after/actual.
Locked solution/BlockProbe restore and both format checks pass. Worker and actual
root saved/current replays each exit0 with0 violations and byte-identical reports.
The current replay establishes full live selected source/Git equality **before
these final documentary updates**; no such equality is claimed afterward.

Repair02 auditor SHA256:
`ed4656226b17a6cc1b4764c51c91aa3d8b0706343463e9ec35a70cd970d5a15c`.
Saved report SHA256:
`f305eb3238b10930bff3637cc0cac2ba7caa56d48aaece1bbf4a4d5f9b42623d`.
Current report SHA256:
`23bfa8c60471a4d69a55aff9590ce948f2fd73c232f210711334351d2cd68262`.
Diagnosis root replay also exits0 and matches its independent report SHA256
`4b3c6d7d164dc2be1f25914cd94df78bbaa92ba68ce9f03562555319b3e89ac4`.
New hosted verification remains open; this test-only repair is not fresh native,
cross-platform system, production sharing or v1 acceptance.
