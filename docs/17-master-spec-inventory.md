# 17 — Master specification inventory

**Authority:** unique implementation spec IDs, scope, inclusions/exclusions, dependency graph, stage status, planning revision, actual merge evidence, release boundaries and the **literal executable starting `/speckit.specify` prompt** for each spec. Every record starts **planned** (not implemented), and its own workflow status and final merge commit lines say where it stands; commit hashes are `null` until actual reviewed merges.

## Reconciliation provenance and boundary

We recovered the earlier **109-spec** proposal only at the level of categories and number ranges: 001–075 MVP; 076–082 conversation intelligence; 083–092 multimodal; 093–099 provider/deployment; 100–109 general API gateway, with MVP shipping after the earlier 075. We **could not recover the verbatim 109 earlier titles or per-entry prompts**. Later explicit decisions added ISK-0000 and required deeper policy/plugin/SDK/CLI/lockfile specs plus mandatory Mode C. Accordingly **this is a new, explicit 125-spec reconciliation**, not a claim that the old numbering or titles were preserved. The approved *feature commitments* are authoritative; this inventory’s technical decomposition and revised **ISK-0086** MVP cutline are the operational starting plan created for this package. If earlier verbatim IDs are located, Roadmap Steward must propose a human-approved mapping rather than silently rewriting this inventory.

**Non-negotiable:** required MVP state modes, providers, policy/catalog/plugin security, distributed governance, identity, observability and operational release gate remain in MVP. A specification is implemented only when its record says so. No fabricated implementation, branch state, remote GitHub settings or hash.

## Inventory fields and executable prompt contract

Every `## ISK-xxxx` record has: title; scope and intent; explicit inclusion/exclusion; exact milestone; prerequisite spec IDs; workflow status; planning revision; **final merge commit (`null` until merged)**; authoritative documents/ADRs; measurable acceptance; and a complete individualized `/speckit.specify` prompt in a fenced text block. Agents **must read and pass through this prompt verbatim** (not synthesize a new one from a title) when entering specify. The prompt references the authoritative documents and constraints. Its first line, `/speckit.specify`, is a literal label that `check-grounding.sh` requires; the installed command is `speckit-specify`, which receives the whole block unchanged. Markdown heading anchors are stable navigation targets, but machine extraction should key off the literal `ISK-xxxx` heading and fenced starting prompt label rather than guessing slugs.

## Milestones and exact ship cutlines

| Milestone | Specification range | Type | Gate |
|---|---|---|---|
| E0 — Agentic foundation | ISK-0000 | MVP prerequisite | Existing Spec Kit preserved; bootstrap workflow validated |
| E1 — Platform/tenancy/security | ISK-0001–ISK-0028 | Internal MVP | Isolation and lifecycle/security tests |
| E2 — Inference adapters/ingress | ISK-0029–ISK-0042 | Internal MVP | Provider conformance matrix |
| E3 — Stateful inference | ISK-0043–ISK-0051 | Internal MVP | Mode A/B/C, continuation, safe stream recovery |
| E4 — Governance, policies, extensibility | ISK-0052–ISK-0079 | Internal MVP | Distributed limits, policy/catalog and verified plugins |
| E5 — Operations/MVP release | ISK-0080–ISK-0086 | Public MVP | **MVP ships after ISK-0086, never before** |
| P1 — Conversation intelligence | ISK-0087–ISK-0095 | Post-MVP | P1 release after ISK-0095 |
| P2 — Multimodal | ISK-0096–ISK-0104 | Post-MVP | P2 release after ISK-0104 |
| P3 — Provider/deployment expansion | ISK-0105–ISK-0112 | Post-MVP | P3 release after ISK-0112 |
| P4 — General HTTP API gateway | ISK-0113–ISK-0124 | Post-MVP | P4 release after ISK-0124 |

**MVP acceptance:** all PR-001–020 in [02](02-product-requirements-and-roadmap.md), tests/benchmarks/security/documentation gates in [12](12-observability-and-nonfunctional-requirements.md), plus verified policy plugin from both read-only mount and startup NuGet on a stock container, first-party catalog tests and two-replica distributed governance, stream-safe durable Mode C, release candidate migration/rollback, accurate provider matrix and protected-branch provenance. Post-MVP milestones may be reprioritized only via approved change history.

## Status, revision, dependencies, and history lifecycle

- **IDs:** four digits and immutable once published; never recycle. Add a new ID at the end (or new separately approved namespace) instead of renumbering committed work. A completed spec cannot be rewritten retroactively; create a follow-on spec and mark any supersession with an audit link.
- **Status machine:** `planned → specifying → clarified → planning → tasking → analyzing → implementing → converging → roadmap-review → pr-ready → merged`; transitions require verified Spec Kit artifact/test/PR evidence. `blocked` is an overlay with reason, owner, precise fix and resume stage, not a fake final status. `canceled`/`superseded` only with human-approved record. `/drive-speckit` owns machine transitions, using the state schema in `specs/0000-repo-bootstrap/contracts/workflow-state.schema.md`.
- **Stage discipline:** one eligible stage per `/drive-speckit` invocation; the implement stage completes all remaining tasks unless blocked; converge remains separate. No autonomous spec advance on missing prompt or external approval.
- **Dependency order:** dependencies must be merged or explicitly permitted by a reviewed integration strategy before implementing dependent spec. Dependency cycles and forward references are invalid. No unsanctioned bypass because a task seems easy.
- **Planning revision:** initially `1.0`; bump on approved material scope/prompt/dependency change and append a review record. Changes that affect downstream work require proposal with diff, affected spec IDs, risk, implementation evidence and user approval.
- **Final merge commit:** initially **`null`**, recorded as the *actual short hash of the merged final implementation* after PR merge, with source PR and full commit hash archived in state/history. No hashes are generated by documentation tooling.
- **Completed specs:** preserve original starting prompt, status transition record and final commit; amendments are append-only notes/new specs with explicit supersession references.
- **Roadmap change history:** append `RC-YYYYMMDD-NNN` entries with proposal hash, evidence, impacted IDs/docs/prompts, decision (approved/rejected/pending), approver, decision date, applied revision/diff hash and merge reference. A pending approval is not executable.
- **Future changes:** additions, cuts, milestone moves, release-boundary changes and future prompt rewrites are **material**. Roadmap Steward can draft an evidence-backed diff; user approves explicitly before authoritative edits. Routine documentation updates reflecting approved implementation still block merges if missing.

## Roadmap change history

| Record | Status | Evidence and effect |
|---|---|---|
| RC-20261105-001 | Baseline generated; not an implementation approval record | Reconciled known 109-spec milestone structure with explicit later ISK-0000, Mode C, policy/plugin SDK scope. Started every spec `planned`, revision `1.0`, final commit `null`. |
| RC-20261105-002 | Approved (DEC-20261105-001) | ISK-0000 prompt and acceptance evidence no longer call for tests or simulations of the workflow skills, which are plain skills verified by use. ISK-0000 moves to planning revision `1.1`. Proposal and diff hash: `.iskender/state/proposals/RC-20261105-002.json`. |
| RC-20261106-004 | Approved (see `.iskender/state/approvals.json`) | ISK-0000 delivered the project skeleton, version pins, analyzer and nullability settings, and dependency tests that ISK-0001 was written to create. ISK-0001 now extends and refines them, and maps the docs/03 modules onto the graph. ISK-0001 moves to planning revision `1.1`. Proposal and diff hash: `.iskender/state/proposals/RC-20261106-004.json`. |
| RC-20261106-005 | Approved (see `.iskender/state/approvals.json`) | ADR-0030 records the pre-MVP alpha prereleases that ISK-0000 built, and states that they are not MVP releases. ISK-0086 takes on the version and tag rule for the public MVP and later releases, and moves to planning revision `1.1`. Proposal and diff hash: `.iskender/state/proposals/RC-20261106-005.json`. |

## Specification records and literal starting prompts

## ISK-0000 — Repository Bootstrap & Agentic Development Framework

