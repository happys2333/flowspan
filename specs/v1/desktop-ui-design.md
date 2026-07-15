# Flowspan v1 Desktop UI Design

Status: accepted evolving implementation specification for desktop tasks 7.1–7.3b

Requirements: R1–R4, R8–R12

## 1. Purpose statement

Flowspan's desktop entry lets a person understand which device is local, which
peers are trusted, which Activities can continue elsewhere, whether anything is
being shared, and who holds driver authority. The interface is an honest control
surface over incremental capabilities: unavailable pairing, capture, input, or
remote-window behavior is named rather than represented as working.

Task 7.1 proves a launchable, testable composition root, protected local identity
startup, visible safety state, and accessible control structure. It does not
claim completed pairing, physical-LAN discovery, sharing, native permission, or
three-platform real-machine behavior.

## 2. Aesthetic direction

**Industrial/utilitarian.** The shell resembles a trustworthy operations desk:
hard boundaries, compact labels, stable placement, visible state, and restrained
motion. Decoration never competes with Activity state or safety controls. There
are no decorative gradients, translucent glass cards, centered card stacks, or
color-only statuses.

## 3. Color palette

| Token | Value | Use |
| --- | --- | --- |
| Graphite | `#161A1D` | window background and dark text on alert controls |
| Steel | `#252B30` | navigation rail and working surfaces |
| Chalk | `#F2EFE6` | primary text |
| Safety amber | `#F5B700` | focus, warning, and current selection |
| Signal red | `#FF6B6B` | emergency-stop surface |
| Cool gray | `#AAB4BC` | secondary text, always paired with explicit labels |

Measured sRGB contrast ratios include Chalk on Graphite at 15.23:1, Chalk on
Steel at 12.45:1, Safety amber on Graphite at 9.72:1, Signal red on Graphite at
6.31:1, and Cool gray on Steel at 6.79:1. Native high-contrast themes and real
display behavior still require platform evidence.

## 4. Typography

- headings: `Bahnschrift SemiCondensed`, then `DIN Alternate`, then
  `DejaVu Sans Condensed`;
- body: `Segoe UI Variable Text`, then `Avenir Next`, then `DejaVu Sans`;
- identifiers and diagnostics: `Cascadia Mono`, then `Menlo`, then
  `DejaVu Sans Mono`.

Every stack names maintained fonts normally present on at least one target and
has a Linux-available final fallback. The design does not bundle or request
Inter, Roboto, Arial, Helvetica, generic `system-ui`, or emoji glyphs as icons.
Font presence, shaping, and fallback are verified on the real-machine matrix.

## 5. Layout strategy

At desktop width, a fixed local-identity and safety rail occupies the left side.
The working plane uses an asymmetric 7:5 split: Activity continuity receives the
larger region and trusted devices the smaller region. A separate safety band
keeps the sharing label and emergency stop visually independent and early in the
keyboard order.

Below the compact breakpoint, regions flow vertically in semantic order:
safety, local identity, Activities, then devices. Content wraps and scrolls
instead of clipping. The initial minimum window is 900 by 620 device-independent
pixels; acceptance also exercises increased text scale and a smaller logical
viewport before declaring the layout robust.

## 6. Information architecture

1. **Safety band** — explicit `Not sharing`, `Sharing`, `Recovering`, or
   `Protection blocked` text; current driver when relevant; emergency stop.
2. **Local identity rail** — device display name, stable Device ID, identity
   protection status, and an optional fingerprint disclosure.
3. **Activity workspace** — resumable Activity list and selected-operation
   preview; task 7.1 contains a truthful empty state only.
4. **Trusted-device workspace** — connected/trusted peers, capabilities, and
   warnings; task 7.1 contains a truthful empty state only.
5. **Recovery surface** — bounded failure reason and actionable recovery text,
   never exception details or secret-store payloads.

## 7. Task 7.1 interaction states

The shell begins in `Initializing identity` and asynchronously loads or creates
the local identity through the matching protected store. Success shows the
device name, stable ID, full fingerprint, and `Operating-system protected`.
The fingerprint can be disclosed with one keyboard-operable toggle so a person
can verify the complete value rather than trusting an ambiguous abbreviation.

