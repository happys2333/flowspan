# Native Remote Window Design

Status: portable contracts, shared authenticated control composition, Task 4
media-route/codec contracts, and Task 5.1-5.4 Transport composition implemented;
protocol-1.7 Preparation codec/managed-session candidate implemented; production
Desktop Preparation runtime and native adapters pending

Requirements: NR1-NR10

## 1. Delivery shape

The native work extends the existing portable controller instead of replacing
it. Domain and protocol code continue to own Activity identity, Capability,
Driver Lease, protection policy, state transitions, encryption, replay checks,
and media budgets. Platform projects own only local operating-system resources.
The Desktop project composes those two layers and owns presentation.

The implementation proceeds in vertical slices:

1. freeze the native source, permission, protection, capture, input, and local
   Emergency Stop contracts with deterministic fakes;
2. compose Remote Window control over the existing authenticated peer connection;
3. freeze the authenticated media-route and codec decisions and fixtures;
4. compose those decisions in the production Desktop host and participant runtime;
5. deliver macOS, then Windows, then Linux platform adapters and packaged
   real-machine evidence.

No platform becomes generally available because another platform's adapter is
complete. Readiness is computed per operation and per current machine.

## 2. Dependency direction

```mermaid
flowchart LR
    Desktop["Flowspan.Desktop\nselection, permission UX, rendering"]
    Platform["Flowspan.Platform\nportable native contracts and controller"]
    Native["Flowspan.Platform.<OS>\nOS adapters"]
    Transport["Flowspan.Transport\nauthenticated control and bounded media"]
    Domain["Flowspan.Domain/Application\nActivity, Trust, Capability, Driver"]

    Desktop --> Platform
    Desktop --> Native
    Desktop --> Transport
    Native --> Platform
    Platform --> Domain
    Transport --> Platform
    Transport --> Domain
```

Platform projects must not reference Desktop or Transport. They return typed
facts, opaque local tokens, bounded frame buffers, and `LocalBoundaryResult`.
They contain no user-visible prose. Desktop adapts native permission facts to its
resource-backed presentation model. Transport never receives a native handle.

## 3. Ephemeral generic-window source

A platform source catalog enumerates only locally visible, policy-eligible
windows. It owns an in-memory registry from an unpredictable `NativeSourceToken`
to the native window/process instance and a monotonically increasing generation.
The process-local snapshot contains:

- token, generation, and an ephemeral Flowspan Activity ID used to bind only the
  live Remote Window session;
- bounded local display title and owning application label;
- current geometry revision;
- capture/input support and current protection classification.

Desktop projects each eligible snapshot into a dedicated Remote Window source
inventory with `SupportsSemanticResume=false`. It does not create an Activity
Descriptor or Activity Kind and does not insert the source into the Application
Activity catalog, Handoff, Move, Replace, Swap, Group, or Scene inventories. The
Activity ID is an ephemeral live-session binding, not a semantic descriptor.
Selection and Start carry the exact token/generation only inside the local
process. The wire continues to carry Activity ID, not the token or native handle.

The registry invalidates an entry when the window closes, its owning process
instance changes, geometry identity becomes ambiguous, or policy excludes it.
Its 128-state process bound counts both visible entries and removed states whose
use or callback drain is still pending; registration resumes only after retained
state drain completes.
The controller retains its own lease handle, so disposing a caller's selection
handle cannot silently detach a live safety subscription. Invalidation first
closes new use admission, then drains any generation-bound native use, publishes
the controller's `Unavailable` state, closes frame admission, and stops capture,
input, and participant sessions before an external source-close call returns.
The registry retains removed states until their drain completes, so concurrent
registration and registry disposal join the same invalidation. A re-entrant
close from an active native use or invalidation callback ancestry defers only
when the target already requires a wait; the original drainer or use-scope exit
completes it. This prevents cross-source circular waits while every top-level
external close still joins the drain. Per-invocation callback activity prevents
a stale copied execution context from bypassing a later drain. The portable v1
registry permits only a bounded display-name update in place. A change to owning
application, geometry, capture/input support, or protection is a
security-binding change: it closes new use admission under the registry lock,
removes the old catalog entry, drains its exact use and invalidation callbacks,
and returns a failed update so the platform enumerator must register a fresh
source. This conservative retirement prevents capture on one geometry while
input uses another; a future atomic native rebind requires a separate design.
When a different active source-use ancestry could make two display-only updates
wait on each other, `TryUpdate` returns false and restores use admission instead
of blocking. Titles are never identity.

