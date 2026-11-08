# Contract: Module Dependency Rules and Build Conventions

**Version**: 2

**Supersedes**: version 1, `specs/0000-repo-bootstrap/contracts/module-dependency-rules.md`, which stays unedited as a completed record.

**Rule source**: `tests/Iskender.Architecture.Tests/module-dependency-rules.json`. The tables below must match it, and rule C1 fails the run if they do not.

**Enforced by**: `tests/Iskender.Architecture.Tests`, under CI check "5. Architecture and dependency rules".

**Review owner**: Architecture (ADR) owner. See `CONTRIBUTING.md` for who holds the role.

## Changing this contract

A change to an approved edge, module home, role, or rule is made by a spec's contract revision. The revision increments the version, publishes this contract in that spec's `contracts/` directory, updates the rule source, repoints the links to the current contract in `README.md`, `docs/03-domain-and-technical-architecture.md`, and `.claude/agents/reviewers/architecture-guard.md`, and records the following:

| Version | Spec | Change | Reason | Review owner |
|---|---|---|---|---|
| 1 | ISK-0000 | Baseline of seven projects and rules A1 to A5 | First compilable graph | Architecture (ADR) owner |
| 2 | ISK-0001 | Adds `Iskender.Hosting` and `Iskender.ControlPlane.Host`; maps the `docs/03` modules; `Domain` and `Management` edges; removes `Gateway.Host` to `Management` | RC-20261106-004; the data plane must deploy without the control plane | Architecture (ADR) owner |

Rule IDs are never reused. A removed rule stays listed as retired.

## Module map

| Module | Assembly | Namespace root |
|---|---|---|
| Hosting/Ingress | `Iskender.Hosting` | `Iskender.Hosting` |
| Identity/Authorization | `Iskender.Domain` | `Iskender.Domain.Identity` |
| Registry/Configuration | `Iskender.Domain` | `Iskender.Domain.Registry` |
| Policy Engine | `Iskender.Domain` | `Iskender.Domain.Policy` |
| Routing/Resilience | `Iskender.Domain` | `Iskender.Domain.Routing` |
| Providers | `Iskender.Providers.Abstractions`, with adapters in `Iskender.Providers.<Provider>` | `Iskender.Providers` |
| Conversations | `Iskender.Domain` | `Iskender.Domain.Conversations` |
| Governance | `Iskender.Domain` | `Iskender.Domain.Governance` |
| Telemetry/Audit | `Iskender.Domain` | `Iskender.Domain.Telemetry` |
| Management | `Iskender.Management` | `Iskender.Management` |
| Plugins | `Iskender.Domain` | `Iskender.Domain.Plugins` |
| Infrastructure | `Iskender.Infrastructure`, with adapters in `Iskender.Infrastructure.<Adapter>` | `Iskender.Infrastructure` |

Each module's ownership and constraints stay in the Module boundaries table of `docs/03-domain-and-technical-architecture.md`, which rule M1 compares with this map on every run.

## Port homes

These place the `docs/03` ports and the request context. Signatures are not defined here. The owner named below freezes them.

| Item | Home | Signature owner |
|---|---|---|
| `GatewayExecutionContext` and capability-bounded views | `Iskender.Core.Contracts` | ISK-0002 |
| `ICredentialResolver` (secret handles) | `Iskender.Core.Contracts` | Later identity and credential specs |
| `IProviderAdapter` | `Iskender.Providers.Abstractions` | ISK-0030 |
| `IPolicyHandler` | `Iskender.Policy.Sdk` | ISK-0052 |
| `IBackendSelector` | `Iskender.Domain.Routing` | Later routing specs |
| `IConversationStore` | `Iskender.Domain.Conversations` | Later conversation specs |
| `IQuotaCoordinator`, `IUsageLedger` | `Iskender.Domain.Governance` | Later governance specs |
| `IPolicyPlanPublisher` | `Iskender.Domain.Policy` | Later policy specs |
| `IPluginTrustStore` | `Iskender.Domain.Plugins` | Later plugin specs |
| Ports shared across modules, such as persistence (ISK-0004) and coordination (ISK-0005) | `Iskender.Domain.Ports` | Those specs |

## Project roles

Only public contract and embeddable projects may be packable, and all of them must be. Being packable does not make an embeddable project's types a supported public API. Compatibility rules for public contract packages are owned by ISK-0002, and `-alpha` prereleases carry no compatibility commitment (ADR-0030).

A packable project's package must carry each of its project references as a dependency, or an embedder restores a package that is missing an assembly it needs. P2 rejects `PrivateAssets`, `IncludeAssets`, and `ExcludeAssets` metadata on a packable project's `ProjectReference`, in an item, an `Update`, or an `ItemDefinitionGroup`, since each can drop the reference from the package. P3 checks the result in the real pack, so a reference dropped by any other route also fails.

| Project | Role |
|---|---|
| `Iskender.Core.Contracts` | public contract |
| `Iskender.Providers.Abstractions` | public contract |
| `Iskender.Policy.Sdk` | public contract |
| `Iskender.Hosting` | embeddable |
| `Iskender.Domain` | embeddable |
| `Iskender.Infrastructure` | embeddable |
| `Iskender.Management` | embeddable |
| `Iskender.Providers.<Provider>` | embeddable |
| `Iskender.Infrastructure.<Adapter>` | embeddable |
| `Iskender.Gateway.Host` | executable |
| `Iskender.ControlPlane.Host` | executable |
| Any project under `tests/` | test |

## Approved direct references

Anything not listed is rejected by rule A5. A new production project must be added here, unless its name matches a pattern.

