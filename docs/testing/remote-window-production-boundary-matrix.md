# Remote Window production-boundary fault matrix

## Purpose and evidence boundary

This document freezes the finite managed production-boundary matrix required to
close Native Remote Window Task 5.5a. It turns the broad requirement to test
"every boundary" into eleven named boundary families and seven fault families.
It is a coverage inventory, not a declaration that Task 5.5a is complete.

The normative sources are
[NR2.5-NR2.12 and NR10](../../specs/v1/native-remote-window/requirements.md),
the [Native Remote Window design](../../specs/v1/native-remote-window/design.md),
and the [test strategy](test-strategy.md). A new boundary that cannot be assigned
to exactly one family below must first extend this document; it must not be
silently treated as covered by an adjacent row.

The current production-composed tracer has **21 xUnit case executions**, not 21
complete boundary families:

- one admitted DriverEligible success;
- one accepted-TCP connection reset before verified `FSM1` attachment completes;
- five renderer-preparation failures;
- one renderer-failure-to-replacement exact-binding/ABA trace;
- one exact caller cancellation after verified attachment;
- one exact deadline-equality expiry after verified attachment;
- three active authority/safety-loss cases;
- seven authenticated-disconnect cleanup-fault cases; and
- one reverse-only Mirror-grant rejection.

The decomposition and exact commands are recorded in the
[managed production tracer evidence](../evidence/2026-08-28-managed-remote-window-production-tracer.md).
Its local result is a same-host **managed loopback run on macOS**. Hosted
Windows, macOS, and Linux runs remain managed and contract evidence. None of
them is native API, physical two-device, signed-package, or notarization
evidence. `CreateProduction()` must continue to report Remote Window unavailable
until the native runtime gates are independently satisfied.

## Boundary families

| ID | Finite production boundary family | Included calls and decisions | Authority that must remain closed on failure |
| --- | --- | --- | --- |
| **W** | Wire schema and binding | Protocol version; canonical Prepare/Ready codec; direction and authenticated sender/recipient; Session, Activity, Device, role, deadline, digest, and allowlisted reason validation | Transaction reservation, media route, participant preparation, capture, Driver, input, and rendering |
| **H0** | Initial host facts | Exact current source lease/generation; authenticated current connection; current Trust and peer-relative Mirror grant; prompt-free capture and requested-role permission facts | Responder route selection, Prepare, capture, controller, and participant membership |
| **H1** | Host pre-Prepare safety and route | Fresh `Safe` protection; independent Emergency Stop readiness; responder media-route selection and ownership before Prepare | Prepare, native capture, controller, participant membership, and final Admission |
| **TX** | Preparation transaction | Prepare send admission; the single pending transaction; exact Ready matching; deadlines; terminal outcome and tombstone; replay, duplicate, and concurrency handling | A second transaction, reused route/session/correlation, capture, participant membership, and Admission |
| **P0** | Participant policy and current lease | Current authenticated participant connection/Trust; local recipient and receive policy; exact request binding; current generation-bound connection lease | Media connect, renderer preparation, Ready success, and rendering |
| **P1** | Participant media attachment | Verified endpoint connector; generation-bound initiator route; `FSM1` attachment and acknowledgement; exact media binding | Renderer preparation, Ready success, capture, Admission, and rendering |
| **P2** | Participant renderer worker | Deadline/lifetime-owned renderer preparation; Missing/null, throw, and cancellation classification; prepared renderer ownership | Ready success and all rendering |
| **RS** | Ready send and response completion | Ready/Rejected send admission and commit; at-most-one buffered Admission; response-completion hook; deferred versus eager fail-close ordering | Participant admission, host capture, and reuse of a fail-close-pending generation |
| **AD** | Final Admission | Correlated Admission state; `Applied` or `AlreadyApplied`; exact effective role and media binding; participant known-binding publication and renderer-open gate | Participant endpoint invocation before the legal phase and every render before exact Admission |
| **HC** | Host post-Ready commit | Revalidation of every host fact; formal protection and Emergency Stop registration; controller `Start`; exact `AddParticipant`; Admission state publication; final frame-admission open | Native capture disclosure, Driver/input authority, participant membership, and active-generation publication |
| **CL** | Terminal owner graph | Close admission first; fail-close; controller/capture/input/session; active and pending frames/queues; renderer; protection; Emergency Stop; permission observer; attachment/route/directory; connection/control; shared completion and failure aggregation | Restart after unconfirmed cleanup, stale callbacks, retained authority, owner resurrection, and hidden failure |

