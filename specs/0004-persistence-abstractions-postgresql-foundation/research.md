# Research: ISK-0004 — Persistence Abstractions & PostgreSQL Foundation

Each entry records a decision, why it was made, and what was rejected. The spec left no `NEEDS CLARIFICATION` marker. `specify` settled TR-3, TR-14, and TR-17, and `clarify` settled the supported-range health rule, the migration order, operator-scope failures, and repository homes. These entries settle how the approved requirements are built, and change none of them. Four were decided by the maintainer during `plan` on 2026-11-09, as R1, R9, R13, and R15 record. R13's decision adds a ninth health reason to the spec's TR-11 and SC-006.

## R1. The PostgreSQL driver (AV-1)

**Decision**: `Npgsql` 10.0.3. Its `net10.0` dependency group has one package, `Microsoft.Extensions.Logging.Abstractions` (MIT, `>= 10.0.0`), which central transitive pinning already holds at 10.0.14. Only `Iskender.Infrastructure.PostgreSql` references it.

**License**: Npgsql's `.nuspec` declares the SPDX expression `PostgreSQL`, which is OSI-approved and permissive but not on `check-license.sh`'s allow-list. **The maintainer, as Product/Charter owner, approved adding `PostgreSQL` to the allow-list during `plan` on 2026-11-09.** The PR adds it to `allowed_licenses` and says so in its notes. The approval covers the expression, not one package ID.

**Rationale**: Npgsql is the only maintained ADO.NET provider for PostgreSQL on .NET 10. It supports what the spec needs: server-side cancellation through a cancel request when a token fires, per-physical-connection password callbacks (`UsePasswordProvider`, R17), `SslMode.VerifyFull` (R15), SQLSTATE codes on `PostgresException`, and a bounded pool through `NpgsqlDataSource`. `Iskender.Infrastructure.<Adapter>` denies only the `provider` category, so rule A19 allows the `driver` category here, with no ISK-0001 revision.

**Alternatives considered**: None is maintained. ODBC drivers would need a native install, which breaks the offline restore-and-test path.

## R2. No data-access library in this spec

**Decision**: No EF Core, Dapper, or other mapper. The adapter uses Npgsql's ADO.NET commands directly, with positional parameters (`$1`) only, never string concatenation of values.

**Rationale**: The spec's surface is connections, transactions, locks, migrations, and health. None of it needs mapping, and `docs/04` and the decision register list the mapper split as a recommendation for a storage spec's tests to settle. The fixture repositories in tests are a few statements each. A later spec that wants Dapper adds it to the adapter, which rule A19 already allows. Keeping parameters positional stops any value from being written into SQL text, which FR-026 also needs.

**Alternatives considered**: EF Core, whose model-generated migrations TR-8 rejects, and whose change tracker hides statement boundaries; Dapper now, which would be an unused dependency.

## R3. The migration library: a small runner in the adapter

**Decision**: Iskender writes its own migration runner in `Iskender.Infrastructure.PostgreSql.Migrations`. It has no package dependency beyond Npgsql.

**Rationale**: TR-8 says the library must support everything listed, and none of the candidates does:

| Requirement | DbUp | FluentMigrator | Evolve | grate |
|---|---|---|---|---|
| Plain SQL forward and paired reverse scripts | No reverse | Reverse in C#, not SQL | No reverse (undo is a paid Flyway feature) | No reverse |
| Module ownership, schema per module, module dependency order | No | No | No | No |
| SHA-256 of normalized text, refuse on mismatch | Journal only | No checksum | Checksum, other algorithm | Hash, other normalization |
| Data-losing reverse refused without confirmation | No | No | No | No |
| Session advisory lock on a documented key, bounded wait | No | No | Lock, fixed key, no bound | No |

Wrapping one of them would leave most of the behavior in Iskender's code anyway, while adding a package whose semantics the tests would have to pin. The runner is about as large as the set validator and the lock, and every rule it enforces has a test.

**Alternatives considered**: The four libraries above; generating migrations from a mapper model, which TR-8 rejects.

