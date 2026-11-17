# Research: ISK-0008 — Local Developer Environment & Reproducible Compose

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-11-16

Each decision records what was chosen, why, and what was rejected. The maintainer decided three questions during `plan` on 2026-11-16 (R5, R9, R14). Each is marked **Maintainer decision**. Every other decision follows from the spec, the contracts it names, or the code as it stands on `main`. Two of them correct the spec where it assumed something the code does not do, with no choice left to make (R5, R6), and are marked **Correction**.

Facts found in the code are cited by path. The contracts are the [container contract](../0007-container-entry-points-process-separation/contracts/container.md), version 1, the [hosting contract](../0007-container-entry-points-process-separation/contracts/hosting.md), version 2, the [persistence contract](../0005-coordination-redis-capability-contracts/contracts/persistence.md), version 2, the [coordination contract](../0005-coordination-redis-capability-contracts/contracts/coordination.md), and the [configuration contract](../0003-configuration-schema-validation-foundation/contracts/configuration.md).

## R1. Where the topology lives

**Decision**: One directory, `local/`, at the repository root:

```text
local/
├── compose.yaml                       # The topology: services, volumes, network, project name
├── compose.dependencies.yaml          # Layered on compose.yaml by start --dependencies: loopback ports for postgres and valkey
├── config/
│   ├── data-plane/                    # Mounted read-only at /etc/iskender in data-plane
│   │   ├── iskender.yaml
│   │   └── iskender.development.yaml
│   └── control-plane/                 # Mounted read-only at /etc/iskender in control-plane and migrate
│       ├── iskender.yaml
│       └── iskender.development.yaml
├── native/
│   ├── data-plane/                    # ISKENDER_CONFIG_DIRECTORY for a native data plane (R5)
│   └── control-plane/                 # ISKENDER_CONFIG_DIRECTORY for a native control plane (R5)
├── postgres/
│   └── initdb/10-provision.sh         # Run once by the official image on an empty data volume (R7)
├── scripts/
│   ├── start.sh, stop.sh, reset.sh, migrate.sh, smoke.sh
│   └── lib/common.sh, lib/readiness.sh
└── .secrets/                          # Ignored by Git and Docker: <project>.env, the generated passwords (R8)
```

`local/` is outside `src/` and `tests/` (FR-001). It shares no file with `tests/Iskender.Container.Tests/compose/`, and neither includes the other. The ISK-0007 fixture's own comment already names "ISK-0008's developer Compose" as separate (`tests/Iskender.Container.Tests/compose/compose.yaml`, line 3).

Three repository files change to fit it:

- `.gitignore` gains `/local/.secrets/`. Its `*.env` rule would already ignore the password file, but an explicit entry states the intent and survives a change to that rule.
- `.dockerignore` gains `local/`. Both images build from the repository root, so without it the password file would be sent to the build daemon in the context, although neither Dockerfile copies it (`src/Iskender.Gateway.Host/Dockerfile` copies only the root build files and `src/`). Neither Dockerfile changes (FR-004).
- `.github/CODEOWNERS` gains `/local/`, and `.github/scripts/docs-gate.sh` adds `local/*` to its behavior-bearing paths, so a change to the topology needs a documentation change, as a change to a host does.

**Rejected**: `deploy/local/`, since `docs/04` forbids development-only defaults in production manifests and a `deploy/` path invites copying. `tools/` or `eng/`, which read as build tooling, not something a developer runs. A directory under `tests/`, which the spec excludes.

## R2. Services, the `dependencies` mode, and the `migrate` profile

**Decision**: `local/compose.yaml` defines `postgres`, `valkey`, `data-plane`, and `control-plane` with no profile, so a plain `up` starts all four (TR-2). It also defines two one-shot services, each under a Compose profile, so neither starts by default:

| Service | Profile | Image | Purpose |
|---|---|---|---|
| `migrate` | `migrate` | The local control-plane tag | Runs one `migrate` operation with the control plane's documents (R11) |
| `probe` | `probe` | `curlimages/curl` by digest | Reads one `/health/ready` for the smoke script (R14) |

