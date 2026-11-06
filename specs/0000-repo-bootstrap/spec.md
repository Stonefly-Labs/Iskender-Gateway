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
  installed toolkit. The `checklist` stage generates requirements-quality checklists, the agent
  performs the review and marks what it can discharge, and only items needing human input are
  surfaced (refined in the checklist-review session below).
- Q: How does the documentation merge gate decide a PR requires concurrent documentation updates? →
  A: Path + label driven — changes under designated behavior-bearing paths (public contracts/SDK
  surfaces, host/config, CI, grounding manifest) require a matching documentation change in
  the same PR, unless an explicit skip label (e.g. `docs-not-required`) with written justification is
  applied.
- Q: What is the acceptance rule for the manifest `schemaVersion`? → A: `schemaVersion` is a single
  integer major version; the validator accepts it when it equals a supported major (currently `1`)
  and reserves a major bump strictly for breaking changes, while additive/compatible fields are
  handled by tolerant parsing (unknown keys ignored or warned) rather than a version change. Any
  unsupported major, or a malformed/absent value, is blocked (fail-closed) with the offending value
  and owner action.

### Session 2026-11-05 (checklist review)

- Q: Who holds the review-owner roles? → A: Every role in the constitution's "Quality Gates & Review
  Owners" table is currently held by the sole maintainer, GitHub `christopherhouse`.
- Q: What does roadmap approval gate? → A: The material categories listed in FR-037, including
  milestones. The active spec's own pre-merge stage edits and the inventory's workflow-status and
  final-merge-commit fields are not gated unless they change a material category.
- Q: What does the `checklist` stage review? → A: What `speckit-checklist` normally reviews: the
  quality of the requirements in the spec and design artifacts, not the implementation. The agent
  generates the checklists, performs the review, marks what is satisfied, and raises only items
  that need human input.
- Q: How many worktrees must locking cover? → A: One. There is a sole implementer working in a single
  worktree, so cross-worktree contention is out of scope.
- Q: How is the final merge hash recorded when `main` is protected? → A: In a final records-only
  `chore/` PR, opened after the spec's converged PR has been merged.
- Q: How are the workflow rules built and verified? → A: `/drive-speckit` and `/review-roadmap` are
  plain Claude Code skills that automate the Spec Kit workflow. There is no CLI or compiled library
  behind them and no unit tests of them; they are verified by use. The ISK-0000 prompt and evidence were
  updated to match (RC-20261105-002). Architecture tests of the .NET
  skeleton remain (FR-006).

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
5. **Given** the repository root, **When** a contributor inspects its conventions, **Then**
   `.gitignore`, `.gitattributes`, `.editorconfig`, `global.json`, central package versions, and the
   license notice are all present.
6. **Given** the skeleton, **When** a project reference that violates the approved dependency
   direction is introduced, **Then** the architecture tests fail, even if the projects contain no
   types.
7. **Given** the onboarding assets, **When** an agent opens `CLAUDE.md`, **Then** it states the project
   purpose, authority hierarchy, manifest discovery, branch rules, Spec Kit integration, review gates,
   command usage, fast local validation, and privacy/integrity warnings.

---

### User Story 2 - Resumable one-stage-per-invocation workflow driver (Priority: P1)

A maintainer invokes `/drive-speckit` with no stage argument. The skill discovers the repository
root, manifest, inventory/status, active constitution, Spec Kit artifacts, current branch/worktree,
and last checkpoint; validates invariants; and executes **exactly one** eligible workflow stage,
then stops at the stage boundary. If the process is interrupted and the maintainer invokes it again,
work resumes from verified state without repeating completed work and without corrupting state.

**Why this priority**: The resumable stage engine is the central value of the agentic framework; it
is what lets long-running agent work proceed safely across interruptions and reviews.

**Independent Test**: Use `/drive-speckit` on a real spec: drive a sequence of stages, interrupt
mid-stage, re-invoke, and confirm the same stage resumes from the last verified checkpoint with no
duplicated completed work; confirm that `implement` works all remaining tasks and then stops
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
6. **Given** a spec that has completed `analyze`, **When** the `checklist` stage runs, **Then** the agent
   generates the requirements-quality checklists, performs the review, marks the items it can
   discharge, makes no code changes, and blocks only on items that need human input.
