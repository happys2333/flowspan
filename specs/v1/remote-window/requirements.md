# Remote Window and Mirror Control Requirements

Status: approved v1 baseline; portable control plane and authenticated bounded
media complete; Desktop workflow in progress

## Problem and scope

Flowspan needs an honest fallback when an Activity cannot resume semantically.
Remote Window keeps execution on the source Device, presents captured output on
another Device, and optionally accepts narrowly authorized remote input. Mirror
uses the same live-sharing safety model without claiming that application state
moved.

This specification covers the v1 portable control plane, authenticated bounded
media, authorization, Driver Lease, protection, Emergency Stop, bounded
resource, Desktop candidate, and evidence contracts. It also defines the still
open boundary for native Windows, macOS, and Linux adapters.

## User outcomes

- A user can tell that an Activity is remotely presented and which Device is
  currently the Driver.
- A trusted peer can view only when granted `mirror.view`, and can drive only
  when also granted `mirror.drive`.
- Losing a lease, connection, Capability, or trustworthy protection state stops
  remote control rather than guessing.
- A local emergency stop removes authority and closes local sharing without
  depending on the peer, network, or acknowledgement.
- A semantic-resume failure is never misrepresented as native migration.

## Acceptance requirements

### RW1 - Honest fallback semantics

- RW1.1: When semantic resume is unavailable and Remote Window is supported,
  Flowspan shall offer it as a separately named fallback and shall state that
  execution remains on the source Device.
- RW1.2: Remote Window shall not become an Activity descriptor kind and shall
  not claim to transfer process memory, unsaved application state, credentials,
  or unsupported application internals.
- RW1.3: When capture, protection detection, or an authenticated sharing path is
  unavailable, Flowspan shall leave the source Activity unchanged and report a
  named unavailability or degradation.
- RW1.4: When the Desktop offers a fallback for an Activity that cannot resume
  semantically, it shall name the action `Remote Window`, state that execution
  remains on the source Device, and keep semantic Handoff/Move distinct.
- RW1.5: The Desktop fallback shall bind one exact active local Activity and one
  selected target from a purpose-specific Remote Window inventory into a visible
  preview. A `ViewOnly` candidate shall have a current authenticated connection
  and `mirror.view`; a `DriverEligible` candidate shall have the same connection
  plus one current grant containing both `mirror.view` and `mirror.drive`.
  `activity.receive` alone shall never qualify a Remote Window target. Changing
  the preview role, connection, or Trust grant shall refilter the inventory and
  clear a selection that no longer qualifies. Starting shall cross a dedicated
  service boundary only after capture permission is granted, pass the exact role
  shown in that preview, and re-read current authority at use time. Concurrent
  Desktop start attempts shall be serialized and revalidated after acquiring the
  start gate; at most one admitted request may cross the service boundary for
  that inactive session.

### RW2 - Session admission and visible state

- RW2.1: When a Remote Window or Mirror session starts, the source shall verify
  that the Activity is active on the local host before capture is disclosed.
- RW2.2: While a session is active or protection-paused, Flowspan shall publish
  a bounded, accessible sharing snapshot containing the Activity identity,
  lifecycle, capture state, participant count, and current Driver.
- RW2.3: A session controller shall own exactly one Activity. Independent
  controllers may coexist subject to a later composition-level resource limit;
  no controller may silently replace another Activity's session.
- RW2.4: If capture admission fails, the session shall not become active or
  advertise that capture is running. A fresh Safe protection observation shall
  not resume capture/input or publish Active/Capturing until capture admission
  has confirmed for the same session generation.
- RW2.5: While the Desktop is open, it shall keep one persistent top-level
  sharing indicator synchronized with the latest bounded session snapshot. The
  indicator shall distinguish Starting, Active, ProtectionPaused,
  EmergencyStopped, Unavailable, and inactive states without relying on color.
- RW2.6: The Desktop sharing detail shall expose bounded Activity, capture,
  participant, Driver, lease, and protection state without frames, raw input,
  descriptor payloads, peer exception text, or native handles.
