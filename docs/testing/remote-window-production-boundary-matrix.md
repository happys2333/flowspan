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

The current production-composed tracer has **42 xUnit case executions**, not 42
complete boundary families:

- one admitted DriverEligible success;
- one accepted-TCP connection reset before verified `FSM1` attachment completes;
- five renderer-preparation failures;
- one authenticated-control disconnect after Prepare send admission and
  bilateral `FSM1` attachment while renderer Preparation is non-cooperatively
  blocked;
- one exact participant deadline equality after Prepare send admission and
  bilateral `FSM1` attachment while renderer Preparation is non-cooperatively
  blocked;
- one participant Trust revoke after Prepare send admission and bilateral
  `FSM1` attachment while renderer Preparation is non-cooperatively blocked;
- one renderer-failure-to-replacement exact-binding/ABA trace;
- one exact caller cancellation after verified attachment;
- one exact deadline-equality expiry after verified attachment;
- three active authority/safety-loss cases;
- seven authenticated-disconnect cleanup-fault cases;
- one final-Admission side-effect-then-throw case after participant known
  binding publication;
- one final-Admission authority revoke after participant exact Admission commit
  but before host frame-admission open;
- one authenticated disconnect after participant exact Admission commit but
  before host frame-admission open, with Trust and Mirror grant unchanged;
- one authenticated disconnect while host capture Start still owns the HC
  boundary, after Ready and bilateral attachment but before Admission publish;
- one fingerprint-bound authority revoke while host capture Start still owns HC,
  with the exact Trust identity retained and Mirror authority removed;
- one exact caller cancellation while host capture Start still owns HC, with
  current authenticated authority retained until owned cleanup;
- one bounded capture Start rejection after exact first-frame disposal, with
  authenticated authority unchanged until owned cleanup;
- one authenticated disconnect after a real fingerprint-bound host
  Authorization reservation is acquired but before that reservation is returned
  to the coordinator;
- one authenticated disconnect after the real responder-route side effect but
  before any protocol Prepare call;
- one reverse-only Mirror-grant rejection;
- one exact-source `R < M < S` reservation invalidation;
- one managed process-local Emergency Stop readiness `R < M < S`
  invalidation;
- one exact Trust/Capability Authorization `R < M < S` invalidation;
- one exact Permission `R < M < S` invalidation;
- one authenticated-control disconnect after exact Connection reservation and
  route selection but before Prepare send-admission entry;
- one exact media mutation during the post-promotion, pre-capture live-callback
  handoff after verified `FSM1` attachment;
- two exact Protection `R < M < S` invalidations, for `SecureInput` and
  `Unknown`, after route selection and before successful Prepare send admission;
  and
