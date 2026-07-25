# Activity Groups and Scene Plan Implementation Plan

- [x] 1. Freeze task-8.1 requirements and design
  - Define bounded explicit Group membership, version/revision semantics, Scene
    policies, exact Group expansion, secret-minimized schema, and non-goals.
  - Record the durable choice in ADR 0016.
  - _Requirements: GS1–GS5_

- [x] 2. Implement ordered Activity Groups
  - Add opaque Group and Scene identifiers.
  - Add immutable bounded Group creation and revision with exact-order,
    duplicate, null, overflow, defensive-copy, and redaction tests.
  - _Requirements: GS1, GS3, GS5_

- [x] 3. Implement typed version-1 Scene plans
  - Add typed source-disposition and destination-conflict policies.
  - Add individual and exact Group-derived plans with immutable ordering,
    revision, binding, invalid-policy, mismatch, bound, and redaction tests.
  - _Requirements: GS2–GS3, GS5_

- [x] 4. Implement the canonical local Scene codec
  - Emit the frozen compact UTF-8 property order and enum tokens.
  - Reject over-size/depth, missing, duplicate, unknown, mistyped, malformed,
    trailing, unsupported-version, bound, and secret-field inputs.
  - Freeze one canonical fixture and SHA-256 digest; prove canonical round trip.
  - _Requirements: GS3–GS5_

- [x] 5. Close task-8.1 delivery evidence
  - [x] Run locked restore, format, Release build, all 824 tests, Desktop TEST
    MODE composition, protocol-1.3 simulator, 24-project dependency
    vulnerability query, and diff checks on the local macOS host.
  - [x] Run the 21 focused Group/Scene domain tests and 9 codec tests in 20
    independent processes each; close Standards and Spec review with zero
    remaining findings.
  - [x] Push the implementation commit and verify its Windows, macOS, Ubuntu,
    Secret Scan, CodeQL, Gitleaks SARIF, and downloaded TRX evidence at exact
    SHA `d65e24790dc8b0cdaa6f32522e56a5611b57d2d8`.
  - The task-status commit carrying this closure must pass the same workflows
    before the status becomes effective.
  - Evidence: `docs/evidence/2026-07-25-activity-groups-scene-plans.md`.
  - Keep apply, repository/UI, physical-device, packaging, and v1 gates open.
  - _Requirements: GS5_