The spec's `dependencies` profile cannot be a Compose profile. A Compose profile only adds services, so it cannot leave the planes out of a start, and it cannot add published ports to a service that the default start also runs without them. The spec requires the default start to publish no database or Redis port and the dependencies start to publish both (TR-9). So **start --dependencies** layers `local/compose.dependencies.yaml`, which adds only `ports` to `postgres` and `valkey`, and starts those two services by name. It stops `data-plane` and `control-plane` first if they run, so the topology matches the mode. The contract calls this the `dependencies` mode. Every other Compose command passes both files, so each sees every service of the project in either mode. The default start's `up` passes only `compose.yaml`, so the dependency ports never apply in the default mode. The migration job runs with `--no-deps`, because `docker compose run` otherwise starts the job's dependencies, and with both files would recreate a running `postgres` with the `dependencies` mode's ports, or start a stopped one during a smoke run (R11).

Both planes `depends_on` `postgres` and `valkey` with `condition: service_healthy`, and neither depends on the other or on `migrate` (FR-002).

**Rejected**: Publishing `postgres` and `valkey` always, which breaks TR-9. A second copy of each dependency under the profile, with ports, which gives two services over one volume and two containers on a mode switch. Port forwarders in the profile, which add an image.

## R3. Images and tags

**Decision**: The start script builds both images with `docker build -f <Dockerfile> -t <tag> --build-arg REVISION=<commit> .` from the repository root, as ISK-0007's fixture does (`tests/Iskender.Container.Tests/Fixture/ImageBuild.cs`, line 94). `<commit>` is `git rev-parse HEAD`. The tags are `<project>/data-plane:local` and `<project>/control-plane:local`, where `<project>` is the Compose project name (R9), so a test project never replaces a developer's tags. The Compose services name these tags with `pull_policy: never`, and Compose builds nothing itself, so every image the topology runs comes from the start script's build of the current checkout. `--dependencies` builds no plane image. The migration script exits 2, naming the start script, when the control-plane tag is absent.

`postgres` and `valkey` use exactly the digests of the ISK-0004 and ISK-0005 fixtures, which TR-4 lists and `tests/Iskender.Container.Tests/compose/compose.yaml` also uses.

**Validated, AV-1**: on 2026-11-16, `docker buildx imagetools inspect` showed each pinned digest to be a multi-platform index with `linux/amd64` and `linux/arm64` (`linux/arm64/v8` for PostgreSQL) entries: `postgres:17-bookworm@sha256:665dc7b3…`, `valkey/valkey:8.1.11@sha256:8f836008…`, `curlimages/curl:8.17.0@sha256:935d9100…`, and both .NET base images of the Dockerfiles. The plane images build for the builder's own architecture (container contract, Known limits). The topology ran on `arm64` during this check.

**Rejected**: `build:` sections in `compose.yaml`, which would let `docker compose up` rebuild without `REVISION` and on its own schedule. Published images, which ISK-0007 does not publish.

## R4. Hardening and lifecycle

**Decision**: Each plane service sets `read_only: true`, `tmpfs: [/tmp]`, `cap_drop: [ALL]`, `security_opt: [no-new-privileges:true]`, and `stop_grace_period: 40s`, and sets no `user`, `privileged`, `network_mode`, or `cap_add` (TR-5). The documents set no `container` timings, so the defaults apply: `shutdownDelay` 0 s + `shutdownTimeout` 30 s + 5 s = 35 s, below 40 s (container contract, Lifecycle rule 4). The `migrate` and `probe` services take the same `read_only`, `cap_drop`, and `no-new-privileges` settings.

`postgres` and `valkey` keep their official images' own users and start-up, since both entrypoints start as root to set ownership of their data directory and then drop to their own user. TR-5 covers the plane services. The dependencies are protected by being reachable only on the Compose network, or on loopback in the `dependencies` mode (R10).

