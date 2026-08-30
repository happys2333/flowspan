# Final Admission authenticated-disconnect checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`7be177bb010c55ba44c852a851b60c3ba843d9d7`

Final hosted evidence tree: pending an exact `7be177b` workflow execution.

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 35th production-composed managed tracer execution. Its
fault is independent authenticated transport loss at the exact final-Admission
side-effect window. It therefore advances only AD Disconnect from Missing to
Partial. The row also crosses host post-publication revalidation and terminal
cleanup, but it is not an HC-origin disconnect injection and does not complete
the CL disconnect matrix. HC Disconnect remains Missing, CL Disconnect remains
Partial, and every other cell is unchanged.

`AdFinalAdmissionAuthenticatedDisconnectFailsClosedAndDrainsBothNodes` shares
the real authenticated protocol-1.7 loopback, bilateral verified `FSM1`,
prepared renderer, controller, participant endpoint, and final-Admission hook
with the side-effect-then-throw and authority-revoke rows. The participant has
already committed and published its exact Admission as `Applied` or
`AlreadyApplied`, but the host has not yet performed post-publication fact/
protection revalidation or opened `Admission.TryOpen()`.

At the boundary, capture has started exactly once and its initial pre-Admission
frame has been disposed. The hook deliberately emits a second frame and proves
that owner is also disposed exactly once with zero media send or participant
render; input remains empty. The authenticated generation is current. The hook
then starts the real `participantConnection.DisposeAsync()` to inject transport
loss independently of Trust or Capability mutation.

The hook does not await full connection disposal. Instead, it waits for a
barrier published only after the real host revocation callback has returned.
That proves the invalidation callback has run before assertions without folding
the entire disconnect/session teardown into the hook. At the barrier, the old
host generation is non-current and cannot be reacquired. The host Trust record,
exact peer fingerprint, and sole `mirror.view` grant remain unchanged both at
the boundary and after cleanup.

Host Start fails closed with exact bounded
`authenticated_connection_stale`, no inner exception, and no fingerprint or
dependency payload. Capture and input receive local Emergency Stop; the frame
gate never opens; and media send, render, and input remain zero despite the
participant's exact Admission commit. Outside the hook, the test joins full
participant connection disposal and session completion, then proves the
controller, capture/input/session, renderer, protection, permission observer,
Emergency Stop, media sessions, route, directory, handler, channel, connection,
control, and host-generation owners drain across both managed nodes. The exact
source lease remains current.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~AdFinalAdmission'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~AdFinalAdmission'
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~AdFinalAdmissionAuthenticatedDisconnectFailsClosedAndDrainsBothNodes' || exit 1; done
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~AdFinalAdmissionAuthenticatedDisconnectFailsClosedAndDrainsBothNodes' || exit 1; done
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

Final local results at exact implementation commit `7be177b`:

- focused final-Admission rows: `3/3` in Debug and Release;
- ten fresh authenticated-disconnect processes per configuration: `10/10` in
  Debug and `10/10` in Release;
- production-composed managed tracer: `35/35` in Debug and Release;
- Desktop: `712/712` in Debug and Release;
- solution tests: `2576/2576` in Debug and Release;
- solution builds: zero warnings and zero errors;
- format verification and `git diff --check`: passed; and
- self-review plus independent strict review: 0 P0, 0 P1, and 0 P2 findings.

## Hosted exact-SHA evidence

Pending successful Windows, macOS, Linux, Secret Scan, CodeQL, and reproducible
unsigned-package jobs for exact commit
`7be177bb010c55ba44c852a851b60c3ba843d9d7`. CI `33301715578` and CodeQL
`33301715584` target documentation-only commit `c13acc5`, not `7be177b`, and are
explicitly not evidence for this checkpoint.

## Explicit limitations

This is same-host managed loopback and contract evidence on macOS. It does not
instantiate native capture/input/protection/permission/Emergency Stop APIs, a
physical Device pair, packaged accessibility, signing, notarization, or release
acceptance. A future portable hosted run will remain managed evidence; it will
not become native or physical proof.

The scenario covers one authenticated disconnect after participant final-
Admission commit but before host frame-gate open. It does not complete other
Admission buffering/send/commit disconnect phases, disconnect-plus-cleanup
faults, every host post-Ready boundary, or native non-cooperative teardown. AD
Disconnect remains Partial; HC Disconnect remains Missing; CL Disconnect remains
Partial. Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance,
`CreateProduction()`, every native/physical/signing/notarization/release gate,
and the long-term Goal remain open.
