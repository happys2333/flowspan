# Flowspan v1 Threat Model

Status: living document; security implementation is not yet release-ready

Method: assets/trust boundaries plus STRIDE-oriented abuse analysis

## 1. Security objectives

Flowspan must prevent an untrusted or revoked peer from learning Activity
content, viewing a window, or injecting input. A paired peer receives only
explicit capabilities. Users can always see and locally stop active sharing.
Loss of connectivity, stale state, or adapter uncertainty must remove authority
rather than grant it.

## 2. Assets

- long-lived device identity private key and trust records;
- Activity descriptors, file content, window frames, clipboard-like context;
- keyboard, pointer, and other input authority;
- secure-input/protected-surface state;
- Scene definitions, operation journal, receipts, and diagnostics;
- peer names and network metadata that reveal device presence.

## 3. Trust boundaries

1. Unauthenticated LAN discovery traffic -> pairing candidate channel.
2. Candidate peer -> user-confirmed trusted identity.
3. Authenticated peer -> per-capability authorization.
4. Network bytes -> bounded protocol decoder.
5. Flowspan core -> native capture/input/credential APIs.
6. Normal window -> sensitive or protected surface.
7. Structured events -> logs, crash reports, or exported diagnostics.
8. Application process -> future relay, codec, or third-party dependency.

## 4. Adversaries and assumptions

- An attacker may join the LAN, spoof discovery packets, scan ports, replay
  messages, interrupt traffic, or race a pairing attempt.
- A previously trusted device may be stolen, compromised, or revoked.
- A peer may be honest but buggy and send malformed, oversized, duplicated,
  delayed, or contradictory messages.
- A local unprivileged process may inspect ordinary files and logs.
- The operating system, its credential store, and the local user account are
  trusted for v1. Defending against a fully compromised kernel is out of scope.
- The user may misread or accidentally approve a prompt; pairings and sharing
  indicators must therefore be specific and revocable.

## 5. Threats and required mitigations

