# Desktop Quality and String Externalization Design

Status: accepted for implementation

Requirements: DQ1-DQ5

## 1. Architecture

The localization boundary remains inside the outer `Flowspan.Desktop` project.
Lower layers continue to return typed domain, protocol, and diagnostic values;
they do not reference desktop resources or Avalonia.

Neutral-English `.resx` catalogs are embedded in `Flowspan.Desktop`. A small
`DesktopText` facade owns `ResourceManager` lookup, neutral fallback,
`CurrentCulture` formatting, catalog enumeration for verification, and startup
projection into Avalonia application resources.

Static XAML presentation uses Avalonia dynamic-resource references. Inline
`Binding.StringFormat` templates use application `StaticResource` references
because a Binding is not an Avalonia styled property and v1 has no live culture
switch. View-model presentation uses `DesktopText.Get` for complete values and
`DesktopText.Format` for templates. This keeps user data out of resource keys
and gives tests one public resource contract without adding a localization
framework or another runtime dependency.

## 2. Catalog structure

Keys use a stable `<Surface>_<Meaning>` convention, for example
`MainWindow_EmergencyStop_Name` or `RemoteWindow_FallbackReady_Description`.
Related values may share text but retain separate keys when their semantic or
accessibility context can diverge later.

The neutral catalog is the source of v1 English. Culture-specific satellite
catalogs are intentionally absent. `ResourceManager` fallback therefore keeps
unsupported UI cultures usable while preserving a standard future translation
path.

Machine contracts remain outside the catalog when translation would break an
API or evidence format. Examples include `mirror.view`,
`native_adapters_unavailable`, protocol `1.5`, JSON member names, and file
extensions. A displayed sentence or label that contains one of these values is
still a resource template.

## 3. Avalonia composition

`App.Initialize` loads application XAML and then projects the resolved catalog
into `Application.Resources`. `MainWindow.axaml` consumes only named resource
references for literal presentation, accessibility text, and binding format
templates. Bound view-model properties remain normal compiled bindings.

The projection happens before `MainWindow` construction. v1 does not support a
live culture switch, so resources are resolved once per application lifetime.
Tests may create a fresh app lifetime under another UI culture to verify neutral
fallback without mutating a live window.

## 4. View-model composition

Formatting is intentionally narrow:

- `Get(key)` returns a complete neutral/localized value.
- `Format(key, args)` resolves the template with `CurrentUICulture` and formats
  arguments with `CurrentCulture`.
- identifiers and diagnostic timestamps are converted explicitly with invariant
  formats before being passed as arguments when their representation is part of
  an existing contract.

The migration changes presentation construction only. Command state, service
calls, authorization, state reduction, redaction, and exception sanitization
must not move into the resource layer.

## 5. Verification strategy

The work proceeds as vertical TDD slices:

1. A resource contract test fails, then the neutral catalog and facade make it
   pass.
2. An XAML literal gate fails, then each static value moves to a resource.
3. A presentation-source gate fails for one view-model surface, then that
   surface migrates before the next surface is admitted.
4. Existing public-behavior tests continue asserting rendered English output;
   targeted culture tests prove formatting and neutral fallback.
5. Headless quality tests retain keyboard, automation, scaling, contrast, and
   no-motion coverage.

The source gate parses XML structurally for XAML and classifies C# string tokens
conservatively. Its machine-contract allowlist is explicit and narrow. Resource
references and catalog values are checked in both directions so dead or missing
entries do not accumulate silently.

## 6. Evidence limits

Automated evidence can close the repository externalization work and portable
headless checks. It cannot close native screen-reader, high-contrast, focus-ring,
font fallback, text-scaling, or operating-system reduced-motion checks. Those
remain in the real-machine acceptance matrix and release criteria until recorded
on packaged Windows, macOS, and Linux builds.

## 7. Risks and mitigations

- **A resource migration changes wording.** Existing exact-text behavior tests
  and the bidirectional catalog/reference gate keep neutral English reviewable
  and reject dead or missing entries.
- **A reason code is accidentally localized.** Machine contracts are documented,
  explicitly classified, and covered by existing protocol/diagnostic tests.
- **A missing key becomes invisible text.** Lookup fails fast in verification,
  and catalog completeness tests reject missing or blank values.
- **Formatting becomes locale-dependent where it must be stable.** IDs, wire
  values, hashes, and diagnostic timestamps keep explicit invariant conversion.
- **Headless checks are overstated.** Test names, evidence, and release criteria
  retain the DQ5 boundary.
