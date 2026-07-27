# Scene Repository Requirements

Status: approved product direction; task 8.3 Scene-repository slice pending

## Problem and scope

Tasks 8.1 and 8.2 made a saved Scene deterministic and safely applicable, but
no Scene survives a process restart: nothing persists a `ScenePlan`, and the
Desktop Scene panel stays inert until an external workflow provides one. Task
8.1 also recorded that Scene definitions are local private product data —
they reveal Device and Activity identifiers, desired placement slots, and
user-chosen names — so persistence must define repository access, filesystem
protection, inspect/delete lifecycle, and export redaction before the release
criterion can close.

This slice covers the private atomic Scene repository, its bounded canonical
at-rest format, the Desktop inspect/select/delete/export lifecycle, and the
redacted Scene export document. It integrates with Scene apply exclusively
through `SceneApplyViewModel.SelectScene(...)`. It does not add Scene or Group
creation UI, Group persistence, trust/history/diagnostics export, or import of
external Scene documents.

## Acceptance criteria

### SR1 — Private atomic Scene repository

- The repository shall store from 0 through 64 Scene plans, each individually
  bounded by the existing canonical Scene codec limits (32 KiB, 64 items,
  format version 1). Saving beyond the Scene bound shall be rejected before
  any durable mutation.
- Every stored Scene shall be persisted as its exact canonical Scene codec
  bytes; a load-then-save cycle shall be byte-identical and preserve Scene
  identity, revision, Group binding, item order, placements, and policies.
  The stored digest identity of a Scene shall be the uppercase SHA-256 of its
  canonical bytes, equal to the digest bound by Scene apply previews.
- Each stored entry shall bind exactly one UTC save timestamp. Non-UTC
  timestamps shall be rejected on save and on reopen.
- Saving a Scene whose ID is not stored shall insert it. Saving a Scene whose
  ID is stored shall require a strictly greater revision; an identical
  revision with identical canonical bytes shall be an idempotent no-op that
  performs no durable write, and any other same-or-lower revision shall be
  rejected without mutating durable state.
- Deleting a stored Scene shall remove exactly that Scene; deleting an absent
  Scene shall report not-found without a durable write. An empty repository
  shall be a valid persisted state — deleting the last Scene shall not be
  special-cased.
- Every mutation shall persist a complete candidate snapshot atomically before
  the in-memory state is published. A failed or ambiguous save shall not
  publish the candidate and shall fail further mutations closed until the
  repository is reopened from durable state.
- Reopen shall fail closed: corrupt, truncated, tampered, unsupported-version,
  over-bound, duplicate-Scene-ID, non-canonically-ordered, or structurally
  conflicting durable content shall be rejected rather than partially loaded,
  and the rejection shall not echo stored content.

### SR2 — Filesystem protection

