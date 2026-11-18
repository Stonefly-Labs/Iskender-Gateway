# Work Contract

**Version**: 1

**Owning spec**: ISK-0009 — Module Events & Background Work Coordination

**Review owner**: Architecture (ADR) owner, with the Security owner for Scopes and the directory, and the Privacy/Operations owner for Tracing and Diagnostics. See `CONTRIBUTING.md` for who holds each role.

This contract states what module authors, operators, and later specs may rely on from Iskender's durable internal work handoff: the outbox, the dispatcher, recurring jobs, and the worker that runs them. The type shapes are in [data-model.md](../data-model.md), and the reasons in [research.md](../research.md). Storage follows the [persistence contract](persistence.md), version 3, leases and fencing the [coordination contract](coordination.md), version 2, the worker's composition the [hosting contract](hosting.md), version 3, and its process the [container contract](container.md), version 2.

## Changing this contract

A change to a rule here is made by a later spec's revision of this contract, which increments the version, publishes the contract in that spec's `contracts/` directory, repoints the links in `docs/03-domain-and-technical-architecture.md` and `docs/04-technology-stack-and-deployment.md`, and records the change in this table. Renaming or removing a state, failure class, error code, event ID, metric, configuration field, lease name, or job name breaks every handler, dashboard, and manifest that reads it.

| Version | Spec | Change | Approval | Owner |
|---|---|---|---|---|
| 1 | ISK-0009 | First version | This spec's PR | Architecture (ADR) owner |

The ports are in the embeddable `Iskender.Domain`, so the ISK-0002 compatibility harness does not version them. This contract's version is the commitment to later specs. The error codes are in `Iskender.Core.Contracts` and are versioned by the harness.

## What this is not

*Review owner: Architecture (ADR) owner.*

1. It is a durable handoff of one item to one handler, not an event bus. There is no publish/subscribe, fan-out, consumer group, replayable log, or ordering between items (spec Q3).
2. It uses PostgreSQL alone for items, and PostgreSQL leases for dispatch. No broker, queue service, Kafka, or cloud SDK is used, and none may be added under this version.
3. Reaching every data-plane replica is ISK-0057's, for example through an ISK-0005 cache namespace generation bump made by a handler (spec AV-4).

## Ownership

*Review owner: Architecture (ADR) owner.*

1. The ports are in `Iskender.Domain.Ports`. No port signature names a driver, PostgreSQL, or ASP.NET Core type.
2. The outbox, the item store, the dispatcher, and the jobs are in `Iskender.Infrastructure.PostgreSql.Work`, which names only the schema `isk_work`. The scope directory is in `Iskender.Infrastructure.PostgreSql.Directory`, which names only `isk_directory` (persistence contract, Ownership rule 4).
3. The worker's composition root, `AddIskenderWorker(...)`, is in `Iskender.Management`, and the `work` command in `Iskender.ControlPlane.Host`.
4. Each port and adapter type's XML comment names ISK-0009 as its owning spec.

## Registration

*Review owner: Architecture (ADR) owner.*

| Method | Assembly | Module | Section | Requires | Composed by |
|---|---|---|---|---|---|
| `AddIskenderPostgreSqlWork()` | `Iskender.Infrastructure.PostgreSql` | `work-postgresql` | | `persistence-postgresql` | Data plane, control plane, worker |
| `AddIskenderPostgreSqlWorkDispatcher()` | `Iskender.Infrastructure.PostgreSql` | `work-dispatcher` | `work` | `work-postgresql`, `coordination-postgresql` | Worker only |

1. `AddIskenderPostgreSqlWork()` adds the outbox and the migration modules `directory` and `work`. It declares no `FeatureRequirement`, because it uses only the caller's unit.
2. `AddIskenderPostgreSqlWorkDispatcher()` adds the dispatcher, its composition start-up, the two shipped jobs, and their requirements (Leases and fencing rule 1), and registers the `work` section. A plane that does not compose it rejects a `work` section with `config.namespace.unknown` (container contract, Configuration roots rule 8).
3. A producing module declares each kind it enqueues with `WorkRegistrations.AddKind`, and the one handler of a kind is registered with `WorkRegistrations.AddHandler<THandler>`. A later job is registered with `WorkRegistrations.AddJob<TJob>`. A second handler for a kind, or a second job of a name, fails at the call with `hosting.registration_duplicate`.
4. Handlers and jobs are resolved from the host's container. A handler is registered in every process that may dispatch its kind, and a kind declaration in every process that may enqueue it.
5. Both methods are embeddable, and follow the hosting contract's Registration rule 9. An embedded host may compose the dispatcher under the same requirements, so its leases must be PostgreSQL's.