- RW2.7: The Desktop shall reduce the command-result snapshot returned by Start,
  Emergency Stop, and local reset before performing any follow-up service
  refresh. If that later read is unavailable, the accepted started, stopped, or
  reset state shall remain last known rather than reverting to pre-command
  uncertainty; in particular, a stoppable state shall retain Emergency Stop.

### RW3 - Capability authorization

- RW3.1: When a peer joins as view-only, the host shall re-read that peer's
  current `mirror.view` grant before adding it.
- RW3.2: When a peer joins as driver-eligible, receives Driver authority, or
  submits input, the host shall re-read both `mirror.view` and `mirror.drive` at
  that use boundary.
- RW3.3: View-only participants shall never receive Driver authority and shall
  never reach the input boundary.
- RW3.4: When `mirror.drive` is removed but `mirror.view` remains, the host shall
  invalidate any peer-held lease, return authority to the safe owner, and keep
  the peer view-only.
- RW3.5: When `mirror.view` is removed, the host shall remove the participant,
  invalidate any peer-held lease, and locally disconnect that peer's sharing
  session before reporting reconciliation complete. If that local disconnect
  fails, the host shall retain a peer-scoped pending-disconnect fact without
  restoring participant or Driver authority. A later Capability reconciliation
  or explicit participant disconnect for that peer shall retry the local
  boundary until it confirms, then clear the pending fact; only a request after
  confirmation may report the peer already absent. While the pending fact
  exists, when that peer regains Capability and requests admission, the host
  shall fail closed with `BoundaryFailed` / `peer_disconnect_pending` and shall
  not restore participant or Driver authority. Only confirmed cleanup may make
  that peer eligible for fresh admission.
- RW3.6: A denied operation shall not disclose capture or input payloads and
  shall produce only a stable reason code and bounded identifiers.
- RW3.7: While the explicitly enabled local-network lifetime is active, when a
  current Trust Record grants at least one Activity control or Mirror control
  Capability, the elected connector and shared listener shall admit that peer
  through an any-of authenticated idle control-channel profile. Admission shall
  grant no Remote Window operation by itself: `mirror.drive` without
  `mirror.view` may keep the idle channel available but shall not qualify a
  target, participant, Driver, or input path. A successful Trust mutation shall
  refresh the purpose-scoped inventory; removing the final eligible control
  Capability shall drain the channel, while retaining another eligible
  Capability shall not interrupt it.

### RW4 - Driver lease and input

- RW4.1: At most one unexpired Driver Lease epoch shall authorize input for a
  session.
- RW4.2: When Driver authority transfers, the host shall publish the higher
  epoch before any input from the new Driver can reach the native boundary.
- RW4.3: Input from every previous epoch, a non-participant, a view-only peer,
  or a peer without current Capabilities shall be rejected.
- RW4.4: When a remote Driver disconnects or its lease expires, the host shall
  reject that Driver's input and return authority to the host safe owner with a
  higher epoch.
- RW4.5: Input and Driver transitions shall be serialized so a normal transfer
  cannot overtake an already admitted input operation. Emergency stop and
  protection blocking shall preempt that normal-operation serialization.
- RW4.6: Remote input events shall be portable, strictly shaped, bounded, and
  defensively copied before reaching an adapter. Raw events shall never enter
  receipts, normal logs, or diagnostic exports.

### RW5 - Protection state

- RW5.1: Before remote input reaches an adapter, the host shall evaluate a
  current protection observation at the use boundary.
- RW5.2: Unknown, stale, future-skewed, secure-input, protected-content, and
  configured-sensitive-window observations shall fail closed.
- RW5.3: When protection becomes unsafe or uncertain, the host shall first
  publish a fail-closed authority state that rejects new input, then
  synchronously pause or blank the local capture and input gates. It shall
  publish `Paused` as a confirmed capture state only after both gates confirm;
  otherwise it shall remain blocked and explicitly unconfirmed.
- RW5.4: Capture and input may resume only after a fresh safe observation and a
  still-active, non-emergency-stopped session. A failed resume shall re-pause
  both local gates, and a failed or superseded resume shall never publish
  Active; protection changes that do not converge within a fixed local bound
  shall remain visibly blocked and unconfirmed.
