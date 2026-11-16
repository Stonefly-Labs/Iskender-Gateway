# Implementation Plan: ISK-0007 — Container Entry Points & Process Separation

**Branch**: `feature/0007-container-entry-points-process-separation` | **Date**: 2026-11-15 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/0007-container-entry-points-process-separation/spec.md`

## Summary

Turn the two empty host shells into independent container entry points, and prove them in Compose. The work has eight parts:

1. **Composition core** in `Iskender.Domain.Registry.Composition`: the configuration sources, once-per-container validation, the requirement check, and the composition lifecycle move out of `Iskender.Hosting`, through hosting contract version 2. `AddIskender(...)` keeps its public surface and calls the core, and a new control-plane root, `AddIskenderControlPlane(...)` in `Iskender.Management`, calls the same core. Each root names its plane, so each plane's guard holds its own lease (R1 to R3).
2. **Refusal mapping** in the core: a refused mandatory feature fails start, in embedded and container hosts alike, with event 6004 (R4). Composition start-ups stop after the server has drained (R5).
3. **Container library**, `Iskender.Infrastructure.Container`, a new adapter under the existing pattern: the `container` section, the configuration roots, the listeners told apart by local port, liveness and readiness, the stop sequence, the command line and exit codes, OpenTelemetry, and JSON logs (R6 to R13, R15).
4. **Migration command** in `Iskender.Management`: `migrate apply`, `reverse`, and `status`, run by the control-plane executable with the serving plane's registrations and no start-up (R14).
5. **Entry points**: each host's `Program.cs` hands its plane's composition to the container library.
6. **Images**: one multi-stage Dockerfile per plane, digest-pinned, chiseled, numeric non-root, read-only root (R16).
7. **Evidence**: the Compose topology and its tests, the cross-host lifecycle test, and in-process tests of every readiness reason, exit code, and stop path (R17 to R19).
8. **Publication**: the container contract version 1, hosting contract version 2, rules contract version 5 with no edge change, the operator guide in `docs/04`, and repointed links (R21, R22).

See [research.md](research.md) for the decisions. The maintainer decided four of them during `plan` on 2026-11-15 (R1, R6, R11, R14), and they are recorded in the spec's Clarifications.

## Technical Context

**Language/Version**: C# 14 on .NET 10, SDK 10.0.404 per `global.json`, shared framework 10.0.15.

**Primary Dependencies**:

- `Iskender.Domain`: `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Hosting.Abstractions` 10.0.15, both MIT and already pinned (R1).
- `Iskender.Infrastructure.Container` (new): `FrameworkReference` to `Microsoft.AspNetCore.App`; `OpenTelemetry.Extensions.Hosting` and `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.21.0, Apache-2.0, bringing `OpenTelemetry`, `OpenTelemetry.Api`, and `OpenTelemetry.Api.ProviderBuilderExtensions` 1.21.0, Apache-2.0 (R15).
- `Iskender.Management`: none new. It takes the abstractions through `Iskender.Domain`.
- Hosts: project references to `Iskender.Infrastructure.Container`, `Iskender.Infrastructure.PostgreSql`, and `Iskender.Infrastructure.Redis`.
- Images: `mcr.microsoft.com/dotnet/sdk:10.0.404-noble` and `mcr.microsoft.com/dotnet/aspnet:10.0.15-noble-chiseled`, pinned by digest (R16).
- Tests: the pinned `xunit.v3`, `Testcontainers.PostgreSql`, and `Microsoft.AspNetCore.TestHost`. The Compose tests drive the `docker` command line, with no new package.

**Storage**: None new. The lease, migration, and Compose scenarios use PostgreSQL 17 and Valkey 8 at the ISK-0004 and ISK-0005 fixtures' digests. No product table is added. The only migration module is ISK-0005's `coordination`.

**Testing**: xUnit v3 on Microsoft.Testing.Platform under CI check "4. Test": unit tests of the core, in-process tests of the container library through test hosts with stub probes, ISK-0006's hosting suite, Testcontainers tests for the lease and migration scenarios, and Compose and image tests through the real images. The architecture suite (check 5) and the compatibility harness run against rules contract version 5 and must pass.

**Target Platform**: Linux containers, `linux/amd64` on CI's `ubuntu-24.04` runner and the developer's own architecture locally. Development on Windows, macOS, and Linux.

