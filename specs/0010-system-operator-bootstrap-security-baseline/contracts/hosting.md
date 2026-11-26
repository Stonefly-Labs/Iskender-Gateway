# Hosting Contract

**Version**: 4

**Supersedes**: version 3, `specs/0009-module-events-background-work-coordination/contracts/hosting.md`, which stays unedited as a completed record, as do version 2, `specs/0007-container-entry-points-process-separation/contracts/hosting.md`, and version 1, `specs/0006-host-composition-di-registration/contracts/hosting.md`.

**Owning spec**: ISK-0006 — Host Composition & DI Registration. Revised by ISK-0007 — Container Entry Points & Process Separation, by ISK-0009 — Module Events & Background Work Coordination, and by ISK-0010 — System-Operator Bootstrap & Security Baseline.

**Review owner**: Architecture (ADR) owner, with the Security owner for the Admin route group, Coexistence, and Static state, and the Privacy/Operations owner for Diagnostics. See `CONTRIBUTING.md` for who holds each role.

This contract states what an application that embeds Iskender, the two container entry points, and the later specs that add modules and ingress may rely on from the composition roots `AddIskender(...)`, `AddIskenderControlPlane(...)`, and `AddIskenderWorker(...)`, from `MapIskender(...)` and `MapIskenderOperatorSecurity(...)`, and from the module and adapter registrations. Version 1's type shapes are in ISK-0006's [data-model.md](../../0006-host-composition-di-registration/data-model.md), version 2's changes in ISK-0007's [data-model.md](../../0007-container-entry-points-process-separation/data-model.md), version 3's in ISK-0009's [data-model.md](../../0009-module-events-background-work-coordination/data-model.md), and version 4's in ISK-0010's [data-model.md](../data-model.md). The reasons are in each spec's research. Module placement and roles follow the [module dependency rules contract](../../0007-container-entry-points-process-separation/contracts/module-dependency-rules.md), version 5. The container entry points follow the [container contract](container.md), version 3. The admin route group's request pipeline, routes, and anonymous endpoint inventory follow the [operator-security contract](operator-security.md), version 1.

## Changing this contract

A change to a rule here is made by a later spec's revision of this contract, which increments the version, publishes the contract in that spec's `contracts/` directory, repoints the links in `docs/03-domain-and-technical-architecture.md` and `docs/04-technology-stack-and-deployment.md`, and records the change in this table.

| Version | Spec | Change | Approval | Owner |
|---|---|---|---|---|
| 1 | ISK-0006 | First version | ISK-0006's PR | Architecture (ADR) owner |
| 2 | ISK-0007 | The plane-neutral composition core moves into `Iskender.Domain`; the control-plane root `AddIskenderControlPlane(...)`; module registrations require a composition root, still named `hosting`; each root names its plane, whose guard the coordination start-up starts; a refused mandatory feature fails start (Start-up rule 7, event 6004); composition start-ups stop after the server stops (Start-up rule 8). No public member of `Iskender.Hosting` changes | ISK-0007's PR | Architecture (ADR) owner |
| 3 | ISK-0009 | A third root, `AddIskenderWorker(...)`, for the worker plane, `CompositionPlane.Worker` (Ownership rule 2, Registration rules 1 and 8); its single-instance lease `instance.worker` (Start-up rule 6.2); the outbox and dispatcher registrations (Registration rule 6); the dispatcher's start-up `work-dispatcher` (Start-up rule 6); the work types' static fields (Static state). No public member of `Iskender.Hosting` changes | ISK-0009's PR, maintainer decision in `plan` on 2026-11-17 | Architecture (ADR) owner |
| 4 | ISK-0010 | `Iskender.Management` owns the operator-security registration and takes ASP.NET Core by framework reference (Ownership rule 4); the modules `operator-security` and `identity-postgresql` (Registration rules 6 and 7); the admin route group `MapIskenderOperatorSecurity(...)` (Admin route group); the route-declaration check before any listener binds (Start-up rule 11); composing the control plane into an embedded host, settling ISK-0006's AV-5 (Embedding the control plane); the `operator` section (Configuration); the identity and audit types' static fields (Static state). No public member of `Iskender.Hosting` changes | ISK-0010's PR, maintainer decisions in `plan` on 2026-11-21 | Architecture (ADR) owner |

`Iskender.Hosting`'s public surface is also versioned by the ISK-0002 compatibility harness, as contract `hosting`. That version and this contract's version are separate: the harness version follows the surface, and this one follows the rules.

## Ownership

*Review owner: Architecture (ADR) owner.*

1. The **composition core** is in `Iskender.Domain`, namespace `Iskender.Domain.Registry.Composition`: the composition sources, once-per-container validation, the requirement check, the composition lifecycle and its start-ups, the refused-feature check, and events 6001 to 6004. It is plane-neutral, and both roots compose it.
2. A **composition root** is the one registration per container that owns the core and names its plane:

   | Root | Assembly | Root module | Plane | Sections |
   |---|---|---|---|---|
   | `AddIskender(...)` | `Iskender.Hosting` | `hosting` | Data plane | `hosting` |
   | `AddIskenderControlPlane(...)` | `Iskender.Management` | `control-plane` | Control plane | None |
   | `AddIskenderWorker(...)` | `Iskender.Management` | `worker` | Worker | None |

