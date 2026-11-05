# Feature Specification: ISK-0000 — Repository Bootstrap & Agentic Development Framework

**Feature Branch**: `feature/0000-repo-bootstrap`

**Created**: 2026-11-05

**Status**: Draft

**Input**: User description: "Create **ISK-0000 — Repository Bootstrap & Agentic Development Framework**. Use the complete authoritative bootstrap brief **@docs/14-spec-0000-bootstrap-brief.md**, plus @docs/13-development-standards-and-agent-workflows.md, @docs/15-constitution-prompt.md, @docs/16-architecture-decision-register.md and @docs/17-master-spec-inventory.md. Inspect and **preserve the already-installed Spec Kit installation and commands**; never reinstall or reinitialize Spec Kit. The initial constitution-generation prompt is discovered through `.iskender/grounding.yaml` (grounding.constitutionPrompt); the active constitution, once created, is `.specify/memory/constitution.md`. Scope: repository conventions, compilable .NET solution/module/test skeleton, CI/branch rules, CLAUDE.md and README, original kebab SVG, secure development standards, full resumable `drive-speckit` stage skill (including exact 3/problem, 10/run, 2 no-progress budgets), convergence separation and severity rules, actionable blocker summaries, Roadmap Steward/review-roadmap with human approval, manifest validation and approved-prompt retrieval. Explicitly exclude AI gateway business implementation, provider adapters, policy engine implementation and database domain schema. Design acceptance tests for missing/corrupt manifest, missing prompt, stage resumption, budget persistence, convergence passes 1–4, priority of active constitution, protected main and documents merge gate. **This invocation is SPECIFY ONLY:** generate the Spec Kit specification and do not execute any implementation, plan, tasks, repo installation, changes to existing Spec Kit or remote settings. The drive-speckit skill does not exist before ISK-0000 is implemented; manual Spec Kit bootstrap is required."

## Overview

ISK-0000 is the foundational specification for the Iskender Gateway project. It establishes the
repository, a compilable (but business-feature-free) .NET solution skeleton, protected
collaboration rules, and a durable, resumable **Claude Code + existing GitHub Spec Kit** agentic
development process. The deliverable is the *tooling, guardrails, and onboarding assets* that every
later specification (ISK-0001 and beyond) depends on — not any AI gateway runtime behavior.

This specification describes **requirements only**. It does not implement anything, does not modify
the existing Spec Kit installation, and does not change remote/Git settings.

## Clarifications

### Session 2026-11-05

- Q: Which workflow stages must `/drive-speckit` support, and in what dependency order? → A: The
  canonical `docs/13` stage set, in dependency order — `bootstrap/detect` (preflight) → `specify` →
  `clarify` → `plan` → `tasks` → `analyze` → `checklist` → `implement` → `converge` →
  `roadmap-review` → `pr-merge-checkpoint` — with exact Spec Kit commands discovered from the
  installed toolkit. The `checklist` stage generates an acceptance/quality checklist, the agent
  reviews the implementation against it, and any item the agent cannot confidently discharge is
  surfaced for explicit human review/approval.
- Q: How does the documentation merge gate decide a PR requires concurrent documentation updates? →
  A: Path + label driven — changes under designated behavior-bearing paths (public contracts/SDK
  surfaces, host/config, CI, grounding manifest/inventory) require a matching documentation change in
  the same PR, unless an explicit skip label (e.g. `docs-not-required`) with written justification is
  applied.
- Q: What is the acceptance rule for the manifest `schemaVersion`? → A: `schemaVersion` is a single
  integer major version; the validator accepts it when it equals a supported major (currently `1`)
  and reserves a major bump strictly for breaking changes, while additive/compatible fields are
  handled by tolerant parsing (unknown keys ignored or warned) rather than a version change. Any
  unsupported major, or a malformed/absent value, is blocked (fail-closed) with the offending value
  and owner action.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Clean-clone compilable skeleton & onboarding (Priority: P1)

A new contributor (human or agent) clones the repository on a machine with **no cloud credentials**
and, by following the repository's documented commands, restores, builds, and runs the test suite
successfully. They find a README explaining the mission and workflow, a `CLAUDE.md` describing the
agent integration and authority hierarchy, and a recognizable project logo — and the existing Spec
Kit installation is untouched.

**Why this priority**: Nothing else can be built, reviewed, or merged until there is a buildable,
navigable repository. This is the minimum viable product of a "bootstrap" and is demonstrable on its
own.

