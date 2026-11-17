# Feature Specification: ISK-0007 — Container Entry Points & Process Separation

**Feature Branch**: `feature/0007-container-entry-points-process-separation`

**Created**: 2026-11-15

**Status**: Draft

**Input**: User description: "Create the specification **ISK-0007 — Container Entry Points & Process Separation** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Create independent control-plane and data-plane entry points, startup validation and graceful host lifecycle**. In-scope behaviors and artifacts: **OCI Dockerfiles; nonroot; health/readiness; configuration roots; split-process strategy**. Explicitly exclude: **No production cloud provisioning or comprehensive admin UI**. Prerequisites: **ISK-0003, ISK-0004, ISK-0005, ISK-0006**; do not assume unavailable implementation already exists. ISK-0001 already made `Iskender.Gateway.Host` the data-plane host and created `Iskender.ControlPlane.Host` as the control-plane host, both empty shells, so give them their entry-point behavior rather than creating new projects. Follow the current module dependency rules contract, linked from the Module boundaries section of @docs/03-domain-and-technical-architecture.md: the data-plane host must not reach `Iskender.Management`, the control-plane host must not reach `Iskender.Hosting`, and no project, test projects included, may reference either host, so host tests run the process or test through a library. ISK-0003 already published `ConfigurationValidator` in `Iskender.Domain.Registry`, which takes at most one base document, one overlay document for the active environment, and `ISKENDER__` environment variables, and treats every environment name other than `development` as production-grade. Follow the current configuration contract, linked from the Persistence, publication and distributed coordination section of @docs/03-domain-and-technical-architecture.md: map each entry point's configuration roots onto those sources, refuse to start or report not ready on a rejected configuration, and settle that spec's assumptions AV-4 and AV-6 for the container topologies. ISK-0004 already published `IMigrationRunner` and `IPersistenceHealthProbe` in `Iskender.Domain.Ports`, implemented in `Iskender.Infrastructure.PostgreSql`, which applies or reverses migrations only when a host calls the runner, under a migration login that a data-plane process need not hold. Follow the current persistence contract, linked from the Persistence, publication and distributed coordination section of @docs/03-domain-and-technical-architecture.md: expose applying and reversing migrations as an explicit operation that no process runs on start-up, map each of the probe's 10 reasons onto readiness without calling the probe from liveness, keep a replica ready while the reason is `migration_in_progress`, as the maintainer decided in ISK-0004, and settle that spec's assumption AV-2. ISK-0005 already published the coordination ports in `Iskender.Domain.Ports`, which modules reach only through `CoordinationGate`, the local substitutes, the `coordination` section schema, and the single-instance guard in `Iskender.Infrastructure`, the public `RedisCoordinator` in `Iskender.Infrastructure.Redis`, and the PostgreSQL coordinator, fencing guard, and `coordination` migration module in `Iskender.Infrastructure.PostgreSql`, and composed none of them into a host. Follow the current coordination contract, linked from the Persistence, publication and distributed coordination section of @docs/03-domain-and-technical-architecture.md: compose the coordinator configured for each primitive into the control-plane host, include the `coordination` migration module in the explicit migration operation, start the control plane's single-instance guard before running `CoordinationCapabilityChecker` once at its start-up, map the five refusal reasons in each plane's capability report onto start-up failure or readiness and the coordination health probe's seven reasons onto readiness without calling the probe from liveness, dispose the Redis coordinator only on shutdown, subscribe OpenTelemetry to the `Iskender.Coordination` sources, and settle ISK-0005's assumption AV-4. ISK-0006 already published `AddIskender(...)` and `MapIskender(prefix)` in `Iskender.Hosting`, which validate the configuration once per container when the host starts and fail its start with `hosting.configuration_rejected` or `hosting.registration_missing`, and one registration method per infrastructure assembly: `AddIskenderCoordination()`, `AddIskenderPostgreSqlPersistence()`, `AddIskenderPostgreSqlCoordination()`, and `AddIskenderRedisCoordination()`. In a host composed that way, start-up checks that each configured coordinator is registered, starts the data plane's single-instance guard, and runs `CoordinationCapabilityChecker` once, keeping its results as the container's `CapabilityReport` without failing start on a refusal, and the container disposes every adapter when the host stops. Follow the current hosting contract, linked from the Module boundaries section of @docs/03-domain-and-technical-architecture.md: build the data-plane host from those methods rather than composing the adapters again, map their start-up failures and that report onto start-up and readiness, run the cross-host lifecycle test with the `EmbeddedHost` fixture in `tests/Iskender.Hosting.Testing`, and settle ISK-0006's assumptions AV-1 and AV-2. Each of those registrations requires `AddIskender(...)`, and the guard they start is the data plane's, while the control-plane host must not reach `Iskender.Hosting`, so compose the control plane through a revision of the hosting contract rather than around it. ISK-0006 settled the configuration contract's AV-4 and AV-6 for embedded hosts: Iskender reads only the documents named in `IskenderOptions`, never the host's `IConfiguration`, and never derives its environment name from the host's. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Split containers launch in Compose and fail readiness with accurate missing-dependency diagnostics**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify."

## Overview

The constitution is present at `.specify/memory/constitution.md` (version 1.1.0) and governs this spec.

Control plane and data plane must be independently deployable (ADR-0003, Constitution II), and Iskender ships as an OCI container of equal standing with the embedded library (ADR-0004). Today both hosts are empty shells: `Iskender.Gateway.Host` and `Iskender.ControlPlane.Host` build a web application with no services, no configuration, no health endpoints, and no image. ISK-0006 composed the configuration engine, persistence, and coordination into an embedded ASP.NET Core host, but every registration it added requires `AddIskender(...)` in `Iskender.Hosting`, which the control-plane host must not reach (rule A7), and its coordination start-up always starts the data plane's single-instance guard. ISK-0004, ISK-0005, and ISK-0006 each left this spec the work that turns their parts into running processes: the explicit migration operation, the readiness mapping of their health reasons and capability refusals, composing the coordinators into both hosts, disposing the Redis coordinator on shutdown, and OpenTelemetry subscription.

This spec gives operators:

1. **Two independent entry points**: the data-plane host and the control-plane host each start, validate their own configuration, compose their own modules, and stop gracefully, as separate processes with separate images that share no process state.
2. **A plane-neutral composition core**, through a revision of the hosting contract, so the control-plane host composes the same configuration validation, module registrations, and coordination start-up as the data-plane host, without reaching `Iskender.Hosting`, and each plane's single-instance guard holds its own plane's lease.
3. **Container configuration roots**: a fixed, documented mapping from each container's files and environment onto ISK-0003's base, overlay, and environment layers, and an explicit environment name that never comes from ASP.NET Core's.
4. **Liveness and readiness**: separate endpoints on a separate health listener, with readiness mapping every persistence health reason, every coordination health reason, the single-instance state, and the capability report onto value-free reason codes. Liveness never calls a probe.
5. **Start-up refusal with distinct exit codes**: a rejected configuration, a missing registration, or a refused mandatory feature stops the process with a documented exit code and a value-free diagnostic.
6. **An explicit migration operation**: a one-shot command of the control-plane executable that applies or reverses the shipped migration modules, `coordination` included, and that nothing runs on start-up.
7. **Non-root OCI images**: one image per plane, built from digest-pinned bases, running as a fixed non-root user with a read-only root filesystem.
8. **Acceptance evidence**: a Compose topology of both images, in which the split containers launch, report live, and fail readiness with the reason that names the missing dependency, and a cross-host lifecycle test that compares the container data plane with ISK-0006's embedded host.

This spec adds no gateway ingress, provider, authentication scheme, management API, or admin UI, and provisions no cloud resource. Until ISK-0021 provides a credential provider, a container that composes PostgreSQL cannot connect, so it reports not ready with `authentication_failed`, and the migration command cannot connect either. This spec reports that state accurately rather than working around it.

