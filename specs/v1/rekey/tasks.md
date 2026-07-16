# Protocol 1.3 Live Rekey Implementation Plan

- [x] 1. Freeze requirements and design
  - Define version boundary, wire shape, key schedule, usage bound, simultaneous
    request rule, interruption recovery, and evidence limits.
  - Record the durable decision in ADR 0015.
  - _Requirements: RK1–RK7_

- [ ] 2. Add canonical protocol and derivation primitives
  - Add the protocol-1.3 feature gate without advertising it in production yet.
  - Implement the bounded 14-byte KeyUpdate codec and frozen golden fixture/hash.
  - Implement the directional next-key derivation and fixed test vector.
  - _Requirements: RK1–RK3, RK7_

- [ ] 3. Make secure-frame epochs mutable and bounded
  - Replace the constant epoch with independent sender/receiver epoch state.
  - Rotate one protector atomically, reset its sequence, and erase its old key.
  - Enforce frame, sequence, and epoch limits with replay/gap/early/late negatives.
  - _Requirements: RK2–RK4_

- [ ] 4. Build the deterministic two-peer rekey transaction
  - Model requesting, responding, crossed requests, coalesced local requests,
    impossible future observation, and timeout/close outcomes.
  - Add property traces for repeated transitions and simultaneous requests.
  - _Requirements: RK5–RK6, RK7_

- [ ] 5. Integrate `SecureControlChannel`
  - Multiplex KeyUpdate below application `ControlMessage` decoding.
  - Reserve the last allowed old-epoch frame and rotate before application send.
  - Add bounded `RekeyAsync`, response handling, cleanup-failure preservation,
    and deterministic I/O fault injection.
  - _Requirements: RK2–RK6_

- [ ] 6. Integrate production protocol 1.3
  - Gate rekey by the authenticated negotiated version.
  - Prefer 1.3 in Desktop discovery/reconnect and the simulator only after the
    complete transaction is wired.
  - Preserve 1.2 Finished compatibility and close/reconnect at its usage bound.
  - Add real loopback traffic before/after repeated and simultaneous updates.
  - _Requirements: RK1, RK4–RK7_

- [ ] 7. Close delivery evidence
  - Run format, Release build, all tests, composition, simulator, dependency
    query, diff check, focused fresh-process repetitions, Standards, and Spec
    review.
  - Push implementation, evidence, and task-status commits; verify Windows,
    macOS, Ubuntu, Secret Scan, CodeQL, and downloaded TRX sums for exact commits.
  - Keep independent cryptographic review and physical two-device evidence open.
  - _Requirements: RK7_
