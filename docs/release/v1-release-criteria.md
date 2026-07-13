# Flowspan v1 Release Criteria

Status: not met

No release may be called v1-complete until every mandatory item below is checked
with evidence. `Not applicable` needs an approved requirements change, not an
implementation convenience.

## Product behavior

- [ ] Semantic handoff works for every documented v1 descriptor kind. (R3, R4)
- [ ] Remote Window fallback is functional, visibly labelled, and permissioned
      on Windows, macOS, and Linux. (R3, R6, R9)
- [ ] Move preserves source state until target acknowledgement. (R4)
- [ ] Replace offers an honest undo capsule or blocks before destructive work.
      (R4, R11)
- [ ] Swap remains atomic across tested drops/restarts and surfaces recovery.
      (R5, R8)
- [ ] Mirror/view and driver transfer enforce lease ownership. (R6)
- [ ] Activity Groups and Scenes plan, apply, and report partial outcomes. (R7)
- [ ] Discovery, pairing, revoke, and reconnect work without Internet. (R2, R8)

## Security and privacy

- [ ] Cryptographic protocol and key storage have an accepted security ADR and
      independent maintainer review. (R2, R9)
- [ ] Every payload channel is authenticated and end-to-end encrypted. (R9)
- [ ] Capability matrix negative tests pass. (R2, R9)
- [ ] Sensitive/protected surface behavior fails closed on all platforms. (R9)
- [ ] Emergency stop is verified locally under peer/network/UI failure. (R9)
- [ ] Diagnostic canary/redaction tests and manual export inspection pass. (R9,
      R11)
- [ ] Dependency, secret, and static scans have no unmitigated high/critical
      finding. (R12)

## Reliability and compatibility

- [ ] Deterministic model/property suite covers all normative invariants. (R5,
      R6, R8, R12)
- [ ] Fault-injection suite covers every critical transaction I/O boundary.
      (R4, R5, R8)
- [ ] Protocol golden fixtures and supported-version negotiation pass. (R8)
- [ ] Sleep/wake, network change, peer restart, and disk/journal failure evidence
      exists. (R8, R11)
- [ ] Load/backpressure limits have measured results and safe failure behavior.
      (R8, R9)

## Desktop quality

- [ ] Feature permissions are requested progressively with correct rationale.
      (R1, R10)
- [ ] Every operation and degradation has a visible state, result, and recovery
      path. (R3, R10, R11)
- [ ] Keyboard, screen reader, scaling, contrast, and reduced-motion checks pass.
      (R10)
- [ ] User-visible strings are externalized; v1 language scope is documented.
      (R10)
- [ ] Trust, history, Scenes, and diagnostics can be inspected and deleted.
      (R11)

## Build and distribution evidence

- [ ] A clean checkout restores and passes format, Release build, and all tests
      using documented commands. (R12)
- [ ] Required CI checks are green on Windows, macOS, and Linux at the release
      commit. (R1, R12)
- [ ] Signed/notarized packages install, upgrade, launch, and uninstall on real
      supported machines. (R1, R12)
- [ ] SBOM, dependency licenses, checksums, provenance, and changelog are
      attached to the release. (R12)
- [ ] Crash recovery and local data migration are tested from the previous
      supported release candidate. (R8, R11)
- [ ] Known limitations explicitly separate simulator/CI evidence from untested
      real-machine behavior. (R12)

## Current evidence

- [2026-07-13 macOS headless foundation](../evidence/2026-07-13-macos-foundation.md):
  locked restore, format verification, warning-free Release build, 182 unit,
  integration, security, and platform-contract tests, simulator, and NuGet
  vulnerability query passed locally.
- [2026-07-13 macOS Keychain](../evidence/2026-07-13-macos-keychain.md):
  local Security.framework identity create/reload/delete, concurrent-add, and
  trust create/restart/update/revoke smoke passed with disposable Keychain items.
- [2026-07-13 hosted CI foundation](../evidence/2026-07-13-hosted-ci.md):
  locked restore, format, build, tests, simulator, secret scan, and CodeQL passed
  on committed slices through `2ef9961` using Windows, macOS, and Ubuntu hosted
  runners.

This is foundation evidence only. It does not satisfy physical-device, native
permission/hardware, packaging, independent security-review, or full product
acceptance gates.
