# Implementation Plan: ISK-0005 — Coordination & Redis Capability Contracts

**Branch**: `feature/0005-coordination-redis-capability-contracts` | **Date**: 2026-11-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/0005-coordination-redis-capability-contracts/spec.md`

## Summary

Give every later distributed feature one set of coordination ports, an honest statement of the consistency each coordinator gives, and a start-up check that refuses a feature rather than run it on weaker guarantees. The work has seven parts:

1. **Ports** in `Iskender.Domain.Ports`: the coordination scope, names and key derivation, consistency levels, feature requirements, the capability checker and the gate that every call passes through, atomic operations, leases, fencing tokens and the fencing guard, the versioned cache, failure classes, and the health and credential ports.
2. **Error codes**: 7 `coordination.*` codes in `ErrorCodes`, raising `core-contracts` from 3 to 4, with compatible changes only.
3. **Local substitutes and shared settings** in `Iskender.Infrastructure`: the `coordination` section, in-process implementations declaring `local`, a bounded in-memory cache store, and the single-instance guard.
4. **The PostgreSQL coordinator** in `Iskender.Infrastructure.PostgreSql.Coordination`: `strict` atomic operations, leases, fencing, and cache generations in scope-keyed tables in `isk_coordination`, with tokens from a durable sequence, beside the advisory locks and never on them.
5. **The Redis adapter**, a new embeddable project `Iskender.Infrastructure.Redis`, the only production project that takes `StackExchange.Redis`, declaring `leased` for atomic operations, leases, and cache invalidation, and `best_effort` for fencing.
6. **Tests**: a shared conformance suite run against every implementation, two-client runs for the distributed ones, a Valkey Testcontainers fixture, the capability check's full combination table, and the refusal and lease fencing acceptance groups.
7. **Publication**: the version 1 coordination contract, version 2 of the persistence contract, links from `docs/03` and `docs/04`, a coordination runbook section in `docs/04`, and the ISK-0002 contract's error code and version tables.

The new project matches ISK-0001's `Iskender.Infrastructure.<Adapter>` pattern, so no ISK-0001 rule, edge, or role changes. See [research.md](research.md) for the decisions.

## Technical Context

**Language/Version**: C# 14 on .NET 10, SDK 10.0.403 per `global.json`

**Primary Dependencies**:

- `Iskender.Domain` (ports): none new.
- `Iskender.Infrastructure`: none new. `TimeProvider`, `ActivitySource`, and `Meter` from the base class library.
- `Iskender.Infrastructure.PostgreSql`: none new. It already references `Iskender.Infrastructure`, which gives it the shared section and the in-memory cache store.
- `Iskender.Infrastructure.Redis`: `StackExchange.Redis` 3.4.31 (MIT), with `RESPite` 3.4.31 and `System.IO.Hashing` 10.0.13, all MIT (R1). The license allow-list does not change.
- Tests: `Testcontainers.Redis` 4.16.0 (MIT), in the Redis test project only (R2), beside the pinned `xunit.v3` 4.0.2 and `Testcontainers.PostgreSql` 4.16.0.

**Storage**: PostgreSQL 17 through ISK-0004's ports, with a new `coordination` module and schema `isk_coordination` (R8). Redis-compatible server, tested on Valkey 8 (R2). Neither is required: with no distributed coordinator configured, only local substitutes run.

**Testing**: xUnit v3 on Microsoft.Testing.Platform under CI check "4. Test", with container-backed tests through Testcontainers. The architecture suite and the compatibility harness run unchanged and must pass.

**Target Platform**: Windows, macOS, and Linux development machines, with or without a container runtime, and `ubuntu-24.04` in CI, which provides Docker (AV-7). Both images are pinned by multi-architecture digest.

**Project Type**: Embeddable domain ports, an embeddable infrastructure library, and two embeddable infrastructure adapters, in a modular monolith.

**Performance Goals**: None asserted (Constitution VIII). Lease bounds, timeouts, size limits, the instance lease duration, and the probe's 2-second allowance are safety bounds.

**Constraints**:

- Builds and runs the non-container tests offline after restore, with no credentials (SC-001).
- Restores in `--locked-mode`: every new and changed project commits its lock file (rule B2), with versions in `Directory.Packages.props` (rule B1).
- No scope ID, key, hash, cached value, host, or credential in any output (FR-026). The Redis client's logging stays off.
- No session-scoped lock, and the persistence contract's Advisory locks section byte-for-byte unchanged (FR-018, SC-008).
- No host wiring, rate algorithm, quota window, budget, breaker, or worker (SC-008).
- This PC restores through its user-level proxy feed, which must never appear in a committed `nuget.config` or in CI.

**Scale/Scope**: 4 primitives, 4 consistency levels, 3 coordinator kinds, 6 capability reasons (5 refusals), 7 error codes, 7 health reasons, 9 failure classes, 11 log events, 5 metrics, 20 configuration fields, 4 PostgreSQL tables and 1 sequence.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Gate | Status | Notes |
|---|---|---|---|
| I. Product & License | MIT; no open-core withholding | Pass | Every new production package is MIT (R1). The test server, Valkey, is BSD-3-Clause and test-only (R2). Every coordinator ships in the open-source distribution |
| II. Architecture & Topology | Boundaries and direction enforced; optional Redis never silently weakens; local path builds without cloud credentials | Pass | Ports in `Iskender.Domain.Ports`, Redis only in `Iskender.Infrastructure.Redis` (rules A5, A19 unchanged). Redis is optional for correctness, because PostgreSQL provides `strict` coordination (TR-1). The capability check refuses rather than weakens (R5). No cloud SDK |
| III. Mandatory State | Durable conversation tests | N/A | No conversation code |
| IV. Provider Correctness | Unavailable features fail explicitly | N/A | No provider code. The same honesty rule is applied to coordinators: each declares only what it can show (R9) |
| V. Security & Privacy | Isolation tests; fail closed; telemetry redaction | Pass | Trusted scopes only (R4), hashed keys with no tenant IDs in Redis, scope-keyed tables with forced row-level security as a second layer (R6), mandatory paths cannot degrade, verified TLS by default, secret references and a redacting credential type (R11), and a canary over every output and the key space (SC-005) |
| VI. Policy & Plugins | Typed policies; no competing implementation | N/A | No policy code. ISK-0060 and ISK-0061 build their policies on these ports |
| VII. Resilience & Governance | Explicit failure semantics; no silent weakening; fail-closed hard budget | Pass | Declared levels per primitive, a fixed failure classification with unknown outcomes marked and never retried, fencing tokens checked by the store, two-client race tests, and `degrade` refused on mandatory paths |
| VIII. Test & Operational Rigor | Risk-proportionate tests; no invented numbers; rollback docs | Pass | Unit, property-style combination, integration, Testcontainers, concurrency, fault, and security tests. Image digests are read during `implement`, never invented. The `coordination` migration has a reverse script under ISK-0004's rules. No performance figure is asserted |
| IX. Engineering Practice | Versioned surfaces; doc sync; supply-chain hygiene | Pass | `core-contracts` 3 to 4, coordination contract version 1, persistence contract version 2, docs in the same PR, packages centrally pinned and lock-file hashed. The Redis client was a recommendation; this spec selects it and records why (R1). R5 and R6 stay unapplied under DEC-20261105-002 |
| X. Spec Kit Discipline | Stage and budget rules | Pass | Run by `/drive-speckit`. `.specify/` is unchanged |
| XI. Human Governance | No silent roadmap change; real evidence | Pass | No roadmap, ADR, constitution, or ISK-0001 change. The persistence contract changes through its own revision procedure. Every decision is recorded with the maintainer as decider |
| XII. Enforcement | Owner and gate per principle | Pass | Each contract section names its review owner |

**Post-design re-check**: Pass. The maintainer decided two judgments on 2026-11-11, both recorded in research:

- Scope-keyed tables with forced row-level security for the PostgreSQL coordinator, added as a third table kind in persistence contract version 2 (R6). The spec's FR-004 and FR-024 are updated to match.
- The Redis adapter's declared levels: `leased` for atomic operations, leases, and cache invalidation, and `best_effort` for fencing, so a feature that needs fenced leases at `leased` or above takes them from PostgreSQL (R9, settling AV-3).

During `checklist`, the maintainer decided that the fencing guard accepts PostgreSQL leases only, and that the purge takes a scope (R22). Neither changes a gate above.

## Project Structure

### Documentation (this feature)

```text
specs/0005-coordination-redis-capability-contracts/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── coordination.md           # Version 1
│   └── persistence.md            # Version 2 of ISK-0004's contract
├── checklists/
│   └── requirements.md
└── tasks.md                      # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Iskender.Core.Contracts/
│   ├── Properties/ContractPackage.cs        # core-contracts 3 → 4
│   └── Errors/ErrorCodes.cs                 # + 7 coordination.* codes, since 4
├── Iskender.Domain/
│   └── Ports/                               # Existing namespace Iskender.Domain.Ports
│       ├── CoordinationScope.cs, CoordinationName.cs, CacheNamespace.cs, CoordinationKey.cs
│       ├── ConsistencyLevel.cs, CoordinationPrimitive.cs, CoordinatorKind.cs, CoordinatorCapabilities.cs
│       ├── CoordinationTopology.cs, OutageBehavior.cs, FeatureRequirement.cs, SingleInstanceState.cs
│       ├── CapabilityReason.cs, FeatureCapability.cs, CapabilityReport.cs, CoordinationCapabilityChecker.cs
│       ├── CoordinationGate.cs, FeatureCoordination.cs
│       ├── IAtomicOperations.cs, CoordinationValue.cs, Reservation.cs
│       ├── ILeaseCoordinator.cs, Lease.cs, LeaseWait.cs, LeaseAcquisition.cs, FencingToken.cs, IFencingGuard.cs
│       ├── IVersionedCache.cs, CacheLookup.cs, CacheStamp.cs
│       ├── CoordinationFailureClass.cs, ICoordinationHealthProbe.cs, CoordinationHealthResult.cs
│       └── ICoordinationCredentialProvider.cs, CoordinationCredential.cs, ISingleInstanceStatus.cs
├── Iskender.Infrastructure/
│   └── Coordination/                        # New, namespace Iskender.Infrastructure.Coordination
│       ├── CoordinationSectionSchema.cs, CoordinationSettings.cs
│       ├── LocalAtomicOperations.cs, LocalLeaseCoordinator.cs, LocalVersionedCache.cs, InMemoryCacheStore.cs
│       ├── SingleInstanceGuard.cs
│       └── CoordinationLog.cs, CoordinationTelemetry.cs
├── Iskender.Infrastructure.PostgreSql/
│   ├── TableAccess.cs                       # + ScopeKeyedTable
│   ├── Migrations/PostgreSqlMigrationRunner.cs   # Policy check covers scope_key tables
│   └── Coordination/                        # New framework namespace, isk_coordination only
│       ├── CoordinationMigrations.cs, Scripts/0001_coordination.up.sql, .down.sql
│       ├── CoordinationTables.cs
│       ├── PostgreSqlAtomicOperations.cs, PostgreSqlLeaseCoordinator.cs, PostgreSqlVersionedCache.cs
│       └── PostgreSqlFencingGuard.cs, PostgreSqlCoordinationHealth.cs, CoordinationMaintenance.cs
└── Iskender.Infrastructure.Redis/           # New, embeddable, packable
    ├── Iskender.Infrastructure.Redis.csproj # StackExchange.Redis; refs Infrastructure, Domain, Core.Contracts
    ├── packages.lock.json
    ├── RedisConnection.cs, RedisScripts.cs, RedisFailureClassifier.cs
    ├── RedisAtomicOperations.cs, RedisLeaseCoordinator.cs, RedisVersionedCache.cs
    └── RedisHealthProbe.cs

