# Windows WARP known-pixel readback

Date: 2026-10-04. Exact implementation:
`781610a16b0d2c0293c91aa44081af9236c4588b`.
This is a standalone native ABI experiment, not Windows Graphics Capture or a
production adapter. See the [tool](../../tools/Flowspan.Windows.CaptureProbe/README.md)
and its [local compilation evidence](../../tools/Flowspan.Windows.CaptureProbe/evidence/2026-10-04-local.md).

## Actual Windows execution

[CI 37200897609](https://github.com/happys2333/flowspan/actions/runs/37200897609),
run 235, attempt 1, has exact head SHA above. Windows test job `111432190039`
completed successfully, including standalone build, formatting, ten portable
self-checks and the default native WARP probe. Its completed-job stdout is:

```text
self_test=pass cases=10 native_api_called=false
probe=pass mode=warp api=D3D11CreateDevice driver=WARP apartment=MTA source=7x5 content=3x2 bytes=24 row_pitch=12 feature_level=0xb000 map_attempts=2 owned_refs=4 released_refs=4 sha256=fc865b98e8180228df0ec6c60cd9a919aa9033ae1b802fbfefe91ede9ac2e3af window_capture_executed=false permissions_requested=0 pixel_files_written=0
```

The Windows x64 process actually created a software D3D11 device, initialized
one fixed texture, cropped its 3-by-2 content rectangle into a staging texture,
submitted the copy, mapped it after two nonblocking attempts, copied 24 bytes,
verified the independent known-answer SHA-256 and balanced its four acquired
COM references. Feature level `0xb000` is 11.0. The native RowPitch was 12,
equal to the packed row width: native padding was not exercised. Padded rows
remain a separately executed managed self-check. Four balanced application
references do not establish driver-internal resource/leak freedom.

No user window was enumerated, created or captured; no capture/input permission
was requested and no pixel file was written. This does not exercise WGC, WinRT
capture items, HWND identity, reverse-COM callbacks, hardware GPU drivers,
protected content, secure desktop, input or Emergency Stop.

## Failed enclosing CI and retained artifacts

The enclosing CI is **failure**, not a successful three-platform checkpoint.
Windows and macOS each have 12 downloaded TRX files with 2624/2624 passed tests
and every non-success counter zero. Linux job `111432190006` has 12 TRX files,
2623/2624 passed, and one Transport failure:

```text
RegistryDisposeStartsEveryOwnedRouteBeforeJoiningCleanup
Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.ObjectDisposedException)
RemoteWindowMediaAttachmentTests.cs:625
```

Linux Transport is 757/758; Desktop is 757/757 on every OS. Linux's test-step
failure skipped every subsequent probe/composition/simulator step, and all
package jobs were skipped. There is no Linux WARP self-test or Skip execution
claim for this run and no package success claim. macOS actually passed ten
Windows-probe self-tests and printed its explicit unsupported-host Skip.
The failed test has a separately verified
[test-only synchronization repair](2026-10-04-media-route-disposal-test.md);
that local repair does not erase this run's failure or claim a later hosted pass.

Downloaded artifact archive bytes were hashed and matched API digests; artifact
metadata binds them to this exact SHA and run:

| Artifact | ID | Downloaded SHA-256 |
| --- | ---: | --- |
| Windows TRX | `11302784370` | `bd89c7d148e2807e34bd5ccb6d1cd056a8510f53e6d9494abc1f9d2664557767` |
| Linux TRX | `11303330258` | `2dade1c0df0c355cd11932680045d2af2dcb8fdce372be1170bc5f9b0caae86f` |
| macOS TRX | `11302073930` | `e7a6ef4885a4c25738955fb08526347b734dbe016b45fcf7b5184b11fc12f9b1` |
| Secret SARIF | `11301878909` | `dc595b024b0415263b058f0785dca60a4a72a125e6e569d5a3a317bd07438775` |

Secret Scan succeeded with 208 rules and zero SARIF results.
[CodeQL 37200897582](https://github.com/happys2333/flowspan/actions/runs/37200897582)
succeeded. Exact-SHA analysis `1888610958` reports CodeQL 2.27.1, 52 rules,
zero results and zero exact-ref open alerts. Its log reports 391/398 C# files
scanned and three raw diagnostics; full-file coverage and zero raw diagnostics
are not claimed. The REST SARIF provenance matches this revision.

Temporary raw logs, archives, metadata and parsed TRX are retained in
`/tmp/flowspan-warp-hosted-37200897609/`. Retrieve independently:

```sh
gh run view 37200897609 --repo happys2333/flowspan --json headSha,status,conclusion,jobs
gh api repos/happys2333/flowspan/actions/jobs/111432190039/logs
gh run download 37200897609 --repo happys2333/flowspan --name test-results-Windows --name test-results-Linux --name test-results-macOS --name gitleaks-results.sarif
gh api repos/happys2333/flowspan/code-scanning/analyses/1888610958
```

## Status

The narrow Windows x64 software-D3D11 ABI/readback question now has an executed
known-pixel answer. Task 7, native WGC, input/protection/independent stop,
production host composition, physical Devices, package lifecycle and release
acceptance remain open. This successful native substep does not make the failed
enclosing CI successful.
