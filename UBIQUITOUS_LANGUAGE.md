# Flowspan Ubiquitous Language

These terms are normative in requirements, protocol names, diagnostics, and UI
copy. They describe user-visible workspace continuity rather than physical
screens or arbitrary process migration.

## Activity continuity

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Activity** | A unit of user intent plus portable context that can be captured, resumed, placed, or observed. | Screen, app process, session |
| **Activity Descriptor** | A versioned, validated representation of portable Activity context and metadata. | Process image, app dump |
| **Adapter** | An integration that captures and/or resumes one semantic Activity kind. | Migrator |
| **Semantic Handoff** | A resume of an Activity through a target Adapter using portable context. | App migration |
| **Remote Window** | An explicit fallback that keeps execution on the source while presenting captured output and optional authorized input elsewhere. | Migration, screen handoff |
| **Handoff** | An operation that resumes an Activity on another device while preserving the source. | Move |
| **Move** | An operation that resumes an Activity elsewhere, then closes or suspends the source only after verified target acknowledgement; failed source cleanup is a committed duplicate warning, not a target rollback. | Handoff |
| **Replace** | An operation that preserves eligible target state before installing an incoming Activity in its Placement. | Overwrite |
| **Replace Target Inventory** | A purpose-scoped authenticated preview query that returns only bounded, eligible, same-kind, payload-free target choices for one incoming Activity kind. | Remote Activity browser, catalog sync |
| **Replace Target Snapshot** | The target ID, revision, descriptor digest, kind, normal-sensitivity title, and Placement slot captured for preview and later stale-selection detection; it grants no mutation authority. | Backup, live handle |
| **Replace Preview** | A local comparison and confirmation state bound to one incoming Activity, peer, and exact Replace Target Snapshot. It grants no mutation authority and is revoked by participant, snapshot, or inventory-refresh changes. | Replace command, authorization |
| **Replace Recovery Snapshot** | A bounded, immutable, target-local, payload-free projection of known Replace/undo journal state, ordered to expose unresolved destructive boundaries before terminal history. | Log dump, remote history |
| **Undo Capsule** | A target-owned, expiring preservation of the exact pre-Replace semantic state plus bindings needed for one safe compensating undo. | Backup, rollback promise |
| **Swap** | One atomic transaction that exchanges two Activity Placements or changes neither. | Two moves |
| **Swap Activity Snapshot** | One purpose-scoped, exact disclosure of a named active, normal-sensitivity Activity for a Swap; it is complete enough to build semantic recovery but is never a list or inventory. | Activity inventory, catalog sync |
| **Swap Transaction Intent** | A bounded, payload-free coordinator record written before Prepare that binds one Operation, deadline, both expected Activity snapshots, and both device-owned reservation tokens. | Draft decision, retry cache |
| **Swap Decision** | The one durable Commit or Abort outcome for a Swap, bound to both Device/token participants; an Abort also records its reason. | Message response, local guess |
| **Swap Endpoint Binding** | The durable Operation ID, correlation ID, and remote participant Device ID that must all match an endpoint replay or post-revocation decision. | Operation existence, session hint |
| **Exact Recorded Decision Convergence** | The narrow rule allowing only a decision matching a durable Swap Endpoint Binding and its request/token/digest evidence to converge after `activity.swap` revocation. | Authorization bypass, best-effort retry |
| **Swap Endpoint Journal** | A bounded, Device-owned protected record of Prepared reservations, Swap Endpoint Bindings, exact local/incoming Activity snapshots, and terminal Swap Decisions used for deterministic restart reduction. | Coordinator log, Activity database |
| **Mirror** | An Activity presentation on multiple devices while authoritative execution remains on one host. | Copy, sync |
| **Placement** | The desired device and presentation location of an Activity. | Screen ownership |

