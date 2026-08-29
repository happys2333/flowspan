# ADR 0027: Remote Window host Preparation reservation

- Status: Proposed implementation contract
- Date: 2026-08-30
- Decision owners: Flowspan maintainers
- Desktop-only core checkpoint: `294042f`; production composition pending

## Context

[NR2.6](../../specs/v1/native-remote-window/requirements.md) requires the host
to revalidate the exact source, authenticated connection, Trust and role
Capabilities, prompt-free permissions, fresh Safe protection, independent local
Emergency Stop readiness, and responder media route before it sends protocol-1.7
Prepare. [NR2.10](../../specs/v1/native-remote-window/requirements.md) separately
requires the host to wait for exact Ready success before it installs the formal
protection and Emergency Stop owners and crosses native capture.

[ADR 0026](0026-protocol-1-7-remote-window-preparation.md) freezes the protocol
order and identifies the actual Prepare wire-send admission as a linearization
point. The [Native Remote Window design](../../specs/v1/native-remote-window/design.md)
also keeps native ownership narrow and requires every post-route terminal
failure to consume the media session and close its owning authenticated control
connection.

The current managed host coordinator performs several point-in-time reads and
repeats them immediately before route selection. It also subscribes early to
permission and authenticated-connection loss. Those checks reduce the ordinary
race window and provide useful fail-close defense, but they do not define one
linear order against changes made from arbitrary threads:

- a source can invalidate after the last lease read;
- a permission revision can change after the last permission snapshot;
- the current Mirror grant has no exact revision or operation reservation;
- an authenticated connection can revoke between its current check and media
  route selection; and
- Emergency Stop readiness is deliberately a prompt-free point probe that
  claims no registration ownership.

A later post-Ready revalidation prevents these races from granting capture or
participant authority, but a stale Prepare can still be admitted to the wire or
a route can be selected after a logically earlier revocation. That violates the
pre-Prepare ordering even when final capture remains closed. Repeating reads at
more source locations cannot prove the missing arbitrary-thread ordering.

This gap is represented by the H0 and H1 families in the
[Remote Window production-boundary fault matrix](../testing/remote-window-production-boundary-matrix.md).
It is a blocker for Native Remote Window Task 5.5a. Production Remote Window
must remain unavailable while this contract is proposed or incompletely
implemented.

## Decision

Implement one process-local `RemoteWindowHostPreparationReservation` per host
Preparation generation. It is a bounded, revocable reservation over an exact
bundle of observed host facts. It grants no Capability, participant membership,
Driver Lease, input, capture, media disclosure, or rendering authority.

The reservation is a Desktop composition module. Platform, Security, and
Transport keep ownership of their individual facts and expose only the narrow
epoch/reservation operations required to participate in its linearization. OS
projects do not implement a second host state machine.

### Desktop-only core checkpoint

Commit `294042fdfcc346e3eade3551d57cc7ccba95c601` implements the internal
`RemoteWindowHostPreparationReservation` state machine in `Flowspan.Desktop`.
It begins in `Collecting`, requires an explicit deadline-checked transition to
`Armed`, and then implements the exact monotonic route, Prepare-send, Ready, and
promotion phases specified below. Route admission conservatively records that a
route may be owned before the external route call, so side-effect-then-throw
uses `ConsumeConnection` cleanup scope.

The core represents Source, Permission, Authorization, Connection, Emergency
Stop, and Protection with six distinct opaque process-local fact epochs. One
epoch bundle can be claimed by only one host reservation. Exact host generation
and epoch matching reject stale callbacks and ABA replacement attempts. The
core derives fact failure reasons from a fixed enum allowlist and gives all
concurrent invalidations one terminal completion created with
`RunContinuationsAsynchronously`.

The nine deterministic tests cover `M < R`, `R < M < S`, `S < M`, route
side-effect-then-throw, deadline equality at Arm/route/send/Ready/promotion,
bundle reuse, host-generation/fact-epoch ABA, simultaneous six-fact
invalidation, and exact Ready/promotion phase and binding. Strict review first
returned BLOCK with one P1 and two P2 findings; after Collecting, bundle-claim,
deadline, and late reason-validation/redaction repairs, final review returned
APPROVE with 0 P0, 0 P1, and 0 P2 findings. Local verification and its limits
are recorded in the
[core evidence](../evidence/2026-08-30-host-preparation-reservation-core.md).