7. **Given** more than one eligible spec and no spec in progress, **When** `/drive-speckit` is invoked,
   **Then** it selects the lowest-numbered `ISK-xxxx` whose dependencies are satisfied and whose
   approved prompt is present.

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
2. **Given** a manifest that is absent or unparseable, or has an unsupported schema, a missing required
   key, an absent, empty, or unreadable target file, or a path that escapes the repository root
   (traversal, disallowed absolute path, or symlink), **When** it is validated, **Then** validation
   fails with the exact reason, the offending path/key, and owner instructions — and no stage
   proceeds.
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

**Independent Test**: In real use, confirm the skill stops at the exact budgets (3 meaningful repairs
per problem, 10 failed repairs per run, 2 consecutive no-progress cycles), that they persist in the
state record across restarts, that convergence follows the separation and severity rules, and that
every blocked result carries the full blocker contract.

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

**Independent Test**: Run the review after a real convergence and confirm any drift produces a
structured proposal with evidence and exact diffs; when proposals are approved or rejected, confirm
approvals are logged while rejected proposals leave authoritative files unchanged and preserve
history.

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
5. **Given** a proposal awaiting approval, **When** `/review-roadmap` is re-invoked after an approval or
   a CI/PR transition, **Then** it reloads the stored proposal and applies it only if the recorded
   approval hash matches the stored diff and that diff still applies cleanly; otherwise it stays
   blocked and writes nothing.

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
- **Out-of-root path**: a `grounding.*` path uses `..` traversal, a disallowed absolute path, or a
  symlink whose real (resolved) target lies outside the repository root → reject as unsafe with the
  offending value.
- **Missing grounding file**: a referenced document does not exist, is empty (zero bytes), or cannot be
  read → block with the exact path and reason; never substitute a different file.
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
- **FR-003a**: `/drive-speckit` and `/review-roadmap` MUST be plain Claude Code skills, with their rules
  written in the skill text and their state kept in plain files. There MUST NOT be a CLI or compiled
  library behind them, and they are verified by use rather than unit tests.

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
  required. A **cloud-provisioning attempt** is any invocation of cloud provisioning or deployment
  tooling (e.g. `az`, `azd`, `terraform`, `bicep`, `pulumi`) or any creation of a cloud resource by the
  repository's build, test, or CI steps. `restore` MAY reach a public NuGet feed without credentials
  or use a primed cache; `build` and `test` MUST need no network after restore. Where this spec or
  its design docs say "offline", they mean this.
- **FR-008**: Dependency and version pinning MUST be tracked, and license notices MUST be clean and
  present.

**Onboarding & identity assets**

- **FR-009**: The repository MUST include a top-level README covering mission, license, architecture
  overview, build/test instructions, Spec Kit how-to, future plugin/policy scope, contributor
  workflow, and a prominent link to `docs/README.md`. The link target `docs/README.md` MUST exist and
  MUST serve as a navigable index to the grounding documents (at minimum the bootstrap brief,
  development standards, constitution prompt, decision register, and master spec inventory); the root
  README link MUST NOT dangle.
- **FR-010**: The repository MUST include a `CLAUDE.md` covering project purpose, the document
  authority hierarchy, manifest discovery, current-branch and protected-`main` rules, Spec Kit
  integration, review gates, command usage, fast local validation (the exact offline command
  sequence an agent/contributor runs before pushing — e.g. `dotnet build -warnaserror && dotnet
  test`), and privacy/integrity warnings.
- **FR-011**: The repository MUST include an original, self-contained, accessible kebab-inspired
  Iskender SVG logo with `title`/`desc`, not a copy of trademarked or downloaded artwork. The logo
  MUST remain legible on **both light and dark backgrounds** (e.g. via `currentColor`, an explicit
  stroke/contrast treatment, or a declared safe background) so it renders acceptably in common
  light/dark README/host themes.

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
  keys), and MUST fail closed on any unsupported major or malformed/absent value, and on a manifest
  file that is absent or unparseable.
- **FR-014**: The framework MUST detect and reject path traversal, disallowed absolute paths, and
  **symlinks that resolve outside the repository root** (evaluated against the fully resolved,
  symlink-expanded real path), and MUST detect absent, empty (zero-byte), and unreadable target files,
  blocking with the exact offending value and owner instructions.
