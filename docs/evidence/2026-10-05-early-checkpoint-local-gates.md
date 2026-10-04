# Early-association checkpoint: complete local and strict-gate evidence

Status: local managed/full-solution, compatibility and separate actual
Foundation early gates pass. Fresh exact-commit hosted verification remains
pending. The original `9deed36` CI failure is preserved in the
[failed checkpoint](2026-10-05-early-coordinator-hosted-failure.md); this record
does not rewrite it or close actual Capture, MSC2b/MSC9, Task 6, v1 or the Goal.

## Complete solution and compatibility

Saved base is `9deed368f2fe9697e5dd9e9d02ef0409701f2a3b` plus seven frozen
tool/test inputs. On actual macOS 27.0.1/build 26A434 ordinary arm64, SDK
10.0.301/runtime 10.0.9, all 24 commands in
`/tmp/flowspan-msc-early-root-20261005/run-01/` exit zero with empty stderr.
Locked restore, solution/tool formats, six zero-warning/error builds, TEST MODE,
simulator, 26-project including-transitive advisory query, standalone tools,
Windows portable self-tests, diff and unchanged-input checks pass. No vulnerable
packages were reported by that point-in-time query; it is not universal security
proof or a local secret-scan result.

Debug/Release each contain 12 TRX / 2777 total/executed/Passed, Completed
summaries and zero other counters. All result/definition/Execution/TestEntry
identities match; MacOS has 206 and Desktop 762 cases. Both qualified inventories
match, canonical SHA-256
`ef0952258305c78086408f1dd0539a644785c7f87dc81e02bc3661f0b63a69c8`.
Pairing10/router33/coordinator29 inventories are unchanged. Relative to 2775,
two natural ProtectionMutation rows gain False names and two forced True rows
are added; there is no other inventory change.

The 543 source/build inputs match before/after and reconstruct from exact base
Git objects plus seven preserved source snapshots. Production coordinator/router
are unchanged. Each configuration preserves 31 runtime files (22 DLLs, seven
PDBs and two JSONs); OS/runtime/native assets are not a complete environment or
compiler attestation. Old no-native/synthetic/Phase2a contracts are unchanged.
This run intentionally excludes the new early mode and workflow/helper sources.

Root replayed the independent read-only auditor successfully:

```sh
python3 /tmp/flowspan-msc-early-root-audit-20261005.py --verify-working-tree
```

- Auditor SHA-256: `6b26a87f9bcd51213f82bd3786b622b71e92b513b0619d4fcc03551aca191473`
- JSON 68784B SHA-256: `1b84ccb0ed1e7fc54b4e47afff6ef9c8a508e4f568c301888ae240390851fdef`
- Executed runner SHA-256: `c170ac5cb7158bb8c046fe318546f8dbf99046952b54012db9d57d6d2595e3d5`

## Independent CI-helper and actual-native root replay

Root separately ran `/tmp/flowspan-root-early-ci-replay-20261005.sh`, preserving
all command/stdout/stderr/exit records in
`/tmp/flowspan-root-early-ci-replay-20261005/`. All nine commands exit zero:
152 raw-byte fixtures plus four POSIX CLI cases; 12 watchdog contracts; one
actual final Release early native gate (30s deadline/2s grace); both independent
evidence auditors; Bash syntax; diff; unchanged source and runtime manifests.
The helper tests are infrastructure evidence, not native execution themselves.

Actual native stdout is 1057B, stderr 0B, native/watchdog exits both zero.
The report says leader_joined=true, reason=exited, no signals/failure. Stdout
SHA-256 `c5343ee33cbf70c92c039c316b37e6642dd66bcbe067c737519d8bdd48e8d38c`
matches final native Debug/Release. This is an additional root early process;
the separate native audit's 24 processes remain its own count.

Frozen helper sources:

| Input | SHA-256 |
| --- | --- |
| gate shell | `7584e321c3ee0a1b967aa1de1f91638ce2928295d44d6f49c8de0f683240a545` |
| raw/CLI fixtures | `2ad9b1052511a54b74a361e349246e40ab3241a565005ad2ff7d0921c41a1037` |
| POSIX watchdog | `1af63eaec429fc1c14f75d2f18f6c8d0f334ef3c7abaaf76df50c16ae0982443` |
| watchdog contracts | `66d6a5fa64d8ab7613646dcb4a55ebb73090a500cfa57333bfb0474235e86ca9` |
| CI workflow | `50bccdcce3debfac460c69d61800b37a61778d6d535f35f6ae3a30f0655c09d6` |

Root replay runner SHA-256
`0bc0b63a2dd6a6c2942ce48e45189402d879437c2c381a44d77796d81eb38fe6`;
input manifest SHA-256
`bd73957083c98a6a09a4f0dccd7c1e43fa3d8eb52769b80ffac197e930e53409`;
runtime manifest SHA-256
`847f907845202c09841baa45a8a1ee65d0adc9f294d60b228d4cb69d36928928`.

Python3 stdlib is CI process supervision, not a product-language split. The
watchdog owns an unreaped POSIX leader through timeout TERM/grace/KILL, retaining
raw logs and final join outcome. Darwin zombie-only group KILL EPERM is recorded
as nonzero127, not fictitious cleanup success. Deliberately detached/background
descendants after normal leader exit and native resource drain are outside its
contract. The rejected Bash watchdog prototype and its real hangs remain in
`/tmp/flowspan-msc-early-gate-20261005/bash-prototype-rejected/` and are not proof
for the shipped helper. No SCStream/AppKit/permission/pixel/input API enters the
Foundation mode. Windows/Linux helper execution and exact-SHA hosted outcomes
must be verified independently after push. Root's cached actionlint 1.7.12 also
exits zero with no output against the final workflows; it is static validation,
not hosted execution.
