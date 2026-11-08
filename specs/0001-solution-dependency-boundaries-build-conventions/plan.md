# Implementation Plan: ISK-0001 — Solution Dependency Boundaries & Build Conventions

**Branch**: `feature/0001-solution-dependency-boundaries-build-conventions` | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/0001-solution-dependency-boundaries-build-conventions/spec.md`

## Summary

Refine the ISK-0000 project graph and harden the rules that protect it, with no gateway behavior added. The work has four parts:

1. **Graph**: Add the empty `Iskender.Hosting` library and the `Iskender.ControlPlane.Host` shell. Make `Iskender.Gateway.Host` the data-plane host by removing its `Management` reference. Add the `Domain` and `Management` edges that `docs/03` implies.
2. **Rule source**: Move the rules, the allow-list, and the module map out of test code into one JSON file. A hand-written version 2 contract is checked against it on every run.
3. **Enforcement**: Extend `tests/Iskender.Architecture.Tests` to scan every project file by reading its XML, so that every conditional branch counts, and to run the module-namespace rules on a compiled fixtures project. Every rule gets a negative fixture, and a meta-test keeps that true.
4. **Conventions**: Raise the analyzer baseline, turn on transitive pinning, make the embeddable projects packable, and update CI, the documentation gate, CODEOWNERS, and the docs that name the old graph.

A spike against the repository confirmed that the baseline needs one code fix, that transitive pinning changes no lock file, and how NetArchTest matches namespaces. See [research.md](research.md).

## Technical Context

**Language/Version**: C# 14 on .NET 10, SDK 10.0.403 per `global.json`

**Primary Dependencies**: None new (FR-024). Tests use the already pinned `NetArchTest.Rules` 1.3.2 and `xunit.v3` 4.0.2, plus `System.Text.Json`, `System.Xml.Linq`, and `System.IO.Compression` from the base class library.

**Storage**: N/A. No database, table, or stored runtime data (FR-036). The rule source is a file in the repository.

**Testing**: xUnit v3 on Microsoft.Testing.Platform, with NetArchTest for type-level rules, under the existing CI check 5.

**Target Platform**: Windows and Linux development machines, and `ubuntu-24.04` in CI. Results must match across them.

**Project Type**: Build and test infrastructure for a .NET modular monolith. Two empty projects are added.

**Performance Goals**: None asserted for the product. The architecture suite must stay inside CI's existing 20-minute job limit. Pack took about 1.3 seconds in the spike.

**Constraints**:

- Runs offline after restore, with no credentials (SC-009).
- Runs under the existing CI check, with no new check (FR-033).
- Restores in `--locked-mode`, so every new project needs a committed lock file.
- This PC must restore through its user-level proxy feed. That feed must never appear in a committed `nuget.config` or in CI.

**Scale/Scope**: 9 production projects and 3 test projects, 12 modules, 34 rules. Later specs inherit the rules across the 125-spec roadmap.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Gate | Status | Notes |
|---|---|---|---|
| I. Product & License | MIT; no open-core withholding | Pass | New packages inherit `PackageLicenseExpression` from `Directory.Build.props`. `check-license.sh` still runs. |
| II. Architecture & Topology | Architecture tests enforce boundaries and direction; planes deploy independently; builds without cloud credentials | Pass | This spec is the enforcement. A6 and A7 keep the planes apart in the graph. Actual deployment stays with ISK-0007. |
| III. Mandatory State | Durable conversation tests | N/A | Names a home only (FR-036). |
| IV. Provider Correctness | Conformance suite | N/A | No adapters. The pattern rules only limit what a later adapter may reference. |
| V. Security & Privacy | Isolation and fail-closed tests | Pass | No runtime surface is added. The rules protect ownership boundaries (A3, A5, A16). Missing inputs fail the run instead of passing with nothing checked (C2). |
| VI. Policy & Plugins | Typed policies; no sandbox claim | Pass | `Domain → Policy.Sdk` matches `docs/03`. `Policy.Sdk` still reaches nothing but `Core.Contracts`. |
| VII. Resilience & Governance | Chaos and budget tests | N/A | Names a home only. |
| VIII. Test & Operational Rigor | Risk-proportionate tests; clean build; no invented numbers | Pass | Architecture tests fit the risk. Every rule has a fixture (SC-003). No performance figure is claimed beyond the spike's observed timing, which is labelled as such. |
| IX. Engineering Practice | Protected `main`; doc sync; SBOM and provenance | Pass | Docs move in the same PR. The new executable gets the same attestation (FR-028). R5 and R6 stay unapplied under logged exception DEC-20261105-002. |
| X. Spec Kit Discipline | Stage and budget rules | Pass | Run by `/drive-speckit`. `.specify/` is unchanged. |
| XI. Human Governance | No silent roadmap change; real evidence | Pass | Scope comes from RC-20261106-004 (DEC-20261106-003). The `docs/03` and README edits are non-material under FR-034. Spike results are recorded as observed. |
| XII. Enforcement | Owner and gate per principle | Pass | C3 ties the rule source to the Architecture (ADR) owner. |

**Note on ADR-0030**: FR-027 relies on it. The constitution's binding range ends at ADR-0029, and DEC-20261106-004 approved ADR-0030 without an amendment to that range. This spec does not depend on it being constitutional, only on the approved register entry, so there is no conflict to escalate.

**Post-design re-check**: Pass, with no changes. The design adds no runtime surface and no package, and it needs no exception.

## Project Structure

### Documentation (this feature)

```text
specs/0001-solution-dependency-boundaries-build-conventions/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── module-dependency-rules.md   # Version 2; supersedes the ISK-0000 baseline
└── tasks.md                         # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Iskender.Core.Contracts/           # Unchanged
├── Iskender.Providers.Abstractions/   # Unchanged
├── Iskender.Policy.Sdk/               # Unchanged
├── Iskender.Domain/                   # + Providers.Abstractions, Policy.Sdk; packable
├── Iskender.Infrastructure/           # Packable
├── Iskender.Management/               # + Domain; packable
├── Iskender.Hosting/                  # New; empty; packable; references Domain, Core.Contracts
├── Iskender.Gateway.Host/             # + Hosting; - Management
└── Iskender.ControlPlane.Host/        # New; executable shell; Program.cs only