## 4. Portable native contracts

`Flowspan.Platform` gains narrow contracts rather than one broad platform
service:

- `INativeRemoteWindowSourceCatalog` for bounded prompt-free enumeration and
  exact token/generation resolution;
- `INativeRemoteWindowPermissionBoundary` for prompt-free snapshots and explicit
  capture/input requests;
- `INativeRemoteWindowCaptureBoundary` implementing the existing capture gate
  plus bounded frame delivery and exact source binding;
- `INativeRemoteInputBoundary` implementing the existing input gate plus current
  geometry mapping;
- `INativeProtectionSource` publishing timestamped typed observations;
- `ILocalEmergencyStopRegistration` registering one independent local action.

Generic-window capture and input cross the native boundary with a process-local
`NativeRemoteWindowSourceUse`. It contains the opaque token, ephemeral Activity
and host IDs, owner and Session generations, source generation, and geometry
revision, but no title, application label, native handle, or process identity.
The token is ignored by JSON and every diagnostic projection. Platform adapters
must resolve the token and re-check every generation immediately before the OS
capture call or first input event; a whole input batch fails before injection if
the geometry revision no longer matches. The controller acquires a source- and
geometry-bound use scope immediately before capture admission or input and holds
it across the complete native boundary call. Source invalidation and any
security-binding metadata change close new admission before waiting for an
existing scope to drain. The controller re-checks after each boundary as cleanup
evidence, not as permission to undo partial native work.

Callbacks include an owner generation and never expose native exception text.
Frame delivery uses an owned bounded buffer that must be disposed by the
consumer. The controller wraps the destination in a source-, owner-, Session-,
geometry-, and sequence-bound sink with one in-flight delivery and one
latest-frame pending slot. Wrong, stale, non-advancing, replaced, late, or
downstream-failed frames are disposed exactly once. Delivery crosses the
destination only while the exact Session is Active, capture is confirmed, and
the current protection observation is fresh Safe. A blocked or failed protection
pause therefore drops late frames without closing the exact binding; a confirmed
Safe resume can admit a later advancing sequence. Predicate failure or a
destination exception emits one typed terminal fault, moves the controller to
`Unavailable`, and stops capture, input, and participant gates. `CloseNow`
rejects new and pending frames without waiting for a blocked destination;
ordinary Stop drains an in-flight delivery after closing producers. Controller
disposal invoked from any frame callback closes immediately and defers final
resource release until the registered delivery exits. Top-level external
controller disposal joins every registered operation, including a blocked frame
delivery, before it returns. Concurrent external disposers first join the one
initial fail-close attempt and then a `NotStarted` / `InProgress` / `Completed`
finalization barrier, so no caller can observe final cleanup merely started and
release a borrowed boundary early. Fail-close and final cleanup retain
controller-local recursion tokens while also entering the shared drain-activity
chain. Controller operations, frame deliveries, protection and Emergency
callbacks, source uses, and source-invalidation callbacks all use that one
process-wide chain; every invocation becomes inactive when it exits.
Synchronous or `Task.Run` child disposal can therefore identify its active
parent activity. When a controller target actually has work that would require
waiting, any active shared ancestry defers the wait after fail-close. This
breaks same-kind and cross-component disposal cycles without weakening a
top-level external caller's drain guarantee. A copied context loses the
exemption when its original invocation exits and must join any later drain. The
last registered operation claims finalizer ownership under the same lock before
waking external waiters; this prevents an external finalizer from waiting on a
source callback that is itself trying to exit. New
source-invalidation callback admission closes with disposal before that final
zero-operation observation. The contract permits at most one active capture
source per local v1 runtime. The portable controller remains the only authority
for pause, resume, input, and stop decisions.

Frames and protection observations carry owner, Session, source, and applicable
geometry generations. Protection state commits before observer delivery.
Observer delivery is revision ordered, externally drained on disposal, and
bounded to eight queued notifications; overflow coalesces to `Unknown` rather
than allowing a newer Safe observation to erase an undelivered unsafe state.
Emergency Stop registration is one-shot and carries exact owner/Session
generations. Registration loss is a named fail-closed activation, a callback
cannot be replaced while it is running, and callback references are cleared on
trigger, loss, unregister, or disposal. Protection, Emergency Stop, source
invalidation, and frame-delivery drain checks track complete callback ancestry
through `ExecutionContext`. Each invocation uses a unique active token, so a
synchronous child worker can dispose an ancestor without waiting on its own
logical call stack, while a delayed worker from an older callback cannot bypass
a later callback's drain. Frame delivery uses a shared family owner plus a
per-delivery token. A sink defers only under active frame-delivery ancestry,
which prevents symmetric destinations from deadlocking. In a cycle with a
controller, protection source, Emergency registrar, or source registry, that
other component sees the frame activity and yields; the sink can then finish its
delivery. This direction preserves ordinary controller Stop, which must still
join its owned delivery. A top-level external sink or boundary disposer has no
active scope and waits for completion.

