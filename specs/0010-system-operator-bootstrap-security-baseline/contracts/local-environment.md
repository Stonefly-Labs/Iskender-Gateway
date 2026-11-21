# Local Environment Contract

**Version**: 3

**Supersedes**: version 2, `specs/0009-module-events-background-work-coordination/contracts/local-environment.md`, which stays unedited as a completed record, as does version 1, `specs/0008-local-developer-environment-reproducible-compose/contracts/local-environment.md`.

**Owning spec**: ISK-0008 — Local Developer Environment & Reproducible Compose. Version 2 revised by ISK-0009 — Module Events & Background Work Coordination. Version 3 revised by ISK-0010 — System-Operator Bootstrap & Security Baseline

**Review owner**: Architecture (ADR) owner, with the Security owner for Passwords, Ports, and Environment safety, and the Privacy/Operations owner for Scripts, Smoke assertions, and Reset. See `CONTRIBUTING.md` for who holds each role.

This contract states what developers, the contributor guide, the acceptance test, and later specs may rely on from the developer topology under `local/`. The reasons are in ISK-0008's [research.md](../../0008-local-developer-environment-reproducible-compose/research.md), and the entities in its [data-model.md](../../0008-local-developer-environment-reproducible-compose/data-model.md). Version 2's reasons are in ISK-0009's [research.md](../../0009-module-events-background-work-coordination/research.md), and version 3's in ISK-0010's [research.md](../research.md), R20. The topology runs the two images of the [container contract](container.md), version 3, the control-plane image also as the worker and the bootstrap job, and changes none of its rules, nor any rule of the persistence, coordination, configuration, hosting, work, or [operator-security](operator-security.md) contracts.

## Changing this contract

A change to a rule here is made by a later spec's revision of this contract, which increments the version, publishes the contract in that spec's `contracts/` directory, repoints the link in `docs/04-technology-stack-and-deployment.md`, and records the change in this table. Renaming or removing a script, option, exit code, service, mode, profile, variable, or port default breaks every developer and test that uses it. A change to an expected smoke state, such as ISK-0021 making the planes ready, is a revision.

| Version | Spec | Change | Approval | Owner |
|---|---|---|---|---|
| 1 | ISK-0008 | First version | ISK-0008's PR | Architecture (ADR) owner |
| 2 | ISK-0009 | Adds the `worker` service, running the control-plane image with `work` and its own documents, whose overlay selects `postgresql` for leases (Services, Modes, Hardening, Documents, Native runs), and its expected smoke state (Smoke assertions 1 to 3 and 7). The data-plane and control-plane smoke answers are unchanged byte for byte | ISK-0009's PR, maintainer decision in `specify` on 2026-11-17 | Architecture (ADR) owner |
| 3 | ISK-0010 | Publishes the control plane's traffic port on `127.0.0.1` (Modes, Ports), adds the `bootstrap` service and the bootstrap script (Services, Hardening, Scripts, Exit codes), declares each plane's traffic mode, `upstream` in its base and `none` in its development overlay (Documents 2, 3), forbids a tracked token (Documents 6), and adds the admin surface's fail-closed smoke state (Smoke assertions 7 to 10). The three readiness answers are unchanged byte for byte | ISK-0010's PR, maintainer decisions in `plan` on 2026-11-21 | Privacy/Operations owner, Security owner for Ports |

## Layout

*Review owner: Architecture (ADR) owner.*

1. The topology is `local/` at the repository root. It holds `compose.yaml`, `compose.dependencies.yaml`, `config/<plane>/`, `native/<plane>/`, `postgres/initdb/`, `scripts/`, and `README.md`, where `<plane>` is `data-plane`, `control-plane`, or, from version 2, `worker`.
2. It shares no file, service definition, or document with `tests/Iskender.Container.Tests/compose/`, and neither includes the other.
3. `local/.secrets/` is ignored by Git (`.gitignore`) and by Docker (`.dockerignore` excludes `local/`).
4. Every file under `local/` is stored with LF endings. Every script is tracked with mode `100755`.
5. The topology adds no entry point, executable, Dockerfile, or image definition, and changes neither ISK-0007 Dockerfile.

## Project name

*Review owner: Security owner.*

