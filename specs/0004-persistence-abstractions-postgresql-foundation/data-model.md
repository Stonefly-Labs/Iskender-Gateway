# Data Model: ISK-0004 — Persistence Abstractions & PostgreSQL Foundation

Types are named as the plan intends to build them. Port types are in `Iskender.Domain.Ports`, which uses no module namespace (rule A18) and no driver, data-access, or ASP.NET Core type (FR-001). Adapter types are in `Iskender.Infrastructure.PostgreSql`. Decisions behind them are in [research.md](research.md). The rules a caller relies on are in [contracts/persistence.md](contracts/persistence.md).

## Ports: `Iskender.Domain.Ports`

### `UnitOfWorkScope` (sealed class, no public constructor)

| Member | Meaning |
|---|---|
| `static ForResource(ScopedResourceReference reference)` | The attested owner of a resource. `null` → `persistence.scope_required` |
| `static ForRequest(GatewayExecutionContext context)` | The request's resolved owner, deadline, and cancellation token. `null`, or an unresolved owner → `persistence.scope_required` |
| `static Operator` | The operator scope, for system data |
| `Owner` | The `OwnerScope`, fixed for the unit's life |
| `Deadline` | The request's deadline, or `null` |
| `RequestCancellation` | The request's token, or `None` |

Validation: the only routes are the three factories (R4). Equality is not offered, since a scope is a capability, not a value.

### `UnitOfWorkOptions` (sealed record)

| Field | Type | Default | Rule |
|---|---|---|---|
| `Module` | `string` | required | A module name: 1 to 31 characters of `a-z`, `0-9`, `_`, starting with a letter. A metric and log label |
| `Operation` | `string` | required | 1 to 63 characters of `a-z`, `0-9`, `_`, `.`, starting with a letter. A bounded operation kind label, such as `registry.update` |
| `Isolation` | `UnitOfWorkIsolation` | `ReadCommitted` | |
| `Timeout` | `TimeSpan` | 60 s | More than zero. Capped by the scope's deadline (R7) |
| `Deadline` | `DateTimeOffset?` | `null` | For a unit not opened from a request. The earlier of this and the scope's deadline applies |
| `SafeToRerun` | `bool` | `false` | Read only by `UnitOfWorkRetry` |

### `UnitOfWorkIsolation` (enum)

`ReadCommitted`, `RepeatableRead`, `Serializable`.

### `IUnitOfWorkFactory`

`ValueTask<IUnitOfWork> OpenAsync(UnitOfWorkScope scope, UnitOfWorkOptions options, CancellationToken cancellationToken)`.

Opens one connection and one transaction, sets the transaction-local scope settings and `statement_timeout` (R5, R7). Fails with `persistence.scope_required`, `persistence.timeout`, `persistence.cancelled`, or `persistence.unavailable`, before returning a unit.

### `IUnitOfWork : IAsyncDisposable`

| Member | Meaning |
|---|---|
| `Scope` | The `UnitOfWorkScope` it was opened with |
| `Isolation` | Its isolation level |
| `State` | `UnitOfWorkState` |
| `ValueTask CommitAsync(CancellationToken)` | Commits. Allowed once, in `Open` |
| `ValueTask<bool> AcquireAdvisoryLockAsync(AdvisoryLockName name, AdvisoryLockWait wait, CancellationToken)` | Transaction-scoped lock in the unit's own scope. `TryOnly` returns `false` when held; `Bounded` returns `true` or throws `persistence.lock_timeout` |
| `DisposeAsync()` | Rolls back unless committed, and returns the connection to the pool |

One logical operation at a time: a second concurrent call on the same unit fails with `core.internal` (class `Unexpected`) and faults the unit.

### `UnitOfWorkState` (enum) and transitions

```text
Open ──CommitAsync ok──────────────► Committed ──Dispose──► Closed
Open ──CommitAsync, outcome unknown► Faulted (CommitOutcomeUnknown) ──Dispose──► Closed
Open ──any failure / timeout / cancel► Faulted ──Dispose (rollback)──► Closed
Open ──Dispose without commit──────► RolledBack ─────────► Closed
```

Any call on a unit not in `Open`, other than `DisposeAsync`, fails with `core.internal`.

### `AdvisoryLockName` (sealed class, value equality)

`Module`, a module name, and `Name`, matching the `Operation` pattern above. `AdvisoryLockWait` is `TryOnly` or `Bounded(TimeSpan)` with a positive bound.

