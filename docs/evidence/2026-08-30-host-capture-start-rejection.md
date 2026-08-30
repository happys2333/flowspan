# Host capture-start rejection checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`858acb2c28321ed8603646227d8834eef318405a`

Immediate pairing-race fix dependency:
`72394484e9fd0fd556497641f1ac5d79afe80bce`

Final hosted evidence tree: pending an exact post-`858acb2` workflow execution.

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 39th production-composed managed tracer execution. Its
fault is a bounded negative returned by capture Start while HC owns the host
commit boundary. It therefore advances only HC Reject from Missing to Partial.
HC Reject, Cancel, Revoke, and Disconnect are now all Partial. Every other matrix
cell is unchanged.

`HcCaptureStartRejectAfterFrameSideEffectFailsClosedAndDrainsBothNodes` reuses
the capture-start runner after Ready and exact bilateral `FSM1` attachment.
Capture emits its pre-Admission frame and observes that owner disposed exactly
once. The hook runs while capture Start is still current, probes the exact same
authenticated generation, and immediately disposes that probe. Trust, peer
fingerprint, sole `mirror.view`, and transport remain unchanged.

After the hook, capture returns
`LocalBoundaryResult.Failed("capture_start_failed")`. Host Start exposes the
exact bounded `capture_start_failed` reason with no inner exception, fingerprint,
or dependency payload. Admission publication, media send, participant render,
and input remain zero. The frame gate never opens.

Production performs owned fail-close and connection disposal. Capture and input
use their ordinary Stop boundaries; the exact test asserts both Emergency Stop
counts remain zero. The controller, capture/input/session, renderer, protection,
permission observer, Emergency Stop registration, media sessions, route,
directory, handler, channel, connection, control, and host-generation owners
drain across both nodes. The exact source lease remains current.

No production change was required for this row.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~HcCaptureStartRejectAfterFrameSideEffectFailsClosedAndDrainsBothNodes'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~HcCaptureStartRejectAfterFrameSideEffectFailsClosedAndDrainsBothNodes'
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~HcCaptureStartRejectAfterFrameSideEffectFailsClosedAndDrainsBothNodes' || exit 1; done
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~HcCaptureStartRejectAfterFrameSideEffectFailsClosedAndDrainsBothNodes' || exit 1; done
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

Final local results through exact commit `858acb2`:

- focused HC rejection row: `1/1` in Debug and Release;
- ten fresh focused processes per configuration: `10/10` in Debug and `10/10`
  in Release;
- production-composed managed tracer: `39/39` in Debug and Release;
- Desktop: `716/716` in Debug and Release;
- combined solution tests: `2580/2580` in Debug and Release;
- solution builds: zero warnings and zero errors;
- format verification and `git diff --check`: passed; and
- self-review plus independent strict review: 0 P0, 0 P1, and 0 P2 findings.

## Hosted exact-SHA evidence

Pending successful Windows, macOS, Linux, Secret Scan, CodeQL, and reproducible
unsigned-package jobs for an exact tree containing `858acb2`, `7239448`, and
these records. CI `33304022418` and CodeQL `33304022374` target the preceding
37-case `9ca4b2c` tree and do not prove this row.

## Explicit limitations

This is same-host managed loopback and contract evidence on macOS. It does not
instantiate native capture/input/protection/permission/Emergency Stop APIs, a
physical Device pair, packaged accessibility, signing, notarization, or release
acceptance. A future portable hosted run will remain managed evidence; it will
not become native or physical proof.

The scenario covers one bounded capture Start rejection after Ready and
attachment. It does not complete every HC negative boundary, other rejection
phases, rejection-plus-cleanup faults, or native non-cooperative teardown. HC
Reject remains Partial; HC Cancel, Revoke, and Disconnect remain Partial. Tasks
5, 5.5a, and 5.5, aggregate H0/H1 acceptance, `CreateProduction()`, every native/
physical/signing/notarization/release gate, and the long-term Goal remain open.
