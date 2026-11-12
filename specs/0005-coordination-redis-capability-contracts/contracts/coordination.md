# Coordination Contract

**Version**: 1

**Owning spec**: ISK-0005 — Coordination & Redis Capability Contracts

**Review owner**: Architecture (ADR) owner, with the Security owner for Scope and keys, Credentials, and the scope-keyed tables, and the Privacy/Operations owner for Diagnostics. See `CONTRIBUTING.md` for who holds each role.

This contract states what module authors, operators, and later specs may rely on from the coordination ports in `Iskender.Domain.Ports` and their implementations: the local substitutes in `Iskender.Infrastructure`, the Redis adapter in `Iskender.Infrastructure.Redis`, and the PostgreSQL coordinator in `Iskender.Infrastructure.PostgreSql`. The type shapes are in [data-model.md](../data-model.md), and the reasons in [research.md](../research.md). The PostgreSQL tables follow the [persistence contract](persistence.md), version 2.

## Changing this contract

A change to a rule here is made by a later spec's revision of this contract, which increments the version, publishes the contract in that spec's `contracts/` directory, repoints the links in `docs/03-domain-and-technical-architecture.md` and `docs/04-technology-stack-and-deployment.md`, and records the change in this table.

| Version | Spec | Change | Approval | Owner |
|---|---|---|---|---|
| 1 | ISK-0005 | First version | This spec's PR | Architecture (ADR) owner |

The ports are in the embeddable `Iskender.Domain`, so the ISK-0002 compatibility harness does not version them. This contract's version is the commitment to later specs. The error codes are in `Iskender.Core.Contracts` and are versioned by the harness.

## Ownership

*Review owner: Architecture (ADR) owner.*

1. The ports are in `Iskender.Domain.Ports`. No port signature names a driver, Redis, PostgreSQL, or ASP.NET Core type (FR-001).
2. `Iskender.Infrastructure.Redis` is the only production project that references `StackExchange.Redis`. The local substitutes are in `Iskender.Infrastructure.Coordination`, and the PostgreSQL coordinator in `Iskender.Infrastructure.PostgreSql.Coordination`, which names only the schema `isk_coordination` (FR-002).
3. Modules reach coordination only through `CoordinationGate.ForFeature`, whose `Atomic`, `Leases`, `Fencing`, and `Cache` facades cover every port. They are not given the raw ports.
4. Each port and adapter type's XML comment names ISK-0005 as its owning spec.

## Scope and keys

*Review owner: Security owner.*

1. Every operation takes a `CoordinationScope`, which comes only from `ForResource`, `ForRequest`, `Operator`, or `From(UnitOfWorkScope)`. A `null` or unresolved owner fails with `coordination.scope_required` before any I/O (FR-003).
2. State is named by module and name. No port accepts a raw key.
3. The key hash is the lowercase hex SHA-256 over UTF-8 `iskender.coordination.v1`, `0x00`, the scope's canonical form (`org:<id>`, `project:<org id>/<project id>`, or `operator`), `0x00`, the module, `0x00`, and the name.
4. A Redis key is `isk:v1:<module>:{<hash>}:<suffix>`. Only the module appears in clear text. No organization ID, project ID, credential, or name text appears in any Redis key (FR-004).
5. A PostgreSQL row is keyed by the same hash and carries the canonical scope in `scope_key` for row-level security, under persistence contract version 2, Isolation rule 8.
6. Coordination state is per exact scope. A project scope sees none of its organization's state, and an organization scope sees none of its projects'.

## Consistency

*Review owner: Architecture (ADR) owner.*

1. The levels are `local`, `best_effort`, `leased`, and `strict`. `best_effort < leased < strict`, and `local` is outside that order:

   | Level | Guarantee |
   |---|---|
   | `local` | Exact within one process only |
   | `best_effort` | Shared across replicas. An acknowledged update may be lost or applied twice on coordinator restart or failover |
   | `leased` | Shared and atomic per key while the coordinator stays up. On failover, grants and updates may be lost, bounded by key or lease expiry |
   | `strict` | Linearizable per key. No acknowledged update or fencing token is lost or repeated across coordinator restart or failover |

