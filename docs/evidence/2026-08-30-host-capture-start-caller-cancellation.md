# Host capture-start caller-cancellation checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`0f26c26e93c0af6013372245ba448fd839037a1c`

Final hosted evidence tree: pending an exact `0f26c26` workflow execution.

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 38th production-composed managed tracer execution. Its
fault is exact caller cancellation while capture Start still owns HC. It
therefore advances only HC Cancel from Missing to Partial. CL Cancel remains
Partial, and every other matrix cell is unchanged.

`HcCallerCancellationAfterCaptureSideEffectFailsClosedAndDrainsBothNodes`
shares the capture-start runner with the authenticated-disconnect and authority-
revoke rows. A dedicated caller `CancellationTokenSource` is passed only to
`DesktopRemoteWindowHostCoordinator.StartAsync`; a separate 20-second harness
token bounds network setup, observation, and cleanup joins. The two tokens are
never treated as interchangeable evidence.

Ready has arrived, exact bilateral `FSM1` sessions are attached, and capture has
emitted its pre-Admission frame. The frame owner is disposed exactly once and the
hook runs before capture Start returns. Admission publication, media send,
participant render, and input are all zero. The hook synchronously cancels the
dedicated caller token.

Cancellation is not authority revocation. At the hook boundary, the
authenticated connection remains current; an exact generation probe acquires
the same authenticated generation and is immediately disposed. Trust, the exact
peer fingerprint, and the sole `mirror.view` grant remain unchanged.

Before rethrowing an `OperationCanceledException` carrying the exact caller
token, Host Start awaits its owned ordinary Stop, fail-close, connection
disposal, and cleanup. The test then observes that exception, joins participant
session completion, and verifies bilateral drain. No Admission or frame
authority opens, and the controller, capture/input/session,
renderer, protection, permission observer, Emergency Stop registration, media
sessions, route, directory, handler, channel, connection, control, and host-
generation owners drain across both nodes. The exact source lease remains
current.

No production change was required for this row.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~HcCallerCancellationAfterCaptureSideEffectFailsClosedAndDrainsBothNodes'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~HcCallerCancellationAfterCaptureSideEffectFailsClosedAndDrainsBothNodes'
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~HcCallerCancellationAfterCaptureSideEffectFailsClosedAndDrainsBothNodes' || exit 1; done
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~HcCallerCancellationAfterCaptureSideEffectFailsClosedAndDrainsBothNodes' || exit 1; done
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
```

Final local results at exact commit `0f26c26`:

- focused HC caller-cancellation row: `1/1` in Debug and Release;
- ten fresh focused processes per configuration: `10/10` in Debug and `10/10`
  in Release;
- production-composed managed tracer: `38/38` in Debug and Release;
- Desktop: `715/715` in Debug and Release;
- solution tests: `2579/2579` in Debug and Release;
- solution builds: zero warnings and zero errors;
- format verification and `git diff --check`: passed; and
- self-review plus independent strict review: 0 P0, 0 P1, and 0 P2 findings.

## Hosted exact-SHA evidence

Pending successful Windows, macOS, Linux, Secret Scan, CodeQL, and reproducible
unsigned-package jobs for exact commit
`0f26c26e93c0af6013372245ba448fd839037a1c`. CI `33304022418` and CodeQL
`33304022374` target the preceding 37-case documentation tree `9ca4b2c`, not
this caller-cancellation row.

## Explicit limitations

This is same-host managed loopback and contract evidence on macOS. It does not
instantiate native capture/input/protection/permission/Emergency Stop APIs, a
physical Device pair, packaged accessibility, signing, notarization, or release
acceptance. A future portable hosted run will remain managed evidence; it will
not become native or physical proof.

The scenario covers one exact caller cancellation after Ready and attachment
while capture Start owns HC. It does not complete every HC boundary, other
cancellation phases, cancellation-plus-cleanup faults, foreign/tokenless
cancellation, or native non-cooperative teardown. HC Cancel remains Partial and
CL Cancel remains Partial. Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance,
`CreateProduction()`, every native/physical/signing/notarization/release gate,
and the long-term Goal remain open.