**Plane health checks**: each plane service has a Compose health check that runs the image's own exec-form command, `["CMD", "dotnet", "/app/Iskender.Gateway.Host.dll", "health", "live"]` or the control-plane equivalent, which the container contract permits (Images rule 2) and which needs no shell. Its `timeout` is 35 s, above the command's own 30-second wait. The start script waits with `docker compose up --detach --wait --wait-timeout 180` (R12), so "each plane's `health live` command exits 0" is exactly Compose's `healthy`. Readiness is not checked at start.

## R5. Configuration documents, and native runs

**Decision**: Each plane has a base document and a `development` overlay under `local/config/<plane>/` (TR-8):

| Document | Holds |
|---|---|
| Data-plane base | `hosting.requestTimeout: 100s`, the default written out so the document names `hosting`. `persistence.host: postgres`, `database: iskender_local`, `username: iskender_local_runtime`, `password.secretRef: local/postgres-runtime`. No `tlsMode`, so `require` |
| Control-plane base | The same `persistence` fields, plus `migration.username: iskender_local_migrator` and `migration.password.secretRef: local/postgres-migration`. No `hosting` |
| Both overlays | `persistence.tlsMode: disable`. `coordination.atomic.coordinator`, `leases.coordinator`, and `cache.coordinator`: `redis`. `coordination.redis.endpoints: [valkey:6379]`, `redis.tlsMode: disable`. No `topology`, so `multi_replica`, and no Redis user or password |

The Redis endpoint must be in the overlay, not the base: outside `development`, a section with `redis.endpoints` and no `redis.username` or `redis.password` is refused with `config.field.required` (coordination contract, Configuration). The primitive selections are not development-only, but selecting `redis` with no endpoint leaves coordination `not_configured` and not ready (container contract, Readiness rule 4), so they sit with the endpoint. The base alone therefore validates with no environment name and leaves coordination on `local`.

**Correction, native runs**: TR-8 and FR-014 say a native host reaches the `dependencies` mode's loopback ports "through documented `ISKENDER__` overrides for the host names and ports". That works for PostgreSQL, whose `host` and `port` are scalars. It cannot work for Valkey: `coordination.redis.endpoints` is an array, and a variable that sets an `object` or `array` field is `config.field.type_mismatch` (configuration contract, Environment variables rule 3).

**Maintainer decision (2026-11-16, `plan`)**: native runs use their own tracked documents, `local/native/data-plane/` and `local/native/control-plane/`. Each is the matching `local/config/<plane>/` pair with only these lines changed: `persistence.host: 127.0.0.1` and `port: 15432`, the endpoint `127.0.0.1:16379`, and, for the control plane only, `container.trafficPort: 8090` and `healthPort: 8091`, so both native hosts can run at once on one workstation. A test compares each pair line by line and fails on any other difference. A PostgreSQL host port override still reaches a native host through `ISKENDER__PERSISTENCE__PORT`. A Valkey host port override needs an edited copy of the native directory, which the guide shows and the contract records as a known limit. TR-8 and FR-014 are reworded in `spec.md`.

A native host binds both its listeners on all interfaces (container contract, Listeners rule 1), unlike the topology's loopback ports. The guide says so, since it is the one place where a local run is reachable from the network.

**Rejected** (by the maintainer): documents that the start script generates with the real published ports, which adds a generated configuration layer for reset to clean up; leaving native hosts on `local` coordination, which makes their readiness differ from the containers' (US5.2).

## R6. Which plane can reject which document

**Correction**: US3.6 says each document "names its plane's own sections only, so the other plane would reject it with `config.namespace.unknown`". That holds in one direction only. The data plane registers `hosting`, which the control plane does not, so the control plane rejects the data-plane documents. The control plane registers no section of its own: `AddIskenderControlPlane(...)` lists none (hosting contract, Registration rule 6), and its other sections, `container`, `coordination`, and `persistence`, are registered by both planes. So the data plane accepts the control-plane documents. Making it reject them would need a new section, which is code outside this spec. US3.6 is reworded in `spec.md`: the data-plane documents name `hosting`, which the control plane rejects, and the control-plane documents hold the migration login's references, which the data plane never needs. The tests assert both.

