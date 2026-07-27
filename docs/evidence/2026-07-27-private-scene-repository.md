# Private Scene Repository Evidence — 2026-07-27

## Evidence boundary

This record closes the Scene portion of task 8.3: a bounded private atomic
repository for canonical Scene plans, purpose-separated protected stores on
Windows, macOS, and Linux, a redacted export, and the Desktop inspect/select/
delete/export lifecycle. It does not close Scene or Group creation UI, Group
persistence, import, trust/history/diagnostics lifecycle, packaged
distribution, or physical two-device behavior.

All local results are macOS-host same-host evidence. Hosted results are
runner-image evidence. Neither is physical-device evidence or native packaged
application evidence.

Branch: `codex/v1-foundation`

Implementation commit:
`9dc38610e730ca3bc240115eacc16a00d5c58bc7`
(`feat: store Scene plans in a private repository`).

The task-status commit containing this record is effective only after that
exact commit passes the same CI, Secret Scan, and CodeQL workflows.

## Implemented contract

- `PersistentSceneRepository` stores 0–64 exact canonical Scene plans in one
  strict bounded envelope. Mutations persist a complete candidate before
  publishing it, and failed or ambiguous saves poison mutations until reopen.
- The authenticated state-file engine uses `FSCR` magic and independent
  Keychain, Secret Service, and DPAPI key purposes. Unsupported or failed
  platform stores degrade the Desktop feature without unprotected fallback.
- The Desktop lists and inspects local private Scene data, selects only through
  `SceneApplyViewModel.SelectScene`, requires two-step exact delete
  confirmation, and keeps `NOT SHARING` unchanged.
- Redacted export structurally omits Scene names, Activity IDs, Device IDs,
  slots, payloads, and exception text. Export files are create-new,
  owner-only on POSIX, and reject reparse targets and unsafe file names.

## Local environment and commands

```text
Host: macOS 26.5.2 (build 25F84), Apple Silicon, Asia/Shanghai
.NET SDK: 10.0.301
.NET runtime: 10.0.9
RID: osx-arm64
```

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx -c Release --no-restore
dotnet test Flowspan.slnx -c Release --no-build --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  -c Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  -c Release --no-build --no-restore
dotnet list package --vulnerable --include-transitive
git diff --check
```

Focused stress used 20 fresh processes for each command:

```sh
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~SceneRepository'
dotnet test tests/Flowspan.Integration.Tests/Flowspan.Integration.Tests.csproj \
  -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~SceneRepository'
```

## Local results and review

- Locked restore and format verification passed.
- The Release build passed with 0 warnings and 0 errors.
- All 1118 tests passed with 0 failed and 0 skipped:
  - Desktop 190, Transport 255, Integration 321, Security 123;
  - Domain 60, Protocol 47, shared Platform 32;
  - Windows Platform 26, macOS Platform 24, Linux Platform 26;
  - mDNS Transport 14.
- Desktop composition printed its explicit TEST MODE success line, and the
  deterministic simulator exited successfully.
- The direct/transitive vulnerability query reported no known vulnerable
  package, and `git diff --check` passed.
- Desktop SceneRepository coverage passed 19 tests in every one of 20 fresh
  processes. Integration SceneRepository coverage passed 21 tests in every
  one of 20 fresh processes.
- Persistence, security/privacy, Desktop/UI, and specification review closed
  two real findings before the implementation commit: decrypted canonical
  buffers are now zeroed, and export file names use a strict ASCII whitelist
  that blocks Windows alternate data streams. Regression tests cover both.
- Fault tests cover healthy-instance delete failures before write and after
  ambiguous write, candidate non-publication, poisoning, and durable-truth
  recovery after reopen. Native platform tests assert off-OS rejection.

These results are local macOS and portable same-host evidence. Platform-named
contract projects do not by themselves prove native Windows or Linux APIs.

## Hosted exact-commit evidence

Implementation commit `9dc38610e730ca3bc240115eacc16a00d5c58bc7`
passed [CI run `30235457186`](https://github.com/happys2333/flowspan/actions/runs/30235457186):

- Windows job [`89882320158`](https://github.com/happys2333/flowspan/actions/runs/30235457186/job/89882320158);
- macOS job [`89882320161`](https://github.com/happys2333/flowspan/actions/runs/30235457186/job/89882320161);
- Ubuntu job [`89882320165`](https://github.com/happys2333/flowspan/actions/runs/30235457186/job/89882320165);
- Secret Scan job [`89882320108`](https://github.com/happys2333/flowspan/actions/runs/30235457186/job/89882320108).

Each OS job restored locked dependencies, verified formatting, built with
warnings as errors, ran all tests, validated Desktop composition in explicit
TEST MODE, ran the simulator, and uploaded test evidence.

Downloaded artifacts were independently hashed and parsed:

| Artifact | ID | SHA-256 | Files/results |
| --- | ---: | --- | ---: |
| Windows TRX | `8641517658` | `a4d5fa1ae58c00ac51df23450bdf8f9dce69890e47c1e582c06cdac1c7eaed0d` | 11 TRX |
| macOS TRX | `8641493745` | `c9b80fcd50e1444cc91f9aa361ecb2303d946faccbb38a2efe0f8b1e8811570a` | 11 TRX |
| Linux TRX | `8641506915` | `aa5d638415615ed9b2e96fe1a2cceb202ae8b9aa0321703f24ad4266191a78f7` | 11 TRX |
| Gitleaks SARIF | `8641479479` | `bd57e10f979b56ba8e66c5e9df000944baef80ec47e5888c559676388a5c2800` | 208 rules, 0 results |

Summing every downloaded TRX `Counters` element independently produced the
same result on Windows, macOS, and Ubuntu:

```text
files=11 total=1118 executed=1118 passed=1118 failed=0 error=0 timeout=0
aborted=0 inconclusive=0 passedButRunAborted=0 notRunnable=0
notExecuted=0 disconnected=0 warning=0 completed=0 inProgress=0 pending=0
```

[CodeQL run `30235457178`](https://github.com/happys2333/flowspan/actions/runs/30235457178),
job [`89882320136`](https://github.com/happys2333/flowspan/actions/runs/30235457178/job/89882320136),
also passed for the exact implementation commit. Analysis `1529915723`
evaluated 52 rules and reported 0 results and 0 open branch alerts.

Hosted runners prove portable build and contract behavior on those runner
images, not physical two-device networking or native packaged behavior.

## Remaining gates

- Scene and Group creation UI, Group persistence, and import remain open.
- Trust, history, and redacted diagnostics inspect/delete/export lifecycle
  remain open under task 8.3.
- Native Adapter, physical-device, packaged three-OS, external independent
  security/cryptographic review, and release-wide v1 gates remain open.
