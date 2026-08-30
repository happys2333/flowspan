# Host route authenticated-disconnect checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Implementation commit:
`d5931817d95b592bfa4e22eb8da304a18c86e2ca`

Fixture-stabilization commit:
`c98a570441c7f152fce6dbef868eb5a70682e8b6`

Final hosted evidence tree:
`c98a570441c7f152fce6dbef868eb5a70682e8b6`

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint adds the 41st production-composed managed tracer execution and
hardens the host's route-to-Prepare authority gate. Its fault is an independent
authenticated transport loss after the real responder-route side effect has
completed but before the coordinator calls protocol Prepare. It therefore
advances only H1 Disconnect from Missing to Partial. H0 Disconnect and CL
Disconnect remain Partial, and every other matrix cell is unchanged.

`H1AuthenticatedDisconnectAfterRouteSideEffectPreventsPrepareAndDrainsBothNodes`
uses real authenticated protocol-1.7 TCP over loopback. The route hook runs only
after the inner production responder-route call returns. At that exact boundary,
the route wrapper and host media directory each report one route, the Connection
Preparation registration is current, the H1 Protection reservation exists, and
Emergency Stop readiness is current. The Prepare call and send-admission counts
are both zero.

The hook starts participant Connection disposal, then waits for a barrier that
is completed only after the production host revocation callback returns. It does
not await the full disconnect from inside the route call. After that callback,
the authenticated Connection and its exact Preparation registration are
non-current and the old generation cannot be reacquired. The host Trust record,
peer fingerprint, and sole `mirror.view` grant remain unchanged, so the injected
fault is transport loss rather than Trust or Capability revocation.

The host returns bounded `authenticated_connection_stale` with no inner
exception, fingerprint, or dependency payload. Neither the protocol Prepare
method nor its wire send-admission boundary is entered. Media attachment,
capture, participant renderer preparation, Admission, media send, render, and
input remain zero. Because the real route side effect exists, owned cleanup
fail-closes and disposes the host Connection exactly once. The Connection and
Permission registrations, Protection source, Emergency readiness, capture/input/
session owners, media directories and routes, handlers, participant channel,
connections, control owner, host generation, snapshot, and media budget drain
across both nodes. The caller-owned source lease and unchanged Trust grant remain
current.

## TDD RED and production repair

Against the pre-fix production path, the new tracer reached the real route,
completed the transport callback barrier, returned the expected bounded stale
failure, and passed its authority and cleanup assertions. Its final exact
assertion was RED:

```text
Expected PrepareCount: 0
Actual PrepareCount:   1
```

The first minimal GREEN inserted a post-route `ValidateCurrentHostFacts` call.
That stopped this Connection-disconnect row, but strict counter-review showed a
bare fact read could still enter Prepare for caller cancellation, deadline,
Protection or Emergency termination, and could let a later point-read failure
replace an already-linearized terminal cause.

The final `EnsurePrePrepareAuthorityIsCurrent` gate preserves the complete
pre-Prepare order:

1. exact caller cancellation;
2. an already-recorded terminal Preparation cause;
3. canonical deadline equality;
4. current source, Connection, Permission, and Authorization facts plus a fresh
   exact-source `Safe` Protection observation; and
5. the same cancellation, terminal-cause, and deadline checks again after those
   reads.

