# Participant policy and final Admission fault checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Committed baseline: `2364f763f707bb6a958c56423d58a5f7165cc9cd`

Implementation commit: `113fce0edafda4206c40419a69a1372d7cb4e303`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint advances only the P0, AD, and HC rows in the finite
[production-boundary matrix](../testing/remote-window-production-boundary-matrix.md).
It does not complete Task 5.5a.

The participant receive-policy boundary now permits only the policy-owned
`renderer_unavailable` and `role_unsupported` rejection reasons. Any other
non-null text, including attacker-controlled or accidental canary text, becomes
`renderer_unavailable`. A non-fatal policy throw, including one with a canary in
its inner exception, has the same bounded result. These failures occur before
connection acquisition, `FSM1`, renderer preparation, Ready success, Admission,
or rendering. A recovered policy can prepare a fresh request through real
loopback `FSM1`, after which the tested owners drain.

The host final-Admission publication boundary now reduces unexpected and
foreign-token failures to `host_admission_publish_failed` without carrying raw
exception text or an inner exception. An exact caller cancellation remains an
`OperationCanceledException` with the caller's original token. The authenticated
connection lease performs the required token normalization: only a cancellation
carrying the lease's linked token while the caller token is cancelled is
reissued with the caller token. Foreign cancellation retains its identity and
cannot be relabelled merely because caller cancellation races it.

The production-composed managed tracer now has 22 cases. Its new AD case uses
real authenticated loopback TCP, protocol 1.7 and bilateral `FSM1`; it waits for
the participant endpoint to complete Admission, commit the known binding and
publish `StateChanged`, then throws one host-side canary after the inner
publication side effect. The host frame gate never opens, so media send and
render counts remain zero. The asserted capture, input, renderer, protection,
permission observer, Emergency Stop, control, connection, route, directory and
handler owners drain; host fail-close and connection disposal each execute once,
and the old authenticated generation cannot be reacquired.

## TDD evidence

- An unknown policy canary reason initially escaped to the response constructor
  and failed with `ArgumentException`; bounding it produced a stable rejection.
- An Admission publication `IOException` initially escaped with its canary;
  coordinator projection produced `host_admission_publish_failed`.
- A foreign-token Admission cancellation initially escaped when caller
  cancellation raced it; exact-token classification fixed that path.
- A real lease cancellation initially returned its linked token instead of the
  caller token; lease-boundary normalization restored the original token.
- The managed side-effect tracer initially failed to compile because its
  post-publication injection seam did not exist; the minimal wrapper seam then
  exercised the production cleanup path.

Final independent review returned APPROVE with 0 P0, 0 P1, and 0 P2 findings
after the lease-token and conservative evidence-language repairs.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowHostCoordinatorTests|FullyQualifiedName~DesktopRemoteWindowPreparationPeerTests|FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowHostCoordinatorTests|FullyQualifiedName~DesktopRemoteWindowPreparationPeerTests|FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~AuthenticatedRemoteWindowConnectionLeaseTests'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~AuthenticatedRemoteWindowConnectionLeaseTests'
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj --configuration Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj --configuration Release --no-build --no-restore
git diff --check
```

Results:

- focused Desktop host/participant/tracer Debug and Release: `81/81` each;
- focused connection-lease Debug and Release: `18/18` each;
- Desktop Debug and Release: `581/581` each;
- Transport Debug and Release: `704/704` each;
- solution Debug and Release build: zero warnings, zero errors;
- solution Debug and Release tests: `2295/2295` each;
- format, diff, direct/transitive vulnerability audit, explicit TEST MODE
  composition, and deterministic protocol-1.7 simulator: passed.

Hosted Windows/macOS/Linux CI, Secret Scan, unsigned-package and CodeQL evidence
for the checkpoint commit is pending.

## Explicit limitations

P0 still lacks current-lease collaborator throws, pending Trust/connection
revocation, authenticated disconnect and remaining cleanup-fault combinations.
AD/HC still lack participant-endpoint throw, authority revoke, authenticated
disconnect, the other post-Ready host boundaries, and their cleanup variants.
The host pre-Prepare facts/readiness bundle is still not linearizable across an
arbitrary concurrent thread; its reservation design remains open.

This checkpoint adds no native capture or input, protected-surface behavior,
physical two-device evidence, signing, notarization, or release acceptance.
Tasks 5, 5.5a, 5.5, Tasks 6-10 and the long-term Goal remain open.
`CreateProduction()` must continue to report Remote Window unavailable.
