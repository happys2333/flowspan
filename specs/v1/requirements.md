# Flowspan v1 Requirements

Status: approved product baseline, implementation in progress

Last updated: 2026-07-13

## 1. Problem

People working across several computers currently rebuild context by hand: they
reopen resources, move files, repeat navigation, and decide which machine owns
input. Flowspan must make that transition explicit and safe. Its unit of
continuity is an **Activity** (intent plus portable context), not a physical
screen and not an arbitrary application process image.

## 2. v1 scope

Flowspan v1 targets Windows, macOS, and Linux desktop computers. It provides:

- local-first device discovery, authenticated pairing, and direct encrypted
  connections;
- semantic handoff where an adapter can faithfully describe an Activity;
- an explicitly labelled remote-window fallback when semantic handoff is not
  available;
- handoff, move, replace, atomic swap, mirror, driver transfer, Activity
  Groups, and saved Scenes;
- capability grants, visible sharing state, emergency stop, sensitive-content
  protections, undo, and structured diagnostics.

The first executable slice is a deterministic two-node simulator. It exercises
the same domain and protocol state machines used by real transports and
platform adapters.

## 3. Actors

- **Owner**: the person who controls a trusted device and its private data.
- **Peer device**: another Flowspan installation paired by the owner.
- **Participant**: a person permitted to view or drive a mirrored Activity.
- **Adapter**: a platform/application integration that can capture or resume a
  semantic Activity.

## 4. Requirements and acceptance criteria

### R1 — Supported desktop platforms and identity

Each installation has a stable device identity, a human-readable name, and a
new identity fingerprint. Platform-specific behavior is behind testable
interfaces.

- R1.1: When Flowspan starts for the first time, it shall create a unique device
  identity without requiring an online account.
- R1.2: When the same profile starts again, Flowspan shall retain its device ID
  and trust relationships while rotating ephemeral session material.
- R1.3: While building on a supported runner, the repository shall compile and
  execute platform-neutral tests on Windows, macOS, and Linux.
- R1.4: When a platform capability is unavailable or permission is denied,
  Flowspan shall report the unavailable capability and an actionable recovery
  path rather than silently failing.

### R2 — Local discovery, pairing, and authorization

- R2.1: While two devices are on the same local network, when discovery is
  enabled, each device shall advertise only the minimum data needed to offer a
  connection and shall not advertise Activity content.
- R2.2: When an unpaired peer requests a session, Flowspan shall require a
  user-verifiable pairing ceremony before trusting the peer.
- R2.3: When both users confirm the same short authentication string, Flowspan
  shall persist the peer identity and the capability grant selected during the
  ceremony.
- R2.4: When either side rejects or times out the pairing ceremony, neither side
  shall gain a trust record or Activity capability.
- R2.5: When a trusted peer reconnects with a different identity key, Flowspan
  shall block the connection, preserve the current Trust Record, and surface an
  identity-change warning that distinguishes the trusted fingerprint from the
  conflicting or unavailable observed fingerprint.
- R2.6: When the owner revokes a peer or capability, new operations requiring
  that trust shall be rejected immediately and active sharing shall stop.

### R3 — Activity representation and semantic handoff

- R3.1: When an adapter captures a supported Activity, Flowspan shall produce a
  versioned descriptor containing its kind, display metadata, resumable
  context, origin, sensitivity, and integrity metadata.
- R3.2: When a target adapter accepts a descriptor, it shall validate the
  descriptor before opening external resources or changing target state.
- R3.3: When the target cannot faithfully resume an Activity semantically,
  Flowspan shall offer a clearly identified remote-window fallback and shall
  not describe the result as a native migration.
- R3.4: When neither semantic resume nor the permitted fallback is available,
  Flowspan shall leave the source Activity unchanged and explain why the
  operation cannot continue.
- R3.5: Flowspan shall never claim to transfer arbitrary process memory,
  unsaved application state, credentials, or unsupported application internals.

### R4 — Handoff, move, and replace

- R4.1: When a user hands off an Activity, Flowspan shall resume it on the
  target while leaving the source usable.
