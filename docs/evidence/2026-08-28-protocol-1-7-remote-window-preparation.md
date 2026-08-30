# Protocol 1.7 Remote Window Preparation Candidate Evidence - 2026-08-28

## Evidence boundary

Classification: **local portable protocol and managed-session candidate**,
**hosted portable contract**, and **unsigned package**.

Branch: `codex/v1-foundation`

Implementation commit: `33b39bb374b9b1a49bd10a272eb08a62331cfcd9`,
based on `b00caa7317610f4bc761cdd68ec9fb357f0b98e7`.

The local commands below ran on the implementation content immediately before
commit. Exact-commit hosted results are recorded separately below. Together they
prove portable managed contracts on the named local and hosted runners. They are
not native API, physical Device, packaged accessibility, signed release, or
production Remote Window evidence.

Task 5.5a remains open. At this exact commit, the production Desktop gave its
authenticated control handler and published listener the same media directory,
and the handler could atomically lease the current generation's Preparation
channel and media session. It did not yet include a coordinator or managed
two-node tracer that consumed the lease.

The later implementation commit `7255f04` adds the verified peer-endpoint
connector, host/participant Preparation components, host coordinator, fixed host
control router, and four deliberately narrow managed two-node tracer scenarios.
Subsequent checkpoints expand that tracer to six cases at `761ac75`, nine at
`fde38b2bae9d02f177fd86e22a8beecb060325e9`, and ten in test-only commit
`0f1f32d0e8ea251194755a5b4d150d3e294433ff`. Test commits `45e2d49` and
`5bb6d08` add an eleventh caller-cancellation case. These checkpoints include
attachment, renderer, exact-deadline, and caller-cancellation fail-close
ordering. Test-only commit `ac48ec3` adds no case; it makes the renderer
fixture's bilateral-attachment-before-injected-failure boundary explicit rather
than assuming responder directory publication precedes renderer entry. Test-only
commit `58569be` adds a twelfth case that freezes the valid earlier window after
the initiator validates the FSM1 acknowledgement but before the host directory
publication. Test-only commit `63a52e5` adds a thirteenth case in which
fail-close and the coordinator/control/directory/route graph drain while one
listener handler remains blocked, after which the delayed attachment is rejected
and the handler settles without resurrection. Test-only commit `5e5f380` adds no
case; it makes responder attachment publication an explicit bounded barrier in
six post-attachment renderer-rejection fixtures. Test-only commit `8841080`
adds a fourteenth case: one active authenticated disconnect with one Emergency
Stop registration-disposal cleanup fault.
Test-only commit `6ff3fefaa667e23f309681fe5fe953ae97bb5861` adds a fifteenth
managed case that composes
renderer rejection and old-generation cleanup with a strictly newer replacement
generation in one Desktop ABA trace. Exact-SHA CI `33266348260` and CodeQL
`33266348243` both passed; detailed artifact evidence is recorded in the managed
tracer document.
Test-only commit `13681fb451df53290496416d11837ffb5435e500` preserves the
fourteenth registration cleanup-fault case and adds a sixteenth capture Emergency
Stop cleanup-fault case. Exact-SHA CI `33267557804` and CodeQL `33267557806`
both passed; detailed artifact evidence is recorded in the managed tracer
document.
Test-only commit `2c6ff3221c494cd7003ad0a55e91c28e473615da` expands that
disconnect cleanup-fault theory from two rows to three and adds a seventeenth
combined case: one active authenticated disconnect encounters both the capture
Emergency Stop and Emergency Stop registration-disposal faults. Exact-SHA CI
`33269125217` and CodeQL `33269125313` both passed; detailed artifact evidence
is recorded in the managed tracer document.
Test-only commit `26cd380091f6fd387173e2565023cbb27a96aab0` expands the same
theory to five rows: the eighteenth injects an input Emergency Stop fault and
the nineteenth injects a host-connection disposal fault. Exact-SHA CI
`33270854982` and CodeQL `33270854935` both passed; detailed job, artifact, and
digest evidence is recorded in the managed tracer document.
Test-only commit `5c50870ee11639ee642781e647b135fdd4fc59f7` expands the theory
to seven rows. The twentieth injects a host fail-close fault after real inner
fail-close, and the twenty-first combines Emergency Stop registration disposal
with host-connection disposal. Exact-SHA CI `33271787570` and CodeQL
`33271787616` both passed; detailed job, artifact, and digest evidence is
recorded in the managed tracer document.
Those extensions are recorded separately in
`docs/evidence/2026-08-28-managed-remote-window-production-tracer.md`; they are not
part of this exact-commit evidence. The complete per-boundary fault matrix,
native adapters, physical-device proof, and release evidence remain absent.
`CreateProduction()` therefore continues to report `native_adapters_unavailable`.

## Candidate scope

- Protocol 1.7 independently gates strict host-to-participant
  `remote-window.prepare` and participant-to-host `remote-window.ready` messages.
  Their outer control envelope admits exactly the ten canonical root properties,
  and `protocol` admits exactly `major` and `minor`. Protocol 1.5 and 1.6 fixtures
  and extension-tolerant readers remain frozen.
- Both messages bind the negotiated version, correlation, Session, Activity,
  directed Devices, requested role, deadline, and domain-separated canonical
  SHA-256 `prepareDigest`. Ready echoes the complete binding and carries one
  terminal result plus an allowlisted bounded reason.
- Prepare/Ready `sentAt` and body deadlines use fixed-width UTC spelling with
  literal `+00:00`, whole-millisecond precision, and the shortest fraction that
  preserves a nonzero millisecond value. Alternate offsets, `Z`, redundant
  zeros, and sub-millisecond spellings fail closed for these 1.7 messages.
- One authenticated control registration reserves at most one Preparation. The
  participant dispatches preparation through an owned deadline/lifetime worker,
  leaving the single authenticated read loop available.
- The host distinguishes `ReadyBuffered` from `ReadyAcknowledged`. A Ready
  observed during Prepare send cannot authorize final Admission or complete the
  caller until the Prepare send commits. State and completion publish at the same
  linearization point, so later Stop or clock observation cannot reverse an
  acknowledged result.
- The participant rejects Admission before Ready send begins and can buffer one
  exact final Admission during an exposed Ready send. It invokes the final local
  boundary only after Ready send succeeds. Failed sends discard buffered state.
- Prepare, Ready, and final Admission check cancellation, Stop, and the absolute
  Preparation deadline at actual wire-send admission. Timer or watchdog latency
  cannot put an already-expired frame on the connection.
- Ready grants no participant, Driver, capture, input, or rendering authority.
  Only the existing exact Admission state can establish the participant binding.
- Desktop networking passes one process-owned media directory to both the
  authenticated handler and published listener. A Ready registration exposes one
  atomic, revocable generation lease over its Preparation channel and transferred
  media session; reconnect cannot retarget an older lease. Revocation callbacks
  register through the generation rather than mutating the caller's execution
  context. Per-invocation markers allow callback-owned synchronous disposal while
  stale or sibling callback contexts still join complete cleanup.
- `AuthenticatedActivitySessionHandler.Changed` remains a synchronous observer
  notification outside the lifecycle lock. An observer may snapshot a Ready route
  or initiate nonblocking work, but must not synchronously wait for a round trip
  that depends on the same dispatcher's read loop. This candidate does not claim
  asynchronous observer isolation.

## Local verification

Environment:

```text
Host: macOS, Apple Silicon, Asia/Hong_Kong
.NET SDK: selected by global.json
Branch: codex/v1-foundation
Verification date: 2026-08-28
```

Commands:

```sh
dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
dotnet test tests/Flowspan.Protocol.Tests/Flowspan.Protocol.Tests.csproj \
  --configuration Debug --no-build --no-restore \
  --filter 'FullyQualifiedName~Flowspan.Protocol.Tests.ControlMessageCodecTests'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Debug --no-restore
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Debug --no-build --no-restore \
  --filter 'FullyQualifiedName~Flowspan.Transport.Tests.RemoteWindowControlMessageCodecTests'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Debug --no-build --no-restore \
  --filter '(FullyQualifiedName~Flowspan.Transport.Tests.RemoteWindowControlSessionTests|FullyQualifiedName~Flowspan.Transport.Tests.RemoteWindowControlSessionConcurrencyTests)'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Debug --no-build --no-restore \
  --filter 'FullyQualifiedName~Flowspan.Transport.Tests.RemoteWindowControlSessionConcurrencyTests'
for iteration in $(seq 1 20); do
  dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
    --configuration Debug --no-build --no-restore \
    --filter 'FullyQualifiedName=Flowspan.Transport.Tests.AuthenticatedRemoteWindowMediaLoopbackTests.BudgetExhaustionRequiresFreshControlHandshakeAndMediaRoute' \
    || exit 1
done
seq 1 96 | xargs -n1 -P4 sh -c '
  dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
    --configuration Debug --no-build --no-restore \
    --filter "FullyQualifiedName~StopNowReturnsBeforeBlockingSinkCancellationCallback|FullyQualifiedName~CancellationCallbackFailureCannotSkipQueueCleanup|FullyQualifiedName~CopiedDispatchContextDoesNotBypassExternalDisposalDrain" \
    --verbosity quiet
' sh
seq 1 64 | xargs -n1 -P4 sh -c '
  dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
    --configuration Debug --no-build --no-restore \
    --filter "FullyQualifiedName~AuthenticatedRemoteWindowConnectionLeaseTests|FullyQualifiedName~GenerationRevocationAllowsReentrantHandlerDisposal|FullyQualifiedName~GenerationRevocationAllowsTaskRunHandlerDisposal|FullyQualifiedName~CopiedGenerationRevocationContextJoinsAfterCallbackReturns" \
    --verbosity quiet
' sh
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
```

Observed results:

- formatting and whitespace verification passed;
- direct/transitive NuGet audit covered all solution projects and reported no
  known vulnerable package;
- generic control-envelope codec tests passed `53/53`, including exact protocol
  1.7 root/protocol fields and explicit protocol-1.5/1.6 extension compatibility;
- focused Remote Window control codec tests passed `140/140`;
- focused Remote Window managed-session tests passed `81/81`, including `62/62`
  concurrency cases;
- additional dispatcher, registration-readiness, connection-generation/lease,
  shared-disposal, and initialization-rollback regressions ran outside those two
  focused filters;
- the complete Debug Transport suite passed `660/660`;
- Release build completed with 0 warnings and 0 errors;
- the complete Release solution passed `2096/2096` across 12 test assemblies,
  including Protocol `75/75`, Transport `660/660`, and Desktop `449/449`;
- three previously timing-sensitive callback/disposal regressions passed in 96
  fresh processes with three cases per process (`288/288`);
- six generation callback, copied-context, and owner-retention regressions passed
  in 64 fresh processes with six cases per process (`384/384`);
- Desktop composition validation passed in explicit TEST MODE; and
- the simulator reported protocol `1.7`, source preserved, target resumed, and
  atomic Swap committed with a redacted receipt.

The media budget recovery theory was also rerun 20 times after removing its
test-only listener-slot race: all four frame/plaintext-by-direction cases passed
on every iteration (`80/80`). This is same-host managed loopback evidence, not a
physical network claim.

## Hosted exact-commit evidence

