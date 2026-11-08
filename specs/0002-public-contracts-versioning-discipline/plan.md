# Implementation Plan: ISK-0002 — Public Contracts & Versioning Discipline

**Branch**: `feature/0002-public-contracts-versioning-discipline` | **Date**: 2026-11-07 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/0002-public-contracts-versioning-discipline/spec.md`

## Summary

Give `Iskender.Core.Contracts` its first public types, and add the discipline that keeps every public contract package stable. The work has four parts:

1. **Contract types**: opaque, type-prefixed resource IDs with a registry of resource types; owner scopes and attested scoped references; a closed set of 13 error categories, a registry of error codes with fixed safe messages, and a gateway error with diagnostics held apart; and a request-local `GatewayExecutionContext` with set-once values and empty, sealed views for later specs.
2. **Version tags**: an assembly attribute declaring each package's contract and version, and a tag on every public type and later member.
3. **Compatibility harness**: a new test project that compares each package's public surface, the registries, and the wire fixtures with their state at the merge base with `origin/main`, classifies every difference with the break catalog, and fails on any break the break record does not declare.
4. **Publication**: the version 1 contract in this directory, CODEOWNERS entries for the new sources, baselines, and fixtures, and links from `CONTRIBUTING.md` and `README.md`.

No package, project under `src/`, or project edge is added, so the ISK-0001 contract needs no revision. See [research.md](research.md) for the design decisions.

## Technical Context

**Language/Version**: C# 14 on .NET 10, SDK 10.0.403 per `global.json`

**Primary Dependencies**: None new (FR-044). Contract types use only the base class library: `System.Security.Cryptography.RandomNumberGenerator`, `System.Text.Json` (`Utf8JsonWriter`, `JsonDocument`), `System.Diagnostics` (`ActivityTraceId`), and `TimeProvider`. Tests use the already pinned `xunit.v3` 4.0.2, with `System.Reflection` and `NullabilityInfoContext` for the surface reader.

**Storage**: N/A. No database, table, or storage model (FR-044). Registries are C# declarations, and baselines, fixtures, and the break record are files in the repository.

**Testing**: xUnit v3 on Microsoft.Testing.Platform, under CI check "4. Test". The harness reads Git history with `git show`, which works offline once `origin/main` is fetched.

**Target Platform**: Windows and Linux development machines, and `ubuntu-24.04` in CI. Fixtures are compared byte for byte, and `.gitattributes` keeps them LF everywhere.

**Project Type**: Public .NET contract library plus test infrastructure, in a modular monolith.

**Performance Goals**: None asserted (Constitution VIII). The 1,000,000-ID uniqueness test (SC-004) must stay inside CI's existing 20-minute job limit.

**Constraints**:

- Runs offline after restore, with no credentials (FR-039, SC-001).
- Runs under an existing CI check, with no new required check (FR-039).
- Restores in `--locked-mode`, so each new test project needs a committed lock file (ISK-0001 rule B2).
- `Iskender.Core.Contracts` may reference no other `Iskender.*` project and no denied package (ISK-0001 rules A1, A19).
- This PC restores through its user-level proxy feed, which must never appear in a committed `nuget.config` or in CI.

**Scale/Scope**: 3 public contract packages, 13 resource types, 13 error categories, 22 error codes, 2 wire contracts, 32 rules. Every later spec that adds a public type, code, or resource type runs under the harness.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Gate | Status | Notes |
|---|---|---|---|
| I. Product & License | MIT; no open-core withholding | Pass | No new package or project. `check-license.sh` still runs. |
| II. Architecture & Topology | Boundaries and direction enforced; builds without cloud credentials | Pass | All types go in `Iskender.Core.Contracts` with no new edge (FR-004). The ISK-0001 suite keeps transport, provider, cloud, and storage packages out. Rule H7 adds a type-level check on the context. |
| III. Mandatory State | Durable conversation tests | N/A | Registers the `conversation` resource type only. Provider conversation IDs stay in `ProviderNativeId` (FR-013). |
| IV. Provider Correctness | Unavailable features fail explicitly | Pass | `capability_unavailable` is a category of its own. No provider mapping is added (FR-044). |
| V. Security & Privacy | Isolation tests; fail closed; telemetry redaction | Pass | IDs carry no owner and come from a secure source with no fallback (FR-006, FR-012). Operator scope never equals a tenant scope (FR-010). Trusted references need an attesting resolver (FR-011). Safe messages come only from the registry, and missing and foreign resources answer identically (TR-3). Missing harness inputs fail the run (H1). Telemetry filtering of diagnostics stays with the observability specs, which the contract records as a known limit. |
| VI. Policy & Plugins | Typed policies; shared contracts | Pass | `policy-sdk` is versioned on its own (FR-029). `PolicyExecutionView` is empty for the policy specs to fill. |
| VII. Resilience & Governance | No retry after stream | Pass | Construction rejects retry advice once output is visible (FR-025), and is tested for every category. |
| VIII. Test & Operational Rigor | Risk-proportionate tests; no invented numbers | Pass | Every break kind has a negative fixture (H8, SC-002). Diagnostics name the rule, item, and fix (FR-040). No performance figure is asserted. |
| IX. Engineering Practice | Versioned SDK and API surfaces; doc sync | Pass | This spec is the versioning discipline. The contract, CONTRIBUTING, and README change in the same PR (research R14). R5 and R6 stay unapplied under DEC-20261105-002, which the contract states. |
| X. Spec Kit Discipline | Stage and budget rules | Pass | Run by `/drive-speckit`. `.specify/` is unchanged. |
| XI. Human Governance | No silent roadmap change; real evidence | Pass | No `docs/` topic document changes beyond what FR-043 allows, and none is planned. Breaks need a recorded Architecture (ADR) owner approval. |
| XII. Enforcement | Owner and gate per principle | Pass | Every contract section names the Architecture (ADR) owner, and CODEOWNERS requests that review. |

**Note on ADR-0030**: the versioning policy relies on it, as ISK-0001 did. The constitution's binding range ends at ADR-0029, and DEC-20261106-004 approved ADR-0030 without amending that range. This plan depends only on the approved register entry, so there is no conflict to escalate.

**Post-design re-check**: Pass, with no changes. The design adds no runtime surface outside `Iskender.Core.Contracts`, no package, and no exception. Two choices are judgments a reviewer should confirm, and both are recorded in research: the module and category given to each `docs/08` reason code (R10), and the protected-resolver route for scoped references with its known limit (R7).

## Project Structure

### Documentation (this feature)

```text
specs/0002-public-contracts-versioning-discipline/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── public-contracts-and-versioning.md   # Version 1
├── checklists/
│   └── requirements.md
└── tasks.md                                 # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Iskender.Core.Contracts/
│   ├── Properties/ContractPackage.cs     # [assembly: ContractPackage("core-contracts", 1)]
│   ├── Versioning/                       # ContractPackageAttribute, ContractVersionAttribute, WireContract(s)
│   ├── Identifiers/                      # ResourceType(s), ResourceId, OwnerScope, ScopedResourceReference,
│   │                                     #   OwnershipResolver, ProviderNativeId, CrockfordBase32 (internal)
│   ├── Errors/                           # ErrorCategory, ErrorCode(s), GatewayError, GatewayException,
│   │                                     #   RetryAdvice, ProviderErrorMetadata, ErrorDiagnostics, ErrorEnvelopeCodec
│   └── Context/                          # GatewayExecutionContext, CorrelationContext, RequestCorrelationId,
│                                         #   ConfigurationRevision, the four views, ExtensionContextView
├── Iskender.Providers.Abstractions/
│   └── Properties/ContractPackage.cs     # [assembly: ContractPackage("providers-abstractions", 1)]
└── Iskender.Policy.Sdk/
    └── Properties/ContractPackage.cs     # [assembly: ContractPackage("policy-sdk", 1)]

