# Scene Apply Requirements

Status: approved product direction; task 8.2 implementation pending

## Problem and scope

Task 8.1 made a saved Scene deterministic, but a definition is not authority to
move or replace current work. Between save, preview, confirmation, and use,
Activities, Trust, Capabilities, destination occupancy, and undo availability
can all change. Applying a Scene must therefore be an explicitly previewed,
best-effort orchestration of existing Activity operations, never a claim of
atomic whole-Scene migration.

This slice covers current-state planning, destructive confirmation, ordered
execution, durable partial-result reduction, and safe Replace compensation. It
does not add Scene repository persistence or inspect/delete/export behavior.

## Acceptance criteria

### SA1 — Exact current-state preview

- When a user previews a Scene, Flowspan shall bind the preview to the exact
  Scene ID, revision, canonical Scene digest, creation time, expiry, parent
  Operation ID, and one distinct child Operation/correlation pair per item.
- Preview and result item collections shall be immutable defensive copies with
  exactly 1 through 64 ordered entries. Auxiliary source-candidate and Replace-
  confirmation collections shall be immutable, unique, and bounded from 0
  through 64; an over-bound or truncated source result is a blocker, never a
  partial choice set. Duplicate indices/identities, malformed Unicode,
  undefined enums, nonpositive revisions, noncanonical digests, invalid time
  ranges, or over-bound evidence shall fail before an object is published.
- While preparing a preview, Flowspan shall inspect every explicit Scene item
  in saved order without mutating an Activity or acquiring mutation authority.
- Because a Scene does not save a source Device, Flowspan shall locate sources
  only with a purpose-scoped exact-Activity-ID query. Zero active eligible
  sources shall block the item. More than one shall require the user to select
  one exact source and regenerate the complete preview; Flowspan shall not pick
  by discovery order, revision, title, Device ID, or any other heuristic.
- For each item, Flowspan shall report one resolved action—No Change, Handoff,
  Move, or Replace—or one precise blocker. When the user-selected exact source
  already occupies the requested destination Device and placement slot, the
  action shall be No Change and apply shall not call an operation or Adapter.
- Destination emptiness shall be decided only by a purpose-scoped exact-slot
  occupancy query, not by the filtered Replace inventory. The query shall
  distinguish Empty, one exact eligible conflict, opaque protected/ineligible
  occupancy, and Ambiguous without disclosing hidden Activity metadata.
- Replace shall be resolved only when exact-slot occupancy returns one eligible
  conflicting Activity, the Scene policy permits Replace With Undo, and source
  disposition is Preserve Source. When Move After Acknowledgement meets an
  occupied target, v1 shall block before mutation: closing the source after
  Replace would make target-only undo capable of deleting the last incoming
  instance, while leaving it open would violate the requested Move.
- When Require Empty observes a conflict, when destination occupancy is
  opaque/ineligible or ambiguous, or when Replace preservation/undo is
  unavailable, Flowspan shall block that item rather than treating filtered
  inventory as empty, choosing a target, or overwriting work.
- When a Group-derived Scene's current Group revision is known and differs from
  the bound revision, Flowspan shall show a stale-Group warning but shall keep
  the saved explicit Scene items as the only execution order.

### SA2 — Approval is exact and pre-mutation

- Before the first mutation, Flowspan shall reject an expired preview, a Scene
  ID/revision/digest mismatch, a changed preview fingerprint, or an approval
  that does not explicitly confirm every exact Replace target.
- A preview shall expire no later than five minutes after creation. Expiry gates
  creation of a new apply attempt; once exact approval and durable intent were
  accepted in time, retry/recovery shall use that same attempt rather than
  silently creating a fresh authorization window.
- Preview and Replace-confirmation fingerprints shall use one versioned,
  domain-separated, canonical length-prefixed UTF-8 encoding and SHA-256. The
  approval shall contain the unique Replace fingerprints in strict Scene index
  order, exactly equal to the preview's Replace items—no missing, extra,
  duplicate, reordered, or syntactically noncanonical value is accepted.
