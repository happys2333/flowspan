# Hosted regression reliability tasks

- [x] 1. Reproduce and repair the participant fixture dependency cycle; cover
  both legitimate cleanup winners with unconditional release/join.
  - _Requirements: HRR1, HRR4_
- [x] 2. Reproduce inbound server timeout/client EOF under controlled scheduling;
  inject the inbound clock and test exact expiry/caller cancellation/cleanup.
  - _Requirements: HRR2-HRR4_
- [x] 3. Freeze focused and solution Debug/Release, quality and complete source/
  runtime/qualified inventories; review the final diff.
  - _Requirements: HRR4_
- [ ] 4. Commit/push the implementation branch and independently verify fresh
  exact-SHA all-OS CI/CodeQL, complete artifacts and root saved-data replay.
  - _Requirements: HRR4, MEP8_

Local tasks1–3 are backed by the
[repair evidence](../../../docs/evidence/2026-10-05-enumeration-hosted-gates.md).
Task4 and overall HRR/MEP acceptance remain open until fresh hosted verification.
