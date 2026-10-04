# Exact 974e954 hosted association checkpoint

Status: exact-commit CI, CodeQL and preserved-input artifact audit passed.
This closes the repaired hosted test checkpoint and MSC Phase 2a's hosted
Foundation gate, not MSC task 2, actual Capture, physical devices or v1.
The failed f989 checkpoint remains preserved in
[the router evidence](2026-10-05-macos-stream-delegate-router.md).

## Exact source and results

Commit `974e954097eaaa6a58408caf5d06c53262cd79d4`,
`refs/heads/codex/v1-foundation`, push, attempt 1, run number 244:

- [CI 37223583226](https://github.com/happys2333/flowspan/actions/runs/37223583226):
  completed success; all eight expanded jobs succeeded.
- [CodeQL 37223583281](https://github.com/happys2333/flowspan/actions/runs/37223583281):
  completed success; its analysis job succeeded.

Saved workflow bytes match `git show` of that exact commit. The audit reads
committed source, never later working-tree changes. Completed API records and
actual watch exits are retained separately from the initial in-progress state.

| Hosted OS | TRX | total/executed/Passed | Desktop | MacOS assembly |
| --- | ---: | ---: | ---: | ---: |
| Windows | 12 | 2746 | 760 | 177 |
| macOS | 12 | 2746 | 760 | 177 |
| Linux | 12 | 2746 | 760 | 177 |

Every other counter is zero; every result is Passed and project summary
Completed. The full qualified case inventories match, SHA-256
`0eef9bf4431a094af0c0629af09dbb7e09ed97789746dd12361e37d2d21aa9cb`.
Each host independently passes the router's 33 cases, pairing's ten cases
including its original unfinished case and three new regression rows, and the
previously failed permission-race case. No permission case was added or removed.
These are portable hosted tests, not three-platform native capture evidence.

## Downloaded artifacts and native boundary

All six selected small ZIPs were downloaded. Exact run/SHA/ref/repository,
API/upload size and digest, recomputed local ZIP hash and every member hash
agree. There are no downloaded dumps.

| Artifact | ID | ZIP bytes | Recomputed SHA-256 |
| --- | --- | ---: | --- |
| Windows TRX | 11310923911 | 639739 | `6c88a57591873839b05ccb236160f6b391d3ce9a3602057980427175cd81428d` |
| macOS TRX | 11310873820 | 639934 | `d7c90a56e2e23a9f178745ca2a086bd5e6d24a074d5a28bf6739a4ff9828d693` |
| Linux TRX | 11310803925 | 641531 | `9a081f84cef8cf78dd72a21d5f71a8ce63feaaeb8b7a85ac492672c4725ca69e` |
| Foundation association | 11311232634 | 638 | `097a0bfc24fcaed9b3f116b3827afc585b8d5221d6c387b59345af75ae906fb4` |
| Synthetic delegate | 11311167938 | 462 | `1971e20e8870acbf8f93622e491b94cdb864f65d64f902f596c144f91078482e` |
| Gitleaks SARIF | 11310564119 | 6764 | `367f9493a966c0833cec25e7c8d4319c8df48431030fd926c3a697d383c64d7e` |

The macOS job actually executes one new independent Foundation process,
separate from the 23 local executions. Its exact raw stdout is 710 bytes,
one LF, no NUL; stderr is empty. SHA-256
`8365c877125681768dc2515c0430de1c211a87e1b419e53e72924ea9aba6818c`
matches the strict committed CI gate and local final output. It reports 86
successful NSObject superclass deallocations, 249 callbacks, balanced source
and tag retains/releases, a maximum-16 tag budget and zero final live tags.
The actual step limit is two minutes, not an independent OS process watchdog.

`early_publication_proved=false`; `SCStream_creations=0`,
`permissions_requested=0`, `pixel_reads=0`, `capture_executed=false`.
This proves only the healthy Foundation lifetime scenario in
[Phase 2a](2026-10-05-macos-native-association.md), not early publication,
native exception safety, native drain or actual Capture. Capture remains
`delegate=0`. The separate synthetic process has 277-byte stdout, empty stderr,
61 callbacks and four process-retained bridges; it is the older MDO boundary.

## Metadata/log-only and security limits

The Linux ABI ZIP and all three unsigned-package ZIPs were not downloaded.
Exact API/upload IDs, sizes and digests match, but their local ZIP/member hashes
and internal verification were not performed for this SHA. Each hosted package
job passes TEST MODE, two seals/verifies, same-stage container diff and the
26-project including-transitive vulnerability query. This does not prove
independent build reproduction, signed installation or release acceptance.
Linux's ABI log reports no daemon, portal, capture stream or hardware operation.
Its repeated tee/grep line is one execution, not two. Windows WARP is buffer
readback, not WGC capture; all three test jobs pass TEST MODE and protocol-1.7
simulation.

CodeQL 2.27.1 analysis `1889415605` binds to job `111498587633` and this
SHA/ref. The exact-tree and reported C# counts are 435/435, not a per-file TRAP
audit. Reconstructed API SARIF contains 52 unique descriptors: 20 problem,
24 pathproblem and eight metrics, zero results. There are three raw diagnostics,
zero summary diagnostics and one Ubuntu-runner migration notice. It is not
the original uploaded SARIF or universal/native security proof.

Gitleaks 8.24.3 scans only the exact latest commit patch (`--log-opts=-1`),
approximately 72642 bytes, with 208 descriptors and zero results. It is not a
full-history, working-tree, dependency or binary scan. The security source chain
was independently cross-checked without changing GitHub state.

## Reproduction

Raw API/workflow/job/logs, six ZIPs, extracted native bytes, complete inventory,
collector and audit are retained in `/tmp/flowspan-msc-hosted-974e954/`.
Root independently replays the preserved audit with exit 0 and no acceptance
violations:

```sh
python3 /tmp/flowspan-msc-hosted-974e954/audit.py
```

`audit.json`: 123351 bytes, SHA-256
`35ccbe6c7168e210f26e6a9e8dec187f3c386bf04c3b4100ed38b7e90646334d`.
`case-inventory.json`: 1140647 bytes, SHA-256
`47eba6ce2331878471ceb9e7daac7732ee908f80f5ad0d7e11768e8cab45d90d`.
Audit script SHA-256
`c2e9c3626733f2b7eda53f3d4d52403ebed3b9b0381ac3367ad1ca32c4a8b646`.
The audit verifies provenance and these bounded outcomes, not full product
acceptance. MSC 2b, Capture integration, source-loss, protection/input,
independent Emergency Stop, physical-device and release gates remain open.
