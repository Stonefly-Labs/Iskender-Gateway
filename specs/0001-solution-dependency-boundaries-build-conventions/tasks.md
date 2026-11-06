---

description: "Task list for ISK-0001 — Solution Dependency Boundaries & Build Conventions"
---

# Tasks: ISK-0001 — Solution Dependency Boundaries & Build Conventions

**Input**: Design documents from `specs/0001-solution-dependency-boundaries-build-conventions/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md, contracts/module-dependency-rules.md

**Tests**: Required. FR-029 and SC-003 ask for a negative fixture for every rule, so each rule task delivers the rule and its fixtures together. Write the fixtures first and watch them fail, but commit only when the suite is green, because every commit must leave the build and tests passing (plan, "Implementation approach").

**Scope guard**: No gateway, provider, policy, or database behavior, and no new package pins (FR-024, FR-036). Nothing changes under `specs/0000-repo-bootstrap/`, `.specify/`, or in `.iskender/grounding.yaml` (SC-008).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task belongs to (US1 to US5)
- Exact file paths are included in each description

## Path Conventions

`ATests` below means `tests/Iskender.Architecture.Tests`. Rule IDs, tables, and values come from `contracts/module-dependency-rules.md`, which is the version 2 contract, and the entity fields come from `data-model.md`.

## Notes for every task

- **Restore on this PC**: Use the existing user-level proxy feed. Never commit a `nuget.config` or put that feed in CI.
- **Lock files**: After changing a reference, run `dotnet restore Iskender.sln --force-evaluate` and commit each changed `packages.lock.json`, since CI restores in `--locked-mode`.
- **Fixture literals**: A literal such as `#nullable disable` must sit inside a one-line string, because the source scan blanks those out before matching. Anywhere else, including a multi-line raw string, the scan flags the test file itself (research R8).
- **A failure you cannot fix**: Report it. Never add a suppression or weaken a rule to get past it (spec AV-4, FR-023).

---

## Phase 1: Setup

**Purpose**: Confirm the starting point, so that any later failure can be attributed to this work.

- [X] T001 Run `dotnet restore Iskender.sln --locked-mode`, `dotnet format Iskender.sln --verify-no-changes --no-restore`, `dotnet build Iskender.sln --configuration Release --no-restore -warnaserror`, `dotnet test --solution Iskender.sln --configuration Release --no-build`, `bash .github/scripts/check-grounding.sh`, and `bash .github/scripts/check-license.sh` from the repository root, and confirm that all of them pass before any file changes. Report any failure rather than working around it.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build the rule engine against the graph as it stands, then refine the graph (plan steps 1 and 2). Graph refinement is here rather than in US3 because M2 in US1 needs `Iskender.Hosting` to exist, and the fixtures in US2 need the final allow-list.

**CRITICAL**: No user story work can begin until this phase is complete.

### Rule engine on the existing graph