- one active authenticated-control disconnect whose host Connection disposal is
  held past the production cleanup-confirmation deadline, followed by late
  bilateral drain and permanently latched restart denial.

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
The [pending-renderer disconnect evidence](../evidence/2026-08-30-pending-renderer-authenticated-disconnect.md)
records the 31st case at exact commit `8d0831d`; its exact-SHA CI, CodeQL, and
reproducible unsigned-package jobs pass with the detailed artifacts and digests
retained there.
The [pending-renderer deadline evidence](../evidence/2026-08-30-pending-renderer-deadline.md)
records the 32nd case, implemented at `40d4f78` and stabilized at final evidence
tree `de4009a`.
The [participant current-lease ownership evidence](../evidence/2026-08-30-participant-lease-acquisition-ownership.md)
records the later connected-peer acquisition side-effect tests at `681c0f7` and
their first hosted candidate tree `213327c`. CodeQL succeeds at that exact tree,
but its Windows CI job times out in an unrelated protection-drain test, so it is
not successful matrix evidence. Those connected-peer tests strengthen P0 Throw
ownership without themselves adding a tracer case or changing a matrix status.
The [pending-renderer Trust-revoke evidence](../evidence/2026-08-30-pending-renderer-trust-revoke.md)
records the 33rd case at exact implementation commit `8413d06`. It advances only
P0 Revoke and P2 Revoke from Missing to Partial; TX Revoke and CL Revoke receive
additional owner-path evidence but stay Partial.
The [final-Admission authority-revoke evidence](../evidence/2026-08-30-final-admission-authority-revoke.md)
records the 34th case at exact implementation commit `15aba95`. It advances only
AD Revoke from Missing to Partial. Its path observations do not promote HC
Revoke from Missing or CL Revoke from Partial.
Final exact-SHA CI `33300966551` and CodeQL `33300966509` pass for `15aba95`.
Retained artifacts prove `2575/2575` with every non-success counter zero on all
three hosted OSes, Gitleaks 208/0, CodeQL 52/0 with 0 exact-ref open alerts, and
three reproducible version-0.1.209 unsigned packages whose `5/5` checksums and
repository verification pass. Exact jobs, artifacts, digests, and limitations
are retained in all three checkpoint records above.
The [final-Admission authenticated-disconnect evidence](../evidence/2026-08-30-final-admission-authenticated-disconnect.md)
records the 35th case at exact implementation commit `7be177b`. It advances only
AD Disconnect from Missing to Partial; HC Disconnect stays Missing and CL
Disconnect stays Partial. Final exact-SHA CI `33302056214` and CodeQL
`33302056182` pass for evidence tree `629d1e5`; retained artifacts prove
`2576/2576` on every hosted OS, Gitleaks 208/0, CodeQL 52/0 with 0 exact-ref
alerts, and three reproducible version-0.1.211 unsigned packages. Earlier CI
`33301715578` and CodeQL `33301715584` target `c13acc5`, not this new case.
The [host capture-start authenticated-disconnect evidence](../evidence/2026-08-30-host-capture-start-authenticated-disconnect.md)
records the 36th case and causal-failure fix at exact commit `fe0be79`. It
advances only HC Disconnect from Missing to Partial; AD Disconnect and CL
Disconnect stay Partial. Final exact-SHA CI `33303210427` and CodeQL
`33303210391` pass for evidence tree `a0c9648`; retained artifacts prove
`2577/2577` on every hosted OS, Gitleaks 208/0, CodeQL 52/0 with 0 exact-ref
alerts, and three reproducible version-0.1.213 unsigned packages. Earlier CI
`33302708813` and CodeQL `33302708801` target `17a3401`, not this new case.
The [host capture-start authority-revoke evidence](../evidence/2026-08-30-host-capture-start-authority-revoke.md)
records the 37th case at exact commit `62e9372`. It advances only HC Revoke from
Missing to Partial; HC, AD, and CL Disconnect remain Partial. The failed
`9ca4b2c` CI remains historical evidence for an unrelated pairing race. Final
cumulative evidence tree `c4c02a3` contains this row plus pairing fix `7239448`
and the later rows through 39; CI `33305006486` and CodeQL `33305006421`
succeeded with `2580/2580` on each hosted OS, Gitleaks 208/0, CodeQL 52/0, and
three verified reproducible unsigned packages.
The [host capture-start caller-cancellation evidence](../evidence/2026-08-30-host-capture-start-caller-cancellation.md)
records the 38th case at exact commit `0f26c26`. It advances only HC Cancel from
Missing to Partial; CL Cancel stays Partial. Final cumulative hosted evidence is
the successful `c4c02a3` tree and runs named above; the earlier `9ca4b2c` runs
precede this row.
The [host capture-start rejection evidence](../evidence/2026-08-30-host-capture-start-rejection.md)
records the 39th case at exact commit `858acb2`. It advances only HC Reject from
Missing to Partial. HC Reject, Cancel, Revoke, and Disconnect are now all
Partial; every other cell is unchanged. Final cumulative hosted evidence is the
successful `c4c02a3` tree and runs named above.
The [host initial Authorization authenticated-disconnect evidence](../evidence/2026-08-30-host-initial-authorization-disconnect.md)
records the 40th case at exact commit `077c996`. It advances only H0 Disconnect
from Missing to Partial; H1 Disconnect remains Missing and CL Disconnect remains
Partial. Exact-SHA CI `33305848081` and CodeQL `33305848085` succeeded; each
hosted OS passed `2581/2581`, with Gitleaks 208/0, CodeQL 52/0, and verified
reproducible unsigned packages.
The [host route authenticated-disconnect evidence](../evidence/2026-08-30-host-route-authenticated-disconnect.md)
records the 41st case and post-route authority-gate repair at exact commit
`d593181`. It advances only H1 Disconnect from Missing to Partial; H0 and CL
Disconnect remain Partial. CI `33306962398` failed only macOS Transport
`ProtocolOnePointTwoInvalidInitiatorFinishedNeverRunsHandler(Omit)`: macOS
passed `2581/2582` overall and Desktop `718/718`, while Ubuntu and Windows each
passed `2582/2582` and Secret Scan passed 208/0. CodeQL `33306962391` passed
52/0; packages were skipped. Test-only `c98a570` widens only that theory's
300 ms/2 s/3 s budgets to 2 s/4 s/6 s without changing its assertions. Exact-
SHA CI `33307322868` and CodeQL `33307322870` then succeeded: every hosted OS
passed `2582/2582`, Gitleaks reported 208/0, CodeQL reported 52/0 with 0 exact-
ref open alerts, and all three reproducible unsigned packages verified.
The [bounded cleanup-confirmation evidence](../evidence/2026-08-30-bounded-cleanup-confirmation.md)
records the 42nd case at exact implementation commit `685225e`. It advances only
CL Timeout from Missing to Partial. Exact-SHA CI `33311180093` and CodeQL
`33311180128` succeeded; downloaded artifacts prove `2584/2584` with every
non-success counter zero on each hosted OS, Gitleaks 208/0, CodeQL 52/0 with 0
exact-ref open alerts, and all three reproducible unsigned packages verified.
The [external Dispose-first evidence](../evidence/2026-08-30-dispose-first-bounded-cleanup.md)
records one additional coordinator CL Timeout order at exact implementation
commit `ea984fb`. Exact-SHA CI `33314229467` and CodeQL `33314229459` succeeded;
downloaded artifacts prove `2585/2585` with every non-success counter zero on
each hosted OS, Gitleaks 208/0, CodeQL 52/0 with 0 exact-ref open alerts, and all
three reproducible version-`0.1.222` unsigned packages verified. This closes
only Task 5.5a.3a. It adds no 43rd production-composed tracer and changes no
matrix status; CL Timeout remains Partial.
The tracer results above are same-host **managed loopback runs on macOS**. The
external Dispose-first local row is a managed Desktop harness rather than a
loopback trace. Hosted Windows, macOS, and Linux results remain managed and
contract evidence. None of them is native API, physical two-device,
signed-package, or notarization evidence. `CreateProduction()` must continue to
report Remote Window unavailable until the native runtime gates are
independently satisfied.

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
| **H0** | **P** [E-H] | **P** [E-H] | **P** [E-H] | **N/A** [N2] | **P** [E-H] | **P** [E-H, E-TRACE] | **P** [E-H, E-CL] |
| **H1** | **P** [E-H] | **P** [E-H] | **P** [E-H] | **P** [E-H] | **P** [E-H] | **P** [E-H, E-TRACE] | **P** [E-H, E-CL] |
| **TX** | **C** [E-TX] | **P** [E-TX] | **C** [E-TX] | **C** [E-TX] | **P** [E-TX, E-TRACE] | **P** [E-TX] | **P** [E-TX, E-CL] |
| **P0** | **P** [E-P0] | **P** [E-P0] | **P** [E-P0] | **P** [E-P0, E-TX] | **P** [E-P0, E-TRACE] | **P** [E-P0, E-TRACE] | **P** [E-P0, E-CL] |
| **P1** | **P** [E-P1] | **P** [E-P1] | **P** [E-P1] | **P** [E-P1, E-TX] | **P** [E-P1] | **P** [E-P1, E-TRACE] | **P** [E-P1, E-CL] |
| **P2** | **C** [E-P2] | **C** [E-P2] | **P** [E-P2] | **P** [E-P2, E-TRACE] | **P** [E-P2, E-TRACE] | **P** [E-P2] | **P** [E-P2, E-CL] |
| **RS** | **C** [E-RS] | **P** [E-RS] | **C** [E-RS] | **C** [E-RS] | **P** [E-RS] | **P** [E-RS] | **P** [E-RS, E-CL] |
| **AD** | **C** [E-AD] | **P** [E-AD, E-TRACE] | **C** [E-AD] | **C** [E-AD] | **P** [E-AD, E-TRACE] | **P** [E-AD, E-TRACE] | **P** [E-AD, E-CL] |
| **HC** | **P** [E-HC, E-TRACE] | **P** [E-HC, E-TRACE] | **P** [E-HC, E-TRACE] | **P** [E-HC] | **P** [E-HC, E-TRACE] | **P** [E-HC, E-TRACE] | **P** [E-HC, E-CL] |
| **CL** | **P** [E-CL] | **P** [E-CL] | **P** [E-CL] | **P** [E-TRACE, E-CL] | **P** [E-TRACE, E-CL] | **P** [E-TRACE, E-CL] | **P** [E-TRACE, E-CL] |

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
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.H0AuthenticatedDisconnectDuringAuthorizationReservationFailsClosedAndDrainsBothNodes`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  at exact commit `077c996` adds a pre-safety Connection order. Real
  protocol-1.7 loopback has acquired a fingerprint-bound Trust Authorization
  reservation inside a deterministic wrapper but has not yet returned it to the
  coordinator. At that barrier the exact Connection Preparation registration
  and live callback are current; H1 Protection, Emergency Stop, route, Prepare,
  capture, Admission, media attachment/send, render, and input authority are
  unopened. A real
  authenticated disconnect reaches the live callback, makes Connection and its
  Preparation registration non-current, and prevents old-generation
  reacquisition while the Authorization registration and unchanged Trust grant
  remain current. Releasing the barrier transfers the Authorization
  registration to the coordinator, whose owned stale-Connection cleanup
  disposes it. No route exists, so fail-close stays zero, connection disposal is
  exactly once, and both nodes drain. The
  [host initial Authorization authenticated-disconnect evidence](../evidence/2026-08-30-host-initial-authorization-disconnect.md)
  records exact local and hosted results: every hosted OS passed `2581/2581`,
  Gitleaks reported 208/0, CodeQL reported 52/0 with 0 exact-ref open alerts,
  and all three reproducible unsigned packages verified.
  By fault origin this advances only H0 Disconnect from Missing to Partial.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.H1AuthenticatedDisconnectAfterRouteSideEffectPreventsPrepareAndDrainsBothNodes`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  at exact commit `d593181` runs real authenticated protocol-1.7 loopback. Its
  hook executes only after the inner responder-route operation returns, with one
  real host route, current Connection Preparation, reserved Protection, and
  current Emergency Stop readiness, but zero Prepare calls. Participant
  Connection disposal reaches a barrier published only after the production
  host revocation callback returns. The old generation and Connection
  registration are then non-current and unreacquirable while Trust,
  fingerprint, and sole `mirror.view` remain unchanged. The final post-route
  gate returns causal `authenticated_connection_stale` before the Prepare method
  or wire admission; owned route cleanup fail-closes and disposes once and both
  nodes drain. Its exact RED was `PrepareCount` expected 0, actual 1. The repair
  orders caller cancellation, terminal cause, deadline, current facts plus
  fresh-safe Protection, then repeats cancellation/terminal/deadline; non-fatal
  concurrent failures retain the terminal reason, while OOM remains the exact
  primary for the existing outer cleanup/aggregation path. The
  [host route authenticated-disconnect evidence](../evidence/2026-08-30-host-route-authenticated-disconnect.md)
  records local results, the exact `d593181` hosted failure, and the `c98a570`
  fixture-only stabilization plus successful exact-SHA hosted rerun. Every
  hosted OS passed `2582/2582`, Gitleaks reported 208/0, CodeQL reported 52/0
  with 0 exact-ref open alerts, and all three reproducible unsigned packages
  verified; the retained failed run itself produced no package evidence.
  By fault origin this advances only H1 Disconnect from Missing to Partial.
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
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.TxP0P2AuthenticatedControlDisconnectWhileRendererPreparationIsBlockedFailsClosedAndDrains`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  admits Prepare send, attaches both exact `FSM1` sessions, blocks renderer
  Preparation before any Ready outcome, then disconnects authenticated control.
  Owned cleanup enters and cancels without completing before renderer release;
  release produces one bounded `Rejected/preparation_cancelled`, disposes the
  late renderer, opens no later authority, and drains both nodes.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.TxP0P2ExactDeadlineWhileRendererPreparationIsBlockedFailsClosedAndDrains`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  advances only the participant clock to exact deadline equality while the host
  remains before deadline and peer disconnect has not entered. Renderer
  cancellation precedes one bounded `Rejected/preparation_expired`; late
  renderer and both-node owners drain without Ready or later authority.

