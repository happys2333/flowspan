# Protocol 1.7 Remote Window Preparation Candidate Evidence - 2026-08-28

## Evidence boundary

Classification: **local portable protocol and managed-session candidate**.

Branch: `codex/v1-foundation`

Verified base commit: `b00caa7317610f4bc761cdd68ec9fb357f0b98e7`.

The candidate tree is the commit containing this evidence file. The commands
below ran on its content immediately before commit. They prove managed contracts
on the local macOS host only. They are not Windows, Linux, native API, physical
Device, packaged accessibility, signed release, or production Remote Window
evidence.

Task 5.5a remains open. The production Desktop now gives its authenticated
control handler and published listener the same media directory, and the handler
can atomically lease the current generation's Preparation channel and media
session. No production Preparation coordinator yet consumes that lease with a
verified peer-endpoint connector, prepares the responder route before Prepare,
completes initiator `FSM1` before Ready, owns a participant renderer, or joins the
exact source/capture/input/protection owners in complete two-node cleanup.
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

- a production host/participant Preparation coordinator that consumes the
  generation-bound Preparation/media lease, obtains a verified peer-endpoint
  connector, creates the responder route before Prepare, and completes initiator
  `FSM1` plus renderer readiness before Ready;
- explicit complementary one-way-grant and reversed-grant-negative coverage at
  the trust-bound Preparation boundary;
- the complete managed two-node tracer through final state, one rendered frame,
  authorized Driver input, Emergency Stop, and zero-owner cleanup;
- combined failure injection across every production owner; and
- exact-commit Windows/macOS/Linux CI evidence for this candidate.

Native adapters, real permissions, protected surfaces, physical two-Device
quality, packaged accessibility, signing/notarization, Tasks 5.5 and 6-10, v1
release criteria, and the long-term Flowspan Goal remain open.