- [X] T002 [P] Create `ATests/Violation.cs` with the fields `ruleId`, `source`, `target`, `detail`, `contractPath`, and `contractVersion`, and the exact message format `<rule ID>: <source> -> <target or setting>: <what is wrong>. See <contract path> (version <n>).` Identity is "`ruleId + source + target`", and "duplicates collapse". Sort by rule order in the source, then source, then target, using ordinal comparison, so that Windows and Linux report identically (FR-031). Create `ATests/Support/ProvesRuleAttribute.cs`, which is applied as `[ProvesRule("A5")]` and may be repeated on a test. Cover the format, collapsing, and ordering in `ATests/ViolationTests.cs`.
- [X] T003 [P] Create `ATests/module-dependency-rules.json` with the shape in `data-model.md`. Copy the final values from the contract for `contract`, `moduleTable`, `modules` (12), `portHomes` (10), `testProjects`, `moduleNamespaces` (`shared` of `Iskender.Domain.Ports`, empty `allowList`), `forbiddenReach` (A2, A3, A7 to A11, from its Forbidden reach table), `deniedPackages` (each category with its prefixes, framework references, and SDKs), `approvedSdks`, `protectedSettings`, `suppressions` (`NU1900` only), and `rules` (all 34, each with `retired: false`). `projects` and `requiredReferences` describe the graph as it stands at this point, with the two pattern entries, and the A6 `forbiddenReach` entry is left out because `Iskender.Gateway.Host` still references `Iskender.Management`. This lets T008 pass on the real repository. T009 to T011 move these to their final values.
- [X] T004 Create `ATests/RuleSource.cs`, which loads and validates the source with `System.Text.Json`. A failure is a C2 violation that names the field, and "the run does not continue on partial data". Enforce every check under Validation in `data-model.md`: the file exists and parses, no required field is empty except `allowList` and a category's `frameworkReferences` and `sdks`, "`contract.version` is at least 2", `contract.path` exists under `speckit.specificationsRoot`, `rules[].id` is unique, a role is one of `public contract`, `embeddable`, `executable`, or `test`, every `denied` category exists in `deniedPackages`, every `forbiddenReach.rule` exists in `rules`, and `*` appears only in a `forbiddenReach.from`. Resolve `moduleTable.file` under `grounding.root` and `contract.path` under `speckit.specificationsRoot` by reading `.iskender/grounding.yaml` line by line with no YAML package, tracking the section each key sits in, so neither `docs/` nor `specs/` is hard-coded. Add `ATests/RuleSourceTests.cs` with `[ProvesRule("C2")]` tests for a missing file, malformed JSON, each validation failure, and an unresolved `grounding.root` or `speckit.specificationsRoot` (FR-032).
- [X] T005 Create `ATests/RepositoryScan.cs`, which finds every `*.*proj` file in the repository, skipping only `bin/`, `obj/`, `artifacts/`, `TestResults/`, `.git/`, and `node_modules/` (research R5), and reads each as XML without MSBuild (research R4). Populate every Project field in `data-model.md`: `name`, `path` (repository-relative with forward slashes), `isUnderTests` ("True only for paths under `tests/`"), `references`, `packageReferences`, `assemblyReferences`, `frameworkReferences`, `sdk`, `properties`, `items`, and `lockFilePackages` (excluding entries of type `Project`). Read elements under every condition and inside targets. Follow `<Import>` elements, resolving only `$(MSBuildThisFileDirectory)` and `$(MSBuildProjectDirectory)`, and read the root `Directory.Build.props`, `Directory.Packages.props`, and `Directory.Build.targets` as imports of every project, so that a reference, package, or `Import` in one of them counts against every project under any condition (FR-013). Count a `GlobalPackageReference` in `packageReferences`. Do not count a required setting in the root `Directory.Build.props` or `Directory.Packages.props` as a project setting, since B3 checks it there, but do count a forbidden setting or item there, for B4. Read an item element whatever its operation (`Include`, `Remove`, or `Update`). Keep every scanned file as its own entry, even when two share a name, so that A14 can report them. Read the project paths from `Iskender.sln` as text, into the solution projects in `data-model.md`. Resolve references by file name, ignoring case and slash direction. A file that cannot be parsed, or a missing or unreadable solution file, produces a C2 violation naming it, and the scan continues; finding no projects is a C2 violation (FR-032). Cover this in `ATests/RepositoryScanTests.cs` with `[ProvesRule("C2")]` on the failure cases, including a reference written with backslashes and different casing, and with passing tests that a reference and a package in each root file reach every scanned project.
- [X] T006 [P] Create `ATests/Support/RepositoryFixture.cs`, which copies to a temp directory every real input that a rule reads, applies one mutation, returns the root for scanning, and deletes the directory afterward (research R8). The copy holds `Iskender.sln`, every project and lock file, the `*.cs` files under `src/` and `tests/` without `bin/` or `obj/`, the root build files, `global.json`, `.editorconfig`, the rule source, `.iskender/grounding.yaml`, the `docs/03` table's file, the contract, `.github/CODEOWNERS`, `.github/workflows/ci.yml`, `.github/scripts/docs-gate.sh`, and `CONTRIBUTING.md`. Create `ATests/Support/MarkdownTable.cs`, which reads every table under a named heading, in order, and returns each one's rows, and which M1 and C1 share, since the contract's Denied packages section holds two tables. Cover both in `ATests/SupportTests.cs`, including a missing heading, a heading with no table, and a heading with two tables.
- [X] T007 Rewrite `ATests/ProjectGraph.cs` to build from `RepositoryScan` projects and `RuleSource` definitions, and delete its `ApprovedReferences` constant, since "the test code holds no copy" of the source's contents. Implement name resolution as in `data-model.md`: "an exact entry wins, and only then is a pattern tried", and "a pattern's segment contains no dot", so `Iskender.Providers.Abstractions` never matches `Iskender.Providers.<Provider>` and `Iskender.Providers.Foo.Bar` matches nothing. Implement the three graph operations: direct check, a reach walk that tolerates cycles, and cycle detection over all nodes. A reference to a project that is not in the scan stays visible to A13 rather than being dropped. The test-project exemption depends only on `isUnderTests`, replacing the `EndsWith(".Tests")` check (FR-014).
- [X] T008 Create `ATests/Rules/GraphRules.cs`, which evaluates A1 to A12 from the source, and `ATests/Rules/RuleRunner.cs`, which runs every rule family and returns all violations in one sorted list (FR-031). Later rule tasks register their family here. In `ATests/DependencyDirectionTests.cs`, replace `RepositoryProjectGraphHasNoViolations` with a test that asserts `RuleRunner` reports nothing on the real repository, and adapt the rest to the new graph and message format without weakening them: the `Graph` helper must state each project's location, since `A5AllowsTestProjectsToReferenceWhatTheyTest`, `A2AppliesToTestProjects`, and `A5UnknownProductionProjectFails` can no longer rely on the project's name, and `A5UnapprovedEdgeFails` asserts the new exact message. Keep `LoadParsesWindowsStyleReferencePaths` against the new scan. `HostComposesEverySkeletonProject` and the `Management → Domain` case still hold on the old graph, and T011 changes them.

