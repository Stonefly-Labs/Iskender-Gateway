# Iskender Gateway Constitution

This constitution is the authoritative governance document for the Iskender Gateway project.
It supersedes the bootstrap prompt in `docs/15-constitution-prompt.md` and any prior plan.
Each principle is immutable in intent; the rules within are normative and enforced through the
gates listed under "Quality Gates & Review Owners". Amendments require the process in Governance.

## Core Principles

### I. Product Identity & License

Iskender MUST remain an MIT-licensed, fully open-source, .NET 10 / ASP.NET Core **AI-first**
gateway whose primary audience is enterprise platform engineering. It MUST ship with first-class
SDK embedding and container hosting of equal standing. The project MAY later evolve toward a
general HTTP/API gateway, but that evolution MUST NOT compromise the AI-first MVP. No feature,
packaging, or roadmap decision may introduce an **open-core** assumption: no capability required
for the stated MVP value may be withheld from the open-source distribution.

*Rationale:* Licensing and an AI-first entry point reduce adoption friction while preserving the
neutral pipeline for later HTTP generality (ADR-0001, ADR-0002, ADR-0004).

### II. Architecture & Deployment Topology

The system MUST be a modular monolith of explicit domains with versioned contracts and preserved
dependency direction. Control plane and data plane MUST be independently deployable. Both
`AddIskender(...)` embedded hosting and the container image MUST honor identical pipeline
contracts; embedded hosting MUST NOT require global service ownership. Foundational endpoint,
authentication, policy, routing, and telemetry abstractions SHOULD be kept HTTP/API-neutral where
future generality plausibly requires it. PostgreSQL MUST be the durable source of truth; optional
Redis or other distributed coordination MUST be capability-correct and MUST NOT silently weaken
guarantees when absent. Azure ACA/PG/Redis is a cloud-neutral **reference** only — all cloud
dependencies MUST sit behind interfaces with a local/Compose path, never a lock-in.

*Rationale:* Shared modular code reduces drift while separate process boundaries permit
independent scaling and operator security policy (ADR-0003, ADR-0006, ADR-0007).

### III. Mandatory Conversation State

The MVP MUST support three conversation modes: **stateless**, **provider-managed**, and
**gateway-managed durable**. Canonical typed items, tool-call history, OpenAI-compatible
**Responses** and **Conversations** ingress, basic cross-provider continuation, and stream-safe
persistence are non-negotiable and MUST NOT be deferred. Gateway-managed history MUST be owned in
PostgreSQL; replica-local state is non-authoritative. Advanced compaction, branching, and
context optimization are **post-MVP** and MUST NOT be pulled into MVP scope without an approved
ADR and roadmap change.

*Rationale:* Gateway-owned durable state must survive provider changes and restarts and therefore
cannot be deferred if durable conversations are part of MVP value (ADR-0011, ADR-0012).

### IV. Provider Correctness & Honest Capability

Provider support MUST use capability-declared adapters validated against publicly documented
conformance for OpenAI, Azure OpenAI/Foundry, Anthropic, Gemini, and generic compatible backends.
Chat Completions, Responses, Conversations, Embeddings, SSE, tools, and structured output MUST be
offered only where semantically supported by the target provider. The gateway MUST fail
explicitly when a requested feature is unavailable and MUST NOT silently claim universal parity.
A published, evidence-backed capability matrix MUST gate routing before irreversible execution.

*Rationale:* Heterogeneous adapters minimize lock-in, and capability flags block invalid routing
before an irreversible call (ADR-0013).

### V. Security & Privacy

Organization → project → resource isolation MUST be non-bypassable and enforced at every
persistence, query, worker, job, and stream boundary with scoped RBAC. Control-plane auth MUST
support local ASP.NET Core Identity and external OIDC; data-plane auth MUST support API keys and
validated JWT/OAuth bearer, with issuance and acceptance of API keys independently controllable.
Workload identities and upstream credential profiles MUST NOT leak into telemetry or client
configuration. Payload logging MUST be **off by default**; retention, deletion, and audit MUST be
scoped. Mandatory security and hard-budget checks MUST default to least privilege and **fail
closed**.

