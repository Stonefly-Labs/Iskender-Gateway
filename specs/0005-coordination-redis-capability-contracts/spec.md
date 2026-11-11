# Feature Specification: ISK-0005 — Coordination & Redis Capability Contracts

**Feature Branch**: `feature/0005-coordination-redis-capability-contracts`

**Created**: 2026-11-11

**Status**: Draft

**Input**: User description: "Create the specification **ISK-0005 — Coordination & Redis Capability Contracts** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Establish optional Redis and coordination ports with declared consistency and multi-replica capability checks**. In-scope behaviors and artifacts: **Atomic operations interface; leases; fencing; cache versioning; outage classification; local substitutes**. Explicitly exclude: **No claim local memory is production-grade distributed enforcement**. Prerequisites: **ISK-0002, ISK-0004**; do not assume unavailable implementation already exists. ISK-0004 already published transaction-scoped PostgreSQL advisory locks through `IUnitOfWork.AcquireAdvisoryLockAsync` in `Iskender.Domain.Ports`, documented as mutual exclusion inside one database during one transaction and not as a lease, a fencing token, or a distributed lock, and only its migration runner holds a session-scoped lock. Follow the current persistence contract, linked from the Persistence, publication and distributed coordination section of @docs/03-domain-and-technical-architecture.md: build leases and fencing beside those locks rather than on them, add no session-scoped lock for modules, and settle that spec's assumption AV-3. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Distributed feature refuses unsupported coordinator and lease fencing tests pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify."

## Overview

The constitution is present at `.specify/memory/constitution.md` (version 1.1.0) and governs this spec.

Iskender has no coordination code yet. Redis is an approved *option* for hot and ephemeral coordination, never a replacement for PostgreSQL as the source of truth (ADR-0006, Constitution II), and a distributed control must never be silently weakened when a capable coordinator is absent (Constitution II and VII, `docs/03` Persistence, `docs/08` Coordination and consistency). ISK-0001 reserved `Iskender.Domain.Ports` as the home of shared coordination ports, with this spec as their signature owner, and put `StackExchange.Redis` in the `driver` category that only an `Iskender.Infrastructure.<Adapter>` project may reference. ISK-0004 published the unit of work, transaction-scoped advisory locks that are explicitly **not** leases, fencing tokens, or distributed locks, the PostgreSQL adapter, its migration framework, and its Testcontainers fixture.

Every later feature that coordinates across replicas needs the same foundation underneath it: rate and token limits (ISK-0060), quotas and concurrency leases (ISK-0061), distributed circuit breakers (ISK-0066), background work leases (ISK-0009), API key cache invalidation (ISK-0017), and readiness (ISK-0007). This spec gives them:

1. **Coordination ports**: an atomic operations interface, leases with fencing tokens, and a versioned cache, each keyed by owner scope so that one tenant can neither read nor disturb another's coordination state.
2. **Declared consistency**: every coordinator implementation declares, per primitive, the consistency it actually provides, and every consuming feature declares the consistency it requires and what it does when the coordinator fails.
3. **Multi-replica capability checks**: a start-up check that refuses to run a feature whose requirement the configured coordinator and declared topology cannot meet, with a safe reason code, instead of quietly running it on weaker guarantees.
4. **Outage classification**: a fixed classification of coordinator failures into ISK-0002 error codes, including an unknown-outcome marker, so that a consuming feature applies its own declared fail-closed or degraded behavior.
5. **Local substitutes**: in-process implementations of every port, for tests and single-instance deployments, which declare themselves `local` and can never satisfy a multi-replica requirement.
6. **Fixtures**: a coordinator conformance suite that every implementation must pass, and a Redis Testcontainers fixture beside ISK-0004's PostgreSQL one.

This spec defines no rate algorithm, quota window, budget, breaker, or worker. Those belong to the specs above, which use these ports.

Project, package, and type names appear in this spec because they are its subject, not a choice of implementation.

## Decision Classification

### Accepted decisions (already approved; this spec applies them)