## Work items

*Review owner: Architecture (ADR) owner.*

1. A work item has an opaque ID (a UUID version 7), its owner scope, the producing module, a kind `<module>.<name>`, a payload version from 1 to 1,000, a payload, an idempotency key, a bucket, a due time, an attempt count, a state, its last failure's class and code, and the W3C trace context current at enqueue.
2. **The payload is at most 65,536 bytes**, in every plane, and is not configurable under this version (maintainer decision in `plan`, 2026-11-17). The producing module serializes it. It holds no secret, credential, prompt, or response text, and it never appears in a log, trace, metric, error, or readiness answer.
3. The idempotency key is 1 to 128 characters of `A-Z`, `a-z`, `0-9`, `.`, `_`, `:`, and `-`, and is unique per scope and kind among retained items.
4. The bucket is the low 6 bits of the first byte of SHA-256 over UTF-8 `iskender.work.bucket.v1`, `0x00`, and the item ID's lowercase 36-character text. It is fixed at enqueue.
5. The states are `pending`, `completed`, and `poisoned`. Only `pending` items are dispatched. `completed` and `poisoned` are final, and only retention removes them.

## The outbox

*Review owner: Architecture (ADR) owner; Security owner for rule 2.*

1. `IWorkOutbox.EnqueueAsync(unit, request)` writes the item and its directory registration in `unit`'s transaction, so the item exists after the unit commits and does not exist after it rolls back. Nothing retries an unknown commit outcome (persistence contract, Failures rule 4).
2. It fails before any write, faulting the unit so that it commits nothing:

   | Condition | Code |
   |---|---|
   | The unit is not open | `work.unit_not_open` |
   | `request.Owner` is not the unit's scope | `work.scope_mismatch` |
   | The kind is not declared, or its payload version is above the declared highest | `work.kind_undeclared` |
   | The payload is over 65,536 bytes | `work.payload_too_large` |

3. An enqueue whose scope, kind, and idempotency key match a retained item writes nothing and returns that item's ID with `Created` `false`. Two producers racing with one key leave one item.
4. An item may be owned by any scope, the operator's included. An operator-scope item is written and read only in an operator unit (maintainer decision in `plan`, 2026-11-17).
5. `request.Delay` makes the item due that long after the enqueue, measured on the database's clock.
6. **The dedupe window**: once retention deletes an item, its key enqueues a new item. The window is `retention.completed` for a completed item and `retention.poisoned` for a poisoned one.

## Scopes and the directory

*Review owner: Security owner.*

1. Every read and write of an item happens in a unit of the item's own scope, under the scope-keyed table's row-level security. No operator unit reads or writes a tenant scope's item or any tenant row (FR-009, as reworded in `plan`).
2. The outbox registers the item's scope in the directory under module `work`, and the PostgreSQL coordinator registers a scope under module `coordination` when a write inserts a row with an expiry (coordination contract, Atomic operations rule 7). The directory's table kind, and its register and settle operations, are persistence contract Isolation rules 9 and 10.
3. The only cross-scope read is an operator unit's read of directory rows, which reveals only that a scope has work or expiring rows.
4. The dispatcher and the jobs turn a directory row into a unit scope only through the framework-internal `DirectoryScopeResolver`, an ISK-0002 ownership resolver. It accepts only the canonical forms `operator`, `org:<id>`, and `project:<org id>/<project id>`, with IDs of the right type in canonical text, and yields exactly that scope. A malformed row is skipped with event 8009, and no unit is opened for it in any scope.
5. A directory row is removed only by a settle in its scope's own unit, when no work of its module remains: no item of the scope in any state for `work`, and no atomic-operation or lease row for `coordination`.

## Dispatch and apply

*Review owner: Architecture (ADR) owner.*

