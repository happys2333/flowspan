# Same-Capture cleanup — exact hosted checkpoint

Status: fresh exact-commit CI, CodeQL and root replay of downloaded evidence
pass. This closes task 3b.1 hosted verification only. NativeSource task 3b.2a,
nonzero delegate composition, production sharing and v1 remain open.

## Exact run and complete inventories

Commit `a07d911624c57c3a33ab9708de96d1d7838674dc`, implementation branch
`codex/v1-foundation`, push, attempt 1, run number 248:

- [CI](https://github.com/happys2333/flowspan/actions/runs/37239700037): all eight
  jobs successful.
- [CodeQL](https://github.com/happys2333/flowspan/actions/runs/37239700068): successful.

Windows, macOS and Linux each contain 12 raw TRX / 2809 total, executed and
Passed cases, with all non-success counters zero. Unique result/definition/
execution/entry identities agree. Complete qualified inventories match across
all three OSes and the saved final local Debug/Release TRX. Canonical inventory
SHA-256: `d354c07f1fe1a99d78561ecc7a4a775242651e81b7f7a39a239425b0557fc8cb`.
All 32 Capture, 29 coordinator, 33 router, 10 pairing and four
ProtectionMutation rows pass. The 15 new Capture rows are additions to the
historical 2794 inventory with no removals, not inherited test outcomes.

The 553 saved root build inputs match the exact commit and captured manifests.
Collection uses exact Git objects, not the later NativeSource working tree.
The implementation and local/native proof are recorded separately in
[cleanup evidence](2026-10-05-macos-capture-cleanup.md).

## Artifact, helper and security scope

Eleven fresh small ZIPs are downloaded and checked against API/upload size and
SHA-256 facts, with bounded safe-member validation. All 14 artifact receipts
are bound to this exact run. The three large unsigned package ZIPs were not
downloaded or internally inspected; package success is hosted/API/log evidence,
not signed installation or release acceptance.

The hosted macOS synthetic delegate, Foundation association and early modes
run the no-capture prerequisites. Raw early output remains exact LF-only 1057
bytes with empty stderr and zero native/watchdog status. Every OS passes 152
raw-byte fixtures; macOS/Linux additionally pass four POSIX CLI fixtures and
12 watchdog contracts. Windows POSIX skips remain explicit. Intentional
nonzero child outcomes and Darwin zombie-only SIGKILL EPERM stay failures;
leader join is not universal descendant or native cleanup proof. Default
capture-probe Skip is not actual Capture success. Linux thread-loop ABI needs
no daemon, portal or capture. No hosted actual SCStream capture is claimed.

CodeQL logs report 441/441 C# files scanned; exact Git has 441 tracked C# files.
API-reconstructed SARIF has 52 rule descriptors and zero selected-rule results.
It is not the original uploaded SARIF; three logged raw diagnostics have
unavailable contents. Counts are not per-file database identity, safe interop
or complete security clearance. Gitleaks 8.24.3 scans only the one exact commit
patch with `--log-opts=-1` (approximately 56653 bytes), 208 descriptors and zero
results, not full history, the worktree or dependencies.

## Actual root replay

Preserved evidence: `/tmp/flowspan-capture-cleanup-hosted-a07d911/`.
Root actually executed both saved-data audits with exit 0 and no violations:

```sh
python3 /tmp/flowspan-capture-cleanup-hosted-a07d911/audit.py
python3 /tmp/flowspan-capture-cleanup-hosted-a07d911/codeql/audit.py
```

Main script SHA-256:
`52b4ae5a189edc04775e265a07678b2368135bb89ec8acf0603e8eb35ad3090e`.
Main 1464383-byte report SHA-256:
`58d2041d056d162be22d03c7a18754bdb627fc58722b6b1f5709c180c0781ac7`.
CodeQL script/report SHA-256:
`04fa369ccc5195c54842912ee609e57b10f83fc582706ef398c70f1365f181f7` /
`50ef61c4b6141db5f675f1e53a57df362abfa435da96ea0ba474752d2921f617`.
Single-reviewer saved-evidence inspection has zero material findings and
independently checks all 1195 downloaded ZIP members, raw inventories and
exact Git bindings. Report:
`/tmp/flowspan-capture-cleanup-hosted-a07d911/independent-review.md`, SHA-256
`1ff8acdd1eaf19d2813f206d9c751006e95f7ed4d4841e7ea18b0618908f9c56`.

No dispatch, retry, issue, PR, comment, review or discussion was published.
Old failed/timeout/compiler/Skip records remain unchanged. This all-green
hosted checkpoint creates no new RED-to-GREEN, real native fault, nonzero
delegate, physical-device, minimum-OS/architecture, protection/input,
independent Emergency Stop, accessibility, legal/signing or release proof.
Task 6, v1 acceptance and the long-term Goal remain open.
