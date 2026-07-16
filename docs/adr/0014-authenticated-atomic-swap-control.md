# ADR 0014: Carry Atomic Swap through an authenticated, capability-bound channel

- Status: Accepted for task 3.3c
- Date: 2026-07-16
- Decision owners: Flowspan maintainers

## Context

ADR 0012 persists coordinator intent and decisions. ADR 0013 persists endpoint
reservations and reduces terminal decisions after restart. The remaining
`ISwapEndpointChannel` still obtains the other Device's Activity through a
synchronous in-process catalog lookup and invokes endpoint methods directly.
That cannot represent a physical peer, does not bind responses to the
authenticated control session, and performs no peer-relative capability check.

Prepare needs complete original and incoming semantic Activity snapshots so a
participant can persist enough information to finish an exact Commit after
restart. A broad Activity inventory would disclose more titles and metadata than
the coordinator needs, while a payload-free snapshot cannot drive semantic
recovery. The transport therefore needs one narrowly addressed disclosure step
before Prepare.

Capability revocation introduces a second safety boundary. It must stop new
destructive work, but a Device cannot safely discard or reject an exact durable
Commit merely because the user revoked authority after both endpoints prepared.
Doing so could manufacture a mixed terminal outcome.

## Decision

Add the independent `activity.swap` Capability. It is not implied by Replace,
Offer, or Receive and is appended to the persisted Capability enum so existing
bit assignments remain stable.

Replace the channel's synchronous catalog lookup with an asynchronous exact
snapshot request. The request identifies one Operation, correlation, target
Device, Activity ID, and UTC deadline. A success returns one exact
`ActivityInstance`; rejection returns no Activity content. Only an active,
normal-sensitivity local Activity is eligible. There is no list or wildcard
request.

The authenticated control protocol adds three request/result pairs:

1. `activity.swap.snapshot` and `.result`;
2. `activity.swap.prepare` and `.result`; and
3. `activity.swap.decision` and `.result`.

The encrypted session authenticates the envelope sender. Message bodies bind
the target Device, Operation and correlation IDs, exact revisions and descriptor
digests, Device-bound reservation tokens, UTC deadlines, result phase and
failure, and the durable decision digest. Snapshot and Prepare bodies carry the
complete descriptors required by the semantic endpoint. Decoders are strict,
bounded by the existing 192 KiB body and 256 KiB frame limits, reject unknown
fields, and recompute every payload, descriptor, request, and decision digest.
Responses are accepted only for the matching pending request; an unsolicited or
cross-operation response faults the session closed.

The six message types require negotiated protocol 1.1. Version 1.0 continues to
support its existing non-Swap traffic but rejects Swap envelope construction and
decoding, and an authenticated 1.0 session is not exposed as a Swap channel. The
compatibility contract freezes six fixed-ID canonical frames, complete JSON, and
SHA-256 hashes. Snapshot and Prepare sending plus response wait end at their
recorded deadline; decision sending plus acknowledgement uses the decision
envelope's 30-second lifetime. A silent connected peer therefore produces
acknowledgement loss, releases the pending correlation, and closes the session
instead of waiting forever. Result handling rechecks the current clock so timer
scheduling delay cannot admit a response at or after its deadline.
Every inbound Swap request or result is also rejected at or after its envelope
expiry before endpoint work; recovery resends an unchanged durable decision in
a fresh 30-second envelope.
An independent injected-clock wait bounds send even if a connection ignores its
cancellation token. Pending cleanup removes the exact registered instance before
releasing its correlation, so an older send cannot free a newer cross-operation
owner after an early response.

An authorized endpoint applies these rules:

- exact snapshot and every new Prepare require the current peer-relative
  `activity.swap` grant, exact local/peer placements, and two active,
  normal-sensitivity Activities;
- an unknown decision, including Abort-before-Prepare, also requires the current
  grant so an unauthorized peer cannot consume bounded journal capacity;
