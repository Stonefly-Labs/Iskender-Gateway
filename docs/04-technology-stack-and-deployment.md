# 04 — Technology stack and deployment

**Authority:** approved technology baseline, distribution and deployment invariants. Detailed throughput/recovery criteria in [12](12-observability-and-nonfunctional-requirements.md); dependencies and security in [05](05-identity-security-and-privacy.md).

## Choices and status

| Area | Baseline | Status and rationale |
|---|---|---|
| Runtime | **.NET 10**, ASP.NET Core, C# | Approved; host-agnostic, optimized networking and composable DI |
| Server components | Modular monolith in domain, independently deployed control/data processes | Approved architecture |
| Durable store | **PostgreSQL** | Approved; transactional records and durable conversations. ISK-0004 sets the ports, migrations, isolation, and health rules in its [persistence contract](../specs/0005-coordination-redis-capability-contracts/contracts/persistence.md) (version 2) |
| Coordination/cache | **Redis**-compatible service | Approved option/reference for hot/ephemeral state, distributed counters; requirement determined by enabled capabilities/topology. ISK-0005 sets what each coordinator declares and when a feature is refused in its [coordination contract](../specs/0005-coordination-redis-capability-contracts/contracts/coordination.md), and Valkey 8 is the tested server |
| Management UI | **Next.js + shadcn/ui** | Approved; keep admin UI outside latency-critical data plane |
| Telemetry | **OpenTelemetry** | Approved common instrumentation standard |
| Distribution | Standard OCI container(s) **and NuGet/`AddIskender(...)`** | Approved parallel consumption modes |
| First cloud reference | Azure Container Apps + Azure Database for PostgreSQL + Azure managed Redis | Approved initial reference topology, not code-level lock-in |
| Local development | Native .NET + containers for dependencies, Docker Compose | Recommended implementation of approved local/on-prem support |
| Build | .NET SDK, centrally managed versions/dependencies, GitHub Actions | Recommended tooling within approved CI practice |
| Libraries | EF Core/Npgsql, Dapper, StackExchange.Redis, Polly, typed HTTP clients, CEL interpreter implementations | **Candidates, not mandates**; benchmark, review licensing and use only where appropriate. ADR-0035 chose Npgsql with plain SQL, and no mapper or migration library, for PostgreSQL |
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

