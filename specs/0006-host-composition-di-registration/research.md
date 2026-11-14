# Research: ISK-0006 — Host Composition & DI Registration

Each entry records a decision, why it was made, and what was rejected. The spec has no `NEEDS CLARIFICATION` marker left. `specify` settled the configuration sources, the environment name, and the sample's home, and `clarify` settled coordination at start and the two PostgreSQL opt-ins. These entries settle how the approved requirements are built. The maintainer decided five of them during `plan` on 2026-11-14, as R8, R9, R11, R13, and R15 record. Those decisions reword US5-2, US5-5, one edge case, TR-12, and TR-13 in the spec, add TR-14, and record a `plan` clarification session. No other requirement changes.

## R1. Packages and frameworks (AV-6)

**Decision**:

| Project | Adds | Why |
|---|---|---|
| `Iskender.Hosting` | `<FrameworkReference Include="Microsoft.AspNetCore.App" />` | `IServiceCollection`, `IHostedLifecycleService`, routing, and endpoint filters. ASP.NET Core is the `aspnetcore` category, which ISK-0001 does not deny `Iskender.Hosting` |
| `Iskender.Infrastructure` | `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.14, `Microsoft.Extensions.Hosting.Abstractions` 10.0.14 | Registration methods, and the composition check hosted service (R8) |
| `Iskender.Infrastructure.PostgreSql`, `Iskender.Infrastructure.Redis` | Nothing direct | They take both packages through `Iskender.Infrastructure` |
| `samples/Iskender.Samples.EmbeddedHost` | `Microsoft.NET.Sdk.Web` | An ordinary ASP.NET Core application (R5) |
| `tests/Iskender.Hosting.Testing`, `tests/Iskender.Hosting.Tests` | `Microsoft.AspNetCore.TestHost` 10.0.14 | The in-memory server (R17) |

The shared framework on this machine and in the pinned SDK is 10.0.14, and the pinned `Microsoft.Extensions.Logging.Abstractions` is 10.0.14. Version 10.0.14 keeps every `Microsoft.Extensions.*` package on one servicing level. `Microsoft.Extensions.DependencyInjection.Abstractions` is already in `Iskender.Domain`'s lock file at 10.0.14, as a transitive package.

**License**: every package above is published by Microsoft under the SPDX expression `MIT`, which `check-license.sh` already allows, so AV-6 holds and the allow-list does not change. `implement` confirms it by running `check-license.sh` once the packages are restored. If a package's `.nuspec` declares anything else, that is a blocker, not a reason to change the list.

**Rationale**: A library that ships ASP.NET Core extension methods takes the shared framework by `FrameworkReference`, which packs as a framework dependency, not as packages. The adapters need only the dependency injection and hosting abstractions, which carry no ASP.NET Core type and are not in any denied category.

**Alternatives considered**: Individual `Microsoft.AspNetCore.*` packages, which are not published for .NET 10. A `Microsoft.Extensions.DependencyInjection.Abstractions` reference in `Iskender.Domain`, which would put registration code in the domain assembly.

## R2. The public surface of `Iskender.Hosting` (TR-1, TR-9, FR-001, FR-017, FR-018)

**Decision**: `Iskender.Hosting` exposes six public types, all in namespace `Iskender.Hosting` and tagged `[ContractVersion("hosting", 1)]`, under `[assembly: ContractPackage("hosting", 1)]`:

| Type | Shape |
|---|---|
| `IskenderServiceCollectionExtensions` | `IskenderBuilder AddIskender(this IServiceCollection services, Action<IskenderOptions> configure)` |
| `IskenderEndpointRouteBuilderExtensions` | `RouteGroupBuilder MapIskender(this IEndpointRouteBuilder endpoints, string prefix)` |
| `IskenderOptions` | `BaseDocument`, `OverlayDocument` (`IskenderConfigurationDocument?`), `ReadEnvironmentVariables` (`bool`, default `false`), `EnvironmentName` (`string?`, default `null`) |
| `IskenderConfigurationDocument` | `FromFile(string path, IskenderDocumentFormat format, string? sourceName = null)`, `FromText(string sourceName, IskenderDocumentFormat format, string text)`, `FromStream(string sourceName, IskenderDocumentFormat format, Func<Stream> open)`. `SourceName`, `Format` |
| `IskenderDocumentFormat` | `Json`, `Yaml` |
| `IskenderBuilder` | `IServiceCollection Services` |

No public member names an `Iskender.Domain` type, so `ConfigurationDocumentSource`, `ConfigurationDocumentFormat`, and `ConfigurationSchemaRegistry` stay behind the surface, and Hosting maps its own document type onto them internally (FR-018). The surface holds only Hosting's own types, .NET types, and ASP.NET Core and `Microsoft.Extensions` types. A test reflects over every public member and fails on any type from `Iskender.Domain`.

`configure` runs once, inside `AddIskender(...)`, against a fresh `IskenderOptions`. The resulting values are copied into an internal, immutable `IskenderRuntimeOptions` and registered as an instance. `IOptions<T>` is not used, so no other code can reconfigure Iskender later, and no host option type is touched. The documents are only described here: a file is not opened, a stream factory is not called, and text is not parsed until validation (R6, FR-011).

The stream form takes a factory, not a stream, so that each container that validates opens its own stream, and so that registration holds no open handle. The host's stream is disposed after it is read.

**Rationale**: This is the convention ASP.NET Core libraries use, and it keeps the host's own `Program.cs` in control (TR-1). A small surface keeps the harness's version 1 baseline small and makes each later addition a visible, compatible change.

**Alternatives considered**: Returning `IServiceCollection`, which leaves later Iskender-specific registration no type to hang on. Taking `ConfigurationDocumentSource` directly, which puts a `Iskender.Domain` type on a versioned surface (FR-018). `IOptions<IskenderOptions>`, which a later `Configure<IskenderOptions>` call could change after Iskender's registration had been checked.

## R3. The composition seam (TR-5, FR-004, FR-006)

**Decision**: Module and adapter registrations reach Hosting only through three plain types in `Iskender.Domain.Ports`, which `Iskender.Hosting`, `Iskender.Infrastructure`, and every adapter already reference. None names a dependency injection type, so `Iskender.Domain` takes no new package.

| Type | Role |
|---|---|
| `CompositionModule` (sealed class) | One registered module: `Name` (1 to 64 characters of `a-z`, `0-9`, `-`), `Sections` (`IReadOnlyList<SectionSchema>`), and `Requires` (names of modules it needs). Registered as an instance, one per call |
| `IValidatedConfiguration` (interface) | `ConfigurationSnapshot Snapshot { get; }`. Implemented by Hosting. Module services read their sections through it |
| `ICompositionStartup` (interface) | `string ModuleName`, `ValueTask StartAsync(ConfigurationSnapshot snapshot, CancellationToken)`, `ValueTask StopAsync(CancellationToken)`. Hosting runs every registered one at host start and stop, in module-name order |

`AddIskender(...)` registers a `CompositionModule` named `hosting`, carrying the `hosting` section (R14). The composition summary and the schema registry are built from every registered `CompositionModule`, whatever order the calls came in. The registry is built once per container, on first need, by `ConfigurationSchemaRegistry.Create`, so a namespace claimed by two modules fails with ISK-0003's `config.schema.duplicate_namespace`.

The helper that adds a `CompositionModule` and checks for duplicates lives in `Iskender.Infrastructure.Composition.CompositionRegistration`, so every adapter uses the same rule. Hosting has its own internal copy for its own module, because it cannot reach `Iskender.Infrastructure` (rule A8).

No assembly is scanned. A module exists in a container only if a call put its `CompositionModule` there.

**Rationale**: The seam has to be reachable from `Iskender.Hosting` and from the infrastructure assemblies, and `Iskender.Domain` is the only internal assembly both reference other than `Iskender.Core.Contracts`. `Iskender.Domain.Ports` is the approved home for types shared across modules (ISK-0001 Port homes), and putting the seam in `Iskender.Core.Contracts` would version an internal wiring detail. So no reference is added to any project, as FR-006 requires.

**Alternatives considered**: An `IIskenderBuilder` interface that adapters extend, which would have to live in `Iskender.Core.Contracts` with a dependency injection package, making the public contract package depend on `Microsoft.Extensions.DependencyInjection.Abstractions`. Discovering modules through `[assembly:]` attributes, which is scanning by another name.

## R4. Registrations this spec ships (FR-005, TR-5)

**Decision**: Every registration is an extension method on the host's `IServiceCollection`, in its assembly's own namespace, called beside `AddIskender(...)` in any order:

| Method | Assembly | Module | Sections | Requires | Composes |
|---|---|---|---|---|---|
| `AddIskender(...)` | `Iskender.Hosting` | `hosting` | `hosting` | | Options, the schema registry, the validated configuration, the start-up service, the request bootstrap |
| `AddIskenderCoordination()` | `Iskender.Infrastructure` | `coordination` | `coordination` | `hosting` | `CoordinationSettings`, the local substitutes, the coordinator selection, the capability report, `CoordinationGate`, the single-instance guard, the coordination health probe |
| `AddIskenderPostgreSqlPersistence()` | `Iskender.Infrastructure.PostgreSql` | `persistence-postgresql` | `persistence` | `hosting` | `PostgreSqlSettings`, `PostgreSqlDataSources`, `IUnitOfWorkFactory`, `IMigrationRunner`, `IPersistenceHealthProbe` |
| `AddIskenderPostgreSqlCoordination()` | `Iskender.Infrastructure.PostgreSql` | `coordination-postgresql` | | `coordination`, `persistence-postgresql` | The PostgreSQL atomic operations, leases, fencing guard, cache, and health, sharing the persistence data sources |
| `AddIskenderRedisCoordination()` | `Iskender.Infrastructure.Redis` | `coordination-redis` | | `coordination` | `RedisCoordinator` and its ports. Its settings are the `coordination` section's `redis` fields |

The coordinator registrations add a `CoordinatorRegistration` (Infrastructure, sealed class: `Kind`, and factories for each primitive the coordinator offers and for its health probe). The coordination registration selects, for each primitive, the registration whose `Kind` the `coordination` section names, or the local substitute for `local`.

The PostgreSQL coordinator requires both `coordination` and `persistence-postgresql`, so a host with PostgreSQL storage composes no coordination unless it asks, as `clarify` decided. The Redis coordinator requires `coordination`, so the local substitutes and the checker are composed explicitly too.

Credential providers are not registered by Iskender. The data sources and the Redis connection resolve `IDatabaseCredentialProvider` and `ICoordinationCredentialProvider` with `GetService`, so a host that registers none gets ISK-0004's and ISK-0005's behavior with a `null` provider: health reports `authentication_failed`, and calls fail with `persistence.unavailable` or `coordination.unavailable`.

Port signatures, settings types, and every ISK-0004 and ISK-0005 contract rule stay as they are. The registrations construct the existing types with their existing constructors.

**Rationale**: Each assembly knows what its own types need, and the host opts in by referencing the assembly and calling its method, which is ISK-0001's embedding model. Requiring `coordination` before a coordinator keeps "no module unless the host asks" true for the checker and the local substitutes as well.

**Alternatives considered**: Coordinator registrations that call `AddIskenderCoordination()` themselves, which composes a module the host did not ask for and would turn a later explicit call into a duplicate. One `AddIskenderPostgreSql()` that composes both, which `clarify` rejected.

## R5. The hosting sample and the rules contract revision (TR-10, FR-022, FR-024)

**Decision**: Module dependency rules contract version 4, published in [contracts/module-dependency-rules.md](contracts/module-dependency-rules.md):

1. `Iskender.Hosting` changes role from `embeddable` to `public contract`. Its approved references, its forbidden reach (A8, A10), and its denied categories are unchanged.
2. A new role, `sample`, not packable. A sample's project lives under `samples/`. The approved references table adds `Iskender.Samples.EmbeddedHost`, which may reference `Iskender.Hosting`, `Iskender.Core.Contracts`, `Iskender.Infrastructure`, and `Iskender.Infrastructure.<Adapter>`.
3. A new forbidden-reach rule, **A20**: no production project, and no sample, reaches a sample. Test projects may reference one, so the fixture hosts it in process. Rule A2 still forbids every project from reaching a host.
4. A14 is extended: a project may also live under `samples/`, and no project there is named `*.Tests`.
5. A19's denied categories for the sample are those of an executable, `cloud`, `provider`, and `driver`, checked on direct references only, as for executables, since its adapters bring their drivers transitively.
6. C3 covers every project under `samples/` in `.github/CODEOWNERS`, as it does under `src/`.
7. The `docs/03` expectation "Same request lifecycle in embedded and container hosts" moves from "Deferred to ISK-0006 and ISK-0007" to "Embedded half by ISK-0006's fixture; container half and the cross-host test by ISK-0007".
8. The Left to later specs list removes the hosting sample.

The sample is `samples/Iskender.Samples.EmbeddedHost`, an `Microsoft.NET.Sdk.Web` project that references `Iskender.Hosting` and `Iskender.Infrastructure`. It has its own `/hello` endpoint, a middleware that adds a response header, a `GreetingService`, and a `Sample:Greeting` key in its `appsettings.json`. It calls `AddIskender(...)` with `iskender.yaml` as the base document and `AddIskenderCoordination()`, and maps Iskender under `/iskender`. Its `Program.cs` calls a public static `SampleApplication.Create(string[] args, Action<WebApplicationBuilder>? configure = null)`, which builds the `WebApplication` without running it, so the fixture can replace the server with the in-memory one. The sample is built by check 3 and run by the hosting tests in check 4, and it changes no Iskender source.

**Rationale**: ISK-0001 asks the spec that adds the sample to place it, and `specify` chose this home and role. A separate role keeps the sample out of packages and out of the executables' attestation (C4), and A20 lets only tests reference it.

**Alternatives considered**: Running the sample as a child process from the tests, which would need a port and a process, and is what the acceptance criterion excludes. `WebApplicationFactory<TEntryPoint>` from `Microsoft.AspNetCore.Mvc.Testing`, which adds a package to reach a factory method the sample can expose itself.

## R6. Configuration sources, environment name, and precedence (TR-2 to TR-4, FR-002, FR-003)

**Decision**: At validation, Hosting builds one `ConfigurationLoadRequest` from the runtime options:

- **Base and overlay**: each `IskenderConfigurationDocument` becomes a `ConfigurationDocumentSource` of the same format and source name. A file is read through `ConfigurationDocumentSource.FromFile`, text through `FromString`, and a stream factory through `FromStream`, with the stream disposed afterwards. A file or stream that cannot be opened or read fails start with `hosting.configuration_rejected` (R15).
- **Environment variables**: `ConfigurationEnvironmentSource.FromProcess()` only when `ReadEnvironmentVariables` is `true`. Otherwise no environment source, so the snapshot records 0 variables applied.
- **Environment name**: `EnvironmentName` as given. `null` means no name, which is production-grade. The host's `IHostEnvironment` is never read.
- **Precedence**: ISK-0003's, with no further layer. Values a host builds in code reach Iskender only as `FromText` documents.

Hosting never resolves `IConfiguration` and never writes to it. A test lists every service Hosting resolves and fails on `IConfiguration`, `IHostEnvironment`, or `IWebHostEnvironment`, and another compares the host's configuration key by key before and after start (SC-004).

**Rationale**: These apply the `specify` decisions without adding anything. ISK-0003's sources already bound their reads, so Hosting adds no reader of its own.

## R7. Validation timing and the snapshot (TR-7, FR-011 to FR-013)

**Decision**: `IskenderConfiguration`, an internal container singleton implementing `IValidatedConfiguration`, validates exactly once per container, at the first of these:

1. The host's start. Its `IHostedLifecycleService.StartingAsync` runs before any `StartAsync`, so before the server starts listening.
2. The first resolution of a service that needs the snapshot, if the host resolves one before start.

The first caller runs validation under a lock. Later callers get the stored outcome: the snapshot, or the same `hosting.configuration_rejected` failure. A rejected configuration leaves no snapshot, so every service that needs one fails with that code and none is ever built on defaults (FR-013, US2-4). If the start is cancelled before validation finishes, nothing is stored, and the next caller starts over.

`ConfigurationSnapshot` and its `ConfigurationRevision` are registered as container singletons that resolve through `IskenderConfiguration`, so every Iskender service in the container sees one snapshot and one revision (FR-012). Validation uses the host's `ILoggerFactory`, under category `Iskender.Configuration`, so ISK-0003's events 3001 to 3003 reach the host's log.

Registration itself reads no document, runs no validation, opens no connection, and reads no secret (FR-011). A test registers documents whose factories and paths throw, then builds the container without starting it, and checks that nothing was called.

**Rationale**: The configuration contract leaves refusal to the host, and failing in the host's own `StartingAsync` lets the host's start-up handling decide what to do. Validating on first need as well as at start answers US2-4 without a second code for "not started yet", and still validates once.

**Alternatives considered**: Validating in `AddIskender(...)`, which runs before the host can log, and breaks FR-011. A `hosting.not_started` failure for resolution before start, which adds a code the spec does not list and makes an early resolution fail on a valid configuration.

## R8. Missing and out-of-order registrations (TR-5, TR-7a, TR-12; maintainer decision)

**Decision**: Registrations may come in any order (FR-004), so a check that depends on another call runs when the host starts, the first point at which Iskender code runs after the container is built. **Decided by the maintainer in `plan` on 2026-11-14**: US5-2 and US5-5 fail "when the host starts", not "when the host is built".

| Mistake | Where it fails | Code |
|---|---|---|
| `AddIskender(...)` twice on one collection | At the second call | `hosting.registration_duplicate` |
| The same adapter registration twice | At the second call | `hosting.registration_duplicate` |
| An adapter registration without `AddIskender(...)` | At host start | `hosting.registration_missing` |
| A registration whose `Requires` names a module that is not registered, such as the PostgreSQL coordinator without the persistence adapter | At host start, before validation | `hosting.registration_missing` |
| `MapIskender(...)` without `AddIskender(...)` | At the call, which runs after the container is built | `hosting.registration_missing` |
| A coordinator selected by configuration whose adapter is not registered | At host start, after validation | `hosting.registration_missing` |

When `AddIskender(...)` is present, its start-up service checks every module's `Requires` first. When it is absent, nothing of Hosting's runs, so `CompositionRegistration` also adds one internal `IHostedService`, `CompositionCheck`, once per collection, which fails start with `hosting.registration_missing` if no `hosting` module is registered and does nothing otherwise. This is why `Iskender.Infrastructure` takes the hosting abstractions (R1).

Each failure's diagnostics name the module or coordinator kind and primitive, and never a configuration value.

**Rationale**: An adapter registration cannot know whether `AddIskender(...)` will be called later, and the container exposes no hook that runs Iskender code when it is built. Host start is the earliest point that always runs.

**Alternatives considered**: Requiring `AddIskender(...)` first, so an adapter call can fail at once. That gives up any-order registration, which FR-004 and TR-5 require.

## R9. Duplicate registrations (TR-12, TR-13, FR-020; maintainer decision)

**Decision**: **Decided by the maintainer in `plan` on 2026-11-14**: a duplicate registration throws `GatewayException` with `hosting.registration_duplicate` from the second call, and writes no log event, because no container or logger exists yet at that point. TR-13 drops the duplicate-registration event, so FR-020, which requires TR-13's events, no longer includes it, and the exception is its only signal. The remaining events are numbered with no gap (R16).

A duplicate is detected by module name, through the `CompositionModule` instances already in the collection. So two schemas never claim one namespace through a repeated call (US5-3), and a namespace claimed by two different modules still fails with ISK-0003's `config.schema.duplicate_namespace` when the registry is built.

**Alternatives considered**: Recording the duplicate and failing at host start with the event, which loses the stack trace that points at the offending line.

## R10. Coordination at start (TR-7a, FR-013a)

**Decision**: `AddIskenderCoordination()` registers one `ICompositionStartup` for module `coordination`. At host start, after validation succeeds, it:

1. Reads `CoordinationSettings` from the snapshot.
2. For each primitive whose coordinator is `redis` or `postgresql`, checks that a `CoordinatorRegistration` of that kind is registered. If one is not, start fails with `hosting.registration_missing`, naming the coordinator kind and the primitive, event 6003 is written, and nothing below runs (US5-4).
3. Builds each primitive's port from the selected registration or the local substitute.
4. Creates `SingleInstanceGuard` for `SingleInstancePlane.DataPlane`, with the lease coordinator the section selects and `instanceLease.duration`, and starts it. The guard connects only with `single_instance` and a PostgreSQL lease coordinator. Otherwise it reports unverified and opens nothing, as ISK-0005 decided.
5. Runs `CoordinationCapabilityChecker.Check` once over every registered `FeatureRequirement`, which today is none, with the topology, the declared capabilities, and the guard's state. It stores the result as the container's `CapabilityReport` and writes events 5001 and 5002 through `CoordinationLog`.
6. Builds the container's `CoordinationGate` from the report and the ports.

At host stop, `StopAsync` disposes the guard, which stops its timer and releases its lease. Container disposal disposes it too, and disposing twice is safe.

`CoordinationGate` and `CapabilityReport` are registered as container singletons that resolve only after this step has run. Before then, and after a failed start, resolving them fails with `coordination.capability_unsupported`, which is ISK-0005's fail-closed code for a feature that is not available, so no call can pass a gate that was never checked. A refusal is logged and does not fail start, since mapping refusals onto start-up or readiness is ISK-0007's (AV-2).

The data plane is the only plane an `AddIskender(...)` host runs, so the guard's plane is fixed. A control-plane embedding follows the same pattern when one exists (AV-5).

**Rationale**: This is TR-7a, built from ISK-0005's types as they are. Building the gate from the start-time report means its feature results and the guard's first state are always the ones the log shows.

**Alternatives considered**: Building the gate lazily on first resolution, which could run the check before the guard took its lease, and so accept a `single_instance` feature that another process holds.

## R11. Static state and the scan (TR-6, FR-008, SC-006; maintainer decision)

**Decision**: A test in `Iskender.Hosting.Tests` reads every production assembly under `src/` through reflection and reports each static field that is not allowed. A field is allowed when it is a literal (`const`), when it belongs to a compiler-generated type (named with `<` or marked `[CompilerGenerated]`), or when it is `initonly` (`static readonly`, or the backing field of a get-only static property) and its declared type is in one of these classes:

1. **Immutable values**: primitives, enums, `string`, `decimal`, `TimeSpan`, `DateTimeOffset`, `Guid`.
2. **Immutable collections**: any type in `System.Collections.Frozen` or `System.Collections.Immutable`.
3. **Delegates**: such as the `Action<ILogger, ...>` that `LoggerMessage.Define` returns, which captures nothing.
4. **Telemetry instruments**: `ActivitySource`, `Meter`, `Counter<T>`, `UpDownCounter<T>`, `Histogram<T>`, and the observable instruments. **Decided by the maintainer in `plan` on 2026-11-14**: instruments stay static and are listed as allowed. They hold no values: measurements go to listeners, which the host owns. `MeterListener` is process-wide, so a meter per container would not give each container's exporter only its own measurements anyway. And moving them would change `UnitOfWorkRetry.RunAsync`, a port that FR-005 freezes.
5. **Sentinels**: a field whose type is its own declaring type, and that type is sealed, such as `VersionToken.Initial` and `CoordinationScope.Operator`.
6. **Contract registry types**: `SectionSchema`, `ErrorCode`, `ResourceType`, and `WireContract`, whose instances ISK-0002 and ISK-0003 define as immutable.
7. **Listed fields**: each field the scan cannot prove immutable but that holds no per-container state, listed by `Type.Field` with its reason in the hosting contract's Static state table.

Any other static field fails, naming its type and field. A static field that is not `initonly` always fails, including the backing field of a settable static property.

A read of `src/` on 2026-11-14 found these fields outside classes 1 to 6. `implement` fixes the first four, and the contract lists the rest:

- **Fixed, state**: `PersistenceTelemetry.HeldConnections`, a static `ConcurrentDictionary<string, long>` counting held connections, is real process-wide state. It moves into an instance owned by each `PostgreSqlDataSources`, which reports its own counts through an `ObservableGauge` on a `Meter` it creates and disposes with itself. The metric's name, unit, and tags do not change.
- **Fixed, mutable type**: `RedisConnection.ExcludedCommands`, a static `HashSet<string>` that is never changed, becomes a `FrozenSet<string>`, copied into the client's `CommandMap` where it is used. `CoordinationPrimitives.All`, `CoordinationTelemetry.AllowedTags`, and `RedisScripts.All` keep their `IReadOnlyList<T>` property types, so no signature changes, but are backed by a `static readonly ImmutableArray<T>` field instead of an array, which a cast could change.
- **Listed**: `CanonicalWriter.Options`, `CanonicalWriter.Utf8`, `MigrationScript.StrictUtf8`, `FieldRules.IgnoreAsciiCase`, `GatewayError.NoDiagnostics`, `FailureClassifier.Recorded`, and the `RedisScripts` script fields, each with its reason in the contract.

That read was by text search, so the scan's first run in `implement` is the real check. A field it finds that is not accounted for above is fixed if it holds state, and otherwise listed with its reason. A field that holds state is never listed.

**Rationale**: A reflection scan of the built assemblies sees every static field, however it was written, and the allowed classes are those whose instances cannot carry one container's state into another. Listing an exception by name makes each one a reviewed decision.

**Alternatives considered**: Moving every meter to the host's `IMeterFactory`, which the maintainer rejected for the reasons in class 4. A text scan of the sources, which misses fields a source generator or a property adds.

## R12. Lifetimes (TR-6, FR-007, FR-009, FR-010)

**Decision**: The full table is in the [hosting contract](contracts/hosting.md#lifetimes). In short:

- **Container singletons**: the runtime options, the schema registry, `IskenderConfiguration`, the snapshot and revision, every settings type, the data sources, the coordinators and their ports, the capability report, the gate, the guard, and the health probes. Each is built by the host's container from a factory registered by its module, and disposed by it.
- **Request-scoped**: `GatewayExecutionContext`, through an internal scoped `RequestContextHolder` that the bootstrap fills once per request.
- **Transient**: none.

No Iskender service captures a scoped one. The hosting tests build every host with `ValidateScopes` and `ValidateOnBuild` set to `true`, for every combination of registrations in R4, and expect no error (FR-007).

Iskender registers nothing with `TryAdd` over a host service. It never registers `TimeProvider`, `ILoggerFactory`, or a credential provider. It resolves `TimeProvider` with `GetService` and falls back to `TimeProvider.System`, and it resolves `ILoggerFactory` with `GetService` and falls back to `NullLoggerFactory`.

**Rationale**: Every service having one owner, the host's container, is what keeps two containers in one process apart (SC-006, US1-4).

## R13. The request deadline (FR-015; maintainer decision)

**Decision**: **Decided by the maintainer in `plan` on 2026-11-14**: `Iskender.Hosting` registers a configuration section `hosting`, version 1, owner module `Iskender.Hosting`, with one field, `requestTimeout`: a duration with a default of `100s`, allowed from `1s` to `1h`, not development-only. The request bootstrap sets each request's deadline to the time it started plus `requestTimeout`, from the container's `TimeProvider`. The spec records this as TR-14. Later ingress specs add fields to the section, raising its version.

**Rationale**: `GatewayExecutionContext` requires a deadline, and a value that changes behavior belongs in validated configuration, where ISK-0003's precedence and revision cover it, not in code (TR-4). 100 seconds is the default `HttpClient` timeout, so a host sees the same bound from Iskender as from its own outbound calls.

**Alternatives considered**: A fixed 100-second constant, which no operator could change without a code change.

## R14. The route group and the request bootstrap (TR-8, FR-014, FR-015, FR-016)

**Decision**: `MapIskender(prefix)` checks that the `hosting` module is registered (otherwise `hosting.registration_missing`, with event 6003), then calls `endpoints.MapGroup(prefix)` and adds to the group:

1. An endpoint filter, `RequestBootstrap`, which runs first for every endpoint in the group. It creates the correlation context and the request's `GatewayExecutionContext`, resolves the context's configuration revision from the snapshot, and stores it in the request's `RequestContextHolder`.
2. One catch-all endpoint, `{**path}`, with the lowest route precedence, which answers 404 with the ISK-0002 error envelope for `core.resource.not_found`, written by `ErrorEnvelopeCodec`, with `Content-Type: application/json` and the request's gateway correlation ID.

The method returns the group, so the host can add its own conventions. Later specs add endpoints to the same group, and the filter applies to them too (AV-4).

Correlation follows ISK-0002: the gateway always generates the `req_` ID. A caller value is read from the `X-Correlation-ID` request header and kept only if it passes ISK-0002's rule, otherwise dropped. It never appears in a log template or a key. The trace and span IDs come from `Activity.Current` when the host has one, otherwise from new random IDs. The cancellation token is the request's `RequestAborted`.

Iskender adds no middleware: `MapIskender(...)` calls neither `Use...` nor `UseRouting`, and registers no `IStartupFilter`. So a request outside the prefix runs no Iskender code (FR-015), and host middleware runs before the group as it would before any endpoint. Iskender adds no authentication or authorization scheme, CORS policy, exception handler, logging provider, telemetry exporter, or fallback route outside the group. A test lists the service collection before and after the Iskender registrations, and fails on any added `IStartupFilter`, `IAuthenticationSchemeProvider` change, `IConfigureOptions<>` for a host options type, `ILoggerProvider`, or exporter (US3-5).

Mandatory authentication, when the identity specs add it, is an endpoint filter that `MapIskender(...)` puts on the group itself, so host middleware cannot remove it (FR-016). This spec adds no authentication, and the contract states the rule for those specs.

A prefix of `/` is allowed. ASP.NET Core's routing precedence then decides between the host's routes and Iskender's catch-all, so the host's more specific routes keep matching, and the catch-all answers anything else, as the contract documents. Mapping twice with different prefixes creates two groups over one container and one snapshot.

**Rationale**: A route group is how ASP.NET Core scopes conventions to a set of endpoints without touching the rest of the pipeline, which is exactly "no global ownership of routes".

**Alternatives considered**: Middleware that matches the prefix, which runs on every host request. `MapFallback`, which is global to the host's routes.

## R15. Error codes (TR-12, FR-019; maintainer decision)

**Decision**: Three codes in `ErrorCodes`, each `since: 5`, raising `core-contracts` from 4 to 5 with compatible changes only:

| Code | Category | Meaning | Safe message |
|---|---|---|---|
| `hosting.configuration_rejected` | `internal` | Host start failed because Iskender's configuration was rejected or a configuration document could not be read | Iskender could not start because its configuration was rejected. |
| `hosting.registration_duplicate` | `internal` | `AddIskender(...)` or one module registration was called twice on one service collection | An Iskender registration was added more than once. |
| `hosting.registration_missing` | `internal` | A module registration or `MapIskender(...)` was used without `AddIskender(...)`, a module was registered without a module it requires, or a coordinator selected by configuration has no registration | A required Iskender registration is missing. |

`hosting.configuration_rejected` carries ISK-0003's text rendering, `ConfigurationValidationReport.ToString()`, as its `ErrorDiagnostics.Text`, and its diagnostics values are `errorCount` and `reportedCount`.

**Decided by the maintainer in `plan` on 2026-11-14**: a base or overlay document that cannot be read, because its file is missing or unreadable or its stream factory or stream fails, fails start with `hosting.configuration_rejected`. Its diagnostics name the layer (`base` or `overlay`) and the source name, say that the document could not be read, and hold no content and no exception text. ISK-0003 has no code for this and stays unchanged. The document is read before ISK-0003's validator is called, so the validator does not run and writes none of its events 3001 to 3003. The failure counts as one error: its diagnostics values are `errorCount` 1 and `reportedCount` 1, and event 6002 is written with an `ErrorCount` of 1 (settled in `checklist`, from "fails start the same way"). The spec's edge case is reworded to match.

The codes are added to the ISK-0002 contract's Error codes table and `core-contracts` row in place, as ISK-0004 and ISK-0005 did, since adding a code needs no revision of that contract. The harness baselines are regenerated in the same commit. The `hosting` package is new, so it starts at version 1 with no break (rule V2).

## R16. Diagnostics (TR-13, FR-020, FR-021)

**Decision**: Events written through the host's `ILoggerFactory`, under category `Iskender.Hosting`, with `LoggerMessage.Define` as ISK-0003 and ISK-0005 do:

| Event ID | Name | Level | Template |
|---|---|---|---|
| 6001 | `IskenderComposed` | Information | `Iskender composed: modules {Modules}, sections {Sections}, environment {Environment}, revision {Revision}.` |
| 6002 | `IskenderStartRefused` | Error | `Iskender start refused: configuration rejected with {ErrorCount} error(s).` |
| 6003 | `IskenderRegistrationMissing` | Error | `Iskender registration missing: {Missing} required by {RequiredBy}.` |

`{Modules}` is the module names joined by `,`, and `{Sections}` the `name@version` pairs, in ordinal order. `{Environment}` is the name or `(none)`. `{Missing}` is a module name or `<kind> coordinator for <primitive>`, and `{RequiredBy}` a module name, `configuration`, or `MapIskender`. 6001 is written once, after a successful start. 6002 is written once: after ISK-0003's 3002 and 3003 events when the configuration is rejected, and on its own, with an `ErrorCount` of 1, when a document cannot be read (R15). 6003 is written before each `hosting.registration_missing` failure that has a logger: at host start, and at `MapIskender(...)`. `CompositionCheck` writes it through the host's logger when `AddIskender(...)` is missing.

A canary test plants a configuration value, a secret reference name, a database host, and a request route value, and finds none of them in any event, exception, diagnostic, or response (SC-005).

Iskender adds no logging provider, metric exporter, or trace exporter (FR-021). OpenTelemetry subscription stays with ISK-0007.

## R17. The embedded-host fixture and the tests (TR-11, FR-025)

**Decision**:

- **`tests/Iskender.Hosting.Testing`** (class library, test role): `EmbeddedHost`, built by `EmbeddedHost.CreateAsync(...)` or `EmbeddedHost.FromSampleAsync(...)`. It builds a `WebApplication` with `UseTestServer()`, `ValidateScopes` and `ValidateOnBuild` on, a captured logger provider, and the given configuration documents and registrations. It exposes `Services`, `Client`, `Logs`, `StartAsync`, `StopAsync`, and `DisposeAsync`. It starts no process and opens no port. ISK-0007 reuses it for the cross-host lifecycle test.
- **`tests/Iskender.Hosting.Tests`**: the acceptance groups below, the public surface test, the static-state scan, the lifetime matrix, the canary, and the error code tests.

| Group | Proves |
|---|---|
| Embedded composition | US1, SC-002. The sample started 20 times in a row through the fixture, resolving every documented service, with no process started. PostgreSQL resolution without a connection (Testcontainers is not needed, because nothing connects). The single-instance lease, against ISK-0004's PostgreSQL fixture |
| Start-up refusal | US2, SC-003. One fixture configuration for each of ISK-0003's 15 `invalid_request` configuration codes, built from ISK-0003's fixture documents with a test-only module that registers their test schema through `CompositionModule` |
| Coexistence | US3, SC-004. The sample with and without Iskender, compared on status, body, and headers, except `Date`, and on the host's configuration, key by key |
| Lifetimes and state | US4, SC-006. Container validation for every registration combination, the static-state scan, 100 concurrent requests each checking its own context and correlation ID, and disposal |
| Registration mistakes | US5, SC-007. Each row of R8's table, and one test per error code |

**Rationale**: A separate fixture library lets ISK-0007 reference the fixture without referencing these tests. The PostgreSQL single-instance test is the only one that needs a container, so the rest run with no container runtime, and it follows ISK-0005's skip-or-fail rule.

## R18. Repository wiring and documentation (FR-022, FR-023, FR-026)

**Decision**:

- `Iskender.slnx` adds a `/samples/` folder with the sample, and the two test projects. `.github/CODEOWNERS` adds `/samples/`, the sample, and the test projects.
- `Directory.Packages.props` adds `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, and `Microsoft.AspNetCore.TestHost`, each at 10.0.14. Every changed project commits its lock file.
- `Iskender.Contracts.Compatibility.Tests` references `Iskender.Hosting` and commits `baselines/hosting.surface.txt`.
- `SolutionBuildsTests` allows `src/Iskender.Hosting/` sources.
- `.github/scripts/docs-gate.sh` and `CONTRIBUTING.md` already treat `src/Iskender.Hosting/**` as behavior-bearing. `samples/**` is added, since a sample change is a documented behavior.
- The rules contract version 4 is linked from `README.md`, `docs/03`, `docs/04`, `docs/05`, and `.claude/agents/reviewers/architecture-guard.md`, as its Changing section requires. The rule source's `contract` entry points at it.
- The ISK-0002 contract's Contract versions and Error codes tables gain the `hosting` row and the three codes, and its known limit "Embeddable packages are outside the harness" names only `Iskender.Domain`, as AV-5 settles.
- `docs/03` and `docs/04` link to the hosting contract. `docs/04` gains an "Embedding guide" section: adding the packages, choosing modules and adapters, configuration sources and the environment name, start-up failure, the route prefix, and what Iskender does not change in the host. The sample's `README.md` points to it.

## Settled assumptions

| Assumption | Outcome |
|---|---|
| ISK-0003 AV-4 (embedded) | Explicit sources only (TR-2, R6) |
| ISK-0003 AV-6 (embedded) | ISK-0003's precedence, no further layer (TR-4, R6) |
| ISK-0002 AV-5 | `Iskender.Hosting` becomes a `public contract` through rules contract version 4 (R5) |
| This spec's AV-6 | Every new package is MIT (R1), confirmed by `check-license.sh` during `implement` |