3. `Iskender.Hosting` owns `AddIskender(...)`, `MapIskender(...)`, `IskenderOptions`, the `hosting` section, the request bootstrap, and the route group. It references only `Iskender.Domain` and `Iskender.Core.Contracts`, and reaches no infrastructure assembly, adapter, or `Iskender.Management` (rules A5, A8, A10).
4. `Iskender.Management` owns `AddIskenderControlPlane(...)`, `ControlPlaneOptions`, `AddIskenderWorker(...)`, and `WorkerOptions`. Since version 4 it also owns, in namespace `Iskender.Management.Security`, `AddIskenderOperatorSecurity()`, `MapIskenderOperatorSecurity(...)`, the route declaration methods `RequireOperatorAction(...)` and `AllowAnonymousOperatorEndpoint(...)`, `IOperatorPrincipalResolver`, and the `operator` section. It takes the shared framework `Microsoft.AspNetCore.App` by `FrameworkReference`, which rule A19 allows, because its denied categories do not include `aspnetcore`. It still references only `Iskender.Domain` and `Iskender.Core.Contracts`, and reaches no infrastructure assembly (rule A3).
5. Each infrastructure assembly owns the registration method for its own services. The composition seam types, `CompositionModule`, `CompositionRoot`, `CompositionPlane`, `IValidatedConfiguration`, and `ICompositionStartup`, are in `Iskender.Domain.Ports`. `CompositionServices.AddModule` is the seam a registration calls, and `Iskender.Infrastructure.Composition.CompositionRegistration.Add` forwards to it.
6. No root builds, starts, runs, or owns a host. The host calls it in its own code (FR-001).
7. Each type's XML comment names its owning spec: ISK-0006, ISK-0007 for the types version 2 adds or moves, ISK-0009 for the types version 3 adds, or ISK-0010 for the types version 4 adds.

## Registration

*Review owner: Architecture (ADR) owner.*

1. `AddIskender(services, configure)` runs `configure` once, against a fresh `IskenderOptions`, and copies the result. Later changes to that options object have no effect. It returns an `IskenderBuilder` whose `Services` is the same collection. `AddIskenderControlPlane(services, configure)` and `AddIskenderWorker(services, configure)` do the same with `ControlPlaneOptions` and `WorkerOptions`, and return the collection.
2. Each root's options name at most one base document and at most one overlay document, each a file path, text, or stream factory with a source name and a format of `Json` or `Yaml`. `ReadEnvironmentVariables` opts into the process's `ISKENDER__` variables, and is `false` by default. `EnvironmentName` sets Iskender's active environment name, and is `null`, production-grade, by default (TR-2, TR-3).
3. Iskender never reads the host's `IConfiguration`, `IHostEnvironment`, or `IWebHostEnvironment`, never changes the host's configuration, and never derives its environment name from the host's.
4. Precedence is ISK-0003's: defaults, base, overlay, environment variables. There is no further layer, and values built in code reach Iskender only as `FromText` documents (TR-4).
5. Registration reads no document, calls no stream factory, runs no validation, opens no network connection, and reads no secret (FR-011).
6. Module and adapter registrations are explicit extension methods on the host's `IServiceCollection`, called in any order relative to the composition root and to each other. No assembly is scanned. A module is composed only if a call registered it:

   | Method | Assembly | Module | Sections | Requires |
   |---|---|---|---|---|
   | `AddIskender(...)` | `Iskender.Hosting` | `hosting` | `hosting` | |
   | `AddIskenderControlPlane(...)` | `Iskender.Management` | `control-plane` | | |
   | `AddIskenderWorker(...)` | `Iskender.Management` | `worker` | | |
   | `AddIskenderOperatorSecurity()` | `Iskender.Management` | `operator-security` | `operator` | `control-plane`, `identity-postgresql` |
   | `AddIskenderContainer()` | `Iskender.Infrastructure.Container` | `container` | `container` | `hosting` |
   | `AddIskenderCoordination()` | `Iskender.Infrastructure` | `coordination` | `coordination` | `hosting` |
   | `AddIskenderPostgreSqlPersistence()` | `Iskender.Infrastructure.PostgreSql` | `persistence-postgresql` | `persistence` | `hosting` |
   | `AddIskenderPostgreSqlCoordination()` | `Iskender.Infrastructure.PostgreSql` | `coordination-postgresql` | | `coordination`, `persistence-postgresql` |
   | `AddIskenderRedisCoordination()` | `Iskender.Infrastructure.Redis` | `coordination-redis` | | `coordination` |
   | `AddIskenderPostgreSqlWork()` | `Iskender.Infrastructure.PostgreSql` | `work-postgresql` | | `persistence-postgresql` |
   | `AddIskenderPostgreSqlWorkDispatcher()` | `Iskender.Infrastructure.PostgreSql` | `work-dispatcher` | `work` | `work-postgresql`, `coordination-postgresql` |
   | `AddIskenderPostgreSqlIdentity()` | `Iskender.Infrastructure.PostgreSql` | `identity-postgresql` | | `persistence-postgresql`, `work-postgresql` |

