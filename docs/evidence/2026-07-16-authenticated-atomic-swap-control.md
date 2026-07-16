# Authenticated Atomic Swap Control Evidence — 2026-07-16

## Evidence boundary

This slice carries exact Activity snapshot, Prepare, and durable Commit or
Abort convergence over the authenticated local control session. It proves a
protocol-1.1 Swap channel, independent `activity.swap` authorization, strict
message and journal binding, bounded network waits, and one coordinator using a
local direct endpoint plus a real encrypted loopback durable endpoint.

It does not expose a Desktop Swap command or prove exact human confirmation,
durable Activity-catalog integration, a representative native Adapter,
physical two-device interruption, sleep/wake, abrupt power loss, packaged-app
behavior, or migration of arbitrary application process state.

Branch: `codex/v1-foundation`

Verified implementation commit:
`72f87de46a5accd609dcd932a9d1a3b5fe585bcd`

Task 3.3c remains open until the evidence-closure commit containing the hosted
results below passes the same Windows, macOS, and Ubuntu CI, Secret Scan, and
CodeQL workflows.

The protocol and security decision is recorded in
[ADR 0014](../adr/0014-authenticated-atomic-swap-control.md).

## Local environment

```text
Host: macOS 26.5.2 (build 25F84), Apple Silicon, Asia/Shanghai
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
RID: osx-arm64
```

## Commands

```sh
dotnet restore Flowspan.slnx --locked-mode --nologo
dotnet format Flowspan.slnx --no-restore
dotnet format Flowspan.slnx --verify-no-changes --no-restore \
  --verbosity minimal
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable \
  --include-transitive --no-restore
git diff --check
```

Four risk-focused groups also ran in 20 fresh testhost processes per command:

```sh
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~SwapActivityControlSessionTests'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~SwapControlMessageCodecTests'
dotnet test tests/Flowspan.Integration.Tests/Flowspan.Integration.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~AuthorizedSwapEndpointTests|FullyQualifiedName~SwapCoordinatorTests'
dotnet test tests/Flowspan.Integration.Tests/Flowspan.Integration.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~PersistentSwapEndpointJournalTests'
```

## Local results

- locked restore, formatting, format verification, and patch-whitespace checks
  passed;
- the Release build passed with 0 warnings and 0 errors;
- 701 tests passed, 0 failed, and 0 skipped:
  - Desktop: 159;
  - Transport: 177;
  - Integration: 132;
  - Security: 90;
  - Domain: 36;
  - Protocol: 25;
  - shared platform contracts: 16;
  - Windows platform contracts: 18;
  - macOS platform contracts: 16;
  - Linux platform contracts: 18;
  - mDNS transport contracts: 14;
- Swap session, Swap codec, authorized/coordinator, and endpoint-journal stress
  each passed 20/20 fresh processes, with 18, 20, 37, and 18 tests respectively
  in every process;
