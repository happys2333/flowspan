# Protocol 1.3 Live Rekey Requirements

Status: approved v1 security scope, implementation pending

## Problem and scope

Flowspan's current authenticated session keeps one AES-256-GCM key per direction
for the lifetime of the TCP connection. Protocol 1.3 must rotate those traffic
keys before a bounded usage limit without exposing an application message under
an ambiguous epoch. It must preserve the existing protocol 1.0/1.1 compatibility
path and protocol 1.2 encrypted Finished behavior.

This slice covers the secure-frame key schedule, encrypted KeyUpdate transaction,
control-channel integration, deterministic fault model, and exact-version
compatibility evidence. It does not add a new identity ceremony or trust grant.

## Acceptance criteria

### RK1 — Negotiated feature boundary

- When both peers offer protocol 1.3, Flowspan shall negotiate 1.3 and enable
  live KeyUpdate only after both encrypted Finished frames verify.
- When a session negotiates 1.2 or older, Flowspan shall never emit or accept a
  KeyUpdate message. If that session reaches the per-epoch usage bound, it shall
  close and recover through a fresh authenticated connection.
- When a protocol 1.2 reader receives a protocol-1.3-only plaintext, it shall
  fail closed rather than interpret it as an application control message.

### RK2 — Directional monotonic transition

- While sending in epoch `N`, when an endpoint updates its traffic key, it shall
  encrypt exactly one canonical KeyUpdate with the epoch-`N` key before changing
  its sender to epoch `N+1`, sequence zero.
- While receiving in epoch `N`, when a valid KeyUpdate declares epoch `N+1`, the
  endpoint shall authenticate and validate the complete old-epoch frame before
  changing its receiver to epoch `N+1`, sequence zero.
- When a frame declares an old, repeated, skipped, zero, or otherwise unexpected
  epoch, Flowspan shall reject it without advancing epoch or sequence state.
- When a new-epoch frame arrives before its old-epoch KeyUpdate, Flowspan shall
  close the channel; it shall never probe both old and new keys.

### RK3 — Key derivation and erasure

- When deriving epoch `N+1`, Flowspan shall use HKDF-SHA-256 with explicit
  Flowspan domain separation, the session identifier, traffic direction, and
  next epoch bound into the derivation.
- When a transition succeeds, Flowspan shall erase the superseded directional
  key before the new epoch becomes available to application traffic.
- When derivation, encoding, encryption, transport, parsing, authentication, or
  transition fails, Flowspan shall close the channel and erase all owned session
  keys; it shall not continue on a half-transitioned connection.

### RK4 — Bounded usage

- Before encrypting 1,048,576 frames under one directional AES-GCM key, a
  protocol 1.3 sender shall reserve the final permitted old-epoch frame for
  KeyUpdate and rotate before sending more application data.
- When an implementation cannot complete the reserved KeyUpdate, it shall close
  the channel instead of exceeding the bound.
- Sequence and epoch exhaustion shall be explicit failures; neither counter may
  wrap.

### RK5 — Requested and simultaneous updates

- When a caller requests a full-connection rekey, Flowspan shall update its send
  direction and request that the peer update the other direction within a
  bounded deadline.
- When a peer requests an update from an epoch already superseded locally, the
  existing later local epoch shall satisfy the request without an unnecessary
  extra rotation.
- When both peers request rekey concurrently, their directional updates shall
  commute: each endpoint shall reach matching send/receive epochs without
  deadlock, epoch rollback, or unbounded response ping-pong.
- While one local request is pending, duplicate local requests shall coalesce or
  fail with a structured busy result; they shall not create overlapping epoch
  ownership.

### RK6 — Interruption recovery

- When cancellation, timeout, EOF, or write failure occurs after a KeyUpdate is
  committed to either local direction, Flowspan shall fault and close that
  channel. Reconnect shall perform a fresh signed handshake and begin a new
  session at epoch one; a partial rekey shall not resume across TCP connections.
- While old-epoch application frames are already in flight on the ordered
  stream, the receiver may process them before the KeyUpdate. After the
  KeyUpdate, every later frame in that direction shall use the new epoch.

### RK7 — Evidence

- The KeyUpdate plaintext and next-key derivation shall have frozen golden
  fixtures and SHA-256 hashes.
- Deterministic model/property tests shall cover valid updates, automatic usage
  limits, replay, gap, early-new-epoch, late-old-epoch, malformed messages,
  simultaneous requests, counter exhaustion, and interruption at every critical
  I/O boundary.
- Real loopback integration shall prove application traffic before and after
  multiple rekeys and prove that a failed rekey is never returned as a usable
  authenticated channel.
- The final implementation, evidence, and task-status commits shall pass local
  formatting/build/tests, Windows/macOS/Ubuntu CI, Secret Scan, CodeQL, and
  independently summed downloaded TRX evidence.

## Non-goals

- Post-compromise recovery through a fresh ECDH exchange inside one TCP session.
- Resuming a partially completed rekey across disconnect or process restart.
- Rekey support for protocol 1.0, 1.1, or 1.2.
- Replacing the platform credential stores or long-lived device identity.

## Traceability

These criteria refine v1 requirements R8.3, R8.4, R9.1, and R12.3 without
changing the approved product scope.