| Project | May reference |
|---|---|
| `Iskender.Core.Contracts` | (nothing internal) |
| `Iskender.Providers.Abstractions` | `Iskender.Core.Contracts` |
| `Iskender.Policy.Sdk` | `Iskender.Core.Contracts` |
| `Iskender.Providers.<Provider>` | `Iskender.Providers.Abstractions`, `Iskender.Core.Contracts` |
| `Iskender.Domain` | `Iskender.Core.Contracts`, `Iskender.Providers.Abstractions`, `Iskender.Policy.Sdk` |
| `Iskender.Infrastructure` | `Iskender.Domain`, `Iskender.Core.Contracts` |
| `Iskender.Infrastructure.<Adapter>` | `Iskender.Infrastructure`, `Iskender.Domain`, `Iskender.Core.Contracts` |
| `Iskender.Management` | `Iskender.Domain`, `Iskender.Core.Contracts` |
| `Iskender.Hosting` | `Iskender.Domain`, `Iskender.Core.Contracts` |
| `Iskender.Gateway.Host` | `Iskender.Hosting`, `Iskender.Domain`, `Iskender.Infrastructure`, `Iskender.Providers.Abstractions`, `Iskender.Policy.Sdk`, `Iskender.Core.Contracts`, `Iskender.Providers.<Provider>`, `Iskender.Infrastructure.<Adapter>` |
| `Iskender.ControlPlane.Host` | `Iskender.Management`, `Iskender.Domain`, `Iskender.Infrastructure`, `Iskender.Core.Contracts`, `Iskender.Infrastructure.<Adapter>` |

A pattern's `<Provider>` or `<Adapter>` is a single name segment, and `Abstractions` is not a provider. Test projects may reference anything except a host (rule A2).

## Module namespaces in `Iskender.Domain`

The module allow-list starts empty, so no module namespace may use another. Every type in the assembly must sit in a namespace mapped above or in `Iskender.Domain.Ports`. A namespace belongs to a module when it equals the module's root or is a child of it, so `Iskender.Domain.Policy.Cel` is Policy and `Iskender.Domain.PolicyExtras` is unmapped. A type the compiler emits, such as a closure, an async state machine, or `<PrivateImplementationDetails>`, is checked as the declared type that contains it, so a use inside a lambda or an async method body still counts. One that no declared type contains is skipped.

| Module namespace | May use |
|---|---|
| `Iskender.Domain.Identity` | `Iskender.Domain.Ports` |
| `Iskender.Domain.Registry` | `Iskender.Domain.Ports` |
| `Iskender.Domain.Policy` | `Iskender.Domain.Ports` |
| `Iskender.Domain.Routing` | `Iskender.Domain.Ports` |
| `Iskender.Domain.Conversations` | `Iskender.Domain.Ports` |
| `Iskender.Domain.Governance` | `Iskender.Domain.Ports` |
| `Iskender.Domain.Telemetry` | `Iskender.Domain.Ports` |
| `Iskender.Domain.Plugins` | `Iskender.Domain.Ports` |

`Iskender.Domain.Ports` may use no module namespace. There is no shared namespace. IDs and errors shared across modules belong in `Iskender.Core.Contracts` (ISK-0002), and types used in port signatures belong in `Iskender.Domain.Ports`.

## Required references

Rule A12 requires these direct references.

| Project | Must reference |
|---|---|
| `Iskender.Gateway.Host` | `Iskender.Hosting`, `Iskender.Infrastructure` |
| `Iskender.ControlPlane.Host` | `Iskender.Management`, `Iskender.Infrastructure` |

## Forbidden reach

A project in `From` may not reach a project in `To`, directly or transitively. An entry is an exact project name or a pattern. `*` means every project, test projects included, and is valid only in `From`. The walk starts from the project's references, so a project never counts as reaching itself. That case is a cycle, which rule A4 reports.

| Rule | From | To |
|---|---|---|
| A2 | `*` | `Iskender.Gateway.Host`, `Iskender.ControlPlane.Host` |
| A3 | `Iskender.Domain`, `Iskender.Providers.Abstractions`, `Iskender.Providers.<Provider>`, `Iskender.Policy.Sdk`, `Iskender.Management` | `Iskender.Infrastructure`, `Iskender.Infrastructure.<Adapter>` |
| A6 | `Iskender.Gateway.Host` | `Iskender.Management`, `Iskender.ControlPlane.Host` |
| A7 | `Iskender.ControlPlane.Host` | `Iskender.Hosting`, `Iskender.Gateway.Host` |
| A8 | `Iskender.Hosting` | `Iskender.Infrastructure`, `Iskender.Infrastructure.<Adapter>`, `Iskender.Management` |
| A9 | `Iskender.Domain`, `Iskender.Policy.Sdk`, `Iskender.Providers.Abstractions`, `Iskender.Providers.<Provider>` | `Iskender.Management`, `Iskender.Hosting`, `Iskender.Gateway.Host`, `Iskender.ControlPlane.Host` |
| A10 | `Iskender.Domain`, `Iskender.Hosting` | `Iskender.Providers.<Provider>` |
| A11 | `Iskender.Infrastructure` | `Iskender.Infrastructure.<Adapter>` |

## Rules

Each rule has at least one negative fixture, enforced by a meta-test.

