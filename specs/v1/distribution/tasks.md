# Distribution and Release Evidence Implementation Plan

- [x] 1. Freeze distribution requirements and decision
  - Define targets, package layouts, reproducibility, signing seam, supply-chain
    records, hosted evidence, bounds, and explicit real-machine exclusions.
  - Record the deterministic self-contained package decision in ADR 0020.
  - _Requirements: D1–D6_

- [x] 2. Lock RID-specific self-contained publishing
  - Declare `win-x64`, `osx-arm64`, and `linux-x64` and update every affected
    package lock without weakening ordinary locked restore.
  - Configure deterministic Release single-file publish without trim or
    ReadyToRun assumptions and freeze version/repository metadata inputs.
  - _Requirements: D1–D2_

- [x] 3. Implement bounded stage preparation
  - Add the .NET release tool, strict CLI model, target layouts, macOS bundle
    metadata, path/file/link bounds, fixed modes, and canonical file manifest.
  - Test valid target layouts plus traversal, links, duplicates, bounds,
    existing-output, wrong entry point, and source mutation failures.
  - _Requirements: D1–D3, D6_

- [x] 4. Implement deterministic sealing and verification
  - Add ZIP and tar.gz sealing, canonical extraction, repeated-byte equality,
    stage-tree verification, signing-report binding, and unsigned release guard.
  - Test tamper, extra/missing files, wrong modes, malformed archives, metadata
    drift, signature mismatch, and decompression bounds.
  - _Requirements: D2–D3, D5–D6_

- [x] 5. Generate supply-chain companion records
  - Read the locked RID graph and restored NuGet metadata to emit SPDX 2.3,
    complete license inventory, SHA256SUMS, provenance, and inert update data.
  - Freeze schemas and test dependency completeness, NOASSERTION handling,
    archive hashes, canonical JSON, and all cross-record bindings.
  - _Requirements: D4–D6_

- [x] 6. Compose the hosted package matrix
  - Add matching Windows/macOS/Ubuntu jobs for locked RID restore, publish,
    prepare, packaged TEST MODE smoke, deterministic reseal, verify, audit, and
    named artifact upload.
  - Keep release credentials absent and artifacts visibly unsigned.
  - _Requirements: D1–D6_

- [x] 7. Close automated distribution evidence
  - Run local gates and exact-commit hosted package jobs; download and verify
    all packages and companion records independently.
  - Mark 9.1–9.2 only after evidence lands; keep all task-9.3 real signing,
    notarization, install, accessibility, LAN, and physical-machine gates open.
  - Exact commands, package digests, artifact IDs, independent verification,
    TRX counters, repairs, and evidence limits are recorded in
    [deterministic distribution evidence](../../../docs/evidence/2026-07-28-deterministic-distribution.md).
  - _Requirements: D5–D6_