### Refined graph

- [X] T009 Create `src/Iskender.Hosting/Iskender.Hosting.csproj` as an empty `Microsoft.NET.Sdk` library, with no source files, referencing `Iskender.Domain` and `Iskender.Core.Contracts` (FR-008, TR-1). Add it to `Iskender.sln`, commit its lock file, and add it to `projects` in `ATests/module-dependency-rules.json` with role `embeddable` and denied categories `cloud`, `provider`, `driver`, and `data-access`.
- [X] T010 Create `src/Iskender.ControlPlane.Host/Iskender.ControlPlane.Host.csproj` (`Microsoft.NET.Sdk.Web`) and `src/Iskender.ControlPlane.Host/Program.cs`, which maps no endpoints and registers no gateway services, referencing `Iskender.Management`, `Iskender.Domain`, `Iskender.Infrastructure`, and `Iskender.Core.Contracts` (FR-008, TR-2). Add it to `Iskender.sln`, commit its lock file, and add it to `projects` in `ATests/module-dependency-rules.json` with role `executable`. In the same commit, change `SkeletonContainsNoGatewayBusinessCode` in `tests/Iskender.Core.Contracts.Tests/SolutionBuildsTests.cs` to list exactly both `Program.cs` files and assert that neither maps an endpoint (FR-030). Sort the enumerated paths with ordinal comparison before the `Assert.Equal`, because file system order differs between Windows and Linux. Keep the word "Map" out of the new `Program.cs` comment, because the test searches for that text.
- [X] T011 Refine the edges in one commit, with their tests (FR-009, FR-012, FR-030). Add `Iskender.Providers.Abstractions` and `Iskender.Policy.Sdk` to `src/Iskender.Domain/Iskender.Domain.csproj`, and `Iskender.Domain` to `src/Iskender.Management/Iskender.Management.csproj`. In `src/Iskender.Gateway.Host/Iskender.Gateway.Host.csproj`, add `Iskender.Hosting` and remove `Iskender.Management`. Update the comment in `src/Iskender.Gateway.Host/Program.cs` to call it the data-plane host, keeping the word "Map" out of it because the test searches for that text. Set `projects` and `requiredReferences` in `ATests/module-dependency-rules.json` to their final values, and add the A6 `forbiddenReach` entry. In `ATests/DependencyDirectionTests.cs`, replace `HostComposesEverySkeletonProject` with a test that asserts A12 for both hosts and A6 and A7 on the real graph, change the `Management → Domain` case of `A5UnapprovedEdgeFails` to `Management → Infrastructure`, add both new projects to `RepositoryProjectGraphContainsEverySkeletonProject`, and add `Iskender.Hosting` to `SkeletonAssemblies`. Extend the type-level tests' forbidden lists with `Iskender.Hosting` and `Iskender.ControlPlane`. Add `Iskender.Hosting` to `ATests/Iskender.Architecture.Tests.csproj`. Regenerate lock files.
- [X] T012 Update `README.md`: add `Iskender.Hosting` and `Iskender.ControlPlane.Host` to the project table, describe `Iskender.Gateway.Host` as the data-plane host, and point the "approved dependency direction" link at `specs/0001-solution-dependency-boundaries-build-conventions/contracts/module-dependency-rules.md` (FR-034). This comes straight after T011 because `src/Iskender.Gateway.Host/**` is a behavior-bearing path, and the README would otherwise describe a graph that no longer exists.

**Checkpoint**: The real repository passes on the refined graph, with A1 to A12 running from the rule source.

---

## Phase 3: User Story 1 - Every module has one known home (Priority: P1) 🎯 MVP

**Goal**: One table says where each `docs/03` module lives, and the tests keep it in step with `docs/03`.

**Independent Test**: Delete a map entry, or add a row to a copy of the `docs/03` table, and confirm the tests fail and name the module.

- [X] T013 [US1] Create `ATests/Rules/ModuleMapRules.cs`, registered in `RuleRunner`, with M1 and M2 (FR-005 to FR-007). M1 reads the table under `moduleTable.heading` and compares its first column with `modules[].module`, using "ordinal, case-sensitive matching", and reports each missing, extra, or duplicate entry by name. A table that cannot be found or yields no modules is a C2 violation. M2 checks that each map and port entry names an existing project, an approved pattern, or a namespace under `Iskender.Domain` that `modules` or `moduleNamespaces.shared` defines, that `namespaceRoot` is unique across the map, and that `Iskender.Domain` modules have a child namespace of it. Add `ATests/ModuleMapRuleTests.cs` with on-disk fixtures from `RepositoryFixture`: a `[ProvesRule("M1")]` test each for a missing entry, an added `docs/03` row, a duplicate, and an entry for a module `docs/03` does not list; `[ProvesRule("M2")]` tests for a map entry and a port entry naming a project that does not exist, and for a port entry naming an unmapped `Iskender.Domain` namespace; `[ProvesRule("C2")]` tests for a missing heading and a table with no rows; a test that the real repository resolves 12 of 12 modules (SC-002); and a test that the real port entry homed in `Iskender.Domain.Ports` passes.
- [X] T014 [P] [US1] In `docs/03-domain-and-technical-architecture.md`, add one sentence under "Module boundaries", above the table, linking to the Module map section of the version 2 contract (FR-034). Change nothing else, and keep the table parseable by M1.

