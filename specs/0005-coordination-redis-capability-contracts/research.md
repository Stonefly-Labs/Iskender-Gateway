# Research: ISK-0005 — Coordination & Redis Capability Contracts

Each entry records a decision, why it was made, and what was rejected. The spec has no `NEEDS CLARIFICATION` marker left. `specify` settled TR-1, which coordinators ship, and `clarify` settled the cache without Redis and the instance lease. These entries settle how the approved requirements are built. The maintainer decided two of them during `plan` on 2026-11-11, as R6 and R9 record. R6's decision adds a sentence each to the spec's FR-004 and FR-024, and changes no other requirement.

## R1. The Redis client (AV-1)

**Decision**: `StackExchange.Redis` 3.4.31, referenced only by `Iskender.Infrastructure.Redis`. Its `net10.0` dependency group is `RESPite` 3.4.31, `Microsoft.Extensions.Logging.Abstractions` (`>= 10.0.13`, already pinned at 10.0.14), and `System.IO.Hashing` 10.0.13. `RESPite` adds `Microsoft.Bcl.AsyncInterfaces`, `System.Buffers`, `System.IO.Pipelines`, and `System.Memory`.

**License**: every one of them declares the SPDX expression `MIT`, read from each `.nuspec` on nuget.org on 2026-11-11. `check-license.sh` already allows `MIT`, so AV-1 holds and the allow-list does not change. `implement` confirms it by running `check-license.sh` once the packages are restored.

**Rationale**: It is the most widely used maintained Redis client for .NET. It supports Lua scripts (`EVALSHA`), TLS with certificate and host-name validation, ACL users, `READONLY` and authentication errors as distinct server replies, and multiplexed asynchronous commands. ISK-0001 already names it in the `driver` category, which `Iskender.Infrastructure.<Adapter>` may reference, so no ISK-0001 rule changes. Only the adapter sees its types, so the choice affects no public contract, and no ADR is needed (`docs/16` Recommendations table).

**Alternatives considered**: `NRedisStack`, which wraps StackExchange.Redis for module commands this spec does not use. `CSRedisCore` and `FreeRedis`, which are less widely used and maintained.

## R2. The Redis-compatible test server (AV-2)

**Decision**: The Redis fixture runs the official `valkey/valkey` image (BSD-3-Clause) of major version 8, pinned by the digest of its multi-architecture index, through `Testcontainers.Redis` 4.16.0 (MIT) with the image overridden. `implement` reads the digest from the registry and records the lookup. It is never invented.

**Rationale**: Valkey speaks the same protocol and Lua scripting as Redis 7.2, the last BSD-licensed Redis. Its license is permissive, so a test run pulls nothing under a source-available or copyleft license. The adapter uses only commands both support (`SET`, `GET`, `INCR`, `PEXPIRE`, `PTTL`, `EVALSHA`, `SCRIPT LOAD`, `TIME`, `PING`, `ROLE`, `HELLO`), so it stays Redis-compatible, which is what `docs/04` approves. The image is a test dependency and ships in no package.

**Alternatives considered**: `redis` 8, whose server license is a choice of AGPLv3, RSALv2, or SSPLv1. `redis` 7.2, which is out of upstream support.

## R3. Where each part lives (FR-001, FR-002)

**Decision**:

| Part | Project and namespace |
|---|---|
| Ports, scopes, names, keys, levels, requirements, the capability checker, the coordination gate, failure classes, health and credential types | `Iskender.Domain`, namespace `Iskender.Domain.Ports` |
| The `coordination` section schema and settings, the local substitutes, the in-memory cache store, and the single-instance guard | `Iskender.Infrastructure`, namespace `Iskender.Infrastructure.Coordination` |
| The Redis adapter | New project `Iskender.Infrastructure.Redis` |
| The PostgreSQL coordinator, fencing guard, cache generations, and the `coordination` migration module | `Iskender.Infrastructure.PostgreSql`, namespace `Iskender.Infrastructure.PostgreSql.Coordination` |

