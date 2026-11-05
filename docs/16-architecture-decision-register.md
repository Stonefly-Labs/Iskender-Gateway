# 16 — Architecture decision register

**Authority:** decision status/traceability. This register reflects recoverable approved user decisions through 2026-11-05; new implementation choices should be added through ADR workflow. Exact original 109-spec title list was not recoverable verbatim, so new spec numbering in [17](17-master-spec-inventory.md) is a proposed reconciliation, not historical evidence.

## Decision status rules

`Approved`: explicitly settled design, superseded only by later approved ADR. `Proposed`: recommendation awaiting decision, not binding architectural choice. `Superseded`: preserve original text/ID and reference successor. `Rejected`: record rationale and prevent accidental resurrection. ADR identifiers are never reused. Any change crossing a product scope/security/release boundary requires human approval and roadmap impact analysis. Implementation choices that merely satisfy approved contracts may be recorded as proposed technical ADRs and reviewed with PR owners.

### ADR-0001

**AI-first MIT gateway with later general HTTP evolution**

- **Status:** Approved.
- **Context:** Platform teams need governed AI traffic and cloud choice.
- **Decision:** Open-source Iskender core MIT; general HTTP API surface follows AI MVP.
- **Rationale:** Licensing and an AI-first entry point reduce adoption friction while keeping the neutral pipeline available for later HTTP expansion.
- **Consequences / trade-offs:** Keeps MVP scope credible; forbids hidden open-core assumptions.
- **References:** [01](01-product-charter-and-vision.md), [02](02-product-requirements-and-roadmap.md).

### ADR-0002

**.NET 10/ASP.NET Core primary runtime**

- **Status:** Approved.
- **Context:** Composable hosting and productivity with a hot-path optimization path.
- **Decision:** Use .NET/C# for MVP; isolate potential native/Rust hot paths.
- **Rationale:** The .NET ecosystem and ASP.NET hosting model optimize composability; measurements rather than speculative Rust rewrites should dictate native hot-path changes.
- **Consequences / trade-offs:** Native interop must not rewrite domain or safety contracts.
- **References:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md).

### ADR-0003

**Modular monolith and split deployment**

- **Status:** Approved.
- **Context:** Shared domain correctness but different operational scale.
- **Decision:** Explicit modules; independently host control/data planes.
- **Rationale:** A shared modular codebase reduces drift while separate process boundaries permit independent scaling and operator security policies.
- **Consequences / trade-offs:** Version config and IPC/storage contracts; preserve dependency direction.
- **References:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md).

### ADR-0004

**Container and NuGet embedded consumption**

- **Status:** Approved.
- **Context:** Users want both a self-hosted image and AddIskender integration.
- **Decision:** Ship both modes with same pipeline contracts.
- **Rationale:** Embedding requires first-class DI/middleware contracts; containers simplify deployment for operators who do not own application code.
- **Consequences / trade-offs:** Embedded must not require global service ownership.
- **References:** [01](01-product-charter-and-vision.md), [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md).

### ADR-0005

**Organization/project/resource ownership**

- **Status:** Approved.
- **Context:** Teams need scoped governance and isolation.
- **Decision:** Use org→project→resource scopes, RBAC, ownership-bound references.
- **Rationale:** Clear ownership scopes make multi-team governance and adversarial isolation testable at every persistence and authorization boundary.
- **Consequences / trade-offs:** Every query/worker/job/stream requires authorization checks.
- **References:** [03](03-domain-and-technical-architecture.md), [05](05-identity-security-and-privacy.md).

### ADR-0006

**PostgreSQL durability, Redis coordination option**

- **Status:** Approved.
- **Context:** Portability and durable history without cloud coupling.
- **Decision:** PG source of truth; Redis for hot/ephemeral coordination when enabled.
- **Rationale:** Postgres supplies transactional durability; optional Redis speeds distributed hot paths without replacing authoritative records.
- **Consequences / trade-offs:** Strong distributed controls require capable backend; no silent weakening.
- **References:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md), [06](06-conversation-state-architecture.md), [08](08-routing-resilience-and-governance.md).

### ADR-0007

**Azure first reference; portable clouds/on-prem**

- **Status:** Approved.
- **Context:** Initial launch targets ACA/PG/Redis.
- **Decision:** Cloud dependencies behind interfaces with Compose/local path.
- **Rationale:** Cloud-native initial deployment is pragmatic, but interchangeable service interfaces keep self-hosted and other-cloud installations viable.
- **Consequences / trade-offs:** Provider identity adapters separate from domain logic.
- **References:** [04](04-technology-stack-and-deployment.md), [05](05-identity-security-and-privacy.md).

