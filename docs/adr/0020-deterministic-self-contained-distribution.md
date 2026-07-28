# ADR 0020: Deterministic Self-contained Distribution

- Status: Accepted
- Date: 2026-07-28
- Decision owners: Flowspan maintainers

## Context

Flowspan builds and tests on Windows, macOS, and Linux, but it has no end-user
package, release metadata, signing integration point, or package smoke test.
A portable `dotnet publish` on the macOS development host was measured at
564 MiB and included native assets for unrelated platforms. It is therefore not
an acceptable distribution shape or evidence for a platform package.

The v1 criteria require reproducible packages, signing hooks, SBOM, dependency
licenses, checksums, provenance, update metadata, and exact evidence while also
forbidding hosted or simulated results from being presented as real-machine
installation, signing, accessibility, or device evidence.

## Decision

Build self-contained single-file Desktop publishes for the explicit initial
matrix `win-x64`, `osx-arm64`, and `linux-x64`. Lock those runtime graphs and
package only on a matching hosted OS. Use ZIP for Windows, deterministic tar.gz
for macOS and Linux, and wrap macOS output in a minimal `Flowspan.app` bundle.

Add a .NET 10 `Flowspan.Release` tool with separate preparation,
external-signing, sealing, and verification phases. Its only third-party runtime
dependency is the centrally pinned official `NuGet.Packaging` library, used to
verify signed-package content hashes and SPDX expressions with NuGet semantics.
The tool owns path confinement, fixed metadata, deterministic archives,
canonical manifests, SPDX 2.3, dependency-license inventory, checksums,
SLSA-compatible provenance, and inert update metadata.

Ordinary CI emits only `unsigned-test-artifact` packages and smoke-runs their
entry point in explicit TEST MODE. A future credentialed release invokes native
signing between preparation and sealing and must provide a matching structured
verification report. The packager never claims that report is cryptographic
proof and task 9.3 re-verifies downloaded artifacts on real supported machines.

## Consequences

- Hosted packages are architecture-specific, bounded, and launchable without a
  separately installed .NET runtime.
- Host/runtime-pack changes become visible lock-file changes reviewed with source.
- The ILLink package that executes during single-file publish is content-locked
  and verified before distribution builds.
- Package metadata is generated without network lookup from the committed
  application/host/runtime locks and restored NuGet archives.
- CI artifact size and restore time increase, but unrelated platform native
  assets no longer ship in each package.

The initial RID matrix is a release baseline, not a permanent architecture
promise. Adding Windows Arm64, macOS x64, Linux Arm64, distro packages, or native
installers requires locked assets and matching native acceptance evidence.

## Alternatives considered

### Framework-dependent portable publish

This avoids runtime-pack locks but requires users to install the exact .NET
runtime and, in the measured output, retained unrelated native platform assets.
It is larger than expected, weakens launch reproducibility, and does not produce
an honest per-platform package.

### NativeAOT or trimmed publish

Smaller artifacts are attractive, but reflection and dynamic UI/native loading
need a separate compatibility effort. Size optimization does not justify
silently changing runtime semantics at the first distribution boundary.

### Platform-specific installer frameworks now

MSIX/MSI, DMG/PKG, AppImage, deb, and rpm each add policy, signing, upgrade, and
uninstall semantics. Archives provide an inspectable unsigned CI artifact and a
stable signing seam first. Installers remain mandatory real-machine work under
task 9.3 rather than being guessed from hosted runners.

### Third-party SBOM and packaging actions

Mature tools exist, but adding unpinned release executables would create a new
supply-chain dependency at the point intended to attest the existing graph.
The required formats and archive rules are small enough for a reviewed .NET
tool; future replacement remains possible behind the frozen output contracts.

## Evidence boundary

Green hosted package jobs prove locked publish, deterministic sealing, metadata
consistency, and command-line smoke on named runner images. They do not prove
signature trust, Apple notarization, installer integration, OS permission UX,
native screen reader behavior, launch at login, upgrade/uninstall, or physical
two-device operation.
