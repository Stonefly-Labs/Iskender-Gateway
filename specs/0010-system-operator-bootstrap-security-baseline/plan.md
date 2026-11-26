# Implementation Plan: ISK-0010 — System-Operator Bootstrap & Security Baseline

**Branch**: `feature/0010-system-operator-bootstrap-security-baseline` | **Date**: 2026-11-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/0010-system-operator-bootstrap-security-baseline/spec.md`

## Summary

Give the control plane a first operator, refuse everything until it has one, check privilege on every admin route, and list every anonymous endpoint. The work has eight parts:

1. **Ports**: the transport-neutral Identity ports in `Iskender.Domain.Identity` (installation state, tokens, redemption, enrollment, authorization) and the audit port in `Iskender.Domain.Telemetry` (R1).
2. **Storage**: two migration modules, `audit` (schema `isk_audit`, a new framework namespace) and `identity` (schema `isk_identity`), with operator tables only, a forward-only installation row, an append-only audit table, and a redemption that serializes on a row lock (R2 to R6, R11, R12).
3. **The admin surface** in `Iskender.Management`: `AddIskenderOperatorSecurity()`, `MapIskenderOperatorSecurity(prefix)`, a pipeline wrapped around every endpoint in the group, the uninitialized gate, principal resolution as a port with no production implementation, grant checks, route declarations checked at start, and the attempt limit (R7 to R10).
4. **The `bootstrap` command** of the control-plane executable, `issue` and `recover`, which prints a token once to standard output after its commit (R6, R14).
5. **Transport**: the `container` section's three new fields, in-process TLS 1.2+ from read-only PEM files, and the refusal to start without protection outside `development`, on both planes (R13).
6. **The anonymous endpoint inventory** and its enforced test (R19).
7. **The developer topology**: the control plane's traffic port on loopback, `bootstrap.sh`, and three new smoke assertions (R20).
8. **Evidence and publication**: the operator fixture and tests, the `operator-security` contract, four contract revisions, `core-contracts` 7, and the `docs/03`, `docs/04`, and `docs/05` changes (R17, R18, R21).

See [research.md](research.md) for the decisions. The maintainer decided five of them in `plan` on 2026-11-21, recorded in the spec's Clarifications and its "Maintainer decisions taken in `plan`" table: the audit schema stays `isk_audit` (R2), `GET /admin/v1/installation` ships as the one protected route (R7), the admin prefix is `/admin/v1` (R7), only unencrypted PEM keys are accepted (R13), and `audit` and `identity` declare dependencies so that a rollback reverses only them (R15).

## Technical Context

**Language/Version**: C# 14 on .NET 10, SDK 10.0.404 per `global.json`, shared framework 10.0.15.

**Primary Dependencies**: None new. `Iskender.Management` takes the shared framework `Microsoft.AspNetCore.App` by `FrameworkReference`, as `Iskender.Hosting` and `Iskender.Infrastructure.Container` already do, so no NuGet package is added. Kestrel's TLS, `System.Security.Cryptography` (`X509Certificate2.CreateFromPemFile`, `RandomNumberGenerator`, `SHA256`, `CryptographicOperations.FixedTimeEquals`), and `System.Threading.RateLimiting` are in the shared framework. The new test project takes only packages already pinned.

**Storage**: PostgreSQL 17, at the ISK-0004 fixture's digest. Two new migration modules: `audit` (schema `isk_audit`, table `records`, depends on `work`) and `identity` (schema `isk_identity`, tables `installation`, `tokens`, `principals`, and `operator_grants`, depends on `audit`). All are operator tables. No product table is added.

**Testing**: xUnit v3 on Microsoft.Testing.Platform under CI check "4. Test": unit tests of the token text, hash, settings, and pipeline; Testcontainers tests through the new operator fixture, with 20 × 50 concurrent redemptions across two hosts; the container tests of the `bootstrap` command, the transport modes, TLS, and the inventory; and the local environment acceptance run. The architecture suite (check 5) runs against rules contract version 5 unchanged, and the compatibility harness against `core-contracts` 7.

**Target Platform**: Linux containers, `linux/amd64` on CI's `ubuntu-24.04` runner and the developer's own architecture locally. Development on Windows, macOS, and Linux.

**Project Type**: Changes to five embeddable libraries, `Iskender.Core.Contracts`, and the control-plane executable, one new test project, and the developer topology, in a modular monolith.

**Performance Goals**: None asserted (Constitution VIII). Every bound in the spec is a safety bound. The 20 runs of SC-002 count test repetitions, not throughput.

**Constraints**:

- No local account, password, session, cookie, OIDC login, tenant role, project grant, API key, management resource API, or management UI (spec Out of Scope).
- No new production project, so no edge, role, home, or rule of the module dependency rules changes, and the rules contract stays at version 5 (R1).
- `Iskender.Hosting`'s public surface and baseline don't change (FR-040). `core-contracts` goes from 6 to 7 with additions only. `providers-abstractions` and `policy-sdk` gain nothing.
- The redemption uses no advisory lock or coordinator (AV-2). No function bypasses row-level security, and the operator scope touches no tenant table.
- Every readiness answer, every stop sequence, and the three smoke readiness answers are unchanged byte for byte (FR-021, FR-036).
- No tracked file holds a token, enrollment code, private key, or certificate. Tests generate certificates at run time.
- No new image, executable, or Dockerfile.
- Builds and runs every test offline after restore, with no cloud credentials, except the container-backed tests, which skip with their reason locally and fail when `CI=true`.
- `.specify/` and `.iskender/grounding.yaml` are unchanged.

**Scale/Scope**: 1 new contract and 4 revisions, about 30 new types, 5 new tables in 2 new schemas, 2 new registration methods and 1 mapping method, 1 new configuration section with 3 fields and 3 new `container` fields, 11 error codes, 1 resource type, 3 admin routes, 8 identity events and 1 container event, 4 metrics, 3 exit codes, 2 command operations, 1 new script, 1 new Compose service, and 1 new test project.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Gate | Status | Notes |
|---|---|---|---|
| I. Product & License | MIT; container and embedded of equal standing | Pass | No package is added. The operator surface is an embeddable registration and mapping, and an embedded control-plane host composes it as the container does (R15) |
| II. Architecture & Topology | Modular monolith; independent planes; PostgreSQL as source of truth; local path | Pass | State, tokens, grants, and audit live in PostgreSQL only. The admin surface is only in `Iskender.Management`, which the data plane cannot reach (rule A6). The worker composes none of it. No project or edge is added. The developer topology runs it without cloud credentials |
| III. Mandatory State | Durable conversation tests | N/A | No conversation code |
| IV. Provider Correctness | Unavailable capabilities fail explicitly | N/A | No provider code. Every refusal has a registered code |
| V. Security & Privacy | Isolation; control-plane auth seams; fail closed; nothing sensitive in telemetry | Pass | The gate runs before authentication and denies on an uninitialized or unknown state (R8). Authorization denies by default and fails closed on an unreadable store (R10). Tokens are 256-bit, hashed, single-use, single-active, and short-lived (R4 to R6). Operator tables only, and a `system_operator` grant reaches no tenant row (R11). Anonymous endpoints are inventoried and enforced by a test (R19). Plaintext transport is refused outside `development` on both planes (R13). Secrets, hashes, and principal IDs never reach telemetry, tested by a canary (R18). The Security owner reviews the operator-security contract, container Listeners, and persistence's audit privileges |
| VI. Policy & Plugins | Typed policies; mandatory authentication | N/A | No policy or plugin code. Mandatory authentication of admin routes is the gate and the resolver port |
| VII. Resilience & Governance | Explicit failure semantics; no silent weakening | Pass | Every failure has a registered code and status. An unknown commit outcome is never retried. The attempt limit is per process, and the token's entropy, not the limit, carries the guarantee (spec Assumptions) |
| VIII. Test & Operational Rigor | Risk-proportionate tests; no invented numbers; rollback docs | Pass | Unit, Testcontainers, two-host concurrency, container, TLS, inventory, canary, and Compose tests. No figure is asserted. The `docs/04` runbook covers issuing, redemption, recovery, TLS, publishing, audit, diagnostics, and rollback: `migrate reverse --steps 2 --confirm-data-loss` removes `identity`, then `audit`, and leaves ISK-0009's modules untouched (R15) |
| IX. Engineering Practice | Versioned surfaces; doc sync; supply-chain hygiene | Pass | Operator-security contract 1, container 3, hosting 4, persistence 4, local environment 3, and `core-contracts` 7. The public contracts and versioning contract gains table rows in place, which its own rules say needs no revision (R17). The docs change in the same PR. R5 and R6 stay unapplied under DEC-20261105-002 |
| X. Spec Kit Discipline | Stage and budget rules | Pass | Run by `/drive-speckit`. `.specify/` is unchanged |
| XI. Human Governance | No silent roadmap change; real evidence | Pass | No roadmap, ADR, constitution, or grounding change. Each contract changes through its own revision procedure, which the approved prompt and the earlier specs' "Left to later specs" tables assign here. Every `plan` decision records the maintainer as decider. The recovery policy stays a draft that awaits human approval (AV-4, FR-025) |
| XII. Enforcement | Owner and gate per principle | Pass | Each contract section names its review owner |

**Post-design re-check**: Pass. The five `plan` decisions add FR-036a and refine TR-6, TR-10, TR-11, TR-13, FR-022a, FR-026, FR-031, and FR-032, and change no gate above. The module dependency rules contract needs no revision (R1), so FR-039's conditional version 6 is not published, and SC-010 still holds.

Four refinements follow from the code, with no choice left to make, and are recorded in research:

- `Iskender.Management` may already take ASP.NET Core under rule A19, so the rules contract stays at version 5 (R1).
- New error codes and resource types are added in place to the public contracts and versioning contract's tables, with no revision (R17).
- A malformed redemption gets the uniform `403` even when the store is down, because the redemptions are exempt from the gate (R8).
- Existing container test documents that start a plane without an environment name must declare a transport mode (R13).

## Project Structure

### Documentation (this feature)

```text
specs/0010-system-operator-bootstrap-security-baseline/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── operator-security.md    # Version 1, new
│   ├── container.md            # Version 3 of ISK-0007's contract
│   ├── hosting.md              # Version 4 of ISK-0006's contract
│   ├── persistence.md          # Version 4 of ISK-0004's contract
│   └── local-environment.md    # Version 3 of ISK-0008's contract
├── checklists/
│   └── requirements.md
└── tasks.md                    # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Iskender.Core.Contracts/
│   ├── Errors/ErrorCodes.cs                     # + 11 operator.* codes, since 7
│   ├── Identifiers/ResourceTypes.cs             # + principal (prin), since 7
│   └── Properties/ContractPackage.cs            # core-contracts 7
├── Iskender.Domain/
│   ├── Identity/                                # New: InstallationStatus, IInstallationState, OperatorTokenPurpose,
│   │                                            #   OperatorTokenText, IssuedOperatorToken, IOperatorTokens,
│   │                                            #   OperatorRequestContext, OperatorActor, RedemptionOutcome,
│   │                                            #   RedemptionFailureReason, IOperatorRedemption, IEnrollmentCodes,
│   │                                            #   OperatorAction(s), OperatorRole(s), OperatorDecision, IOperatorAuthorization
│   └── Telemetry/                               # New: AuditRecord, AuditActor, AuditAction, AuditResult, AuditReason,
│                                                #   IAuditRecorder
├── Iskender.Infrastructure.PostgreSql/
│   ├── PostgreSqlServiceCollectionExtensions.cs # + AddIskenderPostgreSqlIdentity()
│   ├── Identity/                                # New: IdentityTables, IdentityMigrations, OperatorTokenHash, the five stores
│   │   └── Scripts/0001_identity.{up,down}.sql
│   └── Audit/                                   # New framework namespace: AuditTables, AuditMigrations, PostgreSqlAuditRecorder
│       └── Scripts/0001_audit.{up,down}.sql
├── Iskender.Management/
│   ├── Iskender.Management.csproj               # + FrameworkReference Microsoft.AspNetCore.App
│   ├── Security/                                # New: AddIskenderOperatorSecurity, MapIskenderOperatorSecurity,
│   │                                            #   OperatorEndpointConventions, IOperatorPrincipalResolver,
│   │                                            #   OperatorPrincipalResolution, OperatorSettings, OperatorSectionSchema,
│   │                                            #   AdminPipeline, OperatorRouteCheck, OperatorLog, OperatorTelemetry
│   └── Bootstrap/                               # New: BootstrapArguments, BootstrapOperation, BootstrapCommand, BootstrapOutcome
├── Iskender.Infrastructure.Container/
│   ├── ContainerSectionSchema.cs, ContainerSettings.cs, TrafficTransport.cs   # Section version 2; transport and certificate checks
│   ├── ContainerEntryPoint.cs                   # TLS binding; exit codes 8 to 10; event 7008
│   ├── ExitCodes.cs                             # + 8, 9, 10
│   ├── Health/HealthEndpoints.cs                # + Routes
│   ├── Diagnostics/ContainerLog.cs              # + 7008
│   └── Telemetry/ContainerTelemetry.cs          # + Iskender.Identity
├── Iskender.Gateway.Host/DataPlaneDefinition.cs  # The data plane's definition, moved out of Program.cs (CONV-0010-002)
└── Iskender.ControlPlane.Host/ControlPlaneDefinition.cs  # + operator registrations, MapIskenderOperatorSecurity("/admin/v1"), bootstrap command; moved out of Program.cs, and compiled by the inventory test (CONV-0010-002)