1. The Compose project name is `ISKENDER_LOCAL_PROJECT`, or `iskender-local` when it is unset or empty.
2. A value other than `iskender-local` or `iskender-local-` followed by 1 to 24 characters of `a-z` and `0-9` makes every script exit 64 before anything runs. ISK-0007's project names (`isk-` and 12 hexadecimal characters) can never match.
3. `compose.yaml` sets `name: ${ISKENDER_LOCAL_PROJECT:-iskender-local}`, and every script passes `--project-name`. Every Compose command passes both Compose files, except the default mode's `up`, which passes only `compose.yaml`, so that the `dependencies` mode's ports never apply in the default mode (Modes rule 1).
4. One project per name. Several projects from one checkout at once are supported only under different names and ports. The name is per Docker host, not per checkout: two checkouts or worktrees on one host under the same name are one project, and a reset from either removes the other's data. Each checkout that runs its own topology sets its own `ISKENDER_LOCAL_PROJECT`.

## Services

*Review owner: Architecture (ADR) owner.*

| Service | Image | Started by | Depends on |
|---|---|---|---|
| `postgres` | `postgres:17-bookworm@sha256:665dc7b37ac52a17e298511414c21d23ffa4e6a6ef6225b3e5d59c7afb35e994` | Default, `dependencies` | |
| `valkey` | `valkey/valkey:8.1.11@sha256:8f836008e0986c36adf50da714c7060cf689dbb335f61fe077939d90fa650809` | Default, `dependencies` | |
| `data-plane` | `<project>/data-plane:local`, built by start | Default | `postgres`, `valkey` healthy |
| `control-plane` | `<project>/control-plane:local`, built by start | Default | `postgres`, `valkey` healthy |
| `worker` | `<project>/control-plane:local`, with `command: ["work"]` | Default | `postgres`, `valkey` healthy |
| `migrate` | `<project>/control-plane:local` | Profile `migrate`, through the migration script | `postgres` healthy |
| `bootstrap` | `<project>/control-plane:local` | Profile `bootstrap`, through the bootstrap script, and removed when it exits | `postgres` healthy |
| `probe` | `curlimages/curl:8.17.0@sha256:935d9100e9ba842cdb060de42472c7ca90cfe9a7c96e4dacb55e79e560b3ff40` | Profile `probe`, through the smoke script | |

1. A digest changes only with the ISK-0004 or ISK-0005 fixture it matches, or for `probe` by itself, through a revision of this table.
2. The plane tags are built by the start script from `src/Iskender.Gateway.Host/Dockerfile` and `src/Iskender.ControlPlane.Host/Dockerfile`, from the repository root, with `REVISION` set to `git rev-parse HEAD`. Compose services use `pull_policy: never` and build nothing.
3. No plane or worker depends on another, on `migrate`, or on `bootstrap`. No service runs `migrate` unless the migration script runs it, and none runs `bootstrap` unless the bootstrap script runs it.
4. The default start is `docs/04` topology 1's all-in-one developer distribution: both unchanged plane images, each its own process in its own container, with their dependencies. It is not one container, because the planes must run as separate processes (container contract, Entry points rule 3), one container running two processes needs a process supervisor as its entry point, which would be a new entry point, and the chiseled runtime image has no shell, init, or supervisor and may not gain a shell or package manager (Images rule 2). The optional UI is left out until a management UI spec adds it. **This settles the container contract's AV-6.**

## Modes

*Review owner: Architecture (ADR) owner.*

| Mode | Started by | Services | Published |
|---|---|---|---|
| Default | `start.sh` | `postgres`, `valkey`, `data-plane`, `control-plane`, `worker` | The data plane's and, from version 3, the control plane's traffic ports |
| `dependencies` | `start.sh --dependencies` | `postgres`, `valkey` | `postgres` and `valkey` |

1. The `dependencies` mode layers `compose.dependencies.yaml`, which adds only `ports` to `postgres` and `valkey`, and starts those two by name. It stops `data-plane`, `control-plane`, and `worker` first if they run, and builds no plane image. A Compose profile cannot express it, because a profile only adds services.
2. A start in either mode after the other reuses the same volumes.

## Hardening

*Review owner: Security owner.*