tests/
├── Iskender.Contracts.Compatibility.Tests/     # New; the harness
│   ├── Surface/                          # Reflection reader, canonical lines, classifier (S, V rules)
│   ├── Wire/                             # Fixture checks (W rules)
│   ├── Registries/                       # Snapshot and classifier (G rules)
│   ├── Harness/                          # Git base reader, break record, rule catalog, violation format,
│   │                                     #   H rules, run orchestration
│   ├── baselines/                        # core-contracts, providers-abstractions, policy-sdk surfaces;
│   │                                     #   registries.json
│   ├── fixtures/                         # wire/<contract>/v1/, breaks/<rule>/, compatible/
│   ├── break-record.json                 # Empty entries list
│   └── *Tests.cs                         # Real-repository run, catalog meta-test, one test per rule
├── Iskender.Contracts.Compatibility.Fixtures/  # New; compiled Kinds/<rule>/Before and After pairs (S, V rules)
└── Iskender.Core.Contracts.Tests/
    ├── ResourceIdTests.cs, OwnerScopeTests.cs, GatewayErrorTests.cs,
    │   ErrorEnvelopeCodecTests.cs, GatewayExecutionContextTests.cs      # New
    └── SolutionBuildsTests.cs            # Source-file list allows Core.Contracts (research R15)

