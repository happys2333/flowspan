# Durable Atomic Swap Endpoint Evidence — 2026-07-16

## Evidence boundary

This slice proves one bounded, protected Atomic Swap endpoint journal per
Device. It proves that a participant persists its exact reservation before
returning Prepared, persists Commit or Abort before local catalog mutation or
acknowledgement, and deterministically reduces a recorded decision after
restart.

It does not prove that the Activity catalog itself is durable, that Swap is
carried over an authenticated and capability-authorized wire protocol, or that
Desktop confirmation and recovery UI exist. It also does not prove physical
two-device interruption, abrupt process termination, power loss, live Linux
Secret Service, or migration of arbitrary application process state.

Verified implementation commit:
`0f59891a5e866618409719afce5d5307c3340ff7`

Branch: `codex/v1-foundation`

The implementation decision and its limits are recorded in
[ADR 0013](../adr/0013-durable-swap-endpoint-journal.md).

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
dotnet format Flowspan.slnx --no-restore --verbosity minimal
dotnet format Flowspan.slnx --no-restore --verify-no-changes \
  --verbosity minimal
dotnet build Flowspan.slnx --no-restore --configuration Release --nologo
dotnet test Flowspan.slnx --no-restore --no-build \
  --configuration Release --nologo
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable \
  --include-transitive --no-restore
git diff --check
```

The stateful endpoint groups also ran in 20 fresh testhost processes per
command:

```sh
dotnet test tests/Flowspan.Integration.Tests/Flowspan.Integration.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Swap'
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~SwapEndpoint'
dotnet test \
  tests/Flowspan.Platform.Windows.Tests/Flowspan.Platform.Windows.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~SwapEndpoint'
dotnet test \
  tests/Flowspan.Platform.MacOS.Tests/Flowspan.Platform.MacOS.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~SwapEndpoint'
dotnet test \
  tests/Flowspan.Platform.Linux.Tests/Flowspan.Platform.Linux.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~SwapEndpoint'
```

## Local results

- locked restore, formatting, verification, and patch-whitespace checks passed;
- the Release build passed with 0 warnings and 0 errors;
- 632 tests passed, 0 failed, and 0 skipped:
  - Desktop: 158;
  - Transport: 137;
  - Integration: 112;
  - Security: 90;
  - Domain: 36;
  - Protocol: 17;
  - shared platform contracts: 16;
  - Windows platform contracts: 18;
  - macOS platform contracts: 16;
  - Linux platform contracts: 18;
  - mDNS transport contracts: 14;
- Integration Swap stress passed 20/20 fresh processes, with all 42 filtered
  tests passing in every process;
- shared authenticated endpoint-file contracts passed 20/20 fresh processes,
  with all 3 tests passing in every process;
- Windows, macOS, and Linux endpoint-store groups passed 20/20 fresh processes,
  with 2, 2, and 1 tests respectively in every process;
- the matching macOS group used a disposable native Security.framework Keychain
  item. Windows and Linux groups on this host prove only their non-native
  platform contracts;
- explicit Desktop composition printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`;
- the deterministic simulator negotiated protocol 1.0 and printed
  `Atomic swap committed: True`;
- the NuGet direct/transitive vulnerability audit reported no known vulnerable
  package in all 24 projects.

## Proven behavior

### Durable participant ordering

- a successful Prepare is published only after the Device-owned journal has
  saved its exact Operation ID, reservation token, UTC expiry, request digest,
  original Activity, and incoming Activity;
- Commit or Abort is saved with the exact two-Device/token decision before any
  catalog change or success acknowledgement;
- an Abort received before Prepare is a durable terminal tombstone, so delayed
  Prepare cannot reopen the Operation;
- duplicate Prepare, Commit, and Abort delivery is idempotent, while Operation,
  request, token, or decision reuse with different content conflicts;
- a Prepared reservation and a persisted Commit that has not reached its exact
  replacement state both reject overlapping new Swap work.

### Restart reduction and convergence

- a reconstructed Prepared record remains `Recovering / OperationInProgress`;
  it never guesses Abort from expiry or process restart;
- a reconstructed Commit accepts only the exact original with no incoming ID,
  then uses the catalog's atomic replace primitive;
- if the exact computed replacement already exists and the original is absent,
  replay is complete without a second mutation;
- missing, drifted, duplicated, or otherwise conflicting catalog state remains
  `Recovering / RevisionConflict` and does not change either Activity;
- a coordinator and two endpoint journals were all reconstructed after the
  first Commit delivery was dropped and the second endpoint had committed. The
  coordinator replayed its durable Commit and both endpoints converged;
- the Activity catalog remains an external authoritative Adapter boundary. This
  reducer does not claim process-memory, unsaved external-state, or arbitrary
  application recovery.

### Ambiguous persistence and hostile state

- a failed Prepare save publishes no in-memory reservation and changes no
  Activity;
- a decision save injected after the backing store accepted bytes changes no
  Activity through the stale instance. That instance permanently requires
  reopen, after which the actual durable Commit is reduced;
