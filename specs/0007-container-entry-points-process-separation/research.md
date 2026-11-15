# Research: ISK-0007 — Container Entry Points & Process Separation

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-11-15

Each decision records what was chosen, why, and what was rejected. The maintainer decided four questions during `plan` on 2026-11-15 (R1, R6, R11, R14). Each is marked **Maintainer decision**. Every other decision follows from the spec, the contracts it names, or the code as it stands on `main`.

Facts found in the code are cited by path. The approved references, roles, and denied categories are those of the [module dependency rules contract](../0006-host-composition-di-registration/contracts/module-dependency-rules.md), version 4.

## R1. Where the composition core goes, and what "unchanged" means for the hosting suite

**Decision**: The plane-neutral core moves into `Iskender.Domain`, in the new namespace `Iskender.Domain.Registry.Composition`. It holds:

| Moves from | To | Role |
|---|---|---|
| `Iskender.Hosting.Composition.IskenderRuntimeOptions`, and the mapping from `IskenderConfigurationDocument` | `CompositionSources`, `CompositionDocument` | What the configuration is read from |
| `Iskender.Hosting.Configuration.IskenderConfiguration` | `CompositionConfiguration` | Validates once per container, and stores the snapshot or the failure |
| `Iskender.Hosting.Composition.IskenderStartup` | `CompositionLifecycle` | Requirement check and validation in `StartingAsync`, composition start-ups in `StartAsync`, the refused-feature check (R4), and reverse stop (R5) |
| `Iskender.Hosting.Composition.CompositionGuard` | `CompositionModules` | Finds modules, checks requirements, builds the registration failures |
| `Iskender.Hosting.Diagnostics.HostingLog` and `Iskender.Infrastructure.Composition.RegistrationMissing` | `CompositionLog` | Events 6001 to 6004, category `Iskender.Hosting` |
| `Iskender.Infrastructure.Composition.CompositionRegistration.Add` and `CompositionCheck` | `CompositionServices.AddModule` and `CompositionCheck` | The seam every module registration uses, and the start check for a missing root |

`Iskender.Infrastructure.Composition.CompositionRegistration.Add` stays as a public method that forwards to `CompositionServices.AddModule`, because the four adapter registrations and the test fixtures call it. `Iskender.Hosting` keeps `HostingSectionSchema`, `HostingSettings`, the request bootstrap, the route group, and its public types.

`Registry` may use `Iskender.Domain.Ports` and nothing else (rule A17), which is all the core needs: `ConfigurationValidator` and the configuration sources are in `Registry`, and the seam types are in `Ports`. `Iskender.Domain.Ports` itself could not hold the core, because `Ports` may use no module namespace (rule A18) and the core calls `ConfigurationValidator`.

The core needs `IHostedLifecycleService` and `IServiceCollection`, so `Iskender.Domain` takes `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Hosting.Abstractions` at 10.0.15, both MIT and already pinned in `Directory.Packages.props`. Neither is in a category `Iskender.Domain` denies: `aspnetcore` matches the `Microsoft.AspNetCore` prefix and the `Microsoft.AspNetCore.App` framework reference only, and neither package declares a framework reference. **This validates AV-1.** The architecture suite proves it in `implement`, through A19's lock-file and `project.assets.json` checks.

**Maintainer decision (2026-11-15, `plan`)**: US6.4 and SC-009 say ISK-0006's hosting suite passes unchanged. Several of its tests are white-box: `IskenderConfigurationTests` constructs `IskenderConfiguration` and `IskenderRuntimeOptions`, `AddIskenderTests` resolves `IskenderStartup`, and `LifetimesDisposalTests` lists disposed types by name and reads `IskenderConfiguration`'s private `_gate` field through reflection. The maintainer chose to read "unchanged" as **unchanged behavior**: in that suite, and in the same way in `Iskender.Infrastructure.Tests`, only lines that name a moved internal type may change, meaning type and namespace names, constructor calls, and the reflection on private fields. No assertion about observable behavior is changed or removed, and the `hosting` baseline does not change. US6.4 and SC-009 are reworded in `spec.md` to say so.

**Rejected**:

