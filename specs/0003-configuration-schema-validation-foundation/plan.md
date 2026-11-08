# Implementation Plan: ISK-0003 — Configuration Schema & Validation Foundation

**Branch**: `feature/0003-configuration-schema-validation-foundation` | **Date**: 2026-11-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/0003-configuration-schema-validation-foundation/spec.md`

## Summary

Give Iskender one way to declare, load, merge, and validate configuration, and to name a validated configuration with an immutable revision. The work has five parts:

1. **Public declaration and report types** in `Iskender.Core.Contracts.Configuration`: section and field schemas, `SecretReference`, field errors, the validation report, and read-only snapshot and section views. `core-contracts` goes from version 1 to 2, and 17 `config.*` error codes join `ErrorCodes`.
2. **The engine** in `Iskender.Domain.Registry`: the schema registry, bounded JSON and YAML readers that build one canonical tree with locations, the overlay merge with environment variables, the closed validator that collects every field error, and the snapshot with its content-derived `cfg-` revision.
3. **Diagnostics**: a fixed text rendering and three structured log events, none of which ever holds a configuration value.
4. **Tests and fixtures** in a new `tests/Iskender.Domain.Tests` project: a negative fixture per error code and edge case, a canary check, revision determinism, and overlay precedence.
5. **Publication**: the version 1 configuration contract, links from `docs/03` and `docs/04`, the ISK-0002 contract's code and version tables, and a license scan that now reads package licenses.

The engine takes two MIT packages, `YamlDotNet` and `Microsoft.Extensions.Logging.Abstractions`. No project, edge, or role is added, so the ISK-0001 contract needs no revision. See [research.md](research.md) for the design decisions.

## Technical Context

**Language/Version**: C# 14 on .NET 10, SDK 10.0.403 per `global.json`

**Primary Dependencies**:

- `Iskender.Core.Contracts`: none new. Base class library only.
- `Iskender.Domain`: `YamlDotNet` 18.1.0 (MIT, no dependencies), used only through its event parser (research R1), and `Microsoft.Extensions.Logging.Abstractions` 10.0.14 (MIT), which brings `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.14 (MIT) (research R12). JSON, hashing, and UTF-8 checks use `System.Text.Json`, `System.Security.Cryptography.SHA256`, and `System.Text.Unicode.Utf8`.
- Tests: the already pinned `xunit.v3` 4.0.2.

**Storage**: N/A. No database, table, or storage model (FR-038). Fixtures are files in the repository.

**Testing**: xUnit v3 on Microsoft.Testing.Platform, under CI check "4. Test". The compatibility harness and the architecture suite run unchanged and must pass.

**Target Platform**: Windows, macOS, and Linux development machines, and `ubuntu-24.04` in CI. Fixtures are LF everywhere through `.gitattributes`, so line and column expectations hold on every platform.

**Project Type**: Public .NET contract types plus an embeddable domain library, in a modular monolith.

**Performance Goals**: None asserted (Constitution VIII). TR-12's limits are safety bounds. SC-004's allocation bound is a test of boundedness, not a performance figure.

**Constraints**:

- Runs offline after restore, with no credentials (FR-023, SC-001).
- Restores in `--locked-mode`, so the new test project and the changed `Iskender.Domain` need committed lock files (ISK-0001 rule B2), with versions in `Directory.Packages.props` (rule B1).
- `Iskender.Domain` may take no denied package (rule A19), and every type must sit in `Iskender.Domain.Registry` (rules A17, A18).
- No ASP.NET Core, dependency injection, file system, or transport type on the public surface of `Iskender.Core.Contracts.Configuration` (FR-004).
- No configuration value in any error, rendering, log event, exception message, or diagnostic (FR-020).
- This PC restores through its user-level proxy feed, which must never appear in a committed `nuget.config` or in CI.