7. **The composition-root requirement**: `hosting` in a `Requires` list names the composition root, and any root meets it (`CompositionRoot.Requirement`). Every other name in `Requires` is met only by a module of that name. The name is kept from version 1, so a missing root is still reported as `hosting` (event 6003) in every plane. `control-plane` is such a name: only the control-plane root's own module meets it, so `operator-security` composes under `AddIskenderControlPlane(...)` alone. Under `AddIskender(...)` or `AddIskenderWorker(...)`, `operator-security` fails the requirement check with `hosting.registration_missing` (event 6003).
8. Each root registers one `CompositionRoot` naming its plane. The coordination start-up starts that plane's single-instance guard (Start-up rule 6.2).
9. A later module follows the same pattern: one method in its own assembly, which adds one `CompositionModule` with its sections and requirements through `CompositionServices.AddModule`, or `CompositionRegistration.Add` where the assembly references `Iskender.Infrastructure`, plus its services, and, if it must act at host start, one `ICompositionStartup`.
10. Iskender registers no `TimeProvider`, `ILoggerFactory`, credential provider, or other service a host might own. It resolves them with `GetService`, and falls back to `TimeProvider.System`, `NullLoggerFactory`, and a `null` credential provider.

## Registration mistakes

*Review owner: Architecture (ADR) owner.*

| Mistake | Fails | Code | Event |
|---|---|---|---|
| A composition root called twice on one collection, or two different roots on one collection | At the second call, naming the second root's module | `hosting.registration_duplicate` | None, since no logger exists yet |
| A module registration called twice on one collection | At the second call | `hosting.registration_duplicate` | None |
| Two coordinator registrations of one kind | At the second call | `hosting.registration_duplicate` | None |
| A module registration without a composition root | At host start | `hosting.registration_missing`, naming `hosting` | 6003 |
| A module whose `Requires` names a module not registered | At host start, before validation | `hosting.registration_missing` | 6003 |
| `operator-security` composed under `AddIskender(...)` or `AddIskenderWorker(...)` | At host start, before validation | `hosting.registration_missing` | 6003 |
| `MapIskender(...)` without `AddIskender(...)` | At the call | `hosting.registration_missing` | 6003 |
| `MapIskenderOperatorSecurity(...)` without `AddIskenderOperatorSecurity()` | At the call | `hosting.registration_missing` | 6003 |
| `MapIskenderOperatorSecurity(...)` called more than once, or never, in a host that composes `operator-security` | At host start, before any listener binds | `operator.route_undeclared` | 9005 |
| An endpoint in the admin route group that declares no requirement, or both | At host start, before any listener binds | `operator.route_undeclared` | 9005 |
| A coordinator selected by configuration with no registration | At host start, after validation | `hosting.registration_missing` | 6003 |
| Two modules claiming one section namespace | When the schema registry is built, at host start | `config.schema.duplicate_namespace` | None |

Diagnostics name the module, or the coordinator kind and primitive, or the route template, and never a configuration value.

## Lifetimes

*Review owner: Architecture (ADR) owner.*

| Service | Lifetime | Owner |
|---|---|---|
| `CompositionRoot`, composition sources, schema registry, validated configuration, `ConfigurationSnapshot`, `ConfigurationRevision`, `CompositionStatus`, composition lifecycle | Container singleton | `Iskender.Domain` (the core), registered by the root |
| Hosting settings | Container singleton | `Iskender.Hosting` |
| `GatewayExecutionContext` | Request-scoped, set once by the bootstrap | `Iskender.Hosting` |
| `CoordinationSettings`, local substitutes, `CapabilityReport`, `CoordinationGate`, `SingleInstanceGuard` (as `ISingleInstanceStatus`), `ICoordinationHealthProbe` | Container singleton | `Iskender.Infrastructure` |
| `PostgreSqlSettings`, `PostgreSqlDataSources`, `IUnitOfWorkFactory`, `IMigrationRunner`, `IPersistenceHealthProbe` | Container singleton | `Iskender.Infrastructure.PostgreSql` |
| PostgreSQL coordinator ports and fencing guard | Container singleton, sharing the persistence data sources | `Iskender.Infrastructure.PostgreSql` |
| `RedisCoordinator` and its ports | Container singleton | `Iskender.Infrastructure.Redis` |
| `ContainerSettings`, the container lifecycle, the readiness evaluator | Container singleton | `Iskender.Infrastructure.Container` |
| The outbox, `WorkSettings`, the dispatcher, its start-up, the jobs | Container singleton | `Iskender.Infrastructure.PostgreSql` |
| Work handlers and recurring jobs | Container singleton, created by the dispatcher through the host's container | The module that registers them |

1. Every Iskender service is created by the host's container and disposed by it. None is transient.
2. No singleton captures a scoped service. A host built with `ValidateScopes` and `ValidateOnBuild` set to `true` builds with no error for every combination of the registrations above (FR-007). This includes a combination with a registration mistake, such as a module without `AddIskender(...)` or without a module it requires, so that the mistake is reported at host start with its code (Registration mistakes), never as a container error at build.
3. Disposing the container disposes every disposable Iskender service it created, and nothing Iskender started keeps running (FR-010).
4. `GatewayExecutionContext` is reachable only within its own request. Resolving it outside an Iskender request fails with `core.context.not_resolved` (FR-009).
5. Two containers in one process, whether from two hosts or from building one collection twice, share no Iskender service, snapshot, or state.

