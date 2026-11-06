# Research: ISK-0001 — Solution Dependency Boundaries & Build Conventions

The spec has no open `NEEDS CLARIFICATION` items. This file records how the plan meets the requirements that leave the technique open, and what a spike on 2026-11-06 proved against the repository at `ea87798`. The spike ran in a scratch copy, and nothing from it was committed.

## R1. Where the machine-readable rule source lives

- **Decision**: `tests/Iskender.Architecture.Tests/module-dependency-rules.json`.
- **Rationale**: FR-004 lets a later spec revise the contract. If the source lived under this spec's directory, a revision would have to edit a completed record, which is the situation FR-001 avoids for ISK-0000. A fixed path also gives CODEOWNERS one line to protect (FR-003), and that line covers the code that enforces the rules. Each revision updates `contract.path` and `contract.version` in the source, so violation messages always name the current contract.
- **Alternatives rejected**: `specs/0001-*/contracts/*.json`, for the reason above. `.iskender/`, which holds workflow state and grounding, not build rules. An entry in `.iskender/grounding.yaml`, which this spec must not change (SC-008) and which only registers grounding paths.

## R2. Source format

- **Decision**: JSON, read with `System.Text.Json`.
- **Rationale**: It is in the base class library, so it needs no new package (FR-024), and a malformed file fails loudly.
- **Alternatives rejected**: YAML needs a parser package. C# constants are what ISK-0000 did, and the prose and code drifted apart (TR-5).

## R3. Keeping the contract prose honest

- **Decision**: The contract is written by hand, and rule C1 parses its tables and compares each with the source, in both directions.
- **Rationale**: FR-002 allows generation or checking. Checking needs only the Markdown table reader that the `docs/03` comparison (FR-007) already needs, and adds no generator to run or forget.
- **Alternatives rejected**: A generator, which adds a tool and a stale-output failure mode with no extra safety.

## R4. How project files are read

- **Decision**: Parse project files as XML and follow their imports. Do not evaluate them with MSBuild.
- **Rationale**: FR-013 has to catch a reference hidden behind a build condition. MSBuild evaluation reports only the branch that is active for the current properties, so it would miss the case the rule exists for. Reading the XML sees every branch. Following `<Import>` elements, reading a root `Directory.Build.targets` as an import of every project, and treating any `Directory.Build.*` below the root as a violation, covers imported files. It also adds no package.
- **Limits**:
  - An `Include` or `Import` containing an MSBuild property that the reader cannot resolve is a violation (A13), rather than a skipped entry. `$(MSBuildThisFileDirectory)` and `$(MSBuildProjectDirectory)` are the only properties resolved.
  - An SDK can inject anything, and the reader cannot see inside one. So A13 approves `Microsoft.NET.Sdk` and `Microsoft.NET.Sdk.Web` by name, on the `Project` element, an `Sdk` element, or an `Import`, and rejects any other.
  - Properties passed on the command line are out of reach. CI's command lines are reviewed through `.github/` CODEOWNERS.
- **Alternatives rejected**: `Microsoft.Build` with `Microsoft.Build.Locator`, for the conditions problem and the extra pins. `dotnet msbuild -getProperty`, which has the same conditions problem and starts a process per project.

## R5. Which files count as project files

- **Decision**: Every `*.*proj` file in the repository, skipping only `bin/`, `obj/`, `artifacts/`, `TestResults/`, `.git/`, and `node_modules/`.
- **Rationale**: FR-013 says every project file regardless of directory, except build output. The ISK-0000 loader scans only `src/` and `tests/`, which is the "placed elsewhere" gap in User Story 2. Rule A14 then rejects any location other than those two directories.

## R6. Telling test projects from production projects

- **Decision**: A project is a test project only if it sits under `tests/`. The name plays no part in the exemption. A project under `src/` named `*.Tests` also gets its own violation (A14).
- **Rationale**: FR-014, and the first edge case in the spec. The ISK-0000 graph used `EndsWith(".Tests")`, so this is a correction of an existing gap.
- **Consequence**: A helper project under `tests/` that does not end in `.Tests`, such as the fixtures project in R8, is a test project.

## R7. Type-level module rule, and what NetArchTest does

- **Decision**: Use NetArchTest 1.3.2, already pinned, with `HaveDependencyOn` for the cross-module check. Filter its `ResideInNamespace` selection to an exact namespace match in the test code.
- **Spike evidence**:
  - `ResideInNamespace("Iskender.Domain.Policy")` also selected `Iskender.Domain.PolicyExtras.B`, so it matches by prefix. Without a filter, a type in a new `PolicyExtras` namespace could be reported as a Policy type.
  - `HaveDependencyOn("Iskender.Domain.Policy")` did **not** fire for a type that only used `PolicyExtras`, so its matching is safe.
  - It did fire for a dependency that appeared only in a method body, a generic argument, an attribute, or `typeof`.
  - `Types.InAssembly(...).GetTypes()` listed declared types and no compiler helper types, even with nullable annotations in use, so the type-home rule (A18) needs no exclusion list. It did list `Program` from top-level statements, which is the only synthesized name that appeared. No library project uses top-level statements.

