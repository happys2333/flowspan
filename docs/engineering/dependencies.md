# Direct dependency inventory

Last reviewed: 2026-07-13

Flowspan pins direct NuGet versions centrally and commits lock files for every
project. Production dependencies require an ADR that records fit, maintenance,
license, security, and replacement considerations. A release SBOM and complete
transitive license report remain mandatory under the v1 release criteria.

## Production

| Package | Version | License | Purpose | Decision evidence |
| --- | --- | --- | --- | --- |
| `Avalonia` | 12.1.0 | MIT | Desktop control, styling, binding, and XAML core | [ADR 0007](../adr/0007-avalonia-desktop-shell.md) |
| `Avalonia.Desktop` | 12.1.0 | MIT | Windows, macOS, and Linux desktop backends | [ADR 0007](../adr/0007-avalonia-desktop-shell.md) |
| `Avalonia.Themes.Fluent` | 12.1.0 | MIT | Maintained accessible control templates; Flowspan owns visual tokens | [ADR 0007](../adr/0007-avalonia-desktop-shell.md) |
| `Makaretu.Dns.Multicast` | 0.27.0 | MIT source tag; nupkg metadata undeclared | Isolated provisional mDNS/DNS-SD browser and publisher | [ADR 0004](../adr/0004-dns-sd-discovery-boundary.md) |
| `System.Security.Cryptography.ProtectedData` | 10.0.9 | MIT | Windows CurrentUser DPAPI byte API | [ADR 0005](../adr/0005-platform-secret-storage.md) |

The package is published by Microsoft from
[`dotnet/runtime`](https://github.com/dotnet/runtime). It is referenced only by
`Flowspan.Platform.Windows`; other platforms do not receive a plaintext or
portable fallback.

`Makaretu.Dns.Multicast` is referenced only by `Flowspan.Transport.Mdns`; no
third-party DNS type crosses the adapter boundary. Its locked graph currently
includes `Common.Logging` 3.4.1, `Common.Logging.Core` 3.4.1, `IPNetwork2`
2.1.2, `Makaretu.Dns` 2.0.1, `SimpleBase` 1.3.1, `Tmds.LibC` 0.2.0, and .NET
Standard compatibility packages. NuGet reported no known vulnerability on
2026-07-13, but several transitive versions are old and the nupkg omits license
metadata. Physical network validation, provenance resolution, and the final
license report remain release gates; the narrow adapter is the replacement
seam.

## Test infrastructure

| Package | Version | License | Purpose |
| --- | --- | --- | --- |
| `Avalonia.Headless` | 12.1.0 | MIT | In-memory desktop control tree and input tests |
| `Microsoft.NET.Test.Sdk` | 17.14.1 | MIT | .NET test host and protocol |
| `coverlet.collector` | 6.0.4 | MIT | coverage data collector |
| `xunit` | 2.9.3 | Apache-2.0 | test framework |
| `xunit.runner.visualstudio` | 3.1.4 | Apache-2.0 | test runner adapter |

Transitive versions and content hashes are recorded in each
`packages.lock.json`. This inventory is an engineering control, not the final
release license artifact.

The Avalonia family is confined to `Flowspan.Desktop` and
`Flowspan.Desktop.Tests`. The test project deliberately uses the framework-neutral
`Avalonia.Headless` package rather than `Avalonia.Headless.XUnit` 12.1.0 because
the latter requires xUnit v3 extensibility while the repository test suite
currently uses xUnit v2. `Avalonia.Headless` brings `Avalonia.Fonts.Inter`
transitively for its in-memory test platform; Flowspan production styles neither
reference nor ship that package intentionally.

### Avalonia restored graph

The 2026-07-13 locked `net10.0` graph contains:

- Avalonia 12.1.0 platform packages: `FreeDesktop`, `FreeDesktop.AtSpi`,
  `HarfBuzz`, `Native`, `Remote.Protocol`, `Skia`, `Win32`, and `X11`;
- `Avalonia.BuildServices` 11.3.2, `MicroCom.Runtime` 0.11.6, and
  `Tmds.DBus.Protocol` 0.94.1;
- `HarfBuzzSharp` 8.3.1.3 plus Linux, macOS, WebAssembly, and Win32 native
  packages;
- `SkiaSharp` 3.119.4 plus Linux, macOS, WebAssembly, and Win32 native packages;
- `Avalonia.Angle.Windows.Natives` 2.1.27548.20260419.

NuGet declares MIT for the Avalonia, MicroCom, Tmds, HarfBuzzSharp, and
SkiaSharp package families. The ANGLE native package does not publish an SPDX
expression in the NuGet catalog, but its nupkg includes a redistribution license
matching the license at repository commit
`1c89805903c1482166356d3b950d474973180e61`; that provenance and binary notice
must be reproduced and rechecked by the final release-license job rather than
being inferred from the other Avalonia packages.

Ten large/native archives reviewed during the initial restore total 266.71 MiB
as NuGet download inputs. This is not the application artifact size: RID-specific
publish must prove that unused Windows/Linux/WebAssembly assets are excluded.
The downloaded archives used to seed the local restore were compared with the
NuGet catalog SHA-512 values before use. Committed lock files remain the
reproducible source of package identities and content hashes for CI.

## CI automation

GitHub Actions are pinned to immutable commits; the adjacent workflow comments
retain their human-readable release tags. On 2026-07-13, the exact tag targets
were queried from each upstream repository and each pinned `action.yml` was
checked for the Node 24 runtime. CodeQL `v4.37.0` is an annotated tag, so the
table records its peeled commit rather than its tag-object SHA.

| Action | Release | Immutable commit | Runtime | Purpose |
| --- | --- | --- | --- | --- |
| `actions/checkout` | `v7.0.0` | `9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0` | Node 24 | Minimal/full-history source checkout |
| `actions/setup-dotnet` | `v5.4.0` | `26b0ec14cb23fa6904739307f278c14f94c95bf1` | Node 24 | Install SDK selected by `global.json` and cache locked packages |
| `actions/upload-artifact` | `v7.0.1` | `043fb46d1a93c77aae656e7c1c64a875d1fc6a0a` | Node 24 | Retain per-runner test evidence |
| `gitleaks/gitleaks-action` | `v3.0.0` | `e0c47f4f8be36e29cdc102c57e68cb5cbf0e8d1e` | Node 24 | Scan committed history for secrets |
| `github/codeql-action` | `v4.37.0` | `99df26d4f13ea111d4ec1a7dddef6063f76b97e9` | Node 24 | Initialize and analyze C# CodeQL database |

The gitleaks `v3.0.0` release states that it changes the runtime from Node 20 to
Node 24 without changing inputs, outputs, or behavior. GitHub repository
metadata did not identify an SPDX license for `gitleaks/gitleaks-action` at the
reviewed commit; that remains an explicit input to the final release-license
review rather than an inferred license claim.