| ID | Rule | Source |
|---|---|---|
| A1 | `Iskender.Core.Contracts` references no other `Iskender.*` project. | Baseline, FR-010 |
| A2 | No project, test projects included, reaches `Iskender.Gateway.Host` or `Iskender.ControlPlane.Host`. | Baseline, FR-010, FR-014 |
| A3 | `Iskender.Domain`, `Iskender.Providers.Abstractions`, `Iskender.Providers.<Provider>`, `Iskender.Policy.Sdk`, and `Iskender.Management` do not reach `Iskender.Infrastructure` or an infrastructure adapter. | Baseline, FR-010, FR-011 |
| A4 | The whole project graph has no reference cycles. | Baseline, FR-010 |
| A5 | A production project references only what the approved references allow, and every production project is listed or matches a pattern. | Baseline, FR-009, FR-010 |
| A6 | `Iskender.Gateway.Host` reaches neither `Iskender.Management` nor `Iskender.ControlPlane.Host`. | FR-011 |
| A7 | `Iskender.ControlPlane.Host` reaches neither `Iskender.Hosting` nor `Iskender.Gateway.Host`. | FR-011 |
| A8 | `Iskender.Hosting` reaches neither `Iskender.Infrastructure`, an infrastructure adapter, nor `Iskender.Management`. | FR-011 |
| A9 | `Iskender.Domain`, `Iskender.Policy.Sdk`, and `Iskender.Providers.*` reach neither `Iskender.Management`, `Iskender.Hosting`, nor a host. | FR-011 |
| A10 | `Iskender.Domain` and `Iskender.Hosting` do not reach a provider adapter. | FR-011 |
| A11 | `Iskender.Infrastructure` does not reach an infrastructure adapter. | FR-011 |
| A12 | `Iskender.Gateway.Host` references `Iskender.Hosting` and `Iskender.Infrastructure`, and `Iskender.ControlPlane.Host` references `Iskender.Management` and `Iskender.Infrastructure`. | FR-012 |
| A13 | Every project reference and import resolves to a file in the repository, and every SDK is approved. Conditions are ignored, so every branch counts, and the root build files count as imports of every project. No project takes a direct assembly reference to, or a package named like, an `Iskender.*` project. Every project listed in `Iskender.sln` was scanned. | FR-013 |
| A14 | Every project is under `src/` or `tests/`, no project under `src/` is named `*.Tests`, and no two project files share a name, ignoring case. | FR-013, FR-014 |
| A15 | A production project's assembly name, root namespace, package ID, and target name equal its project name, and its target name may also be its assembly name. | FR-015 |
| A16 | A production assembly grants `InternalsVisibleTo` only to a test project. | FR-016 |
| A17 | A type in an `Iskender.Domain` module namespace uses no type in another module's namespace, unless the module allow-list permits it. | FR-017 |
| A18 | Every type in `Iskender.Domain` is in a mapped namespace or `Iskender.Domain.Ports`, and `Iskender.Domain.Ports` uses no module namespace. | FR-017 |
| A19 | A production project takes no package, framework reference, or SDK that its role denies, directly or transitively. | FR-018 |
| B1 | Every project takes package versions only from central management. | FR-019 |
| B2 | Every project has a committed lock file. | FR-020 |
| B3 | The root build files set the pinned analyzer baseline and every other required setting exactly once, with its required value and no condition, including central management, transitive pinning, and disabled version overrides. | FR-019, FR-020, FR-021, FR-022 |
| B4 | No required setting appears outside the root build files, under any condition, in a target, or in a nested configuration file, and no forbidden setting or item appears anywhere, the root build files included. | FR-022 |
| B5 | No tracked source file disables nullable checks, by a directive or by being treated as generated code. | FR-022 |
| B6 | Every warning suppression, in a project, source, or configuration file, is listed below. | FR-023 |
| P1 | Every project has a role, and a project is packable exactly when its role says so. | FR-025 |
| P2 | Every project reference from a packable project targets a packable project and sets no asset metadata. | FR-026 |
| P3 | Every `Iskender.*` dependency of every package in a pack of the solution is also in the pack's output, and each packable project's package lists its project references as dependencies. | SC-007 |
| M1 | Each module in the `docs/03` Module boundaries table has exactly one map entry, with no missing, extra, or duplicate entries. | FR-005, FR-007 |
| M2 | Each map and port entry names an existing project, an approved pattern, or a namespace under `Iskender.Domain` that the module map or the module namespaces section defines. | FR-005, FR-006 |
| C1 | This contract's tables and version match the rule source. | FR-002 |
| C2 | A missing or unreadable input, or a run that finds no projects or no modules, fails. | FR-032 |
| C3 | `.github/CODEOWNERS` covers the rule source, `.editorconfig`, the root build files, and every project under `src/`. | FR-003 |
| C4 | Each executable is attested and checksummed in `.github/workflows/ci.yml`, and each executable, each public contract project, and `Iskender.Hosting` is a behavior-bearing path in `.github/scripts/docs-gate.sh` and `CONTRIBUTING.md`. | FR-028 |

## Reading references (A13)

A13 fails closed: a reference the scan cannot read is a violation, rather than something it skips. The scan reads names as written and does not evaluate MSBuild, so each of these is reported:

- A `ProjectReference` or `Import` holding an MSBuild property or item expression, or naming no file in the repository.
- A `Reference` whose `Include` or `HintPath` holds an MSBuild property (`$(...)`), an item list or transform (`@(...)`), metadata or an escape (any `%`, such as `%2E`), or a wildcard (`*` or `?`). Otherwise its `Include`, up to any comma, is read as an assembly name and as a file name, and the file name of its `HintPath` is read too.
- A `PackageReference`, `GlobalPackageReference`, `FrameworkReference`, `PackageVersion`, or `PackageDownload` whose name holds any of the same, since rules A19 and B1 match these by name.
- Any item of a type that the compiler receives as a reference, or that feeds `ResolveAssemblyReference` other than `Reference` and project references: `ReferencePath`, `ReferencePathWithRefAssemblies`, `_ResolvedProjectReferencePaths`, `_ExplicitReference`, `ResolvedCompileFileDefinitions`, `ResolvedCompileFileDefinitionsToAdd`, and `AddModules`. These come from the `References` parameter of the `Csc` task, the `Assemblies` and `AssemblyFiles` parameters of `ResolveAssemblyReference`, and the items that SDK 10.0.403 adds to them. A project file has no use for any of them.
- A task `Output` that creates any of the items above, since its value is unknown.

## Denied packages

A prefix matches the exact package ID or the ID followed by a dot. Both a project's direct references and its `packages.lock.json` are checked, so a package that arrives transitively counts. A direct assembly reference is checked against the same prefixes, using the names that A13 reads from it, so `<Reference Include="Innocent"><HintPath>..\lib\Npgsql.dll</HintPath></Reference>` counts as `Npgsql`. Executables are the exception and are checked on direct references only, because the approved references let them compose adapters, whose packages then appear in their lock files.

