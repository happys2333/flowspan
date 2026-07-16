# Activity Groups and Scene Plan Requirements

Status: approved v1 direction; task 8.1 implementation pending

## Problem and scope

Flowspan can operate on individual Activities but has no canonical model for a
user-visible ordered Activity Group or a saved Scene. Without one, later Scene
apply code could silently reorder work, expand a changed Group ambiguously, or
persist Activity payloads and session credentials that do not belong in a
placement plan.

This slice freezes the task-8.1 domain model and version-1 local serialization.
It covers explicit Group membership, Scene placement and operation policies,
bounded canonical decoding, and structural exclusion of secrets. It does not
execute a Scene, persist a Scene repository, or add UI.

## Acceptance criteria

### GS1 — Explicit ordered Activity Groups

- When an Activity Group is created, Flowspan shall assign it one non-empty
  Group ID, one positive revision, and from 1 through 64 distinct Activity IDs.
- When Flowspan accepts Group membership, it shall preserve the caller's exact
  order in an immutable snapshot; later mutation of the caller's collection
  shall not change the Group.
- When a Group is revised, Flowspan shall preserve its Group ID, increment its
  revision exactly once without overflow, and validate the complete replacement
  membership under the same bounds.
- When membership is empty, duplicated, over the bound, or contains a null ID,
  Flowspan shall reject it before publishing a Group.
- A v1 Activity Group shall contain Activities only. It shall not contain or
  recursively expand another Group.

### GS2 — Exact versioned Scene plans

- When a Scene plan is created, Flowspan shall bind a non-empty Scene ID, a
  positive revision, format version 1, a bounded display name, and from 1
  through 64 distinct ordered Activity plan items.
- For every Activity plan item, Flowspan shall store only the Activity ID, exact
  desired device and placement slot, source-disposition policy, and
  destination-conflict policy.
- The v1 source-disposition policy shall be either Preserve Source or Move
  After Acknowledgement. The destination-conflict policy shall be either
  Require Empty or Replace With Undo. These policies reuse the existing Handoff,
  Move, and Replace safety semantics rather than creating another operation.
- When a Scene is saved from a Group, Flowspan shall bind the Group ID and Group
  revision and require the Scene items to match the Group's exact Activity ID
  order. It shall not defer membership expansion until apply time.
- When a Scene is revised, Flowspan shall preserve its Scene ID, increment its
  revision exactly once without overflow, and validate the complete replacement
  plan under the same rules.
- When a plan contains duplicate Activity IDs, an undefined policy, an invalid
  placement, an empty or oversized item list, or a mismatched Group binding,
  Flowspan shall reject it before publishing a Scene.

### GS3 — Secret-minimized representation

- A Scene format shall have no field for an Activity Descriptor, Activity
  payload, adapter state, identity/trust material, capability snapshot, session
  identifier, traffic key, reservation token, or Undo Capsule.
- When a Scene is encoded or rendered as a diagnostic string, Flowspan shall not
  include Activity content or a user-visible Scene name.
- When a decoder sees an unknown or duplicate JSON property, trailing data, an
  unsupported format version, or a value outside the frozen schema, it shall
  reject the document rather than retain unrecognized content.
- Flowspan shall not claim to recognize arbitrary secrets typed into an allowed
  user-visible name or placement slot; v1 protection is a closed, minimal typed
  schema plus documented export redaction.

### GS4 — Deterministic bounded local format

- The Scene v1 JSON codec shall emit UTF-8 with one canonical property order,
  lowercase canonical GUIDs, exact enum tokens, and preserved Activity order.
- The codec shall accept at most 32 KiB, JSON depth 8, 64 Activity items, names
  up to 120 characters, and placement slots up to the existing 80-character
  Activity Placement bound.
- A decode followed by encode shall reproduce the canonical bytes and preserve
  Scene identity, revision, Group binding, item order, placements, and policies.
- The canonical fixture and its SHA-256 digest shall be frozen in tests.

### GS5 — Evidence

- Domain tests shall cover order, defensive copying, duplicate/empty/overflow
  rejection, revision monotonicity, Group binding, and redacted string output.
- Codec tests shall cover the golden fixture, round trip, every bound, unknown
  and duplicate properties, malformed IDs/enums/versions, trailing data, and
  explicit secret-field rejection.
- The implementation and status commits shall pass formatting, warnings-as-
  errors, all tests, Windows/macOS/Ubuntu CI, Secret Scan, CodeQL, and downloaded
  TRX verification before task 8.1 is complete.

## Non-goals

- Scene preview, authorization, execution, partial-result reporting, Replace
  confirmation, or compensating undo; those belong to task 8.2.
- Scene repository persistence, inspect/delete/export UI, or diagnostics export;
  those belong to task 8.3.
- Nested Groups, live Group references whose membership changes at apply time,
  Mirror driver leases, Remote Window sessions, arbitrary application process
  state, or mobile-platform behavior.
- Detecting arbitrary secret-looking text inside a user-chosen name or slot.

## Traceability

These criteria refine v1 requirements R7.1 and R7.2 and task 8.1 without
changing the approved Flowspan product scope.
