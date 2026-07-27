# ADR 0018: Private atomic Scene repository with redacted export

- Status: Accepted for the task 8.3 Scene-repository slice
- Date: 2026-07-26
- Decision owners: Flowspan maintainers
- Review gate: filesystem protection, destructive confirmation, and export
  redaction review

## Context

Tasks 8.1 and 8.2 made Scenes deterministic and safely applicable, but no
Scene survives a restart, and the Desktop Scene panel stays inert until an
external workflow provides a `ScenePlan`. Task 8.1 recorded that Scene
definitions are local private product data — Device and Activity
identifiers, placement slots, and user-chosen names — and explicitly
deferred repository access, inspect/delete/export, filesystem protection,
and export redaction to task 8.3. The v1 design also left "Activity history
and Scene persistence" behind an explicit format decision.

## Decision

- Scene persistence is one private atomic repository: a single bounded
  encrypted state file holding 0–64 Scenes, not one file per Scene and not a
  database. Every mutation writes a complete candidate snapshot atomically
  before the in-memory state is published, and an ambiguous save fails the
  open instance closed until it is reopened from durable truth — the same
  contract as the Scene apply journal.
- Stored Scenes are the exact canonical `ScenePlanCodec` bytes frozen by
  task 8.1, embedded raw inside a strict version-1 envelope. Reopen
  revalidates every Scene through the same closed 32 KiB/depth-8 schema, and
  the repository digest is the same uppercase SHA-256 the Scene apply
  preview binds, so a repository-loaded Scene passes approval verification
  unchanged.
- Upsert is revision-monotonic per Scene ID: a strictly greater revision
  replaces, an identical revision with identical bytes is an idempotent
  no-op without a write, and anything else is rejected. Delete removes
  exactly one Scene; the empty repository is a valid persisted state.
- Protection reuses the authenticated encrypted state-file engine with a new
  `FSCR` magic and a new purpose-separated platform key (Keychain, Secret
  Service, DPAPI). There is no unprotected fallback: if the platform store
  cannot open, the Desktop feature degrades to visibly unavailable.
- The Desktop lifecycle is inspect/select/delete/export only. Selection
  hands the loaded plan to the existing
  `SceneApplyViewModel.SelectScene(...)` boundary and grants no authority.
  Delete uses the established two-step exact-identity destructive
  confirmation. Scene and Group creation UI remain open scope, so the
  repository's save operation is exercised programmatically until then.
- Export is a separate redacted projection, never the canonical bytes: it
  whitelists Scene ID, revisions, format versions, digest, timestamps,
  Group-binding IDs, item count, and per-item policies, and structurally
  omits the Scene name, Activity IDs, Device IDs, and slots. Export files
  are written create-new with owner-only modes under the local application
  data export directory; there is no file picker in v1.

## Alternatives considered

### One file per Scene in a plain directory

Rejected. Per-file storage multiplies partial-failure states (orphaned,
half-written, mixed-version files), makes the 64-Scene bound and duplicate
detection advisory, and leaves file names as an unredacted metadata channel.
One authenticated envelope keeps open/save all-or-nothing.

### Reuse an existing purpose key or state file

Rejected. Purpose separation is the standing rule (independent keys per
state kind, magic in the AEAD associated data). Sharing the Scene apply
journal's key or file would let one compromised or corrupted purpose affect
another and would break the existing cross-purpose rejection tests.

### Store a decoded DTO instead of canonical bytes

Rejected. Re-serializing Scenes through a second schema creates a second
source of truth that can drift from the frozen 8.1 codec and silently change
the digest Scene apply binds. Embedding the exact canonical bytes keeps one
schema, one digest, and byte-identical round-trips.

### Export the canonical Scene JSON

Rejected for v1. Canonical bytes contain the user-chosen name and placement
slots; R9.5 requires exported diagnostics to be redacted. A whitelist
projection with frozen fixtures is verifiable; import of external documents
stays out of scope so the unredacted format never needs to leave the
encrypted store.

### Save-file dialog for export

Rejected for v1. There is no storage-provider precedent in the codebase, a
dialog is hard to drive in headless evidence, and a fixed owner-only export
directory with create-new semantics is sufficient and honest. Revisit with
the packaging/native evidence work.

## Consequences

- Scenes survive restart with the same protection stance as Replace and
  Scene apply state, and the Scene panel's repository workflow becomes real.
- Users can inspect their own Scene data fully in the UI while anything that
  leaves the repository is redacted by construction.
- The repository adds three per-OS store pairs and one Platform wrapper that
  are near-mechanical copies of the existing pattern — more constants to
  keep distinct, which purpose-separation tests enforce.
- Scene creation UI, Group persistence, import, and trust/history/
  diagnostics lifecycle remain explicitly open; the apply panel still shows
  its truthful inert state until a Scene is selected.

## Revisit triggers

Revisit per-Scene files or a database only if the Scene bound grows beyond
64 or cross-device sync appears. Revisit the export-directory decision when
native packaging adds a reviewed file-picker convention. Revisit import only
with a dedicated hostile-document review.