**Scale/Scope**: 10 field types, 17 error codes, 4 layers, 3 log events, 5 limits, and 1 canonical form. Every later module spec declares its section on this foundation.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Gate | Status | Notes |
|---|---|---|---|
| I. Product & License | MIT; no open-core withholding | Pass | Both new packages are MIT. `check-license.sh` is extended to read package licenses, as it says to when a production project first takes a package (research R17). The Product/Charter owner reviews the allow-list |
| II. Architecture & Topology | Boundaries and direction enforced; builds without cloud credentials | Pass | Public types in `Iskender.Core.Contracts`, engine in `Iskender.Domain.Registry`, with no new project or edge (FR-003). Neither package is in a denied category. No host configuration is taken over (ADR-0004) |
| III. Mandatory State | Durable conversation tests | N/A | No conversation code |
| IV. Provider Correctness | Unavailable features fail explicitly | N/A | No provider code. Provider adapters will declare sections through `Iskender.Core.Contracts` |
| V. Security & Privacy | Isolation tests; fail closed; telemetry redaction | Pass | Closed validation (TR-2), production-grade by default (TR-6), plaintext secrets rejected, YAML constructs and limits enforced, values never echoed and checked by a canary test (SC-003), sections isolated by schema instance. Configuration is operator scope and impersonates no tenant |
| VI. Policy & Plugins | Typed policies; JSON/YAML and fluent configuration compile to one model | Pass | One canonical tree for JSON and YAML. No CEL compiler, policy schema, plan, or activation (FR-038). CEL text in a section is an opaque string |
| VII. Resilience & Governance | No retry after stream; fail closed on hard budget | N/A | No request path |
| VIII. Test & Operational Rigor | Risk-proportionate tests; no invented numbers | Pass | A negative fixture per code and edge case, determinism and sensitivity tests, a bounded-allocation test, operable diagnostics with an error-to-resolution text form. No performance figure is asserted |
| IX. Engineering Practice | Versioned surfaces; doc sync; supply-chain hygiene | Pass | `core-contracts` 1 to 2 with version tags (FR-033). The contract and `docs/03` and `docs/04` links change in the same PR. Packages are centrally pinned and lock-file hashed. R5 and R6 stay unapplied under DEC-20261105-002 |
| X. Spec Kit Discipline | Stage and budget rules | Pass | Run by `/drive-speckit`. `.specify/` is unchanged |
| XI. Human Governance | No silent roadmap change; real evidence | Pass | No roadmap, ADR, or constitution change. The ISK-0002 contract changes only in the two tables FR-032 and FR-033 name |
| XII. Enforcement | Owner and gate per principle | Pass | Each contract section names its review owner |

**Post-design re-check**: Pass, with no changes. Four choices are judgments a reviewer should confirm, and each is recorded in research:

- The snapshot and section views as abstract classes with protected constructors, with the known limit that in-process code can derive one (R10).
- Setting a secret field from an environment variable only through a `__SECRETREF` segment (R6).
- Including schema versions in the canonical form, so a schema change alone changes the revision (R9).
- Extending the license scan with a permissive-license allow-list under `.github/` (R17).

## Project Structure

### Documentation (this feature)

