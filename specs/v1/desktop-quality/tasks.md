# Desktop Quality and String Externalization Implementation Plan

- [x] 1. Establish the resource contract
  - Add the neutral-English embedded catalog and the small public lookup/format
    facade.
  - Prove neutral fallback, missing-key failure, and culture-aware formatting
    through the public facade.
  - Project resolved startup strings into Avalonia application resources.
  - _Requirements: DQ1, DQ2_

- [x] 2. Externalize static desktop XAML
  - Move visible text, control content, window title, tooltips, automation names,
    and automation help into named resources without changing English output.
  - Add a structural regression test that rejects new user-visible XAML
    literals and missing/blank references.
  - Re-run rendered-window behavior tests after each surface group.
  - _Requirements: DQ1, DQ3_

- [x] 3. Externalize view-model presentation
  - Migrate shell, pairing, trusted-device, Activity, Scene, local-data, and
    Remote Window presentation values and templates.
  - Preserve invariant protocol, reason-code, schema, filename, identifier, and
    diagnostic representations.
  - Add a conservative presentation-source gate and exact-output/culture tests.
  - _Requirements: DQ1-DQ3_

- [x] 4. Strengthen deterministic desktop-quality coverage
  - Preserve keyboard activation and automation metadata for every core flow.
  - Verify explicit text accompanies safety/status colors, larger text wraps or
    scrolls at the supported minimum, and interactive targets remain at least
    44 device-independent pixels high.
  - Reject animation/transition resources while v1 has no required motion.
  - _Requirements: DQ4_

- [-] 5. Verify and record the evidence boundary
  - Run locked restore, format, Release build, focused tests, full tests,
    composition validation, simulator, dependency audit, and repository scans.
  - Update the parent task and release evidence only for behavior actually
    proven by the exact commit and CI matrix.
  - Keep native screen-reader, high-contrast, focus-ring, font, scaling, and
    reduced-motion checks open until packaged real-machine evidence exists.
  - [x] Local macOS locked restore, format, warning-free Release build, 1,558
    tests, explicit TEST MODE composition, protocol-1.5 simulator, dependency
    vulnerability query, XML/resource audit, and diff checks pass for the
    current worktree candidate.
  - [ ] Record exact-commit hosted Windows/macOS/Linux CI, CodeQL, Secret Scan,
    package, and independently parsed TRX evidence.
  - [ ] Execute packaged real-machine screen-reader, focus, high-contrast,
    font/text scaling, and reduced-motion checks on Windows, macOS, and Linux.
  - _Requirements: DQ3-DQ5_
