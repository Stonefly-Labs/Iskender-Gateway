# Data Model: ISK-0005 — Coordination & Redis Capability Contracts

Types are named as the plan intends to build them. Port types are in `Iskender.Domain.Ports`, with no driver, Redis, PostgreSQL, or ASP.NET Core type in any signature (FR-001). The reasons are in [research.md](research.md), and the rules a caller relies on are in [contracts/coordination.md](contracts/coordination.md).

## Ports: `Iskender.Domain.Ports`

### Scope and names

| Type | Shape |
|---|---|
| `CoordinationScope` (sealed class, no public constructor) | `ForResource(ScopedResourceReference)`, `ForRequest(GatewayExecutionContext)`, `Operator`, `From(UnitOfWorkScope)`. `UnitScope`, `Owner`, `Deadline`, `RequestCancellation`. `null` or an unresolved owner → `coordination.scope_required`. No equality |
| `CoordinationName` (sealed class, value equality) | `Module` (module name pattern), `Name` (operation pattern, as `AdvisoryLockName`) |
| `CacheNamespace` (sealed class, value equality) | Same shape as `CoordinationName`. An entry is named by a namespace and a `Name` |
| `CoordinationKey` (static) | `string Hash(OwnerScope scope, string module, string name)`: lowercase hex SHA-256 (research R4). `string ScopeKey(OwnerScope)`: the canonical form. Pure, unit-tested with published vectors |

### Levels, requirements, and the check

| Type | Shape |
|---|---|
| `ConsistencyLevel` (enum) | `Local`, `BestEffort`, `Leased`, `Strict`. `Satisfies(required)` implements the order in research R5 |
| `CoordinationPrimitive` (enum) | `AtomicOperations`, `Leases`, `Fencing`, `CacheInvalidation` |
| `CoordinatorKind` (enum) | `Local`, `Redis`, `PostgreSql` |
| `CoordinatorCapabilities` (sealed record) | `Kind`, and `ConsistencyLevel? LevelOf(primitive)`. `null` means not offered |
| `CoordinationTopology` (enum) | `SingleInstance`, `MultiReplica` |
| `OutageBehavior` (sealed class) | `FailClosed`, or `Degrade(string signal)` with `signal` matching the operation pattern |
| `FeatureRequirement` (sealed record) | `Name`, `Module`, `IReadOnlyDictionary<CoordinationPrimitive, ConsistencyLevel> Minimums` (non-empty), `MandatoryPath` (`bool`), `Outage` |
| `SingleInstanceState` (enum) | `NotApplicable`, `Held`, `HeldByOther`, `Lost`, `Unverified` |
| `CapabilityReason` (enum) | `Accepted`, `UnsupportedConsistency`, `CoordinatorNotConfigured`, `LocalInMultiReplica`, `DegradeNotPermitted`, `SingleInstanceViolated`. Written as the snake-case codes in TR-7 |
| `FeatureCapability` (sealed record) | `Feature`, `Accepted` (`bool`), `Reason`, `Mode` (`single_instance` or `distributed` when accepted), and the `Level` granted per primitive |
| `CapabilityReport` (sealed class) | The `FeatureCapability` per feature, the topology, and the single-instance state |
| `CoordinationCapabilityChecker` (static) | `CapabilityReport Check(IReadOnlyCollection<FeatureRequirement>, CoordinationTopology, IReadOnlyDictionary<CoordinationPrimitive, CoordinatorCapabilities?>, SingleInstanceState)`. Pure, applying the rule order in research R5. A malformed set of requirements throws `coordination.capability_unsupported` |
| `CoordinationGate` (sealed class) | Built from the report, the configured ports, and an `ISingleInstanceStatus`. `FeatureCoordination ForFeature(string name)` |
| `FeatureCoordination` (sealed class) | `Atomic`, `Leases`, `Fencing`, `Cache`: facades over the ports that check acceptance and the single-instance state before each call (FR-009) |

### Atomic operations

`IAtomicOperations`, every method taking a `CoordinationScope`, a `CoordinationName`, and a `CancellationToken`:

| Method | Result | Rule |
|---|---|---|
| `AddAsync(delta, expiry)` | `CoordinationValue` | Adds a signed `long`. Creates the key with `expiry` if absent. Overflow → `core.value_invalid`, nothing changes |
| `ReserveAsync(amount, limit, expiry)` | `Reservation` | `amount > 0`, `limit ≥ 0`. Granted only if `value + amount ≤ limit` with no overflow |
| `ReleaseAsync(amount)` | `CoordinationValue?` | Subtracts, never below 0, and returns the new value. An absent key stays absent and returns `null`. Not tied to a reservation and not idempotent: the caller releases what it reserved, once |
| `CompareAndSetAsync(expectedVersion, value, expiry)` | `bool` | `expectedVersion` 0 creates. True only when the stored version matches |
| `ReadAsync()` | `CoordinationValue?` | `null` when absent or expired |

`CoordinationValue` is `Value` (`long`) and `Version` (`long`, raised by every change). `Reservation` is `Granted` (`bool`) and `Value`. Expiry is set when a key is created and kept by later operations. It must be positive and at most `keys.maximumExpiry`.

### Leases and fencing

| Type | Shape |
|---|---|
| `ILeaseCoordinator` | `AcquireAsync(scope, name, duration, LeaseWait, ct) → LeaseAcquisition`; `RenewAsync(Lease, duration, ct) → Lease`; `ReleaseAsync(Lease, ct)` |
| `LeaseWait` | `TryOnly`, or `Bounded(TimeSpan)` with `0 < t ≤ leases.maximumWait` |
| `LeaseAcquisition` | `Acquired(Lease)` or `Contended`. Contention is a result, not an error |
| `Lease` (sealed class) | `Scope`, `Name`, `Token`, `Holder` (random 128-bit), `Issuer` (`CoordinatorKind`, set by the coordinator that granted it), `Remaining` (research R12), `IsSafeToAct` (`Remaining > leases.safetyMargin`). Only coordinators create it, through the public `Lease.Grant`, which is coordinator-only by convention within the process (contract Known limits). `ToString()` shows no scope ID |
| `FencingToken` (readonly struct) | A positive `long`, ordered. Zero or negative → `core.value_invalid` |
| `IFencingGuard` | `ValueTask AcceptAsync(IUnitOfWork unit, CoordinationName resource, Lease lease, CancellationToken)`. Runs in the caller's unit, in the unit's own scope, which must be the lease's scope (otherwise `coordination.scope_required`). A lease whose `Issuer` is not `PostgreSql` → `coordination.capability_unsupported` before any write. A stale token or another lease's token → `coordination.fencing_rejected`, and the unit faults |

Lease state transitions:

```text
(none) ──Acquire──► Held ──Renew (holder matches)──► Held
                     │──Release──────────────────────► (none)
                     │──expiry, or another Acquire after expiry──► Lost
Lost ──Renew──► coordination.lease_lost        Lost ──Release──► no-op
```

### Versioned cache

| Type | Shape |
|---|---|
| `IVersionedCache` | `TryGetAsync(scope, ns, name, expectedVersion, ct) → CacheLookup`; `SetAsync(scope, ns, name, version, ReadOnlyMemory<byte> value, expiry, CacheStamp stamp, ct) → bool`, `true` only when stored; `InvalidateAsync(scope, ns, ct)` |
| `CacheLookup` | `Hit(ReadOnlyMemory<byte>)`, or `Miss` with a `CacheStamp?`. A coordinator failure is a `Miss` with no stamp |
| `CacheStamp` (readonly struct) | The namespace generation a miss observed. Opaque to callers by convention: `CacheStamp.Of` is public for the coordinators and is not called by module code (contract Known limits). `SetAsync` stores only when the namespace's generation still equals it, and otherwise stores nothing and returns `false` |

A value larger than `cache.maximumValueBytes` → `core.value_invalid`, nothing stored.

### Failures, health, and credentials