1. `data-plane`, `control-plane`, `worker`, `migrate`, `bootstrap`, and `probe` run with `read_only: true`, `cap_drop: [ALL]`, and `no-new-privileges:true`, and set no `user`, `privileged`, `network_mode`, or `cap_add`. The planes, `worker`, `migrate`, and `bootstrap` also have a `tmpfs` at `/tmp`.
2. `data-plane`, `control-plane`, and `worker` have `stop_grace_period: 40s`, above the defaults' `shutdownDelay` + `shutdownTimeout` + 5 s = 35 s. A document that raises either timing raises the grace period with it.
3. Each plane's health check, and the worker's with the control plane's `<host>`, is the exec-form `["CMD", "dotnet", "/app/<host>.dll", "health", "live"]`, every 2 s, with a 35-second timeout.
4. `postgres` and `valkey` keep their images' own users and entrypoints.

## Ports

*Review owner: Security owner.*

| Service | Container port | Host address | Default host port | Override variable | Mode |
|---|---|---|---|---|---|
| `data-plane` | 8080 | `127.0.0.1` | 18080 | `ISKENDER_LOCAL_DATA_PLANE_PORT` | Default |
| `control-plane` | 8080 | `127.0.0.1` | 18090 | `ISKENDER_LOCAL_CONTROL_PLANE_PORT` | Default |
| `postgres` | 5432 | `127.0.0.1` | 15432 | `ISKENDER_LOCAL_POSTGRES_PORT` | `dependencies` |
| `valkey` | 6379 | `127.0.0.1` | 16379 | `ISKENDER_LOCAL_VALKEY_PORT` | `dependencies` |

1. Nothing else is published in any mode. No health port is ever published, and the worker publishes nothing. From version 3, the control plane publishes only its traffic port, which carries the admin surface of the operator-security contract.
2. An override must be an integer from 1,024 to 65,535, or the script exits 64.
3. Before starting, the start script exits 2 when a port it will publish already answers on `127.0.0.1`, naming the override variable. A port that this project's own service already publishes, as `docker compose port` reports it, does not count, so a start against a running topology succeeds.

## Volumes

*Review owner: Privacy/Operations owner.*

| Volume | Mounted at | Kept by stop | Removed by reset |
|---|---|---|---|
| `postgres-data` | `postgres:/var/lib/postgresql/data` | Yes | Yes |
| `valkey-data` | `valkey:/data` | Yes | Yes |

Compose names them `<project>_postgres-data` and `<project>_valkey-data`.

## PostgreSQL

*Review owner: Security owner.*

1. The database is `iskender_local`. On an empty `postgres-data` volume, `postgres/initdb/10-provision.sh` creates exactly:

   | Role | Attributes | On `iskender_local` |
   |---|---|---|
   | `iskender_runtime` | `NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE` | Nothing |
   | `iskender_local_runtime` | `LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE`, member of `iskender_runtime` | `CONNECT` |
   | `iskender_local_migrator` | `LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE` | `CONNECT`, `CREATE` |

2. It creates no schema, table, or row, and runs no migration. Passwords reach `psql` from its environment through `\getenv`, never on a command line or in SQL text. The session sets `VERBOSITY terse` and `log_min_error_statement` to `panic`, so a failed statement is never echoed with its text to the container's output or the server log.
3. The health check is `pg_isready -h 127.0.0.1 -U postgres -d iskender_local`, which passes only after initialisation.

## Passwords

*Review owner: Security owner.*

1. The password file is `local/.secrets/<project>.env`, holding `POSTGRES_PASSWORD`, `ISKENDER_LOCAL_RUNTIME_PASSWORD`, and `ISKENDER_LOCAL_MIGRATION_PASSWORD`, each 32 characters of `A-Za-z0-9` from `/dev/urandom`. The directory is mode `0700` and the file `0600`.
2. The start script writes it only when both it and the `postgres-data` volume are absent. When the volume exists and the file does not, start exits 2 and names the reset script. When the file exists but does not hold exactly those three names, each with a value of 32 characters of `A-Za-z0-9`, start exits 2, names the reset script, and changes nothing. Every start sets the directory to `0700` and the file to `0600` again.
3. Only `postgres` reads it, as an `env_file` with `required: false`.
4. No script, document, log, readiness answer, or script output holds a password. The documents name the passwords only as the secret references `local/postgres-runtime` and `local/postgres-migration`, which no credential provider resolves before ISK-0021.

