# Evidence: desktop trusted reconnect, 2026-07-14

Classification: **Local**, **headless UI/contract**, **same-host loopback**, and
**Hosted CI**

Branch: `codex/v1-foundation`

Verified implementation commit:
`49d1c0c1f6792270e0782ef7fbb1d69febaf8777`

Headless-session remediation commit:
`0dd5a1ac00cd4462115464eec39a008944d901b7`

Verified delivery head:
`0dd5a1ac00cd4462115464eec39a008944d901b7`

## Local environment and commands

```text
OS: macOS 26.5.2 (build 25F84), arm64
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
```

The implementation and remediation heads were checked with the applicable
commands below. Locked restore was rerun for the implementation head; the
remediation reused that lock-verified restore and rebuilt before testing.

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
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
git diff --check
```

The two race-sensitive classes were also run in 20 fresh testhost processes
each:

```sh
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~MainWindowAccessibilityTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~DesktopTrustedPeerConnectionsTests'
```

Observed results:

- locked restore passed for all 24 projects;
- format verification and the patch whitespace check passed;
- Release build passed with 0 warnings and 0 errors;
- the complete unfiltered suite passed 383 tests with 0 failures and 0 skips:
  33 domain, 22 integration, 15 Linux platform, 10 macOS platform, 12 Windows
  platform, 8 common platform, 17 protocol, 86 security, 14 DNS-SD transport,
  90 transport, and 76 desktop tests;
- 20 independent Headless-window processes each passed 5/5, for 100 additional
  race-sensitive executions;
- 20 independent trusted-reconnect processes each passed 10/10, for 200
  additional coordinator, cancellation, and loopback executions;
- the composition validator printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`;
- the deterministic simulator negotiated protocol 1.0, preserved the source,
  resumed the target, and exited successfully;
- NuGet reported no known vulnerable direct or transitive package in any
  project.

## First hosted result and corrected failure diagnosis

The implementation CI run
[29335443123](https://github.com/happys2333/flowspan/actions/runs/29335443123)
did not pass completely. macOS job `87093411885`, Windows job `87093411908`,
and secret-scan job `87093411924` succeeded, while Ubuntu job `87093411839`
failed one desktop test:
`MainWindowAccessibilityTests.ShellDeclaresTextStatesAndSupportsKeyboardDisclosure`.
The exception was a `NullReferenceException` in
`Avalonia.Headless.HeadlessUnitTestSession.Dispose`. CodeQL run
[29335443074](https://github.com/happys2333/flowspan/actions/runs/29335443074)
and its `Analyze C#` job `87093412337` succeeded for the same implementation
commit.

The test already waited for the production window's `Closed` event, so this was
not the earlier asynchronous-close lifecycle gap and was not a product window
failure. Inspection of Avalonia 12.1.0's exact
`HeadlessUnitTestSession.StartNew` source showed a distinct construction race:
the worker can publish the session before the caller-side assignment stores the
dispatcher `Task`. The published session can therefore retain a null dispatcher
task that `Dispose` later dereferences. Avalonia 12.1.0 was the latest package
available during this check, and the same construction pattern remained in the
upstream main branch.

The remediation uses Avalonia's supported assembly session cache,
`HeadlessUnitTestSession.GetOrStartForAssembly`, instead of disposing a newly
constructed session per test. Assembly attributes explicitly select
`AvaloniaTestIsolationLevel.PerTest`, so each dispatch still creates and
disposes a fresh application and Dispatcher scope; only the session dispatcher
lives for the testhost process. Every test continues to await the real
production `Closed` event.

No upstream issue was opened. Repository communication remains subject to the
separate exact-text approval gate.

## Replacement hosted results

Replacement CI run
[29341591365](https://github.com/happys2333/flowspan/actions/runs/29341591365)
completed successfully for the verified delivery head. Every required step
reported `success` in:

- `Test (ubuntu-latest)` (`87114450304`);
- `Test (macos-latest)` (`87114450332`);
- `Test (windows-latest)` (`87114450503`);
- `Secret scan` (`87114450219`).

Each hosted OS restored all 24 locked projects, verified formatting, built with
warnings as errors, ran all 383 tests with 0 failures and 0 skips, ran the
explicit TEST MODE desktop composition validator, ran the deterministic
simulator, and uploaded test evidence. Each desktop suite reported 76 passes,
each transport suite 90, each security suite 86, and each integration suite 22.
Every validator printed its explicit TEST MODE success line; every simulator
printed protocol 1.0, `Source preserved: True`, and `Target resumed: True`.

Replacement CodeQL run
[29341591320](https://github.com/happys2333/flowspan/actions/runs/29341591320)
also completed successfully. Its `Analyze C#` job (`87114450089`) restored
locked dependencies, built the analyzed source, scanned 141/141 C# files, and
successfully uploaded results.

Both replacement runs were created at `2026-07-14T14:36:16Z`. CI completed at
`2026-07-14T14:38:56Z`, and CodeQL completed at `2026-07-14T14:39:21Z`. Run,
job, step, commit, timestamp, suite-count, validator-output, simulator-output,
and CodeQL coverage evidence was queried with `gh run view` on 2026-07-14.

## What this proves

- Trusted reconnect starts only inside the explicitly enabled local-network
  lifetime; desktop launch alone still opens no network service.
- Ordinal Device-ID election assigns exactly one outgoing connector while the
  other peer waits on the shared authenticated listener, avoiding symmetric
  duplicate idle channels without granting authority.
- Every connection attempt rereads current Trust and the required local grant,
  reconstructs the candidate key only from Trust, and verifies the signed offer
  before opening TCP.
- Per-peer waiting, waiting-inbound, authenticating, authenticated-idle,
  retrying, capability-required, permanent-block, and unavailable states are
  deterministic and never describe an idle channel as sharing.
- Conflicting discovery fingerprints latch a block until networking is
  disabled; handshake identity changes also warn when no safe observed
  fingerprint exists. Neither path silently repairs Trust.
- Capability edit, downgrade, revoke, network failure, explicit disable, and
  window close reconcile, cancel, and drain the relevant reconnect workers.
- A production-composed same-host TCP test authenticates both peers through one
  elected connection and projects `AUTHENTICATED — IDLE / NOT SHARING` on both
  sides. Headless tests cover the corresponding text, warning, keyboard, and
  automation surface.
- The corrected Headless harness passed the OS that exposed the upstream race,
  while retaining per-test application isolation and production-close waits.

## What this does not prove

- No physical LAN or multicast packet was observed. These results do not prove
  DNS-SD through a router, VLAN, VPN, host firewall, or changing interface.
- No two physical devices exercised reconnect, peer restart, sleep/wake,
  identity replacement, or capability changes against each other.
- Hosted runners did not grant native network permissions, show native
  notifications, operate a screen reader, or validate a packaged window.
- The authenticated handler remains deliberately idle. No Activity, Remote
  Window, file/media, mirror, input, emergency-stop, Group, or Scene traffic was
  carried, and `NOT SHARING` is not evidence for those later slices.
- This completes task 7.2d only. Progressive native permission education,
  physical evidence, platform integration, packaging, independent security
  review, and the remaining v1 release criteria are still open.
