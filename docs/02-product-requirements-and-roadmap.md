# 02 — Product requirements and roadmap

**Authority:** MVP product requirements and feature acceptance. **Detailed technical contracts:** [03–12](03-domain-and-technical-architecture.md). **Spec IDs, dependencies, prompts and exact ship boundary:** [17](17-master-spec-inventory.md).

## Scope and definitions

**MVP** is the first broadly credible self-hostable AI gateway release; engineering milestones preceding it are not public release gates. All mandatory capabilities below must have evidence, not just checked-off tasks. A capability is considered compatible only for a provider/mode explicitly present in the published matrix. A **logical endpoint** exposes an ingress contract; a **model alias** names the consumer-facing model; a **deployment** identifies an independently configured provider target; a **backend pool** governs candidate deployments. These nouns are defined authoritatively in [03](03-domain-and-technical-architecture.md).

## Functional requirements

| ID | Priority | Required MVP behavior | Verification / acceptance |
|---|---|---|---|
| PR-001 | Must | Standard container and composable NuGet/`AddIskender(...)` hosts | Same data-plane conformance suite runs in both hosts |
| PR-002 | Must | Independent control/data plane deployments, coherent configuration publication | Drain/restart/config-version acceptance tests |
| PR-003 | Must | Organization → Project → Resource ownership, scoped RBAC and tenant isolation | Allow/deny matrix and adversarial cross-tenant tests |
| PR-004 | Must | Control plane local ASP.NET Core Identity plus external OIDC login | Login, account/admin boundaries, session and failure tests |
| PR-005 | Must | Data plane API keys and JWT/OAuth bearer with independently configurable key issuance/validation | Issuance/rotation/revocation and bearer enforcement tests |
| PR-006 | Must | Credential profiles and supported workload identities for upstream inference | Credential leak tests and provider-specific integration fixtures |
| PR-007 | Must | Providers: OpenAI, Azure OpenAI / Azure AI Foundry, Anthropic, Gemini, generic OpenAI-compatible | Reproducible provider-adapter contracts and capability matrix |
| PR-008 | Must | Chat Completions, Responses, Conversations, Embeddings; streaming, tools, structured outputs where semantically supported | Matrix of positive, negative, streaming and edge-case cases |
| PR-009 | Must | Stateless, provider-managed, and **durable gateway-managed** conversation state | Restart, continuity, tool-history and cross-provider tests |
| PR-010 | Must | Logical aliases/endpoints, multiple backends per endpoint, algorithms and health/circuit-breaker isolation | Routing distribution and failure simulation |
| PR-011 | Must | Round-robin, weighted, priority and latency-aware policies; bounded retry/failover | Deterministic algorithm/stream-safety tests |
| PR-012 | Must | Distributed rate/token limits, quotas, concurrency and spend enforcement | Replica-contention, reservation/reconciliation and refusal tests |
| PR-013 | Must | Typed policy engine, CEL applicability, inheritance/guardrails, immutable plans and atomic activation | Compiler diagnostics, priority/inheritance and swap tests |
| PR-014 | Must | First-party authentication/authorization, traffic, governance, routing, modification and telemetry policies | Catalog-level tests, safety limits and scope checks |
| PR-015 | Must | Trusted policy plugins: SDK, template, pack-time manifest/hashes/schema, CLI, approval lockfile, read-only mounts/startup NuGet | Tampering, allowlist, boot failure and packaging demo |
| PR-016 | Must | OTel tracing/metrics/logs, policy telemetry and safe usage/accounting | Correlation, secret scrubbing and cardinality tests |
| PR-017 | Must | Control-plane management APIs and practical Next.js/shadcn/ui administration | Operator flows for every release-critical resource, access check |
| PR-018 | Must | Versioned configuration, inspectable effective policies, audit and diagnoses | Reproduce/apply/reject/rollback and audit trail cases |
| PR-019 | Must | Cloud-neutral adapters and Azure Container Apps/Postgres/Redis reference deployment; Compose/local | Provision/test and capability-mismatch behavior |
| PR-020 | Must | Reproducible CI, test matrix, packaging, licenses, docs, security and load gates | Release checklist and artifacts reproducible from protected main |

### Important distinctions

