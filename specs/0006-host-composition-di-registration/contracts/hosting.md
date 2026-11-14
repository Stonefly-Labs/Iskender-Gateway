# Hosting Contract

**Version**: 1

**Owning spec**: ISK-0006 — Host Composition & DI Registration

**Review owner**: Architecture (ADR) owner, with the Security owner for Coexistence and Static state, and the Privacy/Operations owner for Diagnostics. See `CONTRIBUTING.md` for who holds each role.

This contract states what an application that embeds Iskender, and the later specs that add modules and ingress, may rely on from `AddIskender(...)`, `MapIskender(...)`, and the module and adapter registrations. The type shapes are in [data-model.md](../data-model.md), and the reasons in [research.md](../research.md). Module placement and roles follow the [module dependency rules contract](module-dependency-rules.md), version 4.

## Changing this contract

A change to a rule here is made by a later spec's revision of this contract, which increments the version, publishes the contract in that spec's `contracts/` directory, repoints the links in `docs/03-domain-and-technical-architecture.md` and `docs/04-technology-stack-and-deployment.md`, and records the change in this table.

| Version | Spec | Change | Approval | Owner |
|---|---|---|---|---|
| 1 | ISK-0006 | First version | This spec's PR | Architecture (ADR) owner |

`Iskender.Hosting`'s public surface is also versioned by the ISK-0002 compatibility harness, as contract `hosting`. That version and this contract's version are separate: the harness version follows the surface, and this one follows the rules.

## Ownership

*Review owner: Architecture (ADR) owner.*

1. `Iskender.Hosting` owns `AddIskender(...)`, `MapIskender(...)`, the runtime options, start-up validation, the request bootstrap, and the route group. It references only `Iskender.Domain` and `Iskender.Core.Contracts`, and reaches no infrastructure assembly, adapter, or `Iskender.Management` (rules A5, A8, A10).
2. Each infrastructure assembly owns the registration method for its own services. The composition seam types, `CompositionModule`, `IValidatedConfiguration`, and `ICompositionStartup`, are in `Iskender.Domain.Ports`.
3. Neither method builds, starts, runs, or owns a host. The host calls both in its own code (FR-001).
4. Each type's XML comment names ISK-0006 as its owning spec.

## Registration

*Review owner: Architecture (ADR) owner.*

1. `AddIskender(services, configure)` runs `configure` once, against a fresh `IskenderOptions`, and copies the result. Later changes to that options object have no effect. It returns an `IskenderBuilder` whose `Services` is the same collection.
2. The options name at most one base document and at most one overlay document, each a file path, text, or stream factory with a source name and a format of `Json` or `Yaml`. `ReadEnvironmentVariables` opts into the process's `ISKENDER__` variables, and is `false` by default. `EnvironmentName` sets Iskender's active environment name, and is `null`, production-grade, by default (TR-2, TR-3).
3. Iskender never reads the host's `IConfiguration`, `IHostEnvironment`, or `IWebHostEnvironment`, never changes the host's configuration, and never derives its environment name from the host's.
4. Precedence is ISK-0003's: defaults, base, overlay, environment variables. There is no further layer, and values built in code reach Iskender only as `FromText` documents (TR-4).
5. Registration reads no document, calls no stream factory, runs no validation, opens no network connection, and reads no secret (FR-011).
6. Module and adapter registrations are explicit extension methods on the host's `IServiceCollection`, called in any order relative to `AddIskender(...)` and to each other. No assembly is scanned. A module is composed only if a call registered it:

   | Method | Assembly | Module | Sections | Requires |
   |---|---|---|---|---|
   | `AddIskender(...)` | `Iskender.Hosting` | `hosting` | `hosting` | |
   | `AddIskenderCoordination()` | `Iskender.Infrastructure` | `coordination` | `coordination` | `hosting` |
   | `AddIskenderPostgreSqlPersistence()` | `Iskender.Infrastructure.PostgreSql` | `persistence-postgresql` | `persistence` | `hosting` |
   | `AddIskenderPostgreSqlCoordination()` | `Iskender.Infrastructure.PostgreSql` | `coordination-postgresql` | | `coordination`, `persistence-postgresql` |
   | `AddIskenderRedisCoordination()` | `Iskender.Infrastructure.Redis` | `coordination-redis` | | `coordination` |

7. A later module follows the same pattern: one method in its own assembly, which adds one `CompositionModule` with its sections and requirements through `CompositionRegistration.Add`, plus its services, and, if it must act at host start, one `ICompositionStartup`.
8. Iskender registers no `TimeProvider`, `ILoggerFactory`, credential provider, or other service a host might own. It resolves them with `GetService`, and falls back to `TimeProvider.System`, `NullLoggerFactory`, and a `null` credential provider.

