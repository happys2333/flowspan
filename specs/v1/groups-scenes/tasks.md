# Activity Groups and Scene Plan Implementation Plan

- [x] 1. Freeze task-8.1 requirements and design
  - Define bounded explicit Group membership, version/revision semantics, Scene
    policies, exact Group expansion, secret-minimized schema, and non-goals.
  - Record the durable choice in ADR 0016.
  - _Requirements: GS1–GS5_

- [ ] 2. Implement ordered Activity Groups
  - Add opaque Group and Scene identifiers.
  - Add immutable bounded Group creation and revision with exact-order,
    duplicate, null, overflow, defensive-copy, and redaction tests.
  - _Requirements: GS1, GS3, GS5_

- [ ] 3. Implement typed version-1 Scene plans
  - Add typed source-disposition and destination-conflict policies.
  - Add individual and exact Group-derived plans with immutable ordering,
    revision, binding, invalid-policy, mismatch, bound, and redaction tests.
  - _Requirements: GS2–GS3, GS5_

- [ ] 4. Implement the canonical local Scene codec
  - Emit the frozen compact UTF-8 property order and enum tokens.
  - Reject over-size/depth, missing, duplicate, unknown, mistyped, malformed,
    trailing, unsupported-version, bound, and secret-field inputs.
  - Freeze one canonical fixture and SHA-256 digest; prove canonical round trip.
  - _Requirements: GS3–GS5_

- [ ] 5. Close task-8.1 delivery evidence
  - Run locked restore, format, Release build, all tests, Desktop composition,
    simulator, dependency vulnerability query, and diff checks.
  - Run focused Group/Scene tests in independent processes and complete
    Standards/Spec review.
  - Push implementation, evidence, and task-status commits; verify Windows,
    macOS, Ubuntu, Secret Scan, CodeQL, and downloaded TRX sums.
  - Keep apply, repository/UI, physical-device, packaging, and v1 gates open.
  - _Requirements: GS5_