**Checkpoint**: US1 works alone: each module has one home, and the tests name any drift.

---

## Phase 4: User Story 2 - Forbidden references are rejected (Priority: P1)

**Goal**: A reference that crosses a boundary fails the test run with a message saying which rule broke, however it was disguised.

**Independent Test**: Apply each forbidden edge to a fixture and confirm that a test fails with the expected rule ID.

- [ ] T015 [US2] Create `ATests/GraphRuleTests.cs` with `[ProvesRule]` tests for A1 to A5, on synthetic graphs and on one on-disk fixture each. Include a theory that enumerates every project pair from the source and asserts that each edge outside the allow-list gets A5, so that "every forbidden edge" is computed rather than listed (research R9). Cover the first three negative security scenarios in the spec: a provider adapter referencing `Iskender.Domain`, `Iskender.Policy.Sdk` referencing `Iskender.Domain` and `Iskender.Infrastructure`, and `Iskender.Management` referencing `Iskender.Infrastructure`. Also cover `Iskender.Providers.Foo.Bar` failing A5, and a run with several violations reporting all of them in stable order in one list (FR-031). Assert the whole message for at least one violation of each rule, including rule ID, source, target, contract path, and version (SC-004).
- [ ] T016 [US2] Create `ATests/Rules/BoundaryRules.cs`, registered in `RuleRunner`, with A13 (FR-013): every project reference and import resolves to a file in the repository, every SDK is approved, whether on the `Project` element, an `Sdk` element, or an `Import`, and no project takes a direct assembly reference to, or a package named like, an `Iskender.*` project. An `Include` or `Import` containing an unresolvable MSBuild property is a violation rather than a skipped entry. Every project listed in `Iskender.sln` must be a scanned project, so a project under a skipped directory cannot be built unchecked. Add `ATests/BoundaryRuleTests.cs` with on-disk `[ProvesRule("A13")]` fixtures for a reference under a never-true condition (which must also report A5), a reference in an imported file, a `ProjectReference` conditioned on the project name in the root `Directory.Build.props` (which must also report A5 for each project that is not allowed that edge), a dangling `Import` in the root `Directory.Packages.props`, a `<Reference>` to an `Iskender.*` assembly, a `PackageReference` named `Iskender.Domain`, an unresolved property, a dangling reference, an unapproved SDK on each element, and a solution entry for a project under `artifacts/`.
- [ ] T017 [US2] Add A14, A15, and A16 to `ATests/Rules/BoundaryRules.cs` (FR-013 to FR-016). A14: every project is under `src/` or `tests/`, no project under `src/` is named `*.Tests`, and no two project files share a name, ignoring case. A15: a production project's assembly name, root namespace, and package ID equal its project name, treating the root `RootNamespace` of `$(MSBuildProjectName)` as compliant. A16: a production assembly grants `InternalsVisibleTo` only to a test project, checked both as a project item and as an `[assembly: InternalsVisibleTo]` attribute in source, finding the attribute with the contract's text matching and reading the granted name from the original line. Add on-disk `[ProvesRule]` fixtures to `ATests/BoundaryRuleTests.cs` for a project outside both directories, `*.Tests` under `src/` (which must also fail A5), a second project with an approved project's file name under `tests/`, a second one differing only in case, each of the three identity settings, and a grant to a production assembly by each route, plus a passing case for a grant to a test project.
- [ ] T018 [US2] Create `tests/Iskender.Architecture.Fixtures/Iskender.Architecture.Fixtures.csproj`, a test-role project that "references no production project and is not packable", with `tests/Iskender.Architecture.Fixtures/ModuleViolations.cs` declaring public types with real violations: `Iskender.Domain.Policy` using a type from `Iskender.Domain.Routing`; `Iskender.Domain.Ports` using a module type; one type directly in `Iskender.Domain`; one in the unmapped `Iskender.Domain.Unmapped`; one in `Iskender.Domain.PolicyExtras`, the prefix trap from research R7; and a clean module type using only `Iskender.Domain.Ports`. Add it to `Iskender.sln`, reference it from `ATests/Iskender.Architecture.Tests.csproj`, and commit the lock files.
- [ ] T019 [US2] Create `ATests/Rules/ModuleRules.cs`, registered in `RuleRunner`, with A17 and A18 (FR-017), taking the target assembly as a parameter. Use NetArchTest's `HaveDependencyOn` for the dependency check, and filter `ResideInNamespace` results to an exact namespace or its children in test code, since it matches by prefix (research R7). Honor `moduleNamespaces.allowList`. Add `ATests/ModuleRuleTests.cs` with `[ProvesRule("A17")]` and `[ProvesRule("A18")]` tests that run on the fixtures assembly and assert the exact set of violations by type and namespace, including that the `PolicyExtras` type is reported as unmapped and not as Policy, and that the clean type is not reported. Also assert that the real `Iskender.Domain` is clean, without counting that as proof, because the assembly is empty (spec edge case).

