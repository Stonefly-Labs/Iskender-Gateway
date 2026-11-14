# Implementation Plan: ISK-0006 — Host Composition & DI Registration

**Branch**: `feature/0006-host-composition-di-registration` | **Date**: 2026-11-14 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/0006-host-composition-di-registration/spec.md`

## Summary

Let an ASP.NET Core application compose Iskender into its own container and pipeline, and prove it in process. The work has eight parts:

1. **Public surface** in `Iskender.Hosting`: `AddIskender(...)`, `MapIskender(...)`, the runtime options, the configuration document type, and the builder, versioned by the compatibility harness as contract `hosting` version 1.
2. **Composition seam** in `Iskender.Domain.Ports`: `CompositionModule`, `IValidatedConfiguration`, and `ICompositionStartup`, through which each infrastructure assembly registers its own services with no new project reference.
3. **Start-up**: requirement checks, once-per-container validation through ISK-0003's engine with host-owned refusal, and coordination at start: coordinator presence, the single-instance guard, the capability check, and the gate.
4. **Registrations** for coordination in `Iskender.Infrastructure`, PostgreSQL persistence and, separately, the PostgreSQL coordinator in `Iskender.Infrastructure.PostgreSql`, and the Redis coordinator in `Iskender.Infrastructure.Redis`.
5. **Route group and request bootstrap**: one group under the host's prefix, a per-request `GatewayExecutionContext` with a deadline from the new `hosting.requestTimeout`, and the ISK-0002 404 envelope, with no global middleware.
6. **No hidden singletons**: a reflection scan of static fields, fixing the one piece of process-wide state ISK-0004 left (`PersistenceTelemetry.HeldConnections`) and three mutable-typed statics.
7. **Evidence**: the `samples/Iskender.Samples.EmbeddedHost` application, the reusable `EmbeddedHost` fixture, and the acceptance tests of User Stories 1 to 5.
8. **Publication**: the hosting contract version 1, module dependency rules contract version 4, three `hosting.*` error codes raising `core-contracts` to 5, links from `docs/03` and `docs/04`, and an embedding guide.

See [research.md](research.md) for the decisions. The maintainer decided five of them during `plan` on 2026-11-14 (R8, R9, R11, R13, R15).

## Technical Context

**Language/Version**: C# 14 on .NET 10, SDK 10.0.403 per `global.json`, shared framework 10.0.14

**Primary Dependencies**:

- `Iskender.Hosting`: `FrameworkReference` to `Microsoft.AspNetCore.App` (R1).
- `Iskender.Infrastructure`: `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.14 and `Microsoft.Extensions.Hosting.Abstractions` 10.0.14, both MIT. The adapters take them through `Iskender.Infrastructure`.
- `Iskender.Domain`: none new.
- Sample: `Microsoft.NET.Sdk.Web`, with no package.
- Tests: `Microsoft.AspNetCore.TestHost` 10.0.14 (MIT), beside the pinned `xunit.v3` 4.0.2 and `Testcontainers.PostgreSql` 4.16.0.

**Storage**: None new. The PostgreSQL single-instance test uses ISK-0004's fixture. Every other test opens no connection.

**Testing**: xUnit v3 on Microsoft.Testing.Platform under CI check "4. Test", through the in-memory ASP.NET Core test server. The architecture suite (check 5) and the compatibility harness run against the revised rules and must pass.

**Target Platform**: Windows, macOS, and Linux development machines, and `ubuntu-24.04` in CI. Only the single-instance lease test needs a container runtime.

**Project Type**: A public contract library (`Iskender.Hosting`), embeddable infrastructure libraries, a sample ASP.NET Core application, and two test projects, in a modular monolith.

**Performance Goals**: None asserted (Constitution VIII). `requestTimeout`'s 100-second default and its bounds are safety bounds, not performance figures. The 100 concurrent requests in US4-3 test isolation, not throughput.

**Constraints**:

- Builds and runs every test but one offline after restore, with no credentials and no container runtime (SC-001).
- Restores in `--locked-mode`: every new and changed project commits its lock file (rule B2), with versions in `Directory.Packages.props` (rule B1).
- No public member of `Iskender.Hosting` names an `Iskender.Domain` type (FR-018).
- No port signature, settings type, or contract rule of ISK-0004 or ISK-0005 changes (FR-005). The static-state fixes keep every property type.
- No global middleware, authentication scheme, readiness endpoint, container entry point, logging provider, or exporter (SC-008). `Iskender.Gateway.Host` and `Iskender.ControlPlane.Host` stay as they are (AV-1).
- This PC restores through its user-level proxy feed, which must never appear in a committed `nuget.config` or in CI.

**Scale/Scope**: 6 public types, 3 seam types, 5 registrations and modules, 1 new configuration section with 1 field, 3 error codes, 3 log events, 2 new test projects, 1 sample, 1 new architecture rule (A20).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Gate | Status | Notes |
|---|---|---|---|
| I. Product & License | MIT; no open-core withholding | Pass | Every new package is MIT (R1). Embedding has the same standing as the container (ADR-0004), and the sample and fixture ship in the open-source repository |
| II. Architecture & Topology | Boundaries and direction enforced; embedded hosting without global ownership; local path builds without cloud credentials | Pass | `Iskender.Hosting` keeps its two references (A5, A8, A10). The seam is in `Iskender.Domain.Ports`, so no edge is added (R3). No global middleware, and no host option or configuration changed (R14). Rules contract version 4 places the sample, and A20 keeps it out of production graphs (R5). No cloud SDK |
| III. Mandatory State | Durable conversation tests | N/A | No conversation code |
| IV. Provider Correctness | Unavailable features fail explicitly | N/A | No provider code. The gate fails closed before start (R10) |
| V. Security & Privacy | Isolation tests; fail closed; telemetry redaction | Pass | A rejected configuration stops start, and nothing runs on defaults (R7). The host's development setting never relaxes Iskender's defaults (R6). One context per request, and no state between containers (R11, R12). A canary across every output (R16). Mandatory authentication stays inside the group for the identity specs (R14) |
| VI. Policy & Plugins | Typed policies; no competing implementation | N/A | No policy or plugin code. No assembly is scanned (R3) |
| VII. Resilience & Governance | Explicit failure semantics; no silent weakening | Pass | A configured coordinator that was not registered fails start, rather than letting features run refused or fall back (R8, R10). The capability check and the single-instance guard run in embedded hosts |
| VIII. Test & Operational Rigor | Risk-proportionate tests; no invented numbers; rollback docs | Pass | Unit, integration, concurrency, reflection, and in-process host tests, and one Testcontainers test. No performance figure is asserted. Rollback is reverting the PR: there is no migration and no stored state |
| IX. Engineering Practice | Versioned surfaces; doc sync; supply-chain hygiene | Pass | `hosting` 1, `core-contracts` 4 to 5, hosting contract 1, rules contract 4, docs in the same PR, packages centrally pinned and lock-file hashed. R5 and R6 stay unapplied under DEC-20261105-002 |
| X. Spec Kit Discipline | Stage and budget rules | Pass | Run by `/drive-speckit`. `.specify/` is unchanged |
| XI. Human Governance | No silent roadmap change; real evidence | Pass | No roadmap, ADR, constitution, or grounding change. The rules contract changes through its own revision procedure, which ISK-0001 assigned to this spec. Every `plan` decision records the maintainer as decider |
| XII. Enforcement | Owner and gate per principle | Pass | Each contract section names its review owner |

**Post-design re-check**: Pass. The maintainer decided five judgments on 2026-11-14, each recorded in research. They reword parts of the spec, and change no gate above:

- Static telemetry instruments are allowed, and the real process-wide counter is fixed (R11).
- Order-dependent registration checks fail at host start (R8), so US5-2 and US5-5 say "when the host starts".
- A duplicate registration throws at the call and writes no event (R9), so TR-13, and with it FR-020, drops that event.
- The request deadline comes from a new `hosting.requestTimeout` field (R13), recorded as TR-14.
- An unreadable configuration document fails start with `hosting.configuration_rejected` (R15), so the edge case is reworded.

## Project Structure

### Documentation (this feature)

```text
specs/0006-host-composition-di-registration/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── hosting.md                    # Version 1
│   └── module-dependency-rules.md    # Version 4 of ISK-0001's contract
├── checklists/
│   └── requirements.md
└── tasks.md                          # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Iskender.Core.Contracts/
│   ├── Properties/ContractPackage.cs        # core-contracts 4 → 5
│   └── Errors/ErrorCodes.cs                 # + 3 hosting.* codes, since 5
├── Iskender.Domain/
│   └── Ports/                               # + CompositionModule, IValidatedConfiguration, ICompositionStartup
│       └── CoordinationPrimitive.cs         # All backed by ImmutableArray (R11)
├── Iskender.Hosting/                        # Role: public contract
│   ├── Iskender.Hosting.csproj              # + FrameworkReference Microsoft.AspNetCore.App
│   ├── Properties/ContractPackage.cs        # hosting 1
│   ├── IskenderServiceCollectionExtensions.cs, IskenderEndpointRouteBuilderExtensions.cs
│   ├── IskenderOptions.cs, IskenderConfigurationDocument.cs, IskenderDocumentFormat.cs, IskenderBuilder.cs
│   ├── Composition/                         # IskenderRuntimeOptions, CompositionGuard, IskenderStartup
│   ├── Configuration/                       # IskenderConfiguration, HostingSectionSchema, HostingSettings
│   ├── Requests/                            # RequestBootstrap, RequestContextHolder, NotFoundEndpoint
│   └── Diagnostics/HostingLog.cs            # Events 6001 to 6003
├── Iskender.Infrastructure/
│   ├── Iskender.Infrastructure.csproj       # + DI and hosting abstractions
│   ├── Composition/                         # CompositionRegistration, CompositionCheck
│   └── Coordination/                        # + CoordinationServiceCollectionExtensions, CoordinatorRegistration,
│                                            #   CoordinationStartup, ComposedCoordination; AllowedTags (R11)
├── Iskender.Infrastructure.PostgreSql/
│   ├── PostgreSqlServiceCollectionExtensions.cs   # Persistence, and the coordinator as a separate opt-in
│   ├── PostgreSqlDataSources.cs             # Owns its HeldConnections
│   └── Diagnostics/                         # HeldConnections; PersistenceTelemetry loses its static dictionary
└── Iskender.Infrastructure.Redis/
    ├── RedisServiceCollectionExtensions.cs
    ├── RedisConnection.cs                   # ExcludedCommands as FrozenSet (R11)
    └── RedisScripts.cs                      # All backed by ImmutableArray (R11)

samples/
└── Iskender.Samples.EmbeddedHost/           # New, role: sample, Microsoft.NET.Sdk.Web
    ├── Iskender.Samples.EmbeddedHost.csproj # refs Iskender.Hosting, Iskender.Infrastructure
    ├── packages.lock.json
    ├── Program.cs, SampleApplication.cs, GreetingService.cs, SampleHeaderMiddleware.cs
    ├── appsettings.json                     # Sample:Greeting
    ├── iskender.yaml                        # Base document
    └── README.md                            # Points to the embedding guide

tests/
├── Iskender.Hosting.Testing/                # New class library: EmbeddedHost, CapturedLog
├── Iskender.Hosting.Tests/                  # New: acceptance groups, surface, static-state scan, lifetimes, canary
├── Iskender.Infrastructure.PostgreSql.Tests/    # HeldConnections per data source
├── Iskender.Infrastructure.Redis.Tests/     # ExcludedCommands still applied
├── Iskender.Architecture.Fixtures/          # + A20 negative fixture, a sample under samples/
├── Iskender.Architecture.Tests/
│   ├── module-dependency-rules.json         # Version 4: roles, sample, A20, A14, A19, C3
│   └── Rules/                               # A14, A19, A20, C3 read samples/
├── Iskender.Core.Contracts.Tests/
│   └── SolutionBuildsTests.cs               # Allows src/Iskender.Hosting/
└── Iskender.Contracts.Compatibility.Tests/
    ├── Iskender.Contracts.Compatibility.Tests.csproj   # + reference to Iskender.Hosting
    └── baselines/                           # + hosting.surface.txt; core-contracts.surface.txt, registries.json regenerated

Directory.Packages.props                     # + 3 package versions at 10.0.14
Iskender.slnx                                # + /samples/ folder and the three new projects
.github/CODEOWNERS                           # + /samples/ and the new projects
.github/scripts/docs-gate.sh, CONTRIBUTING.md    # + samples/** as behavior-bearing
README.md, docs/05-identity-security-and-privacy.md, .claude/agents/reviewers/architecture-guard.md
                                             # Repoint the rules contract to version 4
docs/03-domain-and-technical-architecture.md # Hosting contract and rules contract v4 links
docs/04-technology-stack-and-deployment.md   # Same links, and an "Embedding guide" section
specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md
                                             # Contract versions and Error codes tables, AV-5 known limit
```