## Registration mistakes

*Review owner: Architecture (ADR) owner.*

| Mistake | Fails | Code | Event |
|---|---|---|---|
| `AddIskender(...)` called twice on one collection | At the second call | `hosting.registration_duplicate` | None, since no logger exists yet |
| A module registration called twice on one collection | At the second call | `hosting.registration_duplicate` | None |
| Two coordinator registrations of one kind | At the second call | `hosting.registration_duplicate` | None |
| A module registration without `AddIskender(...)` | At host start | `hosting.registration_missing` | 6003 |
| A module whose `Requires` names a module not registered | At host start, before validation | `hosting.registration_missing` | 6003 |
| `MapIskender(...)` without `AddIskender(...)` | At the call | `hosting.registration_missing` | 6003 |
| A coordinator selected by configuration with no registration | At host start, after validation | `hosting.registration_missing` | 6003 |
| Two modules claiming one section namespace | When the schema registry is built, at host start | `config.schema.duplicate_namespace` | None |

Diagnostics name the module, or the coordinator kind and primitive, and never a configuration value.

## Lifetimes

*Review owner: Architecture (ADR) owner.*

| Service | Lifetime | Owner |
|---|---|---|
| Runtime options, schema registry, validated configuration, `ConfigurationSnapshot`, `ConfigurationRevision`, hosting settings | Container singleton | `Iskender.Hosting` |
| `GatewayExecutionContext` | Request-scoped, set once by the bootstrap | `Iskender.Hosting` |
| `CoordinationSettings`, local substitutes, `CapabilityReport`, `CoordinationGate`, `SingleInstanceGuard` (as `ISingleInstanceStatus`), `ICoordinationHealthProbe` | Container singleton | `Iskender.Infrastructure` |
| `PostgreSqlSettings`, `PostgreSqlDataSources`, `IUnitOfWorkFactory`, `IMigrationRunner`, `IPersistenceHealthProbe` | Container singleton | `Iskender.Infrastructure.PostgreSql` |
| PostgreSQL coordinator ports and fencing guard | Container singleton, sharing the persistence data sources | `Iskender.Infrastructure.PostgreSql` |
| `RedisCoordinator` and its ports | Container singleton | `Iskender.Infrastructure.Redis` |

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
| `Iskender.Domain.Registry.Snapshots.CanonicalWriter.Options` | A `JsonWriterOptions` struct, copied on each use and never written |
| `Iskender.Domain.Registry.Snapshots.CanonicalWriter.Utf8`, `Iskender.Infrastructure.PostgreSql.Migrations.MigrationScript.StrictUtf8` | A `UTF8Encoding` holds only the settings it was built with |
| `Iskender.Domain.Registry.Validation.FieldRules.IgnoreAsciiCase` | An `AsciiIgnoreCaseComparer`, which has no fields |
| `Iskender.Core.Contracts.Errors.GatewayError.NoDiagnostics` | An empty `ErrorDiagnostics`, which is immutable |
| `Iskender.Infrastructure.PostgreSql.FailureClassifier.Recorded` | A `ConditionalWeakTable` keyed by the `GatewayException` instance it describes. An entry is reachable only through that exception, which belongs to one call, and is collected with it |
| `Iskender.Infrastructure.Redis.RedisScripts` script fields | Each `RedisScript` holds a fixed name and script text, set at construction |

The scan's first run in `implement` confirms this list. A field it finds that is not here is either fixed or added with its reason and reviewed.

## Start-up

*Review owner: Architecture (ADR) owner.*

1. Iskender's configuration is validated once per container, through `ConfigurationValidator` with the host's logger, at the first of the host's `StartingAsync` and the first resolution of a service that needs the snapshot (FR-012). Every Iskender service in the container sees the one resulting snapshot and revision.
2. Before validation, the start checks every module's `Requires`.
3. A rejected configuration makes the host's start fail with `GatewayException` `hosting.configuration_rejected`. Its `ErrorDiagnostics.Text` is ISK-0003's text rendering, and its values are `errorCount` and `reportedCount`. ISK-0003 writes events 3002 and 3003, and then Hosting writes 6002. No snapshot is stored, and every service that needs one fails with the same code (FR-013).
4. A base or overlay document that cannot be read fails start the same way. Its diagnostics name the layer and the source name, say the document could not be read, and hold no content and no exception text. The document is read before ISK-0003's validator is called, so the validator does not run and writes no event 3001 to 3003. The failure counts as one error: its values are `errorCount` 1 and `reportedCount` 1, and Hosting writes 6002 with an `ErrorCount` of 1.
5. A valid configuration writes ISK-0003's 3001, and Hosting writes 6001 once, naming the same revision.
6. Then each registered `ICompositionStartup` runs, in ordinal module-name order. Today only `coordination` registers one:
   1. A primitive whose configured coordinator is `redis` or `postgresql` with no registration of that kind fails start with `hosting.registration_missing`, naming the kind and the primitive. Nothing below runs.
   2. The single-instance guard starts for the data plane, with the selected lease coordinator and `instanceLease.duration`. It connects only with `single_instance` and a PostgreSQL lease coordinator.
   3. ISK-0005's capability check runs once over every registered `FeatureRequirement`, and its results are stored as the container's `CapabilityReport` and logged as events 5001 and 5002.
   4. The container's `CoordinationGate` is built from that report. Before this step, resolving the gate or the report fails with `coordination.capability_unsupported`.
