# Bounded Replace Core and Authenticated Protocol Evidence — 2026-07-15

## Evidence boundary

This slice proves one bounded `workspace.note/v1` Replace application core and
its authenticated control messages. It does not claim that Replace is exposed
by the desktop, durable across process restart, exercised between physical
devices, or able to preserve arbitrary application state.

Implementation commit:
`d0f492aaab4b6a4df4b83340b40cd5dfd4a46934`

Branch: `codex/v1-foundation`

Local host: macOS, Apple Silicon, Asia/Shanghai

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

Two affected groups also ran in 20 fresh testhost processes each:

```sh
dotnet test tests/Flowspan.Integration.Tests/Flowspan.Integration.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~ReplaceTests'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Replace'
```

## Local results

- locked restore: passed for all 24 projects;
- formatting: passed;
- Release build: passed with 0 warnings and 0 errors;
- tests: 452 passed, 0 failed, 0 skipped:
  - Desktop: 105;
  - Transport: 111;
  - Security: 90;
  - Integration: 37;
  - Domain: 33;
  - Protocol: 17;
  - Platform contracts: 8;
  - Windows platform contracts: 12;
  - macOS platform contracts: 10;
  - Linux platform contracts: 15;
  - mDNS transport contracts: 14;
- Replace application fresh-process stress: 20/20 passed;
- Replace codec/session/encrypted-loopback fresh-process stress: 20/20 passed;
- explicit desktop TEST MODE composition: passed;
- NuGet audit: no known vulnerable direct or transitive package;
- deterministic simulator: passed, but it still runs Handoff and is not counted
  as Replace evidence.

## Proven behavior

### Application and Adapter boundary

- the target requires its peer-relative `activity.replace` capability;
  `activity.offer` does not authorize Replace;
- the request binds the target Activity ID, expected revision, expected
  descriptor digest, incoming descriptor, Placement, Operation/correlation,
  devices, deadline, and undo expiry;
- revision or descriptor mismatch rejects before Adapter capture;
- capture failure, mismatched captured state, or capsule-store failure rejects
  before incoming resume and preserves the original Activity;
- `workspace.note/v1` captures only its validated semantic descriptor;
- a committed Replace stores one target-owned capsule before resume and installs
  the incoming Activity as the next revision;
- exact Replace retry returns the recorded receipt and capsule reference without
  repeating capture or resume;
- successful undo restores the original descriptor as a new revision; exact
  retry returns the recorded result without a second restore;
- expired capsules reject before restore, and a different operation cannot
  consume an already consumed capsule.

### Authenticated protocol boundary

- Replace uses separate strict `activity.replace` and
  `activity.replace.result` messages rather than reusing Handoff/Move transfer;
- request and result decoders reject unknown/missing fields, wrong target,
  malformed values, digest mismatch, and target-snapshot tampering;
- the result carries a payload-free capsule reference only; the preserved target
  descriptor and payload never return to the source;
- a pending sender verifies participants, correlation/Operation, incoming
  Activity/digest, target ID/revision/digest, and undo expiry;
- a forged result for another target snapshot faults the session closed;
- session loss after send yields acknowledgement-lost rather than assuming
  commit or rejection;
- one real same-host loopback test carries Replace over the production
  authenticated/encrypted control connection and receives an exactly bound undo
  reference.

## Hosted results for the implementation commit

GitHub Actions CI run
[`29399005803`](https://github.com/happys2333/flowspan/actions/runs/29399005803)
completed successfully:

- macOS job
  [`87298989916`](https://github.com/happys2333/flowspan/actions/runs/29399005803/job/87298989916):
  success;
- Ubuntu job
  [`87298989967`](https://github.com/happys2333/flowspan/actions/runs/29399005803/job/87298989967):
  success;
- Windows job
  [`87298990009`](https://github.com/happys2333/flowspan/actions/runs/29399005803/job/87298990009):
  success;
- Secret Scan job
  [`87298989945`](https://github.com/happys2333/flowspan/actions/runs/29399005803/job/87298989945):
  success.

Each OS job performed locked restore, formatting, warning-as-error Release build,
all tests, explicit TEST MODE composition, and the deterministic Handoff
simulator. Hosted runner success is portable build/contract evidence, not
physical-device or native Adapter evidence.

CodeQL run
[`29399005823`](https://github.com/happys2333/flowspan/actions/runs/29399005823)
and Analyze C# job
[`87298990175`](https://github.com/happys2333/flowspan/actions/runs/29399005823/job/87298990175)
completed successfully. CodeQL reported 153/153 C# files scanned and completed
processing the uploaded result.

## Remaining before the Replace release criterion can pass

- a protected durable capsule store and restart-safe Replace/undo journals;
- expiry cleanup, storage tamper, disk failure, crash-boundary, and restart
  recovery tests;
- an authorized remote target Activity snapshot/inventory flow;
- desktop destructive preview, exact target confirmation, receipt/recovery, and
  visible target-local undo with accessible keyboard/screen-reader behavior;
- capability revocation races in the composed desktop runtime;
- native Adapter evidence beyond the descriptor-only workspace note;
- physical two-device LAN interruption and packaged Windows/macOS/Linux tests.

The desktop composition intentionally does not inject `IReplacePeer` or expose a
Replace control. Therefore the v1 release criterion “Replace offers an honest
undo capsule or blocks before destructive work” remains unchecked.