## R4. Opening a unit of work only from a trusted scope (FR-004, FR-005)

**Decision**: `IUnitOfWorkFactory.OpenAsync` takes a `UnitOfWorkScope`, a sealed class in `Iskender.Domain.Ports` with no public constructor. Its only factories are:

- `UnitOfWorkScope.ForResource(ScopedResourceReference)`: the attested owner of a resource. This is the route for background workers, which get the reference from a trusted resolver.
- `UnitOfWorkScope.ForRequest(GatewayExecutionContext)`: the request's resolved owner, with the request's deadline and cancellation token.
- `UnitOfWorkScope.Operator`: the operator scope, for system data.

None takes an `OwnerScope`, so a scope built from caller values cannot be passed (`OwnerScope` has public constructors, and ISK-0002 calls such a scope "inert until a resolver attests it"). A `null` argument, or a context whose owner is not resolved, fails with `persistence.scope_required` before any connection is acquired.

**Rationale**: Making the attested reference the only tenant route turns "forgot the scope" into a compile error and "unresolved scope" into a fail-closed error before any I/O.

**Alternatives considered**: An `OwnerScope` parameter, which accepts caller-built values; an ambient scope in `AsyncLocal`, which TR-2 rejects.

## R5. Row-level security keyed on transaction-local settings (FR-008, FR-010)

**Decision**:

1. A **tenant-owned table** is one with an `owner_organization_id text not null` column. A **project-owned** table also has `owner_project_id text not null`. Both hold the canonical `ResourceId` text. The automated check reads this from the catalog, so a table cannot opt out by omission from a list.
2. The unit opens its transaction and sets three settings in the same round trip, all transaction-local (`set_config(name, value, true)`): `iskender.scope_kind` (`organization`, `project`, or `operator`), `iskender.organization_id`, and `iskender.project_id`. Unused ones are set to the empty string.
3. Each tenant-owned table has `ENABLE` and `FORCE ROW LEVEL SECURITY`, and one policy for all commands, with the same expression in `USING` and `WITH CHECK`. For an organization-owned table:

   `owner_organization_id = nullif(current_setting('iskender.organization_id', true), '')`

   For a project-owned table, the same plus `AND owner_project_id = nullif(current_setting('iskender.project_id', true), '')`, which an organization scope cannot satisfy, because its project setting is empty.
4. Outside a unit's transaction, the settings revert. After a transaction-local `set_config`, PostgreSQL keeps the setting defined as an empty string on that connection, so `nullif(..., '')` makes an empty or missing setting match no row. The operator scope sets empty IDs, so row-level security returns no tenant rows to it.
5. The runtime role is a member of the `NOLOGIN` group role `iskender_runtime`, owns nothing, and has neither `BYPASSRLS` nor superuser. The migration role owns every module schema and table. Migrations grant `SELECT, INSERT, UPDATE, DELETE` on tables and `USAGE` on sequences to `iskender_runtime`, through default privileges set when the runner creates the schema (R12).
6. **Policy check**: after migration, an integration test lists every table in an `isk_*` schema that has `owner_organization_id`, and fails unless `relrowsecurity`, `relforcerowsecurity`, and at least one policy are all present, and unless a project-owned table's policy names `owner_project_id`. The migration runner runs the same check after each module and refuses to record the module complete if it fails. A fixture migration that creates a tenant table without a policy proves both.

**Rationale**: Transaction-local settings cannot outlive the transaction, so a pooled connection carries nothing into the next unit (TR-7). Putting the predicate in a policy keyed on those settings gives the second layer TR-3 asks for. Detecting tenant ownership from a column means a new table is checked by default.

**Alternatives considered**: Session settings reset on return to the pool, which fails open when the reset is skipped; a role per tenant, which does not scale and needs `SET ROLE`; a list of tenant tables kept in code, which a new table can be left out of.

## R6. Refusing tenant tables in the operator scope before sending SQL (FR-007)