7. A refusal does not fail start. Mapping refusals onto start-up or readiness is ISK-0007's (TR-7a).
8. At host stop, each `ICompositionStartup.StopAsync` runs in reverse order. The guard stops its timer and releases its lease.
9. A stop or cancellation during validation stores nothing and leaves nothing running.
10. Migrations never run at start. Health probes are never called at start.

## Route group and request bootstrap

*Review owner: Architecture (ADR) owner.*

1. `MapIskender(prefix)` maps one route group under `prefix` and returns it, so the host can add conventions. A `null` or empty prefix, or one that does not start with `/`, fails with `core.value.invalid`. It may be called more than once, with different prefixes. Every group shares the container's one snapshot.
2. Every endpoint in a group first runs the request bootstrap, which creates the request's correlation context and `GatewayExecutionContext` and resolves its configuration revision.
3. **Correlation**: the gateway generates the `req_` ID, which is authoritative. A caller value is read from the `X-Correlation-ID` request header and kept only if it is 1 to 128 characters of `A-Z`, `a-z`, `0-9`, `.`, `_`, `:`, and `-`. Otherwise it is dropped (ISK-0002, Request context).
4. **Deadline**: the request's start plus `hosting.requestTimeout`, from the container's `TimeProvider`. **Cancellation**: the request's `RequestAborted`. **Trace**: the current activity's trace and span IDs, or new random IDs when there is none.
5. Until an ingress spec adds endpoints, the group's only endpoint is a catch-all at the lowest route precedence. It answers 404 with the ISK-0002 `error-envelope` for `core.resource.not_found`, as `application/json`, carrying the request's gateway correlation ID.
6. Later specs add endpoints to the group without changing `MapIskender(...)`'s signature (AV-4). Mandatory authentication, when the identity specs add it, is attached to the group itself by `MapIskender(...)`, so host middleware cannot remove or replace it (FR-016).

## Coexistence

*Review owner: Security owner.*

1. Iskender adds no middleware, `IStartupFilter`, exception handler, authentication or authorization scheme, CORS policy, logging provider, metric exporter, trace exporter, or fallback route outside its groups, and configures no host options type (FR-014, FR-021).
2. A request outside every Iskender prefix runs no Iskender code (FR-015).
3. Host middleware registered before the endpoints runs for Iskender's requests as for any other endpoint.
4. A prefix of `/`, or one that overlaps a host route, is allowed. ASP.NET Core's routing precedence then decides: the host's more specific routes keep matching, and Iskender's catch-all answers the rest under its prefix.

## Configuration

*Review owner: Architecture (ADR) owner.*

The namespace `hosting`, version 1, registered by `Iskender.Hosting`:

| Field | Type | Default | Constraints | Development-only |
|---|---|---|---|---|
| `requestTimeout` | `duration` | `100s` | `1s` to `1h` | No |

## Error codes

*Review owner: Architecture (ADR) owner.*

Registered in `ErrorCodes`, `since: 5`, and listed in the ISK-0002 contract's Error codes table.

| Code | Category | Meaning | Safe message |
|---|---|---|---|
| `hosting.configuration_rejected` | `internal` | Host start failed because Iskender's configuration was rejected or a configuration document could not be read | Iskender could not start because its configuration was rejected. |
| `hosting.registration_duplicate` | `internal` | `AddIskender(...)` or one module registration was called twice on one service collection | An Iskender registration was added more than once. |
| `hosting.registration_missing` | `internal` | A module registration or `MapIskender(...)` was used without `AddIskender(...)`, a module was registered without a module it requires, or a coordinator selected by configuration has no registration | A required Iskender registration is missing. |

## Diagnostics

*Review owner: Privacy/Operations owner.*

Written through the host's `ILoggerFactory`, category `Iskender.Hosting`. Templates and event IDs are fixed.

