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

The module allow-list starts empty, so no module namespace may use another. Every type in the assembly must sit in a namespace mapped above or in `Iskender.Domain.Ports`. A namespace belongs to a module when it equals the module's root or is a child of it, so `Iskender.Domain.Policy.Cel` is Policy and `Iskender.Domain.PolicyExtras` is unmapped.

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
| A15 | A production project's assembly name, root namespace, and package ID equal its project name. | FR-015 |
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
| P2 | Every project reference from a packable project targets a packable project. | FR-026 |
| P3 | Every `Iskender.*` dependency of every package in a pack of the solution is also in the pack's output. | SC-007 |
| M1 | Each module in the `docs/03` Module boundaries table has exactly one map entry, with no missing, extra, or duplicate entries. | FR-005, FR-007 |
| M2 | Each map and port entry names an existing project, an approved pattern, or a namespace under `Iskender.Domain` that the module map or the module namespaces section defines. | FR-005, FR-006 |
| C1 | This contract's tables and version match the rule source. | FR-002 |
| C2 | A missing or unreadable input, or a run that finds no projects or no modules, fails. | FR-032 |
| C3 | `.github/CODEOWNERS` covers the rule source, `.editorconfig`, the root build files, and every project under `src/`. | FR-003 |
| C4 | Each executable is attested and checksummed in `.github/workflows/ci.yml`, and each executable, each public contract project, and `Iskender.Hosting` is a behavior-bearing path in `.github/scripts/docs-gate.sh` and `CONTRIBUTING.md`. | FR-028 |

## Denied packages

A prefix matches the exact package ID or the ID followed by a dot. Both a project's direct references and its `packages.lock.json` are checked, so a package that arrives transitively counts. Executables are the exception and are checked on direct references only, because the approved references let them compose adapters, whose packages then appear in their lock files.

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

Test projects are exempt, since they ship in no package. Provider adapters may take the `cloud` category because a cloud-hosted provider's SDK can sit under a cloud prefix, such as `Azure.AI.OpenAI`. This is a deny-list: a package it does not name is not caught, and "its own" SDK within an allowed category is checked by review. A `GlobalPackageReference` in the root `Directory.Packages.props` counts as a direct package of every project.

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

