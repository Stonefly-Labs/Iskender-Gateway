# Container Contract

**Version**: 3

**Supersedes**: version 2, `specs/0009-module-events-background-work-coordination/contracts/container.md`, which stays unedited as a completed record. Version 1, `specs/0007-container-entry-points-process-separation/contracts/container.md`, also stays unedited.

**Owning spec**: ISK-0007 — Container Entry Points & Process Separation. Version 2 revised by ISK-0009 — Module Events & Background Work Coordination. Version 3 revised by ISK-0010 — System-Operator Bootstrap & Security Baseline

**Review owner**: Architecture (ADR) owner, with the Security owner for Listeners, Images, and Threat model, and the Privacy/Operations owner for Readiness, Lifecycle, and Diagnostics. See `CONTRIBUTING.md` for who holds each role.

This contract states what operators, Compose files, orchestrator manifests, release pipelines, and later specs may rely on from Iskender's two container entry points and the worker that the control-plane executable runs. The type shapes are in ISK-0007's [data-model.md](../../0007-container-entry-points-process-separation/data-model.md), and the reasons in its [research.md](../../0007-container-entry-points-process-separation/research.md). Version 2's changes are in ISK-0009's [data-model.md](../../0009-module-events-background-work-coordination/data-model.md) and [research.md](../../0009-module-events-background-work-coordination/research.md). Version 3's changes are in ISK-0010's [data-model.md](../data-model.md) and [research.md](../research.md). Composition follows the [hosting contract](hosting.md), version 4, module placement the [module dependency rules contract](../../0007-container-entry-points-process-separation/contracts/module-dependency-rules.md), version 5, background work the [work contract](../../0009-module-events-background-work-coordination/contracts/work.md), version 1, migrations the [persistence contract](persistence.md), version 4, and the admin surface, tokens, and transport modes the [operator-security contract](operator-security.md), version 1.

## Changing this contract

A change to a rule here is made by a later spec's revision of this contract, which increments the version, publishes the contract in that spec's `contracts/` directory, repoints the links in `docs/03-domain-and-technical-architecture.md` and `docs/04-technology-stack-and-deployment.md`, and records the change in this table. Removing or renaming an exit code, event ID, readiness reason, endpoint, configuration field, or environment variable is a breaking change for every manifest that reads it.

| Version | Spec | Change | Approval | Owner |
|---|---|---|---|---|
| 1 | ISK-0007 | First version | ISK-0007's PR | Architecture (ADR) owner |
| 2 | ISK-0009 | The `work` command of the control-plane executable runs the worker, its own plane with `service.name` `iskender-worker` (Entry points rules 1 and 2, Command line rule 1); the outbox in every plane and the dispatcher in the worker (Entry points rule 2); the worker's stop step "stop claiming at the signal" (Lifecycle rule 1); the migration set gains `directory` and `work` (Migration command rule 4); `Iskender.Work` telemetry (Telemetry rule 1); `{Plane}` `worker` (Diagnostics rule 1). No exit code, readiness check or reason, endpoint, `container` field, or step of the data plane's or serving control plane's sequence changes | ISK-0009's PR, maintainer decision in `plan` on 2026-11-17 | Architecture (ADR) owner |
| 3 | ISK-0010 | The control plane composes `AddIskenderOperatorSecurity()` and `AddIskenderPostgreSqlIdentity()`, in serving, `migrate`, and `bootstrap`, and maps `MapIskenderOperatorSecurity("/admin/v1")` on its traffic listener when serving, and the worker composes neither (Entry points rule 2); the `bootstrap issue` and `bootstrap recover` commands, exit codes 8, 9, and 10, and the extensions of exit codes 2, 5, and 64 (Command line rules 1 to 3, Bootstrap command); an `operator` section is rejected in a data-plane or worker document (Configuration roots rule 8); the `container` section's version 2 adds `trafficTransport`, `tlsCertificateFile`, and `tlsKeyFile`; the transport modes and TLS rules, which settle ISK-0007's AV-5, so the TLS row of Left to later specs is removed (Listeners rules 1, 4, 5, and 7); no installation state in readiness (Readiness rule 10); the transport check and the route-declaration check (Start-up rules 1 and 6); the migration set gains `identity` and `audit` (Migration command rule 4); `Iskender.Identity` telemetry (Telemetry rule 1); event 7008 and 7002 for the new refusals (Diagnostics rules 2, 9, and 10); the transport, TLS, and bootstrap tests (Test fixtures rules 6 to 9). No existing exit code changes meaning, and no readiness check, reason, or answer, or step of the worker's sequence, changes | ISK-0010's PR, maintainer decisions in `plan` on 2026-11-21 | Architecture (ADR) owner, Security owner for Listeners |

## Entry points

*Review owner: Architecture (ADR) owner.*