The families are intentionally ordered by the production flow. A fault is
charged to the boundary that originates it. For example, a malformed deadline
spelling belongs to **W**, deadline equality while a transaction is pending
belongs to **TX**, expiry while host commit is starting belongs to **HC**, and a
timer-disposal exception belongs to **CL**.

## Fault families and status rules

The columns are causal injection families, not CLR exception names:

| Column | Exact meaning |
| --- | --- |
| **Reject** | The boundary returns a valid negative, unavailable, policy-denied, stale, mismatched, or malformed outcome without an unexpected dependency exception. The result must be bounded and non-reflecting. |
| **Throw** | The invoked production dependency unexpectedly throws. Non-fatal exceptions must be projected or retained according to the boundary contract without leaking canary/native details. `OutOfMemoryException` is not converted into a product rejection. |
| **Cancel** | The exact caller or owned lifetime token is cancelled before or while the boundary is in progress. Foreign/tokenless `OperationCanceledException` remains a Throw unless that boundary explicitly classifies it otherwise. |
| **Timeout** | The canonical request deadline reaches equality or passes, or an explicitly specified bounded watchdog expires. It is not inferred from a generic cancellation result. |
| **Revoke** | A previously current Trust, Capability, permission, protection, source, lease, or generation fact becomes invalid through its authoritative state/change path. |
| **Disconnect** | Authenticated control or media transport ownership is lost independently of an authority revocation. |
| **Cleanup-fault** | Stop, fail-close, disposal, callback drain, or another owner-release step fails after or alongside the primary outcome. Every later cleanup still runs and all primary/cleanup failures remain observable with the specified identity and order. |

Each matrix cell has one of these states:

- **C — Covered:** direct automated evidence exercises the production component
  at that family, proves the expected terminal outcome and authority gate, and
  drains every owner created by that scenario. All currently applicable
  sub-boundaries in that cell are represented.
- **P — Partial:** direct production-component evidence exists, but at least one
  applicable sub-boundary, race, owner, combined failure, or terminal assertion
  remains absent. Adjacent unit evidence cannot promote a cell to C.
- **M — Missing:** no direct automated evidence for the required injection was
  found at this production family. A happy-path pass or evidence at another
  family does not count.
- **N/A — Not applicable:** the current contract gives the family no such
  operation or ownership. Every N/A is justified below. If the implementation
  later introduces that behavior, the cell automatically reopens as M.

## Coverage snapshot

Evidence keys link to the production tests below the matrix. Statuses are
conservative: a P is not converted to C merely because a lower-level helper has
similar coverage.

| Boundary | Reject | Throw | Cancel | Timeout | Revoke | Disconnect | Cleanup-fault |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **W** | **C** [E-W] | **N/A** [N1] | **N/A** [N1] | **N/A** [N1] | **N/A** [N1] | **N/A** [N1] | **N/A** [N1] |
| **H0** | **P** [E-H] | **P** [E-H] | **P** [E-H] | **N/A** [N2] | **P** [E-H] | **M** | **P** [E-H, E-CL] |
| **H1** | **P** [E-H] | **P** [E-H] | **P** [E-H] | **P** [E-H] | **P** [E-H] | **M** | **P** [E-H, E-CL] |
| **TX** | **C** [E-TX] | **P** [E-TX] | **C** [E-TX] | **C** [E-TX] | **P** [E-TX] | **P** [E-TX] | **P** [E-TX, E-CL] |
| **P0** | **P** [E-P0] | **M** | **P** [E-P0] | **P** [E-P0, E-TX] | **M** | **M** | **P** [E-P0, E-CL] |
| **P1** | **P** [E-P1] | **P** [E-P1] | **P** [E-P1] | **P** [E-P1, E-TX] | **P** [E-P1] | **P** [E-P1, E-TRACE] | **P** [E-P1, E-CL] |
| **P2** | **C** [E-P2] | **C** [E-P2] | **P** [E-P2] | **M** | **M** | **P** [E-P2] | **P** [E-P2, E-CL] |
| **RS** | **C** [E-RS] | **P** [E-RS] | **C** [E-RS] | **C** [E-RS] | **P** [E-RS] | **P** [E-RS] | **P** [E-RS, E-CL] |
| **AD** | **C** [E-AD] | **M** | **C** [E-AD] | **C** [E-AD] | **M** | **M** | **P** [E-AD, E-CL] |
| **HC** | **M** | **M** | **M** | **P** [E-HC] | **M** | **M** | **P** [E-HC, E-CL] |
| **CL** | **P** [E-CL] | **P** [E-CL] | **P** [E-CL] | **M** | **P** [E-CL] | **P** [E-TRACE, E-CL] | **P** [E-TRACE, E-CL] |

### N/A rationale

