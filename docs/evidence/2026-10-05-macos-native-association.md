# macOS Foundation association proof — MSC Phase 2a

Status: implemented and actually verified locally in Debug/Release and in the
[exact 974e954 hosted checkpoint](2026-10-05-association-hosted-checkpoint.md).
MSC task 2 stays open: its early
initializer/publication-race obligations are not implemented or proved.

Scope: [MSC requirements](../../specs/v1/native-remote-window/macos-stream-delegate/requirements.md),
[ADR 0030](../adr/0030-generation-routed-macos-stream-delegate.md). This tool is
excluded from product composition. Actual Capture still passes `delegate=0`.

## Actual native behavior

`--run-associations` creates only task-owned Foundation NSObject sources:
one permanent +1 stateless bridge, one permanent managed router and numeric-only
NSObject tags. Default/help make no native calls; unknown arguments exit 2
before initialization. Unsupported execution is an explicit Skip, never Pass.

The actual runtime checks the tag class/superclass, dealloc encoding/IMP,
`q` ivar encoding, size/alignment and runtime-reported offset. The ordinary-arm64
super-send uses NSObject superclass with `objc_msgSendSuper`, not TagClass or
the distinct Super2 convention. No read/message of freed self occurs afterward.
Its separate maximum-16 tag quota returns only after successful superclass
deallocation. Associations are retained and never cleared at managed retirement.

Typed callbacks retain borrowed source and tag before managed routing. A
handler actually releases its caller's source +1; acquired references still
permit subsequent native association/generation reads, then nested finally
balances tag/source references and the callback pool. No native call is under
the router gate. Registration and contained reverse-entry failures are checked
by outer execution gates. Fatal preservation is implemented; native/OS fault injection
was not performed and is not claimed proved by this healthy run.

Late old-generation callbacks cannot target a replacement. Sixty-four healthy
generations and forced GC preserve exact routing and terminal-once admission.
Sixteen retained sources hold tags after simulated managed-Capture cleanup;
a seventeenth tag is refused before allocation. Actual source/tag deallocation
returns one independent tag permit for a new generation. These are simulated
Capture absence/cleanup facts, not actual Capture teardown.

## Executed evidence

Actual macOS 27.0.1/build 26A434, ordinary arm64; SDK 10.0.301/runtime 10.0.9.
Worker final Debug/Release builds have zero warnings/errors. Twenty-two
independent final association processes (11 per configuration) return exit 0,
identical 710-byte, single-LF stdout and empty stderr. Root separately builds
Release and actually executes the exact CI shell gate, adding one independent
native process. Every run reports:

- one permanent bridge/router, no probe GCHandles;
- 86 tags allocated, creator owners released, dealloc entries and successful
  NSObject superclass deallocations; maximum 16 live tags, final zero;
- 87 source owners acquired/released, 249 source callback retains/releases,
  247 tag callback retains/releases and two rejected unknown callbacks;
- 64 healthy generations, 12 forced-GC rounds, zero contained failures or
  quarantined tags; no SCStream, AppKit, permissions, pixels or capture;
- `early_publication_proved=false`.

Native stdout SHA-256:
`8365c877125681768dc2515c0430de1c211a87e1b419e53e72924ea9aba6818c`.
Old synthetic Debug/Release output remains byte-identical at 277 bytes with
SHA-256 `ca64ed8dd8591c786457c2f84ece3885b4245a0ef155d10f785fb06a7e19fdf5`.
Default/help/unknown behavior is separately executed in both configurations.

Three historical actual behavioral RED→GREEN logs cover tag lifetime, missing
callback retains and unknown-tag rejection. Intermediate RED source/DLL inputs
were not snapshotted, so final hashes are not attributed to those executions.
One compile failure and an accidental prior-DLL execution are explicitly
excluded from behavioral/final native evidence. Late-generation, healthy-loop
and quota tests passed directly and are regressions, not invented RED stages.

Raw commands, outputs, exit records, DLL/source manifests and detailed limits:
`/tmp/flowspan-msc-native-association-20261005/phase2a-handoff.md`.
Root raw native evidence: `/tmp/flowspan-checkpoint-local-20261005/native-ci/`.
The independent combined audit verifies all 22 worker results, frozen inputs,
three historical log pairs and root's actual CI gate result:

```sh
python3 /tmp/flowspan-checkpoint-audit-20261005.py
```

Its JSON SHA-256 is
`7db75e8ea017e99862286204d779e4f837dfb2c75cc3e2bbce7ceaad9adb63ff`.
Explicit `--run-native` executes a new opt-in native process; replay without
that argument only audits preserved evidence. Complete local solution results
are in [the fixture repair evidence](2026-10-05-windows-fixture-lifecycle-repair.md).

## Frozen implementation and CI gate

SHA-256: Program
`f08d0021267bf31f5349cfcfa795bd72dfea9492b6b29fea9a1713ce078e7cd5`;
NativeAssociationProbe
`379c24a960b893e7610f5765278515db575551b7329149ad57cbebae165109ff`;
NativeAssociationInterop
`115ac2943bfe0506aade0a3831e27a6775094df7680fa1f46c1d34061af5406f`;
README `534198316332eb743df23592ced77301c3f4d545b4f8cdb86937f1d35b430a33`.

The new macOS CI step has a two-minute GitHub Actions step timeout within the
20-minute test-job timeout, not an independent OS process watchdog. It requires exact
Pass fields/counters, empty stderr, bounded single-LF output with no NUL, and
uploads raw evidence even after execution failure. It rejects Skip. Seventy-four
portable shell fixtures verify valid output, every changed/missing field,
bad bytes/line endings, stderr and failed command handling; these fixtures are
gate tests, not native execution. Actionlint 1.7.7 and diff checks pass.
Fixture audit: `/tmp/flowspan-association-gate-audit-20261005.py`; output SHA-256
`1477f4d001bf308e0a35189b6e1ef724d40b3ad6c6c269f4240b1816776844fd`.
Standards and Spec static reviews report zero remaining findings.

## Open boundaries

Unknown nil tags are rejected rather than recorded as early facts. Exact
initializer ownership, publication/pending-clear/third-read races, distinct
early-stream ambiguity and poison/quarantine protocol remain required before
Capture integration. The permanent bridge/classes are bounded process retention,
not cleanup. Managed retirement does not prove native drain/no future callbacks.
Objective-C exceptions, corrupt pointers and ABI faults may terminate a process.

No actual SCStream source loss, native capture, sample/Block drain, TCC,
protection/input/independent Emergency Stop, minimum-OS, Intel/arm64e, physical
devices, signed packages, production host sharing or v1 release criterion is
closed by this Foundation proof. The previous f989 hosted failure is preserved;
974e954's own successful CI/CodeQL and downloaded raw proof are recorded in
the hosted checkpoint above, without inheriting a prior-SHA outcome.
