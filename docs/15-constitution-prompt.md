# 15 — Ready-to-use Spec Kit constitution-generation prompt

**Purpose:** bootstrap input for `/speckit.constitution`. **Not the active constitution.** Once created, `.specify/memory/constitution.md` is authoritative. Amendments require explicit user approval and impact analysis against implementations, prompts and grounding references.

## Exact prompt to provide to `/speckit.constitution`

> Generate the **Iskender Gateway** project constitution using `@docs/01-product-charter-and-vision.md` through `@docs/14-spec-0000-bootstrap-brief.md`, `@docs/16-architecture-decision-register.md` and `@docs/17-master-spec-inventory.md`. Discover paths from `.iskender/grounding.yaml`; do not guess missing files. Respect approved decisions; distinguish recommendation from approval; do not contradict the milestone cutline, policy architecture or state-mode commitments.
>
> **I. Product and license.** Iskender is an MIT-licensed, open-source, .NET 10/ASP.NET Core **AI-first gateway**, evolving toward a general HTTP/API gateway later. Enterprise platform engineering is primary; SDK/container flexibility is essential. Do not introduce an open-core assumption.
>
> **II. Architecture and scope.** Maintain a modular monolith of explicit domains and contracts, independently deployable control/data planes and first-class `AddIskender(...)`/container hosting. Keep foundational endpoint, authentication, policy, routing and telemetry abstractions HTTP/API-neutral where future generality requires. PostgreSQL is durable source of truth; optional Redis/distributed coordination must be capability-correct, and Azure ACA/PG/Redis is a cloud-neutral **reference**, not lock-in.
>
> **III. Mandatory state.** MVP supports stateless, provider-managed and **gateway-managed durable** conversation modes. Canonical typed items, tool history, Responses and Conversations compatibility, basic cross-provider continuation and stream-safe persistence are non-negotiable. Advanced compaction/branching/optimization is post-MVP.
>
> **IV. Provider correctness.** Use capability-declared adapters and publicly documented conformance for OpenAI, Azure OpenAI/Foundry, Anthropic, Gemini and generic compatible backends; Chat Completions, Responses, Conversations, Embeddings, SSE, tools and structured output as semantically supported. Fail explicitly for unavailable features; never silently claim universal parity.
>
> **V. Security and privacy.** Non-bypassable organization/project/resource isolation, scoped RBAC, secure control-plane local Identity or external OIDC and data-plane API keys/JWT bearer; independent API-key issuance/acceptance controls. Workload identity and credential profiles never leak to telemetry. Payload logging off by default; scope retention, deletion and audit. Default to least privilege and fail closed for mandatory security/budget checks.
>
> **VI. Policies and plugins.** Versioned typed .NET policy definitions; CEL only for applicability; C# handlers for effects; declarative JSON/YAML and fluent configuration; deterministic stages, inheritance and mandatory guardrails; compiled immutable plans and atomic activation. Built-in and custom handlers share contracts. Trusted in-process NuGet plugins require manifests/hashes/schemas, lockfile allowlisting, template and CLI, safe mount/startup download and verified dependencies. Assembly loading is NOT a sandbox. Never switch to Rego/custom procedural DSL or require custom images for policy plugins.
>
> **VII. Resilience/governance.** Treat multiple independent backend deployments per alias as foundational. Enforce selection, failover, breakers, distributed request/token limits, quotas, concurrency, usage and budget reservation/reconciliation with explicit failure semantics. Do not retry after streamed output or silently weaken distributed guarantees.
>
> **VIII. Test and operational rigor.** Changes require unit/property/integration/Testcontainers/architecture/provider conformance/security/stream/chaos/performance tests proportionate to risk. OTel with bounded sensitive-safe metrics; benchmark objectively, never invent achieved numbers. Preserve clean build, documented upgrades, rollback and incident diagnostics.
>
> **IX. Engineering practice.** Work on `feature/`, `chore/`, `bugfix/` branches off protected `main`, use PR review/CI and mandatory concurrent documentation maintenance. Stable ADRs with history, dependency direction, versioned SDK and APIs, reproducible builds and supply-chain hygiene. Do not select non-approved libraries as constitutional facts.
>
> **X. Agentic Spec Kit discipline.** Preserve the existing Spec Kit installation. ISK-0000 creates `drive-speckit`/Roadmap Steward. One workflow stage per invocation; implement all pending tasks within its invocation then stop before converge; repair budgets 3/problem, 10/run, 2 no-progress persisted. Convergence passes 1–3 remediate all severities through separate implement calls; pass 4+ Low-only may be accepted with rationale; Medium+ blocks; never silently downgrade. Always provide human summary and precise actionable blockers.
>
> **XI. Human governance.** Roadmap Steward can propose evidence-backed diffs, never silently change material roadmap, spec prompts/scope/milestones, architecture or constitution. Require explicit user approval and auditable revision history; preserve completed-spec records and actual merge hashes. Accept no fabricated implementation progress.
>
> **XII. Constitutional enforcement.** For each principle specify normative MUST/SHOULD language, review owner, measurable test/merge/release gate and procedure for requesting an exception. Conflicts must be escalated rather than guessed; security, privacy, release and compatibility requirements cannot be waived informally. Constitution amendments need human approval, version/date, evidence, impact assessment on active specs/SDK/APIs/docs, migration plan and update of affected tests.
>
> Produce a concise but enforceable constitution, with numbered immutable principles and governance/amendment section. Do not implement any application feature during constitution generation. If a source is missing, report the exact failure instead of creating invented architecture or copying an outdated plan.

## After generation

Review the proposed constitution against [16](16-architecture-decision-register.md), approved MVP acceptance [02](02-product-requirements-and-roadmap.md) and inventory [17](17-master-spec-inventory.md). Record generated version and approvals in repository workflow history, update documentation references if approved and make sure `drive-speckit` reads the **active** file thereafter. Retain this prompt as historical bootstrap input; do not treat it as able to overwrite the active constitution automatically.
