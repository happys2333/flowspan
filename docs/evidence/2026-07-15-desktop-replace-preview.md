# Desktop Replace Preview and Confirmation Evidence

Date: 2026-07-15

Branch: `codex/v1-foundation`

Scope: v1 task 7.3c.4 candidate — a query-only, keyboard-operable Desktop
Replace preview for `workspace.note/v1`. This evidence does not activate or
exercise destructive Desktop Replace.

Verified implementation commit:
`4407de7187d5aba013efc0073643854545983bca`

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

The Desktop Replace-sensitive group also ran in 20 fresh testhost processes:

```sh
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Replace'
```

## Local results

- locked restore: passed for all 24 projects;
- formatting and patch whitespace checks: passed;
- Release build: passed with 0 warnings and 0 errors;
- tests: 533 passed, 0 failed, 0 skipped:
  - Desktop: 125;
  - Transport: 137;
  - Integration: 65;
  - Security: 90;
  - Domain: 33;
  - Protocol: 17;
  - Platform contracts: 10;
  - Windows platform contracts: 14;
  - macOS platform contracts: 12;
  - Linux platform contracts: 16;
  - mDNS transport contracts: 14;
- fresh-process Desktop Replace stress: 20/20 processes, with 21 filtered
  tests passing in every process;
- explicit Desktop TEST MODE composition: passed;
- NuGet audit: no known vulnerable direct or transitive package in all 24
  projects;
- deterministic simulator: passed, but it still exercises Handoff and is not
  counted as Replace evidence.

## Proven behavior

### Purpose-scoped review

- Replace inventory is loaded only after the user has selected one local
  incoming Activity and one authenticated peer and invokes the named query
  action;
- the preview projects only the existing payload-free inventory snapshot: peer,
  target Activity ID, title, kind, placement, revision, descriptor digest,
  capture time, and bounded/truncated coverage;
- the interface presents the incoming title/kind beside the exact target title,
  kind, peer, placement, revision, and full descriptor digest, and states that
  the source remains active;
- empty, truncated, permission-denied, missing-source, unsupported-Adapter,
  deadline, acknowledgement-loss, peer, and exception outcomes have bounded,
  actionable text and do not include exception details or Activity payload.

### Snapshot and confirmation safety

- selecting another source, peer, or target snapshot clears the prior target
  inventory or confirmation as appropriate;
- every refresh revokes confirmation. An unchanged ID/revision/digest may be
  reselected for orientation but requires confirmation again;
- a missing target or changed revision/digest is not automatically selected and
  produces `TARGET CHANGED — REVIEW REFRESHED INVENTORY`, explicitly stating
  that no Replace request was sent;
- a participant generation rejects a late inventory result after source/peer or
  Activity/session state changes, preventing stale async results from repopulating
  the preview;
- even a test-only fully configured service capability becomes UI-eligible only
  when an exact current preview is selected, explicitly confirmed, and not busy;
  refresh locks it again.

### Accessibility and composition gate

- Avalonia Headless verifies programmatic names for the query, inventory,
  incoming/target comparison, confirmation, activation status, and locked
  destructive control;
- the query, two-item target navigation, and confirmation are keyboard operated
  in the headless shell contract; confirmation text names both Activities and
  the target peer, and state is not conveyed by color alone;
- the top-level sharing indicator remains `NOT SHARING` throughout the preview;
- `IDesktopActivityService` advertises no destructive Replace capability in the
  production runtime. The production `AuthenticatedActivitySessionHandler`
  still uses `replacePeer: null`; Transport tests distinguish that inventory-only
  composition from a test handler that actually owns an inbound `IReplacePeer`;
- the Desktop service exposes no `ReplaceAsync` command, so preview confirmation
  cannot send a destructive message or mutate either catalog.

## Hosted results for the implementation commit

GitHub Actions CI run
[`29416122683`](https://github.com/happys2333/flowspan/actions/runs/29416122683)
completed successfully for the exact implementation commit:

- Windows job
  [`87354599052`](https://github.com/happys2333/flowspan/actions/runs/29416122683/job/87354599052):
  success;
- Secret Scan job
  [`87354599083`](https://github.com/happys2333/flowspan/actions/runs/29416122683/job/87354599083):
  success;
- macOS job
  [`87354599094`](https://github.com/happys2333/flowspan/actions/runs/29416122683/job/87354599094):
  success;
- Ubuntu job
  [`87354599111`](https://github.com/happys2333/flowspan/actions/runs/29416122683/job/87354599111):
  success.

Each OS job passed locked restore, formatting, warning-as-error Release build,
all 533 tests, explicit TEST MODE composition, the deterministic Handoff
simulator, and evidence upload.

CodeQL run
[`29416122672`](https://github.com/happys2333/flowspan/actions/runs/29416122672)
and Analyze C# job
[`87354598944`](https://github.com/happys2333/flowspan/actions/runs/29416122672/job/87354598944)
completed successfully for the same exact commit.

Hosted runners prove portable build, contracts, same-host loopback, and the
native-safe tests already present in the matrix. They do not prove physical LAN
reachability, two physical devices, packaged applications, native accessibility,
or arbitrary-application Adapters.

## Hosted results for the evidence commit

The evidence and task-status commit
`d3727a9197827c0d3b539c9d3f2c9c3621d37e55` was independently verified by CI
run
[`29416495289`](https://github.com/happys2333/flowspan/actions/runs/29416495289):

- Ubuntu job
  [`87355840785`](https://github.com/happys2333/flowspan/actions/runs/29416495289/job/87355840785):
  success;
- Secret Scan job
  [`87355840859`](https://github.com/happys2333/flowspan/actions/runs/29416495289/job/87355840859):
  success;
- macOS job
  [`87355840884`](https://github.com/happys2333/flowspan/actions/runs/29416495289/job/87355840884):
  success;
- Windows job
  [`87355840965`](https://github.com/happys2333/flowspan/actions/runs/29416495289/job/87355840965):
  success.

CodeQL run
[`29416493790`](https://github.com/happys2333/flowspan/actions/runs/29416493790)
and Analyze C# job
[`87355836393`](https://github.com/happys2333/flowspan/actions/runs/29416493790/job/87355836393)
also completed successfully. Thus both the exact implementation commit and the
commit that records task 7.3c.4 as complete passed the same hosted gates. The
closure commit that adds this second result remains subject to those gates
before its status is treated as final.

## Evidence limits

- The local host is macOS. Windows and Linux platform tests here are managed
  contracts, not native desktop execution; hosted matrix results are still
  required for this candidate.
- Avalonia Headless proves focus, bindings, names, and text state. It is not a
  native NVDA, Narrator, VoiceOver, Orca, keyboard-layout, scaling, or physical
  display test.
- No physical LAN, two-device, arbitrary native application, process-state,
  or Remote Window migration was exercised.
- The simulator result remains Handoff-only. The new Desktop flow performs a
  purpose-scoped inventory query but no destructive Replace.
- Replace receipts/recovery history, startup recovery presentation, target-local
  visible undo, native accessibility evidence, and destructive endpoint
  composition remain open. Therefore task 7.3c, parent task 7.3, the Replace
  release criterion, and v1 remain incomplete.

This evidence completes only task 7.3c.4. Task 7.3c, its parent 7.3, and the v1
criterion “Replace offers an honest undo capsule or blocks before destructive
work” remain unchecked.
