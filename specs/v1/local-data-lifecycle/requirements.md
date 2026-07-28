# Local Data Lifecycle Requirements

Status: approved product direction; task 8.3 remaining slice pending

## Problem and scope

Flowspan already lets the user inspect and revoke Trust Records and now
persists Scenes, but operation receipts are process-local and trust has no
export. Diagnostics has no R11.3 bundle or lifecycle. This slice completes
task 8.3 for trust, history, and redacted diagnostics without weakening the
existing protected-store or NOT SHARING boundaries.

The slice adds a bounded protected receipt history, redacted trust and history
exports, an on-demand diagnostic bundle, and Desktop inspect/delete/export
controls. It records only real `OperationReceipt` values; it never invents a
receipt for preflight failures or uncertain outcomes that lack one.

## Acceptance criteria

### LD1 — Protected operation history

- The history shall retain 0 through 256 receipts in recorded order. Adding
  the 257th shall atomically evict the oldest entry.
- Every entry shall have a local random entry ID, an exact `OperationReceipt`,
  and a canonical UTC recorded timestamp. Duplicate receipt writes shall be
  retained as separate audit events rather than silently merged.
- The complete history candidate shall persist before publication. Failed or
  ambiguous saves shall not publish the candidate and shall poison mutations
  until reopen; receipt persistence failure shall never change the already
  determined product-operation result.
- Users shall be able to inspect redacted receipt metadata, delete one entry,
  or clear all history. Deleting absent data shall perform no write.
- Reopen shall reject malformed, over-bound, duplicate-ID, misordered,
  unsupported-version, or non-canonical data without partially loading it.

### LD2 — Purpose-separated storage

- History shall use the authenticated atomic state-file engine with magic
  `FSOH` and a new per-purpose key on Keychain, Secret Service, or DPAPI.
- History keys, contexts, accounts, lock files, and payload paths shall be
  distinct from identity, trust, Replace, Swap, Scene apply, and Scene
  repository purposes. Cross-purpose opens shall fail authentication.
- Receipt identifiers, Device IDs, Activity IDs, descriptor digests, and
  canary content shall not appear in plaintext state-file bytes.
- Unsupported or unavailable protected storage shall visibly degrade history
  without an unprotected fallback or a Desktop startup crash.

### LD3 — Redacted trust and history export

- Trust export shall include only format and export versions, export time,
  store protection, peer count, per-peer ordinal, verification time, and
  granted Capability names.
- Trust export shall omit display names, Device IDs, fingerprints, public
  keys, session data, secrets, and exception text.
- History export shall include only format and export versions, export time,
  entry count, and each entry's ordinal, kind, status, UTC timestamps, and
  failure code.
- History export shall omit entry, operation, correlation, Activity, and
  Device IDs, Activity kind, descriptor digest, payloads, slots, names, raw
  input, private keys, secrets, and exception text.
- Exports shall use unique create-new names under the owner-only local export
  directory. Stored or peer-controlled content shall never choose a path.
- Export failure shall use fixed UI text and shall not report a partial file
  as successful.

### LD4 — Redacted diagnostic bundle lifecycle

- The bundle shall include the Flowspan and runtime versions, operating-system
  family, supported protocol versions, active negotiated protocol versions,
  Capability grants authorizing active sessions, operation-state counts, and
  recent failure codes.
- When no session is active, negotiated protocol versions shall be an empty
  list rather than inferred from supported versions or Trust Records.
- Operation state shall be derived from the current protected history and
  shall contain only kind, status, occurred-at time, and failure code.
- The bundle shall exclude content, raw input, names, paths, identifiers,
  fingerprints, public or private keys, secrets, descriptor digests, slots,
  discovery records, exception text, and environment variables.
- The Desktop shall preview the exact redacted bundle, write it as an
  owner-only create-new export, enumerate bundle files created in its fixed
  directory, and delete one selected bundle with explicit confirmation.
- Enumeration and deletion shall reject reparse points and unsafe names and
  shall not follow links outside the fixed export directory.

### LD5 — Desktop lifecycle and safety

- Existing Trust inspect, Capability edit, and two-step revoke behavior shall
  remain authoritative; trust export shall be a separate keyboard-operable
  action that grants no authority and starts no network activity.
- History shall expose refresh, selected-entry inspect, two-step delete,
  two-step clear-all, and redacted export with accessible names and fixed
  non-echoing failures.
- Diagnostics shall expose refresh/preview, export, bundle selection, and
  two-step delete with accessible names and fixed non-echoing failures.
- The panels shall preserve the global NOT SHARING indicator and shall not
  request network, capture, accessibility, or input permission.
- UI and failure text shall not display raw Activity IDs, Device IDs, Scene
  names, placement slots, or exception content.

### LD6 — Evidence

- Repository tests shall cover empty open, append, bound eviction, selected
  delete, clear, reopen, canonical round-trip, and every save fault boundary.
- Strict codec tests shall cover closed schemas, bounds, canonical IDs and UTC
  timestamps, enum drift, ordering, duplicates, and trailing data.
- Protection tests shall cover `FSOH`, tamper and cross-purpose rejection,
  plaintext canaries, purpose-separated constants, and off-OS behavior.
- Export tests shall freeze all three JSON shapes and prove every prohibited
  canary absent from bytes and filenames.
- Desktop tests shall cover unavailable degradation, receipt ingestion that
  cannot alter operation outcomes, all confirmation paths, keyboard access,
  fixed failures, and persistent NOT SHARING.
- Local gates, focused stress, Windows/macOS/Ubuntu CI, Secret Scan, CodeQL,
  and downloaded TRX totals shall pass before task 8.3 is marked complete.

## Non-goals

- Fabricating receipts for preflight-only failures, acknowledgement loss, or
  Scene outcomes that do not carry an `OperationReceipt`.
- Exporting full Trust Records, canonical receipt records, payloads, raw logs,
  crash dumps, environment variables, or arbitrary filesystem content.
- A save-file dialog, import, automatic upload, telemetry, cloud history,
  cross-user storage, or a support-service integration.
- Physical-device, packaged-app, native accessibility, power-loss, or external
  security-review evidence; these remain separate release gates.

## Traceability

These criteria refine R9.5 and R11.1–R11.4 and complete the non-Scene scope of
task 8.3 without changing the approved v1 product requirements.
