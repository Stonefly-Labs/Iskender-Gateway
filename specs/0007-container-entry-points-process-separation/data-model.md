# Data Model: ISK-0007 — Container Entry Points & Process Separation

Types are named as the plan intends to build them. `Iskender.Hosting`'s public surface, contract `hosting`, does not change (TR-16). Every other public type here is in an embeddable assembly and is not a supported public API (ADR-0030). The reasons are in [research.md](research.md). The rules operators and later specs rely on are in [contracts/container.md](contracts/container.md) and [contracts/hosting.md](contracts/hosting.md), version 2.

## Composition seam: `Iskender.Domain.Ports` (additions)

| Type | Shape |
|---|---|
| `CompositionPlane` (enum) | `DataPlane`, `ControlPlane` |
| `CompositionRoot` (sealed record) | `CompositionRoot(CompositionPlane plane, string module)`. `module` follows `CompositionModule`'s name rule. `const string Requirement = "hosting"`: the requirement name either root meets (research R2). Registered once per collection by `CompositionServices.AddRoot` |

`CompositionModule`, `IValidatedConfiguration`, and `ICompositionStartup` do not change.

## Composition core: `Iskender.Domain.Registry.Composition` (new)

| Type | Lifetime | Shape |
|---|---|---|
| `CompositionDocument` (sealed class, no public constructor) | Value | `static FromFile(string path, ConfigurationDocumentFormat format, string? sourceName = null)`, `static FromText(string sourceName, ConfigurationDocumentFormat format, string text)`, `static FromStream(string sourceName, ConfigurationDocumentFormat format, Func<Stream> open)`. `SourceName`, `Format`. ISK-0003's source-name rule at construction → `core.value.invalid`. Opens nothing at construction |
| `CompositionSources` (sealed record) | Singleton instance | `CompositionDocument? BaseDocument`, `CompositionDocument? OverlayDocument`, `bool ReadEnvironmentVariables`, `string? EnvironmentName`. Copied once by the root |
| `CompositionServices` (static class) | | `AddRoot(IServiceCollection services, CompositionRoot root, CompositionSources sources, IEnumerable<SectionSchema> rootSections)`: registers the root, its module, the schema registry, `CompositionConfiguration`, the snapshot, revision, `CompositionStatus`, and `CompositionLifecycle` as `IHostedService` and `IHostedLifecycleService`. A second root → `hosting.registration_duplicate`. `AddModule(IServiceCollection services, CompositionModule module)`: the seam, as `CompositionRegistration.Add` was, adding `CompositionCheck` once |
| `CompositionConfiguration` | Singleton, `IDisposable` | Implements `IValidatedConfiguration`. `Validate(CancellationToken)`, `Snapshot`, `Revision`. States: `NotValidated` → `Valid(snapshot)` or `Rejected(exception)`. A cancelled run returns to `NotValidated`. `Probe(CompositionSources, ConfigurationSchemaRegistry)`: a static, logger-free validation for the `health` command (research R13) |
| `CompositionLifecycle` | Singleton, `IHostedLifecycleService` | `StartingAsync`: requirement check, validation, events 6001 to 6003. `StartAsync`: each `ICompositionStartup` in module-name order, then the refused-feature check, event 6004 (research R4), then `CompositionStatus` → `Started`. `StoppedAsync`: each start-up's `StopAsync` in reverse order (research R5). `EnsureComposableAsync(CancellationToken)`: the `StartingAsync` checks, callable by an entry point before the host starts (research R9) |
| `CompositionStatus` | Singleton | `CompositionPhase Phase` (`Starting`, `Started`, `Stopped`), changed only by `CompositionLifecycle` |
| `CompositionModules` (static) | | Module lookup, the requirement check with `hosting` met by a root, and the registration failures. Moved from `CompositionGuard` |
| `CompositionCheck` | Singleton, `IHostedService` | With modules and no `CompositionRoot`, writes 6003 naming `hosting` and the first module in ordinal order, and fails with `hosting.registration_missing`. Moved from `Iskender.Infrastructure` |
| `CompositionLog` (static, delegates only) | | Events 6001 to 6004, category `Iskender.Hosting`. Public `RegistrationMissing(ILogger, string missing, string requiredBy)` for the coordination start-up |

## Changes to existing assemblies