- **Scope and intent:** Establish protected repository, .NET/management/test skeleton and reproducible Claude Code plus existing Spec Kit workflow without implementing gateway features.
- **Included:** Existing Spec Kit inspection and preservation; full drive-speckit; Roadmap Steward; manifest discovery; constitution; CI; CODEOWNERS; original kebab SVG.
- **Excluded/non-goals:** No inference handlers; no database domain schema; no Spec Kit reinstallation.
- **Milestone:** E0 — Agentic foundation (MVP prerequisite).
- **Dependencies:** None (bootstrap first).
- **Workflow status:** `merged`.
- **Planning revision:** `1.1`.
- **Final merge commit (short hash):** `ff9dcfd`.
- **Authoritative documents:** [13](13-development-standards-and-agent-workflows.md), [14](14-spec-0000-bootstrap-brief.md), [15](15-constitution-prompt.md), [16](16-architecture-decision-register.md), [17](17-master-spec-inventory.md). **Relevant approved ADRs:** [ADR-0025](16-architecture-decision-register.md#adr-0025), [ADR-0026](16-architecture-decision-register.md#adr-0026), [ADR-0027](16-architecture-decision-register.md#adr-0027), [ADR-0028](16-architecture-decision-register.md#adr-0028).
- **Acceptance evidence:** Clean-clone build and architecture tests pass; the manifest and docs-gate checks reject broken inputs; resumable stages, repair budgets, convergence outcomes and the blocker contract are verified by first real use of the skills.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create **ISK-0000 — Repository Bootstrap & Agentic Development Framework**. Use the complete authoritative bootstrap brief **@docs/14-spec-0000-bootstrap-brief.md**, plus @docs/13-development-standards-and-agent-workflows.md, @docs/15-constitution-prompt.md, @docs/16-architecture-decision-register.md and @docs/17-master-spec-inventory.md. Inspect and **preserve the already-installed Spec Kit installation and commands**; never reinstall or reinitialize Spec Kit. The initial constitution-generation prompt is discovered through `.iskender/grounding.yaml` (grounding.constitutionPrompt); the active constitution, once created, is `.specify/memory/constitution.md`. Scope: repository conventions, compilable .NET solution/module/test skeleton, CI/branch rules, CLAUDE.md and README, original kebab SVG, secure development standards, full resumable `drive-speckit` stage skill (including exact 3/problem, 10/run, 2 no-progress budgets), convergence separation and severity rules, actionable blocker summaries, Roadmap Steward/review-roadmap with human approval, manifest validation and approved-prompt retrieval. Explicitly exclude AI gateway business implementation, provider adapters, policy engine implementation and database domain schema. Define acceptance criteria for missing/corrupt manifest, missing prompt, stage resumption, budget persistence, convergence passes 1–4, priority of active constitution, protected main and documents merge gate. `drive-speckit` and `review-roadmap` are plain Claude Code skills with no CLI or compiled tooling behind them and no unit tests of them; they are verified by use. **This invocation is SPECIFY ONLY:** generate the Spec Kit specification and do not execute any implementation, plan, tasks, repo installation, changes to existing Spec Kit or remote settings. The drive-speckit skill does not exist before ISK-0000 is implemented; manual Spec Kit bootstrap is required.
```

## ISK-0001 — Solution Dependency Boundaries & Build Conventions

- **Scope and intent:** Extend the compilable modular project graph and dependency direction delivered by ISK-0000 across standalone and embeddable components, and map the docs/03 module table onto them.
- **Included:** Mapping of docs/03 modules to approved assemblies or namespaces; refined module assembly references that supersede the ISK-0000 baseline, including the Domain, Management and host edges; additions to the existing central version pins, analyzers and nullability settings; extended dependency architecture tests.
- **Excluded/non-goals:** No provider business functionality; no premature database tables; no recreation of what ISK-0000 delivered.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0000.
- **Workflow status:** `analyzing` (no implementation evidence).
- **Planning revision:** `1.1`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md), [13](13-development-standards-and-agent-workflows.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0002](16-architecture-decision-register.md#adr-0002), [ADR-0004](16-architecture-decision-register.md#adr-0004).
- **Acceptance evidence:** Fresh build passes, every docs/03 module maps to an approved assembly or namespace, and forbidden cross-module references are rejected by architecture tests.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0001 — Solution Dependency Boundaries & Build Conventions** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md @docs/13-development-standards-and-agent-workflows.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Extend the compilable modular project graph and dependency direction delivered by ISK-0000 across standalone and embeddable components, and map the docs/03 module table onto them**. In-scope behaviors and artifacts: **Mapping of docs/03 modules to approved assemblies or namespaces; refined module assembly references that supersede the ISK-0000 baseline, including the Domain, Management and host edges; additions to the existing central version pins, analyzers and nullability settings; extended dependency architecture tests**. Explicitly exclude: **No provider business functionality; no premature database tables; no recreation of what ISK-0000 delivered**. Prerequisites: **ISK-0000**; do not assume unavailable implementation already exists. ISK-0000 already delivered the seven-project skeleton under `src/`, `Directory.Packages.props`, the analyzer and nullability settings in `Directory.Build.props`, and `tests/Iskender.Architecture.Tests`, so start from the repository as it stands and do not recreate them. The rules in `specs/0000-repo-bootstrap/contracts/module-dependency-rules.md` are the baseline; this spec's own contract supersedes them and leaves that completed record unedited. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Fresh build passes, every docs/03 module maps to an approved assembly or namespace, and forbidden cross-module references are rejected by architecture tests**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0002 — Public Contracts & Versioning Discipline

- **Scope and intent:** Design stable common request context, resource identifiers, errors, contracts and versioning policy for transport-neutral gateway modules.
- **Included:** Opaque scoped IDs; error categories; contract version tags; API and SDK compatibility test harness.
- **Excluded/non-goals:** No complete provider mapping or protocol-specific business implementation.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0001.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0002](16-architecture-decision-register.md#adr-0002), [ADR-0004](16-architecture-decision-register.md#adr-0004).
- **Acceptance evidence:** Version contract fixtures and compatibility checks fail on unsupported breaking changes.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0002 — Public Contracts & Versioning Discipline** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Design stable common request context, resource identifiers, errors, contracts and versioning policy for transport-neutral gateway modules**. In-scope behaviors and artifacts: **Opaque scoped IDs; error categories; contract version tags; API and SDK compatibility test harness**. Explicitly exclude: **No complete provider mapping or protocol-specific business implementation**. Prerequisites: **ISK-0001**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Version contract fixtures and compatibility checks fail on unsupported breaking changes**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0003 — Configuration Schema & Validation Foundation

- **Scope and intent:** Provide typed configuration loading, schema validation and environment overlay conventions with immutable version markers.
- **Included:** JSON/YAML input; validation errors; namespaced settings; secret reference placeholders; configuration revisions.
- **Excluded/non-goals:** No policy CEL compiler or dynamic plan activation yet.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0001, ISK-0002.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md), [09](09-policy-language-and-execution.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0002](16-architecture-decision-register.md#adr-0002), [ADR-0004](16-architecture-decision-register.md#adr-0004).
- **Acceptance evidence:** Bad schema and unsafe values produce actionable field errors before runtime.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0003 — Configuration Schema & Validation Foundation** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md @docs/09-policy-language-and-execution.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Provide typed configuration loading, schema validation and environment overlay conventions with immutable version markers**. In-scope behaviors and artifacts: **JSON/YAML input; validation errors; namespaced settings; secret reference placeholders; configuration revisions**. Explicitly exclude: **No policy CEL compiler or dynamic plan activation yet**. Prerequisites: **ISK-0001, ISK-0002**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Bad schema and unsafe values produce actionable field errors before runtime**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0004 — Persistence Abstractions & PostgreSQL Foundation

- **Scope and intent:** Create tenant-aware database connection lifecycle, migration framework and transactional repository contracts.
- **Included:** PostgreSQL migrations; transactional boundaries; health; local Testcontainers; advisory concurrency where appropriate.
- **Excluded/non-goals:** No full conversation schema, budget ledger or identity membership rules.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0001, ISK-0002.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md), [05](05-identity-security-and-privacy.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0002](16-architecture-decision-register.md#adr-0002), [ADR-0004](16-architecture-decision-register.md#adr-0004).
- **Acceptance evidence:** Migration rollback/forward and parallel transaction integration tests pass.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0004 — Persistence Abstractions & PostgreSQL Foundation** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Create tenant-aware database connection lifecycle, migration framework and transactional repository contracts**. In-scope behaviors and artifacts: **PostgreSQL migrations; transactional boundaries; health; local Testcontainers; advisory concurrency where appropriate**. Explicitly exclude: **No full conversation schema, budget ledger or identity membership rules**. Prerequisites: **ISK-0001, ISK-0002**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Migration rollback/forward and parallel transaction integration tests pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0005 — Coordination & Redis Capability Contracts

- **Scope and intent:** Establish optional Redis and coordination ports with declared consistency and multi-replica capability checks.
- **Included:** Atomic operations interface; leases; fencing; cache versioning; outage classification; local substitutes.
- **Excluded/non-goals:** No claim local memory is production-grade distributed enforcement.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0002, ISK-0004.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0002](16-architecture-decision-register.md#adr-0002), [ADR-0004](16-architecture-decision-register.md#adr-0004).
- **Acceptance evidence:** Distributed feature refuses unsupported coordinator and lease fencing tests pass.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0005 — Coordination & Redis Capability Contracts** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Establish optional Redis and coordination ports with declared consistency and multi-replica capability checks**. In-scope behaviors and artifacts: **Atomic operations interface; leases; fencing; cache versioning; outage classification; local substitutes**. Explicitly exclude: **No claim local memory is production-grade distributed enforcement**. Prerequisites: **ISK-0002, ISK-0004**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Distributed feature refuses unsupported coordinator and lease fencing tests pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0006 — Host Composition & DI Registration

- **Scope and intent:** Make domain services composable for host-owned ASP.NET Core service collection and runtime options.
- **Included:** AddIskender registration design; service lifetimes; minimal hosting sample; coexisting middleware.
- **Excluded/non-goals:** No fully working provider ingress yet; no hidden singletons.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0001, ISK-0002, ISK-0003.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0002](16-architecture-decision-register.md#adr-0002), [ADR-0004](16-architecture-decision-register.md#adr-0004).
- **Acceptance evidence:** Embedding host resolves modules without requiring standalone process.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0006 — Host Composition & DI Registration** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Make domain services composable for host-owned ASP.NET Core service collection and runtime options**. In-scope behaviors and artifacts: **AddIskender registration design; service lifetimes; minimal hosting sample; coexisting middleware**. Explicitly exclude: **No fully working provider ingress yet; no hidden singletons**. Prerequisites: **ISK-0001, ISK-0002, ISK-0003**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Embedding host resolves modules without requiring standalone process**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0007 — Container Entry Points & Process Separation

- **Scope and intent:** Create independent control-plane and data-plane entry points, startup validation and graceful host lifecycle.
- **Included:** OCI Dockerfiles; nonroot; health/readiness; configuration roots; split-process strategy.
- **Excluded/non-goals:** No production cloud provisioning or comprehensive admin UI.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0003, ISK-0004, ISK-0005, ISK-0006.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0002](16-architecture-decision-register.md#adr-0002), [ADR-0004](16-architecture-decision-register.md#adr-0004).
- **Acceptance evidence:** Split containers launch in Compose and fail readiness with accurate missing-dependency diagnostics.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0007 — Container Entry Points & Process Separation** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Create independent control-plane and data-plane entry points, startup validation and graceful host lifecycle**. In-scope behaviors and artifacts: **OCI Dockerfiles; nonroot; health/readiness; configuration roots; split-process strategy**. Explicitly exclude: **No production cloud provisioning or comprehensive admin UI**. Prerequisites: **ISK-0003, ISK-0004, ISK-0005, ISK-0006**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Split containers launch in Compose and fail readiness with accurate missing-dependency diagnostics**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0008 — Local Developer Environment & Reproducible Compose

- **Scope and intent:** Provide full opt-in local topology including Postgres/Redis and docs for scripts and tests.
- **Included:** Compose files; seed-free startup; environment safety; clean data reset; smoke scripts.
- **Excluded/non-goals:** No fabricated cloud secrets or production SLA.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0004, ISK-0005, ISK-0007.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [04](04-technology-stack-and-deployment.md), [13](13-development-standards-and-agent-workflows.md). **Relevant approved ADRs:** [ADR-0002](16-architecture-decision-register.md#adr-0002), [ADR-0004](16-architecture-decision-register.md#adr-0004), [ADR-0007](16-architecture-decision-register.md#adr-0007), [ADR-0025](16-architecture-decision-register.md#adr-0025).
- **Acceptance evidence:** Fresh clone starts minimal topology, builds and tests with no external credentials.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0008 — Local Developer Environment & Reproducible Compose** for Iskender Gateway. Use the authoritative project inputs @docs/04-technology-stack-and-deployment.md @docs/13-development-standards-and-agent-workflows.md and the approved ADRs ADR-0002, ADR-0004, ADR-0007, ADR-0025; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Provide full opt-in local topology including Postgres/Redis and docs for scripts and tests**. In-scope behaviors and artifacts: **Compose files; seed-free startup; environment safety; clean data reset; smoke scripts**. Explicitly exclude: **No fabricated cloud secrets or production SLA**. Prerequisites: **ISK-0004, ISK-0005, ISK-0007**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Fresh clone starts minimal topology, builds and tests with no external credentials**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0009 — Module Events & Background Work Coordination

- **Scope and intent:** Create durable internal work handoff for reconciliation/config notifications without cloud bus dependency.
- **Included:** Outbox/lease approach; idempotent jobs; tracing; retry poison handling; shutdown.
- **Excluded/non-goals:** No general event-bus product or external Kafka dependency.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0004, ISK-0005, ISK-0007.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0002](16-architecture-decision-register.md#adr-0002), [ADR-0004](16-architecture-decision-register.md#adr-0004).
- **Acceptance evidence:** Crash after DB commit and duplicate worker delivery do not lose or double-apply work.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0009 — Module Events & Background Work Coordination** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Create durable internal work handoff for reconciliation/config notifications without cloud bus dependency**. In-scope behaviors and artifacts: **Outbox/lease approach; idempotent jobs; tracing; retry poison handling; shutdown**. Explicitly exclude: **No general event-bus product or external Kafka dependency**. Prerequisites: **ISK-0004, ISK-0005, ISK-0007**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Crash after DB commit and duplicate worker delivery do not lose or double-apply work**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0010 — System-Operator Bootstrap & Security Baseline

- **Scope and intent:** Define secure initial operator setup, privilege checks and baseline unauthenticated endpoint restrictions.
- **Included:** First-admin bootstrap; safe recovery path draft; TLS assumptions; health exposure; audit hooks.
- **Excluded/non-goals:** No complete external IdP integration or project-level grants.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0004, ISK-0007.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [05](05-identity-security-and-privacy.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009).
- **Acceptance evidence:** Uninitialized admin routes deny access; bootstrap token cannot be reused.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0010 — System-Operator Bootstrap & Security Baseline** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0003, ADR-0005, ADR-0008, ADR-0009; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Define secure initial operator setup, privilege checks and baseline unauthenticated endpoint restrictions**. In-scope behaviors and artifacts: **First-admin bootstrap; safe recovery path draft; TLS assumptions; health exposure; audit hooks**. Explicitly exclude: **No complete external IdP integration or project-level grants**. Prerequisites: **ISK-0004, ISK-0007**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Uninitialized admin routes deny access; bootstrap token cannot be reused**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0011 — Organization & Project Resource Model

- **Scope and intent:** Implement organization/project ownership and validated lifecycle with opaque IDs and cross-scope reference rules.
- **Included:** Resource roots; ownership indexes; project create/update; scoped naming; lifecycle states.
- **Excluded/non-goals:** No full policy engine or UI.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0004, ISK-0002, ISK-0010.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [05](05-identity-security-and-privacy.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009).
- **Acceptance evidence:** Cross-organization resource injection/query always denied.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0011 — Organization & Project Resource Model** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0003, ADR-0005, ADR-0008, ADR-0009; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement organization/project ownership and validated lifecycle with opaque IDs and cross-scope reference rules**. In-scope behaviors and artifacts: **Resource roots; ownership indexes; project create/update; scoped naming; lifecycle states**. Explicitly exclude: **No full policy engine or UI**. Prerequisites: **ISK-0004, ISK-0002, ISK-0010**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Cross-organization resource injection/query always denied**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0012 — Generic Resource Registry & Scoped Metadata

- **Scope and intent:** Provide typed resource registry for endpoints, deployments and other first-class assets with ownership and version records.
- **Included:** Resource type metadata; optimistic concurrency; soft deletion; owner validation; query pagination.
- **Excluded/non-goals:** No inference model alias behavior yet.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0011, ISK-0003.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [05](05-identity-security-and-privacy.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009).
- **Acceptance evidence:** Unowned resource references fail even when guessed ID exists.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0012 — Generic Resource Registry & Scoped Metadata** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0003, ADR-0005, ADR-0008, ADR-0009; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Provide typed resource registry for endpoints, deployments and other first-class assets with ownership and version records**. In-scope behaviors and artifacts: **Resource type metadata; optimistic concurrency; soft deletion; owner validation; query pagination**. Explicitly exclude: **No inference model alias behavior yet**. Prerequisites: **ISK-0011, ISK-0003**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Unowned resource references fail even when guessed ID exists**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0013 — Control-plane Local ASP.NET Core Identity

- **Scope and intent:** Implement local accounts with secure password credential lifecycle and stable principal mapping.
- **Included:** Hashing; registration/admin invite policy; sessions; recovery; lockouts; audit.
- **Excluded/non-goals:** No embedded OIDC server or guessed enterprise identity settings.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0004, ISK-0010, ISK-0011.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Local login/logout/reset and attack-rate fixtures pass with safe cookies.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0013 — Control-plane Local ASP.NET Core Identity** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement local accounts with secure password credential lifecycle and stable principal mapping**. In-scope behaviors and artifacts: **Hashing; registration/admin invite policy; sessions; recovery; lockouts; audit**. Explicitly exclude: **No embedded OIDC server or guessed enterprise identity settings**. Prerequisites: **ISK-0004, ISK-0010, ISK-0011**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Local login/logout/reset and attack-rate fixtures pass with safe cookies**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0014 — External OIDC Login & Principal Linking

- **Scope and intent:** Allow bring-your-own standards-based OIDC control-plane login and explicit internal principal linking.
- **Included:** PKCE/state/nonce; issuer discovery/key rotation; subject binding; session lifecycle.
- **Excluded/non-goals:** No homegrown authorization server or email-only auto-linking.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0013, ISK-0011.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Multiple issuers cannot impersonate one another; rotation and invalid-token tests pass.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0014 — External OIDC Login & Principal Linking** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Allow bring-your-own standards-based OIDC control-plane login and explicit internal principal linking**. In-scope behaviors and artifacts: **PKCE/state/nonce; issuer discovery/key rotation; subject binding; session lifecycle**. Explicitly exclude: **No homegrown authorization server or email-only auto-linking**. Prerequisites: **ISK-0013, ISK-0011**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Multiple issuers cannot impersonate one another; rotation and invalid-token tests pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0015 — Scoped RBAC Permissions & Role Grants

- **Scope and intent:** Define and enforce organization/project resource actions with stable permission checks.
- **Included:** Role matrix; principal membership; grant/revoke; global admin segregation; authorization middleware.
- **Excluded/non-goals:** No optional policy engine requirement for core isolation.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0011, ISK-0012, ISK-0013.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [05](05-identity-security-and-privacy.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009).
- **Acceptance evidence:** Every management action has allow/deny matrix and cross-scope negative tests.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0015 — Scoped RBAC Permissions & Role Grants** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0003, ADR-0005, ADR-0008, ADR-0009; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Define and enforce organization/project resource actions with stable permission checks**. In-scope behaviors and artifacts: **Role matrix; principal membership; grant/revoke; global admin segregation; authorization middleware**. Explicitly exclude: **No optional policy engine requirement for core isolation**. Prerequisites: **ISK-0011, ISK-0012, ISK-0013**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Every management action has allow/deny matrix and cross-scope negative tests**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0016 — Data-plane API Key Storage & Issuance

- **Scope and intent:** Implement project-scoped high-entropy ingress credentials with one-time display and hashed verification.
- **Included:** Issue/list metadata; expiry; scoped aliases; hashed storage; audit.
- **Excluded/non-goals:** No OAuth bearer support in same spec; never store plaintext.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0011, ISK-0015, ISK-0004.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Persisted DB exfiltration does not expose usable raw keys; expiry denies use.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0016 — Data-plane API Key Storage & Issuance** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement project-scoped high-entropy ingress credentials with one-time display and hashed verification**. In-scope behaviors and artifacts: **Issue/list metadata; expiry; scoped aliases; hashed storage; audit**. Explicitly exclude: **No OAuth bearer support in same spec; never store plaintext**. Prerequisites: **ISK-0011, ISK-0015, ISK-0004**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Persisted DB exfiltration does not expose usable raw keys; expiry denies use**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0017 — API Key Rotation, Revocation & Disable Switches

- **Scope and intent:** Implement separate issuance and acceptance toggles plus safe key rotation/revocation propagation.
- **Included:** Independent issue/validate settings; overlap windows; cache invalidation; audit.
- **Excluded/non-goals:** No forced mass revoke unless explicitly requested.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0016, ISK-0005.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Issued keys denied when acceptance disabled while issuance-only disable preserves existing acceptance.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0017 — API Key Rotation, Revocation & Disable Switches** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement separate issuance and acceptance toggles plus safe key rotation/revocation propagation**. In-scope behaviors and artifacts: **Independent issue/validate settings; overlap windows; cache invalidation; audit**. Explicitly exclude: **No forced mass revoke unless explicitly requested**. Prerequisites: **ISK-0016, ISK-0005**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Issued keys denied when acceptance disabled while issuance-only disable preserves existing acceptance**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0018 — Data-plane JWT Bearer Validator

- **Scope and intent:** Verify configured OAuth/JWT bearer with issuer/audience/alg/clock and identity-to-project mapping.
- **Included:** Multiple issuer profiles; JWKS rotation; scopes; claims; principal binding.
- **Excluded/non-goals:** No token introspection or exchange in MVP.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0011, ISK-0015, ISK-0003.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** None/forged/wrong issuer/wrong project JWTs always denied.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0018 — Data-plane JWT Bearer Validator** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Verify configured OAuth/JWT bearer with issuer/audience/alg/clock and identity-to-project mapping**. In-scope behaviors and artifacts: **Multiple issuer profiles; JWKS rotation; scopes; claims; principal binding**. Explicitly exclude: **No token introspection or exchange in MVP**. Prerequisites: **ISK-0011, ISK-0015, ISK-0003**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **None/forged/wrong issuer/wrong project JWTs always denied**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0019 — Authentication Selection & Per-scope Guardrails

- **Scope and intent:** Integrate key and JWT ingress rules with organization/project/resource ownership and safe effective configuration.
- **Included:** Bearer-only mode; API key disable; inheritance; endpoint auth requirements; optional anonymous policy prohibitions.
- **Excluded/non-goals:** No optional policy bypass of platform auth and owner rules.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0017, ISK-0018, ISK-0012.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [05](05-identity-security-and-privacy.md), [09](09-policy-language-and-execution.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009).
- **Acceptance evidence:** Cross-tenant requests fail under every configured authentication mode.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0019 — Authentication Selection & Per-scope Guardrails** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/05-identity-security-and-privacy.md @docs/09-policy-language-and-execution.md and the approved ADRs ADR-0003, ADR-0005, ADR-0008, ADR-0009; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Integrate key and JWT ingress rules with organization/project/resource ownership and safe effective configuration**. In-scope behaviors and artifacts: **Bearer-only mode; API key disable; inheritance; endpoint auth requirements; optional anonymous policy prohibitions**. Explicitly exclude: **No optional policy bypass of platform auth and owner rules**. Prerequisites: **ISK-0017, ISK-0018, ISK-0012**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Cross-tenant requests fail under every configured authentication mode**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0020 — Management API Authentication & Session Safety

- **Scope and intent:** Consolidate browser cookie and service automation auth controls for management endpoints.
- **Included:** CSRF/session invalidation; authorization integration; safe errors; audit.
- **Excluded/non-goals:** No complete management resource API or UI workflows.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0013, ISK-0014, ISK-0015.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** CSRF and stale-session attempts fail; API and UI enforce same permissions.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0020 — Management API Authentication & Session Safety** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Consolidate browser cookie and service automation auth controls for management endpoints**. In-scope behaviors and artifacts: **CSRF/session invalidation; authorization integration; safe errors; audit**. Explicitly exclude: **No complete management resource API or UI workflows**. Prerequisites: **ISK-0013, ISK-0014, ISK-0015**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **CSRF and stale-session attempts fail; API and UI enforce same permissions**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0021 — Secrets Provider Abstraction & Encryption

- **Scope and intent:** Build safe secret references, key custody boundaries, rotation contract and encrypted local fallback.
- **Included:** Envelope encryption ports; TLS; redact-on-serialize; audit secret reads; backup recovery semantics.
- **Excluded/non-goals:** No Azure-only Key Vault mandate or secrets in config JSON.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0004, ISK-0003, ISK-0010.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [04](04-technology-stack-and-deployment.md), [05](05-identity-security-and-privacy.md). **Relevant approved ADRs:** [ADR-0002](16-architecture-decision-register.md#adr-0002), [ADR-0004](16-architecture-decision-register.md#adr-0004), [ADR-0007](16-architecture-decision-register.md#adr-0007), [ADR-0005](16-architecture-decision-register.md#adr-0005).
- **Acceptance evidence:** Secret never appears in trace/log/API responses; rotate and restart cases pass.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0021 — Secrets Provider Abstraction & Encryption** for Iskender Gateway. Use the authoritative project inputs @docs/04-technology-stack-and-deployment.md @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0002, ADR-0004, ADR-0007, ADR-0005; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Build safe secret references, key custody boundaries, rotation contract and encrypted local fallback**. In-scope behaviors and artifacts: **Envelope encryption ports; TLS; redact-on-serialize; audit secret reads; backup recovery semantics**. Explicitly exclude: **No Azure-only Key Vault mandate or secrets in config JSON**. Prerequisites: **ISK-0004, ISK-0003, ISK-0010**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Secret never appears in trace/log/API responses; rotate and restart cases pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0022 — Provider Credential Profiles & Rotation

- **Scope and intent:** Bind deployment-independent credential profile definitions and scoped secret access.
- **Included:** API key profiles; bearer/workload federation metadata; credential separation by account.
- **Excluded/non-goals:** No provider-specific live token client implementations yet.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0012, ISK-0015, ISK-0021.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Wrong scope cannot bind credential; rotation preserves audit and enforces access.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0022 — Provider Credential Profiles & Rotation** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Bind deployment-independent credential profile definitions and scoped secret access**. In-scope behaviors and artifacts: **API key profiles; bearer/workload federation metadata; credential separation by account**. Explicitly exclude: **No provider-specific live token client implementations yet**. Prerequisites: **ISK-0012, ISK-0015, ISK-0021**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Wrong scope cannot bind credential; rotation preserves audit and enforces access**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0023 — Azure Managed Identity Provider Auth

- **Scope and intent:** Implement Azure upstream credential adapter for Azure inference where supported with token audience and renewal.
- **Included:** Workload identity; token acquisition/caching; specific Azure audience/tenant checks.
- **Excluded/non-goals:** No presumption all Azure Foundry deployments support same auth.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0022, ISK-0006.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Wrong audience/expired identity fails securely; refresh uses no plaintext tracing.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0023 — Azure Managed Identity Provider Auth** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement Azure upstream credential adapter for Azure inference where supported with token audience and renewal**. In-scope behaviors and artifacts: **Workload identity; token acquisition/caching; specific Azure audience/tenant checks**. Explicitly exclude: **No presumption all Azure Foundry deployments support same auth**. Prerequisites: **ISK-0022, ISK-0006**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Wrong audience/expired identity fails securely; refresh uses no plaintext tracing**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0024 — Google Service Account & Workload Identity Auth

- **Scope and intent:** Implement Google ADC/service account/federation adapter for supported Gemini endpoints.
- **Included:** Credential type resolution; scoped token audience; keyless option; expiry/refresh.
- **Excluded/non-goals:** No universal promise of Google service account for every endpoint.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0022, ISK-0006.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Mock federation/token renewal works and invalid scopes are rejected.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0024 — Google Service Account & Workload Identity Auth** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement Google ADC/service account/federation adapter for supported Gemini endpoints**. In-scope behaviors and artifacts: **Credential type resolution; scoped token audience; keyless option; expiry/refresh**. Explicitly exclude: **No universal promise of Google service account for every endpoint**. Prerequisites: **ISK-0022, ISK-0006**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Mock federation/token renewal works and invalid scopes are rejected**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0025 — OpenAI Workload Federation Capability

- **Scope and intent:** Support documented OpenAI workload identity federation only where actual upstream APIs permit it.
- **Included:** Capability gating; issuer/audience config; fallback static key profile; diagnostics.
- **Excluded/non-goals:** Do not misrepresent unsupported provider model/account combinations.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0022, ISK-0006.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Unsupported endpoint fails explicitly and supported federation fixtures pass.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0025 — OpenAI Workload Federation Capability** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Support documented OpenAI workload identity federation only where actual upstream APIs permit it**. In-scope behaviors and artifacts: **Capability gating; issuer/audience config; fallback static key profile; diagnostics**. Explicitly exclude: **Do not misrepresent unsupported provider model/account combinations**. Prerequisites: **ISK-0022, ISK-0006**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Unsupported endpoint fails explicitly and supported federation fixtures pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0026 — Privacy Defaults, Safe Audit & Data Retention

- **Scope and intent:** Introduce redaction/privacy controls, append-style audit semantics and tenant-scoped retention framework.
- **Included:** Payload logging off; audit correlation; deletion hooks; operator retention settings.
- **Excluded/non-goals:** No blanket provider-side erasure promise or advanced content classification.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0011, ISK-0015, ISK-0021, ISK-0009.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Prompt/token secret fixture absent in default logs, data deletion scoped, audit events reconciled.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0026 — Privacy Defaults, Safe Audit & Data Retention** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Introduce redaction/privacy controls, append-style audit semantics and tenant-scoped retention framework**. In-scope behaviors and artifacts: **Payload logging off; audit correlation; deletion hooks; operator retention settings**. Explicitly exclude: **No blanket provider-side erasure promise or advanced content classification**. Prerequisites: **ISK-0011, ISK-0015, ISK-0021, ISK-0009**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Prompt/token secret fixture absent in default logs, data deletion scoped, audit events reconciled**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0027 — Outbound Network Safety & Provider Egress Rules

- **Scope and intent:** Protect runtime from untrusted URLs, metadata endpoint SSRF and unsafe provider redirects.
- **Included:** Endpoint validation; TLS policy; credential destination restrictions; egress diagnostics.
- **Excluded/non-goals:** No full WAF product or all-purpose network security service.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0021, ISK-0022, ISK-0007.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Redirect to private/metadata IP blocked and credentials not forwarded.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0027 — Outbound Network Safety & Provider Egress Rules** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Protect runtime from untrusted URLs, metadata endpoint SSRF and unsafe provider redirects**. In-scope behaviors and artifacts: **Endpoint validation; TLS policy; credential destination restrictions; egress diagnostics**. Explicitly exclude: **No full WAF product or all-purpose network security service**. Prerequisites: **ISK-0021, ISK-0022, ISK-0007**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Redirect to private/metadata IP blocked and credentials not forwarded**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0028 — Tenant Security & RBAC Adversarial Test Suite

- **Scope and intent:** Consolidate negative security coverage across control/data/store/worker/resource APIs.
- **Included:** Cross-tenant test grid; privilege removal; direct object reference; static credential attacks.
- **Excluded/non-goals:** No weakening controls to make tests green.
- **Milestone:** E1 — Platform, tenancy, identity and security (MVP).
- **Dependencies:** ISK-0011, ISK-0015, ISK-0019, ISK-0026, ISK-0027.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [05](05-identity-security-and-privacy.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009).
- **Acceptance evidence:** Automated cross-tenant tests block build on any owner scope leak.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0028 — Tenant Security & RBAC Adversarial Test Suite** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/05-identity-security-and-privacy.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0003, ADR-0005, ADR-0008, ADR-0009; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Consolidate negative security coverage across control/data/store/worker/resource APIs**. In-scope behaviors and artifacts: **Cross-tenant test grid; privilege removal; direct object reference; static credential attacks**. Explicitly exclude: **No weakening controls to make tests green**. Prerequisites: **ISK-0011, ISK-0015, ISK-0019, ISK-0026, ISK-0027**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Automated cross-tenant tests block build on any owner scope leak**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0029 — Inference Ingress HTTP & Protocol Validation

- **Scope and intent:** Create logical inference API dispatch, content validation, correlation and safe request error envelope.
- **Included:** Endpoint registry ingress; size limits; structured 4xx/5xx; cancellation.
- **Excluded/non-goals:** No full provider translation or arbitrary HTTP gateway routes.
- **Milestone:** E2 — Inference adapters and ingress (MVP).
- **Dependencies:** ISK-0006, ISK-0007, ISK-0002, ISK-0019.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Invalid payload rejected before upstream and error IDs remain stable.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0029 — Inference Ingress HTTP & Protocol Validation** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0003, ADR-0005, ADR-0013, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Create logical inference API dispatch, content validation, correlation and safe request error envelope**. In-scope behaviors and artifacts: **Endpoint registry ingress; size limits; structured 4xx/5xx; cancellation**. Explicitly exclude: **No full provider translation or arbitrary HTTP gateway routes**. Prerequisites: **ISK-0006, ISK-0007, ISK-0002, ISK-0019**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Invalid payload rejected before upstream and error IDs remain stable**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0030 — Provider Adapter SDK & Capability Registry

- **Scope and intent:** Define provider-neutral adapter contract, capability declarations and typed streaming events.
- **Included:** Capability enums+limits; errors; mock transport; deployment registration.
- **Excluded/non-goals:** No provider-specific SDK assumptions in core contracts.
- **Milestone:** E2 — Inference adapters and ingress (MVP).
- **Dependencies:** ISK-0029, ISK-0022, ISK-0002.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Mock adapters pass contract and forbidden dependency tests.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0030 — Provider Adapter SDK & Capability Registry** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0003, ADR-0005, ADR-0013, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Define provider-neutral adapter contract, capability declarations and typed streaming events**. In-scope behaviors and artifacts: **Capability enums+limits; errors; mock transport; deployment registration**. Explicitly exclude: **No provider-specific SDK assumptions in core contracts**. Prerequisites: **ISK-0029, ISK-0022, ISK-0002**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Mock adapters pass contract and forbidden dependency tests**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0031 — Logical Model Aliases & Deployment Registry

- **Scope and intent:** Implement alias-to-pool bindings and independently configured concrete deployment metadata.
- **Included:** Model alias lifecycle; deployment identity/region/credential refs; capability filters.
- **Excluded/non-goals:** No routing algorithms or health engine in this spec.
- **Milestone:** E2 — Inference adapters and ingress (MVP).
- **Dependencies:** ISK-0012, ISK-0022, ISK-0030.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [07](07-provider-adapters-and-api-compatibility.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Two distinct deployments behind one alias preserve separate identities.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0031 — Logical Model Aliases & Deployment Registry** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/07-provider-adapters-and-api-compatibility.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0003, ADR-0005, ADR-0013, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement alias-to-pool bindings and independently configured concrete deployment metadata**. In-scope behaviors and artifacts: **Model alias lifecycle; deployment identity/region/credential refs; capability filters**. Explicitly exclude: **No routing algorithms or health engine in this spec**. Prerequisites: **ISK-0012, ISK-0022, ISK-0030**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Two distinct deployments behind one alias preserve separate identities**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0032 — OpenAI Chat Completions Adapter

- **Scope and intent:** Map OpenAI Chat Completions requests/responses, tools and streaming into canonical adapter events.
- **Included:** Messages; tool calls; JSON schema capability; SSE; errors and usage.
- **Excluded/non-goals:** No pretense all OpenAI models support identical features.
- **Milestone:** E2 — Inference adapters and ingress (MVP).
- **Dependencies:** ISK-0030, ISK-0031.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Golden fixtures for sync, streaming, tool errors and unsupported options.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0032 — OpenAI Chat Completions Adapter** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0013, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Map OpenAI Chat Completions requests/responses, tools and streaming into canonical adapter events**. In-scope behaviors and artifacts: **Messages; tool calls; JSON schema capability; SSE; errors and usage**. Explicitly exclude: **No pretense all OpenAI models support identical features**. Prerequisites: **ISK-0030, ISK-0031**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Golden fixtures for sync, streaming, tool errors and unsupported options**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0033 — Azure OpenAI & Foundry Adapter

- **Scope and intent:** Support Azure OpenAI and eligible Azure AI Foundry inference with endpoint/API version/deployment auth distinctions.
- **Included:** Version handling; deployment names; identity profiles; streaming; errors.
- **Excluded/non-goals:** No assumption Azure Foundry is one universal wire protocol.
- **Milestone:** E2 — Inference adapters and ingress (MVP).
- **Dependencies:** ISK-0030, ISK-0031, ISK-0023.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Integration matrix covers at least supported Azure flavors and rejects mismatched versions.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0033 — Azure OpenAI & Foundry Adapter** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Support Azure OpenAI and eligible Azure AI Foundry inference with endpoint/API version/deployment auth distinctions**. In-scope behaviors and artifacts: **Version handling; deployment names; identity profiles; streaming; errors**. Explicitly exclude: **No assumption Azure Foundry is one universal wire protocol**. Prerequisites: **ISK-0030, ISK-0031, ISK-0023**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Integration matrix covers at least supported Azure flavors and rejects mismatched versions**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0034 — Anthropic Messages Adapter

- **Scope and intent:** Translate Chat Completions/Responses portable features to Anthropic Messages with explicit gaps.
- **Included:** Role mapping; tool calls/results; stream deltas; token usage; safety parameters.
- **Excluded/non-goals:** No universal mapping of provider-private thinking state.
- **Milestone:** E2 — Inference adapters and ingress (MVP).
- **Dependencies:** ISK-0030, ISK-0031.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Fixtures test Anthropic text/tool/stream and fail unsupported strict features.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0034 — Anthropic Messages Adapter** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0013, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Translate Chat Completions/Responses portable features to Anthropic Messages with explicit gaps**. In-scope behaviors and artifacts: **Role mapping; tool calls/results; stream deltas; token usage; safety parameters**. Explicitly exclude: **No universal mapping of provider-private thinking state**. Prerequisites: **ISK-0030, ISK-0031**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Fixtures test Anthropic text/tool/stream and fail unsupported strict features**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0035 — Google Gemini Adapter

- **Scope and intent:** Translate Gemini APIs for supported text/embeddings/tools and streaming capabilities.
- **Included:** Content parts; tools; safety; JSON output where supported; usage.
- **Excluded/non-goals:** No unproven parity with proprietary tool or schema behavior.
- **Milestone:** E2 — Inference adapters and ingress (MVP).
- **Dependencies:** ISK-0030, ISK-0031, ISK-0024.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Provider-specific tests validate streamed tool args, schema limitations and errors.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0035 — Google Gemini Adapter** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0013, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Translate Gemini APIs for supported text/embeddings/tools and streaming capabilities**. In-scope behaviors and artifacts: **Content parts; tools; safety; JSON output where supported; usage**. Explicitly exclude: **No unproven parity with proprietary tool or schema behavior**. Prerequisites: **ISK-0030, ISK-0031, ISK-0024**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Provider-specific tests validate streamed tool args, schema limitations and errors**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0036 — Generic OpenAI-compatible Adapter

- **Scope and intent:** Support configurable vendor/local compatible backends with declared capability profiles.
- **Included:** Custom base URL, auth headers, operation support, model aliases, feature probes.
- **Excluded/non-goals:** No blind assumption Responses/Conversations or tool support on all compatible hosts.
- **Milestone:** E2 — Inference adapters and ingress (MVP).
- **Dependencies:** ISK-0030, ISK-0031, ISK-0027.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Unknown unsupported operation fails clearly; custom endpoint fixture passes.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0036 — Generic OpenAI-compatible Adapter** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0013, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Support configurable vendor/local compatible backends with declared capability profiles**. In-scope behaviors and artifacts: **Custom base URL, auth headers, operation support, model aliases, feature probes**. Explicitly exclude: **No blind assumption Responses/Conversations or tool support on all compatible hosts**. Prerequisites: **ISK-0030, ISK-0031, ISK-0027**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Unknown unsupported operation fails clearly; custom endpoint fixture passes**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0037 — OpenAI-compatible Chat Completions Frontend

- **Scope and intent:** Finalize public chat completion semantics and translation to adapter contracts.
- **Included:** Request validation; streaming SSE; choices; tool args; usage/error mapping.
- **Excluded/non-goals:** No hidden agent orchestration or model-hosting inference.
- **Milestone:** E2 — Inference adapters and ingress (MVP).
- **Dependencies:** ISK-0029, ISK-0032, ISK-0033, ISK-0034, ISK-0035, ISK-0036.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Cross-provider tests prove consistent ingress and explicit capability rejection.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0037 — OpenAI-compatible Chat Completions Frontend** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0013, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Finalize public chat completion semantics and translation to adapter contracts**. In-scope behaviors and artifacts: **Request validation; streaming SSE; choices; tool args; usage/error mapping**. Explicitly exclude: **No hidden agent orchestration or model-hosting inference**. Prerequisites: **ISK-0029, ISK-0032, ISK-0033, ISK-0034, ISK-0035, ISK-0036**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Cross-provider tests prove consistent ingress and explicit capability rejection**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0038 — OpenAI-compatible Embeddings Frontend

- **Scope and intent:** Implement embeddings ingress, batching and declared output format to capable adapters.
- **Included:** Input types; dimensions gating; usage; error mapping; rate boundaries.
- **Excluded/non-goals:** No vector database, RAG orchestration or semantic cache.
- **Milestone:** E2 — Inference adapters and ingress (MVP).
- **Dependencies:** ISK-0029, ISK-0030, ISK-0032, ISK-0033, ISK-0035, ISK-0036.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Embedding dimensions and provider limitations tested with golden fixtures.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0038 — OpenAI-compatible Embeddings Frontend** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0013, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement embeddings ingress, batching and declared output format to capable adapters**. In-scope behaviors and artifacts: **Input types; dimensions gating; usage; error mapping; rate boundaries**. Explicitly exclude: **No vector database, RAG orchestration or semantic cache**. Prerequisites: **ISK-0029, ISK-0030, ISK-0032, ISK-0033, ISK-0035, ISK-0036**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Embedding dimensions and provider limitations tested with golden fixtures**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0039 — Structured Output & JSON Schema Compatibility

- **Scope and intent:** Normalize structured-output requests and validate provider-declared strictness.
- **Included:** Schema subset mapping; validation errors; adapter capability flags; no silent prompt-only fallback.
- **Excluded/non-goals:** No guarantee universal strict JSON schema enforcement.
- **Milestone:** E2 — Inference adapters and ingress (MVP).
- **Dependencies:** ISK-0037, ISK-0030.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Strictness claims consistent with conformance matrix and unsupported schema rejects.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0039 — Structured Output & JSON Schema Compatibility** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0013, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Normalize structured-output requests and validate provider-declared strictness**. In-scope behaviors and artifacts: **Schema subset mapping; validation errors; adapter capability flags; no silent prompt-only fallback**. Explicitly exclude: **No guarantee universal strict JSON schema enforcement**. Prerequisites: **ISK-0037, ISK-0030**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Strictness claims consistent with conformance matrix and unsupported schema rejects**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0040 — Tool Call/Result Normalization

- **Scope and intent:** Create provider-neutral typed tool call IDs, serialized arguments and result correlation semantics.
- **Included:** Parallel tool capability; streaming args; stable IDs; ordering and errors.
- **Excluded/non-goals:** No autonomous tool execution framework or arbitrary code runners.
- **Milestone:** E2 — Inference adapters and ingress (MVP).
- **Dependencies:** ISK-0037, ISK-0030.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0013](16-architecture-decision-register.md#adr-0013).
- **Acceptance evidence:** Tool lifecycle fixtures roundtrip OpenAI/Anthropic/Gemini-compatible cases.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0040 — Tool Call/Result Normalization** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0011, ADR-0012, ADR-0013; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Create provider-neutral typed tool call IDs, serialized arguments and result correlation semantics**. In-scope behaviors and artifacts: **Parallel tool capability; streaming args; stable IDs; ordering and errors**. Explicitly exclude: **No autonomous tool execution framework or arbitrary code runners**. Prerequisites: **ISK-0037, ISK-0030**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Tool lifecycle fixtures roundtrip OpenAI/Anthropic/Gemini-compatible cases**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0041 — Streaming Transport & Backpressure Semantics

- **Scope and intent:** Implement safe SSE event conversion, bounded buffering, cancellation and terminal errors.
- **Included:** UTF8 fragmentation; slow clients; deadlines; first-visible-byte tracking.
- **Excluded/non-goals:** No retry/failover after emitting visible output.
- **Milestone:** E2 — Inference adapters and ingress (MVP).
- **Dependencies:** ISK-0037, ISK-0030, ISK-0040.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [07](07-provider-adapters-and-api-compatibility.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Kill/slow-client tests show bounded memory and no duplicate emitted chunks.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0041 — Streaming Transport & Backpressure Semantics** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/07-provider-adapters-and-api-compatibility.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0003, ADR-0005, ADR-0013, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement safe SSE event conversion, bounded buffering, cancellation and terminal errors**. In-scope behaviors and artifacts: **UTF8 fragmentation; slow clients; deadlines; first-visible-byte tracking**. Explicitly exclude: **No retry/failover after emitting visible output**. Prerequisites: **ISK-0037, ISK-0030, ISK-0040**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Kill/slow-client tests show bounded memory and no duplicate emitted chunks**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0042 — Provider Conformance Harness & Compatibility Matrix

- **Scope and intent:** Generate evidence-backed versioned provider/feature table and deterministic golden test runners.
- **Included:** Error/stream/tool/schema/embeddings cases; live smoke toggle; unknown states.
- **Excluded/non-goals:** No unsupported feature labeled supported; no real secrets in CI.
- **Milestone:** E2 — Inference adapters and ingress (MVP).
- **Dependencies:** ISK-0032, ISK-0033, ISK-0034, ISK-0035, ISK-0036, ISK-0037, ISK-0038, ISK-0039, ISK-0041.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0023](16-architecture-decision-register.md#adr-0023).
- **Acceptance evidence:** Every advertised capability links to passing test ID and Untested stays explicit.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0042 — Provider Conformance Harness & Compatibility Matrix** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0013, ADR-0012, ADR-0023; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Generate evidence-backed versioned provider/feature table and deterministic golden test runners**. In-scope behaviors and artifacts: **Error/stream/tool/schema/embeddings cases; live smoke toggle; unknown states**. Explicitly exclude: **No unsupported feature labeled supported; no real secrets in CI**. Prerequisites: **ISK-0032, ISK-0033, ISK-0034, ISK-0035, ISK-0036, ISK-0037, ISK-0038, ISK-0039, ISK-0041**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Every advertised capability links to passing test ID and Untested stays explicit**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0043 — Conversation Mode Selection & Identifier Contracts

- **Scope and intent:** Implement explicit mode A/B/C choice and opaque scoped identifiers for responses, conversation metadata and native mappings.
- **Included:** Mode configuration; ID namespaces; reference validation; ownership enforcement.
- **Excluded/non-goals:** No durable content persistence before canonical schema.
- **Milestone:** E3 — Stateful inference (MVP).
- **Dependencies:** ISK-0011, ISK-0019, ISK-0029, ISK-0030.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [06](06-conversation-state-architecture.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Invalid cross-mode and cross-project IDs reject without information leakage.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0043 — Conversation Mode Selection & Identifier Contracts** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/06-conversation-state-architecture.md and the approved ADRs ADR-0003, ADR-0005, ADR-0011, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement explicit mode A/B/C choice and opaque scoped identifiers for responses, conversation metadata and native mappings**. In-scope behaviors and artifacts: **Mode configuration; ID namespaces; reference validation; ownership enforcement**. Explicitly exclude: **No durable content persistence before canonical schema**. Prerequisites: **ISK-0011, ISK-0019, ISK-0029, ISK-0030**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Invalid cross-mode and cross-project IDs reject without information leakage**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0044 — Canonical Typed Conversation Item Schema

- **Scope and intent:** Model conversation, turn, response, message content parts and tool events with extensible typed versioned representation.
- **Included:** Typed roles; tool IDs; provenance; timestamps; extension fields; schema migrations.
- **Excluded/non-goals:** No semantic summarization or global chat analytics.
- **Milestone:** E3 — Stateful inference (MVP).
- **Dependencies:** ISK-0043, ISK-0040, ISK-0004.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Roundtrip fixtures preserve ordering/tool links and reject invalid type transitions.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0044 — Canonical Typed Conversation Item Schema** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md and the approved ADRs ADR-0011, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Model conversation, turn, response, message content parts and tool events with extensible typed versioned representation**. In-scope behaviors and artifacts: **Typed roles; tool IDs; provenance; timestamps; extension fields; schema migrations**. Explicitly exclude: **No semantic summarization or global chat analytics**. Prerequisites: **ISK-0043, ISK-0040, ISK-0004**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Roundtrip fixtures preserve ordering/tool links and reject invalid type transitions**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0045 — Durable Conversation PostgreSQL Store

- **Scope and intent:** Persist gateway-owned conversations/items with optimistic revisions and tenant-scoped transactions.
- **Included:** Append/read/query; revision CAS; indexes; test migration; deletion hooks.
- **Excluded/non-goals:** No reliance on Redis as sole history source.
- **Milestone:** E3 — Stateful inference (MVP).
- **Dependencies:** ISK-0044, ISK-0011, ISK-0026.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Restart/replica CAS and cross-tenant retrieval tests pass.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0045 — Durable Conversation PostgreSQL Store** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md and the approved ADRs ADR-0011, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Persist gateway-owned conversations/items with optimistic revisions and tenant-scoped transactions**. In-scope behaviors and artifacts: **Append/read/query; revision CAS; indexes; test migration; deletion hooks**. Explicitly exclude: **No reliance on Redis as sole history source**. Prerequisites: **ISK-0044, ISK-0011, ISK-0026**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Restart/replica CAS and cross-tenant retrieval tests pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0046 — Stateless & Provider-managed State Modes

- **Scope and intent:** Implement A passthrough and B native-provider ID lifecycle with declared provider capability.
- **Included:** Native ID mapping; request flags; retention caveats; safe metadata mapping.
- **Excluded/non-goals:** No false cross-provider portability promise in B.
- **Milestone:** E3 — Stateful inference (MVP).
- **Dependencies:** ISK-0043, ISK-0030, ISK-0032, ISK-0033.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0013](16-architecture-decision-register.md#adr-0013).
- **Acceptance evidence:** Mode A leaves no content history; B provider ID is bound to scope.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0046 — Stateless & Provider-managed State Modes** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0011, ADR-0012, ADR-0013; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement A passthrough and B native-provider ID lifecycle with declared provider capability**. In-scope behaviors and artifacts: **Native ID mapping; request flags; retention caveats; safe metadata mapping**. Explicitly exclude: **No false cross-provider portability promise in B**. Prerequisites: **ISK-0043, ISK-0030, ISK-0032, ISK-0033**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Mode A leaves no content history; B provider ID is bound to scope**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0047 — Gateway-owned Responses API Surface

- **Scope and intent:** Implement OpenAI-compatible Responses create/retrieve/continuation on canonical conversation history.
- **Included:** previous_response_id; response lifecycle; normalized typed tool events; errors.
- **Excluded/non-goals:** No unsupported hidden reasoning emulation or compaction.
- **Milestone:** E3 — Stateful inference (MVP).
- **Dependencies:** ISK-0045, ISK-0046, ISK-0041, ISK-0040.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0013](16-architecture-decision-register.md#adr-0013).
- **Acceptance evidence:** Response chain resumes across process restarts without duplicate turns.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0047 — Gateway-owned Responses API Surface** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0011, ADR-0012, ADR-0013; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement OpenAI-compatible Responses create/retrieve/continuation on canonical conversation history**. In-scope behaviors and artifacts: **previous_response_id; response lifecycle; normalized typed tool events; errors**. Explicitly exclude: **No unsupported hidden reasoning emulation or compaction**. Prerequisites: **ISK-0045, ISK-0046, ISK-0041, ISK-0040**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Response chain resumes across process restarts without duplicate turns**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0048 — Gateway-owned Conversations API Surface

- **Scope and intent:** Implement OpenAI-compatible Conversations lifecycle, items and scoped retrieval/deletion.
- **Included:** Create/list/retrieve/update item subset; ownership; conflict handling; retention.
- **Excluded/non-goals:** No provider-side deletion guarantees or branching tree.
- **Milestone:** E3 — Stateful inference (MVP).
- **Dependencies:** ISK-0045, ISK-0047, ISK-0026.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0013](16-architecture-decision-register.md#adr-0013).
- **Acceptance evidence:** Cross-project IDs denied; item-order/revision tests pass.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0048 — Gateway-owned Conversations API Surface** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0011, ADR-0012, ADR-0013; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement OpenAI-compatible Conversations lifecycle, items and scoped retrieval/deletion**. In-scope behaviors and artifacts: **Create/list/retrieve/update item subset; ownership; conflict handling; retention**. Explicitly exclude: **No provider-side deletion guarantees or branching tree**. Prerequisites: **ISK-0045, ISK-0047, ISK-0026**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Cross-project IDs denied; item-order/revision tests pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0049 — Stream-safe Conversation Commit & Recovery

- **Scope and intent:** Guarantee success/failed/partial state transitions, orphan recovery and usage event idempotence.
- **Included:** In-progress turns; terminal commit; cancellation; lease recovery; duplicate submit detection.
- **Excluded/non-goals:** No exactly-once upstream provider claim.
- **Milestone:** E3 — Stateful inference (MVP).
- **Dependencies:** ISK-0047, ISK-0048, ISK-0041, ISK-0009.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015).
- **Acceptance evidence:** Terminated replica and duplicate SSE fixtures never mark partial response completed.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0049 — Stream-safe Conversation Commit & Recovery** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0011, ADR-0012, ADR-0014, ADR-0015; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Guarantee success/failed/partial state transitions, orphan recovery and usage event idempotence**. In-scope behaviors and artifacts: **In-progress turns; terminal commit; cancellation; lease recovery; duplicate submit detection**. Explicitly exclude: **No exactly-once upstream provider claim**. Prerequisites: **ISK-0047, ISK-0048, ISK-0041, ISK-0009**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Terminated replica and duplicate SSE fixtures never mark partial response completed**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0050 — Cross-provider Context Reconstruction

- **Scope and intent:** Translate portable canonical history to different supported provider requests with explicit fidelity limits.
- **Included:** Role/tool mapping; context-size admission; unsupported-feature rejection; token estimation.
- **Excluded/non-goals:** No hidden reasoning preservation, smart summarization or compaction.
- **Milestone:** E3 — Stateful inference (MVP).
- **Dependencies:** ISK-0044, ISK-0047, ISK-0034, ISK-0035, ISK-0036, ISK-0040.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0013](16-architecture-decision-register.md#adr-0013).
- **Acceptance evidence:** Persist with A provider then continue with B; test correct tool linkage and explicit loss reasons.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0050 — Cross-provider Context Reconstruction** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0011, ADR-0012, ADR-0013; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Translate portable canonical history to different supported provider requests with explicit fidelity limits**. In-scope behaviors and artifacts: **Role/tool mapping; context-size admission; unsupported-feature rejection; token estimation**. Explicitly exclude: **No hidden reasoning preservation, smart summarization or compaction**. Prerequisites: **ISK-0044, ISK-0047, ISK-0034, ISK-0035, ISK-0036, ISK-0040**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Persist with A provider then continue with B; test correct tool linkage and explicit loss reasons**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0051 — Conversation Retention & Isolation Hardening

- **Scope and intent:** Complete lifecycle retention/erase, audit and concurrent appends under multiple replicas.
- **Included:** Tenant scoped retention jobs; in-progress deletion; revision conflict; recovery probes.
- **Excluded/non-goals:** No semantic conversation search, branches or AI compaction.
- **Milestone:** E3 — Stateful inference (MVP).
- **Dependencies:** ISK-0045, ISK-0048, ISK-0049, ISK-0050, ISK-0026.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md), [06](06-conversation-state-architecture.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Retention deletion removes read/continue, pending jobs cannot resurrect data.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0051 — Conversation Retention & Isolation Hardening** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md @docs/06-conversation-state-architecture.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Complete lifecycle retention/erase, audit and concurrent appends under multiple replicas**. In-scope behaviors and artifacts: **Tenant scoped retention jobs; in-progress deletion; revision conflict; recovery probes**. Explicitly exclude: **No semantic conversation search, branches or AI compaction**. Prerequisites: **ISK-0045, ISK-0048, ISK-0049, ISK-0050, ISK-0026**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Retention deletion removes read/continue, pending jobs cannot resurrect data**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0052 — Policy SDK Typed Descriptors & Runtime Contracts

- **Scope and intent:** Build public versioned typed policy descriptors, handler/effects contract and legal stages/scopes.
- **Included:** IPolicyHandler-style SDK; typed settings; DI; streaming capabilities; safe context.
- **Excluded/non-goals:** No direct loading of arbitrary plugins or CEL effect language.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0002, ISK-0006, ISK-0019.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [09](09-policy-language-and-execution.md), [10](10-first-party-policy-catalog.md), [11](11-plugin-extensibility-and-sdk.md). **Relevant approved ADRs:** [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017), [ADR-0018](16-architecture-decision-register.md#adr-0018), [ADR-0019](16-architecture-decision-register.md#adr-0019).
- **Acceptance evidence:** Mock built-in/external handlers run through same validation interface.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0052 — Policy SDK Typed Descriptors & Runtime Contracts** for Iskender Gateway. Use the authoritative project inputs @docs/09-policy-language-and-execution.md @docs/10-first-party-policy-catalog.md @docs/11-plugin-extensibility-and-sdk.md and the approved ADRs ADR-0016, ADR-0017, ADR-0018, ADR-0019; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Build public versioned typed policy descriptors, handler/effects contract and legal stages/scopes**. In-scope behaviors and artifacts: **IPolicyHandler-style SDK; typed settings; DI; streaming capabilities; safe context**. Explicitly exclude: **No direct loading of arbitrary plugins or CEL effect language**. Prerequisites: **ISK-0002, ISK-0006, ISK-0019**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Mock built-in/external handlers run through same validation interface**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0053 — CEL Applicability Compiler & Sandbox Limits

- **Scope and intent:** Implement typed Common Expression Language conditions with resource-limited pure evaluation.
- **Included:** Typed input schema; compile errors; restricted functions; AST cache; cost budgets.
- **Excluded/non-goals:** No Rego, arbitrary C# expression execution or procedural DSL.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0052, ISK-0003.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [09](09-policy-language-and-execution.md). **Relevant approved ADRs:** [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017).
- **Acceptance evidence:** Invalid field, excessive cost and effectful operation all rejected before runtime.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0053 — CEL Applicability Compiler & Sandbox Limits** for Iskender Gateway. Use the authoritative project inputs @docs/09-policy-language-and-execution.md and the approved ADRs ADR-0016, ADR-0017; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement typed Common Expression Language conditions with resource-limited pure evaluation**. In-scope behaviors and artifacts: **Typed input schema; compile errors; restricted functions; AST cache; cost budgets**. Explicitly exclude: **No Rego, arbitrary C# expression execution or procedural DSL**. Prerequisites: **ISK-0052, ISK-0003**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Invalid field, excessive cost and effectful operation all rejected before runtime**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0054 — Policy Configuration JSON/YAML & Fluent Builders

- **Scope and intent:** Implement equivalent declarative and fluent authoring into one canonical typed policy configuration.
- **Included:** Schemas; versioned kinds; scope metadata; roundtrip serialization; diagnostics.
- **Excluded/non-goals:** No independent policy execution semantics by authoring format.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0052, ISK-0053, ISK-0003.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [09](09-policy-language-and-execution.md). **Relevant approved ADRs:** [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017).
- **Acceptance evidence:** JSON/YAML/fluent programs yield same compiled plan hash.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0054 — Policy Configuration JSON/YAML & Fluent Builders** for Iskender Gateway. Use the authoritative project inputs @docs/09-policy-language-and-execution.md and the approved ADRs ADR-0016, ADR-0017; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement equivalent declarative and fluent authoring into one canonical typed policy configuration**. In-scope behaviors and artifacts: **Schemas; versioned kinds; scope metadata; roundtrip serialization; diagnostics**. Explicitly exclude: **No independent policy execution semantics by authoring format**. Prerequisites: **ISK-0052, ISK-0053, ISK-0003**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **JSON/YAML/fluent programs yield same compiled plan hash**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0055 — Policy Lifecycle Stages & Deterministic Ordering

- **Scope and intent:** Define pipeline stage schedule, tie-break precedence and legal effect boundaries, including streams.
- **Included:** Ingress/admission/route/request/response/completion; ordered identifiers; error semantics.
- **Excluded/non-goals:** No reordering baseline platform authentication with plugin handlers.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0052, ISK-0053, ISK-0037, ISK-0041.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [09](09-policy-language-and-execution.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017).
- **Acceptance evidence:** Shuffle input declarations and get identical deterministic effects.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0055 — Policy Lifecycle Stages & Deterministic Ordering** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/09-policy-language-and-execution.md and the approved ADRs ADR-0003, ADR-0005, ADR-0016, ADR-0017; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Define pipeline stage schedule, tie-break precedence and legal effect boundaries, including streams**. In-scope behaviors and artifacts: **Ingress/admission/route/request/response/completion; ordered identifiers; error semantics**. Explicitly exclude: **No reordering baseline platform authentication with plugin handlers**. Prerequisites: **ISK-0052, ISK-0053, ISK-0037, ISK-0041**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Shuffle input declarations and get identical deterministic effects**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0056 — Hierarchical Policy Assignments & Guardrails

- **Scope and intent:** Compile effective org/project/resource policies, restrictive parent minima and bounded overrides.
- **Included:** Inheritance categories; conflict diagnostics; provenance; versioned assignments.
- **Excluded/non-goals:** No descendant override of mandatory parent security minima.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0054, ISK-0055, ISK-0012, ISK-0015.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [05](05-identity-security-and-privacy.md), [09](09-policy-language-and-execution.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009).
- **Acceptance evidence:** Parent hard deny persists across all project/resource overrides.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0056 — Hierarchical Policy Assignments & Guardrails** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/05-identity-security-and-privacy.md @docs/09-policy-language-and-execution.md and the approved ADRs ADR-0003, ADR-0005, ADR-0008, ADR-0009; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Compile effective org/project/resource policies, restrictive parent minima and bounded overrides**. In-scope behaviors and artifacts: **Inheritance categories; conflict diagnostics; provenance; versioned assignments**. Explicitly exclude: **No descendant override of mandatory parent security minima**. Prerequisites: **ISK-0054, ISK-0055, ISK-0012, ISK-0015**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Parent hard deny persists across all project/resource overrides**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0057 — Immutable Plan Compilation & Atomic Activation

- **Scope and intent:** Publish validated signed/hashed config revisions and swap immutable plans per request without partial rollout.
- **Included:** Plan versioning; activation rollback; request pin; distributed notification; diagnostics.
- **Excluded/non-goals:** No binary plugin hot reload and no mutating in-flight plans.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0055, ISK-0056, ISK-0009.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [09](09-policy-language-and-execution.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017).
- **Acceptance evidence:** High-concurrency streams pin exactly one plan; invalid activation preserves valid state safely.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0057 — Immutable Plan Compilation & Atomic Activation** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/09-policy-language-and-execution.md and the approved ADRs ADR-0003, ADR-0005, ADR-0016, ADR-0017; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Publish validated signed/hashed config revisions and swap immutable plans per request without partial rollout**. In-scope behaviors and artifacts: **Plan versioning; activation rollback; request pin; distributed notification; diagnostics**. Explicitly exclude: **No binary plugin hot reload and no mutating in-flight plans**. Prerequisites: **ISK-0055, ISK-0056, ISK-0009**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **High-concurrency streams pin exactly one plan; invalid activation preserves valid state safely**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0058 — Effective Policy Inspection & Troubleshooting

- **Scope and intent:** Expose sanitized plan provenance, CEL why-match and activation failures to authorized operators.
- **Included:** Plan diff; evaluation trace sampling; redaction; diagnostic error codes.
- **Excluded/non-goals:** No private key/prompt exposure or config-generated code execution.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0057, ISK-0020.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [09](09-policy-language-and-execution.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017), [ADR-0023](16-architecture-decision-register.md#adr-0023).
- **Acceptance evidence:** Operator can identify rejected rule and unblock steps without secrets.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0058 — Effective Policy Inspection & Troubleshooting** for Iskender Gateway. Use the authoritative project inputs @docs/09-policy-language-and-execution.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0016, ADR-0017, ADR-0023; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Expose sanitized plan provenance, CEL why-match and activation failures to authorized operators**. In-scope behaviors and artifacts: **Plan diff; evaluation trace sampling; redaction; diagnostic error codes**. Explicitly exclude: **No private key/prompt exposure or config-generated code execution**. Prerequisites: **ISK-0057, ISK-0020**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Operator can identify rejected rule and unblock steps without secrets**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0059 — First-party Access & Identity Policies

- **Scope and intent:** Implement typed JWT requirement/claims, credential-agnostic authorization, identity and network constraints.
- **Included:** Catalog identifiers/access.*; mandatory auth distinction; config schemas; inheritance.
- **Excluded/non-goals:** No duplicate separate JWT claim-only handler or baseline auth bypass.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0052, ISK-0056, ISK-0019, ISK-0027.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md), [09](09-policy-language-and-execution.md), [10](10-first-party-policy-catalog.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** JWT/claim and general authorization checks work for key/bearer and tighten on child scopes.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0059 — First-party Access & Identity Policies** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md @docs/09-policy-language-and-execution.md @docs/10-first-party-policy-catalog.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement typed JWT requirement/claims, credential-agnostic authorization, identity and network constraints**. In-scope behaviors and artifacts: **Catalog identifiers/access.*; mandatory auth distinction; config schemas; inheritance**. Explicitly exclude: **No duplicate separate JWT claim-only handler or baseline auth bypass**. Prerequisites: **ISK-0052, ISK-0056, ISK-0019, ISK-0027**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **JWT/claim and general authorization checks work for key/bearer and tighten on child scopes**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0060 — First-party Traffic Rate & Token Policies

- **Scope and intent:** Implement request and token throttling with policy contracts backed by capable distributed coordinator.
- **Included:** Rate algorithms; token pre-reservation; reconciliation; CEL conditions; policy diagnostics.
- **Excluded/non-goals:** No local unlimited fallback on Redis outage.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0052, ISK-0057, ISK-0005, ISK-0037.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [08](08-routing-resilience-and-governance.md), [09](09-policy-language-and-execution.md), [10](10-first-party-policy-catalog.md). **Relevant approved ADRs:** [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015), [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017).
- **Acceptance evidence:** Two replicas obey shared rate/token limits under stress and recovery.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0060 — First-party Traffic Rate & Token Policies** for Iskender Gateway. Use the authoritative project inputs @docs/08-routing-resilience-and-governance.md @docs/09-policy-language-and-execution.md @docs/10-first-party-policy-catalog.md and the approved ADRs ADR-0014, ADR-0015, ADR-0016, ADR-0017; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement request and token throttling with policy contracts backed by capable distributed coordinator**. In-scope behaviors and artifacts: **Rate algorithms; token pre-reservation; reconciliation; CEL conditions; policy diagnostics**. Explicitly exclude: **No local unlimited fallback on Redis outage**. Prerequisites: **ISK-0052, ISK-0057, ISK-0005, ISK-0037**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Two replicas obey shared rate/token limits under stress and recovery**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0061 — First-party Quota & Concurrency Policies

- **Scope and intent:** Implement period quotas and leased concurrent stream caps with typed policy options.
- **Included:** Quota windows; periodic reset; concurrency lease; cancellation/fencing.
- **Excluded/non-goals:** No unchecked phantom leases or unbounded active streams.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0060, ISK-0005, ISK-0041.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [08](08-routing-resilience-and-governance.md), [09](09-policy-language-and-execution.md), [10](10-first-party-policy-catalog.md). **Relevant approved ADRs:** [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015), [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017).
- **Acceptance evidence:** Overlapping requests cannot exceed hard bound after coordinator race.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0061 — First-party Quota & Concurrency Policies** for Iskender Gateway. Use the authoritative project inputs @docs/08-routing-resilience-and-governance.md @docs/09-policy-language-and-execution.md @docs/10-first-party-policy-catalog.md and the approved ADRs ADR-0014, ADR-0015, ADR-0016, ADR-0017; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement period quotas and leased concurrent stream caps with typed policy options**. In-scope behaviors and artifacts: **Quota windows; periodic reset; concurrency lease; cancellation/fencing**. Explicitly exclude: **No unchecked phantom leases or unbounded active streams**. Prerequisites: **ISK-0060, ISK-0005, ISK-0041**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Overlapping requests cannot exceed hard bound after coordinator race**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0062 — Usage Ledger & Token/Cost Estimation

- **Scope and intent:** Create auditable usage ingestion and price profile/version estimation independent of provider billing claims.
- **Included:** Reported vs estimated usage; attempts; costs; ledger IDs; project aggregation.
- **Excluded/non-goals:** No financial charging/payments platform or claim invoice accuracy.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0042, ISK-0049, ISK-0004.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [08](08-routing-resilience-and-governance.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015), [ADR-0023](16-architecture-decision-register.md#adr-0023).
- **Acceptance evidence:** Usage ledger provenance and correction entries sum reproducibly.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0062 — Usage Ledger & Token/Cost Estimation** for Iskender Gateway. Use the authoritative project inputs @docs/08-routing-resilience-and-governance.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0014, ADR-0015, ADR-0023; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Create auditable usage ingestion and price profile/version estimation independent of provider billing claims**. In-scope behaviors and artifacts: **Reported vs estimated usage; attempts; costs; ledger IDs; project aggregation**. Explicitly exclude: **No financial charging/payments platform or claim invoice accuracy**. Prerequisites: **ISK-0042, ISK-0049, ISK-0004**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Usage ledger provenance and correction entries sum reproducibly**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0063 — Financial Budget Reservation & Reconciliation

- **Scope and intent:** Reserve budgets before provider invocation, settle after streams and reclaim orphan holds.
- **Included:** Currency; hard/soft policies; pending reservations; retry accounting; reconciliation.
- **Excluded/non-goals:** No fail-open hard budget or double-count retry charges.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0062, ISK-0060, ISK-0009, ISK-0049.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [08](08-routing-resilience-and-governance.md), [10](10-first-party-policy-catalog.md). **Relevant approved ADRs:** [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015), [ADR-0018](16-architecture-decision-register.md#adr-0018), [ADR-0019](16-architecture-decision-register.md#adr-0019).
- **Acceptance evidence:** Concurrent calls cannot oversubscribe approved hard budget without diagnosed bounded uncertainty.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0063 — Financial Budget Reservation & Reconciliation** for Iskender Gateway. Use the authoritative project inputs @docs/08-routing-resilience-and-governance.md @docs/10-first-party-policy-catalog.md and the approved ADRs ADR-0014, ADR-0015, ADR-0018, ADR-0019; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Reserve budgets before provider invocation, settle after streams and reclaim orphan holds**. In-scope behaviors and artifacts: **Currency; hard/soft policies; pending reservations; retry accounting; reconciliation**. Explicitly exclude: **No fail-open hard budget or double-count retry charges**. Prerequisites: **ISK-0062, ISK-0060, ISK-0009, ISK-0049**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Concurrent calls cannot oversubscribe approved hard budget without diagnosed bounded uncertainty**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0064 — First-party Spend & Governance Policies

- **Scope and intent:** Implement spend, usage attribution, model/provider allowlists as typed scoped handlers.
- **Included:** Spend caps; cost center; model/deployment constraints; parent guardrails.
- **Excluded/non-goals:** No direct modification of immutable ledger by plugin handler.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0063, ISK-0056, ISK-0031.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [08](08-routing-resilience-and-governance.md), [09](09-policy-language-and-execution.md), [10](10-first-party-policy-catalog.md). **Relevant approved ADRs:** [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015), [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017).
- **Acceptance evidence:** Hard budget and disallowed provider remain denied under child override.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0064 — First-party Spend & Governance Policies** for Iskender Gateway. Use the authoritative project inputs @docs/08-routing-resilience-and-governance.md @docs/09-policy-language-and-execution.md @docs/10-first-party-policy-catalog.md and the approved ADRs ADR-0014, ADR-0015, ADR-0016, ADR-0017; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement spend, usage attribution, model/provider allowlists as typed scoped handlers**. In-scope behaviors and artifacts: **Spend caps; cost center; model/deployment constraints; parent guardrails**. Explicitly exclude: **No direct modification of immutable ledger by plugin handler**. Prerequisites: **ISK-0063, ISK-0056, ISK-0031**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Hard budget and disallowed provider remain denied under child override**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0065 — Routing Algorithms & Pool Eligibility

- **Scope and intent:** Build round-robin, weighted, priority and latency-aware selection with capability/residency and budget filters.
- **Included:** Eligible backend set; selection policies; weight/priority; latency estimator.
- **Excluded/non-goals:** No arbitrary request selection of unowned deployment.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0031, ISK-0042, ISK-0064.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015).
- **Acceptance evidence:** Statistical routing and priority failover tests respect eligibility and health.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0065 — Routing Algorithms & Pool Eligibility** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0003, ADR-0005, ADR-0014, ADR-0015; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Build round-robin, weighted, priority and latency-aware selection with capability/residency and budget filters**. In-scope behaviors and artifacts: **Eligible backend set; selection policies; weight/priority; latency estimator**. Explicitly exclude: **No arbitrary request selection of unowned deployment**. Prerequisites: **ISK-0031, ISK-0042, ISK-0064**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Statistical routing and priority failover tests respect eligibility and health**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0066 — Backend Health & Distributed Circuit Breakers

- **Scope and intent:** Implement per-deployment passive health state, optional active probes and distributed breaker transitions.
- **Included:** Independent health; half-open probes; hysteresis; coordinator failure.
- **Excluded/non-goals:** No globally shared breaker across unrelated provider accounts.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0065, ISK-0005.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015).
- **Acceptance evidence:** A failing backend is isolated; other pool candidate remains available.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0066 — Backend Health & Distributed Circuit Breakers** for Iskender Gateway. Use the authoritative project inputs @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0014, ADR-0015; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement per-deployment passive health state, optional active probes and distributed breaker transitions**. In-scope behaviors and artifacts: **Independent health; half-open probes; hysteresis; coordinator failure**. Explicitly exclude: **No globally shared breaker across unrelated provider accounts**. Prerequisites: **ISK-0065, ISK-0005**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **A failing backend is isolated; other pool candidate remains available**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0067 — Retry/Timeout/Failover Engine

- **Scope and intent:** Enforce bounded end-to-end/attempt deadlines and safe pre-visible failover.
- **Included:** Idempotence checks; retry budget; error class; stream first-byte rule.
- **Excluded/non-goals:** No replay of streamed output and no unlimited retry loops.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0066, ISK-0041, ISK-0049.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015).
- **Acceptance evidence:** Fault injection verifies bounded retries and no post-output failover.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0067 — Retry/Timeout/Failover Engine** for Iskender Gateway. Use the authoritative project inputs @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0014, ADR-0015; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Enforce bounded end-to-end/attempt deadlines and safe pre-visible failover**. In-scope behaviors and artifacts: **Idempotence checks; retry budget; error class; stream first-byte rule**. Explicitly exclude: **No replay of streamed output and no unlimited retry loops**. Prerequisites: **ISK-0066, ISK-0041, ISK-0049**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Fault injection verifies bounded retries and no post-output failover**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0068 — First-party Routing & Resilience Policies

- **Scope and intent:** Implement routing pool/strategy/pinning and retry/deadline/failover as typed handlers.
- **Included:** Catalog routing.* and resilience.*; capability tests; parent bounds.
- **Excluded/non-goals:** No handler override of eligibility or mandatory scope denial.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0065, ISK-0067, ISK-0057.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [08](08-routing-resilience-and-governance.md), [09](09-policy-language-and-execution.md), [10](10-first-party-policy-catalog.md). **Relevant approved ADRs:** [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015), [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017).
- **Acceptance evidence:** Configuration changes alter safe selected target without breaking health/circuit rules.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0068 — First-party Routing & Resilience Policies** for Iskender Gateway. Use the authoritative project inputs @docs/08-routing-resilience-and-governance.md @docs/09-policy-language-and-execution.md @docs/10-first-party-policy-catalog.md and the approved ADRs ADR-0014, ADR-0015, ADR-0016, ADR-0017; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement routing pool/strategy/pinning and retry/deadline/failover as typed handlers**. In-scope behaviors and artifacts: **Catalog routing.* and resilience.*; capability tests; parent bounds**. Explicitly exclude: **No handler override of eligibility or mandatory scope denial**. Prerequisites: **ISK-0065, ISK-0067, ISK-0057**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Configuration changes alter safe selected target without breaking health/circuit rules**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0069 — First-party Request/Response Transforms

- **Scope and intent:** Implement allowlisted header and inference request/response shaping policies with stream-aware safeguards.
- **Included:** Catalog transform.*; protected headers; schema/legal stage constraints.
- **Excluded/non-goals:** No generic arbitrary XML/API gateway transform engine in MVP.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0055, ISK-0057, ISK-0041.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [09](09-policy-language-and-execution.md), [10](10-first-party-policy-catalog.md). **Relevant approved ADRs:** [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017), [ADR-0018](16-architecture-decision-register.md#adr-0018), [ADR-0019](16-architecture-decision-register.md#adr-0019).
- **Acceptance evidence:** Protected auth headers cannot be changed; SSE order stays valid.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0069 — First-party Request/Response Transforms** for Iskender Gateway. Use the authoritative project inputs @docs/09-policy-language-and-execution.md @docs/10-first-party-policy-catalog.md and the approved ADRs ADR-0016, ADR-0017, ADR-0018, ADR-0019; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement allowlisted header and inference request/response shaping policies with stream-aware safeguards**. In-scope behaviors and artifacts: **Catalog transform.*; protected headers; schema/legal stage constraints**. Explicitly exclude: **No generic arbitrary XML/API gateway transform engine in MVP**. Prerequisites: **ISK-0055, ISK-0057, ISK-0041**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Protected auth headers cannot be changed; SSE order stays valid**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0070 — First-party Context Enrichment & Requirements

- **Scope and intent:** Implement typed safe policy context attachment and required metadata guards.
- **Included:** Catalog context.*; namespace collision detection; CEL visibility; redaction.
- **Excluded/non-goals:** No trust promotion of user-controlled fields to auth evidence.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0053, ISK-0057, ISK-0059.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [09](09-policy-language-and-execution.md), [10](10-first-party-policy-catalog.md). **Relevant approved ADRs:** [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017), [ADR-0018](16-architecture-decision-register.md#adr-0018), [ADR-0019](16-architecture-decision-register.md#adr-0019).
- **Acceptance evidence:** CEL sees typed safe metadata while secrets and spoofable trust are blocked.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0070 — First-party Context Enrichment & Requirements** for Iskender Gateway. Use the authoritative project inputs @docs/09-policy-language-and-execution.md @docs/10-first-party-policy-catalog.md and the approved ADRs ADR-0016, ADR-0017, ADR-0018, ADR-0019; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement typed safe policy context attachment and required metadata guards**. In-scope behaviors and artifacts: **Catalog context.*; namespace collision detection; CEL visibility; redaction**. Explicitly exclude: **No trust promotion of user-controlled fields to auth evidence**. Prerequisites: **ISK-0053, ISK-0057, ISK-0059**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **CEL sees typed safe metadata while secrets and spoofable trust are blocked**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0071 — First-party Telemetry & Audit Policies

- **Scope and intent:** Implement span enrich, custom metric, safe structured event and audited governance signal handlers.
- **Included:** Catalog telemetry.*; OTel allowlists; sampling; cardinality control.
- **Excluded/non-goals:** No request/prompt logging by default or duplicate billing events.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0057, ISK-0062, ISK-0026.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [10](10-first-party-policy-catalog.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0018](16-architecture-decision-register.md#adr-0018), [ADR-0019](16-architecture-decision-register.md#adr-0019), [ADR-0023](16-architecture-decision-register.md#adr-0023).
- **Acceptance evidence:** Policy-generated signals are correlated with requests but never disclose credentials.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0071 — First-party Telemetry & Audit Policies** for Iskender Gateway. Use the authoritative project inputs @docs/10-first-party-policy-catalog.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0018, ADR-0019, ADR-0023; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement span enrich, custom metric, safe structured event and audited governance signal handlers**. In-scope behaviors and artifacts: **Catalog telemetry.*; OTel allowlists; sampling; cardinality control**. Explicitly exclude: **No request/prompt logging by default or duplicate billing events**. Prerequisites: **ISK-0057, ISK-0062, ISK-0026**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Policy-generated signals are correlated with requests but never disclose credentials**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0072 — Policy Engine Conformance & Performance Gate

- **Scope and intent:** Stress inheritance/CEL/activation, all built-ins and streaming effects under adversarial conditions.
- **Included:** Plan property tests; mutation validation; p99 overhead; secure default verification.
- **Excluded/non-goals:** No unapproved fallback DSL or unnecessary engine rewrite.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0058, ISK-0059, ISK-0060, ISK-0061, ISK-0064, ISK-0068, ISK-0069, ISK-0070, ISK-0071.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [09](09-policy-language-and-execution.md), [10](10-first-party-policy-catalog.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017), [ADR-0018](16-architecture-decision-register.md#adr-0018), [ADR-0019](16-architecture-decision-register.md#adr-0019).
- **Acceptance evidence:** All 28 declared handlers have schema/negative tests and measured bounded runtime overhead.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0072 — Policy Engine Conformance & Performance Gate** for Iskender Gateway. Use the authoritative project inputs @docs/09-policy-language-and-execution.md @docs/10-first-party-policy-catalog.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0016, ADR-0017, ADR-0018, ADR-0019; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Stress inheritance/CEL/activation, all built-ins and streaming effects under adversarial conditions**. In-scope behaviors and artifacts: **Plan property tests; mutation validation; p99 overhead; secure default verification**. Explicitly exclude: **No unapproved fallback DSL or unnecessary engine rewrite**. Prerequisites: **ISK-0058, ISK-0059, ISK-0060, ISK-0061, ISK-0064, ISK-0068, ISK-0069, ISK-0070, ISK-0071**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **All 28 declared handlers have schema/negative tests and measured bounded runtime overhead**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0073 — Plugin SDK Template & Developer Samples

- **Scope and intent:** Provide dotnet new iskender-policy scaffold plus sample handler/test host as ergonomic public SDK experience.
- **Included:** Typed handler+options; test fixtures; DI; docs; sample config.
- **Excluded/non-goals:** No custom gateway image requirement or untrusted code execution.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0052, ISK-0057.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [09](09-policy-language-and-execution.md), [11](11-plugin-extensibility-and-sdk.md). **Relevant approved ADRs:** [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017), [ADR-0020](16-architecture-decision-register.md#adr-0020), [ADR-0021](16-architecture-decision-register.md#adr-0021).
- **Acceptance evidence:** New project compiles/packs, sample policy runs and docs match CLI.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0073 — Plugin SDK Template & Developer Samples** for Iskender Gateway. Use the authoritative project inputs @docs/09-policy-language-and-execution.md @docs/11-plugin-extensibility-and-sdk.md and the approved ADRs ADR-0016, ADR-0017, ADR-0020, ADR-0021; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Provide dotnet new iskender-policy scaffold plus sample handler/test host as ergonomic public SDK experience**. In-scope behaviors and artifacts: **Typed handler+options; test fixtures; DI; docs; sample config**. Explicitly exclude: **No custom gateway image requirement or untrusted code execution**. Prerequisites: **ISK-0052, ISK-0057**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **New project compiles/packs, sample policy runs and docs match CLI**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0074 — Pack-time Plugin Manifest, Hashes & Schemas

- **Scope and intent:** Automatically create canonical manifest/file hash inventory and typed JSON schema inside NuGet artifacts.
- **Included:** Build tasks; deterministic metadata; dep graph; manual manifest validation; path safety.
- **Excluded/non-goals:** No self-referential unverifiable manifest hash or blind signed status.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0073, ISK-0054.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [11](11-plugin-extensibility-and-sdk.md). **Relevant approved ADRs:** [ADR-0020](16-architecture-decision-register.md#adr-0020), [ADR-0021](16-architecture-decision-register.md#adr-0021), [ADR-0022](16-architecture-decision-register.md#adr-0022).
- **Acceptance evidence:** Changed byte or duplicate path detected; repeated pack produces deterministic manifest.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0074 — Pack-time Plugin Manifest, Hashes & Schemas** for Iskender Gateway. Use the authoritative project inputs @docs/11-plugin-extensibility-and-sdk.md and the approved ADRs ADR-0020, ADR-0021, ADR-0022; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Automatically create canonical manifest/file hash inventory and typed JSON schema inside NuGet artifacts**. In-scope behaviors and artifacts: **Build tasks; deterministic metadata; dep graph; manual manifest validation; path safety**. Explicitly exclude: **No self-referential unverifiable manifest hash or blind signed status**. Prerequisites: **ISK-0073, ISK-0054**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Changed byte or duplicate path detected; repeated pack produces deterministic manifest**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0075 — Plugin Inspection/Validation/Verification CLI

- **Scope and intent:** Ship Iskender CLI inspect/validate/verify with no plugin code execution during inspection.
- **Included:** Machine JSON diagnostics; schema checks; hash checks; compatibility verification.
- **Excluded/non-goals:** No automatic approval from successful verification.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0074.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [11](11-plugin-extensibility-and-sdk.md). **Relevant approved ADRs:** [ADR-0020](16-architecture-decision-register.md#adr-0020), [ADR-0021](16-architecture-decision-register.md#adr-0021), [ADR-0022](16-architecture-decision-register.md#adr-0022).
- **Acceptance evidence:** Tampered, missing-dependency or invalid SDK version returns nonzero with actionable fields.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0075 — Plugin Inspection/Validation/Verification CLI** for Iskender Gateway. Use the authoritative project inputs @docs/11-plugin-extensibility-and-sdk.md and the approved ADRs ADR-0020, ADR-0021, ADR-0022; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Ship Iskender CLI inspect/validate/verify with no plugin code execution during inspection**. In-scope behaviors and artifacts: **Machine JSON diagnostics; schema checks; hash checks; compatibility verification**. Explicitly exclude: **No automatic approval from successful verification**. Prerequisites: **ISK-0074**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Tampered, missing-dependency or invalid SDK version returns nonzero with actionable fields**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0076 — Operator Plugin Lockfile & Approval Workflow

- **Scope and intent:** Define and generate privileged approved artifact/dependency lockfiles with explicit exact integrity pinning.
- **Included:** CLI lock; transitive deps; namespace ownership; signatures optional; version pin.
- **Excluded/non-goals:** No floating latest or unaudited hot plugin approval.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0075, ISK-0015, ISK-0026.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md), [11](11-plugin-extensibility-and-sdk.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Unlisted and dependency-substituted packages cannot enter approved registry.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0076 — Operator Plugin Lockfile & Approval Workflow** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md @docs/11-plugin-extensibility-and-sdk.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Define and generate privileged approved artifact/dependency lockfiles with explicit exact integrity pinning**. In-scope behaviors and artifacts: **CLI lock; transitive deps; namespace ownership; signatures optional; version pin**. Explicitly exclude: **No floating latest or unaudited hot plugin approval**. Prerequisites: **ISK-0075, ISK-0015, ISK-0026**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Unlisted and dependency-substituted packages cannot enter approved registry**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0077 — Read-only Mounted Plugin Bundle Loading

- **Scope and intent:** Load explicitly approved bundles from read-only mounts using verified manifests and lockfile.
- **Included:** Startup resolver; handler descriptor registry; AssemblyLoadContext compatibility; readiness.
- **Excluded/non-goals:** No directory sweep and no claim managed load contexts sandbox malicious code.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0076, ISK-0057, ISK-0007.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [09](09-policy-language-and-execution.md), [11](11-plugin-extensibility-and-sdk.md). **Relevant approved ADRs:** [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017), [ADR-0020](16-architecture-decision-register.md#adr-0020), [ADR-0021](16-architecture-decision-register.md#adr-0021).
- **Acceptance evidence:** Stock container loads approved mount and refuses unlisted DLL.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0077 — Read-only Mounted Plugin Bundle Loading** for Iskender Gateway. Use the authoritative project inputs @docs/09-policy-language-and-execution.md @docs/11-plugin-extensibility-and-sdk.md and the approved ADRs ADR-0016, ADR-0017, ADR-0020, ADR-0021; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Load explicitly approved bundles from read-only mounts using verified manifests and lockfile**. In-scope behaviors and artifacts: **Startup resolver; handler descriptor registry; AssemblyLoadContext compatibility; readiness**. Explicitly exclude: **No directory sweep and no claim managed load contexts sandbox malicious code**. Prerequisites: **ISK-0076, ISK-0057, ISK-0007**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Stock container loads approved mount and refuses unlisted DLL**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0078 — Approved Startup NuGet Plugin Retrieval

- **Scope and intent:** Fetch pinned plugin artifacts from approved NuGet source at startup, verify before promotion.
- **Included:** Digest pin; bounded staging; feed failure; offline cache; dependency closure.
- **Excluded/non-goals:** No request-time package download or unpinned source trust.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0076, ISK-0077.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [11](11-plugin-extensibility-and-sdk.md). **Relevant approved ADRs:** [ADR-0020](16-architecture-decision-register.md#adr-0020), [ADR-0021](16-architecture-decision-register.md#adr-0021), [ADR-0022](16-architecture-decision-register.md#adr-0022).
- **Acceptance evidence:** Feed swapping and byte tampering fail closed; valid artifact loads on stock image.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0078 — Approved Startup NuGet Plugin Retrieval** for Iskender Gateway. Use the authoritative project inputs @docs/11-plugin-extensibility-and-sdk.md and the approved ADRs ADR-0020, ADR-0021, ADR-0022; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Fetch pinned plugin artifacts from approved NuGet source at startup, verify before promotion**. In-scope behaviors and artifacts: **Digest pin; bounded staging; feed failure; offline cache; dependency closure**. Explicitly exclude: **No request-time package download or unpinned source trust**. Prerequisites: **ISK-0076, ISK-0077**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Feed swapping and byte tampering fail closed; valid artifact loads on stock image**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0079 — Plugin Lifecycle, Compatibility & Safety Tests

- **Scope and intent:** Validate trusted in-process isolation limits, binary restart rule and dynamic policy settings.
- **Included:** SDK compatibility; DI scope; required plugin readiness; rolling restart; schema update.
- **Excluded/non-goals:** No runtime .NET assembly sandbox or hot binary replacement.
- **Milestone:** E4 — Governance, policy engine and extensibility (MVP).
- **Dependencies:** ISK-0077, ISK-0078, ISK-0057.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [09](09-policy-language-and-execution.md), [11](11-plugin-extensibility-and-sdk.md). **Relevant approved ADRs:** [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017), [ADR-0020](16-architecture-decision-register.md#adr-0020), [ADR-0021](16-architecture-decision-register.md#adr-0021).
- **Acceptance evidence:** Settings activate dynamically; new binary only after restart; absent required plugin blocks readiness.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0079 — Plugin Lifecycle, Compatibility & Safety Tests** for Iskender Gateway. Use the authoritative project inputs @docs/09-policy-language-and-execution.md @docs/11-plugin-extensibility-and-sdk.md and the approved ADRs ADR-0016, ADR-0017, ADR-0020, ADR-0021; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Validate trusted in-process isolation limits, binary restart rule and dynamic policy settings**. In-scope behaviors and artifacts: **SDK compatibility; DI scope; required plugin readiness; rolling restart; schema update**. Explicitly exclude: **No runtime .NET assembly sandbox or hot binary replacement**. Prerequisites: **ISK-0077, ISK-0078, ISK-0057**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Settings activate dynamically; new binary only after restart; absent required plugin blocks readiness**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0080 — Control-plane Management REST APIs

- **Scope and intent:** Expose authorized CRUD for projects, resources, aliases, deployments, policies, plugin approvals and usage views.
- **Included:** Pagination; optimistic revision; validation; audit; safe errors.
- **Excluded/non-goals:** No management UI requirement for core endpoint availability.
- **Milestone:** E5 — Operations and MVP release (MVP).
- **Dependencies:** ISK-0015, ISK-0020, ISK-0031, ISK-0058, ISK-0062, ISK-0076.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [05](05-identity-security-and-privacy.md), [09](09-policy-language-and-execution.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009).
- **Acceptance evidence:** All mutating calls enforce RBAC and published config revision.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0080 — Control-plane Management REST APIs** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/05-identity-security-and-privacy.md @docs/09-policy-language-and-execution.md and the approved ADRs ADR-0003, ADR-0005, ADR-0008, ADR-0009; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Expose authorized CRUD for projects, resources, aliases, deployments, policies, plugin approvals and usage views**. In-scope behaviors and artifacts: **Pagination; optimistic revision; validation; audit; safe errors**. Explicitly exclude: **No management UI requirement for core endpoint availability**. Prerequisites: **ISK-0015, ISK-0020, ISK-0031, ISK-0058, ISK-0062, ISK-0076**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **All mutating calls enforce RBAC and published config revision**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0081 — Management SPA Foundation & Authentication UX

- **Scope and intent:** Build Next.js/shadcn management shell with safe login/logout and org/project context switching.
- **Included:** Role-aware nav; accessible forms; OIDC/local sessions; safe error pages.
- **Excluded/non-goals:** No secret-bearing client bundle or independent SPA auth bypass.
- **Milestone:** E5 — Operations and MVP release (MVP).
- **Dependencies:** ISK-0080, ISK-0020, ISK-0007.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [04](04-technology-stack-and-deployment.md), [05](05-identity-security-and-privacy.md). **Relevant approved ADRs:** [ADR-0002](16-architecture-decision-register.md#adr-0002), [ADR-0004](16-architecture-decision-register.md#adr-0004), [ADR-0007](16-architecture-decision-register.md#adr-0007), [ADR-0005](16-architecture-decision-register.md#adr-0005).
- **Acceptance evidence:** Keyboard-accessible login and cross-tenant role switching tests pass.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0081 — Management SPA Foundation & Authentication UX** for Iskender Gateway. Use the authoritative project inputs @docs/04-technology-stack-and-deployment.md @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0002, ADR-0004, ADR-0007, ADR-0005; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Build Next.js/shadcn management shell with safe login/logout and org/project context switching**. In-scope behaviors and artifacts: **Role-aware nav; accessible forms; OIDC/local sessions; safe error pages**. Explicitly exclude: **No secret-bearing client bundle or independent SPA auth bypass**. Prerequisites: **ISK-0080, ISK-0020, ISK-0007**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Keyboard-accessible login and cross-tenant role switching tests pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0082 — Management Config, Policy & Plugin Workflows

- **Scope and intent:** Build practical resource configuration, effective-policy inspection and plugin approval views.
- **Included:** Alias/pool/deploy CRUD; CEL diagnostics; version diffs; plugin verification/lock displays.
- **Excluded/non-goals:** No uncontrolled client-side enforcement of resource ownership.
- **Milestone:** E5 — Operations and MVP release (MVP).
- **Dependencies:** ISK-0081, ISK-0080, ISK-0058, ISK-0079.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [09](09-policy-language-and-execution.md), [11](11-plugin-extensibility-and-sdk.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017).
- **Acceptance evidence:** Admin can diagnose and roll back invalid policy and inspect approved bundle digest.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0082 — Management Config, Policy & Plugin Workflows** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/09-policy-language-and-execution.md @docs/11-plugin-extensibility-and-sdk.md and the approved ADRs ADR-0003, ADR-0005, ADR-0016, ADR-0017; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Build practical resource configuration, effective-policy inspection and plugin approval views**. In-scope behaviors and artifacts: **Alias/pool/deploy CRUD; CEL diagnostics; version diffs; plugin verification/lock displays**. Explicitly exclude: **No uncontrolled client-side enforcement of resource ownership**. Prerequisites: **ISK-0081, ISK-0080, ISK-0058, ISK-0079**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Admin can diagnose and roll back invalid policy and inspect approved bundle digest**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0083 — Operations UI, Usage & Financial Views

- **Scope and intent:** Expose sanitized routing health, quotas, budget usage and trace correlation for operators.
- **Included:** Breaker/attempt panels; finance uncertainty; audit export; metrics links.
- **Excluded/non-goals:** No invoice billing or secret/prompt capture by default.
- **Milestone:** E5 — Operations and MVP release (MVP).
- **Dependencies:** ISK-0081, ISK-0066, ISK-0063, ISK-0071.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [08](08-routing-resilience-and-governance.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015), [ADR-0023](16-architecture-decision-register.md#adr-0023).
- **Acceptance evidence:** Authorized operator can correlate sampled request with ledger and denied rule.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0083 — Operations UI, Usage & Financial Views** for Iskender Gateway. Use the authoritative project inputs @docs/08-routing-resilience-and-governance.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0014, ADR-0015, ADR-0023; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Expose sanitized routing health, quotas, budget usage and trace correlation for operators**. In-scope behaviors and artifacts: **Breaker/attempt panels; finance uncertainty; audit export; metrics links**. Explicitly exclude: **No invoice billing or secret/prompt capture by default**. Prerequisites: **ISK-0081, ISK-0066, ISK-0063, ISK-0071**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Authorized operator can correlate sampled request with ledger and denied rule**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0084 — OTel Automatic Instrumentation & Diagnostics

- **Scope and intent:** Finalize GenAI semantic conventions, spans/meters/logging, cardinality, deployment correlation and export options.
- **Included:** Root spans; provider attempts; stream measurements; policy custom metrics; redaction tests.
- **Excluded/non-goals:** No provider SaaS export requirement or high-cardinality model payload tracing.
- **Milestone:** E5 — Operations and MVP release (MVP).
- **Dependencies:** ISK-0042, ISK-0062, ISK-0071, ISK-0083.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0023](16-architecture-decision-register.md#adr-0023).
- **Acceptance evidence:** Traces/metrics reveal latency source and pass secret/pii redaction fixtures.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0084 — OTel Automatic Instrumentation & Diagnostics** for Iskender Gateway. Use the authoritative project inputs @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0023; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Finalize GenAI semantic conventions, spans/meters/logging, cardinality, deployment correlation and export options**. In-scope behaviors and artifacts: **Root spans; provider attempts; stream measurements; policy custom metrics; redaction tests**. Explicitly exclude: **No provider SaaS export requirement or high-cardinality model payload tracing**. Prerequisites: **ISK-0042, ISK-0062, ISK-0071, ISK-0083**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Traces/metrics reveal latency source and pass secret/pii redaction fixtures**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0085 — Deployment, Security & Performance Release Hardening

- **Scope and intent:** Prove Compose, embedded, stock image and Azure ACA reference under load and failure with security audit.
- **Included:** 2+ replicas; chaos; backups; upgrade/rollback; SBOM; benchmark envelopes; threat review.
- **Excluded/non-goals:** No invented achieved SLO/latency numbers or extra feature scope.
- **Milestone:** E5 — Operations and MVP release (MVP).
- **Dependencies:** ISK-0028, ISK-0042, ISK-0051, ISK-0072, ISK-0079, ISK-0083, ISK-0084.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [04](04-technology-stack-and-deployment.md), [05](05-identity-security-and-privacy.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0002](16-architecture-decision-register.md#adr-0002), [ADR-0004](16-architecture-decision-register.md#adr-0004), [ADR-0007](16-architecture-decision-register.md#adr-0007), [ADR-0005](16-architecture-decision-register.md#adr-0005).
- **Acceptance evidence:** Release-candidate matrix, negative security, fault recovery, plugin tamper and benchmark reports pass approved gates.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0085 — Deployment, Security & Performance Release Hardening** for Iskender Gateway. Use the authoritative project inputs @docs/04-technology-stack-and-deployment.md @docs/05-identity-security-and-privacy.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0002, ADR-0004, ADR-0007, ADR-0005; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Prove Compose, embedded, stock image and Azure ACA reference under load and failure with security audit**. In-scope behaviors and artifacts: **2+ replicas; chaos; backups; upgrade/rollback; SBOM; benchmark envelopes; threat review**. Explicitly exclude: **No invented achieved SLO/latency numbers or extra feature scope**. Prerequisites: **ISK-0028, ISK-0042, ISK-0051, ISK-0072, ISK-0079, ISK-0083, ISK-0084**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Release-candidate matrix, negative security, fault recovery, plugin tamper and benchmark reports pass approved gates**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0086 — MVP Release Acceptance & Documentation

- **Scope and intent:** Complete explicit public MVP release gate and verifiable end-to-end docs, compatibility, provenance and release checklist.
- **Included:** All product PR-001–020; clean install; provider matrix; signed/pinned artifacts; version and tag rule for the public MVP and later releases, replacing the pre-MVP alpha rule; docs/adr/inventory; audit signoff.
- **Excluded/non-goals:** No post-MVP multimodal/general API claims or fake merge hashes.
- **Milestone:** E5 — Operations and MVP release (MVP).
- **Dependencies:** ISK-0085, ISK-0082, ISK-0084.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.1`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [01](01-product-charter-and-vision.md), [02](02-product-requirements-and-roadmap.md), [12](12-observability-and-nonfunctional-requirements.md), [13](13-development-standards-and-agent-workflows.md), [17](17-master-spec-inventory.md). **Relevant approved ADRs:** [ADR-0001](16-architecture-decision-register.md#adr-0001), [ADR-0029](16-architecture-decision-register.md#adr-0029), [ADR-0023](16-architecture-decision-register.md#adr-0023), [ADR-0025](16-architecture-decision-register.md#adr-0025), [ADR-0030](16-architecture-decision-register.md#adr-0030).
- **Acceptance evidence:** Human-approved release checklist passes and MVP is cut only after ISK-0086.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0086 — MVP Release Acceptance & Documentation** for Iskender Gateway. Use the authoritative project inputs @docs/01-product-charter-and-vision.md @docs/02-product-requirements-and-roadmap.md @docs/12-observability-and-nonfunctional-requirements.md @docs/13-development-standards-and-agent-workflows.md @docs/17-master-spec-inventory.md and the approved ADRs ADR-0001, ADR-0029, ADR-0023, ADR-0025, ADR-0030; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Complete explicit public MVP release gate and verifiable end-to-end docs, compatibility, provenance and release checklist**. In-scope behaviors and artifacts: **All product PR-001–020; clean install; provider matrix; signed/pinned artifacts; version and tag rule for the public MVP and later releases, replacing the pre-MVP alpha rule; docs/adr/inventory; audit signoff**. Explicitly exclude: **No post-MVP multimodal/general API claims or fake merge hashes**. Prerequisites: **ISK-0085, ISK-0082, ISK-0084**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Human-approved release checklist passes and MVP is cut only after ISK-0086**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0087 — Conversation Summarization Contracts & Safety

- **Scope and intent:** Introduce explicit opt-in canonical summarization and source provenance that preserves critical tool results.
- **Included:** Summary typed item; user/admin controls; retention; quality criteria; replay tests.
- **Excluded/non-goals:** No automatic silent truncation of required tool messages.
- **Milestone:** P1 — Conversation intelligence (post-MVP).
- **Dependencies:** ISK-0051, ISK-0086.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Summaries include provenance and a fixture detects omitted mandatory tool output.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0087 — Conversation Summarization Contracts & Safety** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md and the approved ADRs ADR-0011, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Introduce explicit opt-in canonical summarization and source provenance that preserves critical tool results**. In-scope behaviors and artifacts: **Summary typed item; user/admin controls; retention; quality criteria; replay tests**. Explicitly exclude: **No automatic silent truncation of required tool messages**. Prerequisites: **ISK-0051, ISK-0086**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Summaries include provenance and a fixture detects omitted mandatory tool output**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0088 — Token-aware Context Compaction Policies

- **Scope and intent:** Implement policy-driven bounded context compaction under model windows with deterministic fallback.
- **Included:** Compaction thresholds; candidate selection; audit and token estimation; rollback.
- **Excluded/non-goals:** No surprise deletion of authoritative conversation history.
- **Milestone:** P1 — Conversation intelligence (post-MVP).
- **Dependencies:** ISK-0087, ISK-0060.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015).
- **Acceptance evidence:** Large history fits target window or errors explicitly with retained original history.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0088 — Token-aware Context Compaction Policies** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0011, ADR-0012, ADR-0014, ADR-0015; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement policy-driven bounded context compaction under model windows with deterministic fallback**. In-scope behaviors and artifacts: **Compaction thresholds; candidate selection; audit and token estimation; rollback**. Explicitly exclude: **No surprise deletion of authoritative conversation history**. Prerequisites: **ISK-0087, ISK-0060**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Large history fits target window or errors explicitly with retained original history**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0089 — Conversation Branching & Forked Continuations

- **Scope and intent:** Allow authorized branch checkpoints with lineage and conflict-free revisions.
- **Included:** Branch IDs; parent snapshots; copy-on-write item refs; permissions.
- **Excluded/non-goals:** No default automatic branch on concurrent write race.
- **Milestone:** P1 — Conversation intelligence (post-MVP).
- **Dependencies:** ISK-0045, ISK-0087.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Branches remain isolated while parent history is immutable.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0089 — Conversation Branching & Forked Continuations** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md and the approved ADRs ADR-0011, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Allow authorized branch checkpoints with lineage and conflict-free revisions**. In-scope behaviors and artifacts: **Branch IDs; parent snapshots; copy-on-write item refs; permissions**. Explicitly exclude: **No default automatic branch on concurrent write race**. Prerequisites: **ISK-0045, ISK-0087**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Branches remain isolated while parent history is immutable**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0090 — Conversation Search & Metadata Index

- **Scope and intent:** Add tenant-scoped searchable metadata and optional content indexes with privacy controls.
- **Included:** Index policy; safe filters; deletes; access; performance.
- **Excluded/non-goals:** No default whole-content search of private prompts.
- **Milestone:** P1 — Conversation intelligence (post-MVP).
- **Dependencies:** ISK-0051, ISK-0087.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md), [06](06-conversation-state-architecture.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Deleting conversation removes index entries, project scope enforced.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0090 — Conversation Search & Metadata Index** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md @docs/06-conversation-state-architecture.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Add tenant-scoped searchable metadata and optional content indexes with privacy controls**. In-scope behaviors and artifacts: **Index policy; safe filters; deletes; access; performance**. Explicitly exclude: **No default whole-content search of private prompts**. Prerequisites: **ISK-0051, ISK-0087**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Deleting conversation removes index entries, project scope enforced**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0091 — Intelligent Context Selection

- **Scope and intent:** Add opt-in relevance selection and guardrails to preserve essential context through cost/quality tradeoffs.
- **Included:** Source trace; token budget; required item pins; evaluation harness.
- **Excluded/non-goals:** No guarantee semantically equivalent hidden reasoning.
- **Milestone:** P1 — Conversation intelligence (post-MVP).
- **Dependencies:** ISK-0088, ISK-0089.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Evaluation shows selected context fidelity and provenance in controlled cases.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0091 — Intelligent Context Selection** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md and the approved ADRs ADR-0011, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Add opt-in relevance selection and guardrails to preserve essential context through cost/quality tradeoffs**. In-scope behaviors and artifacts: **Source trace; token budget; required item pins; evaluation harness**. Explicitly exclude: **No guarantee semantically equivalent hidden reasoning**. Prerequisites: **ISK-0088, ISK-0089**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Evaluation shows selected context fidelity and provenance in controlled cases**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0092 — Provider-native State Bridge Enhancements

- **Scope and intent:** Improve optional translation of supported provider-native thread/response ID workflows and migration signals.
- **Included:** Provider mapping profiles; state export where permitted; conformance.
- **Excluded/non-goals:** No universal hidden reasoning migration promise.
- **Milestone:** P1 — Conversation intelligence (post-MVP).
- **Dependencies:** ISK-0087, ISK-0030.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0013](16-architecture-decision-register.md#adr-0013).
- **Acceptance evidence:** Unsupported native state emits capability error, supported paths replay correctly.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0092 — Provider-native State Bridge Enhancements** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0011, ADR-0012, ADR-0013; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Improve optional translation of supported provider-native thread/response ID workflows and migration signals**. In-scope behaviors and artifacts: **Provider mapping profiles; state export where permitted; conformance**. Explicitly exclude: **No universal hidden reasoning migration promise**. Prerequisites: **ISK-0087, ISK-0030**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Unsupported native state emits capability error, supported paths replay correctly**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0093 — Tool Workflow Continuation Extensions

- **Scope and intent:** Extend tool-turn lifecycle, parallel tool calls and async tool result continuation safely.
- **Included:** Tool correlation; retry/idempotency; timeouts; partial failure.
- **Excluded/non-goals:** No autonomous agent execution host as prerequisite.
- **Milestone:** P1 — Conversation intelligence (post-MVP).
- **Dependencies:** ISK-0040, ISK-0049, ISK-0086.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0013](16-architecture-decision-register.md#adr-0013).
- **Acceptance evidence:** Concurrent tool result order validated across resume and retry.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0093 — Tool Workflow Continuation Extensions** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0011, ADR-0012, ADR-0013; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Extend tool-turn lifecycle, parallel tool calls and async tool result continuation safely**. In-scope behaviors and artifacts: **Tool correlation; retry/idempotency; timeouts; partial failure**. Explicitly exclude: **No autonomous agent execution host as prerequisite**. Prerequisites: **ISK-0040, ISK-0049, ISK-0086**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Concurrent tool result order validated across resume and retry**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0094 — Conversation Retention Policy Templates

- **Scope and intent:** Provide richer organizational retention policy options, export and compliance auditability.
- **Included:** Policy templates; deletion evidence; tenant export; legal preservation hook.
- **Excluded/non-goals:** No certification or false provider-side deletion claim.
- **Milestone:** P1 — Conversation intelligence (post-MVP).
- **Dependencies:** ISK-0051, ISK-0090.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md), [06](06-conversation-state-architecture.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Export/delete access tests and privacy audit templates pass.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0094 — Conversation Retention Policy Templates** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md @docs/06-conversation-state-architecture.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Provide richer organizational retention policy options, export and compliance auditability**. In-scope behaviors and artifacts: **Policy templates; deletion evidence; tenant export; legal preservation hook**. Explicitly exclude: **No certification or false provider-side deletion claim**. Prerequisites: **ISK-0051, ISK-0090**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Export/delete access tests and privacy audit templates pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0095 — Conversation Intelligence Release Gate

- **Scope and intent:** Validate advanced context capabilities as opt-in features with migration and isolation tests.
- **Included:** Branch/summary/compaction regression; versioned API docs; benchmark.
- **Excluded/non-goals:** No weakening Mode C MVP durability/integrity.
- **Milestone:** P1 — Conversation intelligence (post-MVP).
- **Dependencies:** ISK-0087, ISK-0088, ISK-0089, ISK-0090, ISK-0091, ISK-0092, ISK-0093, ISK-0094.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0023](16-architecture-decision-register.md#adr-0023).
- **Acceptance evidence:** P1 suite proves cross-provider continuation and original history protected.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0095 — Conversation Intelligence Release Gate** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0011, ADR-0012, ADR-0023; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Validate advanced context capabilities as opt-in features with migration and isolation tests**. In-scope behaviors and artifacts: **Branch/summary/compaction regression; versioned API docs; benchmark**. Explicitly exclude: **No weakening Mode C MVP durability/integrity**. Prerequisites: **ISK-0087, ISK-0088, ISK-0089, ISK-0090, ISK-0091, ISK-0092, ISK-0093, ISK-0094**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **P1 suite proves cross-provider continuation and original history protected**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0096 — Canonical Multimodal Content Schema

- **Scope and intent:** Extend typed item parts for images, audio, video and structured modality metadata.
- **Included:** Blob refs; mime/size; provenance; retention; capability flags.
- **Excluded/non-goals:** No embedded unrestricted object storage or fake provider support.
- **Milestone:** P2 — Multimodal (post-MVP).
- **Dependencies:** ISK-0095, ISK-0044.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0013](16-architecture-decision-register.md#adr-0013).
- **Acceptance evidence:** Content roundtrip and per-modality retention/size limits pass.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0096 — Canonical Multimodal Content Schema** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0011, ADR-0012, ADR-0013; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Extend typed item parts for images, audio, video and structured modality metadata**. In-scope behaviors and artifacts: **Blob refs; mime/size; provenance; retention; capability flags**. Explicitly exclude: **No embedded unrestricted object storage or fake provider support**. Prerequisites: **ISK-0095, ISK-0044**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Content roundtrip and per-modality retention/size limits pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0097 — Image Input Processing & Adapter Support

- **Scope and intent:** Add provider-gated image inputs with secure media handling and prompt attachment mapping.
- **Included:** Image URL/bytes; MIME security; storage refs; OpenAI/Gemini/Anthropic mapping.
- **Excluded/non-goals:** No image generation inference product unless separately scoped.
- **Milestone:** P2 — Multimodal (post-MVP).
- **Dependencies:** ISK-0096, ISK-0030.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Media validation and provider capability/rejection test matrix pass.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0097 — Image Input Processing & Adapter Support** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0013, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Add provider-gated image inputs with secure media handling and prompt attachment mapping**. In-scope behaviors and artifacts: **Image URL/bytes; MIME security; storage refs; OpenAI/Gemini/Anthropic mapping**. Explicitly exclude: **No image generation inference product unless separately scoped**. Prerequisites: **ISK-0096, ISK-0030**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Media validation and provider capability/rejection test matrix pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0098 — Audio Ingestion & Speech Input

- **Scope and intent:** Support declared transcription/audio-input modality ingress and normalized event payloads.
- **Included:** Audio formats; streaming/nonstreaming; usage; deadline; provider mapping.
- **Excluded/non-goals:** No voice assistant UX or universal real-time audio equivalence.
- **Milestone:** P2 — Multimodal (post-MVP).
- **Dependencies:** ISK-0096, ISK-0030.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Audio validation and stream failure tests pass without unbounded buffering.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0098 — Audio Ingestion & Speech Input** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0013, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Support declared transcription/audio-input modality ingress and normalized event payloads**. In-scope behaviors and artifacts: **Audio formats; streaming/nonstreaming; usage; deadline; provider mapping**. Explicitly exclude: **No voice assistant UX or universal real-time audio equivalence**. Prerequisites: **ISK-0096, ISK-0030**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Audio validation and stream failure tests pass without unbounded buffering**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0099 — Audio Generation & Speech Output

- **Scope and intent:** Provide speech/audio-output with provider capability declarations, chunk ordering and quotas.
- **Included:** Output formats; usage; SSE/audio chunks; cancellation.
- **Excluded/non-goals:** No claim all chat models return speech.
- **Milestone:** P2 — Multimodal (post-MVP).
- **Dependencies:** ISK-0098, ISK-0062.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015).
- **Acceptance evidence:** Streaming audio order, accounting and unsupported fallback behavior verified.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0099 — Audio Generation & Speech Output** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0013, ADR-0012, ADR-0014, ADR-0015; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Provide speech/audio-output with provider capability declarations, chunk ordering and quotas**. In-scope behaviors and artifacts: **Output formats; usage; SSE/audio chunks; cancellation**. Explicitly exclude: **No claim all chat models return speech**. Prerequisites: **ISK-0098, ISK-0062**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Streaming audio order, accounting and unsupported fallback behavior verified**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0100 — Realtime Multimodal Session Gateway