**Checkpoint**: US1 and US2 give the full direct-reference and module-namespace enforcement.

---

## Phase 5: User Story 3 - Refined edges let the planes deploy apart (Priority: P2)

**Goal**: The graph shows that each plane builds without the other's surface, and that the Domain and Management edges match `docs/03`.

**Independent Test**: Compute each host's transitive references and compare them with the contract.

- [ ] T020 [US3] Add `[ProvesRule]` tests for A6 to A11 to `ATests/GraphRuleTests.cs`, each on a synthetic graph where the forbidden target is reached through an intermediate project, so that a direct-only check would pass (FR-011). A6: `Iskender.Gateway.Host` reaching `Iskender.Management` and `Iskender.ControlPlane.Host`. A7: `Iskender.ControlPlane.Host` reaching `Iskender.Hosting` and `Iskender.Gateway.Host`. A8: `Iskender.Hosting` reaching `Iskender.Infrastructure`, an adapter, and `Iskender.Management`. A9: `Iskender.Domain`, `Iskender.Policy.Sdk`, `Iskender.Providers.Abstractions`, and a provider adapter reaching `Iskender.Management`, `Iskender.Hosting`, and a host. A10: `Iskender.Domain` and `Iskender.Hosting` reaching a provider adapter. A11: `Iskender.Infrastructure` reaching an infrastructure adapter. Add an A3 transitive case that goes through an adapter, and an A4 cycle through each new project.
- [ ] T021 [US3] Add `[ProvesRule("A12")]` tests to `ATests/GraphRuleTests.cs` for each of the four required references going missing. Add tests on the real graph asserting that the data-plane host's transitive references include 0 control-plane projects and the control-plane host's include 0 ingress projects (SC-005), and an A2 case for a test project reaching `Iskender.ControlPlane.Host`.

**Checkpoint**: Plane separation is proved on the real graph and on fixtures.

---

## Phase 6: User Story 4 - Build conventions cannot be weakened quietly (Priority: P2)

**Goal**: A project that opts out of the pinned versions, analyzers, or nullability settings fails the tests.

**Independent Test**: Apply each protected-setting violation to a fixture and confirm the expected rule ID.