- RW5.5: Native capture adapters shall independently enforce the same protection
  gate continuously; a portable observation test is not native proof.
- RW5.6: The Desktop shall review capture permission before requesting it and
  shall request input/accessibility permission only after the user explicitly
  enables remote driving. Denial, revocation, and unsupported platform state
  shall keep sharing or driving disabled with a named recovery action. An
  undefined permission value or failed permission read shall reduce to
  `Unavailable`.
- RW5.7: Permission request return values shall not overwrite newer adapter
  state. Explicit capture denial, revocation, unsupported, unavailable, or
  ungranted state shall win a concurrent start and synchronously stop any
  stoppable session. Losing input permission while a DriverEligible start or
  session may exist shall revoke that path before more remote input is accepted.
  A permission `Changed` callback shall cross the required synchronous local
  Emergency Stop boundary before queuing its UI presentation refresh. Start
  admission and the callback shall use one synchronized admission-permission
  fact: a revocation admitted first shall prevent the service call, while a start
  admitted first shall be visible to the callback and synchronously stopped.

### RW6 - Local emergency stop

- RW6.1: When emergency stop is activated locally, the controller shall first
  latch the stop, increment/revoke the Driver Lease epoch, and publish an
  emergency-stopped state synchronously.
- RW6.2: In the same local action, the controller shall synchronously request
  capture halt, input halt, and local sharing-session disconnect without sending
  or awaiting a peer acknowledgement.
- RW6.3: Emergency stop shall preempt a starting, active, protection-paused, or
  normally stopping session and shall be idempotent. Capture, input, and session
  confirmation shall bind both the stop generation and session generation. A
  pending capture admission completion shall invalidate older capture-stop
  proof; only a successful late cleanup for that current generation may confirm
  it again or authorize reset. Repeated or concurrent attempts in one stop
  generation shall invoke all three local boundaries and accumulate independent
  confirmation without letting a later failed invocation regress earlier proof.
  Each completed result shall project the accumulated proof that exists for that
  same stop/session generation; a later session shall not reuse it. While any
  attempt for the current generation remains in progress, reset shall fail
  closed and require a later retry.
- RW6.4: Boundary failures shall be returned as separate bounded reason codes.
  Flowspan shall not report capture, input, or sessions stopped when that local
  boundary did not confirm the action.
- RW6.5: Reset shall require explicit local confirmation. Reset shall not restore
  participants, capture, or a prior remote Driver automatically.
- RW6.6: Whenever a session is Starting, Active, or ProtectionPaused, the
  Desktop shall expose one keyboard-operable Emergency Stop in the persistent
  sharing header. Activating it shall call only the local stop boundary, disable
  repeated activation, and show each unconfirmed capture/input/session boundary
  without waiting for a peer or network acknowledgement.
- RW6.7: A transient unavailable state shall preserve the last accepted
  Activity/revision watermark and keep Emergency Stop available for a last-known
  stoppable session. A stale snapshot shall not roll that state back. A lower
  revision for the same Activity may represent a new session only after an
  explicit authoritative inactive/Idle observation; a different Activity is a
  new context. Desktop command and queued Emergency Stop outcomes shall bind an
  explicit controller/safety generation. A successful Start returned after an
  authoritative inactive/Idle boundary shall be synchronously stopped when no
  replacement session has begun, because that return may prove capture crossed
  after the inactive observation. Once a replacement session begins, the older
  Start result or cleanup proof shall have no state, latch, metadata,
  presentation, or stop effect on that replacement or on its later inactive
  boundary. A service snapshot shall pass Activity/revision reduction before it
  may elevate the safety role, reset Emergency Stop state, or trigger a
  permission-loss stop; a rejected stale DriverEligible snapshot shall have no
  effect on an accepted current view-only session.
- RW6.8: The Desktop may offer an explicit local retry reset from a controller
  `Unavailable` state produced by a failed start only when its accepted snapshot
  follows a successful local capture-stop boundary and confirms capture
  `Stopped`, no remote participant, and no current Driver. The failed Start path
  shall attempt that local capture stop before returning; a failed or throwing
  cleanup shall publish `Unconfirmed`. Reset may move that controller to Idle but
  shall restore no authority. Transient service unavailability, a snapshot claim
  without controller-held stop confirmation, and any unconfirmed stop shall not
  qualify.
