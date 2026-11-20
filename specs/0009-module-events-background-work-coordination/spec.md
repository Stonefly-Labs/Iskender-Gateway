# Feature Specification: ISK-0009 — Module Events & Background Work Coordination

**Feature Branch**: `feature/0009-module-events-background-work-coordination`

**Created**: 2026-11-17

**Status**: Draft

**Input**: User description: "Create the specification **ISK-0009 — Module Events & Background Work Coordination** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md @docs/08-routing-resilience-and-governance.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Create durable internal work handoff for reconciliation/config notifications without cloud bus dependency**. In-scope behaviors and artifacts: **Outbox/lease approach; idempotent jobs; tracing; retry poison handling; shutdown**. Explicitly exclude: **No general event-bus product or external Kafka dependency**. Prerequisites: **ISK-0004, ISK-0005, ISK-0007, ISK-0008**; do not assume unavailable implementation already exists. […] Acceptance must demonstrably include: **Crash after DB commit and duplicate worker delivery do not lose or double-apply work**. […] Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify." The full prompt is the ISK-0009 entry of `docs/17-master-spec-inventory.md`.

## Overview

The constitution is present at `.specify/memory/constitution.md` and governs this spec.

Later specs need to hand work from one module to another durably: a configuration change in the control plane that must be published (ISK-0057), a usage settlement that must be reconciled (ISK-0063), a stream commit that must be recovered (ISK-0049), audit retention (ISK-0026), and provider batch jobs (ISK-0111). The architecture forbids a cloud message bus as an MVP prerequisite (`docs/03` Architectural tests and non-goals; `docs/04` Choices: no mandated message broker or queue), and asks for minimal infrastructure built on PostgreSQL and Redis primitives. ISK-0004 left the outbox to this spec (persistence contract, Left to later specs), and ISK-0005 left worker leases and scheduling `PurgeExpiredAsync` across scopes to it (coordination contract, Left to later specs). ISK-0007 and ISK-0008 each left an assumption AV-7 about where maintenance work runs.

This spec gives module authors and operators:

1. **A transactional outbox**: a module records a **work item** inside the same unit of work as the data change that causes it, so the item exists if and only if the change committed.
2. **A dispatcher** that delivers each item at least once to the one handler registered for its kind, and applies it at most once: the handler's writes and the item's completion commit together, in one unit of work that the fencing guard has accepted a PostgreSQL-issued lease's token for. A crash after that commit cannot redeliver the item, and a duplicate or stale delivery cannot commit.
3. **Idempotent recurring jobs**: scheduled maintenance, run by one holder of a lease at a time, starting with the purge of expired coordination rows across scopes and the retention of finished work items.
4. **Retry and poison handling**: bounded attempts with backoff, a failure classification that separates transient from permanent failures, and a quarantined **poisoned** state that is never redelivered, including for an item that crashes its process on every attempt.
5. **A scope directory**, so that a worker learns which owner scopes have pending work without an operator-scope unit ever reading a tenant row.
6. **A worker command** of the control-plane executable, which runs the dispatcher and the jobs, and a matching service in the developer topology. The data plane and the serving control plane run no background work.
7. **Tracing and diagnostics**: each dispatch is linked to the trace that enqueued it, with value-free events, metrics, and readiness.
8. **Shutdown inside ISK-0007's stop bound**: in-flight work finishes or is abandoned safely before the process exits within the orchestrator's grace period.

This spec adds no broker, bus, queue service, Kafka, or cloud SDK. It adds no product handler, gateway endpoint, provider, credential provider, or management API. The data plane does not consume work items. Delivering a change to every data-plane replica is ISK-0057's (Q3).

Contract, type, command, service, and path names appear in this spec where an approved contract already fixes them or where they are the interface this spec defines, not as a choice of implementation.

## Decision Classification

### Accepted decisions (already approved; this spec applies them)

| Decision | Source |
|---|---|
| A modular monolith with explicit modules. Control and data planes, and optional background processing, are independently executable deployments that share approved persistence and coordination contracts. Storage and coordination contracts are versioned, and dependency direction is preserved | ADR-0003, `docs/03` Logical architecture, Constitution II |
| Iskender ships as containers and as the embedded library, with the same pipeline contracts, and embedding requires no global service ownership | ADR-0004, Constitution II |
| .NET 10 and ASP.NET Core are the runtime | ADR-0002 |
| Organization → project → resource ownership. Every query, worker, job, and stream requires its scope's checks, and isolation is enforced at every worker and job boundary | ADR-0005, Constitution V |
| No cloud message bus, broker, or queue is a prerequisite. Minimal infrastructure uses durable PostgreSQL and Redis primitives, behind cloud-neutral contracts | `docs/03` Architectural tests and non-goals, `docs/04` Choices |
| Every database access happens in a unit of work for one owner scope: one connection, one transaction. A unit commits only on `CommitAsync`. The adapter never retries, and an unknown commit outcome is never retried automatically | Persistence contract version 2, Units of work, Failures |
| Operator-scope units cannot touch tenant tables, tenant-scope units cannot touch operator tables, and a scope-keyed table shows each scope only its exact rows. Operator access to tenant data needs an audited elevated operation that a later spec defines | Persistence contract version 2, Isolation rules 5, 6, and 8 |
| Advisory locks are transaction-scoped and are not leases, fencing tokens, or distributed locks | Persistence contract version 2, Advisory locks rule 4 |
| Modules reach coordination only through `CoordinationGate.ForFeature`, after declaring a `FeatureRequirement`. The fencing guard runs inside the caller's unit, in the lease's scope, and accepts only leases the PostgreSQL coordinator issued. A refused feature with `MandatoryPath` fails start, and any other refused feature fails closed and appears in readiness | Coordination contract version 1, Ownership rule 3, Requirements, Leases rule 6; hosting contract version 2, Start-up rule 7 |
| `PurgeExpiredAsync(scope, batch)` deletes expired atomic-operation and lease rows of one scope, and fence records and namespace generations are never purged. ISK-0009 runs it across scopes | Coordination contract, Atomic operations rule 6; ISK-0005 research R22 |
| Composition start-ups run in ordinal module-name order at start, and in reverse after the server and every other hosted service stop | Hosting contract version 2, Start-up rules 6 and 8 |
| On a stop signal, readiness turns `stopping`, requests drain for `shutdownDelay` and then up to `shutdownTimeout`, start-ups stop in reverse, and the orchestrator's grace period must exceed `shutdownDelay` + `shutdownTimeout` + 5 seconds. A `migrate` run starts no composition start-up | Container contract version 1, Lifecycle, Migration command rule 3 |
| The developer overlay selects `redis` for atomic operations, leases, and cache, and the smoke script asserts each plane's readiness answer byte for byte, with an empty `refused` list. A change to an expected smoke state is a revision of the local environment contract | Local environment contract version 1, Documents rule 3, Smoke assertions 3, Changing this contract |
| Graceful shutdown rejects new work, drains bounded active work, cancels at the deadline, and reconciles leases. At-least-once metadata notification behavior is documented | `docs/12` Reliability and horizontal scale |
| Telemetry is OpenTelemetry, with a bounded, sensitive-safe vocabulary, and no payload, prompt, credential, or tenant identifier in a label | Constitution V and VIII, `docs/12` Standard signals |
| No credential provider ships until ISK-0021, so before then no process can connect to PostgreSQL | Persistence and coordination contracts, Credentials |
| No SLO, latency, or throughput figure is asserted without a reviewer-approved baseline. Every bound in this spec is a safety bound | Constitution VIII |
| One stage runs per `/drive-speckit` invocation, and `specify` produces the specification only | ADR-0025, Constitution X |

### Maintainer decisions taken in `specify`

