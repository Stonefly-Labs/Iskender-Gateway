# Quickstart: validating ISK-0005

How to prove the coordination foundation works. The rules are in [contracts/coordination.md](contracts/coordination.md) and [contracts/persistence.md](contracts/persistence.md) (version 2), and the fixtures in [data-model.md](data-model.md#test-fixtures).

## Prerequisites

- The pinned .NET SDK from `global.json`, with packages restored (`dotnet restore Iskender.slnx --locked-mode`).
- A container runtime (Docker or a compatible engine). The first run pulls the digest-pinned PostgreSQL 17 and Valkey 8 images once. No cloud credentials are needed.
- Without a container runtime, the same commands still pass, with every container-backed test reported as skipped and its reason. Setting `CI=true` makes those tests fail instead, as in CI.

## 1. The whole gate

```bash
dotnet format --verify-no-changes
dotnet build -warnaserror
dotnet test
bash .github/scripts/check-grounding.sh
bash .github/scripts/check-license.sh
```

Expected: every command exits 0. `check-license.sh` lists `StackExchange.Redis 3.4.31 MIT` and its dependencies as allowed, and the allow-list is unchanged. With a container runtime, 0 tests are skipped in `Iskender.Infrastructure.PostgreSql.Tests` and `Iskender.Infrastructure.Redis.Tests` (SC-001).

## 2. Acceptance evidence

These two groups are the spec's acceptance evidence (FR-030).

| Group | Projects | Proves | Expected |
|---|---|---|---|
| Refuses an unsupported coordinator | `Iskender.Domain.Tests`, `Iskender.Infrastructure.Tests`, `Iskender.Infrastructure.PostgreSql.Tests` | User Story 1, SC-002 | Every row of the requirement table gives its expected result and reason; all 5 refusal reasons occur; a refused feature's calls fail with `coordination.capability_unsupported` and touch no state; a second `single_instance` process is refused with `single_instance_violated` |
| Lease fencing | `Iskender.Infrastructure.Tests`, `Iskender.Infrastructure.PostgreSql.Tests`, `Iskender.Infrastructure.Redis.Tests` | User Story 2, SC-003 | Against each implementation: a later holder's token is greater; the stale holder's write fails with `coordination.fencing_rejected` and commits nothing; renewal after expiry fails with `coordination.lease_lost`; 20 competing clients never overlap and never reuse a token |

SC-003 and SC-004 ask for 20 consecutive passes. Run each group 20 times in a loop and check every run exits 0.

## 3. Other scenarios

| Scenario | Proves | Expected |
|---|---|---|
| 2 clients × 100 reservations against limit 100 | User Story 3, SC-004 | Exactly 100 granted, value 100, on Redis and PostgreSQL |
| 50 compare-and-set calls with one version | User Story 3 | Exactly one succeeds |
| Stopped and paused coordinator | User Story 3, FR-022 | `coordination.unavailable` or `coordination.timeout` within the timeout; never granted; an unknown write outcome is marked and not retried |
| Version mismatch and namespace bump from another client | User Story 4 | Misses on Redis, PostgreSQL, and local |
| Cache read with the coordinator stopped | FR-020 | A miss and event 5008, no exception |
| Cross-scope state for every primitive, 2 organizations and 2 projects | SC-007 | No scope sees another's state, including a project and its organization |
| Scope-keyed tables with the repository predicate omitted | Persistence contract Isolation rule 8 | Row-level security alone keeps each scope to its own rows |
| Probe against each condition | User Story 5, SC-006 | Each of the 7 reasons, within the timeout plus 2 seconds |
| Canary across the whole suite and the Redis key space | SC-005 | 0 occurrences |
| `core-contracts` harness | SC-006 | Raised 3 → 4, compatible only, other packages unchanged |
| Diff of the Advisory locks section, versions 1 and 2 | SC-008 | Identical |

## 4. Configuration examples

A two-replica deployment without Redis:

```yaml
iskender:
  coordination:
    topology: multi_replica
    atomic: { coordinator: postgresql }
    leases: { coordinator: postgresql }
    cache: { coordinator: postgresql }
```

The same with Redis for counters and the cache, keeping fenced leases on PostgreSQL:

```yaml
iskender:
  coordination:
    topology: multi_replica
    atomic: { coordinator: redis }
    leases: { coordinator: postgresql }
    cache: { coordinator: redis }
    redis:
      endpoints: [ "redis.internal:6380" ]
      username: iskender
      password: { secretRef: "redis-password" }
```

The `secretRef` form follows the ISK-0003 configuration contract. With no `coordination` section at all, every primitive is `local` in `multi_replica`, so every distributed feature is refused with `local_in_multi_replica`.