| Decision | Source |
|---|---|
| PostgreSQL is the source of truth. Redis is optional, used for hot and ephemeral coordination when enabled, and never replaces authoritative records. Strong distributed controls require a capable backend, with no silent weakening | ADR-0006, Constitution II, `docs/03` Persistence |
| Without Redis, a feature that needs distributed exactness must use an equivalently correct alternative or reject the unsupported deployment topology | `docs/03` Persistence |
| Each feature documents whether it provides strict, leased, or best-effort semantics. Replica failure needs expiry, fencing, and reconciliation. No architecture relies on node affinity to enforce tenant budgets | `docs/08` Coordination and consistency |
| A local in-memory limit is a single-instance feature only. Rate and limit storage failures cannot silently become unlimited in a multi-replica deployment. A hard budget check fails closed when coordination cannot establish the remaining balance | `docs/08` Distributed request and token rate enforcement, Budgets; Constitution V and VII |
| Concurrency uses atomic leases with fencing and expiry, and robust release on disconnection or crash | `docs/08` Quotas and concurrency |
| Multi-replica breakers need a distributed coordinator or a declared weaker mode that is never presented as a distributed guarantee | `docs/08` Health and circuit breakers |
| Counter keys never encode raw secrets | `docs/08` Distributed request and token rate enforcement |
| Explicit modules with preserved dependency direction. Control plane and data plane are independently deployable, and embedded hosting must not take global service ownership | ADR-0003, ADR-0004, Constitution II |
| Organization → project → resource isolation is enforced at every persistence, query, worker, job, and stream boundary. The owner scope comes from trusted sources only, never from caller-supplied values | ADR-0005, Constitution V, ISK-0002 contract |
| Shared coordination ports live in `Iskender.Domain.Ports`, with ISK-0005 as signature owner. `StackExchange.Redis` is a `driver` package that only an `Iskender.Infrastructure.<Adapter>` project may reference | ISK-0001 module dependency rules contract, Port homes and Denied packages |
| Advisory locks are mutual exclusion inside one PostgreSQL database during one transaction, not a lease, fencing token, or distributed lock. Modules get transaction-scoped locks only, and the migration lock is the only session-scoped lock. Leases and fencing belong to ISK-0005 | ISK-0004 persistence contract, Advisory locks rules 1–5 |
| A change to a persistence contract rule is made only by a later spec's revision of that contract, with a version increment and repointed links | ISK-0004 persistence contract, Changing this contract |
| New error codes go in `ErrorCodes` under existing ISK-0002 categories, and any change to `Iskender.Core.Contracts`' public surface increments `core-contracts`, checked by the compatibility harness | ISK-0002 contract, Versioning policy |
| Module settings are a namespaced ISK-0003 section with a typed schema. Secrets are `secretRef` placeholders, never values. Development-only values are rejected outside `development` | ISK-0003 configuration contract |
| Readiness covers the coordination backend when an enabled capability needs it, separately from liveness, with safe reason codes | `docs/04` Deployment invariants |
| Network links to Redis are protected. No cloud SDK in domain or contract projects, and a local path builds without cloud credentials | `docs/04` Deployment invariants, Constitution II |
| The Redis client library is recommended, not approved. ISK-0005 tests coordination behavior and proposes an ADR if the choice affects public contracts | `docs/04` Questions reserved, `docs/16` Recommendations table, Constitution IX |
| Integration tests run Redis and PostgreSQL through Testcontainers. Two-replica tests cover distributed quotas, budgets, and breakers, and chaos tests cover Redis failure | `docs/12` Test matrix and Scalability, Constitution VIII |
| Payload logging is off by default, and metric cardinality is bounded and sensitive-safe | Constitution V and VIII |
| No numeric performance figure is asserted without a reviewer-approved baseline | Constitution VIII |

### Technical recommendations (proposed by this spec; reviewed in `clarify`)

