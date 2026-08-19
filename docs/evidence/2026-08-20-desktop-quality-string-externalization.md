# Desktop Quality and String Externalization Evidence - 2026-08-20

## Evidence status and boundary

Classification: **local**, **hosted portable contract**, **headless Desktop**,
and **unsigned package**.

Branch: `codex/v1-foundation`

Implementation commit:
`1c1ac2902f774435736d79bafeff4e6965426f4a`, based on
`d0e402a0df3ecc14fffa5a889e7a52dda00ed601`.

This record closes only the repository and deterministic headless portion of
desktop quality task 7.4. It proves the neutral-English resource boundary,
portable presentation behavior, source regression gates, and the existing
headless keyboard, automation, sizing, contrast, and no-required-motion
contracts on the named runners.

It does not prove native screen-reader speech or order, visible focus rings,
operating-system high contrast, font fallback, native text scaling,
reduced-motion integration, signed installation, or physical-device behavior.
Those checks remain open in task 7.4b and the v1 release criteria.

## Implemented contract

- Five embedded neutral `.resx` catalogs contain 1,114 unique, non-empty,
  commented keys. `DesktopText` discovers those catalogs, resolves with
  `CurrentUICulture`, formats display values with `CurrentCulture`, rejects
  missing, blank, or duplicate keys, and projects the catalog into Avalonia
  application resources before the main window is built.
- `MainWindow.axaml` resolves visible text, control content, tooltips,
  watermarks, automation names, automation help, and binding templates from
  the catalog. View models use the same facade for dynamic presentation text.
- Protocol versions, Capability IDs, reason codes, schema values, paths,
  hashes, identifiers, and round-trip diagnostic timestamps remain machine
  contracts. The resource migration does not move authorization, redaction,
  state reduction, or fail-closed behavior into the presentation layer.
- Structural tests reject literal XAML presentation and accessibility text,
  inline binding fallback/format literals, direct C# view-model prose, missing
  or dead resource references, blank or duplicate values, malformed composite
  formats, unnamed interactive controls, and newly declared animation or
  transition resources.
- The v1 language scope explicitly distinguishes the single C# implementation
  language from the single maintained neutral-English user-interface language.

The detailed requirements, design, and task traceability are in
`specs/v1/desktop-quality/`; the durable decision is ADR 0023.

## Local candidate gate

Environment:

```text
Host: macOS 26.6.1 (build 25G76), Apple Silicon, Asia/Hong_Kong
.NET SDK: 10.0.301
Branch: codex/v1-foundation
Verification date: 2026-08-20
Committed implementation: 1c1ac2902f774435736d79bafeff4e6965426f4a
```

The following commands ran against the clean candidate content that was then
committed unchanged as the implementation commit:

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore \
  --logger "trx;LogFilePrefix=Local" \
  --results-directory TestResults/desktop-quality-precommit
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable \
  --include-transitive --no-restore