Project, package, type, command, and path names appear in this spec because they are its subject, not a choice of implementation.

## Decision Classification

### Accepted decisions (already approved; this spec applies them)

| Decision | Source |
|---|---|
| Explicit modules with preserved dependency direction. Control plane and data plane are independently hosted and independently deployable | ADR-0003, Constitution II |
| Iskender ships as standard OCI containers and as the embedded `AddIskender(...)` library, with the same pipeline contracts | ADR-0004, Constitution I and II, `docs/04` Choices |
| .NET 10 and ASP.NET Core are the runtime | ADR-0002 |
| Organization, project, and resource ownership scopes. Configuration under ISK-0003 is operator configuration and defines no tenant-owned resource | ADR-0005, ISK-0003 contract Root and namespaces rule 6 |
| The production split has independent data-plane replicas and a control-plane API, as separate scaling, security, health and readiness, and rollout units, with database and Redis reached through constrained identities | `docs/04` Deployable topologies 2 |
| Liveness (the process works) is separate from readiness (mandatory configuration, durable store, coordination backend ready), and both expose safe diagnostic reason codes | `docs/04` Deployment invariants |
| Containers run non-root with minimum Linux capabilities, a read-only root filesystem where practicable, explicit write paths, and read-only plugin mounts. Dev-only defaults cannot appear in production manifests | `docs/04` Deployment invariants |
| The data-plane host must not reach `Iskender.Management` or the control-plane host (A6). The control-plane host must not reach `Iskender.Hosting` or the data-plane host (A7). No project, test projects included, reaches either host (A2). Executables take the `cloud`, `provider`, and `driver` categories only through adapters | Module dependency rules contract version 4 |
| Configuration is validated from at most one base document, one overlay for the active environment, and `ISKENDER__` variables, with ISK-0003's precedence. Only `development` permits development-only values. Refusing to start or reporting not ready on a rejected configuration belongs to the host | ISK-0003 configuration contract, Inputs, Overlays and environment variables, Embedding rule 3 |
| Iskender reads only the documents its options name, never the host's `IConfiguration`, and never derives its environment name from the host's | ISK-0006 TR-2, TR-3, hosting contract Registration rules 2 and 3 |
| Migrations run only when `IMigrationRunner` is called, under the migration login, which the runtime login and a data-plane process need not hold. The `coordination` migration module joins the migration set of any host that uses the PostgreSQL coordinator | Persistence contract version 2, Migrations rules 9, 11, 12, and Configuration |
| A replica stays ready while the persistence reason is `migration_in_progress`, because migrations in the compatibility window are expand-only (**decided by the maintainer in ISK-0004 `checklist` on 2026-11-10**) | Persistence contract, Health rule 4 |
| Liveness never calls the persistence or the coordination health probe | Persistence contract Health rule 1, coordination contract Health rule 1 |
| The capability check runs once per container and every call through a refused feature fails closed. Mapping refusals onto start-up and readiness is ISK-0007's | Coordination contract, Requirements rules 3, 5, 7; hosting contract Start-up rule 7 |
| Dispose the Redis coordinator only on shutdown, after start-up has finished | Coordination contract, Known limits |
| No database or coordination credential provider ships outside test projects until ISK-0021, and no interim development-only provider is added. Until then a production host cannot connect: PostgreSQL reports `authentication_failed`, as does a Redis that requires a password | ISK-0004 TR-14, persistence and coordination contracts, Credentials |
| Mandatory security checks fail closed. Payload logging is off by default. Metric cardinality is bounded and sensitive-safe. No performance figure is asserted without a reviewer-approved baseline | Constitution V, VIII |
| Every module registration is explicit, with no assembly scanning, and Iskender keeps no static or process-wide state | ISK-0006 hosting contract, Registration rule 6, Static state |

### Maintainer decisions taken in `specify`

| Decision | Recorded in |
|---|---|
| **Composition seam** (Q1): the plane-neutral composition core moves out of `Iskender.Hosting` into `Iskender.Domain`. `AddIskender(...)` stays the public data-plane entry, and a new control-plane composition root in `Iskender.Management` composes the same core. Registrations require a composition root instead of the `hosting` module, and the root names its plane, so each plane's guard holds its own lease | TR-2 |
| **Refusal mapping** (Q2): a refused feature with `MandatoryPath` fails start for any of the five reasons, `single_instance_violated` included, with a distinct exit code. A refused feature without `MandatoryPath` leaves the process ready and is listed in readiness. Losing the instance lease later makes the process not ready until it is retaken | TR-7 |
| **Migration operation** (Q3): a one-shot command of the control-plane executable, run from the control-plane image, composing only persistence and its migration modules, with no HTTP listener, exiting with a code. The data-plane image has no migration path. The logic lives in a library that tests reach | TR-9 |

### Technical recommendations (proposed by this spec; reviewed in `clarify`)

