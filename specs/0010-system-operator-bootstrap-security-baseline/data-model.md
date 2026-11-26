# Data Model: ISK-0010 — System-Operator Bootstrap & Security Baseline

**Spec**: [spec.md](spec.md) | **Research**: [research.md](research.md) | **Contract**: [operator-security contract](contracts/operator-security.md)

Type names are the interface this spec defines. Member lists show what later specs and tests rely on, not every private detail. Each port and adapter type's XML comment names ISK-0010 as its owning spec (FR-042).

## Identity ports (`Iskender.Domain.Identity`)

These use only `Iskender.Domain.Ports` and `Iskender.Core.Contracts` (rule A17), and name no ASP.NET Core, driver, or Telemetry type.

| Type | Shape | Rules |
|---|---|---|
| `InstallationStatus` (sealed record) | `IsInitialized`, `InitializedAt` (`DateTimeOffset?`) | `InitializedAt` is set exactly when `IsInitialized` |
| `IInstallationState` | `ValueTask<InstallationStatus> ReadAsync(CancellationToken)` | Opens its own operator-scope unit. Throws the persistence failure when it cannot read. Never caches |
| `OperatorTokenPurpose` (enum) | `Bootstrap`, `Recovery`, `Enrollment` | Codes `bootstrap`, `recovery`, `enrollment` |
| `OperatorTokenText` (sealed class) | `Purpose`, `TokenId`; `static bool TryParse(string? text, out OperatorTokenText token)` | Parses research R4's shape only. Holds the secret's bytes privately. `ToString()` is `<purpose> token <id>` and never the secret |
| `IssuedOperatorToken` (sealed class) | `Purpose`, `TokenId`, `ExpiresAt`, `string Reveal()` | `Reveal()` returns the full text once and throws `core.internal` on a second call. `ToString()` never holds the secret |
| `IOperatorTokens` | `ValueTask<IssuedOperatorToken> IssueAsync(OperatorTokenPurpose purpose, OperatorRequestContext context, CancellationToken)` | `Bootstrap` or `Recovery` only, otherwise `core.value.invalid`. Fails with `operator.already_initialized` or `operator.not_initialized` (research R6) |
| `OperatorRequestContext` (sealed record) | `CorrelationId` (`RequestCorrelationId`), `ConfigurationRevision`, `Actor` (`OperatorActor`) | What every audited operation records |
| `OperatorActor` (sealed record) | `Kind` (`Anonymous`, `OperatorCommand`, `Principal`), `Principal` (`ResourceId?`) | `Principal` is set exactly when `Kind` is `Principal` |
| `RedemptionOutcome` (sealed class) | `Succeeded`, `Principal` (`ResourceId?`), `EnrollmentCode` (`IssuedOperatorToken?`), `FailureReason` (`RedemptionFailureReason?`) | A success has a principal and a code. A failure has neither |
| `RedemptionFailureReason` (enum) | `Malformed`, `Unknown`, `WrongSecret`, `WrongPurpose`, `Consumed`, `Superseded`, `Expired`, `AlreadyInitialized`, `NotInitialized` | Codes as the operator-security contract's reason classes. Only the reasons after `Unknown` are audited (FR-028) |
| `IOperatorRedemption` | `ValueTask<RedemptionOutcome> RedeemAsync(OperatorTokenPurpose route, OperatorTokenText token, OperatorRequestContext context, CancellationToken)` | The unit of research R5. Throws the persistence failure on an unreadable or unknown commit |
| `IEnrollmentCodes` | `ValueTask ConsumeAsync(IUnitOfWork unit, ResourceId principal, OperatorTokenText code, OperatorRequestContext context, CancellationToken)` | In the caller's operator-scope unit. Fails with `operator.enrollment_invalid` unless the code is an unconsumed, unexpired `enrollment` code bound to `principal`. Writes `enrollment.consumed` (research R11) |
| `OperatorAction` (sealed record) | `Name` | The operation pattern (`PortNames.RequireOperation`). `OperatorActions.InstallationRead` is `installation.read` |
| `OperatorRole` (sealed record) | `Name`, `Actions` | `OperatorRoles.SystemOperator` is `system_operator`, holding every action in `OperatorActions.All` |
| `OperatorDecision` (enum) | `Allowed`, `Denied` | |
| `IOperatorAuthorization` | `ValueTask<OperatorDecision> DecideAsync(ResourceId principal, OperatorAction action, CancellationToken)` | Reads the grants on every call. Throws the persistence failure when it cannot read (FR-015) |

