# ADR 0027: Remote Window host Preparation reservation

- Status: Accepted implementation contract; remaining matrix/native evidence open
- Date: 2026-08-30
- Decision owners: Flowspan maintainers
- Source-composed checkpoint: `ec63942`
- Emergency Stop readiness-composed checkpoint: `8e349cc`
- Trust/Capability-composed checkpoint: `635dc23`
- Permission-composed checkpoint: `d607ed1`
- Authenticated Connection-composed checkpoint: `259c3bb`
- Native Protection-composed checkpoint: `c987ca8`
- Protection exact evidence/test-stabilization checkpoint: `457a2c4`
- Remaining fact composition and order/fault coverage pending

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

Before the source-composed checkpoint, the managed host coordinator performed
several point-in-time reads and repeated them immediately before route
selection. It also subscribed early to permission and authenticated-connection
loss. Those checks reduced the ordinary race window and provided useful
fail-close defense, but did not define one linear order against changes made
from arbitrary threads:

- a source can invalidate after the last lease read;
- a permission revision can change after the last permission snapshot;
- the current Mirror grant has no exact revision or operation reservation;
- an authenticated connection can revoke between its current check and media
  route selection; and
- Emergency Stop readiness is deliberately a prompt-free point probe that
  claims no registration ownership.

At that point, a later post-Ready revalidation prevented these races from
granting capture or participant authority, but a stale Prepare could still be
admitted to the wire or a route selected after a logically earlier revocation.
That violated the pre-Prepare ordering even when final capture remained closed.
Repeating reads at more source locations could not prove the missing arbitrary-
thread ordering. The source-composed checkpoint below closes one exact Source
order; the other facts and source orders retain this gap.

This gap is represented by the H0 and H1 families in the
[Remote Window production-boundary fault matrix](../testing/remote-window-production-boundary-matrix.md).
It is a blocker for Native Remote Window Task 5.5a. The contract is now accepted
and partially implemented by the vertical checkpoints below, but Production
Remote Window must remain unavailable while the remaining production-composed
orders, fault intersections, native adapters, and release gates are incomplete.

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
does not close H0, H1, Task 5.5a, or production availability. The later seam
and source-composed checkpoints provide the first source-only vertical without
changing that historical core result.

### Source-composed vertical checkpoint

Commit `ec63942296175f63964d8f463335d6b621e22042` connects the same Desktop
reservation to the exact Platform source lease's atomic invalidation slot, the
generation-bound authenticated responder-route operation, the actual Transport
Prepare send-admission hook, exact Ready matching, promotion, and coordinator
cleanup. The source guard is installed before preflight, remains live through
post-Ready source and protection revalidation, and is released only after the
same reservation promotes.

The production-composed managed tracer now proves source order `R < M < S`:
after the real authenticated route is selected, source unregister linearizes
under the source-state mutation gate; the later real send-admission hook rejects
with zero Prepare wire delivery, capture, media, render, or Admission; and the
owned connection, route, directories, handlers, leases, controller, and host
generation drain. The existing two-node success tracer traverses that same
reservation, route, send, Ready, and promotion path. The exact local and hosted
evidence is recorded in the
[source-linearization checkpoint](../evidence/2026-08-30-host-preparation-source-linearization.md).

This checkpoint implements only the Source fact's vertical composition. The
production-composed `M < R` and `S < M` source tracer orders remain open, as do
Permission, Trust/Capability, authenticated Connection mutation, Emergency Stop
reserve/promote, Protection, and the complete per-boundary fault matrix. The
aggregate H0/H1 cells therefore remain P or M, Task 5.5a remains unchecked, and
`CreateProduction()` remains unavailable.

### Emergency Stop readiness-composed vertical checkpoint

Commit `8e349cc7d9f722caa7e6df404ec6a59117d7d588` implements the first
Emergency Stop fact vertical. The managed process-local registrar now owns one
exact readiness slot bound to host owner and Session generations. Reservation
installs no activation callback. Readiness loss and one-time promotion to the
formal registration linearize under the registrar gate, so exactly one of the
pre-Ready invalidation sink or the post-promotion registration-loss callback
owns a concurrent loss.

The Desktop coordinator reserves that slot before route admission and promotes
the same owner only after exact Ready, media attachment, host-fact revalidation,
and a fresh formal protection observation. Promotion failure, registration loss,
registrar disposal, cancellation, or side-effect-then-throw remains pre-capture
and retains any formal owner for ordered cleanup. The formal registration is
released only after the controller's capture, input, and sharing boundaries
have stopped.

The production-composed managed tracer proves Emergency Stop `R < M < S`:
after a real authenticated route is selected, process-local registrar loss
invalidates the exact host reservation, and the later actual Transport
send-admission hook admits no Prepare wire or later authority. Exact local and
hosted evidence is recorded in the
[Emergency Stop readiness checkpoint](../evidence/2026-08-30-host-emergency-stop-readiness-reservation.md).