| Assembly | Change |
|---|---|
| `Iskender.Hosting` | `AddIskender(...)` maps `IskenderOptions` to `CompositionSources` and calls `CompositionServices.AddRoot` with `CompositionPlane.DataPlane`, module `hosting`, and `HostingSectionSchema.Schema`, then registers `HostingSettings` and the request context. `MapIskender(...)` checks for the `hosting` module as before. `IskenderRuntimeOptions`, `IskenderConfiguration`, `IskenderStartup`, `CompositionGuard`, and `HostingLog` are removed. Public types and signatures do not change |
| `Iskender.Infrastructure` | `CompositionRegistration.Add` forwards to `CompositionServices.AddModule`. `CompositionCheck` and `RegistrationMissing` are removed. `CoordinationStartup` resolves `CompositionRoot` and maps its plane to `SingleInstancePlane`, defaulting to the data plane when none is registered |
| `Iskender.Infrastructure.PostgreSql`, `Iskender.Infrastructure.Redis` | None. Their registrations keep `Requires: ["hosting"]` |

## Control-plane root: `Iskender.Management` (new types)

| Type | Shape |
|---|---|
| `ControlPlaneServiceCollectionExtensions` (static class) | `IServiceCollection AddIskenderControlPlane(this IServiceCollection services, Action<ControlPlaneOptions> configure)`. `null` arguments → `core.value.invalid`. Calls `CompositionServices.AddRoot` with `CompositionPlane.ControlPlane`, module `control-plane`, and no section. A second root → `hosting.registration_duplicate` |
| `ControlPlaneOptions` (sealed class, settable properties) | `CompositionDocument? BaseDocument`, `CompositionDocument? OverlayDocument`, `bool ReadEnvironmentVariables` (default `false`), `string? EnvironmentName` (default `null`) |
| `MigrationOperation` (abstract record) | `MigrationOperation.Apply`, `MigrationOperation.Reverse(int Steps, bool ConfirmDataLoss)`, `MigrationOperation.Status` |
| `MigrationArguments` (static) | `MigrationOperation? Parse(IReadOnlyList<string> args)`: `null` for a usage error (research R14) |
| `MigrationCommand` (sealed class) | `ValueTask<MigrationOutcome> RunAsync(IServiceProvider services, MigrationOperation operation, CancellationToken cancellationToken)`. Validates through `CompositionLifecycle.EnsureComposableAsync`, resolves `IMigrationRunner`, runs one call. Starts nothing |
| `MigrationOutcome` (sealed record) | `int ExitCode`, `string Operation` (`apply`, `reverse`, `status`), `string Outcome` (`succeeded`, `failed`, `current`, `behind`, `ahead`), `int Count`, `ErrorCode? Code` |

## Container library: `Iskender.Infrastructure.Container` (new project)

