# Remote Window and Mirror Control Design

Status: approved design for the portable Task 6 control-plane slice

## Design summary

`RemoteWindowSessionController` owns the live safety state for one Activity on
its execution host. It composes the existing immutable `MirrorSession` and
`DriverLease` domain model with current Capability lookup, protection policy,
capture control, remote-input injection, and local session-disconnect ports.

The controller lives in `Flowspan.Platform`, the existing portable platform and
safety layer. That project already depends inward on `Flowspan.Application` and
`Flowspan.Domain`; placing the controller there avoids an Application -> Platform
cycle and keeps native projects as thin implementations of narrow local ports.
No native API enters Domain or Application.

The live state is intentionally memory-only. A process restart removes capture,
transport keys, participants, and Driver authority rather than reconstructing a
possibly stale sharing session.

## Public control surface

The portable slice introduces these conceptual interfaces and models:

```csharp
public interface IMirrorAuthorizationSource
{
    CapabilityGrant GetCurrentGrant(DeviceId peerDeviceId);
}

public interface IRemoteWindowCaptureBoundary
{
    ValueTask<LocalBoundaryResult> StartAsync(...);
    LocalBoundaryResult PauseNow(MirrorPauseReason reason);
    LocalBoundaryResult ResumeNow();
    LocalBoundaryResult EmergencyStopNow();
    LocalBoundaryResult StopNow();
}

public interface IRemoteInputBoundary
{
    ValueTask<LocalBoundaryResult> InjectAsync(
        RemoteInputBatch batch,
        CancellationToken cancellationToken);
    LocalBoundaryResult PauseNow(MirrorPauseReason reason);
    LocalBoundaryResult ResumeNow();
    LocalBoundaryResult EmergencyStopNow();
    LocalBoundaryResult StopNow();
}

public interface ILocalSharingSessionBoundary
{
    LocalBoundaryResult DisconnectPeerNow(DeviceId peerDeviceId);
    LocalBoundaryResult DisconnectAllNow();
}
```

Every `*Now` member is a local fail-closed gate operation, not remote protocol.
Its implementation must synchronously prevent new frames/input/session traffic;
resource disposal may continue internally. The type intentionally has no peer
acknowledgement method.

`LocalBoundaryResult` is either confirmed, already applied, or failed with one
validated reason code. It contains no exception, payload, or peer text.

The controller exposes start, join/update role, transfer Driver, inject input,
reconcile current Capability, apply protection observation, disconnect peer,
refresh expired lease, ordinary stop, emergency stop, and explicit local reset.
Each response contains a `RemoteWindowSharingSnapshot` or a bounded reason code.

## Live state

```mermaid
stateDiagram-v2
  [*] --> Idle
  Idle --> Starting: start admitted
  Starting --> Active: capture gate confirmed
  Starting --> Unavailable: capture failed
  Active --> ProtectionPaused: unsafe or uncertain protection
  ProtectionPaused --> Active: fresh safe state and local gates resume
  Active --> Ended: ordinary local stop
  ProtectionPaused --> Ended: ordinary local stop
  Starting --> EmergencyStopped: emergency stop
  Active --> EmergencyStopped: emergency stop
  ProtectionPaused --> EmergencyStopped: emergency stop
  EmergencyStopped --> Idle: explicit local reset
  Unavailable --> Idle: retry/reset
```

`MirrorSession` remains the authority source once start is active. The wrapper
state represents capture admission and protection pause, which are not Driver
Lease states. The sharing snapshot always derives from one locked copy of both.

Start is two-phase. It publishes `Starting`, awaits only local capture admission,
then publishes `Active` if no emergency stop won the race. If emergency stop
occurs while start is pending, the latch and state change synchronously; a late
successful start is immediately halted and cannot publish `Active`. A late start
also reconciles the latest monotonic protection observation rather than
publishing from the observation that originally admitted the attempt.

## Serialization and preemption

Normal operations use one `SemaphoreSlim` to serialize capture start, join,
role change, Driver transfer, input injection, disconnect, expiry refresh, and
ordinary stop. State reads and the emergency/protection fast paths use a short
private lock.

Input holds the normal-operation gate from its final authorization/protection/
epoch check through the local injection call. Therefore a normal transfer cannot
publish a new Driver while an older input operation is still being admitted.

Emergency stop never waits for the normal-operation gate. Under the state lock
it activates `EmergencyStopLatch`, transitions `MirrorSession` through
`EmergencyStop`, and publishes the higher epoch before calling the three local
`*Now` boundaries. Protection blocking follows the same fail-closed ordering:
state first denies input, then synchronous local capture/input gates are paused.