**Independent Test**: Perform a fresh clone into an environment with no cloud secrets, run the
documented restore/build/test commands, and confirm a green build; confirm the Spec Kit files are
unchanged from their pre-bootstrap state; confirm README, `CLAUDE.md`, and the logo asset exist and
are valid.

**Acceptance Scenarios**:

1. **Given** a fresh clone with no cloud credentials configured, **When** the contributor runs the
   documented restore, build, and test commands, **Then** all steps succeed and no cloud
   provisioning is attempted.
2. **Given** the repository skeleton, **When** the solution is built, **Then** every project (host,
   core contracts, domain/modules, infrastructure, provider abstractions, policy SDK, management,
   and test assemblies) compiles while containing no gateway business logic, provider adapter code,
   policy engine, or database domain schema.
3. **Given** the pre-bootstrap Spec Kit installation, **When** the bootstrap is complete, **Then**
   the existing `.specify` scripts, templates, and memory are preserved byte-for-byte except for
   independently approved integrations, and Spec Kit was never reinstalled or reinitialized.
4. **Given** the onboarding assets, **When** a contributor opens the README, **Then** it states the
   mission, license, architecture overview, build/test instructions, Spec Kit how-to, future
   plugin/policy scope, contributor workflow, and links prominently to `docs/README.md`; the logo
   is an original, self-contained, accessible kebab-inspired SVG with `title`/`desc`.

---

### User Story 2 - Resumable one-stage-per-invocation workflow driver (Priority: P1)

A maintainer invokes `/drive-speckit` with no stage argument. The skill discovers the repository
root, manifest, inventory/status, active constitution, Spec Kit artifacts, current branch/worktree,
and last checkpoint; validates invariants; and executes **exactly one** eligible workflow stage,
then stops at the stage boundary. If the process is interrupted and the maintainer invokes it again,
work resumes from verified state without repeating completed work and without corrupting state.

**Why this priority**: The resumable stage engine is the central value of the agentic framework; it
is what lets long-running agent work proceed safely across interruptions and reviews.

**Independent Test**: Using mock Spec Kit fixtures and a simulated spec, drive a sequence of stages,
interrupt mid-stage, re-invoke, and confirm the same stage resumes from the last verified checkpoint
with no duplicated completed work; confirm that `implement` works all remaining tasks and then stops
**before** `converge` rather than auto-entering it.

**Acceptance Scenarios**:

1. **Given** no prior state for a new spec, **When** `/drive-speckit` is invoked, **Then** it selects
   the correct eligible stage in dependency order (not by scanning the current directory) and records
   durable, versioned, atomically written state.
2. **Given** an `implement` stage with multiple remaining tasks, **When** the skill runs, **Then** it
   works all remaining tasks until completed, blocked, or user input is required, and does **not**
   launch `converge` on its own.
3. **Given** an interrupted stage, **When** the skill is re-invoked, **Then** it resumes the same
   stage from verified artifacts without repeating completed operations and without resetting
   persisted counters.
4. **Given** two agents attempting to drive the same spec concurrently, **When** the second
   invocation starts, **Then** worktree/spec contention blocks rather than corrupts state.
5. **Given** any invocation, **When** it completes, **Then** it emits a concise human summary headed
   `drive-speckit (<stage>)` containing the spec ID and title, executed stage, work done, current
   status, next stage/action, and the re-invocation instruction when appropriate.

---

### User Story 3 - Deterministic grounding discovery & verbatim approved-prompt retrieval (Priority: P1)

An agent entering the `specify` stage for any spec discovers all paths through
`.iskender/grounding.yaml`, validates the manifest, resolves the active constitution with correct
precedence, and retrieves the spec's approved `/speckit.specify` prompt **verbatim** from the master
inventory. When any input is missing, corrupt, or unsafe, the agent blocks with an actionable error
instead of guessing a path or synthesizing a prompt.

**Why this priority**: Deterministic, tamper-evident discovery is the trust anchor for the whole
workflow; without verbatim approved prompts and correct constitution precedence, agent work can
silently drift from approved scope.

**Independent Test**: Provide valid and deliberately broken manifests/inventories and confirm that
valid inputs resolve correctly while each failure mode blocks with a precise, owner-addressed
message; confirm the active constitution overrides the bootstrap constitution prompt once it exists.

**Acceptance Scenarios**:

1. **Given** the supplied `.iskender/grounding.yaml`, **When** the manifest is validated, **Then** a
   supported `schemaVersion` and all required keys are confirmed, `grounding.root` resolves relative
   to the repository root, and document paths resolve relative to that root.
