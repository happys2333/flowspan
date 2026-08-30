# Native Remote Window Requirements

Status: approved product baseline; protocol-1.7 Preparation codec/managed-session
candidate implemented; production Desktop/native composition in progress

Parent requirements: R1, R3.3-R3.5, R6, R8.4, R9, R10, R12

Parent tasks: 5.4, 6.1-6.6, 7.2, 7.3, 7.4b, 9.3-9.4

## 1. Problem and scope

The portable Remote Window controller, authenticated protocol, bounded encrypted
media channel, and headless Desktop workflow are complete. Production composition
still reports `native_adapters_unavailable`: no supported platform can resolve an
exact local window, capture it, render remote frames, inject authorized input,
observe native protection state, or register an independent local Emergency Stop.

This specification covers the production path for Windows, macOS, and Linux. It
does not claim that arbitrary process memory moves between devices. Source
execution remains on the host and the product must continue to label the result
as Remote Window.

## 2. User stories

- As a user whose application has no semantic Adapter, I can select one exact
  local window and continue it on a paired Device through an explicitly labelled
  Remote Window.
- As a host user, I can see what is shared, who may view or drive it, and stop
  capture and input locally even when the peer, network, or main UI is stuck.
- As a participant, I can view bounded live output and can inject input only while
  I hold the current Driver Lease and every native safety gate remains safe.
- As a release reviewer, I can distinguish platform contract tests from packaged
  real-machine evidence for permissions, capture, input, protection, accessibility,
  networking, and failure recovery.

## 3. Acceptance requirements

### NR1 - Exact local source and truthful Activity binding

1. When Flowspan enumerates a generic native window, it shall create an ephemeral
   Remote Window source record bound to one Activity ID used only for the live
   sharing identity, one local source token and generation, the native window
   identity, and the owning application instance.
2. The source token and native handle shall remain host-local, memory-only, and
   absent from descriptors, protocol frames, diagnostics, persistence, exports,
   and peer-visible state.
3. A generic native-window source shall have no Activity Descriptor or Activity
   Kind, shall report semantic resume as unsupported, and shall never enter the
   semantic Activity catalog or be offered for Handoff, Move, Replace, Swap,
   Group, or Scene operations in v1.
4. When a selected native window closes, is replaced, becomes ambiguous, changes
   owning application instance, or no longer matches its generation, Flowspan
   shall revoke the selection before capture starts or continues.
5. When a title or owning application is sensitive, unavailable, or outside the
   local disclosure policy, Flowspan shall show bounded generic local text and
   shall not disclose the original title to a peer.

### NR2 - Progressive permission and readiness

1. While no native operation is requested, Flowspan shall read permission state
   without displaying an operating-system prompt.
2. When the user explicitly reviews or requests capture or input permission,
   Flowspan shall request only that permission and shall expose Granted,
   NotDetermined, Denied, Revoked, Unsupported, or temporarily Unavailable state
   with a recovery action.
3. Production Remote Window shall report available only when the exact source,
   platform capture, protected-surface observation, local Emergency Stop, and
   required participant rendering path are ready. Driving shall additionally
   require native input permission and input support.
4. Permission success shall grant no peer Capability or Driver authority. Every
   peer action shall still pass current Trust, Capability, session, role, lease,
   protection, and generation checks at use time.
5. When the host user starts one exact source for one participant and role,
   Flowspan shall require negotiated protocol 1.7 or later and shall complete one
   bounded host-to-participant Prepare/Ready transaction before crossing native
   capture. Protocol 1.5 Admission shall not be reused as Ready.
6. Before Prepare is sent, the host shall revalidate the exact source lease and
   generation, current authenticated connection, Trust and role Capabilities,
   prompt-free capture and requested-role readiness, fresh Safe protection,
   independent Emergency Stop readiness, and the responder media route. Prepare
   shall bind correlation, Session, Activity, directed Devices, frozen role,
   canonical whole-millisecond UTC deadline, and a domain-separated canonical SHA-256
   `prepareDigest`, and shall expose no native or route identity.
   Ready received before Prepare send begins shall fault closed. During Prepare
   send, one exact Ready success may be buffered but shall not complete until send
   success wins the Stop/deadline commit. An exact rejection is a safe terminal
   acknowledgement and shall close the connection even if it cancels send flush.