**Decision**: Repository code in the adapter creates commands only through `PostgreSqlUnitOfWork.Command(TableAccess access, string sql)`. `TableAccess` names the module table the statement uses, as a `TenantTable` or an `OperatorTable` descriptor that the module declares once. A unit in the operator scope that is given a `TenantTable` fails with `persistence.scope_not_permitted` before the command is created. A unit in a tenant scope given an `OperatorTable` fails the same way, because tenant units never touch operator data either. An `internal` test seam turns the pre-check off, so the bypass test can prove that row-level security alone returns and changes zero rows. The elevated operation is reserved: no API in this spec opens one.

The fixture architecture test (R21) fails when SQL text in a module namespace names a table its descriptors do not declare, so a statement cannot hide a tenant table behind an `OperatorTable` descriptor.

**Rationale**: The pre-check gives a clear `permission_denied` error rather than an empty result, which the clarification asked for, and row-level security stays the backstop.

**Alternatives considered**: Parsing every statement at run time, which is slow and incomplete; relying on row-level security alone, which returns zero rows silently, not the error the clarification chose.

## R7. Timeouts, deadline, and cancellation (FR-009)

**Decision**:

| Bound | Source | Mechanism |
|---|---|---|
| Acquire a connection | `connectionTimeout`, capped by the remaining deadline | `NpgsqlDataSource.OpenConnectionAsync` with a linked token that fires at the earlier time. Pool exhaustion waits under the same token |
| Each statement | `statementTimeout`, capped by the remaining deadline | `SET LOCAL statement_timeout` at unit open (the server bound), and the command's token (the client bound, which sends a cancel request) |
| The whole unit | `UnitOfWorkOptions.Timeout` (default 60 s), capped by the remaining deadline | A linked `CancellationTokenSource` with `CancelAfter`. When it fires, the running statement is cancelled on the server, the unit is faulted, and it rolls back |
| Request cancellation | `GatewayExecutionContext.CancellationToken` or the caller's token | Linked into every token above |

A deadline already passed when the unit is opened fails with `persistence.timeout` and acquires nothing. When a token fires, the adapter reports `persistence.timeout` if a deadline or timeout fired it, and `persistence.cancelled` if the caller's or the request's token did.

**Judgment for review**: TR-13's configuration fields have no "unit timeout". The unit's own bound comes from `UnitOfWorkOptions.Timeout`, set by the module that opens the unit, with a 60-second default. This is a safety bound, not a performance figure, and adds no configuration field.

**Rationale**: Npgsql's cancellation sends PostgreSQL's cancel request, so the query stops on the server. The server-side `statement_timeout` covers the case where the client cancel request is lost.

**Alternatives considered**: Client-side timeouts only, which leave the query running; a new configuration field, which TR-13 does not list.

## R8. Classifying failures (FR-013, FR-015)

**Decision**: One classifier in the adapter maps every exception from Npgsql to a class and code. Raw exceptions never cross a port: callers see a `GatewayException` with the code (R9), with no inner exception, and its message is the code's safe message.

| Observed | Class | Code |
|---|---|---|
| SQLSTATE `40001` (serialization failure), `40P01` (deadlock) | Transient conflict | `persistence.concurrency_conflict` |
| A stale version token (R10) | Concurrency conflict | `persistence.concurrency_conflict` |
| SQLSTATE `23505` | Unique conflict | `persistence.unique_conflict` |
| SQLSTATE class `08`, `57P01`, `57P02`, `57P03`, socket and I/O errors, a broken connection | Unavailable | `persistence.unavailable` |
| SQLSTATE `57014` or a token, when a timeout or deadline fired | Timeout | `persistence.timeout` |
| SQLSTATE `57014` or a token, when the caller's or request's token fired | Cancelled | `persistence.cancelled` |
| SQLSTATE `55P03` during an advisory lock wait | Timeout (lock) | `persistence.lock_timeout` |
| SQLSTATE `28P01`, `28000`, or no credential provider registered | Authentication failed | `persistence.unavailable` |
| Anything else, including `42501` (insufficient privilege) | Unexpected | `core.internal` |

A failure inside `COMMIT` that leaves the connection broken sets `CommitOutcomeUnknown` and reports `persistence.unavailable`, never success and never a definite rollback. The retry helper never re-runs such a unit.