The existing controller currently accepts a semantic `ActivityInstance`. This
slice replaces that dependency with a bounded `RemoteWindowSourceReference`
containing Activity ID, optional semantic kind, display label, host Device, and
source generation. A compatibility factory may adapt an active semantic Activity,
but a generic native source has no descriptor or synthetic `remote.window/v1`
kind. Protocol 1.5 continues to bind the existing Activity ID field, so this
refactor changes no frozen wire fixture.

The controller owns its retained source lease, invalidation registration, and
bounded frame-sink lifetime. It borrows native capture/input implementations;
the production Desktop runtime owns those asynchronous native resources and
must dispose them only after controller quiescence during task 5.

## 5. Production Desktop runtime

Task 5's `ProductionDesktopRemoteWindowService` will own one runtime generation
containing:

- the exact selected native source lease;
- `RemoteWindowSessionController` and unpredictable Session ID;
- the current authenticated control endpoint;
- the process-level authenticated media directory shared with the control
  handler and published listener;
- one generation-bound peer media connector/listener-endpoint lease;
- one bounded pending Preparation owner and terminal tombstone;
- capture-to-encoder and decoder-to-renderer queues;
- protection subscription and local Emergency Stop registration;
- cancellation and complete asynchronous disposal.

Start order is strict. The host revalidates Activity/source, its current grant to
the participant, prompt-free permission, connection, protection, Emergency Stop,
and media ownership; allocates the controller/Session; prepares the responder
route; and sends protocol-1.7 Prepare before native capture. The participant
validates the current authenticated receiver connection and policy, returns the
control read loop immediately, and uses an owned worker plus the generation-bound
connector lease to attach `FSM1` and prepare its renderer. It does not require a
reciprocal Mirror grant. Only after Ready success does the host revalidate every
fact, register protection and Emergency Stop ownership, cross native capture with
frame admission closed, add the exact participant, and return final Admission
state. Only that exact accepted state opens participant rendering. Any failure
unwinds in reverse order and retains no participant or Driver authority.

[ADR 0027](../../../docs/adr/0027-remote-window-host-preparation-reservation.md)
defines the accepted, partially implemented exact epoch bundle, route/send
admission ordering, Emergency Stop readiness promotion, and deterministic
concurrency acceptance needed to make that pre-Prepare order linearizable
across arbitrary threads. The remaining mutation orders, per-boundary fault
intersections, native/physical evidence, and release gates still block Task
5.5a; point-in-time revalidation remains defense in depth and cannot close
those gates by itself.

The media directory must be the same instance passed to the authenticated
control handler and the shared `FlowspanTcpInboundListener`; a Desktop-only or
second directory cannot find the transferred media session. The connector lease
must bind the peer endpoint to the current authenticated connection generation,
not merely reuse an unverified discovery address. The runtime never receives an
`AuthenticatedRemoteWindowMediaSessionRegistration`; it retrieves the current
connection-owned session by exact peer from the directory, and loss of that
registration invalidates preparation.

The existing Desktop reducer consumes only controller snapshots and permission
facts. It does not infer native success from a task completing. Production
composition keeps `native_adapters_unavailable` until every mandatory host and
participant component for the selected role reports ready.

## 6. Authenticated control and media routing

Task 3 replaces the competing Activity and Remote Window read loops with one
connection-owned dispatcher. It routes strict, version-gated message types to
Activity/Replace/Swap/Scene or Remote Window peers and exposes their outbound
channels from the same authenticated registration. Protocol 1.0-1.4 registrations
do not expose a Remote Window channel; protocol 1.5 does. The production any-of
admission profile treats `activity.offer`, `activity.receive`,
`activity.replace`, `activity.swap`, `scene.apply`, `mirror.view`, and
`mirror.drive` as connection alternatives. Admission establishes only an idle
authenticated control channel; every operation rechecks its exact current grant,
and unknown or cross-routed message types remain fatal.