- **Mandatory built-in security** (tenant isolation, authenticated management, safe secrets, basic data-plane credential validation, non-bypassable authorization) is not optional just because policies can add stronger constraints.
- **Policies** implement additional scoped constraints and operational controls; an operator cannot remove mandatory higher-level guardrails with a lower scope.
- **Streaming and state** must avoid duplicate committed tool events or text, unsafe retries after visible output, or retention of uncommitted partial assistant turns as success.
- **Distributed features** must require a proven coordination backend; no silent fallback from exact enforcement to approximate local counters on replicas.
- **Cost/budget governance** distinguishes estimated reservations from finalized invoice-equivalent usage; approximation and uncertainty are visible.

## Primary use cases

1. A platform operator configures an organization/project, upstream provider credential profiles, model alias and two separately configured backend deployments, then applies routing and a spend guardrail; a client calls the logical endpoint using a scoped API key or JWT.
2. An application calls Responses, stores the gateway conversation ID, completes a tool exchange, restarts the gateway replicas and continues the conversation on a different supported provider with documented portability gaps.
3. A security operator disables API-key issuance and acceptance for an OIDC-only project, checks delegated JWT claims and verifies no legacy key can authenticate.
4. A plugin author runs the official template, writes a typed handler, packs the NuGet artifact, generates manifest and config schema, runs CLI validation, and gives a bundle to an operator. The operator approves a locked digest; replicas verify and load it without a custom image.
5. An SRE traces a request through policy decisions, backend selection, retry attempt and streaming completion; diagnostics disclose failure class, not secret values or private prompt bodies.

## Engineering milestones and release gates

The authoritative IDs are in [17](17-master-spec-inventory.md). No calendar commitments.

| Milestone | Scope | Completion evidence |
|---|---|---|
| E0 — Agentic foundation | ISK-0000 and base engineering contracts | Protected branches and Spec Kit/bootstrap workflows exercised |
| E1 — Platform and identity | Repository modules, persistence, tenancy, security primitives | Negative tenant isolation and core management flows pass |
| E2 — Provider/inference surface | Core providers, compatibility interfaces and streams | Matrix and contract fixtures pass |
| E3 — Stateful inference | All three state modes, durable history, cross-provider basic continuation | Crash/restart/tool/stream continuation tests pass |
| E4 — Governance and extensibility | Routing, limits, policy engine/catalog, NuGet plugins and approvals | Multi-replica enforcement, failure and tamper tests pass |
| E5 — Operations and MVP release | Administration, telemetry, hardening, release validation | All below release gates green; ship after **ISK-0086** |
| P1 — Conversation intelligence | Smart compaction, branching, richer lifecycle features | Separate post-MVP compatibility review |
| P2 — Multimodal and richer inference | Audio/image/video and richer structured modalities | Capability-gated provider conformance |
| P3 — Provider and deployment expansion | Extra providers, advanced deployment and optimization | Reliability and security review |
| P4 — General HTTP API gateway | Neutral HTTP/API gateway features | Existing AI surface nonregression |

## Explicit MVP release gate

**MVP ships only after ISK-0086 completes and all release criteria pass.** Earlier milestones are internal. Gate ownership is defined in [17](17-master-spec-inventory.md); objective verification methods in [12](12-observability-and-nonfunctional-requirements.md). In addition to PR-001–020, acceptance requires: independently reviewed privilege isolation; durable state recovery including abnormal termination and stream cancellation; reconciliation of budget/token reservations; absence of secret/payload leaks by default; deterministic policy activation; verified policy bundle tamper rejection; non-zero pass counts for every advertised provider/feature; at least two-replica load/failure tests; published performance methodology and *measured*, not invented, outcomes; tested upgrades and rollback; licensing/provenance; operator and SDK documentation.

## Explicit exclusions

Not required for MVP: arbitrary general HTTP API publishing; out-of-process untrusted plugin sandbox; agent orchestration; semantic response cache; advanced compaction/summarization/branching; full multimodal support; guaranteed preservation of proprietary hidden reasoning state across providers; payment/billing product; mandatory hosted control plane; OpenIddict as a built-in authorization server. Post-MVP speculation is not acceptance authority.

## Change control

Approved requirements are not silently moved out of the release gate. Proposed cuts, expanded scopes, or new milestones must be passed through Roadmap Steward and explicit human approval [13](13-development-standards-and-agent-workflows.md). Technical recommendations may change within an approved spec if equivalent behavior is maintained and recorded in the ADR register [16](16-architecture-decision-register.md).