tests/
├── Iskender.Coordination.Conformance/       # New class library: abstract conformance tests, ICoordinatorUnderTest
├── Iskender.Infrastructure.Tests/           # New: local substitutes, section schema, in-memory store
├── Iskender.Infrastructure.PostgreSql.Tests/
│   └── Coordination/                        # PostgreSQL coordinator, fencing guard, scope-keyed RLS, instance lease
├── Iskender.Infrastructure.Redis.Tests/     # New: Valkey fixture, Redis adapter, outage classification
├── Iskender.Domain.Tests/
│   └── Ports/                               # Checker combination table, key vectors, names, scopes, lease arithmetic
├── Iskender.Core.Contracts.Tests/
│   └── SolutionBuildsTests.cs               # Allows the new projects
└── Iskender.Contracts.Compatibility.Tests/
    └── baselines/                           # core-contracts.surface.txt, registries.json regenerated

Directory.Packages.props                     # + StackExchange.Redis 3.4.31, Testcontainers.Redis 4.16.0
Iskender.slnx                                # + the four new projects
.github/CODEOWNERS                           # + the new projects
docs/03-domain-and-technical-architecture.md # Coordination and persistence v2 links (FR-032)
docs/04-technology-stack-and-deployment.md   # Links and a "Coordination" runbook section
specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md
                                             # Error codes and versioning tables only (FR-023)
