# ADR 0001: Use C# and .NET 10 as the single implementation language

- Status: Accepted
- Date: 2026-07-13
- Decision owners: Flowspan maintainers

## Context

Flowspan needs a small, testable core plus Windows, macOS, and Linux platform
integration. The starting repository has no legacy constraints. The local
development host already has .NET SDK 10.0.301, Node 26, CMake, and Clang, but no
Rust or Go toolchain. Product direction explicitly prefers one language when it
can meet quality and platform needs.

The implementation needs deterministic state-machine tests, usable cryptography
and networking primitives, native interop, desktop packaging, accessibility,
and maintainability across three operating systems.

## Decision

Use C# with .NET 10 LTS across the domain, protocol, services, native adapter
wrappers, tooling, and tests.

- Production core libraries initially depend only on the .NET base class
  libraries.
- Native platform APIs are reached through small C# interop/adaptation
  assemblies; a native helper is added only when an ADR and measured need
  justify it.
- The desktop shell is planned around Avalonia, but the dependency and version
  are not introduced until the headless vertical slice is stable.
- The repository pins an SDK feature band, enables nullable analysis,
  deterministic builds, built-in analyzers, and warnings as errors.

## Evidence

- Microsoft lists .NET 10 as an LTS release and documents its support lifecycle:
  <https://dotnet.microsoft.com/platform/support/policy/dotnet-core>.
- .NET publishes supported OS/RID and native interop guidance:
  <https://learn.microsoft.com/dotnet/core/rid-catalog> and
  <https://learn.microsoft.com/dotnet/standard/native-interop/>.
- The base class library provides authenticated encryption, ECDH/ECDSA,
  networking, JSON, and cross-platform process/runtime services; cryptographic
  availability is still verified on every CI OS.
- Avalonia documents Windows, macOS, and Linux targets and uses a permissive
  license: <https://docs.avaloniaui.net/docs/overview/supported-platforms> and
  <https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md>.

Links are design evidence, not a substitute for CI or hardware results.

## Alternatives considered

### Rust plus Tauri

Strong memory-safety and compact distribution are attractive, but it introduces
a second UI language/runtime for a complete desktop product and the current
host lacks Rust. Native screen/input integration would still require substantial
platform work. Rust remains an option for a narrowly justified media helper.

### TypeScript plus Electron

The local toolchain is excellent and UI delivery is fast. However, the runtime
footprint is larger and security-sensitive native capture/input paths would
depend on native add-ons in another language. It does not satisfy the
single-language preference as cleanly.

### C++ with Qt

It offers deep native integration and mature UI support, but memory-safety risk,
build complexity, and slower state-machine iteration are poor trade-offs for
the initial small team and clean-room codebase.

### Kotlin with Compose Desktop

It is viable for UI and shared logic, but native API coverage and packaging
introduce comparable bridging work with less direct access to the platform APIs
Flowspan prioritizes.

## Consequences

- Contributors need only the pinned .NET SDK for the headless core.
- Domain and protocol tests are portable and fast.
- Native APIs unavailable in .NET require carefully reviewed interop.
- Avalonia, codecs, mDNS, and persistence libraries remain explicit later
  dependencies, each with supply-chain and licensing review.
- Binary size and startup must be measured once the desktop shell exists;
  NativeAOT is an optimization option, not an architectural assumption.

## Revisit triggers

Revisit if a mandatory platform feature cannot be implemented safely through
C# interop, measured media latency requires a native component, or supported
desktop packaging proves unreliable on one of the three target systems.