| Category | Package prefixes | Framework references | SDKs |
|---|---|---|---|
| `aspnetcore` | `Microsoft.AspNetCore` | `Microsoft.AspNetCore.App` | `Microsoft.NET.Sdk.Web` |
| `cloud` | `Azure`, `Microsoft.Azure`, `AWSSDK`, `Amazon`, `Google.Cloud`, `Google.Apis` | | |
| `provider` | `OpenAI`, `Anthropic`, `Anthropic.SDK`, `Google.GenAI`, `Mscc.GenerativeAI`, `Betalgo.OpenAI`, `Betalgo.Ranges.OpenAI` | | |
| `driver` | `Npgsql`, `StackExchange.Redis`, `Microsoft.Data.SqlClient`, `System.Data.SqlClient`, `Microsoft.Data.Sqlite`, `MySql`, `MySqlConnector`, `Oracle`, `MongoDB` | | |
| `data-access` | `Microsoft.EntityFrameworkCore`, `Dapper`, `linq2db`, `NHibernate` | | |

| Project | Denied categories |
|---|---|
| `Iskender.Core.Contracts`, `Iskender.Policy.Sdk`, `Iskender.Providers.Abstractions`, `Iskender.Domain` | `aspnetcore`, `cloud`, `provider`, `driver`, `data-access` |
| `Iskender.Infrastructure` | `cloud`, `provider`, `driver` |
| `Iskender.Hosting`, `Iskender.Management` | `cloud`, `provider`, `driver`, `data-access` |
| `Iskender.Gateway.Host`, `Iskender.ControlPlane.Host` | `cloud`, `provider`, `driver` |
| `Iskender.Providers.<Provider>` | `driver`, `data-access` |
| `Iskender.Infrastructure.<Adapter>` | `provider` |

Test projects are exempt, since they ship in no package. Provider adapters may take the `cloud` category because a cloud-hosted provider's SDK can sit under a cloud prefix, such as `Azure.AI.OpenAI`. This is a deny-list: a package it does not name is not caught, and "its own" SDK within an allowed category is checked by review. A `GlobalPackageReference` in the root `Directory.Packages.props` counts as a direct package of every project. Rule B1 rejects a `PackageDownload` item anywhere, the root build files included, whatever its operation, because NuGet restores it at its own exact version outside central management and the lock file.

## Approved SDKs

| SDK | Used by |
|---|---|
| `Microsoft.NET.Sdk` | Libraries and test projects |
| `Microsoft.NET.Sdk.Web` | Executables |

## Protected settings

These may be set only in the root `Directory.Build.props` or `Directory.Packages.props`.

| Setting | Required value | File |
|---|---|---|
| `TargetFramework` | `net10.0` | `Directory.Build.props` |
| `LangVersion` | `14.0` | `Directory.Build.props` |
| `Nullable` | `enable` | `Directory.Build.props` |
| `TreatWarningsAsErrors` | `true` | `Directory.Build.props` |
| `EnableNETAnalyzers` | `true` | `Directory.Build.props` |
| `AnalysisLevel` | `10.0-recommended` | `Directory.Build.props` |
| `EnforceCodeStyleInBuild` | `true` | `Directory.Build.props` |
| `Deterministic` | `true` | `Directory.Build.props` |
| `RestorePackagesWithLockFile` | `true` | `Directory.Build.props` |
| `ManagePackageVersionsCentrally` | `true` | `Directory.Packages.props` |
| `CentralPackageTransitivePinningEnabled` | `true` | `Directory.Packages.props` |
| `CentralPackageVersionOverrideEnabled` | `false` | `Directory.Packages.props` |

