# Research: ISK-0010 — System-Operator Bootstrap & Security Baseline

Each decision has its reason and the alternatives rejected. The maintainer took four of them in `plan` on 2026-11-21, recorded in the spec's "Maintainer decisions taken in `plan`" table: the audit schema (R2), the shipped protected route (R7), the admin paths (R7), and unencrypted keys only (R13). The rest follow from the spec, the contracts, and the code, and the refinements that leave no choice are marked as such.

## R1. Where the code lives

**Decision**: No production project is added. The work sits in five places:

| Place | Holds |
|---|---|
| `Iskender.Domain.Identity` | The transport-neutral ports and types of the Identity/Authorization module: installation state, operator tokens, redemption, enrollment, operator actions and roles, and the authorization decision |
| `Iskender.Domain.Telemetry` | The audit-recording port and its record types (Telemetry/Audit module) |
| `Iskender.Infrastructure.PostgreSql.Identity` | The `identity` migration module, schema `isk_identity`, and the PostgreSQL stores behind the Identity ports (persistence Ownership rule 3) |
| `Iskender.Infrastructure.PostgreSql.Audit` | A new framework namespace, the `audit` migration module, schema `isk_audit`, and the PostgreSQL audit recorder (R2) |
| `Iskender.Management`, namespaces `Iskender.Management.Security` and `Iskender.Management.Bootstrap` | `AddIskenderOperatorSecurity()`, `MapIskenderOperatorSecurity(...)`, the admin gate, route declarations, the principal-resolution port, the redemption and installation endpoints, the `operator` section, and the `bootstrap` command's library |

`Iskender.Infrastructure.Container` gains the transport modes, the TLS listener, three exit codes, and the `Iskender.Identity` subscription. `Iskender.ControlPlane.Host` maps the admin routes on its traffic listener and adds the `bootstrap` command. The adapter gains one registration, `AddIskenderPostgreSqlIdentity()`, which adds the stores, the audit recorder, and both migration modules.