Implementation commit `33b39bb374b9b1a49bd10a272eb08a62331cfcd9`
passed [CI run `33135891925`](https://github.com/happys2333/flowspan/actions/runs/33135891925),
attempt 1:

- macOS test job
  [`98735658238`](https://github.com/happys2333/flowspan/actions/runs/33135891925/job/98735658238);
- Windows test job
  [`98735658266`](https://github.com/happys2333/flowspan/actions/runs/33135891925/job/98735658266);
- Ubuntu test job
  [`98735658394`](https://github.com/happys2333/flowspan/actions/runs/33135891925/job/98735658394);
- Secret Scan job
  [`98735658175`](https://github.com/happys2333/flowspan/actions/runs/33135891925/job/98735658175);
- `osx-arm64` package job
  [`98736235201`](https://github.com/happys2333/flowspan/actions/runs/33135891925/job/98736235201);
- `linux-x64` package job
  [`98736235219`](https://github.com/happys2333/flowspan/actions/runs/33135891925/job/98736235219); and
- `win-x64` package job
  [`98736235247`](https://github.com/happys2333/flowspan/actions/runs/33135891925/job/98736235247).

Every test job restored locked dependencies, verified formatting, built with
warnings as errors, ran all tests, validated Desktop composition in explicit
TEST MODE, ran the protocol-1.7 simulator, and uploaded TRX evidence. Every
package job verified content-locked tooling, published and smoke-tested a
self-contained target, sealed and compared two reproducible unsigned outputs,
audited direct/transitive dependencies, and uploaded one test package.

Downloaded TRX and Secret Scan artifacts were parsed with XML and JSON parsers.
Artifact digests are the SHA-256 values reported by the GitHub artifact API, and
every artifact is bound to the exact implementation SHA.

| Artifact | ID | Artifact digest | Parsed result |
| --- | ---: | --- | --- |
| Windows TRX | `9672063938` | `6ee5e23b3cce1bca630f9dcdcbb88814193468e684a3f7173b24924bdc622273` | 12 files, 2096/2096 passed |
| macOS TRX | `9672047778` | `0efd3075bcb9a258b3c74b2b05db1601288a84d63acfd5aa5cdc45159a65c821` | 12 files, 2096/2096 passed |
| Linux TRX | `9672058047` | `5437301c8eb412874c913cef11d155e5abfa6e0354686212f8816252261832a9` | 12 files, 2096/2096 passed |
| Gitleaks SARIF | `9672001388` | `87b4867b855ece19b2cd2b899382fab0be8e498962deb006e759f1141221fb0f` | SARIF 2.1.0, 208 rules, 0 results |

Each platform aggregate reported `2096` total, executed, and passed tests, with
every unsuccessful or indeterminate counter zero. Per-project counts were
Desktop 449, Transport 660, Integration 338, Platform 219, Security 131,
Release 71, Domain 60, Protocol 75, Platform.Windows 27, Platform.Linux 27,
Platform.macOS 25, and mDNS 14.

The reproducible version `0.1.147` unsigned-package artifacts are also bound to
that workflow SHA:

| Artifact | ID | Artifact digest |
| --- | ---: | --- |
| `win-x64` unsigned test package | `9672118174` | `111b5e064d2ad25725323b2808ae83d4511161d48862d8b87e02ae8062139285` |
| `linux-x64` unsigned test package | `9672092284` | `f140b98519a2a35ecf1ae202e56b6097e524b8cdd338a503cc72cc2d2a80e774` |
| `osx-arm64` unsigned test package | `9672095676` | `ca624819e946886d7a1475d7870734286364ddadab2aa71b481108f0ca91feaa` |

[CodeQL run `33135891896`](https://github.com/happys2333/flowspan/actions/runs/33135891896),
job [`98735658050`](https://github.com/happys2333/flowspan/actions/runs/33135891896/job/98735658050),
also passed for the exact implementation SHA. CodeQL 2.26.4 analysis
`1685306607` evaluated 52 rules and reported 0 results and 0 open branch alerts.

These hosted results prove portable builds, managed contract behavior, Secret
Scan, CodeQL, and reproducible unsigned packaging on the named runner images.
They do not prove native capture, input, protection, permission, physical-device,
packaged accessibility, signing, notarization, or release readiness.

## Subsequent managed fail-close checkpoint

Implementation commit `fde38b2bae9d02f177fd86e22a8beecb060325e9`
retains the strict protocol-1.7 Preparation state machine and adds a
request-bound participant fail-close watchdog for renderer preparation. After a
real authenticated control session and successful `FSM1`, the managed tracer
independently observes both media sessions as attached to the exact protocol,
Device, Session, and Activity binding. A renderer factory throw, a legal
null/Missing renderer, or a foreign/tokenless `OperationCanceledException` then
synchronously makes the participant generation unavailable before the Rejected
response is observed. The host observes `renderer_start_failed` for throw or
foreign cancellation, or `renderer_unavailable` for null/Missing, before
fail-close. No Admission, capture, media send, or render occurs, and all
owner/route/directory/control counts drain to zero.

The watchdog is bound to the exact request and accepts only a positive remaining
deadline of at most 10 seconds. It survives lease disposal and closes at that
deadline if the host does not. The same request is idempotent; a conflicting
request cannot replace or extend the deadline. Expired, overlong, conflicting,
or time-provider setup failure does not poison the generation and instead
fail-closes eagerly. Actual linked cancellation and deadline expiry also remain
eager. Owner revocation cancels the watchdog, explicit and deadline fail-close
share one cleanup, and primary renderer failure remains visible together with
cleanup or lifecycle failure.

On local macOS arm64 with .NET SDK 10.0.301, both warning-as-error builds passed
with zero warnings and errors; both complete solutions passed `2232/2232`, with
Desktop `544/544` and Transport `701/701`. Ten fresh Debug and ten fresh Release
renderer-theory processes passed `60/60` case executions; focused connection-
lease and media-session suites passed `16/16` and `28/28`, respectively, in each
configuration. Format, diff, direct/transitive dependency vulnerability,
explicit TEST MODE composition, and simulator checks passed. There is no local
`gitleaks` result. Exact-SHA CI `33249181870` and CodeQL `33249181871` for
`fde38b2` both succeeded. Downloaded Windows, macOS, and Linux artifacts each
contain 12 TRX files summing to `2232/2232`, with every non-success counter
zero. Secret Scan and all three reproducible unsigned package jobs also passed.
These remain managed contract and packaging results, not native or physical
Remote Window evidence.

## Subsequent test-only preparation-expiry checkpoint

Test-only commit `0f1f32d0e8ea251194755a5b4d150d3e294433ff` changes no
production source. Its tenth managed tracer case completes real authenticated
protocol 1.7 with a signed, verified endpoint, successful `FSM1` and Ready, one
renderer Prepare, and independently verified bilateral media attachment with the
exact protocol, Device, Session, and Activity binding. A coordinator-only
`MutableClock` then advances exactly to the request deadline. Existing production
`EnsurePreparationIsCurrent` treats equality as expired and produces the
allowlisted `preparation_expired` result before Admission or capture.

The test observes one media-attachment wait, zero Admission publication,
capture, media send, and render, followed by one host fail-close and one Dispose.
Snapshot and TerminalFailure are null. ActiveMediaBudget is null because no
active generation was published; the test does not claim to observe a pending
budget. Renderer, route, directory, handler, lease, channel, and control owners
drain to zero, and the old generation cannot be reacquired.

Local macOS focused Debug and Release runs passed `1/1`, the full managed tracer
class passed `10/10` in each configuration, both warning-as-error builds
completed with zero warnings and errors, and both complete solutions passed
`2233/2233`, including Desktop `545/545` and Transport `701/701`. Format, diff,
direct/transitive dependency vulnerability, explicit TEST MODE composition, and
simulator checks passed. Internal strict review reported no P0/P1/P2 finding but
is not an external audit. Superseding SHA
`e504c839cac2e45a4ca7ad17316c8278e4928c2e` passed exact-SHA CI
`33250747660` and CodeQL `33250747671`: each hosted OS passed `2233/2233`, with
Secret Scan and all reproducible unsigned package jobs also passing. These
remain hosted managed contract and packaging results, not native or physical
evidence.

At this checkpoint, only one post-`FSM1`, pre-Admission timeout was covered;
actual caller cancellation, cleanup-fault injection, and the full per-boundary
matrix remained open.

## Subsequent actual caller-cancellation checkpoint

Test commit `45e2d494501167712ec4abdff69d8d232f355d14`, followed by fixture
reliability commit `5bb6d0863033c3b6668335e15d6a6fe336ee46a7`, changes no
production source. After authenticated protocol 1.7, signed candidate
verification, successful `FSM1`/Ready, and exact bilateral attachment, an
independent CTS supplied only to `StartAsync` is cancelled while the harness CTS
keeps connection, run, and cleanup alive and the clock remains before the
deadline. Production surfaces `TaskCanceledException` with the exact caller
token, not timeout, foreign renderer cancellation, or a rejection reason.
Admission, capture, send, and render remain zero; fail-close and Dispose each run
once and all owners drain.

Local focused Debug/Release passed `1/1`, the tracer class passed `11/11`, twenty
fresh Debug processes passed `20/20`, both warning-as-error builds passed, and
both solutions passed `2234/2234`, including Desktop `546/546`, Platform
`219/219`, and Transport `701/701`. Other gates passed, and strict review found
no P0/P1/P2; that is not an external audit. Exact-SHA hosted CI and CodeQL for
`5bb6d08` succeeded in runs `33251741558` and `33251741546`. Each hosted OS
passed `2234/2234` with every non-success counter zero; Secret Scan and all
reproducible unsigned package jobs also passed. These remain managed contract
and packaging results, not native or physical evidence. This covers only one
post-`FSM1`, pre-Admission caller cancellation; cleanup-fault and the full
per-boundary matrix remain open.

## Subsequent renderer attachment-evidence checkpoint

Docs SHA `908a04a2f465bccccf56b72fd36cb5f048506a63` failed Linux CI
`33254082958` in only the renderer `Throw` row. The initiator had validated the
authenticated FSM1 acknowledgement and entered renderer preparation while the
responder listener had not yet published the host directory attachment. That is
a valid scheduling window: responder acknowledgement write precedes route
attachment commit and directory handoff. Windows and macOS each passed
`2234/2234`; Linux passed `2233/2234`. Secret Scan and CodeQL passed, but package
jobs were skipped, so the run is diagnostic rather than acceptance evidence.

Test-only commit `ac48ec3aa88aa78f736b5550bc778a5ff4e95abb` changes no
production source. Its renderer fixture locates both real connection-owned media
sessions and awaits both bounded `WaitForAttachmentAsync` completions before it
injects throw, Missing, or foreign cancellation. The test records an explicit
completed barrier and attachment-at-injected-failure state; this is test-owned
synchronization, not a new production happens-before rule. Debug and Release
solutions passed `2234/2234`, the final focused pressure passed `120/120`, and
strict review found no P0/P1/P2.

Exact-SHA CI `33254883850` and CodeQL `33254883851` succeeded. Windows,
macOS, and Linux each passed `2234/2234`; Secret Scan and all three reproducible
unsigned package jobs passed. This restores the advertised post-bilateral-
attachment renderer-failure evidence. It does not cover immediate renderer
failure after initiator acknowledgement but before host directory publication;
that concurrency row remained open at this checkpoint.

## Subsequent pre-directory renderer-failure checkpoint

Test-only commit `58569be3215bbb38a6767398d28c3f428130601a` adds no
production source and expands the managed tracer to twelve cases. A test wrapper
blocks the production listener handler after the authenticated FSM1
acknowledgement and route attachment but before it forwards the attachment to
the real host directory. The participant is attached while the exact host
session is observed but remains unattached. Immediate renderer failure then
commits a real allowlisted Rejected response; a second test gate proves the host
has validated that response while coordinator fail-close and Dispose remain
zero. The test publishes the real host attachment before allowing the response
to return, after which fail-close and Dispose each run once and every tracked
owner drains. Admission, capture, media send, and rendering remain zero. This
does not claim that fail-close itself occurs before host publication; that is a
separate cleanup-race boundary.

The TDD RED timed out only the new row at 258 ms while the existing three
renderer rows passed. Final local Debug and Release solutions passed
`2235/2235`, including Desktop `547/547`; the four-row renderer theory passed 40
fresh Debug processes at eight-way concurrency for `160/160`. Format, diff,
direct/transitive NuGet vulnerability, explicit TEST MODE composition, and
simulator checks passed. Strict review reported no P0/P1/P2; that is not an
external security audit.

Exact-SHA CI `33256672974` and CodeQL `33256672962` succeeded. Windows, macOS,
and Linux each passed `2235/2235` with every non-success counter zero. Secret
Scan, CodeQL analysis, and all three reproducible unsigned package jobs passed.
These remain managed-loopback contract and packaging results, not native,
physical-Device, signed, notarized, or release evidence. This closes only the
pre-directory renderer-failure row; the complete fault matrix remains open.

## Subsequent fail-close-before-publication checkpoint

Test-only commit `63a52e5e7d2cbba7555a084bc6fa389dba6b5dd9` adds no
production source and expands the managed tracer to thirteen cases. The fifth
renderer row holds the real listener handler before host directory publication
while the real Rejected response returns to the coordinator. Start, fail-close,
Dispose, and the coordinator's control, directory, route, lease, and local-
resource cleanup all finish with that one listener handler still blocked,
`ForwardCount == 0`, and zero Admission, capture, send, or render. Releasing the
gate afterward produces the expected `MediaAttachment`-stage
`InvalidDataException` for a stale attachment with no live owning control
connection. A second cleanup observation proves no route or owner resurrection.
The row does not construct a replacement generation, so it is not replacement
or ABA evidence.

The TDD RED passed the existing four renderer rows and failed only the new row
after 29 ms because its boundary-specific cleanup orchestration was not yet
present. Final focused Debug and Release passed `5/5`; the tracer class passed
`13/13` in both configurations; and 40 fresh, eight-way concurrent processes
passed all five rows for `200/200`. Both warning-as-error builds completed with
zero warnings and errors, and both complete solutions passed `2236/2236`, including Desktop
`548/548`, Platform `219/219`, and Transport `701/701`. Strict review reported
no P0/P1/P2 finding. This is test-only orchestration evidence, not a production
defect fix or new security control.

The first complete Release validation run also exposed responder
`SendEpoch == 1`: the client could consume response bytes before the responder's
post-flush local epoch continuation. Focused stress separately observed
initiator `SendEpoch == 3`, proving the second call could start after the first
completed instead of joining one pending request. Test-only commit
`0e573907c30cf34b97339a1dd79ee8d3ca824399` starts both `RekeyAsync` calls
before server receive and uses a marker returned by that receive loop as the
responder-transition barrier. The production send gate already covers response
write through epoch advance, so this fixes two test-ordering gaps without a
production source change; 200 fresh alternating Debug/Release processes passed.

Exact HEAD `0e573907c30cf34b97339a1dd79ee8d3ca824399` has CI run
[`33259599324`](https://github.com/happys2333/flowspan/actions/runs/33259599324)
and CodeQL run
[`33259599282`](https://github.com/happys2333/flowspan/actions/runs/33259599282)
successfully completed. Each hosted OS passed `2236/2236` with every non-success
counter zero; Secret Scan, CodeQL analysis, and all three reproducible unsigned
package jobs passed. Exact artifact IDs and SHA-256 digests are recorded in the
managed tracer evidence. This checkpoint closes only the cleanup-before-
publication renderer row; the full fault matrix and every native, physical,
signed/notarized, and release gate remain open.

## Subsequent exact-binding ABA Transport checkpoint

Test-only commit `ba58562aff020e3cd9fcc5c8066bcfe74d692b8b` adds no
production source and does not expand the thirteen-case managed Desktop tracer.
It independently exercises the protocol-1.7 authenticated media directory after
an accepted old `FSM1` attachment is blocked between route attachment and
directory publication. The old control generation fully drains; the same Device
pair reconnects with higher generations, prepares a replacement route for the
same Session and Activity with a fresh Route ID, and completes real `FSM1`
acceptance so that route is Attached while directory publication remains gated.
Releasing only the old gate rejects its stale exact binding without attaching to
or stopping the replacement. The replacement gate can then publish the new
binding and exchange one encrypted media frame before all owners drain.

The correct two-gate harness was GREEN against the existing production guard.
A separate fixture RED with one shared gate failed with expected 1/actual 2, and
removing the exact-binding inequality guard made the bounded focused test fail;
these prove harness capability and mutation sensitivity, not a production defect.
Final local results were focused `1/1`, class `29/29`, Transport `702/702`, 80
fresh Debug processes `80/80`, and both solutions `2237/2237`. Exact-SHA CI
[`33261748925`](https://github.com/happys2333/flowspan/actions/runs/33261748925)
attempt 1 records a macOS runner exit 137 at format before any build or tests.
Attempt 2 reran the unchanged SHA and passed `2237/2237` on every hosted OS,
Secret Scan, and every unsigned package job. CodeQL
[`33261748927`](https://github.com/happys2333/flowspan/actions/runs/33261748927)
also passed with 52 rules, 0 results, and 0 open alerts. Full job, artifact, and
digest evidence is in the Transport candidate document.

This closes one Transport exact-binding replacement-generation row only. It is
not a full Desktop renderer-to-replacement trace, and the remaining replacement/
ABA, per-boundary, combined-failure, native, physical, and release evidence stays
open.

## Subsequent renderer-rejection attachment-barrier checkpoint

CI `33262767594` for documentation SHA
`124b1a0c8325d7b469702682f8b7f14c1aebfa54` passed Ubuntu and Windows
`2237/2237` but failed one macOS test. The initiator could verify the real `FSM1`
acknowledgement and enter Missing-renderer rejection while the responder was
still between acknowledgement write and host-directory attachment publication.
The fixture then completed response cleanup and closed the responder's still-
borrowed stream. This was a test-ordering gap, not a product failure.

Test-only commit `5e5f380393a46021d8106a7f3fa817d3b7ac3765` leaves the
managed tracer at thirteen cases and changes no production source. Six renderer-
rejection fixtures now assert the exact rejection first, then use a bounded,
cancellable responder attachment-publication barrier before initiating cleanup.
A temporary 100-ms acknowledgement-to-publication delay reproduced the hosted
exception deterministically; the barrier passed under that probe, after which the
instrumentation was removed and the production diff returned to empty. Final
local class Debug/Release passed `17/17`, 40 fresh alternating processes passed
`680/680`, Desktop passed `548/548`, and both solutions passed `2237/2237`.

Superseding exact-SHA CI
[`33263840825`](https://github.com/happys2333/flowspan/actions/runs/33263840825)
passed `2237/2237` on every hosted OS, Secret Scan, and all three unsigned package
jobs. CodeQL
[`33263840823`](https://github.com/happys2333/flowspan/actions/runs/33263840823)
passed 52 rules with 0 results and 0 open alerts. Exact jobs, artifacts, and
digests are recorded in the managed tracer evidence. The fixture barrier does
not create a production guarantee that acknowledgement verification and
responder directory publication are one event.

## Subsequent authenticated-disconnect cleanup-fault checkpoint

Test-only commit `8841080d8cfbfa3714b3cb7c6d858396ceb756b8` changes no
production source and expands the managed tracer to fourteen cases. After real
protocol-1.7 Preparation, `FSM1`, Admission, encrypted media, and one render, the
participant authenticated-control disconnects. Emergency Stop registration
disposal first removes its callback and then throws one injected `IOException`.
The same failure remains observable through `TerminalFailure` and coordinator
Dispose, while capture/input Emergency Stop and every later managed media,
control, renderer, protection, permission, budget, route, and handler owner
drain.

Local focused Debug/Release passed `1/1`, 80 fresh alternating processes passed
`80/80`, the tracer passed `14/14`, Desktop passed `549/549`, and both solutions
passed `2238/2238`; warning-as-error, format, and diff gates passed. Exact-SHA CI
[`33264566458`](https://github.com/happys2333/flowspan/actions/runs/33264566458)
passed `2238/2238` on macOS, Linux, and Windows plus Secret Scan and all three
unsigned package jobs. CodeQL
[`33264566368`](https://github.com/happys2333/flowspan/actions/runs/33264566368)
passed 52 rules with 0 results and 0 open alerts. Full artifact evidence is in
the managed tracer document.

This closes one active authenticated-disconnect by Emergency Stop registration-
disposal cleanup-fault intersection only. Other cleanup owners and combinations,
the remaining per-boundary matrix, a full Desktop replacement trace, and every
native, physical, and release gate remain open.

## Exact-SHA Desktop renderer-to-replacement ABA checkpoint

Test-only commit `6ff3fefaa667e23f309681fe5fe953ae97bb5861` changes no
production source and expands
the managed tracer from fourteen to fifteen cases with
`RendererFailureLateAttachmentCannotRetargetReplacementDesktopGeneration`.
It establishes an old generation over real authenticated loopback TCP and
protocol 1.7, completes `FSM1` route attachment, and gates the accepted old media
handler before host-directory publication. The participant renderer Prepare
throws, the real allowlisted Rejected result is observed before fail-close, and
the old Start and complete coordinator/control/directory/route/lease graph drain
while that handler remains blocked.

The same Device pair reconnects over a second authenticated TCP connection with
strictly higher host and participant control generations. The replacement has a
fresh Session, correlation, and Route ID for the same Activity. It completes
real `FSM1` on the participant and reaches its own independent pre-directory
gate. Releasing only the old gate rejects the stale exact binding with the
expected no-live-owning-control `InvalidDataException`; the replacement remains
current and pending with zero Admission, capture, media send, or render. Releasing
the replacement gate then yields `Applied`, final Admission, capture, and one
BGRA-to-JPEG-to-encrypted-media-to-decode/render transfer. Explicit Stop drains
every remaining managed owner across the replacement graph.

Test sensitivity was demonstrated by a shared-gate RED after 442 ms (old release
also released the replacement) and by a 30-second timeout RED when the production
exact-binding inequality guard was temporarily removed; the guard was restored.
The independent two-gate fixture is GREEN. Two test-owned scheduling gaps found
during validation are now explicit bounded barriers: Completion-to-Exited handler
publication and final renderer-disposal publication.

After those fixture repairs, 160 fresh processes alternating Debug and Release
passed `160/160`. Focused Debug/Release passed `1/1`, the tracer class passed
`15/15`, Desktop passed `550/550`, and complete solutions passed `2239/2239` in
both configurations. Warning-as-error builds completed with 0 warnings and 0
errors. Format, diff, direct/transitive dependency vulnerability, TEST MODE
composition, and simulator checks passed.

Exact-SHA CI `33266348260` completed successfully. The downloaded macOS, Linux,
and Windows artifacts (`9718793256`, `9718793971`, and `9718809301`) each contain
12 TRX files summing to `2239/2239`, with every non-success counter zero. Secret
Scan artifact `9718756058` contains SARIF 2.1.0 with 208 rules and 0 results. The
three reproducible unsigned package jobs and artifacts (`9718829244`,
`9718833748`, and `9718832840`) passed. CodeQL run `33266348243`, job
`99136974922`, also passed; analysis `1692201085` evaluated 52 rules with 0
results, and the exact-commit branch query returned 0 open alerts. Exact job,
artifact, and digest records are in the managed tracer document.

This closes one full Desktop renderer-failure-to-replacement ABA path, not the
complete replacement/ABA or per-boundary fault matrix. Native APIs, physical
Devices, signed/notarized packages, release acceptance, Tasks 5, 5.5a, 5.5 and
6-10, and the long-term Flowspan Goal remain open. `CreateProduction()` must
continue to report `native_adapters_unavailable`.

## Exact-SHA authenticated-disconnect capture cleanup-fault checkpoint

Test-only commit `13681fb451df53290496416d11837ffb5435e500` changes no
production source. It parameterizes the existing fourteenth registration-
disposal cleanup-fault Fact and adds the sixteenth managed tracer case. Both
rows complete real protocol 1.7, `FSM1`, final Admission, encrypted media, and
one render before the participant authenticated-control connection disconnects.

In the new row, capture `EmergencyStopNow` first clears its owner and then throws
one injected `IOException`. Production exposes the stable
`InvalidOperationException` reason `capture=local_boundary_exception`; input and
sharing-session Emergency Stop are confirmed in the same round. The projection
has no inner exception, and its `ToString()` does not disclose the injected
message. Later ordinary capture and input Stop each execute once, capture and
input Emergency Stop each execute twice, and sharing-session disconnect executes
three times. Every renderer, protection, permission, budget, media-directory,
route, Emergency Stop registration, handler, channel, connection, and
current/retained control owner drains. `TerminalFailure` and the first coordinator
disposal explicitly observed by the test share the same projected exception
instance.

The initial TDD fixture without injectable capture behavior reached its
20-second bound with one parameter row passing and one failing. The one-shot
throw then showed that the draft raw-exception expectation was wrong: production
correctly returned its bounded public projection, and the test was aligned to
that contract. Strict review reported no P0, P1, or P2 finding.

Local focused Debug/Release passed `2/2`; 80 fresh alternating processes
exercising both parameter rows passed `160/160`; the tracer passed `16/16`;
Desktop passed `551/551`; and both complete solutions passed `2240/2240`.
Warning-as-error builds reported 0 warnings and 0 errors. Format, diff,
direct/transitive dependency vulnerability, TEST MODE composition, and simulator
checks passed.

Exact-SHA CI run `33267557804` passed. Downloaded macOS, Linux, and Windows
artifacts `9719127378`, `9719139759`, and `9719155031` each contain 12 TRX files
summing to `2240/2240`, with every non-success counter zero. Secret Scan artifact
`9719100182` contains SARIF 2.1.0 with 208 rules and 0 results. Reproducible
unsigned package artifacts `9719174251`, `9719175384`, and `9719185880` passed.
CodeQL run `33267557806`, job `99140225649`, passed; exact-SHA analysis
`1692249638` evaluated 52 rules with 0 results, and the branch query returned 0
open alerts. Exact job, artifact, and digest records are in the managed tracer
document.

This adds one cleanup owner only. The complete cleanup-owner combination and
per-boundary fault matrix, native APIs, physical Devices, signed/notarized
packages, release acceptance, Tasks 5, 5.5a, 5.5 and 6-10, and the long-term
Flowspan Goal remain open. `CreateProduction()` must continue to report
`native_adapters_unavailable`.

## Exact-SHA combined authenticated-disconnect cleanup-fault checkpoint

Test-only commit `2c6ff3221c494cd7003ad0a55e91c28e473615da` changes no
production source. It expands the authenticated-disconnect cleanup theory from
two rows to three and adds the seventeenth managed tracer case. The new row
completes real protocol 1.7, `FSM1`, final Admission, encrypted media, and one
render before the participant authenticated-control connection disconnects. In
the resulting single fail-close round, capture `EmergencyStopNow` clears its
owner and throws its one-shot fault, and Emergency Stop registration disposal
clears its callback, becomes non-current, and throws its separate one-shot
fault.

The final `TerminalFailure` is an outer `AggregateException` with exactly two
direct inner exceptions in fixed order. Index 0 is the bounded capture
`InvalidOperationException` whose exact reason reports
`capture=local_boundary_exception`, confirms input Emergency Stop and all
sharing sessions disconnected, has a null `InnerException`, and excludes the
capture canary from both its own and the outer aggregate's `ToString()`. Index 1
is the raw registration `IOException` by exact object identity. An additional
deadline-bounded wait observes final aggregate publication before sampling it,
and the first explicitly observed coordinator `DisposeAsync` throws that same
outer aggregate instance.

The combined row preserves the capture row's exact cleanup counts: capture and
input Emergency Stop each execute twice; capture records one failure; capture
and input Stop each execute once; sharing-session disconnect executes three
times; registration disposal, host fail-close, and host disposal each execute
once. Every budget, capture, input, sharing-session, renderer, protection,
permission, registration, media-directory, route, handler, channel, connection,
and current/retained control owner drains.

The TDD RED already contained the combined expectation but deliberately left
the `injectRegistration` predicate unmatched for the combined parameter.
Focused Debug therefore passed exactly two rows and failed one; the combined
row reached its 20-second bound while waiting for the final aggregate. Expanding
only that predicate turned the row GREEN. No production gap or production-source
diff was required, and strict review reported no P0, P1, or P2 finding.

Final local focused Debug and Release runs passed `3/3`. Eighty fresh processes
alternating configurations exercised all three rows for `240/240` passing
cases. The full managed tracer passed `17/17`, Desktop passed `552/552`, and the
complete Debug and Release solutions passed `2241/2241`. Both warning-as-error
builds reported 0 warnings and 0 errors. Format and diff checks, the
direct/transitive dependency vulnerability audit, explicit TEST MODE
composition, and the deterministic protocol-1.7 simulator all passed.

Exact-SHA CI run `33269125217` passed. Downloaded macOS, Linux, and Windows
artifacts `9719588391`, `9719580744`, and `9719595020` each contain 12 TRX files
summing to `2241/2241`, with every non-success counter zero. Secret Scan
artifact `9719549681` contains SARIF 2.1.0 with 208 rules and 0 results.
Reproducible unsigned package artifacts `9719612911`, `9719620448`, and
`9719614617` passed. CodeQL run `33269125313`, job `99144325272`, passed;
exact-SHA analysis `1692310744` evaluated 52 rules with 0 results, and the branch
query returned 0 open alerts. Exact job, artifact, and digest records are in the
managed tracer document.

This closes one combined active-authenticated-disconnect cleanup-fault
cross-product only. Other cleanup owners and cross-products, the complete
per-boundary fault matrix, native APIs, physical Devices, signed/notarized
packages, release acceptance, Tasks 5, 5.5a, 5.5 and 6-10, and the long-term
Flowspan Goal remain open. `CreateProduction()` must continue to report
`native_adapters_unavailable`.

## Exact-SHA input and host-connection cleanup-fault checkpoint

Test-only commit `26cd380091f6fd387173e2565023cbb27a96aab0` changes no
production source. It preserves the historical fourteenth through seventeenth
managed cases and expands the authenticated-disconnect cleanup theory from
three rows to five. Both new rows complete real protocol 1.7, `FSM1`, final
Admission, encrypted media, and one render before the participant authenticated-
control connection disconnects.

The eighteenth row injects a one-shot input `EmergencyStopNow` fault after the
fixture applies the local input Emergency Stop. Production exposes the bounded
`InvalidOperationException` reason with capture confirmed as
`native_capture_emergency_stopped`, input reported as
`local_boundary_exception`, and all sharing sessions confirmed disconnected.
The projection has no inner exception and excludes the injected input canary.
The same projected exception instance remains visible through `TerminalFailure`
and the first explicitly observed coordinator `DisposeAsync`. Exact fixture
proof records one input failure after the Emergency Stop applied, capture and
input Emergency Stop twice each, sharing-session disconnect three times,
capture and input Stop once each, and complete owner drain.

The nineteenth row injects a one-shot host-connection disposal fault only after
the wrapper awaits real inner connection disposal. The raw `IOException` remains
visible by exact identity through `TerminalFailure` and the first explicitly
observed coordinator `DisposeAsync`. Capture and input Emergency Stop each run
once, sharing-session disconnect runs twice, capture and input Stop each run
once, host fail-close and host disposal each run once, and the disposal failure
is observed once. The connection is non-current before the injected throw is
exposed; cleanup preserves that failure and continues until the complete managed
owner graph drains.

The two additions have separate TDD evidence. With the input expectation already
present but its injection deliberately absent, focused Debug passed `3/4`; only
the new input row reached the 20-second bound. Enabling that injection made
Debug and Release pass `4/4`. With the host-connection expectation present but
its injection deliberately absent, focused Debug then passed `4/5`; only the
new connection row reached the same bound. Enabling that post-disposal injection
made Debug and Release pass `5/5`. Strict review reported no P0, P1, or P2
finding after a P2 fixture-proof gap was closed by explicitly proving the input
Emergency Stop applied before its throw and the real inner connection disposed
before its wrapper threw.

Final local focused Debug and Release passed `5/5`. Forty fresh processes
alternating configurations exercised all five rows for `200/200`; the managed
tracer passed `19/19`; Desktop passed `554/554`; and the complete Debug and
Release solutions passed `2243/2243`. Both warning-as-error builds reported 0
warnings and 0 errors. Format and diff checks, direct/transitive dependency
vulnerability audit, explicit TEST MODE composition, and the deterministic
protocol-1.7 simulator passed.

Exact-SHA CI run `33270854982` passed. Windows job `99148948744`, macOS job
`99148948751`, and Linux job `99148948771` produced artifacts `9720102563`,
`9720101275`, and `9720091210`; each downloaded artifact contains 12 TRX files
with `2243/2243` total, executed, and passed, and every non-success counter zero.
Secret Scan job `99148948598` passed; artifact `9720051796` contains SARIF 2.1.0
with 208 rules and 0 results. Reproducible unsigned package jobs `99149440091`,
`99149440092`, and `99149440102` and their artifacts `9720121001`, `9720122674`,
and `9720130752` passed. CodeQL run `33270854935`, job `99148948588`, passed;
exact-SHA analysis `1692380533` evaluated 52 rules with 0 results, and the exact-
commit branch query returned 0 open alerts. Exact artifact digests are recorded
in the managed tracer document.

These additions close one input owner row and one host-connection disposal row
only. Other cleanup owners and cross-products, the complete per-boundary fault
matrix, native APIs, physical Devices, signing/notarization, release acceptance,
Tasks 5, 5.5a, 5.5 and 6-10, and the long-term Flowspan Goal remain open.
`CreateProduction()` must continue to report `native_adapters_unavailable`.

## Exact-SHA host fail-close and combined disposal cleanup-fault checkpoint

Test-only commit `5c50870ee11639ee642781e647b135fdd4fc59f7` changes no
production source. It preserves the historical fourteenth through nineteenth
managed cases and expands the authenticated-disconnect cleanup theory from five
rows to seven. Both additions complete real protocol 1.7, `FSM1`, final
Admission, encrypted media, and one render before the participant authenticated-
control connection disconnects.

The twentieth row makes the host-connection wrapper await real inner fail-close
before throwing one one-shot `IOException`. The coordinator's shared immediate-
terminal/`CleanupCore` task runs once; the raw exception remains observable by
exact identity through `TerminalFailure` and the first explicitly observed
coordinator `DisposeAsync`. Host fail-close executes once and records one
failure. Connection disposal executes once without failure; capture and input
Emergency Stop each execute once; sharing-session disconnect executes twice;
and capture and input Stop each execute once. Cleanup preserves the failure and
drains the complete managed owner graph.

The twenty-first row combines Emergency Stop registration disposal and host-
connection disposal failures in the same `CleanupCore` result. Its terminal
failure is one flat `AggregateException` with exactly two direct inner
exceptions in fixed order: the raw registration `IOException` at index 0 and
the raw connection-disposal `IOException` at index 1, both by exact object
identity. Because both failures arise in the same cleanup result, no partial-
terminal publication wait is needed. Successful host fail-close executes once;
connection disposal executes and fails once; registration disposal executes
once; capture and input Emergency Stop each execute once; sharing-session
disconnect executes twice; and capture and input Stop each execute once. The
same aggregate instance remains visible through `TerminalFailure` and the first
explicitly observed coordinator `DisposeAsync`, and every owner drains.

The two additions have separate TDD evidence. With the host fail-close
expectation present but its injection deliberately absent, focused Debug passed
`5/6`; only the new row reached its 20-second bound. Enabling the post-inner-
fail-close throw made Debug and Release pass `6/6`. With the combined aggregate
expectation present but connection-disposal injection deliberately absent,
focused Debug passed `6/7`; only the new row failed quickly because its actual
terminal type was the registration `IOException`. Enabling the second injection
made Debug and Release pass `7/7`.

Forty fresh processes alternating configurations exercised all seven rows for
`280/280`; the managed tracer passed `21/21`; Desktop passed `556/556`; and the
complete Debug and Release solutions passed `2245/2245`. Both warning-as-error
builds reported 0 warnings and 0 errors. Format and diff checks, direct/transitive
dependency vulnerability audit, explicit TEST MODE composition, and the
deterministic protocol-1.7 simulator passed. Strict review reported no P0, P1,
or P2 finding.

Exact-SHA CI run `33271787570` passed. macOS job `99151415595`, Windows job
`99151415610`, and Linux job `99151415683` produced artifacts `9720344235`,
`9720362485`, and `9720351695`; each downloaded artifact contains 12 TRX files
with `2245/2245` total, executed, and passed, and every non-success counter zero.
Secret Scan job `99151415513` passed; artifact `9720313861` contains SARIF 2.1.0
with 208 rules and 0 results. Reproducible unsigned package jobs `99151947627`,
`99151947629`, and `99151947698` and their artifacts `9720389717`, `9720380455`,
and `9720381237` passed. CodeQL run `33271787616`, job `99151415334`, passed;
exact-SHA analysis `1692416062` evaluated 52 rules with 0 results, and the exact-
commit branch query returned 0 open alerts. Exact artifact digests are recorded
in the managed tracer document.

These additions close one host fail-close owner row and one registration-plus-
connection-disposal cross-product only. Other cleanup owners and cross-products,
the complete per-boundary fault matrix, native APIs, physical Devices,
signing/notarization, release acceptance, Tasks 5, 5.5a, 5.5 and 6-10, and the
long-term Flowspan Goal remain open. `CreateProduction()` must continue to
report `native_adapters_unavailable`.

## 2026-08-30 pending pre-Prepare safety candidate

The current worktree candidate makes the host prerequisite order match the
approved boundary before route selection or Prepare: fresh exact-source `Safe`
protection, a first host-fact revalidation, pure Emergency Stop readiness, a
second host-fact revalidation, exact caller-cancellation barriers, and canonical
deadline currentness. Candidate tests inject unsafe/throwing protection,
unavailable/throwing readiness, cancellation, deadline equality, source or
permission/grant revocation, and authenticated-connection loss at the named
synchronous seams.

Permission, authenticated-connection, protection-read, and readiness throws
reduce to stable canary-free reasons. The finite matrix and current partial or
missing status are in
[`remote-window-production-boundary-matrix.md`](../testing/remote-window-production-boundary-matrix.md).
The synchronous seams do not prove absolute TOCTOU linearization against every
concurrent thread. `CheckReadiness()` also does not reserve the later Emergency
Stop registration, so closing that race remains a Task 5.5a blocker.

The same worktree contains a first macOS permission adapter candidate. Its
prompt-free snapshot calls only `CGPreflightScreenCaptureAccess`; only the
explicit request calls `CGRequestScreenCaptureAccess`. Input remains
`Unsupported`, and the adapter is not composed by `CreateProduction()`.
Operation sequencing, observer isolation, and disposal tests prevent stale
concurrent facts, blocked safety notification, and late publication. It is not
capture, input, protection, physical two-Device, packaged TCC, signing, or
notarization evidence.

Local Debug/Release solution verification passes `2286/2286` tests with zero
build warnings/errors. Exact-SHA CI `33275235290` and CodeQL `33275235305` pass
on evidence commit `92edfff`. Scope, commands, results, artifact digests, and
limitations are in
[`2026-08-30-pre-prepare-safety-and-macos-permission-preflight.md`](2026-08-30-pre-prepare-safety-and-macos-permission-preflight.md).
Tasks 5, 5.5a, 5.5, every native/physical/release gate, and the Goal remain open;
`CreateProduction()` must continue to report Remote Window unavailable.

## 2026-08-30 participant policy and final Admission fault candidate

Implementation `113fce0` bounds participant receive-policy reasons and throws
before any connection/renderer owner, preserves exact caller cancellation across
the production lease's linked token, projects other Admission publication faults
as `host_admission_publish_failed`, and adds a 22nd managed two-node tracer row.
That row waits for participant known-binding publication before injecting a host
side-effect-then-throw; frame admission stays closed, media/render stay zero, and
the directly asserted owners drain.

Focused Desktop host/participant/tracer rows pass `81/81`, focused lease rows
pass `18/18`, Desktop passes `581/581`, Transport passes `704/704`, and complete
Debug/Release solutions pass `2295/2295`. Warning-as-error builds, format, diff,
vulnerability audit, explicit TEST MODE composition, simulator, and final strict
review pass. Exact-SHA CI `33277518618` and CodeQL `33277518619` pass on evidence
commit `158c9a1`, including `2295/2295` on each hosted OS, Gitleaks 208/0, and
CodeQL 52/0. No native/physical/release result is claimed. The finite matrix
keeps P0, AD, HC, Task 5.5a and all later gates open. Full scope and limitations
are in
[`2026-08-30-participant-policy-and-final-admission-faults.md`](2026-08-30-participant-policy-and-final-admission-faults.md).

## Review and remaining evidence

Independent read-only concurrency and acceptance reviews found and drove fixes
for candidate defects before this evidence was recorded:

1. buffered Ready could authorize final Admission before Prepare send success;
2. Stop or a later clock read could reverse an already committed Ready result;
3. deadline timer scheduling latency could let expired Prepare, Ready, or final
   Admission frames reach the wire boundary;
4. a generation revocation callback could deadlock when a child task synchronously
   waited for handler disposal, while stale or sibling callback contexts could
   later bypass the real cleanup drain;
5. cleanup of a duplicate registration that never started dispatch could notify
   shared peers and erase the active registration's Remote Window state;
6. concurrent Desktop runtime disposers could return before the first cleanup and
   fail to observe its error; and
7. protocol-1.7 Prepare/Ready envelopes accepted unknown outer or protocol fields;
8. Desktop initialization rollback could mask its primary failure and skip later
   cleanup when shared media-directory cleanup also failed; and
9. the state-machine evidence did not directly exercise terminal duplicate or
   conflicting Prepare, unknown/cross-request/duplicate/delayed Ready, concurrent
   reservation, or final Admission role drift in both directions.

The final reviewed tree has deterministic regression coverage for each defect and
evidence finding and no reported P0 or P1 finding in the implemented
codec/managed-session scope.

Still required before Task 5.5a can close:

- complete reject, throw, cancel, timeout, revoke, disconnect, and cleanup-fault
  coverage at every applicable production boundary rather than extrapolating
  from the current twenty-one managed tracer cases, including one authenticated-
  disconnect by an Emergency registration cleanup fault, a separate capture
  Emergency Stop cleanup fault, a separate input Emergency Stop cleanup fault,
  one host fail-close cleanup fault, one host-connection disposal cleanup fault,
  one row combining capture and registration faults, one row combining
  registration and connection-disposal faults, one exact-binding Transport
  replacement-generation row, and one full Desktop renderer-to-replacement
  trace;
- add combined failure injection across every production owner and prove all
  cleanup failures remain observable; and
- keep the shipped composition unavailable until Task 5.5 supplies the native
  source, capture, input, protection, renderer, visible sharing, and Emergency
  Stop path.

Native adapters, real permissions, protected surfaces, physical two-Device
quality, packaged accessibility, signing/notarization, Tasks 5.5 and 6-10, v1
release criteria, and the long-term Flowspan Goal remain open.

## 2026-08-30 first bounded cleanup-confirmation vertical

Implementation commit `685225ed92b76ee2e6f4800b9c97f8baf2af378d`
adds the 42nd production-composed managed tracer case and the first bounded
cleanup-confirmation vertical from ADR 0028. It covers one active runtime
generation only. A real same-host TCP pair negotiates authenticated protocol
1.7 and bilateral `FSM1` attachment, reaches
final Admission, sends encrypted media, and renders one frame before the
participant independently closes its authenticated-control connection.

The production host revocation callback closes admission, removes the
generation from active authority, retains it as retiring, creates and arms the
single cleanup timer on the callback thread, and starts one real cleanup task.
The tracer's host-Connection wrapper enters its owned `DisposeAsync` but blocks
before forwarding to the real managed connection, so that owner is observably
unsettled. The timeout does not cancel, detach, replace, or pretend to complete
that real cleanup.

With the injected `TimeProvider` at timeout minus one tick, the original
Connection is still blocked, the generation is still retiring, no terminal
failure is published, and a replacement Start is still pending. Its route,
Prepare, Admission, media, capture, renderer preparation, permission
reservation, Protection reservation, and Emergency Stop readiness/registration
counts remain unchanged. Advancing the final tick to exact ten-second equality
publishes one stable `host_cleanup_timeout` failure and completes the bounded
confirmation. The waiting replacement then fails with
`host_cleanup_unconfirmed`; its connection and Protection input are cleaned up,
but it gains no route, Prepare, Admission, capture, render, media, Driver/input,
permission, Protection, or Emergency Stop authority.

After the test releases the original disposal barrier, the same real cleanup
continues and the two-node managed owner graph drains: the original Connection,
control generation, registrations, protection, renderer, capture/input state,
media sessions, routes, budget, handlers, and peer connections all settle. The
retiring reference clears and the only timer is released. The timeout failure
and monotonic cleanup-unconfirmed latch remain. A second replacement Start is
again rejected before authority, and coordinator disposal observes the same
timeout exception instance.

Local macOS verification for exact implementation `685225e` reports:

- complete Debug and Release solutions: `2584/2584` each;
- Desktop Debug and Release: `720/720` each;
- production-composed managed tracer Debug and Release: `42/42` each;
- Debug and Release warning-as-error solution builds: zero warnings and zero
  errors; and
- format verification, explicit production-composition validation, the
  deterministic protocol-1.7 simulator, and direct/transitive dependency
  vulnerability audit: passed.

Exact implementation CI `33311180093` and CodeQL `33311180128` succeed.
Downloaded artifacts prove `2584/2584` with every non-success counter zero on
each hosted OS, Gitleaks 208/0, CodeQL 52/0 with zero exact-ref open alerts, and
all three reproducible unsigned packages verified. Exact job, artifact, and
digest details are retained in
[`2026-08-30-bounded-cleanup-confirmation.md`](2026-08-30-bounded-cleanup-confirmation.md).

By fault origin, this vertical changes only CL Timeout from M to P. The
authenticated disconnect is the trigger, not a new completion of the
already-partial CL Disconnect cell. The current implementation and case do not
cover a non-cooperative synchronous Emergency Stop prefix before timer arm,
explicit Stop- or Dispose-first termination, timer creation/arming or disposal
failure, real cleanup winning at the deadline, late cleanup fault or
`OutOfMemoryException`, pre-generation cleanup, or the complete per-boundary
reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault matrix.

This is managed same-host loopback evidence, not native capture, input,
protection, permission, renderer, sharing-indicator, or Emergency Stop API
evidence; it is also not physical Windows/macOS/Linux, packaged accessibility,
signing, notarization, or release acceptance. Tasks 5, 5.5a, and 5.5 remain
open, as do every native/physical/signing/notarization/release gate and the
long-term Flowspan Goal. `CreateProduction()` must continue to report Remote
Window unavailable.

## 2026-08-30 external Dispose-first bounded cleanup

Implementation commit `ea984fb01cad46ab128c6d294835df59327aa8ac`
completes only Task 5.5a.3a, the first explicit Dispose-first extension of ADR
0028. This is a deterministic managed Desktop coordinator row, not another
production-composed two-node tracer. The tracer class therefore remains at 42
cases.

The row starts with a stable active generation and an uncontended lifecycle
gate. The first external Dispose sets the disposed gate. Its worker then closes
admission and publishes the exact generation as retiring, together with the one
real cleanup task, bounded confirmation, and sole timer, before any potentially
blocking controller or owner call. The original host Connection disposal stops
at a test-owned barrier, proving that real owner cleanup remains incomplete.

A later authenticated-disconnect callback enters its existing synchronous
capture/input Emergency Stop prefix and attaches cleanup exactly once to the
published operation. It does not create another cleanup task, timer, or owner
graph. Start during disposal preserves `ObjectDisposedException` and cannot
advance route, Prepare, Admission, capture, input, Permission, Authorization,
Protection, or Emergency Stop authority. A post-claim frame is not sent.

At timeout minus one tick, concurrent external Dispose calls, the real cleanup
task, retiring ownership, and the timer remain pending. Exact equality publishes
the stable `host_cleanup_timeout`; concurrent and later callers share the same
public Task and exact exception instance. Releasing the Connection then clears
the retiring owner and timer. Post-drain Dispose still returns the same Task and
failure, and Start remains rejected by the disposed gate.

Local macOS Debug and Release verification reports focused `1/1`, twenty fresh
focused processes `20/20`, coordinator `117/117`, Desktop `721/721`, and
solution `2585/2585`, with warning-as-error builds and all supporting quality
gates passing. Exact-SHA CI `33314229467` and CodeQL `33314229459` succeed.
Downloaded artifacts prove `2585/2585` on every hosted OS with every
non-success counter zero, Gitleaks 208/0, CodeQL 52/0 with zero exact-ref open
alerts, and three reproducible version-`0.1.222` unsigned packages. Exact jobs,
artifacts, digests, commands, and limits are retained in
[`2026-08-30-dispose-first-bounded-cleanup.md`](2026-08-30-dispose-first-bounded-cleanup.md).

This direct evidence stays within the already-Partial CL Timeout cell and does
not promote any matrix status. Explicit Stop-first, lifecycle-gate contention,
cleanup-completion winner and equality races, timer setup/disposal faults, late
non-fatal cleanup failure, fatal OOM, pre-generation cleanup, and every other
active or pending owner remain Task 5.5a.3 work. Tasks 5, 5.5a.3, 5.5a, and 5.5,
every native/physical/signing/notarization/release gate, and the long-term Goal
remain open. The local row and hosted runs are managed contract evidence, not
native API, physical two-Device, signed, notarized, or release proof.
`CreateProduction()` remains unavailable.
