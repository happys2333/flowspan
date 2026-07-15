# Protected Replace Recovery State Evidence — 2026-07-15

## Evidence boundary

This slice proves bounded, authenticated persistence and restart-safe recovery
for the existing descriptor-only `workspace.note/v1` Replace core. It does not
claim that Replace is exposed by the desktop, that a physical two-device or LAN
path was exercised, that Linux used a live desktop Secret Service, or that
Flowspan can preserve arbitrary application process state.

Verified implementation commit:
`3652c9b8b12bee776f0a08ee7bed5990d658298e`

Branch: `codex/v1-foundation`

Local host: macOS 26.5.2 (build 25F84), Apple Silicon, Asia/Shanghai

Toolchain:

```text
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4.27133
```

## Commands

```sh
dotnet restore Flowspan.slnx --locked-mode --nologo
dotnet format Flowspan.slnx --no-restore --verify-no-changes --verbosity minimal
dotnet build Flowspan.slnx --no-restore --configuration Release --nologo
dotnet test Flowspan.slnx --no-restore --no-build --configuration Release --nologo
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
git diff --check
```

The two most stateful affected groups also ran in 20 fresh testhost processes
per command:

```sh
dotnet test tests/Flowspan.Integration.Tests/Flowspan.Integration.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~ReplaceTests'
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~AuthenticatedReplaceStateFileTests'
```

The new project references initially caused locked restore to reject the stale
lock graph as intended. `dotnet restore Flowspan.slnx --force-evaluate` refreshed
and committed the affected lock files; the candidate then passed locked restore.

## Local results

- locked restore: passed for all 24 projects;
- formatting and patch whitespace checks: passed;
- Release build: passed with 0 warnings and 0 errors;
- tests: 472 passed, 0 failed, 0 skipped:
  - Desktop: 105;
  - Transport: 111;
  - Security: 90;
  - Integration: 50;
  - Domain: 33;
  - Protocol: 17;
  - Platform contracts: 10;
  - Windows platform contracts: 14;
  - macOS platform contracts: 12;
  - Linux platform contracts: 16;
  - mDNS transport contracts: 14;
- combined Replace and persistent-state fresh-process stress: 20/20 processes,
  with 26 tests passing in each process;
- authenticated state-file fresh-process stress: 20/20 processes, with 2 tests
  passing in each process;
- the macOS platform suite performed a disposable native Security.framework
  Keychain key create, authenticated-file save, restart/load, and cleanup;
- explicit desktop TEST MODE composition: passed;
- NuGet audit: no known vulnerable direct or transitive package in all 24
  projects;
- deterministic simulator: passed, but it still exercises Handoff and is not
  counted as Replace evidence.

## Proven behavior

### One durable recovery boundary

- one `IReplaceStateStore` snapshot owns undo capsules, Replace pending/final
  records, undo pending/final records, and capsule-consumption markers;
- the versioned canonical JSON snapshot is deterministically ordered and bounded
  to 16 capsules, 256 Replace records, 256 undo records, and 4 MiB;
- load rejects unknown fields or versions, malformed or non-canonical ordering,
  duplicate identifiers, out-of-range values, and reconstructed descriptor
  digest mismatches;
- a failed atomic save does not publish the proposed state in memory;
- expiry cleanup atomically removes an unreserved expired capsule and its
  completed Replace replay record, preserves a capsule reserved by pending undo,
  and retains committed consumption proof.

### Restart and destructive-boundary behavior

- Replace persists `Pending` before Adapter capture/resume and persists its
  terminal receipt afterward;
- an exact completed Replace retry after store reconstruction replays the same
  receipt without repeating Adapter work;
- a reconstructed pending Replace returns `Recovering / OperationInProgress`
  without repeating capture or resume;
- if resume rejects and capsule cleanup cannot be saved, or if resume succeeds
  but catalog swap fails, the capsule remains and the result is explicitly
  `Recovering` rather than falsely reporting a safe rollback;
- undo persists a pending reservation before restore, then persists terminal
  result and consumption in one snapshot;
- exact completed undo retry after reconstruction replays without a second
  restore, while a different operation cannot consume the capsule again;
- a terminal-save failure leaves pending recovery state, so restart never
  repeats already-attempted destructive Adapter work.

### Protected platform persistence

- the state file uses AES-256-GCM with a fresh nonce per save and authenticates
  its magic, version, nonce, payload length, ciphertext, and tag;
- writes use a same-directory write-through temporary file, flush to disk, and
  atomic move; Unix files are owner-only and reparse-point paths are rejected;
- tamper, malformed length, invalid key size, oversize payload, cancellation,
  and file/store failure paths fail closed;
- Windows stores a separately attributed random 32-byte state key under
  CurrentUser DPAPI. The hosted Windows test performs a real DPAPI API
  create/save/reload round trip;
- macOS stores the random key under a separate Keychain service/account. Both
  the local macOS suite and hosted macOS suite perform a real
  Security.framework round trip with disposable items;
- Linux passes the Base64 key through stdin and stores only that key as a
  separately attributed Secret Service item; the descriptor and state snapshot
  never enter command arguments or the Secret Service item.

The Ubuntu tests use a controlled fake `secret-tool` runner. They prove bounded
invocation and payload contracts, not an unlocked live desktop Secret Service.

## Hosted results for the implementation commit

GitHub Actions CI run
[`29405003884`](https://github.com/happys2333/flowspan/actions/runs/29405003884)
completed successfully for the exact implementation commit:

- Windows job
  [`87318198982`](https://github.com/happys2333/flowspan/actions/runs/29405003884/job/87318198982):
  success;
- Ubuntu job
  [`87318198991`](https://github.com/happys2333/flowspan/actions/runs/29405003884/job/87318198991):
  success;
- macOS job
  [`87318199002`](https://github.com/happys2333/flowspan/actions/runs/29405003884/job/87318199002):
  success;
- Secret Scan job
  [`87318199003`](https://github.com/happys2333/flowspan/actions/runs/29405003884/job/87318199003):
  success.

Each OS job passed locked restore, formatting, warning-as-error Release build,
all 472 tests, explicit TEST MODE composition, the deterministic Handoff
simulator, and test-evidence upload. These hosted runners prove portable build,
contract, and the platform-API smoke described above. They do not prove a
physical LAN, two physical devices, packaged applications, or native
arbitrary-application Adapters.

CodeQL run
[`29405003828`](https://github.com/happys2333/flowspan/actions/runs/29405003828)
and Analyze C# job
[`87318198704`](https://github.com/happys2333/flowspan/actions/runs/29405003828/job/87318198704)
completed successfully. CodeQL scanned 163/163 C# files and uploaded the result.

## Remaining before the Replace release criterion can pass

- an authorized remote target Activity inventory and exact snapshot selection;
- a desktop destructive preview that identifies both Activities and requires
  explicit confirmation;
- a visible receipt/recovery surface and target-local undo control with
  keyboard and screen-reader contracts;
- production desktop composition of the Replace endpoint only after those
  safeguards are present;
- capability-revocation races in the composed desktop runtime;
- live desktop Linux Secret Service, physical abrupt termination/power-loss,
  and native Adapter evidence beyond `workspace.note/v1`;
- physical two-device LAN interruption and packaged Windows/macOS/Linux tests.

The desktop still intentionally does not inject `IReplacePeer` or expose a
Replace control. Therefore task 7.3c, its parent 7.3, and the v1 criterion
“Replace offers an honest undo capsule or blocks before destructive work” remain
unchecked. This evidence completes only protected durable-core subtasks 7.3c.1
and 7.3c.2.
