# Feature Specification: ISK-0004 — Persistence Abstractions & PostgreSQL Foundation

**Feature Branch**: `feature/0004-persistence-abstractions-postgresql-foundation`

**Created**: 2026-11-09

**Status**: Draft

**Input**: User description: "Create the specification **ISK-0004 — Persistence Abstractions & PostgreSQL Foundation** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Create tenant-aware database connection lifecycle, migration framework and transactional repository contracts**. In-scope behaviors and artifacts: **PostgreSQL migrations; transactional boundaries; health; local Testcontainers; advisory concurrency where appropriate**. Explicitly exclude: **No full conversation schema, budget ledger or identity membership rules**. Prerequisites: **ISK-0001, ISK-0002**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Migration rollback/forward and parallel transaction integration tests pass**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify."

## Overview

The constitution is present at `.specify/memory/constitution.md` (version 1.1.0) and governs this spec.

Iskender has no persistence code yet. PostgreSQL is the approved durable source of truth (ADR-0006, Constitution II), and ISK-0001 reserved `Iskender.Domain.Ports` as the home of the persistence ports, with this spec as their signature owner, and `Iskender.Infrastructure.<Adapter>` as the only place a database driver may be referenced. ISK-0002 published the owner scope, the trusted scoped reference, `GatewayExecutionContext`, and the error code registry. ISK-0003 published the configuration section mechanism and secret reference placeholders. Every later module that stores data (organizations and projects, the resource registry, identity, API keys, secrets, conversations, usage, audit) needs the same foundation underneath it:

1. **Tenant-aware connection lifecycle**: every database access happens inside a unit of work opened for exactly one owner scope (an organization, a project, or the operator scope), with bounded connection, statement, and request-deadline timeouts, cancellation that reaches the server, and no session state that survives into another scope.
2. **Transactional repository contracts**: explicit transaction boundaries, isolation levels, optimistic concurrency, and a fixed classification of database failures into ISK-0002 error categories, so that a module repository never handles raw driver exceptions or decides retry on its own.
3. **Migration framework**: versioned, checksummed, module-owned schema migrations, each with a tested reverse step, applied by an explicit, serialized operation, never as a side effect of a data-plane replica starting.
4. **Health**: a persistence health probe with safe reason codes that tells a host whether the store is reachable and its schema is compatible, for ISK-0007's readiness.
5. **Advisory concurrency**: transaction-scoped advisory locks for short critical sections within one database, and the session lock that serializes migrators, without claiming the leases or fencing that ISK-0005 owns.
6. **Local Testcontainers fixtures**: a reusable PostgreSQL fixture that every later storage spec builds its integration tests on.

ISK-0005 builds coordination on top of this, ISK-0007 exposes the migration operation and readiness, ISK-0008 adds PostgreSQL to local Compose, ISK-0011, ISK-0012, ISK-0013 and later specs add their own tables through this migration framework, and ISK-0021 resolves secret references. This spec defines none of their tables or behavior.

Project, package, and type names appear in this spec because they are its subject, not a choice of implementation.

## Clarifications

### Session 2026-11-09

- Q: When this release finds migrations in the database that it doesn't know about, but they fall inside the schema range it says it supports, should the health probe report healthy? → A: Yes. Unknown migrations inside the declared supported range give `healthy` with `ok`, and only a schema outside the range gives `schema_ahead`. The migration runner still refuses to proceed while any unknown migration is present.
- Q: When several modules have pending migrations, in what order should the migration operation apply them? → A: Each module declares the modules it depends on. The runner applies modules in that dependency order, breaking ties by module name, and each module's migrations in version order. Reverse steps run in exactly the opposite order, and a dependency cycle refuses the set before anything is applied.
- Q: When an operator-scope unit of work queries a tenant-owned table outside an elevated operation, should it fail with an error, or quietly see zero rows? → A: It fails closed with a new code, `persistence.scope_not_permitted` (`permission_denied`), raised before the query is sent. Row-level security still returns zero rows as a backstop, and both layers are tested.
- Q: Where should each module's own repository code live, given that domain modules may not reference the database driver? → A: Repository interfaces live with each module in `Iskender.Domain`. Their PostgreSQL implementations live in `Iskender.Infrastructure.PostgreSql`, in one namespace per module, and an architecture test limits each module's namespace to its own module's schema.

## Decision Classification

### Accepted decisions (already approved; this spec applies them)

