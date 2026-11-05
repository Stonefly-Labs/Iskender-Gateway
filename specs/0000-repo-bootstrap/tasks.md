---

description: "Task list for ISK-0000 — Repository Bootstrap & Agentic Development Framework"
---

# Tasks: ISK-0000 — Repository Bootstrap & Agentic Development Framework

**Input**: Design documents from `/specs/0000-repo-bootstrap/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (all present)

**Tests**: INCLUDED. The spec explicitly requires acceptance tests ("Design acceptance tests for
missing/corrupt manifest, missing prompt, stage resumption, budget persistence, convergence passes
1–4, …") and Constitution Principle VIII demands synthetic, repeatable verification. Test tasks are
therefore first-class in every workflow story.

**Organization**: Tasks are grouped by user story (US1–US6) to enable independent implementation and
testing. Priorities: US1/US2/US3 = P1, US4/US5/US6 = P2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task belongs to (US1…US6)
- Exact file paths are included in each description

## Path Conventions

Repository-root layout (per plan.md): `src/` (7 skeleton projects), `tools/` (workflow core + CLI),
`tests/` (architecture, workflow, smoke), `.github/`, `.iskender/`, `.claude/`, `assets/`, and
root-level config/onboarding files. The installed `.specify/` tree is **read-only** and preserved
byte-for-byte.

---

## Phase 1: Setup (Shared Repository Conventions)

**Purpose**: Repository-level conventions and directory structure every later task depends on.

- [ ] T001 Create the top-level directory structure at repo root: `src/`, `tools/`, `tests/`, `assets/`, `.github/workflows/`, `.github/ISSUE_TEMPLATE/`, `.claude/skills/`, `.claude/agents/reviewers/`, and `.iskender/state/` (do NOT touch the existing `.specify/` or `.iskender/grounding.yaml`)
- [ ] T002 [P] Create `.gitignore` at repo root covering .NET (`bin/`, `obj/`, `*.user`), Next.js (`node_modules/`, `.next/`), IDE (`.vs/`, `.idea/`), OS (`.DS_Store`, `Thumbs.db`), secrets (`*.env`, `*.pfx`, `appsettings.*.local.json`), and build artifacts (`artifacts/`, `*.nupkg`, SBOM outputs) — per FR-004
- [ ] T003 [P] Create `.gitattributes` at repo root forcing deterministic line endings (`* text=auto eol=lf`, binary rules for `*.svg`/images, `*.sln eol=crlf` if required by tooling) — per FR-004/D13
- [ ] T004 [P] Create `.editorconfig` at repo root with formatting rules for C#, Markdown, JSON, and YAML (indentation, `end_of_line = lf`, final newline, C# analyzer severities) — per FR-004/D13
- [ ] T005 [P] Confirm the existing MIT `LICENSE` is present at repo root and unmodified; record its presence (no overwrite) — per FR-008, Principle I

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared .NET build infrastructure and the workflow-core project scaffolding that the P1
workflow stories (US2/US3/US4) all build on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T006 Create `global.json` at repo root pinning the .NET 10 SDK (with `rollForward` policy) — per FR-007/D13
- [ ] T007 Create `Directory.Build.props` at repo root enabling `Nullable=enable`, analyzers, `TreatWarningsAsErrors=true`, deterministic builds, `LangVersion`, and the shared `Iskender.*` namespace/root-namespace convention — per FR-005/FR-008/D13
- [ ] T008 Create `Directory.Packages.props` at repo root enabling Central Package Management (`ManagePackageVersionsCentrally=true`) and pinning every NuGet version in one place: xUnit + runner, `NetArchTest.Rules`, `YamlDotNet`, `System.CommandLine`, and coverage tooling — per FR-008/D13
- [ ] T009 Create an empty `Iskender.sln` at repo root (`dotnet new sln`) — per FR-005
- [ ] T010 Create `.iskender/state/.gitkeep` so the versioned workflow-state directory is tracked while empty — per D5
- [ ] T011 Spec Kit preservation guard: record the installed Spec Kit version and the discovered command set (e.g. `speckit-{specify,clarify,plan,tasks,analyze,checklist,implement,converge}`) into a dedicated notes file `specs/0000-repo-bootstrap/speckit-preservation.md` (NOT the Phase-0 `research.md`, which stays immutable), and confirm `git status --porcelain .specify` is empty (byte-for-byte preserved). MUST NOT reinstall, reinitialize, or modify `.specify/` — per FR-001/FR-002/FR-003, SC-002
- [ ] T012 [P] Scaffold `tools/Iskender.Workflow/Iskender.Workflow.csproj` as a .NET 10 class library (no business logic yet) — per D1
- [ ] T013 [P] Scaffold `tools/Iskender.Workflow.Cli/Iskender.Workflow.Cli.csproj` as a `System.CommandLine` console app with a root command and empty sub-command stubs (`validate-manifest`, `get-prompt`, `preflight`, `state`, `budget`, `converge`, `summary`); reference `Iskender.Workflow` — per D1, contracts/drive-speckit-cli.md
- [ ] T014 [P] Scaffold `tests/Iskender.Workflow.Tests/Iskender.Workflow.Tests.csproj` (xUnit) with a `Fixtures/` folder for mock Spec Kit artifacts and scripted failure modes; reference `Iskender.Workflow` — per D1, plan.md Testing
- [ ] T015 Register the foundational projects in `Iskender.sln` (`dotnet sln add` for the three `tools/`+`tests/` projects) and confirm `dotnet restore` + `dotnet build` succeed offline

**Checkpoint**: Foundation ready — solution restores/builds offline; workflow-core projects exist.

---

## Phase 3: User Story 1 - Clean-clone compilable skeleton & onboarding (Priority: P1) 🎯 MVP

**Goal**: A business-logic-free but compilable .NET modular-monolith skeleton (7 `src/` projects +
architecture/smoke tests) that restores/builds/tests green on a clean clone with no cloud
credentials, plus README, `CLAUDE.md`, and an original accessible kebab SVG logo.

**Independent Test**: Fresh clone with no cloud secrets → `dotnet restore && dotnet build -warnaserror
&& dotnet test` all green with zero cloud provisioning; every skeleton project compiles with no
gateway/provider/policy/DB business logic; `.specify/` unchanged; README, `CLAUDE.md`, and
`assets/iskender-logo.svg` (with `<title>`/`<desc>`) exist and are valid.

### Implementation — skeleton projects (all compile EMPTY, no business logic — FR-047)

- [ ] T016 [P] [US1] Create `src/Iskender.Core.Contracts/Iskender.Core.Contracts.csproj` (root namespace `Iskender.Core.Contracts`); **no internal dependencies** — per module-dependency-rules.md A1
- [ ] T017 [P] [US1] Create `src/Iskender.Domain/Iskender.Domain.csproj` (namespace `Iskender.Domain`)
- [ ] T018 [P] [US1] Create `src/Iskender.Infrastructure/Iskender.Infrastructure.csproj` (namespace `Iskender.Infrastructure`)
- [ ] T019 [P] [US1] Create `src/Iskender.Providers.Abstractions/Iskender.Providers.Abstractions.csproj` (namespace `Iskender.Providers.Abstractions`); **no provider adapter code** — per FR-047
- [ ] T020 [P] [US1] Create `src/Iskender.Policy.Sdk/Iskender.Policy.Sdk.csproj` (namespace `Iskender.Policy.Sdk`); **no policy engine / CEL / plugin loading** — per FR-047
- [ ] T021 [P] [US1] Create `src/Iskender.Management/Iskender.Management.csproj` (namespace `Iskender.Management`); Next.js UI is documented future scope only — per FR-047
- [ ] T022 [P] [US1] Create `src/Iskender.Gateway.Host/Iskender.Gateway.Host.csproj` as an ASP.NET Core host shell with a minimal `Program.cs` and **no business endpoints** — per FR-047, plan.md
- [ ] T023 [US1] Wire `ProjectReference` edges per the approved direction (`Providers.Abstractions`/`Policy.Sdk`/`Domain`/`Management` → `Core.Contracts`; `Infrastructure` → `Domain`,`Core.Contracts`; `Host` composes all; **no project references `Host`**) and register all seven `src/` projects in `Iskender.sln` — per module-dependency-rules.md

### Tests for User Story 1

- [ ] T024 [P] [US1] Create `tests/Iskender.Architecture.Tests/Iskender.Architecture.Tests.csproj` (xUnit + `NetArchTest.Rules`) and implement reference-direction assertions A1–A5 in `tests/Iskender.Architecture.Tests/DependencyDirectionTests.cs`: (A1) `Core.Contracts` depends on no other `Iskender.*` production project; (A2) no project depends on `Iskender.Gateway.Host`; (A3) `Domain`/`Providers.Abstractions`/`Policy.Sdk`/`Management` do not depend on `Infrastructure`; (A4) no cycles among `src/`; (A5) `tools/*` and `src/*` do not reference each other — per FR-006, module-dependency-rules.md
- [ ] T025 [P] [US1] Create `tests/Iskender.Core.Contracts.Tests/Iskender.Core.Contracts.Tests.csproj` with a smoke test in `SolutionBuildsTests.cs` proving the solution builds and the test runner executes — per plan.md
- [ ] T026 [US1] Register `tests/Iskender.Architecture.Tests` and `tests/Iskender.Core.Contracts.Tests` in `Iskender.sln`, then run `dotnet build -warnaserror` and `dotnet test` and confirm green — per US1 Independent Test, SC-001

### Onboarding & identity assets (US1)

- [ ] T027 [P] [US1] Create `README.md` at repo root covering mission, MIT license, architecture overview, documented `restore`/`build`/`test` instructions, Spec Kit how-to, future plugin/policy scope, contributor workflow, and a **prominent link to `docs/README.md`** — per FR-009, US1 scenario 4
- [ ] T028 [P] [US1] Create `CLAUDE.md` at repo root covering project purpose, the document authority hierarchy (active constitution authoritative), manifest discovery via `.iskender/grounding.yaml`, current-branch and protected-`main` rules, Spec Kit integration, review gates, command usage, fast local validation (the exact offline command sequence, e.g. `dotnet build -warnaserror && dotnet test`), privacy/integrity warnings, and an explicit statement that `/drive-speckit`, the Roadmap Steward, and `/review-roadmap` do not exist until ISK-0000 is implemented (the initial bootstrap uses the installed Spec Kit commands manually) — per FR-010/FR-003
- [ ] T029 [P] [US1] Create `assets/iskender-logo.svg` as an original, self-contained (no external refs/fonts/raster), accessible kebab-inspired SVG with `<title>`, `<desc>`, and `role="img"`; not derived from trademarked/downloaded artwork — per FR-011/D12, US1 scenario 4

**Checkpoint**: US1 fully functional — clean clone builds/tests green offline; onboarding assets valid. MVP demonstrable.

---

## Phase 4: User Story 2 - Resumable one-stage-per-invocation workflow driver (Priority: P1)

**Goal**: A durable, versioned, atomically written workflow-state engine that selects/resumes
**exactly one** eligible stage per invocation in dependency order, blocks (not corrupts) on
contention, stops `implement` before `converge`, and a `/drive-speckit` skill that delegates to the
CLI and emits the mandatory summary.

**Independent Test**: Using mock Spec Kit fixtures, drive a sequence of stages (one per invocation in
dependency order); confirm `implement` works all remaining tasks then **stops before `converge`**;
interrupt mid-stage and re-invoke to confirm the same stage resumes from the last verified checkpoint
with zero repeated work and zero counter reset; simulate a second concurrent driver and confirm it
blocks on the lock.

### Tests for User Story 2

- [ ] T030 [P] [US2] `tests/Iskender.Workflow.Tests/StageSelectionTests.cs`: `select-stage` returns exactly one eligible stage following dependency order `bootstrap/detect → specify → clarify → plan → tasks → analyze → checklist → implement → converge → roadmap-review → pr-merge-checkpoint`, never by CWD scanning — per FR-020, S1
- [ ] T031 [P] [US2] `tests/Iskender.Workflow.Tests/AtomicStateWriteTests.cs`: a simulated crash mid-write never yields a partial record (temp→fsync→rename) — per S2, FR-021/FR-028
- [ ] T032 [P] [US2] `tests/Iskender.Workflow.Tests/StateLockContentionTests.cs`: a second driver that cannot acquire `.iskender/state/<specId>.lock` **blocks** with a contention error and does not write — per S3, US2 scenario 4
- [ ] T033 [P] [US2] `tests/Iskender.Workflow.Tests/ImplementStopBeforeConvergeTests.cs`: `implement` works all remaining tasks until complete/blocked/input-needed and never sets `currentStage` to `converge` — per S4, FR-023, US2 scenario 2
- [ ] T034 [P] [US2] `tests/Iskender.Workflow.Tests/ResumeTests.cs`: resume reads the record, verifies `artifactPaths` + `gitEvidence`, continues from `lastConfirmedTask` with zero repeated work and zero reset of persisted counters — per S5/S6, FR-022, SC-005
- [ ] T035 [P] [US2] `tests/Iskender.Workflow.Tests/SummaryEmissionTests.cs`: every invocation emits the `drive-speckit (<stage>)` summary with spec ID+title, executed stage, work done, status, next stage/action, and re-invocation instruction — per FR-034, SC-008
- [ ] T035a [P] [US2] `tests/Iskender.Workflow.Tests/ChecklistStageTests.cs`: the `checklist` stage is selected only **after** `analyze` and **before** `implement`; when a checklist item cannot be confidently discharged the stage sets `stageStatus=blocked` (human review required) and never auto-advances to `implement`; the stage records **no** code modifications (consistent with the stage-separation rule) — per FR-024a, S4

### Implementation for User Story 2

- [ ] T036 [P] [US2] `tools/Iskender.Workflow/State/WorkflowStateRecord.cs` model per workflow-state.schema.md: `stateVersion` (int), `specId`, `currentStage`, `stageStatus` ∈ `pending|in_progress|blocked|complete`, `enteredAt` (ISO-8601), `runnerId`, `sourceHashes`, `artifactPaths[]`, `lastConfirmedTask`, `gitEvidence`, `budgets`, `partialFailureDiagnostics` (last-attempt error class/command/outcome captured for resume + blocker reporting), `convergeLedgerRef`, `approvalLedgerRef` — per FR-021
- [ ] T037 [P] [US2] `tools/Iskender.Workflow/State/WorkflowStage.cs` stage enum + dependency ordering: `bootstrap/detect`, `specify`, `clarify`, `plan`, `tasks`, `analyze`, `checklist`, `implement`, `converge`, `roadmap-review`, `pr-merge-checkpoint` — per data-model §4
- [ ] T038 [US2] `tools/Iskender.Workflow/State/AtomicStateStore.cs`: read/write `.iskender/state/<specId>.state.json` atomically (write `*.tmp` → fsync → rename-over) — per S2, D5 (depends T036)
- [ ] T039 [US2] `tools/Iskender.Workflow/State/AdvisoryLock.cs`: acquire/release `.iskender/state/<specId>.lock` (runnerId+timestamp); second acquirer **blocks** with contention error — per S3, D5
- [ ] T040 [US2] `tools/Iskender.Workflow/State/StageSelector.cs`: select/resume exactly one eligible stage in dependency order + approved status; enforce `implement`-stops-before-`converge` — per FR-020/FR-023, S1/S4 (depends T037)
- [ ] T041 [US2] `tools/Iskender.Workflow/State/ResumeCoordinator.cs`: verify artifacts + `gitEvidence`, continue from `lastConfirmedTask`, never double-apply — per S5, FR-022 (depends T038)
- [ ] T042 [US2] `tools/Iskender.Workflow/Reporting/SummaryEmitter.cs`: emit the mandatory `drive-speckit (<stage>)` summary payload per blocker-and-summary.contract.md §Summary — per FR-034
- [ ] T043 [US2] `tools/Iskender.Workflow.Cli/Commands/StateCommands.cs`: wire `state read`, `state select-stage`, `state write --spec --stage --status [--task] [--commit]`, `state lock|unlock --spec --runner`, and `summary emit --spec` to the library — per contracts/workflow-state.schema.md §CLI
- [ ] T044 [US2] Create the `/drive-speckit` skill at `.claude/skills/drive-speckit/SKILL.md`: no stage argument; preflight discovery via the CLI; select/resume exactly one eligible stage; the `specify` stage loads the verbatim approved prompt and blocks on absence (see T044b); `implement` works all remaining tasks then stops before `converge`; the `checklist` stage runs after `analyze` and before `implement` (see T044a); emit the mandatory summary; delegate all deterministic operations to `Iskender.Workflow.Cli` — per FR-019/FR-020/FR-023/FR-024, contracts/drive-speckit-cli.md
- [ ] T044b [US2] Implement the `specify` stage in the `/drive-speckit` skill (`.claude/skills/drive-speckit/SKILL.md`): on entering `specify` for a spec, the skill MUST load the approved exact prompt from the master inventory by delegating to `Iskender.Workflow.Cli get-prompt --spec <id>` (byte-for-byte, per FR-016), MUST NOT regenerate or substitute a prompt, and MUST **block** with the actionable blocker contract when the approved prompt is absent (never synthesize/template) — per FR-024/FR-016/FR-017, contracts/drive-speckit-cli.md; covered by the existing `PromptRetrievalTests.cs` (T052) and `MissingPromptTests.cs` (T053)
- [ ] T044a [US2] Implement the `checklist` stage in the `/drive-speckit` skill (`.claude/skills/drive-speckit/SKILL.md`): positioned after `analyze` and before `implement`, it generates an acceptance/quality checklist by delegating to the installed `speckit-checklist` command, reviews the implementation against it, and surfaces any item it cannot confidently discharge for explicit human review/approval (recording the item as `blocked` per `blocker-and-summary.contract.md`); it MUST NOT modify code — any resulting remediation occurs only in a separate `implement` invocation — per FR-024a, contracts/drive-speckit-cli.md
- [ ] T045 [US2] Run the US2 test set (`dotnet test --filter Workflow` subset) and confirm green — per US2 Independent Test, SC-005

**Checkpoint**: US2 functional — stage engine selects/resumes one stage, blocks on contention, stops before converge; skill emits summaries.

---

## Phase 5: User Story 3 - Deterministic grounding discovery & verbatim approved-prompt retrieval (Priority: P1)

**Goal**: Robust repo-root resolution, fail-closed grounding-manifest validation, byte-for-byte
approved-prompt retrieval from the master inventory, and correct active-constitution precedence —
every failure mode blocking with an actionable, owner-addressed message.

**Independent Test**: Valid + deliberately broken manifests/inventories → valid inputs resolve
correctly; each failure mode blocks with a precise, owner-addressed message; the active
`.specify/memory/constitution.md` overrides the bootstrap constitution prompt once it exists.

### Tests for User Story 3

- [ ] T046 [P] [US3] `tests/Iskender.Workflow.Tests/ManifestValidatorValidTests.cs`: the supplied `.iskender/grounding.yaml` passes V1–V8 (supported `schemaVersion`, all required keys, `grounding.root` under repo root, doc paths resolve + exist, `speckit.*` under repo root, unknown keys tolerated) — per V1–V8, FR-013
- [ ] T047 [P] [US3] `tests/Iskender.Workflow.Tests/ManifestSchemaVersionTests.cs`: `schemaVersion` **is an integer equal to a supported major (currently `1`)**; any unsupported major or malformed/absent value fails closed with the offending value + supported majors + owner action — per V2, FR-013, clarification
- [ ] T048 [P] [US3] `tests/Iskender.Workflow.Tests/ManifestMissingKeyTests.cs`: a missing required key fails closed naming the missing key path + owner action — per V3, FR-013
- [ ] T049 [P] [US3] `tests/Iskender.Workflow.Tests/ManifestMissingTargetTests.cs`: an absent referenced target file fails closed with the exact missing path — per V5, FR-014
- [ ] T050 [P] [US3] `tests/Iskender.Workflow.Tests/ManifestPathSafetyTests.cs`: a `..` traversal path or disallowed absolute path is rejected as "unsafe" with the offending value — per V7, FR-014
- [ ] T051 [P] [US3] `tests/Iskender.Workflow.Tests/RepoRootResolverTests.cs`: repo root resolves from a nested directory and from a Git worktree (`.git` directory **or** gitfile) independent of CWD — per FR-012/D2, US3 scenario 6
- [ ] T052 [P] [US3] `tests/Iskender.Workflow.Tests/PromptRetrievalTests.cs`: the approved `/speckit.specify` prompt for `ISK-0000` is returned **byte-for-byte** identical to the fenced block under the literal heading in `docs/17-master-spec-inventory.md` (no trim/re-wrap) — per FR-016, SC-004, D4
- [ ] T053 [P] [US3] `tests/Iskender.Workflow.Tests/MissingPromptTests.cs`: an inventory entry lacking its fenced prompt **blocks** `specify`; no prompt is synthesized or templated — per FR-017, US3 scenario 4
- [ ] T054 [P] [US3] `tests/Iskender.Workflow.Tests/ConstitutionPrecedenceTests.cs`: with both `docs/15-constitution-prompt.md` and `.specify/memory/constitution.md` present, the active constitution is authoritative and the bootstrap prompt is historical only — per FR-018, SC-011, US3 scenario 5

### Implementation for User Story 3

- [ ] T055 [P] [US3] `tools/Iskender.Workflow/Discovery/RepoRootResolver.cs`: walk upward from CWD to the first dir containing **both** a `.git` entry (dir or gitfile) and `.iskender/grounding.yaml`; fall back to `git rev-parse --show-toplevel`; actionable error if none — per FR-012/D2
- [ ] T056 [P] [US3] `tools/Iskender.Workflow/Discovery/GroundingManifest.cs`: typed model + YamlDotNet parsing for `schemaVersion`, `grounding.{root,inventory,constitutionPrompt,bootstrapBrief,decisionRegister}`, `speckit.{constitution,specificationsRoot}`, `workflow.initialSpec`; unknown keys tolerated (warn) — per FR-013/D3, grounding-manifest.schema.md
- [ ] T057 [US3] `tools/Iskender.Workflow/Discovery/PathSafety.cs`: detect/reject `..` traversal and disallowed absolute paths; confirm a path resolves under a given root — per V7, FR-014
- [ ] T058 [US3] `tools/Iskender.Workflow/Discovery/ManifestValidator.cs`: enforce V1–V8 fail-closed, returning typed `{ IsValid, OffendingKey, OffendingValue, Reason, OwnerAction }`; never guess a path or invent a second registry — per FR-013/FR-014/FR-015, grounding-manifest.schema.md (depends T056, T057)
- [ ] T059 [P] [US3] `tools/Iskender.Workflow/Discovery/PromptRetriever.cs`: locate the `ISK-xxxx` heading in the inventory and extract the fenced approved-prompt block **byte-for-byte**; block if heading or fence absent — per FR-016/FR-017, D4
- [ ] T060 [P] [US3] `tools/Iskender.Workflow/Discovery/ConstitutionResolver.cs`: resolve governance precedence `.specify/memory/constitution.md` > `docs/15-constitution-prompt.md`; never overwrite the active constitution without recorded approval — per FR-018
- [ ] T061 [US3] `tools/Iskender.Workflow.Cli/Commands/DiscoveryCommands.cs`: implement `validate-manifest [--manifest]`, `get-prompt --spec`, and `preflight --spec` (root + manifest + constitution + artifacts + checkpoint + lock discovery), wiring T055–T060 — per contracts/drive-speckit-cli.md, grounding-manifest.schema.md §CLI
- [ ] T062 [US3] Run the US3 test set and validate quickstart Scenarios 2 & 3 (`validate-manifest` exit 0 on the supplied manifest; `get-prompt --spec 0000` byte-for-byte) — per SC-003/SC-004

**Checkpoint**: US3 functional — discovery/validation/prompt/constitution deterministic and fail-closed.

---

## Phase 6: User Story 4 - Bounded repair budgets, convergence severity governance & actionable blockers (Priority: P2)

**Goal**: Enforce exact repair budgets (3/10/2) that persist across restarts, convergence severity
governance across passes 1–4, and the full blocker contract on every blocked result.

**Independent Test**: Synthetic failing/looping fixtures enforce **3** meaningful failed repairs per
problem, **10** failed repairs per run, **2** consecutive no-progress cycles, persisted across a
simulated restart; drive convergence passes 1–4 with mixed severities confirming separation/severity
rules; every blocked path emits the full blocker contract.

**Note**: Builds on the `Iskender.Workflow` state record and CLI scaffolded in US2 (budgets live
inside the state record per data-model §6).

### Tests for User Story 4

- [ ] T063 [P] [US4] `tests/Iskender.Workflow.Tests/BudgetPerProblemTests.cs`: stop after **3 meaningful (hypothesis- or action-changing) failed repairs per problem**; repeating an unchanged command does not count — per FR-025, US4 scenario 1
- [ ] T064 [P] [US4] `tests/Iskender.Workflow.Tests/BudgetPerRunTests.cs`: stop after **10 failed repairs per invocation run** — per FR-025, US4 scenario 2
- [ ] T065 [P] [US4] `tests/Iskender.Workflow.Tests/BudgetNoProgressTests.cs`: stop after **2 consecutive no-progress cycles** — per FR-025, US4 scenario 2
- [ ] T066 [P] [US4] `tests/Iskender.Workflow.Tests/BudgetPersistenceTests.cs`: per-problem and no-progress counters persist across a simulated process/skill restart and are **not** reset — per FR-026, SC-005/SC-006
- [ ] T067 [P] [US4] `tests/Iskender.Workflow.Tests/RepairFingerprintTests.cs`: a meaningful repair changes hypothesis/action and records expected observation + result; an unchanged re-run is "no progress"; fingerprint = `specId/taskId/stage + errorClass + depContext + logHash` — per FR-025, D6
- [ ] T068 [P] [US4] `tests/Iskender.Workflow.Tests/ConvergeScheduleTests.cs`: passes 1–3 set **all** findings (including `Low`) to `scheduled`; remediation only in a separate `implement` — per C2, FR-030
- [ ] T069 [P] [US4] `tests/Iskender.Workflow.Tests/ConvergeAcceptLowTests.cs`: pass 4+ may `accept` **Low-only** findings with recorded `{ owner, rationale, evidence, timestamp }` — per C3, FR-031
- [ ] T070 [P] [US4] `tests/Iskender.Workflow.Tests/ConvergeBlockTests.cs`: any `Medium`/`High`/`Critical` finding on **any** pass blocks and returns to implement/escalation — per C4, FR-031
- [ ] T071 [P] [US4] `tests/Iskender.Workflow.Tests/ConvergeZeroFindingsTests.cs`: zero findings permit immediate success, even on pass 1 — per C5, FR-032
- [ ] T072 [P] [US4] `tests/Iskender.Workflow.Tests/ConvergeSeverityPersistenceTests.cs`: severities (`Low|Medium|High|Critical`) are never silently downgraded; `passNumber` and prior findings persist across restarts — per C6, FR-032
- [ ] T073 [P] [US4] `tests/Iskender.Workflow.Tests/ConvergeReadOnlyTests.cs`: `converge` evaluates and records only and never modifies code — per C1, FR-030
- [ ] T074 [P] [US4] `tests/Iskender.Workflow.Tests/BlockerContractTests.cs`: every blocked result includes blocked stage/spec/task, precise reason, evidence/error, tried hypotheses/commands + outcomes, exact unblock steps, responsible actor, and resume point; unknown cause is labeled "unknown" with diagnostic commands — per FR-035, SC-008

### Implementation for User Story 4

- [ ] T075 [P] [US4] `tools/Iskender.Workflow/Budgets/RepairBudget.cs` model per data-model §6: `perProblem[fingerprint].meaningfulFailures`, `runFailedRepairs`, `consecutiveNoProgress`, `fingerprint`, `resetJustifications[]` — per FR-025/FR-026
- [ ] T076 [US4] `tools/Iskender.Workflow/Budgets/BudgetEnforcer.cs`: enforce limits **3 / 10 / 2**; classify meaningful-vs-no-progress via fingerprint; persist counters in the committed state record; allow only recorded, approval-bounded resets (never automatic on restart) — per FR-025/FR-026/FR-027, D6 (depends T075)
- [ ] T077 [P] [US4] `tools/Iskender.Workflow/Converge/ConvergenceLedger.cs` model per convergence-ledger.schema.md: `ledgerVersion`, `specId`, `passNumber`, `findings[]` with `id`, `severity` ∈ `{Low,Medium,High,Critical}`, `evidence`, `status` ∈ `{open,scheduled,accepted,remediated}`, `firstSeenPass`, `acceptance` (`{owner,rationale,evidence,timestamp}` or null); persisted to the sibling file `.iskender/state/<specId>.converge.json` and referenced from the state record's `convergeLedgerRef` — per FR-029
- [ ] T078 [US4] `tools/Iskender.Workflow/Converge/SeverityGovernor.cs`: implement governance C1–C7 (passes 1–3 schedule all; pass 4+ accept Low-only; any Medium+ blocks; zero findings succeed; no silent downgrade; `roadmap-review` eligible only after success, never auto-run) — per FR-030/FR-031/FR-032/FR-033 (depends T077)
- [ ] T079 [US4] `tools/Iskender.Workflow/Reporting/BlockerReport.cs`: emit the full blocker contract payload per blocker-and-summary.contract.md §Blocker — per FR-035
- [ ] T080 [US4] `tools/Iskender.Workflow.Cli/Commands/ConvergeCommands.cs` and `BudgetCommands.cs`: wire `budget check --spec --fingerprint`, `converge record --spec --pass --findings`, and `converge evaluate --spec` (returns `success|blocked(<severities>)|schedule(<ids>)` + summary/blocker payload) — per contracts
- [ ] T081 [US4] Persist edits/results/checkpoints atomically after each task/attempt (integrate budgets + ledger refs into the atomic state write) — per FR-028
- [ ] T082 [US4] Run the US4 test set and validate quickstart Scenarios 5, 6 & 7 — per SC-006/SC-007/SC-008

**Checkpoint**: US4 functional — budgets 3/10/2 persist; convergence severity governed; blockers complete.

---

## Phase 7: User Story 5 - Human-governed roadmap review (Priority: P2)

**Goal**: A Roadmap Steward + `/review-roadmap` that produces evidence-backed, **diff-only** change
proposals, blocks material changes pending explicit human approval, logs approvals, and preserves
history on rejection.

**Independent Test**: Run review against a drift fixture → structured proposal with evidence + exact
diffs; approve one and reject another → approvals logged with all fields; rejected proposals leave
authoritative files unchanged and preserve completed-spec history.

### Tests for User Story 5

- [ ] T083 [P] [US5] `tests/Iskender.Workflow.Tests/RoadmapProposalTests.cs`: a review outputs a structured, evidence-backed, **diff-only** proposal with evidence, issue, `affectedSpecIds`, exact diffs, `dependencyMilestoneImpact`, requirement-change assessment, rollback option, decision owner, and `classification` ∈ `{material,non-material}` — per FR-036, US5 scenario 1
- [ ] T084 [P] [US5] `tests/Iskender.Workflow.Tests/RoadmapApprovalGateTests.cs`: a material change with no human approval modifies **no** authoritative file and blocks with affected IDs + exact unblock action — per FR-037, US5 scenario 2
- [ ] T085 [P] [US5] `tests/Iskender.Workflow.Tests/ApprovalLedgerTests.cs`: an approval records `decisionId`, `timestamp`, `approvedDiffHash`, `reviewer`, `rationale`, `outcome` before any file change — per FR-037, US5 scenario 3
- [ ] T086 [P] [US5] `tests/Iskender.Workflow.Tests/RoadmapRejectionTests.cs`: on rejection, authoritative files remain unchanged, the decision is recorded, and completed-spec history (records, revision IDs, real merge hashes) is preserved — per FR-038, US5 scenario 4

### Implementation for User Story 5

- [ ] T087 [P] [US5] `tools/Iskender.Workflow/Roadmap/ApprovalLedger.cs` model per data-model §8: `.iskender/state/approvals.json` entries `{ decisionId, timestamp, approvedDiffHash, reviewer, rationale, outcome ∈ {approved,rejected} }` (atomic write) — per FR-037/FR-038
- [ ] T088 [P] [US5] `tools/Iskender.Workflow/Roadmap/RoadmapChangeProposal.cs` model per data-model §7 — per FR-036
- [ ] T089 [US5] `tools/Iskender.Workflow/Roadmap/RoadmapReviewer.cs`: compare implementation against ADRs/constitution/remaining specs/dependency graph; classify material vs non-material; block material changes pending approval; never silently write approval-required changes — per FR-036/FR-037/FR-039 (depends T087, T088)
- [ ] T090 [P] [US5] Create the Roadmap Steward agent at `.claude/agents/roadmap-steward.md`: reviewer only (not an orchestrator), diff-only proposals, awaits human approval — per FR-036/FR-046
- [ ] T091 [P] [US5] Create the `/review-roadmap` skill at `.claude/skills/review-roadmap/SKILL.md`: resumable across approvals and CI/PR transitions; emits structured proposals; never silently writes approval-required changes — per FR-039
- [ ] T092 [P] [US5] Create bounded specialized reviewer helper agents under `.claude/agents/reviewers/` (e.g. `architecture-guard.md`, `security-isolation.md`, `test-reliability.md`, `documentation-audit.md`): bounded responsibility, no recursive spawning, no overlapping orchestration — per FR-046
- [ ] T093 [US5] Run the US5 test set and validate quickstart Scenario 8 — per SC-009

**Checkpoint**: US5 functional — diff-only roadmap proposals, human-gated approvals, history preserved.

---

## Phase 8: User Story 6 - Protected collaboration, CI & mandatory documentation merge gate (Priority: P2)

**Goal**: A GitHub Actions CI skeleton running all required checks, a deterministic path+label
documentation merge gate, CODEOWNERS/templates/security policy, and protected-`main` branch rules
documented (applied where admin permissions allow, otherwise flagged **pending** — never falsely
reported as applied).

**Independent Test**: Direct push to `main` rejected (or documented pending with owner + verification
command where admin rights absent); a PR touching a behavior-bearing path without a `docs/**` change
is blocked by the docs gate; running `ci.yml` on a sample change executes every check.

### Implementation for User Story 6

- [ ] T094 [US6] Create `.github/workflows/ci.yml` with the 10 ordered checks: (1) `dotnet restore` (locked) → (2) `dotnet format --verify-no-changes` → (3) `dotnet build -warnaserror` → (4) `dotnet test` (all assemblies) → (5) architecture tests → (6) docs/link/manifest/inventory consistency (`Iskender.Workflow.Cli validate-manifest` + link/inventory check) → (7) documentation merge gate → (8) secret scanning (Gitleaks) → (9) dependency hygiene (`dotnet list package --vulnerable` + Dependabot) → (10) SBOM (CycloneDX) + build-provenance attestation hook; no live cloud secrets required — per FR-042/D9, ci-and-merge-gates.contract.md
- [ ] T095 [US6] Implement the deterministic **documentation merge gate** job/script wired into `ci.yml`: changes under behavior-bearing paths (`src/Iskender.Core.Contracts/**`, `src/Iskender.Policy.Sdk/**`, `src/Iskender.Gateway.Host/**`, `.github/workflows/**`, `.iskender/grounding.yaml`, `docs/17-master-spec-inventory.md`) MUST include a matching `docs/**` (or relevant README/CLAUDE.md) change in the same PR, unless a `docs-not-required` label carrying written justification is applied; pure refactor/test-only changes outside those paths are never blocked — per FR-043/D10
- [ ] T096 [P] [US6] Create `.github/CODEOWNERS` gating high-risk paths (contracts/SDK surfaces, host/config, CI, grounding manifest/inventory) to their owners — per FR-041/FR-042, US6 scenario 4
- [ ] T097 [P] [US6] Create `.github/dependabot.yml` for NuGet (and GitHub Actions) dependency updates — per FR-042/FR-045
- [ ] T098 [P] [US6] Create `.github/pull_request_template.md` (checklist incl. docs-gate acknowledgement) — per FR-041, US6 scenario 4
- [ ] T099 [P] [US6] Create `.github/ISSUE_TEMPLATE/bug_report.md`, `.github/ISSUE_TEMPLATE/feature_request.md`, and `.github/ISSUE_TEMPLATE/config.yml` — per FR-041
- [ ] T100 [P] [US6] Create `SECURITY.md` at repo root (vulnerability reporting policy) — per FR-041/FR-045, Principle V
- [ ] T101 [P] [US6] Create `CONTRIBUTING.md` at repo root codifying branch conventions (`feature/`/`chore/`/`bugfix/` off protected `main`), PR/review rules, and secure-dev standards (least privilege, secret scanning, dependency integrity, no untrusted plugin execution, threat review of new public endpoints/identity/serialization), and release/versioning conventions (SemVer for SDK/API surfaces, the tag/release process, and never-reused ADR identifiers) — per FR-040/FR-041/FR-045/D13
- [ ] T102 [P] [US6] Create branch-protection documentation under `docs/` (+ reference from `CONTRIBUTING.md`): IaC-compatible description and the exact `gh api` commands for required reviews/checks, CODEOWNERS enforcement, and no-direct-push; include a **pending-rule** reporting convention (owner + verification command) for when admin rights/tokens are absent, performing no remote mutation — per FR-040/FR-041/FR-044/D11, SC-010
- [ ] T103 [US6] Wire the `validate-manifest` + docs/link/inventory consistency check (check 6) to invoke `Iskender.Workflow.Cli` so CI fails closed on manifest/inventory drift — per FR-042
- [ ] T104 [US6] Validate quickstart Scenario 9: direct push to `main` rejected (or documented pending); a docs-missing PR blocked by the gate; `ci.yml` executes every check on a sample change — per SC-010
- [ ] T104a [US6] Define the terminal `pr-merge-checkpoint` stage in the stage engine (`tools/Iskender.Workflow/State/StageSelector.cs`) and `/drive-speckit` skill: as the final stage after `roadmap-review`, it verifies CI checks are green, the documentation merge gate is satisfied, and required reviews are present, then records the **actual** merge commit/ref into the workflow-state `gitEvidence` (never fabricated); it performs **no** code modifications and **no** remote mutation without authorized access, and marks the spec complete — per FR-020/FR-044, Principle XI; covered by a `PrMergeCheckpointTests.cs` assertion that the stage is terminal, writes only real `gitEvidence`, and makes no code changes

**Checkpoint**: US6 functional — CI runs all checks, docs gate deterministic, branch rules applied or accurately pending.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: End-to-end validation and documentation consistency across all stories.

- [ ] T105 [P] Ensure `docs/README.md` exists and is linked prominently from the root `README.md`; run the link/consistency check — per FR-009
- [ ] T106 [P] Run all quickstart scenarios 1–10 end-to-end and record results — per quickstart.md
- [ ] T107 Final clean-clone offline validation: fresh clone, `dotnet restore && dotnet build -warnaserror && dotnet test` green with zero cloud provisioning — per SC-001
- [ ] T108 [P] Confirm Spec Kit preserved byte-for-byte (`git status --porcelain .specify` empty) and the active constitution resolves correctly — per SC-002/SC-011

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS all user stories**.
- **User Stories (Phase 3–8)**: All depend on Foundational completion.
  - **US1 (P1)** is independent of the workflow stories — can be built and demoed as the MVP alone.
  - **US2 (P1)** scaffolds the `Iskender.Workflow` state engine + `/drive-speckit` skill.
  - **US3 (P1)** adds discovery/manifest/prompt/constitution logic to the `Iskender.Workflow`
    project scaffolded in Foundational (T012–T014). Independently testable; its CLI commands feed
    US2's `preflight`.
  - **US4 (P2)** builds on US2's state record (budgets live inside it) and US3's discovery; adds
    budgets + convergence + blocker reporting.
  - **US5 (P2)** adds roadmap review + approval ledger; `roadmap-review` becomes eligible only after
    US4's convergence succeeds.
  - **US6 (P2)** adds CI/collaboration; CI check 6 consumes US3's `validate-manifest` CLI.
- **Polish (Phase 9)**: Depends on all desired user stories.

### Within Each User Story

- Tests are written first and expected to FAIL before implementation. **Exception (US1)**: the architecture/smoke tests (T024/T025) inspect the skeleton projects, so they are authored after those projects exist (T016–T023) and are listed after implementation within Phase 3.
- Models before services; services before CLI commands; CLI before the skill wiring.
- Story complete before moving to the next priority.

### Parallel Opportunities

- All `[P]` Setup tasks (T002–T005) run together.
- Foundational `[P]` scaffolds (T012–T014) run together.
- **US1**: all seven `src/` project creations (T016–T022) run in parallel; the two test projects
  (T024–T025) and the three onboarding assets (T027–T029) run in parallel.
- **US2/US3/US4/US5**: each story's test files are all `[P]` (different files); model classes marked
  `[P]` run together before the services that depend on them.
- Once Foundational is done, **US1, US2, and US3 (all P1)** can be worked by three developers in
  parallel; US4/US5/US6 follow.

---

## Parallel Example: User Story 1

```bash
# Launch all seven skeleton projects together:
Task T016: Create src/Iskender.Core.Contracts
Task T017: Create src/Iskender.Domain
Task T018: Create src/Iskender.Infrastructure
Task T019: Create src/Iskender.Providers.Abstractions
Task T020: Create src/Iskender.Policy.Sdk
Task T021: Create src/Iskender.Management
Task T022: Create src/Iskender.Gateway.Host

# Then onboarding assets together:
Task T027: README.md
Task T028: CLAUDE.md
Task T029: assets/iskender-logo.svg
```

## Parallel Example: User Story 3 (test-first)

```bash
# Launch all US3 test files together (they fail until T055–T061 land):
Task T046: ManifestValidatorValidTests.cs
Task T047: ManifestSchemaVersionTests.cs
Task T048: ManifestMissingKeyTests.cs
Task T049: ManifestMissingTargetTests.cs
Task T050: ManifestPathSafetyTests.cs
Task T051: RepoRootResolverTests.cs
Task T052: PromptRetrievalTests.cs
Task T053: MissingPromptTests.cs
Task T054: ConstitutionPrecedenceTests.cs
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories).
3. Complete Phase 3: User Story 1.
4. **STOP and VALIDATE**: fresh clone → restore/build/test green offline; README/CLAUDE.md/logo valid.
5. Demo the compilable skeleton + onboarding (the "bootstrap" MVP).

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → compilable skeleton + onboarding (MVP).
3. US2 → resumable stage engine + `/drive-speckit`.
4. US3 → deterministic discovery/manifest/prompt/constitution.
5. US4 → budgets + convergence governance + blockers.
6. US5 → human-governed roadmap review.
7. US6 → protected collaboration + CI + docs gate.
8. Polish → full quickstart validation.

### Parallel Team Strategy

After Foundational: Developer A → US1, Developer B → US2, Developer C → US3 (all P1, independent
feature areas sharing only the Foundational scaffolds). US4/US5/US6 follow once their P1
prerequisites land.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks.
- [Story] label maps each task to its user story for traceability.
- Tests are required by this spec; verify they fail before implementing.
- All state writes are atomic + lock-guarded; never reset persisted budget/no-progress counters on
  restart.
- The installed `.specify/` tree and `.iskender/grounding.yaml` are read-only — never reinstall,
  reinitialize, or edit Spec Kit.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
