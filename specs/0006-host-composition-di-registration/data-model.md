# Data Model: ISK-0006 — Host Composition & DI Registration

Types are named as the plan intends to build them. Public types of `Iskender.Hosting` are versioned by the compatibility harness as contract `hosting`. Every other type here is in an embeddable assembly and is not a supported public API (ADR-0030). The reasons are in [research.md](research.md), and the rules a host relies on are in [contracts/hosting.md](contracts/hosting.md).

## Public surface: `Iskender.Hosting` (contract `hosting`, version 1)

| Type | Shape |
|---|---|
| `IskenderServiceCollectionExtensions` (static class) | `IskenderBuilder AddIskender(this IServiceCollection services, Action<IskenderOptions> configure)`. `null` arguments → `core.value.invalid`. A second call on one collection → `hosting.registration_duplicate` |
| `IskenderEndpointRouteBuilderExtensions` (static class) | `RouteGroupBuilder MapIskender(this IEndpointRouteBuilder endpoints, string prefix)`. A `null` or empty prefix, or one that does not start with `/` → `core.value.invalid`. No `AddIskender(...)` → `hosting.registration_missing` |
| `IskenderOptions` (sealed class, settable properties) | `IskenderConfigurationDocument? BaseDocument`, `IskenderConfigurationDocument? OverlayDocument`, `bool ReadEnvironmentVariables` (default `false`), `string? EnvironmentName` (default `null`). Read once, inside `AddIskender(...)` |
| `IskenderConfigurationDocument` (sealed class, no public constructor) | `static FromFile(string path, IskenderDocumentFormat format, string? sourceName = null)`, `static FromText(string sourceName, IskenderDocumentFormat format, string text)`, `static FromStream(string sourceName, IskenderDocumentFormat format, Func<Stream> open)`. `string SourceName`, `IskenderDocumentFormat Format`. The source name follows ISK-0003's rule (1 to 256 characters, no control character, U+2028, or U+2029), checked at construction → `core.value.invalid`. Nothing is opened or read at construction |
| `IskenderDocumentFormat` (enum) | `Json`, `Yaml` |
| `IskenderBuilder` (sealed class, no public constructor) | `IServiceCollection Services` |

Each type carries `[ContractVersion("hosting", 1)]`. No public member names an `Iskender.Domain` type (FR-018).

## Internal to `Iskender.Hosting`

| Type | Lifetime | Shape |
|---|---|---|
| `IskenderRuntimeOptions` (sealed record) | Singleton instance | The copied options: base, overlay, environment-variable opt-in, environment name |
| `IskenderConfiguration` | Singleton | Implements `IValidatedConfiguration`. `Validate(CancellationToken)` runs once (research R7). States: `NotValidated` → `Valid(snapshot)` or `Rejected(exception)`. A cancelled run returns to `NotValidated` |
| `IskenderStartup` | Singleton, `IHostedLifecycleService` | `StartingAsync`: requires check, validation, event 6001 or 6002. `StartAsync`: every `ICompositionStartup.StartAsync`, in module-name order. `StopAsync`: every `StopAsync`, in reverse order |
| `HostingSectionSchema` | Static schema | Namespace `hosting`, version 1, owner `Iskender.Hosting`, field `requestTimeout` (research R13) |
| `HostingSettings` | Singleton | `TimeSpan RequestTimeout`, read from the snapshot |
| `RequestContextHolder` | Scoped | `GatewayExecutionContext? Context`, set once by the bootstrap. A second set → `core.context.already_resolved` |
| `RequestBootstrap` | Endpoint filter on the group | Builds `CorrelationContext` and `GatewayExecutionContext`, resolves the revision, fills the holder |
| `HostingLog` | Static, delegates only | Events 6001 to 6003 |
| `CompositionGuard` | Static helpers | Duplicate detection for `AddIskender(...)`, and the `hosting` marker check for `MapIskender(...)` |

`GatewayExecutionContext` is registered as a scoped factory that returns the holder's context, or fails with `core.context.not_resolved` outside an Iskender request.

## Composition seam: `Iskender.Domain.Ports`

| Type | Shape |
|---|---|
| `CompositionModule` (sealed class) | `CompositionModule(string name, IEnumerable<SectionSchema> sections, IEnumerable<string> requires)`. `Name`: 1 to 64 characters of `a-z`, `0-9`, `-`, starting with a letter. `Sections` and `Requires`: no `null` items, no repeats. Bad input → `core.value.invalid`. Owned by ISK-0006 |
| `IValidatedConfiguration` (interface) | `ConfigurationSnapshot Snapshot { get; }`. Fails with `hosting.configuration_rejected` when the configuration was rejected |
| `ICompositionStartup` (interface) | `string ModuleName { get; }`, `ValueTask StartAsync(ConfigurationSnapshot snapshot, CancellationToken cancellationToken)`, `ValueTask StopAsync(CancellationToken cancellationToken)` |

