# ADR 0022: Protocol 1.5 Remote Window control and purpose-separated bounded media

- Status: Accepted
- Date: 2026-08-08

## Context

The portable Remote Window controller now owns current Capability checks,
monotonic Driver authority, protection pause, and local Emergency Stop. It has
no authenticated peer schema or media transport. Reusing protocol 1.4 without a
feature gate would let older peers negotiate a version whose semantics they do
not implement. Sending high-volume binary content as canonical JSON would also
weaken bounds, logging discipline, and future transport replacement.

## Decision

Protocol 1.5 adds strict admission, Driver, input, disconnect, and state control
messages. Every body binds one unpredictable live Session ID, exact Activity,
authenticated host/participant, deadline/correlation, and applicable state or
lease epoch. Protocol 1.0-1.4 rejects these types.

Remote input stays on the reliable control path because current Capability,
protection, and exact lease checks must remain serialized through the local
input boundary. Results never echo input.

Video, audio, and cursor use a second binary stream protected by directional
AES-256-GCM keys derived from the authenticated transcript with the distinct
HKDF context `FLOWSPAN-REMOTE-WINDOW-MEDIA-V1`. Control and media do not share
keys, counters, framing, queues, or rekey state. The binary header binds Session
ID, Activity ID, kind, sequence, and chunk coordinates.

The normative portable limits are those in
`specs/v1/remote-window/design.md`: 64 KiB payloads, at most 16 video chunks,
8 frames/512 KiB per peer, 128 frames/8 MiB/15 peers per session, 512 frames and
32 MiB per second received per peer, and a 2-second default/10-second maximum
accepted-write timeout. Resource reservations are fail-closed and released on
every terminal path.

## Alternatives considered

### Put media in canonical control JSON

This reuses one codec but expands payload exposure, base64 overhead, head-of-line
blocking, and structured-log risk. It also couples media evolution to operation
fixtures. Rejected.

### Reuse control AEAD keys on a second stream

Separate framing alone does not separate nonce/counter domains or compromise
impact. Independent HKDF purpose material is small and removes that ambiguity.
Rejected.

### Introduce QUIC or a production codec now

QUIC streams and a real codec may improve measured quality, but they add a new
transport or native dependency before the protocol, budgets, and native capture
boundaries exist. The second ordered-stream port keeps either option open and
is sufficient for the bounded tracer slice. Deferred until physical measurement
justifies a dependency ADR.

### Treat Remote Window as protocol 1.4

That silently changes an already frozen feature set and makes downgrade behavior
ambiguous. Rejected.

## Consequences

- All current production peers prefer 1.5 after upgrade but retain explicit
  downgrade through 1.0-1.4.
- Control fixtures and media binary fixtures become compatibility contracts.
- A second stream needs routing to the authenticated live Session before native
  media can ship; unknown Session IDs fail closed.
- Ordered TCP is acceptable for the portable tracer, not proof of interactive
  quality. Physical latency/loss measurements may trigger a transport ADR.
- This decision does not close native capture/input/protection, Desktop UX,
  accessibility, permission, physical-device, or independent security-review
  gates.

## Implementation note

The later headless Desktop candidate now consumes the bounded controller state,
permission contract, and local Emergency Stop surface. This does not revise the
protocol decision or close native permission, capture/input/protection,
production codec/rendering, packaged accessibility, physical-device, or
independent security-review gates.