## Static state

*Review owner: Security owner.*

No production Iskender type holds a service, configuration value, snapshot, connection, cache, or count in a static or process-wide field (FR-008). A reflection scan of every production assembly under `src/` enforces this. A static field passes only when it is a `const`, belongs to a compiler-generated type, or is `initonly` with a declared type in one of these classes:

| Class | Types |
|---|---|
| Immutable values | Primitives, enums, `string`, `decimal`, `TimeSpan`, `DateTimeOffset`, `Guid` |
| Immutable collections | Types in `System.Collections.Frozen` and `System.Collections.Immutable` |
| Delegates | Any delegate type, such as those `LoggerMessage.Define` returns |
| Telemetry instruments | `ActivitySource`, `Meter`, `Counter<T>`, `UpDownCounter<T>`, `Histogram<T>`, `ObservableCounter<T>`, `ObservableUpDownCounter<T>`, `ObservableGauge<T>`. They hold no values: measurements go to the listeners the host owns (maintainer decision, 2026-11-14) |
| Sentinels | A field of its own sealed declaring type, such as `VersionToken.Initial` |
| Contract registry types | `SectionSchema`, `ErrorCode`, `ResourceType`, `WireContract` |

Any other static field must be listed here, with the reason it holds no per-container state. A field that holds state is fixed, never listed. A field that is not `initonly` is never allowed.

| Field | Reason |
|---|---|
| `Iskender.Domain.Registry.CanonicalWriter.Options` | A `JsonWriterOptions` struct, copied on each use and never written |
| `Iskender.Domain.Registry.CanonicalWriter.Utf8`, `Iskender.Infrastructure.PostgreSql.Migrations.MigrationScript.StrictUtf8` | A `UTF8Encoding` holds only the settings it was built with |
| `Iskender.Domain.Registry.FieldRules.IgnoreAsciiCase` | An `AsciiIgnoreCaseComparer`, which has no fields |
| `Iskender.Core.Contracts.Errors.GatewayError.NoDiagnostics` | An empty `ErrorDiagnostics`, which is immutable |
| `Iskender.Infrastructure.PostgreSql.Classification.Recorded` | A `ConditionalWeakTable` keyed by the `GatewayException` instance it describes. An entry is reachable only through that exception, which belongs to one call, and is collected with it |
| `Iskender.Infrastructure.Redis.RedisScripts` script fields | Each `RedisScript` holds a fixed name and script text, set at construction |
| The backing fields of `Iskender.Core.Contracts.Errors.ErrorCategory.All`, `Iskender.Core.Contracts.Errors.ErrorCodes.All`, `Iskender.Core.Contracts.Identifiers.ResourceTypes.All`, and `Iskender.Core.Contracts.Versioning.WireContracts.All` | Each is a `ReadOnlyCollection<T>` over an array that nothing else references, holding the contract's registry instances. It cannot be changed through the collection or a cast |
| The backing field of `Iskender.Infrastructure.PostgreSql.Coordination.CoordinationMigrations.Module` | A sealed `MigrationModule` holding a fixed name, version range, and dependencies, and a delegate that reads the assembly's embedded scripts on each call |
| `Iskender.Infrastructure.PostgreSql.Coordination.CoordinationTables.Values`, `.Leases`, `.Fences`, and `.CacheGenerations` | Each is a sealed `ScopeKeyedTable` holding a fixed schema and table name |
| The backing field of `Iskender.Infrastructure.PostgreSql.PostgreSqlAdapter.DriverType` | A `System.Type`, which is the driver's type metadata |
| The backing fields of `Iskender.Infrastructure.PostgreSql.Directory.DirectoryMigrations.Module` and `Iskender.Infrastructure.PostgreSql.Work.WorkMigrations.Module` | Each is a sealed `MigrationModule`, as `CoordinationMigrations.Module` is (version 3) |
| `Iskender.Infrastructure.PostgreSql.Directory.DirectoryTables.Scopes`, `Iskender.Infrastructure.PostgreSql.Work.WorkTables.Items` and `.JobRuns` | Each is a sealed `TableAccess` holding a fixed schema and table name (version 3) |
| The backing fields of `Iskender.Domain.Identity.OperatorActions.InstallationRead` and `Iskender.Domain.Identity.OperatorRoles.SystemOperator` | A sealed `OperatorAction` holding a fixed name, and a sealed `OperatorRole` holding a fixed name and a read-only list of those actions (version 4) |
| The backing fields of `Iskender.Domain.Identity.OperatorActions.All` and `Iskender.Domain.Identity.OperatorRoles.All` | Each is a `ReadOnlyCollection<T>` over an array that nothing else references, as `ErrorCodes.All` is (version 4) |
| The backing fields of `Iskender.Infrastructure.PostgreSql.Audit.AuditMigrations.Module` and `Iskender.Infrastructure.PostgreSql.Identity.IdentityMigrations.Module` | Each is a sealed `MigrationModule`, as `CoordinationMigrations.Module` is (version 4) |
| `Iskender.Infrastructure.PostgreSql.Audit.AuditTables.Records`, and `Iskender.Infrastructure.PostgreSql.Identity.IdentityTables.Installation`, `.Principals`, `.OperatorGrants`, and `.Tokens` | Each is a sealed `OperatorTable` holding a fixed schema and table name (version 4) |