| ID | Recommendation | Why | Alternative rejected |
|---|---|---|---|
| TR-1 | **Decided by the maintainer in `specify` on 2026-11-11:** ISK-0005 ships three coordinator kinds. The **local substitutes** declare `local`. The **Redis adapter** offers atomic operations, leases, and the cache at the levels the plan proves (AV-3). The **PostgreSQL coordinator** offers `strict` atomic operations and leases, with fencing tokens from a durable monotonic source, in tables of its own beside the advisory locks and never on them. It offers no cache. Together they settle the register's "strong-consistency coordinator fallback when Redis is not deployed": a multi-replica deployment without Redis runs a feature on PostgreSQL when the feature's requirement allows it, and Redis is chosen for throughput, not for correctness. The PostgreSQL coordinator's tables and the fence records (TR-10) live in a schema `isk_coordination`, added through a revision of the persistence contract (FR-024) | ISK-0009's worker leases and ISK-0061's concurrency leases then work in every multi-replica deployment, Redis or not, and `docs/03`'s "equivalently correct alternative" exists | Redis and local only, so a multi-replica deployment without Redis refuses every distributed feature; PostgreSQL and local only, which defers the Redis adapter this spec's scope names |
| TR-2 | The coordination ports go in `Iskender.Domain.Ports`: an atomic operations port, a lease port, a fencing guard, a versioned cache port, a coordinator capability declaration, a feature requirement declaration, a capability checker, a coordination health probe, and the failure classification they report. No port signature names a driver, Redis, PostgreSQL, or ASP.NET Core type. The local substitutes live in `Iskender.Infrastructure`, which needs no driver. The Redis adapter is a new embeddable project `Iskender.Infrastructure.Redis`, the only production project that references the Redis client. The PostgreSQL coordinator and the fencing guard live in `Iskender.Infrastructure.PostgreSql`, in a namespace of their own that names only `isk_coordination` | This is where ISK-0001 already placed them, so its rules need no revision, and a consuming module stays testable without a coordinator | Ports in `Iskender.Core.Contracts`, which would make an internal seam a versioned public contract; local substitutes in `Iskender.Domain`, which mixes implementations into the port home |
| TR-3 | Every coordination call takes a **coordination scope** built the same way as ISK-0004's `UnitOfWorkScope`: from a trusted scoped reference, from the request's resolved owner scope, or as the operator scope. A null or unresolved scope fails with `coordination.scope_required` before any I/O. Callers name state by owning module and a name, never by a raw key. The adapter derives the stored key from a versioned prefix, the scope's canonical form, the module, and the name through SHA-256, and stores only the module in clear text, so no organization ID, project ID, API key, or other secret appears in a coordinator's key space | Redis has no row-level security, so tenant isolation must come from the key itself. A caller that cannot supply a raw key cannot reach another scope's state, and hashing keeps tenant IDs out of `MONITOR` output and key listings | Caller-built key strings, which are one typo away from a cross-tenant collision; tenant IDs in clear text in keys |
| TR-4 | **Consistency levels** are a fixed, ordered set, declared per primitive (atomic operations, leases, fencing, cache): `local` (exact within one process only), `best_effort` (shared across replicas, but an acknowledged update may be lost or applied twice on coordinator restart or failover), `leased` (shared and atomic while the coordinator stays up; on failover, grants and updates may be lost, bounded by lease expiry, and protected writes rely on fencing), and `strict` (linearizable per key, and no acknowledged update or fencing token is lost or repeated across coordinator restart or failover). A coordinator implementation declares the level it actually provides, and when in doubt declares the weaker one | `docs/08` requires each feature to say whether it is strict, leased, or best-effort. A fixed vocabulary makes the start-up check mechanical and reviewable | Free-text guarantees; a boolean "distributed" flag, which hides the difference between leased and strict |
| TR-5 | Each consuming feature registers a **requirement declaration**: its name, owning module, the primitives it uses, the minimum consistency level for each, and its **outage behavior**, which is either `fail_closed` or `degrade` with a named, safe signal. A feature in a mandatory security or hard-budget path cannot declare `degrade` (Constitution V and VII), and the checker rejects such a declaration | It puts the decision about outage behavior where `docs/08` puts it, with the feature, and makes it reviewable in one place | Letting the coordinator decide on failure, which cannot know whether the caller is a hard budget or a soft alert |
| TR-6 | **Topology** is declared in configuration as `single_instance` or `multi_replica`. When it is not declared, it is `multi_replica`, so a forgotten setting fails closed. In `multi_replica`, a `local` primitive satisfies no requirement above `local`. In `single_instance`, a local substitute satisfies `best_effort` and `leased` requirements, never `strict`, and is reported as single-instance, never as distributed | A local counter is correct for one process and wrong for two. Defaulting to `multi_replica` means a deployment that scales out without updating its configuration is refused, not silently weakened | Defaulting to `single_instance`, which fails open when replicas are added |
| TR-7 | The **capability checker** runs once at start-up, after configuration is validated, and compares every registered requirement with the configured coordinator's declared capabilities and the topology. It reports each feature as `accepted` or `refused` with one safe reason code: `unsupported_consistency`, `coordinator_not_configured`, `local_in_multi_replica`, or `degrade_not_permitted`. A refused feature never runs on weaker guarantees. ISK-0007 maps a refusal onto start-up failure or readiness | This is the acceptance evidence "distributed feature refuses unsupported coordinator", made a single deterministic check rather than a scattering of `if` statements | Checking lazily on first use, which fails a live request instead of a deployment |
| TR-8 | The **atomic operations port** offers single-key operations only, each atomic on its coordinator: add a signed delta with an expiry and return the new value; **reserve** an amount only if the result stays within a limit, returning granted or denied and the value, with no read-modify-write window; release a reservation; compare-and-set a value against a version; and read. Every operation takes a deadline and cancellation, and every key has an expiry bounded by configuration. Multi-key transactions are not offered. Window algorithms, rates, and quotas are built on top by ISK-0060 and ISK-0061 | These are the primitives `docs/08` names (atomic scoped counters, reservations). Single-key atomicity works the same in Redis, in a cluster, and in PostgreSQL | Multi-key scripts, which break across Redis cluster slots; shipping rate algorithms here, which belong to ISK-0060 |
| TR-9 | A **lease** is granted for a scope, module, and name with a duration, and carries a **fencing token**: a 64-bit integer, strictly increasing per lease key, never reused even after the lease expires. Acquisition is try-only or waits up to a bound, and a held lease is reported as contention, not as an error. Renewal succeeds only while the caller still holds that token, otherwise it reports `coordination.lease_lost`. Release is idempotent. Expiry is measured on the coordinator's clock, never the caller's, and the port reports the remaining validity so a holder can stop work before expiry with a configured safety margin | Fencing tokens are what make a lease safe when a paused or partitioned holder wakes up after expiry. Using the coordinator's clock removes replica clock skew from the safety argument | Leases without tokens; expiry by caller clock; building leases on advisory locks, which ISK-0004 forbids |
| TR-10 | The **fencing guard** protects a resource in PostgreSQL. Inside a caller's ISK-0004 unit of work, it records, per scope, module, and resource name, the highest fencing token accepted, and atomically accepts a token only if it is greater than or equal to that value. A lower token fails with `coordination.fencing_rejected`, and the caller's write does not commit. It uses ordinary rows in the unit's transaction, not an advisory lock, and adds no session-scoped lock | This is how a lease protects durable state: the store itself refuses the stale holder, whatever the coordinator did. Building it on the unit of work keeps it inside ISK-0004's isolation and transaction rules | Trusting the holder to check its own lease before writing, which fails when it pauses between the check and the write |
| TR-11 | The **versioned cache** stores scoped values with an expiry and a version. A reader states the version it expects (for example a configuration revision or a row version token), and an entry of another version is a miss. A namespace can be invalidated by bumping its generation atomically, which makes every older entry a miss without deleting it. The cache is never authoritative: on any coordinator failure a read is a miss and the caller reads the source of truth. Values are bounded in size, never logged, and a cached value is only ever served within its own scope | It lets ISK-0017 and later specs invalidate across replicas without a scan, and makes a stale entry harmless rather than wrong | Delete-on-change invalidation, which races with a concurrent refill; a cache that serves stale data during an outage |
| TR-12 | Coordinator failures are classified once, in the adapter, into: **unavailable** (cannot connect, connection lost, replica read-only during failover), **timeout**, **cancelled**, **contention** (lease held, reservation denied: a result, not an error), **lease lost**, **fencing rejected**, **capability unsupported**, **scope violation** (raised before I/O), and **unexpected**. A write whose outcome is unknown (connection lost after sending) carries `OutcomeUnknown`, and the adapter never retries it automatically. The adapter never retries any operation on its own | The consumer decides, through its declared outage behavior, what a failure means. An unknown outcome on a counter can mean a double count, so it must be visible | Automatic retries, which can double-reserve; treating an unknown outcome as failure or success |
| TR-13 | Error codes are added to `ErrorCodes` under existing ISK-0002 categories: `coordination.unavailable` (`internal`), `coordination.timeout` (`deadline_exceeded`), `coordination.cancelled` (`cancelled`), `coordination.lease_lost` (`conflict`), `coordination.fencing_rejected` (`conflict`), `coordination.capability_unsupported` (`internal`), and `coordination.scope_required` (`internal`). Each has a safe message with no key, host, or server text. This raises `core-contracts` from 3 to 4 with compatible changes only | No new category is needed, so the ISK-0002 contract needs no revision, and the codes follow ISK-0004's choice of `internal` for a backend outage | A new `coordination_unavailable` category, which needs an ISK-0002 contract revision |
| TR-14 | The coordination health probe reports one status and one reason from a fixed set: `ok`, `not_configured`, `unreachable`, `authentication_failed`, `timeout`, `read_only`, `unexpected`. It has its own timeout, never throws for a coordinator condition, and liveness never calls it. ISK-0007 maps it onto readiness | Operators need to tell an outage from a misconfiguration, and a fixed set is safe to show on a dashboard | Exposing driver error text, which can carry host names |
| TR-15 | The module declares an ISK-0003 namespace `coordination` with the topology (TR-6), the coordinator chosen for each primitive, lease duration bounds and safety margin, maximum key expiry, maximum cached value size, operation and health probe timeouts, and, when Redis is used, endpoints, database index, user name, password as `secretRef`, and TLS mode (default `require`, `disable` development-only). There is no connection string field. Redis credentials come through a host-implemented coordination credential port in `Iskender.Domain.Ports`, on the pattern of ISK-0004's database credential port, with no implementation outside test projects until ISK-0021 | Separate fields let ISK-0003 validate each one and keep the password a reference. Following ISK-0004's credential pattern keeps secret resolution in ISK-0021 | A connection string, which embeds the password |
| TR-16 | Diagnostics use fixed log events with stable IDs in a range of their own: capability check result per feature, coordinator connection failure by class, lease lost, fencing rejection, unknown outcome, cache namespace invalidated, and health reason change. Operations are OpenTelemetry activities and metrics from `Iskender.Coordination` sources, labelled only with module, primitive, operation, outcome, failure class, and consistency level. No label or field holds a scope ID, derived key, cached value, host, or credential | Operators must diagnose a refused feature or a lost lease from logs alone, with bounded cardinality | Logging keys or tenant IDs |
| TR-17 | A **coordinator conformance suite**, written once against the ports, runs against every implementation: local substitutes, and each shipped distributed coordinator through Testcontainers. It covers atomicity under concurrent callers, reservation limits, lease exclusivity, token monotonicity, expiry, renewal after loss, fencing rejection, cache version misses, scope isolation, and classification of a stopped or paused coordinator. Distributed implementations also run it from at least 2 independent clients standing in for replicas. The Redis fixture follows ISK-0004's: one container per run, pinned by digest, skipped with a reason without a container runtime, and failing instead in CI | One suite keeps implementations honest about the level they declare, and gives later specs a ready fixture | A separate test set per implementation, which drifts |

