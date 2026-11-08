# 04 — Technology stack and deployment

**Authority:** approved technology baseline, distribution and deployment invariants. Detailed throughput/recovery criteria in [12](12-observability-and-nonfunctional-requirements.md); dependencies and security in [05](05-identity-security-and-privacy.md).

## Choices and status

| Area | Baseline | Status and rationale |
|---|---|---|
| Runtime | **.NET 10**, ASP.NET Core, C# | Approved; host-agnostic, optimized networking and composable DI |
| Server components | Modular monolith in domain, independently deployed control/data processes | Approved architecture |
| Durable store | **PostgreSQL** | Approved; transactional records and durable conversations |
| Coordination/cache | **Redis**-compatible service | Approved option/reference for hot/ephemeral state, distributed counters; requirement determined by enabled capabilities/topology |
| Management UI | **Next.js + shadcn/ui** | Approved; keep admin UI outside latency-critical data plane |
| Telemetry | **OpenTelemetry** | Approved common instrumentation standard |
| Distribution | Standard OCI container(s) **and NuGet/`AddIskender(...)`** | Approved parallel consumption modes |
| First cloud reference | Azure Container Apps + Azure Database for PostgreSQL + Azure managed Redis | Approved initial reference topology, not code-level lock-in |
| Local development | Native .NET + containers for dependencies, Docker Compose | Recommended implementation of approved local/on-prem support |
| Build | .NET SDK, centrally managed versions/dependencies, GitHub Actions | Recommended tooling within approved CI practice |
| Libraries | EF Core/Npgsql, Dapper, StackExchange.Redis, Polly, typed HTTP clients, CEL interpreter implementations | **Candidates, not mandates**; benchmark, review licensing and use only where appropriate |
| Performance extensions | Native/Rust only if measurements justify | Approved future flexibility, **not** an MVP language requirement |

**No default cloud secret service, message broker, queue, or SaaS metrics provider is mandated.** Expose adapters for secret resolution, identity and config notifications, and implement minimal infrastructure using durable PostgreSQL/Redis primitives. Avoid cloud-specific SDKs in core/domain contracts.

## Deployable topologies

1. **All-in-one developer container:** control API, data-plane host and optional UI in an easy dev distribution; not proof of production scalability.
2. **Production split:** independent data-plane replica group, control-plane API/UI and optional maintenance worker. Separate scaling, security, health/readiness and rollout units; database/Redis accessed through constrained identities.
3. **Embedded:** consumer app calls `builder.Services.AddIskender(...)` (exact public API signature finalized in spec) and maps gateway endpoints. Host provides DI and may opt into control-plane services; data plane must not take global ownership of routes, logging or configuration. No hidden external control-plane requirement for configured embedded use.
4. **Azure reference:** ACA revision-based deployment, PostgreSQL with backup/PITR planning and managed Redis for coordination. Secret references through workload identity or a replaceable secret provider. Managed identities used where supported; no assumption Azure is the only cloud.
5. **Cloud/on-prem alternatives:** OCI/Kubernetes or ordinary VMs, compatible Postgres, compatible Redis/alternative concurrency coordinator, external OIDC. Local Compose can run the full basic topology.

## Package boundaries and composition

Propose separate packages for hosting/contracts, policy SDK, provider adapters, storage/coordination adapters and optional management, with stable versioning. Do **not** leak private storage models into NuGet public interfaces. A sample ASP.NET Core app must demonstrate `AddIskender(...)` alongside user middleware/services with no fork of Iskender source. Keep provider-specific registration conditional. The official plugin template and build packaging flow live in [11](11-plugin-extensibility-and-sdk.md).

ISK-0001 set these packages out in its [project roles](../specs/0001-solution-dependency-boundaries-build-conventions/contracts/module-dependency-rules.md#project-roles): `Iskender.Hosting`, `Iskender.Core.Contracts`, `Iskender.Policy.Sdk`, `Iskender.Providers.Abstractions` with adapters in `Iskender.Providers.<Provider>`, `Iskender.Infrastructure` with storage and coordination adapters in `Iskender.Infrastructure.<Adapter>`, and `Iskender.Management`, alongside `Iskender.Domain`. Only an infrastructure adapter may take a database driver, only a provider or infrastructure adapter may take a cloud SDK, and only a provider adapter may take a provider SDK. The sample app gets its location through a revision of that contract.

## Deployment invariants

- Separate **liveness** (process works) from **readiness** (mandatory config, durable store, required verified plugins, coordination backend and provider registrations ready); expose safe diagnostic reason codes.
- Schema upgrades are versioned and forward/backward compatible across a documented rolling-upgrade window; maintenance/runbook specifies rollback/backup restoration expectations.
- Runtime configuration updates are validate → compile → publish → atomic activate, preserving version correlation. New plugin binaries and replacement assemblies require process restart/redeployment, even if policy *settings* can activate dynamically.
- Containers run non-root with minimum Linux capabilities, read-only root where practicable, explicit write/cache paths and **read-only plugin mounts**. Downloaded plugin artifacts go to bounded, verified staging storage, never executed before lockfile verification.
- Dev-only defaults cannot appear in production manifests. Protect network links to Postgres/Redis/TLS and internal management endpoints.

## CI and supply chain

GitHub Actions exercises format/static analysis, architecture/unit tests, integration Testcontainers, provider conformance fixtures, SBOM/dependency review, vulnerability checks and controlled performance benchmarks. Publish signed/provenance-bearing release artifacts as policy permits; checksum images/packages and pin dependencies. The repository includes reproducible build instructions, lockfiles or centrally pinned versions where appropriate, branch rules and secret-free CI configuration. No assumption settings are already configured in an unseen GitHub repository.

## Capacity and performance

Instrument serialization/translation, policy CEL evaluation, token accounting, storage waits, upstream HTTP and proxy copies separately. Benchmark long-lived SSE with backpressure, large parallel request counts and plugin handler cost in both embedded and container hosting. Optimize allocations/caching after obtaining traces and baseline. A future Rust module should cross a deliberately narrow FFI boundary for a proven hot path; Rust use does not bypass .NET validation/policy ownership. See [12](12-observability-and-nonfunctional-requirements.md).

## Questions reserved for implementation review

Select exact relational mapper, CEL runtime package, migration strategy, Redis client and secure secret provider based on tests and licensing. Their use is not preapproved simply by mention. Document chosen variants in approved ADRs [16](16-architecture-decision-register.md) and keep the corresponding spec acceptance contract intact.
