# Quickstart: Validating ISK-0001

These scenarios prove the spec end to end. The rules are in [contracts/module-dependency-rules.md](contracts/module-dependency-rules.md), and the test inputs are in [data-model.md](data-model.md).

## Prerequisites

- .NET SDK 10.0.403 or a later feature band, as `global.json` allows.
- Bash, for the repository scripts.
- A NuGet feed or a primed package cache for the first restore. Nothing else needs a network or a credential.

## 1. Fresh clone (SC-001)

From a clean checkout with no `bin/` or `obj/`:

```bash
git clean -xdn   # Confirm that nothing untracked would remain, then run with -f if needed
dotnet restore Iskender.sln --locked-mode
dotnet format Iskender.sln --verify-no-changes --no-restore
dotnet build Iskender.sln --configuration Release --no-restore -warnaserror
dotnet test --solution Iskender.sln --configuration Release --no-build
bash .github/scripts/check-grounding.sh
bash .github/scripts/check-license.sh
```

**Expected**: Every command exits 0, and the build reports 0 warnings and 0 errors.

## 2. Architecture rules alone (FR-033)

```bash
dotnet test --project tests/Iskender.Architecture.Tests --configuration Release --no-build
```

**Expected**: All tests pass, including:

- The repository scan, with no violations.
- The `docs/03` comparison, with 12 of 12 modules mapped (SC-002).
- The meta-test showing that every rule has a negative fixture (SC-003).
- The pack closure check (SC-007).

To run with no network (SC-009), disconnect after the restore in scenario 1 and repeat this command. **Expected**: The same result.

## 3. A forbidden reference is rejected (US2, SC-004)

In a scratch branch, add this to `src/Iskender.Management/Iskender.Management.csproj`:

```xml
<ItemGroup Condition="'$(NeverTrue)' == 'true'">
  <ProjectReference Include="..\Iskender.Infrastructure\Iskender.Infrastructure.csproj" />
</ItemGroup>
```

Rerun scenario 2. **Expected**: The run fails, even though the condition is never true, and the output includes the following in one run. Discard the change afterward.

```text
A3: Iskender.Management -> Iskender.Infrastructure: ... See specs/0001-solution-dependency-boundaries-build-conventions/contracts/module-dependency-rules.md (version 2).
A5: Iskender.Management -> Iskender.Infrastructure: ... See specs/0001-solution-dependency-boundaries-build-conventions/contracts/module-dependency-rules.md (version 2).
```

## 4. Planes stay apart (US3, SC-005)

The architecture tests assert this on every run. To see it directly:

```bash
dotnet list src/Iskender.Gateway.Host/Iskender.Gateway.Host.csproj reference
dotnet list src/Iskender.ControlPlane.Host/Iskender.ControlPlane.Host.csproj reference
```

**Expected**: The first lists no `Iskender.Management`, and the second lists no `Iskender.Hosting`. Neither reaches the other's surface through its other references, which the A6 and A7 fixtures prove transitively.

## 5. The module map follows `docs/03` (US1)

In a scratch branch, add a row to the Module boundaries table in `docs/03-domain-and-technical-architecture.md`, then rerun scenario 2. **Expected**: An M1 violation naming the new module. Discard the change, remove one entry from `modules` in `tests/Iskender.Architecture.Tests/module-dependency-rules.json`, and rerun. **Expected**: M1 again, naming the removed module, along with C1 because the contract no longer matches. Discard the change.

## 6. Conventions cannot be weakened (US4, SC-006)

The fixtures cover each protected setting. To try one by hand, add `<Nullable>disable</Nullable>` to any project file. **Expected**: A B4 violation naming the project and the setting. Discard the change.

## 7. Packages are complete (US5, SC-007)

```bash
dotnet pack Iskender.sln --configuration Release --no-build -p:Version=0.0.1-alpha --output artifacts/packages
ls artifacts/packages
```

**Expected**: 7 packages: `Iskender.Core.Contracts`, `Iskender.Providers.Abstractions`, `Iskender.Policy.Sdk`, `Iskender.Hosting`, `Iskender.Domain`, `Iskender.Infrastructure`, and `Iskender.Management`. Neither host and no test project is present. `artifacts/` is ignored by Git.

## 8. Both hosts start with no gateway behavior (FR-008, FR-036)

```bash
dotnet run --project src/Iskender.Gateway.Host --no-build --configuration Release
dotnet run --project src/Iskender.ControlPlane.Host --no-build --configuration Release
```

**Expected**: Each process starts, maps no endpoints, and stops on Ctrl+C. Any request returns 404.

## 9. Scope limits (SC-008)

```bash
git diff --stat origin/main...HEAD -- specs/0000-repo-bootstrap .iskender/grounding.yaml .specify
git diff origin/main...HEAD -- Directory.Packages.props
git diff --name-only --diff-filter=A origin/main...HEAD -- 'src/*.cs'
```

**Expected**:

- The first command prints nothing.
- The second shows only the two central-management settings, with no new `PackageVersion`.
- The third lists only `src/Iskender.ControlPlane.Host/Program.cs`.

## 10. Documentation gate (FR-028)

```bash
DOCS_GATE_CHANGED_FILES='src/Iskender.Hosting/Iskender.Hosting.csproj' DOCS_GATE_LABELS='' bash .github/scripts/docs-gate.sh
DOCS_GATE_CHANGED_FILES='src/Iskender.ControlPlane.Host/Program.cs' DOCS_GATE_LABELS='' bash .github/scripts/docs-gate.sh
```

**Expected**: Each exits 1 with `BLOCKED: no documentation change in this PR`, which shows that both paths are now behavior-bearing.
