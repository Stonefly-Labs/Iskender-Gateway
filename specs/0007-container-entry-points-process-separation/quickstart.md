# Quickstart: validating ISK-0007

This guide runs the checks that show the feature works. The rules are in [contracts/container.md](contracts/container.md) and [contracts/hosting.md](contracts/hosting.md), and the types in [data-model.md](data-model.md).

## Prerequisites

- The pinned .NET SDK (`global.json`, 10.0.404) on `PATH`, with `DOTNET_ROOT` set if it is a user install.
- Packages restored: `dotnet restore Iskender.slnx --locked-mode`.
- For the image, Compose, migration, and lease scenarios: Docker Engine with BuildKit and Compose v2, and network access, since the image build restores from nuget.org and the fixtures pull digest-pinned images. Without a container runtime those tests are skipped with their reason. With `CI=true` they fail instead.

## 1. Repository gates

```bash
dotnet format --verify-no-changes
dotnet build -warnaserror
dotnet test
bash .github/scripts/check-grounding.sh
bash .github/scripts/check-license.sh
```

Expected: every command passes with zero warnings (SC-001). `check-license.sh` lists no package outside its allowed licenses, which shows the OpenTelemetry packages and everything they bring are Apache-2.0 or MIT (AV-2). The architecture suite passes against rules contract version 5 with no edge change (FR-004, FR-031), and the compatibility harness reports no change to `hosting`, `core-contracts`, `providers-abstractions`, or `policy-sdk` (SC-009).

## 2. Build and inspect the images

```bash
docker build -f src/Iskender.Gateway.Host/Dockerfile -t iskender-data-plane:local --build-arg REVISION="$(git rev-parse HEAD)" .
docker build -f src/Iskender.ControlPlane.Host/Dockerfile -t iskender-control-plane:local --build-arg REVISION="$(git rev-parse HEAD)" .
docker image inspect --format '{{.Config.User}} {{json .Config.Labels}}' iskender-data-plane:local
```

Expected: user `1654:1654`, and the `source`, `revision`, and `licenses` (`MIT`) labels. `Iskender.Container.Tests` checks the file lists: no `Iskender.Management.dll` in the data-plane image, no `Iskender.Hosting.dll` in the control-plane image, no SDK, test assembly, or setuid or setgid file in either (SC-007, US1.7).

## 3. Run a plane by hand

```bash
mkdir -p /tmp/isk-dp && cat > /tmp/isk-dp/iskender.yaml <<'YAML'
iskender:
  persistence:
    host: postgres.invalid
    database: iskender
    username: iskender_runtime
    password: { secretRef: db-runtime }
YAML
docker run --rm --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges \
  -v /tmp/isk-dp:/etc/iskender:ro -p 127.0.0.1:18081:8081 --name isk-dp iskender-data-plane:local
```

In another terminal:

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:18081/health/live    # 200
curl -s http://127.0.0.1:18081/health/ready                                     # 503, persistence "unreachable"
docker exec isk-dp dotnet Iskender.Gateway.Host.dll health ready; echo $?       # 1
docker stop -t 40 isk-dp; docker wait isk-dp                                     # 0
```

The readiness format and its reasons are in the container contract's [Readiness](contracts/container.md#readiness) section. The container's log is JSON lines with events 7001, 6001, 7003, 7004, and 7005, and holds no host name, user name, or secret reference name.

Exit codes to try (container contract, [Command line and exit codes](contracts/container.md#command-line-and-exit-codes)):

| Run | Expected exit |
|---|---|
| `docker run --rm iskender-data-plane:local nonsense` | 64 |
| `docker run --rm iskender-data-plane:local migrate apply` | 64 |
| A base document with a misspelled field mounted at `/etc/iskender` | 2 |
| `-e ISKENDER_CONFIG_DIRECTORY=/nowhere` | 2 |
| `-e ISKENDER_ENVIRONMENT=Prod_EU` | 2 |
| `-e ASPNETCORE_ENVIRONMENT=Development` with `tlsMode: disable` in the document | 2, because Iskender's environment is still none |
| `docker run --rm -v /tmp/isk-dp:/etc/iskender:ro iskender-control-plane:local migrate apply` | 5, `persistence.unavailable`, before ISK-0021 |

## 4. Acceptance: the Compose topology

```bash
dotnet test --project tests/Iskender.Container.Tests -- --filter-trait "Category=Compose"
```

The tests build both images once, start `tests/Iskender.Container.Tests/compose/compose.yaml` with one service per User Story 1 scenario and plane, and check each container's exit code, liveness, readiness status, and readiness reasons, over 5 consecutive `up` and `down` cycles (SC-002). Expected readiness reasons:

| Scenario | Persistence | Coordination | Exit |
|---|---|---|---|
| No PostgreSQL server | `unreachable` | `not_configured`, ready | Running |
| PostgreSQL 17 running | `authentication_failed` | `not_configured`, ready | Running |
| `redis` selected, no Redis server | as above | `unreachable` | Running |
| `redis` selected, no `redis` settings | as above | `not_configured`, not ready | Running |
| A rejected configuration | | | 2, with events 3002, 3003, and 6002 |

The canary test plants a configuration value, a secret reference name, a database host, and a user name, and finds each 0 times in readiness answers, exit diagnostics, and logs (SC-006).

## 5. In-process scenarios

```bash
dotnet test --project tests/Iskender.Container.Tests -- --filter-not-trait "Category=Compose"
dotnet test --project tests/Iskender.Domain.Tests
dotnet test --project tests/Iskender.Hosting.Tests
```

These cover, with stub probes and test hosts built from the container library:

- Every persistence and coordination reason against readiness, 0 probe calls from 1,000 liveness requests, one probe call for 100 concurrent readiness requests, and the readiness-changed event (User Story 3, SC-003).
- `/health/ready` on the traffic listener, with and without a `Host` header naming the health port, reaching Iskender's 404 envelope (US3.5).
- The stop sequence, a stop during start-up, a second stop signal, and the timeout with requests in flight (User Story 2, SC-005).
- Each of the five refusal reasons failing start for a mandatory feature and leaving a non-mandatory feature ready, in a container test host and, through `AddIskender(...)`, in an embedded host (US2.7, US2.8, SC-004).
- Both planes declared `single_instance` against ISK-0004's PostgreSQL fixture with a test credential provider: each plane holds its own lease, a second control plane fails with exit code 4, a lost lease makes readiness `single_instance_lost`, and a lease held by another process makes it `single_instance_held_by_other` (US2.2, US2.3, US2.9).
- `migrate apply`, `reverse`, and `status` through `MigrationCommand` against the fixture, with a test credential provider: exit codes 0, 5, 6, and 7, and events 4007, 4009, 4011, and 7006 (User Story 4).
- Two roots on one collection, a module with no root, and ISK-0006's hosting suite under the plan's rule for moved internal types (User Story 6).

## 6. Cross-host lifecycle

```bash
dotnet test --project tests/Iskender.Container.Tests -- --filter-trait "Category=CrossHost"
```

Expected: the data-plane container and the hosting sample give the same status, envelope, error code, and correlation behavior for every request, apart from generated IDs, and the data-plane container and an `EmbeddedHost` composed with the data plane's registrations name the same revision in event 6001 (User Story 5, SC-008).

## 7. Documentation

`docs/04` holds the operator guide to the two images, the configuration roots, the `container` section, the health endpoints and readiness reasons, the exit codes, the migration command, and the stop sequence, including the grace-period rule. Its Database migrations and Coordination runbooks say how the command and readiness work (FR-034). `docs/03` and `docs/04` link hosting contract version 2 and the container contract (FR-030).