- **N1 — W operational faults:** W is a synchronous, deterministic codec and
  binding validator with no I/O, cancellation token, mutable authority, or
  owned cleanup. Its protocol exceptions are the Reject path. Wire-send throws,
  cancellation, deadline expiry, transport loss, and send cleanup are charged
  to TX or RS. Lexical or envelope/body deadline disagreement remains a W
  Reject.
- **N2 — H0 timeout:** H0 facts are prompt-free synchronous snapshots. They do
  not wait or arm a watchdog. An implementation that makes one of these reads
  asynchronous must change this cell to M before merging.

## Direct evidence index

### E-W — codec, fixtures, and authenticated binding

- [`RemoteWindowControlMessageCodecTests`](../../tests/Flowspan.Transport.Tests/RemoteWindowControlMessageCodecTests.cs)
  directly freezes all three protocol-1.7 frames, the domain-separated digest,
  every echoed binding, direction, version, canonical time, allowlisted reason,
  hostile schema, duplicate/unknown field, and downgrade rejection.
- [`remote-window-preparation-v1.7.json`](../../tests/Flowspan.Transport.Tests/Fixtures/remote-window-preparation-v1.7.json)
  is the frozen byte/hash fixture.
- [`RemoteWindowControlSessionTests.RealAuthenticatedPreparationBootstrapsBindingBeforePublishedState`](../../tests/Flowspan.Transport.Tests/RemoteWindowControlSessionTests.cs)
  carries that binding through a real authenticated control session.

### E-H — host initial facts and pre-Prepare safety

