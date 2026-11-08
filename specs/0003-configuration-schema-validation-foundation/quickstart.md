# Quickstart: ISK-0003 — Configuration Schema & Validation Foundation

How to prove the feature works. Rules are in the [configuration contract](contracts/configuration.md), and types are in the [data model](data-model.md).

## Prerequisites

- .NET SDK 10.0.403, as pinned in `global.json`.
- Packages restored once (`dotnet restore --locked-mode`). Everything after that runs offline, with no cloud credentials.
- `origin/main` fetched, for the compatibility harness (`git fetch origin main`).

## 1. The whole gate

```bash
dotnet format --verify-no-changes
dotnet build -warnaserror
dotnet test
bash .github/scripts/check-grounding.sh
bash .github/scripts/check-license.sh
```

**Expected**: every command exits 0, with zero warnings (SC-001). `check-license.sh` reports the three new packages as MIT.

## 2. Scenarios

Each scenario is an automated test in `tests/Iskender.Domain.Tests` unless noted. Run one area with a filter, such as `dotnet test tests/Iskender.Domain.Tests --filter "FullyQualifiedName~Overlay"`.

| # | Scenario | Fixtures | Expected | Covers |
|---|---|---|---|---|
| 1 | A document with five independent defects | `Fixtures/invalid/five-defects.yaml` | One failed report holding five errors in source order, each with code, path, line, column, layer, problem, and fix. No snapshot, no revision | User Story 1, FR-018, FR-019, FR-022 |
| 2 | A near-miss field name | `Fixtures/invalid/unknown-field-near-miss.yaml` | `config.field.unknown` at `iskender.<ns>.conectionTimeout`, with a fix naming `connectionTimeout` | User Story 1 scenario 1, SC-006 |
| 3 | One negative fixture per error code | `Fixtures/invalid/<code>/*` with `.expect.json` sidecars | Each fails with exactly its expected code, path, and line. The two `config.schema.*` codes are proved by registry tests, and `config.section.invalid_access` by section access tests | SC-002, FR-035 |
| 4 | Every edge case in the spec | `Fixtures/edge/*` | As each sidecar states | FR-035, FR-036 |
| 5 | Canary | Every negative fixture, with `ISKENDER-CANARY-7f3a` planted in every value | The canary appears 0 times in reports, renderings, exception messages, and captured log events | User Story 2 scenario 7, SC-003 |
| 6 | Alias bomb | `Fixtures/invalid/yaml-alias-bomb.yaml`, which would expand past 1,000,000 nodes | `config.document.unsupported_construct` at the first alias. Allocation measured after a warm-up stays below 1 MiB, and a bomb expanding 1,000 times further allocates within 64 KiB of it | SC-004 |
| 7 | Revision determinism | `Fixtures/equivalent/<pair>/` (at least 5 pairs: format, key order, whitespace, comments, layer split) | Equal revisions within each pair | SC-005, FR-028 |
| 8 | Revision sensitivity | `Fixtures/valid/full.json` with 20 single-value changes made in the test | 20 distinct revisions, all different from the original | SC-005 |
| 9 | Overlay precedence | `Fixtures/overlay/base.yaml`, `overlay.production.yaml`, `env.json`, `expected.json` | The merged snapshot's canonical form equals `expected.json`'s. The variable wins over the overlay, the overlay over the base, the base over the default. Arrays are replaced whole | User Story 4 |
| 10 | Section isolation | Two test schemas, `alpha` and `beta` | Each schema reads only its own section. A copy of `alpha`'s schema fails. A duplicate registration fails with `config.schema.duplicate_namespace` | User Story 3 |
| 11 | Unsafe values | `Fixtures/invalid/secret-plaintext.yaml`, `secret-in-plain-field.yaml`, `secret-ref-traversal.yaml`, `dev-only-in-production.yaml` (validated with `production` and with no environment) | `config.secret.plaintext`, `config.field.type_mismatch`, `config.secret.reference_malformed`, `config.value.unsafe_for_environment` | User Story 2 |
| 12 | Diagnostics | A valid and an invalid fixture, with a capturing logger | One 3001 event, or one 3002 per error and one 3003. Text rendering lines match the contract's form | User Story 5 |
| 13 | Snapshot derivation | Reflection over every production assembly in the repository | No type outside `Iskender.Domain.Registry` derives from `ConfigurationSnapshot` or `ConfigurationSection` | Contract known limits, research R10 |
| 14 | Public types | `tests/Iskender.Core.Contracts.Tests` | `SecretReference` rejects malformed names, and the report's factories reject a failure with no errors and a success with no snapshot | FR-022, FR-025 |

## 3. Compatibility

```bash
dotnet test tests/Iskender.Contracts.Compatibility.Tests
```

**Expected**: `core-contracts` reports version 2, with only additions, 0 undeclared breaks, and no change to `providers-abstractions` or `policy-sdk` (SC-007). The committed baselines equal the regenerated ones.

## 4. Boundaries

```bash
git diff --stat origin/main...HEAD -- .specify .iskender/grounding.yaml specs/0000-repo-bootstrap specs/0001-solution-dependency-boundaries-build-conventions
```

**Expected**: no output (SC-008). Review confirms that the PR adds no host wiring, database table, network call, CEL or policy code, or secret resolution (FR-038), and that `dotnet test tests/Iskender.Architecture.Tests` passes with the engine's types in `Iskender.Domain.Registry` (rules A17, A18, A19).

## 5. Trying it by hand

A test or a host calls the engine directly. In outline:

1. Declare a `SectionSchema` for a namespace, with its fields.
2. Build a `ConfigurationSchemaRegistry` from the schemas.
3. Create a `ConfigurationLoadRequest` with the registry, a base `ConfigurationDocumentSource`, an optional overlay, an optional `ConfigurationEnvironmentSource`, and the environment name.
4. Call `ConfigurationValidator.Validate(request, logger)`.
5. Print the report. A valid one ends `Configuration valid: revision cfg-...`. A rejected one lists each error and ends `<n> configuration error(s); configuration rejected.`