2. Each implementation declares one level per primitive, and when in doubt the weaker one:

   | Primitive | Local substitute | Redis | PostgreSQL |
   |---|---|---|---|
   | Atomic operations | `local` | `leased` | `strict` |
   | Leases | `local` | `leased` | `strict` |
   | Fencing | `local` | `best_effort` | `strict` |
   | Cache invalidation | `local` | `leased` | `strict` |

   **Decided by the maintainer in `plan` on 2026-11-11** for Redis. A feature that needs fencing at `leased` or above takes its leases from PostgreSQL.

3. A local substitute is never reported as distributed or as production-grade distributed enforcement (FR-010).

## Requirements and the capability check

*Review owner: Architecture (ADR) owner.*

1. A feature registers a `FeatureRequirement`: name, module, the minimum level for each primitive it uses, `MandatoryPath`, and an outage behavior of `fail_closed` or `degrade` with a named signal (FR-006).
2. Topology is `single_instance` or `multi_replica`, and `multi_replica` when not set (FR-007).
3. The checker gives each feature one result. The first matching rule wins:

   | Order | Condition | Result and reason |
   |---|---|---|
   | 1 | `MandatoryPath` with `degrade` | refused, `degrade_not_permitted` |
   | 2 | `single_instance`, and another process holds the instance lease | refused, `single_instance_violated` |
   | 3 | A primitive it uses is set to `redis` or `postgresql` without that coordinator's settings | refused, `coordinator_not_configured` |
   | 4 | `multi_replica`, and a primitive it uses is `local` | refused, `local_in_multi_replica` |
   | 5 | `single_instance`, a `local` primitive, and a `strict` minimum for it | refused, `unsupported_consistency` |
   | 6 | A distributed primitive below its minimum, or not offered | refused, `unsupported_consistency` |
   | 7 | Otherwise | accepted, as `single_instance` or `distributed` |

4. A malformed set of requirements (a duplicate feature name, an empty or repeated primitive, or a `local` minimum in `multi_replica`) fails with `coordination.capability_unsupported` when registered.
5. Every call through a refused or unknown feature fails with `coordination.capability_unsupported` before touching any state, and never falls back to a local substitute (FR-009).
6. **Single instance** (FR-007a): with PostgreSQL configured, a `single_instance` process holds the operator-scope lease `coordination` / `instance.data_plane` or `instance.control_plane`, renewed every third of `instanceLease.duration`. While it is lost, the process's `single_instance` features fail with `coordination.capability_unsupported`, until it is taken again. The gate checks the state before each call, so a call already past the gate completes, and only later calls fail. Without PostgreSQL, the declaration is accepted and reported as unverified.
7. ISK-0007 maps refusals onto start-up failure or readiness.

## Atomic operations

*Review owner: Architecture (ADR) owner.*

1. Operations are single-key: add, reserve, release, compare-and-set, and read. Multi-key transactions are not offered (FR-011).
2. A reservation is granted only if `value + amount ≤ limit`, checked and applied atomically, and is denied rather than overflow (FR-012). A denied reservation is a result, not an error.
3. Every key's expiry is positive and at most `keys.maximumExpiry`, is set when the key is created, and is kept by later operations (FR-013). Fencing token counters, fence records, and namespace generations never expire.
4. Every change raises the key's version by one.
5. A release subtracts the given amount, never below 0. It is not tied to a reservation and is not idempotent: the caller tracks what it reserved and releases it once. ISK-0061 builds concurrency leases on top.
6. Expired PostgreSQL rows count as absent at once. `PurgeExpiredAsync(scope, batch)` deletes expired atomic operations and lease rows in the given scope only, under row-level security (FR-013a). **Decided by the maintainer in `checklist` on 2026-11-11.**

## Leases and fencing

*Review owner: Architecture (ADR) owner; Security owner for rule 6.*

