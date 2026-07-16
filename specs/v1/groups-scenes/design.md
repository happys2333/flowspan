# Activity Groups and Scene Plan Design

Status: implemented candidate for task 8.1; hosted delivery evidence pending

## Design summary

Task 8.1 adds two immutable domain aggregates and one strict local codec:

- an `ActivityGroup` is one stable ID, one monotonic revision, a bounded display
  name, and an immutable ordered list of unique Activity IDs;
- a `ScenePlan` is one stable ID, one monotonic revision, format version 1, an
  optional exact Group binding, and an immutable ordered list of typed Activity
  placement/policy items;
- `ScenePlanCodec` is a closed, bounded JSON format for local persistence. It
  neither serializes an Activity Descriptor nor accepts extension data.

The design deliberately reuses Handoff, Move, and Replace concepts. A Scene is
an orchestration plan, not a fourth transfer protocol and not a process-memory
snapshot.

## Domain model

`Flowspan.Domain` adds `GroupId` and `SceneId` beside the existing opaque GUID
identifiers. Empty GUIDs are rejected and canonical text is lowercase `D`
format.

### Activity Group

```csharp
public sealed record ActivityGroup
{
    public const int MaximumActivities = 64;
    public const int MaximumNameCharacters = 120;

    public GroupId Id { get; }
    public long Revision { get; }
    public string Name { get; }
    public ImmutableArray<ActivityId> Activities { get; }

    public static ActivityGroup Create(...);
    public ActivityGroup Revise(string name, IEnumerable<ActivityId> activities);
}
```

Factories trim names, reject control characters and malformed UTF-16, require
revisions from one upward, materialize the enumerable exactly once, reject
null/duplicate IDs, and copy it into `ImmutableArray`. `Revise` validates a
complete replacement before returning an aggregate with the same ID and
`checked(Revision + 1)`.

There is no Group-of-Groups type. This matches the ubiquitous language and
prevents recursive expansion, cycles, and hidden ordering rules.

### Scene plan item

```csharp
public enum SceneSourceDisposition
{
    PreserveSource,
    MoveAfterAcknowledgement,
}

public enum SceneConflictPolicy
{
    RequireEmpty,
    ReplaceWithUndo,
}

public sealed record SceneActivityPlan
{
    public ActivityId ActivityId { get; }
    public ActivityPlacement Placement { get; }
    public SceneSourceDisposition SourceDisposition { get; }
    public SceneConflictPolicy ConflictPolicy { get; }

    public static SceneActivityPlan Place(...);
}
```

`PreserveSource` maps to Handoff semantics. `MoveAfterAcknowledgement` maps to
the existing Move acknowledgement boundary. `RequireEmpty` blocks a collision;
`ReplaceWithUndo` invokes the existing Replace preservation and undo boundary
during task 8.2. The plan item contains no descriptor, source device, secret, or
runtime session state. `ActivityPlacement.On` rejects malformed UTF-16 and
control characters on the raw slot before whitespace normalization; Scene
construction checks the normalized placement invariant again before publishing
the item.

### Exact Group binding

```csharp
public sealed record SceneGroupBinding
{
    public GroupId GroupId { get; }
    public long GroupRevision { get; }
}
```

`ScenePlan.CreateFromGroup` requires the item Activity IDs to equal the Group's
IDs in the same order. The Scene stores only `GroupId` plus `GroupRevision`; the
already expanded item IDs remain the executable source of truth. A later Group
edit therefore cannot silently change a saved Scene. Task 8.2 may present a
stale-binding warning, but it never performs live membership expansion.

### Scene plan

```csharp
public sealed record ScenePlan
{
    public const int CurrentFormatVersion = 1;
    public const int MaximumActivities = 64;
    public const int MaximumNameCharacters = 120;

    public SceneId Id { get; }
    public long Revision { get; }
    public int FormatVersion { get; }
    public string Name { get; }
    public SceneGroupBinding? GroupBinding { get; }
    public ImmutableArray<SceneActivityPlan> Activities { get; }

    public static ScenePlan Create(...);
    public static ScenePlan CreateFromGroup(...);
    public ScenePlan Revise(...);
    public ScenePlan ReviseFromGroup(...);
}
```