- the endpoint journal format v2 binds Operation ID, correlation ID, and remote
  participant Device ID; older records without all three are unsupported rather
  than treated as evidence, and missing, null, wrong-type, duplicate, or
  non-canonical binding encodings fail closed;
- once that complete binding exists, the core endpoint may validate an exact
  Prepare replay or apply an exact decision even if the deadline passed or the
  grant was later revoked; a mismatched request, token, participant, correlation,
  peer, or digest still fails;
- session registration and revocation continue to use the authoritative Trust
  coordinator, so a revoked swap-only session drains and must not reconnect.

The coordinator can then combine a local direct endpoint channel with one real
authenticated remote channel. It fetches both snapshots, durably writes intent,
prepares both participants, records Commit or Abort, and drives the same
decision to each participant. Desktop discovery, exact confirmation, recovery
history, and destructive command enablement remain separate and unavailable in
this slice.

ADR 0012's intent-before-mutation rule still holds: the exact snapshot is a
read-only, single-Activity disclosure used to construct the intent, while no
Prepare or endpoint mutation occurs before the durable intent write.

## Alternatives considered

### Reuse `activity.replace`

Swap changes two Devices and has a durable two-participant convergence rule.
Granting single-target Replace must not silently authorize this larger action.

### Send Prepare without a snapshot query

The coordinator cannot build the reciprocal Prepare command without an exact
remote Activity snapshot. Smuggling an in-process reference through the channel
would leave production composition impossible and its tests misleading.

### Return a remote Activity inventory

Swap core needs one exact selected Activity. A list would disclose unrelated
titles and descriptors before the Desktop has a bounded, confirmed selection
workflow.

### Reject every message immediately after capability revocation

This is appropriate for new work but unsafe after a durable Commit exists.
Unilateral rejection could leave one endpoint committed and the other prepared.
Allowing only an exact already-recorded decision preserves least authority while
retaining convergence.

### Allow unknown Abort without a grant

Abort-before-Prepare is safe only inside an authorized transaction. Permitting
arbitrary unauthorized tombstones would let a peer exhaust the endpoint's 32
record bound.

## Consequences

- One encrypted loopback session can carry the full coordinator-to-participant
  protocol without pretending a remote catalog is local memory.
- Full Activity payload crosses only an authenticated, explicitly authorized
  channel and never enters discovery, receipts, or diagnostics.
- The pending-correlation registry becomes shared by Handoff, Move, Replace,
  Replace inventory, and all Swap phases; only one outstanding operation may own
  a correlation on a session.
- Protocol 1.1 is the first negotiated version that exposes Swap; 1.0 peers
  degrade by retaining other compatible control operations without a Swap
  channel.
- Endpoint journal payload v2 intentionally fails closed on v1 records because
  they cannot prove correlation/peer binding for post-revocation convergence.
- Capability UI and Trust persistence must recognize `activity.swap` without
  changing existing on-disk bit assignments.
- Protocol availability does not enable a Desktop Swap command. Exact user
  confirmation, visible recovery, Adapter eligibility, and physical-device
  evidence remain open.

## Evidence required

Task 3.3c requires golden and hostile codec tests for all six messages, exact
pending-response binding, unsolicited and cross-operation rejection, capability
denial for snapshot/Prepare/unknown decision, post-revocation convergence for an
already recorded Operation/correlation/peer binding, acknowledgement-loss
classification, and a real authenticated loopback coordinator using one local
and one remote durable
endpoint. Full local gates, fresh-process stress, Windows/macOS/Linux CI, Secret
Scan, CodeQL, and downloaded TRX counters are required. These remain same-host
and hosted-runner evidence, not physical two-device or arbitrary-application
state migration evidence.

## Revisit triggers

Revisit exact snapshot disclosure when Desktop Swap selection is designed,
when a non-tracer Adapter defines capture/resume eligibility, or when another
minor version needs capability negotiation finer than the current version gate.
