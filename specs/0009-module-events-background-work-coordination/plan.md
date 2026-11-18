# Implementation Plan: ISK-0009 — Module Events & Background Work Coordination

**Branch**: `feature/0009-module-events-background-work-coordination` | **Date**: 2026-11-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/0009-module-events-background-work-coordination/spec.md`

## Summary

Add a durable, PostgreSQL-only handoff of work between modules, with no broker. The work has eight parts:

1. **Ports** in `Iskender.Domain.Ports`: work kinds and their declarations, item requests, the outbox, handlers and deliveries, recurring jobs, failures, and `CompositionPlane.Worker` (R1, R11).
2. **The scope directory**, a new table kind in a new framework namespace and schema, `isk_directory`, with per-command row-level security policies, a register and a settle operation that cannot lose a row, and an ownership resolver that turns a row into a unit scope. It lets an operator unit find every scope with work without reading a tenant row (R2 to R4).
3. **The outbox and item store** in a new framework namespace and schema, `isk_work`: an enqueue in the caller's unit, deduplicated per scope, kind, and key, with a fixed 64 KiB payload limit (R5, R14).
4. **The dispatcher**: 64 buckets folded onto `dispatch.shards` shard leases per scope, a durable attempt record, a lock-first fenced apply, a failure record, classified retry with bounded backoff, poison, version skew, in-flight caps, and a shutdown inside ISK-0007's bound (R6 to R9, R12, R13).
5. **Recurring jobs**: a job port, fenced bookkeeping, and the two shipped jobs, `coordination.purge_expired` and `work.retention` (R10).
6. **The worker**: its own plane and root, `AddIskenderWorker(...)`, and the `work` serving command of the control-plane executable (R11).
7. **The developer topology**: a `worker` service with its own documents, and its expected smoke answer (R17).
8. **Evidence and publication**: the work fixture and crash host, telemetry and isolation tests, the `work` contract, five contract revisions, `core-contracts` 6, and the `docs/03` and `docs/04` changes (R15, R16, R18, R19).

See [research.md](research.md) for the decisions. The maintainer decided four of them during `plan` on 2026-11-17, and they are recorded in the spec's Clarifications and its "Maintainer decisions taken in `plan`" table: operator units write only the `operator` directory row (R2), operator-scope items exist (R2), the worker is its own plane (R11), and the payload limit is a fixed contract rule (R14).

## Technical Context

**Language/Version**: C# 14 on .NET 10, SDK 10.0.404 per `global.json`, shared framework 10.0.15.

**Primary Dependencies**: None new. The PostgreSQL adapter keeps its pinned `Npgsql`. The dispatcher uses `Microsoft.Extensions.Hosting.Abstractions` 10.0.15, which `Iskender.Domain` already takes, for `IHostApplicationLifetime` and `HostOptions`. `System.Diagnostics` provides `ActivitySource` and `Meter`. The new test projects take only packages already pinned: `xunit.v3`, `Testcontainers.PostgreSql`, and `Microsoft.NET.Test.Sdk`.

**Storage**: PostgreSQL 17, at the ISK-0004 fixture's digest. Two new migration modules: `directory` (schema `isk_directory`, table `scopes`) and `work` (schema `isk_work`, tables `items` and `job_runs`). `coordination` is unchanged and declares no dependency on `directory`, which `AddIskenderPostgreSqlCoordination()` adds to the migration set (decided in `checklist`). No product table is added.

**Testing**: xUnit v3 on Microsoft.Testing.Platform under CI check "4. Test": unit tests of the port types, settings, backoff, and classification; Testcontainers tests through the new work fixture; the crash host killed with `SIGKILL`; the container tests of the `work` command; the local environment acceptance run with the worker. The architecture suite (check 5) runs against rules contract version 5 unchanged, and the compatibility harness against `core-contracts` 6.

**Target Platform**: Linux containers, `linux/amd64` on CI's `ubuntu-24.04` runner and the developer's own architecture locally. Development on Windows, macOS, and Linux.

**Project Type**: Changes to six embeddable libraries and the control-plane executable, two new test projects, and the developer topology, in a modular monolith.

**Performance Goals**: None asserted (Constitution VIII, spec AV-2). Every configuration default is a safety bound. The 20 kill cycles per injection point in SC-001 count test repetitions, not throughput.

**Constraints**:

- No broker, bus, queue service, Kafka, or cloud SDK, and no `LISTEN`/`NOTIFY` (spec Out of Scope).
- No new production project, so no edge, role, home, or rule of the module dependency rules changes, and the rules contract stays at version 5.
- `Iskender.Hosting`'s public surface and baseline do not change (FR-036). `core-contracts` goes from 5 to 6 with additions only. `providers-abstractions` and `policy-sdk` gain nothing.
- No advisory lock is used as a lease, fence, or mutual exclusion between processes (FR-024). No function bypasses row-level security (AV-1).
- No port signature of ISK-0004 or ISK-0005 changes. `CoordinationMaintenance` gains a method. `CoordinationMigrations.Module` is unchanged.
- The data plane's and serving control plane's readiness answers, stop sequences, and smoke answers are unchanged byte for byte (FR-026, FR-031).
- No new image, executable, or Dockerfile.
- Builds and runs every test offline after restore, with no cloud credentials, except the container-backed tests, which skip with their reason locally and fail when `CI=true`.
- `.specify/` and `.iskender/grounding.yaml` are unchanged.

**Scale/Scope**: 1 new contract and 5 revisions, about 20 new port types, 3 new tables in 2 new schemas, 2 new registration methods and 1 new root, 1 new configuration section with 16 fields, 8 error codes, 12 events, 9 metrics, 2 shipped jobs, 1 new serving command, 1 new Compose service, 2 new test projects.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Gate | Status | Notes |
|---|---|---|---|
| I. Product & License | MIT; container and embedded of equal standing | Pass | No package is added. The outbox and dispatcher are embeddable registrations, so an embedded host composes the same work handoff (FR-037) |
| II. Architecture & Topology | Modular monolith; independent planes; PostgreSQL as source of truth; capability-correct coordination; local path | Pass | Work lives in PostgreSQL only, behind the persistence and coordination contracts, with no broker (`docs/03`, `docs/04`). The worker is a separate process of the control-plane executable, deployable and scalable apart. No new project or edge. The dispatcher declares strict leases and fencing, so it is refused, never weakened, without PostgreSQL leases (R9). The developer topology runs it with no cloud credentials |
| III. Mandatory State | Durable conversation tests | N/A | No conversation code. Stream recovery handlers are ISK-0049's |
| IV. Provider Correctness | Unavailable capabilities fail explicitly | Pass | A refused dispatcher fails start with exit 4. A skewed kind is reported, not silently poisoned (R8) |
| V. Security & Privacy | Isolation at every worker and job boundary; fail closed; no identifiers in telemetry | Pass | Every item read and write runs in its own scope's unit under row-level security (R2, FR-009). The directory is the one cross-scope read, of scope keys only, and its policies let each scope write only its own row (R2). A row becomes a scope only through an ISK-0002 ownership resolver, and a malformed one is skipped (R4). Payloads, scope IDs, item IDs, and keys never reach telemetry, tested by a canary (SC-008). The Security owner reviews R2 to R4 and persistence Isolation rules 9 and 10 |
| VI. Policy & Plugins | Typed policies; no competing implementation | N/A | No policy or plugin code |
| VII. Resilience & Governance | Explicit failure semantics; no silent weakening | Pass | Every failure has a class and a registered code (R7). Retries are bounded, poison is durable, a database outage counts no attempt, and an unknown commit outcome is never retried (R6, R7). Shutdown cancels at a bound and releases leases (R12) |
| VIII. Test & Operational Rigor | Risk-proportionate tests; no invented numbers; rollback docs | Pass | Unit, Testcontainers, real process kills, multi-dispatcher, isolation with the predicate removed, container, and Compose tests. No figure is asserted. The `docs/04` runbook covers the worker, poison, the dedupe window, and rollback: the `work` and `directory` migrations reverse cleanly, in that order, as two steps that leave `coordination` applied, and the `directory` reverse is data-losing (decided in `checklist`) |
| IX. Engineering Practice | Versioned surfaces; doc sync; supply-chain hygiene | Pass | Work contract 1, persistence 3, coordination 2, container 2, hosting 3, local environment 2, `core-contracts` 6. Docs change in the same PR. No package or image is added. R5 and R6 stay unapplied under DEC-20261105-002 |
| X. Spec Kit Discipline | Stage and budget rules | Pass | Run by `/drive-speckit`. `.specify/` is unchanged |
| XI. Human Governance | No silent roadmap change; real evidence | Pass | No roadmap, ADR, constitution, or grounding change. The five contracts change through their own revision procedures, which the approved prompt and the earlier specs' "Left to later specs" tables assign here, and the hosting revision was decided by the maintainer. Every `plan` decision records the maintainer as decider |
| XII. Enforcement | Owner and gate per principle | Pass | Each contract section names its review owner |

**Post-design re-check**: Pass. The four `plan` decisions reword parts of the spec (TR-1, TR-4, TR-9, TR-14, TR-18, FR-002, FR-006, FR-009, FR-025, FR-029, FR-035) and change no gate above. The hosting revision adds a contract version the spec did not list, with no public surface change, so SC-010 still holds.

Three refinements follow from the code, with no choice left to make, and are recorded in research:

- The directory column is `listed_scope`, not `scope_key`, because the migration runner tells table kinds apart by column name and requires a scope-keyed table to have exactly one policy (R2).
- A single conditional delete cannot serialize with a producer under Read Committed, because its subquery keeps its first snapshot, so the settle locks the row first and checks in a second statement (R3).
- The coordinator registers only on inserts, which `RETURNING (xmax = 0)` detects, because every coordination row of a scope implies its directory row (R3).

## Project Structure

### Documentation (this feature)

```text
specs/0009-module-events-background-work-coordination/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── work.md                 # Version 1, new
│   ├── persistence.md          # Version 3 of ISK-0004's contract
│   ├── coordination.md         # Version 2 of ISK-0005's contract
│   ├── container.md            # Version 2 of ISK-0007's contract
│   ├── hosting.md              # Version 3 of ISK-0006's contract
│   └── local-environment.md    # Version 2 of ISK-0008's contract
├── checklists/
│   └── requirements.md
└── tasks.md                    # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Iskender.Core.Contracts/
│   ├── Errors/ErrorCodes.cs                    # + 8 work.* codes, since 6
│   └── Properties/ContractPackage.cs           # core-contracts 6
├── Iskender.Domain/
│   └── Ports/                                  # + WorkKind, WorkKindDeclaration, WorkItemRequest, WorkEnqueueResult,
│                                               #   IWorkOutbox, WorkDelivery, IWorkHandler, WorkHandlerRegistration,
│                                               #   IRecurringJob, RecurringJobRegistration, RecurringJobContext,
│                                               #   RecurringJobOutcome, WorkFailures, WorkFailureClass, WorkItemState,
│                                               #   WorkRegistrations; CompositionPlane.Worker
├── Iskender.Infrastructure/
│   └── Coordination/                           # SingleInstancePlane.Worker, instance.worker; CoordinationStartup plane map
├── Iskender.Infrastructure.PostgreSql/
│   ├── TableAccess.cs                          # + ScopeDirectoryTable
│   ├── PostgreSqlServiceCollectionExtensions.cs  # + AddIskenderPostgreSqlWork(), AddIskenderPostgreSqlWorkDispatcher();
│   │                                           #   AddIskenderPostgreSqlCoordination() adds the directory module
│   ├── Migrations/PostgreSqlMigrationRunner.cs # Checks the scope directory kind's four policies
│   ├── Directory/                              # New: DirectoryTables, ScopeDirectory, DirectoryScopeResolver,
│   │   └── Scripts/0001_directory.{up,down}.sql   #   DirectoryMigrations
│   ├── Work/                                   # New: WorkTables, WorkMigrations, PostgreSqlWorkOutbox, WorkSettings,
│   │   │                                       #   WorkSectionSchema, WorkDispatcher, WorkDispatcherStartup, ShardLeases,
│   │   │                                       #   WorkClassifier, WorkBackoff, JobRunner, PurgeExpiredJob,
│   │   │                                       #   WorkRetentionJob, WorkLog, WorkTelemetry
│   │   └── Scripts/0001_work.{up,down}.sql
│   └── Coordination/                           # Coordinator inserts register the scope; SettleDirectoryAsync;
│                                               #   AddIskenderPostgreSqlCoordination adds the directory module
├── Iskender.Management/
│   └── WorkerServiceCollectionExtensions.cs, WorkerOptions.cs   # New: AddIskenderWorker(...)
├── Iskender.Infrastructure.Container/
│   ├── ContainerPlaneDefinition.cs             # + ServingCommands; PlaneName worker
│   ├── ContainerServingCommand.cs              # New
│   ├── ContainerEntryPoint.cs                  # Serves a serving command; resolves WorkSettings before binding
│   └── Telemetry/ContainerTelemetry.cs         # + Iskender.Work
├── Iskender.Gateway.Host/Program.cs            # + AddIskenderPostgreSqlWork()
└── Iskender.ControlPlane.Host/Program.cs       # + AddIskenderPostgreSqlWork(); the work serving command