- **FR-015**: The framework MUST NOT invent a second registry, guess missing paths, or proceed on
  partial/ambiguous manifest data.

**Approved-prompt retrieval & constitution precedence**

- **FR-016**: The framework MUST retrieve a spec's approved `/speckit.specify` prompt **verbatim**
  (byte-for-byte) from the master inventory by keying off the literal `ISK-xxxx` heading and fenced
  prompt label, and MUST NOT synthesize a prompt from a title. "Byte-for-byte" is defined against the
  **bytes of the fenced block body as stored on disk after checkout**; the framework MUST preserve interior content exactly — no trimming, re-wrapping, or
  trailing-newline alteration — excluding only the fence delimiter lines themselves.
- **FR-017**: A missing approved prompt MUST block the `specify` stage with an actionable error; the
  framework MUST NOT generate or substitute a replacement.
- **FR-018**: The framework MUST discover the bootstrap constitution prompt via
  `grounding.constitutionPrompt` for bootstrap only; once `.specify/memory/constitution.md` exists it
  MUST be treated as authoritative, and MUST NOT be overwritten without explicit human approval.

**`drive-speckit` stage engine**

- **FR-019**: `/drive-speckit` MUST, on invocation with no stage argument, discover repository root,
  manifest/schema, inventory/status, active constitution, Spec Kit artifacts, current worktree/branch,
  last checkpoint, and pending approvals; printed stage names are informational only.
- **FR-019a**: This discovery (preflight) MUST run on every invocation. It **passes** only when the
  repository root resolves, the manifest passes validation, the active constitution or bootstrap
  prompt resolves, the inventory is readable, an installed Spec Kit command is identified for each
  canonical stage that uses one, and no live lock is held for the spec. Any failure MUST block with
  the FR-035 contract, and no stage runs. The `bootstrap/detect` stage consumes an invocation only
  when the spec has no state record: on a preflight pass it creates the initial record and stops at
  the stage boundary.
- **FR-020**: The skill MUST select or resume **exactly one** eligible workflow stage per invocation,
  following dependency order and approved status, never speculative current-directory scanning. The
  canonical workflow stages, in dependency order, are `bootstrap/detect` (preflight), `specify`,
  `clarify`, `plan`, `tasks`, `analyze`, `checklist`, `implement`, `converge`, `roadmap-review`, and
  `pr-merge-checkpoint`; the exact underlying Spec Kit commands MUST be discovered from the installed
  toolkit rather than assumed. When more than one spec is simultaneously eligible, the skill MUST
  select the spec deterministically by master-inventory dependency/milestone order (the
  lowest-numbered `ISK-xxxx` whose dependencies are satisfied and whose approved prompt is present),
  never by current-directory scanning. A dependency is satisfied when its inventory entry records a
  real final merge commit. A spec with an incomplete state record is resumed ahead of starting a new
  one.
- **FR-021**: The skill MUST persist durable, versioned, small, machine-readable state with atomic
  writes and locking, recording entered timestamp/runner ID, source prompt/config hashes, artifact
  paths, stage status, last confirmed task, actual Git commit/ref evidence, convergence finding
  ledger, budgets, repair fingerprints, partial-failure diagnostics, and the human approval ledger.
  The state record carries an integer `stateVersion`; a record whose `stateVersion` is **newer** than
  the running engine supports MUST fail closed (block with an owner action) rather than be
  misinterpreted, and the engine MUST read any **older** supported version without data loss.
  `runnerId` MUST have the exact format `host:user:startedAtUtc`, where `startedAtUtc` is when the
  invocation began, so lock ownership is attributable to one invocation.