This checkpoint is deliberately not wired into
`DesktopRemoteWindowHostCoordinator`, Platform, Security, Transport, or
`CreateProduction()`. It does not provide source invalidation admission, an
authenticated connection route operation, the Transport send-admission hook,
or any real fact reservation. It changes no production-boundary matrix cell and
does not close H0, H1, Task 5.5a, or production availability. The next slice
must connect real source invalidation through the connection route operation to
the actual Transport Prepare send-admission point.

### Exact epoch bundle

The reservation binds one immutable `HostPreparationEpochBundle` containing at
least:

- the opaque source identity, source generation, and geometry revision;
- permission owner generation, exact permission revision, and the capture/input
  states required by the frozen role;
- participant Device, exact Trust/Capability revision, and the required
  peer-relative Mirror grant;
- authenticated connection generation, local and peer Devices, negotiated
  protocol, and connection-owned media-session identity;
- Emergency Stop registrar generation and one readiness-reservation identity;
- protection owner, Session, and source generations, protection revision, and
  observation time;
- the unpredictable Remote Window Session, Activity, participant, frozen role,
  correlation, and canonical deadline; and
- one local host Preparation generation that prevents an older callback from
  invalidating or promoting its replacement.

No source token, native handle, permission epoch, Trust revision, connection
generation, Emergency Stop reservation identity, protection revision, or media
route locator enters Prepare, Ready, diagnostics, persistence, or peer-visible
state. The existing protocol-1.7 binding and fixtures do not change.

Each fact epoch is exact and independently monotonic for the lifetime of the
reservation. Any accepted change invalidates that reservation permanently.
Revocation followed by regrant cannot revive it; retry requires a fresh
authenticated connection, Session, correlation, media session, and epoch
bundle. A single aggregate integer is not a substitute for these independent
identities.

### Participating fact reservations

The host reservation composes these fact-specific operations:

- **Source:** retain the exact existing source lease and install a non-blocking,
  generation-bound invalidation guard. Do not retain a native source-use scope
  across network preparation. Source-use scopes remain limited to complete
  native capture/input calls.
- **Permission:** reserve one exact owner/revision snapshot. A newer revision,
  owner replacement, unavailable read, or required-role denial makes the old
  reservation terminal. The reservation observes only prompt-free facts.
- **Trust and Capability:** reserve the exact peer and required `mirror.view`, or
  `mirror.view` plus `mirror.drive`, under the same mutation gate used by Trust
  revoke and Capability update. The generic any-of authenticated connection
  registration is connectivity, not this operation authorization.
- **Authenticated connection:** admit responder-route selection under the exact
  connection-generation gate and bind it to the connection-owned media session.
  Revoke, fail-close, owner release, and route admission therefore have a defined
  order.
- **Emergency Stop:** reserve readiness and local registrar capacity without
  installing the formal Emergency Stop callback. The reservation is bound to
  the intended owner and Session generations and can be promoted only once.
- **Protection:** retain a short-lived exact Safe observation epoch that can
  invalidate Preparation but cannot pause, resume, or stop a controller. The
  formal protection observer remains a post-Ready owner.

Fact mutation first performs a bounded invalidation transition against the host
reservation while it owns the fact's mutation gate. Observer delivery, native
stop, connection close, disposal, and other potentially blocking work occur
after releasing that gate. The invalidation transition may latch state and
signal cancellation, but it must not await, dispose another owner, call the UI,
invoke native work, or run attacker-controlled callbacks.

### Reservation state and linearization

The host reservation has the following monotonic states:

1. `Collecting` while exact fact reservations are acquired;
2. `Armed` after every required fact and the absolute deadline are current;
3. `RouteAdmitted` when responder-route selection wins its connection-generation
   admission point;
4. `RouteSelected` after the route operation returns, or conservatively when a
   route operation may have produced a side effect before throwing;
5. `PrepareSending` when the actual Transport send-admission point commits;
6. `ReadyMatched` after one exact Ready success and current media binding match;
7. `Promoted` after the formal post-Ready owners are installed; or
8. one terminal invalidated, failed, cancelled, expired, or disposed state.

`RouteAdmitted` is the point of no return for cleanup. The authenticated
connection generation admits a bounded route-selection operation before calling
the media-route registry. If connection revocation wins first, no route is
selected. If route admission wins first, the physical route operation may finish
after a concurrent revocation, but it is ordered before that revocation and the
connection cleanup must join and consume it.

The host cannot commit Prepare send itself. Transport extends the existing
protocol-1.7 send-admission path with a synchronous, internal reservation hook.
That hook runs under the same Stop-before-send gate, after deadline and
connection checks and immediately before invoking the wire boundary. It may
only validate the exact request and perform the bounded reservation transition
from `RouteSelected` to `PrepareSending`.