Task 3 composes only that authenticated control channel. Task 4 freezes the
media-route and codec decisions below. Task 5 is delivered as vertical slices:
the current Transport slice composes the same-listener attachment, live
control-owned media-session lifetime, logical-frame queue, and reassembly, while
the complete production Desktop host/participant runtime remains open.

The media stream stays distinct from control keys, framing, counters, queues, and
rekey state as required by ADR 0022. ADR 0024 assigns this attachment contract to
protocol 1.6 and preserves every 1.5 control and media fixture byte-for-byte.
Protocol 1.5 continues to expose Remote Window control; only 1.6 or later exposes
the production media-route feature.

ADR 0026 assigns host-selected Preparation to protocol 1.7. Its independent
`remote-window.prepare` and `remote-window.ready` messages bind the exact
correlation, Session, Activity, directed Devices, role, deadline, and canonical
domain-separated SHA-256 `prepareDigest`. Ready repeats that complete binding and
returns one terminal success/rejection plus a bounded reason. Neither message
reuses Admission or carries the `FSM1` route locator. Protocol 1.5 and 1.6 peers
retain their frozen behavior and cannot be selected for this production
host-initiated workflow.
Their deadline and envelope send times use whole-millisecond UTC. The writer
truncates an observed sub-millisecond send time before deriving the exact TTL and
does not change the bound deadline.
The host keeps the outbound record `Created` until its Prepare send actually
starts. A pre-send Ready is fatal; a success received during send is buffered
until send/Stop/deadline commit, while an exact rejection remains a terminal
acknowledgement even when closing the connection cancels local send flush.

The preparation dispatch path reserves at most one transaction and starts an
owned asynchronous participant worker before returning to the connection's sole
read loop. The worker attaches media and prepares the renderer, then sends Ready
through the existing serialized send path. Stop, deadline, revoke, connection
loss, and disposal cancel and join the worker. A terminal record remains through
the deadline or connection close so replay cannot revive it. Once either side
selects its media route role, every rejection or fault closes the owning control
connection and requires a fresh handshake rather than retrying the old session.
Admission received before Ready send begins is fatal. During Ready send the
participant can buffer at most one exact final Admission without invoking its
endpoint; only send success consumes it, and send failure discards it.

Ready establishes no known binding. After Ready success the host still starts
the controller, adds the exact participant, and sends correlated state. Only an
Admission action with Applied or AlreadyApplied outcome and the exact effective
role establishes the participant's known live binding and opens media-frame
admission. Current host-side Trust/Capability is checked before Prepare, before
capture, and by AddParticipant; the receiving side checks connection/Trust and
local receive policy without interpreting its opposite-direction grants.

The production listener classifies an independent `FSM1` attachment
envelope on the same published endpoint as pairing and authenticated control. Its
bounded clear prefix contains only the versioned purpose and an unpredictable
route ID needed to locate a current in-memory control route. It is not proof of
identity and is never logged. The request and acknowledgement are protected by
the existing directional `FLOWSPAN-REMOTE-WINDOW-MEDIA-V1` keys and bind the
negotiated protocol, both Device IDs in direction, exact route ID, Remote Window
Session ID, Activity ID, and fresh initiator nonce. The responder acknowledgement
echoes that nonce and contributes a responder nonce before either side admits a
media frame.

The route registry is process-local, bounded, time-limited, single-use, and owned
by the live control connection. Closing or revoking that connection invalidates
its route before stream cleanup. Unknown, expired, replayed, already-attached,
wrong-peer, wrong-direction, wrong-session, and wrong-Activity attempts fail
closed. v1 permits at most one attached media stream per authenticated control
connection. Clear route lookup does not grant Capability, session admission,
Driver authority, or access to media plaintext.

The frozen registry defaults to 32 live routes with a hard maximum of 128. A
pending route lives for 30 seconds by default and never more than two minutes;
the process independently remembers at most 512 initiator-nonce fingerprints and
512 consumed route IDs for the maximum route lifetime. Successful registration
reserves its route ID before publication completes. The reservation survives
claim, revocation, and cleanup, and an attached route keeps occupying its history
slot past the replay window until cleanup releases ownership. Either full history
fails new work closed; pruning resumes admission only after the maximum TTL and
after no live owner still requires the route. Timer-arm failure is the one
unpublished-admission case that rolls its history reservation back. Attachment
handshakes use a two-second default and ten-second hard timeout. The fixed request
and acknowledgement are 200 and 232 bytes respectively, and each nonce is 32
bytes. A matched malformed, cancelled, timed-out, or rejected claim consumes the
route; neither cleanup nor retry can republish that identifier inside the replay
window. Long-running processes bound memory without weakening single-use
semantics through arbitrary eviction.