### `AdvisoryLockKey` (static)

`long Derive(OwnerScope scope, string module, string name)`, as R14 specifies. Pure, so unit-tested with published vectors.

### `VersionToken` (readonly struct)

A positive `long`. `Initial` is 1, and `Next()` adds one. Zero or a negative value → `core.value_invalid`.

### `UnitOfWorkRetry` (static)

`ValueTask<T> RunAsync<T>(IUnitOfWorkFactory factory, UnitOfWorkScope scope, UnitOfWorkOptions options, Func<IUnitOfWork, CancellationToken, ValueTask<T>> work, TimeProvider timeProvider, CancellationToken cancellationToken)`.

At most 3 attempts. Re-runs only on `TransientConflict`, with `SafeToRerun` true and `CommitOutcomeUnknown` false (R9).

### `PersistenceException` (sealed exception)

| Member | Meaning |
|---|---|
| `Code` | The ISK-0002 `ErrorCode` |
| `FailureClass` | `PersistenceFailureClass` |
| `CommitOutcomeUnknown` | `true` only when the connection broke during `COMMIT` |

`Message` is `"<code id>: <safe message>"`, with no inner exception and no database detail.

### `PersistenceFailureClass` (enum)

`TransientConflict`, `ConcurrencyConflict`, `UniqueConflict`, `Unavailable`, `Timeout`, `Cancelled`, `AuthenticationFailed`, `ScopeViolation`, `Unexpected`. `ScopeViolation` covers `persistence.scope_required` and `persistence.scope_not_permitted`, which come from the adapter, not the database.

### Migration port types

| Type | Shape |
|---|---|
| `IMigrationRunner` | `ApplyAsync(MigrationApplyRequest, ct)`, `ReverseAsync(MigrationReverseRequest, ct)`, `GetStatusAsync(ct)` |
| `MigrationApplyRequest` | No fields beyond the token today. Applies every pending migration |
| `MigrationReverseRequest` | `Steps` (positive, default 1), `ConfirmDataLoss` (default `false`) |
| `MigrationIdentity` | `Module`, `Version` (positive `int`), value equality, ordered by the global order |
| `MigrationRunResult` | `Applied` or `Reversed`: the identities changed, in order |
| `MigrationStatus` | Per module: `SchemaVersion` (`int?`), `SupportedRange`, `Pending` identities, `Unknown` identities, `ChecksumMismatches` identities |
| `SchemaVersionRange` | `Minimum`, `Maximum`, positive, `Minimum ≤ Maximum` |

Failures: `persistence.migration_locked`, `persistence.migration_checksum_mismatch`, `persistence.schema_incompatible` (unknown migration, invalid set, missing reverse, cycle, cross-schema script, missing group role, failed row-level security check), `persistence.reverse_requires_confirmation`, and the connection classes from R8.

### Health port types

| Type | Shape |
|---|---|
| `IPersistenceHealthProbe` | `ValueTask<PersistenceHealthResult> CheckAsync(CancellationToken)`. Never throws for a database condition |
| `PersistenceHealthResult` | `Status` and `Reason`, nothing else |
| `PersistenceHealthStatus` | `Healthy`, `Unhealthy` |
| `PersistenceHealthReason` | `Ok`, `Unreachable`, `AuthenticationFailed`, `Timeout`, `SchemaBehind`, `SchemaAhead`, `ChecksumMismatch`, `MigrationInProgress`. Written as the snake-case codes in TR-11 |

`Healthy` occurs only with `Ok`, and `Ok` only with `Healthy`.

### Credential port types

| Type | Shape |
|---|---|
| `IDatabaseCredentialProvider` | `ValueTask<DatabaseCredential> GetCredentialAsync(DatabaseCredentialRequest, CancellationToken)` |
| `DatabaseCredentialRequest` | `Reference` (`SecretReference`), `Purpose` |
| `DatabaseCredentialPurpose` | `Runtime`, `Migration` |
| `DatabaseCredential` | `Password` (string). `ToString()` returns `[redacted]`. No equality, no serialization attribute |

## Adapter: `Iskender.Infrastructure.PostgreSql`