These may not be set anywhere in a project or imported file, the root build files included: `TargetFrameworks`, `TargetFrameworkVersion`, `AnalysisMode`, `AnalysisMode<Category>`, `AnalysisLevel<Category>`, `RunAnalyzers`, `RunAnalyzersDuringBuild`, `RunAnalyzersDuringLiveAnalysis`, `CodeAnalysisTreatWarningsAsErrors`, `CodeAnalysisRuleSet`, `WarningLevel`, `WarningsAsErrors`, `MSBuildTreatWarningsAsErrors`, `MSBuildWarningsNotAsErrors`, `MSBuildWarningsAsMessages`, `NuGetLockFilePath`, `RestoreLockedMode`, `ImportDirectoryBuildProps`, `ImportDirectoryBuildTargets`, `ImportDirectoryPackagesProps`, `DirectoryBuildTargetsPath`, `CustomBeforeDirectoryBuildTargets`, `CustomAfterDirectoryBuildTargets`, `CustomBeforeMicrosoftCommonTargets`, `CustomAfterMicrosoftCommonTargets`, `CustomBeforeMicrosoftCSharpTargets`, `CustomAfterMicrosoftCSharpTargets`, `BeforeMicrosoftNETSdkTargets`, `AfterMicrosoftNETSdkTargets`, `LanguageTargets`, `CommonTargetsPath`, `CSharpTargetsPath`, `CSharpCoreTargetsPath`, `CodeAnalysisTargets`, `MSBuildExtensionsPath`, `MSBuildUserExtensionsPath`, `MSBuildProjectExtensionsPath`, `NuGetBuildTasksPackTargets`, `ILLinkTargetsPath`, `ILCompilerTargetsPath`, `MsTestToolsTargets`, `MsAppxPackageTargets`, `ReportingServicesTargets`, `WebPublishProfileFile`, `_BlazorWebAssemblyPropsFile`, `_BlazorWebAssemblyTargetsFile`, `_BlazorWebAssemblyVersionedTargetsFile`, `_ComputeTargetsDir`, `_ContainersTargetsDir`, `_CopyTargetsDir`, `_DotNetCLIToolTargetsDir`, `_PublishProfilesDir`, `_PublishTargetsDir`, `_SourceLinkSdkSubDir`, `_TransformTargetsDir`, `_WebAssemblyPropsFile`, `_WebAssemblyTargetsFile`, `AdditionalImport`, `AfterMicrosoftNetSdkProps`, `AfterTargetFrameworkInferenceTargets`, `AlternateCommonProps`, `BeforeTargetFrameworkInferenceTargets`, `CSharpDesignTimeTargetsPath`, `CustomAfterBlazorWebAssemblySdkTargets`, `CustomAfterDirectoryBuildProps`, `CustomAfterMicrosoftCommonCrossTargetingTargets`, `CustomAfterMicrosoftCommonProps`, `CustomAfterMicrosoftVisualBasicTargets`, `CustomAfterRazorSdkTargets`, `CustomAfterStaticWebAssetsSdkTargets`, `CustomBeforeBlazorWebAssemblySdkTargets`, `CustomBeforeDirectoryBuildProps`, `CustomBeforeMicrosoftCommonCrossTargetingTargets`, `CustomBeforeMicrosoftCommonProps`, `CustomBeforeMicrosoftVisualBasicTargets`, `CustomBeforeRazorSdkTargets`, `CustomBeforeStaticWebAssetsSdkTargets`, `DirectoryBuildPropsPath`, `DirectoryPackagesPropsPath`, `FSharpDesignTimeTargetsPath`, `FSharpOverridesTargetsShim`, `FSharpPropsShim`, `FSharpTargetsShim`, `MicrosoftNETBuildExtensionsTargets`, `MSBuildBinPath`, `MSBuildExtensionsPath32`, `MSBuildProjectFile`, `MSBuildProjectFullPath`, `MSBuildSDKsPath`, `MSBuildThisFile`, `MSBuildThisFileDirectory`, `MSBuildThisFileName`, `MSBuildToolsPath`, `MSBuildToolsVersion`, `MSBuildWebTargetsPath`, `MSBuildWorkerTargetsPath`, `NETCoreSdkBundledCliToolsProps`, `NETCoreSdkBundledMSBuildInformationProps`, `NETCoreSdkBundledVersionsProps`, `NetFrameworkPropsPath`, `NetFrameworkTargetsPath`, `NuGetPropsFile`, `NuGetRestoreTargets`, `PublishProfileName`, `PublishProtocol`, `RazorDesignTimeTargets`, `RazorSdkCurrentVersionProps`, `RazorSdkCurrentVersionTargets`, `StaticWebAssetsSdkCurrentVersionProps`, `StaticWebAssetsSdkCurrentVersionTargets`, `VisualBasicCoreTargetsPath`, `VisualBasicDesignTimeTargetsPath`, `VisualBasicTargetsPath`, `VisualStudioVersion`, `VSTestTargets`, `CompilerResponseFile`, `BaseIntermediateOutputPath`, `_DirectoryBuildPropsFile`, `_DirectoryBuildPropsBasePath`, `_DirectoryBuildTargetsFile`, `_DirectoryBuildTargetsBasePath`, `_DirectoryPackagesPropsFile`, `_DirectoryPackagesPropsBasePath`, `PublishProfile`, `PublishProfileFullPath`, `ArtifactsPath`, `UseArtifactsOutput`, `ProjectToOverrideProjectExtensionsPath`, `ProjectExtensionsPathForSpecifiedProject`, `NuGetPackageRoot`, `ProjectAssetsFile`, `Features`, `IsImplicitlyTriggeredBuild`, `OptimizeImplicitlyTriggeredBuild`, `DiscoverEditorConfigFiles`, `DiscoverGlobalAnalyzerConfigFiles`, `GenerateMSBuildEditorConfigFile`, `SkipGlobalAnalyzerConfigForPackage`, `EffectiveCodeAnalysisTreatWarningsAsErrors`, `ResolvedCodeAnalysisRuleSet`, `CscToolPath`, `CscToolExe`, `SkipCompilerExecution`, `ProduceOnlyReferenceAssembly`, `MicrosoftCodeAnalysisNetAnalyzers<Suffix>`, `_GlobalAnalyzerConfig<Suffix>`, and the `Analyzer`, `EditorConfigFiles`, and `GlobalAnalyzerConfigFiles` items. An item counts whatever its operation, so `<Analyzer Remove="@(Analyzer)" />` is a violation. An entry with a placeholder in angle brackets, such as `AnalysisMode<Category>` or `_GlobalAnalyzerConfig<Suffix>`, stands for every name that starts with the text before the placeholder and continues past it.

The properties from `Features` to `_GlobalAnalyzerConfig<Suffix>` switch off nullable analysis, analyzers, code style, or configuration discovery without naming a required setting. They come from every `$(Name)` that SDK 10.0.403 reads in `Roslyn/Microsoft.CSharp.Core.targets`, which holds the `Csc` invocation, in `Roslyn/Microsoft.Managed.Core.targets`, which holds `_ComputeSkipAnalyzers` and editorconfig and global-config discovery, in `Roslyn/Microsoft.Managed.Core.CurrentVersions.targets`, and in `Microsoft.NET.Sdk.Analyzers.targets` and `Microsoft.CodeAnalysis.NetAnalyzers.targets` (258 names, 237 not listed before). `Iskender.Domain` was given one violation of each kind: a nullable warning (CS8603), an analyzer rule that `.editorconfig` raises (CA1305), an analyzer rule that only the analysis level raises (CA1051), and a code-style rule (IDE0161). Each name was then set on its own in the project body to `true`, `false`, a nonsense value, a version number, and the path of a global analyzer config or rule set file in the project, and the build was run. A name is listed if any kind of violation stopped being reported while the build still compiled. Most of the others fail the build on their own, with an error such as CS2013 or MSB4030. Three were decided by a separate probe: `IsImplicitlyTriggeredBuild` with `OptimizeImplicitlyTriggeredBuild` skips analyzers only together, `CscToolPath` with `CscToolExe` ran a stand-in compiler and the build succeeded, and `SkipCompilerExecution` succeeded from an earlier build's output. `ProduceOnlyReferenceAssembly` skips compiling method bodies. The compiler currently crashes on such a body when it has a diagnostic, so the build fails, but that is a compiler defect rather than a check, so it is listed too. `DiscoverGlobalAnalyzerConfigFiles` changed nothing, because no global config is allowed (see below), but its only effect is to switch off configuration discovery, so it is listed with `DiscoverEditorConfigFiles`.

