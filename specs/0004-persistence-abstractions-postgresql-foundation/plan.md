# Implementation Plan: ISK-0004 — Persistence Abstractions & PostgreSQL Foundation

**Branch**: `feature/0004-persistence-abstractions-postgresql-foundation` | **Date**: 2026-11-09 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/0004-persistence-abstractions-postgresql-foundation/spec.md`

## Summary

Give every later storage spec one tenant-isolated, transactional, migratable PostgreSQL foundation. The work has six parts:

1. **Ports** in `Iskender.Domain.Ports`: the trusted unit-of-work scope, the unit of work and its factory, isolation, advisory locks and their key derivation, version tokens, the retry helper, the failure classes recorded on a faulted unit (failures themselves are ISK-0002 `GatewayException`s), and the migration, health, and database-credential ports. No driver type appears in any signature.
2. **Error codes**: 12 `persistence.*` codes in `ErrorCodes`, raising `core-contracts` from 2 to 3, with compatible changes only.
3. **The adapter**, a new embeddable project `Iskender.Infrastructure.PostgreSql`, the only production project that takes `Npgsql`: the `persistence` configuration section, data sources with per-connection credentials and verified TLS, the unit of work with transaction-local scope settings and server-side timeouts, the failure classifier, table descriptors and the operator-scope pre-check, optimistic concurrency, transaction-scoped advisory locks, the migration runner, the health probe, and the diagnostics.
4. **Isolation in the database**: row-level security forced on every tenant-owned table, keyed on the unit's transaction-local settings, checked automatically, with a runtime role that owns nothing.
5. **Tests**: a new `tests/Iskender.Infrastructure.PostgreSql.Tests` project with the shared Testcontainers fixture, two test-only sample modules with 7 migrations, the migration round-trip and parallel-transaction acceptance tests, the isolation, health, canary, and compatibility-window tests, and the module-namespace check. Pure port logic is tested in `tests/Iskender.Domain.Tests`.
6. **Publication**: the version 1 persistence contract, links from `docs/03` and `docs/04`, an operator runbook section in `docs/04`, the ISK-0002 contract's error code and version tables, and the license allow-list extended with `PostgreSQL`.

The project name matches ISK-0001's `Iskender.Infrastructure.<Adapter>` pattern, so no ISK-0001 rule, edge, or role changes. See [research.md](research.md) for the decisions.

## Technical Context

**Language/Version**: C# 14 on .NET 10, SDK 10.0.403 per `global.json`

**Primary Dependencies**:

- `Iskender.Domain` (ports): none new. Base class library plus the existing `Iskender.Core.Contracts` types.
- `Iskender.Infrastructure.PostgreSql`: `Npgsql` 10.0.3 (SPDX `PostgreSQL`, approved for the allow-list by the Product/Charter owner on 2026-11-09, research R1). Its only `net10.0` dependency, `Microsoft.Extensions.Logging.Abstractions`, is already pinned at 10.0.14. No data-access library (R2) and no migration library (R3). Activities and metrics use the base class library's `ActivitySource` and `Meter` (R18).
- Tests: the pinned `xunit.v3` 4.0.2, plus `Testcontainers.PostgreSql` 4.16.0 (MIT) in the new test project only (R19).

**Storage**: PostgreSQL 17, the tested and oldest supported major version (R19, AV-6). One database per deployment, one schema per module (`isk_<module>`), and the framework's `isk_migrations` schema. This spec creates only the history table and test-only fixture tables.

**Testing**: xUnit v3 on Microsoft.Testing.Platform under CI check "4. Test", with container-backed integration tests through Testcontainers. The architecture suite and the compatibility harness run unchanged and must pass.

**Target Platform**: Windows, macOS, and Linux development machines, with or without a container runtime, and `ubuntu-24.04` in CI, which provides Docker (AV-7). The PostgreSQL image is pinned by its multi-architecture digest, so arm64 and amd64 both run it.

**Project Type**: Embeddable domain ports and an embeddable infrastructure adapter, in a modular monolith.

**Performance Goals**: None asserted (Constitution VIII). Timeouts, pool sizes, the 3-attempt retry limit, the 60-second unit default, and the probe's 2-second allowance are safety bounds.

**Constraints**:

- Builds and runs the non-container tests offline after restore, with no credentials. Container tests need only the pinned image (SC-001).
- Restores in `--locked-mode`: the new and changed projects commit lock files (rule B2), with versions in `Directory.Packages.props` (rule B1).
- `Iskender.Domain` takes no new package, and every new type there sits in `Iskender.Domain.Ports`, which uses no module namespace (rules A18, A19).
- No password, host, database name, user name, SQL, or server text in any output (FR-026). Npgsql's own logging and tracing stay off.
- No host wiring, no migration command, no organization or other real module table (SC-008).
- This PC restores through its user-level proxy feed, which must never appear in a committed `nuget.config` or in CI.

**Scale/Scope**: 4 port groups (unit of work, migrations, health, credentials), 12 error codes, 10 health reasons, 9 failure classes, 14 log events, 5 metrics, 14 configuration fields, and 2 test modules with 7 migrations.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Gate | Status | Notes |
|---|---|---|---|
| I. Product & License | MIT; no open-core withholding | Pass | Npgsql's `PostgreSQL` license is OSI-approved and permissive. The Product/Charter owner approved adding it to the allow-list on 2026-11-09 (R1), as `docs/04` permits. Testcontainers is MIT and test-only |
| II. Architecture & Topology | Boundaries and direction enforced; builds without cloud credentials | Pass | Ports in `Iskender.Domain.Ports`, driver only in `Iskender.Infrastructure.PostgreSql`, which matches an approved pattern (A5, A19 unchanged). PostgreSQL is the source of truth. No cloud SDK. Migrations are an explicit operation, so control and data planes stay independent |
| III. Mandatory State | Durable conversation tests | N/A | No conversation code. ISK-0044 and ISK-0045 build on these ports |
| IV. Provider Correctness | Unavailable features fail explicitly | N/A | No provider code |
| V. Security & Privacy | Isolation tests; fail closed; telemetry redaction | Pass | Trusted scopes only (R4), row-level security as a forced second layer (R5), operator pre-check (R6), foreign rows reported as missing, verified TLS by default (R15), secret references and a redacting credential type (R17), a canary test over every output (SC-005), and a runtime role with no DDL or bypass rights |
| VI. Policy & Plugins | Typed policies; no competing implementation | N/A | No policy code |
| VII. Resilience & Governance | Explicit failure semantics; no silent weakening | Pass | Fixed failure classes, bounded retry only when the caller says it is safe, unknown commit outcomes never retried, advisory locks documented as transaction-local and not leases (FR-028). Leases and fencing stay with ISK-0005 |
| VIII. Test & Operational Rigor | Risk-proportionate tests; no invented numbers; rollback docs | Pass | Testcontainers, integration, concurrency, security, and architecture tests. The rollback round trip is acceptance evidence. A runbook covers migration, rollback, backup, and health. The image digest is read from the registry during `implement`, never invented. No performance figure is asserted |
| IX. Engineering Practice | Versioned surfaces; doc sync; supply-chain hygiene | Pass | `core-contracts` 2 to 3 with version tags. The contract, `docs/03`, and `docs/04` change in the same PR. Packages are centrally pinned and lock-file hashed. Npgsql and Dapper were recommendations; this spec selects Npgsql and records why. R5 and R6 stay unapplied under DEC-20261105-002 |
| X. Spec Kit Discipline | Stage and budget rules | Pass | Run by `/drive-speckit`. `.specify/` is unchanged |
| XI. Human Governance | No silent roadmap change; real evidence | Pass | No roadmap, ADR, constitution, or ISK-0001 change. The ISK-0002 contract changes only in its error codes and versioning tables. Both plan-stage decisions are recorded with the maintainer as decider |
| XII. Enforcement | Owner and gate per principle | Pass | Each contract section names its review owner |

**Post-design re-check**: Pass. The maintainer reviewed five judgments on 2026-11-09 and accepted the recommendation for each, all recorded in research:

- Kept: `UnitOfWorkOptions.Timeout` as the unit's own bound, since TR-13 has no unit timeout field (R7).
- Kept: `core.internal` for the unexpected class and `persistence.unavailable` for authentication failure, since TR-15 lists no codes for them (R8).
- Changed: ports fail only with ISK-0002's `GatewayException`, and the failure class is recorded on the faulted unit, rather than in a separate `PersistenceException` (R9).
- Changed: a ninth health reason, `insufficient_privilege`, replaces reusing `schema_behind` for missing grants. The spec's TR-11, User Story 4, edge cases, and SC-006 are updated to match (R13).
- Kept: a hand-written migration runner rather than a library (R3).

**Checklist decisions**: the maintainer decided four checklist items on 2026-11-10, recorded in the spec, contract, and research: a project unit sees its organization's organization-owned rows and an organization unit sees no project-owned row (FR-006); migration failures carry module, version, and reason as `ErrorDiagnostics` values (R9); a tenth health reason, `unexpected` (TR-11, R13); and ISK-0007 owns the readiness mapping and stays ready during `migration_in_progress` (contract Health rule 4).

## Project Structure

### Documentation (this feature)

```text
specs/0004-persistence-abstractions-postgresql-foundation/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── persistence.md            # Version 1
├── checklists/
│   └── requirements.md
└── tasks.md                      # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Iskender.Core.Contracts/
│   ├── Properties/ContractPackage.cs        # core-contracts 2 → 3
│   └── Errors/ErrorCodes.cs                 # + 12 persistence.* codes, since 3
├── Iskender.Domain/
│   └── Ports/                               # New, namespace Iskender.Domain.Ports
│       ├── UnitOfWorkScope.cs, UnitOfWorkOptions.cs, UnitOfWorkIsolation.cs, UnitOfWorkState.cs
│       ├── IUnitOfWorkFactory.cs, IUnitOfWork.cs, UnitOfWorkRetry.cs
│       ├── AdvisoryLockName.cs, AdvisoryLockWait.cs, AdvisoryLockKey.cs, VersionToken.cs
│       ├── PersistenceFailure.cs, PersistenceFailureClass.cs
│       ├── IMigrationRunner.cs, MigrationIdentity.cs, MigrationRequests.cs, MigrationStatus.cs, SchemaVersionRange.cs
│       ├── IPersistenceHealthProbe.cs, PersistenceHealthResult.cs
│       └── IDatabaseCredentialProvider.cs, DatabaseCredential.cs
└── Iskender.Infrastructure.PostgreSql/      # New, embeddable, packable
    ├── Iskender.Infrastructure.PostgreSql.csproj   # Npgsql; refs Infrastructure, Domain, Core.Contracts
    ├── packages.lock.json
    ├── PostgreSqlSectionSchema.cs, PostgreSqlSettings.cs, PostgreSqlDataSources.cs
    ├── PostgreSqlUnitOfWorkFactory.cs, PostgreSqlUnitOfWork.cs, FailureClassifier.cs
    ├── TableAccess.cs, VersionedRows.cs
    ├── Migrations/                          # MigrationModule, MigrationScript, MigrationSet,
    │                                        #   SchemaReferenceScanner, PostgreSqlMigrationRunner, MigrationLock
    ├── Health/PostgreSqlHealthProbe.cs
    └── Diagnostics/PersistenceLog.cs, PersistenceTelemetry.cs