## R7. PostgreSQL provisioning

**Decision**: `postgres` sets `POSTGRES_DB=iskender_local` and mounts `local/postgres/initdb/` read-only at `/docker-entrypoint-initdb.d`. The official image runs `10-provision.sh` once, on an empty data volume, after `initdb` and before the server listens on TCP. The script runs `psql` with `ON_ERROR_STOP=1` and reads each password into a `psql` variable from its environment with `\getenv` (PostgreSQL 15 and later), then uses it as `PASSWORD :'runtime_password'`, so no password appears on a command line or in the SQL text. The session sets `\set VERBOSITY terse` and `SET log_min_error_statement = panic`, so a failed statement is never echoed with its text, and the server's default `log_statement` of `none` keeps statements out of its log. (Settled in `checklist`: the earlier wording passed each password with `-v`, which puts it on `psql`'s command line.) It creates:

| Role | Attributes | Grants |
|---|---|---|
| `iskender_runtime` | `NOLOGIN NOBYPASSRLS NOSUPERUSER NOCREATEDB NOCREATEROLE` | None. It owns nothing |
| `iskender_local_runtime` | `LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE`, `IN ROLE iskender_runtime` | `CONNECT` on `iskender_local`. No `CREATE` |
| `iskender_local_migrator` | `LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE` | `CONNECT` and `CREATE` on `iskender_local` |

This is the persistence runbook's set (`docs/04`, Database migrations and rollback). PostgreSQL 15 and later grant `PUBLIC` no `CREATE` on the `public` schema and none on the database, so the runtime login can create nothing. The script creates no table and no schema, and runs no migration (FR-007).

**Health check**: `pg_isready -h 127.0.0.1 -U postgres -d iskender_local`. The `-h 127.0.0.1` matters: during initialisation the image runs a temporary server on its Unix socket only, and a socket check would report healthy before the provisioning script has finished.

## R8. Passwords

**Decision**: The password file is `local/.secrets/<project>.env`, holding `POSTGRES_PASSWORD`, `ISKENDER_LOCAL_RUNTIME_PASSWORD`, and `ISKENDER_LOCAL_MIGRATION_PASSWORD`. When it is absent and the project's `postgres-data` volume is absent too, the start script creates `local/.secrets/` with mode `0700`, sets `umask 077`, and writes each password as 32 characters of `A-Za-z0-9` from `/dev/urandom` (`LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom | head -c 32`), about 190 bits each. `/dev/urandom` is the kernel's cryptographic source on Linux and macOS. The file is mode `0600`.

Only `postgres` reads it, as a Compose `env_file` with `required: false`, so that the stop and reset scripts still parse the Compose file when it is absent. No other service names it. Compose reads it on the host, as the user who runs the script, so its owner-only mode does not stop the container from receiving the values.

When the `postgres-data` volume exists and the file does not, the start script exits 2 and names the reset script (FR-009). When the file exists and the volume does not, the start provisions a fresh volume with the existing passwords, which is a valid first start. When the file exists but does not hold exactly the three names, each with 32 characters of `A-Za-z0-9`, the start exits 2, names the reset script, and changes nothing. Every start sets the directory to `0700` and the file to `0600` again, so a mode changed by hand does not persist (settled in `checklist`).

**Known limit**: the three values are in the `postgres` container's environment, which `docker inspect` shows to anyone who can run Docker commands on the workstation. That user can already read the file and the data volume, so no reader gains anything, and no script prints them.

**Rejected**: Compose `secrets` with a file source. Outside Swarm they are bind mounts that keep the host file's owner and mode, so on Linux an owner-only file is unreadable to the `postgres` user (uid 999) that reads `POSTGRES_PASSWORD_FILE`. `openssl rand`, which is not on every minimal Linux.