| Decision | Recorded in |
|---|---|
| **Finding scopes** (Q1): a narrow **scope directory**, added by a revision of the persistence contract (version 3) as a new table kind. A unit may register only its own scope, inside its own transaction. Only an operator-scope unit may read the directory's rows across scopes (refined in `clarify`: a scope's own unit may also read and delete its own rows), and the directory holds nothing but scope keys, a module name, and a due time. All real work happens in a unit opened in each listed scope, under that scope's row-level security. It needs Security owner review | TR-4 |
| **Where work runs** (Q2): a new long-running **worker command** of the control-plane executable, which binds only the health listener. The serving control plane and the data plane run no workers. In the developer topology, a `worker` service runs that command with its own documents, whose overlay selects `postgresql` for leases. The data-plane and control-plane smoke answers stay byte for byte, and the worker's expected state is added through a revision of the local environment contract. **This settles ISK-0007's AV-7 and ISK-0008's AV-7** | TR-9, TR-10 |
| **Delivery** (Q3): each item goes to one handler, at least once, and is applied at most once. No broadcast to every replica is added. Delivering a change to every data-plane replica is left to ISK-0057, which can have a handler bump an ISK-0005 cache namespace generation that every replica reads | TR-1, Out of Scope |

### Maintainer decisions taken in `plan`

| Decision | Recorded in |
|---|---|
| **Operator directory row** (P1): an operator-scope unit reads every directory row, and, like every scope, inserts, refreshes, and deletes only its own `operator` row, so operator-scope leases and items are purged and dispatched like any other scope's | TR-4, FR-006 |
| **Worker plane** (P2): the worker is its own composition plane, `worker`, with the root `AddIskenderWorker(...)`, the single-instance lease `instance.worker`, and `service.name` `iskender-worker`. This adds a fifth revision, hosting contract version 3, with no public member of `Iskender.Hosting` changed | TR-9, TR-18, FR-025, FR-035 |
| **Payload limit** (P3): 64 KiB is a fixed rule of the `work` contract in every plane, not a configuration field | TR-1, TR-14, FR-002, FR-029 |
| **Operator-scope items** (P4): an item may be owned by the operator scope, and is read and written only in an operator unit | FR-009 |

### Technical recommendations (proposed by this spec; reviewed in `clarify`)

