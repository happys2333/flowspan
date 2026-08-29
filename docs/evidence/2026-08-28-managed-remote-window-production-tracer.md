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

### Subsequent exact-SHA checkpoint: permission loss

Commit `579c9cd1b39ac790ca21980cb646d205f501464b` extends the managed
tracer with active native-capture permission loss (`Granted` to `Denied`). CI run
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
permission boundary is not a real Windows, macOS, or Linux permission-loss
result.

### Attachment-failure implementation checkpoint

Commit `80191d6208b8eb942ff71c894cd5d067471d6499` adds a verified `FSM1`
attachment-failure tracer and hardens response-commit ordering,
deadline fallback, failed-generation isolation, permission revision handling,
callback retirement, and terminal cleanup failure preservation. Commit
`ca638742c84b32a98c79d710df6f4a85157189bb` then extends the preparation-only
deferred fail-close through an accepted TCP connection whose `FSM1` attachment
fails, while preserving ordinary media eager fail-close. Test-only commit
`761ac750bbe3e12bd07f89037c71af4a9607102a` then proves TCP accept before the
reset and removes a separate outbound-reservation scheduling assumption. The
attachment-checkpoint local results below are bound to `761ac75`. Hosted
exact-SHA CI and CodeQL for that commit are tracked separately below.

### Hosted exact-SHA verification: attachment-failure checkpoint

