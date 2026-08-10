# Desktop Remote Window Workflow Candidate Evidence - 2026-08-10

## Evidence status and boundary

Classification: **Local**, **portable contract**, and **headless Desktop**.

Branch: `codex/v1-foundation`

Implementation commit:
`a92c0bff6c5d4624ca4d57a352aac9c05c61d1c5`, based on
`3390667830531a9ada2b9a834a912cc85216870f`. The exact-commit local gate below
ran at that implementation commit with only this evidence record untracked. An
exact-commit hosted workflow result does not yet exist; the hosted section below
therefore remains intentionally open.

This record covers the Desktop Remote Window workflow and the portable safety
boundaries it composes. It does not prove a native screen was captured, native
input was injected or stopped, a protected surface was detected, an operating-
system permission prompt behaved correctly, a physical emergency action ran, or
two physical Devices communicated over a LAN.

## Implemented candidate contract

- The persistent sharing header, detail state, progressive capture/input review,
  local Emergency Stop, local retry-reset, and fallback start surface consume
  bounded view-model state without claiming native adapter success.
- Semantic Activity targets remain purpose-scoped to `activity.receive`.
  Remote Window has its own target inventory and selection: view-only requires a
  current authenticated peer with `mirror.view`; driving requires the same peer
  and one current grant containing both `mirror.view` and `mirror.drive`.
- Changing the next-request driving role refilters that inventory. A target that
  is only view-capable is cleared when the preview changes to driving;
  receive-only and drive-without-view targets are not selectable. Connection or
  Trust withdrawal also clears the selected target before the fallback can
  start. The service still revalidates exact Activity, target, role, Trust,
  Capability, protection, and session state at use time; the picker is not
  authority.
- The elected production reconnect profile, connector eligibility, and shared
  production listener all admit Mirror control alternatives. A `mirror.view`-
  only peer can therefore establish the encrypted idle channel and reach the
  view-only inventory. A `mirror.drive`-only peer may establish that channel but
  reaches neither role-qualified picker. Moving between eligible Activity and
  Mirror grants retains the connection; removing the final eligible control
  grant drains it.
- Successful Trust register, capability-update, and revoke operations publish a
  post-commit change outside the coordinator mutation gate. The Desktop runtime
  uses that signal to refresh and clear a purpose-scoped selection even when a
  different Capability keeps the authenticated connection alive. A throwing
  observer cannot fail the committed mutation or skip later observers.
- The next-request role remains separate from an admitted or active session role.
  Late results cannot relabel a frozen in-flight or active target/role.
- Revoking `mirror.view` removes the peer and its lease authority before the
  local disconnect boundary. An unconfirmed disconnect is retained as bounded,
  peer-scoped pending cleanup; later reconciliation or explicit disconnect
  retries it without restoring the participant. Re-authorizing that peer does
  not bypass the pending cleanup, and active participants plus pending cleanup
  share the fixed 16-slot session budget.
- Successful capture start results that arrive after authoritative inactivity or
  caller cancellation are failed closed. A stale result cannot stop a newer
  replacement session on the same controller, including after that replacement
  ends, and the controller rechecks cancellation even when an injected capture
  boundary ignores its token.
- Service snapshots pass the monotonic reducer before they can update safety
  role or trigger a permission stop. A rejected lower-revision Driver-eligible
  snapshot therefore cannot stop the accepted current view-only session.
- Shell, permission, and teardown projections retain callback self-drain
  exclusion, so a synchronous busy-state observer can request disposal without
  deadlocking its own callback while external disposal still joins complete
  cleanup. Disposal reports an unconfirmed Emergency Stop instead of claiming
  that owned resources were safely stopped.
- Emergency Stop confirmation is accumulated per boundary within the current
  stop/session generation. Repeated and concurrent attempts return that
  accumulated proof without letting a later failed boundary invocation regress
  an earlier confirmation, and never reuse proof for a replacement session.
- The independent Remote Window target list has an accessibility name, bounded
  dimensions, keyboard selection, and separate binding from the semantic target
  list.

## Local focused results

Environment:

```text
Host: macOS 26.6.1 (build 25G76), Apple Silicon, Asia/Shanghai
.NET SDK: 10.0.301
Branch: codex/v1-foundation
Base commit: 3390667830531a9ada2b9a834a912cc85216870f
Verification date: 2026-08-10
```

Commands already executed against this candidate worktree:

