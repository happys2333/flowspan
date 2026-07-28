# Distribution and Release Evidence Design

Status: approved design for tasks 9.1–9.2

## Design summary

`Flowspan.Release` is a .NET 10 console tool with `prepare`, `seal`, and
`verify` commands. It consumes a RID-specific self-contained Desktop publish,
builds a fixed staging layout, optionally admits an externally verified signing
report, creates a deterministic archive, and emits canonical companion records.
The same library surface is exercised directly by deterministic tests.

The initial hosted matrix is `win-x64`, `osx-arm64`, and `linux-x64`. This is an
explicit architecture baseline, not a claim that other architectures are
unsupported forever. Adding a RID requires its locked graph, native smoke, and
real-machine release evidence.

## Build inputs

The invocation owns these bounded values:

- semantic version and monotonically mapped numeric macOS bundle version;
- exact 40-character lowercase source commit and source repository URL;
- RID, channel, minimum supported version, download base URL;
- `SOURCE_DATE_EPOCH`, builder identity, and optional CI run identity;
- publish, stage, output, application-lock, runtime-lock, build-tool-lock, and
  NuGet-cache paths;
  and
- signing mode plus an optional verified signing report.

CI obtains the source timestamp from the committed Git object. Local invocations
must pass it explicitly. Wall-clock time, current username, absolute checkout
path, directory enumeration order, and archive-library defaults are not inputs.
Hosted CI maps its monotonically increasing run number into bounded
`major.minor.patch` components accepted by `CFBundleVersion`.

Every source project participating in the Desktop graph declares the three
Runtime Identifiers so ordinary solution restore can lock every referenced
project without a RID-specific restore. Publish uses Release, self-contained,
single-file, compression, deterministic symbols disabled, and no trimming or
ReadyToRun assumptions.

## Preparation

`prepare` rejects any existing stage path and requires the publish root to
contain exactly the declared single-file entry point. This excludes symbols,
temporary signing material, and unrelated assets before staging. It rejects
links and non-files, normalizes every relative path, applies count/
size/path bounds, and copies through a temporary sibling before atomic rename.
The Windows and Linux roots contain the published files directly. macOS uses:

```text
Flowspan.app/Contents/Info.plist
Flowspan.app/Contents/MacOS/<published files>
```

The tool writes canonical `.flowspan-stage.json` beside the package root after
all stage files are present. It freezes the bounded build inputs and an ordinal
prepared-file array containing path, length, mode, and SHA-256, but remains
outside the package. `seal` copies the staged root, optionally admits the bound
signature report, and then writes `flowspan-package.json` after all other
package files are present. The package manifest freezes version, commit, RID,
signature state, entry point, epoch, and an ordinal file array containing path,
length, mode, and SHA-256. It does not hash itself. `seal` verifies that its
pre-manifest signed-tree digest still matches the completed manifest.

## Signing seam

An unsigned test flow runs `prepare` then
`seal --signature-state unsigned-test-artifact`.
A credentialed release flow runs `prepare`, invokes platform-owned signing and
verification outside the tool, writes `flowspan-signature.json`, then calls
`seal --signature-state verified --signature-report ...`.

The report is evidence about platform verification, not a signature invented by
the packager. It binds RID, staged-tree digest, provider, signer identity,
verification command and version, UTC verification time, and evidence digest.
`seal` rejects `verified` without a canonical report whose RID and tree digest
match. CI has no release credentials and therefore emits only conspicuously
unsigned artifacts.

Sealing accepts only the prepared entry point and bundle metadata. Verified
macOS sealing may additionally admit `Contents/_CodeSignature/CodeResources`;
only the entry point may change, while bundle metadata must remain byte-identical.
All other post-signing paths are rejected. Stage and output paths cannot overlap.

## Deterministic archives