On 2026-08-29, CI run
[`33246518217`](https://github.com/happys2333/flowspan/actions/runs/33246518217)
and CodeQL run
[`33246518202`](https://github.com/happys2333/flowspan/actions/runs/33246518202)
both completed successfully at exact SHA
`761ac750bbe3e12bd07f89037c71af4a9607102a`.

- Test jobs `99084815368` (ubuntu-latest), `99084815327`
  (windows-latest), and `99084815272` (macos-latest) all passed. Downloaded
  artifacts `9713036050`, `9713049836`, and `9713030986` each contain exactly
  12 TRX files summing to `2210/2210` executed and passed tests. Every failed,
  error, timeout, aborted, inconclusive, passed-but-run-aborted, not-runnable,
  not-executed, disconnected, warning, completed, in-progress, and pending
  counter is zero.
- Secret Scan job `99084815167` passed. Artifact `9713006548` has GitHub digest
  `sha256:6afab1f33a3f8ad0b10359123533216bc44c0dc8860b367bcbc7ec7306d3e5a9`;
  the downloaded SARIF is version 2.1.0 with one run, 208 rules, and 0 results.
- CodeQL job `99084815073` passed initialization, locked restore, analyzed
  build, and analysis. The exact-SHA GitHub analysis record `1691410909`
  reports 52 rules and 0 results, and the open-alert query returned an empty
  array.
- All three reproducible unsigned package jobs passed:

  | Runtime | Job | Artifact ID | Artifact SHA-256 |
  | --- | ---: | ---: | --- |
  | `osx-arm64` | `99085198766` | `9713071832` | `d3ed2bdb96c2895514e643af398a4a673bb63e032d0c3de1170329ff00b848a0` |
  | `linux-x64` | `99085198816` | `9713070884` | `6dd7cfd17c23edef3c16bc73ebb3cbe456a93ac0b7e1a1ea66119b5e339cdfed` |
  | `win-x64` | `99085198829` | `9713092713` | `0beb98d60261a45406da9ac01fa369eb8049d52aa9e3458dbad832be79b6ad9b` |

The successful run followed two recorded diagnostic failures rather than a
blind retry. CI `33245101404` at `e4e3c8b` exposed a macOS-only preparation-peer
test that depended on a bound-but-not-listening socket refusing promptly. After
the accepted-TCP deferred fail-close repair, CI `33245887517` at `ca63874`
exposed the same platform assumption in the managed tracer. Linux, Windows,
Secret Scan, and both corresponding CodeQL runs completed successfully, but
neither failed CI run is acceptance evidence. `761ac75` replaces both fixtures
with an accepted loopback connection followed by `SO_LINGER(0)` reset and
explicitly proves the accept boundary.

### Subsequent implementation checkpoint: renderer preparation

Implementation commit `fde38b2bae9d02f177fd86e22a8beecb060325e9`
extends the managed tracer from six to nine cases. Its three-case renderer
theory first completes real authenticated protocol-1.7 control and a successful
`FSM1` attachment, then supplies one of the following preparation outcomes:

- the renderer factory throws;
- the renderer factory legally returns a null/Missing renderer; or
- the renderer factory throws a foreign or tokenless
  `OperationCanceledException` that is not the linked generation/caller
  cancellation and is not deadline expiry.

In every case, external test assertions independently observe both the host and
participant media sessions with `IsAttached == true` and the exact protocol,
Device pair, Session, and Activity binding before renderer failure. The
participant synchronously marks the connection generation fail-close-pending
before its Rejected response is observed. The host observes
`renderer_start_failed` for the throw and foreign cancellation, or
`renderer_unavailable` for null/Missing, before fail-close. Admission, capture,
media send, and rendering remain at zero, and all owner, route, media-directory,
and authenticated-control counts converge to zero.

The deferral is request-bound rather than an unbounded public poison. It accepts
only the exact Preparation request with a positive remaining deadline no more
than 10 seconds away. The watchdog remains live after connection-lease disposal
and fail-closes the generation at the original request deadline if the host
does not. Repeating the same request is idempotent; a conflicting request cannot
replace or extend it. Expired, overlong, conflicting, or time-provider setup
failure refuses deferral without poisoning the generation and uses eager
fail-close. Actual linked cancellation and deadline expiry also remain eager.
Owner revocation cancels the watchdog; explicit and deadline close share one
cleanup; renderer primary, cleanup, and lifecycle failures remain jointly
observable.

### Hosted exact-SHA verification: renderer-preparation checkpoint

On 2026-08-29, CI run
[`33249181870`](https://github.com/happys2333/flowspan/actions/runs/33249181870)
and CodeQL run
[`33249181871`](https://github.com/happys2333/flowspan/actions/runs/33249181871)
both completed successfully at exact SHA
`fde38b2bae9d02f177fd86e22a8beecb060325e9`.

- Test jobs `99091769535` (ubuntu-latest), `99091769679`
  (windows-latest), and `99091769627` (macos-latest) all succeeded. Downloaded
  artifacts `9713839810`, `9713849788`, and `9713835696` each contain exactly
  12 TRX files summing to `2232/2232` executed and passed tests. Every failed,
  error, timeout, aborted, inconclusive, passed-but-run-aborted, not-runnable,
  not-executed, disconnected, warning, completed, in-progress, and pending
  counter is zero. Their GitHub artifact digests are, respectively,
  `sha256:3d5cd0dbdbc2dd959e289313ce0581bf597de6373e084fe15259cd134f653fa5`,
  `sha256:e37d6b38765120dc46a95f3be09eabf9b821ed5f48a589dc19d0a1ad9d357ec2`,
  and `sha256:a4be2fd5168df2247114fc1ed42ff65e184401116b7173b4a397ec2e8d6b5952`.
- Secret Scan job `99091769653` passed. Artifact `9713801682` has GitHub
  digest
  `sha256:75a01dbc25067eb98aa441cba9a4c6dde6feb312dde28b806cb1ef84b04f857a`;
  the downloaded SARIF is version 2.1.0 with one run, 208 rules, and 0 results.
- CodeQL job `99091769641` passed. Exact-SHA analysis `1691513849` reports
  52 rules and 0 results, and the repository open-alert query returned 0.
- All three reproducible unsigned package jobs and artifacts passed:

  | Runtime | Job | Artifact ID | Artifact SHA-256 |
  | --- | ---: | ---: | --- |
  | `osx-arm64` | `99092224960` | `9713867688` | `02197ccd98204d6349b64d8466eb158d1526385654e20d7cd181cf95c16fa001` |
  | `linux-x64` | `99092224990` | `9713871814` | `274d1b24d97e60a14ed271907c9a3f07992d52106db0aeebe699ce822588a55f` |
  | `win-x64` | `99092224953` | `9713873910` | `c0b562b97b45465d6d78ae64e492ad0b7b4ffb68e5c9c8255acd663180f67e4c` |

Downloaded test and Secret Scan artifacts were rechecked under
`/tmp/flowspan-ci-33249181870-verify`; that temporary path is not durable
project evidence. These hosted results prove the managed build, contract tests,
explicit TEST MODE composition, simulator, Secret Scan, CodeQL, dependency
audit, and reproducible unsigned packaging on the named runners. They do not
prove native API behavior, physical two-Device operation, signed packages, or
notarization.

### Subsequent test-only checkpoint: preparation expiry

Test-only commit `0f1f32d0e8ea251194755a5b4d150d3e294433ff` changes no
production source and expands the managed tracer from nine to ten cases.
`VerifiedFsm1AttachmentThenPreparationExpiryFailsClosedBeforeAdmissionOrCapture`
uses real authenticated protocol 1.7 and a signed, verified candidate with the
expected endpoint, identity fingerprint, and negotiated version. It completes
`FSM1` and Ready, invokes renderer factory Prepare exactly once, and independently
observes both host and participant media sessions with `IsAttached == true` and
the exact protocol, Device pair, Session, and Activity binding.

Only then does the test-only coordinator `MutableClock` move exactly to
`request.Deadline`. Existing production `EnsurePreparationIsCurrent` treats
deadline equality as expired and returns the allowlisted
`preparation_expired`. Media-attachment wait occurs once. Admission publication,
capture, media send, and rendering remain at zero. Host fail-close and Dispose
each occur once. Snapshot and TerminalFailure are null. ActiveMediaBudget is
null because no active generation was published; this is not a claim that the
test observed a pending media budget. Renderer, route, both directories,
handler, lease, channel, and control owners drain to zero, and the old connection
generation cannot be reacquired.

At this checkpoint, this was one deterministic post-`FSM1`, pre-Admission
timeout case; actual caller cancellation, cleanup-fault injection, and the
complete per-boundary fault matrix remained open. Superseding exact-SHA hosted
evidence is recorded below.

### Hosted exact-SHA verification: expiry and CI hang guard

Superseding exact SHA `e504c839cac2e45a4ca7ad17316c8278e4928c2e`, which contains
the `0f1f32d` expiry test, the subsequent documentation, and the `e504c83` CI
diagnostic guard, passed CI run
[`33250747660`](https://github.com/happys2333/flowspan/actions/runs/33250747660)
and CodeQL run
[`33250747671`](https://github.com/happys2333/flowspan/actions/runs/33250747671).

- Test jobs `99095889058` (Ubuntu), `99095889178` (Windows), and `99095889190`
  (macOS) succeeded. Downloaded artifacts contain 12 TRX files per platform,
  each summing to `2233/2233` executed and passed tests with every non-success
  counter zero:

  | Platform | Artifact ID | Artifact SHA-256 |
  | --- | ---: | --- |
  | Linux | `9714317366` | `8f6eb70ef77b1ac1acbf83a6b4a886459826f78da0f07a8560148133e9a68f28` |
  | Windows | `9714325254` | `b1f973fd23ddf977dfb7857309db4b7bba83a436b12e37549d93f6c5fe042453` |
  | macOS | `9714315419` | `2c6a77bb9f8da8028e2cb21ff6091b8925cfa1429fd544f1ce71155d3cc5b69f` |

- Secret Scan job `99095889152` passed. Artifact `9714281655`, digest
  `eca3b2148b0b1e0a135046a7b833319426258179cb5cbc65f3e1d9d4650f7296`,
  is SARIF 2.1.0 with one run, 208 rules, and 0 results.
- CodeQL job `99095889246` passed. Exact-SHA analysis `1691573225` reports 52
  rules and 0 results; the open-alert query returned 0.
- Reproducible unsigned package jobs all passed:

  | Runtime | Job | Artifact ID | Artifact SHA-256 |
  | --- | ---: | ---: | --- |
  | `osx-arm64` | `99096307082` | `9714340281` | `f6747a7a89756c52db5bb703848945cdeb1c94e491e51dfa0b3394a16852e105` |
  | `linux-x64` | `99096307091` | `9714342742` | `f01c1337280ba1a7387ab515b7acb5cc27c49fd38d48bcff6bd2df6c2f131adc` |
  | `win-x64` | `99096307117` | `9714351926` | `e495877ba6b43553aded654a74b836e8231e9d8740396d0832fed5d2ee4621b9` |

A preceding docs-only CI run `33249644505` had an intermittent Windows testhost
stall. Attempt 1 produced no Desktop TRX before the 20-minute job timeout and
cancellation; the other 11 TRX files passed `1688/1688`. Isolated rerun job
`99095158216` produced 12 TRX files at `2232/2232` and made attempt 2 successful.
That is evidence of intermittency, not a deterministic Windows platform failure.
The `e504c83` workflow adds `--blame-hang --blame-hang-timeout 3m
--blame-hang-dump-type none`; its normal Windows test step completed in 51 seconds.
The guard only makes a future hang fail fast and retain sequence diagnostics. It
does not identify or fix the unknown root cause and intentionally collects no
memory dump.

These hosted results prove managed tests, diagnostics, Secret Scan, CodeQL, and
reproducible unsigned packages on the named runners. They are not native API,
physical two-Device, signed-package, notarization, or release evidence.

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

## Attachment-failure proven slices

At `80191d6208b8eb942ff71c894cd5d067471d6499`, retained by `761ac75`, the same
test class contains six test cases:

- the success and reversed-grant cases above;
- `TerminalAuthorityOrSafetyLossTerminatesActiveHostSession`, parameterized for
  authenticated control disconnect, same-session Mirror capability revocation,
  and managed native-capture permission loss (`Granted` to `Denied`); and
- `VerifiedFsm1AttachmentFailureAfterTcpAcceptRejectsWithoutAdmissionOrCapture`,
  which uses real authenticated control and a signed, verified endpoint whose
  loopback listener proves TCP accept before immediately resetting the
  connection. It returns only the allowlisted `media_attachment_failed` reason,
  never waits for media attachment or publishes Admission, and starts neither
  capture nor rendering.

The failed attachment generation becomes non-current immediately: retry, lease
reacquisition, endpoint validation, route preparation, linked operations, and
media attachment are rejected before response delivery finishes. A committed
Rejected response is observable by the host before the owning control
generation closes; if the host does not close it, the original Preparation
deadline supplies the participant-side fail-close fallback. If the response is
not committed, the participant fail-closes. Terminal Dispose or Disconnect
retains the original attachment failure together with any cleanup failure.

The permission scenario uses a deterministic managed permission source. The
attachment-failure scenario covers an accepted TCP connection reset before the
`FSM1` handshake completes; it is not malformed, tampered, timed-out, or
cancelled `FSM1` attachment traffic. These six cases still do not form the
complete boundary fault matrix.

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

### Attachment-failure local verification: `761ac75`

The 2026-08-29 implementation checkpoint used the same macOS 26.6.2 arm64 host
and .NET SDK 10.0.301. Debug and Release were built independently with
`-warnaserror`; every test command then used `--no-build --no-restore` against
the immediately preceding build.

Observed results:

- the attachment-failure tracer checkpoint passed `6/6` in focused Debug and
  `6/6` in focused Release;
- ten fresh Debug and ten fresh Release processes passed all six cases, for
  `120/120` case executions;
- the complete Debug and Release solutions each passed `2210/2210` across 12
  test projects with no failed or skipped tests, including Desktop `535/535`
  and Transport `688/688`;
- the new committed-Rejected deadline fallback passed 10 fresh processes
  (`10/10`);
- the accepted-TCP attachment failure regression passed in focused Debug and
  Release, and the attachment-primary plus terminal-cleanup-failure theory
  passed 10 fresh Debug plus 10 fresh Release processes (`40/40` case
  executions) using a real loopback listener that accepts and immediately resets
  the socket with `SO_LINGER(0)`;
- both solution builds completed with 0 warnings and 0 errors;
- `dotnet format --verify-no-changes`, `git diff --check`, the explicit
  trailing-whitespace search, and the direct/transitive NuGet vulnerability
  audit passed;
- Desktop composition passed only in explicit TEST MODE; and
- the simulator reported protocol 1.7, source preserved, target resumed, and
  atomic Swap committed.

This checkpoint has no local `gitleaks` result; the exact-SHA hosted Secret Scan
and CodeQL results are recorded above. Hosted Windows/Linux execution proves the
managed build, contract tests, explicit TEST MODE composition, simulator, and
unsigned packaging only. Neither those runs nor the local macOS loopback proves
a native platform API, physical two-Device path, signed package, or notarization.

### Subsequent local verification: `fde38b2`

The renderer-preparation checkpoint was verified on the same macOS 26.6.2
arm64 host with .NET SDK 10.0.301. Debug and Release builds used warnings as
errors, and the complete test suites ran sequentially against the immediately
preceding build.

Representative commands:

```sh
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Debug --no-build --no-restore \
  --filter 'FullyQualifiedName~VerifiedFsm1AttachmentThenRendererFailureCommitsRejectionBeforeFailClose'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Debug --no-build --no-restore \
  --filter 'FullyQualifiedName~AuthenticatedRemoteWindowConnectionLeaseTests'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Debug --no-build --no-restore \
  --filter 'FullyQualifiedName~AuthenticatedRemoteWindowMediaSessionsTests'
dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
```

Observed results:

- Debug and Release warning-as-error builds each completed with 0 warnings and
  0 errors;
- the complete Debug and Release solutions each passed `2232/2232`, including
  Desktop `544/544` and Transport `701/701` in each configuration;
- ten fresh Debug and ten fresh Release processes passed all three
  renderer-preparation theory cases (`60/60` case executions);
- the focused connection-lease tests passed `16/16` in Debug and Release;
- the focused authenticated media-session tests passed `28/28` in Debug and
  Release;
- format, diff, direct/transitive NuGet vulnerability, explicit TEST MODE
  composition, and deterministic protocol-1.7 simulator checks passed; and
- this machine has no `gitleaks` installation, so no local secret-scan result is
  claimed.

An internal strict review of the final implemented slice reported no P0, P1, or
P2 finding. That is a code-review result, not an external security audit. The
separate exact-SHA hosted results are recorded above and do not change the local
evidence boundary.

### Test-only preparation-expiry local verification: `0f1f32d`

The test-only checkpoint was verified on local macOS in Debug and Release. The
focused expiry case passed `1/1` in each configuration, and the complete managed
tracer class passed `10/10` in each. Warning-as-error solution builds completed
with 0 warnings and 0 errors. The complete Debug and Release solutions each
passed `2233/2233`, including Desktop `545/545` and Transport `701/701`.
Formatting, diff, direct/transitive NuGet vulnerability, explicit TEST MODE
composition, and deterministic protocol-1.7 simulator checks passed.

Internal strict review reported no P0, P1, or P2 finding. That is a code-review
result, not an external security audit. Superseding exact-SHA hosted results are
recorded above.

### Subsequent test-only checkpoint: actual caller cancellation

Test commit `45e2d494501167712ec4abdff69d8d232f355d14`, followed by
fixture-reliability commit `5bb6d0863033c3b6668335e15d6a6fe336ee46a7`, changes no
production source and expands the managed tracer from ten to eleven cases. The
new caller-cancellation case uses real authenticated protocol 1.7, a signed and
verified candidate, successful `FSM1` plus Ready, and bilateral media sessions
attached to the exact protocol, Device pair, Session, and Activity binding.

An independent caller CTS is supplied only to `StartAsync`; the harness CTS
continues to own the connection, run, and cleanup. The final hook cancels the
caller while the clock still satisfies `Now < request.Deadline`. Production
surfaces the `OperationCanceledException` family—observed as
`TaskCanceledException`—with the exact caller token. It is not deadline expiry,
a foreign renderer cancellation, or a bounded rejection reason. Admission,
capture, media send, and rendering remain zero. Host fail-close and Dispose each
occur once, and every renderer, route, directory, handler, lease, channel, and
control owner drains.

Local focused caller-cancellation runs passed `1/1` in Debug and Release; the
whole tracer class passed `11/11` in both configurations; and twenty fresh Debug
caller processes passed `20/20`. After the fixture reliability repair, Debug and
Release warning-as-error builds completed with zero warnings and errors, and
both full solutions passed `2234/2234`, including Desktop `546/546`, Platform
`219/219`, and Transport `701/701`. Format, diff, dependency-vulnerability,
explicit TEST MODE composition, and simulator checks passed. Strict caller
review reported no P0/P1/P2 finding and is not an external audit. Exact-SHA
hosted results are recorded below.

The first full Debug run exposed an old Platform-test fixture race: expected
`BoundaryFailed`, actual `Applied`. Production `stateLock` already supplied the
correct linearization; only `RecordingCaptureBoundary` used a shared non-atomic
call count. Before repair, parallel stress failed 23 of 400 runs. The fixture now
uses an interlocked capture count, deterministic barrier, locked timeline, and
`finally` release/join. Post-repair stress passed `160/160` plus `80/80`, and
strict review reported no P0/P1/P2 finding. This was a test-fixture reliability
repair, not a product bug.

### Hosted exact-SHA verification: caller cancellation and fixture reliability

At exact SHA `5bb6d0863033c3b6668335e15d6a6fe336ee46a7`, CI run
[`33251741558`](https://github.com/happys2333/flowspan/actions/runs/33251741558)
and CodeQL run
[`33251741546`](https://github.com/happys2333/flowspan/actions/runs/33251741546)
both completed successfully.

- Test jobs `99098481419` (Ubuntu), `99098481420` (Windows), and `99098481485`
  (macOS) succeeded. Downloaded artifacts each contain 12 TRX files summing to
  `2234/2234`, with every non-success counter zero:

  | Platform | Artifact ID | Artifact SHA-256 |
  | --- | ---: | --- |
  | Windows | `9714619720` | `9827bdc21161ab0ab0c56c9dbcd609fc9ad73e35fc06982bb97f645dc4541667` |
  | Linux | `9714606289` | `477e8b7385ca7cb02cc517dfdc3225205e65c8718d1f807d41c688859ec87396` |
  | macOS | `9714593978` | `f7c4d6374144f60f4984de0fd570b799900df283d7700c72c8934c1dad973316` |

- Secret Scan job `99098481369` passed. Artifact `9714569328`, digest
  `a7fe0ca862740be13e1b38ce80c2f7bc14ed587342e7980dfaf740e7b30164d9`,
  is SARIF 2.1.0 with one run, 208 rules, and 0 results.
- CodeQL job `99098481276` passed. Exact-SHA analysis `1691612129` reports 52
  rules and 0 results; the open-alert query returned 0.
- All reproducible unsigned package jobs passed:

  | Runtime | Job | Artifact ID | Artifact SHA-256 |
  | --- | ---: | ---: | --- |
  | `linux-x64` | `99098967284` | `9714639080` | `625b4ff6c80e6fcec9200a4e7eb658357f7944cd02b706fabe6e8bd7f80501e8` |
  | `osx-arm64` | `99098967311` | `9714636465` | `19d51d1ae2ba401b32686743e234cf35f46bdc5f8fa1b5191a4c409cab9a7650` |
  | `win-x64` | `99098967315` | `9714643950` | `b65b7e81ea1a174bf77ac2c1444e704f478d9e52e91b4bfb13e5cf4990700f97` |

These hosted results prove the managed build, contract tests, explicit TEST MODE
composition, simulator, Secret Scan, CodeQL, dependency audit, and reproducible
unsigned packaging on the named runners. They do not prove native API behavior,
physical two-Device operation, signed packages, notarization, or release
readiness.

This checkpoint covers only one post-`FSM1`, pre-Admission actual caller
cancellation. Cleanup-fault injection and the complete per-boundary matrix
remain open.

## Hosted exact-SHA verification: host-control test scheduling repair

Docs-only SHA `f300432c7e372658f06d2196a182c3c9ddfc99af` did not pass its
exact-SHA CI run
[`33252295470`](https://github.com/happys2333/flowspan/actions/runs/33252295470).
Linux job `99099957823` and macOS job `99099957860` succeeded, and their
downloaded artifacts contain 12 TRX files at `2234/2234`. Windows job
`99099957891` produced all 12 TRX files but passed only `2233/2234`; its Desktop
TRX passed `545/546`. The single failure was
`DesktopRemoteWindowHostControlPeerTests.ExactParticipantPeerDisconnectRoutesAndDrainsBeforeReplacement`
after 5.48 seconds with the bounded message "The lifetime operation did not
retire the old generation."

| Platform | Artifact ID | Artifact SHA-256 | Result |
| --- | ---: | --- | --- |
| Windows | `9714781603` | `c0094be6232aa717cba2b731cef52b3ec05bedc5c92ea16fbebc69795f0835a7` | `2233/2234`, one failed |
| Linux | `9714768158` | `38c5dc27c57d5f2354ecff99b6f7f8fb512cfd52b86cc64582ac4fd4430fc8a0` | `2234/2234` |
| macOS | `9714765004` | `f991c783c77f490cabb8076f5a56fc2672c92c104a1de456b13ecc947e28fce3` | `2234/2234` |

Secret Scan job `99099957755` passed. Artifact `9714729288`, digest
`9505dc05692200f0c79858c5d83e686cd1635c62b100ec79b090fef78de935fa`,
is SARIF 2.1.0 with one run, 208 rules, and 0 results. CodeQL run
[`33252295459`](https://github.com/happys2333/flowspan/actions/runs/33252295459)
and job `99099957748` passed; analysis `1691638338` reports 52 rules and 0
results. Package jobs were skipped because Windows tests failed. Consequently,
neither the CI run nor its skipped packages are acceptance evidence.

Production `DesktopRemoteWindowHostControlPeer.Register` retires the current
generation under its state gate before synchronously waiting for the old routed
call to drain. Diagnosis found no production counterexample. The failed fixture
instead scheduled the synchronously blocking participant disconnect and then
the synchronously draining replacement `Register` with `Task.Run`, while its
test continuation tight-polled with `Task.Yield`. Under Windows full-suite
thread-pool pressure, the replacement delegate could remain unstarted for the
five-second observation window. Artificially limiting the runtime to two worker
threads reproduced whole-testhost starvation, including timeout continuations;
that setting remained unsuitable as a post-fix pass criterion and is not counted
as one.

Test-only commit `7b6a6d6796e0280c53eb71755285090c8e19cb5d` changes no production
source. Every synchronously blocking host-control peer disconnect, replacement
`Register`, and external registration `Dispose` in the test class now runs with
`TaskCreationOptions.LongRunning`, `DenyChildAttach`, and
`TaskScheduler.Default`. The failed case has a separate
`replacementStarted` gate before retirement observation. The five-second poll
now yields through a 10 ms cancellable delay. It still rejects a production
implementation that fails to retire current, publishes replacement before old
call drain, or completes either lifetime operation early.

Representative local commands:

```sh
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Debug --no-build --no-restore \
  --filter 'FullyQualifiedName~DesktopRemoteWindowHostControlPeerTests'
COMPlus_ThreadPool_ForceMinWorkerThreads=1 \
COMPlus_ThreadPool_ForceMaxWorkerThreads=8 \
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Debug --no-build --no-restore \
  --filter 'FullyQualifiedName~ExactParticipantPeerDisconnectRoutesAndDrainsBeforeReplacement'
export COMPlus_ThreadPool_ForceMinWorkerThreads=1
export COMPlus_ThreadPool_ForceMaxWorkerThreads=8
seq 1 80 | xargs -P 8 -I{} sh -c \
  'dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
    --configuration Debug --no-build --no-restore \
    --filter "FullyQualifiedName~DesktopRemoteWindowHostControlPeerTests" \
    --logger "console;verbosity=quiet" >/dev/null'
dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
```

Local macOS Debug and Release warning-as-error builds each completed with zero
warnings and errors. Both complete solutions passed `2234/2234`, including
Desktop `546/546`, Platform `219/219`, and Transport `701/701`. The host-control
test class passed `15/15` in each configuration. With a runnable constrained
worker configuration, the exact regression passed `1/1` in two seconds; eight
concurrent processes then ran the class 80 times, passing `1200/1200` case
executions in 28 seconds. Format, diff, direct/transitive NuGet vulnerability,
explicit TEST MODE composition, and deterministic protocol-1.7 simulator checks
passed. Strict review found no P0/P1 in the change. One pre-existing P2
test-only cleanup debt remains: several sibling assertion-failure paths do not
release their blocking fake in `finally` and can therefore compound a future
regression's diagnostics.

At exact SHA `7b6a6d6796e0280c53eb71755285090c8e19cb5d`, CI run
[`33253258876`](https://github.com/happys2333/flowspan/actions/runs/33253258876)
and CodeQL run
[`33253258929`](https://github.com/happys2333/flowspan/actions/runs/33253258929)
both completed successfully.

- Test jobs `99102472825` (Ubuntu), `99102472803` (Windows), and `99102472713`
  (macOS) succeeded. Downloaded artifacts each contain 12 TRX files summing to
  `2234/2234`; failed, error, timeout, and aborted counters are all zero:

  | Platform | Artifact ID | Artifact SHA-256 |
  | --- | ---: | --- |
  | Windows | `9715065785` | `aa58568f2af93805d5ccffb01a6ae6b516627c647b4afb2ab00e183f3b9a6809` |
  | Linux | `9715054224` | `e05754484e3401fef97bc0e411da1ae6dc22307e79ce83e55fcd787a85b45274` |
  | macOS | `9715047093` | `5751b13240cfc8d7b22cb9c6e28c46bcab3f1e50a2a706759f3a4e1ae9a29264` |

- Secret Scan job `99102472889` passed. Artifact `9715016065`, digest
  `ff0a6eef7f0d1d36ad2169b3f17df6f259070666d5f4248ea4af5ad5dce37631`,
  is SARIF 2.1.0 with one run, 208 rules, and 0 results.
- CodeQL job `99102473105` passed. Exact-SHA analysis `1691683754` reports 52
  rules and 0 results; the branch open-alert query returned 0.
- All reproducible unsigned package jobs passed:

  | Runtime | Job | Artifact ID | Artifact SHA-256 |
  | --- | ---: | ---: | --- |
  | `osx-arm64` | `99102949512` | `9715082255` | `29b767de49b17d69bb62e0a9f66daf49a87705e000a23f2cb96283a12831a8fa` |
  | `linux-x64` | `99102949539` | `9715086623` | `3c82173e1e22135b8beece9d161dd0c8ae1a88e81b026fefd4bd2ca505f04121` |
  | `win-x64` | `99102949546` | `9715090885` | `96f34bb0ecb317f28f77cffbedbd3af89d03ca1ae1439090e8446f725efc5e0b` |

This repair closes the recorded test-scheduling failure only. It adds no tracer
case and no product behavior. The successful hosted results remain managed
contract, TEST MODE composition, analysis, and unsigned-package evidence; they
do not prove native capture/input/protection, physical two-Device behavior,
signed packages, notarization, or release acceptance.

## Hosted exact-SHA verification: renderer attachment barrier

Docs commit `908a04a2f465bccccf56b72fd36cb5f048506a63` did not pass exact-SHA
CI run
[`33254082958`](https://github.com/happys2333/flowspan/actions/runs/33254082958).
Windows job `99104665954` and macOS job `99104666009` succeeded at
`2234/2234`. Ubuntu job `99104665963` produced 12 TRX files but passed only
`2233/2234`; its Desktop TRX passed `545/546`. The only failure was the `Throw`
row of
`VerifiedFsm1AttachmentThenRendererFailureCommitsRejectionBeforeFailClose`,
which reached the former host-session-attached assertion after 73 ms and
observed false.

| Platform | Artifact ID | Artifact SHA-256 | Result |
| --- | ---: | --- | --- |
| Linux | `9715297813` | `2855db7dbb5b0abaac1fae9540b07b66c8af0c0f6dd1c6db01fa71f7e0f68425` | `2233/2234`, one failed |
| Windows | `9715294261` | `0153348a0bddf91350531a7a1d640d506634fd41a2161af0f3ba306a43002ce9` | `2234/2234` |
| macOS | `9715291915` | `c299b2ef7a621a91e91a27db1e84f24379d09a4cd6b543c8dc6c2ff1b103aeb3` | `2234/2234` |

Secret Scan job `99104665927` passed. Artifact `9715256084`, digest
`715ee6aaf369d8dcfe77459e6001ca3fb5078cee3748ae4ec71c4c53dc5b068b`,
is SARIF 2.1.0 with one run, 208 rules, and 0 results. CodeQL run
[`33254082923`](https://github.com/happys2333/flowspan/actions/runs/33254082923)
and job `99104665832` passed; exact-SHA analysis `1691726029` reports 52 rules
and 0 results. Package jobs were skipped after the Linux test failure, so this
CI run is diagnostic evidence rather than acceptance evidence.

The failure exposed a test sampling race rather than a production defect.
Responder attachment writes the authenticated `FSM1` acknowledgement before it
commits the route as Attached and before the listener publishes the borrowed
attachment into the host media-session directory. The initiator can validate
that acknowledgement, publish its own attached session, and enter participant
renderer preparation during that valid window. Therefore the old fixture could
observe the exact host session while its `IsAttached` flag was still false.
Production does not rely on bilateral attachment at renderer-factory entry: the
participant relies on its verified acknowledgement, while the host separately
waits for its local attachment before Admission.

Test-only commit `ac48ec3aa88aa78f736b5550bc778a5ff4e95abb` changes no production
source. The three renderer-failure rows now make their advertised
"bilateral attachment, then renderer failure" boundary explicit: after locating
both real connection-owned sessions, the test factory awaits both production
`WaitForAttachmentAsync` completions with the generation/deadline token. Only
then does `AttachmentBarrierCompleted` become true and the fixture inject throw,
Missing, or foreign cancellation. `PrepareCount` increments before the barrier
so a barrier cancellation remains diagnosable, and the sampled fields are named
`AttachedAtInjectedFailure`. This is a test-owned synchronization point, not a
claim that production naturally orders host directory publication before
renderer entry.

Representative local commands:

```sh
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Debug --no-build --no-restore \
  --filter 'FullyQualifiedName~VerifiedFsm1AttachmentThenRendererFailureCommitsRejectionBeforeFailClose'
seq 1 40 | xargs -P 8 -I{} sh -c \
  'dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
    --configuration Debug --no-build --no-restore \
    --filter "FullyQualifiedName~VerifiedFsm1AttachmentThenRendererFailureCommitsRejectionBeforeFailClose" \
    --logger "console;verbosity=quiet" >/dev/null'
dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
```

Debug and Release warning-as-error builds completed with zero warnings and
errors. Both full solutions passed `2234/2234`, including Desktop `546/546`,
Platform `219/219`, and Transport `701/701`. The focused Debug renderer theory
passed `3/3`, the Release tracer class passed `11/11`, and 40 fresh Debug
processes running eight at a time passed all three theory rows for `120/120`
case executions in 36 seconds. Format, diff, direct/transitive NuGet
vulnerability, explicit TEST MODE composition, and deterministic protocol-1.7
simulator checks passed. Strict concurrency review found no P0/P1/P2 in the
final change.

At exact SHA `ac48ec3aa88aa78f736b5550bc778a5ff4e95abb`, CI run
[`33254883850`](https://github.com/happys2333/flowspan/actions/runs/33254883850)
and CodeQL run
[`33254883851`](https://github.com/happys2333/flowspan/actions/runs/33254883851)
both completed successfully.

- Test jobs `99106739600` (Ubuntu), `99106739632` (Windows), and `99106739585`
  (macOS) succeeded. Downloaded artifacts each contain 12 TRX files summing to
  `2234/2234`, with failed, error, timeout, and aborted counters all zero:

  | Platform | Artifact ID | Artifact SHA-256 |
  | --- | ---: | --- |
  | Linux | `9715531765` | `cdc77345678598803ab60b65abfc593c8cf1cc0abe403f81dc9ec71e356ded30` |
  | Windows | `9715550246` | `a2b464b994b384d3fc00510ceac4233daa5929e3e45d53d45f6932e11ff1c1e7` |
  | macOS | `9715530823` | `b24cdd125f315b2920a2e78584a7dd4df3d2edfe6b24055c3a241c1a24f4331a` |

- Secret Scan job `99106739552` passed. Artifact `9715493359`, digest
  `0d7458cb357cd019f5e3341043b575c0fc9613284dd002ee92ce539e0cacc2c7`,
  is SARIF 2.1.0 with one run, 208 rules, and 0 results.
- CodeQL job `99106739392` passed. Exact-SHA analysis `1691759476` reports 52
  rules and 0 results; the branch open-alert query returned 0.
- All reproducible unsigned package jobs passed:

  | Runtime | Job | Artifact ID | Artifact SHA-256 |
  | --- | ---: | ---: | --- |
  | `linux-x64` | `99107259801` | `9715568980` | `a6556c80edd98a187b75b3f9c71c8445e79d04e029dfecf7d3c6f53892b5c794` |
  | `win-x64` | `99107259811` | `9715574283` | `e2a81901fd0153bed0a35479186c983c050f6424c67cc8a595e322abd0fa0b04` |
  | `osx-arm64` | `99107259839` | `9715574944` | `3b2782b96ca4fb9ca13b40bc7aa13b8cdeb579cec64c95862ee440762b85a7b0` |

These results close the bilateral-attachment tracer's sampling race only.
Nothing in this checkpoint proves native APIs, physical Devices, signed
packages, notarization, or release acceptance.

### Subsequent pre-directory renderer-failure checkpoint

Test-only commit `58569be3215bbb38a6767398d28c3f428130601a` changes no
production source and expands the managed tracer from eleven to twelve cases.
The fourth renderer theory row deterministically injects failure after the
initiator has validated the authenticated FSM1 acknowledgement, while the host
listener has accepted and attached the route but has not yet called the real
host media-session directory.

Two test-only gates expose and freeze exact production checkpoints. The media
handler wrapper records the accepted attachment and blocks before forwarding to
`AuthenticatedRemoteWindowMediaSessionDirectory.HandleAsync`. At that point the
participant session is attached, the exact host session and binding are visible
but the host session is not attached, and Admission, capture, media send, and
rendering remain zero. The renderer factory immediately throws; the participant
commits an allowlisted `renderer_start_failed` Rejected response. A host wrapper
observes and validates that real response but does not return it to the
coordinator, proving fail-close and Dispose are still zero while the media
handler remains unforwarded. The test then releases the media handler, waits for
the real host attachment, and only then returns Rejected to the coordinator.
Fail-close and Dispose each run once, both attachment handlers settle, and all
renderer, route, directory, handler, lease, channel, control, protection,
permission-observer, Emergency Stop, capture, and media-budget owners converge
to zero. The exact generation cannot be reacquired.

The TDD RED used a new test wrapper over the existing production-listener
injection seam while the old fixture still waited for bilateral attachment: only
the new pre-directory row timed out after 258 ms, while the existing three rows
passed. The minimal GREEN made that row wait only for listener-handler entry and
inject failure before host directory publication; all four rows then passed.
This RED was a missing deterministic test capability, not a production defect.
Final teardown uses bounded waits and nested cleanup so a future fail-close
regression cannot prevent participant or listener shutdown.

Representative local commands:

```sh
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Debug --no-build --no-restore \
  --filter 'FullyQualifiedName~VerifiedFsm1AttachmentThenRendererFailureCommitsRejectionBeforeFailClose'
seq 1 40 | xargs -P 8 -I{} sh -c \
  'dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
    --configuration Debug --no-build --no-restore \
    --filter "FullyQualifiedName~VerifiedFsm1AttachmentThenRendererFailureCommitsRejectionBeforeFailClose" \
    --logger "console;verbosity=quiet" >/dev/null'
dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
```

Debug and Release warning-as-error builds each completed with zero warnings and
errors. Both complete solutions passed `2235/2235`, including Desktop `547/547`,
Platform `219/219`, and Transport `701/701`. The Release tracer class passed
`12/12`; the final four-row renderer theory passed 40 fresh Debug processes,
eight at a time, for `160/160` case executions in ten seconds. Format, diff,
direct/transitive NuGet vulnerability, explicit TEST MODE composition, and the
deterministic protocol-1.7 simulator passed. Strict concurrency review found no
P0/P1/P2 in the final change.

At exact SHA `58569be3215bbb38a6767398d28c3f428130601a`, CI run
[`33256672974`](https://github.com/happys2333/flowspan/actions/runs/33256672974)
and CodeQL run
[`33256672962`](https://github.com/happys2333/flowspan/actions/runs/33256672962)
both completed successfully.

- Test jobs `99111509925` (Ubuntu), `99111509774` (Windows), and `99111509852`
  (macOS) succeeded. Downloaded artifacts each contain 12 TRX files summing to
  `2235/2235`, with failed, error, timeout, and aborted counters all zero:

  | Platform | Artifact ID | Artifact SHA-256 |
  | --- | ---: | --- |
  | Linux | `9716051767` | `289285c772868f160ad5a047636fa86d358168d9d94b0648f62085443b870937` |
  | Windows | `9716058609` | `79c80337bb74cd46ebd6b9f9d6271defa394eee14ed6c4cc15f930c3c1eefb20` |
  | macOS | `9716040256` | `1b1f540b4c4323d5791fce8eb01e78820097ef05fb06302b124fa6c889c8ebb8` |

- Secret Scan job `99111509855` passed. Artifact `9716009828`, digest
  `2e72785b7112256a92051a630f5962476ccf44f29a0fc6d46b19ed8eec104844`,
  is SARIF 2.1.0 with one run, 208 rules, and 0 results.
- CodeQL job `99111509610` passed. Exact-SHA analysis `1691829208` reports 52
  rules and 0 results; the branch open-alert query returned 0.
- All reproducible unsigned package jobs passed:

  | Runtime | Job | Artifact ID | Artifact SHA-256 |
  | --- | ---: | ---: | --- |
  | `win-x64` | `99111979228` | `9716087915` | `255b9a02964c7ece829733202f5410395027e33477de5cd345a61da267267ac7` |
  | `linux-x64` | `99111979244` | `9716082818` | `077ac8b035bb7ddc32e283fd3c0757e0ece119b1233dafc4b18e1d76fd8ffcfd` |
  | `osx-arm64` | `99111979245` | `9716077155` | `68464e38af50df0a3bc644e7ed6c6eae4d80cc704f255a0b13060d40d3a980c5` |

This checkpoint closes only the immediate renderer-failure row between
initiator acknowledgement and host directory publication. The remaining
per-boundary reject, throw, cancel, timeout, revoke, disconnect, and
cleanup-fault matrix remains open. In particular, it does not cover fail-close
while the media handler remains unforwarded: host attachment is deliberately
published before Rejected returns to the coordinator. It is managed loopback
and test infrastructure evidence, not native, physical-Device, signed-package,
notarization, or release evidence.

## Security relevance

- **T05:** complementary one-way success and reversed-grant denial demonstrate
  peer-relative Mirror direction without inventing a reciprocal capability;
  same-session grant downgrade drains the active session.
- **T06:** capture remains closed before Prepare/Ready and attachment complete;
  media/rendering remain closed until final Admission; Driver input and local
  Emergency Stop are exercised. The managed permission-loss case closes
  admission, invokes local Emergency Stop, and converges the host owner graph to
  zero; it drives `Granted` to `Denied` and is not evidence of a real
  operating-system permission transition. Three renderer-preparation rows use
  a test-owned wait for bilateral exact-bound media attachment before injecting
  failure. A fourth row injects failure after initiator acknowledgement but
  before host directory publication, then proves Rejected precedes fail-close;
  neither path admits a participant, capture, media send, or rendering
  authority. The expiry case additionally
  completes Ready and one renderer Prepare before exact deadline equality, then
  admits no participant or active generation. The caller-cancellation case keeps
  the harness alive but cancels the exact Start caller before deadline, with no
  Admission, capture, send, or render.
- **T08:** the success path carries `FSM1` and encrypted media through the real
  production listener and decodes JPEG at the participant.
- **T10:** verified-endpoint attachment reset exposes only
  `media_attachment_failed`, not the socket exception or endpoint details.
  Renderer throw and foreign cancellation expose only `renderer_start_failed`;
  null/Missing exposes only `renderer_unavailable`; exact deadline equality
  exposes only `preparation_expired`. Actual caller cancellation propagates the
  cancellation family and exact caller token instead of a rejection reason.
- **T13:** terminal authenticated-control disconnect, capability revocation,
  managed permission loss, and verified-endpoint attachment reset after TCP
  accept converge the relevant ownership graph to zero. The four renderer
  failures do the same after successful `FSM1`, including the handler-gated
  pre-directory case, with Admission/capture/send/render all at zero.
  Rejected-response cleanup is ordered after response
  commit, retains the request deadline through a maximum-10-second watchdog,
  survives lease disposal, and preserves primary plus cleanup/lifecycle
  failures. Explicit and deadline close share one cleanup; actual linked
  cancellation or deadline expiry remains eager. The test-only expiry case
  observes one media-attachment wait, then one host fail-close and one Dispose;
  it drains renderer, route, directory, handler, lease, channel, and control
  owners without publishing Admission or an active generation, and the old
  generation cannot be reacquired. Actual caller cancellation independently
  produces one fail-close and one Dispose while the harness remains live, then
  drains the same owner graph.
- **T14:** Ready and attachment do not render; final Admission opens rendering,
  and Emergency Stop does not wait for network acknowledgement.
- **T15:** the tracer uses protocol 1.7. Existing protocol tests cover downgrade
  behavior; this evidence adds no physical or packaged downgrade result.

## Explicit non-evidence and remaining gates

The test strategy requires reject, throw, cancel, timeout, revoke, disconnect,
and cleanup-fault coverage at every applicable boundary. This evidence covers
only the twelve current cases above and does not establish that complete matrix.
In particular, the `FSM1` failure case covers an accepted verified-endpoint TCP
connection that resets before the attachment handshake completes, not every
malformed, tampered, timeout, cancellation, listener, or cleanup-fault boundary.
The expiry and caller-cancellation cases cover one post-`FSM1`, pre-Admission
example each; cleanup-fault coverage and the remaining per-boundary cases remain
open.

Tasks 5, 5.5a, 5.5, and 6-10 remain open, as does the long-term Flowspan Goal.
`CreateProduction()` must continue to report Remote Window unavailable; this
document is not evidence that production Remote Window is available.

Hosted Windows, macOS, and Linux execution through `58569be` is managed-loopback
and contract evidence only. There is no evidence here for Windows, macOS, or Linux
native capture/input/protection APIs; physical two-Device operation; signed or
notarized packages; package lifecycle behavior; or full release acceptance.
Those gates require native platform or physical evidence without extrapolating
from hosted or same-host managed loopback runs.
