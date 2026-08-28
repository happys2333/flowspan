# ADR 0026: Protocol 1.7 Remote Window preparation

- Status: Accepted
- Date: 2026-08-28

## Context

The production Desktop starts a Remote Window from the host: the user selects
one exact local source, one authenticated participant, and a requested role.
Protocol 1.5, however, can only admit a participant that already knows the live
Session and Activity binding. Its `remote-window.admission` command is strictly
participant-to-host. Protocol 1.6 adds an authenticated `FSM1` media attachment,
but its clear route ID is only a locator and the attachment grants no session,
Capability, Driver, or rendering authority.

None of the existing messages can safely initiate the host-selected workflow:

- `remote-window.state` may describe only a known admitted binding; an
  unsolicited state for an unknown Session and Activity is fatal;
- `activity.transfer` requires an Activity Descriptor and Activity Kind, which a
  generic native-window source deliberately does not have;
- Driver, input, and disconnect operate only on an existing participant; and
- exposing an `FSM1` route in control JSON would turn a process-local locator
  into unnecessary protocol metadata without establishing readiness or consent.

Protocol 1.6 and its fixtures are already frozen by ADR 0024. Adding a new
control type under negotiated 1.6 would make an old 1.6 peer treat valid new
traffic as an unknown fatal type while both peers claimed the same feature set.

## Decision

Protocol 1.7 adds one bounded Remote Window Preparation transaction with two new
strict control messages:

| Message | Direction | Meaning |
| --- | --- | --- |
| `remote-window.prepare` | host to participant | Ask the exact participant to prepare one Session/Activity/role binding. |
| `remote-window.ready` | participant to host | Return one terminal ready or rejected result for that exact Prepare. |

`Prepare` and `Ready` are independent protocol messages. `Ready` does not reuse
`remote-window.admission`, and a protocol-1.7 host does not require a participant
to invent an unsolicited Admission request in order to answer a host-selected
operation. Protocol 1.5 Admission remains byte-for-byte compatible for its
existing participant-initiated control contract.

The control envelope's correlation ID is the Preparation transaction ID. Both
messages bind that correlation, the negotiated protocol, authenticated sender,
unpredictable Remote Window Session ID, exact Activity ID, host Device ID,
participant Device ID, requested `ViewOnly` or `DriverEligible` role, and one
canonical whole-millisecond UTC deadline no more than ten seconds after the
Prepare was sent.
Prepare additionally carries `prepareDigest`, the uppercase hexadecimal SHA-256
of the canonical binding prefixed by the domain
`flowspan.remote-window.prepare.v1`. Ready repeats every field including that
digest and adds:

- one boolean `ready` terminal outcome; and
- one allowlisted bounded `reasonCode` that discloses no exception or native
  detail.

The digest input is the domain, protocol major, protocol minor, correlation ID,
Session ID, Activity ID, host Device ID, participant Device ID, canonical role,
and deadline Unix milliseconds, each UTF-8 encoded in that order with one line
feed separator and no trailing separator. Both peers recompute it and compare
the decoded 32 bytes in constant time. The exact repeated binding, correlation,
digest, and authenticated connection generation are one request identity. No
native token, native handle, source generation, media route ID, Activity
Descriptor, Activity Kind, raw title, input, frame, key, or exception text
appears in either body. Unknown, duplicate, null, wrong-type, or trailing fields
are rejected.

Prepare and Ready writers convert the observed send time to UTC and truncate it
to a whole-millisecond envelope `sentAt` before calculating the exact integral
TTL to the unchanged deadline. This canonicalization can move `sentAt` earlier by
less than one millisecond; it never moves or extends the authorization deadline.
For both `sentAt` and the body deadline, the lexical form is fixed-width
`yyyy-MM-ddTHH:mm:ss`, followed by no fraction for zero milliseconds or the
shortest one-to-three digit fraction that preserves the millisecond value, and
the literal `+00:00` suffix. Thus 1, 10, 100, 120, and 123 milliseconds encode as
`.001`, `.01`, `.1`, `.12`, and `.123`. `Z`, another offset, redundant fractional
zeros, more than three fraction digits, and variable-width date or time fields
are noncanonical and rejected. This lexical restriction applies only to the new
1.7 Prepare/Ready readers; frozen 1.5 and 1.6 readers retain their accepted UTC
spellings.

Preparation has this ordered state machine:

1. The host verifies protocol 1.7, the exact current source lease, authenticated
   connection, Trust and role Capabilities, prompt-free permissions/readiness,
   fresh Safe protection, Emergency Stop readiness, and media ownership. It
   allocates an unpredictable Session ID, registers the responder `FSM1` route,
   records one exact pending Prepare, and sends `Prepare`. It has not crossed
   native capture and has admitted no participant.
   A successful Ready received before Prepare send begins is fatal. While the
   Prepare send is in progress, one exact success enters `ReadyBuffered` but
   cannot complete the transaction or authorize final Admission until send
   success wins the Stop/deadline commit and advances it to
   `ReadyAcknowledged`. The readiness completion is published in that same
   commit and cannot later be reversed by Stop or a post-commit clock read.
   An exact Ready rejection is a safe terminal acknowledgement and closes the
   connection even if that close cancels the local send flush.
2. The participant validates the authenticated direction, every binding,
   deadline, current authenticated Trust/connection, non-revoked/non-stopping
   receiver state, requested role, local receive policy, renderer, and media
   readiness. It does not require its reciprocal local `mirror.view` or
   `mirror.drive` grant, which would authorize the opposite source direction;
   v1 has no `remote-window.receive` Capability. It connects the initiator side
   of the exact `FSM1` attachment outside the control read loop. The dispatcher
   must remain able to read and route control traffic while asynchronous
   media/renderer preparation is pending.
