# Operator Security Contract

**Version**: 1

**Owning spec**: ISK-0010 — System-Operator Bootstrap & Security Baseline

**Review owner**: Security owner, with the Architecture (ADR) owner for Ownership and Composition, and the Privacy/Operations owner for Audit records and Diagnostics. See `CONTRIBUTING.md` for who holds each role.

This contract states what operators, security reviewers, and later identity specs may rely on from Iskender's operator baseline: the installation state, operator tokens and their redemption, the first operator principal and its grant, privilege checks, the uninitialized gate, the anonymous endpoint inventory, the transport assumptions, the recovery draft, and the audit records. The type shapes are in [data-model.md](../data-model.md), and the reasons in [research.md](../research.md). Storage follows the [persistence contract](persistence.md), version 4, composition the [hosting contract](hosting.md), version 4, the processes and listeners the [container contract](container.md), version 3, and the developer topology the [local environment contract](local-environment.md), version 3.

## Changing this contract

A change to a rule here is made by a later spec's revision of this contract, which increments the version, publishes the contract in that spec's `contracts/` directory, repoints the links in `docs/03-domain-and-technical-architecture.md`, `docs/04-technology-stack-and-deployment.md`, and `docs/05-identity-security-and-privacy.md`, and records the change in this table. **Adding an endpoint reachable without authentication is a revision, reviewed by the Security owner.** Renaming or removing a route, action, role, reason class, audit action, error code, event ID, metric, or configuration field breaks every client, dashboard, and manifest that reads it.

| Version | Spec | Change | Approval | Owner |
|---|---|---|---|---|
| 1 | ISK-0010 | First version | ISK-0010's PR, maintainer decisions in `specify`, `clarify`, and `plan` on 2026-11-21 | Security owner |

The ports are in the embeddable `Iskender.Domain` and `Iskender.Management`, so the ISK-0002 compatibility harness does not version them. This contract's version is the commitment to later specs. The error codes and the `principal` resource type are in `Iskender.Core.Contracts` and are versioned by the harness.

## What this is not

No local account, password, session, cookie, external OIDC login, organization or project role, project-level grant, API key, management resource API, or management UI. No credential provider, so before ISK-0021 no process can connect to PostgreSQL, and every admin route that reaches the store answers `503`. No production implementation of principal resolution or of the enrollment consumer. No access to tenant data for any operator.

## Ownership

*Review owner: Architecture (ADR) owner.*

1. The transport-neutral ports are in `Iskender.Domain.Identity`, and the audit port in `Iskender.Domain.Telemetry`. Neither module namespace uses the other (module dependency rules contract, version 5, rule A17, unchanged).
2. The HTTP surface, the principal-resolution port, the `operator` section, and the `bootstrap` command's library are in `Iskender.Management`, which takes the shared framework `Microsoft.AspNetCore.App`. Rule A19 allows it, and rule A3 still holds.
3. The PostgreSQL implementations are in `Iskender.Infrastructure.PostgreSql.Identity`, naming only `isk_identity`, and in the framework namespace `Iskender.Infrastructure.PostgreSql.Audit`, naming only `isk_audit` (persistence contract version 4, Ownership rule 4; **decided by the maintainer in `plan` on 2026-11-21**).
4. Each port and adapter type's XML comment names ISK-0010 as its owning spec.

## Composition

*Review owner: Architecture (ADR) owner.*

