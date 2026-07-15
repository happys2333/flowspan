# ADR 0011: Bounded Replace with a Target-Owned Undo Capsule

Status: accepted for the `workspace.note/v1` tracer slice; desktop activation and
durable persistence remain pending

Date: 2026-07-15

## Context

Replace is intentionally destructive: it installs an incoming Activity in a
Placement currently occupied by another Activity. R4.4 and R11.2 require
Flowspan to preserve enough target state before that work and to offer an
idempotent compensating undo while the retention window remains open. A generic
Handoff/Move offer is insufficient because it contains no target Activity,
expected revision, target descriptor digest, or undo commitment.

Flowspan must also avoid suggesting that an Adapter can preserve arbitrary
process memory or unsaved application internals. The first supported kind is the
bounded `workspace.note/v1` descriptor, whose complete semantic state is the
validated descriptor itself.

## Decision

Replace uses a distinct application command and authenticated wire message,
`activity.replace`. The request digest binds all of the following:

- Operation and correlation IDs plus operation deadline;
- authenticated source and target device IDs;
- target Activity ID, expected revision, and descriptor digest;
- incoming Activity ID, kind, descriptor digest, payload digest, and payload;
- target Placement; and
- undo expiry.

The target rechecks its current `activity.replace` grant and exact target
snapshot. It then asks an `IReplaceActivityAdapter` to capture the target's
semantic state. The application validates that the captured descriptor is the
same Activity and descriptor digest, creates a target-owned capsule, and must
store it before calling incoming resume or changing the Activity catalog. A
capture, validation, or store failure returns a named rejection and performs no
incoming resume.

The current retention limit is 15 minutes. A capsule binds the original and
replacement Activities, target revision/digests, Operation/correlation, devices,
capture time, and expiry. The full original descriptor stays in the target
store. The authenticated result returns only a payload-free capsule reference:
capsule ID, target ID/revision/digest, incoming ID/digest, and expiry.

Undo is local to the target. It succeeds only while the capsule is unexpired and
the current Activity is still the exact replacement revision. Restore creates a
new revision rather than reviving the old revision number. A replay of the same
undo request returns its recorded result without invoking the Adapter twice; a
different request against an already consumed capsule is rejected. Expired,
unknown, consumed, and revision-conflicted capsules have distinct reason codes.

Replace uses `activity.replace`, not `activity.offer`. A lost result is reported
as acknowledgement-lost; the sender cannot infer whether the target committed.
The target operation journal makes an exact retry idempotent and rejects reuse of
the Operation ID with different bound content.

The desktop composition does not yet inject a Replace endpoint or expose a
Replace control. Activation is blocked until the UI can present a remote target
Activity snapshot, obtain an explicit destructive confirmation, show the exact
undo expiry, and execute local target undo. A protocol existing is not evidence
that this user flow is shipped.

## Consequences

- Replace cannot accidentally inherit Handoff/Move's source-preserving or
  source-cleanup semantics.
- A source never receives the target's preserved payload in a result or receipt.
- Adapters that cannot prove an honest semantic capsule fail before destructive
  work; Remote Window is not silently substituted.
- The current in-memory capsule store and undo journal prove process-lifetime
  behavior only. Crash/restart durability, protected local persistence, tamper
  detection, retention cleanup, desktop target selection, and desktop undo are
  mandatory later evidence before Replace can satisfy the v1 release criterion.
- Same-host loopback tests prove authenticated framing and state ordering, not
  physical LAN behavior or native application restoration.

## Verification

- Application integration tests cover capture/store failure, mismatched capture,
  target revision/digest conflict, successful replace, exact retry, successful
  undo, undo retry, expiry, and consumed capsules.
- Codec tests cover strict request/result round trips, target-snapshot tampering,
  payload-free capsule references, and participant/correlation binding.
- Session tests cover authenticated inbound/outbound Replace, exact pending
  result binding, acknowledgement loss, forged target metadata, and a real
  encrypted loopback connection.