- Keeping Hosting's internal types as facades with their current names and fields, so no test file changes. The validate-once state would exist twice, once in Hosting and once in the core the control plane uses, or the core would carry fields shaped for reflection.
- A type forwarder that keeps `Iskender.Hosting.Configuration.IskenderConfiguration` in `Iskender.Domain`. The namespace is unmapped there, which rule A18 rejects.
- The core in `Iskender.Infrastructure`, which `Iskender.Hosting` cannot reach (rule A8), and a new shared project, both already rejected by TR-2.

## R2. Composition roots, the requirement name, and the plane

**Decision**: A composition root registers one `CompositionRoot` singleton, in `Iskender.Domain.Ports`, naming its plane (`CompositionPlane.DataPlane` or `ControlPlane`) and its root module (`hosting` or `control-plane`). `CompositionServices.AddRoot` is the one method both roots call. A second root on one collection fails at that call with `hosting.registration_duplicate`, naming the second root's module.

Every module registration keeps `Requires: ["hosting"]`. Version 2 of the hosting contract defines `hosting` as the name of the **composition-root requirement**, which either root meets. The requirement check treats `hosting` as met when a `CompositionRoot` is registered, and treats every other name as before. So a module registration with no root still fails at host start with `hosting.registration_missing` and event 6003 naming `hosting`, as today, in both planes.

`CoordinationStartup` reads the plane from `CompositionRoot` and starts that plane's guard, through `SingleInstancePlane` from ISK-0005. Its constructor does not change, and with no `CompositionRoot` registered, as in its direct tests, it uses the data plane.

**Why**: The requirement name, event 6003's text, and the registrations' `Requires` lists are observable behavior that ISK-0006's tests assert, so keeping them meets R1's rule with no edit. A plane carried by the root, rather than inferred from which assembly is loaded, makes the guard's lease name a direct consequence of the registration.

**Rejected**: Renaming the requirement to `composition-root`, which changes event 6003's text, the `missing` diagnostic value, and the assertions that check them. A plane argument on `AddIskenderCoordination()`, which lets a host register the data plane's guard under the control plane's root.

## R3. The control-plane root

**Decision**: `AddIskenderControlPlane(this IServiceCollection services, Action<ControlPlaneOptions> configure)` in `Iskender.Management`, namespace `Iskender.Management`, returning the collection. `ControlPlaneOptions` mirrors `IskenderOptions`: `BaseDocument` and `OverlayDocument` as `CompositionDocument`, `ReadEnvironmentVariables`, and `EnvironmentName`. The root module is `control-plane`, with no configuration section and no route group, since this spec maps no management endpoint.

`Iskender.Management` references only `Iskender.Domain` and `Iskender.Core.Contracts`, and takes the DI and hosting abstractions through `Iskender.Domain`. It denies `cloud`, `provider`, `driver`, and `data-access`, none of which this needs. So the method fits its approved references with no change, which completes AV-1's second half.

**Rejected**: Taking `CompositionSources` directly, which makes a future option a breaking change for callers. A `control-plane` configuration section with no field, which would make every control-plane document carry an empty section.

## R4. A refused mandatory feature fails start in the core

**Decision**: After every composition start-up has run, `CompositionLifecycle` reads the container's `CapabilityReport`, if coordination is composed, and the registered `FeatureRequirement`s. For the first refused feature with `MandatoryPath`, in registration order, it writes event 6004, stops the start-ups already started in reverse order, which releases a held instance lease, and throws `GatewayException` `coordination.capability_unsupported`. The failure's diagnostic values are `feature`, `module`, and `reason`, which are code-defined names, never configuration values. A refused feature without `MandatoryPath` changes nothing at start.

Event 6004 is new in the hosting contract, version 2, and is written by every host the core composes, so an embedded host built with `AddIskender(...)` writes it too (US2.7). The container also writes its own event 7002 when a start fails, naming the error code, the exit code, and for exit code 4 the same feature, module, and reason (TR-14). Event 5002, from ISK-0005, still records every refusal first.

**Why**: TR-7 puts the mapping in the shared core, so both kinds of host behave the same, and requires an event naming the feature, module, and reason in both. Event 6004 is the composition's record, and 7002 the process's.