- R4.2: When a user moves an Activity, Flowspan shall not close or suspend the
  source until the target has acknowledged a successful resume.
- R4.3: When target resume fails during a move, Flowspan shall keep the source
  active and record a failed, retryable operation.
- R4.4: When a user replaces a target Activity, Flowspan shall preserve enough
  target state to offer undo before installing the incoming Activity.
- R4.5: When an operation ID is retried after lost acknowledgement, Flowspan
  shall return the recorded result without applying the operation twice.
- R4.6: When the target has acknowledged a move but source cleanup fails,
  Flowspan shall preserve the committed target, report a duplicate Activity as
  `CommittedWithWarning`, and shall not claim that the move completed cleanly.
- R4.7: While a user prepares Replace, when an authenticated source with local
  `activity.receive` requests target choices, the target shall require its
  current peer-relative `activity.replace` grant and return only a bounded,
  payload-free inventory of active, normal-sensitivity Activities that its
  adapters can preserve for undo and whose kind matches the incoming Activity.
  Each choice shall bind target ID, revision, descriptor digest, kind, title,
  and placement slot; sensitive, restricted, closed, incompatible, unsupported,
  and non-local Activities shall not be disclosed.
- R4.8: When a target Activity changes after inventory capture, Replace shall
  reject the stale ID/revision/descriptor-digest selection before capture,
  resume, or target mutation and require the user to refresh the preview.

### R5 — Atomic swap

- R5.1: When two eligible Activities are selected for swap, Flowspan shall
  validate both descriptors and reserve both endpoints before committing either
  replacement.
- R5.2: If either endpoint rejects or times out before commit, Flowspan shall
  abort both reservations and keep both original Activities active.
- R5.3: If connectivity is lost after commit begins, both endpoints shall use
  the durable transaction record to converge on committed or aborted state; the
  UI shall show `recovering` until the outcome is known.
- R5.4: Replayed prepare, commit, and abort messages shall be idempotent.

### R6 — Mirror and driver authority

- R6.1: When an Activity is mirrored, every participating device shall show a
  persistent, accessible sharing indicator naming the Activity and current
  driver.
- R6.2: While an Activity is mirrored, only the holder of the current driver
  lease shall be permitted to inject input.
- R6.3: When driver authority is transferred, the previous lease shall be
  invalidated before the new lease can inject input.
- R6.4: When a driver lease expires or its session disconnects, Flowspan shall
  stop accepting its input and return control to the configured safe owner.
- R6.5: View-only participants shall never receive an input capability.

### R7 — Activity Groups and Scenes

- R7.1: When a user creates an Activity Group, Flowspan shall preserve the
  explicit membership and stable ordering of its Activities.
- R7.2: When a user saves a Scene, Flowspan shall store desired Activity
  placement and policies without embedding secrets or ephemeral session keys.
- R7.3: When a Scene is applied, Flowspan shall present a plan, apply independent
  operations deterministically, and report partial completion per Activity.
- R7.4: When a Scene operation would replace existing work, Flowspan shall
  require the same preservation and undo rules as a direct replace.

### R8 — Local-first reliability

- R8.1: While peers share a reachable local network, Flowspan shall discover and
  connect them without an Internet service.
- R8.2: When the network changes or a peer restarts, Flowspan shall reconnect
  with bounded exponential backoff and restore only still-valid capabilities.
  The user-visible state shall distinguish waiting for a peer, authenticating,
  authenticated but idle, retrying, and a permanent security or policy block.
- R8.3: When protocol versions differ, peers shall negotiate a mutually supported
  version or reject the session with a structured compatibility error.
- R8.4: When messages are duplicated, delayed, reordered within an allowed
  transaction, or acknowledgements are lost, the operation state machine shall
  preserve its safety invariants.
- R8.5: Internet discovery and relay shall be represented by replaceable
  interfaces but are not v1 release requirements.
- R8.6: Launching Flowspan shall not implicitly open a LAN listener, browser, or
  advertisement. Trusted-session reconnect shall run only inside an explicitly
  enabled local-network lifetime and shall cancel and drain when that lifetime
  ends.

### R9 — Security and privacy controls