A non-fatal fact/protection failure observed alongside a terminal reservation is
projected back to the recorded terminal reason. `OutOfMemoryException` is
excluded from that reduction and proceeds by exact instance as the primary
failure into the existing outer cleanup/aggregation path. The additional
pre-Prepare Protection read moved the existing
`CallerCancellationBeforeEmergencyPromotionInstallsNoFormalOwner` hook from its
second to its third read so that test still injects after media wait and before
Emergency Stop promotion rather than changing scenario meaning.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~H1AuthenticatedDisconnectAfterRouteSideEffectPreventsPrepareAndDrainsBothNodes'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~H1AuthenticatedDisconnectAfterRouteSideEffectPreventsPrepareAndDrainsBothNodes'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~H1AuthenticatedDisconnectAfterRouteSideEffectPreventsPrepareAndDrainsBothNodes|FullyQualifiedName~DesktopRemoteWindowHostCoordinatorTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~H1AuthenticatedDisconnectAfterRouteSideEffectPreventsPrepareAndDrainsBothNodes|FullyQualifiedName~DesktopRemoteWindowHostCoordinatorTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DesktopRemoteWindowManagedTwoNodeTracerTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
```

Final local results through exact commit `d593181`:

- focused H1 authenticated-disconnect row: `1/1` in Debug and Release;
- H1 row plus coordinator class: `116/116` in Debug and Release;
- production-composed managed tracer: `41/41` in Debug and Release;
- Desktop: `718/718` in Debug and Release;
- combined solution tests: `2582/2582` in Debug and Release;
- solution builds: zero warnings and zero errors; and
- format verification and `git diff --check`: passed.

### Fixture-stabilization verification

`c98a570` changes only the test harness budgets in
`ProtocolOnePointTwoInvalidInitiatorFinishedNeverRunsHandler`: handshake/failure/
outer bounds move from 300 ms/2 s/3 s to 2 s/4 s/6 s. The authentication-stage,
handler-zero, exact failure, and cleanup assertions are unchanged.

```bash
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~ProtocolOnePointTwoInvalidInitiatorFinishedNeverRunsHandler'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~ProtocolOnePointTwoInvalidInitiatorFinishedNeverRunsHandler'
for run in {1..10}; do dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~ProtocolOnePointTwoInvalidInitiatorFinishedNeverRunsHandler' || exit 1; done
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Debug --no-restore
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj --configuration Release --no-restore
```

The focused theory passes `3/3` in Debug and Release, ten fresh Release
processes pass `30/30`, and Transport passes `755/755` in Debug and Release.
Strict review reports APPROVE with 0 P0/P1/P2 findings. The fix changes no
production source, protocol assertion, tracer count, or matrix status.

## Hosted `d593181` failure history

[CI `33306962398`](https://github.com/happys2333/flowspan/actions/runs/33306962398)
completed with `failure`, attempt 1, for exact `d593181`. Ubuntu, Windows, and
Secret Scan succeeded. Downloaded test artifacts contain 12 TRX files per OS:

| Platform | Job ID | Result | Artifact ID | Artifact SHA-256 |
| --- | ---: | --- | ---: | --- |
| Ubuntu | `99245099589` | `2582/2582` | `9730813234` | `524b92db0605f9e1af038b6e8a6a3faefc1e2dc6178ac875c66339d7de7292de` |
| Windows | `99245099605` | `2582/2582` | `9730825099` | `26a4ce98df8f3d55d8aea0dea7c5bacdaaa0d499a4ad6e7f331c89bacfccf501` |
| macOS | `99245099639` | `2581/2582` | `9730802177` | `73682fecc6667534e2f4f97c4405fd42e045ad4474dffa359b7c9a9288b5a138` |

The sole macOS failure was the Transport theory execution
`ProtocolOnePointTwoInvalidInitiatorFinishedNeverRunsHandler(Omit)`:
Transport passed `754/755`, while Desktop independently passed `718/718`. The
old 300 ms fixture watchdog closed the server before the manual initiator
completed responder authentication, so the harness observed
`EndOfStreamException`. This is unrelated to the H1 production change, whose
Desktop suite passed, but it makes the exact CI run unsuccessful evidence.

Secret Scan job `99245099480` succeeded. Artifact `9730771204`, SHA-256
`d3ed20131d72a279479313df06e29fa31e68cdf4bf7cd915b405879e5f7ad25e`,
contains Gitleaks SARIF with 208 rules and 0 results.

[CodeQL `33306962391`](https://github.com/happys2333/flowspan/actions/runs/33306962391)
succeeded independently for exact `d593181`. Job `99245099442` produced
analysis `1693859401` with 52 rules, 0 results, no analysis error, and 0 open
alerts on the branch ref.

Package job `99245587511` was skipped because CI did not succeed. No package
artifact was produced; this checkpoint makes no hosted package-success claim.

## Final hosted status

`c98a570` widens only the failing theory's test-harness budgets as described
above. [CI `33307322868`](https://github.com/happys2333/flowspan/actions/runs/33307322868)
and [CodeQL `33307322870`](https://github.com/happys2333/flowspan/actions/runs/33307322870)
completed with `success`, run 218 attempt 1, for exact tree
`c98a570441c7f152fce6dbef868eb5a70682e8b6`. All three test artifacts contain
12 TRX files, `2582/2582` total, executed, and passed. Failed, error, timeout,
aborted, inconclusive, passed-but-run-aborted, not-runnable, not-executed,
disconnected, warning, completed, in-progress, and pending counters are all zero:

| Platform | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Linux | `99246048330` | `9730921471` | `1de32127c6eeb2cc9c90453aa3a72dff5f082b447041f54825d59aef9573c3b7` |
| macOS | `99246048353` | `9730925706` | `1ce4f0d6d9348cfa4b701d9c390b2c540f1ebd0dc5761512624ce219b2390241` |
| Windows | `99246048311` | `9730933373` | `7b00d7e7c93dfa80b6658d1a9d9162012507d87ea61bc72d97ee4b8c45f52848` |

Secret Scan job `99246048249`, artifact `9730880395`, has outer SHA-256
`ccb161c29338738cc0d5f7b09238e6eaefd7504a0331d1a8a900884d72f5f29c`
and payload SHA-256
`f1cc1fc5bf34d5e9643655ac6480ff4681e27aa9c2c639f03067fc7ea8595e3d`,
with Gitleaks 208 rules and 0 results. CodeQL job `99246048379` produced analysis
`1693872518`, SARIF `c07c8586-a460-11f1-9dc2-7ef2398e4798`, payload SHA-256
`dec953f1b8e2c744a85b5541ccab88312f278ee568a2348e7055836205e100d9`,
52 rules and 0 results, empty warning/error fields, and 0 exact-ref open alerts.

All packages report version `0.1.218`, exact SHA, and
`unsigned-test-artifact`; all `5/5` `SHA256SUMS` entries pass per runtime and
the repository `Flowspan.Release verify` command passes each artifact directory:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 | Archive SHA-256 | Tree SHA-256 |
| --- | ---: | ---: | --- | --- | --- |
| `win-x64` | `99246534383` | `9730959605` | `62cc297dd4ad724b6e72696623bfaf6c49a80c396e46a191b6f850db8479cf22` | `442a41498dd7c9b4df7169453ccde0df1e67ffac84f628b3d88c0bf0feca1d60` | `4ef04e2fdd29ddb482f19108fadde9ce1aba2725e546667a69b3afc2f7b243a9` |
| `osx-arm64` | `99246534403` | `9730954078` | `0d593f97c01b7d160d4fdafed8a6140500ac0904c2c3e98721db31b0b82b8abc` | `799318f85868fc1416429e4ce2088167e83d87fcf6ed4633a6ae12bc4a512542` | `914cdb4c70f8fe9072e69e8616be6bb4d261da4a0e9e733c546be12fe1d34d4f` |
| `linux-x64` | `99246534478` | `9730952849` | `04afdb45501f406eaa88adf8fd644ec48db85efe23eda1b11da3749d28b8bc7b` | `240fa08131e56fde5e3337fdcd3e4860058b03207199fc027b0dd468c237f885` | `5b62856aabee9d469433e3f83001474935fc2f72876b2b6c398ea3806688390e` |

These successful exact-SHA jobs close the portable managed evidence gap left by
the retained `d593181` failure history. They prove the managed 41-case tree and
reproducible unsigned packages, not native APIs, physical Devices, signing,
notarization, or release acceptance.

## Explicit limitations

The focused local tracer is same-host managed loopback evidence on macOS. The
hosted matrix is portable managed/contract evidence. Neither form instantiates
native capture/input/protection/permission/Emergency Stop APIs, a physical
Device pair, packaged accessibility, signing, notarization, or release
acceptance.

The scenario covers one H1 authenticated disconnect after route selection and
before any Prepare call. It does not complete the other route/send disconnect
phases, H1 reject/throw/cleanup-fault intersections, native non-cooperative
teardown, or the aggregate H0/H1 matrix. H1 Disconnect remains Partial; H0 and
CL Disconnect remain Partial. Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance,
every native/physical/signing/notarization/release gate, and the long-term Goal
remain open. `CreateProduction()` remains unavailable.