| ID | Recommendation | Why | Alternative rejected |
|---|---|---|---|
| TR-1 | **Work item**: a record with an opaque ID, its owner scope, the producing module, a kind (`<module>.<name>`), a payload version, a payload of at most 65,536 bytes (64 KiB), a fixed limit of the `work` contract in every plane (decided in `plan`), of bytes the module serializes, an idempotency key unique per scope and kind, the time it becomes due, an attempt count, a state (`pending`, `completed`, or `poisoned`), the last failure's class and code, and the W3C trace context current at enqueue. The payload is never logged, traced, or used as a label, and holds no secret, credential, prompt, or response text | Gives handlers what they need and diagnostics nothing they must not see | A free-form JSON event with headers, which invites payload logging |
| TR-2 | **Enqueue in the caller's unit**: the outbox port takes the caller's open unit and writes the item in its transaction. A unit in another scope than the item's fails before any write. An enqueue whose scope and idempotency key match an existing item writes nothing and reports the existing item, so a re-run producer never enqueues twice. A rolled-back unit leaves no item. Only kinds declared at registration may be enqueued | The change and the item commit or vanish together, which is what an outbox is for, and it uses only ISK-0004's unit (settles ISK-0004's AV-5) | A separate transaction after the change, which loses work on a crash between them |
| TR-3 | **Storage**: a framework migration module `work`, schema `isk_work`, owned by a framework namespace of the PostgreSQL adapter, holding the items in a scope-keyed table under row-level security on `scope_key`, through a revision of the persistence contract (version 3). It names no other schema | Items belong to their scope, and scope-keyed tables are the existing kind for framework state that every scope uses | A tenant-owned table, which an operator worker could not use, and an operator table, which tenant units cannot write |
| TR-4 | **Scope directory** (decided in `specify`, Q1): a new table kind, revised into the persistence contract (version 3), whose policy lets a unit read, insert, refresh, or delete only the rows for its own exact scope, and lets an operator-scope unit read every row (decided in `clarify`), and, like every scope, write only its own `operator` row (decided in `plan`). A row is removed only in the scope's own unit, by one statement that deletes it only when that unit sees no remaining work for its module, serialized with a producer's refresh of the same row, so a producer that commits between the check and the delete cannot lose its row. The outbox registers the item's scope, with module `work` and the item's due time, in the same unit as the enqueue. The PostgreSQL coordinator registers a scope, with module `coordination`, in the same unit as any write that creates a row with an expiry, through a revision of the coordination contract (version 2). The directory holds no ID other than the canonical scope, no payload, and no name. A tenant-scope unit can read nothing of another scope from it | Lets one operator worker find every scope with work, while every read of work happens under the owning scope's row-level security. It is narrower than the reserved elevated operation | Operator reads of tenant rows (the reserved elevated operation); waiting for ISK-0011's organization table, which leaves the cross-scope purge undone |
| TR-5 | **Opening a scope from the directory**: the worker turns a directory row into a unit of work scope only through one framework-internal attestation, available only to the dispatcher and the jobs, which validates the canonical scope's form and grants nothing beyond that scope. A malformed row is skipped with a diagnostic, never widened to operator scope | The ISK-0002 rule is that a scope comes only from an approved resolver, and a stored key is not a request | Building a scope from free text anywhere |
| TR-6 | **Dispatch and at-most-once apply**: each item is given, at enqueue, a stable bucket from 0 to 63 derived from its ID, which the `work` contract fixes. Each scope has `work.dispatch.shards` dispatch shards (default 4, a divisor of 64 from 1 to 64), and an item's shard is its bucket modulo that count. For each shard of a scope with due items, the dispatcher takes that shard's PostgreSQL-issued lease, `work` / `dispatch.<shard>` (namespace `work`, key `dispatch.<shard>`) in the scope, which covers every item of the shard it claims. So a busy scope spreads over up to that many workers, and an item that crashes its worker stalls only its shard until the lease expires. Each shard has its own fence resource, so fence records, which ISK-0005 never purges, are bounded by 64 per scope, never by the number of items (decided in `clarify`). The holder of a shard's lease may run several of its items at once, up to `work.dispatch.concurrency` items per worker and `work.dispatch.scopeConcurrency` per scope in one worker. A change of the shard count, including during a rolling upgrade, moves items between shards safely, because an item's completion in FR-013 is conditional on the item still being `pending` in the same unit, so two holders of different shard leases can never both commit it. Before running a handler, it records the attempt durably, in its own committed unit, so that a crash still counts. The handler then runs inside one unit in the item's scope, in which the fencing guard accepts the shard lease's token for that shard's fence resource first, the handler makes its writes, and the item is marked `completed` on the condition that it is still `pending`, all committed together. An item already `completed`, or a stale token, commits nothing. A handler that runs past its timeout, `work.handler.timeout` or the shorter or longer value its registration declares within the contract's bounds, is cancelled, so its unit rolls back, and the attempt counts as transient. A handler that needs a side effect outside that unit uses the item's ID and idempotency key to make it idempotent itself, and its registration says so | A crash after the commit cannot redeliver the item, a crash before it rolls back everything, and a duplicate or paused worker cannot commit. This is the approved acceptance | Deleting the item before running the handler, which loses work on a crash; relying on the handler alone for idempotency |
| TR-7 | **Retry and poison**: a handler failure is classified. A transient class (`persistence.unavailable`, `persistence.timeout`, `persistence.concurrency_conflict`, `coordination.*` other than `capability_unsupported`, or cancellation by shutdown) makes the item due again after exponential backoff with jitter, from `work.retry.initialDelay` (default 1 s) to `work.retry.maximumDelay` (default 5 min). A permanent class, which the handler declares or which is an invalid payload, poisons it at once. Any other failure, including an unexpected exception or an error code outside both lists, is `unclassified` and is retried as transient (decided in `clarify`), recorded with class `unclassified` and code `core.internal`. An attempt that ends with no recorded result, because its process crashed or was killed, or because the worker lost the shard's lease, keeps the class `abandoned` and code `work.attempt_abandoned` that its attempt record wrote before the handler ran (refined in `checklist`). An item is poisoned when its attempt count reaches the kind's maximum, default `work.retry.maximumAttempts` (8). A poisoned item is never redelivered, keeps its last failure's class and code, and is kept for `work.retention.poisoned` (default 30 days) | Bounded retries stop a bad item from looping, and a durable attempt count catches an item that kills its process | Unbounded retries; a separate dead-letter table, which adds a second write path |
| TR-8 | **Version skew**: an item whose kind no registered handler knows, or whose payload version is above the handler's highest supported version, is left `pending` and untouched, is not counted as an attempt, and is reported by a metric and an event. The dispatcher claims only items whose kind and payload version it handles, so a skewed item never occupies a claimed batch or delays another item, and skewed items are found by a separate count, not by claiming them (decided in `clarify`). A handler supports every payload version it has ever been published with, until a contract revision retires one | In a rolling upgrade, an older worker must not poison an item that the newer release will handle | Poisoning unknown kinds, which loses work during an upgrade |
| TR-9 | **The worker command** (decided in `specify`, Q2): `work`, a long-running command of `Iskender.ControlPlane.Host`, through a revision of the container contract (version 2). It is its own composition plane, `worker`, with the root `AddIskenderWorker(...)`, the single-instance lease `instance.worker`, and `service.name` `iskender-worker` (decided in `plan`). It makes the serving control plane's registrations, with that root in place of `AddIskenderControlPlane(...)`, plus the dispatcher's, binds only the health listener, answers liveness and readiness with ISK-0007's five checks unchanged, and runs no traffic listener. It uses the existing exit codes: 0, 1, 2, 3, 4 for a refused dispatcher (whose requirement has `MandatoryPath`), and 64. `work` on the data plane is a usage error, exit 64. The serving control plane and the data plane register only the outbox, so they can enqueue, and run no dispatcher or job. **This settles ISK-0007's AV-7**: maintenance work runs as another command of the control-plane executable, with no new executable role | A separate command scales and secures apart from the control plane (`docs/04` topology 2, "optional maintenance worker"), and gives the worker its own documents | A new image or executable, which the prompt excludes; workers inside the serving control plane, which would force its leases onto PostgreSQL |
| TR-10 | **Developer topology** (decided in `specify`, Q2): a revision of the local environment contract (version 2) adds a `worker` service from the control-plane image, running `work`, with its own documents. Its overlay selects `postgresql` for leases, so its dispatcher is accepted, neither refused nor stopping its start, and leaves the data-plane and control-plane documents unchanged. It starts in the default mode, with the same hardening and grace period as the planes, publishes no port, and depends only on `postgres` and `valkey` being healthy. The smoke script asserts the four existing answers unchanged and adds the worker's expected answer before ISK-0021: `503`, with `persistence` `authentication_failed`, `coordination` `authentication_failed`, `single_instance` `not_applicable`, `capability` `none_refused`, and an empty `refused`. **This settles ISK-0008's AV-7** | The developer topology stays the full basic topology, and the existing smoke answers stay byte for byte | An opt-in profile, which would leave the worker untested by smoke; changing the control plane's overlay, which changes its answer |
| TR-11 | **Recurring jobs**: a job has a name, an interval, and a batch size. One run of a job takes an operator-scope PostgreSQL lease named for the job, so one process runs it at a time, and fences its bookkeeping (its cursor and last-run time) with that lease. ISK-0009 ships two jobs. `coordination.purge_expired` reads the directory's `coordination` rows and calls `PurgeExpiredAsync` for each scope until it returns fewer than the batch, then removes the scope's `coordination` directory row in the scope's own unit, only if that unit sees no expiring row remaining (TR-4). `work.retention` deletes `completed` items older than `work.retention.completed` (default 7 days) and `poisoned` items older than `work.retention.poisoned`, scope by scope. Both are idempotent: deleting an expired row twice is harmless. The purge's deletes run in `CoordinationMaintenance`'s own unit, unchanged, so they are not fenced. Only the job's bookkeeping is | One holder at a time, and safe to repeat after a crash. Settles ISK-0005's AV-5: worker leases and fencing use its ports alone, and the cross-scope purge needs only the directory registration of TR-4 | Changing `PurgeExpiredAsync` to take the caller's unit, which revises ISK-0005's maintenance API for no safety gain |
| TR-12 | **Coordination requirements**: the dispatcher declares a feature that uses leases at `strict`, fencing at `strict`, `MandatoryPath`, and `fail_closed`. Each job declares its own feature in the same way. The outbox declares none, because it uses only the caller's unit. So a worker whose leases are not on PostgreSQL is refused with `unsupported_consistency` and exits 4, while a plane that only enqueues is never refused | Makes the fencing need visible at start, as the coordination contract requires, instead of failing on the first item | Leaving the dispatcher undeclared, which would fail only at the first fenced write |
| TR-13 | **Shutdown**: on the first stop signal, the dispatcher and the jobs stop claiming at once. Each item in flight may finish until `shutdownDelay` + `shutdownTimeout` after the signal, and is then cancelled, so its unit rolls back and the item stays `pending`, its attempt recorded as transient and handed back, so a shutdown never poisons it (maintainer decision, 2026-11-18, `converge`). The work start-up's stop, which runs before the coordination start-up's because start-ups stop in reverse module order, releases every lease it holds, so another worker can take the items as soon as they are due rather than after the leases expire. The worker exits within `shutdownDelay` + `shutdownTimeout` + 5 seconds of the signal. The container contract revision records the new step "stop claiming at the signal" in the worker's sequence, and changes no step of the data plane's or the serving control plane's | Fits background work inside ISK-0007's bound, so a correct grace period never kills a worker mid-commit, and a killed one is still safe under TR-6 | Waiting for every handler without bound, which outlasts the grace period |
| TR-14 | **Configuration**: an ISK-0003 namespace `work`, registered by the dispatcher's registration only, with the poll interval, batch size, lease duration (within ISK-0005's lease bounds), the shard count `work.dispatch.shards` (default 4), the in-flight caps `work.dispatch.concurrency` per worker and `work.dispatch.scopeConcurrency` per scope within a worker (no larger than `concurrency`), the handler timeout `work.handler.timeout`, the retry and retention settings above, and each job's interval and batch. The payload limit is not configurable (TR-1). Every default is a safety bound, not a performance figure. The plan fixes the defaults and bounds of the in-flight caps and the handler timeout. A document with a `work` section is rejected by the data plane and the serving control plane (`config.namespace.unknown`), as for any section a plane does not register | Keeps the worker's settings out of the other planes, as ISK-0007 does for `hosting` | Settings on the outbox registration, which every plane would then accept |
| TR-15 | **Tracing**: enqueue captures the current W3C trace context in the item. Each dispatch starts an activity from `ActivitySource("Iskender.Work")`, linked to that context rather than a child of it, with the kind, attempt, and outcome only. A job run starts its own activity. Trace context from outside the process is never trusted for anything but the link | Lets an operator follow work from the change that caused it, without making a slow worker's span part of the request's trace | Making the dispatch a child span, which stretches request traces over minutes |
| TR-16 | **Diagnostics**: events 8001 onward under the category `Iskender.Work`, and metrics from `Meter("Iskender.Work")`: items enqueued, dispatched, completed, retried, poisoned, and skipped for version skew, dispatch duration, due backlog per kind, and job runs. Fields and labels are limited to module, kind, job, outcome, failure class, error code, and attempt. No scope ID, item ID, idempotency key, payload, or configuration value appears in any field or label | Operable diagnostics with a bounded vocabulary | Logging item IDs, which identify tenants' work |
| TR-17 | **Error codes**: a small set under `work.*` in `ErrorCodes`, with `since: 6`, raising `core-contracts` from 5 to 6 with compatible changes only, covering at least an enqueue in another scope than the unit's, a payload over the limit, an undeclared kind, and an enqueue on a unit that is not open. The plan fixes the codes, categories, and safe messages | Ports fail only with registered codes, as every other port does | Reusing `core.internal` |
| TR-18 | **`work` contract**: a new versioned contract, `work`, version 1, in this spec's `contracts/` directory, owning the work item, the outbox port, handler and job registration, delivery and apply semantics, retry and poison rules, version skew, the scope directory's use, the jobs, configuration, error codes, diagnostics, and the test fixture. The revisions are persistence version 3 (TR-3, TR-4), coordination version 2 (TR-4, TR-11), container version 2 (TR-9, TR-13), local environment version 2 (TR-10), and hosting version 3 (TR-9, added in `plan`). Each revision is published in this spec's `contracts/` directory, and the links in `docs/03` and `docs/04` are repointed | Later specs build handlers on this, and change it only through a revision | Rules spread across the four existing contracts with no owner |
| TR-19 | **Test fixture**: a work fixture in the tests, built on the ISK-0004 PostgreSQL fixture, that runs two or more independent dispatchers against one database, lets a test handler record each apply in its own unit, injects a crash before and after commit by killing the dispatching process or abandoning its unit, pauses a dispatcher past its lease's expiry, and forces duplicate claims. It follows ISK-0005's container-runtime rule | It is how the approved acceptance is shown, rather than asserted | A mock unit of work, which cannot show commit atomicity |