**Structure Decision**: Hosting's public types sit at the root of `Iskender.Hosting`, and its internals in feature folders. The seam joins the existing ports in `Iskender.Domain.Ports`, the approved home for types shared across modules, so no project gains a reference. Each infrastructure assembly's registration method lives next to the types it composes. The sample gets the new `samples/` root and `sample` role from rules contract version 4. The fixture is a separate class library, so ISK-0007 can reference it without referencing these tests.

## Implementation approach

The order keeps the build and every test passing for each commit.

1. **Packages**: add the three package versions, and prove `check-license.sh` passes once they are restored.
2. **Error codes and version**: add the three codes with `since: 5`, raise `core-contracts` to 5, and update the ISK-0002 contract's tables and the harness baselines in the same commit.
3. **Rules contract version 4 in the rule source**: the `sample` role, `samples/` in A14, A19, and C3, rule A20 with its negative fixture, and `Iskender.Hosting` as `public contract`. Point the rule source at version 4, repoint the links its Changing section names, and update the architecture tests that pin the version 3 path (`RuleSourceTests`, `SupportTests`, `ContractDocumentRuleTests`). Hosting becoming a public contract makes the harness require its baseline in the same commit, so step 4's minimal surface lands with it: `ContractPackage("hosting", 1)` and the harness reference, with an empty surface baseline that grows as types are added.
4. **Seam types** in `Iskender.Domain.Ports`, with unit tests for `CompositionModule`'s validation.
5. **Public surface and options**: the six public types, the `Iskender.Domain` exposure test, and registration-time tests (no I/O, duplicate detection). Regenerate `hosting.surface.txt`.
6. **Validation**: `IskenderConfiguration`, `HostingSectionSchema`, the source mapping, `IskenderStartup`'s requires check and validation, events 6001 to 6003, and the start-up refusal tests through a first version of the fixture.
7. **Fixture library**: `EmbeddedHost` and `CapturedLog`, used from here on by every in-process test.
8. **Route group and bootstrap**: `MapIskender(...)`, the bootstrap filter, the holder, the catch-all, and the coexistence and concurrency tests.
9. **Infrastructure registrations**: `CompositionRegistration`, `CompositionCheck`, `AddIskenderCoordination()`, `CoordinatorRegistration`, and `CoordinationStartup`, with the missing-coordinator and gate-before-start tests.
10. **Adapter registrations**: PostgreSQL persistence, the PostgreSQL coordinator as a separate opt-in, and the Redis coordinator, with the no-connection resolution test and the single-instance lease test on ISK-0004's fixture.
11. **Static state**: the scan, the `HeldConnections` move, the `FrozenSet` and `ImmutableArray` changes, and the contract's listed fields confirmed against the scan's first run.
12. **Sample**: the project, its lock file, solution and CODEOWNERS entries, and the sample-based acceptance runs, including SC-002's 20 consecutive starts.
13. **Lifetimes and canary**: container validation over every registration combination, the disposal test, and the canary across the hosting suite.
14. **Publication**: link the hosting contract from `docs/03` and `docs/04`, write the embedding guide, add `samples/**` to the docs gate, and check every quickstart scenario passes.