## Authority and safety

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Driver** | The device currently authorized by a live Driver Lease to provide input to a mirrored Activity. | Owner, controller |
| **Driver Lease** | A short-lived, monotonic authority token whose expiry or revocation stops remote input. | Input session |
| **Capability** | A narrow permission granted to a paired peer for one class of action. | Trust, access |
| **Pairing** | An interactive ceremony that verifies device identities and creates initial Capability grants. | Login, connection |
| **Unverified Pairing Candidate** | A structurally valid, short-lived LAN discovery observation that may be selected for Pairing but has no authenticated identity or authority until the pairing transcript binds and verifies its signed offer. | Trusted device, connected device, verified peer |
| **Trust Record** | A local binding from a peer identity key to its name, verification state, and granted Capabilities. | Account, session |
| **Identity Claim** | An unauthenticated Device ID or fingerprint carried before handshake proof and usable only to locate candidate trust. | Authenticated identity |
| **Sensitive Surface** | A window, secure-input state, protected content, or policy label that must block or blank capture or input. | Private window |
| **Emergency Stop** | A local action that immediately disables capture and input, revokes Driver Leases, and disconnects active sharing. | Disconnect |
| **Degradation** | A named reduction from requested behavior, such as Semantic Handoff to Remote Window. | Fallback without qualification |

## Composition and operations

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Activity Group** | An explicit ordered collection of Activities operated on as a user-visible unit. | Workspace snapshot |
| **Scene** | Saved desired Placements and policies for Activities or Activity Groups without process memory or secrets. | Snapshot |
| **Scene Apply Preview** | An expiring read-only current-state plan bound to one exact Scene revision/digest, saved item order, child operation identities, blockers, and destructive targets; it grants no authority. | Dry run, authorization |
| **Scene Apply Attempt** | One durable best-effort ordered execution of an approved Scene preview, identified independently from its child Operations. | Scene transaction |
| **Exact Source Selection** | The user's explicit choice of one exact active Device/Activity/revision/digest/kind/slot snapshot returned by a purpose-scoped exact-Activity-ID lookup; a Scene does not imply a source. | Best source, primary copy |
| **Exact Slot Occupancy** | A purpose-scoped Scene observation of one requested Device/slot as Empty, one Eligible Conflict, Opaque, or Ambiguous before eligibility filtering; only the eligible case identifies an Activity. | Replace inventory, empty target list |
| **No Change** | A Scene item outcome used when the exact selected source already occupies the requested Device/slot; it performs no child operation or Adapter call. | Successful move |
| **Partial Completion** | A truthful Scene outcome in which some independent items reached terminal outcomes while others failed, were blocked, or were not attempted. | Partial success without item detail |
| **Compensation** | An explicit best-effort follow-up that invokes only existing exact safe undo evidence; it is not atomic rollback. | Rollback |
| **Operation** | An idempotent requested change identified by a globally unique Operation ID. | Request, command |
| **Operation Receipt** | A redacted durable record of an Operation, its participants, transitions, outcome, and possible undo. | Log |
| **Reservation** | A time-bounded promise made during prepare that an Activity can participate in a transaction. | Lock |
| **Local-first** | A product property in which useful discovery and direct operation work on a LAN without an Internet service and state remains locally owned. | Offline-only |

## Relationships

- An **Activity** has one or more **Placements** after a successful **Handoff**;
  a **Mirror** specifically retains one authoritative execution host.
- An **Activity Descriptor** describes one resumable revision of one
  **Activity**; it never represents arbitrary process memory.
- A **Semantic Handoff** and a **Remote Window** are distinct execution modes;
  the latter keeps authoritative execution on the source.
- A **Move** is complete only after the target acknowledges resume; a
  **Handoff** never removes the source. If Move source cleanup fails, the target
  remains committed and both active copies must be reported.
- A **Mirror** has at most one effective **Driver Lease** at a time.
- A **Trust Record** may grant zero or more independent **Capabilities**.
- A Capability is always a local grant to the named peer. In particular,
  `activity.offer` allows that peer to send an Activity to this device, while
  `activity.receive` allows this device to disclose and send an Activity to that
  peer. A reusable Activity control channel may require either grant, but each
  Operation still checks its exact direction immediately before payload use.
- `activity.swap` is independent of Offer, Receive, and Replace. It authorizes
  new exact Swap Activity Snapshot disclosure, Prepare, or unknown decisions;
  only Exact Recorded Decision Convergence survives later revocation.
- An **Unverified Pairing Candidate** can open a Pairing attempt but cannot
  become a **Trust Record**, show a SAS prompt, or authorize a **Capability**
  until its Device ID, fingerprint, signature, and lifetime match the
  transcript-authenticated peer.
- An **Identity Claim** selects candidate trust but cannot establish identity or
  authorize a **Capability** without key and transcript verification.
- An **Activity Group** contains one or more ordered **Activities**; a **Scene**
  may describe Placements for Activities or Groups.
