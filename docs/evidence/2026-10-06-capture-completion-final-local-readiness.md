# MCC final local readiness — 2026-10-06

Status: finite implementation/source review ready for recorded gates, not MCC
acceptance. Base `1ab251135f3617bc18e39d5f67e38885b53e85a7` with dirty overlay.
The actual base..HEAD commit list and three-dot diff are empty; review therefore
reads base-to-worktree tracked changes and full new completion/test/probe files,
not an empty committed diff. Authoritative source is
`specs/v1/native-remote-window/macos-capture-completion/` and ADR0034.

## Final review and one static probe repair

Spec has0 finite findings. Standards initially has1 hard violation and0 judgement
calls: `CaptureCompletionLifetime.cs` finally attempts release even when its
invocation-thread Join returns false or throws. A thread not yet in the ABI could
later enter a released Block pointer; the final assertion cannot undo release.

The minimum static repair guards both still-unattempted caller and known-extra
releases with `joined`. False/throw retains their references and waiting events,
then the probe fails. A null invocation is the separately known no-thread case.
Normal healthy lifetime execution remains unchanged. README states the same
limits. Final Standards and separate Spec delta review each close with0 findings.
This is source review/repair, not an executed runtime RED or successful timeout
cleanup. New frozen builds/default CLI and healthy native gates remain required.

- Probe source SHA256:
  `9c2ca8cb87b9a1fd0cdb913a2cc3789d42cbc2f4e40e9dc2453485744db0ddb5`.
- Probe README:
  `7637ec8fc76a41206da7fe0c8b666b5012cc8028896a2e803bbdd07cb0eba889`.
- Accepted164 TEST remains
  `5a67a6f913ae7aeba5c76e63c2d224cb8e53459dd21cede0f2ea03760d53ae3f`.
- API remains
  `e6861e8c3ad27b37dea28285c1d3af8015758241435ed0bfbca5cf38eb0ab52a`.

Review covers shared action/failure/completed closure and joins, active/inactive
ancestry/self-join, first-result winners, borrow exit and four original-thread
pools, unknown-effect no-retry, legacy zero/two ABI, ordinary known-held recovery,
strict gate and CI classification. It is static, not new test/native execution.
The original Scope and evidence limits remain unchanged.

## Authorized next gate sequence

Root confirms task5 finite implementation/test/source-review readiness; full
MCC/MSC/v1/Goal remains unverified. The complete current Git-selected tree will
be frozen once in `/tmp/flowspan-mcc-final-local-20261005/campaigns/final164-01/`.
Keep live source/Git state fixed until worker and actual root current replays
finish; then update execution evidence. Failed stages cannot be overwritten.

Prepared identity inputs are `inputs-final164-01/` under that toolkit: baseline
2951 historical identities,56 actual additions,164 repeat identities. They
preserve identities only, never historical PASS. Actual final full-solution
counts still require fresh TRX. Expected union3007 is not a claimed test result.

Required new gates: full solution Debug/Release, locked quality/format/analyzers,
dependency/secret/portable-gate security checks,10 focused processes per config,
exact-source native one-argument Block D/R and healthy task-owned Capture D/R,
then new committed-SHA all-OS CI/CodeQL after suitable local gates.
Only existing TCC may be used; missing permission is Skip/unverified, no request.
GitHub communication keeps its exact-text per-target approval gate.

This record declares readiness only. No formal campaign, full solution, scanner,
new native run, new hosted result, commit/push or complete acceptance is claimed
here. Delegate0, Protection Unknown, uncomposed max16 Capture permit and unavailable
production sharing remain unchanged.