### Assumptions needing validation

| ID | Assumption | Validated by |
|---|---|---|
| AV-1 | The Redis client the plan selects, and every package it brings into a `src/` project, declares an SPDX expression already on the `check-license.sh` list. If not, the list is extended under Product/Charter owner review, as `docs/04` allows | This spec's plan, Product/Charter owner |
| AV-2 | A Redis-compatible server image with an acceptable license for test use can be pinned by digest for the fixture. The image is a test dependency and ships in no package | This spec's plan |
| AV-3 | The plan can prove which consistency level a single-primary Redis deployment provides for each primitive, including whether fencing tokens can repeat after a restart or failover. Where it cannot prove a level, the adapter declares the weaker one (TR-4) | This spec's plan and conformance suite |
| AV-4 | ISK-0007 can map the capability checker's refusals and the coordination health reasons onto start-up failure and readiness without this spec adding host wiring | ISK-0007 |
| AV-5 | ISK-0009 can take worker leases and fence its job writes with these ports alone | ISK-0009 |
| AV-6 | ISK-0060, ISK-0061, and ISK-0066 can build rate windows, token reservations, concurrency caps, and breaker state on the single-key operations in TR-8, without multi-key atomicity | ISK-0060, ISK-0061, ISK-0066 |
| AV-7 | The CI runner's container runtime can run Redis and PostgreSQL containers together, so the conformance suite runs in the existing `dotnet test` check without a new CI check | This spec's plan |

**ISK-0004's AV-3 is settled by this spec.** Its leases and fencing are new ports built beside the advisory locks, not on them. The fencing guard uses ordinary rows inside a unit of work. This spec adds no session-scoped lock, and changes neither `IUnitOfWork.AcquireAdvisoryLockAsync` nor Advisory locks rules 1 to 5 of the persistence contract. The PostgreSQL coordinator's tables and the fence records (TR-1, TR-10) are added through a revision of the persistence contract (FR-024).

### Post-MVP work (named so that it is not designed here)

Multi-key atomic transactions. Redis Cluster and Sentinel topology management beyond what the client library offers. Cross-region or active-active coordination. Coordinator-side rate algorithms as server scripts maintained by Iskender. A management API view of leases and counters. Automatic migration of coordination state between coordinator kinds. A distributed cache of tenant payloads beyond small values.