Windows uses `ZipArchive` with ordinal entries, fixed DOS-compatible timestamp,
no host extras, fixed UTF-8 names, and explicit regular/executable attributes.
macOS and Linux use `System.Formats.Tar` USTAR entries with fixed uid/gid, empty
owner/group names, fixed mode and modification time, then deterministic gzip.
Archive roots are included in every entry and empty directories are omitted.
Verification reconstructs the canonical archive from verified extracted bytes
and requires byte equality, rejecting container comments, extras, and gzip drift.

The stage manifest fixes `0644` for data and `0755` only for the declared entry
point and required native executables. Sealing rejects any stage path not in the
manifest, any changed byte, and any changed file kind. Two seals from the same
stage must be byte-identical.

## Supply-chain records

The tool combines the Desktop lock sections for `net10.0` and its selected RID
with a committed three-RID .NET host/runtime-pack lock. For each distinct package
and version it locates the restored NuGet archive in the configured global package
folder, checks the cache SHA-512, uses official `NuGet.Packaging` semantics to
verify the lock content hash for signed and unsigned packages, computes raw
archive SHA-256, and reads nuspec/license content only from the verified nupkg.
No network lookup occurs during record generation.

The single-file publish also consumes `Microsoft.NET.ILLink.Tasks`. A separate
canonical build-package lock freezes its official NuGet content hash, and CI
verifies the restored archive before any distribution publish executes it.

The SPDX 2.3 document describes the Flowspan archive, its selected .NET runtime,
and all application NuGet packages. Every dependency gets a purl and raw archive
digest. Official NuGet SPDX parsing admits only standard identifiers. URL,
file-only, absent, non-standard, or invalid declarations become explicit
`NOASSERTION` review states. The license report also lists Flowspan itself as a
review-required application entry until repository license policy is declared.

Companion records are canonical UTF-8 JSON with LF and one trailing newline:

- `<package>.spdx.json` — SPDX dependency and archive statement;
- `<package>.licenses.json` — complete license-review inventory;
- `<package>.provenance.json` — SLSA v1 in-toto statement;
- `<package>.update.json` — inert version/channel/download metadata; and
- `SHA256SUMS` — lowercase SHA-256 and exact file name, ordinal by name.

The update document is data only. It does not add network access, automatic
download, trust-on-first-use, rollback policy, or an updater to the product.

## Verification

`verify` parses every JSON document with duplicate/unknown-property rejection,
requires canonical re-encoding byte equality, checks all cross-document target,
version, commit, signature, source, builder, invocation, package-list, license,
size, and digest bindings, rehashes `SHA256SUMS`, and extracts the archive into a
bounded temporary directory for package-manifest verification. ZIP type bits and
timestamps must match; tar entries must be USTAR regular files with fixed owner,
mode, and timestamp. Traversal, links, devices, duplicates, extra files, wrong
metadata, and decompression limit violations are rejected before launch.

Verification does not treat a structured signing report as cryptographic proof.
Credentialed release automation and task 9.3 must run the platform verifier
again on the downloaded artifact before installation.

## CI flow

The ordinary test matrix remains the source-quality gate. A dependent package
matrix runs on matching hosted runners and performs:

1. ordinary locked restore of the committed multi-RID graph;
2. content-hash verification of executable NuGet build tooling;
3. Release self-contained single-file publish;
4. `prepare` into a clean staging directory;
5. packaged entry-point `--validate-composition` smoke with shared runtime
   discovery disabled in explicit TEST MODE;
6. unsigned `seal`, repeated deterministic seal comparison, and `verify`;
7. direct/transitive vulnerability audit; and
8. named artifact upload with a 14-day retention.

The package job never marks unsigned output as signed, never uses release
secrets, and never substitutes its hosted launch for native packaged-window,
accessibility, permission, installer, or physical-device evidence.

## Failure and publication model

Preparation and sealing write to fresh sibling temporary directories and move
only after all validation succeeds. Existing outputs are rejected rather than
merged or overwritten. A failure leaves no completed manifest, archive,
checksum set, update record, or provenance statement that could be mistaken for
a release candidate.
