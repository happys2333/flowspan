# Original-caller cancellation / EOF repair — local evidence

- Date: 2026-10-04
- Base: `c5332462880f079a3767e7c6cf4c3d4990c457d3`
- Verification tree: `/Users/happys/.codex/worktrees/viewer-exact-verification/flowspan`
- Host: macOS 27.0.1 (26A434) arm64; .NET SDK 10.0.301 / runtime 10.0.9
- Status: implementation and local managed verification complete; the new
  exact-source hosted checkpoint remains pending.
- Portable tool-format patch: `/tmp/flowspan-cancel-eof-diagnosis/portable.applypatch`

This is a hash-identified modified tree above the stated base, not an exact-SHA
hosted success claim. No commit, push, GitHub message, release, or source-tree
switch was performed by this delegated task. The four source files are frozen.

## Trigger and established cause

Hosted CI `37204976024`, Linux, failed
`DesktopActivityRuntimeTests.AuthenticatedRuntimesExchangeNoteAndExposeOnlyEligibleLiveTarget`:
the source run expected cancellation after the common caller requested stop but
returned EOF. The stack passes through `SecureControlChannel.ReceiveAsync:242`
and `AuthenticatedControlSessionDispatcher.RunAsync:97` in the base tree. The
Linux log is `/tmp/flowspan-native-candidate-hosted-37204976024/linux-test.log`.

The original caller token is set canceled before `CancellationTokenSource.Cancel`
runs its callbacks. Its callbacks execute sequentially, newest first. The target
handler may receive cancellation and close its secure stream while the source
handler's linked cancellation callback is still pending. Source receives EOF
with the original caller canceled but both downstream linked tokens uncanceled.
The old dispatcher checked only its innermost linked token.

Ranked alternatives were checked: both real runs remain pending immediately
before caller cancellation after a successful semantic Handoff, ruling out a
premature source/target run failure. Prior contract evidence at
`docs/evidence/2026-07-16-durable-atomic-swap-endpoints.md`, repair
`06c659f5e79d5ffee3174491ba66cd7090bb1b4a`, already specifies that stop-related
IO is projected as cancellation while a non-canceled EOF remains EOF. The
expectation was not invented to conceal the hosted failure.

## Actual RED evidence

1. The original focused Desktop test passes locally, 1/1 (`baseline/`).
2. A test-only callback registered between source and target RunAsync holds
   source cancellation propagation after target cancellation. The original
   real-TCP Handoff still succeeds. Source now deterministically returns the
   same EOF and the expected-OCE assertion fails in two fresh processes, 65 ms
   and 59 ms (`controlled-red/`, `controlled-red-repeat1/`). No sleep, guessed
   timing, reflection, or early peer disposal is used; the gate is always
   released in `finally`.
3. New dispatcher contracts with the original caller parameter wired but not
   used for classification produce 2 failed / 4 passed / 6 total
   (`transport-baseline-red/`). The failures are exact-caller cancellation and
   cancellation plus an independently failing owned cleanup.
4. A deliberately rejected after-cleanup classification mutation makes
   `CallerCancellationDuringCleanupCannotRelabelAlreadyRecordedEof` fail:
   expected original EOF, actual OCE (`late-classification-mutation-red/`). The
   mutation was fully removed. This proves the negative test rejects late
   reclassification; no post-cleanup reclassification remains in the repair.

The first attempted new-test build stopped at CA2201 for the deliberate OOM
constructor. A narrowly scoped existing-style fault-injection pragma repaired
that build issue; the compiler failure is not counted as behavior RED.

## Independently observable token levels

`TokenHierarchyProbe/` uses the existing dispatcher receive test seam and the
existing `Flowspan.Transport.Tests` friend identity. It copies and references
the pre-repair Debug assemblies, rather than adding a production API.

Flag order: original caller / handler linked / dispatcher read linked.

| Scenario | At receive exception | At parent gate | At completion | Observed result |
| --- | --- | --- | --- | --- |
| Caller stop first, propagation held | true / false / false | true / false / false | true / true / false | original EOF instance |
| EOF first, cleanup held, caller cancels later | false / false / false | — | true / true / true | original EOF instance |

