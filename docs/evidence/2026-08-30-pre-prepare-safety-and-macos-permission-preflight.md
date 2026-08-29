# Pre-Prepare safety and macOS permission preflight candidate

Date: 2026-08-30

Branch: `codex/v1-foundation`

Committed baseline: `7f4ec4dba0158dd16e1856eb5012e69d91540b61`

Implementation commit: `eb2e2ad9da18163604fdb2d5fa484acfad70a507`

Hosted evidence commit: `92edfffced6cf7b73bc2d91ec31d253bbc71c0c1`

Available local environment: macOS 26.6.2 arm64, Xcode 26.6 with macOS
26.5 SDK headers, .NET SDK 10.0.301

## Evidence status

This describes the implementation commit above with completed local
verification and the hosted evidence commit above. It claims no packaged
real-machine native, physical-device, signing, notarization, or release result.

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

The hosted evidence commit received Windows, macOS, and Linux CI, Secret Scan,
CodeQL, and reproducible unsigned-package jobs. Their success remains managed
contract/build evidence, not packaged real-machine native, physical, signing,
or notarization evidence.

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

Final independent review first found and blocked one pre-route asynchronous
fail-close cleanup escape. The added blocking/failing regression and shared-task
cleanup repair closed it; re-review returned APPROVE with 0 P0, 0 P1, and 0 P2
findings for this checkpoint.

## Hosted exact-SHA evidence

[CI run `33275235290`](https://github.com/happys2333/flowspan/actions/runs/33275235290)
completed successfully for hosted evidence commit
`92edfffced6cf7b73bc2d91ec31d253bbc71c0c1`. Each downloaded platform artifact
contains exactly 12 TRX files with `2286/2286` total, executed, and passed, and
every failed, error, timeout, aborted, inconclusive, passed-but-run-aborted,
not-executed, disconnected, warning, completed, in-progress, and pending counter
is zero:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| macOS | `99160667448` | `9721329113` | `a6b6ff5fdee29997eb4049befe479edb3e78c64f2ff1a0645d92f98d9fe3b5a8` |
| Windows | `99160667488` | `9721342698` | `c71738164dc88773a91eadfcc97aeb4bae18e1886c83116079159491716ee8df` |
| Linux | `99160667552` | `9721323183` | `1b5bff6caca9290c5d21180b76d69fd761be8cb7da3f39542f3ad751ed845a6e` |

Secret Scan job `99160667385` passed. Artifact `9721286733`, digest
`dc09b09be6367b19741f368fe3cae9a362ec4039100b64b0f78e259678b4686c`,
contains SARIF 2.1.0 with 208 Gitleaks rules and 0 results. Every reproducible
unsigned package job passed its content lock, explicit TEST MODE composition,
seal verification, dependency audit, and artifact upload:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| `win-x64` | `99161227701` | `9721371228` | `906d9b943b05d928253bbf2a70ae09042d41340140cf8958e25b188cdb578a19` |
| `osx-arm64` | `99161227709` | `9721368316` | `82cd226210c84e1257968f82872e97100859c38a6e3217bbceef3508263c42a3` |
| `linux-x64` | `99161227667` | `9721361904` | `4c3aace40057bfbd64f27f25061c5c6fdef377b82883edf3a64cc2ed6ac97d28` |

[CodeQL run `33275235305`](https://github.com/happys2333/flowspan/actions/runs/33275235305),
job `99160667435`, completed successfully. Exact-SHA analysis `1692553645`
evaluated 52 rules with 0 results, and the exact-commit branch query returned 0
open alerts.

Tasks 5, 5.5a, 5.5, Tasks 6-10, all native/physical/release gates, and the Goal
remain open. `CreateProduction()` must continue to report Remote Window
unavailable.