**Rationale**: ISK-0001 puts shared ports in `Iskender.Domain.Ports`. `Iskender.Infrastructure` takes no driver, so the local substitutes and shared settings can live there, and both adapters already may reference it. `Iskender.Infrastructure.Redis` matches the approved `Iskender.Infrastructure.<Adapter>` pattern, so rules A5 and A19 apply unchanged. The PostgreSQL coordinator needs the unit of work's internal command API (`PostgreSqlUnitOfWork.Command`), so it lives in the PostgreSQL adapter. Its namespace is a framework namespace like `.Migrations`, because `coordination` is not in ISK-0001's module map, and persistence contract version 2 permits it (R7).

**Alternatives considered**: A separate `Iskender.Infrastructure.PostgreSql.Coordination` project, which would need the unit of work's internals to be public. Local substitutes in `Iskender.Domain`, which would mix implementations into the port home.

## R4. Coordination scope and key derivation (FR-003, FR-004)

**Decision**: `CoordinationScope` has the same three factories as ISK-0004's `UnitOfWorkScope` (`ForResource`, `ForRequest`, `Operator`), plus `From(UnitOfWorkScope)`, and carries the `UnitOfWorkScope` it wraps. A null or unresolved scope fails with `coordination.scope_required` before any I/O. Callers name state with a `CoordinationName` (module, name), using the same patterns as `AdvisoryLockName`. The stored key is

```text
isk:v1:<module>:{<hex>}:<suffix>
```

where `<hex>` is the lowercase hexadecimal SHA-256 over UTF-8 `iskender.coordination.v1`, `0x00`, the scope's canonical form (`org:<id>`, `project:<org id>/<project id>`, or `operator`, as in ISK-0004's advisory lock key), `0x00`, the module, `0x00`, and the name. `<suffix>` names the record kind (`v` value, `l` lease, `t` token counter, `g` generation, `e:<hex>` cache entry). The braces are a Redis hash tag, so a lease and its token counter, or a cache entry and its namespace generation, always share one cluster slot.

In PostgreSQL, the same `<hex>` is the row's key. The row also carries the scope's canonical form in `scope_key` for row-level security (R6), the same way an ISK-0004 tenant-owned table carries its owner IDs. That is not a key name, so FR-004's rule on key names holds. The spec's FR-004 is clarified to say so.

**Rationale**: Wrapping `UnitOfWorkScope` gives coordination exactly the trust rules persistence already has, and lets the PostgreSQL coordinator open its unit from the same scope without any conversion that could widen it. Hashing keeps organization IDs out of Redis key listings, `MONITOR`, and slow logs. A full SHA-256 makes collisions irrelevant. Only the module, a bounded label, appears in clear text, so operators can still see which module owns memory.

**Alternatives considered**: A new scope type with its own factories, which duplicates the trust rules. A 64-bit key like the advisory lock's, which can collide, which is harmless for locks but would merge two tenants' counters here.

## R5. Consistency levels, requirements, and the capability check (FR-005 to FR-010)

**Decision**: `ConsistencyLevel` is `Local`, `BestEffort`, `Leased`, `Strict`. `Local` is outside the order. For the others, `BestEffort < Leased < Strict`. A coordinator declares one level, or none, for each `CoordinationPrimitive`: `AtomicOperations`, `Leases`, `Fencing`, `CacheInvalidation`. A `FeatureRequirement` has a name (unique, pattern as `UnitOfWorkOptions.Operation`), an owning module, a minimum level per primitive it uses, a `MandatoryPath` flag, and an `OutageBehavior` of `FailClosed` or `Degrade(signal)`.

`CoordinationCapabilityChecker.Check` is a pure function of the requirements, the topology, the capabilities configured for each primitive, and the single-instance verification state. For each feature, it applies the first matching rule:

| Order | Condition | Result |
|---|---|---|
| 1 | `MandatoryPath` and `Degrade` | `refused`, `degrade_not_permitted` |
| 2 | `single_instance`, and the instance lease is held by another process | `refused`, `single_instance_violated` |
| 3 | A primitive the feature uses is configured for Redis or PostgreSQL, and that coordinator's settings are missing | `refused`, `coordinator_not_configured` |
| 4 | `multi_replica`, and a primitive the feature uses is `Local` | `refused`, `local_in_multi_replica` |
| 5 | `single_instance`, a `Local` primitive, and a `Strict` requirement for it | `refused`, `unsupported_consistency` |
| 6 | A distributed primitive below the feature's minimum, or not offered | `refused`, `unsupported_consistency` |
| 7 | Otherwise | `accepted`, reported `single_instance` or `distributed` |