- the endpoint codec binds the top-level Device ID, orders Operation records and
  decision participants, limits state to 32 records and 4 MiB, and recomputes
  request, payload, descriptor, and decision digests;
- unknown fields, Device mismatch, duplicate or noncanonical Operation order,
  record and byte overflow, invalid enum and UTC values, digest tamper, and a
  valid decision that does not bind the recorded local token all fail closed.

### Protected endpoint persistence

- endpoint state uses its own `FSEF` AES-256-GCM envelope with authenticated
  header/ciphertext, fresh nonce, and same-directory atomic replacement;
- its 256-bit key interface, path, magic, and purpose identifiers are separate
  from coordinator `FSSF`, Replace `FSRF`, identity, and Trust state;
- Windows uses a Swap-endpoint-specific CurrentUser DPAPI context and key path.
  The hosted Windows suite performs the matching native DPAPI round trip;
- macOS uses a Swap-endpoint-specific Keychain service/account. Both local and
  hosted macOS suites perform a disposable native Security.framework round trip;
- Linux uses a Swap-endpoint-specific Secret Service purpose/account and
  coordination lock. Hosted Ubuntu uses a controlled fake `secret-tool`, so it
  proves invocation and disclosure contracts rather than a live unlocked
  desktop Secret Service;
- plaintext canaries do not appear in protected files, and tampered envelopes,
  invalid bounds, invalid key lengths, and pre-cancellation fail closed.

## Review findings closed before the implementation commit

The two-axis code/spec review found no documented standards violation and no
scope creep. Its three Spec findings were closed before commit:

- a persisted Commit whose catalog reduction failed now continues to exclude an
  overlapping Swap instead of being treated like terminal history;
- hostile codec tests now cover every class named by ADR 0013, including
  participant-token mismatch and decode bounds;
- the design, threat model, test strategy, README, and ubiquitous language now
  describe the protected endpoint slice and its remaining limits.

## Hosted results for the implementation commit