## Clarifications

### Session 2026-11-11

- Q: The decision register lists the coordinator to use when Redis is not deployed as unresolved. Which coordinators should ISK-0005 ship? → A: Local substitutes, a Redis adapter, and a PostgreSQL coordinator that provides `strict` leases, fencing, and atomic counters in tables beside the advisory locks, so Redis stays optional for correctness (TR-1).

## User Scenarios & Testing *(mandatory)*

The users of this feature are module authors in later specs (governance, routing, background work, identity), operators who deploy Iskender as one instance or as many replicas, and reviewers who need evidence that no distributed guarantee is weakened silently.

### User Story 1 - A distributed feature refuses an unsupported coordinator (Priority: P1)

An operator deploys several data-plane replicas, but configures only the local substitutes, or a coordinator weaker than a feature needs. Rather than enforce a limit per replica and multiply it by the replica count, the gateway refuses that feature at start-up and says why, in a reason code safe to show on a dashboard.

**Why this priority**: This is half of the acceptance evidence, and the core of Constitution VII's "MUST NOT silently weaken distributed guarantees".

**Independent Test**: Register fixture features with different requirements, start the checker under each combination of topology and coordinator, and compare each feature's result and reason with an expected table.

**Acceptance Scenarios**:

1. **Given** topology `multi_replica` and only local substitutes, **When** the checker evaluates a fixture feature requiring `leased` leases, **Then** the feature is `refused` with `local_in_multi_replica`, and calling it fails with `coordination.capability_unsupported` without touching any state.
2. **Given** no topology in configuration, **When** the checker runs, **Then** the topology is treated as `multi_replica`, and the result is the same as in scenario 1.
3. **Given** topology `single_instance` and local substitutes, **When** the checker evaluates features requiring `best_effort`, `leased`, and `strict`, **Then** the first two are `accepted` and reported as single-instance, and the `strict` one is `refused` with `unsupported_consistency`.
4. **Given** a coordinator declaring `leased` for atomic operations, **When** a fixture feature requires `strict` counters, **Then** it is `refused` with `unsupported_consistency`.
5. **Given** a fixture feature marked as a hard-budget path that declares outage behavior `degrade`, **When** the checker runs, **Then** it is `refused` with `degrade_not_permitted`.
6. **Given** a coordinator that meets every requirement, **When** the checker runs, **Then** every feature is `accepted`, and one diagnostic event per feature records its result, level, and reason.

---

### User Story 2 - A stale lease holder cannot corrupt protected state (Priority: P1)

Two replicas compete for the same piece of work. One takes a lease, then pauses (a long garbage collection, a network partition) past the lease's expiry. The other takes the lease and writes. When the first wakes and tries to write, its write is refused, whatever the coordinator remembers.

**Why this priority**: This is the other half of the acceptance evidence, and what ISK-0009 workers and ISK-0061 concurrency leases rely on.

**Independent Test**: With two independent clients against one coordinator and one PostgreSQL fixture database, run the pause-and-expire sequence and many concurrent acquire-write-release cycles, and check that no write with a stale token commits.

**Acceptance Scenarios**:

1. **Given** client A holds a lease with token T, **When** the lease expires and client B acquires it, **Then** B's token is greater than T.
2. **Given** B has written through the fencing guard with its token, **When** A writes with T, **Then** A's write fails with `coordination.fencing_rejected` and A's unit of work commits nothing.
3. **Given** A's lease has expired, **When** A tries to renew it, **Then** renewal fails with `coordination.lease_lost`.
4. **Given** 20 concurrent clients repeatedly acquiring one lease key, writing through the fencing guard, and releasing, **When** they finish, **Then** at no moment did two clients hold the lease, every accepted write's token is greater than or equal to the one before it, and no token was issued twice.
5. **Given** a held lease, **When** another client tries try-only acquisition, **Then** it gets contention immediately, and with a bounded wait it gets the lease no later than the bound after release, or contention at the bound.
6. **Given** a lease released twice, or released after it was lost, **When** the release runs, **Then** it completes without error and does not release another holder's lease.
7. **Given** the same lease name in two organization scopes, **When** both acquire at once, **Then** neither waits for the other and their tokens are independent.

---

### User Story 3 - Counters stay exact under concurrent replicas (Priority: P1)

Rate, token, quota, and concurrency features reserve capacity from a shared counter. Concurrent callers on several replicas never push the counter past its limit and never lose an update, and a coordinator outage is reported, never treated as "unlimited".

**Why this priority**: These primitives carry every distributed limit in the MVP (Constitution VII, `docs/08`).

**Independent Test**: Run concurrent reservations from 2 independent clients against one counter with a limit, and compare granted totals with the limit.

**Acceptance Scenarios**:

1. **Given** a counter with limit 100 and 2 clients each making 100 concurrent reservations of 1, **When** they finish, **Then** exactly 100 are granted, 100 are denied, and the counter reads 100.
2. **Given** a granted reservation, **When** it is released, **Then** the counter falls by that amount, and a later reservation can use it.
3. **Given** 50 concurrent compare-and-set calls on one value with the same expected version, **When** they finish, **Then** exactly one succeeds.
4. **Given** a stopped coordinator, **When** a reservation is attempted, **Then** it fails with `coordination.unavailable` within the operation timeout, and is never reported as granted.
5. **Given** a connection dropped after a reservation was sent, **When** the result returns, **Then** it carries `OutcomeUnknown` and the adapter has not retried it.