A requirement that names a primitive twice, an unknown primitive, a duplicate feature name, or a `Local` minimum in `multi_replica` is refused as a malformed declaration with `coordination.capability_unsupported` when the requirements are registered.

The checker produces a `CapabilityReport`. `CoordinationGate.ForFeature(name)` returns a `FeatureCoordination` whose atomic operations, leases, and cache facades check the report, and the instance lease when the feature relies on `single_instance`, before every call. A refused or unknown feature fails with `coordination.capability_unsupported` before touching the coordinator. The raw ports are not handed to modules.

**Rationale**: A fixed order of rules gives one reason per feature and makes SC-002's table mechanical. Making the gate the only route to the ports is what makes FR-009 hold for every call, not only at start-up, and it is also where a lost instance lease takes effect.

**Alternatives considered**: Leaving each feature to check its own acceptance, which one forgotten check would bypass. Throwing at start-up on the first refusal, which ISK-0007 decides, not this spec.

## R6. Isolation of the PostgreSQL coordinator's tables (FR-024; maintainer decision)

**Decision**: **Decided by the maintainer in `plan` on 2026-11-11.** The persistence contract gains a third table kind, a **scope-keyed table**. It has `scope_key text not null`, holding the canonical form of the owning scope, and forced row-level security with one policy for all commands:

```sql
scope_key = CASE nullif(current_setting('iskender.scope_kind', true), '')
  WHEN 'operator' THEN 'operator'
  WHEN 'organization' THEN 'org:' || nullif(current_setting('iskender.organization_id', true), '')
  WHEN 'project' THEN 'project:' || nullif(current_setting('iskender.organization_id', true), '')
                       || '/' || nullif(current_setting('iskender.project_id', true), '')
END
```

