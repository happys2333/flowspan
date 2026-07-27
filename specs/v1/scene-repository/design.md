# Scene Repository Design

Status: approved design for the task 8.3 Scene-repository slice

## Design summary

The Scene repository is a fourth instance of the existing protected durable
state pattern, plus a thin Desktop lifecycle:

1. `PersistentSceneRepository` (Application) owns bounded upsert/delete
   semantics and whole-state complete-candidate persistence behind a new
   `ISceneRepositoryStatePayloadStore` port.
2. `AuthenticatedSceneRepositoryStateFile` (Platform) wraps the existing
   AES-256-GCM authenticated atomic state-file engine with the new magic
   `FSCR` and a new `ISceneRepositoryStateKeyStore` purpose key.
3. Per-OS key/payload stores (Keychain, Secret Service, DPAPI) with
   repository-specific service/account/context/path constants.
4. `DesktopSceneRepositoryRuntime` + `SceneRepositoryViewModel` present the
   inspect/select/delete/export lifecycle and hand a selected `ScenePlan` to
   the existing `SceneApplyViewModel.SelectScene(...)` boundary.

No new NuGet package, protocol message, Capability, or Adapter is added. The
repository never talks to the network, the Scene apply journal, or Replace
state.

## At-rest format

The payload store persists one bounded canonical JSON envelope:

```json
{"formatVersion":1,"scenes":[{"savedAt":"<UTC O>","scene":{...}}]}
```

- `scenes` holds 0 through `PersistentSceneRepository.MaximumSceneCount` (64)
  entries ordered by ascending ordinal canonical Scene ID; duplicates are
  rejected.
- `scene` is the exact canonical `ScenePlanCodec` document written with
  `WriteRawValue`; decode extracts the nested raw text and revalidates it
  through `ScenePlanCodec.Decode`, so the frozen task-8.1 strict schema —
  32 KiB bound, depth 8, closed properties, canonical GUIDs, malformed-Unicode
  rejection — applies unchanged to every stored Scene, and re-encode is
  byte-identical.
- `savedAt` must round-trip as exact UTC `O` format (`RequireUtc`).
- The envelope decoder enforces closed property sets (`RequireOnly`
  convention), document depth 12, and the whole-payload bound
  `MaximumPayloadBytes` (4 MiB — 64 × 32 KiB plus envelope overhead) before
  parsing; every malformed condition becomes a generic
  `InvalidDataException` that never echoes stored content.
- The Scene digest is not stored: it is recomputed as the uppercase SHA-256
  hex of the nested canonical bytes on open and on save, and therefore always
  equals the digest Scene apply binds into previews.

Envelope version 1 is frozen by fixture tests. An unknown `formatVersion`
fails closed.

## Application layer

```csharp
public interface ISceneRepositoryStatePayloadStore
{
    ValueTask<byte[]?> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}

public sealed record SceneRepositoryEntry
{
    public ScenePlan Scene { get; }
    public DateTimeOffset SavedAt { get; }
    public string SceneDigest { get; }   // uppercase 64-hex SHA-256 of canonical bytes
}

public sealed class PersistentSceneRepository : IDisposable
{
    public const int MaximumSceneCount = 64;
    public const int MaximumPayloadBytes = 4 * 1024 * 1024;

    public static ValueTask<PersistentSceneRepository> OpenAsync(
        ISceneRepositoryStatePayloadStore payloadStore,
        CancellationToken cancellationToken = default);

    public int SceneCount { get; }
    public ImmutableArray<SceneRepositoryEntry> Snapshot();
    public ValueTask<SceneRepositoryEntry> SaveAsync(ScenePlan scene, DateTimeOffset savedAt, CancellationToken cancellationToken = default);
    public ValueTask<bool> DeleteAsync(SceneId sceneId, CancellationToken cancellationToken = default);
}
```

Semantics mirror `PersistentSceneApplyJournal`:

- `OpenAsync` loads and strictly decodes the whole state (null payload means
  empty), zeroing loaded buffers after decode. Every plaintext byte buffer the
  codec derives is zeroed too: the owned copy handed to `JsonDocument`, each
  per-entry canonical Scene buffer on both encode and decode, the digest input,
  and the `ArrayBufferWriter` the envelope is written through.
- Mutations copy the snapshot, apply the change, encode the complete
  candidate, save, and only then publish, under one gate.
- A thrown save wraps as `SceneRepositoryPersistenceException`
  (`IOException`) and poisons the open instance: every later mutation fails
  with a fixed reopen-required message until a new `OpenAsync` republishes
  durable truth. Reads of the already-published snapshot stay allowed.