### ADR-0008

**Local Identity plus external OIDC control auth**

- **Status:** Approved.
- **Context:** Both self-hosted teams and enterprise IdPs.
- **Decision:** Support local ASP.NET Core Identity and BYO OIDC.
- **Rationale:** Local identity keeps self-hosting accessible, while OIDC makes enterprise user lifecycle and SSO feasible without operating a new authorization server.
- **Consequences / trade-offs:** Internal principal/role mapping; no automatic cross-IdP trust.
- **References:** [05](05-identity-security-and-privacy.md).

### ADR-0009

**Data-plane API keys and OAuth bearer**

- **Status:** Approved.
- **Context:** Workloads require flexible ingress auth.
- **Decision:** Keys and validated JWT/OAuth; independently disable key issue/acceptance.
- **Rationale:** Key-based clients and federated OAuth workloads have distinct needs, so operators must control issuance and validation independently.
- **Consequences / trade-offs:** Revocation/tenant mapping and per-scope checks remain mandatory.
- **References:** [05](05-identity-security-and-privacy.md).

### ADR-0010

**Scoped upstream credential profiles**

- **Status:** Approved.
- **Context:** Backends/accounts need independent secrets/workload identities.
- **Decision:** Deployment-referenced credentials; Azure, Google, OpenAI federation where supported.
- **Rationale:** Per-deployment credential separation prevents accidental account/tenant token reuse and enables short-lived workload identities where supported.
- **Consequences / trade-offs:** Some providers only accept keys; never expose secrets in client config.
- **References:** [05](05-identity-security-and-privacy.md), [07](07-provider-adapters-and-api-compatibility.md).

### ADR-0011

**Three mandatory conversation modes**

- **Status:** Approved.
- **Context:** User explicitly requires gateway-owned durable state in MVP.
- **Decision:** Stateless, provider-managed, gateway-managed history with PG ownership.
- **Rationale:** Gateway ownership is necessary to survive provider changes and restarts, and it cannot be deferred if durable conversations are part of MVP value.
- **Consequences / trade-offs:** Compaction/branching delayed; replica-local state non-authoritative.
- **References:** [06](06-conversation-state-architecture.md).

### ADR-0012

**Responses and Conversations ingress in MVP**

- **Status:** Approved.
- **Context:** Portable continuation must be an API feature not internal only.
- **Decision:** OpenAI-compatible endpoint set with typed items/tool history/stream safety.
- **Rationale:** Standards-shaped ingress makes continuation useful to application developers while explicit compatibility limits prevent false parity.
- **Consequences / trade-offs:** Provider-specific limitations and native IDs must be explicit.
- **References:** [06](06-conversation-state-architecture.md), [07](07-provider-adapters-and-api-compatibility.md).

### ADR-0013

**Five MVP provider categories**

- **Status:** Approved.
- **Context:** Practical heterogeneous model support.
- **Decision:** OpenAI, Azure OpenAI/Foundry, Anthropic, Gemini, generic compatible.
- **Rationale:** Heterogeneous adapters minimize provider lock-in and capability flags block invalid backend routing before irreversible execution.
- **Consequences / trade-offs:** Publish evidence-backed capability matrix; no fake universal support.
- **References:** [07](07-provider-adapters-and-api-compatibility.md).

### ADR-0014

**Multiple independent backend deployments/pool**

- **Status:** Approved.
- **Context:** Resilience and accounts/regions demand separate failure domains.
- **Decision:** Pool candidates individually own credentials, health, breakers and selection.
- **Rationale:** Separate deployment identities allow real failover, health and cost governance instead of treating a model name as a single endpoint.
- **Consequences / trade-offs:** No single-global model/backend assumption.
- **References:** [03](03-domain-and-technical-architecture.md), [08](08-routing-resilience-and-governance.md).

### ADR-0015

**Distributed governance and financial accounting**

- **Status:** Approved.
- **Context:** Enterprise multi-replica budgets/quotas need correctness.
- **Decision:** Request/token limits, quotas, concurrency, usage and spend reservation/reconciliation.
- **Rationale:** Token and spend reservations must coordinate across replicas and reconcile attempts to avoid bypassing enterprise guardrails.
- **Consequences / trade-offs:** No invoice-exact claim or fail-open hard budget.
- **References:** [08](08-routing-resilience-and-governance.md).

### ADR-0016

**Typed policy + CEL applicability + C# effects**

- **Status:** Approved.
- **Context:** Flexible rules without inventing a procedural language.
- **Decision:** Typed versioned handlers, CEL when, JSON/YAML and fluent forms.
- **Rationale:** CEL gives a reviewable declarative predicate while typed handlers keep side effects testable and strongly versioned.
- **Consequences / trade-offs:** No Rego/substitute DSL; validate before activation.
- **References:** [09](09-policy-language-and-execution.md).

