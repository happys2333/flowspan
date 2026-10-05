# Same-Capture constructor pool checkpoint — 2026-10-05

Status: one finite MCC task4 behavior is accepted locally. The remaining pool,
observer/resource-use, race, native and hosted obligations remain open; task4
is still in progress. HEAD remains
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree overlay.
Evidence: `/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

## Actual RED and minimal GREEN

`ConstructorBodyFatalThenPoolPopAfterEffectRetainsExactShellBeyondSlotReplacement`
executes the actual factory. Configuration effect precedes a wrapper containing
the original fatal. Its known pool is then consumed on the creating thread and
throws a separate ordinary failure. Before any destructive Take, a NoInlining
helper reads the thread-local failed-factory slot as a nullable weak observation.
A second actual factory, using an independent output-release-after-effect fault,
replaces that slot. Repeated first-shell Dispose (when observable), fixture raw
teardown and outer GC all precede the final assertion; no test strong reference
or production-state reset establishes retention.

Compiled Debug `constructor-red01` fails at the final tuple: expected count2
and all9 facts true, actual count1 and all9 false. The first Capture, operations,
source-state and two callback markers are lost; outward and repeated-Dispose
original-fatal identity and replacement/unchanged-cleanup proof also fail. This
is an actual behavioral RED, not a compiler/setup failure.

GREEN adds only four fixed constructor attempted/confirmed pool facts. Body
failure is recorded without first releasing the shell. The known pool receives
one contained pop on its creating synchronous thread; original primary/fatal
selection and exact failed-shell handoff precede independent cleanup. An
unconfirmed constructor pool prevents only shell-root/count return, not the
single cleanup attempts for known output/configuration/queue/source owners.
No other pool, observer join, race or outer-drain behavior is repaired here.

The identical complete test file passes Debug `constructor-green01` and Release
`constructor-green-release01`. Focused D/R each pass121; project D/R each pass390,
with exact qualified sets equal to the accepted389 baseline plus this1 Fact.
No identities are removed. Restore/build/test exit0 with0 warnings/errors;
503 selected inputs and138 runtime files per configuration are bound. A separate
565-input snapshot passes locked serial solution restore and verify-only format.
These are portable controlled effects, not native pool-fault containment or a
full-solution/new hosted gate.

## Frozen bindings and verification

- Unchanged test SHA256: `9b9467bd0a388c7b3293392f337c7c99175b33fd4bc2fb07f895c3d1fc4455c8`.
- GREEN API.

  SHA-256: `1b6d27f0d5c554d9fbb4ce9fb62a458a786879715c47d247056badd465caaf85`.

- Source503 manifest: `0ce370f19b58f6f3d22f797852a8c01926d70952329bd2b7f595b12bf78a2eee`.
- Runtime D/R manifests: `19b956b1536a8e259e2082b4c45574c6414616eae9cd2d3be153a0c035061384` / `cd1154c8228782b2ce92d9dae1f6a88974fe3af39e732aac51639e7086ae69ba`.
- Auditor: `11483da596482862f6a240e53c635caefd44edd040b752f0e9718878042fa1f7`.
- Replay: `c572335fa77db7731c77634f74e032e39d1d51fb6eb11d780b576a2856c81abc`.
- Saved report: `bd11a4413856304c8c06a79990e6a2bdf8756e8b8d26a170e23ae2908bca24c8`.
- Selected-current report: `dbf45af88666e7f15a1111ae0e01f646940b3e10d9f1948d57e83fe79bd86231`.

Independent Standards and Spec reviews each have0 concrete findings. Root
actually runs both frozen replay modes and compares each report with its worker
counterpart: all exits0, no violations and byte-identical reports. Complete raw
receipts, commands, source/runtime inventories, TRX/DLL bindings and identity
baseline are checked; replay itself executes no build, test or native operation.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/constructor-replay.sh constructor-next-saved01 saved
```

Selected-current equality excludes GitHub scripts/documentation and becomes
historical after subsequent edits. Unknown/invalid pool acquisition, other
constructor outcomes and Start/Stop/output-removal scopes remain separate work.
The replaceable slot is not durable rooting or global Capture admission. No
MSC parent, production sharing, release, v1 or active Goal is closed.