1. There are two executables, each its own image, built from the same commit. The control-plane executable also runs the worker, as its own process, through the `work` command:

   | Plane | Project | Image | Arguments | `service.name` | Composition root |
   |---|---|---|---|---|---|
   | Data plane | `Iskender.Gateway.Host` | Built from `src/Iskender.Gateway.Host/Dockerfile` | None | `iskender-data-plane` | `AddIskender(...)` |
   | Control plane | `Iskender.ControlPlane.Host` | Built from `src/Iskender.ControlPlane.Host/Dockerfile` | None | `iskender-control-plane` | `AddIskenderControlPlane(...)` |
   | Worker | `Iskender.ControlPlane.Host` | The control-plane image | `work` | `iskender-worker` | `AddIskenderWorker(...)` |

2. Each composes exactly these registrations, and no adapter outside them:

   | Registration | Data plane | Control plane | Worker |
   |---|---|---|---|
   | Composition root | `AddIskender(...)` | `AddIskenderControlPlane(...)` | `AddIskenderWorker(...)` |
   | `AddIskenderContainer()` | Yes | Yes | Yes |
   | `AddIskenderCoordination()` | Yes | Yes | Yes |
   | `AddIskenderPostgreSqlPersistence()` | Yes | Yes | Yes |
   | `AddIskenderPostgreSqlCoordination()` | Yes | Yes | Yes |
   | `AddIskenderRedisCoordination()` | Yes | Yes | Yes |
   | `AddIskenderPostgreSqlWork()` (the outbox) | Yes | Yes | Yes |
   | `AddIskenderPostgreSqlWorkDispatcher()` | No | No | Yes |
   | `AddIskenderOperatorSecurity()` | No | Yes | No |
   | `AddIskenderPostgreSqlIdentity()` | No | Yes | No |
   | Route group | `MapIskender("/")` on the traffic listener | `MapIskenderOperatorSecurity("/admin/v1")` on the traffic listener | None |

   The data plane and the serving control plane run no dispatcher and no job. The worker is the only process that runs background work (work contract, Registration).

   `migrate` and `bootstrap` make the serving control plane's registration calls. The worker composes neither operator registration and binds no traffic listener.

3. The planes and the worker share no process, file, or in-memory state, and meet only through PostgreSQL and Redis under their contracts. Each starts, runs, and stops with the others absent.
4. The data-plane image holds no `Iskender.Management` assembly, and the control-plane image no `Iskender.Hosting` assembly.
5. PostgreSQL persistence is always composed, so the `persistence` section's required fields are required in every container. The Redis coordinator is always registered, and connects only when a primitive selects it.

## Command line and exit codes

*Review owner: Architecture (ADR) owner.*

1. Arguments are parsed before any configuration is read.

   | Arguments | Plane | Effect |
   |---|---|---|
   | None | Both | Serve |
   | `work` | Control plane | Serve as the worker: the same start, readiness, and stop as serving, with the worker's composition, and only the health listener |
   | `health live`, `health ready` | Both | Read the configuration with no events, send `GET http://127.0.0.1:<healthPort>/health/<live or ready>`, wait up to 30 s, and exit 0 on `200` and 1 otherwise |
   | `migrate apply` | Control plane | Apply every pending migration |
   | `migrate reverse [--steps <n>] [--confirm-data-loss]` | Control plane | Reverse the latest `n` migrations, default 1 |
   | `migrate status` | Control plane | Report the schema state |
   | `bootstrap issue` | Control plane | Issue a bootstrap token, superseding any active one, and print it once, while the installation is `uninitialized` |
   | `bootstrap recover` | Control plane | Issue a recovery token, superseding any active one, and print it once, while the installation is `initialized` |
   | Anything else | Both | Print usage to standard error, and exit 64 |

2. Exit codes:

   | Code | Meaning |
   |---|---|
   | 0 | Clean stop after a stop signal, or a command that succeeded. For `migrate status`, the schema is current |
   | 1 | Unexpected failure, such as a port that cannot be bound. For `health`, not `200` |
   | 2 | `hosting.configuration_rejected`: a rejected configuration, or an unreadable, ambiguous, or missing configured layer. Also `config.field.out_of_range` when `trafficPort` equals `healthPort`, which is checked after validation and before any listener binds. Also `operator.transport_unprotected` and `operator.certificate_unusable`, checked at the same point (The `container` section) |
   | 3 | `hosting.registration_missing` |
   | 4 | A refused feature with `MandatoryPath` (`coordination.capability_unsupported`), including the worker's `work.dispatch` and `work.job.*` features when its leases are not PostgreSQL's |
   | 5 | A migration or bootstrap command failed with a `persistence.*` code, or `migrate status` could not run |
   | 6 | `migrate status`: behind, with a pending migration or a module below its supported range |
   | 7 | `migrate status`: ahead or mismatched, with an unknown migration, a module above its supported range, or a checksum mismatch. This wins over 6 |
   | 8 | `bootstrap issue`: the installation is already initialized (`operator.already_initialized`) |
   | 9 | `bootstrap recover`: the installation is not initialized (`operator.not_initialized`) |
   | 10 | Serving start refused: an admin route without exactly one declaration (`operator.route_undeclared`) |
   | 64 | Usage error, including `--steps` that is not an integer from 1 to 2,147,483,647, a repeated or unknown option, `migrate`, `work`, or `bootstrap` on the data plane, `work` followed by any argument, or `bootstrap` with a missing, unknown, or extra argument |

   A code is added, never reused.

