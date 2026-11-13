# 03 — Domain and technical architecture

**Authority:** module boundaries, domain vocabulary, invariants, contract topology and execution lifecycle. See [05](05-identity-security-and-privacy.md) for authorization, [09](09-policy-language-and-execution.md) for policy semantics, [06](06-conversation-state-architecture.md) for durable conversations.

## Logical architecture

```text
Applications / SDKs / HTTP clients
                  |
        Data-plane ingress (hostable library or container)
        - ingress adapters + protocol contracts
        - credential validation + tenant/project authorization
        - logical endpoint resolution + immutable policy plan
        - conversation mode + adapters + routing/backends
        - limits, telemetry, streaming, accounting
                  |
         Provider deployments / heterogeneous AI backends

Operators / management SPA / declarative GitOps configuration
                  |
        Control-plane API (independently deployable)
        - org/project/resource configuration, RBAC, secrets metadata
        - validated policy/program compilation + approvals
        - deployment/credential management, audit, diagnostics
                  |
     PostgreSQL source of truth + configuration publication
     Redis for hot state / distributed primitives when enabled
```

A **modular monolith** is the initial code organization, *not* a requirement to host all processes together. Module boundaries are enforced by distinct projects/namespaces and contracts; control plane, data plane and optional background processing are independently executable deployments sharing approved persistence and message/config coordination contracts.

## Resource hierarchy and nouns

- **Organization:** root tenant boundary, security/governance owner and billing/usage aggregation scope.
- **Project:** bounded workspace beneath one organization; holds delegated roles, credentials, logical endpoints and deployment resources. No implicit cross-project access.
- **Resource:** typed, ID-addressable, versioned asset with an immutable owner scope, e.g. endpoint, model alias, backend pool, deployment, credential profile, API key, policy definition, policy assignment, budget, conversation, audit entry. Some system-level configuration belongs to a separate operator scope and must never impersonate a tenant.
- **Logical endpoint:** stable URL+protocol contract and authentication attachment. An endpoint is a consumer-facing ingress resource and may resolve a model alias.
- **Model alias:** consumer-facing model identifier mapping to an eligible backend pool and capability constraints.
- **Backend pool:** selection group of multiple **deployment** resources, each with independent provider, upstream identity, endpoint, model, health, circuit breaker, weight, priority and cost metadata.
- **Deployment:** one concrete provider/model/region/account connection with a versioned configuration and credential profile reference.
- **Policy definition:** versioned typed handler kind + settings; **policy assignment:** immutable binding to org/project/resource scope, stage, condition, ordering and override rules. Effective plans are compiled and atomic.
- **Credential profile:** restricted, versioned reference to a secret or workload identity configuration, never client-visible plaintext.
- **Conversation:** tenant-owned sequence of canonical typed items, with explicit selected state mode and revision, distinct from provider-native identifiers.

Resource IDs are opaque; authorization requires the **owner scope from persistent metadata**, not a caller-provided organization header. Natural names are unique within the declared owner boundary only. Every foreign-key-like reference must validate that sharing is allowed by ownership/role and intended scope, not merely that a target ID exists.