- A Replace confirmation shall bind the item index, incoming Activity ID,
  target Device/Activity ID, target revision, descriptor digest, kind, and
  placement slot shown in the preview.
- Every executable item shall bind the exact user-selected source Device,
  Activity ID, revision, descriptor digest, kind, and placement slot. A source
  selection or source snapshot change shall require a new complete preview.
- The preview shall clearly distinguish Preserve Source from Move After
  Acknowledgement and identify each target Activity that would be preserved in
  an Undo Capsule. One generic "apply" acknowledgement shall not authorize an
  unlisted or changed replacement.
- Preview and approval objects shall contain no Activity payload, session key,
  token, reservation, or Undo Capsule content.
- Scene source/slot observations and remote child execution shall require an
  explicitly negotiated Scene-capable protocol version. An older peer shall
  block that item as unsupported before receiving an unknown control message.

### SA3 — Deterministic best-effort execution

- When an approved Scene is applied, Flowspan shall process items sequentially
  in the exact saved Scene order and shall reuse the child Operation IDs frozen
  by the preview on every retry.
- Immediately before each item, Flowspan shall recheck current source identity,
  revision/digest, lifecycle, Trust, required Capability, target connection,
  destination occupancy, and exact Replace target/undo availability.
- `scene.apply` shall be an additional orchestration Capability on every
  participating peer, not a replacement for child-operation authorization.
  Each child shall also perform the existing Handoff/Move `activity.receive`
  and `activity.offer`, or Replace `activity.receive` and `activity.replace`,
  checks for its authenticated participants immediately before protected state
  or Adapter use.
- When the selected source is remote from the coordinator, the coordinator
  shall send only a strict payload-free child instruction bound to coordinator,
  source, target, parent attempt, child Operation/correlation IDs, preview
  fingerprint, exact source/target evidence, action, and deadline. The source
  peer shall revalidate and invoke the existing operation locally so Activity
  content travels only on the existing end-to-end source-to-target path.
- A duplicate remote child instruction shall return its exact durable child
  outcome without repeating Adapter work. Delivery/acknowledgement ambiguity
  shall reduce to Recovering and stop later Scene items.
- A preview-blocked or newly rejected item shall produce a per-Activity outcome
  without calling an Adapter. A terminal Rejected, Failed, or
  Committed-With-Warning item shall not prevent later independent items.
- When an item becomes Recovering, acknowledgement is lost, a durable result is
  ambiguous, or an unexpected operation exception cannot prove non-delivery,
  Flowspan shall stop before the next item and mark every remainder as not
  attempted after an uncertain boundary.
- Cancellation before the first item shall mutate nothing. Cancellation between
  items shall retain recorded results and mark the remainder not attempted.
  Cancellation during a committed send shall never be relabelled as a clean
  cancellation without durable outcome evidence.

### SA4 — Durable partial results and replay

- Before executing a child operation, Flowspan shall durably record the exact
  Scene attempt binding and child identities. After each item, it shall durably
  record the outcome before starting the next item.
- When the same Scene attempt is retried, Flowspan shall return recorded
  terminal item outcomes without a second Adapter mutation and shall resume
  only at an unambiguous next item.
- When restart finds an in-flight item without a terminal durable outcome,
  Flowspan shall surface Recovering and shall not execute it or any later item
  until reconciliation proves an outcome.
- The bounded apply journal shall contain only identifiers, revisions, digests,
  action/status/reason codes, timestamps, and payload-free undo references. It
  shall not contain Activity titles, descriptor payloads, exception text, Trust
  records, session material, or Undo Capsule content.

### SA5 — Per-Activity and overall truth

- An apply result shall preserve exact item order and report, for every item,
  requested and resolved action, child Operation/correlation IDs when assigned,
  outcome, reason, timestamp, and a payload-free Undo Capsule reference only
  when a Replace actually committed with recoverable target state.
