# macOS Foundation early-association proof — local evidence

Status: final no-capture tool locally executed in Debug/Release and independently
audited. [Root CI-helper replay and broader combined solution gates](2026-10-05-early-checkpoint-local-gates.md)
also pass in separate records. Exact `2c6f8fd` has separately audited
[hosted evidence](2026-10-05-early-association-hosted-checkpoint.md), not inferred
from these local executions. Actual
Capture still passes `delegate=0`; MSC task 2b, MSC9 composition, parent Task 6,
v1 and the active Goal remain open.

Scope: [MSC requirements](../../specs/v1/native-remote-window/macos-stream-delegate/requirements.md),
[ADR 0030](../adr/0030-generation-routed-macos-stream-delegate.md).
`--run-early-associations` is separate from the
[Phase 2a mode](2026-10-05-macos-native-association.md), whose output still has
`early_publication_proved=false`. It is excluded from product composition.

## Actual native behaviors and boundaries

The mode uses actual Foundation NSObject sources, one permanent process-rooted
stateless bridge/router/coordinator, validated immutable numeric tags, retained
borrowed callback sources, and ordinary-arm64 NSObject superclass deallocation.
The actual nil-associated terminal callback retains one same-source pending
fact; after the caller releases its source +1, the retained source remains valid
until exact publication replays the terminal once. Repeated same-source terminal
facts coalesce; Active does not occupy or replace pending storage.

The publication scenario pauses only after the actual second native nil read,
then performs exact association publication/pending clear and admits a
replacement initializer. Resuming the old callback makes a third actual tag
read and notifies only the original generation. The ambiguity scenario instead
has a third actual nil read: two related exact initializer tokens remain
quarantined and construction is poisoned, with no guessed replacement or
ambiguity notification. This is source-identity ambiguity, not an observed
global native ownership fault; `native_admission_closed=false` remains explicit.

These are controlled native-boundary interleavings, not SCStream-produced races
or OS-scheduler stress evidence. Managed-only nested-fatal selection assertions
are not native/Objective-C/ABI fault injection. No SCStream creation, AppKit,
permission, pixel, capture, input or physical-device API is exercised. Configured
tag budget 16 is not a new exhaustion proof: observed maximum live tags is two.
Managed retirement, balanced references and observed deallocation are not native
delegate drain or complete Capture cleanup.

## Frozen source and execution provenance

Actual host: macOS 27.0.1/build 26A434, ordinary arm64; SDK 10.0.301/runtime
10.0.9. No minimum-OS, Intel or arm64e result is inferred. Saved execution base
is `9deed368f2fe9697e5dd9e9d02ef0409701f2a3b` plus these four tool inputs:

| Tool input | SHA-256 |
| --- | --- |
| `NativeEarlyAssociationApi.cs` | `20e0a3e0ecc17f0fa1bdc465cea5c986b2e4be22832e1b0fbb3edb853be3581b` |
| `NativeEarlyAssociationProbe.cs` | `2205d456d28cbd8cc95ea8f1fc8f0c4f7e94cf0f9e7542e4e03ab42e0f9c0456` |
| `Program.cs` | `cc92a2c45dd0a0e5b7a5e4df76529067927a5f62de8d0534cf46819d0287776b` |
| `README.md` | `b9a635936250fc55dd02d83d12d2e5518e213993b6b67fe098d1914a5d9571ab` |

Production coordinator/router stay unchanged at SHA-256
`5ad673ccc15ac0ea74a15cfb98f651c5de18ec0e737b0a9f727e32341230e2f3` /
`ed4975c2440124218ba9e6c3117669912e8061842c346e169a2cc1a6c02ddde4`.
Only `13-final-debug`, `14-final-release`, `15-final-verification` count as
final-source proof. Each configuration preserves 328 relevant source/build
inputs and a 17-file isolated runtime. Source and runtime manifests match
before/after execution and later repeats; locked builds have zero warnings and
errors. Unrelated concurrently edited repository files are not covered by this
input manifest. This is bounded sequential build/run provenance, not compiler
attestation or a preserved independently executable OS/SDK environment.

## Final process records

Twenty-four independent final-source early native processes exit 0 with identical
1057-byte stdout (one trailing LF, no CR/NUL) and zero-byte stderr. Final stdout
SHA-256 is `c5343ee33cbf70c92c039c316b37e6642dd66bcbe067c737519d8bdd48e8d38c`.
The saved commands explicitly select isolated DLLs and use GNU timeout with
45-second TERM and five-second KILL-after bounds.

Each process records:

- ten typed native callbacks; four allocated/owner-released/dealloc-entered/
  superclass-deallocated tags; six source owners acquired/released;
- source reference retains/releases 11/11, tag reference retains/releases 7/7;
  association/protocol/nil-protocol reads 26/18/11;
- one pending replay and one original-generation publication-race notification;
  zero replacement/ambiguity notifications; three ambiguity-scoped nil reads;
- two related quarantined initializers, two forced-GC rounds; final tags,
  charged/uncertain reference ownership and contained failures zero;
- same-source pending, publication-race and ambiguity-poison proof fields true.

The quarantined initializer permits remain process-owned; zero reference records
does not mean all Capture obligations are cleaned. `15-final-verification`
contains 32 supervised process invocations plus one formatting check: 22 early,
two synthetic, two Phase 2a, six default/help/unknown. Combined with the two main
`13`/`14` executions this gives the 24 early records above. Unknown arguments
correctly exit 2 with expected stderr, before native work. Default/help retain
90-byte no-native skips; synthetic retains 277 bytes and Phase 2a 710 bytes.
The legacy outputs are byte-identical to their saved contracts.

## RED history, independent audit and pending gates

Only `02-red-runtime` → `03-green-tracer` is genuine native behavioral
RED→GREEN: the saved unchanged terminal-replay assertion fails with exit 1,
then passes with exit 0 after connecting the existing coordinator instead of
the deliberately retained Phase-2a nil-drop baseline. This is not a production
coordinator bug fix. The tracer GREEN still reports publication-race proof false.
`05-race-runtime` and `06-poison-runtime` pass directly after scenario expansion;
they are not additional RED cycles.

`01`, `04`, `11`, `12` are compiler-only exclusions without native run records.
`07`, `08`, `09` and `10-review-final-debug` are preserved historical successes
but superseded. Identical stdout cannot rebind them to the final source.

Detailed evidence: `/tmp/flowspan-msc-early-native-20261005/handoff-final.md`,
with source snapshots, runtime manifests, raw triplets and recipes. Independent
read-only audit:

```sh
python3 /tmp/flowspan-msc-early-native-evidence-audit-20261005.py
```

Independent repeated audits and the root replay return exit 0, no findings and
identical serialized JSON.
Script SHA-256:
`85252f54f4d91391268d997fc5787ed6a2f5069f92e526926f9c16c4d6f40c70`;
JSON `/tmp/flowspan-msc-early-native-evidence-audit-20261005.json` SHA-256:
`f04372e41f96fbfd821b0ec21e262af41908302a0b8df05844b881a569d1960d`.
Audit reads preserved bytes and immutable Git objects, not native/build/test
execution or retrospective dirty-source attribution. Final static Standards/Spec
reviews have no remaining findings.

The [9deed36 hosted attempt](2026-10-05-early-coordinator-hosted-failure.md)
remains failed and only ran older Phase 2a. New hosted early-mode evidence,
actual Capture composition, source-loss,
protection/input, independent Emergency Stop, native drain, physical devices,
minimum OS, signed distribution and release acceptance are not established by
this record.