---

### User Story 4 - Caches never serve a stale version (Priority: P2)

A module caches a resolved value keyed by a configuration revision. When the revision changes, or the module invalidates the namespace, every replica's next read is a miss, and an outage of the cache only costs a read of the source of truth.

**Why this priority**: ISK-0017 key revocation and configuration activation depend on it, but it never decides correctness on its own.

**Independent Test**: Write entries, change the expected version or bump the namespace generation from another client, and check every read; then stop the coordinator and check reads are misses.

**Acceptance Scenarios**:

1. **Given** an entry stored with version V1, **When** a reader expects V2, **Then** it is a miss.
2. **Given** entries in a namespace, **When** another client bumps the namespace generation, **Then** every read of those entries from any client is a miss.
3. **Given** a stopped coordinator, **When** a read runs, **Then** it is a miss within the operation timeout, and no error reaches the caller beyond a diagnostic event.
4. **Given** an entry stored in organization A's scope, **When** organization B reads the same module and name, **Then** it is a miss.
5. **Given** a value larger than the configured maximum, **When** it is stored, **Then** it is refused and nothing is stored.

---

### User Story 5 - Operators can see coordination health and why a feature was refused (Priority: P2)

An operator deploys with a wrong Redis password, an unreachable endpoint, a coordinator in failover, or no coordinator at all. The health probe and the checker's diagnostics say which, without exposing the host, user, key, or password.

**Why this priority**: ISK-0007's readiness needs it, and an outage without a reason code is slow to recover. It depends on Stories 1 and 3.

**Independent Test**: Run the probe against each condition, and search every output for planted canaries.

**Acceptance Scenarios**:

1. **Given** each of the conditions above, **When** the probe runs, **Then** it reports the matching reason from TR-14 within its timeout plus a fixed allowance.
2. **Given** canary values planted as the Redis host, user name, password, an organization ID, and a cached value, **When** every probe result, error message, log, metric label, and trace attribute from the suite is searched, **Then** no canary is found.

---

### Edge Cases

- A lease is requested with a duration outside the configured bounds: it is refused before any I/O.
- A holder's remaining validity is below the safety margin: the port reports it as not safe to act, and a write through the fencing guard is still checked by token.
- The coordinator restarts and loses state: what a lease or counter keeps depends on the declared level; a `strict` coordinator loses nothing, and the conformance suite checks the declaration.
- The coordinator's clock jumps: expiry uses the coordinator's clock only, so replica clock skew does not change who holds a lease.
- A reservation would overflow a 64-bit counter: it is denied, never wrapped.
- A key's expiry is not set or exceeds the configured maximum: the operation is refused before I/O.
- The Redis replica the client reaches is read-only during failover: writes are classified as unavailable, and the probe reports `read_only`.
- A fencing guard row does not exist yet for a resource: the first token is accepted and recorded.
- The request's deadline has passed before a call: no I/O happens, and the call fails with `coordination.timeout`.
- The request is cancelled during a wait for a lease: the wait stops, no lease is held afterwards, and the call fails with `coordination.cancelled`.
- A feature registers twice under one name, or with an unknown primitive: the checker refuses the declaration.
- TLS mode `disable` outside `development`: configuration validation rejects it under ISK-0003.

## Requirements *(mandatory)*

### Functional Requirements

**Ports and ownership**

- **FR-001**: The coordination ports in TR-2 MUST be defined in `Iskender.Domain.Ports`, and MUST NOT expose driver, Redis, PostgreSQL, or ASP.NET Core types in any signature. Each port and adapter type MUST document ISK-0005 as its owning spec.
- **FR-002**: The local substitutes MUST live in `Iskender.Infrastructure`. The Redis adapter MUST live in `Iskender.Infrastructure.Redis` and MUST be the only production project referencing the Redis client. The PostgreSQL coordinator and fencing guard MUST live in `Iskender.Infrastructure.PostgreSql`, in a namespace that names only the schema `isk_coordination`. All ISK-0001 architecture rules MUST pass unchanged.
- **FR-002a**: The PostgreSQL coordinator MUST declare `strict` for atomic operations, leases, and fencing, and MUST offer no cache. Its fencing tokens MUST come from a durable monotonic source in PostgreSQL, so no token is repeated after a restart or failover of the database.
- **FR-003**: Every coordination call MUST take a coordination scope built only as TR-3 describes. A null or unresolved scope MUST fail with `coordination.scope_required` before any I/O, and no port MUST accept a raw key.
- **FR-004**: Stored keys MUST be derived as TR-3 describes, and MUST NOT contain an organization ID, project ID, credential, or caller-supplied text other than the module name.

**Declared consistency and capability checks**

- **FR-005**: Every coordinator implementation MUST declare a level from TR-4 for each primitive it offers, and every local substitute MUST declare `local`.
- **FR-006**: Every consuming feature MUST register a requirement declaration as TR-5 describes. A declaration of `degrade` by a feature marked as a mandatory security or hard-budget path MUST be refused with `degrade_not_permitted`.
- **FR-007**: Topology MUST be configurable as `single_instance` or `multi_replica`, and MUST be `multi_replica` when not set.
- **FR-008**: The capability checker MUST evaluate every registered requirement against the configured coordinators and topology by the rules in TR-6 and TR-7, and MUST report every feature as `accepted` or `refused` with one reason code from TR-7.
- **FR-009**: A refused feature's every coordination call MUST fail with `coordination.capability_unsupported` before touching any state, and MUST NOT fall back to a local substitute.
- **FR-010**: A local substitute MUST NOT be reported, in any result, diagnostic, or documentation, as distributed or production-grade distributed enforcement.