## Valkey

*Review owner: Architecture (ADR) owner.*

`valkey-server --maxmemory-policy noeviction --appendonly yes --appendfsync everysec --protected-mode no`, with no password, ACL user, or TLS, which only `development` permits (coordination contract, Credentials rule 2). Health check: `valkey-cli ping`.

## Documents and environment safety

*Review owner: Security owner.*

1. `config/<plane>/` is mounted read-only at `/etc/iskender` in that plane's service, `config/worker/` in `worker`, and `config/control-plane/` in `migrate` and `bootstrap`. Each plane service, `worker`, `migrate`, and `bootstrap` sets `ISKENDER_ENVIRONMENT=development` itself, so the developer's shell value never reaches a container, and no other Iskender variable is passed in.
2. Each base `iskender.yaml` holds no development-only value, and validates alone with no environment name:

   | Field | Data plane | Control plane | Worker |
   |---|---|---|---|
   | `hosting.requestTimeout` | `100s` | Absent | Absent |
   | `container.trafficTransport` | `upstream` | `upstream` | Absent |
   | `persistence.host` | `postgres` | `postgres` | `postgres` |
   | `persistence.database` | `iskender_local` | `iskender_local` | `iskender_local` |
   | `persistence.username` | `iskender_local_runtime` | `iskender_local_runtime` | `iskender_local_runtime` |
   | `persistence.password.secretRef` | `local/postgres-runtime` | `local/postgres-runtime` | `local/postgres-runtime` |
   | `persistence.migration.username` | Absent | `iskender_local_migrator` | Absent |
   | `persistence.migration.password.secretRef` | Absent | `local/postgres-migration` | Absent |

   The worker holds no migration login, since no long-running process needs one (container contract, Start-up rule 5), and no `work` section, so it runs on the work contract's defaults.

   From version 3, each plane's base declares `container.trafficTransport: upstream`, a mode valid outside `development`, so it still validates alone with no environment name, where the default `none` would be refused with `operator.transport_unprotected` (container contract, The `container` section). The worker's documents are unchanged and hold no transport mode, since the worker binds no traffic listener and ignores it.

3. Each overlay `iskender.development.yaml` holds every development-only value and the Redis selection: `persistence.tlsMode: disable`, `coordination.atomic.coordinator`, `leases.coordinator`, and `cache.coordinator` `redis`, `coordination.redis.endpoints: [valkey:6379]`, and `coordination.redis.tlsMode: disable`. It sets no `coordination.topology`, so `multi_replica`, and no Redis user or password. From version 3, each plane's overlay also sets `container.trafficTransport: none`, which only `development` permits, so that the documents state the topology's loopback-only plain HTTP openly rather than inherit it from a default. The worker's overlay is the control plane's with `coordination.leases.coordinator` `postgresql` instead and no `container` section, so that its dispatcher's `strict` lease and fencing minimums are met and it is not refused (work contract, Leases and fencing rule 2). Its atomic operations and cache stay `redis`.
4. Precedence is ISK-0003's: defaults, base, overlay, `ISKENDER__` variables. There is no further layer. **This settles the configuration contract's AV-6 for local use.**
5. The control plane rejects the data-plane documents with `config.namespace.unknown`, since they name `hosting`. The control plane's only section of its own, `operator` from version 3, is absent from its documents, so the data plane would accept the control-plane documents. They are kept separate so the data plane never holds the migration login's references. The worker rejects the data-plane documents too, and the planes would accept the worker's, which hold no `work` section. They are kept separate so the worker's lease selection never reaches the planes, which would change their readiness answers.
6. No tracked file under `local/` holds a password, token, or credential value. From version 3, no tracked file holds a bootstrap, recovery, or enrollment token, or a fixed value usable as one. A token exists only when the bootstrap command issues it, or when the smoke script generates one at run time (Smoke assertions 8).

## Native runs

*Review owner: Architecture (ADR) owner.*

