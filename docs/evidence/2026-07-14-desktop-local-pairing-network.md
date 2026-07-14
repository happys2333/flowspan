# Evidence: desktop local pairing network, 2026-07-14

Classification: **Local**, **headless UI/contract**, **same-host loopback**, and
**Hosted CI**

Branch: `codex/v1-foundation`

Verified implementation commit:
`e95b1b8023530aeba6e661c99d933c2266d01eb9`

Latest verified delivery head:
`697e87fe8f2320f7d7ed1cb1b1a5019be59f43f2`

## Local environment and commands

```text
OS: macOS 26.5.2 (build 25F84), arm64
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
```

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive
git diff --check
```

Observed results:

- locked restore passed for all 24 projects;
- format verification and the patch whitespace check passed;
- Release build passed with 0 warnings and 0 errors;
- the complete unfiltered suite passed 371 tests with 0 failures and 0 skips,
  including 64 desktop, 90 transport, 86 security, and 22 integration tests;
- the composition validator printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`;
- the deterministic simulator negotiated protocol 1.0, preserved the source,
  resumed the target, and exited successfully;
- NuGet reported no known vulnerable direct or transitive package in any
  project.

Gitleaks 8.30.1 was downloaded to a temporary directory from its official
GitHub release. The Darwin arm64 archive SHA-256 was
`b40ab0ae55c505963e365f271a8d3846efbc170aa17f2607f13df610a9aeb6a5`,
which matched the published checksum. `gitleaks git . --redact` scanned all 39
commits and found no leak. A directory scan of the current source worktree,
excluding `.git` and ignored build/test output directories, also found no leak.

## Hosted CI results