tests/
├── Iskender.Identity.Tests/                     # New: Fixture/, Gate/, Redemption/, Concurrency/, Privilege/, Audit/,
│                                                #   Recovery/, Enrollment/, Canary/, Embedding/, and TestOperatorPrincipalResolver
├── Iskender.Container.Tests/                    # Security/anonymous-endpoints.json and the inventory test; Transport/, Bootstrap/;
│                                                #   test documents declare trafficTransport; LocalEnvironment/ smoke changes
├── Iskender.Infrastructure.PostgreSql.Tests/    # Ownership scan of .Identity and .Audit; migration order, privileges, rollback
├── Iskender.Domain.Tests/                       # Token text, port type tests
├── Iskender.Architecture.Tests/                 # No production project references the test resolver
└── Iskender.Contracts.Compatibility.Tests/      # core-contracts 7 baseline

local/
├── compose.yaml                                 # control-plane publishes 127.0.0.1:18090; + bootstrap service (profile)
├── config/data-plane/, config/control-plane/    # Base: trafficTransport upstream; development overlay: none
├── native/data-plane/, native/control-plane/    # The same, for native runs
├── scripts/bootstrap.sh                         # New
├── scripts/smoke.sh, start.sh                   # Admin assertions; the new port check
└── README.md

Iskender.slnx                                    # + tests/Iskender.Identity.Tests
docs/03-domain-and-technical-architecture.md     # Operator-security paragraph; repointed contract links
docs/04-technology-stack-and-deployment.md       # Operator bootstrap runbook; embedding the control plane; local development
docs/05-identity-security-and-privacy.md         # Links the operator-security contract from control-plane identity and authorization
specs/0009-module-events-background-work-coordination/contracts/public-contracts-and-versioning.md
                                                 # Error codes and Resource types rows, in place (rule H4)
