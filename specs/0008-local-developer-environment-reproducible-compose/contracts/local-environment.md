# Local Environment Contract

**Version**: 1

**Owning spec**: ISK-0008 — Local Developer Environment & Reproducible Compose

**Review owner**: Architecture (ADR) owner, with the Security owner for Passwords, Ports, and Environment safety, and the Privacy/Operations owner for Scripts, Smoke assertions, and Reset. See `CONTRIBUTING.md` for who holds each role.

This contract states what developers, the contributor guide, the acceptance test, and later specs may rely on from the developer topology under `local/`. The reasons are in [research.md](../research.md), and the entities in [data-model.md](../data-model.md). The topology runs the two images of the [container contract](../../0007-container-entry-points-process-separation/contracts/container.md), version 1, and changes none of its rules, nor any rule of the persistence, coordination, configuration, or hosting contracts.

## Changing this contract

A change to a rule here is made by a later spec's revision of this contract, which increments the version, publishes the contract in that spec's `contracts/` directory, repoints the link in `docs/04-technology-stack-and-deployment.md`, and records the change in this table. Renaming or removing a script, option, exit code, service, mode, profile, variable, or port default breaks every developer and test that uses it. A change to an expected smoke state, such as ISK-0021 making the planes ready, is a revision.

| Version | Spec | Change | Approval | Owner |
|---|---|---|---|---|
| 1 | ISK-0008 | First version | This spec's PR | Architecture (ADR) owner |

## Layout

*Review owner: Architecture (ADR) owner.*

1. The topology is `local/` at the repository root. It holds `compose.yaml`, `compose.dependencies.yaml`, `config/<plane>/`, `native/<plane>/`, `postgres/initdb/`, `scripts/`, and `README.md`, where `<plane>` is `data-plane` or `control-plane`.
2. It shares no file, service definition, or document with `tests/Iskender.Container.Tests/compose/`, and neither includes the other.
3. `local/.secrets/` is ignored by Git (`.gitignore`) and by Docker (`.dockerignore` excludes `local/`).
4. Every file under `local/` is stored with LF endings. Every script is tracked with mode `100755`.
5. The topology adds no entry point, executable, Dockerfile, or image definition, and changes neither ISK-0007 Dockerfile.

## Project name

*Review owner: Security owner.*

1. The Compose project name is `ISKENDER_LOCAL_PROJECT`, or `iskender-local` when it is unset or empty.
2. A value other than `iskender-local` or `iskender-local-` followed by 1 to 24 characters of `a-z` and `0-9` makes every script exit 64 before anything runs. ISK-0007's project names (`isk-` and 12 hexadecimal characters) can never match.
3. `compose.yaml` sets `name: ${ISKENDER_LOCAL_PROJECT:-iskender-local}`, and every script passes `--project-name` and both Compose files.
4. One project per name. Several projects from one checkout at once are supported only under different names and ports.

## Services

*Review owner: Architecture (ADR) owner.*

| Service | Image | Started by | Depends on |
|---|---|---|---|
| `postgres` | `postgres:17-bookworm@sha256:665dc7b37ac52a17e298511414c21d23ffa4e6a6ef6225b3e5d59c7afb35e994` | Default, `dependencies` | |
| `valkey` | `valkey/valkey:8.1.11@sha256:8f836008e0986c36adf50da714c7060cf689dbb335f61fe077939d90fa650809` | Default, `dependencies` | |
| `data-plane` | `<project>/data-plane:local`, built by start | Default | `postgres`, `valkey` healthy |
| `control-plane` | `<project>/control-plane:local`, built by start | Default | `postgres`, `valkey` healthy |
| `migrate` | `<project>/control-plane:local` | Profile `migrate`, through the migration script | `postgres` healthy |
| `probe` | `curlimages/curl:8.17.0@sha256:935d9100e9ba842cdb060de42472c7ca90cfe9a7c96e4dacb55e79e560b3ff40` | Profile `probe`, through the smoke script | |

1. A digest changes only with the ISK-0004 or ISK-0005 fixture it matches, or for `probe` by itself, through a revision of this table.
2. The plane tags are built by the start script from `src/Iskender.Gateway.Host/Dockerfile` and `src/Iskender.ControlPlane.Host/Dockerfile`, from the repository root, with `REVISION` set to `git rev-parse HEAD`. Compose services use `pull_policy: never` and build nothing.
3. No plane depends on the other or on `migrate`. No service runs `migrate` unless the migration script runs it.
4. The default start is `docs/04` topology 1's all-in-one developer distribution: both unchanged plane images, each its own process in its own container, with their dependencies. It is not one container, because the planes must run as separate processes (container contract, Entry points rule 3), one container running two processes needs a process supervisor as its entry point, which would be a new entry point, and the chiseled runtime image has no shell, init, or supervisor and may not gain a shell or package manager (Images rule 2). The optional UI is left out until a management UI spec adds it. **This settles the container contract's AV-6.**

