# Complete local audit of the 470d0f3 unsigned test packages

Date: 2026-10-04. Follow-up to the previously pending downloads in the
[hosted checkpoint](2026-10-04-cancellation-wgc-hosted-checkpoint.md).
The earlier handoff accurately recorded partial transfers; this later audit
completes them without claiming a signed or production release.

## Exact source and complete artifacts

All three artifacts bind implementation
`470d0f354f420b39bdc1ad5736bcb66acb01792d`, `codex/v1-foundation`,
[CI run 37208080273](https://github.com/happys2333/flowspan/actions/runs/37208080273),
run 237 / attempt 1. Complete outer-ZIP bytes and SHA-256 independently agree
with the artifact API and completed upload logs:

| RID / artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| linux-x64 / 11305887335 | 42093756 | `1b4ff8abbd9aa2295bfa4d0ad6e144e091b35617c0b7805f0840a323b88807da` |
| win-x64 / 11305192762 | 44097224 | `ecae08580ba7530411686dff0fb896b8eea44a03073ff9da42d3cc98c4b09aaa` |
| osx-arm64 / 11304918188 | 42925355 | `dc4e1184c9dbb10922e4c38f3272bcb79488e5165389e9f3cf8d30e3821704d1` |

Recoverable transfers used gh's internal authentication; no token or signed
download URL was printed or persisted. Old incomplete checkpoints remain
separately preserved rather than being relabeled as complete.

## Local verification actually executed

A frozen Release verifier whose source matches `470d0f3` accepted each separate
extracted RID directory, exit zero. Its DLL SHA-256 is
`1cae8c1a21a0c30bbeb4d7dfc147ad96d2706b02bede210e876952ae0145ab67`.
The root independently reran all three verifications against those complete
directories; no packaged executable was launched by this local audit.

Independent checks additionally confirmed:

- exactly six safe top-level members per outer artifact, with no duplicate or
  nested member substituted for an expected companion file;
- five exact SHA256SUMS records and hashes for the inner archive plus update,
  license, SPDX and provenance JSON;
- exact inner payload inventory, each length/hash, and no missing, extra or
  duplicate entry; manifests contain one payload for Linux/Windows and two for
  macOS, besides the manifest itself;
- version `0.1.237`, build version `0.2.37`, exact commit/RID/repository,
  channel `ci`, minimum version `0.1.0`, source epoch `1791122788`, invocation
  `/actions/runs/37208080273/attempts/1` and `unsigned-test-artifact` state;
- update/archive/SBOM/licenses/provenance cross-binding, including 38 inventory
  packages per RID and declared selected Host/Runtime inventory version
  **10.0.10**. This audit did not independently parse the single-file bundle
  to confirm its embedded runtime binary version. Hosted runtime 10.0.12 is
  a separate observed execution fact, not that inventory declaration.

Inner archives (distinct from the outer GitHub ZIPs):

| RID | Inner bytes | Inner SHA-256 |
| --- | ---: | --- |
| linux-x64 | 42092319 | `774f27106350d79f7270e30e31006da0dbc323a32fb7db04208e32a2485b1a5f` |
| win-x64 | 44079414 | `622c8c0150cfb4f20785171b017e53f2ed82b7bab2b14910033c72d786abb96e` |
| osx-arm64 | 42912789 | `de3f6892742f4120b1115007d336e0e5ee1c5e74dc89983f56af91d7efac31e4` |

Evidence and reproducible audit helper are retained under
`/tmp/flowspan-checkpoint-hosted-470d0f3/packages/` in `package-audit.json`,
`audit-packages.mjs`, raw APIs/logs, complete archives and per-RID extraction.
The verifier command is:

```sh
dotnet /tmp/flowspan-checkpoint-hosted-470d0f3/packages/verifier/Flowspan.Release.dll \
  verify --output <absolute-extracted-RID-directory>
```

## What remains unproven

The verifier proves internal record/byte consistency, not authenticated SLSA
provenance, trusted builder execution or real start/finish timestamps. Manifest
and provenance declarations were compared to external run metadata, but their
unsigned contents are not an attestation. The deterministic archive check is
not proof that independent executable builds are byte-identical.

All three license inventories retain `reviewRequired=true`; the Flowspan
application has no selected license expression and records
`application-license-review-required`. Other unresolved declared-file/missing/
legacy-URL entries are not legally cleared by a valid SPDX document.

This audit neither signs, notarizes, installs nor runs native product sharing.
Packaged TCC/accessibility/protection/input/independent Emergency Stop, physical
two-device behavior, application licensing and complete release/v1 acceptance
remain open. This result belongs to exact `470d0f3`; do not substitute it for
newer commits' package artifacts.