ISK-0002 set the ID format, the registered [resource types](../specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md#resource-types), and a scoped reference that only an approved ownership resolver can create, in its [public contracts and versioning contract](../specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md).

## Module boundaries

The assembly and namespace that each module lives in are in the [module map](../specs/0001-solution-dependency-boundaries-build-conventions/contracts/module-dependency-rules.md#module-map).

| Module | Owns | May depend on | Must not do |
|---|---|---|---|
| Hosting/Ingress | host adapters, pipeline bootstrap, protocol dispatch | contracts, runtime composition | assume standalone container or ASP.NET global static state |
| Identity/Authorization | principals, credentials, role checks, claims | identity contracts, persistence | delegate tenant boundaries solely to optional policies |
| Registry/Configuration | org/projects/resources, validation, versioning | domain contracts, persistence | directly mutate activated data-plane snapshots |
| Policy Engine | typed definitions, CEL evaluation, effective plan compilation | handler SDK, context contracts | execute arbitrary config-supplied C# source |
| Routing/Resilience | pools, health, breaker, attempts, constraints | provider capability, coordination | assume one deployment per model alias |
| Providers | adapters, capabilities, translation, provider auth | provider SDK contracts, secret handles | own tenant/RBAC policy or durable conversations |
| Conversations | typed items, state revisions, retention, continuation | provider capability, persistence | trust provider IDs as tenant ownership |
| Governance | request/token/concurrency quotas, budgets, reservations | coordination, usage accounting | claim strong distributed limits without coordinating backend |
| Telemetry/Audit | instrumentation, correlation, sensitive-data filters | OTel contracts, append-only event contract | emit prompts or credentials by default |
| Management | APIs, admin operations, views | authorized services | bypass domain validation with raw storage updates |
| Plugins | approval, assembly resolution, SDK registry | policy contracts, integrity service | scan arbitrary folders or treat .NET load contexts as sandboxes |
| Infrastructure | Postgres, Redis, secret/identity providers, transport | module interfaces | leak cloud-specific classes into domain/public SDK |

Use a request-local scoped **GatewayExecutionContext** carrying caller principal, resolved tenant/project, endpoint, immutable configuration revision, deadline, correlation IDs, routing attempt ledger, budget reservations and policy execution context. Mutable per-request state is not shared across requests; extension APIs expose capability-bounded views, not unrestricted storage or secret access.

ISK-0002 publishes `GatewayExecutionContext` and its views in `Iskender.Core.Contracts`. Its [request context](../specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md#request-context) section names the spec that owns each view.

## Request/response execution pipeline

1. Accept request, establish correlation, validate protocol/path/body size/content type and cancellation. Start root trace.
2. Authenticate ingress using configured enabled mechanism(s). Independently enforce non-optional tenant/project/resource ownership and authorization. Resolve logical endpoint and model alias.
3. Pin an immutable published config version and effective policy plan for the lifetime of this request. Execute deterministic pre-admission/policy stages; reject safely before expensive upstream calls.
4. Validate requested inference capabilities against available deployments, choose conversation mode, load/prepare durable context when applicable; calculate candidate token/budget reservations.
5. Apply concurrency/rate/token/budget enforcement using capability-appropriate distributed coordination. Evaluate backend selection/health/breaker, failover eligibility and pre-upstream policies.
6. Select deployment, acquire credential handle, run provider adapter; apply request transformation only within valid provider/API semantics. Retry **only** when safe and bounded; track each attempt separately.
7. Stream or buffer provider responses with backpressure, cancellation and timeouts. Run response/policy/telemetry stages without silently corrupting token/event ordering.
8. Persist committed canonical conversation items and authoritative usage/reconciliation; release unused reservations, update health and audit decisions. Finalize trace and safe client error mapping.

### Stream invariants

- Headers or visible output are a retry boundary; never replay already visible output to another backend without explicit protocol-defined recovery semantics.
- Commit only well-defined completed conversation entries; interrupted streams retain separate failure/partial-state markers with a clear policy for whether they are resumable.
- Request cancellation propagates to provider, storage and reservations; mandatory settlement cannot be skipped if the client disconnects.
- Fail-closed controls (authorization, hard budget, required plugin plan) cannot degrade to allow-all on outage.

## Policy inheritance and ownership

Assignments from organization, project and resource levels are compiled by stage and deterministic ordering; narrower scopes may supplement/override only where the parent policy explicitly allows it. Security minima and mandatory guardrails cannot be removed by descendants. Effective-plan diagnostics show definition/version, provenance and merge result while filtering sensitive settings. Full semantics: [09](09-policy-language-and-execution.md).

## Persistence, publication and distributed coordination

PostgreSQL is source of truth for resource metadata, approvals, versioning, identity persistence, usage ledger and durable gateway-managed state. Data planes use immutable validated snapshots with a version marker; publish/activate atomically and preserve last-known-good config only where security constraints permit. Backends cannot assume shared in-process memory. Redis can implement hot caches, rate/token coordination and breaker counters with vetted atomicity and outage policies; if absent, features requiring distributed exactness must use an equivalently correct alternative or reject unsupported deployment topology. Migration and service contracts are cloud-neutral.

ISK-0003 sets how configuration is declared, read, merged, validated, and named by a content-derived revision for each validated snapshot, in its [configuration contract](../specs/0003-configuration-schema-validation-foundation/contracts/configuration.md). ISK-0057 owns publication and activation.

ISK-0004 sets the persistence ports, units of work, tenant isolation with row-level security, schema migrations, and the store health probe in its [persistence contract](../specs/0005-coordination-redis-capability-contracts/contracts/persistence.md), now at version 2, which ISK-0005 revised to add the scope-keyed tables and the `isk_coordination` schema.

ISK-0005 sets the coordination ports (atomic operations, leases with fencing tokens, the fencing guard, and the versioned cache), the consistency level each coordinator declares, the capability check that refuses a feature whose deployment cannot meet its minimums, single-instance verification, and the coordination health probe in its [coordination contract](../specs/0005-coordination-redis-capability-contracts/contracts/coordination.md). A local substitute is never reported as distributed, and only the PostgreSQL coordinator offers fencing.

## Interfaces, isolation and portability

Version management APIs separately from OpenAI-compatible ingress and the public policy SDK. Define ports for `IProviderAdapter`, `IBackendSelector`, `IConversationStore`, `IQuotaCoordinator`, `IUsageLedger`, `ICredentialResolver`, `IPolicyHandler`, `IPolicyPlanPublisher`, `IPluginTrustStore`; these are **illustrative interface names**, not locked public signatures. Capabilities, error classes, idempotency and cancellation are explicit. Allow future Rust/native hot-path bridges only behind stable marshaling boundaries; never bake in a requirement for Rust or compromise core security invariants.

The error categories and codes, the versioning policy for the public contract packages, and the compatibility harness that enforces it are in the [public contracts and versioning contract](../specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md), first published by ISK-0002.

## Architectural tests and non-goals

Enforce package dependency direction in architecture tests; forbid cross-module DB table updates; verify organization scopes on every query and event; disallow direct provider SDK types in public common contracts; ensure same request lifecycle in embedded/container hosts; test two independent replicas against one resource snapshot. Do not introduce an agent framework, Kubernetes dependency, cloud message bus, or general HTTP traffic features as prerequisites to MVP.
