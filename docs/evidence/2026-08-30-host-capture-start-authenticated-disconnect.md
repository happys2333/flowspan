# Host capture-start authenticated-disconnect checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation and production-fix commit:
`fe0be79e0accbbb0cd4eef27b62e12620a18eccf`

Final hosted evidence tree: pending an exact `fe0be79` workflow execution.

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 36th production-composed managed tracer execution. Its
fault is authenticated transport loss while the host capture `StartAsync` call
still owns the HC boundary. It therefore advances only HC Disconnect from
Missing to Partial. AD Disconnect and CL Disconnect remain Partial, and every
other matrix cell is unchanged.

`HcAuthenticatedControlDisconnectDuringCaptureStartFailsClosedAndDrainsBothNodes`
uses real authenticated protocol-1.7 loopback. The participant has returned
Ready, and exact bilateral `FSM1` sessions are attached with the same binding.
The host begins capture before any final Admission publication. Capture emits
its required pre-Admission frame, waits until that owner is disposed exactly
once, and enters the test hook without returning from capture Start.

At that boundary, capture Start count is one, the authenticated host generation
is current, and Admission publication, media send, participant render, and input
are all zero. The hook starts real `participantConnection.DisposeAsync()` and
waits for a barrier published after the real host revocation callback returns.
It does not await full connection disposal. The old generation is then non-
current and cannot be reacquired, while Trust, the exact peer fingerprint, and
the sole `mirror.view` grant remain unchanged.

After the hook returns, capture Start finishes. Host Start must preserve the
causal loss of current authenticated Connection authority rather than allowing
a later local controller outcome to overwrite it. The exact public result is
`authenticated_connection_stale`, with no inner exception, peer fingerprint, or
dependency payload. No Admission is published, the frame gate never opens, and
capture/input receive local Emergency Stop. Outside the hook, full participant
disconnect and session completion are joined before the controller, capture/
input/session, renderer, protection, permission observer, Emergency Stop, media
sessions, route, directory, handler, channel, connection, control, and host-
generation owners drain across both managed nodes. The exact source lease
remains current.

## TDD and production fix

The first exact production-composed row was RED:

- expected: `authenticated_connection_stale`;
- actual: `emergency_stop_won_start_race`.

The Preparation reservation was already promoted and its temporary Connection
registration released. The disconnect instead made the authenticated Connection
non-current and invoked its retained live revocation callback, but after
controller/capture Start returned the coordinator projected the later local
Start result first. The minimal production change adds one
`ValidateCurrentHostFacts` call immediately after controller `StartAsync`
returns and before the returned Start reason is projected. That revalidation
preserves the already-linearized authenticated disconnect as the causal failure.
The same row is GREEN after the change.

The existing post-promotion media-mutation tracer expectation changes from
`session_not_idle` to `authenticated_connection_stale` for the same reason: its
same-generation `RequestControlStop` makes the Connection non-current and
invokes the retained live callback. Post-Start revalidation now reports that
causal fact instead of the later controller surface state.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~HcAuthenticatedControlDisconnectDuringCaptureStartFailsClosedAndDrainsBothNodes'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~HcAuthenticatedControlDisconnectDuringCaptureStartFailsClosedAndDrainsBothNodes'
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~HcAuthenticatedControlDisconnectDuringCaptureStartFailsClosedAndDrainsBothNodes' || exit 1; done
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~HcAuthenticatedControlDisconnectDuringCaptureStartFailsClosedAndDrainsBothNodes' || exit 1; done
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

Final local results at exact commit `fe0be79`:

- focused HC disconnect row: `1/1` in Debug and Release;
- ten fresh focused processes per configuration: `10/10` in Debug and `10/10`
  in Release;
- production-composed managed tracer: `36/36` in Debug and Release;
- Desktop: `713/713` in Debug and Release;
- solution tests: `2577/2577` in Debug and Release;
- solution builds: zero warnings and zero errors;
- format verification and `git diff --check`: passed; and
- self-review plus two independent strict reviews: 0 P0, 0 P1, and 0 P2
  findings.

## Hosted exact-SHA evidence

Pending successful Windows, macOS, Linux, Secret Scan, CodeQL, and reproducible
unsigned-package jobs for exact commit
`fe0be79e0accbbb0cd4eef27b62e12620a18eccf`. CI `33302708813` and CodeQL
`33302708801` target documentation-only tree `17a3401`, not `fe0be79`, and are
explicitly not evidence for this checkpoint.

## Explicit limitations

This is same-host managed loopback and contract evidence on macOS. It does not
instantiate native capture/input/protection/permission/Emergency Stop APIs, a
physical Device pair, packaged accessibility, signing, notarization, or release
acceptance. A future portable hosted run will remain managed evidence; it will
not become native or physical proof.

The scenario covers one authenticated disconnect after Ready and attachment
while capture Start owns the host commit boundary. It does not complete every HC
boundary, other disconnect phases, disconnect-plus-cleanup faults, or native
non-cooperative teardown. HC Disconnect remains Partial; AD Disconnect and CL
Disconnect remain Partial. Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance,
`CreateProduction()`, every native/physical/signing/notarization/release gate,
and the long-term Goal remain open.