CI composition validation uses an explicitly injected in-memory store and must
show `TEST MODE — identity is not persisted`. Production failure shows
`Identity unavailable`, keeps network/sharing work disabled, and provides a
sanitized recovery action. It must not create a plaintext fallback.

Until the emergency-stop service is implemented, the safety band truthfully
shows `Not sharing` and the stop control is present but unavailable with an
accessible explanation. The control must be wired to the local fail-closed stop
port before any production sharing path can become active.

## 8. Accessibility and input contract

- Controls have programmatic names; status values are represented in text and
  not by color alone.
- Visual order, reading order, and tab order agree. The identity disclosure is
  the first enabled control in task 7.1; the safety action retains a stable
  location.
- Keyboard focus uses a three-device-independent-pixel Safety amber outline with
  sufficient offset from control edges.
- Interactive targets have a minimum height of 44 device-independent pixels.
- Text may wrap and containers may grow; critical state is not placed in a
  fixed-height clipping region.
- Ambient animation is absent. Future motion must have a reduced-motion path.
- Visible labels remain present even when automation names are supplied.
- User-visible strings are centralized during task 7.4; adding a string directly
  to a later view is not a license to postpone its externalization.

Headless tests cover control-tree construction, bindings, programmatic names,
keyboard activation, truthful states, and a non-secret startup failure. Native
screen-reader, focus indication, scaling, contrast-mode, and reduced-motion
checks remain real-machine acceptance work.

## 9. Design self-audit

- Forbidden palette search: no purple, violet, indigo, fuchsia, or blue-purple
  gradient is part of the specification.
- Forbidden font search: none of Inter, Roboto, Arial, Helvetica, `system-ui`, or
  `-apple-system` is selected.
- Icon search: task 7.1 uses text and structural rules, no emoji or icon-only
  control.
- Layout audit: the desktop layout is rail plus asymmetric 7:5 workspace, not a
  centered card grid.
- Scope audit: empty states and disabled actions describe missing capability;
  they do not claim that pairing, sharing, or arbitrary application migration
  works.

## 10. Task 7.2a: incoming pairing confirmation bridge

The first task 7.2 slice connects the existing `IPairingDecisionSource` security
port to a desktop confirmation surface. It handles an inbound ceremony only
after the core has exchanged canonical hellos and verified the peer's transcript
signature. Discovery remains untrusted and cannot produce this prompt by itself.

The surface shows the peer's display name, Device ID, full identity fingerprint,
six-digit short authentication string, protocol version, and expiry. The owner
must explicitly confirm that the same code is visible on both devices before the
accept action becomes available. The initial Capability grant is empty. The only
grants exposed in this slice are `activity.offer` and `activity.receive`; neither
requests capture, accessibility/input, or another operating-system privilege.

Exactly one prompt may be active, matching the production listener's default
pairing capacity. A second concurrent decision request is rejected rather than
replacing the visible peer or borrowing its confirmation. Reject, peer
cancellation, deadline, view disposal, or a stale command clears the prompt and
cannot grant a Capability to a later request. Accept returns only the explicit
local grant to the pairing ceremony; Trust is still written solely after the
peer also accepts and both signed completion proofs verify.

This slice is complete only when a deterministic two-node ceremony proves that
both desktop decisions are required, the two sides may grant different local
Capabilities, and rejection leaves both Trust Stores empty. Headless control
tests must also prove keyboard operation and accessible names/state.

The following task 7.2 work remains separate and must stay visibly unavailable:
an unpaired discovery list, initiating a connection to a selected candidate,
persistent trusted-device enumeration, Capability editing/revocation, pairing
outcome history, identity-change warnings, and progressive native permission
education.

## 11. Task 7.2b: persistent trusted-device authority

The trusted-device workspace loads the protected Trust Store through the same
coordinator that will admit and stop peer sessions. It lists immutable peer
snapshots in stable Device ID order and distinguishes `No paired devices` from
`Trust Store unavailable`; neither state claims that discovery or a production
listener is running. Selecting a peer exposes its full display name, Device ID,
fingerprint, verification time, and current independent Capability grants.