- **FR-022**: The skill MUST resume re-entrantly from verified artifacts, avoiding repeated operations
  where sufficient evidence already exists, and MUST block (not corrupt) on worktree/spec contention.
  Evidence is **sufficient** only when every path in `artifactPaths` exists, any recorded
  `gitEvidence.commit` exists and is HEAD or an ancestor of it, and `sourceHashes` still match; any
  mismatch MUST block rather than guess. Tasks at or before `lastConfirmedTask`, and tasks marked
  complete in `tasks.md`, MUST NOT be repeated. Lock contention MUST **block-and-exit** with a
  contention error naming the current lock owner and verification steps — it MUST NOT silently wait,
  spin, or steal the lock. The owner MUST refresh the lock timestamp on every state write and before
  any long-running step. A lock MUST be treated as **stale and reclaimable** only when the recorded
  runner is on the same host and the lock timestamp is more than **15 minutes** old; reclaiming a
  stale lock MUST be recorded. A crash **between** an atomic state write and lock release
  MUST leave the committed state intact and the lock reclaimable by the stale-lock rule, never
  requiring the completed work to be redone.
- **FR-023**: In the `implement` stage, the skill MUST work all remaining tasks until completed,
  blocked, or user input is required, and MUST NOT launch `converge` itself.
- **FR-024**: On entering `specify` for a spec, the skill MUST load the approved exact prompt from the
  inventory (via the verbatim-retrieval mechanism of FR-016) and MUST NOT regenerate a missing prompt
  or substitute a template; a missing prompt MUST block per FR-017.
- **FR-024a**: The skill MUST provide a `checklist` stage (positioned after `analyze` and before
  `implement`) that generates requirements-quality checklists via the installed `speckit-checklist`
  command. The agent MUST then perform the review itself against the spec and design artifacts, mark
  each satisfied item complete, and raise only the items that need human input; such items MUST block
  rather than be silently passed. The stage reviews requirements, not implementation (implementation
  is evaluated by `converge`), and MUST NOT modify code.
- **FR-024b**: The terminal `pr-merge-checkpoint` stage MUST run after the spec's converged PR has been
  merged by a human. It MUST verify that CI, the documentation gate, and required reviews passed, then
  record the **actual** merge commit in a final records-only `chore/` PR off `main`, updating the
  spec's state record (`gitEvidence`) and its inventory entry (workflow status and final merge commit).
  That PR MUST contain no code changes. The agent MAY open it and MUST NOT merge or close it.
  If the feature PR merges before these stages finish, the remaining work MUST go through a
  `chore/<NNNN>-post-merge` PR off `main`, and that PR's merge commit is the one recorded and tagged.

**Bounded repair budgets**

- **FR-025**: The framework MUST limit repairs to **3 meaningful failed repairs per problem**, **10
  failed repairs per invocation run**, and **2 consecutive no-progress cycles**; a meaningful repair
  changes a hypothesis or action and records an expected observation and result. "Meaningful" MUST be
  determined mechanically, not by judgment: an attempt counts as meaningful only if its **command (or
  action) differs** from the immediately preceding attempt for the same problem **and** it carries a
  recorded expected-observation; an attempt whose command/action is unchanged (same normalized
  invocation) is a **no-progress** attempt. One **no-progress cycle** = one completed attempt
  classified as no-progress; the consecutive counter increments on each and resets to zero on the next
  meaningful attempt. A "problem" is identified by a stable fingerprint
  (`specId/taskId/stage + normalized error class + dependency context + log hash`) so the same
  recurring failure maps to one problem across attempts and restarts. A **run** starts when a spec's
  record enters a stage and ends when it moves to another stage, so resuming a stage continues the
  same run. Only the end of a run resets the failed-repairs count.
- **FR-026**: Per-problem repair counts and no-progress state MUST persist across invocations and MUST
  NOT be reset by restarting the skill/process; a documented, approval-bounded reset MAY apply only
  when a genuinely new authorized hypothesis or externally changed cause exists. A reset MUST be
  authorized by the **Spec Kit maintainer** (the Principle X review owner) and recorded as a
  `resetJustification` entry (authorizer, timestamp, new hypothesis, externally changed cause);
  no actor and no restart may reset a counter without such a recorded entry.
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
  restarts. Every severity change on an existing finding MUST append to its `severityHistory` (pass,
  from, to, changedBy, reason, timestamp), so changes are verifiable from the ledger alone. A lower
  severity without a recorded reason and the Spec Kit maintainer as `changedBy` MUST be rejected.
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
  requirement-change assessment, rollback option, decision owner). "Diff-only" means each proposed
  change is expressed as an exact unified diff against a named authoritative file; prose-only
  proposals are not acceptable. A single proposal that contains **any** material change MUST be
  classified and gated as **material as a whole** (it blocks pending approval per FR-037); purely
  non-material changes MUST be carried in a **separate** proposal so that approval of routine doc sync
  is never bundled with a material change.
