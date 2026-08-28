# Managed Remote Window Production-Path Tracer Evidence - 2026-08-28

## Evidence boundary

Classification: **local exact-commit macOS managed same-host production-path
tracer evidence**.

Branch: `codex/v1-foundation`

Implementation commit: `7255f048f768426a1c898888af1744b3d9b83bec`,
based on `2d10e8a8b344ea16918eca241e8371a495d21723`.

This local evidence is bound to the exact implementation content above, but not
to a hosted CI run. It exercises real production managed components over
loopback, but does not prove native platform APIs, physical Devices, packaged
behavior, signing, notarization, or release readiness.

## Proven slices

`DesktopRemoteWindowManagedTwoNodeTracerTests` contains four authenticated
managed loopback paths:

- `DriverEligibleWindowTraversesManagedTwoNodeProductionPathAndCleansUp` uses
  protocol 1.7, the production `FlowspanTcpInboundListener`, authenticated
  control, a verified endpoint, `FSM1` media attachment, Prepare/Ready, final
  Admission, BGRA-to-JPEG capture, encrypted media, decode, and rendering. It
  then exercises Driver acquisition, exact input, Emergency Stop, and explicit
  teardown, ending with control, directory, media-budget, and owner cleanup.
- `ReverseOnlyMirrorGrantCannotPrepareOrStartCapture` proves the successful
  direction needs only the host-to-participant Mirror grant. Reversing that
  grant fails closed with `mirror_capability_denied`; capture and rendering
  remain at zero.
- `TerminalControlLossTerminatesActiveHostSession` disconnects the participant's
  authenticated control transport after the session is active and a frame has
  rendered. Without an explicit coordinator Stop or Dispose, revocation drives
  the coordinator snapshot to null and clears Emergency Stop, capture, input,
  session, control route, media budget and directory, renderer, protection,
  permission observer, registration, and connection-lease owners.
  `TerminalFailure` remains null.
- The parameterized active-session path also downgrades the host's current grant
  from `mirror.view` to `activity.offer`. The production Trust coordinator
  persists the downgrade, drains the now-ineligible authenticated session, and
  triggers the same complete host cleanup without an explicit coordinator
  Stop/Dispose.

These are success, reversed-grant negative, terminal disconnect cleanup, and
same-session capability-revocation cleanup slices. They are not the complete
boundary fault matrix required by the test strategy.

## Local verification

Environment:

```text
Host: macOS, Apple Silicon, Asia/Hong_Kong
OS: macOS 26.6.2 (25G83), arm64
.NET SDK: 10.0.301
Branch: codex/v1-foundation
Implementation commit: 7255f048f768426a1c898888af1744b3d9b83bec
Verification date: 2026-08-28
```

Commands:

```sh
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --no-restore --configuration Debug \
  --filter FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --no-restore --configuration Release \
  --filter FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests

# Run five times after the Debug build above.
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --no-restore --no-build --configuration Debug \
  --filter FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests

dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
```

Observed results:

- the focused Debug run passed `4/4` in approximately 230 ms;
- the focused Release run passed `4/4` in approximately 201 ms;
- five additional focused Debug processes passed `4/4` in approximately
  176 ms, 178 ms, 164 ms, 165 ms, and 165 ms;
- formatter, diff, and trailing-whitespace checks passed for the tracer test;
- the direct/transitive package audit reported no known vulnerable dependency in
  any solution project;
- the Debug and Release solution builds each completed with 0 warnings and 0
  errors;
- the complete Debug and Release solutions each passed `2190/2190` across 12 test
  projects, with 0 failed and 0 skipped, including Desktop `526/526` and
  Transport `677/677`;
- Desktop composition validation passed only in explicit TEST MODE;
- the deterministic simulator reported protocol 1.7, source preserved, target
  resumed, and atomic Swap committed; and
- no Windows or Linux tracer execution was performed for this evidence.

The final Release test and executable checks used the immediately preceding
Release build with `--no-build`; all commands used `--no-restore`. They prove the
checked exact-implementation artifacts, not a clean restore or hosted CI run.
`git diff --check` does not inspect untracked files; the new tracer source and
this evidence were checked separately for trailing whitespace.

## Security relevance

- **T05:** complementary one-way success and reversed-grant denial demonstrate
  peer-relative Mirror direction without inventing a reciprocal capability;
  same-session grant downgrade drains the active session.
- **T06:** capture remains closed before Prepare/Ready and attachment complete;
  media/rendering remain closed until final Admission; Driver input and local
  Emergency Stop are exercised.
- **T08:** the success path carries `FSM1` and encrypted media through the real
  production listener and decodes JPEG at the participant.
- **T13:** terminal authenticated-control disconnect automatically converges
  active host ownership, route, directory, budget, renderer, and leases to zero
  without a terminal cleanup failure.
- **T14:** Ready and attachment do not render; final Admission opens rendering,
  and Emergency Stop does not wait for network acknowledgement.
- **T15:** the tracer uses protocol 1.7. Existing protocol tests cover downgrade
  behavior; this evidence adds no physical or packaged downgrade result.

## Explicit non-evidence and remaining gates

The test strategy requires reject, throw, cancel, timeout, revoke, disconnect,
and cleanup-fault coverage at every applicable boundary. This evidence covers
only the four slices above and does not establish that complete matrix.

Tasks 5, 5.5a, and 5.5 remain open, as does the long-term Flowspan Goal.
`CreateProduction()` must continue to report Remote Window unavailable; this
document is not evidence that production Remote Window is available.

There is no evidence here for Windows or Linux tracer execution; Windows,
macOS, or Linux native capture/input/protection APIs; physical two-Device
operation; signed or notarized packages; package lifecycle behavior; or full
release acceptance. Those gates require exact-tree CI and platform or physical
evidence without extrapolating from this same-host managed loopback run.