Each authenticated protocol-1.6 control registration transfers its
purpose-separated media `SecureFrameSession` into exactly one connection-owned
media session. The responder registers one route; the initiator connects one
attachment. Route expiry, revoke, matched attachment failure, media send/receive
fault, registration disposal, or listener shutdown requests stop of the owning
control connection. Listener handler cleanup owns the accepted attachment and
attempts disposal even after handler failure; one failure retains its identity and
simultaneous handler and cleanup failures are aggregated.

An encoded logical video frame owns 1 through 1,048,576 bytes and becomes 1
through 16 `RemoteWindowMediaFrame` chunks of at most 65,536 bytes. Every chunk
uses the same Session, Activity, kind, and count with zero-based indexes and a
strictly continuous sequence range. The assembler consumes and clears each chunk,
rejects any binding, kind, order, count, sequence, empty-payload, or aggregate-size
violation, and returns one disposable completed owner. Partial and failed
assemblies are cleared.

The logical sender has one active frame and capacity for only the latest pending
frame. A newer pending frame replaces and clears the older one. The active frame
emits one chunk into the existing peer/session-budgeted capacity-one queue and
waits for its terminal delivery before taking the next, so logical frames cannot
interleave on the wire. Stop, replacement, backpressure, send failure,
cancellation, and disposal settle every submission with a bounded outcome and
clear all retained owners. Queue teardown attempts cancellation, worker join,
sink disposal, budget unregister, and cancellation-source disposal even when an
earlier stage fails.

The attached media `SecureFrameSession` deliberately has no live rekey protocol.
Its attachment envelopes and media frames consume the same directional epoch
budgets. Before either direction would exceed `2^20` protected frames, 1 GiB of
plaintext, or a sequence/epoch boundary, the runtime must close the attachment
and its owning authenticated control connection. Recovery performs a complete
fresh authenticated control handshake, derives a new purpose-separated media
session, and registers its new session-identifier route. It must not raise a
budget, advance the media epoch without an authenticated transition, or reuse the
consumed route.

Implementation commit `a75afb142c335d8da71e511c29e51b14ad2b3cf7`
proves the complete recovery path through the production listener at the
same-host managed-loopback boundary, exercised again by the hosted OS matrix,
with a 2-by-2 matrix: frame-count and plaintext exhaustion, each in the
initiator-to-responder and
responder-to-initiator direction. Shrink-only test
limits apply only to the purpose-separated media session and are identical before
and after recovery. Each case completes the attachment, exchanges exactly one
media frame, rejects the next frame before any wire bytes are emitted, keeps the
media epoch at one, closes both attachment and control, clears both session
directories and the route registry, rejects the consumed `FSM1` request while a
fresh route exists, then completes a new authenticated handshake and exchanges
media through its distinct session-identifier route. A fresh authenticated
transcript also rejects ciphertext from the prior media session.

The exact implementation tree passes 460 Transport tests in both Debug and
Release, 131 Security tests in both configurations, and all 1,878 Release tests
locally on macOS with zero warnings. Exact-commit CI `33109385771` passes the same
1,878 tests on Windows, macOS, and Linux plus Secret Scan and all three
reproducible unsigned package jobs; CodeQL `33109385769` also passes. These are
deterministic managed-loopback and portable hosted contracts. They do not prove
the Desktop runtime, native capture/input/protection, a physical LAN,
accessibility, interactive quality, or packaged real-machine behavior.

This decision gate is intentional: native capture may be developed and tested
behind the bounded frame sink, but production availability remains false until
the Desktop runtime composes exact-source capture, encoder, authenticated media,
decoder, participant renderer, native safety gates, and complete teardown.

## 7. Frame encoding and rendering

The first production codec is independently framed JPEG through directly pinned
SkiaSharp 3.119.4, as frozen by ADR 0025. It is chosen for a small, inspectable
intra-frame failure surface and frame-drop recovery; it is not a claim of
video-codec efficiency. The portable boundary accepts only the existing bounded
BGRA8888 frame contract, including its validated row stride, discards alpha, and
returns owned bytes rather than exposing a Skia type outside `Flowspan.Desktop`.

Capture writes to a capacity-one latest-frame queue. Encoding uses the frozen
ladder: original size at qualities 82, 68, and 54, then 3/4 and 1/2 scale at
qualities 68 and 54. Scaling never enlarges a frame and each candidate is checked
against the logical-frame ceiling before export. Each attempt rents one bounded
scratch buffer and clears it on return instead of allocating a fixed large
object. It never enlarges protocol limits or queues.

