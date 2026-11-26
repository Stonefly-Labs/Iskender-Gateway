# Quickstart: ISK-0010 — System-Operator Bootstrap & Security Baseline

How to show that this spec works, once it is implemented. The rules are in the [operator-security contract](contracts/operator-security.md) and the four revisions beside it, and the types are in [data-model.md](data-model.md). This guide does not repeat them.

## Prerequisites

- The pinned .NET SDK from `global.json`. On this workstation, export `DOTNET_ROOT` and `PATH` for `~/.dotnet` first.
- A running container runtime for the PostgreSQL, container, and local environment tests. Without one, those tests skip with their reason locally, and fail when `CI=true`.
- `bash`, `git`, `curl`, and Docker Compose 2.24.0 or later for the developer topology.

## 1. Build, format, and the offline checks

```bash
dotnet format --verify-no-changes
dotnet build -warnaserror
bash .github/scripts/check-grounding.sh
bash .github/scripts/check-license.sh
```

Expected: each exits 0. No package is added. `Iskender.Management` gains a framework reference, not a package, so the license check sees no new entry.

## 2. Uninitialized admin routes deny access (User Story 1, SC-001)

```bash
dotnet test tests/Iskender.Identity.Tests --filter "FullyQualifiedName~Gate"
```

Expected: on an empty, migrated database, every mapped admin route except the two redemptions answers `403` with `operator.uninitialized` with no credential, a malformed credential, and a credential the test resolver accepts, and no row of `isk_identity` or `isk_audit` changes. A route the test adds through the supported mapping gets the same answer. With the store unreachable, every admin route, the redemptions included, answers `503` with `operator.state_unavailable`. An undeclared route fails start with `operator.route_undeclared`, and no listener binds.

## 3. The bootstrap token cannot be reused (User Story 2, SC-002, SC-003)

```bash
dotnet test tests/Iskender.Identity.Tests --filter "FullyQualifiedName~Redemption|FullyQualifiedName~Concurrency"
```

Expected:

- One redemption initializes the installation, with exactly one principal, one `system_operator` grant, one enrollment code returned once, and one success audit record.
- A second redemption of the same token, an expired, superseded, or wrong-purpose token, and a wrong secret each answer the uniform `403` `operator.redemption_failed`, with bodies identical apart from the correlation ID. Each writes one failure record naming its reason class.
- In each of at least 20 runs, 50 concurrent redemptions across two hosts on a fresh database end with exactly 1 success, 49 uniform failures, and 1 principal. The runs set `operator.bootstrap.attemptsPerMinute` to 600. A separate test shows the default limit answering `429`.
- `bootstrap issue` on an initialized installation exits 8.

## 4. Privilege checks fail closed (User Story 3, SC-006)

```bash
dotnet test tests/Iskender.Identity.Tests --filter "FullyQualifiedName~Privilege"
```

Expected: on an initialized installation, `GET <prefix>/installation` answers `401` `identity.unauthenticated` with no credential, and with no resolver composed. It answers `403` `identity.auth_denied` for a test principal with no grant, `200` for the operator principal, and `503` `operator.authorization_unavailable` with the grants unreadable or a resolver that throws.

## 5. Anonymous endpoints, health, and transport (User Story 4, SC-005, SC-009)

```bash
dotnet test tests/Iskender.Container.Tests --filter "FullyQualifiedName~AnonymousEndpoint|FullyQualifiedName~Transport|FullyQualifiedName~Bootstrap"
```

Expected:

- The inventory test passes for the data plane, the control plane, and the worker, and its negative fixture, with one unlisted anonymous endpoint, fails naming the process, listener, method, and route. The contract's inventory table matches `anonymous-endpoints.json`.
- Each readiness answer is byte for byte what container contract version 2 records. On the control plane's traffic listener, `/health/live` and `/health/ready` answer `404`.
- Without an environment name, a plane with `trafficTransport: none`, or with a missing, malformed, encrypted, expired, or mismatched certificate generated at run time, exits 2 and binds no listener. Its output names the field and never the path or content.
- With `listener` and a certificate generated at run time, a TLS 1.2 client completes a redemption against the control-plane image, and a plain-HTTP request to the same port runs no route.
- `bootstrap issue` prints only the token to standard output, and `bootstrap` on the data plane exits 64.

## 6. Audit and recovery (User Stories 5 and 6, SC-007)

```bash
dotnet test tests/Iskender.Identity.Tests --filter "FullyQualifiedName~Audit|FullyQualifiedName~Recovery|FullyQualifiedName~Enrollment"
```

Expected: every issue, supersede, success, and failure that names a stored token has exactly one record, committed with its change, and a rolled-back redemption leaves none. As the runtime login, `UPDATE` and `DELETE` on `isk_audit.records`, and on `isk_identity.installation`, are refused. 1,000 denied anonymous requests add 0 records and raise `iskender.identity.admin.denials` by 1,000. Recovery creates a second principal and leaves the first and its grant unchanged. A recovery token can't be issued before initialization (exit 9), can't be reused, and fails at the bootstrap route. An enrollment code is consumed once, only for its own principal.

## 7. Telemetry canary (SC-004)

```bash
dotnet test tests/Iskender.Identity.Tests --filter "FullyQualifiedName~Canary"
```

Expected: no token secret, no hash outside `isk_identity.tokens`, and no principal ID in logs, traces, or metrics are found anywhere in the captured output.

## 8. Embedding (User Story 7)

```bash
dotnet test tests/Iskender.Identity.Tests --filter "FullyQualifiedName~Embedding"
```

Expected: a test host with `AddIskenderControlPlane(...)`, the operator registrations, and its own prefix passes sections 2 to 4. Composing `AddIskenderOperatorSecurity()` under `AddIskender(...)` or `AddIskenderWorker(...)` fails start with `hosting.registration_missing`.

## 9. Migrations and rollback

```bash
dotnet test tests/Iskender.Infrastructure.PostgreSql.Tests --filter "FullyQualifiedName~Migration"
```

Expected: the forward order is `coordination`, `directory`, `work`, `audit`, `identity`. Applying ISK-0009's modules, then this release's, adds exactly `audit` and `identity`. `migrate reverse --steps 2 --confirm-data-loss` removes `identity`, then `audit`, and leaves every ISK-0009 module and row untouched.

## 10. Developer topology (User Story 7, SC-008)

```bash
local/scripts/start.sh
local/scripts/smoke.sh
local/scripts/bootstrap.sh issue; echo "exit $?"
curl -s -o /dev/null -w '%{http_code}\n' "http://127.0.0.1:${ISKENDER_LOCAL_CONTROL_PLANE_PORT:-18090}/admin/v1/installation"
local/scripts/stop.sh
```

Expected, before ISK-0021: smoke exits 0, with the three readiness answers unchanged and the admin assertions of local environment contract version 3. `bootstrap.sh issue` exits 5. The `curl` prints `503`. Nothing new is published except the control plane's traffic port on `127.0.0.1`. ISK-0008's five-cycle acceptance run passes, and a search of the tracked files finds no token.

## 11. Compatibility and architecture (SC-010)

```bash
dotnet test tests/Iskender.Contracts.Compatibility.Tests
dotnet test tests/Iskender.Architecture.Tests
```

Expected: `core-contracts` 7 adds only codes and a resource type, the `hosting` package's baseline does not change, and the architecture suite passes against rules contract version 5, unchanged. Run the architecture suite to a file, as it can be slow.