## Audit port (`Iskender.Domain.Telemetry`)

| Type | Shape | Rules |
|---|---|---|
| `AuditRecord` (sealed record) | `Actor` (`AuditActor`), `TargetPrincipal` (`ResourceId?`), `Action` (`AuditAction`), `Result` (`AuditResult`), `Reason` (`AuditReason`), `ConfigurationRevision`, `CorrelationId` | `Scope` is always `operator` in this spec. There is no field for a token, hash, address, or body (FR-027) |
| `AuditActor` (sealed record) | `Kind` (`Anonymous`, `OperatorCommand`, `Principal`), `Principal` (`ResourceId?`) | Stored as `anonymous`, `operator_command`, or the principal's ID |
| `AuditAction` (enum) | `BootstrapTokenIssued`, `RecoveryTokenIssued`, `TokenSuperseded`, `BootstrapRedeemed`, `RecoveryRedeemed`, `EnrollmentConsumed` | Codes in the operator-security contract's audit table |
| `AuditResult` (enum) | `Succeeded`, `Failed` | |
| `AuditReason` (enum) | `None` and the audited `RedemptionFailureReason` values | `None` exactly when `Succeeded` |
| `IAuditRecorder` | `ValueTask AppendAsync(IUnitOfWork unit, AuditRecord record, CancellationToken)` | Writes in `unit`'s transaction only, and only in an operator-scope unit, otherwise `persistence.scope_not_permitted`. An invalid record fails with `core.value.invalid` before any statement |

`OperatorActor` and `AuditActor` have the same shape but live in different modules, because rule A17 forbids `Iskender.Domain.Identity` from using a Telemetry type. The PostgreSQL adapter maps one to the other.

## Management types (`Iskender.Management.Security`, `Iskender.Management.Bootstrap`)

| Type | Shape | Rules |
|---|---|---|
| `OperatorSecurityServiceCollectionExtensions` | `AddIskenderOperatorSecurity(this IServiceCollection)` | Adds module `operator-security`: section `operator`, requires `control-plane` and `identity-postgresql`. A second call fails with `hosting.registration_duplicate` |
| `OperatorSecurityEndpointRouteBuilderExtensions` | `RouteGroupBuilder MapIskenderOperatorSecurity(this IEndpointRouteBuilder, string prefix)` | Maps research R7's routes once. The prefix is `/` followed by 1 to 64 characters of `a-z`, `0-9`, `-`, and `/`, otherwise `core.value.invalid` |
| `OperatorEndpointConventions` | `RequireOperatorAction(this IEndpointConventionBuilder, OperatorAction)`, `AllowAnonymousOperatorEndpoint(this IEndpointConventionBuilder, string inventoryEntry)` | Exactly one per endpoint in the group (research R9) |
| `IOperatorPrincipalResolver` | `ValueTask<OperatorPrincipalResolution> ResolveAsync(HttpContext context, CancellationToken)` | Implemented by ISK-0013, ISK-0014, and ISK-0020. None ships here |
| `OperatorPrincipalResolution` (readonly record struct) | `Principal` (`ResourceId?`); `Unresolved` | |
| `OperatorSettings` (sealed class) | `TokenLifetime`, `AttemptsPerMinute`, `EnrollmentCodeLifetime` | Read once from the `operator` section |
| `OperatorSectionSchema` (static) | `Schema` | Namespace `operator`, version 1 (research R16) |
| `OperatorRouteCheck` (internal) | `IHostedLifecycleService` | Research R9 |
| `AdminPipeline` (internal) | Wraps each endpoint's delegate | Research R8 |
| `BootstrapArguments`, `BootstrapOperation` | `Parse(IReadOnlyList<string>)` returns `Issue`, `Recover`, or `null` | `bootstrap issue` and `bootstrap recover` only |
| `BootstrapCommand` | `static ValueTask<BootstrapOutcome> RunAsync(Func<ServiceProvider> buildServices, BootstrapOperation, ILoggerFactory, TextWriter standardOutput, TextWriter standardError, CancellationToken)` | Research R6 and R14. Writes only the token to `standardOutput` |
| `BootstrapOutcome` (readonly record struct) | `ExitCode`, `Code` (`ErrorCode?`) | |

