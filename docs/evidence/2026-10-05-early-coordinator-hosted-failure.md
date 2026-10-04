# Exact 9deed36 hosted coordinator checkpoint — failed CI

Status: failed complete hosted checkpoint. The new coordinator's portable tests
pass on each OS, but that cannot substitute for the complete CI gate. No cause
or repair is inferred from this evidence record. The earlier successful
`974e954` checkpoint is not inherited by this commit.

Commit `9deed368f2fe9697e5dd9e9d02ef0409701f2a3b`, push to
`refs/heads/codex/v1-foundation`, run number 245, attempt 1:

- [CI 37228426977](https://github.com/happys2333/flowspan/actions/runs/37228426977)
  completed failure; Windows tests failed and the package matrix was skipped
  before expansion (one placeholder, not three executed package jobs).
- [CodeQL 37228426955](https://github.com/happys2333/flowspan/actions/runs/37228426955)
  completed success.

Committed workflow/source bytes and every executed job's checkout SHA match
the exact run. No rerun, dispatch or GitHub communication was performed.

## Complete downloaded test evidence

| Hosted OS | TRX | executed | Passed | Failed | MacOS project | Desktop |
| --- | ---: | ---: | ---: | ---: | --- | --- |
| macOS | 12 | 2775 | 2775 | 0 | 206/206 | 760/760 |
| Linux | 12 | 2775 | 2775 | 0 | 206/206 | 760/760 |
| Windows | 12 | 2775 | 2771 | 4 | 205/206 | 757/760 |

All other counters are zero; the two TimeoutException cases are Failed results,
not TRX timed-out/aborted runs. All 29 coordinator cases pass on every OS. Router
results are 33/33 on macOS/Linux, 32 Passed plus one Failed on Windows.
The complete qualified-name inventories match each other and final local
run-02: SHA-256
`1d65da43989b932729c943080a549342b8ea9d7d981d092511bcfdb6eae3b88d`.

The exact Windows failures, with complete raw messages/stacks retained, are:

1. `MacOSRemoteWindowStreamDelegateRouterTests.RetirementClosesAdmissionBeforeJoiningAdmittedHandler`:
   Assert.False expected false, actual true, test line 140.
2. `DesktopRemoteWindowManagedTwoNodeTracerTests.ProtectionMutationAfterReservedRoutePreventsPrepareWireAndDrains(replacementKind: SecureInput)`:
   expected NotDelivered, actual null, test line 3558.
3. `DesktopPairingDecisionSourceTests.CancellationCoalescingFixtureDrainsPublicationAfterInjectedFailure(failurePoint: "before-publication")`:
   TimeoutException at fixture line 209, cleanup line 235, test line 151.
4. `DesktopPairingDecisionSourceTests.DisposeWaitsForActiveCancellationPublication`:
   TimeoutException at test line 25.

Windows restore/format/build passed; post-test composition, simulator and probes
were skipped, so no corresponding Windows evidence is claimed for this SHA.
macOS/Linux post-test TEST MODE, protocol-1.7 simulation and probe checks pass.

## Artifacts and native/security scope

Six small ZIPs are downloaded; API/upload ID, size and digest agree with local
recomputed ZIP and safe-member hashes:

| Artifact ID | ZIP bytes | SHA-256 |
| --- | ---: | --- |
| macOS TRX 11313386391 | 648383 | `b3edbd77c1fc7777dbb9ced841475675459c0734949d0caa39a5188d9495da3e` |
| Windows TRX 11313371560 | 649420 | `913a5d76afec2cca96bc24416ddc986e2fedd064ceb8b485a89ce0402bdcf29d` |
| Linux TRX 11312746994 | 649018 | `e27cabc69792635b061a3a29de3a2a9f444b4fceb3c34fe3c16aef5bb3dd2a1b` |
| Foundation 11313361487 | 638 | `b2de50165b01655b0964aaf0693e9f52901642ca98b9a3e85ddf468536e73a52` |
| Synthetic 11313127308 | 462 | `810da98aab214a6ca383285d7a3f5cd77ab46653c85630dbe0e7a874e52c6d1d` |
| Gitleaks 11312686679 | 6764 | `4cdafd8f5de9bf99ff6cb290553952f89294bbd1e0bd04ef79729ec54a69c316` |

The Linux ABI artifact is API/upload/log-only, not locally downloaded. There
are no package artifacts or unsigned-package/release results for this SHA.

One new hosted Phase 2a Foundation process has 710-byte one-LF/no-NUL stdout,
empty stderr, SHA-256
`8365c877125681768dc2515c0430de1c211a87e1b419e53e72924ea9aba6818c`:
86 superclass deallocations, 249 callbacks, early_publication_proved=false.
One new synthetic process has 277-byte stdout, empty stderr, SHA-256
`ca64ed8dd8591c786457c2f84ece3885b4245a0ef155d10f785fb06a7e19fdf5`:
61 callbacks and four process-retained bridges. Both remain no-capture
compatibility evidence, not coordinator/native early-publication or Capture
proof. Their two-minute step deadline is not an OS process watchdog.

CodeQL 2.27.1 analysis `1889580312` has committed/log C# counts 437/437,
52 descriptors and zero results, **three raw diagnostics**, and one runner
migration notice. The SARIF is API-reconstructed, not the original upload;
count equality is not a per-file TRAP audit or universal security proof.
Gitleaks 8.24.3 actually scans two first-parent, non-merge patches:
`9a025894^..9deed368`, approximately 86220 bytes, 208 descriptors, zero results.
It is not the prior run's last-one-commit scan or a full-history/working-tree
secret audit. No native fault, physical-device or release acceptance follows.

## Reproduction and open gates

Raw APIs, exact workflows, source-hash audit, logs, ZIPs, native bytes and
inventory are in `/tmp/flowspan-msc-hosted-9deed36/`. Exact sources are read
from the immutable Git commit, not copied into that directory. Independent
artifact/source/security reviews have no integrity findings; root replayed
with exit 0:

```sh
python3 /tmp/flowspan-msc-hosted-9deed36/audit.py
```

This exit verifies preserved provenance/structure only. `audit.json` explicitly
says CI_FAILED and retains six acceptance violations; it is not a passing gate.
Audit script SHA-256:
`bd000a86ead4f45628ba2dc45be444b7179ed881293afab0cb5f89ede70b5e2e`;
audit JSON (129662 bytes):
`b3ec6aa84b2cc9ebf7da29fe74ad3ce90e2a361f4cd0d8429a2f0fc5e80a938d`;
serialized inventory JSON:
`ca18bcfb4c5f1c21846b848e9417af2efcc02133cb055f016c27c1fb43634487`.

The complete hosted checkpoint stays open until a new exact implementation
commit independently passes. MSC 2b native, actual Capture (`delegate=0`),
parent Task 6, source-loss, protection/input, independent Emergency Stop,
physical-device, signed distribution, v1 and the active Goal remain open.