These may not be set anywhere in a project or imported file, the root build files included: `TargetFrameworks`, `TargetFrameworkVersion`, `AnalysisMode`, `AnalysisMode<Category>`, `AnalysisLevel<Category>`, `RunAnalyzers`, `RunAnalyzersDuringBuild`, `RunAnalyzersDuringLiveAnalysis`, `CodeAnalysisTreatWarningsAsErrors`, `CodeAnalysisRuleSet`, `WarningLevel`, `WarningsAsErrors`, `MSBuildTreatWarningsAsErrors`, `MSBuildWarningsNotAsErrors`, `MSBuildWarningsAsMessages`, `NuGetLockFilePath`, `RestoreLockedMode`, `ImportDirectoryBuildProps`, `ImportDirectoryBuildTargets`, `ImportDirectoryPackagesProps`, `DirectoryBuildTargetsPath`, `CustomBeforeDirectoryBuildTargets`, `CustomAfterDirectoryBuildTargets`, `CustomBeforeMicrosoftCommonTargets`, `CustomAfterMicrosoftCommonTargets`, `CustomBeforeMicrosoftCSharpTargets`, `CustomAfterMicrosoftCSharpTargets`, `BeforeMicrosoftNETSdkTargets`, `AfterMicrosoftNETSdkTargets`, `LanguageTargets`, `CommonTargetsPath`, `CSharpTargetsPath`, `CSharpCoreTargetsPath`, `CodeAnalysisTargets`, `MSBuildExtensionsPath`, `MSBuildUserExtensionsPath`, `MSBuildProjectExtensionsPath`, `NuGetBuildTasksPackTargets`, `ILLinkTargetsPath`, `ILCompilerTargetsPath`, `MsTestToolsTargets`, `MsAppxPackageTargets`, `ReportingServicesTargets`, `WebPublishProfileFile`, `_BlazorWebAssemblyPropsFile`, `_BlazorWebAssemblyTargetsFile`, `_BlazorWebAssemblyVersionedTargetsFile`, `_ComputeTargetsDir`, `_ContainersTargetsDir`, `_CopyTargetsDir`, `_DotNetCLIToolTargetsDir`, `_PublishProfilesDir`, `_PublishTargetsDir`, `_SourceLinkSdkSubDir`, `_TransformTargetsDir`, `_WebAssemblyPropsFile`, `_WebAssemblyTargetsFile`, `AdditionalImport`, `AfterMicrosoftNetSdkProps`, `AfterTargetFrameworkInferenceTargets`, `AlternateCommonProps`, `BeforeTargetFrameworkInferenceTargets`, `CSharpDesignTimeTargetsPath`, `CustomAfterBlazorWebAssemblySdkTargets`, `CustomAfterDirectoryBuildProps`, `CustomAfterMicrosoftCommonCrossTargetingTargets`, `CustomAfterMicrosoftCommonProps`, `CustomAfterMicrosoftVisualBasicTargets`, `CustomAfterRazorSdkTargets`, `CustomAfterStaticWebAssetsSdkTargets`, `CustomBeforeBlazorWebAssemblySdkTargets`, `CustomBeforeDirectoryBuildProps`, `CustomBeforeMicrosoftCommonCrossTargetingTargets`, `CustomBeforeMicrosoftCommonProps`, `CustomBeforeMicrosoftVisualBasicTargets`, `CustomBeforeRazorSdkTargets`, `CustomBeforeStaticWebAssetsSdkTargets`, `DirectoryBuildPropsPath`, `DirectoryPackagesPropsPath`, `FSharpDesignTimeTargetsPath`, `FSharpOverridesTargetsShim`, `FSharpPropsShim`, `FSharpTargetsShim`, `MicrosoftNETBuildExtensionsTargets`, `MSBuildBinPath`, `MSBuildExtensionsPath32`, `MSBuildProjectFile`, `MSBuildProjectFullPath`, `MSBuildSDKsPath`, `MSBuildThisFile`, `MSBuildThisFileDirectory`, `MSBuildThisFileName`, `MSBuildToolsPath`, `MSBuildToolsVersion`, `MSBuildWebTargetsPath`, `MSBuildWorkerTargetsPath`, `NETCoreSdkBundledCliToolsProps`, `NETCoreSdkBundledMSBuildInformationProps`, `NETCoreSdkBundledVersionsProps`, `NetFrameworkPropsPath`, `NetFrameworkTargetsPath`, `NuGetPropsFile`, `NuGetRestoreTargets`, `PublishProfileName`, `PublishProtocol`, `RazorDesignTimeTargets`, `RazorSdkCurrentVersionProps`, `RazorSdkCurrentVersionTargets`, `StaticWebAssetsSdkCurrentVersionProps`, `StaticWebAssetsSdkCurrentVersionTargets`, `VisualBasicCoreTargetsPath`, `VisualBasicDesignTimeTargetsPath`, `VisualBasicTargetsPath`, `VisualStudioVersion`, `VSTestTargets`, `CompilerResponseFile`, `BaseIntermediateOutputPath`, `_DirectoryBuildPropsFile`, `_DirectoryBuildPropsBasePath`, `_DirectoryBuildTargetsFile`, `_DirectoryBuildTargetsBasePath`, `_DirectoryPackagesPropsFile`, `_DirectoryPackagesPropsBasePath`, `PublishProfile`, `PublishProfileFullPath`, and the `Analyzer`, `EditorConfigFiles`, and `GlobalAnalyzerConfigFiles` items. An item counts whatever its operation, so `<Analyzer Remove="@(Analyzer)" />` is a violation.

The properties from `DirectoryBuildTargetsPath` onward each decide a file that the SDK imports or passes to the compiler, which the project scan would not otherwise read. They come from three sources in SDK 10.0.403:

- **Named in an import**: every `$(Name)` in the `Project` attribute of an `<Import>`, from every file under `sdk/10.0.403` with a `<Project>` element, with comments removed (262 files, 124 with an `<Import>`, 95 names). This includes MSBuild's reserved properties, such as `MSBuildThisFile`, although a project that sets one already fails with MSB4004, so the list stays the plain output of the enumeration and an SDK upgrade can compare it directly. Files outside that directory, such as workload manifests, are not enumerated.
- **Passed as a response file**: every `$(Name)` in a task's `ResponseFiles` attribute in the same files. The only one is `CompilerResponseFile`.
- **Feeding an import**: properties that the definition of an import-named property reads, kept only if setting one redirected an import. Each was set before an explicit SDK import in a scratch copy, and the run looked for a marker property from the redirected file. `BaseIntermediateOutputPath`, the six `_DirectoryBuild*` and `_DirectoryPackages*` properties, `PublishProfile`, and `PublishProfileFullPath` redirected one. `RoslynTargetsPath`, `ProjectExtensionsPathForSpecifiedProject`, `WebPublishMethod`, and `FSharpLanguageTargets` did not, so they are not listed. This reads one level of definitions, not every property that could reach an import.