**Project Type**: Two ASP.NET Core executables, one new embeddable library, changes to three embeddable libraries, and one new test project, in a modular monolith.

**Performance Goals**: None asserted (Constitution VIII). The 30-second `health` wait, the 35-second grace bound, and the readiness cache default are safety bounds, not performance figures. The 100 concurrent readiness requests and 1,000 liveness requests in User Story 3 count probe calls, not throughput.

**Constraints**:

- Builds and runs every test offline after restore, with no cloud credentials and no container runtime, except the container-backed tests, which skip with their reason locally and fail when `CI=true` (SC-001).
- Restores in `--locked-mode`: every new and changed project commits its lock file (rule B2), with versions in `Directory.Packages.props` (rule B1).
- No edge, role, home, or rule of the module dependency rules changes (FR-004, FR-031). The control-plane host reaches no `Iskender.Hosting`, and the data-plane host no `Iskender.Management`.
- `Iskender.Hosting`'s public surface and baseline do not change, and `core-contracts`, `providers-abstractions`, and `policy-sdk` gain nothing (FR-032).
- No port signature or contract rule of ISK-0003, ISK-0004, or ISK-0005 changes.
- No gateway ingress, management endpoint, authentication scheme, provider, credential provider, cloud provisioning file, or registry push (SC-010).
- `.specify/` and `.iskender/grounding.yaml` are unchanged.
- This PC restores through its user-level proxy feed, which must never appear in a committed `nuget.config`, in a Dockerfile, or in CI.

**Scale/Scope**: 2 entry points, 2 Dockerfiles, 1 new library project, 1 new test project, about 14 core types moved or added, 1 new configuration section with 5 fields, 5 readiness checks, 10 exit codes, 7 new events (6004, and 7001 to 7007), 0 new error codes, 3 contract revisions.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Gate | Status | Notes |
|---|---|---|---|
| I. Product & License | MIT; container and embedded of equal standing | Pass | New packages are Apache-2.0 or MIT (R15), proved by `check-license.sh`. Images carry the `MIT` label. The runtime base is Ubuntu chiseled and .NET, both redistributable (R16). The container entry points reuse the embedded composition, so neither form is second-class (ADR-0004) |
| II. Architecture & Topology | Control and data planes independently deployable; boundaries enforced; local path without cloud credentials | Pass | Two executables, two images, no shared state (TR-1). A6 and A7 hold, and the shared code sits in an adapter that reaches neither `Iskender.Hosting` nor `Iskender.Management` (R6). The core moves within `Iskender.Domain` into a namespace A17 and A18 permit (R1). No cloud SDK, and no provisioning |
| III. Mandatory State | Durable conversation tests | N/A | No conversation code |
| IV. Provider Correctness | Unavailable capabilities fail explicitly | Pass | A refused mandatory feature stops start, and a refused non-mandatory one is listed in readiness and fails closed on every call (R4, R11) |
| V. Security & Privacy | Fail closed; non-root; no secrets in output; telemetry redaction | Pass | Validation before any listener binds (R9). Health endpoints only on their own listener, chosen by local port (R10). Numeric non-root, no capability, read-only root, no shell (R16). Readiness and events are value-free, and ASP.NET Core's request logs are off (R11, R15). No exporter unless chosen. A lease held by another process is not ready (R11). The canary covers readiness, exit diagnostics, and container logs |
| VI. Policy & Plugins | Typed policies; no competing implementation | N/A | No policy or plugin code. Read-only plugin mounts are left to the plugin specs |
| VII. Resilience & Governance | Explicit failure semantics; no silent weakening | Pass | Every failure class has its own exit code (TR-10). Replicas stay ready during an expand-only migration, per the ISK-0004 decision. Migrations run only as an explicit job, which starts nothing (R14). The stop sequence drains before it releases the lease (R5, R12) |
| VIII. Test & Operational Rigor | Risk-proportionate tests; no invented numbers; rollback docs | Pass | Unit, in-process, Testcontainers, Compose, and image tests, with 5 consecutive Compose runs. No performance figure is asserted. The operator guide documents the stop sequence, the grace rule, and the migration command's role in rollback. Rollback of this PR is a revert: it adds no table and stores nothing |
| IX. Engineering Practice | Versioned surfaces; doc sync; supply-chain hygiene | Pass | Container contract 1, hosting contract 2, rules contract 5. Docs change in the same PR. Packages are centrally pinned and lock-file hashed, and base images are digest-pinned. R5 and R6 stay unapplied under DEC-20261105-002 |
| X. Spec Kit Discipline | Stage and budget rules | Pass | Run by `/drive-speckit`. `.specify/` is unchanged |
| XI. Human Governance | No silent roadmap change; real evidence | Pass | No roadmap, ADR, constitution, or grounding change. The three contracts change through their own revision procedures, which the approved prompt and the earlier specs assign here. Every `plan` decision records the maintainer as decider |
| XII. Enforcement | Owner and gate per principle | Pass | Each contract section names its review owner |