| Type | Namespace | Role |
|---|---|---|
| `PostgreSqlSectionSchema` | root | Registers the `persistence` section (R16) |
| `PostgreSqlSettings` | root | The validated section, read once. Holds `SecretReference`s, never passwords |
| `PostgreSqlDataSources` | root | Builds the runtime and migration `NpgsqlDataSource`s, with `UsePasswordProvider` and `SslMode` from R15 |
| `PostgreSqlUnitOfWorkFactory` | root | `IUnitOfWorkFactory` |
| `PostgreSqlUnitOfWork` | root | `IUnitOfWork`. `internal static From(IUnitOfWork)` gives repository code its `Command(TableAccess, sql)` |
| `TableAccess`, `TenantTable`, `OperatorTable` | root | Table descriptors a module declares once (R6) |
| `VersionedRows` | root | Optimistic update and delete (R10) |
| `FailureClassifier` | root | R8 |
| `MigrationModule` | `.Migrations` | `Name`, `Schema` (`isk_<name>`), `DependsOn`, `SupportedRange`, and the assembly and resource prefix that holds its scripts |
| `MigrationScript` | `.Migrations` | Identity, forward and reverse text (normalized), checksum, data-losing flag |
| `MigrationSet` | `.Migrations` | Validates and orders the modules (R11). Immutable |
| `SchemaReferenceScanner` | `.Migrations` | The SQL lexer for schema names (R11) |
| `PostgreSqlMigrationRunner` | `.Migrations` | `IMigrationRunner` |
| `MigrationLock` | `.Migrations` | The session lock and its published key (R12) |
| `PostgreSqlHealthProbe` | `.Health` | `IPersistenceHealthProbe` (R13) |
| `PersistenceLog`, `PersistenceTelemetry` | `.Diagnostics` | R18 |

## Database objects

| Object | Owner | Contents |
|---|---|---|
| Schema `isk_migrations` | Migration role | `history (module text, version integer, checksum text, applied_at timestamptz, applied_by text, primary key (module, version))`. `SELECT` granted to `iskender_runtime` |
| Schema `isk_<module>` | Migration role | That module's tables only. `USAGE` and default DML privileges granted to `iskender_runtime` |
| Role `iskender_runtime` | Operator provisioned | `NOLOGIN` group. No `BYPASSRLS`, owns nothing |
| Runtime login role | Operator provisioned | Member of `iskender_runtime`, no `CREATE` on the database or any schema |
| Migration login role | Operator provisioned | Owns the schemas it creates. Needs `CREATE` on the database |

A tenant-owned table has `owner_organization_id text not null`, and a project-owned one also `owner_project_id text not null`. Each has row-level security enabled and forced and the policy from R5. A versioned table has `row_version bigint not null default 1`.

### Transaction-local settings

| Setting | Organization unit | Project unit | Operator unit |
|---|---|---|---|
| `iskender.scope_kind` | `organization` | `project` | `operator` |
| `iskender.organization_id` | org ID | org ID | empty |
| `iskender.project_id` | empty | project ID | empty |

## Test fixtures

| Item | Contents |
|---|---|
| Module `alpha` (`isk_alpha`) | 1: tenant table `widgets` (org-owned, versioned). 2: project-owned table `widget_notes` and operator table `settings`. 3: adds `widgets.label` (reverse data-losing). 4: adds nullable `widgets.colour` (the "next release" expand). Declared range `[1, 5]`. A fixture-only 6 is used by the `schema_ahead` test |
| Module `beta` (`isk_beta`) | Depends on `alpha`. 1: tenant table `counters`. 2: `lock_log` for the advisory lock test. 3: an index. Declared range `[1, 3]` |
| Invalid sets | Missing reverse, duplicate version, dependency cycle, cross-schema reference, a failing forward script, a tenant table without a policy, an edited script after apply |
| Snapshots | Expected catalog text after each forward step (R20) |
| Credential provider | A test `IDatabaseCredentialProvider` returning the container's passwords |
| Canary | One value used as host alias, database name, both user names, and both passwords, searched for in every output (SC-005) |

## Relationships

```text
UnitOfWorkScope ──(1)── IUnitOfWork ──(1 connection, 1 transaction)── PostgreSQL
      │                       │
 OwnerScope (ISK-0002)        ├── AdvisoryLockName → AdvisoryLockKey (scope, module, name)
                              └── TableAccess → TenantTable | OperatorTable (module-declared)

MigrationModule ──DependsOn──► MigrationModule
      │ 1..n
MigrationScript ──applied──► isk_migrations.history row
      │
SchemaVersionRange ──judges──► PersistenceHealthResult
```