| Decision | Source |
|---|---|
| PostgreSQL is the durable source of truth for resource metadata, approvals, versioning, identity persistence, usage ledger, and gateway-managed state. Redis is optional, for hot and ephemeral coordination, and never weakens guarantees silently | ADR-0006, Constitution II, `docs/03` Persistence |
| Explicit modules with preserved dependency direction. Control plane and data plane are independently deployable processes that share approved persistence contracts | ADR-0003, Constitution II, `docs/03` Logical architecture |
| Architecture tests forbid cross-module database table updates and verify organization scopes on every query and event | `docs/03` Architectural tests |
| Organization → project → resource isolation is enforced at every persistence, query, worker, job, and stream boundary. Authorization takes the owner scope from persistent metadata, never from a caller-supplied value | ADR-0005, Constitution V, `docs/03` Resource hierarchy |
| Mandatory security checks fail closed and default to least privilege | Constitution V |
| Shared persistence ports live in `Iskender.Domain.Ports`, with ISK-0004 as signature owner. Only an `Iskender.Infrastructure.<Adapter>` project may reference a database driver or data-access library. `Iskender.Domain`, `Iskender.Hosting`, `Iskender.Management`, and the public contract packages may reference neither | ISK-0001 module dependency rules contract, Port homes and Denied packages |
| The owner scope has organization, project, and operator kinds, and an operator scope never stands in for a tenant scope. A trusted scoped reference cannot be built from caller-supplied scope values | ISK-0002 contract, FR-010, FR-011 |
| New gateway error codes go in `ErrorCodes` under the existing ISK-0002 categories. A new category needs a revision of the ISK-0002 contract | ISK-0002 contract, Changing this contract |
| A change to `Iskender.Core.Contracts`' public surface increments `core-contracts`, and an undeclared break fails the compatibility harness | ISK-0002 contract, Versioning policy |
| Module settings are declared as a namespaced section with a typed schema. A secret is never written into configuration: a secret field takes a `secretRef` placeholder, which ISK-0003 never resolves | ISK-0003 configuration contract |
| Liveness is separate from readiness. Readiness covers the durable store and exposes safe diagnostic reason codes | `docs/04` Deployment invariants |
| Schema upgrades are versioned and forward and backward compatible across a documented rolling-upgrade window, and the runbook states rollback and backup-restore expectations | `docs/04` Deployment invariants |
| Network links to PostgreSQL are protected, and the database is reached through constrained identities | `docs/04` Topology 2 and Deployment invariants |
| Integration tests use PostgreSQL through Testcontainers, including transactional conflict and rolling schema compatibility | `docs/12` Test matrix, `docs/13` Tests, Constitution VIII |
| Payload logging is off by default. Operational logs carry safe metadata only, with bounded cardinality | Constitution V and VIII, `docs/05` Logging |
| Migration and service contracts are cloud-neutral. No cloud SDK in domain or contract projects, and a local path builds without cloud credentials | `docs/03` Persistence, Constitution II |
| Libraries such as EF Core, Npgsql, and Dapper are candidates, not mandates. The exact mapper split and migration library are recommended, not approved, and are settled by this storage spec's tests | `docs/04` Choices and Questions reserved, `docs/16` Recommendations table, Constitution IX |
| A production package's license must be one of `MIT`, `Apache-2.0`, `BSD-2-Clause`, or `BSD-3-Clause`. A spec that adds another extends that list under Product/Charter owner review | `docs/04` CI and supply chain |
| No numeric performance figure is asserted without a reviewer-approved baseline | Constitution VIII |

### Technical recommendations (proposed by this spec; reviewed in `clarify`)

