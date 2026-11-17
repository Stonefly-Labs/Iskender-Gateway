# Data Model: ISK-0008 — Local Developer Environment & Reproducible Compose

This spec adds no type to any `src/` project, no table, and no stored record. Its entities are files, Compose resources, and test fixtures. The rules developers and later specs rely on are in [contracts/local-environment.md](contracts/local-environment.md), and the reasons in [research.md](research.md).

## Developer topology

| Element | Shape | Rules |
|---|---|---|
| Compose project | Name `ISKENDER_LOCAL_PROJECT`, default `iskender-local`, pattern `iskender-local(-[a-z0-9]{1,24})?` | Contract, Project name |
| Compose files | `local/compose.yaml`, with `local/compose.dependencies.yaml` layered by every script | Contract, Modes |
| Services | `postgres`, `valkey`, `data-plane`, `control-plane`; `migrate` (profile `migrate`); `probe` (profile `probe`) | Contract, Services |
| Modes | Default, `dependencies` | Contract, Modes |
| Volumes | `<project>_postgres-data`, `<project>_valkey-data` | Contract, Volumes |
| Network | Compose's default network for the project, `<project>_default` | Removed by reset |
| Image tags | `<project>/data-plane:local`, `<project>/control-plane:local` | Contract, Services rule 2. Kept by reset |

### Lifecycle

```text
(absent) ──start──► running ──stop──► stopped ──start──► running
   ▲                   │                 │
   └──────reset────────┴──────reset──────┘
```

- **absent → running**: first start. Writes the password file, provisions PostgreSQL on the empty volume.
- **stopped → running**: later start. Reuses the volumes and the password file, generates nothing.
- **reset**: removes the project's containers, network, volumes, and password file. The next start is a first start.
- A `postgres-data` volume with no password file is an invalid state, which start refuses with exit code 2.

## Plane configuration documents

| Directory | Read by | Files |
|---|---|---|
| `local/config/data-plane/` | `data-plane` | `iskender.yaml`, `iskender.development.yaml` |
| `local/config/control-plane/` | `control-plane`, `migrate` | `iskender.yaml`, `iskender.development.yaml` |
| `local/native/data-plane/` | A native data-plane host | The same pair, with native hosts and ports |
| `local/native/control-plane/` | A native control-plane host | The same pair, with native hosts and ports |

Fields and values are in the contract's Documents and Native runs sections. Validation rules:

- A base validates with no environment name. An overlay is read only under `ISKENDER_ENVIRONMENT=development`.
- Overlay values placed in a base, with no environment name, are rejected (`config.value.unsafe_for_environment` for `tlsMode`, and `config.field.required` for a Redis endpoint with no user or password).
- The control plane rejects the data-plane documents (`config.namespace.unknown` on `hosting`).
- A native pair differs from its container pair only in the lines the contract lists.

## Provisioning script

| Item | Value |
|---|---|
| Path | `local/postgres/initdb/10-provision.sh`, mounted read-only at `/docker-entrypoint-initdb.d` |
| Runs | Once, on an empty `postgres-data` volume, by the image's entrypoint |
| Inputs | `ISKENDER_LOCAL_RUNTIME_PASSWORD`, `ISKENDER_LOCAL_MIGRATION_PASSWORD`, and the database `iskender_local` from `POSTGRES_DB` |
| Creates | `iskender_runtime`, `iskender_local_runtime`, `iskender_local_migrator`, and their grants, as the contract's PostgreSQL rule 1 lists |
| Never | Creates a schema, table, or row, runs a migration, or puts a password on a command line |

## Password file

| Item | Value |
|---|---|
| Path | `local/.secrets/<project>.env` |
| Mode | File `0600`, directory `0700` |
| Content | `POSTGRES_PASSWORD`, `ISKENDER_LOCAL_RUNTIME_PASSWORD`, `ISKENDER_LOCAL_MIGRATION_PASSWORD`, each `[A-Za-z0-9]{32}` |
| Created | By start, only when it and the `postgres-data` volume are both absent |
| Read | By Compose for `postgres` only, and by smoke for the value search |
| Removed | By reset |

## Scripts

| Script | Options | Exit codes |
|---|---|---|
| `local/scripts/start.sh` | `--dependencies`, `--help` | 0, 1, 2, 64 |
| `local/scripts/stop.sh` | `--help` | 0, 1, 2, 64 |
| `local/scripts/reset.sh` | `--yes`, `--help` | 0, 1, 2, 64 |
| `local/scripts/migrate.sh` | `apply`, `reverse [--steps <n>] [--confirm-data-loss]`, `status`, `--help` | The job's own code, or 2 or 64 before the job starts |
| `local/scripts/smoke.sh` | `--help` | 0, 1, 2, 64 |
| `local/scripts/lib/common.sh` | Sourced | Root, project name, port, and prerequisite checks; the Compose wrapper |
| `local/scripts/lib/readiness.sh` | Sourced | The expected readiness body and the per-check comparison |

Environment variables the scripts read: `ISKENDER_LOCAL_PROJECT`, `ISKENDER_LOCAL_DATA_PLANE_PORT`, `ISKENDER_LOCAL_POSTGRES_PORT`, `ISKENDER_LOCAL_VALKEY_PORT`. They read no other variable to change behavior.

## Smoke assertions

| Assertion | Expected before ISK-0021 |
|---|---|
| Services running | `postgres`, `valkey`, `data-plane`, `control-plane` |
| `health live` | Exit 0 on each plane |
| Readiness | `503` with the contract's exact body on each plane |
| `migrate status` | Exit 5, one event 7006 with `status` and `persistence.unavailable` |
| PostgreSQL roles | Exactly the three roles and their grants. No schema or relation of Iskender's |
| Valkey | `maxmemory-policy` `noeviction` |
| Values | No password, database name, login name, or secret reference name in plane logs, readiness answers, or smoke output |

## Test fixtures: `tests/Iskender.Container.Tests/LocalEnvironment/`

| Type | Shape |
|---|---|
| `LocalCheckout` | Creates a temporary directory holding every file `git ls-files -z` lists, with its working-tree content and executable bit, then `git init` and one commit with a fixed test identity and no signing. `Root`, `Head`. Deletes the directory on disposal |
| `LocalTopology` | One project in a `LocalCheckout`: a name `iskender-local-t<12 hex>`, three free ports, `RunScriptAsync(script, args, stdin?)` returning exit code, standard output, and standard error, and `DisposeAsync`, which runs `reset.sh --yes` and removes the two image tags even on failure |
| `RequireLocalTools` | `RequireDocker`'s rule extended to `bash` and `git` on `PATH` |
| `RecordedReadiness` | Readiness bodies for `ReadinessComparisonTests`: the expected one, one with persistence `ok`, one with coordination `unreachable`, one still `starting` |

Test classes are listed in [research.md](research.md#r17-acceptance-test).