## Modes

*Review owner: Architecture (ADR) owner.*

| Mode | Started by | Services | Published |
|---|---|---|---|
| Default | `start.sh` | `postgres`, `valkey`, `data-plane`, `control-plane` | The data plane's traffic port |
| `dependencies` | `start.sh --dependencies` | `postgres`, `valkey` | `postgres` and `valkey` |

1. The `dependencies` mode layers `compose.dependencies.yaml`, which adds only `ports` to `postgres` and `valkey`, and starts those two by name. It stops `data-plane` and `control-plane` first if they run, and builds no plane image. A Compose profile cannot express it, because a profile only adds services.
2. A start in either mode after the other reuses the same volumes.

## Hardening

*Review owner: Security owner.*

1. `data-plane`, `control-plane`, `migrate`, and `probe` run with `read_only: true`, `cap_drop: [ALL]`, and `no-new-privileges:true`, and set no `user`, `privileged`, `network_mode`, or `cap_add`. The planes and `migrate` also have a `tmpfs` at `/tmp`.
2. `data-plane` and `control-plane` have `stop_grace_period: 40s`, above the defaults' `shutdownDelay` + `shutdownTimeout` + 5 s = 35 s. A document that raises either timing raises the grace period with it.
3. Each plane's health check is the exec-form `["CMD", "dotnet", "/app/<host>.dll", "health", "live"]`, every 2 s, with a 35-second timeout.
4. `postgres` and `valkey` keep their images' own users and entrypoints.

## Ports

*Review owner: Security owner.*

| Service | Container port | Host address | Default host port | Override variable | Mode |
|---|---|---|---|---|---|
| `data-plane` | 8080 | `127.0.0.1` | 18080 | `ISKENDER_LOCAL_DATA_PLANE_PORT` | Default |
| `postgres` | 5432 | `127.0.0.1` | 15432 | `ISKENDER_LOCAL_POSTGRES_PORT` | `dependencies` |
| `valkey` | 6379 | `127.0.0.1` | 16379 | `ISKENDER_LOCAL_VALKEY_PORT` | `dependencies` |

1. Nothing else is published in any mode. No health port is ever published, and the control plane publishes nothing.
2. An override must be an integer from 1,024 to 65,535, or the script exits 64.
3. Before starting, the start script exits 2 when a port it will publish already answers on `127.0.0.1`, naming the override variable.

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

2. It creates no schema, table, or row, and runs no migration. Passwords reach `psql` as variables, never on a command line.
3. The health check is `pg_isready -h 127.0.0.1 -U postgres -d iskender_local`, which passes only after initialisation.

## Passwords

*Review owner: Security owner.*

1. The password file is `local/.secrets/<project>.env`, holding `POSTGRES_PASSWORD`, `ISKENDER_LOCAL_RUNTIME_PASSWORD`, and `ISKENDER_LOCAL_MIGRATION_PASSWORD`, each 32 characters of `A-Za-z0-9` from `/dev/urandom`. The directory is mode `0700` and the file `0600`.
2. The start script writes it only when both it and the `postgres-data` volume are absent. When the volume exists and the file does not, start exits 2 and names the reset script.
3. Only `postgres` reads it, as an `env_file` with `required: false`.
4. No script, document, log, readiness answer, or script output holds a password. The documents name the passwords only as the secret references `local/postgres-runtime` and `local/postgres-migration`, which no credential provider resolves before ISK-0021.

## Valkey

*Review owner: Architecture (ADR) owner.*

`valkey-server --maxmemory-policy noeviction --appendonly yes --appendfsync everysec --protected-mode no`, with no password, ACL user, or TLS, which only `development` permits (coordination contract, Credentials rule 2). Health check: `valkey-cli ping`.

## Documents and environment safety

*Review owner: Security owner.*