This is not a native hotkey or operating-system action. At that checkpoint the
other production-composed Emergency Stop `M/R/S` orders, its complete fault
matrix, real Windows/macOS/Linux registration behavior, Source `M < R` and
`S < M`, Permission, Trust/Capability, authenticated Connection mutation,
Protection, and the complete owner/fault matrix remained open.

### Trust/Capability-composed vertical checkpoint

Exact commit `635dc23ec0c8f2812d527e16135b3d9c40885788` implements the first
Trust/Capability fact vertical. Its prerequisite commit `1c1999c` binds the
authenticated peer public-key fingerprint proved by the real handshake to the
generation-bound Remote Window connection lease. Security commit `7a1349b`
adds an exact process-local reservation for peer Device ID, that fingerprint,
and an all-of Capability set under the existing Trust mutation gate; repair
`4138b03` preserves fatal `OutOfMemoryException` identity through invalidation
and active-session Stop.

Every Applied revoke or Capability update invalidates all matching Preparation
registrations under the mutation gate after store commit and before ordinary
`Changed` observers or active-session Stop. This deliberately includes an
Applied update whose resulting grant equals its predecessor. All matching
registrations are deactivated before non-fatal invalidation sinks run, so a
sink failure cannot retain an old reservation or undo the mutation. Monotonic
registration identities prevent revoke/regrant, replacement identity, and late
old disposal from affecting an ABA replacement.

The Desktop adapter maps ViewOnly to `mirror.view` and DriverEligible to the
all-of set `mirror.view` plus `mirror.drive`. The coordinator acquires the
reservation before route admission, uses the existing host reservation as its
bounded invalidation sink, checks the exact registration again before
promotion, releases it after promotion, and owns it through terminal cleanup.
Missing authenticated fingerprint, identity replacement, denial, unexpected
non-fatal failure, exact caller cancellation, and fatal exhaustion preserve the
fixed fail-closed classifications defined by the implementation.

The production-composed managed tracer proves Authorization `R < M < S`: a
real authenticated responder route is selected, an Applied same-grant update
invalidates the exact reservation, and actual Transport send admission emits no
Prepare wire or later authority before both nodes drain. Exact local and hosted
evidence is recorded in the
[Trust/Capability Preparation checkpoint](../evidence/2026-08-30-host-trust-capability-preparation-reservation.md).

This single managed order does not complete Authorization `M < R`, `S < M`, or
its reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault intersections.
Permission, authenticated Connection mutation, Protection, remaining Source
and Emergency Stop orders, and native/physical behavior remain open. Aggregate
H0/H1 cells therefore stay P or M, Task 5.5a stays unchecked, and
`CreateProduction()` stays unavailable.

### Permission-composed vertical checkpoint

Exact commit `d607ed1c3217c9c4102c4b893d20da9a6845f02d` implements the first
Permission fact vertical. Platform defines a narrow synchronous, prompt-free
Preparation reservation that binds the exact permission owner generation,
revision, capture and input facts, and frozen participant role. ViewOnly
requires Granted capture; DriverEligible requires Granted capture and input.
Snapshot drift or required-role denial invalidates Permission, while an
unsupported, unavailable, disposed, or absent reservation boundary fails closed
as `native_permission_unavailable`.

The macOS permission boundary participates under the same gate used to commit
accepted CoreGraphics observations. A changed fact advances the revision,
deactivates every current Preparation registration, and invokes their bounded
invalidation sinks before ordinary `Changed` observers. A repeated equal fact
does not invalidate. Operation sequencing prevents an older native completion
from overwriting a newer commit, and registration identity prevents
Revoked/Granted or late-dispose ABA from reviving or removing a replacement.
All registrations become inactive before sink delivery; non-fatal failures do
not block later sinks or observers, and fatal exhaustion remains unwrapped.

The coordinator acquires the exact permission registration after the source
reservation and before route admission, receives ownership synchronously before
the reservation operation can later throw, checks the same registration again
before host-reservation promotion, releases it after promotion, and owns it
through terminal cleanup. The existing permission observer and current reads
remain live-session defense in depth, not substitutes for this pre-Prepare
guard.

The production-composed managed tracer proves Permission `R < M < S`: after a
real authenticated responder route is selected, a managed Granted-to-Revoked
revision invalidates the exact reservation, and actual Transport send admission
emits no Prepare wire or later authority before both nodes drain. Regrant does
not revive the terminal generation. Exact local and hosted evidence is recorded
in the
[Permission Preparation checkpoint](../evidence/2026-08-30-host-permission-preparation-reservation.md).