```sh
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Release --no-restore --filter \
  "FullyQualifiedName~DesktopActivityRuntimeTests|FullyQualifiedName~DesktopTrustedPeerConnectionsTests|FullyQualifiedName~SystemDesktopLocalPairingNetworkSessionTests|FullyQualifiedName~RemoteWindowWorkspaceViewModelTests|FullyQualifiedName~WorkspaceShellViewModelTests|FullyQualifiedName~DesktopPairingDecisionSourceTests|FullyQualifiedName~LocalPairingViewModelTests|FullyQualifiedName~MainWindowAccessibilityTests"
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore \
  --logger "trx;LogFilePrefix=macOS-local" \
  --results-directory \
  artifacts/test-results/2026-08-10-remote-window-candidate-5
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable \
  --include-transitive --no-restore
git diff --check
```

Observed results:

- The deterministic Desktop regression set passed `249/249`; the complete
  Desktop project later passed `386/386` inside the final solution gate.
- The role-scoped Shell regression directly rejected receive-only and
  drive-without-view targets, cleared a view-only target on Driver upgrade, and
  cleared the restored Driver target after connection and Trust withdrawal.
- Focused production-profile tests exercised both elected outgoing and shared
  inbound paths with Activity and Mirror-only grants. The actual local-network
  factory loopback admitted `mirror.view` into the view-only inventory and kept
  `mirror.drive` alone out of both pickers; Trust and connection-coordinator
  tests covered post-commit refresh, eligible-alternative retention, final-
  alternative drain, and retention of the existing scene-only production peer
  admission.
- Controller tests retained failed peer disconnects for deterministic retry,
  rejected same-peer re-admission until cleanup confirmation, bounded active
  participants plus pending cleanup to 16 shared slots, merged sequential and
  concurrent Emergency Stop confirmations, and proved that cancellation-
  ignoring capture starts are cleaned up. Desktop tests proved that Idle-before-
  success starts fail closed, stale starts cannot stop an active or already-ended
  replacement session, rejected stale Driver snapshots cannot trigger a current
  view-only permission stop, permission busy observers can synchronously dispose,
  and every concurrent disposer joins the same success or failure completion.
- `git diff --check` passed and no `[DEBUG-` instrumentation remains under
  `src/` or `tests/`.

## Final local gate

Passed for the candidate worktree:

- Locked restore and format verification passed.
- All 26 projects built in Release with 0 warnings and 0 errors.
- Structured XML parsing of 12 fresh TRX files reported `1542` total, `1542`
  executed, `1542` passed, and 0 failed, error, timeout, aborted, inconclusive,
  not-runnable, or not-executed tests.
- Per-project results were Desktop 386, Domain 60, Integration 338, shared
  Platform 110, Linux contracts 27, macOS contracts 25, Windows contracts 27,
  Protocol 59, Release 71, Security 125, mDNS Transport 14, and Transport 300.
- Desktop composition printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`
- The deterministic simulator printed protocol `1.5`, source preserved, target
  resumed, and atomic swap committed with a redacted receipt.
- NuGet reported no known vulnerable direct or transitive package in any of the
  26 projects. `git diff --check` passed.
- Fresh TRX files remain in
  `artifacts/test-results/2026-08-10-remote-window-candidate-5`. This ignored
  local path is diagnostic evidence, not a committed release artifact.

This proves the named portable/headless implementation commit on the local
macOS host. It does not provide hosted or matching native-platform evidence.

## Hosted exact-commit evidence

Pending. Windows, macOS, and Ubuntu CI, Secret Scan, CodeQL, and reproducible
unsigned package jobs must pass for the implementation commit. Hosted runner
results remain portable evidence unless a job invokes and verifies the matching
native API.

## Open evidence

- Windows Graphics Capture, SendInput, secure desktop/protected content, and a
  local physical emergency action.
- ScreenCaptureKit, Accessibility/TCC, secure input/protected windows, and a
  local physical emergency action.
- Wayland portal/PipeWire/RemoteDesktop lifecycle, compositor coverage, and
  explicit X11 security degradation.
- Physical two-Device LAN behavior, production codec/rendering, sustained load
  measurements, native screen-reader and focus observation, signed/notarized
  install/upgrade/uninstall, and the complete real-machine acceptance matrix.

Tasks 6-9 in the Remote Window plan, parent native/physical tasks, and the v1
release criteria remain open where they require this evidence.
