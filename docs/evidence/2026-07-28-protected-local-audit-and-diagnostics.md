# Protected Local Audit and Diagnostics Evidence — 2026-07-28

## Evidence boundary

This record closes the remaining trust, operation-history, and redacted
diagnostics portion of task 8.3. It covers a bounded protected receipt history,
purpose-separated platform stores, whitelist exports, and the Desktop inspect,
delete, clear, preview, and export lifecycle.

All local results are macOS-host same-host evidence. Hosted results are
runner-image evidence. Neither is physical two-device evidence, native
packaged-application evidence, nor external security-review evidence.

Branch: `codex/v1-foundation`

Implementation commit:
`a9e92c5c9ef8fad20dacc3a858eddbecf3cb2aea`
(`feat: protect local audit and diagnostics`).

The closure recorded by the task-status commit containing this record is
effective only after that commit passes CI, Secret Scan, and CodeQL.

## Implemented contract

- `PersistentOperationHistory` retains the latest 256 exact receipts in a
  strict 1 MiB canonical envelope. Complete candidates persist before
  publication; every failed or ambiguous mutation poisons until explicit
  reopen, while reads preserve the last published snapshot.
- `FSOH` state uses independent Keychain, Secret Service, and DPAPI purposes,
  paths, lock files, accounts, contexts, and keys. Unsupported protection
  visibly degrades without an unprotected fallback or Desktop startup crash.
- The production Desktop sink persists real receipts without allowing audit
  I/O failure to rewrite an already determined operation result. Preflight and
  acknowledgement-loss paths without a real receipt remain unrecorded.
- Trust, history, and diagnostics exports are closed field whitelists. They
  exclude identity, Activity, content, descriptor, slot, key, discovery,
  environment, path, and exception details outside their approved fields.
- Diagnostic files are owner-only create-new exports. Listing and deletion are
  confined to generated diagnostic names and reject unsafe names, directory
  links, target links, and dangling links.
- Desktop controls provide redacted inspect, two-step history delete and clear,
  Trust/history/diagnostics export, exact diagnostics preview, and two-step
  diagnostic-file delete while preserving the global `NOT SHARING` state.

## Local environment and commands

```text
Host: macOS 26.5.2 (build 25F84), Apple Silicon, Asia/Shanghai
.NET SDK: 10.0.301
.NET runtime: 10.0.9
RID: osx-arm64
```

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx -c Release --no-restore
dotnet test Flowspan.slnx -c Release --no-build --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  -c Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  -c Release --no-build --no-restore
dotnet list package --vulnerable --include-transitive
git diff --check
```

Focused stress used 20 fresh processes for each filtered Integration, Desktop,
shared Platform, macOS Platform, Linux Platform, and Windows Platform command.
The filters selected 17, 12, 12, 1, 1, and 1 tests respectively.

```sh
dotnet test tests/Flowspan.Integration.Tests -c Release --no-build \
  --no-restore --filter \
  'FullyQualifiedName~PersistentOperationHistoryTests|FullyQualifiedName~LocalDataExportTests'
dotnet test tests/Flowspan.Desktop.Tests -c Release --no-build \
  --no-restore --filter \
  'FullyQualifiedName~LocalData|FullyQualifiedName~TrustExport|FullyQualifiedName~HistoryDeleteClearAndExport|FullyQualifiedName~DiagnosticExportDelete|FullyQualifiedName~TrustedDeviceCapabilitiesAndRevoke'
dotnet test tests/Flowspan.Platform.Tests -c Release --no-build \
  --no-restore --filter \
  'FullyQualifiedName~AuthenticatedOperationHistory|FullyQualifiedName~RedactedExportFile'
dotnet test tests/Flowspan.Platform.MacOS.Tests -c Release --no-build \
  --no-restore --filter 'FullyQualifiedName~History'
dotnet test tests/Flowspan.Platform.Linux.Tests -c Release --no-build \
  --no-restore --filter 'FullyQualifiedName~History'
dotnet test tests/Flowspan.Platform.Windows.Tests -c Release --no-build \
  --no-restore --filter 'FullyQualifiedName~History'