- Upsert rules: unknown Scene ID inserts (rejected at 64 stored Scenes before
  encoding); known ID requires `scene.Revision` strictly greater than stored;
  equal revision with byte-identical canonical encoding returns the stored
  entry without a write; anything else throws `InvalidOperationException`
  with a fixed non-echoing message.
- `DeleteAsync` returns false without a write when absent; empty state is
  persistable (the codec accepts zero entries, unlike the apply journal).
- `SavedAt` must be UTC offset zero on save and reopen.

`SceneRepositoryExport.EncodeRedacted(SceneRepositoryEntry entry,
DateTimeOffset exportedAt)` (Application) produces the frozen redacted export
document described below; it is a pure projection with no I/O.

## Platform layer

`AuthenticatedSceneRepositoryStateFile : ISceneRepositoryStatePayloadStore`
delegates to the shared engine with magic `"FSCR"u8`,
`PersistentSceneRepository.MaximumPayloadBytes`, and state name
"Scene repository". `ISceneRepositoryStateKeyStore : IAuthenticatedStateKeyStore`
is the new purpose marker. The engine already provides: header
(magic + version) as AEAD associated data, random nonce, atomic
temp-create-new + write-through + fsync + rename, `.lock` coordination,
reparse-point rejection, and owner-only POSIX modes. Cross-purpose opens fail
on tag verification because the magic participates in the AAD.

Per-OS constants (all distinct from every existing purpose):

| OS | Key custody | State path |
| --- | --- | --- |
| macOS | Keychain service `app.flowspan.scene-repository-state-key`, account `primary-scene-repository-state-key` | `.../Flowspan/Security/scene-repository-state.fscr` |
| Linux | secret-tool `kind=scene-repository-state-key`, account `primary-scene-repository-state-key`, lock `scene-repository-state-key-secret-tool.lock` | same file name |
| Windows | DPAPI context `Flowspan.SceneRepositoryStateKey.DPAPI.v1`, key file `scene-repository-state-key.dpapi` | same file name |

`RedactedExportFile.WriteAsync(directory, fileName, content, ct)` (Platform)
writes the plaintext redacted export: create directory with owner-only mode,
reject reparse points, open `FileMode.CreateNew` (a colliding name fails
rather than overwrites), write-through + flush, owner-only file mode, a
1 MiB content bound, and return the full path. File names are restricted to
an ASCII allowlist (letters, digits, `-`, `_`, `.`, never leading `.`), which
refuses rooted, path-separator-bearing, and Windows alternate-data-stream
(`name.json:stream`) names — the last would otherwise write into an existing
file despite `FileMode.CreateNew`.

## Redacted export document

```json
{"formatVersion":1,"exportKind":"flowspan.scene-export.redacted/v1",
 "exportedAt":"<UTC O>","sceneId":"<guid>","sceneRevision":1,
 "sceneFormatVersion":1,"sceneDigest":"<HEX64>","savedAt":"<UTC O>",
 "group":null,"activityCount":2,
 "activities":[{"index":0,"sourceDisposition":"preserve-source",
                "conflictPolicy":"require-empty"}]}
```

`group` is `{"groupId":"<guid>","revision":n}` when bound. The projection
whitelists fields exactly (the `ReceiptJson` convention); Scene name,
Activity IDs, Device IDs, and slots are structurally absent, which fixture
and canary tests freeze. The export exists for diagnostics correlation — the
digest and IDs match what Scene apply journals and previews bind — while the
canonical (unredacted) format never leaves the encrypted repository in v1.

Export file name:
`scene-export-{sceneId:D}-{exportedAt:yyyyMMddTHHmmssfffZ}-{unique}.json`
(the trailing component is a random 32-hex value so repeated exports never
collide under create-new semantics) under
`LocalApplicationData/Flowspan/Exports` (directory injectable for tests;
never derived from stored Scene content).

## Desktop lifecycle

`IDesktopSceneRepositoryService` (Desktop) exposes:

```csharp
public interface IDesktopSceneRepositoryService : IAsyncDisposable
{
    bool IsSceneRepositoryReady { get; }
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);
    ValueTask<ImmutableArray<SceneRepositoryEntry>> ListScenesAsync(CancellationToken cancellationToken = default);
    ValueTask<SceneRepositoryEntry> SaveSceneAsync(ScenePlan scene, CancellationToken cancellationToken = default);
    ValueTask<bool> DeleteSceneAsync(SceneId sceneId, CancellationToken cancellationToken = default);
    ValueTask<DesktopSceneExportResult?> ExportSceneAsync(SceneId sceneId, CancellationToken cancellationToken = default);
}
```

