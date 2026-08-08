# Remote Window and Mirror Implementation Plan

- [x] 1. Freeze requirements and control-plane design
  - Define honest fallback semantics, current Capability checks, monotonic lease
    authority, protection fail-closed behavior, local emergency-stop ordering,
    bounded input, and explicit native/media evidence limits.
  - Record the portable control-plane decision in ADR 0021.
  - _Requirements: RW1-RW8_

- [x] 2. Implement the portable live-session control plane
  - Add public bounded sharing snapshots, reason/result types, current
    authorization source, capture/input/session ports, and deterministic fakes.
  - Compose one Activity's `MirrorSession` through start, participant admission,
    role change, Driver transfer, disconnect, expiry, and ordinary stop.
  - Prove every peer use boundary re-reads current `mirror.view`/`mirror.drive`.
  - Local portable evidence: 69 Platform tests and the complete 1257-test
    solution run recorded in
    `docs/evidence/2026-08-08-portable-remote-window-control-plane.md`.
  - _Requirements: RW2-RW4, RW7_

- [x] 3. Integrate protection and local emergency stop
  - Gate every input attempt with a fresh safe protection observation.
  - Pause/blank capture and input on unsafe/unknown/stale state; resume only when
    both local gates confirm a fresh safe state.
  - Revoke authority before synchronous local capture/input/session stop, run all
    boundaries after failures, and expose payload-free confirmation results.
  - Add races for start, input, transfer, protection, and non-cooperative peers.
  - Monotonic protection revisions, bounded re-entrant convergence, late-start
    reconciliation, and Emergency Stop dominance have deterministic public-API
    regression coverage. Native and physical evidence remains in tasks 6-9.
  - _Requirements: RW4-RW7_

- [ ] 4. Freeze authenticated control and bounded media protocol
  - Add versioned strict messages for session admission, participant state,
    Driver Lease epochs, protection pause, and local disconnect.
  - Add encrypted bounded frame/cursor/input channels, backpressure, per-peer and
    per-session resource ceilings, hostile-length/rate tests, and no structured
    logging of payloads.
  - _Requirements: RW1-RW8_

- [ ] 5. Implement Desktop Remote Window and Mirror workflow
  - Add progressive capture/input permission preflights, explicit Remote Window
    naming, persistent sharing/Driver/protection state, keyboard operation,
    accessible emergency stop, and truthful boundary-failure recovery.
  - Keep source execution and every unavailable/degraded/native state visible.
  - _Requirements: RW1-RW6, RW8_

- [ ] 6. Implement Windows native adapters
  - Add Windows Graphics Capture, SendInput under current lease, secure-desktop
    and protected-content detection, local emergency hotkey, and matching native
    tests/manual evidence.
  - _Requirements: RW4-RW8_

- [ ] 7. Implement macOS native adapters
  - Add ScreenCaptureKit, Accessibility input, TCC grant/deny/revoke recovery,
    secure-input/protected-window detection, local emergency action, and matching
    native tests/manual evidence.
  - _Requirements: RW4-RW8_

- [ ] 8. Implement Linux native adapters and named degradation
  - Add Wayland portal/PipeWire and RemoteDesktop lifecycles, revocation and
    protection handling, plus explicit X11 capability/security degradation.
  - Add matching native tests/manual evidence across the supported compositor
    matrix.
  - _Requirements: RW1, RW4-RW8_

- [ ] 9. Close Task 6 evidence
  - Run property/fault/security/load tests and exact-commit Windows/macOS/Ubuntu
    CI, Secret Scan, CodeQL, and artifact verification.
  - Record physical two-device, native permission/protection, accessibility,
    emergency-stop-under-failure, and real-machine evidence without upgrading
    simulated or hosted claims.
  - Update parent tasks 6.1-6.6 and release criteria only for proven scope.
  - _Requirements: RW8_
