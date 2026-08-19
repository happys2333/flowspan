# ADR 0021: Fail-closed Remote Window Control Plane

- Status: Accepted
- Date: 2026-08-08
- Clarified: 2026-08-10, 2026-08-20
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
reduced to a stable payload-free reason code. Repeated or concurrent stop
attempts accumulate confirmations only within the current stop/session
generation, and every completed result projects that accumulated proof without
letting a later failed retry regress an earlier confirmation.

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

Native completion clarifies the controller's source dependency without changing
the descriptor decision below. The controller may consume a bounded
`RemoteWindowSourceReference`: an active semantic Activity adapts its ID, kind,
title, and host, while a generic native window receives an ephemeral Activity ID
and has no descriptor or semantic kind. Its source token, generation, and native
identity remain process-local. This permits exact generic-window sharing without
inserting a synthetic Activity into semantic operations, persistence, or wire
payloads; protocol 1.5 continues to bind only its existing Activity ID field.

## Concurrency clarification

Capture admission, Emergency Stop confirmation, and protection work are bound to
an explicit session generation. Safe protection cannot resume gates before
capture admission confirms for that generation. A late successful admission
invalidates older capture-stop proof and must be stopped again; only successful
cleanup for the current stop/session generation can authorize reset. Every
normal-operation waiter registers before it can queue, rechecks disposal after
acquiring the gate, and drains before synchronization is disposed.

Reset fails closed with `emergency_stop_in_progress` while any attempt in the
current stop generation remains. After all attempts finish, a later reset retry
requires all three accumulated confirmations for that same session generation;
no proof is reusable by a later session.

Capability removal commits participant and Driver-authority revocation before
calling the local peer-disconnect boundary. A failed peer disconnect remains a
peer-scoped pending cleanup that explicit disconnect or Capability
reconciliation retries without restoring authority; confirmation clears it.

Desktop Start cancellation is rechecked after the service boundary returns. A
successful result from a cancellation-ignoring service, or one returned after an
authoritative inactive/Idle observation, is stopped while it is still the
current session. If a replacement session has already begun on the same
controller, generation plus inactive-boundary provenance rejects the older
result without stopping or mutating the replacement.

Protection reconciliation remains bounded to eight attempts. Exhaustion leaves
capture blocked and `Unconfirmed` even if the final fail-closed pause call
succeeds. If stale protection work crosses into a terminal lifecycle, it closes
capture, input, and local sharing-session boundaries and retains all three
results, including session-disconnect provenance. Emergency reassertion follows
the same rule.

Presentation and service observers never own a safety lock. Desktop state uses
generation-bound atomic reducer facts under a short gate, invokes observers only
after releasing it, and performs immediate local Emergency Stop before any
potentially blocking cancellation, event removal, operation drain, or dependency
teardown. Permission-busy and associated command callbacks register an
external-boundary lease before notification; disposal requested synchronously
from that callback excludes its own lease from the drain so it cannot wait on
itself, while external callers still join complete cleanup.

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
