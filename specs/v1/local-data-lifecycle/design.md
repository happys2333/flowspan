# Local Data Lifecycle Design

Status: approved design for the remaining task 8.3 slice

## Design summary

The slice adds one protected receipt repository and two Desktop-facing
projections:

1. `PersistentOperationHistory` in Diagnostics owns the strict bounded
   receipt envelope behind `IOperationHistoryStatePayloadStore`.
2. `AuthenticatedOperationHistoryStateFile` and per-OS key stores reuse the
   authenticated atomic state engine with purpose `FSOH`.
3. `LocalDataExport` emits frozen trust, history, and diagnostics JSON from
   explicit whitelist DTOs.
4. `DesktopLocalDataRuntime` is both the production `IReceiptSink` and the
   serialized Desktop lifecycle service. Sink persistence failures are
   isolated from product-operation results and surfaced as degraded history.
5. `LocalDataViewModel` presents redacted history and diagnostics. Existing
   `TrustedDevicesViewModel` gains a redacted trust-export action.

No protocol message, Capability, network behavior, telemetry, database, or
NuGet package is added.

## Protected history model

```csharp
public sealed record OperationHistoryEntry(
    Guid EntryId,
    long Sequence,
    DateTimeOffset RecordedAt,
    OperationReceipt Receipt);

public interface IOperationHistoryStatePayloadStore
{
    ValueTask<byte[]?> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}
```

`PersistentOperationHistory` retains 256 entries and caps the canonical
payload at 1 MiB. `AppendAsync` assigns a cryptographically random entry ID
and the next positive sequence, uses the receipt's canonical UTC occurrence
time as `RecordedAt`, evicts the lowest sequence when full, persists the
complete candidate, then publishes it.

Delete and clear use the same complete-candidate path. Missing selected IDs
and clearing an empty repository are no-write no-ops. Any thrown save becomes
`OperationHistoryPersistenceException`, leaves the published snapshot
unchanged, and poisons further mutations until reopen. Reads remain available.

The format is canonical closed JSON:

```json
{"formatVersion":1,"nextSequence":2,"entries":[
  {"entryId":"<guid>","sequence":1,"recordedAt":"<UTC O>","receipt":{
    "operationId":"<guid>","correlationId":"<guid>","kind":"handoff",
    "status":"committed","sourceDeviceId":"<guid>",
    "targetDeviceId":"<guid>","activityId":"<guid>",
    "activityKind":"workspace.note/v1","descriptorDigest":"<HEX64>",
    "occurredAt":"<UTC O>","failureCode":"none"}}]}
```

Entries are ordered by strictly increasing sequence; `nextSequence` is greater
than every retained sequence. Exact enum strings and canonical lower-case
GUIDs are required. `activityKind` and
`descriptorDigest` are both null only for `ActivityNotFound`. Decode rebuilds
each receipt through `OperationReceipt.FromRecordedResult`, rejects unknown or
duplicate properties, duplicate entry IDs, non-canonical order, non-UTC time,
over-bounds, trailing data, and version drift. Loaded and encoded plaintext
buffers are zeroed after use.

## Platform protection

`AuthenticatedOperationHistoryStateFile` delegates to
`AuthenticatedReplaceStateFile` with magic `FSOH`, the 1 MiB bound, and state
name "operation history". `IOperationHistoryStateKeyStore` is a new marker.

| OS | Key custody | State path |
| --- | --- | --- |
| macOS | Keychain service `app.flowspan.operation-history-state-key`, account `primary-operation-history-state-key` | `.../Flowspan/Security/operation-history-state.fsoh` |
| Linux | secret-tool kind/account `operation-history-state-key`, dedicated lock | same file name |
| Windows | DPAPI context `Flowspan.OperationHistoryStateKey.DPAPI.v1`, dedicated key file | same file name |

There is no unprotected fallback. Desktop composition uses an unsupported
adapter on other platforms and visibly degrades the lifecycle.

## Receipt ingestion and failure isolation

`DesktopLocalDataRuntime` implements `IReceiptSink`. `Write` serializes through
the runtime gate and synchronously completes the protected append because the
existing sink contract is synchronous. All underlying awaits use
`ConfigureAwait(false)`.

The sink catches every non-fatal persistence/open exception, marks history as
reopen-required, and returns normally. An audit-store failure therefore cannot
turn an already committed operation into a reported product failure. The next
explicit refresh reopens durable truth; failed candidates are never retried or
reported as persisted. A fixed degraded status tells the user that receipt
retention failed.

Production `DesktopActivityRuntime` receives this sink instead of
`NullReceiptSink` for its `FlowspanNode` and `ReplaceEndpoint`. Existing call
sites therefore record source, target, Replace, Move, Handoff, and Scene child
receipts that actually exist. Preflight-only failures and uncertain results
without an `OperationReceipt` remain unrecorded rather than fabricated.