**Judgment for review**: TR-15 lists no code for the unexpected class or for authentication failure. Unexpected failures use the existing ISK-0002 `core.internal`, and authentication failure uses `persistence.unavailable`, which FR-031 already names for a missing credential provider. The health probe still tells them apart by reason code (`authentication_failed`). No new code is invented.

**Rationale**: One table keeps every module's handling the same, which is TR-5's point.

## R9. The failure type and the retry helper (FR-014)

**Decision, made by the maintainer during `plan` on 2026-11-09**: Ports fail only with the ISK-0002 `GatewayException`, built with `new GatewayException(code)`, so its message is the code's safe message and it has no inner exception. There is no persistence exception type. What the code alone cannot say is recorded on the unit: when a unit faults, `IUnitOfWork.Failure` holds a `PersistenceFailure` with the `ErrorCode`, the `PersistenceFailureClass`, and `CommitOutcomeUnknown`. Callers and the host's error mapping (ISK-0006, ISK-0007) handle one exception type, as ISK-0002's FR-042 expects, and `Iskender.Core.Contracts` does not change beyond the 12 codes.

`UnitOfWorkRetry.RunAsync` takes the factory, the scope, the options, and the work. It opens a new unit for each attempt. When the work or the commit throws, it reads the unit's `Failure` before disposing it, and re-runs the whole unit only when the class is transient conflict, `CommitOutcomeUnknown` is false, and `UnitOfWorkOptions.SafeToRerun` is true. An exception with no recorded failure, such as one the work itself raised, is never retried. It makes at most 3 attempts in all, then throws `persistence.concurrency_conflict`. It waits no fixed delay between attempts: a short random jitter, at most 50 ms and drawn from the injected `TimeProvider`'s clock for tests, spreads contending units.

**Rationale**: Only the caller knows whether its work can be repeated, and PostgreSQL cannot retry one statement inside an aborted transaction.

**Alternatives considered**: A sealed `PersistenceException` in the ports, which the first draft of this plan used. Code that catches `GatewayException` would miss it, and the error mapping would need a second type. Unsealing `GatewayException` so a persistence exception could derive from it, which the harness treats as compatible but which lets any plugin subclass the core error type, weakening an ISK-0002 choice. Polly for the retry loop, which `docs/04` lists as a candidate. It adds a package to `Iskender.Domain` for a loop of a few lines with one predicate.

## R10. Optimistic concurrency (FR-016)

**Decision**: A versioned table has `row_version bigint not null`, starting at 1. `VersionToken` is a struct in `Iskender.Domain.Ports` wrapping a positive `long`. `VersionedRows.UpdateAsync` and `DeleteAsync` in the adapter run `... WHERE id = $1 AND row_version = $2`, setting `row_version = row_version + 1` and returning the new token. When no row changes, one follow-up `SELECT 1 ... WHERE id = $1` in the same unit tells a stale token (`persistence.concurrency_conflict`) from a missing or foreign row (`core.resource.not_found`). Because the follow-up runs under the same row-level security, another scope's row reads as missing.

**Rationale**: A monotonic counter is simple, needs no clock, and PostgreSQL's `xmin` changes on vacuum freeze and cannot be relied on.

## R11. Migration scripts, naming, and checksums (FR-017 to FR-020, FR-023)

**Decision**:

1. A module ships its scripts as embedded resources named `<module>/<version>_<slug>.up.sql` and `<module>/<version>_<slug>.down.sql`, where `<version>` is a positive integer written with 4 digits.
2. A reverse script whose first line is `-- iskender:data-losing` is data-losing.
3. **Normalization** before hashing: decode as UTF-8 with no BOM (a BOM is removed, and invalid UTF-8 refuses the set), and change CRLF and lone CR to LF. Nothing else changes, so whitespace and comments count.
4. **Checksum**: SHA-256 over the normalized forward text, one `0x00` byte, and the normalized reverse text, written as lowercase hex. Editing either script after it is applied stops the runner.
5. **Set validation**, all before anything is applied: each up script has its down script and the reverse; versions are unique within a module; every module named as a dependency exists; the dependency graph has no cycle; and every schema-qualified name in each script names its own module's schema. The schema check uses a small lexer that skips comments, string literals, dollar-quoted bodies, and quoted identifiers' contents, and matches identifiers of the form `isk_<name>`. A script that uses another `isk_` schema, or `public.`, fails. While a script runs, `search_path` is set to its own schema only.
6. **Order**: a topological sort of modules by declared dependencies, with ties broken by ordinal module name, then versions ascending within a module. Reverse steps run in exactly the opposite order.