### ADR-0017

**Policy scopes and atomic compiled plans**

- **Status:** Approved.
- **Context:** Inheritance/security changes must be deterministic and safe.
- **Decision:** Org/project/resource, mandatory guardrails, immutable plans, atomic activation.
- **Rationale:** Immutable compiled snapshots avoid partially applied policy changes and permit request-level traceability and safe rollback.
- **Consequences / trade-offs:** Stale/missing mandatory security plan fails closed.
- **References:** [09](09-policy-language-and-execution.md).

### ADR-0018

**Namespaced first-party policy identifiers**

- **Status:** Approved.
- **Context:** Avoid collisions/confusion with Azure APIM policy identifiers.
- **Decision:** Use iskender-domain semantic namespace, owned partner namespaces.
- **Rationale:** Namespaced identifiers protect brand clarity and SDK stability; they also avoid misleading equivalence with Azure APIM XML policy names.
- **Consequences / trade-offs:** Catalog identity/version migration required.
- **References:** [09](09-policy-language-and-execution.md), [10](10-first-party-policy-catalog.md).

### ADR-0019

**First-party parity with SDK handlers**

- **Status:** Approved.
- **Context:** Extensibility should be a core platform capability.
- **Decision:** Built-ins share typed plugin handler contracts where feasible.
- **Rationale:** Using common handler contracts reduces built-in/custom semantic drift while non-optional authentication stays outside discretionary extensions.
- **Consequences / trade-offs:** Mandatory auth stays non-optional platform invariant.
- **References:** [09](09-policy-language-and-execution.md), [10](10-first-party-policy-catalog.md), [11](11-plugin-extensibility-and-sdk.md).

### ADR-0020

**NuGet plugin SDK and template**

- **Status:** Approved.
- **Context:** Third parties should use normal dotnet tooling.
- **Decision:** Public SDK, dotnet new iskender-policy, pack-time manifests/hashes/schemas.
- **Rationale:** NuGet and project templates meet .NET authors where they work and automate error-prone manifest/schema/hash production.
- **Consequences / trade-offs:** Manual manifest valid but must meet exact integrity schema.
- **References:** [11](11-plugin-extensibility-and-sdk.md).

### ADR-0021

**Plugin approval lockfile and safe delivery**

- **Status:** Approved.
- **Context:** No custom container rebuild required.
- **Decision:** Approved read-only bundles or startup NuGet download, digest/dep lockfiles.
- **Rationale:** Digest-pinned, operator-approved artifacts make stock images extensible without permitting opportunistic startup code execution.
- **Consequences / trade-offs:** No unapproved directory sweep; missing required bundle blocks readiness.
- **References:** [11](11-plugin-extensibility-and-sdk.md).

### ADR-0022

**Trusted in-process plugins with restarts**

- **Status:** Approved.
- **Context:** Performance/composition prioritize .NET handlers.
- **Decision:** Load approved trusted assemblies in process; binaries restart/redeploy.
- **Rationale:** In-process handlers achieve low overhead and share DI but must be trusted because .NET assembly load isolation cannot constrain malicious code.
- **Consequences / trade-offs:** AssemblyLoadContext is not a sandbox; out-of-process later.
- **References:** [11](11-plugin-extensibility-and-sdk.md).

### ADR-0023

**OpenTelemetry and safe defaults**

- **Status:** Approved.
- **Context:** Operators need correlatable inference observability.
- **Decision:** OTel auto-instrumentation, safe policy telemetry, payload off by default.
- **Rationale:** OpenTelemetry retains vendor-agnostic operations and avoids making prompt capture an accidental observability default.
- **Consequences / trade-offs:** Control metric cardinality and avoid sensitive logging.
- **References:** [05](05-identity-security-and-privacy.md), [12](12-observability-and-nonfunctional-requirements.md).

### ADR-0024

**Next.js and shadcn/ui management**

- **Status:** Approved.
- **Context:** Practical operational UI separate from hot path.
- **Decision:** Use Next.js/shadcn management application.
- **Rationale:** Admin UI is a thin authorized client, leaving management APIs reusable by automation and keeping JavaScript off the request hot path.
- **Consequences / trade-offs:** Keep core gateway usable via API and embedded host.
- **References:** [04](04-technology-stack-and-deployment.md).

### ADR-0025

**Spec Kit and Claude Code stage discipline**

