# Quickstart: validating ISK-0004

How to prove the persistence foundation works. The rules are in [contracts/persistence.md](contracts/persistence.md), and the fixtures in [data-model.md](data-model.md#test-fixtures).

## Prerequisites

- The pinned .NET SDK from `global.json`, with packages restored (`dotnet restore Iskender.slnx --locked-mode`).
- A container runtime (Docker or a compatible engine) for the integration tests. The first run pulls the digest-pinned PostgreSQL 17 image once. No cloud credentials are needed.
- Without a container runtime, the same commands still pass, with every container-backed test reported as skipped and its reason. Setting `CI=true` makes those tests fail instead, as in CI.

## 1. The whole gate

```bash
dotnet format --verify-no-changes
dotnet build -warnaserror
dotnet test
bash .github/scripts/check-grounding.sh
bash .github/scripts/check-license.sh
```

Expected: every command exits 0. `check-license.sh` lists `Npgsql 10.0.3 PostgreSQL` as allowed. With a container runtime, the test summary shows 0 skipped tests in `Iskender.Infrastructure.PostgreSql.Tests` (SC-001).

## 2. Acceptance evidence

These two groups are the spec's acceptance evidence (FR-036). Filter them with the test platform's filter options on the `Iskender.Infrastructure.PostgreSql.Tests` project.

| Group | Proves | Expected |
|---|---|---|
| Migration round trip | User Story 1, SC-002 | 7 fixture migrations in 2 modules apply in dependency order; each reverse step matches the earlier snapshot; forward again matches the first run; the failing script, edited script, and unconfirmed data-losing reverse are refused with their codes |
| Parallel transactions | User Story 2, SC-003 | 20 optimistic writers give 1 winner and 19 `persistence.concurrency_conflict`; 20 serializable increments lose and double nothing; advisory-locked appends never interleave; locks in different scopes do not block each other; 2 concurrent migrators apply each migration once |

SC-003 asks for 20 consecutive passes of the parallel group. Run it 20 times in a loop and check every run exits 0.

## 3. Other scenarios

| Scenario | Proves | Expected |
|---|---|---|
| Cross-scope reads, updates, deletes, and references, with and without the repository predicate | User Story 3, SC-004 | Every attempt returns not found or changes 0 rows |
| Operator scope on a tenant table | FR-007 | `persistence.scope_not_permitted`; with the pre-check bypassed, 0 rows |
| Pooled connection reuse across scopes | FR-008 | No setting, temporary table, or lock carries over |
| Probe against each failure condition | User Story 4, SC-006 | Each of the 10 reasons, each within its timeout plus 2 s |
| Canary search | SC-005 | The canary appears 0 times in any output |
| Previous-release queries on the next schema | FR-024 | Pass, and the probe reports `ok`; beyond `Maximum` it reports `schema_ahead` |
| Parallel test classes with the same keys | User Story 5 | Both pass, neither sees the other's rows |
| Error code coverage | SC-007 | Each of the 12 codes is produced by at least one test |

## 4. Compatibility and boundaries

- The compatibility harness reports `core-contracts` at 3 with compatible changes only, and no change to `providers-abstractions` or `policy-sdk` (SC-007).
- The architecture suite passes unchanged, and the adapter's module-namespace check passes (FR-002, FR-002a).
- `git diff --stat origin/main` shows no change under `.specify/`, to `.iskender/grounding.yaml`, to the ISK-0001 rules contract, or to a host project (SC-008).

## 5. Trying it by hand

With the test fixture's container running, you can connect with `psql` as the migration login and check:

- `SELECT module, version, applied_by FROM isk_migrations.history ORDER BY applied_at;` lists the fixture migrations.
- `SELECT relname, relrowsecurity, relforcerowsecurity FROM pg_class WHERE relnamespace = 'isk_alpha'::regnamespace;` shows row-level security forced on the tenant tables.
- While a migration runs, `SELECT * FROM pg_locks WHERE locktype = 'advisory' AND classid = 1433339375 AND objid = 1001676181;` shows the migration lock.