tests/
├── Iskender.Work.Tests/                        # New test project: fixture, Outbox/, Directory/, Dispatch/, Crash/,
│                                               #   Duplicate/, Shards/, Retry/, Skew/, Isolation/, Jobs/, Shutdown/,
│                                               #   Telemetry/
├── Iskender.Work.CrashHost/                    # New test-role console program, killed by the crash tests
├── Iskender.Domain.Tests/                      # Port type tests
├── Iskender.Infrastructure.PostgreSql.Tests/   # Directory kind in the runner's policy check; coordinator registration
├── Iskender.Infrastructure.Tests/              # instance.worker
├── Iskender.Container.Tests/                   # Worker command, readiness, exit codes, stop; LocalEnvironment/ with the worker
├── Iskender.Hosting.Tests/                     # Worker root, duplicate roots
└── Iskender.Contracts.Compatibility.Tests/     # core-contracts 6 baseline

local/
├── compose.yaml                                # + worker
├── config/worker/iskender.yaml, iskender.development.yaml    # New
├── native/worker/iskender.yaml, iskender.development.yaml    # New
├── scripts/smoke.sh, start.sh                  # Worker assertions; dependencies mode stops the worker
└── README.md

Iskender.slnx                                   # + the two test projects
docs/03-domain-and-technical-architecture.md    # Work contract paragraph; repointed contract links
docs/04-technology-stack-and-deployment.md      # Background work runbook; worker in entry points, embedding, local development
specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md
                                                # Error codes table rows for work.* (rule H4)
