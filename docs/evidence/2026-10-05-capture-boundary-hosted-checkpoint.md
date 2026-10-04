# Same-Capture boundary — exact hosted checkpoint

Status: exact-commit hosted CI, CodeQL and independent saved-artifact audit pass.
This closes MSC task 3a only; it does not enable production sharing or verify
later cleanup changes, nonzero delegate composition, native fault paths or v1.

## Exact execution

Commit `473c625e566a7f603ec54e6ee0e1baaa8b070c76`, implementation branch
`codex/v1-foundation`, push event, attempt 1, run number 247:

- [CI](https://github.com/happys2333/flowspan/actions/runs/37235781339): success,
  all eight jobs successful.
- [CodeQL](https://github.com/happys2333/flowspan/actions/runs/37235781325): success.

No dispatch, retry, issue, PR, comment, review or discussion was published.
Collector source reads use exact-commit Git objects, not later task 3b changes.
The independent local checkpoint is
[same-Capture system boundary](2026-10-05-macos-capture-system-boundary.md).

## Complete portable regression

Windows, macOS and Linux each have 12 raw TRX files with 2794 total, executed
and Passed cases; all non-success counters are zero. Result, definition,
execution and entry identities agree and are unique. Complete qualified
inventories match across the three hosted OSes and saved final local
Debug/Release raw TRX. Canonical sorted `assembly:testName` plus LF SHA-256:
`ed4b8030d226714fb950a6ff566e967e96ac8164079e084f1b28237ab4be00cd`.

All 17 same-Capture cases, 29 coordinator cases, 33 router cases, 10 pairing
cases and four ProtectionMutation rows pass with their exact identities.
The 17 Capture cases are the only additions to the historical baseline, with
no removed cases. No older execution result is imported as a current-SHA pass.
All 553 saved root source/build/helper inputs match the exact commit and saved
before/after manifests. The collector retains those inputs and two exact
committed evidence documents.

## Native helpers and strict gates

The synthetic delegate, Foundation association and early modes each execute
once in hosted macOS. They report no SCStream, Capture, AppKit, permissions or
pixel reads. Early stdout is exact LF-only 1057 bytes, SHA-256
`c5343ee33cbf70c92c039c316b37e6642dd66bcbe067c737519d8bdd48e8d38c`;
stderr is empty, native/watchdog statuses are zero and the strict step succeeds.
These are bounded no-capture helper proofs, not actual Capture/delegate drain.

Every OS passes 152 raw-byte fixtures (one accepted, 151 rejected).
macOS/Linux also pass four POSIX CLI fixtures and 12 watchdog contracts;
Windows POSIX skips remain explicit. `gate.command.exit.raw` is a pre-validation
watchdog status, not independent final CLI-exit proof. Rejected CLI outcomes
rely on saved successful harness assertions. Leader join does not prove all
arbitrary descendants vanished. Darwin zombie-only SIGKILL EPERM remains
truthfully fail-closed 127, including a timeout child that exits zero.

Linux thread-loop ABI succeeds without a daemon, portal or capture.
Standalone capture probes execute no-capture defaults in CI; Skip is not actual
capture success. The selected task-owned local macOS Capture runs are separate
local evidence, not hosted executions or nonzero-delegate composition.

## Security and packages

CodeQL logs report 441/441 C# files scanned and three raw diagnostic messages.
The exact Git tree also contains 441 tracked C# files. The selected exact-run
API-reconstructed SARIF has 52 unique rule descriptors and zero results. It is
not an original Actions SARIF ZIP and does not retain raw diagnostic-message
contents. These counts do not prove complete semantic coverage or safe interop.

Gitleaks 8.24.3 uses `--log-opts=-1`: one exact checkout commit patch,
approximately 74577 bytes, 208 unique descriptors and zero results. Full-history
checkout is not a full-history scan; worktree and dependencies are outside it.

All three unsigned package jobs seal/verify twice, compare outputs, validate
explicit TEST MODE and query 26 solution projects including transitive
dependencies without reported known vulnerabilities. The three large package
ZIPs were not downloaded, locally hashed, expanded or internally verified.
Their success is hosted/API/log evidence, not signed installation or release.

## Preserved evidence and actual root replay

`/tmp/flowspan-capture-seam-hosted-473c625/` retains API metadata, eight CI logs,
24 local raw TRX, 555 exact source/document files, 11 small artifact ZIPs and
exact CodeQL API/log bytes. All 14 artifact API/upload size/digest facts bind
this run; the 11 downloaded ZIPs additionally match local SHA-256/size and pass
safe bounded member validation. Package archives have only API/upload binding.

Root actually replayed the following saved-data audit with exit 0,
`HOSTED_CHECKPOINT_GATES_PASSED_NOT_V1` and no acceptance violations:

```sh
python3 /tmp/flowspan-capture-seam-hosted-473c625/audit.py
```

Frozen audit script SHA-256:
`cf5941447b564d36485b16b0b553210313ab37ee67b437fb5d7f1dc3bcccbecd`.
Frozen 1449159-byte report SHA-256:
`14eb20d768c3c04dd83b693d93b0635583fed314d56c682c932d79a378a45c3e`.
CodeQL sub-audit script/report SHA-256:
`955aa52052a1e31d8c8318006fb601d28d51230b34cd81c58357e548afd0dfb9` /
`79c21ebe8abc37c0337ebae4d1329a358e71cd0ee3773bce9538098fc12a635a`.
Independent saved-evidence review has zero material findings; its 1195 ZIP
members pass separate CRC/safety checks. Review file SHA-256:
`06025476cf32ccb0eb3e491fe5358fce5abb29ece5a30c16c732cef48e4ae9d0`.

Original local failed diagnostic/timeout/skip evidence remains preserved.
This audit establishes no new RED→GREEN or single-worker/native-fault result.
MSC task 3b, MSC6/MSC9 release debts, Task 6, production sharing, physical
devices, minimum OS/architectures, protected/secure input, independent Emergency
Stop, accessibility, signing, release acceptance, v1 and the Goal remain open.