- **Scope and intent:** Introduce session lifecycle and duplex streaming semantics for supported real-time providers.
- **Included:** Connection auth; session quotas; protocol adaptation; interruption.
- **Excluded/non-goals:** No unsafe universal WebRTC/WS equivalence assumptions.
- **Milestone:** P2 — Multimodal (post-MVP).
- **Dependencies:** ISK-0098, ISK-0099, ISK-0019.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015).
- **Acceptance evidence:** Session teardown, reconnect and tenant isolation verified under load.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0100 — Realtime Multimodal Session Gateway** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0013, ADR-0012, ADR-0014, ADR-0015; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Introduce session lifecycle and duplex streaming semantics for supported real-time providers**. In-scope behaviors and artifacts: **Connection auth; session quotas; protocol adaptation; interruption**. Explicitly exclude: **No unsafe universal WebRTC/WS equivalence assumptions**. Prerequisites: **ISK-0098, ISK-0099, ISK-0019**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Session teardown, reconnect and tenant isolation verified under load**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0101 — Video Input & Timed Frame Semantics

- **Scope and intent:** Add time-indexed frame input and validated media references for capable providers.
- **Included:** Timecodes; frame limits; provider mapping; costs.
- **Excluded/non-goals:** No video transcoding service or guarantee every model supports video.
- **Milestone:** P2 — Multimodal (post-MVP).
- **Dependencies:** ISK-0096, ISK-0030.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012).
- **Acceptance evidence:** Frame ordering and maximum-size tests exercise compatibility matrix.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0101 — Video Input & Timed Frame Semantics** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0013, ADR-0012; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Add time-indexed frame input and validated media references for capable providers**. In-scope behaviors and artifacts: **Timecodes; frame limits; provider mapping; costs**. Explicitly exclude: **No video transcoding service or guarantee every model supports video**. Prerequisites: **ISK-0096, ISK-0030**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Frame ordering and maximum-size tests exercise compatibility matrix**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0102 — Multimodal Structured Output & Tool Events