- [ ] T022 [US4] Raise the baseline before the rules that require it (FR-020, FR-021). Add `<AnalysisLevel>10.0-recommended</AnalysisLevel>` and `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` to `Directory.Build.props`. Add `<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>` and `<CentralPackageVersionOverrideEnabled>false</CentralPackageVersionOverrideEnabled>` to `Directory.Packages.props`, with no new `PackageVersion`. Fix `CA1861` at `tests/Iskender.Core.Contracts.Tests/SolutionBuildsTests.cs:32` by moving the array to a local variable, with no suppression, and fix any finding in code from earlier tasks the same way, since the spike ran before that code existed. Delete the `CA2007` line and its comment from `.editorconfig` (research R14). Confirm that a forced restore leaves the existing lock files unchanged, as the spike found, and that a Release build with `-warnaserror` reports 0 warnings.
- [ ] T023 [US4] Create `ATests/Rules/BuildConventionRules.cs`, registered in `RuleRunner`, with B1 and B2 (FR-019, FR-020). B1: no `Version` or `VersionOverride` on a `PackageReference`, including one in a root build file, and no project setting of `ManagePackageVersionsCentrally`, under any condition. A `GlobalPackageReference` in the root `Directory.Packages.props` may state a `Version`, since that is where central management keeps it. B2: every project has a committed `packages.lock.json`. Add `ATests/BuildConventionRuleTests.cs` with on-disk `[ProvesRule]` fixtures for a version in a project, a `PackageReference` with a version in the root `Directory.Build.props`, a `VersionOverride`, central management disabled in a project, and a missing lock file, plus a passing case for a `GlobalPackageReference` with a version. Every message names the project and package or setting (US4 scenario 1).
- [ ] T024 [US4] Add B3 and B4 to `ATests/Rules/BuildConventionRules.cs` (FR-019 to FR-022). B3: the root build files set every required value in `protectedSettings.required` exactly once and with no condition, including central management, transitive pinning, and disabled overrides in `Directory.Packages.props`. B4: no required or forbidden setting or item from `protectedSettings` appears in a project or imported file, under any condition or in a target, and no forbidden one appears in a root build file. A `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, or `.editorconfig` below the root is a violation, and so is a `Directory.Build.rsp`, `MSBuild.rsp`, or `.globalconfig` anywhere, including the root. A root `Directory.Build.targets` is allowed. Add `[ProvesRule]` fixtures to `ATests/BuildConventionRuleTests.cs`: a B3 theory over every entry in `protectedSettings.required`, removing it and changing it in its root file, and a B4 theory over every entry in `protectedSettings`, setting it in a project, so that a new setting is covered automatically (SC-006). The B4 theory includes `<Nullable>disable</Nullable>` under a never-true condition and inside a target. Add a B3 fixture for a required setting repeated under a condition in its root file, B4 fixtures for a forbidden setting in each root build file, one using `CustomAfterMicrosoftCommonTargets` to name a file, and `<Analyzer Remove="@(Analyzer)" />` inside a target. Add one fixture for each file named above, and a passing case for a root `Directory.Build.targets`.
- [ ] T025 [US4] Add B5 and B6 to `ATests/Rules/BuildConventionRules.cs` (FR-022, FR-023). Scan `*.cs` under `src/` and `tests/`, skipping `bin/` and `obj/`, with the text matching in the contract's Listed suppressions section: blank out one-line string literals and `//` comments, keeping column positions, then match, and read a suppressed ID from the original line. B5: no `#nullable disable` directive, including `# nullable disable`, indented forms, and a trailing `warnings` or `annotations`, and no tracked file treated as generated, by the name patterns in the contract or by `<auto-generated` or `<autogenerated` in a comment before its first code, read from the original text. B6: every suppression is listed in `suppressions`, looking for `NoWarn` and `WarningsNotAsErrors` as properties or as item metadata on a `PackageReference` or `ProjectReference`, `#pragma warning disable`, `[SuppressMessage]`, `[UnconditionalSuppressMessage]`, `[GeneratedCode]`, `generated_code = true` in `.editorconfig`, and `dotnet_diagnostic` or `dotnet_analyzer_diagnostic` severities of `none`, `silent`, `suggestion`, or `default` in `.editorconfig`. An attribute matches inside an attribute list, with an `assembly:` or `module:` target, an `Attribute` suffix, or a namespace qualifier. `NU1900` in `Directory.Build.props` stays allowed, and an unlisted ID there fails. Add `[ProvesRule]` fixtures for each form to `ATests/BuildConventionRuleTests.cs`, naming the file (US4 scenario 3), plus a passing case for the same text inside a one-line string and a `//` comment. Test code must follow the fixture-literal note above.
- [ ] T026 [US4] Add A19 to `ATests/Rules/BoundaryRules.cs` (FR-018, research R15). A prefix "matches the exact package ID or the ID followed by a dot". Check each project's direct `PackageReference` items against each denied category's `prefixes`, its `FrameworkReference` items against `frameworkReferences`, and its SDK against `sdks`, all from the rule source, and also check `lockFilePackages`, except that executables are checked on direct references only and test projects are exempt. Add `[ProvesRule("A19")]` fixtures to `ATests/BoundaryRuleTests.cs`: `Iskender.Domain` taking a cloud SDK, a driver, a data-access library, the ASP.NET Core framework reference, and the `Microsoft.NET.Sdk.Web` SDK; a denied package arriving only through a lock file; a denied `GlobalPackageReference` in the root `Directory.Packages.props`, which must be reported for a host as well; `Iskender.Infrastructure` taking a driver; `Iskender.Management` taking a data-access library; a provider adapter taking a data-access library; a prefix look-alike such as `Azureish.Tools` that must not match; and passing cases for a provider adapter taking `Azure.AI.OpenAI`, an infrastructure adapter taking `Npgsql`, and a host whose lock file holds an adapter's package.

**Checkpoint**: Every protected setting has a fixture that fails when it is weakened (SC-006).

---

## Phase 7: User Story 5 - Embeddable packages are complete (Priority: P3)

**Goal**: A consumer gets packages whose dependencies were all packed, and the project roles are explicit.

**Independent Test**: Pack the solution and confirm that each package's `Iskender.*` dependencies were also produced.

- [ ] T027 [US5] Set `<IsPackable>true</IsPackable>` and a `<Description>` in `src/Iskender.Domain/Iskender.Domain.csproj`, `src/Iskender.Infrastructure/Iskender.Infrastructure.csproj`, `src/Iskender.Management/Iskender.Management.csproj`, and `src/Iskender.Hosting/Iskender.Hosting.csproj` (FR-025, TR-6). Create `ATests/Rules/PackagingRules.cs`, registered in `RuleRunner`, with P1 and P2 (FR-025, FR-026). P1: every project has a role, and a project is packable exactly when its role says so, where packable "means the project file sets `IsPackable` to `true` without a condition". P2: every project reference from a packable project targets a packable project. Add `ATests/PackagingRuleTests.cs` with on-disk `[ProvesRule]` fixtures for a host marked packable, a test project marked packable, an embeddable project not marked packable, a conditional `IsPackable`, and a packable project referencing a non-packable one, naming both (US5 scenario 1).
- [ ] T028 [US5] Add P3 to `ATests/Rules/PackagingRules.cs` (SC-007, research R10), split into two parts. The first runs `dotnet pack Iskender.sln --no-build -p:Version=0.0.0-closure-check --output <temp> -nodeReuse:false`, using the configuration from the test assembly's output path and the host path from `DOTNET_HOST_PATH`, falling back to `dotnet`. The second reads each `.nuspec` with `System.IO.Compression` and checks that every `Iskender.*` dependency was produced. Missing build output fails with a message to build the solution first and does not skip. In `ATests/PackagingRuleTests.cs`, add a `[ProvesRule("P3")]` test of the second part on a synthetic `.nupkg` with a dangling dependency, and a test that the real pack produces the 7 packages in quickstart scenario 7 with neither host and no test project.