1. `native/<plane>/` is the matching `config/<plane>/` pair with only these values changed: `persistence.host: 127.0.0.1`, `persistence.port: 15432`, the Redis endpoint `127.0.0.1:16379`, and, for the control plane only, `container.trafficPort: 8090` and `container.healthPort: 8091`, and for the worker only, `container.trafficPort: 8092` and `container.healthPort: 8093`. A native worker runs the control-plane host with the argument `work`.
2. A host runs natively against the `dependencies` mode with `ISKENDER_CONFIG_DIRECTORY` naming `local/native/<plane>` and `ISKENDER_ENVIRONMENT=development`. Its readiness reports the same checks and reasons as the containerized plane.
3. `ISKENDER__PERSISTENCE__PORT` follows an overridden PostgreSQL port. A Redis endpoint cannot be set by a variable, because `redis.endpoints` is an array (configuration contract, Environment variables rule 3), so an overridden Valkey port needs an edited copy of the native directory.
4. A native host binds both its listeners on all interfaces (container contract, Listeners rule 1), unlike the topology's loopback ports.

## Scripts

*Review owner: Privacy/Operations owner.*

Each script is `local/scripts/<name>.sh`, runs under `bash` 3.2 or later, works from any directory inside the checkout, prints usage to standard output for `--help` and exits 0, and writes only value-free messages, to standard error. Each finds the repository root from its own path with `git rev-parse --show-toplevel`, and exits 2 when that root has no `Iskender.slnx` and `local/compose.yaml`.

| Script | Options | Does |
|---|---|---|
| `start.sh` | `--dependencies` | Checks the prerequisites and ports, from version 3 the control plane's traffic port included, writes the password file when Passwords rule 2 allows it, builds both plane images unless `--dependencies`, and runs `docker compose up --detach --wait --wait-timeout 180`. Exits 0 when every started service is healthy: `postgres` and `valkey`, and in the default mode each plane's and the worker's `health live` |
| `stop.sh` | | `docker compose stop --timeout 40`. Keeps every volume and the password file |
| `reset.sh` | `--yes` | After confirmation, or with `--yes`, `docker compose down --volumes --remove-orphans --timeout 40`, then removes the password file. Removes no image, and nothing of another project or outside `local/`. An answer other than `y` or `Y` at the prompt removes nothing and exits 0 |
| `migrate.sh` | `apply`, `reverse [--steps <n>] [--confirm-data-loss]`, `status` (default) | Runs the `migrate` service once with the operation, with `--no-deps`, so it never starts or recreates `postgres`, and exits with its exit code |
| `bootstrap.sh` | `issue`, `recover` | From version 3, runs `docker compose run --rm --no-deps bootstrap bootstrap <operation>` once, so it never starts or recreates `postgres` and its container is removed when it exits, and exits with its exit code. The command prints a token it issues once, to standard output, so the token appears only in the developer's own terminal. The script writes it to no file and nowhere else. There is no default operation |
| `smoke.sh` | | Asserts the Smoke assertions below against the running default topology |

Prerequisites: `git`; `docker`; a running container runtime; Docker Compose 2.24.0 or later, with Docker Engine 24.0 or later. Windows runs the scripts in WSL 2.

## Exit codes

*Review owner: Privacy/Operations owner.*

| Code | Meaning |
|---|---|
| 0 | Success. For smoke, every assertion passed |
| 1 | A smoke assertion failed, an image build or a Compose step failed, or a service exited or was not healthy within start's 180-second bound. The message names the service and, if it exited, its exit code |
| 2 | A prerequisite is missing or unusable: no `git`, `docker`, or Compose, the runtime not running, Compose older than 2.24.0, a script outside a checkout, a published port already in use, a `postgres-data` volume without its password file, a password file that Passwords rule 2 rejects, or, for the migration or bootstrap script, no control-plane image |
| 64 | A usage error: an unknown option or operation, a missing bootstrap operation, an invalid project name or port, or a reset in a non-interactive shell without `--yes` |

The migration script exits with the `migrate` command's own code (container contract, Command line and exit codes 2), except that it exits 2 or 64 itself before starting the job. From version 3, the bootstrap script likewise exits with the `bootstrap` command's own code (container contract, Command line and exit codes), such as 8 when `issue` finds the installation already initialized, 9 when `recover` finds it not initialized, and 5 when persistence is unavailable, except that it exits 2 or 64 itself before starting the job. Each failure names the check or prerequisite, and no value.

## Smoke assertions

