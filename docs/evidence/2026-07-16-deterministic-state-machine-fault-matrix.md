# Deterministic State-Machine and Fault-Matrix Evidence — 2026-07-16

## Evidence boundary

This slice consolidates deterministic generated transitions and fault injection
for the implemented Activity operation journal, Move, Atomic Swap, Mirror
driver authority, and authenticated control sessions. It proves the current
core invariants under bounded combinations of drop-before-delivery,
acknowledgement loss, duplicate delivery, delay/expiry, disconnect, and journal
failure.

It does not claim that every future v1 state machine already exists or that the
release-wide property criterion is complete. Activity Groups, Scenes, Remote
Window, packaged native Adapters, physical two-device networking, process kill,
power loss, sleep/wake, interface churn, and real disk-corruption behavior remain
separate work.

Branch: `codex/v1-foundation`

The exact implementation commit and hosted run identifiers are pending until
this local candidate is committed and pushed. Task 3.4 remains open until the
same exact commit passes Windows, macOS, and Ubuntu CI, Secret Scan, CodeQL, and
downloaded TRX verification.

## Local environment

```text
Host: macOS 26.5.2 (build 25F84), Apple Silicon, Asia/Shanghai
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
RID: osx-arm64
```

## Commands

```sh
dotnet restore Flowspan.slnx --locked-mode --nologo
dotnet format Flowspan.slnx --no-restore
dotnet format Flowspan.slnx --verify-no-changes --no-restore \
  --verbosity minimal
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable \
  --include-transitive --no-restore
git diff --check
```

Four model/fault groups also ran in 20 fresh testhost processes per command:

```sh
dotnet test tests/Flowspan.Domain.Tests/Flowspan.Domain.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~MirrorSessionTests'
dotnet test tests/Flowspan.Integration.Tests/Flowspan.Integration.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~HandoffTests|FullyQualifiedName~MoveTests|FullyQualifiedName~InMemoryOperationJournalTests'
dotnet test tests/Flowspan.Integration.Tests/Flowspan.Integration.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~SwapCoordinatorTests'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~ActivityControlSessionTests'
```

## Local results

- locked restore, formatting, format verification, and patch-whitespace checks
  passed;
- the Release build passed with 0 warnings and 0 errors;
- 713 tests passed, 0 failed, and 0 skipped:
  - Desktop: 159;
  - Transport: 177;
  - Integration: 141;
  - Security: 90;
  - Domain: 39;
  - Protocol: 25;
  - shared platform contracts: 16;
  - Windows platform contracts: 18;
  - macOS platform contracts: 16;
  - Linux platform contracts: 18;
  - mDNS transport contracts: 14;
- Mirror, Activity/journal, Swap coordinator, and control-session groups each
  passed 20/20 fresh processes, with 11, 26, 27, and 40 tests respectively in
  every process;
