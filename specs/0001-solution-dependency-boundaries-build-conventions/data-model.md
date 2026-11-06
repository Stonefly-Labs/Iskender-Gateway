# Data Model: ISK-0001 — Solution Dependency Boundaries & Build Conventions

This spec adds no database, table, or stored runtime data. The entities below are what the architecture tests read and report. They exist only in test code and in the rule source.

## Rule source

`tests/Iskender.Architecture.Tests/module-dependency-rules.json` is the single machine-readable source (FR-002). The test code holds no copy of its contents. The contract, `contracts/module-dependency-rules.md`, has the human-readable form of the same values, and rule C1 compares the two.

```json
{
  "contract": { "path": "0001-solution-dependency-boundaries-build-conventions/contracts/module-dependency-rules.md", "version": 2 },
  "moduleTable": { "file": "03-domain-and-technical-architecture.md", "heading": "Module boundaries" },
  "modules": [
    { "module": "Policy Engine", "assembly": "Iskender.Domain", "namespaceRoot": "Iskender.Domain.Policy" }
  ],
  "portHomes": [
    { "item": "IPolicyHandler", "home": "Iskender.Policy.Sdk", "owner": "ISK-0052" }
  ],
  "projects": [
    { "name": "Iskender.Domain", "role": "embeddable", "references": ["Iskender.Core.Contracts"], "denied": ["aspnetcore"] },
    { "name": "Iskender.Providers.<Provider>", "role": "embeddable", "references": ["Iskender.Core.Contracts"], "denied": ["driver", "data-access"] }
  ],
  "testProjects": { "root": "tests", "role": "test" },
  "requiredReferences": { "Iskender.Gateway.Host": ["Iskender.Hosting", "Iskender.Infrastructure"] },
  "forbiddenReach": [
    { "rule": "A2", "from": ["*"], "to": ["Iskender.Gateway.Host", "Iskender.ControlPlane.Host"] },
    { "rule": "A6", "from": ["Iskender.Gateway.Host"], "to": ["Iskender.Management", "Iskender.ControlPlane.Host"] }
  ],
  "moduleNamespaces": { "shared": "Iskender.Domain.Ports", "allowList": [] },
  "deniedPackages": {
    "aspnetcore": { "prefixes": ["Microsoft.AspNetCore"], "frameworkReferences": ["Microsoft.AspNetCore.App"], "sdks": ["Microsoft.NET.Sdk.Web"] },
    "cloud": { "prefixes": ["Azure", "AWSSDK"], "frameworkReferences": [], "sdks": [] }
  },
  "approvedSdks": [{ "sdk": "Microsoft.NET.Sdk", "usedBy": "Libraries and test projects" }],
  "protectedSettings": {
    "required": [{ "name": "Nullable", "value": "enable", "file": "Directory.Build.props" }],
    "forbidden": ["ImportDirectoryBuildProps"],
    "forbiddenItems": ["EditorConfigFiles"]
  },
  "suppressions": [
    { "id": "NU1900", "where": "Directory.Build.props", "reason": "..." }
  ],
  "rules": [
    { "id": "A5", "statement": "...", "source": "FR-009", "retired": false }
  ]
}
```

The values above illustrate the shape only. The contract holds the full values.

**Validation** (a failure is a C2 violation that names the field, and the run does not continue on partial data):

- The file exists and parses as JSON, and no required field is missing or empty. `allowList` and a category's `frameworkReferences` and `sdks` may be empty.
- `contract.version` is at least 2, and `contract.path` exists under `speckit.specificationsRoot` from `.iskender/grounding.yaml`, so the path to `specs/` is not hard-coded. Messages show the resolved repository-relative path.
- `rules[].id` is unique. A role is one of `public contract`, `embeddable`, `executable`, or `test`.
- Every category in a project's `denied` list exists in `deniedPackages`, and every `forbiddenReach.rule` exists in `rules`.
- A `forbiddenReach` entry is an exact project name or a pattern. `*` means every project, test projects included, and is valid only in `from`.
- `moduleTable.file` resolves under `grounding.root` from `.iskender/grounding.yaml`, so the path to `docs/` is not hard-coded.

## Entities

### Module

A row of the `docs/03` Module boundaries table, read at test time.

| Field | Source | Notes |
|---|---|---|
| `name` | First column of the table | Compared with the map using ordinal, case-sensitive matching |

### Module home

| Field | Notes |
|---|---|
| `module` | Must match exactly one `Module.name` (M1) |
| `assembly` | Project name, or `name, with adapters in pattern` for two modules; must exist in the graph (M2) |
| `namespaceRoot` | Unique across the map. For `Iskender.Domain` modules, a child of `Iskender.Domain` |

### Port home

| Field | Notes |
|---|---|
| `item` | Display name from FR-006, with no signature |
| `home` | An existing project, or a namespace under `Iskender.Domain` that `modules` or `moduleNamespaces.shared` defines (M2) |
| `owner` | The spec or spec family that will freeze the signature |

### Project definition (from the rule source)

| Field | Notes |
|---|---|
| `name` | Exact name, or a pattern with one trailing `<Segment>` |
| `role` | See Project role |
| `references` | The approved direct references. May contain patterns |
| `denied` | Package categories this project may not take (A19). Empty for test projects |

