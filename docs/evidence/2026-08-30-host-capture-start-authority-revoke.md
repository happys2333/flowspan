# Host capture-start authority-revoke checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`62e9372aef378e8c085ccf79502104f63ae8aa76`

First hosted documentation tree:
`9ca4b2c5665cc7ffd462a1a59b8314388f16bc58`

Final hosted evidence tree: pending a post-`7239448` exact workflow execution.

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 37th production-composed managed tracer execution. Its
fault is a real host authority revoke while capture Start still owns HC. It
therefore advances only HC Revoke from Missing to Partial. HC, AD, and CL
Disconnect remain Partial, and every other matrix cell is unchanged.

`HcAuthorityRevokeDuringCaptureStartFailsClosedAndDrainsBothNodes` shares the
capture-start runner with the authenticated-disconnect row. Ready has arrived,
exact bilateral `FSM1` media sessions are attached, and capture has emitted its
pre-Admission frame. That frame owner is disposed exactly once and the hook runs
before capture Start returns. Admission publication, media send, participant
render, and input are all zero.

The hook calls real fingerprint-bound `hostTrust.UpdateCapabilitiesAsync` with
`CapabilityGrant.None`. The mutation returns `Applied` and reaches the real host
revocation callback barrier. The current Trust record and exact peer fingerprint
remain present, while `mirror.view` is absent and the Capability set is empty.
The old authenticated generation is non-current and cannot be reacquired.

After the hook returns, the `fe0be79` post-Start host-fact revalidation preserves
the already-linearized Connection cause. Host Start exposes exact bounded
`authenticated_connection_stale` with no inner exception, fingerprint, or
dependency payload. No Admission is published and the frame gate never opens.
Capture/input Emergency Stop locally; full session completion is joined; and the
controller, capture/input/session, renderer, protection, permission observer,
Emergency Stop, media sessions, route, directory, handler, channel, connection,
control, and host-generation owners drain across both nodes. The exact source
lease remains current.

No additional production change was required. The one-line revalidation added
at `fe0be79` for capture-start authenticated disconnect is sufficient to retain
the causal result for this authority-revoke sibling.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~HcAuthorityRevokeDuringCaptureStartFailsClosedAndDrainsBothNodes'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~HcAuthorityRevokeDuringCaptureStartFailsClosedAndDrainsBothNodes'
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~HcAuthorityRevokeDuringCaptureStartFailsClosedAndDrainsBothNodes' || exit 1; done
for run in {1..10}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~HcAuthorityRevokeDuringCaptureStartFailsClosedAndDrainsBothNodes' || exit 1; done
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

Final local results at exact commit `62e9372`:

- focused HC authority-revoke row: `1/1` in Debug and Release;
- ten fresh focused processes per configuration: `10/10` in Debug and `10/10`
  in Release;
- production-composed managed tracer: `37/37` in Debug and Release;
- Desktop: `714/714` in Debug and Release;
- solution tests: `2578/2578` in Debug and Release;
- solution builds: zero warnings and zero errors;
- format verification and `git diff --check`: passed; and
- self-review plus independent strict review: 0 P0, 0 P1, and 0 P2 findings.

## Hosted exact-SHA evidence

[CI run `33304022418`](https://github.com/happys2333/flowspan/actions/runs/33304022418),
run number 214 attempt 1, is **failure evidence**, not a successful hosted
checkpoint, for exact documentation tree `9ca4b2c` containing this row.

The authority-revoke tracer itself was not the reported failure. Only macOS job
`99237248671` failed, in the unrelated local-pairing test
`DisposeRejectsAndStopsCancellationIgnoringLateEnableSession`: it expected
`OperationCanceledException` but observed `ObjectDisposedException`. Ubuntu job
`99237248699`, Windows job `99237248779`, and Secret Scan job `99237248763`
succeeded. Package matrix job `99237778338` was skipped, so no package result is
claimed from this run.

[CodeQL run `33304022374`](https://github.com/happys2333/flowspan/actions/runs/33304022374),
run number 214 attempt 1, independently completed with `success`. Job
`99237248324` produced analysis ID `1693740950` and SARIF ID
`7eace976-a455-11f1-9f4a-a44bc3e1e004`; service warning/error text is empty and
the analysis reports 52 rules with 0 results. This does not convert failed CI
into successful evidence.

Production fix `72394484e9fd0fd556497641f1ac5d79afe80bce` restores
deterministic lifetime-cancellation precedence for that unrelated pairing race.
Final hosted evidence for the authority row remains pending a later exact tree
containing both fixes and these records. Exact `a0c9648` CI `33303210427` and
CodeQL `33303210391` still prove only the preceding 36-case tree.

## Explicit limitations

This is same-host managed loopback and contract evidence on macOS. It does not
instantiate native capture/input/protection/permission/Emergency Stop APIs, a
physical Device pair, packaged accessibility, signing, notarization, or release
acceptance. A future portable hosted run will remain managed evidence; it will
not become native or physical proof.

The scenario covers one authority revoke after Ready and attachment while
capture Start owns HC. It does not complete every HC boundary, other revoke
phases, revoke-plus-cleanup faults, or native non-cooperative teardown. HC Revoke
remains Partial. HC, AD, and CL Disconnect remain Partial. Tasks 5, 5.5a, and
5.5, aggregate H0/H1 acceptance, `CreateProduction()`, every native/physical/
signing/notarization/release gate, and the long-term Goal remain open.