| ID | Recommendation | Why | Alternative rejected |
|---|---|---|---|
| TR-1 | The persistence ports (unit of work factory, unit of work, advisory lock, migration runner, health probe, and the failure classification they report) go in `Iskender.Domain.Ports`. The PostgreSQL implementation goes in a new embeddable adapter project, `Iskender.Infrastructure.PostgreSql`, the only project that references the driver and any data-access library. No port signature names a driver, data-access, or ASP.NET Core type. A later module's repository interfaces live with that module in `Iskender.Domain`, and their PostgreSQL implementations live in `Iskender.Infrastructure.PostgreSql`, one namespace per module (for example `Iskender.Infrastructure.PostgreSql.Registry`). An architecture test limits each module namespace to its own module's schema | This is where ISK-0001 already placed them, so the rules need no revision, and a module repository stays testable without a database | Ports in `Iskender.Infrastructure`, which `Iskender.Domain` cannot reach; ports in `Iskender.Core.Contracts`, which would make an internal storage seam a versioned public contract (`docs/04` says private storage models must not leak into public interfaces) |
| TR-2 | Every database access happens inside a **unit of work** opened with one explicit owner scope: an organization or project scope taken from a trusted scoped reference or the request's resolved owner scope, or the operator scope for system data. The scope is fixed for the unit's lifetime. There is no ambient, default, or "unscoped" unit, and a unit cannot be opened from caller-supplied scope values | A query that forgot its scope is the most likely cross-tenant leak. Making the scope a precondition of every access turns that bug into a compile-time or fail-closed error | An ambient scope read from a static or async-local value, which a background worker can inherit wrongly; an optional scope parameter |
| TR-3 | A tenant-owned table carries its owning organization ID, and its project ID when project-owned. A repository read or write in a tenant scope only ever sees or changes rows of that scope. A row of another scope is indistinguishable from a missing one (`not_found`), never `permission_denied`. Operator-scope units cannot read or write tenant-owned tables except through an explicitly named, audited elevated operation that a later spec defines. **Decided by the maintainer in `specify` on 2026-11-09:** isolation is enforced twice. The repository contracts and architecture tests put the owner predicate on every tenant-owned query, and PostgreSQL row-level security enforces it again: every tenant-owned table has row-level security enabled and forced, with policies keyed on a transaction-local owner scope setting that the unit of work sets when it opens. The runtime role neither owns the tables nor has the right to bypass row-level security. A tenant-owned table without a policy fails an automated check | Reporting "not found" for another tenant's row stops ID enumeration (`docs/05` negative tests). With a second, database-enforced layer, one missed predicate does not leak data | Returning `permission_denied` for foreign rows, which confirms the ID exists; application-level predicates alone, where one missed predicate leaks data |
| TR-4 | A unit of work has an explicit transaction: it commits only when the caller commits, and a unit that is disposed, cancelled, or faulted without a commit rolls back. The default isolation level is Read Committed. A caller may request Repeatable Read or Serializable for one unit. Nested transactions are not offered. One unit uses one connection for its whole life, and is used by one logical operation at a time | Explicit commit makes a forgotten commit lose data visibly in tests rather than commit half a change silently. Per-unit isolation lets the few operations that need serializability pay for it | Implicit auto-commit per statement; ambient `TransactionScope`, which hides boundaries and can escalate unexpectedly |
| TR-5 | Database failures are classified once, in the adapter, into a fixed set: **transient conflict** (serialization failure or deadlock), **concurrency conflict** (stale row version), **unique conflict**, **unavailable** (cannot connect, connection lost, server shutting down), **timeout**, **cancelled**, **authentication failed**, and **unexpected**. The adapter never retries on its own. A bounded retry helper re-runs a whole unit of work after a transient conflict only when the caller marks the unit as safe to re-run, at most 3 attempts in all, and then reports a concurrency conflict | Only the caller knows whether its work is safe to repeat. Retrying a single statement inside a failed transaction is never valid in PostgreSQL | Automatic retry of every failure, which can repeat side effects; leaving each repository to interpret driver error codes, which drifts |
| TR-6 | Optimistic concurrency is a primitive: a row may carry a version token, and an update or delete that names a stale token changes nothing and reports a concurrency conflict. ISK-0012 applies it to registry resources | A reusable primitive keeps every module's "stale revision" behavior the same, matching ISK-0002's `conflict` category | Last-writer-wins; pessimistic row locks held across requests |
| TR-7 | Connections come from a bounded pool. Any session setting a unit makes for its scope is transaction-local, so nothing set for one scope is visible to the next borrower of the connection. Acquiring a connection, each statement, and the whole unit are bounded: the effective limit is the shorter of the configured timeout and the remaining request deadline from `GatewayExecutionContext`. Cancellation of the request cancels the running statement on the server and rolls the unit back | Pooled connections are the classic route for state to leak across tenants. A deadline that the database ignores lets one slow query hold a request and a connection past its budget | Session-level settings reset on return, which fails open if the reset is skipped; client-side-only cancellation, which leaves the query running |
| TR-8 | Migrations are versioned SQL scripts. Each belongs to exactly one owning module, changes only that module's own PostgreSQL schema (one schema per module, named for it, such as `isk_registry`), and has a paired reverse script. A history table records each applied migration's module, version, SHA-256 checksum of its normalized text, the time applied, and the database role that applied it. Each module declares the modules whose schemas it depends on. The runner applies modules in that dependency order, ties broken by module name, and each module's migrations in version order. Reverse steps run in exactly the opposite order, and a dependency cycle refuses the set. A migration runs in its own transaction, and a failure leaves the history unchanged. An applied migration whose checksum no longer matches the shipped script stops the runner. The migration library is chosen in the plan, and must support all of this | Plain SQL is reviewable and does not tie the schema to a mapper the project has not approved. One schema per module makes "no cross-module table updates" checkable by an architecture test | Migrations generated from a mapper model, which couples schema to an unapproved library choice; one shared schema, where ownership is a naming convention only |
| TR-9 | Applying migrations is an explicit operation, which ISK-0007 exposes as a command or entry point. A data-plane process never applies migrations as a side effect of starting. The operation runs under a migration identity, separate from the runtime identity, and the runtime identity needs only data access, not schema changes. Concurrent migrators are serialized by a PostgreSQL session advisory lock on a fixed, documented key: a second migrator waits up to a configured bound and then fails without changing anything | Several replicas starting at once must not race to change the schema. A runtime identity that cannot change the schema limits the damage a compromised data plane can do (`docs/04` constrained identities) | Migrating on every process start; one identity with DDL rights for everything |
| TR-10 | The gateway release declares, per module, the range of schema versions it can run against. A migration must keep the previous release working (expand, then contract in a later release), so the rolling-upgrade window is one release. Applying reverse scripts steps the schema back one migration at a time. A reverse script that destroys data is marked as such, and the runner refuses to apply it without an explicit confirmation flag. The runbook covers backup before migration and restore when a reverse step cannot recover data | This is how `docs/04`'s forward and backward compatibility is made testable. Marking data-losing reverse steps stops an operator rolling back from deleting data by surprise | Forward-only migrations with restore-from-backup as the only rollback, which the acceptance evidence (rollback tests) rules out |
| TR-11 | The persistence health probe reports one status (`healthy` or `unhealthy`) and one reason code from a fixed set: `ok`, `unreachable`, `authentication_failed`, `timeout`, `schema_behind`, `schema_ahead`, `checksum_mismatch`, `migration_in_progress`. The probe has its own bounded timeout and runs one trivial query plus a read of the migration history. `schema_behind` and `schema_ahead` are judged against the release's declared supported range (TR-10): migrations the release does not know about, but which leave every module's schema inside that range, report `healthy` with `ok`, so the previous release stays ready during a rolling upgrade. Its output never contains a host, port, database or user name, connection text, SQL, or server error text. Liveness never calls it. ISK-0007 maps it onto readiness | Readiness needs to know both that the store answers and that this release can run against its schema. A fixed reason set is safe to expose and easy to alert on | Exposing the driver's error message, which can contain host names and user names |
| TR-12 | Advisory concurrency offers **transaction-scoped** advisory locks only to modules: a lock keyed by owner scope, owning module, and a name, released automatically at commit or rollback, acquired with a bounded wait or a try-only mode. The key is a stable 64-bit hash of those three parts, and the key derivation is documented so that two modules cannot collide by accident. It is documented as mutual exclusion inside one PostgreSQL database during one transaction, and not as a lease, a fencing token, or a distributed lock across stores, which ISK-0005 owns. The session-scoped lock is used only by the migration runner (TR-9) | A transaction-scoped lock cannot be leaked by a crashed caller, because PostgreSQL releases it with the transaction. Scoping the key by tenant and module stops one tenant's work from blocking another's | Session-scoped locks for modules, which leak with pooled connections; presenting advisory locks as a general distributed lock |
| TR-13 | The adapter declares an `iskender.persistence` configuration section under ISK-0003: host, port, database name, runtime username, runtime password (`secretRef`), TLS mode (default `require`; `disable` is development-only), pool minimum and maximum, connection timeout, statement timeout, health probe timeout, and a `migration` object with its own username, password (`secretRef`), and lock wait. No connection string field is offered, so no password can be embedded in one | Separate fields let ISK-0003 check each one and keep the password a secret reference. A connection-string field would carry the password as plain text | A single connection string |
| TR-14 | **Decided by the maintainer in `specify` on 2026-11-09:** ISK-0004 defines a narrow database-credential port in `Iskender.Domain.Ports`, which the host implements. The adapter passes it the `secretRef` reference from the configured field (runtime or migration) and receives the credential for one connection attempt, without caching it in configuration or logging it. ISK-0004 ships no implementation that reads secrets: tests supply the Testcontainers credential through a test implementation, and a production host cannot connect until ISK-0021 provides one. If no implementation is registered, opening a connection fails with `persistence.unavailable` and the probe reports `authentication_failed` | It keeps secret resolution entirely in ISK-0021 and adds no interim code that ISK-0021 would have to replace | A plaintext password field; an interim development-only resolver reading environment variables or files |
| TR-15 | Persistence error codes are added to `ErrorCodes` under existing categories: `persistence.unavailable` (`internal`), `persistence.timeout` (`deadline_exceeded`), `persistence.cancelled` (`cancelled`), `persistence.concurrency_conflict` (`conflict`), `persistence.unique_conflict` (`conflict`), `persistence.scope_required` (`internal`), `persistence.scope_not_permitted` (`permission_denied`), `persistence.lock_timeout` (`conflict`), `persistence.migration_locked` (`internal`), `persistence.migration_checksum_mismatch` (`internal`), `persistence.schema_incompatible` (`internal`), and `persistence.reverse_requires_confirmation` (`invalid_request`). Each carries a safe message with no database detail. This raises `core-contracts` by one, with compatible changes only | No new category is needed, so the ISK-0002 contract needs no revision. A storage outage is not caused by the caller, so it is `internal` rather than an upstream category, which ISK-0002 reserves for inference backends | A new `storage_unavailable` category, which needs an ISK-0002 contract revision; leaking driver error codes |
| TR-16 | Diagnostics use fixed, template-based log events with stable event IDs for: connection failures by class, unit-of-work rollback by reason, transient-conflict retries, advisory lock timeouts, migration start, each migration step, completion, failure, and lock waits, and health status changes. Each unit of work and each migration step is an OpenTelemetry activity. Metrics cover pool usage, connection acquire wait, unit-of-work duration and outcome, and conflict and retry counts. Labels are limited to module, operation kind, outcome, and failure class. No label or log field holds an owner ID, SQL text, parameter values, or connection details | Operators must be able to diagnose a failed migration or a slow store from logs and traces alone, and bounded labels keep metric cardinality and privacy safe | Logging SQL with parameters; tenant IDs as metric labels |
| TR-17 | Integration tests use a shared Testcontainers PostgreSQL fixture: one container per test run, from an image pinned by digest, with an isolated database per test class created from a migrated template so tests can run in parallel. Fixture migrations for a test-only sample module, never shipped, exercise the framework. **Decided by the maintainer in `specify` on 2026-11-09:** on a machine without a container runtime, the container-backed tests are reported as skipped, each with the reason, and the rest of the suite runs. In CI, a missing container runtime fails the run, so these tests are never skipped there | One container per run keeps the suite fast enough to run on every change. Per-class databases give isolation without a container per test | A container per test; a shared database with cleanup between tests, which lets one test's leftovers break another |