*Rationale:* Clear ownership scopes make multi-team governance and adversarial isolation testable,
and safe defaults prevent accidental exposure (ADR-0005, ADR-0008, ADR-0009, ADR-0010, ADR-0023).

### VI. Policy & Plugin Architecture

Policies MUST be versioned, typed .NET definitions. CEL MUST be used **only** for applicability
(`when`) predicates; effects MUST be implemented as typed C# handlers. Declarative JSON/YAML and
fluent configuration MUST both be supported. Execution MUST use deterministic stages, scope
inheritance (org/project/resource), mandatory guardrails, compiled **immutable** plans, and
**atomic** activation; a stale or missing mandatory security plan MUST fail closed. Built-in and
custom handlers MUST share contracts, while mandatory authentication remains a non-optional
platform invariant. Trusted in-process NuGet plugins MUST carry manifests, hashes, and schemas,
MUST be lockfile-allowlisted with digest-pinned safe mount or startup download of verified
dependencies, and MUST be producible via the SDK template and CLI. Assembly loading is **NOT** a
sandbox. The project MUST NOT switch to Rego or a custom procedural DSL, and MUST NOT require
custom container images to deliver policy plugins.

*Rationale:* CEL gives a reviewable predicate while typed handlers keep effects testable;
operator-approved digest-pinned artifacts extend stock images without opportunistic code
execution (ADR-0016, ADR-0017, ADR-0018, ADR-0019, ADR-0020, ADR-0021, ADR-0022).

### VII. Resilience & Distributed Governance

Multiple independent backend deployments per alias/pool MUST be treated as foundational; each
candidate owns its own credentials, health, breakers, and selection. The gateway MUST enforce
selection, failover, circuit breakers, distributed request/token limits, quotas, concurrency
limits, usage accounting, and budget reservation/reconciliation, each with explicit failure
semantics. The gateway MUST NOT retry after any streamed output has been emitted, MUST NOT claim
invoice-exact spend, and MUST NOT silently weaken distributed guarantees or fail open on a hard
budget.

*Rationale:* Separate deployment identities enable real failover and cost governance; reservations
must coordinate across replicas to prevent bypassing enterprise guardrails (ADR-0014, ADR-0015).

### VIII. Test & Operational Rigor

Changes MUST carry tests proportionate to risk drawn from: unit, property, integration,
Testcontainers, architecture, provider-conformance, security, stream, chaos, and performance.
Observability MUST use OpenTelemetry with bounded, sensitive-safe metric cardinality.
Benchmarks MUST be measured objectively; achieved performance numbers MUST NOT be invented, and no
SLO/latency/throughput/RPO/RTO figure may be asserted without a reviewer-approved baseline. A
clean build, documented upgrade path, rollback procedure, and incident diagnostics MUST be
preserved.

*Rationale:* Proportionate testing plus honest measurement keeps correctness and operability
verifiable rather than assumed (ADR-0023, ADR-0029).

### IX. Engineering Practice & Supply-Chain Hygiene

All work MUST proceed on `feature/`, `chore/`, or `bugfix/` branches off a protected `main`, via
reviewed PRs with passing CI; direct pushes to `main` are prohibited. Documentation MUST be
maintained concurrently with the code it describes. ADRs MUST be stable with preserved history and
never-reused identifiers, dependency direction MUST be respected, and SDK/API surfaces MUST be
versioned with reproducible builds and supply-chain hygiene (SBOM/provenance where available).
Libraries that are only *recommended* (not approved) in the decision register MUST NOT be recorded
as constitutional facts; they are selected through implementation specs/ADRs.