ISK-0002 versions `Iskender.Core.Contracts`, `Iskender.Providers.Abstractions`, and `Iskender.Policy.Sdk` as separate contracts, and `tests/Iskender.Contracts.Compatibility.Tests` fails the test run on an undeclared break to any of them, under the [versioning policy](../specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md#versioning-policy). Until ISK-0086 completes, package versions follow ADR-0030.

## Deployment invariants

- Separate **liveness** (process works) from **readiness** (mandatory config, durable store, required verified plugins, coordination backend and provider registrations ready); expose safe diagnostic reason codes.
- Schema upgrades are versioned and forward/backward compatible across a documented rolling-upgrade window; maintenance/runbook specifies rollback/backup restoration expectations.
- Runtime configuration updates are validate → compile → publish → atomic activate, preserving version correlation. New plugin binaries and replacement assemblies require process restart/redeployment, even if policy *settings* can activate dynamically.
- Containers run non-root with minimum Linux capabilities, read-only root where practicable, explicit write/cache paths and **read-only plugin mounts**. Downloaded plugin artifacts go to bounded, verified staging storage, never executed before lockfile verification.
- Dev-only defaults cannot appear in production manifests. Protect network links to Postgres/Redis/TLS and internal management endpoints.

ISK-0003 rejects development-only values outside the `development` environment, plaintext secrets in secret fields, and every configuration error before anything runs, under its [configuration contract](../specs/0003-configuration-schema-validation-foundation/contracts/configuration.md).

## Database migrations and rollback

This is the operator runbook for the PostgreSQL schema. The [persistence contract](../specs/0005-coordination-redis-capability-contracts/contracts/persistence.md) holds the rules behind it. PostgreSQL 17 is the tested major version, and the oldest one supported.

**Provisioning.** Create three roles before the first migration:

- `iskender_runtime`, a group role with `NOLOGIN` and `NOBYPASSRLS`. It owns nothing. The runner refuses to migrate, with reason `missing_runtime_role`, until it exists.
- A runtime login that is a member of `iskender_runtime`, with `NOSUPERUSER`, `NOBYPASSRLS`, and no `CREATE` on the database. The gateway's data plane connects as this login only, and needs no migration login.
- A migration login with `CREATE` on the database and nothing more. It owns the `isk_<module>` schemas it creates and grants the runtime group their use.

Each password is a secret reference that the host's credential provider resolves on every physical connection. `tlsMode: require` verifies the server certificate chain against the system trust store and the host name against the certificate, which is stricter than libpq's `require`. `disable` is for development only.

**Running migrations.** Migrations run only as an explicit operation, never on start-up. ISK-0007 exposes that operation. Each migration runs in its own transaction, so a script cannot use a statement PostgreSQL refuses inside a transaction, such as `CREATE INDEX CONCURRENTLY` or `VACUUM`. A failed migration rolls back and records nothing, and earlier migrations stay applied. Back up the database before every migration run.

**The migration lock.** One migrator runs at a time. It holds a session advisory lock on the key `6156145740695756181`, and others wait up to `migration.lockWait`, then fail with `persistence.migration_locked`. A crashed holder's lock is released with its connection. To see who holds it:

```sql
SELECT pid, granted FROM pg_locks
WHERE locktype = 'advisory' AND classid = 1433339375 AND objid = 1001676181 AND objsubid = 1;
```

**Reversing.** A reverse request takes one step by default, in the exact reverse of apply order. A step whose reverse script is marked `-- iskender:data-losing` needs explicit confirmation. Without it, the whole request fails with `persistence.reverse_requires_confirmation` and nothing changes. When a reverse step cannot recover the data it drops, restore from the backup instead.

**Rolling back a release.** A release cannot reverse migrations it does not know, because it does not ship their reverse scripts. Reverse the newer migrations with the newer release first, then deploy the older one. Within the compatibility window, migrations are expand-only, so the previous release keeps working against the newer schema.

**Health reasons.** The store health probe reports one reason. It connects as the runtime login on a pool of its own, holding at most one connection, so a hung probe never takes a connection from real work. When sizing `max_connections`, count `poolMaximum` plus one for each process. ISK-0007 maps reasons onto readiness, and keeps replicas ready during `migration_in_progress`, because a migration in the window is expand-only.

| Reason | Operator action |
|---|---|
| `ok` | None |
| `unreachable` | Check the network path, the server, and TLS |
| `authentication_failed` | Check the runtime login, its secret reference, and the credential provider |
| `timeout` | Check server load and the network |
| `migration_in_progress` | Wait for the migration to finish |
| `schema_behind` | Run migrations |
| `insufficient_privilege` | Fix the runtime login's grants or its membership of `iskender_runtime` |
| `unexpected` | Read event 4014 for the SQLSTATE class |
| `checksum_mismatch` | Restore the shipped script; never edit an applied one |
| `schema_ahead` | Deploy the newer release, or reverse the newer migrations with the newer release |

## Coordination

This is the operator runbook for distributed coordination. The [coordination contract](../specs/0005-coordination-redis-capability-contracts/contracts/coordination.md) holds the rules behind it.

**Declaring the topology.** Set `coordination.topology` to `single_instance` or `multi_replica`. Left unset, it is `multi_replica`, so a forgotten setting never turns a per-replica limit into the real limit. With PostgreSQL configured, a `single_instance` process holds an operator-scope lease for its plane (`instance.data_plane` or `instance.control_plane`) and renews it every third of `instanceLease.duration`. A second process of the same plane is refused, and a process that loses the lease fails its `single_instance` features until it takes the lease again. Without PostgreSQL, the declaration is accepted and reported as unverified, because only a PostgreSQL lease can show that no other process holds the plane.

**Choosing a coordinator.** Each primitive takes its coordinator from its own section (`atomic`, `leases`, `cache`), and fencing follows `leases`. Each coordinator declares one consistency level per primitive:

| Primitive | `local` | `redis` | `postgresql` |
|---|---|---|---|
| Atomic operations | `local` | `leased` | `strict` |
| Leases | `local` | `leased` | `strict` |
| Fencing | not offered | not offered | `strict` |
| Cache invalidation | `local` | `leased` | `strict` |

The fencing guard accepts only PostgreSQL-issued leases, so a feature with any fencing minimum is refused with `unsupported_consistency` unless `leases` is on PostgreSQL. With PostgreSQL as the cache coordinator, cached values stay in each process's memory, bounded by `cache.maximumEntries`, and only each namespace's generation is shared.

**Refusal reasons.** The capability check gives each feature one result, logged as event 5001 or 5002. Every call through a refused feature fails with `coordination.capability_unsupported` and touches no state. ISK-0007 maps refusals onto start-up and readiness.

| Reason | Operator action |
|---|---|
| `degrade_not_permitted` | A mandatory path cannot degrade; fix the feature's declaration |
| `single_instance_violated` | Another process of this plane holds the instance lease; stop it, or declare `multi_replica` with distributed coordinators |
| `coordinator_not_configured` | Configure the `redis` section or the `persistence` section the primitive names |
| `local_in_multi_replica` | Choose `redis` or `postgresql` for the primitive |
| `unsupported_consistency` | Choose a coordinator that declares the feature's minimum |

**Health reasons.** The coordination probe checks Redis, then PostgreSQL, for each one a primitive uses, within `healthProbeTimeout`. ISK-0007 maps reasons onto readiness; liveness never calls it.

| Reason | Operator action |
|---|---|
| `ok` | None |
| `not_configured` | Healthy when no primitive uses a distributed coordinator, though `multi_replica` then refuses every feature that needs one; unhealthy when a primitive names a coordinator with no settings, so configure it |
| `unreachable` | Check the network path, the server, and TLS |
| `authentication_failed` | Check the ACL user or runtime login, its secret reference, and the credential provider |
| `timeout` | Check server load and the network |
| `read_only` | Redis answers as a replica; wait for failover to finish, or point at the primary |
| `unexpected` | Read event 5010 |

**Provisioning Redis.** Give the gateway an ACL user with only the key pattern `~isk:v1:*` and the commands `EVALSHA`, `EVAL`, `SCRIPT LOAD`, `PING`, `ROLE`, `HELLO`, `GET`, `SET`, `DEL`, `INCR`, `PEXPIRE`, `PTTL`, `HGET`, `HSET`, and `HMGET`. It never needs `CONFIG`, `KEYS`, `FLUSHALL`, `SELECT`, or any administrative command, and it always uses database 0. Its password is a secret reference the host's credential provider resolves when the connection is built, and again after an authentication failure. Outside `development`, `redis.username` and `redis.password` are required whenever `redis.endpoints` is set, and the gateway refuses the configuration with `config.field.required` otherwise; only development may use an open Redis. `tlsMode: require` verifies the certificate chain against the system trust store and each endpoint's own host name; `disable` is for development only. Run Redis with `maxmemory-policy` set to `noeviction` or a `volatile-*` policy: fencing token counters and namespace generations never expire, and an `allkeys-*` policy could evict one, repeating a token or serving an invalidated entry. The adapter cannot check the policy, because its user has no `CONFIG`. Expect nothing from Redis persistence: a restart or failover can lose acknowledged writes, which is why Redis declares `leased` and offers no fencing.

**The `coordination` migration module.** It creates the schema `isk_coordination`, with `coordination_values`, `leases`, `fences`, `cache_generations`, and the `fencing_tokens` sequence, each table under row-level security on `scope_key`. It runs with the other modules, as an explicit operation. Expired value and lease rows count as absent at once; `PurgeExpiredAsync` deletes them one scope at a time, and ISK-0009 schedules it. Fence records and namespace generations are never purged.

**Known limits.** A Redis failover can lose writes, grants, and token increments, and Redis Cluster and Sentinel management is post-MVP. A rotated Redis password takes effect only when the connection is rebuilt after an authentication failure. The Redis client cannot cancel a command already sent, so a cancelled write's outcome is unknown, and nothing retries it. A cancelled lease acquire is the exception: the adapter releases the lease by its holder, so a grant the server made is not left held. PostgreSQL does the same for a lease acquire whose commit outcome is unknown after the caller's or request's token fired: the acquire fails with `coordination.cancelled` and `outcome = unknown`, and a release by holder, in the scope's owner scope and detached from the request's deadline and token, waits for the in-flight commit to settle. A PostgreSQL server that stops answering is reported as a timeout within `operationTimeout`, but each abandoned call keeps its pooled connection until the driver gives up, which can take `connectionTimeout` or longer, so size `poolMaximum` for a stall. Expiry follows the coordinator's clock, so a clock step moves lease expiry with it, though the fencing guard still compares tokens. Records that never expire grow with the lease and namespace names modules use. No count of keys per scope is set; size Redis memory for the expected load. `Lease.Grant` and `CacheStamp.Of` are public because the coordinators in the infrastructure assemblies call them, so only convention and review keep module code from building a lease or a cache stamp; a forged PostgreSQL lease would pass the fencing guard. Callers outside the process cannot reach them. Write an IPv6 Redis endpoint as `[address]:port`: an unbracketed one is not refused when the configuration is read, and calls then fail with `coordination.unavailable`. Dispose the Redis coordinator only on shutdown, because a dispose during its first connection can leave that connection open.

## CI and supply chain

GitHub Actions exercises format/static analysis, architecture/unit tests, integration Testcontainers, provider conformance fixtures, SBOM/dependency review, vulnerability checks and controlled performance benchmarks. Publish signed/provenance-bearing release artifacts as policy permits; checksum images/packages and pin dependencies. The repository includes reproducible build instructions, lockfiles or centrally pinned versions where appropriate, branch rules and secret-free CI configuration. No assumption settings are already configured in an unseen GitHub repository.

`.github/scripts/check-license.sh` (CI check 6b) reads every package that a project under `src/` resolves, directly or transitively, from its `packages.lock.json`, and passes it only when the restored package's `.nuspec` declares one of the SPDX license expressions `MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, or `PostgreSQL`. A package with another expression, a license file instead of an expression, or no restored `.nuspec` fails the check. A spec that adds such a package extends the list in that script under Product/Charter owner review, as ISK-0004 did for `PostgreSQL`, the license of Npgsql.

## Capacity and performance

Instrument serialization/translation, policy CEL evaluation, token accounting, storage waits, upstream HTTP and proxy copies separately. Benchmark long-lived SSE with backpressure, large parallel request counts and plugin handler cost in both embedded and container hosting. Optimize allocations/caching after obtaining traces and baseline. A future Rust module should cross a deliberately narrow FFI boundary for a proven hot path; Rust use does not bypass .NET validation/policy ownership. See [12](12-observability-and-nonfunctional-requirements.md).

## Questions reserved for implementation review

Select exact relational mapper, CEL runtime package, migration strategy, Redis client and secure secret provider based on tests and licensing. Their use is not preapproved simply by mention. Document chosen variants in approved ADRs [16](16-architecture-decision-register.md) and keep the corresponding spec acceptance contract intact.
