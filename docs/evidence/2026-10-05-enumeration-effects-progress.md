# macOS enumeration effects progress — 2026-10-05

Historical checkpoint; later implementation/evidence is recorded in the
[composition checkpoint](2026-10-05-enumeration-composition-progress.md).

This is a portable managed/controlled-effects checkpoint, not complete MEP,
native capture, cross-platform or v1 acceptance. The active Goal is unchanged.
Base: `f2cbdb8588482203751736d245718650c113f372`. Raw immutable stages are under
`/tmp/flowspan-enumeration-effects-20261005/`.

## Executed behavioral tracers

| Stages | Exact failing behavior | RED | GREEN |
| --- | --- | --- | --- |
| 01→02 | Dispatch throws after callback; confirmed content was leaked | 12P/1F | 13P |
| 03→04 | First pool pop fault skips content cleanup and hides earlier fatal | 13P/2F | 15P |
| 05→06 | First pool push after-effect fault leaves context open | 15P/1F | 16P |
| 07→08 | Second pool push after-effect fault returns unknown pool debt | 16P/1F | 17P |
| 09→10 | Completion release overwrites earlier dispatch fatal | 17P/1F | 18P |
| 11→12 | Duplicate invocation fault races confirmed retain into double release | 18P/1F | 19P |
| 17→18 | Contained completion release fault falsely publishes healthy sources | 23P/1F | 24P |
| 23→24 | First pool fatal after ordinary dispatch is overwritten by later content fatal | 24P/1F | 25P |

Stages13-16 are direct-GREEN regression additions, not RED→GREEN claims:
missing callback with bounded failure and rejected late retain (20P), healthy
nonempty source charge transfer (21P), exhausted capacity before another owned
effect (22P), and stale context/old callback isolation after actual batch reuse
(23P). Assertions execute through real enumeration, creation context, pool and
CreateSourceCore. Only external native effects/completion ABI are controlled.
Stage25 adds a direct-GREEN later-admitted-callback fatal ordering contract
(26P); it is not another claimed RED→GREEN pair. Both review counterexamples are
closed by one first-observed fatal CAS shared by every enumeration fault entry.

Dispatch and both thread-affine pools now retain attempted/confirmed facts in
the original batch ledger. Atomic result admission and closure share one CAS.
Failed dispatch with no admitted callback does not await an impossible exit
latch. Confirmed content is selected once after admitted invocation join, even
when a concurrent callback fault won result completion. Known completion release
is attempted independently; its normal confirmation does not erase a contained
ABI diagnosis. Independent known-source cleanup still runs before fatal escape.
Unknown effects keep the original graph and finite charge, with no guessed
release, retry, timeout return or metadata-bearing ordinary diagnostics.

## Saved-data checks

Every stage records command, cwd, actual exit, TRX, SDK, base, explicit overlay,
specification snapshots and source/runtime hashes. Selected source inventories
contain759 entries with before/after equality; they are not claims about every
compiler input. These six-file overlays explicitly exclude parallel staged Block,
new native probe and friend-assembly changes and retain the old Block bytes.

Through12 audit:

- Script: `/tmp/flowspan-enumeration-effects-audit-20261005/audit-effects-through12.py`
  SHA256 `97b77a5c87c3a3834bcd7de928ddbdf2765607e9457e23f5793ab2abc84f0f12`.
- Report: `report-through12.json`, SHA256
  `383261571aa21b164600ed5552e898188110942cd26e118f6817edb76ba17776`.
- Independent saved-data replays pass; root replay actually exits0 and its report
  is byte-identical. Complete test files are unchanged within each pair, exact
  failure names/messages/lines match, old test identities are retained, and source
  changes are restricted to the intended API/ledger repairs.

Through10 also has an actual root exit0 replay with byte-identical report.
The attempted root through12 invocation with unsupported `--through 12` returned
usage exit2; the corrected documented `--output` invocation then passed. This is
a checker invocation error, not a behavioral/native result.

Old stages19/20 pass project D/R341 and21/22 pass solution D/R2912, preserving
all2900 baseline identities. They are superseded by the review-found fatal
ordering repair, not current final-source gates. An attempted format check in
stage22's source without its default restore returned2 with unresolved references;
it is not a passing format result or a demonstrated code-format violation.

Current combined source is frozen under
`/tmp/flowspan-enumeration-combined-20261005/run02/`:766 selected files and matching
live-before/live-after/frozen inventories. It includes final effects26 and staged
Block19 contracts, the new probe and its friend assembly. The first freeze
candidate compared unsorted Git selection with sorted hashes and failed before
any build/test; its files and failed recorder are retained. Run02 corrects only
the recorder ordering. Actual locked restore and solution format now exit0.
Debug/Release solution builds and separate BlockProbe builds each exit0 with0
warnings/errors. Tests each exit0 with12 TRX/2933 Passed, including macOS362;
all non-success counters are0. Selected source and full bin runtime manifests
are byte-identical before/after each run. Full canonical qualified inventories
match D/R, retaining every old2900 identity and adding exactly effects14 plus
primitive19. Each full bin runtime inventory has1557 files. After documentary
updates,13 selected compiler/probe/test-owned files still match the frozen source;
this does not assert that every live document or every compiler input is identical.

Current saved-data audit and actual root replay both exit0, `violations=[]`;
reports are byte-identical:

```sh
python3 /tmp/flowspan-enumeration-effects-audit-20261005/audit-effects-through25.py --output /tmp/flowspan-enumeration-effects-audit-20261005/root-replay-through25.json
python3 /tmp/flowspan-enumeration-effects-audit-20261005/audit-combined-run02.py --verify-live-compiler-inputs --output /tmp/flowspan-enumeration-effects-audit-20261005/root-replay-combined-run02.json
```

Through25 script SHA256
`ff7481b29f21e5b15e0d01a009842e502b76f1d74c43984897266e38691c2681`;
report SHA256 `32c7bb80e8998ea5f4f36795ebfd1f476c9d0a33593930eca530b936fa409922`.
Combined script SHA256
`822dc143d5a4c8c632ca4f15ccc9b1bf32f298c3e96cc9ec715f61d3766aa6f4`;
report SHA256 `4952f28b98bfb661489e8e7f6644710ad759a4f2c5edc3eea0854d86ed22b620`.
The audit preserves old/superseded candidates and does not fabricate a freeze
exit file: root actually observed freeze exit0, independently corroborated by
the three equal manifests. No previous checkpoint's test/CI result is inherited.

Single-layer Standards and Spec review found the earlier fatal ordering issue,
which was actually reproduced in stage23 and fixed in24. Both axes then report0
remaining concrete findings on the frozen combined source, without calling
uncomposed task4 complete. Review is static, not an extra test execution.

## Remaining boundary

The first-idle managed invocation latch is not final ABI return, native-copy
retirement or exclusion of later native entry. Actual staged Block ownership and
an independently executed extra-native-copy probe now have local evidence in the
[staged Block checkpoint](2026-10-05-staged-block-progress.md); neither is yet
composed into enumeration. Completion factory-local debt remains
unresolved on this old Create path. The combined local D/R and task-owned Block
ABI result do not replace complete enumeration/SCK
lifetime acceptance or fresh exact-commit hosted gates, which remain open.

Production remains `delegate=0`, macOS14.2/Arm64 candidate, Protection Unknown
and sharing unavailable. Global Capture admission, secure input/protection,
Emergency Stop, physical LAN, minimum-platform/accessibility, independent security
review and signed install/update/uninstall acceptance are not established.