The decoder first requires one structurally complete JPEG with no trailing second
image, then uses `SKCodec` metadata to require TopLeft orientation, a single still
frame, positive dimensions no larger than 16,384 on either axis, and at most
16,777,216 pixels, exactly equivalent to 67,108,864 decoded BGRA bytes. Only then
may it allocate one tightly packed BGRA8888 destination. Truncated data, concatenated or
trailing images, unsupported color conversion, animation/multiple frames, other
formats, or an incomplete decode fail closed without returning pixels. The
participant decodes off the UI thread and swaps the latest complete bitmap on the
UI dispatcher. Stale Session, Activity, sequence, or renderer-generation frames
are discarded before presentation.

Encoded and decoded result owners require idempotent disposal and clear their
managed bytes. The codec clears source/scaled Skia pixel spans, its native encoded
copy, failed decode buffers, and pooled scratch before releasing them. Task 5 must
preserve that ownership through queue, transport, and renderer handoff.

Golden compatibility covers a fixed decoder JPEG, the 1.6 attachment envelopes,
and the existing fixed media-frame codec vector. Encoder output is deliberately
not hash-frozen across supported OS native Skia builds; tests instead assert JPEG
identity, dimensions, alpha behavior, byte limits, and successful bounded decode.

Audio remains unavailable until its capture, codec, consent, and renderer have
their own acceptance slice. Cursor may be embedded in the captured frame for the
first production slice; a separate cursor frame requires a later measured need.

## 8. Platform adapters

### Windows

The Windows project uses Windows Graphics Capture for the exact selected window,
`SendInput` for the closed input vocabulary, and native desktop/session checks to
classify lock or secure desktop. Capture-session closed events, access loss, and
protected-content uncertainty publish unsafe state. COM and frame-pool callbacks
are generation-bound and disposed on a dedicated owner.

### macOS

The macOS project uses ScreenCaptureKit for exact-window capture,
`CGPreflightScreenCaptureAccess`/`CGRequestScreenCaptureAccess` for capture TCC,
Accessibility trust plus CoreGraphics events for input, and
`IsSecureEventInputEnabled` as one fail-closed protection signal. ScreenCaptureKit
window sharing/protection facts and source loss supplement secure input.

Core C APIs use source-generated C# interop. ScreenCaptureKit is Objective-C and
block based; implementation must first prove that direct managed interop can own
callbacks and lifetimes without private ABI assumptions. Otherwise a minimal,
versioned, C-callable Swift shim is allowed only at that ABI boundary and requires
an ADR, deterministic build input, packaging, signing, and leak/crash tests. All
state and policy remain in C#.

### Linux

Wayland uses the ScreenCast and RemoteDesktop portals for user-mediated source
selection and input plus PipeWire for frames. Portal handles and PipeWire nodes
are session-scoped and never persisted. Portal closure or revocation stops the
runtime. X11 is a separately selected adapter with an always-visible security
degradation; no silent Wayland-to-X11 fallback is permitted.

## 9. Protection and Emergency Stop ordering

Native observers publish into a single generation-aware protection reducer. Any
exception, callback loss, stale timestamp, lock transition, secure input, source
loss, or protected-content uncertainty becomes unsafe before controller resume.
Only a newer Safe observation may attempt both native gate resumes.

The local Emergency Stop registration is established before capture admission.
Its callback performs only bounded synchronous gate closure and signals deferred
cleanup. Frame admission closes without waiting for encoder, transport, or
renderer work; capture, input, and participant gates then close immediately.
Ordinary stop and disposal close those producers before draining an in-flight
frame delivery. Emergency Reset never waits on a blocked destination: it returns
`native_frame_delivery_drain_pending` and remains stopped until a later retry can
confirm the old delivery drained. A stale native lease is terminal for that
controller and cannot be reset to Idle. The Emergency callback does not allocate
frames, await, invoke the UI dispatcher, or contact the peer. Registration
conflict or loss blocks or stops sharing. Disposal drains the callback owner
before releasing native handles.

## 10. Bounded cleanup confirmation