- Overall status shall be derived from item evidence as Completed,
  Completed-With-Warnings, Partially-Completed, Blocked, Recovering, or
  Cancelled; it shall never report atomic success for a partially applied Scene.
- Completed shall require every item to be satisfied by No Change or Committed
  with no warning. Completed-With-Warnings shall require every item satisfied
  with at least one committed warning. Recovering shall dominate whenever an
  uncertain item exists. Partially-Completed shall require at least one
  satisfied item and at least one blocked/rejected/failed/cancelled remainder.
  Cancelled shall mean cancellation before any item was satisfied; Blocked shall
  mean no item was satisfied and all known outcomes are terminal blockers,
  rejections, or failures.
- Diagnostic rendering shall omit Scene names, Activity titles, placement
  slots, descriptor payloads, and exception messages.

### SA6 — Safe compensation

- Flowspan shall never automatically reverse a Handoff or synthesize a reverse
  Move after partial completion.
- When the user requests compensation, Flowspan may attempt only committed
  Preserve-Source Replace items that returned an unexpired exact Undo Capsule
  reference. It shall call the existing target-owned Replace undo path in
  reverse Scene order.
- Compensation shall recheck exact current replacement state and shall report
  each undo result independently. One failed, stale, expired, consumed, or
  Recovering undo shall not be represented as whole-Scene rollback.

### SA7 — Evidence

- Model/property tests shall cover saved ordering, every mixed outcome,
  retry/replay, cancellation boundaries, stale preview/approval, and the
  Recovering halt invariant.
- Model tests shall freeze canonical fingerprint vectors and cover 1/64/65
  bounds, defensive copying, valid non-BMP Unicode, malformed surrogates,
  undefined enums, invalid IDs/revisions/digests/times, and preview/approval/
  result diagnostic redaction.
- Source-location tests shall cover zero, one, and multiple active placements,
  explicit selection/repreview, exact-destination No Change, and prove that no
  source-order/revision/title/Device-ID heuristic can select automatically.
- Exact-slot tests shall prove Empty separately from an eligible conflict,
  opaque protected/ineligible occupancy, and Ambiguous occupancy, including
  proof that filtered Replace inventory cannot authorize an empty-slot result.
- Policy-matrix tests shall prove Empty maps Preserve Source to Handoff and Move
  After Acknowledgement to Move; an eligible conflict maps only Preserve Source
  plus Replace With Undo to Replace; occupied Move-plus-Replace blocks before
  Adapter work and cannot enter compensation.
- Integration tests shall prove Handoff, Move, and exact Replace/Undo routing
  through existing production operation boundaries with current authorization
  rechecks, independent `scene.apply` denial, operation-specific denial, and
  zero duplicate Adapter mutations.
- Protocol tests shall freeze the Scene feature's minimum version and strict
  source lookup, slot inspection, child execution/result messages; older
  negotiated versions, wrong participants/bindings, payload-like fields,
  duplicate delivery, disconnect, timeout, and lost acknowledgement shall fail
  closed.
- Fault injection shall cover every apply-journal save and operation-call
  boundary. Security tests shall prove destructive confirmation binding and
  preview/result/journal redaction.
- Task 8.2 is complete only after local stress, Windows/macOS/Ubuntu CI, Secret
  Scan, CodeQL, downloaded TRX verification, and explicit evidence limits pass
  at the implementation and task-status commits.

## Non-goals

- Atomic all-or-nothing Scene semantics.
- Automatic rollback of Handoff or Move.
- Live Group expansion, nested Groups, Mirror/driver actions, or Remote Window
  actions inside Scene format v1.
- Scene repository persistence, browsing, editing, delete, export, or
  diagnostic export; those remain task 8.3.
- Arbitrary application process-memory migration or physical-device claims from
  same-host/hosted tests.

## Traceability

These criteria refine v1 requirements R7.3–R7.4 and R11.2 and task 8.2 without
changing the approved product scope.