This checkpoint does not prove a real macOS TCC revoke. The macOS adapter's
commit gate is tested with controlled interop; its matching-host test only
reaches the prompt-free preflight call, input remains `Unsupported`, and the
managed tracer does not instantiate that native boundary. Windows and Linux
native permission boundaries remain unimplemented. Production-composed
Permission `M < R`, `S < M`, and the complete Permission fault matrix also
remain open. Authenticated Connection mutation, Protection, the remaining
Source, Authorization, and Emergency Stop orders, and the complete matrix still
block H0/H1 and Task 5.5a. `CreateProduction()` remains unavailable.

### Authenticated Connection-composed vertical checkpoint

Exact commit `259c3bbda4648bc6c45b71d78fbc7a34feb4de71` replaces the
authenticated Connection point read with one composite Preparation
registration owned simultaneously by the exact connection generation and its
exact media session. Owner claim is synchronous and rolls back both slots on a
partial failure. Monotonic registration identity prevents generation/media
replacement and late-dispose ABA.

Generation revoke, explicit or deferred fail-close, media Dispose, control
Stop, and responder-route invalidation deactivate the old registration under
their authoritative gate. Responder-route selection and the actual Transport
Prepare send hook require that exact registration. A public, omitted, foreign,
or stale owner cannot cross either gate. The coordinator owns the registration
before route, rechecks it before host promotion, and releases it only after the
live revocation callback is installed; cleanup retains the same exact owner on
every failure path.

The managed tracer covers one authenticated disconnect after route selection
and one exact media mutation during the post-promotion, pre-capture handoff.
The disconnect row terminates before actual send-admission entry, so a separate
two-lease Transport regression supplies send-gate evidence. The media row
reaches Ready and verified `FSM1` attachment, then observes the live callback
and Emergency Stop before capture. Exact local, hosted, static-analysis, and
unsigned-package evidence is recorded in the
[Connection Preparation checkpoint](../evidence/2026-08-30-host-connection-preparation-reservation.md).

Production-composed Connection `M < R` and `S < M`, the remaining disconnect/
cleanup-fault intersections, native behavior, and the complete matrix remain
open. This checkpoint does not promote H0/H1 or make production available.

### Native Protection-composed vertical checkpoint

Exact implementation commit
`c987ca84e1f9f867f0edef3222a94dc8d25a2583` binds one source-owned
Protection Preparation registration to the complete accepted observation:
owner, Session, and source generations; revision; kind; `ObservedAt`; and source
identifier. Reservation requires exact equality and fresh `Safe`, transfers
ownership synchronously under the source mutation gate, rolls back a failed
claim, and uses monotonic registration identity to prevent replacement and
late-dispose ABA.

The host reservation binds the complete inclusive freshness interval from
`ObservedAt - MaximumFutureClockSkew` through
`ObservedAt + MaximumProtectionAge`. Arm, route admission, actual Prepare send
admission, Ready matching, and host promotion all reject time before or after
that interval; request-deadline equality remains expired and takes priority.
The source independently rechecks exact identity and freshness at Protection
promotion and at capture-start admission.

The same registration moves monotonically through `Temporary`,
`FormalPreStart`, and `Live`. It remains Temporary through Prepare, Ready,
verified media attachment, ordinary-observer subscribe/read, and final host
fact validation. Immediately before host-reservation promotion, the coordinator
promotes that exact object to FormalPreStart. After the controller publishes
`Starting`, capture-start admission under the same source mutation gate either
rejects a mutation-first generation with zero capture or promotes the exact
registration to Live immediately before source use/native capture.

Every live observation first closes controller Protection admission and latches
its exact observation/admission epoch under the source mutation gate. Notify
runs outside that gate and before ordinary observers. The host retains a bounded
FIFO with active, deactivating `ExecutionContext` ancestry, so stale copied
contexts rejoin a later drainer and symmetric A↔B callbacks do not deadlock.
Source loss is terminal and queue overflow fails closed. Every native frame
destination and native or semantic input adapter call holds an exact drainable
`ProtectionAdmissionUse`; mutation closes new use admission first. A current
Safe reconciliation reopens only when the same epoch/observation still owns an
Active/Capturing session and all earlier uses have drained, with reentrant use
ancestry safely deferring its own wait.

The managed production tracer proves only Protection `R < M < S` for
`SecureInput` and `Unknown`: each mutation follows real authenticated route
selection, makes the actual send hook return `NotDelivered`, emits no Prepare
wire or later authority, and drains both nodes. Exact evidence tree
`457a2c4b9e3d6905218e826cedd60029bbd1b35e` changes only one test cleanup
barrier after the bare implementation commit. Exact local, hosted, Secret Scan,
CodeQL, and reproducible unsigned-package evidence is recorded in the
[Protection Preparation checkpoint](../evidence/2026-08-30-host-protection-preparation-reservation.md).

