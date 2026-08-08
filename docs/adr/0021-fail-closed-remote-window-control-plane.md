# ADR 0021: Fail-closed Remote Window Control Plane

- Status: Accepted
- Date: 2026-08-08
- Decision owners: Flowspan maintainers

## Context

Flowspan already has an immutable Mirror/Driver Lease model and a portable input
policy, but no use-case boundary owns live capture, current Capability checks,
participant lifecycle, protection transitions, or emergency stop. Native API
work before that control contract would duplicate safety decisions three times
and make deterministic failure testing difficult.

Remote Window also has a high evidence risk: an in-memory fake can demonstrate
ordering but cannot demonstrate Windows secure desktop, macOS secure input, or a
Wayland portal. The architecture must make those evidence boundaries visible.

## Decision

Implement one memory-only `RemoteWindowSessionController` per Activity in the
portable `Flowspan.Platform` assembly. Compose the existing Domain
`MirrorSession` with narrow current-authorization, capture, remote-input, and
local-session boundaries. Keep `Flowspan.Application` independent of Platform;
native projects implement or adapt the outer ports.

Serialize normal lifecycle and input operations, but give protection blocking
and emergency stop synchronous local preemption paths. Emergency stop first
latches and revokes the lease under the state lock, then calls capture halt,
input halt, and local disconnect gates. Those gates have no peer-acknowledgement
operation. Each boundary reports confirmation independently and exceptions are
reduced to a stable payload-free reason code.

Version every accepted protection observation independently from the public
snapshot revision. Only a boundary result for the current protection revision
may publish Active or a confirmed Paused state. Reconcile re-entrant changes to
the newest observation within a fixed local bound, then remain fail-closed on
non-convergence; Emergency Stop always dominates stale pause/resume completion.

Use a small validated platform-neutral HID/pointer/scroll event model. Keep
frames, audio, cursor images, file bytes, native handles, raw platform messages,
and Activity descriptor payloads outside the control state and diagnostics.

Do not persist a live session or Driver Lease. Restart is a fail-closed sharing
termination and requires new authorization and capture admission.

## Consequences

- Capability removal, lease expiry, disconnect, protection uncertainty, and
  emergency stop converge on the same monotonic authority model.
- Driver transfer and normal input have a deterministic serialization point;
  emergency stop cannot be queued behind a hung peer or normal operation.
- Boundary failures are visible without falsely claiming capture or input ended.
- Native adapters remain thin and share portable contract/property/fault tests.
- Multiple Activity sessions remain possible as independent controllers; the
  Desktop composition layer must add a measured resource policy before exposing
  concurrent sessions.
- Live recovery after process restart is intentionally not supported in v1.

## Alternatives considered

### Put native lifecycle logic directly in each OS project

This would make emergency ordering, authorization races, and lease behavior
platform-dependent and leave only expensive manual evidence for critical
invariants.

### Put Platform types into Flowspan.Application

Application should own use cases, but the current portable protection policy and
platform capability vocabulary already live in `Flowspan.Platform`, which
depends inward on Application. Reversing that dependency would create a cycle or
force an unrelated storage-boundary migration. The portable outer control layer
keeps the existing dependency graph intact while Domain remains independent.

### Make emergency stop an asynchronous peer command

This violates R9.4: a silent, malicious, disconnected, or congested peer could
retain local capture/input authority or delay visible safety state.

### Persist and restore live sharing

Transport keys, peer presence, protection state, window handles, and Driver
authority cannot be safely reconstructed from a journal alone. Restoring them
would manufacture authority after uncertainty.

### Treat Remote Window as another Activity descriptor kind

That would blur source-hosted presentation with semantic migration and could
route live capture through descriptor persistence or operation diagnostics.

## Evidence boundary

Portable tests may prove state, ordering, bounds, current Capability use, fault
reduction, and redaction. Hosted CI may prove that those contracts compile and
run across the three OS families. Neither proves capture/input permission,
protected surfaces, emergency hotkeys, compositor behavior, physical networking,
or packaged accessibility. Tasks 6.3-6.6 and 9.3 remain open until matching
native and real-machine evidence exists.
