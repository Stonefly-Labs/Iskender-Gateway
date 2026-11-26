# 04 — Technology stack and deployment

**Authority:** approved technology baseline, distribution and deployment invariants. Detailed throughput/recovery criteria in [12](12-observability-and-nonfunctional-requirements.md); dependencies and security in [05](05-identity-security-and-privacy.md).

## Choices and status

| Area | Baseline | Status and rationale |
|---|---|---|
| Runtime | **.NET 10**, ASP.NET Core, C# | Approved; host-agnostic, optimized networking and composable DI |
| Server components | Modular monolith in domain, independently deployed control/data processes | Approved architecture |
| Durable store | **PostgreSQL** | Approved; transactional records and durable conversations. ISK-0004 sets the ports, migrations, isolation, and health rules in its [persistence contract](../specs/0010-system-operator-bootstrap-security-baseline/contracts/persistence.md) (version 3) |
| Coordination/cache | **Redis**-compatible service | Approved option/reference for hot/ephemeral state, distributed counters; requirement determined by enabled capabilities/topology. ISK-0005 sets what each coordinator declares and when a feature is refused in its [coordination contract](../specs/0009-module-events-background-work-coordination/contracts/coordination.md), and Valkey 8 is the tested server |
| Management UI | **Next.js + shadcn/ui** | Approved; keep admin UI outside latency-critical data plane |
| Telemetry | **OpenTelemetry** | Approved common instrumentation standard |
| Distribution | Standard OCI container(s) **and NuGet/`AddIskender(...)`** | Approved parallel consumption modes |
| First cloud reference | Azure Container Apps + Azure Database for PostgreSQL + Azure managed Redis | Approved initial reference topology, not code-level lock-in |
| Local development | Native .NET + containers for dependencies, Docker Compose | Recommended implementation of approved local/on-prem support |
| Build | .NET SDK, centrally managed versions/dependencies, GitHub Actions | Recommended tooling within approved CI practice |
| Libraries | EF Core/Npgsql, Dapper, StackExchange.Redis, Polly, typed HTTP clients, CEL interpreter implementations | **Candidates, not mandates**; benchmark, review licensing and use only where appropriate. ADR-0035 chose Npgsql with plain SQL, and no mapper or migration library, for PostgreSQL, and ADR-0036 chose StackExchange.Redis for the Redis adapter |
| Performance extensions | Native/Rust only if measurements justify | Approved future flexibility, **not** an MVP language requirement |

**No default cloud secret service, message broker, queue, or SaaS metrics provider is mandated.** Expose adapters for secret resolution, identity and config notifications, and implement minimal infrastructure using durable PostgreSQL/Redis primitives. Avoid cloud-specific SDKs in core/domain contracts.

## Deployable topologies