3. The data-plane executable has no migration command, no `work` command, and no `bootstrap` command.

## Configuration roots

*Review owner: Architecture (ADR) owner.*

1. The configuration directory is `ISKENDER_CONFIG_DIRECTORY` when set, otherwise `/etc/iskender`.
2. The base layer is `iskender.yaml` or `iskender.json` in that directory. When `ISKENDER_ENVIRONMENT` is set, the overlay layer is `iskender.<environment>.yaml` or `iskender.<environment>.json`. Names match exactly, including case. An overlay file name is formed only from a name that meets ISK-0003's environment-name rule (1 to 32 characters of `a-z`, `0-9`, and `-`). Any other name forms no path and looks up no file: it is passed to ISK-0003 as given, which rejects it with `config.environment.invalid`, exit code 2.
3. The process's `ISKENDER__` variables are always read, as ISK-0003's environment layer.
4. Iskender's environment name is `ISKENDER_ENVIRONMENT`, or none when unset. None is production-grade. A variable that is present but empty counts as unset: an empty `ISKENDER_ENVIRONMENT` gives no environment name, which is production-grade, and an empty `ISKENDER_CONFIG_DIRECTORY` selects `/etc/iskender` (maintainer decision, 2026-11-15, `checklist`).
5. Precedence is ISK-0003's: defaults, base, overlay, environment variables. There is no further layer.
6. `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, `appsettings*.json`, command-line arguments, `ASPNETCORE_URLS`, and every other ASP.NET Core setting never reach Iskender's configuration, its environment name, or a listener.
7. A missing base or overlay file is a missing layer. The default directory may be absent. These fail start with `hosting.configuration_rejected`, exit code 2, and events 6002 and 7002, naming the layer and no content:
   - `ISKENDER_CONFIG_DIRECTORY` names a directory that does not exist.
   - A layer has both a `.yaml` and a `.json` file.
   - A layer's file exists but cannot be read.
8. Each plane validates only the sections it registers, so a document holding another plane's section, such as `hosting` in a control-plane or worker document, or `work` in a data-plane or control-plane document, or `operator` in a data-plane or worker document, is rejected with `config.namespace.unknown`.
9. Event 6001 names every document used.

## The `container` section

*Review owner: Architecture (ADR) owner.*

Namespace `container`, version 2, registered by `AddIskenderContainer()` in `Iskender.Infrastructure.Container`:

| Field | Type | Default | Constraints | Development-only |
|---|---|---|---|---|
| `trafficPort` | integer | `8080` | 1,024 to 65,535; not `healthPort`, checked when the settings are read | No |
| `healthPort` | integer | `8081` | 1,024 to 65,535; not `trafficPort` | No |
| `shutdownDelay` | duration | `0s` | `0s` to `1m` | No |
| `shutdownTimeout` | duration | `30s` | `1s` to `5m` | No |
| `readinessCacheDuration` | duration | `2s` | `0s` to `30s` | No |
| `trafficTransport` | enumeration | `none` | `listener`, `upstream`, or `none`. `none` only when Iskender's environment name is `development` | No |
| `tlsCertificateFile` | string | None | An absolute path to a PEM certificate chain. Required when `trafficTransport` is `listener`, and rejected otherwise | No |
| `tlsKeyFile` | string | None | An absolute path to an unencrypted PEM private key. Required when `trafficTransport` is `listener`, and rejected otherwise | No |

The three transport fields are checked when the settings are read, after validation, only for a plane with a traffic listener, so the worker ignores them:

1. `trafficTransport` `none` when Iskender's environment name is not `development`, including when there is none, fails with `operator.transport_unprotected`, naming `container.trafficTransport`.
2. A `tlsCertificateFile` or `tlsKeyFile` that is missing in `listener` mode, or set in another mode, fails with `operator.certificate_unusable`, naming the field.
3. In `listener` mode, the certificate and key are loaded once, as a pair. A missing, unreadable, malformed, encrypted, not-yet-valid, expired, or mismatched pair fails with `operator.certificate_unusable`, naming the field and never its path or content.
4. Each fails start with exit code 2 and event 7002, before any listener binds.

## Listeners

*Review owner: Security owner.*

1. Both listeners are on all interfaces, bound only from the `container` section, and only after the configuration is valid. The health listener is plain HTTP. The traffic listener is plain HTTP unless its mode is `listener`. Its transport modes are:

   | Mode | Traffic listener |
   |---|---|
   | `listener` | In-process TLS, from the PEM certificate chain and private key files of `tlsCertificateFile` and `tlsKeyFile`, both mounted read-only |
   | `upstream` | Plain HTTP. The operator declares that TLS is terminated in front of the container |
   | `none` | Plain HTTP with no declaration, permitted only in `development` |
2. A request is routed by the port of the listener it arrived on, never by its `Host` header.
3. The health listener serves only `GET /health/live` and `GET /health/ready`, and answers any other request with `404` and no body.
4. On the data plane, the traffic listener serves `MapIskender("/")`. `/health/live` and `/health/ready` on the traffic listener reach Iskender's route group, never the health endpoints. On the control plane, `/health/live` and `/health/ready` on the traffic listener reach the admin surface, which answers `404`, never the health endpoints.
5. The control plane binds its traffic listener and serves only the admin routes under `/admin/v1` there: `POST /admin/v1/bootstrap`, `POST /admin/v1/recovery`, and `GET /admin/v1/installation`. The details are in the [operator-security contract](operator-security.md). The worker binds only its health listener, always.
6. Readiness is advisory. The traffic listener serves requests the same way whether or not the process is ready, and each dependency fails closed through its own error code. Only the orchestrator acts on readiness.
7. Transport:
   - In `listener` mode, TLS 1.2 is the minimum version. Cipher suites and every other TLS parameter are the platform's defaults. A plain-HTTP request to the traffic port fails the TLS handshake, so no route runs.
   - The certificate and key are loaded once at start, and never at first connection. Rotating them needs a restart.
   - Only an unencrypted PEM private key is accepted, protected by its file permissions. An encrypted key fails start with `operator.certificate_unusable` and exit code 2.
   - The health listeners of the data plane, the control plane, and the worker are plain HTTP, protected by being a separate port, which is never published beyond the orchestrator's private network.
   - Outside `development`, the data plane and the control plane each need `listener` or `upstream`. The worker binds no traffic listener, so the rule does not apply to it.
   - ISK-0007's AV-5 is settled by ISK-0010.

## Liveness

*Review owner: Privacy/Operations owner.*

`GET /health/live` answers `200` with `{"version":1,"status":"live"}` as `application/json` while the process runs. It calls no probe and no network dependency.

## Readiness

*Review owner: Privacy/Operations owner.*

1. `GET /health/ready` answers `200` when every check is ready and `503` otherwise, as `application/json`, with no authentication:

   ```json
   {
     "version": 1,
     "status": "not_ready",
     "checks": [
       { "name": "lifecycle", "status": "ready", "reason": "started" },
       { "name": "persistence", "status": "not_ready", "reason": "authentication_failed" },
       { "name": "coordination", "status": "ready", "reason": "not_configured" },
       { "name": "single_instance", "status": "ready", "reason": "not_applicable" },
       { "name": "capability", "status": "ready", "reason": "refused_features" }
     ],
     "refused": [
       { "feature": "example.feature", "module": "example", "reason": "local_in_multi_replica" }
     ]
   }
   ```

   `status` is `ready` or `not_ready`. The checks appear in this order, always all five. `refused` lists every refused feature without `MandatoryPath`, in registration order, and is empty when there is none.

2. **Lifecycle**:

   | Reason | Ready | When |
   |---|---|---|
   | `starting` | No | From process start until every composition start-up has finished |
   | `started` | Yes | Afterwards, until a stop signal |
   | `stopping` | No | From the first stop signal |

3. **Persistence**, from `IPersistenceHealthProbe`:

   | Reason | Ready |
   |---|---|
   | `ok` | Yes |
   | `migration_in_progress` | Yes. Migrations in the compatibility window are expand-only (maintainer decision in ISK-0004, 2026-11-10) |
   | `unreachable`, `authentication_failed`, `timeout`, `schema_behind`, `insufficient_privilege`, `unexpected`, `checksum_mismatch`, `schema_ahead` | No |

4. **Coordination**, from `ICoordinationHealthProbe`:

   | Reason | Ready |
   |---|---|
   | `ok` | Yes |
   | `not_configured`, healthy (no primitive uses a distributed coordinator) | Yes |
   | `not_configured`, unhealthy (a selected coordinator has no settings) | No |
   | `unreachable`, `authentication_failed`, `timeout`, `read_only`, `unexpected` | No |

5. **Single instance**, from `ISingleInstanceStatus`:

   | State | Ready | Reason |
   |---|---|---|
   | `held` | Yes | `held` |
   | `not_applicable` | Yes | `not_applicable` |
   | `unverified` | Yes | `unverified` |
   | `lost` | No | `single_instance_lost` |
   | `held_by_other` | No | `single_instance_held_by_other` (maintainer decision, 2026-11-15) |

   Until the coordination start-up has composed the guard, the state is `unverified`, while the lifecycle check is still `starting` (maintainer decision, 2026-11-16, `implement`). A read of the state that throws otherwise reports `unexpected`, not ready (rule 11).

6. **Capability**: ready, with reason `none_refused` or `refused_features`, unless reading the capability report throws, which reports `unexpected`, not ready (rule 11).
7. A module that is not composed reports `not_composed` and is ready. Both entry points compose every module this table reads, so only a test host can produce it.
8. The persistence and coordination probes run concurrently within one evaluation, never one after the other, so the longest probe bounds the answer. Each probe runs at most one call at a time. Requests that arrive while it runs share its result, and a result no older than `readinessCacheDuration` is reused. With `0s`, each request that finds no call in flight starts one. A response comes within the longest composed probe's timeout plus its 2-second allowance.
9. A change in any check's reason writes event 7003 once.
10. Neither endpoint's answer holds a host, port, database name, user name, secret reference name, configuration value, installation state, or server error text. Feature and module names are code-defined. The answers are unchanged byte for byte from version 2.
11. A check whose read throws, whether building its probe, reading the single-instance state, or reading the capability report, reports `unexpected` and is not ready, and the answer is still the report, as `application/json`. Event 7007 names the check and the exception's type name (Diagnostics rule 8; maintainer decision, 2026-11-16, `implement`).

## Start-up

*Review owner: Architecture (ADR) owner.*

1. The entry point resolves its configuration roots, builds the application, and runs the hosting contract's requirement check and validation before any listener binds. A failure exits 2 or 3. After validation and before any listener binds, it checks the traffic listener's transport mode and, in `listener` mode, loads the certificate (The `container` section). A refusal exits 2.
2. The host then starts, with the lifecycle check `starting`, and runs the composition start-ups of hosting contract Start-up rule 6. Its hosted services start together, so both listeners bind and answer while the start-ups run.
3. A refused feature with `MandatoryPath` fails start with `coordination.capability_unsupported` and exit code 4, for each of the five reasons, `single_instance_violated` included (hosting contract Start-up rule 7). The capability report never changes while the process runs, so a lasting refusal is a restart, which the orchestrator applies with its own backoff.
4. A refused feature without `MandatoryPath` leaves the process ready, every call through it fails closed, and readiness lists it.
5. No process applies or reverses a migration at start, and no long-running process needs the migration login.
6. On the control plane, the host's start first runs the operator route-declaration check, before any hosted service starts, so before any listener binds. An admin route that declares neither or both of an operator action and an anonymous inventory entry, or a `MapIskenderOperatorSecurity` call made more than once or never, fails start with `operator.route_undeclared` and exit code 10, after event 9005 of the operator-security contract and event 7002, with no listener bound.

## Lifecycle

*Review owner: Privacy/Operations owner.*

1. On `SIGTERM` or `SIGINT`:
   1. The lifecycle check turns `stopping` at once, and event 7004 is written. In the worker, the dispatcher and the jobs stop claiming at once, and in-flight items may run until `shutdownDelay` + `shutdownTimeout` after the signal (work contract, Shutdown). This is the worker's one added step.
   2. Requests keep being served for `shutdownDelay`, so load balancers can drain.
   3. The server stops accepting connections, and waits up to `shutdownTimeout` for requests in flight, then cancels them.
   4. The composition start-ups stop in reverse order. In the worker, the work start-up cancels any item still running and releases its leases first. The guard stops its timer and releases its lease.
   5. The container is disposed, which disposes the Redis coordinator and the data sources.
   6. Event 7005 is written, and the process exits 0.
2. A stop signal during start-up turns the lifecycle check `stopping` at once and writes event 7004, as rule 1.1 does, then waits for the composition start-up to finish or be cancelled before anything is disposed, so the Redis coordinator is never disposed during its first connection. The rest of the sequence then runs, and the process writes event 7005 and exits 0, as after a stop once started (SC-005).
3. A second stop signal does not shorten the sequence, and never skips releasing the lease.
4. **The orchestrator's grace period before it kills the process must exceed `shutdownDelay` + `shutdownTimeout` + 5 seconds.** With the defaults that is 35 seconds, longer than Docker's default of 10. A shorter grace period is a misconfiguration: the process is killed mid-stop, and the lease is not released and expires on its own TTL.

## Migration command

*Review owner: Architecture (ADR) owner.*

1. `migrate` runs from the control-plane image, as a one-shot job that an operator or a release pipeline runs before rolling out a release.
2. It makes the same registration calls as the serving control plane, so the same documents validate (maintainer decision, 2026-11-15). It runs the requirement check and validation, then one call to `IMigrationRunner`, under the migration login.
3. It runs no composition start-up, so it starts no guard, runs no capability check, connects to no coordinator, and binds no listener.
4. Its migration set is every shipped migration module the registrations add. From version 2 that is `directory`, `coordination`, and `work`. From version 3 it also has `identity` and `audit`, so the forward order is `coordination`, `directory`, `work`, `audit`, `identity` (persistence contract version 4). A `migrate` run issues no token and serves no route.
5. ISK-0004's events 4007 to 4012 are written as the persistence contract defines. Event 7006 is written once for every run whose arguments parse, whatever its exit code: for exit codes 2 and 3 its outcome is `failed` and its count 0, after the hosting contract's own events, and for exit codes 2, 3, and 5 it names the failure's code (Diagnostics rule 7). A `migrate` run serves nothing, so it writes none of events 7001 to 7005.
6. A missing migration login fails with `persistence.unavailable` before connecting. Two `migrate apply` jobs at once: the second waits up to `migration.lockWait`, then fails with `persistence.migration_locked`.
7. Until ISK-0021 provides a credential provider, every `migrate` operation exits 5 with `persistence.unavailable`.

## Bootstrap command

*Review owner: Security owner.*

1. `bootstrap` runs from the control-plane image, as a one-shot job that an operator runs to issue a bootstrap token before the first operator exists, or a recovery token afterwards.
2. It makes the same registration calls as the serving control plane, so the same documents validate. It runs the requirement check and validation, then one call to issue the token, under the runtime login, never the migration login.
3. It runs no composition start-up, so it starts no guard, runs no capability check, connects to no coordinator, and binds no listener.
4. In one unit of work it supersedes any active token of the purpose, stores the new token's hash, and writes the audit records (operator-security contract). Only after the commit does it write the token, then a newline, once to standard output. It writes nothing else to standard output. Its events and diagnostics go to standard error and never hold the token.
5. Its exit codes are 0 when the token is issued, 2 and 3 as for serving, 5 for a `persistence.*` failure, 8 for `bootstrap issue` on an initialized installation, 9 for `bootstrap recover` on an uninitialized one, and 64 for a usage error. A run that does not exit 0 prints no token.
6. Event 7008 is written once for every run whose arguments parse, whatever its exit code, after the hosting contract's own events for exit codes 2 and 3 (Diagnostics rule 9). A `bootstrap` run serves nothing, so it writes none of events 7001 to 7005.
7. Until ISK-0021 provides a credential provider, every `bootstrap` operation exits 5 with `persistence.unavailable`.

## Images

*Review owner: Security owner.*

1. Each Dockerfile is multi-stage, and pins every base image by digest:

   | Stage | Image |
   |---|---|
   | Build | `mcr.microsoft.com/dotnet/sdk:10.0.404-noble@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb` |
   | Runtime | `mcr.microsoft.com/dotnet/aspnet:10.0.15-noble-chiseled@sha256:7c625e44e5d588b4d7b2a913e94ca4bd5098ce47442da404829e4c7936a764e1` |

   A digest changes only through a revision of this table.

2. The runtime image:
   - Runs as user and group `1654:1654`.
   - Has no file with the setuid or setgid bit.
   - Needs no Linux capability, and runs with `cap_drop: ALL` and `no-new-privileges`.
   - Runs with a read-only root filesystem, writing only to `/tmp`. With a read-only root, `/tmp` must be mounted writable, as a `tmpfs` or an empty volume.
   - Holds the published application only, under `/app`, owned by root, with no SDK, source, test assembly, shell, or package manager.
   - Carries the labels `org.opencontainers.image.source`, `org.opencontainers.image.revision`, and `org.opencontainers.image.licenses` `MIT`.
   - Exposes 8080 and 8081, the default ports.
   - Sets `WORKDIR /app` and the exec-form `ENTRYPOINT ["dotnet", "/app/<host>.dll"]`, so arguments given to the container, such as `migrate apply` or `health ready`, reach the command line of rule Command line 1.
   - Declares no `HEALTHCHECK`. Only the orchestrator decides what to probe and how to act on it (Listeners rule 6). A container health check runs the exec-form command `["dotnet", "/app/<host>.dll", "health", "live"]` or `"ready"`, which needs no shell.
3. The images are built and tested in CI's existing checks and are not pushed to a registry by ISK-0007.
4. Plugin mounts are left to the plugin specs. When they arrive, they are read-only.

## Telemetry

*Review owner: Privacy/Operations owner.*

1. Each entry point subscribes the OpenTelemetry SDK to the activity sources and meters `Iskender.Coordination`, `Iskender.Persistence`, from version 2, `Iskender.Work`, and, from version 3, `Iskender.Identity`, with `service.name` as Entry points rule 1 lists.
2. An OTLP exporter exists only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Otherwise nothing is exported. The other standard `OTEL_*` variables configure that exporter.
3. Logs go to standard output as structured JSON, one object per line. `Microsoft` categories log at `Warning` and above, so no request path or host is written, and `Iskender` categories at `Information` and above.

## Diagnostics

*Review owner: Privacy/Operations owner.*

Category `Iskender.Container`. Templates and IDs are fixed.

| Event ID | Name | Level | Template |
|---|---|---|---|
| 7001 | `ContainerStarting` | Information | `Iskender container starting: plane {Plane}, release {Release}.` |
| 7002 | `ContainerStartRefused` | Error | `Iskender container start refused: code {Code}, exit code {ExitCode}, feature {Feature}, module {Module}, reason {Reason}.` |
| 7003 | `ContainerReadinessChanged` | Information | `Iskender readiness changed: check {Check}, from {From} to {To}.` |
| 7004 | `ContainerStopRequested` | Information | `Iskender container stop requested: plane {Plane}.` |
| 7005 | `ContainerStopped` | Information | `Iskender container stopped: plane {Plane}, exit code {ExitCode}, failure {Failure}.` |
| 7006 | `ContainerMigrationFinished` | Information | `Iskender migration command finished: operation {Operation}, outcome {Outcome}, count {Count}, code {Code}.` |
| 7007 | `ContainerReadinessCheckFailed` | Warning | `Iskender readiness check failed: check {Check}, failure {Failure}.` |
| 7008 | `ContainerBootstrapFinished` | Information | `Iskender bootstrap command finished: operation {Operation}, outcome {Outcome}, code {Code}.` |

1. `{Plane}` is `data_plane`, `control_plane`, or `worker`. `{Release}` is the assembly's informational version.
2. 7002 is written once for every serving start that fails with exit code 2, 3, 4, or 10, after the hosting contract's own event (6002, 6003, or 6004), or, for exit code 10, after event 9005, and before 7005. `{Code}` is the failure's error code: for the exit-2 transport and certificate refusals, `operator.transport_unprotected` or `operator.certificate_unusable`, and for exit code 10, `operator.route_undeclared`. `{Feature}`, `{Module}`, and `{Reason}` are the refused feature's, for exit code 4. For the transport and certificate refusals, `{Reason}` is the failing field, `iskender.container.trafficTransport`, `iskender.container.tlsCertificateFile`, or `iskender.container.tlsKeyFile`, and never the file's path or content (The `container` section rule 3; maintainer decision, 2026-11-26, `implement`). Each is `-` otherwise.
3. `{From}` is `-` on the first evaluation.
4. `{Failure}` is `-`, or, for exit code 1, the unexpected exception's type name, never its message.
5. 7005 is written at Error level for a non-zero exit code.
6. No event holds a configuration value, secret reference name, connection detail, file content, or request route value. ISK-0003's, ISK-0004's, ISK-0005's, and the hosting contract's events are written as their contracts define.
7. 7006's `{Code}` is the failure's error code for a `migrate` run that exits 2, 3, or 5, such as `hosting.configuration_rejected`, `persistence.unavailable`, or `persistence.migration_locked`, and `-` otherwise (maintainer decision, 2026-11-16, `implement`).
8. 7007 is written each time a readiness check's read throws, which reports the check `unexpected`. `{Check}` is the check's name and `{Failure}` the exception's type name, never its message (maintainer decision, 2026-11-16, `implement`).
9. 7008 is written once for every `bootstrap` run whose arguments parse, to standard error with the other logs. `{Operation}` is `issue` or `recover`. `{Outcome}` is `succeeded` or `failed`. `{Code}` is the failure's error code for a run that exits 2, 3, 5, 8, or 9, such as `hosting.configuration_rejected`, `persistence.unavailable`, `operator.already_initialized`, or `operator.not_initialized`, and `-` otherwise. It never holds the token.
10. No event names a certificate or key file's path or content.

## Test fixtures

*Review owner: Performance owner (+ QA).*

1. `tests/Iskender.Container.Tests/compose/compose.yaml` is the acceptance topology. It is separate from ISK-0008's developer Compose. It builds nothing itself. The tests build both images once per run and pass their tags in.
2. It runs one service per User Story 1 scenario and plane, with PostgreSQL 17 and Valkey 8 at the digests of the ISK-0004 and ISK-0005 fixtures, every service with `read_only`, a `/tmp` `tmpfs`, `cap_drop: ALL`, `no-new-privileges`, and `stop_grace_period: 40s`.
3. The cross-host lifecycle test compares the data-plane container with ISK-0006's hosting sample through `EmbeddedHost`, and compares the configuration revision with an `EmbeddedHost` composed with Entry points rule 2's data-plane registrations.
4. Tests that need a container runtime follow ISK-0005's rule: skipped with their reason without one, and failing when `CI` is `true`.
5. The worker's tests run the control-plane image with `work`: its readiness, its exit codes 2, 4, and 64, and its stop within the bound of Lifecycle rule 4.
6. The transport tests start each plane without an environment name and assert that `none` exits 2 with `operator.transport_unprotected` and no listener bound, and that `upstream` and `listener` start. They assert that the worker ignores the transport fields.
7. The TLS tests run each plane's image in `listener` mode and assert a TLS 1.2 or later handshake on the traffic port, a refused plain-HTTP request there, a plain-HTTP health listener, and exit code 2 with `operator.certificate_unusable` for a missing, malformed, encrypted, expired, or mismatched pair, with no path or content in the output. The certificate is a self-signed one, generated at run time into a temporary directory mounted read-only, and never tracked.
8. The bootstrap command tests run the control-plane image with `bootstrap issue` and `bootstrap recover`, and assert exit code 5 with `persistence.unavailable` until ISK-0021, nothing on standard output, and event 7008 on standard error. They assert exit code 64 for `bootstrap` on the data plane and for a missing, unknown, or extra argument. Exit codes 8 and 9, and the token and a newline as the only standard output of a successful run, are asserted through the command's entry point with the operator fixture's database. Exit code 10 is asserted for an admin route without exactly one declaration.
9. Every test document that starts a plane without an environment name declares `trafficTransport`. Existing container test documents declare `upstream`.

## Threat model and known limits

### What the rules defend against

The Negative Security and Failure Scenarios tables in ISK-0007's [spec.md](../../0007-container-entry-points-process-separation/spec.md), ISK-0009's [spec.md](../../0009-module-events-background-work-coordination/spec.md), and ISK-0010's [spec.md](../spec.md) lists each threat with the requirement that covers it. This contract's rules implement them: the host's environment relaxing Iskender's defaults (Configuration roots 4, 6), starting on a partly valid configuration (Start-up 1), a mandatory feature that cannot run (Start-up 3), a second instance (Readiness 5), a migration taking replicas out (Readiness 3), migration credentials in a running plane (Migration command 3, Start-up 5), health endpoints exposed to callers (Listeners 2 to 4), probes overloading dependencies (Readiness 8), running as root (Images 2), each image carrying the other plane (Entry points 4), dropped requests or a held lease at stop (Lifecycle), values in output (Readiness 10, Diagnostics 6), a moving base image (Images 1), unchosen telemetry endpoints (Telemetry 2), plain-HTTP interception of a token or enrollment code outside `development` (The `container` section 1, Listeners 1 and 7), a bad or expired certificate silently downgrading to plain HTTP (The `container` section 2, Start-up 1), the private key leaking through diagnostics (Diagnostics 10), health endpoints on the admin listener (Listeners 4), installation state in readiness (Readiness 10), an admin route added without protection (Start-up 6), the admin surface in the data plane or the worker (Entry points 2), and a token in logs or printed before it is stored (Bootstrap command 4, Diagnostics 9).

### Known limits

- **`upstream` cannot be verified by the process**: it is the operator's declaration that TLS is terminated in front of the container, and the process serves plain HTTP on it. The health listener is protected only by being a separate port, which is never published beyond the orchestrator's private network.
- **Certificate rotation needs a restart**: the certificate and key are loaded once at start.
- **No encrypted keys before ISK-0021**: `listener` mode accepts only an unencrypted PEM private key, protected by its file permissions.
- **No credential provider before ISK-0021**: a container that composes PostgreSQL reports `authentication_failed`, and every `migrate` and `bootstrap` operation exits 5.
- **The capability report is fixed per process**: a refusal that would clear later is a restart.
- **A short grace period**: an orchestrator that kills the process before Lifecycle rule 4's bound leaves the lease to expire on its TTL.
- **`health` reads the configuration**: each `health` command validates the configuration with no events. A configuration that changes on disk after start can make the check fail while the server is healthy.
- **Building images needs the network**: the build stage restores from nuget.org, so a container-runtime test run needs network access.
- **Single architecture**: images are built for the runner's architecture only.

## Left to later specs

| Item | Spec |
|---|---|
| The developer Compose topology and the all-in-one developer container, built from these images (AV-6) | ISK-0008 |
| Database and coordination credential providers in both entry points and the worker (AV-8, and ISK-0009's AV-6) | ISK-0021 |
| Hot reload of configuration | ISK-0057 |
| Further management API endpoints on the control plane's traffic listener | ISK-0080 |
| Passphrase-protected private keys | ISK-0021 |
| Certificate reload without restart, automatic certificate management, mutual TLS | Post-MVP |
| Publishing signed, provenance-bearing images to a registry for the public MVP release, the stock image of ISK-0085 and ISK-0086 | ISK-0086 |
| Multi-architecture images | Post-MVP |
| Read-only plugin mounts | The plugin specs |