```

**Structure Decision**: The ports go in the two module namespaces of `Iskender.Domain` that already exist, the HTTP surface in `Iskender.Management`, the only control-plane library, and storage in the PostgreSQL adapter, the only project that may name a schema. So no project is added, and the admin surface cannot reach the data plane. The tests get their own project because the gate, redemption, concurrency, privilege, audit, and canary suites share one multi-host fixture.

## Implementation approach

The order keeps the build and every test passing at each commit. `tasks.md` orders the same work by user story. Where the two orders differ, `implement` follows `tasks.md`.

1. **Error codes and resource type**: the 11 `operator.*` codes and `principal` in `core-contracts` 7, the in-place table rows, and the harness baseline (R17).
2. **Ports**: the Identity and Telemetry types, with unit tests, including `OperatorTokenText` parsing and its `ToString()` (R1, R4).
3. **Audit storage**: the `audit` module, its revoked privileges, `PostgreSqlAuditRecorder`, and the commit, rollback, privilege, and invalid-record tests (R12).
4. **Identity storage**: the `identity` module, the hash, the installation state, token issue and supersede, redemption, enrollment, and authorization stores, `AddIskenderPostgreSqlIdentity()`, the migration order and rollback tests (R3 to R6, R11, R15).
5. **The operator fixture**: two hosts on one database, the test resolver, and the unreachable store (R21).
6. **Concurrency**: the 20 × 50 two-host runs, and the race between redemption and supersede (R5, SC-002).
7. **Admin surface**: the section, the registration, the mapping, the pipeline, the gate, the resolver port, authorization, the limit, the route check, and the three routes, with the gate, privilege, and declaration tests (R7 to R10, R16).
8. **Diagnostics**: events, metrics, activities, and the canary (R18).
9. **Transport**: the `container` section's version 2, the checks, TLS binding, and the exit-2 tests. Existing test documents declare `upstream` (R13).
10. **Command**: `bootstrap issue` and `bootstrap recover`, exit codes 8 to 10, event 7008, and the control-plane host's registrations and mapping, with the container tests (R6, R14).
11. **Inventory**: `HealthEndpoints.Routes`, `anonymous-endpoints.json`, the inventory test and its negative fixture, and the image probes (R19).
12. **Recovery and enrollment**: the recovery and enrollment tests (R11, User Story 6).
13. **Embedding**: the embedding tests and the refusal under the other roots (R15).
14. **Developer topology**: the port, the documents, `bootstrap.sh`, the smoke assertions, and the acceptance run (R20).
15. **Publication**: the contract link repoints, the `docs/03` paragraph, the `docs/04` runbook and guides, the `docs/05` links, `local/README.md`, and every quickstart scenario run once (FR-039 to FR-041).

## Requirement coverage

| Requirements | Where they land |
|---|---|
| FR-001, FR-002 | `isk_identity.installation`, its revoked privileges, `PostgreSqlInstallationState`; privilege test |
| FR-003 to FR-007 | `PostgreSqlOperatorTokens`, `OperatorTokenText`, `OperatorTokenHash`, `BootstrapCommand`; issue, supersede, expiry, and canary tests |
| FR-008 to FR-011a | `PostgreSqlOperatorRedemption`, `PostgreSqlEnrollmentCodes`; redemption, concurrency, uniform-failure, and enrollment tests |
| FR-012 | `AdminPipeline`'s limiter and body checks; limit and body tests |
| FR-013 to FR-017 | `principals`, `operator_grants`, `PostgreSqlOperatorAuthorization`, `IOperatorPrincipalResolver`, `OperatorRouteCheck`; privilege and declaration tests |
| FR-018, FR-019 | `AdminPipeline`'s gate; gate tests |
| FR-020, FR-021 | `anonymous-endpoints.json`, `HealthEndpoints.Routes`; inventory and readiness tests |
| FR-022 to FR-022b | `ContainerSectionSchema`, `ContainerSettings`, `ContainerEntryPoint`; transport and TLS tests |
| FR-023 to FR-025 | Recovery in the redemption store; recovery tests; the contract's Recovery draft section; `docs/05` unchanged on its Decision status |
| FR-026 to FR-029 | `isk_audit.records`, `PostgreSqlAuditRecorder`; audit and privilege tests |
| FR-030 to FR-033 | The registrations, the mapping, the control-plane `ControlPlaneDefinition.cs`, `OperatorSectionSchema`; container, embedding, and migration tests |
| FR-034 to FR-036a | `local/`; the acceptance run; `GET /admin/v1/installation` |
| FR-037, FR-038 | `OperatorLog`, `OperatorTelemetry`, `ContainerTelemetry`; canary |
| FR-039 to FR-042 | The five contracts, `core-contracts` 7, the harness, `docs/03`, `docs/04`, `docs/05`, XML comments |
| FR-043 | `tests/Iskender.Identity.Tests` |

## Risks

| Risk | Mitigation |
|---|---|
| The row lock does not serialize every race the design expects, such as a redemption against a concurrent supersede | The concurrency suite runs redemptions against concurrent issues as well as against each other, with injected pauses between statements, and asserts at most one principal every time (SC-002) |
| `UseKestrelCore()` rejects `UseHttps` without `UseKestrelHttpsConfiguration()`, or the chiseled image cannot hold an ephemeral key | `listener` mode calls `UseKestrelHttpsConfiguration()`, and the container tests run TLS in the read-only, non-root image (AV-8). A failure is a blocker for `implement` |
| Existing container and local tests start planes without an environment name, and the default `none` refuses them | Every such document declares `upstream` in the same commit as the transport check |
| The default `none` breaks an operator's existing production manifest on upgrade | Intended, and stated in the runbook's upgrade notes: a manifest must declare its transport. Readiness and routes are unchanged otherwise |
| An endpoint mapped outside the admin group on the control plane escapes the gate | The inventory test fails on any anonymous endpoint not listed, and the control-plane host maps only the group |
| A secret or principal ID reaches telemetry through an exception message | Events carry type names only, and the canary searches every captured output |
| The 20 × 50 concurrency runs are slow | One shared fixture database per run, reset between runs. No sleeps. Bounded waits only |
| `core-contracts` 7 breaks a consumer | Additions only, proved by the harness |

## Complexity Tracking

No constitution violations to justify.
