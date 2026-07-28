# ADR 0019: Protected local audit with whitelist diagnostics

- Status: Accepted for the remaining task 8.3 slice
- Date: 2026-07-28
- Decision owners: Flowspan maintainers
- Review gate: persistence failure isolation, export redaction, destructive
  lifecycle, and diagnostics-source review

## Context

Operation receipts exist as structured values but Desktop production discards
them through `NullReceiptSink`. Trust can be inspected and revoked but not
exported. Diagnostics can serialize one receipt, yet R11.3 requires versions,
negotiated capabilities, operation state, and recent error codes, while R9.5
forbids content, secrets, raw input, and private keys. R11.4 also requires
inspect and delete controls for trust, history, and diagnostic data.

These surfaces contain identifiers and security state. Plain logs, arbitrary
object serialization, or an unprotected history file would create a new data
leak. Audit persistence must also be secondary to product truth: if writing a
receipt fails after an operation commits, the operation must not be reported
as failed.

## Decision

- Keep a bounded local history of the latest 256 exact `OperationReceipt`
  events in one authenticated atomic state file. Use a strict versioned JSON
  envelope, complete-candidate save-before-publish semantics, fail-closed
  poisoning until reopen, and a new `FSOH` magic and purpose-separated
  Keychain/Secret Service/DPAPI key.
- The production history runtime implements the existing synchronous
  `IReceiptSink`. It waits for protected persistence but absorbs audit-store
  failure after recording a degraded state, so a receipt failure never changes
  the already determined operation outcome.
- Record only real receipts emitted by existing operation boundaries. Do not
  synthesize audit facts for preflight or uncertain paths that have no receipt.
- Export trust, history, and diagnostics through three closed whitelist
  projections. Trust omits peer identity. History omits every raw identifier
  and descriptor field. Diagnostics includes versions, actual active negotiated
  protocol versions, Capability grants authorizing authenticated sessions,
  aggregate operation state, and recent error codes, but no identifiers,
  exception text, environment data, content, raw input, secrets, or keys.
- Keep exported diagnostics as explicit owner-only create-new files in the
  existing fixed export directory. The Desktop can list and delete only files
  matching the generated diagnostics prefix and strict filename grammar; it
  never follows reparse points or accepts an arbitrary path.
- Reuse the existing industrial/utilitarian Desktop hierarchy. Add keyboard
  and accessibility-complete lifecycle controls without changing NOT SHARING,
  starting network activity, or requesting a privileged capability.

## Alternatives considered

### Plain JSON log or rolling text file

Rejected. A log invites accidental payload and exception leakage, has weak
schema/version boundaries, and cannot reuse the authenticated complete-state
failure model. The 256-entry encrypted envelope is bounded and testable.

### Throw when receipt persistence fails

Rejected. Receipt sinks are called after effects can be committed. Propagating
an audit failure would convert a successful operation into a false failure and
could trigger unsafe retry. The UI instead exposes degraded retention.

### Background best-effort queue

Rejected for v1. It shortens operation latency but introduces shutdown races,
silent crash loss, queue backpressure policy, and ambiguous inspect/delete
ordering. Synchronous bounded persistence is simpler and honest at this scale.

### Serialize Trust Records and receipts directly

Rejected. Direct serialization exports stable peer, Activity, and descriptor
identifiers and makes future model fields leak by default. Explicit whitelist
DTOs make additions opt-in and fixture-testable.

### Include configured versions as negotiated versions

Rejected. Supported versions and Trust grants are not session negotiation.
The bundle reports active negotiated versions only from authenticated session
snapshots and emits an empty list when no such session exists.

### Automatic diagnostic upload

Rejected. v1 has no account or support service, and automatic transfer would
conflict with Flowspan's local-first and explicit-network boundaries.

## Consequences

- Product operations gain restart-persistent local audit without allowing
  audit I/O to rewrite product truth.
- Users can inspect and remove history, export a minimized Trust inventory,
  and inspect/export/delete diagnostic bundles without exposing raw identity
  or content data.
- The solution adds another purpose-separated platform store and therefore
  more constants and platform contracts; cross-purpose tests enforce the
  separation.
- Receipt coverage remains exactly the set of real receipt-producing paths.
  Closing any broader R11.1 gaps requires operation-specific receipt changes,
  not fabricated history entries.
- Export files are intentionally plaintext because they are user-requested
  redacted artifacts. Owner-only modes and explicit delete reduce exposure but
  do not make them equivalent to the encrypted history store.

## Revisit triggers

Revisit the 256-entry envelope if measured usage requires longer retention.
Revisit synchronous sink persistence only with a durable queue design that
preserves shutdown, ordering, backpressure, and failure truth. Revisit file
selection and support upload only during packaging with explicit consent and
an independent privacy review.