| Type | Shape |
|---|---|
| `CoordinationFailureClass` (enum) | `Unavailable`, `Timeout`, `Cancelled`, `LeaseLost`, `FencingRejected`, `CapabilityUnsupported`, `ScopeViolation`, `AuthenticationFailed`, `Unexpected`. Carried as the `failure_class` diagnostics value, with `outcome` = `unknown` when a write's outcome is unknown (research R11) |
| `ICoordinationHealthProbe` | `ValueTask<CoordinationHealthResult> CheckAsync(CancellationToken)`. Never throws for a coordinator condition. The caller's token → `coordination.cancelled` |
| `CoordinationHealthResult` | `Status` (`Healthy`, `Unhealthy`), `Reason` |
| `CoordinationHealthReason` (enum) | `Ok`, `NotConfigured`, `Unreachable`, `AuthenticationFailed`, `Timeout`, `ReadOnly`, `Unexpected`. `Healthy` only with `Ok`, or with `NotConfigured` when nothing distributed is in use |
| `ICoordinationCredentialProvider` | `ValueTask<CoordinationCredential> GetCredentialAsync(CoordinationCredentialRequest, CancellationToken)` |
| `CoordinationCredentialRequest` | `Reference` (`SecretReference`) |
| `CoordinationCredential` | `Password`. `ToString()` returns `[redacted]` |
| `ISingleInstanceStatus` | `SingleInstanceState State` |

## Infrastructure: `Iskender.Infrastructure.Coordination`

| Type | Role |
|---|---|
| `CoordinationSectionSchema`, `CoordinationSettings` | The `coordination` section (research R16). Settings hold `SecretReference`s, never passwords |
| `LocalAtomicOperations`, `LocalLeaseCoordinator`, `LocalVersionedCache` | Local substitutes declaring `Local` for every primitive except fencing, which they do not offer (contract Consistency rule 2). One lock per key, `TimeProvider` time, a process-wide token counter |
| `InMemoryCacheStore` | Bounded, least-recently-used value store, shared by the local cache and the PostgreSQL cache |
| `SingleInstanceGuard` | `ISingleInstanceStatus`, from a PostgreSQL lease (research R14) |
| `CoordinationLog`, `CoordinationTelemetry` | Events and instruments (research R18) |

## Redis adapter: `Iskender.Infrastructure.Redis`

| Type | Role |
|---|---|
| `RedisCoordinator` | The public entry point: builds the three ports and the health probe over one shared connection from `CoordinationSettings`, an `ICoordinationCredentialProvider`, an `ILogger`, and a `TimeProvider`, and disposes that connection (TR-2) |
| `RedisConnection` | The multiplexer, TLS, credential resolution, and rebuild after an authentication failure (R11) |
| `RedisScripts` | The Lua scripts and their SHA-1s (R10) |
| `RedisAtomicOperations`, `RedisLeaseCoordinator`, `RedisVersionedCache` | The ports. Capabilities as research R9 |
| `RedisFailureClassifier` | R11 |
| `RedisHealthProbe` | `PING` and `ROLE` |

## PostgreSQL coordinator: `Iskender.Infrastructure.PostgreSql.Coordination`

| Type | Role |
|---|---|
| `CoordinationMigrations` | `public static MigrationModule Module`: `coordination`, range `[1, 1]`, no dependencies, scripts embedded in the adapter |
| `CoordinationTables` | The four `ScopeKeyedTable` descriptors |
| `PostgreSqlAtomicOperations`, `PostgreSqlLeaseCoordinator`, `PostgreSqlVersionedCache` | The ports, `Strict` for every primitive (R8, R13) |
| `PostgreSqlFencingGuard` | `IFencingGuard` |
| `CoordinationMaintenance` | `PurgeExpiredAsync(scope, batch)`: deletes expired `coordination_values` and `leases` rows in that scope only, under row-level security (FR-013a) |

Adapter root, from persistence contract version 2: `ScopeKeyedTable(module, table)`, a third `TableAccess` kind usable in every scope, and the migration runner's policy check extended to `scope_key` tables.

## Database objects (`isk_coordination`)

| Object | Columns | Key |
|---|---|---|
| `coordination_values` | `scope_key text`, `key_hash text`, `value bigint`, `version bigint`, `expires_at timestamptz` | `(scope_key, key_hash)` |
| `leases` | `scope_key`, `key_hash`, `holder uuid`, `token bigint`, `expires_at` | `(scope_key, key_hash)` |
| `fences` | `scope_key`, `resource_hash text`, `lease_hash text`, `highest_token bigint` | `(scope_key, resource_hash)` |
| `cache_generations` | `scope_key`, `namespace_hash text`, `generation bigint` | `(scope_key, namespace_hash)` |
| Sequence `fencing_tokens` | | `USAGE` granted to `iskender_runtime` |