```

## Local results and review

- Locked restore and format verification passed.
- The Release build passed with 0 warnings and 0 errors.
- All 1154 tests passed with 0 failed and 0 skipped:
  - Desktop 201, Transport 255, Integration 338, Security 123;
  - Domain 60, Protocol 47, shared Platform 37;
  - Windows Platform 27, macOS Platform 25, Linux Platform 27;
  - mDNS Transport 14.
- Desktop composition printed its explicit TEST MODE success line, and the
  deterministic simulator exited successfully.
- The direct/transitive vulnerability query reported no known vulnerable
  package, and `git diff --check` passed.
- Every focused suite passed in each of 20 fresh processes: Integration 17,
  Desktop 12, shared Platform 12, and one native-contract test in each
  platform project. This produced 880 successful focused test executions.
- Review-driven regressions enforce strict JSON property order, exact immutable
  receipt reconstruction, and frozen export field sets using the semantic
  `activeAuthorizedCapabilities` field plus non-echoing canaries.
- Receipt-save failure keeps the fixed degraded state visible until explicit
  refresh reopens durable truth, without changing a completed product result.
  Diagnostic lifecycle tests reject directory, target, and dangling links,
  including a reparse point introduced after directory creation.

These results are local macOS and portable same-host evidence. Platform-named
contract projects do not by themselves prove native Windows or Linux APIs.

## Hosted exact-commit evidence

Implementation commit `a9e92c5c9ef8fad20dacc3a858eddbecf3cb2aea`
passed [CI run `30323979173`](https://github.com/happys2333/flowspan/actions/runs/30323979173):

- Ubuntu job [`90165413266`](https://github.com/happys2333/flowspan/actions/runs/30323979173/job/90165413266);
- Windows job [`90165413275`](https://github.com/happys2333/flowspan/actions/runs/30323979173/job/90165413275);
- macOS job [`90165413364`](https://github.com/happys2333/flowspan/actions/runs/30323979173/job/90165413364);
- Secret Scan job [`90165413267`](https://github.com/happys2333/flowspan/actions/runs/30323979173/job/90165413267).

Each OS job restored locked dependencies, verified formatting, built with
warnings as errors, ran all tests, validated Desktop composition in explicit
TEST MODE, ran the simulator, and uploaded test evidence.

Downloaded artifacts were independently hashed and parsed:

| Artifact | ID | SHA-256 | Files/results |
| --- | ---: | --- | ---: |
| Windows TRX | `8675026370` | `b4a03edf506c9891ae211413c81aa43c78368e361634562807403d8a9fbae56b` | 11 TRX |
| Linux TRX | `8675008519` | `b72932dd49268114e0b5955d9804a116ad6e2b9b431ebd92584c6c49bd20b1d7` | 11 TRX |
| macOS TRX | `8675004061` | `13a217337e8f1f229ae1c6562d522ac18946607b1a1fdc868d5209edf7233005` | 11 TRX |
| Gitleaks SARIF | `8674972198` | `b03e5557dee4f98cc0467fe11ac7de2376b6f1f3fb4ff18f05e6bf1512224a64` | 208 rules, 0 results |

Summing every downloaded TRX `Counters` element independently produced the
same result on Windows, macOS, and Ubuntu:

```text
files=11 total=1154 executed=1154 passed=1154 failed=0 error=0 timeout=0
aborted=0 inconclusive=0 passedButRunAborted=0 notRunnable=0
notExecuted=0 disconnected=0 warning=0 completed=0 inProgress=0 pending=0
```

[CodeQL run `30323979168`](https://github.com/happys2333/flowspan/actions/runs/30323979168),
job [`90165413765`](https://github.com/happys2333/flowspan/actions/runs/30323979168/job/90165413765),
also passed for the exact implementation commit. Analysis `1535534042`
evaluated 52 rules and reported 0 results and 0 open branch alerts.

Hosted runners prove portable build and contract behavior on those runner
images, not physical two-device networking or native packaged behavior.

## Remaining gates

- Real-machine native protected-store and lifecycle validation outside hosted
  runner images remains open on Windows, macOS, and Linux.
- Physical two-device networking and lifecycle evidence remains open.
- Reproducible packaging, signing, SBOM, provenance, checksums, installer and
  update validation, and packaged accessibility evidence remain open.
- External independent security/cryptographic review and release-wide v1
  acceptance remain open under tasks 9.1–9.4.
