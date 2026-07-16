# ADR 0013: Persist endpoint reservations before acknowledgement

- Status: Accepted for task 3.3b
- Date: 2026-07-16
- Decision owners: Flowspan maintainers

## Context

ADR 0012 makes the Atomic Swap coordinator durable, but each endpoint still
keeps its reservation, Abort tombstone, and applied decision only in process
memory. An endpoint restart after returning Prepared can therefore forget the
reservation token. A restart after accepting a durable Commit can also lose the
incoming Activity snapshot needed to finish the local replacement.

R5 requires both endpoints to reserve before either replacement, converge from
the durable decision after connectivity loss, and accept replay idempotently.
Task 3.3b closes the endpoint half of that boundary before any authenticated
wire or Desktop composition is exposed.

## Decision

Each Device owns one bounded endpoint journal. The protected snapshot binds the
top-level Device ID and stores at most 32 Operation records in 4 MiB. A record
contains its reservation token, exact original and incoming Activity snapshots,
UTC expiry, request digest, terminal decision and decision digest when known.
An Abort delivered before Prepare is stored as a decision-only tombstone.

Unlike the payload-free coordinator intent, a Prepared endpoint record must
contain the complete descriptors. A process reconstructed after Commit needs
the incoming title and adapter payload to create the exact replacement. This is
private local recovery state: it is encrypted and authenticated at rest and is
never added to discovery, diagnostics, receipts, or coordinator records.

The endpoint follows these write boundaries:

1. validate the exact local catalog snapshot and conflicts;
2. persist Prepared before returning a successful Prepare acknowledgement;
3. persist the exact Commit or Abort decision before changing the catalog or
   acknowledging the decision;
4. for Commit, reduce the durable record against the authoritative catalog;
5. acknowledge only when that reduction reaches the exact committed snapshot.

Commit reduction accepts only two catalog states:

- the exact original exists and the incoming ID does not: atomically replace
  the original with the recorded incoming snapshot; or
- the original is absent and the exact computed replacement already exists:
  treat a replay after acknowledgement loss or restart as complete.

Any other catalog state is a revision conflict and remains recovering. A
Prepared record never infers Abort from expiry or restart; it remains reserved
until the authenticated coordinator decision is delivered. Abort changes no
Activity and remains as an idempotent tombstone.

The canonical JSON codec is strict, deterministically ordered, versioned, and
rejects unknown fields, Device mismatch, duplicate Operation IDs, invalid enum
or UTC values, descriptor/request/decision digest mismatch, invalid participant
binding, and bounds violations.

Prepared admission also proves terminal reducibility: the incoming revision
must be below `long.MaxValue`, and each undecided record reserves 1 KiB inside
the 4 MiB payload limit for the fixed-shape two-participant decision. A peer
cannot obtain Prepared for state that will overflow either the replacement
revision or the later decision write. The strict reader requires every field,
including zero-valued enums, and rejects duplicate fields, non-canonical GUIDs,
or timestamp aliases that the writer never emits.

The endpoint file uses a distinct `FSEF` AES-256-GCM envelope and an independent
random key. Windows protects that key with a Swap-endpoint-specific CurrentUser
DPAPI context and file; macOS uses a separate Keychain service/account; Linux
uses a separate Secret Service purpose/account and coordination lock. No
endpoint path, magic, or key purpose is reused from coordinator or Replace
state.

As with the coordinator journal, any save exception permanently blocks further
writes through that journal instance. Atomic replacement may have completed
before the exception surfaced, so recovery must reopen the file and follow its
actual terminal decision rather than overwrite it from stale memory.

## Alternatives considered

### Persist only token and descriptor digests

Digests can validate a later snapshot but cannot reconstruct the incoming
Activity. Commit recovery would depend on an unavailable peer or would invent
content, violating deterministic endpoint convergence.

### Persist an `applied` flag after changing the catalog

This adds a second ambiguous destructive boundary. Persisting Commit first and
reducing against the two exact catalog states makes the catalog mutation itself
idempotent without requiring a post-mutation write.

### Abort every Prepared record on endpoint restart

The coordinator may already hold a durable timely Commit. A unilateral Abort
would permit mixed terminal state, so Prepared remains blocked until the
recorded coordinator decision arrives.

### Reuse the coordinator state file or key

Coordinator and endpoint records have different disclosure and lifecycle
properties, and a Device may act as both. Separate files and keys avoid
multi-writer corruption and preserve purpose separation.

## Consequences

- Prepare success proves that the local reservation can survive restart.
- A terminal decision survives restart before any local destructive change.
- Replayed Commit and Abort reduce idempotently from exact catalog evidence.
- Full Activity content is present in the protected endpoint journal, increasing
  the importance of key, file, retention, and diagnostics boundaries.
- Capacity or persistence failure fails closed and never returns Prepared or
  mutates the catalog.
- The Activity catalog remains an external authoritative Adapter boundary; this
  slice does not claim to persist arbitrary application process state.
- Authenticated Swap messages, capability grants, Desktop confirmation and
  recovery UI, physical LAN interruption, live Linux Secret Service, and native
  application evidence remain later work.

## Evidence required

Task 3.3b requires reconstructed-journal Prepare/Commit/Abort tests, exact
catalog reduction tests, already-applied replay, Prepared blocking, ambiguous
save failure, capacity and hostile-codec tests, authenticated endpoint-file
tamper/bounds tests, independent supported-platform credential contracts,
fresh-process stress, full local gates, and exact-commit Windows/macOS/Linux CI,
Secret Scan, and CodeQL.

## Revisit triggers

Revisit retention and compaction before Desktop history is composed, and revisit
the catalog reduction contract when a non-tracer Adapter introduces an explicit
durable apply/compensation protocol.