Send/response throws, revocation/disconnect while every distinct transaction
phase is pending, and all cleanup-fault combinations are not complete; those
cells therefore remain P.

### E-P0 — participant policy and current connection lease

- [`DesktopRemoteWindowPreparationPeerTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowPreparationPeerTests.cs)
  directly covers bounded local receive-policy rejection, unknown policy reason
  reduction, policy-throw redaction, stopping/busy state, failed current-lease
  acquisition, acquisition side-effect-then-throw for non-fatal and fatal
  failures, linked cancellation, prepared-owner release, peer disconnect, and
  primary-plus-cleanup preservation. The policy reject/throw rows prove zero
  connection acquisition and renderer authority, then recover with a fresh
  request through real loopback `FSM1` Ready. The acquisition side-effect rows
  attach a real current lease to the generation before the collaborator failure
  propagates: non-fatal projection releases it and permits an ABA-safe
  replacement, while fatal exhaustion retains it until terminal cleanup.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.P0ParticipantTrustRevokeWhileRendererPreparationIsBlockedFailsClosedAndDrains`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  uses the production authenticated session-attempt, system connector, signed
  candidate, Trust store, and generation-bound lease. After admitted Prepare and
  bilateral attachment, participant Trust revoke invalidates the current lease,
  enters disconnect cleanup, and cancels blocked renderer Preparation. Cleanup
  cannot falsely complete until renderer release; afterward one bounded
  `Rejected/preparation_cancelled` and `PermanentRejection/PeerNotTrusted`
  appear, with no later authority and complete both-node drain.

The one pending-renderer Trust-revoke phase moves P0 Revoke from Missing to
Partial. Remaining Trust/connection revocation phases, the other authenticated-
disconnect phases, and cleanup-fault combinations remain incomplete. P0
therefore remains partial overall. The current-lease ownership repair separately
strengthens the already-P P0 Throw cell but does not promote it to Covered.

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
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.TxP0P2AuthenticatedControlDisconnectWhileRendererPreparationIsBlockedFailsClosedAndDrains`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  supplies one production-composed authenticated disconnect while the renderer
  worker is non-cooperatively blocked after bilateral attachment. It proves
  cancellation is observed before cleanup can finish, then late renderer
  disposal, bounded rejection, zero host Ready authority, and full drain after
  release.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.TxP0P2ExactDeadlineWhileRendererPreparationIsBlockedFailsClosedAndDrains`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  supplies one production-composed P2 Timeout after exact Prepare send and
  bilateral attachment. Split manual clocks prove participant deadline equality
  cancels the renderer before disconnect while host time remains earlier; release
  yields `Rejected/preparation_expired`, disposes the late renderer, and opens no
  later authority.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.P0ParticipantTrustRevokeWhileRendererPreparationIsBlockedFailsClosedAndDrains`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  supplies one production-composed P2 Revoke. Real participant Trust mutation
  cancels the pending renderer lifetime through the current authenticated
  session; cleanup remains incomplete before explicit renderer release, then
  produces bounded cancellation, disposes the late result, and drains.

Missing/null and ordinary throw classification are covered. One exact deadline-
equality timeout and one disconnect while renderer preparation is blocked now
cross the production path. One authoritative Trust revoke now crosses that same
pending-renderer phase. Other timeout and revoke phases, disconnect phases, and
cleanup cross-products remain open, so Timeout and Revoke plus the existing
non-C cells remain partial.

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
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.AdFinalAdmissionAuthorityRevokeFailsClosedAndDrainsBothNodes`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  waits for that same exact participant Admission commit, then uses real host
  Trust mutation to remove `mirror.view` before host post-publication
  revalidation and frame-gate open. The current connection becomes stale and
  cannot be reacquired; capture Emergency Stops; media/render/input stay zero;
  and both nodes drain with a bounded host result.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.AdFinalAdmissionAuthenticatedDisconnectFailsClosedAndDrainsBothNodes`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  waits for exact participant Admission commit, then starts real participant
  connection disposal without changing Trust, fingerprint, or `mirror.view`.
  A post-revocation-callback barrier proves the old generation is non-current
  and unreacquirable before hook return without awaiting full disposal. The
  emitted boundary frame is disposed, authority stays closed, and outside-hook
  session/disconnect joins drain both nodes.
- [`AuthenticatedRemoteWindowConnectionLeaseTests`](../../tests/Flowspan.Transport.Tests/AuthenticatedRemoteWindowConnectionLeaseTests.cs)
  proves linked Admission cancellation is normalized back to the exact caller
  token without relabelling a foreign cancellation.

A participant endpoint throw, the remaining authority-revoke and authenticated-
disconnect phases, and remaining wire/cleanup variants are still missing.
Revoke and Disconnect therefore advance only to Partial, and Throw also remains
Partial rather than complete.

### E-HC — host commit after Ready

- [`DesktopRemoteWindowHostCoordinatorTests.StartKeepsFramesClosedUntilFinalAdmissionIsPublished`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowHostCoordinatorTests.cs)
  proves the happy-path order with frame admission closed.
- The same test file's `ExpiredPreparationAfterMediaAttachmentNeverStartsCapture`
  proves one post-attachment deadline failure and its cleanup.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  proves the admitted success, one cancellation/expiry before Admission, and a
  final state publication side-effect-then-throw plus one post-publication
  authority revoke and one authenticated disconnect after participant known
  binding, but does not inject every individual HC commit call. By fault origin
  the latter rows are AD evidence, not direct HC Revoke/Disconnect injections.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.HcAuthenticatedControlDisconnectDuringCaptureStartFailsClosedAndDrainsBothNodes`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  injects real authenticated disconnect after Ready and bilateral attachment
  while capture Start still owns HC. The first frame disposes once, Admission/
  send/render/input remain zero, post-callback currentness fails, and both nodes
  drain with causal `authenticated_connection_stale`.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.HcAuthorityRevokeDuringCaptureStartFailsClosedAndDrainsBothNodes`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  applies exact fingerprint-bound `CapabilityGrant.None` at the same capture-
  start boundary. Trust identity remains while Mirror authority is empty; the
  current generation is invalidated, Admission/send/render/input remain zero,
  and both nodes drain with the same causal bounded result.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.HcCallerCancellationAfterCaptureSideEffectFailsClosedAndDrainsBothNodes`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  cancels only the exact Start caller token at that boundary. The authenticated
  connection remains current and the same generation is probed then released;
  exact-token cancellation escapes, ordinary Stop cleanup runs, and both nodes
  drain without Admission/send/render/input.
- [`DesktopRemoteWindowManagedTwoNodeTracerTests.HcCaptureStartRejectAfterFrameSideEffectFailsClosedAndDrainsBothNodes`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  returns bounded `capture_start_failed` after the first frame disposes and a
  same-generation currentness probe succeeds. Trust/transport remain unchanged,
  Admission/send/render/input stay zero, ordinary Stop runs with zero Emergency
  Stop, and both nodes drain.

Direct negative/throw/cancel/revoke/disconnect cases are still required for each
post-Ready host revalidation, the remaining protection promotion/capture-start/
live intersections, Emergency Stop registration, controller `Start`, exact
`AddParticipant`, and final open. State publication Throw now has direct
evidence, but the remaining Reject/Cancel/Revoke/Disconnect and cleanup variants
stay incomplete. Capture-start now has one direct row for each of Reject, Cancel,
Revoke, and Disconnect. In particular, a failure before HC is not HC evidence.

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
- The pending-renderer disconnect tracer proves owned cleanup and cancellation
  enter without falsely completing while a non-cooperative renderer still owns
  its call. Explicit release then disposes the late renderer and drains the
  complete managed two-node owner graph.
- The pending-renderer deadline tracer also drains the late renderer and full
  owner graph, but its injected fault originates at P2 deadline equality.
  Cleanup itself does not time out, so this is not CL Timeout evidence.
- The pending-renderer Trust-revoke tracer invalidates the participant lease,
  keeps owned disconnect cleanup incomplete until the non-cooperative renderer
  is released, and then drains the full graph. This strengthens CL Revoke, but
  does not cover the remaining cleanup owners or revoke-plus-cleanup failures.
- The final-Admission authority-revoke tracer invokes local capture/input
  Emergency Stop after participant Admission commit, prevents frame-gate open,
  and drains both nodes. This strengthens the already-Partial CL Revoke path but
  does not inject a CL-origin failure.
- The final-Admission authenticated-disconnect tracer similarly invokes local
  Emergency Stop, joins full disconnect/session cleanup outside the boundary
  hook, and drains both nodes. This strengthens the already-Partial CL Disconnect
  path but does not inject a CL-origin failure.
- The host capture-start disconnect tracer joins the same complete two-node
  cleanup after preserving the Connection cause through controller Start. This
  strengthens CL Disconnect but does not complete its owner/fault combinations.
- The host capture-start authority-revoke sibling drains the same graph after an
  Applied Trust mutation. This strengthens CL Revoke but does not inject a CL-
  origin fault or complete revoke-plus-cleanup combinations.
- The host capture-start caller-cancellation sibling uses ordinary Stop and
  owned fail-close/disposal while preserving exact token identity. This
  strengthens CL Cancel but does not complete cancellation-plus-cleanup rows.
- The capture-start rejection sibling also uses ordinary Stop and drains the
  complete graph with zero Emergency Stop. This strengthens CL Reject without
  completing rejection-plus-cleanup rows.
- [`DesktopRemoteWindowHostCoordinatorTests.TerminalCleanupWatchdogTimeoutReleasesGateAndPermanentlyBlocksRestartUntilTrueDrain`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowHostCoordinatorTests.cs)
  and the 42nd managed tracer execution provide direct CL Timeout evidence for
  one active authenticated disconnect with host Connection disposal blocked.
  They prove T-1 pending state, exact equality timeout, bounded lifecycle-gate
  release, zero-authority replacement rejection, the same real cleanup task's
  late completion, bilateral drain, and sticky restart denial.
- [`DesktopRemoteWindowHostCoordinatorTests.DisposeFirstCleanupTimeoutIsStableAcrossConcurrentDisconnectAndLateDrain`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowHostCoordinatorTests.cs)
  adds one explicit external Dispose-first CL Timeout order for a stable active
  generation and uncontended lifecycle gate. It proves synchronous retiring/
  timer publication before owner blocking, one later terminal-callback cleanup
  attachment, T-1 pending state, exact-equality timeout, shared public Task and
  exception identity, disposed-Start precedence, and late true Connection drain
  without public-result mutation.

The remaining renderer, active/pending frame, queue, attachment, route,
directory, controller, protection FIFO/admission-use, permission-observer,
sharing-session, Emergency Stop, and control-owner fault injections and their
meaningful combinations remain open. CL Timeout is Partial only: Stop-first,
lifecycle-gate contention, other owners, timer faults, cleanup-winner races,
late failure/OOM, and pre-generation bounded cleanup remain untested.

### E-TRACE — production-composed managed loopback

- [`DesktopRemoteWindowManagedTwoNodeTracerTests`](../../tests/Flowspan.Desktop.Tests/DesktopRemoteWindowManagedTwoNodeTracerTests.cs)
  is now the executable 42-case class.
- The [managed tracer evidence record](../evidence/2026-08-28-managed-remote-window-production-tracer.md)
  records the first 22 cases' exact local/hosted commands, artifacts, results,
  and limitations. The source-linearization, Emergency Stop readiness,
  Trust/Capability, and Permission evidence above record the 23rd through 26th
  cases and their exact-SHA execution. The Connection Preparation evidence
  records the 27th and 28th cases. The Protection Preparation evidence records
  the 29th and 30th cases. The pending-renderer authenticated-disconnect
  evidence records the 31st case. The pending-renderer deadline evidence records
  the 32nd case and keeps its narrow P2 Timeout scope explicit. The pending-
  renderer Trust-revoke evidence records the 33rd case and keeps its narrow
  P0/P2 Revoke scope explicit. The final-Admission authority-revoke evidence
  records the 34th case and keeps its narrow AD Revoke scope explicit. The
  final-Admission authenticated-disconnect evidence records the 35th case and
  keeps its narrow AD Disconnect scope explicit. The host capture-start
  authenticated-disconnect evidence records the 36th case and keeps its narrow
  HC Disconnect scope explicit. The host capture-start authority-revoke evidence
  records the 37th case and keeps its narrow HC Revoke scope explicit. The host
  capture-start caller-cancellation evidence records the 38th case and keeps its
  narrow HC Cancel scope explicit. The capture-start rejection evidence records
  the 39th case and keeps its narrow HC Reject scope explicit. The host initial
  Authorization authenticated-disconnect evidence records the 40th case and
  keeps its narrow H0 Disconnect scope explicit. The host route
  authenticated-disconnect evidence records the 41st case and keeps its narrow
  H1 Disconnect scope explicit.
- The [bounded cleanup-confirmation evidence](../evidence/2026-08-30-bounded-cleanup-confirmation.md)
  records the 42nd case and keeps its narrow CL Timeout scope explicit.
- The [external Dispose-first evidence](../evidence/2026-08-30-dispose-first-bounded-cleanup.md)
  is a coordinator contract row, not a production-composed tracer execution;
  the executable tracer class therefore remains at 42 cases.
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
handoff at `259c3bb`, plus one pre-safety disconnect during the Authorization
ownership handoff at `077c996`, and one route-side-effect disconnect that reaches
the production callback barrier but prevents the Prepare method itself at
`d593181`. None of these disconnect rows enters the actual send hook; the
Transport two-lease regression supplies that separate gate evidence.
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
   the remaining authenticated-disconnect phases before route selection. One
   real disconnect is now frozen after a fingerprint-bound Authorization
   reservation is acquired but before its ownership handoff. The deterministic
   source, permission, grant, and connection revocation barriers do not prove an
   atomic reservation across arbitrary concurrent threads. Source and
   Authorization now each have one real `R < M < S` vertical, and Permission
   has one managed production-composed `R < M < S` vertical over its exact fact
   gate. Add their production-composed `M < R` and `S < M` orders and remaining
   fault intersections, add real native permission observation evidence, and
   extend the exact authenticated Connection gate through the remaining
   production-composed mutation, disconnect, and cleanup-fault intersections.
   All current Connection disconnect tracers terminate before actual send
   admission and cannot stand in for those missing rows.
2. **H1:** finish the safety/route reject and throw variants; extend connection
   loss through each exact route/send phase beyond the existing blocked-Prepare
   and route-side-effect/pre-Prepare disconnects; preserve any route side effect
   while cleanup also fails; finish the other production-composed Emergency Stop
   orders and fault variants; prove native Emergency Stop registration/action
   behavior; and complete the
   remaining Protection promotion, capture-start, live FIFO/admission-use,
   source-loss, and cleanup-fault intersections plus native probe behavior. Add
   production-composed Protection `M < R` and `S < M`; the current Protection
   tracer covers only `R < M < S`.
3. **TX/RS:** cover throw, revoke, disconnect, and cleanup failure at every
   distinct send-admission, buffered-response, terminal-commit, completion-hook,
   and tombstone phase rather than treating Stop as every terminal cause.
4. **P0:** the remaining current Trust/connection revoke phases; authenticated
   disconnect at the other participant-preparation phases; exact cleanup-fault
   combinations and retry/terminal denial where the production contract
   requires them. Current-lease acquisition side-effect-then-throw ownership and
   one pending-renderer Trust revoke now have direct evidence, but do not fill
   these remaining rows.
5. **P1:** malformed/tampered `FSM1`, blocked-handshake cancellation and exact
   timeout, endpoint/listener disconnect variants, generation revocation at
   each attach phase, and remaining handler/route/directory cleanup failures.
6. **P2:** the remaining deadline and authoritative lease/Trust-revoke phases;
   timeout/revoke plus cleanup-fault intersections; the remaining disconnect
   phases; and all renderer cleanup combinations. The current row covers one
   real participant Trust revoke while renderer Preparation is blocked.
7. **AD:** participant-endpoint throw, the remaining authority-revoke and
   authenticated-disconnect phases, and remaining cleanup failures at the exact
   buffered/send/commit phases. The current revoke and disconnect rows cover
   only the post-participant-commit, pre-host-frame-open window.
8. **HC:** independent rejection and throw injection for the remaining
   revalidations, protection/Emergency owner registration, exact
   `AddParticipant`, Admission state-send variants, and final open; the remaining
   cancellation, deadline, revoke, and disconnect races between each step.
   Capture-start now has one direct Reject, Cancel, Revoke, and Disconnect row.
9. **CL:** remaining single-owner cleanup faults; meaningful ordered combined
   failures; active versus pending owner variants; stable shared completion;
   zero retained owner/budget counts; and the remaining cleanup-timeout
   initiators beyond active disconnect and external Dispose-first, lifecycle-
   gate contention, owners, winner races, timer faults, late failures/OOM, and
   pre-generation paths beyond the two implemented Connection-owner rows.

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