tests/
├── Iskender.Architecture.Tests/
│   ├── module-dependency-rules.json   # New; the single rule source (FR-002)
│   ├── RuleSource.cs                  # New; loads and validates the source
│   ├── Violation.cs                   # New; message format and stable ordering
│   ├── RepositoryScan.cs              # New; finds and parses project files and imports
│   ├── ProjectGraph.cs                # Changed; allow-list moves to the source; adds reach
│   ├── Rules/                         # New; one class per family: Graph, Boundary, Module,
│   │                                  #   BuildConvention, Packaging, ModuleMap, ContractDocument
│   ├── Support/                       # New; ProvesRuleAttribute, MarkdownTable, RepositoryFixture
│   ├── DependencyDirectionTests.cs    # Changed per FR-030
│   └── *Tests.cs                      # New; one file per rule family, plus the meta-test
├── Iskender.Architecture.Fixtures/    # New; test role; compiled type-level violations
└── Iskender.Core.Contracts.Tests/     # SolutionBuildsTests.cs changed per FR-030

Directory.Build.props                  # + AnalysisLevel, EnforceCodeStyleInBuild
Directory.Packages.props               # + transitive pinning, overrides disabled
.editorconfig                          # No-op CA2007 suppression deleted (research R14)
Iskender.sln                           # + 3 projects
.github/CODEOWNERS                     # + rule source, tests, .editorconfig, root targets, 5 src projects
.github/scripts/docs-gate.sh           # + 2 behavior-bearing paths
.github/workflows/ci.yml               # + new executable in attest and checksum steps
.claude/agents/reviewers/architecture-guard.md  # Points at the version 2 contract
CONTRIBUTING.md, README.md, docs/03-domain-and-technical-architecture.md  # FR-028, FR-034
```

**Structure Decision**: Extend the existing solution in place. The rule source sits next to the code that consumes it, under a path that later contract revisions can change without editing this spec's record (research R1). No code goes in `src/` beyond the one host shell, as SC-008 requires.

## Implementation approach

The order below matters, because each step keeps the build and tests passing for the next commit.

1. **Rule engine, on the old graph**: Add the source, its loader, the violation format, and the scanner, with the source first describing the graph as it stands. This proves the scanner and its fixtures before the graph moves.
2. **Refine the graph**: Add the new projects and edges, regenerate lock files with a forced restore, and update the source. Change the three FR-030 tests in the same commit as the edges they encode. Each must end at least as strict, as follows:
   - `HostComposesEverySkeletonProject` becomes a check of A12 for both hosts, which also asserts A6 and A7 on the real graph.
   - The `Management → Domain` case in `A5UnapprovedEdgeFails` becomes `Management → Infrastructure`, since that edge is the one this rule now has to stop.
   - `SkeletonContainsNoGatewayBusinessCode` lists both `Program.cs` files, and asserts that neither maps an endpoint.
3. **Map and boundary rules**: Map (M1, M2), boundary (A13 to A16), and module (A17, A18). Each comes with its negative fixtures.
4. **Raise the baseline, then the rules that need it**: Set the analyzer and pinning properties, fix `CA1861` in `SolutionBuildsTests.cs` and anything the new baseline finds in the code from earlier steps, and delete the no-op `CA2007` suppression. Then add build conventions (B1 to B6) and A19 with their fixtures. B3 fails on the real repository until the baseline is set, so it cannot come first.
5. **Packaging**: P1 to P3, which make the embeddable projects packable.
6. **Repository wiring**: CODEOWNERS with C3, the documentation gate and CI with C4, and the agent.
7. **Contract check on**: Turn on C1 and the meta-test last among the rules, since they compare the finished source with the finished contract.

## Requirement coverage

| Requirements | Where they land |
|---|---|
| FR-001, FR-002, FR-004 | `contracts/module-dependency-rules.md`, `module-dependency-rules.json`, rule C1 |
| FR-003 | CODEOWNERS, rule C3 |
| FR-005 to FR-007 | Source `modules` and `portHomes`, rules M1 and M2 |
| FR-008, FR-009, FR-012 | New projects and edges, rules A5 and A12 |
| FR-010, FR-011 | Rules A1 to A11 |
| FR-013 to FR-016 | `RepositoryScan`, rules A13 to A16 |
| FR-017 | Fixtures project, rules A17 and A18 |
| FR-018 | Source `deniedPackages`, rule A19 |
| FR-019 to FR-023 | Root build files, rules B1 to B6 |
| FR-024 | No new pins; checked in quickstart scenario 9 |
| FR-025, FR-026 | Project `IsPackable`, rules P1 and P2 |
| FR-027 | No public type added; checked in quickstart scenario 9 |
| FR-028 | `ci.yml`, `docs-gate.sh`, `CONTRIBUTING.md`, rule C4 |
| FR-029 | `RepositoryFixture`, fixtures project, `[ProvesRule]` meta-test |
| FR-030 | Step 2 above |
| FR-031, FR-032 | `Violation`, `RuleSource`, rule C2 |
| FR-033 | Existing CI check 5, unchanged |
| FR-034 | `README.md`, `docs/03` |
| FR-035 | Contract's expectations table |
| FR-036 | Review, and `SkeletonContainsNoGatewayBusinessCode` |

## Risks

| Risk | Mitigation |
|---|---|
| XML reading misses a reference route that MSBuild would honor | Follow imports; read the root build files as imports of every project; check every solution entry was scanned; reject nested build files, unresolved properties, and properties that redirect an import (A13, B4). Limits are recorded in the contract. |
| The pack test is sensitive to a partial build | It fails with a message to build the solution, and CI always builds first |
| The source scan flags its own fixture strings | The scan blanks out one-line strings and comments, and fixture literals stay inside one-line strings (research R8) |
| Lock files regenerated through this PC's proxy feed differ from the public feed | Lock files record package content hashes, not sources. CI's `--locked-mode` restore proves they match |

## Complexity Tracking

No constitution violations to justify.