### Assumptions needing validation

| ID | Assumption | Validated by |
|---|---|---|
| AV-1 | The PostgreSQL driver the plan selects declares the SPDX expression `PostgreSQL`, which is not on the `check-license.sh` list. Adding it requires extending the list under Product/Charter owner review, as `docs/04` allows. Test-only packages such as Testcontainers are outside that check, which reads only `src/` projects | This spec's plan, Product/Charter owner |
| AV-2 | ISK-0007 can expose the migration operation (TR-9) as a separate command or entry point and map the health probe (TR-11) onto readiness, without this spec adding host wiring | ISK-0007 |
| AV-3 | ISK-0005 can build its coordination ports beside these without needing a session-scoped lock from this spec | ISK-0005 |
| AV-4 | ISK-0011 and ISK-0012 can create organization, project, and registry tables through this migration framework and repository contracts, and the scope rules in TR-2 and TR-3 are enough for their ownership checks | ISK-0011, ISK-0012 |
| AV-5 | ISK-0009's module events (for example an outbox) can be written inside the same unit of work as the data change, using only the contracts defined here | ISK-0009 |
| AV-6 | One supported PostgreSQL major version, pinned in the test fixture, is enough for MVP tests, and the oldest major version supported is recorded in the plan and the runbook. The Azure reference topology offers that version | This spec's plan, ISK-0085 |
| AV-7 | The CI runner provides a container runtime for Testcontainers, so the PostgreSQL integration tests run in the existing `dotnet test` check without a new CI check | This spec's plan |

### Post-MVP work (named so that it is not designed here)

Read replicas and read routing. Partitioning, sharding, or a database per tenant. Multiple PostgreSQL databases per deployment. Online schema-change tooling for very large tables. Automated backup scheduling and point-in-time restore orchestration. Column-level encryption (ISK-0021 owns encryption design). Savepoints and nested transactions. A management API view of migration history.

## User Scenarios & Testing *(mandatory)*

The users of this feature are module authors in later specs, operators who deploy and upgrade Iskender, and reviewers who need evidence of isolation and safe upgrades.

### User Story 1 - Schema changes can be applied, rolled back, and re-applied safely (Priority: P1)

An operator upgrades Iskender. They run the migration operation once, it applies every pending migration in order and records them. If the release must be rolled back, they apply the reverse steps, and the schema returns to exactly the previous version. Running the forward migrations again gives the same schema as before.

**Why this priority**: This is half of the acceptance evidence. A schema change that cannot be undone, or that leaves a different schema after a round trip, turns every upgrade into a risk of data loss or outage.

**Independent Test**: Against an empty database, apply all fixture migrations, capture the schema, reverse them all, check the database is back to empty, apply them again, and compare the schema with the first capture.

**Acceptance Scenarios**:

1. **Given** an empty database and a set of fixture migrations across two modules, one depending on the other, **When** the migration operation runs, **Then** every migration is applied in module dependency order and then version order, each is recorded with its module, version, checksum, time, and role, and the schema matches the expected catalog snapshot.
2. **Given** a fully migrated database, **When** the reverse steps are applied one at a time, **Then** after each step the schema matches the snapshot taken after the corresponding forward step, and after the last the module schemas are empty.
3. **Given** a database that was migrated forward, reversed, and migrated forward again, **When** its schema is compared with the first forward run, **Then** they are identical.
4. **Given** a migration whose forward script fails partway, **When** the operation runs, **Then** that migration's changes are rolled back, it is not recorded as applied, earlier migrations stay applied, and the failure names the failing migration's module and version with a safe reason, never its SQL or the server's error text.
5. **Given** an applied migration whose shipped script has since been edited, **When** the operation or the health probe runs, **Then** it stops with `persistence.migration_checksum_mismatch` or `checksum_mismatch`, naming the module and version, and changes nothing.
6. **Given** a reverse step marked as data-losing, **When** an operator applies it without the confirmation flag, **Then** it is refused with `persistence.reverse_requires_confirmation`, and nothing changes.