The properties from `DirectoryBuildTargetsPath` to `ProjectAssetsFile` each decide a file that the SDK imports or passes to the compiler, which the project scan would not otherwise read. They come from these sources in SDK 10.0.403:

- **Named in an import**: every `$(Name)` in the `Project` attribute of an `<Import>`, from every file under `sdk/10.0.403` with a `<Project>` element, with comments removed (262 files, 124 with an `<Import>`, 95 names). This includes MSBuild's reserved properties, such as `MSBuildThisFile`, although a project that sets one already fails with MSB4004, so the list stays the plain output of the enumeration and an SDK upgrade can compare it directly. Files outside that directory, such as workload manifests, are not enumerated.
- **Passed as a response file**: every `$(Name)` in a task's `ResponseFiles` attribute in the same files. The only one is `CompilerResponseFile`.
- **Feeding an import, at any depth**: found by evaluation, so it has no depth limit. Each of the 1,908 property names that a file under `sdk/10.0.403` defines, or reads in a value or condition, was set on its own in `Iskender.Domain` and in `Iskender.Gateway.Host`, both before an explicit `Sdk.props` import and in the project body, to an absolute path in the repository, to `true`, and to `false`. MSBuild evaluated each project in memory, and a name was kept if an import-named property moved to a path in the repository or an import from outside the SDK appeared. Names that failed evaluation for all three values were set again to version numbers. Besides the properties already listed, `ArtifactsPath` and `UseArtifactsOutput` qualified: either one moves `MSBuildProjectExtensionsPath` from the project's `obj/` to `artifacts/obj/<project>/` or below the given path, both of which the scan skips. `TargetPlatformVersion` fails evaluation off Windows for every value and only selects Windows targets inside the SDK, so it is not listed.
- **Feeding an import only together, or only when the file exists**: setting one name at a time cannot find these, so every name that the definitions of the import-named properties read, at any depth (294 names), was also decided by reading its use. `ProjectToOverrideProjectExtensionsPath` set to the project's own path makes `Sdk.props` take `MSBuildProjectExtensionsPath` from `ProjectExtensionsPathForSpecifiedProject`, and `_DirectoryBuildPropsFile` and its siblings, `PublishProfile`, and `PublishProfileFullPath` redirect an import only when the file they name exists. Each listed name was confirmed by a marker property from the redirected file. The other names select files inside the SDK, such as `Language`, `OutputType`, and the `Using*` flags, or matter only together with a property already listed, such as `_PublishProfileRootFolder` with `PublishProfile`, and are not listed. `RoslynTargetsPath` is overwritten after the project body, so it redirects nothing.
- **Read from NuGet's generated files**: restore writes `<project file>.nuget.g.props` to `obj/`, which sets `NuGetPackageRoot` and `ProjectAssetsFile` only if they are empty, then imports each package's build files from under `NuGetPackageRoot`. A project that sets `NuGetPackageRoot` first redirects every package import, and one that sets `ProjectAssetsFile` chooses the assets file from which the SDK takes compile references and analyzers.

Two routes need no property, since MSBuild imports them from a fixed place:

- **`<project file>.user`**: MSBuild imports this from beside a project file whenever it exists. The scan reads it as an import of that project, so every rule sees what it adds. Visual Studio writes these locally, so one that sets only unprotected properties passes.
- **`<project file>.*.props` and `.targets` in a project's `obj/`**: MSBuild imports these on any checkout that holds them, including a fresh clone if they were force-added to Git. Each one is a B4 violation, other than NuGet's `.nuget.g.props` and `.nuget.g.targets`, which every restore overwrites. Only the `obj/` directly under each project is read, because the properties that move it are forbidden above.

A `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, or `.editorconfig` below the repository root is a B4 violation. So is a `Directory.Build.rsp`, `MSBuild.rsp`, or `.globalconfig` anywhere, including the root. A root `Directory.Build.targets` is allowed.

MSBuild imports the root `Directory.Build.props`, `Directory.Packages.props`, and `Directory.Build.targets` into every project, so each is read as an import of every project. A reference, package, or `Import` in one of them counts against every project, whatever its condition. The only difference is that the root `Directory.Build.props` and `Directory.Packages.props` set the required settings above, once each and with no condition (B3), and a `GlobalPackageReference` states its version there, since central management has no other place for it.

## Listed suppressions

| ID | Where | Reason |
|---|---|---|
| `NU1900` | `WarningsNotAsErrors` in `Directory.Build.props` | An unreachable audit feed must not fail a build that has no network. CI checks vulnerabilities separately. |

Rule B6 looks for `NoWarn` and `WarningsNotAsErrors` as properties or item metadata, `#pragma warning disable`, `[SuppressMessage]`, `[UnconditionalSuppressMessage]`, `[GeneratedCode]`, an `AssemblyAttribute` item naming one of those three attributes, whose check ID it reads from `_Parameter2` as A16 reads a grant from `_Parameter1`, `generated_code = true` in `.editorconfig`, and `dotnet_diagnostic` or `dotnet_analyzer_diagnostic` severities of `none`, `silent`, `suggestion`, `refactoring`, or `default` in `.editorconfig`, ignoring case. These are every severity that SDK 10.0.403 accepts other than `warning` and `error`. A probe set each name on CS8603 and CA1051, and the compiler ignored any other name, such as `hidden` or `info`, so the diagnostic stayed an error.

Rule B5 also rejects a tracked source file under `src/` or `tests/` that is named, ignoring case, `*.g.cs`, `*.g.i.cs`, `*.designer.cs`, `*.generated.cs`, or `TemporaryGeneratedFile_*`, or that has `<auto-generated` or `<autogenerated` in a comment before its first code, because the compiler turns off nullable checks and analyzers for such a file. This check reads the original text, before comments are blanked out.