7. When the participant receives a well-formed Prepare, it shall verify the
   authenticated current connection and Trust, local recipient, exact binding
   and digest, non-revoked/non-stopping state, local receive policy, deadline,
   renderer readiness, and initiator media attachment before returning one
   terminal Ready. It shall not require a reciprocal `mirror.view` or
   `mirror.drive` grant, because those grants authorize the opposite source
   direction and v1 has no `remote-window.receive` Capability.
8. While participant media/renderer preparation is pending, the authenticated
   control read loop shall remain able to dispatch traffic. Preparation shall run
   in one owned, deadline- and lifetime-cancelled worker that Stop and disposal
   join; it shall not synchronously wait inside the single read loop.
9. When participant preparation succeeds, Ready shall repeat every exact binding
   and digest and report success. When the user rejects or a local preparation
   boundary fails, Ready shall report one allowlisted bounded rejection and
   release prepared owners without starting host capture. Malformed or wrongly
   bound traffic shall fault closed without reflecting attacker-controlled text.
   A final Admission before Ready send begins shall fault closed without invoking
   the participant endpoint. During Ready send, at most one exact final Admission
   may be buffered without authority; send success may consume it, while send
   failure shall discard it and close the connection.
10. When the host receives Ready success, it shall match the one live pending
    transaction and current media binding, revalidate every host fact, register
    protection and independent Emergency Stop ownership, call controller Start
    with frame admission closed, and then add the exact participant with the
    frozen role. The participant shall establish its known binding and render
    only after the final correlated state reports Admission, Applied or
    AlreadyApplied, and the exact effective role.
11. Prepare, Ready, authenticated connection, permission, route possession,
    attachment, and renderer readiness shall grant no Capability, participant
    membership, Driver Lease, input, capture, or rendering authority by
    themselves. No captured frame shall be disclosed before final admission.
12. If either side rejects, expires, cancels, disconnects, revokes, faults, or
    fails any revalidation, capture, admission, final state, media, renderer, or
    cleanup boundary, Flowspan shall close frame admission and unwind every
    owner. Once media route-role selection occurred, it shall consume that media
    session and close the owning authenticated control connection; retry shall
    require a fresh handshake, route, Session ID, and correlation. One control
    registration shall admit at most one Preparation and retain its bounded
    terminal tombstone through the deadline or connection close.

### NR3 - Capture and media

1. When Start crosses native capture, the adapter shall capture only the exact
   admitted window and shall exclude Flowspan overlays, permission UI, and all
   unrelated display content. Whole-display capture is outside this v1 slice.
2. Captured frames shall enter the existing purpose-separated encrypted media
   budget through a bounded encoder queue. The adapter shall drop or coalesce
   frames before exceeding a queue or memory limit and shall never block
   Emergency Stop on encoding, transport, or rendering.
3. A frame that cannot fit the protocol's chunk and byte limits after bounded
   adaptation shall be dropped with a payload-free reason. It shall not be split
   outside the frozen media-frame contract.
4. The participant shall render only authenticated frames bound to the current
   Session ID, Activity ID, host Device, and strictly advancing media sequence.
5. When capture, encoding, transport, decoding, or rendering fails, Flowspan
   shall preserve source execution, stop or pause native sharing as required,
   and present a named degradation or recovery state.
6. When a Remote Window media stream attaches to a live authenticated control
   connection, Flowspan shall require protocol 1.6 or later and shall bind the
   attachment to the exact local and peer Device IDs, live control route,
   Session ID, Activity ID, and one fresh initiator nonce acknowledged by the
   responder before admitting media frames.
7. While classifying a second inbound stream, Flowspan shall use a distinct,
   bounded media-attachment envelope and shall reject unknown routes, expired or
   already-attached routes, replayed nonces, mismatched identities or sessions,
   unsupported flags, trailing fields, and protocol 1.5 downgrade attempts before
   exposing the stream to a media consumer.
8. When encoding a captured BGRA8888 frame, Flowspan shall discard alpha and try
   only the frozen finite JPEG quality/scale ladder; it shall return a bounded
   drop reason when no candidate fits the 1-MiB logical-video-frame ceiling.
9. Before allocating decoded pixels, Flowspan shall validate that the payload is
   one still JPEG image with positive bounded dimensions, no more than 16,777,216
   pixels, and no more than 67,108,864 decoded BGRA bytes; malformed, truncated,
   animated, multi-frame, or other formats shall fail closed.