---

### User Story 2 - Concurrent work never corrupts data or deadlocks silently (Priority: P1)

Several replicas, or several requests in one replica, change the same data at the same time. Each change either commits completely or not at all. Stale updates are rejected rather than overwriting newer data, serialization failures are retried only when the caller said it is safe, and two migrators started at once never apply a migration twice.

**Why this priority**: This is the other half of the acceptance evidence. Distributed governance, registries, and durable conversations all depend on these guarantees (Constitution VII, `docs/12` two-replica tests).

**Independent Test**: Run many concurrent units of work against one fixture row and one advisory lock key, and check that the committed result matches exactly one serial order, with every loser reported by the expected code.

**Acceptance Scenarios**:

1. **Given** 20 concurrent units of work that each read a row with version token V and update it, **When** they commit, **Then** exactly one succeeds, the other 19 report `persistence.concurrency_conflict`, and the row holds the winner's value with a new version.
2. **Given** 20 concurrent serializable units that each increment one counter row and are marked safe to re-run, **When** they all complete, **Then** the counter equals 20 plus the number that exhausted their retries reported as `persistence.concurrency_conflict`, no increment is lost or doubled, and no unit was retried more than 3 attempts in all.
3. **Given** the same serializable workload marked not safe to re-run, **When** a unit hits a serialization failure, **Then** it is not retried and reports `persistence.concurrency_conflict`.
4. **Given** 10 concurrent units that take the same transaction-scoped advisory lock key and append to a log table inside it, **When** they finish, **Then** the appended entries never interleave, and each lock was released at its unit's commit or rollback.
5. **Given** two units in different organization scopes using the same lock name in the same module, **When** both take the lock at once, **Then** neither waits for the other.
6. **Given** two migration operations started at the same moment against one database, **When** both finish, **Then** each pending migration was applied exactly once, and the second either found nothing to do or failed with `persistence.migration_locked` after its configured wait.
7. **Given** a unit of work that is disposed without a commit, or whose request is cancelled mid-statement, **When** its transaction ends, **Then** none of its changes are visible to any other unit, and the server-side statement was cancelled.

---

### User Story 3 - Data stays inside its owner scope (Priority: P1)

A module author writes a repository for a tenant-owned table. Whatever query they write, a unit of work opened for one organization or project can never read, change, or learn of another scope's rows, and a background worker cannot reach tenant data without a scope.

**Why this priority**: Owner isolation is a non-bypassable platform invariant (Constitution V, ADR-0005), and this is the boundary every later storage spec builds on.

**Independent Test**: Seed fixture rows for two organizations and two projects, then try every read, update, delete, and reference-by-ID from the other scope, and check each gets `not_found` or zero rows affected.

**Acceptance Scenarios**:

1. **Given** fixture rows owned by organizations A and B, **When** a unit of work in scope A reads by B's row ID, **Then** the result is `core.resource.not_found`, the same as for an ID that does not exist.
2. **Given** a unit in project scope A/P1, **When** it updates or deletes a row of project A/P2 by ID, **Then** zero rows change and the result is not found.
3. **Given** code that tries to open a unit of work without a scope, or with an owner scope built from a caller-supplied value, **When** it compiles or runs, **Then** it is impossible to express, or fails with `persistence.scope_required`, before any connection is opened.
4. **Given** a unit in the operator scope, **When** it queries a tenant-owned fixture table outside an elevated operation, **Then** it fails with `persistence.scope_not_permitted` before any statement reaches the database, and with that check bypassed in a test, row-level security returns zero rows and changes zero rows.
5. **Given** a connection that served a unit in scope A, **When** the pool hands it to a unit in scope B, **Then** no setting, temporary table, prepared state, or lock from scope A is visible.

---

### User Story 4 - Operators can tell whether the store is ready, and why not (Priority: P2)

An operator deploys a release against a database that is down, has the wrong password, is behind on migrations, or is ahead of this release. The health probe says which, in a reason code safe to show on a dashboard, without leaking connection details.

**Why this priority**: Readiness needs it (ISK-0007), and an operator without a reason code cannot recover an outage quickly. It depends on Stories 1 and 3.

**Independent Test**: Run the probe against a stopped container, a wrong password, a database missing the latest migration, a database whose schema is beyond the release's declared supported range, and a migrating database, and check each reason code and that none of the output holds the host, user, database name, or server error text.

**Acceptance Scenarios**:

1. **Given** a reachable, fully migrated database, **When** the probe runs, **Then** it reports `healthy` with `ok`.
2. **Given** each of the failure conditions above, **When** the probe runs, **Then** it reports `unhealthy` with `unreachable`, `authentication_failed`, `schema_behind`, `schema_ahead`, or `migration_in_progress` respectively, within its configured timeout.
3. **Given** a database that never answers, **When** the probe runs, **Then** it returns `timeout` within its timeout plus a fixed allowance, rather than hanging.
4. **Given** any probe result, log line, metric, or trace from these runs, **When** it is searched for the planted host, database name, user name, and password, **Then** none is found.
5. **Given** a database with a migration this release does not know about, which keeps the module's schema inside the release's declared supported range, **When** the probe runs, **Then** it reports `healthy` with `ok`, and the migration operation still refuses to proceed.

---

### User Story 5 - Later specs get a ready-made PostgreSQL test fixture (Priority: P2)

A later spec's author writes integration tests for a new table. They use the shared fixture, which starts PostgreSQL in a container once, gives each test class its own migrated database, and works the same on a laptop and in CI.

**Why this priority**: Every later storage spec needs it, and it is the vehicle for this spec's own acceptance tests.