1. `config/<plane>/` is mounted read-only at `/etc/iskender` in that plane's service, and `config/control-plane/` in `migrate`. Each plane service, and `migrate`, sets `ISKENDER_ENVIRONMENT=development` itself, so the developer's shell value never reaches a container, and no other Iskender variable is passed in.
2. Each base `iskender.yaml` holds no development-only value, and validates alone with no environment name:

   | Field | Data plane | Control plane |
   |---|---|---|
   | `hosting.requestTimeout` | `100s` | Absent |
   | `persistence.host` | `postgres` | `postgres` |
   | `persistence.database` | `iskender_local` | `iskender_local` |
   | `persistence.username` | `iskender_local_runtime` | `iskender_local_runtime` |
   | `persistence.password.secretRef` | `local/postgres-runtime` | `local/postgres-runtime` |
   | `persistence.migration.username` | Absent | `iskender_local_migrator` |
   | `persistence.migration.password.secretRef` | Absent | `local/postgres-migration` |

3. Each overlay `iskender.development.yaml` holds every development-only value and the Redis selection: `persistence.tlsMode: disable`, `coordination.atomic.coordinator`, `leases.coordinator`, and `cache.coordinator` `redis`, `coordination.redis.endpoints: [valkey:6379]`, and `coordination.redis.tlsMode: disable`. It sets no `coordination.topology`, so `multi_replica`, and no Redis user or password.
4. Precedence is ISK-0003's: defaults, base, overlay, `ISKENDER__` variables. There is no further layer. **This settles the configuration contract's AV-6 for local use.**
5. The control plane rejects the data-plane documents with `config.namespace.unknown`, since they name `hosting`. The control plane registers no section of its own, so the data plane would accept the control-plane documents. They are kept separate so the data plane never holds the migration login's references.
6. No tracked file under `local/` holds a password, token, or credential value.

## Native runs

*Review owner: Architecture (ADR) owner.*

1. `native/<plane>/` is the matching `config/<plane>/` pair with only these values changed: `persistence.host: 127.0.0.1`, `persistence.port: 15432`, the Redis endpoint `127.0.0.1:16379`, and, for the control plane only, `container.trafficPort: 8090` and `container.healthPort: 8091`.
2. A host runs natively against the `dependencies` mode with `ISKENDER_CONFIG_DIRECTORY` naming `local/native/<plane>` and `ISKENDER_ENVIRONMENT=development`. Its readiness reports the same checks and reasons as the containerized plane.
3. `ISKENDER__PERSISTENCE__PORT` follows an overridden PostgreSQL port. A Redis endpoint cannot be set by a variable, because `redis.endpoints` is an array (configuration contract, Environment variables rule 3), so an overridden Valkey port needs an edited copy of the native directory.
4. A native host binds both its listeners on all interfaces (container contract, Listeners rule 1), unlike the topology's loopback ports.

## Scripts

*Review owner: Privacy/Operations owner.*

Each script is `local/scripts/<name>.sh`, runs under `bash` 3.2 or later, works from any directory inside the checkout, prints usage for `--help` and exits 0, and writes only value-free messages, to standard error. Each finds the repository root from its own path with `git rev-parse --show-toplevel`, and exits 2 when that root has no `Iskender.slnx` and `local/compose.yaml`.

| Script | Options | Does |
|---|---|---|
| `start.sh` | `--dependencies` | Checks the prerequisites and ports, writes the password file when Passwords rule 2 allows it, builds both plane images unless `--dependencies`, and runs `docker compose up --detach --wait --wait-timeout 180`. Exits 0 when every started service is healthy: `postgres` and `valkey`, and in the default mode each plane's `health live` |
| `stop.sh` | | `docker compose stop --timeout 40`. Keeps every volume and the password file |
| `reset.sh` | `--yes` | After confirmation, or with `--yes`, `docker compose down --volumes --remove-orphans --timeout 40`, then removes the password file. Removes no image, and nothing of another project or outside `local/` |
| `migrate.sh` | `apply`, `reverse [--steps <n>] [--confirm-data-loss]`, `status` (default) | Runs the `migrate` service once with the operation, and exits with its exit code |
| `smoke.sh` | | Asserts the Smoke assertions below against the running default topology |

Prerequisites: `git`; `docker`; a running container runtime; Docker Compose 2.24.0 or later, with Docker Engine 24.0 or later. Windows runs the scripts in WSL 2.

## Exit codes

*Review owner: Privacy/Operations owner.*

| Code | Meaning |
|---|---|
| 0 | Success. For smoke, every assertion passed |
| 1 | A smoke assertion failed, an image build or a Compose step failed, or a service exited or was not healthy within start's 180-second bound. The message names the service and, if it exited, its exit code |
| 2 | A prerequisite is missing or unusable: no `git`, `docker`, or Compose, the runtime not running, Compose older than 2.24.0, a script outside a checkout, a published port already in use, a `postgres-data` volume without its password file, or, for the migration script, no control-plane image |
| 64 | A usage error: an unknown option or operation, an invalid project name or port, or a reset in a non-interactive shell without `--yes` |

