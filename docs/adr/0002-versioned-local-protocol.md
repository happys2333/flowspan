# ADR 0002: Versioned local-first protocol with durable idempotent operations

- Status: Accepted for the simulator and control plane
- Date: 2026-07-13

## Context

Flowspan must operate without an Internet service, recover from local-network
interruptions, negotiate different software versions, and support operations
whose safety differs: handoff can be retried, move must preserve the source
until acknowledgement, and swap must atomically change two placements.

## Decision

Use a versioned message protocol over an abstract ordered duplex stream.

- Control messages are length-prefixed canonical UTF-8 JSON with strict size
  and shape limits. Media/file data use separate bounded binary frames.
- Protocol negotiation selects a compatible major version and an explicit
  feature set before operation traffic.
- Within major version 1, the exact negotiated minor is the feature gate: 1.0
  carries the original non-Swap control set and 1.1 adds the six Atomic Swap
  messages. A 1.0 session never exposes or decodes Swap; peers advertising both
  versions retain a safe non-Swap downgrade path.
- Every command is addressed by an unguessable operation ID and includes a
  digest of its immutable request. Journals cache terminal results.
- Retry of the same ID and digest returns the same result. Reuse of an ID with a
  different digest is a protocol conflict.
- The current in-memory journal binds at most 4,096 distinct Operation IDs per
  instance. The first digest remains bound after a retryable result or handler
  exception. At capacity, an unknown Operation fails closed before its handler,
  while an already bound same-digest retry remains eligible. This bound is
  process-scoped evidence, not durable restart history.
- Move uses target-before-source commit ordering.
- Swap uses prepare reservations and a durable commit decision. Endpoint state
  converges from its journal after lost messages.
- Clocks, IDs, randomness, transport, and journal are ports so simulation can
  enumerate failure boundaries deterministically.

## Why JSON for v1 control traffic

Control volume is low; transparent fixtures and diagnostic reproducibility are
more valuable initially than compact encoding. The codec boundary permits a
future binary format. Canonical encoding and digests are implemented by one
codec rather than assuming ordinary JSON serialization is canonical.

## Safety invariants

1. A failed move never destroys the only acknowledged Activity.
2. A swap never commits only from a prepare request.
3. A terminal operation outcome never changes.
4. An operation ID never denotes two requests.
5. Input without the highest unexpired driver lease is rejected.
6. Unknown peer, capability, protocol version, or protection state fails closed.

## Consequences

- A durable journal is part of correctness, not merely observability.
- Protocol fixtures and compatibility tests are required before message
  schemas change.
- Every minor feature addition requires frozen canonical frames and an
  old-minor fail-closed or compatibility test; protocol 1.1 freezes all six Swap
  frames and their SHA-256 hashes.
- Cross-device atomicity can remain `recovering` during a partition; the UI must
  represent uncertainty rather than invent a result.
- Discovery and transport implementations can change without changing domain
  operations.

## Deferred decisions

The production mDNS library, storage engine, and cryptographic wire formats need
focused ADRs after their spikes. They must preserve these invariants.