**Rationale**: Checksumming both scripts stops a silent edit to a rollback path. A lexer is enough to catch an accidental cross-module reference, and the per-script `search_path` stops unqualified names from reaching another schema.

**Known limit**: The lexer cannot see a schema name built at run time inside dynamic SQL (`EXECUTE format(...)`). Migration review covers that, and the contract says so.

## R12. History, roles, and the migration lock (FR-018, FR-021, FR-022)

**Decision**:

- The framework owns the schema `isk_migrations`, which holds `isk_migrations.history (module text, version integer, checksum text, applied_at timestamptz, applied_by text, primary key (module, version))`. `applied_by` is `current_user`. The runner creates the schema and table in its own first transaction if missing, and grants `SELECT` on the history to `iskender_runtime` so the health probe can read it.
- When a module has no schema yet, the runner creates it with `CREATE SCHEMA isk_<module>`, owned by the migration role, then grants `USAGE` to `iskender_runtime` and sets default privileges (R5). The reverse of a module's version 1 leaves an empty schema. The runner does not drop schemas.
- The session advisory lock key is the signed 64-bit integer from the first 8 bytes, big-endian, of SHA-256 of the ASCII text `iskender.migrations.lock.v1`. The contract publishes its decimal value, and a unit test pins it. The runner takes it with `pg_try_advisory_lock` polled every 250 ms until `migration.lockWait` passes, then fails with `persistence.migration_locked`. Polling, not `lock_timeout`, keeps the runner's connection usable to report the failure.
- Before taking the lock, the runner checks that the group role `iskender_runtime` exists, and refuses with `persistence.schema_incompatible` and a safe reason if not. The runbook provisions the roles.

**Rationale**: The history table carries what FR-018 lists and nothing else. A fixed, published key lets an operator see the lock in `pg_locks`. A crashed holder's connection closes, so PostgreSQL releases the session lock.

## R13. Supported schema range and health judgment (FR-024, FR-025)

**Decision**: Each module declares `SchemaVersionRange(Minimum, Maximum)`. `Maximum` may be above the module's latest shipped version, which is how a release promises that the next release's migrations up to that number are expand-only. A migration that would break the previous release is numbered above the previous release's `Maximum`. A module's **schema version** is the highest version recorded for it in the history.

The probe runs `SELECT 1`, then one read of the history and one of `pg_locks`, all within `healthProbeTimeout`, and decides in this order:

| Order | Condition | Reason |
|---|---|---|
| 1 | The connection fails as unavailable | `unreachable` |
| 2 | The connection fails on authentication, or no credential provider is registered | `authentication_failed` |
| 3 | The probe's timeout fires | `timeout` |
| 4 | The migration lock is held (a row in `pg_locks` with `locktype = 'advisory'` and the published key) | `migration_in_progress` |
| 5 | The history table is missing | `schema_behind` |
| 6 | The history read fails with SQLSTATE `42501`, or the runtime role lacks `USAGE` on a module schema the history shows as created | `insufficient_privilege` |
| 7 | An applied migration known to the release has a different checksum | `checksum_mismatch` |
| 8 | Any module's schema version is below its `Minimum` | `schema_behind` |
| 9 | Any module's schema version is above its `Maximum` | `schema_ahead` |
| 10 | Otherwise | `ok` (`healthy`) |

Unknown applied migrations inside the range report `ok`, and the migration runner still refuses while any is present (clarification of 2026-11-09).

