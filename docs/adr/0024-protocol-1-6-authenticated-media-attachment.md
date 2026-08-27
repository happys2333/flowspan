# ADR 0024: Protocol 1.6 authenticated media attachment

- Status: Accepted
- Date: 2026-08-20

## Context

ADR 0022 froze protocol 1.5 Remote Window control messages, purpose-separated
media keys, encrypted media frames, and resource limits. The current loopback
tracer can place a second stream beside a control stream only because the test
passes keys and bindings directly. A production listener still needs to classify
that stream and attach it to exactly one live authenticated control connection.

Extending the existing `FSH1` authenticated-session hello would create two
meanings for one parser and would require a second identity handshake to recover
keys already derived by the control connection. Opening another unauthenticated
port would add discovery, firewall, and ownership ambiguity. Adding attachment
semantics under negotiated 1.5 would mutate frozen compatibility evidence.

## Decision

Protocol 1.6 adds a Remote Window media-route feature. Protocol 1.5 retains its
unchanged control and encrypted-frame formats but cannot attach a production
media stream. Peers continue to negotiate 1.0-1.6 explicitly.

An attached stream begins with a distinct `FSM1` envelope on the same published
listener as pairing and authenticated control. A small fixed, bounded clear
prefix declares only the attachment format, negotiated version, request kind,
zero flags, and an unpredictable live route ID. The route ID is a locator, not a
credential; it is process-local, omitted from logs and diagnostics, expires, and
is invalidated with its owning control connection.

The request and acknowledgement have fixed lengths of 200 and 232 bytes. The
registry defaults to 32 live routes with a 128 hard limit; a route lives for 30
seconds by default and at most two minutes. Two independent replay histories each
have a 512-entry hard limit: initiator-nonce fingerprints and consumed route IDs.
Both are retained for the maximum two-minute route lifetime. A route ID enters
the consumed history atomically with successful registration, remains reserved
through claim, attachment, revocation, and cleanup, and cannot be registered
again during that window. An attached route continues to occupy its history slot
until attachment cleanup even after the replay window passes. If either history
is full, new registration or claim fails closed until an eligible entry expires
and no live owner requires it. The attachment transaction has a two-second
default and ten-second maximum timeout. Initiator and responder nonces are each
32 bytes.

The remaining request is encrypted and authenticated with the initiator-to-
responder Remote Window media key derived from the existing authenticated
transcript. It binds:

- protocol 1.6;
- local and peer Device IDs in direction;
- the exact live route ID;
- the exact Remote Window Session ID and Activity ID;
- a fresh unpredictable initiator nonce.

After the clear locator matches a pending route, the responder atomically reserves
that single-use entry before protected validation so concurrent or malformed
claims cannot reuse it. The route becomes attached only after every protected
binding and current-control-route check passes. The responder then returns a
media-key-protected acknowledgement that echoes the initiator nonce and
contributes a fresh responder nonce. Neither side admits a media frame until that
acknowledgement verifies.
Unknown fields, nonzero flags, invalid lengths, trailing bytes, version
downgrades, wrong direction, identity/session/Activity mismatch, expired or
revoked routes, repeated nonces, and a second claim fail closed and close the
candidate stream.

Timer-arm failure rolls back an unpublished admission and its route-history
reservation. Once admission succeeds, malformed protected input, cancellation,
timeout, revocation, or cleanup failure never makes that route reusable inside
the replay window. This bounded retention is deliberately fail closed for a
long-running process rather than evicting a still-security-relevant identifier.

The v1 registry is bounded and permits one attached media stream for each live
authenticated control connection. Revocation closes new claims before draining
the attachment. Capability and Driver authority remain on the serialized control
path and are rechecked independently; route possession grants neither.

There is no live rekey transaction for the attached media `SecureFrameSession`.
Its request/acknowledgement and media frames all consume the session's directional
epoch limits. Before either direction would exceed `2^20` protected frames, 1 GiB
of plaintext, or a sequence/epoch boundary, both the media attachment and owning
authenticated control connection must terminate. A complete fresh authenticated
control handshake then derives a new purpose-separated media session and its new
session-identifier route. Implementations must not exceed those limits, advance a
media epoch without an authenticated transition, or reuse a consumed route.

Task 4 freezes and tests the codec, registry, attachment handshake, fixtures, and
loopback behavior. Task 5 owns composition into the production Desktop listener
and Remote Window runtime, so this decision alone does not make native Remote
Window available.

The Task 5 Transport slice now classifies `FSM1` on the same production listener
as pairing and authenticated control and transfers each protocol-1.6 connection's
purpose-separated media session into one connection-owned directory entry. The
responder route, initiator connect, accepted attachment, media I/O, and control
registration share one failure domain: route expiry/revoke, attachment failure,
media fault, or disposal requests control stop and consumes the route.

Handshake reads and writes wait through a caller-cancellable wrapper even when a
stream ignores the supplied token. An owned wire buffer that remains borrowed by
such an operation is observed and cleared only after the underlying operation
settles. Session disposal also requests cancellation and closes the candidate
stream; cancellation and stream cleanup failures remain observable. The listener
always attempts accepted-attachment disposal after its handler and aggregates a
handler failure with a distinct cleanup failure instead of overwriting either.
This contract does not claim bounded return from a synchronous stream `Dispose`
implementation that itself blocks forever.

The small-budget exhaustion and complete fresh-control-handshake recovery path is
not yet composed. Until that testable epoch boundary exists, the Task 5 parent and
production Remote Window availability remain open.

## Alternatives considered

### Extend `FSH1` with another hello kind

This couples two independently bounded lifecycles to the identity handshake and
makes an initial prefix ambiguous to the listener. Rejected.

### Run media on a separately advertised port

This increases local discovery and firewall surface while still requiring an
authenticated binding to the control connection. Rejected for v1.

### Put attachment fields in clear text

Even when the route ID is unpredictable, clear Session, Activity, or Device IDs
would expose correlation metadata and permit unauthenticated mutation. Rejected.

### Change protocol 1.5 in place

The 1.5 fixtures are already compatibility evidence. Rejected; 1.6 makes support
and downgrade behavior explicit.

## Consequences

- Production peers prefer 1.6, while 1.5 remains valid for control-only Remote
  Window behavior.
- Listener classification gains a third fixed magic without reinterpreting
  `FSP1` or `FSH1`.
- Registry ownership and teardown become part of control-connection cleanup.
- Media budget exhaustion is a full authenticated reconnect boundary, not a
  transparent media rekey or route retry.
- Ordered TCP remains an implementation baseline, not physical interactive-
  quality evidence.
- Native capture, rendering, platform permission, packaged accessibility,
  physical-device, and independent security-review gates remain open.