**Atomic operations**

- **FR-011**: The atomic operations port MUST offer the single-key operations in TR-8, each atomic on its coordinator, and MUST NOT offer multi-key transactions.
- **FR-012**: A reservation MUST be granted only if the resulting value stays within its limit, with no window between the check and the update, and MUST be denied rather than wrap on overflow.
- **FR-013**: Every key MUST have an expiry no longer than the configured maximum.

**Leases and fencing**

- **FR-014**: A lease MUST carry a fencing token that is strictly increasing per lease key and never reused, and expiry MUST be measured on the coordinator's clock.
- **FR-015**: Acquisition MUST offer try-only and bounded-wait modes and MUST report a held lease as contention. Renewal MUST fail with `coordination.lease_lost` once the caller no longer holds its token. Release MUST be idempotent and MUST NOT release another holder's lease.
- **FR-016**: The port MUST report a held lease's remaining validity, and MUST report it not safe to act when that is below the configured safety margin.
- **FR-017**: The fencing guard MUST, inside the caller's ISK-0004 unit of work, accept a token only if it is greater than or equal to the highest token accepted for that scope, module, and resource name, and MUST otherwise fail with `coordination.fencing_rejected` so that the unit commits nothing.
- **FR-018**: Leases and the fencing guard MUST NOT be built on `IUnitOfWork.AcquireAdvisoryLockAsync` or any advisory lock, and this spec MUST NOT add a session-scoped lock.

**Versioned cache**

- **FR-019**: The cache MUST treat an entry of a version other than the reader's expected version as a miss, and MUST make every entry of a namespace a miss when its generation is bumped.
- **FR-020**: On any coordinator failure, a cache read MUST be a miss, and the cache MUST NOT be used as a source of truth by any port in this spec.
- **FR-021**: The cache MUST refuse values above the configured maximum size, and MUST NOT log or label a cached value.

**Failures, errors, and health**

- **FR-022**: The adapter MUST classify every coordinator failure into the classes in TR-12, MUST mark an unknown write outcome with `OutcomeUnknown`, and MUST NOT retry any operation itself. Raw driver exceptions MUST NOT cross the port.
- **FR-023**: The error codes in TR-13 MUST be registered in `ErrorCodes` under the stated categories, each with a safe message, and the compatibility harness MUST report `core-contracts` raised from 3 to 4 with compatible changes only.
- **FR-024**: The PostgreSQL coordinator's tables and the fence records MUST be added through ISK-0004's migration framework, in the schema `isk_coordination`, under its isolation rules, and through a revision of the persistence contract that increments it to version 2, permits that namespace and schema, and leaves its Advisory locks rules unchanged.
- **FR-025**: The coordination health probe MUST report one status and one reason from TR-14 within its timeout plus a fixed allowance documented as a safety bound, and MUST NOT be called by liveness.
- **FR-026**: No coordination result, log, metric, trace, or error message MUST contain a host, port, user name, password, connection text, derived key, scope ID, cached value, or server error text.

**Configuration and diagnostics**

- **FR-027**: The module MUST declare the ISK-0003 namespace `coordination` from TR-15, with the Redis password as a `secretRef`, TLS defaulting to `require`, `disable` development-only, and no connection string field. Redis credentials MUST come only through the coordination credential port, with no implementation that reads secrets outside test projects.
- **FR-028**: The module MUST emit the log events, activities, and metrics in TR-16, labelled only with the fields TR-16 lists.

**Fixtures, tests, and documentation**

- **FR-029**: The coordinator conformance suite in TR-17 MUST run against every shipped implementation, and distributed implementations MUST also run it from at least 2 independent clients.
- **FR-030**: The integration suite MUST include the refusal tests of User Story 1 and the lease fencing tests of User Story 2. Both are this spec's acceptance evidence.
- **FR-031**: Without a container runtime, container-backed tests MUST be reported as skipped, each with its reason, and in CI a missing container runtime MUST fail the run.
- **FR-032**: Documentation MUST be updated in the same PR: `docs/03` and `docs/04` link to this spec's coordination contract and to the revised persistence contract, and an operator section covers topology declaration, choosing Redis or PostgreSQL per primitive, refusal reasons, health reasons, and Redis provisioning.

### Negative Security and Failure Scenarios

| Scenario | Expected outcome | Covered by |
|---|---|---|
| Replicas each enforce a local limit, multiplying the real limit | The feature is refused in `multi_replica` with local substitutes | FR-007, FR-008, FR-009, User Story 1 |
| An operator forgets to declare topology before scaling out | Undeclared topology is `multi_replica` | FR-007 |
| A coordinator outage turns a hard budget into "unlimited" | Hard-budget paths cannot declare `degrade`, and outages are reported, never granted | FR-006, FR-022, User Story 3 |
| A paused lease holder overwrites a newer holder's work | The fencing guard rejects its stale token | FR-014, FR-017, User Story 2 |
| A lease is trusted after its expiry because a replica's clock is slow | Expiry uses the coordinator's clock, and a safety margin stops work early | FR-014, FR-016 |
| One tenant reads or disturbs another tenant's counter, lease, or cache entry | Keys are derived from the trusted scope, and no raw key is accepted | FR-003, FR-004, User Story 2, User Story 4 |
| Tenant IDs or secrets appear in the coordinator's key space | Keys are hashed, with only the module in clear | FR-004, SC-005 |
| A retried reservation double-counts | The adapter never retries, and unknown outcomes are marked | FR-022 |
| A stale cache entry is served after a change | Version and generation mismatches are misses | FR-019 |
| A cache outage serves stale or wrong data | A failed read is a miss, and the source of truth is read | FR-020 |
| A large cached value exhausts coordinator memory | Values above the maximum are refused, and every key expires | FR-013, FR-021 |
| A Redis password leaks through configuration, logs, or errors | `secretRef` only, and the canary test checks every output | FR-026, FR-027, SC-005 |
| A plaintext link to Redis | TLS defaults to `require`, and `disable` is development-only | FR-027 |
| An advisory lock is used as a lease | Leases and fencing are separate ports, and no advisory lock backs them | FR-018 |
| A counter overflows and wraps to a small value | Overflow is denied | FR-012 |

