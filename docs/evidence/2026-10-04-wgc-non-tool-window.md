# Hosted task-owned WGC: non-tool-window experiment

Date: 2026-10-04. This records an actual Windows hosted native probe, not a
production adapter, physical-device test or v1 acceptance. Task 7 stays open.

## One-variable change and preserved RED

Implementation `27a26b1033684f86cf2cbcb4142a8b7d2cbd95e1` changes only the first
`CreateWindowExW` argument in `SelfWindow.cs`, from `0x80` (`WS_EX_TOOLWINDOW`)
to `0`. Popup style, 64×64 geometry, nonactivating ShowWindow, paint markers,
timing, ABI, ownership and strict workflow gate are unchanged. The workflow
SHA-256 remains
`28ddfbb893ce0abd4e7cf559a7c8a79b20631a31bb234fd74e6c91cba4341cf2`.

The [original native RED](2026-10-04-wgc-hosted-item-failure.md), exact source
`470d0f3`, run `37208080753`, is retained: CreateForWindow failed with
`0x80070057`, exit 1, no capture session and zero frames. It is not relabeled
Skip or replaced by this result.

Historical Microsoft sample-maintainer reports made the tool-window style the
leading falsifiable hypothesis. The successful changed input supports that
hypothesis on this runner image; one prior failed run and later successful
runs do not establish a universal Windows eligibility contract or exclude
every timing/environment alternative. No IID/signature substitution or
whole-screen workaround was made.

## Local checks before native execution

On the local macOS host, Debug and Release standalone builds each report zero
warnings/errors. Each configuration passes the unchanged ten baseline and
twelve WGC-boundary managed cases, explicitly `native_api_called=false`.
Standalone format and diff checks pass. An independent read-only reviewer
confirmed that exactly one source blob changed and all other source/project
and workflow inputs remain unchanged. Logs are retained at
`/tmp/flowspan-wgc-style-diff-20261004/`.

These local checks are not Windows ABI or capture evidence.

## Actual hosted native execution

- [Run 37210127349](https://github.com/happys2333/flowspan/actions/runs/37210127349),
  attempt 1/job `111459441664` and fresh attempt 2/job `111460652945`:
  both run, job and strict native gate **success**.
- Exact source: `27a26b1033684f86cf2cbcb4142a8b7d2cbd95e1`.
- Ref: `refs/heads/codex/wgc-self-window-probe`; deliberate push trigger.
- Windows Server 2025 Datacenter 10.0.26100, X64;
  `win25-vs2026` image `20260925.250.1`, PowerShell 7.6.6.
- Actual SDK 10.0.401 (allowed by global.json latestFeature roll-forward),
  not an assertion of equality with local SDK 10.0.301.
- Invocation: standalone Release probe with `--native-self-window`.

Each original `native.stdout.raw` is 616 UTF-8 bytes, one record with CRLF;
each `native.stderr.raw` is empty. The two records are byte-identical:

```text
probe=pass mode=wgc_self_window reason=known_markers_verified api=WindowsGraphicsCapture driver=WARP apartment=MTA content=64x64 frames=1 row_pitch=256 feature_level=0xb000 owned_refs=21 released_refs=21 secondary_windows_policy=explicit_false hresult=0x00000000 marker_sha256=c836b97f55a9b91993869d4040438f5975519655fa455f07819951eb1373e54e frame_sha256=5e64758e298858414ec29e616070b4006bf212211309804268704a88e7bd7943 capture_session_started=true window_capture_executed=true cleanup_confirmed=true existing_windows_enumerated=0 user_titles_read=0 permissions_requested=0 pixel_files_written=0 protection=unknown
```

The supervisor records started/exited/raw-streams-complete true, exit 0,
timeout/tree-kill/supervisor-error false, and a 45,000 ms watchdog. The gate
checks all 24 success fields and rejects Skip; successful supervisor status
alone is not counted as native success. The verified result is consistent with
the retained original streams.

The marker hash is the fixed four-interior-color expectation. The whole-frame
hash is observed, not a universal DWM expectation. Native RowPitch equals
64×4, so this execution proves no native row padding; padding remains separate
managed-fixture evidence. The 21/21 count is this probe's tracked COM balance,
not an independent driver/GPU leak detector.

## Integrity anchors

Both artifacts are 6,631 bytes with 16 retained members:

| Attempt | Job | Artifact | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| 1 | 111459441664 | 11305988231 | `ffa46670a3bc3bdbc8abdf4746b60e8165adafc89089fd4b024f1196d5be3c68` |
| 2 | 111460652945 | 11306881092 | `41a2788d242030144d80170dc440f5afbd7b192abcdcbeb35df225a86fbd791d` |

```text
12d51f99e95bac7390544ef13ccc6b551f566c12302a9dd4db85064bac06de80  native.stdout.raw
ffb1a84414cc10cd68e65d2be4ad9df2261fec31104a2c5f562c04e814df0784  source-files.sha256
```

For each attempt, the outer artifact size/hash agrees between API, upload log
and complete local download. All 16 extracted artifact and 17 extracted log
members match their ZIP bytes. Both source manifests are 1,317 LF bytes;
all eleven source/project blobs and four build inputs match the exact commit,
and those manifests are byte-identical across attempts. The separate
source-manifest.sha256 file merely references that digest; its own digest is
`89c48440e11f9a68955ea48fcaa56a8a638ee3712868e0a9ab04cb442c42f58c`.
The original source manifest was
`d426f32f279ce47a0be67d2eb7a49eb9a278b1e409d6edf8e207764e63de5b7d`.
Manifests bind exact Git LF source bytes, separate from raw runtime CRLF.
Attempt-1 API metadata, logs and archive were frozen before requesting a
fresh-process rerun; original files remain at
`/tmp/flowspan-wgc-hosted-27a26b1/`, with attempt 2 isolated under `attempt2/`.
Each machine audit has `errors=[]`; the root also independently read raw
streams/status, recomputed attempt-1 anchors and checked the one-line diff.
Read-only audit reproduction:

```sh
ruby /tmp/flowspan-wgc-hosted-27a26b1/audit.rb
ruby /tmp/flowspan-wgc-hosted-27a26b1/attempt2/audit.rb
```

## Acceptance boundary and next work

This is a task-owned popup HWND captured through WGC using a software WARP
device. It never enumerates other windows, reads user titles, requests
permissions, hides the OS capture border, injects input or writes pixels.
Protection is still **Unknown**, not Safe.

It does not prove generic source identity, HWND ABA/source-Closed callbacks,
resize/recreate/device loss, permission deny/revoke, protected content, secure
desktop/input, independent Emergency Stop, hardware GPU, production host
composition, physical two-device operation, signing or release acceptance.
The production factory remains unavailable. Future platform work must retain
these gates instead of treating this feasibility result as product readiness.
