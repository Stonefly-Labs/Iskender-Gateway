# Quickstart: ISK-0009 — Module Events & Background Work Coordination

How to show that this spec works, once it is implemented. The rules are in the [work contract](contracts/work.md) and the four revisions beside it, and the types in [data-model.md](data-model.md). This guide does not repeat them.

## Prerequisites

- The pinned .NET SDK from `global.json`. On this workstation, export `DOTNET_ROOT` and `PATH` for `~/.dotnet` first.
- A running container runtime for the PostgreSQL, container, and local environment tests. Without one, those tests skip with their reason locally, and fail when `CI=true`.
- `bash`, `git`, and Docker Compose 2.24.0 or later for the developer topology.

## 1. Build, format, and the offline checks

```bash
dotnet format --verify-no-changes
dotnet build -warnaserror
bash .github/scripts/check-grounding.sh
bash .github/scripts/check-license.sh
```

Expected: each exits 0. No package is added, so the license check sees no new entry.

## 2. Crash and duplicate delivery (User Stories 1 and 2, SC-001, SC-002)

```bash
dotnet test tests/Iskender.Work.Tests --filter "FullyQualifiedName~Crash|FullyQualifiedName~Duplicate|FullyQualifiedName~Fencing|FullyQualifiedName~Shards"
```

Expected:

- The crash tests kill `Iskender.Work.CrashHost` with `SIGKILL` at each injection point (after the producer's commit, after the attempt record, during the handler, after the apply's commit), at least 20 times per point, and every committed item ends `completed` with exactly one recorded effect. No item exists for a rolled-back producer.
- Two dispatchers forced onto the same item commit it once. A dispatcher paused past its lease's expiry is rejected with `coordination.fencing_rejected` and event 5006, and nothing it wrote persists.
- With as many dispatchers as shards and items in every shard of one scope, more than one dispatcher handles that scope, none exceeds its in-flight caps, and dispatchers with shard counts 2 and 4 together apply no item twice.

## 3. Retry, poison, and skew (User Stories 3 and 7, SC-003, SC-004)

```bash
dotnet test tests/Iskender.Work.Tests --filter "FullyQualifiedName~Retry|FullyQualifiedName~Poison|FullyQualifiedName~Skew"
```

Expected: transient retries fall within the backoff bounds of the work contract's Retry rule 2; a permanent failure poisons on its first attempt; an `unclassified` failure and a handler that kills its process are poisoned after exactly their maximum attempts and never dispatched again; other items keep flowing; an item of an unknown kind stays `pending` with no attempt counted, is never claimed, and is reported by `iskender.work.items.skewed` and event 8008.

## 4. Isolation and the directory (User Story 4, SC-005)

```bash
dotnet test tests/Iskender.Work.Tests --filter "FullyQualifiedName~Isolation|FullyQualifiedName~Directory"
```

Expected: each handler's unit is in its item's scope; a tenant unit reads no other scope's directory row or item and cannot register or delete another scope's row; an operator unit reads every directory row and no tenant item; a malformed directory row is skipped with event 8009 and opens no unit; the removal race test, which settles a row while producers enqueue, leaves no committed item without its row. Each test passes with the repository's scope predicate removed as well as present.

## 5. Jobs (User Story 5, SC-006)

```bash
dotnet test tests/Iskender.Work.Tests --filter "FullyQualifiedName~Jobs"
```

Expected: expired atomic-operation and lease rows in three scopes are deleted, every fence record and namespace generation stays, a scope's `coordination` row is removed once nothing expiring remains, two workers never run a job at once, and a job killed mid-run finishes on the next run with nothing changed beyond the deletes.

## 6. Shutdown, telemetry, and the worker command (User Stories 6 and 7, SC-007, SC-008)

```bash
dotnet test tests/Iskender.Work.Tests --filter "FullyQualifiedName~Shutdown|FullyQualifiedName~Telemetry"
dotnet test tests/Iskender.Container.Tests --filter "FullyQualifiedName~Worker"
```

Expected: after `SIGTERM`, readiness is `stopping`, no item is claimed, a short item completes, a long one is cancelled and stays `pending` with its lease released, and the process exits 0 within `shutdownDelay` + `shutdownTimeout` + 5 seconds with event 7005. A dispatch activity links to the trace that enqueued it and is not its child. The captured logs, spans, and metrics of the whole run hold no scope ID, item ID, idempotency key, or payload marker. `work` on the data plane exits 64, `work extra` exits 64, a `work` section in a control-plane document exits 2, a worker given Redis leases exits 4 with event 7002 naming `work.dispatch`, and the worker's readiness has the five checks.

## 7. The developer topology (User Story 8, SC-009)

```bash
local/scripts/start.sh
local/scripts/smoke.sh
docker compose --project-name iskender-local -f local/compose.yaml logs worker | grep -E '"EventId":(5002|7002)' && echo "unexpected refusal"
local/scripts/reset.sh --yes
```

Expected: start exits 0 with `postgres`, `valkey`, `data-plane`, `control-plane`, and `worker` healthy; smoke exits 0, which asserts the two existing readiness answers byte for byte and the worker's answer as the local environment contract's Smoke assertion 3 records it; the log search prints nothing. The 5-cycle acceptance run is `dotnet test tests/Iskender.Container.Tests --filter "FullyQualifiedName~LocalEnvironment"`.

## 8. Everything

```bash
dotnet test
```

Expected: every test passes, including the compatibility harness with `core-contracts` at version 6 and the `hosting` baseline unchanged, and the architecture suite against rules contract version 5 (SC-010).