1. `AddIskenderOperatorSecurity()` adds the module `operator-security`, which registers the `operator` section and requires `control-plane` and `identity-postgresql`. Under `AddIskender(...)` or `AddIskenderWorker(...)`, start fails with `hosting.registration_missing` (**settles ISK-0006's AV-5**).
2. `AddIskenderPostgreSqlIdentity()` adds the module `identity-postgresql`, which requires `persistence-postgresql` and `work-postgresql`, the ports' implementations, and the migration modules `audit` and `identity`.
3. `MapIskenderOperatorSecurity(prefix)` maps the admin routes once, under a prefix the host chooses. The container's prefix is `/admin/v1` (**decided by the maintainer in `plan` on 2026-11-21**). A second call, or a call without `AddIskenderOperatorSecurity()`, fails start.
4. An embedding host composes `AddIskenderControlPlane(...)`, both registrations, and the PostgreSQL persistence and outbox adapters on its own service collection, and maps the routes. It never composes them beside `AddIskender(...)` on one collection, so an application that embeds both planes runs two hosts.
5. The data plane and the worker compose none of this, and reject an `operator` section with `config.namespace.unknown`.

## Installation state

*Review owner: Security owner.*

1. The state is `uninitialized` or `initialized`. It is one operator-scope row of `isk_identity.installation`. A database with no row is `uninitialized`.
2. The state moves only from `uninitialized` to `initialized`, only through a successful bootstrap redemption. The runtime login has no `UPDATE`, `DELETE`, or `TRUNCATE` on the table, so nothing can move it back.
3. The state never appears in a readiness answer, so readiness and the developer topology's smoke answers are unchanged.
4. A process may keep an `initialized` answer once read, because the state never moves back. It never keeps an `uninitialized` or unknown answer.

## Tokens

*Review owner: Security owner.*

1. **Form**: `isk<p>_<id>_<secret>`, where `<p>` is `b` (bootstrap), `r` (recovery), or `e` (enrollment), `<id>` is 26 lowercase Crockford base32 characters encoding 16 random bytes, and `<secret>` is 43 unpadded base64url characters encoding 32 bytes from a cryptographically secure source. Any other text is malformed. The prefix is informational. The stored purpose decides.
2. **Storage**: only SHA-256 over UTF-8 `iskender.operator-token.v1`, `0x00`, the purpose, `0x00`, the ID, `0x00`, and the secret's 32 bytes is stored, with the ID, purpose, expiry, and the consumed and superseded markers. Verification compares hashes with a constant-time comparison.
3. **Lifetime**: bootstrap and recovery tokens last `operator.bootstrap.tokenLifetime`, and enrollment codes `operator.enrollment.codeLifetime`, measured by the database's clock. Rotating or expiring a token needs no restart.
4. **One active**: issuing a bootstrap or recovery token supersedes every unconsumed token of the same purpose in the same commit. A partial unique index makes a second active token of a purpose impossible.
5. **Never shown again**: a token's text appears once, on the issuing command's standard output, or, for an enrollment code, in the redemption's answer. It never appears in a log, trace, metric, error, readiness answer, audit record, configuration document, or database row.
6. **Issuing**: `bootstrap issue` issues a bootstrap token only while the installation is `uninitialized`, and `bootstrap recover` a recovery token only while it is `initialized`. Issuers serialize on a transaction-scoped advisory lock with a 5-second bound, so two concurrent issues both succeed, and the later supersedes the earlier. An issuer that waits longer fails with `persistence.lock_timeout`, exits 5, and prints nothing (persistence contract, Advisory locks rule 3). The text is printed only after the commit. The command's rules and exit codes are in the container contract, Bootstrap command.

## Redemption

*Review owner: Security owner.*

1. Each redemption is one operator-scope unit at Read Committed. It locks the token's row, verifies it, and, for a bootstrap, inserts the installation row with `ON CONFLICT DO NOTHING`. It then consumes the token and creates the principal, its `system_operator` grant, its enrollment code, and the success audit record, all in one commit or none.
2. **Exactly one succeeds**: of any number of concurrent redemptions, across any number of control-plane processes on one database, exactly one bootstrap ever succeeds, and a consumed token never succeeds again.
3. **Reason classes**, checked in this order: `malformed`, `unknown`, `wrong_secret`, `wrong_purpose`, `consumed`, `superseded`, `expired`, then `already_initialized` (bootstrap) or `not_initialized` (recovery). `malformed` and `unknown` write no audit record. Every other failure writes one, and changes no token.
4. **Uniform failure**: every failed redemption, for any reason, answers `403` with `operator.redemption_failed`, and its body differs from every other failure's only in the correlation ID. The reason class appears only in the audit record and in event 9002.
5. **Success** answers `200` as `application/json` with `Cache-Control: no-store` and exactly `{"principalId":"<principal ID>","enrollmentCode":"<code>"}`.
6. **Recovery** creates a new operator principal with the `system_operator` grant. It never reads, changes, or removes another principal, grant, or sign-in method.
7. **Purpose**: a token presented at the other purpose's route fails with `wrong_purpose`, whatever the installation state, because neither redemption is behind the uninitialized gate.
8. An unknown commit outcome answers `503` with `operator.state_unavailable`, and is never retried. If the installation is then `initialized`, a new redemption fails. Otherwise the operator issues a new token.

## Principals and grants

*Review owner: Security owner.*

1. An operator principal has an opaque ISK-0002 ID of resource type `principal`, prefix `prin`, `since: 7`. It holds no identity-provider subject, name, email, or sign-in method.
2. This contract defines one role, `system_operator`, granted only at the operator scope, and one action, `installation.read`. `system_operator` holds every action this contract defines. Later specs add actions through a revision.
3. A `system_operator` grant gives no access to any tenant row. Operator access to tenant data still needs the audited elevated operation that the persistence contract reserves.

## Enrollment

*Review owner: Security owner.*

1. Each successful bootstrap or recovery redemption creates one enrollment code bound to its new principal, in the same commit, with the token form and storage of Tokens rules 1 and 2 and purpose `enrollment`.
2. `IEnrollmentCodes.ConsumeAsync` consumes a code once, atomically, in the caller's unit, only for its bound principal, and writes an `enrollment.consumed` audit record. Anything else fails with `operator.enrollment_invalid`.
3. This spec ships no consumer. ISK-0013 or ISK-0014 attaches the first sign-in method through it. An expired or lost code is replaced only through recovery, which creates a new principal.

## Admin routes

*Review owner: Security owner.*

1. The admin routes, under the host's prefix, are:

   | Method and route | Requirement | Answers |
   |---|---|---|
   | `POST <prefix>/bootstrap` | Anonymous, inventory entry `control-plane.traffic.bootstrap` | Redemption rules 4 and 5, `429`, `503` |
   | `POST <prefix>/recovery` | Anonymous, inventory entry `control-plane.traffic.recovery` | The same |
   | `GET <prefix>/installation` | `installation.read` | `200` with `{"state":"initialized","initializedAt":"<RFC 3339 UTC>"}`, `401`, `403`, `503` |

   `GET <prefix>/installation` is the one protected route shipped (**decided by the maintainer in `plan` on 2026-11-21**). Its answer never holds a principal ID.

2. **Declarations**: every endpoint in the group declares exactly one of `RequireOperatorAction(action)` or `AllowAnonymousOperatorEndpoint(entry)`. One that declares neither or both fails start with `operator.route_undeclared`, event 9005, and no listener bound. The check runs in `IHostedLifecycleService.StartingAsync`, before any hosted service starts.
3. **Coverage**: the group's last convention wraps every endpoint's delegate, however it was mapped, so no endpoint in the group escapes the pipeline below.
4. **Unmatched requests**: a request under the prefix that matches no route by both method and path answers `404` with no body, including a known path with another method, so no `405` reveals that a route exists (**decided by the maintainer in `checklist` on 2026-11-21**). Matching ignores the media types a route handler accepts, so a request with another `Content-Type` gets the pipeline's answer, and the handler's `415` only once the pipeline admits it (CONV-0010-004).
5. **Pipeline**: each request runs these steps in order, and the first that applies answers:

   | Step | Protected routes | Redemptions |
   |---|---|---|
   | Attempt limit | | Beyond `operator.bootstrap.attemptsPerMinute` attempts per process per minute, across both routes: `429` `operator.redemption_limited`, with no database read |
   | Body | | Not `application/json`, over 4 KiB, or not exactly `{"token":"<string>"}`: the uniform failure, with no database read and no audit record |
   | Uninitialized gate | `uninitialized`: `403` `operator.uninitialized`. Unreadable: `503` `operator.state_unavailable` | None |
   | Principal | None resolved, or no resolver composed: `401` `identity.unauthenticated`. The resolver throws: `503` `operator.authorization_unavailable` | None |
   | Authorization | No grant for the action: `403` `identity.auth_denied`. Grants unreadable: `503` `operator.authorization_unavailable` | None |
   | Handler | Runs | A malformed token: the uniform failure, with no read. Otherwise Redemption rule 1. A persistence failure: `503` `operator.state_unavailable` |

6. **The gate runs before any principal is resolved**, so no credential, valid, forged, or stolen, changes the uninitialized answer, and a route added by a later spec inherits it with no code of its own.
7. Every failure body is the ISK-0002 `error-envelope` with the request's correlation ID.
8. **Principal resolution** is the port `IOperatorPrincipalResolver`, which ISK-0013, ISK-0014, and ISK-0020 implement. This spec ships none, so on an initialized installation every protected route answers `401`. Tests use a test-only implementation that no production project references.
9. **Authorization** reads the principal's grants from the store on every request, with no cache, and never reads a role from a token.

## Anonymous endpoint inventory

*Review owner: Security owner.*

1. These are every endpoint reachable without authentication. The rule source is `tests/Iskender.Container.Tests/Security/anonymous-endpoints.json`, which this table must match:

   | Entry | Process | Listener | Method | Route | Why it is anonymous |
   |---|---|---|---|---|---|
   | `data-plane.health.live` | Data plane | Health | `GET` | `/health/live` | Orchestrator liveness probe (container contract, Liveness) |
   | `data-plane.health.ready` | Data plane | Health | `GET` | `/health/ready` | Orchestrator readiness probe (container contract, Readiness) |
   | `data-plane.traffic.not-found` | Data plane | Traffic | Any | `MapIskender("/")` catch-all | Answers `404` with no data until ISK-0019 and ISK-0029 put authentication in front of it |
   | `control-plane.health.live` | Control plane | Health | `GET` | `/health/live` | As the data plane's |
   | `control-plane.health.ready` | Control plane | Health | `GET` | `/health/ready` | As the data plane's |
   | `control-plane.traffic.bootstrap` | Control plane | Traffic | `POST` | `/admin/v1/bootstrap` | The first operator has no credential. The token is the credential |
   | `control-plane.traffic.recovery` | Control plane | Traffic | `POST` | `/admin/v1/recovery` | A locked-out installation has no usable credential. The token is the credential |
   | `worker.health.live` | Worker | Health | `GET` | `/health/live` | As the data plane's |
   | `worker.health.ready` | Worker | Health | `GET` | `/health/ready` | As the data plane's |

2. The inventory test composes each process's container registrations and routes, enumerates every endpoint on each listener, and fails on any endpoint that is reachable without authentication and is not listed, and on any listed entry no process maps, naming the process, listener, method, and route. An endpoint is reachable without authentication unless it declares an operator action.
3. The health endpoints stay unauthenticated, on the health listeners only, with their bodies as container contract version 2 records them. The control plane's traffic listener serves no health endpoint.

## Transport

*Review owner: Security owner.*

1. Each traffic listener has a transport mode, the container section's `trafficTransport`: `listener` (in-process TLS), `upstream` (the operator declares TLS terminated in front), or `none` (plain HTTP, `development` only).
2. Outside `development`, the data plane and the control plane each start only with `listener` or `upstream`. Otherwise they exit 2 with `operator.transport_unprotected` and bind no listener.
3. `listener` reads an unencrypted PEM certificate chain and private key from read-only mounted files, once at start, and requires TLS 1.2 or later. A missing, unreadable, malformed, encrypted, not-yet-valid, expired, or mismatched pair exits 2 with `operator.certificate_unusable`, naming the field and never the file's path, content, or key (**unencrypted keys only, decided by the maintainer in `plan` on 2026-11-21**). Rotation is a restart. This contract fixes only the minimum version: cipher suites and every other TLS parameter are the platform's defaults.
4. The health listeners of all three processes stay plain HTTP, protected by being a separate port that is never published beyond the orchestrator's private network. The worker has no traffic listener.
5. **ISK-0007's AV-5 is settled by this contract.** ISK-0019 and ISK-0085 recheck the data plane's transport once credentials cross it.

## Recovery draft

*Review owner: Security owner, with the human approver.*

1. **This is a draft.** The minimum admin recovery and MFA policy needs human approval before the public MVP (spec AV-4, `docs/05` Decision status).
2. A recovery token is issued only on an `initialized` installation, by `bootstrap recover`, with the form, lifetime, single-active rule, delivery, and audit of a bootstrap token.
3. Its trust anchor is the ability to run the control plane's command with its documents and database access, the same as for `migrate`.
4. Redeeming it follows Redemption rules 1 to 8, and creates a new operator principal (Redemption rule 6).

## Audit records

*Review owner: Privacy/Operations owner, with the Security owner for rules 3 and 4.*

1. `IAuditRecorder.AppendAsync` appends one row of `isk_audit.records` in the caller's operator-scope unit, so a record exists if and only if its change committed.
2. Each record holds an `audit` ID, the time, the actor (`anonymous`, `operator_command`, or a principal ID), the target principal when there is one, the action, the scope `operator`, the result, a reason class, the configuration revision, and the correlation ID. A `bootstrap` command run has no request, so it generates one correlation ID for the run, and its records use the revision of the documents it validated. It never holds a token, hash, network address, request body, or configuration value.
3. The runtime login may insert and read records, and may not update, delete, or truncate them.
4. **What is recorded**:

   | Action | Result | Reasons | Actor | When |
   |---|---|---|---|---|
   | `bootstrap_token.issued` | `succeeded` | `none` | `operator_command` | `bootstrap issue` commits |
   | `recovery_token.issued` | `succeeded` | `none` | `operator_command` | `bootstrap recover` commits |
   | `token.superseded` | `succeeded` | `none` | `operator_command` | Once per token an issue supersedes |
   | `bootstrap.redeemed` | `succeeded` or `failed` | `none`, or a reason from Redemption rule 3 after `unknown` | `anonymous` | A bootstrap redemption naming a stored token |
   | `recovery.redeemed` | `succeeded` or `failed` | The same | `anonymous` | A recovery redemption naming a stored token |
   | `enrollment.consumed` | `succeeded` | `none` | The consumer's | An enrollment code is consumed (no consumer in this spec) |

5. A malformed request, a redemption naming no stored token, a limited attempt, and every denied admin request write no record, and are counted by the metrics and events below, so an anonymous flood cannot grow the table.
6. Tenant audit, retention, and tamper-evident ordering are ISK-0026's (spec AV-6).

## Configuration

*Review owner: Security owner.*

Namespace `operator`, version 1, registered only by `AddIskenderOperatorSecurity()`:

| Field | Type | Default | Constraints | Development-only |
|---|---|---|---|---|
| `bootstrap.tokenLifetime` | duration | `1h` | `5m` to `24h`. Also the recovery token's lifetime | No |
| `bootstrap.attemptsPerMinute` | integer | `10` | 1 to 600, per process, across both redemption routes | No |
| `enrollment.codeLifetime` | duration | `24h` | `1h` to `7d` | No |

The transport fields are the container section's (container contract version 3).

## Error codes

*Review owner: Architecture (ADR) owner.*

`core-contracts` 7 adds these codes, all `since: 7`:

| Code | Category | Where |
|---|---|---|
| `operator.redemption_failed` | `permission_denied` | `403`, every failed redemption |
| `operator.redemption_limited` | `limit_exceeded` | `429` |
| `operator.uninitialized` | `permission_denied` | `403`, the gate |
| `operator.state_unavailable` | `internal` | `503`, the state or the redemption's unit cannot be read |
| `operator.authorization_unavailable` | `internal` | `503`, the grants cannot be read, or the resolver threw |
| `operator.already_initialized` | `conflict` | `bootstrap issue`, exit code 8 |
| `operator.not_initialized` | `conflict` | `bootstrap recover`, exit code 9 |
| `operator.route_undeclared` | `internal` | Start, exit code 10 |
| `operator.transport_unprotected` | `invalid_request` | Start, exit code 2 |
| `operator.certificate_unusable` | `invalid_request` | Start, exit code 2 |
| `operator.enrollment_invalid` | `permission_denied` | `IEnrollmentCodes.ConsumeAsync` |

Unauthenticated requests use `identity.unauthenticated`, and privilege denials `identity.auth_denied`. The safe messages are in `ErrorCodes`, and the public contracts and versioning contract's tables list them.

## Diagnostics

*Review owner: Privacy/Operations owner.*

Fields and labels are limited to action, outcome, reason class, purpose, and route template. No token, hash, principal ID, network address, or configuration value appears in any field or label. Category `Iskender.Identity`. Templates and IDs are fixed.

| Event ID | Name | Level | Template |
|---|---|---|---|
| 9001 | `OperatorRedemptionSucceeded` | Information | `Iskender operator redemption succeeded: purpose {Purpose}.` |
| 9002 | `OperatorRedemptionFailed` | Warning | `Iskender operator redemption failed: purpose {Purpose}, reason {Reason}.` |
| 9003 | `OperatorRequestDenied` | Information | `Iskender admin request denied: route {Route}, reason {Reason}.` |
| 9004 | `OperatorTokenIssued` | Information | `Iskender operator token issued: purpose {Purpose}, superseded {Superseded}.` |
| 9005 | `OperatorRouteUndeclared` | Error | `Iskender admin route has no single declaration: route {Route}.` |
| 9006 | `OperatorRedemptionLimited` | Warning | `Iskender operator redemption attempts limited.` |
| 9007 | `OperatorResolutionFailed` | Warning | `Iskender operator principal resolution failed: failure {Failure}.` |
| 9008 | `OperatorStoreUnavailable` | Warning | `Iskender operator store unavailable: route {Route}, code {Code}.` |

1. `{Purpose}` is `bootstrap` or `recovery`. 9002's `{Reason}` is a reason class from Redemption rule 3. 9003's `{Reason}` is `uninitialized`, `unauthenticated`, or `forbidden`. `{Route}` is the route template, never the request path. `{Superseded}` is a count.
2. 9006 is written at most once per minute per process. 9007's `{Failure}` is the exception's type name, never its message.
3. Metrics from `Meter("Iskender.Identity")`:

   | Instrument | Kind | Labels |
   |---|---|---|
   | `iskender.identity.redemptions` | Counter | `purpose`, `outcome`, `reason` |
   | `iskender.identity.admin.denials` | Counter | `route`, `reason` (`limited`, `rejected_body`, `uninitialized`, `unauthenticated`, `forbidden`, `unavailable`) |
   | `iskender.identity.tokens.issued` | Counter | `purpose` |
   | `iskender.identity.tokens.superseded` | Counter | `purpose` |

4. Activities from `ActivitySource("Iskender.Identity")`: `iskender.identity.redeem` and `iskender.identity.authorize`, with the same label vocabulary.
5. The container entry points subscribe the OpenTelemetry SDK to `Iskender.Identity`.

## Test fixture

*Review owner: Performance owner (+ QA).*

1. `tests/Iskender.Identity.Tests` holds the operator fixture, built on the ISK-0004 PostgreSQL fixture: two or more control-plane hosts on one database, the test principal resolver, and a store made unreachable.
2. The concurrency tests run 50 concurrent redemptions across two hosts at least 20 times, with `operator.bootstrap.attemptsPerMinute` set so that no attempt is limited. The limit has its own test.
3. A telemetry canary searches every captured log, trace, metric, error body, readiness answer, audit record, and database row of the run for each token's secret, and for each hash outside the token table, and the logs, traces, and metrics for each principal ID.
4. Tests that need a container runtime follow ISK-0005's rule: skipped with their reason without one, and failing when `CI` is `true`.

## Threat model and known limits

### What the rules defend against

The Negative Security and Failure Scenarios table in the spec lists each threat with the requirement that covers it. This contract's rules implement them: an attacker reaching a fresh control plane first (Admin routes 5 and 6), token reuse and races (Redemption 1 and 2), guessing (Tokens 1, Admin routes 5), an answer that reveals why (Redemption 4), tokens in output or storage (Tokens 2 and 5), stale tokens (Tokens 3 and 4), routes added without protection (Admin routes 2 and 3), unreviewed anonymous endpoints (Anonymous endpoint inventory), unreachable stores (Admin routes 5), role strings (Admin routes 9), operator access to tenants (Principals and grants 3), recovery taking over another operator (Redemption 6), purpose confusion (Redemption 7), lost or edited audit records (Audit records 1 and 3), audit floods (Audit records 5), and plain-HTTP interception (Transport).

### Known limits

- **No sign-in before ISK-0013 or ISK-0014**: an initialized installation's protected routes answer `401` to everyone.
- **No database access before ISK-0021**: every admin route that reaches the store answers `503`, and `bootstrap` exits 5.
- **`upstream` cannot be verified**: the process trusts the operator's declaration that TLS is terminated in front of it.
- **Standard output is the token's only channel**: an orchestrator that collects a `bootstrap` job's standard output puts the token in that collector. The runbook says to run the command attached, read the token from its own terminal, and redeem it at once. Consumption ends a redeemed token's use, and its lifetime bounds an unredeemed one.
- **Response time is not uniform**: a redemption refused before any store read answers sooner than one that reaches the store, so timing can reveal whether an identifier names a stored token. The identifier is 128 random bits and not a secret, and the answer never reveals the reason, so this reveals nothing that helps guess a secret.
- **A known identifier can write failure records**: someone who holds a token's identifier but not its secret can add `wrong_secret` records, bounded by the attempt limit of each process.
- **The attempt limit is per process**: replicas multiply it. The token's 256-bit secret, not the limit, defeats guessing.
- **Recovery's trust anchor is database access**: anyone who can run the control plane's command with its documents can create an operator, as they can already run migrations.
- **An expired enrollment code strands its principal**: recovery creates a new one, and the stranded principal keeps its grant until a later spec removes principals.

## Left to later specs

| Item | Spec |
|---|---|
| Local accounts and the first sign-in method, consuming the enrollment code (spec AV-3) | ISK-0013 |
| External OIDC login and principal linking | ISK-0014 |
| Organization and project roles, memberships, and project-level grants | ISK-0015 |
| Management authentication for automation | ISK-0020 |
| Credential providers, so the stores can connect, and passphrase-protected private keys | ISK-0021 |
| Tenant audit, retention, and tamper-evident ordering (spec AV-6) | ISK-0026 |
| The minimum admin recovery and MFA policy (spec AV-4) | Human approval; ISK-0013 and ISK-0085 |
| Management resource APIs and the management UI | ISK-0080, ISK-0081 |
| The audited elevated operator operation over tenant data | A later spec, still unassigned |
| Hardware-backed or two-person recovery, certificate reload without restart, mutual TLS, distributed limits on anonymous routes | Post-MVP |