## Authorization and participant reconciliation

The host is always the safe owner and local driver-eligible participant.

| Requested/effective role | Required current grant |
| --- | --- |
| View-only | `mirror.view` |
| Driver-eligible | `mirror.view` and `mirror.drive` |
| Driver transfer/input | `mirror.view` and `mirror.drive` re-read at use time |

Adding a peer uses no cached preview authorization. Reconciliation has closed
behavior:

- both grants current: retain DriverEligible;
- view current, drive absent: transfer any peer-held lease to the host with a
  higher epoch, then retain the peer ViewOnly;
- view absent: remove the participant, return any peer-held lease to the host,
  and locally disconnect that peer;
- host: never read from peer Trust and never remove/downgrade.

Participant bounds are checked before mutation. Rejection does not call capture,
input, or session boundaries.

## Portable input contract

`RemoteInputEvent` is a discriminated, immutable value for HID key, normalized
pointer move, pointer button, or bounded two-axis scroll. Factories enforce the
valid fields for each kind. `RemoteInputBatch` contains 1-64 events and makes a
defensive copy. It intentionally carries no text, process handle, native window
handle, or platform payload.

An input attempt supplies peer Device ID, lease epoch, and batch. Before calling
the port, the controller checks:

1. emergency latch and active session state;
2. participant and DriverEligible role;
3. current `mirror.view` and `mirror.drive`;
4. fresh safe `ProtectionSnapshot`;
5. exact current unexpired Driver Lease epoch.

Only an `Allowed` decision reaches `IRemoteInputBoundary`. The batch is never
included in the result.

## Protection control

The existing `ProtectionKind`, `ProtectionSnapshot`, and `RemoteInputPolicy`
remain the portable decision vocabulary. A snapshot older than 500 ms, more than
50 ms in the future, Unknown, SensitiveWindow, SecureInput, or ProtectedContent
is blocked.

Applying a blocked observation first publishes `ProtectionPaused`, then calls
capture and input `PauseNow`. The result records each local confirmation. A
boundary failure leaves the session blocked and explicitly unconfirmed; it does
not claim capture is blank.

A fresh Safe observation calls both `ResumeNow` gates. `Active` is published
only if both confirm. If either resume fails, both gates receive a fail-closed
Unknown pause so a partially resumed capture or input path cannot remain open;
the operation still reports its original boundary failure and an unconfirmed
blocked state. Native capture adapters must also continuously apply platform
protection independently because controller polling alone cannot prove
frame-by-frame safety.

Every accepted observation advances a monotonic protection revision. A pause or
resume boundary result may publish a stable state only while its revision is
still current. A superseded operation re-applies the latest target; same-thread
boundary re-entry records the new observation for the outer reconciler instead
of recursively opening another boundary chain. Reconciliation is limited to
eight attempts, after which both gates receive an Unknown pause and the session
remains blocked. Emergency Stop is checked after each re-entrant boundary and
is re-applied if a stale protection operation ran after it.

## Emergency stop and ordinary stop

Emergency stop is synchronous and non-cancellable. It performs:

1. activate the local latch;
2. revoke the Driver Lease through a higher epoch and publish EmergencyStopped;
3. call capture `EmergencyStopNow`;
4. call input `EmergencyStopNow`;
5. call local session `DisconnectAllNow`.

All boundary calls run even when an earlier call fails or throws. Throws become
`local_boundary_exception` for only that boundary. The result separately names
capture, input, and session confirmation. Repetition preserves the first stop
epoch and retries unconfirmed local boundaries without restoring authority.

Ordinary stop is also local and closes all three boundaries, but it serializes
with normal work and ends the session. Explicit reset after emergency stop only
returns the controller to Idle and clears the latch after every local boundary
is confirmed stopped. It does not restart capture or restore participants.

## Evidence and remaining layers

Portable unit/integration tests use public controller methods and deterministic
ports. They prove ordering and behavior but not operating-system behavior.

Later slices add:

- authenticated control messages and bounded encrypted media with
  backpressure/resource ceilings;
- Windows Graphics Capture, SendInput, secure desktop, protected capture, and a
  local emergency hotkey;
- ScreenCaptureKit, Accessibility/TCC, secure input, and protected-window probes;
- Wayland portal/PipeWire/RemoteDesktop and explicit X11 degradation;
- Desktop permission preflights, persistent accessible sharing indicator, and
  real-machine accessibility/physical-device evidence.

No portable test, hosted runner, or fake closes those native/manual gates.