3. After the attachment acknowledgement and renderer preparation succeed, the
   participant sends exactly one `Ready(true, participant_ready)`. A user denial
   or local preparation failure sends one `Ready(false, <bounded reason>)` when
   the message itself remains well formed. A malformed, unauthenticated, or
   wrongly bound Prepare faults the control connection without reflecting
   attacker-selected detail.
   A final Admission received before Ready send begins is fatal and cannot invoke
   the participant endpoint. While the Ready send is in progress, the participant
   may buffer at most one strictly bound final Admission without acting on it. It
   consumes that frame only after Ready send succeeds; send failure discards it
   and closes the connection.
4. The host matches `Ready` to the one live pending request and current media
   binding, then rechecks source, connection, Trust, Capabilities, permission,
   protection, and Emergency Stop facts. Only a matching `Ready(true)` may
   continue. The host registers the protection and independent Emergency Stop
   owners, starts the controller/native capture while media-frame admission is
   still closed, and then adds the exact participant with the frozen role.
5. The host returns the existing strict `remote-window.state` admission outcome
   for the same correlation and binding. The participant establishes its known
   live binding and opens rendering only when that state has action `Admission`,
   outcome `Applied` or `AlreadyApplied`, and the exact requested effective role,
   and the current media binding still matches. The first accepted media
   sequence must still advance strictly. Ready itself never establishes a known
   live binding.

`Prepare`, `Ready`, route possession, attachment success, renderer readiness,
permission success, and authenticated connection admission each grant no
Capability, participant membership, Driver Lease, input authority, capture
authority, or rendering authority by themselves. The controller remains the
authority owner. Every use boundary reloads current facts.

Prepare, Ready, and final Admission also recheck their absolute Preparation
deadline at the actual send-admission linearization point, while holding the
same Stop-before-send gate and before calling the wire boundary. Timer or
watchdog scheduling latency therefore cannot place an already-expired frame on
the connection.

One authenticated control registration permits at most one Remote Window
Preparation because its transferred media session can select only one route
role. A duplicate, conflicting, unknown, expired, or already-terminal
correlation fails closed. A terminal tombstone remains until the deadline or
connection close so a delayed `Ready` cannot revive work. A role change is a new
Preparation on a fresh authenticated connection; it does not mutate the pending
role.

`Ready(false)`, timeout, cancellation, disconnect, revocation, malformed
traffic, route/attachment failure, host revalidation failure, capture failure,
participant admission failure, final-state failure, or renderer failure closes
frame admission and unwinds every prepared owner. Once a route or media-session
role was selected, cleanup consumes that media session and closes its owning
authenticated control connection. Retry requires a complete fresh authenticated
handshake, new media session, new route, new Session ID, and new correlation. It
must not republish a consumed route or retry Preparation on the old connection.
Cleanup closes admission before draining renderer, queues, attachment, route,
controller, protection, and Emergency Stop owners; one cleanup failure cannot
skip later stages, and concurrent failures remain observable without exposing
their text to the peer.

Protocol support is explicit:

- 1.5: frozen Remote Window control and encrypted media-frame formats;
- 1.6: 1.5 plus the frozen authenticated `FSM1` media attachment; and
- 1.7: 1.6 plus host-selected Remote Window Preparation.

A peer negotiated below 1.7 must not construct, advertise, accept, or silently
approximate Preparation. There is no downgrade to Activity transfer, unsolicited
state, clear media, or an unprepared Admission. Production target readiness for
the host-selected workflow therefore requires protocol 1.7 or later.

## Rejected alternatives

### Reuse `remote-window.admission` as Ready

Admission is frozen participant-to-host behavior and conflates readiness with
participant membership. It also cannot return an explicit participant rejection
without waiting for timeout. Rejected; Preparation has its own terminal result.

### Publish an unsolicited `remote-window.state`

Accepting state for an unknown binding would let a host create sharing state on
a participant that has not prepared local rendering and would weaken the
existing replay guard. Rejected.

### Send an Activity transfer

A generic native source has no descriptor or semantic kind and must stay out of
Handoff, Move, Replace, Swap, Group, and Scene inventories. Rejected.

### Put the route ID in Prepare

The attachment already derives the route from its connection-owned media
session. The clear route locator is neither authority nor user-visible state.
Rejected.

### Add the messages to protocol 1.6

This would mutate an accepted frozen compatibility contract and make version
negotiation lie to existing 1.6 peers. Rejected; the feature starts at 1.7.

## Consequences

- Production peers prefer 1.7, while 1.5 and 1.6 retain their frozen behavior.
- The control dispatcher gains two 1.7-gated Remote Window types and must launch
  participant preparation without blocking its sole read loop.
- The Desktop runtime needs one bounded pending-Preparation owner per control
  registration and an exact final-state gate before media publication.
- Canonical 1.7 Prepare/Ready fixtures, digest vectors, complementary one-way
  grant cases, sub-millisecond clock cases, strict hostile cases,
  1.6 byte-stability,
  downgrade, replay, concurrency, deadlock, and failure-cleanup tests are release
  evidence.
- This decision does not make production Remote Window available. Task 5.5,
  native adapters, packaged accessibility, physical two-device quality, signed
  release evidence, v1 release criteria, and the long-term Goal remain open.