**Rejected**: Writing only 7002, under `Iskender.Container`, which an embedded host would never write. A new error code, which TR-16 rejects.

## R5. Composition start-ups stop after the server has drained

**Decision**: `CompositionLifecycle` stops its start-ups in `IHostedLifecycleService.StoppedAsync`, which the host runs after every hosted service's `StopAsync`, the web server's included, rather than in `StopAsync`.

**Why**: `WebApplicationBuilder` registers the web server's `GenericWebHostService` before any service the application adds, and the host stops hosted services in reverse registration order, so a stop in `StopAsync` would release the instance lease and stop the guard before the server had drained (TR-11). `StoppedAsync` runs after the drain, in embedded and container hosts alike, and it is still "at host stop" as Start-up rule 8 says. The guard's disposal and the coordination stop already ignore the token, so a stop token that the drain used up still releases the lease.

**Rejected**: Registering the core before the server, which `WebApplicationBuilder` gives no supported way to do. Stopping services concurrently (`HostOptions.ServicesStopConcurrently`), which makes the order undefined.

## R6. The container library

**Maintainer decision (2026-11-15, `plan`)**: A new embeddable project, `Iskender.Infrastructure.Container`, holds what both entry points share and what tests must reach: the `container` section, the configuration roots, the listeners, the health endpoints and readiness evaluator, the stop sequence, the command line and exit codes, OpenTelemetry, and JSON logging.

It matches the `Iskender.Infrastructure.<Adapter>` pattern, so its role (embeddable, packable), approved references (`Iskender.Infrastructure`, `Iskender.Domain`, `Iskender.Core.Contracts`), and denied categories (`provider`) are already in the rules contract. Both hosts may reference the pattern, rule A11 keeps `Iskender.Infrastructure` from reaching it, and tests may reference it. It takes the `Microsoft.AspNetCore.App` framework reference, which its role allows, and does not reach `Iskender.Hosting` or `Iskender.Management`, so neither image gains the other plane's code.

It adds a project, a package, a lock file, a solution entry, and a CODEOWNERS entry for rule C3. No edge, role, or rule changes.

**Rejected**: Adding the same code to `Iskender.Infrastructure`, which would make every adapter, and every embedder that uses one, take ASP.NET Core and OpenTelemetry. Putting it in each host, which no test may reference (rule A2), and which duplicates it.

## R7. The web application the entry points build

**Decision**: Each entry point builds with `WebApplication.CreateEmptyBuilder`, adds Kestrel with `UseKestrelCore()`, routing, and the JSON console logger explicitly, and sets the content root to the application directory. `CreateEmptyBuilder` adds no configuration source, so `appsettings*.json`, environment variables, command-line arguments, `ASPNETCORE_URLS`, and `ASPNETCORE_ENVIRONMENT` reach neither ASP.NET Core nor Iskender (FR-010). The host's own `IHostEnvironment` keeps its default name, which Iskender never reads (hosting contract, Registration rule 3).

**Why**: It is the one builder that starts with no configuration source, so there is nothing to remove, and a later SDK adding a default source cannot reach Iskender.

**Rejected**: `CreateBuilder` or `CreateSlimBuilder` with their sources removed, which depends on knowing every default source the SDK adds.

## R8. Configuration roots

**Decision**: The entry point resolves its sources before building the host:

1. The directory is `ISKENDER_CONFIG_DIRECTORY` when set, otherwise `/etc/iskender`. A named directory that does not exist fails. The default directory may be absent.
2. The base layer is `iskender.yaml` or `iskender.json` in that directory. The overlay layer, only when `ISKENDER_ENVIRONMENT` is set, is `iskender.<environment>.yaml` or `.json`. A layer with both files fails, naming the layer as ambiguous. A layer with neither is a missing layer.
3. `ReadEnvironmentVariables` is always on, and the environment name is `ISKENDER_ENVIRONMENT`, or none.
4. File names are matched exactly, so `Iskender.yaml` is not the base document. The environment name is passed to ISK-0003 as given, which rejects an invalid one with `config.environment.invalid`. An overlay file name is formed only from a name that meets ISK-0003's environment-name rule, so a name such as `../x` never becomes a path. A variable that is present counts as set, even when empty, so an empty `ISKENDER_ENVIRONMENT` is rejected and an empty `ISKENDER_CONFIG_DIRECTORY` fails as a named directory that does not exist (settled in `checklist`, from ISK-0003's environment-name rule 2, which checks a given name before any document is read).