For every participating fact invalidation `M` and Prepare send admission `S`,
the implementation must produce exactly one of these orders:

- if `M` linearizes before `S`, `S` is rejected and no Prepare bytes are admitted
  to the connection; or
- if `S` linearizes before `M`, at most that one exact Prepare is admitted, `M`
  immediately makes the generation terminal, and cleanup prevents capture,
  final Admission, input, and rendering.

Similarly, for connection revocation `C` and route admission `R`, `C < R`
produces no route, while `R < C` produces an owned route that must be consumed
and cannot be reused. Wall-clock callback order or the time at which an
asynchronous wire flush completes does not override these admitted-operation
orders.

### Ready promotion without early authority

Ready success grants no authority. After matching Ready and the current media
binding, the host:

1. verifies that the exact host reservation is still current and before its
   deadline;
2. installs the formal protection observer using subscribe-then-read ordering
   while retaining the preflight protection epoch guard;
3. atomically promotes the same Emergency Stop readiness reservation to a
   formal `ILocalEmergencyStopRegistration` under the registrar gate;
4. revalidates the complete epoch bundle and formal owner generations;
5. calls controller Start with frame admission closed;
6. adds the exact participant with the frozen role; and
7. publishes final correlated Admission state before frame admission can open.

Emergency Stop readiness promotion installs the callback only after Ready. A
reservation can protect local registrar capacity and detect local replacement,
but it must not pretend that an operating system can reserve a global hotkey or
permission when that platform offers no such primitive. Promotion rechecks and
registers the real native owner; failure remains pre-capture and terminal.

The preflight protection guard and formal observer overlap until the formal
owner has accepted a fresh Safe observation, so there is no unobserved transfer
gap. Temporary source, permission, Trust, connection, Emergency Stop readiness,
and protection reservations are Preparation guards, not live sharing authority
owners. This preserves NR2.10's requirement that formal protection and
Emergency Stop ownership begin only after Ready success.

## Ownership, rollback, and cleanup

The host runtime generation exclusively owns the reservation and every fact
lease it collects. Acquisition failure unwinds already acquired fact leases in
reverse order and never selects a route.

Before `RouteAdmitted`, terminal cleanup closes local frame admission, retires
callbacks, releases temporary reservations, and disposes the borrowed host
connection lease. It does not claim that a route or media role was consumed.

At or after `RouteAdmitted`, every terminal outcome first closes frame admission
and then shares one connection fail-close and one complete cleanup task. It
consumes the connection-owned media session and route, closes the owning
authenticated control connection, and releases controller, protection,
Emergency Stop, queue, media, and callback owners. A route operation that may
have performed a side effect before throwing is treated as post-route even when
no binding was returned.

Partial promotion is also post-route. If formal protection subscription or
Emergency Stop promotion succeeds and a later check fails, cleanup unregisters
that owner and continues through every remaining stage. One cleanup failure
cannot skip another owner. Primary and cleanup exceptions retain their local
identity and deterministic aggregate order, while peers and diagnostics receive
only allowlisted payload-free reason codes.

Disposing or invalidating a reservation is idempotent. Concurrent Stop,
revocation, timeout, fail-close, callback, and Dispose callers join the same
terminal transition and cleanup completion. Unconfirmed security-owner cleanup
is terminal for that coordinator generation and blocks restart.

## Deterministic concurrency acceptance

Task 5.5a cannot close from sequential callback injection alone. Tests use
explicit barriers and dedicated threads and prove both legal orders for every
source, permission, Trust/Capability, authenticated-connection, Emergency Stop,
and protection mutation:

- mutation before route admission: zero route and zero Prepare;
- route admission before mutation but mutation before Prepare send admission:
  owned route consumed, zero Prepare;
- Prepare send admission before mutation: at most one exact Prepare, then
  fail-close, with zero capture, participant Admission, Driver, input, frame, or
  rendering authority;
- source invalidation joins cleanup without holding a source-use scope across
  the network wait;
- permission revision `N` Granted, `N+1` Revoked, and `N+2` Granted cannot
  revive the reservation for `N`;
- Capability revoke and regrant cannot revive the old Trust reservation;
- connection revoke, fail-close, owner release, and Dispose race both sides of
  route admission without publishing a route after revoke wins;
- Emergency Stop reservation races a competing reservation, registration,
  registrar disposal, Ready promotion, cancellation, and registration loss;
