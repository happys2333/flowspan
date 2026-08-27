# ADR 0025: Bounded SkiaSharp JPEG codec for Remote Window v1

- Status: Accepted
- Date: 2026-08-20

## Context

Remote Window needs a production image codec before native capture and rendering
can be composed. The codec must fit the frozen 16-chunk, 1-MiB logical video-frame
ceiling, recover by dropping individual frames, decode hostile input without
unbounded allocation, and behave on Windows, macOS, and Linux. Flowspan already
ships SkiaSharp 3.119.4 transitively through Avalonia 12.1.0, but relying on that
transitive version would leave a security-sensitive codec input implicit.

JPEG is not the most bandwidth-efficient choice for desktop motion. It does,
however, provide independently decodable intra-frames, mature Skia support, and
a small integration surface suitable for the first measured vertical slice.

## Decision

Pin `SkiaSharp` 3.119.4 as a direct `Flowspan.Desktop` dependency and record it in
the dependency inventory, lock files, SBOM, license report, and reproducible
release inputs. No Skia type crosses the Desktop boundary.

The encoder accepts only the existing owned, bounded BGRA8888 frame contract with
positive dimensions and a validated row stride. Alpha is discarded by encoding
opaque JPEG. It attempts only this ordered ladder and stops at the first candidate
no larger than 1,048,576 bytes:

1. original dimensions at quality 82, 68, then 54;
2. three-quarter dimensions at quality 68, then 54;
3. half dimensions at quality 68, then 54.

Scaling rounds down to at least one pixel, never enlarges a frame, and uses one
clear-on-return pooled scratch buffer per attempt instead of allocating a fixed
1-MiB large object for every profile. No adaptive loop, user-controlled quality,
or protocol-limit increase is allowed. If every candidate is too large or Skia
cannot encode it, the frame is dropped with a payload-free typed result.

The decoder accepts no more than 1,048,576 encoded bytes. A bounded JPEG marker
walk first requires one complete image with no trailing or concatenated image.
Before allocating pixels it then uses `SKCodec` metadata to require JPEG,
TopLeft orientation, one still frame, positive dimensions no larger than 16,384
per side, and at most 16,777,216 total pixels. Because the only output is tightly
packed BGRA8888, that pixel ceiling is exactly the 67,108,864-byte decoded
ceiling; there is no separate unreachable decoded-byte failure state. It rejects
animation/multiple frames, other formats, invalid or truncated data, unsupported
conversion, and incomplete decode. Success returns one owned bounded BGRA8888
buffer and plain dimensions; callers never receive Skia objects.

Successful encoded and decoded owners implement idempotent disposal and clear
their managed payload or pixel buffer before releasing it. The codec clears
source and scaled `SKBitmap` pixel spans and the native `SKData` encoded copy
before disposing those Skia owners; failed managed decode buffers and pooled
encode scratch are also cleared. Callers must dispose every successful owner.

Commit one small fixed JPEG as the normative decoder fixture. The v1 fixture is
397 bytes with SHA-256
`f294e425eda6aea42373311b447ac5518eabe2a897304b5a90c9a25ae3c8095e`.
Do not hash-freeze encoder output across operating systems because native Skia
implementations may produce different valid bytes. Encoder tests instead verify
JPEG detection, dimensions, alpha discard, ladder bounds, deterministic result
shape within one runtime, and successful decode through the bounded decoder.

Revisit the codec after packaged two-device measurements on every supported
platform if median end-to-end latency exceeds 150 ms, sustained view falls below
20 frames per second at 1920x1080, median encoded bandwidth exceeds 20 Mbit/s, or
codec CPU exceeds one logical core for 30 seconds. A replacement codec requires
its own dependency, security, packaging, and protocol decision; measurements do
not silently relax current limits.

## Alternatives considered

### PNG

PNG is lossless and simple but routinely exceeds the frame budget on photographic
or rapidly changing windows. It is unsuitable as the only production fallback.

### H.264, HEVC, AV1, or platform codecs now

These may provide materially better quality and latency, but add codec state,
hardware and patent variability, platform-specific lifetimes, and recovery paths
before Flowspan has physical measurements. Deferred behind the revisit gate.

### Continue using transitive SkiaSharp

A security-sensitive decoder should not change because Avalonia changes its
internal graph. Rejected; the exact package becomes a reviewed direct input.

### Freeze encoder bytes as a golden fixture

That would make native implementation details a cross-platform compatibility
contract without improving decoder safety. Rejected.

## Consequences

- The first production media path is simple, bounded, and frame-independent but
  not optimized for motion or low bandwidth.
- Package and native-asset changes become visible in locked restore and release
  evidence.
- Hostile decoder tests and allocation limits are mandatory on all CI systems.
- Sensitive managed and Skia scratch lifetimes are explicit and clear before
  release; sustained allocation/GC behavior remains a Task 5 packaged load gate.
- Physical quality, native capture/rendering, signed package, and two-device
  evidence remain release gates.

## Decision evidence

- <https://www.nuget.org/packages/SkiaSharp/3.119.4>
- <https://github.com/mono/SkiaSharp/commit/f568ac94dd768ef9a2f593537cfde2dd0d348ef5>
- <https://github.com/mono/SkiaSharp/blob/f568ac94dd768ef9a2f593537cfde2dd0d348ef5/LICENSE.md>

The restored nupkg declares MIT and hashes to
`e32a449d31945c0b9d169eb5bc676e1e3f589aab69888e49d703fdc1c384c176`
with SHA-256. Committed project lock files remain the reproducible content-hash
authority used by CI and release packaging.

The direct and native-asset inputs were rechecked on 2026-08-27. `v3.119.4` was
published as a non-prerelease on 2026-05-25; the upstream repository is active,
not archived or disabled, and the package plus Linux, macOS, and Win32 assets all
declare MIT and the same source commit. Their native-asset nupkg SHA-256 values
are respectively
`fae0554059b1107ef7888e46c20bdfb548401ef7a7a6f7391ad4fadc7432d50a`,
`f7f2f539ce5bba337aa4a8d6eac25caf58cbdd12edf3f32ddcc98294e730cf2c`,
and `5a5698b1b4e1fdc9ffe9868df6874db5fa69f21a4de76ba71a01a542e9b43391`.
The locked full solution reported no known direct or transitive NuGet
vulnerabilities on that date. Hosted package and codec behavior on all three
operating systems remains required evidence, not inferred from this review.