*Review owner: Privacy/Operations owner.*

Valid before ISK-0021, against the default mode. In the `dependencies` mode the planes are not running, so assertion 1 fails and smoke exits 1. The smoke script runs every check and exits 1 if any fails, naming each failure. Version 3 keeps every assertion of version 2 and changes none of them, the three readiness bodies of assertion 3 included byte for byte, except that it extends assertion 7 to the generated token and adds assertions 8 to 10:

1. `postgres`, `valkey`, `data-plane`, `control-plane`, and `worker` are running. Each one that is not is named.
2. Each plane's and the worker's `health live` command, run with `docker compose exec`, exits 0.
3. Each plane's `/health/ready`, read through `probe` at `http://<plane>:8081/health/ready`, answers `503` with exactly this body, after waiting up to 60 s while `lifecycle` is `starting`:

   ```json
   {"version":1,"status":"not_ready","checks":[{"name":"lifecycle","status":"ready","reason":"started"},{"name":"persistence","status":"not_ready","reason":"authentication_failed"},{"name":"coordination","status":"ready","reason":"ok"},{"name":"single_instance","status":"ready","reason":"not_applicable"},{"name":"capability","status":"ready","reason":"none_refused"}],"refused":[]}
   ```

   Any other answer fails, a persistence reason of `ok` included, and the failure names each check whose reason differs and the reason found.

   From version 2, the worker's `/health/ready`, read through `probe` at `http://worker:8081/health/ready` with the same wait, answers `503` with exactly this body, in which only `coordination` differs from the planes' answer, because its PostgreSQL lease coordinator cannot connect before ISK-0021:

   ```json
   {"version":1,"status":"not_ready","checks":[{"name":"lifecycle","status":"ready","reason":"started"},{"name":"persistence","status":"not_ready","reason":"authentication_failed"},{"name":"coordination","status":"not_ready","reason":"authentication_failed"},{"name":"single_instance","status":"ready","reason":"not_applicable"},{"name":"capability","status":"ready","reason":"none_refused"}],"refused":[]}
   ```

   A first run in `implement` (T027) found both planes answering `unverified`: under `multi_replica` the coordination start-up never started the single-instance guard, and readiness read the guard's initial state, contrary to the container contract's Readiness rule 5, which allows `unverified` only while `lifecycle` is `starting`. On 2026-11-17 the maintainer approved fixing this in ISK-0008 (T044): `CoordinationStartup` now marks the guard not applicable under `multi_replica`. No contract rule changes.
4. `migrate.sh status` exits 5, and the job writes one event 7006 naming the operation `status` and the code `persistence.unavailable`.
5. `postgres` holds the roles and grants of PostgreSQL rule 1 and no other role except the image's superuser `postgres` and PostgreSQL's predefined `pg_*` roles, and no schema outside `pg_catalog`, `information_schema`, `pg_toast`, and `public`, with no relation in `public`.
6. `valkey` reports `maxmemory-policy` `noeviction`.
7. No plane or worker log, readiness answer, or smoke output holds a password, the database name, a login name, or a secret reference name, or, from version 3, the text of the token generated for assertion 8. The values searched for reach the search through a pattern file descriptor, never as a command-line argument. The output of assertions 8 to 10 is part of the smoke output searched.
8. From version 3, after assertion 3's wait for the control plane, `POST /admin/v1/bootstrap`, read through `probe` at `http://control-plane:8080/admin/v1/bootstrap` with `Content-Type: application/json` and the body `{"token":"<t>"}`, answers `503` with an ISK-0002 `error-envelope` whose code is `operator.state_unavailable`. `<t>` is a well-formed bootstrap token of the operator-security contract's shape, ISK-0010's research R4: `iskb_`, 26 characters of lowercase Crockford base32, `_`, and 43 characters of unpadded base64url, generated from `/dev/urandom` at each run and never a fixed value. It is held only in a shell variable and reaches `probe` on its standard input, never as a command-line argument. The redemption reaches the store, which it cannot read before ISK-0021, and a single redemption per run stays within the default attempt limit.
9. From version 3, `GET /admin/v1/installation`, read the same way at `http://control-plane:8080/admin/v1/installation` with no credential, answers the same: `503` with an `error-envelope` whose code is `operator.state_unavailable`, because the gate cannot read the installation state.
10. From version 3, `bootstrap.sh issue` exits 5, writes nothing to standard output, and the job writes one event 7008 naming the operation `issue` and the code `persistence.unavailable`.