Factories enforce non-empty unique Activity IDs, exact ordering, defined enums,
and bounded names. Revisions are positive and increment with `checked`; format
version is always 1 in the domain object. `ToString()` includes only Scene ID,
revision, format, Group ID/revision when present, and item count. It excludes the
Scene name, placement slots, and all Activity content.

## Canonical local format

`Flowspan.Application.ScenePlanCodec` owns the local JSON boundary because
serialization is not a domain invariant. Its maximum input is 32 KiB and its
maximum JSON depth is 8.

The canonical property order is frozen:

```json
{
  "formatVersion": 1,
  "sceneId": "11111111-1111-1111-1111-111111111111",
  "revision": 1,
  "name": "Focus",
  "group": {
    "groupId": "22222222-2222-2222-2222-222222222222",
    "revision": 3
  },
  "activities": [
    {
      "activityId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "deviceId": "33333333-3333-3333-3333-333333333333",
      "slot": "main",
      "sourceDisposition": "preserve-source",
      "conflictPolicy": "require-empty"
    }
  ]
}
```

An ungrouped Scene writes `"group": null`. `Utf8JsonWriter` emits compact UTF-8
without a BOM. The writer uses unescaped Unicode because this is an
`application/json` data artifact, never HTML; Scene names and slots reject
control characters and malformed UTF-16, and a 64-item maximum-Unicode plan
remains below the decoder's 32 KiB limit. GUIDs use lowercase `D` text. Enum
tokens are exactly:

- `preserve-source`;
- `move-after-acknowledgement`;
- `require-empty`;
- `replace-with-undo`.

Decode checks the byte bound before parsing, uses `JsonDocument` with depth 8,
comments disabled, and trailing commas disabled, then walks every object through
a property-name set. Each object must contain every required property exactly
once and no unknown property. Numeric values must be exact JSON integers. The
codec constructs domain factories so decoded values pass the same invariants as
in-process values.

The codec does not accept a generic metadata map or retain unknown JSON. This is
the structural secret boundary: payload, token, key, trust, and session fields
are not representable and cannot round-trip through an extension bag.

## Module changes

- `Flowspan.Domain/Identifiers.cs`: add `GroupId` and `SceneId`.
- `Flowspan.Domain/Activities.cs`: validate raw placement text before
  normalization.
- `Flowspan.Domain/GroupsAndScenes.cs`: immutable Group, binding, Scene item, and
  Scene plan aggregates.
- `Flowspan.Application/ScenePlanCodec.cs`: strict version-1 local codec.
- `Flowspan.Domain.Tests`: Group and Scene invariant tests.
- `Flowspan.Integration.Tests`: codec fixture, hostile schema, bound, and round-
  trip tests.

No transport message, Capability check, Desktop command, or platform adapter is
added by task 8.1.

## Verification matrix

- exact Group order and defensive copying;
- empty, duplicate, null, 65-item, control-character, malformed-Unicode, and
  overlong negatives;
- stable IDs and monotonic checked revisions;
- individual and Group-derived Scenes, including exact Group-order mismatch;
- invalid enum values and placement data;
- redacted `ToString()` canaries;
- canonical fixture bytes and SHA-256 digest;
- canonical round trip and order preservation;
- missing, duplicate, unknown, mistyped, malformed, over-depth, over-size, and
  trailing-data JSON negatives;
- explicit rejection of fields named like payload, traffic key, session ID,
  reservation token, and Undo Capsule.

## Security and delivery limits

Scene definitions reveal device and Activity identifiers plus desired placement
slots and user-chosen names. They are local private product data even though the
schema carries no Activity payload or cryptographic secret. Task 8.3 must define
repository access, inspect/delete/export, filesystem protection, and export
redaction before the release criterion can close.

This slice does not authorize or execute `scene.apply`; task 8.2 must recheck
current Trust, `scene.apply`, operation-specific Capabilities, Activity state,
and Replace confirmation immediately before every operation. Same-host and
hosted tests do not constitute physical two-device Scene behavior.