2. **Given** a manifest with an unsupported schema, missing required key, absent target file, or a
   path that escapes the repository root (traversal or disallowed absolute path), **When** it is
   validated, **Then** validation fails with the exact reason, the offending path/key, and owner
   instructions — and no stage proceeds.
3. **Given** a request to begin `specify` for a spec, **When** the approved prompt is retrieved,
   **Then** it is returned byte-for-byte identical to the fenced prompt in the master inventory for
   that `ISK-xxxx` entry.
4. **Given** an inventory entry whose approved prompt is missing, **When** `specify` is attempted,
   **Then** the agent blocks and never generates or substitutes a prompt.
5. **Given** both the bootstrap constitution prompt (`docs/15-constitution-prompt.md`) and an active
   `.specify/memory/constitution.md`, **When** governance precedence is resolved, **Then** the active
   constitution is authoritative and the bootstrap prompt is treated only as historical input.
6. **Given** execution from a nested working directory or a Git worktree, **When** the repository
   root is resolved, **Then** it is found independently of the shell's current directory.

---

### User Story 4 - Bounded repair budgets, convergence severity governance & actionable blockers (Priority: P2)

While implementing and converging a spec, the framework enforces bounded repair budgets and strict
convergence severity rules so that agents cannot loop forever, cannot reset their budgets by
restarting, and cannot silently suppress findings. Any blocked outcome produces a precise,
actionable diagnosis.

**Why this priority**: These guardrails prevent runaway cost and quiet quality erosion; they are
essential for trustworthy autonomy but build on the stage engine (US2) and discovery (US3).

**Independent Test**: With synthetic failing/looping fixtures, verify the exact budgets (3 meaningful
repairs per problem, 10 failed repairs per run, 2 consecutive no-progress cycles) are enforced and
persisted across restarts; drive convergence through passes 1–4 with findings of varied severity and
confirm the separation and severity rules; confirm every blocked path emits the full blocker
contract.

**Acceptance Scenarios**:

1. **Given** a single recurring problem, **When** the agent attempts repairs, **Then** it stops after
   3 meaningful (hypothesis- or action-changing) failed repairs for that problem; repeating an
   unchanged command does not count as progress.
2. **Given** an implement run accumulating failures, **When** 10 failed repairs occur in the run, or 2
   consecutive no-progress cycles occur, **Then** the run stops and these counters persist across
   invocations and are not reset by a process/skill restart.
3. **Given** convergence passes 1 through 3, **When** findings exist, **Then** all findings including
   Low are scheduled for remediation, and remediation occurs only in a **separate** `implement`
   invocation — `converge` never modifies code itself.
4. **Given** convergence pass 4 or later, **When** only Low findings remain, **Then** they may be
   explicitly accepted with recorded evidence, owner, and rationale; **Given** any Medium, High, or
   Critical finding on any pass, **Then** it always blocks and returns to implement/escalation.
5. **Given** a convergence pass with zero findings, **When** it completes, **Then** success is
   permitted immediately, even on pass 1; severities are never silently downgraded.
6. **Given** any blocked result, **When** the summary is emitted, **Then** it names the blocked
   stage/spec/task, the precise reason, evidence/error, tried hypotheses/commands and outcomes, exact
   unblock steps, the responsible actor, and the stage/task that will resume afterward; unknown root
   causes are labeled unknown with diagnostic commands rather than a vague "blocked".

---

### User Story 5 - Human-governed roadmap review (Priority: P2)

After convergence, a maintainer runs the Roadmap Steward via `/review-roadmap`. It compares the
actual implementation against approved ADRs, the active constitution, remaining specs/prompts, and
the dependency/milestone graph, then produces an evidence-backed, **diff-only** change proposal.
Material changes wait for explicit human approval before any authoritative file is modified.

**Why this priority**: Independent roadmap review catches implementation drift, but it is a reviewer
that must never rewrite product commitments autonomously; it follows the build/converge stages.

**Independent Test**: Run the review against a fixture with intentional drift and confirm it produces
a structured proposal with evidence and exact diffs; approve one proposal and reject another, and
confirm approvals are logged while rejected proposals leave authoritative files unchanged and
preserve history.

**Acceptance Scenarios**:

1. **Given** a converged spec, **When** `/review-roadmap` runs, **Then** it outputs a structured
   proposal citing commit/file/test evidence, the issue, affected spec IDs, exact proposed diffs,
   dependency/milestone impact, whether it changes approved requirements, a rollback option, and the
   decision owner.