Each Desktop host termination has a synchronous authority-closing prefix and one
complete real task. The initiating path closes frame admission and moves the
`RuntimeGeneration` out of active authority if present and into retiring
ownership before any await, watchdog arm, or task publication. The task then
includes controller Stop, callback retirement, fact-reservation release,
Emergency Stop release, connection fail-close, media and control teardown,
protection disposal, authenticated connection disposal, and final Admission
release. After explicit Stop claims the generation, it may supply its exact
caller token only to the first controller Stop attempt. The one real cleanup
task, confirmation operation, and watchdog are published before that attempt or
any other potentially blocking owner call. A throw or exact caller cancellation
from the first attempt becomes a terminal primary outcome and cannot cancel the
remainder of the owned task. A returned `FullyStopped == false` result is
likewise an unconfirmed primary outcome. Each of those three paths invokes
exactly one owned fallback Stop with `CancellationToken.None`; a
`FullyStopped == true` first result does not repeat Stop. All fallback Stop and
owner release remain inside that same task. This replaces the earlier shape in
which explicit controller Stop could wait outside the shared cleanup task. A
thrown or cancelled initial outcome remains the terminal primary even when
fallback cleanup succeeds before the deadline; public Stop and later Dispose
expose the same instance, and later Start remains fail-closed with
`host_cleanup_unconfirmed`.

The coordinator separates three states. `active` grants the only live host
authority and is cleared before cleanup confirmation waits. `retiring` retains
the exact generation, its not-yet-released owners, and its real cleanup task
until that task actually settles. A monotonic cleanup-unconfirmed latch denies
all future Start authority. Real cleanup may release owners as their ordered
steps finish; `retiring` means that no unsettled owner is abandoned, not that an
already disposed owner must be retained. Late completion may clear the retiring
reference but never the latch.

The generation also owns one cleanup-confirmation operation. It uses a
monotonic `TimeProvider`, a ten-second default, a thirty-second hard maximum,
at most one timer-arm attempt, one stable timeout failure instance, and one
atomic completion-versus-expiry decision. A successful arm owns exactly one
timer. The deadline begins when real cleanup begins and is never restarted or
extended by a later Stop, revocation, callback, or Dispose waiter. Cleanup
completion wins only by committing the atomic decision before the timer callback.
Otherwise timeout wins, including exact deadline equality while cleanup remains
uncommitted. The timer never receives authority to cancel the real cleanup task.

When timeout wins, the coordinator sets the sticky latch and records the
allowlisted `host_cleanup_timeout` outcome before completing confirmation. A
terminal callback that owns the coordinator lifecycle semaphore then releases
that semaphore without waiting for physical cleanup. A later Start can therefore
acquire the semaphore, observe the latch, and return
`host_cleanup_unconfirmed` before route, Prepare, capture, Admission, media, or
Driver authority. V1 has no reset transition; even late successful cleanup
requires disposal and construction of a new coordinator before another Start.

A non-fatal failure to create or arm the watchdog publishes
`watchdog_unavailable`, sets the same latch, and leaves the real cleanup task
owned and observed. Timer release is not allowed to delay a confirmation already
published; a release failure is observed on the late cleanup diagnostic path.
An `OutOfMemoryException` from the provider follows the fatal rule below rather
than becoming a bounded reason.

The public Dispose completion and the real cleanup completion are intentionally
different tasks. Once a shared Dispose task has completed with
`host_cleanup_timeout`, it cannot be changed to deliver a later cleanup fault;
concurrent and later Dispose calls continue to share that stable completion.
The coordinator therefore observes real cleanup through a separate internal
completion and terminal diagnostic ledger. A late success drains owners without
changing the timeout. A late non-fatal fault is appended exactly once after the
timeout and remains available to structured diagnostics and tests even though it
cannot retroactively mutate an already returned Dispose task.

When external Dispose is the first terminal path, it first sets the disposed
gate. Once its worker obtains lifecycle ownership of a published active
generation, it synchronously closes frame admission and atomically moves that
generation to `retiring` while publishing the one real cleanup task,
confirmation operation, and watchdog. No potentially blocking controller or
owner boundary runs before that prefix completes. Concurrent and later external
Dispose calls share the same underlying public task and, after timeout, the same
stable `host_cleanup_timeout` instance. A Dispose call from generation callback
ancestry returns a non-waiting completed `ValueTask`, but still initiates or
joins that same operation for external observers. Start after explicit Dispose
retains `ObjectDisposedException` precedence; a non-disposed coordinator whose
terminal cleanup timed out exposes `host_cleanup_unconfirmed` instead. Both
paths reject before granting authority.

