# ADR 0011: Bounded Replace with a Target-Owned Undo Capsule

Status: accepted for the `workspace.note/v1` tracer slice; durable state,
query-only target inventory, preview-only confirmation, protected recovery, and
target-local visible undo delivered; destructive desktop activation pending

Date: 2026-07-15

## Context

Replace is intentionally destructive: it installs an incoming Activity in a
Placement currently occupied by another Activity. R4.4 and R11.2 require
Flowspan to preserve enough target state before that work and to offer an
idempotent compensating undo while the retention window remains open. A generic
Handoff/Move offer is insufficient because it contains no target Activity,
expected revision, target descriptor digest, or undo commitment.

Flowspan must also avoid suggesting that an Adapter can preserve arbitrary
process memory or unsaved application internals. The first supported kind is the
bounded `workspace.note/v1` descriptor, whose complete semantic state is the
validated descriptor itself.

## Decision

Replace uses a distinct application command and authenticated wire message,
`activity.replace`. The request digest binds all of the following:

- Operation and correlation IDs plus operation deadline;
- authenticated source and target device IDs;
- target Activity ID, expected revision, and descriptor digest;
- incoming Activity ID, kind, descriptor digest, payload digest, and payload;
- target Placement; and
- undo expiry.

The target rechecks its current `activity.replace` grant and exact target
snapshot. It then asks an `IReplaceActivityAdapter` to capture the target's
semantic state. The application validates that the captured descriptor is the
same Activity and descriptor digest, creates a target-owned capsule, and must
store it before calling incoming resume or changing the Activity catalog. A
capture, validation, or store failure returns a named rejection and performs no
incoming resume.

The current retention limit is 15 minutes. A capsule binds the original and
replacement Activities, target revision/digests, Operation/correlation, devices,
capture time, and expiry. The full original descriptor stays in the target
store. The authenticated result returns only a payload-free capsule reference:
capsule ID, target ID/revision/digest, incoming ID/digest, and expiry.

Undo is local to the target. It succeeds only while the capsule is unexpired and
the current Activity is still the exact replacement revision. Restore creates a
new revision rather than reviving the old revision number. A replay of the same
undo request returns its recorded result without invoking the Adapter twice; a
different request against an already consumed capsule is rejected. Expired,
unknown, consumed, and revision-conflicted capsules have distinct reason codes.

Replace uses `activity.replace`, not `activity.offer`. A lost result is reported
as acknowledgement-lost; the sender cannot infer whether the target committed.
The target operation journal makes an exact retry idempotent and rejects reuse of
the Operation ID with different bound content.

### Purpose-scoped target inventory

Target discovery uses distinct `activity.replace.inventory` request/result
messages rather than exposing a general remote Activity browser. The source
requires its current peer-relative `activity.receive` before channel lookup; on
every request the target reloads the requesting peer's current
`activity.replace` before catalog projection. The request binds the target,
incoming kind, correlation, and deadline. Only active, normal-sensitivity,
target-local, same-kind Activities backed by an `IReplaceActivityAdapter` are
eligible.

The result contains at most 64 snapshots in strict Activity-ID order. Each
snapshot contains only ID, positive revision, descriptor digest, kind, bounded
title, and bounded Placement slot. Payload, payload digest, origin, incompatible
kind, and protected metadata stay local. Truncation is valid only for a full
page; rejected results contain no targets. Capture time is bound to the query
deadline and authenticated send time. Transfer, inventory, and destructive
Replace share one atomic pending correlation reservation per secure session.

An inventory snapshot is preview data, never mutation authority. A later
destructive command must still carry and revalidate the selected
ID/revision/digest before Adapter capture, resume, or catalog mutation. The
desktop now composes the query-only inventory endpoint but does not inject an
`IReplacePeer` or expose a destructive Replace control. Activation remains
blocked until the UI presents both Activities, obtains explicit confirmation,
shows recovery/receipt state and exact undo expiry, and exposes target-local
undo. Protocol availability is not evidence that this user flow is shipped.

### Durable target state

The delivered durable slice persists the bounded capsule repository, Replace
operation journal, undo journal, and capsule-consumption markers as one
versioned target-owned snapshot. The application port exposes only bounded
load, atomic replace, and delete operations. A platform adapter obtains a
random 256-bit state key from the current user's OS credential service and uses
AES-256-GCM to protect the snapshot in an atomic local file. Windows protects
the key with current-user DPAPI, macOS stores it in Keychain, and Linux stores
it through Secret Service. Each purpose uses identifiers distinct from device
identity and Trust data. Full Activity descriptors never enter command-line
arguments, logs, receipts, discovery, or the remote result.

The file envelope has an explicit magic value and format version, a fresh nonce
for every save, bounded ciphertext length, and an authentication tag. Save
writes and flushes a same-directory temporary file before atomic replacement;
the in-memory snapshot changes only after that replacement succeeds. Startup
fails closed on an unavailable key, unsupported version, malformed bounds,
authentication failure, non-canonical state, or a descriptor/digest mismatch.
No empty in-memory fallback may hide an unreadable existing repository.

The durable journal writes a `Pending` record before invoking destructive
Adapter work and replaces it with a terminal result afterward. An exact retry
of a terminal record replays it; different content conflicts. A `Pending`
record observed after restart is reported as `Recovering` and is never silently
re-executed. Undo preparation reserves the capsule for one Operation; terminal
commit records the result and consumption marker atomically. If the terminal
write fails after Adapter/catalog mutation, the caller receives `Recovering`
and the durable `Pending` record continues to block a duplicate restore.

