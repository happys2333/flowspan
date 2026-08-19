# Desktop Remote Window Workflow Evidence - 2026-08-10

## Evidence status and boundary

Classification: **Local**, **hosted portable contract**, **headless Desktop**,
and **unsigned package**.

Branch: `codex/v1-foundation`

Feature implementation commit:
`a92c0bff6c5d4624ca4d57a352aac9c05c61d1c5`, based on
`3390667830531a9ada2b9a834a912cc85216870f`. Final verified commit:
`e34e73339dbb1c1ccf9de0b047653ddc5d7fbb59`. The final commit adds the
post-semaphore cancellation check needed when pairing admission races disposal
and removes single-core thread-pool scheduling as a timing-test confounder
without relaxing the one-second Emergency Stop assertions. The final local gate
and hosted workflows below both ran against that exact final commit.

This record covers the Desktop Remote Window workflow and the portable safety
boundaries it composes. It does not prove a native screen was captured, native
input was injected or stopped, a protected surface was detected, an operating-
system permission prompt behaved correctly, a physical emergency action ran, or
two physical Devices communicated over a LAN.

## Implemented workflow contract

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
Final verified commit: e34e73339dbb1c1ccf9de0b047653ddc5d7fbb59
```

Commands executed against the exact final verified commit:

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
  artifacts/test-results/2026-08-10-remote-window-candidate-6
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

Passed for exact commit `e34e73339dbb1c1ccf9de0b047653ddc5d7fbb59`:

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
  `artifacts/test-results/2026-08-10-remote-window-candidate-6`. This ignored
  local path is diagnostic evidence, not a committed release artifact.

This proves the named portable/headless implementation on the local macOS host.
It does not provide matching native-platform evidence.

## Hosted exact-commit evidence

Final commit `e34e73339dbb1c1ccf9de0b047653ddc5d7fbb59` passed
[CI run `31346175920`](https://github.com/happys2333/flowspan/actions/runs/31346175920):

- Ubuntu test job [`93328435900`](https://github.com/happys2333/flowspan/actions/runs/31346175920/job/93328435900);
- macOS test job [`93328435916`](https://github.com/happys2333/flowspan/actions/runs/31346175920/job/93328435916);
- Windows test job [`93328435918`](https://github.com/happys2333/flowspan/actions/runs/31346175920/job/93328435918);
- Secret Scan job [`93328435891`](https://github.com/happys2333/flowspan/actions/runs/31346175920/job/93328435891);
- `linux-x64` package job [`93328815637`](https://github.com/happys2333/flowspan/actions/runs/31346175920/job/93328815637);
- `osx-arm64` package job [`93328815645`](https://github.com/happys2333/flowspan/actions/runs/31346175920/job/93328815645);
- `win-x64` package job [`93328815635`](https://github.com/happys2333/flowspan/actions/runs/31346175920/job/93328815635).

Every test job restored locked dependencies, verified formatting, built with
warnings as errors, ran all tests, validated Desktop composition in explicit
TEST MODE, ran the protocol-1.5 deterministic simulator, and uploaded TRX
evidence. Every package job verified content-locked tooling, published and
smoke-tested its self-contained target, sealed and verified two reproducible
unsigned outputs, compared them recursively, audited direct/transitive
dependencies, and uploaded the resulting test package.

Downloaded test and Secret Scan artifacts were parsed with XML and JSON
parsers. `Artifact digest` is GitHub's service-computed SHA-256. `Tree SHA-256`
independently hashes every extracted relative path and file digest in sorted
order.

| Artifact | ID | Artifact digest | Tree SHA-256 | Parsed result |
| --- | ---: | --- | --- | --- |
| Windows TRX | `9047432733` | `c25b28e29fd01e9bb4e6ff47f56f3237d553319284567f92df970ff36457444c` | `1784fbfe504a04c289c50bc3a26566e5df1e5c1e6c43c87b05ae416271a907b8` | 12 files, 1542/1542 passed |
| macOS TRX | `9047424561` | `c34b7cdebfff1dce88b2dfd78efea92a86db41356be6d202a2604a18d768bd83` | `62365cb3528785c6bbbfeda2da8a9b729a51e506443e3b7c7e92d837dfa3e297` | 12 files, 1542/1542 passed |
| Ubuntu TRX | `9047429439` | `6b0e36529d3a7d8b0e67192735a1806cf285ebc6d979343cda6234992f376c5c` | `a9cbcd4d7a4ee65f1fbc085f70e8b109191271d96a730289c70e691722e2213a` | 12 files, 1542/1542 passed |
| Gitleaks SARIF | `9047394755` | `976f538c38f5a869f539ab6e9b7d7f5a74ba5834502b70229ec8c11a7a67d416` | `49424e51411d8baac254af99c5d621befe644dd8291e9d20e52346d7c0ba7f83` | 208 rules, 0 results |

All three TRX aggregates also reported 0 failed, error, timeout, aborted,
inconclusive, passed-but-aborted, not-runnable, not-executed, disconnected,
warning, completed, in-progress, or pending tests. The 36 files total 4626/4626
passes.

The three downloaded package directories independently passed the repository's
`Flowspan.Release verify` command and all 15 `SHA256SUMS` entries. Their SLSA
provenance binds version `0.1.126`, the exact final commit, CI run attempt 1,
and the named builder. Each SBOM contains 38 packages and 38 relationships.

| Package | ID | Artifact digest | Tree SHA-256 | Inner archive bytes / SHA-256 |
| --- | ---: | --- | --- | --- |
| `win-x64` | `9047459439` | `bd8f95cdc3e191a4106eb5ae2ba16e3ba158b9bee94379ecf98cba2c9d3729b8` | `ee81a4a2fc6e21140dcfcf7259d60c05d1f16978f6d25d48dc3b5cc90803e992` | 43,820,048 / `c840da7a2943f57c80aec77483cbbe80366b1454d81f572965369e8abd2bef37` |
| `linux-x64` | `9047451443` | `b1461d7f94ccf62e6ca7f8a1e155db912d611f2a430089c1aeffd446fbe25f55` | `18b0a9260653deb9d5b063dbd2b99484ad34bbca8fd9f952524c068e9dd685ce` | 41,834,599 / `fab2f074f8815d19e4d1ddf2d76f019ad86ebb8c69bb94a5d626668f7a61a540` |
| `osx-arm64` | `9047459750` | `c217725c0fb8c3425b82bc1de43662eda4d4bf5c5c09eabd8d21754481da6df0` | `07a5a7233aac6fe72ae121da20f0c6b27adc11c15ed82366d17b3545ab96ba55` | 42,654,863 / `52f943d70bcd1e4b9eb7475296081b3a922724dd196a063cbbc1224ced29706f` |

All packages are explicitly `unsigned-test-artifact`; their license reports
remain `reviewRequired=true`. They are not release-signed installers.

[CodeQL run `31346175815`](https://github.com/happys2333/flowspan/actions/runs/31346175815),
job [`93328435496`](https://github.com/happys2333/flowspan/actions/runs/31346175815/job/93328435496),
also passed for the exact final commit. CodeQL 2.26.2 analysis `1593081849`
evaluated 52 rules, reported 0 results, and the branch had 0 open alerts.

These hosted results prove portable build and contract behavior plus
reproducible unsigned packaging on the named runner images. Platform-named
contract suites and hosted smoke tests do not prove native capture, input,
protection, permission prompts, or physical-device behavior.

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
release criteria remain open where they require native, physical, load,
accessibility, signed-package, or real-machine evidence.