- R9.1: All peer operation payloads shall be end-to-end encrypted and bound to
  authenticated device identities; discovery metadata may remain unencrypted
  but shall be signed and minimized.
- R9.2: When a peer requests an operation, Flowspan shall enforce the stored
  peer, Activity, direction, and capability policy before exposing content or
  accepting control.
- R9.3: When secure input, a protected surface, or a configured sensitive window
  is active, Flowspan shall blank or pause capture and reject remote input as
  the platform permits.
- R9.4: When the user activates emergency stop locally, Flowspan shall revoke
  active driver leases, halt capture and input, and disconnect sharing sessions
  without waiting for a peer acknowledgement.
- R9.5: Flowspan shall redact secrets, raw input, content payloads, and private
  keys from normal logs and exported diagnostics.
- R9.6: Long-lived private identity material and trust records shall use the
  platform credential store where available; an explicitly degraded test or
  portable mode shall be visibly marked.

### R10 — Usability, accessibility, and localization readiness

- R10.1: When no privileged capability is used, Flowspan shall not request that
  permission during first launch.
- R10.2: Before requesting local-network, screen-capture,
  accessibility/input, or a similar privilege, Flowspan shall explain the
  feature, the data exposed, and how to revoke it.
- R10.3: Every destructive or privacy-relevant degradation shall be named in the
  confirmation surface and operation receipt.
- R10.4: Core flows shall be keyboard operable, expose accessible names and
  state, avoid color-only status, and respect reduced-motion and text scaling.
- R10.5: User-visible strings shall be externalizable even though v1 ships only
  one maintained language.
- R10.6: Before starting an explicitly enabled local-network lifetime, Flowspan
  shall show platform-specific prompt expectations, enumerate the discovery
  metadata visible on the LAN, provide a platform-appropriate revocation path,
  and require affirmative acknowledgement. Opening or canceling that review
  shall not start a listener, browser, advertisement, or reconnect worker.

### R11 — Undo, audit, and diagnostics

- R11.1: When an operation changes Activity placement or driver authority,
  Flowspan shall create a structured receipt with correlation ID, timestamps,
  participating devices, outcome, and redacted reason codes.
- R11.2: When undo is safe and its retention window is open, Flowspan shall offer
  an idempotent compensating action and clearly state what cannot be restored.
- R11.3: When a user exports diagnostics, Flowspan shall produce a structured,
  redacted bundle including versions, negotiated capabilities, operation state,
  and recent error codes.
- R11.4: A user shall be able to inspect and delete local trust, history, Scene,
  and diagnostic data.

### R12 — Maintainability, clean-room provenance, and release evidence

- R12.1: Domain, protocol, security, transport, platform, and UI responsibilities
  shall have explicit module boundaries and dependency direction.
- R12.2: Any design inspired by an existing project shall be documented at the
  level of public concepts; GPL implementation code shall not be copied or
  translated into this repository.
- R12.3: Every protocol/state-machine change shall include deterministic unit or
  property tests, and every network transaction shall have fault-injection
  coverage for its critical failure boundaries.
- R12.4: Every release candidate shall pass formatting, warnings-as-errors,
  lint/static analysis, unit, integration, protocol compatibility, security
  checks, and the Windows/macOS/Linux CI matrix defined by the test strategy.
- R12.5: When a platform behavior has not been executed on real hardware, the
  release evidence shall identify it as unverified rather than infer success
  from mocks or another operating system.

## 5. v1 non-goals

- Mobile, tablet, iPadOS, iOS, Android, and browser-only clients.
- WAN account service, rendezvous, NAT traversal, or hosted relay.
- Live migration of arbitrary process memory or kernel resources.
- Automatic synchronization of every file or application database.
- Unattended remote administration or hidden monitoring.
- Multiple maintained UI translations in v1.

## 6. Release-level acceptance

v1 is acceptable only when all R1–R12 criteria are implemented or explicitly
removed by an approved product decision, all mandatory checks in
`docs/release/v1-release-criteria.md` pass, and the evidence distinguishes local
macOS results from CI and real-machine Windows/Linux results. A green simulator
alone is not sufficient for declaring the desktop product complete.
