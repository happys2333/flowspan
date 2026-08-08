# Remote Window and Mirror Control Requirements

Status: approved v1 baseline; portable control-plane implementation in progress

## Problem and scope

Flowspan needs an honest fallback when an Activity cannot resume semantically.
Remote Window keeps execution on the source Device, presents captured output on
another Device, and optionally accepts narrowly authorized remote input. Mirror
uses the same live-sharing safety model without claiming that application state
moved.

This specification covers the v1 control plane, authorization, driver lease,
protection, emergency-stop, bounded resource, and evidence contracts. It also
defines the boundary for later media transport and native Windows, macOS, and
Linux adapters.

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
  advertise that capture is running.

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
  session before reporting reconciliation complete.
- RW3.6: A denied operation shall not disclose capture or input payloads and
  shall produce only a stable reason code and bounded identifiers.

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

### RW6 - Local emergency stop

- RW6.1: When emergency stop is activated locally, the controller shall first
  latch the stop, increment/revoke the Driver Lease epoch, and publish an
  emergency-stopped state synchronously.
- RW6.2: In the same local action, the controller shall synchronously request
  capture halt, input halt, and local sharing-session disconnect without sending
  or awaiting a peer acknowledgement.
- RW6.3: Emergency stop shall preempt a starting, active, protection-paused, or
  normally stopping session and shall be idempotent.
- RW6.4: Boundary failures shall be returned as separate bounded reason codes.
  Flowspan shall not report capture, input, or sessions stopped when that local
  boundary did not confirm the action.
- RW6.5: Reset shall require explicit local confirmation. Reset shall not restore
  participants, capture, or a prior remote Driver automatically.

### RW7 - Bounds, concurrency, and privacy

- RW7.1: One session shall contain at most 16 participants including the host,
  and all externally supplied labels, reason codes, batches, and events shall
  have fixed validation limits.
- RW7.2: Concurrent join, transfer, input, disconnect, protection, stop, and
  Capability reconciliation shall preserve a coherent snapshot and monotonic
  Driver Lease epoch.
- RW7.3: Exceptions from local adapters shall be reduced to stable boundary
  reason codes; exception messages shall not enter public results or diagnostics.
- RW7.4: Normal state and evidence shall exclude frames, audio, raw input,
  Activity descriptor payloads, credentials, session keys, and peer-supplied
  exception text.

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

## Non-goals for the portable control-plane slice

- Encoding or transporting video, audio, cursor, clipboard, or files.
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
