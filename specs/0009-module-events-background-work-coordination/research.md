# Research: ISK-0009 — Module Events & Background Work Coordination

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-11-17

Each decision records what was chosen, why, and what was rejected. Four were decided by the maintainer during `plan` on 2026-11-17 (R2, R11, R14, and R2's operator-scope items), and they are also recorded in the spec's Clarifications. The rest follow from the spec, the approved contracts, and the code.

## R1. Where the code lives

**Decision**: No new project.

| Part | Home |
|---|---|
| Port types: work kinds, item requests, the outbox, handlers, deliveries, jobs, failures | `Iskender.Domain.Ports` |
| The outbox, the item store, the dispatcher, the jobs, the `work` section, the `work` migration module | `Iskender.Infrastructure.PostgreSql.Work`, a new framework namespace of the PostgreSQL adapter, naming only `isk_work` |
| The scope directory, its table kind, its attestation, the `directory` migration module | `Iskender.Infrastructure.PostgreSql.Directory`, a new framework namespace, naming only `isk_directory` |
| The directory registration of expiring coordination rows, and the coordination directory settle | `Iskender.Infrastructure.PostgreSql.Coordination`, unchanged in what it names |
| The worker's composition root, `AddIskenderWorker(...)` | `Iskender.Management` |
| The `work` serving command | `Iskender.ControlPlane.Host`'s `Program.cs`, with serving-command support in `Iskender.Infrastructure.Container` |
| `CompositionPlane.Worker` | `Iskender.Domain.Ports` |

**Rationale**: every step of the dispatcher is a PostgreSQL unit of work with a PostgreSQL-issued lease, because only the PostgreSQL coordinator offers fencing (coordination contract, Consistency rule 2). A store-neutral dispatcher in `Iskender.Infrastructure` would need a store-neutral item port with exactly one implementation, and an adapter cannot reference another adapter, so a separate `Iskender.Infrastructure.Work` project could not reach the PostgreSQL unit. The persistence contract's Ownership rule 4 already names framework namespaces per schema, so two new ones follow the existing pattern. No edge, home, role, or rule of the module dependency rules changes, so the rules contract stays at version 5.

**Rejected**: a new adapter project (cannot reach `Iskender.Infrastructure.PostgreSql`); putting the dispatcher in `Iskender.Infrastructure` behind a new item-store port (one implementation, more surface); placing the directory inside `isk_work` (the coordination namespace would then write another module's schema, and `coordination` would depend on `work`).

## R2. The scope directory table kind

**Decision**: migration module `directory`, schema `isk_directory`, one table:

```sql
CREATE TABLE scopes (
    listed_scope text NOT NULL,
    module text NOT NULL CHECK (module ~ '^[a-z][a-z0-9_]{0,30}$'),
    due_at timestamptz,
    PRIMARY KEY (listed_scope, module)
);
```

Row-level security is enabled and forced, with exactly four permissive policies, where `<own>` is the scope-keyed expression of persistence contract Isolation rule 8 (version 3 numbering) applied to `listed_scope`:

| Policy | Command | Expression |
|---|---|---|
| `scopes_read` | `SELECT` | `USING (listed_scope = <own> OR nullif(current_setting('iskender.scope_kind', true), '') = 'operator')` |
| `scopes_insert` | `INSERT` | `WITH CHECK (listed_scope = <own>)` |
| `scopes_update` | `UPDATE` | `USING (listed_scope = <own>) WITH CHECK (listed_scope = <own>)` |
| `scopes_delete` | `DELETE` | `USING (listed_scope = <own>)` |

So every unit, the operator's included, inserts, refreshes, locks, and deletes only its own scope's rows, and an operator unit also reads every row (**Decided by the maintainer in `plan` on 2026-11-17**: operator units write only the `operator` row, as every scope writes only its own). PostgreSQL applies the `UPDATE` policy's `USING` to `SELECT ... FOR UPDATE` and `FOR KEY SHARE`, so an operator unit cannot lock a tenant row either. The column is `listed_scope`, not `scope_key`, so that the migration runner tells the kind apart by column name, as it does today, and checks this exact set of four policies instead of the scope-keyed kind's single policy.

A new `TableAccess` kind, `ScopeDirectoryTable`, may be used in every scope, like `ScopeKeyedTable`, and repository code still writes `listed_scope = $n`, as Isolation rule 4 requires.

The rows hold only the canonical scope, the module name (`work` or `coordination` today), and a due time. `due_at` is `NULL` when the row's module has work remaining but none due, such as only `completed` or `poisoned` items.

**Operator-scope items** (**Decided by the maintainer in `plan` on 2026-11-17**): an item may be owned by the operator scope. It is read and written only in an operator unit, as every item is in its own scope's unit. FR-009 is reworded: no operator unit reads or writes a tenant scope's item or any tenant row.

**Rationale (validates AV-1)**: separate per-command policies give the read rule its operator exception without widening any write. No `SECURITY DEFINER` function and no `BYPASSRLS` role is needed.

**Rejected**: an implicit operator scope that the directory never lists (the coordinator and outbox would need a special case, and FR-006's "change none" would hold only by omission); a function that bypasses row-level security (AV-1 forbids it).

## R3. Registering and settling a directory row

**Decision**: two operations of the `Directory` namespace. Neither uses an advisory lock (FR-024). Register runs in the producer's unit at whatever isolation that unit asked for. Settle runs only in the framework's own units, always Read Committed.

**Register** `(unit, module, due)`, in the producer's own unit, after the write that needs the row:

1. `INSERT INTO isk_directory.scopes (listed_scope, module, due_at) VALUES ($own, $module, $due) ON CONFLICT DO NOTHING RETURNING 1`. A row inserted: done.
2. `SELECT due_at FROM isk_directory.scopes WHERE listed_scope = $own AND module = $module FOR KEY SHARE`. No row, because a settle deleted it after step 1 saw it: back to step 1, at most 3 rounds in all, then fail with `persistence.concurrency_conflict`.
3. If `due_at` is `NULL` or later than `$due`: `UPDATE ... SET due_at = $due WHERE listed_scope = $own AND module = $module AND (due_at IS NULL OR due_at > $due)`.

**Settle** `(scope, module, next)`, in the scope's own unit, where `next` is the owning namespace's query for its remaining work:

1. `SELECT due_at FROM isk_directory.scopes WHERE listed_scope = $own AND module = $module FOR UPDATE`. No row: done.
2. Run `next` as a new statement. It returns *none* (no work of the module remains), or the earliest due time, or `NULL` (work remains, none due).
3. *None*: `DELETE` the row. Otherwise `UPDATE` its `due_at`. Commit.

**Why it cannot lose a row**: `FOR KEY SHARE` and `FOR UPDATE` conflict, and producers' `FOR KEY SHARE` locks do not conflict with each other, so producers never queue behind each other on the row. A producer that holds its lock before the settle locks makes the settle wait for its commit, and Read Committed gives the settle's step 2 a new snapshot, which sees the producer's committed work, so the row is kept. A producer that reaches step 2 after the settle locks waits for the settle to commit: if the settle deleted the row, its locking read finds nothing and it inserts a new row; if the settle kept it with a later due time, step 3 lowers it. A producer whose step 1 conflicts with a row the settle then deletes loops. In every order, a committed item or expiring row has its directory row (FR-006, SC-005's removal race).

**Isolation**: settle needs Read Committed, because its step 2 must see work committed while it waited for the lock. The dispatcher and the jobs open its units, so they always open them at Read Committed. Register works at every isolation, so a producer that needs Repeatable Read or Serializable for its own writes, such as a budget, still enqueues atomically with them. At those levels PostgreSQL refuses to act on a row version the producer's snapshot cannot see: step 1's conflict with an invisible row, or step 2's lock on a row that a settle has since changed or deleted, fails with SQLSTATE `40001`. That is `persistence.concurrency_conflict`, a transient conflict that `UnitOfWorkRetry` reruns whole. The unit commits nothing, so no item is ever left without its row, and the cost is a retry, not lost work. The same holds for the outbox's dedupe insert (R5).

**Why not a single conditional delete**: `DELETE ... WHERE NOT EXISTS (SELECT ... FROM items ...)` re-checks only the deleted row after waiting on it. Its subquery keeps the statement's first snapshot, so a producer that commits while the delete waits is not seen, and the row is lost.

**Why not a lock on every refresh**: `INSERT ... ON CONFLICT DO UPDATE` locks the row on every enqueue and every expiring coordination write, which would serialize all writers of one scope on one row.

**What `next` is**:

| Module | Remaining work | Due time |
|---|---|---|
| `work` | Any item of the scope, in any state | Earliest `due_at` of its `pending` items, or `NULL` when none is pending |
| `coordination` | Any row of the scope in `coordination_values` or `leases`, all of which expire | Earliest `expires_at` |

Fence records and namespace generations never expire, so they never keep a `coordination` row.

**When the coordinator registers**: only when a write inserts a new row into `coordination_values` or `leases`, detected with `RETURNING (xmax = 0)`. That includes the placeholder row that a lease release inserts for an acquire whose outcome is unknown. Updating a live or expired row needs no registration: a row of the scope exists, so its directory row exists, because the settle deletes the directory row only when no row of the scope remains. This keeps the registration to one extra insert per new key, not per write.

## R4. Turning a directory row into a scope (TR-5)

**Decision**: an internal `DirectoryScopeResolver : OwnershipResolver` in the `Directory` namespace, the only place that reads `listed_scope` text, used only by the dispatcher and the jobs. It accepts exactly `operator`, `org:<organization id>`, or `project:<organization id>/<project id>`, where each ID parses as an ISK-0002 `ResourceId` of the right type and its canonical text equals the input. It returns `UnitOfWorkScope.Operator`, or `UnitOfWorkScope.ForResource` over an attested `ScopedResourceReference` whose owner is the parsed `OrganizationScope` or `ProjectScope` and whose ID is the organization or project. Anything else is malformed: the row is skipped, event 8009 is written with the module only, and no unit is opened.

**Rationale**: ISK-0002 lets a scope come only from an approved resolver, and `OwnershipResolver.Attest` is that seam. The persistence ports need no new factory, so `UnitOfWorkScope` keeps its three sources.

**Rejected**: a new `UnitOfWorkScope.FromCanonical(string)` (any caller could build a scope from text).

## R5. Work item identity and storage

**Decision**:

- The item ID is a UUID version 7, `Guid.CreateVersion7()`, stored as `uuid`, and opaque to handlers. No ISK-0002 resource type is registered, because a work item is never addressed through the public surface.
- The bucket is the low 6 bits of the first byte of SHA-256 over UTF-8 `iskender.work.bucket.v1`, `0x00`, and the ID's lowercase 36-character text. It is computed once at enqueue and stored, so a later change of algorithm cannot move an item.
- Table `isk_work.items` is scope-keyed (persistence contract Isolation rule 8). Its columns are in [data-model.md](data-model.md#isk_workitems).
- Uniqueness is `(scope_key, kind, idempotency_key)`. An enqueue is `INSERT ... ON CONFLICT (scope_key, kind, idempotency_key) DO NOTHING RETURNING item_id`. On a conflict, a second statement reads the existing item's ID. Under Read Committed the insert waits for a concurrent producer with the same key and, if it commits, conflicts, so one item exists (US1.4).
- The dedupe window is `work.retention.completed` for completed items, and `work.retention.poisoned` for poisoned ones: after retention deletes an item, the same key enqueues a new one.

**Rejected**: a `bigserial` ID (it reveals volume and ordering across scopes); deriving the bucket at read time (a change of the formula would move items between shards silently).

## R6. The dispatch cycle

**Decision**: one dispatcher per worker process, a loop that wakes every `work.pollInterval`, and at once after a pass that claimed a full batch:

1. **Directory page**, in an operator unit: `SELECT listed_scope FROM isk_directory.scopes WHERE module = 'work' AND due_at <= clock_timestamp() AND listed_scope > $cursor ORDER BY listed_scope LIMIT $batchSize`. The cursor wraps to the start when a page is short, so every due scope is visited within ⌈due scopes ÷ `batchSize`⌉ polls whatever the order of due times. The operator unit reads nothing else.
2. **Shard counts**, per scope, in a unit of that scope (R4): the due `pending` items the worker can handle, grouped by `bucket % shards`, and, separately, the due items it cannot (R8).
3. **Shard leases**: for each shard with handled items, within the in-flight caps, `TryOnly` the lease `work` / `dispatch.<shard>` in the scope, for `work.leaseDuration`, through `CoordinationGate.ForFeature("work.dispatch")`. Contention skips the shard.
4. **Claim**, in a scope unit, holding the lease: up to `batchSize` due, handled `pending` items of the shard, ordered by `due_at`, then `item_id`.
5. **Per item**, while `lease.IsSafeToAct`, each in its own task, up to `work.dispatch.concurrency` per worker and `work.dispatch.scopeConcurrency` per scope:
   1. **Attempt record**, its own unit, committed: the fencing guard accepts the shard lease for resource `work` / `dispatch.<shard>`, then the item is poisoned at once if its `attempt_count` already reached the kind's maximum, with its last failure kept, or otherwise `attempt_count + 1`, `due_at = clock_timestamp() + backoff(attempt)`, and last failure `abandoned` / `work.attempt_abandoned`, all conditional on `state = 'pending'` and the `attempt_count` the claim read. No row changed: another worker moved it, and it is skipped.
   2. **Apply**, one unit: the guard accepts the token; `SELECT ... FOR UPDATE` the item where `state = 'pending'` and `attempt_count` is this attempt's (no row: commit nothing, event 8010 `superseded`); the handler runs with that unit, its delivery, and a token that fires at its timeout, at shutdown, or on a lost lease; the item is marked `completed`, with `finished_at` and its last failure cleared, conditional on the same state and attempt; commit. Holding the row lock means a duplicate holder waits and then finds the item changed, rather than running the handler concurrently.
   3. **Failure record**, when the apply did not commit because the handler failed: its own unit, with the guard, records the class and code, and poisons the item for a permanent class or when `attempt_count` has reached the maximum, conditional on the same state and attempt. Its backoff was already set by step 5.1.
6. **Renewal**: each held shard lease is renewed every third of `work.leaseDuration`. A renewal that fails with `coordination.lease_lost` cancels every in-flight handler of the shard.
7. **Settle**: after a shard's pass, the lease is released, and once every shard of the scope is idle, the scope's `work` row is settled (R3), which sets its due time to its earliest pending item's.

The fencing guard rejects the token of a worker paused past its lease's expiry once the next holder has fenced a higher token, so the paused worker's attempt record, apply, and failure record commit nothing (US2.2). A change of shard count makes two lease keys cover one item; the conditional writes of steps 5.1 to 5.3 still let one holder commit (US2.4, FR-013). A crash leaves the item `pending` with its attempt counted and its due time pushed by the backoff, and the shard's lease expires, so another worker takes it after the later of the two (US1.3).

**Rationale**: three short units per attempt keep the attempt count durable (FR-012) without holding a transaction open across the attempt record, and the lock-first apply makes the at-most-once commit the database's, not the handler's. Polling meets AV-2: an item committed with a due time already passed is dispatched at the next poll that visits its scope, within one poll interval when the due scopes fit in one page, and no figure is asserted.

**Rejected**: `SKIP LOCKED` claims without leases (no fence, so a paused worker could commit); one unit for attempt and apply (a crash would roll back the attempt count, so a crashing item would never be poisoned); `LISTEN`/`NOTIFY` (post-MVP).

## R7. Retry, backoff, and classification

**Decision**:

- `d(n) = min(work.retry.maximumDelay, work.retry.initialDelay × 2^(n−1))` for attempt `n`, and the delay is drawn uniformly from `[max(work.retry.initialDelay, d(n) ÷ 2), d(n)]`. So the first retry waits exactly `initialDelay`, and none waits longer than `maximumDelay`, which is what US3.1 and SC-004 test.
- Classification of a handler's outcome:

  | Outcome | Class | Code recorded | Effect |
  |---|---|---|---|
  | `WorkFailures.Permanent(code)`, a `GatewayException` whose diagnostics hold `work_failure = permanent` | `permanent` | Its code | Poisoned at once |
  | `work.payload_invalid` raised through `WorkFailures.Permanent` | `permanent` | `work.payload_invalid` | Poisoned at once |
  | `persistence.unavailable`, `persistence.timeout`, `persistence.concurrency_conflict`, any `coordination.*` code except `coordination.capability_unsupported` | `transient` | Its code | Retried |
  | The handler's timeout fired | `transient` | `work.handler_timeout` | Retried |
  | Cancelled at shutdown | `transient` | `work.handler_cancelled` | Retried, and released at once (R12) |
  | Any other `GatewayException`, or any other exception | `unclassified` | `core.internal` | Retried as transient (spec Clarifications, 2026-11-17) |
  | A process crash or kill | `abandoned`, recorded by the attempt record | `work.attempt_abandoned` | Retried, and poisoned at the claim once the maximum is reached |
  | Cancelled after a lost shard lease | `abandoned`, from the attempt record; no failure record is written, since the worker no longer holds the lease (refined in `checklist`) | `work.attempt_abandoned` | Retried, and poisoned at the claim once the maximum is reached |

- Poisoning happens when `attempt_count` reaches the kind's maximum: the registration's `MaximumAttempts`, or `work.retry.maximumAttempts`. An item that kills its process every time is poisoned by the claim that finds its count at the maximum, so it runs exactly that many times (SC-003).

**Rejected**: a separate dead-letter table (a second write path); unbounded retries; poisoning `unclassified` at once (decided otherwise in `clarify`).

## R8. Version skew

**Decision**: the claim and the shard counts filter on the handlers this worker registered: `EXISTS (SELECT 1 FROM unnest($kinds, $lowest, $highest) h(kind, lowest, highest) WHERE h.kind = i.kind AND i.payload_version BETWEEN h.lowest AND h.highest)`. A second count, run with the shard counts, groups the due `pending` items that fail this filter by kind, feeds the gauge `iskender.work.items.skewed`, and writes event 8008 once per kind when it first appears in the process, and again after it has cleared. Skewed items are never claimed, so no attempt is counted and they never occupy a batch (FR-015, FR-018).

## R9. Coordination requirements

**Decision**: the dispatcher registers `FeatureRequirement("work.dispatch", "work", { leases: strict, fencing: strict }, mandatoryPath: true, fail_closed)`, and each job registers its own, `work.job.<job>`, the same way. The outbox registers none. A worker whose leases are `redis` or `local` is therefore refused with `unsupported_consistency` and exits 4 (US2.5), while the planes, which only enqueue, are never refused. Job leases are operator-scope, named `work` / `job.<job>`, and fence the job's bookkeeping resource of the same name.

## R10. Recurring jobs

**Decision**:

- Port: `IRecurringJob` with a `RecurringJobRegistration(name, defaultInterval, defaultBatch)`, so later specs, such as ISK-0026's audit retention, add jobs the same way. A job name is `<module>.<name>`.
- Bookkeeping: `isk_work.job_runs (scope_key, job, last_started_at, last_finished_at, cursor)`, scope-keyed, written only in operator scope.
- Each worker checks each job every `work.pollInterval`. When `last_started_at + interval` has passed, it tries the job's lease `TryOnly`. Holding it, an operator unit runs the guard for resource `work` / `job.<job>` and then records `last_started_at`. The run proceeds, recording its cursor through the same fenced write after each page, and records `last_finished_at` at the end. A second worker finds the lease held and skips (US5.2). A crash leaves the cursor at the last finished page, and the next run resumes there; an older cursor only repeats deletes of rows already gone (FR-022).
- `coordination.purge_expired`: pages the directory's `coordination` rows with `due_at <= clock_timestamp()` in an operator unit, and for each scope calls `CoordinationMaintenance.PurgeExpiredAsync(scope, batch)` until it returns fewer than `batch`, then `CoordinationMaintenance.SettleDirectoryAsync(scope)`, a new method in the coordination namespace that runs R3's settle with the coordination `next` query in the scope's own unit. The purge's deletes stay in `CoordinationMaintenance`'s own units, unfenced (TR-11).
- `work.retention`: pages the directory's `work` rows, whatever their due time, and for each scope deletes `completed` items finished before `clock_timestamp() - work.retention.completed` and `poisoned` items finished before `clock_timestamp() - work.retention.poisoned`, `batch` at a time, then settles the scope's `work` row.
- The interval is measured from `last_started_at`, so a job runs at most once per interval across all workers.

**Rejected**: changing `PurgeExpiredAsync` to take the caller's unit (TR-11); a cron syntax (post-MVP).

## R11. The worker's plane and command

**Decision** (**Decided by the maintainer in `plan` on 2026-11-17**): the worker is its own composition plane.

- `CompositionPlane` gains `Worker`. A new root, `AddIskenderWorker(services, configure)` in `Iskender.Management`, with root module `worker`, `WorkerOptions` of the same shape as `ControlPlaneOptions`, and no section, calls the same core as `AddIskenderControlPlane(...)`. Two roots on one collection are still `hosting.registration_duplicate`.
- The single-instance guard of a `worker` holds the operator-scope lease `coordination` / `instance.worker`, so a worker and a control plane that both declare `single_instance` never refuse each other. `SingleInstancePlane` gains `Worker`.
- `{Plane}` in events 7001, 7004, and 7005 is `worker`, and `service.name` is `iskender-worker`.
- `ContainerPlaneDefinition` gains `ServingCommands`, a map from a command name to a serving variant: its plane, service name, and composition. A serving command runs the same serve path as no arguments, binds only the health listener, and takes no further arguments (`work` followed by anything is a usage error, exit 64). The control plane's definition maps `work` to the worker variant, whose composition is `AddIskenderWorker(...)`, the serving control plane's five adapter registrations, `AddIskenderPostgreSqlWork()`, and `AddIskenderPostgreSqlWorkDispatcher()`. The data plane maps none, so `work` there exits 64.
- `health live` and `health ready` work in the worker's container unchanged, since they read the configuration and call the health port.

This adds a fifth revision, hosting contract version 3, which TR-18 did not list: Ownership rule 2's root table, Registration rule 6's method table, Registration rule 8, Start-up rule 6.2's lease names, and Start-up rule 6's list of start-ups. It changes no public member of `Iskender.Hosting`, so the harness's `hosting` version and baseline stay (SC-010).

**Rejected**: sharing the control plane's identity (a worker and a control plane that both declare `single_instance` would refuse each other, and telemetry could not tell them apart); a new executable or image (excluded by the prompt).

## R12. Shutdown

**Decision**:

- The dispatcher's start-up, `ModuleName` `work-dispatcher`, resolves `IHostApplicationLifetime` with `GetService`. On `ApplicationStopping` it stops claiming items and starting jobs at once, and records the deadline as the signal's time plus the host's `HostOptions.ShutdownTimeout`. The container entry point already sets that to `shutdownDelay` + `shutdownTimeout` (container contract, Lifecycle), so the deadline is TR-13's bound in a container, and the embedding host's own shutdown timeout in an embedded host.
- `StopAsync` runs after the server stops (hosting contract, Start-up rule 8), before the coordination start-up's, since `work-dispatcher` sorts after `coordination`. It waits for in-flight items until the deadline, then cancels them. Each cancelled handler's unit rolls back, which sends PostgreSQL's cancel request, and its failure record notes `work.handler_cancelled` as transient, then the start-up releases every held lease, each bounded by the coordination `operationTimeout`, so another worker can take the items as soon as they are due, without waiting for the leases to expire (US6.2). Their due time stays the one the attempt record set (R6 step 5.1). A failure record or release that cannot finish within 2 seconds of the deadline is abandoned: the attempt is already counted, and the lease expires on its own TTL.
- So the process exits within `shutdownDelay` + `shutdownTimeout` + 5 seconds of the signal (SC-007). The container contract revision adds one step to the worker's sequence, "stop claiming at the signal", and changes none of the other planes' steps (validates AV-3).
- A stop signal during start-up: the dispatcher's loop starts only once `StartAsync` returns, and checks `ApplicationStopping` before every claim, so nothing is claimed.

## R13. Configuration

**Decision**: namespace `work`, version 1, registered only by `AddIskenderPostgreSqlWorkDispatcher()`. Fields, defaults, and bounds are in [the work contract](contracts/work.md#configuration). Rules the ISK-0003 schema cannot express are checked when `WorkSettings` is read, after validation and before anything binds, failing with `config.field.out_of_range` and exit 2: `dispatch.shards` divides 64; `dispatch.scopeConcurrency` is at most `dispatch.concurrency`; `retry.maximumDelay` is at least `retry.initialDelay`; `leaseDuration` is within `coordination.leases.minimumDuration` and `maximumDuration`. The container entry point already resolves module settings that check across fields before binding, and it resolves `WorkSettings` the same way. Every default is a safety bound, not a performance figure: the in-flight caps bound connections (each in-flight item holds at most one pooled connection at a time, and the default per-worker cap of 16 is below the default pool of 20), and the handler timeout bounds a hung handler.

## R14. The payload limit

**Decision** (**Decided by the maintainer in `plan` on 2026-11-17**): 64 KiB (65,536 bytes) is a fixed rule of the `work` contract, enforced by the outbox in every plane, and not a configuration field. The table's `CHECK (octet_length(payload) <= 65536)` enforces it a second time. A later revision of the work contract may raise it. TR-1, TR-14, FR-002, and FR-029 are reworded.

**Rejected**: a `workOutbox` section in every plane (a worker and a producer could disagree, and an item could be enqueued that a worker refuses).

## R15. Tracing

**Decision**: enqueue stores `Activity.Current`'s W3C `traceparent`, and its `tracestate` when it is at most 512 characters, else none. A dispatch starts `work.dispatch` from `ActivitySource("Iskender.Work")`, kind `Consumer`, with no parent and one link built by `ActivityContext.TryParse(traceparent, tracestate, isRemote: true)`. A value that does not parse adds no link. Tags are `iskender.work.kind`, `iskender.work.attempt`, and `iskender.work.outcome`. A job run starts `work.job`, tagged `iskender.work.job` and `iskender.work.outcome`. The stored context is used for nothing else (TR-15).

## R16. Diagnostics and error codes

**Decision**: events 8001 to 8012 under `Iskender.Work`, the metrics of `Meter("Iskender.Work")`, and eight `work.*` codes with `since: 6`, all listed in [the work contract](contracts/work.md). The codes are added to the ISK-0002 contract's Error codes table, which rule H4 keeps in step, with no revision of that contract (its "Changing this contract" section). `core-contracts` goes from 5 to 6, with additions only. Each container entry point adds `Iskender.Work` to its OpenTelemetry activity sources and meters.

## R17. The developer topology

**Decision**: local environment contract version 2.

- `local/config/worker/` holds `iskender.yaml`, the control plane's base without the migration login (no long-running process needs it: container contract, Start-up rule 5), and `iskender.development.yaml`, the control plane's overlay with `coordination.leases.coordinator: postgresql`. `local/native/worker/` is the matching native pair, with `container.trafficPort: 8092` and `container.healthPort: 8093`.
- The `worker` service runs `<project>/control-plane:local` with `command: ["work"]`, the planes' hardening, `stop_grace_period: 40s`, a `health live` check, no published port, and dependence on `postgres` and `valkey` being healthy. It starts in the default mode, and stops before the `dependencies` mode starts, like the planes.
- Smoke assertion 1 adds `worker`, assertion 2 adds its `health live`, and assertion 3 adds its answer, which is the planes' answer with `coordination` `not_ready`, reason `authentication_failed`, since the PostgreSQL coordinator cannot connect before ISK-0021. The two existing answers are unchanged byte for byte. Assertion 7 searches the worker's log too.
- The worker's dispatcher is accepted, since its leases are `postgresql`, so no 5002 or 7002 is written (US8.3).

## R18. Tests

**Decision**:

- A new test project, `tests/Iskender.Work.Tests`, referencing the PostgreSQL test fixture from `tests/Iskender.Infrastructure.PostgreSql.Tests` (persistence contract, Test fixture rule 2), with the work fixture: two or more in-process dispatchers over one database, a test handler that records each apply in its own table in the handler's unit, injection hooks at each point of FR-012 and FR-013, a paused dispatcher, forced duplicate claims, and different shard counts.
- A test-role console project, `tests/Iskender.Work.CrashHost`, which runs one dispatcher against a database the test names and reports reaching each injection point on standard output. The crash test kills it with `SIGKILL` at each point, 20 times per point (SC-001), and restarts it. In-process "abandonment" terminates the dispatcher's backend connections from the fixture and drops it without a stop.
- The container tests gain the `work` command's exit codes, its readiness, and its stop, and the local environment tests gain the worker.
- Tests that need a container runtime follow ISK-0005's rule, and all of them run in CI check 4, so CI stays at 10 checks (validates AV-7).

## R19. Documentation and links

**Decision**: the six contracts are published in this spec's `contracts/` directory. The links to the persistence, coordination, container, hosting, and local environment contracts in `docs/03` and `docs/04` are repointed, and `docs/03` gains a paragraph on the `work` contract and the scope directory. `docs/04` gains a "Background work" runbook (FR-038), the worker in "Container entry points", the outbox and dispatcher in the embedding guide (FR-037), and the worker in "Local development". `local/README.md` and `CONTRIBUTING.md`'s contributor steps name the worker service.
