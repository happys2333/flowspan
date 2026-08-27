# Native Remote Window Transport Candidate Evidence - 2026-08-28

## Evidence status and boundary

Classification: **local portable contract candidate** and **same-host managed
loopback candidate**.

Branch: `codex/v1-foundation`

Base commit: `645b065571a08f777a8272d171be5d5151826bd6`.

Candidate implementation commit: **not yet assigned**. This record currently
describes an uncommitted working tree on macOS. It is not exact-commit hosted CI,
native-platform, physical-device, packaged, signed, or release evidence.

This candidate supports only Task 5.1-5.3 in
`specs/v1/native-remote-window/tasks.md`. Task 5 and Flowspan v1 remain open.

## Implemented Transport slice

- Each authenticated protocol-1.6 control registration transfers its
  purpose-separated Remote Window media session into one connection-owned entry.
  Protocol 1.5 and earlier remain unable to publish or attach a media route.
- The production `FlowspanTcpInboundListener` classifies the fixed `FSM1`
  attachment on the same published endpoint as pairing and authenticated control,
  with independent bounded media capacity and exactly one owner for the pre-read
  envelope and accepted stream.
- Responder route registration and initiator attachment bind the directed Device
  pair, route, Session, and Activity. Expiry, revoke, matched attachment failure,
  media send/receive fault, disposal, or listener shutdown consumes the route,
  releases the media owner, and requests stop of the owning control connection.
- Attachment and listener cleanup preserve a single original failure and aggregate
  distinct primary and cleanup failures. Caller cancellation, deadline, and
  session disposal stop waiting on token-ignoring async handshake I/O while
  detached borrowed buffers remain stable until the underlying operation settles
  and are then cleared.
- One logical video frame owns 1 through 1,048,576 bytes and is split into at most
  16 chunks of at most 65,536 bytes. The assembler requires the exact binding,
  video kind, count, zero-based order, continuous sequence, non-empty chunks, and
  aggregate bound; it consumes and clears chunks and partial state on every path.
- The logical sender owns one active frame and only the latest pending frame. It
  sends one wire chunk at a time through the existing peer/session-budgeted queue,
  reports bounded outcomes, clears replaced and terminal owners, and prevents
  logical-frame interleaving.
- Stop/disposal initiation returns while a cancellation callback is temporarily
  blocked. Cleanup completion joins that callback and any borrowed send, then
  attempts worker, sink, budget, and cancellation-source cleanup; throwing stages
  are aggregated. Active and pending submissions settle and the same peer can
  create a fresh queue after completed cleanup.

## Local candidate gates

The final candidate tree is required to run:

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Debug --no-restore
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
git diff --check
```

Environment:

```text
Host: macOS 26.6.2 (build 25G83), Apple Silicon, Asia/Hong_Kong
.NET SDK: 10.0.301
Branch: codex/v1-foundation
Verification date: 2026-08-28
Base commit: 645b065571a08f777a8272d171be5d5151826bd6
Candidate state: uncommitted working tree
```

Observed results on that final local tree:

- Locked restore and full format verification passed.
- Release build with warnings as errors completed with 0 warnings and 0 errors.
- The Transport suite passed `456/456` in Debug and `456/456` in Release.
- The complete Release solution passed `1872/1872`: Desktop 446, Transport 456,
  Integration 338, Platform 219, Security 129, Release 71, Domain 60, Protocol 60,
  Platform.Windows 27, Platform.Linux 27, Platform.macOS 25, and mDNS 14.
- The focused strict-assembler regression passed `15/15`; the affected media
  session/channel/listener/logical-sender/outbound-queue set passed `62/62`.
- The direct/transitive vulnerability query covered all 26 projects and found no
  known vulnerable NuGet package. `git diff --check` and the conflict-marker scan
  passed.

These results are same-host macOS managed-code evidence. Platform-named contract
assemblies did not execute native Windows or Linux Remote Window adapters here.

## Independent review

Two read-only reviews of the initial Transport tree identified no P0 and one P1:
a second chunk zero silently replaced an incomplete logical frame on the ordered
stream. The candidate now rejects that truncation, clears both owners, and proves
recovery only through a subsequent fresh frame. It also changes registration
cleanup to asynchronous failure-preserving disposal and unconditionally observes
late prefix-write/read/flush faults after cancellation.

Final independent ownership, security, and acceptance reviews of the complete
candidate tree reported no remaining P0 or P1. One P2 diagnostic issue remains:
normal control-session shutdown can observe the same media-registration cleanup
failure through both dispatcher-owned cleanup and the joined registration
completion, so a final aggregate may contain the same exception twice. The
failure remains visible and cleanup is still joined; this does not leak authority,
routes, frame owners, or budget.

## Open evidence

- Task 5.4 must inject small frame/plaintext/sequence budgets and prove that
  exhaustion closes the attachment and owning control connection, performs a
  complete fresh authenticated control handshake, derives a fresh media session,
  and uses a new route without in-place rekey or consumed-route reuse.
- Task 5.5 must compose `ProductionDesktopRemoteWindowService`, exact native source
  lease/revalidation, permission/readiness, controller, capture-to-JPEG encoder,
  authenticated media, decoder-to-participant renderer, protection, independent
  Emergency Stop, visible sharing, input, and ordered teardown. Production
  composition must continue to report `native_adapters_unavailable` until then.
- The current tests do not prove completed teardown if a cancellation callback or
  synchronous third-party `Stream.Dispose` implementation blocks forever. They
  prove prompt stop/disposal initiation for the injected callback case, followed
  by complete joined cleanup after release. They do not prove native Windows,
  macOS, Wayland, or X11 behavior.
- The candidate commit must pass exact-commit Windows, macOS, and Linux CI, Secret
  Scan, and CodeQL before this record may claim hosted portable evidence.
- Physical two-device loss/reconnect/load/latency, native accessibility, signed or
  notarized package lifecycle, Tasks 6-10, affected release criteria, and the
  long-term Goal remain open.