The first row also shows that checking only the dispatcher's incoming handler
token would not fix this case. The second row explains why checking the caller
only after dispatcher cleanup would incorrectly relabel an earlier real EOF.

Probe source SHA-256:

- Program.cs: `d2fa670d275b3d53711770f36f9d0b5b114f44a3806a0d37dd309f98be0e143d`
- project: `fa1edfc99163eca176a6d96ec256616fc35c8f269a83d4ea9aced6b60befd286`
- copied pre-repair Transport DLL:
  `1671b67f7717dda7af6a5559a5a3e8fbd9bcae6cdd21e994d661c632eec3b251`

## Narrow implementation

The handler passes its original caller token to one optional **internal**
dispatcher argument used only to classify IO at the existing receive catch,
before cleanup. Requested original-caller cancellation takes precedence for
the reissued OCE token; the original IO is retained as its inner exception. The
existing linked token remains the actual read, dispatch, and authority token.
Existing caller/lifetime linkage, owner drain, cleanup order, and failure
aggregation are unchanged. There is no public API or dependency addition.

The six new contracts cover:

- canceled original caller before either linked token propagates: exact OCE
  token and exact original EOF inner exception;
- explicit cancelable but unrequested caller: exact original EOF;
- EOF already recorded before caller cancellation during gated cleanup: exact
  original EOF;
- canceled caller with receive Aggregate(IO, other): exact original aggregate;
- canceled caller with receive OOM: exact original OOM;
- stop-related receive IO plus owned-cleanup failure: both projected OCE and
  exact cleanup failure remain in the aggregate.

The existing Desktop Handoff regression now exercises this real-TCP callback
ordering and checks the exact source caller token and original EOF inner type.

## Frozen file hashes

| Repository path | SHA-256 |
| --- | --- |
| `src/Flowspan.Transport/AuthenticatedControlSessionDispatcher.cs` | `a19c993d97e49ff201ab6ab91f46a43330fba42412861151261352cc66038512` |
| `src/Flowspan.Transport/ActivityControlSession.cs` | `fd71a5294ec8f8f3404a6867a7ade57f6d43f3efad55e2fecf0775d4f0d4aed9` |
| `tests/Flowspan.Desktop.Tests/DesktopActivityRuntimeTests.cs` | `58816b34cda0bd49ac60423c5d564886a6f5d89826ea8e8da92a6ff18dcd1865` |
| `tests/Flowspan.Transport.Tests/AuthenticatedControlSessionDispatcherCancellationTests.cs` | `fd13e17d4a26ba2e97277472bf1faddadca21682107b33715572fe782d966cde` |

Portable patch SHA-256:
`6dcd734a13c032becd45e1f972ee31dcbcb83990eec69e370e8f0725f4b3273d`.
Root reported applying the patch to the main workspace and independently
matching each of the four source hashes.

## GREEN verification

All evidence paths below are inside `/tmp/flowspan-cancel-eof-diagnosis/`.

| Scope | Debug | Release | Final-tree artifact directories |
| --- | --- | --- | --- |
| Desktop focused real-TCP regression | 1/1 | 1/1 | `desktop-green-debug/`, `desktop-green-release/` |
| Dispatcher six focused contracts | 6/6 | 6/6 | `transport-green-debug/`, `transport-green-release/`; final content also verified by full suites and stress |
| Full Desktop project | 757/757 | 757/757 | `final-desktop-full-debug/`, `final-desktop-full-release/` |
| Full Transport project | 764/764 | 764/764 | `final-transport-full-debug/`, `final-transport-full-release/` |
| Fresh-process Desktop regression | 20/20 processes | 20/20 processes | `stress-Desktop-{Debug,Release}-{1..20}/` |
| Fresh-process dispatcher contracts | 20/20 processes, 6 cases each | 20/20 processes, 6 cases each | `stress-Transport-{Debug,Release}-{1..20}/` |

The initially focused six-case runs precede a small negative-test strengthening
that explicitly passes an unrequested cancelable token instead of default None.
The final full suites and all 80 stress processes use the frozen strengthened
test hash. No production or Desktop file changed between focused and final QA.