These name only `Iskender.Core.Contracts` and .NET types, so rule A18 holds and `Iskender.Domain` takes no package.

## `Iskender.Infrastructure`

| Type | Shape |
|---|---|
| `Composition.CompositionRegistration` (static class, public) | `void Add(IServiceCollection services, CompositionModule module)`: a repeat of `module.Name` → `hosting.registration_duplicate`. Adds `CompositionCheck` once per collection |
| `Composition.CompositionCheck` (internal, `IHostedService`) | At start: no `hosting` module → event 6003 and `hosting.registration_missing`. Otherwise nothing |
| `Coordination.CoordinationServiceCollectionExtensions` (static class, public) | `IServiceCollection AddIskenderCoordination(this IServiceCollection services)` |
| `Coordination.CoordinatorRegistration` (sealed class, public) | `CoordinatorKind Kind`, `Func<IServiceProvider, IAtomicOperations>? Atomic`, `Func<IServiceProvider, ILeaseCoordinator>? Leases`, `Func<IServiceProvider, IFencingGuard>? Fencing`, `Func<IServiceProvider, IVersionedCache>? Cache`, `Func<IServiceProvider, ICoordinationHealthProbe> Health`, `CoordinatorCapabilities Capabilities`. A second registration of one kind → `hosting.registration_duplicate` |
| `Coordination.CoordinationStartup` (internal, `ICompositionStartup`) | The six steps in research R10 |
| `Coordination.ComposedCoordination` (internal, singleton) | After start: the selected ports, the guard, the `CapabilityReport`, and the `CoordinationGate`. Before start → `coordination.capability_unsupported` |

## `Iskender.Infrastructure.PostgreSql`

| Type | Shape |
|---|---|
| `PostgreSqlServiceCollectionExtensions` (static class, public) | `AddIskenderPostgreSqlPersistence(this IServiceCollection)`, `AddIskenderPostgreSqlCoordination(this IServiceCollection)` |
| `Diagnostics.HeldConnections` (internal sealed class) | Replaces the static dictionary in `PersistenceTelemetry`. One per `PostgreSqlDataSources`, with its own `Meter` and the `iskender.persistence.pool.connections` gauge, disposed with the data sources (research R11) |

## `Iskender.Infrastructure.Redis`

| Type | Shape |
|---|---|
| `RedisServiceCollectionExtensions` (static class, public) | `AddIskenderRedisCoordination(this IServiceCollection)` |

## Modules

| Module | Registered by | Sections | Requires |
|---|---|---|---|
| `hosting` | `AddIskender(...)` | `hosting` | |
| `coordination` | `AddIskenderCoordination()` | `coordination` | `hosting` |
| `persistence-postgresql` | `AddIskenderPostgreSqlPersistence()` | `persistence` | `hosting` |
| `coordination-postgresql` | `AddIskenderPostgreSqlCoordination()` | | `coordination`, `persistence-postgresql` |
| `coordination-redis` | `AddIskenderRedisCoordination()` | | `coordination` |

## Configuration: `hosting` section, version 1

| Field | Type | Default | Constraints | Development-only |
|---|---|---|---|---|
| `requestTimeout` | `duration` | `100s` | `1s` to `1h` | No |

## Flow and states

```text
AddIskender(...) and module registrations    (any order; no I/O)
        │
builder.Build()                              (host's container; ValidateOnBuild checks factories only)
        │
host start ── StartingAsync ──┬─ Requires check ── missing ──▶ 6003, hosting.registration_missing
                              ├─ Validate once ─── rejected ─▶ 3002…, 3003, 6002, hosting.configuration_rejected
                              └─ valid ─▶ 6001
              StartAsync ─────┬─ coordination: selected coordinators registered? ── no ─▶ 6003, hosting.registration_missing
                              └─ guard start, capability check (5001/5002), gate built
        │
requests under prefix ── RequestBootstrap ── catch-all ──▶ 404 core.resource.not_found envelope
        │
host stop ── StopAsync ── guard stopped, lease released
        │
container disposed ── every Iskender singleton disposed
```

## Test fixtures

| Fixture | Project | Use |
|---|---|---|
| `EmbeddedHost` | `tests/Iskender.Hosting.Testing` | Builds and starts an in-process host on the in-memory server, from the sample or from given registrations and documents |
| `CapturedLog` | `tests/Iskender.Hosting.Testing` | Records every event with its ID, level, and rendered message, for the event and canary tests |
| `TestSchemaModule` | `tests/Iskender.Hosting.Tests` | Registers ISK-0003's test schema through `CompositionModule`, so ISK-0003's fixture documents can drive start-up refusal |
| Canary values | `tests/Iskender.Hosting.Tests` | A configuration value, a secret reference name, a database host, and a request route value, searched for in every output (SC-005) |
| PostgreSQL | `tests/Iskender.Infrastructure.PostgreSql.Tests` fixture | The single-instance lease test, with ISK-0005's skip-or-fail rule |