Iskender.sln                              # + 2 test projects
.github/CODEOWNERS                        # + the two new test projects (FR-003)
CONTRIBUTING.md, README.md                # Link to the version 1 contract (FR-043, research R14)
```

**Structure Decision**: Extend the existing solution in place, with all contract types in `Iskender.Core.Contracts` under the namespaces the data model lists. The harness is a separate test project so that it is its own rule family with its own contract, beside ISK-0001's architecture suite rather than inside it (research R13). The two other public contract packages get only their assembly attribute, and start with empty baselines.

## Implementation approach

The order matters, because each step keeps the build and tests passing for the next commit.

1. **Versioning attributes**: add `ContractPackageAttribute`, `ContractVersionAttribute`, and the assembly attribute in all three packages. Nothing else changes yet, so the ISK-0001 suite proves the edges still hold.
2. **Identifiers**: the base32 codec, resource type registry, `ResourceId`, `OwnerScope`, `ScopedResourceReference`, `OwnershipResolver`, and `ProviderNativeId`, with their tests. Update `SkeletonContainsNoGatewayBusinessCode` in the same commit as the first source file (research R15). Parse failures need codes, so this step adds `ErrorCategory`, `ErrorCode`, `ErrorCodes`, and `GatewayException` first, with only the codes it uses.
3. **Errors**: the rest of the error codes, `GatewayError` and its parts, and the envelope codec, with tests for SC-005 and SC-006.
4. **Request context**: the context, correlation, revision, and views, with tests for User Story 4.
5. **Harness core, on fixtures**: the rule catalog, violation format, surface reader, classifiers, break record, and base reader, proved first on the fixtures project and the break and compatible fixtures. The H8 meta-test comes with them.
6. **Harness on the repository**: generate and commit the three surface baselines, the registry baseline, and the wire fixtures, then turn on the real run. On this PR the base has no baselines, so every item is an addition at version 1.
7. **Publication**: turn on H4 against this contract, add the CODEOWNERS entries, the solution entries, and the `CONTRIBUTING.md` and `README.md` links.

## Requirement coverage

| Requirements | Where they land |
|---|---|
| FR-001, FR-002 | `contracts/public-contracts-and-versioning.md`; the registries and break record; rules H4, H5 |
| FR-003 | `.github/CODEOWNERS`; the contract's header |
| FR-004 | All types in `Iskender.Core.Contracts`; no new edge, checked by the ISK-0001 suite |
| FR-005 to FR-009, FR-012 | `Identifiers/`; `ResourceIdTests` |
| FR-010, FR-011 | `OwnerScope`, `ScopedResourceReference`, `OwnershipResolver`; rule H6; `OwnerScopeTests` |
| FR-013 | `ProviderNativeId` |
| FR-014 to FR-019 | `Context/`; rule H7; `GatewayExecutionContextTests` |
| FR-020 to FR-026 | `Errors/`; `GatewayErrorTests`, `ErrorEnvelopeCodecTests` |
| FR-027 to FR-029 | Versioning attributes; rules V1 to V3 |
| FR-030 to FR-032 | The contract's Versioning policy and Break catalog; rule V4 |
| FR-033 to FR-038 | The harness; rules S, W, G, H1 to H3, H8 |
| FR-039 | CI check 4, unchanged |
| FR-040 to FR-042 | The violation format; registered codes on every contract-type failure |
| FR-043 | The contract, and links in `CONTRIBUTING.md` and `README.md` |
| FR-044 | Review; quickstart scenario 10; the ISK-0001 suite |

## Risks

| Risk | Mitigation |
|---|---|
| Reflection renders a type differently across runtimes or platforms, so the baseline flaps | Canonical names come from metadata names, not `ToString()`. Lines are sorted ordinally. CI on Linux and local runs on macOS or Windows must produce the same file, which the harness's own tests check on a fixture |
| `origin/main` is missing or stale locally | The run fails with H1 and says to fetch. CI's checkout uses `fetch-depth: 0` |
| The 1,000,000-ID test is slow | It generates IDs in memory with no I/O, and the ordering check needs one pass. If it nears the CI limit, the sample is not reduced below SC-004's figure; the blocker is reported instead |
| A later spec cannot extend a view without a break | Views are sealed classes, to which adding members is compatible (research R8). AV-1 is validated by the specs that fill them |
| The surface reader misses a kind of change | Each S rule has a compiled before-and-after fixture, so a gap shows as a failing H8 or rule test |

## Complexity Tracking

No constitution violations to justify.
