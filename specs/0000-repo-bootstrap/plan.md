# Implementation Plan: ISK-0000 — Repository Bootstrap & Agentic Development Framework

**Branch**: `feature/0000-repo-bootstrap` | **Date**: 2026-11-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/0000-repo-bootstrap/spec.md`

## Summary

ISK-0000 delivers the *tooling, guardrails, and onboarding assets* for Iskender Gateway — not any
gateway runtime behavior. The work splits into three cooperating layers:

1. **Repository & skeleton** — a compilable, business-logic-free .NET 10 modular-monolith solution
   (host, core contracts, domain, infrastructure, provider abstractions, policy SDK, management, and
   test assemblies) with deterministic formatting/line-ending config, centrally pinned dependencies,
   reproducible `restore/build/test`, and architecture (reference-direction) tests.
2. **Agent integration** — plain Claude Code skills, with no CLI or compiled tooling behind them
   (FR-003a): a full resumable `/drive-speckit` skill (one eligible stage per invocation, automating
   the installed Spec Kit skills, with repo-root resolution, verbatim approved-prompt retrieval,
   atomic+locked versioned state, repair budgets of 3/problem, 10/run, 2 no-progress, and the
   convergence ledger with severity rules), a `/review-roadmap` skill plus Roadmap Steward agent
   (diff-only proposals awaiting human approval), bounded reviewer helpers, and `CLAUDE.md`.
3. **Collaboration & onboarding** — protected-`main` branch-rule documentation (applied where admin
   permissions allow, otherwise flagged pending), CODEOWNERS/PR/issue/security templates, a GitHub
   Actions CI skeleton (restore→format→build→test→architecture→docs/manifest/inventory
   consistency→secret scan→dependency hygiene→SBOM/provenance), the documentation merge gate,
   README, and an original accessible kebab-inspired SVG logo.

Technical approach is grounded in the installed Spec Kit (preserved byte-for-byte; commands
discovered, never reinstalled) and the active constitution at `.specify/memory/constitution.md`,
which is authoritative over the bootstrap prompt.

## Technical Context

**Language/Version**: C# on .NET 10 (`global.json`-pinned SDK); PowerShell 7+ for existing Spec Kit
scripts; Markdown/YAML/JSON for skills, manifest, docs, and CI.

**Primary Dependencies**: .NET SDK 10 (ASP.NET Core for the host project only); `xunit.v3` +
`NetArchTest.Rules` for architecture/reference-direction tests; Bash for the CI check scripts;
installed GitHub Spec Kit (preserved, commands discovered). Management UI (Next.js) is a documented
future scope placeholder, not built here.

**Storage**: Files only. Workflow state records are versioned JSON under `.iskender/state/` written
atomically (temp-file + rename) with an advisory lock file per spec. No database; no cloud.

**Testing**: xUnit for .NET architecture and smoke tests only. The workflow skills are verified by use,
not unit tests (FR-003a). No live secrets in default CI; Testcontainers-based integration is out of
scope for the skeleton (deferred to later specs).

**Target Platform**: Cross-platform developer machines (Windows 11 primary, Linux CI runners);
.NET 10 runtime. Clean clone must build/test with **no cloud credentials**.

**Project Type**: Repository bootstrap — multi-project .NET modular-monolith **skeleton** + Claude
Code agent tooling + CI/collaboration scaffolding. Not a single-app project.

**Performance Goals**: None asserted. Per Constitution Principle VIII, no SLO/latency/throughput
figure is claimed without a reviewer-approved baseline, and this spec establishes no runtime path.

**Constraints**: Preserve installed Spec Kit byte-for-byte (except independently approved
integrations); never reinstall/reinitialize it. Fail-closed on manifest/prompt errors. Atomic +
locked state writes; counters survive process restart. Build/test succeed offline (per FR-007:
restore may use a public NuGet feed without credentials; build and test need no network) with no
cloud provisioning. No gateway business logic, provider adapters, policy engine, or DB domain schema.

**Scale/Scope**: 7 production skeleton projects + 2 test assemblies; 1 CI workflow + 2 check scripts;
2 Claude Code skills + 1 steward agent + bounded reviewer helpers; README, CLAUDE.md, logo, and
collaboration templates.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

The active constitution (`.specify/memory/constitution.md`, v1.1.0) governs. Runtime-behavior
principles (III Conversation State, IV Provider Correctness, VI Policy/Plugins runtime, VII
Resilience) are **out of scope by design** (FR-047) — the skeleton must merely not violate their
direction. Applicable gates for this bootstrap:

| Principle | Applicability to ISK-0000 | Gate satisfied by this plan |
|---|---|---|
| I. Product Identity & License | Yes | License scan in CI (`check-license.sh`) proves MIT; README states MIT + mission; no open-core withholding (bootstrap withholds nothing). |
| II. Architecture & Topology | Yes | Modular-monolith skeleton with explicit dependency direction enforced by `Iskender.Architecture.Tests`; local build path requires no cloud creds. |
| III. Mandatory State | Deferred (FR-047) | No conversation runtime built; skeleton leaves domain empty. No violation. |
| IV. Provider Correctness | Deferred (FR-047) | `Iskender.Providers.Abstractions` compiles empty; no adapter code or parity claims. |
| V. Security & Privacy | Yes | Secret scanning + dependency integrity in CI; secrets ignored via `.gitignore`; secure-dev standards doc; fail-closed manifest validation; no secrets in telemetry (none emitted). |
| VI. Policy & Plugins | Deferred (FR-047) | `Iskender.Policy.Sdk` compiles empty; no CEL/handlers/plugin loading. |
| VII. Resilience & Governance | Deferred (FR-047) | No runtime governance built. |
| VIII. Test & Operational Rigor | Yes | Architecture tests proportionate to an empty skeleton; workflow skills verified by use; clean build; **no invented performance numbers**; rollback/upgrade notes in README/quickstart. |
| IX. Engineering Practice & Supply-Chain | Yes | `feature/`/`chore/`/`bugfix/` off protected `main`, with required reviews excepted until a second PR identity exists (`docs/18`); concurrent docs merge gate; central package pinning + `global.json`; SBOM/provenance hooks; only *approved* decisions recorded as facts. |
| X. Agentic Spec Kit Discipline | Yes (core) | One eligible stage/invocation; `implement` stops before `converge`; budgets 3/10/2 persisted across restarts; convergence severity rules; mandatory summary + blocker contract. Spec Kit preserved. |
| XI. Human Governance & Anti-Fabrication | Yes | Roadmap Steward proposes diff-only, awaits approval; real Git evidence recorded in state; no fabricated progress. |
| XII. Constitutional Enforcement | Yes | Each applicable gate has an owner + measurable check wired into CI/review; deviations justified in-PR; no informal waivers. |

**Gate result: PASS.** No violations requiring justification. Complexity Tracking left empty.

## Project Structure

### Documentation (this feature)

```text
specs/0000-repo-bootstrap/
├── plan.md              # This file (/speckit-plan output)
├── spec.md              # Feature specification (input)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (schemas & interface contracts)
│   ├── grounding-manifest.schema.md
│   ├── workflow-state.schema.md
│   ├── convergence-ledger.schema.md
│   ├── drive-speckit-skill.md
│   ├── blocker-and-summary.contract.md
│   ├── ci-and-merge-gates.contract.md
│   └── module-dependency-rules.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
Iskender.sln
global.json                      # pins .NET 10 SDK
Directory.Build.props            # nullable enable, analyzers, LangVersion, common props
Directory.Packages.props         # central package management (version pinning)
.editorconfig                    # C#/Markdown/JSON/YAML formatting
.gitattributes                   # deterministic line endings
.gitignore                       # .NET/Next.js/IDE/OS/secrets/artifacts
LICENSE                          # MIT (existing)
README.md                        # mission/license/arch/build/Spec Kit/workflow + docs/README link
CLAUDE.md                        # agent authority hierarchy, manifest discovery, gates, warnings
SECURITY.md                      # vulnerability reporting policy
CONTRIBUTING.md                  # branch/PR conventions, secure-dev standards
assets/
└── iskender-logo.svg            # original, self-contained, accessible kebab SVG (title/desc)