## R9. The Compose project name

**Maintainer decision (2026-11-16, `plan`)**: the project name is `iskender-local` by default. The scripts read `ISKENDER_LOCAL_PROJECT`, which must be `iskender-local` or `iskender-local-` followed by 1 to 24 characters of `a-z` and `0-9`. Any other value exits 64 before anything runs. ISK-0007's fixture uses `isk-` and 12 hexadecimal characters (`tests/Iskender.Container.Tests/Fixture/ComposeProject.cs`, line 41), which can never match, so a reset can never reach it. The image tags (R3) and the password file (R8) are derived from the name, so a second project never shares either. The acceptance test sets its own name and free ports, so it never touches a developer's running topology. The name is per Docker host, not per checkout: two checkouts or worktrees under the same name are one project, so each sets its own name. The contract records this as a known limit (settled in `checklist`).

`compose.yaml` sets `name: ${ISKENDER_LOCAL_PROJECT:-iskender-local}`, and the scripts also pass `--project-name`, so a hand-typed `docker compose -f local/compose.yaml` command reaches the same project.

**Rejected** (by the maintainer): a strictly fixed name, with the acceptance test refusing to run while a developer's project exists.

## R10. Published ports

**Decision**: published only on `127.0.0.1` (TR-9):

| Service | Container port | Host port | Override variable | Mode |
|---|---|---|---|---|
| `data-plane` | 8080, traffic | 18080 | `ISKENDER_LOCAL_DATA_PLANE_PORT` | Default |
| `postgres` | 5432 | 15432 | `ISKENDER_LOCAL_POSTGRES_PORT` | `dependencies` |
| `valkey` | 6379 | 16379 | `ISKENDER_LOCAL_VALKEY_PORT` | `dependencies` |

The defaults sit away from each service's usual port, so a PostgreSQL, Redis, or web server already on the workstation does not collide. No health port, `8081` on either plane, is published in any mode, and the control plane publishes nothing. Each value must be an integer from 1,024 to 65,535, or the script exits 64.

Before starting, the start script tries a TCP connection to `127.0.0.1:<port>` for each port it will publish, using `bash`'s `/dev/tcp`. If one answers, it exits 2, naming the port's override variable (US5.4). If Compose still fails to bind, the script exits 1 and names the same variables.

## R11. The migration job

**Decision**: the `migrate` service runs the control-plane tag with `local/config/control-plane/` mounted read-only at `/etc/iskender`, `ISKENDER_ENVIRONMENT=development`, `depends_on: postgres: condition: service_healthy`, and no published port. The migration script accepts `apply`, `reverse` with `--steps <n>` and `--confirm-data-loss`, or `status`, and `status` by default. It rejects anything else with exit code 64 before starting the job, and passes the image's own argument checks through. It runs `docker compose run --rm --no-deps --no-TTY migrate migrate <operation> [options]`, so the job never starts or recreates `postgres` (R2), and exits with the job's exit code (FR-018). Before ISK-0021 that is 5 for every operation, with event 7006 naming `persistence.unavailable` (container contract, Migration command rule 7).

## R12. Scripts

**Decision**: five `bash` scripts in `local/scripts/`, each tracked with mode `100755` and LF endings, and two sourced libraries:

- `lib/common.sh`: finds the repository root from the script's own path with `git -C <dir> rev-parse --show-toplevel`, and requires `Iskender.slnx` and `local/compose.yaml` there, or exits 2 ("outside a checkout"). It validates `ISKENDER_LOCAL_PROJECT` and the port variables, checks the prerequisites, runs Compose with both files and the project name, and writes value-free messages to standard error.
- `lib/readiness.sh`: the readiness comparison of R13 as functions with no side effects, so tests can give it a recorded answer.

Prerequisites, each exit code 2 with the check's name: `git`; `docker` on `PATH`; `docker version` answering, so the runtime is running; `docker compose version --short` at 2.24.0 or later, compared numerically by major, minor, and patch, so `5.5.0` passes.