The privilege check is `has_schema_privilege(current_user, 'isk_<module>', 'USAGE')` for each module the release knows that has history, run in the same round trip as the history read. A module with no history yet has no schema, so it is `schema_behind`, not a privilege failure. **Decided by the maintainer during `plan` on 2026-11-09**: this is a ninth reason, `insufficient_privilege`, added to TR-11 and SC-006, rather than reusing `schema_behind`, which would tell an operator to run migrations when the fix is a grant.

The fixed allowance in FR-025 is 2 seconds: the probe must return within `healthProbeTimeout` plus 2 s. This is a safety bound for the test, not a performance figure.

## R14. Advisory lock keys (FR-027, FR-028)

**Decision**: `AdvisoryLockKey.Derive(OwnerScope, module, name)` in `Iskender.Domain.Ports` computes SHA-256 over the UTF-8 text `iskender.lock.v1`, a `0x00`, the scope's canonical form (`org:<id>`, `project:<org id>/<project id>`, or `operator`), a `0x00`, the module name, a `0x00`, and the lock name, and takes the first 8 bytes, big-endian, as a signed 64-bit key. A module name is 1 to 31 characters of `a-z`, `0-9`, and `_`, starting with a letter, the same as a migration module's (R11). A lock name is 1 to 63 characters of `a-z`, `0-9`, `_`, and `.`, starting with a letter. The adapter takes it with `pg_advisory_xact_lock` under `SET LOCAL lock_timeout` for a bounded wait (restored afterwards), or `pg_try_advisory_xact_lock` for try-only. The scope is the unit's own scope, so a module cannot take another tenant's lock. A collision between two different triples is possible in principle (a 64-bit hash), and its effect is only extra waiting, never a correctness loss.

**Rationale**: A versioned prefix lets the derivation change later without colliding with old keys. Derivation sits in the ports so it is documented and unit-tested without a database.

## R15. TLS mode semantics (TR-13, FR-029)

**Decision, made by the maintainer during `plan` on 2026-11-09**: `tlsMode` is an enumeration with two members, `require` (the default) and `disable`, and the field is development-only, so `disable` fails with `config.value.unsafe_for_environment` outside `development`. **Iskender's `require` means encrypted and verified**: the adapter uses `SslMode.VerifyFull`, which checks the server certificate chain against the system trust store and the host name against the certificate. This is stricter than libpq's `require`, which does not verify, and the contract and runbook say so. A custom CA bundle setting is post-MVP.

**Rationale**: ISK-0003 marks a whole field development-only, so any non-default value of a single mode field would fail outside `development`. Two members keep that rule correct, and verifying by default stops a man-in-the-middle on the database link (`docs/04` protected links). Testcontainers and local Compose run in `development` with `disable`.

**Alternatives considered**: `require` meaning encrypt-only, as libpq does, which leaves the link open to interception; a separate verification field, which TR-13 does not list.

## R16. The `iskender.persistence` section (FR-029)

**Decision**: The adapter registers namespace `persistence` through ISK-0003's `SectionSchema`. Fields: `host` (string, required), `port` (integer 1–65535, default 5432), `database` (string, required), `username` (string, required), `password` (`secretRef`), `tlsMode` (R15), `poolMinimum` (integer 0–1000, default 0), `poolMaximum` (integer 1–1000, default 20), `connectionTimeout` (duration 1 s–5 m, default 15 s), `statementTimeout` (duration 1 s–1 h, default 30 s), `healthProbeTimeout` (duration 1 s–1 m, default 5 s), and the object `migration` with `username` (string, required), `password` (`secretRef`), and `lockWait` (duration 0–1 h, default 60 s). There is no connection string field. `poolMinimum` above `poolMaximum` is rejected when the adapter builds its settings, with `config.field.out_of_range` naming `poolMinimum`. Every default is a safety bound, not a performance figure.

**Rationale**: These are TR-13's fields exactly. Separate fields let ISK-0003 validate each one, and the passwords stay secret references.

## R17. The database-credential port (FR-031)