- explicit Desktop composition printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`;
- the deterministic simulator printed protocol `1.1`,
  `Source preserved: True`, `Target resumed: True`, and
  `Atomic swap committed: True`;
- the NuGet direct/transitive vulnerability query reported no known vulnerable
  package in all 24 projects.

## Generated model evidence

### Operation identity and terminality

`SeededOperationSequencesPreserveTerminalIdempotency` executes 32 fixed seeds
with 128 events each across eight Operation IDs, three request digests, and
Committed, CommittedWithWarning, Rejected, Failed, and Recovering outcomes.
After every event it checks that:

- a same-digest terminal retry returns the exact recorded receipt without a
  second handler call;
- another digest conflicts without executing the handler;
- terminal results never change;
- Failed and Recovering entries remain retryable instead of becoming false
  terminal history, while the first request digest remains permanently bound
  to the Operation ID and a different digest still conflicts;
- a handler exception has the same permanent digest binding, and the bounded
  in-memory journal rejects an unknown Operation at configured capacity before
  handler work while still allowing a known same-digest retry; production uses
  the 4,096 default and the boundary test reduces capacity to one.

Each failure reports the seed, event index, Operation, digest, and generated
status so the sequence is reproducible without wall-clock timing.

### Move safety under composed delivery faults

`GeneratedDeliverySequencesPreserveMoveSafetyAndIdempotency` enumerates all
`4^3 = 64` three-event sequences from normal delivery, drop before delivery,
drop acknowledgement, and duplicate delivery, then appends one normal retry.
After every attempt it proves:

- a closed source always has an active acknowledged target;
- Failed or Recovering leaves the source active;
- the target Adapter resumes at most once and the target catalog contains at
  most one instance;
- the bounded normal retry converges to one committed target and one source
  close.

The assertion message includes the complete generated fault trace and attempt.

### Atomic Swap and lease authority

- the existing `ExhaustiveGeneratedFaultMatrixPreservesAtomicTerminalInvariant`
  enumerates `4^4 = 256` Prepare/Decision fault combinations for both
  participants, recovers twice, and proves either both original placements or
  both committed replacements with an immutable decision digest;
- delay past the snapshot/reservation deadline durably aborts without mixed
  placement, while reordered Abort, duplicate Prepare/Decision, overlap, and
  Operation reuse stay idempotent or conflict;
- `SeededTransitionSequencesNeverReviveOldDriverAuthority` executes 32 fixed
  seeds with 128 events each across participant role changes, removal, driver
  transfer, lease expiry/refresh, emergency stop, rejected stopped-state work,
  and owner resume. After every event, at most one current epoch can inject;
  every retired epoch stays invalid; ViewOnly never injects; Emergency Stop
  exposes no viewer or driver.

## Fault-boundary evidence

| Boundary | Deterministic evidence |
| --- | --- |
| Drop before delivery | Move generated matrix; Swap Prepare/Decision matrices and recovery |
| Lost acknowledgement | Move generated matrix; Swap recovery; Activity/Swap pending-session reduction |
| Duplicate/replay | Move generated matrix; Handoff journal replay; Swap duplicate Prepare/Decision |
| Delay/expiry/reorder | manual clocks at operation and envelope deadlines; delayed Swap abort; Abort-before-Prepare tombstone |
| Disconnect | Activity and Swap control-session loss reduces every pending kind to acknowledgement loss and closes the session |
| Journal failure | Handoff pre-write failure blocks before Adapter/catalog mutation; Handoff and Move write-after-result failures replay without duplicate Adapter/catalog work; Replace, Swap coordinator, and endpoint tests inject pre-write and ambiguous post-write failure |

The Handoff pre-write journal test was added test-first. Its RED phase failed
only because the fixture had no boundary injection parameter; the minimal GREEN
change wired the `IOperationJournal` test double. Review then found three blind
spots: transient journal results did not permanently bind the original digest,
the current Mirror driver could become ViewOnly without rotating its epoch, the
safe Owner could become ViewOnly while a peer drove, and Handoff/Move did not
exercise write-after-result ambiguity. Regression tests failed before the
production fixes, then passed after the journal retained the first digest
independently of its retryable execution entry and Mirror kept the safe Owner
plus current driver eligible.
A second review added explicit handler-exception binding, bounded the in-memory
journal to 4,096 distinct IDs, and wrapped every generated SUT call so an
unexpected exception carries its seed/event/fault trace. A real in-memory
journal wrapped by the post-result fault double proves Handoff and Move replay
without repeating Adapter or catalog work.

## Hosted evidence pending

No Windows, Linux, or hosted macOS result is claimed by this local section.
After push, this document must record the exact commit, CI/CodeQL run and job
IDs, Secret Scan result, artifact IDs, and independently summed TRX counters.
The evidence/task-status commit must then pass the same workflows before task
3.4 is final.

## Remaining limits

- The generated set covers state machines already implemented in the current
  core. It cannot cover not-yet-built Remote Window, Group, Scene, or complete
  Mirror transport/UI behavior.
- Deterministic ports model disconnect and journal faults; they do not prove an
  OS process kill, abrupt power loss, filesystem rollback, real interface churn,
  or physical packet behavior.
- Hosted runners and same-host TCP loopback are CI/contract evidence, not real
  two-device or native permission evidence.

Therefore this local evidence does not yet close task 3.4, the release-wide
property/fault criteria, or Flowspan v1.