The scan's first run in `implement` (T037) corrected the namespaces of four entries, and added the last four rows with their reasons for review. It also found `LocalLeaseCoordinator.s_lastToken`, a process-wide token counter, which holds state, and so was fixed: each coordinator now has its own counter (maintainer decision, 2026-11-14). A field it finds that is not here is either fixed or added with its reason and reviewed.

## Start-up

*Review owner: Architecture (ADR) owner.*

1. Iskender's configuration is validated once per container, through `ConfigurationValidator` with the host's logger, at the first of the host's `StartingAsync`, an entry point's explicit call to the same checks before it starts the host, and the first resolution of a service that needs the snapshot (FR-012). Every Iskender service in the container sees the one resulting snapshot and revision.
2. Before validation, the start checks every module's `Requires`, with `hosting` met by a composition root (Registration rule 7).
3. A rejected configuration makes the host's start fail with `GatewayException` `hosting.configuration_rejected`. Its `ErrorDiagnostics.Text` is ISK-0003's text rendering, and its values are `errorCount` and `reportedCount`. ISK-0003 writes events 3002 and 3003, and then Hosting writes 6002. No snapshot is stored, and every service that needs one fails with the same code (FR-013).
4. A base or overlay document that cannot be read fails start the same way. Its diagnostics name the layer and the source name, say the document could not be read, and hold no content and no exception text. The document is read before ISK-0003's validator is called, so the validator does not run and writes no event 3001 to 3003. The failure counts as one error: its values are `errorCount` 1 and `reportedCount` 1, and Hosting writes 6002 with an `ErrorCount` of 1.
5. A valid configuration writes ISK-0003's 3001, and Hosting writes 6001 once, naming the same revision.
6. Then each registered `ICompositionStartup` runs, in ordinal module-name order. Today `coordination` registers one, and so, in a host that composes the dispatcher, does `work-dispatcher`, which runs after it and whose steps the [work contract](../../0009-module-events-background-work-coordination/contracts/work.md) defines:
   1. A primitive whose configured coordinator is `redis` or `postgresql` with no registration of that kind fails start with `hosting.registration_missing`, naming the kind and the primitive. Nothing below runs.
   2. The single-instance guard starts for the plane the composition root names, holding `instance.data_plane`, `instance.control_plane`, or `instance.worker`, with the selected lease coordinator and `instanceLease.duration`. It connects only with `single_instance` and a PostgreSQL lease coordinator.
   3. ISK-0005's capability check runs once over every registered `FeatureRequirement`, and its results are stored as the container's `CapabilityReport` and logged as events 5001 and 5002.
   4. The container's `CoordinationGate` is built from that report. Before this step, resolving the gate or the report fails with `coordination.capability_unsupported`.
