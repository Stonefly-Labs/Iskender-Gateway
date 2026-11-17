# Feature Specification: ISK-0008 — Local Developer Environment & Reproducible Compose

**Feature Branch**: `feature/0008-local-developer-environment-reproducible-compose`

**Created**: 2026-11-16

**Status**: Draft

**Input**: User description: "Create the specification **ISK-0008 — Local Developer Environment & Reproducible Compose** for Iskender Gateway. Use the authoritative project inputs @docs/04-technology-stack-and-deployment.md @docs/13-development-standards-and-agent-workflows.md and the approved ADRs ADR-0002, ADR-0004, ADR-0007, ADR-0025; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Provide full opt-in local topology including Postgres/Redis and docs for scripts and tests**. In-scope behaviors and artifacts: **Compose files; all-in-one developer container; seed-free startup; environment safety; clean data reset; smoke scripts**. Explicitly exclude: **No fabricated cloud secrets or production SLA**. Prerequisites: **ISK-0004, ISK-0005, ISK-0007**; do not assume unavailable implementation already exists. ISK-0003 permits development-only configuration values only when the active environment is `development`, and rejects them under any other name or none, under the current configuration contract linked from the Deployment invariants section of @docs/04-technology-stack-and-deployment.md. The local topology works within that rule and settles that spec's assumption AV-6 for local use. ISK-0004 tests against PostgreSQL 17, the oldest major version it supports, and its operator runbook requires the `iskender_runtime` group role, a runtime login, and a separate migration login before the first migration, with `tlsMode: disable` permitted only in `development`. Follow the current persistence contract, linked from the Database migrations and rollback section of @docs/04-technology-stack-and-deployment.md: provision PostgreSQL that way, pin its image by digest as the ISK-0004 test fixture does, and apply migrations only through the operation ISK-0007 exposes. ISK-0004 ships no database credential provider outside test projects and forbids an interim development-only one, so until ISK-0021 provides one, the local gateway processes cannot connect or migrate and report not ready with `authentication_failed`. The smoke scripts assert that state rather than working around it. ISK-0005 tests Redis coordination against the official Valkey 8 image pinned by digest, permits a Redis without `redis.username` and `redis.password`, and `redis.tlsMode: disable`, only in `development`, and requires an eviction policy of `noeviction` or `volatile-*`. Follow the current coordination contract, linked from the Coordination section of @docs/04-technology-stack-and-deployment.md: run the Compose Redis within those rules, and note that no coordination credential provider exists until ISK-0021, so before then a Compose Redis that requires a password cannot be reached, and coordination on PostgreSQL is not ready for the same reason as the database. ISK-0007 built the data-plane and control-plane images, `src/Iskender.Gateway.Host/Dockerfile` and `src/Iskender.ControlPlane.Host/Dockerfile`, under the current container contract, linked from the Container entry points section of @docs/04-technology-stack-and-deployment.md. Each reads `iskender.yaml` or `iskender.json` from `/etc/iskender` or `ISKENDER_CONFIG_DIRECTORY`, its overlay only when `ISKENDER_ENVIRONMENT` names it, so `development` must be set there to permit development-only values, and the process's `ISKENDER__` variables. Each runs as `1654:1654` with a read-only root and a writable `/tmp`, needs a stop grace period above `shutdownDelay` + `shutdownTimeout` + 5 seconds, and answers readiness on a health port that should not be published. Build the developer Compose topology, and the all-in-one developer container of the Deployable topologies section of @docs/04-technology-stack-and-deployment.md, from those images, their configuration roots, and the control-plane image's `migrate` command, rather than from new entry points, keep them separate from ISK-0007's acceptance Compose fixture under `tests/`, and settle that contract's assumption AV-6. The all-in-one container keeps the planes in separate processes, as that contract's Entry points rule 3 requires, or records why it cannot. Before ISK-0021, every `migrate` operation exits 5 with `persistence.unavailable`, and each container reports persistence `authentication_failed`. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Fresh clone starts minimal topology, builds and tests with no external credentials**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify."

## Overview

The constitution is present at `.specify/memory/constitution.md` (version 1.1.0) and governs this spec.

Every cloud dependency must sit behind an interface with a local, Compose path (ADR-0007, Constitution II), and the Architecture gate requires that the local path builds without cloud credentials. ISK-0007 built the two plane images and an acceptance Compose fixture under `tests/`, but that fixture exists to prove failure diagnostics. It runs one container per failure scenario, takes its image tags and a per-run password from the test harness, and is not something a developer can start. There is no supported way to run Iskender with its dependencies on a workstation. ISK-0007 left its assumption AV-6 to this spec: that the developer topology, and `docs/04` topology 1, can be built from the two images, their configuration roots, and the `migrate` command, with no new entry point.

This spec gives developers:

1. **A developer Compose topology**: one command, from a fresh clone, builds both plane images from their ISK-0007 Dockerfiles and starts them with PostgreSQL 17 and Valkey 8. Both are pinned by the digests that the ISK-0004 and ISK-0005 fixtures use, and PostgreSQL is provisioned as the ISK-0004 runbook requires. The topology lives outside `tests/`, separate from ISK-0007's acceptance fixture.
2. **Opt-in profiles**: a dependencies-only profile for running the hosts natively with `dotnet run`, and a one-shot migration job that runs the control-plane image's `migrate` command.
3. **The all-in-one developer distribution of `docs/04` topology 1**, which is the default one-command start. The spec records why it is not a single container.
4. **Seed-free start-up**: the topology starts from empty volumes with no seed data, no migration at start, and no manual step.
5. **Environment safety**: development-only values live only in a `development` overlay, every published port binds to loopback, the health ports are never published, and no generated password is committed or printed.
6. **Clean data reset**: one command removes this topology's data volumes and generated passwords, and nothing else.
7. **Smoke scripts** that assert the topology's real state before ISK-0021. Both planes are live. Each is not ready only because persistence reports `authentication_failed`, while coordination on the open, development-only Valkey is `ok`, and `migrate` exits 5 with `persistence.unavailable`. They assert that state rather than working around it.
8. **Documentation** for the scripts, profiles, and tests, in `docs/04` and the contributor guide.

This spec adds no entry point, image, gateway endpoint, provider, credential provider, product table, seed data, or cloud resource, and changes no ISK-0003 to ISK-0007 contract rule. Until ISK-0021 provides a credential provider, no local gateway process can connect to PostgreSQL. This spec reports that state accurately rather than working around it, and adds no interim development-only provider, which ISK-0004 forbids.

Image, service, profile, script, variable, and path names appear in this spec because they are its subject, not a choice of implementation.