**Post-design re-check**: Pass. The maintainer decided four judgments on 2026-11-15, each recorded in research and in the spec's Clarifications. They reword parts of the spec and change no gate above:

- ISK-0006's hosting suite keeps every behavior assertion, and only lines that name moved internal types change (R1), so US6.4 and SC-009 say so.
- The shared container code is a new adapter project under the existing pattern (R6), which needs no rule change.
- A lease held by another process makes readiness not ready with `single_instance_held_by_other` (R11), added to TR-8, FR-018, and as US2.10.
- The migration command makes the serving plane's registration calls and starts nothing (R14), so TR-9 is reworded.

Two further refinements follow from the code, with no choice left to make, and are recorded in research: the equal-ports check happens when the settings are read, since ISK-0003 has no rule across fields (R9), and the cross-host revision comparison uses the data plane's registrations, since the sample registers neither `persistence` nor `container` (R18).

## Project Structure

### Documentation (this feature)

```text
specs/0007-container-entry-points-process-separation/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── container.md                  # Version 1
│   ├── hosting.md                    # Version 2 of ISK-0006's contract
│   └── module-dependency-rules.md    # Version 5 of ISK-0001's contract
├── checklists/
│   └── requirements.md
└── tasks.md                          # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Iskender.Domain/
│   ├── Iskender.Domain.csproj               # + DI and hosting abstractions
│   ├── Ports/                               # + CompositionPlane, CompositionRoot
│   └── Registry/Composition/                # New: CompositionDocument, CompositionSources, CompositionServices,
│                                            #   CompositionConfiguration, CompositionLifecycle, CompositionStatus,
│                                            #   CompositionModules, CompositionCheck, CompositionLog
├── Iskender.Hosting/
│   ├── IskenderServiceCollectionExtensions.cs   # Calls CompositionServices.AddRoot
│   ├── Composition/, Configuration/IskenderConfiguration.cs, Diagnostics/HostingLog.cs
│   │                                        # Removed: moved to the core
│   └── Configuration/HostingSectionSchema.cs, HostingSettings.cs, Requests/   # Unchanged
├── Iskender.Management/
│   ├── ControlPlaneServiceCollectionExtensions.cs, ControlPlaneOptions.cs     # New
│   └── Migrations/                          # New: MigrationOperation, MigrationArguments, MigrationCommand, MigrationOutcome
├── Iskender.Infrastructure/
│   ├── Composition/CompositionRegistration.cs   # Forwards to CompositionServices.AddModule
│   ├── Composition/CompositionCheck.cs, RegistrationMissing.cs                # Removed: moved to the core
│   └── Coordination/CoordinationStartup.cs  # Plane from CompositionRoot
├── Iskender.Infrastructure.Container/       # New project, role: embeddable (Iskender.Infrastructure.<Adapter>)
│   ├── Iskender.Infrastructure.Container.csproj, packages.lock.json
│   ├── ContainerServiceCollectionExtensions.cs, ContainerSectionSchema.cs, ContainerSettings.cs
│   ├── ConfigurationRoots.cs
│   ├── ContainerEntryPoint.cs, ContainerPlaneDefinition.cs, ContainerCommand.cs, ExitCodes.cs
│   ├── Lifecycle/ContainerLifecycle.cs
│   ├── Health/HealthEndpoints.cs, ReadinessEvaluator.cs, ProbeGate.cs, ReadinessReport.cs
│   ├── Telemetry/ContainerTelemetry.cs
│   └── Diagnostics/ContainerLog.cs          # Events 7001 to 7007
├── Iskender.Gateway.Host/
│   ├── Iskender.Gateway.Host.csproj, packages.lock.json   # + Container, PostgreSql, Redis references
│   ├── Program.cs                           # The data plane's definition
│   └── Dockerfile                           # New
└── Iskender.ControlPlane.Host/
    ├── Iskender.ControlPlane.Host.csproj, packages.lock.json   # + Container, PostgreSql, Redis references
    ├── Program.cs                           # The control plane's definition and the migrate command
    └── Dockerfile                           # New

tests/
├── Iskender.Domain.Tests/Registry/Composition/  # Core tests
├── Iskender.Hosting.Tests/                  # Moved-type references only (R1); + embedded refusal test
├── Iskender.Infrastructure.Tests/           # Moved-type references; guard plane from the root
├── Iskender.Container.Tests/                # New test project
│   ├── Iskender.Container.Tests.csproj, packages.lock.json
│   ├── Fixture/                             # ImageBuild, ComposeProject, RequireDocker, ContainerTestHost, stub probes
│   ├── compose/compose.yaml, compose/scenarios/<scenario>/iskender.yaml
│   └── Roots/, Settings/, Listeners/, Readiness/, Lifecycle/, CommandLine/, Migrations/, SingleInstance/,
│       Images/, Compose/, CrossHost/, Canary/
├── Iskender.Architecture.Tests/
│   └── module-dependency-rules.json         # Version 5 and its contract path
└── Iskender.Contracts.Compatibility.Tests/  # No baseline change

.dockerignore                                # New
Iskender.slnx                                # + the two new projects
.github/CODEOWNERS                           # + the new projects and .dockerignore
README.md, docs/05-identity-security-and-privacy.md, .claude/agents/reviewers/architecture-guard.md
                                             # Repoint the rules contract to version 5
docs/03-domain-and-technical-architecture.md # Hosting contract v2, container contract, rules contract v5 links
docs/04-technology-stack-and-deployment.md   # Same links; "Container entry points" guide; migration and coordination runbooks
```