- The repository state file shall use the existing authenticated encrypted
  state-file engine with a new unique four-byte magic and a new
  purpose-separated platform key: macOS Keychain, Linux Secret Service, and
  Windows DPAPI keys and state paths shall be distinct from every existing
  purpose, and a repository state file shall not be openable as any other
  purpose (nor any other purpose's file as the repository).
- State files and their temporary/lock siblings shall be owner-only on POSIX
  (0700 directory, 0600 files), shall reject reparse-point targets, and shall
  be replaced only by the engine's temp-write-then-atomic-rename sequence.
- Scene names, placement slots, Activity IDs, and Device IDs shall not appear
  in plaintext anywhere in the repository state file bytes.
- On an unsupported platform or when the platform store fails to open, the
  Desktop shall degrade the Scene repository feature to visibly unavailable
  without crashing startup and without falling back to unprotected storage.

### SR3 — Inspect, select, and delete lifecycle

- The Desktop shall list stored Scenes with name, Scene ID, revision, item
  count, Group binding when present, canonical digest, and save time, and
  shall let the user inspect a selected Scene's ordered items — Activity ID,
  destination Device and slot, source disposition, and conflict policy. This
  inspection shows the user their own local Scene data; it is not a diagnostic
  surface.
- Selecting a stored Scene for apply shall call the existing
  `SceneApplyViewModel.SelectScene(...)` boundary with the loaded `ScenePlan`
  and no observed Group revision (no current-Group source exists in v1), and
  shall not itself preview, authorize, or execute anything.
- Delete shall be a two-step explicit confirmation in the established
  destructive pattern: a review step naming the exact Scene (name, ID,
  revision) and stating that the action has no undo, then a separately
  focusable confirm control. Deleting a Scene shall not touch the Scene apply
  journal, Replace undo state, or any applied Activity.
- If the deleted Scene is the one currently selected in the Scene apply panel,
  the selection shall not be silently re-pointed; the apply panel keeps its
  already-loaded immutable plan and its existing preview/expiry rules.
- List, inspect, delete, and export shall be keyboard operable with accessible
  names, follow the existing focus and 44-pixel control conventions, and never
  change the global sharing indicator.
- Repository failures shall surface fixed truthful status strings; exception
  text, stored names, and slots shall not enter status or description fields
  reserved for fixed strings.

### SR4 — Redacted Scene export

- Exporting a Scene shall produce a structured redacted JSON document
  containing only: an export kind and format version, export time, Scene ID,
  revision, Scene format version, canonical digest, save time, Group binding
  IDs/revision when present, item count, and per-item index plus source
  disposition and conflict policy.
- The export shall not contain the Scene name, any Activity ID, any Device
  ID, any placement slot, any Activity payload or descriptor field, or any
  exception text. This redaction shall hold even though the on-screen inspect
  view legitimately shows the user their own names and slots.
- The export file shall be written under the local application data export
  directory with a create-new unique file name (never overwriting), owner-only
  POSIX file mode, and reparse-point rejection; the UI shall show the exact
  resulting path and the redacted content, and shall never write to a path
  derived from stored Scene content.
- A failed export shall report a fixed failure status without a partial file
  being reported as success.

### SR5 — Evidence

- Repository tests shall cover empty open, insert, revise, idempotent re-save,
  stale-revision rejection, delete, delete-to-empty, reopen round-trip,
  0/64/65 Scene bounds, and byte-identical canonical persistence.
- Fault-injection tests shall cover every save boundary with fail-before-write
  and fail-after-write (ambiguous) stores and shall prove candidate
  non-publication plus fail-closed poisoning until reopen.
- Strict-format tests shall reject unknown/duplicate/missing envelope fields,
  malformed or non-canonical nested Scene documents, duplicate or misordered
  Scene IDs, non-UTC timestamps, over-bound payloads, and version drift.
- Protection tests shall prove the new magic, cross-purpose open rejection in
  both directions, tamper rejection, plaintext canary absence (Scene name,
  slot, Activity ID, Device ID) from state-file bytes, and per-OS purpose
  separation of key and path constants, with off-OS behavior asserted as
  unsupported.
- Export tests shall freeze the redacted export shape and prove name, slot,
  Activity-ID, and Device-ID canary absence from exported bytes, plus
  create-new collision behavior and failure reporting.
- Desktop tests shall cover the full keyboard lifecycle (list, inspect,
  select-for-apply reaching `SelectScene`, two-step delete, export),
  accessible names, unavailable degradation, fixed redacted failure strings,
  and the persistent NOT SHARING indicator.
- The implementation and task-status commits shall pass formatting,
  warnings-as-errors, all tests, Windows/macOS/Ubuntu CI, Secret Scan,
  CodeQL, and downloaded TRX verification before this slice of task 8.3 is
  complete.

## Non-goals

- Scene or Group creation/editing UI, and any workflow that saves a Scene from
  current Activities; the repository's save operation is exercised through its
  programmatic boundary until that UI exists.
- Group persistence or a current-Group revision source; stale-Group warnings
  at select time therefore remain inactive in v1.
- Import of Scene documents produced outside this repository.
- Trust, history, and diagnostics inspect/delete/export (the remaining scope
  of task 8.3), the diagnostics bundle of R11.3, packaged distribution,
  physical-device evidence, and native assistive-technology evidence.
- Multi-user or roaming repository storage; the repository is per-OS-user
  local state.

## Traceability

These criteria refine v1 requirements R9.5 and R11.4 and the Scene-repository
portion of task 8.3 without changing the approved product scope. Trust
inspect/delete already exists under task 6.x; history and diagnostics
lifecycle remain open under task 8.3.
