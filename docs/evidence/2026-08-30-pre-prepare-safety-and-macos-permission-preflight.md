# Pre-Prepare safety and macOS permission preflight candidate

Date: 2026-08-30

Branch: `codex/v1-foundation`

Committed baseline: `7f4ec4dba0158dd16e1856eb5012e69d91540b61`

Implementation commit: `eb2e2ad9da18163604fdb2d5fa484acfad70a507`

Available local environment: macOS 26.6.2 arm64, Xcode 26.6 with macOS
26.5 SDK headers, .NET SDK 10.0.301

## Evidence status

This describes the implementation commit above with completed local
verification. It claims no hosted CI, CodeQL, Secret Scan, package, or native
product result. The
final evidence owner must append hosted artifacts.

The finite acceptance inventory is
[`remote-window-production-boundary-matrix.md`](../testing/remote-window-production-boundary-matrix.md).
This candidate advances only part of H0/H1 and the first macOS permission
preflight boundary. It does not complete Task 5.5a.

## Managed host safety candidate

`DesktopRemoteWindowHostCoordinator.StartAsync` keeps route selection and
Prepare behind this order:

1. validate the current source lease, authenticated connection and protocol,
   permission facts, and peer-relative Mirror grant;
2. require one fresh, identity- and generation-bound `Safe` protection fact;
3. stop on exact caller cancellation;
4. revalidate source, connection, permission, and grant facts;
5. perform one pure, prompt-free Emergency Stop readiness check;
6. stop on exact caller cancellation;
7. revalidate the host facts a second time;
8. stop on caller cancellation or canonical deadline equality; and
9. only then select the responder route and send Prepare.

The existing post-Ready path separately revalidates facts and formally registers
protection and Emergency Stop ownership before capture and final Admission.
Prepare, Ready, route possession, and readiness grant no capture, participant,
Driver, input, render, or frame authority.

Unexpected non-fatal dependency exceptions use bounded public reasons:

| Boundary | Stable reason |
| --- | --- |
| permission snapshot | `native_permission_unavailable` |
| authenticated-connection facts | `authenticated_connection_stale` |
| protection snapshot | `native_protection_not_safe` |
| Emergency Stop readiness | `emergency_stop_readiness_unavailable` |

Candidate tests require these surfaces to omit injected canary text and inner
exceptions. `OutOfMemoryException` is not converted to a product rejection.
They inject unsafe protection, unavailable readiness, dependency throws, caller
cancellation, deadline equality, source invalidation, permission/grant
revocation, and connection revocation at named synchronous seams, requiring
failure before route or Prepare and the tested startup-owner cleanup assertions.
If a pre-route safety callback has already started authenticated fail-close,
cleanup joins that exact task even though no route was selected; a blocked then
throwing fail-close remains the ordered cleanup failure and blocks restart.
The finite matrix keeps the remaining cleanup owners and cleanup-fault
combinations partial rather than inferring them from this slice.

These tests do not prove absolute linearization against mutation from an
arbitrary concurrent thread. `CheckReadiness()` observes the present registrar
state but does not reserve the later registration. An atomic readiness-to-
registration reservation or equivalent generation-bound owner remains a Task
5.5a blocker.

## macOS permission candidate

`MacOSNativeRemoteWindowPermissionBoundary` is a narrow CoreGraphics adapter:

- `GetSnapshot()` calls `CGPreflightScreenCaptureAccess` without requesting a
  prompt;
- only `RequestCapturePermissionAsync` calls
  `CGRequestScreenCaptureAccess`, after checking caller cancellation;
- unsupported runtimes remain `Unsupported` without crossing native code;
- granted capture that later fails preflight becomes `Revoked`;
- interop exceptions become bounded `Unavailable`; and
- input remains explicitly `Unsupported`.

Injected-interop tests distinguish preflight from request and cover grant,
denial, revocation, unsupported runtime, redaction, pre-cancellation, concurrent
late results, observer isolation/reentrancy, and disposal. A matching-host smoke
calls production CoreGraphics preflight only; it passed with one bounded
`Granted` or `NotDetermined` fact and did not call the request API or display a
permission prompt.

The adapter is not wired into `CreateProduction()`. It does not implement or
prove ScreenCaptureKit capture, native frame ownership, CoreGraphics input,
Accessibility permission, secure-input/protected-surface observation, native
Emergency Stop, physical two-Device operation, packaged TCC, Windows/Linux,
signing, notarization, or release readiness.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowHostCoordinatorTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowHostCoordinatorTests'
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~NativeRemoteWindowContractsTests'
dotnet test tests/Flowspan.Platform.Tests/Flowspan.Platform.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~NativeRemoteWindowContractsTests'
dotnet test tests/Flowspan.Platform.MacOS.Tests/Flowspan.Platform.MacOS.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~MacOSNativeRemoteWindowPermissionBoundaryTests'
dotnet test tests/Flowspan.Platform.MacOS.Tests/Flowspan.Platform.MacOS.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~MacOSNativeRemoteWindowPermissionBoundaryTests'
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
dotnet list Flowspan.slnx package --vulnerable --include-transitive
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj --configuration Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj --configuration Release --no-build --no-restore
```

The evidence branch head must then receive Windows, macOS, and Linux CI, Secret
Scan, CodeQL, and reproducible unsigned-package jobs. Hosted success would
remain managed contract/build evidence, not packaged real-machine native,
physical, signing, or notarization evidence.

Local results on the environment above:

- focused host coordinator Debug/Release: `36/36` each;
- focused portable native contracts Debug/Release: `30/30` each;
- focused macOS permission boundary Debug/Release: `22/22` each;
- full macOS platform project Debug/Release: `47/47` each;
- solution Debug/Release build with warnings as errors: zero warnings, zero
  errors;
- solution Debug/Release tests: `2286/2286` each;
- solution format verification and `git diff --check`: passed;
- direct and transitive NuGet vulnerability audit: no known vulnerable package
  in any solution project;
- explicit-test-mode Desktop composition validation: passed;
- deterministic simulator: committed protocol 1.7 handoff result; and
- matching-host CoreGraphics smoke: passed without calling Request or displaying
  a permission prompt.

Hosted evidence still to append:

- Implementation SHA: `eb2e2ad9da18163604fdb2d5fa484acfad70a507`
- Exact-SHA Windows/macOS/Linux CI and unsigned-package artifacts: pending
- Exact-SHA Secret Scan and CodeQL results: pending

Tasks 5, 5.5a, 5.5, Tasks 6-10, all native/physical/release gates, and the Goal
remain open. `CreateProduction()` must continue to report Remote Window
unavailable.