Every column is `not null`. Each table has row-level security enabled and forced, with the scope-keyed policy, and default DML privileges for `iskender_runtime`, under the ISK-0004 rules.

## Redis keys

```text
isk:v1:<module>:{<hash>}:v          value hash {v, ver}
isk:v1:<module>:{<hash>}:l          lease "holder:token"
isk:v1:<module>:{<hash>}:t          token counter
isk:v1:<module>:{<nshash>}:g        namespace generation
isk:v1:<module>:{<nshash>}:e:<hash> cache entry hash {ver, gen, val}
```

The `:v`, `:l`, and `:e:` keys always have an expiry. The `:t` and `:g` keys never do, because a counter or generation that restarted would repeat (FR-013).

## Configuration

Namespace `coordination`, version 1. Every default is a safety bound, not a performance figure.

| Field | Type | Default | Notes |
|---|---|---|---|
| `topology` | enumeration `single_instance`, `multi_replica` | `multi_replica` | |
| `atomic.coordinator` | enumeration `local`, `redis`, `postgresql` | `local` | |
| `leases.coordinator` | same | `local` | |
| `cache.coordinator` | same | `local` | |
| `leases.minimumDuration` | duration 100 ms–1 h | 1 s | |
| `leases.maximumDuration` | duration 1 s–24 h | 10 m | Not below `minimumDuration` |
| `leases.safetyMargin` | duration 0–1 m | 2 s | |
| `leases.maximumWait` | duration 0–5 m | 30 s | |
| `keys.maximumExpiry` | duration 1 s–400 d | 35 d | Long enough for a calendar-month quota window |
| `cache.maximumValueBytes` | integer 1–1,048,576 | 65,536 | |
| `cache.maximumEntries` | integer 1–1,000,000 | 10,000 | In-memory store only |
| `operationTimeout` | duration 100 ms–1 m | 5 s | |
| `healthProbeTimeout` | duration 1 s–1 m | 5 s | |
| `instanceLease.duration` | duration 5 s–5 m | 30 s | |
| `redis.endpoints` | array of string `host:port`, 0–16 items | | Required in effect when any primitive uses `redis` (research R16) |
| `redis.username` | string | optional | ACL user |
| `redis.password` | `secretRef` | optional | |
| `redis.tlsMode` | enumeration `require`, `disable` | `require` | `disable` is development-only |
| `redis.connectTimeout` | duration 1 s–1 m | 5 s | |

## Test fixtures

| Item | Contents |
|---|---|
| Conformance suite | Abstract test classes over the ports, with one or two clients from `ICoordinatorUnderTest` |
| Requirement table | Every combination of topology, required level, declared level, outage behavior, mandatory flag, and single-instance state, with the expected result and reason (SC-002) |
| Fixture features | `fixture.strict_counter`, `fixture.leased_lease`, `fixture.best_effort_cache`, `fixture.hard_budget` (mandatory, `degrade`) |
| Valkey fixture | One container per run, pinned by digest, skipped with a reason without a container runtime, failing when `CI` is `true` |
| Credential provider | A test `ICoordinationCredentialProvider` returning the container's ACL password |
| Canary | One value used as Redis host alias, user name, password, an organization ID, and a cached value (SC-005) |

## Relationships

```text
FeatureRequirement* ─┐
CoordinationTopology ├─► CoordinationCapabilityChecker ─► CapabilityReport ─► CoordinationGate
CoordinatorCapabilities per primitive ┘                                 │
SingleInstanceState (SingleInstanceGuard) ─────────────────────────────┘
CoordinationGate.ForFeature ─► FeatureCoordination ─► IAtomicOperations | ILeaseCoordinator | IVersionedCache
                                                       (Local | Redis | PostgreSql)
Lease ──token──► IFencingGuard ──inside──► IUnitOfWork (ISK-0004) ──► isk_coordination.fences
CoordinationScope ──wraps──► UnitOfWorkScope ──► OwnerScope (ISK-0002)
```