The source scans read every `.cs` file under `src/` and `tests/` outside the skipped directories, so rule B5 also rejects a `Compile` item that adds any other file: one with another extension, one outside those two directories, one in a skipped directory such as `obj/`, or one the scan cannot read because it holds an MSBuild expression, a `%`, or a recursive `**` wildcard, which reaches into `obj/` and `bin/`. A task output that creates a `Compile` item is rejected too. MSBuild resolves the path from the project's directory, wherever the item is defined. `Remove` and `Update` add no file, so they pass, and so does a `Compile` item that links a `.cs` file from another project under `src/` or `tests/`, since the scans already read that file.

Source scans (B5, B6, and the attribute form of A16) work on text. A directive matches with leading whitespace and with whitespace after the `#`, and `#nullable disable` matches with a trailing `warnings` or `annotations`. An attribute matches anywhere, including inside a list such as `[Fact, SuppressMessage(...)]`, with or without an `assembly:` or `module:` target, an `Attribute` suffix, or a namespace qualifier, and with white space, comments, or line breaks between its name and `(`. Names are matched after `\u` and `\U` escapes are decoded and formatting characters are removed, since the compiler ignores both in an identifier, so `Internals\u0056isibleTo` matches. Arguments are read from string tokens, so a quoted value inside a comment is ignored. An argument that is not a plain string literal, such as a named argument, a constant, or an interpolated string, reads as `(unreadable)`, which matches no test project or listed suppression, so it fails. An alias hides each later use from a text match, so a `using` or `global using` alias of `InternalsVisibleToAttribute` in a production source file is itself an A16 violation, and an alias of `SuppressMessageAttribute`, `UnconditionalSuppressMessageAttribute`, or `GeneratedCodeAttribute` is itself a B6 violation. The same holds for a `Using` item with `Alias` metadata in a project or imported file, which the SDK writes as a `global using` alias to a generated file in `obj/`. An item matches with or without the `Attribute` suffix, a namespace, or `global::`. The scan lexes each whole file. Comments, including block comments over several lines, and string literals, including verbatim and raw strings over several lines, are blanked out before matching (a string after `@` is verbatim, never raw, even when it starts with three quotes, as `@"""x"` does), so a test can name these forms in any string. The code inside an interpolation hole is not blanked, so an attribute on a lambda there still counts. A directive line is read as written, apart from a trailing `//` comment. Code in a branch that `#if` excludes is still scanned, which can only add a violation.

## Built assemblies

The scans above read what a project and its sources say. A test in check 5 also reads what the compiler produced: each production project's assembly at `bin/<configuration>/<TargetFramework>/<project name>.dll`, in the configuration the tests were built in, with `System.Reflection.Metadata`. It loads nothing. It reports through the existing rule IDs:

- **A13**: an assembly reference to an `Iskender.*` assembly that the project cannot reach through approved references in the rule source. The compiler records a reference to every assembly whose types the code uses, including through an approved reference, so the allowed set is everything reachable that way.
- **A19**: an assembly reference that matches a prefix its role denies, such as `Microsoft.AspNetCore.Http.Abstractions` under `Microsoft.AspNetCore`.
- **A15**: an assembly whose name differs from its project name.
- **A16**: an `InternalsVisibleToAttribute` on the assembly that grants to anything other than a test project.
- **B4**: an assembly with a declared method or field whose signature holds a reference type but no `NullableAttribute` or `NullableContextAttribute` anywhere, which means nullable analysis was off. Members the compiler synthesized, named with `<` or marked `[CompilerGenerated]`, are not counted, since it annotates none of them.
- **C2**: a missing assembly, with the hint to build the solution first in that configuration. A renamed target file therefore fails here.

So a route fails here whatever spelling reached the compiler. Every reference, framework reference, `InternalsVisibleTo`, and target-name form that T046, T048, T049, T051, T054, and T056 probed was caught by this check alone. The `SuppressMessage` forms were not, because `SuppressMessageAttribute` is conditional on `CODE_ANALYSIS` and never reaches metadata, so B6 rests on the scans.

## Violation format

Every message follows this shape, and all violations appear in one run, ordered by rule ID in contract order, then source, then target:

```text
<rule ID>: <source> -> <target or setting>: <what is wrong>. See <contract path> (version <n>).
```

For example:

```text
A5: Iskender.Management -> Iskender.Infrastructure: not an approved reference. See specs/0001-solution-dependency-boundaries-build-conventions/contracts/module-dependency-rules.md (version 2).
```

The rule ID leads to the section to consult: its row in the Rules section above states the rule and its source.

## `docs/03` architectural test expectations

| `docs/03` expectation | Status |
|---|---|
| Package dependency direction | Enforced here |
| No direct provider SDK types in public common contracts | Enforced here at package level (A19); type-level follow-up with ISK-0030 |
| No cross-module database table updates | Deferred to ISK-0004, since no tables exist |
| Organization scope on every query and event | Deferred to ISK-0004 and the specs that add queries and events |
| Same request lifecycle in embedded and container hosts | Deferred to ISK-0006 and ISK-0007 |
| Two independent replicas against one resource snapshot | Deferred to ISK-0057, which publishes config revisions, with multi-replica proof in ISK-0085 |

## Threat model and known limits

### What the rules defend against

The rules defend against an accidental or careless edit, and against a deliberate one by a contributor whose change goes through CODEOWNERS review. They make such a change fail CI with a named rule, so a reviewer does not have to spot it.

They do not defend against a compromised SDK or NuGet feed, a malicious `Exec` or `MSBuild` task, or a change a reviewer approves knowing what it does. The SDK and the packages run code during every build, and the rules run inside that build.

### Layers