A `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, or `.editorconfig` below the repository root is a B4 violation. So is a `Directory.Build.rsp`, `MSBuild.rsp`, or `.globalconfig` anywhere, including the root. A root `Directory.Build.targets` is allowed.

MSBuild imports the root `Directory.Build.props`, `Directory.Packages.props`, and `Directory.Build.targets` into every project, so each is read as an import of every project. A reference, package, or `Import` in one of them counts against every project, whatever its condition. The only difference is that the root `Directory.Build.props` and `Directory.Packages.props` set the required settings above, once each and with no condition (B3), and a `GlobalPackageReference` states its version there, since central management has no other place for it.

## Listed suppressions

| ID | Where | Reason |
|---|---|---|
| `NU1900` | `WarningsNotAsErrors` in `Directory.Build.props` | An unreachable audit feed must not fail a build that has no network. CI checks vulnerabilities separately. |

Rule B6 looks for `NoWarn` and `WarningsNotAsErrors` as properties or item metadata, `#pragma warning disable`, `[SuppressMessage]`, `[UnconditionalSuppressMessage]`, `[GeneratedCode]`, `generated_code = true` in `.editorconfig`, and `dotnet_diagnostic` or `dotnet_analyzer_diagnostic` severities of `none`, `silent`, `suggestion`, or `default` in `.editorconfig`.

Rule B5 also rejects a tracked source file under `src/` or `tests/` that is named, ignoring case, `*.g.cs`, `*.g.i.cs`, `*.designer.cs`, `*.generated.cs`, or `TemporaryGeneratedFile_*`, or that has `<auto-generated` or `<autogenerated` in a comment before its first code, because the compiler turns off nullable checks and analyzers for such a file. This check reads the original text, before comments are blanked out.

Source scans (B5, B6, and the attribute form of A16) work on text. A directive matches with leading whitespace and with whitespace after the `#`, and `#nullable disable` matches with a trailing `warnings` or `annotations`. An attribute matches anywhere on a line, including inside a list such as `[Fact, SuppressMessage(...)]`, with or without an `assembly:` or `module:` target, an `Attribute` suffix, or a namespace qualifier. One-line string literals and `//` comments are blanked out before matching, so a test can name these forms in a one-line string.

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

## Known limits

- **Review is requested, not enforced**: Rule C3 and several limits below rely on CODEOWNERS. While rules R5 and R6 in `docs/18-branch-protection.md` stay unapplied under logged exception DEC-20261105-002, an entry requests the owner's review and GitHub does not require it. C3 proves coverage, not that a review happened.
- **Version increments**: C1 proves that this contract and the rule source agree. Nothing proves that a changed rule also incremented the version, which review covers.
- **Build routes a project-file reader cannot judge**: A custom target can run a tool through an `Exec` or `MSBuild` task, and a `Compile` item can link a source file from another project. Review covers these.
- **New SDK routes**: The forbidden settings come from the SDK 10.0.403 enumeration described under Protected settings. A later SDK can add a property or item that these lists do not name, so an SDK upgrade should repeat it.
- **Generated types**: A18 treats a type that a source generator emits like any other, so a generator that emits into an unmapped namespace fails it. The spec that first needs one adds an exclusion through a contract revision.
- **Package sources**: A committed `nuget.config` is not checked. Lock files record package content hashes, so a `--locked-mode` restore rejects a package whose content differs.
- **Private storage models**: No rule keeps them out of a package's public surface, because none exists yet. The first spec that adds one (ISK-0004) owns this.
- **Runtime reflection**: `Assembly.Load` and similar calls cannot be seen by reference checks. `docs/13` keeps unreviewed reflection out of the request path by review.
- **Command-line properties**: A `-p:` argument can override a setting. CI's command lines are protected by the `.github/` CODEOWNERS entry.
- **Empty namespaces**: A namespace with no types cannot be observed in compiled output. Until later specs add types, the module map is checked as data, and rules A17 and A18 are proved on fixtures.
- **Host type-level rules**: No project may reference a host, so hosts are checked through the project graph only.
- **`.editorconfig` weakening**: Rule B6 sees a suppressing severity, but not a deleted severity line or a lowered `csharp_style_*` severity. Those are protected by the `.editorconfig` CODEOWNERS entry (rule C3).
- **Skipped directories**: The scan skips `bin/`, `obj/`, `artifacts/`, `TestResults/`, `.git/`, and `node_modules/`, all of which Git ignores. A project there builds only if `Iskender.sln` lists it or a scanned project references it, and rule A13 rejects both.

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
