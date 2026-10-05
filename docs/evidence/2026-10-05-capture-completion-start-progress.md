# Same-Capture Start completion checkpoint — 2026-10-05

Status: finite MCC tasks1–2 accepted locally. Full MCC, native, hosted,
production and v1 acceptance remain open; Goal active.
Read-only base: `1ab251135f3617bc18e39d5f67e38885b53e85a7`.
Immutable stages: `/tmp/flowspan-capture-completion-start-20261005/`.

## Actual first ownership tracer

`red-setup01` fails compilation with CA2012 and is not behavioral RED.
`red01` compiles and fails the actual premature shell-charge return. `red02`
adds the full weak-graph tuple: expected `(1,T,T,T,T,T)`, actual
`(0,F,F,F,F,F)` after actual Start failure, Stop/two Dispose calls, fixture raw
cleanup and forced GC. It observes the original shell, actual one-argument
completion/primitive, operations and retained-source graph without strong test
roots. Fixture cleanup removes only its raw native memory/GCHandles; it does not
invoke production cleanup helpers or reset retained-owner accounting.

`green01` passes the unchanged first Fact and complete test-file bytes, SHA256
`a002a99c0b03d94cbd02b78eca16a99357a05e99098bd58c39ead5bd3ee9e163`.
Only the adapter and Capture orchestration change within that pair: acquisition
moves from the factory to after attachment to the exact original Capture.
The shared staged primitive uses the actual `InvokeOne`, Cdecl
`void(block, object)` and `v16@?0@8`. Legacy zero/two-argument Create users stay
unchanged. Unknown copy receives no guessed release/free/invoke; independently
confirmed native/source cleanup receives one attempt and original nested fatal
identity survives Stop and both Dispose calls.

`healthy01` is a separate direct-GREEN contract through actual typed one-argument
Start and Stop, not a second claimed RED. Stop acquisition plumbing permits that
healthy path; it is not Stop factory fault, native-copy lifetime or race proof.

## Final portable gates

Expanded final tests add exactly two Facts. A nonparallel collection declared in
the new partial file isolates the existing Capture class from other global
retained-owner observations; existing test files remain byte-identical.
Focused02 Debug/Release each pass **110** with equal complete qualified sets.
Project01 Debug/Release each pass **379**: all377 historical macOS identities
plus exactly the two new Facts, with0 removed and all non-success counters0.
The historical baseline is repair02; it is not evidence for this new code.

Each final configuration binds564 selected source inputs and138 complete bin
runtime files before/after/actual, including each TRX codeBase/storage binding.
All188 original selected test C# files remain byte-identical; the separate
controlled Block runtime only gains system-effects instrumentation. Focused01
used a broader-named filter that did not select the staged Block class; only
Focused02 is the final explicit staged-Block/Capture regression set.

Quality01 preserves MSB4166 restore failure and its subsequent missing-assets
format failure, not a passed quality gate. New quality02 runs locked solution
restore with `--disable-parallel -m:1`, then format verification, both actual
exit0. Project builds are warning/error free. This is not a new full-solution
Debug/Release test freeze, which remains MCC task7.

Final selected-input manifest SHA256:
`2ac3eedf05b1c01dbc9e50bbe6c0e7e4b77810a8dac6eecf3ce8ec74512d4501`.
Auditor SHA256:
`0ced1a59a3d7ff131b494e9021212c4efe6839d1c11a25dc22ded2c9e6e79650`.
Replay wrapper SHA256:
`33d3d05d289e845bc8aa884d05f32c22b07d490cce8df2210faa5de378b1c4e6`.
Saved report SHA256:
`0103553594b3bc0f11b3c5aa50db5ed8324cda2741d7b910e8175f6a7dc84e66`.
Current selected-input report SHA256:
`d22efcb6d4d65e18d9a593f4817942981dee3bf9eb05b4be2c07dd1bfd7f408e`.

Worker replays and root's actual saved/current replays each exit0 with
`violations=[]`; each root report actually compares byte-identical to its
worker counterpart. Current mode checks selected build inputs only, not the
whole live tree or concurrently updated root documentation. Subsequent Stop
implementation changes will supersede this current-equality observation, not
the immutable saved proof.

Fresh-label read-only replay (labels must not already exist):

```sh
bash /tmp/flowspan-capture-completion-start-20261005/replay.sh next-saved01 saved
```

Both static review axes finish with0 concrete findings against the stable six
code-file hashes. Review and saved-data replay are not additional tests/native
executions. The evidence recorder checks exact commands/configurations/filters,
qualified identities, compiler summaries and full saved receipts.

## Remaining boundary

MCC tasks3–8 remain open: Stop faults, unknown/invalid root effects, release/free
uncertainty, four thread-affine pool scopes, resource-use closure/joins, race/
reentry and physical-copy/terminal-managed lifetime, new one-argument native
ABI/healthy SCK, full-solution and fresh exact-SHA hosted gates. Production
still uses delegate=0, macOS14.2 ordinary-arm64 candidate, Protection Unknown
and sharing unavailable. This per-shell retention proof does not compose the
process-wide max-16 Capture permit or establish native fault containment,
physical/release or v1/Goal acceptance. See
[ADR0034](../adr/0034-staged-macos-capture-completion.md) and
[MCC tasks](../../specs/v1/native-remote-window/macos-capture-completion/tasks.md).