- **Status:** Approved.
- **Context:** Agent needs repeatable controlled implementation.
- **Decision:** Spec 0000 creates resumable drive-speckit, stage-per-invocation.
- **Rationale:** Resumable stage boundaries avoid lost agent progress and accidental unreviewed transitions across build, converge and merge activities.
- **Consequences / trade-offs:** Implement continues tasks but never auto-enters converge.
- **References:** [13](13-development-standards-and-agent-workflows.md), [14](14-spec-0000-bootstrap-brief.md).

### ADR-0026

**Repair/convergence bounds**

- **Status:** Approved.
- **Context:** Prevent infinite retries and hidden severity downgrades.
- **Decision:** 3/problem,10/run,2 no-progress; passes1-3 fix Low, pass4+ Low accepted.
- **Rationale:** Persisted repair budgets and strict severity rules bound coding agents and prevent restart-based loop or quiet issue suppression.
- **Consequences / trade-offs:** Medium+ blocks; separate implement and converge stages.
- **References:** [13](13-development-standards-and-agent-workflows.md).

### ADR-0027

**Human-approved Roadmap Steward**

- **Status:** Approved.
- **Context:** Implementation may invalidate future assumptions.
- **Decision:** Review after convergence, propose diffs/evidence, await material approval.
- **Rationale:** An independent roadmap review catches implementation drift without permitting an agent to rewrite product commitments on its own.
- **Consequences / trade-offs:** Preserve spec history and commitments.
- **References:** [13](13-development-standards-and-agent-workflows.md), [17](17-master-spec-inventory.md).

### ADR-0028

**Grounding manifest and constitution hierarchy**

- **Status:** Approved.
- **Context:** Reliable discovery across shell paths and bootstrap.
- **Decision:** Use .iskender/grounding.yaml and initial constitution prompt; active constitution wins.
- **Rationale:** A small versioned manifest makes bootstrap and long-term path discovery deterministic, and active constitution precedence prevents prompt-driven resets.
- **Consequences / trade-offs:** Never guess missing paths or overwrite existing constitution.
- **References:** [13](13-development-standards-and-agent-workflows.md), [14](14-spec-0000-bootstrap-brief.md), [15](15-constitution-prompt.md), [17](17-master-spec-inventory.md).

### ADR-0029

**Release after full MVP validation**

- **Status:** Approved.
- **Context:** Broadly credible release requires correctness and operational evidence.
- **Decision:** Exact reconstructed inventory gate after ISK-0086; acceptance requirements in 02/12.
- **Rationale:** A named release spec makes the cumulative quality gate explicit; revised ID is an operational planning choice, not evidence of prior implementation.
- **Consequences / trade-offs:** Gate ID is a planning decomposition, not an earlier verbatim approval.
- **References:** [02](02-product-requirements-and-roadmap.md), [12](12-observability-and-nonfunctional-requirements.md), [17](17-master-spec-inventory.md).

## Recommendations and unresolved approvals (not ADR decisions)

| Candidate decision | Status | Owner/action and evidence required |
|---|---|---|
| Concrete CEL evaluator implementation and CEL limits | Recommended; not approved | Implementation spec benchmarks evaluator cost/security and records selected library/license |
| Exact EF Core / Dapper split, migrations and Redis client | Recommended; not approved | Storage spec tests migration/concurrency behavior and proposes ADR if it affects public contracts |
| Strong-consistency coordinator fallback when Redis is not deployed | Unresolved | Prove semantics in multi-replica cases; either require capable backend or restrict deployment mode |
| Local user MFA minimum and emergency admin recovery | Unresolved | Security owner reviews threat model and operator impact before product release |
| Default retention and encryption key management service/provider | Unresolved | Privacy/operations owner approves documented defaults and local deployment path |
| Numeric SLO, latency/memory/throughput thresholds and RPO/RTO | Unresolved | Performance baseline + reviewer-approved release targets; no invented measurement |
| Hosted/commercial plans, OpenIddict and out-of-process isolation | Post-MVP candidates | Separate product approval if pursued; do not contaminate MVP obligations |
| First-party catalog field names and handler versions | Proposed contract surface | Freeze via implementing specs/ADR before public SDK release |

## ADR submission and supersession procedure

A proposed ADR records decision question, current constraints, 2+ options (where meaningful), evidence/benchmarks, security/compatibility consequences and affected specs. Approval logs approver and date, marks status `Approved`, updates references and test expectations. A superseded ADR retains full historical rationale and points to successor `ADR-xxxx`; successor lists prior ID, migration and impact on deployed configurations. Material roadmap changes also require a Roadmap Steward proposal and explicit approval [13](13-development-standards-and-agent-workflows.md). No ADR records are considered implementation-complete solely because they appear here.