| ID | Recommendation | Why | Alternative rejected |
|---|---|---|---|
| TR-1 | **Split-process strategy**: two executables, as today, and two OCI images, one per plane, built from the same commit. The data-plane image holds no `Iskender.Management` assembly, and the control-plane image holds no `Iskender.Hosting` assembly. The planes share no process, file, or in-memory state, and meet only through PostgreSQL and Redis under their contracts. Each image is versioned, deployed, scaled, and rolled out independently | Separate scaling, security, and rollout units (`docs/04`, ADR-0003). One image per plane keeps each plane's code out of the other's attack surface, as rules A6 and A7 already do for the build | One image with two entry points, which ships each plane's code in the other's image |
| TR-2 | **Composition core and roots** (decided in `specify`, Q1). Through a revision of the hosting contract to version 2: the runtime options, start-up validation, the requirement check, and the composition start-ups move into `Iskender.Domain`, in a namespace the module dependency rules permit, and the seam that module registrations use is reachable from `Iskender.Domain`. A **composition root** is the one registration per container that owns these: `AddIskender(...)` for the data plane, in `Iskender.Hosting`, and `AddIskenderControlPlane(...)`, a new method in `Iskender.Management`, for the control plane. Every module registration requires a composition root instead of the `hosting` module. Two roots in one collection fail at the second call with `hosting.registration_duplicate`, and a module registration without a root fails at host start with `hosting.registration_missing`, as today. The root names its plane, and the coordination start-up starts that plane's single-instance guard, so the control plane holds `instance.control_plane`. `AddIskender(...)`, `MapIskender(...)`, and `IskenderOptions` keep their public signatures and behavior, apart from the refused-mandatory-feature start failure of TR-7 | The approved prompt asks for the control plane to compose through a revision of the hosting contract, not around it. `Iskender.Hosting` can already reach `Iskender.Domain`, and so can `Iskender.Management`, so neither plane gains an edge | The core in `Iskender.Infrastructure`, which `Iskender.Hosting` cannot reach (A8); a new shared library project, which adds a project and a package |
| TR-3 | **Configuration roots** (settles ISK-0003 AV-4 for the container topologies). Each entry point reads, from its configuration directory: the base document `iskender.yaml` or `iskender.json`, and, when an environment name is set, the overlay `iskender.<environment>.yaml` or `iskender.<environment>.json`. The directory is `/etc/iskender` unless `ISKENDER_CONFIG_DIRECTORY` names another. Both entry points read the process's `ISKENDER__` variables. The environment name comes only from `ISKENDER_ENVIRONMENT`, and is none, which is production-grade, when unset. `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, `appsettings*.json`, command-line arguments, and ASP.NET Core's URL settings never reach Iskender's configuration or environment name. A missing base or overlay file is a missing layer. The default directory may be absent, which leaves only defaults and environment variables, but a directory named by `ISKENDER_CONFIG_DIRECTORY` that does not exist, or a layer with both a `.yaml` and a `.json` file, fails start with `hosting.configuration_rejected`, naming the layer and no content. The composition event names every document used | Operators mount a file or set variables. A fixed name and an explicit environment variable make the source of every value visible, and keep a container's ASP.NET Core environment from relaxing Iskender's defaults, as TR-3 of ISK-0006 decided for embedded hosts | Reading the host's `IConfiguration`, which ISK-0006 rejected; a configurable file name, which lets two configurations hide behind one mount |
| TR-4 | **Precedence** (settles ISK-0003 AV-6 for the container topologies): ISK-0003's defaults, base, overlay, environment, with no further layer. Each plane validates only the sections it registers, so a document holding the other plane's section fails with `config.namespace.unknown`, and each plane gets its own documents | One precedence for every topology keeps embedded and container hosts producing the same revision from the same inputs | A command-line layer, which would need a configuration contract revision |
| TR-5 | **What each entry point composes**. The data plane: `AddIskender(...)`, `AddIskenderCoordination()`, `AddIskenderPostgreSqlPersistence()`, `AddIskenderPostgreSqlCoordination()`, and `AddIskenderRedisCoordination()`, with `MapIskender("/")` on the traffic listener, built from those methods and composing no adapter again (settles ISK-0006 AV-1). The control plane: `AddIskenderControlPlane(...)` and the same four module registrations. PostgreSQL persistence is always composed, because PostgreSQL is the source of truth (Constitution II), so the `persistence` section's required fields are required in every container. The Redis coordinator is registered in both, and connects only when a primitive selects it. The control plane maps no management endpoint in this spec | Every MVP gateway deployment needs PostgreSQL. Registering every coordinator lets the configuration choose one per primitive, as ISK-0005 designed, without a rebuild | Composing persistence only when configured, which a container cannot know before it reads its configuration |
| TR-6 | **Listeners and the `container` section**: each entry point registers a configuration namespace `container`, version 1, with `trafficPort` (integer, default `8080`), `healthPort` (integer, default `8081`, different from `trafficPort`), `shutdownDelay` (duration, default `0s`, `0s` to `1m`), `shutdownTimeout` (duration, default `30s`, `1s` to `5m`), and `readinessCacheDuration` (duration, default `2s`, `0s` to `30s`). Ports are from 1024 to 65535, so a non-root process can bind them. The listeners are plain HTTP, and only these fields set them. The control plane opens only its health listener until a management API spec adds endpoints. Readiness is advisory: the traffic listener serves requests the same way whether or not the process is ready, each dependency fails closed through its own error code, and only the orchestrator acts on readiness (**decided in `clarify`**, Q5) | Listener and lifecycle values change behavior, so they belong in validated configuration. A separate health port keeps probe endpoints and their reason codes off the public traffic listener | ASP.NET Core's `ASPNETCORE_URLS`, which bypasses validation; health endpoints on the traffic port, which exposes them to callers |
| TR-7 | **Refusal mapping** (decided in `specify`, Q2; settles ISK-0005 AV-4 and, with TR-8, ISK-0006 AV-2). After the capability check runs, a refused feature with `MandatoryPath` fails start for any of the five reasons (`degrade_not_permitted`, `single_instance_violated`, `coordinator_not_configured`, `local_in_multi_replica`, `unsupported_consistency`), with `coordination.capability_unsupported`, exit code 4, and an event naming the feature, module, and reason. The report never changes while the process runs, so a lasting refusal is a restart, which the orchestrator applies with its own backoff. A refused feature without `MandatoryPath` leaves the process ready, every call through it fails closed as ISK-0005 requires, and readiness lists it with its reason. A `single_instance` process that later loses its instance lease is not ready, with reason `single_instance_lost`, until the guard takes the lease again. The mapping lives in the shared composition core, so an embedded host built with `AddIskender(...)` also fails start on a refused mandatory feature, with the same code, and hosting contract version 2 replaces Start-up rule 7 with this rule (**decided in `clarify`**, Q4). An embedded host has no exit code, so its start throws, as for the other start-up failures | A mandatory path that cannot run should not take traffic, and waiting cannot fix a refusal the report will never re-evaluate. Embedded hosts are first-class (ADR-0004), so they get the same rule | Refusals as not-ready only, which leaves a live process that can never become ready |
| TR-8 | **Readiness and liveness** (settles ISK-0004 AV-2 for readiness, and ISK-0006 AV-2). `GET /health/live` and `GET /health/ready`, on the health listener only, outside every Iskender route group, with no authentication, answering `application/json`. Liveness answers `200` while the process runs, and calls no probe. Readiness answers `200` when every check is ready and `503` otherwise, with a versioned, value-free report listing each check, its status, and one reason code. The checks, always all five, in this order: **lifecycle** (`starting` until the composition start-ups finish, `stopping` from the first stop signal), **persistence** (`ok` and `migration_in_progress` ready; `unreachable`, `authentication_failed`, `timeout`, `schema_behind`, `insufficient_privilege`, `unexpected`, `checksum_mismatch`, and `schema_ahead` not ready, each with that reason), **coordination** (`ok` and a healthy `not_configured` ready; an unhealthy `not_configured`, `unreachable`, `authentication_failed`, `timeout`, `read_only`, and `unexpected` not ready), **single instance** (`single_instance_lost` and `single_instance_held_by_other` not ready; held, not applicable, and unverified ready, reported as such; **`held_by_other` decided in `plan`**), and **capability** (always ready, listing refused non-mandatory features). Each readiness request runs each probe at most once at a time, and reuses a result no older than `readinessCacheDuration`, so a burst of probe requests makes one probe call. A change of any check's reason writes one event | `docs/04` and the persistence and coordination contracts assign this mapping here. Reason codes come from the contracts, which already hold no values, so the report needs none | One readiness endpoint per dependency, which orchestrators cannot combine; probes from liveness, which the contracts forbid |
| TR-9 | **Migration command** (decided in `specify`, Q3; settles ISK-0004 AV-2 for the operation). The control-plane executable takes `migrate apply`, `migrate reverse [--steps <n>] [--confirm-data-loss]`, and `migrate status`. It makes the same registration calls as the serving control plane, so the same documents validate, and builds and runs only persistence and the shipped migration modules, `coordination` included (**decided in `plan`**). It runs the requirement check and validation and no composition start-up, so it opens no listener, starts no guard, runs no capability check, connects to no coordinator, runs the one operation through `IMigrationRunner` under the migration login, writes ISK-0004's events, and exits. Exit code 0 is success, and 5 is any `persistence.*` failure, with the code and safe reason in its diagnostic. `migrate status` exits 0 when the schema is current, 6 when it is behind (migrations pending), 7 when it is ahead or has a checksum mismatch, and 5 when the check cannot run, naming the state and no connection detail (**decided in `clarify`**, Q6). No process runs it on start-up, and the long-running control plane needs no migration login. The data-plane executable has no migration command. The command's logic lives in a library that tests call with a test credential provider | An explicit, auditable, one-shot job that an operator or a release pipeline runs before rolling out, using the image that already ships | A command on both executables, which puts a migration path in the data-plane image; an HTTP endpoint, which needs authentication that does not exist yet |
| TR-10 | **Command line and exit codes**: with no arguments, each executable serves. Both take `health live` and `health ready`, which call their own health listener over loopback and exit 0 when it answers `200` and 1 otherwise, so an image without a shell or `curl` can run a container health check. Any other argument exits 64 with a usage message, before any configuration is read. Exit codes: 0 clean stop or successful command, 1 unexpected failure, 2 `hosting.configuration_rejected`, 3 `hosting.registration_missing`, 4 refused mandatory feature (TR-7), 5 migration failure (TR-9), 6 `migrate status` schema behind, 7 `migrate status` schema ahead or checksum mismatch, 64 usage error | Orchestrators and Compose read exit codes. One code per class tells an operator which runbook entry to read without parsing logs | A single non-zero code |
| TR-11 | **Graceful lifecycle**: on `SIGTERM` or `SIGINT`, readiness turns `stopping` at once, requests keep being served for `shutdownDelay` so load balancers can drain, then the server stops accepting connections and waits up to `shutdownTimeout` for requests in flight. The composition start-ups then stop in reverse order: the guard stops its timer and releases its lease. Last, the container is disposed, which disposes the Redis coordinator and the data sources. A stop that arrives during start-up waits for the composition start-up to finish or be cancelled before disposal, so the Redis coordinator is never disposed during its first connection. The process exits 0 after a clean stop. The orchestrator's grace period before it kills the process must exceed `shutdownDelay` + `shutdownTimeout` + 5 seconds. The Compose fixture sets `stop_grace_period` to match, and the operator guide states the rule (**decided in `clarify`**, Q7) | ISK-0005 forbids disposing the Redis coordinator during its first connection, and ISK-0006 requires the lease released at stop | Disposing on the first signal, which drops requests in flight and can strand a Redis connection |
| TR-12 | **Images**: a multi-stage Dockerfile per plane, whose build and runtime base images are pinned by digest. The runtime stage runs as a fixed numeric non-root user and group, sets no setuid or setgid file, needs no Linux capability, works with a read-only root filesystem, and writes only to `/tmp`. It holds the published application only, with no SDK, source, or test assembly, and carries the OCI labels for source, revision, and `MIT` license. The images are built and tested in the existing CI checks and are not pushed to a registry by this spec | `docs/04` invariants. Digest pins make the build reproducible, and a numeric user lets an orchestrator verify non-root | Running as root, or a named user only, which `runAsNonRoot` cannot check |
| TR-13 | **Telemetry**: each entry point subscribes the OpenTelemetry SDK to the `Iskender.Coordination` and `Iskender.Persistence` activity sources and meters, with `service.name` `iskender-data-plane` or `iskender-control-plane`. An OTLP exporter is enabled only when the standard `OTEL_EXPORTER_OTLP_ENDPOINT` variable is set, and otherwise nothing is exported. Logs go to standard output as structured JSON. Payloads, configuration values, secret reference names, and connection details are never written | ISK-0004 and ISK-0005 left the subscription here. The OpenTelemetry variables configure the entry point's own telemetry pipeline, which the host owns, not Iskender's configuration | An exporter on by default, which would send telemetry to an endpoint no one chose |
| TR-14 | **Diagnostics**: fixed events, category `Iskender.Container`, with stable IDs from 7001, for: entry point starting (plane, release version), start refused (error code and exit code for every start failure with exit code 2, 3, or 4, and the feature, module, and reason for a refused mandatory feature), readiness changed (check, from reason, to reason), stop requested, stopped (exit code), and migration command finished (operation, outcome, applied or reversed count). No event holds a configuration value, secret reference name, connection detail, file content, or request route value. ISK-0003's, ISK-0004's, ISK-0005's, and ISK-0006's events are written as their contracts define | An operator must tell from container logs why a container is not ready or did not start | Messages without IDs, which alerting cannot match |
| TR-15 | **Container contract**: a new versioned contract, `container`, version 1, in this spec's `contracts/` directory, owning the entry points, the command line and exit codes, the configuration roots, the `container` section, the listeners, the readiness report format and its mapping tables, the lifecycle, the image rules, the events, and the fixtures | Operators, Compose files, and orchestrator manifests depend on these as an interface | Scattering them across the hosting and persistence contracts |
| TR-16 | **Public surface**: `Iskender.Hosting`'s public surface, and so the `hosting` compatibility contract and its baseline, do not change. The one behavior change, an embedded start failing on a refused mandatory feature (TR-7), adds no type, member, or error code. `Iskender.Core.Contracts` gains no error code, and `core-contracts`, `providers-abstractions`, and `policy-sdk` do not change. `AddIskenderControlPlane(...)` is in the embeddable `Iskender.Management`, outside the harness, and prerelease under ADR-0030, like the other embeddable registrations | The start-up failures reuse existing codes, and moving the core changes no public signature | A new `hosting.capability_refused` code, which duplicates `coordination.capability_unsupported` |
| TR-17 | **Acceptance fixtures**: a Compose topology, as a test fixture under `tests/` and separate from ISK-0008's developer Compose, that builds both images and runs them with PostgreSQL 17 and Valkey 8 pinned by the digests the ISK-0004 and ISK-0005 fixtures use. The tests start it and check each container's exit code, liveness, readiness status, and readiness reasons for each scenario in User Story 1. A cross-host lifecycle test sends the same requests to the data-plane container and to the hosting sample through `EmbeddedHost`, and compares them (settles ISK-0006 AV-1). Both need a container runtime and follow ISK-0005's rule: skipped with their reason without one, and failing when `CI` is `true` | The acceptance evidence must come from the real images and the real orchestration format. No test may reference a host (A2), so the tests run the images | Tests that start the host projects in process, which rule A2 forbids |