## R8. Where negative fixtures live

FR-029 asks for synthetic graphs, on-disk projects, and type-level examples.

- **Decision**:
  - **Synthetic graphs** stay inline in the tests, as ISK-0000 did.
  - **On-disk projects** are written to a temp directory at test time by a helper that copies the real repository's project and build files, applies one mutation, and runs the scan. Nothing tracked contains a violating project file, because the FR-013 repository scan would then find it. Starting from the real files proves that the mutation alone caused the violation.
  - **Type-level examples** go in a new test-role project, `tests/Iskender.Architecture.Fixtures`, which declares types in `Iskender.Domain.*` namespaces with real violations. It references no production project and is not packable. A18 and A17 are run on this assembly with an exact expected result, and on the real `Iskender.Domain`, which must be clean.
- **Rationale**: Empty assemblies prove nothing (spec edge case), and the namespace rule needs compiled IL.
- **Alternatives rejected**: Compiling fixtures at test time with Roslyn, which needs a `Microsoft.CodeAnalysis` pin for what a small project does. Tracked `*.csproj` fixtures, for the scan conflict above.
- **Constraint for fixture authors**: A literal such as `#nullable disable` in test code must not start a line, or the source scan (B5, B6) will flag the test file itself. Write it inside a one-line string.

## R9. Proving each rule has a fixture

- **Decision**: A `[ProvesRule("A5")]` attribute on each negative test. A meta-test reflects over the test assembly and fails if a rule ID in the source has no such test, or a test names an ID the source does not list.
- **Rationale**: SC-003 is "100% of the rules". Counting by hand does not stay true across 125 specs.
- **Also**: A theory enumerates every project pair from the source and asserts that each edge outside the allow-list gets A5, so "every forbidden edge" is computed, not listed.

## R10. Pack closure check

- **Decision**: One architecture test (rule P3) runs `dotnet pack Iskender.sln --no-build -p:Version=0.0.0-closure-check --output <temp> -nodeReuse:false`, reads each `.nuspec` from the produced packages, and checks that every `Iskender.*` dependency was produced.
- **Spike evidence**: Pack with `--no-build` took about 1.3 seconds on the current three packable projects, and the nuspec listed `Iskender.Core.Contracts` with the expected version.
- **Rationale**: SC-007 is about the real pack output. FR-033 keeps this inside the existing architecture check rather than adding a CI step.
- **Details**:
  - The configuration comes from the test assembly's output path, so that Debug and Release runs both match their build.
  - `-nodeReuse:false` stops a lingering MSBuild node from holding the test's output pipes open.
  - The host path comes from `DOTNET_HOST_PATH`, falling back to `dotnet`.
  - If the build output is missing, the test fails and says to build the solution first. It does not skip.
- **Alternatives rejected**: Inferring closure from the graph alone, which P2 already does and which would not catch a packaging setting that drops a dependency.

## R11. Analyzer baseline (validates AV-4)