## Test fixtures

*Review owner: Performance owner (+ QA).*

1. `tests/Iskender.Container.Tests/LocalEnvironment/` holds the acceptance tests. They run in CI check 4 and add no check.
2. Each runs in a clean copy of the tracked files only, made into its own Git checkout, under a project name `iskender-local-t<12 hex>` and free ports, and removes its project and image tags when it ends. A native host run by a test also gets free listener ports, set in its temporary copy of the native documents.
3. Tests that need a container runtime, `bash`, or `git` follow ISK-0005's rule: skipped with their reason without one, and failing when `CI` is `true`.
4. The acceptance run is 5 consecutive cycles of start, smoke, and reset in one clean copy, with the worker running and its log holding no event 5002 or 7002, each exiting 0, with no project container, network, or volume left after each reset.

## Threat model and known limits

### What the rules defend against

The Negative Security and Failure Scenarios tables in ISK-0008's [spec.md](../../0008-local-developer-environment-reproducible-compose/spec.md), ISK-0009's [spec.md](../../0009-module-events-background-work-coordination/spec.md), and ISK-0010's [spec.md](../spec.md) list each threat. These rules implement them: development-only values outside `development` (Documents 2, 3), passwords committed or printed (Passwords), an interim credential making the topology look ready (Smoke assertions 3), the database, Redis, or a health endpoint reachable from another machine (Ports), relaxed image hardening (Hardening 1), a reset reaching another project (Project name 2, Scripts), a password regenerated for a provisioned volume (Passwords 2), Redis eviction (Valkey), migration at start (Services 3), a moving image (Services 1), the ISK-0007 fixture and the topology drifting together (Layout 2), and smoke passing on a changed state (Smoke assertions 3). From version 3, they also defend against the control plane's admin surface reachable from another machine, since its traffic port is published on `127.0.0.1` only (Ports), plain HTTP left implicit, since each plane's documents state their transport mode (Documents 2, 3), a token committed, fabricated, or printed, since a token is generated at run time and never tracked (Documents 6, Scripts, Smoke assertions 7, 8), and the admin surface failing open before ISK-0021 (Smoke assertions 8 to 10).

### Known limits

- **Not ready before ISK-0021**: no credential provider exists, so both planes and the worker report persistence `authentication_failed`, the worker also reports coordination `authentication_failed`, it dispatches nothing, and every `migrate` operation exits 5. ISK-0021 changes the expected smoke state through a revision of this contract (ISK-0008's spec AV-6).
- **No developer bootstrap before ISK-0021**: for the same reason, `bootstrap.sh issue` and `recover` exit 5, and a redemption and the installation route answer `503`, so the bootstrap cannot complete in the developer topology (ISK-0010's spec AV-5).
- **Plain HTTP on the control plane's published port**: its traffic mode is `none` in `development`, so a token redeemed there crosses loopback unencrypted.
- **Passwords in the `postgres` environment**: `docker inspect` shows them to a user who can run Docker commands, who can already read the password file.
- **Native hosts listen on all interfaces**, and a native Valkey port override needs an edited document.
- **One project name per Docker host**: two checkouts under the same name share one project (Project name rule 4).
- **No TLS between local services.**
- **One architecture**: the plane images build for the workstation's own architecture.
- **Building needs the network**: the image build restores packages from nuget.org, and the first start pulls the pinned images.

## Left to later specs

| Item | Spec |
|---|---|
| Credential providers, after which the smoke state becomes ready | ISK-0021 (ISK-0008's spec AV-6) |
| The end-to-end developer bootstrap once credential providers exist, with the expected smoke states of Smoke assertions 8 to 10 changing through a revision of this contract | ISK-0021 (ISK-0010's spec AV-5) |
| The optional management UI service of topology 1 | The management UI spec (ISK-0008's spec AV-8) |
| A single all-in-one image with a supervisor, a local Kubernetes topology, native Windows scripts, a local observability stack, TLS between local services, seed or sample data | Post-MVP |
| Hot reload of configuration | ISK-0057 |
