# Quickstart: validating ISK-0006

How to prove that an application embeds Iskender without a standalone process. The rules are in [contracts/hosting.md](contracts/hosting.md) and [contracts/module-dependency-rules.md](contracts/module-dependency-rules.md) (version 4), and the fixtures in [data-model.md](data-model.md#test-fixtures).

## Prerequisites

- The pinned .NET SDK from `global.json`, with packages restored (`dotnet restore Iskender.slnx --locked-mode`).
- No container runtime is needed, except for the one single-instance lease test, which pulls ISK-0004's digest-pinned PostgreSQL 17 image on its first run. Without a runtime that test is skipped with its reason, and setting `CI=true` makes it fail instead, as in CI. No cloud credentials are needed.

## 1. The whole gate

```bash
dotnet format --verify-no-changes
dotnet build -warnaserror
dotnet test
bash .github/scripts/check-grounding.sh
bash .github/scripts/check-license.sh
```

Expected: every command exits 0. `check-license.sh` lists `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, and `Microsoft.AspNetCore.TestHost` 10.0.14 as `MIT`, and the allow-list is unchanged. The build includes `samples/Iskender.Samples.EmbeddedHost` (SC-001).

## 2. Acceptance evidence

All of these run in `tests/Iskender.Hosting.Tests` through the `EmbeddedHost` fixture, on the in-memory server. They are the spec's acceptance evidence (FR-025).

| Group | Proves | Expected |
|---|---|---|
| Embedded composition | US1, SC-002 | The sample starts in process 20 times in a row. Each run resolves the snapshot, its revision, the schema registry with `coordination` and `hosting`, and the coordination services, and starts no process. A PostgreSQL host resolves `IUnitOfWorkFactory` with no connection opened. Two hosts in one process see different revisions, and disposing one leaves the other answering |
| Start-up refusal | US2, SC-003 | Each of ISK-0003's 15 `invalid_request` configuration codes stops start with `hosting.configuration_rejected`, its text rendering in the diagnostics, and events 3002, 3003, and 6002 in the log. A development-only value under a host running in `Development` with no Iskender environment name is rejected with `config.value.unsafe_for_environment`. Without the opt-in, `ISKENDER__` variables are ignored |
| Coexistence | US3, SC-004 | `/hello` answers with the same status, body, and headers, apart from `Date`, with and without Iskender. `/iskender/anything` answers 404 with the `core.resource.not_found` envelope and a `req_` correlation ID, and the sample's middleware header is present. `/missing` gets the host's own 404. The host's configuration keys are unchanged |
| Lifetimes and state | US4, SC-006 | Container validation passes for every registration combination. The static-state scan reports no unlisted field. 100 concurrent requests have 100 distinct contexts and correlation IDs. After disposal, no Iskender timer or connection remains |
| Registration mistakes | US5, SC-007 | Each row of the contract's Registration mistakes table fails with its code. Each of the three `hosting.*` codes is produced by at least one test |

To run only these:

```bash
dotnet test --project tests/Iskender.Hosting.Tests
```

## 3. Run the sample by hand

```bash
dotnet run --project samples/Iskender.Samples.EmbeddedHost
curl -i http://localhost:5000/hello
curl -i http://localhost:5000/iskender/anything
```

Expected: `/hello` returns the sample's greeting with its own header. `/iskender/anything` returns `404` with an `application/json` error envelope whose `code` is `core.resource.not_found`. The log holds events 3001 and 6001, naming the same revision. The port is whatever `dotnet run` reports. This is a convenience check: the acceptance evidence is section 2, which opens no port.

To see a refused start, edit `samples/Iskender.Samples.EmbeddedHost/iskender.yaml` to misspell a field, and run again. The host exits with `hosting.configuration_rejected`, and the log shows `config.field.unknown` with its source, line, and column, and no value.

## 4. Compatibility and architecture

```bash
dotnet test --project tests/Iskender.Contracts.Compatibility.Tests
dotnet test --project tests/Iskender.Architecture.Tests
```

Expected: the harness reports `hosting@1` with its new baseline, `core-contracts` raised from 4 to 5 with three compatible error code additions and no break, and no change to `providers-abstractions` or `policy-sdk` (SC-007). The architecture suite passes against rules contract version 4, including A20's negative fixture (SC-008).

## 5. Scope checks

```bash
git diff --stat origin/main -- .specify .iskender/grounding.yaml
```

Expected: no output (SC-008). Review confirms that the PR adds no provider ingress endpoint, authentication scheme, readiness endpoint, container entry point, logging provider, or exporter.
