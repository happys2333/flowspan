# macOS production capture probe

This standalone tool exercises the actual `MacOSRemoteWindowScreenCaptureKitApi`,
`MacOSRemoteWindowSourceCatalog`, and `MacOSRemoteWindowCaptureBoundary` on a
supported arm64 macOS host. It is not shipped with Flowspan.

```sh
dotnet run --project tools/Flowspan.MacOS.NativeCaptureProbe
dotnet run --project tools/Flowspan.MacOS.NativeCaptureProbe -- --run
```

The default invocation and `--help` only print usage and a skip result. They do
not load native frameworks, call the permission preflight, create an application,
or capture anything. Unknown arguments return exit code 2 without native work.
`--run` returns a skip result when the host is unsupported or the existing screen
capture permission is absent. The tool never requests permission.

With existing permission, the tool creates its own borderless 64-by-64-point,
four-color window in its own process. AppKit bootstrap, activation policy, and
run-loop pumping are confined to this executable. The production library's
enumeration is first checked to preserve an absent `NSApplication` and exclude
the current process by default.

The internal native API allowlist admits only the task-created window. A second
guard checks both its window ID and this process's PID before publication into
the catalog and before every capture creation. Other enumerated sources are
disposed without capture. The tool does not read window titles or process names,
and never saves pixels. Marker validation reads only the task-owned image in
memory; pixel owners are disposed through the production clearing-owner path.

The run checks a tightly packed native frame, four interior color markers,
`StopAndDrainAsync` and independent `IsDrained`, a start/stop race, a real catalog
and boundary frame with generation binding, and the boundary's `StopCompletion`.
After boundary drain, it observes zero additional downstream `takeSampleOwnership`
delivery-proxy calls and sink frames for 600 ms. The proxy counter does not
instrument all `DidOutput` or native ABI entries, which can return without delivery.
It checks that hiding the task window invalidates the source within a bounded 3-second poll,
and checks that both native callback roots and retained capture owners return to
zero. The callback root count uses private reflection only as a standalone
diagnostic and fails closed if its shape changes; the retained-owner count uses
the driver's internal diagnostic. Neither count alone proves complete cleanup.

The API wrapper also forwards failed-construction owner handoff to the driver,
so the production boundary can join asynchronous rollback instead of losing its
owner. This real-host run exercises normal successful operations on the task-owned
window. It does not inject failure into native Start, Stop, output removal, or
queue barriers. Simulated failure-path contract tests provide separate evidence
and must not be reported as real native fault injection.

Limitations: `SCShareableContent` enumeration is global and can expose metadata
of other windows; allowing only task-owned capture and avoiding title reads does
not isolate enumeration metadata. Permission revocation between preflight and
enumeration remains a TCC time-of-check/time-of-use race and may cause operating
system UI. This run does not prove handling of same-process window-ID ABA reuse.
Native protection remains `Unknown`; sensitive-window and secure-input protection
are not validated here.

Exit code 0 means either a clearly labeled skip or all selected checks passed;
exit code 1 means verification failed, including an observation timeout or
unproven cleanup. An observation timeout does not establish native termination.
Real-host success here does not prove Windows/Linux behavior, physical two-device
integration, input injection, production UI composition, or release readiness.