2. **Given** a material change proposal (scope, inclusion/exclusion, new/deleted specs, prompt intent,
   security boundaries, MVP cutline, new architectural decision, or constitution change), **When** no
   human approval exists, **Then** no authoritative file is modified and the review blocks with the
   affected IDs and exact unblock action.
3. **Given** an approved proposal, **When** it is accepted, **Then** the approval records a decision
   ID, timestamp, approved diff hash, reviewer, and rationale before any file changes.
4. **Given** a rejected proposal, **When** it is declined, **Then** authoritative files remain
   unchanged, the decision is recorded, and completed-spec history (records, revision IDs, real merge
   hashes) is preserved.

---

### User Story 6 - Protected collaboration, CI & mandatory documentation merge gate (Priority: P2)

A contributor proposes changes only through branches (`feature/`, `chore/`, `bugfix/`) off a
protected `main`, via reviewed PRs with passing CI. The CI skeleton validates build/format/tests,
architecture/dependency rules, documentation/link/manifest/inventory consistency, secret scanning,
and dependency hygiene. A PR that changes behavior without the required concurrent documentation
updates is blocked by the documentation merge gate. Where repository admin permissions are
unavailable, the required remote settings are accurately documented as pending rather than falsely
reported as applied.

**Why this priority**: Protected collaboration and the docs merge gate keep the record truthful and
reviewable, but they frame the work produced by the earlier stories.

**Independent Test**: Attempt a direct push to `main` and confirm it is rejected (or documented as a
required pending rule where permissions are missing); open a PR that lacks required doc updates and
confirm the documentation gate blocks it; run the CI skeleton on a sample change and confirm each
check executes.

**Acceptance Scenarios**:

1. **Given** protected `main`, **When** a direct push to `main` is attempted, **Then** it is rejected;
   where admin permissions are unavailable to apply the rule, the exact required action, owner, and
   verification command are documented as pending and the rule is not falsely reported as applied.
2. **Given** a PR whose code changes lack the required concurrent documentation updates, **When** the
   documentation merge gate runs, **Then** the PR is blocked until docs are aligned.
3. **Given** the CI skeleton, **When** it runs on a PR, **Then** it executes restore, format, build,
   all tests, architecture/dependency checks, docs/link/manifest/inventory consistency, secret
   scanning, and dependency hygiene, with SBOM/provenance hooks present.
4. **Given** CODEOWNERS, PR/issue templates, a security reporting policy, and release/versioning
   conventions, **When** a contributor opens a PR, **Then** the owners and templates apply and
   high-risk paths require their owners' review.
5. **Given** no authorized remote tooling access, **When** branch rules would be changed, **Then** no
   automatic remote mutation occurs.

### Edge Cases

- **Corrupt or unparseable manifest**: `.iskender/grounding.yaml` is malformed YAML or has an
  unsupported `schemaVersion` → block with the parser/validation error, the file path, and owner
  instructions; do not proceed with partial assumptions.
- **Out-of-root path**: a `grounding.*` path uses `..` traversal or a disallowed absolute path →
  reject as unsafe with the offending value.
- **Missing grounding file**: a referenced document does not exist → block with the exact missing
  path; never substitute a different file.
- **Missing approved prompt**: an inventory entry lacks its fenced `/speckit.specify` prompt → block;
  never synthesize or template a replacement.
- **Constitution absent vs present**: before `.specify/memory/constitution.md` exists, the bootstrap
  prompt is the only input; once it exists, it takes precedence and must not be silently overwritten.
- **Interrupted/aborted stage**: a crash mid-stage → on re-invocation, resume from the last atomic
  checkpoint with intact counters; never double-apply completed work.
- **Concurrent drivers**: two invocations target the same spec/worktree → the later one blocks on a
  lock rather than corrupting shared state.
- **Budget-exhausted but new authorized hypothesis**: a genuinely new, externally changed cause may
  justify a documented, approval-bounded counter reset — never an automatic loop reset.
- **Repeated identical attempt**: re-running an unchanged failing command does not count as a
  meaningful repair or as progress.
- **Clean clone without credentials**: build/test must succeed with no cloud secrets and must not
  attempt cloud provisioning.
- **Spec Kit command mismatch**: if installed Spec Kit commands differ from expectations, diagnose and
  adapt to the installed commands or block for approval — never reinstall or reinitialize Spec Kit.
- **Premature implementation request during specify**: if asked to implement ISK-0000 during the
  specify stage, stage discipline still forbids it.

## Requirements *(mandatory)*