Any scope may use a scope-keyed table, and each sees only its own rows. With no settings, the expression is `NULL` and no row matches. Repository code still writes `AND scope_key = $n` (Isolation rule 4's two layers). The migration runner's policy check also covers every table with a `scope_key` column. In the adapter this is a third `TableAccess` kind, `ScopeKeyedTable`.

**Rationale**: One table per primitive serves all three scope kinds. Every scope, the operator included, gets both isolation layers. The fencing guard can write its fence row inside a caller's tenant-scope unit, which an operator table would refuse. A project unit does **not** see its organization's rows here, because coordination state is per exact scope, unlike FR-006 of ISK-0004.

**Alternatives considered**: Organization-owned, project-owned, and operator tables for each primitive (about 12 tables, three copies of every statement). Operator tables keyed only by hash, with no row-level security, which also stops the guard running in a tenant unit.

## R7. Persistence contract version 2 (FR-024)

**Decision**: This spec publishes `contracts/persistence.md` version 2 in its own directory, and repoints the links in `docs/03` and `docs/04` to it. The changes:

1. **Ownership rule 4**: the framework namespace `Iskender.Infrastructure.PostgreSql.Coordination` names only `isk_coordination`, and the module-namespace check enforces it.
2. **Isolation**: a new rule 8 for scope-keyed tables (R6). Rule 6 lists `ScopeKeyedTable` as usable by every scope. Rule 7's policy check covers `scope_key` tables.
3. **Migrations**: a new rule 12. The adapter ships its first production migration module, `coordination`, with no dependencies.
4. **Left to later specs**: the "Leases, fencing, Redis" row is removed, because ISK-0005 delivers them.

Advisory locks rules 1 to 5 are unchanged, word for word, and the contract's version table records the change with ISK-0005 as the spec and the Architecture (ADR) owner, with the Security owner for the isolation rule.

**Rationale**: This is the change procedure version 1 sets out. Keeping the advisory lock rules byte-for-byte unchanged is how SC-008 is checked.

## R8. The PostgreSQL coordinator (FR-002a, TR-8 to TR-11)

**Decision**: Module `coordination`, schema `isk_coordination`, supported range `[1, 1]`. Version 1 creates scope-keyed tables:

| Table | Columns beyond `scope_key` | Use |
|---|---|---|
| `coordination_values` | `key_hash`, `value bigint`, `version bigint`, `expires_at timestamptz` | Atomic operations |
| `leases` | `key_hash`, `holder uuid`, `token bigint`, `expires_at` | Leases |
| `fences` | `resource_hash`, `lease_hash`, `highest_token bigint` | The fencing guard |
| `cache_generations` | `namespace_hash`, `generation bigint` | Cache invalidation |

Each has primary key `(scope_key, key_hash)` (or its equivalent). Fencing tokens come from one sequence, `isk_coordination.fencing_tokens`, so they are strictly increasing across all keys, never reused, and not lost across a PostgreSQL restart, because a sequence is never rolled back and its value is WAL-logged. Gaps are allowed.

Each operation opens its own unit of work, Read Committed, in the coordination scope, runs one statement that both checks and changes the row (for example `UPDATE … SET value = value + $1 WHERE … AND value + $1 <= $2 RETURNING value`, or `INSERT … ON CONFLICT DO UPDATE … WHERE`), and commits. The row lock serializes concurrent callers, so each key is linearizable, and a committed change survives restart, so the coordinator declares `Strict` for every primitive it offers. Time is the server's `clock_timestamp()`. An expired row counts as absent and is replaced or deleted by the next write to its key. `PurgeExpiredAsync(scope, batch)` deletes expired `coordination_values` and `leases` rows of one scope in bounded batches, under row-level security, for ISK-0009 to schedule across scopes (R22).

The fencing guard runs in the caller's unit: `INSERT … ON CONFLICT (scope_key, resource_hash) DO UPDATE SET highest_token = excluded.highest_token WHERE fences.lease_hash = excluded.lease_hash AND fences.highest_token <= excluded.highest_token RETURNING 1`. No row returned means a stale token or a different lease, which fails with `coordination.fencing_rejected` and faults the unit.

**Rationale**: One statement per operation gives atomicity with no transaction held across a round trip. A global sequence is the simplest durable source of monotonic tokens. Recording the lease's hash in the fence row stops a token from one lease key being compared with tokens from another.

**Alternatives considered**: Advisory locks or `SELECT … FOR UPDATE` held across calls, which ISK-0004 rules out for leases. A per-key token column, whose monotonicity depends on the row never being deleted.

## R9. Redis consistency (AV-3; maintainer decision)

**Decision**: **Decided by the maintainer in `plan` on 2026-11-11.** The Redis adapter declares:

| Primitive | Level | Why |
|---|---|---|
| Atomic operations | `Leased` | Each script is atomic on the primary. A failover under asynchronous replication can lose acknowledged writes, bounded by key expiry |
| Leases | `Leased` | `SET NX PX` is exclusive while the primary is up. A failover can lose a grant, bounded by lease duration |
| Fencing | `BestEffort` | The token counter (`INCR`) can lose its last increments on failover, so a token can be issued twice. Nothing the adapter can check proves otherwise |
| Cache invalidation | `Leased` | A failover can lose a generation bump, bounded by entry expiry |

`WAIT` and AOF `appendfsync always` narrow these windows but do not remove them, so they change no level. A feature that needs fencing at `Leased` or above, such as ISK-0009's workers, must take its leases from the PostgreSQL coordinator. The checker enforces this.

**Rationale**: This is TR-4's rule (when in doubt, declare the weaker level), applied to Redis's documented replication behavior. The conformance suite runs every scenario against a single Valkey node and proves the declared levels' guarantees while it stays up.

**Alternatives considered**: Raising Redis fencing to `Leased` with a PostgreSQL-issued epoch bumped on failover, which adds failover detection and a PostgreSQL dependency to the Redis adapter and needs proof this plan cannot give.

## R10. Redis operations (TR-8, TR-9, TR-11)

**Decision**: Every operation is one Lua script, loaded once with `SCRIPT LOAD` and called with `EVALSHA` (falling back to `EVAL` on `NOSCRIPT`), on keys that share one hash tag. A value key is a hash `{v, ver}` with `PEXPIRE` set when the key is created. Reserve checks `v + amount <= limit` and overflow in the script. A lease key holds `holder:token`, set with `SET NX PX`, and its token comes from `INCR` on the `:t` key inside the same script. Renewal and release compare the holder before changing anything. Scripts return `PTTL`, so the client computes remaining validity from the server's expiry, less the time since it sent the command (R12). A cache entry is a hash `{ver, gen, val}`, and the namespace generation is a separate key with the same hash tag. Values travel as bytes and are never logged.

**Rationale**: A script is atomic in Redis, so there is no read-modify-write window (FR-012). Sharing a hash tag keeps scripts valid on a cluster, although clusters are post-MVP. Using the server's TTL keeps replica clock skew out of lease safety.

## R11. Redis connection, TLS, credentials, and classification (TR-12, TR-15)

**Decision**: One `ConnectionMultiplexer` per process, built from `ConfigurationOptions` set field by field, never from a connection string. `tlsMode: require` sets `Ssl = true` and validates the certificate chain against the system store and each endpoint's own host name against its certificate, so `SslHost` stays unset and every endpoint is added as a `DnsEndPoint`. `disable` is development-only. The password comes from `ICoordinationCredentialProvider` when the multiplexer is built. On an authentication failure (`NOAUTH`, `WRONGPASS`), the adapter disposes it, and the next call builds a new one with a freshly resolved credential, at most once per `connectTimeout`. The client's own logging is not connected.

The client cannot take a password callback per physical connection, unlike Npgsql (ISK-0004 R17). Resolving once per multiplexer, and again after an authentication failure, is the nearest equivalent, and the contract says so.

Classification: `RedisConnectionException` and `READONLY` replies are `Unavailable`. `RedisTimeoutException` is `Timeout`. An authentication reply is `Unavailable` with class `AuthenticationFailed`. Cancellation is `Cancelled`. The client does not cancel a command already sent, so the adapter stops waiting, and for a write, sets `outcome = unknown`. `OutcomeUnknown` is also set for a write whose connection failed or timed out after sending. It is never set when the write's own command was not yet handed to the client, as while connecting or during `SCRIPT LOAD`, or when the client reports it still waiting to be sent. It is carried as the `ErrorDiagnostics` values `failure_class` and `outcome`, which ISK-0002 never sends to a client. The adapter never retries.

**Rationale**: It keeps every rule ISK-0004 set for PostgreSQL (no connection string, verified TLS, a secret reference resolved by the host) as closely as the client allows, and states the one difference.

## R12. Lease validity and the safety margin (FR-014, FR-016)

**Decision**: A `Lease` records the coordinator-reported remaining time at grant and a monotonic timestamp (`TimeProvider.GetTimestamp`) taken **before** the request was sent. `Remaining = granted − elapsed since send`, which is never longer than the true remaining time, because network delay only shortens it. `IsSafeToAct` is `Remaining > leases.safetyMargin`. Durations outside `[leases.minimumDuration, leases.maximumDuration]`, and waits above `leases.maximumWait`, are refused with `core.value_invalid` before any I/O. A bounded wait polls with jittered backoff, using `TimeProvider`.

**Rationale**: Measuring from before the send is the standard conservative bound, and needs no wall clock.

## R13. The cache with each coordinator (FR-019 to FR-021, clarify decision)

**Decision**: Redis keeps entries and generations in Redis (R10). The PostgreSQL coordinator keeps values in the process's `InMemoryCacheStore` (bounded by `cache.maximumEntries`, least recently used evicted), and stores each namespace's generation in `cache_generations`. A read checks the generation (one indexed `SELECT` in its own unit) and the expected version against the in-memory entry. A bump is an `UPDATE … SET generation = generation + 1`, or an insert. The local substitute keeps both in memory. Any coordinator failure makes a read a miss, writes a 5008 diagnostic event, and throws nothing. A failed set or invalidation throws, because a lost invalidation must be visible to its caller. A miss carries a `CacheStamp`, the generation it read, and `SetAsync` stores only while that generation is current: in Redis the set script compares `:g` with the stamp, and with PostgreSQL the store compares the generation it reads in the same call. This closes the race where a value read from the source before a bump is stored after it (R22).

**Rationale**: This is the `clarify` decision. Throwing on a failed invalidation matters for ISK-0017: revocation must not report success when no replica will see it.

## R14. Single-instance verification (FR-007a, clarify decision)

**Decision**: `SingleInstanceGuard` (in `Iskender.Infrastructure.Coordination`) takes a PostgreSQL lease in the operator scope, module `coordination`, name `instance.data_plane` or `instance.control_plane`, for `instanceLease.duration`. It renews every third of the duration on a `TimeProvider` timer, and reports `Held`, `HeldByOther`, `Lost`, or `Unverified`. The checker refuses with `single_instance_violated` on `HeldByOther`. The gate fails `single_instance` features with `coordination.capability_unsupported` while the state is `Lost`, and resumes when the lease is taken again. Without PostgreSQL it reports `Unverified`, which the checker accepts and reports. ISK-0006 starts and disposes the guard. This spec adds no host wiring.

**Rationale**: It reuses the PostgreSQL lease, which is `Strict`, so two processes can never both hold it.

## R15. Error codes (FR-023)

**Decision**: Seven codes in `ErrorCodes`, `since: 4`, raising `core-contracts` from 3 to 4:

| Code | Category | Safe message |
|---|---|---|
| `coordination.unavailable` | `internal` | The coordination service is unavailable. |
| `coordination.timeout` | `deadline_exceeded` | The coordination service did not respond in time. |
| `coordination.cancelled` | `cancelled` | The coordination operation was cancelled. |
| `coordination.lease_lost` | `conflict` | The lease is no longer held. |
| `coordination.fencing_rejected` | `conflict` | A newer holder has already changed this resource. |
| `coordination.capability_unsupported` | `internal` | This feature is not available in the current deployment. |
| `coordination.scope_required` | `internal` | A coordination operation was attempted without an owner scope. |

The ISK-0002 contract's error code and versioning tables are updated in the same commit, and the harness baselines are regenerated.

**Rationale**: `capability_unsupported` is a deployment refusal, not a caller error, so it is `internal`, not ISK-0002's `capability_unavailable`, which describes an endpoint or deployment the caller chose. An expected contention or a denied reservation is a result, not an error, so it needs no code.

## R16. The `coordination` section (FR-027)

**Decision**: `CoordinationSectionSchema` in `Iskender.Infrastructure` registers the namespace `coordination`, version 1. The fields are listed in [data-model.md](data-model.md#configuration). Redis endpoints are not required by the schema, because the section is always present. A primitive configured for `redis` with no endpoints is refused by the checker with `coordinator_not_configured`, and the probe reports `not_configured`.

**Rationale**: ISK-0003 cannot make a field in an always-present section conditionally required. Refusing in the checker gives the same fail-closed result with a precise reason.

## R17. Health (FR-025)

**Decision**: `ICoordinationHealthProbe.CheckAsync` checks each distributed coordinator in use: Redis with `PING` and `ROLE` (a replica role gives `read_only`), and PostgreSQL with one read of `cache_generations` in an operator unit. The first unhealthy result, in the order Redis then PostgreSQL, is reported. Every reason is listed in the contract. `not_configured` is healthy when no primitive uses a distributed coordinator, and unhealthy when one does but its settings are missing. The probe returns within `healthProbeTimeout` plus a fixed 2-second allowance, a safety bound.

## R18. Diagnostics (FR-026, FR-028)

**Decision**: Events 5001 to 5011 (listed in the contract), through the host's `ILogger`. `ActivitySource("Iskender.Coordination")` with activities `coordination.operation` and `coordination.capability_check`, and `Meter("Iskender.Coordination")` with `iskender.coordination.operations`, `iskender.coordination.operation.duration`, `iskender.coordination.lease.lost`, `iskender.coordination.fencing.rejected`, and `iskender.coordination.capability.refused`. Labels are limited to module, primitive, operation, outcome, failure class, consistency level, and coordinator kind.

## R19. Tests and fixtures (FR-029 to FR-031)

**Decision**:

- `tests/Iskender.Coordination.Conformance`: a new non-test class library of abstract xUnit test classes written against the ports, with an `ICoordinatorUnderTest` seam that gives one or two independent clients. It covers atomicity, reservations, compare-and-set, leases, tokens, fencing, cache versions, scope isolation, and failure classification.
- `tests/Iskender.Infrastructure.Tests` (new): the local substitutes against the suite, the section schema, and the in-memory store.
- `tests/Iskender.Infrastructure.PostgreSql.Tests`: the PostgreSQL coordinator against the suite, with its two clients being two separate `NpgsqlDataSource`s, plus the fencing guard, row-level security on scope-keyed tables, and the instance lease, all on ISK-0004's fixture.
- `tests/Iskender.Infrastructure.Redis.Tests` (new): the Redis adapter against the suite, with two separate multiplexers, on a Valkey fixture following ISK-0004's rules (R2). Stopping and pausing the container covers outage classification.
- `tests/Iskender.Domain.Tests/Ports/`: the checker against its full combination table (SC-002), key derivation vectors, names, scopes, and lease arithmetic.

The acceptance groups are the refusal tests (User Story 1) and the lease fencing tests (User Story 2), each run against every implementation.

**Rationale**: One suite keeps every implementation honest about the level it declares. A class library holding abstract tests is the usual xUnit pattern for conformance suites.

## R20. Repository wiring

**Decision**: Add `StackExchange.Redis` 3.4.31 and `Testcontainers.Redis` 4.16.0 to `Directory.Packages.props`. Add the three new projects to `Iskender.slnx`, `.github/CODEOWNERS`, and `SolutionBuildsTests`' allowed prefixes, each with a committed lock file. The Redis and PostgreSQL projects reference `Iskender.Infrastructure`. CI needs no new check (AV-7): the existing `dotnet test` check runs both containers.

## R21. Documentation (FR-032)

**Decision**: `docs/03` Persistence links the coordination contract and the revised persistence contract. `docs/04` gets a "Coordination" runbook section beside "Database migrations and rollback": topology, choosing a coordinator per primitive and what each declares, refusal reasons, health reasons, Redis provisioning (ACL user, TLS, no persistence expectations), and the `isk_coordination` migration module. The ISK-0002 contract gets its error code and version rows.

## R22. Decisions made during `checklist` (FR-013, FR-013a, FR-017, FR-019)

**Decided by the maintainer in `checklist` on 2026-11-11:**

1. **The fencing guard accepts PostgreSQL leases only.** A fence record is permanent, but a local token counter restarts with its process, and a Redis token counter restarts when Redis loses it. A token below the recorded one would leave the resource rejecting every write. `Lease.Issuer` records the coordinator that granted the lease, and the guard fails any lease not issued by the PostgreSQL coordinator with `coordination.capability_unsupported` before writing. This matches R9: a feature that needs fencing at `Leased` or above already takes its leases from PostgreSQL.
2. **The purge takes a scope.** Under R6, an operator unit sees only operator rows, and persistence contract version 2, Isolation rule 6, reserves operator access to tenant data for a later audited operation. `PurgeExpiredAsync(scope, batch)` therefore deletes expired rows in one scope, and ISK-0009 iterates the scopes. Fence records and namespace generations are never purged, because they must not restart (FR-013).

Settled from the approved documents in the same stage, without a new decision:

- Token counters, fence records, and namespace generations never expire, and FR-013's expiry rule covers only atomic operations keys and cache entries. Redis must run with an eviction policy that never evicts a key without an expiry, which the runbook states (FR-032).
- A cache store carries the generation observed by its miss (R13), so FR-019 also holds when a store and a bump race.
- `IsSafeToAct` is false at or below the safety margin, as R12 already decides. FR-016 is worded to match.
- `FeatureCoordination` gains a `Fencing` facade, so the gate covers every port (FR-009, contract Ownership rule 3).
- TR-12 lists `authentication failed` as a class, and contention as a result, matching the data model.

## Settled assumptions

| Assumption | Outcome |
|---|---|
| ISK-0004 AV-3 | Settled. Leases and fencing are new ports beside the advisory locks, the fencing guard uses ordinary rows in the caller's unit, no session-scoped lock is added, and Advisory locks rules 1 to 5 are unchanged (R7, R8) |
| AV-1 | Holds: every package is `MIT` (R1) |
| AV-2 | Holds: Valkey, BSD-3-Clause (R2) |
| AV-3 | Settled by R9 |
| AV-4 to AV-6 | Left to ISK-0007, ISK-0009, and ISK-0060, ISK-0061, ISK-0066 |
| AV-7 | Holds for `ubuntu-24.04` runners, which run Docker. `implement` confirms it in CI |