## Decision Classification

### Accepted decisions (already approved; this spec applies them)

| Decision | Source |
|---|---|
| Cloud dependencies sit behind interfaces with a Compose/local path, never a lock-in. The local/Compose path builds without cloud credentials | ADR-0007, Constitution II and its gate |
| Iskender ships as standard OCI containers and as the embedded library, with the same pipeline contracts | ADR-0004, Constitution I and II |
| .NET 10 and ASP.NET Core are the runtime. Local development uses native .NET plus containers for dependencies, with Docker Compose (recommended implementation of the approved local support) | ADR-0002, `docs/04` Choices |
| Topology 1 is an all-in-one developer distribution of the control API, the data-plane host, and an optional UI, and is not proof of production scalability. Local Compose can run the full basic topology | `docs/04` Deployable topologies 1 and 5 |
| Development-only values are permitted only when the environment name is `development`, and rejected under any other name or none. Dev-only defaults cannot appear in production manifests | ISK-0003 configuration contract, `docs/04` Deployment invariants |
| Each plane image reads `iskender.yaml` or `iskender.json` from `/etc/iskender` or `ISKENDER_CONFIG_DIRECTORY`, its overlay only when `ISKENDER_ENVIRONMENT` names it, then `ISKENDER__` variables, with ISK-0003's precedence and no further layer. Each runs as `1654:1654` with a read-only root, a writable `/tmp`, `cap_drop: ALL`, and `no-new-privileges`. Its grace period must exceed `shutdownDelay` + `shutdownTimeout` + 5 seconds. Its health port should not be published | Container contract version 1, Configuration roots, Images, Lifecycle rule 4, Listeners rule 7 |
| The planes run as separate processes that share no process, file, or in-memory state (Entry points rule 3). Arguments given to a container reach the entry point's command line, and the control-plane image runs `migrate apply`, `migrate reverse`, and `migrate status` as a one-shot job | Container contract, Entry points, Images rule 2, Migration command |
| PostgreSQL 17 is the tested and oldest supported major version. Before the first migration, provision `iskender_runtime` (`NOLOGIN`, `NOBYPASSRLS`), a runtime login that is a member of it (`NOSUPERUSER`, `NOBYPASSRLS`, no `CREATE` on the database), and a migration login with `CREATE` on the database and nothing more. `tlsMode: disable` is development-only. Migrations run only as the explicit ISK-0007 operation | Persistence contract version 2, `docs/04` Database migrations and rollback |
| A Redis without `redis.username` and `redis.password`, and `redis.tlsMode: disable`, are permitted only in `development`. Redis runs with `maxmemory-policy` `noeviction` or `volatile-*`. Valkey 8 is the tested server | Coordination contract, Credentials rule 2, Known limits; `docs/04` Coordination |
| No database or coordination credential provider ships outside test projects until ISK-0021, and no interim development-only provider is added. Until then a process cannot connect to PostgreSQL: the probe reports `authentication_failed`, every `migrate` operation exits 5 with `persistence.unavailable`, a Redis that requires a password cannot be reached, and coordination on PostgreSQL is not ready for the same reason | ISK-0004 TR-14, persistence and coordination contracts, Credentials; container contract, Migration command rule 7 |
| ISK-0007's acceptance Compose fixture under `tests/` stays separate from the developer topology | Container contract, Test fixtures rule 1 |
| Tests that need a container runtime are skipped, each with its reason, without one, and fail when `CI` is `true` | ISK-0005's rule, container contract Test fixtures rule 4 |
| Secrets are never committed. CI is secret-free, with reproducible local commands and no live secret dependencies | Constitution V and IX, `docs/04` CI and supply chain, `docs/13` Tests |
| No SLO, latency, throughput, RPO, or RTO figure is asserted without a reviewer-approved baseline | Constitution VIII |
| One stage runs per `/drive-speckit` invocation, and `specify` produces the specification only | ADR-0025, Constitution X |

### Maintainer decisions taken in `specify`

| Decision | Recorded in |
|---|---|
| **All-in-one container** (Q1): no single container. The default one-command start of the developer Compose topology is `docs/04` topology 1's all-in-one developer distribution. It runs both unchanged plane images, each its own process in its own container, with their dependencies. The spec records why a single container cannot keep the planes in separate processes under the current contracts. Any change to the wording of `docs/04` Deployable topologies 1 goes through `/review-roadmap` | TR-3 |
| **Local database passwords** (Q2): generated per checkout. On first start, the start script generates random passwords for the PostgreSQL superuser, the runtime login, and the migration login, and stores them in a gitignored file. Nothing secret is committed. A reset removes the file with the data, and the next start generates new passwords | TR-6 |
| **Default service set** (Q3): by default, a start brings up PostgreSQL 17, Valkey 8 (open, development only), and both plane containers. The opt-in profiles are dependencies-only, for native `dotnet run`, and the one-shot migration job | TR-2 |
| **The all-in-one form** (Q4): the default start *is* topology 1's developer distribution. There is no separate `all-in-one` profile | TR-2, TR-3 |

### Technical recommendations (proposed by this spec; reviewed in `clarify`)

