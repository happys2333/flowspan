# Clean-room Engineering and Provenance

Flowspan is an original implementation. Public concepts from remote desktop,
distributed systems, and projects such as Deskflow may inform requirements and
architecture, but GPL implementation code must not be copied, translated, or
used as a line-by-line template.

## Rules

1. Prefer standards, platform documentation, papers, and independently written
   tests as primary design sources.
2. Record non-obvious external conceptual influences in the relevant ADR.
3. Do not paste upstream source into prompts, issues, tests, comments, or this
   repository for implementation assistance.
4. Do not preserve upstream names, message layouts, constants, comments, test
   vectors, or control flow unless they are part of a public standard.
5. Dependencies must be consumed under compatible licenses and recorded by the
   release SBOM/license report.
6. If provenance of a contribution is uncertain, quarantine it and rewrite from
   the documented requirement and public standard.

## Conceptual influences allowed by the product baseline

- explicit connection/ownership state machines;
- platform abstraction boundaries;
- device topology concepts;
- monotonically sequenced ownership/authority;
- reconnect behavior and version negotiation.

Flowspan's Activity model, transaction protocol, capability system, descriptors,
diagnostics, UI, and code are designed and implemented independently from the
requirements in this repository.

## Contribution attestation

Pull requests should state that contributions are original or identify their
licensed source. Generated code and fixtures need the same review as handwritten
code. A future `CONTRIBUTING.md` will include this attestation once external
contribution workflow is enabled.