- deadline equality and exact caller cancellation race route and send admission;
- simultaneous invalidations from multiple facts cause one terminal transition,
  one fail-close task, and one cleanup task; and
- route side-effect-then-throw, reservation release failure, partial promotion,
  and cleanup failure retain all failures while later owners still drain.

Tests must not rely on `Thread.Sleep`, thread-pool availability, polling before a
competing operation has started, or a callback placed only between sequential
reads. Bounded barriers identify the exact admission point. Repeated pressure
runs supplement but do not replace the deterministic two-order assertions.

The production-boundary matrix keeps H0, H1, TX, HC, and CL partial or missing
until these scenarios cross the real Desktop, Security, Transport, and Platform
modules and prove the complete owner graph.

## Managed and native evidence boundary

The epoch bundle, host reservation state machine, exact Trust reservation,
Emergency Stop reserve/promote contract, authenticated route operation,
Transport send-admission hook, ABA resistance, and deterministic cleanup races
are managed production-contract work. They belong to Task 5.5a and must be
implemented before its protocol/runtime prerequisite is accepted.

Native platform gates separately prove that each adapter supplies truthful
epochs and invalidation observations:

- macOS TCC and Accessibility state, ScreenCaptureKit source/protection loss,
  secure input, and local Emergency Stop registration;
- Windows capture/input permissions, exact window lifetime, secure desktop, and
  emergency action;
- Wayland portal/PipeWire/RemoteDesktop lifetime, compositor limits, X11 named
  degradation, and local Emergency Stop behavior.

An OS can revoke a permission or source outside Flowspan's locks. The managed
linearization point is therefore the adapter's authoritative observation commit,
not an invented lock over external OS state. Matching-host and packaged
real-machine evidence must measure and fail closed over observation latency,
native call revalidation, and registration loss. Hosted managed CI cannot prove
those facts.

Implementing the managed contract does not make production Remote Window
available and does not satisfy native, physical two-device, signing,
notarization, accessibility, or release gates.

## Rejected alternatives

### Repeat every snapshot immediately before route and Prepare

Additional reads reduce an average race window but do not create an ordering
against a mutation on another thread. They also spread security ordering across
the coordinator and its tests. Rejected as the acceptance contract; repeated
reads remain useful defense in depth.

### Hold a native source-use scope through Preparation

A source-use scope is designed to cover one complete native capture or input
call. Holding it across a network deadline would block source invalidation and
external close, and could form cleanup cycles. Rejected.

### Register formal protection and Emergency Stop owners before Prepare

This would make participant preparation own live safety resources before Ready
and contradict NR2.10 and ADR 0026. Rejected. Short-lived, non-authorizing epoch
guards and an Emergency Stop readiness reservation are promoted only after
Ready.

### Use one global host epoch

Source, permission, Trust, connection, Emergency Stop, and protection are owned
by independent modules and have independent replacement and ABA behavior. A
single counter either misses those identities or forces unrelated modules under
one broad lock. Rejected; the reservation carries an exact epoch bundle and one
local generation only for stale-callback exclusion.

### Implement the ordering independently in each OS adapter

The route and Prepare send-admission points are shared Desktop and Transport
decisions. Duplicating them in three OS projects would make security behavior
platform-dependent and leave only expensive native testing for a portable
invariant. Rejected. Native adapters report exact facts; the shared reservation
owns their composition.

## Clean-room statement

This contract is derived from Flowspan's approved requirements, ADR 0021 safety
model, ADR 0026 protocol state machine, current narrow platform seams, and the
observed production-boundary gaps. It is an original clean-room design. It does
not copy Deskflow source code, data structures, wire formats, naming, or GPL
implementation text.

## Consequences

- Task 5.5a remains blocked until the Desktop core is connected to the real
  Platform, Security, Transport, and coordinator fact owners and its managed
  production-boundary evidence is reproducible.
- The coordinator gains one deep host Preparation reservation module instead of
  more caller-visible read ordering.
- Security gains an exact, revocable operation-Capability reservation rather
  than treating any-of connection admission as Mirror authority.
- Platform gains an Emergency Stop readiness reservation that can be promoted
  without early formal registration.
- Transport gains a generation-bound route-selection operation and one bounded
  host reservation hook at the existing Prepare send-admission point.
- Existing protocol-1.5, 1.6, and 1.7 wire fixtures remain unchanged.
- `CreateProduction()` remains Remote Window unavailable until this contract,
  all native adapters, packaged real-machine evidence, physical testing, and
  release gates independently pass.