### Functional Requirements

**Spec Kit preservation & bootstrap discipline**

- **FR-001**: The bootstrap MUST inspect and record the installed Spec Kit version and supported
  commands before any action, and MUST preserve `.specify` scripts, templates, and memory
  byte-for-byte except for independently approved integrations.
- **FR-002**: The bootstrap MUST NOT reinstall, reinitialize, or overwrite the existing Spec Kit
  installation; command mismatches MUST be diagnosed and adapted to, or blocked for approval.
- **FR-003**: The framework MUST recognize that `/drive-speckit`, Roadmap Steward, and
  `/review-roadmap` do not exist before ISK-0000 is implemented, and that the initial bootstrap uses
  the installed Spec Kit commands manually.

**Repository skeleton & conventions**

- **FR-004**: The repository MUST provide `.gitignore` (for .NET, Next.js, IDE, OS, secrets,
  artifacts), `.gitattributes` for deterministic line endings, and `.editorconfig` for C#, Markdown,
  JSON, and YAML formatting.
- **FR-005**: The repository MUST provide a .NET solution with empty but **compilable** projects for
  the host, core contracts, domain/modules, infrastructure, provider abstractions, policy SDK,
  management, and test assemblies, with predictable namespace conventions.
- **FR-006**: The skeleton MUST include reference-direction (architecture) tests or CI checks that
  enforce approved module dependency direction.
- **FR-007**: The repository MUST include documented, reproducible `restore`/`build`/`test` commands
  and MUST build and test successfully on a clean clone with no cloud credentials, without attempting
  cloud provisioning; optional local/Compose dependency scaffolding MAY be provided but MUST NOT be
  required.
- **FR-008**: Dependency and version pinning MUST be tracked, and license notices MUST be clean and
  present.

**Onboarding & identity assets**

- **FR-009**: The repository MUST include a top-level README covering mission, license, architecture
  overview, build/test instructions, Spec Kit how-to, future plugin/policy scope, contributor
  workflow, and a prominent link to `docs/README.md`.
- **FR-010**: The repository MUST include a `CLAUDE.md` covering project purpose, the document
  authority hierarchy, manifest discovery, current-branch and protected-`main` rules, Spec Kit
  integration, review gates, command usage, fast local validation, and privacy/integrity warnings.
- **FR-011**: The repository MUST include an original, self-contained, accessible kebab-inspired
  Iskender SVG logo with `title`/`desc`, not a copy of trademarked or downloaded artwork.

**Grounding manifest validation & discovery**

- **FR-012**: The framework MUST resolve the repository root robustly via Git/known markers,
  independently of the shell's current working directory, including from nested directories and Git
  worktrees.
- **FR-013**: The framework MUST parse `.iskender/grounding.yaml`, validate a supported
  `schemaVersion` and all required keys, resolve `grounding.root` relative to the repository root,
  resolve `grounding.*` document paths relative to that root, and resolve `speckit.*` paths relative
  to the repository root. `schemaVersion` is a single integer **major** version (currently `1`); the
  framework MUST accept a manifest whose major equals a supported major, MUST reserve a major bump
  strictly for breaking changes (handling additive/compatible fields via tolerant parsing of unknown
  keys), and MUST fail closed on any unsupported major or malformed/absent value.
- **FR-014**: The framework MUST detect and reject path traversal and disallowed absolute paths, and
  MUST detect absent target files, blocking with the exact offending value and owner instructions.
- **FR-015**: The framework MUST NOT invent a second registry, guess missing paths, or proceed on
  partial/ambiguous manifest data.

**Approved-prompt retrieval & constitution precedence**

- **FR-016**: The framework MUST retrieve a spec's approved `/speckit.specify` prompt **verbatim**
  (byte-for-byte) from the master inventory by keying off the literal `ISK-xxxx` heading and fenced
  prompt label, and MUST NOT synthesize a prompt from a title.
- **FR-017**: A missing approved prompt MUST block the `specify` stage with an actionable error; the
  framework MUST NOT generate or substitute a replacement.
- **FR-018**: The framework MUST discover the bootstrap constitution prompt via
  `grounding.constitutionPrompt` for bootstrap only; once `.specify/memory/constitution.md` exists it
  MUST be treated as authoritative, and MUST NOT be overwritten without explicit human approval.

**`drive-speckit` stage engine**

- **FR-019**: `/drive-speckit` MUST, on invocation with no stage argument, discover repository root,
  manifest/schema, inventory/status, active constitution, Spec Kit artifacts, current worktree/branch,
  last checkpoint, and pending approvals; printed stage names are informational only.