The migration script exits with the `migrate` command's own code (container contract, Command line and exit codes 2), except that it exits 2 or 64 itself before starting the job. Each failure names the check or prerequisite, and no value.

## Smoke assertions

*Review owner: Privacy/Operations owner.*

Valid before ISK-0021. The smoke script runs every check and exits 1 if any fails, naming each failure:

1. `postgres`, `valkey`, `data-plane`, and `control-plane` are running. Each one that is not is named.
2. Each plane's `health live` command, run with `docker compose exec`, exits 0.
3. Each plane's `/health/ready`, read through `probe` at `http://<plane>:8081/health/ready`, answers `503` with exactly this body, after waiting up to 60 s while `lifecycle` is `starting`:

   ```json
   {"version":1,"status":"not_ready","checks":[{"name":"lifecycle","status":"ready","reason":"started"},{"name":"persistence","status":"not_ready","reason":"authentication_failed"},{"name":"coordination","status":"ready","reason":"ok"},{"name":"single_instance","status":"ready","reason":"not_applicable"},{"name":"capability","status":"ready","reason":"none_refused"}],"refused":[]}
   ```

   Any other answer fails, a persistence reason of `ok` included, and the failure names each check whose reason differs and the reason found.
4. `migrate.sh status` exits 5, and the job writes one event 7006 naming the operation `status` and the code `persistence.unavailable`.
5. `postgres` holds exactly the roles and grants of PostgreSQL rule 1, and no schema outside `pg_catalog`, `information_schema`, `pg_toast`, and `public`, with no relation in `public`.
6. `valkey` reports `maxmemory-policy` `noeviction`.
7. No plane log, readiness answer, or smoke output holds a password, the database name, a login name, or a secret reference name.

## Test fixtures

*Review owner: Performance owner (+ QA).*

1. `tests/Iskender.Container.Tests/LocalEnvironment/` holds the acceptance tests. They run in CI check 4 and add no check.
2. Each runs in a clean copy of the tracked files only, made into its own Git checkout, under a project name `iskender-local-t<12 hex>` and free ports, and removes its project and image tags when it ends.
3. Tests that need a container runtime, `bash`, or `git` follow ISK-0005's rule: skipped with their reason without one, and failing when `CI` is `true`.
4. The acceptance run is 5 consecutive cycles of start, smoke, and reset in one clean copy, each exiting 0, with no project container, network, or volume left after each reset.

## Threat model and known limits

### What the rules defend against

The Negative Security and Failure Scenarios table in [spec.md](../spec.md) lists each threat. These rules implement them: development-only values outside `development` (Documents 2, 3), passwords committed or printed (Passwords), an interim credential making the topology look ready (Smoke assertions 3), the database, Redis, or a health endpoint reachable from another machine (Ports), relaxed image hardening (Hardening 1), a reset reaching another project (Project name 2, Scripts), a password regenerated for a provisioned volume (Passwords 2), Redis eviction (Valkey), migration at start (Services 3), a moving image (Services 1), the ISK-0007 fixture and the topology drifting together (Layout 2), and smoke passing on a changed state (Smoke assertions 3).

### Known limits

- **Not ready before ISK-0021**: no credential provider exists, so both planes report persistence `authentication_failed` and every `migrate` operation exits 5. ISK-0021 changes the expected smoke state through a revision of this contract (spec AV-6).
- **Passwords in the `postgres` environment**: `docker inspect` shows them to a user who can run Docker commands, who can already read the password file.
- **Native hosts listen on all interfaces**, and a native Valkey port override needs an edited document.
- **No TLS between local services.**
- **One architecture**: the plane images build for the workstation's own architecture.
- **Building needs the network**: the image build restores packages from nuget.org, and the first start pulls the pinned images.

## Left to later specs

| Item | Spec |
|---|---|
| Credential providers, after which the smoke state becomes ready | ISK-0021 (spec AV-6) |
| Maintenance work as another control-plane command service | ISK-0009 (spec AV-7) |
| The optional management UI service of topology 1 | The management UI spec (spec AV-8) |
| A single all-in-one image with a supervisor, a local Kubernetes topology, native Windows scripts, a local observability stack, TLS between local services, seed or sample data | Post-MVP |
| Hot reload of configuration | ISK-0057 |
