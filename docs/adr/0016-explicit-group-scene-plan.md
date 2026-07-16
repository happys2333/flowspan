# ADR 0016: Explicit ordered Groups and expanded Scene plans

- Status: Accepted for task 8.1 implementation
- Date: 2026-07-16
- Decision owners: Flowspan maintainers
- Review gate: task 8.2 authorization and Replace-safety review before apply

## Context

Flowspan needs user-visible Activity Groups and reusable Scenes. A Scene that
stores opaque application state would repeat the false promise of process
migration. A Scene that expands a mutable Group only at apply time could also
change meaning without changing the saved Scene.

## Decision

- An Activity Group is a bounded, immutable, explicitly ordered collection of
  unique Activity IDs with a stable Group ID and monotonic revision.
- Groups contain Activities only; v1 has no nested Groups.
- A Scene format is versioned independently from its monotonic content revision.
- A Group-derived Scene stores the Group ID and revision but also freezes the
  exact expanded Activity ID order and a desired Placement for every item.
- Scene policy tokens map only to existing Handoff, Move, and Replace safety
  semantics. Scene apply does not become an alternate transfer protocol.
- The v1 format is a closed, bounded canonical JSON schema. It has no generic
  metadata or extension bag and no field for descriptors, payloads, adapter
  state, trust, capabilities, session material, keys, reservations, or undo
  content.
- Unknown and duplicate properties fail decoding instead of being retained.

The executable criteria and exact schema are in
`specs/v1/groups-scenes/requirements.md` and
`specs/v1/groups-scenes/design.md`.

## Alternatives considered

### Store only a Group reference and expand it during apply

Rejected because Group edits would silently change a saved Scene. The bound
Group revision remains provenance; explicit Scene items remain the plan.

### Store full Activity Descriptors in the Scene

Rejected because descriptors carry Activity payload and origin metadata. A
Scene describes desired placement and policy, not resumable content.

### Allow nested Groups

Rejected for v1 because cycle detection, recursive ordering, partial expansion,
and authorization inheritance add complexity without being required by R7.

### Invent a generic Scene action language

Rejected because it creates another orchestration primitive. Typed policies
reuse existing Handoff, Move, and Replace invariants and can be reviewed against
their existing failure semantics.

## Consequences

- Group and Scene values can be tested entirely in the platform-independent
  core.
- Saved Scene meaning is stable across later Group edits.
- Format evolution requires a new explicit version and migration decision.
- Task 8.2 must still plan and execute each Activity independently, reauthorize
  at use time, preserve Replace undo rules, and report partial completion.
- Task 8.3 must add private local persistence and inspect/delete/export behavior.

## Revisit triggers

Revisit if v1 acceptance requires nested Groups, Mirror/Remote Window Scene
policies, cross-user Scene exchange, or a signed interoperable Scene format.