**Decision**: `IDatabaseCredentialProvider.GetCredentialAsync(DatabaseCredentialRequest, CancellationToken)` in `Iskender.Domain.Ports`. The request carries the `SecretReference` from the configured field and a `DatabaseCredentialPurpose` (`Runtime`, `Migration`). It returns `DatabaseCredential`, which holds the password and whose `ToString` returns `[redacted]`. The adapter calls it from Npgsql's `UsePasswordProvider` callback, which runs once per physical connection open, so nothing is cached in settings or logged. With no provider registered, the callback throws, the classifier reports `persistence.unavailable`, and the probe reports `authentication_failed`. Only a test project implements the port in this spec.

**Rationale**: It keeps secret resolution in ISK-0021, as the maintainer decided for TR-14, and Npgsql's callback is designed for exactly this.

## R18. Diagnostics (FR-026, FR-032)

**Decision**:

- **Log events** through `Microsoft.Extensions.Logging.Abstractions`, already pinned, with message templates and event IDs 4001 to 4014, listed in the contract. Fields are limited to module, migration version, operation kind, outcome, failure class, reason code, and durations. `[LoggerMessage]` is not used, as in ISK-0003 (no source generator).
- **Activities** from `ActivitySource("Iskender.Persistence")`: `persistence.unit_of_work` per unit and `persistence.migration_step` per migration, with tags `iskender.module`, `iskender.operation`, `iskender.outcome`, and `iskender.failure_class`. Npgsql's own tracing is not enabled, because its spans carry SQL text and the database name.
- **Metrics** from `Meter("Iskender.Persistence")`: `iskender.persistence.pool.connections` (observable gauge: idle, busy), `iskender.persistence.connection.acquire_duration` and `iskender.persistence.unit_of_work.duration` (histograms, seconds), and `iskender.persistence.conflicts` and `iskender.persistence.retries` (counters). Labels are only module, operation kind, outcome, and failure class.
- Npgsql's own logging is not wired to the host's logger factory, because its messages can include connection details.

Both `ActivitySource` and `Meter` are in the base class library, so no OpenTelemetry package is added. The host's OpenTelemetry setup (ISK-0006) subscribes to the names.

## R19. The Testcontainers fixture (FR-033, FR-035, AV-6, AV-7)

**Decision**:

- Packages, in the test project only: `Testcontainers.PostgreSql` 4.16.0 (MIT), which brings `Testcontainers` and its dependencies. `check-license.sh` reads only `src/` lock files, so these are outside it. CI check 9 still scans them for vulnerabilities.
- Image: the official `postgres` image, major version **17**, Debian variant, pinned by the digest of its multi-architecture index so it runs on both amd64 CI and arm64 laptops. **The digest is read from the registry during `implement` and recorded as found. It is never invented.** PostgreSQL 17 is both the tested and the oldest supported major version, and the runbook says so. Azure Database for PostgreSQL offers 17, and ISK-0085 confirms the reference topology.
- One container per test run, through an xUnit v3 assembly fixture. At start, it runs an init script that creates the login roles `isk_migrator` and `isk_runtime_login`, the group role `iskender_runtime`, and two template databases: `isk_template_empty` and `isk_template_migrated`, which has the fixture modules applied.
- Each test class gets its own database, `CREATE DATABASE isk_t_<n> TEMPLATE <template>`, so classes run in parallel without sharing rows.
- **No container runtime**: the fixture records why it could not start. Each container-backed test calls `RequirePostgreSql()`, which calls `Assert.Skip(reason)`. When the environment variable `CI` is `true`, as GitHub Actions sets it, it throws instead, so the test fails and the run fails (TR-17). Unit tests that need no database run either way.
- The ubuntu-24.04 GitHub runner provides Docker, so the integration tests run in the existing check 4 and no CI check is added (AV-7).

**Rationale**: One container keeps the suite fast, template databases make per-class isolation cheap, and digest pinning stops a moving tag from changing results.

## R20. Comparing schemas by catalog snapshot (SC-002)