## PostgreSQL adapter types

| Type | Namespace | Role |
|---|---|---|
| `IdentityTables` (internal) | `.Identity` | `OperatorTable`s for `installation`, `tokens`, `principals`, and `operator_grants` |
| `IdentityMigrations.Module` | `.Identity` | Migration module `identity`, version 1, depends on `audit`, range 1 to 1 |
| `PostgreSqlInstallationState`, `PostgreSqlOperatorTokens`, `PostgreSqlOperatorRedemption`, `PostgreSqlEnrollmentCodes`, `PostgreSqlOperatorAuthorization` (internal) | `.Identity` | The Identity ports |
| `OperatorTokenHash` (internal) | `.Identity` | The hash of research R4 and its constant-time check |
| `AuditTables` (internal) | `.Audit` | The `OperatorTable` `records` |
| `AuditMigrations.Module` | `.Audit` | Migration module `audit`, version 1, depends on `work`, range 1 to 1 |
| `PostgreSqlAuditRecorder` (internal) | `.Audit` | `IAuditRecorder` |
| `PostgreSqlServiceCollectionExtensions.AddIskenderPostgreSqlIdentity()` | (root) | Module `identity-postgresql`, requires `persistence-postgresql` and `work-postgresql`. Adds the ports above and both migration modules |

## Container types (`Iskender.Infrastructure.Container`)