1. Each `pending` item is delivered at least once to the one handler registered for its kind, and its effects inside the database are applied at most once.
2. **Shards**: a scope has `dispatch.shards` shards, and an item's shard is its bucket modulo that count. The shard lease is the PostgreSQL-issued lease `work` / `dispatch.<shard>` in the item's scope, and its fence resource has the same name, so a scope has at most 64 lease keys and 64 fence records, whatever its number of items.
3. A worker holds a shard's lease while it handles that shard's items, renews it every third of `leaseDuration`, and cancels every in-flight handler of the shard when a renewal fails with `coordination.lease_lost`. It runs at most `dispatch.concurrency` items at once, and at most `dispatch.scopeConcurrency` of one scope.
4. **Attempt record**: before a handler runs, a unit of the item's scope accepts the lease's token through the fencing guard, adds 1 to the attempt count, sets the due time to the attempt's backoff (Retry rule 2), records the class `abandoned` and code `work.attempt_abandoned`, and commits. All of it is conditional on the item being `pending` with the attempt count the claim read. A crash after this commit still counts the attempt.
5. **Apply**: one unit of the item's scope accepts the token, locks the item while it is `pending` with this attempt's count, runs the handler with that unit, and marks the item `completed`, under the same condition, then commits. A stale token, a token of another lease key, or an item already moved by another holder, including the holder of another shard's lease after a change of shard count, commits nothing.
6. **Failure record**: when the handler fails, a third unit accepts the token and records the failure's class and code, poisoning the item when Retry rule 3 or 4 says so, under the same condition.
7. The handler's unit is open, fenced, and in the item's scope. The handler writes only through it, and neither commits nor disposes it.
8. A handler that is still running at its timeout, the registration's `Timeout` or `handler.timeout`, is cancelled. Its unit rolls back, and the attempt is transient with code `work.handler_timeout`.
9. A handler with an effect outside its unit declares `HasExternalEffects`, and makes that effect idempotent itself, keyed by the delivery's `ItemId` and `IdempotencyKey`. Exactly-once external effects are not offered.
10. Every dispatch decision uses the database's clock. A coordinator clock step moves lease expiry but cannot make the fencing guard accept a stale token.

## Retry and poison

*Review owner: Architecture (ADR) owner.*

1. A handler's outcome is classified:

   | Outcome | Class | Code recorded |
   |---|---|---|
   | `WorkFailures.Permanent(code)`, including `work.payload_invalid` | `permanent` | Its code |
   | `persistence.unavailable`, `persistence.timeout`, `persistence.concurrency_conflict`, or a `coordination.*` code other than `coordination.capability_unsupported` | `transient` | Its code |
   | The handler's timeout | `transient` | `work.handler_timeout` |
   | Cancellation at shutdown | `transient` | `work.handler_cancelled` |
   | Any other exception, any other code | `unclassified` | `core.internal` |
   | A crash or kill after the attempt record | `abandoned` | `work.attempt_abandoned` |

2. **Backoff**: for attempt `n`, `d(n) = min(retry.maximumDelay, retry.initialDelay × 2^(n−1))`, and the item becomes due again after a delay drawn uniformly from `[max(retry.initialDelay, d(n) ÷ 2), d(n)]`.
3. A `permanent` failure poisons the item at once.
4. Any other class retries until the attempt count reaches the kind's maximum, the registration's `MaximumAttempts` or `retry.maximumAttempts`. The failure record that reaches it poisons the item, and an item found at its maximum by a claim, because its last attempt crashed, is poisoned by that claim's attempt record without running.
5. A poisoned item keeps its last failure's class and code, is never delivered again, and is kept for `retention.poisoned`.
6. A poisoned item never delays another item, in its scope or any other.
7. An attempt is counted only when its attempt record commits. While the database is unreachable, nothing is counted and nothing is poisoned, and the dispatcher retries its poll at `pollInterval`.

## Version skew

*Review owner: Architecture (ADR) owner.*

1. A worker claims only items whose kind it has a handler for, with a payload version within the handler's lowest and highest.
2. Any other due `pending` item is left untouched, counts no attempt, never occupies a claimed batch, and is found by a separate count that feeds the gauge `iskender.work.items.skewed` and event 8008.
3. A handler supports every payload version it has been published with. Retiring a version or a kind is a revision of this contract, which states how pending items of it are handled.

## Recurring jobs

*Review owner: Architecture (ADR) owner.*