**Independent Test**: Run two test classes in parallel that each write conflicting data with the same keys, and check neither sees the other's data and both pass.

**Acceptance Scenarios**:

1. **Given** two test classes that run in parallel, **When** both insert rows with identical keys, **Then** both pass, and neither sees the other's rows.
2. **Given** the fixture's image reference, **When** it is read, **Then** it is pinned by digest, not by a moving tag.
3. **Given** a machine with a container runtime and restored packages, **When** the test suite runs, **Then** the fixture needs no cloud credentials and no network access other than pulling the pinned image once.

---

### Edge Cases

- The database is reachable but the runtime role lacks rights on a module schema: the unit fails as unexpected with a safe message, and the probe reports `unhealthy`, never `ok`.
- The pool is exhausted: acquiring a connection waits no longer than the shorter of the configured timeout and the request deadline, then reports `persistence.timeout`.
- A request's deadline has already passed when a unit is opened: no connection is acquired, and the result is `persistence.timeout`.
- The connection drops after the commit is sent but before it is acknowledged: the outcome is reported as `persistence.unavailable`, never as success or as a definite rollback, and the retry helper does not re-run the unit automatically.
- Two migrations in the same module claim the same version: the runner refuses the set before applying anything. The same version in different modules is allowed, because the global order comes from module dependencies.
- Module dependencies form a cycle: the runner refuses the set before applying anything.
- A migration script touches another module's schema: an architecture or build-time check fails before it ships.
- A repository implementation in one module's namespace queries another module's schema: the architecture test fails.
- A migration in the database is unknown to the release (a newer release ran): the runner refuses to proceed. The probe reports `healthy` with `ok` while every module's schema is still inside the release's declared supported range, and `schema_ahead` once any module's schema is beyond it.
- The migration lock holder crashes: PostgreSQL releases the session lock with the connection, and the next migrator proceeds.
- A reverse script is missing for a migration: the set is refused before anything is applied.
- A unit of work is used from two concurrent operations: the second use fails rather than interleaving on one connection.
- The TLS mode is `disable` outside `development`: configuration validation rejects it under ISK-0003.
- A transient conflict happens on the last allowed attempt: the unit reports `persistence.concurrency_conflict` and is not retried again.

## Requirements *(mandatory)*

### Functional Requirements

**Ports and ownership**

- **FR-001**: The persistence ports MUST be defined in `Iskender.Domain.Ports`, and MUST NOT expose driver, data-access library, or ASP.NET Core types in any signature.
- **FR-002**: The PostgreSQL implementation MUST live in `Iskender.Infrastructure.PostgreSql`, which MUST be the only production project referencing the PostgreSQL driver or a data-access library. All ISK-0001 architecture rules MUST pass unchanged.
- **FR-002a**: A module's repository interfaces MUST live with that module in `Iskender.Domain`, and their PostgreSQL implementations MUST live in `Iskender.Infrastructure.PostgreSql` under one namespace per module. An architecture test MUST fail when code in one module's namespace reads or writes another module's schema, This spec's test-only sample module mirrors that layout inside test projects and ships in no production package (FR-034).
- **FR-003**: Every port and adapter type MUST document its owning spec, and the ports MUST be usable with a test double so a module repository can be unit-tested without a database.

**Connection lifecycle and scope**

- **FR-004**: Every database access MUST happen inside a unit of work opened with exactly one owner scope (organization, project, or operator), fixed for its lifetime.
- **FR-005**: It MUST NOT be possible to open a unit of work without a scope, or with an owner scope built from caller-supplied values; any such attempt that reaches run time MUST fail with `persistence.scope_required` before a connection is opened.
- **FR-006**: In a tenant scope, every repository read and write on a tenant-owned table MUST be limited to rows of that scope, and a row of another scope MUST be reported exactly as a missing row.
- **FR-007**: An operator-scope unit MUST NOT read or write tenant-owned tables except through a named elevated operation, which this spec reserves and does not define. Such an attempt MUST fail with `persistence.scope_not_permitted` before the statement is sent to the database, and row-level security MUST still return zero rows and change zero rows if that check is bypassed.
- **FR-008**: No session-level state set for one scope MUST be visible to a later unit that reuses the same pooled connection.
- **FR-009**: Connection acquisition, each statement, and the unit as a whole MUST each be bounded by the shorter of its configured timeout and the remaining request deadline, and request cancellation MUST cancel the running statement on the server and roll the unit back.
- **FR-010**: Every tenant-owned table MUST have PostgreSQL row-level security enabled and forced, with a policy keyed on the unit of work's transaction-local owner scope, in addition to the repository's own owner predicate. The runtime role MUST NOT own tenant-owned tables or be able to bypass row-level security. An automated check MUST fail when a tenant-owned table lacks a policy, and the isolation tests in SC-004 MUST pass both with the repository predicate and with it deliberately omitted.

**Transactions and failures**

- **FR-011**: A unit of work MUST commit only on an explicit commit, and MUST roll back when disposed, cancelled, or faulted without one.
- **FR-012**: A unit MUST default to Read Committed and MUST allow Repeatable Read or Serializable to be requested per unit. Nested transactions MUST NOT be offered.
- **FR-013**: The adapter MUST classify every database failure into the classes in TR-5 and report it with the matching code from FR-030. Raw driver exceptions MUST NOT cross the port.
- **FR-014**: The adapter MUST NOT retry anything itself. The retry helper MUST re-run a whole unit only after a transient conflict, only when the caller marked it safe to re-run, and at most 3 attempts in all.
- **FR-015**: An unknown commit outcome (connection lost during commit) MUST be reported as `persistence.unavailable` and MUST NOT be retried automatically.
- **FR-016**: The optimistic concurrency primitive MUST make an update or delete with a stale version token change nothing and report `persistence.concurrency_conflict`.

**Migrations**