- **Scope and intent:** Extend strict schema/tool support to modalities without losing typed conversation semantics.
- **Included:** Cross-modal tool parts; schema compatibility; safe streaming mapping.
- **Excluded/non-goals:** No generic untyped binary dumps in policy context.
- **Milestone:** P2 — Multimodal (post-MVP).
- **Dependencies:** ISK-0097, ISK-0098, ISK-0099, ISK-0101.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [06](06-conversation-state-architecture.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0011](16-architecture-decision-register.md#adr-0011), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0013](16-architecture-decision-register.md#adr-0013).
- **Acceptance evidence:** Mixed text/media/tool fixtures roundtrip and reject unsupported combinations.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0102 — Multimodal Structured Output & Tool Events** for Iskender Gateway. Use the authoritative project inputs @docs/06-conversation-state-architecture.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0011, ADR-0012, ADR-0013; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Extend strict schema/tool support to modalities without losing typed conversation semantics**. In-scope behaviors and artifacts: **Cross-modal tool parts; schema compatibility; safe streaming mapping**. Explicitly exclude: **No generic untyped binary dumps in policy context**. Prerequisites: **ISK-0097, ISK-0098, ISK-0099, ISK-0101**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Mixed text/media/tool fixtures roundtrip and reject unsupported combinations**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0103 — Multimodal Cost & Safety Governance

- **Scope and intent:** Apply modality-aware size, token/unit accounting and budgets with explicit uncertainty.
- **Included:** Audio seconds; image frames; multi-unit price model; policy gates.
- **Excluded/non-goals:** No invoice certainty or hidden media logging.
- **Milestone:** P2 — Multimodal (post-MVP).
- **Dependencies:** ISK-0102, ISK-0063.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [08](08-routing-resilience-and-governance.md), [10](10-first-party-policy-catalog.md). **Relevant approved ADRs:** [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015), [ADR-0018](16-architecture-decision-register.md#adr-0018), [ADR-0019](16-architecture-decision-register.md#adr-0019).
- **Acceptance evidence:** Concurrent multimodal budget reservations settle with correct unit provenance.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0103 — Multimodal Cost & Safety Governance** for Iskender Gateway. Use the authoritative project inputs @docs/08-routing-resilience-and-governance.md @docs/10-first-party-policy-catalog.md and the approved ADRs ADR-0014, ADR-0015, ADR-0018, ADR-0019; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Apply modality-aware size, token/unit accounting and budgets with explicit uncertainty**. In-scope behaviors and artifacts: **Audio seconds; image frames; multi-unit price model; policy gates**. Explicitly exclude: **No invoice certainty or hidden media logging**. Prerequisites: **ISK-0102, ISK-0063**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Concurrent multimodal budget reservations settle with correct unit provenance**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0104 — Multimodal Release Gate

- **Scope and intent:** Publish tested modality capability matrix and failure/retention/performance evidence.
- **Included:** Provider feature tests; security scanning; docs; rollback.
- **Excluded/non-goals:** No multimodal marketing without passing conformance.
- **Milestone:** P2 — Multimodal (post-MVP).
- **Dependencies:** ISK-0096, ISK-0097, ISK-0098, ISK-0099, ISK-0100, ISK-0101, ISK-0102, ISK-0103.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0023](16-architecture-decision-register.md#adr-0023).
- **Acceptance evidence:** P2 release gate certifies only documented supported modalities.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0104 — Multimodal Release Gate** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0013, ADR-0012, ADR-0023; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Publish tested modality capability matrix and failure/retention/performance evidence**. In-scope behaviors and artifacts: **Provider feature tests; security scanning; docs; rollback**. Explicitly exclude: **No multimodal marketing without passing conformance**. Prerequisites: **ISK-0096, ISK-0097, ISK-0098, ISK-0099, ISK-0100, ISK-0101, ISK-0102, ISK-0103**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **P2 release gate certifies only documented supported modalities**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0105 — Additional Provider Adapter Extension Packs

- **Scope and intent:** Introduce additional chosen providers through versioned adapter package interfaces.
- **Included:** Discovery; auth profile; conformance; availability and provider-specific errors.
- **Excluded/non-goals:** No mandatory commitment to any unevaluated vendor.
- **Milestone:** P3 — Providers and deployment expansion (post-MVP).
- **Dependencies:** ISK-0104, ISK-0030.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md), [11](11-plugin-extensibility-and-sdk.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0020](16-architecture-decision-register.md#adr-0020), [ADR-0021](16-architecture-decision-register.md#adr-0021).
- **Acceptance evidence:** New provider adapter passes public conformance without core rewrite.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0105 — Additional Provider Adapter Extension Packs** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md @docs/11-plugin-extensibility-and-sdk.md and the approved ADRs ADR-0013, ADR-0012, ADR-0020, ADR-0021; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Introduce additional chosen providers through versioned adapter package interfaces**. In-scope behaviors and artifacts: **Discovery; auth profile; conformance; availability and provider-specific errors**. Explicitly exclude: **No mandatory commitment to any unevaluated vendor**. Prerequisites: **ISK-0104, ISK-0030**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **New provider adapter passes public conformance without core rewrite**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0106 — AWS IAM & Bedrock Provider Support

- **Scope and intent:** Provide a distinct AWS credential/signing profile and Bedrock adapter after provider review.
- **Included:** IAM role/STS signing; region; model capability; audit.
- **Excluded/non-goals:** No AWS SDK in core domain abstractions.
- **Milestone:** P3 — Providers and deployment expansion (post-MVP).
- **Dependencies:** ISK-0105, ISK-0022.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md), [07](07-provider-adapters-and-api-compatibility.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** Expired credentials, scope and model capability tests pass.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0106 — AWS IAM & Bedrock Provider Support** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Provide a distinct AWS credential/signing profile and Bedrock adapter after provider review**. In-scope behaviors and artifacts: **IAM role/STS signing; region; model capability; audit**. Explicitly exclude: **No AWS SDK in core domain abstractions**. Prerequisites: **ISK-0105, ISK-0022**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Expired credentials, scope and model capability tests pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0107 — Provider Failover Simulation & Explainability

- **Scope and intent:** Offer offline replay/counterfactual routing and failure-mode simulation using sanitized requests.
- **Included:** Policy versions; routing candidate reason; what-if latency/cost.
- **Excluded/non-goals:** No replay of private production prompts by default.
- **Milestone:** P3 — Providers and deployment expansion (post-MVP).
- **Dependencies:** ISK-0065, ISK-0066, ISK-0086.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [08](08-routing-resilience-and-governance.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015), [ADR-0023](16-architecture-decision-register.md#adr-0023).
- **Acceptance evidence:** Simulation reproduces observed selection on recorded safe metadata.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0107 — Provider Failover Simulation & Explainability** for Iskender Gateway. Use the authoritative project inputs @docs/08-routing-resilience-and-governance.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0014, ADR-0015, ADR-0023; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Offer offline replay/counterfactual routing and failure-mode simulation using sanitized requests**. In-scope behaviors and artifacts: **Policy versions; routing candidate reason; what-if latency/cost**. Explicitly exclude: **No replay of private production prompts by default**. Prerequisites: **ISK-0065, ISK-0066, ISK-0086**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Simulation reproduces observed selection on recorded safe metadata**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0108 — Inference Response Caching & Invalidation

- **Scope and intent:** Add exact-match cache where semantics, privacy, model/version and scope permit.
- **Included:** Key hashing; tenant partitions; TTL; quota/cost accounting.
- **Excluded/non-goals:** No semantic cache default or storage of secrets in cache keys.
- **Milestone:** P3 — Providers and deployment expansion (post-MVP).
- **Dependencies:** ISK-0105, ISK-0062.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015).
- **Acceptance evidence:** Tenant cache isolation and invalidation prevent stale unsafe responses.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0108 — Inference Response Caching & Invalidation** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0013, ADR-0012, ADR-0014, ADR-0015; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Add exact-match cache where semantics, privacy, model/version and scope permit**. In-scope behaviors and artifacts: **Key hashing; tenant partitions; TTL; quota/cost accounting**. Explicitly exclude: **No semantic cache default or storage of secrets in cache keys**. Prerequisites: **ISK-0105, ISK-0062**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Tenant cache isolation and invalidation prevent stale unsafe responses**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0109 — Semantic Cache Experiments & Guardrails

- **Scope and intent:** Evaluate optional approximate response reuse with quality and privacy thresholds.
- **Included:** Embedding similarity; refusal of unsafe reuse; metrics; opt-in control.
- **Excluded/non-goals:** No global cross-user cache or claim exact semantic correctness.
- **Milestone:** P3 — Providers and deployment expansion (post-MVP).
- **Dependencies:** ISK-0108, ISK-0038.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015).
- **Acceptance evidence:** False-match test corpus and user privacy boundaries gate enablement.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0109 — Semantic Cache Experiments & Guardrails** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0013, ADR-0012, ADR-0014, ADR-0015; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Evaluate optional approximate response reuse with quality and privacy thresholds**. In-scope behaviors and artifacts: **Embedding similarity; refusal of unsafe reuse; metrics; opt-in control**. Explicitly exclude: **No global cross-user cache or claim exact semantic correctness**. Prerequisites: **ISK-0108, ISK-0038**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **False-match test corpus and user privacy boundaries gate enablement**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0110 — Advanced Routing Optimization & Canary

- **Scope and intent:** Add model quality/cost/latency adaptive routing plus canary/shadow with safe policies.
- **Included:** Exploration budgets; policy precedence; analysis; canary rollback.
- **Excluded/non-goals:** No bypass of hard budgets, provider restrictions or live-output safety.
- **Milestone:** P3 — Providers and deployment expansion (post-MVP).
- **Dependencies:** ISK-0107, ISK-0108.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015).
- **Acceptance evidence:** Adaptive selection and canary revert tests maintain governed candidate set.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0110 — Advanced Routing Optimization & Canary** for Iskender Gateway. Use the authoritative project inputs @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0014, ADR-0015; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Add model quality/cost/latency adaptive routing plus canary/shadow with safe policies**. In-scope behaviors and artifacts: **Exploration budgets; policy precedence; analysis; canary rollback**. Explicitly exclude: **No bypass of hard budgets, provider restrictions or live-output safety**. Prerequisites: **ISK-0107, ISK-0108**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Adaptive selection and canary revert tests maintain governed candidate set**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0111 — Provider Batch & Asynchronous Jobs