Failure projection uses semantic slots rather than thread-arrival order:
terminal primary failures first, cleanup-confirmation timeout or watchdog setup
failure second, watchdog-release failures third, and real owner-cleanup failures
in the fixed cleanup-step order last. Aggregates are flat and retain original
non-fatal exception instances. A direct or nested `OutOfMemoryException`
dominates that projection by its first original instance. Cleanup continues
through independently safe later owner steps, but OOM is never wrapped as a
rejection, timeout, or aggregate. The separate real completion is always
observed, including when its fatal result arrives after the public timeout.

The final v1 contract applies the same model to resources acquired before a
`RuntimeGeneration` exists. Pre-generation validation cleanup will own one real
task and one non-extensible bounded confirmation operation rather than holding
the lifecycle semaphore indefinitely. Until that path is implemented and
tested, it remains an explicit Task 5.5a cleanup sub-boundary gap.

The first production-composed vertical is deliberately narrower than the final
contract: one active host receives an authenticated terminal disconnect while
one real cleanup owner is deterministically blocked. A manual `TimeProvider`
advances the production watchdog to equality, proves admission and authority are
already closed, proves the global lifecycle gate can reject a replacement Start,
then releases the owner and proves complete two-node drain while restart remains
latched. This single order may advance only CL Timeout from Missing to Partial.
It does not cover explicit Stop or Dispose initiation, pre-generation cleanup,
cleanup-wins races, watchdog setup/disposal failure, late cleanup fault, OOM,
other active or pending owners, or their combinations.

The first Task 5.5a.3 extension isolates external Dispose-first initiation on a
stable active generation with an uncontended lifecycle gate. A manual cleanup
clock and one blocked host Connection owner prove synchronous
`active -> retiring` publication, one timer and real task, T-1 pending state,
exact-equality `host_cleanup_timeout`, one shared public Dispose task and
exception instance for concurrent and later external callers, then late true
owner drain without public-result mutation. A terminal callback arriving after
the Dispose claim may run its existing synchronous safety prefix, but its cleanup
ownership may only attach to that same operation. Stop-first caller-token and
fallback semantics, arbitrary race precedence, late faults/OOM, and timer or pre-
generation failures remain later Task 5.5a.3 slices.

The next Task 5.5a.3b slice isolates Stop-first initiation on one stable active
generation with an uncontended lifecycle gate. Before controller Stop can
block, the coordinator closes Admission and publishes `active -> retiring`, the
one real cleanup task, one confirmation operation, and one watchdog. Two
separate deterministic scenarios keep the slice auditable. The
caller-cancel-before-deadline scenario cancels only after publication and proves
the first Stop receives the exact caller token, preserves its cancellation as
the primary outcome, then performs exactly one successful fallback with
`CancellationToken.None` while confirmation and remaining cleanup stay live.
Public Stop and later Dispose expose that same cancellation instance, and
restart on the coordinator remains fail-closed after true cleanup drains.

The blocked-Stop scenario proves T-1 remains pending, exact equality publishes
`host_cleanup_timeout`, and a late `FullyStopped == true` release drains the
same real task without fallback or public-result mutation. Concurrent
Stop/Dispose/callback precedence, ordinary throw and `FullyStopped == false`
combinations, lifecycle-gate contention, timer faults, late cleanup fault/OOM,
pre-generation cleanup, and other blocked owners remain open after this slice.

## 11. Testing and evidence

Each platform adapter has three evidence levels:

1. portable contracts with injected native calls on every CI OS;
2. matching-host native API smoke that verifies prompt-free facts and safe
   create/stop/dispose without manufacturing permissions;
3. packaged real-machine scenarios with explicit grant/deny/revoke and observable
   capture/input/protection/Emergency Stop results.

Fault tests cover every native call and callback boundary, cancellation,
late-generation callbacks, resource ceilings, and partial cleanup. Media tests
include hostile dimensions, compressed bombs, malformed frames, backpressure,
and UI disposal. Physical tests record machines, OS/compositor versions,
permissions, devices, network, package digest, exact commit, and limitations.

The protocol-1.7 prerequisite additionally freezes canonical Prepare and both
Ready outcomes plus digest vectors, preserves every 1.5/1.6 fixture, and tests
lower-version rejection, strict schemas, direction/identity/binding/role/deadline
tamper, one-way grant direction, duplicate/concurrent/late outcomes, terminal
tombstones, read-loop progress, and full owner cleanup. A managed two-node tracer
must prove no capture before Ready/attachment, no frame before final Admission
state, and empty controller/renderer/queue/attachment/route/directory/control
ownership after teardown. It remains portable evidence.

Hosted runners may prove only the native calls they actually execute. A runner
without an interactive desktop or permission grant remains contract evidence.
