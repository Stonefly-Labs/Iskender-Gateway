# Persistence Contract

**Version**: 1

**Owning spec**: ISK-0004 — Persistence Abstractions & PostgreSQL Foundation

**Review owner**: Architecture (ADR) owner, with the Security owner for Isolation and Credentials and the Privacy/Operations owner for Diagnostics. See `CONTRIBUTING.md` for who holds each role.

This contract states what module authors, operators, and later specs may rely on from the persistence ports in `Iskender.Domain.Ports` and their PostgreSQL implementation in `Iskender.Infrastructure.PostgreSql`. The type shapes are in [data-model.md](../data-model.md), and the reasons in [research.md](../research.md).

## Changing this contract

A change to a rule here is made by a later spec's revision of this contract, which increments the version, publishes the contract in that spec's `contracts/` directory, repoints the links in `docs/03-domain-and-technical-architecture.md` and `docs/04-technology-stack-and-deployment.md`, and records the change in this table.

| Version | Spec | Change | Approval | Owner |
|---|---|---|---|---|
| 1 | ISK-0004 | First version | This spec's PR | Architecture (ADR) owner |

The ports are not a public contract package: they are in the embeddable `Iskender.Domain`, so the ISK-0002 compatibility harness does not version them. This contract's version is the commitment to later specs.

## Ownership

*Review owner: Architecture (ADR) owner.*

1. The ports are in `Iskender.Domain.Ports`. No port signature names a driver, data-access, or ASP.NET Core type (FR-001).
2. `Iskender.Infrastructure.PostgreSql` is the only production project that references `Npgsql` or any data-access library (FR-002). It matches the ISK-0001 pattern `Iskender.Infrastructure.<Adapter>`, so its role (embeddable), its approved references, and its denied category (`provider`) come from the ISK-0001 contract unchanged.
3. A module's repository interfaces live with the module in `Iskender.Domain.<Module>`. Their PostgreSQL implementations live in `Iskender.Infrastructure.PostgreSql.<Module>`, where `<Module>` is the module's namespace segment from the ISK-0001 module map (FR-002a).
4. Code in `Iskender.Infrastructure.PostgreSql.<Module>` names only the schema `isk_<module>`, with `<module>` lowercased. Code in the framework namespaces (`Iskender.Infrastructure.PostgreSql`, `.Migrations`, `.Health`, `.Diagnostics`) names only `isk_migrations`. The adapter's test suite checks this on every run.
5. Each port and adapter type's XML comment names ISK-0004 as its owning spec (FR-003).

## Units of work

*Review owner: Architecture (ADR) owner; Security owner for rules 1 to 3.*

1. Every database access happens inside a unit of work, opened by `IUnitOfWorkFactory.OpenAsync` with a `UnitOfWorkScope`. There is no other way to get a connection from the ports (FR-004).
2. A `UnitOfWorkScope` comes only from `ForResource(ScopedResourceReference)`, `ForRequest(GatewayExecutionContext)`, or `Operator`. A `null` argument or an unresolved request owner fails with `persistence.scope_required` before a connection is acquired (FR-005).
3. The scope is fixed for the unit's life. A unit is one connection and one transaction.
4. A unit commits only on `CommitAsync`. Disposing it without a commit, a failure, a timeout, or cancellation rolls it back (FR-011).
5. Isolation is Read Committed unless the unit asks for Repeatable Read or Serializable. There are no nested transactions or savepoints (FR-012).
6. A unit serves one logical operation at a time. A concurrent second call fails with `core.internal` and faults the unit.
7. Bounds (FR-009):

   | Bound | Limit |
   |---|---|
   | Acquiring a connection | The earlier of `connectionTimeout` and the deadline |
   | Each statement | The earlier of `statementTimeout` and the deadline, enforced on the server by `statement_timeout` and on the client by a cancel request |
   | The whole unit | The earlier of `UnitOfWorkOptions.Timeout` (default 60 s) and the deadline |

   The deadline is the earlier of the request's and `UnitOfWorkOptions.Deadline`. One already passed fails with `persistence.timeout` and acquires nothing. Cancellation sends PostgreSQL's cancel request, so the statement stops on the server, and the unit rolls back.

## Isolation

*Review owner: Security owner.*

