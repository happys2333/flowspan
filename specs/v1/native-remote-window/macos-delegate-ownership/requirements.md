# macOS delegate ownership prerequisite

Status: scoped implementation contract under NR6, NR8 and NR10; not production
source-loss acceptance. Existing ScreenCaptureKit capture keeps `delegate=0`.

As a maintainer, I need a reproducible no-capture callback lifetime proof before
publishing a delegate to ScreenCaptureKit. This prerequisite grants no capture,
input, protection, participant or Driver authority.

## Acceptance criteria

- MDO1: When an owner is reserved, the pool shall consume one fixed slot before
  publication. A pool shall support at most 16 slots, reject exhaustion, and
  never recycle a published slot or replace its bridge-address binding.
  Native probe publication shall retain its bridge and pool until process exit;
  capacity is a probe-instance budget, not a production memory bound.
- MDO2: When a bridge receives a terminal event before binding, it shall retain
  at most one bounded stream-identity fact without dereferencing native stream
  or error pointers and without calling a not-yet-installed handler. Binding
  shall be exact and one-time; a mismatch or failed initialization shall retire
  the published owner rather than treat it as unpublished.
- MDO3: When an active, exact-stream owner receives a terminal event without
  any sample event, it shall synchronously close its local admission and notify
  once outside its tiny state gate. Active/inactive observations shall not
  reopen admission or restore authority. Wrong-stream events shall grant none.
- MDO4: When retirement starts, the owner shall synchronously close admission
  before waiting. It shall join admitted managed invocations, then clear handler
  and binding while retaining an immutable closed tombstone. Completion shall
  be named `ManagedInvocationsExited`, never `NativeDrained`.
- MDO5: When retirement is requested from its callback or an ExecutionContext
  descendant, it shall reject self-join before acquiring the owner gate. Both
  direct calls and Task.Run descendants shall report `SelfJoinRejected`.
  Notifications, waits and completion continuations shall run outside the gate.
- MDO6: When a late callback targets a retired bridge, it shall return without
  reading native stream/error data or affecting another owner. Reverse entries
  shall contain managed faults, restore callback ancestry and return invocation
  ownership in finally; failures shall remain observable, not native success.
- MDO7: Default/help/unknown probe modes shall make no native calls. Only
  `--run-synthetic` on supported ordinary-arm64 macOS may instantiate NSObject
  bridges and invoke typed Objective-C/GCD callbacks. It shall not create
  SCStream, AppKit, windows, enumerate sources, preflight/request permissions,
  read titles, capture samples/pixels or inject input. Unsupported hosts shall
  report explicit Skip, not native Pass.

## Non-goals

SCStream delegate retention/unbinding, no-future-native-callback proof, real
source destruction, production capture drain, TCC, protection, input, independent
Emergency Stop, physical devices and packaged/release behavior remain open.