**Validated, AV-2**: the scripts need Compose's `--wait` and `--wait-timeout` (2.17.0), `depends_on` with `service_healthy`, profiles on `run`, and `env_file` with `required: false`, added in 2.24.0. So the minimum is **Compose 2.24.0 with Docker Engine 24.0**. This plan was checked on Docker Engine 29.8.2 with Compose 5.5.0 on macOS `arm64`. CI's `ubuntu-24.04` runner ships Docker and Compose v2 above that minimum, which the acceptance test checks in every run, since start checks the version. Windows is supported through WSL 2 with Docker Desktop's WSL integration, which runs the same Linux `bash`.

**Validated, AV-3**: the scripts are written for `bash` 3.2, the macOS system shell: no associative arrays, `mapfile`, `${var,,}`, `[[ -v ]]`, `|&`, or `wait -n`, and no expansion of an empty array under `set -u`. They use only `git`, `docker`, `tr`, `head`, `sed`, `grep`, `cat`, `mkdir`, `chmod`, `rm`, `sleep`, and `date`. The acceptance test runs them under macOS `bash` 3.2 on this workstation and `bash` 5 on CI.

Bounds, which are safety bounds, not performance figures (Constitution VIII): start waits up to 180 s for health, which covers a first PostgreSQL initialisation; smoke waits up to 60 s for `lifecycle` to leave `starting`; each probe request has a 15-second limit, above the readiness answer's own bound of the longest probe timeout (5 s) plus 2 s (container contract, Readiness rule 8).

The reset script asks `Remove all data of <project>? [y/N]` when standard input is a terminal, removes nothing and exits 0 on any answer other than `y` or `Y`, and without `--yes` in a non-interactive shell it exits 64 and removes nothing (FR-020). It runs `docker compose down --volumes --remove-orphans --timeout 40` with both files and the project name, then removes `local/.secrets/<project>.env`. It passes no `--rmi`, so no image is removed. `down --volumes` removes only volumes the project's Compose file declares, under its own name.

## R13. Smoke assertions

**Decision**: the smoke script runs these checks in order, reports every failure rather than stopping at the first, and exits 1 if any failed (TR-13):

1. Each of `postgres`, `valkey`, `data-plane`, and `control-plane` is running. Otherwise the script names each one that is not, and skips the checks that need it.
2. Each plane's image `health live` command exits 0, through `docker compose exec`, which needs no shell in the image.
3. For each plane, polling every second for up to 60 s while the answer's `lifecycle` reason is `starting`, the probe's answer is status `503` with exactly this body:

   ```json
   {"version":1,"status":"not_ready","checks":[{"name":"lifecycle","status":"ready","reason":"started"},{"name":"persistence","status":"not_ready","reason":"authentication_failed"},{"name":"coordination","status":"ready","reason":"ok"},{"name":"single_instance","status":"ready","reason":"not_applicable"},{"name":"capability","status":"ready","reason":"none_refused"}],"refused":[]}
   ```

   The body is compared as one exact string. `implement` confirms the serializer's exact bytes against a running plane and records them in the contract. Comparing the whole answer means any change fails, a persistence reason of `ok` included. On a mismatch, `lib/readiness.sh` extracts each check's `reason` with `sed` and names each check whose reason differs, with the reason found, such as `coordination: expected ok, found unreachable`. Reasons are code-defined words, never values.
4. `migrate status` through the migration job exits 5, and its output has exactly one event 7006 line naming `status` and `persistence.unavailable`.
5. `postgres` holds the three roles of R7 with their attributes, and no other role except its superuser `postgres` and the predefined `pg_*` roles, `iskender_local_migrator` holds `CREATE` on the database, `iskender_local_runtime` does not, and no schema exists outside `pg_catalog`, `information_schema`, `pg_toast`, and `public`, and `public` holds no relation. This checks seed-free start-up (FR-016).
6. `valkey-cli CONFIG GET maxmemory-policy` answers `noeviction`.
7. No plane log line, readiness answer, or smoke output holds any of the three passwords, the database name, either login name, or either secret reference name. The smoke script reads these values from the password file and the documents, searches with `grep -F -f`, giving the values through a process substitution written by the `printf` builtin so that none is ever a command-line argument, and reports only which kind of value was found (settled in `checklist`).