```text
specs/0003-configuration-schema-validation-foundation/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── configuration.md          # Version 1
├── checklists/
│   └── requirements.md
└── tasks.md                      # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Iskender.Core.Contracts/
│   ├── Properties/ContractPackage.cs     # core-contracts 1 → 2
│   ├── Context/ConfigurationRevision.cs  # XML comment only (FR-030)
│   ├── Errors/ErrorCodes.cs              # + 17 config.* codes, since 2
│   └── Configuration/                    # New: SectionSchema, FieldSchema, FieldConstraints,
│                                         #   CharacterClassPattern, ConfigurationFieldKind, SecretReference,
│                                         #   ConfigurationLayer, ConfigurationSourceLocation,
│                                         #   ConfigurationFieldError, ConfigurationValidationReport,
│                                         #   ConfigurationSnapshot, ConfigurationSection, SectionVersion,
│                                         #   ConfigurationLayers
└── Iskender.Domain/
    ├── Iskender.Domain.csproj            # + YamlDotNet, Microsoft.Extensions.Logging.Abstractions
    ├── packages.lock.json
    └── Registry/                         # New, namespace Iskender.Domain.Registry
        ├── ConfigurationSchemaRegistry.cs, ConfigurationValidator.cs, ConfigurationLoadRequest.cs
        ├── Sources/                      # ConfigurationDocumentSource, ConfigurationEnvironmentSource
        ├── Reading/                      # CanonicalNode, JsonDocumentReader, YamlDocumentReader,
        │                                 #   EnvironmentVariableReader, ConfigurationLimits
        ├── Validation/                   # OverlayMerger, SchemaValidator, ErrorTextTemplates, NameSuggester
        ├── Snapshots/                    # CanonicalWriter, Base32Lower, ValidatedSnapshot, ValidatedSection
        └── Diagnostics/ConfigurationLog.cs

tests/
├── Iskender.Domain.Tests/                # New
│   └── Configuration/
│       ├── Fixtures/                     # valid/, invalid/<code>/, edge/, equivalent/<pair>/, overlay/,
│       │                                 #   each negative fixture with a .expect.json sidecar
│       ├── TestSchemas.cs                # alpha, beta, and a full-coverage schema
│       └── *Tests.cs                     # registry, readers, merge, validator, secrets, revision,
│                                         #   rendering, diagnostics, canary, derivation scan
├── Iskender.Core.Contracts.Tests/
│   ├── Configuration/*Tests.cs           # New: public type rules
│   └── SolutionBuildsTests.cs            # Allows src/Iskender.Domain/Registry/ (research R16)
└── Iskender.Contracts.Compatibility.Tests/
    └── baselines/                        # core-contracts.surface.txt, registries.json regenerated

Directory.Packages.props                  # + 2 package versions (and the transitive pin if needed)
Iskender.sln                              # + Iskender.Domain.Tests
.gitattributes                            # Fixtures kept LF, if not already covered
.github/CODEOWNERS                        # + tests/Iskender.Domain.Tests/
.github/scripts/check-license.sh          # Reads package licenses (research R17)
docs/03-domain-and-technical-architecture.md, docs/04-technology-stack-and-deployment.md   # Contract links (FR-002)
specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md
                                          # Error codes table + versioning table only (FR-032, FR-033)
```

**Structure Decision**: Extend the existing solution in place. Public types go in a new `Configuration` folder and namespace of `Iskender.Core.Contracts`, and the engine goes under `Iskender.Domain/Registry/`, which the module map already gives to Registry/Configuration. Subfolders of `Registry/` share the single namespace `Iskender.Domain.Registry`, so rule A18 sees one module. A new test project is needed because nothing yet may reference `Iskender.Domain` for tests (research R16).

## Implementation approach

The order matters, because each step keeps the build and every test passing for the next commit.

1. **License scan first**: extend `check-license.sh` to read package licenses from restored `.nuspec` files, and prove it against the current tree, which has no packages. Then the package step cannot fail check 1 for want of a scan.
2. **Error codes and version**: add the 17 codes with `since: 2`, raise `core-contracts` to 2, update the `ConfigurationRevision` comment, and update the ISK-0002 contract's two tables and the baselines in the same commit, so the harness passes.
3. **Public types**: add `Iskender.Core.Contracts.Configuration`, with tags, tests, and regenerated baselines.
4. **Test project and packages**: add `tests/Iskender.Domain.Tests`, its solution and CODEOWNERS entries, the two packages in `Directory.Packages.props`, and the lock files. Update `SkeletonContainsNoGatewayBusinessCode` with the first `Registry/` source file.
5. **Registry**: `ConfigurationSchemaRegistry` and every `config.schema.*` check.
6. **Readers**: the bounded buffer, UTF-8 and control-character checks, the JSON reader, the YAML reader with every banned construct, and the limits, each with negative fixtures.
7. **Environment variables and merge**: prefix, mapping, `__SECRETREF`, conflicts, precedence, `null` handling, and the layer named on unknown fields.
8. **Validator**: every FR-021 check, error collection and ordering, suggestions, truncation, and the text rendering.
9. **Snapshot and revision**: the canonical writer, base32 encoder with RFC 4648 vectors, the snapshot and section implementations, isolation by schema instance, and determinism and sensitivity tests.
10. **Diagnostics and canary**: the three events, the capturing logger, the canary across every negative fixture, the alias bomb allocation test, and the derivation scan.
11. **Publication**: the `docs/03` and `docs/04` links, and a final check that every quickstart scenario passes.

