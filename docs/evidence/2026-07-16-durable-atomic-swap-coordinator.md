# Durable Atomic Swap Coordinator Evidence — 2026-07-16

## Evidence boundary

This slice proves a bounded, payload-free, protected coordinator journal for
the two-participant `workspace.note/v1` Atomic Swap tracer. It proves that the
coordinator persists intent before Prepare, persists a participant-bound
decision before delivery, and reduces an undecided restart only through a
durable Abort.

It does not prove protected endpoint reservation or Activity-catalog
persistence, an authenticated Swap wire protocol, capability enforcement,
Desktop confirmation/recovery UI, physical two-device LAN behavior, abrupt
termination or power-loss durability, or native application-state exchange.

Verified implementation commit:
`d647a7ed624dd1c8e3e839692027eff7128d8950`

Branch: `codex/v1-foundation`

The implementation decision and its limits are recorded in
[ADR 0012](../adr/0012-durable-atomic-swap-intent.md).

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
dotnet format Flowspan.slnx --no-restore --verify-no-changes --verbosity minimal
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

The stateful Swap groups also ran in 20 fresh testhost processes per command:

```sh
dotnet test tests/Flowspan.Integration.Tests/Flowspan.Integration.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Swap'
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Swap'
dotnet test \
  tests/Flowspan.Platform.Windows.Tests/Flowspan.Platform.Windows.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Swap'
dotnet test \
  tests/Flowspan.Platform.MacOS.Tests/Flowspan.Platform.MacOS.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Swap'
dotnet test \
  tests/Flowspan.Platform.Linux.Tests/Flowspan.Platform.Linux.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Swap'
```

## Local results

- locked restore, formatting, and patch-whitespace checks passed;
- the Release build passed with 0 warnings and 0 errors;
- 613 tests passed, 0 failed, and 0 skipped:
  - Desktop: 158;
  - Transport: 137;
  - Integration: 101;
  - Security: 90;
  - Domain: 36;
  - Protocol: 17;
  - platform contracts: 13;
  - Windows platform contracts: 16;
  - macOS platform contracts: 14;
  - Linux platform contracts: 17;
  - mDNS transport contracts: 14;
- Integration Swap stress passed 20/20 fresh processes, with all 31 filtered
  tests passing in every process;
- authenticated Swap-file contracts passed 20/20 fresh processes, with all 3
  filtered tests passing in every process;
- Windows, macOS, and Linux Swap state-store contracts each passed 20/20 fresh
  processes, with 2, 2, and 1 filtered tests respectively in every process;