Each check reads only the topology's own project. Searching the logs for values is the same canary technique ISK-0007 uses (`tests/Iskender.Container.Tests/Canary/`).

## R14. The readiness probe image

**Maintainer decision (2026-11-16, `plan`)**: the `probe` service uses `curlimages/curl:8.17.0@sha256:935d9100e9ba842cdb060de42472c7ca90cfe9a7c96e4dacb55e79e560b3ff40`. It runs as `curl_user` (uid 100, gid 101) by default, and with `--read-only`, `--cap-drop ALL`, and `no-new-privileges` it runs curl unchanged (checked 2026-11-16). Its labels declare `org.opencontainers.image.licenses: MIT` and the source `https://github.com/curl/curl-container`. curl itself is under the curl licence, a permissive MIT-style licence. The topology runs the image and redistributes nothing from it. The smoke script runs `docker compose run --rm --no-TTY probe --silent --show-error --max-time 15 --output - --write-out '\n%{http_code}' http://<plane>:8081/health/ready`, with no published port. The service sets `restart: "no"`.

**Validates AV-4**: pinned by its multi-platform index digest, non-root, and licensed for use alongside an MIT project, decided by the maintainer as Product/Charter owner on 2026-11-16.

**Rejected** (by the maintainer): reusing the pinned PostgreSQL image with a hand-written HTTP/1.0 request over `bash`'s `/dev/tcp`, which adds no image but parses HTTP by hand.

## R15. Valkey

**Decision**: `valkey-server --maxmemory-policy noeviction --appendonly yes --appendfsync everysec --protected-mode no`, with `/data` on the named volume `valkey-data` (TR-7). An append-only file keeps fencing counters and namespace generations across a stop and start, to within one second, where a periodic snapshot could lose minutes of them and hand out a fencing token again. `--protected-mode no` is needed because Valkey refuses non-loopback clients when no password is set, and the planes connect over the Compose network. Valkey is reachable only on that network, and on loopback in the `dependencies` mode. No `maxmemory` is set, so nothing is evicted and a full workstation reports an error rather than dropping keys. Health check: `valkey-cli ping`.

## R16. Seed-free start-up

**Decision**: no service, script, or document seeds data or migrates at start (TR-10). The only state created at first start is R7's roles. A start on existing volumes skips the image's initialisation, reuses the password file, generates nothing, and exits 0 (US1.6). Smoke check 5 proves that no schema or relation exists.

## R17. Acceptance test

**Decision**: the tests live in `tests/Iskender.Container.Tests/LocalEnvironment/`, beside ISK-0007's container tests, so that they reuse `RequireDocker` and `DockerCli` and run in check 4 (FR-022). They add no new project, package, or CI check, so CI stays at 10 checks. **This validates AV-5.** They follow ISK-0005's rule through `RequireDocker`, and also need `bash` and `git` on `PATH`, with the same skip-or-fail rule.

The clean copy is made by `LocalCheckout`, a new fixture type: it copies every file `git ls-files -z` lists, with its working-tree content and executable bit, into a new temporary directory, then runs `git init` and one commit there, with a fixed test identity and signing off. It copies no ignored file, so no password file, data volume, build output, or local configuration. The copy is a real checkout whose `HEAD` is the image's revision, and a developer's uncommitted changes to the scripts are what the test runs. Each test sets `ISKENDER_LOCAL_PROJECT` to `iskender-local-t<12 hex>` and the port variables to free ports, and removes the project and its two image tags when it ends, even on failure.