git diff --check
```

Observed results:

- Locked restore and format verification passed.
- All 26 projects built in Release with 0 warnings and 0 errors.
- Structured XML parsing of 12 fresh TRX files reported 1,558 total, 1,558
  executed, 1,558 passed, and 0 failed or skipped. Desktop passed 402/402.
- Explicit TEST MODE Desktop composition passed.
- The deterministic simulator reported protocol 1.5, source preserved, target
  resumed, and atomic swap committed.
- NuGet found no known vulnerable direct or transitive dependency in any
  project.
- XML parsing found 1,114 unique resource keys across five catalogs, with zero
  blank values, missing comments, or duplicate keys. Resource/XAML audits and
  `git diff --check` passed.

The local TRX directory is ignored diagnostic evidence, not a committed release
artifact. This local gate does not stand in for another operating system.

## Hosted exact-commit evidence

Implementation commit `1c1ac2902f774435736d79bafeff4e6965426f4a` passed
[CI run `32283948196`](https://github.com/happys2333/flowspan/actions/runs/32283948196):

- Ubuntu test job [`96168955301`](https://github.com/happys2333/flowspan/actions/runs/32283948196/job/96168955301);
- macOS test job [`96168955324`](https://github.com/happys2333/flowspan/actions/runs/32283948196/job/96168955324);
- Windows test job [`96168955337`](https://github.com/happys2333/flowspan/actions/runs/32283948196/job/96168955337);
- Secret Scan job [`96168955160`](https://github.com/happys2333/flowspan/actions/runs/32283948196/job/96168955160);
- `linux-x64` package job [`96170248138`](https://github.com/happys2333/flowspan/actions/runs/32283948196/job/96170248138);
- `osx-arm64` package job [`96170248133`](https://github.com/happys2333/flowspan/actions/runs/32283948196/job/96170248133);
- `win-x64` package job [`96170247969`](https://github.com/happys2333/flowspan/actions/runs/32283948196/job/96170247969).

Every test job restored locked dependencies, verified formatting, built with
warnings as errors, ran the full suite, validated Desktop composition in
explicit TEST MODE, ran the protocol-1.5 simulator, and uploaded TRX evidence.
Every package job verified content-locked tooling, published and smoke-tested a
self-contained target, sealed and compared two reproducible unsigned outputs,
audited direct and transitive dependencies, and uploaded one test package.

Downloaded TRX and Secret Scan artifacts were parsed with XML and JSON parsers.
`Artifact digest` is GitHub's service-computed SHA-256. `Tree SHA-256` hashes a
sorted manifest of every extracted relative path and file SHA-256.

| Artifact | ID | Artifact digest | Tree SHA-256 | Parsed result |
| --- | ---: | --- | --- | --- |
| Windows TRX | `9377011883` | `61ff7ab322a1077efc2ea13d606f802194c76f773cbd1e6948089f566b61cc7e` | `74dba838840200535aeba0d0d5828e3da18895c252ce034ecd5dde61190ca243` | 12 files, 1558/1558 passed |
| macOS TRX | `9376937738` | `7637b5b03d5423e7f2f92ea6954d5d0cfb82837fb9abbc7dee2fbee8112ae11d` | `4e3d05d871addcbd020e12b5ea81f046ec40062e5bbd712bf890548aa12a18bb` | 12 files, 1558/1558 passed |
| Ubuntu TRX | `9376980802` | `94d4e08d79443f2c8df64739e57b837dd3aeca8243b136948effb98cfbf6192d` | `0d155ee0203bee7350d9e97694de36564def4021fff105a198123d284d992afe` | 12 files, 1558/1558 passed |
| Gitleaks SARIF | `9376878056` | `2b8d02fc0e86997252d68639fa2e8bf471397eb609dae7f219937afc8593a5c4` | `30b18e59f84d7bf1dc7e3eb60edbec4f2598eeea70e38fed7af10f1f47fb3c5c` | 208 rules, 0 results |

All three TRX aggregates also reported zero failed, error, timeout, aborted,
inconclusive, passed-but-aborted, not-runnable, not-executed, disconnected,
warning, completed, in-progress, or pending tests. The 36 files total
4,674/4,674 passes; Desktop contributes 402/402 on every hosted OS.

The three downloaded package directories independently passed the repository's
`Flowspan.Release verify` command and all 15 non-manifest `SHA256SUMS` entries.
Their SLSA provenance binds version `0.1.128`, the implementation commit, CI run
attempt 1, and the named hosted builder. Each SPDX 2.3 SBOM contains 38 packages
and 38 relationships.

| Package | ID | Artifact digest | Tree SHA-256 | Inner archive bytes / SHA-256 |
| --- | ---: | --- | --- | --- |
| `win-x64` | `9377099327` | `f78d437cb18530cfcf34bfc009e2644288ce554d83edc42f42cc460759112d65` | `d0ecc6cc5f5b63dc5476d94482f6543c7034566d663123091b82645ef261faf3` | 43,856,846 / `810af07cbbb2dafeb585394fe12e22909c3e9e1a94c371f27c2bd533c96b2a1d` |
| `linux-x64` | `9377074102` | `17e277159f6cbb0ed5138f2d0726998147fffb36fc012a10ad72efe7356b6765` | `9236c3963c093a016036be68fbbf0cc8c851fda276bad58b82455a8119a787a1` | 41,871,656 / `02a7850368c6ba34bd9e3dee4c9d19a587bda96ab4792c95a0d161025edd67b6` |
| `osx-arm64` | `9377074629` | `466627a8c5830ac25cee0a5942502951e3a5e784fc6e646bfe39501c6d9d4a04` | `163de8103ab7ee938f17c63721c88ae8b155f0b21827855038071fbbd7c400e0` | 42,687,742 / `1505dbbfb6c92f09da486d121e739db41ddeb61a6ccdc5f15310e702a40de785` |

All packages are explicitly `unsigned-test-artifact`; each 37-package license
report remains `reviewRequired=true`. These are not signed release installers.

[CodeQL run `32283948098`](https://github.com/happys2333/flowspan/actions/runs/32283948098),
job [`96168958399`](https://github.com/happys2333/flowspan/actions/runs/32283948098/job/96168958399),
also passed for the implementation commit. CodeQL 2.26.3 analysis `1642708058`
evaluated 52 rules, reported 0 results, and the branch had 0 open alerts.

## Open evidence

- Packaged real-machine screen-reader navigation, speech, and reading order on
  Windows, macOS, and Linux.
- Visible focus, operating-system high contrast, font fallback, native text
  scaling, and reduced-motion integration on all three supported platforms.
- Signed or notarized install, upgrade, launch, and uninstall evidence.
- Physical-device, native permission/hardware, sustained-load, and the remaining
  product, security, reliability, and release gates.

Task 7.4a is complete on the strength of the repository gates and exact-commit
hosted evidence above. Task 7.4b, parent task 7.4, every affected release
criterion, and the v1 Goal remain open.
