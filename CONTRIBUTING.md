# Contributing to Flowspan

Flowspan is early and security-sensitive. Changes should be small, traceable to
`specs/v1/requirements.md`, and accompanied by evidence appropriate to their
risk.

## Before changing code

1. Find or add the task in `specs/v1/tasks.md` and link its requirement.
2. Add or update an ADR for a durable architecture, protocol, dependency,
   cryptographic, persistence, or platform decision.
3. Update the threat model when data exposure, trust, native capture/input, or
   authorization changes.

## Required local checks

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
```

Do not report Windows, Linux, or native API success based only on a macOS run or
a fake adapter. Label simulator, CI-runner, and real-machine results separately.

## Originality and provenance

By contributing, state that the change is your original work or identify every
licensed source. Do not copy or translate Deskflow GPL implementation code.
Public distributed-systems concepts and platform documentation may inform an
independent implementation; record non-obvious influences in the relevant ADR.
See `docs/engineering/clean-room.md`.

## Security

- Deny capabilities by default and test the negative path.
- Never log Activity payloads, raw input, credentials, private keys, or sensitive
  filenames.
- Use deterministic clocks/randomness in model tests; do not wait on wall time.
- Treat parsers and native adapters as hostile-input boundaries.
- Do not introduce a production dependency without license, vulnerability, and
  maintenance review.