| ID | Threat | Required mitigations | Verification |
| --- | --- | --- | --- |
| T01 | Spoofed or malformed discovery offer | Signed short-lived canonical offer, fresh nonce refresh, prompt withdraw, minimal metadata, current trusted-key binding, bounded TXT/record/address caches, reject self/loopback/multicast endpoints, never trust discovery alone | forged/expired/random TXT, canonical publication profile, refresh/withdraw, identity-change, batch-limit, and candidate-address tests |
| T02 | Pairing MITM | Bounded canonical `FSP1` messages; fresh nonces and role/version-bound transcript; verify both identity signatures before prompting; matching SAS on both endpoints; explicit signed dual confirmation followed by distinct signed completion proofs; local-only capability choice; desktop code-comparison acknowledgement with zero-capability default and one active prompt; whole-ceremony timeout; verify a claimed identity-change key before warning | golden/hostile codec, altered transcript/signature/confirmation/completion, reject, prompt/network timeout, identity-conflict, two-node desktop-decision loopback, cancellation/stale-prompt, and headless keyboard tests |
| T03 | Peer impersonation after pairing | Bind signed candidates to the current trusted key; treat an inbound hello's claimed Device ID only as unauthenticated trust-record routing input; authenticate every secure-session transcript against the selected trusted key; reload trust and compare the authenticated key again before session registration; block identity-key changes; require Device ID plus current fingerprint for desktop trust mutation so stale UI cannot affect a replacement identity; permanent rejection outranks network-change retry | claimed-ID/key-substitution, candidate-binding, stale-admin-snapshot, multi-peer loopback, and reconnect/registration-race integration tests |
| T04 | Replay/duplicate command | Session epoch, message/operation IDs, TTL, request digest, durable idempotency journal | property/fault tests |
| T05 | Capability escalation | Deny by default; typed independent capabilities; read immutable trust projections; apply the complete edited grant only through the coordinator; check before connect and again after authentication; revoke/downgrade persists first and drains every active handler that depends on removed authority | permission matrix, immutable-snapshot, conditional-mutation, stop-failure, and connection-registration race tests |
| T06 | Remote input after authority loss | Monotonic driver lease epochs, short expiry, local enforcement, emergency-stop epoch bump | lease model/property tests |
| T07 | Sensitive content capture | Continuous protection-state probe, fail closed on unknown/stale, visible pause/blank state | platform contract + native manual tests |
| T08 | Malformed/oversized or cross-protocol input | classify only bounded `FSP1`/hello and `FSH1`/hello initial frames; transfer the pre-read frame to exactly one decoder; enforce frame, depth, field, count, decompression, timeout, and allocation limits | hostile selector/codec corpus, wrong-family/kind rejection, and protocol property tests |
| T09 | Descriptor opens dangerous target | schema validation, URL scheme allowlist, safe filename handling, no source paths, confirmation where needed | adapter security tests |
| T10 | Secret leakage in logs | structured allowlist logging, redaction before sinks, no payloads/raw input/private keys | redaction canary tests |
| T11 | Journal tampering/rollback | restricted storage, authenticated records or database integrity, monotonic revisions, recovery conflict state | persistence tamper tests |
| T12 | Compromised dependency/update | lock files, dependency review, hashes/signatures, SBOM, signed artifacts and update metadata | CI supply-chain checks |
| T13 | Resource exhaustion | per-peer rate, concurrency and size limits; at most 128 total inbound connection slots, with independent bounded pairing and authenticated-session capacity; pairing messages limited to 4096 bytes and the whole pairing ceremony limited to two minutes by default/ten minutes maximum; bounded initial-family selection and handshake timeouts, queues, and DNS-SD record/instance/address caches; serialized reconnect loop with bounded backoff and coalesced network-change events; cancellation and backpressure | selector/pairing/session capacity and deadline tests, hostile input, load/fault/DNS-batch, timeout, and reconnect-churn tests |
| T14 | Invisible monitoring | foreground sharing indicator on every participant, no unattended defaults, audit receipt, immediate local stop | UI accessibility/e2e tests |
| T15 | Downgrade to unsafe fallback | authenticated feature negotiation; name degraded mode; explicit capability and confirmation | downgrade tests |
| T16 | Future relay reads content | application-layer E2E encryption independent of byte-forwarding relay | relay-as-attacker integration test |

## 6. Security state machine rules

- `Discovered` is never equivalent to `Paired`.
- `Paired` is never equivalent to capability-authorized.
- Capabilities are evaluated for every new operation and driver lease.
- Trust revocation removes authorization and active-session eligibility before
  waiting for shutdown; every affected registered session receives a stop
  request before the revocation call returns, with failures surfaced.
- Reconnection creates a new secure session and key epoch.
- Unknown identity, version, protection state, transaction outcome, or lease
  epoch cannot authorize capture or input.
- Emergency stop is local, synchronous at the platform boundary, and does not
  wait for a network/UI round trip.

## 7. Sensitive data retention

Descriptor payloads exist only as long as required by the operation/undo policy.
Receipts keep opaque IDs, kinds, sizes, hashes, outcome, and reason codes. Trust
and history deletion is local and immediate; peer notification is best effort.
Diagnostic exports are generated into a new user-selected file, scanned for
registered canary secrets in tests, and never uploaded automatically.

## 8. Platform-specific review gates

- **Windows**: secure desktop behavior, protected capture flags, DPAPI/Credential
  Manager scope, SendInput integrity levels, and emergency-stop hotkey.
- **macOS**: Screen Recording and Accessibility TCC behavior, secure input,
  Keychain access groups, hardened runtime, and notarization.
- **Linux**: Wayland portal consent, PipeWire node lifetime, RemoteDesktop portal
  revocation, Secret Service availability, and explicit risk messaging for X11.

Mocks prove core behavior only. Each native gate needs matching-runner tests and
documented real-machine exploratory evidence before v1 acceptance.

## 9. Security release blockers

- unreviewed production cryptographic protocol;
- plaintext identity private keys or Activity content;
- any path for hidden capture/input;
- emergency stop dependent on peer response;
- known critical/high dependency vulnerability without documented mitigation;
- missing negative authorization or malformed-input tests;
- diagnostics that can contain test canary secrets.