### Assumptions needing validation

| ID | Assumption | Validated by |
|---|---|---|
| AV-1 | The composition core can move into `Iskender.Domain` without a package or framework reference that rule A19 denies to `Iskender.Domain` (no `aspnetcore` category), and `Iskender.Management` can host `AddIskenderControlPlane(...)` within its approved references and denied categories | This spec's plan |
| AV-2 | The OpenTelemetry packages the entry points need, and every package they bring into an executable's lock file, declare an SPDX expression already on the `check-license.sh` list | This spec's plan, Product/Charter owner |
| AV-3 | The CI runner can build the two images and run Docker Compose inside the existing `dotnet test` check, so no new CI check is needed, and CI stays at 10 checks | This spec's plan |
| AV-4 | The `aspnet` runtime base image the plan selects runs with a numeric non-root user, a read-only root filesystem, and no Linux capability, and its license allows redistribution in an MIT project's image | This spec's plan |
| AV-5 | Plain HTTP listeners are enough for MVP topologies, because TLS is terminated in front of the containers, as by the Azure Container Apps ingress in the reference topology. TLS on the entry points' own listeners, and protection of the health listener beyond a separate port, are left to a later spec | ISK-0010, rechecked by ISK-0085's threat review (RC-20261116-003) |
| AV-6 | ISK-0008 can build its developer Compose topology, and the all-in-one developer container of `docs/04` topology 1, from these two images, their configuration roots, and the migration command | ISK-0008 |
| AV-7 | ISK-0009's maintenance work can run as the control plane, or as another command of the control-plane executable, without a new executable role | ISK-0009 |
| AV-8 | ISK-0021's credential providers can be registered by both entry points with no change to the configuration roots, after which the same containers become ready with no other change | ISK-0021 |

**ISK-0003's AV-4 and AV-6 are settled for the container topologies by this spec** (TR-3, TR-4).

**ISK-0004's AV-2 is settled by this spec** (TR-8, TR-9).

**ISK-0005's AV-4 is settled by this spec** (TR-7, TR-8).

**ISK-0006's AV-1 and AV-2 are settled by this spec** (TR-5, TR-7, TR-8, TR-17).

### Post-MVP work (named so that it is not designed here)

Multi-architecture images. TLS on the entry points' own listeners. A management API and the admin UI. Hot reload of configuration, which belongs to ISK-0057. Redis Cluster and Sentinel. Production cloud provisioning, Kubernetes manifests, Helm charts, and Azure Container Apps templates. A separate maintenance worker executable. Downloaded plugin staging and read-only plugin mounts, which belong to the plugin specs.

## Clarifications

### Session 2026-11-15 (specify)