- RW6.9: When cancellation is observed after a Desktop Start boundary returns,
  cancellation shall win even if that boundary ignored its token and returned
  success: the Desktop shall not apply the result and shall synchronously stop
  the still-current returned session. When portable capture admission is
  cancelled, the controller shall likewise run local capture cleanup before
  propagating cancellation, including when the adapter ignored cancellation and
  returned success.

### RW7 - Bounds, concurrency, and privacy

- RW7.1: One session shall reserve at most 16 participant slots. The host, each
  active peer participant, and each peer-scoped pending-disconnect fact shall
  consume one slot from that shared budget. A pending slot shall not be reused
  for re-admission of its peer before cleanup confirms, and a different new peer
  shall be rejected before it can consume a seventeenth slot. Only confirmed
  disconnect cleanup or the next session generation may release a pending slot.
  All externally supplied labels, reason codes, batches, and events shall have
  fixed validation limits.
- RW7.2: Concurrent join, transfer, input, disconnect, protection, stop, and
  Capability reconciliation shall preserve a coherent snapshot and monotonic
  Driver Lease epoch.
- RW7.3: Exceptions from local adapters shall be reduced to stable boundary
  reason codes; exception messages shall not enter public results or diagnostics.
- RW7.4: Normal state and evidence shall exclude frames, audio, raw input,
  Activity descriptor payloads, credentials, session keys, and peer-supplied
  exception text.
- RW7.5: Remote Window wire messages shall require negotiated protocol 1.5 or
  later. A 1.0-1.4 session shall reject their construction and decoding while
  retaining its older negotiated feature set.
- RW7.6: Every Remote Window command and state shall bind the authenticated
  sender and recipient, one unpredictable live Session ID, the exact Activity
  ID, a correlation ID, a short deadline, and the current state or Driver epoch
  needed by that operation. A stale session, wrong participant, wrong Activity,
  expired envelope, or unsolicited result shall fail closed before controller
  or input work.
- RW7.7: Admission, Driver request, input, disconnect, and participant/
  protection state shall use strict canonical control schemas with no unknown,
  duplicate, null, or wrong-type fields. Remote input shall retain the portable
  1-64 event contract and shall not be placed in media frames.
- RW7.8: Video, audio, and cursor data shall use a separately framed binary
  channel protected with key material purpose-separated from control traffic.
  Each frame shall bind its live Session ID, Activity ID, media kind, monotonic
  sequence, and bounded chunk coordinates before exposing payload bytes.
- RW7.9: A media payload shall not exceed 64 KiB. A peer outbound queue shall
  hold at most 8 frames and 512 KiB; one live session shall reserve at most 128
  frames and 8 MiB across at most 15 remote peers. Video shall contain at most
  16 chunks per logical frame; audio and cursor frames shall contain one chunk.
- RW7.10: A peer that exceeds 512 media frames or 32 MiB in one second, sends an
  invalid encrypted length, cannot drain an accepted write within 2 seconds, or
  exceeds a queue/resource ceiling shall be rejected or backpressured without
  weakening control, protection, Driver, or Emergency Stop state. Every pending
  reservation shall be released on success, failure, cancellation, and dispose.
- RW7.11: Desktop disposal shall first cross a synchronous local Emergency Stop
  whenever a Remote Window session may exist, before cancellation callbacks,
  event removal, or an uncooperative Start can block. It shall then initiate
  cancellation and unsubscribe/drain the Remote Window and permission owners;
  one failed cleanup step shall not skip later steps, and concurrent disposal
  callers shall join the same completion and failure. The safety gate shall
  atomically order admission, reducer generations, permission revocation, late
  callbacks, and service disposal, but shall never invoke PropertyChanged,
  command observers, a dispatcher, or another user callback while held. Every
  permission-busy and associated command notification shall establish its
  callback lease before notification and run after the safety gate is released.
  When such an observer synchronously requests disposal, that disposal shall
  exclude its current callback lease from the drain so it cannot wait on itself;
  independent disposal callers shall still wait for complete cleanup.