- **FR-020**: The skill MUST select or resume **exactly one** eligible workflow stage per invocation,
  following dependency order and approved status, never speculative current-directory scanning. The
  canonical workflow stages, in dependency order, are `bootstrap/detect` (preflight), `specify`,
  `clarify`, `plan`, `tasks`, `analyze`, `checklist`, `implement`, `converge`, `roadmap-review`, and
  `pr-merge-checkpoint`; the exact underlying Spec Kit commands MUST be discovered from the installed
  toolkit rather than assumed.
- **FR-021**: The skill MUST persist durable, versioned, small, machine-readable state with atomic
  writes and locking, recording entered timestamp/runner ID, source prompt/config hashes, artifact
  paths, stage status, last confirmed task, actual Git commit/ref evidence, convergence finding
  ledger, budgets, repair fingerprints, partial-failure diagnostics, and the human approval ledger.
- **FR-022**: The skill MUST resume re-entrantly from verified artifacts, avoiding repeated operations
  where sufficient evidence already exists, and MUST block (not corrupt) on worktree/spec contention.
- **FR-023**: In the `implement` stage, the skill MUST work all remaining tasks until completed,
  blocked, or user input is required, and MUST NOT launch `converge` itself.
- **FR-024**: On entering `specify` for a spec, the skill MUST load the approved exact prompt from the
  inventory and MUST NOT regenerate a missing prompt or substitute a template.
- **FR-024a**: The skill MUST provide a `checklist` stage (positioned after `analyze` and before
  `implement`) that generates an acceptance/quality checklist for the spec and reviews the
  implementation against it; any item the agent cannot confidently discharge MUST be surfaced for
  explicit human review/approval rather than silently passed. The `checklist` stage MUST NOT modify
  code — any resulting remediation occurs only in a separate `implement` invocation, consistent with
  the stage-separation rule in FR-030.

**Bounded repair budgets**

- **FR-025**: The framework MUST limit repairs to **3 meaningful failed repairs per problem**, **10
  failed repairs per invocation run**, and **2 consecutive no-progress cycles**; a meaningful repair
  changes a hypothesis or action and records an expected observation and result.
- **FR-026**: Per-problem repair counts and no-progress state MUST persist across invocations and MUST
  NOT be reset by restarting the skill/process; a documented, approval-bounded reset MAY apply only
  when a genuinely new authorized hypothesis or externally changed cause exists.
- **FR-027**: On exhausting a budget, the skill MUST stop and issue an actionable diagnosis including
  who may authorize escalation, and MUST NOT skip acceptance, disable tests, or alter approved
  requirements to make progress.
- **FR-028**: The framework MUST persist edits, results, and checkpoints atomically after each
  task/attempt.

**Convergence governance**

- **FR-029**: A `converge` stage MUST evaluate implementation, design constraints, security, tests,
  and acceptance against the current spec and write a versioned finding ledger, each finding with a
  stable ID, severity, and evidence.
- **FR-030**: Convergence passes 1–3 MUST schedule remediation of **all** findings (including Low),
  and remediation MUST occur only in a **separate** `implement` invocation; `converge` MUST NOT modify
  code as a hidden second stage.
- **FR-031**: On pass 4 and later, Low-only findings MAY be explicitly accepted with recorded
  evidence/owner/rationale when no remaining release gate prohibits them; Medium, High, and Critical
  findings MUST always block and return to implement/escalation.
- **FR-032**: Zero findings MUST permit immediate success, even on pass 1; severities MUST NOT be
  silently downgraded; pass numbers and earlier findings/repair evidence MUST be tracked across
  restarts.
- **FR-033**: After successful convergence, Roadmap Steward review MUST become eligible as an
  independent stage; `converge` MUST NOT auto-run it.

**Mandatory summaries & blocker contract**

- **FR-034**: Every `/drive-speckit` invocation MUST emit a concise human summary headed
  `drive-speckit (<stage>)` with spec ID and title, executed stage, work done, current status, next
  stage/action, and the re-invocation instruction when appropriate.
- **FR-035**: Every blocked result MUST include the blocked stage/spec/task, precise reason,
  evidence/error, tried hypotheses/commands and outcomes, exact unblock steps, the responsible actor,
  and the stage/task that will resume afterward; an unknown root cause MUST be labeled unknown with
  diagnostic commands rather than a vague "blocked".

**Roadmap Steward & human governance**

