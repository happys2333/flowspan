# Implementation plan

- [x] 1. Implement one terminal-without-sample RED→GREEN vertical through the
  internal owner API; close admission and notify exactly once. Record actual
  failing and passing executions, not a constant-shape assertion.
  - _Requirements: MDO3, MDO6_
- [x] 2. Extend one behavior at a time for early terminal/binding mismatch,
  blocked invocation retirement, late callback isolation, immutable address,
  slot exhaustion and direct/Task.Run self-join rejection.
  - _Requirements: MDO1-MDO6_
- [x] 3. Add and actually execute the no-capture synthetic Objective-C/GCD
  probe on the matching host in Debug and Release; verify no-native defaults
  and record Skip truthfully elsewhere.
  - _Requirements: MDO7, NR10.1-NR10.2_
- [x] 4. Verify complete solution, standalone format/build, all-OS CI and
  evidence-integrity review before claiming this prerequisite complete.
  - _Requirements: NR8, NR10_
  - Local Debug/Release each pass 2710/2710; final synthetic modes, strict
    CI fixtures and evidence-integrity review pass. Exact implementation
    `c27532dd809cb9d29f5ec01b45e37bbca5fcddd3` CI `37214730077` and CodeQL
    `37214730078` succeed. Downloaded Windows/macOS/Linux archives each have
    12 TRX / 2710 Passed, all non-success counters zero and matching case
    inventories; downloaded macOS raw synthetic output is byte-identical to
    local Debug/Release (61 callbacks / four retained bridges, no capture).
    The preserved audits were independently rerun with exit 0. See
    [local and hosted evidence](../../../../docs/evidence/2026-10-04-macos-delegate-ownership.md).

Task 6, production delegate composition and all native/physical/release gates
remain open even when this prerequisite is complete.