The editor exposes all seven v1 grants with explicit labels:
`activity.offer`, `activity.receive`, `activity.replace`, `mirror.view`,
`mirror.drive`, `file.receive`, and `scene.apply`. Every option begins from the
persisted grant. Changing a checkbox changes only an unsaved draft; `Save
capabilities` sends the selected Device ID, the displayed fingerprint, and the
complete grant to the coordinator. A stale fingerprint, missing peer, cancelled
operation, protected-store failure, or session-stop failure is shown in text and
followed by an authoritative list refresh. A Capability grant does not itself
request capture or input permission and the UI says so.

Revocation is a two-step destructive action. `Review revoke` reveals the exact
peer and states that new operations will be rejected immediately and active
sharing will be asked to stop. Only the separately focused `Revoke device`
control performs the conditional coordinator mutation; `Cancel` returns without
changing Trust. Revocation has no claimed undo. If Trust was durably removed but
one or more session stop requests fail, the result must say that authorization
is removed while shutdown confirmation failed, rather than reporting either a
full failure or full success.

The selected peer and draft are discarded when that fingerprint is no longer
current. While a save or revoke is in progress, competing mutations are disabled
but reading and emergency-stop placement remain stable. Headless tests cover
stable ordering, exact grant round-trips, stale identity refusal, keyboard
selection and editing, revoke confirmation, accessible names, truthful empty and
failure states, and authoritative refresh. Native screen-reader behavior and
physical active-session shutdown remain separate acceptance evidence.

Window close is an asynchronous lifecycle boundary: it cancels pending Trust
Store initialization or mutation, waits for the admitted operation to drain,
then disposes the session coordinator, persistent repository, identity, and
pairing prompt before allowing the window to close. The UI thread does not
synchronously block on this cleanup.

## 12. Task 7.2c: explicitly enabled local pairing network

Flowspan does not open a listener, browse multicast DNS, or advertise merely
because the shell launched. The owner first activates `Enable local pairing`.
The action explains that Flowspan will become discoverable on the current local
network and may cause the operating system or firewall to request local-network
access. Identity and protected Trust must already be available. Enable, retry,
and close are serialized and cancellable.

After enable succeeds, the surface separately states that the local pairing
listener is available and whether any candidates are currently observed. DNS-SD
metadata for an unpaired candidate is always labelled `UNVERIFIED — PAIRING
REQUIRED`; it is not a trusted device, connected session, or verified display
name. The list shows the advertised name, Device ID, full advertised
fingerprint, endpoint, and offer expiry. Self offers, malformed/expired offers,
port disagreement, and unsafe addresses never appear.

Selecting `Pair device` pins one candidate snapshot and permits only one
outbound ceremony at a time. Before the existing desktop SAS prompt is opened,
the peer identity authenticated by the pairing transcript must match the
candidate's Device ID and advertised fingerprint and must verify the pinned
signed offer within its lifetime. A mismatch rejects without showing a code or
writing Trust. A candidate whose Device ID is already trusted with another key
is shown as `IDENTITY CHANGED — BLOCKED`, retains the current Trust Record, and
cannot start pairing. A current trusted identity is shown as already paired and
is not offered as a new pairing action.

Production enable owns one dual-stack TCP endpoint, the unified bounded inbound
listener, one DNS-SD browser/publisher adapter, timed signed advertisement, and
the unpaired-candidate projection. Incoming and outgoing ceremonies use the same
protected local identity, desktop decision source, and Trust coordinator as the
trusted-device editor. Successful pairing refreshes the authoritative trusted
list. The current Activity layer is absent, so an authenticated control channel
may remain idle but `NOT SHARING` stays truthful and no Activity capability is
exercised.

Network bind, browser, publication, pairing, and cleanup failures are sanitized
into a bounded reason plus recovery action. Partial enable is unwound: an
advertisement cannot remain published after the listener or browser failed, and
window close cancels and awaits listener, advertisement, discovery, and pairing
before disposing identity or Trust. If either background network loop exits
after enable, the surface leaves `LOCAL PAIRING ENABLED`, clears the listener and
candidate presentation, shows a fixed `LOCAL PAIRING UNAVAILABLE` recovery
message without exception details, and enables retry only after the failed
session has been detached. Headless and loopback tests prove state, binding,
trust refresh, cancellation, cleanup, background-fault, and retry contracts.
Physical multicast, firewall prompts, dual-machine SAS comparison, and native
permission text remain separate evidence.