- **Scope and intent:** Introduce batch inference and job lifecycle with durable reconciliation/cancellation.
- **Included:** Async request IDs; provider batch mapping; quotas; audit.
- **Excluded/non-goals:** No generic distributed job platform outside inference.
- **Milestone:** P3 — Providers and deployment expansion (post-MVP).
- **Dependencies:** ISK-0105, ISK-0009.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [07](07-provider-adapters-and-api-compatibility.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0013](16-architecture-decision-register.md#adr-0013), [ADR-0012](16-architecture-decision-register.md#adr-0012), [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015).
- **Acceptance evidence:** Job crash/restart recovery and cross-tenant isolation pass.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0111 — Provider Batch & Asynchronous Jobs** for Iskender Gateway. Use the authoritative project inputs @docs/07-provider-adapters-and-api-compatibility.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0013, ADR-0012, ADR-0014, ADR-0015; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Introduce batch inference and job lifecycle with durable reconciliation/cancellation**. In-scope behaviors and artifacts: **Async request IDs; provider batch mapping; quotas; audit**. Explicitly exclude: **No generic distributed job platform outside inference**. Prerequisites: **ISK-0105, ISK-0009**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Job crash/restart recovery and cross-tenant isolation pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0112 — Expanded Provider/Deployment Release Gate

- **Scope and intent:** Validate expanded adapters, routing/cache and deployment options against MVP contracts.
- **Included:** Capability matrix; cost/health tests; load and security checks.
- **Excluded/non-goals:** No retroactive weakening of MVP feature promises.
- **Milestone:** P3 — Providers and deployment expansion (post-MVP).
- **Dependencies:** ISK-0105, ISK-0106, ISK-0107, ISK-0108, ISK-0109, ISK-0110, ISK-0111.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [04](04-technology-stack-and-deployment.md), [07](07-provider-adapters-and-api-compatibility.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0002](16-architecture-decision-register.md#adr-0002), [ADR-0004](16-architecture-decision-register.md#adr-0004), [ADR-0007](16-architecture-decision-register.md#adr-0007), [ADR-0013](16-architecture-decision-register.md#adr-0013).
- **Acceptance evidence:** P3 release requires no regression in MVP endpoint and state behavior.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0112 — Expanded Provider/Deployment Release Gate** for Iskender Gateway. Use the authoritative project inputs @docs/04-technology-stack-and-deployment.md @docs/07-provider-adapters-and-api-compatibility.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0002, ADR-0004, ADR-0007, ADR-0013; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Validate expanded adapters, routing/cache and deployment options against MVP contracts**. In-scope behaviors and artifacts: **Capability matrix; cost/health tests; load and security checks**. Explicitly exclude: **No retroactive weakening of MVP feature promises**. Prerequisites: **ISK-0105, ISK-0106, ISK-0107, ISK-0108, ISK-0109, ISK-0110, ISK-0111**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **P3 release requires no regression in MVP endpoint and state behavior**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0113 — Generic HTTP Route Domain Model

- **Scope and intent:** Extend foundational neutral endpoint resources to arbitrary HTTP route/upstream concepts.
- **Included:** Route matching; URI templates; backend targets; ownership; versioning.
- **Excluded/non-goals:** No immediate full APIM feature-parity claim.
- **Milestone:** P4 — General-purpose HTTP API gateway (post-MVP).
- **Dependencies:** ISK-0112, ISK-0012, ISK-0031.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015).
- **Acceptance evidence:** AI inference route continues to work unchanged alongside basic HTTP route.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0113 — Generic HTTP Route Domain Model** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0003, ADR-0005, ADR-0014, ADR-0015; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Extend foundational neutral endpoint resources to arbitrary HTTP route/upstream concepts**. In-scope behaviors and artifacts: **Route matching; URI templates; backend targets; ownership; versioning**. Explicitly exclude: **No immediate full APIM feature-parity claim**. Prerequisites: **ISK-0112, ISK-0012, ISK-0031**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **AI inference route continues to work unchanged alongside basic HTTP route**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0114 — General HTTP Proxy & Streaming Pipeline

- **Scope and intent:** Implement standards-compliant HTTP proxy semantics with safe headers, streaming and cancellation.
- **Included:** Methods; bodies; hop-by-hop headers; TLS; backpressure; redirects.
- **Excluded/non-goals:** No unbounded buffering or transparent security bypass.
- **Milestone:** P4 — General-purpose HTTP API gateway (post-MVP).
- **Dependencies:** ISK-0113, ISK-0041.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015).
- **Acceptance evidence:** RFC-relevant proxy tests pass for chunked streams and cancellation.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0114 — General HTTP Proxy & Streaming Pipeline** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0003, ADR-0005, ADR-0014, ADR-0015; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Implement standards-compliant HTTP proxy semantics with safe headers, streaming and cancellation**. In-scope behaviors and artifacts: **Methods; bodies; hop-by-hop headers; TLS; backpressure; redirects**. Explicitly exclude: **No unbounded buffering or transparent security bypass**. Prerequisites: **ISK-0113, ISK-0041**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **RFC-relevant proxy tests pass for chunked streams and cancellation**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0115 — HTTP Backend Health, Routing & Failover

