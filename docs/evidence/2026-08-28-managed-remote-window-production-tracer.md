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

## Hosted exact-SHA verification

On 2026-08-29, the GitHub API and downloaded artifacts were rechecked against
evidence commit `81b90081265d3d37465557d25406972db2079600` (which contains the
implementation commit above). The hosted CI run
[`33155459214`](https://github.com/happys2333/flowspan/actions/runs/33155459214)
is `success` and has that exact `head_sha`; its independently triggered CodeQL
run [`33155459192`](https://github.com/happys2333/flowspan/actions/runs/33155459192)
is also `success` at the same SHA.

- CI test jobs `98797054205` (ubuntu-latest), `98797054321`
  (windows-latest), and `98797054461` (macos-latest) all succeeded. Their
  downloaded test-result artifacts contain exactly 12 TRX files per platform.
  Each platform sums to `2190` total/executed/passed tests. Every TRX terminal
  non-success or uncertain counter is zero: failed, error, timeout, aborted,
  inconclusive, passed-but-run-aborted, not-runnable, not-executed,
  disconnected, warning, completed, in-progress, and pending.
- Secret Scan job `98797054361` succeeded. Downloaded artifact
  `9679402894` (`gitleaks-results.sarif`,
  `sha256:0cb6753164548f80ba7fdbbd3265c2adca8e78be77a402a867bd9f9a82084ddf`)
  is SARIF 2.1.0 with one `gitleaks` run, 208 rules, and 0 results.
- CodeQL job `98797054245` (`Analyze C#`) succeeded: its initialized, locked
  restore, build, and analysis steps all succeeded, and the hosted log records
  a completed analysis upload. The GitHub code-scanning analyses and open-alert
  APIs, queried at the exact SHA during this verification, each returned an
  empty array. That absence is not a retained CodeQL SARIF artifact or a claim
  of an independently counted SARIF result total.
- Reproducible unsigned package jobs all succeeded and each sealed, verified,
  and byte-compared two independently produced package directories before
  uploading its artifact:

  | Runtime | Job | Artifact ID | Artifact SHA-256 |
  | --- | ---: | ---: | --- |
  | `osx-arm64` | `98798002805` | `9679551279` | `a5791b3f73416af608ce3525a2ab0d73bf273e9c1a683813003e1911d40ca80d` |
  | `linux-x64` | `98798002831` | `9679549768` | `e9f84656804d1d4a1273f31465496f5f4855a7d117ffab0f6a4f6b672c1938eb` |
  | `win-x64` | `98798002909` | `9679576815` | `34ec73ebd1730126a8f7ce227b6c8b923a0170dd8fdf6a1af6b3b0c477f6e8cc` |

The downloaded CI artifacts are retained locally under
`/tmp/flowspan-ci-33155459214` for this verification only. Hosted runner
evidence proves the checked managed build/test/package workflow, not native
capture, input, protection, or accessibility API behavior; physical two-Device
operation; signed packages; macOS notarization; or release acceptance.

### Subsequent exact-SHA checkpoint: permission revocation

Commit `579c9cd1b39ac790ca21980cb646d205f501464b` extends the managed
tracer with active native-capture permission revocation. CI run
[`33242809777`](https://github.com/happys2333/flowspan/actions/runs/33242809777)
and CodeQL run
[`33242809786`](https://github.com/happys2333/flowspan/actions/runs/33242809786)
both completed successfully at that exact SHA on 2026-08-29.

- Test jobs `99074918618` (ubuntu-latest), `99074918581`
  (windows-latest), and `99074918623` (macos-latest) each passed
  `2192/2192`. Their retained test-result artifacts are `9711896727`,
  `9711910710`, and `9711893418`, respectively.
- Secret Scan job `99074918491` passed. Its SARIF artifact is `9711859872`
  with GitHub digest
  `sha256:f97946a362df1a63a6349312deb8a5fc67c4554011c6ccf2d0cf75ec14f8ff6d`.
- Reproducible unsigned package jobs `99075385945` (linux-x64),
  `99075385946` (win-x64), and `99075385947` (osx-arm64) passed and uploaded
  artifacts `9711930381`, `9711942289`, and `9711930664`.
- CodeQL job `99074918438` completed its locked restore, analyzed build, and
  analysis steps successfully.

This is hosted managed contract and packaging evidence only. The injected
permission boundary is not a real Windows, macOS, or Linux permission-revocation
result.

### Current implementation checkpoint

Commit `80191d6208b8eb942ff71c894cd5d067471d6499` adds the verified `FSM1`
endpoint connection-failure tracer and hardens response-commit ordering,
deadline fallback, failed-generation isolation, permission revision handling,
callback retirement, and terminal cleanup failure preservation. The local
results below are bound to that implementation commit. Hosted exact-SHA CI and
CodeQL for this commit remain pending until the branch is pushed and the
workflows complete.

## Baseline proven slices

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

## Current proven slices

At `80191d6208b8eb942ff71c894cd5d067471d6499`, the same test class contains
six test cases:

- the success and reversed-grant cases above;
- `TerminalAuthorityOrSafetyLossTerminatesActiveHostSession`, parameterized for
  authenticated control disconnect, same-session Mirror capability revocation,
  and managed native-capture permission revocation; and
- `VerifiedFsm1EndpointConnectionFailureRejectsWithoutAdmissionOrCapture`,
  which uses real authenticated control and a signed, verified endpoint whose
  loopback TCP socket is deliberately unavailable. It returns only the
  allowlisted `media_attachment_failed` reason, never waits for media Admission,
  and starts neither capture nor rendering.

The failed attachment generation becomes non-current immediately: retry, lease
reacquisition, endpoint validation, route preparation, linked operations, and
media attachment are rejected before response delivery finishes. A committed
Rejected response is observable by the host before the owning control
generation closes; if the host does not close it, the original Preparation
deadline supplies the participant-side fail-close fallback. If the response is
not committed, the participant fail-closes. Terminal Dispose or Disconnect
retains the original attachment failure together with any cleanup failure.

The permission scenario uses a deterministic managed permission source. The
endpoint-failure scenario covers TCP connection refusal after endpoint
verification; it is not malformed, tampered, timed-out, or cancelled `FSM1`
attachment traffic. These six cases still do not form the complete boundary
fault matrix.

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

### Current local verification: `80191d6`

The 2026-08-29 implementation checkpoint used the same macOS 26.6.2 arm64 host
and .NET SDK 10.0.301. Debug and Release were built independently with
`-warnaserror`; every test command then used `--no-build --no-restore` against
the immediately preceding build.

Observed results:

- the current tracer passed `6/6` in focused Debug and `6/6` in focused
  Release;
- twenty additional fresh Debug processes passed all six cases, for
  `120/120` case executions;
- the complete Debug and Release solutions each passed `2209/2209` across 12
  test projects with no failed or skipped tests, including Desktop `535/535`
  and Transport `687/687`;
- the new committed-Rejected deadline fallback passed 10 fresh processes
  (`10/10`), and the attachment-primary plus terminal-cleanup-failure theory
  passed three fresh processes (`6/6` case executions) after retaining a bound,
  non-listening endpoint socket for deterministic refusal;
- both solution builds completed with 0 warnings and 0 errors;
- `dotnet format --verify-no-changes`, `git diff --check`, the explicit
  trailing-whitespace search, and the direct/transitive NuGet vulnerability
  audit passed;
- Desktop composition passed only in explicit TEST MODE; and
- the simulator reported protocol 1.7, source preserved, target resumed, and
  atomic Swap committed.

This checkpoint has no local `gitleaks` result. Secret Scan and CodeQL remain
hosted gates for the exact commit, and were still pending when this local
evidence was written. No Windows or Linux execution, native platform API,
physical two-Device, signed package, or notarization result is inferred from
these macOS managed-loopback tests.

## Security relevance

- **T05:** complementary one-way success and reversed-grant denial demonstrate
  peer-relative Mirror direction without inventing a reciprocal capability;
  same-session grant downgrade drains the active session.
- **T06:** capture remains closed before Prepare/Ready and attachment complete;
  media/rendering remain closed until final Admission; Driver input and local
  Emergency Stop are exercised. The managed permission-revocation case closes
  admission, invokes local Emergency Stop, and converges the host owner graph to
  zero; it is not evidence of a real operating-system permission transition.
- **T08:** the success path carries `FSM1` and encrypted media through the real
  production listener and decodes JPEG at the participant.
- **T10:** verified-endpoint TCP refusal exposes only
  `media_attachment_failed`, not the socket exception or endpoint details.
- **T13:** terminal authenticated-control disconnect, capability revocation,
  permission revocation, and verified-endpoint connection refusal converge the
  relevant ownership graph to zero. Rejected-response cleanup is ordered after
  response commit, retains the original deadline as a bounded fallback, and
  preserves attachment and cleanup failures together.
- **T14:** Ready and attachment do not render; final Admission opens rendering,
  and Emergency Stop does not wait for network acknowledgement.
- **T15:** the tracer uses protocol 1.7. Existing protocol tests cover downgrade
  behavior; this evidence adds no physical or packaged downgrade result.

## Explicit non-evidence and remaining gates

The test strategy requires reject, throw, cancel, timeout, revoke, disconnect,
and cleanup-fault coverage at every applicable boundary. This evidence covers
only the six current cases above and does not establish that complete matrix.
In particular, the `FSM1` failure case covers verified-endpoint TCP connection
refusal, not every malformed, tampered, timeout, cancellation, listener, or
cleanup-fault boundary.

Tasks 5, 5.5a, 5.5, and 6-10 remain open, as does the long-term Flowspan Goal.
`CreateProduction()` must continue to report Remote Window unavailable; this
document is not evidence that production Remote Window is available.

There is no evidence here for Windows or Linux tracer execution; Windows,
macOS, or Linux native capture/input/protection APIs; physical two-Device
operation; signed or notarized packages; package lifecycle behavior; or full
release acceptance. Those gates require exact-tree CI and platform or physical
evidence without extrapolating from this same-host managed loopback run.
