# Scene Apply Design

Status: approved design for task 8.2; implementation pending

## Design summary

Scene apply is a two-phase application workflow over existing Handoff, Move,
Replace, and Undo boundaries:

1. `SceneApplyPlanner` creates an expiring immutable preview from the exact
   saved Scene and current read-only observations.
2. `SceneApplyCoordinator` verifies an exact approval, journals the attempt,
   and executes child operations sequentially in Scene order.

The coordinator is best-effort, not a distributed transaction. It continues
after proven terminal failures so independent Activities can complete, but a
Recovering/unknown child outcome is a hard ordering barrier.

## Core model

The application layer adds payload-free types:

- `SceneApplyPreview`: Scene ID/revision/digest, parent Operation/correlation,
  creation/expiry, Group-revision warning, and ordered item previews;
- `SceneApplyItemPreview`: item index, Activity ID, requested policy, resolved
  No Change/Handoff/Move/Replace action or blocker, exact user-selected source
  evidence, child IDs, and optional exact Replace target evidence;
- `SceneSourceSelection`: item index plus exact source Device, Activity,
  revision, descriptor digest, kind, and placement snapshot selected from one
  purpose-scoped exact-ID result;
- `SceneSlotOccupancy`: one of Empty, Eligible Conflict, Opaque, or Ambiguous;
  only Eligible Conflict contains an exact target snapshot;
- `SceneApplyApproval`: preview fingerprint plus the exact set of confirmed
  Replace item fingerprints;
- `SceneApplyResult` and `SceneApplyItemResult`: derived overall and ordered
  per-item truth;
- `SceneCompensationResult`: reverse-order per-Replace undo truth.

`SceneApplyItemReason` is separate from the wire-level `FailureCode`. It names
planning/orchestration conditions such as stale preview, destination occupied,
ambiguous occupancy, replacement confirmation required, and not attempted after
uncertainty without pretending that a child wire operation occurred.

All collections are immutable and bounded by `ScenePlan.MaximumActivities`.
Fingerprints use canonical length-prefixed fields and SHA-256. They are binding
digests, not authentication or authorization tokens.

### Canonical binding fingerprints

`SceneApplyBindingCodec` writes a fixed typed token sequence, not JSON. Each
token is four-byte unsigned big-endian UTF-8 byte length followed by those exact
bytes. The first token is a domain/version string
`flowspan.scene-apply-preview/v1` or
`flowspan.scene-apply-replace-confirmation/v1`. Collections write an invariant
decimal count followed by entries in Scene index order; optional values write an
explicit `none` or `some` token before any value. No locale, platform newline,
dictionary iteration, or display rendering participates.

Scalar forms are frozen: IDs are lowercase canonical `D` GUIDs; revisions,
indices, and counts are invariant decimal without leading zero; timestamps are
UTC round-trip `O`; digests and resulting SHA-256 fingerprints are uppercase
64-character hexadecimal; enums use fixed lowercase tokens; and strings are
their validated UTF-8 bytes without normalization beyond the owning domain
factory. The encoder is bounded before allocation and rejects malformed UTF-16.

The preview sequence covers the Scene ID/revision/digest, parent IDs,
creation/expiry, Group-warning evidence, item count, and for every item its
index, Scene policies/destination, child IDs, source status/exact selection,
resolved action/reason, exact-slot classification, and optional exact Replace
target. A Replace-confirmation fingerprint covers the preview fingerprint plus
that item's index, incoming Activity, and complete exact target snapshot. An
approval holds those Replace fingerprints once each in strict Scene index order
and must equal the preview-derived sequence exactly. Golden byte/hash fixtures
freeze both domains.

## Ports and module boundaries

```csharp
public interface ISceneApplyPreflightPort
{
    ValueTask<SceneSourceLookup> LocateSourcesAsync(
        ActivityId activityId,
        int index,
        OperationContext childContext,
        CancellationToken cancellationToken);

    ValueTask<SceneExactSlotInspection> InspectExactSlotAsync(
        SceneActivityPlan item,
        SceneSourceSelection source,
        OperationContext childContext,
        CancellationToken cancellationToken);
}

public interface ISceneActivityOperationPort
{
    ValueTask<SceneActivityOperationResult> ExecuteAsync(
        SceneActivityPreparation preparation,
        CancellationToken cancellationToken);

    ValueTask<UndoReplaceResult> UndoReplaceAsync(
        UndoCapsuleReference capsule,
        OperationContext context,
        CancellationToken cancellationToken);
}

public interface ISceneApplyJournal
{
    ValueTask<SceneApplyJournalState> LoadOrCreateAsync(...);
    ValueTask RecordItemStartedAsync(...);
    ValueTask RecordItemOutcomeAsync(...);
    ValueTask RecordCompletedAsync(...);
}
```