`Iskender.Management` takes `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. Its denied categories in the module dependency rules contract are `cloud`, `provider`, `driver`, and `data-access`, not `aspnetcore`, so rule A19 allows it, and rule A3 still holds because it references only `Iskender.Domain` and `Iskender.Core.Contracts`. **This validates the spec's AV-1 with no edge, home, role, or rule change, so the module dependency rules contract stays at version 5** and FR-039's "where TR-12 needs it" does not apply. Both module namespaces already exist in its map, and neither uses the other: the Identity ports name no Telemetry type, and only the PostgreSQL adapter, which rule A17 does not cover, calls both.

**Rationale**: Identity's ports stay free of ASP.NET Core, as the rules contract's AV-1 for ISK-0013 expects, and the HTTP surface sits in the one assembly the control plane alone composes, so rule A6 keeps it out of the data plane.

**Alternatives rejected**: A new `Iskender.Identity` project, which needs a rules revision and adds nothing a namespace does not. Putting the admin endpoints in `Iskender.Hosting`, which is the data plane's and a public contract. Putting the principal-resolution port in `Iskender.Domain.Identity`, which cannot take `HttpContext` (rule A19 denies `aspnetcore` to `Iskender.Domain`).

## R2. The audit schema (decided by the maintainer in `plan`)

**Decision**: Keep migration module `audit` and schema `isk_audit`, as TR-13 and FR-026 name them. Persistence contract version 4 makes `Iskender.Infrastructure.PostgreSql.Audit` a framework namespace that names only `isk_audit`, as ISK-0009 did for `Directory` and `Work`. Its one table, `isk_audit.records`, is an operator table.

**Rationale**: Persistence Ownership rules 3 and 4 would put the Telemetry/Audit module's code in `Iskender.Infrastructure.PostgreSql.Telemetry` and its tables in `isk_telemetry`, which names the wrong half of the module for a store that holds audit records only. Tenant audit (ISK-0026) will extend the same namespace.

**Alternatives rejected**: `isk_telemetry`, which follows the rule mechanically but misnames the store, and needed a spec edit. The maintainer chose to keep the spec's names.

## R3. Installation state

**Decision**: `isk_identity.installation`, an operator table of at most one row:

```sql
singleton boolean primary key default true check (singleton),
state text not null check (state = 'initialized'),
initialized_at timestamptz not null,
initialized_by text not null  -- the first operator principal's ID
```

No row is `uninitialized`. Only the bootstrap redemption inserts the row, and nothing updates or deletes it. The migration revokes `UPDATE`, `DELETE`, and `TRUNCATE` on it from `iskender_runtime`, so even a bug in the runtime cannot move the state back (FR-002). `IInstallationState.ReadAsync` returns `Uninitialized` or `Initialized(at)` in an operator-scope unit, and throws the persistence failure otherwise.

**Rationale**: A missing row needs no seed, so a fresh migration is `uninitialized` with nothing to insert. The single-row primary key makes the bootstrap's insert the serialization point of R5.

**Alternatives rejected**: A row seeded by the migration and updated in place, which needs `UPDATE`, so the runtime could also move it back.

## R4. Token form, hashing, and storage

**Decision**: A token is the text `isk<p>_<id>_<secret>`. `<p>` is `b` (bootstrap), `r` (recovery), or `e` (enrollment). `<id>` is 16 random bytes in lowercase Crockford base32, 26 characters, and `<secret>` is 32 random bytes from `RandomNumberGenerator` in unpadded base64url, 43 characters. A text of any other shape is malformed and is never looked up. The prefix only helps an operator tell tokens apart. The stored purpose, not the prefix, decides.

`isk_identity.tokens` is an operator table:

| Column | Type | Notes |
|---|---|---|
| `token_id` | `text` primary key | `<id>` |
| `purpose` | `text not null` | `bootstrap`, `recovery`, or `enrollment` |
| `secret_hash` | `bytea not null` | 32 bytes |
| `principal_id` | `text` | The bound principal, for `enrollment` only |
| `issued_at`, `expires_at` | `timestamptz not null` | From the database's clock |
| `consumed_at`, `superseded_at` | `timestamptz` | `NULL` until set |

A partial unique index on `(purpose)` where `purpose in ('bootstrap', 'recovery')` and both markers are `NULL` makes "at most one active per purpose" a database fact. Enrollment codes are one per principal, through a partial unique index on `(principal_id)` with the same condition.

The hash is SHA-256 over UTF-8 `iskender.operator-token.v1`, `0x00`, the purpose, `0x00`, the ID, `0x00`, and the secret's 32 bytes. It is compared in .NET with `CryptographicOperations.FixedTimeEquals` (FR-004). Binding the purpose and ID into the hash means a row cannot be reused for another purpose or ID even by someone who can write the table.

**Rationale**: With 256 bits of entropy a slow password hash adds nothing, and a fast hash keeps redemption cheap under a flood. The separate identifier lets a lookup find one row without comparing the secret in SQL, so the comparison stays in constant time in code.

**Alternatives rejected**: bcrypt, scrypt, or Argon2, which defend low-entropy secrets this token does not have. An HMAC with a server key, which needs a key that does not exist before ISK-0021. Comparing hashes in a `WHERE` clause, which is not constant-time.

## R5. Redemption in one unit (validates AV-2)

**Decision**: Each redemption is one operator-scope unit at Read Committed, with no advisory lock or coordinator:

1. `SELECT … FROM isk_identity.tokens WHERE token_id = $1 FOR UPDATE`. No row: roll back, and fail with reason `unknown`. No audit record (FR-028).
2. In code: compare the hash, computed under the row's stored purpose and never the text's prefix (Tokens rule 1; CONV-0010-006), in constant time, then check, in this order, purpose, consumed, superseded, and expiry, the last against `statement_timestamp()` read in the same `SELECT`. The first that fails is the reason class: `wrong_secret`, `wrong_purpose`, `consumed`, `superseded`, or `expired`. Write the failure audit record and commit.
3. **Bootstrap**: `INSERT INTO isk_identity.installation … ON CONFLICT DO NOTHING RETURNING singleton`. No row returned: write the failure record with reason `already_initialized` and commit, leaving the token unconsumed. **Recovery**: `SELECT EXISTS (… installation)`. False: reason `not_initialized`, the same way.
4. Mark the token consumed with `… AND expires_at > statement_timestamp()`, so expiry is judged again by the consuming statement's clock, after any wait on the row lock. No row updated: roll back, then write the failure record with reason `expired` in a unit of its own and commit, so a token is consumed before its expiry or not at all (CONV-0010-007). Otherwise insert the principal, its `system_operator` grant, the enrollment code, and the success audit record, then commit. An enrollment code's consumption is guarded the same way.

Concurrent redemptions of one token serialize on the row lock of step 1. Under Read Committed, PostgreSQL re-reads a row that `FOR UPDATE` waited on, so a waiter sees `consumed_at` set and fails with `consumed`. Two different bootstrap tokens can both be valid only during a supersede, and then the step-3 insert serializes them: the second waits on the primary key until the first commits, then finds the conflict. So exactly one bootstrap ever succeeds (FR-009). An unknown commit outcome is reported as `persistence.unavailable`, a `503`, and never retried (persistence Failures rule 4).

**Rationale**: Every success and every failure that names a stored token commits exactly one audit record with its outcome, and the decision is made under a row lock, never by a check-then-act across two units.

**Alternatives rejected**: One conditional `UPDATE … WHERE consumed_at IS NULL …`, which serializes just as well but cannot say why it matched nothing, so it cannot write the reason class FR-028 requires. Serializable isolation, which adds retries for nothing the row lock does not already give. An advisory lock, which the spec's AV-2 asks to avoid.

## R6. Issuing tokens

**Decision**: `bootstrap issue` and `bootstrap recover` call `IOperatorTokens.IssueAsync(purpose)`. In one operator-scope unit it:

1. Takes the transaction-scoped advisory lock `(operator, identity, token-issue)` with `Bounded(5 s)`, so two issuers serialize instead of one failing on the partial unique index. This is mutual exclusion inside one transaction, which Advisory locks rules 1 and 4 allow.
2. Marks every active token of the purpose superseded. This waits on the row lock of any redemption in progress.
3. Only then reads the installation state, and fails with `operator.already_initialized` (bootstrap) or `operator.not_initialized` (recovery), rolling back the supersede. Under Read Committed this statement sees a redemption that committed while step 2 waited, so a bootstrap issue never succeeds on an installation that became initialized meanwhile (CONV-0010-001). Otherwise it writes one `token.superseded` audit record for each superseded token.
4. Inserts the new row, with `expires_at = statement_timestamp() + $lifetime`, and writes the issue record.
5. Commits, then returns the token text.

The command writes the token and a newline to standard output only after the commit, and nothing else to standard output.

**Rationale**: The token never exists before its row is durable, and a lost commit leaves nothing printed.

**Alternatives rejected**: Printing before the commit, which shows the operator a token that may never be stored.

## R7. The admin surface (paths decided by the maintainer in `plan`)

**Decision**: `MapIskenderOperatorSecurity(this IEndpointRouteBuilder, string prefix)` maps one route group. The container calls it with `/admin/v1`:

| Method and route | Requirement | Answers |
|---|---|---|
| `POST /admin/v1/bootstrap` | Anonymous, inventory entry `control-plane.traffic.bootstrap` | `200` with `{"principalId":…,"enrollmentCode":…}`, or the uniform `403`, `429`, or `503` |
| `POST /admin/v1/recovery` | Anonymous, inventory entry `control-plane.traffic.recovery` | The same |
| `GET /admin/v1/installation` | Operator action `installation.read` | `200` with `{"state":"initialized","initializedAt":…}`, or `401`, `403`, or `503` |

`GET /admin/v1/installation` is the one protected route the control plane ships (decided by the maintainer in `plan`, FR-036a). Its answer never holds a principal ID. Every success sets `Cache-Control: no-store`. Every failure is the ISK-0002 `error-envelope` with the request's correlation ID. A request under the prefix that matches no route by both method and path answers `404` with no body, as the health listener does, so it reveals nothing and is not an endpoint. That includes a known path with another method, which ASP.NET Core would otherwise answer with `405` from an endpoint outside the group, bypassing the gate (decided by the maintainer in `checklist` on 2026-11-21).

The group's last convention, through `RouteGroupBuilder.Finally`, wraps the `RequestDelegate` of every endpoint in the group, however it was mapped, so no endpoint in the group escapes the gate, and it reads each endpoint's declaration. An endpoint with no declaration is recorded as undeclared (R9).

**Rationale**: Wrapping the delegate covers route handlers and plain `RequestDelegate` endpoints alike, which an endpoint filter would not, since filters apply only to route handlers.

**Alternatives rejected**: Endpoint filters, which a plain delegate bypasses. A middleware, which a mapping method on `IEndpointRouteBuilder` cannot add, and which an embedding host would have to place correctly.

## R8. The request pipeline

**Decision**: Each wrapped endpoint runs these steps in order, and stops at the first that answers:

| Step | Protected route | Redemption |
|---|---|---|
| 1. Correlation | A new `RequestCorrelationId` | The same |
| 2. Attempt limit | | Per control-plane host (service container), `operator.bootstrap.attemptsPerMinute`, shared by both routes: `429` `operator.redemption_limited`, with no read |
| 3. Body | | Not `application/json`, over 4 KiB, or not exactly `{"token":"<string>"}`: the uniform `403`, with no read and no audit record |
| 4. Gate | Read the state: `uninitialized` is `403` `operator.uninitialized`, unreadable is `503` `operator.state_unavailable` | None (decided in `clarify`) |
| 5. Principal | Resolve: none composed or none resolved is `401` `identity.unauthenticated`; a throw is `503` `operator.authorization_unavailable` | None |
| 6. Authorization | Decide: no grant is `403` `identity.auth_denied`; unreadable grants are `503` `operator.authorization_unavailable` | None |
| 7. Handler | Runs | The R5 unit. A token that is malformed is the uniform `403` with no read. A persistence failure is `503` `operator.state_unavailable` |

The limit uses `System.Threading.RateLimiting.FixedWindowRateLimiter`, with a one-minute window and no queue. It is a singleton of the host's service provider, never a static, so two embedded hosts in one process each have their own (hosting contract, Lifetimes and Static state). The uniform failure is `403` `operator.redemption_failed`, whose body differs between failures only in its correlation ID (SC-003).

A redemption checks the state only inside its own unit (R5), so it answers `503` only when it reaches the store and the store fails. A malformed body or token never reaches the store, so it gets the uniform `403` whatever the store's state. This follows from the `clarify` decision exempting both redemptions from the gate, and leaves no choice.

**Rationale**: The gate runs before any principal is resolved, so no credential, valid, forged, or stolen, changes the uninitialized answer (FR-018). The cheapest refusals come first, so a flood costs no database read.

**Alternatives rejected**: ASP.NET Core's rate-limiting middleware, which an embedding host would have to add to its own pipeline.

## R9. Route declarations and the start-time check

**Decision**: A route declares exactly one of `.RequireOperatorAction(OperatorAction)` or `.AllowAnonymousOperatorEndpoint(string inventoryEntry)`, both extension methods on `IEndpointConventionBuilder` in `Iskender.Management.Security`. An `IHostedLifecycleService` that `AddIskenderOperatorSecurity()` registers enumerates every `EndpointDataSource` in its `StartingAsync`. That runs before any hosted service's `StartAsync`, so it runs before Kestrel binds. It fails start with `operator.route_undeclared`, and event 9005 naming the route template, when an endpoint in the group declares neither or both, or when `MapIskenderOperatorSecurity` was called more than once or never. The container maps that code to exit code 10, after event 7002, with no listener bound.

The check runs only when a host starts. `migrate` and `bootstrap` build a service provider and start no host, so they map no route and the check does not run. `MapIskenderOperatorSecurity` rejects a prefix outside the data model's pattern with `core.value.invalid` at the call, as `MapIskender(...)` does.

**Rationale**: Mapping happens before the host starts, so the endpoints exist when `StartingAsync` runs, and the check covers an embedding host as well as the container.

**Alternatives rejected**: Checking in the container entry point, which `Iskender.Infrastructure.Container` cannot do because it may not reference `Iskender.Management`, and which would leave embedding hosts unchecked. Checking at first request, which lets an undeclared route ship.

## R10. Principal resolution and authorization

**Decision**:

- **`IOperatorPrincipalResolver`**, in `Iskender.Management.Security`: `ValueTask<OperatorPrincipalResolution> ResolveAsync(HttpContext context, CancellationToken cancellationToken)`, returning `Unresolved` or `Resolved(ResourceId principal)`. It sits in `Iskender.Management`, not `Iskender.Domain.Identity`, because it reads the HTTP request (R1). ISK-0013, ISK-0014, and ISK-0020 register implementations. With none registered, the pipeline treats every request as unresolved, so a protected route answers `401` (FR-017).
- **`IOperatorAuthorization`**, in `Iskender.Domain.Identity`: `ValueTask<OperatorDecision> DecideAsync(ResourceId principal, OperatorAction action, CancellationToken)`, returning `Allowed` or `Denied`, and throwing the persistence failure when it cannot read. The PostgreSQL implementation reads the principal's grants in an operator-scope unit on every call, with no cache, and maps each role to its actions in code. `system_operator` holds every action this spec defines, which today is `installation.read`.
- **`OperatorAction`**: a name with the operation pattern (`PortNames.RequireOperation`), so later specs add actions without changing a type.
- Tests register `TestOperatorPrincipalResolver`, in `tests/Iskender.Identity.Tests`, which reads a test header. No production project references it, which an architecture-suite check confirms (FR-043).

**Rationale**: Grants are read from the store for an internal principal on every request, never from a role string, so a revoked grant takes effect at once and an unreadable store fails closed.

**Alternatives rejected**: Caching grants, which delays a revocation by the cache's lifetime and needs a coordinator to invalidate.

## R11. Principal, grant, and enrollment tables

**Decision**: Operator tables in `isk_identity`:

- `principals`: `principal_id text primary key`, `kind text not null check (kind = 'operator')`, `created_at timestamptz not null`, `created_through text not null check (created_through in ('bootstrap', 'recovery'))`. It holds no identity-provider subject, name, or email (FR-013).
- `operator_grants`: `principal_id text references principals`, `role text not null check (role = 'system_operator')`, `granted_at timestamptz not null`, primary key `(principal_id, role)`.
- Enrollment codes are rows of `tokens` with purpose `enrollment`, bound to `principal_id`, lasting `operator.enrollment.codeLifetime`.

`IEnrollmentCodes.ConsumeAsync(IUnitOfWork unit, ResourceId principal, string code, CancellationToken)` runs in the caller's unit, so a consumer attaches its sign-in method in the same commit. It locks the row as R5 does, succeeds only for the code's own principal while the code is unconsumed and unexpired, writes an `enrollment.consumed` audit record, and otherwise fails with `operator.enrollment_invalid`. This spec ships no caller of it (FR-011a).

Principal IDs use the new ISK-0002 resource type `principal`, prefix `prin`, `since: 7`.

**Rationale**: One token table and one hash rule serve all three purposes, so enrollment codes need no second design.

**Alternatives rejected**: A separate enrollment table, which repeats the token table's columns, rules, and tests.

## R12. Audit records

**Decision**: `IAuditRecorder.AppendAsync(IUnitOfWork unit, AuditRecord record, CancellationToken)`, in `Iskender.Domain.Telemetry`, appends one row of `isk_audit.records` in the caller's unit, so the record commits or rolls back with its change (FR-026). Its columns are `audit_id` (a `ResourceTypes.AuditEntry` ID), `recorded_at` (`statement_timestamp()`), `actor`, `target_principal`, `action`, `scope`, `result`, `reason`, `configuration_revision`, and `correlation_id`. A check constraint limits each enumerated column to the operator-security contract's fixed list. The `audit` migration revokes `UPDATE`, `DELETE`, and `TRUNCATE` on the table from `iskender_runtime`, overriding the schema's default privileges (persistence Migrations rule 6), so the runtime login can insert and read only (FR-029). An index on `recorded_at` serves reads.

The recorder rejects an `AuditRecord` whose fields are outside the lists with `core.value.invalid` before any statement, and the type holds no field for a token, hash, address, or body (FR-027).

**Rationale**: A privilege the database enforces cannot be bypassed by a bug in the runtime. Tamper-evident ordering is ISK-0026's (AV-6).

**Alternatives rejected**: A trigger that rejects updates, which the table's owner can disable, and which needs a function the runtime might call. A hash chain, which is ISK-0026's.

## R13. Transport modes and TLS (unencrypted keys decided by the maintainer in `plan`)

**Decision**: The `container` section becomes version 2, with three fields:

| Field | Type | Default | Constraints |
|---|---|---|---|
| `trafficTransport` | enumeration | `none` | `listener`, `upstream`, or `none` |
| `tlsCertificateFile` | string | none | An absolute path. Required when `listener`, and rejected otherwise |
| `tlsKeyFile` | string | none | The same |

`ContainerSettings.From` checks them after validation, only for a definition with a traffic listener, so the worker ignores them:

- `none` outside `development` fails with `operator.transport_unprotected`, naming `container.trafficTransport`.
- A file field missing in `listener` mode, or set in another mode, fails with `operator.certificate_unusable`, naming the field.
- In `listener` mode, `X509Certificate2.CreateFromPemFile(cert, key)` loads the pair once. A missing, unreadable, malformed, encrypted, not-yet-valid, expired, or mismatched pair fails with `operator.certificate_unusable`, naming the field, and never its path or content.

Both map to exit code 2 with event 7002, before any listener binds. The traffic listener then binds with `ListenAnyIP(port, o => o.UseHttps(new HttpsConnectionAdapterOptions { ServerCertificate = certificate, SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }))`. Because the entry point uses `UseKestrelCore()`, it also calls `UseKestrelHttpsConfiguration()` in `listener` mode, which Kestrel requires before `UseHttps`. A plain-HTTP request to that port fails the TLS handshake, so no route runs. `CreateFromPemFile` keeps the key in memory and writes no certificate store, which suits the read-only, non-root image (**validates AV-8**, proved by the container tests). Only unencrypted PEM keys are accepted (decided by the maintainer in `plan`). The health listener stays plain HTTP. Rotation is a restart.

`upstream` binds plain HTTP and is the operator's declaration. Event 7001 gains no field: the transport mode is configuration, and Diagnostics rule 6 keeps values out of events.

**Rationale**: The decision taken in `specify` and extended in `clarify` makes each plane state its transport outside `development`. A default of `none` means a document that says nothing fails safe.

**Alternatives rejected**: A default of `upstream`, which silently vouches for a front end that may not exist. Loading the certificate at first connection, which would let a broken file start a plane. Passphrase-protected keys, which need a secret provider that ISK-0021 adds.

## R14. Commands and exit codes

**Decision**: The control-plane executable gains `bootstrap issue` and `bootstrap recover`, parsed before any configuration is read, run like `migrate` through `context.BuildServices`, with no composition start-up, no listener, and the runtime login. The data plane answers `bootstrap` with usage, exit code 64. New exit codes, never reused:

| Code | Meaning |
|---|---|
| 8 | `bootstrap issue`: the installation is already initialized (`operator.already_initialized`) |
| 9 | `bootstrap recover`: the installation is not initialized (`operator.not_initialized`) |
| 10 | Serving start refused: an admin route without exactly one declaration (`operator.route_undeclared`) |

A `persistence.*` failure exits 5, a configuration rejection 2, and a missing registration 3, as for `migrate`. New container event 7008, `ContainerBootstrapFinished`, `Iskender bootstrap command finished: operation {Operation}, outcome {Outcome}, code {Code}.`, is written once for every run whose arguments parse, to standard error with the other logs, and never holds the token.

**Rationale**: Each refusal an operator can act on has its own code, as the spec requires, and the command mirrors `migrate`, whose trust anchor it shares.

**Alternatives rejected**: One exit code for both state refusals, which FR-003's "its own exit code" rules out.

## R15. Composition and embedding (settles ISK-0006's AV-5)

**Decision**: `AddIskenderOperatorSecurity(this IServiceCollection)` adds the module `operator-security`, which registers the `operator` section and requires `control-plane` and `identity-postgresql`. `AddIskenderPostgreSqlIdentity()` adds the module `identity-postgresql`, the stores, the audit recorder, and the `identity` and `audit` migration modules, and requires `persistence-postgresql` and `work-postgresql`. Under `AddIskender(...)` or `AddIskenderWorker(...)`, the requirement check fails with `hosting.registration_missing`. `MapIskenderOperatorSecurity` called without the registration fails with `hosting.registration_missing` too. An embedding host composes `AddIskenderControlPlane(...)`, both registrations, and the adapters on its own service collection, and maps the routes under its own prefix. Its own principal resolver, when it has one, is registered the same way.

The container composes the operator registrations in the serving control plane and in `migrate` and `bootstrap`, so the same documents validate, but not in the worker. The migration set therefore gains `identity` and `audit` (FR-032).

**Migration order (decided by the maintainer in `plan`)**: `audit` declares a dependency on `work`, and `identity` on `audit`, so the forward order is `coordination`, `directory`, `work`, `audit`, `identity`, and rolling back below ISK-0010 is `migrate reverse --steps 2 --confirm-data-loss`, which reverses `identity`, then `audit`, and leaves ISK-0009's modules and their rows untouched. Without the dependencies, the runner's tie-break by name (persistence Migrations rule 5) would order them `audit`, `coordination`, `directory`, `identity`, `work`, and two reverse steps would remove `work` and `identity`. The `work` dependency exists for ordering only. So `AddIskenderPostgreSqlIdentity()` requires `work-postgresql`, which every container plane already composes, and an embedding control plane composes `AddIskenderPostgreSqlWork()` too.

**Alternatives rejected**: Reversing in the order the history records, which a fresh install, whose history matches the static order, does not fix, and which changes ISK-0004's runner. A rollback of five steps and a re-apply of three, which loses the outbox and directory rows.

**Rationale**: This is the existing pattern of registration calls and roots, with no change to a public member of `Iskender.Hosting`.

**Alternatives rejected**: Composing the operator surface under the data-plane root, which rule A6 and TR-12 forbid.

## R16. Configuration

**Decision**: Namespace `operator`, version 1, registered only by `AddIskenderOperatorSecurity()`:

| Field | Type | Default | Range |
|---|---|---|---|
| `bootstrap.tokenLifetime` | duration | `1h` | `5m` to `24h` |
| `bootstrap.attemptsPerMinute` | integer | `10` | 1 to 600 |
| `enrollment.codeLifetime` | duration | `24h` | `1h` to `7d` |

The recovery token uses `bootstrap.tokenLifetime`. A data-plane or worker document with an `operator` section is rejected with `config.namespace.unknown` (container Configuration roots rule 8).

## R17. Error codes and the resource type

**Decision**: `core-contracts` 7 adds 11 codes, all `since: 7`, and the resource type `principal`. The `public-contracts-and-versioning` contract's Error codes and Resource types tables gain the rows in place, which its "Changing this contract" section says needs no revision.

| Code | Category | HTTP on the admin surface | Safe message |
|---|---|---|---|
| `operator.redemption_failed` | `permission_denied` | 403 | The token was not accepted |
| `operator.redemption_limited` | `limit_exceeded` | 429 | Too many redemption attempts; try again later |
| `operator.uninitialized` | `permission_denied` | 403 | The installation has not been initialized |
| `operator.state_unavailable` | `internal` | 503 | The installation state cannot be read |
| `operator.authorization_unavailable` | `internal` | 503 | The authorization decision cannot be made |
| `operator.already_initialized` | `conflict` | Command only | The installation is already initialized |
| `operator.not_initialized` | `conflict` | Command only | The installation is not initialized |
| `operator.route_undeclared` | `internal` | Start only | An admin route declares no requirement |
| `operator.transport_unprotected` | `invalid_request` | Start only | The traffic listener has no transport protection |
| `operator.certificate_unusable` | `invalid_request` | Start only | The TLS certificate or key cannot be used |
| `operator.enrollment_invalid` | `permission_denied` | Port only | The enrollment code was not accepted |

`401` uses the existing `identity.unauthenticated`, and a privilege denial `identity.auth_denied`.

**Rationale**: `operator.authorization_unavailable` is added beside the spec's list because FR-015's unreadable grants and the edge case of a resolver that throws need a registered `503`, and `operator.state_unavailable` would misname them.

## R18. Diagnostics

**Decision**: Category `Iskender.Identity`, events 9001 to 9008, and the meter and activity source `Iskender.Identity`, which every entry point subscribes to (FR-038). Field and label values are limited to action, outcome, reason class, purpose, and route template. The events, metrics, and activities are fixed in the operator-security contract's Diagnostics section. A telemetry canary test searches all captured output for every secret, hash, and principal ID of the run (SC-004).

## R19. The anonymous endpoint inventory

**Decision**: The rule source is `tests/Iskender.Container.Tests/Security/anonymous-endpoints.json`, and the operator-security contract's inventory table must match it, which the test checks as rule C1 does for the rules contract. The inventory test composes each process's Entry points rule 2 registrations and routes through `ContainerEntryPoint`, as the ISK-0007 tests do, enumerates every `EndpointDataSource` endpoint on the traffic listener, and reads the health listener's routes from `HealthEndpoints.Routes`, a list that the handler itself serves from. An endpoint is anonymous unless it declares an operator action. The test fails on any anonymous endpoint missing from the list, and on a listed one that no process maps, naming process, listener, method, and route. A negative fixture adds an unlisted endpoint and expects the failure (SC-005). The container tests also probe each listed endpoint on the built images.

## R20. Developer topology

**Decision**: Local environment contract version 3. The `control-plane` service publishes `127.0.0.1:${ISKENDER_LOCAL_CONTROL_PLANE_PORT:-18090}:8080`. A `bootstrap` service, under a Compose profile so `up` never starts it, runs the control-plane image with the control-plane documents, like `migrate`. `local/scripts/bootstrap.sh issue|recover` runs `docker compose run --rm --no-deps bootstrap bootstrap <operation>`, so the token appears only in the developer's terminal, and exits with the command's code. Each plane's base document adds `container.trafficTransport: upstream`, and its development overlay sets `none`. The worker's documents do not change. The smoke script adds three assertions, valid before ISK-0021: `POST /admin/v1/bootstrap` with a well-formed token generated at run time from `/dev/urandom` answers `503` `operator.state_unavailable`; `GET /admin/v1/installation` answers the same; and `bootstrap.sh issue` exits 5. The three readiness answers are unchanged byte for byte.

## R21. Tests

**Decision**: A new test project, `tests/Iskender.Identity.Tests`, holds the operator fixture: two or more control-plane hosts on one ISK-0004 PostgreSQL fixture database, the test principal resolver, a store made unreachable by pointing a host at a closed port, and by stopping a dedicated fixture server, as the ISK-0004 health tests do, and the 20 × 50 concurrency runs of SC-002 with `attemptsPerMinute` 600. It also holds the gate, privilege, audit, recovery, enrollment, telemetry canary, and embedding tests. `tests/Iskender.Container.Tests` gains the inventory, transport, TLS, command, and exit code tests, generating a self-signed certificate at run time into a temporary directory mounted read-only, so no key is ever tracked. Existing container test documents declare `upstream`. The architecture, compatibility, and PostgreSQL adapter suites gain the checks named in the plan. Everything runs in the existing CI checks, so CI stays at 10 (**validates AV-7**).

## Refinements with no choice left

- **Module dependency rules stay at version 5** (R1): `Iskender.Management` may take ASP.NET Core today.
- **`public-contracts-and-versioning` gains rows in place, with no revision** (R17), as its own "Changing this contract" section states.
- **A malformed redemption gets the uniform `403` even when the store is down** (R8), because the redemptions are exempt from the gate (`clarify`).
- **Container tests that start a plane without an environment name must declare a transport mode** (R13), or they fail with exit code 2, as FR-022a requires.