### Assumptions needing validation

| ID | Assumption | Validated by |
|---|---|---|
| AV-1 | PostgreSQL row-level security can express the scope directory's policy, so that a unit can read, insert, refresh, or delete only its own scope's rows while an operator unit can read every row, and so that a row's conditional delete is serialized with a producer's refresh of it, without a function that bypasses row-level security | This spec's plan, Security owner |
| AV-2 | Polling the directory and the item table, with no `LISTEN`/`NOTIFY` or broker, keeps the delay from commit to dispatch within one poll interval plus backoff, at the scale of the MVP topologies. No figure is asserted until a baseline is approved | This spec's plan; a figure only with a reviewer-approved baseline |
| AV-3 | The worker command can stop claiming at the stop signal and fit its drain inside the existing stop sequence, needing only the one added step of TR-13 in the container contract revision | This spec's plan |
| AV-4 | ISK-0057 can reach every data-plane replica with a handler that bumps an ISK-0005 cache namespace generation, with no broadcast channel from this spec | ISK-0057 |
| AV-5 | The handlers of ISK-0026, ISK-0049, ISK-0063, and ISK-0111 can keep their database side effects inside the handler's unit in the item's scope, and make any external side effect idempotent with the item's ID and idempotency key | Those specs |
| AV-6 | Once ISK-0021 provides credential providers, the worker in the developer topology becomes ready with no change of service or document layout, and its expected smoke state changes through a revision of the local environment contract | ISK-0021 |
| AV-7 | The work fixture and its crash tests run in the existing `dotnet test` check, so CI stays at 10 checks | This spec's plan |

**ISK-0004's AV-5 is settled by this spec** (TR-2, TR-3): an item is written inside the same unit of work as the data change, using only ISK-0004's unit. Finding scopes across tenants needs the scope directory table kind, which this spec adds through a revision of the persistence contract (TR-4).

**ISK-0005's AV-5 is settled by this spec** (TR-6, TR-11, TR-12): worker leases and fencing use only ISK-0005's ports, through `CoordinationGate`. Scheduling the purge across scopes adds only the directory registration of TR-4, through a revision of the coordination contract.

**ISK-0007's AV-7 is settled by this spec** (TR-9): maintenance work runs as another command of the control-plane executable, with no new executable role.

**ISK-0008's AV-7 is settled by this spec** (TR-10): maintenance work joins the developer topology as another control-plane command service, under a revision of the local environment contract that leaves the existing smoke answers unchanged.

### Post-MVP work (named so that it is not designed here)

A general event bus, publish/subscribe fan-out, consumer groups, or a replayable event log. Kafka, a cloud queue, or any external broker adapter. `LISTEN`/`NOTIFY` wake-ups or other latency optimizations, which wait for a measured need. Ordered delivery per key. Calendar or cron schedules beyond a job's fixed interval. A management API or UI for inspecting, requeuing, or discarding poisoned items, which waits for the first management API spec. Exactly-once external side effects.

## Clarifications

### Session 2026-11-17 (specify)

- Q: Work items and expired coordination rows are owned by tenant scopes, and an operator-scope unit cannot see tenant rows. How does a worker find which scopes have pending work or rows to purge? → A: A narrow scope directory, a new table kind added by revising the persistence contract to version 3. A unit registers only its own scope, only operator scope reads the directory, it holds no other tenant data, and all work happens in a unit opened in each listed scope. It needs Security owner review (TR-4, TR-5).
- Q: Workers fence their writes, so they need PostgreSQL leases, but the developer overlay selects `redis` for leases. Where does background work run? → A: A new long-running worker command of the control-plane executable, binding only the health listener. The serving control plane runs no workers. In the developer topology, a `worker` service with its own overlay selects `postgresql` for leases. The existing smoke answers stay byte for byte, and the worker's state is added through local environment version 2 (TR-9, TR-10).
- Q: Does ISK-0009 deliver each item to one handler, or also broadcast to every data-plane replica? → A: To one handler, at least once, applied once. Reaching every replica is left to ISK-0057, for example through an ISK-0005 cache namespace generation bump, and no broadcast mechanism is added (TR-1, AV-4).

### Session 2026-11-17 (clarify)

- Q: What should one dispatcher lease (and its fence) cover: a whole scope, or a single work item? → A: A whole scope. One lease key per owner scope, `work.dispatch`, held by one worker at a time, with its fence resource also per scope, so fence records are bounded by the number of scopes. A scope's items are handled by one worker at a time, and work spreads across workers by scope (TR-6, FR-011, FR-013). **Revised by the next answer.**
- Q: One lease per scope limits a busy tenant to one worker process and lets an item that crashes its worker stall the whole tenant until the lease expires. Should a scope's dispatch be sharded instead? → A: Yes, revising the answer above. Each scope has a fixed number of dispatch shards, `work.dispatch.shards`, default 4, each with its own lease key and fence resource, so a busy scope spreads over up to that many workers and a crash-looping item stalls only its shard. Fence records stay bounded, by the shard maximum times the number of scopes. A per-handler timeout, and a cap on how many items a worker runs at once, overall and for any one scope, are added (TR-6, TR-14, FR-011, FR-013, FR-014, FR-029).
- Q: Who may read and delete a scope's own row in the scope directory? → A: A scope's own unit may read, insert, refresh, and delete only its own rows, and an operator-scope unit may read every row. The check that no work remains and the row's delete run together in the scope's own unit, serialized with a producer's refresh of the same row, so a producer committing between them cannot lose its row. A tenant-scope unit still sees no other scope's row (TR-4, TR-11, FR-006, FR-020, FR-021).
- Q: When a handler throws a failure the spec doesn't classify, such as an unexpected exception or an error code outside the transient list, should the item retry or be poisoned at once? → A: Treat it as transient, bounded. Any failure that is not explicitly permanent retries with backoff until the kind's maximum attempts, and then the item is poisoned with failure class `unclassified` and code `core.internal` (TR-7, FR-016, FR-017).
- Q: How should the dispatcher keep items it can't handle (an unknown kind, or a payload version too new) from crowding out other work in the same scope? → A: The dispatcher claims only items whose kind and payload version it handles, so a skewed item never occupies a batch. A separate count reports skewed items through the skew metric and event. They are never touched, no attempt is counted, and a newer worker picks them up at once (TR-8, FR-015, FR-018).

### Session 2026-11-17 (checklist)