1. A lease's duration, on acquisition and on renewal, is within `[leases.minimumDuration, leases.maximumDuration]`, and a bounded wait is at most `leases.maximumWait`. Anything else fails with `core.value_invalid` before any I/O. Bounded waits poll, and give no fairness or ordering guarantee between waiters.
2. A lease carries a fencing token, strictly increasing per lease key and never reused. PostgreSQL tokens are strictly increasing across all keys (FR-014).
3. Expiry is measured on the coordinator's clock. `Remaining` is the granted time less the time since the request was sent, and `IsSafeToAct` is `Remaining > leases.safetyMargin` (FR-016).
4. Acquisition is try-only or bounded, and a held lease is reported as contention (FR-015).
5. Renewal succeeds only while the caller still holds its token, otherwise it fails with `coordination.lease_lost`. Release is idempotent and never releases another holder's lease.
6. The fencing guard runs inside the caller's ISK-0004 unit of work, in the lease's scope. It accepts a token only when it is greater than or equal to the highest token accepted for that resource **and** comes from the lease key that resource was first fenced with. Otherwise it fails with `coordination.fencing_rejected`, and the unit faults and commits nothing (FR-017). It accepts only leases whose `Issuer` is the PostgreSQL coordinator, because only its tokens survive a restart, and fails any other lease with `coordination.capability_unsupported` before any write. **Decided by the maintainer in `checklist` on 2026-11-11.**
7. Neither leases nor the guard use an advisory lock, and this spec adds no session-scoped lock (FR-018).

## Versioned cache

*Review owner: Architecture (ADR) owner.*

1. An entry of a version other than the reader's expected version is a miss. Bumping a namespace's generation makes every entry in it a miss (FR-019). A miss carries a `CacheStamp`, the generation it observed, and `SetAsync` stores only when that generation is still current, so a value read from the source before a bump is never stored after it. A miss caused by a failure carries no stamp, and a set with no stamp stores nothing.
2. With the PostgreSQL coordinator, values stay in the process's bounded in-memory store, and every read checks the generation stored in PostgreSQL (FR-019a). **Decided by the maintainer in `clarify` on 2026-11-11.**
3. A read that meets a coordinator failure is a miss and writes event 5008. A failed set or invalidation throws (FR-020).
4. A value above `cache.maximumValueBytes` is refused, and no value is ever logged or used as a label (FR-021).

## Failures

*Review owner: Architecture (ADR) owner.*

1. Ports fail only with the ISK-0002 `GatewayException`, carrying a registered code and its safe message, with no inner exception, key, host, or server text. The `ErrorDiagnostics` values `failure_class` and, for a write whose outcome is unknown, `outcome = unknown` are set. ISK-0002 never sends diagnostics to a client.
2. Classification:

   | Class | Code | When |
   |---|---|---|
   | Unavailable | `coordination.unavailable` | Cannot connect, connection lost, `READONLY` during failover, or for PostgreSQL `persistence.unavailable` |
   | Authentication failed | `coordination.unavailable` | `NOAUTH`, `WRONGPASS`, or a missing or failing credential provider |
   | Timeout | `coordination.timeout` | `operationTimeout`, or the deadline, passed |
   | Cancelled | `coordination.cancelled` | The caller's or request's token fired |
   | Lease lost | `coordination.lease_lost` | Renewal without holding the token |
   | Fencing rejected | `coordination.fencing_rejected` | Fencing guard rule 6 |
   | Capability unsupported | `coordination.capability_unsupported` | Capability check rules 4 and 5, or a lease from another coordinator given to the fencing guard (Leases rule 6) |
   | Scope violation | `coordination.scope_required` | Scope and keys rule 1, raised before I/O |
   | Unexpected | `core.internal` | Anything else |

3. The adapters never retry. A write whose outcome is unknown is not retried by anything in this contract (FR-022).
4. When both could apply, a fired token is a timeout if `operationTimeout` or the deadline fired it, and a cancellation if the caller's or the request's token did, as ISK-0004 research R7 decides for persistence.

## Health

*Review owner: Architecture (ADR) owner.*

1. `ICoordinationHealthProbe.CheckAsync` checks each distributed coordinator in use, Redis then PostgreSQL, and reports the first unhealthy result. It returns within `healthProbeTimeout` plus a fixed 2-second allowance, a safety bound, and never throws for a coordinator condition. Liveness never calls it (FR-025).
2. Reasons:

   | Reason | Status | When | Operator action |
   |---|---|---|---|
   | `ok` | healthy | Every distributed coordinator in use answers | |
   | `not_configured` | healthy | No primitive uses a distributed coordinator | None, unless the topology is `multi_replica` |
   | `not_configured` | unhealthy | A primitive uses `redis` or `postgresql` without its settings | Configure the coordinator |
   | `unreachable` | unhealthy | Cannot connect, or the connection broke | Check the network path, the server, and TLS |
   | `authentication_failed` | unhealthy | Rejected credentials, or no or a failing credential provider | Check the ACL user and its secret reference |
   | `timeout` | unhealthy | The probe's timeout fired | Check server load and the network |
   | `read_only` | unhealthy | Redis answers as a replica | Wait for failover to finish, or point at the primary |
   | `unexpected` | unhealthy | Anything else | Read event 5010 |

