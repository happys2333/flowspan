# Activity Groups and Scene Plans Evidence — 2026-07-25

## Evidence boundary

This record closes the automated evidence for task 8.1 only: immutable ordered
Activity Groups, typed version-1 Scene plans, and their strict local canonical
JSON codec. It does not implement or validate Scene preview/apply, per-Activity
execution results, compensating undo, repository persistence, inspect/delete/
export UI, native application migration, or physical two-device behavior.

Branch: `codex/v1-foundation`

Specification commit:
`25a32815e0360408ba645e09678785d80faddc90`.

Implementation commit:
`d65e24790dc8b0cdaa6f32522e56a5611b57d2d8`.

The task-status commit containing this record is effective only after that
exact commit passes the same CI, Secret Scan, and CodeQL workflows.

## Implemented contract

- A Group has a non-empty opaque ID, positive monotonic revision, bounded
  control-free well-formed Unicode name, and an immutable ordered snapshot of
  1 through 64 distinct Activity IDs. Nested Groups are not representable.
- A Scene has an independent opaque ID, monotonic revision, format version 1,
  and 1 through 64 exact Activity placements. Policies map only to existing
  Handoff/Move and Require Empty/Replace With Undo semantics.
- A Group-derived Scene freezes the Group ID/revision and the exact expanded
  Activity order. Later Group edits cannot silently expand a saved Scene.
- The local codec is a closed 32 KiB/depth-8 schema. It rejects unknown,
  duplicate, missing, mistyped, over-bound, trailing, non-canonical ID,
  unsupported-version, malformed Unicode, control-character, and secret-canary
  fields before publishing a Scene.
- The 583-byte canonical fixture has SHA-256
  `1BD613EBA1866B9D6AD1533CF052261DFF91A71D525A68339B51B83DCC0AE0D3`.
- Group and Scene diagnostic strings omit names, placement slots, and Activity
  membership. The schema has no descriptor, payload, Adapter state, Trust,
  Capability snapshot, session, traffic key, reservation, or Undo Capsule
  field.

## Local environment and commands

```text
Host: macOS 26.5.2 (build 25F84), Apple Silicon, Asia/Shanghai
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
RID: osx-arm64
```

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
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

## Local results and review

- Locked restore and format verification passed.
- The Release build passed with 0 warnings and 0 errors.
- All 824 tests passed with 0 failed and 0 skipped:
  - Desktop 162, Transport 219, Integration 150, Security 123;
  - Domain 60, Protocol 28, shared platform 16;
  - Windows platform 18, macOS platform 16, Linux platform 18;
  - mDNS transport 14.
- Desktop composition printed its explicit TEST MODE success line.
- The deterministic simulator selected protocol 1.3 and reported preserved
  source, resumed target, and committed Atomic Swap. This remains a same-host
  deterministic simulator, not Scene-apply or physical-device evidence.
- The direct/transitive vulnerability query reported no known vulnerable
  package in all 24 projects.
- The 21 Group/Scene domain tests and 9 codec tests each passed in 20/20 fresh
  `dotnet test` processes.
- Standards and Spec review each closed with 0 findings. Spec review first
  found that trimming could hide leading/trailing controls; public domain and
  codec regressions failed before the fix, then passed after raw Group name,
  Scene name, and placement slot validation moved before normalization.
- Lone UTF-16 surrogates and JSON surrogate escapes are rejected, while valid
  surrogate pairs round-trip and the maximum Unicode Scene stays within its
  own decoder bound.

These are local macOS and portable same-host results. Platform-named contract
projects do not prove native Windows or Linux APIs.

## Hosted exact-commit evidence

Implementation commit `d65e24790dc8b0cdaa6f32522e56a5611b57d2d8`
passed [CI run `29507314008`](https://github.com/happys2333/flowspan/actions/runs/29507314008):

- macOS job [`87651324739`](https://github.com/happys2333/flowspan/actions/runs/29507314008/job/87651324739);
- Ubuntu job [`87651324764`](https://github.com/happys2333/flowspan/actions/runs/29507314008/job/87651324764);
- Windows job [`87651324810`](https://github.com/happys2333/flowspan/actions/runs/29507314008/job/87651324810);
- Secret Scan job [`87651324778`](https://github.com/happys2333/flowspan/actions/runs/29507314008/job/87651324778).

Every OS job restored locked dependencies, verified formatting, built with
warnings as errors, ran all tests, validated Desktop composition in explicit
TEST MODE, ran the protocol-1.3 simulator, and uploaded test evidence. Hosted
runners prove portable build and contract behavior on those runner images, not
physical two-device networking or native permission behavior.

Downloaded artifacts were independently hashed and parsed:

| Artifact | ID | SHA-256 | Files/results |
| --- | ---: | --- | ---: |
| Windows TRX | `8379122374` | `8f57349b2ee5f85a0afad0e43c66bc8af6872a380395ccfe751abb469cb3b51d` | 11 TRX |
| macOS TRX | `8379098096` | `a7cb9f56f2791af6bb46104450087a2f02db0f2fd40b938c91f6e444f5eb404a` | 11 TRX |
| Linux TRX | `8379087315` | `60becaae9ac93532c0c81b9ab8e688f3ef122a812b403b6949f105ad1d382e6c` | 11 TRX |
| Gitleaks SARIF | `8379035637` | `8245479ad7d697d1560710dd94d7379256f3caf7490ff7f387f8f180a05d1494` | 208 rules, 0 results |

Summing every downloaded TRX `Counters` element independently produced the
same result on Windows, macOS, and Ubuntu:

```text
files=11 total=824 executed=824 passed=824 failed=0 error=0 timeout=0
aborted=0 inconclusive=0 passedButRunAborted=0 notRunnable=0
notExecuted=0 disconnected=0 warning=0 completed=0 inProgress=0 pending=0
```

[CodeQL run `29507314069`](https://github.com/happys2333/flowspan/actions/runs/29507314069),
job [`87651325326`](https://github.com/happys2333/flowspan/actions/runs/29507314069/job/87651325326),
also passed for the exact implementation commit. CodeQL 2.26.0 evaluated 52
rules in analysis `1487840062` and reported 0 results, no warning, and 0 open
alerts for the branch.

## Remaining gates

- Task 8.2 must plan, authorize, preview, and execute each Scene item against
  current Trust, Capabilities, Activity state, and Replace confirmation/undo,
  then report deterministic per-Activity partial outcomes.
- Task 8.3 must add a private atomic Scene repository and inspect/delete/export
  behavior with filesystem protection and redaction.
- Native Adapter, accessibility, packaged three-OS, physical two-device,
  independent security-review, cryptographic-review, and release-wide v1 gates
  remain open. This task does not claim arbitrary application process-state
  migration.