**Checkpoint**: All five user stories work on their own.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Protect the rule source, wire up CI and the gates, and turn on the checks that compare finished work (plan steps 5 and 6).

- [ ] T029 Update `.github/CODEOWNERS` to cover `/tests/Iskender.Architecture.Tests/`, `/tests/Iskender.Architecture.Fixtures/`, `/.editorconfig`, `/Directory.Build.targets`, and the 5 uncovered or new projects `src/Iskender.Domain`, `src/Iskender.Infrastructure`, `src/Iskender.Management`, `src/Iskender.Hosting`, and `src/Iskender.ControlPlane.Host` (FR-003). Create `ATests/Rules/ContractDocumentRules.cs`, registered in `RuleRunner`, with C3: `.github/CODEOWNERS` covers the rule source, `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `Directory.Build.targets`, and every project under `src/`. Add `ATests/ContractDocumentRuleTests.cs` with `[ProvesRule("C3")]` fixtures for an uncovered source file, `.editorconfig`, each root build file, and project.
- [ ] T030 Add `src/Iskender.Hosting/*` and `src/Iskender.ControlPlane.Host/*` to `is_behavior_bearing` in `.github/scripts/docs-gate.sh`, and list both in the documentation merge gate in `CONTRIBUTING.md`. In `.github/workflows/ci.yml`, add `src/Iskender.ControlPlane.Host/bin/${{ env.CONFIGURATION }}/net10.0/Iskender.ControlPlane.Host.dll` to the attestation `subject-path` and the same path, in shell form, to the fork-PR `sha256sum` step (FR-028). Add C4 to `ATests/Rules/ContractDocumentRules.cs` as the contract words it, with `[ProvesRule("C4")]` fixtures in `ATests/ContractDocumentRuleTests.cs` for an executable missing from each of the two CI steps and a behavior-bearing path missing from each of the two files, including `Iskender.Hosting`, which is embeddable and is named in the rule. Run quickstart scenario 10.
- [ ] T031 [P] Point `.claude/agents/reviewers/architecture-guard.md` line 8 at `specs/0001-solution-dependency-boundaries-build-conventions/contracts/module-dependency-rules.md`, so that it does not review against superseded rules.
- [ ] T032 Add C1 to `ATests/Rules/ContractDocumentRules.cs` (FR-002, research R3). Parse the contract at the resolved `contract.path` with `MarkdownTable`, and compare with the source in both directions: version, module map, port homes, project roles including the `Any project under tests/` row against `testProjects`, approved direct references, required references, forbidden reach, module namespaces, rules, denied categories with their prefixes, framework references, and SDKs, per-project denials, approved SDKs, required settings, the forbidden settings and items listed in prose, and listed suppressions. Reconcile any drift by fixing whichever side is wrong against the spec, never by loosening the comparison. Add `[ProvesRule("C1")]` fixtures to `ATests/ContractDocumentRuleTests.cs` for a mutated row in each table, in each direction, and for a mismatched version.
- [ ] T033 Create `ATests/RuleCoverageTests.cs`, a meta-test that reflects over the test assembly and fails if a rule ID in the source that is not retired has no `[ProvesRule]` test, or a test names an ID the source does not list (FR-029, SC-003, research R9). Prove it with a synthetic rule list and test list in both directions. Add any fixture it finds missing.
- [ ] T034 Run quickstart scenario 9 and confirm the scope limits (SC-008): no diff under `specs/0000-repo-bootstrap`, `.iskender/grounding.yaml`, or tracked `.specify` files, no new `PackageVersion` in `Directory.Packages.props` (FR-024), `src/Iskender.ControlPlane.Host/Program.cs` as the only new production source file, and no public type added under `src/` beyond that shell (FR-027). Confirm no fixture type or `tests/` output appears in a package (FR-029), and that `contracts/module-dependency-rules.md` carries the FR-035 table and states that it supersedes version 1 (FR-001).
- [ ] T035 Run all of `specs/0001-solution-dependency-boundaries-build-conventions/quickstart.md` from a clean tree, including scenario 1 from a fresh clone (SC-001) and the scratch-branch scenarios 3, 5, and 6, discarding each change afterward. For SC-009, ask the maintainer to run scenario 2 with the network disconnected and report the result, and wait for the answer, because an agent on this PC would lose its own connection. Record who ran it and the observed result, and do not count a run with the network connected as this evidence. Confirm that a violation message identifies the rule and project, and through the rule ID the contract section, without opening test code (SC-004), and that the rules run under existing CI check 5 with no new check (FR-033). Report real results in the PR notes, and report any failure rather than changing a rule to pass it.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup, and blocks all user stories. T002, T003, and T006 come first, then T004, T005, T007, T008, and T009 to T012 in order. Each task must leave the repository green.
- **User Stories (Phases 3 to 7)**: All depend on Foundational.
- **Polish (Phase 8)**: Depends on all user stories. T032 and T033 compare finished work, so they run after every rule exists, and T034 and T035 run last.

### User Story Dependencies

- **US1 (P1)**: Independent after Foundational.
- **US2 (P1)**: Independent after Foundational. T016 precedes T017, and T018 precedes T019.
- **US3 (P2)**: Independent after Foundational, and shares `ATests/GraphRuleTests.cs` with T015, so T015 precedes T020.
- **US4 (P2)**: T022 precedes T023 to T025, since those rules require the raised baseline. T026 extends `ATests/Rules/BoundaryRules.cs`, so T016 precedes it.
- **US5 (P3)**: Independent after Foundational. T027 precedes T028, since the pack test expects 7 packages.

### Within Each Task

- Write the negative fixtures first and watch them fail, then make the rule pass, and commit only when the suite is green.
- Each family task registers itself in `ATests/Rules/RuleRunner.cs`, so the real repository is checked by that rule from then on.

### Parallel Opportunities

Most rule tasks touch `RuleRunner.cs`, or share a rule file with a neighbor, so few are marked [P]. The genuinely independent ones are T002, T003, and T006 in Foundational, T014, and T031.

---

## Parallel Example: Foundational

```text
Task: "Create ATests/Violation.cs and ATests/Support/ProvesRuleAttribute.cs" (T002)
Task: "Create ATests/module-dependency-rules.json" (T003)
Task: "Create ATests/Support/RepositoryFixture.cs and ATests/Support/MarkdownTable.cs" (T006)
```

---

## Implementation Strategy

### MVP First (US1 and US2)

1. Complete Phases 1 and 2, which also deliver the refined graph.
2. Complete US1 and US2, which are the spec's named acceptance evidence: every `docs/03` module maps to a home, and forbidden references are rejected.
3. Validate with quickstart scenarios 2, 3, and 5.

### Incremental Delivery

US3, US4, and US5 each add one independently testable guarantee. All five are in scope for this spec, and its single PR merges only after Phase 8. Stopping at the MVP would not satisfy the spec.

---

## Traceability

### Rule to task

| Rule | Implemented | Negative fixture |
|---|---|---|
| A1 to A5 | T008 | T015 |
| A6 to A11 | T008 | T020 |
| A12 | T008 | T021 |
| A13 | T016 | T016 |
| A14 to A16 | T017 | T017 |
| A17, A18 | T019 | T018, T019 |
| A19 | T026 | T026 |
| B1, B2 | T023 | T023 |
| B3, B4 | T024 | T024 |
| B5, B6 | T025 | T025 |
| P1, P2 | T027 | T027 |
| P3 | T028 | T028 |
| M1, M2 | T013 | T013 |
| C1 | T032 | T032 |
| C2 | T004, T005, T013 | T004, T005, T013 |
| C3 | T029 | T029 |
| C4 | T030 | T030 |

### Requirement to task

| Requirements | Tasks |
|---|---|
| FR-001 | T034; contract published in the plan stage |
| FR-002 | T003, T004, T032 |
| FR-003 | T029 |
| FR-004 | T032; process stated in the contract |
| FR-005 to FR-007 | T003, T013, T014 |
| FR-008, FR-009, FR-012 | T009, T010, T011, T021 |
| FR-010, FR-011 | T008, T015, T020 |
| FR-013 to FR-016 | T005, T007, T016, T017 |
| FR-017 | T018, T019 |
| FR-018 | T026 |
| FR-019 to FR-023 | T022, T023, T024, T025 |
| FR-024, FR-027 | T034 |
| FR-025, FR-026 | T027 |
| FR-028 | T030 |
| FR-029 | T006, T018, T033 |
| FR-030 | T008, T010, T011 |
| FR-031, FR-032 | T002, T004, T005, T008, T013, T015 |
| FR-033 | T035 |
| FR-034 | T012, T014 |
| FR-035 | T034; table published in the plan stage |
| FR-036 | T010, T034 |
| SC-001, SC-004, SC-009 | T035 |
| SC-002 | T013 |
| SC-003 | T033 |
| SC-005 | T021 |
| SC-006 | T024 |
| SC-007 | T028 |
| SC-008 | T034 |