src/
├── Iskender.Gateway.Host/           # ASP.NET Core host shell (no business endpoints)
├── Iskender.Core.Contracts/         # versioned contract surface (empty)
├── Iskender.Domain/                 # domain/modules (empty)
├── Iskender.Infrastructure/         # infrastructure (empty)
├── Iskender.Providers.Abstractions/ # provider abstractions (empty, no adapters)
├── Iskender.Policy.Sdk/             # policy SDK surface (empty, no engine)
└── Iskender.Management/             # management surface (empty; Next.js UI = future scope)

tests/
├── Iskender.Architecture.Tests/     # reference-direction / module-boundary enforcement
└── Iskender.Core.Contracts.Tests/   # smoke test proving the solution builds & tests run

.github/
├── workflows/ci.yml                 # restore→format→build→test→arch→docs/manifest→secret→deps→SBOM
├── scripts/
│   ├── check-grounding.sh           # manifest, inventory, and link consistency
│   └── docs-gate.sh                 # documentation merge gate
├── CODEOWNERS
├── dependabot.yml
├── pull_request_template.md
└── ISSUE_TEMPLATE/{bug_report.md,feature_request.md,config.yml}

.iskender/
├── grounding.yaml                   # PRESERVED (supplied manifest)
└── state/                           # workflow state records (versioned JSON, atomic+lock)
    ├── proposals/                   # pending and decided roadmap change proposals
    └── .gitkeep

.specify/                            # PRESERVED byte-for-byte, apart from approved constitution amendments — never reinstalled/reinitialized
.claude/
├── CLAUDE is at repo root (above)
├── skills/
│   ├── drive-speckit/               # full resumable one-stage-per-invocation driver
│   └── review-roadmap/              # Roadmap Steward skill (diff-only proposals)
└── agents/
    ├── roadmap-steward.md
    └── reviewers/                   # bounded helpers (architecture-guard, security-isolation, …)
```

**Structure Decision**: Multi-project modular-monolith skeleton under `src/` (one project per
constitution-named module, dependency direction enforced by `tests/Iskender.Architecture.Tests`).
Workflow behavior lives in plain Claude Code skills under `.claude/`, with no compiled tooling behind
them; the installed `.specify/` tree is treated as read-only.

## Complexity Tracking

> No Constitution Check violations. Section intentionally empty.