| Test | Proves |
|---|---|
| `FreshCopyAcceptanceTests` | 5 consecutive cycles of start, smoke, and reset in one clean copy, each exiting 0, with no project container, network, or volume left after each reset (SC-001, SC-006). The first cycle also inspects services, digests, user, hardening, grace period, published ports, and the images' revision label (US1.1 to US1.5), and records every password, log, readiness answer, and script output for the value search (SC-004) |
| `RestartTests` | A second start with volumes kept reuses the passwords, generates nothing, and exits 0 (US1.6). The stop script keeps both volumes (Edge Cases) |
| `SmokeFailureTests` | `valkey` stopped, `postgres` stopped, and no running topology each make smoke exit 1, naming the check and the reason found (US2.4, US2.5, US2.8) |
| `ReadinessComparisonTests` | `lib/readiness.sh` given recorded answers, including one with persistence `ok`, fails each that differs and names the check (US2.6) |
| `ResetTests` | A control project's volume, labelled with an `isk-` project name, survives the reset (US4.2). A non-interactive reset without `--yes` removes nothing and exits 64 (US4.4). A volume with no password file makes start exit 2 (US4.5) |
| `DependenciesModeTests` | `start --dependencies` starts only `postgres` and `valkey` on loopback and builds no plane image. Each host's built assembly, run natively from the test's own build output with a copy of its native documents, `ISKENDER_ENVIRONMENT=development`, and the test's ports, free listener ports included, reports the same readiness body as the containers (US5.1, US5.2). A port in use makes start exit 2, naming its variable (US5.4) |
| `MigrationScriptTests` | `apply`, `status`, and `reverse` each exit 5 with event 7006 naming the operation and `persistence.unavailable`. An unknown operation exits 64 and starts no job. A default start runs no `migrate` container (US6) |
| `ScriptContractTests` | Each script's `--help` exits 0. Every usage error exits 64. Running from a subdirectory works, and a copy of a script outside a checkout exits 2. Every file under `local/` is LF in Git (`git ls-files --eol`), and every script is mode `100755` (FR-024, SC-008) |
| `DocumentTests` | In process, through the configuration engine with each plane's registrations: each base validates alone with no environment name. Base and overlay validate with `development`. The overlay's values as a base, with no environment name, are rejected. A name other than `development` reads no overlay. The control plane rejects the data-plane documents with `config.namespace.unknown`. Each native pair differs from its container pair only in R5's lines. No tracked file under `local/` holds a value other than a secret reference in a secret field (US3, SC-007) |

`DocumentTests`, `ScriptContractTests`, and `ReadinessComparisonTests` need no container runtime. The others do.

**Exit codes covered** (SC-008): 0 by every cycle; 1 by `SmokeFailureTests`; 2 by `ResetTests` and `DependenciesModeTests`; 64 by `ScriptContractTests` and `ResetTests`; and the migration job's 5 and the script's 64 by `MigrationScriptTests`.

## R18. Documentation

**Decision**: `docs/04-technology-stack-and-deployment.md` gains a `## Local development` section after `## Container entry points`. It covers the prerequisites and their minimum versions, the five scripts and their exit codes, the default topology as topology 1's developer distribution and why it is not one container, the `dependencies` mode and native runs, ports and override variables, the documents and where development-only values live, the password file, reset, the smoke assertions, the state before ISK-0021, and the settlement of ISK-0003 AV-6 and ISK-0007 AV-6. It links to this spec's `local-environment` contract. The wording of `## Deployable topologies` item 1 does not change (FR-003). `CONTRIBUTING.md` names the start and smoke scripts and their prerequisites under "Before you push", and `local/README.md` points to the `docs/04` section. `docs/03` is not changed, since it links no topology contract.

## R19. Compatibility

**Decision**: no `src/` file changes. The compatibility harness therefore reports no change to `hosting`, `core-contracts`, `providers-abstractions`, or `policy-sdk` (SC-010), and the module dependency rules contract stays at version 5 with no change, since `local/` holds no project. No container, persistence, coordination, configuration, or hosting rule changes (FR-021).
