# Same-Capture late root-free checkpoint — 2026-10-05

Status: one additional MCC task3 behavior accepted locally. Remaining task3
cases, tasks4–8, MCC/MSC and v1 are open; Goal active.
Base HEAD: `1ab251135f3617bc18e39d5f67e38885b53e85a7` with the accepted
[Stop checkpoint](2026-10-05-capture-completion-stop-progress.md) overlay.
Immutable evidence: `/tmp/flowspan-capture-completion-root-free-20261005/`.

## Actual late fault and minimal repair

Healthy actual one-argument Start acquires one additional same-pointer heap
reference using the existing controlled runtime. This extends one physical
capture's lifetime; it adds neither another capture/helper nor a fixture seam.
Healthy Stop and Dispose release caller references and independently known
stream/output/queue/configuration/source owners, while the last Start reference
remains outstanding.

The early Dispose result and charge are saved without assertions. The test then
arms root free to consume its fixture GCHandle and throw a wrapper containing
the original fatal, releases the last reference through the actual production
dispose helper, repeats Dispose, raw-tears down the fixture and observes the
weak graph after GC. `tracer02` actually fails: expected `(1,1,T,T,T,T,T)`, actual
`(0,0,F,F,F,F,F)` for pre-fault/current charge and Capture/completion/primitive/
operations/source graph. `tracer01` is preserved as a setup-only pre-fault
boolean assertion error, not behavioral RED.

`green01` passes byte-identical complete ownership-test SHA256
`457c277da7bc1d24cbcc3e7d21a81e4f1eadd5b966b14286d71b78d05ccf3377`.
Only the API changes in the behavioral pair. After caller and independent owner
cleanup, both staged slots freshly expose failure and require confirmed native
retirement plus terminal managed drain before shell-root/count return. Cached
caller-release success cannot conceal a later helper failure; both slots are
checked even when an earlier cleanup fails.

The late fault leaves caller release confirmed but root free only attempted;
native retirement and terminal managed drain remain unconfirmed. Repeated
Dispose exposes the original fatal, with no release/free retry. Independent
owners clean up once. No wait, pool, resource-use join, race or outer `IsDrained`
repair is added. Independent releases precede lifetime checks, avoiding a
stream-held-Block retirement wait cycle. Retained count remains diagnostic, not
a composed process-wide Capture permit.

## Frozen regression and independent replay

Focused Debug/Release each pass112; macOS project Debug/Release each pass381.
Qualified sets match exactly: accepted380 plus this Fact, preserving original377
plus Start2, Stop1 and late-free1, with no removals/non-success counters. Locked
restore/build succeed with0 warnings/errors. Stages bind503 selected C# inputs,
complete runtimes, command argv/cwd/exit/output and qualified TRX
storage/codeBase/DLL bytes before/after. Tools, Python/CI and concurrent docs are
excluded; no whole-tree equality, full-solution format or native result is
claimed by this campaign.

Standards:0 concrete findings. Spec:0 concrete findings for this finite behavior.
Both pin final API SHA256
`ef8abaa78f6dad313c07a31a993120682b931f3d17f77ce533b354d2a93ebf89`
and the test SHA above. Static review is not additional execution.

Actual root saved/current replay and corresponding worker `cmp` each exit0,
with `violations=[]`:

- Selected manifest: `e2807b3a7b38d70cd48567f087adfe1de04bd1b599eac70cd72a380901816fce`.
- Auditor: `f4b78360c1f975018a2b7b51f870f2534dd56fb978191b50adba86dec950ad7b`.
- Replay: `c9580648312d54c16f4a4a6f65efba8ac504598b5352ae1508af419fd6b70a50`.
- Saved report: `fdd593c42e86c45bcbb7ef0f5e488a95d55d2606a27985e620554cf26e98731c`.
- Selected-current report: `e1b9f9ce21a99861eb1030bb0eee9b39d7b0c3b1e254199954942311763fba5f`.

Fresh-label replay (no build, test or native execution):

```sh
bash /tmp/flowspan-capture-completion-root-free-20261005/replay.sh next-saved01 saved
```

Later changes supersede selected-current equality, not immutable proof. This is
controlled-effects evidence, not actual native fault containment. Root
allocation/invalid-return/caller-release cases, four pools, resource-use
closure/join, races/reentry and outer drain debt remain separate. The independent
one-argument native Block mode and hosted gate are not this campaign's evidence.
Production stays delegate=0, Protection Unknown and sharing unavailable; global
admission, physical/release and v1 stay open.
