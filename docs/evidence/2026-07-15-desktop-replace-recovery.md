# Desktop Replace Receipt and Recovery Evidence — 2026-07-15

## Evidence boundary

This slice proves task 7.3c.5: one bounded, payload-free, target-local,
read-only projection of protected Replace and undo journal state, plus Desktop
startup and accessibility presentation. It does not expose a Replace, recovery,
or undo command and does not compose a production destructive Replace endpoint.

Verified implementation commit:
`04cad52170676005a6706f3fe471a45becd5e8f3`

Branch: `codex/v1-foundation`

Local host: macOS 26.5.2 (build 25F84), Apple Silicon, Asia/Shanghai

Toolchain:

```text
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
dotnet test Flowspan.slnx --no-restore --no-build --configuration Release --nologo
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
git diff --check
```

The two recovery-sensitive groups also ran in 20 fresh testhost processes per
command:

```sh
dotnet test tests/Flowspan.Integration.Tests/Flowspan.Integration.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~RecoverySnapshot'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~ReplaceRecovery'
```

## Local results

- locked restore: passed for all 24 projects;
- formatting and patch whitespace checks: passed;
- Release build: passed with 0 warnings and 0 errors;
- tests: 541 passed, 0 failed, 0 skipped:
  - Desktop: 130;
  - Transport: 137;
  - Integration: 68;
  - Security: 90;
  - Domain: 33;
  - Protocol: 17;
  - Platform contracts: 10;
  - Windows platform contracts: 14;
  - macOS platform contracts: 12;
  - Linux platform contracts: 16;
  - mDNS transport contracts: 14;
- fresh-process recovery stress: 20/20 processes for each group, with 3
  Integration and 5 Desktop filtered tests passing per process;
- explicit Desktop TEST MODE composition: passed;
- NuGet audit: no known vulnerable direct or transitive package in all 24
  projects;
- deterministic simulator: passed, but it still exercises Handoff and is not
  counted as Replace recovery evidence.

## Proven behavior

### Protected-state read model

- the projection reads the same immutable in-memory snapshot reconstructed from
  the authenticated target-local Replace store; it creates no second log or
  history file and performs no network request;
- one snapshot contains at most 64 combined Replace/undo records; unresolved
  pending or recorded-recovering boundaries sort before terminal history, then
  records use known time and Operation ID for deterministic ordering;
- each record distinguishes Replace versus undo and pending versus terminal,
  and includes only known operation/correlation/capsule/Activity/device IDs,
  timestamp kind/value, status, redacted failure code, and capsule expiry/
  availability;
- a pre-capture pending journal entry is shown with only its known Operation ID;
  participant, Activity, correlation, capsule, and time fields remain explicitly
  absent instead of being guessed;
- available, expired, pending, and consumed capsule states are derived from the
  same capsule and undo journals at the snapshot time;
- serialized snapshot canaries prove that Activity titles, descriptor and
  payload content, target/incoming descriptor digests, request digests, and
  exceptions are absent.

### Desktop startup and failure boundary

- production composition selects the existing Windows DPAPI, macOS Keychain,
  or Linux Secret Service protected Replace payload store and opens it during
  Activity workspace startup;
- an empty or valid store produces a captured read-only snapshot; a load,
  authentication, schema, key, or I/O failure maps to
  `REPLACE RECOVERY STATE UNAVAILABLE — REPLACE LOCKED` without exception text;
- a Replace-state failure does not block portable note creation, Handoff, or
  Move once identity and Trust are ready;
- the recovery surface names unresolved-count and truncation states and shows
  exact snapshot and capsule times without claiming that a stale snapshot is a
  live action state;
- `IDesktopActivityService` still has no Replace, recovery, or undo command. The
  production `AuthenticatedActivitySessionHandler` still receives
  `replacePeer: null`, and both runtime capability and the destructive button
  remain false/locked.

### Accessibility and read-only presentation

- Avalonia Headless verifies programmatic names for recovery status, guidance,
  coverage, snapshot time, record list, state, Operation/correlation/capsule
  IDs, and undo availability;
- a two-record list is keyboard navigable, while the panel contains no action
  control and the existing destructive Replace control remains disabled;
- recovery, terminal, unavailable, expiry, and truncation states are expressed
  in text rather than by color alone; the top-level sharing indicator remains
  `NOT SHARING`.

## Hosted results for the implementation commit

GitHub Actions CI run
[`29418943469`](https://github.com/happys2333/flowspan/actions/runs/29418943469)
completed successfully for the exact implementation commit:

- Secret Scan job
  [`87364165876`](https://github.com/happys2333/flowspan/actions/runs/29418943469/job/87364165876):
  success;
- Ubuntu job
  [`87364165953`](https://github.com/happys2333/flowspan/actions/runs/29418943469/job/87364165953):
  success;
- macOS job
  [`87364165968`](https://github.com/happys2333/flowspan/actions/runs/29418943469/job/87364165968):
  success;
- Windows job
  [`87364166010`](https://github.com/happys2333/flowspan/actions/runs/29418943469/job/87364166010):
  success.

Each OS job passed locked restore, formatting, warning-as-error Release build,
all 541 tests, explicit TEST MODE composition, the deterministic Handoff
simulator, and evidence upload.

CodeQL run
[`29418943525`](https://github.com/happys2333/flowspan/actions/runs/29418943525)
and Analyze C# job
[`87364177367`](https://github.com/happys2333/flowspan/actions/runs/29418943525/job/87364177367)
completed successfully for the same exact commit.

Hosted runners prove portable build, contracts, protected-store abstractions,
and the native-safe tests already present in the matrix. They do not prove
physical LAN reachability, abrupt power-loss durability, packaged applications,
native accessibility, or arbitrary-application Adapters.

## Evidence limits

- The local host is macOS. Windows and Linux platform tests here are managed
  contracts, not native packaged Desktop execution; hosted results are required
  for this candidate but remain portable build/contract evidence.
- Avalonia Headless proves bindings, names, focus, navigation, and text state.
  It is not native NVDA, Narrator, VoiceOver, Orca, keyboard-layout, scaling,
  contrast, or physical-display evidence.
- No physical LAN, two-device, sleep/wake, abrupt power loss, arbitrary native
  application, process-state, Remote Window, or independent security review was
  exercised.
- The snapshot is read-only and captured at Activity workspace startup or a
  service change. No live expiry timer, manual recovery, target-local undo
  action, source-side Replace command, or destructive endpoint is present.
- Therefore task 7.3c, parent task 7.3, the Replace release criterion, and v1
  remain incomplete.

This evidence completes only task 7.3c.5. Task 7.3c, its parent 7.3, and the v1
criterion “Replace offers an honest undo capsule or blocks before destructive
work” remain unchecked.
