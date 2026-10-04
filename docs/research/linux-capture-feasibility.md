# Linux capture feasibility, 2026-10-04

Status: read-only API/source research. No portal or Linux capture execution.
Task 8 and production host readiness remain open. The immediate implementation
is a separate no-capture PipeWire thread-loop ABI probe, not a production adapter.

## Session-selected source identity

Wayland's ScreenCast portal does not offer prompt-free global window enumeration
or an authoritative native window/PID/application-instance identity. The user's
selection creates a session-scoped stream capability. Node titles or `app.id`
must not masquerade as exact window identity.

ScreenCast interface v6 provides `pipewire-serial` (`t`, uint64). Its declaration
explicitly warns that node IDs can be reused after destruction, including
hotplug, display changes and suspend/resume. A candidate should require one
WINDOW (`source_type=2`) stream, bind the exact Session generation and returned
authoritative serial, target the PipeWire `target.object` serial, and disable
reconnection to a replacement source. Missing authoritative serial is a named
unsupported/degraded state, not exact-source proof.

Selection occurs in `ScreenCast.Start`, which already starts an OS screencast
session. A possible `PortalSelectionReservation` would retain this locally
authorized session without a Flowspan consumer or pixel ingestion before Ready;
no pixel disclosure could occur before final Admission. This is not a claim
that the compositor never began capture before Ready. The eventual native
composition must explicitly reconcile that distinction with the managed tracer
language in NR10.6; this research does not waive any approved acceptance gate.

Successful selection, available portals, ordinary frames or black frames never
prove Protection Safe. RemoteDesktop device authorization also does not prove
input is restricted to the selected window. Keep protection Unknown and driving
unavailable until those separate safety contracts have real evidence.

## Request, Session and file-descriptor ownership

An eventual adapter must subscribe to Response/Closed before issuing a portal
method, using a random unguessable token and the unique bus name to predict its
object path. Response can precede the method reply; success cannot commit until
the returned handle matches the expected path. Duplicate/late response,
cancellation, expiry, bus disconnect and portal name-owner change must never
revive an old generation. `Request.Close` emits no Response; cleanup must not
wait for one. Late Session/FD results still need an owned cleanup path.

The response `session_handle` has the historical string signature and must be
validated as an object path. A D-Bus `h` is an index into a transferred-FD list,
not the operating-system FD number. `Tmds.DBus.Protocol.Reader.ReadHandle<SafeFileHandle>()`
transfers ownership; `ReadHandleRaw()` leaves it owned by the Message and must
not be wrapped as another owning handle. Native connection FD transfer and all
failure paths require exactly-once ownership tests.

Session closure, source removal, stream error, bus/portal replacement and
revocation close delivery immediately. Native teardown belongs to a separate
owned worker and must join callbacks; callback-local Dispose cannot self-wait.

## Toolchain evidence and smallest slices

Tmds.DBus.Protocol 0.95.1 is MIT and supplies a maintained low-level D-Bus route
compatible with modern .NET. PipeWire.NET/Media 0.3.0-dev2 is MIT, active and
alpha; its context explicitly requires a native library at least 1.6. That is
not an assumed capability of `ubuntu-latest`. It is not selected for production
by this research, and no dependency has been added.

The first independent probe will use only stable base C ABI functions from
plain C#: initialize PipeWire, inspect its loaded version, create/start/stop/
destroy a private thread loop, and balance deinitialization. It will create no
context/core/daemon connection, stream, portal, hardware source or pixel owner.
An actual Linux native success is only thread-loop ABI evidence. Other OS/ABI
and absent-library results must be explicit skips, not passes.

Subsequent slices can add private fake/real D-Bus Request/Session ownership
tests, then a private no-hardware PipeWire marker producer/consumer graph with
hash, serial, source-loss and Stop-race checks. Those synthetic markers do not
prove Wayland ScreenCast, compositor isolation or physical Devices. Native CI
must record the actual library version and image rather than assume them.

## Fixed source inputs

- [ScreenCast v6 declaration](https://github.com/flatpak/xdg-desktop-portal/blob/1d20fadc304f6601452b5db65ed91197dba77041/data/org.freedesktop.portal.ScreenCast.xml)
- [Request paths, race and Close](https://github.com/flatpak/xdg-desktop-portal/blob/1d20fadc304f6601452b5db65ed91197dba77041/data/org.freedesktop.portal.Request.xml)
- [Session closure](https://github.com/flatpak/xdg-desktop-portal/blob/1d20fadc304f6601452b5db65ed91197dba77041/data/org.freedesktop.portal.Session.xml)
- [Tmds.DBus.Protocol project](https://github.com/tmds/Tmds.DBus/blob/491bde2c16d65a7904397933cffa24e15e45eb2a/src/Tmds.DBus.Protocol/Tmds.DBus.Protocol.csproj)
- [Typed and raw FD ownership](https://github.com/tmds/Tmds.DBus/blob/491bde2c16d65a7904397933cffa24e15e45eb2a/src/Tmds.DBus.Protocol/Reader.Handle.cs)
- [PipeWire.NET version and lifetime](https://github.com/Agash/PipeWire.NET/blob/c5a022704fdf3723cba2b792b226d7a960a53474/src/PipeWire.NET/Core/PipeWireContext.cs)
- [PipeWire.NET callback/disposal boundary](https://github.com/Agash/PipeWire.NET/blob/c5a022704fdf3723cba2b792b226d7a960a53474/src/PipeWire.NET.Media/PipeWireStreamCore.cs)
- [PipeWire stream flags/buffers](https://github.com/PipeWire/pipewire/blob/8fa27cabdc6c0c1350c69c026af5850ef0af1e26/src/pipewire/stream.h)

These are protocol declarations and permissive/public API inputs for clean-room
implementation, not copied LGPL/GPL product core. None establishes packaged
GNOME/KDE grant/deny/revoke, protection, input, independent Emergency Stop,
X11 degradation, two-Device behavior, signing or release acceptance.
