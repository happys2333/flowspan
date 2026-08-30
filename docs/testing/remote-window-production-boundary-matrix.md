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

The current production-composed tracer has **30 xUnit case executions**, not 30
complete boundary families:

- one admitted DriverEligible success;
- one accepted-TCP connection reset before verified `FSM1` attachment completes;
- five renderer-preparation failures;
- one renderer-failure-to-replacement exact-binding/ABA trace;
- one exact caller cancellation after verified attachment;
- one exact deadline-equality expiry after verified attachment;
- three active authority/safety-loss cases;
- seven authenticated-disconnect cleanup-fault cases;
- one final-Admission side-effect-then-throw case after participant known
  binding publication; and
- one reverse-only Mirror-grant rejection;
- one exact-source `R < M < S` reservation invalidation;
- one managed process-local Emergency Stop readiness `R < M < S`
  invalidation;
- one exact Trust/Capability Authorization `R < M < S` invalidation;
- one exact Permission `R < M < S` invalidation;
- one authenticated-control disconnect after exact Connection reservation and
  route selection but before Prepare send-admission entry; and
- one exact media mutation during the post-promotion, pre-capture live-callback
  handoff after verified `FSM1` attachment; and
- two exact Protection `R < M < S` invalidations, for `SecureInput` and
  `Unknown`, after route selection and before successful Prepare send admission.

The first 22 cases' decomposition and exact commands are recorded in the
[managed production tracer evidence](../evidence/2026-08-28-managed-remote-window-production-tracer.md).
The [source-linearization evidence](../evidence/2026-08-30-host-preparation-source-linearization.md),
[Emergency Stop readiness evidence](../evidence/2026-08-30-host-emergency-stop-readiness-reservation.md),
[Trust/Capability evidence](../evidence/2026-08-30-host-trust-capability-preparation-reservation.md),
and [Permission evidence](../evidence/2026-08-30-host-permission-preparation-reservation.md)
record the 23rd through 26th cases respectively. The
[Connection evidence](../evidence/2026-08-30-host-connection-preparation-reservation.md)
records the 27th and 28th cases. The
[Protection evidence](../evidence/2026-08-30-host-protection-preparation-reservation.md)
records the 29th and 30th cases at exact evidence tree `457a2c4`.
These local results are same-host **managed loopback runs on macOS**. Hosted
Windows, macOS, and Linux results remain managed and contract evidence. None of
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
| **P0** | **P** [E-P0] | **P** [E-P0] | **P** [E-P0] | **P** [E-P0, E-TX] | **M** | **M** | **P** [E-P0, E-CL] |
| **P1** | **P** [E-P1] | **P** [E-P1] | **P** [E-P1] | **P** [E-P1, E-TX] | **P** [E-P1] | **P** [E-P1, E-TRACE] | **P** [E-P1, E-CL] |
| **P2** | **C** [E-P2] | **C** [E-P2] | **P** [E-P2] | **M** | **M** | **P** [E-P2] | **P** [E-P2, E-CL] |
| **RS** | **C** [E-RS] | **P** [E-RS] | **C** [E-RS] | **C** [E-RS] | **P** [E-RS] | **P** [E-RS] | **P** [E-RS, E-CL] |
| **AD** | **C** [E-AD] | **P** [E-AD, E-TRACE] | **C** [E-AD] | **C** [E-AD] | **M** | **M** | **P** [E-AD, E-CL] |
| **HC** | **M** | **P** [E-HC, E-TRACE] | **M** | **P** [E-HC] | **M** | **M** | **P** [E-HC, E-CL] |
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
- [`RemoteWindowHostPreparationReservationTests`](../../tests/Flowspan.Desktop.Tests/RemoteWindowHostPreparationReservationTests.cs)
  at commit `294042f` freezes the standalone Desktop core's six opaque fact
  epochs, single-claim bundle, `Collecting` through promotion/terminal phases,
  `M < R`, `R < M < S`, `S < M`, side-effect route failure, all transition
  deadlines, ABA rejection, single terminal outcome, and bounded reasons. The
  [core evidence](../evidence/2026-08-30-host-preparation-reservation-core.md)
  records its local `9/9`, Desktop `590/590`, and solution `2304/2304` results.
  The core is not connected to the coordinator, source registry, permission,
  Trust, authenticated connection, Emergency Stop, protection, or Transport
  send-admission seams, so this evidence changes no H0, H1, TX, HC, or CL cell.