1. A job has a name `<module>.<name>`, an interval, and a batch size. A run holds the operator-scope PostgreSQL lease `work` / `job.<name>`, and every write to its bookkeeping row in `isk_work.job_runs`, its start, cursor, and finish, first passes the fencing guard for the resource `work` / `job.<name>` with that lease. Two processes never run one job at once.
2. A job is due when its interval has passed since its last start, across every worker.
3. Every job is safe to repeat after a crash at any point, with no effect beyond finishing its work. A run resumes at its saved cursor.
4. **`coordination.purge_expired`** (default interval `5m`, batch 500): for each scope with a due `coordination` directory row, calls `PurgeExpiredAsync(scope, batch)` until a call returns fewer than `batch`, then `SettleDirectoryAsync(scope)`. The deletes run in `CoordinationMaintenance`'s own units, unfenced. It never deletes a fence record or a namespace generation.
5. **`work.retention`** (default interval `1h`, batch 500): for each scope with a `work` directory row, deletes `completed` items finished more than `retention.completed` ago and `poisoned` items finished more than `retention.poisoned` ago, `batch` at a time, then settles the scope's `work` row.

## Leases and fencing

*Review owner: Architecture (ADR) owner.*

1. The dispatcher registers `FeatureRequirement("work.dispatch", "work", leases: strict, fencing: strict, MandatoryPath, fail_closed)`, and each job `work.job.<name>` the same way. Both reach leases and the guard only through `CoordinationGate.ForFeature`.
2. So a worker whose leases are `redis` or `local` is refused with `unsupported_consistency` at start, and exits 4 with event 7002 naming the feature. A plane that only enqueues is never refused.
3. No part of this contract uses an advisory lock as a lease, a fence, or mutual exclusion between processes.

## Shutdown

*Review owner: Privacy/Operations owner.*

1. On the first stop signal, the dispatcher and the jobs claim no further item and start no further run.
2. In-flight items may finish until the host's shutdown timeout after the signal. In a container that is `shutdownDelay` + `shutdownTimeout` (container contract, Lifecycle). Then they are cancelled: each unit rolls back, the item stays `pending`, and its attempt is transient with code `work.handler_cancelled`.
3. The work start-up's stop, which runs before the coordination start-up's, releases every lease it holds, so another worker can take the items at once. A failure record or release that cannot finish within 2 seconds of the deadline is abandoned, and the lease expires on its own.
4. The worker exits 0 within `shutdownDelay` + `shutdownTimeout` + 5 seconds of the signal. A grace period shorter than that kills it mid-stop, and the items stay safe under Dispatch and apply rules 4 to 6.
5. A `migrate` run starts no composition start-up, so it runs no dispatcher and no job.

## Configuration

*Review owner: Architecture (ADR) owner.*

The namespace `work`, version 1, registered by `AddIskenderPostgreSqlWorkDispatcher()`. Every default is a safety bound, not a performance figure.

| Field | Type | Default | Constraints |
|---|---|---|---|
| `pollInterval` | duration | `5s` | `1s` to `5m` |
| `batchSize` | integer | 32 | 1 to 1,000; directory rows per page, and items per shard claim |
| `leaseDuration` | duration | `30s` | `5s` to `10m`, and within `coordination.leases.minimumDuration` and `maximumDuration`, checked when read |
| `dispatch.shards` | integer | 4 | 1 to 64, a divisor of 64, checked when read |
| `dispatch.concurrency` | integer | 16 | 1 to 256 |
| `dispatch.scopeConcurrency` | integer | 4 | 1 to 256, at most `dispatch.concurrency`, checked when read |
| `handler.timeout` | duration | `30s` | `1s` to `15m`. A registration's `Timeout`, from `1s` to `1h`, replaces it for its kind |
| `retry.initialDelay` | duration | `1s` | `100ms` to `1h` |
| `retry.maximumDelay` | duration | `5m` | `1s` to `24h`, at least `retry.initialDelay`, checked when read |
| `retry.maximumAttempts` | integer | 8 | 1 to 100. A registration's `MaximumAttempts` replaces it for its kind |
| `retention.completed` | duration | `7d` | `1h` to `365d` |
| `retention.poisoned` | duration | `30d` | `1d` to `365d` |
| `jobs.purgeExpired.interval` | duration | `5m` | `1m` to `24h` |
| `jobs.purgeExpired.batch` | integer | 500 | 1 to 10,000 |
| `jobs.retention.interval` | duration | `1h` | `1m` to `24h` |
| `jobs.retention.batch` | integer | 500 | 1 to 10,000 |

