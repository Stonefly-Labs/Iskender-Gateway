---

description: "Task list for ISK-0000 — Repository Bootstrap & Agentic Development Framework"
---

# Tasks: ISK-0000 — Repository Bootstrap & Agentic Development Framework

**Input**: Design documents from `/specs/0000-repo-bootstrap/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (all present)

**Tests**: Architecture and smoke tests cover the .NET skeleton (FR-006). The workflow skills are plain
Claude Code skills verified by use, with no unit tests and no CLI behind them (FR-003a).

**Numbering**: IDs were kept when the CLI and its tests were removed on 2026-11-05, so gaps are expected.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task belongs to (US1…US6)
- Exact file paths are included in each description

## Path Conventions

Repository-root layout (per plan.md): `src/` (7 skeleton projects), `tests/` (architecture, smoke),
`.github/`, `.iskender/`, `.claude/`, `assets/`, and root-level config/onboarding files. The installed
`.specify/` tree is **read-only** and preserved byte-for-byte.

---

## Phase 1: Setup (Shared Repository Conventions)

- [X] T001 Create the top-level directory structure at repo root: `src/`, `tests/`, `assets/`, `.github/workflows/`, `.github/scripts/`, `.github/ISSUE_TEMPLATE/`, `.claude/skills/`, `.claude/agents/reviewers/`, and `.iskender/state/` (do NOT touch the existing `.specify/` or `.iskender/grounding.yaml`)
- [X] T002 [P] Create `.gitignore` at repo root covering .NET (`bin/`, `obj/`, `*.user`), Next.js (`node_modules/`, `.next/`), IDE (`.vs/`, `.idea/`), OS (`.DS_Store`, `Thumbs.db`), secrets (`*.env`, `*.pfx`, `appsettings.*.local.json`), and build artifacts (`artifacts/`, `*.nupkg`, SBOM outputs); it MUST NOT exclude `.iskender/state/` records (workflow state is committed/durable) and MUST ignore only the transient `.iskender/state/*.lock` and `.iskender/state/*.tmp` files — per FR-004, Research §D5
- [X] T003 [P] Create `.gitattributes` at repo root forcing deterministic line endings (`* text=auto eol=lf`, binary rules for `*.svg`/images, `*.sln eol=crlf` if required by tooling) — per FR-004/D13
- [X] T004 [P] Create `.editorconfig` at repo root with formatting rules for C#, Markdown, JSON, and YAML (indentation, `end_of_line = lf`, final newline, C# analyzer severities) — per FR-004/D13
- [X] T005 [P] Confirm the existing MIT `LICENSE` is present at repo root and unmodified; record its presence (no overwrite) — per FR-008, Principle I

---

## Phase 2: Foundational (Blocking Prerequisites)

- [X] T006 Create `global.json` at repo root pinning the .NET 10 SDK (with `rollForward` policy) and selecting Microsoft.Testing.Platform as the test runner — per FR-007/D13
- [X] T007 Create `Directory.Build.props` at repo root enabling `Nullable=enable`, analyzers, `TreatWarningsAsErrors=true`, deterministic builds, `LangVersion`, `RestorePackagesWithLockFile=true` (with each project's `packages.lock.json` committed, backing CI's locked restore), and the shared `Iskender.*` namespace/root-namespace convention — per FR-005/FR-008/D13
- [X] T008 Create `Directory.Packages.props` at repo root enabling Central Package Management (`ManagePackageVersionsCentrally=true`) and pinning every NuGet version in one place: `xunit.v3` and `NetArchTest.Rules` — per FR-008/D13
- [X] T009 Create an empty `Iskender.sln` at repo root (`dotnet new sln --format sln`, because SDK 10 defaults to `.slnx`) — per FR-005/D13
- [X] T010 Create `.iskender/state/.gitkeep` so the versioned workflow-state directory is tracked while empty — per D5
- [X] T011 Spec Kit preservation guard: record the installed Spec Kit version and the discovered command set (e.g. `speckit-{specify,clarify,plan,tasks,analyze,checklist,implement,converge}`) into a dedicated notes file `specs/0000-repo-bootstrap/speckit-preservation.md`, record baseline commit `ade58b7` there, and confirm `git diff --exit-code ade58b7 -- .specify` passes (byte-for-byte preserved, including against committed changes). MUST NOT reinstall, reinitialize, or modify `.specify/` — per FR-001/FR-002/FR-003, SC-002

---

## Phase 3: User Story 1 - Clean-clone compilable skeleton & onboarding (Priority: P1) 🎯 MVP

**Goal**: A business-logic-free but compilable .NET modular-monolith skeleton (7 `src/` projects +
architecture/smoke tests) that restores/builds/tests green on a clean clone with no cloud
credentials, plus README, `CLAUDE.md`, and an original accessible kebab SVG logo.

**Independent Test**: Fresh clone with no cloud secrets → `dotnet restore && dotnet build -warnaserror
&& dotnet test` all green with zero cloud provisioning; `.specify/` unchanged; README, `CLAUDE.md`, and
`assets/iskender-logo.svg` (with `<title>`/`<desc>`) exist and are valid.

### Implementation — skeleton projects (all compile EMPTY, no business logic — FR-047)

- [X] T016 [P] [US1] Create `src/Iskender.Core.Contracts/Iskender.Core.Contracts.csproj` (root namespace `Iskender.Core.Contracts`); **no internal dependencies** — per module-dependency-rules.md A1
- [X] T017 [P] [US1] Create `src/Iskender.Domain/Iskender.Domain.csproj` (namespace `Iskender.Domain`)
- [X] T018 [P] [US1] Create `src/Iskender.Infrastructure/Iskender.Infrastructure.csproj` (namespace `Iskender.Infrastructure`)
- [X] T019 [P] [US1] Create `src/Iskender.Providers.Abstractions/Iskender.Providers.Abstractions.csproj` (namespace `Iskender.Providers.Abstractions`); **no provider adapter code** — per FR-047
- [X] T020 [P] [US1] Create `src/Iskender.Policy.Sdk/Iskender.Policy.Sdk.csproj` (namespace `Iskender.Policy.Sdk`); **no policy engine / CEL / plugin loading** — per FR-047
- [X] T021 [P] [US1] Create `src/Iskender.Management/Iskender.Management.csproj` (namespace `Iskender.Management`); Next.js UI is documented future scope only — per FR-047
- [X] T022 [P] [US1] Create `src/Iskender.Gateway.Host/Iskender.Gateway.Host.csproj` as an ASP.NET Core host shell with a minimal `Program.cs` and **no business endpoints** — per FR-047, plan.md
- [X] T023 [US1] Wire `ProjectReference` edges per the approved direction (`Providers.Abstractions`/`Policy.Sdk`/`Domain`/`Management` → `Core.Contracts`; `Infrastructure` → `Domain`,`Core.Contracts`; `Host` composes all; **no project references `Host`**) and register all seven `src/` projects in `Iskender.sln` — per module-dependency-rules.md

### Tests for User Story 1

- [X] T024 [P] [US1] Create `tests/Iskender.Architecture.Tests/Iskender.Architecture.Tests.csproj` (`xunit.v3` + `NetArchTest.Rules`) and implement reference-direction assertions A1–A4 in `tests/Iskender.Architecture.Tests/DependencyDirectionTests.cs`: (A1) `Core.Contracts` depends on no other `Iskender.*` production project; (A2) no project depends on `Iskender.Gateway.Host`; (A3) `Domain`/`Providers.Abstractions`/`Policy.Sdk`/`Management` do not depend on `Infrastructure`; (A4) no cycles among `src/`. Assert each rule against the `ProjectReference` graph parsed from every `.csproj` (empty assemblies keep no unused references, so type-level checks alone pass without checking anything), keep `NetArchTest` type-level assertions alongside, and add tests proving that a deliberate violation in a fixture graph fails — per FR-006, US1 scenario 6, module-dependency-rules.md
- [X] T025 [P] [US1] Create `tests/Iskender.Core.Contracts.Tests/Iskender.Core.Contracts.Tests.csproj` with a smoke test in `SolutionBuildsTests.cs` proving the solution builds and the test runner executes — per plan.md
- [X] T026 [US1] Register `tests/Iskender.Architecture.Tests` and `tests/Iskender.Core.Contracts.Tests` in `Iskender.sln`, then run `dotnet build -warnaserror` and `dotnet test` and confirm green — per US1 Independent Test, SC-001

### Onboarding & identity assets (US1)

- [X] T027 [P] [US1] Create `README.md` at repo root covering mission, MIT license, architecture overview, documented `restore`/`build`/`test` instructions, Spec Kit how-to, future plugin/policy scope, contributor workflow, and a **prominent link to `docs/README.md`** — per FR-009, US1 scenario 4
- [X] T028 [P] [US1] Create `CLAUDE.md` at repo root covering project purpose, the document authority hierarchy (active constitution authoritative), manifest discovery via `.iskender/grounding.yaml`, current-branch and protected-`main` rules, Spec Kit integration, review gates, command usage, fast local validation (the exact offline command sequence, e.g. `dotnet build -warnaserror && dotnet test`), privacy/integrity warnings, and an explicit statement that `/drive-speckit`, the Roadmap Steward, and `/review-roadmap` do not exist until ISK-0000 is implemented (the initial bootstrap uses the installed Spec Kit commands manually) — per FR-010/FR-003, US1 scenario 7
- [X] T029 [P] [US1] Create `assets/iskender-logo.svg` as an original, self-contained (no external refs/fonts/raster), accessible kebab-inspired SVG with `<title>`, `<desc>`, and `role="img"`; legible on **both light and dark backgrounds** (via `currentColor` or an explicit stroke/contrast treatment or a declared safe background); not derived from trademarked/downloaded artwork — per FR-011/D12, US1 scenario 4

---

## Phase 4: User Story 2 - Resumable one-stage-per-invocation workflow driver (Priority: P1)

**Goal**: A `/drive-speckit` skill that selects or resumes **exactly one** eligible stage per invocation,
keeps durable state in plain files, blocks on contention, stops `implement` before `converge`, and
emits the mandatory summary.

- [X] T044 [US2] Create the `/drive-speckit` plain skill at `.claude/skills/drive-speckit/SKILL.md`: no stage argument; preflight on every invocation, blocking on any failure (FR-019/FR-019a); spec selection (incomplete record first, then lowest-numbered eligible `ISK-xxxx`, FR-020); exactly one stage per invocation in the order `bootstrap/detect → specify → clarify → plan → tasks → analyze → checklist → implement → converge → roadmap-review → pr-merge-checkpoint`, running the discovered Spec Kit skills (FR-020); `implement` works all remaining tasks then stops before `converge` (FR-023); state record per workflow-state.schema.md with temp-file-and-rename writes, `stateVersion` fail-closed reads, and the lock rules including block-and-exit, refresh, and 15-minute stale reclaim (FR-021/FR-022/FR-028); resume verification (FR-022); the commit, push, and PR cadence (FR-040a); the mandatory summary (FR-034) — per FR-003a, contracts/drive-speckit-skill.md
- [X] T044b [US2] Write the `specify` stage in the skill: load the approved exact prompt from the master inventory byte-for-byte with the extraction command in the skill, never regenerate or substitute it, and **block** with the blocker contract when it is absent — per FR-024/FR-016/FR-017
- [X] T044a [US2] Write the `checklist` stage in the skill: after `analyze` and before `implement`, generate requirements-quality checklists via `speckit-checklist`, perform the review against the spec and design artifacts, mark each satisfied item complete, and raise only items needing human input (recording them as `blocked`); it reviews requirements, not implementation, and MUST NOT modify code — per FR-024a, US2 scenario 6

---

## Phase 5: User Story 3 - Deterministic grounding discovery & verbatim approved-prompt retrieval (Priority: P1)

**Goal**: Robust repo-root resolution, fail-closed manifest validation, byte-for-byte prompt retrieval,
and correct constitution precedence, with every failure blocking with an actionable message.

- [X] T055 [US3] Write repo-root resolution in the skill: `git rev-parse --show-toplevel` from any working directory, confirmed by `.iskender/grounding.yaml` at that root, with an actionable block when no root is found — per FR-012/D2, US3 scenario 6
- [X] T058 [US3] Create `.github/scripts/check-grounding.sh` enforcing V1–V8 fail-closed (absent/unparseable manifest, `schemaVersion`, required keys, `grounding.root`, absent/empty/unreadable targets, `speckit.*` paths, traversal/absolute/symlink escape, unknown keys warned), inventory consistency, and relative Markdown links, naming the rule, offending key/value, and owner action; never guessing a path. The skill's preflight runs it — per FR-013/FR-014/FR-015, grounding-manifest.schema.md, SC-003
- [X] T060 [US3] Write constitution precedence in the skill: `.specify/memory/constitution.md` > `docs/15-constitution-prompt.md`; never overwrite the active constitution without recorded approval — per FR-018, SC-011
- [X] T062 [US3] Run `check-grounding.sh` against the real manifest and against broken copies covering V1–V7, and run the prompt extraction for `ISK-0000` and for an entry with no prompt, confirming quickstart Scenarios 2 & 3 — per SC-003/SC-004

---

## Phase 6: User Story 4 - Bounded repair budgets, convergence severity governance & actionable blockers (Priority: P2)

**Goal**: Exact repair budgets (3/10/2) that persist across restarts, convergence severity governance,
and the full blocker contract on every blocked result.

- [X] T076 [US4] Write repair budgets in the skill: limits **3 / 10 / 2**; classify meaningful-vs-no-progress mechanically per FR-025; the problem fingerprint per D6; counters persisted in the state record and never reset by a restart; only recorded resets authorized by the Spec Kit maintainer; on exhaustion, stop with a diagnosis naming who may escalate, without skipping acceptance, disabling tests, or altering requirements — per FR-025/FR-026/FR-027/FR-028, D6
- [X] T078 [US4] Write the `converge` stage in the skill: run `speckit-converge`, record findings in `.iskender/state/<specId>.converge.json` per convergence-ledger.schema.md (including `severityHistory`), and apply C1–C7: passes 1–3 schedule all; pass 4+ may accept Low-only; any Medium+ blocks; zero findings succeed; no silent downgrade; `roadmap-review` eligible only after success, never auto-run — per FR-029/FR-030/FR-031/FR-032/FR-033
- [X] T079 [US4] Write the blocker contract in the skill per blocker-and-summary.contract.md, including labeling unknown causes with diagnostic commands — per FR-035, SC-008

---

## Phase 7: User Story 5 - Human-governed roadmap review (Priority: P2)

**Goal**: A Roadmap Steward + `/review-roadmap` that produces evidence-backed, **diff-only** proposals,
blocks material changes pending explicit human approval, logs decisions, and preserves history.

- [X] T090 [P] [US5] Create the Roadmap Steward agent at `.claude/agents/roadmap-steward.md`: reviewer only (not an orchestrator), compares implementation against ADRs/constitution/remaining specs/dependency graph, classifies per FR-036/FR-037, and produces diff-only proposals awaiting human approval — per FR-036/FR-046
- [X] T091 [P] [US5] Create the `/review-roadmap` plain skill at `.claude/skills/review-roadmap/SKILL.md`: stores proposals at `.iskender/state/proposals/<proposalId>.json` per data-model §7; applies the FR-037 material categories, exemptions, authoritative files, and owner rule; records decisions in `.iskender/state/approvals.json` per data-model §8 with `approvedDiffHash` as SHA-256 over the exact diff, before any file changes; leaves files unchanged on rejection; separates non-material changes into their own proposal; resumes across approvals and CI/PR transitions, applying only on a matching hash and a clean `git apply --check` — per FR-036/FR-037/FR-038/FR-039, US5 scenarios 1–5
- [X] T092 [P] [US5] Create bounded specialized reviewer helper agents under `.claude/agents/reviewers/` (`architecture-guard.md`, `security-isolation-reviewer.md`, `test-reliability-reviewer.md`, `documentation-auditor.md`): bounded responsibility, no recursive spawning, no overlapping orchestration — per FR-046

---

## Phase 8: User Story 6 - Protected collaboration, CI & mandatory documentation merge gate (Priority: P2)

**Goal**: A GitHub Actions CI skeleton running all required checks, a deterministic path+label
documentation merge gate, CODEOWNERS/templates/security policy, and protected-`main` branch rules
documented (applied where permitted, otherwise flagged **pending** — never falsely reported as applied).

- [X] T094 [US6] Create `.github/workflows/ci.yml` with the 10 ordered checks: (1) `dotnet restore --locked-mode` (against the committed `packages.lock.json` files) → (2) `dotnet format --verify-no-changes` → (3) `dotnet build -warnaserror` → (4) `dotnet test` (all assemblies) → (5) architecture tests → (6) docs/link/manifest/inventory consistency (`.github/scripts/check-grounding.sh`) → (7) documentation merge gate → (8) secret scanning (Gitleaks), with verified false positives in `.gitleaksignore` → (9) dependency hygiene (`dotnet list package --vulnerable` + Dependabot) → (10) SBOM (CycloneDX) + build-provenance attestation hook — a **real executing step** that emits an SBOM artifact and a provenance attestation (or a documented local equivalent where a hosted attestation service is unavailable), not a placeholder comment; no live cloud secrets required — per FR-042/D9, ci-and-merge-gates.contract.md
- [X] T095 [US6] Create `.github/scripts/docs-gate.sh`, wired into `ci.yml`: changes under behavior-bearing paths (`src/Iskender.Core.Contracts/**`, `src/Iskender.Providers.Abstractions/**`, `src/Iskender.Policy.Sdk/**`, `src/Iskender.Gateway.Host/**`, `.github/workflows/**`, `.iskender/grounding.yaml`, `docs/17-master-spec-inventory.md`) MUST include a **matching documentation change** — defined as ≥1 added/modified file under `docs/**` **or** a root `README.md`/`CLAUDE.md` — in the same PR, unless a `docs-not-required` label's most recent application, read from the PR's label events, was by a CODEOWNER of every affected path **and** the text under a `## Docs-not-required justification` heading in the PR description is non-empty, ignoring HTML comments and whitespace (label present + empty justification MUST fail the gate); pure refactor/test-only changes outside those paths are never blocked — per FR-043/D10
- [X] T096 [P] [US6] Create `.github/CODEOWNERS` gating high-risk paths (contracts/SDK surfaces, host/config, CI, grounding manifest/inventory) and the FR-037 authoritative files to `@christopherhouse` — per FR-037/FR-041/FR-042, US6 scenario 4
- [X] T097 [P] [US6] Create `.github/dependabot.yml` for NuGet (and GitHub Actions) dependency updates — per FR-042/FR-045
- [X] T098 [P] [US6] Create `.github/pull_request_template.md` (checklist incl. docs-gate acknowledgement and a `## Docs-not-required justification` section whose placeholder is an HTML comment) — per FR-041/FR-043, US6 scenario 4
- [X] T099 [P] [US6] Create `.github/ISSUE_TEMPLATE/bug_report.md`, `.github/ISSUE_TEMPLATE/feature_request.md`, and `.github/ISSUE_TEMPLATE/config.yml` — per FR-041
- [X] T100 [P] [US6] Create `SECURITY.md` at repo root (vulnerability reporting policy) — per FR-041/FR-045, Principle V
- [X] T101 [P] [US6] Create `CONTRIBUTING.md` at repo root codifying branch conventions (`feature/`/`chore/`/`bugfix/` off protected `main`), the commit, push, and PR cadence (FR-040a), PR/review rules, secure-dev standards (least privilege, secret scanning, dependency integrity, no untrusted plugin execution, threat review of new public endpoints/identity/serialization), release/versioning conventions (SemVer for SDK/API surfaces, the tag/release process, and never-reused ADR identifiers), and a Review Owners section recording that every constitution role is held by the sole maintainer, GitHub `christopherhouse` — per FR-037/FR-040/FR-041/FR-045/D13
- [X] T102 [P] [US6] Create branch-protection documentation under `docs/` (+ reference from `CONTRIBUTING.md`): IaC-compatible description and the exact `gh api` commands for required reviews/checks, CODEOWNERS enforcement, and no-direct-push; include a **pending-rule** reporting convention (owner + verification command), performing no remote mutation; report each rule **individually** as applied (with verification evidence) or pending so a partially applied rule set is never summarized as fully applied; state that GitHub does not count a PR author's own approval, so a required-review rule cannot be met on a PR opened under the only approver's account — per FR-040/FR-041/FR-044/D11, SC-010
- [X] T103 [US6] Wire `.github/scripts/check-grounding.sh` as check 6 so CI fails closed on manifest/inventory/link drift — per FR-042
- [ ] T104 [US6] Validate quickstart Scenario 9: direct push to `main` rejected (or documented pending); a docs-missing PR blocked by the gate; `ci.yml` executes every check on a sample change — per SC-010
- [X] T104a [US6] Write the terminal `pr-merge-checkpoint` stage in the skill: after `roadmap-review` and the human merge of the feature PR, verify CI checks are green, the documentation merge gate is satisfied, and required reviews are present (or their rule is reported pending), then open a final records-only `chore/` PR off `main` recording the **actual** merge commit, which must be reachable from `origin/main`, in the workflow-state `gitEvidence` and the inventory entry's workflow status and final merge commit (never fabricated), and mark the spec complete; it performs **no** code modifications and the agent never merges or closes that PR — per FR-024b/FR-044, Principle XI

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T105 [P] Ensure `docs/README.md` exists as a navigable index to the grounding documents (at minimum the bootstrap brief, development standards, constitution prompt, decision register, and master spec inventory) and is linked prominently from the root `README.md` with a non-dangling link; run the link/consistency check — per FR-009
- [X] T106 [P] Run quickstart scenarios 1–3 and 10, and record the results in `specs/0000-repo-bootstrap/speckit-preservation.md`; scenarios 4–8 are confirmed by first real use of the skills and scenario 9 by T104 — per quickstart.md
- [X] T107 Final clean-clone validation with no cloud credentials: fresh clone, `dotnet restore && dotnet build -warnaserror && dotnet test` green, and confirm no build, test, or CI step invokes `az`, `azd`, `terraform`, `bicep`, or `pulumi` — per FR-007, SC-001
- [X] T108 [P] Confirm Spec Kit preserved byte-for-byte (`git diff --exit-code ade58b7 -- .specify`) and the active constitution resolves correctly — per SC-002/SC-011

---

## Dependencies & Execution Order

- **Setup (Phase 1)** then **Foundational (Phase 2)**, which blocks the skeleton and CI.
- **US1** is independent of the workflow skills and can be demoed alone as the MVP.
- **US2/US3/US4** all write the same `/drive-speckit` skill file, so they run in order; T058 creates the
  script the skill's preflight runs.
- **US5** adds `/review-roadmap`; `roadmap-review` becomes eligible only after convergence succeeds.
- **US6** adds CI and collaboration files; check 6 is the US3 script. T104a writes to the skill.
- **Polish (Phase 9)** depends on all of the above. T104 needs a pushed branch and an open PR.

---

## Notes

- The installed `.specify/` tree and `.iskender/grounding.yaml` are read-only — never reinstall,
  reinitialize, or edit Spec Kit.
- Commit after each task, push after every commit, open the PR right after the first push, and keep
  its notes current and complete before merge (FR-040a). No one needs to ask.
