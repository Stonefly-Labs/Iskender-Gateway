# 14 — ISK-0000 bootstrap brief

**Specification:** **ISK-0000 — Repository Bootstrap & Agentic Development Framework**. **Status:** source brief for `/speckit.specify`; **not executed**. **Authoritative for ISK-0000 scope**; workflow details in [13](13-development-standards-and-agent-workflows.md) and initial prompt in [17](17-master-spec-inventory.md#isk-0000--repository-bootstrap--agentic-development-framework).

## Goal and non-goals

Establish a secure, maintainable .NET 10 Iskender Gateway repository, initial solution/module/test skeleton, reproducible tooling, protected collaboration workflow and durable **Claude Code + existing GitHub Spec Kit** process. Spec 0000 implements tooling and guardrails, **not** gateway inference functionality, policies, database schema, or provider adapters. **Do not reinitialize, reinstall, overwrite or break existing Spec Kit installation.** Inspect its version and supported commands first, preserving `.specify` and existing scripts/templates/memory; integrate with them.

## Work products

### Repository conventions and skeleton

Create `.gitignore` appropriate for .NET/Next.js/IDE/OS/secrets/artifacts, `.gitattributes` for deterministic line endings, `.editorconfig` for C#/Markdown/JSON/YAML formatting; tracked dependency/version pinning and clean license notices. Set up .NET solution, directories and empty compilable projects for host, core contracts, domain/modules, infrastructure, provider abstractions, policy SDK, management and test assemblies **without implementing their business features**. Include predictable namespace conventions and reference-direction tests or CI checks. Add local examples and `dotnet restore/build/test` scripts/documentation. Support a clean clone with no cloud credentials and optional Compose dependency scaffolding; do not provision Azure resources.

### Claude Code integration

Write `CLAUDE.md` with project purpose, document authority hierarchy, manifest discovery, current branch and protected main rules, Spec Kit integration, review gates, command usage, fast local validation and privacy/integrity warnings. Implement bounded specialized agents/skills from [13](13-development-standards-and-agent-workflows.md), including **full `/drive-speckit`** and **Roadmap Steward + `/review-roadmap`**. Tests use stub projects/mock Spec Kit fixtures and scripted failure modes; do not simulate prior implementation or rewrite the actual inventory as completed.

### Manifest/discovery integration

**Preserve and use** `.iskender/grounding.yaml` supplied with this package; don't invent a second registry. Resolve repo root via Git/known markers robustly for nested CWD/worktrees. Parse YAML, validate supported `schemaVersion` and required keys, detect path traversal/disallowed absolute paths and absent files. `grounding.root` relative to repo root; all `grounding.*` document paths relative to that root; `speckit.*` paths relative to repo root. Discover `15-constitution-prompt.md` for bootstrap only; after active `.specify/memory/constitution.md` exists, it is authoritative. Locate current approved inventory entry and preserve exact embedded `/speckit.specify` prompt. Failure must be actionable; **never guess missing paths or invent missing prompts**.

### Workflow state

Implement versioned machine-readable state with atomic writes, lock against two agents driving the same spec, source prompt/config hashes, stage status, last confirmed task, actual Git commit/ref evidence, convergence finding ledger, budgets, repair fingerprints, partial failure diagnostics and human approval ledger. Enforce **one eligible stage per invocation**. In implement, work all remaining tasks until completed or blocked; do not launch converge. Repair budget: 3 meaningful repairs/problem, 10 failed repairs/run, two no-progress cycles, persisted appropriately across calls; classify and report attempts, and never reset just because process restarted. Converge passes 1–3 remediate all findings through separate implement invocation; pass 4+ may accept documented Low-only; Medium+ always block; zero findings succeed; repeated failure escalates. Summary and blocker contract are mandatory [13](13-development-standards-and-agent-workflows.md).

### Roadmap and approval workflow

Steward inspects actual implementation, current contracts, ADRs, active constitution, remaining specs/prompts and dependencies after convergence. Material roadmap/architecture/constitution changes produce **diff-only proposals pending explicit human approval**. Preserve completed spec audit/history, revision IDs and pending approvals; routine approved-doc sync is required before merge. If approval is unavailable, block clearly with affected IDs and exact unblock action; never autoapprove.

### GitHub and repository collaboration

Provide branch rule configuration recommendations or IaC-compatible documentation for protected `main`, required reviews/checks, no direct pushes, CODEOWNERS, PR/issue templates, security reporting guidance and release/versioning conventions. Set branch names `feature/`, `chore/`, `bugfix/`; all work proceeds on branches and merged PRs. Implement GitHub Actions skeleton for build/format/unit/architecture/doc validation, secret scan and dependency hygiene, SBOM/provenance hooks, codeowner paths. If repo admin rights/tokens are missing, do not pretend remote rules were applied: document exact required actions, owner and verification commands in a blocker/action report. No automatic remote mutations without authorized tool access.

### Visual and project onboarding

Write top-level repository README with mission, license, architecture overview, build/test, Spec Kit how-to, plugin/policy future scope, contributor workflow, and prominent link to `docs/README.md`. Produce **original kebab-inspired Iskender SVG logo** suitable for README: safe self-contained vector paths/shapes and accessible `title`/`desc`; not a copy of trademarked artwork or downloaded stock. SVG creation is a code/document asset inside bootstrap, not a requested rendered image in this package.

## Required initial manual sequence (before `drive-speckit` exists)

1. Inspect existing `.specify` and installed slash commands; record version and backup/compatibility observations. **Do not run initialization/install.**
2. Extract this grounding package into **repository root** without overwriting installed Spec Kit files. Confirm `.iskender/grounding.yaml` and all cross references.
3. Run installed `/speckit.constitution` using [15](15-constitution-prompt.md); check active constitution at `.specify/memory/constitution.md`. If it already exists, review it and seek approval before amendment instead of overwriting.
4. Use ISK-0000 starting prompt from [17](17-master-spec-inventory.md#isk-0000--repository-bootstrap--agentic-development-framework) as input to installed `/speckit.specify`, pointing at `@docs/14-spec-0000-bootstrap-brief.md`; **specify documents requirements, it does not implement**.
5. With existing Spec Kit commands, conduct clarifications (if any), plan, tasks, implementation and independent convergence manually for this bootstrap spec. Commit on an off-main branch, run tests/docs gates and merge by reviewed PR.
6. Verify `/drive-speckit` and `review-roadmap` are installed, discover manifest/inventory/constitution and resume valid state. Subsequent specs are driven using `/drive-speckit` and approved prompts.

If user asks coding agent to implement Spec 0000 prematurely during specify, stage discipline still forbids it. Do not reinit Spec Kit to fix command mismatch; diagnose and adapt to installed commands or block for approval.

## Acceptance criteria for ISK-0000

- Clean clone restores/builds/tests without cloud credentials; skeleton compiles without fake provider code.
- Existing Spec Kit files are preserved byte-for-byte except independently approved integrations, and initial active constitution can be resolved correctly.
- Manifest schema/path validation succeeds for supplied file; corrupt schema/absent file/out-of-root path tests fail with exact reason and owner instructions.
- ISK-0000 approved starting prompt is retrieved **verbatim** from the master inventory; missing prompt blocks rather than generating one.
- Skill selects correct eligible stage and persists progress; implement continues multiple tasks; converge is separate; restart resumes without resetting repair counts.
- Synthetic tests verify three-repairs/problem, ten failed repairs/run, two no-progress cycles; converge Low pass 1–3, Low pass 4+, Medium+ reject, zero-findings success.
- Every stage emits required summary; every blocked path names task, cause/evidence, attempted repairs, action/owner and exact resume stage. Unknown root cause includes diagnostics.
- Steward produces evidence/diff and awaits human approval; rejected proposal leaves authoritative files unchanged; completed-spec history survives revision.
- Branch rules/CODEOWNERS/templates/CI are established **where permissions allow**, otherwise accurately flagged as pending with steps/owner.
- README, `CLAUDE.md`, original accessible kebab SVG, docs-link checks and secure repository defaults are present; code/documentation PR gate enforces consistency.

## Status/decision boundary

This is an **approved bootstrap requirement scope** from current conversation, not an assertion about the existing repository or Spec Kit version. Unobserved repository state must be discovered in the implementation stage. No application code, Git settings or Spec Kit operations are performed by the grounding-package generation.
