# Direct dependency inventory

Last reviewed: 2026-07-13

Flowspan pins direct NuGet versions centrally and commits lock files for every
project. Production dependencies require an ADR that records fit, maintenance,
license, security, and replacement considerations. A release SBOM and complete
transitive license report remain mandatory under the v1 release criteria.

## Production

| Package | Version | License | Purpose | Decision evidence |
| --- | --- | --- | --- | --- |
| `System.Security.Cryptography.ProtectedData` | 10.0.9 | MIT | Windows CurrentUser DPAPI byte API | [ADR 0005](../adr/0005-platform-secret-storage.md) |

The package is published by Microsoft from
[`dotnet/runtime`](https://github.com/dotnet/runtime). It is referenced only by
`Flowspan.Platform.Windows`; other platforms do not receive a plaintext or
portable fallback.

## Test infrastructure

| Package | Version | License | Purpose |
| --- | --- | --- | --- |
| `Microsoft.NET.Test.Sdk` | 17.14.1 | MIT | .NET test host and protocol |
| `coverlet.collector` | 6.0.4 | MIT | coverage data collector |
| `xunit` | 2.9.3 | Apache-2.0 | test framework |
| `xunit.runner.visualstudio` | 3.1.4 | Apache-2.0 | test runner adapter |

Transitive versions and content hashes are recorded in each
`packages.lock.json`. This inventory is an engineering control, not the final
release license artifact.
