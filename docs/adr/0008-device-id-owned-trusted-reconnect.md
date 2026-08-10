# ADR 0008: Elect one Device-ID-owned trusted reconnect connector

- Status: Accepted for the desktop v1 local-network composition
- Date: 2026-07-14
- Amended: 2026-08-10 for Activity and Mirror any-of control admission
- Decision owners: Flowspan maintainers

## Context

After pairing, both desktop peers can browse the same signed DNS-SD offers and
accept authenticated TCP sessions. If both independently reconnect whenever
they observe the other, a healthy pair normally creates two symmetric idle
control channels. Duplicate channels complicate status, capability revocation,
future operation ownership, reconnect storms, and fault diagnosis.

The v1 slice must remain local-first and zero-configuration without adding a
coordinator service. It also cannot describe an authenticated but idle channel
as sharing, and it must not open networking merely because the application
launched.

## Decision

Within one explicitly enabled local-network lifetime, compare the canonical
string forms of the two stable Device IDs using ordinal ordering. The smaller
Device ID owns the outgoing trusted reconnect supervisor; the larger Device ID
waits on the already shared authenticated listener. Both inbound and outbound
handlers project into the same per-Trust-Record status.

The election grants no authority. Before TCP opens, the connector still:

- loads the current Trust Record;
- requires at least one locally granted control direction: `activity.offer`,
  `activity.receive`, `activity.replace`, `activity.swap`, `mirror.view`, or
  `mirror.drive`;
- reconstructs the peer public key only from Trust;
- verifies the signed, unexpired discovery offer with that key; and
- completes the authenticated transcript and post-handshake Trust registration.

The remote listener independently performs its own current-Trust, identity, and
any-of capability check. The channel exists only when both endpoints grant at
least one locally meaningful control direction. Admission does not authorize an
Operation: an outbound Semantic Handoff separately requires local
`activity.receive`, and its target separately requires local `activity.offer`
before accepting payload. Remote Window similarly requires `mirror.view` to
select or join a viewer and both `mirror.view` and `mirror.drive` for Driver
authority or input. A drive-only grant may retain an idle channel but grants no
viewer, Driver, or input authority. A conflicting discovery fingerprint is
blocked and warned about, never used to change the election or replace Trust.

Discovery change wakes a waiting/retrying elected connector. It does not cancel
an already authenticated current-key channel, which prevents periodic offer
refresh and unauthenticated conflicting records from becoming a trivial session
teardown mechanism. Network-address change, capability downgrade, revocation,
explicit Disable, background network failure, or window close still cancels and
drains through the existing supervision and Trust lifecycles.

## Alternatives considered

### Both peers always connect

This is simple locally but creates duplicate channels in the normal case and
requires a later duplicate-resolution protocol. It also doubles handshake and
retry work during interface churn.

### First connection wins

Racing both peers and closing one channel needs authenticated tie-breaking and
careful handover so simultaneous close decisions cannot discard both. The
stable Device IDs already provide a deterministic answer before connection.

### User-selected primary device

This adds configuration and a stale preference failure mode to a relationship
that can be decided consistently without user input.

### Use discovery name, address, or port

Names are user-editable and addresses/ports change across interfaces and
restarts. None is a stable authenticated ownership key.

## Consequences

- A healthy pair maintains at most one Flowspan-owned outgoing/incoming idle
  channel for this composition.
- Both peers must have local networking explicitly enabled, and at least one
  eligible Activity or Mirror control grant must remain valid on each side.
  Removing one alternative keeps an any-of session; removing the final
  alternative drains it.
- Device ID ordering is protocol-visible behavior and must stay canonical and
  covered by deterministic tests.
- A Device ID collision is not repaired by ownership logic; identity
  authentication and Trust binding still reject the wrong key.
- The channel may carry the bounded Activity control messages implemented by a
  later slice. When no live Mirror/driver session exists it remains labelled
  `AUTHENTICATED — IDLE / NOT SHARING`; a one-shot source-preserving Handoff is
  not represented as continuous sharing.

## Evidence and limits

Deterministic coordinator tests cover both election sides, all-of versus any-of
admission, Activity-to-Mirror alternative changes, final grant removal,
identity-warning latching, retry progress, revoke, and drain. Same-process
loopback cases run the production authenticated connector and listener with
Activity and Mirror-only grants and observe idle status on both peers. A factory
loopback further proves that `mirror.view` reaches the purpose-scoped view-only
inventory while `mirror.drive` alone does not. Hosted matrix results can prove
these contracts execute on Windows, macOS, and Linux runners; they do not prove
physical multicast, firewall, sleep/wake, interface churn, or dual-machine
behavior.

## Revisit triggers

Revisit when multiple simultaneous control transports per peer become a product
requirement, when a relay path needs independent path ownership, or when
measured failover shows that a single elected connector cannot meet the v1
recovery budget.
