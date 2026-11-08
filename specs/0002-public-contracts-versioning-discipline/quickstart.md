# Quickstart: ISK-0002 — Public Contracts & Versioning Discipline

These scenarios prove the feature end to end once it is implemented. The rules they exercise are in [contracts/public-contracts-and-versioning.md](contracts/public-contracts-and-versioning.md), and the types in [data-model.md](data-model.md).

## Prerequisites

- .NET SDK 10.0.403, as pinned in `global.json`.
- Packages restored once: `dotnet restore Iskender.sln --locked-mode`. Nothing after that needs a network or cloud credentials.
- `origin/main` fetched locally: `git fetch origin main`. The harness compares against the merge base.

Run everything from the repository root (`git rev-parse --show-toplevel`).

## 1. The whole gate passes (SC-001)

```bash
dotnet format --verify-no-changes
dotnet build -warnaserror
dotnet test
bash .github/scripts/check-grounding.sh
bash .github/scripts/check-license.sh
```

**Expected**: every command succeeds with zero warnings. The `dotnet test` output includes `Iskender.Contracts.Compatibility.Tests` and `Iskender.Core.Contracts.Tests`. The harness lists declared breaks and deprecations, which are none at first.

To confirm it runs offline, disconnect the network and repeat `dotnet test --no-restore`. **Expected**: the same result.

## 2. Every break kind is caught (SC-002, User Story 1)

```bash
dotnet test --project tests/Iskender.Contracts.Compatibility.Tests --filter-trait "Category=BreakCatalog"
```

**Expected**: one passing test per catalog rule (S1 to S9, W1 to W6, G1 to G5, V1 to V4). Each runs the harness on its negative fixture and asserts that the run fails with that rule ID. The compatible-change fixtures pass. Rule H8 fails if a catalog rule has no fixture.

## 3. A real break fails, then passes once declared (User Story 1)

1. On a scratch branch, delete a public member from a type in `src/Iskender.Core.Contracts`, then run `dotnet test`.

   **Expected**: the run fails with `S2`, naming `core-contracts`, the member's key, and the baseline. It also reports `H3`, because the baseline no longer matches, and `V2`, because the contract version was not incremented.

2. Increment the version in `[assembly: ContractPackage("core-contracts", ...)]`, copy the regenerated baseline from the `H3` message into `baselines/core-contracts.surface.txt`, and add a break record entry as the contract shows, naming the Architecture (ADR) owner.

   **Expected**: the run passes and lists the declared break.

3. Change `approvedBy` to any other role. **Expected**: `H2` fails.

Discard the scratch branch.

## 4. Hiding a break in the baseline fails (FR-036)

On a scratch branch, delete a public member and edit the baseline to match, without a break record entry.

**Expected**: `S2` still fails, because the comparison is made against the baseline at the merge base.

## 5. Missing inputs fail (FR-037)

On a scratch branch, delete `baselines/policy-sdk.surface.txt`, then, separately, `fixtures/wire/error-envelope/v1/`.

**Expected**: each fails with `H1`, naming what is missing. Running with `ISKENDER_COMPAT_BASE=does-not-exist` also fails with `H1`.

## 6. IDs are opaque and typed (SC-004, User Story 2)

```bash
dotnet test --project tests/Iskender.Core.Contracts.Tests --filter-class "*ResourceIdTests"
```

**Expected**:

- 1,000,000 generated IDs of one type have no duplicates, none contains the planted owner values or the generation time, and consecutive pairs are in ascending order between 49% and 51% of the time.
- Every ID of each other registered type fails to parse as the tested type with `core.resource_id.wrong_type`.
- Malformed, empty, oversized, upper-case, and set-pad-bit inputs fail with their `core.resource_id.*` codes, and no error message holds more than a bounded excerpt of the input.
- A failing random source makes generation fail with `core.random_source_failed`.
- `OperatorScope.Instance` never equals an organization or project scope.

## 7. Errors are safe and stable (SC-005, SC-006, User Story 3)

```bash
dotnet test --project tests/Iskender.Core.Contracts.Tests --filter-class "*GatewayErrorTests"
```

**Expected**:

- For all 13 categories, an error with a planted credential, prompt fragment, raw upstream body, and foreign tenant name in its diagnostics shows none of them in its envelope, and its `redaction_status` is `withheld`.
- A missing resource and another tenant's resource produce byte-identical envelopes apart from `correlation_id`.
- Retry advice on a category that forbids it, or on any error with `OutputVisibility.Visible`, fails with `core.error.retry_not_permitted`.
- An unregistered code fails with `core.error.unregistered_code`.

## 8. The request context holds (User Story 4)

```bash
dotnet test --project tests/Iskender.Core.Contracts.Tests --filter-class "*GatewayExecutionContextTests"
```

**Expected**: a second resolve fails with `core.context.already_resolved` and leaves the value unchanged; two concurrent contexts never see each other's values; a context whose deadline has passed reports `IsExpired`; and rule `H7` passes on the real surface.

## 9. The published contract matches its sources (User Story 5)

Change a safe message in `ErrorCodes.cs` without changing the contract table, then run `dotnet test`.

**Expected**: `H4` fails and names the code. Remove a `[ContractVersion]` attribute from a public type. **Expected**: `V1` fails and names the type.

## 10. Scope stayed inside the spec (SC-009, FR-044)

```bash
git diff --stat origin/main...HEAD -- .specify .iskender/grounding.yaml specs/0000-repo-bootstrap specs/0001-solution-dependency-boundaries-build-conventions
git diff origin/main...HEAD -- Directory.Packages.props
```

**Expected**: the first command lists no file, other than the ignored `.specify/feature.json`, which Git does not track. The second shows no new package. The ISK-0001 architecture suite, run by `dotnet test`, still passes, which proves no denied package or transport type entered `Iskender.Core.Contracts`.