- Q: `coordination` depending on `directory` puts `coordination` before `directory` in reverse order, so rolling back below ISK-0009 would drop the coordination tables and fence records. Keep the dependency, or drop it? → A: Drop it. `coordination` declares no dependency on `directory`, `AddIskenderPostgreSqlCoordination()` still adds the `directory` module, and a rollback reverses `work` and then `directory`, leaving `coordination` untouched (persistence contract Migrations rule 12, research R3).
- Q: Expiring coordination rows that exist when a database is upgraded have no directory row, and updates register nothing. How are they handled? → A: Accepted as a known limit. They are reached once their scope next inserts a coordination row, and FR-006's guarantee covers rows written from this release on (coordination contract Atomic operations rule 7 and Known limits, research R3).

### Session 2026-11-17 (plan)

- Q: FR-006 lets an operator-scope unit read every directory row and change none, but operator-scope leases and items also need an `operator` row that only an operator unit can write. How may the operator write? → A: Its own row only. Operator units read every row, and insert, refresh, or delete only the `operator` row, as every scope does with its own (TR-4, FR-006; research R2).
- Q: The worker composes the control plane's root, so it would take `instance.control_plane` and report `service.name` `iskender-control-plane`. What identity should it have? → A: Its own plane, `worker`: `CompositionPlane.Worker`, the root `AddIskenderWorker(...)`, the lease `instance.worker`, `{Plane}` `worker`, and `service.name` `iskender-worker`, through hosting contract version 3 (TR-9, TR-18, FR-025, FR-035; research R11).
- Q: The outbox enforces `work.maximumPayloadBytes` in every plane, but only the worker registers the `work` section. Which fix? → A: A fixed contract limit. 64 KiB is a rule of the `work` contract in every plane, and leaves the configuration (TR-1, TR-14, FR-002, FR-029; research R14).
- Q: FR-009 read literally forbids operator-scope work items. Should they exist? → A: Yes. An operator-scope item is read and written only in an operator unit, and FR-009 forbids operator units only another scope's items and tenant rows (FR-009; research R2).

## User Scenarios & Testing *(mandatory)*

The "users" are module authors in later specs, who produce and handle work, and operators, who run and diagnose the worker.

### User Story 1 - Work survives a crash and is applied once (Priority: P1)

A module author enqueues a work item in the same unit of work as a data change. A worker applies it through a handler. Whatever crashes, and whenever, the item is neither lost nor applied twice.

**Why this priority**: It is the approved acceptance evidence, and every later handler depends on it.

**Independent Test**: With the work fixture, enqueue items whose test handler records each apply in its own unit. Kill the dispatching process at each injection point of FR-040, restart, and assert that every item is `completed` with exactly one recorded apply.

**Acceptance Scenarios**:

1. **Given** a producer's unit that changes data and enqueues an item, **When** the unit commits, **Then** the item is `pending` and due; **When** the unit rolls back instead, **Then** neither the change nor the item exists.
2. **Given** a handler whose writes and the item's completion have committed, **When** the worker process is killed before it records anything else, **Then** after restart the item is not dispatched again and its effect appears exactly once.
3. **Given** a worker killed during a handler, before its commit, **When** its lease expires or another worker takes the items, **Then** the item is dispatched again, its attempt count shows both attempts, and its effect appears exactly once.
4. **Given** a producer that runs twice with the same idempotency key in the same scope, **When** both commit, **Then** one item exists.

---

### User Story 2 - Duplicate and stale deliveries cannot commit (Priority: P1)

Two or more workers run against one database. A worker can pause, for example in a long garbage collection, until its lease expires while another takes over.

**Why this priority**: Duplicate worker delivery is named in the approved acceptance, and multi-replica operation is the default topology.

**Independent Test**: Run two dispatchers, force both to claim the same item, and pause one past its lease's expiry while the other completes the item. Resume the paused one and assert that its commit is rejected, nothing it wrote persists, and the item's effect appears once.

**Acceptance Scenarios**:

1. **Given** two workers claiming the same item, **When** both run the handler, **Then** at most one unit commits, and the other is rejected by the fencing guard or finds the item already `completed` and commits nothing.
2. **Given** a worker paused past its lease's expiry, **When** it resumes and tries to commit, **Then** the fencing guard rejects its token with `coordination.fencing_rejected`, its unit commits nothing, and event 5006 is written.
3. **Given** one scope with due items in every shard and as many workers as shards, **When** they dispatch, **Then** more than one worker handles that scope's items, and an item that crashes its worker stalls only its own shard.
4. **Given** two workers configured with different shard counts, **When** both dispatch the same scope, **Then** no item's effect appears more than once.
5. **Given** a worker whose leases come from Redis or the local substitute, **When** it starts, **Then** the dispatcher's feature is refused with `unsupported_consistency`, and the process exits 4 with event 7002 naming the feature.

---

### User Story 3 - Failing work retries, then is quarantined (Priority: P1)

A handler fails. Transient failures retry with backoff, permanent failures and exhausted retries poison the item, and an item that crashes its process every time still ends up poisoned.

**Why this priority**: Retry and poison handling is in the approved scope, and a poison item must not stop all other work.

**Independent Test**: Register test handlers that fail transiently, fail permanently, throw an unexpected exception, and crash the process. Assert the attempt counts, due times within the backoff bounds, the final `poisoned` state with the failure's class and code, that other items keep flowing, and that a poisoned item is never dispatched again.

**Acceptance Scenarios**:

1. **Given** a handler that fails with a transient class, **When** it is dispatched, **Then** the item is due again no earlier than the backoff for that attempt, and no later than `work.retry.maximumDelay` plus one poll interval.
2. **Given** a handler that declares a permanent failure, **When** it fails, **Then** the item is `poisoned` at once, with that class and code.
3. **Given** a handler that kills the process on every attempt, **When** the attempts reach the maximum, **Then** the item is `poisoned` and is never dispatched again.
4. **Given** a poisoned item in a scope, **When** other items in that scope and other scopes are due, **Then** they are dispatched as normal.
5. **Given** a handler that throws an unexpected exception on every attempt, **When** the attempts reach the maximum, **Then** each failed attempt was retried with backoff, and the item is `poisoned` with class `unclassified` and code `core.internal`.

---

### User Story 4 - Workers stay inside each scope (Priority: P1)

Work and its handlers run in their owner scope, and no worker, job, or directory read lets one tenant's work see or change another's.

**Why this priority**: Isolation at every worker and job boundary is non-negotiable (Constitution V, ADR-0005).

**Independent Test**: Enqueue items in two organizations and a project, and assert that each handler's unit is in its item's scope, that a tenant-scope unit reads nothing of another scope from the directory or another scope's items, that an operator-scope unit reads no tenant item, and that a unit cannot register another scope in the directory.

**Acceptance Scenarios**:

1. **Given** an item of organization A, **When** it is handled, **Then** the handler's unit is in A's scope, and a write it attempts to B's rows changes nothing.
2. **Given** a producer's unit in scope A, **When** it enqueues an item for scope B, **Then** it fails before any write with the scope-mismatch code.
3. **Given** a tenant-scope unit, **When** it reads the scope directory or tries to register or delete another scope's row, **Then** it sees no other scope's row, and registers or deletes nothing.
4. **Given** a malformed scope key in the directory, **When** the dispatcher reads it, **Then** it is skipped with a diagnostic, and no unit is opened for it in any scope.

---

### User Story 5 - Expired coordination rows are purged across scopes (Priority: P2)

An operator runs the worker, and expired atomic-operation and lease rows of every scope are removed over time, while fence records and namespace generations stay.

**Why this priority**: ISK-0005 leaves the purge's scheduling to this spec. Expired rows already count as absent, so this reclaims space rather than fixing correctness.

**Independent Test**: Create expiring coordination rows in several scopes, let them expire, run the purge job, and assert that they are gone, that fences and generations remain, that each scope's directory row is removed once nothing expiring remains, and that two workers never run the job at once.

**Acceptance Scenarios**:

1. **Given** expired rows in three scopes, **When** the job runs, **Then** every expired row is deleted, and every fence record and namespace generation remains.
2. **Given** two workers, **When** the job's interval passes, **Then** exactly one runs it, and the other finds the job's lease held.
3. **Given** a worker killed during the job, **When** another runs it, **Then** it finishes the purge with no error and no change beyond deleting expired rows.

---

### User Story 6 - The worker stops inside the grace period (Priority: P2)

An operator stops the worker. It stops taking work at once, lets in-flight work finish within the bound, abandons the rest safely, releases its leases, and exits before the orchestrator would kill it.

**Why this priority**: Shutdown is in scope, and ISK-0007 fixes the bound.

**Independent Test**: Start a worker with long-running test handlers, send `SIGTERM`, and assert readiness turns `stopping`, no new item is claimed, short items complete, long items are cancelled and remain `pending`, the leases are released, and the process exits 0 within `shutdownDelay` + `shutdownTimeout` + 5 seconds.

**Acceptance Scenarios**:

1. **Given** a running worker, **When** it receives `SIGTERM`, **Then** readiness is `stopping` and no item is claimed after the signal.
2. **Given** an item still running at `shutdownDelay` + `shutdownTimeout` after the signal, **When** that bound passes, **Then** its unit rolls back, the item stays `pending`, and another worker can take it as soon as it is due, without waiting for the lease to expire.
3. **Given** a correctly configured grace period, **When** the worker stops, **Then** it exits 0 with event 7005 before the grace period ends.

---

### User Story 7 - Operators trace and diagnose work (Priority: P2)

An operator follows a work item from the change that enqueued it, sees backlogs, retries, poisoned items, and version skew, and never sees a payload or tenant identifier in telemetry.

**Why this priority**: Tracing and operable diagnostics are in the approved scope.

**Independent Test**: Enqueue under an active trace, dispatch, and assert the dispatch activity's link to the enqueuing trace, the events and metrics for each outcome, and that no captured log, span, or metric holds a scope ID, item ID, idempotency key, or payload byte.

**Acceptance Scenarios**:

1. **Given** an item enqueued under trace T, **When** it is dispatched, **Then** the dispatch activity links to T and is not a child of it.
2. **Given** an item of a kind no handler knows, **When** the dispatcher sees it, **Then** it stays `pending`, its attempt count is unchanged, it is never in a claimed batch, other due items of its scope are dispatched as normal, and the skew metric and event report its kind.
3. **Given** any test run, **When** the captured telemetry is searched for the test's scope IDs, item IDs, idempotency keys, and payload markers, **Then** none is found.

---

### User Story 8 - The worker runs in the developer topology (Priority: P3)

A developer starts the developer topology and gets a `worker` service beside the planes, with the existing smoke answers unchanged.

**Why this priority**: ISK-0008's AV-7 asks for it, but no product handler exists yet, and no process can connect to PostgreSQL before ISK-0021.

**Independent Test**: Run ISK-0008's acceptance cycle with the revised topology and assert that the data-plane and control-plane readiness answers are byte for byte unchanged, that the worker's answer is the one TR-10 records, and that the worker's dispatcher is not refused.

**Acceptance Scenarios**:

1. **Given** a fresh clone, **When** the start script runs, **Then** `postgres`, `valkey`, `data-plane`, `control-plane`, and `worker` are running and healthy by their `health live` commands.
2. **Given** the running topology, **When** smoke runs, **Then** the two existing readiness answers match the local environment contract byte for byte, and the worker answers `503` with exactly the body its revision records.
3. **Given** the worker's logs, **When** they are searched, **Then** they hold no event 5002 and no event 7002.

### Edge Cases

- A producer enqueues and then its unit's commit outcome is unknown: the item exists if and only if the change does, and nothing retries the commit (persistence contract, Failures rule 4).
- The database is unreachable: the dispatcher and jobs back off on the poll, count no attempt they could not record, and poison nothing.
- A handler takes longer than its lease: the dispatcher renews the lease while the handler runs. If a renewal fails with `coordination.lease_lost`, the handler's unit is cancelled and commits nothing, the worker writes nothing more for that item, and the attempt keeps the class `abandoned` from its attempt record.
- A handler hangs: at its timeout it is cancelled, its unit rolls back, and the attempt counts as transient. Until then its shard's other items wait, because its unit holds the shard's fence row (`work` contract Dispatch and apply rule 5), while the scope's other shards keep running in the remaining in-flight slots. *Amended in `implement` on 2026-11-18 by the maintainer, who kept the guard's acceptance first in every unit: accepting after the handler would invert the lock order against the attempt and failure records.*
- An item crashes its worker on every attempt: only its shard stalls until that shard's lease expires, and the scope's other shards keep running on other workers, until the item is poisoned at its maximum attempts.
- The shard count is changed, or differs between workers during a rolling upgrade: items move between shards, and FR-013's conditional completion still lets at most one holder commit an item.
- The coordinator's clock steps: lease expiry moves with it, but the fencing guard compares tokens, so no stale commit is accepted.
- A payload over the limit, or an undeclared kind, fails the enqueue, and the producer's unit faults and commits nothing.
- A scope's directory row is removed while a producer registers new work for it: the removal and the producer's refresh are serialized on the row in the scope's own units, so either the producer's registration commits after the removal and recreates the row, or the removal sees the new item and keeps the row. No committed item is ever left without its row.
- An item becomes due during shutdown: it is not claimed, and stays for the next worker.
- A stop signal arrives during start-up: as ISK-0007's Lifecycle rule 2, nothing is claimed, and the stop sequence runs once the start-up finishes or is cancelled.
- A `migrate` run: no composition start-up runs, so no dispatcher or job runs.
- A `work` section in a data-plane or serving control-plane document: rejected with `config.namespace.unknown`, exit 2.
- Retention removes a `completed` item, and its producer later re-runs with the same idempotency key: a new item is enqueued. The dedupe window is `work.retention.completed`, and the contract states it.
- A handler is removed in a later release while items of its kind are pending: they stay `pending`, the skew metric reports them, and a contract revision says how its kind is retired.

## Requirements *(mandatory)*

### Functional Requirements

**Outbox**

- **FR-001**: A module MUST be able to enqueue a work item only inside an open unit of work, in the same transaction as its data change, so that the item exists after commit and does not exist after rollback.
- **FR-002**: An enqueue MUST fail before any write when the unit's scope differs from the item's, when the kind is not declared by the producing module's registration, when the payload exceeds the work contract's fixed limit of 65,536 bytes, or when the unit is not open, each with its registered `work.*` code, and the unit MUST then commit nothing.
- **FR-003**: An enqueue whose scope, kind, and idempotency key match an item still retained MUST write nothing and report the existing item.
- **FR-004**: Items MUST be stored in the `work` migration module's schema `isk_work`, in a scope-keyed table under row-level security, under persistence contract version 3. Nothing outside the PostgreSQL adapter's work framework namespace may name that schema.
- **FR-005**: The payload MUST never appear in a log, trace, metric, error, or readiness answer. The work contract MUST state that a payload holds no secret, credential, prompt, or response text.

**Scope directory**

- **FR-006**: Persistence contract version 3 MUST define the scope directory table kind: a unit may read, insert, refresh, or delete only the rows for its own exact scope, an operator-scope unit may also read every row, and changes only its own `operator` row (decided in `plan`), and rows hold only the canonical scope, a module name, and a due time. A row MUST be deleted only in its scope's own unit, by a delete conditional on that unit seeing no remaining work for the row's module, serialized with any concurrent refresh of the same row, so that no committed item or expiring row is ever left without its directory row. An expiring coordination row that already existed when the database was upgraded to this release is registered only when its scope next inserts a coordination row (decided in `checklist`).
- **FR-007**: The outbox MUST register the item's scope in the directory in the same unit as the enqueue. Coordination contract version 2 MUST require the PostgreSQL coordinator to register a scope in the same unit as any write that creates a row with an expiry.
- **FR-008**: The dispatcher and jobs MUST turn a directory row into a unit of work scope only through one framework-internal attestation that validates the scope's canonical form and yields exactly that scope. A malformed row MUST be skipped with an event, and MUST never produce an operator-scope or other-scope unit.
- **FR-009**: No operator-scope unit opened by this spec MAY read or write a tenant scope's work item or any tenant row. Every read and write of an item MUST happen in a unit of the item's own scope, so an operator-scope item is read and written only in an operator unit (decided in `plan`).

