# Portable Remote Window Control-Plane Evidence - 2026-08-08

## Evidence boundary

This record closes the local automated evidence for the portable portions of
Remote Window tasks 2 and 3: one-Activity live session control, current
Capability checks, monotonic Driver authority, bounded remote input, protection
pause/resume convergence, local Emergency Stop, and payload-free failure
results. It does not implement or prove an authenticated Remote Window wire
protocol, media transport, Desktop sharing workflow, native capture/input or
protection APIs, physical emergency action, permissions, accessibility, or
two-device behavior.

Branch: `codex/v1-foundation`

The verified worktree was based on
`82f34cdbfe602efeffe4007ac38fb58db4b028b7`. Local worktree results are not
exact-commit hosted evidence. The implementation commit and its Windows,
macOS, Ubuntu, Secret Scan, CodeQL, and downloaded-artifact results must be
added only after that commit is pushed and the matching workflows finish.

## Implemented contract

- One controller owns one active local Activity and composes capture, remote
  input, current authorization, local sharing-session, `MirrorSession`, and
  `DriverLease` boundaries without persisting live authority.
- View admission requires current `mirror.view`; Driver admission, transfer,
  and input require one current immutable grant containing both `mirror.view`
  and `mirror.drive`. Input also requires the exact unexpired Driver epoch and
  a fresh Safe protection observation.
- Sessions admit at most 16 participants. Input batches contain 1-64 closed,
  defensively copied HID key, normalized pointer, button, or bounded scroll
  events. Results and display strings contain no raw input or Activity payload.
- Every protection observation advances an internal monotonic revision. Only a
  current revision may publish Active or confirmed Paused state. Re-entrant and
  concurrent supersession, late capture start, and local gate failures converge
  to the latest observation; eight unstable attempts fail closed. A partial
  resume failure re-pauses both gates before returning the original failure.
- Emergency Stop latches and revokes Driver authority before local boundaries,
  runs capture/input/session gates despite individual failures, has no peer ACK
  boundary, wins pending start/input/protection races, and is re-applied if a
  stale protection boundary ran after the stop.

## Local environment and commands

```text
Host: macOS 26.6.1 (build 25G76), Apple Silicon, Asia/Shanghai
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
RID: osx-arm64
Verification time: 2026-08-08T08:27:43Z
```

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore \
  --logger "trx;LogFilePrefix=macOS-local" \
  --results-directory <fresh-directory>
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable \
  --include-transitive --no-restore
git diff --check
```

## Local results

- Locked restore and format verification passed.
- Release build passed for all 26 projects with 0 warnings and 0 errors.
- Structured parsing of 12 fresh TRX files reported 1257 total, 1257 executed,
  1257 passed, 0 failed, 0 error, 0 timeout, 0 aborted, 0 inconclusive, and 0
  not executed.
- Per-project results were Desktop 201, Domain 60, Integration 338, shared
  Platform 69, Linux contracts 27, macOS contracts 25, Windows contracts 27,
  Protocol 47, Release 71, Security 123, mDNS Transport 14, and Transport 255.
- The controller suite includes deterministic public-interface tests for
  same-thread re-entry, a blocked cross-thread Safe/unsafe race, late Start
  convergence, bounded 64-transition protection churn, partial-resume
  reclosure, stale gate failure, and Emergency Stop after a stale resume.
  Sixteen fixed seeds still execute 48 mixed control transitions and retry
  every retired Device/epoch through the public input API.
- The five protection supersession/churn/Emergency regression tests passed in
  20/20 fresh test processes (100 focused test executions) without a failure.
- Desktop composition printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`
- The deterministic simulator printed protocol `1.4`, source preserved,
  target resumed, and atomic swap committed, with a redacted receipt.
- NuGet reported no known vulnerable direct or transitive package in any of the
  26 projects. `git diff --check` passed.
- Fresh TRX files were parsed from
  `/var/folders/rf/dgnm75850p1_wm033h90780c0000gn/T/flowspan-remote-window-trx-XXXXXX.U5fENsiaS1`.
  This volatile local path is diagnostic evidence, not a retained release
  artifact.
- No local `gitleaks` executable was installed. No local secret-scan result is
  claimed; the pinned hosted Secret Scan remains mandatory.

The platform-named contract assemblies compiling and passing on this macOS host
do not prove Windows, macOS, or Linux native APIs.

## Hosted evidence pending

- Push the implementation commit on `codex/v1-foundation`.
- Require the exact SHA to pass all three CI OS jobs, package jobs, Secret Scan,
  and CodeQL.
- Download each TRX and Gitleaks artifact, record artifact IDs and SHA-256
  digests, parse their structured result counters, and confirm the workflow SHA
  before adding hosted evidence.

Until those items are recorded, this document proves only local portable
behavior on the named host. Tasks 4-9 in the Remote Window plan, parent tasks
6.3-6.6, physical task 5.4, accessibility task 7.4, and release tasks 9.3-9.4
remain open.