tests/
├── Iskender.Infrastructure.PostgreSql.Tests/   # New
│   ├── Fixture/                             # PostgreSqlFixture (assembly fixture), per-class databases,
│   │                                        #   init roles script, RequirePostgreSql, test credential provider,
│   │                                        #   catalog snapshot, capturing logger/listener, canary
│   ├── Modules/Alpha/, Modules/Beta/        # Test-only modules: descriptors, repositories, embedded SQL
│   ├── Modules/Invalid/                     # Invalid migration sets
│   ├── Snapshots/                           # Expected catalog text per forward step
│   └── *Tests.cs                            # unit of work, isolation, classifier, concurrency, locks,
│                                            #   migrations, round trip, health, configuration, canary,
│                                            #   compatibility window, module-namespace check
├── Iskender.Domain.Tests/
│   └── Ports/*Tests.cs                      # New: scope factories, options, retry, lock key vectors, version token
├── Iskender.Core.Contracts.Tests/
│   └── SolutionBuildsTests.cs               # Allows src/Iskender.Domain/Ports/ and the adapter (R23)
└── Iskender.Contracts.Compatibility.Tests/
    └── baselines/                           # core-contracts.surface.txt, registries.json regenerated

Directory.Packages.props                     # + Npgsql 10.0.3, Testcontainers.PostgreSql 4.16.0
Iskender.slnx                                # + the two new projects
.github/CODEOWNERS                           # + both new projects
.github/scripts/check-license.sh             # + PostgreSQL in allowed_licenses
docs/03-domain-and-technical-architecture.md # Contract link in Persistence (FR-037)
docs/04-technology-stack-and-deployment.md   # Contract link, "Database migrations and rollback" runbook
specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md
                                             # Error codes and versioning tables only (FR-030)
```

**Structure Decision**: Ports go under `src/Iskender.Domain/Ports/`, the home ISK-0001 gives shared persistence ports. The adapter is a new project under the approved `Iskender.Infrastructure.<Adapter>` pattern, so no contract revision is needed. Its framework code sits in the root, `.Migrations`, `.Health`, and `.Diagnostics` namespaces, leaving `Iskender.Infrastructure.PostgreSql.<Module>` free for later modules' repositories. A new test project holds the container fixture, because later storage specs reuse it, and it needs packages no existing test project should take.

## Implementation approach

The order matters, because each step keeps the build and every test passing for the next commit.

1. **License and packages first**: add `PostgreSQL` to `allowed_licenses`, add the two package versions, and prove `check-license.sh` passes once Npgsql is restored.
2. **Error codes and version**: add the 12 codes with `since: 3`, raise `core-contracts` to 3, and update the ISK-0002 contract's two tables and the baselines in the same commit, so the harness passes.
3. **Ports**: add `Iskender.Domain.Ports` with unit tests in `Iskender.Domain.Tests/Ports/`, and add the prefix to `SolutionBuildsTests`.
4. **Adapter project**: create `Iskender.Infrastructure.PostgreSql` with its lock file, solution entry, CODEOWNERS entry, and `SolutionBuildsTests` prefix. Run the architecture suite to confirm A5, A19, P1, and P3.
5. **Test project and fixture**: create the test project, read the PostgreSQL 17 image digest from the registry, write the fixture, roles, templates, per-class databases, and the skip or fail behavior. Prove it with one trivial container test, and with `CI=true` and no runtime.
6. **Configuration and data sources**: the section schema, settings, TLS mode, and the credential port wiring, with configuration and canary tests.
7. **Unit of work**: scope settings, isolation, commit and rollback, bounds and cancellation, the concurrent-use guard, the classifier, and connection-reuse tests.
8. **Isolation**: table descriptors, the operator pre-check and its test seam, row-level security in the fixture modules, the policy check, and the cross-scope tests with and without the predicate.
9. **Concurrency**: version tokens, the retry helper against real serialization failures, and advisory locks, with the parallel tests run 20 times.
10. **Migrations**: scripts, checksums, the set validator and lexer, ordering, the runner, history, the lock, reverse steps, the data-losing flag, the policy check after each module, the round trip with snapshots, and the concurrent-migrator test.
11. **Health**: the probe and every reason code, timing within the allowance, and the compatibility-window tests.
12. **Diagnostics and the namespace check**: the 14 events, activities, metrics, the canary across the whole suite, and the module-namespace check.
13. **Publication**: the `docs/03` and `docs/04` links and runbook, and a final check that every quickstart scenario passes.

## Requirement coverage

| Requirements | Where they land |
|---|---|
| FR-001 to FR-003 | `Iskender.Domain.Ports`; `Iskender.Infrastructure.PostgreSql`; the ISK-0001 suite; the module-namespace check; XML comments |
| FR-004, FR-005 | `UnitOfWorkScope`, `PostgreSqlUnitOfWorkFactory`; scope tests |
| FR-006 to FR-008, FR-010 | `TableAccess`, the scope settings, row-level security in the fixture modules, the policy check; isolation and reuse tests |
| FR-009 | `PostgreSqlUnitOfWork` bounds; timeout, deadline, pool-exhaustion, and server-cancel tests |
| FR-011 to FR-016 | `PostgreSqlUnitOfWork`, `FailureClassifier`, `UnitOfWorkRetry`, `VersionedRows`; classifier and concurrency tests |
| FR-017 to FR-024 | `Migrations/`; set validation, round trip, failure, checksum, lock, identity, reverse, and compatibility-window tests |
| FR-025, FR-026 | `PostgreSqlHealthProbe`; reason-code, timing, and canary tests |
| FR-027, FR-028 | `AdvisoryLockKey`, `IUnitOfWork.AcquireAdvisoryLockAsync`; lock tests; the contract's Advisory locks section |
| FR-029 | `PostgreSqlSectionSchema`; configuration tests |
| FR-030 | `ErrorCodes`; the ISK-0002 contract tables; the harness; one test per code |
| FR-031 | `IDatabaseCredentialProvider`, `PostgreSqlDataSources`; missing-provider and canary tests |
| FR-032 | `Diagnostics/`; event, activity, and metric tests |
| FR-033 to FR-035 | `Fixture/`; parallel-class, digest, and skip-or-fail tests |
| FR-036 | The round-trip and parallel groups (quickstart section 2) |
| FR-037 | `contracts/persistence.md`; `docs/03`, `docs/04` |

## Risks

| Risk | Mitigation |
|---|---|
| A serializable or optimistic test is flaky under parallel load | SC-003 needs 20 consecutive passes, so flakiness shows before merge. Tests synchronize starts with a barrier and assert on final state, not timing. A real flake is a blocker for `implement`, not a reason to lower the count |
| Npgsql's pool resets session state in a way that hides a leak the test should catch | The reuse test forces one physical connection (`poolMaximum` 1) and checks settings, temporary tables, and advisory locks directly from the catalog |
| `statement_timeout` and a client cancel race, giving the wrong code | The classifier decides timeout or cancelled from which token fired, not from the SQLSTATE alone, and a test covers each |
| The digest-pinned image is removed from the registry | The digest is of an official image index. If a pull fails, the fixture reports it as the skip or failure reason, and a later change updates the digest with a recorded lookup |
| A Testcontainers dependency arrives with a vulnerability, failing check 9 | Pin the latest patched version at `implement`. If none is clean, report a blocker rather than suppressing the check |
| Row-level security policies slow later modules | No figure is asserted. Later specs measure, as Constitution VIII requires |
| Verified TLS blocks a database with a private CA outside development | Documented as a known limit. A CA bundle setting is post-MVP |
| The schema lexer misses a reference in dynamic SQL | Documented as a known limit, with review coverage |

## Complexity Tracking

No constitution violations to justify.