*Rationale:* Branch protection, concurrent docs, and ADR discipline keep the record truthful and
changes reviewable (ADR-0024 as a scoped example; docs/14 bootstrap brief; register recommendations
table).

### X. Agentic Spec Kit Discipline

The existing Spec Kit installation MUST be preserved (never reinitialized or overwritten). ISK-0000
establishes `drive-speckit` and the Roadmap Steward. Exactly **one** eligible workflow stage runs
per invocation. `implement` MUST work all remaining tasks until completed or blocked and MUST stop
before `converge` (it never auto-enters convergence). Repair budgets — 3 meaningful repairs per
problem, 10 failed repairs per run, 2 no-progress cycles — MUST be persisted across restarts and
never reset by a process restart. Convergence passes 1–3 MUST remediate all severities through
separate `implement` calls; pass 4+ MAY accept **Low-only** findings with written rationale;
**Medium or higher ALWAYS blocks**; severities MUST NOT be silently downgraded. Every stage MUST
emit a human summary and precise, actionable blockers.

*Rationale:* Resumable stage boundaries and persisted budgets prevent lost progress, restart loops,
and quiet issue suppression (ADR-0025, ADR-0026, ADR-0028).

### XI. Human Governance & Anti-Fabrication

The Roadmap Steward MAY propose evidence-backed, diff-only changes but MUST NOT silently alter
material roadmap, spec prompts, scope, milestones, architecture, or this constitution. Such changes
REQUIRE explicit human approval and an auditable revision history. Completed-spec records and the
**actual** Git merge hashes/refs MUST be preserved. Fabricated implementation progress MUST NOT be
accepted or recorded under any circumstance.

*Rationale:* Independent review catches drift without letting an agent rewrite product commitments,
and truthful merge evidence keeps the audit trail trustworthy (ADR-0027, ADR-0028).

### XII. Constitutional Enforcement

Every principle MUST have, and the "Quality Gates & Review Owners" section MUST record: normative
MUST/SHOULD language, a named review owner, and at least one measurable test/merge/release gate.
Conflicts between principles or with a proposed change MUST be escalated to the relevant owner
rather than guessed. Security, privacy, release, and compatibility requirements MUST NOT be waived
informally. Any exception REQUIRES a written request (see Governance) approved by the principle's
review owner and logged; an unapproved exception is a compliance violation that blocks merge.

*Rationale:* Enforcement is only real when ownership, gates, and the exception path are explicit
(ADR-0028; docs/15 constitution prompt §XII).

## Scope, Milestones & Decision Authority

- **Approved vs. recommended.** Only decisions marked `Approved` in
  `docs/16-architecture-decision-register.md` (ADR-0001..ADR-0029) are binding constitutional
  facts. Items in the register's "Recommendations and unresolved approvals" table (e.g., concrete
  CEL evaluator, EF Core/Dapper split, Redis fallback consistency, MFA minimum, retention/key
  management defaults, numeric SLOs, hosted plans) are **not** approved and MUST be settled through
  implementation specs or new ADRs before they bind.
- **Milestone cutline.** The three conversation modes, Responses/Conversations ingress, the five
  provider categories, distributed governance, and the policy/plugin architecture are MVP.
  Advanced compaction/branching/optimization, out-of-process plugin isolation, and hosted/commercial
  plans are **post-MVP** and MUST NOT contaminate MVP obligations.
- **Release gate.** The public MVP (milestone E5, ISK-0080–ISK-0086) MUST ship only after
  **ISK-0086 — MVP Release Acceptance & Documentation** completes, never before (ADR-0029;
  `docs/17` cutline). Acceptance REQUIRES all product requirements PR-001–020 (`docs/02`) and the
  test/benchmark/security/documentation gates (`docs/12`), plus a human-approved release checklist.
  The ISK-0086 identifier is a planning decomposition of the reconstructed 125-spec inventory, not
  evidence of prior implementation.
