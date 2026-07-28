# Distribution and Release Evidence Requirements

Status: approved design baseline for tasks 9.1–9.2

## Scope

This slice produces self-contained unsigned test packages for one declared v1
architecture on Windows, macOS, and Linux. It also creates machine-verifiable
dependency, integrity, provenance, and update metadata and a signing boundary
that can be filled by release credentials without changing package contents.

It does not claim installer UX, signing, notarization, upgrade, uninstall,
launch-at-login, native accessibility, or physical-machine acceptance.

## D1 — Declared targets and package shape

- The automated baseline shall build `win-x64`, `osx-arm64`, and `linux-x64`.
- Every package shall be self-contained and contain only its target RID assets.
- Windows shall use a ZIP rooted at `Flowspan/`; macOS shall use a compressed
  tar archive rooted at `Flowspan.app/`; Linux shall use a compressed tar
  archive rooted at `flowspan/`.
- The macOS package shall contain a valid minimal application bundle with an
  explicit bundle identifier, executable, display version, and build version.
- Package names shall bind product, version, RID, and unsigned/signed state.

## D2 — Reproducibility and confinement

- Package input shall be a dedicated publish directory, never the repository.
- Enumeration shall be ordinal, paths shall be normalized and relative, and
  links, traversal, duplicate paths, devices, and unsupported file kinds shall
  be rejected.
- Archive timestamps shall come from one UTC `SOURCE_DATE_EPOCH`; permissions
  shall be fixed by package role rather than inherited from the host.
- Repeating seal with identical bytes and metadata shall produce identical
  archives and metadata on the same supported toolchain.
- Every included file shall have a SHA-256 digest in a canonical file manifest.

## D3 — Signing boundary

- Preparation and sealing shall be separate phases. A platform signer may
  modify only the prepared staging tree between those phases.
- Unsigned CI packages shall be named and marked `unsigned-test-artifact` and
  shall never be accepted as release packages.
- Signed sealing shall require a structured verification report that binds the
  target, signer identity, verification tool, timestamp, and staged-tree digest.

## D4 — Supply-chain records

- The Desktop lock graph and a committed selected-RID .NET host/runtime-pack
  lock shall be the dependency sources.
- Build-time NuGet packages that execute during distribution publish shall have
  committed content hashes verified before execution.
- An SPDX 2.3 JSON SBOM shall list the application and every direct, transitive,
  and RID-specific NuGet dependency with version, package URL, archive digest,
  and declared license metadata when available.
- A canonical license report shall list Flowspan and every SBOM dependency.
  Missing, non-standard, invalid, or file-only license declarations shall be
  explicit review findings, never inferred.
- `SHA256SUMS` shall cover every distributed archive and companion record.
- SLSA-compatible provenance shall bind source repository, source commit,
  builder identity, invocation, target, package digest, and build timestamp.
- Update metadata shall bind channel, version, minimum supported version,
  package URL, size, digest, RID, and signature state without enabling an
  updater in this slice.

## D5 — Verification and CI evidence

- A verifier shall reject malformed, non-canonical, mismatched, missing,
  unlisted, or extra package files and companion records.
- Each hosted target shall restore its locked RID graph, publish self-contained,
  prepare, smoke-run the packaged executable in explicit TEST MODE, seal,
  verify, and upload a uniquely named artifact.
- A repeated deterministic packaging test shall compare archive and companion
  record bytes, not only their parsed meaning.
- Package jobs shall run after the ordinary test job on the matching OS and
  shall retain logs and outputs for exact-commit evidence.
- Release evidence shall distinguish hosted runner-image results from signed,
  notarized, installer, native accessibility, and physical-machine results.

## D6 — Bounds and failure behavior

- Version, channel, repository, commit, target, file count, path length,
  individual file size, and total package size shall have explicit bounds.
- Invalid input shall fail before replacing a completed output directory.
- Temporary plaintext signing material is outside this tool and shall never be
  accepted as package input or copied into output.
- Diagnostics shall identify the failed release field or relative path but
  shall not print file content, secret values, or credential material.

## Acceptance boundary

Automated completion closes the engineering and hosted-artifact portions of
tasks 9.1–9.2 only. Task 9.3 remains mandatory for real signing, notarization,
installation, upgrade, launch, uninstall, permissions, accessibility, LAN, and
physical-device behavior on supported machines.