- explicit Desktop composition printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`;
- the deterministic simulator negotiated protocol 1.0 and printed
  `Atomic swap committed: True` in addition to its Semantic Handoff result;
- the NuGet direct/transitive vulnerability audit reported no known vulnerable
  packages in the solution.

## Proven behavior

### Durable intent and exact transaction binding

- the coordinator saves one intent before contacting either endpoint;
- the intent binds Operation and correlation IDs, an absolute UTC deadline,
  both Device and Activity IDs, both revisions and descriptor digests, and one
  unique device-bound reservation token per participant;
- the journal is exact-once by Operation ID and request digest: an exact replay
  is idempotent, while different participants or content conflict;
- Activity title and descriptor payload never enter the coordinator record;
- the canonical versioned JSON is deterministically ordered and bounded to 256
  transactions and 1 MiB; unknown fields, malformed ordering, duplicate IDs,
  invalid bounds, and digest tamper fail closed.

### Decision, restart, and write-failure ordering

- Commit or Abort is saved on the same transaction before the coordinator sends
  it, and the decision digest covers outcome, timestamp, Abort reason, and both
  ordered Device/token bindings;
- a reconstructed journal replays an existing durable decision without minting
  new tokens;
- a reconstructed undecided intent first persists Abort and can never guess or
  continue toward Commit;
- a failed initial intent save touches neither endpoint and does not publish the
  proposed transaction in memory;
- after any save exception, that journal instance rejects further writes. A new
  instance must reopen the protected file, preventing stale in-memory Abort from
  overwriting a Commit that an ambiguous atomic save actually published;
- generated Prepare/decision fault matrices preserve the invariant that both
  participants end with their originals or both end swapped, never a mixed
  terminal state.

### Participant ordering and exclusion

- each decision is accepted only for the exact local Device/token reservation;
- Abort-before-Prepare creates an idempotent tombstone, so a delayed Prepare
  cannot reopen an already aborted Operation;
- duplicate Commit and Abort delivery is idempotent;
- one live Prepared reservation excludes a second Operation on the same local
  Activity until the first transaction terminates;
- Prepare rejects an incoming Activity ID already present at that endpoint;
- expiry and lost acknowledgement paths converge through recorded decisions
  without claiming success when delivery remains uncertain.

### Protected coordinator persistence

- the Swap snapshot uses its own `FSSF` AES-256-GCM envelope, authenticates its
  bounded header and ciphertext, uses a fresh nonce, and atomically replaces a
  same-directory write-through temporary file;
- its random 256-bit key, file path, magic, and purpose identifiers are separate
  from identity, Trust, and Replace state;
- Windows uses a Swap-specific CurrentUser DPAPI context and key path. The
  hosted Windows suite performs a real DPAPI save/load round trip;
- macOS uses a Swap-specific Keychain service/account. Both the local and hosted
  macOS suites perform a disposable native Security.framework round trip;
- Linux passes only the Base64 key through stdin to a Swap-specific Secret
  Service item. The hosted Ubuntu suite uses a controlled fake `secret-tool`,
  so it proves invocation and disclosure contracts, not a live unlocked desktop
  Secret Service;
- authentication, bounds, cancellation, key, path, and file failures fail
  closed, and plaintext canaries do not appear in the state file.

## Hosted results for the implementation commit

GitHub Actions CI run
[`29466388399`](https://github.com/happys2333/flowspan/actions/runs/29466388399)
completed successfully for exact implementation commit
`d647a7ed624dd1c8e3e839692027eff7128d8950`:

- Windows job
  [`87520319347`](https://github.com/happys2333/flowspan/actions/runs/29466388399/job/87520319347):
  success;
- Ubuntu job
  [`87520319359`](https://github.com/happys2333/flowspan/actions/runs/29466388399/job/87520319359):
  success;
- macOS job
  [`87520319416`](https://github.com/happys2333/flowspan/actions/runs/29466388399/job/87520319416):
  success;
- Secret Scan job
  [`87520319368`](https://github.com/happys2333/flowspan/actions/runs/29466388399/job/87520319368):
  success.

The downloaded test artifacts were:

- Windows artifact `8363141122`, `test-results-Windows`;
- Linux artifact `8363127589`, `test-results-Linux`;
- macOS artifact `8363125026`, `test-results-macOS`.

Each artifact contains 11 TRX files. Directly summing their counters produced
613 passed, 0 failed, 0 errors, and 0 not-executed tests on each OS. Thus the
hosted count is derived from the uploaded test evidence, not only the job
conclusion. Each OS job also passed locked restore, formatting, warning-as-error
Release build, explicit TEST MODE composition, and the updated Handoff/Atomic
Swap simulator.

CodeQL run
[`29466388365`](https://github.com/happys2333/flowspan/actions/runs/29466388365)
and Analyze C# job
[`87520319205`](https://github.com/happys2333/flowspan/actions/runs/29466388365/job/87520319205)
completed successfully. CodeQL scanned 173/173 C# files and uploaded the result.

## Hosted results for the evidence commit

The evidence and task-status commit
`1b05d6435557c403291d9f86872da1115a44033e` was independently verified by CI
run
[`29466866890`](https://github.com/happys2333/flowspan/actions/runs/29466866890):

- Windows job
  [`87521755414`](https://github.com/happys2333/flowspan/actions/runs/29466866890/job/87521755414):
  success;
- Ubuntu job
  [`87521755383`](https://github.com/happys2333/flowspan/actions/runs/29466866890/job/87521755383):
  success;
- macOS job
  [`87521755401`](https://github.com/happys2333/flowspan/actions/runs/29466866890/job/87521755401):
  success;
- Secret Scan job
  [`87521755366`](https://github.com/happys2333/flowspan/actions/runs/29466866890/job/87521755366):
  success.

The downloaded Windows artifact `8363308538`, Linux artifact `8363299513`, and
macOS artifact `8363292801` each contained 11 TRX files. Direct counter sums
again produced 613 passed, 0 failed, 0 errors, and 0 not-executed tests on every
hosted OS.

CodeQL run
[`29466866909`](https://github.com/happys2333/flowspan/actions/runs/29466866909)
and Analyze C# job
[`87521755391`](https://github.com/happys2333/flowspan/actions/runs/29466866909/job/87521755391)
also completed successfully, scanned 173/173 C# files, and uploaded the result.
Thus both the implementation and evidence commits passed the same hosted gates.
The closure commit that records these second results remains subject to those
gates before task 3.3a is treated as final.

## Remaining work and explicit limits

- Task 3.3b must persist protected endpoint reservations, terminal decisions,
  and restart reduction. Current endpoint reservations and Activity catalogs
  remain in process memory.
- The Swap messages are not yet frozen in the bounded protocol or carried over
  an authenticated, capability-authorized Flowspan control session.
- Desktop has no Swap selection, exact confirmation, receipt/recovery, or
  accessibility surface, and no Swap capability is composed into production.
- The current Adapter is the descriptor-only `workspace.note/v1` tracer. It
  does not migrate an arbitrary application's process or private internal state.
- Same-host contracts and hosted runners do not replace physical two-device LAN,
  disconnect/reconnect, abrupt process termination, power-loss, packaged-app,
  live Linux Secret Service, or native application evidence.

Therefore this evidence closes only task 3.3a. Parent task 3.3, generated/fault
task 3.4, any Atomic Swap release criterion, and v1 remain open.