- **Grounding authority.** Paths are discovered via `.iskender/grounding.yaml`; missing paths or
  prompts MUST block with an actionable error and MUST NOT be guessed or invented (ADR-0028).

## Quality Gates & Review Owners

| Principle | Review Owner | Measurable Gate |
|---|---|---|
| I. Product & License | Product/Charter owner | License scan proves MIT; MVP capability audit shows no open-core withholding |
| II. Architecture & Topology | Architecture (ADR) owner | Architecture tests enforce module boundaries & dependency direction; control/data plane deploy independently; local/Compose path builds without cloud creds |
| III. Mandatory State | Architecture (ADR) owner | Integration + stream-safe persistence tests pass for all three modes; cross-provider continuation test green |
| IV. Provider Correctness | Architecture (ADR) owner | Provider-conformance suite per capability matrix; unavailable features fail explicitly (negative tests) |
| V. Security & Privacy | Security owner (privacy items: Privacy/Operations owner) | Security/isolation tests pass; fail-closed tests for mandatory checks; telemetry redaction tests; payload logging default-off asserted |
| VI. Policy & Plugins | Architecture (ADR) owner | Policy compile/activation atomicity + fail-closed tests; plugin manifest/hash/lockfile verification tests; no Rego/custom-image dependency |
| VII. Resilience & Governance | Architecture (ADR) owner | Chaos/failover + distributed limit/budget reconciliation tests; no-retry-after-stream test; fail-closed hard-budget test |
| VIII. Test & Operational Rigor | Performance owner (+ QA) | Risk-proportionate test matrix present in PR; benchmarks measured, not asserted; clean build + rollback docs present |
| IX. Engineering Practice | Architecture (ADR) owner | Protected-main + required reviews/CI enforced; doc-sync check green; SBOM/provenance where permitted |
| X. Spec Kit Discipline | Spec Kit maintainer | Stage-discipline + repair-budget + convergence-severity tests (3/problem, 10/run, 2 no-progress; Medium+ blocks) |
| XI. Human Governance | Roadmap Steward + human approver | Steward proposals are diff-only and await approval; merge evidence records real hashes; no fabricated progress |
| XII. Enforcement | Each principle's owner | Every merge verifies owner+gate; exceptions logged with approval; unresolved conflicts escalated, not guessed |

## Governance

**Supremacy.** This constitution supersedes other practices and the bootstrap prompt. Where a spec,
plan, ADR, or agent instruction conflicts with it, this document wins until formally amended.

**Amendment procedure.** An amendment REQUIRES: (1) explicit human approval; (2) a version bump and
ISO date; (3) supporting evidence; (4) an impact assessment against active specs, SDK/API surfaces,
and documentation; (5) a migration plan; and (6) an update of affected tests. Material changes to
roadmap, scope, milestones, architecture, or this constitution also REQUIRE a Roadmap Steward
diff-only proposal and recorded approval. The temporary Sync Impact Report at the top of this file
is review scratch material and is expected to be removed before the amended file is committed.

**Versioning policy (semantic).** MAJOR — backward-incompatible governance/principle removal or
redefinition. MINOR — a new principle/section or materially expanded guidance. PATCH —
clarifications, wording, or non-semantic refinements.

**Compliance review.** Every PR and review MUST verify compliance with the applicable principles and
their gates in "Quality Gates & Review Owners". Complexity or deviation MUST be justified in-PR.

**Exception requests.** An exception MUST be requested in writing (PR description or linked ADR),
state the principle, the specific deviation, scope, and expiry, and be approved and logged by that
principle's review owner. Security, privacy, release, and compatibility requirements MUST NOT be
waived informally. Unresolved conflicts MUST be escalated to the owner, never guessed.

**Version**: 1.0.0 | **Ratified**: 2026-11-05 | **Last Amended**: 2026-11-05
**Ratified by**: Christopher House, human approver