**Structure Decision**: The core joins the Registry module as `Iskender.Domain.Registry.Composition`, the only place in `Iskender.Domain` that may call `ConfigurationValidator` and use the seam types (R1). The control-plane root sits in `Iskender.Management`, the control plane's own library. What both entry points share sits in one new adapter project under the approved `Iskender.Infrastructure.<Adapter>` pattern (R6). The Dockerfiles sit beside the projects they build, under paths that are already behavior-bearing and code-owned. The container tests are one project, since the in-process and image tests share fixtures and scenarios.

## Implementation approach

The order keeps the build and every test passing at each commit. `tasks.md` orders the same work by user story. Where the two orders differ, `implement` follows `tasks.md`.

1. **Core move**: add the abstractions to `Iskender.Domain`, move the core types into `Iskender.Domain.Registry.Composition` with `CompositionRoot` and `CompositionPlane`, make `AddIskender(...)` call `CompositionServices.AddRoot`, forward `CompositionRegistration.Add`, and update only the moved-type references in ISK-0006's tests. The hosting suite, the harness, and the architecture suite pass with no behavior assertion changed (R1).
2. **Plane and stop order**: `CoordinationStartup` takes its plane from the root, and the core stops start-ups in `StoppedAsync`, with tests of each (R2, R5).
3. **Refused mandatory feature**: the core's check and event 6004, with tests of each refusal reason in the core and in an embedded host (R4).
4. **Control-plane root**: `AddIskenderControlPlane(...)`, the duplicate-root and missing-root tests, and the architecture suite (R3).
5. **Container library skeleton**: the project, its lock file, solution and CODEOWNERS entries, the `container` section and settings, and the configuration roots with their tests (R6, R8).
6. **Entry point**: the empty builder, validation before listeners, both listeners by local port, liveness, the command line, exit codes 0, 1, 2, 3, and 64, events 7001, 7002, and 7005 (R7, R9, R10, R13).
7. **Readiness**: `ProbeGate`, `ReadinessEvaluator`, the five checks, event 7003, and the reason, liveness-count, concurrency, and traffic-listener tests (R11).
8. **Lifecycle**: `ContainerLifecycle`, the delay and drain, the stop during start-up, a second signal, and exit code 4 (R12).
9. **Telemetry and logs**: OpenTelemetry with the conditional exporter, the JSON logger and its filters, and `check-license.sh` over the new lock files (R15).
10. **Migration command**: `MigrationArguments`, `MigrationCommand`, the control plane's `migrate` command, and the tests against ISK-0004's fixture with a test credential provider, for exit codes 5, 6, and 7 and events 4007 to 4011 and 7006 (R14).
11. **Entry points**: both `Program.cs` files, their references and lock files, and the single-instance lease tests across both planes (TR-5, US2.2, US2.3).
12. **Images**: the Dockerfiles, `.dockerignore`, and the image inspection tests (R16).
13. **Compose and cross-host**: the topology, its scenarios, the 5-run acceptance test, the image-level exit code tests, the cross-host test, and the canary (R17, R18).
14. **Publication**: rules contract version 5 in the rule source, the link repoints, the operator guide and runbooks in `docs/04`, and every quickstart scenario run once (R21, R22).

