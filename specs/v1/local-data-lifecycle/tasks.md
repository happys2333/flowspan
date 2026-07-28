# Local Data Lifecycle Implementation Plan

- [x] 1. Freeze lifecycle requirements and design
  - Define bounded history, purpose-separated protection, redacted exports,
    diagnostics sources, Desktop lifecycle, and evidence boundaries.
  - Record the protected audit and whitelist-export decision in ADR 0019.
  - _Requirements: LD1–LD5_

- [x] 2. Implement protected operation history
  - Add the payload-store port, strict canonical codec, bounded repository,
    append/delete/clear semantics, fault poisoning, and plaintext zeroing.
  - Cover bounds, canonical reopen, strict negatives, and every save boundary.
  - _Requirements: LD1, LD6_

- [x] 3. Implement platform history stores
  - Add `FSOH`, a purpose key marker, and macOS/Linux/Windows key and payload
    stores with dedicated constants and unsupported-platform behavior.
  - Prove tamper/cross-purpose rejection, canary absence, purpose separation,
    cancellation, and native/off-OS contracts.
  - _Requirements: LD2, LD6_

- [x] 4. Implement redacted exports and file lifecycle
  - Freeze trust, history, and diagnostics whitelist projections.
  - Extend owner-only export storage with safe diagnostics enumeration and
    deletion restricted to fixed generated names.
  - _Requirements: LD3–LD4, LD6_

- [x] 5. Compose receipt ingestion and Desktop lifecycle
  - Inject the non-throwing persistent sink into production operation paths.
  - Add trust export plus history and diagnostics inspect/delete/export panels,
    shell initialization/disposal, and unavailable validation composition.
  - _Requirements: LD3–LD5_

- [x] 6. Close automated evidence
  - Run local gates, focused stress, Windows/macOS/Ubuntu CI, Secret Scan,
    CodeQL, and downloaded TRX verification; record exact evidence.
  - Keep physical/native, packaging, external review, and v1 release gates open.
  - _Requirements: LD6_
