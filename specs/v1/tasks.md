# Flowspan v1 Implementation Plan

Status legend: `[ ]` pending, `[-]` in progress, `[x]` complete. A checked task
means its linked evidence exists; it does not imply the entire product works.

## 0. Product and engineering baseline

- [x] 0.1 Record the approved v1 scope as EARS-style acceptance criteria.
  - Covers desktop scope, Activity semantics, safety, local-first behavior, and
    evidence honesty.
  - _Requirements: R1–R12_
- [x] 0.2 Define normative domain language and ambiguity rules.
  - _Requirements: R3–R8, R11_
- [x] 0.3 Record initial architecture and single-language technology choice.
  - _Requirements: R1, R8, R12_
- [x] 0.4 Create threat model, test strategy, clean-room policy, and v1 release
  checklist.
  - _Requirements: R2, R9, R12_

## 1. Reproducible headless workspace

- [x] 1.1 Pin the .NET SDK and create solution-wide deterministic, nullable,
  analyzer, warning, formatting, and package-lock settings.
  - _Requirements: R1, R12_
- [x] 1.2 Add domain, protocol, application, diagnostics, simulator, and test
  projects with enforced dependency direction.
  - _Requirements: R12.1_
- [x] 1.3 Add developer commands and CI jobs for Windows, macOS, and Linux.
  - _Requirements: R1.3, R12.4–R12.5_

## 2. Two-node semantic handoff slice

- [x] 2.1 Implement validated IDs, Activity descriptor metadata, sensitivity,
  revision, placement, capability, and receipt value types.
  - _Requirements: R3.1–R3.2, R9.2, R11.1_
- [x] 2.2 Implement in-memory node, adapter, journal, fake clock, and deterministic
  transport ports.
  - _Requirements: R4, R8.4, R12.3_
- [x] 2.3 Implement semantic handoff and target validation with a portable note
  adapter.
  - _Requirements: R3.1–R3.4, R4.1, R4.5_
- [x] 2.4 Ship a CLI simulator scenario and unit/integration tests proving two
  nodes can hand off while the source remains active.
  - _Requirements: R3, R4.1, R12.3_

## 3. Reliable operations and protocol

- [x] 3.1 Implement version envelope, bounded codec, negotiation, golden fixtures,
  and compatibility errors.
  - _Requirements: R8.3, R9.1, R12.3_
- [-] 3.2 Implement move/replace ordering, undo capsule metadata, and idempotent
  operation journal.
  - _Requirements: R4, R11.2_
- [-] 3.3 Implement atomic swap prepare/reserve/decision/commit/recovery state
  machines.
  - _Requirements: R5_
- [-] 3.4 Add generated transition/property cases and deterministic drop,
  duplicate, delay, disconnect, and journal-failure tests.
  - _Requirements: R4.5, R5, R8.4, R12.3_

## 4. Device security

- [ ] 4.1 Freeze identity, pairing transcript, SAS, ECDH/HKDF/AEAD, nonce, and key
  rotation formats in a reviewed ADR with test vectors.
  - _Requirements: R1.1–R1.2, R2, R9.1_
- [ ] 4.2 Implement identity/secret-store ports, pairing state machine, trust
  records, capability grants, revocation, and identity-change handling.
  - _Requirements: R2, R9.2, R9.6_
- [ ] 4.3 Implement encrypted framed sessions and negative tests for tamper,
  replay, downgrade, expiry, and key substitution.
  - _Requirements: R8.3, R9.1_
- [ ] 4.4 Implement Windows Credential Manager/DPAPI, macOS Keychain, and Linux
  Secret Service adapters with marked degraded-mode behavior.
  - _Requirements: R9.6_

## 5. LAN discovery and reconnection

- [ ] 5.1 Spike DNS-SD implementation choices and record dependency/security
  decision.
  - _Requirements: R2.1, R8.1, R12_
- [ ] 5.2 Implement signed minimal discovery offers and direct framed TCP
  transport.
  - _Requirements: R2.1, R8.1, R9.1_
- [ ] 5.3 Implement bounded reconnect/backoff and reauthentication across network
  changes and restarts.
  - _Requirements: R8.2_
- [ ] 5.4 Run physical two-device LAN tests and preserve evidence.
  - _Requirements: R2, R8, R12.5_

## 6. Mirror, driver, and protection

- [-] 6.1 Implement mirror lifecycle and monotonic expiring driver leases.
  - _Requirements: R6.1–R6.5_
- [ ] 6.2 Implement platform protection-state, capture, input, and emergency-stop
  contracts plus deterministic fakes.
  - _Requirements: R9.3–R9.4_
- [ ] 6.3 Implement Windows capture/input/protected-surface adapters and native
  evidence.
  - _Requirements: R1, R6, R9_
- [ ] 6.4 Implement macOS capture/input/protected-surface adapters and native
  evidence.
  - _Requirements: R1, R6, R9_
- [ ] 6.5 Implement Wayland portal/PipeWire and explicit X11 fallback adapters
  plus native Linux evidence.
  - _Requirements: R1, R3.3, R6, R9_
- [ ] 6.6 Add bounded media/file channels, backpressure, and hostile-peer resource
  tests.
  - _Requirements: R3.3, R6, R8.4, R9_

## 7. Desktop experience

- [ ] 7.1 Record Avalonia dependency/version decision and create the accessible
  desktop composition root.
  - _Requirements: R10, R12_
- [ ] 7.2 Implement unified device/Activity entry, progressive permission
  prompts, pairing, capability editing, and identity-change warnings.
  - _Requirements: R1, R2, R10_
- [ ] 7.3 Implement operation preview, named degradation, persistent sharing
  indicator, recovery, receipt, and undo surfaces.
  - _Requirements: R3, R4, R5, R6, R10, R11_
- [ ] 7.4 Externalize user-visible strings and verify keyboard, screen reader,
  scaling, contrast, and reduced motion.
  - _Requirements: R10_

## 8. Groups, Scenes, and lifecycle

- [ ] 8.1 Implement ordered Activity Groups and versioned Scene plans without
  secrets.
  - _Requirements: R7.1–R7.2_
- [ ] 8.2 Implement deterministic apply, per-Activity result, replace protection,
  and compensating undo where safe.
  - _Requirements: R7.3–R7.4, R11.2_
- [ ] 8.3 Implement inspect/delete/export for trust, history, Scenes, and redacted
  diagnostics.
  - _Requirements: R9.5, R11_

## 9. Packaging and v1 acceptance

- [ ] 9.1 Add reproducible Windows, macOS, and Linux packaging, signing hooks,
  SBOM, license report, provenance, checksums, and update metadata.
  - _Requirements: R1, R12_
- [ ] 9.2 Execute the complete CI matrix and preserve named artifacts/results.
  - _Requirements: R1.3, R12.4–R12.5_
- [ ] 9.3 Execute real-machine install, permission, LAN, security, accessibility,
  failure, and lifecycle matrix on all three OS families.
  - _Requirements: R1–R12_
- [ ] 9.4 Close every item in `docs/release/v1-release-criteria.md`, document known
  limitations, and only then mark the long-term Goal complete.
  - _Requirements: R1–R12_