## Requirement coverage

| Requirements | Where they land |
|---|---|
| FR-001, FR-002, FR-003 | `IskenderServiceCollectionExtensions`, `IskenderOptions`, `IskenderConfigurationDocument`, `IskenderConfiguration`; source, environment, and `IConfiguration` tests |
| FR-004, FR-005, FR-006 | `CompositionModule`, `CompositionRegistration`, the five registration methods; order, opt-in, and rules suite tests |
| FR-007 to FR-010 | The Lifetimes table; container validation matrix, static-state scan, concurrency, and disposal tests |
| FR-011, FR-012, FR-013 | `IskenderConfiguration`, `IskenderStartup`; registration-inertness, once-per-container, and refusal tests |
| FR-013a | `CoordinationStartup`, `ComposedCoordination`; missing-coordinator, guard, and capability report tests |
| FR-014, FR-015, FR-016 | `MapIskender(...)`, `RequestBootstrap`; coexistence tests; the contract's authentication rule for later specs |
| FR-017, FR-018, FR-019 | `ContractPackage("hosting", 1)`, version tags, `ErrorCodes`; the harness, the exposure test, one test per code |
| FR-020, FR-021 | `HostingLog`; event and canary tests; the added-services test |
| FR-022 | Rules contract version 4; the rule source; the architecture suite and A20's fixture |
| FR-023 | `contracts/hosting.md` |
| FR-024, FR-025 | `samples/Iskender.Samples.EmbeddedHost`, `tests/Iskender.Hosting.Testing`; the acceptance groups |
| FR-026 | `docs/03`, `docs/04` and its embedding guide, the sample's README |

## Risks

| Risk | Mitigation |
|---|---|
| ASP.NET Core starts the server before an Iskender hosted service runs, so a request arrives before validation | Validation runs in `IHostedLifecycleService.StartingAsync`, which runs before every `StartAsync`, the server's included. Validation on first need covers any earlier resolution (R7). A test sends a request during a slow validation and checks it waits or fails with the code, never sees defaults |
| `ValidateOnBuild` fails on a factory whose dependency only a later registration adds | Factories resolve optional dependencies with `GetService`. The lifetime matrix builds every combination with validation on |
| The catch-all endpoint outranks a host route under an overlapping prefix | It uses the lowest route precedence. A coexistence test maps the sample's own route under Iskender's prefix and checks the host's route wins |
| Making `Iskender.Hosting` a public contract fails rule H1 or V1 for a commit with types but no tags | Step 3 lands the package attribute and baseline with the role change, and each later commit tags its types and regenerates the baseline |
| The static-state scan finds fields the text search missed | The scan's first run is the real check (R11). Each finding is fixed if it holds state, otherwise listed with a reason, and never suppressed |
| Moving `HeldConnections` changes the gauge's reported values | The metric's name, unit, and tags stay. A test opens units on two data sources and checks each reports only its own |
| The single-instance lease test is flaky | It asserts on lease state after start and after stop, not on timing, with ISK-0005's bounds. A real flake is a blocker for `implement` |
| A new package gets a vulnerability, failing check 9 | Pin the latest patched 10.0 servicing release at `implement`. If none is clean, report a blocker rather than suppress the check |

## Complexity Tracking

No constitution violations to justify.