A check made when the settings are read fails with `config.field.out_of_range`, naming the field and no value, and the container exits 2. A later job's interval and batch come from its registration's defaults until its own spec adds fields for them.

## Tracing

*Review owner: Privacy/Operations owner.*

1. Enqueue records `Activity.Current`'s W3C `traceparent`, and its `tracestate` when it is at most 512 characters.
2. A dispatch starts the activity `work.dispatch` from `ActivitySource("Iskender.Work")`, of kind `Consumer`, with no parent and one link to the recorded context. A recorded context that does not parse adds no link. Its tags are `iskender.work.kind`, `iskender.work.attempt`, and `iskender.work.outcome`.
3. A job run starts `work.job`, tagged `iskender.work.job` and `iskender.work.outcome`.
4. The recorded context is used for the link and nothing else.

## Error codes

*Review owner: Architecture (ADR) owner.*

All are added to `ErrorCodes` with `since: 6`, raising `core-contracts` from 5 to 6 with compatible changes only, and to the ISK-0002 contract's Error codes table.

| Code | Category | Meaning | Safe message |
|---|---|---|---|
| `work.unit_not_open` | `internal` | A work item was enqueued on a unit of work that is not open | A work item could not be recorded. |
| `work.scope_mismatch` | `internal` | A work item's owner scope differs from its unit of work's | A work item could not be recorded in this scope. |
| `work.kind_undeclared` | `internal` | A work item's kind, or its payload version, was not declared by its module | A work item of this kind cannot be recorded. |
| `work.payload_too_large` | `internal` | A work item's payload is over 65,536 bytes | A work item is too large to be recorded. |
| `work.payload_invalid` | `internal` | A handler could not read a work item's payload, a permanent failure | A work item could not be processed. |
| `work.handler_timeout` | `deadline_exceeded` | A handler did not finish within its timeout | A work item handler did not finish in time. |
| `work.handler_cancelled` | `cancelled` | A handler was cancelled because its worker was stopping | A work item handler was cancelled. |
| `work.attempt_abandoned` | `internal` | An attempt ended with no recorded result, such as a crash | A work item attempt did not finish. |

## Diagnostics

*Review owner: Privacy/Operations owner.*

Fields and labels are limited to module, kind, job, outcome, failure class, error code, and attempt. No scope ID, item ID, idempotency key, payload, or configuration value appears in any field or label. Kinds and job names are code-defined. Category `Iskender.Work`. Templates and IDs are fixed.

| Event ID | Name | Level | Template |
|---|---|---|---|
| 8001 | `WorkDispatcherStarted` | Information | `Iskender work dispatcher started.` |
| 8002 | `WorkDispatcherStopping` | Information | `Iskender work dispatcher stopping.` |
| 8003 | `WorkDispatcherStopped` | Information | `Iskender work dispatcher stopped: outcome {Outcome}.` |
| 8004 | `WorkItemEnqueued` | Debug | `Iskender work item enqueued: kind {Kind}, outcome {Outcome}.` |
| 8005 | `WorkItemCompleted` | Debug | `Iskender work item completed: kind {Kind}, attempt {Attempt}.` |
| 8006 | `WorkItemRetryScheduled` | Information | `Iskender work item retry scheduled: kind {Kind}, attempt {Attempt}, class {FailureClass}, code {Code}.` |
| 8007 | `WorkItemPoisoned` | Warning | `Iskender work item poisoned: kind {Kind}, attempt {Attempt}, class {FailureClass}, code {Code}.` |
| 8008 | `WorkItemsSkewed` | Warning | `Iskender work items skipped for version skew: kind {Kind}, outcome {Outcome}.` |
| 8009 | `WorkDirectoryRowMalformed` | Warning | `Iskender skipped a malformed scope directory row: module {Module}.` |
| 8010 | `WorkApplyDiscarded` | Information | `Iskender work item apply discarded: kind {Kind}, attempt {Attempt}, outcome {Outcome}.` |
| 8011 | `WorkJobRun` | Information | `Iskender work job run: job {Job}, outcome {Outcome}.` |
| 8012 | `WorkPassFailed` | Warning | `Iskender work pass failed: class {FailureClass}, code {Code}.` |

