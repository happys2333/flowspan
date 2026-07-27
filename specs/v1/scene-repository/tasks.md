# Scene Repository Implementation Plan

- [ ] 1. Freeze Scene-repository requirements and design
  - Define repository bounds, atomic complete-candidate persistence, upsert
    and delete semantics, filesystem protection, the inspect/select/delete/
    export lifecycle, and export redaction.
  - Record the private atomic repository decision in ADR 0018.
  - _Requirements: SR1–SR4_

- [ ] 2. Implement the persistent Scene repository
  - Add `ISceneRepositoryStatePayloadStore`, `SceneRepositoryEntry`,
    `SceneRepositoryPersistenceException`, and `PersistentSceneRepository`
    with the strict envelope codec embedding exact canonical
    `ScenePlanCodec` bytes.
  - Cover empty/insert/revise/idempotent/stale/delete/reopen, 0/64/65
    bounds, byte-identical persistence, digest equality, every save-boundary
    fault with candidate non-publication and fail-closed poisoning, and
    envelope strictness negatives.
  - _Requirements: SR1, SR5_

- [ ] 3. Implement the protected platform stores
  - Add `ISceneRepositoryStateKeyStore`, `AuthenticatedSceneRepositoryStateFile`
    (magic `FSCR`), and macOS/Linux/Windows key and payload stores with
    repository-specific constants.
  - Prove magic, tamper and cross-purpose rejection, plaintext canary
    absence, purpose-separated constants, off-OS unsupported behavior, and
    pre-cancelled no-side-effect.
  - _Requirements: SR2, SR5_

- [ ] 4. Implement the redacted Scene export
  - Add `SceneRepositoryExport.EncodeRedacted` and
    `RedactedExportFile.WriteAsync` (create-new, owner-only, reparse-point
    rejection, strict file-name validation).
  - Freeze the redacted fixture; prove name/slot/Activity-ID/Device-ID canary
    absence, collision behavior, and failure reporting.
  - _Requirements: SR4, SR5_

- [ ] 5. Implement the Desktop repository lifecycle
  - Add `IDesktopSceneRepositoryService`, `DesktopSceneRepositoryRuntime`
    (open/degrade/reopen-after-poison), `SceneRepositoryViewModel`, the
    MainWindow Scene repository panel, shell wiring, and production/
    validation composition.
  - Integrate selection through `SceneApplyViewModel.SelectScene(plan, null)`
    only.
  - Cover unavailable degradation, list/inspect, select-for-apply, two-step
    delete, export path/failure strings, redacted exceptions, the
    single-Dispatch keyboard workflow with accessible names, and the
    persistent NOT SHARING indicator.
  - _Requirements: SR3–SR4, SR5_

- [ ] 6. Close Scene-repository automated evidence
  - Run local full-suite, focused stress repeats, composition validation,
    and vulnerability/format gates; verify implementation and task-status
    commits on Windows/macOS/Ubuntu CI, Secret Scan, CodeQL, and downloaded
    TRX sums; record the evidence document.
  - Keep Scene/Group creation UI, Group persistence, import,
    trust/history/diagnostics lifecycle, physical/native, packaging, and v1
    release gates open.
  - _Requirements: SR5_