GitHub Actions CI run
[`29468865023`](https://github.com/happys2333/flowspan/actions/runs/29468865023)
completed successfully for exact implementation commit
`0f59891a5e866618409719afce5d5307c3340ff7`:

- Windows job
  [`87527785384`](https://github.com/happys2333/flowspan/actions/runs/29468865023/job/87527785384):
  success;
- Ubuntu job
  [`87527785382`](https://github.com/happys2333/flowspan/actions/runs/29468865023/job/87527785382):
  success;
- macOS job
  [`87527785391`](https://github.com/happys2333/flowspan/actions/runs/29468865023/job/87527785391):
  success;
- Secret Scan job
  [`87527785399`](https://github.com/happys2333/flowspan/actions/runs/29468865023/job/87527785399):
  success.

The downloaded test artifacts were:

- Windows artifact `8364039719`, `test-results-Windows`;
- Linux artifact `8364038194`, `test-results-Linux`;
- macOS artifact `8364031948`, `test-results-macOS`.

Each artifact contains 11 TRX files. Directly summing their counters produced
632 passed and 0 failed tests on each OS. Thus the hosted count comes from the
uploaded test evidence rather than only the job conclusion. Each OS job also
passed locked restore, format verification, warning-as-error Release build,
explicit TEST MODE composition, simulator, and artifact upload.

CodeQL run
[`29468865044`](https://github.com/happys2333/flowspan/actions/runs/29468865044)
and Analyze C# job
[`87527785527`](https://github.com/happys2333/flowspan/actions/runs/29468865044/job/87527785527)
completed successfully. CodeQL scanned 180/180 C# files and uploaded the result.

## Hosted results for the evidence commit

The evidence and task-status commit
`c9e3125d3df0a86656e502e4f9f70894f3ad9624` was independently verified by CI
run
[`29469102251`](https://github.com/happys2333/flowspan/actions/runs/29469102251):

- Windows job
  [`87528518993`](https://github.com/happys2333/flowspan/actions/runs/29469102251/job/87528518993):
  success;
- Ubuntu job
  [`87528519014`](https://github.com/happys2333/flowspan/actions/runs/29469102251/job/87528519014):
  success;
- macOS job
  [`87528518998`](https://github.com/happys2333/flowspan/actions/runs/29469102251/job/87528518998):
  success;
- Secret Scan job
  [`87528519000`](https://github.com/happys2333/flowspan/actions/runs/29469102251/job/87528519000):
  success.

The downloaded Windows artifact `8364133927`, Linux artifact `8364121907`, and
macOS artifact `8364117184` each contained 11 TRX files. Direct counter sums
again produced 632 passed and 0 failed tests on every hosted OS.

CodeQL run
[`29469102202`](https://github.com/happys2333/flowspan/actions/runs/29469102202)
and Analyze C# job
[`87528518741`](https://github.com/happys2333/flowspan/actions/runs/29469102202/job/87528518741)
also completed successfully, scanned 180/180 C# files, and uploaded the result.
Thus both the implementation and evidence commits passed the same hosted gates.

## Closure race, diagnosis, and repair

The first documentation closure commit
`589e9f987b2d9ecb6d25da9acf18267f6f3d667c` triggered CI run
[`29469290059`](https://github.com/happys2333/flowspan/actions/runs/29469290059).
Windows job
[`87529045009`](https://github.com/happys2333/flowspan/actions/runs/29469290059/job/87529045009),
macOS job
[`87529045007`](https://github.com/happys2333/flowspan/actions/runs/29469290059/job/87529045007),
and Secret Scan job
[`87529045017`](https://github.com/happys2333/flowspan/actions/runs/29469290059/job/87529045017)
passed. Ubuntu job
[`87529045058`](https://github.com/happys2333/flowspan/actions/runs/29469290059/job/87529045058)
failed only
`DesktopActivityRuntimeTests.AuthenticatedRuntimesExchangeNoteAndExposeOnlyEligibleLiveTarget`:
the test expected cancellation during simultaneous two-ended shutdown but one
end observed `EndOfStreamException` first. CodeQL run
[`29469290073`](https://github.com/happys2333/flowspan/actions/runs/29469290073)
and Analyze C# job
[`87529045130`](https://github.com/happys2333/flowspan/actions/runs/29469290073/job/87529045130)
passed.

The failure was an existing authenticated control-session teardown race rather
than an endpoint-journal atomicity failure. Both ends shared a cancellation
token. Canceling both handlers could make one secure channel fault and close
its stream before the other handler observed its own cancellation, producing
EOF even though local stop had already been requested. The endpoint
implementation and evidence commits had passed the same Ubuntu test, and the
failing test does not exercise Swap endpoint persistence.

Repair commit `06c659f5e79d5ffee3174491ba66cd7090bb1b4a` adds a deterministic
connection double that returns EOF only after its read token is canceled. The
test failed before the repair with raw `EndOfStreamException` and passes after
`ActivityControlSession` normalizes an I/O termination to
`OperationCanceledException` only when its linked local cancellation is already
requested. A negative contract proves that an EOF while the session is still
running remains `EndOfStreamException`, so ordinary peer disconnect is not
hidden.

Local repair verification passed:

- locked restore, format verification, patch-whitespace check, and a Release
  build with 0 warnings and 0 errors;
- all 634 tests with 0 failed and 0 skipped, including 158 Desktop and 139
  Transport tests;
- the exact formerly failing Desktop test in 100/100 fresh testhost processes;
- all four real authenticated loopback session tests;
- explicit TEST MODE composition and the deterministic simulator;
- direct/transitive NuGet vulnerability audit with no known vulnerable package
  in any of the 24 projects.

GitHub Actions CI run
[`29469818397`](https://github.com/happys2333/flowspan/actions/runs/29469818397)
passed for the exact repair commit:

- Windows job
  [`87530564435`](https://github.com/happys2333/flowspan/actions/runs/29469818397/job/87530564435):
  success;
- Ubuntu job
  [`87530564449`](https://github.com/happys2333/flowspan/actions/runs/29469818397/job/87530564449):
  success;
- macOS job
  [`87530564432`](https://github.com/happys2333/flowspan/actions/runs/29469818397/job/87530564432):
  success;
- Secret Scan job
  [`87530564423`](https://github.com/happys2333/flowspan/actions/runs/29469818397/job/87530564423):
  success.

The downloaded Windows artifact `8364396551`, Linux artifact `8364378923`, and
macOS artifact `8364375147` each contained 11 TRX files. Directly summing their
counters produced 634 passed, 0 failed, and 0 not executed on each hosted OS.
Each job also passed locked restore, format verification, warning-as-error
Release build, explicit TEST MODE composition, simulator, and artifact upload.

CodeQL run
[`29469818419`](https://github.com/happys2333/flowspan/actions/runs/29469818419)
and Analyze C# job
[`87530564407`](https://github.com/happys2333/flowspan/actions/runs/29469818419/job/87530564407)
completed successfully and uploaded the result.

The documentation closure commit containing this section remains subject to
the same CI, Secret Scan, and CodeQL gates before task 3.3b is treated as final.

## Remaining work and explicit limits

- The Activity catalog itself is not persisted by this slice. A representative
  native Adapter still needs an explicit durable apply/compensation contract.
- Swap messages are not frozen in the bounded protocol or carried over an
  authenticated, capability-authorized Flowspan control session.
- Desktop has no Swap inventory, exact confirmation, receipt, recovery, or
  accessibility surface, and no Swap capability is production-composed.
- The implemented Activity is the descriptor-only `workspace.note/v1` tracer;
  Flowspan does not migrate arbitrary application processes or private state.
- Same-host contracts and hosted runners do not replace physical two-device LAN,
  disconnect/reconnect, sleep/wake, abrupt process termination, power loss,
  packaged-app, live Linux Secret Service, or native application evidence.

Therefore this evidence closes only task 3.3b. Parent task 3.3, generated/fault
task 3.4, the Atomic Swap release criterion, and v1 remain open.
