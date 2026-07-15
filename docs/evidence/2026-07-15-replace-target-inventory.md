# Purpose-Scoped Replace Target Inventory Evidence — 2026-07-15

## Evidence boundary

This slice proves a query-only, authenticated Replace target inventory for the
bounded semantic Activity model. It does not expose or compose destructive
Desktop Replace, perform physical two-device or LAN testing, preserve arbitrary
application process state, or prove native accessibility.

Verified implementation commit:
`23d1ba4a9688902f6e38d0c82138dc7829815af5`

Branch: `codex/v1-foundation`

Local host: macOS 26.5.2 (build 25F84), Apple Silicon, Asia/Shanghai

Toolchain:

```text
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
RID: osx-arm64
```

## Commands

```sh
dotnet restore Flowspan.slnx --locked-mode --nologo
dotnet format Flowspan.slnx --no-restore --verify-no-changes --verbosity minimal
dotnet build Flowspan.slnx --no-restore --configuration Release --nologo
dotnet test Flowspan.slnx --no-restore --no-build --configuration Release --nologo
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
git diff --check
```

The three inventory-sensitive groups also ran in 20 fresh testhost processes
per command:

```sh
dotnet test tests/Flowspan.Integration.Tests/Flowspan.Integration.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~ReplaceTargetInventoryTests'
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~ReplaceInventory'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Replace'
```

## Local results

- locked restore: passed for all 24 projects;
- formatting and patch whitespace checks: passed;
- Release build: passed with 0 warnings and 0 errors;
- tests: 516 passed, 0 failed, 0 skipped:
  - Desktop: 108;
  - Transport: 137;
  - Integration: 65;
  - Security: 90;
  - Domain: 33;
  - Protocol: 17;
  - Platform contracts: 10;
  - Windows platform contracts: 14;
  - macOS platform contracts: 12;
  - Linux platform contracts: 16;
  - mDNS transport contracts: 14;
- fresh-process inventory stress: 20/20 processes for each group, with 15
  Integration, 20 Transport, and 4 Desktop filtered tests passing per process;
- explicit Desktop TEST MODE composition: passed;
- NuGet audit: no known vulnerable direct or transitive package in all 24
  projects;
- deterministic simulator: passed, but it still exercises Handoff and is not
  counted as Replace evidence.

## Proven behavior

### Purpose and authorization

- the source checks its current peer-relative `activity.receive` before channel
  lookup or query send;
- the target reloads the requesting peer's current `activity.replace` on every
  query; removing that grant in an existing encrypted session immediately
  returns `CapabilityDenied` with no target metadata;
- `activity.replace` is one independent any-of admission capability for an idle
  Activity control channel, but admission grants no inventory or destructive
  operation by itself;
- the control channel and Desktop status remain idle and `NOT SHARING`.

### Bounded payload-free projection

- only target-local, active, normal-sensitivity, same-kind Activities with an
  `IReplaceActivityAdapter` are eligible;
- sensitive, restricted, closed, non-local, different-kind, unsupported, and
  handoff-only Adapter Activities are omitted;
- one result contains at most 64 unique snapshots in strict Activity-ID order;
  a truncated result contains a full page and marks truncation explicitly;
- each snapshot contains only Activity ID, positive revision, descriptor
  digest, kind, bounded title, and bounded Placement slot;
- descriptor payload, payload digest, origin device, and protected metadata are
  absent; title and Placement control characters are rejected;
- the endpoint samples time once for deadline and capture, and the result model
  stops enumerating after the 65th item establishes an oversize page.

### Protocol and session failure behavior

- `activity.replace.inventory` binds correlation, target, incoming kind, and a
  deadline inside the authenticated envelope lifetime;
- `activity.replace.inventory.result` additionally binds requesting device,
  target sender, query deadline, capture time, strict schema, and the exact
  bounded snapshot shape;
- unknown fields, malformed digests, wrong participants/purpose/deadline,
  capture-after-send, duplicate/unsorted targets, rejected-result disclosure,
  and oversized arrays fail closed;
- Transfer, Replace inventory, and destructive Replace share one atomic pending
  correlation reservation, so message types cannot reuse a live correlation;
- disconnect after send becomes `AcknowledgementLost`; unsolicited or
  wrong-correlation results fault the session closed;
- a deterministic stop-versus-registration race proves a query cannot register
  after the session drain and wait forever.

### Desktop composition boundary

- two authenticated Desktop runtimes query the real encrypted inventory channel
  and project a payload-free snapshot without modifying either Activity catalog;
- the Desktop runtime composes only `IReplaceTargetInventoryPeer`;
  `IReplacePeer` remains `null`, and there is no Replace button, destructive
  command, or target undo control;
- the later destructive command still has to carry and revalidate the selected
  target ID/revision/digest before capture, resume, or catalog mutation.

## Hosted results for the implementation commit

GitHub Actions CI run
[`29412409556`](https://github.com/happys2333/flowspan/actions/runs/29412409556)
completed successfully for the exact implementation commit:

- Secret Scan job
  [`87342366246`](https://github.com/happys2333/flowspan/actions/runs/29412409556/job/87342366246):
  success;
- macOS job
  [`87342366250`](https://github.com/happys2333/flowspan/actions/runs/29412409556/job/87342366250):
  success;
- Ubuntu job
  [`87342366255`](https://github.com/happys2333/flowspan/actions/runs/29412409556/job/87342366255):
  success;
- Windows job
  [`87342366264`](https://github.com/happys2333/flowspan/actions/runs/29412409556/job/87342366264):
  success.

Each OS job passed locked restore, formatting, warning-as-error Release build,
all 516 tests, explicit TEST MODE composition, the deterministic Handoff
simulator, and evidence upload.

CodeQL run
[`29412409555`](https://github.com/happys2333/flowspan/actions/runs/29412409555)
and Analyze C# job
[`87342366032`](https://github.com/happys2333/flowspan/actions/runs/29412409555/job/87342366032)
completed successfully for the same exact commit.

Hosted runners prove portable build, contracts, same-host loopback, and the
native-safe tests already present in the matrix. They do not prove physical LAN
reachability, two physical devices, packaged applications, native accessibility,
or arbitrary-application Adapters.

## Remaining before the Replace release criterion can pass

- a Desktop destructive preview that identifies the incoming and selected
  target Activities and detects a stale selection;
- explicit destructive confirmation with keyboard and screen-reader contracts;
- visible receipt and restart-recovery state;
- target-local undo with exact expiry and outcome presentation;
- only after those safeguards, production composition of `IReplacePeer`;
- live native Adapter evidence beyond `workspace.note/v1`, physical two-device
  LAN interruption/revocation, and packaged Windows/macOS/Linux tests.

This evidence completes only task 7.3c.3. Task 7.3c, its parent 7.3, and the v1
criterion “Replace offers an honest undo capsule or blocks before destructive
work” remain unchecked.