`ExportSceneAsync` returns null when the Scene is no longer stored, so the
panel can report not-found truthfully instead of a generic failure.

`DesktopSceneRepositoryRuntime` (internal) owns one
`PersistentSceneRepository`, a `TimeProvider`, and the export directory. It
serializes operations behind a gate. `InitializeAsync` opens the repository
and degrades to not-ready on failure (matching `DesktopActivityRuntime`'s
journal degradation) — never to unprotected storage. After a persistence
failure poisons the open instance, the runtime disposes it and reopens from
durable state on the next operation; the failed mutation itself is never
retried implicitly. `SaveSceneAsync` exists for the future creation workflow
and tests; no v1 UI calls it.

`SceneRepositoryViewModel` follows `SceneApplyViewModel` conventions (no
dispatcher, `ConfigureAwait(true)`, lifetime `CancellationTokenSource`,
`AsyncRelayCommand`, fixed uppercase status strings, `catch (Exception)`
without binding). It receives the service and a `selectScene` callback bound
to `Scenes.SelectScene(plan, null)`. It exposes the Scene list (name, ID,
revision, item count, Group binding, digest, saved-at), selected-Scene
ordered item details (the user's own data — names and slots are legitimately
visible here), and the commands Refresh, Select for apply,
Begin/Cancel/Confirm delete (two-step `TrustedDevicesViewModel` pattern with
"This action has no undo."), and Export (surfacing the exact written path
plus redacted content, or a fixed failure status).

`WorkspaceShellViewModel` constructs and exposes `SceneRepository`,
initializes it during `InitializeAsync`, and disposes it in the existing
failure-collecting order. `DesktopCompositionRoot.CreateProduction` adds the
OS-switch payload store factory with an Unsupported fallback;
`CreateValidation` passes no repository service, so the panel proves the
unavailable degradation and `--validate-composition` asserts it while still
passing.

MainWindow adds a "Scene repository" bordered panel beside the existing
Scene apply panel: `SceneRepositoryList`, per-Scene inspect region, and the
named controls tests drive (`SceneRepositoryRefreshButton`,
`SceneRepositorySelectButton`, `SceneRepositoryBeginDeleteButton`,
`SceneRepositoryConfirmDeleteButton`, `SceneRepositoryCancelDeleteButton`,
`SceneRepositoryExportButton`, plus status texts), all with
`AutomationProperties.Name` and destructive `HelpText`, 44-pixel minimum
control heights, and no color-only state.

## Verification matrix

- empty open, insert, revise, idempotent identical re-save without write,
  stale/equal-revision rejection, delete, delete-to-empty, reopen round-trip;
- 0/64/65 Scene bounds and the 32 KiB per-Scene bound through the repository;
- byte-identical canonical persistence and digest equality with Scene apply;
- every save boundary × {fail-before-write, fail-after-write}: candidate not
  published, poisoning until reopen, durable truth wins on reopen;
- envelope strictness: unknown/duplicate/missing fields, malformed nested
  Scene, duplicate/misordered Scene IDs, non-UTC timestamps, version drift,
  over-bound payloads, trailing data;
- `FSCR` magic on disk, tamper rejection, cross-purpose rejection in both
  directions, plaintext canary absence (name, slot, Activity ID, Device ID);
- per-OS key/path purpose-separation constants, native round-trips gated on
  the running OS, off-OS unsupported assertions, pre-cancelled no-side-effect;
- frozen redacted export fixture, export canary absence, create-new
  collision, invalid file-name rejection, reparse-point rejection;
- Desktop: unavailable degradation, refresh/list/inspect, select-for-apply
  reaching `SelectScene` with the loaded plan, two-step delete, export path
  and failure strings, redacted exception handling, full single-Dispatch
  keyboard workflow with accessible-name assertions and the persistent
  NOT SHARING check;
- local fresh-process stress plus exact-commit hosted matrix and downloaded
  TRX evidence.

## Delivery limits

Hosted OS jobs prove portable code and contract behavior only. Native
Keychain/Secret Service/DPAPI behavior on real logged-in desktops, process
kill and power loss, packaging, physical devices, and native assistive
technology remain separate evidence. Scene/Group creation UI, Group
persistence, import, and trust/history/diagnostics lifecycle remain open.
