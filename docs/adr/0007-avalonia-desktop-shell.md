# ADR 0007: Use Avalonia 12.1 for the desktop shell

- Status: Accepted
- Date: 2026-07-13
- Decision owners: Flowspan maintainers

## Context

Flowspan now needs its first executable desktop composition root after the
headless domain, security, discovery, and transport slices. The shell must run
on Windows, macOS, and Linux without introducing a second implementation
language. It must also permit deterministic headless tests while keeping native
launch, accessibility, packaging, and permission evidence as separate gates.

The UI framework is an outer dependency. No Avalonia type may cross into the
domain, protocol, application, security, transport, or platform contracts.
First launch must not request screen capture, remote-input, or accessibility
permission merely because the window opened.

## Decision

Pin the Avalonia family to exact version `12.1.0` and use:

- `Avalonia`, `Avalonia.Desktop`, and `Avalonia.Themes.Fluent` in
  `Flowspan.Desktop`;
- `Avalonia.Headless` only in `Flowspan.Desktop.Tests`;
- the existing xUnit v2 infrastructure rather than
  `Avalonia.Headless.XUnit`, whose 12.1.0 package depends on xUnit v3
  extensibility.

The production entry point uses `UsePlatformDetect()` and targets the
repository-wide `net10.0` framework. The Fluent package supplies maintained
control templates, but Flowspan owns its palette, typography, spacing, focus,
and safety-state styles. The application does not use Avalonia's optional Inter
font package or diagnostics package.

The composition root selects the existing operating-system-protected device
identity adapter and presents only a bounded identity summary to the view
model. A separate, visibly degraded in-memory validation mode is permitted for
CI and headless composition tests. A production storage failure is surfaced as
a blocked state with a recovery action; it never falls back silently to a
plaintext identity.

## Evidence reviewed

- NuGet lists `12.1.0` as a stable version of all selected packages, published
  on 2026-07-09, with MIT license expressions.
- The package catalog provides `net10.0` assets for `Avalonia`,
  `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, and `Avalonia.Headless`.
- The Avalonia 12.1.0 release is non-draft and non-prerelease. Its release notes
  include a headless-session construction hang fix and a Windows UI Automation
  fix, both relevant to this slice's verification surface.
- Avalonia documents Windows, macOS, and Linux desktop support. Its current
  supported-platform page calls Linux Wayland support a private preview, so
  Flowspan does not treat Wayland launch as accepted without matching-machine
  evidence.
- Avalonia's repository license is MIT.

Sources, reviewed 2026-07-13:

- <https://www.nuget.org/packages/Avalonia/12.1.0>
- <https://www.nuget.org/packages/Avalonia.Desktop/12.1.0>
- <https://www.nuget.org/packages/Avalonia.Themes.Fluent/12.1.0>
- <https://www.nuget.org/packages/Avalonia.Headless/12.1.0>
- <https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.0>
- <https://docs.avaloniaui.net/docs/supported-platforms>
- <https://github.com/AvaloniaUI/Avalonia/blob/12.1.0/licence.md>

These sources establish package and upstream facts, not Flowspan runtime or
accessibility evidence.

## Dependency boundary

`Flowspan.Desktop` is an outer composition project. View models consume narrow
desktop-facing startup/session ports and immutable presentation snapshots.
Only the concrete composition root knows the security and platform identity
adapters. Lower layers never reference `Flowspan.Desktop` or Avalonia.

Headless tests prove control-tree construction, bindings, input routing, and
declared automation metadata. They do not prove native rendering, screen-reader
output, OS high-contrast behavior, Wayland/X11 selection, app signing, or real
permission prompts.

## Alternatives considered

### Avalonia 11.3 maintenance line

The older line has a longer deployment history. Version 12.1 is selected because
the repository already targets .NET 10, 12.1 publishes an explicit `net10.0`
asset group, and its current headless and Windows automation fixes apply to the
verification plan. Exact pinning and CI limit upgrade drift; real-machine
acceptance remains mandatory because the release is recent.

### Per-platform native UI frameworks

WinUI/WPF, AppKit, and GTK would require three UI implementations or a second
language/runtime. That would multiply accessibility behavior and test surfaces
before Flowspan has completed its native capture/input adapters.

### Electron or a browser shell

This would add a second language/runtime and a browser-process attack and
packaging surface without reducing the platform-native capture/input work.

### .NET MAUI

MAUI does not provide the required first-class Linux desktop target, so it does
not meet the approved v1 platform scope.

## Consequences

- The desktop shell remains one C#/.NET codebase across the three v1 platforms.
- The production dependency graph becomes materially larger and must remain
  isolated, locked, audited, and included in the release SBOM/license report.
- Headless UI tests can run in the existing CI matrix without a display server.
- A green headless test is not evidence of a successful native desktop launch.
- Linux X11 is the currently documented stable UI path; Wayland launch remains
  an explicit risk and manual acceptance item even though Flowspan's capture
  design remains Wayland-portal-first.
- Patch or minor updates require lock-file review, the complete CI matrix, and
  renewed native smoke evidence before a release candidate adopts them.

## Revisit triggers

Revisit if Avalonia cannot meet keyboard, screen-reader, scaling, reduced-motion,
or packaging acceptance on any target; if Linux Wayland cannot provide a
reliable shell path; if a high-severity dependency issue lacks a timely fix; or
if measured startup/memory costs exceed the release budgets established during
packaging work.