- **FR-017**: Each migration MUST declare one owning module and a version, MUST have a reverse script, and MUST change only its module's own schema. Each module MUST declare the modules it depends on. The runner MUST apply modules in dependency order, ties broken by module name, each module's migrations in version order, and reverse steps in exactly the opposite order. A set with a missing reverse script, a duplicate version within a module, a script touching another module's schema, or a module dependency cycle MUST be refused before anything is applied.
- **FR-018**: The migration history MUST record, per applied migration, module, version, SHA-256 checksum of its normalized text, time applied, and database role.
- **FR-019**: Each migration MUST run in its own transaction, and a failed migration MUST leave neither its changes nor a history record.
- **FR-020**: A checksum mismatch, or an applied migration unknown to the release, MUST stop the runner before it changes anything.
- **FR-021**: Migration MUST be an explicit operation that is never run by a data-plane process starting. It MUST be serialized across processes by a session advisory lock on a documented fixed key, and a migrator that cannot get the lock within its configured wait MUST fail with `persistence.migration_locked` without changing anything.
- **FR-022**: The migration operation MUST use the migration credentials, and the runtime credentials MUST need no right to change any schema.
- **FR-023**: Reverse steps MUST be applicable one migration at a time, and a reverse step marked data-losing MUST be refused with `persistence.reverse_requires_confirmation` unless explicitly confirmed.
- **FR-024**: The release MUST declare, per module, the range of schema versions it supports. A migration MUST keep the previous release working against the new schema, and the fixture suite MUST include a test that runs the previous fixture release's queries against the new schema.

**Health**

- **FR-025**: The health probe MUST report `healthy` or `unhealthy` with one reason code from TR-11, within its configured timeout plus a fixed allowance that the plan documents as a safety bound, not a performance figure. It MUST report `schema_behind` or `schema_ahead` only when a module's schema is outside the release's declared supported range (FR-024), and MUST report `healthy` with `ok` when unknown applied migrations leave every module inside that range.
- **FR-026**: Probe output, and every persistence log, metric, trace, and error message, MUST NOT contain a host, port, database name, user name, password, connection text, SQL text, parameter value, or server error text.

**Advisory concurrency**

- **FR-027**: Modules MUST be offered only transaction-scoped advisory locks, keyed by owner scope, owning module, and name through a documented stable 64-bit derivation, released at commit or rollback, with a bounded wait or try-only acquisition. A wait that expires MUST report `persistence.lock_timeout`.
- **FR-028**: Advisory locks MUST be documented as mutual exclusion inside one database transaction only, and MUST NOT be described or used as leases or fencing tokens.

**Configuration, errors, and diagnostics**

- **FR-029**: The adapter MUST declare the `iskender.persistence` section from TR-13 under ISK-0003, with both passwords as `secretRef` fields, TLS defaulting to `require`, `disable` marked development-only, and no connection string field.
- **FR-030**: The error codes in TR-15 MUST be registered in `ErrorCodes` under the stated existing categories, each with a safe message holding no database detail, and the compatibility harness MUST report only compatible changes.
- **FR-031**: The adapter MUST obtain each database credential through the host-implemented database-credential port in TR-14, passing the configured `secretRef` reference. ISK-0004 MUST ship no implementation that reads secrets outside test projects. With no implementation registered, a connection attempt MUST fail with `persistence.unavailable` and the probe MUST report `authentication_failed`. No password MUST appear in configuration, logs, traces, metrics, or exception messages.
- **FR-032**: The adapter MUST emit the log events, activities, and metrics in TR-16, with labels limited to module, operation kind, outcome, and failure class.

**Fixtures, tests, and documentation**

- **FR-033**: A shared PostgreSQL Testcontainers fixture MUST start one container per test run from an image pinned by digest, and give each test class an isolated database created from a migrated template.
- **FR-034**: Test-only fixture migrations for a sample module MUST exist and MUST NOT ship in any production package.
- **FR-035**: Without a container runtime, container-backed tests MUST be reported as skipped, each with its reason, while the rest of the suite runs. In CI, a missing container runtime MUST fail the run.
- **FR-036**: The integration suite MUST include the migration rollback-and-forward tests of User Story 1 and the parallel transaction tests of User Story 2. Both are this spec's acceptance evidence.
- **FR-037**: Documentation MUST be updated in the same PR: `docs/03` and `docs/04` link to this spec's persistence contract, and an operator runbook section covers running migrations, rollback, data-losing reverse steps, backup before migration, and the health reason codes.

### Negative Security and Failure Scenarios

| Scenario | Expected outcome | Covered by |
|---|---|---|
| A repository query forgets its owner predicate | Row-level security still limits it to the unit's scope, and the cross-scope tests run with the predicate omitted | FR-006, FR-010, SC-004 |
| A new tenant-owned table ships without a row-level security policy | The automated policy check fails | FR-010 |
| The runtime role bypasses row-level security as table owner | The runtime role owns no tenant-owned table and cannot bypass it | FR-010, FR-022 |
| An attacker enumerates another tenant's row IDs | Foreign rows are reported exactly as missing ones | FR-006, User Story 3 |
| A foreign key points at another tenant's row | Reference checks run inside the caller's scope, so the target is not found | FR-006, SC-004 |
| A background worker reads tenant data with no scope | It cannot open a unit without a scope | FR-004, FR-005 |
| The operator scope is used to bypass tenant boundaries | Refused with `persistence.scope_not_permitted` outside a named elevated operation, and row-level security returns zero rows if that check is bypassed | FR-007, FR-010 |
| Scope state leaks through a pooled connection | Scope state is transaction-local | FR-008, User Story 3 |
| A compromised data plane alters or drops schema | The runtime identity has no schema rights | FR-022 |
| A tampered migration script is shipped after being applied | Checksum mismatch stops the runner and the probe | FR-020, User Story 1 |
| Two replicas migrate at once | Serialized by the migration lock | FR-021, User Story 2 |
| A rollback silently deletes data | Data-losing reverse steps need confirmation | FR-023 |
| A password leaks through configuration, logs, or errors | `secretRef` only, and the canary test checks every output | FR-026, FR-029, FR-031, SC-005 |
| Connection text or server errors leak host and user names | Fixed safe messages and reason codes | FR-026, SC-005 |
| A plaintext link to the database | TLS defaults to `require`, and `disable` is development-only | FR-029 |
| A slow query holds a connection past the request deadline | Server-side cancellation at the deadline | FR-009, Edge Cases |
| Pool exhaustion stalls every request | Bounded acquire wait, then `persistence.timeout` | FR-009 |
| A retried unit repeats a side effect | Only units marked safe are retried | FR-014, FR-015 |
| A crashed lock holder blocks everyone | Transaction-scoped locks release with the transaction | FR-027 |
| One tenant's lock blocks another tenant | Lock keys include the owner scope | FR-027, User Story 2 |
| An advisory lock is trusted as a distributed lease | Documented as transaction-local only | FR-028 |
| A newer schema meets an older release | The runner refuses. The probe stays `ok` inside the declared supported range, and reports `schema_ahead` beyond it | FR-020, FR-024, FR-025 |