- A **Scene Apply Preview** binds one exact saved **Scene** and current evidence
  but never substitutes for Trust or Capability checks at use time. A Scene has
  no implied source: multiple active placements require **Exact Source
  Selection** and complete repreview. **Exact Slot Occupancy**, never filtered
  Replace inventory, determines whether the destination is empty or blocked.
- A **Scene Apply Attempt** executes child **Operations** sequentially in saved
  order. Proven terminal failures may produce **Partial Completion**; a
  Recovering child stops later items. **Compensation** may undo exact committed
  Preserve-Source Replace items but never promises whole-Scene rollback.
  `scene.apply` permits
  orchestration only; each child still requires its operation-specific
  Capabilities. A **No Change** item calls no Operation or Adapter. Occupied
  Move-plus-Replace is blocked in v1 because target-only undo after source
  cleanup could remove the incoming Activity's last instance.
- An **Operation** produces one terminal **Operation Receipt** and may reference
  one or more expiring **Reservations** before commitment.
- A **Swap Transaction Intent** precedes both endpoint Prepare requests. If it
  is reconstructed without a **Swap Decision**, recovery records Abort before
  contacting either participant; it never guesses Commit.
- A **Swap Decision** binds each reservation token to its participant Device.
  Commit exchanges both Placements or remains recovering; Abort preserves both
  originals and blocks a delayed Prepare through an endpoint tombstone.
- A **Swap Activity Snapshot** names and returns at most one exact Activity. It
  is never a peer inventory, wildcard query, or discovery record.
- A **Swap Endpoint Journal** persists Prepared before acknowledgement and a
  **Swap Decision** before local catalog mutation. Its **Swap Endpoint Binding**
  must match before revoked authority can use **Exact Recorded Decision
  Convergence**. It contains protected private recovery content and never becomes
  discovery, diagnostics, or coordinator metadata.
- A committed **Replace** has one target-owned **Undo Capsule**. Its payload
  never travels back to the source; only bound, payload-free availability
  metadata may appear in an authenticated result.
- A **Replace Target Inventory** contains at most one canonical page of
  **Replace Target Snapshots**. Sensitive, restricted, inactive, non-local,
  different-kind, or unsupported Activities do not appear. A later **Replace**
  must revalidate the chosen ID, revision, and descriptor digest before
  destructive work.
- A **Replace Preview** may retain an unchanged target selection across a fresh
  inventory query for orientation, but it always revokes confirmation. A missing
  or changed snapshot requires a fresh selection; confirmation alone never
  authorizes or sends **Replace**.
- A **Replace Recovery Snapshot** exposes only known opaque identifiers,
  participants, state, redacted reason, timestamps, and capsule availability.
  It never includes descriptors, preserved payload, request digests, or
  exception text, and it never invents fields absent from a pre-capture pending
  record.

## Example dialogue

> **Developer:** “Did the laptop migrate the editor to the desktop?”
>
> **Domain expert:** “No. Its Adapter completed a **Semantic Handoff** of the
> editor **Activity**. If that Adapter were unavailable, Flowspan could instead
> offer a labelled **Remote Window**, with execution still on the laptop.”
>
> **Developer:** “If the user chooses **Move**, when may the laptop close its
> copy?”
>
> **Domain expert:** “Only after the desktop acknowledges the resume. A plain
> **Handoff** preserves the source, and a **Swap** changes both Placements
> atomically or changes neither.”
>
> **Developer:** “Can both mirrored devices inject input?”
>
> **Domain expert:** “No. Only the current **Driver Lease** authorizes input, and
> **Emergency Stop** revokes it locally without waiting for the peer.”

## Flagged ambiguities

- **Screen** can mean a monitor, desktop, window, or user context; use
  **Activity**, **Placement**, or **Remote Window** for the intended concept.
- **Migration** falsely suggests arbitrary process-state transfer; use
  **Semantic Handoff**, **Move**, or **Remote Window**, and state what remains on
  the source.
- **Share** can mean view, drive, or disclose content; use **Mirror** plus the
  exact **Capability**.
- **Connected** conflates discovery, trust, and readiness; distinguish
  *discovered*, *paired*, *authenticated*, and *operation-ready*.
- **Session** is overloaded; qualify it as *transport session*, *secure session*,
  *mirror session*, or *application session*.
- **Owner** can mean the local person, authoritative execution host, or input
  holder; use *device owner*, *execution host*, or **Driver**.
