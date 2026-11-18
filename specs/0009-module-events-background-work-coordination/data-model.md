# Data Model: ISK-0009 — Module Events & Background Work Coordination

**Spec**: [spec.md](spec.md) | **Research**: [research.md](research.md) | **Contract**: [work contract](contracts/work.md)

Type names are the interface this spec defines. Member lists show what later specs and tests rely on, not every private detail.

## Port types (`Iskender.Domain.Ports`)

Each type's XML comment names ISK-0009 as its owning spec (FR-039).

| Type | Shape | Rules |
|---|---|---|
| `WorkKind` (sealed record) | `Module`, `Name`; `ToString()` is `<module>.<name>` | `Module` matches the module pattern `[a-z][a-z0-9_]{0,30}`, `Name` matches `[a-z][a-z0-9_]{0,62}`. Otherwise `core.value_invalid` |
| `WorkKindDeclaration` (sealed record) | `Kind`, `HighestPayloadVersion` | A producing module's statement that it enqueues `Kind`, with payload versions 1 to `HighestPayloadVersion` (1 to 1,000) |
| `WorkItemRequest` (sealed record) | `Owner` (`OwnerScope`), `Kind`, `PayloadVersion`, `Payload` (`ReadOnlyMemory<byte>`), `IdempotencyKey`, `Delay` (`TimeSpan`, default zero) | `IdempotencyKey` is 1 to 128 characters of `A-Z`, `a-z`, `0-9`, `.`, `_`, `:`, `-`. `Delay` is 0 to 30 days. `ToString()` shows the kind only |
| `WorkEnqueueResult` (readonly record struct) | `ItemId` (`Guid`), `Created` (`bool`) | `Created` is `false` when an item with the same scope, kind, and key was still retained |
| `IWorkOutbox` | `ValueTask<WorkEnqueueResult> EnqueueAsync(IUnitOfWork unit, WorkItemRequest request, CancellationToken cancellationToken)` | Writes in `unit`'s transaction only. Fails before any write with `work.unit_not_open`, `work.scope_mismatch`, `work.kind_undeclared`, or `work.payload_too_large`, and then faults the unit |
| `WorkDelivery` (sealed class) | `ItemId`, `Owner`, `Kind`, `PayloadVersion`, `Payload`, `IdempotencyKey`, `Attempt` | `ToString()` shows the kind and attempt only |
| `IWorkHandler` | `ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken)` | `unit` is open in the item's scope, already fenced. The handler writes only through it and never commits or disposes it |
| `WorkHandlerRegistration` (sealed record) | `Kind`, `LowestPayloadVersion`, `HighestPayloadVersion`, `MaximumAttempts` (`int?`), `Timeout` (`TimeSpan?`), `HasExternalEffects`, `HandlerType` | Versions 1 to 1,000 with lowest ≤ highest; `MaximumAttempts` 1 to 100; `Timeout` 1 s to 1 h; `HandlerType` implements `IWorkHandler`. One registration per kind |
| `IRecurringJob` | `Name` (`WorkKind`-shaped `<module>.<name>`), `ValueTask<RecurringJobOutcome> RunAsync(RecurringJobContext context, CancellationToken cancellationToken)` | |
| `RecurringJobRegistration` (sealed record) | `Name`, `DefaultInterval`, `DefaultBatch`, `JobType` | Interval 1 min to 24 h, batch 1 to 10,000 |
| `RecurringJobContext` (sealed class) | `Batch`, `Cursor` (`string?`), `ValueTask SaveCursorAsync(string? cursor, CancellationToken)` | `SaveCursorAsync` is the fenced bookkeeping write. A cursor is at most 512 characters |
| `RecurringJobOutcome` (enum) | `Completed`, `Partial` | `Partial` keeps the cursor for the next run |
| `WorkFailures` (static class) | `GatewayException Permanent(ErrorCode code)` | Sets the diagnostics value `work_failure = permanent`. The dispatcher classifies any other exception by its code (research R7) |
| `WorkFailureClass` (enum) | `Transient`, `Permanent`, `Unclassified`, `Abandoned` | Codes `transient`, `permanent`, `unclassified`, `abandoned` |
| `WorkItemState` (enum) | `Pending`, `Completed`, `Poisoned` | Codes `pending`, `completed`, `poisoned` |
| `WorkRegistrations` (static class) | `AddKind(IServiceCollection, WorkKindDeclaration)`, `AddHandler<THandler>(IServiceCollection, WorkHandlerRegistration)`, `AddJob<TJob>(IServiceCollection, RecurringJobRegistration)` | DI registration helpers. A second registration for one kind or job name fails at the call with `hosting.registration_duplicate` |
| `CompositionPlane` (enum, revised) | `DataPlane`, `ControlPlane`, `Worker` | `Worker` is new (research R11) |