10. When sending one encoded logical video frame, Flowspan shall split 1 through
    1,048,576 owned bytes into at most 16 ordered chunks of at most 65,536 bytes,
    with one Session, Activity, chunk count, and strictly continuous sequence
    range. The receiver shall reject a wrong binding, kind, count, index, order,
    sequence, empty chunk, or aggregate size and shall clear any partial frame.
11. While transport is slower than capture, Flowspan shall retain at most one
    not-yet-started logical video frame. A newer frame shall replace and clear the
    older pending frame; the active frame shall send at most one wire chunk at a
    time, shall never interleave with another logical frame, and shall report a
    bounded Sent, Replaced, Dropped, Failed, or Cancelled outcome.
12. When a connection-owned media route expires, is revoked, fails attachment,
    faults during media I/O, or is disposed, Flowspan shall stop the owning
    authenticated control connection and shall not reuse that media session or
    route. Budget exhaustion additionally requires the fresh authenticated
    recovery specified in NR8.
13. When the host-selected production workflow prepares its media path, the host
    shall call `PrepareResponderRoute` before Prepare and the participant shall
    call `ConnectInitiatorAsync` for the same Session and Activity before Ready.
    The route locator shall remain inside the authenticated `FSM1` attachment and
    shall not appear in Prepare, Ready, logs, diagnostics, or presentation.

### NR4 - Native input

1. When an authenticated input batch reaches the host, Flowspan shall re-check
   the exact current Driver Lease, Capability, source generation, permission,
   protection observation age, and local stop latch immediately before native
   injection.
2. Native adapters shall map only the closed v1 keyboard, pointer-button,
   absolute/relative motion, and scroll vocabulary. Unknown codes, invalid
   coordinates, and unsupported combinations shall fail closed without partial
   injection.
3. Coordinate mapping shall bind to the current captured content rectangle and
   scale, clamp safely, and reject a stale geometry generation.
4. Pausing, permission loss, protection uncertainty, source loss, ordinary stop,
   Emergency Stop, or disposal shall synchronously prevent new native input.
5. Clipboard, text substitution, file transfer, privileged shortcuts, secure
   attention sequences, and credential-field automation remain out of scope.

### NR5 - Native protection

1. While a Remote Window may be live, Flowspan shall observe secure input,
   protected content, sensitive-window policy, lock/secure desktop state, and
   source availability using the strongest supported platform signals.
2. When any signal is unsafe, stale, unavailable, contradictory, or throws,
   Flowspan shall publish Unknown or the exact unsafe ProtectionKind before
   pausing capture and input.
3. A Safe observation shall not resume a session unless it is newer than the
   blocking observation, within the portable maximum age, and both native gates
   confirm resume for the current session generation.
4. Platform limitations shall be named. An adapter shall not treat an absent or
   unverifiable protected-content signal as Safe.

### NR6 - Independent local Emergency Stop

1. Before capture becomes active, Flowspan shall register a documented local
   Emergency Stop action that does not depend on the peer, network, renderer, or
   main-window event loop.
2. When the action fires, the local stop latch shall close before callbacks and
   shall synchronously stop or gate capture, input, and participant sessions.
3. Registration failure shall block Start. A conflicting operating-system hotkey
   shall produce a visible recovery state rather than silently choosing another
   chord.
4. Ordinary shutdown and disposal shall unregister the action only after the
   native gates are stopped and all callbacks are drained.

### NR7 - Platform behavior

1. On supported Windows releases, capture shall use Windows Graphics Capture,
   input shall use the documented SendInput boundary, and secure desktop or
   protected-content uncertainty shall fail closed.
2. On supported macOS releases, capture shall use ScreenCaptureKit, input shall
   use Accessibility-authorized CoreGraphics events, TCC deny/revoke shall be
   recoverable, and secure input or protected-window uncertainty shall fail
   closed.
3. On supported Wayland desktops, capture and input shall use the user-mediated
   ScreenCast and RemoteDesktop portals plus PipeWire. Portal revocation or
   session closure shall stop the live session.
4. On X11, Flowspan shall expose a separate, explicit security degradation and
   shall not imply Wayland portal isolation. Unsupported compositors shall remain
   view/input unavailable rather than falling back silently.

### NR8 - Lifecycle and resource bounds

1. Sleep, lock, logout, display reconfiguration, window closure, process exit,
   network loss, peer restart, and permission revocation shall produce bounded,
   generation-safe transitions without retaining Driver authority.
2. Native callbacks shall carry an owner and session generation. A callback from
   a stopped or replaced generation shall release its native resources and shall
   not publish state or frames.