- **FR-036**: The Roadmap Steward and `/review-roadmap` MUST run after implementation and convergence
  as reviewers (not competing orchestrators) and MUST produce structured, evidence-backed, diff-only
  change proposals (evidence, issue, affected spec IDs, exact diffs, dependency/milestone impact,
  requirement-change assessment, rollback option, decision owner).
- **FR-037**: Material changes MUST receive explicit human approval before any authoritative file is
  modified; approval MUST record a decision ID, timestamp, approved diff hash, reviewer, and
  rationale.
- **FR-038**: On rejection, authoritative files MUST remain unchanged, the decision MUST be recorded,
  and completed-spec history (records, revision IDs, and real merge hashes) MUST be preserved; routine
  non-material doc sync that reflects approved implementation is required before merge.
- **FR-039**: `/review-roadmap` MUST be resumable across approvals and CI/PR transitions and MUST
  never silently write approval-required changes.

**Protected collaboration, CI & documentation gate**

- **FR-040**: All work MUST proceed on `feature/`, `chore/`, or `bugfix/` branches off a protected
  `main`; direct commits/pushes to `main` MUST be prohibited, and PRs MUST require successful CI and
  review.
- **FR-041**: The repository MUST provide branch-rule configuration recommendations or IaC-compatible
  documentation for protected `main`, required reviews/checks, CODEOWNERS, PR/issue templates,
  security reporting guidance, and release/versioning conventions.
- **FR-042**: A GitHub Actions CI skeleton MUST provide restore, formatting, build, all tests,
  architecture/dependency checks, docs/link/manifest/inventory consistency validation, secret
  scanning, dependency hygiene, and SBOM/provenance hooks, with codeowner paths.
- **FR-043**: Documentation maintenance MUST be enforced as a mandatory merge gate: a PR that changes
  behavior without the required concurrent documentation updates MUST be blocked. The gate MUST use a
  deterministic path- and label-driven rule: changes touching designated behavior-bearing paths
  (public contracts/SDK surfaces, host/config, CI, and the grounding manifest/inventory) MUST include
  a matching documentation change in the same PR, unless an explicit skip label (e.g.
  `docs-not-required`) carrying written justification is applied; pure refactor or test-only changes
  outside those paths MUST NOT be blocked.
- **FR-044**: When repository admin rights/tokens are missing, the framework MUST document the exact
  required actions, owner, and verification commands as pending, MUST NOT falsely report remote rules
  as applied, and MUST NOT perform automatic remote mutations without authorized tool access.

**Secure development standards**

- **FR-045**: The repository MUST codify secure development standards: least privilege, secret
  scanning, dependency integrity, no untrusted plugin execution, and explicit threat review of new
  public endpoints/identity flows/serialization — recorded as repository guidance and enforced via the
  applicable CI checks.

**Specialized reviewer helpers (bounded responsibility)**

- **FR-046**: The framework MAY provide bounded specialized reviewer helpers (e.g., hot-path/DI,
  security/isolation, provider conformance, policy/plugin, test reliability, documentation audit,
  architecture guard); only `drive-speckit` MUST orchestrate stage transitions and only the Roadmap
  Steward MUST propose material roadmap diffs, with no recursive agent spawning or overlapping
  orchestration.

**Scope boundary (non-goals)**

- **FR-047**: This specification MUST exclude AI gateway business/inference implementation, provider
  adapter implementations, policy engine implementation, and database domain schema; the skeleton MUST
  compile without any such business code.

### Key Entities *(include if feature involves data)*

- **Grounding manifest** (`.iskender/grounding.yaml`): authoritative path registry; attributes include
  `schemaVersion`, `grounding.root`, grounding document paths (inventory, constitution prompt,
  bootstrap brief, decision register), and `speckit.*` paths. Validated for schema, required keys, and
  path safety.
- **Master spec inventory entry**: one `ISK-xxxx` record holding title, scope, inclusions/exclusions,
  milestone, dependencies, workflow status, planning revision, final merge commit, authoritative
  docs/ADRs, acceptance evidence, and the **literal fenced approved `/speckit.specify` prompt**.
- **Active constitution** (`.specify/memory/constitution.md`): authoritative governance document that
  supersedes the bootstrap constitution prompt once it exists.
- **Workflow state record**: durable, versioned, atomically written per-spec state — current/last
  stage, timestamps, runner ID, source prompt/config hashes, artifact paths, last confirmed task,
  actual Git commit/ref evidence, lock ownership.