A failure in steps 1 and 2 is reported through the core's failure for an unreadable layer (hosting contract, Start-up rule 4): `hosting.configuration_rejected` with `errorCount` 1, event 6002, and the layer and source name in its diagnostic, never content. The entry point exits 2.

`ISKENDER_CONFIG_DIRECTORY` and `ISKENDER_ENVIRONMENT` have one underscore, so ISK-0003's `ISKENDER__` reader never treats them as configuration paths.

**Why**: TR-3. Resolving the files before the host is built keeps every source visible to the operator and to event 6001.

**Rejected**: Glob matching, or a configurable file name, which TR-3 rejects.

## R9. Validation before any listener binds

**Decision**: After `Build()` and before `StartAsync`, the entry point calls the core's requirement check and validation directly, the same calls `StartingAsync` makes, and maps their failures to exit codes 2 and 3. Only then does the server start, with Kestrel's listeners bound from `ContainerSettings`, which a `ConfigureKestrel` callback resolves from the application's services. `StartingAsync` then finds the stored snapshot.

ISK-0003 has no rule across two fields, so `trafficPort` equal to `healthPort` is caught when `ContainerSettings` reads the validated section, as `PostgreSqlSettings` catches `poolMinimum` above `poolMaximum`: `config.field.out_of_range`, naming `container.healthPort`. The entry point reads `ContainerSettings` straight after validation and before any listener binds, and maps that failure to exit code 2 with event 7002.

**Why**: Kestrel needs the validated `container` section to know its ports, and FR-012 requires a rejected configuration to stop the process before readiness can answer. Validating first means no port is bound for a configuration that will be rejected.

**Rejected**: Binding fixed ports and validating later, which contradicts TR-6, and reading the ports outside validation, which bypasses ISK-0003.

## R10. Traffic and health listeners are told apart by local port

**Decision**: The pipeline branches on `HttpContext.Connection.LocalPort`. A request on the health port enters a branch that serves only `GET /health/live` and `GET /health/ready`, and answers anything else with a plain 404. A request on the traffic port enters the branch that holds `MapIskender("/")`, and on the data plane only. The control plane binds only its health port.

**Why**: The local port is set by the listener the connection arrived on. ASP.NET Core's `RequireHost` matches the `Host` header, which a caller chooses, so a caller could reach the health endpoints through the traffic port by sending `Host: x:8081`. US3.5 is tested with that header.

**Rejected**: `RequireHost`, for that reason, and two `WebApplication`s in one process, which would compose Iskender twice.

## R11. Readiness

**Decision**: `ReadinessEvaluator`, in the container library, computes one report per request from five checks in TR-8's order. The persistence and coordination probes run concurrently within one evaluation, so FR-019's bound is the longest probe's, not their sum (settled in `checklist`). Each probe is wrapped in a `ProbeGate`: one call in flight at a time, shared by every request that arrives while it runs, and a result reused while it is no older than `readinessCacheDuration`. The probe call runs on its own token, bounded by the probe's own timeout, so one caller hanging up does not cancel the call others are waiting on. Each caller waits with its own request token.

The single-instance check maps the guard's states:

| State | Readiness | Reason |
|---|---|---|
| `held`, `not_applicable`, `unverified` | Ready | The state's code |
| `lost` | Not ready | `single_instance_lost` |
| `held_by_other` | Not ready | `single_instance_held_by_other` |

**Maintainer decision (2026-11-15, `plan`)**: TR-8 did not map `held_by_other`, which arises when another process already holds the plane's lease and no mandatory feature fails the start. The maintainer chose **not ready**, with the new reason `single_instance_held_by_other`, until the guard takes the lease, so that a process that cannot show it is the only instance never takes traffic. TR-8 and FR-018 are updated in `spec.md`.