CI run
[29324464484](https://github.com/happys2333/flowspan/actions/runs/29324464484)
completed successfully for the verified implementation commit. Every required
step reported `success` in:

- `Test (ubuntu-latest)` (`87057324051`);
- `Test (macos-latest)` (`87057324067`);
- `Test (windows-latest)` (`87057324095`);
- `Secret scan` (`87057324052`).

Each hosted OS restored all 24 locked projects, verified formatting, built with
warnings as errors, ran all 371 tests with 0 failures and 0 skips, ran the
explicit TEST MODE desktop composition validator, ran the deterministic
simulator, and uploaded test evidence. Each desktop suite reported 64 passes,
each transport suite reported 90 passes, each security suite reported 86
passes, and each integration suite reported 22 passes. The validator printed
its explicit TEST MODE success line on all three jobs; each simulator printed
protocol 1.0, `Source preserved: True`, and `Target resumed: True`.

CodeQL run
[29324464710](https://github.com/happys2333/flowspan/actions/runs/29324464710)
also completed successfully for the same commit. Its `Analyze C#` job
(`87057325760`) restored locked dependencies, built the analyzed source, and
completed analysis successfully.

Both runs were created at `2026-07-14T10:11:39Z`; CI completed at
`2026-07-14T10:13:51Z`, and CodeQL completed at `2026-07-14T10:14:43Z`. Run,
job, step, commit, timestamp, suite-count, validator-output, and simulator-output
evidence was queried with `gh run view` on 2026-07-14.

### Delivery-head revalidation and CI race correction

The first evidence-document commit, `6dd38f33ecb49c2c5b8cb575274c8a7d7d1f7c9f`,
did not pass its complete CI run. Run
[29325387710](https://github.com/happys2333/flowspan/actions/runs/29325387710)
passed Windows, macOS, and secret scan, but Ubuntu job `87060349671` failed one
desktop test with a `NullReferenceException` in
`Avalonia.Headless.HeadlessUnitTestSession.Dispose`. The checked test called
the production window's intentionally asynchronous `Close()` path and then
disposed its headless session without waiting for the window's `Closed` event.
Waiting for `Closed` fixed that real test-lifecycle gap; the product close path
itself did not fail.

Commit `697e87fe8f2320f7d7ed1cb1b1a5019be59f43f2` changed all five headless window
tests to wait for actual asynchronous window closure before releasing their
test session. The originally failing test passed ten consecutive local runs,
the affected class passed 5/5, and the complete local suite passed 371/371.

Revalidation CI run
[29330482945](https://github.com/happys2333/flowspan/actions/runs/29330482945)
then completed successfully for that delivery head:

- `Test (macos-latest)` (`87076960918`);
- `Test (windows-latest)` (`87076960947`);
- `Test (ubuntu-latest)` (`87076961685`);
- `Secret scan` (`87076960910`).

Every OS again passed the complete 371-test suite, explicit TEST MODE desktop
validation, simulator, and artifact upload. Revalidation CodeQL run
[29330482960](https://github.com/happys2333/flowspan/actions/runs/29330482960)
and its `Analyze C#` job (`87076960951`) also completed successfully. Both runs
were created at `2026-07-14T11:53:30Z`; CI completed at
`2026-07-14T11:56:25Z`, and CodeQL completed at `2026-07-14T11:56:35Z`.

A later task 7.2d Ubuntu run reproduced the same disposal exception with the
`Closed` wait present. Inspection of the exact Avalonia 12.1.0 source then
identified an independent race in `HeadlessUnitTestSession.StartNew`: its
worker can publish the session before the outer assignment stores the
dispatcher task, leaving the session's task field null when `Dispose` reads it.
The successful rerun above proved the lifecycle fix and that delivery head, but
did not prove this upstream construction race had been eliminated. Flowspan's
assembly-scoped-session remediation and its replacement hosted evidence are
recorded in `2026-07-14-desktop-trusted-reconnect.md`.

## What this proves

- Production shell launch does not bind, browse, or advertise. Local pairing
  starts only after the enabled command is available and explicitly invoked.
- Production enable borrows the same protected `DeviceIdentity`, desktop SAS
  decision source, and persistent `TrustSessionCoordinator` used elsewhere in
  the shell, then owns one bound TCP endpoint, unified inbound listener,
  minimized signed advertisement, DNS-SD browser, and candidate projection.
- Unpaired discovery observations are presented only as Unverified Pairing
  Candidates. Malformed, expired, future-skewed, self, port-inconsistent,
  loopback, multicast, and unusable endpoint data does not enter the snapshot.
  Removal, expiry, canonical ordering, and authoritative Trust
  reclassification have deterministic tests.
- Before any outgoing SAS prompt, the discovery-bound decision gate requires
  the transcript-authenticated Device ID and fingerprint to match the pinned
  candidate and verifies its offer signature and lifetime with that
  authenticated public key. Forgery, substitution, identity change, and stale
  offers reject without delegating to the desktop prompt.
- A two-node loopback identity-substitution integration test proves the
  initiator sees no SAS and neither Trust store changes. Matching-key inbound
  and outbound ceremonies persist Trust and refresh the authoritative desktop
  list.
- Listener and advertisement loops form one supervised failure domain. Either
  unexpected post-enable exit cancels the other loop, stops the socket,
  withdraws the advertisement, releases the browser, changes the runtime and UI
  to a sanitized fault, and allows a fresh explicit retry. Partial startup,
  pairing cancellation, in-flight enable, close, and already-faulted startup
  races are covered.
- The checked desktop XAML exposes explicit enable/disable, permission
  education, listener state, full candidate identity fields, selection,
  pair/cancel, recovery, declared automation names, and keyboard access. The
  Activity layer remains absent, so the surface truthfully states `NOT SHARING`.
- The implementation commit passed the configured Windows/macOS/Ubuntu hosted
  contract matrix, secret scan, and CodeQL workflow.

## What this does not prove

- No physical LAN or multicast packet was observed. The tests do not prove
  DNS-SD behavior through a real router, VLAN, VPN, changing interface, or
  host-firewall policy.
- No two physical devices or two people compared a SAS. Same-host loopback and
  headless decision tests prove protocol and composition contracts only.
- Hosted runners did not exercise native firewall dialogs, packaged-app network
  entitlements, native screen-reader speech, or production permission copy.
- The authenticated control handler deliberately remains idle. No Activity,
  Remote Window, file transfer, mirror, input, emergency stop, or Scene traffic
  was executed, and `NOT SHARING` is not evidence for those later slices.
- This does not complete parent task 7.2, platform-native work, packaging, or
  v1 acceptance. Trusted reconnect status, broader identity-change outcomes,
  progressive native permissions, physical evidence, and the remaining task
  tracker and release-criteria items remain open.