- Q: The control-plane host must not reach `Iskender.Hosting`, but every module registration requires `AddIskender(...)` and the coordination start-up always starts the data plane's guard. How should the hosting contract revision let the control plane compose? → A: Move the plane-neutral core into `Iskender.Domain`. `AddIskender(...)` stays the public data-plane entry, and a new `AddIskenderControlPlane(...)` in `Iskender.Management` composes the same core. Registrations require a composition root instead of `hosting`, and the root names its plane (TR-2).
- Q: How should the capability check's five refusal reasons map onto start-up and readiness? → A: A refused feature with `MandatoryPath` fails start for any reason, `single_instance_violated` included, with a distinct exit code. A refused feature without it leaves the process ready and is listed in readiness. Losing the instance lease later makes the process not ready until it is retaken (TR-7).
- Q: Where should the explicit migration operation be exposed? → A: As a one-shot command of the control-plane executable, run from the control-plane image, composing only persistence and its migration modules, with no listener, exiting with a code. The data-plane image has no migration path, and the logic is in a library that tests reach (TR-9).

### Session 2026-11-15 (clarify)

- Q: Should an embedded host built with `AddIskender(...)` also refuse to start when a mandatory feature is refused, like the containers do? → A: Yes. The refusal mapping lives in the shared composition core, so embedded and container hosts both fail start with `coordination.capability_unsupported`, and hosting contract version 2 replaces Start-up rule 7 (TR-7, FR-014).
- Q: While the data plane is not ready, should its traffic listener still serve requests or turn them away? → A: Serve them. Readiness is advisory to the orchestrator; the traffic listener does not gate on it, and each dependency fails closed through its own error code (TR-6, FR-011a).
- Q: What exit code should `migrate status` return when the database is reachable but behind, ahead, or mismatched? → A: 0 current, 6 behind (pending), 7 ahead or checksum mismatch, 5 when the check cannot run (TR-9, TR-10, FR-023).
- Q: The default stop sequence can outlast Docker's 10-second grace before `SIGKILL`. How should that be handled? → A: Keep the defaults. The contract requires the orchestrator's grace to exceed `shutdownDelay` + `shutdownTimeout` + 5s, the Compose fixture sets `stop_grace_period` to match, and the operator guide says so (TR-11, FR-021a).

### Session 2026-11-15 (plan)

- Q: TR-2 moves the composition core, but some of ISK-0006's hosting tests name the internal types that move, and one reads a private field through reflection. What does "passes unchanged" mean? → A: Unchanged behavior. Only lines that name a moved internal type may change: type and namespace names, constructor calls, and reflection on private fields. No behavior assertion is changed or removed, and the `hosting` baseline does not change (US6.4, SC-009).
- Q: Where does the ASP.NET Core code both entry points share live? → A: In a new embeddable library, `Iskender.Infrastructure.Container`, under the existing `Iskender.Infrastructure.<Adapter>` pattern, so no dependency rule changes.
- Q: How does readiness treat a single-instance lease held by another process? → A: Not ready, with reason `single_instance_held_by_other`, until the guard takes the lease (TR-8, FR-018, US2.10).
- Q: How does the migration command compose, given that a plane validates only the sections it registers? → A: With the same registration calls as the serving control plane, so the same documents validate, running the requirement check and validation and no composition start-up (TR-9).

### Session 2026-11-15 (checklist)

- Q: Does a present but empty `ISKENDER_ENVIRONMENT` or `ISKENDER_CONFIG_DIRECTORY` count as set or unset? → A: Unset. An empty environment name gives none, which is production-grade, and an empty directory selects `/etc/iskender` (Edge Cases, container contract Configuration roots rule 4).

## User Scenarios & Testing *(mandatory)*

The users of this feature are operators who deploy and run the two planes, release engineers who run migrations, authors of later specs who add modules to either plane, and reviewers who need evidence that the planes are separate and fail safely.

### User Story 1 - Split containers launch and report which dependency is missing (Priority: P1)

An operator starts the data-plane and control-plane containers with Compose. Both start, report live, and report not ready, and each readiness answer names the dependency that is missing and why: no database server, a database the process cannot log into, a Redis that is down, or a coordinator selected with no settings.

**Why this priority**: It is this spec's acceptance evidence, and the first thing an operator sees.

**Independent Test**: Start the Compose fixture in each dependency state below, and read each container's exit code, `/health/live`, `/health/ready`, and logs.

**Acceptance Scenarios**:

1. **Given** both images with valid configurations and no PostgreSQL server, **When** they start, **Then** each container stays running, `/health/live` answers `200`, and `/health/ready` answers `503` with persistence reason `unreachable`.
2. **Given** PostgreSQL 17 running and reachable, **When** the containers start, **Then** readiness answers `503` with persistence reason `authentication_failed`, because no credential provider exists before ISK-0021.
3. **Given** a configuration that selects `redis` for a primitive, with Redis settings but no Redis server, **When** the containers start, **Then** readiness reports coordination reason `unreachable`, in addition to the persistence reason.
4. **Given** a configuration that selects `redis` for a primitive with no `redis` settings, **When** the containers start, **Then** readiness reports coordination reason `not_configured`, with status not ready.
5. **Given** a rejected configuration, such as a misspelled field, **When** a container starts, **Then** it exits with code 2, and its log holds ISK-0003's value-free report and events 3002, 3003, and 6002, and no configuration value.
6. **Given** each scenario above, **When** readiness is read, **Then** the reported reason names the actual missing dependency in 100% of scenarios, and no response or log line holds a host name, port, user name, password, secret reference name, or configuration value.
7. **Given** both containers running, **When** their processes and images are inspected, **Then** each runs as the fixed non-root user, and the data-plane image holds no `Iskender.Management` assembly and the control-plane image no `Iskender.Hosting` assembly.

---

### User Story 2 - Each plane starts and stops on its own, safely (Priority: P1)

An operator deploys, scales, restarts, and stops each plane independently. A plane starts only on a valid configuration and composition, holds its own plane's instance lease when declared `single_instance`, drains on stop, and releases everything it holds.

**Why this priority**: Independent deployability is constitutional (Constitution II, ADR-0003), and a plane that starts on a bad configuration or leaks a lease breaks the other guarantees.

**Independent Test**: Through the composition core in a test host and through the images, start, stop, and restart each plane alone, with each start-up failure class, and with both planes declared `single_instance` against one PostgreSQL.

**Acceptance Scenarios**:

1. **Given** only the control-plane container, **When** it starts, **Then** it composes its modules without the data plane running, and the same holds for the data plane alone.
2. **Given** both planes declared `single_instance` with the PostgreSQL lease coordinator and a test credential provider, **When** both start, **Then** the data plane holds `instance.data_plane` and the control plane holds `instance.control_plane`, and neither refuses the other.
3. **Given** a second control-plane process with the same `single_instance` configuration and a feature with `MandatoryPath`, **When** it starts, **Then** it fails start with exit code 4 and reason `single_instance_violated`, and the first keeps its lease.
4. **Given** a running process that receives `SIGTERM`, **When** it stops, **Then** readiness answers `503` with lifecycle reason `stopping` at once, requests in flight complete within `shutdownTimeout`, the instance lease is released, the Redis coordinator is disposed, and the process exits 0.
5. **Given** a configuration that selects `postgresql` for a primitive, **When** the control plane starts, **Then** the PostgreSQL coordinator is composed through the module registrations, and no coordinator is composed outside them.
6. **Given** a stop signal during the composition start-up, **When** the process stops, **Then** it waits for the start-up to finish or be cancelled before disposing the Redis coordinator, and leaves nothing running.
7. **Given** an embedded host built with `AddIskender(...)` and a feature with `MandatoryPath` that the capability check refuses, **When** the host starts, **Then** its start fails with `coordination.capability_unsupported` and the event naming the feature, module, and reason.
8. **Given** a refused feature without `MandatoryPath`, **When** the process starts, **Then** it starts, is ready, and lists the feature and its reason in readiness, and every call through the feature fails with `coordination.capability_unsupported`.
9. **Given** a running `single_instance` process whose lease is lost, **When** readiness is read, **Then** it answers `503` with `single_instance_lost` until the guard takes the lease again, and then `200`.
10. **Given** a `single_instance` process with no mandatory feature that starts while another process holds its plane's lease, **When** readiness is read, **Then** it answers `503` with `single_instance_held_by_other` until the guard takes the lease, and then `200`.

