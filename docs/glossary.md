# Flowspan Ubiquitous Language

These terms are normative in requirements, protocol names, diagnostics, and UI
copy. Avoid using *screen*, *session*, *share*, or *migration* without the more
precise term below.

| Term | Meaning |
| --- | --- |
| Activity | A unit of user intent plus portable context that can be captured, resumed, placed, or observed. It is not a process image. |
| Activity Descriptor | Versioned, validated representation of portable Activity context and metadata. |
| Adapter | Integration that captures and/or resumes a semantic Activity kind. |
| Semantic Handoff | Resume of an Activity through a target adapter using portable context. |
| Remote Window | Explicit fallback that keeps execution on the source and presents captured output and optional authorized input elsewhere. |
| Handoff | Resume on another device while preserving the source. |
| Move | Resume on another device, then close or suspend the source only after target acknowledgement. |
| Replace | Preserve eligible target state, then install an incoming Activity in its placement. |
| Swap | One atomic transaction that exchanges two Activity placements or changes neither. |
| Mirror | One Activity observed on multiple devices while execution remains on one host. |
| Driver | Device currently authorized by a live lease to provide input to a mirrored Activity. |
| Driver Lease | Short-lived, monotonic authority token; expiry or revocation stops remote input. |
| Placement | Desired device and presentation location of an Activity. |
| Activity Group | Explicit, ordered collection of Activities operated on as a user-visible unit. |
| Scene | Saved desired placements and policies for Activities or Groups; not a snapshot of process memory. |
| Capability | Narrow permission such as `activity.offer`, `activity.receive`, `mirror.view`, or `mirror.drive`. |
| Pairing | Interactive ceremony that verifies device identities and creates initial capability grants. |
| Trust Record | Local binding from a peer identity key to its name, verification state, and granted capabilities. |
| Operation | Idempotent requested change identified by a globally unique operation ID. |
| Operation Receipt | Redacted durable record of an operation, its participants, transitions, outcome, and possible undo. |
| Reservation | Time-bounded promise made during prepare that an Activity can participate in a transaction. |
| Emergency Stop | Local action that immediately disables capture and input, revokes driver leases, and disconnects active sharing. |
| Sensitive Surface | Window, secure-input state, protected content, or policy label that must block or blank capture/input. |
| Degradation | Named reduction from requested behavior, such as semantic handoff to Remote Window. |
| Local-first | Useful discovery and direct operation on a LAN without an Internet service; state is owned locally. |

## Discouraged ambiguous terms

- **Migrate app**: say *semantic move* or *Remote Window* and state what remains
  on the source.
- **Screen ownership**: say *Activity placement* or *driver authority*.
- **Connected**: distinguish *discovered*, *paired*, *authenticated*, and
  *operation-ready*.
- **Session**: qualify it as *transport session*, *secure session*, *mirror
  session*, or *application session*.