**Dispatch and apply**

- **FR-010**: The dispatcher MUST deliver each due `pending` item at least once to the one handler registered for its kind, and MUST NOT deliver a `completed` or `poisoned` item.
- **FR-011**: Each item MUST carry a stable bucket from 0 to 63, assigned at enqueue from its ID, and its shard MUST be its bucket modulo `work.dispatch.shards`. The dispatcher MUST hold the item's shard lease, the PostgreSQL-issued lease `work` / `dispatch.<shard>` (namespace `work`, key `dispatch.<shard>`) in the item's scope, while it handles the item, renew it while a handler runs, and cancel the handler's unit if a renewal fails. At most one process MUST hold a given shard's lease at a time. Lease keys and fence resources MUST be one per shard of a scope, never one per item, so at most 64 exist per scope. A worker MUST run at most `work.dispatch.concurrency` items at once, and at most `work.dispatch.scopeConcurrency` of one scope.
- **FR-012**: Before a handler runs, its attempt MUST be recorded in a committed unit, so a crash during the handler still counts the attempt.
- **FR-013**: A handler MUST run inside one unit of the item's scope, in which the fencing guard first accepts the shard lease's token for that shard's fence resource, then the handler writes, then the item is marked `completed` on the condition that it is still `pending`, all committed together. A stale token, a token from another lease key, or an item already `completed`, including one completed by the holder of another shard lease after a change of the shard count, MUST commit nothing.
- **FR-014**: A handler's registration MUST declare its kind, its supported payload versions, its maximum attempts, its timeout if it differs from `work.handler.timeout`, and whether it has any side effect outside its unit. A handler still running at its timeout MUST be cancelled, its unit MUST roll back, and the attempt MUST count as transient. A handler with such a side effect MUST receive the item's ID and idempotency key for its own idempotency.
- **FR-015**: An item whose kind has no registered handler, or whose payload version exceeds the handler's highest supported version, MUST stay `pending`, count no attempt, and be reported by an event and a metric. The dispatcher MUST claim only items whose kind and payload version it handles, and MUST find skewed items by a separate count, never by claiming them.

**Retry and poison**

- **FR-016**: A handler failure MUST be classified as transient or permanent. A transient failure MUST make the item due again after an exponential backoff with jitter, bounded by `work.retry.initialDelay` and `work.retry.maximumDelay`. A permanent failure, which the handler declares or which is an invalid payload, MUST poison the item at once. Any other failure, including an unexpected exception, MUST be classified `unclassified`, recorded with code `core.internal`, and retried as transient. An attempt that ends with no recorded result, through a crash or a lost shard lease, MUST keep the class `abandoned` and code `work.attempt_abandoned`, and is retried until the kind's maximum.
- **FR-017**: An item MUST be poisoned when its recorded attempts reach its kind's maximum, including attempts that ended in a process crash. A poisoned item MUST keep its last failure's class and code, never be delivered again, and be retained for `work.retention.poisoned`.
- **FR-018**: A poisoned item, or an item skewed under FR-015, MUST NOT delay any other item's dispatch, in its scope or any other.

**Recurring jobs**

- **FR-019**: A job run MUST hold an operator-scope PostgreSQL-issued lease named for the job, so at most one process runs a job at a time, and MUST fence its bookkeeping writes with that lease.
- **FR-020**: The `coordination.purge_expired` job MUST call `PurgeExpiredAsync` for every scope the directory lists under `coordination`, repeating per scope until a call returns fewer than its batch, and MUST remove a scope's `coordination` directory row only in that scope's own unit, under FR-006's conditional delete, when no expiring row remains. It MUST never delete a fence record or a namespace generation.
- **FR-021**: The `work.retention` job MUST delete `completed` items older than `work.retention.completed` and `poisoned` items older than `work.retention.poisoned`, one scope at a time, and remove a scope's `work` directory row, in that scope's own unit under FR-006's conditional delete, once no item remains.
- **FR-022**: Every job MUST be safe to repeat after a crash at any point, with no effect beyond completing its work.

**Coordination requirements**

- **FR-023**: The dispatcher and each job MUST declare a `FeatureRequirement` with leases at `strict`, fencing at `strict`, `MandatoryPath`, and `fail_closed`, and reach the lease coordinator and fencing guard only through `CoordinationGate`. The outbox MUST declare none.
- **FR-024**: No part of this spec MAY use an advisory lock as a lease, a fence, or mutual exclusion between processes.

**Worker command and shutdown**

- **FR-025**: Container contract version 2 MUST add the command `work` to the control-plane executable, and hosting contract version 3 the worker's composition plane and root, `AddIskenderWorker(...)`. It MUST make the serving control plane's registrations, with that root in place of `AddIskenderControlPlane(...)`, plus the dispatcher's, bind only the health listener, answer liveness and readiness with the five existing checks and body format, and use only the existing exit codes. `work` on the data plane MUST exit 64.
- **FR-026**: The data plane and the serving control plane MUST compose the outbox only, run no dispatcher and no job, and keep their readiness answers and stop sequences unchanged. The migration set of `migrate` MUST include the `work` module.
- **FR-027**: On the first stop signal, the worker MUST claim no further item or job, let in-flight work run until `shutdownDelay` + `shutdownTimeout` after the signal, then cancel it so its units roll back, release every lease it holds, and exit 0 within `shutdownDelay` + `shutdownTimeout` + 5 seconds of the signal.
- **FR-028**: A `migrate` run MUST run no dispatcher and no job.

**Configuration**

- **FR-029**: The dispatcher's registration MUST register the ISK-0003 namespace `work`, with the poll interval, batch size, lease duration (within ISK-0005's lease bounds), shard count (default 4, a divisor of 64), the per-worker and per-scope in-flight caps, handler timeout, retry, retention, and per-job interval and batch fields, each with a default that is a safety bound. The data plane and the serving control plane MUST reject a `work` section with `config.namespace.unknown`.

**Developer topology**

- **FR-030**: Local environment contract version 2 MUST add a `worker` service in the default mode, from the control-plane image, running `work` with its own read-only documents whose overlay selects `postgresql` for leases, with the planes' hardening and grace period, no published port, and dependence only on `postgres` and `valkey` being healthy.
- **FR-031**: The smoke script MUST assert the data-plane and control-plane readiness answers byte for byte as version 1 records them, and the worker's answer as version 2 records it, with no refused feature.

**Tracing and diagnostics**

- **FR-032**: Enqueue MUST record the current W3C trace context in the item. Each dispatch and job run MUST start an activity from `ActivitySource("Iskender.Work")`, a dispatch linked to the recorded context and not its child.
- **FR-033**: Events MUST be written under the category `Iskender.Work` with IDs from 8001, and metrics from `Meter("Iskender.Work")`, for enqueue, dispatch, completion, retry, poison, version skew, backlog, job runs, and malformed directory rows. Fields and labels MUST be limited to module, kind, job, outcome, failure class, error code, and attempt.
- **FR-034**: No log, trace, metric, error, or readiness answer MAY hold a scope ID, item ID, idempotency key, payload, or configuration value. The container entry points MUST subscribe to `Iskender.Work` as they do to `Iskender.Persistence` and `Iskender.Coordination`.

**Contracts, compatibility, and documentation**