1. 8003's `{Outcome}` is `drained` or `cancelled`. 8004's is `created` or `existing`. 8008's is `appeared` or `cleared`, written once per kind each time it changes in the process. 8010's is `fencing_rejected`, `superseded`, or `lease_lost`. 8011's is `completed`, `partial`, `held`, or `failed`.
2. 8012 is written when a poll or job step fails before any item, once per change of class and code, and again when a pass succeeds.
3. Metrics from `Meter("Iskender.Work")`:

   | Instrument | Kind | Labels |
   |---|---|---|
   | `iskender.work.items.enqueued` | Counter | `kind`, `outcome` |
   | `iskender.work.items.dispatched` | Counter | `kind` |
   | `iskender.work.items.completed` | Counter | `kind` |
   | `iskender.work.items.retried` | Counter | `kind`, `failure_class`, `error_code` |
   | `iskender.work.items.poisoned` | Counter | `kind`, `failure_class`, `error_code` |
   | `iskender.work.items.skewed` | Observable gauge | `kind` |
   | `iskender.work.backlog` | Observable gauge | `kind`: due `pending` items this worker can handle, as of its last pass |
   | `iskender.work.dispatch.duration` | Histogram, seconds | `kind`, `outcome` |
   | `iskender.work.job.runs` | Counter | `job`, `outcome` |

4. The container entry points subscribe the OpenTelemetry SDK to `Iskender.Work`, as they do to `Iskender.Persistence` and `Iskender.Coordination`.

## Test fixture

*Review owner: Performance owner (+ QA).*

1. `tests/Iskender.Work.Tests` provides the work fixture, built on the ISK-0004 PostgreSQL fixture: two or more independent dispatchers against one database, a test handler that records each apply in its own table through the handler's unit, injection points before and after each commit of Dispatch and apply rules 4 to 6, a dispatcher paused past its lease's expiry, forced duplicate claims, dispatchers with different shard counts, and a handler that hangs or crashes in one shard while the others run.
2. `tests/Iskender.Work.CrashHost` is a test-role console program that runs one dispatcher against a named database and reports each injection point it reaches. The crash tests kill it with `SIGKILL` at each point, at least 20 times per point.
3. Later specs test their handlers with the same fixture.
4. Tests that need a container runtime follow ISK-0005's rule: skipped with their reason without one, and failing when `CI` is `true`.

## Threat model and known limits

### What the rules defend against

The Negative Security and Failure Scenarios table in [spec.md](../spec.md) lists each threat with the requirement that covers it. This contract's rules implement them: lost work on a crash (The outbox 1, Dispatch and apply 4), double-applied work (Dispatch and apply 5), stale and duplicate workers (Dispatch and apply 2, 5), a worker without fencing (Leases and fencing 2), cross-scope reads and writes (Scopes and the directory 1, 4), directory tampering (Scopes and the directory 4), poison loops (Retry and poison 4), poison and skew starving other work (Retry and poison 6, Version skew 2), identifiers in telemetry (Diagnostics), outages turning into poison (Retry and poison 7), and a kill during stop (Shutdown 4).

### Known limits

- **Polling**: the delay from commit to dispatch is up to one `pollInterval` when every due scope fits in one directory page, plus backoff. No latency figure is asserted (spec AV-2).
- **A crashing item stalls its shard**: until its shard's lease expires, its other items wait. The scope's other shards keep running.
- **No ordering**: items of one scope, kind, or key run in any order and concurrently.
- **External effects**: a handler's effects outside its unit may run more than once, and their idempotency is the handler's.
- **Directory registration cost**: an enqueue adds one or two statements, and a coordination write that inserts a new expiring row adds one insert. No figure is asserted.
- **Skewed items keep a scope due**: a scope whose only due items are skewed is visited every poll until a newer worker handles them.
- **Not ready before ISK-0021**: no credential provider exists, so no process can connect to PostgreSQL, and the worker dispatches nothing.

## Left to later specs

| Item | Spec |
|---|---|
| Product handlers: configuration publication, usage reconciliation, stream recovery, audit retention, provider batch jobs | ISK-0057, ISK-0063, ISK-0049, ISK-0026, ISK-0111 (spec AV-5) |
| Reaching every data-plane replica | ISK-0057 (spec AV-4) |
| A management API or UI for poisoned items | The first management API spec |
| The worker's ready state in the developer topology | ISK-0021 (spec AV-6) |
| `LISTEN`/`NOTIFY` wake-ups, ordered delivery, calendar schedules | Post-MVP |
