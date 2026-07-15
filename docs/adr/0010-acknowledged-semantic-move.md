# ADR 0010: Close a moved source only after verified target acknowledgement

- Status: Accepted for desktop task 7.3b
- Date: 2026-07-15
- Decision owners: Flowspan maintainers

## Context

ADR 0009 introduced a bounded `workspace.note/v1` Semantic Handoff over one
authenticated control channel. Handoff is additive: the target resumes a copy
and the source remains active. Move has a stricter safety boundary because a
premature or guessed source close can destroy the only usable Activity.

The existing application state machine already models target-first Move,
idempotent delivery, acknowledgement loss, and source-cleanup failure. The
desktop needs to expose those semantics without claiming process migration,
without weakening directional authorization, and without presenting Move under
a source-preserving Handoff confirmation.

## Decision

Desktop task 7.3b supports Move only for the existing bounded
`workspace.note/v1` Activity kind. The source performs these steps in order:

1. verify that the local Activity is still active;
2. re-read Trust and require local `activity.receive` for the selected target;
3. require that target's live authenticated Activity channel;
4. send the bounded descriptor through the existing encrypted transfer;
5. accept only a payload-free receipt precisely bound to the authenticated
   participants, protocol, correlation, Operation, Activity, kind, and
   descriptor digest;
6. only after a committed target receipt, ask the source Adapter to close.

Target rejection, delivery failure, and acknowledgement loss preserve the
source. A successful target resume followed by source-close failure is
`CommittedWithWarning / SourceCleanupFailed`: the target is not rolled back and
the UI says two active copies may exist. Only an exactly `Committed` Move removes
the closed source from the desktop's active Activity projection.

The target continues to bind the sender to the authenticated Device ID and
requires its current local `activity.offer` before Adapter use. Shared-channel
any-of admission remains only a liveness decision; it does not replace either
operation-direction check.

Handoff and Move use separate previews and confirmation controls. The Move
preview states target-first ordering and all source-preserving negative outcomes.
The target list and common receipt use operation-neutral automation names.
Receipt summaries and undo guidance are operation-aware: a committed Move has no
automatic reversal and requires a new Move to return; an uncertain Move instructs
the user to inspect both devices before retrying.

## Alternatives considered

### Close or suspend the source before transfer

This makes a network or target failure destructive and violates the invariant
that a Move never removes the only acknowledged Activity.

### Treat a lost acknowledgement as rejection

The target may already have resumed the Activity. Claiming rejection would hide
a possible duplicate and could make an immediate retry misleading.

### Roll back the target when source cleanup fails

Destroying an acknowledged target to manufacture an all-or-nothing result can
leave neither side usable if rollback also fails. The committed target is kept
and duplicate cleanup is surfaced explicitly.

### Put a Move button in the Handoff preview

The Handoff preview promises that the source stays open. Reusing that confirmation
for Move would invalidate the user's reviewed boundary and confuse assistive
technology.

## Consequences

- A clean Move is target-first and removes the closed source from the active UI.
- Negative or uncertain delivery outcomes preserve the source and remain
  retryable or inspectable according to the receipt.
- Source-cleanup failure is visible as committed-with-warning, not success or
  rollback.
- Handoff and Move share a bounded encrypted transport but retain distinct user
  semantics.
- The current desktop journal and Activity catalog are still in memory; durable
  restart recovery and duplicate-reconciliation history remain future work.
- Replace, swap, Mirror, driver transfer, Remote Window media, and additional
  Activity kinds remain separate slices.

## Evidence and limits

Deterministic application tests cover target-first ordering, rejection,
delivery/acknowledgement loss, idempotent retry, and source-cleanup failure.
Desktop tests cover source authorization, peer unavailability, authenticated
encrypted loopback success and target rejection, result projection, and
keyboard/automation behavior. These are local, same-host, headless, and—after
exact-commit workflows pass—hosted CI contracts. They do not prove physical LAN,
two-machine interruption, packaged native accessibility, arbitrary application
state transfer, or durable restart recovery.

## Revisit triggers

Revisit this decision when Activity/journal persistence is added, when users can
reconcile duplicate outcomes from history, when another Activity kind needs a
different close semantic, or when replace/swap introduces compensating undo.