## Redacted projections

`LocalDataExport` writes UTF-8 canonical JSON with an explicit
`formatVersion: 1` and one of these kinds:

- `flowspan.trust-export.redacted/v1`: protection label, peer count, and each
  peer's ordinal, UTC verification time, and sorted Capability names.
- `flowspan.history-export.redacted/v1`: entry count and each entry's ordinal,
  operation kind/status, recorded/occurred UTC times, and failure code.
- `flowspan.diagnostics.redacted/v1`: application/runtime/OS versions,
  supported and active negotiated protocol versions, sorted granted
  Capabilities, aggregate operation-state counts, and recent failure codes.

Every projection is a whitelist. No serializer ever receives a Trust Record,
raw receipt, Device/Activity identity, descriptor, exception, filesystem path,
environment block, or peer discovery record.

Diagnostic versions use the Desktop assembly informational version, the .NET
runtime description, a normalized OS family (`windows`, `macos`, `linux`, or
`unsupported`), and `ProtocolFeatures.ProductionSupportedVersions`. Active
negotiated versions come only from authenticated connection snapshots and are
empty while no authenticated session exists. Active authorized Capabilities
are the distinct union of current Trust grants for authenticated peers and are
labelled as authorization, not negotiation.

Operation-state counts group the current protected history by exact kind and
status. Recent errors are the newest 32 entries whose failure code is not
`None`, projected as occurred time, kind, status, and failure code. This is
enough for R11.3 troubleshooting without identifiers or content.

Files use fixed prefixes plus UTC time and a random 32-hex suffix:

- `trust-export-<time>-<unique>.json`
- `history-export-<time>-<unique>.json`
- `diagnostics-<time>-<unique>.json`

`RedactedExportFile.WriteAsync` remains the create-new owner-only writer.
New list/delete helpers accept only the fixed diagnostics prefix and strict
ASCII filename allowlist, reject the directory and target if either is a
reparse point, return names rather than arbitrary paths, and delete no other
export kind.

## Desktop lifecycle

`IDesktopLocalDataService` exposes initialize, history snapshot, selected/all
history deletion, trust/history/diagnostics export, diagnostics preview,
diagnostics-file list, and selected diagnostics-file deletion. The concrete
runtime owns the persistent repository, export directory, Trust authority,
and active-connection snapshot delegate behind one gate.

`LocalDataViewModel` follows the existing Scene repository conventions:
fixed uppercase statuses, no exception binding, `AsyncRelayCommand`, lifetime
cancellation, selected history and diagnostics items, two-step destructive
confirmation, and exact redacted export preview/path. History inspect shows
only kind, status, times, and failure code; raw IDs never enter its properties.

`TrustedDevicesViewModel` keeps the existing inspect/edit/revoke panel and adds
one export command. It requests the service's current authoritative Trust
snapshot at activation time, so stale list state cannot choose export content.

### Interface design specification

- Purpose: let a local user audit and remove private lifecycle data without
  implying sharing, uploading, or diagnostic completeness.
- Aesthetic direction: existing Flowspan industrial/utilitarian shell.
- Palette: reuse Graphite `#111416`, Steel `#20262A`, cool-gray borders,
  Safety Amber, and Signal Red resources; no new palette tokens.
- Typography: reuse the approved Avalonia shell typography and Cascadia Mono /
  Menlo / DejaVu Sans Mono identifier stack; no new font dependency.
- Layout: add a full-width asymmetric 7:5 lifecycle row immediately below the
  workspace overview and before network/action regions, with history dominant
  and diagnostics secondary. This keeps private local-data controls prominent
  without redesigning the approved hierarchy.

All new controls have explicit automation names, descriptive help text for
destructive/export actions, minimum 44-pixel hit targets, keyboard activation,
textual state independent of color, and no motion. The global NOT SHARING
banner remains unchanged and headless tests assert it.

## Verification matrix

- history empty/append/evict/delete/clear/reopen and canonical exact round-trip;
- save-before-write and save-after-write failures for append/delete/clear,
  candidate non-publication, poisoning, and reopen recovery;
- strict envelope and receipt validation, bounds, ordering, enum/version drift;
- `FSOH`, tamper/cross-purpose rejection, canary ciphertext absence, per-OS
  purpose constants, cancellation, and off-OS unsupported behavior;
- frozen trust/history/diagnostics fixtures and prohibited-field canaries;
- safe diagnostics list/delete, collision, symlink/reparse, and prefix tests;
- Desktop unavailable, sink-failure isolation, all lifecycle commands,
  accessibility names, keyboard dispatch, fixed failures, and NOT SHARING;
- exact-commit local gates, stress, hosted matrix, scans, and TRX verification.

## Delivery limits

Hosted and same-host evidence is not physical-device, native credential-store,
packaged-app, native accessibility, power-loss, or external review evidence.
Those and the broader v1 release criteria remain open.