| Type | Change |
|---|---|
| `ContainerSectionSchema` | Version 2: `trafficTransport`, `tlsCertificateFile`, `tlsKeyFile` (research R13) |
| `TrafficTransport` (enum) | `Listener`, `Upstream`, `None` |
| `ContainerSettings` | `TrafficTransport`, `TlsCertificate` (`X509Certificate2?`), `TlsCertificateChain` (`X509Certificate2Collection`, the file's certificates after the first, sent with it; CONV-0010-005). `From(section, environmentName, hasTrafficListener)` runs the transport and certificate checks |
| `ExitCodes` | `AlreadyInitialized = 8`, `NotInitialized = 9`, `RouteUndeclared = 10` |
| `ContainerEntryPoint` | Binds TLS in `listener` mode; maps the new codes; event 7008 |
| `Health/HealthEndpoints.Routes` | The health listener's fixed route list, which the handler serves from and the inventory test reads (research R19) |
| `Telemetry/ContainerTelemetry` | Adds `Iskender.Identity` |

## Core contracts (`core-contracts` 7)

| Item | Change |
|---|---|
| `ErrorCodes` | The 11 `operator.*` codes of research R17, `since: 7` |
| `ResourceTypes.Principal` | `principal`, prefix `prin`, `since: 7` |
| `ContractPackage` | `core-contracts` 7 |

## Database objects

All are operator tables (persistence contract version 4, Isolation rule 6).

### `isk_identity.installation`

| Column | Type | Constraint |
|---|---|---|
| `singleton` | `boolean` | Primary key, default `true`, `check (singleton)` |
| `state` | `text not null` | `check (state = 'initialized')` |
| `initialized_at` | `timestamptz not null` | |
| `initialized_by` | `text not null` | A `principal` ID |

`iskender_runtime` has `SELECT` and `INSERT` only.

### `isk_identity.tokens`

| Column | Type | Constraint |
|---|---|---|
| `token_id` | `text` | Primary key, 26 characters |
| `purpose` | `text not null` | `bootstrap`, `recovery`, or `enrollment` |
| `secret_hash` | `bytea not null` | `octet_length = 32` |
| `principal_id` | `text` | References `principals`. Not null exactly when `purpose = 'enrollment'` |
| `issued_at`, `expires_at` | `timestamptz not null` | `expires_at > issued_at` |
| `consumed_at`, `superseded_at` | `timestamptz` | Not both set |

Partial unique indexes: `(purpose)` where `purpose in ('bootstrap', 'recovery') and consumed_at is null and superseded_at is null`, and `(principal_id)` where `purpose = 'enrollment' and consumed_at is null and superseded_at is null`.

### `isk_identity.principals`

| Column | Type | Constraint |
|---|---|---|
| `principal_id` | `text` | Primary key |
| `kind` | `text not null` | `check (kind = 'operator')` |
| `created_at` | `timestamptz not null` | |
| `created_through` | `text not null` | `bootstrap` or `recovery` |

### `isk_identity.operator_grants`

| Column | Type | Constraint |
|---|---|---|
| `principal_id` | `text not null` | References `principals` |
| `role` | `text not null` | `check (role = 'system_operator')` |
| `granted_at` | `timestamptz not null` | |

Primary key `(principal_id, role)`.

### `isk_audit.records`

| Column | Type | Constraint |
|---|---|---|
| `audit_id` | `text` | Primary key, an `audit` ID |
| `recorded_at` | `timestamptz not null` | Default `statement_timestamp()`, indexed |
| `actor` | `text not null` | `anonymous`, `operator_command`, or a `principal` ID |
| `target_principal` | `text` | |
| `action` | `text not null` | The contract's action list |
| `scope` | `text not null` | `check (scope = 'operator')` |
| `result` | `text not null` | `succeeded` or `failed` |
| `reason` | `text not null` | The contract's reason list |
| `configuration_revision` | `text not null` | |
| `correlation_id` | `text not null` | |

`iskender_runtime` has `SELECT` and `INSERT` only.

## State transitions

**Installation**: no row (`uninitialized`) → one row (`initialized`), only through a successful bootstrap redemption. No transition back.

**Token**:

```text
issued ──redeemed (success)──────────► consumed
  │
  ├──a newer token of its purpose─────► superseded
  │
  └──expires_at passes────────────────► expired (no marker; compared at redemption)
```

A failed redemption changes no token. An enrollment code is never superseded in this spec, because each principal gets one.

**Admin request** (research R8): limited → body rejected → gate denied (`uninitialized` or unavailable) → unauthenticated → unavailable → forbidden → handled. The first state that applies answers.

## Validation summary

| Rule | Where it is enforced | Failure |
|---|---|---|
| Token shape | `OperatorTokenText.TryParse` | The uniform `403`, no read |
| Body content type, size, and shape | `AdminPipeline` | The uniform `403`, no read |
| Attempts per minute | `AdminPipeline`'s limiter | `429` `operator.redemption_limited` |
| One active bootstrap or recovery token | Supersede in `IssueAsync`, and the partial unique index | Supersede, never a duplicate |
| State only moves forward | Revoked `UPDATE`, `DELETE`, and `TRUNCATE`; insert-only code | The database refuses |
| Audit is append-only | Revoked `UPDATE`, `DELETE`, and `TRUNCATE` | The database refuses |
| Every admin route declares exactly once | `OperatorRouteCheck` | Start fails, exit code 10 |
| Transport outside `development` | `ContainerSettings.From` | Exit code 2, `operator.transport_unprotected` |
| Certificate and key usable | `ContainerSettings.From` | Exit code 2, `operator.certificate_unusable` |
| `operator` section only on the control plane | ISK-0003 registry | `config.namespace.unknown` |
| Settings ranges | `OperatorSectionSchema` | `config.field.out_of_range` |