The four final full-suite TRX counters were parsed independently: total,
executed, and passed agree, all other outcome counters are zero. All 80 stress
TRX were independently parsed: 40 have total/executed/passed 1/1/1; 40 have
6/6/6; all non-success outcome counters are zero. Both the parent delegate and
independent reviewer confirmed these artifacts.

Representative commands:

```sh
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter FullyQualifiedName~AuthenticatedRuntimesExchangeNoteAndExposeOnlyEligibleLiveTarget --logger trx --results-directory /tmp/flowspan-cancel-eof-diagnosis/desktop-green-debug
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Release --no-restore --logger trx --results-directory /tmp/flowspan-cancel-eof-diagnosis/final-transport-full-release
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --logger trx --results-directory /tmp/flowspan-cancel-eof-diagnosis/final-desktop-full-release
dotnet format Flowspan.slnx --no-restore --verify-no-changes --include src/Flowspan.Transport/AuthenticatedControlSessionDispatcher.cs src/Flowspan.Transport/ActivityControlSession.cs tests/Flowspan.Desktop.Tests/DesktopActivityRuntimeTests.cs tests/Flowspan.Transport.Tests/AuthenticatedControlSessionDispatcherCancellationTests.cs
git diff --check
```

The stress loop starts independent `dotnet test --no-build --no-restore`
processes, grouped by project/configuration, 20 each. Formatting verification
covers the four changed files and passes on the frozen tree; `git diff --check`
passes. Test-triggered project builds pass the repository analyzers. This task
does not claim a full-solution build or full-solution test pass for this new tree.

## Independent review and limits

Read-only independent review reports **APPROVE**, scoped to this cancellation/EOF
repair, with 0 P0 / 0 P1 / 0 P2 across Standards and Spec. The reviewer independently
checks source hashes, final full D/R TRX, all 80 stress TRX, and diff whitespace.
No source mutation, build, or publishing was done by the reviewer.

These are managed local real-loopback and deterministic contract results on
macOS, not a new Windows/macOS/Linux hosted success, native capture, physical
cross-device/LAN, signed package, or full v1 acceptance result. The original
Linux run remains failed; root must publish a new implementation commit and
verify its exact-SHA hosted results without conflating them with this base.

## Root integration verification

The root integrated the four frozen files through `apply_patch` and independently
compared every source hash against the isolated verification tree. Locked
restore, full-solution format verification and warning-as-error Debug/Release
builds passed, with zero build warnings/errors. Each complete solution run has
12 TRX files, 2692 distinct test names and 2692/2692 passed; all other outcome
counters are zero. Transport is 764/764 and Desktop is 757/757 per configuration.
The six new contracts account for the increase from the base's 2686 cases.

Explicit TEST MODE composition and the protocol-1.7 deterministic simulator
passed. A parsed direct/transitive dependency query covers all 26 solution
projects with no reported vulnerabilities or error entries. These checks use
the hash-identified integrated working tree, not a claim of clean exact-SHA
Windows/Linux execution. Root logs/TRX/query JSON are retained at
`/tmp/flowspan-cancel-wgc-root-20261004/`.

TDD and the diagnose workflow drove the deterministic real-TCP RED, the minimal
receive-time repair, and the negative test rejecting after-cleanup relabeling.
The failed `c533246` hosted run remains historical failure and is not overwritten.

The integrated implementation was then committed as
`470d0f354f420b39bdc1ad5736bcb66acb01792d`. From that clean commit, the root
reran full Debug/Release format/build/test verification: zero build warnings or
errors, 12 TRX per configuration, 2692 distinct cases and 2692/2692 passed, all
non-success counters zero. Raw exact-commit records are retained at
`/tmp/flowspan-checkpoint-470d0f3/`. Main-branch CI `37208080273` now succeeds;
downloaded Windows/macOS/Linux inventories each contain the same 2692 cases,
all Passed, including the six new contracts and controlled real-TCP regression.
The old failed run is not reclassified. Full hosted security/package provenance
is in the [hosted checkpoint](2026-10-04-cancellation-wgc-hosted-checkpoint.md).
The three unsigned package jobs passed, but complete package download/hash and
internal inspection remain pending at handoff; those are not locally passed.