### Key Entities

- **Owner scope**: ISK-0002's organization, project, or operator scope, fixed for a unit of work.
- **Unit of work**: One scoped transaction on one connection, with its isolation level, deadline, and commit or rollback outcome.
- **Persistence failure class**: Transient conflict, concurrency conflict, unique conflict, unavailable, timeout, cancelled, authentication failed, or unexpected, each mapped to one error code.
- **Version token**: The optimistic concurrency marker on a row.
- **Advisory lock key**: The 64-bit key derived from owner scope, module, and name.
- **Migration**: A module-owned, versioned forward script with its reverse script, checksum, and data-losing flag.
- **Module migration dependency**: A module's declared list of modules whose schemas it depends on, which fixes the global apply and reverse order.
- **Migration history record**: Module, version, checksum, time applied, and role.
- **Supported schema range**: Per module, the schema versions a release can run against.
- **Persistence health result**: Status and reason code.
- **Persistence settings**: The `iskender.persistence` section, including the separate migration credentials.
- **PostgreSQL test fixture**: The shared container, template database, and per-class databases.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a machine with a container runtime, with packages restored and no cloud credentials, the format check, build, and tests pass with zero warnings, and all 10 CI checks pass. Without a container runtime, the same commands pass with every container-backed test reported as skipped with its reason, and 0 other tests skipped. In CI, 0 container-backed tests are skipped.
- **SC-002**: The migration round trip (forward, reverse step by step, forward again) passes for a fixture set of at least 6 migrations across at least 2 modules, including one data-losing reverse step, with the schema after each step equal to its expected snapshot and the final forward schema identical to the first.
- **SC-003**: Each parallel test in User Story 2 passes on 20 consecutive runs, with 0 lost or doubled updates, exactly one winner per optimistic conflict, 0 interleaved entries under an advisory lock, and 0 migrations applied twice.
- **SC-004**: For every fixture repository operation (read, list, update, delete, and reference by ID), across two organizations and two projects, 100% of cross-scope attempts return not found or change 0 rows, and 0 return another scope's data. This holds both with the repository's owner predicate and with it deliberately omitted, so row-level security alone is shown to hold.
- **SC-005**: With a canary value planted as the host, database name, user names, and both passwords, the canary appears 0 times in probe results, error messages, captured logs, metric labels, and trace attributes across the whole persistence test suite.
- **SC-006**: Each of the 8 health reason codes is produced by at least one test, and each unhealthy test returns within its configured timeout plus the fixed allowance.
- **SC-007**: Each of the 12 error codes in TR-15 is produced by at least one test, and the compatibility harness reports `core-contracts` raised by one with only compatible changes, 0 undeclared breaks, and 0 changes to the other two contract packages.
- **SC-008**: The PR changes 0 tracked files under `.specify/`, 0 in `.iskender/grounding.yaml`, and 0 in the ISK-0001 rules contract. It adds 0 organization, project, identity, membership, conversation, usage, or budget tables, 0 host wiring, and 0 cloud SDK references.

## Assumptions

- ISK-0001, ISK-0002, and ISK-0003 are merged and complete on `main`. Their contracts, the owner scope, trusted scoped reference, `GatewayExecutionContext`, `ErrorCodes`, the compatibility harness, and the configuration section mechanism exist as their contracts describe.
- The tables this spec creates are test-only fixture tables and the migration history table. Every real module table is created by the spec that owns it, through this framework.
- "Tenant-aware" means scoped by ISK-0002 owner scope. Who may act in a scope (membership, roles, permissions) is decided upstream of persistence by the identity and RBAC specs; this spec only guarantees that a unit stays inside the scope it was given.
- The elevated operator operation over tenant data is reserved here and defined by the spec that first needs it, with audit.
- The gateway connects to one PostgreSQL database per deployment, with module schemas inside it.
- The rolling-upgrade window of one release is a compatibility rule, not a performance claim. No latency, throughput, or pool-size figure is asserted, as Constitution VIII requires. Timeouts and pool sizes are configurable safety bounds.
- Any interaction with durable conversations, financial controls, policy plans, plugin trust, or streaming goes through their own specs, which use these contracts. This spec defines none of their behavior.

## Out of Scope

- The conversation schema and conversation store (ISK-0044, ISK-0045), the usage ledger and budget reservation (ISK-0062, ISK-0063), and identity membership rules (ISK-0013 to ISK-0015).
- Organization, project, and registry tables and their ownership logic (ISK-0011, ISK-0012).
- Redis, leases, fencing, and distributed coordination (ISK-0005).
- Host composition, the migration command or entry point, and readiness endpoints (ISK-0006, ISK-0007). Local Compose (ISK-0008). Module events and outbox delivery (ISK-0009).
- Secret resolution, encryption, and key management (ISK-0021). ISK-0004 defines only the database-credential port in TR-14.
- Backups, point-in-time restore, and cloud database provisioning, beyond the runbook's expectations.
- Changing the constitution, the roadmap, ADRs, the ISK-0001 rules contract, or `.iskender/grounding.yaml`. Any roadmap consequence goes through `/review-roadmap`.