- explicit Desktop composition printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`;
- the deterministic simulator printed protocol `1.1`,
  `Source preserved: True`, `Target resumed: True`, and
  `Atomic swap committed: True`;
- the NuGet direct/transitive vulnerability query reported no known vulnerable
  package in all 24 projects.

## Proven behavior

### Versioned and strictly bound control

- protocol 1.1 exposes six Swap request/result messages; protocol 1.0 rejects
  their construction and decoding while retaining compatible non-Swap Activity
  operations;
- fixed-ID canonical frames and SHA-256 fixtures freeze Snapshot, Prepare, and
  Decision requests and results;
- strict decoders reject unknown, missing, duplicate, noncanonical, expired,
  cross-Operation, cross-correlation, wrong-participant, wrong-target, and
  digest-mismatched input;
- one session-wide correlation reservation covers Handoff, Move, Replace,
  Replace inventory, and all Swap phases. Cleanup removes only the exact pending
  instance, so an old send cannot release a newer owner.

### Authorization and durable convergence

- `activity.swap` is independent of Offer, Receive, and Replace in Trust
  persistence, Desktop authorization controls, connection admission, and
  per-operation checks;
- snapshot disclosure and new Prepare require the current authenticated peer's
  grant. New Prepare also requires exact peer/local placement, active lifecycle,
  normal sensitivity, and a representable successor revision;
- after revocation, only an exact already-recorded
  Operation/correlation/peer-bound Prepare replay or Decision convergence can
  reach the endpoint;
- endpoint journal v2 persists the correlation and peer beside each reservation
  and decision, rejects v1 and noncanonical state, and reserves bounded terminal
  decision headroom for every Prepared record;
- an expired unknown Abort cannot consume a tombstone, while a fresh authorized
  Abort remains idempotent and bounded.

### Deadlines, uncertainty, and composition

- Snapshot and Prepare send/response waits use their operation deadline;
  Decision send/acknowledgement uses a deterministic 30-second bound;
- every receive point rechecks both the operation deadline and envelope
  `SentAt + TTL`. A non-cooperative send that ignores cancellation is abandoned,
  safely observed, and cannot hold the caller beyond the bound;
- timeout or ambiguous partial delivery closes the session and returns
  `AcknowledgementLost`; a fully authenticated matching response that was
  already processed takes precedence over a later send exception;
- cancellation and disposal request lifetime stop once, make the Swap channel
  unavailable immediately, and complete exact pending instances without a CTS
  disposal race;
- the integration path uses an authenticated encrypted TCP loopback session,
  one direct local endpoint, and one protected durable remote endpoint. Both
  catalogs converge under the coordinator's recorded decision.

## Final two-axis review

The final working tree was reviewed independently against repository Standards
and the v1 Spec after all earlier findings were repaired.

- Standards: 0 hard violations and 0 judgement findings;
- Spec: 0 missing/partial requirements, 0 scope-creep findings, and 0 incorrect
  implementations.

The review specifically rechecked fail-closed security, protocol 1.0/1.1
compatibility, exact pending-owner cleanup, session close races, journal v2
strictness, clean-room boundaries, Desktop non-exposure, and evidence honesty.

## Hosted results for the implementation commit

GitHub Actions CI run
[`29484373333`](https://github.com/happys2333/flowspan/actions/runs/29484373333)
completed successfully for exact implementation commit
`72f87de46a5accd609dcd932a9d1a3b5fe585bcd`:

- Windows job
  [`87575241019`](https://github.com/happys2333/flowspan/actions/runs/29484373333/job/87575241019):
  success;
- Ubuntu job
  [`87575241041`](https://github.com/happys2333/flowspan/actions/runs/29484373333/job/87575241041):
  success;
- macOS job
  [`87575241080`](https://github.com/happys2333/flowspan/actions/runs/29484373333/job/87575241080):
  success;
- Secret Scan job
  [`87575241076`](https://github.com/happys2333/flowspan/actions/runs/29484373333/job/87575241076):
  success.

Every OS job passed locked restore, format verification, warning-as-error
Release build, the full test command, explicit TEST MODE composition, the
deterministic protocol-1.1 simulator, and artifact upload. The downloaded
artifacts were:

- Windows artifact `8369847178`, `test-results-Windows`;
- Linux artifact `8369806312`, `test-results-Linux`;
- macOS artifact `8369793874`, `test-results-macOS`.

Each artifact contains 11 TRX files. Directly summing the `Counters` attributes
produced 701 total, 701 executed, 701 passed, 0 failed, and 0 not executed tests
on each OS. Thus the hosted count comes from downloaded test evidence rather
than only the job conclusion. Logs from every OS also contain
`Flowspan desktop composition validation passed in explicit TEST MODE.`,
protocol `1.1`, `Source preserved: True`, `Target resumed: True`, and
`Atomic swap committed: True`.

CodeQL run
[`29484373352`](https://github.com/happys2333/flowspan/actions/runs/29484373352)
and Analyze C# job
[`87575240903`](https://github.com/happys2333/flowspan/actions/runs/29484373352/job/87575240903)
completed successfully. CodeQL scanned 185/185 C# files and uploaded the result.

The evidence-closure commit containing this section remains subject to the same
CI, Secret Scan, CodeQL, and downloaded-TRX gates before task 3.3c is final.

## Remaining work and explicit limits

- Desktop has no Swap inventory, exact confirmation, progress, receipt,
  recovery, undo, or accessibility surface.
- The Activity catalog remains an external in-memory Adapter boundary in this
  composition; native semantic durability is not proved.
- Same-host encrypted loopback and hosted runners do not replace physical
  two-device LAN, disconnect/reconnect, sleep/wake, abrupt process termination,
  power loss, packaged-app, or native application evidence.
- The current tracer Activity does not migrate arbitrary application processes
  or private in-process state.

Therefore the implementation commit has closed its local and hosted gates, but
task 3.3c remains open until the evidence-closure HEAD passes the same gates.
Parent task 3.3, task 3.4, the Atomic Swap release criterion, and Flowspan v1
remain open.