## 13. Task 7.2d: trusted reconnect status and identity warnings

Trusted reconnect extends the explicitly enabled local-network surface; it is
not a new launch-time background service. When local pairing is off, every
listener, browser, advertisement, reconnect supervisor, and idle authenticated
handler is absent. Enable starts them as one owned lifetime, while Disable,
network failure, or window close cancels and awaits all of them before protected
identity or Trust is disposed.

The local-network surface lists one status for every current Trust Record. It
uses `WAITING FOR TRUSTED PEER`, `WAITING FOR INBOUND AUTHENTICATION`,
`AUTHENTICATING`, `AUTHENTICATED — IDLE / NOT SHARING`, `RETRYING LOCALLY`, or a
specific permanent policy/security block. It never shortens an authenticated
idle channel to `Connected`, and the persistent top-level `NOT SHARING`
  indicator remains unchanged. A peer without either local `activity.offer` or
  `activity.receive` is shown as policy-ineligible rather than repeatedly
  contacted. Saving a relevant grant reconciles supervisors; revocation or
  downgrade first changes Trust and drains authority through
  `TrustSessionCoordinator`, then removes or stops the reconnect projection.

For a permitted pair, the lexicographically smaller Device ID initiates and the
other waits on the shared inbound listener. Either direction updates the same
per-peer authenticated-idle presentation. Candidate arrival wakes only a
waiting/retrying connector; periodic offer refresh or a conflicting record does
not cancel an already authenticated current-key channel.

Any observed discovery record that claims a trusted Device ID with another
fingerprint creates a high-prominence `IDENTITY CLAIM BLOCKED` warning. It shows
the protected Trust fingerprint, the conflicting advertised fingerprint, and
explains that the record was rejected before connection and is not proof that
the trusted peer changed keys. The warning remains latched until local
networking is disabled so a short-lived record cannot disappear before review.
If authenticated handshake evidence reports an identity change without a safe
fingerprint, the warning explicitly says the observed fingerprint is
unavailable. No warning action mutates Trust; re-pairing or identity replacement
requires a later explicit design.

Deterministic tests cover connector ownership, all state projections, signed
current-key candidate binding, conflicting-record latching, retry wake-up,
capability reconcile, revoke/disable/close drain, inbound/outbound idle status,
sanitized permanent failures, and keyboard/screen-reader text contracts.
Same-host loopback can prove the encrypted idle channel composition. Physical
sleep/wake, interface churn, firewall behavior, multicast, and two-machine
identity-change observation remain real-machine acceptance evidence.

## 14. Task 7.2e: platform-specific local-network permission preflight

`LOCAL PAIRING OFF` offers `REVIEW LOCAL NETWORK ACCESS`; it does not directly
start networking. The review names the current platform, explains why Flowspan
needs LAN access, enumerates the minimized signed discovery fields visible to
other devices on that LAN, states that Activity content and Capability grants
are not advertised, describes the prompt/firewall behavior a user may see, and
gives the matching revocation path. Windows names private-network firewall
access, macOS names Privacy & Security > Local Network, and Linux explicitly
states that firewall/sandbox controls vary by desktop and distribution.

Opening or canceling the review must leave the listener, browser,
advertisement, and trusted-reconnect workers absent. `ENABLE ON LOCAL NETWORK`
stays disabled until the owner checks an explicit acknowledgement. A successful
enable hides the preflight while preserving `NOT SHARING`; explicit Disable
clears the acknowledgement so a new network lifetime requires a new review.
An enable or background failure reopens the already acknowledged review beside
the recovery action, allowing a bounded retry without pretending permission
was granted. Startup, review, and cancellation never probe or request
screen-capture or remote-input privileges.

Unit tests cover all three platform guides, the no-side-effect review/cancel
boundary, command gating, retry, Disable reset, and disposal during enable. A
Headless test must prove keyboard access and declared automation names. Hosted
matrix results prove selection and UI contracts only; real prompts, firewall
state, settings navigation, and revocation remain matching-machine evidence.