## Requirement coverage

| Requirements | Where they land |
|---|---|
| FR-001, FR-002, FR-003 | Both `Program.cs` files and Dockerfiles; image inspection and single-plane Compose tests |
| FR-004 | Rules contract version 5 and the rule source; the architecture suite |
| FR-005, FR-006, FR-007 | `Iskender.Domain.Registry.Composition`, `CompositionRoot`, `AddIskenderControlPlane(...)`, `CoordinationStartup`; root, duplicate, missing-root, and lease tests |
| FR-008 | Both `Program.cs` files, the container contract's Entry points rule 2; the TR-5 composition test |
| FR-009, FR-010, FR-011, FR-011a | `ConfigurationRoots`, `ContainerEntryPoint`'s empty builder, `ContainerSectionSchema`; roots, ASP.NET Core variable, and not-ready-traffic tests |
| FR-012 to FR-015 | `ContainerEntryPoint`, `CompositionLifecycle`, `ExitCodes`; one test per exit code |
| FR-016 to FR-020 | `HealthEndpoints`, `ReadinessEvaluator`, `ProbeGate`; reason, liveness-count, concurrency, listener, and canary tests |
| FR-021, FR-021a, FR-022 | `ContainerLifecycle`, `CompositionLifecycle.StoppedAsync`, the Compose `stop_grace_period`; stop tests, the operator guide |
| FR-023, FR-024, FR-025 | `MigrationCommand`, `MigrationArguments`, the control plane's `migrate` command; migration tests in process and from the image |
| FR-026, FR-027 | Both Dockerfiles; image tests under check 4 |
| FR-028, FR-029 | `ContainerTelemetry`, `ContainerLog`, `CompositionLog`; event and exporter tests |
| FR-030, FR-031, FR-032 | The three contracts; the link repoints; the harness |
| FR-033 | `tests/Iskender.Container.Tests/compose/`, the cross-host test |
| FR-034 | `docs/04` |

## Risks

| Risk | Mitigation |
|---|---|
| Moving the core changes behavior that ISK-0006's tests do not name | The core moves before anything else changes (step 1), and the whole hosting suite, the harness, and the architecture suite must pass at that commit with only moved-type references edited |
| Kestrel reads its options before validation runs, so ports come from defaults | The entry point validates before `StartAsync`, and the `ConfigureKestrel` callback reads `ContainerSettings` from the validated snapshot. A test sets non-default ports and checks both listeners |
| The web server stops after the core, so the lease is released before the drain | The core stops in `StoppedAsync` (R5). A test holds a request open across a stop and checks the lease is held until it finishes |
| A signal during start-up disposes the Redis coordinator mid-connection | The entry point awaits `StartAsync` before stopping and disposing (R12). A test stops during a slow first Redis connection |
| A chiseled image lacks something the runtime needs | The image tests start each image read-only, with no capability, and reach readiness and a clean stop. A missing file fails them |
| Image builds slow or break check 4 | Images are built once per test run. A build failure is a test failure, never skipped when `CI=true` |
| Compose tests are flaky | They assert on states with ISK-0005's bounds and wait on health with a timeout, not on fixed sleeps. A real flake is a blocker for `implement` |
| The OpenTelemetry packages bring a dependency outside the license list or with a vulnerability | `check-license.sh` and check 9 run over the new lock files in step 9. A failure is reported as a blocker, not suppressed |
| A base image digest is withdrawn | Digests are recorded in the container contract. A withdrawn one is replaced through a revision of that table, never by a tag |

## Complexity Tracking

No constitution violations to justify.