- RW7.12: Shell disposal shall close and drain Activity-to-Remote-Window
  projection leases, initiate Remote Window and local-pairing safety teardown
  before awaiting either, and only then release Trust, identity, and other
  dependencies. Local pairing shall retain ownership of any session whose
  cleanup is unconfirmed, visibly block re-enable, and make concurrent disposal
  callers join one completion without a re-entrant cancellation callback waiting
  on itself.

### RW8 - Evidence

- RW8.1: Public-interface tests shall cover lifecycle, current Capability checks,
  every lease invalidation path, protection transitions, emergency-stop ordering,
  adapter failure, cancellation, and concurrency races.
- RW8.2: Model/property tests shall prove no old epoch regains authority across
  generated joins, transfers, disconnects, expiry, protection changes, stop,
  and explicit reset.
- RW8.3: Contract tests shall run on Windows, macOS, and Linux hosted CI, but
  shall be labelled portable unless they invoke and verify a matching native API.
- RW8.4: Task 6 is not complete until bounded encrypted media, hostile-peer
  limits, native capture/input/protection behavior, physical two-device use, and
  the real-machine acceptance matrix have separate evidence.
- RW8.5: Protocol 1.5 shall freeze canonical Remote Window control frames and
  hashes, test 1.4 downgrade rejection, and exercise authenticated two-node
  loopback binding. Media tests shall cover AEAD tamper, hostile length/rate,
  sequence/chunk validation, per-peer/session backpressure, timeout,
  cancellation, fault cleanup, and payload-free diagnostic strings.
- RW8.6: Headless Desktop tests shall prove persistent sharing text, dynamic
  accessibility names/help, keyboard Emergency Stop, focus stability, and
  truthful unavailable/protection/boundary-failure states through public view
  model and window behavior.
- RW8.7: Headless Desktop tests shall prove selected-Activity fallback preview,
  an independent keyboard-operable Remote Window target inventory, rejection of
  receive-only and drive-without-view peers, acceptance of mirror-view peers,
  fail-closed selection clearing after role/Trust/connection changes, keyboard
  start, exact participant role, command-result reduction before
  refresh, permission/start races and stop-before-dispatch ordering, one
  service-boundary crossing under concurrent starts, stale snapshot rejection,
  atomic concurrent revision reduction, inactive-before-success fail-closed
  cleanup, replacement-session isolation, cancellation-ignoring successful Start
  cleanup, generation-bound stale-outcome rejection, the exact stop-confirmed
  retry-reset predicate, new-session reset, and observer-triggered disposal
  without callback self-wait. Ordered safety progress shall hold when
  cancellation callbacks, event removal, observers, Start, or adapter boundaries
  block or fail. Portable controller tests shall prove retryable pending peer
  disconnect and current-generation cumulative Emergency Stop confirmations.
  Shell and pairing tests shall also prove parallel safety teardown, projection
  draining, retained cleanup ownership, and shared disposal completion.

## Non-goals for the authenticated control and bounded media slice

- Selecting a production video/audio codec, capturing a real desktop, rendering
  decoded frames, or claiming measured interactive quality on a physical LAN.
- Clipboard and file transfer. They require their own content policy, protocol,
  consent, and resource evidence.
- Implementing Windows Graphics Capture/SendInput, ScreenCaptureKit/macOS
  Accessibility, Wayland portals/PipeWire, or X11 input.
- Treating deterministic fakes or hosted runners as native permission,
  protected-surface, physical-device, or accessibility evidence.
- Persisting live sharing or Driver Leases across process restart. Restart ends
  live sharing and requires a new authorized session.
- Adding Remote Window or Mirror actions to Scene format v1.

## Traceability

| Requirement | Parent requirements |
| --- | --- |
| RW1 | R3.3-R3.5 |
| RW2 | R6.1, R9.2 |
| RW3 | R2.6, R6.5, R9.2 |
| RW4 | R6.2-R6.5 |
| RW5 | R9.3 |
| RW6 | R9.4 |
| RW7 | R8.4, R9.5, R12.3 |
| RW8 | R12.3-R12.5 |