```

**Structure Decision**: Ports join ISK-0004's in `src/Iskender.Domain/Ports/`, the home ISK-0001 gives shared coordination ports. Local substitutes and the shared section live in `Iskender.Infrastructure`, which both adapters already reference and which takes no driver. The PostgreSQL coordinator lives in the existing adapter because it needs the unit of work's internal command API, under a framework namespace that persistence contract version 2 permits. The Redis adapter is a new project under the approved pattern. The conformance suite is a separate class library, so the three implementation test projects share one set of tests.

## Implementation approach

The order keeps the build and every test passing for each commit.

1. **Packages**: add the two package versions, and prove `check-license.sh` passes once `StackExchange.Redis` is restored.
2. **Error codes and version**: add the 7 codes with `since: 4`, raise `core-contracts` to 4, and update the ISK-0002 contract's two tables and the harness baselines in the same commit.
3. **Ports, pure parts**: scope, names, key derivation with published vectors, levels, requirements, the checker with its full combination table, the gate, tokens, and lease arithmetic, with unit tests in `Iskender.Domain.Tests/Ports/`.
4. **Ports, I/O parts**: the atomic operations, lease, fencing guard, cache, health, and credential interfaces.
5. **Conformance suite**: the class library with the abstract tests and the `ICoordinatorUnderTest` seam.
6. **Section and local substitutes**: the `coordination` schema, settings, the local implementations, and the in-memory store, passing the suite in the new `Iskender.Infrastructure.Tests`.
7. **Persistence contract version 2 in code**: `ScopeKeyedTable`, the extended policy check, and the module-namespace check for `.Coordination`, with tests on ISK-0004's fixture, including row-level security with the predicate omitted.
8. **The PostgreSQL coordinator**: the `coordination` migration and reverse, tables, sequence, the three ports, the fencing guard, maintenance, and health, passing the suite with two data sources.
9. **The single-instance guard** on the PostgreSQL lease, with the two-process refusal and lost-lease tests.
10. **The Redis adapter project**: lock file, solution and CODEOWNERS entries, connection, TLS, credentials, scripts, the three ports, the classifier, and the probe, with the Valkey fixture (digest read from the registry), passing the suite with two multiplexers, then the stop, pause, and replica-role tests.
11. **Acceptance runs**: the refusal and lease fencing groups 20 times, and the reservation group 20 times on each distributed implementation.
12. **Diagnostics**: the 11 events, activities, metrics, and the canary across the suite and the Redis key space.
13. **Publication**: the two contracts linked from `docs/03` and `docs/04`, the runbook section, and a final check that every quickstart scenario passes.

## Requirement coverage

| Requirements | Where they land |
|---|---|
| FR-001, FR-002, FR-002a | `Iskender.Domain.Ports`; `Iskender.Infrastructure.Coordination`; `Iskender.Infrastructure.PostgreSql.Coordination`; `Iskender.Infrastructure.Redis`; the ISK-0001 suite; the module-namespace check |
| FR-003, FR-004 | `CoordinationScope`, `CoordinationKey`; scope, vector, and key-space canary tests |
| FR-005 to FR-010, FR-007a | Capability declarations, `FeatureRequirement`, `CoordinationCapabilityChecker`, `CoordinationGate`, `SingleInstanceGuard`; the combination table and the refusal group |
| FR-011 to FR-013 | `IAtomicOperations` and its three implementations; conformance and reservation tests |
| FR-014 to FR-018 | `ILeaseCoordinator`, `Lease`, `FencingToken`, `PostgreSqlFencingGuard`; the lease fencing group; the Advisory locks diff |
| FR-019 to FR-021, FR-019a | `IVersionedCache` and its implementations, `InMemoryCacheStore`; cache conformance tests |
| FR-022 | The classifiers; stop, pause, and unknown-outcome tests |
| FR-023 | `ErrorCodes`; the ISK-0002 contract tables; the harness; one test per code |
| FR-024 | Persistence contract version 2; `ScopeKeyedTable`; the `coordination` migration module; the policy check |
| FR-025, FR-026 | The health probes; reason-code, timing, and canary tests |
| FR-027 | `CoordinationSectionSchema`; configuration tests |
| FR-028 | `CoordinationLog`, `CoordinationTelemetry`; event, activity, and metric tests |
| FR-029 to FR-031 | `Iskender.Coordination.Conformance`; the Redis fixture; skip-or-fail tests |
| FR-032 | `contracts/coordination.md`, `contracts/persistence.md`; `docs/03`, `docs/04` |

## Risks

| Risk | Mitigation |
|---|---|
| A lease or reservation race test is flaky | SC-003 and SC-004 need 20 consecutive passes. Tests start clients behind a barrier and assert on final state and recorded tokens, never on timing. A real flake is a blocker for `implement`, not a reason to lower the count |
| Expiry tests are slow or timing-sensitive | The local substitutes use a fake `TimeProvider`. Distributed tests use short durations within the configured minimum and wait on observed expiry, with generous bounds |
| The Redis client behaves differently on cancellation than expected | R11 treats every cancelled write as an unknown outcome, so a misjudgment can only make a result more cautious. A test covers it |
| Pausing a container to force timeouts is unreliable on some runtimes | Use `docker pause` through Testcontainers where supported, and otherwise a blackholing proxy. If neither works on the CI runner, report a blocker rather than skip the test |
| Scope-keyed row-level security slows coordination on PostgreSQL | No figure is asserted. Redis exists for throughput, and later specs measure (Constitution VIII) |
| A Testcontainers or Redis client dependency gets a vulnerability, failing check 9 | Pin the latest patched version at `implement`. If none is clean, report a blocker rather than suppress the check |
| The Valkey image digest is removed from the registry | The digest is of an official image index. A failed pull is the skip or failure reason, and a later change updates the digest with a recorded lookup |

## Complexity Tracking

No constitution violations to justify.