Protection `M < R`, `S < M`, the complete promotion/capture/live/source-loss/
cleanup-fault matrix, native platform probes, physical devices, signing,
notarization, and release acceptance remain open. Tasks 5, 5.5a, and 5.5,
aggregate H0/H1, `CreateProduction()`, and the Goal remain open.

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
- **Protection:** retain one exact full-observation registration that grants no
  authority while `Temporary`, then promote that same registration after Ready
  through `FormalPreStart` to `Live`. A live observation first closes Protection
  admission and latches its exact epoch under the source mutation gate; bounded
  formal notification then reconciles the controller outside that gate. The
  ordinary post-Ready observer remains defense in depth rather than the transfer
  owner.

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

1. subscribes the ordinary protection observer and reads one fresh exact
   observation while the exact Protection registration remains `Temporary`;
2. atomically promotes the same Emergency Stop readiness reservation to a
   formal `ILocalEmergencyStopRegistration` under the registrar gate;
3. revalidates Source, Permission, authenticated Connection,
   Trust/Capability, and Protection together with the complete host reservation
   and formal owner generations;
4. promotes the same Protection registration to `FormalPreStart`;
5. promotes the host Preparation reservation;
6. releases the temporary Source, Permission, authenticated Connection, and
   Trust/Capability Preparation owners;
7. calls controller Start with frame admission closed; immediately before the
   first source use or native capture, admission under the source mutation gate
   promotes that same Protection registration to `Live`; and
8. adds the exact participant with the frozen role and publishes final
   correlated Admission before frame admission can open.

Emergency Stop readiness promotion installs the callback only after Ready. A
reservation can protect local registrar capacity and detect local replacement,
but it must not pretend that an operating system can reserve a global hotkey or
permission when that platform offers no such primitive. Promotion rechecks and
registers the real native owner; failure remains pre-capture and terminal.

Protection transfer is a monotonic transition of one exact registration rather
than an overlap between an independent guard and formal observer. Mutation wins
under the same source gate at each transition, and the ordinary observer is
defense in depth only. Temporary source, permission, Trust, connection,
Emergency Stop readiness, and Protection registrations are Preparation guards,
not live sharing authority owners. The first formal Protection state and the
Emergency Stop owner are installed only after Ready success, preserving NR2.10.

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

The Permission-composed checkpoint implements that observation-commit contract
for the current macOS screen-capture adapter and makes it deterministically
testable. It does not add continuous TCC observation, Accessibility/input,
ScreenCaptureKit capture, or a packaged grant/revoke result, and therefore is
not native permission acceptance evidence.

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

### Register live formal protection and Emergency Stop owners before Prepare

This would make participant preparation own live safety resources before Ready
and contradict NR2.10 and ADR 0026. Rejected. The implemented Protection object
is a non-authorizing `Temporary` registration before Ready and becomes formal
only through the post-Ready `FormalPreStart` and capture-gated `Live`
transitions; Emergency Stop likewise remains a readiness reservation until its
post-Ready promotion.

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

- Authenticated Connection and Protection are now connected to their exact
  managed owners. Task 5.5a remains blocked by the other `M < R`, `R < M < S`,
  and `S < M` orders, the per-boundary reject/throw/cancel/timeout/revoke/
  disconnect/cleanup-fault intersections, and the still-incomplete managed
  production-boundary matrix. One production-composed order for each connected
  fact is insufficient.
- The coordinator gains one deep host Preparation reservation module instead of
  more caller-visible read ordering.
- Security gains an exact, revocable operation-Capability reservation rather
  than treating any-of connection admission as Mirror authority.
- Platform has a managed process-local Emergency Stop readiness reservation
  that can be promoted without early formal registration; native adapters and
  physical behavior still require separate evidence.
- Platform has an exact prompt-free Permission Preparation reservation, and the
  macOS boundary has a deterministic observation-commit gate. Real TCC revoke,
  Accessibility/input, Windows/Linux native permission behavior, and packaged
  recovery still require separate evidence.
- Platform has one exact Protection registration spanning Temporary
  Preparation, formal pre-start, live notification, capture admission, native
  frame destination, and native/semantic input-use scopes. Native observation
  latency, secure-input/source-loss probes, physical devices, and every
  remaining fault intersection still require separate evidence.
- Transport gains a generation-bound route-selection operation and one bounded
  host reservation hook at the existing Prepare send-admission point.
- Existing protocol-1.5, 1.6, and 1.7 wire fixtures remain unchanged.
- `CreateProduction()` remains Remote Window unavailable until this contract,
  all native adapters, packaged real-machine evidence, physical testing, and
  release gates independently pass.
