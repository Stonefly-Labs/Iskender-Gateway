# Implementation Plan: ISK-0008 — Local Developer Environment & Reproducible Compose

**Branch**: `feature/0008-local-developer-environment-reproducible-compose` | **Date**: 2026-11-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/0008-local-developer-environment-reproducible-compose/spec.md`

## Summary

Give developers a supported way to run Iskender with its dependencies on a workstation, built from the two ISK-0007 images and nothing new. The work has seven parts:

1. **Topology**: `local/compose.yaml` with `postgres`, `valkey`, `data-plane`, and `control-plane`, the `migrate` and `probe` one-shot services under profiles, and `local/compose.dependencies.yaml` for the `dependencies` mode (R1, R2, R4).
2. **Documents**: a base and a `development` overlay per plane, with every development-only value in the overlay, plus native copies for `dotnet run` (R5, R6).
3. **PostgreSQL provisioning and passwords**: an initialisation script that creates the three ISK-0004 identities, and per-checkout generated passwords in an ignored, owner-only file (R7, R8).
4. **Scripts**: `start`, `stop`, `reset`, `migrate`, and `smoke` in `bash` 3.2, with fixed exit codes, and the restricted project-name override (R9, R10, R11, R12).
5. **Smoke assertions**: the exact readiness answer before ISK-0021, the migration job's failure, the roles, the eviction policy, and a value search (R13, R14, R15).
6. **Evidence**: tests in `tests/Iskender.Container.Tests/LocalEnvironment/`, including 5 fresh-copy cycles in CI check 4 (R17).
7. **Publication**: the `local-environment` contract version 1, a "Local development" guide in `docs/04`, the contributor guide, and repository wiring (`.gitignore`, `.dockerignore`, `CODEOWNERS`, the docs gate) (R1, R18).

See [research.md](research.md) for the decisions. The maintainer decided three of them during `plan` on 2026-11-16 (R5, R9, R14), and they are recorded in the spec's Clarifications. Two corrections follow from the code with no choice left to make (R5, R6).

## Technical Context

**Language/Version**: `bash` 3.2 or later for the scripts and the provisioning script; Compose file format of Docker Compose 2.24.0 or later; YAML documents under ISK-0003's configuration contract; C# 14 on .NET 10, SDK 10.0.404 per `global.json`, for the tests.

**Primary Dependencies**:

- Images, each pinned by digest: `postgres:17-bookworm` and `valkey/valkey:8.1.11` at the ISK-0004 and ISK-0005 fixtures' digests, `curlimages/curl:8.17.0` for the probe (R14), and the two plane images built from their unchanged ISK-0007 Dockerfiles (R3).
- Tools: `git`, Docker Engine 24.0 or later, Docker Compose 2.24.0 or later (AV-2, R12).
- Tests: the existing `xunit.v3` and `Iskender.Container.Tests` fixtures (`RequireDocker`, `DockerCli`). No new package.

**Storage**: PostgreSQL 17 and Valkey 8 on two named volumes. No table, schema, row, or migration is created (FR-007, FR-016).

**Testing**: xUnit v3 on Microsoft.Testing.Platform under CI check "4. Test": in-process document validation, script contract tests with no container runtime, and container tests that drive the scripts in a clean copy. The architecture suite (check 5) and the compatibility harness run unchanged.

**Target Platform**: Developer workstations on Linux, macOS, and Windows with WSL 2, `amd64` or `arm64`; CI's `ubuntu-24.04` runner, `amd64`.

**Project Type**: Developer tooling: Compose files, documents, and shell scripts in the repository, with tests in an existing test project. No `src/` change.

**Performance Goals**: None asserted (Constitution VIII). The 180-second start bound, the 60-second lifecycle wait, and the 15-second probe limit are safety bounds, not performance figures.

**Constraints**:

- No new entry point, executable, Dockerfile, image definition, credential provider, product table, seed data, endpoint, or cloud resource (FR-004, SC-009).
- Neither ISK-0007 Dockerfile changes, and nothing under `tests/Iskender.Container.Tests/compose/` changes (SC-009).
- No rule of the container, persistence, coordination, configuration, or hosting contract changes (FR-021). No `src/` file changes except T044's maintainer-approved fix to the coordination start-up, which changes no public API, so no compatibility baseline changes (SC-010).
- Builds and runs every test offline after restore with no cloud credentials, except the container-backed tests, which also need network access to pull images and restore packages inside the image build, and skip with their reason locally without a runtime, and fail when `CI=true`.
- CI stays at 10 checks (FR-022).
- `.specify/` and `.iskender/grounding.yaml` are unchanged.
- This PC restores through its user-level proxy feed, which must never appear in a committed file under `local/`, in a Dockerfile, or in CI.

**Scale/Scope**: 2 Compose files, 6 services, 2 modes, 2 volumes, 8 documents, 1 provisioning script, 5 scripts and 2 libraries, 4 script exit codes, 7 smoke assertions, 3 published ports, 1 new contract, about 9 test classes, and 0 `src/` changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Gate | Status | Notes |
|---|---|---|---|
| I. Product & License | MIT; container and embedded of equal standing | Pass | The topology runs the two MIT images unchanged. The probe image declares `MIT`, and curl is under the curl licence, permissive, approved by the maintainer as Product/Charter owner (R14, AV-4). PostgreSQL and Valkey are run, not redistributed. No package is added |
| II. Architecture & Topology | Planes independently deployable; local path without cloud credentials | Pass | Each plane runs in its own container with no shared state, and neither depends on the other (R2). The whole topology starts from a fresh clone with no credential (US1, SC-001). It is topology 1's developer distribution without a new entry point (R2, contract Services rule 4) |
| III. Mandatory State | Durable conversation tests | N/A | No conversation code |
| IV. Provider Correctness | Unavailable capabilities fail explicitly | Pass | The topology reports its real state before ISK-0021, `authentication_failed`, and the smoke run fails if that changes, rather than working around it (R13) |
| V. Security & Privacy | Fail closed; non-root; no secrets in output | Pass | Development-only values only in `development` overlays (R5). Generated passwords in an owner-only ignored file, never printed, and a value search over logs and output (R8, R13). Loopback-only ports, no health port published (R10). The images keep their hardening (R4). The reset reaches only its own project (R9, R12) |
| VI. Policy & Plugins | Typed policies; no competing implementation | N/A | No policy or plugin code |
| VII. Resilience & Governance | Explicit failure semantics | Pass | Every script failure has its class's exit code (contract, Exit codes). Migration stays an explicit job (R11). Valkey never evicts coordination keys and keeps them across restarts (R15) |
| VIII. Test & Operational Rigor | Risk-proportionate tests; no invented numbers; rollback docs | Pass | 5 fresh-copy cycles in CI, failure-case smoke tests, document tests, and script contract tests (R17). No performance figure. Rollback of this PR is a revert: it stores nothing outside a developer's own volumes |
| IX. Engineering Practice | Versioned surfaces; doc sync; supply-chain hygiene | Pass | `local-environment` contract version 1. Docs change in the same PR, and `local/` joins the docs gate and CODEOWNERS (R1, R18). Every image is pinned by digest. R5 and R6 stay unapplied under DEC-20261105-002 |
| X. Spec Kit Discipline | Stage and budget rules | Pass | Run by `/drive-speckit`. `.specify/` is unchanged |
| XI. Human Governance | No silent roadmap change; real evidence | Pass | No roadmap, ADR, constitution, or grounding change, and topology 1's wording in `docs/04` is unchanged (FR-003). Each `plan` decision records the maintainer as decider |
| XII. Enforcement | Owner and gate per principle | Pass | Each contract section names its review owner |

**Post-design re-check**: Pass. The maintainer decided three judgments on 2026-11-16, each recorded in research and in the spec's Clarifications. They reword parts of the spec and change no gate above:

- Native runs use tracked native documents, because a Redis endpoint cannot be set by an `ISKENDER__` variable (R5). TR-8 and FR-014 are reworded.
- The project name has a restricted override, so the acceptance test can run beside a developer's topology and a reset still cannot reach ISK-0007's projects (R9). TR-1, TR-15, FR-001, and FR-020 are reworded.
- The probe image is `curlimages/curl`, pinned by digest (R14). TR-14 and AV-4 are updated.

One correction follows from the code with no choice left to make: the control plane registers no section of its own, so only the data-plane documents are rejected by the other plane (R6). US3.6 is reworded.

## Project Structure

### Documentation (this feature)

```text
specs/0008-local-developer-environment-reproducible-compose/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── local-environment.md          # Version 1
├── checklists/
│   └── requirements.md
└── tasks.md                          # Created by /speckit-tasks
```

### Source Code (repository root)

```text
local/                                     # New
├── README.md                              # Points to docs/04, Local development
├── compose.yaml
├── compose.dependencies.yaml
├── config/data-plane/iskender.yaml, iskender.development.yaml
├── config/control-plane/iskender.yaml, iskender.development.yaml
├── native/data-plane/iskender.yaml, iskender.development.yaml
├── native/control-plane/iskender.yaml, iskender.development.yaml
├── postgres/initdb/10-provision.sh
└── scripts/
    ├── start.sh, stop.sh, reset.sh, migrate.sh, smoke.sh
    └── lib/common.sh, lib/readiness.sh