| Layer | Classes of route it closes |
|---|---|
| Project XML scan | Project and assembly references, packages, framework references, SDKs, and imports, read with no MSBuild evaluation, so every condition counts and an unreadable name fails (the graph and boundary rules, B1 to B4, B6, P1, P2). Protected and forbidden settings anywhere in an imported file, a `.user` file, or `obj/` (B3, B4) |
| Source scans | Each `.cs` file under `src/` and `tests/` is lexed whole: nullable directives and generated-code markers (B5), suppressions (B6), and `InternalsVisibleTo` grants (A16), whatever the comments, line breaks, escapes, or aliases. `Compile` items that add a file the scans do not read (B5) |
| Built-assembly check | Any reference, rename, `InternalsVisibleTo` grant, or switched-off nullable analysis that reached the compiler's output, by any spelling (A13, A15, A16, A19, B4, C2). See Built assemblies |
| Compiled types | Type-level dependencies between `Iskender.Domain` module namespaces, read from the compiled assembly (A17, A18) |
| Compiler and analyzers | `TreatWarningsAsErrors`, nullable analysis, and the pinned analysis level, set once in the root build files (B3) and protected from being switched off (B4, B6) |
| Packing | The real pack proves that each package's internal dependencies are packed and that it keeps each project reference (P3) |
| CODEOWNERS | The rule source, `.editorconfig`, the root build files, every project under `src/` (C3), and `.github/`, which holds CI's command lines |

### Known limits

- **Review is requested, not enforced**: Rule C3 and several limits below rely on CODEOWNERS. While rules R5 and R6 in `docs/18-branch-protection.md` stay unapplied under logged exception DEC-20261105-002, an entry requests the owner's review and GitHub does not require it. C3 proves coverage, not that a review happened.
- **Version increments**: C1 proves that this contract and the rule source agree. Nothing proves that a changed rule also incremented the version, which review covers.
- **Build routes a project-file reader cannot judge**: A custom target can run a tool through an `Exec` or `MSBuild` task. Review covers this. This does not cover task outputs: an `Output` element's `PropertyName` counts as a property set in a target, and its `ItemName` as an item of that type, so B4 and the other rules see them. Its value is unknown, so one that creates a `ProjectReference`, `Reference`, or `PackageReference` is an A13 violation.
- **New SDK routes**: The forbidden settings come from the SDK 10.0.403 enumeration described under Protected settings. A later SDK can add a property or item that these lists do not name, so an SDK upgrade should repeat it.
- **Generated types**: A18 treats a type that a source generator emits like any other, so a generator that emits into an unmapped namespace fails it. The spec that first needs one adds an exclusion through a contract revision.
- **Package sources**: A committed `nuget.config` is not checked. Lock files record package content hashes, so a `--locked-mode` restore rejects a package whose content differs.
- **Private storage models**: No rule keeps them out of a package's public surface, because none exists yet. The first spec that adds one (ISK-0004) owns this.
- **Runtime reflection**: `Assembly.Load` and similar calls cannot be seen by reference checks. `docs/13` keeps unreviewed reflection out of the request path by review.
- **Command-line properties**: A `-p:` argument can override a setting. CI's command lines are protected by the `.github/` CODEOWNERS entry.
- **Empty namespaces**: A namespace with no types cannot be observed in compiled output. Until later specs add types, the module map is checked as data, and rules A17 and A18 are proved on fixtures.
- **Host type-level rules**: No project may reference a host, so hosts are checked through the project graph only.
- **`.editorconfig` weakening**: Rule B6 sees a suppressing severity, but not a deleted severity line or a lowered `csharp_style_*` severity. Those are protected by the `.editorconfig` CODEOWNERS entry (rule C3).
- **Source scans read all branches**: Code that an `#if` excludes is still scanned, which can only add a violation.
- **Suppressions leave no metadata**: `SuppressMessage`, `UnconditionalSuppressMessage`, and `#pragma warning disable` never reach the built assembly, so B6 depends on the source and project scans alone.
- **The built-assembly check sees what the compiler kept**: An unused reference leaves no assembly reference, though it also has no effect. B4 is checked only for an assembly with a declared reference-typed member. The check reads the files in `bin/`, so a stale file from an earlier local build can hide a rename. CI builds fresh before check 5.
- **Skipped directories**: The scan skips `bin/`, `obj/`, `artifacts/`, `TestResults/`, `.git/`, and `node_modules/`, all of which Git ignores, though a file can still be force-added. A project there builds only if `Iskender.sln` lists it or a scanned project references it, and rule A13 rejects both. A build file there takes effect only if something imports it. An `Import` that names one is read like any other, and the one implicit import from these directories, from a project's `obj/`, is reported under Protected settings.

## Negative security and failure scenarios

| Scenario | Risk if allowed | Rule |
|---|---|---|
| A provider adapter references `Iskender.Domain` | An adapter could own tenancy or conversations, or treat provider IDs as ownership | A5 |
| `Iskender.Policy.Sdk` references `Iskender.Domain` or `Iskender.Infrastructure` | Plugin authors compile against internals, which defeats capability-bounded views | A5, A3 |
| `Iskender.Management` references `Iskender.Infrastructure` | Admin operations bypass domain validation and ownership checks | A5, A3 |
| The data-plane host reaches `Iskender.Management` | Management surface and its dependencies ship in the internet-facing process | A6 |
| `Iskender.Domain` takes a cloud SDK or database driver | Cloud lock-in, with storage models leaking into public packages | A19 |
| `Iskender.Infrastructure` or an unrelated project takes a driver or cloud SDK | Every embedder inherits it, so one adapter's choice is imposed on all | A19 |
| A production assembly grants `InternalsVisibleTo` to another | The boundary holds in the graph but not in practice | A16 |
| A project under `src/` is named `*.Tests` | It escapes the allow-list | A14, A5 |
| A type in `Iskender.Domain` sits outside every mapped namespace | Modules share it freely, which bypasses the module rule | A18 |
| The rule source is loosened without an owner's review | Enforcement is removed by the change it should stop | C3 |
| A project property imports a file the checks do not read | A forbidden reference or a weakened setting arrives unseen | B4 |
| A tracked source file is named or marked as generated | Nullable checks and analyzers turn off with no directive to find | B5, B6 |
| A second project reuses an approved project's file name | A forbidden dependency passes as an approved edge | A14 |
| A packable project's package omits one of its project references | Embedders restore a package that cannot load | P2, P3 |