## PostgreSQL adapter types

| Type | Namespace | Role |
|---|---|---|
| `ScopeDirectoryTable` | `Iskender.Infrastructure.PostgreSql` | The new `TableAccess` kind. Usable in every scope. Its predicate placeholder is `{listed}` |
| `ScopeDirectory` (internal) | `.Directory` | `RegisterAsync(unit, module, due)` and `SettleAsync(unit, module, next)` (research R3) |
| `DirectoryScopeResolver` (internal) | `.Directory` | The attestation of research R4 |
| `DirectoryMigrations.Module` | `.Directory` | Migration module `directory`, version 1, no dependencies, range 1 to 1 |
| `WorkMigrations.Module` | `.Work` | Migration module `work`, version 1, depends on `directory`, range 1 to 1 |
| `CoordinationMigrations.Module` | `.Coordination` | Unchanged: no dependency on `directory` (research R3, Migration order). `AddIskenderPostgreSqlCoordination()` adds the `directory` module to the set |
| `PostgreSqlWorkOutbox` | `.Work` | `IWorkOutbox` |
| `WorkDispatcher` (internal) | `.Work` | The loop of research R6 |
| `WorkDispatcherStartup` | `.Work` | `ICompositionStartup`, `ModuleName` `work-dispatcher` |
| `WorkSettings` | `.Work` | The validated `work` section, with the cross-field checks of research R13 |
| `PurgeExpiredJob`, `WorkRetentionJob` | `.Work` | The two shipped jobs |
| `WorkLog`, `WorkTelemetry` | `.Work` | Events 8001 to 8012, `ActivitySource` and `Meter` `Iskender.Work` |
| `CoordinationMaintenance.SettleDirectoryAsync(CoordinationScope, CancellationToken)` | `.Coordination` | New: settles the scope's `coordination` row in its own unit |
| `PostgreSqlServiceCollectionExtensions.AddIskenderPostgreSqlWork()` | `Iskender.Infrastructure.PostgreSql` | Module `work-postgresql`, requires `persistence-postgresql`. Registers the outbox and the `directory` and `work` migration modules |
| `PostgreSqlServiceCollectionExtensions.AddIskenderPostgreSqlWorkDispatcher()` | `Iskender.Infrastructure.PostgreSql` | Module `work-dispatcher`, section `work`, requires `work-postgresql` and `coordination-postgresql`. Registers the dispatcher, the start-up, the two jobs, and their `FeatureRequirement`s |
| `AddIskenderPostgreSqlCoordination()` (revised) | `Iskender.Infrastructure.PostgreSql` | Also adds the `directory` migration module |

## Management and container types

| Type | Role |
|---|---|
| `WorkerServiceCollectionExtensions.AddIskenderWorker(services, configure)` | The worker's composition root, module `worker`, plane `Worker` |
| `WorkerOptions` | `BaseDocument`, `OverlayDocument`, `ReadEnvironmentVariables`, `EnvironmentName`, as `ControlPlaneOptions` |
| `ContainerServingCommand` (sealed record) | `Plane`, `ServiceName`, `Compose` |
| `ContainerPlaneDefinition` (revised) | Gains `ServingCommands` (`IReadOnlyDictionary<string, ContainerServingCommand>`, default empty). `PlaneName` maps `Worker` to `worker` |
| `SingleInstancePlane` (revised) | Gains `Worker`, lease name `instance.worker` |

## Database objects

### `isk_directory.scopes`