3. Start, pause, resume, stop, Emergency Stop, and dispose shall be idempotent at
   every native boundary and shall return only bounded reason codes.
4. Native resources, frame buffers, event taps, portal sessions, PipeWire
   streams, COM objects, and callback handles shall be released on success,
   failure, cancellation, timeout, replacement, and disposal.
5. When an asynchronous media handshake or frame operation ignores cancellation,
   caller cancellation, the configured deadline, or session disposal shall still
   stop waiting without clearing or reusing a buffer still borrowed by that
   operation. Flowspan shall observe the detached completion and clear the buffer
   only after the underlying operation settles.
6. Media teardown shall attempt cancellation, active and pending frame release,
   queue drain, budget release, sink disposal, attachment disposal, route cleanup,
   and control stop even when an earlier stage fails. One failure shall preserve
   its original identity; concurrent primary and cleanup failures shall remain
   observable together.
7. Before either attached media direction exhausts `2^20` protected frames,
   1 GiB of plaintext, or its sequence/epoch range, Flowspan shall close the media
   attachment and owning authenticated control connection. Recovery shall perform
   a fresh authenticated control handshake, derive a fresh media session, and use
   a new route without raising a budget, rekeying media in place, or republishing
   the consumed route.
8. A requested-role change, duplicate or concurrent Prepare, delayed Ready,
   unknown correlation, digest mismatch, or connection-generation change shall
   not mutate or revive pending work. After terminal Preparation cleanup, retry
   shall use a fresh authenticated connection rather than retaining stale media
   or participant state.
9. When a Desktop host runtime generation first enters terminal cleanup,
   Flowspan shall synchronously close frame admission, remove that generation
   from active authority if present, and register it as retiring ownership before
   any await, watchdog arm, or task publication. It shall then create one
   generation-owned real termination task that includes controller Stop and all
   later owner release, and let concurrent Stop, revocation, fail-close,
   terminal-callback, and disposal paths join that same task. Caller cancellation
   or cleanup-confirmation timeout shall not cancel, replace, or abandon the real
   task or release an owner that the task has not settled. If an initial explicit
   controller Stop throws, is cancelled, or returns `FullyStopped == false`, the
   real task shall retain that primary outcome and run at most one owned
   no-caller-cancellation fallback Stop; it shall not run fallback after
   `FullyStopped == true`. When external Dispose is the first terminal path, it
   shall set the coordinator's disposed gate before returning. Once it obtains
   lifecycle ownership of a published active generation, it shall synchronously
   close admission, move that generation to retiring ownership, and publish the
   one cleanup operation and watchdog before invoking any potentially blocking
   controller or owner boundary. Dispose called from a generation callback shall
   initiate or join that same operation without waiting from its own callback
   ancestry.
10. When the real termination task starts, Flowspan shall create exactly one
    cleanup-confirmation operation for that generation and attempt to arm at most
    one monotonic `TimeProvider` watchdog. The operation shall own one stable
    timeout outcome shared by every waiter whether or not timer setup succeeds;
    a successful arm shall own exactly one timer. The default duration shall be
    ten seconds, the fixed hard maximum shall be thirty seconds, and a later
    waiter shall not extend or restart the deadline.
11. If the real termination task has not committed completion when its watchdog
    fires, Flowspan shall close the public or coordinator wait with the bounded
    reason `host_cleanup_timeout`, verify that the same generation remains absent
    from active authority and retained in retiring ownership, and set a monotonic
    cleanup-unconfirmed latch before releasing the host lifecycle gate. The
    watchdog shall end only confirmation waiting; the retiring generation shall
    continue to own and observe real cleanup.
12. While the cleanup-unconfirmed latch is set on a coordinator that has not
    entered explicit disposal, when Start is attempted, Flowspan shall reject it
    with `host_cleanup_unconfirmed` before granting a
    route, sending Prepare, starting capture, admitting a participant,
    publishing media, or creating Driver authority. A late successful cleanup
    shall release its owners but shall not clear the latch; v1 shall provide no
    automatic reset on that coordinator. After explicit Dispose has set the
    disposed gate, Start shall instead preserve normal object-lifetime semantics
    by throwing `ObjectDisposedException` before granting any authority.