Name resolution: an exact entry wins, and only then is a pattern tried. So `Iskender.Providers.Abstractions` never matches `Iskender.Providers.<Provider>`. A pattern's segment contains no dot, so `Iskender.Providers.Foo.Bar` matches nothing and fails A5.

Test projects have no entry in `projects`. `testProjects.root` is the one directory that gives the test role, and rule C1 compares it with the `Any project under tests/` row of the contract's role table.

### Project (from the repository scan)

| Field | Notes |
|---|---|
| `name` | The file name without its extension, in the casing found on disk. Unique across the repository, ignoring case (A14). Two files with one name stay separate entries, so that A14 can report them |
| `path` | Repository-relative, with forward slashes |
| `isUnderTests` | True only for paths under `tests/`. This alone gives the test role (FR-014) |
| `references` | Every `ProjectReference`, from the project, its imports, and the root build files, under any condition. Resolved by file name, ignoring case and slash direction |
| `packageReferences`, `assemblyReferences`, `frameworkReferences`, `sdk` | Inputs to A13, A19, and B1. `packageReferences` includes a `GlobalPackageReference` from the root `Directory.Packages.props`, whose own `Version` B1 allows |
| `properties`, `items` | Every property and protected item element found, under any condition or inside a target. Inputs to A15, B4, and B6 |
| `lockFilePackages` | Package names from `packages.lock.json`, excluding entries of type `Project`. Input to A19 and B2 |

The root `Directory.Build.props`, `Directory.Packages.props`, and `Directory.Build.targets` are read as imports of every project, because MSBuild imports them into every project. A required setting in the root `Directory.Build.props` or `Directory.Packages.props` is checked by B3 rather than counted as a project setting. A forbidden setting or item there still counts, for B4.

A project file that cannot be parsed produces a C2 violation naming the file, and the scan continues so that the rest are still reported.

### Solution projects

The project paths listed in `Iskender.sln`, read as text with no MSBuild. Each must match a scanned project (A13). A missing or unreadable solution file is a C2 violation.

### Project role

State is fixed per project, with no transitions at run time. A change is a contract revision.

| Role | Packable | Test exemption from A5 and A19 | Examples |
|---|---|---|---|
| `public contract` | Yes, required | No | `Iskender.Core.Contracts` |
| `embeddable` | Yes, required | No | `Iskender.Domain` |
| `executable` | No, required | No | `Iskender.Gateway.Host` |
| `test` | No, required | Yes, never for A2 | Anything under `tests/` |

"Packable" means the project file sets `IsPackable` to `true` without a condition. `Directory.Build.props` sets the default to `false`.

### Rule

| Field | Notes |
|---|---|
| `id` | Stable and never reused. A1 to A19 boundaries, B build conventions, P packaging, M map, C contract |
| `statement` | One sentence, matched by the contract |
| `source` | The FR or SC it comes from |
| `retired` | A retired rule stays listed so its ID cannot be reused |

Every rule that is not retired has at least one test carrying `[ProvesRule("<id>")]`.

### Violation

| Field | Notes |
|---|---|
| `ruleId` | Determines the first sort key, in the rule source's order |
| `source` | A project, file, or module name |
| `target` | A project, setting, package, namespace, or file |
| `detail` | What is wrong, in one clause |
| `contractPath`, `contractVersion` | Taken from the rule source when the message is built. The path is resolved to be repository-relative |

Identity is `ruleId + source + target`. Duplicates collapse, so one bad edge reached by two checks of the same rule is reported once. Sorting by `ruleId` order, then `source`, then `target`, uses ordinal comparison, so Windows and Linux report identically (FR-031).

## Graph

The nodes are `Project` values, and the edges are their `references`. Three operations:

- **Direct check**: A5, using each project's definition.
- **Reach**: A depth-first walk of production and test edges that starts from a project's references and tolerates cycles, used by A2, A3, and A6 to A11. A path is reported as the target. A project never counts as reaching itself, since a cycle is A4's concern.
- **Cycles**: A4, over all nodes, including `Iskender.Hosting` and `Iskender.ControlPlane.Host`.

A reference to a project that is not in the scan is an A13 violation. It is not silently dropped from the graph.

## Refined project graph

Direct references after this spec. Bold marks what changes from the repository at the start of this spec.

| Project | References |
|---|---|
| `Iskender.Core.Contracts` | none |
| `Iskender.Providers.Abstractions` | `Core.Contracts` |
| `Iskender.Policy.Sdk` | `Core.Contracts` |
| `Iskender.Domain` | `Core.Contracts`, **`Providers.Abstractions`**, **`Policy.Sdk`** |
| `Iskender.Infrastructure` | `Domain`, `Core.Contracts` |
| `Iskender.Management` | `Core.Contracts`, **`Domain`** |
| **`Iskender.Hosting`** (new) | **`Domain`**, **`Core.Contracts`** |
| `Iskender.Gateway.Host` | **`Hosting`**, `Domain`, `Infrastructure`, `Providers.Abstractions`, `Policy.Sdk`, `Core.Contracts`. **Loses `Management`** |
| **`Iskender.ControlPlane.Host`** (new) | **`Management`**, **`Domain`**, **`Infrastructure`**, **`Core.Contracts`** |