---

### User Story 3 - Readiness maps every health reason, and liveness never probes (Priority: P1)

An orchestrator polls liveness and readiness. Liveness tells it only whether to restart the process. Readiness tells it whether to send traffic, mapping every persistence and coordination reason as the contracts require, and keeping replicas ready during a migration.

**Why this priority**: A wrong mapping either sends traffic to a broken replica or takes every replica out during a routine migration.

**Independent Test**: Through the composition core in a test host, with stub probes returning each reason, read readiness for each persistence reason and each coordination reason, and count probe calls from liveness.

**Acceptance Scenarios**:

1. **Given** each of the persistence probe's 10 reasons, **When** readiness is read, **Then** `ok` and `migration_in_progress` answer ready, and the other 8 answer not ready with that reason.
2. **Given** each of the coordination probe's 7 reasons, with `not_configured` in both its healthy and unhealthy forms, **When** readiness is read, **Then** `ok` and the healthy `not_configured` answer ready, and the rest answer not ready with that reason.
3. **Given** any state, **When** liveness is read 1,000 times, **Then** the persistence and coordination probes are called 0 times.
4. **Given** 100 concurrent readiness requests, **When** they complete, **Then** each probe was called at most once for them, and each answered within the probe's timeout plus its 2-second allowance.
5. **Given** a request to `/health/ready` on the traffic listener, **When** it is handled, **Then** it does not reach the health endpoints.
6. **Given** a readiness reason that changes, **When** the next readiness result is computed, **Then** one readiness-changed event is written, naming the check and both reasons.

---

### User Story 4 - A release engineer runs migrations as an explicit, one-shot operation (Priority: P2)

A release engineer runs the control-plane image once with `migrate apply` before rolling out a release, and with `migrate reverse` to roll one back. No running process ever migrates, and the data plane never holds the migration login.

**Why this priority**: Schema changes must be explicit, auditable, and separate from start-up (persistence contract), but no product tables exist yet, so only the `coordination` module is migrated today.

**Independent Test**: Through the migration command's library with a test credential provider against the ISK-0004 PostgreSQL fixture, and through the control-plane image in Compose, run each operation and check the database, exit codes, and events.

**Acceptance Scenarios**:

1. **Given** an empty database provisioned as the runbook describes, **When** `migrate apply` runs with the migration login, **Then** the `coordination` module is applied, ISK-0004's events 4007, 4009, and 4011 are written, and the command exits 0.
2. **Given** an applied database, **When** `migrate reverse` runs without `--confirm-data-loss` on a data-losing step, **Then** nothing changes, and it exits 5 with `persistence.reverse_requires_confirmation`.
3. **Given** a running data-plane or control-plane process, **When** it starts or runs, **Then** it never calls `IMigrationRunner` to apply or reverse, and its configuration needs no migration login.
4. **Given** the data-plane executable, **When** it is run with `migrate apply`, **Then** it exits 64, and no migration code runs.
5. **Given** the control-plane image in Compose before ISK-0021, **When** `migrate apply` runs, **Then** it exits 5 with `persistence.unavailable`, because no credential provider exists, and its diagnostic names the code and no connection detail.
6. **Given** a database behind the shipped migrations, then one applied, then one ahead of them or with a changed checksum, **When** `migrate status` runs on each, **Then** it exits 6, 0, and 7, and a status that cannot connect exits 5.
7. **Given** a migration in progress, **When** a data-plane replica's readiness is read, **Then** it stays ready, with persistence reason `migration_in_progress` reported.

---

### User Story 5 - The container data plane behaves like the embedded host (Priority: P2)

A reviewer checks that the container data plane and an embedded host built with `AddIskender(...)` run the same request lifecycle.

**Why this priority**: `docs/03` requires the same request lifecycle in embedded and container hosts, and ISK-0006 deferred the container half here.

**Independent Test**: Send the same requests to the data-plane container and to the hosting sample through `EmbeddedHost`, and compare.

**Acceptance Scenarios**:

1. **Given** the same configuration, with the data-plane container not ready because no credential provider exists, **When** both hosts handle the same set of requests under Iskender's routes, **Then** status, the ISK-0002 error envelope, error code, and correlation behavior match for 100% of requests, apart from the generated IDs.
2. **Given** a valid caller correlation header, **When** both hosts handle it, **Then** both keep it under ISK-0002's rules, and both generate a `req_` gateway ID.
3. **Given** the same configuration documents, **When** both hosts start, **Then** both log event 6001 naming the same configuration revision.

---

### User Story 6 - The control plane composes without reaching the data plane's hosting (Priority: P3)

An author of a later control-plane spec adds a module to the control plane through `AddIskenderControlPlane(...)` and the module registrations, as data-plane authors do through `AddIskender(...)`.

**Why this priority**: It is the seam later specs build on, but no operator sees it directly.

**Independent Test**: Build test hosts with each composition root and registration combination, and run the architecture tests.

**Acceptance Scenarios**:

1. **Given** the module dependency rules, **When** the architecture tests run, **Then** the control-plane host reaches no `Iskender.Hosting` and the data-plane host no `Iskender.Management`, and no project gains an unapproved edge.
2. **Given** `AddIskender(...)` and `AddIskenderControlPlane(...)` on one service collection, **When** the second runs, **Then** it fails with `hosting.registration_duplicate`.
3. **Given** a module registration without either root, **When** the host starts, **Then** it fails with `hosting.registration_missing` and event 6003.
4. **Given** the hosting suite of ISK-0006, **When** it runs after the core moves, **Then** every test passes with its behavior assertions unchanged, and the `hosting` compatibility baseline is unchanged. Only lines that name a moved internal type change: type and namespace names, constructor calls, and reflection on private fields (**decided in `plan`**).

---

### Edge Cases

- `ISKENDER_ENVIRONMENT` is unset while `ASPNETCORE_ENVIRONMENT` is `Development`: Iskender has no environment name, so a development-only value such as `tlsMode: disable` rejects the configuration and the container exits 2.
- `ISKENDER_ENVIRONMENT` holds an invalid name, such as `Prod_EU`: the configuration is rejected with `config.environment.invalid`, and the container exits 2.
- `ISKENDER_ENVIRONMENT` or `ISKENDER_CONFIG_DIRECTORY` is present but empty: it counts as unset, so the environment name is none, which is production-grade, and the directory is `/etc/iskender`.
- An environment name is set and no overlay file exists for it: the overlay layer is missing, which is valid, and the composition event records no overlay.
- Both `iskender.yaml` and `iskender.json` exist in the directory: start fails with `hosting.configuration_rejected`, naming the base layer as ambiguous.
- `ISKENDER_CONFIG_DIRECTORY` names a directory that does not exist: start fails with `hosting.configuration_rejected`. The default directory being absent is not an error.
- The data plane's document holds a `management` section, or the control plane's a `hosting` section: the plane does not register it, so the configuration is rejected with `config.namespace.unknown`.
- A port is below 1024: the configuration is rejected with an ISK-0003 field error, and the container exits 2. `trafficPort` equals `healthPort`: reading the settings fails with `config.field.out_of_range` before any listener binds, and the container exits 2.
- The process gets a second stop signal during the drain: it keeps to `shutdownTimeout`, and does not skip releasing the lease.
- The orchestrator's grace period is shorter than `shutdownDelay` + `shutdownTimeout` + 5 seconds and it kills the process mid-stop: the lease is not released and expires on its own TTL. The contract and the operator guide name this as a misconfiguration.
- `shutdownTimeout` passes with requests still in flight: they are cancelled, the composition still stops, and the process exits 0.
- An executable is given an unknown argument: it exits 64 before reading any configuration.
- `migrate reverse --steps 0` or a negative count: it exits 64 before connecting.
- The migration login is missing from the configuration: `migrate` exits 5 with `persistence.unavailable` before connecting, as the persistence contract requires.
- Two `migrate apply` jobs start together: one waits for the migration lock up to `migration.lockWait`, then fails with `persistence.migration_locked`, exit 5.
- A request reaches the data plane's traffic listener while it is not ready, before ISK-0021 for example: it is served as usual, and a call through an unavailable dependency fails closed with that dependency's error code.
- The readiness cache duration is `0s`: each request runs the probes, still at most one call at a time per probe.
- No `OTEL_EXPORTER_OTLP_ENDPOINT` is set: nothing is exported, and the process runs as usual.
- The root filesystem is mounted read-only: the process starts and runs, writing only to `/tmp`.