- **FR-037**: Material changes MUST receive explicit human approval before any authoritative file is
  modified; approval MUST record a decision ID, timestamp, approved diff hash, reviewer, and
  rationale. A change is **material** when it alters any of: scope, inclusion/exclusion, new or deleted
  specs, approved prompt intent, security boundaries, milestones (including the MVP cutline), a new
  architectural decision, or the constitution. The following are **not** gated unless they alter a
  material category: edits to the in-progress spec's own `spec.md`/`plan.md` made by its Spec Kit
  stages before its feature PR merges, and updates to an inventory entry's workflow status and final
  merge commit that record real evidence. **Authoritative files** are the governed artifacts whose change requires this gate,
  namely: the active constitution (`.specify/memory/constitution.md`), the master spec inventory
  (`docs/17-master-spec-inventory.md`) including any approved `/speckit.specify` prompt, the
  architecture decision register (`docs/16-architecture-decision-register.md`), the grounding manifest
  (`.iskender/grounding.yaml`), and any committed spec's `spec.md`/`plan.md`. The **approved diff
  hash** MUST be a SHA-256 over the exact approved unified diff (byte-for-byte as presented for
  approval), so an approval provably binds to one and only one diff. The **reviewer/decision owner**
  MUST be the human review owner named for the affected principle in the constitution's "Quality Gates
  & Review Owners" table (or that owner's recorded delegate); when several principles are affected,
  each one's owner MUST approve. Role holders and delegates are recorded in `CONTRIBUTING.md`; every
  role is currently held by the sole maintainer, GitHub `christopherhouse`.
- **FR-038**: On rejection, authoritative files MUST remain unchanged, the decision MUST be recorded,
  and completed-spec history (records, revision IDs, and real merge hashes) MUST be preserved. Only
  **real** Git merge hashes/refs may be recorded; synthesized or placeholder progress MUST NOT be
  recorded on any path. **Routine non-material doc sync** — documentation changes that merely describe
  already-approved, already-implemented behavior and touch no material surface (per the FR-037
  material categories) — does **not** require roadmap approval and **is required** before merge to
  satisfy the documentation gate (FR-043).
- **FR-039**: `/review-roadmap` MUST be resumable across approvals and CI/PR transitions and MUST
  never silently write approval-required changes. Each proposal MUST be persisted, with its exact
  diffs, at `.iskender/state/proposals/<proposalId>.json`, and each approval ledger entry MUST reference
  its `proposalId`. A re-invocation MUST reload pending proposals and MUST apply one only when the
  SHA-256 of its stored diff equals the recorded `approvedDiffHash` and the diff still applies cleanly;
  otherwise it MUST block and write nothing.

**Protected collaboration, CI & documentation gate**

- **FR-040**: All work MUST proceed on `feature/`, `chore/`, or `bugfix/` branches off a protected
  `main`; direct commits/pushes to `main` MUST be prohibited, and PRs MUST require successful CI and
  review.
- **FR-040a**: Work MUST be committed on the current branch as each task is implemented, not batched at
  the end, and pushed after every commit. The PR MUST be opened right after the first push, and its
  notes MUST be kept current and be complete before it merges. Agents MUST do this without being asked,
  and MUST NOT merge or close a PR.
- **FR-041**: The repository MUST provide branch-rule configuration recommendations or IaC-compatible
  documentation for protected `main`, required reviews/checks, CODEOWNERS, PR/issue templates,
  security reporting guidance, and release/versioning conventions. That documentation MUST state that
  GitHub does not count a PR author's own approval, so a required-review rule cannot be met on a PR
  opened under the only approver's account, and report the rule per FR-044 until it can be.
- **FR-041a**: A version tag on `main` MUST trigger a workflow that builds and tests at that version,
  packs the public contract and SDK projects, and publishes a GitHub release with the packages, an SBOM,
  and provenance. The current tag rule is `v0.0.<spec number>-alpha`, applied by `pr-merge-checkpoint`
  to the merge commit of the human-merged PR that FR-024b records. Tags MUST NOT be moved or deleted. Pushing to a
  NuGet feed is out of scope here.
