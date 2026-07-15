# ADR 0011: Bounded Replace with a Target-Owned Undo Capsule

Status: accepted for the `workspace.note/v1` tracer slice; durable state,
query-only target inventory, and preview-only confirmation delivered;
destructive desktop activation pending

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

## Consequences

- Replace cannot accidentally inherit Handoff/Move's source-preserving or
  source-cleanup semantics.
- A source never receives the target's preserved payload in a result or receipt.
- Adapters that cannot prove an honest semantic capsule fail before destructive
  work; Remote Window is not silently substituted.
- The durable-state module and Windows/macOS/Linux protected-key adapters now
  exist. Desktop composes a payload-free target query plus a preview-only,
  snapshot-bound confirmation surface, but it still does not compose
  destructive Replace. In-memory state remains available only for deterministic
  tests. Receipt/recovery presentation, startup recovery presentation, and
  target-local visible undo remain mandatory before product activation.
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
- The durable-state candidate covers protected-key restart, exact Replace and
  undo replay, pending recovery without duplicate Adapter calls, atomic save
  failure on both sides of destructive boundaries, authenticated-file and
  canonical-descriptor tamper, bounds, expiry cleanup, concurrent retry, and
  current-host macOS Keychain smoke. Hosted Windows DPAPI and portable Linux
  Secret Service contracts remain required before this sub-slice can close.
