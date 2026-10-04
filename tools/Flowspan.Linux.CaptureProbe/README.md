# Linux thread-loop ABI smoke probe

This standalone, dependency-free `net10.0` executable is **not capture** and is
not a Flowspan product adapter. It is outside the solution and must not ship in
the product package. Production Remote Window readiness remains unchanged.

Default execution only supports ordinary Linux x64/arm64 processes with
`libpipewire-0.3.so.0` and the required exports. Missing library, missing exports,
or another host produces explicit `probe=skip`, never native-pass evidence.
The parent CI job must require an actual `probe=pass` and record the installed
package and reported library versions.

## Scope

The default path checks all exports before initialization, then directly calls:

1. `pw_init(NULL, NULL)` and a bounded `pw_get_library_version` read;
2. `pw_thread_loop_new` and `pw_thread_loop_get_loop`;
3. `pw_thread_loop_start`, two recursive locks, an outside-loop-thread fact,
   and `pw_thread_loop_get_time` with a 16-byte signed-64-bit `timespec`;
4. two unlocks, `pw_thread_loop_stop` outside the lock, destruction, and one
   paired `pw_deinit`.

There is **no** context/core creation, daemon connection, portal/D-Bus call,
stream, hardware operation, window enumeration/capture, permission request,
pixel read/write, source selection, input, or protected-content observation.
The time call verifies field range and an observable nonzero result; default-zero
out storage is rejected because it cannot prove that native wrote the result.
This is not a claim that all zero `timespec` values are universally invalid,
and it does not claim a monotonic clock.
The `in_thread` observation verifies the calling worker is outside the loop
thread; it does not prove frame delivery or callback scheduling.

The native owner lives entirely on one background worker. Its external join has
a 15-second budget. On timeout, the caller performs no native release and exits
nonzero; blocked or otherwise unconfirmed native owners and their loaded library
remain retained until standalone process teardown. A failed Stop/Unlock also
prevents later destruction/deinitialization. This process-exit strategy is not a
production cleanup contract.

## Run

```sh
dotnet build tools/Flowspan.Linux.CaptureProbe -c Release
dotnet run --project tools/Flowspan.Linux.CaptureProbe -c Release --no-build -- --help
dotnet run --project tools/Flowspan.Linux.CaptureProbe -c Release --no-build -- --self-test
dotnet run --project tools/Flowspan.Linux.CaptureProbe -c Release --no-build
dotnet format tools/Flowspan.Linux.CaptureProbe/Flowspan.Linux.CaptureProbe.csproj --verify-no-changes --no-restore
```

Help and all 17 self-test cases invoke no PipeWire native API and do not load its
library. The self-tests use only a small injected C-API boundary fake: layout,
success/Start failure, version/output bounds, blocked worker ownership, and ten
native-fault/cleanup outcomes. Their pass is portable contract evidence only.
Unknown arguments fail with exit code 2; native failures/timeouts use 1;
portable self-test, help, native pass, and explicit Skip use 0. Thus exit code 0
alone is insufficient evidence of native success.

## Fixed API sources and limits

The C signatures and stop-before-destroy/recursive-lock rules were checked at:

- PipeWire **0.3.7**, commit
  `0b3e9edaa2a155b6f1d1189a2dadbdb568c05ab1`:
  [thread-loop.h](https://github.com/PipeWire/pipewire/blob/0b3e9edaa2a155b6f1d1189a2dadbdb568c05ab1/src/pipewire/thread-loop.h),
  [thread-loop.c](https://github.com/PipeWire/pipewire/blob/0b3e9edaa2a155b6f1d1189a2dadbdb568c05ab1/src/pipewire/thread-loop.c),
  [initialization declarations](https://github.com/PipeWire/pipewire/blob/0b3e9edaa2a155b6f1d1189a2dadbdb568c05ab1/src/pipewire/pipewire.h),
  [version declaration](https://github.com/PipeWire/pipewire/blob/0b3e9edaa2a155b6f1d1189a2dadbdb568c05ab1/src/pipewire/version.h.in).
- PipeWire **1.6.9**, commit
  `8fa27cabdc6c0c1350c69c026af5850ef0af1e26`:
  [thread-loop.h](https://github.com/PipeWire/pipewire/blob/8fa27cabdc6c0c1350c69c026af5850ef0af1e26/src/pipewire/thread-loop.h),
  [init/deinit semantics](https://github.com/PipeWire/pipewire/blob/8fa27cabdc6c0c1350c69c026af5850ef0af1e26/src/pipewire/pipewire.c),
  [MIT license](https://github.com/PipeWire/pipewire/blob/8fa27cabdc6c0c1350c69c026af5850ef0af1e26/COPYING).

This probe does not require PipeWire >=1.6. `get_time` was added in 0.3.7, and
0.3.0 lacks `pw_deinit`; actual export availability is checked rather than
guessing from the package version. Before 0.3.49, deinit is one-shot: this probe
initializes/deinitializes exactly once and performs no reinitialization.
Native `bool` is marshalled as a one-byte return, C pointers as `nint`, and Linux
ordinary x64/arm64 `long`/`time_t` fields as signed 64-bit values.

No Linux native result has been obtained on the local macOS host. See
[local evidence](evidence/2026-10-04-local.md). GNOME/KDE Wayland, X11, portal
permissions/revocation, exact-window identity, protection, independent Emergency
Stop, physical devices, packaged behavior and the complete v1 gates remain open.