| Event ID | Name | Level | Template |
|---|---|---|---|
| 6001 | `IskenderComposed` | Information | `Iskender composed: modules {Modules}, sections {Sections}, environment {Environment}, revision {Revision}.` |
| 6002 | `IskenderStartRefused` | Error | `Iskender start refused: configuration rejected with {ErrorCount} error(s).` |
| 6003 | `IskenderRegistrationMissing` | Error | `Iskender registration missing: {Missing} required by {RequiredBy}.` |

1. `{Modules}` is the module names joined by `,`, and `{Sections}` the `name@version` pairs, each in ordinal order. `{Environment}` is the name, or `(none)`. `{Missing}` is a module name or `<kind> coordinator for <primitive>`. `{RequiredBy}` is a module name, `configuration`, or `MapIskender`.
2. No event, exception, or diagnostic holds a configuration value, a secret reference name, a connection detail, or a route value from a request.
3. ISK-0003's events 3001 to 3003 and ISK-0005's 5001 to 5011 are written as those contracts define.
4. A duplicate registration writes no event, because it fails before any container exists (maintainer decision, 2026-11-14).

## Public surface

*Review owner: Architecture (ADR) owner.*

1. `Iskender.Hosting` is a `public contract` project, contract `hosting`, version 1, with its baseline in `tests/Iskender.Contracts.Compatibility.Tests/baselines/hosting.surface.txt`. Every public type carries `[ContractVersion("hosting", <since>)]` (FR-017).
2. No public member exposes an `Iskender.Domain` type (FR-018). The surface holds only Hosting's own types, .NET types, and ASP.NET Core and `Microsoft.Extensions` types.
3. The registration methods in `Iskender.Infrastructure` and its adapters, and the seam types in `Iskender.Domain.Ports`, are embeddable, outside the harness, and prerelease under ADR-0030 (AV-3).

## Test fixture

*Review owner: Performance owner (+ QA).*

1. `tests/Iskender.Hosting.Testing` provides `EmbeddedHost`, which builds a host on the in-memory test server, with container validation on and a captured logger, from the hosting sample or from given registrations and documents. It starts no process and opens no port.
2. The acceptance tests of User Stories 1 to 3 run through it. ISK-0007 reuses it for the cross-host lifecycle test.
3. The single-instance lease test needs a container runtime, and follows ISK-0005's rule: skipped with its reason without one, and failing instead when `CI` is `true`. Every other hosting test needs no container.

## Threat model and known limits

### What the rules defend against

The Negative Security and Failure Scenarios table in [spec.md](../spec.md) lists each threat with the requirement that covers it. This contract's rules implement them: a partly valid configuration (Start-up 1 to 4), values in failures (Diagnostics 2), a host setting relaxing Iskender's defaults (Registration 2, 3), Iskender changing the host (Coexistence), cross-request context (Lifetimes 4), shared state between containers (Lifetimes 5, Static state), unrequested adapters (Registration 6), connections at registration (Registration 5), a lease left held (Start-up 8), and a silent second registration (Registration mistakes).

### Known limits

- **Telemetry instruments are process-wide**: two containers in one process write to instruments with the same names, so an exporter sees both containers' measurements together. Telling them apart needs a later tag or per-container meter, which several gateways in one container, post-MVP, would bring.
- **Composition start-ups run after the server starts**: validation runs in `StartingAsync`, before any `StartAsync`, but the `ICompositionStartup` steps run in Iskender's `StartAsync`, which the host runs after the server's own, since the host registers its server first. A request that arrives in between finds the coordination gate and report fail-closed (Start-up 6.4), and a missing coordinator then still fails the host's start.
- **Order-dependent checks run at start**: a missing registration is found when the host starts, not when the container is built, because registrations may come in any order and the container runs no Iskender code when it is built.
- **The catch-all under `/`**: an Iskender group mapped at `/` answers every request no host route matches, with Iskender's 404 envelope instead of the host's own fallback.
- **Reflection scan**: the static-state scan reads declared fields. State reached through a static method's local captured in a non-generated type, or held by a host-supplied object, is caught by review.
- **Stream factories**: a stream factory the host supplies runs inside Iskender's validation. A factory that blocks delays start, and its exception is reported without its text.

## Left to later specs

| Item | Spec |
|---|---|
| Building the container data-plane host from `AddIskender(...)` and `MapIskender(...)`, and the cross-host lifecycle test | ISK-0007 |
| Mapping start-up failure, refusals, and health reasons onto start-up and readiness | ISK-0007 |
| OpenTelemetry subscription to Iskender's sources | ISK-0007 |
| Authentication inside the route group | ISK-0011 onward |
| Protocol endpoints in the route group | ISK-0030 onward |
| Composing control-plane services into an embedded host | The first spec that adds them (AV-5) |
| A compatibility commitment for embeddable registrations | ISK-0086 (AV-3) |