The interfaces are deliberately narrow:

- the planner sees only current payload-free evidence;
- the preflight port performs authenticated, purpose-scoped exact-ID and exact-
  slot queries; its exact-slot result separates a successful occupancy
  observation from Capability Denied, Protocol Unsupported, and Destination
  Unavailable blockers, and it never exposes a general Activity inventory;
- the operation port owns last-moment Trust, orchestration and operation-
  specific Capability, catalog, connection, and exact source/Replace snapshot
  revalidation and calls existing operations;
- the journal owns replay and ambiguous-save behavior but never calls an
  Adapter;
- Desktop maps the preview/result to visible confirmation and outcome models.

`Flowspan.Domain.ScenePlan` remains an inert definition and gains no network or
authorization dependency.

## Preview and exact confirmation

The planner canonical-encodes the Scene with `ScenePlanCodec`, hashes the bytes,
and generates one parent plus one child Operation/correlation pair. A Scene does
not save source Devices, so for each item the preflight port first performs only
a purpose-scoped exact-Activity-ID lookup. It returns bounded payload-free exact
active source snapshots or a non-disclosing blocker; it never enumerates
unrelated Activities.

Zero eligible active sources block the item. More than one returns Selection
Required. The user must select one exact snapshot and regenerate the entire
preview. Discovery order, greatest revision, title, Device ID, or any other
heuristic must not choose a source. The selected snapshot is part of the preview
fingerprint and is fully revalidated at apply time.

If the exact selected source is already on the desired Device and placement
slot, the planner resolves No Change and does not query for mutation authority.
Otherwise it calls a Scene-specific purpose-scoped exact-slot occupancy query.
That query examines occupancy before eligibility filtering and returns exactly:

- Empty, containing no Activity metadata;
- Eligible Conflict, containing one exact Replace-eligible target snapshot;
- Opaque, when occupancy is protected, sensitive, restricted, different-kind,
  unsupported, or otherwise ineligible, containing no hidden metadata; or
- Ambiguous, when absence or one exact conflict cannot be proved, containing no
  candidate metadata.

The filtered Replace inventory is never evidence that a slot is empty. Require
Empty blocks every non-Empty result. Replace With Undo resolves Replace only for
Eligible Conflict with Preserve Source and current durable target-owned undo
availability; Opaque and Ambiguous fail closed.

The portable same-host contract harness models one `SceneApplyPreflightEndpoint`
per Device. Each endpoint reloads the coordinator's peer-relative
`scene.apply` grant for every query and reads only its current local Activity
snapshot. `DirectSceneApplyPreflightPort` aggregates exact-ID responses without
selecting a candidate; if any participating endpoint is denied, unavailable,
malformed, or over-bound, it discards all partial candidates. Exact-slot
inspection runs only on the destination endpoint and examines matching
occupants before sensitivity, kind, Adapter, or undo eligibility filtering.
This direct port proves application semantics but is not evidence for the
protocol-1.4 authenticated transport that remains in task 5.

`SceneActivityOperationEndpoint` binds one Device's `FlowspanNode`, its
`SceneApplyPreflightEndpoint`, and an optional `ReplaceEndpoint` behind one
grant refresh, and `DirectSceneActivityOperationPort` executes an approved item
against those real production boundaries. Immediately before mutation it
re-locates the exact source, rereads it from the source node, and requires an
exact match on Activity ID, revision, descriptor digest, kind, placement,
lifecycle, and sensitivity. A missing Activity is `ActivityNotFound`; a present
but changed one is `RevisionConflict`. The exact locally-read snapshot is then
passed into `FlowspanNode.HandoffAsync`/`MoveAsync`, which compares it again at
the send boundary, so a descriptor that changed after the recheck is never
offered to the target. Replace resolves only when exact-slot occupancy still
returns the same eligible conflict with durable undo availability, and it flows
through `DirectReplaceChannel` into the real `ReplaceEndpoint`, which itself
verifies revision, descriptor digest, exact placement, and lifecycle before
capturing undo. The child deadline is `AcceptedAt` plus five minutes and the
undo expiry is `AcceptedAt` plus `ReplaceEndpoint.MaximumUndoRetention`; neither
is read from retry-time clocks, because both participate in request digests and
must stay stable across duplicate attempts. A same-Device transfer to a
different slot fails closed at the receiving catalog rather than mutating.