- [`NativeRemoteWindowSourceRegistryTests`](../../tests/Flowspan.Platform.Tests/NativeRemoteWindowSourceRegistryTests.cs),
  [`AuthenticatedRemoteWindowConnectionLeaseTests`](../../tests/Flowspan.Transport.Tests/AuthenticatedRemoteWindowConnectionLeaseTests.cs),
  and [`RemoteWindowControlSessionConcurrencyTests`](../../tests/Flowspan.Transport.Tests/RemoteWindowControlSessionConcurrencyTests.cs)
  at exact commit `3d27389` add the atomic source-invalidation slot,
  generation-bound responder-route operation, and real Transport Prepare
  send-admission hook. The
  [admission-seam evidence](../evidence/2026-08-30-host-preparation-admission-seams.md)
  records local and hosted results. That exact commit does not connect the
  Desktop reservation/coordinator to those seams, so it changes no cell and
  closes neither H0 nor H1.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.SourceInvalidationAfterReservedRoutePreventsPrepareWireAndDrains`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  at exact commit `ec63942` connects the same reservation through the production
  source registry, Desktop coordinator, authenticated route, and actual
  Transport Prepare send-admission hook. It proves Source `R < M < S`: the real
  route is owned, source unregister linearizes, the later send gate returns
  NotDelivered with zero Prepare wire or later authority, and both nodes drain.
  The existing DriverEligible success row traverses the same reservation and
  promotion path. The
  [source-linearization evidence](../evidence/2026-08-30-host-preparation-source-linearization.md)
  records local and hosted exact-SHA results. This source-only evidence does not
  upgrade an aggregate cell.
- [`NativeRemoteWindowContractsTests`](../../tests/Flowspan.Platform.Tests/NativeRemoteWindowContractsTests.cs),
  the focused Emergency Stop rows in
  [`DesktopRemoteWindowHostCoordinatorTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowHostCoordinatorTests.cs),
  and
  [`DesktopRemoteWindowManagedTwoNodeTracerTests.EmergencyStopReadinessLossAfterReservedRoutePreventsPrepareWireAndDrains`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  at exact commit `8e349cc` compose one managed process-local registrar slot
  through the same host reservation. The Platform and coordinator rows cover
  reservation/promotion ownership, all three order shapes, cancellation,
  promotion and cleanup faults, and ABA. The real authenticated loopback row
  proves only Emergency Stop `R < M < S`: loss after route selection prevents
  actual Prepare wire admission and drains both nodes. The
  [Emergency Stop readiness evidence](../evidence/2026-08-30-host-emergency-stop-readiness-reservation.md)
  records exact local and hosted results. This is not native hotkey/action
  evidence and does not upgrade an aggregate cell.
- [`AuthenticatedRemoteWindowMediaSessionsTests.ConnectionLeaseRetainsAuthenticatedHandshakePeerFingerprint`](../../tests/Flowspan.Transport.Tests/AuthenticatedRemoteWindowMediaSessionsTests.cs),
  `SameDeviceIdWithNewKeyCannotRetargetOlderConnectionLease`,
  [`TrustSessionCoordinatorTests`](../../tests/Flowspan.Security.Tests/TrustSessionCoordinatorTests.cs),
  the focused Authorization rows in
  [`DesktopRemoteWindowHostCoordinatorTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowHostCoordinatorTests.cs),
  and
  [`DesktopRemoteWindowManagedTwoNodeTracerTests.AppliedSameMirrorGrantAfterReservedRoutePreventsPrepareWireAndDrains`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  at exact commit `635dc23` bind the handshake fingerprint and exact all-of
  role grant through the Security mutation gate and Desktop reservation. The
  lower-level and focused rows cover exact admission, ABA, both gate orders,
  mutation outcomes, all three host order shapes, failure classification, and
  selected release/cleanup outcomes. The real authenticated loopback row proves
  only Authorization
  `R < M < S`: an Applied same-grant update after route selection invalidates
  the exact reservation, the real Transport send hook admits no Prepare wire,
  and both nodes drain. The
  [Trust/Capability Preparation evidence](../evidence/2026-08-30-host-trust-capability-preparation-reservation.md)
  records exact local and hosted results. This one production-composed order
  does not upgrade an aggregate cell.
- [`MacOSNativeRemoteWindowPermissionBoundaryTests`](../../tests/Flowspan.Platform.MacOS.Tests/MacOSNativeRemoteWindowPermissionBoundaryTests.cs),
  the focused Permission rows in
  [`DesktopRemoteWindowHostCoordinatorTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowHostCoordinatorTests.cs),
  and
  [`DesktopRemoteWindowManagedTwoNodeTracerTests.PermissionRevisionAfterReservedRoutePreventsPrepareWireAndDrains`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  at exact commit `d607ed1` bind the exact permission owner generation,
  revision, capture/input facts, and frozen role through the permission
  observation-commit gate and Desktop reservation. The lower-level and focused
  rows cover exact snapshot/role admission, both commit-gate orders, same-fact
  stability, Revoked/Granted ABA, all three host order shapes, ownership
  transfer, cancellation, failure classification, fatal exhaustion, and
  selected release/disposal faults. The real authenticated managed loopback row
  proves only Permission `R < M < S`: a managed Granted-to-Revoked commit after
  route selection invalidates the exact reservation, actual Transport send
  admission emits no Prepare wire, regrant cannot revive the terminal
  generation, and both nodes drain. The
  [Permission Preparation evidence](../evidence/2026-08-30-host-permission-preparation-reservation.md)
  records exact local and hosted results. The macOS tests prove a controlled
  observation-commit gate, not a real TCC revoke, and the managed tracer does
  not instantiate the macOS boundary. This one production-composed order does
  not upgrade an aggregate cell.
- [`AuthenticatedRemoteWindowConnectionLeaseTests`](../../tests/Flowspan.Transport.Tests/AuthenticatedRemoteWindowConnectionLeaseTests.cs),
  [`AuthenticatedRemoteWindowMediaSessionsTests`](../../tests/Flowspan.Transport.Tests/AuthenticatedRemoteWindowMediaSessionsTests.cs),
  the focused Connection rows in
  [`DesktopRemoteWindowHostCoordinatorTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowHostCoordinatorTests.cs),
  and
  [`DesktopRemoteWindowManagedTwoNodeTracerTests.AuthenticatedControlDisconnectAfterReservedRoutePreventsPrepareWireAndDrains`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  plus `MediaMutationAfterPreparationPromotionTriggersLiveCallbackBeforeCapture`
  at exact commit `259c3bb` bind one registration simultaneously to the exact
  authenticated generation and media-session slots. Lower-level and focused
  rows cover owner-claim rollback, ABA, both route/send exact-owner gates, all
  three coordinator order shapes, generation/media invalidation, live
  generation-plus-media exact-once callback handoff, failure classification,
  cleanup ordering, and fatal exhaustion. The real authenticated disconnect row
  selects a route, makes Connection terminal, prevents all later authority, and
  drains both nodes. It exits before the actual Prepare send-admission hook, so
  its zero admission count is not send-gate rejection evidence; the Transport
  two-lease `RemoteWindowControlSession` regression is the actual send-gate
  evidence. The post-promotion media-mutation row reaches Ready and verified
  `FSM1` attachment, then proves the live callback and Emergency Stop precede
  capture with complete drain. The
  [Connection Preparation evidence](../evidence/2026-08-30-host-connection-preparation-reservation.md)
  records exact local results, successful hosted test/Secret Scan/CodeQL jobs,
  verified reproducible unsigned packages, and limitations. These two narrow
  production rows do not upgrade an aggregate cell.
- [`NativeRemoteWindowContractsTests`](../../tests/Flowspan.Platform.Tests/NativeRemoteWindowContractsTests.cs),
  [`RemoteWindowSessionControllerTests`](../../tests/Flowspan.Platform.Tests/RemoteWindowSessionControllerTests.cs),
  the focused Protection rows in
  [`DesktopRemoteWindowHostCoordinatorTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowHostCoordinatorTests.cs),
  and the two `SecureInput`/`Unknown` executions of
  [`DesktopRemoteWindowManagedTwoNodeTracerTests.ProtectionMutationAfterReservedRoutePreventsPrepareWireAndDrains`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  use implementation commit `c987ca8` and exact evidence/test-stabilization tree
  `457a2c4`. They bind the complete accepted protection observation and
  inclusive freshness interval to one exact source registration. Lower-
  level and focused rows cover identity/freshness, ownership rollback and ABA,
  all three abstract coordinator order shapes, `Temporary → FormalPreStart →
  Live`, the post-`Starting` capture gate, bounded live FIFO/drain/ancestry,
  exact frame/input `ProtectionAdmissionUse`, failure classification, and
  selected cleanup. The two negative real authenticated loopback rows prove
  only Protection `R < M < S`: after route selection, `SecureInput` or `Unknown`
  makes the actual Transport send hook return `NotDelivered`, writes zero
  Prepare, opens no later authority, and drains both nodes. The
  [Protection Preparation evidence](../evidence/2026-08-30-host-protection-preparation-reservation.md)
  records exact local results, successful exact-SHA hosted test/Secret Scan/
  CodeQL/package evidence, artifact digests, and limitations. These narrow
  production rows do not upgrade an aggregate cell.

These tests do not yet inject every source, permission, Trust/grant, connection,
observer-registration, protection, readiness, and route failure independently.
Production-composed Source `M < R` and `S < M`, Authorization `M < R` and
`S < M`, Permission `M < R` and `S < M`, the other Emergency Stop `M/R/S`
orders, Protection `M < R` and `S < M`, remaining Connection and Protection
order/fault intersections, their complete fault matrices, native permission,
native protection, and native Emergency Stop behavior are also still open. That
is why the H0/H1 aggregate cells remain P or M.

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
  directly covers bounded local receive-policy rejection, unknown policy reason
  reduction, policy-throw redaction, stopping/busy state, failed current-lease
  acquisition, linked cancellation, prepared-owner release, peer disconnect,
  and primary-plus-cleanup preservation. The policy reject/throw rows prove zero
  connection acquisition and renderer authority, then recover with a fresh
  request through real loopback `FSM1` Ready.

Current-lease collaborator throws, pending Trust/connection revocation,
authenticated disconnect, and cleanup-fault combinations remain incomplete;
P0 therefore remains partial.

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
- [`DesktopRemoteWindowHostCoordinatorTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowHostCoordinatorTests.cs)
  proves unexpected and foreign-token Admission publication failures reduce to
  `host_admission_publish_failed`, while exact caller cancellation retains its
  original token and the asserted host owner graph fails closed.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.AdFinalAdmissionSideEffectThenThrowFailsClosedAndDrainsBothNodes`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  waits until the participant endpoint commits and publishes its known binding,
  then injects a host-side publication throw. Frame admission remains closed,
  media/render stay zero, the asserted owners across both nodes drain, and the
  old generation cannot be reacquired.
- [`AuthenticatedRemoteWindowConnectionLeaseTests`](../../tests/Flowspan.Transport.Tests/AuthenticatedRemoteWindowConnectionLeaseTests.cs)
  proves linked Admission cancellation is normalized back to the exact caller
  token without relabelling a foreign cancellation.

A participant endpoint throw, authority revocation, authenticated disconnect,
and remaining wire/cleanup phase variants are still missing; the Throw cell is
therefore partial rather than complete.

### E-HC — host commit after Ready

- [`DesktopRemoteWindowHostCoordinatorTests.StartKeepsFramesClosedUntilFinalAdmissionIsPublished`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowHostCoordinatorTests.cs)
  proves the happy-path order with frame admission closed.
- The same test file's `ExpiredPreparationAfterMediaAttachmentNeverStartsCapture`
  proves one post-attachment deadline failure and its cleanup.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  proves the admitted success, one cancellation/expiry before Admission, and a
  final state publication side-effect-then-throw after participant known binding,
  but does not inject every individual HC commit call.

Direct negative/throw/cancel/revoke/disconnect cases are still required for each
post-Ready host revalidation, the remaining protection promotion/capture-start/
live intersections, Emergency Stop registration, controller `Start`, exact
`AddParticipant`, and final open. State publication Throw now has direct
evidence, but its revoke/disconnect and cleanup variants remain partial. In
particular, a failure before HC is not HC evidence.

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
directory, controller, protection FIFO/admission-use, permission-observer,
sharing-session, Emergency Stop, and control-owner fault injections and their
meaningful combinations remain open. No production cleanup timeout contract has
been defined or directly tested.

### E-TRACE — production-composed managed loopback

- [`DesktopRemoteWindowManagedTwoNodeTracerTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  is now the executable 30-case class.
- The [managed tracer evidence record](../evidence/2026-08-28-managed-remote-window-production-tracer.md)
  records the first 22 cases' exact local/hosted commands, artifacts, results,
  and limitations. The source-linearization, Emergency Stop readiness,
  Trust/Capability, and Permission evidence above record the 23rd through 26th
  cases and their exact-SHA execution. The Connection Preparation evidence
  records the 27th and 28th cases. The Protection Preparation evidence records
  the 29th and 30th cases and keeps their narrow scope explicit.
- The [protocol-1.7 Preparation evidence](../evidence/2026-08-28-protocol-1-7-remote-window-preparation.md)
  records the broader Task 5.5a checkpoint and explicitly keeps the task open.

E-TRACE can strengthen a cell only when its case injects that exact production
boundary. Its successful end-to-end route cannot fill the other cells by
inference.

## Remaining implementation and evidence work

The cross-thread H0/H1 ordering contract is frozen in
[ADR 0027](../adr/0027-remote-window-host-preparation-reservation.md). Its
Desktop-only core and deterministic state-machine evidence exist at `294042f`.
The Source `R < M < S` vertical now crosses the real source mutation,
authenticated route, Transport send-admission, and owner-cleanup boundaries at
`ec63942`. The Emergency Stop `R < M < S` vertical similarly crosses a managed
process-local registrar, the real authenticated route, Transport send admission,
and owner cleanup at `8e349cc`. Authorization `R < M < S` crosses the
handshake-derived fingerprint, Security mutation gate, real authenticated route,
Transport send admission, and owner cleanup at `635dc23`. Permission
`R < M < S` crosses the exact accepted-observation commit gate, real
authenticated route, Transport send admission, and owner cleanup at `d607ed1`.
Authenticated Connection now has an exact generation-and-media composite
reservation, exact route/send owners, all three focused host order shapes, one
post-route authenticated disconnect, and one post-promotion media-mutation live
handoff at `259c3bb`. The disconnect row does not enter the actual send hook;
the Transport two-lease regression supplies that separate gate evidence.
Protection now has an exact full-observation/freshness registration, formal
capture-start gate, live FIFO, and exact frame/input use scopes, plus one
production-composed `R < M < S` vertical implemented at `c987ca8` and recorded
at exact evidence tree `457a2c4`. The remaining production-composed Source,
Permission, Authorization, Emergency Stop, Connection, and
Protection orders/fault intersections; native permission, protection, and
Emergency Stop behavior; and the complete per-boundary owner/fault evidence
remain required. Neither the ADR, its isolated core, nor these narrow fact rows
promote an aggregate matrix cell by themselves.

The next tests must use the family IDs in their names or evidence notes and add
one direct row for each applicable gap. The presently known gaps are:

1. **H0:** finish injected throws from the remaining initial fact sources and
   authenticated-disconnect coverage before route selection. The deterministic
   source, permission, grant, and connection revocation barriers do not prove an
   atomic reservation across arbitrary concurrent threads. Source and
   Authorization now each have one real `R < M < S` vertical, and Permission
   has one managed production-composed `R < M < S` vertical over its exact fact
   gate. Add their production-composed `M < R` and `S < M` orders and remaining
   fault intersections, add real native permission observation evidence, and
   extend the exact authenticated Connection gate through the remaining
   production-composed mutation, disconnect, and cleanup-fault intersections.
   The current Connection disconnect tracer terminates before actual send
   admission and cannot stand in for those missing rows.
2. **H1:** finish the safety/route reject and throw variants; extend connection
   loss through each exact route/send phase beyond the one current post-route
   disconnect; preserve any route side effect while cleanup also fails; finish
   the other production-composed Emergency Stop orders and fault variants;
   prove native Emergency Stop registration/action behavior; and complete the
   remaining Protection promotion, capture-start, live FIFO/admission-use,
   source-loss, and cleanup-fault intersections plus native probe behavior. Add
   production-composed Protection `M < R` and `S < M`; the current Protection
   tracer covers only `R < M < S`.
3. **TX/RS:** cover throw, revoke, disconnect, and cleanup failure at every
   distinct send-admission, buffered-response, terminal-commit, completion-hook,
   and tombstone phase rather than treating Stop as every terminal cause.
4. **P0:** current-lease collaborator throw; current Trust/connection revoke and
   disconnect while participant preparation is pending; exact cleanup and retry
   denial.
5. **P1:** malformed/tampered `FSM1`, blocked-handshake cancellation and exact
   timeout, endpoint/listener disconnect variants, generation revocation at
   each attach phase, and remaining handler/route/directory cleanup failures.
6. **P2:** exact deadline cancellation while renderer preparation is blocked;
   authoritative lease/Trust revocation at that boundary; disconnect and all
   renderer cleanup combinations.
7. **AD:** participant-endpoint throw, authority revoke, authenticated
   disconnect, and remaining cleanup failures at the exact buffered/send/commit
   phases.
8. **HC:** independent rejection and throw injection for every revalidation,
   protection/Emergency owner registration, controller `Start`,
   `AddParticipant`, remaining Admission state-send variants, and final open;
   cancellation,
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