Expired unconsumed capsules and their non-pending metadata are removed by a
bounded explicit cleanup operation. Pending recovery records and consumption
markers are retained until a later recovery/history policy can prove they are
safe to prune. Store-full, key, disk, cancellation, and authentication failures
remain structured and must not cross a destructive boundary unnoticed.

The same in-memory snapshot provides a bounded read-only recovery projection
for the target desktop. It combines Replace and undo journal entries without
serializing another history file, orders unresolved entries before terminal
history, and discloses only known opaque IDs, participants, timestamps,
redacted outcome/reason, and exact capsule expiry/availability. Descriptor
metadata and payload, preserved state, request digests, and exceptions are
excluded. A pending entry written before capsule capture may contain only an
Operation ID and is presented as incomplete rather than reconstructed from
untrusted or unrelated state.

### Target-local visible undo and restart reduction

The visible undo slice keeps one private target-local `ReplaceEndpoint` beside
the Desktop catalog and protected store, but it does not expose that endpoint to
an authenticated session or compose a source-side destructive command. This
reuses the existing exact-current, pending-before-Adapter, single-consume, and
terminal-replay invariants without creating a second undo implementation.

Because the Desktop catalog is currently process-memory state, startup reduces
the protected terminal history to an exact state-transition graph for the
descriptor-complete `workspace.note/v1` tracer. A committed Replace edge maps
its captured original instance to its exact replacement. A committed undo edge
maps that replacement to the preserved descriptor at the next revision. Only
unambiguous local-target frontier instances are admitted to the empty startup
catalog. A pending or `Recovering` Replace/undo entry blocks all reconstruction;
receipt/capsule mismatch, an orphaned capsule or committed receipt/undo,
conflicting graph edges, duplicate frontier Activity IDs, unsupported Adapter
kinds, or a non-exact live catalog value fail closed. This does not rerun undo
restore and is not a promise of arbitrary process-state recovery or a general
Activity persistence layer.

The UI exposes one action only for a terminal committed Replace whose capsule is
unexpired, unconsumed, has no prior undo attempt, and still names the catalog's
exact current replacement. Selection and recovery refresh revoke confirmation.
The confirmation identifies the opaque capsule, original/replacement Activity
IDs, and exact expiry. Pending, committed, rejected, failed, and recovering
results are projected from the same protected journal; no outcome is inferred
from a timeout or exception, and no completed attempt is silently retried under
a new Operation ID.

The Desktop service performs the same live eligibility preflight so a non-UI
caller cannot bypass the selected recovery mapping. Unknown capsules, unreadable
state, any global pending/`Recovering` boundary, and exact-current capsules that
are no longer actionable stop before a new undo journal write. A known expired,
consumed, or catalog-stale capsule is allowed through to the core endpoint only
to preserve its precise rejection reason; none can cross the Adapter restore
boundary.

## Consequences

- Replace cannot accidentally inherit Handoff/Move's source-preserving or
  source-cleanup semantics.
- A source never receives the target's preserved payload in a result or receipt.
- Adapters that cannot prove an honest semantic capsule fail before destructive
  work; Remote Window is not silently substituted.
- The durable-state module and Windows/macOS/Linux protected-key adapters now
  exist. Desktop composes a payload-free target query plus a preview-only,
  snapshot-bound confirmation surface, a protected target-local recovery
  projection, and exact confirmed local undo, but it still does not compose
  destructive Replace. In-memory state remains available only for deterministic
  tests. Destructive source/target composition remains mandatory before product
  activation.
- Same-host loopback tests prove authenticated framing and state ordering, not
  physical LAN behavior or native application restoration.

## Verification

- Application integration tests cover capture/store failure, mismatched capture,
  target revision/digest conflict, successful replace, exact retry, successful
  undo, undo retry, expiry, and consumed capsules.
- Codec tests cover strict request/result round trips, target-snapshot tampering,
  payload-free capsule references, and participant/correlation binding.
- Session tests cover authenticated inbound/outbound Replace, exact pending
  result binding, acknowledgement loss, forged target metadata, and a real
  encrypted loopback connection.
- Inventory tests cover authorization and live downgrade, same-kind eligibility,
  sensitive/restricted/inactive/non-local/different-kind/unsupported filtering,
  non-Replace incoming Adapter rejection, canonical 64-item truncation, strict
  schema/purpose/time binding, global pending correlation exclusion,
  acknowledgement loss, and a real encrypted loopback query without catalog
  mutation.
- Desktop tests cover explicit query, incoming/target comparison, bounded
  coverage and capture time, confirmation revocation, stale revision/digest or
  missing-target refresh, late-result rejection, sanitized recovery, keyboard
  operation, accessible names/state, and the uncomposed destructive capability.
- Recovery projection tests cover unresolved-first 64-record bounds, known-only
  pending fields, payload/title/digest canaries, available/expired/pending/
  consumed capsule state, protected-store restart and failure, sanitized
  Desktop startup, keyboard list navigation, accessible names, and the locked
  destructive endpoint.
- Target-local undo tests cover terminal-graph restart reduction, pending and
  ambiguous fail-closed behavior, exact-current/expiry/consumption gating,
  explicit confirmation revocation, every recorded outcome, and replay across a
  reconstructed process without another Adapter restore.
- The durable-state candidate covers protected-key restart, exact Replace and
  undo replay, pending recovery without duplicate Adapter calls, atomic save
  failure on both sides of destructive boundaries, authenticated-file and
  canonical-descriptor tamper, bounds, expiry cleanup, concurrent retry, and
  current-host macOS Keychain smoke. Hosted Windows DPAPI and portable Linux
  Secret Service contracts remain required before this sub-slice can close.
