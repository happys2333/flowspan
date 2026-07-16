# ADR 0012: Persist atomic-swap intent before endpoint preparation

- Status: Accepted for task 3.3a
- Date: 2026-07-16
- Decision owners: Flowspan maintainers

## Context

Flowspan's initial in-memory Swap tracer prepares two endpoint reservations,
records a decision, and replays that decision after a simulated disconnect. It
does not survive coordinator restart. It also identifies a decision only by an
unordered token set, so a partial Abort cannot be replayed safely against the
same two endpoint channels. If the coordinator disappears after one Prepare but
before recording a decision, a new process lacks the tokens and request binding
needed to resolve that reservation.

R5 requires both original Activities to remain active before Commit, a durable
decision after Commit begins, convergence after connectivity loss, and
idempotent Prepare/Commit/Abort replay. The current descriptor-only
`workspace.note/v1` tracer can establish those coordinator boundaries without
claiming native process-state migration or production network delivery.

## Decision

Before sending the first Prepare or causing any endpoint mutation, the
coordinator writes one bounded, payload-free transaction intent. ADR 0014 later
adds a narrowly addressed, read-only exact Activity snapshot before this write;
that disclosure is not a reservation or mutation and is the only pre-intent
endpoint contact. The record binds:

1. Operation ID, correlation ID, and UTC deadline;
2. both Device and Activity IDs;
3. both expected Activity revisions and descriptor digests; and
4. two unique reservation tokens, each bound to its participant Device ID.

The journal is exact-once by Operation ID and request digest. A different
request conflicts. The canonical versioned payload is strictly decoded,
deterministically ordered, bounded to 256 transactions and 1 MiB, and published
in memory only after the payload store completes its atomic save.

The production payload store uses a Swap-specific `FSSF` AES-256-GCM envelope,
fresh nonce, authenticated header, bounded ciphertext, same-directory
write-through temporary file, and atomic replacement. Its independent random
256-bit key uses a Swap-specific CurrentUser DPAPI context/file on Windows,
Keychain service/account on macOS, or Secret Service purpose/account on Linux.
It does not reuse the Replace state path, magic, or credential identifier. Store,
key, authentication, version, bounds, and cancellation failures fail closed.
Any save exception permanently blocks further writes through that journal
instance because an atomic replacement may already have succeeded before the
exception surfaced. Recovery must reopen the protected file and follow the
decision actually present; it must never overwrite an ambiguously persisted
Commit with an Abort inferred from stale memory.

Commit or Abort is then stored as a transition on that same record before the
decision is sent. The decision digest covers outcome, UTC decision time, Abort
reason, and the ordered Device/token bindings. Commit has no failure reason;
Abort always records one. A reconstructed transaction that has no decision is
resolved only by first storing Abort and then applying it to both participants.
It is never continued toward Commit because the new process cannot prove how far
the previous Prepare sequence progressed.

Each endpoint accepts only the decision participant whose Device ID and token
match its reservation. An Abort delivered before Prepare becomes an idempotent
tombstone, causing a delayed Prepare for that Operation to reject. A live
Prepared reservation also excludes another Operation from reserving the same
local Activity.

## Alternatives considered

### Persist only the final decision

This leaves no durable evidence between the first Prepare and decision write.
After restart the coordinator would either leak a reservation or invent new
tokens and risk conflicting transactions.

### Retry an undecided transaction toward Commit

The reconstructed coordinator cannot know whether previous requests were
delivered or whether the observed Activities still represent the user's exact
selection. Conservatively choosing Abort preserves both originals and gives a
single recoverable outcome.

### Treat Abort on an unknown reservation as an error

With message reordering, Abort can legitimately arrive before Prepare. Rejecting
it permits a delayed Prepare to create a reservation after the transaction was
already aborted. A participant-bound tombstone closes that race.

### Put full Activity descriptors in the coordinator journal

Recovery of an undecided coordinator transaction needs identity, revision,
digest, and tokens, not Activity content. Excluding title and payload reduces
disclosure and keeps the coordinator record purpose-scoped.

## Consequences

- No endpoint is prepared or mutated unless the coordinator intent is durably
  saved; ADR 0014's exact read-only snapshot is the explicit exception.
- Coordinator restart can reuse an exact decision or safely converge an
  undecided transaction to Abort without creating new tokens.
- Device/token binding removes ambiguous partial-participant replay.
- The journal payload contains no Activity title or descriptor payload.
- The transaction file and encryption key are purpose-separated from identity,
  Trust, and Replace state.
- In-memory endpoints gain deterministic duplicate, reordering, exclusion, and
  recovery semantics suitable for generated fault tests.
- Endpoint reservations and decisions now have a protected journal, and ADR 0014
  composes the transaction through authenticated capability-bound control
  messages. Durable Activity-catalog/native Adapter effects, Desktop confirmation
  and visible recovery, and physical restart evidence remain open.

## Evidence and limits

Task 3.3a requires reconstructed-journal tests, write-failure ordering tests,
drop/duplicate/reorder cases, exact Operation conflict tests, generated
transition sequences, canonical codec hostile cases, local full-suite results,
authenticated-file tamper/bounds tests, supported-platform credential-store
contracts, and exact-commit Windows/macOS/Linux CI plus CodeQL. These tests are
same-host contracts. They do not prove physical LAN interruption, abrupt power loss,
protected endpoint persistence, native application state exchange, or packaged
Desktop accessibility.

## Revisit triggers

Revisit this decision when endpoint journals are protected and composed, when
the authenticated Swap wire format is frozen, or when a non-tracer Activity
Adapter requires an explicit durable apply/compensation protocol.