The action matrix is closed and tested:

| Current state | Source disposition | Conflict policy | Resolution |
| --- | --- | --- | --- |
| Selected source already at exact destination | either | either | No Change |
| Exact slot Empty | Preserve Source | either | Handoff |
| Exact slot Empty | Move After Acknowledgement | either | Move |
| Eligible Conflict | Preserve Source | Replace With Undo | Replace |
| Eligible Conflict | Move After Acknowledgement | Replace With Undo | Blocked: unsafe move-plus-replace |
| Any non-Empty | either | Require Empty | Blocked |
| Opaque or Ambiguous | either | either | Blocked |

The occupied Move-plus-Replace combination is deliberately unsupported in v1.
Existing Replace preserves the source. Closing it afterwards would allow a
target-only Undo to remove the incoming replacement's last instance; leaving it
open would violate Move After Acknowledgement. Flowspan blocks instead of
inventing an unreviewed compound protocol or misleading source semantics.

Before either query discloses its bounded evidence, every remote participant
must currently grant the coordinator peer-relative `scene.apply`. That is only
orchestration permission. Handoff and Move still require the existing source
`activity.receive` and target `activity.offer` checks; Replace still requires
source `activity.receive` and target `activity.replace`. Preview reports missing
layers as blockers, and every layer is reloaded again at operation use time.

Preview never grants authority. Apply rejects the complete request before the
first journal mutation if the preview expired, the Scene binding changed, or
any resolved Replace lacks a byte-for-byte exact confirmed fingerprint.
`SceneApplyPreview.MaximumLifetime` is five minutes and factories require
`createdAt < expiresAt <= createdAt + MaximumLifetime`. Expiry admits or rejects
a new attempt; an already accepted durable attempt retains its original binding
for exact replay/reconciliation and does not mint new child identities.

An observed Group revision mismatch is a warning requiring acknowledgement,
not a source of live membership. The explicit Scene items remain authoritative.

## Execution reducer

The coordinator persists the attempt before child execution, then reduces each
item in ascending index:

```text
PreviewBlocked -> Blocked result -> continue
NoChange -> persist terminal NoChange result -> continue (no operation call)
Ready -> persist Started -> execute/recheck -> persist terminal result
Committed/Warning/Rejected/Failed -> continue
Recovering/unknown -> persist Recovering -> mark remainder NotAttempted -> stop
Cancellation between items -> mark remainder Cancelled -> stop
```

An exception after entering the operation port is outcome-unknown unless the
port returns durable evidence. The coordinator records Recovering/Internal
Failure and stops; it never guesses Not Delivered.

The same attempt reloads the same child IDs. Terminal children replay from the
apply journal and the child operation journal; a recorded Started child without
a terminal outcome blocks at Recovering. Journal save ambiguity poisons the
open instance until it is reopened, matching existing protected-state rules.

Overall status is a pure reduction over ordered item states, in this priority:

1. any Recovering/unknown boundary -> `Recovering`;
2. every item No Change or Committed, with any committed warning ->
   `Completed-With-Warnings`;
3. every item No Change or Committed, without warning -> `Completed`;
4. at least one No Change/Committed and at least one terminal unsatisfied or
   cancelled item -> `Partially-Completed`;
5. cancellation before any satisfied item -> `Cancelled`;
6. otherwise, with only proven terminal blocker/rejection/failure evidence ->
   `Blocked`.

No Change counts as the requested placement already satisfied, not as a
mutation. None of these overall labels implies atomic commit.

## Authenticated remote source boundary

The operation port has two implementations behind one public behavior. If the
selected exact source is local to the coordinator, it invokes the existing
Desktop Handoff, Move, or Replace boundary directly. If the source is another
Device, it sends a payload-free child instruction over that source's current
authenticated encrypted control session. The source-side runner then invokes
the same existing operation against the exact destination. The coordinator
never receives or forwards the Activity descriptor or payload.

Scene source lookup, exact-slot inspection, child instruction, and child result
use strict bounded control messages introduced in protocol 1.4 and are rejected
before send on 1.0–1.3. Each message binds negotiated version, authenticated
sender/receiver, parent attempt, child Operation/correlation identity where
applicable, exact Scene/preview fingerprint, selected source, target evidence,
action, and deadline. Unknown/duplicate fields, mismatched participants,
payload-like extensions, expired requests, or wrong action evidence fault
closed.