- **FR-042**: A GitHub Actions CI skeleton MUST provide restore, formatting, build, all tests,
  architecture/dependency checks, docs/link/manifest/inventory consistency validation, secret
  scanning, dependency hygiene, and SBOM/provenance hooks, with codeowner paths. Each check MUST be a
  **real, executing workflow step** (not a placeholder comment); specifically the SBOM/provenance hook
  MUST run a step that produces an SBOM artifact and emits a build-provenance attestation (or, where a
  hosted attestation service is unavailable, a documented local equivalent) rather than merely
  reserving a slot.
- **FR-043**: Documentation maintenance MUST be enforced as a mandatory merge gate: a PR that changes
  behavior without the required concurrent documentation updates MUST be blocked. The gate MUST use a
  deterministic path- and label-driven rule: changes touching designated behavior-bearing paths
  (public contracts/SDK surfaces — including `Iskender.Core.Contracts`, `Iskender.Providers.Abstractions`,
  and `Iskender.Policy.Sdk` — host/config, CI workflows and scripts, and the grounding manifest) MUST include
  a matching documentation change in the same PR, unless an explicit skip label (e.g.
  `docs-not-required`) carrying written justification is applied; pure refactor or test-only changes
  outside those paths MUST NOT be blocked. A **matching documentation change** is defined
  deterministically as at least one added/modified file in the PR under `docs/**` **or** a root
  onboarding doc (`README.md`/`CLAUDE.md`); a changed behavior-bearing path with no such file present
  blocks. A deleted doc, or one renamed without content changes, is not a documentation change. The
  master inventory (`docs/17`) is its own documentation: a PR changing only that file MUST NOT be
  blocked, and it MUST NOT count as the documentation change for any other path. The skip label is exactly `docs-not-required`. Its written justification is the text under a
  `## Docs-not-required justification` heading in the PR description, ignoring HTML comments and
  whitespace. The label is honored **only** when that text is non-empty (the gate MUST fail the PR if
  the label is present without justification) and when the label's most recent application, read from
  the PR's label events, was by a CODEOWNER of every affected behavior-bearing path.
- **FR-044**: When repository admin rights/tokens are missing, the framework MUST document the exact
  required actions, owner, and verification commands as pending, MUST NOT falsely report remote rules
  as applied, and MUST NOT perform automatic remote mutations without authorized tool access. Where
  admin rights permit **some** rules but not others, each rule MUST be reported individually as either
  **applied** (with verification evidence) or **pending** (with owner + verification command); a
  partially applied rule set MUST NOT be summarized as fully applied.

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
  modifications (byte-for-byte identical except independently approved integrations), verified by
  `git diff --exit-code <baseline> -- .specify` against the baseline commit recorded at bootstrap, and
  the active constitution resolves correctly.
- **SC-003**: 100% of manifest failure cases (validation rules V1–V7: absent or unparseable manifest,
  unsupported schema, missing required key, invalid `grounding.root`, absent/empty/unreadable target
  file, out-of-root `speckit.*` path, traversal/absolute/symlink escape) are blocked with a message
  naming the offending value and the owner action; zero cases proceed silently.
- **SC-004**: The approved starting prompt for a spec is retrieved byte-for-byte identical to the
  inventory in 100% of valid cases, and 100% of missing-prompt cases block without any synthesized
  prompt.
- **SC-005**: After an interruption at any stage, re-invocation resumes the same stage with **zero**
  repeated completed work and **zero** reset of persisted repair/no-progress counters.
- **SC-006**: The skill stops at the exact budgets — 3 meaningful failed repairs per problem, 10 failed
  repairs per run, and 2 consecutive no-progress cycles — with the counts visible in the state record.
- **SC-007**: Convergence follows its rules across passes: passes 1–3 remediate all
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
- There is a sole implementer working in a single worktree. The per-spec lock guards concurrent
  invocations in that worktree; contention between separate worktrees is out of scope, though
  repository-root resolution still works from a worktree (FR-012).
- This is an approved bootstrap *requirement scope*; unobserved repository state is to be discovered
  during the implementation stage, not assumed here.
- This invocation is SPECIFY ONLY: no implementation, plan, tasks, repository installation, Spec Kit
  changes, or remote-setting changes are performed.