13. When real cleanup completes after confirmation timeout, Flowspan shall
    observe that completion independently of the already completed public wait.
    Late success shall preserve the timeout result, while a late non-fatal fault
    shall remain observable exactly once with its original identity and a
    deterministic order after the timeout. A completed shared Dispose result
    shall not be mutated to report a later fault; concurrent and later external
    Dispose calls shall share that completed result and late cleanup shall use a
    separate diagnostic completion surface. A callback-origin Dispose call may
    return a non-waiting completed `ValueTask` to avoid self-drain, but shall
    initiate or join the same public Dispose operation and real cleanup task that
    later external callers observe.
14. When terminal primary, watchdog, and cleanup failures coexist, Flowspan
    shall project them in the fixed order primary, cleanup-confirmation outcome,
    watchdog-release failure, then owner-cleanup failures in cleanup-step order.
    If a direct or nested failure contains an `OutOfMemoryException`, the first
    such exception shall dominate the failure projection by its original
    instance and shall not be converted into a bounded rejection, timeout, or
    aggregate; all independently safe later cleanup steps shall still be
    attempted.
15. If the cleanup watchdog cannot be created or armed because of a non-fatal
    provider failure, Flowspan shall fail confirmation closed with the bounded
    reason `watchdog_unavailable`, retain and observe real cleanup, and block
    restart. A watchdog disposal fault shall not delay an already published
    confirmation and shall remain observable as a cleanup fault. Provider OOM
    shall follow the fatal rule in NR8.14.
16. When cleanup completion and watchdog expiry race, Flowspan shall use one
    atomic winner: completion wins only if it commits first; otherwise expiry
    wins. The losing path shall not change an already published public result,
    start a second cleanup, or release an owner twice.
17. Before Native Remote Window Task 5.5a can close, resources acquired before a
    `RuntimeGeneration` exists shall use an equivalent bounded confirmation
    operation: one real pre-generation cleanup task, one confirmation operation,
    at most one non-extensible watchdog arm attempt, exactly one timer after a
    successful arm, retained ownership through late settlement, the same fatal
    and diagnostic rules, and the same restart denial after unconfirmed cleanup.

### NR9 - Accessibility and visible sharing

1. While capture is active or paused, every supported platform shall show a
   persistent, accessible local sharing indicator with source, participant role,
   protection state, and Emergency Stop.
2. Participant rendering shall expose a programmatic name, textual connection
   and Driver state, visible focus, keyboard navigation, scaling, high-contrast,
   and reduced-motion behavior without making the video frame the only state
   carrier.
3. Platform privacy indicators remain additive. Flowspan shall not hide or
   suppress an operating-system capture or input indicator.

### NR10 - Evidence boundary

1. Portable and platform-contract tests shall cover every state, reason, bound,
   cancellation path, callback generation, and injected native failure on all CI
   operating systems.
2. A native test may be labelled native only when it calls the matching platform
   API and verifies an observable result on that operating system.
3. Parent tasks 6.3-6.5, 7.4b, 9.3, and 9.4 and their applicable release
   criteria shall remain open until packaged real-machine evidence covers grant,
   deny, revoke, capture, rendering, input, protection, Emergency Stop under
   UI/network failure, sleep/wake, source loss, and cleanup on Windows, macOS,
   Wayland, and the documented X11 degradation.
4. Physical two-device latency, loss, reconnect, and sustained-load results shall
   be recorded separately from same-host and hosted-runner evidence.
5. Protocol-1.7 evidence shall include canonical Prepare, Ready-success, and
   Ready-rejection fixtures; stable protocol-1.5/1.6 fixtures; digest vectors;
   hostile schema, direction, identity, binding, role, deadline, replay, and
   downgrade cases; one-way-grant direction; concurrent and delayed terminal
   outcomes; non-deadlocking dispatch; and full cancellation/fault cleanup.
6. A production-composed managed two-node tracer shall prove zero native capture
   before Ready and attachment acknowledgement, zero media publication before
   exact final Admission state, capture-to-JPEG-to-encrypted-media-to-decode-to-
   renderer flow after admission, exact Driver input return, Emergency Stop, and
   empty controller/renderer/queue/attachment/route/directory/control ownership
   after teardown. This tracer shall not be labelled native or physical evidence.

## 4. Non-goals

- Migrating arbitrary process memory, credentials, unsaved internal state, or OS
  security context.
- Mobile, iPadOS, Android, or browser clients in v1.
- Clipboard or file-content transfer.
- Bypassing DRM, secure desktop, secure input, protected content, TCC, portal
  consent, Accessibility consent, or other operating-system policy.
- Claiming production readiness from mocks, headless Avalonia, loopback, or
  unsigned hosted packages.