```

**Structure Decision**: The outbox, dispatcher, and jobs sit in two new framework namespaces of the PostgreSQL adapter, the only project that may name a schema, because every step needs a PostgreSQL unit and a PostgreSQL-issued lease (R1). The worker's root sits beside the control plane's in `Iskender.Management`, and its command in the control-plane executable, so no executable, image, or project is added. The tests get their own project because the crash, duplicate, and isolation suites share one fixture, and the crash host must be a separate process to be killed.

## Implementation approach

The order keeps the build and every test passing at each commit. `tasks.md` orders the same work by user story. Where the two orders differ, `implement` follows `tasks.md`.

1. **Error codes**: the 8 `work.*` codes in `core-contracts` 6, the ISK-0002 table rows, and the harness baseline (R16).
2. **Ports**: the work port types, `WorkRegistrations`, and `CompositionPlane.Worker`, with unit tests (R1).
3. **Directory**: the `directory` module and its policies, `ScopeDirectoryTable`, the runner's check of the kind, `ScopeDirectory`'s register and settle, and `DirectoryScopeResolver`, with policy, race, and malformed-row tests (R2 to R4).
4. **Coordinator registration**: inserts register the scope, `SettleDirectoryAsync`, and the `coordination` module's dependency, with the conformance suite still passing (R3).
5. **Outbox**: the `work` module, `PostgreSqlWorkOutbox`, `AddIskenderPostgreSqlWork()`, the work fixture, and the commit, rollback, dedupe, and failure tests (R5, R14).
6. **Settings**: the `work` section, `WorkSettings` and its cross-field checks (R13).
7. **Dispatcher core**: shard counts, leases, claim, attempt record, apply, failure record, renewal, settle, and the in-process crash, duplicate, paused, and shard tests (R6, R9).
8. **Retry and skew**: classification, backoff, poison, and skew, with their tests (R7, R8).
9. **Crash host**: `tests/Iskender.Work.CrashHost` and the 20-kill-per-point suite (R18).
10. **Jobs**: the job port, `job_runs`, the runner, both jobs, and their tests (R10).
11. **Shutdown**: the start-up, the stop signal, the deadline, cancellation, and lease release, with their tests (R12).
12. **Telemetry**: events, metrics, activities, the canary, and `Iskender.Work` in the container telemetry (R15, R16).
13. **Worker**: `SingleInstancePlane.Worker`, `AddIskenderWorker(...)`, `ServingCommands`, the `work` command in the control-plane host, the outbox in both planes, and the container tests (R11).
14. **Developer topology**: the worker service, its documents, native documents, the smoke script, and the acceptance run (R17).
15. **Publication**: the contracts' link repoints, the `docs/03` paragraph, the `docs/04` runbook and guides, `local/README.md`, and every quickstart scenario run once (R19).

## Requirement coverage

| Requirements | Where they land |
|---|---|
| FR-001, FR-002, FR-003 | `PostgreSqlWorkOutbox`; commit, rollback, failure, and dedupe tests |
| FR-004, FR-005 | `isk_work.items`, the namespace check, the telemetry canary |
| FR-006, FR-007 | `isk_directory.scopes`, `ScopeDirectory`, the coordinator's registration; policy and race tests |
| FR-008 | `DirectoryScopeResolver`; malformed-row test |
| FR-009 | Every unit in the item's scope; isolation tests with the predicate removed |
| FR-010 to FR-014 | `WorkDispatcher`, `ShardLeases`; crash, duplicate, paused, shard, timeout, and cap tests |
| FR-015, FR-018 | The handled-kind filter and skew count; skew and starvation tests |
| FR-016, FR-017 | `WorkClassifier`, `WorkBackoff`; retry and poison tests, and the crash host |
| FR-019 to FR-022 | `JobRunner`, `PurgeExpiredJob`, `WorkRetentionJob`; job tests |
| FR-023, FR-024 | The `FeatureRequirement`s; the refusal test; review and a scan for advisory locks in the work namespaces |
| FR-025 to FR-028 | `AddIskenderWorker(...)`, `ServingCommands`, both `Program.cs` files, `WorkDispatcherStartup`; container and stop tests |
| FR-029 | `WorkSectionSchema`, `WorkSettings`; settings tests |
| FR-030, FR-031 | `local/`; the acceptance run |
| FR-032 to FR-034 | `WorkTelemetry`, `WorkLog`, `ContainerTelemetry`; telemetry tests and canary |
| FR-035 to FR-039 | The six contracts, `core-contracts` 6, the harness, `docs/03`, `docs/04`, XML comments |
| FR-040 | `tests/Iskender.Work.Tests`, `tests/Iskender.Work.CrashHost` |

## Risks

| Risk | Mitigation |
|---|---|
| The register and settle protocol has a race the design missed | A dedicated race test runs settles against concurrent producers many times, with injected pauses between each statement, and asserts no committed item or expiring row is left without its row (SC-005) |
| A producer at Repeatable Read or Serializable loses its directory row in a race | The race test also runs its producers at both stricter levels, and asserts each race ends in `persistence.concurrency_conflict` with nothing committed, never in a missing row (research R3) |
| Directory registration slows coordination writes | Only inserts register, and producers share the row lock. The conformance suite's timing bounds still pass. No figure is asserted |
| Process-kill tests are slow or flaky | The crash host starts with a minimal composition and reports each point on standard output. Tests wait on states with bounds, never on sleeps. A real flake is a blocker for `implement` |
| A handler holds its apply unit's connection for its whole run, exhausting the pool | The in-flight caps' defaults stay below the pool's, and the runbook says to size `persistence.poolMaximum` above `dispatch.concurrency` |
| The worker plane changes behavior that ISK-0007's tests do not name | The worker is a third definition: the data-plane and control-plane definitions are unchanged, and their readiness, stop, and smoke tests must pass unchanged |
| Adding `directory` to an existing database, and rolling it back | The migration tests apply the previous release's modules, then this release's, and check that `directory` and `work` are applied with `coordination` unchanged and readiness `schema_behind` until they are; and that reversing two steps removes `work`, then `directory`, and leaves `coordination` and its rows untouched |
| `core-contracts` 6 breaks a consumer | Additions only, proved by the harness |

## Complexity Tracking

No constitution violations to justify.