3. ISK-0007 maps reasons onto readiness.

## Configuration

*Review owner: Architecture (ADR) owner; Security owner for TLS and passwords.*

The namespace `coordination`, registered by `Iskender.Infrastructure`, has the fields in [data-model.md](../data-model.md#configuration). There is no connection string field. `redis.tlsMode` defaults to `require`, which validates the certificate chain against the system trust store and each endpoint's own host name against its certificate, and `disable` is development-only (FR-027).

The adapter's Redis ACL user needs only the key pattern `~isk:v1:*` and the commands `EVALSHA`, `EVAL`, `SCRIPT LOAD`, `PING`, `ROLE`, and `HELLO`, plus the commands its scripts call (`GET`, `SET`, `DEL`, `INCR`, `PEXPIRE`, `PTTL`, `HGET`, `HSET`, and `HMGET`). It never needs `CONFIG`, `KEYS`, `FLUSHALL`, `SELECT`, or any administrative command: it always uses database 0, and there is no database field. The test fixture creates exactly this user.

## Credentials

*Review owner: Security owner.*

1. The Redis adapter gets its password from the host's `ICoordinationCredentialProvider`, passing `redis.password`'s `SecretReference`, when it builds its connection, and again after an authentication failure. It caches nothing else and logs nothing. The client cannot resolve a password per physical connection, which is the one difference from the persistence contract.
2. No implementation ships outside test projects. Until ISK-0021 provides one, a production Redis coordinator cannot connect, and the probe reports `authentication_failed`.
3. `CoordinationCredential.ToString()` returns `[redacted]`.

## Error codes

All are added to `ErrorCodes` with `since: 4`, raising `core-contracts` from 3 to 4 with compatible changes only (FR-023).

| Code | Category | Safe message |
|---|---|---|
| `coordination.unavailable` | `internal` | The coordination service is unavailable. |
| `coordination.timeout` | `deadline_exceeded` | The coordination service did not respond in time. |
| `coordination.cancelled` | `cancelled` | The coordination operation was cancelled. |
| `coordination.lease_lost` | `conflict` | The lease is no longer held. |
| `coordination.fencing_rejected` | `conflict` | A newer holder has already changed this resource. |
| `coordination.capability_unsupported` | `internal` | This feature is not available in the current deployment. |
| `coordination.scope_required` | `internal` | A coordination operation was attempted without an owner scope. |

## Diagnostics

*Review owner: Privacy/Operations owner.*

Fields and labels are limited to feature, module, primitive, operation, outcome, failure class, consistency level, coordinator kind, and reason code. No scope ID, key, hash, cached value, host, or credential appears in any field or label (FR-026, FR-028).

| Event ID | Name | Level | When |
|---|---|---|---|
| 5001 | `CoordinationFeatureAccepted` | Information | The checker accepted a feature, with its mode and levels |
| 5002 | `CoordinationFeatureRefused` | Warning | The checker refused a feature, with its reason |
| 5003 | `CoordinationConnectionFailed` | Warning | A coordinator connection attempt failed, with its class |
| 5004 | `CoordinationOutcomeUnknown` | Error | A write's outcome is unknown |
| 5005 | `CoordinationLeaseLost` | Warning | Renewal found the lease lost |
| 5006 | `CoordinationFencingRejected` | Warning | The fencing guard rejected a token |
| 5007 | `CoordinationCacheInvalidated` | Debug | A namespace generation was bumped |
| 5008 | `CoordinationCacheDegraded` | Warning | A cache read became a miss because of a failure |
| 5009 | `CoordinationHealthChanged` | Information | The probe's reason differs from its last result |
| 5010 | `CoordinationUnexpectedFailure` | Error | A failure classified as unexpected, with its class only |
| 5011 | `CoordinationSingleInstanceChanged` | Warning | The instance lease state changed |

Activities come from `ActivitySource("Iskender.Coordination")` (`coordination.operation`, `coordination.capability_check`), and metrics from `Meter("Iskender.Coordination")` (`iskender.coordination.operations`, `iskender.coordination.operation.duration`, `iskender.coordination.lease.lost`, `iskender.coordination.fencing.rejected`, `iskender.coordination.capability.refused`). The Redis client's own logging is not connected.

## Test fixture

*Review owner: Performance owner (+ QA).*

1. `tests/Iskender.Coordination.Conformance` holds the conformance suite. Every implementation runs it, and distributed ones run it from two independent clients (FR-029).
2. `tests/Iskender.Infrastructure.Redis.Tests` provides the Redis fixture: one container per run, from the official Valkey 8 image pinned by digest. Later specs reuse it by referencing its fixture types.
3. Without a container runtime, container-backed tests are skipped, each with its reason. When `CI` is `true`, they fail instead (FR-031).

## Threat model and known limits

### What the rules defend against

The Negative Security and Failure Scenarios table in [spec.md](../spec.md) lists each threat with the requirement that covers it. This contract's rules implement them: per-replica limits multiplying a real limit (Requirements 3, 5), a forgotten topology (Requirements 2), a misdeclared single instance (Requirements 6), outage turning into "unlimited" (Requirements 1, Failures), a paused lease holder (Leases 2, 3, 6), cross-tenant state (Scope and keys), tenant IDs in key space (Scope and keys 4), double counting (Failures 3), stale cache entries (Versioned cache), and credential leaks (Credentials, Diagnostics).

### Known limits

- **Redis failover**: under asynchronous replication, a failover can lose acknowledged writes, grants, and token increments. That is why Redis declares `leased` and `best_effort` (Consistency rule 2). Redis Cluster and Sentinel management is post-MVP.
- **Unverified single instance**: without PostgreSQL, a `single_instance` declaration cannot be checked.
- **Redis credential rotation**: a rotated password takes effect when the connection is rebuilt after an authentication failure, not on every physical connection.
- **Cancellation**: the Redis client cannot cancel a command already sent, so a cancelled write's outcome is unknown. The one exception is a lease acquire: the adapter then releases the lease by its random holder, bounded by `operationTimeout`, so a grant the server made is undone and no other holder's lease is touched.
- **Expired PostgreSQL rows**: they are ignored at once, but only deleted by the next write to the key or by `PurgeExpiredAsync`, one scope per call, which ISK-0009 schedules across scopes.
- **Redis eviction**: token counters and namespace generations have no expiry. An eviction policy that can evict them (`allkeys-*`) could repeat a token or serve an invalidated entry, so Redis must run with `noeviction` or a `volatile-*` policy. The adapter cannot check this without `CONFIG`, which its ACL user does not get, so the runbook requires it.
- **Coordinator clock steps**: expiry follows the coordinator's clock, so a step of that clock moves lease expiry by the same amount. Durable writes stay protected, because the fencing guard compares tokens, not times.
- **Records that never expire**: fence records, namespace generations, and Redis token counters stay until removed. Their number grows with the lease and namespace names modules use, which modules choose, not callers.
- **No per-scope key count**: a scope's value keys and cache entries are bounded by expiry and value size, and the in-memory store by `cache.maximumEntries`, but this spec sets no count of keys per scope. Redis memory is sized by the operator. Per-tenant limits belong to the specs that consume these ports.

## Left to later specs

| Item | Spec |
|---|---|
| Composition, starting the single-instance guard, OpenTelemetry subscription | ISK-0006, ISK-0007 |
| Mapping refusals and health reasons onto start-up and readiness | ISK-0007 |
| Local Compose Redis | ISK-0008 |
| Worker leases, and scheduling `PurgeExpiredAsync` across scopes | ISK-0009 |
| Key cache invalidation | ISK-0017 |
| The production credential provider | ISK-0021 |
| Rate windows and token reservations | ISK-0060 |
| Quotas and concurrency caps | ISK-0061 |
| Distributed breakers | ISK-0066 |