**Decision**: A test helper renders a deterministic text snapshot of every `isk_*` schema except `isk_migrations` from `pg_catalog`: tables, columns with types, nullability, and defaults, constraints, indexes, row-level security flags, policies with their expressions, and grants, each sorted by name. Expected snapshots after each forward step are committed fixtures. The round-trip test compares after every step, and compares the second forward run with the first.

**Rationale**: `pg_dump` is not in the test image's client path, and its output varies by version. A catalog query is stable, and is rendered by the code under test's own connection.

## R21. Tests, fixtures, and the module-namespace check (FR-002a, FR-034, FR-036)

**Decision**:

- A new test project, `tests/Iskender.Infrastructure.PostgreSql.Tests`, holds unit tests for the adapter and every container-backed integration test. Pure port logic (scope factories, retry helper, lock key derivation, version token) is tested in the existing `tests/Iskender.Domain.Tests` under `Ports/`.
- The test-only sample modules live in that project under the namespaces `Iskender.Infrastructure.PostgreSql.Tests.Modules.Alpha` and `.Beta`, with schemas `isk_alpha` and `isk_beta`, mirroring the production layout. Alpha has migrations 1 to 4, Beta has 1 to 3 and depends on Alpha (7 migrations in 2 modules). Alpha 3 adds a column whose reverse is data-losing, and Alpha 4 is the "next release" expand migration used by the previous-release test (R22). Their scripts are embedded in the test assembly only, so they ship in no package.
- **Module-namespace check**: a test reads every `.cs` file under `src/Iskender.Infrastructure.PostgreSql/` and the test modules, lexes its string literals, and finds every `isk_<name>` identifier. In a module namespace `...PostgreSql.<Module>` it must be `isk_<module, lowercased>`. In the framework namespaces (`Iskender.Infrastructure.PostgreSql`, `.Migrations`, `.Health`, `.Diagnostics`) it must be `isk_migrations`. It also checks the table names against each module's declared descriptors (R6). The namespaces it treats as modules are the `Iskender.Domain` module names from the ISK-0001 module map, plus the test modules.
- This check is the adapter's own test, not an ISK-0001 rule, so the ISK-0001 contract does not change (SC-008).

## R22. Keeping the previous release working (FR-024)

**Decision**: The fixture defines "release 1" as Alpha 1 to 3 and Beta 1 to 3, with Alpha's range declared as `[1, 5]`, and a set of release-1 queries (insert, read, update, and delete through the fixture repositories). "Release 2" adds Alpha 4, which adds a nullable column. The test migrates to release 2, runs every release-1 query, and checks the probe still reports `ok` for release 1. A second test applies a fixture-only Alpha 6, beyond `Maximum`, and checks release 1 then reports `schema_ahead`.

## R23. Repository wiring

**Decision**:

- `Iskender.slnx` lists the new production and test projects. `.github/CODEOWNERS` gets entries for both.
- `tests/Iskender.Core.Contracts.Tests/SolutionBuildsTests.cs`'s `ContractSourcePrefixes` gains `src/Iskender.Domain/Ports/` and `src/Iskender.Infrastructure.PostgreSql/`, the same way ISK-0003 added `Registry/`.
- `Directory.Packages.props` gains `Npgsql` 10.0.3 and `Testcontainers.PostgreSql` 4.16.0, and every new or changed project commits its lock file (rules B1, B2).
- `check-license.sh` adds `PostgreSQL` to `allowed_licenses` (R1).
- `core-contracts` goes from 2 to 3, the 12 codes are added with `since: 3`, and the ISK-0002 contract's error codes and versioning tables are updated in the same commit, as ISK-0003 did.
- No host project changes: no `AddIskender` wiring, no migration command (SC-008).

## R24. The documentation (FR-037)

**Decision**: The persistence contract is published at `contracts/persistence.md`. `docs/03`'s Persistence section and `docs/04`'s Choices table link to it. `docs/04` gains a section, "Database migrations and rollback", which is the operator runbook: provisioning the two login roles and the group role, running migrations, the lock, reverse steps and the data-losing flag, backup before migration and restore when a reverse step cannot recover data, the health reason codes and what to do about each, the TLS meaning of `require`, and the supported PostgreSQL major version.