The remote runner reloads its current peer-relative `scene.apply` grant for the
coordinator, revalidates the exact local source, and durably deduplicates by the
frozen child Operation/digest before invoking anything. The source-to-target
operation still performs its own existing authenticated capability checks. A
recorded terminal child is replayed exactly; disconnect, lost acknowledgement,
or Started-without-terminal evidence returns Recovering. This is direct peer
orchestration, not an Internet relay or permission delegation.

## Replace and compensation

`ReplaceWithUndo` does not mean blind overwrite. The Desktop operation port:

1. performs the Scene-specific exact-slot occupancy query before eligibility
   filtering and accepts only Eligible Conflict;
2. binds target Device/Activity/revision/digest/kind/slot into preview;
3. re-runs the exact-slot query and matches every field at send time;
4. independently rechecks `scene.apply`, `activity.receive`, and
   `activity.replace` for their proper authenticated participants;
5. calls the existing protected `ReplaceAsync` path;
6. accepts committed success only with a target-owned Undo Capsule reference.

The coordinator records only the capsule reference. Explicit compensation walks
committed Preserve-Source Replace results in descending Scene index and invokes
existing target-local undo. Occupied Move-plus-Replace never executes and cannot
enter this set. Handoff remains a safe copy and Move remains acknowledged
target-first; neither is automatically reversed.

## Desktop presentation

The existing Activity workspace remains the unified entry. A Scene preview must
show saved order, action per Activity, blockers, source-preserved/source-closes
language, exact Replace target, warning/expiry state, and one explicit
confirmation control. Destructive confirmations are individually selectable
and keyboard/screen-reader named. Applying a Scene does not change the global
sharing indicator because Scene v1 contains no capture/input action.

User-visible strings must remain centralized when task 7.4 externalizes the
current product strings. Headless accessibility tests are not native assistive-
technology evidence.

## Persistence and bounds

The apply journal is separate from the Scene repository planned by task 8.3.
Its version-1 record is bounded to 32 attempts and 64 items per attempt. It uses
the existing platform protected-store pattern with a purpose-separated key and
authenticated file magic/version. Records contain no names, titles, payloads,
exception text, Trust, capabilities, or Undo Capsule contents.

Every mutation writes a complete candidate snapshot atomically. A failed or
ambiguous save does not publish the candidate in memory and blocks further
writes until reopen. Corrupt, unsupported, rollback-ambiguous, or structurally
conflicting records fail closed.

## Verification matrix

- exact saved order and immutable preview/approval fingerprints;
- zero/one/multiple exact-ID source results, explicit source selection and full
  repreview, heuristic-selection negatives, and exact-destination No Change;
- exact-slot Empty, Eligible Conflict, Opaque, and Ambiguous results, including
  sensitive/restricted/different-kind/unsupported occupancy and proof that an
  empty filtered Replace inventory cannot produce Empty;
- the complete source-disposition/conflict/occupancy action matrix, especially
  occupied Move-plus-Replace rejection before operation and compensation;
- every mixture of committed, warning, blocked, rejected, failed, recovering,
  cancellation, and thrown operation outcomes;
- exact overall-status priority including all-No-Change, warning-only,
  pre-first cancellation, partial cancellation, and Recovering dominance;
- terminal continuation and Recovering halt properties for 1 through 64 items;
- exact retry IDs, no duplicate Adapter mutation, and restart reduction;
- stale Scene/Group, preview expiry, missing Replace confirmation, changed
  source selection/snapshot, changed target snapshot, independent scene.apply
  and child-operation Capability revocation, destination occupancy races;
- Replace success only with a capsule; reverse-order explicit undo with stale,
  expired, consumed, failed, and Recovering results;
- every journal write failure, strict codec/bound/tamper negatives, and canary
  redaction;
- Desktop keyboard/accessibility contracts and same-host encrypted operation
  routing, including a three-identity coordinator/remote-source/target path in
  which the coordinator never observes descriptor payload;
- protocol-1.4 feature negotiation, golden strict messages, hostile binding,
  duplicate, timeout, disconnect, and acknowledgement-loss cases, plus 1.0–1.3
  unsupported behavior;
- local fresh-process stress plus exact-commit hosted matrix and downloaded TRX
  evidence.

## Delivery limits

Hosted OS jobs prove portable code and contract behavior only. Physical
networking, native application Adapters, protected-store behavior on a logged-in
Linux desktop, process kill, power loss, sleep/wake, native accessibility, and
packaging remain separate evidence. Task 8.3 still owns Scene repository and
inspect/delete/export lifecycle.