- **Scope and intent:** Reuse generic pool/health/breaker contracts for non-AI HTTP workloads.
- **Included:** Backend health; weight/priority; retries conditioned on method idempotency.
- **Excluded/non-goals:** No LLM token semantics imposed on generic HTTP.
- **Milestone:** P4 — General-purpose HTTP API gateway (post-MVP).
- **Dependencies:** ISK-0114, ISK-0066, ISK-0067.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015).
- **Acceptance evidence:** GET/POST retry semantics and independent backend state verified.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0115 — HTTP Backend Health, Routing & Failover** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0003, ADR-0005, ADR-0014, ADR-0015; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Reuse generic pool/health/breaker contracts for non-AI HTTP workloads**. In-scope behaviors and artifacts: **Backend health; weight/priority; retries conditioned on method idempotency**. Explicitly exclude: **No LLM token semantics imposed on generic HTTP**. Prerequisites: **ISK-0114, ISK-0066, ISK-0067**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **GET/POST retry semantics and independent backend state verified**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0116 — Generic Request/Response Transformation Policies

- **Scope and intent:** Extend typed policy SDK to common HTTP header/query/body transformation with strict bounds.
- **Included:** Versioned config; safe body types; stage validation; audit.
- **Excluded/non-goals:** No custom APIM XML language or arbitrary untrusted scripting.
- **Milestone:** P4 — General-purpose HTTP API gateway (post-MVP).
- **Dependencies:** ISK-0114, ISK-0052, ISK-0069.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [09](09-policy-language-and-execution.md), [10](10-first-party-policy-catalog.md). **Relevant approved ADRs:** [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017), [ADR-0018](16-architecture-decision-register.md#adr-0018), [ADR-0019](16-architecture-decision-register.md#adr-0019).
- **Acceptance evidence:** Protected headers and content-length/stream integrity never corrupted.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0116 — Generic Request/Response Transformation Policies** for Iskender Gateway. Use the authoritative project inputs @docs/09-policy-language-and-execution.md @docs/10-first-party-policy-catalog.md and the approved ADRs ADR-0016, ADR-0017, ADR-0018, ADR-0019; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Extend typed policy SDK to common HTTP header/query/body transformation with strict bounds**. In-scope behaviors and artifacts: **Versioned config; safe body types; stage validation; audit**. Explicitly exclude: **No custom APIM XML language or arbitrary untrusted scripting**. Prerequisites: **ISK-0114, ISK-0052, ISK-0069**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Protected headers and content-length/stream integrity never corrupted**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0117 — HTTP Authentication & Authorization Extensions

- **Scope and intent:** Adapt core security and scoped auth guardrails to general HTTP resources.
- **Included:** Key/JWT attachment; path/method rules; claim policies; authorization diagnostics.
- **Excluded/non-goals:** No replacement of mandatory Iskender scope checks.
- **Milestone:** P4 — General-purpose HTTP API gateway (post-MVP).
- **Dependencies:** ISK-0113, ISK-0114, ISK-0019.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [05](05-identity-security-and-privacy.md), [09](09-policy-language-and-execution.md). **Relevant approved ADRs:** [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009), [ADR-0010](16-architecture-decision-register.md#adr-0010).
- **Acceptance evidence:** HTTP resource auth denies cross-project caller across all HTTP verbs.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0117 — HTTP Authentication & Authorization Extensions** for Iskender Gateway. Use the authoritative project inputs @docs/05-identity-security-and-privacy.md @docs/09-policy-language-and-execution.md and the approved ADRs ADR-0005, ADR-0008, ADR-0009, ADR-0010; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Adapt core security and scoped auth guardrails to general HTTP resources**. In-scope behaviors and artifacts: **Key/JWT attachment; path/method rules; claim policies; authorization diagnostics**. Explicitly exclude: **No replacement of mandatory Iskender scope checks**. Prerequisites: **ISK-0113, ISK-0114, ISK-0019**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **HTTP resource auth denies cross-project caller across all HTTP verbs**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0118 — Generic API Product/Subscription Concepts

- **Scope and intent:** Add organizational API products and consumer subscription lifecycle on top of resource scopes.
- **Included:** API product membership; plans; key scopes; quotas; publication.
- **Excluded/non-goals:** No payment processor or proprietary billing requirement.
- **Milestone:** P4 — General-purpose HTTP API gateway (post-MVP).
- **Dependencies:** ISK-0117, ISK-0063.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [05](05-identity-security-and-privacy.md), [08](08-routing-resilience-and-governance.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009).
- **Acceptance evidence:** Subscription cannot widen project ownership or bypass hard limits.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0118 — Generic API Product/Subscription Concepts** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/05-identity-security-and-privacy.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0003, ADR-0005, ADR-0008, ADR-0009; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Add organizational API products and consumer subscription lifecycle on top of resource scopes**. In-scope behaviors and artifacts: **API product membership; plans; key scopes; quotas; publication**. Explicitly exclude: **No payment processor or proprietary billing requirement**. Prerequisites: **ISK-0117, ISK-0063**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Subscription cannot widen project ownership or bypass hard limits**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0119 — Generic HTTP Caching Policies

- **Scope and intent:** Provide standards-aware HTTP response caching where headers/security/tenant isolation permit.
- **Included:** Cache-Control; Vary; ETag; invalidation; auth-aware keys.
- **Excluded/non-goals:** No public leakage of authenticated body across tenants.
- **Milestone:** P4 — General-purpose HTTP API gateway (post-MVP).
- **Dependencies:** ISK-0116, ISK-0108.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [08](08-routing-resilience-and-governance.md), [09](09-policy-language-and-execution.md). **Relevant approved ADRs:** [ADR-0014](16-architecture-decision-register.md#adr-0014), [ADR-0015](16-architecture-decision-register.md#adr-0015), [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017).
- **Acceptance evidence:** Vary/authorization/cross-tenant cache tests pass.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0119 — Generic HTTP Caching Policies** for Iskender Gateway. Use the authoritative project inputs @docs/08-routing-resilience-and-governance.md @docs/09-policy-language-and-execution.md and the approved ADRs ADR-0014, ADR-0015, ADR-0016, ADR-0017; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Provide standards-aware HTTP response caching where headers/security/tenant isolation permit**. In-scope behaviors and artifacts: **Cache-Control; Vary; ETag; invalidation; auth-aware keys**. Explicitly exclude: **No public leakage of authenticated body across tenants**. Prerequisites: **ISK-0116, ISK-0108**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Vary/authorization/cross-tenant cache tests pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0120 — API Specifications & Import/Export

- **Scope and intent:** Support OpenAPI and compatible API metadata import/export with validated path/operation mapping.
- **Included:** Spec validation; preview/diff; route mapping; ownership.
- **Excluded/non-goals:** No blind overwriting active tenant routes or universal SOAP/GraphQL mapping.
- **Milestone:** P4 — General-purpose HTTP API gateway (post-MVP).
- **Dependencies:** ISK-0113, ISK-0114.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [09](09-policy-language-and-execution.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0016](16-architecture-decision-register.md#adr-0016), [ADR-0017](16-architecture-decision-register.md#adr-0017).
- **Acceptance evidence:** Import conflict produces safe diff and preserves active routes.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0120 — API Specifications & Import/Export** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/09-policy-language-and-execution.md and the approved ADRs ADR-0003, ADR-0005, ADR-0016, ADR-0017; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Support OpenAPI and compatible API metadata import/export with validated path/operation mapping**. In-scope behaviors and artifacts: **Spec validation; preview/diff; route mapping; ownership**. Explicitly exclude: **No blind overwriting active tenant routes or universal SOAP/GraphQL mapping**. Prerequisites: **ISK-0113, ISK-0114**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Import conflict produces safe diff and preserves active routes**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0121 — Developer Portal & Consumer Onboarding

- **Scope and intent:** Add optional self-hostable API catalog/docs and key/subscription onboarding UX.
- **Included:** Catalog display; scoped access; sample calls; rate docs.
- **Excluded/non-goals:** No required hosted portal for core operation.
- **Milestone:** P4 — General-purpose HTTP API gateway (post-MVP).
- **Dependencies:** ISK-0118, ISK-0120, ISK-0081.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md), [05](05-identity-security-and-privacy.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0002](16-architecture-decision-register.md#adr-0002), [ADR-0004](16-architecture-decision-register.md#adr-0004).
- **Acceptance evidence:** Consumer portal cannot reveal foreign project docs/keys.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0121 — Developer Portal & Consumer Onboarding** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Add optional self-hostable API catalog/docs and key/subscription onboarding UX**. In-scope behaviors and artifacts: **Catalog display; scoped access; sample calls; rate docs**. Explicitly exclude: **No required hosted portal for core operation**. Prerequisites: **ISK-0118, ISK-0120, ISK-0081**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Consumer portal cannot reveal foreign project docs/keys**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0122 — MCP Gateway & Tool Surface Integration

- **Scope and intent:** Expose selected registered APIs/tools via Model Context Protocol with explicit authentication and tool contracts.
- **Included:** MCP tool discovery; auth; lifecycle; protocol version compatibility.
- **Excluded/non-goals:** No unbounded autonomous agent execution or unsafe tool exposure.
- **Milestone:** P4 — General-purpose HTTP API gateway (post-MVP).
- **Dependencies:** ISK-0120, ISK-0117, ISK-0052.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [05](05-identity-security-and-privacy.md), [09](09-policy-language-and-execution.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0008](16-architecture-decision-register.md#adr-0008), [ADR-0009](16-architecture-decision-register.md#adr-0009).
- **Acceptance evidence:** Tool ACL and session/capability tests pass with updated MCP conformance fixture.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0122 — MCP Gateway & Tool Surface Integration** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/05-identity-security-and-privacy.md @docs/09-policy-language-and-execution.md and the approved ADRs ADR-0003, ADR-0005, ADR-0008, ADR-0009; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Expose selected registered APIs/tools via Model Context Protocol with explicit authentication and tool contracts**. In-scope behaviors and artifacts: **MCP tool discovery; auth; lifecycle; protocol version compatibility**. Explicitly exclude: **No unbounded autonomous agent execution or unsafe tool exposure**. Prerequisites: **ISK-0120, ISK-0117, ISK-0052**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Tool ACL and session/capability tests pass with updated MCP conformance fixture**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0123 — General Gateway Operations & GitOps Expansion

- **Scope and intent:** Extend declarative management, diagnostics, publish/rollback and metrics to HTTP resource family.
- **Included:** Config migration; import review; proxy telemetry; staged rollouts.
- **Excluded/non-goals:** No separate shadow control plane or bypass of plugin approvals.
- **Milestone:** P4 — General-purpose HTTP API gateway (post-MVP).
- **Dependencies:** ISK-0115, ISK-0116, ISK-0118, ISK-0121, ISK-0122.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [03](03-domain-and-technical-architecture.md), [12](12-observability-and-nonfunctional-requirements.md), [13](13-development-standards-and-agent-workflows.md). **Relevant approved ADRs:** [ADR-0003](16-architecture-decision-register.md#adr-0003), [ADR-0005](16-architecture-decision-register.md#adr-0005), [ADR-0023](16-architecture-decision-register.md#adr-0023), [ADR-0025](16-architecture-decision-register.md#adr-0025).
- **Acceptance evidence:** HTTP and AI resources share one managed versioned config history.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0123 — General Gateway Operations & GitOps Expansion** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/12-observability-and-nonfunctional-requirements.md @docs/13-development-standards-and-agent-workflows.md and the approved ADRs ADR-0003, ADR-0005, ADR-0023, ADR-0025; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Extend declarative management, diagnostics, publish/rollback and metrics to HTTP resource family**. In-scope behaviors and artifacts: **Config migration; import review; proxy telemetry; staged rollouts**. Explicitly exclude: **No separate shadow control plane or bypass of plugin approvals**. Prerequisites: **ISK-0115, ISK-0116, ISK-0118, ISK-0121, ISK-0122**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **HTTP and AI resources share one managed versioned config history**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```

## ISK-0124 — General-purpose API Gateway Release Gate

- **Scope and intent:** Validate non-AI HTTP behavior, product/portal/MCP operations and regressions under security/load.
- **Included:** Generic HTTP conformance; migration; ops/docs; release evidence.
- **Excluded/non-goals:** No claim full parity with Azure APIM/Kong or unimplemented advanced policies.
- **Milestone:** P4 — General-purpose HTTP API gateway (post-MVP).
- **Dependencies:** ISK-0113, ISK-0114, ISK-0115, ISK-0116, ISK-0117, ISK-0118, ISK-0119, ISK-0120, ISK-0121, ISK-0122, ISK-0123.
- **Workflow status:** `planned` (no implementation evidence).
- **Planning revision:** `1.0`.
- **Final merge commit (short hash):** `null` (unset; fill only after actual merged PR).
- **Authoritative documents:** [01](01-product-charter-and-vision.md), [02](02-product-requirements-and-roadmap.md), [12](12-observability-and-nonfunctional-requirements.md). **Relevant approved ADRs:** [ADR-0001](16-architecture-decision-register.md#adr-0001), [ADR-0029](16-architecture-decision-register.md#adr-0029), [ADR-0023](16-architecture-decision-register.md#adr-0023).
- **Acceptance evidence:** P4 release preserves all earlier AI gateway and plugin security contracts.

**Starting `/speckit.specify` prompt (literal authoritative input):**

```text
/speckit.specify
Create the specification **ISK-0124 — General-purpose API Gateway Release Gate** for Iskender Gateway. Use the authoritative project inputs @docs/01-product-charter-and-vision.md @docs/02-product-requirements-and-roadmap.md @docs/12-observability-and-nonfunctional-requirements.md and the approved ADRs ADR-0001, ADR-0029, ADR-0023; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Validate non-AI HTTP behavior, product/portal/MCP operations and regressions under security/load**. In-scope behaviors and artifacts: **Generic HTTP conformance; migration; ops/docs; release evidence**. Explicitly exclude: **No claim full parity with Azure APIM/Kong or unimplemented advanced policies**. Prerequisites: **ISK-0113, ISK-0114, ISK-0115, ISK-0116, ISK-0117, ISK-0118, ISK-0119, ISK-0120, ISK-0121, ISK-0122, ISK-0123**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **P4 release preserves all earlier AI gateway and plugin security contracts**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify.
```