## Requirement coverage

| Requirements | Where they land |
|---|---|
| FR-001, FR-002 | `contracts/configuration.md`; links in `docs/03` and `docs/04` |
| FR-003, FR-004 | `Iskender.Core.Contracts.Configuration` and `Iskender.Domain.Registry`; the ISK-0001 suite; review of the public surface baseline |
| FR-005 to FR-009 | `SectionSchema`, `FieldSchema`, `ConfigurationSchemaRegistry`, `ConfigurationSection`; registry and isolation tests |
| FR-010 to FR-013 | `Reading/`; reader tests and document fixtures |
| FR-014 to FR-017 | `EnvironmentVariableReader`, `OverlayMerger`; overlay fixtures |
| FR-018 to FR-024 | `SchemaValidator`, `ErrorTextTemplates`, `NameSuggester`, `ConfigurationValidationReport`; field fixtures, rendering tests |
| FR-025, FR-026 | `SecretReference`; secret fixtures; no resolver anywhere |
| FR-027 to FR-030 | `CanonicalWriter`, `Base32Lower`, `ValidatedSnapshot`; determinism tests; the `ConfigurationRevision` comment |
| FR-031, FR-032 | `ErrorCodes`; the ISK-0002 contract's error codes table; rule H4 |
| FR-033, FR-034 | Assembly attribute, version tags, baselines, the ISK-0002 versioning table; the harness |
| FR-035, FR-036 | `tests/Iskender.Domain.Tests/Configuration/` |
| FR-037 | `ConfigurationLog`; diagnostics tests |
| FR-038, FR-039 | Review; quickstart section 4 |

## Risks

| Risk | Mitigation |
|---|---|
| The YAML parser's fixed buffers make an alias bomb exceed SC-004's 1 MiB ceiling, or allocation differs between the two bombs by more than 64 KiB | Measure after a warm-up read. The fixtures are real bombs of honest size. If either check fails, implement reports a blocker rather than padding the fixtures or changing the criterion |
| YamlDotNet reports a construct differently than research R1 expects, such as an implicit tag on a plain scalar | Each banned construct has a fixture, and each allowed scalar style has one, so a mismatch shows as a failing test before the reader is relied on |
| Line and column differ between platforms | Fixtures are LF through `.gitattributes`, and columns count Unicode scalar values from a byte offset table, not from the parser's text |
| A canonical form detail, such as number formatting, varies by culture | The writer formats with the invariant culture and `Utf8JsonWriter`, and the determinism tests run under a non-English culture as well |
| `Microsoft.Extensions.Logging.Abstractions` ships an analyzer and source generator that the ISK-0001 rules reject | It arrives through NuGet's generated props, which rule B4 allows, and `[LoggerMessage]` is not used (research R12). The architecture suite runs in step 4 to confirm |
| A later spec needs another member on the abstract views | Members are added as virtual with a default, which the harness classifies as compatible, and the contract says so |
| The license allow-list is too narrow for a later package | That spec extends the list under Product/Charter owner review |

## Complexity Tracking

No constitution violations to justify.