The report's format and every reason are in the [container contract](contracts/container.md#readiness). The evaluator keeps the last reason of each check, and writes event 7003 once for each change. A `null` probe, when a module is not composed, reports `not_composed` and is ready, which only a test host can produce, since both entry points compose both probes.

**Why**: TR-8 and FR-019. Sharing the call, rather than only rate-limiting it, gives 100 concurrent requests one probe call (US3.4).

**Rejected**: ASP.NET Core's health checks package, whose report format, status names, and caching are its own, and which runs every check on each request.

## R12. The stop sequence

**Decision**: `ContainerLifecycle`, an `IHostedLifecycleService` in the container library, owns the container's phase:

1. `StoppingAsync`, which the host runs first on a stop signal, sets the lifecycle check to `stopping`, writes event 7004, and waits `shutdownDelay` while the server keeps serving.
2. The host then stops the server, which stops accepting connections and drains for the rest of the host's shutdown timeout. The entry point sets `HostOptions.ShutdownTimeout` to `shutdownDelay` + `shutdownTimeout`, so the drain gets `shutdownTimeout`.
3. `CompositionLifecycle.StoppedAsync` stops the start-ups in reverse order, releasing the lease (R5).
4. The entry point disposes the application, which disposes the container, the Redis coordinator, and the data sources, writes event 7005, and returns 0.

The phase is `starting` from process start until `CompositionLifecycle` reports its start-ups finished, through `CompositionStatus` in the core, then `started`. A signal during start-up cancels the host's start through its stopping token. The signal turns the lifecycle check `stopping` at once and writes 7004, as after start-up. The entry point awaits `StartAsync`, whether it finishes or is cancelled, before it calls `StopAsync` and disposes, so the Redis coordinator is never disposed during its first connection (FR-022). It then writes 7005 and returns 0, as SC-005 requires (settled in `checklist`). A second signal is absorbed by the host's console lifetime, which never shortens the sequence.

**Why**: TR-11 and FR-021. Each step is a host callback the framework already orders, so the sequence needs no thread of its own.

**Rejected**: Disposing on the first signal, which TR-11 rejects. Delaying in `ApplicationStopping`, whose callbacks run synchronously and block the host's stop.

## R13. The command line and exit codes

**Decision**: `ContainerEntryPoint.RunAsync(args, definition)` parses arguments before it reads any configuration, and returns the exit code that `Program.Main` returns:

- No argument: serve.
- `health live` or `health ready`: read the configuration through the core with a null logger, so the check writes no events, take `container.healthPort`, send `GET http://127.0.0.1:<port>/health/<check>`, and exit 0 on `200` and 1 on anything else, including no listener and a rejected configuration. The request waits up to 30 seconds, which is Docker's default health-check timeout, a safety bound, not a performance figure.
- A command the plane's definition adds: the control plane adds `migrate` (R14).
- Anything else: a usage message to standard error, and exit 64.

Every failure maps to one code, as TR-10 lists. An exception the entry point did not expect writes event 7005 with exit code 1 and the exception's type name, never its message.

**Why**: The health command needs the health port, which only the validated configuration holds. Reading it through the core gives the same answer the server used, and a null logger keeps a 10-second health check from writing composition events every time.

**Rejected**: Taking the port as an argument, which can drift from the configuration. Calling `curl`, which the chiseled image does not have (R16).

## R14. The migration command

**Maintainer decision (2026-11-15, `plan`)**: TR-9 says the command "composes only persistence and the shipped migration modules". A plane validates only the sections it registers (TR-4), so a command that registered only persistence would reject the control plane's own documents with `config.namespace.unknown`. The maintainer chose: **the command makes the same registration calls as the serving control plane, so the same documents validate, and starts nothing**. It runs the requirement check and validation, resolves `IMigrationRunner`, and runs one operation. It runs no `ICompositionStartup`, so it starts no guard, runs no capability check, connects to no coordinator, and binds no listener. TR-9 is reworded in `spec.md`.