7. **A refused mandatory feature fails start** (replaces version 1's rule, which left the mapping to ISK-0007). After every start-up has run, the first refused feature with `MandatoryPath`, in registration order, for any of the five refusal reasons, makes the host's start fail with `GatewayException` `coordination.capability_unsupported`. Its diagnostic values are `feature`, `module`, and `reason`. Hosting writes event 6004, after ISK-0005's 5002, and stops the start-ups already started, in reverse order, so a held lease is released. A refused feature without `MandatoryPath` does not fail start, and every call through it fails closed. This holds for an embedded host built with `AddIskender(...)`, whose start throws, and for every container entry point, the worker included, which exit 4 (ISK-0007 TR-7).
8. At host stop, after the server and every other hosted service have stopped (`IHostedLifecycleService.StoppedAsync`), each `ICompositionStartup.StopAsync` runs in reverse order. The guard stops its timer and releases its lease. Stopping after the server means requests in flight finish before the lease is released.
9. A stop or cancellation during validation stores nothing and leaves nothing running.
10. Migrations never run at start. Health probes are never called at start.
11. **The operator route-declaration check** (version 4): in a host that composes `operator-security`, an `IHostedLifecycleService` that `AddIskenderOperatorSecurity()` registers enumerates every `EndpointDataSource` in its `StartingAsync`. That runs before any hosted service's `StartAsync`, and so before any listener binds. It fails the host's start with `operator.route_undeclared`, and event 9005 naming the route template, when an endpoint in the admin route group declares no requirement or both, or when `MapIskenderOperatorSecurity(...)` was called more than once or never. It is not an `ICompositionStartup`, so it does not wait for rule 6, and no request reaches an undeclared route. The container maps the code to an exit code, as the [container contract](container.md) sets out.

## Route group and request bootstrap

*Review owner: Architecture (ADR) owner.*

1. `MapIskender(prefix)` maps one route group under `prefix` and returns it, so the host can add conventions. A `null` or empty prefix, or one that does not start with `/`, fails with `core.value.invalid`. It may be called more than once, with different prefixes. Every group shares the container's one snapshot.
2. Every endpoint in a group first runs the request bootstrap, which creates the request's correlation context and `GatewayExecutionContext` and resolves its configuration revision.
3. **Correlation**: the gateway generates the `req_` ID, which is authoritative. A caller value is read from the `X-Correlation-ID` request header and kept only if it is 1 to 128 characters of `A-Z`, `a-z`, `0-9`, `.`, `_`, `:`, and `-`. Otherwise it is dropped (ISK-0002, Request context).
4. **Deadline**: the request's start plus `hosting.requestTimeout`, from the container's `TimeProvider`. **Cancellation**: the request's `RequestAborted`. **Trace**: the current activity's trace and span IDs, or new random IDs when there is none.
5. Until an ingress spec adds endpoints, the group's only endpoint is a catch-all at the lowest route precedence. It answers 404 with the ISK-0002 `error-envelope` for `core.resource.not_found`, as `application/json`, carrying the request's gateway correlation ID.
6. Later specs add endpoints to the group without changing `MapIskender(...)`'s signature (AV-4). Mandatory authentication, when the identity specs add it, is attached to the group itself by `MapIskender(...)`, so host middleware cannot remove or replace it (FR-016).

## Admin route group

*Review owner: Security owner.*

1. `MapIskenderOperatorSecurity(this IEndpointRouteBuilder endpoints, string prefix)` maps the admin routes once, in one route group under a prefix the host chooses. The container calls it with `/admin/v1`.
2. Called without `AddIskenderOperatorSecurity()`, it fails at the call with `hosting.registration_missing`, as `MapIskender(...)` does without `AddIskender(...)`. Called a second time, or never, in a host that composes `operator-security`, the host's start fails with `operator.route_undeclared` (Start-up rule 11).
3. The group's last convention, through `RouteGroupBuilder.Finally`, wraps the `RequestDelegate` of every endpoint in the group, however it was mapped, so no endpoint in the group escapes the admin gate. It reads each endpoint's declaration, which is exactly one of `RequireOperatorAction(...)` or `AllowAnonymousOperatorEndpoint(...)`. An endpoint with neither, or both, fails start (Start-up rule 11).
4. A path under the prefix that matches no route answers `404` with no body, so it reveals nothing and is not an endpoint. Unlike the catch-all of `MapIskender(...)`, it carries no envelope.
5. The request pipeline the wrapper runs, the routes and their answers, the principal resolution, and the anonymous endpoint inventory are owned by the [operator-security contract](operator-security.md). This contract owns only the mapping method, its registration check, and the start-time declaration check.

## Coexistence

*Review owner: Security owner.*

1. Iskender adds no middleware, `IStartupFilter`, exception handler, authentication or authorization scheme, CORS policy, logging provider, metric exporter, trace exporter, or fallback route outside its groups, the admin route group included, and configures no host options type (FR-014, FR-021).
2. A request outside every Iskender prefix, the admin prefix included, runs no Iskender code (FR-015).
3. Host middleware registered before the endpoints runs for Iskender's requests as for any other endpoint.
4. A prefix of `/`, or one that overlaps a host route, is allowed. ASP.NET Core's routing precedence then decides: the host's more specific routes keep matching, and Iskender's catch-all answers the rest under its prefix.
5. These rules bind the composition roots and the module registrations. Iskender's own container entry points are hosts: they own their pipeline, listeners, logging provider, exporter, and host options, as the [container contract](container.md) sets out, and `AddIskenderContainer()` adds only its module, section, and container services, which only those entry points call.

## Embedding the control plane

*Review owner: Architecture (ADR) owner.*

**This settles ISK-0006's AV-5.**

1. Control-plane services compose into an embedded host through the same pattern as the data plane's: under `AddIskenderControlPlane(...)` on the host's own service collection, with `AddIskenderOperatorSecurity()`, the adapters, including `AddIskenderPostgreSqlPersistence()`, `AddIskenderPostgreSqlWork()`, and `AddIskenderPostgreSqlIdentity()`, and `MapIskenderOperatorSecurity(...)` under the host's own prefix.
2. `AddIskenderPostgreSqlWork()` is needed because `identity-postgresql` requires `work-postgresql`, for migration order only (the [persistence contract](persistence.md), version 4).
3. An embedding host registers its own `IOperatorPrincipalResolver`, when it has one, on the same collection, with any lifetime: the pipeline resolves it from each request's services, so a scoped resolver gets the request's scope (CONV-0010-011). With none registered, every protected admin route answers as the [operator-security contract](operator-security.md) sets out.
4. Control-plane services never compose beside `AddIskender(...)` on one collection, because two roots on one collection fail with `hosting.registration_duplicate` (Registration mistakes). An application that embeds both planes runs two hosts, each with its own collection and container, which share nothing (Lifetimes rule 5).
5. Under `AddIskender(...)` or `AddIskenderWorker(...)`, `operator-security` fails start with `hosting.registration_missing` (Registration rule 7), so the admin surface never composes into the data plane or the worker.

## Configuration

*Review owner: Architecture (ADR) owner.*

The namespace `hosting`, version 1, registered by `Iskender.Hosting`:

| Field | Type | Default | Constraints | Development-only |
|---|---|---|---|---|
| `requestTimeout` | `duration` | `100s` | `1s` to `1h` | No |

The namespace `operator`, version 1, is registered only by `AddIskenderOperatorSecurity()`, in `Iskender.Management`. Its fields are owned by the [operator-security contract](operator-security.md). The data plane and the worker never compose `operator-security`, so they reject a document with an `operator` section with `config.namespace.unknown`.

## Error codes

*Review owner: Architecture (ADR) owner.*

Registered in `ErrorCodes`, `since: 5`, and listed in the ISK-0002 contract's Error codes table.

| Code | Category | Meaning | Safe message |
|---|---|---|---|
| `hosting.configuration_rejected` | `internal` | Host start failed because Iskender's configuration was rejected or a configuration document could not be read | Iskender could not start because its configuration was rejected. |
| `hosting.registration_duplicate` | `internal` | `AddIskender(...)` or one module registration was called twice on one service collection | An Iskender registration was added more than once. |
| `hosting.registration_missing` | `internal` | A module registration or `MapIskender(...)` was used without `AddIskender(...)`, a module was registered without a module it requires, or a coordinator selected by configuration has no registration | A required Iskender registration is missing. |

Version 2 adds no code. The registered meanings, which name `AddIskender(...)`, stay as `core-contracts` registered them, and cover `AddIskenderControlPlane(...)` in the same way: a second root is a duplicate registration, and a module with no root is a missing one. A refused mandatory feature uses ISK-0005's `coordination.capability_unsupported`.

Version 4 adds no `hosting.*` code. `hosting.registration_missing` covers `operator-security` composed outside the control plane, and `MapIskenderOperatorSecurity(...)` called without its registration, in the same way. ISK-0010 registers the `operator.*` codes, `since: 7`, raising `core-contracts` from 6 to 7 with compatible changes only. The one a hosting rule names is `operator.route_undeclared`, at start (Start-up rule 11). The [operator-security contract](operator-security.md) lists them all.

## Diagnostics

*Review owner: Privacy/Operations owner.*

Written through the host's `ILoggerFactory`, category `Iskender.Hosting`. Templates and event IDs are fixed.

| Event ID | Name | Level | Template |
|---|---|---|---|
| 6001 | `IskenderComposed` | Information | `Iskender composed: modules {Modules}, sections {Sections}, environment {Environment}, revision {Revision}.` |
| 6002 | `IskenderStartRefused` | Error | `Iskender start refused: configuration rejected with {ErrorCount} error(s).` |
| 6003 | `IskenderRegistrationMissing` | Error | `Iskender registration missing: {Missing} required by {RequiredBy}.` |
| 6004 | `IskenderMandatoryFeatureRefused` | Error | `Iskender start refused: mandatory feature {Feature} of module {Module} refused with reason {Reason}.` |

1. `{Modules}` is the module names joined by `,`, and `{Sections}` the `name@version` pairs, each in ordinal order. `{Environment}` is the name, or `(none)`. `{Missing}` is a module name, `hosting` for a missing composition root, or `<kind> coordinator for <primitive>`. `{RequiredBy}` is a module name, `configuration`, `MapIskender`, or `MapIskenderOperatorSecurity`. `{Reason}` is one of ISK-0005's five refusal reason codes.
2. The events are written by the composition core in `Iskender.Domain`, under this category in both planes, so a control-plane process writes `Iskender.Hosting` events too.
3. No event, exception, or diagnostic holds a configuration value, a secret reference name, a connection detail, or a route value from a request.
4. ISK-0003's events 3001 to 3003 and ISK-0005's 5001 to 5011 are written as those contracts define. ISK-0010's events 9001 to 9008, category `Iskender.Identity`, 9005 included, are written as the [operator-security contract](operator-security.md) defines.
5. A duplicate registration writes no event, because it fails before any container exists (maintainer decision, 2026-11-14).

## Public surface

*Review owner: Architecture (ADR) owner.*

1. `Iskender.Hosting` is a `public contract` project, contract `hosting`, version 1, with its baseline in `tests/Iskender.Contracts.Compatibility.Tests/baselines/hosting.surface.txt`. Every public type carries `[ContractVersion("hosting", <since>)]` (FR-017).
2. No public member exposes an `Iskender.Domain` type (FR-018). The surface holds only Hosting's own types, .NET types, and ASP.NET Core and `Microsoft.Extensions` types.
3. The registration methods in `Iskender.Infrastructure` and its adapters, `AddIskenderControlPlane(...)`, `ControlPlaneOptions`, `AddIskenderWorker(...)`, `WorkerOptions`, `AddIskenderOperatorSecurity()`, `MapIskenderOperatorSecurity(...)`, the route declaration methods, and `IOperatorPrincipalResolver` in `Iskender.Management`, the seam types in `Iskender.Domain.Ports`, and the core in `Iskender.Domain.Registry.Composition`, are embeddable, outside the harness, and prerelease under ADR-0030 (AV-3).
4. Version 2 changes no public member of `Iskender.Hosting`, so the harness's `hosting` version and baseline do not change. The one behavior change, an embedded start failing on a refused mandatory feature (Start-up rule 7), adds no type, member, or error code. Version 3 changes no public member either, and nor does version 4: the harness's `hosting` version and baseline stay as they are.

## Test fixture

*Review owner: Performance owner (+ QA).*

1. `tests/Iskender.Hosting.Testing` provides `EmbeddedHost`, which builds a host on the in-memory test server, with container validation on and a captured logger, from the hosting sample or from given registrations and documents. It starts no process and opens no port.
2. The acceptance tests of ISK-0006's User Stories 1 to 3 run through it. ISK-0007 reuses it for the cross-host lifecycle test, through the sample and through the data plane's registrations.
3. The single-instance lease test needs a container runtime, and follows ISK-0005's rule: skipped with its reason without one, and failing instead when `CI` is `true`. Every other hosting test needs no container.
4. ISK-0010's embedding test composes the control-plane root with the operator registrations and the adapters of Embedding the control plane rule 1 in an `EmbeddedHost`, maps the admin routes, and runs ISK-0010's User Stories 1 to 3 against it. Composing `AddIskenderOperatorSecurity()` under `AddIskender(...)` or `AddIskenderWorker(...)` fails start with `hosting.registration_missing`. The test needs PostgreSQL, so it follows rule 3 for a container runtime.

## Threat model and known limits

### What the rules defend against

The Negative Security and Failure Scenarios tables in ISK-0006's [spec.md](../../0006-host-composition-di-registration/spec.md) ISK-0007's [spec.md](../../0007-container-entry-points-process-separation/spec.md), ISK-0009's [spec.md](../../0009-module-events-background-work-coordination/spec.md), and ISK-0010's [spec.md](../spec.md) list each threat with the requirement that covers it. This contract's rules implement them: a partly valid configuration (Start-up 1 to 4), values in failures (Diagnostics 2), a host setting relaxing Iskender's defaults (Registration 2, 3), Iskender changing the host (Coexistence), cross-request context (Lifetimes 4), shared state between containers (Lifetimes 5, Static state), unrequested adapters (Registration 6), connections at registration (Registration 5), a lease left held (Start-up 8), a silent second registration (Registration mistakes), a mandatory feature running where it cannot meet its minimums (Start-up 7), one plane holding another's instance lease (Registration 8, Start-up 6.2), the admin surface in the data plane or the worker (Registration 7, Embedding the control plane 5), and an admin route left unprotected by omission (Start-up 11, Admin route group 3).

### Known limits

- **Telemetry instruments are process-wide**: two containers in one process write to instruments with the same names, so an exporter sees both containers' measurements together. Telling them apart needs a later tag or per-container meter, which several gateways in one container, post-MVP, would bring.
- **Composition start-ups run after the server starts**: validation runs in `StartingAsync`, before any `StartAsync`, but the `ICompositionStartup` steps run in Iskender's `StartAsync`, which the host runs after the server's own, since the host registers its server first. A request that arrives in between finds the coordination gate and report fail-closed (Start-up 6.4), and a missing coordinator then still fails the host's start. The operator route-declaration check is not one of them: it runs in `StartingAsync` (Start-up 11).
- **Order-dependent checks run at start**: a missing registration is found when the host starts, not when the container is built, because registrations may come in any order and the container runs no Iskender code when it is built.
- **No parameter injection of the context**: the request bootstrap is an endpoint filter on the group (Route group 2), and minimal APIs bind a route handler's service parameters before the group's endpoint filters run. An endpoint in an Iskender group that takes `GatewayExecutionContext` as a parameter therefore fails with `core.context.not_resolved` (Lifetimes 4). It resolves the context from `HttpContext.RequestServices` inside the handler instead. A later ingress spec that wants parameter injection decides how, for example with a group convention that wraps the request delegate.
- **Who can compose this way**: since version 2, any root meets the `hosting` requirement and the seam is in `Iskender.Domain`, so the control-plane host composes through the same registrations. Provider adapters and `Iskender.Policy.Sdk` may reference neither `Iskender.Domain` nor `Iskender.Infrastructure`, so they still cannot add a module. A spec that needs one to revises this contract first.
- **The requirement name**: `hosting` names the composition root in both planes, so a control-plane process that forgot its root reports `hosting` missing. The name was kept so that version 1's events and diagnostics do not change.
- **The catch-all under `/`**: an Iskender group mapped at `/` answers every request no host route matches, with Iskender's 404 envelope instead of the host's own fallback.
- **Reflection scan**: the static-state scan reads declared fields. State reached through a static method's local captured in a non-generated type, or held by a host-supplied object, is caught by review.
- **Stream factories**: a stream factory the host supplies runs inside Iskender's validation. A factory that blocks delays start, and its exception is reported without its text.

## Left to later specs

| Item | Spec |
|---|---|
| Authentication inside the route group | ISK-0011 onward |
| Protocol endpoints in the route group | ISK-0029 onward |
| A compatibility commitment for embeddable registrations | ISK-0086 (AV-3) |