| Column | Type | Notes |
|---|---|---|
| `listed_scope` | `text not null` | Canonical scope: `operator`, `org:<id>`, or `project:<org id>/<project id>` |
| `module` | `text not null` | `work` or `coordination` today; checked against the module pattern |
| `due_at` | `timestamptz` | `NULL` when work remains but none is due |

Primary key `(listed_scope, module)`. Index `(module, listed_scope) WHERE due_at IS NOT NULL` serves the dispatcher's keyset page. Policies in [research.md](research.md#r2-the-scope-directory-table-kind).

### `isk_work.items`

| Column | Type | Notes |
|---|---|---|
| `scope_key` | `text not null` | Scope-keyed table, persistence contract Isolation rule 8 |
| `item_id` | `uuid not null` | UUID version 7 |
| `module` | `text not null` | The producing module |
| `kind` | `text not null` | `<module>.<name>` |
| `payload_version` | `integer not null` | 1 to 1,000 |
| `payload` | `bytea not null` | `CHECK (octet_length(payload) <= 65536)` |
| `idempotency_key` | `text not null` | 1 to 128 characters |
| `bucket` | `smallint not null` | `CHECK (bucket BETWEEN 0 AND 63)` |
| `state` | `text not null` | `CHECK (state IN ('pending', 'completed', 'poisoned'))` |
| `due_at` | `timestamptz not null` | |
| `attempt_count` | `integer not null default 0` | |
| `last_failure_class` | `text` | `transient`, `permanent`, `unclassified`, `abandoned` |
| `last_failure_code` | `text` | A registered error code |
| `trace_parent` | `text` | At most 55 characters |
| `trace_state` | `text` | At most 512 characters |
| `enqueued_at` | `timestamptz not null default clock_timestamp()` | |
| `finished_at` | `timestamptz` | Set when `completed` or `poisoned` |

Primary key `(scope_key, item_id)`. Unique `(scope_key, kind, idempotency_key)`. Partial index `(scope_key, bucket, due_at) WHERE state = 'pending'` for the claim, and `(scope_key, state, finished_at) WHERE state <> 'pending'` for retention.

### `isk_work.job_runs`

| Column | Type | Notes |
|---|---|---|
| `scope_key` | `text not null` | Always `operator` |
| `job` | `text not null` | The job name |
| `last_started_at` | `timestamptz` | |
| `last_finished_at` | `timestamptz` | |
| `cursor` | `text` | At most 512 characters |

Primary key `(scope_key, job)`. Scope-keyed, with the single policy of Isolation rule 8.

## State transitions

```text
                enqueue (committed unit)
                        │
                        ▼
   ┌──────────────► pending ─────────── apply committed ──────────► completed
   │                  │  │                                              │
   │  transient,      │  │ permanent, or attempts reached               │ retention.completed
   │  unclassified,   │  │ maximum (at failure record or at claim)      ▼
   │  abandoned       │  ▼                                           deleted
   └──────────────────┘ poisoned ──────── retention.poisoned ───────► deleted
```

- `pending` → `pending`: an attempt record (`attempt_count + 1`, backoff, class `abandoned`) and, if the apply fails, a failure record (class and code).
- A skewed item stays `pending`, untouched.
- `completed` and `poisoned` are final. Nothing moves an item out of them except retention's delete.

## Validation summary

| Rule | Where | Failure |
|---|---|---|
| Unit open | Outbox | `work.unit_not_open` |
| `request.Owner` equals `unit.Scope.Owner` | Outbox | `work.scope_mismatch` |
| Kind declared by a `WorkKindDeclaration`, and payload version 1 to its highest | Outbox | `work.kind_undeclared` |
| Payload at most 65,536 bytes | Outbox, then the table's check | `work.payload_too_large` |
| Idempotency key form, delay range, kind form | Port type constructors | `core.value_invalid` |
| A malformed directory row | `DirectoryScopeResolver` | Skipped, event 8009 |
| `dispatch.shards` divides 64; `scopeConcurrency` ≤ `concurrency`; `retry.maximumDelay` ≥ `initialDelay`; `leaseDuration` within the coordination lease bounds | `WorkSettings`, when read | `config.field.out_of_range`, exit 2 |
| A settle in a unit that is not Read Committed (framework code only; register accepts any isolation) | `ScopeDirectory` | `core.internal` |