The logic is `MigrationCommand`, in `Iskender.Management`, which takes an `IServiceProvider` the caller composed. The control-plane host builds a `ServiceCollection` with `ValidateOnBuild` and `ValidateScopes`, its registrations, and the JSON logger, and passes it. Tests compose the same registrations with a test `IDatabaseCredentialProvider`. `MigrationCommand` reaches persistence only through `IMigrationRunner`, a port in `Iskender.Domain.Ports`, so `Iskender.Management` still reaches no infrastructure assembly (rule A3).

| Operation | Runner call | Exit |
|---|---|---|
| `migrate apply` | `ApplyAsync` | 0, or 5 on any `persistence.*` failure |
| `migrate reverse [--steps <n>] [--confirm-data-loss]` | `ReverseAsync` with `Steps` n, default 1, and `ConfirmDataLoss` | 0, or 5 |
| `migrate status` | `GetStatusAsync` | 7 if any module has an unknown migration, a version above its range, or a checksum mismatch; otherwise 6 if any has a pending migration or a version below its range; otherwise 0; 5 if the call fails |

`--steps` takes an integer from 1 to 2,147,483,647. Zero, a negative or non-numeric value, a repeated or unknown option, or a missing operation exits 64 before any configuration is read. A rejected configuration exits 2, and a missing registration 3. Event 7006 names the operation, the outcome, and the count applied or reversed. It is written once for every run whose arguments parse, so exit codes 2 and 3 write it with outcome `failed` and count 0. A `migrate` run serves nothing, so it writes none of 7001 to 7005 (settled in `checklist`, from TR-14's "migration command finished").

**Why**: One set of documents serves the plane and its migration job, and nothing a started plane does runs in the job.

**Rejected**: A new adapter method that registers only persistence and the migration modules, which needs separate documents for the job.

## R15. Telemetry and logs

**Decision**: The container library calls `AddOpenTelemetry()`, with resource `service.name` `iskender-data-plane` or `iskender-control-plane`, adds the activity sources and meters `Iskender.Coordination` and `Iskender.Persistence`, the first through `CoordinationTelemetry.SourceName` in `Iskender.Infrastructure` and the second as the literal the persistence contract's Telemetry section fixes, because the container library's approved references do not include `Iskender.Infrastructure.PostgreSql` and `PostgreSqlAdapter.DiagnosticsName` is there (settled in `checklist`). A test, which may reference both, asserts the literal equals `PostgreSqlAdapter.DiagnosticsName`, and adds the OTLP exporter only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set in the process environment. Packages:

| Package | Version | License | Dependencies |
|---|---|---|---|
| `OpenTelemetry.Extensions.Hosting` | 1.21.0 | Apache-2.0 | `OpenTelemetry`, `Microsoft.Extensions.Hosting.Abstractions` |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.21.0 | Apache-2.0 | `OpenTelemetry` |
| `OpenTelemetry` (transitive) | 1.21.0 | Apache-2.0 | `OpenTelemetry.Api.ProviderBuilderExtensions`, and `Microsoft.Extensions.*` packages, which are MIT |

These were read from the packages' nuspec files on nuget.org on 2026-11-15. The OTLP exporter no longer depends on `Google.Protobuf` or `Grpc.*`. Apache-2.0 and MIT are on `check-license.sh`'s list, **which validates AV-2**, and the script proves it over the lock files in `implement`. The `Microsoft.Extensions.*` versions are pinned to 10.0.15 by central transitive pinning.

Logs go to standard output through the JSON console formatter, with `Microsoft` categories at `Warning` and `Iskender` categories at `Information`, so ASP.NET Core's request logs, which hold paths and hosts, are never written. Scopes are off.

**Why**: TR-13. The SDK reads `OTEL_*` variables through its own configuration, and the exporter's presence is decided by the entry point, so an unset endpoint never creates one.

**Rejected**: `OpenTelemetry.Instrumentation.AspNetCore`, which records request routes, and Serilog, which adds packages for what the framework already does.

## R16. Images

**Decision**: One multi-stage Dockerfile per plane, at `src/Iskender.Gateway.Host/Dockerfile` and `src/Iskender.ControlPlane.Host/Dockerfile`, built with the repository root as context. A root `.dockerignore` keeps `bin/`, `obj/`, `.git/`, `tests/`, and `TestResults/` out.

| Stage | Base | Digest (2026-11-15) |
|---|---|---|
| Build | `mcr.microsoft.com/dotnet/sdk:10.0.404-noble` | `sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb` |
| Runtime | `mcr.microsoft.com/dotnet/aspnet:10.0.15-noble-chiseled` | `sha256:7c625e44e5d588b4d7b2a913e94ca4bd5098ce47442da404829e4c7936a764e1` |

The build stage copies the root build files, `global.json`, and the plane's project files and lock files, runs `dotnet restore --locked-mode` for the host project, copies `src/`, and runs `dotnet publish` in `Release` with `UseAppHost=false`. The runtime stage copies the published output to `/app`, owned by root and not writable by the process, sets `USER 1654:1654`, the chiseled image's `app` user, sets `WORKDIR /app`, exposes 8080 and 8081, sets the OCI labels `org.opencontainers.image.source`, `.revision` from a build argument, and `.licenses` `MIT`, and runs the exec-form `ENTRYPOINT ["dotnet", "/app/<host>.dll"]`, so container arguments reach the command line. It declares no `HEALTHCHECK`, since the orchestrator chooses its probes, and the operator guide gives the exec-form `health` command (settled in `checklist`).

The chiseled image has no shell, package manager, or `curl`, runs as non-root by default, and sets `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true`. Iskender formats with the invariant culture throughout, so invariant globalization changes no behavior. The only path .NET writes at run time is its diagnostics socket under `/tmp`, so the root filesystem can be read-only with `/tmp` as a `tmpfs`. The runtime needs no Linux capability, since both ports are above 1024. The image's packages come from Ubuntu under their own licenses, which permit redistribution, and .NET is MIT. **This validates AV-4**. The image is a build output, not part of the MIT source tree, and `check-license.sh` checks NuGet packages, not base images.

**Why**: TR-12 and FR-026. A digest is immutable, and a numeric user lets `runAsNonRoot` check it.

**Rejected**: The `-extra` chiseled image, which adds ICU and time zone data that nothing uses. A self-contained or trimmed publish, which needs trimming analysis that the adapters have not had. Copying a host-built publish output into a runtime-only image, which TR-12's multi-stage build rules out and which would build differently on each machine.

## R17. The Compose fixture and the container tests

**Decision**: A new test project, `tests/Iskender.Container.Tests`, holds the in-process tests of the container library and the image tests. Its Compose topology is `tests/Iskender.Container.Tests/compose/compose.yaml`, separate from ISK-0008's developer Compose.

- **Images** are built once per test run with `docker build`, tagged with a run ID, and removed afterwards.
- **One Compose project per run** holds a service for each scenario of User Story 1 and each plane, each with its own configuration directory under `compose/scenarios/`, plus PostgreSQL 17 and Valkey 8 at the digests the ISK-0004 and ISK-0005 fixtures pin. Every scenario starts together, so one run is one `docker compose up`.
- Each service is `read_only: true`, with `/tmp` as a `tmpfs`, `cap_drop: [ALL]`, `security_opt: [no-new-privileges:true]`, and `stop_grace_period: 40s`, which exceeds the default `shutdownDelay` + `shutdownTimeout` + 5 seconds (35 seconds). Health ports are published on `127.0.0.1` with ports Docker assigns, read back with `docker compose port`.
- The tests read each container's state and exit code from `docker inspect`, its liveness and readiness through the published port, and its logs from `docker compose logs`. SC-002's 5 consecutive runs are 5 `up` and `down` cycles in one test.
- Image tests use `docker image inspect` for the user and labels, and `docker create` with `docker export` for the file list, with each entry's owner and mode bits from the tar headers, which shows assemblies and setuid or setgid files.
- The tests drive the `docker` command line through `Process`, since Testcontainers for .NET has no Compose support, and follow ISK-0005's rule: skipped with the reason when no container runtime answers, and failing when `CI` is `true`.

GitHub's `ubuntu-24.04` runner provides Docker Engine with BuildKit and Compose v2, and these tests run under the existing check "4. Test". No workflow step is added, and CI stays at 10 checks. **This validates AV-3**. Building inside Docker restores from nuget.org, so a local run with a container runtime needs network access, as pulling the fixtures' images already does.

**Rejected**: Testcontainers' image builder and container API without Compose, which TR-17 rules out. A separate CI check, which FR-027 rules out.

## R18. The cross-host lifecycle test

**Decision**: US5.1 and US5.2 send the same requests to the data-plane container, under `/`, and to the hosting sample through `EmbeddedHost.FromSampleAsync`, under `/iskender`, and compare each response's status, `Content-Type`, envelope, error code, and correlation behavior, ignoring generated IDs. US5.3 compares event 6001's revision between the data-plane container and an `EmbeddedHost` composed with TR-5's data-plane registrations and the same documents.

**Why**: The revision is derived from the whole validated snapshot, and the sample registers neither `persistence` nor `container`, so the data plane's documents cannot validate in the sample and no revision could match. The registrations TR-5 lists are fixed in the container contract, and the test lists the same calls.

**Rejected**: Changing the sample to compose persistence, which would make it need a database and change ISK-0006's evidence.

## R19. Tests by layer

| Layer | Project | Covers |
|---|---|---|
| Core | `Iskender.Domain.Tests` | Roots, the requirement name, the duplicate root, the plane, the refused-feature check, stop order, `CompositionStatus` |
| Embedded | `Iskender.Hosting.Tests` | ISK-0006's suite under R1's rule, and an embedded start failing on a refused mandatory feature (US2.7) |
| Coordination | `Iskender.Infrastructure.Tests` | The guard's plane from the root |
| Container library in process | `Iskender.Container.Tests` | Configuration roots, the `container` section, both listeners, readiness for every reason with stub probes, liveness probe counts, concurrency, the stop sequence, the command line and every exit code, the migration command against the ISK-0004 fixture with a test credential provider, the single-instance lease scenarios, the canary |
| Images and Compose | `Iskender.Container.Tests` | User Story 1, exit codes 2 and 64 and the migration exit codes from real images, image inspection, the cross-host test |
| Rules | `Iskender.Architecture.Tests` | Rules contract version 5, the new project's references and categories |
| Surface | `Iskender.Contracts.Compatibility.Tests` | No change to `hosting`, `core-contracts`, `providers-abstractions`, or `policy-sdk` |

Exit code 3 cannot come from the real images, whose registrations are fixed, so it is produced in process, through `ContainerEntryPoint` with a definition that omits a required module.

## R20. Static state and public surface

**Decision**: Every new production type follows the hosting contract's Static state rule, and the existing reflection scan covers the new project automatically, since it reads every production assembly under `src/`. `ProbeGate` and `ReadinessEvaluator` are container singletons. `Iskender.Hosting`'s public surface does not change, so its baseline does not either. The new public types in `Iskender.Domain`, `Iskender.Management`, and `Iskender.Infrastructure.Container` are embeddable, outside the harness, and prerelease under ADR-0030 (TR-16).

## R21. Documentation and repository wiring

**Decision**: `docs/04` gains a "Container entry points" operator guide, and its Database migrations and Coordination runbooks gain how the command and readiness work. The links to the hosting contract in `docs/03` and `docs/04` move to version 2, and the links to the rules contract, in the files its Changing section lists, move to version 5. The Dockerfiles sit under the host directories, which are already behavior-bearing and covered by CODEOWNERS. CODEOWNERS gains `/src/Iskender.Infrastructure.Container/`, `/tests/Iskender.Container.Tests/`, and `/.dockerignore`.

## R22. The rules contract, version 5

**Decision**: Version 5 changes no edge, role, home, or rule. It records two facts: the `docs/03` expectation "Same request lifecycle in embedded and container hosts" is enforced, by ISK-0006's fixture and this spec's cross-host test, and `Iskender.Infrastructure.Container` is an adapter under the existing pattern. The rule source's version and contract path change with it, and rule C1 compares them.

The known limit on repeating the SDK property enumeration for SDK 10.0.404 stays, reworded only to drop its stale "before ISK-0007 starts". That follow-up is a `chore/` pull request, not this spec's work, and ISK-0007 changes no SDK. Version 5 also says that ISK-0007 adds no executable, since `migrate` is a command of the control-plane host.