| ID | Recommendation | Why | Alternative rejected |
|---|---|---|---|
| TR-1 | **Location and separation**: the developer topology, its configuration documents, its PostgreSQL provisioning script, and its scripts live in one directory outside `src/` and `tests/`, chosen by the plan. Its Compose project name is `iskender-local`, which an override variable may change only to `iskender-local-` followed by 1 to 24 characters of `a-z` and `0-9`, so it is always distinct from every project name ISK-0007's fixture uses (maintainer decision in `plan`, research R9). It shares no file, service definition, or configuration document with `tests/Iskender.Container.Tests/compose/`, and neither one includes the other | The two serve different readers: the fixture proves failure diagnostics per scenario, and the topology is something a developer runs. Keeping them apart keeps a change to one from silently changing the other's evidence | Reusing the ISK-0007 fixture with profiles, which couples acceptance evidence to developer convenience |
| TR-2 | **Services and profiles** (decided in `specify`, Q3 and Q4). Default, with no profile: `postgres`, `valkey`, `data-plane`, and `control-plane`. Mode `dependencies`, which is not a Compose profile because a profile only adds services (research R2): `postgres` and `valkey` only, with their ports published on loopback, for hosts run natively. Profile `migrate`: a one-shot service from the control-plane image, with the control plane's documents, running `migrate apply`, or another `migrate` operation the developer names. No service runs `migrate` by default, and no plane depends on the migration job. Both planes start after `postgres` and `valkey` report healthy, and neither depends on the other | A plain start gives the full basic topology (`docs/04` topology 5), and the profiles serve native .NET development and explicit migration without changing the default | Running migrations as a start dependency, which ISK-0004 forbids and which fails before ISK-0021 anyway |
| TR-3 | **All-in-one developer distribution** (decided in `specify`, Q1 and Q4). The default start is topology 1's developer distribution. It is not one container, for these reasons, which the spec records as AV-6's settlement. Entry points rule 3 requires the planes in separate processes. One container running two processes needs a process supervisor as its entry point, and that would be a new entry point, which the approved prompt excludes. The chiseled runtime image has no shell, init, or supervisor, and Images rule 2 forbids adding a shell or package manager. A separate development image on another base would be a second, unreviewed build path. The optional UI is left out, because no management UI exists yet | Keeps the plane separation and the image rules intact, with the same one-command experience | A development image with a supervisor, which adds an entry point, a shell, and a third-party licence review |
| TR-4 | **Images**: the start script builds both images from `src/Iskender.Gateway.Host/Dockerfile` and `src/Iskender.ControlPlane.Host/Dockerfile`, from the repository root, with `REVISION` set to the checkout's commit and fixed local tags that the plan chooses. It changes neither Dockerfile, and adds no Dockerfile. `postgres` uses `postgres:17-bookworm@sha256:665dc7b37ac52a17e298511414c21d23ffa4e6a6ef6225b3e5d59c7afb35e994`, and `valkey` uses `valkey/valkey:8.1.11@sha256:8f836008e0986c36adf50da714c7060cf689dbb335f61fe077939d90fa650809`, the digests of the ISK-0004 and ISK-0005 fixtures. A digest changes only with those fixtures, through a revision of this spec's contract | The topology runs exactly what CI tests, built from the current checkout | Pulling published images, which ISK-0007 does not publish; tags without digests, which move |
| TR-5 | **Hardening and lifecycle**: each plane service runs as the images do, with `read_only`, a `tmpfs` at `/tmp`, `cap_drop: ALL`, `no-new-privileges`, and a `stop_grace_period` above `shutdownDelay` + `shutdownTimeout` + 5 seconds for the `container` section values its documents set (40 seconds with the defaults, as in ISK-0007's fixture). The topology sets no `user` override, no `privileged`, no host network, and no added capability | The developer topology must run the images under the same rules as production, or it proves nothing about them | Relaxed settings for convenience, which hide real failures |
| TR-6 | **PostgreSQL provisioning and passwords** (decided in `specify`, Q2). On the first start, with an empty data volume, a provisioning script that the official image runs at initialisation creates the database, `iskender_runtime` with `NOLOGIN` and `NOBYPASSRLS`, a runtime login that is a member of it with `NOSUPERUSER`, `NOBYPASSRLS`, and no `CREATE` on the database, and a migration login with `CREATE` on the database and nothing more, as the ISK-0004 runbook requires. The start script generates the three passwords from a cryptographic random source when its gitignored password file is absent, writes the file readable only by its owner, and passes the passwords to `postgres` only. It never prints them, and no plane, log, or smoke output holds them. The plane documents name the passwords only as secret references, which no credential provider resolves before ISK-0021 | Provisions exactly the identities the runbook names, so ISK-0021 can make the topology ready by adding a provider, with no new role. Generated passwords mean no credential is committed, and none is fabricated | A committed well-known password; trust authentication, which skips the identity model |
| TR-7 | **Valkey**: `valkey` runs with `maxmemory-policy noeviction`, with no password, no ACL user, and no TLS, which only `development` permits. It persists its data, in a mode the plan chooses, to its own named volume, which the stop script keeps and the reset script removes. It is reachable only on the Compose network, and on loopback in the `dependencies` mode | An open Redis is the only Redis a process can reach before ISK-0021, and `noeviction` meets the coordination contract | A password-protected Valkey, which no process can reach before ISK-0021 |
| TR-8 | **Configuration documents and environment safety** (settles ISK-0003 AV-6 for local use). Each plane gets its own directory, mounted read-only at `/etc/iskender`, holding a base `iskender.yaml` and a `development` overlay `iskender.development.yaml`. Each plane service sets `ISKENDER_ENVIRONMENT=development`. The base holds no development-only value, so it validates as production-grade. The development-only values (`persistence.tlsMode: disable`, `coordination.redis.tlsMode: disable`, and the open `redis` endpoint) live only in the overlay. The overlay selects `redis` for the `atomic`, `leases`, and `cache` primitives, and leaves `coordination.topology` at its default. Neither document holds a password, only secret references. The precedence of defaults, base, overlay, and environment variables is enough, with no further layer. A developer runs a host natively with `ISKENDER_CONFIG_DIRECTORY` pointing at that plane's tracked native documents and `ISKENDER_ENVIRONMENT=development`, against the `dependencies` mode's loopback ports. The native documents are the plane's documents with only the host names and ports changed, because a Redis endpoint is an array, which no `ISKENDER__` variable can set (maintainer decision in `plan`, research R5) | Confines every relaxation to a file that loads only in `development`, so the same base cannot carry one into another environment. It settles AV-6 with ISK-0003's own precedence | Development values in the base, which an unset environment name would then reject, or would carry into a production copy |
| TR-9 | **Published ports**: only the data plane's traffic port is published, on `127.0.0.1` only. The control plane publishes no port, because it binds only its health listener. No health port is ever published. In the `dependencies` mode, `postgres` and `valkey` publish on `127.0.0.1` only. Each published host port has a default that the plan chooses, and an override variable | Nothing in the developer topology is reachable from another machine, and health reasons stay off every published port, as the container contract requires | Publishing on all interfaces, which exposes an unauthenticated database and Redis to the local network |
| TR-10 | **Seed-free start-up**: a start needs no step beyond the one command, creates no row, applies no migration, and loads no seed or sample data. A start on empty volumes and a start on existing volumes both work. The only state created at first start is the provisioning of TR-6 | No product tables exist, and migrations are an explicit operation, so a start that seeds or migrates would break ISK-0004's rules | A seeding job, which has nothing to seed and hides the migration boundary |
| TR-11 | **Scripts**: POSIX shell scripts (`bash`), alongside the topology, each with a fixed name, `--help`, and the exit codes of TR-12. **start** checks the prerequisites, generates the passwords if absent, builds both images, and starts the default services, or the `dependencies` mode with an option. It exits 0 only when, within a bound the plan chooses, `postgres` and `valkey` report healthy and, in the default start, each plane's image `health live` command exits 0. It does not wait for readiness, which is `503` before ISK-0021 and is the smoke script's to assert. **stop** stops the topology and keeps its data. **reset** stops it and removes its data volumes and its password file, after confirmation, or with `--yes`. **migrate** runs the `migrate` profile with an operation, `status` by default, and passes through its exit code. **smoke** asserts the state of TR-13 against a running topology. Each script works from any working directory inside the checkout, and writes only value-free output | Scripts give one documented entry per task, matching the repository's existing `bash` scripts | Compose commands typed by hand, which drift from the docs; a `Makefile`, which adds a tool |
| TR-12 | **Script exit codes**: 0 success, or every smoke assertion passed; 1 a smoke assertion failed, or an underlying Compose or build step failed; 2 a prerequisite is missing or unusable (no container runtime, no Compose, the runtime not running, a published port already in use, or a `postgres` data volume whose password file was removed); 64 a usage error. `migrate` exits with the `migrate` command's own code (0, 2, 3, 5, 6, 7, 64). Each failure names the failed check or prerequisite, and no value | Developers and the acceptance test read exit codes. One code per class says which runbook entry to read | A single non-zero code |
| TR-13 | **Smoke assertions, before ISK-0021**: for each plane, the container is running, and the image's own `health live` command exits 0. A readiness request from an in-network probe answers `503`, with the checks `lifecycle` `started`, `persistence` `authentication_failed`, `coordination` `ok`, `single_instance` `unverified`, and `capability` `none_refused`, and an empty `refused`. `migrate status` through the `migrate` profile exits 5, and its event 7006 names `persistence.unavailable`. `postgres` reports healthy and lists the three roles of TR-6 with their attributes, and `valkey` answers with `maxmemory-policy` `noeviction`. No plane log line, readiness answer, or smoke output holds a generated password, and no plane log holds a configuration value. Any other state fails the smoke run, a persistence reason of `ok` included, so ISK-0021 must change the expected state when it makes the topology ready | Asserts the real state rather than working around it, as the approved prompt requires, and makes any change to that state visible | Smoke checks that only test liveness, which would pass while readiness silently changed |
| TR-14 | **In-network readiness probe**: because no health port is published, the smoke script reads `/health/ready` from a one-shot probe container on the Compose network, from `curlimages/curl:8.17.0` pinned by digest (maintainer decision in `plan`, research R14), which publishes nothing and exits after the request | Keeps the health port unpublished while still reading reasons, which the `health ready` command's exit code alone does not give | Publishing the health port on loopback, which the container contract advises against |
| TR-15 | **Clean data reset**: the reset removes exactly this topology's named volumes, its containers and network, and its password file, all found by its Compose project name (TR-1). It removes no image, no volume of another Compose project, ISK-0007's fixture included, and no file outside the topology's directory. Without `--yes` it asks for confirmation, and refuses in a non-interactive shell. The next start is a fresh first start | A reset that can reach another project's data is unsafe | `docker system prune` or deleting volumes by pattern, which can remove unrelated data |
| TR-16 | **`local-environment` contract**: a new versioned contract, `local-environment`, version 1, in this spec's `contracts/` directory, owning the services, profiles, images and digests, published ports and their override variables, volumes, the Compose project name, the configuration document layout, the provisioning rules, the scripts and their exit codes, the smoke assertions, and the acceptance test. The container contract is not revised, because this spec changes none of its rules. The settlement of its AV-6 is recorded here and in `docs/04` | Developer docs, scripts, and the acceptance test depend on these as an interface, and ISK-0021 and later specs change them through a revision | Undocumented scripts that later specs change without review |
| TR-17 | **Acceptance test**: a test, in a test project the plan chooses under `tests/`, makes a clean copy of the checkout's tracked files only, with no generated password file, data volume, build output, or local configuration. It runs **start**, then **smoke**, then **reset**, and asserts that each exits 0 and that no volume of the project remains. It runs the `dependencies` mode once as well. It follows ISK-0005's container-runtime rule, runs inside the existing `dotnet test` check, and needs no credential beyond anonymous package and image downloads | It is the approved acceptance evidence: a fresh clone starts the topology with no external credentials | A manual checklist, which is not evidence |

### Assumptions needing validation

| ID | Assumption | Validated by |
|---|---|---|
| AV-1 | The pinned PostgreSQL and Valkey digests are multi-platform index digests that run on `linux/amd64` and `linux/arm64` developer machines, and the plane images build for the developer's own architecture | This spec's plan: **validated** (research R3) |
| AV-2 | One minimum Docker Engine and Compose v2 version, which the plan records, supports the profiles, health-gated start order, and one-shot services this spec needs, on Linux, macOS, and Windows with WSL 2 | This spec's plan: **validated**, Compose 2.24.0 with Docker Engine 24.0 (research R12) |
| AV-3 | The scripts run under the `bash` that ships with macOS (3.2), with current Linux distributions, and with WSL 2, using only tools present there by default plus the container runtime | This spec's plan: **validated** by design, and proved by the tests under macOS `bash` 3.2 and CI's `bash` 5 (research R12) |
| AV-4 | The readiness probe image the plan selects is pinned by digest, runs as non-root, and has a licence that permits use in an MIT project | This spec's plan, Product/Charter owner: **validated**, `curlimages/curl` decided by the maintainer (research R14) |
| AV-5 | The acceptance test can run in the existing `dotnet test` check alongside ISK-0007's container tests, so no new CI check is needed, and CI stays at 10 checks | This spec's plan: **validated** (research R17) |
| AV-6 | ISK-0021 can register its credential providers so that the planes resolve the passwords this topology generates, with no new service, role, or document layout, after which the smoke script's expected persistence reason becomes `ok` and readiness `200` | ISK-0021 |
| AV-7 | ISK-0009's maintenance work can join the topology as another control-plane command service, under a revision of the `local-environment` contract | ISK-0009 |
| AV-8 | The management UI, when specified, can join the topology as an optional service, completing topology 1's "optional UI" | The management UI spec |

**ISK-0003's AV-6 is settled for local use by this spec** (TR-8).

**ISK-0007's AV-6 is settled by this spec** (TR-2, TR-3, TR-4): the developer topology and topology 1's developer distribution are built from the two images, their configuration roots, and the `migrate` command, with no new entry point, and topology 1 is not a single container, for the reasons TR-3 records.

### Post-MVP work (named so that it is not designed here)

A single all-in-one container image with a process supervisor. A local Kubernetes topology. Native Windows scripts without WSL. A local observability stack, such as an OpenTelemetry collector and trace viewer. TLS between local services. Seed or sample data, which waits for product tables and their own specs. Hot reload of configuration (ISK-0057).

## Clarifications

### Session 2026-11-16 (specify)

- Q: The planes must run as separate processes, and the images have no shell or init. How should the all-in-one developer container be provided? → A: Record why it cannot be one container. One Compose command runs both unchanged plane images with their dependencies, and any change to `docs/04` topology 1's wording goes through `/review-roadmap` (TR-3).
- Q: Where should the local PostgreSQL passwords come from? → A: Generated per checkout on first start into a gitignored file. Nothing secret is committed, and a reset regenerates them (TR-6).
- Q: What does a plain start bring up, and what is opt-in? → A: PostgreSQL 17, Valkey 8 (open, development only), and both planes by default. The opt-in profiles are dependencies-only and the migration job (TR-2). The dependencies-only start became the `dependencies` mode in `plan`, because a Compose profile cannot express it (research R2).
- Q: Is there a separate `all-in-one` profile, given that the default start already holds both planes and their dependencies? → A: No. The default start is topology 1's developer distribution (TR-2, TR-3).

### Session 2026-11-16 (clarify)

- Q: When should the start script exit 0 for the default topology? → A: When the planes are live. It waits, up to a bound the plan chooses, until `postgres` and `valkey` report healthy and each plane's image `health live` command exits 0. A plane that exits, or is not live within the bound, makes it exit 1, naming the service. Readiness is left to the smoke script (TR-11).
- Q: Should Valkey keep its data on a named volume across stop and start? → A: Yes. `valkey` persists to its own named volume, which the stop script keeps and the reset script removes, like the `postgres` data volume (TR-7, TR-15).

### Session 2026-11-16 (plan)

- Q: A Redis endpoint is an array, and ISK-0003 rejects an `ISKENDER__` variable that sets an array, so TR-8's overrides cannot point a native host at Valkey. How should native runs reach it? → A: Tracked native documents per plane, the same as the plane's documents except for host names and ports. A PostgreSQL port override still works through `ISKENDER__PERSISTENCE__PORT`, and a Valkey port override needs an edited copy (TR-8, FR-014, US5.2; research R5).
- Q: Which image should the smoke script's in-network readiness probe use? → A: `curlimages/curl:8.17.0`, pinned by its multi-platform index digest, non-root, MIT-labelled, under the curl licence (TR-14, AV-4; research R14).
- Q: How should the acceptance test run beside a developer's own topology? → A: A restricted project-name override: `iskender-local` or `iskender-local-` with 1 to 24 characters of `a-z0-9`, so no override can name an ISK-0007 project. Image tags and the password file follow the name (TR-1, TR-15, FR-001, FR-020; research R9).
- Correction, no choice left: the control plane registers no section of its own, so only the data-plane documents are rejected by the other plane (US3.6; research R6).

### Session 2026-11-17 (implement)

- Q: Both running planes answer `single_instance` `unverified`, not the `not_applicable` that TR-13 and US2.2 state, because the single-instance guard is never started under `multi_replica` and readiness reads its initial state; the container contract says `not_applicable`. How should the smoke expectation proceed? → A: Record the real bytes (`unverified`) in TR-13, US2.2, the contract, and research R13, change no `src/` file, and raise the code-versus-contract mismatch through `/review-roadmap` for a later fix (maintainer decision).

## User Scenarios & Testing *(mandatory)*

The users of this feature are contributors who build and test Iskender on a workstation, developers who run a host natively against real dependencies, reviewers who need evidence that the local path needs no credentials, and authors of later specs, such as ISK-0009 and ISK-0021, who extend the topology.

### User Story 1 - A fresh clone starts the topology with one command and no credentials (Priority: P1)

A contributor clones the repository, runs the start script, and gets PostgreSQL, Valkey, and both planes running, built from the checkout, with no account, credential, or manual step.

**Why this priority**: It is this spec's acceptance evidence, and the Architecture gate's local-path requirement.

**Independent Test**: In a clean copy of the tracked files, with a container runtime and no credentials, run the start script, then inspect the services, their images, and their settings.

**Acceptance Scenarios**:

1. **Given** a clean copy of the tracked files and a running container runtime, **When** the start script runs with no options, **Then** it builds both images from their ISK-0007 Dockerfiles, starts `postgres`, `valkey`, `data-plane`, and `control-plane`, waits until `postgres` and `valkey` are healthy and each plane's `health live` command exits 0, and exits 0.
2. **Given** the start, **When** the environment is inspected, **Then** no cloud account, registry login, API key, or credential was needed beyond anonymous package and image downloads.
3. **Given** the running topology, **When** the services are inspected, **Then** `postgres` and `valkey` run the pinned digests, each plane runs as `1654:1654` with a read-only root, a `/tmp` `tmpfs`, no capability, `no-new-privileges`, and a 40-second grace period, and no service is privileged or on the host network.
4. **Given** the running topology, **When** its published ports are listed, **Then** only the data plane's traffic port is published, on `127.0.0.1` only, and no health port is published.
5. **Given** the first start, **When** PostgreSQL is inspected, **Then** `iskender_runtime`, the runtime login, and the migration login exist with the attributes of TR-6, and no other table, row, or migration history exists.
6. **Given** a second start with the volumes kept, **When** it runs, **Then** it reuses the existing passwords and data, generates nothing new, and exits 0.
7. **Given** a clean copy of the tracked files, **When** the repository's documented build and test commands run, **Then** they pass with no external credentials, as they do today.

---

### User Story 2 - Smoke scripts assert the real state before ISK-0021 (Priority: P1)

A contributor runs the smoke script against the running topology. It confirms that both planes are live, that each is not ready only because no credential provider exists, that coordination on Valkey works, and that migration cannot connect. Anything else fails.

**Why this priority**: The approved prompt requires the scripts to assert this state rather than work around it, and a smoke run that passes on a broken topology is worse than none.

**Independent Test**: Run the smoke script against the default topology, then against topologies with one service stopped, a changed document, and the `dependencies` mode, and check its exit code and output.

**Acceptance Scenarios**:

1. **Given** the default topology, **When** the smoke script runs, **Then** every assertion of TR-13 passes and it exits 0.
2. **Given** each plane, **When** the smoke script reads readiness through the in-network probe, **Then** the answer is `503` with `persistence` `authentication_failed`, `coordination` `ok`, `lifecycle` `started`, `single_instance` `unverified`, and `capability` `none_refused`.
3. **Given** the `migrate` profile, **When** `migrate status` runs, **Then** it exits 5, and its event 7006 names `persistence.unavailable`.
4. **Given** `valkey` stopped, **When** the smoke script runs, **Then** it exits 1 and names the failed coordination assertion and the reason it found, such as `unreachable`.
5. **Given** `postgres` stopped, **When** the smoke script runs, **Then** it exits 1 and names the persistence assertion and the reason it found, such as `unreachable`.
6. **Given** a plane whose readiness reports a persistence reason of `ok`, as after ISK-0021, **When** the smoke script runs, **Then** it exits 1, so the change cannot pass unnoticed.
7. **Given** a smoke run, **When** its output and the plane logs are searched, **Then** no generated password and no configuration value appears.
8. **Given** no running topology, **When** the smoke script runs, **Then** it exits 1, naming the services that are not running.

---

### User Story 3 - Development-only values stay in development (Priority: P1)

A reviewer checks that every relaxation the topology needs, such as plain TCP to PostgreSQL and an open Redis, is permitted only because `ISKENDER_ENVIRONMENT` is `development`, and that nothing in the topology can carry one into another environment.

**Why this priority**: `docs/04` forbids dev-only defaults in production manifests, and the developer files are the ones most likely to be copied.

**Independent Test**: Validate each plane's base document alone, with no environment name, then the overlay values with the environment name unset or changed, and inspect the published ports and files.

**Acceptance Scenarios**:

1. **Given** each plane's base document alone, with no environment name, **When** it is validated, **Then** it is accepted, and it holds no development-only value.
2. **Given** a plane service with `ISKENDER_ENVIRONMENT` unset, **When** it starts with the base and the overlay's values given as its base, **Then** the configuration is rejected with an error code, which ISK-0007's container contract already turns into exit code 2.
3. **Given** `ISKENDER_ENVIRONMENT` set to any name other than `development`, **When** a plane starts, **Then** the `development` overlay is not read, so no development-only value applies.
4. **Given** the tracked files, **When** they are searched, **Then** no password, token, or credential value appears in them, only secret references, and the password file is ignored by Git.
5. **Given** the generated password file, **When** its permissions are read, **Then** only its owner can read or write it.
6. **Given** each document, **When** it is read, **Then** it names only sections its own plane registers. The data-plane documents name `hosting`, so the control plane rejects them with `config.namespace.unknown`. The control plane registers no section of its own, so the data plane would accept its documents, which are kept separate so that the data plane never holds the migration login's references (research R6).

---

### User Story 4 - A contributor resets local data cleanly (Priority: P2)

A contributor runs the reset script to return to a fresh first start, without touching other Compose projects or anything outside the topology.

**Why this priority**: A safe reset makes the first-start path repeatable. It is less urgent than starting and checking the topology.

**Independent Test**: With this topology and an unrelated Compose project both holding volumes, run the reset script, then list volumes and files, and start again.

**Acceptance Scenarios**:

1. **Given** a running topology with data, **When** the reset script runs with `--yes`, **Then** this project's containers, network, and named volumes are removed, the password file is removed, and it exits 0.
2. **Given** an unrelated Compose project with volumes, ISK-0007's fixture included, **When** the reset runs, **Then** none of its containers, volumes, or images is removed.
3. **Given** a reset, **When** the start script runs again, **Then** it is a fresh first start: new passwords are generated, and the roles are provisioned again on an empty volume.
4. **Given** a non-interactive shell and no `--yes`, **When** the reset script runs, **Then** it removes nothing and exits 64.
5. **Given** a `postgres` data volume that exists while the password file is missing, **When** the start script runs, **Then** it exits 2 and tells the contributor to run the reset, rather than generate passwords that the provisioned roles do not have.

---

### User Story 5 - A developer runs a host natively against containerized dependencies (Priority: P2)

A developer starts only PostgreSQL and Valkey and runs a host from source with `dotnet run`, pointing it at the topology's documents.

**Why this priority**: `docs/04` recommends native .NET with containers for dependencies, and it is the fastest edit-run loop. The default topology already proves the images.

**Independent Test**: Start the `dependencies` mode, run each host natively with the documented variables, and read its readiness.

**Acceptance Scenarios**:

1. **Given** the start script with the dependencies option, **When** it runs, **Then** only `postgres` and `valkey` start, published on `127.0.0.1` only, and no plane image is built.
2. **Given** a host run natively with `ISKENDER_CONFIG_DIRECTORY` naming its plane's native documents and `ISKENDER_ENVIRONMENT=development`, **When** its readiness is read, **Then** it reports the same checks and reasons as the containerized plane.
3. **Given** a host run natively with `ISKENDER_ENVIRONMENT` unset, **When** it starts, **Then** the overlay is not read and the development-only values do not apply.
4. **Given** a published port already in use on the workstation, **When** the start script runs, **Then** it fails, naming the port's override variable, and setting that variable lets the start succeed.

---

### User Story 6 - A contributor runs a migration job through the control-plane image (Priority: P3)

A contributor runs the migration script, which runs the control-plane image's `migrate` command as a one-shot job against the topology. Before ISK-0021 it reports, accurately, that it cannot connect.

**Why this priority**: It proves the migration path is wired with no new entry point, but it can do nothing useful until ISK-0021.

**Independent Test**: Run the migration script with each operation, and check its exit code and the job's events.

**Acceptance Scenarios**:

1. **Given** the running topology, **When** the migration script runs `apply`, **Then** the job runs from the control-plane image with the control plane's documents, exits 5 with `persistence.unavailable`, and the script exits 5.
2. **Given** `status` and `reverse`, **When** each runs, **Then** each exits 5, and event 7006 names the operation and `persistence.unavailable`.
3. **Given** an unknown operation, **When** the migration script runs, **Then** it exits 64 without starting the job.
4. **Given** a default start, **When** the services are listed, **Then** no migration job ran, and no plane depends on one.

---

### Edge Cases

- No container runtime is installed, or it is not running: every script that needs it exits 2, naming the missing prerequisite, and changes nothing.
- The Compose version is older than the minimum the plan records: the start script exits 2, naming the version found and the version required.
- A script runs from a subdirectory of the checkout: it finds the repository root and runs as from the root. Outside a checkout, it exits 2.
- A script runs from a Windows checkout with CRLF line endings: the scripts, documents, and provisioning script are kept with LF endings in Git, so they run unchanged.
- ISK-0007's acceptance fixture runs at the same time: the two use different project names and publish no conflicting fixed port, so neither affects the other.
- The image build fails, for example with no network access to the package source: the start script exits 1, naming the failed build, and starts nothing.
- A plane container exits on start, for example with a rejected document, or is not live within the start script's bound: the start script reports the service, and its exit code if it exited, and exits 1.
- The password file exists but the `postgres` data volume was removed outside the reset script: the start provisions a fresh volume with the existing passwords, which is a valid first start.
- `ISKENDER_ENVIRONMENT` is set in the developer's shell: the topology sets each plane's environment explicitly, so the shell's value does not reach the containers.
- The smoke script runs while a plane is still `starting`: it waits, up to a bound the plan chooses, for `lifecycle` to leave `starting` before asserting, and then fails if any check differs.
- The stop script runs: each plane gets its grace period, exits 0, and the `postgres` and `valkey` data volumes are kept.
- The workstation is `arm64`: the plane images build for `arm64`, and the pinned dependency images run if AV-1 holds.

## Requirements *(mandatory)*

### Functional Requirements

**Topology and separation**

- **FR-001**: The developer topology MUST live in one directory outside `src/` and `tests/`, with the Compose project name `iskender-local`, or an override matching `iskender-local-[a-z0-9]{1,24}`, so that it is distinct from every project name ISK-0007's fixture uses, and MUST share no file, service definition, or configuration document with `tests/Iskender.Container.Tests/compose/` (TR-1).
- **FR-002**: A start with no profile MUST bring up `postgres`, `valkey`, `data-plane`, and `control-plane`. The `dependencies` mode MUST bring up `postgres` and `valkey` only. The `migrate` profile MUST run one `migrate` operation from the control-plane image and exit. No service MUST run `migrate` by default, and no plane MUST depend on the migration job or on the other plane (TR-2).
- **FR-003**: The default start MUST be documented as `docs/04` topology 1's all-in-one developer distribution, with each plane its own process in its own container. The spec's reasons why it is not one container MUST be recorded in the `local-environment` contract and `docs/04`. Any change to the wording of `docs/04` Deployable topologies 1 MUST go through `/review-roadmap` (TR-3).
- **FR-004**: The topology MUST add no entry point, executable, Dockerfile, or image definition, and MUST change neither ISK-0007 Dockerfile.

**Images and hardening**

- **FR-005**: The start script MUST build both plane images from their ISK-0007 Dockerfiles from the repository root, with the checkout's commit as `REVISION`. `postgres` and `valkey` MUST use the digests of TR-4 (TR-4).
- **FR-006**: Each plane service MUST run with `read_only`, a `tmpfs` at `/tmp`, `cap_drop: ALL`, `no-new-privileges`, no `user` override, no privilege, no host network, and a `stop_grace_period` above `shutdownDelay` + `shutdownTimeout` + 5 seconds for the values its documents set (TR-5).

**PostgreSQL, Valkey, and passwords**

- **FR-007**: On a first start with an empty data volume, the topology MUST provision the database, `iskender_runtime`, the runtime login, and the migration login with exactly the attributes of TR-6, and MUST create no table, row, or migration history.
- **FR-008**: The start script MUST generate the superuser, runtime, and migration passwords from a cryptographic random source when its password file is absent, MUST write that file readable and writable only by its owner, in a path Git ignores, and MUST give the passwords to `postgres` only. No script, log, readiness answer, or document MUST hold a password value (TR-6).
- **FR-009**: When the `postgres` data volume exists and the password file does not, the start script MUST exit 2 and direct the contributor to the reset script, generating nothing.
- **FR-010**: `valkey` MUST run with `maxmemory-policy noeviction`, with no password, no ACL user, and no TLS, and MUST persist its data to its own named volume, kept by the stop script and removed by the reset script (TR-7).

**Configuration and environment safety**

- **FR-011**: Each plane MUST have its own configuration directory, mounted read-only, holding a base `iskender.yaml` with no development-only value and a `development` overlay `iskender.development.yaml` with every development-only value the topology needs. Each plane service MUST set `ISKENDER_ENVIRONMENT=development` explicitly (TR-8).
- **FR-012**: Each base document MUST validate, alone and with no environment name, as production-grade, and each document MUST hold only its own plane's sections and secret references, never a secret value.
- **FR-013**: The overlays MUST select `redis` for the `atomic`, `leases`, and `cache` primitives against `valkey`, and MUST leave `coordination.topology` at its default (TR-8).
- **FR-014**: The topology MUST use ISK-0003's precedence of defaults, base, overlay, and environment variables, with no further layer, and MUST provide, per plane, tracked native documents that differ from the plane's documents only in host names and ports, and document the `ISKENDER_CONFIG_DIRECTORY` and `ISKENDER_ENVIRONMENT` settings that run each host natively against the `dependencies` mode (TR-8).
- **FR-015**: Only the data plane's traffic port MUST be published by default, on `127.0.0.1` only. No health port MUST ever be published. In the `dependencies` mode, `postgres` and `valkey` MUST publish on `127.0.0.1` only. Each published host port MUST have an override variable (TR-9).

**Seed-free start-up**

- **FR-016**: A start MUST need no step beyond the start script, MUST apply no migration, and MUST load no seed or sample data. Starts on empty and on existing volumes MUST both succeed (TR-10).

**Scripts**

- **FR-017**: The topology MUST provide the **start**, **stop**, **reset**, **migrate**, and **smoke** scripts of TR-11, in `bash`, each with `--help`, each runnable from any directory inside the checkout, and each writing only value-free output. The start script MUST exit 0 only when, within a bound, `postgres` and `valkey` report healthy and, in the default start, each plane's `health live` command exits 0, and MUST exit 1 naming any service that exits or is not live within the bound.
- **FR-018**: Each script MUST use the exit codes of TR-12, and the migration script MUST pass through the `migrate` command's exit code.
- **FR-019**: The smoke script MUST assert exactly the state of TR-13, MUST read readiness through the in-network probe of TR-14 without publishing a health port, MUST wait for `lifecycle` to leave `starting` up to a bound before asserting, and MUST fail on any other state, a persistence reason of `ok` included.
- **FR-020**: The reset script MUST remove only the containers, network, and named volumes of the topology's own Compose project, as TR-1 names it, and its password file, MUST remove no image and nothing of another project, and MUST require confirmation or `--yes`, refusing with exit code 64 in a non-interactive shell without it (TR-15).

**Contract, tests, and documentation**

- **FR-021**: This spec MUST publish the `local-environment` contract, version 1, in its `contracts/` directory, owning what TR-16 lists, and MUST NOT revise the container, persistence, coordination, configuration, or hosting contract.
- **FR-022**: The acceptance test of TR-17 MUST exist, MUST run in a clean copy of the tracked files only, MUST run start, smoke, and reset and the `dependencies` mode, MUST follow ISK-0005's container-runtime rule, and MUST run inside the existing `dotnet test` check, with CI staying at 10 checks.
- **FR-023**: Documentation MUST be updated in the same PR: `docs/04` gains a local development guide to the topology, its profiles, scripts and exit codes, ports and override variables, configuration documents, native `dotnet run` use, reset, smoke assertions, and the state before ISK-0021, and records the settlement of ISK-0007 AV-6. The contributor guide names the start and smoke scripts and their prerequisites.
- **FR-024**: The scripts, documents, and provisioning script MUST be stored with LF line endings regardless of a contributor's Git settings.

### Negative Security and Failure Scenarios

| Scenario | Expected outcome | Covered by |
|---|---|---|
| A development-only value, such as `tlsMode: disable`, reaches a production copy of the documents | Every development-only value is in the `development` overlay, which loads only when `ISKENDER_ENVIRONMENT` is `development`, and the base validates as production-grade | FR-011, FR-012, User Story 3 |
| A database password is committed, printed, or logged | Passwords are generated per checkout into an owner-only, ignored file, given to `postgres` only, and never printed. Documents hold secret references only | FR-008, FR-012, User Story 3 |
| A fabricated credential or an interim credential provider makes the topology look ready | None is added. Persistence reports `authentication_failed`, and the smoke run fails if it reports `ok` | FR-019, User Story 2 |
| The database, Redis, or a health endpoint is reachable from another machine | Published ports bind to `127.0.0.1` only, and no health port is published | FR-015, User Story 1 |
| The developer topology runs the images with relaxed hardening, hiding failures production would have | Plane services keep the image rules: read-only root, no capability, no privilege, numeric non-root user | FR-006, User Story 1 |
| A reset deletes another project's data | It removes only its own project's resources, found by its fixed name, after confirmation | FR-020, User Story 4 |
| New passwords are generated for an already-provisioned volume, locking the contributor out | The start refuses and directs the contributor to reset | FR-009, User Story 4 |
| Redis evicts fencing counters or namespace generations | `valkey` runs with `noeviction` | FR-010, TR-13 |
| A migration runs on start, or a plane depends on migration | No service migrates by default, and the migration job is opt-in and one-shot | FR-002, FR-016, User Story 6 |
| A base image changes under the same tag | Every image is pinned by digest | FR-005, TR-14 |
| A change to the ISK-0007 acceptance fixture silently changes developer behavior, or the reverse | The two share no file | FR-001 |
| A smoke run passes while readiness has changed | The smoke run asserts every check's reason, and fails on any other state | FR-019, User Story 2 |

### Key Entities

- **Developer topology**: The Compose project, its fixed name, its services, its profiles, and its named data volumes, one for `postgres` and one for `valkey`.
- **Plane configuration documents**: Each plane's base and `development` overlay, mounted read-only.
- **Provisioning script**: The first-start creation of the database and the three ISK-0004 identities.
- **Password file**: The per-checkout, gitignored, owner-only file of generated PostgreSQL passwords.
- **Scripts**: start, stop, reset, migrate, and smoke, with their options and exit codes.
- **Smoke assertions**: The expected state of every service and readiness check before ISK-0021.
- **Readiness probe**: The one-shot, in-network container that reads `/health/ready`.
- **`local-environment` contract**: The versioned contract that owns the above.
- **Acceptance test**: The fresh-copy start, smoke, and reset run that is this spec's evidence.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From a clean copy of the tracked files, with a container runtime and no credentials, the start script exits 0 and the smoke script exits 0, on 5 consecutive runs, each followed by a reset.
- **SC-002**: With packages restored and no cloud credentials, the format check, build, and tests pass with zero warnings, and all 10 CI checks pass, the acceptance test included.
- **SC-003**: The smoke run asserts all five readiness checks of both planes, the `migrate status` exit code and its error code, the three PostgreSQL roles, and the Valkey eviction policy, and exits 1 in each of the failure cases of User Story 2 (scenarios 4 to 6 and 8).
- **SC-004**: Each generated password appears 0 times in the tracked files, script output, readiness answers, and captured plane logs across the acceptance test.
- **SC-005**: Exactly one host port is published by the default topology, bound to `127.0.0.1`, and 0 health ports are published in any profile.
- **SC-006**: After a reset, 0 containers, networks, or volumes of the topology's project remain, and 100% of a control project's volumes remain.
- **SC-007**: Each plane's base document validates with no environment name and holds 0 development-only values, and 100% of the topology's development-only values are in the `development` overlays.
- **SC-008**: Each script exit code in TR-12 is produced by at least one test or acceptance run.
- **SC-009**: The PR changes 0 tracked files under `.specify/`, 0 in `.iskender/grounding.yaml`, 0 in either ISK-0007 Dockerfile, and 0 under `tests/Iskender.Container.Tests/compose/`, and adds 0 entry points, images, credential providers, product tables, seed data, endpoints, or cloud provisioning files.
- **SC-010**: The compatibility harness reports 0 changes to `hosting`, `core-contracts`, `providers-abstractions`, and `policy-sdk`.

## Assumptions

- ISK-0003 to ISK-0007 are merged and complete on `main`, and their contracts, the two plane images, the `migrate` command, and their test fixtures exist as their contracts describe.
- Contributors have a container runtime with Compose v2, and network access to pull the pinned images and restore packages from nuget.org. Neither needs a credential.
- No product table exists, so there is nothing to seed, and the only migration module is `coordination`, which cannot run before ISK-0021.
- No product feature registers a `FeatureRequirement` yet, so `capability` reports `none_refused`.
- One topology per checkout. Several topologies from one checkout at once are out of scope.
- The topology is for development only and makes no claim about production scalability, availability, or performance, and sets no SLA (excluded by the approved prompt).
- Any interaction with policy plans, plugin trust, durable conversations, financial controls, or streaming goes through those specs' authoritative documents. This spec composes none of them.

## Out of Scope

- Fabricated cloud secrets, a production SLA, and any availability, latency, or throughput figure.
- A single all-in-one container image, a process supervisor, or any new entry point or image.
- Database and coordination credential providers, interim or otherwise (ISK-0021).
- Seed or sample data, and product tables.
- A management UI or management API (the optional UI of topology 1 waits for its spec).
- Background jobs, such as scheduling `PurgeExpiredAsync` (ISK-0009).
- Production cloud provisioning, Kubernetes manifests, Helm charts, and Azure Container Apps templates.
- Publishing images to a registry, and multi-architecture image builds.
- TLS between local services.
- Changes to ISK-0007's acceptance fixture, the container, persistence, coordination, configuration, or hosting contracts, the constitution, the roadmap, ADRs, or `.iskender/grounding.yaml`. Any roadmap consequence, including topology 1's wording, goes through `/review-roadmap`.