- **FR-035**: This spec MUST publish the `work` contract, version 1, and the revisions persistence version 3, coordination version 2, container version 2, local environment version 2, and hosting version 3 in its `contracts/` directory, each recording its change in its table, and repoint the links in `docs/03` and `docs/04`.
- **FR-036**: The `work.*` error codes MUST be added with `since: 6`, raising `core-contracts` from 5 to 6 with compatible changes only, and the compatibility harness MUST pass. No public member of `Iskender.Hosting` may change.
- **FR-037**: The registration methods for the outbox and the dispatcher MUST be embeddable, follow the hosting contract's Registration rule 9, and be documented in `docs/04`'s embedding guide, so an embedded host may compose the dispatcher under the same coordination requirements.
- **FR-038**: `docs/04` MUST gain a background work runbook: running the worker, its configuration, its readiness and exit codes, retry and poison behavior, the jobs, the dedupe window, diagnostics, sizing the connection pool against the in-flight caps, rollback, and the known limits.
- **FR-039**: Every port and adapter type this spec adds MUST name ISK-0009 as its owning spec in its XML comment.

**Test fixture**

- **FR-040**: The work fixture MUST run two or more independent dispatchers against one database from the ISK-0004 fixture, inject process kills and unit abandonment at each of these points: after the producer's commit, before and after the attempt record's commit (FR-012), during the handler before the apply's commit and after the apply's commit (FR-013), and before and after the failure record's commit (FR-016) (refined in `checklist`), pause a dispatcher past its lease's expiry, force duplicate claims, run dispatchers with different shard counts, and hang or crash a handler in one shard while the others run. Tests that need a container runtime MUST follow ISK-0005's rule.

### Negative Security and Failure Scenarios

| Threat or failure | Required behavior | Covered by |
|---|---|---|
| Crash after the handler's commit | No redelivery, one effect | FR-013, User Story 1 |
| Crash before the commit | Rollback, redelivery, one effect | FR-012, FR-013, User Story 1 |
| Duplicate delivery to two workers | At most one commit | FR-011, FR-013, User Story 2 |
| A paused worker resumes after its lease expired | Fencing rejects its token, nothing commits | FR-013, User Story 2 |
| A worker given Redis or local leases | Refused at start, exit 4 | FR-023, User Story 2 |
| A producer enqueues into another scope | Fails before any write | FR-002, User Story 4 |
| A handler reads or writes another tenant's rows | Its unit is in the item's scope, so row-level security shows nothing | FR-009, FR-013, User Story 4 |
| A tenant reads the scope directory, or registers or deletes another scope's row | Sees no other scope's row, changes nothing | FR-006, User Story 4 |
| A tampered directory row | Skipped, never widened | FR-008, User Story 4 |
| An operator worker reads tenant items | Not possible: items are read only in their scope | FR-009 |
| A poison item loops forever, or crashes its worker forever | Durable attempt count, then poisoned | FR-012, FR-017, User Story 3 |
| A poison item blocks other work | Others keep flowing | FR-018 |
| A rolling upgrade poisons items an older worker does not know | Left pending, never claimed, reported | FR-015, User Story 7 |
| Skewed items fill each batch and starve a scope | Only handled kinds and versions are claimed | FR-015, FR-018, User Story 7 |
| Payload or tenant identifiers in telemetry | Excluded by vocabulary, tested | FR-005, FR-034, User Story 7 |
| A database outage turns into poisoned items | No attempt is counted unless recorded, backoff on poll | FR-012, Edge Cases |
| The worker is killed mid-stop | Within a correct grace period it exits first, otherwise TR-6 still holds | FR-027, FR-013 |
| Two processes run a job at once | One lease holder | FR-019, User Story 5 |
| The purge deletes fences or generations | Never | FR-020 |
| Background work in a `migrate` run | None | FR-028 |
| A worker breaks the developer smoke answers | Existing answers byte for byte, worker not refused | FR-030, FR-031 |

### Key Entities

- **Work item**: one unit of durable work, owned by one scope, of one kind and payload version, with an idempotency key, due time, attempt count, state, last failure, and trace context.
- **Kind declaration**: a module's statement that it may enqueue a kind, with its payload versions.
- **Handler registration**: the one handler for a kind, with its supported versions, maximum attempts, failure classification, and external side-effect flag.
- **Scope directory row**: a canonical scope, a module name, and a due time, readable across scopes only by operator scope, and readable and removable by its own scope.
- **Recurring job**: a named, interval-driven maintenance task, run by one lease holder at a time, with fenced bookkeeping.
- **Worker**: a control-plane process run with `work`, holding the dispatcher and the jobs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the crash test, every item enqueued by a committed unit ends `completed` with exactly one recorded effect, and no item exists for a rolled-back unit, across every injection point of FR-040 and at least 20 kill cycles per point: 0 lost and 0 double-applied items.
- **SC-002**: With two or more dispatchers, forced duplicate claims, a dispatcher paused past its lease's expiry, and dispatchers with different shard counts, 0 items have more than one recorded effect, and every stale commit is rejected. With one scope's items in every shard and as many dispatchers as shards, more than one dispatcher handles that scope's items, and no worker exceeds its in-flight caps.
- **SC-003**: A handler that crashes its process every time is poisoned after exactly its maximum attempts, and is dispatched 0 times afterwards.
- **SC-004**: Every transient retry's due time falls within the backoff bounds of FR-016, every permanent failure poisons on its first attempt, and an `unclassified` failure poisons after exactly its maximum attempts.
- **SC-005**: The isolation tests show 0 rows of another scope read or changed by a handler, 0 directory rows of another scope visible to a tenant unit, 0 committed items or expiring rows left without a directory row in the removal race test, and 0 directory rows registered for another scope, with the repository predicate removed as well as present.
- **SC-006**: After the purge job, 0 expired coordination rows remain in the scopes tested, and every fence record and namespace generation remains.
- **SC-007**: A worker stopped with `SIGTERM` claims 0 items after the signal and exits 0 within `shutdownDelay` + `shutdownTimeout` + 5 seconds in every stop test.
- **SC-008**: The captured telemetry of the whole test run holds 0 scope IDs, item IDs, idempotency keys, or payload markers.
- **SC-009**: The developer topology's five-cycle acceptance run passes with the data-plane and control-plane readiness answers byte for byte unchanged and the worker's answer as recorded, and 0 refused features.
- **SC-010**: The compatibility harness, the architecture tests, and the 10 CI checks pass, with `core-contracts` at version 6 and no change to the `hosting` contract package.

## Assumptions

- No product handler exists in this spec. Its handlers are its two jobs and the test fixture's. Later specs add their own handlers under the `work` contract (AV-5).
- Delivery is at least once with at-most-once apply inside the database. Effects outside the database are each handler's own idempotency obligation.
- Polling is enough at MVP scale, and latency is not optimized until measured (AV-2).
- Before ISK-0021, no process can connect to PostgreSQL, so the worker is not ready and dispatches nothing. Its tests run against the ISK-0004 fixture, which provides its own credentials in test projects only.
- The scope directory is the only cross-scope read this spec adds, and it reveals only that a scope has pending work or expiring rows.

## Out of Scope

- A general event bus, publish/subscribe, consumer groups, fan-out to every replica, or a replayable log (Q3; ISK-0057 owns reaching every data-plane replica).
- Kafka, a cloud queue or bus, or any external broker or adapter.
- Product handlers: configuration publication (ISK-0057), usage and budget reconciliation (ISK-0063), stream recovery (ISK-0049), audit retention (ISK-0026), and provider batch jobs (ISK-0111).
- A management API or UI for poisoned items, and the audited elevated operator operation over tenant data.
- Credential providers (ISK-0021), and the worker's ready state, which follows them.
- Ordered delivery, exactly-once external effects, and latency optimizations such as `LISTEN`/`NOTIFY`.
- Any change to CEL typed policies, plugin trust, durable conversations, financial controls, or streaming. Their specs use this spec's contract under their own authoritative documents.
- Any new image, executable, or Dockerfile.