1. A **tenant-owned table** has `owner_organization_id text not null`. A **project-owned** table also has `owner_project_id text not null`. Both hold canonical ISK-0002 resource ID text.
2. Every tenant-owned table has row-level security enabled and forced, and a policy for all commands whose `USING` and `WITH CHECK` expression is:

   ```sql
   owner_organization_id = nullif(current_setting('iskender.organization_id', true), '')
   -- and, for a project-owned table:
   AND owner_project_id = nullif(current_setting('iskender.project_id', true), '')
   ```

3. The unit sets `iskender.scope_kind`, `iskender.organization_id`, and `iskender.project_id` with `set_config(..., true)` when it opens, so they are transaction-local and gone when it ends (FR-008). The table of values is in [data-model.md](../data-model.md#transaction-local-settings).
4. Repository code still writes the owner predicate on every tenant-owned query. Row-level security is the second layer, and the isolation tests pass with the predicate removed (FR-010, SC-004).
5. A row of another scope is reported exactly as a missing row: `core.resource.not_found`, or zero rows changed (FR-006).
6. Repository code creates commands only through `PostgreSqlUnitOfWork.Command(TableAccess, sql)`. An operator-scope unit given a `TenantTable`, or a tenant-scope unit given an `OperatorTable`, fails with `persistence.scope_not_permitted` before the statement is sent (FR-007). Operator access to tenant data needs a named, audited elevated operation, which this contract reserves and a later spec defines.
7. The runtime login is a member of the group role `iskender_runtime`, owns no table, and has neither `BYPASSRLS` nor superuser. The migration runner checks every tenant-owned table for the policy after each module and refuses to record the module if one is missing.

## Failures

*Review owner: Architecture (ADR) owner.*

1. Ports fail with `PersistenceException`, which carries an ISK-0002 `ErrorCode`, a failure class, and `CommitOutcomeUnknown`. Its message is the code's safe message. It never carries a driver exception, SQL, or server text (FR-013, FR-026).
2. Classification:

   | Class | Code | When |
   |---|---|---|
   | Transient conflict | `persistence.concurrency_conflict` | SQLSTATE `40001`, `40P01` |
   | Concurrency conflict | `persistence.concurrency_conflict` | A stale version token |
   | Unique conflict | `persistence.unique_conflict` | SQLSTATE `23505` |
   | Unavailable | `persistence.unavailable` | Class `08`, `57P01` to `57P03`, I/O failure, broken connection, unknown commit outcome |
   | Timeout | `persistence.timeout` | A deadline or timeout fired, including SQLSTATE `57014` from `statement_timeout` |
   | Timeout (lock) | `persistence.lock_timeout` | An advisory lock wait expired (SQLSTATE `55P03`) |
   | Cancelled | `persistence.cancelled` | The caller's or request's token fired |
   | Authentication failed | `persistence.unavailable` | SQLSTATE `28P01`, `28000`, or no credential provider |
   | Scope violation | `persistence.scope_required`, `persistence.scope_not_permitted` | Raised by the adapter before any I/O |
   | Unexpected | `core.internal` | Anything else |

3. The adapter never retries. `UnitOfWorkRetry.RunAsync` re-runs a whole unit only after a transient conflict, only when `SafeToRerun` is true and the commit outcome is known, at most 3 attempts in all, and then fails with `persistence.concurrency_conflict` (FR-014).
4. A connection lost during `COMMIT` is reported as `persistence.unavailable` with `CommitOutcomeUnknown`, and is never retried automatically (FR-015).

## Optimistic concurrency

A versioned table has `row_version bigint not null default 1`. An update or delete that names a stale `VersionToken` changes nothing and fails with `persistence.concurrency_conflict`. One that names a missing or foreign row fails with `core.resource.not_found`. A successful update returns the next token (FR-016).

## Advisory locks

*Review owner: Architecture (ADR) owner.*

1. Modules get **transaction-scoped** locks only, through `IUnitOfWork.AcquireAdvisoryLockAsync`. PostgreSQL releases them at commit or rollback (FR-027).
2. The key is the first 8 bytes, as a big-endian signed 64-bit integer, of SHA-256 over UTF-8 `iskender.lock.v1`, `0x00`, the scope's canonical form, `0x00`, the module, `0x00`, and the name. The scope's canonical form is `org:<id>`, `project:<org id>/<project id>`, or `operator`. The scope is always the unit's own.
3. `TryOnly` returns `false` when the lock is held. `Bounded(t)` waits up to `t`, then fails with `persistence.lock_timeout`.
4. An advisory lock is mutual exclusion inside one PostgreSQL database, during one transaction. **It is not a lease, a fencing token, or a distributed lock**, and no module may use it as one. Leases and fencing belong to ISK-0005 (FR-028).
5. The only session-scoped lock is the migration lock below.

## Migrations

*Review owner: Architecture (ADR) owner.*

1. **Scripts**: a module embeds `<module>/<version>_<slug>.up.sql` and `<module>/<version>_<slug>.down.sql`, with `<version>` a positive integer written with 4 digits. A reverse script whose first line is `-- iskender:data-losing` is data-losing.
2. **Modules**: a module is 1 to 31 characters of `a-z`, `0-9`, and `_`, starting with a letter. Its schema is `isk_<module>`. It declares the modules it depends on and its `SchemaVersionRange`.
3. **Checksum**: SHA-256, as lowercase hex, over the forward text, a `0x00` byte, and the reverse text, each decoded as UTF-8 with any BOM removed and CRLF or CR changed to LF.
4. **Refused before anything is applied** (`persistence.schema_incompatible`): a missing reverse script, a duplicate version within a module, an unknown dependency, a dependency cycle, invalid UTF-8, a script naming another `isk_` schema or `public`, an applied migration the release does not know, or a missing `iskender_runtime` role. A checksum mismatch is refused with `persistence.migration_checksum_mismatch` (FR-017, FR-020).
5. **Order**: modules in dependency order, ties broken by ordinal module name, then versions ascending. Reverse steps run in exactly the opposite order.
6. **Each migration** runs in its own transaction with `search_path` set to its module's schema. A failure rolls back its changes and records nothing, and earlier migrations stay applied. The failure names the module and version with a safe reason, never SQL or server text (FR-019).
7. **History**: `isk_migrations.history` records module, version, checksum, `applied_at`, and `applied_by` (`current_user`) per applied migration (FR-018). A reverse step deletes the row in the same transaction.
8. **Lock**: the runner holds a session advisory lock on the key **6156145740695756181** (`0x556F05EF3BB45D95`, from SHA-256 of `iskender.migrations.lock.v1`), which appears in `pg_locks` as `classid = 1433339375`, `objid = 1001676181`, `objsubid = 1`. It polls for the lock until `migration.lockWait` passes, then fails with `persistence.migration_locked` and changes nothing. A crashed holder's lock is released with its connection (FR-021).
9. **Identities**: the runner uses the `migration` credentials. The runtime login needs no right to create, alter, or drop anything (FR-022).
10. **Reverse**: one migration per step by default. A data-losing reverse step without `ConfirmDataLoss` fails with `persistence.reverse_requires_confirmation`, and nothing changes (FR-023).
11. **Explicit only**: migrations run only when `IMigrationRunner.ApplyAsync` or `ReverseAsync` is called. Nothing in the adapter runs them on start-up. ISK-0007 exposes the operation (FR-021, AV-2).

## Compatibility window

1. Each module declares `SchemaVersionRange(Minimum, Maximum)`. Its schema version is its highest applied version (FR-024).
2. A migration must keep the previous release working: change by expanding first and contracting in a later release, so the rolling-upgrade window is one release.
3. `Maximum` may exceed the module's latest version. That promises the next release's migrations up to `Maximum` are expand-only. A migration that would break the previous release is numbered above the previous release's `Maximum`.
4. This is a compatibility rule, not a performance claim.

## Health

*Review owner: Architecture (ADR) owner; Privacy/Operations owner for rule 4.*

1. `IPersistenceHealthProbe.CheckAsync` returns a status and one reason. It never throws for a database condition, and liveness never calls it.
2. It runs `SELECT 1`, one read of the history and schema privileges, and one read of `pg_locks`, within `healthProbeTimeout`, and returns within that plus a fixed 2-second allowance, a safety bound (FR-025).
3. Reasons, first match wins:

   | Reason | Status | When | Operator action |
   |---|---|---|---|
   | `unreachable` | unhealthy | Cannot connect, or the connection broke | Check the network path, the server, and TLS |
   | `authentication_failed` | unhealthy | Rejected credentials, or no credential provider | Check the runtime login and its secret reference |
   | `timeout` | unhealthy | The probe's timeout fired | Check server load and the network |
   | `migration_in_progress` | unhealthy | The migration lock is held | Wait for the migration to finish |
   | `checksum_mismatch` | unhealthy | An applied migration's script changed | Restore the shipped script; never edit an applied one |
   | `schema_behind` | unhealthy | A module is below its `Minimum`, the history is missing, or the runtime role lacks `USAGE` on a module schema | Run migrations, or fix the grants |
   | `schema_ahead` | unhealthy | A module is above its `Maximum` | Deploy the newer release, or reverse the newer migrations |
   | `ok` | healthy | Otherwise, including unknown migrations inside the range | |

4. No probe result, log, metric, trace, or error contains a host, port, database name, user name, password, connection text, SQL, parameter value, or server error text (FR-026).

## Configuration

*Review owner: Architecture (ADR) owner; Security owner for TLS and passwords.*

The adapter registers the ISK-0003 namespace `persistence` (FR-029):

| Field | Type | Default | Notes |
|---|---|---|---|
| `host` | string | required | |
| `port` | integer 1–65535 | 5432 | |
| `database` | string | required | |
| `username` | string | required | Runtime login |
| `password` | `secretRef` | | Runtime password |
| `tlsMode` | enumeration `require`, `disable` | `require` | Development-only. **`require` verifies the server certificate chain against the system trust store and the host name against the certificate**, which is stricter than libpq's `require` |
| `poolMinimum` | integer 0–1000 | 0 | Not above `poolMaximum` |
| `poolMaximum` | integer 1–1000 | 20 | |
| `connectionTimeout` | duration 1 s–5 m | 15 s | |
| `statementTimeout` | duration 1 s–1 h | 30 s | |
| `healthProbeTimeout` | duration 1 s–1 m | 5 s | |
| `migration.username` | string | required | Migration login |
| `migration.password` | `secretRef` | | |
| `migration.lockWait` | duration 0–1 h | 60 s | |

There is no connection string field. Every default is a safety bound, not a performance figure.

## Credentials

*Review owner: Security owner.*

1. The adapter gets each password from the host's `IDatabaseCredentialProvider`, passing the configured `SecretReference` and a purpose (`Runtime` or `Migration`), once per physical connection open. It caches nothing and logs nothing (FR-031).
2. ISK-0004 ships no implementation outside test projects. Until ISK-0021 provides one, a production host cannot connect: connection attempts fail with `persistence.unavailable`, and the probe reports `authentication_failed`.
3. `DatabaseCredential.ToString()` returns `[redacted]`.

## Error codes

All are added to `ErrorCodes` with `since: 3`, raising `core-contracts` from 2 to 3 with compatible changes only (FR-030).

| Code | Category | Safe message |
|---|---|---|
| `persistence.unavailable` | `internal` | The data store is unavailable. |
| `persistence.timeout` | `deadline_exceeded` | The data store did not respond in time. |
| `persistence.cancelled` | `cancelled` | The data store operation was cancelled. |
| `persistence.concurrency_conflict` | `conflict` | The data changed while the request was being processed. |
| `persistence.unique_conflict` | `conflict` | A record with the same identity already exists. |
| `persistence.scope_required` | `internal` | A data store operation was attempted without an owner scope. |
| `persistence.scope_not_permitted` | `permission_denied` | This data is not available in the current scope. |
| `persistence.lock_timeout` | `conflict` | A required lock could not be acquired in time. |
| `persistence.migration_locked` | `internal` | Another schema migration is in progress. |
| `persistence.migration_checksum_mismatch` | `internal` | An applied schema migration does not match this release. |
| `persistence.schema_incompatible` | `internal` | The database schema is not compatible with this release. |
| `persistence.reverse_requires_confirmation` | `invalid_request` | This rollback step deletes data and must be confirmed. |

## Diagnostics

*Review owner: Privacy/Operations owner.*

Fields are limited to module, migration version, operation kind, outcome, failure class, reason code, and durations. No owner ID, SQL, parameter value, or connection detail appears in any field or label (FR-032).

| Event ID | Name | Level | When |
|---|---|---|---|
| 4001 | `PersistenceConnectionFailed` | Warning | A connection attempt fails, with its class |
| 4002 | `UnitOfWorkRolledBack` | Debug | A unit ends without commit, with its reason |
| 4003 | `UnitOfWorkFaulted` | Warning | A unit faults, with its class |
| 4004 | `UnitOfWorkCommitOutcomeUnknown` | Error | The connection broke during commit |
| 4005 | `TransientConflictRetried` | Information | The retry helper starts another attempt |
| 4006 | `AdvisoryLockTimedOut` | Warning | A bounded lock wait expired |
| 4007 | `MigrationStarted` | Information | The runner holds the lock and starts |
| 4008 | `MigrationLockWaiting` | Information | The lock is held by another migrator |
| 4009 | `MigrationStepApplied` | Information | One migration applied or reversed |
| 4010 | `MigrationStepFailed` | Error | One migration failed, with module, version, and class |
| 4011 | `MigrationCompleted` | Information | The run finished |
| 4012 | `MigrationRefused` | Error | The set or the database state was refused, with the code |
| 4013 | `PersistenceHealthChanged` | Information | The probe's reason differs from its last result |
| 4014 | `PersistenceUnexpectedFailure` | Error | A failure classified as unexpected, with the SQLSTATE class only (two characters) |

Activities come from `ActivitySource("Iskender.Persistence")` (`persistence.unit_of_work`, `persistence.migration_step`), and metrics from `Meter("Iskender.Persistence")` (`iskender.persistence.pool.connections`, `iskender.persistence.connection.acquire_duration`, `iskender.persistence.unit_of_work.duration`, `iskender.persistence.conflicts`, `iskender.persistence.retries`). Npgsql's own logging and tracing are not enabled.

## Test fixture

*Review owner: Performance owner (+ QA).*

1. `tests/Iskender.Infrastructure.PostgreSql.Tests` provides the shared fixture: one container per test run, from the official PostgreSQL 17 image pinned by digest, and one database per test class, created from an empty or migrated template (FR-033).
2. Later storage specs reuse it by referencing the test project's fixture types, or by moving them into a shared test project in their own spec.
3. Without a container runtime, container-backed tests are skipped, each with its reason. When `CI` is `true`, they fail instead (FR-035).
4. Fixture migrations are embedded only in the test assembly and ship in no package (FR-034).

## Threat model and known limits

### What the rules defend against

The Negative Security and Failure Scenarios table in [spec.md](../spec.md) lists each threat with the requirement that covers it. This contract's rules implement them: a forgotten predicate (Isolation 2, 4), pooled-connection leaks (Isolation 3), operator bypass (Isolation 6), ID enumeration (Isolation 5), schema tampering (Migrations 3, 4), racing migrators (Migrations 8), surprise data loss (Migrations 10), credential and connection-detail leaks (Health 4, Credentials), and slow queries holding connections (Units of work 7).

### Known limits

- **Dynamic SQL in migrations**: the schema check reads script text, so a schema name built at run time with `EXECUTE format(...)` is not seen. Migration review covers it.
- **Code outside the adapter**: a production project other than the adapter cannot reference Npgsql (rule A19), but code inside the adapter could build a command without `Command(TableAccess, sql)`. The adapter's tests and review cover it, and row-level security still applies.
- **Lock key collisions**: two different (scope, module, name) triples could share a 64-bit key. The effect is extra waiting, not lost exclusion.
- **`require` needs a publicly trusted or system-trusted certificate**: a server with a private CA cannot be used outside `development` until a later spec adds a CA bundle setting.
- **One PostgreSQL major version is tested**: 17. Older versions are unsupported.

## Left to later specs

| Item | Spec |
|---|---|
| The migration command and readiness mapping | ISK-0007 |
| Host composition and OpenTelemetry subscription | ISK-0006 |
| Leases, fencing, Redis | ISK-0005 |
| Local Compose PostgreSQL | ISK-0008 |
| The outbox, written in the same unit | ISK-0009 |
| Organization, project, and registry tables | ISK-0011, ISK-0012 |
| The production credential provider | ISK-0021 |
| The elevated operator operation over tenant data | The first spec that needs it |