## Requirements *(mandatory)*

### Functional Requirements

**Split-process strategy and entry points**

- **FR-001**: `Iskender.Gateway.Host` and `Iskender.ControlPlane.Host` MUST each become an entry point that composes, validates, serves, and stops gracefully as this spec describes. No new executable project MUST be created.
- **FR-002**: Each plane MUST ship as its own OCI image, built from its own Dockerfile, holding only its own plane's assemblies, so the data-plane image holds no `Iskender.Management` assembly and the control-plane image no `Iskender.Hosting` assembly (TR-1).
- **FR-003**: The two planes MUST share no process, file, or in-memory state, and MUST each start, run, and stop with the other absent.
- **FR-004**: No project MUST gain a reference that the module dependency rules contract does not approve, and rules A2, A6, and A7 MUST hold.

**Composition**

- **FR-005**: Through a revision of the hosting contract to version 2, the plane-neutral composition core MUST move into `Iskender.Domain`, and a composition root for the control plane, `AddIskenderControlPlane(...)` in `Iskender.Management`, MUST compose the same core, as TR-2 decides. `AddIskender(...)`, `MapIskender(...)`, and `IskenderOptions` MUST keep their public signatures and behavior, apart from FR-014's start failure, which applies to embedded hosts too.
- **FR-006**: Every module registration MUST require a composition root instead of the `hosting` module. Two roots on one collection MUST fail with `hosting.registration_duplicate`, and a module registration with no root MUST fail at host start with `hosting.registration_missing`.
- **FR-007**: The coordination start-up MUST start the single-instance guard of the plane its composition root names, so the control plane holds `instance.control_plane` and the data plane `instance.data_plane`, and MUST start it before running `CoordinationCapabilityChecker` once.
- **FR-008**: Each entry point MUST compose the registrations TR-5 lists, through those methods only, composing no adapter outside them, and the data plane MUST map `MapIskender("/")` on its traffic listener.

**Configuration roots**

