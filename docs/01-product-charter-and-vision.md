# 01 — Product charter and vision

**Product:** Iskender Gateway · **license:** MIT · **decision basis:** approved discussions through 2026-11-05. **Authority:** product mission, audiences, principles, and scope framing; release-specific commitments are authoritative in [02](02-product-requirements-and-roadmap.md) and [17](17-master-spec-inventory.md).

## Mission and problem

Provide an operationally credible, portable **AI inference gateway** that organizations can deploy as a standard container or embed directly into an ASP.NET Core application. Give platform engineers one governed entry point for heterogeneous AI providers without surrendering request control, observability, provider choice, identity boundaries, or conversation ownership. Existing choices often force a trade-off between cloud-managed coupling, broad control with considerable operational complexity, and high-performance but thin inference proxies.

Iskender is a **gateway platform**, not an inference model host, agent framework, prompt IDE, or standalone identity provider. Its initial specialization is AI inference; its foundational HTTP request pipeline, identity, policies, routing, endpoints, telemetry, and extensibility must be fit for a later general-purpose API gateway.

## Users and jobs to be done

| Persona | Primary job | MVP value proposition |
|---|---|---|
| Enterprise platform engineering (primary) | Offer governed inference across teams and environments | Tenant/project boundaries, backend policy control, cost/limits, deployment portability, auditability |
| Security and operations | Enforce access controls and investigate failures | Local/OIDC administration, API key/JWT ingress, safe secrets, policy diagnostics, OTel, approvals |
| Application teams | Consume stable inference endpoints | OpenAI-compatible surfaces, model aliases, streaming, structured output, conversation continuation |
| Independent developer and .NET integrator (secondary) | Self-host or compose a gateway in an existing host | Container distribution, NuGet packages, `AddIskender(...)`, local development |
| Policy-plugin author | Extend governance safely and repeatably | Typed SDK, NuGet delivery, manifest/schema tooling, operator approval lockfiles |

## Differentiation

1. **Composable .NET-native hosting:** same core abstractions for image/container and embedded hosting; separately deployable control and data planes.
2. **Portable conversation ownership in the MVP:** three explicit state modes, including durable gateway-managed conversation history with basic cross-provider continuation; no misleading promise of universal native-provider equivalence.
3. **A first-class typed policy platform:** CEL for applicability, strongly typed .NET handlers for effects, deterministic immutable plans, and hierarchical guardrails; built-in and custom policies share contracts.
4. **Trust-aware runtime extensibility without rebuilding a container:** operator-approved, integrity-pinned bundles from read-only mounts or startup NuGet downloads; in-process code is explicitly trusted.
5. **Enterprise governance without provider lock-in:** scoped access, routing, quotas, spend policy, secret profiles, and OTel-compatible operational controls.
6. **Measured performance and portable primitives:** .NET first, modular/hot-path isolation to allow selected native/Rust implementation later without requiring a platform rewrite.

## Product principles

- **Correctness before apparent compatibility.** Reject unsupported provider capabilities with explicit, diagnosable errors rather than silent semantic changes.
- **Tenant isolation as a hard boundary.** Every owned resource, conversation, key, deployment, usage record, and policy context is scoped and authorized.
- **Safe by default.** Deny unsafe configurations, default payload logging off, redact secrets, prohibit unapproved plugin loading.
- **Operationally inspectable.** Explain routing/policy decisions without exposing credentials or sensitive content.
- **Cloud primitives, not proprietary coupling.** PostgreSQL is the durable baseline; Redis is an acceleration/distributed-coordination option with capability checks; clouds provide replaceable hosting and identity adapters.
- **Stable external contracts.** Version public APIs, SDKs, and on-disk formats; support migrations and explicit breaking-change discipline.
- **Documented, bounded agentic engineering.** Protected main, tests, evidence-backed merges, approved roadmap decisions; see [13](13-development-standards-and-agent-workflows.md).

## MVP and long-term vision

**MVP:** high-quality AI gateway with approved providers, OpenAI-compatible Chat Completions/Responses/Conversations/Embeddings, streaming, structured output and tools (where supported), durable state, governance, policy+plugin platform, administration, telemetry, benchmarked multi-replica reference deployment. The scope is intentionally focused on reliably integrating inference, **not** building an everything gateway.

**Post-MVP:** intelligent conversation/context management; multimodal expansion; extra providers and deployment modes; model lifecycle and richer inference features; generalized HTTP routes, transformation, caching, and API product/dev-portal capabilities. The exact release sequence lives in [17](17-master-spec-inventory.md), not in this charter.

## Outcomes and measurement

| Outcome | Measurement approach (release targets proposed in [12]) |
|---|---|
| Adoption | Successful fresh-install runbook on container, Compose, and embedded ASP.NET Core hosts |
| Correctness | Provider compatibility/conformance matrices with transparent gaps and reproducible cases |
| Governance | Cross-tenant negative tests, quota/budget denial tests, audit completeness and reconciliation |
| Reliability | Failover, restart, controlled degradation, stream interruption, and recovery tests |
| Performance | Comparative fixed-hardware throughput, p50/p95/p99 added gateway latency, resource curves |
| Developer experience | Template-to-pack-to-approve-to-load demonstration without compiling a custom gateway image |
| Operations | Trace-to-request/policy/backend correlation and documented incident diagnostics |

## Scope exclusions and commercialization

The MIT-licensed core is fully open, and no paid-only core, open-core split, hosted service, support contract, or commercial licensing scheme is assumed. Optional future support, managed hosting, integration services, or tooling may be evaluated independently without weakening the approved licensing baseline. No promise of arbitrary untrusted .NET execution or compatibility with every proprietary provider feature is made.

## Decision taxonomy

- **Approved:** mission, audience priority, license, .NET-first implementation, container/embedded distribution, all three MVP conversation modes, policy and plugin approach, cloud portability.
- **Recommended, not yet approved:** particular objective thresholds, implementation packages behind interfaces, UI information architecture, managed hosting/business model.
- **Needs validation:** exact price/performance benchmarks, operational costs, cross-provider fidelity per feature.
- **Post-MVP:** advanced context optimization and general API gateway features.
- **Unresolved approval items:** documented in [16](16-architecture-decision-register.md) rather than implicitly decided here.