## 15. Task 7.3a: portable-note semantic handoff preview and receipt

The first operation surface supports one deliberately narrow Activity kind:
`workspace.note/v1`. A person creates a bounded plain-text note locally, selects
an authenticated trusted peer, reviews a semantic-handoff preview, and sends an
encrypted Activity transfer over that peer's existing authenticated control
session. The target validates the descriptor and its current local
`activity.offer` grant before adding the Activity; the source separately
requires its local `activity.receive` disclosure grant for that target. Success
leaves the source note active and shows a redacted receipt with correlation ID,
target, outcome, timestamp, and reason code. The receipt never repeats note text
or another descriptor payload.

The preview must say `SEMANTIC HANDOFF — SOURCE STAYS OPEN`, name the exact
descriptor kind, destination, sensitivity, and data being sent, and state that
Flowspan does not transfer process memory, unsaved application internals, or
credentials. `REMOTE WINDOW NOT AVAILABLE IN THIS BUILD` is a named capability
limit, not an offered fallback. In task 7.3a, Move, replace, swap, mirror, and
driver transfer remain unavailable and are not represented by enabled controls.
Because a handoff is additive, this slice offers no misleading undo action; the
receipt explains that each device owns its resulting copy.

Only an authenticated session registered for the selected Device ID can become
a target. The transfer envelope is versioned, bounded, correlation-bound, and
validated against the authenticated sender and negotiated protocol. The target
returns one bounded operation receipt. Unknown message types, mismatched
correlation or participant IDs, malformed descriptors, stale capability,
duplicate operation IDs with different content, disconnect, and cancellation
must fail closed without removing the source Activity or displaying payload
content in an error. An acknowledgement lost after target commit is shown as an
uncertain/recovering outcome rather than success.

The top-level safety band remains `NOT SHARING`: a one-shot semantic descriptor
transfer is not a live mirror or remote-control session. The UI exposes visible
labels and automation names for note creation, Activity selection, target
selection, preview, confirmation, and receipt. Deterministic application tests,
authenticated loopback tests, and Avalonia Headless tests are required before
the slice is called complete. Physical two-device LAN behavior remains task 5.4
and release evidence; hosted runners prove protocol, loopback, composition, and
platform-selection contracts only.

## 16. Task 7.3b: acknowledged semantic Move

The next operation surface exposes bounded `workspace.note/v1` Move without
changing the Handoff contract. Handoff and Move have separate bordered previews
and separate confirmation buttons. The Move preview must say
`SEMANTIC MOVE — SOURCE CLOSES AFTER TARGET ACKNOWLEDGEMENT`, name the selected
target, descriptor kind, and sensitivity, and explain that rejection, failure,
or an uncertain outcome leaves the source active. A user must never invoke Move
from the source-preserving Handoff preview.

The Move control is disabled until an active local Activity and authenticated
eligible target are selected, and while another Activity operation is busy. It
has an explicit automation name and help text describing target-first ordering,
is keyboard focusable, and activates through the standard keyboard path. The
shared target list and receipt use operation-neutral automation names; assistive
technology must not hear a Move result described as Handoff.

A verified committed receipt removes the closed source from the active Activity
list and reports that target resume preceded source close. Rejection, failure,
and acknowledgement loss retain the source. `SourceCleanupFailed` is visibly
`COMMITTED WITH WARNING` and says two active copies may exist; the target is not
rolled back. Receipt and undo text is operation-aware: Handoff explains its
source-preserving copy, committed Move says there is no automatic reversal and
requires a new Move to return, and an uncertain Move tells the user to inspect
both devices before retrying. The top-level state remains `NOT SHARING` because
Move transfers one descriptor and does not establish Mirror or remote input.

Deterministic Move ordering/fault tests, production desktop authorization and
peer-unavailable tests, an authenticated encrypted loopback success/rejection
pair, ViewModel projection tests, and Avalonia Headless keyboard/automation tests
gate this slice. These remain same-host and hosted-runner evidence; physical
two-device LAN, packaged accessibility, and arbitrary-application state transfer
are not implied.
