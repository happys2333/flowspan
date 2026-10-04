# macOS stream delegate router — portable evidence

Status: portable implementation and final local verification passed. Exact new-
commit hosted CI/CodeQL evidence remains pending; this is not Windows/Linux
native or production Capture evidence.

Scope: [MSC task 1](../../specs/v1/native-remote-window/macos-stream-delegate/tasks.md)
under [ADR 0030](../adr/0030-generation-routed-macos-stream-delegate.md). The
numeric-generation router is portable and has no native calls. It is not wired
into Capture, which still passes `delegate=0`. Native association/tag lifetime,
real source loss, production host sharing, protection, input, independent
Emergency Stop, physical devices and release/v1 acceptance remain open.

## Reproducibility and evidence ownership

Actual incremental failing/passing executions and focused records are retained
in `/tmp/flowspan-msc-router-20261005/`, with the original frozen manifest in
`RESULTS.md`. No dependency or production-language change is part of this slice.

Final source SHA-256:
`5313b23afcc1edc561372f609ba0ff16db19b435af4ff6dc2aacb44a4e1b445e`.
Final tests SHA-256:
`ffbabf061b4a056d6a0af9b4e1a9ce3cad1883807229613d8a449194b3782502`.
The original worker test freeze was `36f3067f...321acf`; root added the reviewed
outside-handler failure assertion and independently reran the final inputs.

## Executed portable behaviors

Thirteen incremental behavioral RED→GREEN stages contain 16 actual failing
cases: terminal without sample, terminal before activation, exclusive
initializer, published-failure poison, generation saturation, blocked
retirement, direct/active descendant self-join, ordinary/original/depth-65 OOM
faults, complete-cleanup permit reuse, permanent quarantine, unconfirmed
initialization retirement, and stale/unpublished/unknown signal isolation.
Every RED and corresponding GREEN case inventory matches. Stage 4's
unpublished variant was already green; stage 9 has three failed cases and
stage 12 has two. Explicit exception injection is not actual allocator
exhaustion. The other 16 final cases are supplemental already-green contracts,
not manufactured RED evidence.

Initial CA1822 skeleton failure and supplemental CS1503/CA2012 test-authoring
errors executed no tests and have separate logs, not behavioral RED counts.
Spec review found that a callback-local assertion could be contained without
failing its test. Root added an outside-handler `Failure == null` check after
callback completion. This assertion-strengthening is not a production fix or
a new behavioral RED claim. Final Standards and Spec reviews both report zero
remaining findings and verify the final input hashes above.

Final focused Debug/Release each pass 33/33 (`root-review-debug.trx` and
`root-review-release.trx`). Four concurrent lanes, ten fresh Release processes
each, independently pass 1320/1320 cases in
`/tmp/flowspan-msc-final-pressure-20261005/`. Each process includes 32 gated
cycles of duplicate terminal callbacks and eight retirement waiters; all
results are Passed, not merely successful wrapper exits.

## Complete local solution verification

Actual host: macOS 27.0.1 / build 26A434, ordinary arm64, .NET SDK 10.0.301.
Locked restore and final complete no-change formatting pass. Final Debug and
Release warning-as-error builds each report zero warnings/errors. Both final
complete solutions have exactly 12 TRX and 2743 total/executed/Passed; all other
counters and non-Passed results are zero. MacOS project is 177/177 in each.
Both complete case inventories match, SHA-256:
`3ae5a207abfb60f9efe89d91bc689e3ce0fd7bc9a1a9593dcc6b72ea5b68ed0f`.

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Debug --no-restore
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore --logger 'trx;LogFilePrefix=msc-final-debug' --results-directory /tmp/flowspan-msc-final-solution-debug-results-20261005 --blame-hang --blame-hang-timeout 3m --blame-hang-dump-type none
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore --logger 'trx;LogFilePrefix=msc-final-release' --results-directory /tmp/flowspan-msc-final-solution-release-results-20261005 --blame-hang --blame-hang-timeout 3m --blame-hang-dump-type none
python3 /tmp/flowspan-msc-local-audit-20261005.py
```

The independent audit actually verifies both full suites, final focused pair,
all 40 final pressure TRX, all 13 actual RED/GREEN pairs and final source hashes.
Output `/tmp/flowspan-msc-local-audit-20261005.json`, SHA-256
`cbfda5f99de8e740ce1af722d9df4893e23224054989f7693956deaad405abed`.
Earlier pre-review solution/pressure runs remain separately preserved and are
not substituted for these final-input results.

Explicit desktop TEST MODE composition and deterministic protocol-1.7 simulator
each return exit 0. The including-transitive NuGet vulnerability query covers
26 projects, reports no vulnerable packages or query errors, returns exit 0
and has zero-byte stderr. This is a point-in-time advisory query, not universal
security proof. All checks here are local macOS portable/managed execution.
Exact new-SHA Windows/macOS/Linux downloaded TRX, Secret Scan and CodeQL remain
pending; older c27532d hosted results are not inherited by this implementation.

## Native-stage compile-only precheck

Root actually compiled `/tmp/flowspan-msc-tag-abi-20261005.m` to LLVM IR using
Apple Clang 21.0.0, SDK 27.0, ordinary-arm64 target macOS 15.2, `-fno-objc-arc`
and warnings as errors:

```sh
xcrun clang -arch arm64 -mmacosx-version-min=15.2 -fno-objc-arc -Wall -Wextra -Werror -S -emit-llvm /tmp/flowspan-msc-tag-abi-20261005.m -o /tmp/flowspan-msc-tag-abi-20261005.ll
```

Static assertions passed for 16-byte/8-aligned `objc_super`, receiver/class
offsets 0/8 and 8-byte numeric generation. Compiler metadata reports dealloc
encoding `v16@0:8`, generation type `q`, alignment-log2 3. The language-level
super send emits `objc_msgSendSuper2` with current-class convention; the typed
helper emits `objc_msgSendSuper` and requires the verified superclass. The
native implementation must use runtime-reported ivar offset and must not read
the object after successful superclass deallocation.

Source SHA-256:
`aa90955151976fa0aefb482265630890ad3898faadb9076bc8b2ab015fca3a14`.
IR SHA-256:
`773b93c3829fd28756f2a9ff1770bb4fc680f27ad1e6365d089db9f1cbf2f1c1`.
Independent source/IR review found no remaining ABI-shape issue.

This temporary Objective-C translation unit was not linked or executed and
adds no product shim/toolchain. It is compile-time shape evidence only: not
C# struct/PInvoke execution, Foundation association/deallocation, minimum-OS,
native exception safety, Capture integration or MSC task 2 completion.
