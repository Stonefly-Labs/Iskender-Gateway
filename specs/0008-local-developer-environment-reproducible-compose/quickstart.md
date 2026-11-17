# Quickstart: ISK-0008 — Local Developer Environment & Reproducible Compose

These scenarios prove the feature end to end. The rules are in [contracts/local-environment.md](contracts/local-environment.md), and the names in [data-model.md](data-model.md). Run each from the repository root, unless a scenario says otherwise.

## Prerequisites

- `git`, and `bash` 3.2 or later (macOS's system `bash` works; on Windows, use WSL 2).
- Docker Engine 24.0 or later with Docker Compose 2.24.0 or later, running. `docker compose version --short` prints the Compose version.
- Network access to Docker Hub and nuget.org. No account, registry login, or credential is needed.
- The .NET SDK pinned in `global.json`, only for the build and test scenarios and native runs.

## 1. A fresh clone starts with one command (US1)

```bash
git clone <repository> iskender && cd iskender
local/scripts/start.sh; echo "exit $?"
docker compose --project-name iskender-local ps --format '{{.Service}} {{.State}} {{.Health}}'
docker compose --project-name iskender-local port data-plane 8080
```

**Expected**: start exits 0. Four services are running and healthy. The data plane's port is `127.0.0.1:18080`. `local/.secrets/iskender-local.env` exists with mode `0600`, and `git status --short` shows nothing.

Run `local/scripts/start.sh` again: it exits 0, and the password file is unchanged.

## 2. Smoke asserts the state before ISK-0021 (US2)

```bash
local/scripts/smoke.sh; echo "exit $?"
```

**Expected**: exit 0. Then stop one dependency and run it again:

```bash
docker compose --project-name iskender-local stop valkey
local/scripts/smoke.sh; echo "exit $?"
```

**Expected**: exit 1, naming `coordination` and the reason found, such as `unreachable`. Restore the topology with `local/scripts/start.sh`.

## 3. The migration job reports that it cannot connect (US6)

```bash
local/scripts/migrate.sh status; echo "exit $?"
local/scripts/migrate.sh apply; echo "exit $?"
local/scripts/migrate.sh frobnicate; echo "exit $?"
```

**Expected**: 5, 5, and 64. Each of the first two writes one event 7006 naming the operation and `persistence.unavailable`. The third starts no container.

## 4. Development-only values stay in development (US3)

```bash
git ls-files local | xargs grep -n -i -E 'password|secret' 
docker compose --project-name iskender-local exec data-plane dotnet /app/Iskender.Gateway.Host.dll health live; echo "exit $?"
```

**Expected**: every match is a `secretRef` name or a variable name, never a value. The tests in `tests/Iskender.Container.Tests/LocalEnvironment/DocumentTests.cs` cover validation of each base with no environment name, and the rejection of overlay values outside `development`.

## 5. A native host against the dependencies (US5)

```bash
local/scripts/start.sh --dependencies
ISKENDER_CONFIG_DIRECTORY="$PWD/local/native/data-plane" ISKENDER_ENVIRONMENT=development \
  dotnet run --project src/Iskender.Gateway.Host
# In another shell:
curl -s -w '\n%{http_code}\n' http://127.0.0.1:8081/health/ready
```

**Expected**: only `postgres` and `valkey` run, on `127.0.0.1:15432` and `127.0.0.1:16379`. The native host answers `503` with the same body as the smoke script's readiness assertion. Stop it with Ctrl+C.

## 6. A clean reset (US4)

```bash
docker volume create --label com.docker.compose.project=isk-control000000 isk-control000000_data
local/scripts/reset.sh < /dev/null; echo "exit $?"
local/scripts/reset.sh --yes; echo "exit $?"
docker volume ls --filter label=com.docker.compose.project=iskender-local --quiet
docker volume ls --quiet --filter name=isk-control000000_data
docker volume rm isk-control000000_data
```

**Expected**: the first reset exits 64 and removes nothing. The second exits 0. No `iskender-local` volume remains, the control volume does, and `local/.secrets/iskender-local.env` is gone. The next `local/scripts/start.sh` generates new passwords and provisions a fresh volume.

## 7. Build and test with no credentials (US1.7, SC-001, SC-002)

```bash
dotnet format --verify-no-changes
dotnet build -warnaserror
dotnet test
bash .github/scripts/check-grounding.sh
bash .github/scripts/check-license.sh
```

**Expected**: all pass. With a container runtime, the tests in `tests/Iskender.Container.Tests/LocalEnvironment/` run 5 start, smoke, and reset cycles in a clean copy. Without one they are skipped with their reason, and with `CI=true` they fail instead.
