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
| **Move** | An operation that resumes an Activity elsewhere, then closes or suspends the source only after target acknowledgement. | Handoff |
| **Replace** | An operation that preserves eligible target state before installing an incoming Activity in its Placement. | Overwrite |
| **Swap** | One atomic transaction that exchanges two Activity Placements or changes neither. | Two moves |
| **Mirror** | An Activity presentation on multiple devices while authoritative execution remains on one host. | Copy, sync |
| **Placement** | The desired device and presentation location of an Activity. | Screen ownership |

## Authority and safety

| Term | Definition | Aliases to avoid |
| --- | --- | --- |
| **Driver** | The device currently authorized by a live Driver Lease to provide input to a mirrored Activity. | Owner, controller |
| **Driver Lease** | A short-lived, monotonic authority token whose expiry or revocation stops remote input. | Input session |
| **Capability** | A narrow permission granted to a paired peer for one class of action. | Trust, access |
| **Pairing** | An interactive ceremony that verifies device identities and creates initial Capability grants. | Login, connection |
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
  **Handoff** never removes the source.
- A **Mirror** has at most one effective **Driver Lease** at a time.
- A **Trust Record** may grant zero or more independent **Capabilities**.
- An **Identity Claim** selects candidate trust but cannot establish identity or
  authorize a **Capability** without key and transcript verification.
- An **Activity Group** contains one or more ordered **Activities**; a **Scene**
  may describe Placements for Activities or Groups.
- An **Operation** produces one terminal **Operation Receipt** and may reference
  one or more expiring **Reservations** before commitment.

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