| Type | Lifetime | Shape |
|---|---|---|
| `ContainerServiceCollectionExtensions` (static class) | | `AddIskenderContainer(this IServiceCollection services)`: module `container`, section `container`, requires `hosting`. Registers `ContainerSettings`, `ContainerLifecycle`, `ReadinessEvaluator` |
| `ContainerSectionSchema` | Static schema | Namespace `container`, version 1, owner `Iskender.Infrastructure.Container`. Fields in the [container contract](contracts/container.md#the-container-section) |
| `ContainerSettings` | Singleton | `int TrafficPort`, `int HealthPort`, `TimeSpan ShutdownDelay`, `TimeSpan ShutdownTimeout`, `TimeSpan ReadinessCacheDuration`, read from the snapshot |
| `ConfigurationRoots` (static) | | `Resolve(IReadOnlyDictionary<string, string?> environment, IFileSystemProbe files)` → `CompositionSources` or a `hosting.configuration_rejected` failure naming the layer (research R8). The default directory is `/etc/iskender` |
| `ContainerPlaneDefinition` (sealed record) | Value | `CompositionPlane Plane`, `string ServiceName`, `Action<IServiceCollection, CompositionSources> Compose`, `Action<WebApplication>? MapTraffic`, `IReadOnlyDictionary<string, ContainerCommand> Commands` |
| `ContainerCommand` (delegate) | | `ValueTask<int> (IReadOnlyList<string> args, CompositionSources sources, ContainerCommandContext context, CancellationToken cancellationToken)` |
| `ContainerEntryPoint` (static class) | | `Task<int> RunAsync(string[] args, ContainerPlaneDefinition definition)`, and an overload that takes the environment, a clock, and a stop token, for tests. Parses arguments, resolves roots, builds the application (research R7), validates (R9), serves, stops (R12), and returns the exit code |
| `ContainerLifecycle` | Singleton, `IHostedLifecycleService` | The `lifecycle` check's phase: `starting`, `started`, `stopping`. `StoppingAsync` writes 7004 and waits `ShutdownDelay` |
| `ReadinessEvaluator` | Singleton | `ValueTask<ReadinessReport> EvaluateAsync(CancellationToken)`. Resolves `IPersistenceHealthProbe?`, `ICoordinationHealthProbe?`, `ISingleInstanceStatus?`, and `CapabilityReport?`. Writes 7003 on a changed reason |
| `ProbeGate<T>` | Owned by the evaluator | `ValueTask<T> GetAsync(CancellationToken)`. One call in flight, shared; a result reused while no older than the cache duration, on the container's `TimeProvider` |
| `ReadinessReport` (sealed record) | Value | `int Version` (1), `bool Ready`, `IReadOnlyList<ReadinessCheck> Checks`, `IReadOnlyList<RefusedFeature> Refused` |
| `ReadinessCheck` (sealed record) | Value | `string Name`, `bool Ready`, `string Reason` |
| `RefusedFeature` (sealed record) | Value | `string Feature`, `string Module`, `string Reason` |
| `HealthEndpoints` (static) | | Maps the health branch (research R10) and writes the report as JSON |
| `ContainerTelemetry` (static) | | `AddIskenderContainerTelemetry(IServiceCollection services, string serviceName, string? otlpEndpoint)` (research R15) |
| `ContainerLog` (static, delegates only) | | Events 7001 to 7006, category `Iskender.Container` |
| `ExitCodes` (static class of constants) | | `Success` 0, `Unexpected` 1, `ConfigurationRejected` 2, `RegistrationMissing` 3, `MandatoryFeatureRefused` 4, `MigrationFailed` 5, `SchemaBehind` 6, `SchemaAhead` 7, `Usage` 64 |

## Entry points

| Project | `Program.cs` |
|---|---|
| `Iskender.Gateway.Host` | Returns `ContainerEntryPoint.RunAsync(args, definition)` with plane `DataPlane`, service name `iskender-data-plane`, `Compose` calling `AddIskender(...)` with the given sources, `AddIskenderContainer()`, `AddIskenderCoordination()`, `AddIskenderPostgreSqlPersistence()`, `AddIskenderPostgreSqlCoordination()`, and `AddIskenderRedisCoordination()`, `MapTraffic` calling `MapIskender("/")`, and no command |
| `Iskender.ControlPlane.Host` | The same with plane `ControlPlane`, service name `iskender-control-plane`, `AddIskenderControlPlane(...)` in place of `AddIskender(...)`, no `MapTraffic`, and the `migrate` command, which parses with `MigrationArguments`, composes the same registrations into a `ServiceCollection`, and runs `MigrationCommand` |

Each host references `Iskender.Infrastructure.Container`, `Iskender.Infrastructure.PostgreSql`, and `Iskender.Infrastructure.Redis`, which the `Iskender.Infrastructure.<Adapter>` pattern approves.

## State transitions

**Process**:

```text
parse args ──usage error──▶ exit 64
   │
resolve roots ──layer rejected──▶ exit 2
   │
build, validate ──rejected──▶ exit 2 ; ──registration missing──▶ exit 3
   │
start: listeners bound, lifecycle "starting"
   │
composition start-ups ──refused mandatory feature──▶ exit 4 ; ──coordinator missing──▶ exit 3
   │
lifecycle "started" ◀──────────────────────────────┐
   │ SIGTERM/SIGINT                                  │ (no transition back)
lifecycle "stopping" → delay → drain → stop start-ups → dispose → exit 0
```

**Single-instance check** (from `ISingleInstanceStatus.State`, read on each evaluation): `unverified` → `held` | `held_by_other` | `lost`; `held` → `lost`; `lost` → `held`; `held_by_other` → `held`. Ready only in `held`, `not_applicable`, and `unverified`.

## Validation rules

- `container.trafficPort` and `container.healthPort`: integers 1,024 to 65,535, which ISK-0003 enforces. Equal ports fail when `ContainerSettings` reads the section, with `config.field.out_of_range` naming `container.healthPort`, before any listener binds (research R9).
- `container.shutdownDelay`: 0 s to 1 min. `container.shutdownTimeout`: 1 s to 5 min. `container.readinessCacheDuration`: 0 s to 30 s.
- `migrate reverse --steps`: 1 to 2,147,483,647.
- `ISKENDER_ENVIRONMENT`: passed to ISK-0003 as given, which applies its environment-name rule. An empty value counts as unset, giving no environment name. An overlay file name is formed only from a name that meets the rule.
- `ISKENDER_CONFIG_DIRECTORY`: a non-empty value names the directory, which must exist. An empty value counts as unset, selecting `/etc/iskender`.
