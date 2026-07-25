# ADR 0017: Previewed best-effort Scene apply

- Status: Accepted for task 8.2 implementation
- Date: 2026-07-25
- Decision owners: Flowspan maintainers
- Review gate: destructive confirmation, protected journal, and recovery review

## Context

A saved Scene is an inert desired-placement definition. It cannot authorize
current operations because Activities, destination occupancy, Trust,
Capabilities, connections, and undo availability can change after save or even
after preview. Presenting a multi-Activity Scene as an atomic migration would
also be false: Handoff, Move, and Replace have independent acknowledgement and
recovery boundaries.

## Decision

- Scene apply has an expiring read-only preview followed by an exact approved
  execution request. New attempts require a preview no older than five minutes;
  accepted durable attempts retain their original binding for recovery rather
  than minting a new approval window.
- Preview freezes Scene identity/revision/digest, saved order, child operation
  identities, explicit exact-source selections, resolved current
  actions/blockers, and exact Replace targets.
- Scene definitions do not save source Devices. Source discovery is a purpose-
  scoped exact-Activity-ID lookup: zero active eligible sources block, multiple
  sources require explicit user selection and a complete new preview, and no
  ordering/revision/title/Device-ID heuristic may choose. An exact selected
  source already at the requested Device/slot resolves to No Change without an
  operation call.
- Destination emptiness comes only from a Scene-specific exact-slot occupancy
  query that distinguishes Empty, one Eligible Conflict, Opaque protected or
  ineligible occupancy, and Ambiguous. Filtered Replace inventory is never proof
  of Empty, and the latter two states disclose no hidden Activity metadata.
- `scene.apply` is an additional peer-relative orchestration Capability on every
  participant. It never replaces the existing Handoff/Move Offer/Receive or
  Replace Receive/Replace authorization checks at their operation boundaries.
- A selected remote source receives a strict payload-free child instruction and
  invokes the existing operation locally; the coordinator never receives or
  forwards Activity content. Scene source/slot/child control messages require
  explicit protocol-1.4 negotiation and durably replay exact child outcomes.
- Every Replace target is explicitly confirmed before the first mutation and
  revalidated at use time. Scene apply reuses the existing protected Replace and
  target-owned Undo Capsule path; it never invents blind overwrite.
- An occupied Move-After-Acknowledgement plus Replace-With-Undo item is blocked
  in v1. Existing Replace preserves its source; closing the source afterwards
  would make target-only undo capable of removing the incoming Activity's last
  instance, while retaining it would violate the requested Move.
- Items execute sequentially in saved order. Proven terminal failures do not
  block later independent items, but Recovering or unknown outcome stops the
  reducer before the next item.
- A protected bounded apply journal records the parent attempt and every child
  boundary so retry/restart cannot silently duplicate or skip mutation.
- Partial completion is first-class. Whole-Scene status is derived from ordered
  item evidence and is never described as atomic success.
- Compensation is explicit and limited to exact committed Preserve-Source
  Replace capsules in reverse order. Handoff and Move are not automatically
  reversed.

## Alternatives considered

### Treat Scene apply as an atomic distributed transaction

Rejected. Existing operations do not share one transaction and Move/Replace can
cross acknowledgement or recovery boundaries independently. Claiming atomicity
would hide real partial completion.

### Execute all items concurrently

Rejected for v1. Concurrent operations make placement conflicts, confirmation
races, partial-result ordering, cancellation, and recovery harder to explain and
test. Sequential saved order is deterministic and bounded to 64 items.

### Continue after acknowledgement loss

Rejected. Once one child may have committed without a known result, later
mutations would deepen an unresolved workspace state and make replay ambiguous.

### Automatically roll back every earlier item after failure

Rejected. Handoff is a copy and reverse Move may overwrite new state or repeat
side effects. Only existing exact Replace undo evidence supports safe explicit
compensation.

### Expand the current Group at apply time

Rejected by ADR 0016. The saved Scene's explicit items remain authoritative;
current Group revision can only produce a warning.

### Select a source or infer empty occupancy from existing projections

Rejected. Handoff can leave the same Activity active on several Devices, so a
Scene item has no uniquely implied source. Replace inventory deliberately hides
protected and ineligible Activities, so an empty filtered page cannot prove an
empty placement slot. Silent source selection or filtered emptiness would make
the preview nondeterministic and could overwrite work.

### Treat `scene.apply` as sufficient child-operation authority

Rejected. Scene orchestration and Activity disclosure/acceptance/replacement
are separate user grants. Collapsing them would let one high-level permission
bypass the least-privilege boundaries already enforced by each operation.

### Execute occupied Move-plus-Replace and compensate with target-only undo

Rejected. Target undo restores the replaced Activity and removes the incoming
replacement. If Scene apply had already closed that incoming Activity's source,
undo could remove its last instance. Keeping the source open is not a Move.
Supporting this combination needs a separately reviewed compound recovery
protocol; v1 blocks it before mutation.

## Consequences

- Users see the exact planned effects and destructive targets before mutation.
- Multi-placement Activities require a deliberate source choice; opaque slot
  blockers may intentionally reveal less detail than a user expects.
- An occupied Move-plus-Replace Scene item is visibly unsupported in v1 rather
  than receiving misleading Move or unsafe compensation semantics.
- Independent Activities can still complete and report truthful partial results.
- Scene apply needs its own protected progress journal even though the Scene
  repository remains task 8.3.
- Protocol 1.4 adds a bounded Scene control family; 1.0–1.3 peers expose the item
  as unsupported without receiving an unknown message.
- UI and diagnostics must represent Recovering and not-attempted remainders.
- Physical faults, native Adapters, and accessibility still require separate
  release evidence.

## Revisit triggers

Revisit concurrency only after durable conflict analysis and user research.
Revisit whole-Scene atomicity only if all participating operation types gain one
reviewed transaction protocol; current Handoff/Move/Replace semantics do not.