### Key Entities

- **Coordination scope**: ISK-0002's organization, project, or operator scope, from a trusted source.
- **Consistency level**: `local`, `best_effort`, `leased`, or `strict`.
- **Coordinator capability declaration**: The level an implementation provides for each primitive.
- **Feature requirement declaration**: A feature's name, module, primitives, minimum levels, and outage behavior.
- **Topology**: `single_instance` or `multi_replica`.
- **Capability check result**: `accepted` or `refused` with a reason, per feature.
- **Atomic counter and versioned value**: Single-key state with an expiry.
- **Reservation**: An amount granted against a counter within a limit.
- **Lease**: A time-bounded exclusive grant for a scope, module, and name, with its fencing token and coordinator-clock expiry.
- **Fencing token**: A 64-bit, strictly increasing number per lease key.
- **Fence record**: The highest token accepted for a protected resource.
- **Cache entry and namespace generation**: A scoped, versioned value and the generation that invalidates its namespace.
- **Coordination failure class and health result**: The classification in TR-12 and the status and reason in TR-14.
- **Coordination settings**: The `coordination` section.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a machine with a container runtime, with packages restored and no cloud credentials, the format check, build, and tests pass with zero warnings, and all 10 CI checks pass. Without a container runtime, every container-backed test is reported as skipped with its reason and 0 other tests are skipped. In CI, 0 container-backed tests are skipped.
- **SC-002**: The capability check produces the expected result and reason for 100% of the combinations in a fixture table covering both topologies, every consistency level required and declared, and both outage behaviors, and each of the 4 refusal reasons is produced by at least one test.
- **SC-003**: The lease fencing tests of User Story 2 pass on 20 consecutive runs against every shipped implementation, with 0 stale-token writes committed, 0 moments with two holders, and 0 tokens issued twice.
- **SC-004**: The reservation test of User Story 3 grants exactly the limit, with 0 over-grants, on 20 consecutive runs from 2 independent clients against every shipped distributed implementation.
- **SC-005**: With canaries planted as the coordinator host, user name, password, an organization ID, and a cached value, each canary appears 0 times in results, error messages, captured logs, metric labels, trace attributes, and the coordinator's own key names across the whole coordination suite.
- **SC-006**: Each of the 7 error codes in TR-13 and each of the 7 health reasons in TR-14 is produced by at least one test, and the compatibility harness reports `core-contracts` raised by one with 0 undeclared breaks and 0 changes to the other two contract packages.
- **SC-007**: For every primitive, 100% of cross-scope attempts across two organizations and two projects see none of the other scope's state.
- **SC-008**: The PR changes 0 tracked files under `.specify/` and 0 in `.iskender/grounding.yaml`, adds 0 session-scoped locks, changes 0 Advisory locks rules in the persistence contract, adds PostgreSQL tables only in `isk_coordination`, and adds 0 rate algorithms, quota windows, budgets, breakers, workers, host wiring, or cloud SDK references.

## Assumptions

- ISK-0001 to ISK-0004 are merged and complete on `main`, and their contracts, the owner scope, trusted scoped reference, `GatewayExecutionContext`, `ErrorCodes` at `core-contracts` 3, the compatibility harness, the configuration section mechanism, the unit of work, and the PostgreSQL fixture exist as their contracts describe.
- A deployment runs one coordinator per primitive. Choosing different coordinators for different primitives is allowed by configuration, but one primitive is never split across two.
- "Replica" means an independent process of the same plane. The checker cannot see other replicas, so a `single_instance` declaration is the operator's responsibility, and the documentation says so.
- Lease durations, safety margins, timeouts, and size limits are configurable safety bounds, not performance figures. No latency or throughput figure is asserted, as Constitution VIII requires.
- Rate algorithms, quotas, concurrency caps, budgets, breakers, workers, and cache users are defined by their own specs, which use these ports. Any interaction with policy plans, plugin trust, durable conversations, financial controls, or streaming goes through those specs' authoritative documents.

## Out of Scope

- Rate and token algorithms (ISK-0060), quotas and concurrency caps (ISK-0061), budgets and the usage ledger (ISK-0062, ISK-0063), and circuit breakers (ISK-0066).
- Background workers and the outbox (ISK-0009). API key cache invalidation itself (ISK-0017).
- Host composition, readiness endpoints, and start-up failure wiring (ISK-0006, ISK-0007). Local Compose Redis (ISK-0008).
- Secret resolution and the production credential provider (ISK-0021). This spec defines only the coordination credential port in TR-15.
- Provisioning managed Redis in any cloud.
- Changing the constitution, the roadmap, ADRs, the ISK-0001 rules contract, ISK-0004's advisory lock rules, or `.iskender/grounding.yaml`. Any roadmap consequence goes through `/review-roadmap`.
