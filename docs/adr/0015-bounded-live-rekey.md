# ADR 0015: Protocol 1.3 bounded directional live rekey

- Status: Accepted for v1 implementation; independent security review pending
- Date: 2026-07-16
- Decision owners: Flowspan maintainers
- Review gate: independent cryptographic/security review required before v1

## Context

Protocol 1.2 proves bidirectional possession of the handshake-derived traffic
keys but keeps one AES-256-GCM key per direction until disconnect. Flowspan needs
a bounded live transition before the AEAD usage margin is approached, with
deterministic behavior when both peers request an update or the connection fails
mid-transition.

The ordered direct-TCP stream already gives strict frame ordering. Sender and
receiver keys are independent, so forcing an artificial atomic two-direction
commit would add failure states without improving record ordering.

## Decision

- Protocol 1.3 is the first minor version with live KeyUpdate. Adding it to 1.2
  would violate the repository's exact-minor feature policy.
- The traffic keys remain direction-independent but every active update asks the
  connection to converge on one target epoch. The KeyUpdate is encrypted under
  the old key and is the last old-epoch frame; the next frame uses the target
  epoch and sequence zero.
- The canonical 10-byte `FSR1` plaintext carries a request flag and exact next
  epoch.
- The receiver accepts only an authenticated exact-next transition. It never
  tries multiple keys and rejects a new epoch before KeyUpdate or an old epoch
  after it.
- The next 32-byte key is HKDF-SHA-256 over the current directional key, salted
  by the session identifier and domain-separated by context, direction, and next
  epoch. The old key is erased on successful installation.
- A directional key protects at most 1,048,576 frames and 1 GiB of application
  plaintext. Protocol 1.3 reserves bounded transition overhead for KeyUpdate;
  lower protocol versions close and reconnect at the bound.
- A requested target epoch is satisfied when the local send epoch is already at
  or beyond it. This resolves crossed requests without an extra rotation or
  response loop; a target gap larger than one is fatal.
- A post-write cancellation, timeout, or interruption closes the channel and
  recovers through a fresh signed handshake. Partial rekey state is never resumed
  across connections.

The complete wire, key schedule, state rules, and acceptance matrix are in
`specs/v1/rekey/requirements.md` and `specs/v1/rekey/design.md`.

## Evidence and provenance

RFC 8446 sections 4.6.3, 5.5, and 7.2 document the public TLS 1.3 concepts that
motivate old-key KeyUpdate records, independent directional traffic secrets,
key-usage bounds, and deletion of superseded secrets:
<https://www.rfc-editor.org/rfc/rfc8446.html>.

Flowspan does not use TLS wire formats, labels, source code, or state-machine
implementation. `FSR1`, the target-epoch conflict rule, the `FSE1` epoch
integration, and all code/tests are clean-room Flowspan work.

## Alternatives considered

### Atomically switch both directions with proposal/ack/commit

This creates ambiguous crash boundaries and needs additional confirmation rounds.
Independent directional updates already preserve ordered-stream safety and make
simultaneous requests composable.

### Fresh ECDH inside every live rekey

It can add post-compromise recovery properties, but it expands authentication,
simultaneous-proposal, and denial-of-service surfaces. V1 uses a one-way traffic
key chain and treats fresh authenticated reconnect as the recovery boundary.

### Rotate only on reconnect

This is retained for protocol 1.2 compatibility, but it interrupts an otherwise
healthy long-lived session and does not satisfy live rekey for v1.

### Keep an unbounded `ulong` sequence

Nonce uniqueness alone is not the complete AES-GCM usage criterion. A conservative
frame bound is easier to audit and test than relying on counter exhaustion.

## Consequences

- The secure-frame protector becomes a mutable, locked epoch owner rather than a
  fixed epoch-one primitive.
- Internal KeyUpdate handling must be multiplexed below Activity control-message
  decoding and must never escape as an application message.
- A continuous receive loop is required while awaiting a requested peer update;
  timeout closes rather than leaving a half-transitioned channel.
- Protocol 1.3 needs golden fixtures, old-minor compatibility tests, property and
  I/O fault tests, exact-commit hosted evidence, and independent review.

## Revisit triggers

Revisit if independent review requires a lower usage bound, a different KDF
construction, explicit post-compromise recovery, datagram transport, or a
cross-connection resumable rekey protocol.