- **Decision**: `<AnalysisLevel>10.0-recommended</AnalysisLevel>` and `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` in `Directory.Build.props`.
- **Sources**: [MSBuild reference, code analysis properties](https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props#code-analysis-properties) gives the compound `<version>-<mode>` form. SDK 10.0.403 ships `analysislevel_10_recommended.globalconfig`, which confirms the value resolves.
- **Spike evidence**: A Release build with `-warnaserror` reported exactly one finding: `CA1861` at `tests/Iskender.Core.Contracts.Tests/SolutionBuildsTests.cs:32`. Moving the array to a local variable cleared it, with no suppression, and the build then reported 0 warnings and 0 errors. **AV-4 holds.**

## R12. Transitive pinning and overrides

- **Decision**: `<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>` and `<CentralPackageVersionOverrideEnabled>false</CentralPackageVersionOverrideEnabled>` in `Directory.Packages.props`.
- **Sources**: [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management#transitive-pinning) and [NU1013](https://learn.microsoft.com/nuget/reference/errors-and-warnings/nu1013).
- **Rationale**: Pinning applies to any transitive package that has a central version. Committed lock files, restored with `--locked-mode`, already freeze every other transitive package, and together they satisfy FR-020. Disabling overrides makes restore itself reject `VersionOverride`, so B1 is not the only guard.
- **Spike evidence**: With both settings on, a forced restore left all 9 lock files unchanged.
- **Caution for later specs**: NuGet promotes a transitively pinned package to an explicit dependency in the nuspec of a packable project. No packable project uses a pinned package yet, so nothing changes now. A later spec that pins a transitive package of a packable project should expect this.

## R13. Protected settings and suppressions

- **Decision**: The contract and the source hold explicit lists. A protected property or item may appear only in the root `Directory.Build.props` or `Directory.Packages.props`. It may not appear anywhere else, under any condition, in a `Target`, or in an imported file.
- **Rationale**: FR-022 and FR-023 name the categories. The lists also close routes the spec does not name, each of which has the same effect as a named one:
  - `ImportDirectoryBuildProps=false`, which silently removes every root setting at once.
  - `RunAnalyzers`, `RunAnalyzersDuringBuild`, `CodeAnalysisTreatWarningsAsErrors`, `WarningLevel`, `AnalysisMode*`, and `CodeAnalysisRuleSet`.
  - `<EditorConfigFiles>` and `<GlobalAnalyzerConfigFiles>` items, a nested `.editorconfig`, any `.globalconfig`, and a `Directory.Build.rsp` or `MSBuild.rsp`, each of which can change severities or properties from outside a project file.
  - `NoWarn` written as item metadata on a `PackageReference` or `ProjectReference`.

## R14. The existing `CA2007` suppression

- **Finding**: `.editorconfig` sets `dotnet_diagnostic.CA2007.severity = none` for all C# files, with the reason "This is tooling and test code, not a library with a synchronization context". FR-025 now makes `Iskender.Hosting`, `Iskender.Domain`, `Iskender.Infrastructure`, and `Iskender.Management` packable libraries, so that reason no longer holds for all of `src/`. FR-023 requires every suppression to be listed with a true reason.
- **Decision**: Move the suppression into a `[tests/**.cs]` section and list it in the contract. FR-023's existing-exception wording covers only `NU1900`, so this one has to be settled here.
- **Rationale**: `src/` has no `await` today, so this costs nothing now, and embeddable libraries keep the check when real code arrives. It only narrows a suppression, so it cannot weaken anything. A later spec can widen it through a contract revision if the check proves unhelpful in library code.
- **Alternative rejected**: Keeping it global with a reworded reason, which would decide for every later library spec that the check does not apply.

## R15. Denied packages

- **Decision**: The source holds four categories of package prefixes, and a table says which categories each role may not take. A prefix matches the exact ID or the ID followed by a dot.
  - `aspnetcore`: the `Microsoft.AspNetCore` prefix, `FrameworkReference Include="Microsoft.AspNetCore.App"`, and `Sdk="Microsoft.NET.Sdk.Web"`.
  - `cloud`, `provider`, and `driver`/`data-access`: see the contract for the lists.
- **Checks**: Both direct `PackageReference` items and each project's `packages.lock.json`, so that a denied package arriving transitively through an allowed one is also caught.
- **Executables**: Checked on direct references only. FR-009 lets a host reference adapters, so an adapter's driver or SDK legitimately appears in the host's lock file, and checking it there would make FR-009 impossible to use.
- **Provider adapters**: Denied only `driver`. A cloud-hosted provider's SDK can sit under a cloud prefix, such as `Azure.AI.OpenAI`, so denying `cloud` would block the adapter's own SDK.
- **Infrastructure adapters**: Denied only `provider`.
- **`Iskender.Hosting` and `Iskender.Management`**: Also denied `data-access`, which is slightly stricter than FR-018's list. `docs/03` says Management must not bypass domain validation with raw storage updates, and FR-011 says embedding must not impose storage. A later spec can loosen this through a contract revision.
- **Test projects**: Exempt from A19. FR-018's concern is what ships and what embedders inherit, and test projects ship in no package. Later Testcontainers specs will need drivers there. All B rules still apply to them.
- **Limit**: This is a deny-list, so a package outside the lists is not caught, and "its own" SDK within an allowed category is checked by review.

## R16. Files that must move with the new graph

Found by searching for the old contract path and host name outside `specs/0000-repo-bootstrap/`:

| File | Change |
|---|---|
| `README.md` lines 28 to 37 | New projects; link to the version 2 contract (FR-034) |
| `docs/03-domain-and-technical-architecture.md` | Link from Module boundaries to the map (FR-034) |
| `.github/CODEOWNERS` | The rule source, the test and fixtures projects, and the 5 uncovered or new `src/` projects: `Domain`, `Infrastructure`, `Management`, `Hosting`, `ControlPlane.Host` (FR-003) |
| `.github/scripts/docs-gate.sh` and `CONTRIBUTING.md` | New behavior-bearing paths (FR-028) |
| `.github/workflows/ci.yml` lines 117 and 122 | Attest and checksum the new executable (FR-028) |
| `.claude/agents/reviewers/architecture-guard.md` line 8 | Points at the version 1 contract, so it would review against superseded rules |

`.github/workflows/release.yml` needs no change: it attests every `.nupkg`, so the new packages are covered.

## R17. New package pins

- **Decision**: None. Everything above uses the base class library and the existing `NetArchTest.Rules` and `xunit.v3` pins. FR-024 is met by adding nothing.

## Decisions for later specs

- AV-1 and AV-2 stay open for ISK-0013, ISK-0030, and ISK-0052. `Iskender.Hosting` and `Iskender.Management` have no ASP.NET Core reference yet. ISK-0006 and ISK-0007 add it when they add behavior, and A19 permits it for those projects.
- AV-3 stays open for ISK-0011, ISK-0012, and ISK-0015. The module allow-list starts empty.