- [`DesktopRemoteWindowHostCoordinatorTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowHostCoordinatorTests.cs)
  directly covers the success order; pre-Preparation protocol rejection; unsafe
  protection; unavailable Emergency Stop readiness; redacted protection and
  readiness throws; redacted initial permission and connection-fact throws;
  exact caller cancellation after initial facts and each synchronous safety
  probe; source, permission, grant, and connection revocation at the two
  revalidation barriers; deadline equality before route; Ready rejection; exact
  deadline expiry after attachment; route side-effect failure; a pre-route
  started fail-close blocked and failed during cleanup; and selected active
  revocation/cleanup paths.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.ReverseOnlyMirrorGrantCannotPrepareOrStartCapture`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  proves the peer-relative grant direction. The same file's active authority
  loss theory is terminal evidence, not proof of every H0/H1 pre-route race.

These tests do not yet inject every source, permission, Trust/grant, connection,
observer-registration, protection, readiness, and route failure independently;
that is why the H0/H1 aggregate cells remain P or M.

### E-TX — transaction, tombstone, and deadline state machine

- [`RemoteWindowControlSessionConcurrencyTests`](../../tests/Flowspan.Transport.Tests/RemoteWindowControlSessionConcurrencyTests.cs)
  directly covers one pending transaction, duplicate/conflicting Prepare,
  unknown/cross-request/duplicate/delayed Ready, terminal tombstones, send
  admission, buffered Ready/Admission, caller cancellation, exact deadline
  races, Stop races, worker dispatch, response commit, and cleanup joining.
- [`RemoteWindowControlSessionTests`](../../tests/Flowspan.Transport.Tests/RemoteWindowControlSessionTests.cs)
  supplies authenticated loopback, generation replacement, route drain, and
  session-lifetime integration evidence.

Send/response throws, revocation/disconnect while every distinct transaction
phase is pending, and all cleanup-fault combinations are not complete; those
cells therefore remain P.

### E-P0 — participant policy and current connection lease

- [`DesktopRemoteWindowPreparationPeerTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowPreparationPeerTests.cs)
  directly covers stopping/busy state, failed current-lease acquisition, linked
  cancellation, prepared-owner release, peer disconnect, and primary-plus-
  cleanup preservation.

There is no direct production-peer injection for an explicit local receive-
policy rejection or receive-policy throw, nor a complete pending Trust revoke
or authenticated disconnect matrix. P0 cannot be promoted on the strength of
codec allowlist tests.

### E-P1 — verified connector and FSM1 attachment

- [`AuthenticatedRemoteWindowMediaSessionsTests`](../../tests/Flowspan.Transport.Tests/AuthenticatedRemoteWindowMediaSessionsTests.cs)
  covers exact generation binding, verified peer connect, attachment failure,
  cancellation, revocation, stale/replacement generations, fail-close, and
  shared disposal.
- [`RemoteWindowMediaAttachmentTests`](../../tests/Flowspan.Transport.Tests/RemoteWindowMediaAttachmentTests.cs)
  covers malformed/boundary rejection, expiry, replay, claim races, revocation,
  handler cleanup, and selected combined cleanup failures.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.VerifiedFsm1AttachmentFailureAfterTcpAcceptRejectsWithoutAdmissionOrCapture`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  proves one real accepted-loopback reset before `FSM1` completion.

That tracer row is not malformed, tampered, timed-out, cancelled, listener-
failure, or every cleanup-owner evidence. P1 therefore remains partial in every
fault family.

### E-P2 — participant renderer preparation

- [`DesktopRemoteWindowPreparationPeerTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowPreparationPeerTests.cs)
  covers Missing/null, throw, foreign cancellation, linked disposal
  cancellation, late renderer completion, peer disconnect, and selected
  primary-plus-cleanup combinations.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.VerifiedFsm1AttachmentThenRendererFailureCommitsRejectionBeforeFailClose`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  supplies five production-composed cases after exact bilateral `FSM1`
  attachment and proves zero Admission, capture, send, and render.

Missing/null and ordinary throw classification are covered. A renderer blocked
through exact deadline expiry and a renderer interrupted by authoritative lease
or Trust revocation remain missing; cancellation, disconnect, and cleanup
cross-products remain partial.

### E-RS — Ready send and response completion

- [`RemoteWindowControlSessionConcurrencyTests`](../../tests/Flowspan.Transport.Tests/RemoteWindowControlSessionConcurrencyTests.cs)
  directly covers Ready send admission, committed rejection, failed/non-admitted
  send, completion-hook ordering, response-plus-completion failure, buffered
  Admission, exact cancellation, and deadline equality.
- [`AuthenticatedRemoteWindowConnectionLeaseTests`](../../tests/Flowspan.Transport.Tests/AuthenticatedRemoteWindowConnectionLeaseTests.cs)
  covers deferred fail-close deadlines, explicit/deadline shared cleanup,
  revocation, timer failures, and retained fail-close failure.

The remaining send-throw, pending revoke/disconnect phase variants and cleanup
owner combinations keep the non-C cells partial.

### E-AD — exact final Admission

- [`RemoteWindowControlSessionConcurrencyTests`](../../tests/Flowspan.Transport.Tests/RemoteWindowControlSessionConcurrencyTests.cs)
  covers Admission before Ready send, buffering during Ready send, failed-send
  discard, exact-role drift in both directions, Stop/cancellation/deadline final
  commit races, and publication ordering.
- [`DesktopRemoteWindowPreparationPeerTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowPreparationPeerTests.cs)
  proves no rendering before exact Admission, role mismatch rejection, exact
  caller cancellation, and prepared-owner release.

There is no direct final-Admission send/endpoint throw, authority revocation, or
authenticated disconnect injection at that exact phase. Those cells remain M.

### E-HC — host commit after Ready

- [`DesktopRemoteWindowHostCoordinatorTests.StartKeepsFramesClosedUntilFinalAdmissionIsPublished`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowHostCoordinatorTests.cs)
  proves the happy-path order with frame admission closed.
- The same test file's `ExpiredPreparationAfterMediaAttachmentNeverStartsCapture`
  proves one post-attachment deadline failure and its cleanup.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  proves the admitted success and one cancellation/expiry before Admission, but
  does not inject the individual HC commit calls.

Direct negative/throw/cancel/revoke/disconnect cases are still required for each
post-Ready host revalidation, protection registration, Emergency Stop
registration, controller `Start`, exact `AddParticipant`, state publication,
and final open. In particular, a failure before HC is not HC evidence.

### E-CL — terminal cleanup and failure identity

- [`DesktopRemoteWindowHostCoordinatorTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowHostCoordinatorTests.cs)
  covers active media failure, cancelled/unconfirmed Stop, permission and
  connection revocation, stale callbacks, shared Dispose completion, route
  side-effect failure, restart blocking, and selected cleanup failures.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.AuthenticatedControlDisconnectCleanupFaultDrainsAndRemainsObservable`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  contributes seven cases: registration, capture, input, host fail-close, host
  connection disposal, and two two-fault combinations. It proves the tested
  owner graph drains and the specified raw/projected failure identity remains
  observable.
- [`DesktopRemoteWindowPreparationPeerTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowPreparationPeerTests.cs),
  [`AuthenticatedRemoteWindowConnectionLeaseTests`](../../tests/Flowspan.Transport.Tests/AuthenticatedRemoteWindowConnectionLeaseTests.cs),
  and [`RemoteWindowMediaAttachmentTests`](../../tests/Flowspan.Transport.Tests/RemoteWindowMediaAttachmentTests.cs)
  cover selected participant, lease, route, timer, handler, and combined cleanup
  failures.
- [`NativeRemoteWindowContractsTests`](../../tests/Flowspan.Platform.Tests/NativeRemoteWindowContractsTests.cs)
  covers protection/Emergency callback ownership and non-deadlocking drains, but
  remains portable managed-contract evidence.

The remaining renderer, active/pending frame, queue, attachment, route,
directory, controller, protection, permission-observer, sharing-session,
Emergency Stop, and control-owner fault injections and their meaningful
combinations remain open. No production cleanup timeout contract has been
defined or directly tested.

### E-TRACE — production-composed managed loopback

- [`DesktopRemoteWindowManagedTwoNodeTracerTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  is the executable 21-case class.
- The [managed tracer evidence record](../evidence/2026-08-28-managed-remote-window-production-tracer.md)
  records its exact local/hosted commands, artifacts, results, and limitations.
- The [protocol-1.7 Preparation evidence](../evidence/2026-08-28-protocol-1-7-remote-window-preparation.md)
  records the broader Task 5.5a checkpoint and explicitly keeps the task open.

E-TRACE can strengthen a cell only when its case injects that exact production
boundary. Its successful end-to-end route cannot fill the other cells by
inference.

## Remaining implementation and evidence work

The next tests must use the family IDs in their names or evidence notes and add
one direct row for each applicable gap. The presently known gaps are:

1. **H0:** finish injected throws from the remaining initial fact sources and
   authenticated-disconnect coverage before route selection. The deterministic
   source, permission, grant, and connection revocation barriers do not prove an
   atomic reservation across arbitrary concurrent threads.
2. **H1:** finish the safety/route reject and throw variants; inject connection
   loss at the exact route boundary; preserve any route side effect while
   cleanup also fails; and either introduce an atomic readiness/facts
   reservation or retain the cross-thread TOCTOU gap as a blocker.
3. **TX/RS:** cover throw, revoke, disconnect, and cleanup failure at every
   distinct send-admission, buffered-response, terminal-commit, completion-hook,
   and tombstone phase rather than treating Stop as every terminal cause.
4. **P0:** direct local receive-policy reject and throw; current Trust/connection
   revoke and disconnect while participant preparation is pending; exact
   cleanup and retry denial.
5. **P1:** malformed/tampered `FSM1`, blocked-handshake cancellation and exact
   timeout, endpoint/listener disconnect variants, generation revocation at
   each attach phase, and remaining handler/route/directory cleanup failures.
6. **P2:** exact deadline cancellation while renderer preparation is blocked;
   authoritative lease/Trust revocation at that boundary; disconnect and all
   renderer cleanup combinations.
7. **AD:** final Admission send or participant-endpoint throw, authority revoke,
   authenticated disconnect, and cleanup failure at the exact buffered/send/
   commit phases.
8. **HC:** independent rejection and throw injection for every revalidation,
   protection/Emergency owner registration, controller `Start`,
   `AddParticipant`, Admission state send, and final open; cancellation,
   deadline, revoke, and disconnect races between each step.
9. **CL:** remaining single-owner cleanup faults; meaningful ordered combined
   failures; active versus pending owner variants; stable shared completion;
   zero retained owner/budget counts; and an explicit cleanup-timeout decision.

Task 5.5a remains unchecked while any applicable cell is P or M. Task 5.5,
native platform work, physical two-device runs, package lifecycle, signing,
notarization, and release acceptance remain separate open gates.

## Acceptance rule

Task 5.5a may close only when all of the following are true:

1. Every applicable matrix cell is C; every retained N/A still matches the
   production contract and has a reviewed rationale.
2. Each C links to a deterministic test that injects the named production
   boundary and asserts the bounded result, no premature authority, one terminal
   outcome, complete owner/budget drain, retry/tombstone behavior, and primary
   plus cleanup failure identity/order where applicable.
3. The success row and every fault row run in Debug and Release without sleeps
   as correctness barriers; stress or fresh-process runs supplement but do not
   replace deterministic tests.
4. Windows, macOS, and Linux CI run all portable managed/contract rows and retain
   parseable results. Any matching-host native row is reported separately and
   is never inferred from another operating system.
5. The evidence record names the exact commit, commands, SDK/tool versions,
   case counts, CI job/artifact identifiers, and limitations. No result is
   reported for a host or package that did not run it.
6. A final spec and security review finds no unresolved P0/P1 issue in the
   Preparation scope and confirms that `CreateProduction()` remains unavailable
   until the separate native runtime gates pass.

Reaching this managed Task 5.5a rule still does **not** prove native capture or
input, protected-surface behavior, physical cross-device quality, accessibility,
signed/notarized packaging, or v1 release readiness.