1. **All-in-one developer distribution:** control API, data-plane host and optional UI started together by one command in an easy dev distribution. The planes run as separate processes, each in its own container from its unchanged image, because a single container would need a process supervisor that the images do not have ([Local development](#local-development)); not proof of production scalability.
2. **Production split:** independent data-plane replica group, control-plane API/UI and optional maintenance worker. Separate scaling, security, health/readiness and rollout units; database/Redis accessed through constrained identities.
3. **Embedded:** consumer app calls `builder.Services.AddIskender(...)` and maps gateway endpoints with `app.MapIskender(prefix)`, as the [hosting contract](../specs/0010-system-operator-bootstrap-security-baseline/contracts/hosting.md) sets out (see the [Embedding guide](#embedding-guide)). Host provides DI and may opt into control-plane services; data plane must not take global ownership of routes, logging or configuration. No hidden external control-plane requirement for configured embedded use.
4. **Azure reference:** ACA revision-based deployment, PostgreSQL with backup/PITR planning and managed Redis for coordination. Secret references through workload identity or a replaceable secret provider. Managed identities used where supported; no assumption Azure is the only cloud.
5. **Cloud/on-prem alternatives:** OCI/Kubernetes or ordinary VMs, compatible Postgres, compatible Redis/alternative concurrency coordinator, external OIDC. Local Compose can run the full basic topology.

## Package boundaries and composition

Propose separate packages for hosting/contracts, policy SDK, provider adapters, storage/coordination adapters and optional management, with stable versioning. Do **not** leak private storage models into NuGet public interfaces. A sample ASP.NET Core app must demonstrate `AddIskender(...)` alongside user middleware/services with no fork of Iskender source. Keep provider-specific registration conditional. The official plugin template and build packaging flow live in [11](11-plugin-extensibility-and-sdk.md).

ISK-0001 set these packages out in its [project roles](../specs/0007-container-entry-points-process-separation/contracts/module-dependency-rules.md#project-roles): `Iskender.Hosting`, `Iskender.Core.Contracts`, `Iskender.Policy.Sdk`, `Iskender.Providers.Abstractions` with adapters in `Iskender.Providers.<Provider>`, `Iskender.Infrastructure` with storage and coordination adapters in `Iskender.Infrastructure.<Adapter>`, and `Iskender.Management`, alongside `Iskender.Domain`. Only an infrastructure adapter may take a database driver, only a provider or infrastructure adapter may take a cloud SDK, and only a provider adapter may take a provider SDK. The hosting sample lives in `samples/` with the `sample` role, which version 4 of that contract added.

ISK-0006 versioned `Iskender.Hosting` as the `hosting` contract and composes the infrastructure adapters through one explicit registration method each. ISK-0007 revised it to version 2, which adds the control plane's root, `AddIskenderControlPlane(...)`, and ISK-0009 to version 3, which adds the worker's root, `AddIskenderWorker(...)`, as the [hosting contract](../specs/0010-system-operator-bootstrap-security-baseline/contracts/hosting.md#registration) lists. The two container images follow the [container contract](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md). `samples/Iskender.Samples.EmbeddedHost` is the sample application this section asks for.

ISK-0002 versions `Iskender.Core.Contracts`, `Iskender.Providers.Abstractions`, and `Iskender.Policy.Sdk` as separate contracts, and `tests/Iskender.Contracts.Compatibility.Tests` fails the test run on an undeclared break to any of them, under the [versioning policy](../specs/0009-module-events-background-work-coordination/contracts/public-contracts-and-versioning.md#versioning-policy). Until ISK-0086 completes, package versions follow ADR-0030.

## Embedding guide

This is the guide for an ASP.NET Core application that embeds Iskender in its own process. The [hosting contract](../specs/0010-system-operator-bootstrap-security-baseline/contracts/hosting.md) holds the rules behind it, and [`samples/Iskender.Samples.EmbeddedHost`](../samples/Iskender.Samples.EmbeddedHost/README.md) is a working example.

**Add the packages.** Reference `Iskender.Hosting`, and the infrastructure packages for the modules you choose: `Iskender.Infrastructure` for coordination, `Iskender.Infrastructure.PostgreSql` for PostgreSQL persistence, its coordinator, and background work, and `Iskender.Infrastructure.Redis` for the Redis coordinator. Like all of Iskender, they are `MIT`-licensed.

**Choose modules and adapters.** Iskender composes only what you register, with one call each on your own `IServiceCollection`, in any order. Nothing is found by scanning assemblies.

| Method | Module | Requires |
|---|---|---|
| `AddIskender(options => ...)` | `hosting` | |
| `AddIskenderCoordination()` | `coordination` | `hosting` |
| `AddIskenderPostgreSqlPersistence()` | `persistence-postgresql` | `hosting` |
| `AddIskenderPostgreSqlCoordination()` | `coordination-postgresql` | `coordination`, `persistence-postgresql` |
| `AddIskenderRedisCoordination()` | `coordination-redis` | `coordination` |
| `AddIskenderPostgreSqlWork()` | `work-postgresql` | `persistence-postgresql` |
| `AddIskenderPostgreSqlWorkDispatcher()` | `work-dispatcher` | `work-postgresql`, `coordination-postgresql` |
| `AddIskenderPostgreSqlIdentity()` | `identity-postgresql` | `persistence-postgresql`, `work-postgresql` |
| `AddIskenderOperatorSecurity()` | `operator-security` | `control-plane`, the root `AddIskenderControlPlane(...)` registers, and `identity-postgresql` |

`AddIskenderPostgreSqlWork()` adds the work outbox, so your modules can enqueue background work in their own units of work, and the `directory` and `work` migration modules. `AddIskenderPostgreSqlWorkDispatcher()` adds the dispatcher and the shipped recurring jobs, and registers the `work` section. Compose it only where you mean to run work in your process, and then with leases on PostgreSQL, since it needs strict leases and fencing: with `redis` or `local` leases it is refused and your host fails to start. A module declares each kind it enqueues with `WorkRegistrations.AddKind`, and its handler with `WorkRegistrations.AddHandler<THandler>`, resolved from your container. The [Background work](#background-work) runbook has the rest.

**Embedding the control plane.** From ISK-0010, the control plane's admin surface composes into your host the same way, under `AddIskenderControlPlane(...)` instead of `AddIskender(...)`: add `AddIskenderOperatorSecurity()`, `AddIskenderPostgreSqlPersistence()`, `AddIskenderPostgreSqlWork()`, which `identity-postgresql` needs for migration order only, and `AddIskenderPostgreSqlIdentity()`, then call `app.MapIskenderOperatorSecurity("/admin/v1")`, or under a prefix of your own, once. Calling it twice, or never, fails the host's start with `operator.route_undeclared`, as does an endpoint you add to the group without exactly one of `RequireOperatorAction(...)` or `AllowAnonymousOperatorEndpoint(...)`. Register your own `IOperatorPrincipalResolver` if you have one; with none, every protected admin route answers `401`. **An application that embeds both planes runs two hosts**, each with its own service collection, because two roots on one collection fail with `hosting.registration_duplicate`, and `operator-security` under `AddIskender(...)` or `AddIskenderWorker(...)` fails with `hosting.registration_missing` ([Embedding the control plane](../specs/0010-system-operator-bootstrap-security-baseline/contracts/hosting.md#embedding-the-control-plane)). The [Operator bootstrap](#operator-bootstrap) runbook has the rest.

A module whose requirement is missing fails the host's start with `hosting.registration_missing`, and a registration called twice fails at the second call with `hosting.registration_duplicate`. Iskender registers no `TimeProvider`, `ILoggerFactory`, or credential provider: it uses yours if you register one, and otherwise falls back to the system clock, no logging, and no credentials.

**Configuration sources and the environment name.** Iskender reads only the documents you give it in `IskenderOptions`: at most one base and one overlay, each a file path, text, or stream factory, in JSON or YAML, and, only if `ReadEnvironmentVariables` is `true`, the process's `ISKENDER__` variables. It never reads your `IConfiguration`, `IHostEnvironment`, or `IWebHostEnvironment`, and never changes them, so an `iskender` key in your own configuration has no effect. Its environment name is `IskenderOptions.EnvironmentName`, `null` by default, which is production-grade. It is never derived from your host's, so a host running as `Development` does not relax any of Iskender's defaults or allow its development-only values: set `EnvironmentName = "development"` yourself where you mean it. Registration reads nothing: documents are read and validated once per container, when the host starts.

**Start-up failure.** A host whose Iskender configuration is rejected, or whose document cannot be read, fails to start with `hosting.configuration_rejected`. Its diagnostics are ISK-0003's value-free report, and the log holds ISK-0003's events 3002 and 3003, then event 6002. A successful start writes 3001 and then 6001, naming the same revision. A missing registration, or a coordinator that the configuration selects but nothing registered, fails with `hosting.registration_missing` and event 6003, which names what is missing and what needs it. With coordination composed, a feature the capability check refuses is logged as events 5001 and 5002. A refused feature declared with `MandatoryPath` then fails the host's start with `coordination.capability_unsupported` and event 6004, which names the feature, its module, and the refusal reason, after the start-ups already started have stopped, so a held instance lease is released. Any other refused feature does not stop the start, and every call through it fails closed. The coordination start-up runs after your server has started, so a request that arrives in between finds coordination unavailable. Every hosting event is written under the category `Iskender.Hosting`, and none holds a configuration value.

**The route prefix.** `app.MapIskender("/iskender")` maps one route group under the prefix and returns it, so you can add your own conventions to it. Call it after your own middleware: your middleware runs for Iskender's requests as for any other endpoint. The prefix must start with `/`, and may be `/`. ASP.NET Core's routing precedence decides between your routes and Iskender's: your more specific routes keep matching, even under Iskender's prefix, and Iskender's catch-all answers everything else under it with `404` and the `core.resource.not_found` error envelope. You may map more than one prefix, and every group shares the one validated configuration. An endpoint you add to the group can't take `GatewayExecutionContext` as a parameter: minimal APIs bind a handler's service parameters before the group's request bootstrap runs, so the parameter fails with `core.context.not_resolved`. Resolve the context from `HttpContext.RequestServices` inside the handler instead.

**What Iskender does not change.** It adds no middleware, `IStartupFilter`, exception handler, authentication or authorization scheme, CORS policy, logging provider, metric or trace exporter, or fallback route, and it configures none of your options types. A request outside every Iskender prefix runs no Iskender code. It keeps no static or process-wide state, so two hosts in one process each keep their own configuration, and disposing your host disposes everything Iskender created.

## Container entry points

This is the operator guide for the two container images, and the worker the control-plane image also runs. The [container contract](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md) holds the rules behind it; this guide says what to do with them.

**The two images.** `src/Iskender.Gateway.Host/Dockerfile` builds the data plane, which serves Iskender's routes, and `src/Iskender.ControlPlane.Host/Dockerfile` builds the control plane, which serves health and, from ISK-0010, the admin routes under `/admin/v1` on its traffic listener. Each holds only its own plane: no `Iskender.Management` in the data-plane image, no `Iskender.Hosting` in the control-plane image, and no SDK, source, test assembly, shell, or package manager in either. Both run as user `1654:1654`, carry the OCI `source`, `revision`, and `licenses` labels, and pin their base images by digest ([Images](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#images)). Build each from the repository root:

```bash
docker build -f src/Iskender.Gateway.Host/Dockerfile -t iskender-data-plane:local --build-arg REVISION="$(git rev-parse HEAD)" .
```

**The worker.** The control-plane image's `work` command runs the worker, its own composition plane with `service.name` `iskender-worker`: the control plane's modules, the background work dispatcher, and its recurring jobs. It binds only the health listener, answers liveness and readiness with the same five checks, and holds the single-instance lease `instance.worker` when declared `single_instance`. Run it as its own deployment, from the same image, with its own documents, whose `coordination.leases.coordinator` must be `postgresql`; `work` on the data-plane image is a usage error, exit 64. Both planes only enqueue, so they need no `work` section and run no work ([Entry points](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#entry-points)). The [Background work](#background-work) runbook has its configuration and operation.

**Configuration.** Each image reads `iskender.yaml` or `iskender.json` from `/etc/iskender`, or from `ISKENDER_CONFIG_DIRECTORY`, and, when `ISKENDER_ENVIRONMENT` is set, the overlay `iskender.<environment>.yaml` or `.json`, then the `ISKENDER__` variables, in ISK-0003's precedence. With no `ISKENDER_ENVIRONMENT` the process is production-grade, so development-only values are rejected. `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`, `appsettings*.json`, and command-line settings never reach Iskender's configuration or its listeners ([Configuration roots](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#configuration-roots)). Mount the directory read-only. A plane validates only the sections it registers, so a control-plane document with a `hosting` section is rejected. The `container` section sets the two ports, the stop timings, and the readiness cache ([the `container` section](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#the-container-section)).

**Listeners.** The traffic listener, port 8080 by default, serves the data plane's routes under `/`, and on the control plane, from ISK-0010, only the admin routes under `/admin/v1`; the worker has none. The health listener, port 8081, serves only `GET /health/live` and `GET /health/ready`. Each traffic listener's transport is the `container` section's `trafficTransport`: `listener`, in-process TLS 1.2 or later from mounted PEM files, `upstream`, TLS terminated in front of the container, or `none`, plain HTTP, accepted only in `development`. **Every production document must declare `listener` or `upstream`**, since the default is `none` and a plane that starts with it outside `development` exits 2. The health listener stays plain HTTP: do not publish the health port beyond the orchestrator, since its port is its only protection ([Listeners](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#listeners)).

**Liveness and readiness.** Point the liveness probe at `/health/live`, which calls no dependency, and the readiness probe at `/health/ready`, which answers `200` or `503` with five checks and a reason each ([Readiness](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#readiness)). Readiness is advisory: the traffic listener serves the same way either way. What to do about a reason that is not ready:

| Check | Reason | Operator action |
|---|---|---|
| `lifecycle` | `starting`, `stopping` | None; the process is starting its modules or stopping |
| `persistence` | Any but `ok` and `migration_in_progress` | As the [Database migrations](#database-migrations-and-rollback) health reasons say |
| `coordination` | Any but `ok` and a healthy `not_configured` | As the [Coordination](#coordination) health reasons say |
| `single_instance` | `single_instance_held_by_other` | Another process of this plane holds the instance lease; stop it, or declare `multi_replica` |
| `single_instance` | `single_instance_lost` | The lease was lost and is being retaken; check the database if it persists |
| `capability` | `refused_features` (ready) | Each refused feature is listed with its reason; see the [Coordination](#coordination) refusal reasons |
| Any check | `unexpected` | Reading the check threw; event 7007 names the check and the exception's type. Report it if it persists |

**Health checks in a container.** The images declare no `HEALTHCHECK`, so the orchestrator chooses its probes. A container health check runs the exec-form command `["dotnet", "/app/Iskender.Gateway.Host.dll", "health", "ready"]`, or `"live"`, with `Iskender.ControlPlane.Host.dll` on the control plane and the worker, which runs the control-plane image. It needs no shell, and exits 0 on `200` and 1 otherwise ([Command line and exit codes](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#command-line-and-exit-codes)).

**Exit codes.** The contract's [exit code table](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#command-line-and-exit-codes) is authoritative. In short: 0 is a clean stop or a command that succeeded; 1 is an unexpected failure, such as a port that cannot be bound; 2 is a rejected configuration or an unusable configuration root, so read events 3002, 3003, and 6002; 3 is a missing module registration, event 6003; 4 is a refused feature with `MandatoryPath`, event 7002 naming the feature, module, and reason, such as a worker whose leases are not on PostgreSQL, refused for `work.dispatch`, which a restart does not clear until the cause is fixed ([Start-up](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#start-up)); 5, 6, and 7 are the `migrate` outcomes below, and 5 also a `bootstrap` command that cannot reach the store; 8 is `bootstrap issue` on an initialized installation and 9 `bootstrap recover` on an uninitialized one; 10 is a control plane whose admin route has no single declaration, event 9005; 2 also covers `operator.transport_unprotected` and `operator.certificate_unusable`, whose event 7002 names the failing field, such as `iskender.container.tlsKeyFile`, as its reason; and 64 is a usage error.

**Hardening.** Run each container with a read-only root filesystem, `cap_drop: ALL`, and `no-new-privileges`. With a read-only root, mount `/tmp` writable, as a `tmpfs` or an empty volume, since .NET writes its diagnostics socket there ([Images](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#images)).

**Stopping.** On `SIGTERM` the process turns `stopping`, keeps serving for `shutdownDelay`, drains requests for up to `shutdownTimeout`, releases its instance lease, and exits 0. The worker instead stops claiming work at the signal and lets in-flight items finish within the same bound ([Lifecycle](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#lifecycle)). **The orchestrator's grace period must exceed `shutdownDelay` + `shutdownTimeout` + 5 seconds**: 35 seconds with the defaults, longer than Docker's default of 10, so set `stop_grace_period` or `terminationGracePeriodSeconds`. A shorter grace period kills the process mid-stop, and the lease then expires on its TTL.

**Migrations in a release.** Run `migrate apply` from the control-plane image, as a one-shot job with the same documents as the serving control plane, before rolling out the release, and `migrate reverse` with the newer release before rolling back to an older one. No serving process migrates or needs the migration login. The [Database migrations](#database-migrations-and-rollback) runbook has the commands and their exit codes.

**The `bootstrap` command.** The control-plane image's `bootstrap issue` and `bootstrap recover` run once, with the serving control plane's documents and the runtime login, and print the token alone to standard output, with every event, 7008 included, on standard error ([Bootstrap command](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#bootstrap-command)). The [Operator bootstrap](#operator-bootstrap) runbook says how to run it.

**Before ISK-0021.** No credential provider exists yet, so a container that composes PostgreSQL reports persistence `authentication_failed` and is not ready, the control plane's admin routes answer `503` with `operator.state_unavailable`, and every `migrate` and `bootstrap` operation exits 5 ([Threat model and known limits](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#threat-model-and-known-limits)).

## Local development

This is the guide to the developer topology under `local/`, which runs both container images, and the worker, with PostgreSQL and Valkey on a workstation, with no cloud credentials. The [local environment contract](../specs/0010-system-operator-bootstrap-security-baseline/contracts/local-environment.md) holds the rules behind it.

**Prerequisites.** `git`; Docker Engine 24.0 or later with Docker Compose 2.24.0 or later, running; and `bash` 3.2 or later, which macOS ships. On Windows, run the scripts in WSL 2 with Docker Desktop's WSL integration. The first start needs the network: it pulls three images pinned by digest and restores packages from nuget.org inside the image build.

**The topology.** `local/scripts/start.sh` builds both plane images from the checkout, tagged `<project>/data-plane:local` and `<project>/control-plane:local` and labelled with `git rev-parse HEAD`, and starts five services: `postgres` (PostgreSQL 17), `valkey` (Valkey 8), `data-plane`, `control-plane`, and `worker`, which runs the control-plane image with `work`. It waits up to 180 seconds for each to be healthy, the planes and the worker through their own `health live` command. This is `docs/04` topology 1's all-in-one developer distribution, built from the two unchanged images. It is not one container: the planes must run as separate processes, one container running two needs a process supervisor as its entry point, which would be a new entry point, and the runtime image has no shell or supervisor ([Services](../specs/0010-system-operator-bootstrap-security-baseline/contracts/local-environment.md#services) rule 4). The optional management UI is left out until its spec adds it. **This settles ISK-0007's AV-6.** Each plane, and the worker, runs with a read-only root, `/tmp` on `tmpfs`, every capability dropped, `no-new-privileges`, and a 40-second stop grace period. No service seeds data or runs a migration.

**Scripts.** Each runs from any directory of the checkout, prints its usage with `--help`, and writes only value-free messages to standard error.

| Script | Options | Does |
|---|---|---|
| `local/scripts/start.sh` | `--dependencies` | Builds both images and starts the default topology, or with `--dependencies` starts only `postgres` and `valkey` |
| `local/scripts/stop.sh` | | Stops every service. Keeps the volumes and the password file |
| `local/scripts/reset.sh` | `--yes` | Removes the project's containers, network, volumes, and password file, after confirmation in a terminal. A non-interactive shell needs `--yes`. Removes no image and nothing of another project |
| `local/scripts/migrate.sh` | `apply`, `reverse [--steps <n>] [--confirm-data-loss]`, `status` (default) | Runs the control-plane image's `migrate` command once, with the control plane's documents, and exits with its code |
| `local/scripts/bootstrap.sh` | `issue`, `recover` | Runs the control-plane image's `bootstrap` command once, attached, with the control plane's documents, and exits with its code. The token is on its standard output only |
| `local/scripts/smoke.sh` | | Asserts the running default topology's expected state, below |

| Exit code | Meaning | What to do |
|---|---|---|
| 0 | Success; for smoke, every assertion passed | Nothing |
| 1 | A smoke assertion failed, an image build or Compose step failed, or a service was not healthy within 180 seconds. The message names the service and its exit code | Read the named service's logs with `docker compose -p <project> -f local/compose.yaml logs <service>`; an exit code of 2 from a plane is a rejected document |
| 2 | A prerequisite is missing or unusable: no `git` or `docker`, the runtime not running, Compose older than 2.24.0, a script outside a checkout, a port in use, a database volume with no password file, a malformed password file, or, for `migrate.sh`, no control-plane image | Fix the named prerequisite. For a port, set its variable. For the password file, run `reset.sh`. For the image, run `start.sh` |
| 64 | A usage error: an unknown option or operation, an invalid project name or port, or a reset without `--yes` in a non-interactive shell | Correct the command; see `--help` |

`migrate.sh` and `bootstrap.sh` otherwise exit with their command's own code, such as 5 below; `bootstrap.sh` with no control-plane image exits 2.

**Ports.** Only these are published, and only on `127.0.0.1`. No health port is ever published, and the worker publishes nothing. From ISK-0010 the control plane publishes its traffic port, for the admin routes.

| Service | Container port | Default host port | Override | Mode |
|---|---|---|---|---|
| `data-plane` | 8080 | 18080 | `ISKENDER_LOCAL_DATA_PLANE_PORT` | Default |
| `control-plane` | 8080 | 18090 | `ISKENDER_LOCAL_CONTROL_PLANE_PORT` | Default |
| `postgres` | 5432 | 15432 | `ISKENDER_LOCAL_POSTGRES_PORT` | `--dependencies` |
| `valkey` | 6379 | 16379 | `ISKENDER_LOCAL_VALKEY_PORT` | `--dependencies` |

An override must be an integer from 1024 to 65535. Start exits 2, naming the variable, when a port it will publish already answers on loopback, unless the project's own service publishes it.

**Documents.** `local/config/<plane>/iskender.yaml` holds each plane's base, which validates alone with no environment name. `iskender.development.yaml` holds every development-only value: PostgreSQL and Valkey without TLS, and a Valkey endpoint with no user or password, with Redis selected for atomic operations, leases, and cache. `local/config/worker/` holds the worker's pair: the control plane's base without the migration login, and its overlay with PostgreSQL leases, so the dispatcher is not refused, and no `work` section, so it runs on the defaults. Each plane service and the worker sets `ISKENDER_ENVIRONMENT=development` itself, so a value in your shell never reaches a container. Precedence is ISK-0003's: defaults, base, overlay, `ISKENDER__` variables, with no further layer. **This settles ISK-0003's AV-6 for local use.** The passwords appear in the documents only as the secret references `local/postgres-runtime` and `local/postgres-migration`.

**Passwords.** The first start writes `local/.secrets/<project>.env`, which Git and Docker ignore, holding three generated 32-character passwords for PostgreSQL's superuser, the runtime login `iskender_local_runtime`, and the migration login `iskender_local_migrator`. The directory is mode `0700` and the file `0600`, reset on every start. Only `postgres` reads it, and an initialisation script creates the two logins and the group role `iskender_runtime` on an empty volume. No script prints a password. Keep the file while its volume exists: a volume without its file, or a file without exactly its three values, makes start exit 2, and `reset.sh` is the way back.

**Project name.** The Compose project is `iskender-local`, or `ISKENDER_LOCAL_PROJECT`, which must be `iskender-local-` followed by 1 to 24 characters of `a-z` and `0-9`; anything else exits 64. The name also names the image tags and the password file. It is per Docker host, not per checkout: two checkouts or worktrees under one name are one project, and a reset from either removes the other's data, so give each checkout that runs its own topology its own name and ports.

**Reset.** `reset.sh` runs `docker compose down --volumes --remove-orphans` for the project only, then removes its password file. The next start is a first start, with new passwords.

**The `dependencies` mode and native runs.** `start.sh --dependencies` stops the planes and the worker if they run, builds no image, and starts `postgres` and `valkey` published on loopback, reusing the same volumes as the default mode. Run a host natively against them with its built assembly or `dotnet run`, setting `ISKENDER_CONFIG_DIRECTORY` to `local/native/data-plane`, `local/native/control-plane`, or `local/native/worker` and `ISKENDER_ENVIRONMENT=development`. Run the native worker as the control-plane host with the argument `work`:

```bash
ISKENDER_CONFIG_DIRECTORY="$PWD/local/native/worker" ISKENDER_ENVIRONMENT=development dotnet run --project src/Iskender.ControlPlane.Host -- work
```

The data plane runs the same way, with its own directory and project:

```bash
ISKENDER_CONFIG_DIRECTORY="$PWD/local/native/data-plane" ISKENDER_ENVIRONMENT=development dotnet run --project src/Iskender.Gateway.Host
```

The native documents match the container ones except for the loopback host, the published ports, and the listener ports of the control plane, 8090 and 8091, and of the worker, 8092 and 8093, so all three hosts can run at once. After overriding the PostgreSQL port, set `ISKENDER__PERSISTENCE__PORT` to the same value. A Valkey port override needs an edited copy of the native directory, because the Redis endpoint list cannot be set by a variable. A native host listens on all interfaces, unlike the topology's loopback ports.

**Smoke assertions.** `smoke.sh` checks, against the default topology, that all five services run; that each plane's and the worker's `health live` command exits 0; that each plane's and the worker's `/health/ready`, read through an in-network `curl` container, answers `503` with exactly the body the contract records, which for the worker also has coordination `authentication_failed`, after waiting up to 60 seconds while `lifecycle` is `starting`; that `migrate.sh status` exits 5 with one event 7006 naming `persistence.unavailable`; that PostgreSQL holds exactly the three provisioned roles and grants, with no schema or table of Iskender's; that Valkey's `maxmemory-policy` is `noeviction`; that, from ISK-0010, the control plane's `POST /admin/v1/bootstrap`, with a well-formed token generated at each run, and `GET /admin/v1/installation` answer `503` with `operator.state_unavailable`, and `bootstrap.sh issue` exits 5, prints nothing, and writes one event 7008; and that no plane or worker log, readiness answer, or smoke output holds a password, the database name, a login name, a secret reference name, or the generated token. It runs every check and names each failure, with the reason found, such as `coordination: expected ok, found unreachable`.

**Before ISK-0021.** No credential provider exists, so both planes and the worker report persistence `authentication_failed` and are not ready, the worker reports coordination `authentication_failed` too and dispatches nothing, every `migrate` and `bootstrap.sh` operation exits 5, and the control plane's `POST /admin/v1/bootstrap` and `GET /admin/v1/installation` answer `503` with `operator.state_unavailable`. The smoke run asserts exactly this state and fails on any other, so the spec that changes it revises the expected state.

## Operator bootstrap

This is the operator runbook for the system operator's first setup and recovery. The [operator-security contract](../specs/0010-system-operator-bootstrap-security-baseline/contracts/operator-security.md) holds the rules behind it. Before ISK-0021 no credential provider exists, so every step that reaches the store exits 5 or answers `503`, and before ISK-0013 or ISK-0014 there is no sign-in, so an initialized installation's protected routes answer `401` to everyone.

**Issuing the bootstrap token.** A new installation is `uninitialized`. Run the control-plane image's `bootstrap issue` once, as a one-shot job with the serving control plane's documents, **attached to your own terminal**, and read the token from it: it is printed once, on standard output, and stored only as a hash. Its events and diagnostics go to standard error. Do not run it under an orchestrator that collects standard output, since the collector would then hold the token. Issuing again supersedes the earlier token. The token lasts `operator.bootstrap.tokenLifetime`, one hour by default. On an initialized installation the command exits 8 with `operator.already_initialized`.

**Redeeming it.** Redeem it at once with `POST /admin/v1/bootstrap`, as `application/json` with exactly `{"token":"<token>"}`, on the control plane's traffic listener. Exactly one redemption ever succeeds. It answers `200`, with `Cache-Control: no-store`, `{"principalId":"<ID>","enrollmentCode":"<code>"}`: the first operator principal, granted `system_operator`, and a one-time enrollment code for it, valid for `operator.enrollment.codeLifetime`, 24 hours by default. Keep the code as you kept the token. ISK-0013 or ISK-0014 attaches the first sign-in method through it. Every failed redemption answers `403` with `operator.redemption_failed` and the same body, whatever the reason, and more than `operator.bootstrap.attemptsPerMinute` attempts a minute, 10 by default, per process, answer `429` with `operator.redemption_limited`. Until the installation is initialized, every other admin route answers `403` with `operator.uninitialized`.

**Recovery.** **The recovery path is a draft awaiting human approval** ([Recovery draft](../specs/0010-system-operator-bootstrap-security-baseline/contracts/operator-security.md#recovery-draft)). On an initialized installation, `bootstrap recover` issues a recovery token the same way, and `POST /admin/v1/recovery` redeems it, creating a new operator principal and enrollment code. It never changes another principal. On an uninitialized installation the command exits 9 with `operator.not_initialized`. Its trust anchor is the ability to run the control plane's command with its documents, as for `migrate`. A lost or expired enrollment code is replaced only through recovery.

**TLS.** Outside `development`, both planes' traffic listeners must declare `trafficTransport` as `listener` or `upstream`. With `listener`, mount an unencrypted PEM certificate chain and private key read-only and name them in `tlsCertificateFile` and `tlsKeyFile`, as absolute paths. The pair is loaded once, at start, so rotation is a restart, and a missing, unreadable, malformed, encrypted, not-yet-valid, expired, or mismatched pair exits 2 with `operator.certificate_unusable`, naming the field and never the path. With `upstream`, terminate TLS in front of the container, which Iskender cannot verify ([Transport](../specs/0010-system-operator-bootstrap-security-baseline/contracts/operator-security.md#transport)). **Upgrading from ISK-0009, every document of a plane that starts outside `development` must now declare `trafficTransport`**, or the plane exits 2 with `operator.transport_unprotected`.

**What to publish.** Publish the control plane's traffic port only to the operators who bootstrap and administer it, behind its TLS, and never its health port. Never publish, log, paste, or commit a bootstrap token, recovery token, enrollment code, certificate key, or the job's standard output. No tracked file in the repository holds a token or a PEM block, and a test enforces it. The endpoints reachable without authentication are exactly the contract's [anonymous endpoint inventory](../specs/0010-system-operator-bootstrap-security-baseline/contracts/operator-security.md#anonymous-endpoint-inventory).

**Audit records.** Each issue, supersession, redemption naming a stored token, success or failure, and enrollment consumption appends a row to `isk_audit.records` in the same commit as the change, with the actor, action, result, reason class, configuration revision, and correlation ID, and never a token, hash, or address. The runtime login may insert and read them, never update or delete them. A malformed or unknown redemption, a limited attempt, and a denied request write no record, so a flood cannot grow the table ([Audit records](../specs/0010-system-operator-bootstrap-security-baseline/contracts/operator-security.md#audit-records)).

**Diagnostics.** Events 9001 to 9008 are written under the category `Iskender.Identity`, and the container's 7008 for each `bootstrap` run. 9002 names a failed redemption's reason class, 9003 a denied request's route template and reason, 9005 an undeclared route, which stops start with exit code 10, and 9008 an unreadable store. The meter and activity source `Iskender.Identity` count redemptions, denials, and issued and superseded tokens. No field or label holds a token, hash, principal ID, or address ([Diagnostics](../specs/0010-system-operator-bootstrap-security-baseline/contracts/operator-security.md#diagnostics)).

**Rolling back.** Below ISK-0010, reverse two steps with this release, `identity` and then `audit`, both data-losing: `migrate reverse --steps 2 --confirm-data-loss`. That removes the installation state, the operators, and the audit records, so the next bootstrap starts again from `uninitialized`. Then remove `trafficTransport` and the `operator` section from the documents only if the older release rejects them.

**Known limits.** No sign-in exists before ISK-0013 or ISK-0014, and no database access before ISK-0021. `upstream` is a declaration Iskender trusts. The token's only channel is the command's standard output. A refused redemption answers sooner when it reads no store, and a known identifier lets anyone write bounded failure records. The attempt limit is per process, so replicas multiply it; the 256-bit secret, not the limit, defeats guessing. An expired enrollment code strands its principal, which keeps its grant until a later spec removes principals ([Known limits](../specs/0010-system-operator-bootstrap-security-baseline/contracts/operator-security.md#known-limits)).

## Deployment invariants

- Separate **liveness** (process works) from **readiness** (mandatory config, durable store, required verified plugins, coordination backend and provider registrations ready); expose safe diagnostic reason codes.
- Schema upgrades are versioned and forward/backward compatible across a documented rolling-upgrade window; maintenance/runbook specifies rollback/backup restoration expectations.
- Runtime configuration updates are validate → compile → publish → atomic activate, preserving version correlation. New plugin binaries and replacement assemblies require process restart/redeployment, even if policy *settings* can activate dynamically.
- Containers run non-root with minimum Linux capabilities, read-only root where practicable, explicit write/cache paths and **read-only plugin mounts**. Downloaded plugin artifacts go to bounded, verified staging storage, never executed before lockfile verification.
- Dev-only defaults cannot appear in production manifests. Protect network links to Postgres/Redis/TLS and internal management endpoints.

ISK-0003 rejects development-only values outside the `development` environment, plaintext secrets in secret fields, and every configuration error before anything runs, under its [configuration contract](../specs/0003-configuration-schema-validation-foundation/contracts/configuration.md).

## Database migrations and rollback

This is the operator runbook for the PostgreSQL schema. The [persistence contract](../specs/0010-system-operator-bootstrap-security-baseline/contracts/persistence.md) holds the rules behind it. PostgreSQL 17 is the tested major version, and the oldest one supported.

**Provisioning.** Create three roles before the first migration:

- `iskender_runtime`, a group role with `NOLOGIN` and `NOBYPASSRLS`. It owns nothing. The runner refuses to migrate, with reason `missing_runtime_role`, until it exists.
- A runtime login that is a member of `iskender_runtime`, with `NOSUPERUSER`, `NOBYPASSRLS`, and no `CREATE` on the database. The gateway's data plane connects as this login only, and needs no migration login.
- A migration login with `CREATE` on the database and nothing more. It owns the `isk_<module>` schemas it creates and grants the runtime group their use.

Each password is a secret reference that the host's credential provider resolves on every physical connection. `tlsMode: require` verifies the server certificate chain against the system trust store and the host name against the certificate, which is stricter than libpq's `require`. `disable` is for development only. The gateway never negotiates GSS (Kerberos) encryption, so each connection open makes one physical attempt, and a timed-out open is not retried on a new socket.

**Running migrations.** Migrations run only as an explicit operation, never on start-up. ISK-0007 exposes that operation. It is the control-plane image's `migrate` command, run as a one-shot job: `migrate apply` applies every pending migration, `migrate reverse [--steps <n>] [--confirm-data-loss]` reverses the latest `n`, one by default, and `migrate status` reports the schema state. `apply` and `reverse` exit 0, or 5 on a `persistence.*` failure; `status` exits 0 when current, 6 when behind, 7 when ahead or mismatched, and 5 when it cannot run; a rejected configuration exits 2, and a usage error 64. Each run whose arguments parse writes event 7006 with the operation, outcome, and count, and, for exit codes 2, 3, and 5, the failure's error code ([Migration command](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#migration-command)). Each migration runs in its own transaction, so a script cannot use a statement PostgreSQL refuses inside a transaction, such as `CREATE INDEX CONCURRENTLY` or `VACUUM`. A failed migration rolls back and records nothing, and earlier migrations stay applied. Back up the database before every migration run.

**The migration lock.** One migrator runs at a time. It holds a session advisory lock on the key `6156145740695756181`, and others wait up to `migration.lockWait`, then fail with `persistence.migration_locked`. A crashed holder's lock is released with its connection. To see who holds it:

```sql
SELECT pid, granted FROM pg_locks
WHERE locktype = 'advisory' AND classid = 1433339375 AND objid = 1001676181 AND objsubid = 1;
```

**Reversing.** A reverse request takes one step by default, in the exact reverse of apply order. A step whose reverse script is marked `-- iskender:data-losing` needs explicit confirmation. Without it, the whole request fails with `persistence.reverse_requires_confirmation` and nothing changes. When a reverse step cannot recover the data it drops, restore from the backup instead.

**Rolling back a release.** A release cannot reverse migrations it does not know, because it does not ship their reverse scripts. Reverse the newer migrations with the newer release first, then deploy the older one. Within the compatibility window, migrations are expand-only, so the previous release keeps working against the newer schema. Rolling back below ISK-0009 reverses two steps, `work` and then `directory`, both data-losing, so pass `migrate reverse --steps 2 --confirm-data-loss`; it leaves `coordination`, its leases, and its fence records applied ([Migrations](../specs/0010-system-operator-bootstrap-security-baseline/contracts/persistence.md#migrations)).

**Health reasons.** The store health probe reports one reason. It connects as the runtime login on a pool of its own, holding at most one connection, so a hung probe never takes a connection from real work. When sizing `max_connections`, count `poolMaximum` plus one for each process. ISK-0007 maps reasons onto readiness, and keeps replicas ready during `migration_in_progress`, because a migration in the window is expand-only.

| Reason | Operator action |
|---|---|
| `ok` | None |
| `unreachable` | Check the network path, the server, and TLS |
| `authentication_failed` | Check the runtime login, its secret reference, and the credential provider |
| `timeout` | Check server load and the network |
| `migration_in_progress` | Wait for the migration to finish |
| `schema_behind` | Run migrations |
| `insufficient_privilege` | Fix the runtime login's grants or its membership of `iskender_runtime` |
| `unexpected` | Read event 4014 for the SQLSTATE class |
| `checksum_mismatch` | Restore the shipped script; never edit an applied one |
| `schema_ahead` | Deploy the newer release, or reverse the newer migrations with the newer release |

## Coordination

This is the operator runbook for distributed coordination. The [coordination contract](../specs/0009-module-events-background-work-coordination/contracts/coordination.md) holds the rules behind it.

**Declaring the topology.** Set `coordination.topology` to `single_instance` or `multi_replica`. Left unset, it is `multi_replica`, so a forgotten setting never turns a per-replica limit into the real limit. With PostgreSQL configured, a `single_instance` process holds an operator-scope lease for its plane (`instance.data_plane` or `instance.control_plane`) and renews it every third of `instanceLease.duration`. A second process of the same plane is refused, and a process that loses the lease fails its `single_instance` features until it takes the lease again. Without PostgreSQL, the declaration is accepted and reported as unverified, because only a PostgreSQL lease can show that no other process holds the plane.

**Choosing a coordinator.** Each primitive takes its coordinator from its own section (`atomic`, `leases`, `cache`), and fencing follows `leases`. Each coordinator declares one consistency level per primitive:

| Primitive | `local` | `redis` | `postgresql` |
|---|---|---|---|
| Atomic operations | `local` | `leased` | `strict` |
| Leases | `local` | `leased` | `strict` |
| Fencing | not offered | not offered | `strict` |
| Cache invalidation | `local` | `leased` | `strict` |

The fencing guard accepts only PostgreSQL-issued leases, so a feature with any fencing minimum is refused with `unsupported_consistency` unless `leases` is on PostgreSQL. With PostgreSQL as the cache coordinator, cached values stay in each process's memory, bounded by `cache.maximumEntries`, and only each namespace's generation is shared.

**Refusal reasons.** The capability check gives each feature one result, logged as event 5001 or 5002. Every call through a refused feature fails with `coordination.capability_unsupported` and touches no state. ISK-0007 maps refusals onto start-up and readiness.

| Reason | Operator action |
|---|---|
| `degrade_not_permitted` | A mandatory path cannot degrade; fix the feature's declaration |
| `single_instance_violated` | Another process of this plane holds the instance lease; stop it, or declare `multi_replica` with distributed coordinators |
| `coordinator_not_configured` | Configure the `redis` section or the `persistence` section the primitive names |
| `local_in_multi_replica` | Choose `redis` or `postgresql` for the primitive |
| `unsupported_consistency` | Choose a coordinator that declares the feature's minimum |

**In the containers.** Readiness reads three checks from this module ([Readiness](../specs/0010-system-operator-bootstrap-security-baseline/contracts/container.md#readiness)): `coordination` from the health reasons below, `single_instance` from the instance lease (`held`, `not_applicable`, and `unverified` are ready, and it is `unverified` until the start-up has composed the guard; `single_instance_lost` and `single_instance_held_by_other` are not), and `capability`, ready unless reading the capability report throws (`unexpected`), listing each refused feature without `MandatoryPath`. A refused feature with `MandatoryPath` stops the start with exit code 4 and event 7002 naming the feature, module, and reason; fix the cause from the table above before restarting.

**Health reasons.** The coordination probe checks Redis, then PostgreSQL, for each one a primitive uses, within `healthProbeTimeout`. ISK-0007 maps reasons onto readiness; liveness never calls it.

| Reason | Operator action |
|---|---|
| `ok` | None |
| `not_configured` | Healthy when no primitive uses a distributed coordinator, though `multi_replica` then refuses every feature that needs one; unhealthy when a primitive names a coordinator with no settings, so configure it |
| `unreachable` | Check the network path, the server, and TLS |
| `authentication_failed` | Check the ACL user or runtime login, its secret reference, and the credential provider |
| `timeout` | Check server load and the network |
| `read_only` | Redis answers as a replica; wait for failover to finish, or point at the primary |
| `unexpected` | Read event 5010 |

**Provisioning Redis.** Give the gateway an ACL user with only the key pattern `~isk:v1:*` and the commands `EVALSHA`, `EVAL`, `SCRIPT LOAD`, `PING`, `ROLE`, `HELLO`, `GET`, `SET`, `DEL`, `INCR`, `PEXPIRE`, `PTTL`, `HGET`, `HSET`, and `HMGET`. It never needs `CONFIG`, `KEYS`, `FLUSHALL`, `SELECT`, or any administrative command, and it always uses database 0. Its password is a secret reference the host's credential provider resolves when the connection is built, and again after an authentication failure. Outside `development`, `redis.username` and `redis.password` are required whenever `redis.endpoints` is set, and the gateway refuses the configuration with `config.field.required` otherwise; only development may use an open Redis. `tlsMode: require` verifies the certificate chain against the system trust store and each endpoint's own host name; `disable` is for development only. Run Redis with `maxmemory-policy` set to `noeviction` or a `volatile-*` policy: fencing token counters and namespace generations never expire, and an `allkeys-*` policy could evict one, repeating a token or serving an invalidated entry. The adapter cannot check the policy, because its user has no `CONFIG`. Expect nothing from Redis persistence: a restart or failover can lose acknowledged writes, which is why Redis declares `leased` and offers no fencing.

**The `coordination` migration module.** It creates the schema `isk_coordination`, with `coordination_values`, `leases`, `fences`, `cache_generations`, and the `fencing_tokens` sequence, each table under row-level security on `scope_key`. It runs with the other modules, as an explicit operation. Expired value and lease rows count as absent at once; `PurgeExpiredAsync` deletes them one scope at a time, and ISK-0009 schedules it. Fence records and namespace generations are never purged.

**Known limits.** A Redis failover can lose writes, grants, and token increments, and Redis Cluster and Sentinel management is post-MVP. A rotated Redis password takes effect only when the connection is rebuilt after an authentication failure. The Redis client cannot cancel a command already sent, so a cancelled write's outcome is unknown, and nothing retries it. A cancelled lease acquire is the exception: the adapter releases the lease by its holder, so a grant the server made is not left held. PostgreSQL does the same for a lease acquire whose commit outcome is unknown after the caller's or request's token fired: the acquire fails with `coordination.cancelled` and `outcome = unknown`, and a release by holder, in the scope's owner scope and detached from the request's deadline and token, waits for the in-flight commit to settle. A PostgreSQL server that stops answering is reported as a timeout within `operationTimeout`, but each abandoned call keeps its pooled connection until the driver gives up, which can take `connectionTimeout` or longer, so size `poolMaximum` for a stall. Expiry follows the coordinator's clock, so a clock step moves lease expiry with it, though the fencing guard still compares tokens. Records that never expire grow with the lease and namespace names modules use. No count of keys per scope is set; size Redis memory for the expected load. `Lease.Grant` and `CacheStamp.Of` are public because the coordinators in the infrastructure assemblies call them, so only convention and review keep module code from building a lease or a cache stamp; a forged PostgreSQL lease would pass the fencing guard. Callers outside the process cannot reach them. Write an IPv6 Redis endpoint as `[address]:port`: an unbracketed one is not refused when the configuration is read, and calls then fail with `coordination.unavailable`. Dispose the Redis coordinator only on shutdown, because a dispose during its first connection can leave that connection open.

## Background work

This is the operator runbook for background work: the outbox modules enqueue through, the worker that runs it, and the recurring jobs. The [work contract](../specs/0009-module-events-background-work-coordination/contracts/work.md) holds the rules behind it.

**How it works.** A module enqueues a work item in its own unit of work, so the item commits or rolls back with the change that caused it. The worker finds scopes with due work through the scope directory, takes a PostgreSQL lease on one of the scope's shards, and runs each item's handler in a unit of the item's own scope, fenced by that lease. Each item is delivered at least once, its database effects are applied at most once, and items run in no particular order. A handler's effects outside the database are its own to make idempotent.

**Running the worker.** Run the control-plane image with the command `work`, as its own deployment, with a grace period above `shutdownDelay` + `shutdownTimeout` + 5 seconds, as for the planes ([Container entry points](#container-entry-points)). Give it the control plane's documents without the migration login, which no long-running process needs, and with `coordination.leases.coordinator: postgresql`. You may run several workers: they share the work through the shard leases, and a crashed worker's shards pass to another once its leases expire. Migrations run only through `migrate`, which runs no work.

**Configuration.** The `work` section is optional, and only the worker accepts it; a plane rejects it with `config.namespace.unknown`. Every default is a safety bound, not a performance figure ([Configuration](../specs/0009-module-events-background-work-coordination/contracts/work.md#configuration)):

| Field | Default | Sets |
|---|---|---|
| `pollInterval` | `5s` | How often an idle worker reads the directory |
| `batchSize` | 32 | Directory rows per page, and items per shard claim |
| `leaseDuration` | `30s` | Each shard lease, renewed every third of it |
| `dispatch.shards` | 4 | Shards per scope, a divisor of 64 |
| `dispatch.concurrency`, `dispatch.scopeConcurrency` | 16, 4 | Items in flight per worker, and per scope |
| `handler.timeout` | `30s` | A handler's limit, unless its registration sets one |
| `retry.initialDelay`, `retry.maximumDelay`, `retry.maximumAttempts` | `1s`, `5m`, 8 | Backoff and the attempts before poison |
| `retention.completed`, `retention.poisoned` | `7d`, `30d` | How long finished items are kept |
| `jobs.purgeExpired.*`, `jobs.retention.*` | `5m` and 500, `1h` and 500 | Each shipped job's interval and batch |

A value out of range, or a cross-field check such as `leaseDuration` outside the lease limits, fails the start with `config.field.out_of_range`, exit 2.

**Readiness and exit codes.** The worker answers the planes' five readiness checks. Before ISK-0021 it reports persistence and coordination `authentication_failed` and dispatches nothing. A worker whose leases are `redis` or `local` is refused for `work.dispatch` with `unsupported_consistency`, and exits 4 with event 7002; set its leases to `postgresql`. Its other exit codes are the planes': 0 on a clean stop, 1, 2, and 3 as above.

**Retry and poison.** A failed attempt is classified. A permanent failure, such as `work.payload_invalid`, poisons the item at once. Anything else, including a handler timeout (`work.handler_timeout`) or a crash after the attempt started (`work.attempt_abandoned`), retries with jittered exponential backoff until the kind's attempt limit, then poisons it. An unreachable database counts no attempt, so an outage never poisons work. A poisoned item keeps its last failure's class and code, is never delivered again, and never delays another item. Until a management API exists, find poisoned items in `isk_work.items` with state `poisoned`; their payloads stay in the database only.

**Stopping.** At the stop signal the worker claims nothing more and starts no job. In-flight items may finish until `shutdownDelay` + `shutdownTimeout`; then they are cancelled, roll back, and stay `pending` as `work.handler_cancelled`. The worker releases its leases so another can take the items at once, and exits 0.

**The jobs.** `coordination.purge_expired`, every `5m`, deletes expired coordination values and leases one scope at a time, never a fence record or a namespace generation. `work.retention`, every `1h`, deletes completed and poisoned items past their retention. Each run holds a PostgreSQL lease, so no two workers run one job at once, and a run interrupted by a crash resumes at its saved cursor.

**The dedupe window.** An enqueue with the same scope, kind, and idempotency key as a retained item creates nothing and returns that item. Once retention deletes the item, the key enqueues a new one, so the window is `retention.completed` for a completed item and `retention.poisoned` for a poisoned one. Size retention to cover the longest time a producer may repeat a key.

**Version skew.** During a rolling upgrade, an item of a kind no running worker handles, or whose payload version is above its handler's highest, stays `pending` with no attempt counted, and is reported by `iskender.work.items.skewed` and event 8008. A worker that handles it takes it on its next pass.

**Diagnostics.** Events 8001 to 8012 are written under the category `Iskender.Work`, with only module, kind, job, outcome, failure class, error code, and attempt: never a scope ID, item ID, idempotency key, payload, or configuration value. The containers export the `Iskender.Work` activities and metrics with the others ([Diagnostics](../specs/0009-module-events-background-work-coordination/contracts/work.md#diagnostics)). Watch for:

| Signal | Means | Action |
|---|---|---|
| 8007, `iskender.work.items.poisoned` | An item was poisoned | Read its class and code; fix the handler or the producer |
| 8008 `appeared`, `iskender.work.items.skewed` | Items wait for a newer worker | Finish the rollout |
| 8009 | A malformed scope directory row was skipped | Report it; the directory is written only by Iskender |
| 8010 | An apply was discarded: `fencing_rejected` or `superseded` by another holder, or `lease_lost` | None if rare; a steady rate means workers stall past `leaseDuration` |
| 8012 | A pass failed before any item, such as an unreachable database | As the [Database migrations](#database-migrations-and-rollback) health reasons say |
| `iskender.work.backlog` | Due items this worker can take, as of its last pass | Add workers, or raise `dispatch.concurrency`, if it keeps growing |

Each dispatch is a `work.dispatch` span linked to, not parented by, the trace that enqueued it.

**Sizing the pool.** Each in-flight item holds one pooled connection while one of its units is open, and polls, lease operations, and jobs need more, so keep `persistence.poolMaximum` above `dispatch.concurrency`. The defaults, 20 and 16, do. A pool that runs out shows as `persistence.timeout`, a transient failure, never as lost work.

**Rolling back.** Below ISK-0009, reverse two steps with the newer release, `work` and then `directory`, both data-losing: `migrate reverse --steps 2 --confirm-data-loss`. It drops every work item and directory row, and leaves `coordination` applied, with its leases and fence records. Expiring coordination rows from before the upgrade have no directory row, so the purge job reaches them only after their scope writes a new one; they already count as absent, so this costs space, not correctness, and a rollback and a later upgrade leave them the same ([Known limits](../specs/0009-module-events-background-work-coordination/contracts/coordination.md#known-limits)).

**Known limits.** The delay from commit to dispatch is up to one `pollInterval`, plus backoff, and no latency figure is asserted. An item that crashes its worker stalls its shard until the shard's lease expires; the scope's other shards keep running. There is no ordering by scope, kind, or key. A scope whose only due items are skewed is visited every poll. Before ISK-0021, no process can connect to PostgreSQL, so the worker dispatches nothing ([Known limits](../specs/0009-module-events-background-work-coordination/contracts/work.md#known-limits)).

## CI and supply chain

GitHub Actions exercises format/static analysis, architecture/unit tests, integration Testcontainers, provider conformance fixtures, SBOM/dependency review, vulnerability checks and controlled performance benchmarks. Publish signed/provenance-bearing release artifacts as policy permits; checksum images/packages and pin dependencies. The repository includes reproducible build instructions, lockfiles or centrally pinned versions where appropriate, branch rules and secret-free CI configuration. No assumption settings are already configured in an unseen GitHub repository.

`.github/scripts/check-license.sh` (CI check 6b) reads every package that a project under `src/` resolves, directly or transitively, from its `packages.lock.json`, and passes it only when the restored package's `.nuspec` declares one of the SPDX license expressions `MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, or `PostgreSQL`. A package with another expression, a license file instead of an expression, or no restored `.nuspec` fails the check. A spec that adds such a package extends the list in that script under Product/Charter owner review, as ISK-0004 did for `PostgreSQL`, the license of Npgsql.

## Capacity and performance

Instrument serialization/translation, policy CEL evaluation, token accounting, storage waits, upstream HTTP and proxy copies separately. Benchmark long-lived SSE with backpressure, large parallel request counts and plugin handler cost in both embedded and container hosting. Optimize allocations/caching after obtaining traces and baseline. A future Rust module should cross a deliberately narrow FFI boundary for a proven hot path; Rust use does not bypass .NET validation/policy ownership. See [12](12-observability-and-nonfunctional-requirements.md).

## Questions reserved for implementation review

Select exact relational mapper, CEL runtime package, migration strategy, Redis client and secure secret provider based on tests and licensing. Their use is not preapproved simply by mention. Document chosen variants in approved ADRs [16](16-architecture-decision-register.md) and keep the corresponding spec acceptance contract intact.