tests/Iskender.Container.Tests/
└── LocalEnvironment/                      # New
    ├── LocalCheckout.cs, LocalTopology.cs, RequireLocalTools.cs, RecordedReadiness.cs
    ├── FreshCopyAcceptanceTests.cs, RestartTests.cs, SmokeFailureTests.cs, ReadinessComparisonTests.cs
    ├── ResetTests.cs, DependenciesModeTests.cs, MigrationScriptTests.cs, ScriptContractTests.cs
    └── DocumentTests.cs

.gitignore                                 # + /local/.secrets/
.dockerignore                              # + local/
.github/CODEOWNERS                         # + /local/
.github/scripts/docs-gate.sh               # + local/* as behavior-bearing
docs/04-technology-stack-and-deployment.md # + Local development; AV-6 settlements
CONTRIBUTING.md                            # Before you push: start and smoke scripts, prerequisites
```

**Structure Decision**: The topology is one directory outside `src/` and `tests/`, so it cannot share a file with ISK-0007's fixture and holds no project for the module dependency rules to classify (R1). Its tests join `Iskender.Container.Tests`, which already holds the container-runtime fixtures and runs in check 4, so no project, package, or CI check is added (R17).

## Implementation approach

The order keeps the build and every test passing at each commit. `tasks.md` orders the same work by user story. Where the two orders differ, `implement` follows `tasks.md`.

1. **Wiring**: `.gitignore`, `.dockerignore`, `CODEOWNERS`, and the docs-gate path, so no password file can be committed or sent to a build before one exists (R1).
2. **Documents**: the four container documents and the four native ones, with `DocumentTests` (R5, R6).
3. **Script library**: `lib/common.sh` with root finding, the project name and port checks, prerequisites, and the Compose wrapper, and `ScriptContractTests` for `--help`, usage errors, subdirectories, outside a checkout, LF endings, and modes (R9, R10, R12).
4. **Topology**: `compose.yaml`, `compose.dependencies.yaml`, the provisioning script, and `start.sh` and `stop.sh`, with `LocalCheckout`, `LocalTopology`, `RequireLocalTools`, and `RestartTests` (R2, R3, R4, R7, R8, R15).
5. **Readiness and smoke**: `lib/readiness.sh` and `smoke.sh`, with the `probe` service, `ReadinessComparisonTests`, and `SmokeFailureTests`. Confirm the serializer's exact readiness bytes against a running plane, and record them in the contract (R13, R14).
6. **Migration**: the `migrate` service and `migrate.sh`, with `MigrationScriptTests` (R11).
7. **Reset**: `reset.sh`, with `ResetTests` (R12).
8. **Dependencies mode and native runs**: `start.sh --dependencies`, with `DependenciesModeTests` (R2, R5, R10).
9. **Acceptance**: `FreshCopyAcceptanceTests`, 5 cycles, with the inspection and value search of its first cycle (R17).
10. **Publication**: the `docs/04` guide, the contributor guide, `local/README.md`, and every quickstart scenario run once (R18).

## Requirement coverage

| Requirements | Where they land |
|---|---|
| FR-001 | `local/`; `ScriptContractTests` and a test that no file under `local/` is referenced by `tests/Iskender.Container.Tests/compose/` or the reverse |
| FR-002, FR-003, FR-004 | `compose.yaml`, `compose.dependencies.yaml`; the contract's Services and Modes; `docs/04`; `FreshCopyAcceptanceTests`, `DependenciesModeTests`, `MigrationScriptTests` |
| FR-005, FR-006 | `start.sh`, `compose.yaml`; the first acceptance cycle's inspection |
| FR-007, FR-008, FR-009 | `10-provision.sh`, `start.sh`; smoke check 5; `RestartTests`, `ResetTests`, and the value search |
| FR-010 | `compose.yaml`'s `valkey`; smoke check 6; `RestartTests` keeps `valkey-data` |
| FR-011 to FR-014 | The eight documents; `DocumentTests`, `DependenciesModeTests` |
| FR-015 | `compose.yaml`, `compose.dependencies.yaml`, `start.sh`'s port check; the first acceptance cycle and `DependenciesModeTests` |
| FR-016 | `compose.yaml` runs no migration; smoke check 5; `MigrationScriptTests` |
| FR-017, FR-018 | The five scripts and `lib/common.sh`; `ScriptContractTests` and every script test |
| FR-019 | `smoke.sh`, `lib/readiness.sh`, the `probe` service; `ReadinessComparisonTests`, `SmokeFailureTests` |
| FR-020 | `reset.sh`; `ResetTests`, and every acceptance cycle's check that no project resource remains |
| FR-021 | `contracts/local-environment.md`, with no other contract revised |
| FR-022 | `tests/Iskender.Container.Tests/LocalEnvironment/` in check 4 |
| FR-023 | `docs/04` Local development, `CONTRIBUTING.md`, `local/README.md` |
| FR-024 | `.gitattributes` (`* text=auto eol=lf`, already present); `ScriptContractTests` |

## Risks

| Risk | Mitigation |
|---|---|
| The readiness serializer's exact bytes differ from the contract's body | Step 5 records the bytes from a running plane before the comparison is written, and `ReadinessComparisonTests` pins them |
| Five cycles, each with an image build, make check 4 slow | The images are built from the same content as ISK-0007's tests, so Docker's layer cache serves later builds. Each cycle's start still builds both images, as the contract requires, and its build is served from that cache. A real slowdown is reported in the PR, not hidden by fewer cycles |
| `pg_isready` passes during initialisation, so the planes start before the roles exist | The health check uses `-h 127.0.0.1`, which the initialisation server does not listen on (R7) |
| A `bash` 4 construct slips into a script and breaks macOS | The tests run the scripts under macOS `bash` 3.2 locally, and `ScriptContractTests` runs each script's `--help` there. R12 lists the constructs to avoid |
| The reset removes another project's resources | It acts only through Compose with the validated project name and its own files, never by pattern, and `ResetTests` keeps a control volume labelled with an `isk-` project |
| A test leaves a project behind and breaks the next run | `LocalTopology.DisposeAsync` resets and removes its tags even on failure, and each test has its own name and ports |
| A pinned image is withdrawn | Digests are recorded in the contract. A withdrawn one is replaced through a revision of that table, never by a tag |
| Docker Hub rate limits anonymous pulls in CI | The topology pulls three small images once per run, the same two as ISK-0007's fixture plus curl. A limit is a blocker reported with its error, not a skip |

## Complexity Tracking

No constitution violations to justify.