- **Convergence finding ledger**: versioned list of findings, each with a stable ID, severity
  (Low/Medium/High/Critical), evidence, pass number, and remediation/acceptance status.
- **Repair budget counters**: per-problem repair count, per-run failed-repair count, consecutive
  no-progress count, and problem fingerprints — all persisted across invocations.
- **Roadmap change proposal**: evidence, issue, affected spec IDs, exact diffs, dependency/milestone
  impact, requirement-change assessment, rollback option, decision owner, and material/non-material
  classification.
- **Approval ledger entry**: decision ID, timestamp, approved diff hash, reviewer, rationale, and
  outcome (approved/rejected).
- **Solution/module skeleton**: the set of compilable projects (host, core contracts, domain/modules,
  infrastructure, provider abstractions, policy SDK, management, tests) and their approved dependency
  direction.
- **CI pipeline definition**: the set of required checks (restore, format, build, test,
  architecture/dependency, docs/link/manifest/inventory consistency, secret scan, dependency hygiene,
  SBOM/provenance) and the documentation merge gate.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A fresh clone with no cloud credentials completes the documented restore, build, and
  test sequence successfully on the first attempt, with zero cloud-provisioning attempts.
- **SC-002**: The existing Spec Kit installation is preserved with **zero** unintended file
  modifications (byte-for-byte identical except independently approved integrations), and the active
  constitution resolves correctly.
- **SC-003**: 100% of manifest failure cases (unsupported schema, missing required key, absent target
  file, traversal/absolute path) are blocked with a message naming the offending value and the owner
  action; zero cases proceed silently.
- **SC-004**: The approved starting prompt for a spec is retrieved byte-for-byte identical to the
  inventory in 100% of valid cases, and 100% of missing-prompt cases block without any synthesized
  prompt.
- **SC-005**: After an interruption at any stage, re-invocation resumes the same stage with **zero**
  repeated completed work and **zero** reset of persisted repair/no-progress counters.
- **SC-006**: Synthetic tests demonstrate the exact budgets — stopping at 3 meaningful failed repairs
  per problem, 10 failed repairs per run, and 2 consecutive no-progress cycles — in 100% of trials.
- **SC-007**: Convergence behavior is demonstrated across passes 1–4: passes 1–3 remediate all
  severities via a separate implement invocation; pass 4+ accepts Low-only with rationale; any
  Medium+ blocks; zero findings succeed immediately; no severity is ever silently downgraded.
- **SC-008**: 100% of `/drive-speckit` invocations emit the required human summary, and 100% of
  blocked results include every required blocker-contract field.
- **SC-009**: 100% of Roadmap Steward material-change proposals are diff-only and withheld pending
  approval; rejected proposals leave authoritative files unchanged and preserve completed-spec
  history; every approval is logged with its required fields.
- **SC-010**: A direct push to `main` is prevented (or accurately documented as a pending required
  rule where admin permissions are unavailable), and a PR lacking required documentation updates is
  blocked by the documentation merge gate in 100% of trials.
- **SC-011**: Once an active constitution exists, it takes precedence over the bootstrap constitution
  prompt in 100% of governance-resolution checks, and is never overwritten without recorded human
  approval.

## Assumptions

- The spec directory is numbered `0000` (four-digit) to match the project's `ISK-0000` convention,
  `grounding.yaml`'s `workflow.initialSpec: '0000'`, and the `feature/0000-repo-bootstrap` branch,
  rather than the generic three-digit sequential default in `init-options.json`.
- The grounding documents live under `docs/` (as declared by `grounding.root: docs` in
  `.iskender/grounding.yaml`); the `@docs/...` input paths are authoritative.
- The target runtime is .NET 10 / ASP.NET Core and the management UI is Next.js, per the active
  constitution and ADRs; exact library choices (e.g., EF Core/Dapper split, CEL evaluator) remain
  recommendations to be settled by later specs, not fixed here.
- "Compilable skeleton" means projects build and reference-direction checks pass while containing no
  gateway business logic, provider adapters, policy engine, or database domain schema.
- Workflow state is stored in a durable, versioned, atomically written location within the repository
  (or a clearly documented recovery path); the exact storage format/location is a planning decision.
- Remote GitHub branch-protection and settings may not be mutable in all environments; where admin
  access is absent, required rules are documented as pending rather than applied.
- This is an approved bootstrap *requirement scope*; unobserved repository state is to be discovered
  during the implementation stage, not assumed here.
- This invocation is SPECIFY ONLY: no implementation, plan, tasks, repository installation, Spec Kit
  changes, or remote-setting changes are performed.