- **FR-009**: Each entry point MUST map its configuration directory, files, `ISKENDER__` variables, and `ISKENDER_ENVIRONMENT` onto ISK-0003's base, overlay, environment, and environment-name inputs as TR-3 describes, with ISK-0003's precedence and no further layer (TR-4).
- **FR-010**: No entry point MUST let `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, `appsettings*.json`, command-line arguments, or ASP.NET Core's URL settings set an Iskender value, the environment name, or a listener.
- **FR-011**: Each entry point MUST register the `container` section, version 1, with the fields, defaults, and constraints of TR-6, and MUST bind its listeners only from it.
- **FR-011a**: The traffic listener MUST NOT gate requests on readiness: it MUST serve requests the same way while the process is not ready, and each dependency MUST fail closed through its own contract's error code (TR-6).

**Start-up and exit codes**

- **FR-012**: A rejected configuration, an unreadable or ambiguous document, or a missing configured directory MUST stop the process with exit code 2 and `hosting.configuration_rejected` before any listener answers readiness `200`.
- **FR-013**: A missing registration MUST stop the process with exit code 3 and `hosting.registration_missing`.
- **FR-014**: A refused feature with `MandatoryPath` MUST stop the process with exit code 4, `coordination.capability_unsupported`, and an event naming the feature, module, and reason, for each of the five refusal reasons. The same refusal MUST fail the start of an embedded host built with `AddIskender(...)`, with `coordination.capability_unsupported`. A refused feature without `MandatoryPath` MUST NOT stop the process, and MUST be listed in readiness (TR-7).
- **FR-015**: Each executable MUST implement the command line and exit codes of TR-10, and MUST reject any other argument with exit code 64 before reading configuration.

**Health and readiness**

- **FR-016**: Each entry point MUST serve `GET /health/live` and `GET /health/ready` on its health listener only, outside every Iskender route group, as TR-8 describes. Neither MUST be reachable on the traffic listener.
- **FR-017**: Liveness MUST answer `200` while the process runs, and MUST NOT call the persistence probe, the coordination probe, or any network dependency.
- **FR-018**: Readiness MUST map each of the persistence probe's 10 reasons and each of the coordination probe's 7 reasons as TR-8 lists, MUST keep the process ready while the persistence reason is `migration_in_progress`, MUST report the process not ready while its plane's instance lease is lost or held by another process, and MUST report the lifecycle, single-instance, and capability checks as TR-8 lists.
- **FR-019**: Readiness MUST call each probe at most once at a time, reuse a result no older than `readinessCacheDuration`, and answer within the longest composed probe's timeout plus its 2-second allowance.
- **FR-020**: The readiness report MUST be a versioned, value-free document holding each check's name, status, and reason code, and MUST NOT hold a host, port, database name, user name, secret reference name, configuration value, or server error text.

**Lifecycle**

- **FR-021**: On a stop signal, each entry point MUST turn readiness `stopping` at once, keep serving for `shutdownDelay`, stop accepting connections, wait up to `shutdownTimeout` for requests in flight, stop the composition start-ups in reverse order, releasing the instance lease, then dispose the container, and exit 0 (TR-11).
- **FR-021a**: The container contract MUST state that the orchestrator's grace period must exceed `shutdownDelay` + `shutdownTimeout` + 5 seconds, the Compose fixture MUST set `stop_grace_period` to satisfy it, and the operator guide in `docs/04` MUST say so (TR-11).
- **FR-022**: The Redis coordinator MUST be disposed only on shutdown, and never while the composition start-up is still running.

**Migration operation**

- **FR-023**: The control-plane executable MUST provide `migrate apply`, `migrate reverse` with `--steps` and `--confirm-data-loss`, and `migrate status`, as TR-9 decides, with the shipped `coordination` migration module in its migration set, and MUST exit 0 on success and 5 on any persistence failure. `migrate status` MUST exit 0 when the schema is current, 6 when it is behind, 7 when it is ahead or has a checksum mismatch, and 5 when the check cannot run.
- **FR-024**: No process MUST apply or reverse a migration on start-up, the long-running planes MUST NOT require the migration login, and the data-plane executable MUST have no migration command.
- **FR-025**: The migration command's logic MUST live in a library that tests can call with a test credential provider, so the hosts' own code holds only argument parsing and composition calls.

**Images**

- **FR-026**: Each Dockerfile MUST pin every base image by digest, and the runtime image MUST run as a fixed numeric non-root user and group, need no Linux capability, run with a read-only root filesystem, write only to `/tmp`, hold no SDK, source, or test assembly, and carry the OCI source, revision, and license labels (TR-12).
- **FR-027**: Both images MUST be built and tested in the existing CI checks, and CI MUST stay at 10 checks. This spec MUST NOT push an image to a registry.

**Telemetry and diagnostics**

- **FR-028**: Each entry point MUST subscribe OpenTelemetry to the `Iskender.Coordination` and `Iskender.Persistence` activity sources and meters, MUST export only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and MUST write logs to standard output as structured JSON (TR-13).
- **FR-029**: The events of TR-14 MUST be written with fixed IDs, names, levels, and templates, and MUST NOT hold a configuration value, secret reference name, connection detail, file content, or request route value.

**Contracts, fixtures, and documentation**

- **FR-030**: This spec MUST publish the `container` contract, version 1, and the hosting contract, version 2, in its `contracts/` directory, and repoint the links to the hosting contract in `docs/03` and `docs/04`.
- **FR-031**: The module dependency rules contract MUST be revised to version 5 only to record the container half of the same-lifecycle expectation as enforced, with no change to an edge, role, or rule, unless the plan finds a change necessary, which that revision then records.
- **FR-032**: The public surface MUST change only as TR-16 allows: the `hosting` baseline, `core-contracts`, `providers-abstractions`, and `policy-sdk` MUST NOT change.
- **FR-033**: The Compose fixture and the cross-host lifecycle test of TR-17 MUST exist, and the tests of User Stories 1 and 5 MUST run through them. Those tests, with User Story 2's lease and stop tests, are this spec's acceptance evidence.
- **FR-034**: Documentation MUST be updated in the same PR: `docs/04` gains an operator guide to the two images, the configuration roots, the `container` section, the health endpoints and readiness reasons, the exit codes, the migration command, and the stop sequence, and the Database migrations and Coordination runbooks say how the command and readiness work.

### Negative Security and Failure Scenarios

| Scenario | Expected outcome | Covered by |
|---|---|---|
| A container's ASP.NET Core environment relaxes Iskender's production defaults | The environment name comes only from `ISKENDER_ENVIRONMENT`, and unset is production-grade | FR-009, FR-010, Edge Cases |
| A container starts and takes traffic on a partly valid configuration | It exits 2 before readiness is ever ready | FR-012, User Story 1 |
| A mandatory feature runs on a deployment that cannot meet its minimums | Start fails with exit code 4, and an embedded host's start fails with the same code | FR-014, User Story 2 |
| A second process of one plane runs while `single_instance` is declared | A mandatory feature refuses it at start, and a lost lease makes a process not ready | FR-014, FR-018, User Story 2 |
| A routine migration takes every replica out of service | Replicas stay ready during `migration_in_progress` | FR-018, User Story 4 |
| A data-plane process holds migration credentials, or migrates on start | The data plane has no migration path, and no process migrates on start | FR-024, User Story 4 |
| Health endpoints expose internal details or are reachable by callers on the traffic port | Reason codes only, on a separate listener | FR-016, FR-020 |
| Liveness probes overload or depend on the database | Liveness calls no probe | FR-017, User Story 3 |
| A burst of readiness requests floods PostgreSQL or Redis | Each probe runs at most once at a time, with a cached result | FR-019, User Story 3 |
| A container runs as root or with capabilities, or needs a writable root filesystem | Numeric non-root user, no capabilities, read-only root | FR-026, User Story 1 |
| The data-plane image carries control-plane code, or the reverse | Each image holds only its own plane | FR-002, User Story 1 |
| A stop drops requests in flight, keeps the instance lease, or strands a Redis connection | Drain, then reverse stop, then disposal, with Redis disposed only after start-up | FR-021, FR-022, User Story 2 |
| A configuration value, secret reference name, or connection detail reaches a log, readiness answer, or exit diagnostic | Only value-free codes and reasons are written | FR-020, FR-029, SC-006 |
| A base image changes under the same tag | Base images are pinned by digest | FR-026 |
| Telemetry is sent to an endpoint no one chose | No exporter unless the endpoint variable is set | FR-028 |
| The control plane composes around the hosting contract, duplicating its rules | It composes through the same core and module registrations | FR-005, FR-006 |

### Key Entities

- **Entry point**: One plane's executable, its command line, and its exit codes.
- **Composition core and composition root**: The plane-neutral options, start-up validation, requirement check, and composition start-ups, and the one registration per container that owns them and names its plane.
- **Configuration roots**: The configuration directory, its base and overlay files, the `ISKENDER__` variables, and `ISKENDER_ENVIRONMENT`.
- **`container` section**: The listener ports and lifecycle durations of one entry point.
- **Readiness report**: The versioned list of checks, each with a status and one reason code.
- **Migration command**: The one-shot operation that applies, reverses, or reports migrations.
- **Image**: One plane's OCI image and its Dockerfile.
- **Container contract**: The versioned contract `container`, which owns the above.
- **Compose fixture and cross-host lifecycle test**: The acceptance evidence.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With packages restored and no cloud credentials, the format check, build, and tests pass with zero warnings, and all 10 CI checks pass.
- **SC-002**: In the Compose fixture, both containers launch and report live in 100% of the scenarios of User Story 1 apart from the rejected configuration, and readiness names the actual missing dependency's reason in 100% of them, on 5 consecutive runs.
- **SC-003**: Each of the persistence probe's 10 reasons and the coordination probe's 7 reasons, with both forms of `not_configured`, is shown by at least one test to give the readiness status TR-8 lists, and liveness makes 0 probe calls across the suite.
- **SC-004**: Each exit code in TR-10 is produced by at least one test, and each of the five refusal reasons is shown to fail start for a mandatory feature, and, for each reason that can apply to a feature without `MandatoryPath` (all but `degrade_not_permitted`, which only a mandatory path produces), to leave the capability check ready and list the feature under `refused`. The process may still be not ready through another check, as with `coordinator_not_configured` (coordination) and `single_instance_violated` (single instance) (maintainer decision, 2026-11-16, `implement`).
- **SC-005**: After a stop signal, the first readiness request answers `503` with `stopping`, the instance lease is released, and the process exits 0, in each stop test, including a stop during start-up.
- **SC-006**: With canaries planted as a configuration value, a secret reference name, a database host, and a user name, each canary appears 0 times in readiness answers, exit diagnostics, and captured container logs across the suite.
- **SC-007**: Both images run as a numeric non-root user, with no capability and a read-only root filesystem, and image inspection finds 0 control-plane assemblies in the data-plane image and 0 `Iskender.Hosting` assemblies in the control-plane image.
- **SC-008**: The cross-host lifecycle test finds 100% of compared responses matching, apart from generated IDs.
- **SC-009**: The compatibility harness reports 0 changes to `hosting`, `core-contracts`, `providers-abstractions`, and `policy-sdk`, and ISK-0006's hosting suite passes with 0 behavior assertions changed or removed, under US6.4's rule for moved internal types.
- **SC-010**: The PR changes 0 tracked files under `.specify/` and 0 in `.iskender/grounding.yaml`, and adds 0 gateway ingress endpoints, management endpoints, authentication schemes, providers, credential providers, cloud provisioning files, or registry pushes.

## Assumptions

- ISK-0003 to ISK-0006 are merged and complete on `main`, and their contracts, the configuration engine, the persistence adapter, the coordination ports and coordinators, the hosting registrations, and `EmbeddedHost` exist as their contracts describe.
- One gateway per container. Several independently configured gateways in one container are post-MVP.
- Configuration is validated once per container start. Changing it while running belongs to ISK-0057.
- No product feature registers a `FeatureRequirement` yet, so the refusal mapping is proved with test features.
- Until ISK-0021, no container that composes PostgreSQL can be ready, and the ready path is proved through the composition core with a test credential provider.
- Any interaction with policy plans, plugin trust, durable conversations, financial controls, or streaming goes through those specs' authoritative documents. This spec composes none of them.

## Out of Scope

- Production cloud provisioning, Kubernetes manifests, Helm charts, and Azure Container Apps templates.
- A management API and the admin UI, comprehensive or otherwise.
- Gateway ingress, protocol endpoints, providers, and authentication (ISK-0011 onward, ISK-0029 onward).
- The developer Compose topology, seed-free start-up, data reset, and smoke scripts (ISK-0008).
- Database and coordination credential providers (ISK-0021).
- Background jobs and scheduling `PurgeExpiredAsync` (ISK-0009).
- Publishing images to a registry, multi-architecture images, and TLS on the entry points' own listeners.
- Changing the constitution, the roadmap, ADRs, ISK-0003's precedence, ISK-0004's or ISK-0005's port signatures or contract rules, or `.iskender/grounding.yaml`. Any roadmap consequence goes through `/review-roadmap`.
